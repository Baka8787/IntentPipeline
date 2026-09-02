# ADR-005：Action Identity（多 Action 並存的單一身分概念）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-02 翻牌）——**目前的實作基線，但尚未由任何實作驗證**。語意見 ADR-004 §0 |
| 翻牌條件 | ~~ADR-004 `Trial → Accepted`~~ ✅ **已滿足**（ADR-004 於 2026-09-02 Accepted），本 ADR 同日翻牌為 `Trial` |
| 日期 | 2026-09-02（草案） |
| Acceptance Review | ⏳ 未進行。條件見 **§7**（A–G 七條，其中 **G** 專守 Slow 不擴散） |
| Supersede | **無 ADR 被取代**——與 ADR-001／002／003／004 並列。本 ADR **延續**並**不推翻** ADR-004 的 D1–D7；特別是 §5.2「每個動作一個 `StateType`」的否決在此**繼續有效** |
| 關聯文件 | `docs/ADR/004-action-in-fsm.md`（前置；§8 L4 與 §5.2 否決）、**`docs/09-multi-action.md`（本 ADR 的 Living Spec；所有實作細節在該檔）**、`docs/08-skill-system.md` §11.1（FU-1／2／3 登記表）、`docs/02-dev-spec.md` §1／§2.1／§3.3／§7 |
| 影響模組 | `IntentData`（黑板 schema）、`ActionState`、`StateMachineConfigSO` 的索引形狀、`FullBodyStateMachine.EvaluateInterrupts`、`ActionRequestTarget`、`PlayerInputSource`／`CharacterPipelineRunner.ProcessIntents`、`ArchitectureRegressionTests` |
| 前置事實 | `docs/08` §11.1 登記的 FU-1／FU-2／FU-3 三項，來源皆為 Throw vertical slice 期間的實作發現；2026-09-02 作品集方向調整後，三項同時成為阻擋項 |

---

## 1. 背景：三個登記事項其實是同一個問題

`docs/08` §11.1 登記了三項「本輪不處理」的發現，並在表格下方留了一句預言：

> 三項共同的觸發時機一致：**敵人同時需要 `Attack` ＋ `Damage`、玩家同時需要 `Throw` ＋ `Sword` 的那一刻**。
> 在那之前它們是登記事項；在那之後它們是同一個 ADR 的三個面向，**不應該被拆成三張票分別解決**。

那一刻到了。2026-09-02 的作品集方向調整要求玩家同時擁有 **Quick Spell ／ Ice Spell ／ Melee Slash** 三個動作，三項登記事項同時變成阻擋項：

| 登記項 | 磁碟證據 | 為什麼現在擋住 |
|---|---|---|
| **FU-2** 一角色一份 Definition | `ActionState.Initialize` → `config.GetStateParams<ActionDefinitionSO>(Type)`；根因是 `StateMachineConfigSO` 的 `_paramsMap`／`_bakeMap`／`_priorityMap`／`_interruptMap` **全以 `StateType` 為鍵** | 三個動作在結構上無法並存 |
| **FU-3** mailbox 無身分 | `ActionRequestTarget.RequestAction()` 無參數，內部只有一顆 `bool _hasPendingRequest` | 敵人分不清「我被打到」與「我要出手」 |
| **FU-1** Action→Action 中斷不可能 | `FullBodyStateMachine.EvaluateInterrupts` 首行 `if (targetState.Type == _currentState.Type) continue;` | 「揮劍中被打斷」不會發生 |

**三項的共同根因是一句話：系統裡沒有「這是哪一個 Action」這個概念。** 只要那個概念不存在，查表就只能用 `StateType` 當鍵、mailbox 就只能是無名旗標、中斷就只能以型別比較。

---

## 2. 問題陳述

> **需要一個「Action 身分」概念，讓輸入映射、冷卻、HUD、外部 request、中斷規則五個消費者用同一把鍵說話——而且不得因此長出第二個 gate／interrupt 權威。**

最後半句是本 ADR 的真正難點，也是它必須是 ADR 而不是實作細節的原因。ADR-004 **D2** 立下「打斷只由 FSM ＋資產決定」，`§8-L4` 已預先點名：若讓 `ActionState` 內部自行換 Definition，就會在 State 內長出第二個 interrupt 權威，**直接違反 D2**。

---

## 3. Decision（本 ADR 凍結的內容，共五條）

> ⚠️ **本節刻意只凍結「改錯會造成架構污染」的決策。**
> 具體容器型別、身分的表示法、API 形狀、slot 數量、鍵位一律**不凍結**，下放 `docs/09-multi-action.md`（見 §8）。

### D1 — 恰好一個 identity 概念，五個消費者共用

系統中**只准存在一個**「這是哪一個 Action」的身分概念。輸入映射、per-action 冷卻、HUD 查詢、`ActionRequestTarget` 的 request 身分、Action→Action 中斷規則，**全部以它為鍵**。

⛔ **禁止**任一消費者自造第二把鍵（例：HUD 自己維護一份 index、mailbox 自己定義一組 enum）。
**理由**：五把鍵就是五份必須人工保持同步的真相。這正是 `CLAUDE.md`「Respect Ownership」在身分層面的推論。

### D2 — `ActionState` 仍是唯一的 action gate authority

「能不能出手」（含冷卻是否到期、是否 grounded、是否有 pending request）**只由 `ActionState.CanEnter` 回答**。per-action 冷卻的**執行期狀態住在 `ActionState` 內部**。

⛔ **禁止**把冷卻搬到 Runner、Config、HUD 或任何 Presentation 元件——那會讓「能不能出手」有兩個回答者。
**這是 ADR-004 D2 的直接延續，不是新決策。**

### D3 — 中斷規則改以 identity 為鍵；`StateType` 成員數不變

Action→Action 中斷（FU-1）以 **identity** 為鍵表達，**不**新增 `StateType` 成員。

**理由**：ADR-004 §5.2 已否決「每個動作一個 `StateType`」（拓撲爆炸；動作是同構的離散生命週期）。該否決在本 ADR **繼續有效**，且既有不變量 **A13′（`StateType` 恆六員）＋A19** 直接承接本條，不需要新增守衛。

### D4 — Action request 帶身分，屬黑板 schema 變更

`IntentData` 現行的單一 `FireRequested` 旗標，改為**帶 identity 的 action request**。

本條**明確承認這是黑板 schema 變更**（`CLAUDE.md` ADR 判準①），因此它出現在 ADR 而不是 Living Docs。
⛔ **不得**因為「只是加個欄位」或「只是給 HUD 用」而走例外路由。
**形狀不凍結**——欄位組成、是否沿用 `Reset()` 的單幀邊沿語意、與 `ActionRequestTarget` 的關係，全部由 Living Spec 決定（§8）。

### D5 — Effect 不屬於 Action 系統，且不建立通用 framework

命中後施加於**他人**的效果（本輪唯一使用者：Ice 的 Slow）**不屬於 Action 系統**。Action 系統的職責邊界是「我做了什麼、什麼時候生效」；「被打到的人怎麼了」在邊界之外。

Slow 的實作**必須**是既有 intent producer 上的係數，讓結果沿既有 locomotion 管線自動傳播。
⛔ **禁止**建立 `StatusEffect`／`Buff`／`Modifier` 之類的通用機制。
**理由**：`CLAUDE.md`「第二個使用者出現前不得建立 production abstraction」。本輪 Slow 是唯一使用者。第二個效果出現時再談抽象，那時才有兩個資料點可以歸納。

---

## 4. Identity 的需求（給候選方案打分用；本節不做選擇）

| # | 需求 | 來源 |
|---|---|---|
| **R1** | 可作為輸入映射的鍵（Q／E／滑鼠左鍵 → 哪個 Action） | 本輪展示需求 |
| **R2** | 可作為 per-action 冷卻的鍵 | D2 |
| **R3** | 可被 Presentation（HUD）查詢，且**不需要 Presentation 持有 Core 的 authored SO 參照** | `LayerRules`：Presentation 只讀黑板 |
| **R4** | 可表達 `ActionRequestTarget` 的 request 身分（「我被打到」≠「我要出手」） | FU-3 |
| **R5** | 可作為 Action→Action 中斷規則的鍵 | FU-1／D3 |
| **R6** | 不得使 `ActionState` 之外出現第二個 gate／interrupt 權威 | D2／ADR-004 D2 |
| **R7** | 熱路徑零 GC（不得在 `Update` 造字串、不得裝箱） | `CLAUDE.md` Zero GC；A3 |

---

## 5. 候選方案比較（**不凍結；領先方案在 §6，最終選擇由 Living Spec 記錄**）

| 方案 | R1 | R2 | R3 | R4 | R5 | R7 | 主要代價 |
|---|:--:|:--:|:--:|:--:|:--:|:--:|---|
| **A. `int` slot index** | ✅ | ✅ | ✅ | ⚠️ | ✅ | ✅ | **匿名**：資產排序改變即行為改變，且「slot 3」對敵人的受擊語意毫無意義（R4 弱） |
| **B. `enum ActionSlotId`**（如 `Primary`／`Secondary`／`Tertiary`／`Reaction`） | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 新增 slot 需改程式（enum 加一員）——但比照 `StateType` 先例，且該成本**本來就該被看見** |
| **C. `ActionDefinitionSO` 參照本身** | ⚠️ | ✅ | ❌ | ⚠️ | ✅ | ✅ | **違反 R3**：Presentation 得持有 Core authored SO；同一份 Definition 無法被兩個 slot 共用；把 authored fact 送進 runtime seam |
| **D. 每個 Action 一個 `StateType`** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **已由 ADR-004 §5.2 否決**（拓撲爆炸），且 A13′ 會紅。列出僅為完整性 |

**方案 A 與 B 的真正差別不在效能，在「身分有沒有語意」。** 敵人的 `Damage` 是一個 **reaction**，不是「第 3 號技能」；玩家的 Quick Spell 是一個 **主動槽位**。R4 要求身分能承載這個區別，A 做不到而 B 做得到。

---

## 6. 領先候選（**非決議**）

**方案 B（具語意的 slot 身分）目前領先。**

但本 ADR **不凍結它**。理由是：實作會逼出「Damage 到底該不該佔一個 slot」「slot 數量該不該固定」這類問題，而那些問題的答案會回頭影響表示法的選擇。依 `CLAUDE.md` 的 `Design → Trial → Implement → Observe → Revise → Accept` 流程，**這正是 Trial 期間該被推翻的東西，不該在紙上鎖死。**

⇒ 翻牌進 Trial 後，實作可在 A／B 之間改選，**只需記入 §9 修訂紀錄，不必開新 ADR**。改選 C 或 D 則需回到本 ADR 重新裁決（前者違反 R3，後者違反既有否決）。

---

## 7. Acceptance Criteria（`Trial → Accepted` 的條件）

> **在本 ADR 翻牌為 `Trial` 並完成 `docs/09` 的實作之前，不得改為 `Accepted`。**

- [ ] **A. 玩家同時持有三份 Definition**（Quick Spell／Ice Spell／Melee Slash）並可由各自的鍵獨立觸發，冷卻互不干擾
- [ ] **B. 「加第四個 Action ＝ 零程式」可被實地驗證**——實際加入一個測試用 Action，只透過「一份 `ActionDefinitionSO` ＋ 一列動畫映射 ＋ 一列 slot 映射」完成，**runtime 程式碼零修改**。⚠️ 這條是本 ADR 的**真正目的**，等同 ADR-004 的 F
- [ ] **C. 既有 Idle／Move／Jump／Roll／Throw 無回歸**（動畫播放序列與位移路徑逐字不變）
- [ ] **D. EditMode 測試全綠**，含 A13′／A19 維持，並新增「identity 單一來源」不變量（見 `docs/09` §9）
- [ ] **E. 零 GC 通過**（dev-spec §7.4 SOP；穩態 `0 B/frame`）
- [ ] **F. 沒有長出第二個 gate／interrupt 權威**——ADR-004 D2 的延續。若實作出現「為了讓多動作跑起來，只好在 `ActionState` 之外再判斷一次能不能出手」，即為未通過
- [ ] **G. Slow 沒有讓任何既有系統認識它**——`LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController` **零修改**，而敵人的速度階層、停步選片、腳步節奏全部自動正確。⚠️ 任何一個檔案為了 Slow 而被改動，本條即未通過

**未通過時的處置**（依序，不得跳過）：

1. **先修 Trial ADR ／ `docs/09`**，把實作發現寫進去；
2. 再驗證；
3. **不得為了維護舊文字而在程式裡補 workaround**；
4. 若 D1／D2 本身被證偽（例：單一 identity 撐不住五個消費者、或 per-action 冷戳必須離開 `ActionState`），本 ADR 轉為 `Rejected`，**code／ADR／invariant 一起 revert**（回到單一 Definition 的 ADR-004 基線），並開新 ADR 記錄失敗原因。**失敗的 Trial 與成功的 Trial 一樣有紀錄價值。**

**Revert 路徑**：本 ADR 的所有改動都是**加法**（新 identity 概念、schema 加欄位、Config 索引擴張）。revert ＝ 移除加法，回到「一角色一份 Definition」的 ADR-004 Accepted 狀態；Throw 不受影響，因為它在 P-0 已獨立結案。

---

## 8. 明確**不**在本 ADR 凍結的項目（下放 `docs/09-multi-action.md`）

> 比照 ADR-004 §9。本節存在的理由：讓「哪些能改、哪些不能改」可稽核，而不是靠讀者自行判斷。
> **下列項目允許在 Trial 期間因實作發現而修改，只要不違反 §3 的 D1–D5。**

| 項目 | 為什麼是實作細節 |
|---|---|
| identity 的**表示法**（`int`／`enum`／其他） | §6 已明說領先候選非決議；實作會逼出更好的判斷依據 |
| 承載多份 Definition 的**容器形狀**（`ActionState` 內陣列／Config 索引擴張／獨立 SO 清單） | 三種都能滿足 D1–D3；選哪個取決於 Inspector 可維護性，那要接上資產才知道 |
| per-action 冷卻的**儲存形式**（陣列／字典／結構體欄位） | 只要住在 `ActionState` 內（D2），形式無架構後果 |
| HUD 的**查詢 API 形狀** | 只要不違反 R3／R6 |
| **slot 數量**與是否固定 | 本輪只需 3～4 個；上限是 YAGNI 問題 |
| **鍵位配置**（Q／E／滑鼠左鍵） | 純輸入資產配置 |
| `Damage` **是否佔用一個 slot** | 實作會逼出答案：它是 reaction 而非主動技能，可能該有不同待遇 |
| `ActionDefinitionSO` 是否需要**新增欄位**以承載 identity | 加法，無架構後果 |
| Slow 的**係數形式**（乘法／減法／曲線）與掛載點細節 | D5 只凍結「必須走 intent producer 係數、不建 framework」，不凍結算式 |

---

## 9. 修訂紀錄（Trial 期間的每一次修改都必須記在此）

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-02 | 建立草案（`Proposed`） | 作品集方向調整，FU-1／2／3 同時成為阻擋項 |
| 2026-09-02 | **`Proposed → Trial`**。內容一字未動 | ADR-004 通過 Acceptance 並改為 `Accepted`，「同一時間只允許一個 Trial」的限制解除 |

---

## 10. Risks

| # | 風險 | 處置 |
|---|---|---|
| **R-a** | identity 概念被實作成「五個消費者各自的鍵」，D1 名存實亡 | `docs/09` §9 的 identity 單一來源不變量；Acceptance B（加第四個 Action 零程式）會直接暴露 |
| **R-b** | per-action 冷卻為了 HUD 方便而外流到 Presentation | D2 ＋ `WriterRules`：HUD 只讀黑板，寫入者仍唯一 |
| **R-c** | Slow 被做成通用 StatusEffect framework | D5 ＋ Acceptance G（既有五個檔案零修改） |
| **R-d** | `ActionState` 隨動作變多長成 God Class | 承接 ADR-004 R-c 與 A19；第三個動作進來時先談查表再談拆分 |
| **R-e** | 本 ADR 在 ADR-004 未 Accepted 前被誤當成實作基線 | 狀態欄 `Proposed` ＋ 翻牌條件寫在頂部；`WORKLOG.md` 同步 |
