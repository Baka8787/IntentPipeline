using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// ADR-003 Movement Intent 分層（Migration Stage 1）的確定性 EditMode 單元測試。
    /// 驗證 docs/02-dev-spec.md §1.5／§3.6 的契約，對應 §7 架構回歸檢核表的 A6～A8：
    /// gait 解析純函數、producer 行為等價、Locomotion dynamics 可由 intent 重現、
    /// 以及「連續型 intent 不被單幀復位」的生命週期分界。
    /// 私有序列化欄位以反射注入，等同 Inspector 手動配置（比照 AudioSystemTests 慣例）。
    /// </summary>
    public class MovementIntentTests
    {
        private const float Tolerance = 1e-4f;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"找不到 {target.GetType().Name}.{fieldName} 私有欄位（欄位名稱可能已變更）");
            field.SetValue(target, value);
        }

        /// <summary>建立一份 gait 配置：數值為測試用假值，真實資產須依 speed_i/speed_max 從 Bake Data 填。</summary>
        private GaitProfileSO CreateProfile(float defaultIntensity, float sprintIntensity, float walkIntensity,
                                            bool respectAnalogMagnitude = true, bool walkIsToggle = false)
        {
            var profile = ScriptableObject.CreateInstance<GaitProfileSO>();
            _created.Add(profile);
            SetPrivateField(profile, "defaultIntensity", defaultIntensity);
            SetPrivateField(profile, "sprintIntensity", sprintIntensity);
            SetPrivateField(profile, "walkIntensity", walkIntensity);
            SetPrivateField(profile, "respectAnalogMagnitude", respectAnalogMagnitude);
            SetPrivateField(profile, "walkIsToggle", walkIsToggle);
            return profile;
        }

        private PlayerLocomotionPolicy CreatePolicy(GaitProfileSO profile)
        {
            var go = new GameObject("PolicyUnderTest");
            _created.Add(go);
            var policy = go.AddComponent<PlayerLocomotionPolicy>();
            if (profile != null) SetPrivateField(policy, "gaitProfile", profile);
            return policy;
        }

        // =====================================================================
        // GaitProfileSO：修飾鍵 → 強度的純函數（A8 的資料側）
        // =====================================================================

        [Test]
        public void ResolveIntensity_NoModifier_UsesDefaultGait()
        {
            GaitProfileSO profile = CreateProfile(defaultIntensity: 0.6f, sprintIntensity: 1f, walkIntensity: 0.25f);

            Assert.AreEqual(0.6f, profile.ResolveIntensity(1f, sprintHeld: false, walkActive: false), Tolerance,
                "無修飾鍵時應採 defaultIntensity（典型 ARPG 方案＝預設 Run）");
        }

        [Test]
        public void ResolveIntensity_SprintHeld_UsesSprintGait()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f);

            Assert.AreEqual(1f, profile.ResolveIntensity(1f, sprintHeld: true, walkActive: false), Tolerance,
                "按住 Sprint 應採 sprintIntensity");
        }

        [Test]
        public void ResolveIntensity_WalkWinsOverSprint_WhenBothHeld()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f);

            Assert.AreEqual(0.25f, profile.ResolveIntensity(1f, sprintHeld: true, walkActive: true), Tolerance,
                "同時按住時採『較保守者勝』的固定規則（Walk 優先），此為契約而非未定義行為");
        }

        [Test]
        public void ResolveIntensity_ZeroMagnitude_IsZero_RegardlessOfModifiers()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f);

            Assert.AreEqual(0f, profile.ResolveIntensity(0f, sprintHeld: true, walkActive: false), Tolerance,
                "沒有移動輸入時，任何修飾鍵都不得憑空產生移動意圖");
        }

        [Test]
        public void ResolveIntensity_RespectsAnalogMagnitude_WhenEnabled()
        {
            GaitProfileSO analog = CreateProfile(1f, 1f, 1f, respectAnalogMagnitude: true);
            GaitProfileSO digital = CreateProfile(1f, 1f, 1f, respectAnalogMagnitude: false);

            Assert.AreEqual(0.5f, analog.ResolveIntensity(0.5f, false, false), Tolerance,
                "啟用類比時，推桿量應乘進強度");
            Assert.AreEqual(1f, digital.ResolveIntensity(0.5f, false, false), Tolerance,
                "停用類比時，只要有輸入就吃該 gait 檔位的固定強度");
        }

        [Test]
        public void ResolveIntensity_ClampsOutOfRangeInputs()
        {
            GaitProfileSO profile = CreateProfile(1f, 1f, 1f);

            Assert.AreEqual(1f, profile.ResolveIntensity(3f, false, false), Tolerance,
                "推桿量須先 Clamp01，避免對角輸入等情況把強度推出 [0-1] 契約值域");
        }

        // =====================================================================
        // PlayerLocomotionPolicy：producer 契約（A8 行為等價）
        // =====================================================================

        [Test]
        public void ProduceIntent_WithoutProfile_IsEquivalentToPreMigrationBehaviour()
        {
            PlayerLocomotionPolicy policy = CreatePolicy(null);
            var data = new PlayerRuntimeData();
            InputData input = default;
            input.MoveInput = new Vector2(0.6f, 0f);

            policy.ProduceIntent(ref input, data);

            Assert.AreEqual(0.6f, data.MovementIntent.DesiredSpeedNormalized, Tolerance,
                "未指派 GaitProfileSO 時，強度＝原始推桿量——這是 Stage 1『行為等價』的程式保證");
            Assert.AreEqual(new Vector2(0.6f, 0f), data.MovementIntent.DesiredDirection,
                "方向為純意圖，直接沿用原始輸入向量（世界方向換算是 MotionDriver 的職責）");
        }

        [Test]
        public void ProduceIntent_WithProfile_WritesResolvedIntensity()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f);
            PlayerLocomotionPolicy policy = CreatePolicy(profile);
            var data = new PlayerRuntimeData();
            InputData input = default;
            input.MoveInput = new Vector2(0f, 1f);
            input.SprintButtonHeld = true;

            policy.ProduceIntent(ref input, data);

            Assert.AreEqual(1f, data.MovementIntent.DesiredSpeedNormalized, Tolerance,
                "有 profile 時強度須經 gait 解析，而非直送原始推桿量");
        }

        [Test]
        public void ProduceIntent_ReleasedInput_ZeroesIntent_NotDirectionMemory()
        {
            PlayerLocomotionPolicy policy = CreatePolicy(null);
            var data = new PlayerRuntimeData();

            InputData pressed = default;
            pressed.MoveInput = Vector2.up;
            policy.ProduceIntent(ref pressed, data);

            InputData released = default;
            policy.ProduceIntent(ref released, data);

            Assert.AreEqual(0f, data.MovementIntent.DesiredSpeedNormalized, Tolerance,
                "放開輸入後意圖即為 0——『減速滑行期保留方向』屬 model dynamics，不得洩漏進 producer");
            Assert.AreEqual(Vector2.zero, data.MovementIntent.DesiredDirection,
                "意圖是當幀真相，不帶記憶；記憶屬 LocomotionSpeedSmoother");
        }

        // =====================================================================
        // Walk 型態：hold vs toggle（由資產選擇，狀態存黑板）
        // =====================================================================

        [Test]
        public void ProduceIntent_HoldMode_WalkActiveMirrorsHeldKey()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f, walkIsToggle: false);
            PlayerLocomotionPolicy policy = CreatePolicy(profile);
            var data = new PlayerRuntimeData();

            InputData held = default;
            held.MoveInput = Vector2.up;
            held.WalkButtonHeld = true;
            policy.ProduceIntent(ref held, data);

            Assert.IsTrue(data.MovementIntent.WalkModeActive, "hold 方案：按住期間型態應為啟用");
            Assert.AreEqual(0.25f, data.MovementIntent.DesiredSpeedNormalized, Tolerance, "啟用時應採 walkIntensity");

            InputData released = default;
            released.MoveInput = Vector2.up;
            policy.ProduceIntent(ref released, data);

            Assert.IsFalse(data.MovementIntent.WalkModeActive, "hold 方案：放開即關閉，不得殘留");
            Assert.AreEqual(0.6f, data.MovementIntent.DesiredSpeedNormalized, Tolerance, "關閉後回到 defaultIntensity");
        }

        [Test]
        public void ProduceIntent_ToggleMode_EdgeFlipsAndLatches()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f, walkIsToggle: true);
            PlayerLocomotionPolicy policy = CreatePolicy(profile);
            var data = new PlayerRuntimeData();

            // 按下的那一帧：翻轉為啟用
            InputData press = default;
            press.MoveInput = Vector2.up;
            press.WalkButtonDown = true;
            policy.ProduceIntent(ref press, data);
            Assert.IsTrue(data.MovementIntent.WalkModeActive, "toggle 方案：按下的邊沿應翻轉為啟用");

            // 後續沒有任何輸入的帧：型態必須**閂住**（這是 toggle 與 hold 的唯一差別）
            InputData idleFrame = default;
            idleFrame.MoveInput = Vector2.up;
            for (int i = 0; i < 10; i++) policy.ProduceIntent(ref idleFrame, data);
            Assert.IsTrue(data.MovementIntent.WalkModeActive, "toggle 方案：放開按鍵後型態必須維持，否則就退化成 hold");
            Assert.AreEqual(0.25f, data.MovementIntent.DesiredSpeedNormalized, Tolerance, "閂住期間強度應持續為 walkIntensity");

            // 再按一次：翻轉回關閉
            policy.ProduceIntent(ref press, data);
            Assert.IsFalse(data.MovementIntent.WalkModeActive, "toggle 方案：再次按下應翻轉回關閉");
            Assert.AreEqual(0.6f, data.MovementIntent.DesiredSpeedNormalized, Tolerance, "關閉後回到 defaultIntensity");
        }

        [Test]
        public void ProduceIntent_ToggleMode_IgnoresHeldSignal()
        {
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f, walkIsToggle: true);
            PlayerLocomotionPolicy policy = CreatePolicy(profile);
            var data = new PlayerRuntimeData();

            // 只有 Held、沒有邊沿（＝長按不放的第二帧起）：toggle 方案不得因此再次翻轉或誤啟用
            InputData heldOnly = default;
            heldOnly.MoveInput = Vector2.up;
            heldOnly.WalkButtonHeld = true;
            for (int i = 0; i < 5; i++) policy.ProduceIntent(ref heldOnly, data);

            Assert.IsFalse(data.MovementIntent.WalkModeActive,
                "toggle 方案只看邊沿：長按不放不得每帧翻轉（否則型態會以 frame rate 抖動）");
        }

        [Test]
        public void ProduceIntent_ToggleState_LivesOnBlackboard_NotInProducer()
        {
            // ADR-003 D5／§9-L5：toggle 狀態必須完全存在黑板，producer 不得有私有殘留。
            // 驗法：同一顆 policy 換一塊新黑板，型態必須從乾淨狀態開始。
            GaitProfileSO profile = CreateProfile(0.6f, 1f, 0.25f, walkIsToggle: true);
            PlayerLocomotionPolicy policy = CreatePolicy(profile);

            var first = new PlayerRuntimeData();
            InputData press = default;
            press.MoveInput = Vector2.up;
            press.WalkButtonDown = true;
            policy.ProduceIntent(ref press, first);
            Assert.IsTrue(first.MovementIntent.WalkModeActive);

            var second = new PlayerRuntimeData();
            InputData quiet = default;
            quiet.MoveInput = Vector2.up;
            policy.ProduceIntent(ref quiet, second);

            Assert.IsFalse(second.MovementIntent.WalkModeActive,
                "型態若跟著 policy 走而非跟著黑板走，代表 producer 藏了私有狀態——netcode 的 rewind 會對不上（§9-L5）");
        }

        // =====================================================================
        // 生命週期：連續型 intent vs trigger 邊沿（A6）
        // =====================================================================

        [Test]
        public void ResetTransientState_ClearsTriggerIntents_ButNotMovementIntent()
        {
            var data = new PlayerRuntimeData();
            data.Intent.JumpRequested = true;
            data.Intent.RollRequested = true;
            data.Intent.FireRequested = true;
            data.JustLanded = true;
            data.JustLeftGround = true;
            data.MovementIntent.DesiredSpeedNormalized = 0.75f;
            data.MovementIntent.DesiredDirection = Vector2.right;
            data.MovementIntent.WalkModeActive = true;

            data.ResetTransientState();

            Assert.IsFalse(data.Intent.JumpRequested, "trigger 意圖必須在管線順序 7 統一復位");
            Assert.IsFalse(data.Intent.RollRequested, "trigger 意圖必須在管線順序 7 統一復位");
            Assert.IsFalse(data.Intent.FireRequested, "trigger 意圖必須在管線順序 7 統一復位");
            Assert.IsFalse(data.JustLanded, "單幀邊沿事件必須在管線順序 7 統一復位");
            Assert.IsFalse(data.JustLeftGround, "單幀邊沿事件必須在管線順序 7 統一復位");

            Assert.AreEqual(0.75f, data.MovementIntent.DesiredSpeedNormalized, Tolerance,
                "MovementIntent 是連續型 domain intent（每幀由 producer 整體覆寫），不得被單幀復位清零（ADR-003／docs/04 §14.2）");
            Assert.AreEqual(Vector2.right, data.MovementIntent.DesiredDirection,
                "MovementIntent 是連續型 domain intent，不得被單幀復位清零");
            Assert.IsTrue(data.MovementIntent.WalkModeActive,
                "WalkModeActive 是**持久型態**（mode state），被每帧復位清零就永遠關不起來——" +
                "它與 trigger 邊沿分屬兩種生命週期（ADR-003 D5）");
        }

        // =====================================================================
        // LocomotionSpeedSmoother：Stage 1 過渡 dynamics（A7 單一真相／可重現）
        // =====================================================================

        private static LocomotionSpeedSmoother RunSmoother(MovementIntentData[] frames, float dt = 1f / 60f)
        {
            var smoother = new LocomotionSpeedSmoother();
            foreach (MovementIntentData frame in frames)
            {
                smoother.Tick(in frame, accelTime: 0.12f, decelTime: 0.18f, deltaTime: dt);
            }
            return smoother;
        }

        private static MovementIntentData[] Frames(int count, float speed, Vector2 direction)
        {
            var frames = new MovementIntentData[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = new MovementIntentData { DesiredSpeedNormalized = speed, DesiredDirection = direction };
            }
            return frames;
        }

        [Test]
        public void Smoother_RisesTowardsIntent_WithoutOvershooting()
        {
            LocomotionSpeedSmoother smoother = RunSmoother(Frames(10, 1f, Vector2.up));

            Assert.Greater(smoother.Speed, 0f, "有移動意圖時，平滑速度必須開始爬升");
            Assert.Less(smoother.Speed, 1f, "10 幀（≈0.17s）內不應已達滿速——平滑時間常數必須實際生效");
        }

        [Test]
        public void Smoother_ConvergesToIntent_GivenEnoughTime()
        {
            LocomotionSpeedSmoother smoother = RunSmoother(Frames(120, 0.6f, Vector2.up));

            Assert.AreEqual(0.6f, smoother.Speed, 0.01f,
                "長時間持續同一意圖後，衍生速度必須收斂到意圖值（gait 檔位才會對應到正確的 Mixer tier）");
        }

        [Test]
        public void Smoother_SnapsToExactZero_WhenIntentReleased()
        {
            var smoother = new LocomotionSpeedSmoother();
            var moving = new MovementIntentData { DesiredSpeedNormalized = 1f, DesiredDirection = Vector2.up };
            for (int i = 0; i < 120; i++) smoother.Tick(in moving, 0.12f, 0.18f, 1f / 60f);

            var released = new MovementIntentData();
            for (int i = 0; i < 240; i++) smoother.Tick(in released, 0.12f, 0.18f, 1f / 60f);

            Assert.AreEqual(0f, smoother.Speed,
                "完全停止時必須 snap 到精確 0（清 SmoothDamp 殘尾），否則 Mixer 回不到純 Idle、狀態機也收斂不進 Idle");
            Assert.AreEqual(Vector2.zero, smoother.Direction,
                "速度歸零後方向必須一併歸零，關閉 ExecuteBaseMovement 的方向閘門");
        }

        [Test]
        public void Smoother_KeepsLastDirectionWhileCoasting()
        {
            var smoother = new LocomotionSpeedSmoother();
            var moving = new MovementIntentData { DesiredSpeedNormalized = 1f, DesiredDirection = Vector2.right };
            for (int i = 0; i < 120; i++) smoother.Tick(in moving, 0.12f, 0.18f, 1f / 60f);

            var released = new MovementIntentData(); // 放開：意圖歸零，但速度尚在滑行
            smoother.Tick(in released, 0.12f, 0.18f, 1f / 60f);

            Assert.Greater(smoother.Speed, LocomotionSpeedSmoother.Epsilon, "放開後第一幀應仍在減速滑行");
            Assert.AreEqual(Vector2.right, smoother.Direction,
                "滑行期必須保留最後方向，否則方向瞬歸零會造成『身體停、動畫動』的滑步（B9 契約）");
        }

        [Test]
        public void Smoother_OutputIsFullyDerivedFromIntentSequence()
        {
            // ADR-003 §13.4 紀律：MoveSpeed 必須可由 MovementIntent（＋dynamics）重新導出。
            // 同一串意圖餵給兩個獨立實例，輸出必須逐位元一致＝無隱藏輸入、無跨實例殘留狀態。
            MovementIntentData[] script = new MovementIntentData[90];
            for (int i = 0; i < script.Length; i++)
            {
                float speed = i < 30 ? 1f : i < 60 ? 0.25f : 0f;
                script[i] = new MovementIntentData
                {
                    DesiredSpeedNormalized = speed,
                    DesiredDirection = speed > 0f ? new Vector2(0f, 1f) : Vector2.zero
                };
            }

            LocomotionSpeedSmoother first = RunSmoother(script);
            LocomotionSpeedSmoother second = RunSmoother(script);

            Assert.AreEqual(first.Speed, second.Speed,
                "衍生速度必須完全由意圖序列決定（可重現）——若不成立，代表存在繞過 MovementIntent 的第二真相來源");
            Assert.AreEqual(first.Direction, second.Direction,
                "衍生方向必須完全由意圖序列決定（可重現）");
        }
    }
}
