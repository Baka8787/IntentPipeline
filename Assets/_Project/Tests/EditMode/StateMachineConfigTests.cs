using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// <see cref="StateMachineConfigSO.GetStateParams{TParams}"/> 泛型安全查表的 EditMode 單元測試。
    /// 驗證 v0.11 落地的 StateParamsSO 機制三個契約：綁定正確型別回傳資產、
    /// 綁定錯誤型別靜默回傳 null（呼叫端 fallback 的前提）、未綁定狀態回傳 null。
    /// paramsMappings 為私有序列化欄位，比照 StateMachineTests 以反射注入，等同 Inspector 手動配置。
    /// 同檔亦守住 JumpStateParams 所承載的分段動畫選擇純函數；不另開測試檔，避免把同一份
    /// Jump authored data 契約拆成兩個尋找入口。
    /// </summary>
    public class StateMachineConfigTests
    {
        /// <summary>型別不符測試用的替身參數資產（模擬「Jump 狀態誤掛了別種 StateParamsSO」）。</summary>
        private sealed class WrongTypeParams : StateParamsSO { }

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private StateMachineConfigSO BuildConfig(params StateParamsMapping[] mappings)
        {
            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();
            _created.Add(config);

            FieldInfo mappingsField = typeof(StateMachineConfigSO)
                .GetField("paramsMappings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mappingsField, "找不到 StateMachineConfigSO.paramsMappings 私有欄位（欄位名稱可能已變更）");
            mappingsField.SetValue(config, new List<StateParamsMapping>(mappings));

            config.Initialize();
            return config;
        }

        private TParams CreateParams<TParams>() where TParams : StateParamsSO
        {
            var so = ScriptableObject.CreateInstance<TParams>();
            _created.Add(so);
            return so;
        }

        [Test]
        public void GetStateParams_BoundWithCorrectType_ReturnsSameAsset()
        {
            var jumpParams = CreateParams<JumpStateParams>();
            var config = BuildConfig(new StateParamsMapping { State = StateType.Jump, Params = jumpParams });

            var result = config.GetStateParams<JumpStateParams>(StateType.Jump);

            Assert.AreSame(jumpParams, result, "綁定正確型別時應回傳同一份資產參考");
        }

        [Test]
        public void GetStateParams_BoundWithWrongType_ReturnsNullForCallerFallback()
        {
            var wrongParams = CreateParams<WrongTypeParams>();
            var config = BuildConfig(new StateParamsMapping { State = StateType.Jump, Params = wrongParams });

            var result = config.GetStateParams<JumpStateParams>(StateType.Jump);

            Assert.IsNull(result, "型別不符時應靜默回傳 null，讓呼叫端 fallback 到程式碼內建預設值（規格既定行為）");
        }

        [Test]
        public void GetStateParams_UnboundState_ReturnsNull()
        {
            var config = BuildConfig(); // 不綁任何參數資產

            Assert.IsNull(config.GetStateParams<JumpStateParams>(StateType.Jump),
                "未綁定的狀態應回傳 null");
        }

        [Test]
        public void JumpAnimation_LandingSpeed_SelectsSameHardLandRegardlessOfMovement()
        {
            LocomotionStopVariant normal = CreateVariant("JumpIdleLand", 0.4f);
            LocomotionStopVariant hard = CreateVariant("JumpIdleLandHard", 0.6f);
            var table = new JumpAnimationVariantTable(
                default,
                default,
                new JumpAnimationVariantSet(normal, null, null),
                default,
                hard);

            LocomotionStopVariant stop = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Run, true, FootPhase.RightFootDown, -8.01f, 8f, false);
            LocomotionStopVariant continueMove = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Run, true, FootPhase.RightFootDown, -8.01f, 8f, true);

            Assert.AreEqual("JumpIdleLandHard", stop.AnimationKey);
            Assert.AreSame(stop.BakeData, continueMove.BakeData,
                "重落地必須在讀取移動意圖、tier 與腳相前短路到同一份 authored hard 變體");
            Assert.AreEqual(stop.AnimationKey, continueMove.AnimationKey,
                "重落地會吃掉動量，不存在 HardLandToMove 分流");
        }

        [Test]
        public void JumpAnimation_HardLandingSpeedBoundary_FlipsOnlyAboveThreshold()
        {
            LocomotionStopVariant normal = CreateVariant("JumpIdleLand", 0.4f);
            LocomotionStopVariant hard = CreateVariant("JumpIdleLandHard", 0.6f);
            var table = new JumpAnimationVariantTable(
                default,
                default,
                new JumpAnimationVariantSet(normal, null, null),
                default,
                hard);

            LocomotionStopVariant below = JumpState.SelectLandVariant(
                table, LocomotionStopTier.None, false, default, -7.999f, 8f, false);
            LocomotionStopVariant exactlyAt = JumpState.SelectLandVariant(
                table, LocomotionStopTier.None, false, default, -8f, 8f, false);
            LocomotionStopVariant above = JumpState.SelectLandVariant(
                table, LocomotionStopTier.None, false, default, -8.001f, 8f, false);

            Assert.AreEqual("JumpIdleLand", below.AnimationKey);
            Assert.AreEqual("JumpIdleLand", exactlyAt.AnimationKey,
                "規格用『超過門檻』：剛好等於門檻仍屬正常落地");
            Assert.AreEqual("JumpIdleLandHard", above.AnimationKey);
        }

        [Test]
        public void JumpAnimation_CurrentLaunchData_NormalJumpDoesNotSelectHardLand()
        {
            const float apexHeight = 0.9535f;
            const float gravity = 16.78f;
            const float takeoffDelay = 0.0873f;
            var jumpParams = CreateParams<JumpStateParams>();
            float hardLandingSpeed = jumpParams.HardLandingSpeed;
            float initialVelocity = Mathf.Sqrt(2f * gravity * apexHeight);
            float symmetricLandingTime = takeoffDelay + 2f * initialVelocity / gravity;
            float landingVerticalVelocity = JumpState.CalculateVerticalVelocity(
                initialVelocity, gravity, symmetricLandingTime, takeoffDelay);

            LocomotionStopVariant normal = CreateVariant("JumpIdleLand", 0.4f);
            LocomotionStopVariant hard = CreateVariant("JumpIdleLandHard", 0.6f);
            var table = new JumpAnimationVariantTable(
                default,
                default,
                new JumpAnimationVariantSet(normal, null, null),
                default,
                hard);

            LocomotionStopVariant selected = JumpState.SelectLandVariant(
                table,
                LocomotionStopTier.None,
                false,
                default,
                landingVerticalVelocity,
                hardLandingSpeed,
                false);

            Assert.AreEqual(8f, hardLandingSpeed,
                "新建 JumpStateParams 的 authored 重落地門檻必須預設為規格推導的 8 m/s");
            Assert.That(initialVelocity, Is.EqualTo(5.66f).Within(0.01f));
            Assert.That(Mathf.Abs(landingVerticalVelocity), Is.LessThan(hardLandingSpeed),
                "8 m/s 必須高於目前 launch data 的正常對稱落地速度，否則每次跳躍都會誤判為重落地");
            Assert.AreEqual("JumpIdleLand", selected.AnimationKey);
        }

        [Test]
        public void JumpAnimation_FallingBoundary_UsesProjectileVelocityNotClipDuration()
        {
            const float initialVelocity = 6f;
            const float gravity = 12f;
            const float takeoffDelay = 0.2f;

            bool beforeApex = JumpState.ShouldEnterFalling(
                initialVelocity, gravity, 0.699f, takeoffDelay, false);
            bool afterApex = JumpState.ShouldEnterFalling(
                initialVelocity, gravity, 0.701f, takeoffDelay, false);

            Assert.IsFalse(beforeApex, "v_y 尚大於 0 時仍屬 Start");
            Assert.IsTrue(afterApex, "v_y 越過 0 且尚未落地時必須進 Falling");

            // 直接守住純函數邊界：API 根本不接受 clip duration，因此 0.1s／30s 的素材
            // 都只能得到相同物理解答，不可能悄悄把「播完」重新帶回轉移條件。
            MethodInfo method = typeof(JumpState).GetMethod(
                "ShouldEnterFalling", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                StringAssert.DoesNotContain("duration", parameters[i].Name.ToLowerInvariant());
            }
        }

        [Test]
        public void JumpState_FallEntryGrace_RequiresContinuousUngroundedTime()
        {
            const float never = -1f;
            const float grace = 0.1f;

            float ungroundedSince = JumpState.TrackUngroundedSince(false, never, 10f);
            Assert.AreEqual(10f, ungroundedSince, "第一次失地只鎖存 Time.time，不得累加 deltaTime");
            Assert.AreEqual(10f, JumpState.TrackUngroundedSince(false, ungroundedSince, 10.05f),
                "同一段失地被重複詢問時必須保留第一次快照");
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(
                false, ungroundedSince, 10.099f, grace), "短於 grace 的 grounded 抖動不得進 Falling");

            ungroundedSince = JumpState.TrackUngroundedSince(true, ungroundedSince, 10.099f);
            Assert.AreEqual(never, ungroundedSince, "短暫失地後重新著地必須清除 latch");
            Assert.IsFalse(JumpState.ShouldEnterFallFromLostGround(
                true, ungroundedSince, 10.2f, grace));

            ungroundedSince = JumpState.TrackUngroundedSince(false, ungroundedSince, 20f);
            Assert.IsTrue(JumpState.ShouldEnterFallFromLostGround(
                false, ungroundedSince, 20.1f, grace), "連續失地達 grace 必須進 Falling");

            var jumpParams = CreateParams<JumpStateParams>();
            Assert.AreEqual(JumpStateParams.DefaultFallEntryGrace, jumpParams.FallEntryGrace,
                "既有資產缺少序列化 key 時，欄位初始化值必須安全退化為 0.1 秒");
        }

        [Test]
        public void JumpState_PassiveFallEntry_StartsFallingAndDoesNotInjectLaunch()
        {
            JumpState jump = CreateConfiguredJumpState(1);
            var data = new PlayerRuntimeData { IsGrounded = false };
            EnterPassiveFall(jump, data);

            Assert.AreEqual("JumpFalling", jump.AnimationKey,
                "非主動失地必須直接進 Falling，不得先播 Start");
            Assert.IsTrue(GetPrivateField<bool>(jump, "_isVelocityInjected"),
                "非主動失地以『本次滯空不再注入』語意預先封住 launch 分支");

            MotionDriver driver = CreateMotionDriver(-3.25f);
            jump.OnUpdateMotion(driver, null, data);

            Assert.AreEqual(-3.25f, GetPrivateField<float>(driver, "_verticalVelocity"),
                "非主動失地的 OnUpdateMotion 不得呼叫 ApplyJumpLaunch 覆寫既有垂直速度");
            Assert.AreEqual(-123f, GetPrivateField<float>(driver, "_activeGravity"),
                "非主動失地不得把當前重力改成 Jump launch 的重力");
        }

        [Test]
        public void JumpState_ActiveEntry_StartsStartAndInjectsLaunchAsBefore()
        {
            JumpState jump = CreateConfiguredJumpState(1);
            var data = new PlayerRuntimeData { IsGrounded = true };
            data.Intent.JumpRequested = true;

            Assert.IsTrue(jump.CanEnter(data));
            jump.OnEnter(data);
            data.Intent.JumpRequested = false;

            Assert.AreEqual("JumpStart", jump.AnimationKey, "主動起跳仍必須從 Start 開始");
            Assert.IsFalse(GetPrivateField<bool>(jump, "_isVelocityInjected"),
                "主動起跳仍要等待 OnUpdateMotion 的前搖點火");

            MotionDriver driver = CreateMotionDriver(-3.25f);
            jump.OnUpdateMotion(driver, null, data);

            Assert.IsTrue(GetPrivateField<bool>(jump, "_isVelocityInjected"));
            Assert.That(GetPrivateField<float>(driver, "_verticalVelocity"), Is.GreaterThan(0f),
                "主動路徑必須照舊注入向上的 launch 初速");
            Assert.AreNotEqual(-123f, GetPrivateField<float>(driver, "_activeGravity"),
                "主動路徑必須照舊套用該段 launch gravity");
        }

        [TestCase(false, -5f, "JumpNormalLand")]
        [TestCase(false, -9f, "JumpHardLand")]
        [TestCase(true, -5f, "JumpNormalLand")]
        [TestCase(true, -9f, "JumpHardLand")]
        public void JumpState_LandingClassification_UsesBlackboardVelocityForEveryAirborneOrigin(
            bool enteredFromFall,
            float verticalVelocity,
            string expectedAnimationKey)
        {
            JumpState jump = CreateConfiguredJumpState(1);
            var data = new PlayerRuntimeData();

            if (enteredFromFall)
            {
                data.IsGrounded = false;
                EnterPassiveFall(jump, data);
            }
            else
            {
                data.IsGrounded = true;
                data.Intent.JumpRequested = true;
                Assert.IsTrue(jump.CanEnter(data));
                jump.OnEnter(data);
                data.Intent.JumpRequested = false;
                SetPrivateField(jump, "_isVelocityInjected", true);
            }

            data.IsGrounded = true;
            data.VerticalVelocity = verticalVelocity;
            jump.OnTick(data, enteredFromFall ? 0f : 0.15f);

            Assert.AreEqual(expectedAnimationKey, jump.AnimationKey,
                "Normal／Hard 分類必須只看 MotionDriver 發布的 impact velocity，不能依進入來源改公式");
        }

        [Test]
        public void JumpState_PassiveFall_DoesNotGrantAirJump()
        {
            JumpState jump = CreateConfiguredJumpState(2);
            var data = new PlayerRuntimeData { IsGrounded = false };
            EnterPassiveFall(jump, data);

            data.Intent.JumpRequested = true;
            jump.OnTick(data, 0.016f);

            Assert.AreEqual(0, GetPrivateField<int>(jump, "_jumpIndex"),
                "walk-off 不得把同一份 Stages 清單解讀成免費空中跳");
            Assert.IsTrue(GetPrivateField<bool>(jump, "_isVelocityInjected"),
                "非主動失地的 launch 封鎖語意不得被空中按鍵解除");
            Assert.AreEqual("JumpFalling", jump.AnimationKey,
                "被拒絕的空中按鍵不得把 Falling 倒帶回 Start");
        }

        [Test]
        public void JumpAnimation_MovementIntentThreshold_SelectsLandToMove()
        {
            LocomotionStopVariant walkLandLu = CreatePhaseVariant(
                "JumpWalk_LU_Land", 0.4f, FootPhase.LeftFootDown);
            LocomotionStopVariant walkLandRu = CreatePhaseVariant(
                "JumpWalk_RU_Land", 0.4f, FootPhase.RightFootDown);
            LocomotionStopVariant walkMoveLu = CreatePhaseVariant(
                "JumpWalk_LU_Land2Walk", 0.5f, FootPhase.LeftFootDown);
            LocomotionStopVariant walkMoveRu = CreatePhaseVariant(
                "JumpWalk_RU_Land2Walk", 0.5f, FootPhase.RightFootDown);
            LocomotionStopVariant runLandLu = CreatePhaseVariant(
                "JumpRun_LU_Land", 0.4f, FootPhase.LeftFootDown);
            LocomotionStopVariant runLandRu = CreatePhaseVariant(
                "JumpRun_RU_Land", 0.4f, FootPhase.RightFootDown);
            LocomotionStopVariant runMoveLu = CreatePhaseVariant(
                "JumpRun_LU_Land2Run", 0.5f, FootPhase.LeftFootDown);
            LocomotionStopVariant runMoveRu = CreatePhaseVariant(
                "JumpRun_RU_Land2Run", 0.5f, FootPhase.RightFootDown);
            var table = new JumpAnimationVariantTable(
                default,
                default,
                new JumpAnimationVariantSet(
                    default,
                    new[] { walkLandLu, walkLandRu },
                    new[] { runLandLu, runLandRu }),
                new JumpAnimationVariantSet(
                    default,
                    new[] { walkMoveLu, walkMoveRu },
                    new[] { runMoveLu, runMoveRu }),
                default);

            bool shouldStop = JumpState.ShouldContinueMovement(0.2f, 0.2f);
            bool shouldContinue = JumpState.ShouldContinueMovement(0.201f, 0.2f);
            LocomotionStopVariant stopVariant = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Walk, true, FootPhase.RightFootDown, -5f, 8f, shouldStop);
            LocomotionStopVariant moveVariant = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Walk, true, FootPhase.RightFootDown, -5f, 8f, shouldContinue);
            LocomotionStopVariant runStopVariant = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Run, true, FootPhase.LeftFootDown, -5f, 8f, shouldStop);
            LocomotionStopVariant runMoveVariant = JumpState.SelectLandVariant(
                table, LocomotionStopTier.Run, true, FootPhase.LeftFootDown, -5f, 8f, shouldContinue);

            Assert.IsFalse(shouldStop, "等於門檻仍走停止版，對齊 Kubold 的 > 0.2 條件");
            Assert.IsTrue(shouldContinue);
            Assert.AreEqual("JumpWalk_RU_Land", stopVariant.AnimationKey,
                "非重落地停止版必須沿用起跳時的 Walk／RU 快照");
            Assert.AreEqual("JumpWalk_RU_Land2Walk", moveVariant.AnimationKey,
                "非重落地續走版只切換 Land2Move，不得重查 tier 或腳相");
            Assert.AreEqual("JumpRun_LU_Land", runStopVariant.AnimationKey,
                "正常落地停止版也必須沿用起跳時的 Run／LU 快照");
            Assert.AreEqual("JumpRun_LU_Land2Run", runMoveVariant.AnimationKey,
                "移動意圖只能切 Land／Land2Move，不能把 Run／LU 快照降回 Walk／RU");
        }

        [Test]
        public void JumpAnimation_EmptyVariantTable_PreservesLegacyJumpKey()
        {
            const string legacyKey = "Jump";
            JumpAnimationVariantTable emptyTable = default;

            LocomotionStopVariant selected = JumpState.SelectLandVariant(
                emptyTable, LocomotionStopTier.None, false, default, -5f, 8f, false);
            string resolved = null;

            Assert.DoesNotThrow(() => resolved = JumpState.ResolveAnimationKey(selected, legacyKey));
            Assert.AreSame(legacyKey, resolved,
                "空表不得配置或合成新鍵；必須沿用既有 Jump 字串參考");
        }

        private LocomotionStopVariant CreateVariant(string animationKey, float duration)
        {
            var bake = ScriptableObject.CreateInstance<MotionBakeData>();
            bake.BakedDuration = duration;
            _created.Add(bake);
            return new LocomotionStopVariant(bake, animationKey);
        }

        private LocomotionStopVariant CreatePhaseVariant(
            string animationKey,
            float duration,
            FootPhase entryPhase)
        {
            var bake = ScriptableObject.CreateInstance<MotionBakeData>();
            bake.BakedDuration = duration;
            // SelectVariant 刻意要求連續 FootPhaseCurve 存在；測試以起點符號建立最小合法 authored 資料，
            // 不靠陣列索引假裝 LU／RU，才能真的守住「沿用起跳腳相」而非只守住測試排列順序。
            float phaseValue = entryPhase == FootPhase.LeftFootDown ? -1f : 1f;
            bake.FootPhaseCurve = new AnimationCurve(new Keyframe(0f, phaseValue));
            _created.Add(bake);
            return new LocomotionStopVariant(bake, animationKey);
        }

        private JumpState CreateConfiguredJumpState(int stageCount)
        {
            var jumpParams = CreateParams<JumpStateParams>();
            for (int i = 0; i < stageCount; i++) jumpParams.Stages.Add(default);

            LocomotionStopVariant start = CreateVariant("JumpStart", 0.2f);
            LocomotionStopVariant falling = CreateVariant("JumpFalling", 0.2f);
            LocomotionStopVariant normal = CreateVariant("JumpNormalLand", 0.4f);
            LocomotionStopVariant hard = CreateVariant("JumpHardLand", 0.6f);
            var table = new JumpAnimationVariantTable(
                new JumpAnimationVariantSet(start, null, null),
                falling,
                new JumpAnimationVariantSet(normal, null, null),
                default,
                hard);
            SetPrivateField(jumpParams, "animationVariants", table);

            var config = BuildConfig(new StateParamsMapping { State = StateType.Jump, Params = jumpParams });
            var jump = new JumpState();
            jump.Initialize(config, null);
            return jump;
        }

        private static void EnterPassiveFall(JumpState jump, PlayerRuntimeData data)
        {
            Assert.IsFalse(jump.CanEnter(data), "第一次失地只應鎖存 grace 起點");
            SetPrivateField(
                jump,
                "_ungroundedSince",
                Time.time - JumpStateParams.DefaultFallEntryGrace - 0.01f);
            Assert.IsTrue(jump.CanEnter(data), "連續失地超過 grace 後應准入");
            jump.OnEnter(data);
        }

        private MotionDriver CreateMotionDriver(float verticalVelocity)
        {
            var gameObject = new GameObject("JumpState-MotionDriver-Test");
            _created.Add(gameObject);
            CharacterController controller = gameObject.AddComponent<CharacterController>();
            MotionDriver driver = gameObject.AddComponent<MotionDriver>();
            SetPrivateField(driver, "characterController", controller);
            SetPrivateField(driver, "_verticalVelocity", verticalVelocity);
            SetPrivateField(driver, "_activeGravity", -123f);
            SetPrivateField(driver, "_gravityFrame", Time.frameCount);
            return driver;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位");
            return (T)field.GetValue(target);
        }
    }
}
