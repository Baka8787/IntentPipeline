# ADR-005：Action Identity（多 Action 並存的單一身分概念）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-02 翻牌）——**目前的實作基線，但尚未由實作驗證**。語意見 ADR-004 §0 |
| 日期 | 2026-09-02 |
| 驗證方式 | **Code-first**（`CLAUDE.md` Fold-back：Trial 期允許短暫 code-first，那正是驗證的一部分）。文件於工作包結束、交付驗收前 fold back |
| Acceptance Review | ⏳ 未進行。條件見 **§4** |
| Supersede | 無。與 ADR-001／002／003／004 並列，**延續**且不推翻 ADR-004 的 D1–D7（特別是 §5.2「每個動作一個 `StateType`」的否決繼續有效） |
| 關聯文件 | `docs/ADR/004-action-in-fsm.md`（前置，✅ Accepted）、**`docs/09-multi-action.md`（Living Spec；候選比較、需求分析、資產配置、輸入映射、實作細節全在該檔）** |
| 前置事實 | `docs/08` §11.1 登記的 FU-1／FU-2／FU-3；2026-09-02 作品集方向調整後三項同時成為阻擋項 |

> **本 ADR 刻意寫得很短。** 2026-09-02 檢討：初版凍結五條，其中三條分別是既有 authority 的複述、schema routing 事實、與實作分析——**那些不是決策，不該佔用 ADR 的凍結力**。只有兩條通過「改錯會造成架構污染」的門檻。其餘全數下放 `docs/09`。

---

## 1. 問題

`docs/08` §11.1 登記的三項發現有共同根因：**系統裡沒有「這是哪一個 Action」的概念**。

| 登記項 | 磁碟證據 |
|---|---|
| **FU-2** 一角色一份 Definition | `ActionState.Initialize` → `config.GetStateParams<ActionDefinitionSO>(Type)`；根因是 `StateMachineConfigSO` 四張表**全以 `StateType` 為鍵** |
| **FU-3** mailbox 無身分 | `ActionRequestTarget.RequestAction()` 無參數，內部只有一顆 `bool` |
| **FU-1** Action→Action 中斷不可能 | `FullBodyStateMachine.EvaluateInterrupts` 首行 `if (targetState.Type == _currentState.Type) continue;` |

概念不存在 ⇒ 查表只能用 `StateType` 當鍵、mailbox 只能是無名旗標、中斷只能比型別。

---

## 2. Decision（凍結內容，共兩條）

### D1 — 恰好一個 Action identity，所有消費者共用

系統中**只准存在一個**「這是哪一個 Action」的身分概念。輸入映射、per-action 冷卻、HUD 查詢、`ActionRequestTarget` 的 request 身分、Action→Action 中斷規則，**全部以它為鍵**。

⛔ **禁止**任一消費者自造第二把鍵。
**理由**：N 把鍵就是 N 份必須人工同步的真相。這是「Respect Ownership」在身分層面的推論。

**不凍結**：identity 的表示法、容器形狀、API、slot 數量、命名 —— 全部下放 `docs/09`，且**允許在 Trial 期間被程式推翻**（那正是本次 Trial 要驗證的事）。

### D2 — 目前不建立通用 Ability ／ StatusEffect framework

命中後施加於他人的效果（首個使用者：Ice 的 Slow）**不屬於 Action 系統**。Action 系統的邊界是「我做了什麼、什麼時候生效」；「被打到的人怎麼了」在邊界之外。

⛔ **禁止**建立 `Ability`／`StatusEffect`／`Buff`／`Modifier` 之類的通用機制。
**理由**：`CLAUDE.md`「第二個使用者出現前不得建立 production abstraction」。第二個效果出現時再談抽象，那時才有兩個資料點可以歸納。

---

## 3. 明確**不**由本 ADR 管的事（下放 Living Docs，不需要 ADR）

| 項目 | 為什麼不需要 ADR |
|---|---|
| 「`ActionState` 是唯一 gate authority」 | **ADR-004 D2 已經凍結**，複述不增加保護 |
| 「中斷以 identity 為鍵、`StateType` 不加成員」 | ADR-004 §5.2 的否決已涵蓋，且 A13′／A19 已機器化守住 |
| 「request 帶身分屬 schema 變更」 | 那是 **routing 事實**，不是決策。走既有 routing rule 即可 |
| identity 的候選比較、需求清單 | **實作分析**，屬 Living Spec（`docs/09` §3） |
| 資產配置、鍵位、冷卻儲存形式、HUD API | 實作細節 |

---

## 4. Acceptance Criteria（`Trial → Accepted`）

> **進度（2026-09-02 code-first 第一輪後）**：D／F 成立；A 在 EditMode 層成立、待 Play；B／C／E 未驗。

- [~] **A. 同一角色持有並可獨立觸發至少兩份 `ActionDefinitionSO`**，各自獨立輸入與 cooldown，且**共用同一顆 `ActionState`** —— 🟡 **EditMode 層成立**（T18 斷言兩者為同一 `ActionState` 實例、T19 斷言冷卻不連坐）。⏳ **待資產接線後 Play 驗證**（Q／E 實際觸發）
- [ ] **B. 加下一個 Action ＝ 零 runtime 程式**（一份資產 ＋ 一列動畫映射 ＋ 一列 slot 映射）—— ⏳ 需實際加第三個 Action 才算數
- [ ] **C. 既有 Idle／Move／Jump／Roll／Throw 無回歸** —— ⏳ 待 Play。T21 已鎖住舊資產的**解析**路徑，但不涵蓋播放與位移
- [x] **D. EditMode 全綠**（含 A13′／A19 維持）—— ✅ 使用者實跑確認（2026-09-02）。⚠️ 同輪修掉 **A22 自 ADR-004 Trial 期起一直為紅**的既存缺陷（斷言 `IActionReleaseSink`，介面早已改名為 `IActionLifecycleSink`）
- [ ] **E. 零 GC**，穩態 `0 B/frame` —— ⏳ 待 Profiler。⚠️ 熱路徑有改動（`ProcessIntents`、`EvaluateInterrupts`），**必須複驗**
- [x] **F. 沒有長出第二個 gate／interrupt 權威**（ADR-004 D2 的延續）—— ✅ 靜態稽核（定稿後重跑）：`_cooldownEndTime` 僅存在於 `ActionState.cs`；`CanEnter` 與 `CanReenter` **同源於單一 `TryResolveRequest`**，未引入新決策來源；`ActionState` 仍只讀取 facade（`IsPlaying`／`GetNormalizedTime`），從不 `Play`；`Core/` 下 `Instantiate` 零命中。**A23 已將本條機器化**

**未通過**：先修本 ADR ／ `docs/09` → 再驗證 → **不得補 workaround**。
若 **D1 被證偽**（單一 identity 撐不住所有消費者），轉 `Rejected`，code／ADR／invariant 一起 revert 回 ADR-004 的單一 Definition 基線。
**Revert 成本低**：本 ADR 的改動全是加法，Throw 已於 ADR-004 獨立結案、不受影響。

---

## 5. 修訂紀錄

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-02 | 建立草案（`Proposed`） | 作品集方向調整，FU-1／2／3 同時成為阻擋項 |
| 2026-09-02 | `Proposed → Trial` | ADR-004 `Accepted`，「同一時間只允許一個 Trial」解除 |
| 2026-09-02 | **code-first 第一輪落地**：`ActionSlot` 身分 ＋ 多 Definition ＋ per-slot 冷卻 ＋ Action→Action 重入。**D1／D2 未被推翻**，兩條決策一字未動。實作推翻的是**位置**與**重入實作**（詳見 `docs/09` §3.4），兩者都屬本 ADR 明文不凍結的範圍 | Fold-back：Trial 期允許 code-first，工作包結束前同步文件 |
| 2026-09-02 | **瘦身：五條決策砍到兩條**（原 D2／D3／D4 下放 §3 表格）；候選比較與需求清單移入 `docs/09` §3；改採 code-first | 檢討發現 ADR 比它要守護的程式還長。既有 authority 的複述、routing 事實、實作分析**都不該佔用 ADR 的凍結力**——那會稀釋「ADR ＝ 改錯會造成架構污染」的訊號 |
