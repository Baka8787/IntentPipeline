using UnityEngine;
using System;

namespace Project.Presentation.Animation
{
    public abstract class AnimationFacadeBase : MonoBehaviour
    {
        // === 播放控制 ===
        /// <summary>播放指定狀態鍵的動畫，帶過渡時間</summary>
        public abstract void Play(string stateKey, float transitionDuration = 0.15f);

        /// <summary>播放並在結束時觸發回調（用於一次性動作如翻滾、攻擊）</summary>
        public abstract void PlayWithCallback(string stateKey, Action onComplete, float transitionDuration = 0.1f);

        // === 層權重控制 ===
        /// <summary>設定指定層的權重（0~1），用於上半身/全身混合</summary>
        public abstract void SetLayerWeight(int layerIndex, float weight, float transitionDuration = 0.1f);

        // === 參數同步 ===
        /// <summary>傳入連續數值參數（如移動速度），供 Mixer 做 blend 計算</summary>
        public abstract void SetFloat(string key, float value);
        public abstract void SetBool(string key, bool value);

        // === 狀態查詢 ===
        /// <summary>查詢目前主層是否正在播放指定狀態鍵</summary>
        public abstract bool IsPlaying(string stateKey);

        /// <summary>查詢目前主層播放進度（0~1）</summary>
        public abstract float GetNormalizedTime();
    }
}