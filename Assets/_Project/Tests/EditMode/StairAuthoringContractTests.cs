using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Project.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Tests.EditMode
{
    /// <summary>
    /// 樓梯移動面 authoring／碰撞契約（正本：`docs/44`）。
    ///
    /// 四組：①規則本身（合成剖面，不碰場景）②專案碰撞政策 ③每個查詢系統依語意的遮罩
    /// ④SampleScene 內每一道已套用的樓梯。④會在測試中以 Additive 方式開啟場景（若尚未載入），
    /// 讀的是**磁碟上已存檔**的版本。
    /// </summary>
    public class StairAuthoringContractTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab";
        private const string EnemyPrefabPath = "Assets/Prefabs/Y Bot.prefab";

        /// <summary>最低驗收：兩道幾何不同的樓梯（`docs/44` §1）。</summary>
        private static readonly string[] RequiredAuthoredStairs = { "Stairs_650_400_300_Prefab", "Stairs (1)" };

        private static int TreadLayer => LayerMask.NameToLayer(StairMovementSurfaceGenerator.TreadLayerName);
        private static int MovementLayer => LayerMask.NameToLayer(StairMovementSurfaceGenerator.MovementLayerName);

        // ================================================================ ① 規則本身

        private static List<Vector2> SyntheticStair(int risers, float riser, float tread, float step = 0.005f)
        {
            var samples = new List<Vector2>();
            float length = (risers + 1) * tread;
            for (float s = step * 0.5f; s < length; s += step)
            {
                int k = Mathf.Min((int)(s / tread), risers);
                samples.Add(new Vector2(s, k * riser));
            }
            return samples;
        }

        [TestCase(0.195f, 0.343f, 29.62f)]
        [TestCase(0.194f, 0.275f, 35.20f)]
        public void Rule_TwoGeometries_SameAnalyzer_NoPerInstanceParameters(float riser, float tread, float expectedSlope)
        {
            StairProfile p = StairProfileAnalyzer.Analyze(SyntheticStair(18, riser, tread));
            Assert.IsTrue(p.IsValid, p.Failure);
            Assert.AreEqual(18, p.RiserCount);
            Assert.AreEqual(riser, p.RiserHeight, 0.002f);
            Assert.AreEqual(expectedSlope, p.SlopeDegrees, 0.15f);
            Assert.Less(p.NosingResidual, 0.005f);
        }

        [Test]
        public void Rule_NosingLine_IsNeverBelowATread_AndAtMostOneRiserAbove()
        {
            List<Vector2> samples = SyntheticStair(18, 0.194f, 0.275f);
            StairProfile p = StairProfileAnalyzer.Analyze(samples);
            Assert.IsTrue(p.IsValid, p.Failure);
            foreach (Vector2 sample in samples)
            {
                if (sample.x > p.LastNosingS) break; // 頂端踏面由平頂蓋住，高差為 0
                float gap = p.LineAt(sample.x) - sample.y;
                Assert.GreaterOrEqual(gap, -0.005f, $"s={sample.x:F3}");
                Assert.LessOrEqual(gap, p.RiserHeight + 0.005f, $"s={sample.x:F3}");
            }
        }

        [Test]
        public void Rule_NonUniformRisers_AreRejected()
        {
            List<Vector2> samples = SyntheticStair(10, 0.2f, 0.3f);
            for (int i = 0; i < samples.Count; i++)
                if (samples[i].x > 1.6f) samples[i] = new Vector2(samples[i].x, samples[i].y + 0.05f);
            Assert.IsFalse(StairProfileAnalyzer.Analyze(samples).IsValid);
        }

        [Test]
        public void Rule_SmoothSlope_IsNotAStair()
        {
            var samples = new List<Vector2>();
            for (float s = 0f; s < 2.2f; s += 0.005f) samples.Add(new Vector2(s, s * 0.45f));
            Assert.IsFalse(StairProfileAnalyzer.Analyze(samples).IsValid);
        }

        [Test]
        public void Rule_ProfileWithMissingSamples_IsRejected()
        {
            // 中段一整片沒有命中（懸空踏板／破洞）：缺口兩側的高度差不得被當成一道踢面。
            List<Vector2> samples = SyntheticStair(10, 0.2f, 0.3f).Where(v => v.x < 1.2f || v.x > 1.5f).ToList();
            StairProfile p = StairProfileAnalyzer.Analyze(samples);
            Assert.IsFalse(p.IsValid);
            StringAssert.Contains("沒有命中", p.Failure);
        }

        [Test]
        public void Rule_DescendingProfile_IsRejected_SoTheGeneratorMustPickTheUphillDirection()
        {
            List<Vector2> samples = SyntheticStair(8, 0.2f, 0.3f).Select(v => new Vector2(-v.x, v.y)).Reverse().ToList();
            Assert.IsFalse(StairProfileAnalyzer.Analyze(samples).IsValid);
        }

        // ================================================================ ② 專案碰撞政策

        [Test]
        public void Policy_StairLayersExist()
        {
            Assert.GreaterOrEqual(TreadLayer, 0, StairMovementSurfaceGenerator.TreadLayerName);
            Assert.GreaterOrEqual(MovementLayer, 0, StairMovementSurfaceGenerator.MovementLayerName);
        }

        [TestCase(PlayerPrefabPath)]
        [TestCase(EnemyPrefabPath)]
        public void Policy_CharacterLayer_IgnoresTreads_CollidesWithMovementSurface(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            int layer = prefab.GetComponentInChildren<CharacterController>(true).gameObject.layer;
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(layer, TreadLayer),
                $"{LayerMask.LayerToName(layer)} 必須忽略 {StairMovementSurfaceGenerator.TreadLayerName}（專案 Layer Collision Matrix）");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(layer, MovementLayer),
                $"{LayerMask.LayerToName(layer)} 必須碰撞 {StairMovementSurfaceGenerator.MovementLayerName}");
        }

        [Test]
        public void Policy_MovementSurface_CollidesOnlyWithCharacterLayers()
        {
            var characterLayers = new HashSet<int>();
            foreach (string path in new[] { PlayerPrefabPath, EnemyPrefabPath })
                characterLayers.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<CharacterController>(true).gameObject.layer);

            for (int layer = 0; layer < 32; layer++)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
                bool collides = !Physics.GetIgnoreLayerCollision(MovementLayer, layer);
                Assert.AreEqual(characterLayers.Contains(layer), collides,
                    $"{StairMovementSurfaceGenerator.MovementLayerName} × {LayerMask.LayerToName(layer)}");
            }
        }

        [Test]
        public void Policy_Treads_StillCollideWithEveryNonCharacterLayer()
        {
            var characterLayers = new HashSet<int>();
            foreach (string path in new[] { PlayerPrefabPath, EnemyPrefabPath })
                characterLayers.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<CharacterController>(true).gameObject.layer);

            for (int layer = 0; layer < 32; layer++)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) || characterLayers.Contains(layer) || layer == MovementLayer) continue;
                Assert.IsFalse(Physics.GetIgnoreLayerCollision(TreadLayer, layer),
                    $"{StairMovementSurfaceGenerator.TreadLayerName} × {LayerMask.LayerToName(layer)} 必須碰撞（剛體要落在看得到的踏面上）");
            }
        }

        [TestCase(PlayerPrefabPath)]
        [TestCase(EnemyPrefabPath)]
        public void Policy_MovementQueryMask_SeesExactlyWhatTheControllerCollidesWith(string prefabPath)
        {
            int layer = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponentInChildren<CharacterController>(true).gameObject.layer;
            int mask = Project.Presentation.Motion.MotionDriver.MovementCollisionMask(layer);
            Assert.AreEqual(0, mask & (1 << TreadLayer), "膠囊偏移夾持不得看見角色忽略的踏面");
            Assert.AreNotEqual(0, mask & (1 << MovementLayer), "膠囊偏移夾持必須看見移動面");
            Assert.AreEqual(0, mask & (1 << layer), "必須排除自身 layer，否則每次都打到自己");
        }

        [Test]
        public void Policy_CollisionPolicy_IsNotOwnedByARuntimeComponent()
        {
            Type prototype = typeof(Project.Presentation.Motion.MotionDriver).Assembly
                .GetType("Project.Presentation.Motion.RampPrototypeLayerSetup");
            Assert.IsNull(prototype, "碰撞政策必須在專案設定，不得由角色元件在執行期改寫全域物理（docs/44 §4）");
        }

        // ================================================================ ③ 依語意的遮罩

        private sealed class MaskExpectation
        {
            public string Intent;
            public string TypeName;
            public string PropertyPath;
            public bool SeesTread;
            public bool SeesMovement;

            /// <summary>`docs/44` §5 的目標值——鎖住整個遮罩，不只兩個位元。</summary>
            public int ExpectedMask;
        }

        /// <summary>`docs/44` §5 的表格。每一列都是「這個查詢在問什麼」的決定，不是從 Default 推導。</summary>
        private static readonly MaskExpectation[] MaskTable =
        {
            new MaskExpectation { Intent = "腳落在看得到的踏面", TypeName = "Project.Presentation.IK.FootIKController", PropertyPath = "settings.GroundLayers", SeesTread = true, SeesMovement = false, ExpectedMask = -9 },
            new MaskExpectation { Intent = "移動類查詢（step-down）", TypeName = "Project.Presentation.Motion.MotionDriver", PropertyPath = "stepDownQueryLayers", SeesTread = false, SeesMovement = true, ExpectedMask = -257 },
            new MaskExpectation { Intent = "瞄準打在看得到的幾何", TypeName = "Project.Presentation.CameraControl.AimResolver", PropertyPath = "aimRayMask", SeesTread = true, SeesMovement = false, ExpectedMask = 385 },
            new MaskExpectation { Intent = "相機被看得到的幾何擋住", TypeName = "Project.Presentation.CameraControl.ThirdPersonCamera", PropertyPath = "obstructionMask", SeesTread = true, SeesMovement = false, ExpectedMask = 257 },
            new MaskExpectation { Intent = "traversal 障礙是看得到的幾何", TypeName = "Project.Core.Environment.TraversalProbe", PropertyPath = "settings.obstacleMask", SeesTread = true, SeesMovement = false, ExpectedMask = 275 },
            new MaskExpectation { Intent = "地面技能落在看得到的地面", TypeName = "Project.Presentation.Actions.GroundEffectSink", PropertyPath = "groundMask", SeesTread = true, SeesMovement = false, ExpectedMask = 307 },
            new MaskExpectation { Intent = "投射物撞看得到的幾何", TypeName = "Project.Presentation.Actions.ThrownProjectile", PropertyPath = "sweepMask", SeesTread = true, SeesMovement = false, ExpectedMask = -9 },
        };

        /// <summary>每個 prefab 必須帶著這些查詢元件——少一個就代表表格有一列沒被檢查到。</summary>
        private static readonly (string Prefab, string TypeName, int Count)[] RequiredQueryComponents =
        {
            (PlayerPrefabPath, "Project.Presentation.IK.FootIKController", 1),
            (PlayerPrefabPath, "Project.Presentation.Motion.MotionDriver", 1),
            (PlayerPrefabPath, "Project.Presentation.CameraControl.AimResolver", 1),
            (PlayerPrefabPath, "Project.Core.Environment.TraversalProbe", 1),
            (PlayerPrefabPath, "Project.Presentation.Actions.GroundEffectSink", 1),
            (EnemyPrefabPath, "Project.Presentation.IK.FootIKController", 1),
            (EnemyPrefabPath, "Project.Presentation.Motion.MotionDriver", 1),
            ("Assets/Prefabs/Main Camera.prefab", "Project.Presentation.CameraControl.ThirdPersonCamera", 1),
            ("Assets/Prefabs/Projectile_Fireball.prefab", "Project.Presentation.Actions.ThrownProjectile", 1),
            ("Assets/Prefabs/ThrownProjectile.prefab", "Project.Presentation.Actions.ThrownProjectile", 1),
        };

        private static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        private static List<string> CheckMasks(IEnumerable<GameObject> roots, string where)
        {
            var violations = new List<string>();
            foreach (MaskExpectation row in MaskTable)
            {
                Type type = FindType(row.TypeName);
                Assert.IsNotNull(type, row.TypeName);
                foreach (GameObject root in roots)
                {
                    foreach (Component component in root.GetComponentsInChildren(type, true))
                    {
                        var so = new SerializedObject(component);
                        SerializedProperty bits = so.FindProperty(row.PropertyPath + ".m_Bits");
                        if (bits == null) { violations.Add($"{where}/{component.name} {type.Name}.{row.PropertyPath} 找不到"); continue; }
                        int mask = (int)bits.longValue;
                        if (mask != row.ExpectedMask)
                            violations.Add($"{where}/{component.name} {type.Name}.{row.PropertyPath}={mask}，應為 {row.ExpectedMask}（{row.Intent}）");
                    }
                }
            }
            return violations;
        }

        [Test]
        public void Masks_TableValues_EncodeTheirSemanticIntent()
        {
            foreach (MaskExpectation row in MaskTable)
            {
                Assert.AreEqual(row.SeesTread, (row.ExpectedMask & (1 << TreadLayer)) != 0, row.Intent + "：StairTread 位元與語意不符");
                Assert.AreEqual(row.SeesMovement, (row.ExpectedMask & (1 << MovementLayer)) != 0, row.Intent + "：CharacterMovementSurface 位元與語意不符");
            }
        }

        [Test]
        public void Masks_Prefabs_MatchSemanticTable()
        {
            var failures = new List<string>();
            foreach (var required in RequiredQueryComponents)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(required.Prefab);
                Assert.IsNotNull(prefab, required.Prefab);
                int count = prefab.GetComponentsInChildren(FindType(required.TypeName), true).Length;
                if (count != required.Count) failures.Add($"{required.Prefab} 有 {count} 個 {required.TypeName}，應為 {required.Count}");
            }

            var roots = RequiredQueryComponents.Select(r => r.Prefab).Distinct().Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p));
            failures.AddRange(CheckMasks(roots, "prefab"));
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ================================================================ ④ SampleScene

        private Scene _scene;
        private bool _openedHere;

        private Scene LoadScene()
        {
            if (_scene.IsValid() && _scene.isLoaded) return _scene;
            _scene = SceneManager.GetSceneByPath(ScenePath);
            _openedHere = false;
            if (!_scene.IsValid() || !_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                _openedHere = true;
            }
            else if (_scene.isDirty)
            {
                // 契約驗證的對象是磁碟上的場景；未存檔的編輯中場景不能代表它。
                Assert.Inconclusive("SampleScene 有未存檔的變更——存檔後再跑場景契約測試");
            }
            Physics.SyncTransforms();
            return _scene;
        }

        [OneTimeTearDown]
        public void CloseScene()
        {
            if (_openedHere && _scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void Scene_RequiredStairs_AreAuthored()
        {
            List<string> authored = StairMovementSurfaceGenerator.FindAuthoredStairs(LoadScene()).Select(s => s.name).ToList();
            foreach (string name in RequiredAuthoredStairs)
                Assert.Contains(name, authored, $"{name} 沒有產生的移動面");
        }

        [Test]
        public void Scene_EveryAuthoredStair_SatisfiesContract()
        {
            Scene scene = LoadScene();
            float slopeLimit = StairMovementSurfaceGenerator.MinCharacterSlopeLimit(scene);
            var failures = new List<string>();
            List<GameObject> stairs = StairMovementSurfaceGenerator.FindAuthoredStairs(scene);
            Assert.IsNotEmpty(stairs);
            foreach (GameObject stair in stairs)
            {
                StairSurfaceReport report = StairMovementSurfaceGenerator.Validate(stair, slopeLimit);
                TestContext.WriteLine(report.ToString());
                if (!report.IsValid) failures.Add(report.ToString());
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void Scene_InstanceMasks_MatchSemanticTable()
        {
            List<string> violations = CheckMasks(LoadScene().GetRootGameObjects(), "scene");
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void Scene_StairShapedColliders_WithoutMovementSurface_AreReported()
        {
            List<string> unauthored = StairMovementSurfaceGenerator.FindUnauthoredStairShapes(LoadScene());
            if (unauthored.Count == 0) return;
            string message = "尚未套用契約的樓梯（不擋驗收，但它們仍是修前行為）：" + string.Join(", ", unauthored);
            Debug.LogWarning("[StairAuthoringContract] " + message);
            Assert.Pass(message);
        }
    }
}
