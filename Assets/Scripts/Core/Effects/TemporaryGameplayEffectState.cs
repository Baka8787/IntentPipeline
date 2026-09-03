using UnityEngine;

namespace Project.Core.Effects
{
    /// <summary>
    /// 本輪唯一需要的 gameplay effect 身分。刻意只包含 Slow；第二個效果出現前，
    /// 不以可擴充性為名預建 StatusEffect／Buff framework。
    /// </summary>
    public enum Effect
    {
        None = 0,
        Slow = 1
    }

    /// <summary>
    /// 角色身上的單一暫時效果 slot。
    ///
    /// tag、倍率與到期時間刻意分開保存：它們是三筆狀態，不是一個可堆疊 effect 物件。
    /// 查詢時才清除到期 slot，可省去每幀 Update，也不需要投遞者或移動系統負責 remove。
    /// </summary>
    public sealed class TemporaryGameplayEffectState : MonoBehaviour
    {
        private Effect _tag;
        private float _movementSpeedMultiplier = 1f;
        private float _expiresAt;

        /// <summary>
        /// 寫入唯一支援的 Effect.Slow。再次命中直接覆寫同一 slot，因此只刷新期限，
        /// 絕不把倍率再乘一次而形成 0.09 倍。
        /// </summary>
        public void ApplySlow(float movementSpeedMultiplier, float duration)
        {
            ApplySlowAt(movementSpeedMultiplier, duration, Time.time);
        }

        /// <summary>回傳目前生效倍率；無效果或剛到期時固定回傳 1。</summary>
        public float GetMovementSpeedMultiplier()
        {
            return GetMovementSpeedMultiplierAt(Time.time);
        }

        // 顯式時間版本讓 EditMode 測試能確定性驗證刷新與到期，不必等待真實時鐘。
        internal void ApplySlowAt(float movementSpeedMultiplier, float duration, float currentTime)
        {
            _tag = Effect.Slow;
            _movementSpeedMultiplier = Mathf.Clamp01(movementSpeedMultiplier);
            _expiresAt = currentTime + Mathf.Max(0f, duration);
        }

        internal float GetMovementSpeedMultiplierAt(float currentTime)
        {
            ExpireIfNeeded(currentTime);
            return _tag == Effect.Slow ? _movementSpeedMultiplier : 1f;
        }

        internal Effect GetTagAt(float currentTime)
        {
            ExpireIfNeeded(currentTime);
            return _tag;
        }

        internal float ExpiresAt => _expiresAt;

        private void ExpireIfNeeded(float currentTime)
        {
            if (_tag == Effect.None || currentTime < _expiresAt) return;

            _tag = Effect.None;
            _movementSpeedMultiplier = 1f;
            _expiresAt = 0f;
        }
    }
}
