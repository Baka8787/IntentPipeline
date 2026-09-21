using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Project.App;
using Project.Core.Blackboard;
using Project.Core.StateMachine;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

namespace Project.Tests.EditMode
{
    public sealed class RuntimeDebugPanelTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        [Test]
        public void BasicMode_ShowsOnlyAuthoritativeSnapshotValues()
        {
            RuntimeDebugPanel panel = CreatePanel(
                out GameObject root,
                out TextMeshProUGUI state,
                out TextMeshProUGUI animation,
                out TextMeshProUGUI clip,
                out TextMeshProUGUI gait);

            panel.SetPortfolioDisplayModeForTests(RuntimeDebugPanel.PortfolioDisplayMode.Basic);
            panel.UpdatePortfolioLabelsForTests(
                StateType.Move, "RunStop_Left", "MAP_Run_Stop_L", GaitTier.Run);

            Assert.IsTrue(root.activeSelf);
            Assert.AreEqual("State: Move", state.text);
            Assert.AreEqual("Anim State: RunStop_Left", animation.text);
            Assert.AreEqual("Clip: MAP_Run_Stop_L", clip.text);
            Assert.AreEqual("Gait: Run", gait.text);
        }

        [Test]
        public void SameSnapshot_DoesNotReplaceUiStringsEveryFrame()
        {
            RuntimeDebugPanel panel = CreatePanel(
                out _, out TextMeshProUGUI state, out TextMeshProUGUI animation, out TextMeshProUGUI clip, out TextMeshProUGUI gait);

            panel.UpdatePortfolioLabelsForTests(
                StateType.Jump, "JumpRun_Land", "MAP_Jump_Run_Land", GaitTier.Sprint);
            string stateInstance = state.text;
            string animationInstance = animation.text;
            string clipInstance = clip.text;
            string gaitInstance = gait.text;

            panel.UpdatePortfolioLabelsForTests(
                StateType.Jump, "JumpRun_Land", "MAP_Jump_Run_Land", GaitTier.Sprint);

            Assert.AreSame(stateInstance, state.text);
            Assert.AreSame(animationInstance, animation.text);
            Assert.AreSame(clipInstance, clip.text);
            Assert.AreSame(gaitInstance, gait.text);
        }

        [Test]
        public void OffMode_HidesPanelWithoutTouchingSnapshotText()
        {
            RuntimeDebugPanel panel = CreatePanel(
                out GameObject root, out TextMeshProUGUI state, out _, out _, out _);
            panel.UpdatePortfolioLabelsForTests(
                StateType.Traversal, "Climb2m", "MAP_Climb_2m", GaitTier.Walk);

            panel.SetPortfolioDisplayModeForTests(RuntimeDebugPanel.PortfolioDisplayMode.Off);

            Assert.IsFalse(root.activeSelf);
            Assert.AreEqual("State: Traversal", state.text);
        }

        [Test]
        public void MissingAnimationData_IsShownAsUnavailableNotGuessed()
        {
            RuntimeDebugPanel panel = CreatePanel(
                out _, out _, out TextMeshProUGUI animation, out TextMeshProUGUI clip, out _);

            panel.UpdatePortfolioLabelsForTests(StateType.Idle, null, string.Empty, GaitTier.Run);

            Assert.AreEqual("Anim State: —", animation.text);
            Assert.AreEqual("Clip: —", clip.text);
        }

        private RuntimeDebugPanel CreatePanel(
            out GameObject root,
            out TextMeshProUGUI state,
            out TextMeshProUGUI animation,
            out TextMeshProUGUI clip,
            out TextMeshProUGUI gait)
        {
            var host = new GameObject("RuntimeDebugHost");
            _created.Add(host);
            RuntimeDebugPanel panel = host.AddComponent<RuntimeDebugPanel>();

            root = new GameObject("PortfolioPanel", typeof(RectTransform));
            _created.Add(root);
            root.transform.SetParent(host.transform, false);
            state = CreateText(root.transform, "State");
            animation = CreateText(root.transform, "AnimationState");
            clip = CreateText(root.transform, "Clip");
            gait = CreateText(root.transform, "Gait");

            SetPrivateField(panel, "portfolioPanelRoot", root);
            SetPrivateField(panel, "stateText", state);
            SetPrivateField(panel, "animationStateText", animation);
            SetPrivateField(panel, "clipText", clip);
            SetPrivateField(panel, "gaitText", gait);
            return panel;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.AddComponent<TextMeshProUGUI>();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Missing {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }
    }
}
