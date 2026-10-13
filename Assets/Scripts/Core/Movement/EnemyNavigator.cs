using System;
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

        [Tooltip("卡住判定的時間窗（秒）：想移動卻在這段時間內前進不到 StuckMinProgress。")]
        [Min(0f)] public float StuckWindowSeconds;

        [Tooltip("卡住判定的最小前進距離（公尺）。")]
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
    internal sealed class EnemyNavigator
    {
        private NavMeshAgent _agent;
        private EnemyNavigationSettings _settings;

        private bool _hasRequest;
        private Vector3 _requestedDestination;
        private float _lastRequestTime;

        private bool _hasProgressAnchor;
        private Vector3 _progressAnchor;
        private float _progressAnchorTime;

        internal EnemyNavigator(EnemyNavigationSettings settings)
        {
            _settings = settings;
        }

        /// <summary>累計路徑請求次數（效能量測用，`docs/55` P0.7）。</summary>
        internal int PathRequestCount { get; private set; }

        /// <summary>同步後 agent 模擬位置與角色實際位置的水平差（診斷；agent 位置會被夾在 NavMesh 上）。</summary>
        internal float LastSyncDivergence { get; private set; }

        /// <summary>最近一次 <see cref="ReportProgress"/> 判定為卡住（只回報，P0 不改任何行為）。</summary>
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
        /// （P0.4）卡住偵測，只回報。<paramref name="wantsToMove"/>＝上一帧是否發布了移動意圖。
        /// </summary>
        internal void ReportProgress(Vector3 position, bool wantsToMove, float now)
        {
            IsStuck = UpdateStuck(ref _hasProgressAnchor, ref _progressAnchor, ref _progressAnchorTime,
                position, wantsToMove, now, _settings);
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

        /// <summary>
        /// 卡住判定：想移動時，以一個錨點計時；前進超過門檻就換錨點；時間窗內沒前進夠 ⇒ 卡住。
        /// 不想移動時清空（停下來不是卡住）。
        /// </summary>
        internal static bool UpdateStuck(
            ref bool hasAnchor,
            ref Vector3 anchor,
            ref float anchorTime,
            Vector3 position,
            bool wantsToMove,
            float now,
            EnemyNavigationSettings settings)
        {
            if (!wantsToMove)
            {
                hasAnchor = false;
                return false;
            }

            Vector3 moved = position - anchor;
            moved.y = 0f;
            float minimum = Mathf.Max(0f, settings.StuckMinProgress);
            if (!hasAnchor || moved.sqrMagnitude >= minimum * minimum)
            {
                hasAnchor = true;
                anchor = position;
                anchorTime = now;
                return false;
            }

            return now - anchorTime >= Mathf.Max(0f, settings.StuckWindowSeconds);
        }
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
