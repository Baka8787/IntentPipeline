using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.StateMachine;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// Jump landing 需要真實 Time.deltaTime 與 CharacterController.Move 的最小 PlayMode 驗證。
    /// 不組裝完整角色；FSM 的 phase 轉移政策由 EditMode StateMachineTests 負責。
    /// </summary>
    public class JumpLandingPlayModeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.Destroy(_host);
        }

        [UnityTest]
        public IEnumerator HardLand_HorizontalMotionIsSuppressedDuringRecovery()
        {
            _host = new GameObject(nameof(HardLand_HorizontalMotionIsSuppressedDuringRecovery));
            _host.transform.position = new Vector3(0f, 2f, 0f);
            CharacterController controller = _host.AddComponent<CharacterController>();
            controller.minMoveDistance = 0f;
            MotionDriver motionDriver = _host.AddComponent<MotionDriver>();
            yield return null;

            var jump = new JumpState();
            SetLandingPhase(jump, "HardRecovery");
            SetPrivateField(jump, "_isVelocityInjected", true);
            // 避免 batchmode 的極短首幀位移落在 CharacterController 預設 minMoveDistance 以下；
            // recovery 要驗證的是「保留既有垂直速度並走 collision path」，不是重力首幀的位移量。
            SetPrivateField(motionDriver, "_verticalVelocity", -1f);

            var data = new PlayerRuntimeData
            {
                MoveDirection = Vector3.right,
                MoveSpeed = 1f
            };
            Vector3 before = _host.transform.position;

            jump.OnUpdateMotion(motionDriver, null, data);
            Vector3 after = _host.transform.position;

            Assert.That(after.x, Is.EqualTo(before.x).Within(0.0001f),
                "HardRecovery 不得施加玩家的水平 X 位移");
            Assert.That(after.z, Is.EqualTo(before.z).Within(0.0001f),
                "HardRecovery 不得施加玩家的水平 Z 位移");
            Assert.Less(after.y, before.y,
                "抑制水平位移時仍必須執行重力與 CharacterController collision update");
            Assert.That(data.IsGrounded, Is.EqualTo(controller.isGrounded),
                "vertical-only 路徑仍須同步 CharacterController grounded 狀態");
        }

        private static void SetLandingPhase(JumpState jump, string phaseName)
        {
            FieldInfo field = typeof(JumpState).GetField(
                "_landingPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(jump, System.Enum.Parse(field.FieldType, phaseName));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
