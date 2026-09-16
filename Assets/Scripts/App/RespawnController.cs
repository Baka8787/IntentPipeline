using Project.Core.Survivability;
using Project.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.App
{
    /// <summary>
    /// 把一個死掉的角色送回重生點並救活。**薄的協調者，不是新的權威。**
    ///
    /// <para><b>⭐ authority ／ orchestration 分離（使用者 2026-09-14 裁決）</b></para>
    /// <list type="bullet">
    /// <item><c>CharacterHealth.Revive()</c> 是**唯一**能把 <c>IsDead</c> commit 回 false 的 API。
    /// 本元件不碰血量、不碰黑板、不碰 FSM。</item>
    /// <item><c>MotionDriver.Teleport()</c> 是**唯一**的瞬間移動入口。本元件不寫 <c>transform.position</c>
    /// ——那會讓「MotionDriver 是 position 單一寫入者」當場多出第二個寫入者。</item>
    /// <item>本元件只負責**順序**：先把世界狀態整理乾淨，再宣告「他活了」。</item>
    /// </list>
    /// ⇒ 它持有的東西全部是既有元件的引用；它自己**沒有任何真相**。
    ///
    /// <para><b>⚠️ 為什麼重生鍵不能走 <c>InputData</c></b></para>
    /// <c>BlockInput</c> 的語意是「本幀管線看不到任何輸入」——<c>CharacterPipelineRunner</c>
    /// 在順序 2 的閘門直接把整份 <c>InputData</c> 歸零（dev-spec §7-M5 已裁決的簡化）。
    /// 而 <c>DeathArbiterSource</c> 正是在 <c>IsDead</c> 時抬起 <c>BlockInput</c>
    /// ⇒ **死著的時候任何 gameplay 輸入都收不到，包括重生鍵。**
    /// 要塞進去就得在那道閘門開例外，等於為了一顆鍵破壞一條剛收斂完的語意。
    ///
    /// ⇒ 本元件比照 <see cref="GamePauseController"/>：**自己持有 <c>InputAction</c>、
    /// 自己在 <c>Update</c> 讀**。重生與暫停是同一類東西——system-level 指令，不是角色的動作。
    /// <see cref="Respawn"/> 開成 public，未來的 UI 按鈕可直接以 Inspector 引用綁定
    /// （＝不需要 Singleton，也不需要全域查詢）。
    ///
    /// <para><b>⛔ 這不是 checkpoint 系統</b></para>
    /// 第一版只有**一個** <see cref="respawnPoint"/>。沒有 checkpoint 序列、沒有存檔、
    /// 沒有 encounter 重置（敵人不會回血、不會歸位——那是未來獨立的 Restart Encounter 功能，
    /// 使用者 2026-09-14 明確劃在範圍外）。
    ///
    /// <para><b>⛔ 也不是 RespawnState</b></para>
    /// 本輪刻意**不新增狀態**。等到重生真的需要一段**有持續時間的 gameplay phase**
    /// （起身動畫、無敵窗、不可操作期）時，再考慮升格成 <c>StateType</c>。
    /// 現在升格只會多一列 State Matrix 與兩份 config 要 author，而沒有任何動畫可放。
    /// </summary>
    public sealed class RespawnController : MonoBehaviour
    {
        [Header("Respawn Action")]
        [Tooltip("觸發重生的按鍵。未綁定 ＝ 永遠不會重生，行為與加入本元件前等價。\n" +
                 "⚠️ 這是 system-level 輸入，刻意不走 InputData——死亡期間 BlockInput 會把" +
                 "整份 gameplay 輸入歸零（見類別註解）。")]
        public InputAction RespawnAction;

        [Header("Wiring")]
        [Tooltip("要重生的角色。留空 ⇒ 從同物件與子物件尋找。")]
        [SerializeField] private CharacterHealth health;

        [Tooltip("執行傳送的 MotionDriver。留空 ⇒ 從同物件與子物件尋找。\n" +
                 "⚠️ 它是角色 position 的單一寫入者，傳送必須經過它。")]
        [SerializeField] private MotionDriver motionDriver;

        [Tooltip("重生點。留空 ⇒ 沿用場景開始時的位置與朝向（記在 Awake）。\n" +
                 "⛔ 第一版只有一個點，不是 checkpoint 系統。")]
        [SerializeField] private Transform respawnPoint;

        // 沒有指派重生點時的退路：開場位置。**在 Awake 記一次**，之後不再更新——
        // 若每次重生都更新，重生點會跟著角色漂移，等於沒有重生點。
        private Vector3 _fallbackPosition;
        private Quaternion _fallbackRotation;

        private void Awake()
        {
            ResolveDependencies();
            _fallbackPosition = transform.position;
            _fallbackRotation = transform.rotation;
        }

        /// <summary>
        /// 解析同物件的相依元件。**與 <see cref="Awake"/> 分開，是為了 EditMode 測試**——
        /// EditMode 不在 Play mode，<c>AddComponent</c> 不會呼叫 <c>Awake</c>
        /// ⇒ 欄位恆為 null、斷言假性通過。比照
        /// <c>AIMovementSource.ResolveEffectState</c>／<c>WeaponVisibilitySink.ResolveSocket</c> 的既有慣例。
        /// </summary>
        internal void ResolveDependencies()
        {
            if (health == null) health = GetComponentInChildren<CharacterHealth>(true);
            if (motionDriver == null) motionDriver = GetComponentInChildren<MotionDriver>(true);
        }

        private void OnEnable() => RespawnAction?.Enable();

        private void OnDisable() => RespawnAction?.Disable();

        private void Update()
        {
            if (RespawnAction != null && RespawnAction.WasPerformedThisFrame()) Respawn();
        }

        /// <summary>
        /// 重生。**順序是這個方法唯一的內容，也是它存在的理由。**
        ///
        /// <list type="number">
        /// <item><b>先傳送</b>——此時角色仍然是死的：<c>BlockInput</c> 還抬著、<c>DeathState</c> 還握著 FSM，
        /// 世界處於一個**不會有人在這一幀對它做別的事**的穩定狀態。</item>
        /// <item><b>再救活</b>——<c>Revive()</c> 只改 survivability。下一次順序 0.5 的 <c>PublishTo</c>
        /// 把 <c>IsDead = false</c> 發布出去，同一幀順序 4 的 <c>DeathState</c> 看到後自然放行到 Idle／Move。</item>
        /// </list>
        ///
        /// ⚠️ **反過來做會壞**：先 <c>Revive()</c> 再傳送，角色會有至少一幀是「活著、站在屍體原地、
        /// 輸入已解封」——那一幀足以讓玩家按出一個動作、或讓敵人打中他，而他其實應該已經在重生點了。
        ///
        /// <para>公開的理由同 <c>GamePauseController.SetPaused</c>：①EditMode 測試可在沒有輸入裝置的
        /// 情況下確定性驅動；②未來的 UI「重生」按鈕直接以 Inspector 引用綁定本方法。</para>
        /// </summary>
        /// <returns>本次呼叫是否真的重生了（角色還活著 ⇒ false，不做任何事）。</returns>
        public bool Respawn()
        {
            if (health == null || !health.IsDead) return false;

            if (motionDriver != null)
            {
                Vector3 position = respawnPoint != null ? respawnPoint.position : _fallbackPosition;
                Quaternion rotation = respawnPoint != null ? respawnPoint.rotation : _fallbackRotation;
                motionDriver.Teleport(position, rotation);
            }

            return health.Revive();
        }

        /// <summary>供 EditMode 驗證：解析到的血量元件（可能為 null ＝ 接線缺失）。</summary>
        internal CharacterHealth ResolvedHealth => health;

        /// <summary>供 EditMode 驗證：解析到的位移執行者（可能為 null ＝ 接線缺失）。</summary>
        internal MotionDriver ResolvedMotionDriver => motionDriver;

        /// <summary>測試用的顯式接線；production 走 Inspector 指派或 <see cref="ResolveDependencies"/>。</summary>
        internal void ConfigureForTests(CharacterHealth characterHealth, MotionDriver driver, Transform point)
        {
            health = characterHealth;
            motionDriver = driver;
            respawnPoint = point;
        }
    }
}
