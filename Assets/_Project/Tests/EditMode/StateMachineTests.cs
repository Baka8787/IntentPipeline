using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.StateMachine;

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

        /// <summary>建立一台已初始化（進入 Idle）的狀態機與其黑板。</summary>
        private static (FullBodyStateMachine sm, PlayerRuntimeData data) BuildMachine()
        {
            var data = new PlayerRuntimeData();
            var sm = new FullBodyStateMachine();
            // Initialize 內部會呼叫 config.Initialize() 建表，並進入初始 Idle 狀態。
            sm.Initialize(BuildConfig(), data);
            return (sm, data);
        }

        // === 測試 A：著地 + 跳躍意圖 → 成功轉移至 Jump ===
        [Test]
        public void Jump_WhenGroundedAndRequested_TransitionsToJump()
        {
            var (sm, data) = BuildMachine();
            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type, "初始狀態應為 Idle");

            data.IsGrounded = true;
            data.MoveSpeed = 0f;
            data.Intent.JumpRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Jump, sm.CurrentState.Type,
                "著地時發出跳躍意圖，狀態機應成功轉移至 Jump");
        }

        // === 測試 B（防禦）：空中發出跳躍意圖 → 必須被擋下，不可轉移 ===
        [Test]
        public void Jump_WhenNotGrounded_IsBlocked()
        {
            var (sm, data) = BuildMachine();
            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type);

            data.IsGrounded = false;      // 角色在空中
            data.MoveSpeed = 0f;
            data.Intent.JumpRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type,
                "空中發出跳躍意圖必須被著地閘門擋下，不可轉移至 Jump（杜絕無限空中跳）");
        }

        // === 附加防禦：空中不可翻滾 ===
        [Test]
        public void Roll_WhenNotGrounded_IsBlocked()
        {
            var (sm, data) = BuildMachine();

            data.IsGrounded = false;
            data.MoveSpeed = 0f;
            data.Intent.RollRequested = true;

            sm.Tick(data, 0.016f);

            Assert.AreEqual(StateType.Idle, sm.CurrentState.Type,
                "空中發出翻滾意圖必須被著地閘門擋下");
        }

        // === 附加：地面移動中可被跳躍打斷，且打斷同樣受著地閘門約束 ===
        [Test]
        public void Move_WhenGroundedAndJumpRequested_IsInterruptedByJump()
        {
            var (sm, data) = BuildMachine();

            // 先以速度自然過渡進入 Move
            data.IsGrounded = true;
            data.MoveSpeed = 1f;
            sm.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Move, sm.CurrentState.Type, "有速度時應自然過渡到 Move");

            // 地面移動中發出跳躍意圖 → 打斷為 Jump
            data.Intent.JumpRequested = true;
            sm.Tick(data, 0.016f);
            Assert.AreEqual(StateType.Jump, sm.CurrentState.Type, "地面移動中發出跳躍意圖應被打斷為 Jump");
        }

        // 註：Jump 的「真實落地」判定依賴 OnUpdateMotion 內的物理衝量注入（_isVelocityInjected）與
        //     CharacterController.isGrounded 的實際碰撞結果，屬 PlayMode／整合測試範疇，
        //     不在此純狀態機 EditMode 單元測試涵蓋。此處聚焦驗證確定性的「進入／打斷著地閘門」邏輯。
    }
}
