using System;
using UnityEngine;

namespace Project.Presentation.Footstep
{
    /// <summary>
    /// 🆕（M3.x-B）落腳偵測參數。三個數字，各自打一種雜訊。
    /// </summary>
    [Serializable]
    public class FootstepDetectionSettings
    {
        [Tooltip("上膛門檻（m/s，填正值）：腳底下降速度達到此值才視為「真的在落腳」。\n" +
                 "低於此值的移動一律不上膛——這道守的是**時間軸上的速度抖動**。")]
        public float ArmDescentSpeed = 0.35f;

        [Tooltip("擊發門檻（m/s，填正值）：上膛後，下降速度回升到慢於此值即判定落腳。\n" +
                 "⚠️ 必須明顯小於上膛門檻——兩者形成 Schmitt trigger，速度在 0 附近抖動時無法重新上膛。")]
        public float FireDescentSpeed = 0.05f;

        [Tooltip("最小垂直行程（公尺）：距上次落腳之後，腳底必須先抬高至少這個距離，下一次落腳才算數。\n" +
                 "這道守的是**空間上的微幅假動作**（腳貼在地上晃動、Idle 呼吸重心轉移）。")]
        public float MinLiftExcursion = 0.03f;
    }

    /// <summary>
    /// 🆕（M3.x-B）單腳落腳偵測的**跨帧狀態**。
    ///
    /// 刻意做成 <c>struct</c>（值型別）並由偵測器以欄位持有——與 <c>LocomotionSpeedSmoother</c> 同款：
    /// 跨帧狀態需要一個明確的擁有者，值型別讓「誰持有它」在型別層就看得見，且執行期零配置。
    ///
    /// 也刻意做成**可獨立呼叫的純推進函式**（<see cref="Advance"/> 顯式收 <c>deltaTime</c>）：
    /// 偵測邏輯因此能在 EditMode 用確定性的時間步長測試，不必依賴 <c>Time.deltaTime</c>
    /// 或真實 Animator（同 <c>FootIKController.ComputeFootWeight</c> 走純函數測試的先例）。
    ///
    /// **演算法（輪 3 裁決）**：速度雙門檻 ＋ 最小垂直行程，**不使用時間閘**——
    /// 時間閘的門檻必須依最快步頻反推，一旦估錯就會把 sprint 的真實腳步濾掉。
    /// </summary>
    public struct FootPlantTracker
    {
        private float _previousHeight;
        private float _maxHeightSinceLastPlant;
        private float _lastPlantHeight;
        private bool _hasPreviousSample;
        private bool _isArmed;
        private bool _hasPlantedOnce;

        /// <summary>
        /// 推進一帧，回傳本帧是否偵測到落腳。
        /// </summary>
        /// <param name="footBottomHeight">腳底的世界高度（＝IK goal 世界 Y − FeetBottomHeight，**pre-IK**）。</param>
        /// <param name="deltaTime">本帧時間步長。<c>&lt;= 0</c>（暫停）時整段跳過且**不推進任何狀態**——
        /// 沒有時間流逝就沒有速度可算，比照 <c>MotionDriver.IsTimeFrozen</c>。</param>
        public bool Advance(float footBottomHeight, float deltaTime, FootstepDetectionSettings settings)
        {
            if (settings == null || deltaTime <= 0f) return false;

            if (!_hasPreviousSample)
            {
                // 首次採樣：只建立基準，沒有前一帧就算不出速度。
                _previousHeight = footBottomHeight;
                _maxHeightSinceLastPlant = footBottomHeight;
                _lastPlantHeight = footBottomHeight;
                _hasPreviousSample = true;
                return false;
            }

            float verticalSpeed = (footBottomHeight - _previousHeight) / deltaTime; // 負值＝下降
            _previousHeight = footBottomHeight;

            if (footBottomHeight > _maxHeightSinceLastPlant) _maxHeightSinceLastPlant = footBottomHeight;

            // ① 上膛：腳確實在快速下降。
            if (verticalSpeed <= -settings.ArmDescentSpeed)
            {
                _isArmed = true;
                return false;
            }

            // ② 尚未上膛，或下降還沒慢下來 → 不擊發。
            //    ⚠️ 兩個門檻不同值正是 Schmitt trigger 的本體：速度在 0 附近抖動時，
            //       既達不到上膛門檻、也就無法重複擊發。
            if (!_isArmed || verticalSpeed < -settings.FireDescentSpeed) return false;

            // ③ 下降停止＝落腳候選。擊發前先看空間行程。
            _isArmed = false;

            bool liftedEnough = !_hasPlantedOnce ||
                                (_maxHeightSinceLastPlant - _lastPlantHeight) >= settings.MinLiftExcursion;

            // 不論成立與否，這一次的「抬腳額度」都已用掉：下一步的行程要重新累積。
            _maxHeightSinceLastPlant = footBottomHeight;

            if (!liftedEnough) return false;

            _lastPlantHeight = footBottomHeight;
            _hasPlantedOnce = true;
            return true;
        }
    }
}
