#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using Project.Presentation.Motion;

namespace Project.Editor
{
    public class MotionBakeEditor : EditorWindow
    {
        private AnimationClip sourceClip;
        private float sampleRate = 30f;

        [MenuItem("Tools/Project/動畫根運動物理烘焙工具 v3.0")]
        public static void ShowWindow()
        {
            GetWindow<MotionBakeEditor>("動畫物理烘焙工具");
        }

        private void OnGUI()
        {
            GUILayout.Label("【表現層核心 - 物理特徵曲線烘焙器】", EditorStyles.boldLabel);
            GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                sourceClip = (AnimationClip)EditorGUILayout.ObjectField("目標 Animation Clip", sourceClip, typeof(AnimationClip), false);
                sampleRate = EditorGUILayout.FloatField("採樣率 (FPS)", sampleRate);
                sampleRate = Mathf.Clamp(sampleRate, 10f, 120f);
            }

            GUILayout.Space(15);

            using (new EditorGUI.DisabledScope(sourceClip == null))
            {
                if (GUILayout.Button("開始提取物理特徵並生成內建曲線", GUILayout.Height(30)))
                {
                    BakeClipMotion();
                }
            }
        }

        private void BakeClipMotion()
        {
            if (sourceClip == null) return;

            EditorUtility.DisplayProgressBar("物理特徵烘焙", $"正在採樣動畫: {sourceClip.name}...", 0.1f);

            float duration = sourceClip.length;
            int totalFrames = Mathf.CeilToInt(duration * sampleRate) + 1;
            float interval = 1f / sampleRate;

            // 初始化新曲線
            AnimationCurve speedCurve = new AnimationCurve();
            AnimationCurve rotationCurve = new AnimationCurve();

            // 建立虛擬採樣物件
            GameObject bakeDummy = new GameObject("BakeDummy");
            Transform rootTransform = bakeDummy.transform;

            // 物理追蹤輔助變數
            Vector3 lastPos = Vector3.zero;
            Quaternion lastRot = Quaternion.identity;
            float accRotY = 0f;

            try
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    float time = i * interval;
                    if (time > duration) time = duration;

                    float progress = (float)i / totalFrames;
                    EditorUtility.DisplayProgressBar("物理特徵烘焙", $"正在計算物理曲線: {i}/{totalFrames}", progress);

                    // 強制動畫取樣
                    sourceClip.SampleAnimation(bakeDummy, time);

                    Vector3 currentPos = rootTransform.position;
                    Quaternion currentRot = rootTransform.rotation;

                    if (i == 0)
                    {
                        // 第一幀初始化基準點
                        lastPos = currentPos;
                        lastRot = currentRot;
                        speedCurve.AddKey(0f, 0f);
                        rotationCurve.AddKey(0f, 0f);
                        continue;
                    }

                    // 1. 計算水平面瞬時速度 (XZ平面)
                    float dist = Vector3.Distance(new Vector3(currentPos.x, 0f, currentPos.z), new Vector3(lastPos.x, 0f, lastPos.z));
                    float currentSpeed = dist / interval;
                    speedCurve.AddKey(time, currentSpeed);

                    // 2. 計算連續偏航角 (Yaw)
                    Quaternion deltaRot = currentRot * Quaternion.Inverse(lastRot);
                    Vector3 rotatedForward = deltaRot * Vector3.forward;
                    rotatedForward.y = 0f; // 忽略垂直軸偏移

                    // 計算這一幀轉了幾度，並累加到總角度中
                    float deltaYaw = Vector3.SignedAngle(Vector3.forward, rotatedForward.normalized, Vector3.up);
                    accRotY += deltaYaw;
                    rotationCurve.AddKey(time, accRotY);

                    // 紀錄快照供下一幀比對
                    lastPos = currentPos;
                    lastRot = currentRot;
                }
            }
            finally
            {
                DestroyImmediate(bakeDummy);
                EditorUtility.ClearProgressBar();
            }

            // 保存資源邏輯
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

            // 寫入升級後的曲線數據
            asset.SourceClip = sourceClip;
            asset.SampleRate = sampleRate;
            asset.SpeedCurve = speedCurve;
            asset.RotationCurve = rotationCurve;

            if (isNewAsset) AssetDatabase.CreateAsset(asset, path);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("物理烘焙成功！", $"速度與旋轉曲線已完美匯出至：\n{path}", "確定");
        }
    }
}
#endif