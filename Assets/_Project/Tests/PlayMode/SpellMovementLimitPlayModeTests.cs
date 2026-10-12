using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🆕（2026-10-12，`docs/54` AC-3a.8／驗收⑦）**施法限速的首帧延遲：量測，不是假設。**
    ///
    /// 出貨場景、真輸入（D 側移 → Q 施放 Fireball）、真 Runner，固定 60 fps，跑兩次：
    /// <list type="bullet">
    /// <item><b>A（正式）</b>：宣告由 <c>ActionState</c> 在順序 4 發出 ⇒ model 在進入帧 N 不受限、N+1 起受限。</item>
    /// <item><b>B（反事實：零延遲）</b>：在 N 的 Update 之前，測試先替 model 蓋一個宣告 ⇒ N 就受限。
    /// 這等於舊行為（第一次施法時 InCombat 於順序 2.4 就成立）。⚠️ 這只是量測用的對照組，**不是**修法——
    /// 正式程式不得讓任何東西在 FSM 之前預先宣告（`docs/54` §3.5.7-① Z1 已否決）。</item>
    /// </list>
    /// A 與 B 的差＝首帧延遲的全部影響。逐帧資料寫到 <c>Logs/wp3a-first-frame-delay.csv</c>。
    /// </summary>
    public sealed class SpellMovementLimitPlayModeTests : InputTestFixture
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int FirstCastPreFrames = 10;
        private const int RecastPreFrames = 2;
        private const int PostFrames = 50;
        private const float FixedDelta = 1f / 60f;

        private Keyboard _keyboard;
        private Scene _scene;

        private struct Sample
        {
            public int RelativeFrame;
            public bool InAction;
            public float MoveSpeed;
            public float DisplacementSpeed;
            public float LocalDirectionCap;
            public float IntentSpeed;
            public Vector3 Position;
        }

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = 0f;
            base.TearDown();
        }

        /// <param name="recast">
        /// false＝第一次施法（角色面向移動方向）；true＝**冷卻一結束就再施法**（Fireball 冷卻 0.4 s 擋住
        /// 「結束當帧就再施法」，這是最早可達的再施法）——朝向剛從上一次施法回正、速度正在回升。
        /// </param>
        /// <remarks>
        /// ⚠️ 量測方法的教訓（2026-10-12 實測）：再施法情境下 A／B 兩次執行在**施法之前**就已相差約 3 cm
        /// （第一次施法後的相機／瞄準時序不可重現），兩者之後的差不能歸因於首帧延遲。
        /// ⇒ 再施法只跑正式路徑，改用**解析**的對照：進入帧 N 宣告唯一能改變的是 smoother 的目標，
        /// 由 <c>intent</c> 變成 <c>min(intent, cap_N)</c>；兩者之差就是首帧延遲在這一帧的全部影響。
        /// 第一次施法的 A／B 執行逐帧可重現，仍保留直接比較。
        /// </remarks>
        [UnityTest]
        public IEnumerator AC3a8_FirstFrameDelay_IsMeasuredAndBounded([Values(false, true)] bool recast)
        {
            int preFrames = recast ? RecastPreFrames : FirstCastPreFrames;
            var production = new List<Sample>();
            var zeroDelay = new List<Sample>();
            yield return Run(production, emulateZeroDelay: false, recast, preFrames);
            if (!recast) yield return Run(zeroDelay, emulateZeroDelay: true, recast, preFrames);

            WriteCsv(production, recast ? production : zeroDelay, recast ? "recast" : "first");

            int entry = preFrames;   // 相對帧 0＝按下 Q 的那一帧（Action 進入帧 N）
            Assert.IsTrue(production[entry].InAction, "前提：在帧 N 進入施法");
            Assert.IsFalse(production[entry - 1].InAction, "前提：N-1 還沒施法");

            // 解析對照：進入帧 N 的 smoother 目標，正式（不受限）vs 零延遲（受限）。
            float intentAtN = production[entry].IntentSpeed;
            float entryTargetGap = intentAtN - Mathf.Min(intentAtN, production[entry].LocalDirectionCap);

            float maxStep = 0f;
            int firstLimitedFrame = int.MaxValue;
            for (int i = 1; i < production.Count; i++)
            {
                maxStep = Mathf.Max(maxStep, Mathf.Abs(production[i].MoveSpeed - production[i - 1].MoveSpeed));
                if (firstLimitedFrame == int.MaxValue && production[i].InAction &&
                    production[i].LocalDirectionCap < production[i].IntentSpeed)
                {
                    firstLimitedFrame = production[i].RelativeFrame;
                }
            }

            string summary =
                $"[WP3a first-frame {(recast ? "recast" : "first")}] intentAtN={intentAtN:F4} capAtN={production[entry].LocalDirectionCap:F4} " +
                $"entryTargetGap={entryTargetGap:F4} firstFrameCapBites=N+{firstLimitedFrame} " +
                $"speed N-1={production[entry - 1].MoveSpeed:F4} N={production[entry].MoveSpeed:F4} N+1={production[entry + 1].MoveSpeed:F4} " +
                $"speed N+30={production[entry + 30].MoveSpeed:F4} maxStep={maxStep:F4}";
            if (!recast)
            {
                // ⚠️ 只比較到 cap 開始咬住之前：之後的速度由施法朝向決定，而朝向（瞄準）在兩次執行間
                //    不可重現（全套回歸中實測 N+7 起 cap 0.5462 vs 0.5417）——那是朝向雜訊，不是首帧延遲。
                //    宣告只可能影響帧 N，所以 [N, 咬住前] 這一段就是延遲效果的完整觀察窗。
                int window = Mathf.Min(production.Count, entry + Mathf.Max(1, firstLimitedFrame));
                float windowSpeedDiff = 0f, windowPositionDiff = 0f, totalPositionDiff = 0f;
                for (int i = 0; i < production.Count; i++)
                {
                    Vector3 pa = production[i].Position, pb = zeroDelay[i].Position;
                    pa.y = pb.y = 0f;
                    float positionDiff = Vector3.Distance(pa, pb);
                    totalPositionDiff = Mathf.Max(totalPositionDiff, positionDiff);
                    if (i >= window) continue;
                    windowSpeedDiff = Mathf.Max(windowSpeedDiff, Mathf.Abs(production[i].MoveSpeed - zeroDelay[i].MoveSpeed));
                    windowPositionDiff = Mathf.Max(windowPositionDiff, positionDiff);
                }
                summary += $" A/B window[N-{entry},N+{firstLimitedFrame}) speedDiff={windowSpeedDiff:F4} positionDiff={windowPositionDiff * 100f:F3}cm" +
                           $" (whole run positionDiff={totalPositionDiff * 100f:F3}cm, facing noise included)";
                Assert.Less(windowSpeedDiff, 0.002f, "第一次施法：cap 咬住前，正式路徑與零延遲對照組的速度必須一致（延遲不可見）");
                Assert.Less(windowPositionDiff, 0.005f, "第一次施法：cap 咬住前，兩者位置差必須 < 0.5 cm");
                Assert.AreEqual(production[entry - 1].MoveSpeed, production[entry].MoveSpeed, 0.002f,
                    "第一次施法：進入帧 N 的速度與 N-1 的穩態相同（限速在 N 尚未生效）");
            }
            Debug.Log(summary);
            TestContext.WriteLine(summary);

            // 可達的鍵盤情境中，進入帧的朝向仍跟著移動方向（本地＝前方），前方 cap ≥ 意圖速度
            // ⇒ 首帧延遲**不改變**任何東西。這條若紅了，代表調校或朝向行為變了——先重量、回報，⛔ 不得改管線。
            Assert.AreEqual(0f, entryTargetGap, 1e-4f,
                "進入帧 N 的意圖已在本地方向的 cap 之內 ⇒ 首帧延遲在此情境沒有效果");
            Assert.Less(maxStep, 0.02f, "施法前後逐帧速度變化必須平順（無突變）");

            // 限速確實在施法期間生效（cap 咬到之後才會）。
            Assert.Less(firstLimitedFrame, PostFrames, "前提：施法期間朝向轉出後，cap 必須低於意圖速度");
            Assert.Less(production[entry + 30].MoveSpeed, production[entry - 1].MoveSpeed - 0.05f,
                "施法期間側移必須被 Bake 推導的上限壓低");
        }

        private IEnumerator Run(List<Sample> samples, bool emulateZeroDelay, bool recast, int preFrames)
        {
            Time.captureDeltaTime = FixedDelta;
            AsyncOperation load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            yield return load;
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);

            CharacterPipelineRunner player = FindRunner("X Bot");
            SetActive("Y Bot", false);   // 純粹的「非交戰施法」：沒有任何交戰語境介入
            var model = player.GetComponent<LocomotionModel>();
            Assert.IsNotNull(model);
            var profile = (CombatDirectionalSpeedProfileSO)typeof(LocomotionModel)
                .GetField("combatDirectionalSpeedProfile", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(model);
            Assert.IsNotNull(profile);
            for (int i = 0; i < 10; i++) yield return null;

            // 穩態側移：D（相機右）。角色面向移動方向 ⇒ 尚未施法時本地方向＝前方。
            Press(_keyboard.dKey);
            for (int i = 0; i < 60; i++) yield return null;

            if (recast)
            {
                // 先完整施放一次，等它自然結束；量測從結束後 preFrames 帧內的再施法開始。
                Press(_keyboard.qKey);
                yield return null;
                Release(_keyboard.qKey);
                int guard = 0;
                while (player.CurrentState == StateType.Action && guard++ < 600) yield return null;
                Assert.AreNotEqual(StateType.Action, player.CurrentState, "前提：第一次施法已結束");
                // 冷卻（0.4 s）擋住「結束當帧就再施法」——最早可達的再施法是冷卻剛結束的那一帧。
                // 這裡只讀 HUD 用的唯讀量來對齊時機，不參與出手判斷。
                guard = 0;
                while (player.GetActionCooldownNormalized(Project.Core.Actions.ActionSlot.Slot2) > 0f && guard++ < 600)
                    yield return null;
            }

            Vector3 previous = player.transform.position;
            for (int frame = -preFrames; frame <= PostFrames; frame++)
            {
                if (frame == 0)
                {
                    Press(_keyboard.qKey);   // 與 D 不同帧，避開同帧兩個鍵盤事件的卡鍵坑
                    if (emulateZeroDelay) model.SetActionMovementLimit(ActionMovementLimit.DirectionalSpeed);
                }
                if (frame == 1) Release(_keyboard.qKey);

                // 本帧 Update 前的朝向決定本帧的本地方向（與 model 讀的同一個 transform.rotation）。
                Vector3 local = Quaternion.Inverse(player.transform.rotation) * player.RuntimeData.MovementIntent.DesiredDirection;
                float intentSpeed = player.RuntimeData.MovementIntent.DesiredSpeedNormalized;
                float cap = player.RuntimeData.MovementIntent.DesiredDirection.sqrMagnitude > 0f
                    ? profile.ResolveNormalizedSpeed(new Vector2(local.x, local.z))
                    : 1f;

                yield return null;

                Vector3 position = player.transform.position;
                Vector3 delta = position - previous;
                delta.y = 0f;
                previous = position;
                samples.Add(new Sample
                {
                    RelativeFrame = frame,
                    InAction = player.CurrentState == StateType.Action,
                    MoveSpeed = player.RuntimeData.MoveSpeed,
                    DisplacementSpeed = delta.magnitude / FixedDelta,
                    LocalDirectionCap = cap,
                    IntentSpeed = intentSpeed,
                    Position = position,
                });
            }

            Release(_keyboard.dKey);
            yield return SceneManager.UnloadSceneAsync(_scene);
            Time.captureDeltaTime = 0f;
        }

        private static void WriteCsv(List<Sample> a, List<Sample> b, string label)
        {
            var csv = new StringBuilder("frame,inActionA,capA,moveSpeedA,dispSpeedA,moveSpeedB,dispSpeedB,posDiffCm\n");
            for (int i = 0; i < a.Count; i++)
            {
                Vector3 pa = a[i].Position, pb = b[i].Position;
                pa.y = pb.y = 0f;
                csv.Append(a[i].RelativeFrame).Append(',')
                   .Append(a[i].InAction ? 1 : 0).Append(',')
                   .Append(a[i].LocalDirectionCap.ToString("F4")).Append(',')
                   .Append(a[i].MoveSpeed.ToString("F4")).Append(',')
                   .Append(a[i].DisplacementSpeed.ToString("F4")).Append(',')
                   .Append(b[i].MoveSpeed.ToString("F4")).Append(',')
                   .Append(b[i].DisplacementSpeed.ToString("F4")).Append(',')
                   .Append((Vector3.Distance(pa, pb) * 100f).ToString("F3")).Append('\n');
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", $"wp3a-first-frame-delay-{label}.csv"));
            File.WriteAllText(path, csv.ToString());
            TestContext.WriteLine("csv: " + path);
        }

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
    }
}
