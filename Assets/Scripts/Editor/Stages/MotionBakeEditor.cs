#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Project.Presentation.Motion;

namespace Project.Editor
{
    public class MotionBakeEditor : EditorWindow
    {
        private const string OutputFolderPath = "Assets/ScriptableObjects/Motion";

        private AnimationClip sourceClip;

        // 通用批次清單只保存明確選入的 AnimationClip 子資產；不從 FBX 本體自動展開，避免一次誤烘整包 take。
        [SerializeField] private List<AnimationClip> batchClips = new List<AnimationClip>();
        [SerializeField] private bool showBatchClips = true;
        [SerializeField] private Vector2 scrollPosition;

        // 🆕 Humanoid 根運動仰賴 Avatar 重定向，必須用真正掛了 Animator + Avatar 的角色模型採樣
        private GameObject characterPrefab;

        private float sampleRate = 30f;

        // 進階物理特徵參數（v0.7 新增，取自參考演算法的純數學部分，不含其反射注入架構）
        private HumanBodyBones leftFootBone = HumanBodyBones.LeftFoot;
        private HumanBodyBones rightFootBone = HumanBodyBones.RightFoot;
        private float rotationAngleToleranceDeg = 5f;
        private bool bakeTargetLocalDirection = true;
        private float localDirFilterAngleDeg = 12f;
        private float localDirMinDistance = 0.02f;

        // 🆕（自動化特徵分析）腳的世界高度超過「自身 Rest Pose 基線 + 此容忍度」視為該腳騰空；
        // 雙腳同時騰空判定為離地，落地偵測沿用同一容忍度（預設 0.03 以吸收蹬伸期踝骨抬升雜訊）
        private float takeoffFootLiftThreshold = 0.03f;

        // 內部姿態採樣單元，僅用於腳相判定
        private struct PoseInfo { public Vector3 LeftLocal; public Vector3 RightLocal; }

        [MenuItem("Tools/Project/動畫根運動物理烘焙工具 v4.0")]
        public static void ShowWindow()
        {
            MotionBakeEditor window = GetWindow<MotionBakeEditor>("動畫物理烘焙工具");
            window.minSize = new Vector2(400f, 320f);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("【表現層核心 - 物理特徵曲線烘焙器】", EditorStyles.boldLabel);
            GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label("基本設定", EditorStyles.boldLabel);
                sourceClip = (AnimationClip)EditorGUILayout.ObjectField("目標 Animation Clip", sourceClip, typeof(AnimationClip), false);
                characterPrefab = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("採樣用角色模型", "Root 或其子階層必須有且只有一個 Animator，且綁定 Humanoid 類型的 Avatar"),
                    characterPrefab, typeof(GameObject), false);
                sampleRate = EditorGUILayout.FloatField("採樣率 (FPS)", sampleRate);
                sampleRate = Mathf.Clamp(sampleRate, 10f, 120f);

                EditorGUILayout.HelpBox(
                    "角色 Root 或其子階層必須有且只有一個有效的 Humanoid Animator，\n" +
                    "否則無法透過重定向正確採到根運動位移。",
                    MessageType.Info);
            }

            GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label("進階物理特徵", EditorStyles.boldLabel);
                leftFootBone = (HumanBodyBones)EditorGUILayout.EnumPopup("左腳骨骼", leftFootBone);
                rightFootBone = (HumanBodyBones)EditorGUILayout.EnumPopup("右腳骨骼", rightFootBone);
                rotationAngleToleranceDeg = Mathf.Max(0f, EditorGUILayout.FloatField("旋轉收斂容忍度 (度)", rotationAngleToleranceDeg));

                GUILayout.Space(6);
                bakeTargetLocalDirection = EditorGUILayout.ToggleLeft("烘焙混合樹本地方向 (TargetLocalDirection)", bakeTargetLocalDirection);
                using (new EditorGUI.DisabledScope(!bakeTargetLocalDirection))
                {
                    localDirFilterAngleDeg = Mathf.Clamp(EditorGUILayout.FloatField("方向過濾角度閾值 (度)", localDirFilterAngleDeg), 0f, 90f);
                    localDirMinDistance = Mathf.Max(0f, EditorGUILayout.FloatField("最小有效位移距離 (m)", localDirMinDistance));
                }

                GUILayout.Space(6);
                takeoffFootLiftThreshold = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("起跳離地容忍度 (m)", "自動特徵分析用：腳的世界高度超過『自身 Rest Pose 基線 + 此值』視為該腳騰空；雙腳同時騰空判定為離地，落地偵測沿用同一容忍度"),
                    takeoffFootLiftThreshold));

                EditorGUILayout.HelpBox(
                    "旋轉收斂容忍度：判定旋轉曲線在第幾秒就已經穩定在終值附近，之後的角度變化視為抖動雜訊。\n" +
                    "本地方向過濾：位移量小於門檻，或方向已經很接近正前方時，視為原地動作 (Vector3.zero)。",
                    MessageType.None);
            }

            GUILayout.Space(15);

            bool canBake = sourceClip != null && characterPrefab != null;
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button("開始提取物理特徵並生成內建曲線", GUILayout.Height(30)))
                {
                    BakeClipMotion();
                }
            }

            GUILayout.Space(16);

            DrawBatchBakeSection();

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void BakeClipMotion()
            => BakeClipMotion(showResultDialog: true);

        private void DrawBatchBakeSection()
        {
            if (batchClips == null) batchClips = new List<AnimationClip>();

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label("批次烘焙", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "將多支 AnimationClip 拖入下方或從 Project 選取加入。全部共用上方的採樣角色、FPS 與進階參數。\n" +
                    "Import preset 依動作類型而異，本工具不猜測也不修改；請先用「Project 動畫匯入 SOP」明確套用。",
                    MessageType.None);

                Rect dropArea = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
                GUI.Box(dropArea, "拖放 AnimationClip 子資產到這裡");
                HandleBatchDragAndDrop(dropArea);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("加入 Project 選取的 Clip")) AddSelectedBatchClips();
                    using (new EditorGUI.DisabledScope(batchClips.Count == 0))
                    {
                        if (GUILayout.Button("清空", GUILayout.Width(64f))) batchClips.Clear();
                    }
                }

                showBatchClips = EditorGUILayout.Foldout(showBatchClips, $"待烘焙清單（{batchClips.Count}）", true);
                if (showBatchClips)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < batchClips.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            batchClips[i] = (AnimationClip)EditorGUILayout.ObjectField(batchClips[i], typeof(AnimationClip), false);
                            if (GUILayout.Button("移除", GUILayout.Width(52f)))
                            {
                                batchClips.RemoveAt(i);
                                i--;
                            }
                        }
                    }
                    EditorGUI.indentLevel--;
                }

                bool canRunBatch = batchClips.Count > 0 && characterPrefab != null &&
                                   !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;
                using (new EditorGUI.DisabledScope(!canRunBatch))
                {
                    if (GUILayout.Button($"批次烘焙 {batchClips.Count} 支動畫", GUILayout.Height(30)))
                        RunBatchBake();
                }
            }
        }

        private void HandleBatchDragAndDrop(Rect dropArea)
        {
            Event current = Event.current;
            if (!dropArea.Contains(current.mousePosition) ||
                (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
            {
                return;
            }

            bool containsClip = false;
            Object[] dragged = DragAndDrop.objectReferences;
            for (int i = 0; i < dragged.Length; i++)
            {
                if (dragged[i] is AnimationClip)
                {
                    containsClip = true;
                    break;
                }
            }

            if (!containsClip) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                for (int i = 0; i < dragged.Length; i++)
                    if (dragged[i] is AnimationClip clip) AddBatchClip(clip);
            }

            current.Use();
        }

        private void AddSelectedBatchClips()
        {
            int added = 0;
            Object[] selected = Selection.objects;
            for (int i = 0; i < selected.Length; i++)
                if (selected[i] is AnimationClip clip && AddBatchClip(clip)) added++;

            if (added == 0)
                Debug.LogWarning("[Motion Bake Batch] Project 選取範圍沒有新的 AnimationClip 子資產；FBX 本體不會自動展開。");
        }

        private bool AddBatchClip(AnimationClip clip)
        {
            if (clip == null || batchClips.Contains(clip)) return false;
            batchClips.Add(clip);
            return true;
        }

        private void RunBatchBake()
        {
            if (!TryBuildBatch(out AnimationClip[] clips, out string batchError))
            {
                EditorUtility.DisplayDialog("批次烘焙無法開始", batchError, "了解");
                return;
            }

            if (!TryResolveHumanoidAnimator(characterPrefab, out _, out string animatorError))
            {
                EditorUtility.DisplayDialog("批次烘焙無法開始", animatorError, "了解");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "批次烘焙動畫",
                    $"將以目前設定烘焙 {clips.Length} 支動畫，建立／更新 {OutputFolderPath}/Bake_*.asset。\n\n" +
                    "本操作不會修改任何 Animation Import 設定。",
                    "開始",
                    "取消"))
            {
                return;
            }

            AnimationClip originalSource = sourceClip;
            int completed = 0;

            try
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    sourceClip = clips[i];
                    if (!BakeClipMotion(showResultDialog: false))
                        throw new System.InvalidOperationException($"'{clips[i].name}' 烘焙器回報失敗。");
                    completed++;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "批次烘焙中止",
                    $"已完成 {completed}/{clips.Length} 支；失敗原因已寫入 Console。\n\n{exception.Message}",
                    "了解");
                return;
            }
            finally
            {
                sourceClip = originalSource;
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"[Motion Bake Batch] 已以 {sampleRate:F0} FPS 完成 {clips.Length} 支動畫；輸出：{OutputFolderPath}");

            Object outputFolder = AssetDatabase.LoadAssetAtPath<Object>(OutputFolderPath);
            if (outputFolder != null)
            {
                Selection.activeObject = outputFolder;
                EditorGUIUtility.PingObject(outputFolder);
            }

            EditorUtility.DisplayDialog(
                "批次烘焙完成",
                $"已建立／更新 {clips.Length} 支 MotionBakeData。請依各動作需求檢查曲線與特徵。",
                "了解");
        }

        private bool TryBuildBatch(out AnimationClip[] clips, out string error)
        {
            if (batchClips == null || batchClips.Count == 0)
            {
                clips = null;
                error = "待烘焙清單不可為空。";
                return false;
            }

            clips = new AnimationClip[batchClips.Count];
            var unique = new HashSet<AnimationClip>();

            for (int i = 0; i < batchClips.Count; i++)
            {
                AnimationClip clip = batchClips[i];
                if (clip == null)
                {
                    clips = null;
                    error = $"待烘焙清單第 {i + 1} 列為空。";
                    return false;
                }
                if (!unique.Add(clip))
                {
                    clips = null;
                    error = $"待烘焙清單含有重複項目：{clip.name}";
                    return false;
                }

                clips[i] = clip;
            }

            error = null;
            return true;
        }

        private bool BakeClipMotion(bool showResultDialog)
        {
            if (sourceClip == null || characterPrefab == null) return false;

            if (!TryResolveHumanoidAnimator(characterPrefab, out _, out string animatorError))
            {
                if (showResultDialog)
                    EditorUtility.DisplayDialog("烘焙失敗", animatorError, "了解");
                else
                    Debug.LogError($"[Motion Bake] {animatorError}");
                return false;
            }

            EditorUtility.DisplayProgressBar("物理特徵烘焙", $"正在採樣動畫: {sourceClip.name}...", 0.1f);

            float duration = sourceClip.length;
            int totalFrames = Mathf.CeilToInt(duration * sampleRate) + 1;
            float interval = 1f / sampleRate;

            AnimationCurve speedCurve = new AnimationCurve();
            AnimationCurve rotationCurve = new AnimationCurve();

            // 🆕 Feature Analysis：在同一趟採樣迴圈蒐集逐影格原始特徵（根 Y + 雙腳世界高度），
            //    不額外重跑 SampleAnimation，也不影響下方既有的 Root Motion 曲線計算。
            List<MotionFeatureSample> featureSamples = new List<MotionFeatureSample>(totalFrames);

            GameObject bakeAgent = Instantiate(characterPrefab);
            bakeAgent.hideFlags = HideFlags.HideAndDontSave;

            if (!TryResolveHumanoidAnimator(bakeAgent, out Animator animator, out animatorError))
            {
                DestroyImmediate(bakeAgent);
                EditorUtility.ClearProgressBar();
                throw new System.InvalidOperationException(animatorError);
            }

            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Transform rootTransform = animator.transform;

            Vector3 lastPos = Vector3.zero;
            Quaternion lastRot = Quaternion.identity;
            float accRotY = 0f;

            // 🆕 記錄動畫「起始」的位置/朝向，之後算 TargetLocalDirection 要用作參考基準
            Vector3 startPos = Vector3.zero;
            Quaternion startRot = Quaternion.identity;

            try
            {
                rootTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // 🆕 世界空間相對足跡基線：在第一次 SampleAnimation「之前」快取替身雙腳的 Rest Pose 世界高度。
                // 基線屬於 rig 本身（踝骨自然離地高度），與 clip 內容完全解耦——這是起跳/落地偵測
                // 免疫「根節點蹲伏下沉被反相混疊成腳抬起」誤判的關鍵。骨骼引用一併快取，供迴圈內重複讀取。
                Transform leftFootT = animator.GetBoneTransform(leftFootBone);
                Transform rightFootT = animator.GetBoneTransform(rightFootBone);
                float leftFootBaselineY = leftFootT != null ? leftFootT.position.y : 0f;
                float rightFootBaselineY = rightFootT != null ? rightFootT.position.y : 0f;

                for (int i = 0; i < totalFrames; i++)
                {
                    float time = i * interval;
                    if (time > duration) time = duration;

                    float progress = (float)i / totalFrames;
                    EditorUtility.DisplayProgressBar("物理特徵烘焙", $"正在計算物理曲線: {i}/{totalFrames}", progress);

                    // Animator 允許位於角色 Root 的子階層；Humanoid Sample 必須對 Animator 所在物件執行，
                    // 否則只把 clip 丟給外層 gameplay Root，retarget 不會正確作用到 Model 骨架。
                    sourceClip.SampleAnimation(animator.gameObject, time);

                    Vector3 currentPos = rootTransform.position;
                    Quaternion currentRot = rootTransform.rotation;

                    // 🆕 蒐集特徵採樣：讀取雙腳「世界空間」高度（供起跳/落地偵測，與 Rest Pose 基線比較），
                    // 以及根節點世界 Y（供最高點推算）。純讀取、不重置 transform，故不影響下方既有的曲線計算。
                    float leftFootWorldY = leftFootT != null ? leftFootT.position.y : 0f;
                    float rightFootWorldY = rightFootT != null ? rightFootT.position.y : 0f;
                    featureSamples.Add(new MotionFeatureSample(time, currentPos.y, leftFootWorldY, rightFootWorldY));

                    if (i == 0)
                    {
                        startPos = currentPos;
                        startRot = currentRot;
                        lastPos = currentPos;
                        lastRot = currentRot;
                        speedCurve.AddKey(0f, 0f);
                        rotationCurve.AddKey(0f, 0f);
                        continue;
                    }

                    float dist = Vector3.Distance(new Vector3(currentPos.x, 0f, currentPos.z), new Vector3(lastPos.x, 0f, lastPos.z));
                    float currentSpeed = dist / interval;
                    speedCurve.AddKey(time, currentSpeed);

                    Quaternion deltaRot = currentRot * Quaternion.Inverse(lastRot);
                    Vector3 rotatedForward = deltaRot * Vector3.forward;
                    rotatedForward.y = 0f;

                    float deltaYaw = Vector3.SignedAngle(Vector3.forward, rotatedForward.normalized, Vector3.up);
                    accRotY += deltaYaw;
                    rotationCurve.AddKey(time, accRotY);

                    lastPos = currentPos;
                    lastRot = currentRot;
                }

                // 🆕 旋轉收斂裁剪：曲線已經烤完，直接用完成的 rotationCurve 算收斂時間，不需要再多跑一次採樣
                float rotationFinishedTime = CalculateRotationFinishedTime(rotationCurve, duration);

                // 🆕 末尾腳相判定：重置 transform 後在動畫結束時間點單獨採樣一次左右腳骨骼的相對高度
                PoseInfo endPose = SampleClipPose(animator, sourceClip, duration, leftFootBone, rightFootBone);
                FootPhase endPhase = (endPose.LeftLocal.y < endPose.RightLocal.y) ? FootPhase.LeftFootDown : FootPhase.RightFootDown;

                // 🆕 混合樹本地方向：用整段動畫的起點/終點位移，換算成起始朝向下的本地座標系方向
                Vector3 targetLocalDirection = Vector3.zero;
                if (bakeTargetLocalDirection)
                {
                    Vector3 startForwardVec = startRot * Vector3.forward;
                    startForwardVec.y = 0f;
                    float startRootYaw = Vector3.SignedAngle(Vector3.forward, startForwardVec.normalized, Vector3.up);
                    targetLocalDirection = CalculateTargetLocalDirection(startPos, lastPos, startRootYaw);
                }

                // 🆕 Feature Analysis Stage：以蒐集到的採樣建立分析上下文（容忍度由 Inspector 提供，基線來自 Rest Pose）
                MotionFeatureContext featureContext = new MotionFeatureContext(
                    featureSamples, duration, takeoffFootLiftThreshold, leftFootBaselineY, rightFootBaselineY);

                SaveAsset(speedCurve, rotationCurve, rotationFinishedTime, endPhase, targetLocalDirection, featureContext, showResultDialog);
            }
            finally
            {
                DestroyImmediate(bakeAgent);
                EditorUtility.ClearProgressBar();
            }

            return true;
        }

        /// <summary>
        /// 解析採樣角色中唯一的 Humanoid Animator。以「唯一有效元件」為契約，而非硬編碼 Model 子物件名稱；
        /// 這讓角色 Root 與純模型 Prefab 都能使用，同時在多模型階層出現歧義時大聲失敗。
        /// </summary>
        public static bool TryResolveHumanoidAnimator(GameObject characterRoot, out Animator animator, out string error)
        {
            animator = null;
            if (characterRoot == null)
            {
                error = "採樣角色不可為 null。";
                return false;
            }

            Animator[] candidates = characterRoot.GetComponentsInChildren<Animator>(includeInactive: true);
            for (int i = 0; i < candidates.Length; i++)
            {
                Animator candidate = candidates[i];
                if (candidate.avatar == null || !candidate.avatar.isHuman) continue;

                if (animator != null)
                {
                    animator = null;
                    error = $"採樣角色 '{characterRoot.name}' 的子階層含有多個 Humanoid Animator，無法判定採樣目標。";
                    return false;
                }

                animator = candidate;
            }

            if (animator == null)
            {
                error = $"採樣角色 '{characterRoot.name}' 的 Root／子階層找不到綁定有效 Humanoid Avatar 的 Animator。";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 🆕 旋轉完成判定：從最後一幀往前找，只要角度都落在「終值 ± 容忍度」內就視為抖動，
        /// 直到找到第一個超出容忍度的幀，其下一幀即為旋轉真正收斂完成的時間點。
        /// </summary>
        private float CalculateRotationFinishedTime(AnimationCurve rotCurve, float totalTime)
        {
            if (rotCurve == null || rotCurve.length == 0) return 0f;

            int lastIndex = rotCurve.length - 1;
            float finalAngle = rotCurve.keys[lastIndex].value;
            float tol = rotationAngleToleranceDeg;

            int i = lastIndex;
            for (; i >= 0; i--)
            {
                float v = rotCurve.keys[i].value;
                if (Mathf.Abs(Mathf.DeltaAngle(v, finalAngle)) > tol)
                {
                    break;
                }
            }

            if (i < 0)
            {
                return Mathf.Clamp(rotCurve.keys[0].time, 0f, totalTime);
            }

            int finishedIndex = Mathf.Min(lastIndex, i + 1);
            return Mathf.Clamp(rotCurve.keys[finishedIndex].time, 0f, totalTime);
        }

        /// <summary>
        /// 🆕 在指定時間點單獨採樣左右腳骨骼的世界位置，換算成相對於角色根節點的本地座標，
        /// 用 Y 軸高度比較判斷哪隻腳更接近地面（落地）。
        /// </summary>
        private PoseInfo SampleClipPose(Animator anim, AnimationClip clip, float time, HumanBodyBones leftBone, HumanBodyBones rightBone)
        {
            anim.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(anim.gameObject, time);

            Transform leftT = anim.GetBoneTransform(leftBone);
            Transform rightT = anim.GetBoneTransform(rightBone);

            return new PoseInfo
            {
                LeftLocal = leftT != null ? anim.transform.InverseTransformPoint(leftT.position) : Vector3.zero,
                RightLocal = rightT != null ? anim.transform.InverseTransformPoint(rightT.position) : Vector3.zero
            };
        }

        /// <summary>
        /// 🆕 把「起點→終點」的世界位移，投影到動畫起始朝向的本地座標系，
        /// 位移太小或方向太接近正前方就視為原地動作（回傳 Vector3.zero）。
        /// </summary>
        private Vector3 CalculateTargetLocalDirection(Vector3 startPos, Vector3 endPos, float startRootYaw)
        {
            Vector3 delta = endPos - startPos;
            delta.y = 0f;
            if (delta.magnitude < localDirMinDistance) return Vector3.zero;

            Quaternion startYawRot = Quaternion.Euler(0f, startRootYaw, 0f);
            Vector3 localDir = Quaternion.Inverse(startYawRot) * delta.normalized;
            localDir.y = 0f;
            localDir = localDir.sqrMagnitude > 0.0001f ? localDir.normalized : Vector3.zero;

            if (Vector3.Angle(Vector3.forward, localDir) <= localDirFilterAngleDeg) return Vector3.zero;

            return localDir;
        }

        /// <summary>
        /// 建立／更新 <see cref="MotionBakeData"/> 資產：先寫入既有的 Root Motion 曲線與進階特徵，
        /// 接著執行 Feature Analysis Stage 自動提取跳躍物理特徵，最後存檔並彈出結果對話框。
        /// </summary>
        private void SaveAsset(AnimationCurve speedCurve, AnimationCurve rotationCurve, float rotationFinishedTime, FootPhase endPhase, Vector3 targetLocalDirection, MotionFeatureContext featureContext, bool showResultDialog)
        {
            string dirPath = "Assets/ScriptableObjects/Motion/";
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            string path = $"{dirPath}Bake_{sourceClip.name}.asset";
            MotionBakeData asset = AssetDatabase.LoadAssetAtPath<MotionBakeData>(path);
            bool isNewAsset = false;

            if (asset == null)
            {
                asset = CreateInstance<MotionBakeData>();
                isNewAsset = true;
            }

            if (!isNewAsset) Undo.RecordObject(asset, "Update Motion Bake Data");

            asset.SourceClip = sourceClip;

            // 🆕（2026-07-26）動畫長度改為**烘焙期快照**：執行期的 MotionBakeData.Duration 自此讀本欄位，
            // 不再觸碰 SourceClip.length——切斷全專案最後一條「執行期邏輯讀 AnimationClip」的耦合。
            // 與 AutoAverageSpeed 同一個 pattern：Bake Data 是自足的純資料資產。
            asset.BakedDuration = sourceClip.length;
            asset.SampleRate = sampleRate;
            asset.SpeedCurve = speedCurve;
            asset.RotationCurve = rotationCurve;

            // 🆕（v0.16.2）代表移動速度：SpeedCurve 聚合成單一「動畫天生速度」，供 MotionDriver 滿速來源／
            // Mixer 門檻推導引用（dev-spec §3.2「動畫數據 → 配置」資料流）。與執行期 GetRepresentativeSpeed()
            // 的回退計算共用 MotionBakeData.ComputeAverageSpeed，確保烘焙值與即時回退值定義一致。
            asset.AutoAverageSpeed = MotionBakeData.ComputeAverageSpeed(speedCurve);

            // 🆕 進階特徵寫入
            asset.RotationFinishedTime = rotationFinishedTime;
            asset.EndPhase = endPhase;
            asset.TargetLocalDirection = targetLocalDirection;

            // 🆕 Feature Analysis Stage：自動提取跳躍物理特徵並寫入資產。
            // 位置刻意放在既有 Root Motion 曲線／旋轉收斂／腳相欄位「之後」，完全不干涉上方邏輯；
            // 內部自帶安全退化（非跳躍動畫重力回退 9.81），不會拋例外中斷存檔。
            new MotionFeatureAnalysisStage().Run(featureContext, asset);

            if (isNewAsset) AssetDatabase.CreateAsset(asset, path);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (showResultDialog)
            {
                EditorUtility.DisplayDialog(
                    "物理烘焙成功！",
                    $"曲線與進階特徵已匯出至：\n{path}\n\n" +
                    $"末尾腳相：{endPhase}\n" +
                    $"旋轉收斂時間：{rotationFinishedTime:F2}s\n" +
                    $"混合樹本地方向：{targetLocalDirection}\n\n" +
                    $"── 自動化特徵分析 ──\n" +
                    $"起跳前搖 (Takeoff Delay)：{asset.AutoTakeoffDelay:F3}s\n" +
                    $"最高點高度 (Apex Height)：{asset.AutoApexHeight:F3}m\n" +
                    $"滯空時間 (Air Time)：{asset.AutoAirTime:F3}s\n" +
                    $"逆推重力 (Gravity)：{asset.AutoCalculatedGravity:F3} m/s²",
                    "確定");
            }
        }
    }
}
#endif
