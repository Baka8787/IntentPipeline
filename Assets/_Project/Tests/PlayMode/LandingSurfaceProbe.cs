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
using Project.Presentation.Motion;
using Project.Presentation.IK;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Project.Tests.PlayMode
{
    /// <summary>
    /// 🔬 研究探針（只量測，⛔ 不進 production path；全部方法 <c>[Explicit]</c>）——docs/45 §10–§18 的資料來源。
    /// 情境：兩道樓梯 × Walk／Run × 上／下 × 斜向 0°／20° × 13 個停步位置；放開前 ~1.2 m 起逐帧記錄（LateUpdate 最後執行，IK 解完之後）。
    /// 每帧兩腳：T3 相位／β／lift／目標與動畫踝／移動斜坡與踏面／FootIK 地面樣本／停步預測與 next plant（ADR-010）／
    /// 以反射呼叫 T3 自己的 <c>AccumulateContact</c> 重現的支撐（repSup）／resolver intent／骨盆求解器與最終 Hips 骨骼。
    /// 方法：<c>Probe_DiagonalStopLanding</c>（land_*，§10–§15）、<c>Probe_LandingResolverAB</c>（lsr0_/lsr1_，§17）、
    /// <c>Probe_FootLengthReferenceAB</c>（flr0_/flr1_，§18）。輸出 <c>Temp/LandingSurface/</c>（Unity 重啟會清空）。
    /// 分析腳本與重現方式：<c>docs/artifacts/pelvis-analysis/README.md</c>。
    /// </summary>
    [Category("Probe")]
    public sealed class LandingSurfaceProbe : InputTestFixture
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private Keyboard _keyboard;
        private Scene _scene;
        private GameObject _camera;

        public enum Gait { Walk, Run }
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
        public IEnumerator Probe_DiagonalStopLanding(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)] int stopIndex)
            => Drive(stairName, gait, dir, yaw, stopIndex, "land_", null);

        /// <summary>
        /// Phase 2（docs/45 §17）：同一組停步，LandingSurfaceResolver 關／開 A/B。輸出 lsr0_*／lsr1_*（＋ semlsr*）。
        /// 只改場景實例上的設定（Play 中載入的場景，不寫回資產）。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_LandingResolverAB(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)] int stopIndex,
            [Values(false, true)] bool resolver)
            => Drive(stairName, gait, dir, yaw, stopIndex, resolver ? "lsr1_" : "lsr0_", s => s.UseLandingSurfaceResolver = resolver);

        /// <summary>
        /// docs/45 §18：同一組停步，擺盪腳長參考關／開 A/B（resolver 維持 prefab 設定）。輸出 flr0_*／flr1_*（＋ semflr*）。
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Probe_FootLengthReferenceAB(
            [Values("Stairs_650_400_300_Prefab", "Stairs (1)")] string stairName,
            [Values(Gait.Walk, Gait.Run)] Gait gait,
            [Values(Dir.Down, Dir.Up)] Dir dir,
            [Values(0f, 20f)] float yaw,
            [Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)] int stopIndex,
            [Values(false, true)] bool footLength)
            => Drive(stairName, gait, dir, yaw, stopIndex, footLength ? "flr1_" : "flr0_", s => s.SwingFootLengthReference = footLength);

        private IEnumerator Drive(string stairName, Gait gait, Dir dir, float yaw, int stopIndex, string prefix,
            System.Action<FootIKSettings> configure)
        {
            const float diagonalRun = 2.5f;     // 放開前最後這段距離改成斜向
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
            float L(Vector3 p) => Vector3.Dot(new Vector3(p.x - center.x, 0f, p.z - center.z), side);
            float stopS = dir == Dir.Down
                ? Mathf.Lerp(extent - 1.6f, -extent + 1.0f, stopIndex / 12f)
                : Mathf.Lerp(-extent + 1.0f, extent - 1.6f, stopIndex / 12f);

            Quaternion straight = Quaternion.LookRotation(ascent, Vector3.up);
            Quaternion diagonal = Quaternion.Euler(0f, yaw, 0f) * straight;
            _camera.transform.rotation = straight;
            player.RuntimeData.CameraTransform = _camera.transform;
            var cc = player.GetComponent<CharacterController>();
            // 斜向段向右偏 ⇒ 起點先往左移，讓放開點落在樓梯寬度中央附近
            float preShift = -Mathf.Tan(yaw * Mathf.Deg2Rad) * diagonalRun * 0.5f;
            Vector3 start = center - ascent * (extent + 4f) + side * (dir == Dir.Up ? preShift : -preShift);
            cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(start.x, 0.05f, start.z), straight);
            cc.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++) yield return null;

            var footIK = player.GetComponentInChildren<FootIKController>();
            if (configure != null)
            {
                var settings = (FootIKSettings)typeof(FootIKController)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(footIK);
                configure(settings);
            }
            var rec = footIK.gameObject.AddComponent<LandingRecorder>();
            rec.Init(player, footIK, ascent, side, center);

            string phase = "approach";
            if (gait == Gait.Walk) Press(_keyboard.leftCtrlKey);
            Press(_keyboard.wKey);
            int after = 0;
            for (int frame = 0; frame < 3000; frame++)
            {
                yield return null;
                float s = S(player.transform.position);
                if (dir == Dir.Up)
                {
                    if (phase == "approach") phase = "go";
                    else if (phase == "go" && s >= stopS - diagonalRun) { _camera.transform.rotation = diagonal; phase = "diag"; }
                    else if (phase == "diag" && s >= stopS) { Release(_keyboard.wKey); phase = "stop"; }
                    else if (phase == "stop" && ++after > 150) break;
                }
                else
                {
                    if (phase == "approach" && s >= extent - 1.2f) { Release(_keyboard.wKey); phase = "turn"; }
                    else if (phase == "turn") { Press(_keyboard.sKey); phase = "go"; }
                    else if (phase == "go" && s <= stopS + diagonalRun) { _camera.transform.rotation = diagonal; phase = "diag"; }
                    else if (phase == "diag" && s <= stopS) { Release(_keyboard.sKey); phase = "stop"; }
                    else if (phase == "stop" && ++after > 150) break;
                }
                // 只保留放開前 ~1.2 m 與之後
                rec.Phase = phase;
                rec.Recording = phase == "stop" || (phase == "diag" && Mathf.Abs(s - stopS) <= 1.2f);
            }
            Release(_keyboard.leftCtrlKey); Release(_keyboard.sKey); Release(_keyboard.wKey);

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LandingSurface"));
            Directory.CreateDirectory(outDir);
            string label = prefix + (stairName.StartsWith("Stairs_") ? "proto" : "s1") + "_" + gait + "_" + dir
                           + "_y" + yaw.ToString("0", CultureInfo.InvariantCulture) + "_" + stopIndex.ToString("00");
            File.WriteAllText(Path.Combine(outDir, label + ".csv"), rec.Csv.ToString());
            File.WriteAllText(Path.Combine(outDir, (prefix == "land_" ? "sem_" : "sem" + prefix) + label.Substring(5) + ".csv"), rec.FinishSemantics());
        }

        private static float TopY(Vector3 xz) =>
            Physics.Raycast(new Vector3(xz.x, 50f, xz.z), Vector3.down, out RaycastHit h, 100f) ? h.point.y : float.NaN;

        /// <summary>IK 解完之後（LateUpdate 最後執行）逐帧記錄；只記錄，不回饋。</summary>
        [DefaultExecutionOrder(32000)]
        private sealed class LandingRecorder : MonoBehaviour
        {
            public string Phase = "";
            public bool Recording;
            public readonly StringBuilder Csv = new StringBuilder();

            private CharacterPipelineRunner _player;
            private FootIKController _ik;
            private FootIKSettings _settings;
            private Vector3 _ascent, _side, _center;
            private int _mask;
            private FieldInfo _poseF, _lF, _rF, _movingF;
            private Vector3 _prevRoot;
            // Phase 1 候選（只量測）：動畫踝的上一帧位置與腳底離 pose 平面的高度
            private Vector3 _prevPoseL, _prevPoseR;
            private float _prevHL, _prevHR;
            private bool _hasPrevPose;
            // Phase 1c：停步 clip 時間與 Bake 的下一次 plant（反射唯讀；與 LocomotionModel 的發布規則相同）
            private LocomotionModel _model;
            private FieldInfo _stopF;
            private MethodInfo _variantM;
            private AnimationFacadeBase _facade;
            // Phase 1e：T3 自己的接觸取樣（AccumulateContact，反射呼叫，只讀不寫）
            private MethodInfo _accumulateM;
            // Phase 2：resolver 狀態（反射唯讀）與最終 Hips 骨骼
            private FieldInfo _lLandF, _rLandF;
            private Transform _hips;
            // Phase 1e Step B：每個 next-plant 帧的預測輸入，與之後才知道的兩種落地姿勢（run 結束時以 T3 自己的接觸取樣計算）
            private struct NEntry { public int Frame; public bool Left; public Vector3 N; public float PubYaw, Bottom, RampN, PlantT; }
            private struct PoseSample { public int Frame; public bool Left; public float DisplayedClipT; public Quaternion RotInRoot; public float HAnim; public int Phase; }
            private readonly System.Collections.Generic.List<NEntry> _entries = new System.Collections.Generic.List<NEntry>();
            private readonly System.Collections.Generic.List<PoseSample> _poses = new System.Collections.Generic.List<PoseSample>();

            public void Init(CharacterPipelineRunner player, FootIKController ik, Vector3 ascent, Vector3 side, Vector3 center)
            {
                _player = player; _ik = ik; _ascent = ascent; _side = side; _center = center;
                const BindingFlags np = BindingFlags.Instance | BindingFlags.NonPublic;
                _settings = (FootIKSettings)typeof(FootIKController).GetField("settings", np).GetValue(ik);
                _mask = (int)typeof(FootIKController).GetField("_movementMask", np).GetValue(ik);
                _poseF = typeof(FootIKController).GetField("_poseData", np);
                _lF = typeof(FootIKController).GetField("_leftContact", np);
                _rF = typeof(FootIKController).GetField("_rightContact", np);
                _movingF = typeof(FootIKController).GetField("_moving", np);
                _prevRoot = player.transform.position;
                _model = player.GetComponent<LocomotionModel>();
                _stopF = typeof(LocomotionModel).GetField("_stop", np);
                _variantM = typeof(LocomotionModel).GetMethod("TryGetActiveVariant", np);
                _facade = player.GetComponent<AnimationFacadeBase>();
                _accumulateM = typeof(FootIKController).GetMethod("AccumulateContact", np);
                var sb = new StringBuilder("frame,phase,intent,s,lat,rootX,rootY,rootZ,rootYaw,velYaw,ascentYaw,posePlaneY,predActive");
                foreach (string p in new[] { "l", "r" })
                    foreach (string c in ("ph,role,beta,lift,lockH,tgtX,tgtY,tgtZ,poseX,poseY,poseZ,bottom,rampY,treadY,gHas,gY,lockSupY,predOk,predX,predY,predZ,predTreadY,predRampY,predS,predLat," +
                                              "pvx,pvz,h,vh,tc,exX,exZ,exTread3,liveTread3,predTread3,nOk,nFinal,nX,nY,nZ," +
                                              "rotW,rotIRx,rotIRy,rotIRz,rotIRw,hAnim,repSup,repPt,repDelta").Split(','))
                        sb.Append(',').Append(p).Append(c);
                sb.Append(",stopPlaying,clipT,clipDur,bkPlantT,bkPlantLeft,bkAnkX,bkAnkY,bkAnkZ,bkFinal,pubX,pubY,pubZ,pubYaw,snHas,snLeft,snX,snY,snZ,stopNT,facadeNT,stopActive,stopPending");
                // Phase 2（docs/45 §17）：骨盆求解器目標／套用到 IK 的值／最終 Hips 骨骼（相對 root），與兩腳的 resolver intent
                sb.Append(",pelvisSolver,pelvisOut,hipsRel");
                foreach (string p in new[] { "l", "r" })
                    foreach (string c in "Lv,Ly,Lc,Ls,Lr,Lw,Lb".Split(','))
                        sb.Append(',').Append(p).Append(c);
                _lLandF = typeof(FootIKController).GetField("_leftLanding", np);
                _rLandF = typeof(FootIKController).GetField("_rightLanding", np);
                Animator anim = player.GetComponentInChildren<Animator>();
                _hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                Csv.Append(sb).Append('\n');
            }

            private void LateUpdate()
            {
                Vector3 root = _player.transform.position;
                Vector3 vel = (root - _prevRoot) / Mathf.Max(Time.deltaTime, 1e-6f);
                _prevRoot = root;
                var pose = (FootIKPoseData)_poseF.GetValue(_ik);
                float hL = pose.LeftFootPosition.y - pose.PosePlaneY - pose.LeftFootBottomHeight;
                float hR = pose.RightFootPosition.y - pose.PosePlaneY - pose.RightFootBottomHeight;
                Vector3 prevL = _hasPrevPose ? _prevPoseL : pose.LeftFootPosition, prevR = _hasPrevPose ? _prevPoseR : pose.RightFootPosition;
                float prevHL = _hasPrevPose ? _prevHL : hL, prevHR = _hasPrevPose ? _prevHR : hR;
                _prevPoseL = pose.LeftFootPosition; _prevPoseR = pose.RightFootPosition; _prevHL = hL; _prevHR = hR; _hasPrevPose = true;
                if (!Recording) return;

                var moving = (MovingPelvisState)_movingF.GetValue(_ik);
                var td = _ik.TargetData;
                var gd = _ik.GroundData;
                LocomotionStopPrediction pred = _player.RuntimeData.LocomotionStopPrediction;
                float velYaw = new Vector2(vel.x, vel.z).sqrMagnitude > 1e-4f ? Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg : float.NaN;
                var sb = new StringBuilder();
                sb.Append(Time.frameCount).Append(',').Append(Phase).Append(',').Append(F(_player.RuntimeData.MovementIntent.DesiredSpeedNormalized)).Append(',')
                  .Append(F(Dot(root - _center, _ascent))).Append(',').Append(F(Dot(root - _center, _side))).Append(',')
                  .Append(F(root.x)).Append(',').Append(F(root.y)).Append(',').Append(F(root.z)).Append(',')
                  .Append(F(_player.transform.eulerAngles.y)).Append(',').Append(F(velYaw)).Append(',')
                  .Append(F(Mathf.Atan2(_ascent.x, _ascent.z) * Mathf.Rad2Deg)).Append(',')
                  .Append(F(pose.PosePlaneY)).Append(',').Append(pred.Active ? 1 : 0);
                Foot(sb, (FootContactState)_lF.GetValue(_ik), moving.DebugLeftRole, td.LeftFootPosition, pose.LeftFootPosition,
                    pose.LeftFootBottomHeight, gd.Left, root, pose.PosePlaneY, pred, true);
                Candidates(sb, pose.LeftFootPosition, prevL, hL, prevHL, pose.LeftFootRotation, pose.LeftFootBottomHeight, td.LeftFootPosition, pred, true);
                Semantics(sb, (FootContactState)_lF.GetValue(_ik), pose.LeftFootRotation, td.LeftFootRotation, td.LeftFootRotationWeight,
                    td.LeftFootPosition, pose.LeftFootPosition, pose.PosePlaneY, pose.LeftFootBottomHeight, true);
                Foot(sb, (FootContactState)_rF.GetValue(_ik), moving.DebugRightRole, td.RightFootPosition, pose.RightFootPosition,
                    pose.RightFootBottomHeight, gd.Right, root, pose.PosePlaneY, pred, false);
                Candidates(sb, pose.RightFootPosition, prevR, hR, prevHR, pose.RightFootRotation, pose.RightFootBottomHeight, td.RightFootPosition, pred, false);
                Semantics(sb, (FootContactState)_rF.GetValue(_ik), pose.RightFootRotation, td.RightFootRotation, td.RightFootRotationWeight,
                    td.RightFootPosition, pose.RightFootPosition, pose.PosePlaneY, pose.RightFootBottomHeight, false);
                StopClip(sb, pred);
                sb.Append(',').Append(F(moving.Offset)).Append(',').Append(F(td.PelvisOffsetY))
                  .Append(',').Append(F(_hips != null ? _hips.position.y - root.y : float.NaN));
                Landing(sb, (LandingSurfaceState)_lLandF.GetValue(_ik));
                Landing(sb, (LandingSurfaceState)_rLandF.GetValue(_ik));
                Csv.Append(sb).Append('\n');
            }

            private static void Landing(StringBuilder sb, LandingSurfaceState s)
            {
                LandingSurfaceIntent i = s.Intent;
                sb.Append(',').Append(i.Valid ? 1 : 0).Append(',').Append(F(i.Valid ? i.SurfaceY : float.NaN))
                  .Append(',').Append((int)i.Confidence).Append(',').Append((int)i.Source).Append(',').Append(i.Revisions)
                  .Append(',').Append(F(i.Weight)).Append(',').Append(F(i.Weight > 0f ? i.BlendSurfaceY : float.NaN));
            }

            private void Foot(StringBuilder sb, FootContactState c, int role, Vector3 tgt, Vector3 poseAnkle, float bottom,
                in FootGroundContact g, Vector3 root, float posePlaneY, in LocomotionStopPrediction pred, bool left)
            {
                bool predOk = false; Vector3 p = default;
                if (pred.Active) predOk = _ik.TryPredictStopLandingGoal(in pred, left, out p);
                sb.Append(',').Append((int)c.Phase).Append(',').Append(role).Append(',').Append(F(c.DebugBeta)).Append(',').Append(F(c.DebugLift))
                  .Append(',').Append(F(c.Phase == FootLockPhase.Locked ? c.LockedHeight : float.NaN))
                  .Append(',').Append(V(tgt)).Append(',').Append(V(poseAnkle)).Append(',').Append(F(bottom))
                  .Append(',').Append(F(RefY(tgt, root, posePlaneY))).Append(',').Append(F(Tread(tgt)))
                  .Append(',').Append(g.HasGround ? 1 : 0).Append(',').Append(F(g.HasGround ? g.Point.y : float.NaN))
                  .Append(',').Append(F(c.Phase == FootLockPhase.Locked ? c.LockedSupportY : float.NaN))
                  .Append(',').Append(predOk ? 1 : 0).Append(',').Append(predOk ? V(p) : "nan,nan,nan")
                  .Append(',').Append(F(predOk ? Tread(p) : float.NaN)).Append(',').Append(F(predOk ? RefY(p, root, posePlaneY) : float.NaN))
                  .Append(',').Append(F(predOk ? Dot(p - _center, _ascent) : float.NaN)).Append(',').Append(F(predOk ? Dot(p - _center, _side) : float.NaN));
            }

            /// <summary>
            /// Phase 1 目的地候選（只量測）：動畫踝的水平速度、腳底離 pose 平面的高度與垂直速度、到接觸的時間；
            /// 以水平速度外插的落點；三個位置（外插／目前目標／預測）下的三點最高踏面（與 T3 接觸同一規則，腳只取 yaw）。
            /// </summary>
            private void Candidates(StringBuilder sb, Vector3 poseAnkle, Vector3 prevPoseAnkle, float h, float prevH, Quaternion poseRot,
                float bottom, Vector3 tgt, in LocomotionStopPrediction pred, bool left)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-6f);
                Vector2 v = new Vector2(poseAnkle.x - prevPoseAnkle.x, poseAnkle.z - prevPoseAnkle.z) / dt;
                float vh = (h - prevH) / dt;
                float tc = vh < -0.05f ? Mathf.Clamp(Mathf.Max(0f, h) / -vh, 0f, 0.5f) : float.NaN;
                Vector3 fwd = poseRot * Vector3.forward; fwd.y = 0f;
                Quaternion yawOnly = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
                Vector3 ex = float.IsNaN(tc) ? new Vector3(float.NaN, poseAnkle.y, float.NaN)
                    : new Vector3(poseAnkle.x + v.x * tc, poseAnkle.y, poseAnkle.z + v.y * tc);
                bool predOk = false; Vector3 p = default;
                if (pred.Active) predOk = _ik.TryPredictStopLandingGoal(in pred, left, out p);
                sb.Append(',').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(h)).Append(',').Append(F(vh)).Append(',').Append(F(tc))
                  .Append(',').Append(F(ex.x)).Append(',').Append(F(ex.z))
                  .Append(',').Append(F(float.IsNaN(tc) ? float.NaN : Tread3(ex, yawOnly, bottom)))
                  .Append(',').Append(F(Tread3(tgt, yawOnly, bottom)))
                  .Append(',').Append(F(predOk ? Tread3(p, yawOnly, bottom) : float.NaN));
                // Phase 1b：下一次 planned plant（ADR-010 D5）是否就是這隻腳，及其落點（provider 已落地：y ＝ 踏面 ＋ 腳底厚度）
                bool nOk = false, nFinal = false; Vector3 n = default;
                if (pred.HasNextPlant && _ik.TryPredictNextPlantGoal(in pred, out bool nLeft, out nFinal, out n)) nOk = nLeft == left;
                if (nOk)
                    _entries.Add(new NEntry { Frame = Time.frameCount, Left = left, N = n, PubYaw = pred.RootYawDegreesAtPublish, Bottom = bottom,
                                              RampN = RefY(n, _player.transform.position, ((FootIKPoseData)_poseF.GetValue(_ik)).PosePlaneY), PlantT = NextPlantClipTime(left) });
                sb.Append(',').Append(nOk ? 1 : 0).Append(',').Append(nOk && nFinal ? 1 : 0).Append(',').Append(nOk ? V(n) : "nan,nan,nan");
            }

            /// <summary>
            /// Phase 1e：以 T3 本帧的實際輸入重跑 T3 自己的接觸取樣（未選定支撐的那一次：鎖定當帧就是這個）。
            /// ankle ＝ 目標 − lift（鎖定當帧 ResolveHeight 回傳即時解）；rotation ＝ Slerp(動畫旋轉, 目標旋轉, 權重)（T3 的 shownRotation）。
            /// 另記：腳的顯示旋轉相對 root yaw（落地姿勢樣本）、動畫踝離 pose 平面的高度。
            /// </summary>
            private void Semantics(StringBuilder sb, FootContactState c, Quaternion poseRot, Quaternion tgtRot, float rotWeight,
                Vector3 tgt, Vector3 poseAnkle, float posePlaneY, float bottom, bool left)
            {
                Quaternion shown = Quaternion.Slerp(poseRot, tgtRot, rotWeight);
                Quaternion rootYaw = Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f);
                Quaternion inRoot = Quaternion.Inverse(rootYaw) * shown;
                Vector3 ankle = new Vector3(tgt.x, tgt.y - c.DebugLift, tgt.z);
                float distance = _settings.RaycastUpOffset + _settings.RaycastDistance;
                float[] offs = { -Mathf.Max(0f, _settings.HeelOffset), 0f, Mathf.Max(0f, _settings.ToeOffset) };
                float contactDelta = float.NegativeInfinity, supportY = 0f; int supportPoint = 0; bool hasContact = false, liveHit = false;
                for (int i = 0; i < 3; i++)
                {
                    object[] args = { c, false, false, i, ankle, shown, bottom, offs[i], distance, contactDelta, supportY, supportPoint, hasContact, liveHit };
                    _accumulateM.Invoke(_ik, args);
                    contactDelta = (float)args[9]; supportY = (float)args[10]; supportPoint = (int)args[11]; hasContact = (bool)args[12]; liveHit = (bool)args[13];
                }
                _poses.Add(new PoseSample { Frame = Time.frameCount, Left = left, DisplayedClipT = DisplayedClipTime(), RotInRoot = inRoot,
                                            HAnim = poseAnkle.y - posePlaneY, Phase = (int)c.Phase });
                sb.Append(',').Append(F(rotWeight)).Append(',').Append(F(inRoot.x)).Append(',').Append(F(inRoot.y)).Append(',').Append(F(inRoot.z)).Append(',').Append(F(inRoot.w))
                  .Append(',').Append(F(poseAnkle.y - posePlaneY))
                  .Append(',').Append(F(hasContact ? supportY : float.NaN)).Append(',').Append(hasContact ? supportPoint : -1).Append(',').Append(F(hasContact ? contactDelta : float.NaN));
            }

            private (MotionBakeData bake, float clipT, bool playing) StopState()
            {
                object stop = _stopF.GetValue(_model);
                bool playing = (bool)stop.GetType().GetProperty("IsPlaying").GetValue(stop);
                float nt = (float)stop.GetType().GetProperty("NormalizedTime").GetValue(stop);
                object[] args = { null };
                bool hasVariant = (bool)_variantM.Invoke(_model, args);
                MotionBakeData bake = hasVariant ? ((LocomotionStopVariant)args[0]).BakeData : null;
                return (bake, bake != null && playing ? Mathf.Clamp01(nt) * bake.Duration : 0f, playing);
            }

            /// <summary>畫面上姿勢的 clip 時間（Animancer 在 IK 之後的 normalized time）；停步未播放 ⇒ NaN。</summary>
            private float DisplayedClipTime()
            {
                var st = StopState();
                return st.bake != null && st.playing && _facade != null ? Mathf.Clamp01(_facade.GetNormalizedTime()) * st.bake.Duration : float.NaN;
            }

            private float NextPlantClipTime(bool left)
            {
                var st = StopState();
                if (st.bake == null) return float.NaN;
                FootPlantSchedule sch = st.bake.FootPlantSchedule;
                for (int i = 0; i < sch.ValidEventCount; i++)
                {
                    FootPlantScheduleEvent e = sch.GetEvent(i);
                    if (e.ClipTime <= st.clipT + 1e-5f) continue;
                    return (e.Side == FootPlantSide.Left) == left ? e.ClipTime : float.NaN;
                }
                return float.NaN;
            }

            /// <summary>以 T3 自己的 AccumulateContact，在指定踝位與腳姿下算「鎖定當帧」會選的支撐踏面。</summary>
            private float T3Support(Vector3 ankle, Quaternion rot, float bottom)
            {
                float distance = _settings.RaycastUpOffset + _settings.RaycastDistance;
                float[] offs = { -Mathf.Max(0f, _settings.HeelOffset), 0f, Mathf.Max(0f, _settings.ToeOffset) };
                float contactDelta = float.NegativeInfinity, supportY = 0f; int supportPoint = 0; bool hasContact = false, liveHit = false;
                for (int i = 0; i < 3; i++)
                {
                    object[] args = { default(FootContactState), false, false, i, ankle, rot, bottom, offs[i], distance, contactDelta, supportY, supportPoint, hasContact, liveHit };
                    _accumulateM.Invoke(_ik, args);
                    contactDelta = (float)args[9]; supportY = (float)args[10]; supportPoint = (int)args[11]; hasContact = (bool)args[12];
                }
                return hasContact ? supportY : float.NaN;
            }

            /// <summary>
            /// Phase 1e Step B（run 結束時；幾何是靜態的，射線結果與當時相同）：每個 next-plant 帧，在預測 XZ 上以 T3 規則算支撐——
            /// B1 用「畫面 clip 時間第一次到達 Bake plant」那一帧的腳姿；B2 用該腳下一次實際鎖定那一帧的腳姿（上界）。
            /// 踝高 ＝ 預測 XZ 下的移動斜坡（T3 同一條射線）＋ 該姿勢的動畫高度。
            /// </summary>
            public string FinishSemantics()
            {
                var sb = new StringBuilder("frame,foot,plantT,b0,b1,b2,plantFrame,lockFrame\n");
                foreach (NEntry e in _entries)
                {
                    PoseSample? plant = null, lockPose = null;
                    foreach (PoseSample p in _poses)
                    {
                        if (p.Left != e.Left || p.Frame < e.Frame) continue;
                        if (plant == null && !float.IsNaN(e.PlantT) && !float.IsNaN(p.DisplayedClipT) && p.DisplayedClipT >= e.PlantT - 1e-4f) plant = p;
                        if (lockPose == null && p.Phase == (int)FootLockPhase.Locked) lockPose = p;
                    }
                    Quaternion yaw = Quaternion.Euler(0f, e.PubYaw, 0f);
                    float b1 = plant.HasValue ? T3Support(new Vector3(e.N.x, e.RampN + plant.Value.HAnim, e.N.z), yaw * plant.Value.RotInRoot, e.Bottom) : float.NaN;
                    float b2 = lockPose.HasValue ? T3Support(new Vector3(e.N.x, e.RampN + lockPose.Value.HAnim, e.N.z), yaw * lockPose.Value.RotInRoot, e.Bottom) : float.NaN;
                    sb.Append(e.Frame).Append(',').Append(e.Left ? 'l' : 'r').Append(',').Append(F(e.PlantT)).Append(',').Append(F(e.N.y - e.Bottom))
                      .Append(',').Append(F(b1)).Append(',').Append(F(b2)).Append(',').Append(plant.HasValue ? plant.Value.Frame : -1)
                      .Append(',').Append(lockPose.HasValue ? lockPose.Value.Frame : -1).Append('\n');
                }
                return sb.ToString();
            }

            /// <summary>Phase 1c：停步 clip 時間、Bake 的下一次 plant（clip 時間晚於目前的第一個事件）、快照原始值。</summary>
            private void StopClip(StringBuilder sb, in LocomotionStopPrediction pred)
            {
                object stop = _stopF.GetValue(_model);
                bool playing = (bool)stop.GetType().GetProperty("IsPlaying").GetValue(stop);
                bool active = (bool)stop.GetType().GetProperty("IsActive").GetValue(stop);
                bool pending = (bool)stop.GetType().GetProperty("IsPending").GetValue(stop);
                float nt = (float)stop.GetType().GetProperty("NormalizedTime").GetValue(stop);
                object[] args = { null };
                bool hasVariant = (bool)_variantM.Invoke(_model, args);
                MotionBakeData bake = hasVariant ? ((LocomotionStopVariant)args[0]).BakeData : null;
                float dur = bake != null ? bake.Duration : float.NaN;
                float clipT = bake != null ? (playing ? Mathf.Clamp01(nt) * dur : 0f) : float.NaN;
                string plant = "nan,nan,nan,nan,nan,nan";
                if (bake != null && active)
                {
                    FootPlantSchedule sch = bake.FootPlantSchedule;
                    for (int i = 0; i < sch.ValidEventCount; i++)
                    {
                        FootPlantScheduleEvent e = sch.GetEvent(i);
                        if (e.ClipTime <= clipT + 1e-5f) continue;
                        plant = F(e.ClipTime) + "," + (e.Side == FootPlantSide.Left ? 1 : 0) + "," + V(e.AnkleInRoot) + "," + (i == sch.ValidEventCount - 1 ? 1 : 0);
                        break;
                    }
                }
                sb.Append(',').Append(playing ? 1 : 0).Append(',').Append(F(clipT)).Append(',').Append(F(dur)).Append(',').Append(plant)
                  .Append(',').Append(V(pred.RootWorldPositionAtPublish)).Append(',').Append(F(pred.RootYawDegreesAtPublish))
                  .Append(',').Append(pred.HasNextPlant ? 1 : 0).Append(',').Append(pred.NextPlantIsLeft ? 1 : 0).Append(',').Append(V(pred.NextPlantAnkleInPublishRoot))
                  // Phase 1c：LocomotionStopRuntime 記錄的 normalized time（Update，順序 3）vs Animancer 目前狀態在 IK 之後的 normalized time
                  .Append(',').Append(F(nt)).Append(',').Append(F(_facade != null ? _facade.GetNormalizedTime() : float.NaN))
                  .Append(',').Append(active ? 1 : 0).Append(',').Append(pending ? 1 : 0);
            }

            /// <summary>腳跟／腳踝底／腳尖三點下方最高的踏面（GroundLayers；起點＝該點與腳踝較高者 ＋ RaycastUpOffset）。</summary>
            private float Tread3(Vector3 ankle, Quaternion rot, float bottom)
            {
                float best = float.NegativeInfinity;
                float[] offs = { -Mathf.Max(0f, _settings.HeelOffset), 0f, Mathf.Max(0f, _settings.ToeOffset) };
                foreach (float o in offs)
                {
                    Vector3 pt = FootContactSolver.SolePoint(ankle, rot, bottom, o);
                    if (Physics.Raycast(new Vector3(pt.x, Mathf.Max(pt.y, ankle.y) + _settings.RaycastUpOffset, pt.z), Vector3.down, out RaycastHit hit,
                            _settings.RaycastUpOffset + _settings.RaycastDistance + 1f, _settings.GroundLayers, QueryTriggerInteraction.Ignore))
                        best = Mathf.Max(best, hit.point.y);
                }
                return float.IsNegativeInfinity(best) ? float.NaN : best;
            }

            /// <summary>T3 對擺盪腳用的同一條射線（FootIKController.ResolveContactPosition ③；移動面遮罩 ⇒ 樓梯上打到的是移動斜坡）。</summary>
            private float RefY(Vector3 h, Vector3 root, float posePlaneY)
            {
                float up = _settings.RaycastUpOffset;
                float reach = new Vector2(h.x - root.x, h.z - root.z).magnitude;
                float probeDistance = up + _settings.RaycastDistance;
                return Physics.Raycast(new Vector3(h.x, posePlaneY + up + reach, h.z), Vector3.down, out RaycastHit hit,
                    probeDistance + 2f * reach, _mask, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;
            }

            /// <summary>踏面（T3 接觸取樣用的 GroundLayers，不是 CharacterController 的移動斜坡）：腳踝 XZ 正下方最高的面。</summary>
            /// 起點與 T3 接觸取樣相同（腳踝高度 ＋ RaycastUpOffset），避免打到上方的其他 GroundLayers 幾何。
            private float Tread(Vector3 h) =>
                Physics.Raycast(new Vector3(h.x, h.y + _settings.RaycastUpOffset, h.z), Vector3.down, out RaycastHit hit,
                    _settings.RaycastUpOffset + _settings.RaycastDistance + 1f, _settings.GroundLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : float.NaN;

            private static float Dot(Vector3 v, Vector3 axis) => v.x * axis.x + v.z * axis.z;
            private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
            private static string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
