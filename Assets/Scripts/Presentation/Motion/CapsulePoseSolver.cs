using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 一個 landmark（身體參考點）的當幀取樣。
    ///
    /// <para><b>語意</b>：這個關節周圍有一顆半徑 <see cref="FleshRadius"/> 的「肉體球」。
    /// 我們要求那顆球落在膠囊裡，而不是要求關節點落在膠囊裡——
    /// 關節點在膠囊內**不代表**模型表面也在（`docs/27` §12.2：Head 骨骼只超出 0.085，
    /// Head 網格超出 0.271，差 3 倍以上）。</para>
    /// </summary>
    public readonly struct CapsuleLandmarkSample
    {
        /// <summary>關節位置，**Root local 空間**（與 <c>CharacterController.center</c> 同一個座標系）。</summary>
        public readonly Vector3 LocalPosition;

        /// <summary>該關節代表的肉體半徑（公尺）。離線量測、authored；見 <see cref="CapsulePoseProfileSO"/>。</summary>
        public readonly float FleshRadius;

        public CapsuleLandmarkSample(Vector3 localPosition, float fleshRadius)
        {
            LocalPosition = localPosition;
            FleshRadius = fleshRadius;
        }
    }

    /// <summary>
    /// **Authored（作者設定的）膠囊幾何**——不含任何 runtime 偏移。
    ///
    /// ⚠️ 這是 solver 的基準座標系：解出來的 offset 是「相對 <see cref="BaseCenter"/> 的水平位移」，
    /// 不是「相對上一幀 center 的增量」。⇒ 誤差不累積。
    /// </summary>
    public readonly struct AuthoredCapsuleGeometry
    {
        public readonly Vector3 BaseCenter;
        public readonly float Radius;
        public readonly float Height;

        public AuthoredCapsuleGeometry(Vector3 baseCenter, float radius, float height)
        {
            BaseCenter = baseCenter;
            Radius = radius;
            Height = height;
        }

        /// <summary>Unity 的 <c>CharacterController</c> 在 height &lt; 2·radius 時會被夾成一顆球。</summary>
        public float HalfHeight => Mathf.Max(Height * 0.5f, Radius);

        /// <summary>上半球的球心高度。**超過這個高度，水平可用半徑就不再是 <see cref="Radius"/>。**</summary>
        public float TopSphereCenterY => BaseCenter.y + (HalfHeight - Radius);

        /// <summary>下半球的球心高度。</summary>
        public float BottomSphereCenterY => BaseCenter.y - (HalfHeight - Radius);
    }

    /// <summary>Solver 這一幀的結論分類（只供診斷／debug，runtime 不據此分支）。</summary>
    public enum CapsulePoseSolveStatus
    {
        /// <summary>輸入不合法（NaN／半徑 ≤ 0／landmark 數為 0）⇒ 偏移 0。</summary>
        InvalidInput = 0,

        /// <summary>所有 landmark 本來就在膠囊內 ⇒ 不需要偏移。</summary>
        Neutral = 1,

        /// <summary>解出了一個能覆蓋全部（未被略過的）landmark 的偏移。</summary>
        Corrected = 2,

        /// <summary>
        /// 約束互相衝突，找不到能同時覆蓋全部 landmark 的位置 ⇒ 回傳迭代後的折衷點。
        /// <b>這是「膠囊尺寸不夠」的訊號，不是 solver 失敗</b>——加大偏移救不了，要動 radius／height。
        /// </summary>
        Infeasible = 3,
    }

    /// <summary>Solver 的完整輸出。<c>Offset</c> 之外都是診斷用，runtime 只吃 <c>Offset</c>。</summary>
    public readonly struct CapsulePoseSolveResult
    {
        /// <summary>解出的水平偏移（Root local 的 x／z；**y 恆為 0，本 solver 永不碰高度**）。</summary>
        public readonly Vector2 Offset;

        public readonly CapsulePoseSolveStatus Status;

        /// <summary>套用 <see cref="Offset"/> 之後仍然露在膠囊外的最大量（公尺）。≤0 ＝ 全部包住。</summary>
        public readonly float Residual;

        /// <summary>
        /// 因為「**水平移動再多也救不了**」而被略過的 landmark 數：
        /// 肉體半徑 ≥ 膠囊半徑，或關節高到／低到該高度的可用半徑已小於肉體半徑。
        /// &gt;0 ⇒ 這是 radius／height 的問題，記下來但不要靠加大偏移去追。
        /// </summary>
        public readonly int SkippedLandmarks;

        public CapsulePoseSolveResult(
            Vector2 offset, CapsulePoseSolveStatus status, float residual, int skippedLandmarks)
        {
            Offset = offset;
            Status = status;
            Residual = residual;
            SkippedLandmarks = skippedLandmarks;
        }

        public static CapsulePoseSolveResult Invalid =>
            new CapsulePoseSolveResult(Vector2.zero, CapsulePoseSolveStatus.InvalidInput, 0f, 0);
    }

    /// <summary>
    /// **姿勢驅動的膠囊中心修正量求解器**（`docs/27` §12.4；`docs/28` 是白話教學版）。
    ///
    /// <para><b>它回答的問題</b></para>
    /// 「離 authored center **最近**的那個水平位置在哪裡，使得 Head／Neck／UpperChest／Chest
    /// 所代表的肉體球都落進膠囊？」——最近，所以是**最小必要修正**，不是「能推多遠就推多遠」。
    ///
    /// <para><b>⛔ 它刻意不知道的事</b></para>
    /// <list type="bullet">
    /// <item><c>MoveDirection</c>／速度／gait ——舊模型用移動方向猜姿勢，在純側移時會把膠囊
    /// 推向**反方向**（`docs/27` §11.3 ③ 實測）。姿勢是動畫的力學，不是移動方向的函數。</item>
    /// <item>手、腳、武器 ——揮出去的手本來就不該被膠囊包住（包了會在門框卡住，`docs/27` §4.2）。</item>
    /// <item>場景幾何 ——貼牆限制是**呼叫端**的 collision-aware clamp 的事。
    /// 本函式維持純運算 ⇒ EditMode 可完整驗證，不需要場景、不需要 Play。</item>
    /// <item>全身最大外擴點 ／ argmax ——見下方「為什麼不會跳變」。</item>
    /// </list>
    ///
    /// <para><b>數學：凸集投影（convex projection）</b></para>
    /// 「半徑 <c>r_i</c> 的球完全落在膠囊內」這個條件，在膠囊只平移不變形的前提下，
    /// 對水平中心 <c>c</c> 而言是一個**圓盤約束**：
    /// <code>
    /// allow_i = R − r_i                                  // 該球在膠囊裡還剩多少活動半徑
    /// dy_i    = 關節高度超出上半球中心（或低於下半球中心）的量
    /// ρ_i     = sqrt(allow_i² − dy_i²)                    // 球心在**這個高度**的可行半徑
    /// 約束     |q_i − c| ≤ ρ_i                            // q_i ＝ 關節的水平座標
    /// </code>
    /// 每個約束都是一個圓盤，交集是凸集；**required offset ＝ 原點在該交集上的投影**。
    ///
    /// <para><b>⭐ 為什麼這不是 argmax、為什麼不會跳變</b></para>
    /// 直覺做法是「找最外面那個點，往它推」——那是 <c>argmax</c>，當「最外面的是誰」換人時
    /// 輸出會**不連續跳一下**（本 repo 已有這類缺陷紀錄：`docs/27` §4.3）。
    /// 凸集投影不同：即使 active constraint（正在頂住的那個約束）換人，**投影點本身是連續移動的**。
    /// 實測 `docs/27` §12.4：每幀變化量 ≤ 0.0124 m（≈0.8 m/s），平滑器綽綽有餘。
    ///
    /// <para><b>演算法：Dykstra 交替投影</b></para>
    /// 對少數幾個圓盤，Dykstra 演算法收斂很快且**確定性**（固定迭代數、無隨機、無 <c>Physics</c> 查詢）。
    /// 修正量緩衝區是 inline struct ⇒ **零配置**。
    /// </summary>
    public static class CapsulePoseSolver
    {
        /// <summary>單次求解的最大 landmark 數。超過的會被忽略（診斷計為 skipped）。</summary>
        public const int MaxLandmarks = 8;

        /// <summary>
        /// 預設迭代數。`docs/27` §12 的離線研究用 300 次；實測 4 個圓盤在 24 次內殘差已 &lt; 1e-4，
        /// 由 <c>CP10</c> 測試綁住（拿 24 次與 300 次比對）。
        /// </summary>
        public const int DefaultIterations = 24;

        private const float FeasibilityTolerance = 1e-4f;
        private const float NeutralOffsetTolerance = 1e-5f;

        /// <summary>
        /// 求解最小必要水平偏移。**純函式**：相同輸入必得相同輸出，不讀時間、不讀場景、不配置。
        /// </summary>
        /// <param name="samples">landmark 取樣陣列（呼叫端擁有，避免每幀配置）。</param>
        /// <param name="count">陣列中有效的元素數。</param>
        /// <param name="capsule">authored 膠囊幾何（**不含** runtime 偏移）。</param>
        /// <param name="iterations">Dykstra 迭代數；&lt;1 時取 <see cref="DefaultIterations"/>。</param>
        public static CapsulePoseSolveResult Solve(
            CapsuleLandmarkSample[] samples,
            int count,
            in AuthoredCapsuleGeometry capsule,
            int iterations = DefaultIterations)
        {
            if (samples == null || count <= 0) return CapsulePoseSolveResult.Invalid;
            if (!IsFinite(capsule.Radius) || !IsFinite(capsule.Height) || !IsFinite(capsule.BaseCenter))
                return CapsulePoseSolveResult.Invalid;
            if (capsule.Radius <= 0f) return CapsulePoseSolveResult.Invalid;
            if (iterations < 1) iterations = DefaultIterations;
            if (count > MaxLandmarks) count = MaxLandmarks;

            float topY = capsule.TopSphereCenterY;
            float bottomY = capsule.BottomSphereCenterY;

            var q = default(Vec2x8);
            var rho = default(Floatx8);
            int active = 0;
            int skipped = 0;

            for (int i = 0; i < count; i++)
            {
                CapsuleLandmarkSample s = samples[i];
                if (!IsFinite(s.LocalPosition) || !IsFinite(s.FleshRadius)) { skipped++; continue; }

                float allow = capsule.Radius - Mathf.Max(0f, s.FleshRadius);
                if (allow <= 0f) { skipped++; continue; }   // 肉比膠囊還粗：水平移動救不了

                // 膠囊在上／下半球內會變窄；超出半球中心多少，可用半徑就縮多少。
                float dy = s.LocalPosition.y > topY
                    ? s.LocalPosition.y - topY
                    : (s.LocalPosition.y < bottomY ? bottomY - s.LocalPosition.y : 0f);

                float rhoSqr = allow * allow - dy * dy;
                if (rhoSqr <= 0f) { skipped++; continue; }  // 高到（或低到）該高度已經塞不下這顆球

                q.Set(active, new Vector2(s.LocalPosition.x, s.LocalPosition.z));
                rho.Set(active, Mathf.Sqrt(rhoSqr));
                active++;
            }

            if (active == 0)
            {
                // 沒有任何可用約束 ⇒ 沒有根據去移動膠囊。維持 authored 位置是唯一安全答案。
                return new CapsulePoseSolveResult(
                    Vector2.zero, CapsulePoseSolveStatus.Neutral, 0f, skipped);
            }

            // ── Dykstra 交替投影：把原點投影到 ∩D(q_i, ρ_i) ────────────────────────
            Vector2 x = Vector2.zero;
            var correction = default(Vec2x8);

            for (int k = 0; k < iterations; k++)
            {
                for (int i = 0; i < active; i++)
                {
                    Vector2 y = x + correction.Get(i);
                    Vector2 projected = ProjectOntoDisc(y, q.Get(i), rho.Get(i));
                    correction.Set(i, y - projected);
                    x = projected;
                }
            }

            if (!IsFinite(x)) return CapsulePoseSolveResult.Invalid;

            float residual = float.NegativeInfinity;
            for (int i = 0; i < active; i++)
            {
                float over = (x - q.Get(i)).magnitude - rho.Get(i);
                if (over > residual) residual = over;
            }

            CapsulePoseSolveStatus status;
            if (residual > FeasibilityTolerance) status = CapsulePoseSolveStatus.Infeasible;
            else if (x.sqrMagnitude <= NeutralOffsetTolerance * NeutralOffsetTolerance)
                status = CapsulePoseSolveStatus.Neutral;
            else status = CapsulePoseSolveStatus.Corrected;

            return new CapsulePoseSolveResult(x, status, residual, skipped);
        }

        /// <summary>
        /// 把 <paramref name="offset"/> 夾到安全上限 <paramref name="maxOffset"/> 之內。
        /// **只縮長度，不轉方向**——方向是姿勢決定的，上限只能少給、不能改變意圖。
        /// </summary>
        public static Vector2 ApplySafetyCap(Vector2 offset, float maxOffset)
        {
            if (!IsFinite(offset)) return Vector2.zero;
            if (!IsFinite(maxOffset) || maxOffset <= 0f) return Vector2.zero;
            float magnitude = offset.magnitude;
            if (magnitude <= maxOffset) return offset;
            return offset * (maxOffset / magnitude);
        }

        private static Vector2 ProjectOntoDisc(Vector2 point, Vector2 center, float radius)
        {
            Vector2 delta = point - center;
            float distance = delta.magnitude;
            if (distance <= radius) return point;
            if (distance <= 1e-9f) return center;   // 退化：點與圓心重合而半徑為 0
            return center + delta * (radius / distance);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool IsFinite(Vector2 v) => IsFinite(v.x) && IsFinite(v.y);
        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        // ─────────────────────────────────────────────────────────────────────
        // 固定容量的 inline 緩衝區。
        // ⚠️ 用 struct 而不是 `new Vector2[n]`：solver 每幀被呼叫，陣列配置會直接破壞零 GC。
        //    `stackalloc`／`Span` 也可以，但本 repo 目前沒有任何 Span 使用，
        //    這裡不為了少打幾行字引入一個新的語言特性依賴。
        // ─────────────────────────────────────────────────────────────────────
        private struct Vec2x8
        {
            private Vector2 _0, _1, _2, _3, _4, _5, _6, _7;

            public Vector2 Get(int i)
            {
                switch (i)
                {
                    case 0: return _0;
                    case 1: return _1;
                    case 2: return _2;
                    case 3: return _3;
                    case 4: return _4;
                    case 5: return _5;
                    case 6: return _6;
                    default: return _7;
                }
            }

            public void Set(int i, Vector2 v)
            {
                switch (i)
                {
                    case 0: _0 = v; break;
                    case 1: _1 = v; break;
                    case 2: _2 = v; break;
                    case 3: _3 = v; break;
                    case 4: _4 = v; break;
                    case 5: _5 = v; break;
                    case 6: _6 = v; break;
                    default: _7 = v; break;
                }
            }
        }

        private struct Floatx8
        {
            private float _0, _1, _2, _3, _4, _5, _6, _7;

            public float Get(int i)
            {
                switch (i)
                {
                    case 0: return _0;
                    case 1: return _1;
                    case 2: return _2;
                    case 3: return _3;
                    case 4: return _4;
                    case 5: return _5;
                    case 6: return _6;
                    default: return _7;
                }
            }

            public void Set(int i, float v)
            {
                switch (i)
                {
                    case 0: _0 = v; break;
                    case 1: _1 = v; break;
                    case 2: _2 = v; break;
                    case 3: _3 = v; break;
                    case 4: _4 = v; break;
                    case 5: _5 = v; break;
                    case 6: _6 = v; break;
                    default: _7 = v; break;
                }
            }
        }
    }
}
