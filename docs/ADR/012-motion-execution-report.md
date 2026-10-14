# ADR-012：移動執行結果的觀測契約（Motion Execution Report）

| | |
|---|---|
| 狀態 | 🧪 **Trial**（2026-10-14 使用者裁決：P5a 包含 `docs/56` D7 的 S1，**先開 Trial ADR，並限定為「移動執行結果的觀測契約」，不得擴張成新的導航控制系統**）。⚠️ 使用者已裁決 ≠ 工程上已驗證；Acceptance Criteria 見 §8 |
| 來源 | `docs/56` §10.4（D7 研究：既有管線拿不到「被允許移動卻沒有進展」）、§11（P5-0 基線：出拳／受擊中的 `IsStuck` 誤報 27–156 帧） |
| 觸發 ADR 的判準 | ①黑板 schema／ownership 變更（新增一個由 `MotionDriver` 唯一寫入的黑板欄位） |
| 相關 | ADR-003（黑板分層、Movement Model）、ADR-007（Trial，方向權威）、ADR-009（Hurt／Death 打斷） |

---

## 1. Context

敵人的卡住偵測（`EnemyNavigator.UpdateStuck`）只看「上一帧有沒有發布移動意圖」與「位置有沒有前進」。但 producer（順序 2.5）在 Action／Hurt／Death 期間照常發布意圖，而那些狀態根本不執行意圖（`ExecuteBakedCurveMovement`／`ExecuteVerticalOnlyMovement`）。於是 0.80 s 的出拳、0.72 s 的受擊都會被判成「卡住」——P5-0 量到每個情境 27–156 帧誤報（`docs/56` §11.3、§11.6）。

**唯一知道「本帧是否被允許照意圖移動、要求了多少、實際走了多少」的地方是 `MotionDriver.ExecuteBaseMovement`**（`MotionDriver.cs:528–585`）。這些事實沒有被發布；`Core/Movement` 依 A4 不得引用 `Project.Presentation`，所以 producer 也不能直接問 `MotionDriver`。

## 2. Problem

不新增契約，就只能用時間或脈衝去**猜**（例如「出拳後 1.5 s 內不算卡住」）——使用者已否決這種做法（`docs/56` D7）。

## 3. Decision（Trial 期間的實作基線）

### D1 — 新增一個黑板欄位：`MotionExecution`
`PlayerRuntimeData.MotionExecution`（struct `MotionExecutionData`）描述**最近一次 `ExecuteBaseMovement` 的執行結果**：

| 欄位 | 意義 |
|---|---|
| `Frame` | 寫入當帧的 `Time.frameCount` |
| `RequestedHorizontalDistance` | 本帧依 Movement Output 要求的水平位移（`MoveSpeed × moveSpeed × Δt`；不含重力、不含 step-down assist 的垂直修正） |
| `AchievedHorizontalDistance` | `CharacterController.Move` 結算後**實際**的水平位移 |

「本帧有沒有執行基礎移動」**不另設布林**：讀者以 `Frame` 是否等於「上一帧」判定。任何沒有呼叫 `ExecuteBaseMovement` 的帧（Action 的 Bake 曲線、Hurt／Death 的垂直移動、Traversal、Locomotion Stop、`IsTimeFrozen`）自然留下過期的 `Frame` ⇒ 讀者視為「本帧不允許照意圖移動」。這讓「未執行」不需要在每一條其他位移路徑都補寫——**漏寫不可能變成假的「已執行」**。

### D2 — 唯一寫入者：`MotionDriver`
只有 `MotionDriver.ExecuteBaseMovement` 寫入，且在 `CharacterController.Move` **之後**寫（事實，不是預測）。A5 `WriterRules` 增加一條。⛔ 狀態、producer、model 都不得寫。

### D3 — 純觀測，不授權、不控制
- `MotionDriver` **只回報**，不判斷「卡住」，也不因為它改變任何位移。
- 讀者**不得**用它允許或禁止移動、不得用它改寫 `MovementIntent` 的權威（`AIMovementSource` 仍是唯一寫入者）、不得用它推導 FSM 狀態。
- 本 ADR 的唯一讀者是**敵人導航的卡住判定**（`EnemyNavigator`）。卡住判定仍然**只回報**；任何「卡住之後要做什麼」的行為都屬於 Living Spec 的 slice（`docs/55`／`docs/56`），⛔ 不得以本 ADR 為名建立新的導航控制系統。

### D4 — 時序與依賴方向
寫於順序 6（LateUpdate，第 N 帧），讀於順序 2.5（Update，第 N＋1 帧）——與 `IsGrounded`／`VerticalVelocity` 同一個「上一帧結果」先例。依賴方向：`Presentation/Motion → Blackboard ← Core/Movement`；A4 不變（`Core/Movement` 仍不引用 `Project.Presentation`）。

### D5 — 卡住的定義（讀者端）
「**有移動請求、實際被允許移動、但沒有有效進展**」：只在報告屬於上一帧（＝本帧被允許照意圖移動）且要求的位移不可忽略時累積；在累積的**被允許移動時間**達到窗長時，若實際位移遠小於要求的位移，即為卡住。不被允許移動的帧（攻擊、受擊、Traversal、停步曲線、時間凍結）**既不累積、也不回報卡住**。具體門檻屬 Living Spec（§7）。

## 4. Alternatives Considered

| | 方案 | 否決理由 |
|---|---|---|
| S2 | `Core` 定義 `IMotionExecutionReport`，producer 以 `GetComponent` 直接問 `MotionDriver` | 繞過黑板：gameplay 直接查詢另一個系統（違反 Data Driven 原則） |
| S3 | 以 `JustTookDamage`、`RequestedActionSlot` 加時間門檻推測 | 使用者否決；而且請求 ≠ 執行（ActionState 可以拒絕） |
| S4 | 各 State 寫「本帧消費意圖」 | 多寫入者；順序 4 晚於 2.5 |
| — | 加 `BaseMovementExecuted` 布林並要求每條位移路徑都寫 false | 任何一條漏寫的新路徑都會留下過期的 `true` ⇒ 假的「被允許移動」。以 `Frame` 判定讓漏寫只會變成「未執行」（安全方向） |

## 5. Trade-offs

- 黑板多一個 struct（12 bytes），玩家與敵人每帧都寫；目前只有敵人讀。
- 讀者需要知道「上一帧」的概念（`Frame == Time.frameCount − 1`）。測試中的帧號要能注入。

## 6. Consequences

- `EnemyNavigator` 的卡住判定改讀本欄位；非戰鬥行為（`docs/55` P1–P3）讀到的 `IsStuck` 語意變準（出拳／受擊不再誤報），行為規則本身不變。
- 新的位移路徑**不需要**為本 ADR 做任何事（D1 的安全方向）；但若某條新路徑「其實是照意圖移動」，它應該改走 `ExecuteBaseMovement` 或在 Living Spec 明確登記。

## 7. 明確**不**在本 ADR 凍結的項目（下放 `docs/56`）

欄位名稱與單位、卡住的窗長與比例門檻、最小請求量、讀者如何把卡住接到任何行為（若有）、debug 顯示。

## 8. Acceptance Criteria（`Trial → Accepted`）

| AC | 內容 |
|---|---|
| AC1 | A5：`MotionExecution` 只有 `MotionDriver.cs` 寫入（`WriterRules`） |
| AC2 | 卡住判定的純函數測試：被允許移動且無進展 ⇒ 卡住；不被允許（過期帧）⇒ 不累積、不回報；有進展 ⇒ 不卡住；請求可忽略 ⇒ 不累積 |
| AC3 | P5 量測（同一組情境、多輪）：**出拳／受擊中的卡住帧 ＝ 0**（基線 27–156）；`CornerPinned` 推牆時仍偵測到卡住（真陽性不消失） |
| AC4 | 非戰鬥 P1–P3 的 PlayMode 測試全綠；EditMode／PlayMode 全量只剩 K-1／K-2 |
| AC5 | `MotionDriver` 寫入與讀者判定零 GC |
| AC6 | 玩家行為不變（沒有讀者） |
| AC7 | 人工 Play：敵人出拳／受擊後的行為與改動前無可見差異（純觀測） |

## 9. 失敗處置（revert 路徑）

Trial 失敗 ⇒ **code／ADR／invariant 一起 revert**：刪 `MotionExecutionData` 與黑板欄位、刪 `MotionDriver` 的寫入、`EnemyNavigator` 回到「上一帧有意圖 ∧ 位置前進」的判定、移除 A5 新增的那一條。沒有資產或 prefab 改動。

## 10. 修訂紀錄

| 日期 | 內容 |
|---|---|
| 2026-10-14 | 建立（Trial）。 |
| 2026-10-14 | 實作與自動化驗證（`docs/56` §12.5）：AC1 ✅（A5 新規則）；AC2 ✅（7 個純函數案例）；AC3 ✅（8 個情境、多輪：出拳／受擊中的卡住 25–156 → 0；帧對齊指標 `stuckAfterNonExecuted` 全部 0；CornerPinned 推牆仍偵測 368 帧）；AC4 ✅（R3 只剩 K-1／K-2；非戰鬥 P1–P3 測試全綠）；AC5 ⏳（未單獨量 GC）；AC6 ✅（玩家沒有讀者；R3 全綠）；AC7 ⏳ 人工 Play。**仍為 Trial。** |
