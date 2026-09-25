using UnityEngine;

namespace Project.Presentation.IK
{
    /// <summary>站直原型的姿勢階段（docs/05 §3.5.4.6）：Moving → Settling → Upright → Return → Moving。</summary>
    public enum PosturePhase
    {
        Moving = 0,
        Settling = 1,
        Upright = 2,
        Return = 3,
    }

    /// <summary>
    /// 🧪（站直原型）刻意跨帧保存的姿勢狀態（只存數值）。任何失效（IK 不允許、pose 無效、元件停用、瞬移、原型關閉）一律 <see cref="Reset"/>。
    /// </summary>
    public struct PostureState
    {
        public PosturePhase Phase;

        /// <summary>骨盆偏移的速度（m/s）：Settling／Upright／Return 共用同一個，跨轉移延續。Moving 期間不使用。</summary>
        public float Velocity;

        /// <summary>連續「靜止」的帧數（Moving 與 Return 期間累計，達門檻進入 Settling）。</summary>
        public int StillFrames;

        /// <summary>上一帧實際寫出的骨盆偏移——進入 Settling 時用來給速度種子（位移 ／ dt）。</summary>
        public float PreviousOutput;
        public bool HasPreviousOutput;

        // 觀測用（探針讀取；每帧覆寫，決策不讀）。
        public float DebugEnvelope;
        public float DebugPreferred;
        public float DebugHardLimit;

        public void Reset() => this = default;
    }

    /// <summary>本帧給姿勢狀態機的輸入（全部由 FootIKController 在 T3 解完兩腳之後準備好）。</summary>
    public struct PostureInputs
    {
        /// <summary>E：既有參考面包絡的目標（移動中照舊）。</summary>
        public float Envelope;

        /// <summary>R：兩腳都以偏好伸展比搆到本帧 T3 目標的最高骨盆偏移（≤ 0）。</summary>
        public float Preferred;

        /// <summary>C：支撐加權、完全伸直的硬性可達上限（≤ 0）——唯一允許立即下降的安全夾。</summary>
        public float HardLimit;

        /// <summary>本帧是否「靜止」：沒有移動意圖、root 與兩隻動畫腳都慢、兩腳都有即時接觸。</summary>
        public bool Still;
    }

    /// <summary>
    /// 🧪（站直原型，docs/05 §3.5.4.6）純函數：單腳可達上限與姿勢狀態機。不發物理查詢、不讀 Unity 時間。
    /// </summary>
    public static class StandingPostureSolver
    {
        /// <summary>
        /// 讓髖（加上骨盆偏移 p 後）到腳踝目標的距離 ＝ <paramref name="reach"/> 的最高 p。
        /// 水平距離已 ≥ reach（任何垂直位置都搆不到）⇒ 延續到「垂直最近」的姿勢 p ＝ 目標 y − 髖 y；
        /// 兩支在 h ＝ reach 處相等 ⇒ 對輸入連續（Codex：不得跳到固定下限）。
        /// </summary>
        public static float LegBound(Vector3 hip, Vector3 ankleTarget, float reach)
        {
            float dx = hip.x - ankleTarget.x, dz = hip.z - ankleTarget.z;
            float h2 = dx * dx + dz * dz;
            float r2 = reach * reach;
            float vertical = h2 >= r2 ? 0f : Mathf.Sqrt(r2 - h2);
            return vertical - (hip.y - ankleTarget.y);
        }

        /// <summary>臨界阻尼彈簧一步（半隱式；與離線模擬相同的遞推）。</summary>
        public static void SpringStep(ref float position, ref float velocity, float target, float omega, float dt)
        {
            float acceleration = omega * omega * (target - position) - 2f * omega * velocity;
            velocity += acceleration * dt;
            position += velocity * dt;
        }

        /// <summary>
        /// 一帧的狀態轉移與骨盆輸出。<paramref name="pelvis"/> 進來是上一帧的輸出、出去是本帧的輸出。
        /// - Moving：與今天完全相同（MoveTowards 到包絡，速度 <paramref name="movingSpeed"/>）；靜止滿 <paramref name="settleFrames"/> 帧 ⇒ Settling（以上一帧位移給速度種子）。
        /// - Settling／Upright：目標 ＝ min(max(E, min(R, 0)), C)，彈簧 <paramref name="riseOmega"/>；超過 C 立即夾住並把向上的速度歸零。
        ///   |p − 目標| &lt; <paramref name="handbackTolerance"/> ⇒ 標記 Upright（動態相同）。不靜止 ⇒ 立即 Return（速度延續）。
        /// - Return：彈簧 <paramref name="returnOmega"/> 回 E；p ≤ C（投影速度）；
        ///   |p − E| &lt; 容差且 |v| &lt; <paramref name="handbackSpeed"/> ⇒ 交回 Moving。靜止滿門檻 ⇒ 直接回 Settling。
        /// <paramref name="dt"/> ≤ 0 ⇒ 什麼都不推進。
        /// </summary>
        public static void Step(ref PostureState state, in PostureInputs input, float dt, ref float pelvis,
            float movingSpeed, int settleFrames, float riseOmega, float returnOmega, float handbackTolerance, float handbackSpeed)
        {
            if (dt <= 0f) return;

            float start = pelvis;
            state.StillFrames = input.Still ? state.StillFrames + 1 : 0;

            // --- 轉移（先於動態：本帧就用新狀態的動態）---
            switch (state.Phase)
            {
                case PosturePhase.Moving:
                case PosturePhase.Return:
                    if (state.StillFrames >= settleFrames)
                    {
                        if (state.Phase == PosturePhase.Moving)
                            state.Velocity = state.HasPreviousOutput ? (pelvis - state.PreviousOutput) / dt : 0f;
                        state.Phase = PosturePhase.Settling;
                    }
                    break;
                case PosturePhase.Settling:
                case PosturePhase.Upright:
                    if (!input.Still) state.Phase = PosturePhase.Return;
                    break;
            }

            // --- 動態 ---
            switch (state.Phase)
            {
                case PosturePhase.Moving:
                    pelvis = Mathf.MoveTowards(pelvis, input.Envelope, movingSpeed * dt);
                    state.Velocity = 0f;
                    break;

                case PosturePhase.Settling:
                case PosturePhase.Upright:
                {
                    float target = Mathf.Min(Mathf.Max(input.Envelope, Mathf.Min(input.Preferred, 0f)), input.HardLimit);
                    SpringStep(ref pelvis, ref state.Velocity, target, riseOmega, dt);
                    if (pelvis > input.HardLimit)
                    {
                        pelvis = input.HardLimit;
                        if (state.Velocity > 0f) state.Velocity = 0f;
                    }
                    if (state.Phase == PosturePhase.Settling && Mathf.Abs(pelvis - target) < handbackTolerance)
                        state.Phase = PosturePhase.Upright;
                    break;
                }

                case PosturePhase.Return:
                    SpringStep(ref pelvis, ref state.Velocity, input.Envelope, returnOmega, dt);
                    if (pelvis > input.HardLimit)
                    {
                        pelvis = input.HardLimit;
                        if (state.Velocity > 0f) state.Velocity = 0f;
                    }
                    // ⛔ 不夾 p ≥ E：E 是包絡的原始目標，會跳（例如樓梯出口 −0.35 → 0）；夾它等於跟著跳。臨界阻尼彈簧本身不越過目標。
                    if (Mathf.Abs(pelvis - input.Envelope) < handbackTolerance && Mathf.Abs(state.Velocity) < handbackSpeed)
                    {
                        state.Phase = PosturePhase.Moving;
                        state.Velocity = 0f;
                    }
                    break;
            }

            state.PreviousOutput = start;
            state.HasPreviousOutput = true;
        }
    }
}
