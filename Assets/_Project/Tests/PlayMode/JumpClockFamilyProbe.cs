using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Project.Core.Blackboard;
using Project.Core.Movement;
using Project.Core.Pipeline;
using Project.Core.StateMachine;
using Project.Presentation.Animation;
using Project.Presentation.IK;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🔬 THROWAWAY（2026-10-08，docs/48 §6.4 F2-J，measurement-only，⛔ 不改 runtime）：
    /// Jump 以原始意圖選 Walk／Run loop Bake 解讀腳相，但 locomotion 時鐘取自實際速度驅動的 mixer 主導 child。
    /// 每次起跳記錄：主導 loop／被取樣的時鐘屬於哪個 child／Jump 選的 Bake 家族／Jump 讀到的腳相（＝最終 LU／RU 依據）／
    /// 真值（被取樣 child 自己的 Bake 腳相、動畫 pose 實際較低的腳）／起跳後 10 帧的可見性代理（支撐腳滑動、較低腳翻轉）。
    /// 輸出 <c>Temp/JumpClock/</c>（Unity 重啟會清空）。
    /// </summary>
    [Category("Probe")]
    public sealed class JumpClockFamilyProbe : InputTestFixture
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const int SteadyWarmupFrames = 45;
        private const int AfterTakeoffFrames = 40;
        /// <summary>搖桿原始量：預設 stick deadzone（0.125–0.925）後 ≈ 0.8 ⇒ 強度 0.75 × 0.8 ≈ 0.6。實際值記在輸出。</summary>
        private const float AnalogRaw = 0.765f;

        // Sprint 不列：出貨 X Bot 的 SprintAction 沒有綁定（Shift 是 Roll）⇒ 玩家到不了 Sprint（2026-10-08 實測）。
        public enum Case { Walk, Run, Analog06, RunAccel, CombatSide, CombatBack, TurnSide, TurnBack, WalkToggleOn, WalkToggleOff }

        private Keyboard _kb;
        private Gamepad _pad;
        private Scene _scene;
        private GameObject _camera;

        public override void Setup()
        {
            base.Setup();
            _kb = InputSystem.AddDevice<Keyboard>();
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
        public IEnumerator Probe_JumpClockFamily(
            [Values] Case c,
            [Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)] int offset)
        {
            Time.captureDeltaTime = 1f / 60f;
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(_scene);
            _camera = new GameObject("Probe-Camera");
            yield return null;

            CharacterPipelineRunner player = null;
            GameObject enemy = null;
            foreach (GameObject root in _scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "X Bot" && t.GetComponent<CharacterPipelineRunner>() != null) player = t.GetComponent<CharacterPipelineRunner>();
                    if (t.name == "Y Bot" && t.GetComponent<CharacterPipelineRunner>() != null) enemy = t.gameObject;
                }
            Assert.IsNotNull(player, "X Bot"); Assert.IsNotNull(enemy, "Y Bot");
            foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 空曠平地（2 km 地面），避開場景物件。
            Vector3 start = new Vector3(-40f, 0f, -40f);
            Vector3 fwd = Vector3.forward, right = Vector3.right;
            foreach (Vector3 d in new[] { fwd, -fwd, right, -right })
                for (float s = 0f; s <= 25f; s += 0.5f)
                    Assert.Less(Mathf.Abs(TopY(start + d * s)), 0.02f, $"探針路徑不是平地：{start + d * s}");

            bool combat = c == Case.CombatSide || c == Case.CombatBack;
            if (!combat) enemy.SetActive(false);
            else
            {
                // 只當不動的目標：停掉它的管線與導航，不改資產。
                enemy.GetComponent<CharacterPipelineRunner>().enabled = false;
                var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) agent.enabled = false;
                var ecc = enemy.GetComponent<CharacterController>();
                if (ecc != null) ecc.enabled = false;
                enemy.transform.SetPositionAndRotation(start + fwd * 5.5f, Quaternion.LookRotation(-fwd));
                if (ecc != null) ecc.enabled = true;
            }

            _camera.transform.SetPositionAndRotation(start + Vector3.up * 1.6f - fwd * 3f, Quaternion.LookRotation(fwd));
            player.RuntimeData.CameraTransform = _camera.transform;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(start + Vector3.up * 0.05f, Quaternion.LookRotation(fwd));
            cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return null;

            bool combatReady = !combat;
            if (combat)
                for (int i = 0; i < 60 && !combatReady; i++)
                {
                    yield return null;
                    combatReady = player.RuntimeData.CombatContext.InCombat && player.RuntimeData.CombatContext.HasTarget;
                }

            var rec = player.gameObject.AddComponent<JumpClockRecorder>();
            rec.Init(player);

            // 開始移動
            // 轉向類（Combat*／Turn*）：先往 +X 穩定跑 45 帧（X Bot 沒有 persistent combat facing ⇒ 面向跟著移動），
            // 再換鍵轉 90°（Side：−Z）或 180°（Back：−X），在轉向中第 offset×2 帧起跳。
            // 戰鬥方向限速只在「面向 ≠ 移動方向」時生效 ⇒ 只可能出現在轉向暫態（smoke test 實測）。
            bool turn = combat || c == Case.TurnSide || c == Case.TurnBack;
            bool side = c == Case.CombatSide || c == Case.TurnSide;
            // 切換類（WalkToggle*）：Walk 是 toggle（Gait_ActionRPG），穩定 45 帧後按一次 Ctrl，在加／減速期第 offset 帧起跳。
            bool toggle = c == Case.WalkToggleOn || c == Case.WalkToggleOff;
            if (c == Case.Analog06)
            {
                // ⚠️ 假設情境：出貨的 X Bot `MoveAction` 只綁鍵盤 WASD（沒有搖桿）⇒ 玩家目前產生不了 0.6 的強度。
                //    這裡只在**測試載入的場景實例**上加一條搖桿綁定（不改 prefab），量「將來支援類比輸入時」會發生什麼。
                InputAction moveAction = player.GetComponent<PlayerInputSource>().MoveAction;
                moveAction.Disable();
                moveAction.AddBinding("<Gamepad>/leftStick");
                moveAction.Enable();
                yield return null;
            }
            if (c == Case.Walk || c == Case.WalkToggleOff) { Press(_kb.leftCtrlKey); yield return null; Release(_kb.leftCtrlKey); }
            if (c == Case.Analog06) Set(_pad.leftStick, new Vector2(0f, AnalogRaw));
            else if (turn) Press(_kb.dKey);
            else Press(_kb.wKey);

            int wait = c == Case.RunAccel ? offset : (turn || toggle) ? SteadyWarmupFrames : SteadyWarmupFrames + offset * 3;
            for (int i = 0; i < wait; i++) yield return null;
            if (turn)
            {
                // ⚠️ 釋放與按下分兩帧：同一帧對鍵盤送兩個事件不可靠（2026-10-08 實測：D 會卡住，側移變成 45° 斜走）。
                Release(_kb.dKey);
                yield return null;
                Press(side ? _kb.sKey : _kb.aKey);
                rec.MarkTurn();
                for (int i = 0; i < offset * 2; i++) yield return null;
            }
            if (toggle)
            {
                Press(_kb.leftCtrlKey);
                rec.MarkTurn();
                yield return null;
                Release(_kb.leftCtrlKey);
                for (int i = 1; i < offset; i++) yield return null;
            }
            Press(_kb.spaceKey);
            yield return null;
            Release(_kb.spaceKey);
            for (int i = 0; i < AfterTakeoffFrames && !rec.Done; i++) yield return null;

            Release(_kb.wKey); Release(_kb.sKey); Release(_kb.dKey); Release(_kb.aKey);
            Set(_pad.leftStick, Vector2.zero);

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/JumpClock"));
            Directory.CreateDirectory(outDir);
            string name = $"{c}_o{offset:00}";
            File.WriteAllText(Path.Combine(outDir, name + ".csv"), rec.Csv.ToString());
            File.WriteAllText(Path.Combine(outDir, "summary_" + name + ".csv"),
                JumpClockRecorder.SummaryHeader + "\n" + $"{c},{offset},{(combatReady ? 1 : 0)},{rec.SinceTurn}," + rec.Summary + "\n");
            Assert.IsTrue(rec.TookOff, $"{name}：沒有進入 JumpState");
        }

        private static float TopY(Vector3 xz) =>
            Physics.Raycast(new Vector3(xz.x, 50f, xz.z), Vector3.down, out RaycastHit h, 100f) ? h.point.y : float.NaN;

        private sealed class JumpClockRecorder : MonoBehaviour
        {
            internal const string SummaryHeader =
                "case,offset,combatReady,sinceTurn,intent,moveSpeed,inCombat,localAngle,idleW,walkW,runW,sprintW,dominant,sampledClock,sampledChild," +
                "jumpTier,hasPhase,phaseUsed,startClip,truthClockPhase,truthPosePhase,clockFamilyMismatch,wrongVsClock,wrongVsPose," +
                "supportFoot,supportSlideMax10,lowerFlips10";

            public readonly StringBuilder Csv = new StringBuilder(
                "frame,rel,state,clip,intent,moveSpeed,inCombat,localAngle,idleW,idleNT,walkW,walkNT,runW,runNT,sprintW,sprintNT,modelClock,clockValid," +
                "poseLY,poseRY,boneLX,boneLY,boneLZ,boneRX,boneRY,boneRZ,rootX,rootZ\n");
            public string Summary = "";
            public bool TookOff, Done;
            public int SinceTurn = -1;
            private int _turnFrame = -1;
            public void MarkTurn() => _turnFrame = Time.frameCount;

            private CharacterPipelineRunner _runner;
            private FootIKController _ik;
            private object _fsm;
            private PropertyInfo _currentState;
            private FieldInfo _clockF, _hasClockF, _poseF;
            private MotionBakeData _walk, _run, _sprint;
            private Transform _lBone, _rBone;
            private object _animancer;

            private struct Snap
            {
                public bool Valid;
                public float IdleW, IdleNT, WalkW, WalkNT, RunW, RunNT, SprintW, SprintNT;
                public string Dominant;
                public float PoseLY, PoseRY, PosePlaneY;
                public Vector3 BoneL, BoneR;
            }
            private Snap _prev;
            private bool _wasJump;
            private int _takeoffFrame = -1;
            private int _supportFoot; // 0 左、1 右
            private float _supportSlideMax;
            private int _lowerFlips;
            private int _lastLower = -1;
            private Vector3 _prevSupportBone;

            public void Init(CharacterPipelineRunner runner)
            {
                const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
                _runner = runner;
                _ik = runner.GetComponentInChildren<FootIKController>();
                _fsm = typeof(CharacterPipelineRunner).GetField("_stateMachine", np).GetValue(runner);
                _currentState = _fsm.GetType().GetProperty("CurrentState");
                var model = runner.GetComponent<LocomotionModel>();
                _clockF = typeof(LocomotionModel).GetField("_locomotionNormalizedTime", np);
                _hasClockF = typeof(LocomotionModel).GetField("_hasLocomotionTimeSnapshot", np);
                _poseF = typeof(FootIKController).GetField("_poseData", np);
                _walk = (MotionBakeData)typeof(LocomotionModel).GetField("walkLoopBakeData", np).GetValue(model);
                _run = (MotionBakeData)typeof(LocomotionModel).GetField("runLoopBakeData", np).GetValue(model);
                _sprint = (MotionBakeData)typeof(MotionDriver).GetField("moveSpeedSource", np).GetValue(runner.GetComponent<MotionDriver>());
                Animator anim = runner.GetComponentInChildren<Animator>();
                _lBone = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rBone = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                var facade = runner.GetComponent<AnimationFacadeBase>();
                _animancer = facade.GetType().GetField("animancer", np)?.GetValue(facade);
            }

            private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

            private Snap Take()
            {
                var s = new Snap { Valid = true, IdleW = float.NaN, IdleNT = float.NaN, WalkW = float.NaN, WalkNT = float.NaN,
                    RunW = float.NaN, RunNT = float.NaN, SprintW = float.NaN, SprintNT = float.NaN, Dominant = "none" };
                object mixer = PlantSettleProbe.LocomotionMixer(_runner);
                if (mixer != null)
                {
                    float best = 0f;
                    foreach (object child in PlantSettleProbe.Children(mixer))
                    {
                        string n = PlantSettleProbe.ClipName(child);
                        // 主導判定與 AnimancerFacade.TryGetDominantChildNormalizedTime 相同：只看 child 權重，同值取較前者。
                        float w = (float)child.GetType().GetProperty("Weight").GetValue(child);
                        float t = (float)child.GetType().GetProperty("NormalizedTime").GetValue(child);
                        string fam = Family(n);
                        if (fam == "Idle") { s.IdleW = w; s.IdleNT = t; }
                        else if (fam == "Walk") { s.WalkW = w; s.WalkNT = t; }
                        else if (fam == "Run") { s.RunW = w; s.RunNT = t; }
                        else if (fam == "Sprint") { s.SprintW = w; s.SprintNT = t; }
                        if (w > best) { best = w; s.Dominant = fam; }
                    }
                }
                var pose = (FootIKPoseData)_poseF.GetValue(_ik);
                s.PoseLY = pose.LeftFootPosition.y; s.PoseRY = pose.RightFootPosition.y; s.PosePlaneY = pose.PosePlaneY;
                s.BoneL = _lBone.position; s.BoneR = _rBone.position;
                return s;
            }

            private static string Family(string clip) =>
                clip.Contains("Sprint") ? "Sprint" : clip.Contains("Walk") ? "Walk" : clip.Contains("Run") ? "Run" : clip.Contains("Idle") ? "Idle" : "?";

            private MotionBakeData BakeOf(string family) =>
                family == "Walk" ? _walk : family == "Run" ? _run : family == "Sprint" ? _sprint : null;

            private static string Phase(MotionBakeData bake, float nt) =>
                bake == null || bake.Duration <= 0f ? "none" : bake.GetFootPhaseAt(Mathf.Repeat(nt, 1f) * bake.Duration).ToString();

            private string CurrentClip()
            {
                if (_animancer == null) return "?";
                object layers = _animancer.GetType().GetProperty("Layers").GetValue(_animancer);
                object layer = layers.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(layers, new object[] { 0 });
                object state = layer.GetType().GetProperty("CurrentState").GetValue(layer);
                return state == null ? "none" : PlantSettleProbe.ClipName(state);
            }

            private void LateUpdate()
            {
                if (_runner == null || Done) return;
                PlayerRuntimeData data = _runner.RuntimeData;
                object state = _currentState.GetValue(_fsm);
                bool inJump = state is JumpState;
                Snap cur = Take();
                float clock = (float)_clockF.GetValue(_runner.GetComponent<LocomotionModel>());
                bool clockValid = (bool)_hasClockF.GetValue(_runner.GetComponent<LocomotionModel>());
                Vector3 dir = data.MovementIntent.DesiredDirection;
                float localAngle = dir.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(transform.forward, dir, Vector3.up) : float.NaN;

                if (inJump && !_wasJump && !TookOff && _prev.Valid)
                {
                    TookOff = true;
                    _takeoffFrame = Time.frameCount;
                    if (_turnFrame >= 0) SinceTurn = _takeoffFrame - _turnFrame;
                    const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
                    var tier = (LocomotionStopTier)typeof(JumpState).GetField("_takeoffTier", np).GetValue(state);
                    bool hasPhase = (bool)typeof(JumpState).GetField("_hasTakeoffFootPhase", np).GetValue(state);
                    var phaseUsed = (FootPhase)typeof(JumpState).GetField("_takeoffFootPhase", np).GetValue(state);
                    // 被取樣的時鐘屬於哪個 child：上一帧 LateUpdate 的 child NT（＝本帧順序 3 讀到的值）與 model 快照相同者。
                    string sampled = "none";
                    if (clockValid)
                    {
                        if (Mathf.Abs(_prev.WalkNT - clock) < 1e-4f) sampled = "Walk";
                        else if (Mathf.Abs(_prev.RunNT - clock) < 1e-4f) sampled = "Run";
                        else if (Mathf.Abs(_prev.SprintNT - clock) < 1e-4f) sampled = "Sprint";
                        else if (Mathf.Abs(_prev.IdleNT - clock) < 1e-4f) sampled = "Idle";
                        else sampled = "?";
                    }
                    string jumpFam = tier == LocomotionStopTier.Walk ? "Walk" : tier == LocomotionStopTier.Run ? "Run" : "None";
                    string truthClock = Phase(BakeOf(sampled), clock);
                    // 動畫 pose（pre-IK）實際較低的腳——與烘焙 FootPhase 的定義相同（較低者觸地）。
                    string truthPose = _prev.PoseLY <= _prev.PoseRY ? FootPhase.LeftFootDown.ToString() : FootPhase.RightFootDown.ToString();
                    string used = hasPhase ? phaseUsed.ToString() : "none";
                    bool familyMismatch = jumpFam != "None" && sampled != jumpFam;
                    _supportFoot = _prev.PoseLY <= _prev.PoseRY ? 0 : 1;
                    _prevSupportBone = _supportFoot == 0 ? _prev.BoneL : _prev.BoneR;
                    Summary = string.Join(",", F(data.MovementIntent.DesiredSpeedNormalized), F(data.MoveSpeed), data.CombatContext.InCombat ? "1" : "0",
                        F(localAngle), F(_prev.IdleW), F(_prev.WalkW), F(_prev.RunW), F(_prev.SprintW), _prev.Dominant, F(clock), sampled,
                        jumpFam, hasPhase ? "1" : "0", used, CurrentClip(), truthClock, truthPose,
                        familyMismatch ? "1" : "0",
                        hasPhase && truthClock != "none" && used != truthClock ? "1" : "0",
                        hasPhase && used != truthPose ? "1" : "0",
                        _supportFoot == 0 ? "L" : "R");
                }

                if (TookOff)
                {
                    int rel = Time.frameCount - _takeoffFrame;
                    if (rel >= 1 && rel <= 10)
                    {
                        Vector3 bone = _supportFoot == 0 ? cur.BoneL : cur.BoneR;
                        float slide = new Vector2(bone.x - _prevSupportBone.x, bone.z - _prevSupportBone.z).magnitude / Mathf.Max(Time.deltaTime, 1e-6f);
                        _supportSlideMax = Mathf.Max(_supportSlideMax, slide);
                        _prevSupportBone = bone;
                        int lower = cur.BoneL.y <= cur.BoneR.y ? 0 : 1;
                        if (_lastLower >= 0 && lower != _lastLower) _lowerFlips++;
                        _lastLower = lower;
                    }
                    else if (rel == 0) _lastLower = cur.BoneL.y <= cur.BoneR.y ? 0 : 1;
                    if (rel == 10) Summary += "," + F(_supportSlideMax) + "," + _lowerFlips;
                    if (rel >= 30) Done = true;
                }

                Csv.Append(Time.frameCount).Append(',').Append(TookOff ? Time.frameCount - _takeoffFrame : -1).Append(',')
                   .Append(state != null ? state.GetType().Name : "null").Append(',').Append(CurrentClip()).Append(',')
                   .Append(F(data.MovementIntent.DesiredSpeedNormalized)).Append(',').Append(F(data.MoveSpeed)).Append(',')
                   .Append(data.CombatContext.InCombat ? 1 : 0).Append(',').Append(F(localAngle)).Append(',')
                   .Append(F(cur.IdleW)).Append(',').Append(F(cur.IdleNT)).Append(',').Append(F(cur.WalkW)).Append(',').Append(F(cur.WalkNT)).Append(',')
                   .Append(F(cur.RunW)).Append(',').Append(F(cur.RunNT)).Append(',').Append(F(cur.SprintW)).Append(',').Append(F(cur.SprintNT)).Append(',')
                   .Append(F(clock)).Append(',').Append(clockValid ? 1 : 0).Append(',')
                   .Append(F(cur.PoseLY - cur.PosePlaneY)).Append(',').Append(F(cur.PoseRY - cur.PosePlaneY)).Append(',')
                   .Append(F(cur.BoneL.x)).Append(',').Append(F(cur.BoneL.y)).Append(',').Append(F(cur.BoneL.z)).Append(',')
                   .Append(F(cur.BoneR.x)).Append(',').Append(F(cur.BoneR.y)).Append(',').Append(F(cur.BoneR.z)).Append(',')
                   .Append(F(transform.position.x)).Append(',').Append(F(transform.position.z)).Append('\n');

                _prev = cur;
                _wasJump = inJump;
            }
        }
    }
}
