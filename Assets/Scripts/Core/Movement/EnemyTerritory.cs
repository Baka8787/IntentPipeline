using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>巡邏點的走法（`docs/55` §11.4）。</summary>
    public enum WaypointTraversal
    {
        /// <summary>A → B → C → A → …</summary>
        Loop = 0,

        /// <summary>A → B → C → B → A → …</summary>
        PingPong = 1,
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.1 N1／N2，P1）**敵人在哪裡活動——純資料。**
    ///
    /// <para><b>家</b></para>
    /// 預設＝出生點（第一次被讀到時記下，通常是 <c>Awake</c>）。不需要在場景擺任何東西（N1）。
    ///
    /// <para><b>三層半徑（都以「離家水平距離」量，`docs/55` §11.3）</b></para>
    /// <list type="bullet">
    /// <item><b>Roam</b>：非戰鬥時活動的範圍；超出就 Return，回到一半半徑內才結束。</item>
    /// <item><b>Soft Leash</b>：沒被打過的追擊追到這裡就放手。</item>
    /// <item><b>Hard Leash</b>：再怎麼被打都不會追過這裡。</item>
    /// </list>
    /// 判斷邏輯不在這裡：leash 屬於 <c>AICombatContextSource</c>（交戰的唯一 owner），
    /// 移動模式屬於 <see cref="EnemyNonCombatBehavior"/>。本元件只提供數字與家的位置。
    ///
    /// <para><b>Waypoints（預留，N1）</b></para>
    /// 留空 ⇒ 在 Roam 半徑內隨機遊走；有指定 ⇒ 依序巡邏。⚠️ waypoint 是場景物件的引用——
    /// 要指定就得在場景（或 prefab instance）上設定。
    /// </summary>
    public sealed class EnemyTerritory : MonoBehaviour
    {
        [Header("Radii（離家水平距離；⚠️ 測試值，非平衡定值）")]
        [SerializeField, Min(0.5f)] private float roamRadius = 6f;
        [SerializeField, Min(0.5f)] private float softLeashRadius = 14f;
        [SerializeField, Min(0.5f)] private float hardLeashRadius = 22f;

        [Header("Re-engage（N3）")]
        [Tooltip("因 leash 脫戰後，這段時間內不得再交戰（受擊也不行）——避免在邊界振盪。")]
        [SerializeField, Min(0f)] private float reengageCooldownSeconds = 2f;

        [Header("Dwell")]
        [SerializeField, Min(0f)] private float dwellMinSeconds = 1.5f;
        [SerializeField, Min(0f)] private float dwellMaxSeconds = 4f;

        [Header("Waypoints（預留：留空＝遊走）")]
        [SerializeField] private Transform[] waypoints = System.Array.Empty<Transform>();
        [SerializeField] private WaypointTraversal waypointTraversal = WaypointTraversal.Loop;

        private bool _hasHome;
        private Vector3 _home;

        private void Awake()
        {
            CaptureHome();
        }

        /// <summary>家（出生點）。EditMode 不呼叫 Awake ⇒ 第一次讀到時記下目前位置。</summary>
        public Vector3 Home
        {
            get
            {
                if (!_hasHome) CaptureHome();
                return _home;
            }
        }

        // 半徑互相夾住：roam ≤ soft ≤ hard，退化設定也不會反向。
        public float RoamRadius => Mathf.Max(0.5f, roamRadius);
        public float SoftLeashRadius => Mathf.Max(RoamRadius, softLeashRadius);
        public float HardLeashRadius => Mathf.Max(SoftLeashRadius, hardLeashRadius);
        public float ReengageCooldownSeconds => Mathf.Max(0f, reengageCooldownSeconds);
        public float DwellMinSeconds => Mathf.Max(0f, dwellMinSeconds);
        public float DwellMaxSeconds => Mathf.Max(DwellMinSeconds, dwellMaxSeconds);
        public WaypointTraversal WaypointTraversal => waypointTraversal;

        /// <summary>有效（非 null）的 waypoint 數量。</summary>
        public int WaypointCount
        {
            get
            {
                int count = 0;
                if (waypoints == null) return 0;
                for (int i = 0; i < waypoints.Length; i++)
                    if (waypoints[i] != null) count++;
                return count;
            }
        }

        /// <summary>第 <paramref name="index"/> 個**有效** waypoint 的位置（跳過 null）。</summary>
        public bool TryGetWaypoint(int index, out Vector3 position)
        {
            position = default;
            if (waypoints == null || index < 0) return false;
            int valid = 0;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                if (valid == index)
                {
                    position = waypoints[i].position;
                    return true;
                }
                valid++;
            }
            return false;
        }

        /// <summary>到家的水平距離。</summary>
        public float HorizontalDistanceFromHome(Vector3 position)
        {
            Vector3 delta = position - Home;
            delta.y = 0f;
            return delta.magnitude;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 選取時畫出家與三層半徑（綠＝Roam、黃＝Soft、紅＝Hard）與 waypoints。⚠️ 只讀不寫（A2）。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? Home : transform.position;
            UnityEditor.Handles.color = new Color(0.4f, 1f, 0.5f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(home, Vector3.up, RoamRadius);
            UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(home, Vector3.up, SoftLeashRadius);
            UnityEditor.Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            UnityEditor.Handles.DrawWireDisc(home, Vector3.up, HardLeashRadius);

            UnityEditor.Handles.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            int count = WaypointCount;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetWaypoint(i, out Vector3 point)) continue;
                UnityEditor.Handles.DrawWireDisc(point, Vector3.up, 0.6f);
                UnityEditor.Handles.Label(point + Vector3.up * 0.3f, $"WP {i}");
                if (TryGetWaypoint((i + 1) % count, out Vector3 next)) UnityEditor.Handles.DrawDottedLine(point, next, 4f);
            }
        }
#endif

        private void CaptureHome()
        {
            _home = transform.position;
            _hasHome = true;
        }

        /// <summary>測試用：指定家與半徑／waypoints，production 走 Inspector 與出生點。</summary>
        internal void ConfigureForTests(
            Vector3 home, float roam, float soft, float hard, float cooldown = 2f,
            float dwellMin = 1.5f, float dwellMax = 4f, Transform[] points = null,
            WaypointTraversal traversal = WaypointTraversal.Loop)
        {
            _home = home;
            _hasHome = true;
            roamRadius = roam;
            softLeashRadius = soft;
            hardLeashRadius = hard;
            reengageCooldownSeconds = cooldown;
            dwellMinSeconds = dwellMin;
            dwellMaxSeconds = dwellMax;
            waypoints = points ?? System.Array.Empty<Transform>();
            waypointTraversal = traversal;
        }
    }
}
