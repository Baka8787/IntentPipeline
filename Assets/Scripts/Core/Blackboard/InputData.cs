using UnityEngine;

namespace Project.Core.Blackboard
{
    /// <summary>
    /// v0.3 變更：改為 ref struct。
    /// 只能存活於 Stack，徹底根除跨幀持有的鬼影資料風險 (Aliasing)。
    /// </summary>
    public ref struct InputData
    {
        public Vector2 MoveInput;
        public Vector2 LookInput;
        public bool JumpButtonDown;
        public bool RollButtonDown;

        // Action 鍵位：一顆對應一個 ActionSlot（2026-09-02 命名統一，取代 Fire／Secondary／Tertiary）。
        // 原本三顆各說各話——FireButtonDown 是 ADR-004 Throw 時期的語意名，Secondary／Tertiary 是拉丁序號
        // ——三種詞彙描述同一排按鍵，而且都對不上 ActionSlot.Slot1/2/3。
        //
        // ⚖️ 舊註解宣稱「本層不回答它代表哪個技能」。**那句話在有三顆編號按鍵之後就已經不成立**：
        // 三顆與三個 slot 一一對應，這一層早就知道 slot 了，只是用別的名字講。改名是讓它誠實，
        // 不是新增耦合。仍然成立的界線是：ActionState 從不認識按鍵，AI 走 ActionRequestTarget。
        // 按鍵→slot 的實際映射仍在順序 2 的 Intent Processor（CharacterPipelineRunner.ProcessIntents）。
        //
        // ⚠️ 本 struct 是 ref struct、不參與序列化，故改名無資產風險；
        //    PlayerInputSource 的同名欄位則相反，必須靠 [FormerlySerializedAs] 保住 binding。
        // 未綁定 InputAction 時恆為 false，不綁也能正常遊玩（同 SprintAction/WalkAction 先例）。
        public bool Slot1ButtonDown;
        public bool Slot2ButtonDown;
        public bool Slot3ButtonDown;

        // 🆕（ADR-003 §10 Stage 1-3）持續型的中性 action 訊號，供 movement producer 解讀為 gait 強度。
        // ⚠️ 刻意採「比照 Jump/Roll/Fire 的中性 action 命名」，**不做成 [Flags] MovementModifier**——
        // 那會把「這些輸入是為了 movement」的領域分類烤進 raw input 層，並寫死 modifier 數量與領域
        // （ADR-003 §6.3 明確否決）。本層只回答「這顆 action 現在有沒有被按住」，不回答它代表什麼。
        // 未綁定 InputAction 時恆為 false ＝ 無修飾鍵，行為等同 Migration 前。
        public bool SprintButtonHeld;
        public bool WalkButtonHeld;

        // 🆕 同一顆 action 的**邊沿**訊號（`WasPressedThisFrame`）。與上面的 Held 並存而非取代——
        // 「按住才生效」與「按一下切換」是 per-game 控制方案差異（由 GaitProfileSO 選擇），
        // raw input 層不預設哪一種，兩種訊號都如實提供。命名比照既有 Jump/Roll/Fire 的 *ButtonDown。
        public bool WalkButtonDown;
    }
}
