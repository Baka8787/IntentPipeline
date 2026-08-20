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
    /// 3. **自驅動畫參數**（<c>SetFloat(MoveSpeed)</c>）——D4「每個 model 驅動自己的參數」
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
    /// **黑板 Movement Output 的語意**（2026-07-25 裁決）：<c>MoveSpeed</c>／<c>MoveDirection</c>／
    /// <c>UpperBodyWeight</c> 自本輪起**不再是 Runner 維護的 locomotion state**，而是
    /// **當下 active Movement Model 發布的 Movement Output**——消費端（MotionDriver、Jump 空中控制、
    /// Editor 監視器）讀到的是「模型算出來的運動」，寫入者唯一且為本檔。
    /// D4 的最終形態（欄位完全內化、不經黑板）目標不變，但需連動 MotionDriver API 與 Jump 空中控制，
    /// 留待後續階段；本輪刻意不一次做完（migration intermediate state，見 dev-spec §7.3）。
    /// </remarks>
    public class LocomotionModel : MonoBehaviour, IMovementModel
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

#if UNITY_EDITOR
        private int _missingPhaseWarningMask;
        private int _invalidConfigurationWarningMask;
#endif

        /// <inheritdoc />
        public bool IsProducingMotion => _smoother.Speed >= MoveThreshold || _stop.IsActive;

        /// <inheritdoc />
        public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime)
        {
            float desiredSpeed = Mathf.Clamp01(data.MovementIntent.DesiredSpeedNormalized);
            bool isIntending = desiredSpeed >= LocomotionSpeedSmoother.Epsilon;

            // Stop 的「入場強度」是放開發生前一刻的實際速度，不是放開後已被 SmoothDamp
            // 衰減一次的速度。先快照可避免 0.75 Run 在 60/120 FPS 首幀分別掉到約
            // 0.739/0.747，因而錯過下界 0.75；也讓 tier 選擇不受幀率影響。
            float releaseEntryIntensity = _smoother.Speed;

            bool hadAmbientMotionLastFrame = _lastMotionFrame == Time.frameCount - 1;
            if (_stop.IsActive)
            {
                TickActiveStop(data, animationFacade, deltaTime, isIntending, hadAmbientMotionLastFrame);
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
            if (!_stop.IsPending)
            {
                _smoother.Tick(in data.MovementIntent, moveSpeedAccelTime, moveSpeedDecelTime, deltaTime);
            }

            data.MoveSpeed = _smoother.Speed;
            data.MoveDirection = _smoother.Direction;
            data.UpperBodyWeight = _smoother.Speed > LocomotionSpeedSmoother.Epsilon ? 0.5f : 0.0f;

            // 🆕（ADR-003 D4）**model 自驅動畫參數**：由 Locomotion Transition 資產內的 ParameterName
            // 綁定驅動 1D Mixer 混合，本層不認識任何 Mixer（tier 門檻是資料，住在 Locomotion.asset）。
            // ⚠️ 驅動點刻意留在 Update（順序 3）而非 LateUpdate：Animator 評估卡在兩者之間，
            //    移到 LateUpdate 會讓動畫參數比位移晚一幀。
            if (animationFacade != null)
            {
                animationFacade.SetFloat(AnimationFacadeBase.ParamMoveSpeed, _smoother.Speed);
            }

            _wasIntending = isIntending;
        }

        /// <inheritdoc />
        public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data)
        {
            // 只有 ambient Idle/Move 會 delegate 到此，故不需讓 model 認識 StateType。
            _lastMotionFrame = Time.frameCount;

            if (_stop.IsPlaying && TryGetActiveVariant(out LocomotionStopVariant variant))
            {
                motionDriver.ExecuteBakedCurveMovement(
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
            phase = default;
            if (!TryGetCurrentLoopTime(animationFacade, loopBakeData, out float normalizedTime))
            {
                return false;
            }

            float loopTime = Mathf.Repeat(normalizedTime, 1f) * loopBakeData.Duration;
            phase = loopBakeData.GetFootPhaseAt(loopTime);
            return true;
        }

        private bool TryGetCurrentLoopTime(
            AnimationFacadeBase animationFacade,
            MotionBakeData loopBakeData,
            out float normalizedTime)
        {
            normalizedTime = 0f;
            if (loopBakeData == null || loopBakeData.Duration <= 0f ||
                loopBakeData.FootPhaseCurve == null || loopBakeData.FootPhaseCurve.length == 0 ||
                !animationFacade.IsPlaying(locomotionAnimationKey) ||
                !animationFacade.TryGetDominantChildNormalizedTime(
                    locomotionAnimationKey, out normalizedTime) ||
                float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime))
            {
                return false;
            }

            return true;
        }

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
