using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>非戰鬥行為的模式（`docs/55` §11.4）。</summary>
    public enum EnemyNonCombatMode
    {
        /// <summary>尚未決定（剛脫戰或剛啟用）：下一帧依位置選。</summary>
        None = 0,
        Dwell = 1,
        Roam = 2,
        Patrol = 3,
        Return = 4,
    }

    /// <summary>非戰鬥行為這一帧的結論：要不要走、走去哪。速度與方向不歸它管。</summary>
    internal struct NonCombatDecision
    {
        public bool HasDestination;
        public Vector3 Destination;
        public EnemyNonCombatMode Mode;
    }

    /// <summary>
    /// 🆕（2026-10-13，`docs/55` §11.4，P1–P3）**非戰鬥行為狀態：Dwell／Roam／Patrol／Return。**
    ///
    /// <para><b>只回答「現在該去哪」</b></para>
    /// 不碰 NavMeshAgent（經 <see cref="IEnemyNavigationQuery"/> 問可走性）、不產生方向或速度、
    /// 不讀黑板也不決定交戰——交戰中 <c>AIMovementSource</c> 根本不會呼叫它，只會在交戰時呼叫
    /// <see cref="OnEngaged"/> 讓它下次從頭選。
    ///
    /// <para><b>防抖</b></para>
    /// ①Return 用內外圈（出 Roam 才回家、回到一半半徑內才結束）；②抵達以位置判定、半徑 0.6 m；
    /// ③卡住只在「剛變成卡住」的那一帧算一次失敗；④停留時間是 authored 區間內的亂數。
    /// </summary>
    internal sealed class EnemyNonCombatBehavior
    {
        internal const float ArriveRadius = 0.6f;
        internal const float ReturnInnerFraction = 0.5f;
        internal const float RoamMinimumDistance = 2f;
        internal const int RoamAttempts = 6;
        internal const float RetryDwellSeconds = 1f;
        internal const int MaxReturnStucksBeforeRest = 3;

        private readonly RoamPointPicker _picker;

        private EnemyNonCombatMode _mode;
        private bool _hasDestination;
        private Vector3 _destination;
        private float _dwellUntil;
        private bool _advanceWaypointAfterDwell;
        private int _waypointIndex;
        private int _waypointDirection = 1;
        private int _consecutiveFailures;
        private bool _wasStuck;

        internal EnemyNonCombatBehavior(int seed)
        {
            _picker = new RoamPointPicker(seed);
        }

        internal EnemyNonCombatMode Mode => _mode;
        internal int WaypointIndex => _waypointIndex;

        /// <summary>交戰中每帧呼叫：清掉目的地與停留，脫戰後依當下位置重新選（巡邏從最近點接續）。</summary>
        internal void OnEngaged()
        {
            _mode = EnemyNonCombatMode.None;
            _hasDestination = false;
            _advanceWaypointAfterDwell = false;
            _consecutiveFailures = 0;
            _wasStuck = false;
        }

        internal NonCombatDecision Tick(
            Vector3 self, float now, bool stuck, EnemyTerritory territory, IEnemyNavigationQuery query)
        {
            if (territory == null) return default;

            bool stuckEdge = stuck && !_wasStuck;
            _wasStuck = stuck;
            float distanceFromHome = territory.HorizontalDistanceFromHome(self);

            // Return 優先：出了 Roam 半徑就回家（停留中除外——讓停留跑完再判斷，避免在邊界逐帧切換）。
            if (_mode != EnemyNonCombatMode.Return && _mode != EnemyNonCombatMode.Dwell &&
                distanceFromHome > territory.RoamRadius)
            {
                EnterReturn(self, territory);
            }

            switch (_mode)
            {
                case EnemyNonCombatMode.None:
                    ChooseActivity(self, now, territory, query);
                    break;

                case EnemyNonCombatMode.Return:
                    if (distanceFromHome <= territory.RoamRadius * ReturnInnerFraction || Arrived(self))
                    {
                        _consecutiveFailures = 0;
                        EnterDwell(now, territory, advanceWaypoint: false);
                    }
                    else if (stuckEdge && ++_consecutiveFailures >= MaxReturnStucksBeforeRest)
                    {
                        // 回家的路一直被擋：先停一下再試（⛔ 不傳送——位置只由 MotionDriver 寫）。
                        _consecutiveFailures = 0;
                        EnterDwellFor(now, RetryDwellSeconds, advanceWaypoint: false);
                    }
                    break;

                case EnemyNonCombatMode.Dwell:
                    if (now >= _dwellUntil)
                    {
                        if (distanceFromHome > territory.RoamRadius) EnterReturn(self, territory);
                        else ChooseActivity(self, now, territory, query);
                    }
                    break;

                case EnemyNonCombatMode.Roam:
                    if (Arrived(self)) EnterDwell(now, territory, advanceWaypoint: false);
                    else if (stuckEdge) EnterDwellFor(now, RetryDwellSeconds, advanceWaypoint: false);
                    break;

                case EnemyNonCombatMode.Patrol:
                    if (Arrived(self))
                    {
                        _consecutiveFailures = 0;
                        EnterDwell(now, territory, advanceWaypoint: true);
                    }
                    else if (stuckEdge)
                    {
                        // 這個點走不到：跳過（不無限推牆）。
                        SkipWaypoint(self, now, territory, query);
                    }
                    break;
            }

            return new NonCombatDecision
            {
                Mode = _mode,
                HasDestination = _hasDestination,
                Destination = _destination,
            };
        }

        private void ChooseActivity(Vector3 self, float now, EnemyTerritory territory, IEnemyNavigationQuery query)
        {
            int count = territory.WaypointCount;
            if (count > 0)
            {
                if (_mode == EnemyNonCombatMode.None)
                {
                    // 剛啟用或剛脫戰：從最近的點接續。
                    _waypointIndex = Mathf.Max(0, WaypointCursor.Nearest(territory, self));
                }
                else if (_advanceWaypointAfterDwell)
                {
                    _waypointIndex = WaypointCursor.Advance(
                        _waypointIndex, _waypointDirection, count, territory.WaypointTraversal, out _waypointDirection);
                }
                _advanceWaypointAfterDwell = false;
                TryEnterPatrol(self, now, territory, query, triesLeft: count);
                return;
            }

            if (_picker.TryPick(territory.Home, territory.RoamRadius, self, RoamMinimumDistance, RoamAttempts, query,
                    out Vector3 point))
            {
                _mode = EnemyNonCombatMode.Roam;
                _destination = point;
                _hasDestination = true;
                return;
            }

            // 找不到可完整抵達的點：停一下再試。
            EnterDwellFor(now, RetryDwellSeconds, advanceWaypoint: false);
        }

        private void TryEnterPatrol(
            Vector3 self, float now, EnemyTerritory territory, IEnemyNavigationQuery query, int triesLeft)
        {
            int count = territory.WaypointCount;
            for (int i = 0; i < triesLeft; i++)
            {
                if (territory.TryGetWaypoint(_waypointIndex, out Vector3 waypoint) &&
                    (query == null || query.IsPathComplete(self, waypoint)))
                {
                    _mode = EnemyNonCombatMode.Patrol;
                    _destination = waypoint;
                    _hasDestination = true;
                    return;
                }

                _waypointIndex = WaypointCursor.Advance(
                    _waypointIndex, _waypointDirection, count, territory.WaypointTraversal, out _waypointDirection);
            }

            // 所有點都走不到：停一下再試。
            EnterDwellFor(now, RetryDwellSeconds, advanceWaypoint: false);
        }

        private void SkipWaypoint(Vector3 self, float now, EnemyTerritory territory, IEnemyNavigationQuery query)
        {
            int count = territory.WaypointCount;
            if (++_consecutiveFailures >= Mathf.Max(1, count))
            {
                _consecutiveFailures = 0;
                EnterDwellFor(now, RetryDwellSeconds, advanceWaypoint: true);
                return;
            }

            _waypointIndex = WaypointCursor.Advance(
                _waypointIndex, _waypointDirection, count, territory.WaypointTraversal, out _waypointDirection);
            TryEnterPatrol(self, now, territory, query, triesLeft: count);
        }

        private void EnterReturn(Vector3 self, EnemyTerritory territory)
        {
            _mode = EnemyNonCombatMode.Return;
            _hasDestination = true;
            _destination = territory.Home;
            _consecutiveFailures = 0;
            int nearest = WaypointCursor.Nearest(territory, self);
            if (nearest >= 0 && territory.TryGetWaypoint(nearest, out Vector3 waypoint))
            {
                _destination = waypoint;
                _waypointIndex = nearest;
            }
        }

        private void EnterDwell(float now, EnemyTerritory territory, bool advanceWaypoint)
        {
            EnterDwellFor(now, _picker.NextRange(territory.DwellMinSeconds, territory.DwellMaxSeconds), advanceWaypoint);
        }

        private void EnterDwellFor(float now, float seconds, bool advanceWaypoint)
        {
            _mode = EnemyNonCombatMode.Dwell;
            _hasDestination = false;
            _dwellUntil = now + Mathf.Max(0f, seconds);
            _advanceWaypointAfterDwell = advanceWaypoint;
        }

        private bool Arrived(Vector3 self)
        {
            return _hasDestination && RoamPointPicker.HorizontalDistance(self, _destination) <= ArriveRadius;
        }
    }
}
