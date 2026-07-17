using UnityEngine;
using System;

namespace Project.Presentation.Animation
{
    public abstract class AnimationFacadeBase : MonoBehaviour
    {
        // === 參數鍵常數 ===
        /// <summary>
        /// Locomotion 混合參數的動畫圖參數鍵。管線順序 5 每幀以 <see cref="SetFloat"/> 送入黑板
        /// MoveSpeed（0~1 輸入強度）；訂閱者由 Transition 資產內的 ParameterName（StringAsset，
        /// 名稱須與此常數一致）自行綁定，呼叫端與 Facade 都不需要認識具體 Mixer。
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
