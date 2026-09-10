using System;
using UnityEngine;
using Project.Core.Actions;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine.Actions;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 所有同構 Action 共用的一顆 FSM state。Definition 決定 phase、動畫鍵與 release 點；
    /// 本類別不播放動畫、不建立 GameObject，位移只經 MotionDriver。
    ///
    /// 🆕（ADR-005 D1）**一顆 state 承載多份 Definition**，以 <see cref="ActionSlot"/> 為身分索引。
    /// Definition 不再於 Initialize 綁死，而是每次進入時依 request 的 slot 現查——
    /// 這是 FU-2 的解，且**沒有新增第二個 gate 權威**：能不能出手仍然只由本類別的
    /// <see cref="CanEnter"/> 回答（ADR-004 D2）。
    /// </summary>
    public class ActionState : BaseState
    {
        /// <summary>
        /// per-slot 冷卻的陣列長度＝<see cref="ActionSlot"/> 最大成員 +1，查表 O(1) 且執行期零配置。
        ///
        /// ⚠️ **刻意不寫死 <c>(int)ActionSlot.Reaction + 1</c>**：保留段（100 起）將來若再加成員，
        /// 寫死會讓陣列悄悄長度不足並在 <c>OnExit</c> 越界。改為由 enum 自己算，加成員不必記得改這裡。
        ///
        /// ⚖️ **稀疏是設計，不是浪費**：<see cref="ActionSlot"/> 的數值就是身分，且**刻意不要求連續**
        /// （玩家段 1–3、保留段 100 起，中間的空隙讓兩段各自獨立成長）。以身分當索引 ⇒ 表必然稀疏。
        /// 目前 101 格只用到 5 格 ＝ 404 B，**每個角色一次**的配置、不在熱路徑。
        /// ⛔ 不要為了「填滿號碼」去改 enum 數值——那是在改身分，會讓既有資產指向別的 slot。
        /// ⛔ 也不要為了省這 404 B 加一層 slot→密集索引的對照（2026-09-02 已裁決不做）——
        /// 那等於多一把內部的鍵，與 ADR-005 D1「不得有第二把鍵」的精神相悖，而省下的是零頭。
        /// </summary>
        internal static readonly int SlotCount = ComputeSlotCount();

        private const float DirectionSqrEpsilon = 0.000001f;

        /// <summary>
        /// <see cref="ActionTargetingPolicy.CameraConeSoftTarget"/> 的角錐半角（度）。
        ///
        /// ⛔ **刻意是一顆全專案常數，不是 per-Action 旋鈕**（`docs/11` §8.3 明文禁止）——
        /// 「吸不吸敵」是 Definition 的選擇，「吸多寬」不是；後者一旦下放，
        /// 每把武器的吸敵範圍就會各自漂移，玩家再也無法用一句話描述規則。
        /// 30° ＝ 敵人大致在畫面中央三分之一時才修正；要調就調這一個數字。
        /// </summary>
        private const float SoftTargetConeHalfAngleDegrees = 30f;

        private static int ComputeSlotCount()
        {
            var values = (ActionSlot[])System.Enum.GetValues(typeof(ActionSlot));
            int max = 0;
            for (int i = 0; i < values.Length; i++)
            {
                int value = (int)values[i];
                if (value > max) max = value;
            }
            return max + 1;
        }

        private readonly ActionRequestTarget _externalRequestTarget;
        private readonly IActionLifecycleSink[] _lifecycleSinks;

        // 🆕（ADR-005）冷卻改為 per-slot。**仍住在 ActionState 內部**（ADR-004 D2）——
        // 搬到 Runner／Config／HUD 都會讓「能不能出手」有第二個回答者。
        private readonly float[] _cooldownEndTime = new float[SlotCount];
#if UNITY_EDITOR
        // 缺 Definition 是資產接線錯誤，不改變 gate 的嚴格行為；每個 slot 只警告一次，避免 CanEnter
        // 每幀輪詢時持續配置訊息字串並淹沒 Console。
        private readonly bool[] _missingDefinitionWarnings = new bool[SlotCount];
#endif

        private ActionDefinitionSO _definition;
        private ActionSlot _activeSlot;
        private ActionPhase _phase;
        private ActionPhaseEntry _currentEntry;
        private float _phaseElapsed;
        private string _currentAnimationKey;
        private bool _releaseEmittedThisExecution;

        // 🆕 連段（docs/11 §4.3）。第 1 段 ＝ Phases 的 Start，因此 _chainIndex 也是
        // 「已經播完幾個 ChainSegments」的計數；0 表示還在第 1 段。
        // ⚠️ 連段期間 _phase 一直維持 Start——連段是「同一個 Start 換素材重播」，不是新 phase。
        //    這讓 CanTransitionAway／CancelMoveIntentThreshold／Loop 的既有語意一字不必改。
        private int _chainIndex;
        private bool _chainQueued;

        // 🆕（ADR-007 D4／D5）每個段落邊界取得一次的方向承諾。
        // ActionState 是承諾與 release 時點的共同擁有者；facing 與世界效果只讀這一份快照。
        private ActionReleaseContext _releaseContext;

        // Action-time soft auto-target 的無狀態查詢／facing 轉送 seam。
        // ⚠️ 可以是 null——敵人身上沒有相機式 aim source，
        //    承諾因此安靜退化為 HasAim=false，lifecycle 仍照常完成。
        private readonly IAimSource _aimSource;

        public ActionState(
            ActionRequestTarget externalRequestTarget = null,
            IActionLifecycleSink[] lifecycleSinks = null,
            IAimSource aimSource = null)
        {
            _externalRequestTarget = externalRequestTarget;
            _lifecycleSinks = lifecycleSinks ?? Array.Empty<IActionLifecycleSink>();
            _aimSource = aimSource;
        }

        /// <summary>
        /// 「這個 **slot** 會不會取得方向承諾」——**slot 層級的全域閘門**（`docs/11` §8.3）。
        ///
        /// ⚠️ 這裡回答的是**會不會取得**，不是**朝哪取得**。後者自 2026-09-08 起由
        /// <see cref="ActionTargetingPolicy"/> 這個 authored 欄位回答（該裁決同時修訂了 ADR-007 D3，見其 §11）。
        /// 兩者刻意分開：**「受擊不轉向」是 slot 的性質，不該讓每份 Definition 各自宣告一次**——
        /// 那才會回到「一致性交給填表的人」的老問題。
        ///
        /// <see cref="ActionSlot.Reaction"/> 例外：受擊不是出手，被打的人不該自己轉去面對攻擊者。
        /// （此例外**優先於** policy：Reaction 的 Definition 就算標了 soft-target 也不取得承諾。）
        /// </summary>
        internal static bool ShouldFaceTargetOnEnter(ActionSlot slot)
            => slot != ActionSlot.None && slot != ActionSlot.Reaction;

        public override StateType Type => StateType.Action;
        public override string AnimationKey => _currentAnimationKey;
        public override bool CanTransitionAway => _phase == ActionPhase.None;
        public ActionPhase CurrentPhase => _phase;

        /// <summary>當前執行中的 Action 身分；閒置時為 <see cref="ActionSlot.None"/>。</summary>
        public ActionSlot ActiveSlot => _activeSlot;

        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);

#if UNITY_EDITOR
            if (Application.isPlaying && (config == null || config.ActionDefinitionCount == 0))
            {
                Debug.LogWarning("[ActionState] StateMachineConfig 未綁定任何 ActionDefinitionSO；Action 將拒絕進入。");
            }
#endif
        }

        public override bool CanEnter(PlayerRuntimeData data)
        {
            if (data == null || Config == null) return false;
            return TryResolveRequest(data, out _, out _);
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
            _releaseEmittedThisExecution = false;
            _chainIndex = 0;
            _chainQueued = false;

            if (!TryResolveRequest(data, out ActionSlot slot, out ActionDefinitionSO definition))
            {
                // CanEnter 與 OnEnter 之間 request 消失。不進入任何 phase，讓 CanTransitionAway 立刻放行。
                Complete();
                return;
            }

            _activeSlot = slot;
            _definition = definition;

            // 第 1 段邊界取得一次承諾；Reaction 是受擊而非出手，明確不取得。
            CaptureReleaseContext(data);

            ActiveLifecycleSink()?.Begin();
            if (!EnterPhase(ActionPhase.Start)) ActiveLifecycleSink()?.Cleanup();
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (_phase == ActionPhase.None || deltaTime <= 0f) return;
            _phaseElapsed += deltaTime;
            TryEmitRelease();

            switch (_phase)
            {
                case ActionPhase.Start:
                    TryQueueChainAdvance(data);
                    if (_phaseElapsed >= CurrentDuration())
                    {
                        // 排到了就換下一段繼續，沒排到才走原本的收尾路徑。
                        if (_chainQueued && TryAdvanceChain(data)) break;
                        EnterAfterStart();
                    }
                    break;

                case ActionPhase.Loop:
                    if (_definition.CancelMoveIntentThreshold > 0f &&
                        data.MovementIntent.DesiredSpeedNormalized >= _definition.CancelMoveIntentThreshold)
                    {
                        if (!EnterPhase(ActionPhase.Cancel)) Complete();
                    }
                    else if (_currentEntry.WaitForTrigger && IsRetriggeredThisFrame(data))
                    {
                        if (!EnterPhase(ActionPhase.End)) Complete();
                    }
                    else if (!_currentEntry.WaitForTrigger && _phaseElapsed >= CurrentDuration())
                    {
                        if (!EnterPhase(ActionPhase.End)) Complete();
                    }
                    break;

                case ActionPhase.End:
                case ActionPhase.Cancel:
                    if (_phaseElapsed >= CurrentDuration()) Complete();
                    break;
            }
        }

        public override bool CanBeInterruptedBy(BaseState other)
        {
            if (!base.CanBeInterruptedBy(other)) return false;
            return _phase == ActionPhase.None || _currentEntry.Interruptible;
        }

        /// <summary>
        /// 🆕（ADR-005；FU-1 的解）**Action → Action 中斷**。
        ///
        /// `FullBodyStateMachine.EvaluateInterrupts` 依型別排除自己，因此同為 <see cref="StateType.Action"/>
        /// 的兩個技能原本永遠無法互相打斷。本方法讓 FSM 能就「同一顆 state 的重入」單獨提問。
        ///
        /// ⚠️ **這不是第二個 interrupt 權威**：判準仍是既有的兩項——authored 的
        /// <c>Interruptible</c>（資產）＋ 目標 slot 自己的冷卻（本 state 唯一持有）。
        /// 新增的只有「身分不同」這個條件，沒有引入任何新的決策來源。
        /// </summary>
        public override bool CanReenter(PlayerRuntimeData data)
        {
            if (_phase == ActionPhase.None) return false;
            if (!_currentEntry.Interruptible) return false;
            if (!TryResolveRequest(data, out ActionSlot slot, out _)) return false;
            if (slot != _activeSlot) return true;

            return AllowsSameSlotReentry(slot);
        }

        /// <summary>
        /// 🆕（2026-09-06 使用者裁決）**同 slot 重入的唯一例外：`Reaction`。**
        ///
        /// <para><b>Problem</b></para>
        /// 硬直期間再被打一次，畫面上什麼都不會發生——`CanReenter` 要求「身分不同」，
        /// 而 Reaction 被 Reaction 打斷是同一個身分。
        ///
        /// <para><b>為什麼 Reaction 該是例外</b></para>
        /// 一般 Action 的「同身分不得重入」是在防**自己打斷自己**：連段第 2 段會被第 3 段的請求吃掉、
        /// 蓄力會被自己的再按打斷。那條限制對出手是對的。
        /// 但**受擊不是出手**——「再被打一次就該再踉蹌一次」正是硬直的定義，
        /// 它沒有「被自己打斷」的問題，因為觸發者本來就是別人。
        /// 📌 這與 `docs/11` §8.3 把 `Reaction` 排除在轉向規則外是**同一個判斷**：
        /// Reaction 套用的是受擊語意，不是出手語意。
        ///
        /// <para><b>Trade-off</b></para>
        /// 代價是多一條 slot 特例。替代方案「`Interruptible` 為真就允許任何同 slot 重入」更通用，
        /// 但會讓法術連段被自己的後續按鍵吃掉——**放寬的範圍遠大於要解決的問題**，因此不採用。
        ///
        /// <para><b>Impact</b></para>
        /// 普通 Action 的語意**一字未改**（`T20`／`T33` 守）。仍然需要 `Interruptible`：
        /// 「無敵的重擊倒地不該被輕拳打斷」依然由資產決定。
        /// </summary>
        internal static bool AllowsSameSlotReentry(ActionSlot slot) => slot == ActionSlot.Reaction;

        public override void OnUpdateMotion(
            MotionDriver motionDriver,
            AnimationFacadeBase animationFacade,
            PlayerRuntimeData data)
        {
            bool hasBake = _phase != ActionPhase.None &&
                           _currentEntry.Bake != null &&
                           _currentEntry.Bake.Duration > 0f;
            bool isActuallyPlaying = animationFacade != null &&
                                     !string.IsNullOrEmpty(AnimationKey) &&
                                     animationFacade.IsPlaying(AnimationKey);

            if (!hasBake || !isActuallyPlaying)
            {
                motionDriver.ExecuteBaseMovement(data);
                return;
            }

            motionDriver.ExecuteBakedCurveMovement(
                _currentEntry.Bake, animationFacade.GetNormalizedTime(), data);
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            ActiveLifecycleSink()?.Cleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        /// <summary>指定 slot 的冷卻剩餘秒數；0 ＝ 可用。供測試與（未來）HUD 讀取。</summary>
        public float GetCooldownRemaining(ActionSlot slot)
        {
            if (slot == ActionSlot.None) return 0f;
            return Mathf.Max(0f, _cooldownEndTime[(int)slot] - Time.time);
        }

        /// <summary>
        /// 解析「這一幀該進入哪一個 Action」。**唯一的准入判斷點**（ADR-004 D2）。
        /// 玩家意圖優先於 external request——同幀兩者都有時，自己的操作不該被別人的受擊請求蓋掉。
        /// </summary>
        private bool TryResolveRequest(
            PlayerRuntimeData data, out ActionSlot slot, out ActionDefinitionSO definition)
        {
            slot = data.Intent.RequestedActionSlot;
            if (slot == ActionSlot.None && _externalRequestTarget != null)
            {
                slot = _externalRequestTarget.PendingSlot;
            }

            definition = null;
            if (slot == ActionSlot.None) return false;

            definition = Config.GetActionDefinition(slot);
            if (definition == null)
            {
#if UNITY_EDITOR
                WarnMissingDefinitionOnce(slot);
#endif
                return false;
            }
            if (!HasPhase(definition, ActionPhase.Start)) return false;
            if (definition.RequiresGrounded && !data.IsGrounded) return false;
            return Time.time >= _cooldownEndTime[(int)slot];
        }

#if UNITY_EDITOR
        private void WarnMissingDefinitionOnce(ActionSlot slot)
        {
            int index = (int)slot;
            if (index >= 0 && index < _missingDefinitionWarnings.Length)
            {
                if (_missingDefinitionWarnings[index]) return;
                _missingDefinitionWarnings[index] = true;
            }

            Debug.LogWarning(
                $"[ActionState] ActionSlot.{slot} 沒有對應的 ActionDefinitionSO；" +
                "請檢查 StateMachineConfig 的 actionDefinitions 與該 Definition 的 Slot 欄位。");
        }
#endif

        /// <summary>Loop 期的 <c>WaitForTrigger</c>：只有**同一個 slot** 再次被請求才算 re-trigger。</summary>
        private bool IsRetriggeredThisFrame(PlayerRuntimeData data)
        {
            if (data.Intent.RequestedActionSlot == _activeSlot) return true;
            return _externalRequestTarget != null && _externalRequestTarget.PendingSlot == _activeSlot;
        }

        /// <summary>
        /// 連段的「偵測連按」。**只排隊、不立刻切段**——切段固定發生在當前段播完，
        /// 否則第 2 段會從第 1 段的中途插進來，動作看起來像被吃掉。
        ///
        /// ⚠️ 不需要防「進場那一幀的按鍵被誤判成連按」：`IntentData` 的 trigger 旗標是
        /// 「當幀生、當幀死」（`PlayerRuntimeData.ResetTransientState`，管線順序 7），
        /// 而 FSM 是先 `OnTick` 才 `OnEnter`（`FullBodyStateMachine.Tick`）——
        /// 新進入的 state 第一次 `OnTick` 已經是下一幀，那時進場的按壓早就被清掉了。
        /// 因此這裡讀到的必然是**新的一次按壓**。
        /// </summary>
        private void TryQueueChainAdvance(PlayerRuntimeData data)
        {
            if (_chainQueued) return;
            if (_chainIndex >= ChainSegmentCount()) return;   // 已在最後一段，沒有下一段可接

            float duration = CurrentDuration();
            float open = Mathf.Clamp01(_definition.ChainInputOpenNormalized);
            if (duration > 0f && _phaseElapsed < duration * open) return;

            if (IsRetriggeredThisFrame(data)) _chainQueued = true;
        }

        /// <summary>
        /// 換到下一段。`_phase` 仍是 <see cref="ActionPhase.Start"/>——見欄位區的說明。
        ///
        /// ⚠️ **`_releaseEmittedThisExecution` 在此重置**：這是連段唯一動到的既有語意。
        /// 原本它是「整次執行只發一次 Release」（Throw 只丟一顆），連段則要**每段各發一次**
        /// （每段各出一顆投射物）。單段的 Action 走不到這裡，行為不變。
        /// </summary>
        private bool TryAdvanceChain(PlayerRuntimeData data)
        {
            if (_chainIndex >= ChainSegmentCount()) return false;

            _currentEntry = _definition.ChainSegments[_chainIndex];
            _chainIndex++;
            _chainQueued = false;
            _phaseElapsed = 0f;
            _currentAnimationKey = _currentEntry.AnimationKey;
            _releaseEmittedThisExecution = false;
            CaptureReleaseContext(data);
            TryEmitRelease();
            return true;
        }

        private int ChainSegmentCount()
        {
            ActionPhaseEntry[] segments = _definition != null ? _definition.ChainSegments : null;
            return segments != null ? segments.Length : 0;
        }

        private void EnterAfterStart()
        {
            if (EnterPhase(ActionPhase.Loop)) return;
            if (EnterPhase(ActionPhase.End)) return;
            Complete();
        }

        private bool EnterPhase(ActionPhase phase)
        {
            if (!TryFindPhase(phase, out ActionPhaseEntry entry)) return false;

            _phase = phase;
            _currentEntry = entry;
            _phaseElapsed = 0f;
            _currentAnimationKey = entry.AnimationKey;

            if (phase == ActionPhase.Cancel)
            {
                ActiveLifecycleSink()?.Cleanup();
            }
            else
            {
                TryEmitRelease();
            }

            return true;
        }

        /// <summary>在當前段落邊界取得一次方向承諾；後續相機／目標變化不再改寫本段快照。</summary>
        private void CaptureReleaseContext(PlayerRuntimeData data)
        {
            _releaseContext = default;
            if (!ShouldFaceTargetOnEnter(_activeSlot)) return;

            // Definition 缺席時採預設 policy，與「忘了填」得到同一個結果——
            // 可預測性不依賴資產是否完整。
            ActionTargetingPolicy policy = _definition != null
                ? _definition.Targeting
                : ActionTargetingPolicy.CameraForward;
            if (policy == ActionTargetingPolicy.SelfCentered) return;

            // S3a 的玩家 Root 保留 IAimSource 注入鏈；現行實作同時提供「鏡頭方向」與角色世界位置。
            // 敵人版 combat producer／facing source 屬 S3b，尚不在本切片建立第二套 origin seam。
            // ⚠️ 可以是 null（敵人）⇒ 承諾安靜退化為 HasAim=false，lifecycle 仍照常完成。
            if (_aimSource == null) return;
            if (!_aimSource.TryGetAimPoint(out Vector3 aimPoint)) return;

            Vector3 origin = _aimSource.CommitmentOrigin;

            // 🔄 2026-09-08：吸敵**不再是預設**。只有顯性標記 CameraConeSoftTarget 的 Definition
            //    才會被 combat target 改寫方向，且必須通過角錐檢查（docs/11 §8.3）。
            if (policy == ActionTargetingPolicy.CameraConeSoftTarget &&
                TrySoftTarget(
                    data != null ? data.CombatContext : default,
                    origin,
                    aimPoint,
                    out Vector3 softTargetPoint))
            {
                aimPoint = softTargetPoint;
            }

            _releaseContext = new ActionReleaseContext(aimPoint, aimPoint - origin);
        }

        /// <summary>
        /// 唯讀暴露目前段落承諾的水平投影。唯一 facing source 會在順序 4.6 主動 pull。
        /// </summary>
        internal bool TryGetFacingCommitment(out Vector3 worldDirection)
        {
            worldDirection = default;
            if (_phase == ActionPhase.None || !_releaseContext.HasAim) return false;

            worldDirection = _releaseContext.Direction;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= DirectionSqrEpsilon)
            {
                worldDirection = default;
                return false;
            }

            worldDirection.Normalize();
            return true;
        }

        /// <summary>
        /// <see cref="ActionTargetingPolicy.CameraConeSoftTarget"/> 的修正判定：**只有**當 combat target
        /// 落在瞄準方向前方的角錐內才改用它，否則維持 camera forward。
        ///
        /// 🔄 取代 2026-09-08 之前的 `TrySelectAimPoint`（那版無條件讓 combat target 壓過 aim point，
        /// 等於所有 Action 都是 soft-target ⇒ 與 `docs/11` §8.3 的裁決相反）。
        ///
        /// **為什麼是水平角錐**：承諾同時餵給 facing（水平投影）與世界效果（3D）。垂直方向由相機俯仰
        /// 主導、與「敵人在不在我正前方」無關，把它算進角錐只會讓低頭時吸不到人。
        ///
        /// **為什麼不另外查一次相機錐**：`docs/10` §3-D3 禁止新建 targeting service／目標列表。
        /// combat context producer 已經是目標的唯一供應商，這裡只對它的結果加一道方向閘門。
        /// </summary>
        internal static bool TrySoftTarget(
            CombatContextData combatContext,
            Vector3 origin,
            Vector3 aimPoint,
            out Vector3 targetPoint)
        {
            targetPoint = default;
            if (!combatContext.HasTarget) return false;

            Vector3 aimDirection = aimPoint - origin;
            Vector3 targetDirection = combatContext.TargetPosition - origin;
            aimDirection.y = 0f;
            targetDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= DirectionSqrEpsilon ||
                targetDirection.sqrMagnitude <= DirectionSqrEpsilon)
            {
                return false;
            }

            if (Vector3.Angle(aimDirection, targetDirection) > SoftTargetConeHalfAngleDegrees)
            {
                return false;
            }

            targetPoint = combatContext.TargetPosition;
            return true;
        }

        private void TryEmitRelease()
        {
            if (_releaseEmittedThisExecution || !_currentEntry.EmitsRelease) return;

            float normalizedTime = Mathf.Clamp01(_currentEntry.ReleaseNormalizedTime);
            if (_phaseElapsed < CurrentDuration() * normalizedTime) return;

            _releaseEmittedThisExecution = true;
            ActiveLifecycleSink()?.Release(in _releaseContext);
        }

        private float CurrentDuration()
        {
            float bakedDuration = _currentEntry.Bake != null ? _currentEntry.Bake.Duration : 0f;
            return bakedDuration > 0f ? bakedDuration : Mathf.Max(0f, _currentEntry.FallbackDuration);
        }

        private bool TryFindPhase(ActionPhase phase, out ActionPhaseEntry entry)
            => TryFindPhase(_definition, phase, out entry);

        private static bool TryFindPhase(
            ActionDefinitionSO definition, ActionPhase phase, out ActionPhaseEntry entry)
        {
            ActionPhaseEntry[] phases = definition != null ? definition.Phases : null;
            int count = phases != null ? phases.Length : 0;
            for (int i = 0; i < count; i++)
            {
                if (phases[i].Phase != phase) continue;
                entry = phases[i];
                return true;
            }

            entry = default;
            return false;
        }

        private static bool HasPhase(ActionDefinitionSO definition, ActionPhase phase)
            => TryFindPhase(definition, phase, out _);

        private void Complete()
        {
            // 必須在 CommitCooldown／ResetExecutionState 清掉 _activeSlot 前查表；
            // 否則自然完成時會把 Cleanup 送到 None，重演冷卻曾踩過的同一個順序 bug。
            ActiveLifecycleSink()?.Cleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        private IActionLifecycleSink ActiveLifecycleSink()
        {
            int index = (int)_activeSlot;
            return index > 0 && index < _lifecycleSinks.Length ? _lifecycleSinks[index] : null;
        }

        /// <summary>
        /// 把冷卻記在**剛結束的那個 slot** 上，不是全域。Definition 為空（未成功進入）時不寫。
        ///
        /// 🐛 **2026-09-02 修**：本段原本只寫在 <see cref="OnExit"/>，但 <see cref="Complete"/> 會先
        /// <see cref="ResetExecutionState"/> 清掉 <c>_activeSlot</c>／<c>_definition</c> ——
        /// 於是**自然播完的 Action 永遠不會進冷卻**，只有被中斷的才會。方向剛好相反。
        /// 兩個結束路徑（自然完成／被中斷）都要記，所以抽成同一個方法在兩處呼叫；
        /// 由下方的 null 判定保證冪等（`Complete()` 記完即清，後續 `OnExit` 是 no-op），
        /// 因此**不會重複延長冷卻**。
        /// </summary>
        private void CommitCooldown()
        {
            if (_definition == null || _activeSlot == ActionSlot.None) return;

            _cooldownEndTime[(int)_activeSlot] = ComputeCooldownEndTime(
                Time.time, _definition.Cooldown, _definition.CooldownVariance, UnityEngine.Random.value);
        }

        /// <summary>
        /// 🆕（2026-09-06）**冷卻的隨機加量**。近戰敵人 AI 的業界通則是「攻擊間隔平均 2–3 秒」——
        /// 固定間隔會讓敵人讀起來像節拍器，玩家一旦數出拍子，戰鬥就沒有壓力了。
        ///
        /// ⚖️ **為什麼放在資產而不是 AI**：`AIInputSource` 明確不准自建計時節流
        /// （能不能出手的唯一回答者是 `ActionState`，ADR-004 D2）。把變異做成 authored 欄位，
        /// 節奏就變成**可調的資料**而不是 AI 的隱藏狀態——玩家技能填 0 拿到可預測的節奏，
        /// 敵人填 &gt; 0 拿到活的節奏，**兩者共用同一段程式**。
        ///
        /// ⚠️ 只加不減：實際冷卻永遠 ≥ authored `Cooldown`。
        /// 純函數版本讓隨機來源可注入，測試因此是確定性的。
        /// </summary>
        internal static float ComputeCooldownEndTime(
            float currentTime, float cooldown, float variance, float random01)
        {
            float baseCooldown = Mathf.Max(0f, cooldown);
            float extra = Mathf.Max(0f, variance) * Mathf.Clamp01(random01);

            return currentTime + baseCooldown + extra;
        }

        /// <summary>
        /// 清掉單次執行的瞬態。⚠️ <c>_cooldownEndTime</c> **刻意不在此清除**——
        /// 冷卻是跨執行的狀態，清了就等於每次出手都重置冷卻。
        /// </summary>
        private void ResetExecutionState()
        {
            _phase = ActionPhase.None;
            _currentEntry = default;
            _phaseElapsed = 0f;
            _currentAnimationKey = null;
            _releaseEmittedThisExecution = false;
            _chainIndex = 0;
            _chainQueued = false;
            _releaseContext = default;
            _activeSlot = ActionSlot.None;
            _definition = null;
        }
    }
}
