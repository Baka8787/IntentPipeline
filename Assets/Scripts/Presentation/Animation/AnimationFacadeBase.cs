using UnityEngine;
using System;

namespace Project.Presentation.Animation
{
    public abstract class AnimationFacadeBase : MonoBehaviour
    {
        // === 參數鍵常數 ===
        /// <summary>
        /// Locomotion 混合參數的動畫圖參數鍵。🆕（ADR-003 D4 Stage 2）由 **Locomotion model 自己**
        /// 在管線順序 3 每幀以 <see cref="SetFloat"/> 送入其平滑後的移動強度（0~1）——
        /// 「每個 model 驅動自己的參數」，Facade 維持通用 sink、不認識任何 model。
        /// 訂閱者由 Transition 資產內的 ParameterName（StringAsset，名稱須與此常數一致）自行綁定，
        /// 呼叫端與 Facade 都不需要認識具體 Mixer。
        /// </summary>
        public const string ParamMoveSpeed = "MoveSpeed";

        // === 播放控制 ===
        /// <summary>
        /// 播放指定狀態鍵的動畫。過渡時長／播放速度／起始時間全數由該鍵對應的 Transition 資產承載
        /// （v0.16 起資產為單一真相，簽名不再提供 duration 覆寫，杜絕程式碼靜默蓋掉資產設定）。
        /// </summary>
        public abstract void Play(string stateKey);

        /// <summary>播放並在結束時觸發回調（用於一次性動作如翻滾、攻擊）。過渡參數同樣由資產承載。</summary>
        public abstract void PlayWithCallback(string stateKey, Action onComplete);

        // === 層權重控制 ===
        /// <summary>設定指定層的權重（0~1），用於上半身/全身混合</summary>
        public abstract void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f);

        // === 參數同步 ===
        /// <summary>傳入連續數值參數（如移動速度），供 Mixer 做 blend 計算。訂閱關係定義在 Transition 資產端。</summary>
        public abstract void SetFloat(string key, float value);
        public abstract void SetBool(string key, bool value);

        // === IK 通道 ===
        /// <summary>
        /// 🆕（M3）開／關指定層的 Animator IK pass（OnAnimatorIK 回呼的觸發前提）。
        /// 預設 no-op：並非所有動畫後端都有 IK pass 概念；支援 IK 的實作（如 Animancer）自行覆寫。
        /// 呼叫端：FootIKController 初始化期——表現層經 Facade 觸碰動畫系統，不繞過（CLAUDE.md 依賴邊界）。
        /// </summary>
        public virtual void SetApplyAnimatorIK(int layerIndex, bool value) { }

        // === 狀態查詢 ===
        /// <summary>
        /// 查詢指定狀態鍵對應的動畫是否正在播放。
        /// 語意注意：多個狀態鍵可映射同一份 Transition 資產（如 Idle/Move → Locomotion），此時各鍵的查詢結果一致。
        /// </summary>
        public abstract bool IsPlaying(string stateKey);

        /// <summary>查詢目前主層播放進度（0~1）</summary>
        public abstract float GetNormalizedTime();
    }
}
