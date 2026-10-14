using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/56` §12.3，P5a）站位需要的唯讀路徑查詢。讓站位元件不直接碰 <c>UnityEngine.AI</c>
    /// （EditMode 沒有 NavMesh，規則仍要能完整測試）。正式實作是 <see cref="EnemyNavigator"/>。
    /// </summary>
    internal interface IEnemyPathQuery
    {
        /// <summary>
        /// 以同步查詢算 <paramref name="from"/> → <paramref name="to"/> 的路徑；<paramref name="end"/>＝路徑終點
        /// （不完整時就是「最接近 <paramref name="to"/> 的可達點」）。兩端取樣不到或沒有路徑 ⇒ false。
        /// </summary>
        bool TryCalculatePathEnd(Vector3 from, Vector3 to, float targetSampleRadius, out Vector3 end, out bool complete);
    }

    internal enum TacticalSituation
    {
        None,

        /// <summary>目標不可達：通往目標的路徑只到得了一部分，而且最接近的點仍在攻擊距離外。</summary>
        Unreachable,
    }

    /// <summary>站位元件的輸出：一個目的地點（或「已抵達、待命」）。⛔ 不含方向或速度——那些由導航與 producer 決定。</summary>
    internal struct TacticalGoal
    {
        public bool Active;
        public TacticalSituation Situation;
        public Vector3 Point;
        public bool Arrived;
    }

    /// <summary>
    /// 🆕（2026-10-14，`docs/56` D1／§12，P5a）**戰術站位**——只回答「該站在哪」。P5a 只處理**目標不可達**（A1）：
    /// 通往目標的路徑只到得了一部分 ⇒ 停在最接近目標的可達點待命，不再推牆。
    ///
    /// <para><b>邊界</b>（`docs/56` §5.9、§12.1）</para>
    /// 目標只以**值**傳入（<c>CombatContext.TargetPosition</c>），本類別不認識 Transform、不讀黑板、不寫 <c>MovementIntent</c>、
    /// 不碰 <c>NavMeshAgent</c>（A54／A16 的精神；架構測試守）。不決定攻擊（D5 未裁決）、不改 Strafe／Retreat（P5b）。
    /// </summary>
    internal sealed class EnemyTacticalPositioning
    {
        internal const float UnreachableEnterMargin = 0.25f;
        internal const float ArriveEnterRadius = 0.35f;
        internal const float ArriveExitRadius = 0.7f;
        internal const float TargetMovedRecheckDistance = 1.5f;
        internal const float GoalShiftThreshold = 1.0f;
        internal const float ExitCheckInterval = 0.5f;
        internal const float PathDestinationMatchDistance = 1.0f;
        internal const float TargetSampleRadius = 2.5f;

        private bool _active;
        private Vector3 _goal;
        private Vector3 _targetWhenChosen;
        private bool _arrived;
        private float _nextCheckTime;

        internal TacticalSituation Situation => _active ? TacticalSituation.Unreachable : TacticalSituation.None;

        /// <summary>脫戰、目標死亡、自己死亡時清除（D6）。</summary>
        internal void Reset()
        {
            _active = false;
            _arrived = false;
        }

        /// <param name="agentPath">agent 目前路徑的快照（進入判定只讀它，不額外查詢——一般打鬥零額外成本）。</param>
        /// <param name="reach">攻擊有效距離（W10：＝ <c>maximumEngagementDistance</c>）。</param>
        internal TacticalGoal Tick(Vector3 self, Vector3 target, float reach, float now, in EnemyPathSnapshot agentPath,
                                   IEnemyPathQuery query)
        {
            if (!_active)
            {
                if (!IsUnreachable(agentPath, target, reach)) return default;
                _active = true;
                _goal = agentPath.End;
                _targetWhenChosen = target;
                _arrived = false;
                _nextCheckTime = now + ExitCheckInterval;
            }
            else if (now >= _nextCheckTime)
            {
                _nextCheckTime = now + ExitCheckInterval;
                if (query != null && query.TryCalculatePathEnd(self, target, TargetSampleRadius, out Vector3 end, out bool complete))
                {
                    if (complete || HorizontalDistance(end, target) <= reach)
                    {
                        Reset();   // 目標回到可達範圍 ⇒ 交還現行行為
                        return default;
                    }
                    if (HorizontalDistance(target, _targetWhenChosen) > TargetMovedRecheckDistance)
                    {
                        _targetWhenChosen = target;
                        if (HorizontalDistance(end, _goal) > GoalShiftThreshold)
                        {
                            _goal = end;
                            _arrived = false;
                        }
                    }
                }
                // 查詢失敗（取樣不到、沒有路徑）：無法證明目標已可達 ⇒ 維持待命（不推牆是安全方向）。
            }

            float d = HorizontalDistance(self, _goal);
            if (_arrived) _arrived = d <= ArriveExitRadius;
            else _arrived = d <= ArriveEnterRadius;

            return new TacticalGoal { Active = true, Situation = TacticalSituation.Unreachable, Point = _goal, Arrived = _arrived };
        }

        /// <summary>
        /// 進入條件（純函數）：agent 這條路徑是通往目標的（請求的目的地離目標夠近）、只到得了一部分、
        /// 而且部分路徑的終點離目標仍超過攻擊距離＋邊際。終點在攻擊距離內 ⇒ 不算不可達（例如玩家貼牆、位置在 NavMesh 侵蝕帶外）。
        /// </summary>
        internal static bool IsUnreachable(in EnemyPathSnapshot agentPath, Vector3 target, float reach)
        {
            return agentPath.Valid
                   && agentPath.Partial
                   && HorizontalDistance(agentPath.Destination, target) <= PathDestinationMatchDistance
                   && HorizontalDistance(agentPath.End, target) > reach + UnreachableEnterMargin;
        }

        internal static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
