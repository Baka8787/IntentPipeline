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
            Retreat
        }

        [SerializeField] private Transform target;
        [SerializeField, Range(0f, 1f)] private float desiredSpeedNormalized = 1f;

        [Header("Melee Engagement Distance")]
        [SerializeField, Min(0f)] private float minimumEngagementDistance = 1.25f;
        [SerializeField, Min(0f)] private float maximumEngagementDistance = 2f;
        [SerializeField, Min(0f)] private float distanceHysteresis = 0.15f;

        [Header("Hold Strafe")]
        [SerializeField, Range(0f, 1f)] private float holdStrafeSpeedNormalized = 0.35f;
        [SerializeField, Min(0f)] private float strafeDirectionFlipInterval = 2.5f;

        private NavMeshAgent _agent;
        private TemporaryGameplayEffectState _effectState;
        private EngagementMovement _engagementMovement;
        private int _strafeDirectionSign = 1;
        private float _nextStrafeDirectionFlipTime;

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
            if (_agent == null || !_agent.isOnNavMesh || target == null)
            {
                _engagementMovement = EngagementMovement.Hold;
                return;
            }

            // Agent 只維護 path query 的內部位置；角色 Transform 只會被 MotionDriver 搬動。
            _agent.nextPosition = transform.position;
            _agent.SetDestination(target.position);

            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            _engagementMovement = ResolveEngagementMovement(
                _engagementMovement,
                Mathf.Sqrt(toTarget.sqrMagnitude),
                minimumEngagementDistance,
                maximumEngagementDistance,
                distanceHysteresis);

            Vector3 worldDirection;
            if (_engagementMovement == EngagementMovement.Hold)
            {
                UpdateStrafeDirection(Time.time);
                worldDirection = ResolveHoldStrafeDirection(toTarget, _strafeDirectionSign);
            }
            else if (_engagementMovement == EngagementMovement.Retreat)
            {
                // 後退仍只是 producer 產生的方向意圖；真正位移繼續由 MotionDriver 獨佔。
                // 近距離時 path 可能因 stoppingDistance 而沒有有效 steeringTarget，因此以目標反方向
                // 作為局部脫離方向，不能讓 NavMeshAgent 自己搬 Transform 來規避這個情況。
                worldDirection = -toTarget;
            }
            else
            {
                if (_agent.pathPending || !_agent.hasPath ||
                    _agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    return;
                }

                worldDirection = _agent.steeringTarget - transform.position;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f) return;
            worldDirection.Normalize();

            data.MovementIntent.DesiredDirection = worldDirection;
            data.MovementIntent.DesiredSpeedNormalized = _engagementMovement == EngagementMovement.Hold
                ? ResolveDesiredSpeedNormalized(holdStrafeSpeedNormalized, Time.time)
                : ResolveDesiredSpeedNormalized(Time.time);
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
            return EngagementMovement.Hold;
        }

        /// <summary>
        /// 把目標方向水平化後繞 Y 軸旋轉 90 度；正負號只選順／逆時針，不改變單位長度。
        /// 退化的水平向量沒有可靠切線，直接回傳零向量，避免正規化產生 NaN。
        /// </summary>
        internal static Vector3 ResolveHoldStrafeDirection(Vector3 toTarget, int directionSign)
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

            // 內圈：比它更近就該後退。外圈：比它更遠就該接近。兩圈之間＝Hold。
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
            if (_engagementMovement == EngagementMovement.Hold)
            {
                Vector3 strafeDirection = ResolveHoldStrafeDirection(toTarget, _strafeDirectionSign);
                UnityEditor.Handles.DrawLine(origin, origin + strafeDirection * 1.5f);
            }
            UnityEditor.Handles.Label(labelAnchor,
                $"AI move: {_engagementMovement}\n" +
                $"distance {distance:0.00}  band [{minimumEngagementDistance:0.00}, {maximumEngagementDistance:0.00}]\n" +
                $"strafe {(_strafeDirectionSign > 0 ? "CW" : "CCW")}");
        }

        private static Color EngagementGizmoColor(EngagementMovement movement)
        {
            switch (movement)
            {
                case EngagementMovement.Approach: return new Color(1f, 0.85f, 0.2f); // 黃＝往前
                case EngagementMovement.Retreat: return new Color(1f, 0.4f, 0.3f);   // 紅＝後退
                default: return new Color(0.4f, 1f, 0.5f);                            // 綠＝側移
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
