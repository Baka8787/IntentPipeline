using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Presentation.Animation;
using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.StateMachine
{
    /// <summary>
    /// 承載角色所有滯空時間，包含主動起跳與非主動失地後的 Falling。
    /// <c>Jump</c> 這個名字與承載範圍有落差，但這是命名問題、不是結構問題；本輪刻意不新增
    /// <c>Falling</c>／<c>Airborne</c> StateType，也刻意不改名。
    /// </summary>
    public class JumpState : BaseState
    {
        private enum AnimationPhase
        {
            Start,
            Falling,
            Land
        }

        // 落地 gameplay phase 只屬於 JumpState，不進黑板、不擴成全域狀態。
        // NormalStop 可被落地後新出現的移動意圖提前結束；NormalContinue 保留既有 Land2Move；
        // HardRecovery 在 phase duration 內鎖住玩家水平位移。
        private enum LandingPhase
        {
            None,
            NormalStop,
            NormalContinue,
            HardRecovery
        }

        public override StateType Type => StateType.Jump;
        public override string AnimationKey => _currentAnimationKey ?? base.AnimationKey;

        // 🆕（ADR-002）程式碼內建安全退化值：查無 Stages / 該段無可信烘焙資料時使用。
        private const float FallbackInitialVelocity = 7.5f; // m/s
        private const float FallbackGravity = 9.81f;        // m/s²（正值大小）

        // 落地判定保留一段最短滯空保護時間，避免離地瞬間 isGrounded 尚未切換成 false 就被誤判為已落地。
        private const float MinAirborneTimeBeforeLandingCheck = 0.15f;
        private const float UngroundedNever = -1f;

        // 🆕（ADR-002）逐段預算的發射資料與起跳前搖，於 Initialize 依各段 MotionBakeData + 三個倍率算好。
        // 皆為一次性配置（非每幀），執行期只讀取，符合 Zero-GC。
        private JumpLaunchData[] _stageLaunch;
        private float[] _stageTakeoffDelay;
        private int _stageCount;
        private JumpStateParams _jumpParams;

        // 執行期狀態（皆為狀態內部，不進黑板）
        private int _jumpIndex;          // 本次離地後的當前段（0 = 地面跳）
        private float _stageElapsedTime;  // 當前段自「開始」以來的經過時間，用來對齊該段起跳前搖
        private float _airborneTimer;     // 真正離地後的滯空計時，用於落地保護
        private bool _isVelocityInjected; // 當前段是否已注入發射衝量
        private float _ungroundedSince = UngroundedNever; // 非主動失地的起始時刻（Time.time 快照）
        private bool _enterAsFall;        // 本幀 CanEnter 的裁決：這次進入是非主動失地
        private bool _enteredFromFall;    // 本次 JumpState 執行是否源自非主動失地

        // 純視覺相位狀態：AnimationKey 永遠只指向 authored data 內已存在的字串參考，
        // 每幀 getter 不組字串、不 ToString，守住管線順序 5 的零 GC 契約（A23）。
        private AnimationPhase _animationPhase;
        private string _currentAnimationKey;
        private LocomotionStopTier _takeoffTier;
        private FootPhase _takeoffFootPhase;
        private bool _hasTakeoffFootPhase;
        private LandingPhase _landingPhase;
        private float _landElapsedTime;
        private float _landDuration;

        public bool IsLanded { get; private set; }
        public override bool CanTransitionAway
            => IsLanded && (_landingPhase == LandingPhase.None || _landElapsedTime >= _landDuration);

        /// <summary>
        /// 🆕（ADR-002）從 Config 以泛型安全查表拿取 JumpStateParams，逐段預算 JumpLaunchData 與起跳前搖。
        /// 物理量全部來自各段 MotionBakeData（單一真相）；查無綁定或該段無可信資料時安全退化。
        /// </summary>
        public override void Initialize(StateMachineConfigSO config, IMovementModel movementModel)
        {
            base.Initialize(config, movementModel);

            var jumpParams = config != null ? config.GetStateParams<JumpStateParams>(Type) : null;
            _jumpParams = jumpParams;

#if UNITY_EDITOR
            // 🆕（M1 收案輪）設計問題提示（非限制），比照 RollState 斷鏈防線＋M2 Warning 治理原則：
            // 查無 JumpStateParams 時跳躍仍可運作（BuildStages 合成 fallback 段），但物理量與動畫烘焙
            // 數據完全脫鉤——高度/重力用硬編碼、前搖 0s，可能重現「先蹲下再往上」的時間軸不同步。
            // GetStateParams<T> 在「未綁定／引用失效／資產型別不符」三種情境都靜默回 null（v0.10 已知
            // 防呆缺口），本警告即該防呆的輕量落地（專用 Editor 驗證工具依雙 Gate 評估不建）。
            // Application.isPlaying：EditMode 測試以最小拓撲 config 組裝屬合法輸入，不誤鳴；
            // Player build 整段由 UNITY_EDITOR 排除，Play 偵測力零損失。
            if (jumpParams == null && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[JumpState] 查無 {Type} 的 JumpStateParams（StateMachineConfig 的 paramsMappings 未綁定、引用已失效、或資產型別不符）。" +
                    $"跳躍將退化為硬編碼預設（初速 {FallbackInitialVelocity} m/s、重力 {FallbackGravity} m/s²、前搖 0s），" +
                    "與動畫烘焙數據脫鉤（apex 高度不符、起跳時機可能與預備動作不同步）。" +
                    "請檢查 Config.paramsMappings 是否綁定有效的 JumpStateParams 資產。");
            }
#endif

            BuildStages(jumpParams);
        }

        /// <summary>
        /// 依 Stages + 三個 Designer Tuning 倍率，逐段逆推發射初速（v = √(2gh)）並快取。
        /// </summary>
        private void BuildStages(JumpStateParams jumpParams)
        {
            var stages = jumpParams != null ? jumpParams.Stages : null;
            int count = stages != null ? stages.Count : 0;

            float heightMul = jumpParams != null ? jumpParams.HeightMultiplier : 1f;
            float gravityMul = jumpParams != null ? jumpParams.GravityMultiplier : 1f;
            float velMul = jumpParams != null ? jumpParams.LaunchVelocityMultiplier : 1f;

            // 安全退化：沒有配置 Stages 時，合成一段 fallback，讓跳躍仍可運作而不是完全跳不起來。
            if (count <= 0)
            {
                _stageCount = 1;
                _stageLaunch = new JumpLaunchData[1];
                _stageTakeoffDelay = new float[1];
                _stageLaunch[0] = new JumpLaunchData(FallbackInitialVelocity * velMul, FallbackGravity * gravityMul);
                _stageTakeoffDelay[0] = 0f;
                return;
            }

            _stageCount = count;
            _stageLaunch = new JumpLaunchData[count];
            _stageTakeoffDelay = new float[count];

            for (int i = 0; i < count; i++)
            {
                MotionBakeData bake = stages[i].Bake;

                float delay = 0f;
                float launchVelocity;
                float gravity;

                if (bake != null && bake.AutoApexHeight > 0f && bake.AutoCalculatedGravity > 0f)
                {
                    delay = Mathf.Max(0f, bake.AutoTakeoffDelay);
                    gravity = bake.AutoCalculatedGravity * gravityMul;
                    float height = bake.AutoApexHeight * heightMul;
                    // v = √(2gh)：以套用倍率後的 g、h 逆推，再乘上初速倍率。
                    // 三個倍率皆為 1 時，apex 精準命中 AutoApexHeight（ADR-002 §2.3 自洽性）。
                    launchVelocity = Mathf.Sqrt(2f * gravity * height) * velMul;
                }
                else
                {
                    // 該段沒有可信烘焙資料（非跳躍/未烘焙/Bake Into Pose 採不到上升量）→ 安全退化。
                    gravity = FallbackGravity * gravityMul;
                    launchVelocity = FallbackInitialVelocity * velMul;
                }

                _stageLaunch[i] = new JumpLaunchData(launchVelocity, gravity);
                _stageTakeoffDelay[i] = delay;
            }
        }

        public override bool CanEnter(PlayerRuntimeData data)
        {
            // 主動起跳的資格維持「按下跳躍且已著地」；grace 絕不放寬成 Coyote Time。
            if (data.Intent.JumpRequested && data.IsGrounded)
            {
                _ungroundedSince = UngroundedNever;
                _enterAsFall = false;
                return true;
            }

            float now = Time.time;
            _ungroundedSince = TrackUngroundedSince(data.IsGrounded, _ungroundedSince, now);
            if (data.IsGrounded)
            {
                _enterAsFall = false;
                return false;
            }

            float fallEntryGrace = _jumpParams != null
                ? _jumpParams.FallEntryGrace
                : JumpStateParams.DefaultFallEntryGrace;
            _enterAsFall = ShouldEnterFallFromLostGround(
                data.IsGrounded, _ungroundedSince, now, fallEntryGrace);
            return _enterAsFall;
        }

        public override void OnEnter(PlayerRuntimeData data)
        {
#if UNITY_EDITOR
            if (_enterAsFall)
            {
                Debug.Log("<color=yellow>[State] 進入 JUMP 狀態的非主動 Falling；不注入起跳速度！</color>");
            }
            else
            {
                Debug.Log("<color=yellow>[State] 進入 JUMP 狀態！等待該段起跳前搖後注入垂直初速度！</color>");
            }
#endif
            IsLanded = false;
            _jumpIndex = 0;              // 地面跳 = 第 0 段
            _stageElapsedTime = 0f;
            _currentAnimationKey = base.AnimationKey;
            _landingPhase = LandingPhase.None;
            _landElapsedTime = 0f;
            _landDuration = 0f;

            // FullBodyStateMachine 的兩條入口都保證本幀先 CanEnter、再立即 TransitionTo；因此
            // _enterAsFall 到 OnEnter 時必然是本次已提交的裁決，不會殘留。OnEnter 刻意不從 data
            // 重新推導進入原因，避免下游重算已承諾決策的反模式。
            if (_enterAsFall)
            {
                _isVelocityInjected = true; // 語意：本次滯空不會再有任何 launch 注入
                _airborneTimer = MinAirborneTimeBeforeLandingCheck;
                _enteredFromFall = true;
                SnapshotTakeoffContext(data);
                EnterFallingPhase();
                return;
            }

            _airborneTimer = 0f;
            _isVelocityInjected = false; // 離地時刻由 OnUpdateMotion 依當前段前搖決定
            _enteredFromFall = false;
            EnterStartPhase(data);
        }

        public override void OnTick(PlayerRuntimeData data, float deltaTime)
        {
            _stageElapsedTime += deltaTime;

            if (IsLanded)
            {
                _landElapsedTime += deltaTime;

                // 只允許「落地當幀沒有移動」的 NormalStop 被後續新意圖提前結束。
                // NormalContinue 代表落地當幀已選中 Land2Move；持續同一意圖不得在下一幀跳過它。
                // HardRecovery 同樣不接受移動意圖提前中斷，必須等 gameplay recovery duration。
                if (_landingPhase == LandingPhase.NormalStop && HasEffectiveMoveIntent(data))
                {
                    _landingPhase = LandingPhase.None;
                }
                return;
            }

            // 🆕（ADR-002）空中再按跳：在 JumpState 內部消化（interrupt 系統不自我重入，無法靠狀態轉移）。
            // JumpButtonDown = WasPressedThisFrame（邊沿），Intent 每幀復位 → 一次按壓只推進一段。
            // 非主動失地不得憑空取得空中跳；否則會重新打開 ADR-002 已封住的無限空中跳漏洞。
            if (!_enteredFromFall && _isVelocityInjected && data.Intent.JumpRequested && _jumpIndex + 1 < _stageCount)
            {
                _jumpIndex++;
                _isVelocityInjected = false; // 下一段的注入交給 OnUpdateMotion 依該段前搖點火
                _stageElapsedTime = 0f;       // 重新計時，對齊下一段的起跳前搖
                EnterStartPhase(data);
                return;                       // 本幀已推進，暫不做落地判定
            }

            // 尚未真正離地（仍在當前段的前搖階段）時不判定落地，避免貼地站著被 IsGrounded 誤判成已落地。
            if (!_isVelocityInjected) return;

            JumpLaunchData launch = _stageLaunch[_jumpIndex];
            if (_animationPhase == AnimationPhase.Start && ShouldEnterFalling(
                    launch.InitialVerticalVelocity,
                    launch.Gravity,
                    _stageElapsedTime,
                    _stageTakeoffDelay[_jumpIndex],
                    IsLanded))
            {
                EnterFallingPhase();
            }

            _airborneTimer += deltaTime;
            if (_airborneTimer >= MinAirborneTimeBeforeLandingCheck && data.IsGrounded)
            {
                IsLanded = true;
                EnterLandPhase(data);
#if UNITY_EDITOR
                Debug.Log("<color=orange>[State] JUMP 偵測到真實落地（IsGrounded == true）</color>");
#endif
            }
        }

        public override void OnExit(PlayerRuntimeData data)
        {
            IsLanded = false;
            _isVelocityInjected = false;
            _jumpIndex = 0;
            _currentAnimationKey = null;
            _landingPhase = LandingPhase.None;
            _landElapsedTime = 0f;
            _landDuration = 0f;
            _enteredFromFall = false;
            _enterAsFall = false;
            _ungroundedSince = UngroundedNever;
        }

        // 🆕（ADR-002）當前段前搖結束才點火，注入該段逆推的初速 + 重力；其餘時間走一般貼地移動。
        public override void OnUpdateMotion(MotionDriver motionDriver, AnimationFacadeBase animationFacade, PlayerRuntimeData data)
        {
            if (!_isVelocityInjected && _stageElapsedTime >= _stageTakeoffDelay[_jumpIndex])
            {
                motionDriver.ApplyJumpLaunch(_stageLaunch[_jumpIndex]);
                _isVelocityInjected = true;
                _airborneTimer = 0f; // 從真正離地那一刻開始重新計時滯空保護
            }

            // HardRecovery 是 gameplay phase：保留 MotionDriver 的 facing／重力／grounded／collision 更新，
            // 但不施加 Movement Output 的水平速度。不得藉由清空 MoveDirection 破壞黑板單一寫入者。
            if (_landingPhase == LandingPhase.HardRecovery)
            {
                motionDriver.ExecuteVerticalOnlyMovement(data);
                return;
            }

            // 前搖與空中維持既有空中控制；Normal Land／Land2Move 也維持原本的水平運動行為。
            motionDriver.ExecuteBaseMovement(data);
        }

        private void EnterStartPhase(PlayerRuntimeData data)
        {
            _animationPhase = AnimationPhase.Start;
            LocomotionStopVariant variant = SnapshotTakeoffContext(data);
            if (!variant.IsValid) return;

            _currentAnimationKey = variant.AnimationKey;
        }

        private LocomotionStopVariant SnapshotTakeoffContext(PlayerRuntimeData data)
        {
            _takeoffTier = LocomotionStopTier.None;
            _hasTakeoffFootPhase = false;

            JumpAnimationVariantTable table = _jumpParams != null
                ? _jumpParams.AnimationVariants
                : default;
            float moveThreshold = _jumpParams != null ? _jumpParams.MovementIntentThreshold : 0.2f;
            float runThreshold = _jumpParams != null ? _jumpParams.RunIntentThreshold : 0.75f;
            LocomotionStopTier requestedTier = SelectSpeedTier(
                data.MovementIntent.DesiredSpeedNormalized, moveThreshold, runThreshold);

            bool hasCurrentPhase = TryGetCurrentLocomotionPhase(requestedTier, out FootPhase currentPhase);
            LocomotionStopVariant variant = SelectVariant(
                table.Start, requestedTier, hasCurrentPhase, currentPhase);
            if (!variant.IsValid) return variant;

            // Walk／Run 只有在真的由 locomotion 播放頭選到腳相時才承諾家族與腳；來源缺席時
            // SelectVariant 會依 D3 退到 Idle，落地也必須跟著走 Idle 家族，不能假裝選過 LU／RU。
            if (requestedTier != LocomotionStopTier.None && hasCurrentPhase)
            {
                _takeoffTier = requestedTier;
                _takeoffFootPhase = variant.BakeData.GetFootPhaseAt(0f);
                _hasTakeoffFootPhase = true;
            }

            return variant;
        }

        private void EnterFallingPhase()
        {
            _animationPhase = AnimationPhase.Falling;

            JumpAnimationVariantTable table = _jumpParams != null
                ? _jumpParams.AnimationVariants
                : default;
            _currentAnimationKey = ResolveAnimationKey(table.Falling, _currentAnimationKey);
        }

        private void EnterLandPhase(PlayerRuntimeData data)
        {
            _animationPhase = AnimationPhase.Land;

            JumpAnimationVariantTable table = _jumpParams != null
                ? _jumpParams.AnimationVariants
                : default;
            float moveThreshold = _jumpParams != null ? _jumpParams.MovementIntentThreshold : 0.2f;
            float hardLandingSpeed = _jumpParams != null
                ? _jumpParams.HardLandingSpeed
                : JumpStateParams.DefaultHardLandingSpeed;
            bool continueMovement = ShouldContinueMovement(
                data.MovementIntent.DesiredSpeedNormalized, moveThreshold);
            // 分類只讀 MotionDriver 發布的權威實際垂直速度，讓主動 Jump、walk-off falling 與
            // 未來擊退都走同一來源；舊公式假設已知 launch，無法描述沒有發射初速的非主動失地。
            // ⚠️ 此讀取只在第一個 grounded 幀正確，之後發布值已是 reboundForce。兩條入口都守住：
            // 主動跳躍的滯空遠超 MinAirborneTimeBeforeLandingCheck；非主動失地進入時則預先滿足計時器。
            float landingVerticalVelocity = data.VerticalVelocity;
            _landingPhase = ResolveLandingPhase(
                landingVerticalVelocity,
                hardLandingSpeed,
                continueMovement);
            LocomotionStopVariant variant = SelectLandVariant(
                table,
                _takeoffTier,
                _hasTakeoffFootPhase,
                _takeoffFootPhase,
                _landingPhase);

            float fallbackDuration = _jumpParams != null ? _jumpParams.LandFallbackDuration : 0f;
            _landElapsedTime = 0f;

            if (!variant.IsValid)
            {
                // Normal Land 格缺席時維持切片前的立即退場；HardRecovery 則不能因動畫引用失效
                // 就失去 gameplay 鎖定，至少使用既有 fallback duration 完成 recovery。
                _landDuration = _landingPhase == LandingPhase.HardRecovery
                    ? Mathf.Max(0f, fallbackDuration)
                    : 0f;
                if (_landDuration <= 0f) _landingPhase = LandingPhase.None;
                return;
            }

            _currentAnimationKey = variant.AnimationKey;
            _landDuration = ResolveLandDuration(variant, fallbackDuration);
            if (_landDuration <= 0f) _landingPhase = LandingPhase.None;

            // ⚠️ 進入 Land 完全由 IsGrounded 驅動；phase duration 由 JumpState 計時並擁有。
            // Bake duration 只是目前沿用的 authored 秒數來源，不是 Animation callback 或 clip 播完事件。
            // 對 HardRecovery 而言，這段時間是 gameplay recovery；動畫只負責表現它。
        }

        private bool HasEffectiveMoveIntent(PlayerRuntimeData data)
        {
            if (data == null) return false;
            float moveThreshold = _jumpParams != null ? _jumpParams.MovementIntentThreshold : 0.2f;
            return ShouldContinueMovement(data.MovementIntent.DesiredSpeedNormalized, moveThreshold);
        }

        private bool TryGetCurrentLocomotionPhase(LocomotionStopTier tier, out FootPhase phase)
        {
            phase = default;
            if (tier == LocomotionStopTier.None || _jumpParams == null) return false;

            IFootPhaseSource source = MovementModel as IFootPhaseSource;
            if (source == null) return false;

            MotionBakeData loopBakeData = tier == LocomotionStopTier.Walk
                ? _jumpParams.WalkLoopBakeData
                : _jumpParams.RunLoopBakeData;
            return source.TryGetCurrentFootPhase(loopBakeData, out phase);
        }

        /// <summary>
        /// 維護非主動失地的起始快照。重複 CanEnter 只保留第一次的 now，著地則清回 sentinel，
        /// 因此同幀被 EvaluateInterrupts／EvaluateTransitions 各問一次也不會重複累積時間。
        /// </summary>
        internal static float TrackUngroundedSince(
            bool isGrounded,
            float ungroundedSince,
            float now)
        {
            if (isGrounded) return UngroundedNever;
            return ungroundedSince == UngroundedNever ? now : ungroundedSince;
        }

        /// <summary>
        /// 非主動失地只有在連續未著地時間達 grace 後才准入；此規則只過濾 grounded 抖動，
        /// 不接受跳躍意圖，也不構成 Coyote Time。
        /// </summary>
        internal static bool ShouldEnterFallFromLostGround(
            bool isGrounded,
            float ungroundedSince,
            float now,
            float fallEntryGrace)
        {
            if (isGrounded || ungroundedSince == UngroundedNever) return false;
            return now - ungroundedSince >= Mathf.Max(0f, fallEntryGrace);
        }

        /// <summary>
        /// 用 JumpState 自己擁有的拋體參數推導下降起點。簽名刻意沒有 clip duration：
        /// Start→Falling 是物理條件，不是播放完成條件。
        /// </summary>
        internal static bool ShouldEnterFalling(
            float initialVerticalVelocity,
            float gravity,
            float elapsedTime,
            float takeoffDelay,
            bool isLanded)
        {
            if (isLanded || gravity <= 0f) return false;

            float airborneElapsed = elapsedTime - Mathf.Max(0f, takeoffDelay);
            if (airborneElapsed < 0f) return false;

            float verticalVelocity = CalculateVerticalVelocity(
                initialVerticalVelocity, gravity, elapsedTime, takeoffDelay);
            return verticalVelocity <= 0f;
        }

        /// <summary>
        /// 以當前段的 authored 拋體參數重建指定時刻的垂直速度。這不是第二份物理狀態：函式無跨幀儲存、
        /// 不寫黑板，只讓動畫分類與 MotionDriver 接到的同一份 <see cref="JumpLaunchData"/> 保持同源。
        /// </summary>
        internal static float CalculateVerticalVelocity(
            float initialVerticalVelocity,
            float gravity,
            float elapsedTime,
            float takeoffDelay)
        {
            float airborneElapsed = Mathf.Max(0f, elapsedTime - Mathf.Max(0f, takeoffDelay));
            return initialVerticalVelocity - gravity * airborneElapsed;
        }

        internal static LocomotionStopTier SelectSpeedTier(
            float desiredSpeedNormalized,
            float movementThreshold,
            float runThreshold)
        {
            float move = Mathf.Clamp01(movementThreshold);
            float run = Mathf.Max(move, Mathf.Clamp01(runThreshold));
            float desired = Mathf.Clamp01(desiredSpeedNormalized);

            if (desired <= move) return LocomotionStopTier.None;
            return desired >= run ? LocomotionStopTier.Run : LocomotionStopTier.Walk;
        }

        internal static bool ShouldContinueMovement(float desiredSpeedNormalized, float movementThreshold)
            => Mathf.Clamp01(desiredSpeedNormalized) > Mathf.Clamp01(movementThreshold);

        internal static LocomotionStopVariant SelectVariant(
            JumpAnimationVariantSet variants,
            LocomotionStopTier tier,
            bool hasFootPhase,
            FootPhase footPhase)
        {
            if (tier == LocomotionStopTier.None || !hasFootPhase) return variants.Idle;

            LocomotionStopVariant[] candidates = tier == LocomotionStopTier.Walk
                ? variants.Walk
                : variants.Run;
            int index = LocomotionStopSelector.SelectByEntryPhase(candidates, footPhase);
            if (index < 0) return default;

            LocomotionStopVariant selected = candidates[index];
            MotionBakeData selectedBake = selected.BakeData;
            // GetFootPhaseAt 本身能退到 EndPhase，但 Jump 規格要求 LU／RU 必須由連續 FootPhaseCurve
            // 證明；舊資產缺曲線時寧可讓整格失效，交由呼叫端保留現有鍵。
            return selectedBake != null && selectedBake.FootPhaseCurve != null &&
                   selectedBake.FootPhaseCurve.length > 0
                ? selected
                : default;
        }

        internal static LocomotionStopVariant SelectLandVariant(
            JumpAnimationVariantTable table,
            LocomotionStopTier takeoffTier,
            bool hasTakeoffFootPhase,
            FootPhase takeoffFootPhase,
            float landingVerticalVelocity,
            float hardLandingSpeed,
            bool continueMovement)
        {
            LandingPhase landingPhase = ResolveLandingPhase(
                landingVerticalVelocity,
                hardLandingSpeed,
                continueMovement);
            return SelectLandVariant(
                table,
                takeoffTier,
                hasTakeoffFootPhase,
                takeoffFootPhase,
                landingPhase);
        }

        private static LandingPhase ResolveLandingPhase(
            float landingVerticalVelocity,
            float hardLandingSpeed,
            bool continueMovement)
        {
            float threshold = Mathf.Max(0f, hardLandingSpeed);
            if (Mathf.Abs(landingVerticalVelocity) > threshold) return LandingPhase.HardRecovery;
            return continueMovement ? LandingPhase.NormalContinue : LandingPhase.NormalStop;
        }

        private static LocomotionStopVariant SelectLandVariant(
            JumpAnimationVariantTable table,
            LocomotionStopTier takeoffTier,
            bool hasTakeoffFootPhase,
            FootPhase takeoffFootPhase,
            LandingPhase landingPhase)
        {
            // 重落地的語意就是把動量吃掉、站定收住；一旦分類完成就必須短路，不能再讓
            // 速度 tier 或 LU／RU 把它導回「Hard 但續走」這個不存在的組合。
            if (landingPhase == LandingPhase.HardRecovery) return table.HardLand;

            JumpAnimationVariantSet variants = landingPhase == LandingPhase.NormalContinue
                ? table.NormalLandToMove
                : table.NormalLand;

            // 只有正常落地才讀起跳時快照；不查落地當下 locomotion，因此 tier／LU／RU 承諾不會中途換腳。
            return SelectVariant(variants, takeoffTier, hasTakeoffFootPhase, takeoffFootPhase);
        }

        internal static string ResolveAnimationKey(LocomotionStopVariant variant, string currentKey)
            => variant.IsValid ? variant.AnimationKey : currentKey;

        internal static float ResolveLandDuration(LocomotionStopVariant variant, float fallbackDuration)
        {
            float bakedDuration = variant.BakeData != null ? variant.BakeData.Duration : 0f;
            return bakedDuration > 0f ? bakedDuration : Mathf.Max(0f, fallbackDuration);
        }
    }
}
