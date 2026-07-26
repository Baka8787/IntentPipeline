using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.Motion;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// FullBodyStateMachine 轉移邏輯的確定性 EditMode 單元測試（純 C#，不需場景 / MonoBehaviour）。
    /// 對應 Critical #1：驗證「著地閘門」正確放行合法起跳、擋下空中跳，並以真實著地訊號完成落地過渡。
    /// </summary>
    public class StateMachineTests
    {
        /// <summary>
        /// 以程式碼動態建立一份最小可用的狀態拓撲設定檔（不依賴磁碟資產）。
        /// rules 為 StateMachineConfigSO 的私有序列化欄位，測試以反射注入，等同 Inspector 的手動配置。
        /// </summary>
        private static StateMachineConfigSO BuildConfig()
        {
            var config = ScriptableObject.CreateInstance<StateMachineConfigSO>();

            var rules = new List<StateRule>
            {
                new StateRule
                {
                    State = StateType.Idle,
                    Priority = 0,
                    CanBeInterruptedBy = new List<StateType> { StateType.Move, StateType.Jump, StateType.Roll },
                    ValidTransitions = new List<StateType> { StateType.Move }
                },
                new StateRule
                {
                    State = StateType.Move,
                    Priority = 0,
                    CanBeInterruptedBy = new List<StateType> { StateType.Jump, StateType.Roll },
                    ValidTransitions = new List<StateType> { StateType.Idle }
                },
                new StateRule
                {
                    State = StateType.Jump,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(), // 空中不可被打斷
                    ValidTransitions = new List<StateType> { StateType.Idle, StateType.Move }
                },
                new StateRule
                {
                    State = StateType.Roll,
                    Priority = 10,
                    CanBeInterruptedBy = new List<StateType>(), // 無敵幀不可被打斷
                    ValidTransitions = new List<StateType> { StateType.Idle, StateType.Move }
                },
            };

            FieldInfo rulesField = typeof(StateMachineConfigSO)
                .GetField("rules", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(rulesField, "找不到 StateMachineConfigSO.rules 私有欄位（欄位名稱可能已變更）");
            rulesField.SetValue(config, rules);

            return config;
        }

        /// <summary>
        /// 🆕（ADR-003 Stage 2）測試替身：ambient 門檻信號的來源。
        /// 自此狀態機的 Idle／Move 不再讀黑板衍生值 <c>MoveSpeed</c>，改問 active model
        /// 「你在不在產生運動」——拓撲測試因此只需操作這一個布林，不必模擬 B9 平滑。
        /// </summary>
        private class FakeMovementModel : IMovementModel
        {
            public bool IsProducingMotion { get; set; }
            public void Tick(PlayerRuntimeData data, AnimationFacadeBase animationFacade, float deltaTime) { }
            public void UpdateMotion(MotionDriver motionDriver, PlayerRuntimeData data) { }
        }

        /// <summary>建立一台已初始化（進入 Idle）的狀態機與其黑板、以及可操控的門檻信號。</summary>
        private static (FullBodyStateMachine sm, PlayerRuntimeData data, FakeMovementModel model) BuildMachine()
        {
            var data = new PlayerRuntimeData();
            var model = new FakeMovementModel();
            var sm = new FullBodyStateMachine();
            // Initialize 內部會呼叫 config.Initialize() 建表，並進入初始 Idle 狀態。
            // 註：測試 config 特意只含拓撲（無 bakeMappings）。RollState 的「資產斷鏈」警告已由
            // Application.isPlaying 條件排除 EditMode 組裝情境（見 RollState.Initialize 註解）——
            // 拓撲測試不需要、也不該耦合資產層防線的警告文字。
            sm.Initialize(BuildConfig(), data, model);
            return (sm, data, model);
        }

        // === 測試 A：著地 + 跳躍意圖 → 成功轉移至 Jump ===
        [Test]
        public void Jump_WhenGroundedAndRequested_TransitionsToJump()
        {
            var (sm, data, model) = BuildMachine();
            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type, "初始狀態應為 Idle");

            data.IsGrounded = true;
            model.IsProducingMotion = false;
            data.Intent.JumpRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Jump, sm.CurrentState.Type,
                "著地時發出跳躍意圖，狀態機應成功轉移至 Jump");
        }

        // === 測試 B（防禦）：空中發出跳躍意圖 → 必須被擋下，不可轉移 ===
        [Test]
        public void Jump_WhenNotGrounded_IsBlocked()
        {
            var (sm, data, model) = BuildMachine();
            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type);

            data.IsGrounded = false;      // 角色在空中
            model.IsProducingMotion = false;
            data.Intent.JumpRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type,
                "空中發出跳躍意圖必須被著地閘門擋下，不可轉移至 Jump（杜絕無限空中跳）");
        }

        // === 附加防禦：空中不可翻滾 ===
        [Test]
        public void Roll_WhenNotGrounded_IsBlocked()
        {
            var (sm, data, model) = BuildMachine();

            data.IsGrounded = false;
            model.IsProducingMotion = false;
            data.Intent.RollRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type,
                "空中發出翻滾意圖必須被著地閘門擋下");
        }

        // === 附加：地面移動中可被跳躍打斷，且打斷同樣受著地閘門約束 ===
        [Test]
        public void Move_WhenGroundedAndJumpRequested_IsInterruptedByJump()
        {
            var (sm, data, model) = BuildMachine();

            // 先以速度自然過渡進入 Move
            data.IsGrounded = true;
            model.IsProducingMotion = true;
            sm.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Move, sm.CurrentState.Type, "有速度時應自然過渡到 Move");

            // 地面移動中發出跳躍意圖 → 打斷為 Jump
            data.Intent.JumpRequested = true;
            sm.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Jump, sm.CurrentState.Type, "地面移動中發出跳躍意圖應被打斷為 Jump");
        }

        // === 迴歸：Bake 資產存在但沒有可用時長時，Roll 不得秒退（🆕 2026-07-26）===
        [Test]
        public void Roll_WhenBakeDataHasNoDuration_FallsBackInsteadOfEndingInstantly()
        {
            // 情境：Bake 資產綁定正確、但 BakedDuration 為 0（該欄位導入前烘焙的舊資產，或來源 clip 缺席）。
            // 舊實作只用 `_rollBakeData != null` 判定，於是 _rollTimer = 0 → 翻滾第一帧就結束；
            // 這正是「Roll 秒退」在 clip 層的變體，且 FallbackDuration 永遠用不到。
            var staleBake = ScriptableObject.CreateInstance<MotionBakeData>();
            staleBake.BakedDuration = 0f;

            StateMachineConfigSO config = BuildConfig();
            FieldInfo bakeField = typeof(StateMachineConfigSO)
                .GetField("bakeMappings", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(bakeField, "找不到 StateMachineConfigSO.bakeMappings 私有欄位（欄位名稱可能已變更）");
            bakeField.SetValue(config, new List<StateBakeMapping>
            {
                new StateBakeMapping { State = StateType.Roll, BakeData = staleBake }
            });

            var data = new PlayerRuntimeData();
            var model = new FakeMovementModel();
            var sm = new FullBodyStateMachine();
            sm.Initialize(config, data, model);

            data.IsGrounded = true;
            data.Intent.RollRequested = true;
            sm.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Roll, sm.CurrentState.Type, "著地時發出翻滾意圖應進入 Roll");

            // 退化時長為 0.5s；推進 0.1s 後絕不該已經結束
            data.Intent.RollRequested = false;
            sm.Tick(data, 0.1f);

            Assert.AreEqual(StateType.Roll, sm.CurrentState.Type,
                "Bake 資產沒有可用時長時必須退化為固定計時，而不是第一帧就結束——" +
                "退化條件要看『值』而不是『引用是否為 null』");

            Object.DestroyImmediate(staleBake);
        }

        // 註：Jump 的「真實落地」判定依賴 OnUpdateMotion 內的物理衝量注入（_isVelocityInjected）與
        //     CharacterController.isGrounded 的實際碰撞結果，屬 PlayMode／整合測試範疇，
        //     不在此純狀態機 EditMode 單元測試涵蓋。此處聚焦驗證確定性的「進入／打斷著地閘門」邏輯。
    }
}
