using System;
using Project.Core.Blackboard;
using UnityEngine;
using UnityEngine.AI;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.2，P0）敵人導航的可調參數。全部是導航本身的節流與判定門檻，
    /// 不含任何行為（追不追、去哪）——那些屬於行為元件。
    /// </summary>
    [Serializable]
    public struct EnemyNavigationSettings
    {
        [Tooltip("目的地移動超過這個距離（公尺）才重新請求路徑。")]
        [Min(0f)] public float RepathDistance;

        [Tooltip("兩次路徑請求的最短間隔（秒）。目的地移動或路徑失效都受它限制。")]
        [Min(0f)] public float RepathInterval;

        [Tooltip("PathPartial（目的地不可完整抵達）時，離部分路徑終點小於這個距離就改為直接朝目的地。")]
        [Min(0f)] public float PartialPathArriveDistance;

        [Tooltip("卡住判定的時間窗（秒）——只累積「被允許照意圖移動」的時間（ADR-012）；攻擊、受擊、Traversal、停步不累積。")]
        [Min(0f)] public float StuckWindowSeconds;

        [Tooltip("一個窗內至少要求這麼多水平位移（公尺）才判定；要求太少（幾乎沒想動）不算卡住。")]
        [Min(0f)] public float StuckMinProgress;

        public static EnemyNavigationSettings Default => new EnemyNavigationSettings
        {
            RepathDistance = 0.3f,
            RepathInterval = 0.2f,
            PartialPathArriveDistance = 0.5f,
            StuckWindowSeconds = 0.75f,
            StuckMinProgress = 0.15f,
        };
    }

    /// <summary>路徑跟隨的結論（純函數 <see cref="EnemyNavigator.ResolvePathFollow"/> 的輸出）。</summary>
    internal enum PathFollow
    {
        /// <summary>路徑還在算（通常 1～2 帧）：這一帧不給方向。</summary>
        Wait,

        /// <summary>沿路徑走：方向取下一個轉角點。</summary>
        FollowCorner,

        /// <summary>沒有可用路徑，或已走到部分路徑的盡頭：直接朝目的地。</summary>
        DirectToDestination,
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.2，P0）**敵人的路徑查詢者——只回答「往哪走」。**
    ///
    /// <para><b>職責</b></para>
    /// ①每帧在任何查詢之前把 agent 的模擬位置同步到角色實際位置（包含未交戰）；
    /// ②節流路徑請求；③把路徑狀態翻成「等／沿轉角／直接朝目的地」；④回報是否卡住（只回報）。
    ///
    /// <para><b>⛔ 不做的事</b></para>
    /// 不寫 Transform、不用 <c>Warp</c>、不讓 agent 取得位移或旋轉權（A16 守）；
    /// 不決定目的地（行為元件的事）；不產生速度（producer 的事）。
    /// 位置唯一的出海口仍是 <c>MotionDriver</c>。
    /// </summary>
    internal sealed class EnemyNavigator : IEnemyNavigationQuery, IEnemyPathQuery
    {
        private NavMeshAgent _agent;
        // 唯讀查詢用的路徑容器：建構時配置一次，之後重複使用（零 GC）。
        private readonly NavMeshPath _queryPath = new NavMeshPath();
        // GetCornersNonAlloc 的固定緩衝（零 GC）；超過 32 個轉角時取到的「終點」是第 32 個——只用在不可達判定，偏保守。
        private readonly Vector3[] _corners = new Vector3[32];
        private EnemyNavigationSettings _settings;

        private bool _hasRequest;
        private Vector3 _requestedDestination;
        private float _lastRequestTime;

        // 🔄（2026-10-14，ADR-012）卡住判定改讀 MotionDriver 發布的執行事實；只在「被允許照意圖移動」的帧累積。
        private StuckWindow _stuckWindow;

        internal EnemyNavigator(EnemyNavigationSettings settings)
        {
            _settings = settings;
        }

        /// <summary>累計路徑請求次數（效能量測用，`docs/55` P0.7）。</summary>
        internal int PathRequestCount { get; private set; }

        /// <summary>同步後 agent 模擬位置與角色實際位置的水平差（診斷；agent 位置會被夾在 NavMesh 上）。</summary>
        internal float LastSyncDivergence { get; private set; }

        /// <summary>
        /// 最近一次 <see cref="ReportExecution"/> 的判定：**有移動請求、實際被允許移動、但沒有有效進展**（ADR-012 D5）。
        /// 只回報——任何「卡住之後做什麼」都屬於行為元件（ADR-012 D3）。
        /// </summary>
        internal bool IsStuck { get; private set; }

        internal bool IsOnNavMesh => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

        /// <summary>每帧呼叫：agent 可能被測試或執行期替換，綁定是冪等的。</summary>
        internal void Bind(NavMeshAgent agent, EnemyNavigationSettings settings)
        {
            if (!ReferenceEquals(_agent, agent))
            {
                _agent = agent;
                _hasRequest = false;
            }
            _settings = settings;
        }

        /// <summary>
        /// （P0.1）把 agent 的模擬位置拉到角色實際位置。**每帧、在任何查詢之前**——
        /// 舊版只在交戰分支同步，未交戰時 agent 會落後 Transform，路徑從舊位置算。
        /// </summary>
        internal void Sync(Vector3 position)
        {
            if (!IsOnNavMesh)
            {
                LastSyncDivergence = 0f;
                return;
            }
            _agent.nextPosition = position;
            Vector3 delta = _agent.nextPosition - position;
            delta.y = 0f;
            LastSyncDivergence = delta.magnitude;
        }

        /// <summary>（P0.2）請求一條通往 <paramref name="destination"/> 的路徑；是否真的送出由節流規則決定。</summary>
        internal void RequestDestination(Vector3 destination, float now)
        {
            if (!IsOnNavMesh) return;

            bool pathInvalid = !_agent.pathPending &&
                               (!_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid);
            if (!ShouldRequestPath(_hasRequest, _requestedDestination, destination, _lastRequestTime, now,
                    pathInvalid, _settings))
            {
                return;
            }

            _agent.SetDestination(destination);
            _hasRequest = true;
            _requestedDestination = destination;
            _lastRequestTime = now;
            PathRequestCount++;
        }

        /// <summary>
        /// 這一帧該朝哪個點走。回傳 false ＝ 路徑還在算，這一帧不給方向（與舊行為相同）。
        /// 沒有路徑、路徑無效、或已走到部分路徑盡頭 ⇒ 回傳 <paramref name="destination"/> 本身。
        /// </summary>
        internal bool TryResolveSteeringPoint(Vector3 self, Vector3 destination, out Vector3 point)
        {
            point = destination;
            if (!IsOnNavMesh) return true;

            Vector3 toPathEnd = _agent.pathEndPosition - self;
            toPathEnd.y = 0f;
            PathFollow follow = ResolvePathFollow(
                _agent.pathPending,
                _agent.hasPath,
                _agent.pathStatus,
                toPathEnd.magnitude,
                _settings);

            switch (follow)
            {
                case PathFollow.Wait:
                    return false;
                case PathFollow.FollowCorner:
                    point = _agent.steeringTarget;
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 🔄（2026-10-14，ADR-012）卡住偵測，只回報。<paramref name="report"/>＝黑板上的 <c>MotionExecution</c>
        /// （上一帧 LateUpdate 寫入）；<paramref name="currentFrame"/>＝本帧 <c>Time.frameCount</c>。
        /// 報告不是上一帧的 ⇒ 上一帧沒有執行基礎移動（攻擊、受擊、Traversal、停步、時間凍結）⇒ 不累積、不回報卡住。
        /// </summary>
        internal void ReportExecution(in MotionExecutionData report, int currentFrame, float deltaTime)
        {
            bool executedLastFrame = report.DescribesFrame(currentFrame - 1);
            IsStuck = UpdateStuck(ref _stuckWindow, executedLastFrame, report.RequestedHorizontalDistance,
                report.AchievedHorizontalDistance, deltaTime, _settings);
        }

        /// <summary>
        /// 🆕（P5a，`docs/56` §12.3）目前 agent 路徑的唯讀快照（不在計算中才有效）。站位元件用它判斷「通往目標的路徑是否只到得了一部分」。
        /// </summary>
        internal EnemyPathSnapshot SnapshotPath()
        {
            if (!IsOnNavMesh || !_hasRequest || _agent.pathPending || !_agent.hasPath) return default;
            return new EnemyPathSnapshot
            {
                Valid = true,
                Destination = _requestedDestination,
                Partial = _agent.pathStatus == NavMeshPathStatus.PathPartial,
                End = _agent.pathEndPosition,
            };
        }

        // =====================================================================
        // IEnemyNavigationQuery（給行為／目的地選擇的唯讀查詢；不碰 agent 的路徑）
        // =====================================================================

        bool IEnemyNavigationQuery.TrySampleWalkablePoint(Vector3 point, float maxDistance, out Vector3 walkable)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, Mathf.Max(0.01f, maxDistance), NavMesh.AllAreas))
            {
                walkable = hit.position;
                return true;
            }
            walkable = default;
            return false;
        }

        bool IEnemyPathQuery.TryCalculatePathEnd(Vector3 from, Vector3 to, float targetSampleRadius, out Vector3 end, out bool complete)
        {
            end = default;
            complete = false;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit goal, Mathf.Max(0.01f, targetSampleRadius), NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, _queryPath)) return false;
            if (_queryPath.status == NavMeshPathStatus.PathInvalid) return false;
            int count = _queryPath.GetCornersNonAlloc(_corners);
            if (count <= 0) return false;
            end = _corners[Mathf.Min(count, _corners.Length) - 1];
            complete = _queryPath.status == NavMeshPathStatus.PathComplete;
            return true;
        }

        bool IEnemyPathQuery.TrySampleWalkable(Vector3 point, float radius, out Vector3 walkable)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, Mathf.Max(0.01f, radius), NavMesh.AllAreas))
            {
                walkable = hit.position;
                return true;
            }
            walkable = default;
            return false;
        }

        bool IEnemyPathQuery.IsStraightLineClear(Vector3 from, Vector3 to) =>
            !NavMesh.Raycast(from, to, out _, NavMesh.AllAreas);

        float IEnemyPathQuery.DistanceToEdge(Vector3 walkable) =>
            NavMesh.FindClosestEdge(walkable, out NavMeshHit edge, NavMesh.AllAreas) ? edge.distance : -1f;

        bool IEnemyPathQuery.TryPathLength(Vector3 from, Vector3 to, out float length)
        {
            length = 0f;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 1f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(start.position, to, NavMesh.AllAreas, _queryPath)) return false;
            if (_queryPath.status != NavMeshPathStatus.PathComplete) return false;
            int count = Mathf.Min(_queryPath.GetCornersNonAlloc(_corners), _corners.Length);
            for (int i = 1; i < count; i++) length += Vector3.Distance(_corners[i - 1], _corners[i]);
            return count > 0;
        }

        bool IEnemyNavigationQuery.IsPathComplete(Vector3 from, Vector3 to)
        {
            const float EndpointSampleRadius = 1f;
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, EndpointSampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, EndpointSampleRadius, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _queryPath) &&
                   _queryPath.status == NavMeshPathStatus.PathComplete;
        }

        // =====================================================================
        // 純函數（EditMode 可測，不需要 NavMesh）
        // =====================================================================

        /// <summary>
        /// 路徑請求節流。第一次一定請求；之後只有「目的地移動超過 RepathDistance」或「路徑失效」，
        /// 而且距上次請求至少 RepathInterval 才請求。目的地不動、路徑有效 ⇒ 永遠不重複請求。
        /// </summary>
        internal static bool ShouldRequestPath(
            bool hasRequest,
            Vector3 lastDestination,
            Vector3 destination,
            float lastRequestTime,
            float now,
            bool pathInvalid,
            EnemyNavigationSettings settings)
        {
            if (!hasRequest) return true;
            if (now - lastRequestTime < Mathf.Max(0f, settings.RepathInterval)) return false;
            if (pathInvalid) return true;
            float threshold = Mathf.Max(0f, settings.RepathDistance);
            return (destination - lastDestination).sqrMagnitude > threshold * threshold;
        }

        /// <summary>
        /// 路徑狀態判讀。與舊行為一致的部分：還在算 ⇒ 等；沒有路徑或 PathInvalid ⇒ 直接朝目的地。
        /// 🆕（P0.3，`docs/55` G4）PathPartial 且已到部分路徑盡頭附近 ⇒ 直接朝目的地，
        /// 不在部分終點原地停住。
        /// </summary>
        internal static PathFollow ResolvePathFollow(
            bool pathPending,
            bool hasPath,
            NavMeshPathStatus status,
            float distanceToPathEnd,
            EnemyNavigationSettings settings)
        {
            if (pathPending) return PathFollow.Wait;
            if (!hasPath || status == NavMeshPathStatus.PathInvalid) return PathFollow.DirectToDestination;
            if (status == NavMeshPathStatus.PathPartial &&
                distanceToPathEnd <= Mathf.Max(0f, settings.PartialPathArriveDistance))
            {
                return PathFollow.DirectToDestination;
            }
            return PathFollow.FollowCorner;
        }

        /// <summary>每帧要求位移低於此值視為沒有請求（停下來、減速尾端）——被允許移動時重置窗，不算卡住。</summary>
        internal const float NegligibleRequestPerFrame = 1e-4f;

        /// <summary>窗內實際位移低於「要求 × 此比例」即為沒有有效進展。</summary>
        internal const float StuckProgressRatio = 0.25f;

        /// <summary>
        /// 🔄（2026-10-14，ADR-012 D5）卡住判定（純函數）。「有移動請求、實際被允許移動、但沒有有效進展」：
        /// <list type="bullet">
        /// <item>上一帧沒有執行基礎移動 ⇒ 不累積、窗保留（攻擊／受擊結束後接著算）、本帧不回報卡住</item>
        /// <item>被允許移動但要求可忽略 ⇒ 重置窗、不卡住（停下來不是卡住）</item>
        /// <item>否則累積被允許移動的時間與要求／實際位移；窗滿時：要求 ≥ <c>StuckMinProgress</c> ∧ 實際 &lt; 要求 × <see cref="StuckProgressRatio"/> ⇒ 卡住；
        ///       結論保持到下一個窗</item>
        /// </list>
        /// </summary>
        internal static bool UpdateStuck(
            ref StuckWindow window,
            bool executedLastFrame,
            float requested,
            float achieved,
            float deltaTime,
            EnemyNavigationSettings settings)
        {
            if (!executedLastFrame) return false;
            if (requested < NegligibleRequestPerFrame)
            {
                window = default;
                return false;
            }

            window.ExecutedSeconds += Mathf.Max(0f, deltaTime);
            window.Requested += requested;
            window.Achieved += Mathf.Max(0f, achieved);
            if (window.ExecutedSeconds >= Mathf.Max(0f, settings.StuckWindowSeconds))
            {
                window.Stuck = window.Requested >= Mathf.Max(0f, settings.StuckMinProgress)
                               && window.Achieved < StuckProgressRatio * window.Requested;
                window.ExecutedSeconds = 0f;
                window.Requested = 0f;
                window.Achieved = 0f;
            }
            return window.Stuck;
        }
    }

    /// <summary>🆕（2026-10-14，ADR-012）卡住判定的累積窗（只累積被允許移動的帧）。</summary>
    internal struct StuckWindow
    {
        public float ExecutedSeconds;
        public float Requested;
        public float Achieved;
        public bool Stuck;
    }

    /// <summary>🆕（P5a）agent 目前路徑的唯讀快照。<see cref="Valid"/>＝false 表示沒有可讀的路徑（計算中、離網、沒請求過）。</summary>
    internal struct EnemyPathSnapshot
    {
        public bool Valid;
        public Vector3 Destination;
        public bool Partial;
        public Vector3 End;
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.2，P0）**方向的最後一步。** 轉角點 → 水平單位方向。
    /// 未來多敵人 separation（N6）從這裡接入，不改 Navigation 也不改 producer。
    /// </summary>
    internal static class EnemySteering
    {
        internal const float MinimumSqrMagnitude = 0.0001f;

        /// <summary>從 <paramref name="self"/> 朝 <paramref name="point"/> 的水平單位方向；退化時回 false。</summary>
        internal static bool TryResolveDirection(Vector3 self, Vector3 point, out Vector3 direction)
        {
            direction = point - self;
            direction.y = 0f;
            if (direction.sqrMagnitude <= MinimumSqrMagnitude)
            {
                direction = Vector3.zero;
                return false;
            }
            direction.Normalize();
            return true;
        }
    }
}
