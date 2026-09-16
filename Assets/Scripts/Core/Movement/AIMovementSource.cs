using UnityEngine;
using UnityEngine.AI;
using Project.Core.Blackboard;
using Project.Core.Effects;

namespace Project.Core.Movement
{
    /// <summary>
    /// 以 NavMesh 查詢下一段路徑方向，再把結果寫成模型無關的 MovementIntent。
    /// NavMeshAgent 不擁有 Transform；實際位移仍由 LocomotionModel → MotionDriver 結算。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AIMovementSource : MonoBehaviour, IMovementIntentSource
    {
        internal enum EngagementMovement
        {
            Hold,
            Approach,
            Strafe,
            Retreat,

            /// <summary>
            /// 🆕（2026-09-15）**離開 NavMesh 後的自救**。
            ///
            /// <para><b>為什麼需要一個獨立模式</b></para>
            /// 舊版離網時直接 <c>Hold; return;</c>，而全 repo **沒有任何一行**會把 agent 放回可導航區
            /// （<c>isOnNavMesh</c> 全專案只有這個檔案讀）⇒ <c>Hold</c> 是一個**沒有出口的終態**，
            /// 敵人永久靜止（2026-09-15 錄影：連續 11 秒零位移，期間玩家跑遠也不追）。
            ///
            /// 這個模式**刻意不推進 <see cref="ResolveEngagementMovement"/> 的遲滯狀態機**——
            /// 維持原註解的意圖（離網期間的距離變化不得污染遲滯記憶），
            /// 只回答一個問題：**往哪走才回得去。**
            /// </summary>
            Recover
        }

        [SerializeField] private Transform target;
        [SerializeField, Range(0f, 1f)] private float desiredSpeedNormalized = 1f;

        [Header("Combat Engagement")]
        [SerializeField, Min(0f)] private float aggroEnterRadius = 8f;
        [SerializeField, Min(0f)] private float aggroLeaveRadius = 12f;

        [Header("Melee Engagement Distance")]
        [SerializeField, Min(0f)] private float minimumEngagementDistance = 1.25f;
        [SerializeField, Min(0f)] private float maximumEngagementDistance = 2f;
        [SerializeField, Min(0f)] private float distanceHysteresis = 0.15f;

        [Header("Strafe")]
        [SerializeField, Range(0f, 1f)] private float holdStrafeSpeedNormalized = 0.35f;
        [SerializeField, Min(0f)] private float strafeDirectionFlipInterval = 2.5f;

        // 🆕（2026-09-15）側移的**可走性前視距離**。側移方向是純幾何切線，本身不認識環境；
        // 沒有這一步，敵人會把自己側移進牆縫／走出 NavMesh，然後永久卡死。
        // 取值取「一步的量級」即可——太短擋不住，太長會讓敵人在合法的窄通道裡過度保守。
        [SerializeField, Min(0.01f)] private float strafeWalkableProbeDistance = 0.6f;

        [Header("Off-NavMesh Recovery")]
        // 從目前位置往外找最近可導航點的取樣半徑。只在**已經離網**時才會用到，不在熱路徑上。
        [SerializeField, Min(0.01f)] private float offMeshRecoverSampleRadius = 3f;
        [SerializeField, Range(0f, 1f)] private float recoverSpeedNormalized = 0.5f;

        private NavMeshAgent _agent;
        private TemporaryGameplayEffectState _effectState;
        private EngagementMovement _engagementMovement;
        private int _strafeDirectionSign = 1;
        private float _nextStrafeDirectionFlipTime;

        // 純診斷用：兩側都不可走而停步。**不參與任何決策**（gizmo 只讀不寫，A2 守）。
        private bool _strafeBlocked;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            ResolveEffectState();
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _strafeDirectionSign = Random.value < 0.5f ? -1 : 1;
            ScheduleNextStrafeDirectionFlip(Time.time);
        }

        /// <summary>
        /// 取得同物件上的效果持有元件。**與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——
        /// EditMode 不在 Play mode，`AddComponent` **不會呼叫 `Awake`** ⇒ `_effectState` 恆為 null
        /// ⇒ 倍率恆為 1，Slow 相關斷言會全部假性失敗（2026-09-04 首跑實際踩到）。
        ///
        /// ⚠️ 這不是為測試而改行為：production 走 <see cref="Awake"/>，時機與結果完全不變。
        /// 比照本專案既有慣例（<c>TemporaryGameplayEffectState</c> 的 <c>ApplySlowAt</c> 等
        /// <c>internal</c> 顯式時間版本），把「生命週期外也能建立的前置」開成 <c>internal</c>。
        /// </summary>
        internal void ResolveEffectState()
        {
            _effectState = GetComponent<TemporaryGameplayEffectState>();
        }

        public void ProduceIntent(ref InputData input, PlayerRuntimeData data)
        {
            if (data == null) return;

            data.MovementIntent = default;

            // 🆕（2026-09-15）**屍體不產生移動意圖。**
            // `DeathState` 是吸收態、確實不會走路，所以這條在今天看不出差別——但決策層繼續
            // 每帧解出 Approach／Strafe／NavMesh 查詢是**沒有讀者的計算**，而且會讓 gizmo 報告
            // 一個屍體「正在側移」。與 `CharacterFacingSource` 的 `DeathSuppressed`
            // （2026-09-14）同一個模式：**死亡由各自的消費者明確收手**，不靠下游剛好擋住。
            if (data.Survivability.IsDead)
            {
                _engagementMovement = EngagementMovement.Hold;
                _strafeBlocked = false;
                return;
            }

            if (target == null)
            {
                data.CombatContext = default;
                _engagementMovement = EngagementMovement.Hold;
                return;
            }

            Vector3 targetPosition = target.position;
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            float distance = Mathf.Sqrt(toTarget.sqrMagnitude);

            // ── 層 1：接戰語境（**與 NavMesh 無關**）────────────────────────────────
            // CombatContext 回答的是「還在不在跟這個目標交戰」——那是**目標關係**的性質，
            // 不是導航的性質。agent 掉出 NavMesh 不代表敵人脫離戰鬥，所以這一層必須在
            // NavMesh 守衛**之前**結算。
            // 敵人的移動與戰鬥語境共用同一個 target 真相；另建 context source 只會複製
            // target reference ⇒ 目標會出現第二份真相（ADR-007 D3 補充條款明文禁止
            // 「任何其他系統自建 isInCombat 旗標或第二份 target 清單」）。
            // 這裡整體覆寫 value struct，不讓下游回頭讀 Transform。
            bool inCombat = ResolveCombatEngagement(
                data.CombatContext.InCombat,
                distance,
                aggroEnterRadius,
                aggroLeaveRadius);
            data.CombatContext = new CombatContextData
            {
                InCombat = inCombat,
                HasTarget = inCombat,
                TargetPosition = inCombat ? targetPosition : Vector3.zero,
            };

            // 尚未進入交戰語境時必須是 Idle。early-return 是權威邊界：不能讓距離帶
            // 繼續把未交戰的敵人解析成 Approach／Strafe／Retreat。
            if (!inCombat)
            {
                _engagementMovement = EngagementMovement.Hold;
                return;
            }

            // ── 層 2：**可導航前提下**的移動模式 ────────────────────────────────────
            // Approach／Retreat 回答的是「要怎麼走過去」；沒有可用路徑時那個決定沒有意義。
            // ⛔ 離網期間**不得**讓遲滯狀態機繼續推進——否則回到 NavMesh 的那一幀會拿到一個
            //    沒有任何一幀真正推導過的模式（遲滯記憶被離網期間的距離變化污染）。
            //
            // 🆕 2026-09-15：**但也不得就此停住。** 舊版在這裡 `Hold; return;`，
            //    而沒有任何系統會把離網的 agent 放回去 ⇒ 敵人永久靜止。
            //    改為進入 <see cref="EngagementMovement.Recover"/>：遲滯狀態機依舊凍結，
            //    但輸出一個「走回可導航區」的意圖，讓 MotionDriver 自己走回去。
            // ⛔ 刻意**不用** `NavMeshAgent.Warp()`——它會直接設 `transform.position`，
            //    違反本專案「位移由 MotionDriver 獨佔」的不變量。自救也必須走同一條輸出。
            if (_agent == null || !_agent.isOnNavMesh)
            {
                _engagementMovement = EngagementMovement.Recover;
                _strafeBlocked = false;

                bool hasSample = NavMesh.SamplePosition(
                    transform.position, out NavMeshHit sample, offMeshRecoverSampleRadius, NavMesh.AllAreas);

                if (!TryResolveRecoveryDirection(
                        transform.position,
                        hasSample,
                        hasSample ? sample.position : Vector3.zero,
                        toTarget,
                        out Vector3 recoveryDirection))
                {
                    return;
                }

                data.MovementIntent.DesiredDirection = recoveryDirection;
                data.MovementIntent.DesiredSpeedNormalized =
                    ResolveDesiredSpeedNormalized(recoverSpeedNormalized, Time.time);
                return;
            }

            _engagementMovement = ResolveEngagementMovement(
                _engagementMovement,
                distance,
                minimumEngagementDistance,
                maximumEngagementDistance,
                distanceHysteresis);

            // Agent 只維護 path query 的內部位置；角色 Transform 只會被 MotionDriver 搬動。
            _agent.nextPosition = transform.position;
            _agent.SetDestination(targetPosition);

            // 只有側移分支會把它設為 true；其餘模式必須清掉，否則 gizmo 會留著上一次的「被擋」狀態。
            _strafeBlocked = false;

            Vector3 worldDirection;
            if (_engagementMovement == EngagementMovement.Strafe)
            {
                UpdateStrafeDirection(Time.time);

                // 🆕（2026-09-15）**側移必須先問環境。**
                // 切線方向是純幾何的（toTarget 繞 Y 轉 90°），本身完全不認識牆、窄縫或 NavMesh 邊界。
                // 而側移是**穩態**模式（交戰帶 [min, max] 很窄，打鬥期間幾乎全程待在裡面），
                // Approach 只是過渡 ⇒ 舊版等於「唯一諮詢 NavMesh 的是過渡模式，穩態模式不看環境」。
                // 打得夠久就必然把自己側移出網 ⇒ 見 EngagementMovement.Recover 的說明。
                int preferredSign = _strafeDirectionSign;
                bool preferredWalkable = IsStrafeStepWalkable(toTarget, preferredSign);
                bool oppositeWalkable = !preferredWalkable && IsStrafeStepWalkable(toTarget, -preferredSign);

                _strafeBlocked = !preferredWalkable && !oppositeWalkable;

                if (!TryResolveStrafeDirection(
                        toTarget, preferredSign, preferredWalkable, oppositeWalkable,
                        out worldDirection, out int resolvedSign))
                {
                    // 兩側都走不了：站定，**但不改 `_engagementMovement`**。
                    // 下一幀 ResolveEngagementMovement 仍會從同一個距離推導出 Strafe 並重試，
                    // 玩家一動或牆一讓開就自然恢復 ⇒ 這是暫時停步，不是終態。
                    return;
                }

                if (resolvedSign != preferredSign)
                {
                    // 撞牆換邊也要重置計時，否則剛翻完就被 flip interval 立刻翻回被擋的那側。
                    _strafeDirectionSign = resolvedSign;
                    ScheduleNextStrafeDirectionFlip(Time.time);
                }
            }
            else if (_engagementMovement == EngagementMovement.Retreat)
            {
                // 後退仍只是 producer 產生的方向意圖；真正位移繼續由 MotionDriver 獨佔。
                // 近距離時 path 可能因 stoppingDistance 而沒有有效 steeringTarget，因此以目標反方向
                // 作為局部脫離方向，不能讓 NavMeshAgent 自己搬 Transform 來規避這個情況。
                worldDirection = -toTarget;
            }
            else if (_engagementMovement == EngagementMovement.Approach)
            {
                // `pathPending` 是**短暫**的（路徑非同步計算，通常 1～2 帧）⇒ 等它算完是對的，停一兩帧看不出來。
                // 🆕 2026-09-15：`!hasPath`／`PathInvalid` 則**可能持續存在**（目標不可達、
                //    目標站在 NavMesh 之外、中途被 obstacle 切斷）。舊版對這兩者也一律空手 return，
                //    形狀與離網守衛完全相同——**沒有 fallback 的 early-return ＝ 靜止的敵人**。
                //    改為退化成「直接朝目標壓過去」：位移仍由 MotionDriver 結算，撞到牆會自然貼牆滑行。
                // ⚠️ Trade-off：目標真的不可達時，敵人會變成「持續推牆」而不是「站著不動」。
                //    兩者都不理想，但推牆是**可見的嘗試**，站著不動看起來就是壞掉——
                //    而且推牆會讓 FSM／動畫維持在移動語境，不會留下假的 Idle。
                if (_agent.pathPending) return;

                worldDirection = !_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid
                    ? toTarget
                    : _agent.steeringTarget - transform.position;
            }
            else
            {
                // Hold 是真正的零移動 fallback，不再同時暗指「側移」。
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return;
            worldDirection.Normalize();

            data.MovementIntent.DesiredDirection = worldDirection;
            data.MovementIntent.DesiredSpeedNormalized = _engagementMovement == EngagementMovement.Strafe
                ? ResolveDesiredSpeedNormalized(holdStrafeSpeedNormalized, Time.time)
                : ResolveDesiredSpeedNormalized(Time.time);
        }

        /// <summary>
        /// （純函數）接戰**語境**的黏性進出判定。沿用 <c>PlayerCombatContextSource</c> 的
        /// enter／leave 半徑形狀：**尚未進場時用 enter、已進場時用較大的 leave**，
        /// 讓目標在邊界附近來回時不會逐幀翻轉 facing 來源。
        ///
        /// enter／leave 使用獨立的 aggro 半徑，與近戰帶的
        /// <c>minimumEngagementDistance</c>／<c>maximumEngagementDistance</c> 分離。
        /// 退化輸入以 enter 為下限夾住 leave，避免兩個門檻反向。
        ///
        /// ⚠️ 與 <see cref="ResolveEngagementMovement"/> 的分工：本函數只回答「還在不在交戰」，
        /// 不回答「要怎麼移動」。後者需要可用路徑，前者不需要。
        /// </summary>
        internal static bool ResolveCombatEngagement(
            bool wasInCombat,
            float distance,
            float enterRadius,
            float leaveRadius)
        {
            float effectiveEnterRadius = Mathf.Max(0f, enterRadius);
            float effectiveLeaveRadius = Mathf.Max(leaveRadius, effectiveEnterRadius);
            return distance <= (wasInCombat ? effectiveLeaveRadius : effectiveEnterRadius);
        }

        /// <summary>
        /// 交戰距離是 producer 私有的 intent 狀態，不回讀 FSM。外側門檻決定開始移動，內側門檻
        /// 決定何時切入側移；兩者間的 hysteresis 讓微小距離誤差不會逐幀切換前進／側移或後退／側移。
        /// </summary>
        internal static EngagementMovement ResolveEngagementMovement(
            EngagementMovement current,
            float distance,
            float minimumDistance,
            float maximumDistance,
            float hysteresis)
        {
            float minimum = Mathf.Max(0f, minimumDistance);
            float maximum = Mathf.Max(minimum, maximumDistance);
            float deadZone = Mathf.Min(Mathf.Max(0f, hysteresis), (maximum - minimum) * 0.5f);

            if (current == EngagementMovement.Retreat && distance < minimum + deadZone)
                return EngagementMovement.Retreat;
            if (current == EngagementMovement.Approach && distance > maximum - deadZone)
                return EngagementMovement.Approach;

            if (distance < minimum) return EngagementMovement.Retreat;
            if (distance > maximum) return EngagementMovement.Approach;
            return EngagementMovement.Strafe;
        }

        /// <summary>
        /// 把目標方向水平化後繞 Y 軸旋轉 90 度；正負號只選順／逆時針，不改變單位長度。
        /// 退化的水平向量沒有可靠切線，直接回傳零向量，避免正規化產生 NaN。
        /// </summary>
        internal static Vector3 ResolveStrafeDirection(Vector3 toTarget, int directionSign)
        {
            float horizontalSqrMagnitude = toTarget.x * toTarget.x + toTarget.z * toTarget.z;
            if (horizontalSqrMagnitude <= 0.0001f) return Vector3.zero;

            float sign = directionSign < 0 ? -1f : 1f;
            float signedInverseMagnitude = sign / Mathf.Sqrt(horizontalSqrMagnitude);
            return new Vector3(
                toTarget.z * signedInverseMagnitude,
                0f,
                -toTarget.x * signedInverseMagnitude);
        }

        /// <summary>
        /// 🆕（2026-09-15，純函數）**在已知兩側可走性的前提下**選出側移方向。
        ///
        /// 把「要不要換邊」與「怎麼問 NavMesh」拆開的理由很實際：NavMesh 查詢在 EditMode 不存在，
        /// 呼叫端負責問、本函數負責決定 ⇒ **決策邏輯可以在沒有 NavMesh 的情況下完整測試**，
        /// 與本檔既有的 <see cref="ResolveEngagementMovement"/>／<see cref="ResolveStrafeDirection"/>
        /// 同一個模式（純靜態、無副作用、不碰 Unity 場景）。
        /// </summary>
        /// <returns>兩側皆不可走時回 false，呼叫端應停步且**不要**改變交戰模式。</returns>
        internal static bool TryResolveStrafeDirection(
            Vector3 toTarget,
            int preferredSign,
            bool preferredWalkable,
            bool oppositeWalkable,
            out Vector3 direction,
            out int resolvedSign)
        {
            resolvedSign = preferredSign;
            direction = Vector3.zero;

            if (preferredWalkable)
            {
                direction = ResolveStrafeDirection(toTarget, preferredSign);
            }
            else if (oppositeWalkable)
            {
                resolvedSign = -preferredSign;
                direction = ResolveStrafeDirection(toTarget, resolvedSign);
            }
            else
            {
                return false;
            }

            // 退化的水平向量（角色與目標重疊）沒有可靠切線——ResolveStrafeDirection 已回零向量，
            // 這裡把它轉成明確的「解不出方向」，避免呼叫端正規化零向量。
            return direction.sqrMagnitude > 0.0001f;
        }

        /// <summary>
        /// 🆕（2026-09-15，純函數）離網自救的方向決策。
        ///
        /// 優先走向最近的可導航取樣點；取樣失敗（離得太遠、周圍真的沒有 NavMesh）時，
        /// **最後手段是朝目標**——目標是玩家，依定義站在可走的地方，朝它走至少方向是對的。
        /// ⛔ 不回退成「不動」：那正是這次要修掉的終態。
        /// </summary>
        internal static bool TryResolveRecoveryDirection(
            Vector3 selfPosition,
            bool hasNavMeshSample,
            Vector3 sampledPosition,
            Vector3 toTarget,
            out Vector3 direction)
        {
            if (hasNavMeshSample)
            {
                direction = sampledPosition - selfPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f) return true;
            }

            direction = toTarget;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f;
        }

        /// <summary>
        /// 側移一步之後**還在不在可導航區**。
        ///
        /// 用 <see cref="NavMesh.Raycast"/> 而不是 <see cref="NavMesh.SamplePosition"/>：
        /// 前者回答的正是「從 A 直線走到 B 會不會穿出 NavMesh 邊界」，零配置，而且是這裡真正的問題；
        /// 後者只回答「B 附近有沒有 NavMesh」，跨過一道牆縫的另一側也會通過。
        /// </summary>
        private bool IsStrafeStepWalkable(Vector3 toTarget, int directionSign)
        {
            Vector3 step = ResolveStrafeDirection(toTarget, directionSign);
            if (step.sqrMagnitude <= 0.0001f) return false;

            Vector3 destination = transform.position + step * strafeWalkableProbeDistance;
            return !NavMesh.Raycast(transform.position, destination, out _, NavMesh.AllAreas);
        }

        private void UpdateStrafeDirection(float currentTime)
        {
            if (strafeDirectionFlipInterval <= 0f || currentTime < _nextStrafeDirectionFlipTime)
                return;

            _strafeDirectionSign = -_strafeDirectionSign;
            ScheduleNextStrafeDirectionFlip(currentTime);
        }

        private void ScheduleNextStrafeDirectionFlip(float currentTime)
        {
            const float MinimumIntervalScale = 0.75f;
            const float MaximumIntervalScale = 1.25f;
            _nextStrafeDirectionFlipTime = currentTime + strafeDirectionFlipInterval *
                Random.Range(MinimumIntervalScale, MaximumIntervalScale);
        }

#if UNITY_EDITOR
        /// <summary>
        /// **決策層的 Scene 視窗可視化**（2026-09-06）。
        ///
        /// 目的不是漂亮的 debug UI，而是回答一個具體問題：敵人行為怪的時候，
        /// **問題在 decision、movement execution 還是 action pipeline？**
        /// 這裡畫的是 decision 層看到的世界——接戰帶、當前模式、到目標的水平距離。
        /// 若距離與模式一致但角色仍然亂走，問題就不在這一層。
        ///
        /// ⚠️ 全段包在 `#if UNITY_EDITOR` 內（A2 守），且**只讀不寫**——
        /// gizmo 不得成為第二個決策來源。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;

            // 內圈：比它更近就該後退。外圈：比它更遠就該接近。兩圈之間＝Strafe。
            UnityEditor.Handles.color = new Color(1f, 0.4f, 0.3f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, Mathf.Max(0f, minimumEngagementDistance));
            UnityEditor.Handles.color = new Color(0.35f, 0.8f, 1f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, Mathf.Max(0f, maximumEngagementDistance));

            Vector3 labelAnchor = origin + Vector3.up * 2.4f;

            if (target == null)
            {
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.Label(labelAnchor, "AI move: target = <none> ⇒ 永遠不動");
                return;
            }

            Vector3 toTarget = target.position - origin;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            UnityEditor.Handles.color = EngagementGizmoColor(_engagementMovement);
            UnityEditor.Handles.DrawLine(origin, origin + toTarget);
            if (_engagementMovement == EngagementMovement.Strafe)
            {
                Vector3 strafeDirection = ResolveStrafeDirection(toTarget, _strafeDirectionSign);
                UnityEditor.Handles.DrawLine(origin, origin + strafeDirection * 1.5f);
            }
            UnityEditor.Handles.Label(labelAnchor,
                $"AI move: {_engagementMovement}{(_strafeBlocked ? "  ⚠ 兩側皆不可走 ⇒ 停步" : string.Empty)}\n" +
                $"distance {distance:0.00}  band [{minimumEngagementDistance:0.00}, {maximumEngagementDistance:0.00}]\n" +
                $"strafe {(_strafeDirectionSign > 0 ? "CW" : "CCW")}  onNavMesh {(_agent != null && _agent.isOnNavMesh ? "yes" : "NO")}");
        }

        private static Color EngagementGizmoColor(EngagementMovement movement)
        {
            switch (movement)
            {
                case EngagementMovement.Approach: return new Color(1f, 0.85f, 0.2f); // 黃＝往前
                case EngagementMovement.Retreat: return new Color(1f, 0.4f, 0.3f);   // 紅＝後退
                case EngagementMovement.Strafe: return new Color(0.4f, 1f, 0.5f);   // 綠＝側移
                case EngagementMovement.Recover: return new Color(1f, 0.2f, 0.9f);  // 洋紅＝離網自救（看到它就是出過網）
                default: return new Color(0.55f, 0.55f, 0.6f);                       // 灰＝Hold
            }
        }
#endif

        /// <summary>
        /// Slow 在 intent producer 的最後一道輸出上生效；黑板仍只由 AIMovementSource 寫入，
        /// 下游速度平滑、gait、停步、Foot IK 與音效都只會看到自然縮小後的同一份意圖。
        /// </summary>
        internal float ResolveDesiredSpeedNormalized(float currentTime)
        {
            return ResolveDesiredSpeedNormalized(desiredSpeedNormalized, currentTime);
        }

        private float ResolveDesiredSpeedNormalized(float baseSpeedNormalized, float currentTime)
        {
            float multiplier = _effectState != null
                ? _effectState.GetMovementSpeedMultiplierAt(currentTime)
                : 1f;
            return Mathf.Clamp01(baseSpeedNormalized) * multiplier;
        }
    }
}
