using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// 🆕（2026-10-14，`docs/56` §12.3／§13）站位需要的唯讀 NavMesh 查詢。讓站位元件不直接碰 <c>UnityEngine.AI</c>
    /// （EditMode 沒有 NavMesh，規則仍要能完整測試）。正式實作是 <see cref="EnemyNavigator"/>。
    /// </summary>
    internal interface IEnemyPathQuery
    {
        /// <summary>
        /// 以同步查詢算 <paramref name="from"/> → <paramref name="to"/> 的路徑；<paramref name="end"/>＝路徑終點
        /// （不完整時就是「最接近 <paramref name="to"/> 的可達點」）。兩端取樣不到或沒有路徑 ⇒ false。
        /// </summary>
        bool TryCalculatePathEnd(Vector3 from, Vector3 to, float targetSampleRadius, out Vector3 end, out bool complete);

        /// <summary>離 <paramref name="point"/> 最近、在 <paramref name="radius"/> 內的可走點。</summary>
        bool TrySampleWalkable(Vector3 point, float radius, out Vector3 walkable);

        /// <summary>兩個（已在 NavMesh 上的）點之間的直線是否不跨 NavMesh 邊界。</summary>
        bool IsStraightLineClear(Vector3 from, Vector3 to);

        /// <summary>可走點到最近 NavMesh 邊緣的水平距離；查不到 ⇒ 負值。</summary>
        float DistanceToEdge(Vector3 walkable);

        /// <summary>完整路徑的長度（轉角折線總長）；沒有完整路徑 ⇒ false。</summary>
        bool TryPathLength(Vector3 from, Vector3 to, out float length);
    }

    internal enum TacticalSituation
    {
        None,

        /// <summary>目標不可達：通往目標的路徑只到得了一部分，而且最接近的點仍在攻擊距離外（P5a，§12.3）。</summary>
        Unreachable,

        /// <summary>在站位區內，承諾一個以目標為圓心的站位槽（P5b，§13）。</summary>
        Slot,
    }

    /// <summary>站位元件的輸出：一個目的地點（或「已抵達、待命」）。⛔ 不含方向或速度——那些由導航與 producer 決定。</summary>
    internal struct TacticalGoal
    {
        public bool Active;
        public TacticalSituation Situation;
        public Vector3 Point;
        public bool Arrived;
        /// <summary>true ＝ <see cref="Point"/> 與自己之間直線可走（不必請求路徑）。</summary>
        public bool DirectLine;
    }

    /// <summary>
    /// 🆕（2026-10-14，`docs/56` D1）**戰術站位**——只回答「該站在哪」。
    /// <list type="bullet">
    /// <item><b>A1（P5a，§12.3）</b>：通往目標的路徑只到得了一部分 ⇒ 停在最接近目標的可達點待命，不推牆。</item>
    /// <item><b>站位槽（P5b，§13）</b>：在站位區內承諾一個以目標為圓心、半徑＝交戰帶中點的方位角；
    ///       只在槽**失效**（不可走、隔牆、貼牆、卡住）時換；到位後待命，不為了「看起來在動」而移動（D3／D6）。</item>
    /// </list>
    /// <para><b>邊界</b>（§5.9）：目標只以**值**傳入；本類別不認識 Transform、不讀黑板、不寫意圖、不碰 agent、
    /// 不決定攻擊（D5 未裁決）。A56 守。</para>
    /// </summary>
    internal sealed class EnemyTacticalPositioning
    {
        // ── A1 ───────────────────────────────────────────────────────────────────────────────────
        internal const float UnreachableEnterMargin = 0.25f;
        internal const float ArriveEnterRadius = 0.35f;
        internal const float ArriveExitRadius = 0.7f;
        internal const float TargetMovedRecheckDistance = 1.5f;
        internal const float GoalShiftThreshold = 1.0f;
        internal const float ExitCheckInterval = 0.5f;
        internal const float PathDestinationMatchDistance = 1.0f;
        internal const float TargetSampleRadius = 2.5f;

        // ── 站位槽（§13.1；數值是 Living Spec 的初始值）──────────────────────────────────────────────
        internal const float ZoneEnterMargin = 1.0f;
        internal const float ZoneExitMargin = 2.0f;
        internal const float SlotSampleRadius = 0.5f;
        internal const float SelfSampleRadius = 1.0f;
        internal const float ValidityCheckInterval = 0.2f;
        internal const float MinEdgeClearance = 0.3f;
        internal const float ClearanceTarget = 1.0f;
        internal const float TravelNormalizer = 8f;
        internal const float AngleWeight = 0.5f;
        internal const float ClearanceWeight = 0.8f;
        internal const float AbandonPenalty = 0.3f;
        internal const float AbandonPenaltySeconds = 3f;
        internal const float AbandonBearingTolerance = 20f * Mathf.Deg2Rad;
        internal const float DetourAngle = 60f * Mathf.Deg2Rad;
        internal const float DetourStep = 45f * Mathf.Deg2Rad;
        /// <summary>
        /// §13.4：站位槽只用在**目標（幾乎）靜止**時。目標移動中，承諾一個世界方位的槽會讓敵人一路跟著平移、落在攻擊距離外
        /// （MovingSlow 實測：攻擊有效時間 52% → 27%）；這時交還現行的距離帶行為（直接控制到目標的距離）。
        /// 進入「移動中」：速度 &gt; <see cref="MovingTargetEnterSpeed"/>；回到「靜止」：速度 &lt; <see cref="MovingTargetExitSpeed"/>
        /// 持續 <see cref="TargetStillSeconds"/>（遲滯，避免來回切換）。
        /// </summary>
        internal const float MovingTargetEnterSpeed = 0.5f;
        internal const float MovingTargetExitSpeed = 0.3f;
        internal const float TargetStillSeconds = 0.5f;
        /// <summary>目標速度估計的平滑係數（指數移動平均）。</summary>
        internal const float TargetVelocitySmoothing = 0.3f;
        /// <summary>待命時到目標的距離必須在交戰帶內、且離攻擊距離至少這麼多（否則「到位」也打不到——§13.3）。</summary>
        internal const float HoldReachMargin = 0.05f;
        /// <summary>選槽後這段時間內不把「卡住」當成槽失效：卡住的結論會保留一個判定窗，剛換槽時看到的還是換槽前的結論。</summary>
        internal const float StuckGraceAfterSelect = 1.0f;
        internal static readonly float[] CandidateOffsetsDegrees = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f };

        // A1 狀態
        private bool _unreachable;
        private Vector3 _holdPoint;
        private Vector3 _targetWhenChosen;
        private float _nextExitCheck;

        // 站位槽狀態
        private bool _inZone;
        private bool _hasSlot;
        private float _slotBearing;
        private float _slotRadius;
        private float _nextValidityCheck;
        private bool _hasAbandoned;
        private float _abandonedBearing;
        private float _abandonedUntil;
        private float _slotSelectedAt;

        // 目標速度估計（只用位置的值；不認識目標是誰）
        private bool _hasTargetSample;
        private Vector3 _lastTarget;
        private float _lastTargetTime;
        private Vector3 _targetVelocity;
        private bool _targetMoving;
        private float _targetSlowSince;

        private bool _arrived;

        /// <summary>測試／量測用：關閉站位槽（只留 A1）＝ P5a 的行為，用於同一個 build 內的 A/B（§13.3）。</summary>
        internal bool SlotPositioningEnabled { get; set; } = true;

        /// <summary>累計選槽次數（診斷／量測：換位是否頻繁）。</summary>
        internal int SlotChanges { get; private set; }

        /// <summary>目前承諾的槽點（診斷／測試用；沒有槽 ⇒ false）。注意 <see cref="TacticalGoal.Point"/> 是「下一個 steering 點」，可能是繞行中繼點。</summary>
        internal bool TryGetSlotPoint(Vector3 target, out Vector3 point)
        {
            point = _hasSlot ? SlotPoint(target, _slotBearing, _slotRadius) : default;
            return _hasSlot;
        }

        /// <summary>目前是否判定目標在移動（站位槽暫停）。</summary>
        internal bool TargetMoving => _targetMoving;

        internal TacticalSituation Situation =>
            _unreachable ? TacticalSituation.Unreachable : (_hasSlot ? TacticalSituation.Slot : TacticalSituation.None);

        /// <summary>脫戰、目標死亡、自己死亡時清除（D6）。</summary>
        internal void Reset()
        {
            _hasTargetSample = false;
            _targetVelocity = Vector3.zero;
            _targetMoving = false;
            _unreachable = false;
            _inZone = false;
            _hasSlot = false;
            _arrived = false;
        }

        /// <param name="bandMin">交戰帶下界（太近）。</param>
        /// <param name="reach">攻擊有效距離（W10：＝ 交戰帶上界）。</param>
        /// <param name="agentPath">agent 目前路徑的快照（A1 進入判定只讀它——一般打鬥零額外成本）。</param>
        /// <param name="stuck">導航回報的卡住（ADR-012；只用來判定目前的槽失效，E5）。</param>
        internal TacticalGoal Tick(Vector3 self, Vector3 target, float bandMin, float reach, float now,
                                   in EnemyPathSnapshot agentPath, IEnemyPathQuery query, bool stuck = false)
        {
            // ── A1：不可達優先 ─────────────────────────────────────────────────────────────────
            if (_unreachable || IsUnreachable(agentPath, target, reach))
            {
                TacticalGoal hold = TickUnreachable(self, target, reach, now, agentPath, query);
                if (hold.Active)
                {
                    _inZone = false;
                    _hasSlot = false;
                    return hold;
                }
            }

            // ── 站位區（H6）────────────────────────────────────────────────────────────────────
            if (!SlotPositioningEnabled)
            {
                _inZone = false;
                _hasSlot = false;
                return default;
            }
            float distance = HorizontalDistance(self, target);
            UpdateTargetVelocity(target, now);
            if (!_inZone)
            {
                if (distance > reach + ZoneEnterMargin) return default;
                _inZone = true;
                _hasSlot = false;
            }
            else if (distance > reach + ZoneExitMargin)
            {
                _inZone = false;
                _hasSlot = false;
                return default;
            }
            if (query == null) return default;

            // ── 目標在移動 ⇒ 不用站位槽，交還距離帶行為（§13.4）────────────────────────────────────────────
            if (_targetMoving)
            {
                _hasSlot = false;
                _arrived = false;
                return default;
            }

            // ── 承諾：只在失效時換（D6）────────────────────────────────────────────────────────────
            bool stuckCounts = stuck && now - _slotSelectedAt >= StuckGraceAfterSelect;
            if (_hasSlot && (stuckCounts || now >= _nextValidityCheck))
            {
                _nextValidityCheck = now + ValidityCheckInterval;
                if (stuckCounts || !IsSlotValid(SlotPoint(target, _slotBearing, _slotRadius), target, bandMin, reach, query, out _, out _))
                {
                    _hasAbandoned = true;
                    _abandonedBearing = _slotBearing;
                    _abandonedUntil = now + AbandonPenaltySeconds;
                    _hasSlot = false;
                }
            }
            if (!_hasSlot)
            {
                if (!SelectSlot(self, target, bandMin, reach, now, query, out _slotBearing)) return default;   // 沒有有效槽 ⇒ 現行行為
                _slotRadius = IdealRadius(bandMin, reach);
                _hasSlot = true;
                _arrived = false;
                _nextValidityCheck = now + ValidityCheckInterval;
                _slotSelectedAt = now;
                SlotChanges++;
            }

            // ── 到位與走法（D3）────────────────────────────────────────────────────────────────────
            Vector3 slotPoint = SlotPoint(target, _slotBearing, _slotRadius);
            float toSlot = HorizontalDistance(self, slotPoint);
            // 🔄（§13.3）「到位」還要求**從這裡打得到**：到目標的距離在 [bandMin, reach − 邊際]。
            // 否則抵達容差（0.35／0.7 m）比「理想半徑到攻擊距離」的餘裕（Y Bot：1.425 → 1.6 只有 0.175 m）還寬，
            // 會在攻擊距離外「到位」待命、永遠不出手（2026-10-14 實測：被打斷後停在 1.75 m，20 s 內 0 次出拳）。
            bool attackable = distance >= bandMin && distance <= reach - HoldReachMargin;
            _arrived = attackable && (_arrived ? toSlot <= ArriveExitRadius : toSlot <= ArriveEnterRadius);
            if (_arrived)
                return new TacticalGoal { Active = true, Situation = TacticalSituation.Slot, Point = slotPoint, Arrived = true };

            Vector3 steer = ResolveApproachPoint(self, target, _slotBearing, _slotRadius);
            // 繞行中繼點本身必須可走、離邊緣夠遠（牆角裡的中繼點常在牆內）；不行就直接朝槽點（必要時沿路徑）。
            if ((steer - slotPoint).sqrMagnitude > 1e-6f
                && !(query.TrySampleWalkable(steer, SlotSampleRadius, out Vector3 detourOnMesh)
                     && query.DistanceToEdge(detourOnMesh) >= MinEdgeClearance))
            {
                steer = slotPoint;
            }
            bool direct = query.TrySampleWalkable(self, SelfSampleRadius, out Vector3 selfOnMesh)
                          && query.TrySampleWalkable(steer, SlotSampleRadius, out Vector3 steerOnMesh)
                          && query.IsStraightLineClear(selfOnMesh, steerOnMesh);
            return new TacticalGoal { Active = true, Situation = TacticalSituation.Slot, Point = steer, DirectLine = direct };
        }

        private TacticalGoal TickUnreachable(Vector3 self, Vector3 target, float reach, float now,
                                             in EnemyPathSnapshot agentPath, IEnemyPathQuery query)
        {
            if (!_unreachable)
            {
                _unreachable = true;
                _holdPoint = agentPath.End;
                _targetWhenChosen = target;
                _arrived = false;
                _nextExitCheck = now + ExitCheckInterval;
            }
            else if (now >= _nextExitCheck)
            {
                _nextExitCheck = now + ExitCheckInterval;
                if (query != null && query.TryCalculatePathEnd(self, target, TargetSampleRadius, out Vector3 end, out bool complete))
                {
                    if (complete || HorizontalDistance(end, target) <= reach)
                    {
                        _unreachable = false;   // 目標回到可達範圍 ⇒ 交還站位／現行行為
                        _arrived = false;
                        return default;
                    }
                    if (HorizontalDistance(target, _targetWhenChosen) > TargetMovedRecheckDistance)
                    {
                        _targetWhenChosen = target;
                        if (HorizontalDistance(end, _holdPoint) > GoalShiftThreshold)
                        {
                            _holdPoint = end;
                            _arrived = false;
                        }
                    }
                }
                // 查詢失敗：無法證明目標已可達 ⇒ 維持待命（不推牆是安全方向）。
            }

            float d = HorizontalDistance(self, _holdPoint);
            _arrived = _arrived ? d <= ArriveExitRadius : d <= ArriveEnterRadius;
            return new TacticalGoal { Active = true, Situation = TacticalSituation.Unreachable, Point = _holdPoint, Arrived = _arrived };
        }

        /// <summary>目前估計的目標水平速度（m/s）。</summary>
        internal float TargetSpeed => new Vector3(_targetVelocity.x, 0f, _targetVelocity.z).magnitude;

        private void UpdateTargetVelocity(Vector3 target, float now)
        {
            if (_hasTargetSample && now > _lastTargetTime)
            {
                Vector3 v = (target - _lastTarget) / (now - _lastTargetTime);
                v.y = 0f;
                _targetVelocity = Vector3.Lerp(_targetVelocity, v, TargetVelocitySmoothing);
            }
            _hasTargetSample = true;
            _lastTarget = target;
            _lastTargetTime = now;

            float speed = TargetSpeed;
            if (!_targetMoving)
            {
                if (speed > MovingTargetEnterSpeed) _targetMoving = true;
                _targetSlowSince = now;
            }
            else if (speed >= MovingTargetExitSpeed)
            {
                _targetSlowSince = now;
            }
            else if (now - _targetSlowSince >= TargetStillSeconds)
            {
                _targetMoving = false;
            }
        }

        // =====================================================================================================
        // 純函數
        // =====================================================================================================

        /// <summary>A1 進入條件：agent 這條路徑通往目標、只到得了一部分、終點仍在攻擊距離＋邊際之外。</summary>
        internal static bool IsUnreachable(in EnemyPathSnapshot agentPath, Vector3 target, float reach)
        {
            return agentPath.Valid
                   && agentPath.Partial
                   && HorizontalDistance(agentPath.Destination, target) <= PathDestinationMatchDistance
                   && HorizontalDistance(agentPath.End, target) > reach + UnreachableEnterMargin;
        }

        /// <summary>D4：理想攻擊距離＝交戰帶中點（初始值；實際值待攻擊有效距離與動畫驗證）。</summary>
        internal static float IdealRadius(float bandMin, float reach) => (bandMin + reach) * 0.5f;

        /// <summary>從目標看出去的方位角（弧度，0＝+z，順時針為正）。</summary>
        internal static float BearingFromTarget(Vector3 target, Vector3 point) => Mathf.Atan2(point.x - target.x, point.z - target.z);

        internal static Vector3 SlotPoint(Vector3 target, float bearing, float radius) =>
            new Vector3(target.x + Mathf.Sin(bearing) * radius, target.y, target.z + Mathf.Cos(bearing) * radius);

        /// <summary>
        /// V1–V3：槽點可走、取樣後仍在交戰帶內、觸及線不跨 NavMesh 邊界、離邊緣夠遠。
        /// 回傳取樣後的可走點與離邊緣距離（評分用）。
        /// </summary>
        internal static bool IsSlotValid(Vector3 slotPoint, Vector3 target, float bandMin, float reach, IEnemyPathQuery query,
                                         out Vector3 walkable, out float edgeDistance)
        {
            edgeDistance = -1f;
            if (!query.TrySampleWalkable(slotPoint, SlotSampleRadius, out walkable)) return false;
            float d = HorizontalDistance(walkable, target);
            if (d < bandMin || d > reach) return false;
            if (!query.TrySampleWalkable(target, TargetSampleRadius, out Vector3 targetOnMesh)) return false;
            if (!query.IsStraightLineClear(walkable, targetOnMesh)) return false;
            edgeDistance = query.DistanceToEdge(walkable);
            return edgeDistance >= MinEdgeClearance;
        }

        /// <summary>
        /// 選槽：自己目前方位 ＋ 9 個偏移；V1–V4 都過的才計分。回傳成本最低的方位；沒有任何有效槽 ⇒ false。
        /// 成本相同時偏移較小者優先，再以候選順序決定（確定性）。
        /// </summary>
        internal bool SelectSlot(Vector3 self, Vector3 target, float bandMin, float reach, float now, IEnemyPathQuery query,
                                 out float bearing)
        {
            bearing = 0f;
            float selfBearing = BearingFromTarget(target, self);
            float radius = IdealRadius(bandMin, reach);
            bool selfOnMesh = query.TrySampleWalkable(self, SelfSampleRadius, out Vector3 selfWalkable);
            float bestCost = float.PositiveInfinity;
            bool found = false;
            foreach (float offsetDegrees in CandidateOffsetsDegrees)
            {
                float candidate = selfBearing + offsetDegrees * Mathf.Deg2Rad;
                if (!IsSlotValid(SlotPoint(target, candidate, radius), target, bandMin, reach, query, out Vector3 walkable, out float edge))
                    continue;

                // V4 可達：直線可走用直線距離，否則要有完整路徑
                float travel;
                if (selfOnMesh && query.IsStraightLineClear(selfWalkable, walkable)) travel = HorizontalDistance(self, walkable);
                else if (!query.TryPathLength(self, walkable, out travel)) continue;

                float cost = travel / TravelNormalizer
                             + AngleWeight * Mathf.Abs(offsetDegrees) / 135f
                             + ClearanceWeight * Mathf.Max(0f, ClearanceTarget - edge) / ClearanceTarget;
                if (_hasAbandoned && now < _abandonedUntil && Mathf.Abs(DeltaAngle(candidate, _abandonedBearing)) < AbandonBearingTolerance)
                    cost += AbandonPenalty;
                if (cost < bestCost - 1e-5f)
                {
                    bestCost = cost;
                    bearing = candidate;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// 走向槽點的下一個點：方位差 &gt; 60° 時先沿圓周繞（同半徑、往新槽方向偏 45°），不從目標身體中間穿過去。
        /// </summary>
        internal static Vector3 ResolveApproachPoint(Vector3 self, Vector3 target, float slotBearing, float radius)
        {
            float selfBearing = BearingFromTarget(target, self);
            float delta = DeltaAngle(selfBearing, slotBearing);
            if (Mathf.Abs(delta) <= DetourAngle + 1e-3f) return SlotPoint(target, slotBearing, radius);   // 容差：±60° 候選不因浮點誤差被當成要繞行
            float orbitRadius = Mathf.Max(radius, HorizontalDistance(self, target));
            return SlotPoint(target, selfBearing + Mathf.Sign(delta) * DetourStep, orbitRadius);
        }

        /// <summary>從 <paramref name="from"/> 轉到 <paramref name="to"/> 的最短有號角度（弧度，−π..π）。</summary>
        internal static float DeltaAngle(float from, float to) => Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        internal static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
