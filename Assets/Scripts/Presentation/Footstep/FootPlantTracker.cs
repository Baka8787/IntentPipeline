using System;
using UnityEngine;
using Project.Core.Blackboard;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 落腳偵測參數。🔄（Stage 1）全部重新以**磁碟上真實動畫的量測值**校準，不是手感試出來的。
    /// 量測方法與完整數據見 `docs/35-footstep-detector-stage1.md` §2（一次性 probe，已刪除）。
    /// </summary>
    [Serializable]
    public class FootstepDetectionSettings
    {
        [Tooltip("上膛門檻（m/s，填正值）：腳底相對地面的下降速度達到此值才視為「真的在落腳」。\n" +
                 "量測依據：所有真實跨步的峰值下降速度 ≥ 0.85 m/s（最慢 Walk 收步 0.85、最快 Sprint 4.31）；\n" +
                 "停步 settling 的峰值僅 0.03～0.40 m/s。取 0.25 保留對慢速 playback／blend 壓平的餘裕。")]
        public float ArmDescentSpeed = 0.25f;

        [Tooltip("擊發門檻（m/s，填正值）：上膛後，下降速度回升到慢於此值即視為下降結束。\n" +
                 "⚠️ 必須明顯小於上膛門檻——兩者形成 Schmitt trigger，速度在 0 附近抖動時無法重新上膛。")]
        public float FireDescentSpeed = 0.05f;

        [Tooltip("重新上膛所需的抬腳行程（公尺）：距上次落腳之後，腳底必須相對地面抬高至少這個距離，\n" +
                 "下一次落腳才算數。**這是 per-foot 的高度判準，刻意不是時間閘**——時間閘的門檻必須依\n" +
                 "最快步頻反推，估錯就會把 sprint 的真實腳步濾掉（兩個參考實作都踩了這個坑）。\n" +
                 "量測依據：純 settling／骨骼微震的行程 ≤ 0.017 m；最小的真實腳部調整為 0.033 m。取 0.025。")]
        public float ReArmLift = 0.025f;

        [Tooltip("判為「正常腳步」所需的抬腳行程（公尺）。低於此值但高於 ReArmLift 者判為 Shuffle。\n" +
                 "量測依據：最小的真實跨步行程 0.12 m（Walk 收步）；最大的停步調整 0.033 m。\n" +
                 "取 0.060（≈兩者幾何中位），對上下各有約 2 倍餘裕。")]
        public float NormalStepLift = 0.060f;

        [Tooltip("接觸帶（公尺）：擊發時腳底離地必須在此距離內，否則視為還在空中。\n" +
                 "量測依據：真實落腳時腳底離地最大 0.030 m（RunRtLoop，動畫本身腳沒完全踩到）；\n" +
                 "擺盪中低點最低 0.044 m。取 0.045。⚠️ 這道守的是「腳還在半空就出聲」。")]
        public float ContactClearance = 0.045f;

        [Tooltip("落腳時允許的腳部**世界水平速度**上限（m/s）。\n" +
                 "⭐ 這是分離「真正踩地」與「擺盪途中的低點」最乾淨的訊號：踩住的腳在世界空間幾乎不動\n" +
                 "（量測：真實落腳 0.01～0.91 m/s），擺盪中的腳則以約兩倍角色速度掠過（量測：4.00～7.11 m/s）。\n" +
                 "取 2.0（兩側各約 2 倍餘裕）。⚠️ 原地高速旋轉時踩住的腳會繞軸畫弧而有殘速，\n" +
                 "極端轉速下可能漏一步——寧可漏報也不誤報。")]
        public float PlantHorizontalSpeedMax = 2.0f;

        [Tooltip("地面高度跳變容許值（公尺）：單帧內腳下地面高度變化超過此值，視為 ray 跨過了階梯級緣，\n" +
                 "該帧的離地高度與上一帧不可比 ⇒ 不計速度、不擊發、不累計行程。\n" +
                 "量測依據：場景樓梯級高 0.194 m；而擊發前提已把腳的水平速度限在 2.0 m/s，\n" +
                 "即使 45° 斜坡單帧地面變化也僅 0.033 m。取 0.06。")]
        public float GroundStepTolerance = 0.06f;
    }

    /// <summary>單腳偵測的相位。兩態，per-foot，左右完全獨立。</summary>
    public enum FootPlantPhase
    {
        /// <summary>已落腳。必須先累積足夠的抬腳行程（<see cref="FootstepDetectionSettings.ReArmLift"/>）才能離開此態。</summary>
        Planted,

        /// <summary>已抬起。可以偵測下一次落腳。</summary>
        Lifted,
    }

    /// <summary>本帧沒有回報事件的原因。**僅供 debug instrumentation**，不進任何決策。</summary>
    public enum FootstepSuppressReason
    {
        None = 0,
        /// <summary>本帧有事件（沒有被抑制）。</summary>
        Fired,
        /// <summary>時間沒有流逝（暫停／第一帧），沒有速度可算。</summary>
        NoTimeStep,
        /// <summary>尚未建立前一帧基準。</summary>
        Warmup,
        /// <summary>仍在 Planted：抬腳行程還不夠，不構成新的一步。⭐ 收步碎步主要落在這裡。</summary>
        NotReArmed,
        /// <summary>正在快速下降（本帧只負責上膛）。</summary>
        Descending,
        /// <summary>這一段下降從未達到上膛門檻。</summary>
        NeverArmed,
        /// <summary>腳下地面高度本帧跳變（跨過階梯級緣），資料不可比。</summary>
        GroundDiscontinuity,
        /// <summary>下降結束了，但腳底離地仍超過接觸帶——腳還在空中。</summary>
        AboveContactBand,
        /// <summary>⭐ 下降結束且貼近地面，但腳仍以擺盪速度水平掠過——這是擺盪中低點，不是落腳。</summary>
        StillSwinging,
    }

    /// <summary>
    /// 單腳落腳偵測的**跨帧狀態**。
    ///
    /// 刻意做成 <c>struct</c>（值型別）並由偵測器以欄位持有——與 <c>LocomotionSpeedSmoother</c> 同款：
    /// 跨帧狀態需要一個明確的擁有者，值型別讓「誰持有它」在型別層就看得見，且執行期零配置。
    ///
    /// 也刻意做成**可獨立呼叫的純推進函式**（<see cref="Advance"/> 顯式收 <c>deltaTime</c>）：
    /// 偵測邏輯因此能在 EditMode 用確定性的時間步長測試，不必依賴 <c>Time.deltaTime</c>
    /// 或真實 Animator（同 <c>FootIKController.ComputeFootWeight</c> 走純函數測試的先例）。
    ///
    /// ---
    /// ## 🔄 Stage 1：判準從「世界高度」換成「離地高度 ＋ 水平速度」
    ///
    /// **舊版的兩個結構性缺陷**（皆由真實動畫量測證實，見 `docs/35` §2）：
    ///
    /// 1. **擺盪中低點被當成落腳。** 舊版只看「下降停止」。但一次跨步的垂直軌跡**不是單調的**——
    ///    `WalkFwdLoop` 的左腳在 t=0.43 抬到 0.179 m 後降到 t=0.63 的 0.044 m、**再抬回 0.059 m**、
    ///    最後才在 t≈1.00 真正踩地。舊版在 t=0.63 就擊發，且因為之後的行程只剩 0.015 m
    ///    打不到當時的 `MinLiftExcursion 0.03`，**真正的踩地反而被吃掉**。
    ///    ⇒ 走路時聽到的每一聲，都是在錯的相位上發出的。
    ///
    /// 2. **世界高度在樓梯上沒有意義。** 上樓時 root 與腳的世界 Y 同時上升，
    ///    「腳的世界高度」完全無法代表腳與地面的關係。
    ///
    /// **Stage 1 的判準**（四道，缺一不可）：
    /// ① 相對**地面**（不是相對世界原點、也不是相對 root）抬起足夠行程 → 才重新上膛；
    /// ② 曾經真的在快速下降（Schmitt 上膛）；
    /// ③ 下降結束時**貼近地面**（離地 ≤ 接觸帶）；
    /// ④ 下降結束時**水平幾乎不動**（踩住的腳在世界空間是靜止的；擺盪中的腳不是）。
    ///
    /// ⭐ ④ 是把兩類事件分開的關鍵——量測上真實落腳與擺盪中低點的水平速度相差 4.4 倍，
    /// 而兩者的高度只差 1.27 倍。**選對訊號比調對門檻重要。**
    ///
    /// ⚠️ **仍然沒有時間閘、沒有 cooldown。** 兩個參考實作都用固定 cooldown（0.33 s／0.3 s）
    /// 掩蓋偵測問題，代價是步頻上限——本專案 sprint 的步頻會直接被吃掉。
    /// </summary>
    public struct FootPlantTracker
    {
        // --- 前一帧基準 ---
        private bool _hasPreviousSample;
        private float _previousClearance;
        private float _previousGroundReference;
        private bool _previousHadGround;
        private float _previousRootPlaneY;
        private Vector3 _previousFootPosition;

        // --- 相位與上膛 ---
        private bool _isLifted;
        private bool _isArmed;

        // --- 行程累計（相對地面）---
        private float _clearanceAtLastPlant;
        private float _maxClearanceSinceLastPlant;

        // --- 供 instrumentation 檢視（不參與決策）---
        private float _lastClearance;
        private float _lastVerticalSpeed;
        private float _lastHorizontalSpeed;

        /// <summary>目前相位。</summary>
        public FootPlantPhase Phase => _isLifted ? FootPlantPhase.Lifted : FootPlantPhase.Planted;

        /// <summary>是否已上膛（曾在本段下降達到上膛門檻）。</summary>
        public bool IsArmed => _isArmed;

        /// <summary>最近一帧的腳底離地高度（公尺）。</summary>
        public float Clearance => _lastClearance;

        /// <summary>最近一帧的垂直速度（m/s，負值＝下降）。</summary>
        public float VerticalSpeed => _lastVerticalSpeed;

        /// <summary>最近一帧的腳部世界水平速度（m/s）。</summary>
        public float HorizontalSpeed => _lastHorizontalSpeed;

        /// <summary>距上次落腳以來累積的抬腳行程（公尺，相對地面）。</summary>
        public float LiftSinceLastPlant => _maxClearanceSinceLastPlant - _clearanceAtLastPlant;

        /// <summary>
        /// 推進一帧，回傳本帧的落腳分類（<see cref="FootstepKind.None"/> ＝無事件）。
        /// </summary>
        /// <param name="footBottomWorldPosition">腳底的世界座標（IK goal 世界位置 − <c>FeetBottomHeight</c>，**pre-IK**）。</param>
        /// <param name="hasGround">本帧腳下是否觀測到地面（來自 Foot IK 的共享觀測）。</param>
        /// <param name="groundY">腳下地面高度（世界 Y）。<paramref name="hasGround"/> 為 false 時忽略。</param>
        /// <param name="fallbackGroundReferenceY">沒有地面觀測時的退化基準（角色 Root 平面 Y）。</param>
        /// <param name="deltaTime">本帧時間步長。<c>&lt;= 0</c>（暫停）時整段跳過且**不推進任何狀態**——
        /// 沒有時間流逝就沒有速度可算，比照 <c>MotionDriver.IsTimeFrozen</c>。</param>
        /// <param name="settings">偵測參數。</param>
        /// <param name="reason">本帧沒有擊發的原因（僅供 instrumentation）。</param>
        public FootstepKind Advance(
            Vector3 footBottomWorldPosition,
            bool hasGround,
            float groundY,
            float fallbackGroundReferenceY,
            float deltaTime,
            FootstepDetectionSettings settings,
            out FootstepSuppressReason reason)
        {
            if (settings == null || deltaTime <= 0f)
            {
                reason = FootstepSuppressReason.NoTimeStep;
                return FootstepKind.None;
            }

            // 地面基準：有觀測就用觀測，沒有就退回 Root 平面。
            // ⚠️ Root 原點＝腳底＝膠囊底（ADR-001＋CapsuleFitter §0.3 規則 6），故可直接作為地面平面。
            float groundReference = hasGround ? groundY : fallbackGroundReferenceY;
            float clearance = footBottomWorldPosition.y - groundReference;
            _lastClearance = clearance;

            if (!_hasPreviousSample)
            {
                // 首次採樣：只建立基準，沒有前一帧就算不出速度。
                SeedBaseline(footBottomWorldPosition, clearance, groundReference, hasGround, fallbackGroundReferenceY);
                reason = FootstepSuppressReason.Warmup;
                return FootstepKind.None;
            }

            // === 參考系不連續偵測（樓梯級緣）===
            // 「離地高度」是相對量，它的前後兩帧要能相減，前提是**兩帧的參考系一樣**。
            // 樓梯上有兩件事會讓這個前提失效，兩件都必須擋：
            //
            // ① **腳下的地面換了一階**：ray 跨過級緣，`groundY` 單帧跳一整個級高（場景實測 0.194 m）。
            // ② **角色自己被抬上一階**：`CharacterController` 的 step offset 會在單帧內把整個
            //    Root 平面抬高一個級高。此時腳的世界位置**跟著整包上移**，
            //    即使腳相對角色根本沒動，差分也會算出一個巨大的假速度。
            //    ⚠️ ②是實測抓到的——只擋①時，上樓每隔 8~9 帧就會多擊發一次
            //    （正常步距是 26~34 帧），那正是使用者回報的「樓梯腳步過密」。
            //
            // 兩者都不是「腳的運動」，而是「量腳的那把尺跳了」。
            bool groundSourceChanged = hasGround != _previousHadGround;
            bool groundJumped = hasGround && _previousHadGround &&
                                Mathf.Abs(groundReference - _previousGroundReference) > settings.GroundStepTolerance;
            bool rootPlaneJumped = Mathf.Abs(fallbackGroundReferenceY - _previousRootPlaneY) > settings.GroundStepTolerance;
            bool discontinuity = groundSourceChanged || groundJumped || rootPlaneJumped;

            float verticalSpeed = discontinuity ? 0f : (clearance - _previousClearance) / deltaTime;

            Vector3 horizontalDelta = footBottomWorldPosition - _previousFootPosition;
            horizontalDelta.y = 0f;
            float horizontalSpeed = horizontalDelta.magnitude / deltaTime;

            _lastVerticalSpeed = verticalSpeed;
            _lastHorizontalSpeed = horizontalSpeed;

            // 基準前進（⚠️ 無論後面走哪一條分支都必須前進——「不回報」與「沒發生」是兩件事）。
            _previousClearance = clearance;
            _previousGroundReference = groundReference;
            _previousHadGround = hasGround;
            _previousRootPlaneY = fallbackGroundReferenceY;
            _previousFootPosition = footBottomWorldPosition;

            // 行程累計：不連續的那一帧不採計（否則跨級緣會憑空灌進一整個級高的行程）。
            if (!discontinuity && clearance > _maxClearanceSinceLastPlant)
            {
                _maxClearanceSinceLastPlant = clearance;
            }

            // ⚠️ **落腳基準要能向下修正**（否則某些動畫的腳永遠不會出聲）。
            // 行程是「相對上次落腳的高度」量的，但一開始（或剛被 ForcePlanted 之後）那個基準
            // 只是「當下腳在哪」——若此時腳正懸在半空，基準就被錨在空中，
            // 之後永遠量不到足夠的行程。實例：`RunRtLoop` 的右腳全程離地 0.030~0.280 m，
            // 以第一帧的 0.272 當基準時最大行程只有 0.008 m ⇒ **整隻腳永久靜音**（實測證實）。
            //
            // 只在**尚未重新上膛**（仍是 Planted）時下修：那正是「還沒開始算這一步」的階段，
            // 把基準與行程一起拉到新的低點，等同於「腳其實是在這裡才算真的放下」。
            // 一旦進入 Lifted，基準就凍結——否則下降途中會邊走邊吃掉自己的行程。
            if (!_isLifted && !discontinuity && clearance < _clearanceAtLastPlant)
            {
                _clearanceAtLastPlant = clearance;
                _maxClearanceSinceLastPlant = clearance;
            }

            float lift = _maxClearanceSinceLastPlant - _clearanceAtLastPlant;

            // === ① Planted → Lifted：必須相對地面真的抬起來 ===
            //     per-foot、純高度判準、無時間閘。收步 settling 與骨骼微震擋在這裡。
            if (!_isLifted)
            {
                if (lift < settings.ReArmLift)
                {
                    reason = FootstepSuppressReason.NotReArmed;
                    return FootstepKind.None;
                }
                _isLifted = true;
            }

            // === ② Schmitt 上膛：腳確實在快速下降 ===
            if (verticalSpeed <= -settings.ArmDescentSpeed)
            {
                _isArmed = true;
                reason = FootstepSuppressReason.Descending;
                return FootstepKind.None;
            }

            if (!_isArmed)
            {
                reason = FootstepSuppressReason.NeverArmed;
                return FootstepKind.None;
            }

            if (verticalSpeed < -settings.FireDescentSpeed)
            {
                reason = FootstepSuppressReason.Descending;
                return FootstepKind.None;
            }

            // === ③ 下降結束＝落腳候選。三道資格檢查。===
            // ⚠️ 未通過時**刻意保留上膛狀態**：這一段下降確實發生過，只是還沒到真正的接觸點。
            //    清掉上膛會讓稍後真正踩地的那一帧無法擊發（舊版把真實踩地吃掉的成因之一）。

            if (discontinuity)
            {
                reason = FootstepSuppressReason.GroundDiscontinuity;
                return FootstepKind.None;
            }

            if (clearance > settings.ContactClearance)
            {
                reason = FootstepSuppressReason.AboveContactBand;
                return FootstepKind.None;
            }

            if (horizontalSpeed > settings.PlantHorizontalSpeedMax)
            {
                reason = FootstepSuppressReason.StillSwinging;
                return FootstepKind.None;
            }

            // === ④ 擊發 ===
            FootstepKind kind = lift >= settings.NormalStepLift ? FootstepKind.Normal : FootstepKind.Shuffle;
            MarkPlanted(clearance);
            reason = FootstepSuppressReason.Fired;
            return kind;
        }

        /// <summary>
        /// 強制進入 Planted 並清除上膛——落地（<c>JustLanded</c>）時由偵測器呼叫。
        ///
        /// **為什麼落地要強制收束**：落地是更高階的語意，已由 Landing 音效表達。
        /// 若只是「本帧不回報」，雙腳的上膛狀態會留到下一帧，導致落地後立刻多播一兩聲普通腳步
        /// （`ThirdPersonFootsteps` 為此要把雙腳一起設 cooldown，我們改成收束相位，不引入時間閘）。
        /// 行程基準同時重設 ⇒ 落地之後的下一步必須是**真正重新抬腳**才算數。
        /// </summary>
        public void ForcePlanted()
        {
            MarkPlanted(_lastClearance);
        }

        private void MarkPlanted(float clearance)
        {
            _isArmed = false;
            _isLifted = false;
            _clearanceAtLastPlant = clearance;
            _maxClearanceSinceLastPlant = clearance;
        }

        private void SeedBaseline(Vector3 footPosition, float clearance, float groundReference, bool hasGround,
            float fallbackGroundReferenceY)
        {
            _previousClearance = clearance;
            _previousGroundReference = groundReference;
            _previousHadGround = hasGround;
            _previousRootPlaneY = fallbackGroundReferenceY;
            _previousFootPosition = footPosition;
            _clearanceAtLastPlant = clearance;
            _maxClearanceSinceLastPlant = clearance;
            _hasPreviousSample = true;
            _isLifted = false;
            _isArmed = false;
        }
    }
}
