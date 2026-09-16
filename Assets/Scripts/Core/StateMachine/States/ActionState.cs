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

        /// <summary>
        /// **每個 slot 的 sink 清單**（jagged，外層索引＝slot）。
        ///
        /// 🆕 2026-09-14（使用者裁決）：由 per-slot **單顆**改為 per-slot **多顆**。
        /// 理由是 seam 的真實語意本來就是「這個 Action 的副作用」，而一個 Action 天生可以
        /// 同時有好幾個彼此獨立的副作用——命中判定、武器顯隱、刀光、VFX、音效——
        /// 它們共用同一組 `Begin` → `Release` → `Cleanup` 時點，卻沒有理由互相認識。
        /// ⛔ 舊的「一 slot 一 sink」限制不是設計，只是還沒遇到第二個使用者。
        ///
        /// **不變的部分**：時點仍由 ActionState 單一持有；sink 只管自己的本地 Unity 物件。
        /// **通知順序** ＝ Runner 組裝時的 binding 順序，且**穩定**（見 `ResolveActionLifecycleSinks`）。
        /// 空 slot 恆為 <see cref="Array.Empty{T}"/>，因此派送端不需要 null 判定。
        ///
        /// ⚠️ 零 GC：全部在組裝期配置完成；派送是對具體陣列的索引迴圈，執行期不配置、不搜尋元件。
        /// </summary>
        private readonly IActionLifecycleSink[][] _lifecycleSinks;

        // 🆕（ADR-005）冷卻改為 per-slot。**仍住在 ActionState 內部**（ADR-004 D2）——
        // 搬到 Runner／Config／HUD 都會讓「能不能出手」有第二個回答者。
        private readonly float[] _cooldownEndTime = new float[SlotCount];

        // 🆕（2026-09-15，HUD）**本次**冷卻的總長度，供 HUD 換算 0–1 進度。
        // ⚠️ 不能讓 HUD 自己拿 `Definition.Cooldown` 去除——`CooldownVariance` 會讓每一次的
        //    實際長度不同，那樣算出來的進度條會在有變異時失準（而且只有在變異非零時才發作）。
        //    ⇒ 由**擁有冷卻的人**在 commit 的當下記下真正用掉的長度。
        private readonly float[] _cooldownDuration = new float[SlotCount];
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

        // 🆕（2026-09-15）**效果落點**，與上面的 facing 承諾刻意分開。
        // 每帧刷新、release 當下才定案 ⇒ 法術打的是「現在」的目標，不是抬手那一刻的舊座標。
        // 為什麼非分開不可：見 CaptureReleaseContext 的說明（抬手 0.42s ＋ 飛行 ⇒ 約 1.7m 落差）。
        private ActionReleaseContext _effectContext;

        // Action-time soft auto-target 的無狀態查詢／facing 轉送 seam。
        // ⚠️ 可以是 null——敵人身上沒有相機式 aim source，
        //    承諾因此安靜退化為 HasAim=false，lifecycle 仍照常完成。
        private readonly IAimSource _aimSource;

        public ActionState(
            ActionRequestTarget externalRequestTarget = null,
            IActionLifecycleSink[][] lifecycleSinks = null,
            IAimSource aimSource = null)
        {
            _externalRequestTarget = externalRequestTarget;
            _lifecycleSinks = lifecycleSinks ?? Array.Empty<IActionLifecycleSink[]>();
            _aimSource = aimSource;
        }

        /// <summary>
        /// 「這個 **slot** 會不會取得方向承諾」——**slot 層級的全域閘門**（`docs/11` §8.3）。
        ///
        /// ⚠️ 這裡回答的是**會不會取得**，不是**朝哪取得**。後者自 2026-09-08 起由
        /// <see cref="ActionTargetingPolicy"/> 這個 authored 欄位回答（該裁決同時修訂了 ADR-007 D3，見其 §11）。
        ///
        /// 🔄 **2026-09-14（`docs/26` Model B）：移除了 `&amp;&amp; slot != ActionSlot.Reaction`。**
        /// 那個例外的理由是「受擊不是出手，被打的人不該自己轉去面對攻擊者」——
        /// 正確的結論不是在 Action 裡加例外，而是**受擊根本不該是 Action**。
        /// 受擊已遷出為 <see cref="StateType.Hurt"/>，本方法因此回到單純的規則：
        /// **每一個 Action 都取得方向承諾**，沒有例外。
        /// </summary>
        internal static bool ShouldFaceTargetOnEnter(ActionSlot slot)
            => slot != ActionSlot.None;

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

            NotifyBegin();
            if (!EnterPhase(ActionPhase.Start)) NotifyCleanup();
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            if (_phase == ActionPhase.None || deltaTime <= 0f) return;

            // 效果落點每帧跟上目標；facing 承諾**不動**（見 CaptureReleaseContext）。
            RefreshEffectContext(data);
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

        /// <summary>
        /// 🆕（ADR-009 D4）**兩層中斷授權。**
        ///
        /// <para><b>Problem</b></para>
        /// <c>Interruptible</c> 原本是單一層級的否決權。把它設成 <c>false</c>（＝Super Armor，
        /// 現況的 <c>EnemyPunchDefinition</c> 就是）會連**死亡**一起擋掉——「霸體」不該等於「打不死」。
        /// 這個缺口在導入死亡之前不存在，因為當時沒有比受擊更高的中斷來源。
        ///
        /// <para><b>語意（收窄，不是推翻）</b></para>
        /// 資產的 <c>Interruptible</c> 只管**普通**中斷；config 的 <c>CanBeInterruptedBy</c>
        /// 才是狀態層授權。<see cref="StateType.Death"/> 屬 lethal 層，**只受 config 管**。
        /// ⇒ 中斷權限仍然 100% authored：想讓某個狀態連死亡都擋掉，就把 Death 從該狀態的
        /// <c>CanBeInterruptedBy</c> 清單裡拿掉——那是資產的決定，不是這裡的。
        ///
        /// <para><b>為什麼不引入通用的 InterruptAuthority 分級</b></para>
        /// 目前只有兩級（normal／lethal）。為兩級建通用分級制＝在沒有第二個非致死高權中斷的情況下
        /// 把介面定死（CLAUDE.md：第二個使用者出現前不建 abstraction）。
        /// 真的出現第三級時，擴充的是 config 清單與這一段的判斷，不是每份 Definition。
        /// </summary>
        public override bool CanBeInterruptedBy(BaseState other)
        {
            if (!base.CanBeInterruptedBy(other)) return false;
            if (other != null && other.Type == StateType.Death) return true;
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
        /// <remarks>
        /// 🔄 **2026-09-14（`docs/26` Model B）：刪除了 `AllowsSameSlotReentry` 的 Reaction 特例。**
        /// 那條特例存在的唯一理由是「硬直中再被打要再踉蹌一次」，而它的註解自己寫著
        /// 「**受擊不是出手**」——受擊已遷出為 <see cref="StateType.Hurt"/>，
        /// 「再次受擊」現在由 <c>HurtState.CanReenter</c> 回答。
        /// 本方法因此回到 ADR-005 FU-1 的原始語意：**同一個 Action 身分不得自我重入**
        /// （防連段第 2 段被第 3 段的請求吃掉、防蓄力被自己的再按打斷）。
        /// </remarks>
        public override bool CanReenter(PlayerRuntimeData data)
        {
            if (_phase == ActionPhase.None) return false;
            if (!_currentEntry.Interruptible) return false;
            if (!TryResolveRequest(data, out ActionSlot slot, out _)) return false;
            return slot != _activeSlot;
        }

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
            NotifyCleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        /// <summary>指定 slot 的冷卻剩餘秒數；0 ＝ 可用。供測試與 HUD 讀取。</summary>
        public float GetCooldownRemaining(ActionSlot slot)
        {
            if (slot == ActionSlot.None) return 0f;
            return Mathf.Max(0f, _cooldownEndTime[(int)slot] - Time.time);
        }

        /// <summary>
        /// 🆕（2026-09-15，HUD）指定 slot 的冷卻進度：**1 ＝ 剛進冷卻、0 ＝ 已可用**。
        ///
        /// <para><b>⭐ 為什麼這個換算住在這裡，而不是 HUD 自己算</b></para>
        /// 分母是「**這一次**冷卻的實際長度」，而 `CooldownVariance` 讓它每次都不同。
        /// HUD 若拿 `Definition.Cooldown` 當分母，進度條會在變異非零時失準——
        /// 而那是一種只在特定資產設定下才發作、看起來像「進度條有點怪」的靜默錯誤。
        /// ⇒ 讓**擁有冷卻的人**回答進度，HUD 只消費結果（與 `SurvivabilityData.IsDead`
        /// 「不讓下游重新推導已 commit 的狀態」同一條原則）。
        ///
        /// 從未進過冷卻、或長度為 0 ⇒ 回 0（可用），不會除以零。
        /// </summary>
        public float GetCooldownNormalized(ActionSlot slot)
        {
            if (slot == ActionSlot.None) return 0f;

            float duration = _cooldownDuration[(int)slot];
            if (!(duration > 0f)) return 0f;

            return Mathf.Clamp01(GetCooldownRemaining(slot) / duration);
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
                NotifyCleanup();
            }
            else
            {
                TryEmitRelease();
            }

            return true;
        }

        /// <summary>
        /// 在當前段落邊界取得一次方向承諾；後續相機／目標變化不再改寫本段快照。
        ///
        /// 🔄 **2026-09-15：承諾拆成兩個，因為它們回答的是不同的問題。**
        /// <list type="bullet">
        /// <item><b><see cref="_releaseContext"/>（facing 承諾）</b>——段落邊界快照，**行為不變**。
        ///   身體要在抬手期間**朝著出手方向轉過去**，那必須早早定下來，否則角色會跟著目標抽搐。
        ///   ADR-007「facing 一次承諾」在這一半完整保留。</item>
        /// <item><b><see cref="_effectContext"/>（效果落點）</b>——**每帧刷新、release 當下才定案**。</item>
        /// </list>
        ///
        /// <para><b>🐞 為什麼非拆不可（2026-09-15 使用者 Play 回報 ＋ 實測）</b></para>
        /// Fireball 的 release 在 <c>FallbackDuration 1.2 × ReleaseNormalizedTime 0.35</c> ＝ **抬手 0.42 秒後**。
        /// 用段落邊界的舊座標發射，等於打「敵人 0.42 秒前的位置」，再加上約 0.6 秒飛行：
        /// Y Bot 側移 1.644 m/s（<c>min(holdStrafe 0.35, StrafeLeftLoop 1.6443/SprintFwdLoop 6.2614)</c> × 6.2614）
        /// ⇒ 累積 **≈1.7 m**，而敵人膠囊直徑只有 **0.64 m**
        /// ⇒ **側移中的敵人打不中是預期行為，不是偶發。**
        /// 過肩鏡頭下，飛向舊座標的火球投影起來就是「穿過身體」——這正是回報的症狀。
        ///
        /// ⚠️ **代價（已知並接受）**：身體朝向與彈道會相差「目標在抬手期間移動的角度」。
        /// 3 m 距離下約 13°。**用 13° 的視覺落差換掉一次必定落空**，是划算的。
        /// ⛔ 反過來讓 facing 也跟著每帧重取則**不可**——那會讓角色在抬手期間持續扭向目標，
        ///    正是 ADR-007 當初要消除的抽搐。
        /// </summary>
        private void CaptureReleaseContext(PlayerRuntimeData data)
        {
            _releaseContext = ResolveAimContext(data);
            _effectContext = _releaseContext;
        }

        /// <summary>
        /// 效果落點的每帧刷新。只動 <see cref="_effectContext"/>，**不碰 facing 承諾**。
        /// 解不出瞄準（目標消失、`IAimSource` 為 null）時**保留上一次可用的落點**，
        /// 不要退化成 <c>default</c>——那會讓法術朝世界原點飛。
        /// </summary>
        private void RefreshEffectContext(PlayerRuntimeData data)
        {
            if (_phase == ActionPhase.None) return;

            ActionReleaseContext live = ResolveAimContext(data);
            if (live.HasAim) _effectContext = live;
        }

        private ActionReleaseContext ResolveAimContext(PlayerRuntimeData data)
        {
            if (!ShouldFaceTargetOnEnter(_activeSlot)) return default;

            // Definition 缺席時採預設 policy，與「忘了填」得到同一個結果——
            // 可預測性不依賴資產是否完整。
            ActionTargetingPolicy policy = _definition != null
                ? _definition.Targeting
                : ActionTargetingPolicy.CameraForward;
            if (policy == ActionTargetingPolicy.SelfCentered) return default;

            // S3a 的玩家 Root 保留 IAimSource 注入鏈；現行實作同時提供「鏡頭方向」與角色世界位置。
            // 敵人版 combat producer／facing source 屬 S3b，尚不在本切片建立第二套 origin seam。
            // ⚠️ 可以是 null（敵人）⇒ 承諾安靜退化為 HasAim=false，lifecycle 仍照常完成。
            if (_aimSource == null) return default;
            if (!_aimSource.TryGetAimPoint(out Vector3 aimPoint)) return default;

            Vector3 origin = _aimSource.CommitmentOrigin;

            // 🔄 2026-09-08：吸敵**不再是預設**。只有顯性標記 CameraConeSoftTarget 的 Definition
            //    才會被 combat target 改寫方向，且必須通過角錐檢查（docs/11 §8.3）。
            if (policy == ActionTargetingPolicy.CameraConeSoftTarget &&
                TrySoftTarget(
                    data != null ? data.CombatContext : default,
                    origin,
                    out Vector3 softTargetPoint))
            {
                aimPoint = softTargetPoint;
            }

            return new ActionReleaseContext(aimPoint, aimPoint - origin);
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
        /// <see cref="ActionTargetingPolicy.CameraConeSoftTarget"/> 只在 commitment boundary 消費
        /// combat context producer 當幀算好的無記憶候選。range／cone／scoring 只存在於 producer；
        /// ActionState 不重算選擇，也不在段落內追蹤。
        /// </summary>
        internal static bool TrySoftTarget(
            CombatContextData combatContext,
            Vector3 origin,
            out Vector3 targetPoint)
        {
            targetPoint = default;
            if (!combatContext.HasSoftTarget) return false;

            Vector3 targetDirection = combatContext.SoftTargetPosition - origin;
            if (targetDirection.sqrMagnitude <= DirectionSqrEpsilon) return false;

            targetPoint = combatContext.SoftTargetPosition;
            return true;
        }

        private void TryEmitRelease()
        {
            if (_releaseEmittedThisExecution || !_currentEntry.EmitsRelease) return;

            float normalizedTime = Mathf.Clamp01(_currentEntry.ReleaseNormalizedTime);
            if (_phaseElapsed < CurrentDuration() * normalizedTime) return;

            _releaseEmittedThisExecution = true;
            NotifyRelease();
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
            NotifyCleanup();
            CommitCooldown();
            ResetExecutionState();
        }

        // =====================================================================
        // Lifecycle 派送（🆕 2026-09-14：一個 slot 可以有多顆 sink）
        //
        // 三個方法刻意長得一模一樣，也刻意**不**抽成共用的 delegate 派送：
        // `Release` 帶 `in` 參數，抽象化會讓它退化成裝箱或閉包配置，違反零 GC。
        // 重複三個短迴圈比重複一次配置便宜。
        //
        // ⚠️ 每個方法都**先取本地變數再迴圈**：sink 在被通知期間不得改變自己的註冊，
        //    而本地引用讓「即使清單被換掉，本次派送仍走完同一份快照」成為結構性保證。
        // ⚠️ 用索引迴圈而非 foreach：CLAUDE.md 的熱路徑鐵律（介面型集合的 foreach 會裝箱）。
        //    這裡靜態型別已是具體陣列、foreach 本可零配置，但統一寫法避免日後改型別時回歸。
        // =====================================================================

        private void NotifyBegin()
        {
            IActionLifecycleSink[] sinks = ActiveLifecycleSinks();
            for (int i = 0; i < sinks.Length; i++) sinks[i]?.Begin();
        }

        private void NotifyRelease()
        {
            IActionLifecycleSink[] sinks = ActiveLifecycleSinks();
            for (int i = 0; i < sinks.Length; i++) sinks[i]?.Release(in _effectContext);
        }

        private void NotifyCleanup()
        {
            IActionLifecycleSink[] sinks = ActiveLifecycleSinks();
            for (int i = 0; i < sinks.Length; i++) sinks[i]?.Cleanup();
        }

        /// <summary>
        /// 目前 slot 的 sink 清單。**恆不為 null**——空 slot 回 <see cref="Array.Empty{T}"/>，
        /// 讓三個派送方法都不需要 null 判定（少一條每次都要記得寫對的分支）。
        /// </summary>
        private IActionLifecycleSink[] ActiveLifecycleSinks()
        {
            int index = (int)_activeSlot;
            if (index <= 0 || index >= _lifecycleSinks.Length) return Array.Empty<IActionLifecycleSink>();
            return _lifecycleSinks[index] ?? Array.Empty<IActionLifecycleSink>();
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

            float now = Time.time;
            float endTime = ComputeCooldownEndTime(
                now, _definition.Cooldown, _definition.CooldownVariance, UnityEngine.Random.value);

            _cooldownEndTime[(int)_activeSlot] = endTime;
            // 記下**這一次**實際用掉的長度，而不是 authored 的 `Cooldown`——見欄位註解。
            _cooldownDuration[(int)_activeSlot] = Mathf.Max(0f, endTime - now);
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
            _effectContext = default;
            _activeSlot = ActionSlot.None;
            _definition = null;
        }
    }
}
