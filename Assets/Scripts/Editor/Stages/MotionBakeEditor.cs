#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Project.Presentation.Motion;
using Project.Core.StateMachine; // 🆕 引入狀態機配置命名空間

namespace Project.Editor
{
    public class MotionBakeEditor : EditorWindow
    {
        private enum BakeMode { SingleClip, ScanConfigSO }
        private BakeMode currentMode = BakeMode.SingleClip;

        [Header("基礎設定")]
        private GameObject characterPrefab;
        private float sampleRate = 30f;

        [Header("單檔案模式")]
        private AnimationClip singleSourceClip;

        [Header("🆕 批量自動化模式")]
        private StateMachineConfigSO targetConfigSO; // 點擊一鍵掃描配置檔內的所有動作

        [MenuItem("Tools/Project/動畫根運動物理烘焙工具 v4 (支援批量)")]
        public static void ShowWindow()
        {
            GetWindow<MotionBakeEditor>("動畫物理烘焙工具");
        }

        private void OnGUI()
        {
            GUILayout.Label("【表現層核心 - 離線內容建置管線 v4.5】", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // 繪製模式切換頁籤
            currentMode = (BakeMode)GUILayout.Toolbar((int)currentMode, new string[] { "單一動畫烘焙", "Config SO 批量掃描烘焙" });
            GUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label("全域環境配置", EditorStyles.boldLabel);
                characterPrefab = (GameObject)EditorGUILayout.ObjectField("採樣角色預製體(Prefab)", characterPrefab, typeof(GameObject), false);
                sampleRate = EditorGUILayout.FloatField("採樣率 (FPS)", sampleRate);
                sampleRate = Mathf.Clamp(sampleRate, 10f, 120f);
            }

            GUILayout.Space(5);

            // 根據選擇的模式渲染不同的輸入入口
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                if (currentMode == BakeMode.SingleClip)
                {
                    GUILayout.Label("單檔案模式設定", EditorStyles.boldLabel);
                    singleSourceClip = (AnimationClip)EditorGUILayout.ObjectField("目標 Animation Clip", singleSourceClip, typeof(AnimationClip), false);
                }
                else
                {
                    GUILayout.Label("自動化批量設定 (Source Discovery)", EditorStyles.boldLabel);
                    targetConfigSO = (StateMachineConfigSO)EditorGUILayout.ObjectField("目標 Config SO 資產", targetConfigSO, typeof(StateMachineConfigSO), false);
                    EditorGUILayout.HelpBox("💡 運作機制：工具將自動解剖此 Config SO 內的 Bake Mappings，找出所有動畫並自動完成「烘焙+覆寫落地」，無需手動拖拽！", MessageType.None);
                }
            }

            GUILayout.Space(15);

            // 防禦性按鈕鎖定
            bool canBake = characterPrefab != null &&
                           (currentMode == BakeMode.SingleClip ? singleSourceClip != null : targetConfigSO != null);

            using (new EditorGUI.DisabledScope(!canBake))
            {
                string btnText = currentMode == BakeMode.SingleClip ? "開始提取單一物理特徵" : "一鍵執行 Config 全動作批量烘焙";
                if (GUILayout.Button(btnText, GUILayout.Height(35)))
                {
                    ExecuteBakePipeline();
                }
            }
        }

        /// <summary>
        /// 總建置管線入口
        /// </summary>
        private void ExecuteBakePipeline()
        {
            // 1. 環境初始化防禦線 (不論多少動作，只生成一次木偶，極致效能效能最佳化)
            GameObject bakeDummy = Instantiate(characterPrefab);
            bakeDummy.name = "BakeDummy_Pipeline_Active";
            bakeDummy.hideFlags = HideFlags.HideAndDontSave;

            Animator animator = bakeDummy.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(bakeDummy);
                EditorUtility.DisplayDialog("管線中斷！", "錯誤：預製體不包含合法的人形 Humanoid Avatar！", "確定");
                return;
            }

            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            try
            {
                if (currentMode == BakeMode.SingleClip)
                {
                    // 執行單一烘焙
                    string dirPath = "Assets/ScriptableObjects/Motion/";
                    string path = $"{dirPath}Bake_{singleSourceClip.name}.asset";

                    BakeCoreProcessor(animator, singleSourceClip, path);

                    EditorUtility.DisplayDialog("烘焙成功！", $"單一動畫已完美匯出至：\n{path}", "確定");
                }
                else
                {
                    // 🆕 執行批量自動化掃焙與依賴更新
                    // 利用反射或直接公開列表撈取 Config 內的配置（此處直接藉由獲取 SO 欄位內容）
                    // 為了不更動你的 StateMachineConfigSO 私有封裝，我們透過 serializedObject 來翻箱倒櫃撈資料
                    SerializedObject serializedConfig = new SerializedObject(targetConfigSO);
                    SerializedProperty bakeMappingsProp = serializedConfig.FindProperty("bakeMappings");

                    if (bakeMappingsProp == null || bakeMappingsProp.arraySize == 0)
                    {
                        EditorUtility.DisplayDialog("管線結束", "Config SO 內沒有配置任何動作烘焙資料對照 (Bake Mappings 空虛)！", "確定");
                        return;
                    }

                    int successCount = 0;
                    string dirPath = "Assets/ScriptableObjects/Motion/";

                    for (int i = 0; i < bakeMappingsProp.arraySize; i++)
                    {
                        SerializedProperty mappingElem = bakeMappingsProp.GetArrayElementAtIndex(i);
                        SerializedProperty bakeDataProp = mappingElem.FindPropertyRelative("BakeData");

                        if (bakeDataProp.objectReferenceValue == null) continue;

                        MotionBakeData bakeDataAsset = bakeDataProp.objectReferenceValue as MotionBakeData;
                        if (bakeDataAsset.SourceClip == null) continue;

                        float progress = (float)i / bakeMappingsProp.arraySize;
                        EditorUtility.DisplayProgressBar("工業級批量烘焙管線運行中", $"正在處理動作子節點 ({i + 1}/{bakeMappingsProp.arraySize}): {bakeDataAsset.SourceClip.name}", progress);

                        // 取得檔案原路徑，直接就地覆寫，達成零操作手感的 Dependency Update
                        string assetPath = AssetDatabase.GetAssetPath(bakeDataAsset);

                        // 執行核心烘焙
                        BakeCoreProcessor(animator, bakeDataAsset.SourceClip, assetPath);
                        successCount++;
                    }

                    // 重新刷洗資產資料庫
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    // 💡 工業級細節：強迫狀態機資產自己重新 Initialize 建立最新的 O(1) 記憶體快速查表字典
                    targetConfigSO.Initialize();

                    EditorUtility.DisplayDialog("批量管線建置成功！", $"大功告成！全自動化 Source Discovery 執行完畢。\n成功掃描並就地更新了 {successCount} 個動作特徵資產！", "確定");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<color=red>[BakePipeline] 核心拋出嚴重異常 中斷建置: {ex.Message}</color>");
            }
            finally
            {
                // 清洗記憶體，死守環境安全
                DestroyImmediate(bakeDummy);
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 抽離出來的核心純數據採樣演算法 (完全不包含 IO 介面，只對純資產進行物理重定向拓印)
        /// </summary>
        private void BakeCoreProcessor(Animator animator, AnimationClip clip, string targetAssetPath)
        {
            float duration = clip.length;
            int totalFrames = Mathf.CeilToInt(duration * sampleRate) + 1;
            float interval = 1f / sampleRate;

            AnimationCurve speedCurve = new AnimationCurve();
            AnimationCurve rotationCurve = new AnimationCurve();

            Vector3 lastPos = Vector3.zero;
            Quaternion lastRot = Quaternion.identity;
            float accRotY = 0f;

            animator.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            for (int i = 0; i < totalFrames; i++)
            {
                float time = i * interval;
                if (time > duration) time = duration;

                clip.SampleAnimation(animator.gameObject, time);

                Vector3 currentPos = animator.transform.position;
                Quaternion currentRot = animator.transform.rotation;

                if (i == 0)
                {
                    lastPos = currentPos;
                    lastRot = currentRot;
                    speedCurve.AddKey(0f, 0f);
                    rotationCurve.AddKey(0f, 0f);
                    continue;
                }

                // 1. 水平瞬時物理速度計算 (XZ 平面)
                float dist = Vector3.Distance(new Vector3(currentPos.x, 0f, currentPos.z), new Vector3(lastPos.x, 0f, lastPos.z));
                float currentSpeed = dist / interval;
                speedCurve.AddKey(time, currentSpeed);

                // 2. 連續偏航角度計算 (Yaw Angle Unwrapping)
                Quaternion deltaRot = currentRot * Quaternion.Inverse(lastRot);
                Vector3 rotatedForward = deltaRot * Vector3.forward;
                rotatedForward.y = 0f;

                float deltaYaw = Vector3.SignedAngle(Vector3.forward, rotatedForward.normalized, Vector3.up);
                accRotY += deltaYaw;
                rotationCurve.AddKey(time, accRotY);

                lastPos = currentPos;
                lastRot = currentRot;
            }

            // 資源磁碟硬性落地與反序列化
            string directory = Path.GetDirectoryName(targetAssetPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            MotionBakeData asset = AssetDatabase.LoadAssetAtPath<MotionBakeData>(targetAssetPath);
            bool isNewAsset = false;

            if (asset == null)
            {
                asset = CreateInstance<MotionBakeData>();
                isNewAsset = true;
            }

            if (!isNewAsset) Undo.RecordObject(asset, "Auto Pipeline Soft Update");

            asset.SourceClip = clip;
            asset.SampleRate = sampleRate;
            asset.SpeedCurve = speedCurve;
            asset.RotationCurve = rotationCurve;

            if (isNewAsset)
            {
                AssetDatabase.CreateAsset(asset, targetAssetPath);
            }

            EditorUtility.SetDirty(asset);
        }
    }
}
#endif