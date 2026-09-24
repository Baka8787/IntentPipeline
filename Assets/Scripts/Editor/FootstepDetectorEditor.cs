using UnityEditor;
using UnityEngine;
using Project.Core.Blackboard;
using Project.Presentation.Footstep;

namespace Project.Editor
{
    /// <summary>
    /// 🆕（Stage 1）`FootstepDetector` 的 debug 面板：把偵測器的環形緩衝攤成可讀的一列列。
    ///
    /// **為什麼是面板而不是 `Debug.Log`**：腳步偵測每帧對雙腳各跑一次，用 log 觀察等於 console spam，
    /// 而且真正要看的是「擊發前後幾帧的數值長什麼樣」——那是一張表，不是一串訊息。
    /// 偵測器只在「有事件」或「抑制原因改變」時寫一筆，因此 64 筆的緩衝足以涵蓋數十步。
    ///
    /// ⚠️ 本面板**只讀**。它不得改動任何偵測狀態，也不得代替 Play 驗收。
    /// </summary>
    [CustomEditor(typeof(FootstepDetector))]
    public sealed class FootstepDetectorEditor : UnityEditor.Editor
    {
        private bool _showTrace = true;
        private Vector2 _scroll;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var detector = (FootstepDetector)target;

            EditorGUILayout.Space();
            _showTrace = EditorGUILayout.Foldout(_showTrace,
                $"Footstep Trace（{detector.TraceCount} 筆）", true);
            if (!_showTrace) return;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("進入 Play Mode 並勾選 Record Debug Trace 後，這裡會顯示每一次擊發與抑制。",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear")) detector.ClearTrace();
                GUILayout.Label("最新在最下方", EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                "time    foot  kind/reason           phase   arm gnd  clear   vY     vXZ    lift",
                EditorStyles.miniBoldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(260f));
            int count = detector.TraceCount;
            for (int i = 0; i < count; i++)
            {
                FootstepDetector.TraceEntry e = detector.GetTrace(i);
                string label = e.Kind != FootstepKind.None ? e.Kind.ToString() : e.Reason.ToString();

                Color previous = GUI.color;
                if (e.Kind == FootstepKind.Normal) GUI.color = new Color(0.5f, 1f, 0.5f);
                else if (e.Kind == FootstepKind.Shuffle) GUI.color = new Color(1f, 0.9f, 0.5f);
                else if (e.Reason == FootstepSuppressReason.StillSwinging ||
                         e.Reason == FootstepSuppressReason.AboveContactBand) GUI.color = new Color(1f, 0.7f, 0.7f);

                EditorGUILayout.LabelField(string.Format(
                    "{0,7:F2}  {1}    {2,-20}  {3,-7} {4}   {5}   {6,6:F3} {7,6:F2} {8,6:F2} {9,6:F3}",
                    e.Time, e.IsRight ? "R" : "L", label, e.Phase,
                    e.Armed ? "A" : "-", e.HasGround ? "G" : "-",
                    e.Clearance, e.VerticalSpeed, e.HorizontalSpeed, e.LiftSinceLastPlant),
                    EditorStyles.miniLabel);

                GUI.color = previous;
            }
            EditorGUILayout.EndScrollView();

            Repaint();
        }
    }
}
