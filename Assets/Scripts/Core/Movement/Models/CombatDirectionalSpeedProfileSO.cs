using Project.Presentation.Motion;
using UnityEngine;

namespace Project.Core.Movement
{
    /// <summary>
    /// Y Bot combat directional locomotion 的 actor-scoped 校準資料。
    ///
    /// 本資產不保存手填速度：每個方向都直接引用對應的 <see cref="MotionBakeData"/>，
    /// 並以角色既有滿速來源正規化。Mixer threshold 與實際位移因此共用同一批 bake 真相。
    /// 未指派此 profile 的角色完全沿用既有 1D locomotion 行為。
    /// </summary>
    public sealed class CombatDirectionalSpeedProfileSO : ScriptableObject
    {
        [Tooltip("角色既有 MotionDriver 滿速所引用的同一份 Bake Data；只作正規化分母，不複製速度值。")]
        [SerializeField] private MotionBakeData maximumSpeedSource;
        [Tooltip("Forward locomotion 的 Bake Data。")]
        [SerializeField] private MotionBakeData forward;
        [Tooltip("Backward locomotion 的 Bake Data。")]
        [SerializeField] private MotionBakeData backward;
        [Tooltip("Left strafe locomotion 的 Bake Data。")]
        [SerializeField] private MotionBakeData left;
        [Tooltip("Right strafe locomotion 的 Bake Data。")]
        [SerializeField] private MotionBakeData right;

        // 🆕（2026-09-15）**斜向 Bake Data（可留空）。**
        // 原始模型假設「2D mixer 只有 cardinal clip，斜向靠混合」——那對 Y Bot 成立。
        // X Bot 的 spell locomotion mixer **有真正的 45°／135° clip**，而且它們的實測速度
        // 與 cardinal 差很多（RunStrafe45 = 3.62 m/s，比前跑的 3.58 還快；Lt/Rt 只有 2.2）。
        // 用 cardinal 的 L1 模型去推斜向會嚴重低估（45° 推出 0.31，實際 clip 是 0.58）⇒ 滑步。
        // ⚠️ **四格全填才會啟用八向模型**；留空 ⇒ 沿用原本的 cardinal 模型，Y Bot 逐位元不變。
        [Header("Diagonals（可留空；四格全填才啟用八向模型）")]
        [SerializeField] private MotionBakeData forwardLeft;
        [SerializeField] private MotionBakeData forwardRight;
        [SerializeField] private MotionBakeData backwardLeft;
        [SerializeField] private MotionBakeData backwardRight;

        // 🆕（2026-09-15，使用者裁決）**動畫播放倍率的預算上限。**
        //
        // 這是本資產唯一的**設計**參數，其餘全部由 Bake Data 推導——刻意如此：
        // 「這個角色的腳頻可以被拉多快」是一個美術／手感判斷，
        // 「這顆 clip 實際走多快」是量測事實。**兩者不得混在同一個欄位裡。**
        //
        // 1.0 ＝ 完全不拉伸（＝原本行為，Y Bot 走這條）。
        // X Bot 取 1.35：側移／後退 clip 天生只有前跑的 60–62%，
        // 要它們跑到「看起來合理」的速度一定得拉，但 1.79×（舊值）會明顯踩快。
        // ⇒ 上限咬住之後，側移與後退實際落在前跑的 62–65%，**而不是任何人手填的數字**。
        [Header("Playback Budget")]
        [Tooltip("允許的最大動畫播放倍率。1 ＝ 不拉伸（維持原行為）。超過上限的方向會被砍速度而不是踩快。")]
        [SerializeField, Min(1f)] private float maxPlaybackStretch = 1f;

        private bool HasAllDiagonals =>
            forwardLeft != null && forwardRight != null &&
            backwardLeft != null && backwardRight != null;

        /// <summary>八向的原始 bake 速度（m/s）。順序＝角度順時針，0°＝正前方。</summary>
        internal readonly struct DirectionalSpeedOctant
        {
            public readonly float Forward, ForwardRight, Right, BackwardRight;
            public readonly float Backward, BackwardLeft, Left, ForwardLeft;

            public DirectionalSpeedOctant(
                float forward, float forwardRight, float right, float backwardRight,
                float backward, float backwardLeft, float left, float forwardLeft)
            {
                Forward = forward; ForwardRight = forwardRight; Right = right; BackwardRight = backwardRight;
                Backward = backward; BackwardLeft = backwardLeft; Left = left; ForwardLeft = forwardLeft;
            }

            /// <summary>依 45° 槽位取值。switch 而非陣列 ⇒ 執行期零配置。</summary>
            public float this[int slot] => slot switch
            {
                0 => Forward,
                1 => ForwardRight,
                2 => Right,
                3 => BackwardRight,
                4 => Backward,
                5 => BackwardLeft,
                6 => Left,
                _ => ForwardLeft,
            };
        }

        /// <summary>
        /// 取得指定本地方向可使用的 normalized speed 上限。
        /// Cardinal 方向直接使用該 clip 的 bake speed；中間方向落在相鄰 cardinal samples 的連線上，
        /// 讓缺少 diagonal clip 的 2D mixer 自然混合而不必補 Run 資產。
        /// </summary>
        internal float ResolveNormalizedSpeed(Vector2 localDirection)
        {
            float maximum = GetSpeed(maximumSpeedSource);
            float stretch = Mathf.Max(1f, maxPlaybackStretch);

            if (HasAllDiagonals)
            {
                var octant = new DirectionalSpeedOctant(
                    GetSpeed(forward), GetSpeed(forwardRight), GetSpeed(right), GetSpeed(backwardRight),
                    GetSpeed(backward), GetSpeed(backwardLeft), GetSpeed(left), GetSpeed(forwardLeft));
                return ResolveNormalizedSpeed(localDirection, in octant, maximum, stretch);
            }

            // Cardinal-only 路徑：把 stretch 乘進速度即可——L1 公式對速度是齊次的，
            // stretch == 1 時逐位元等同原始實作（Y Bot 不得因為這次擴充而改變任何數值）。
            return ResolveNormalizedSpeed(
                localDirection,
                GetSpeed(forward) * stretch,
                GetSpeed(backward) * stretch,
                GetSpeed(left) * stretch,
                GetSpeed(right) * stretch,
                maximum);
        }

        /// <summary>
        /// （純函數）八向角度插值。
        ///
        /// <para><b>為什麼是角度插值而不是沿用 L1 模型</b></para>
        /// L1 模型（<see cref="ResolveNormalizedSpeed(Vector2,float,float,float,float,float)"/>）回答的是
        /// 「只有 cardinal 樣本時，斜向該給多少」——它刻意給出**保守的內縮值**。
        /// 但當斜向**真的有自己的 clip**時，那個內縮就是錯的：X Bot 的 45° clip 實測 3.62 m/s，
        /// L1 模型卻會推出 0.31 normalized（≈1.96 m/s）⇒ 少了 45%，角色會明顯滑步。
        /// 有樣本就用樣本，這是本專案「Bake Data 是動畫真實位移的唯一真相」的直接後果。
        /// </summary>
        internal static float ResolveNormalizedSpeed(
            Vector2 localDirection,
            in DirectionalSpeedOctant speeds,
            float maximumSpeed,
            float stretch)
        {
            if (!IsPositiveFinite(maximumSpeed)) return 1f;
            if (localDirection.magnitude <= LocomotionSpeedSmoother.Epsilon) return 0f;

            // x ＝ 右、y ＝ 前 ⇒ Atan2(x, y) 讓 0° 落在正前方、90° 落在正右方（順時針）。
            float angle = Mathf.Atan2(localDirection.x, localDirection.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            float slot = angle / 45f;
            int lower = Mathf.FloorToInt(slot) % 8;
            int upper = (lower + 1) % 8;
            float t = slot - Mathf.Floor(slot);

            float lowerSpeed = speeds[lower];
            float upperSpeed = speeds[upper];
            if (!IsPositiveFinite(lowerSpeed) || !IsPositiveFinite(upperSpeed)) return 1f;

            float blended = Mathf.Lerp(lowerSpeed, upperSpeed, t) * Mathf.Max(1f, stretch);
            return Mathf.Clamp01(blended / maximumSpeed);
        }

        internal static float ResolveNormalizedSpeed(
            Vector2 localDirection,
            float forwardSpeed,
            float backwardSpeed,
            float leftSpeed,
            float rightSpeed,
            float maximumSpeed)
        {
            if (!IsPositiveFinite(maximumSpeed)) return 1f;

            float magnitude = localDirection.magnitude;
            if (magnitude <= LocomotionSpeedSmoother.Epsilon) return 0f;

            Vector2 direction = localDirection / magnitude;
            float xSpeed = direction.x < 0f ? leftSpeed : rightSpeed;
            float zSpeed = direction.y < 0f ? backwardSpeed : forwardSpeed;

            float denominator = 0f;
            if (Mathf.Abs(direction.x) > LocomotionSpeedSmoother.Epsilon)
            {
                if (!IsPositiveFinite(xSpeed)) return 1f;
                denominator += Mathf.Abs(direction.x) / (xSpeed / maximumSpeed);
            }

            if (Mathf.Abs(direction.y) > LocomotionSpeedSmoother.Epsilon)
            {
                if (!IsPositiveFinite(zSpeed)) return 1f;
                denominator += Mathf.Abs(direction.y) / (zSpeed / maximumSpeed);
            }

            if (!IsPositiveFinite(denominator)) return 1f;
            return Mathf.Clamp01(1f / denominator);
        }

        private static float GetSpeed(MotionBakeData bakeData)
            => bakeData != null ? bakeData.GetRepresentativeSpeed() : 0f;

        private static bool IsPositiveFinite(float value)
            => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
