using UnityEngine;

namespace Project.Presentation.Motion
{
    /// <summary>
    /// 🆕 v0.7：動畫結束瞬間的落地腳相，供上層動作銜接（步相對齊）判斷該用哪隻腳起步。
    /// </summary>
    public enum FootPhase
    {
        LeftFootDown,
        RightFootDown
    }

    [CreateAssetMenu(fileName = "MotionBakeData", menuName = "Project/Motion/BakeData")]
    public class MotionBakeData : ScriptableObject
    {
        [Header("來源資訊")]
        public AnimationClip SourceClip;
        public float SampleRate = 60f;

        [Header("物理特徵曲線 (X軸為實際時間/秒)")]
        [Tooltip("瞬時速度曲線 (m/s)")]
        public AnimationCurve SpeedCurve;

        [Tooltip("連續累計偏航角曲線 (Degrees)")]
        public AnimationCurve RotationCurve;

        [Header("進階物理特徵（v0.7 新增，取自參考演算法的純數學部分）")]
        [Tooltip("旋轉在第幾秒就已經收斂到終值附近（依容忍度演算法計算）。" +
                 "超過這個時間點後，剩餘的角度變化視為動畫尾段的抖動雜訊，" +
                 "下游狀態可以用這個時間點提早停止套用 deltaYaw，避免收尾抖動被誤判成有意義的轉向。")]
        public float RotationFinishedTime;

        [Tooltip("動畫播放結束的瞬間，左右腳何者落地，供動作銜接（例如翻滾接跑步時要接對步相）使用")]
        public FootPhase EndPhase;

        [Tooltip("連續腳相曲線（🆕 Foot Phase Curve）：值 = 左腳世界Y − 右腳世界Y。<0 左腳觸地、>0 右腳觸地" +
                 "（與 EndPhase 符號一致）。零交越＝換腳時刻，供 Start/Stop 選腳別與未來 Footstep／相位同步。")]
        public AnimationCurve FootPhaseCurve;

        [Tooltip("整段動畫的總位移方向，換算成『動畫開始那一刻』角色本地座標系下的方向向量，" +
                 "可直接餵給 Blend Tree 的 X/Z 方向參數；若位移量太小或方向太接近正前方（死區內），視為 Vector3.zero（原地動作）。")]
        public Vector3 TargetLocalDirection;

        [Header("自動化特徵分析（Feature Analysis Stage 自動提取，跳躍動畫適用）")]

        /// <summary>
        /// 起跳前搖時間（秒）。以「世界空間相對足跡」偵測：雙腳世界高度同時超過「自身 Rest Pose 基線＋容忍度」
        /// 且通過持續騰空驗證，經子影格線性插值後的精確離地時刻；此時間點之前屬於預備/蓄力姿勢。
        /// 非跳躍動畫（偵測不到離地）安全退化為 0。
        /// </summary>
        [Tooltip("起跳前搖（秒）：雙腳同時離地（相對各自 Rest Pose 基線）的精確時刻；之前屬預備/蓄力。非跳躍動畫為 0。")]
        public float AutoTakeoffDelay;

        /// <summary>
        /// 最高點高度 h_max（公尺）。根節點世界空間 Y 相對「起跳時刻」基準的最大上升量（於起跳→落地窗內掃描）。
        /// </summary>
        [Tooltip("最高點高度 h_max（公尺）：起跳→落地窗內，根節點 Y 相對起跳時刻的最大上升量。")]
        public float AutoApexHeight;

        /// <summary>
        /// 滯空時間 t_air（秒）。雙 Pass 精確量測：起跳離地 → 首次真實落地，雙端皆經子影格線性插值。
        /// 找不到落地（jump-loop／跳上高台等不對稱拋物線）時為 0，明示未量測。
        /// </summary>
        [Tooltip("滯空時間 t_air（秒）：起跳→首次落地的精確量測（子影格插值）。找不到落地時為 0。")]
        public float AutoAirTime;

        /// <summary>
        /// 逆向推導的完美重力常數（正值，公尺/秒²）。由拋體運動 g = 8·h_max / t_air² 反推。
        /// 非跳躍動畫、找不到落地、滯空過短或最高點過低時安全退化為標準重力 9.81。
        /// </summary>
        [Tooltip("逆推重力：g = 8·h_max / t_air²。非跳躍/無落地/滯空過短/高度過低時退化為 9.81。")]
        public float AutoCalculatedGravity = 9.81f;

        /// <summary>
        /// 代表移動速度（公尺/秒）：<see cref="SpeedCurve"/> 的平均瞬時速度，於烘焙時算好存檔。
        /// 用途——把「動畫天生跑多快」變成可被 Config／MotionDriver 引用的**資料真相**：
        /// 例如最高速 clip（Fast Run）的此值 = gameplay 滿速，讓腳步視覺與位移速度一致、根除滑步；
        /// Locomotion Mixer 門檻亦可由各段此值正規化推導（dev-spec §3.2 資料流規範）。
        /// 語意為整段平均；loop locomotion clip（Walk／Run）為穩態，平均即代表速度。
        /// 舊資產（此欄為 0、未重烘焙）由 <see cref="GetRepresentativeSpeed"/> 即時從 SpeedCurve 回退計算，
        /// 因此無需為了取得此值強制立即重烘焙全部資產。
        /// </summary>
        [Tooltip("代表移動速度 (m/s)：SpeedCurve 平均值，烘焙時寫入。最高速 clip 的此值可作 MotionDriver 滿速來源／Mixer 門檻推導。")]
        public float AutoAverageSpeed;

        // 便利擴充：取得動畫總長度
        public float Duration => SourceClip != null ? SourceClip.length : 0f;

        /// <summary>
        /// 取得特定時間點的理論「瞬時速度」
        /// </summary>
        public float GetSpeedAt(float time)
        {
            if (SpeedCurve == null || SpeedCurve.length == 0) return 0f;
            return SpeedCurve.Evaluate(time);
        }

        /// <summary>
        /// 取得特定時間點的理論「累計偏航角度」
        /// </summary>
        public float GetRotationAt(float time)
        {
            if (RotationCurve == null || RotationCurve.length == 0) return 0f;
            return RotationCurve.Evaluate(time);
        }

        /// <summary>
        /// 🆕 便利方法：詢問「此刻旋轉是否已經收斂完成」。
        /// 可用於狀態內部判斷是否該停止套用 deltaYaw，或是否可以提早允許自然過渡。
        /// </summary>
        public bool IsRotationFinished(float time) => time >= RotationFinishedTime;

        /// <summary>
        /// 🆕 查詢某時刻哪隻腳觸地（連續腳相）。<see cref="FootPhaseCurve"/> 缺（舊資產未重烘焙）時退回單點
        /// <see cref="EndPhase"/>。符號約定：曲線值 &lt; 0 = 左腳觸地、&gt; 0 = 右腳觸地
        /// （與烘焙時「LeftFoot 較低判 LeftFootDown」一致）。
        /// </summary>
        public FootPhase GetFootPhaseAt(float time)
        {
            if (FootPhaseCurve == null || FootPhaseCurve.length == 0) return EndPhase;
            return FootPhaseCurve.Evaluate(time) < 0f ? FootPhase.LeftFootDown : FootPhase.RightFootDown;
        }

        /// <summary>
        /// 取得代表移動速度（公尺/秒），供「動畫數據 → 配置」資料流使用（dev-spec §3.2）。
        /// 優先回傳烘焙時存檔的 <see cref="AutoAverageSpeed"/>；若為 0（舊資產未重烘焙）則即時從
        /// <see cref="SpeedCurve"/> 計算平均值作安全回退——確保現有資產無需立即重烘焙也能被
        /// MotionDriver 速度來源／Mixer 門檻推導引用。回傳 0 代表此 clip 無有效速度資料（非移動動畫）。
        /// </summary>
        public float GetRepresentativeSpeed()
        {
            if (AutoAverageSpeed > 0f) return AutoAverageSpeed;
            return ComputeAverageSpeed(SpeedCurve);
        }

        /// <summary>
        /// 計算速度曲線的平均瞬時速度（對關鍵影格值取算術平均）。烘焙採等距取樣、關鍵影格均勻分布，
        /// 故算術平均即時間平均。曲線為 null／空時回傳 0。供烘焙工具寫入 <see cref="AutoAverageSpeed"/>
        /// 與執行期回退共用同一套定義，杜絕兩處計算不一致。
        /// </summary>
        public static float ComputeAverageSpeed(AnimationCurve speedCurve)
        {
            if (speedCurve == null || speedCurve.length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < speedCurve.length; i++) sum += speedCurve[i].value;
            return sum / speedCurve.length;
        }
    }
}