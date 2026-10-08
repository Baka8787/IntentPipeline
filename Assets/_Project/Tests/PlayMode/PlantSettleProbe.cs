using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Presentation.Animation;
using Project.Presentation.IK;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🔬 THROWAWAY（2026-10-04，只量測、不改 runtime）：兩個 Play 中肉眼可見的問題——
    /// A「收步後已經站好又微調 XZ、偶爾因此換階」、B「Run 起跑短暫拖步」。
    /// 情境：樓梯上 Run 停步 → 靜止 <see cref="SettleFrames"/> 帧 → 同方向再按 Run → 跑完第一個完整步幅。
    /// 逐帧（LateUpdate 最後、IK 解完、骨骼已是最終姿勢）記錄兩腳的每一層：動畫踝（pre-IK）／一帧外插／T3 目標與相位／
    /// 鎖定點／最終腳骨骼（畫面上看到的）／腳跟·腳踝·腳尖各自命中的踏面；以及 root、意圖、停步 runtime、Animancer 狀態。
    /// 輸出 <c>Temp/PlantSettle/</c>（Unity 重啟會清空）。
    /// </summary>
    [Category("Probe")]
    public sealed class PlantSettleProbe : InputTestFixture
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int SettleFrames = 75;
        private const int RestartFrames = 80;
        /// <summary>搖桿原始量：預設 deadzone（0.125–0.925）後 ≈ 0.5 ⇒ 強度 0.75 × 0.5 ≈ 0.375（Walk 停步帶）。</summary>
        private const float AnalogHalfRaw = 0.525f;
        private Keyboard _keyboard;
        private Gamepad _pad;
        private Scene _scene;
        private GameObject _camera;

        public enum Dir { Down, Up }
        public enum Gait { Walk, Run }

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.captureDeltaTime = 0f;
            if (_camera != null) Object.Destroy(_camera);
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest, Explicit]
        public IEnumerator Probe_RunStopSettleRestart(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 2, 4, 6, 8, 10, 12)] int stopIndex)
            => Drive(stairName, dir, yaw, stopIndex, "settle_", null);

        /// <summary>
        /// B 研究（2026-10-04，只量測、⛔ 不改 runtime）：起跑 transition 的起始 phase vs 縮短 crossfade。
        /// 介入只在測試內（反射 Animancer）：base＝不介入；phiN＝按鍵那一帧把 locomotion mixer 的 Walk／Run 子狀態設到 N/8；
        /// fast＝把仍在淡出的停步 clip 立刻停掉，並在動畫參數上把 MoveSpeed 墊到 Walk 門檻（root 的加速不變）。
        /// lu＝前一個停步是 RunFwdStop_LU（proto 樓梯：下 4／上 4），否則 RU（下 6／上 0）。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_RestartPhase(
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(true, false)] bool lu,
            [Values("base", "phi0", "phi1", "phi2", "phi3", "phi4", "phi5", "phi6", "phi7", "fast")] string variant)
        {
            int stopIndex = lu ? 4 : (dir == Dir.Down ? 6 : 0);
            return Drive("Stairs_650_400_300_Prefab", dir, 0f, stopIndex, "phase_" + variant + "_" + (lu ? "LU" : "RU") + "_", variant);
        }

        /// <summary>
        /// B 研究（平地版）：樓梯上 root 會跟著斜坡升降，動畫踝高度無法分辨「貼地滑動」與 root 垂直移動 ⇒ 改在樓梯前的平地做同一實驗。
        /// 平地直線跑 ~6.5 m 後放開（6 個放開位置讓 LU／RU 兩種停步都出現）→ 靜止 → 同方向起跑。輸出 flatphase_<variant>_o<N>_*.csv。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_RestartPhaseFlat(
            [Values(0, 1, 2, 3, 4, 5)] int offset,
            [Values("base", "phi0", "phi1", "phi2", "phi3", "phi4", "phi5", "phi6", "phi7", "fast")] string variant)
            => Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset, "flatphase_" + variant + "_o" + offset + "_", variant);

        /// <summary>
        /// A1 研究（2026-10-04，只量測）：強制 Run 停步選 RU（測試內把場景實例的 runStopVariants 換成只剩 RU；不存檔、不改 runtime）。
        /// 與自然選片（Probe_RunStopSettleRestart）同一組情境比較；自然選 LU 的情境就是「錯腳相入場」的代價。輸出 forcedRU_*.csv。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_ForcedRU(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 2, 4, 6, 8, 10, 12)] int stopIndex)
            => Drive(stairName, dir, yaw, stopIndex, "forcedRU_", "forceRU");

        /// <summary>
        /// A1 平地對照（2026-10-07，只量測）：平地 Run 停步 6 個放開位置 × 自然選片／強制 RU。輸出 a1flat_nat_*／a1flat_ru_*.csv。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_ForcedRUFlat([Values(0, 1, 2, 3, 4, 5)] int offset, [Values("nat", "ru")] string arm)
            => Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset, "a1flat_" + arm + "_o" + offset + "_", arm == "ru" ? "forceRU" : null);

        /// <summary>
        /// A2 實驗（docs/45 §21）：FootIKSettings.PlantAwareExtrapolation 關／開 A/B（只改場景實例設定）。輸出 xab0_*／xab1_*.csv。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_PlantAwareExtrapAB(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 2, 4, 6, 8, 10, 12)] int stopIndex,
            [Values(false, true)] bool on)
            => Drive(stairName, dir, yaw, stopIndex, on ? "xab1_" : "xab0_", on ? "xab1" : "xab0");

        /// <summary>
        /// B 驗收（ADR-011，Trial）：LocomotionModel.alignRunEntryPhase 關／開（只改場景實例）。平地 LU（o0）／RU（o4）＋兩道樓梯上下 7 個位置。
        /// 輸出 entry0_*／entry1_*.csv。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_EntryPhaseFlatAB([Values(0, 4)] int offset, [Values(false, true)] bool on)
            => Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset, on ? "entry1_" : "entry0_", on ? "ep1" : "ep0");

        [UnityTest, Explicit]
        public IEnumerator Probe_EntryPhaseStairsAB(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0, 2, 4, 6, 8, 10, 12)] int stopIndex,
            [Values(false, true)] bool on)
            => Drive(stairName, dir, 0f, stopIndex, on ? "entry1_" : "entry0_", on ? "ep1" : "ep0");

        /// <summary>B 研究：平地細掃起始 phase（pNN ＝ NN/100），LU（o0）與 RU（o4）各一。</summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_RestartPhaseFine(
            [Values(0, 4)] int offset,
            [Values("p72", "p76", "p80", "p84", "p88", "p92", "p96", "p100")] string variant)
            => Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset, "flatphase_" + variant + "_o" + offset + "_", variant);

        /// <summary>
        /// Stop 吸附感診斷：平地 loop 對照 + Stop。p8b 只屬 Run；NUnit 仍依規格展開完整 Values，
        /// Walk/p8b 組合在執行時明確略過，避免把未定義情境混進輸出。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_StopSnapFlat(
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(0, 1, 2, 3, 4, 5)] int offset,
            [Values("on", "zero", "p8b")] string arm)
        {
            if (gait == Gait.Walk && arm == "p8b")
                Assert.Ignore("PLAN §5：PlantAwareExtrapolation 對照只跑 Run。 ");

            string intervention = arm == "zero" ? "zero" : arm == "p8b" ? "xab1" : null;
            return Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset,
                "snap_" + arm + "_o" + offset + "_", intervention, gait);
        }

        /// <summary>Stop 吸附感診斷：proto 樓梯、yaw 0、Walk／Run、IK 開。</summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_StopSnapStairs(
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(2, 6, 10)] int stopIndex,
            [Values(Gait.Walk, Gait.Run)] Gait gait)
            => Drive("Stairs_650_400_300_Prefab", dir, 0f, stopIndex, "snap_on_", null, gait);

        /// <summary>R1c runtime A/B（docs/45 §24）：平地六個停步 offset；cur／f3／f5 只改場景實例，不存資產。</summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_LockFollowFlat(
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(0, 1, 2, 3, 4, 5)] int offset,
            [Values("cur", "f3", "f5")] string arm)
            => Drive("Stairs_650_400_300_Prefab", Dir.Up, 0f, -1 - offset,
                "lf_" + arm + "_o" + offset + "_", "lf_" + arm, gait);

        /// <summary>R1c runtime A/B（docs/45 §24）：proto 樓梯、yaw 0、上下樓三個停步位置。</summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_LockFollowStairs(
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(2, 6, 10)] int stopIndex,
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values("cur", "f3", "f5")] string arm)
            => Drive("Stairs_650_400_300_Prefab", dir, 0f, stopIndex,
                "lf_" + arm + "_", "lf_" + arm, gait);

        /// <summary>
        /// docs/48 M1.3 驗收：下樓停步時規則⑦的輸入（玩家選的 Gait vs LocomotionModel 已承諾的 stop tier）。
        /// walkKey／run：兩者一致 ⇒ M1.3 前後必須**逐位元相同**。
        /// runToWalk：跑到停步點那一帧按 Ctrl、下一帧放開 ⇒ Gait＝Walk、播 Run Stop（鍵盤可達）。
        /// analogWalk：半推搖桿（強度 ≈ 0.375）⇒ Gait＝Run、播 Walk Stop（⚠️ 假設情境：出貨綁定沒有搖桿）。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_RuleSevenStairs(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(2, 6, 10)] int stopIndex,
            [Values("walkKey", "run", "runToWalk", "analogWalk")] string style)
            => Drive(stairName, Dir.Down, 0f, stopIndex, "r7_" + style + "_", null,
                style == "walkKey" ? Gait.Walk : Gait.Run, style == "walkKey" || style == "run" ? null : style);

        private IEnumerator Drive(string stairName, Dir dir, float yaw, int stopIndex, string prefix, string restartVariant,
            Gait gait = Gait.Run, string stopStyle = null)
        {
            const float diagonalRun = 2.5f;
            Time.captureDeltaTime = 1f / 60f;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            _camera = new GameObject("Probe-Camera");
            yield return null;

            Collider tread = null;
            CharacterPipelineRunner player = null;
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == stairName) tread = t.GetComponent<Collider>();
                    if (t.name == "X Bot" && t.GetComponent<CharacterPipelineRunner>() != null) player = t.GetComponent<CharacterPipelineRunner>();
                    if (t.name == "Y Bot") t.gameObject.SetActive(false);
                }
            Assert.IsNotNull(tread); Assert.IsNotNull(player);
            foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Bounds b = tread.bounds;
            Vector3 center = new Vector3(b.center.x, 0f, b.center.z);
            Vector3 ascent = Vector3.right; float bestRise = 0f;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.forward })
            {
                float half = Vector3.Dot(b.extents, axis) - 0.1f;
                float rise = TopY(center + axis * half) - TopY(center - axis * half);
                if (Mathf.Abs(rise) > Mathf.Abs(bestRise)) { bestRise = rise; ascent = rise > 0f ? axis : -axis; }
            }
            Vector3 side = Vector3.Cross(Vector3.up, ascent);
            float extent = Mathf.Abs(Vector3.Dot(b.extents, ascent));
            float S(Vector3 p) => Vector3.Dot(new Vector3(p.x - center.x, 0f, p.z - center.z), ascent);
            bool flat = stopIndex < 0;
            float stopS = flat ? -extent - 4.5f + 0.13f * (-1 - stopIndex)
                : dir == Dir.Down
                ? Mathf.Lerp(extent - 1.6f, -extent + 2.2f, stopIndex / 12f)
                : Mathf.Lerp(-extent + 1.0f, extent - 2.8f, stopIndex / 12f);

            Quaternion straight = Quaternion.LookRotation(ascent, Vector3.up);
            Quaternion diagonal = Quaternion.Euler(0f, yaw, 0f) * straight;
            _camera.transform.rotation = straight;
            player.RuntimeData.CameraTransform = _camera.transform;
            var cc = player.GetComponent<CharacterController>();
            float preShift = -Mathf.Tan(yaw * Mathf.Deg2Rad) * diagonalRun * 0.5f;
            Vector3 start = center - ascent * (extent + (flat ? 11f : 4f)) + side * (dir == Dir.Up ? preShift : -preShift);
            if (flat) Assert.IsTrue(Mathf.Abs(TopY(start)) < 0.02f && Mathf.Abs(TopY(center - ascent * (extent + 3f))) < 0.02f, "平地起點或停步區不是平地");
            cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(start.x, 0.05f, start.z), straight);
            cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return null;

            var footIK = player.GetComponentInChildren<FootIKController>();
            if (restartVariant == "forceRU") ForceRunStopVariant(player, "RU");
            if (restartVariant == "ep0" || restartVariant == "ep1")
                typeof(LocomotionModel).GetField("alignRunEntryPhase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(player.GetComponent<LocomotionModel>(), restartVariant == "ep1");
            if (restartVariant == "xab0" || restartVariant == "xab1")
            {
                var settings = (FootIKSettings)typeof(FootIKController).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(footIK);
                settings.PlantAwareExtrapolation = restartVariant == "xab1";
            }
            if (restartVariant == "lf_cur" || restartVariant == "lf_f3" || restartVariant == "lf_f5")
            {
                var settings = (FootIKSettings)typeof(FootIKController).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(footIK);
                settings.LockedFollowExperiment = restartVariant != "lf_cur";
                settings.LockedFollowDeadZone = restartVariant == "lf_f5" ? 0.05f : 0.03f;
                Assert.IsFalse(settings.PlantAwareExtrapolation, "docs/45 §24：R1c A/B 必須保持 P8b 關閉。");
            }
            var rootAtUpdate = player.gameObject.AddComponent<RootAtUpdate>();
            rootAtUpdate.Init(player.transform);
            var rec = footIK.gameObject.AddComponent<SettleRecorder>();
            rec.Init(player, footIK, rootAtUpdate, restartVariant == "zero");

            string phase = "approach";
            bool r2wArmed = false;
            if (stopStyle == "analogWalk")
            {
                // ⚠️ 假設情境（docs/48 §7.1）：出貨 X Bot 的 MoveAction 只綁鍵盤；只在測試場景實例加搖桿綁定。
                InputAction moveAction = player.GetComponent<PlayerInputSource>().MoveAction;
                moveAction.Disable(); moveAction.AddBinding("<Gamepad>/leftStick"); moveAction.Enable();
            }
            Key move = dir == Dir.Up ? Key.W : Key.S;
            if (gait == Gait.Walk) Press(_keyboard.leftCtrlKey);
            Press(_keyboard.wKey);
            int after = 0;
            bool fullRun = prefix.StartsWith("snap_") || prefix.StartsWith("lf_");
            for (int frame = 0; frame < 4000; frame++)
            {
                yield return null;
                float s = S(player.transform.position);
                if (dir == Dir.Up)
                {
                    if (phase == "approach") phase = "go";
                    else if (phase == "go" && s >= stopS - diagonalRun) { _camera.transform.rotation = diagonal; phase = "diag"; }
                    else if (phase == "diag" && s >= stopS) { Release(_keyboard.wKey); phase = "stop"; }
                }
                else
                {
                    if (phase == "approach" && s >= extent - 1.2f) { Release(_keyboard.wKey); phase = "turn"; }
                    else if (phase == "turn") { if (stopStyle == "analogWalk") Set(_pad.leftStick, new Vector2(0f, -AnalogHalfRaw)); else Press(_keyboard.sKey); phase = "go"; }
                    else if (phase == "go" && s <= stopS + diagonalRun) { _camera.transform.rotation = diagonal; phase = "diag"; }
                    else if (phase == "diag" && s <= stopS)
                    {
                        if (stopStyle == "runToWalk" && !r2wArmed) { Press(_keyboard.leftCtrlKey); r2wArmed = true; }   // 本帧切 Walk（Gait＝Walk），下一帧才放開
                        // ⚠️ 同一帧只送一個鍵盤釋放：連續兩次 Set 同一個 device，後一個事件會用舊狀態蓋掉前一個（S 會一直按著）。Ctrl 在結尾才放開。
                        else { if (stopStyle == "analogWalk") Set(_pad.leftStick, Vector2.zero); else Release(_keyboard.sKey); phase = "stop"; }
                    }
                }
                if (phase == "stop" && ++after > SettleFrames)
                {
                    Press(move == Key.W ? _keyboard.wKey : _keyboard.sKey);
                    phase = "restart"; after = 0;
                    if (restartVariant != null && (restartVariant.StartsWith("phi") || restartVariant.StartsWith("p") || restartVariant == "fast")) ApplyRestartIntervention(player, restartVariant);
                }
                else if (phase == "restart" && ++after > RestartFrames) break;
                rec.Phase = phase;
                rec.Recording = (fullRun && frame >= 90) || phase == "stop" || phase == "restart"
                                || (phase == "diag" && Mathf.Abs(s - stopS) <= 1.2f);
            }
            if (gait == Gait.Walk) Release(_keyboard.leftCtrlKey);
            Release(_keyboard.sKey); Release(_keyboard.wKey); Release(_keyboard.leftCtrlKey);
            Set(_pad.leftStick, Vector2.zero);

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/PlantSettle"));
            Directory.CreateDirectory(outDir);
            string label = prefix + (stopIndex < 0 ? "flat" : stairName.StartsWith("Stairs_") ? "proto" : "s1") + "_" + gait + "_" + dir
                           + "_y" + yaw.ToString("0", CultureInfo.InvariantCulture) + "_" + (stopIndex < 0 ? "f" + (-1 - stopIndex) : stopIndex.ToString("00"));
            File.WriteAllText(Path.Combine(outDir, label + ".csv"), rec.Csv.ToString());
        }

        /// <summary>A1：測試內把場景實例的 Run 停步變體換成只剩名稱含 tag 的那一支（SelectByEntryPhase 找不到腳相相符者時回傳第一個有效者）。</summary>
        private static void ForceRunStopVariant(CharacterPipelineRunner player, string tag)
        {
            var model = player.GetComponent<LocomotionModel>();
            FieldInfo fi = typeof(LocomotionModel).GetField("runStopVariants", BindingFlags.Instance | BindingFlags.NonPublic);
            var all = (LocomotionStopVariant[])fi.GetValue(model);
            var keep = System.Array.FindAll(all, v => v.BakeData != null && v.BakeData.name.Contains(tag));
            Assert.AreEqual(1, keep.Length, "找不到唯一的 Run 停步變體 " + tag);
            fi.SetValue(model, keep);
        }

        internal static object LocomotionMixer(CharacterPipelineRunner player)
        {
            const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
            var facade = player.GetComponent<AnimationFacadeBase>();
            string key = (string)typeof(LocomotionModel).GetField("locomotionAnimationKey", np).GetValue(player.GetComponent<LocomotionModel>());
            var cache = (System.Collections.IDictionary)facade.GetType().GetField("_stateCache", np).GetValue(facade);
            return cache.Contains(key) ? cache[key] : null;
        }

        /// <summary>測試內介入（反射）；不修改任何 runtime 程式。</summary>
        private static void ApplyRestartIntervention(CharacterPipelineRunner player, string variant)
        {
            object mixer = LocomotionMixer(player);
            Assert.IsNotNull(mixer, "找不到 locomotion mixer state");
            if (variant.StartsWith("phi") || variant.StartsWith("p"))
            {
                float phi = variant.StartsWith("phi") ? int.Parse(variant.Substring(3)) / 8f : int.Parse(variant.Substring(1)) / 100f;
                foreach (object child in Children(mixer))
                {
                    string n = ClipName(child);
                    if (n.Contains("Walk") || n.Contains("Run")) child.GetType().GetProperty("NormalizedTime").SetValue(child, phi);
                }
            }
            else if (variant == "fast")
            {
                var facade = player.GetComponent<AnimationFacadeBase>();
                object animancer = facade.GetType().GetField("animancer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(facade);
                object layers = animancer.GetType().GetProperty("Layers").GetValue(animancer);
                object layer = layers.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(layers, new object[] { 0 });
                var others = new System.Collections.Generic.List<object>();
                foreach (object st in Children(layer))
                    if (!ReferenceEquals(st, mixer) && (float)st.GetType().GetProperty("Weight").GetValue(st) > 0f) others.Add(st);
                foreach (object st in others) st.GetType().GetMethod("Stop", System.Type.EmptyTypes).Invoke(st, null);
                mixer.GetType().GetProperty("Weight").SetValue(mixer, 1f);
                var floor = player.gameObject.AddComponent<SpeedParamFloor>();
                floor.Init(facade, WalkThreshold(mixer), 30);
            }
        }

        internal static System.Collections.Generic.IEnumerable<object> Children(object parent)
        {
            int n = (int)parent.GetType().GetProperty("ChildCount").GetValue(parent);
            MethodInfo get = parent.GetType().GetMethod("GetChild", new[] { typeof(int) });
            for (int i = 0; i < n; i++) { object c = get.Invoke(parent, new object[] { i }); if (c != null) yield return c; }
        }

        internal static string ClipName(object state)
        {
            var clip = state.GetType().GetProperty("Clip")?.GetValue(state) as AnimationClip;
            return clip != null ? clip.name : "";
        }

        private static float WalkThreshold(object mixer)
        {
            MethodInfo getT = mixer.GetType().GetMethod("GetThreshold", new[] { typeof(int) });
            int i = 0;
            foreach (object child in Children(mixer))
            {
                if (ClipName(child).Contains("Walk") && getT != null) return System.Convert.ToSingle(getT.Invoke(mixer, new object[] { i }));
                i++;
            }
            return 1f;
        }

        /// <summary>fast 變體：runner 的 Update 之後、動畫評估之前，把動畫參數 MoveSpeed 墊到至少 floor（只動動畫參數，root 不受影響）。</summary>
        [DefaultExecutionOrder(31000)]
        private sealed class SpeedParamFloor : MonoBehaviour
        {
            private AnimationFacadeBase _facade; private float _floor; private int _frames;
            public void Init(AnimationFacadeBase facade, float floor, int frames) { _facade = facade; _floor = floor; _frames = frames; }
            private void Update()
            {
                if (_frames-- <= 0) { Destroy(this); return; }
                _facade.SetFloat(AnimationFacadeBase.ParamMoveSpeed, Mathf.Max(_floor, CurrentSpeed()));
            }
            private float CurrentSpeed()
            {
                object animancer = _facade.GetType().GetField("animancer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_facade);
                object parameters = animancer.GetType().GetProperty("Parameters").GetValue(animancer);
                foreach (MethodInfo m in parameters.GetType().GetMethods())
                {
                    if (m.Name != "GetValue" || !m.IsGenericMethodDefinition || m.GetParameters().Length != 1) continue;
                    try { return (float)m.MakeGenericMethod(typeof(float)).Invoke(parameters, new object[] { AnimationFacadeBase.ParamMoveSpeed }); }
                    catch { }
                }
                return 0f;
            }
        }

        private static float TopY(Vector3 xz) =>
            Physics.Raycast(new Vector3(xz.x, 50f, xz.z), Vector3.down, out RaycastHit h, 100f) ? h.point.y : float.NaN;

        /// <summary>動畫評估前的 player root 實測值；Frame 防止 recorder 誤讀上一幀。</summary>
        [DefaultExecutionOrder(-32000)]
        private sealed class RootAtUpdate : MonoBehaviour
        {
            private Transform _root;
            public Vector3 Position { get; private set; }
            public int Frame { get; private set; } = int.MinValue;

            public void Init(Transform root) => _root = root;

            private void Update()
            {
                if (_root == null) return;
                Position = _root.position;
                Frame = Time.frameCount;
            }
        }

        [DefaultExecutionOrder(32000)]
        private sealed class SettleRecorder : MonoBehaviour
        {
            public string Phase = "";
            public bool Recording;
            public readonly StringBuilder Csv = new StringBuilder();

            private CharacterPipelineRunner _player;
            private FootIKController _ik;
            private FootIKSettings _settings;
            private FieldInfo _poseF, _lF, _rF, _movingF;
            private LocomotionModel _model;
            private FieldInfo _stopF;
            private AnimationFacadeBase _facade;
            private PropertyInfo _keyP, _clipP;
            private object _animancer;
            private Transform _lBone, _rBone, _lToe, _rToe, _hips;
            private RootAtUpdate _rootAtUpdate;
            private bool _zeroIk;
            private FieldInfo _walkVariantsF, _runVariantsF;
            private Vector3 _prevRoot, _prevPoseL, _prevPoseR, _prevBoneL, _prevBoneR;
            private bool _hasPrev;

            public void Init(CharacterPipelineRunner player, FootIKController ik, RootAtUpdate rootAtUpdate, bool zeroIk)
            {
                _player = player; _ik = ik; _rootAtUpdate = rootAtUpdate; _zeroIk = zeroIk;
                const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
                _settings = (FootIKSettings)typeof(FootIKController).GetField("settings", np).GetValue(ik);
                _poseF = typeof(FootIKController).GetField("_poseData", np);
                _lF = typeof(FootIKController).GetField("_leftContact", np);
                _rF = typeof(FootIKController).GetField("_rightContact", np);
                _movingF = typeof(FootIKController).GetField("_moving", np);
                _model = player.GetComponent<LocomotionModel>();
                _stopF = typeof(LocomotionModel).GetField("_stop", np);
                _walkVariantsF = typeof(LocomotionModel).GetField("walkStopVariants", np);
                _runVariantsF = typeof(LocomotionModel).GetField("runStopVariants", np);
                _facade = player.GetComponent<AnimationFacadeBase>();
                if (_facade != null)
                {
                    _keyP = _facade.GetType().GetProperty("DebugSelectedStateKey", np);
                    _clipP = _facade.GetType().GetProperty("DebugDominantClipName", np);
                    _animancer = _facade.GetType().GetField("animancer", np)?.GetValue(_facade);
                }
                Animator anim = player.GetComponentInChildren<Animator>();
                _lBone = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rBone = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                _lToe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
                _rToe = anim.GetBoneTransform(HumanBodyBones.RightToes);
                _hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                _prevRoot = player.transform.position;

                var sb = new StringBuilder("frame,phase,intent,gait,rootX,rootY,rootZ,rootVx,rootVz,rootSpeed,stopActive,stopPlaying,stopNT,"
                                           + "animKey,animClip,animNT,animSpeed,animWeight,layerActive,pelvisSolver,pelvisOut,hipsRel,"
                                           + "mixIdleW,mixWalkW,mixWalkNT,mixRunW,mixRunNT,intentNorm");
                foreach (string p in new[] { "l", "r" })
                    foreach (string c in ("ph,role,beta,lift,posW,poseX,poseY,poseZ,poseVxz,exX,exZ,tgtX,tgtY,tgtZ,lockX,lockZ,relBlend,lockSupY,lockSupPt," +
                                          "boneX,boneY,boneZ,boneVxz,heelY,ankY,toeY,liveSup,livePt,cfSup,cfPt,k0,k25,k50,k75,k100,nOk,nFinal,nX,nZ").Split(','))
                        sb.Append(',').Append(p).Append(c);
                sb.Append(",rootEvalX,rootEvalY,rootEvalZ,ikZeroed,ltoeBoneX,ltoeBoneY,ltoeBoneZ,rtoeBoneX,rtoeBoneY,rtoeBoneZ,stopBake"
                          + ",posePlaneY,lstance,lprevValid,rstance,rprevValid,hipsY,grounded"
                          + ",predNextOk,predNextX,predNextY,predNextZ,predEndOk,predEndLX,predEndLY,predEndLZ,predEndRX,predEndRY,predEndRZ"
                          + ",stopTier,gaitBB,stopUsesPred,stopDesc");
                Csv.Append(sb).Append('\n');
            }

            private void LateUpdate()
            {
                Vector3 root = _player.transform.position;
                var pose = (FootIKPoseData)_poseF.GetValue(_ik);
                Vector3 boneL = _lBone.position, boneR = _rBone.position;
                float dt = Mathf.Max(Time.deltaTime, 1e-6f);
                Vector3 vRoot = _hasPrev ? (root - _prevRoot) / dt : Vector3.zero;
                Vector3 prevPoseL = _hasPrev ? _prevPoseL : pose.LeftFootPosition, prevPoseR = _hasPrev ? _prevPoseR : pose.RightFootPosition;
                Vector3 prevBoneL = _hasPrev ? _prevBoneL : boneL, prevBoneR = _hasPrev ? _prevBoneR : boneR;
                _prevRoot = root; _prevPoseL = pose.LeftFootPosition; _prevPoseR = pose.RightFootPosition; _prevBoneL = boneL; _prevBoneR = boneR;
                _hasPrev = true;
                if (Recording)
                {
                    PlayerRuntimeData data = _player.RuntimeData;
                    object stop = _stopF.GetValue(_model);
                    bool stopActive = (bool)stop.GetType().GetProperty("IsActive").GetValue(stop);
                    bool stopPlaying = (bool)stop.GetType().GetProperty("IsPlaying").GetValue(stop);
                    float stopNT = (float)stop.GetType().GetProperty("NormalizedTime").GetValue(stop);
                    string key = _keyP != null ? (string)_keyP.GetValue(_facade) : "";
                    string clip = _clipP != null ? (string)_clipP.GetValue(_facade) : "";
                    AnimState(out float nt, out float speed, out float weight, out int active);
                    var moving = (MovingPelvisState)_movingF.GetValue(_ik);
                    var td = _ik.TargetData;
                    var leftContact = (FootContactState)_lF.GetValue(_ik);
                    var rightContact = (FootContactState)_rF.GetValue(_ik);
                    float posePlaneY = pose.PosePlaneY;
                    float leftStance = FootIKController.ComputeFootWeight(pose.LeftFootPosition.y - posePlaneY,
                        _settings.FootGroundedHeightMin, _settings.FootGroundedHeightMax);
                    float rightStance = FootIKController.ComputeFootWeight(pose.RightFootPosition.y - posePlaneY,
                        _settings.FootGroundedHeightMin, _settings.FootGroundedHeightMax);

                    var sb = new StringBuilder();
                    sb.Append(Time.frameCount).Append(',').Append(Phase).Append(',').Append(F(data.MovementIntent.DesiredSpeedNormalized)).Append(',').Append(data.Gait)
                      .Append(',').Append(V(root)).Append(',').Append(F(vRoot.x)).Append(',').Append(F(vRoot.z)).Append(',').Append(F(new Vector2(vRoot.x, vRoot.z).magnitude))
                      .Append(',').Append(stopActive ? 1 : 0).Append(',').Append(stopPlaying ? 1 : 0).Append(',').Append(F(stopNT))
                      .Append(',').Append(key).Append(',').Append(clip).Append(',').Append(F(nt)).Append(',').Append(F(speed)).Append(',').Append(F(weight)).Append(',').Append(active)
                      .Append(',').Append(F(moving.Offset)).Append(',').Append(F(td.PelvisOffsetY)).Append(',').Append(F(_hips.position.y - root.y));
                    MixerChildren(sb);
                    Foot(sb, leftContact, moving.DebugLeftRole, pose.LeftFootPosition, prevPoseL, pose.LeftFootRotation, pose.LeftFootBottomHeight,
                        td.LeftFootPosition, td.LeftFootPositionWeight, td.LeftFootRotation, td.LeftFootRotationWeight, boneL, prevBoneL, dt, true);
                    Foot(sb, rightContact, moving.DebugRightRole, pose.RightFootPosition, prevPoseR, pose.RightFootRotation, pose.RightFootBottomHeight,
                        td.RightFootPosition, td.RightFootPositionWeight, td.RightFootRotation, td.RightFootRotationWeight, boneR, prevBoneR, dt, false);

                    Vector3 rootEval = _rootAtUpdate != null && _rootAtUpdate.Frame == Time.frameCount
                        ? _rootAtUpdate.Position
                        : new Vector3(float.NaN, float.NaN, float.NaN);
                    sb.Append(',').Append(V(rootEval)).Append(',').Append(_zeroIk ? 1 : 0)
                      .Append(',').Append(V(_lToe != null ? _lToe.position : new Vector3(float.NaN, float.NaN, float.NaN)))
                      .Append(',').Append(V(_rToe != null ? _rToe.position : new Vector3(float.NaN, float.NaN, float.NaN)))
                      .Append(',').Append(StopBakeName(stop))
                      .Append(',').Append(F(posePlaneY))
                      .Append(',').Append(F(leftStance)).Append(',').Append(leftContact.PrevContactValid ? 1 : 0)
                      .Append(',').Append(F(rightStance)).Append(',').Append(rightContact.PrevContactValid ? 1 : 0)
                      .Append(',').Append(F(_hips.position.y))
                      .Append(',').Append(data.IsGrounded ? 1 : 0);
                    // docs/48 M3.1：預測端的 C-E 輸出（next plant 與結束站姿的落地 y）——重構前後逐帧比對用
                    LocomotionStopPrediction predM = data.LocomotionStopPrediction;
                    Vector3 pn = default, peL = default, peR = default;
                    bool pnOk = predM.HasNextPlant && _ik.TryPredictNextPlantGoal(in predM, out _, out _, out pn);
                    bool peOk = predM.Active && _ik.TryPredictStopLandingGoal(in predM, true, out peL) & _ik.TryPredictStopLandingGoal(in predM, false, out peR);
                    sb.Append(',').Append(pnOk ? 1 : 0).Append(',').Append(V(pnOk ? pn : new Vector3(float.NaN, float.NaN, float.NaN)))
                      .Append(',').Append(peOk ? 1 : 0).Append(',').Append(V(peOk ? peL : new Vector3(float.NaN, float.NaN, float.NaN)))
                      .Append(',').Append(V(peOk ? peR : new Vector3(float.NaN, float.NaN, float.NaN)));
                    // docs/48 M1.3：規則⑦ 的輸入（committed stop tier vs 黑板 Gait）與求解器自己的鎖存
                    var movingM = (MovingPelvisState)_movingF.GetValue(_ik);
                    sb.Append(',').Append(predM.StopTier).Append(',').Append(data.Gait)
                      .Append(',').Append(movingM.StopUsesPrediction ? 1 : 0).Append(',').Append(movingM.StopDescending ? 1 : 0);
                    Csv.Append(sb).Append('\n');
                }

                // T3（order 6.5）已寫完下一幀目標；本 recorder 最後才清零，讓本列仍反映完整 T3 計算。
                if (_zeroIk) ZeroIkOutput();
            }

            private void ZeroIkOutput()
            {
                FootIKTargetData td = _ik.TargetData;
                td.LeftFootPositionWeight = 0f;
                td.LeftFootRotationWeight = 0f;
                td.RightFootPositionWeight = 0f;
                td.RightFootRotationWeight = 0f;
                td.PelvisOffsetY = 0f;
            }

            private string StopBakeName(object stop)
            {
                if (!(bool)stop.GetType().GetProperty("IsActive").GetValue(stop)) return "";
                int index = (int)stop.GetType().GetProperty("VariantIndex").GetValue(stop);
                var tier = (LocomotionStopTier)stop.GetType().GetProperty("Tier").GetValue(stop);
                FieldInfo variantsField = tier == LocomotionStopTier.Walk ? _walkVariantsF
                    : tier == LocomotionStopTier.Run ? _runVariantsF : null;
                var variants = variantsField?.GetValue(_model) as LocomotionStopVariant[];
                return variants != null && index >= 0 && index < variants.Length && variants[index].BakeData != null
                    ? variants[index].BakeData.name
                    : "";
            }

            private void Foot(StringBuilder sb, FootContactState c, int role, Vector3 posePos, Vector3 prevPose, Quaternion poseRot, float bottom,
                Vector3 tgt, float posW, Quaternion tgtRot, float rotW, Vector3 bone, Vector3 prevBone, float dt, bool left)
            {
                // T3 的一帧外插（PredictPose）：pose ＋（pose − 上一帧 pose）；瞬移判定與 T3 相同
                Vector3 step = posePos - prevPose;
                Vector3 ex = step.magnitude <= _settings.MaxPoseStepPerFrame ? posePos + step : posePos;
                float poseV = new Vector2(step.x, step.z).magnitude / dt;
                float boneV = new Vector2(bone.x - prevBone.x, bone.z - prevBone.z).magnitude / dt;
                // 三點各自命中的踏面：T3 的 pre-lift 踝（目標 − lift）＋ 即將顯示的旋轉
                Quaternion shown = Quaternion.Slerp(poseRot, tgtRot, rotW);
                Vector3 ankle = new Vector3(tgt.x, tgt.y - c.DebugLift, tgt.z);
                float[] offs = { -Mathf.Max(0f, _settings.HeelOffset), 0f, Mathf.Max(0f, _settings.ToeOffset) };
                float[] hy = new float[3];
                float best = float.NegativeInfinity; int bestPt = -1; float liveSup = float.NaN;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pt = FootContactSolver.SolePoint(ankle, shown, bottom, offs[i]);
                    bool hit = Physics.Raycast(new Vector3(pt.x, Mathf.Max(pt.y, ankle.y) + _settings.RaycastUpOffset, pt.z), Vector3.down, out RaycastHit h,
                        _settings.RaycastUpOffset + _settings.RaycastDistance, _settings.GroundLayers, QueryTriggerInteraction.Ignore);
                    hy[i] = hit ? h.point.y : float.NaN;
                    if (hit && h.point.y - pt.y > best) { best = h.point.y - pt.y; bestPt = i; liveSup = h.point.y; }
                }
                // offline counterfactual（in-engine，同一條三點最大穿透規則）：同一高度與旋轉，但 XZ 用「未外插的動畫踝」
                Vector3 cfAnkle = new Vector3(posePos.x, ankle.y, posePos.z);
                float cfBest = float.NegativeInfinity; int cfPt = -1; float cfSup = float.NaN;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pt = FootContactSolver.SolePoint(cfAnkle, shown, bottom, offs[i]);
                    if (Physics.Raycast(new Vector3(pt.x, Mathf.Max(pt.y, cfAnkle.y) + _settings.RaycastUpOffset, pt.z), Vector3.down, out RaycastHit h,
                            _settings.RaycastUpOffset + _settings.RaycastDistance, _settings.GroundLayers, QueryTriggerInteraction.Ignore)
                        && h.point.y - pt.y > cfBest) { cfBest = h.point.y - pt.y; cfPt = i; cfSup = h.point.y; }
                }
                sb.Append(',').Append((int)c.Phase).Append(',').Append(role).Append(',').Append(F(c.DebugBeta)).Append(',').Append(F(c.DebugLift)).Append(',').Append(F(posW))
                  .Append(',').Append(V(posePos)).Append(',').Append(F(poseV)).Append(',').Append(F(ex.x)).Append(',').Append(F(ex.z))
                  .Append(',').Append(V(tgt))
                  .Append(',').Append(F(c.Phase == FootLockPhase.Unlocked ? float.NaN : c.LockPosition.x)).Append(',').Append(F(c.Phase == FootLockPhase.Unlocked ? float.NaN : c.LockPosition.z))
                  .Append(',').Append(F(c.ReleaseBlend))
                  .Append(',').Append(F(c.Phase == FootLockPhase.Locked ? c.LockedSupportY : float.NaN)).Append(',').Append(c.Phase == FootLockPhase.Locked ? c.LockedSupportPoint : -1)
                  .Append(',').Append(V(bone)).Append(',').Append(F(boneV))
                  .Append(',').Append(F(hy[0])).Append(',').Append(F(hy[1])).Append(',').Append(F(hy[2]))
                  .Append(',').Append(F(liveSup)).Append(',').Append(bestPt)
                  .Append(',').Append(F(cfSup)).Append(',').Append(cfPt);
                // A2：外插比例 k 的反事實支撐——XZ ＝ 動畫踝 ＋ k ×（T3 實際外插步＝目標 − 動畫踝；Unlocked／鎖定當帧時目標＝外插點），同一高度與旋轉、同一條三點最大穿透規則
                for (int ki = 0; ki <= 4; ki++)
                {
                    float k = ki * 0.25f;
                    Vector3 kAnkle = new Vector3(posePos.x + k * (tgt.x - posePos.x), ankle.y, posePos.z + k * (tgt.z - posePos.z));
                    float kBest = float.NegativeInfinity; float kSup = float.NaN;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 pt = FootContactSolver.SolePoint(kAnkle, shown, bottom, offs[i]);
                        if (Physics.Raycast(new Vector3(pt.x, Mathf.Max(pt.y, kAnkle.y) + _settings.RaycastUpOffset, pt.z), Vector3.down, out RaycastHit h,
                                _settings.RaycastUpOffset + _settings.RaycastDistance, _settings.GroundLayers, QueryTriggerInteraction.Ignore)
                            && h.point.y - pt.y > kBest) { kBest = h.point.y - pt.y; kSup = h.point.y; }
                    }
                    sb.Append(',').Append(F(kSup));
                }
                // A2：ADR-010 D5 next planned plant（就是這隻腳時）——「plant 信心」夾限策略的輸入
                LocomotionStopPrediction pred = _player.RuntimeData.LocomotionStopPrediction;
                bool nOk = false, nFinal = false; Vector3 n = default;
                if (pred.HasNextPlant && _ik.TryPredictNextPlantGoal(in pred, out bool nLeft, out nFinal, out n)) nOk = nLeft == left;
                sb.Append(',').Append(nOk ? 1 : 0).Append(',').Append(nOk && nFinal ? 1 : 0).Append(',').Append(F(nOk ? n.x : float.NaN)).Append(',').Append(F(nOk ? n.z : float.NaN));
            }

            private void MixerChildren(StringBuilder sb)
            {
                float idleW = float.NaN, walkW = float.NaN, walkNT = float.NaN, runW = float.NaN, runNT = float.NaN;
                object mixer = LocomotionMixer(_player);
                if (mixer != null)
                {
                    float mw = (float)mixer.GetType().GetProperty("Weight").GetValue(mixer);
                    foreach (object c in Children(mixer))
                    {
                        string n = ClipName(c);
                        float w = (float)c.GetType().GetProperty("Weight").GetValue(c) * mw;
                        float t = (float)c.GetType().GetProperty("NormalizedTime").GetValue(c);
                        if (n.Contains("Idle")) idleW = w;
                        else if (n.Contains("Walk")) { walkW = w; walkNT = t; }
                        else if (n.Contains("Run")) { runW = w; runNT = t; }
                    }
                }
                sb.Append(',').Append(F(idleW)).Append(',').Append(F(walkW)).Append(',').Append(F(walkNT)).Append(',').Append(F(runW)).Append(',').Append(F(runNT))
                  .Append(',').Append(F(_player.RuntimeData.MovementIntent.DesiredSpeedNormalized));
            }

            /// <summary>Animancer 第 0 層目前狀態（反射；測試組件不引用 Animancer）。</summary>
            private void AnimState(out float nt, out float speed, out float weight, out int active)
            {
                nt = speed = weight = float.NaN; active = -1;
                if (_animancer == null) return;
                object layers = _animancer.GetType().GetProperty("Layers").GetValue(_animancer);
                object layer = layers.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(layers, new object[] { 0 });
                object state = layer.GetType().GetProperty("CurrentState").GetValue(layer);
                if (state != null)
                {
                    nt = (float)state.GetType().GetProperty("NormalizedTime").GetValue(state);
                    speed = (float)state.GetType().GetProperty("Speed").GetValue(state);
                    weight = (float)state.GetType().GetProperty("Weight").GetValue(state);
                }
                PropertyInfo countP = layer.GetType().GetProperty("ChildCount");
                MethodInfo getChild = layer.GetType().GetMethod("GetChild", new[] { typeof(int) });
                if (countP == null || getChild == null) return;
                int n = (int)countP.GetValue(layer); active = 0;
                for (int i = 0; i < n; i++)
                {
                    object child = getChild.Invoke(layer, new object[] { i });
                    if (child != null && (float)child.GetType().GetProperty("Weight").GetValue(child) > 0.01f) active++;
                }
            }

            private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
            private static string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
