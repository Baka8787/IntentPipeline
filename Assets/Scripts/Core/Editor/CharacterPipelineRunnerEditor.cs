#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Core.StateMachine; // 新增狀態機命名空間引用

namespace Project.Core.Pipeline
{
    /// <summary>
    /// 自訂 Inspector，當點擊掛有 CharacterPipelineRunner 的物件時，在 Inspector 顯示即時黑板
    /// </summary>
    [CustomEditor(typeof(CharacterPipelineRunner))]
    public class CharacterPipelineRunnerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 先畫出原本 Runner 的預設 Inspector 欄位
            DrawDefaultInspector();

            CharacterPipelineRunner runner = (CharacterPipelineRunner)target;
            if (runner == null || !Application.isPlaying) return;

            PlayerRuntimeData data = runner.RuntimeData;
            if (data == null) return;

            GUILayout.Space(15);
            EditorGUILayout.LabelField("【黑板數據流即時監視（編輯器版）v0.4】", EditorStyles.boldLabel);

            // 畫一條橫線
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
            GUILayout.Space(5);

            // =================================================================
            // 0. 核心運行狀態與原始輸入 (v0.4 新增)
            // =================================================================
            EditorGUILayout.LabelField("<b>=== 0. 核心狀態與原始輸入 ===</b>", GUILayout.ExpandWidth(true));

            // 用亮色標出當前狀態
            GUI.color = Color.cyan;
            EditorGUILayout.LabelField("  [Current State]", $"<b>{runner.CurrentState.ToString().ToUpper()}</b>");
            GUI.color = Color.white; // 還原顏色

            // 顯示原始輸入 Snapshot (唯讀模式 EditorGUILayout.LabelField)
            var input = runner.InputDebug;
            EditorGUILayout.LabelField("  Raw Move Input", input.MoveInput.ToString());
            EditorGUILayout.LabelField("  Raw Look Input", input.LookInput.ToString());
            EditorGUILayout.LabelField("  Button: Jump Down", input.JumpButtonDown ? "【TRUE】" : "false");
            EditorGUILayout.LabelField("  Button: Roll Down", input.RollButtonDown ? "【TRUE】" : "false");
            EditorGUILayout.LabelField("  Button: Fire Down", input.FireButtonDown ? "【TRUE】" : "false");

            EditorGUILayout.Space();

            // 1. 意圖區
            data.Intent.JumpRequested = EditorGUILayout.Toggle("Intent: Jump", data.Intent.JumpRequested);
            data.Intent.RollRequested = EditorGUILayout.Toggle("Intent: Roll", data.Intent.RollRequested);
            data.Intent.FireRequested = EditorGUILayout.Toggle("Intent: Fire", data.Intent.FireRequested);

            EditorGUILayout.Space();

            // 2. 參數區
            EditorGUILayout.Vector2Field("Move Direction", data.MoveDirection);
            EditorGUILayout.FloatField("Move Speed Magnitude", data.MoveSpeed);
            EditorGUILayout.Slider("Upper Body Weight", data.UpperBodyWeight, 0f, 1f);
            EditorGUILayout.ObjectField("Camera Transform", data.CameraTransform, typeof(Transform), true);

            EditorGUILayout.Space();

            // 3. 引用區
            string weaponName = data.CurrentWeapon != null ? data.CurrentWeapon.GetType().Name : "空手 (Null)";
            EditorGUILayout.LabelField("Current Weapon", weaponName);
            EditorGUILayout.ObjectField("Aim Target", data.AimTarget, typeof(Transform), true);

            EditorGUILayout.Space();

            // 4. 仲裁區
            EditorGUILayout.Toggle("Arbitration: Block Input", data.Arbitration.BlockInput);
            EditorGUILayout.Toggle("Arbitration: Block IK", data.Arbitration.BlockIK);
            EditorGUILayout.Toggle("Arbitration: Block Audio", data.Arbitration.BlockAudio);
            EditorGUILayout.Toggle("Arbitration: Block Expression", data.Arbitration.BlockExpression);

            // 強制重繪 Inspector，達到即時重新整理的動態效果
            Repaint();
        }
    }
}
#endif