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
        private Keyboard _keyboard;
        private Scene _scene;
        private GameObject _camera;

        public enum Dir { Down, Up }

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
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
            float stopS = dir == Dir.Down
                ? Mathf.Lerp(extent - 1.6f, -extent + 2.2f, stopIndex / 12f)
                : Mathf.Lerp(-extent + 1.0f, extent - 2.8f, stopIndex / 12f);

            Quaternion straight = Quaternion.LookRotation(ascent, Vector3.up);
            Quaternion diagonal = Quaternion.Euler(0f, yaw, 0f) * straight;
            _camera.transform.rotation = straight;
            player.RuntimeData.CameraTransform = _camera.transform;
            var cc = player.GetComponent<CharacterController>();
            float preShift = -Mathf.Tan(yaw * Mathf.Deg2Rad) * diagonalRun * 0.5f;
            Vector3 start = center - ascent * (extent + 4f) + side * (dir == Dir.Up ? preShift : -preShift);
            cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(start.x, 0.05f, start.z), straight);
            cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return null;

            var footIK = player.GetComponentInChildren<FootIKController>();
            var rec = footIK.gameObject.AddComponent<SettleRecorder>();
            rec.Init(player, footIK);

            string phase = "approach";
            Key move = dir == Dir.Up ? Key.W : Key.S;
            Press(_keyboard.wKey);
            int after = 0;
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
                    else if (phase == "turn") { Press(_keyboard.sKey); phase = "go"; }
                    else if (phase == "go" && s <= stopS + diagonalRun) { _camera.transform.rotation = diagonal; phase = "diag"; }
                    else if (phase == "diag" && s <= stopS) { Release(_keyboard.sKey); phase = "stop"; }
                }
                if (phase == "stop" && ++after > SettleFrames)
                {
                    Press(move == Key.W ? _keyboard.wKey : _keyboard.sKey);
                    phase = "restart"; after = 0;
                }
                else if (phase == "restart" && ++after > RestartFrames) break;
                rec.Phase = phase;
                rec.Recording = phase == "stop" || phase == "restart" || (phase == "diag" && Mathf.Abs(s - stopS) <= 1.2f);
            }
            Release(_keyboard.sKey); Release(_keyboard.wKey);

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/PlantSettle"));
            Directory.CreateDirectory(outDir);
            string label = "settle_" + (stairName.StartsWith("Stairs_") ? "proto" : "s1") + "_Run_" + dir
                           + "_y" + yaw.ToString("0", CultureInfo.InvariantCulture) + "_" + stopIndex.ToString("00");
            File.WriteAllText(Path.Combine(outDir, label + ".csv"), rec.Csv.ToString());
        }

        private static float TopY(Vector3 xz) =>
            Physics.Raycast(new Vector3(xz.x, 50f, xz.z), Vector3.down, out RaycastHit h, 100f) ? h.point.y : float.NaN;

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
            private Transform _lBone, _rBone, _hips;
            private Vector3 _prevRoot, _prevPoseL, _prevPoseR, _prevBoneL, _prevBoneR;
            private bool _hasPrev;

            public void Init(CharacterPipelineRunner player, FootIKController ik)
            {
                _player = player; _ik = ik;
                const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
                _settings = (FootIKSettings)typeof(FootIKController).GetField("settings", np).GetValue(ik);
                _poseF = typeof(FootIKController).GetField("_poseData", np);
                _lF = typeof(FootIKController).GetField("_leftContact", np);
                _rF = typeof(FootIKController).GetField("_rightContact", np);
                _movingF = typeof(FootIKController).GetField("_moving", np);
                _model = player.GetComponent<LocomotionModel>();
                _stopF = typeof(LocomotionModel).GetField("_stop", np);
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
                _hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                _prevRoot = player.transform.position;

                var sb = new StringBuilder("frame,phase,intent,gait,rootX,rootY,rootZ,rootVx,rootVz,rootSpeed,stopActive,stopPlaying,stopNT,"
                                           + "animKey,animClip,animNT,animSpeed,animWeight,layerActive,pelvisSolver,pelvisOut,hipsRel");
                foreach (string p in new[] { "l", "r" })
                    foreach (string c in ("ph,role,beta,lift,posW,poseX,poseY,poseZ,poseVxz,exX,exZ,tgtX,tgtY,tgtZ,lockX,lockZ,relBlend,lockSupY,lockSupPt," +
                                          "boneX,boneY,boneZ,boneVxz,heelY,ankY,toeY,liveSup,livePt,cfSup,cfPt").Split(','))
                        sb.Append(',').Append(p).Append(c);
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
                if (!Recording) return;

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

                var sb = new StringBuilder();
                sb.Append(Time.frameCount).Append(',').Append(Phase).Append(',').Append(F(data.MovementIntent.DesiredSpeedNormalized)).Append(',').Append(data.Gait)
                  .Append(',').Append(V(root)).Append(',').Append(F(vRoot.x)).Append(',').Append(F(vRoot.z)).Append(',').Append(F(new Vector2(vRoot.x, vRoot.z).magnitude))
                  .Append(',').Append(stopActive ? 1 : 0).Append(',').Append(stopPlaying ? 1 : 0).Append(',').Append(F(stopNT))
                  .Append(',').Append(key).Append(',').Append(clip).Append(',').Append(F(nt)).Append(',').Append(F(speed)).Append(',').Append(F(weight)).Append(',').Append(active)
                  .Append(',').Append(F(moving.Offset)).Append(',').Append(F(td.PelvisOffsetY)).Append(',').Append(F(_hips.position.y - root.y));
                Foot(sb, (FootContactState)_lF.GetValue(_ik), moving.DebugLeftRole, pose.LeftFootPosition, prevPoseL, pose.LeftFootRotation, pose.LeftFootBottomHeight,
                    td.LeftFootPosition, td.LeftFootPositionWeight, td.LeftFootRotation, td.LeftFootRotationWeight, boneL, prevBoneL, dt);
                Foot(sb, (FootContactState)_rF.GetValue(_ik), moving.DebugRightRole, pose.RightFootPosition, prevPoseR, pose.RightFootRotation, pose.RightFootBottomHeight,
                    td.RightFootPosition, td.RightFootPositionWeight, td.RightFootRotation, td.RightFootRotationWeight, boneR, prevBoneR, dt);
                Csv.Append(sb).Append('\n');
            }

            private void Foot(StringBuilder sb, FootContactState c, int role, Vector3 posePos, Vector3 prevPose, Quaternion poseRot, float bottom,
                Vector3 tgt, float posW, Quaternion tgtRot, float rotW, Vector3 bone, Vector3 prevBone, float dt)
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
