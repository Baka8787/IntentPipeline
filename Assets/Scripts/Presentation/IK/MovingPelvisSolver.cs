using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>
    /// 🧪（docs/46 §6，Moving 原型）一隻腳本帧交給統一骨盆求解器的事實。全部由 Controller 從既有管道組出，
    /// 本結構不讀任何 Unity 狀態。
    /// </summary>
    public struct MovingPelvisFoot
    {
        /// <summary>T3 相位（決定這隻腳的角色：Locked ⇒ Support；Unlocked ⇒ 可能是 Landing；Releasing ⇒ 無約束）。</summary>
        public FootLockPhase Phase;
        /// <summary>骨盆套用那一帧的髖位（pre-IK UpperLeg 一帧外插，與 T3 目標同一套用帧）。</summary>
        public Vector3 HipNext;
        /// <summary>本帧 T3 寫出的踝目標。</summary>
        public Vector3 Target;
        /// <summary>大腿＋小腿骨段長。≤ 0 ⇒ 事實無效。</summary>
        public float LegLength;
        /// <summary>腳下是否有地面命中（沒有 ⇒ 不可能是 Landing）。</summary>
        public bool HasFloor;
        /// <summary>目標腳底離它自己地面的高度（m）：目標踝高 − 腳底厚度 − 地面高。</summary>
        public float SoleHeight;
        /// <summary>T3 可靠支撐（Locked ∧ β≥LockBeta ∧ 動畫腳幾乎不動 ∧ 即時接觸）——SupportTransfer 的 Primary 由它的新邊沿決定。</summary>
        public bool Reliable;
    }

    public struct MovingPelvisTuning
    {
        /// <summary>目標低於骨盆時（必須跟上約束）的臨界阻尼角頻率（rad/s）。</summary>
        public float DropOmega;
        /// <summary>
        /// <see cref="MovingPelvisRecovery.Step"/> 的回升角頻率（rad/s）：步態內約束暫時解除時慢慢回升——逐步 bob 預算的旋鈕。
        /// </summary>
        public float RiseOmega;
        /// <summary>
        /// 下樓中（移動、root 垂直速度 &lt; −<see cref="DescendingStopSpeed"/>）的 <see cref="MovingPelvisRecovery.Step"/> 回升角頻率（rad/s；docs/46 §33）。
        /// 下樓的下蹲大半是「需求峰值之後回升太慢」累積的記憶，不是幾何需求；上樓的需求本身每步週期性很深，維持 <see cref="RiseOmega"/>。
        /// 設成與 <see cref="RiseOmega"/> 相同 ＝ 關閉。
        /// </summary>
        public float DescendRiseOmega;
        /// <summary>
        /// <see cref="MovingPelvisRecovery.TerrainExit"/> 的回升角頻率（rad/s）：地形提示說周圍已不需要這麼低（離開樓梯）時較快回升。
        /// </summary>
        public float TerrainExitOmega;
        /// <summary>進入 TerrainExit：地形提示高於骨盆超過此值（m）。</summary>
        public float TerrainExitMargin;
        /// <summary>離開 TerrainExit：地形提示高於骨盆不到此值（m）。與 <see cref="TerrainExitMargin"/> 構成滯後。</summary>
        public float TerrainExitReleaseMargin;
        /// <summary>開啟停步回升語意（StopHold → Stand）。關閉 ⇒ 意圖放開也照移動中語意（v2 行為）。</summary>
        public bool UseStopSemantics;
        /// <summary><see cref="MovingPelvisRecovery.Stand"/> 的回升角頻率（rad/s）。</summary>
        public float StandRiseOmega;
        /// <summary>停步「落定」的 root 水平速度門檻（m/s）。</summary>
        public float SettleRootSpeed;
        /// <summary>放開時 root 垂直速度低於 −此值（m/s）⇒ 視為下樓停步（FutureReachCap 只批准 Walk 下樓）。</summary>
        public float DescendingStopSpeed;
        /// <summary>停步使用 SupportTransfer（Primary 後就站直）；false ＝ StopHold（等兩腳都 Locked）。</summary>
        public bool UseSupportTransfer;
        /// <summary>
        /// 下樓停步（放開時 root 垂直速度 &lt; −<see cref="DescendingStopSpeed"/>，任何步態）的 SupportTransfer 中，
        /// **正在下降落地（Landing）**的尾腳也能往下拉（docs/46 §35）。§19 規則②只排除 Releasing／上升中的擺盪。
        /// false ＝ 只有 Locked 腳能往下拉（§19 原行為）。
        /// </summary>
        public bool DescendingTrailingLandingPulls;
        /// <summary>Landing 預告：上限放寬 κ × 腳底離地高度，接觸瞬間精確。</summary>
        public float LandingAnticipation;
        /// <summary>Support 的硬可達比（× 腿長）：Humanoid Leg Stretch 的邊界，超過才是真的搆不到。</summary>
        public float HardReachRatio;
        /// <summary>骨盆最低偏移（正值，m）。</summary>
        public float MaxOffset;

        public static MovingPelvisTuning Default => new MovingPelvisTuning
        {
            DropOmega = 12f,
            RiseOmega = 1.5f,
            DescendRiseOmega = 6f,
            TerrainExitOmega = 6f,
            TerrainExitMargin = 0.05f,
            TerrainExitReleaseMargin = 0.02f,
            UseStopSemantics = false,
            StandRiseOmega = 6f,
            SettleRootSpeed = 0.05f,
            DescendingStopSpeed = 0.2f,
            UseSupportTransfer = true,
            DescendingTrailingLandingPulls = true,
            LandingAnticipation = 1.5f,
            HardReachRatio = 1.05f,
            MaxOffset = 0.35f,
        };
    }

    /// <summary>
    /// 本帧骨盆往哪個方向走、用哪一種語意（docs/46 §13）。兩種回升**語意不同，不是同一個 ω 的兩個值**：
    /// </summary>
    public enum MovingPelvisRecovery
    {
        /// <summary>目標不高於骨盆：跟上約束（<see cref="MovingPelvisTuning.DropOmega"/>）或已貼住。</summary>
        None = 0,
        /// <summary>步態內約束暫時解除（後腳放開、下一步還沒落地）：慢回升，控制每步 bob。</summary>
        Step = 1,
        /// <summary>地形提示說周圍已不需要這麼低（例如走出樓梯）：較快回升。提示只選回升速度，⛔ 不是目標、不是上限。</summary>
        TerrainExit = 2,
        /// <summary>停步中（意圖已放開、最後幾步還沒落定）：不回升，只允許跟著約束下降（docs/46 §14）。</summary>
        StopHold = 3,
        /// <summary>停步已落定（兩腳鎖住、root 靜止）：沒有步態起伏要控制，以站立速度回升到兩腳都搆得到的最高處。</summary>
        Stand = 4,
        /// <summary>
        /// 停步中、前腳已成為 Reliable Primary Support（docs/46 §19）：順著支撐腿站直。
        /// 尾腳 Releasing／Swing 不往下拉；尾腳的未來落點只當上限（FutureReachCap），不主動降低骨盆。
        /// </summary>
        SupportTransfer = 5,
    }

    /// <summary>SupportTransfer 期間限制「最高可以升到哪」的來源（除錯用）。</summary>
    public enum MovingPelvisCapSource
    {
        None = 0,
        /// <summary>預測的尾腳最後落點（只批准 Walk 下樓停步）。</summary>
        PredictedLanding = 1,
        /// <summary>Walk 下樓停步但預測尚未可用：保守——暫不回升（只到預測到達或尾腳鎖定為止）。</summary>
        PredictionPending = 2,
        /// <summary>其他停步：尾腳正在下降落地時的即時 Landing 上限（只當上限）。</summary>
        LiveLanding = 3,
    }

    /// <summary>本帧給求解器的情境（不是約束）：只影響回升語意。</summary>
    public struct MovingPelvisContext
    {
        /// <summary>地形提示（Terrain Envelope）；NaN ⇒ 沒有。</summary>
        public float TerrainHint;
        /// <summary>黑板移動意圖 &gt; 0。</summary>
        public bool MovementRequested;
        /// <summary>root 水平速度（m/s）。</summary>
        public float RootHorizontalSpeed;
        /// <summary>root 垂直速度（m/s；負＝下降）——放開時鎖存「是不是下樓停步」。</summary>
        public float RootVerticalSpeed;
        /// <summary>目前是 Walk 步態（黑板 Gait）——放開時鎖存。</summary>
        public bool WalkGait;
        /// <summary>左／右腳預測的最後落點（T3 的 IK goal 參考點，世界座標）；Has* ＝ false 表示該腳沒有可用預測。</summary>
        public bool HasLeftLanding, HasRightLanding;
        public Vector3 LeftLanding, RightLanding;
    }

    /// <summary>
    /// 刻意跨帧保存的求解器內態（與骨盆 MoveTowards、T3 鎖腳同類的演算法內態）。
    /// <see cref="Offset"/> 是「Moving 參考骨盆」，不是最終骨盆：Controller 把它當成移動參考交給既有路徑。
    /// </summary>
    public struct MovingPelvisState
    {
        public bool Active;
        public float Offset;
        public float Velocity;
        public bool HasPreviousSoleHeight;
        public float PreviousLeftSoleHeight;
        public float PreviousRightSoleHeight;
        public bool HasPreviousHardLimit;
        public float PreviousHardLimit;
        /// <summary>上一帧硬上限由哪隻腳決定：0 ＝ 無、1 ＝ 左、2 ＝ 右（docs/46 §27：區分「同一約束移動」與「約束加入／換腳」）。</summary>
        public int PreviousHardOwner;
        /// <summary>TerrainExit 滯後鎖存：進入後直到提示與骨盆的差距小於 release margin 才離開。</summary>
        public bool TerrainExitLatched;
        /// <summary>停步已落定（鎖存到移動意圖恢復）。</summary>
        public bool StopSettled;
        /// <summary>停步的 Primary Support：0 ＝ 還沒有、1 ＝ 左、2 ＝ 右（放開後第一個新的 Reliable 邊沿；鎖存到移動意圖恢復）。</summary>
        public int PrimaryFoot;
        /// <summary>放開當下鎖存：這次停步是否批准使用 FutureReachCap（Walk ∧ 下樓）。</summary>
        public bool StopUsesPrediction;
        /// <summary>放開當下鎖存：這次停步是下樓停步（任何步態；docs/46 §35 的閘）。</summary>
        public bool StopDescending;
        public bool HasPreviousStopFacts;
        public bool PreviousMovementRequested;
        public bool PreviousLeftReliable, PreviousRightReliable;

        // 除錯（probe／Inspector），不參與決策
        public MovingPelvisCapSource DebugCapSource;
        public float DebugCap;
        public MovingPelvisRecovery DebugRecovery;
        public float DebugTerrainHint;
        public float DebugLimit;
        public float DebugHardLimit;
        public float DebugTarget;
        public int DebugLeftRole;
        public int DebugRightRole;
        public bool DebugHardClampApplied;
        /// <summary>本帧硬夾發生在約束加入／換腳（不承接上限的變化率）。</summary>
        public bool DebugHardActivation;
        /// <summary>本帧的 Step 回升使用下樓角頻率。</summary>
        public bool DebugDescendingRise;
        /// <summary>本帧 SupportTransfer 的下拉包含正在落地的尾腳（docs/46 §35）。</summary>
        public bool DebugTrailingLandingPull;

        public void Reset() => this = default;
    }

    /// <summary>
    /// 🧪 docs/46 §6 統一骨盆求解器（Moving）：<c>Intent I=0</c> → 有效 Reach Constraints（Support／Landing）→ 非對稱臨界阻尼。
    /// <list type="bullet">
    /// <item>Support ＝ T3 <c>Locked</c>：上限 <c>LegBound(髖, 目標, L)</c>；硬上限用 <c>HardReachRatio·L</c>。</item>
    /// <item>Landing ＝ T3 <c>Unlocked</c>、有地面、腳底正在下降（ḣ ≤ 0）：上限 <c>LegBound(髖, 目標, L) + κ·h</c>，接觸瞬間精確。</item>
    /// <item>Releasing／上升中的擺盪腳：不約束（移除上限只會讓骨盆放鬆，不會迫使下跳）。</item>
    /// </list>
    /// R-noHL：⛔ 不假設抬跟——T3 尚未支援，骨盆不能用 Contact 做不到的放置算約束。
    /// </summary>
    public static class MovingPelvisSolver
    {
        public const int RoleNone = 0, RoleSupport = 1, RoleLanding = 2;

        /// <summary>這隻腳本帧的角色與上限（+∞ ＝ 不約束）。</summary>
        public static int FootLimit(in MovingPelvisFoot foot, bool hasPreviousSoleHeight, float previousSoleHeight,
            in MovingPelvisTuning tuning, out float limit, out float hardLimit)
        {
            limit = float.PositiveInfinity;
            hardLimit = float.PositiveInfinity;
            if (foot.LegLength <= 0f) return RoleNone;
            if (foot.Phase == FootLockPhase.Locked)
            {
                limit = StandingPostureSolver.LegBound(foot.HipNext, foot.Target, foot.LegLength);
                hardLimit = StandingPostureSolver.LegBound(foot.HipNext, foot.Target, tuning.HardReachRatio * foot.LegLength);
                return RoleSupport;
            }
            // 沒有上一帧的高度 ⇒ 視為 ḣ = 0（不下降也不上升）：與離線模擬相同。
            bool descending = !hasPreviousSoleHeight || foot.SoleHeight <= previousSoleHeight;
            if (foot.Phase == FootLockPhase.Unlocked && foot.HasFloor && descending)
            {
                limit = StandingPostureSolver.LegBound(foot.HipNext, foot.Target, foot.LegLength)
                        + tuning.LandingAnticipation * Mathf.Max(0f, foot.SoleHeight);
                return RoleLanding;
            }
            return RoleNone;
        }

        /// <summary>第一次啟用（或重設後）從目前實際骨盆開始、速度 0：不會因啟用而跳。</summary>
        public static void Begin(ref MovingPelvisState state, float currentOffset)
        {
            state.Reset();
            state.Active = true;
            state.Offset = currentOffset;
        }

        /// <param name="terrainHint">
        /// 地形提示（Terrain Envelope）：周圍地形需要骨盆多低。**只用來選回升語意**（<see cref="MovingPelvisRecovery"/>）；
        /// ⛔ 不進目標、不進上限。NaN／無限 ⇒ 沒有提示（永遠是 Step 回升）。
        /// </param>
        public static void Step(ref MovingPelvisState state, in MovingPelvisFoot left, in MovingPelvisFoot right,
            float terrainHint, float dt, in MovingPelvisTuning tuning)
        {
            var context = new MovingPelvisContext { TerrainHint = terrainHint, MovementRequested = true };
            Step(ref state, in left, in right, in context, dt, in tuning);
        }

        /// <summary>
        /// 完整版：<paramref name="context"/> 帶本帧的地形提示、移動意圖與 root 水平速度。
        /// 停步語意（<see cref="MovingPelvisTuning.UseStopSemantics"/>）只改「回升」：意圖放開後到落定前 <see cref="MovingPelvisRecovery.StopHold"/>，
        /// 落定後 <see cref="MovingPelvisRecovery.Stand"/>。目標與約束與移動中完全相同——停步沒有自己的高度規則。
        /// </summary>
        public static void Step(ref MovingPelvisState state, in MovingPelvisFoot left, in MovingPelvisFoot right,
            in MovingPelvisContext context, float dt, in MovingPelvisTuning tuning)
        {
            if (!state.Active || dt <= 0f) return;
            float terrainHint = context.TerrainHint;

            state.DebugLeftRole = FootLimit(in left, state.HasPreviousSoleHeight, state.PreviousLeftSoleHeight, in tuning,
                out float leftLimit, out float leftHard);
            state.DebugRightRole = FootLimit(in right, state.HasPreviousSoleHeight, state.PreviousRightSoleHeight, in tuning,
                out float rightLimit, out float rightHard);
            state.PreviousLeftSoleHeight = left.SoleHeight;
            state.PreviousRightSoleHeight = right.SoleHeight;
            state.HasPreviousSoleHeight = true;

            float limit = Mathf.Min(leftLimit, rightLimit);
            float hard = Mathf.Min(leftHard, rightHard);
            float floor = -Mathf.Max(0f, tuning.MaxOffset);
            // Intent I = 0：動畫名目高度；只有約束要求時才下降。
            float target = Mathf.Max(Mathf.Min(0f, limit), floor);

            // 回升語意：地形提示只決定「用哪一種回升」，目標仍然只有 min(Intent, 約束)。
            bool hasHint = !float.IsNaN(terrainHint) && !float.IsInfinity(terrainHint);
            float gap = hasHint ? terrainHint - state.Offset : float.NegativeInfinity;
            if (!hasHint || gap < tuning.TerrainExitReleaseMargin) state.TerrainExitLatched = false;
            else if (gap > tuning.TerrainExitMargin) state.TerrainExitLatched = true;
            // 停步：意圖放開後，兩腳都鎖住且 root 幾乎不動才算「落定」；恢復移動意圖即清除。
            bool stopping = tuning.UseStopSemantics && !context.MovementRequested;
            bool releaseEdge = state.HasPreviousStopFacts && state.PreviousMovementRequested && !context.MovementRequested;
            if (!stopping)
            {
                state.StopSettled = false;
                state.PrimaryFoot = 0;
                state.StopUsesPrediction = false;
                state.StopDescending = false;
            }
            else
            {
                // FutureReachCap 只批准 Walk 下樓停步（使用者裁決 §19）：放開當下鎖存。
                if (releaseEdge)
                {
                    state.StopDescending = context.RootVerticalSpeed < -tuning.DescendingStopSpeed;
                    state.StopUsesPrediction = context.WalkGait && state.StopDescending;
                }
                if (!state.StopSettled && left.Phase == FootLockPhase.Locked && right.Phase == FootLockPhase.Locked
                    && context.RootHorizontalSpeed < tuning.SettleRootSpeed)
                    state.StopSettled = true;
                // Primary Support：放開後第一個**新的** Reliable 邊沿（鎖存）。
                if (state.PrimaryFoot == 0)
                {
                    if (left.Reliable && !state.PreviousLeftReliable) state.PrimaryFoot = 1;
                    else if (right.Reliable && !state.PreviousRightReliable) state.PrimaryFoot = 2;
                }
            }
            state.PreviousMovementRequested = context.MovementRequested;
            state.PreviousLeftReliable = left.Reliable;
            state.PreviousRightReliable = right.Reliable;
            state.HasPreviousStopFacts = true;

            bool transfer = tuning.UseSupportTransfer && stopping && state.PrimaryFoot != 0 && !state.StopSettled;
            state.DebugDescendingRise = false;
            state.DebugTrailingLandingPull = false;
            bool hold = false;
            MovingPelvisRecovery recovery;
            float omega;
            state.DebugCapSource = MovingPelvisCapSource.None;
            state.DebugCap = float.PositiveInfinity;
            if (transfer)
            {
                // SupportTransfer：Locked（Support）腳能往下拉；尾腳 Releasing／上升中的擺盪不往下拉。
                float support = Mathf.Min(
                    state.DebugLeftRole == RoleSupport ? leftLimit : float.PositiveInfinity,
                    state.DebugRightRole == RoleSupport ? rightLimit : float.PositiveInfinity);
                bool trailingIsLeft = state.PrimaryFoot == 2;
                MovingPelvisFoot trailing = trailingIsLeft ? left : right;
                // 下樓停步：正在下降落地的尾腳也往下拉（docs/46 §35）——它的需求提前十帧以上就看得到，
                // 不吃進來就只能等它鎖定時由硬夾一帧補上（§34.4）。
                if (tuning.DescendingTrailingLandingPulls && state.StopDescending
                    && (trailingIsLeft ? state.DebugLeftRole : state.DebugRightRole) == RoleLanding)
                {
                    float trailingLimit = trailingIsLeft ? leftLimit : rightLimit;
                    state.DebugTrailingLandingPull = trailingLimit < support;
                    support = Mathf.Min(support, trailingLimit);
                }
                float down = Mathf.Max(Mathf.Min(0f, support), floor);
                float cap = float.PositiveInfinity;
                if (trailing.Phase != FootLockPhase.Locked)
                {
                    if (state.StopUsesPrediction)
                    {
                        bool has = trailingIsLeft ? context.HasLeftLanding : context.HasRightLanding;
                        if (has && trailing.LegLength > 0f)
                        {
                            cap = StandingPostureSolver.LegBound(trailing.HipNext,
                                trailingIsLeft ? context.LeftLanding : context.RightLanding, trailing.LegLength);
                            state.DebugCapSource = MovingPelvisCapSource.PredictedLanding;
                        }
                        else
                        {
                            // 預測還沒到：保守——暫不回升（只到預測到達或尾腳鎖定為止），不是整段 StopHold。
                            cap = state.Offset;
                            state.DebugCapSource = MovingPelvisCapSource.PredictionPending;
                        }
                    }
                    else if ((trailingIsLeft ? state.DebugLeftRole : state.DebugRightRole) == RoleLanding)
                    {
                        cap = trailingIsLeft ? leftLimit : rightLimit;
                        state.DebugCapSource = MovingPelvisCapSource.LiveLanding;
                    }
                }
                state.DebugCap = cap;
                // 尾腳真正 Locked 後，它就是一般的 Support 約束（已在 support 裡），預測自動退出。
                float upTo = Mathf.Max(Mathf.Min(down, cap), floor);
                if (down < state.Offset)
                {
                    target = down;                       // 某隻 Support 腳要求——照常往下
                    recovery = MovingPelvisRecovery.None;
                    omega = tuning.DropOmega;
                }
                else if (upTo > state.Offset)
                {
                    target = upTo;                       // 順著支撐腿站直，不超過 FutureReachCap
                    recovery = MovingPelvisRecovery.SupportTransfer;
                    omega = tuning.StandRiseOmega;
                }
                else
                {
                    target = state.Offset;               // 被 cap 擋住：停住，cap 不會把骨盆往下拉
                    recovery = MovingPelvisRecovery.SupportTransfer;
                    omega = tuning.DropOmega;
                    hold = true;
                }
            }
            else
            {
                recovery = target <= state.Offset ? MovingPelvisRecovery.None
                    : stopping ? (state.StopSettled ? MovingPelvisRecovery.Stand : MovingPelvisRecovery.StopHold)
                    : state.TerrainExitLatched ? MovingPelvisRecovery.TerrainExit
                    : MovingPelvisRecovery.Step;
                // 下樓（移動中、root 正在下降）的 Step 回升用下樓角頻率；停步語意（StopHold／Stand）與 TerrainExit 不受影響。
                bool descendingRise = recovery == MovingPelvisRecovery.Step && !stopping
                    && context.RootVerticalSpeed < -tuning.DescendingStopSpeed;
                state.DebugDescendingRise = descendingRise;
                omega = recovery == MovingPelvisRecovery.None || recovery == MovingPelvisRecovery.StopHold ? tuning.DropOmega
                    : recovery == MovingPelvisRecovery.TerrainExit ? tuning.TerrainExitOmega
                    : recovery == MovingPelvisRecovery.Stand ? tuning.StandRiseOmega
                    : descendingRise ? tuning.DescendRiseOmega
                    : tuning.RiseOmega;
                // StopHold（只剩 Primary 出現之前）：不回升，只允許下降。
                if (recovery == MovingPelvisRecovery.StopHold)
                {
                    target = state.Offset;
                    hold = true;
                }
            }
            state.DebugRecovery = recovery;
            state.DebugTerrainHint = terrainHint;
            float start = state.Offset;
            float acceleration = omega * omega * (target - state.Offset) - 2f * omega * state.Velocity;
            state.Velocity += acceleration * dt;
            if (hold) state.Velocity = Mathf.Min(state.Velocity, 0f);
            state.Offset += state.Velocity * dt;

            if (state.Offset < floor)
            {
                state.Offset = floor;
                state.Velocity = Mathf.Max(state.Velocity, 0f);
            }
            // 硬可達（Leg Stretch 邊界）：Support 腳在骨盆套用那一帧必須搆得到。
            state.DebugHardClampApplied = false;
            state.DebugHardActivation = false;
            float clampedHard = Mathf.Max(hard, floor);
            int hardOwner = float.IsInfinity(hard) ? 0 : (leftHard <= rightHard ? 1 : 2);
            // 同一個有效約束（上一帧就由同一隻腳決定）⇒ 上限在「移動」；否則是約束加入或換腳（docs/46 §27）。
            bool sameConstraint = state.HasPreviousHardLimit && hardOwner == state.PreviousHardOwner;
            if (state.Offset > hard)
            {
                state.DebugHardClampApplied = true;
                state.Offset = clampedHard;
                if (sameConstraint)
                {
                    // 被夾住時速度跟著「上限本身」的變化率，不是這一帧被夾掉的距離：
                    // 把夾掉的距離當速度會讓彈簧衝過約束，再用慢的 ω_up 花好幾秒爬回來（多降的深度沒有任何可達理由）。
                    float follow = state.HasPreviousHardLimit ? (clampedHard - state.PreviousHardLimit) / dt : 0f;
                    state.Velocity = Mathf.Min(state.Velocity, follow);
                }
                else
                {
                    // 約束加入／換腳：上限的階躍是「約束身分改變」，不是上限的速度。位置照樣立刻合法；
                    // 速度保留原本的連續值，只取消會繼續往非法方向（向上）穿越邊界的分量——不注入新的向下速度。
                    state.DebugHardActivation = true;
                    state.Velocity = Mathf.Min(state.Velocity, 0f);
                }
            }
            state.HasPreviousHardLimit = !float.IsInfinity(hard);
            state.PreviousHardLimit = clampedHard;
            state.PreviousHardOwner = hardOwner;

            state.DebugLimit = limit;
            state.DebugHardLimit = hard;
            state.DebugTarget = target;
        }
    }
}
