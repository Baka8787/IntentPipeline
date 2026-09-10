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
        /// <summary>
        /// Slow 的倍率＝「速度剩原本的 30%」（`docs/11` §7.4，使用者裁決，實作不得自行更動）。
        ///
        /// 🆕 **2026-09-05 由 `ThrownProjectile` 的 private const 提升到這裡**：Ice 改走地面 AoE 之後
        /// 出現第二個投遞者（`GroundEffectSink`），同一個「不得更動」的數字散在兩個檔案就是它開始漂移的方式。
        /// ⚖️ 這是**共用常數，不是 framework** ——沒有新增型別、介面或擴充點，
        /// 也沒有違反「第二個使用者出現前不建 abstraction」（第二個使用者就是它出現的原因）。
        /// </summary>
        public const float SlowMovementSpeedMultiplier = 0.3f;

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
