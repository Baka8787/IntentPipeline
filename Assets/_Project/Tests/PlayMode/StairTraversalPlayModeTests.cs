using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Presentation.IK;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 樓梯 authoring 契約的執行期驗收（`docs/44` §9.3）：在真實 SampleScene 的兩道產生式樓梯上，
    /// 用真實輸入驅動 Player（Walk／Run × 上／下）與真實 AI 驅動 Enemy（上／下），逐帧量測：
    /// FSM 是否誤入 Falling、是否誤發落地／離地事件、Foot IK 是否只踩真實踏面、root 垂直速度是否連續。
    /// </summary>
    public sealed class StairTraversalPlayModeTests : InputTestFixture
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int MaximumFrames = 2400;

        /// <summary>相鄰帧垂直速度差上限（m/s）。真實階梯的 stepOffset 單帧抬升 0.19 m 會產生 >10 m/s 的跳變。</summary>
        private const float MaximumVerticalSpeedChange = 1.5f;

        private Keyboard _keyboard;
        private Scene _scene;
        private GameObject _testCamera;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        /// <summary>
        /// ⚠️ 必須在測試本體內載入，不能放在 [UnitySetUp]：UnitySetUp 早於 InputTestFixture.Setup，
        /// 場景角色的 InputAction 會先綁到真實裝置，接著 fixture 重置輸入系統 ⇒ 虛擬鍵盤永遠讀到 0，
        /// 並在拆除時丟 `statePtr` 例外（實測）。
        /// </summary>
        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.IsNotNull(load, ScenePath);
            yield return load;
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            _testCamera = new GameObject("StairTraversalPlayMode-Camera");
            yield return null;
        }

        /// <summary>先卸載再斷言：斷言失敗也不會把場景角色留給後續測試（同 PauseLifecyclePlayModeTests）。</summary>
        private IEnumerator UnloadScene()
        {
            if (_testCamera != null) Object.Destroy(_testCamera);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        /// <summary>保險：測試本體在卸載前就失敗時（例如 Warp 斷言），仍保證場景被卸載。</summary>
        [UnityTearDown]
        public IEnumerator EnsureSceneUnloaded()
        {
            yield return UnloadScene();
        }

        /// <summary>
        /// 被量測的角色必須每帧跑 IK pass：Y Bot 的 Animator 是 CullUpdateTransforms，
        /// 離開主相機畫面時 OnAnimatorIK 不執行，Foot IK 的地面資料會停在上一次（實測：
        /// 在樓梯下半段讀到 208 次過期的 Ground 命中）。這是量測條件，不是樓梯行為。
        /// </summary>
        private static void AlwaysAnimate(CharacterPipelineRunner runner)
        {
            foreach (Animator animator in runner.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        public enum Gait { Walk, Run }
        public enum Direction { Up, Down }

        // ================================================================ Player

        [UnityTest]
        public IEnumerator Player_TraversesGeneratedStair(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(Direction.Up, Direction.Down)] Direction direction)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);

            float startS = direction == Direction.Up ? stair.BottomS - 2.0f : stair.TopS - 0.12f;
            Teleport(player, stair, startS, direction == Direction.Up ? stair.Ascent : -stair.Ascent);
            for (int i = 0; i < 20; i++) yield return null;

            var trace = new Trace(stair, player);
            Key moveKey = direction == Direction.Up ? Key.W : Key.S;
            if (gait == Gait.Walk) Press(_keyboard.leftCtrlKey);
            Press(_keyboard[moveKey]);

            float releaseS = direction == Direction.Up
                ? stair.TopS - (gait == Gait.Run ? 1.3f : 0.45f)
                : stair.BottomS - 1.5f;
            bool released = false;
            int settleFrames = 0;
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;
                trace.Sample();
                float s = stair.S(player.transform.position);
                bool reached = direction == Direction.Up ? s >= releaseS : s <= releaseS;
                if (!released && reached)
                {
                    Release(_keyboard[moveKey]);
                    if (gait == Gait.Walk) Release(_keyboard.leftCtrlKey);
                    released = true;
                    // 放開後進入 Locomotion Stop：它在平地上本來就有 2 帧水平停頓（量測見 docs/44 §9.4），
                    // 那不是樓梯的性質，因此連續性只量「按住移動」的這一段。
                    trace.MeasureContinuity = false;
                }
                if (released && ++settleFrames > 45) break;
            }
            if (!released)
            {
                Release(_keyboard[moveKey]);
                if (gait == Gait.Walk) Release(_keyboard.leftCtrlKey);
            }

            string label = $"Player {stairName} {gait} {direction}";
            string where = $"停在 s={stair.S(player.transform.position):F2}";
            trace.Report(label);
            yield return UnloadScene();

            Assert.IsTrue(released, $"{label}：{MaximumFrames} 帧內沒有走完（{where}）");
            trace.AssertContract(label, requireNoGroundLoss: true, requireContinuity: true);
        }

        /// <summary>
        /// Run 下樓途中放開：Locomotion Stop 走的是烘焙曲線路徑（不經 step-down assist）——
        /// 停在坡面中段也不得誤入 Falling、不得發落地／離地事件、不得失地。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_RunDown_StopMidStair_StaysGrounded(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);

            Teleport(player, stair, stair.TopS - 0.12f, -stair.Ascent);
            for (int i = 0; i < 20; i++) yield return null;

            var trace = new Trace(stair, player);
            Press(_keyboard.sKey);
            bool released = false;
            int settleFrames = 0;
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;
                trace.Sample();
                if (!released && stair.S(player.transform.position) <= 0f)
                {
                    Release(_keyboard.sKey);
                    released = true;
                }
                if (released && ++settleFrames > 90) break;
            }
            if (!released) Release(_keyboard.sKey);

            string label = $"Player {stairName} Run Down stop mid-stair";
            trace.Report(label);
            yield return UnloadScene();

            Assert.IsTrue(released, $"{label}：沒有跑到樓梯中段");
            trace.AssertStopContract(label);
        }

        // ================================================================ 跳躍與墜落（樓梯貼地不得吃掉它們）

        /// <summary>
        /// 在樓梯中段 Run 並按跳：樓梯貼地（step-down assist）不得把起跳初速覆寫掉（docs/42 R6）。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_JumpFromGeneratedStair_IsNotSwallowedByGroundFollowing(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Direction.Up, Direction.Down)] Direction direction)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);

            float startS = direction == Direction.Up ? stair.BottomS - 2.0f : stair.TopS - 0.12f;
            Teleport(player, stair, startS, direction == Direction.Up ? stair.Ascent : -stair.Ascent);
            for (int i = 0; i < 20; i++) yield return null;

            Key moveKey = direction == Direction.Up ? Key.W : Key.S;
            Press(_keyboard[moveKey]);
            bool jumped = false, enteredJump = false, landed = false;
            float launchY = 0f, peakAboveSurface = 0f;
            for (int frame = 0; frame < MaximumFrames && !landed; frame++)
            {
                yield return null;
                float s = stair.S(player.transform.position);
                if (!jumped && Mathf.Abs(s) < 0.4f)
                {
                    launchY = player.transform.position.y;
                    Press(_keyboard.spaceKey);
                    jumped = true;
                    continue;
                }
                if (jumped) Release(_keyboard.spaceKey);
                if (!jumped) continue;
                if (player.CurrentState == StateType.Jump) enteredJump = true;
                Vector3 p = player.transform.position;
                peakAboveSurface = Mathf.Max(peakAboveSurface, p.y - stair.SurfaceY(p));
                landed = enteredJump && player.CurrentState != StateType.Jump;
            }
            Release(_keyboard[moveKey]);

            string label = $"Player jump on {stairName} {direction}";
            string summary = $"jumped={jumped} enteredJump={enteredJump} landed={landed} launchY={launchY:F2} peakAboveSurface={peakAboveSurface:F3}";
            Debug.Log("[StairPlayMode] " + label + ": " + summary);
            yield return UnloadScene();

            Assert.IsTrue(jumped, $"{label}：沒走到樓梯中段（{summary}）");
            Assert.IsTrue(enteredJump, $"{label}：按跳沒有進入 Jump 狀態（{summary}）");
            // 靜止站立時 root 在移動面上方 0.08–0.10 m（膠囊在斜面上的幾何），真的起跳要明顯高過它。
            Assert.Greater(peakAboveSurface, 0.4f, $"{label}：起跳被吃掉，沒有離開移動面（{summary}）");
            Assert.IsTrue(landed, $"{label}：起跳後沒有落地（{summary}）");
        }

        // ================================================================ 身體參考面（docs/05 §3.5.4.3）

        /// <summary>
        /// 身體參考面以 8 個世界固定方向取樣——站在樓梯中段原地轉 90°／180°，骨盆目標不得改變
        /// （Codex 審查 High：沿 facing 取樣時，轉身會讓身體高度改變整段樓梯修正量）。
        /// 也確認在樓梯上骨盆確實被參考面拉低（否則這條測試等於空轉）。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_BodyReference_IsIndependentOfFacing(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);
            var footIK = player.GetComponentInChildren<FootIKController>();
            // 本測試釘的是「參考面包絡」（移動中的身體參考）與朝向無關。站直原型（docs/05 §3.5.4.6）刻意讓**靜止站立**時的骨盆
            // 跟隨實際的腳下支撐（側身站在樓梯上兩腳落在不同階 ⇒ 高度不同），那是新的基線、由
            // Player_StandingPosture_RisesAboveEnvelope_WithFeetReachable 驗證。這裡明確只測包絡本身。
            // 🔄（docs/46 §12）統一 Moving 骨盆開啟時，Moving 的骨盆改由腳的可達約束決定（朝向改變兩腳落點 ⇒ 高度刻意改變），
            //    包絡退為後備／診斷參照——它本身的朝向無關性仍是要守的性質，所以這裡同樣關掉統一路徑、直接量包絡。
            //    統一路徑的基線由 Player_UnifiedMoving_SupportFeetReachable_AtAnyFacing 驗證。
            SetStandingPosture(footIK, false);
            SetUnifiedMoving(footIK, false);

            Teleport(player, stair, 0f, stair.Ascent);
            for (int i = 0; i < 60; i++) yield return null;
            float facingUp = footIK.TargetData.PelvisOffsetY;

            var readings = new List<string> { $"0°:{facingUp:F3}" };
            float worst = 0f;
            foreach (float yaw in new[] { 90f, 180f, 270f, 45f })
            {
                player.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * stair.Ascent, Vector3.up);
                for (int i = 0; i < 60; i++) yield return null;
                float p = footIK.TargetData.PelvisOffsetY;
                readings.Add($"{yaw}°:{p:F3}");
                worst = Mathf.Max(worst, Mathf.Abs(p - facingUp));
            }
            string label = $"[StairPlayMode] body reference {stairName}: " + string.Join(" ", readings);
            Debug.Log(label);
            TestContext.WriteLine(label);
            yield return UnloadScene();

            Assert.Less(facingUp, -0.2f, $"{stairName}：站在樓梯中段時骨盆應被參考面拉低（{label}）");
            Assert.Less(worst, 0.01f, $"{stairName}：原地轉身不得改變骨盆（{label}）");
        }

        /// <summary>
        /// 🧪 站直原型（docs/05 §3.5.4.6，X Bot 開啟）：停在樓梯中段站著 ⇒ 骨盆高於包絡（站直），兩腳仍搆得到 T3 目標；
        /// 原地轉向（不同朝向讓兩腳落在不同階）後重新站定，腳仍搆得到。高度隨實際支撐改變是刻意的。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_StandingPosture_RisesAboveEnvelope_WithFeetReachable(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);
            var footIK = player.GetComponentInChildren<FootIKController>();
            SetStandingPosture(footIK, true);
            // 🔄（docs/46 §14）本測試釘的是舊停步鏈；統一站立開啟時舊鏈被旁路，所以這裡明確關掉它。
            //    統一站立的基線由 Player_UnifiedStanding_StandsAsHighAsBothFeetReach 驗證。
            SetUnifiedStanding(footIK, false);
            Animator animator = null;
            foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) if (a.isHuman) animator = a;
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            var readings = new List<string>();
            float lowestPelvis = 0f, worstReach = 0f;
            foreach (float yaw in new[] { 0f, 90f, 180f })
            {
                Teleport(player, stair, 0f, Quaternion.Euler(0f, yaw, 0f) * stair.Ascent);
                for (int i = 0; i < 180; i++) yield return null; // 3 s：偵測靜止 ＋ 站起來
                float pelvis = footIK.TargetData.PelvisOffsetY;
                // 站著不動 ⇒ root 不移動，畫面腳位就是上一帧套用的目標（沒有議題 3 的位移）
                float reach = Mathf.Max(Vector3.Distance(leftFoot.position, footIK.TargetData.LeftFootPosition),
                    Vector3.Distance(rightFoot.position, footIK.TargetData.RightFootPosition));
                readings.Add($"{yaw}°: pelvis {pelvis:F3} reach {reach:F3}");
                if (yaw == 0f) lowestPelvis = pelvis;
                worstReach = Mathf.Max(worstReach, reach);
            }
            string label = $"[StairPlayMode] standing posture {stairName}: " + string.Join(" | ", readings);
            Debug.Log(label);
            TestContext.WriteLine(label);
            yield return UnloadScene();

            Assert.Greater(lowestPelvis, -0.30f, $"{stairName}：面向上方站著時應站直、高於包絡（−0.335／−0.35）（{label}）");
            Assert.Less(worstReach, 0.02f, $"{stairName}：站直後兩腳仍須搆到 T3 目標（{label}）");
        }

        /// <summary>
        /// 🧪 統一 Moving 骨盆（docs/46 §6／§12，X Bot 開啟）：站原則關掉（只看 Moving 路徑本身），停在樓梯中段、三個朝向，
        /// 兩腳（都是 Support）都必須搆到 T3 目標。高度隨朝向改變是刻意的（兩腳落在不同階），不斷言高度相等。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_UnifiedMoving_SupportFeetReachable_AtAnyFacing(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);
            var footIK = player.GetComponentInChildren<FootIKController>();
            SetStandingPosture(footIK, false);
            SetUnifiedMoving(footIK, true);
            Animator animator = null;
            foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) if (a.isHuman) animator = a;
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            var readings = new List<string>();
            float worstReach = 0f, deepest = 0f;
            foreach (float yaw in new[] { 0f, 90f, 180f })
            {
                Teleport(player, stair, 0f, Quaternion.Euler(0f, yaw, 0f) * stair.Ascent);
                for (int i = 0; i < 180; i++) yield return null; // 3 s：回升（ω_up 1.5）收斂
                float pelvis = footIK.TargetData.PelvisOffsetY;
                float reach = Mathf.Max(Vector3.Distance(leftFoot.position, footIK.TargetData.LeftFootPosition),
                    Vector3.Distance(rightFoot.position, footIK.TargetData.RightFootPosition));
                readings.Add($"{yaw}°: pelvis {pelvis:F3} reach {reach:F3}");
                worstReach = Mathf.Max(worstReach, reach);
                deepest = Mathf.Min(deepest, pelvis);
            }
            string label = $"[StairPlayMode] unified moving {stairName}: " + string.Join(" | ", readings);
            Debug.Log(label);
            TestContext.WriteLine(label);
            yield return UnloadScene();

            Assert.Less(worstReach, 0.02f, $"{stairName}：兩隻 Support 腳都必須搆到 T3 目標（{label}）");
            Assert.GreaterOrEqual(deepest, -0.35f - 1e-4f, $"{stairName}：骨盆不得低於 MaxPelvisOffset（{label}）");
        }

        private static void SetStandingPosture(FootIKController footIK, bool enabled)
        {
            object settings = typeof(FootIKController).GetField("settings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(footIK);
            settings.GetType().GetField("UseStandingPosturePrototype").SetValue(settings, enabled);
        }

        /// <summary>
        /// 🧪 統一站立（docs/46 §14，X Bot 開啟）：停在樓梯中段三個朝向，舊停步鏈被旁路 ⇒
        /// 兩隻腳都搆到 T3 目標、骨盆不低於下限，且站起來之後比移動中的包絡深度（−0.33）高。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_UnifiedStanding_StandsAsHighAsBothFeetReach(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);
            var footIK = player.GetComponentInChildren<FootIKController>();
            SetStandingPosture(footIK, true);
            SetUnifiedMoving(footIK, true);
            SetUnifiedStanding(footIK, true);
            Animator animator = null;
            foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) if (a.isHuman) animator = a;
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            var readings = new List<string>();
            float worstReach = 0f, deepest = 0f, facingUp = 0f;
            foreach (float yaw in new[] { 0f, 90f, 180f })
            {
                Teleport(player, stair, 0f, Quaternion.Euler(0f, yaw, 0f) * stair.Ascent);
                for (int i = 0; i < 180; i++) yield return null;
                float pelvis = footIK.TargetData.PelvisOffsetY;
                float reach = Mathf.Max(Vector3.Distance(leftFoot.position, footIK.TargetData.LeftFootPosition),
                    Vector3.Distance(rightFoot.position, footIK.TargetData.RightFootPosition));
                readings.Add($"{yaw}°: pelvis {pelvis:F3} reach {reach:F3}");
                if (yaw == 0f) facingUp = pelvis;
                worstReach = Mathf.Max(worstReach, reach);
                deepest = Mathf.Min(deepest, pelvis);
            }
            string label = $"[StairPlayMode] unified standing {stairName}: " + string.Join(" | ", readings);
            Debug.Log(label);
            TestContext.WriteLine(label);
            yield return UnloadScene();

            Assert.Less(worstReach, 0.02f, $"{stairName}：站著時兩隻腳都必須搆到 T3 目標（{label}）");
            Assert.GreaterOrEqual(deepest, -0.35f - 1e-4f, $"{stairName}：骨盆不得低於 MaxPelvisOffset（{label}）");
            Assert.Greater(facingUp, -0.30f, $"{stairName}：面向上方站著時應高於移動中的包絡（{label}）");
        }

        private static void SetUnifiedStanding(FootIKController footIK, bool enabled)
        {
            object settings = typeof(FootIKController).GetField("settings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(footIK);
            settings.GetType().GetField("UseUnifiedStandingPelvis").SetValue(settings, enabled);
        }

        private static void SetUnifiedMoving(FootIKController footIK, bool enabled)
        {
            object settings = typeof(FootIKController).GetField("settings",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(footIK);
            settings.GetType().GetField("UseUnifiedMovingPelvis").SetValue(settings, enabled);
        }

        /// <summary>
        /// `Stairs (1)` 頂端是開放邊緣（下方 3.7 m 是地面）：走出去必須正常墜落，樓梯貼地不得把角色黏在半空。
        /// </summary>
        [UnityTest]
        public IEnumerator Player_WalkOffOpenTopEdge_FallsNormally()
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, "Stairs (1)");
            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(player);

            Teleport(player, stair, stair.TopS - 0.12f, stair.Ascent);
            for (int i = 0; i < 20; i++) yield return null;
            float topY = player.transform.position.y;

            Press(_keyboard.leftCtrlKey);
            Press(_keyboard.wKey);
            bool enteredJump = false;
            float minY = topY;
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;
                if (player.CurrentState == StateType.Jump) enteredJump = true;
                minY = Mathf.Min(minY, player.transform.position.y);
                if (enteredJump && player.CurrentState != StateType.Jump) break;
            }
            Release(_keyboard.wKey);
            Release(_keyboard.leftCtrlKey);

            string summary = $"topY={topY:F2} minY={minY:F2} enteredJump={enteredJump}";
            Debug.Log("[StairPlayMode] ledge fall: " + summary);
            yield return UnloadScene();

            Assert.IsTrue(enteredJump, $"走出開放頂端沒有進入 Falling（{summary}）");
            Assert.Less(minY, topY - 3f, $"走出開放頂端沒有掉到地面（{summary}）");
        }

        // ================================================================ Queries

        /// <summary>
        /// 每個查詢系統用**執行期實際的遮罩**對兩道樓梯中段往下打：非移動類查詢必須命中看得到的踏面
        /// （移動面在踏面上方至多一級，遮罩若誤含它會先打到它）；移動類查詢必須命中移動面。
        /// </summary>
        [UnityTest]
        public IEnumerator Queries_HitTheSemanticallyIntendedStairSurface()
        {
            yield return LoadScene();
            var failures = new List<string>();
            var report = new StringBuilder("[StairPlayMode] queries:");

            var queries = new List<(string Name, int Mask, bool ExpectMovement)>();
            void AddSerialized(Component component, string path, bool expectMovement)
            {
                Assert.IsNotNull(component, path);
                var so = new UnityEditor.SerializedObject(component);
                queries.Add(($"{component.name}/{component.GetType().Name}.{path}", (int)so.FindProperty(path + ".m_Bits").longValue, expectMovement));
            }

            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            AddSerialized(player.GetComponentInChildren<FootIKController>(), "settings.GroundLayers", false);
            AddSerialized(enemy.GetComponentInChildren<FootIKController>(), "settings.GroundLayers", false);
            AddSerialized(player.GetComponentInChildren<Project.Presentation.CameraControl.AimResolver>(true), "aimRayMask", false);
            AddSerialized(player.GetComponentInChildren<Project.Core.Environment.TraversalProbe>(true), "settings.obstacleMask", false);
            AddSerialized(player.GetComponentInChildren<Project.Presentation.Actions.GroundEffectSink>(true), "groundMask", false);
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Project.Presentation.CameraControl.ThirdPersonCamera>(true))
                    AddSerialized(camera, "obstructionMask", false);
            foreach (string path in new[] { "Assets/Prefabs/Projectile_Fireball.prefab", "Assets/Prefabs/ThrownProjectile.prefab" })
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                AddSerialized(prefab.GetComponentInChildren<Project.Presentation.Actions.ThrownProjectile>(true), "sweepMask", false);
            }
            queries.Add(("MotionDriver.MovementCollisionMask(Player)", MotionDriver.MovementCollisionMask(player.gameObject.layer), true));
            queries.Add(("MotionDriver.MovementCollisionMask(Enemy)", MotionDriver.MovementCollisionMask(enemy.gameObject.layer), true));

            foreach (string stairName in new[] { "Stairs_650_400_300_Prefab", "Stairs (1)" })
            {
                StairFrame stair = StairFrame.Find(_scene, stairName);
                // 中段踏面的後半：這裡移動面高出踏面最多（接近一級），兩者最容易分辨。
                Vector3 xz = stair.PointAt(0f);
                var origin = new Vector3(xz.x, 50f, xz.z);
                foreach (var query in queries)
                {
                    if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 100f, query.Mask, QueryTriggerInteraction.Ignore))
                    {
                        failures.Add($"{stairName} {query.Name}：沒有命中任何東西");
                        continue;
                    }
                    int expected = query.ExpectMovement ? stair.MovementLayer : stair.TreadLayer;
                    report.Append($" {stairName}|{query.Name}->{LayerMask.LayerToName(hit.collider.gameObject.layer)}@{hit.point.y:F3}");
                    if (hit.collider.gameObject.layer != expected)
                        failures.Add($"{stairName} {query.Name}：命中 {LayerMask.LayerToName(hit.collider.gameObject.layer)}（{hit.collider.name}），應為 {LayerMask.LayerToName(expected)}");
                }
            }

            Debug.Log(report.ToString());
            yield return UnloadScene();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ================================================================ Enemy

        [UnityTest]
        public IEnumerator Enemy_TraversesGeneratedStair(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Direction.Up, Direction.Down)] Direction direction)
        {
            yield return LoadScene();
            StairFrame stair = StairFrame.Find(_scene, stairName);
            CharacterPipelineRunner player = FindRunner("X Bot");
            CharacterPipelineRunner enemy = FindRunner("Y Bot");
            AimCamera(player, stair.Ascent);
            AlwaysAnimate(enemy);

            // 只放寬這個場景實例的 aggro 半徑（不改資產）：要驗的是「敵人能不能走完整道樓梯」，
            // 不是 aggro 調校；較長的 prototype 樓梯兩端距離超過預設的 8 m。
            // 🔄（2026-10-12，`docs/54` §3.3.2）aggro 半徑住在 AICombatContextSource。
            var ai = enemy.GetComponent<Project.Core.Combat.AICombatContextSource>();
            Assert.IsNotNull(ai, "Y Bot 沒有 AICombatContextSource");
            SetPrivateField(ai, "aggroEnterRadius", 30f);
            SetPrivateField(ai, "aggroLeaveRadius", 40f);

            // 玩家站在另一端當誘餌（不輸入）；敵人會停在 1.25–2 m 接戰距離。
            float playerS = direction == Direction.Up ? stair.TopS - 0.12f : stair.BottomS - 2.5f;
            float enemyS = direction == Direction.Up ? stair.BottomS - 1.5f : stair.TopS - 0.12f;
            Teleport(player, stair, playerS, direction == Direction.Up ? -stair.Ascent : stair.Ascent);
            Teleport(enemy, stair, enemyS, direction == Direction.Up ? stair.Ascent : -stair.Ascent);
            // 🔄（2026-10-13，docs/55 P1）敵人有了領地與 leash（以出生點為家）。本測試把敵人瞬移到樓梯旁，
            // 要驗的是「能不能走完整道樓梯」，不是 leash——比照上面放寬 aggro 的做法，把這個實例的家設在瞬移點、
            // 半徑放大到不會介入（不改資產）。
            var territory = enemy.GetComponent<Project.Core.Movement.EnemyTerritory>();
            if (territory != null)
                territory.ConfigureForTests(enemy.transform.position, roam: 60f, soft: 70f, hard: 80f);
            for (int i = 0; i < 20; i++) yield return null;

            var trace = new Trace(stair, enemy);
            float goalS = direction == Direction.Up ? stair.TopS - 2.3f : stair.BottomS - 0.2f;
            bool reached = false;
            int settleFrames = 0;
            for (int frame = 0; frame < MaximumFrames; frame++)
            {
                yield return null;
                trace.Sample();
                float s = stair.S(enemy.transform.position);
                if (!reached) reached = direction == Direction.Up ? s >= goalS : s <= goalS;
                if (reached && ++settleFrames > 30) break;
            }

            string label = $"Enemy {stairName} {direction}";
            string where = $"停在 s={stair.S(enemy.transform.position):F2}，目標 {goalS:F2}";
            trace.Report(label);
            yield return UnloadScene();

            Assert.IsTrue(reached, $"{label}：敵人沒有走過樓梯（{where}）");
            trace.AssertContract(label, requireNoGroundLoss: true, requireContinuity: true);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} 不存在");
            field.SetValue(target, value);
        }

        // ================================================================ helpers

        private CharacterPipelineRunner FindRunner(string name)
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (CharacterPipelineRunner runner in root.GetComponentsInChildren<CharacterPipelineRunner>(true))
                    if (runner.gameObject.name == name) return runner;
            Assert.Fail($"場景裡找不到 {name}");
            return null;
        }

        private void SetActive(string name, bool active)
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) t.gameObject.SetActive(active);
        }

        /// <summary>移動輸入是相機相對的：固定一個面向上坡方向的相機，W＝上樓、S＝下樓。</summary>
        private void AimCamera(CharacterPipelineRunner player, Vector3 ascent)
        {
            _testCamera.transform.rotation = Quaternion.LookRotation(ascent, Vector3.up);
            player.RuntimeData.CameraTransform = _testCamera.transform;
        }

        private static void Teleport(CharacterPipelineRunner runner, StairFrame stair, float s, Vector3 facing)
        {
            Vector3 xz = stair.PointAt(s);
            float y = stair.SurfaceY(xz) + 0.05f;
            var controller = runner.GetComponent<CharacterController>();
            controller.enabled = false;
            var position = new Vector3(xz.x, y, xz.z);
            runner.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            controller.enabled = true;
            // AI 的 NavMeshAgent 不擁有 Transform；瞬移後必須同步它的內部位置，否則路徑從舊位置算。
            var agent = runner.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.enabled)
            {
                Assert.IsTrue(agent.Warp(position), $"{runner.name}：NavMeshAgent.Warp 失敗（{position}）");
                Assert.IsTrue(agent.isOnNavMesh, $"{runner.name}：瞬移後不在 NavMesh 上");
            }
            runner.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            Physics.SyncTransforms();
        }

        /// <summary>樓梯的上升座標系，全部由場景中實際的踏面碰撞體量出來（不寫死任何座標）。</summary>
        private sealed class StairFrame
        {
            public string Name;
            public Collider Tread;
            public Vector3 Center;
            public Vector3 Ascent;
            public float BottomS;
            public float TopS;
            public float HalfWidth;
            public int TreadLayer;
            public int MovementLayer;

            public float S(Vector3 p) => Vector3.Dot(new Vector3(p.x - Center.x, 0f, p.z - Center.z), Ascent);
            public float Lateral(Vector3 p) => Vector3.Dot(new Vector3(p.x - Center.x, 0f, p.z - Center.z), Vector3.Cross(Vector3.up, Ascent));
            public Vector3 PointAt(float s) => Center + Ascent * s;

            /// <summary>角色實際會站上去的表面（移動世界：看得到移動面、看不到踏面）。</summary>
            public float SurfaceY(Vector3 xz)
            {
                int movementWorld = ~((1 << TreadLayer) | (1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("Enemy")));
                return Physics.Raycast(new Vector3(xz.x, 50f, xz.z), Vector3.down, out RaycastHit hit, 100f, movementWorld, QueryTriggerInteraction.Ignore)
                    ? hit.point.y : 0f;
            }

            public static StairFrame Find(Scene scene, string name)
            {
                Collider tread = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == name) tread = t.GetComponent<Collider>();
                Assert.IsNotNull(tread, $"場景裡找不到樓梯 {name}");

                var frame = new StairFrame
                {
                    Name = name,
                    Tread = tread,
                    TreadLayer = LayerMask.NameToLayer("StairTread"),
                    MovementLayer = LayerMask.NameToLayer("CharacterMovementSurface"),
                };
                Assert.AreEqual(frame.TreadLayer, tread.gameObject.layer, $"{name} 不在 StairTread layer——移動面沒有產生");

                Bounds b = tread.bounds;
                frame.Center = new Vector3(b.center.x, 0f, b.center.z);
                // 上升方向：在兩個水平軸的兩端各量一次踏面高度，高度差最大、朝較高的一端。
                float bestRise = 0f;
                foreach (Vector3 axis in new[] { Vector3.right, Vector3.forward })
                {
                    float half = Vector3.Dot(b.extents, axis) - 0.1f;
                    float low = TreadY(tread, frame.Center - axis * half);
                    float high = TreadY(tread, frame.Center + axis * half);
                    float rise = high - low;
                    if (Mathf.Abs(rise) > Mathf.Abs(bestRise)) { bestRise = rise; frame.Ascent = rise > 0f ? axis : -axis; }
                }
                float extent = Mathf.Abs(Vector3.Dot(b.extents, frame.Ascent));
                frame.BottomS = -extent;
                frame.TopS = extent;
                frame.HalfWidth = Mathf.Abs(Vector3.Dot(b.extents, Vector3.Cross(Vector3.up, frame.Ascent)));
                return frame;
            }

            private static float TreadY(Collider c, Vector3 xz) =>
                c.Raycast(new Ray(new Vector3(xz.x, c.bounds.max.y + 1f, xz.z), Vector3.down), out RaycastHit hit, 100f) ? hit.point.y : float.NaN;
        }

        /// <summary>逐帧紀錄。只有角色投影落在樓梯範圍內的帧才計入契約判定。</summary>
        private sealed class Trace
        {
            private readonly StairFrame _stair;
            private readonly CharacterPipelineRunner _runner;
            private readonly FootIKController _footIK;

            private int _onStairFrames;
            private int _jumpStateFrames;
            private int _groundedFalseFrames;
            private int _landed;
            private int _leftGround;
            private int _ikTreadHits;
            private int _ikMovementSurfaceHits;
            private int _ikOtherHits;
            private readonly List<float> _verticalSpeeds = new List<float>();
            private float _previousY = float.NaN;
            private float _previousDeltaTime;
            private readonly List<float> _positionsS = new List<float>();
            private readonly List<float> _horizontalSpeeds = new List<float>();
            private readonly Dictionary<string, int> _ikOtherColliders = new Dictionary<string, int>();
            private float _maxLateralOffset;
            private float _ikOtherMinS = float.PositiveInfinity, _ikOtherMaxS = float.NegativeInfinity;
            private float _previousS;
            private readonly List<float> _separations = new List<float>();

            /// <summary>root 高出「角色應該站著的表面」的最大值——量角色是否離開移動面在空中飛。</summary>
            private float _maxSeparation = float.NegativeInfinity;
            private float _minS = float.PositiveInfinity, _maxS = float.NegativeInfinity;

            public Trace(StairFrame stair, CharacterPipelineRunner runner)
            {
                _stair = stair;
                _runner = runner;
                _footIK = runner.GetComponentInChildren<FootIKController>();
                Assert.IsNotNull(_footIK, runner.name + " 沒有 FootIKController");
            }

            /// <summary>false ⇒ 只停止累積垂直連續性與離面高度（其餘不變量照常計算）。</summary>
            public bool MeasureContinuity = true;

            public string Summary { get; private set; } = "";

            public void Sample()
            {
                Vector3 p = _runner.transform.position;
                float y = p.y;
                // ⚠️ 取樣點在 Update 之後、本帧 LateUpdate 的 Move() 之前 ⇒ 這次看到的位移是
                //    **上一帧**用上一帧 deltaTime 走出來的。除以本帧 deltaTime 會把帧時間抖動變成假的速度跳變（實測）。
                float dt = _previousDeltaTime;
                _previousDeltaTime = Time.deltaTime;
                float s = _stair.S(p);
                bool onStair = s >= _stair.BottomS && s <= _stair.TopS;

                if (onStair && !float.IsNaN(_previousY) && dt > 0f)
                {
                    _onStairFrames++;
                    _maxLateralOffset = Mathf.Max(_maxLateralOffset, Mathf.Abs(_stair.Lateral(p)));
                    _minS = Mathf.Min(_minS, s);
                    _maxS = Mathf.Max(_maxS, s);
                    if (MeasureContinuity)
                    {
                        _verticalSpeeds.Add((y - _previousY) / dt);
                        _horizontalSpeeds.Add(Mathf.Abs(s - _previousS) / dt);
                        _positionsS.Add(s);
                        float separation = y - _stair.SurfaceY(p);
                        _separations.Add(separation);
                        _maxSeparation = Mathf.Max(_maxSeparation, separation);
                    }

                    var data = _runner.RuntimeData;
                    if (_runner.CurrentState == StateType.Jump) _jumpStateFrames++;
                    if (!data.IsGrounded) _groundedFalseFrames++;
                    if (data.JustLanded) _landed++;
                    if (data.JustLeftGround) _leftGround++;

                    CountIKHit(_footIK.GroundData.Left.HasGround, _footIK.GroundData.Left.Collider);
                    CountIKHit(_footIK.GroundData.Right.HasGround, _footIK.GroundData.Right.Collider);
                }
                _previousY = y;
                _previousS = s;
            }

            private void CountIKHit(bool hasGround, Collider collider)
            {
                if (!hasGround || collider == null) return;
                int layer = collider.gameObject.layer;
                if (layer == _stair.MovementLayer) _ikMovementSurfaceHits++;
                else if (layer == _stair.TreadLayer) _ikTreadHits++;
                else
                {
                    _ikOtherHits++;
                    float s = _stair.S(_runner.transform.position);
                    _ikOtherMinS = Mathf.Min(_ikOtherMinS, s);
                    _ikOtherMaxS = Mathf.Max(_ikOtherMaxS, s);
                    string key = collider.name + "[" + LayerMask.LayerToName(layer) + "]";
                    _ikOtherColliders.TryGetValue(key, out int count);
                    _ikOtherColliders[key] = count + 1;
                }
            }

            /// <summary>中段（去頭尾各 15% 帧）相鄰帧垂直速度差的最大值——量 root 是否連續。</summary>
            public float MaxVerticalSpeedChange() => VerticalSpeedChangePercentile(1f);

            public float VerticalSpeedChangePercentile(float percentile)
            {
                int n = _verticalSpeeds.Count;
                int from = n * 15 / 100, to = n - n * 15 / 100;
                var changes = new List<float>();
                for (int i = from + 1; i < to; i++) changes.Add(Mathf.Abs(_verticalSpeeds[i] - _verticalSpeeds[i - 1]));
                if (changes.Count == 0) return 0f;
                changes.Sort();
                return changes[Mathf.Clamp(Mathf.CeilToInt(percentile * changes.Count) - 1, 0, changes.Count - 1)];
            }

            public void Report(string label)
            {
                var sb = new StringBuilder();
                sb.Append($"[StairPlayMode] {label}: onStairFrames={_onStairFrames} s=[{_minS:F2},{_maxS:F2}]/[{_stair.BottomS:F2},{_stair.TopS:F2}]")
                  .Append($" jumpStateFrames={_jumpStateFrames} groundedFalse={_groundedFalseFrames} justLanded={_landed} justLeftGround={_leftGround}")
                  .Append($" ikTread={_ikTreadHits} ikMovementSurface={_ikMovementSurfaceHits} ikOther={_ikOtherHits}")
                  .Append($" deltaVy p50={VerticalSpeedChangePercentile(0.5f):F3} p95={VerticalSpeedChangePercentile(0.95f):F3} max={MaxVerticalSpeedChange():F3}")
                  .Append($" maxSeparation={_maxSeparation:F3} maxLateral={_maxLateralOffset:F2}");
                if (_ikOtherColliders.Count > 0)
                {
                    sb.Append($" ikOtherS=[{_ikOtherMinS:F2},{_ikOtherMaxS:F2}] ikOtherColliders=");
                    foreach (var pair in _ikOtherColliders) sb.Append(pair.Key).Append('×').Append(pair.Value).Append(' ');
                }
                // 最大的三個垂直速度跳變發生在哪裡（s 與當下離面高度）——定位是轉折處還是坡面中段。
                var spikes = new List<int>();
                for (int i = 1; i < _verticalSpeeds.Count; i++) spikes.Add(i);
                spikes.Sort((a, c) => Mathf.Abs(_verticalSpeeds[c] - _verticalSpeeds[c - 1]).CompareTo(Mathf.Abs(_verticalSpeeds[a] - _verticalSpeeds[a - 1])));
                sb.Append(" topSpikes=");
                for (int k = 0; k < Mathf.Min(3, spikes.Count); k++)
                {
                    int i = spikes[k];
                    sb.Append($"[i={i}/{_verticalSpeeds.Count} s={_positionsS[i]:F2} vy {_verticalSpeeds[i - 1]:F2}->{_verticalSpeeds[i]:F2} " +
                              $"vh {_horizontalSpeeds[i - 1]:F2}->{_horizontalSpeeds[i]:F2} sep={_separations[i]:F3}]");
                }
                Summary = sb.ToString();
                Debug.Log(Summary);
                TestContext.WriteLine(Summary);
            }

            /// <summary>停步情境：不要求覆蓋率與連續性（Stop 本身在平地就有 2 帧水平停頓），只要求不失地、不誤判空中。</summary>
            public void AssertStopContract(string label)
            {
                Assert.AreEqual(0, _jumpStateFrames, $"{label}：停在樓梯上時進入了 Jump／Falling 狀態");
                Assert.AreEqual(0, _landed, $"{label}：停在樓梯上時發出落地事件");
                Assert.AreEqual(0, _leftGround, $"{label}：停在樓梯上時發出離地事件");
                Assert.AreEqual(0, _ikMovementSurfaceHits, $"{label}：Foot IK 踩到了看不見的移動面");
                Assert.AreEqual(0, _groundedFalseFrames, $"{label}：停在樓梯上時有 isGrounded=false 的帧");
            }

            public void AssertContract(string label, bool requireNoGroundLoss, bool requireContinuity)
            {
                float coverage = (_maxS - _minS) / (_stair.TopS - _stair.BottomS);
                Assert.Greater(coverage, 0.6f, $"{label}：只走過樓梯的 {coverage:P0}");
                Assert.AreEqual(0, _jumpStateFrames, $"{label}：在樓梯上進入了 Jump／Falling 狀態");
                Assert.AreEqual(0, _landed, $"{label}：在樓梯上發出落地事件");
                Assert.AreEqual(0, _leftGround, $"{label}：在樓梯上發出離地事件");
                Assert.AreEqual(0, _ikMovementSurfaceHits, $"{label}：Foot IK 踩到了看不見的移動面");
                Assert.Greater(_ikTreadHits, 0, $"{label}：Foot IK 從沒踩到真實踏面");
                // 其他地面只允許出現在樓梯兩端（腳還踩在入口／出口地面上）。
                float treadRatio = (float)_ikTreadHits / (_ikTreadHits + _ikOtherHits);
                Assert.GreaterOrEqual(treadRatio, 0.9f, $"{label}：Foot IK 在樓梯上只有 {treadRatio:P0} 命中踏面");
                Assert.Less(_maxLateralOffset, _stair.HalfWidth, $"{label}：角色離開了樓梯寬度範圍");
                if (requireContinuity)
                    Assert.LessOrEqual(MaxVerticalSpeedChange(), MaximumVerticalSpeedChange, $"{label}：root 垂直速度不連續");
                if (requireNoGroundLoss)
                    Assert.AreEqual(0, _groundedFalseFrames, $"{label}：樓梯上有 isGrounded=false 的帧");
            }
        }
    }
}
