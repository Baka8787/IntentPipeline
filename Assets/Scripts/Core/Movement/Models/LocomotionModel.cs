using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（ADR-003 Migration Stage 2）**Locomotion Model**——雙足地面移動模型，
    /// <see cref="IMovementModel"/> 的第一個實作。收攏了 Stage 1 遺留在通用 Runner 的三件事
    /// （ADR-003 §9-L1 的殘餘耦合，本輪結案）：
    /// 1. **B9 平滑**（<see cref="LocomotionSpeedSmoother"/>，整顆從 Runner 換持有者搬來，計算邏輯零改寫）
    /// 2. **運動輸出導出**（速度／方向／上半身權重）
    /// 3. **自驅動畫參數**（<c>MoveSpeed</c>／<c>MoveX</c>／<c>MoveZ</c>）——D4「每個 model 驅動自己的參數」
    ///
    /// 遷移後 <c>CharacterPipelineRunner</c> 不再認識任何 locomotion 概念（無 MoveSpeed、無平滑時間、無 gait）。
    /// </summary>
    /// <remarks>
    /// **為何是 MonoBehaviour**：平滑時間是 per-game 手感參數，需要 Inspector 可調；且比照
    /// <see cref="PlayerLocomotionPolicy"/>（同為掛在角色 Root 的可替換零件）的既有 pattern，
    /// 讓「換 model」＝「換一顆元件」。model 不持有 Facade／MotionDriver 的跨幀引用——
    /// 兩者皆由呼叫端逐幀傳入；跨幀狀態只有值型別 dynamics
    /// <c>_smoother</c>／<c>_stop</c>（ADR-003 §9-L5 snapshot-able 前提）。
    ///
    /// **黑板 Movement Output 的語意**（2026-07-25 裁決）：<c>MoveSpeed</c>／<c>MoveDirection</c>
    /// 自本輪起**不再是 Runner 維護的 locomotion state**，而是
    /// **當下 active Movement Model 發布的 Movement Output**——消費端（MotionDriver、Jump 空中控制、
    /// Editor 監視器）讀到的是「模型算出來的運動」，寫入者唯一且為本檔。
    /// D4 的最終形態（欄位完全內化、不經黑板）目標不變，但需連動 MotionDriver API 與 Jump 空中控制，
    /// 留待後續階段；本輪刻意不一次做完（migration intermediate state，見 dev-spec §7.3）。
    /// </remarks>
    public class LocomotionModel : MonoBehaviour, IMovementModel, IFootPhaseSource
    {
        /// <summary>
        /// ambient 門檻：平滑速度達此值即視為「正在移動」（Move），低於則為 Idle。
        /// 沿用 Migration 前 <c>IdleState/MoveState.CanEnter</c> 內硬編的 0.1f，數值與行為完全等價；
        /// 差別只在**歸屬**——這是 locomotion 的內部門檻，不該長在狀態機裡。
        /// </summary>
        public const float MoveThreshold = 0.1f;
        private const float PhaseMatchTolerance = 0.02f;
        private const float PhaseWaitTimeout = 0.5f;

        // 🆕（B9 Game Feel，自 CharacterPipelineRunner 原樣搬入）鍵盤 0/1 輸入直送會讓 1D Mixer
        // 一幀從 Idle 跳 Sprint、中間 Walk/Run tier 踩不到。以 SmoothDamp 讓速度隨時間爬升/回落，
        // 平順經過各速度 tier；動畫混合與實際位移共用同一平滑值，加減速全程不滑步。
        [Header("Move Speed Smoothing (B9)")]
        [Tooltip("MoveSpeed 0→滿 的加速平滑時間（秒，SmoothDamp）。越小越 snappy。")]
        [SerializeField] private float moveSpeedAccelTime = 0.12f;
        [Tooltip("MoveSpeed 滿→0 的減速平滑時間（秒）。通常略大於加速＝放開後自然滑行收步。")]
        [SerializeField] private float moveSpeedDecelTime = 0.18f;
        [Tooltip("世界 XZ 移動方向的最大轉向角速度（度／秒）。720 表示 90° 轉向至少跨 0.125 秒。")]
        [SerializeField, Min(0f)] private float directionTurnDegreesPerSecond = 720f;

        [Header("Combat Directional Locomotion (Optional)")]
        [Tooltip("只給需要 combat directional locomotion 的 actor。未指派時維持既有 1D／速度行為。")]
        [SerializeField] private CombatDirectionalSpeedProfileSO combatDirectionalSpeedProfile;

        [Header("Forward Stop (C1 / C1.1)")]
        [Tooltip("Walk loop Bake Data：只用 authored FootPhaseCurve 配合 Locomotion 主導子動作時間選 LU/RU；不是 tier 表。")]
        [SerializeField] private MotionBakeData walkLoopBakeData;
        [Tooltip("明確列舉兩支 Walk Stop 變體（Bake Data + Animation Key）。")]
        [SerializeField] private LocomotionStopVariant[] walkStopVariants;
        [Tooltip("Run loop Bake Data：只用 authored FootPhaseCurve 配合 Locomotion 主導子動作時間選 LU/RU。")]
        [SerializeField] private MotionBakeData runLoopBakeData;
        [Tooltip("明確列舉兩支 Run Stop 變體（Bake Data + Animation Key）。")]
        [SerializeField] private LocomotionStopVariant[] runStopVariants;
        [SerializeField] private string locomotionAnimationKey = "Idle";

        [Header("🧪 ADR-011 起跑入場相位對齊（Trial；預設關 ＝ 現行）")]
        [Tooltip("Run、從靜止（Idle／停步）同方向起跑時，讓 locomotion 循環從 run loop 烘焙腳相曲線的「左腳觸地極值」開始。" +
                 "相位只由 Bake 推得，不手填。還原 ＝ 關閉。")]
        [SerializeField] private bool alignRunEntryPhase = false;

        [Tooltip("起跑方向與角色目前朝向的夾角上限（度）——超過就不是「同方向起跑」，不對齊。")]
        [SerializeField, Range(0f, 90f)] private float entryPhaseMaxHeadingDegrees = 30f;
        [SerializeField] private float walkStopMinIntensity = 0.35f;
        [SerializeField] private float walkStopMaxIntensity = 0.50f;
        [SerializeField] private float runStopMinIntensity = 0.75f;
        [SerializeField] private float runStopMaxIntensity = 0.875f;
        [SerializeField] private float stopTimeoutMargin = 0.25f;

        private LocomotionSpeedSmoother _smoother;
        private LocomotionStopRuntime _stop;
        private bool _wasIntending;
        private int _lastMotionFrame = int.MinValue;
        private int _nextWalkFallbackVariantIndex;
        private int _nextRunFallbackVariantIndex;
        private float _locomotionNormalizedTime;
        private bool _hasLocomotionTimeSnapshot;

#if UNITY_EDITOR
        private int _missingPhaseWarningMask;
        private int _invalidConfigurationWarningMask;
#endif

        /// <inheritdoc />
        public bool IsProducingMotion => _smoother.Speed >= MoveThreshold || _stop.IsActive;

        /// <inheritdoc />
        public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime)
        {
            // ADR-010：快照由唯一 Writer 每次 tick 先清除；只有本帧確認停步正在播放、播放頭已前進、
            // Bake 有結束站姿時，才在下方重新寫入 Active=true。⇒ Active 不是「最近跑過」，是「本帧確認」。
            data.LocomotionStopPrediction = default;

            // 順序 3 發生在 FSM 切入 Jump 之前；此處先快照 locomotion 播放頭，JumpState.OnEnter
            // 才能在同一幀承諾 LU／RU。若等到順序 6，順序 5 已開始 cross-fade 到 Jump，來源可能消失。
            CaptureLocomotionTime(animationFacade);

            MovementIntentData movementIntent = data.MovementIntent;
            ApplyCombatDirectionalSpeedLimit(data, ref movementIntent);

            float desiredSpeed = Mathf.Clamp01(movementIntent.DesiredSpeedNormalized);
            // docs/48 §1.3（S-A）：「有沒有移動請求」只有一條判讀規則；作用在 model 自己的有效意圖（已套戰鬥方向限速）上。
            bool isIntending = movementIntent.HasMovementRequest;

            // Stop 的「入場強度」是放開發生前一刻的實際速度，不是放開後已被 SmoothDamp
            // 衰減一次的速度。先快照可避免 0.75 Run 在 60/120 FPS 首幀分別掉到約
            // 0.739/0.747，因而錯過下界 0.75；也讓 tier 選擇不受幀率影響。
            float releaseEntryIntensity = _smoother.Speed;

            bool hadAmbientMotionLastFrame = _lastMotionFrame == Time.frameCount - 1;
            if (_stop.IsActive)
            {
                TickActiveStop(data, animationFacade, deltaTime, isIntending, hadAmbientMotionLastFrame);
            }

            // 🧪 ADR-011：在停步被意圖打斷（已 Play 回 locomotion）之後、本帧動畫參數寫入之前對齊——
            //    這一帧 Walk／Run 子狀態權重仍為 0，重設播放頭不會被看見。
            if (alignRunEntryPhase && animationFacade != null &&
                ShouldAlignRunEntryPhase(_wasIntending, isIntending, releaseEntryIntensity, MoveThreshold,
                    data.Gait == GaitTier.Run, movementIntent.DesiredDirection, transform.forward, entryPhaseMaxHeadingDegrees) &&
                TryGetRunEntryPhase(out float entryPhase))
            {
                animationFacade.TryAlignEntryPhase(locomotionAnimationKey, entryPhase);
            }

            if (!_stop.IsActive && LocomotionStopSelector.IsReleaseRequest(
                    _wasIntending, desiredSpeed, false, data.IsGrounded,
                    hadAmbientMotionLastFrame, deltaTime))
            {
                LocomotionStopTier tier = LocomotionStopSelector.SelectTier(
                    releaseEntryIntensity,
                    walkStopMinIntensity, walkStopMaxIntensity,
                    runStopMinIntensity, runStopMaxIntensity);
                if (tier != LocomotionStopTier.None) TryStartStop(animationFacade, tier);
            }

            // Pending Walk Stop 是本 model 的私有 dynamics：維持 release-entry 速度／方向走到最近 authored
            // 入場相位，避免先 B9 減速、播放 Stop 時又被曲線重新推動。其他時候仍逐字走既有 smoother。
            // 輸出依然只由 MovementIntent ＋ 本 dynamics 導出，沒有第二寫入者或手填輸出。
            // 已 Play 但播放頭尚未前進的帧（docs/07 R8）同樣維持 release-entry，曲線到第一個有效播放頭差才接手。
            if (!_stop.HoldsEntryMotion)
            {
                _smoother.Tick(
                    in movementIntent,
                    moveSpeedAccelTime,
                    moveSpeedDecelTime,
                    directionTurnDegreesPerSecond,
                    deltaTime);
            }

            data.MoveSpeed = _smoother.Speed;
            data.MoveDirection = _smoother.Direction;
            // 🆕（ADR-003 D4）**model 自驅動畫參數**：由 Locomotion Transition 資產內的 ParameterName
            // 自行訂閱；MoveSpeed 保留給現行 1D Mixer，MoveX／MoveZ 供後續 2D Mixer 使用。
            // 本層不認識任何 Mixer（tier 門檻是資料，住在 Locomotion.asset）。
            // ⚠️ 驅動點刻意留在 Update（順序 3）而非 LateUpdate：Animator 評估卡在兩者之間，
            //    移到 LateUpdate 會讓動畫參數比位移晚一幀。
            if (animationFacade != null)
            {
                animationFacade.SetFloat(AnimationFacadeBase.ParamMoveSpeed, _smoother.Speed);

                Vector2 localMove = ProjectMoveParameters(
                    transform.rotation, data.MoveDirection, _smoother.Speed);
                animationFacade.SetFloat(AnimationFacadeBase.ParamMoveX, localMove.x);
                animationFacade.SetFloat(AnimationFacadeBase.ParamMoveZ, localMove.y);
            }

            _wasIntending = isIntending;

            // ADR-010：stop variant 一 committed（含 pending／等待播放頭）就進入發布；各區塊自己的有效條件在 PublishStopPrediction 內判斷。
            if (_stop.IsActive && TryGetActiveVariant(out LocomotionStopVariant predictionVariant))
            {
                PublishStopPrediction(data, predictionVariant.BakeData);
            }

            // ADR-010 D6：已承諾的停步 tier 有自己的有效條件（committed 即有效，不看結束站姿／next plant），
            // 只覆寫這一欄 ⇒ 其他區塊的值與有效旗標逐位元不變。
            if (_stop.IsActive)
            {
                LocomotionStopPrediction prediction = data.LocomotionStopPrediction;
                prediction.StopTier = ToCommittedStopTier(_stop.Tier);
                data.LocomotionStopPrediction = prediction;
            }
        }

        /// <summary>
        /// （純函數）ADR-011 的觸發條件：意圖的上升緣、起跑前在靜止（速度 &lt; restSpeed）、Run、且起跑方向與目前朝向一致。
        /// 只回答「這一帧是不是 Run 的同方向靜止起跑」；⛔ 不涵蓋 Walk、轉向起跑、移動中加速。
        /// </summary>
        public static bool ShouldAlignRunEntryPhase(bool wasIntending, bool isIntending, float previousSpeed, float restSpeed,
            bool runGait, Vector3 desiredDirection, Vector3 facing, float maxHeadingDegrees)
        {
            if (wasIntending || !isIntending || !runGait || previousSpeed >= restSpeed) return false;
            Vector2 d = new Vector2(desiredDirection.x, desiredDirection.z);
            Vector2 f = new Vector2(facing.x, facing.z);
            if (d.sqrMagnitude < 1e-6f || f.sqrMagnitude < 1e-6f) return false;
            return Vector2.Angle(d, f) <= maxHeadingDegrees;
        }

        private bool _hasRunEntryPhase;
        private float _runEntryPhase;

        /// <summary>run loop 烘焙腳相曲線的左腳觸地極值（normalized）；第一次需要時從 Bake 算一次並快取。</summary>
        private bool TryGetRunEntryPhase(out float normalizedTime)
        {
            if (!_hasRunEntryPhase && runLoopBakeData != null &&
                runLoopBakeData.TryGetFootPhaseExtremeNormalizedTime(FootPhase.LeftFootDown, out _runEntryPhase))
            {
                _hasRunEntryPhase = true;
            }
            normalizedTime = _runEntryPhase;
            return _hasRunEntryPhase;
        }

        /// <summary>
        /// ADR-010：本帧的停步預測快照。兩個區塊各有自己的有效條件（D1-R1）：
        /// 結束站姿（<c>Active</c>）要等播放頭已前進；next plant（<c>HasNextPlant</c>）自 variant committed 起即可發布。
        /// 兩者都不成立 ⇒ 保持 default。
        /// </summary>
        private void PublishStopPrediction(PlayerRuntimeData data, MotionBakeData bakeData)
        {
            FootPlantSchedule schedule = bakeData != null ? bakeData.FootPlantSchedule : default;
            float duration = bakeData != null ? bakeData.Duration : 0f;
            if (!schedule.IsValid || duration <= 0f) return;

            // 播放頭尚未前進（pending／等待播放頭）⇒ clip 尚未開始：以 clip 0 為基準、沒有實測的 clip 前進量。
            bool playheadKnown = _stop.IsPlaying && _stop.HasPlaybackRate;
            float currentClipTime = playheadKnown ? Mathf.Clamp01(_stop.NormalizedTime) * duration : 0f;
            if (!(currentClipTime < duration)) return;
            float publishClipTime = playheadKnown ? Mathf.Clamp01(_stop.PreviousNormalizedTime) * duration : 0f;

            float travelledAtPublish = bakeData.GetLocomotionStopHorizontalDisplacementAt(publishClipTime);
            var prediction = new LocomotionStopPrediction
            {
                RootWorldPositionAtPublish = transform.position,
                RootYawDegreesAtPublish = transform.eulerAngles.y,
            };
            if (playheadKnown)
            {
                float totalTravel = bakeData.GetLocomotionStopHorizontalDisplacementAt(duration);
                prediction.Active = true;
                prediction.RemainingForwardDisplacement = Mathf.Max(0f, totalTravel - travelledAtPublish);
                prediction.EndLeftAnkleInRoot = schedule.EndPose.LeftAnkleInRoot;
                prediction.EndRightAnkleInRoot = schedule.EndPose.RightAnkleInRoot;
            }

            // ADR-010 D5：下一次 planned plant（排程依 clip 時間排序）。位置換到 publish root 座標系：
            // root 在 publish → plant 之間沿 publish 朝向前進（與結束站姿同一假設），故 ＝ 該 plant 的 ankle-in-root ＋ (0, 0, 前進量)。
            // 時間基準（docs/45 §13）：root 的位置對應 PreviousNormalizedTime，畫面上的姿勢對應 NormalizedTime（Animator 的評估比 root 早一次）。
            // ⇒ 姿勢顯示到 plant 的那一刻，root 只走到 plant − 一次評估的 clip 前進量；前進量取本帧實測值，不是常數。
            float clipStepPerEvaluation = Mathf.Max(0f, currentClipTime - publishClipTime);
            int eventCount = schedule.ValidEventCount;
            for (int i = 0; i < eventCount; i++)
            {
                FootPlantScheduleEvent plant = schedule.GetEvent(i);
                if (plant.ClipTime <= currentClipTime + 1e-5f) continue;
                float rootClipTimeAtPlant = Mathf.Max(publishClipTime, plant.ClipTime - clipStepPerEvaluation);
                float travel = Mathf.Max(0f, bakeData.GetLocomotionStopHorizontalDisplacementAt(rootClipTimeAtPlant) - travelledAtPublish);
                prediction.HasNextPlant = true;
                prediction.NextPlantIsLeft = plant.Side == FootPlantSide.Left;
                prediction.NextPlantAnkleInPublishRoot = plant.AnkleInRoot + new Vector3(0f, 0f, travel);
                prediction.NextPlantIsFinal = i == eventCount - 1;
                break;
            }
            if (prediction.Active || prediction.HasNextPlant) data.LocomotionStopPrediction = prediction;
        }

        private void ApplyCombatDirectionalSpeedLimit(
            PlayerRuntimeData data, ref MovementIntentData movementIntent)
        {
            if (combatDirectionalSpeedProfile == null || !data.CombatContext.InCombat ||
                movementIntent.DesiredDirection.sqrMagnitude <=
                LocomotionSpeedSmoother.Epsilon * LocomotionSpeedSmoother.Epsilon)
            {
                return;
            }

            Vector3 local = Quaternion.Inverse(transform.rotation) * movementIntent.DesiredDirection;
            float directionalLimit = combatDirectionalSpeedProfile.ResolveNormalizedSpeed(
                new Vector2(local.x, local.z));
            movementIntent.DesiredSpeedNormalized = Mathf.Min(
                movementIntent.DesiredSpeedNormalized, directionalLimit);
        }

        /// <summary>
        /// 將世界移動方向投影到角色本地 XZ，再以平滑後速度縮放。
        /// 只依賴值型別輸入，供 2D locomotion 參數與 EditMode 測試共用，執行期零配置。
        /// </summary>
        internal static Vector2 ProjectMoveParameters(
            Quaternion actorRotation, Vector3 worldMoveDirection, float speed)
        {
            // Quaternion inverse 與 Transform.InverseTransformDirection 等價：兩者都只反轉旋轉、忽略縮放。
            Vector3 local = Quaternion.Inverse(actorRotation) * worldMoveDirection;
            return new Vector2(local.x * speed, local.z * speed);
        }

        /// <inheritdoc />
        public bool TryGetCurrentFootPhase(MotionBakeData loopBakeData, out FootPhase phase)
        {
            phase = default;
            if (!_hasLocomotionTimeSnapshot || loopBakeData == null || loopBakeData.Duration <= 0f ||
                loopBakeData.FootPhaseCurve == null || loopBakeData.FootPhaseCurve.length == 0)
            {
                return false;
            }

            float loopTime = Mathf.Repeat(_locomotionNormalizedTime, 1f) * loopBakeData.Duration;
            phase = loopBakeData.GetFootPhaseAt(loopTime);
            return true;
        }

        /// <inheritdoc />
        public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data)
        {
            // 只有 ambient Idle/Move 會 delegate 到此，故不需讓 model 認識 StateType。
            _lastMotionFrame = Time.frameCount;

            // 播放頭尚未前進時 Δt_clip＝0，曲線驅動只會產生 0 位移（docs/07 R8）⇒ 沿用 release-entry 位移。
            if (_stop.IsPlaying && !_stop.IsAwaitingPlayhead && TryGetActiveVariant(out LocomotionStopVariant variant))
            {
                // 停步仍屬 locomotion：走專用入口，讓樓梯貼地輔助在停步滑行期間同樣生效（docs/44 §9.4）。
                motionDriver.ExecuteLocomotionStopMovement(
                    variant.BakeData, _stop.NormalizedTime, _stop.PreviousNormalizedTime, data);
                return;
            }

            motionDriver.ExecuteBaseMovement(data);
        }

        private void TickActiveStop(
            PlayerRuntimeData data,
            AnimationFacadeBase animationFacade,
            float deltaTime,
            bool isIntending,
            bool hadAmbientMotionLastFrame)
        {
            if (deltaTime <= 0f) return;

            // Jump/Roll 已接管時只讓舊 Stop 失效，絕不能 Play Locomotion 蓋掉新狀態。
            if (!hadAmbientMotionLastFrame)
            {
                _stop.Invalidate();
                return;
            }

            if (isIntending || !data.IsGrounded || animationFacade == null)
            {
                InvalidateStop(animationFacade, true);
                return;
            }

            if (_stop.IsPending)
            {
                TickPendingStop(animationFacade, deltaTime);
                return;
            }

            // Callback 只設旗標，統一在 Tick 仲裁，避免與 FSM 狀態切換競態。
            if (_stop.CompletionRequested)
            {
                InvalidateStop(animationFacade, true);
                return;
            }

            if (!TryGetActiveVariant(out LocomotionStopVariant variant))
            {
                InvalidateStop(animationFacade, true);
                return;
            }

            _stop.Advance(animationFacade.GetNormalizedTime(), deltaTime);
            if (_stop.HasTimedOut(variant.BakeData.Duration, stopTimeoutMargin))
            {
                InvalidateStop(animationFacade, true);
            }
        }

        private void TickPendingStop(AnimationFacadeBase animationFacade, float deltaTime)
        {
            _stop.AdvancePending(deltaTime);
            if (!TryGetStopConfiguration(_stop.Tier, out MotionBakeData loopBakeData, out _) ||
                !TryGetCurrentLoopTime(animationFacade, loopBakeData, out float normalizedTime) ||
                normalizedTime >= _stop.TargetNormalizedTime - PhaseMatchTolerance ||
                _stop.HasPendingTimedOut(PhaseWaitTimeout))
            {
                StartStopPlayback(animationFacade);
            }
        }

        private void TryStartStop(AnimationFacadeBase animationFacade, LocomotionStopTier tier)
        {
            if (animationFacade == null || string.IsNullOrEmpty(locomotionAnimationKey)) return;
            if (!TryGetStopConfiguration(tier, out MotionBakeData loopBakeData, out LocomotionStopVariant[] variants)) return;

            // 本輪只修 Walk。Run 已完成 Play 驗收，維持既有立即選片，不讓 Walk 手感修正擴散。
            if (tier == LocomotionStopTier.Walk &&
                TryGetCurrentLoopTime(animationFacade, loopBakeData, out float currentNormalizedTime) &&
                LocomotionStopSelector.TrySelectNearestFuturePhaseMatch(
                    loopBakeData, variants, currentNormalizedTime, PhaseMatchTolerance,
                    out int matchedVariantIndex, out float targetNormalizedTime))
            {
                _stop.BeginPending(tier, matchedVariantIndex, targetNormalizedTime);
                if (targetNormalizedTime <= currentNormalizedTime + PhaseMatchTolerance)
                {
                    StartStopPlayback(animationFacade);
                }
                return;
            }

            int variantIndex;
            if (TryGetCurrentPhase(animationFacade, loopBakeData, out FootPhase currentPhase))
            {
                variantIndex = LocomotionStopSelector.SelectByEntryPhase(variants, currentPhase);
            }
            else
            {
                int fallbackStart = tier == LocomotionStopTier.Walk
                    ? _nextWalkFallbackVariantIndex
                    : _nextRunFallbackVariantIndex;
                variantIndex = LocomotionStopSelector.SelectNextValid(variants, fallbackStart);
                if (variantIndex >= 0)
                {
                    if (tier == LocomotionStopTier.Walk) _nextWalkFallbackVariantIndex = variantIndex + 1;
                    else _nextRunFallbackVariantIndex = variantIndex + 1;
                }

#if UNITY_EDITOR
                WarnMissingPhaseSource(tier);
#endif
            }

            if (variantIndex < 0 || variants == null || variantIndex >= variants.Length)
            {
#if UNITY_EDITOR
                WarnInvalidConfiguration(tier);
#endif
                return;
            }

            _stop.Begin(tier, variantIndex);
            StartStopPlayback(animationFacade);
        }

        private void StartStopPlayback(AnimationFacadeBase animationFacade)
        {
            if (!_stop.IsActive || !TryGetActiveVariant(out LocomotionStopVariant variant))
            {
                InvalidateStop(animationFacade, true);
                return;
            }

            if (_stop.IsPending) _stop.StartPlaying();
            int generation = _stop.Generation;
            animationFacade.PlayWithCallback(variant.AnimationKey, () =>
            {
                _stop.TryRequestCompletion(generation);
            });

            // void 播放 API 無法回報 mapping 失敗；用既有通用查詢接住。
            if (!animationFacade.IsPlaying(variant.AnimationKey))
            {
                InvalidateStop(animationFacade, true);
                return;
            }

            _stop.Advance(animationFacade.GetNormalizedTime(), 0f);
        }

        private bool TryGetCurrentPhase(
            AnimationFacadeBase animationFacade,
            MotionBakeData loopBakeData,
            out FootPhase phase)
        {
            // CaptureLocomotionTime 與 Jump 共用同一份播放頭快照；Stop 與 Jump 因而不會各自
            // 查一次動畫圖、在同一幀得到不同答案。animationFacade 參數保留既有私有 API 形狀。
            return TryGetCurrentFootPhase(loopBakeData, out phase);
        }

        private bool TryGetCurrentLoopTime(
            AnimationFacadeBase animationFacade,
            MotionBakeData loopBakeData,
            out float normalizedTime)
        {
            normalizedTime = _locomotionNormalizedTime;
            if (loopBakeData == null || loopBakeData.Duration <= 0f ||
                loopBakeData.FootPhaseCurve == null || loopBakeData.FootPhaseCurve.length == 0 ||
                !_hasLocomotionTimeSnapshot)
            {
                return false;
            }

            return true;
        }

        private void CaptureLocomotionTime(AnimationFacadeBase animationFacade)
        {
            _hasLocomotionTimeSnapshot = false;
            _locomotionNormalizedTime = 0f;

            if (animationFacade == null || string.IsNullOrEmpty(locomotionAnimationKey) ||
                !animationFacade.IsPlaying(locomotionAnimationKey) ||
                !animationFacade.TryGetDominantChildNormalizedTime(
                    locomotionAnimationKey, out float normalizedTime) ||
                float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
            {
                return;
            }

            _locomotionNormalizedTime = normalizedTime;
            _hasLocomotionTimeSnapshot = true;
        }

        /// <summary>（純函數）Movement 層的停步 tier → 黑板的 <see cref="CommittedStopTier"/>（ADR-010 D6）。</summary>
        internal static CommittedStopTier ToCommittedStopTier(LocomotionStopTier tier) =>
            tier == LocomotionStopTier.Walk ? CommittedStopTier.Walk
            : tier == LocomotionStopTier.Run ? CommittedStopTier.Run
            : CommittedStopTier.None;

        private bool TryGetActiveVariant(out LocomotionStopVariant variant)
        {
            variant = default;
            if (!_stop.IsActive ||
                !TryGetStopConfiguration(_stop.Tier, out _, out LocomotionStopVariant[] variants) ||
                variants == null || _stop.VariantIndex < 0 || _stop.VariantIndex >= variants.Length)
            {
                return false;
            }

            variant = variants[_stop.VariantIndex];
            return variant.IsValid;
        }

        private bool TryGetStopConfiguration(
            LocomotionStopTier tier,
            out MotionBakeData loopBakeData,
            out LocomotionStopVariant[] variants)
        {
            if (tier == LocomotionStopTier.Walk)
            {
                loopBakeData = walkLoopBakeData;
                variants = walkStopVariants;
                return true;
            }

            if (tier == LocomotionStopTier.Run)
            {
                loopBakeData = runLoopBakeData;
                variants = runStopVariants;
                return true;
            }

            loopBakeData = null;
            variants = null;
            return false;
        }

#if UNITY_EDITOR
        private void WarnMissingPhaseSource(LocomotionStopTier tier)
        {
            int bit = 1 << (int)tier;
            if ((_missingPhaseWarningMask & bit) != 0) return;
            _missingPhaseWarningMask |= bit;
            string tierName = tier == LocomotionStopTier.Walk ? "Walk" : "Run";
            Debug.LogWarning(
                $"[{gameObject.name}] 無法由 Locomotion 播放時間＋{tierName} FootPhaseCurve 取得腳相，" +
                $"{tierName} Stop 暫以有效變體交替選片。請檢查對應 loopBakeData 與 locomotionAnimationKey。",
                this);
        }

        private void WarnInvalidConfiguration(LocomotionStopTier tier)
        {
            int bit = 1 << (int)tier;
            if ((_invalidConfigurationWarningMask & bit) != 0) return;
            _invalidConfigurationWarningMask |= bit;
            string tierName = tier == LocomotionStopTier.Walk ? "Walk" : "Run";
            Debug.LogWarning(
                $"[{gameObject.name}] 沒有可用的 {tierName} Stop 變體；維持既有 B9 收步。" +
                "請配置 MotionBakeData 與獨立 Animation Key。",
                this);
        }
#endif

        private void InvalidateStop(AnimationFacadeBase animationFacade, bool restoreLocomotion)
        {
            if (!_stop.IsActive) return;
            _stop.Invalidate();

            if (restoreLocomotion && animationFacade != null && !string.IsNullOrEmpty(locomotionAnimationKey))
            {
                animationFacade.Play(locomotionAnimationKey);
            }
        }

        private void OnDisable()
        {
            _stop.Invalidate();
            _wasIntending = false;
            _lastMotionFrame = int.MinValue;
        }
    }
}
