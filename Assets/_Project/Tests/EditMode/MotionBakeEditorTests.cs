using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Project.Editor;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// Motion Bake 採樣角色解析契約：Gameplay Root 不必自己持有 Animator，但整個子階層必須恰好有一個
    /// 綁定有效 Humanoid Avatar 的 Animator。守住 X Bot 的 Root／Model 兩層實際組裝方式。
    /// </summary>
    public class MotionBakeEditorTests
    {
        [Test]
        public void HumanoidResolver_XBotGameplayRoot_FindsModelAnimator()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/X Bot.prefab");
            Assert.IsNotNull(prefab, "測試前提：X Bot prefab 必須存在");

            bool resolved = MotionBakeEditor.TryResolveHumanoidAnimator(prefab, out Animator animator, out string error);

            Assert.IsTrue(resolved, error);
            Assert.IsNotNull(animator);
            Assert.IsTrue(animator.avatar.isHuman);
        }

        [Test]
        public void HumanoidResolver_NoAnimator_ReturnsExplicitFailure()
        {
            var root = new GameObject("No Animator Root");
            try
            {
                bool resolved = MotionBakeEditor.TryResolveHumanoidAnimator(root, out Animator animator, out string error);

                Assert.IsFalse(resolved);
                Assert.IsNull(animator);
                StringAssert.Contains("Root／子階層", error);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

    }
}
