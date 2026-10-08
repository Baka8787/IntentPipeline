# 48 — Canonical Concept Proposal：Stop／Locomotion Tier／Foot Contact（2026-10-08）

| | |
|---|---|
| **狀態** | F1／F3：🟢 **方向已裁決**（2026-10-08，見 §0.2）；F2：⏸ **暫停**——§6 的語意檢查結論是「不提升任何全域 tier」，§2 的 M2.1–M2.5 停止 |
| **輸入** | `docs/47` F-01（Stop ownership）、F-02（Gait／locomotion tier）、F-03（plant／lock／landing） |
| **路由** | F1 → ADR-010（Trial）獨立擴充 D6，比照 D5；F2 → **不開 ADR**（§6：不成立）；F3 → Living Spec（`docs/05`／`docs/45`），不需 ADR |
| **閱讀方式** | §0.2 是裁決紀錄；§6 是 F2 的語意檢查（取代 §2 的提案）；每節的「❓ 決策點」若已裁決會標 ✅ |

---

## 0.2 裁決紀錄（2026-10-08，使用者）

| # | 裁決 | 影響 |
|---|---|---|
| F1／F3 | **接受修正方向** | §1、§3 的概念切法成為後續工作的依據 |
| **D1-b** | **Yes**：規則⑦的 Walk／Run 跟**已承諾的 authored stop tier**，不跟 control gait。搖桿半推最後若選到 Walk Stop，就屬於 Walk-stop 規則 | 行為變更；M1.3 落地後要**重跑下樓停步驗收** |
| D2-a | **暫不裁決**。Footstep audio **從 F2 拆出**，視為獨立的 Presentation policy；⛔ 不得從「FootIK／Stop 不該用 control Gait」推出「音色必須跟另一個 tier」 | §2.3 的 Audio 列、M2.5 撤銷 |
| D2-b | **不接受 0.55**。Mixer 是連續 blend，「權重最大的 clip」不天然是 canonical domain fact；⛔ 不得先創造 `BodySpeedTier` 再找 consumer | §2.2 撤銷 |
| D2-d | **暫緩**。要先證明第二個全域 tier 是穩定的 domain concept；若成立，因 ADR-003 已 Accepted，應開**新 ADR**，不改寫 ADR-003 | §6 的檢查結論：不成立 ⇒ 不開 |
| D3-b | **暫不排 runtime 修改**。先完成 M3.1，用樓梯 probe 證明兩份 tread／surface attribution 規則逐帧等價；pelvis 改 authority 之後另開 behavioral slice | M3.2／M3.3 延後 |
| **D2-e**（第二輪） | **Yes**：開 measurement-only slice 驗證 F2-J（combat 側移／後退、analog ≈ 0.6、正常 Walk／Run 對照），記錄 dominant loop／sampled clock／Jump 選的 bake family／最終 LU-RU；**先量、不修** | §6.4、§7 |
| **D2-f**（第二輪） | **Yes**：`docs/47` F-02 改判——各 consumer 各自分類是合理 specialization；真問題＝FootIK 用 Gait 代理 committed stop tier、Jump 可能用 A loop 的時鐘查 B loop 的 Bake | 已改 `docs/47` |
| **M1.2／M1.3**（第二輪） | M1.2 開工：ADR-010 **D6** 發布 LocomotionModel 已承諾的 stop tier——**contract 修補，不是新的全域 locomotion tier**。M1.3 在 M1.2 之後、**分開 commit**；屬行為變更，完成後重跑下樓停步驗收 | §1.4 |
| **D3-c**（第二輪） | **Yes，只做 terminology cleanup**：Animation Plant／Predicted Contact（Landing）／IK Lock／Committed Surface；不做全域 rename | `docs/45` §28 K6 |
| M3.1 結論（第二輪） | **保留為重要結論**：同一組 hits 上「最高踏面 vs 最大穿透」0／6073 分歧 ⇒ §14.4 的差異應繼續追 **footprint pose／XZ placement**，不是 selector rule | `docs/45` §14.4／§28.1 |
| 順序 | **先完成 F2-J measurement，再決定 Jump 要不要修 clock-family ownership** | — |
| 範圍 | 六個 no-behavior-change migration 中，**不依賴新 tier 的可以繼續**；凡預設新 canonical field 已成立的 M2.x 先停 | 繼續：M1.0、M1.1、M3.0、M3.1。停：M2.1–M2.5。M2.0 見 §6.5 |

---

## 0. 本提案遵守的既有裁決（不重議）

設計時逐條對照過下列決定；提案中任何一步若會碰到它們，會在該步明講。

| 來源 | 內容 | 對本提案的限制 |
|---|---|---|
| ADR-010 D2／D5 | 黑板快照只放**正式 consumer 實際讀取**的欄位；擴充是「獨立變更＋正式 consumer 理由」 | F1 只加一個欄位，且必須點名 consumer |
| ADR-010 §9（2026-10-03）＋ `docs/45` §14.1／§16.1 | **Bake plant 語意不得依賴 T3 的鎖定門檻**；landing-pose contract、遲滯不採 | F3 **不**統一烘焙 plant 與 T3 lock 的判準 |
| `docs/46` §14.3 | `StopHold` 以「移動意圖放開」為準（涵蓋取消、輸入空檔、沒有停步 clip 的放開） | F1 **不**把骨盆的停步語意改綁到「有停步 clip」 |
| `docs/46` §19 規則⑦ | 停步落點預測只批准 **Walk 下樓** | F1 決定這個「Walk」指的是什麼 |
| `docs/36` §4.1 | 腳步音色跟控制方案的 `Gait`；AI 沒有 policy ⇒ 預設 Run 是**刻意的**；理由是「強度與檔位必須同源」 | F2 把音效的來源列為決策點，不直接改 |
| ADR-003（Accepted） | 跨黑板 seam 的是 `DesiredSpeedNormalized`（model 無關），modifier 的解讀留在 policy | F2 的 locomotion tier 屬於 model output，不是 intent |
| B11／Gate B（CLAUDE.md） | 不寫 Animancer 內部 `_Thresholds` 序列化 | F2 的 tier 表由 Core 擁有；mixer 門檻照公式人工填 |
| `docs/38` §13 | `IsGrounded` 不加否決層；Foot IK 不是 grounding authority | 本提案不碰 grounding |
| 2026-10-07 | 失地 grace 暫不抽共用 timing primitive | 本提案不碰時間窗 |

### 0.1 對 `docs/47` 的兩處更正（已同步改進該檔）
- **F-02**：AI 的 `Gait` 恆為 Run 是 `docs/36` §4.1 的**刻意設計**，不是遺漏。真正的問題是另一件事：同一份文件的理由是「強度與檔位必須同源」，但 `respectAnalogMagnitude = 1` 時，搖桿半推會讓強度變成 Walk 的量級，`Gait` 卻仍是 Run，同源已經不成立。
- **F-03**：`docs/45` §22.4 量到 T3 **鎖定當下**動畫腳離自己的停點 ≤ 2 cm ⇒ 「onset 差 6 倍」的寫法高估了。兩個定義真正分岔的是**鎖的持續期間／釋放**與**支撐踏面的選擇規則**。此外，烘焙與 T3 分開是 2026-10-03 的明確裁決。

---

## 1. F1 — Stop ownership

### 1.1 結論：「停步」是三個概念加一個觀測，不是一個

目前程式把它們混在 `movementRequested`／`walkGait`／`releaseEdge` 三個變數裡。拆開後，每個都有清楚的 owner：

| ID | Canonical concept | 定義 | Owner | Producer | 發布？ |
|---|---|---|---|---|---|
| **S-A** | **Movement Request**（移動請求） | 本帧的移動意圖是否非零：`DesiredSpeedNormalized ≥ ε`（ε＝現行 `LocomotionSpeedSmoother.Epsilon` 0.001） | 意圖本身（`MovementIntentData`） | 唯一 active 的 `IMovementIntentSource`（不變） | 已在黑板；**缺的是唯一的判讀規則** |
| **S-B** | **Committed Stop**（已承諾的停步） | `LocomotionModel` 已選定一支停步 clip：**tier（Walk／Run）**、階段（Pending／AwaitingPlayhead／Playing）、世代 | `LocomotionModel`／`LocomotionStopRuntime` | 同左 | 目前只發布預測快照；**tier 沒發布** |
| **S-C** | **Rest Settled**（落定） | 意圖放開後，兩腳 T3 Locked ∧ root 水平速度 < 0.05 m/s（鎖存到意圖恢復） | `MovingPelvisSolver`（Presentation policy） | 同左 | 否（不需要） |
| **S-D** | **Stop Descending**（下樓中停步） | 放開當帧 root 垂直速度 < −0.2 m/s（鎖存） | `MovingPelvisSolver`（Presentation 的地形觀測） | 同左 | 否 |

**為什麼骨盆的「停步」對應 S-A 而不是 S-B**：`docs/46` §14 的 `StopHold`／`Stand` 要處理所有「放開後身體將靜止」的情況，包括 Sprint 放開（沒有停步 clip）、0.3–0.6 s 內的取消、輸入空檔。如果改綁 S-B，這些情況會掉回移動中的語意，§14.4 量到的改善（Walk rise→fall 9.9 → 1.3 cm）可能退回去。⇒ **骨盆讀 S-A 是正確的；錯的只是它自己定了一個不同的門檻（`> 0`）。**

**為什麼規則⑦的「Walk」對應 S-B 而不是 `Gait`**：規則⑦批准的是「Walk 停步 clip 的落點預測」。預測本身只存在於 S-B 成立時，資料也來自那支 clip 的 Bake。這裡的 Walk 只可能是**那支 clip 的 tier**。用 `Gait` 代理會在搖桿半推（播 Walk clip、`Gait`＝Run）時漏掉，理論上也會在 Walk 鍵＋停步 tier 為 Run 時誤開（目前的強度設定下不會發生）。

### 1.2 Consumer 對照（現況 → 提案）

| Consumer | 它真正要的問題 | 現況讀什麼 | 提案讀什麼 |
|---|---|---|---|
| `LocomotionModel`（停步觸發） | S-A 的下降緣 | `desired ≥ 0.001`（自己） | S-A（同一條規則，改用共用判讀） |
| `FootIKController` → `MovingPelvisContext.MovementRequested` | S-A | `DesiredSpeedNormalized > 0f`（`:867`） | S-A |
| `FootIKController` → `ResolveStandingPosture` | S-A | `> 0f`（`:1059`） | S-A（或隨 `docs/47` G-3 刪除） |
| `MovingPelvisSolver` release edge／`StopDescending` 鎖存 | S-A 的下降緣（消費端自己記上一帧） | 自己記 `PreviousMovementRequested` | **不變**——對 canonical 事實做邊沿偵測是消費端的合法記憶，不是重新推導 |
| `FootIKController` 規則⑦閘門（`walkGait`，`:868`）→ `StopUsesPrediction` | **S-B.tier == Walk** | `data.Gait == Walk` | `LocomotionStopPrediction` 新欄位（見 1.3） |
| `MovingPelvisSolver` `StopSettled` | S-C | 自己 | 不變（Presentation 擁有） |
| `JumpState` 落地後續走（`HasEffectiveMoveIntent` ≥ 0.2） | 「意圖大到足以續走」 | 自己的門檻 | **不變**——這是 policy，不是 S-A 的 copy |
| `ActionState` 取消（≥ `CancelMoveIntentThreshold`） | 同上 | 同上 | 不變 |

### 1.3 發布形狀（最小）

```text
LocomotionStopPrediction（ADR-010，唯一 Writer：LocomotionModel；每次 tick 先清除）
  + StopTier : LocomotionTier（Walk／Run）      ← 新增一欄
  有效條件：Active ∨ HasNextPlant 為 true 時有效（與 RootWorldPositionAtPublish 同規則）；兩者皆 false ⇒ default
```

- **只加 tier**。⛔ 不發布 Pending／Generation／開始帧：骨盆不需要它們（S-A 的邊沿已經足夠），D2 明列 `Generation` 無正式 consumer。
- 正式 consumer：`FootIKController` 的規則⑦閘門（`docs/46` §19）。
- enum 必須放在 `Project.Core.Blackboard`（黑板不得依賴 `Project.Core.Movement`）。這個 enum 與 F2 的 `LocomotionTier` 是同一個型別；若 F2 尚未裁決，先只放 `Walk`／`Run` 兩個值，`LocomotionStopTier` 保留為 Movement 內部型別，兩者以一個 switch 對應。
- S-A 的判讀規則放在 `MovementIntentData`（Blackboard）上，是純函數，不加欄位，也不是 schema 變更：
  ```text
  MovementIntentData.RequestEpsilon = 0.001
  MovementIntentData.HasMovementRequest → Clamp01(DesiredSpeedNormalized) ≥ RequestEpsilon
  ```
  `LocomotionSpeedSmoother.Epsilon` 改為引用它（或維持數值相等並以測試鎖住）。
  ⚠️ `LocomotionModel` 先套用戰鬥方向限速（`ApplyCombatDirectionalSpeedLimit`，取 `min`）才判斷 isIntending。限速值 > ε 時結果與原始意圖相同；**只有當限速 profile 某個方向給出 < 0.001 時兩者會不同**，需要寫一條測試鎖住這個前提。

### 1.4 Migration path

| 步 | 改什麼 | 路由 | 驗證 | 行為差異 | 還原 |
|---|---|---|---|---|---|
| **M1.0** | 先寫 characterization test：①搖桿半推 Run 放開 ⇒ Model 選 Walk 停步、`Gait`＝Run ⇒ 規則⑦目前**不**生效；②Sprint 放開 ⇒ 無停步 clip、骨盆仍進 `StopHold` | 測試 only | EditMode | 無 | 刪測試 |
| **M1.1** | 加 `MovementIntentData.HasMovementRequest`；Model、FootIK 兩處改用它 | Living Spec（`docs/02` §1 註記判讀規則） | EditMode：邊界值 0、0.0009、0.001；X Bot 停步 probe 前後**逐位元相同**（推導：玩家與 AI 的實際意圖值都不落在 (0, 0.001)） | 只影響 (0, 0.001) 的意圖 | revert 單一 commit |
| **M1.2** | ADR-010 **D6**（比照 D5 的獨立擴充）：`StopTier` 進快照；`A12` 欄位集合、`Model_PublishesAndClearsNumericStopPrediction` 同包更新 | ADR-010 Trial 修訂 ＋ `docs/02` §1 | EditMode（A5／A12／發布與清除） | 無（還沒有 consumer） | 與 D5 相同：移除欄位＋A12 |
| **M1.3** | FootIK 規則⑦閘門改讀 `StopTier == Walk`；刪 `MovingPelvisContext.WalkGait` | Living Spec（`docs/46` §19） | ①Walk 鍵停步 probe：與 M1.2 **逐位元相同**；②搖桿半推停步：**行為改變**（規則⑦開始生效）⇒ 依 ADR-010 D4，擴大批准範圍要附同等驗收：重跑 §18／§19 的 Walk 下樓停步 probe，加上人工 Play | 搖桿半推的 Walk 下樓停步開始使用 FutureReachCap | 閘門改回 `Gait` |
| **M1.4** | `StandingPostureSolver` 那份 release edge 隨 `docs/47` G-3 刪除；本提案不單獨處理 | 依 G-3 | — | — | — |
> ✅ **2026-10-08 完成 M1.0／M1.1**：`MovementIntentData.RequestEpsilon`／`HasMovementRequest`；`LocomotionModel`、`LocomotionStopSelector`、`FootIKController`（兩處）改用同一規則；新增 `A51`、`MovementIntentTests` 的門檻／release edge 測試、`LocomotionStopTests.Characterization_StopTier_FollowsReleaseSpeed_NotControlGait`（出貨資產）。EditMode **859／858／0 failed／1 skipped**（+13）。行為差異只在意圖 ∈ (0, 0.001)——推導上不會出現，**未跑 probe**。M1.0 的另一半（Sprint 放開仍進 StopHold）已由既有 `MovingPelvisSolverTests.StopHold_*` 覆蓋，未重寫。

### 1.5 ❓ 決策點
1. ✅ **D1-a**（2026-10-08：隨 F1 方向接受）：同意「骨盆的停步＝S-A（意圖），規則⑦的 Walk＝S-B（clip tier）」這個切法嗎？
2. ✅ **D1-b = Yes**（2026-10-08）：搖桿半推觸發的 Walk 停步，是否在規則⑦的批准範圍內？（M1.3 會讓它生效；不同意的話，閘門應寫成 `StopTier == Walk ∧ Gait == Walk`，代表規則⑦限縮為「Walk 鍵」，而且要在 `docs/46` §19 寫明原因）

---

## 2. F2 — Gait／Locomotion Tier

> ⏸ **本節已被 §6 取代（2026-10-08）**。保留原文作為「為什麼一開始以為需要 `BodySpeedTier`」的紀錄；
> T-B／T-B′、tier 表、R-nearest、M2.1–M2.5 **全部不成立或暫停**，不得引用為設計依據。

### 2.1 結論：兩個合法概念被一個欄位混用

| ID | Canonical concept | 定義 | Owner | Producer | Consumer（提案後） |
|---|---|---|---|---|---|
| **T-A** | **Control Gait**（控制方案檔位） | 玩家選的步態：Walk 鍵／Sprint 鍵／預設 Run。是**輸入的解讀**，與身體實際速度無關 | `PlayerLocomotionPolicy` | 同左（不變；AI 不寫，維持預設 Run） | `RuntimeDebugPanel`；**腳步音色：決策點** |
| **T-B** | **Locomotion Tier**（身體的速度檔） | 以 model 的實際速度（smoother 輸出 `MoveSpeed`）對 tier 表分類 ⇒ Idle／Walk／Run／Sprint。**回答「身體現在主要在播哪一族 clip」** | `LocomotionModel` | 同左（順序 3，與 `MoveSpeed` 同帧） | JumpState 起跳 tier、停步選擇（內部）、腳步音色（決策點） |
| T-B′ | Target Tier（意圖的速度檔） | 以 model 套用限速後的**有效意圖強度**分類 | `LocomotionModel` | 同左 | **只給 model 內部**（ADR-011 起跑對齊）——⛔ 不發布 |

**為什麼 T-B 屬於 model，不屬於 policy**：ADR-003 讓 policy 只負責「modifier → 強度」；什麼強度對應什麼 clip 是 model（＋它的 mixer 資產）的事。現在 Jump 用**原始意圖**分類、Stop 用**實際速度**分類，兩者都不是 model 以同一張表發布的值。下列三種情況會讓同一個身體被分到不同檔：戰鬥方向限速（model 內部套用 `min`，意圖沒變）、AI 的 Slow（`AIMovementSource` 把 multiplier 乘進意圖）、起步加速期（意圖 0.75、速度還在 Walk 帶）。

### 2.2 Tier 表：單一來源、兩個分類規則可選

現在有五份 Walk／Run 邊界，數值近似但不相等：

| 位置 | Walk | Run | Sprint | 性質 |
|---|---|---|---|---|
| `Locomotion.asset` mixer `_Thresholds` | 0.35 | 0.75 | 1.0 | Presentation 資產（Gate B：不得由程式寫入） |
| `GaitProfileSO`（`Gait_ActionRPG`） | 0.3651 | 0.75 | 1.0 | 控制方案的強度（T-A → 強度） |
| Stop 帶（`LocomotionModel` 序列化） | 0.35–0.50 | 0.75–0.875 | — | Stop 的**資格** policy |
| Jump（`JumpStateParams` ×2） | > 0.2 | ≥ 0.75 | — | 起跳分類（輸入是意圖） |
| FootIK | `Gait` | | | 代理 |

**提案**：`LocomotionModel` 持有一張 **tier 表**（Walk／Run／Sprint 的代表速度，數值＝mixer 門檻 0.35／0.75／1.0）。它是 T-B 唯一的分類依據。

| 分類規則 | 定義 | Walk／Run 分界 | 取捨 |
|---|---|---|---|
| **R-nearest（建議）** | 最近的代表速度＝mixer 裡權重最大的 clip | 0.55（以及 Idle 0.175、Sprint 0.875） | 語意就是「身體主要在播哪族 clip」；與 Stop 帶上限 0.875 剛好一致 |
| R-floor | 不超過速度的最高門檻 | 0.75 | 與 Jump 現行的 0.75 一致；但 0.6 的身體 Run 權重已有 62%，仍被判為 Walk |

- Mixer 門檻仍依 B11 人工填寫。**一致性改由測試保證**：EditMode 讀 `Locomotion.asset` 的門檻與 model 的 tier 表比對。⚠️ 讀取方式要先確認 Animancer 是否提供公開的讀取 API；只能讀私有 `_Thresholds` 的話，等同 Gate B 失敗，退回「公式寫在文件＋只測 Core 自己的表」。
- Stop 帶**保留為 Stop 的 policy**：停步 clip 只在「tier ∈ {Walk, Run} ∧ 速度落在該 tier 的入場帶」時使用。帶寬是內容問題（clip 從多快的速度煞車看起來對），不是 tier 的定義。
- Jump 的 0.2「是否算在移動」改用 T-B 的 Idle 分界，或保留為 Jump 自己的 policy（決策點 D2-c）。

### 2.3 Consumer 對照（現況 → 提案）

| Consumer | 真正的問題 | 現況 | 提案 | 行為差異（推導） |
|---|---|---|---|---|
| `JumpState.SelectSpeedTier`（起跳／落地家族、LU／RU） | 起跳當下身體在播哪族 loop（之後要讀那族 loop 的腳相） | 原始意圖、0.2／0.75 | **T-B** | 起步加速期、戰鬥限速、AI Slow 時起跳家族會改變；R-nearest 下，意圖 0.6 的穩態也會從 Walk 變成 Run |
| `IFootPhaseSource.TryGetCurrentFootPhase(loopBake)` | 同上的腳相 | JumpState 從自己的 params 傳入 loop bake（`docs/47` F-10） | model 用 T-B 決定 loop、自己回答；介面去掉參數；`JumpStateParams.Walk/RunLoopBakeData` 刪除 | 無（只要兩份參照目前相同） |
| Stop 選擇（`LocomotionStopSelector.SelectTier`） | 放開當下能否用 Walk／Run 停步 clip | 速度帶 | T-B ＋ Stop 帶（policy） | 無（帶不變） |
| `FootIKController` 規則⑦ | 那支停步 clip 的 tier | `Gait` | **S-B.tier**（F1，不是 T-B） | 見 M1.3 |
| `LocomotionModel` ADR-011 起跑對齊 | 這次起跑是不是要跑到 Run | `Gait == Run` | **T-B′**（model 內部） | 搖桿半推（目標 Walk）不再對齊 Run 起跑相位 |
| `AudioController`／`FootstepAudioBankSO` | **決策點 D2-a** | `Gait` | T-A 或 T-B | 選 T-B：AI 依速度出 Walk／Run／Sprint 音色、搖桿半推出 Walk 聲、加速期前幾步出 Walk 聲 |
| `RuntimeDebugPanel` | 顯示 | `Gait` | 兩者都顯示 | — |

### 2.4 `Gait`（T-A）的去留
- 若 D2-a 選 **T-B**：`data.Gait` 只剩 debug 面板一個讀者，`WalkModeActive` 已在 `MovementIntent` 裡。屆時可以評估把 `Gait` 移出黑板，改由 policy 自己持有＋debug 讀取。⚠️ 這會推翻 `docs/36` §4.1 與 `WriterRules` 註解的理由，需要你明確裁決。
- 若選 **T-A**：`Gait` 保留，是健康的 specialization（「聽起來像玩家選的步態」）。這種情況下要修的只有 `docs/36` 的「同源」前提：寫明搖桿半推時聲音跟按鍵、不跟身體，或讓 `respectAnalogMagnitude` 對 Gait 也生效（例如依強度把 Run 降為 Walk——但那等於在 policy 裡複製 T-B 的分類，⛔ 不建議）。

### 2.5 Migration path

| 步 | 改什麼 | 路由 | 驗證 | 行為差異 | 還原 |
|---|---|---|---|---|---|
| **M2.0** | characterization test：同一組輸入列出 `Gait`／Stop tier／Jump tier（`docs/47` F-02 的表），鎖住現況 | 測試 only | EditMode | 無 | 刪 |
| **M2.1** | `LocomotionModel` 內加 tier 表＋純函數分類（T-B、T-B′）；**不發布**；Stop 選擇改成「tier ＋ 帶」的寫法 | Living Spec（`docs/04`／`docs/07`） | EditMode：Stop 選擇對所有現行輸入逐位元相同；（Gate B 允許時）mixer 門檻一致性測試 | 無 | revert |
| **M2.2** | ADR-003 已 Accepted，因此**新開 ADR（Proposed → Trial）**：黑板 `LocomotionTier` 欄位，唯一 Writer `LocomotionModel`；WriterRule＋A5；enum 進 `Core.Blackboard`（與 F1 的 `StopTier` 共用） | **新 ADR**（判準①） | EditMode | 無（沒有 consumer） | 依該 ADR 的失敗處置 |
| **M2.3** | ADR-011（Trial）原地修訂：觸發條件 `Gait == Run` → T-B′ == Run | ADR-011 修訂紀錄 | EditMode（ADR-011 AC 測試）＋起跑 probe | 搖桿半推起跑不再對齊 | 改回 |
| **M2.4** | JumpState 改讀 T-B；`IFootPhaseSource` 去參數；刪 `JumpStateParams` 的 loop bake 與兩個門檻（C# 欄位移除；`.asset` 裡的舊鍵在 Unity 下次序列化時消失，⛔ 不手改 YAML） | Living Spec（Jump 分卷）＋ ADR-002 不受影響（跳躍高度資料不變） | EditMode＋跳躍 LU／RU probe；**人工 Play**（起跳／落地家族可見地改變） | 見 2.3 | revert＋資產還原 |
| **M2.5** | 依 D2-a 決定音效來源；若選 T-B，評估 `Gait` 去留 | `docs/36` §4.1 修訂 | EditMode＋人工聽感 | 見 2.3 | revert |

### 2.6 ❓ 決策點
1. **D2-a**：腳步音色跟**玩家選的步態（T-A）**，還是**身體的速度檔（T-B）**？
   建議 T-B：AI 沒有 T-A；搖桿半推時身體在播 Walk clip；`docs/36` 自己的理由（強度與檔位同源）在類比輸入下已不成立。反方理由：加速期前幾步會出 Walk 聲，這正是 `docs/36` 當初想避免的「推錯」。
2. **D2-b**：T-B 的分類規則用 **R-nearest（0.55）**還是 **R-floor（0.75）**？
3. **D2-c**：Jump 的「是否算在移動」（0.2）併入 T-B 的 Idle 分界，還是保留為 Jump 自己的 policy？
4. **D2-d**：是否接受 M2.2 開一個新 ADR？（這是 F2 唯一需要新 ADR 的地方；不接受的話，T-B 只能留在 model 內部，Jump 改成透過 `IMovementModel` 查詢，不經黑板）

---

## 3. F3 — Plant／Lock／Landing

### 3.1 結論：大部分分歧是刻意的；碎裂集中在詞彙、契約與「腳下哪一階」的規則

`docs/45` §22.4 證明鎖定**當下**與動畫停點相差 ≤ 2 cm；2026-10-03 裁決烘焙 plant 不依賴 T3 門檻。所以這裡**不提案統一判準**。要做的是把「哪些概念、彼此可以假設什麼」寫成契約，並收掉其中真正重複的那一份規則。

| ID | Canonical concept | 定義 | Owner | Producer | Consumers |
|---|---|---|---|---|---|
| **C-A** | **Authored Contact**（動畫的觸地） | 動畫內容本身：某支 clip 的第 t 秒哪隻腳觸地、落在 root 空間何處。停步 clip 有離散事件（`FootPlantSchedule`）與結束站姿；loop 有連續的「哪隻腳較低」（`FootPhaseCurve`，用於相位對齊） | `MotionBakeData`（Bake 是動畫真實數值的唯一來源，CLAUDE.md） | `MotionBakeEditor`／`FootPlantScheduleBaker`（Editor） | `LocomotionModel`（停步選 variant、發布 next plant）、JumpState（LU／RU） |
| **C-B** | **Foot Lock**（IK 釘腳） | runtime 是否把腳釘在世界座標：β（stance × 速度）＋遲滯＋UnlockRadius；Locked 期間可依 R1c 在 d0 內跟隨 | `FootContactSolver`（Presentation／IK） | `FootIKController` 每帧 | LandingSurfaceResolver（生命週期）、骨盆（Phase／Reliable）、支撐鎖存 |
| C-C | **Reliable Support** | Locked ∧ β ≥ LockBeta ∧ 即時命中 ∧ 腳速 < 0.3 m/s ∧ 連續 | `FootIKController`（C-B 之上的 policy） | 同左 | `MovingPelvisSolver`（Primary Support） |
| C-D | **Footstep Event** | 鞋底相對 pose 平面的一次落腳 onset（行程＋下降＋接觸帶＋擺盪閘） | `FootstepDetector`／`FootPlantTracker`（Presentation／Footstep） | 同左 | `PresentationEventData` → Audio |
| **C-E** | **Support Surface**（這隻腳踩在哪一階） | 給定一個腳印（腳跟／踝／腳尖三點與腳姿），選出支撐它的踏面高度 | `FootIKController` | **目前三份規則**（見 3.3） | T3 接觸、預測落點、骨盆、footstep 材質 |
| C-F | **Swing Destination**（擺盪腳的目的踏面） | 這次擺盪將落在哪一階、可信度多高；生命週期＝C-B 的 Unlocked 期間 | `LandingSurfaceResolver` | 同左（輸入：C-A 的 next plant ＋ C-E 的即時驗證） | T3 lift／reference raise |
| C-G | **Body Landing**（身體落地） | 空中 → 著地（FSM 與事件兩種確認，`docs/47` F-08） | `JumpState`／`MotionDriver` | — | **不在本提案範圍**；只要求不再與 C-F 共用 "Landing" 一詞 |

### 3.2 概念之間的契約（本提案的核心）

| # | 契約 | 現況 | 動作 |
|---|---|---|---|
| **K1** | **C-A ⇏ C-B**：消費端只能把 C-A 當作「動畫的意圖」（目的地、哪隻腳、是否最後一步），⛔ 不得假設「T3 會在這個時刻／這個位置鎖」 | `LandingSurfaceResolver` 已經這樣做（低可信度必須由即時驗證收斂；鎖定時以 T3 自己的支撐為準）——**符合**，但沒寫成契約 | 寫進 ADR-010 D5 的語意說明（Trial、非 schema 修訂）與 `docs/45` |
| **K2** | **C-F 的生命週期由 C-B 界定**：只在 Unlocked 存在；鎖定後的支撐由 C-B 自己決定 | 符合（`Phase != Unlocked ⇒ Reset`） | 只補文件 |
| **K3** | **C-E 只有一條選擇規則**：「三點中最大穿透（平放時＝最高踏面）」，所有需要「腳踩哪一階」的地方都呼叫同一個函數；各處只差輸入（實際腳姿、預測的平放腳姿、無腳姿時的踝單點） | **三份**：T3 `AccumulateContact`；預測 `TryPredictGroundedAnkle`（取最高）；`SampleGround` 的 `ComputeSoleHeight`（兩點，供骨盆 `SoleHeight` 與 footstep 材質） | 見 M3.2／M3.3 |
| K4 | **預測用平放腳姿**是已裁決的輸入差異（landing-pose contract 不採，§16.1），不是規則差異 | `docs/45` §14.4 把它描述成「兩個規則」 | 文件改寫成「同一規則、不同輸入」；⛔ 不改行為 |
| K5 | **C-D 獨立於 C-B**：聲音 onset 與釘腳是不同問題（`docs/35` 各自實測），⛔ 不得互相取代；但**參考點要寫明**：C-D 用鞋底，C-B 的 stance 用踝（差值被吸收在 0.08 的門檻裡） | `FootstepDetector` 註解寫「同一個簡化」——只在「沿世界 up」這點成立，沒寫出參考點不同 | ✅ 2026-10-08 已補註解與 `docs/45` §28；不改行為 |
| K6 | **詞彙**：`plant` 只指 C-A；T3 一律叫 `lock`；C-F 叫 `swing destination`；`landing` 只指 C-G | `Plant` 同時出現在 C-A（`NextPlant*`）、C-B（`PlantAwareExtrapolation`、`LandingPlantStillDistance`）、C-D（`FootPlantTracker`／`FootPlantPhase`）；`Landing` 同時指 C-F 與 C-G | 見 M3.4（可選） |

### 3.3 C-E 的三份規則（唯一真正的 duplicate）

```text
T3 接觸（ResolveContactPosition:575-620）
  movement surface 參考 → 實際腳姿的 heel/ankle/toe 三點 → AccumulateContact：最大穿透 → supportY
預測落點（TryPredictGroundedAnkle:932）
  預測 root ＋ 平放腳 → heel/ankle/toe 三點 → 取最高踏面               ← 平放時與「最大穿透」等價
SampleGround（:382-460）
  踝單點 → heel/toe 兩點 → ComputeSoleHeight → FootSample.GroundY      ← 不同規則
     ├─ MovingPelvisFoot.SoleHeight / HasFloor（骨盆，與 T3 的 Target 混用）
     └─ FootIKGroundData.Collider（footstep 材質）
```
前兩份的規則等價、輸入不同，可以共用一個函數。第三份規則不同；T3 開啟時它的位置結果已被覆寫，`GroundY` 卻仍流進骨盆。這就是 `docs/47` F-06。

### 3.4 Migration path

| 步 | 改什麼 | 路由 | 驗證 | 行為差異 | 還原 |
|---|---|---|---|---|---|
| **M3.0** | 寫下 K1–K6：ADR-010 D5 的語意說明補 K1（Trial 非 schema 修訂）；`docs/05`／`docs/45` 補 K2–K5 | 文件 only | — | 無 | — |
| **M3.1** | 抽出 C-E 的單一純函數（輸入：三點水平位置＋各點腳底高度＋遮罩；輸出：選中點、踏面高度）；T3 與預測兩處改呼叫它 | Living Spec（`docs/05`） | EditMode：純函數邊界（階緣兩側、平放 vs 抬跟）；樓梯 36 探針 **T3／預測輸出逐位元相同** | 無（兩處目前等價——**必須用探針證明，不能只靠推導**） | revert |
| **M3.2** | T3 開啟時，`MovingPelvisFoot.SoleHeight`／`HasFloor` 改用 T3 的 support（C-E），不再用 `FootSample.GroundY` | Living Spec（`docs/46`） | 樓梯探針前後對照（骨盆目標／輸出 ΔY、tread flip，沿用 §33／§34 指標）＋人工 Play | **骨盆數值會變**（平地不變；階緣處變） | 改回 `left.GroundY` |
| **M3.3** | `FootIKGroundData` 發布 T3 實際支撐的 collider（T3 關閉時退回 `SampleGround`） | Living Spec（`docs/36`） | EditMode（材質解析）＋樓梯轉角人工聽感 | 跨材質交界的腳步材質可能改變 | 改回 |
| M3.4（可選） | 依 K6 改名：`PlantAwareExtrapolation` → lock 用語、`LandingSurface*` → `SwingDestination*`、`FootPlantTracker` → `FootstepTracker`。⚠️ `FootIKSettings` 欄位改名需要 `FormerlySerializedAs`（X Bot prefab 有序列化值）；C# 型別名不影響序列化 | Living Spec | 編譯＋EditMode＋Unity 匯入後確認 prefab 值保留 | 無 | revert |
| ⛔ 不做 | 讓 Bake plant 依賴 T3 門檻；預測加入落地腳姿；遲滯 | 已裁決（§16.1） | — | — | — |
> ✅ **2026-10-08 完成 M3.0**：K1 寫入 ADR-010 D5（Trial 非 schema 文字修訂＋§9 紀錄）；K1–K6 的子系統正本寫入 `docs/45` §28（`docs/05` 有使用者未 commit 的改動，本輪不碰）；`FootstepDetector` 註解補上參考點差異。行為不變。
> ✅ **2026-10-08 完成 M3.1**：`FootContactSolver.ConsiderSupport` 成為 C-E 唯一規則，T3 與預測落點共用；樓梯 probe（12 次、3078 帧）重構前兩次／後一次逐帧比對，runtime 欄位**全部相同**（只有 `frame` 與 probe 自己的反事實射線不同，兩次 baseline 之間就不同）。附帶：同一組三點命中上「最高踏面 vs 最大穿透」**0／6073** 不同 ⇒ §14.4 的分歧來自腳姿輸入（K4）。詳見 `docs/45` §28.1。M3.2／M3.3 依 D3-b 延後為 behavioral slice。

### 3.5 開放問題（列出、不提案）
- **鎖的釋放能否參考 C-A？** 10-03 的裁決禁止的是 **Bake 依賴 T3**，不是 **T3 參考 C-A**。`docs/45` §22.4 的補回問題發生在釋放端：鎖住期間動畫腳已經離開 C-A 的接觸區。因此理論上可以讓 T3 在「C-A 的這次接觸已結束」時提早釋放。限制有三：只有停步 clip 有離散的 C-A（loop／blend 沒有）；R1c（d0 = 3 cm）剛裁決；這會讓 Presentation 的 IK 依賴 Core 發布的動畫事件，是新的耦合。**本提案不推薦現在做**，只記錄這個方向存在。

### 3.6 ❓ 決策點
1. ✅ **D3-a**（2026-10-08：隨 F3 方向接受）：同意以「C-A 與 C-B 刻意分離＋K1 契約」作為正式定位（不再把兩者的差異當成 bug 追）嗎？
2. ✅ **D3-b**（2026-10-08）：暫不排 runtime 修改；先完成 M3.1 並以樓梯 probe 證明逐帧等價；pelvis 改 authority 另開 behavioral slice。
3. ✅ **D3-c = Yes，但只做 terminology cleanup**（2026-10-08）：不做全域 rename；新文件／API／註解統一用 Animation Plant／Predicted Contact（Landing）／IK Lock／Committed Surface，舊欄位碰到相關區域時再逐步改名（對照表見 `docs/45` §28 K6）。原問題：M3.4 的改名要不要做（純詞彙債，成本是 `FormerlySerializedAs`）？

---

## 4. 順序與相依

```text
M1.0 ─ M1.1 ─ M1.2 ─ M1.3            （F1：可先做；只有 M1.3 改行為，需 D1-b）
            │
            └─ enum 放 Core.Blackboard ─┐
M2.0 ─ M2.1 ─┬─ M2.2（新 ADR，需 D2-d）─ M2.4（需 D2-b/c）─ M2.5（需 D2-a）
             └─ M2.3（ADR-011，可獨立）
M3.0 ─ M3.1 ─ M3.2 ─ M3.3             （F3：M3.0／M3.1 不改行為，隨時可做）
docs/47 G-3（骨盆管線收斂）會連帶刪掉 F1 的 StandingPostureSolver 那一份
```
- **不需要任何裁決、也不改行為的**：M1.0、M1.1、M2.0、M2.1、M3.0、M3.1。
- **改行為、必須人工 Play 的**：M1.3、M2.3、M2.4、M2.5、M3.2、M3.3。
- 每一步都是獨立 commit，可單獨 revert。

> 🔄 **2026-10-08 更新（§0.2、§6）**：M2.1–M2.5 停止；M2.0 不做（§6.5）。
> F1 的 `StopTier` **不再與 F2 共用 enum**——§6 結論是沒有全域 tier ⇒ M1.2 的欄位型別應是**停步專用**的（例如把 `LocomotionStopTier` 的 Walk／Run 鏡像到 `Core.Blackboard`），在 M1.2 開工時於 ADR-010 D6 定案。
> 目前可繼續：M1.0、M1.1、M3.0、M3.1。M1.2／M1.3 方向已裁決（D1-b），但不在「no-behavior-change」範圍內，等開工指示。

## 5. 本提案刻意不做的事
- 不建立 `StopService`／`GaitManager` 一類的集中服務；所有 owner 都是既有元件。
- 不新增 interface（唯一的介面變更是 `IFootPhaseSource` **刪掉**一個參數）。
- 不碰 grounding、失地時間窗、T3 的鎖定門檻與 R1c、烘焙 plant 判準。
- 不把骨盆的停步語意改綁到停步 clip（見 1.1）。

---

## 6. F2 語意檢查：`BodySpeedTier` 是 domain fact，還是 convenience representation？（2026-10-08，research-only）

> **判準（使用者裁決）**：只有在**語意、邊界、更新時機、owner 四者都一致**時，才允許提升成黑板的 canonical field。
> ⛔ Mixer 的 0.55 中點**不是既有事實**，只是對連續 blend 的一種量化規則。本節不把它當輸入。

### 6.1 先釐清 repo 裡**確實存在**的相關事實

| 事實 | 性質 | 出處 |
|---|---|---|
| `MoveSpeed`（smoother 輸出，0–1） | **canonical、連續**；mixer 參數就是它 | `LocomotionModel.cs:165,169`；WriterRule |
| Mixer 有四個 child：Idle／WalkFwdLoop／RunFwdLoop／SprintFwdLoop，門檻 0／0.35／0.75／1.0，`_SynchronizeChildren` 全關 | Presentation 資產；**每個 child 有自己的時鐘** | `Locomotion.asset`；ADR-011 §1 |
| 三支 loop 的 authored 腳相原點**不同** | 動畫事實 | `docs/07` §0.2 |
| 「主導 child」＝權重 argmax，同權重取較前者 | **只是取樣規則**：用來決定「讀哪一個時鐘當腳相」；⛔ 不是 tier | `AnimancerFacade.TryGetDominantChildNormalizedTime`；`docs/07` §0.2 |
| Core 不認識 Mixer／child index／clip | 架構原則 | `docs/07` §0.2；`LocomotionModel.cs:170` 註解 |
| Stop 帶刻意放在「該 loop 主導」的區間內（Walk 0.35–0.50、Run 0.75–0.875，上界＝同權重取 Run） | **Stop 自己的資料約束**，文件有寫、**沒有測試** | `docs/07` §0.1–§0.2 |

**實測（live Editor，唯讀）**：把某支 loop 的時鐘交給另一支 loop 的 Bake 解讀時，選錯腳的比例：

| 實際被讀的時鐘 | 用來解讀的 Bake | 選錯腳（占循環的比例） |
|---|---|---|
| WalkFwdLoop | RunFwdLoop | **26.4%** |
| RunFwdLoop | WalkFwdLoop | **26.4%** |
| SprintFwdLoop | RunFwdLoop | 3.3% |
| RunFwdLoop | SprintFwdLoop | 3.3% |

（2000 個 normalized time 取樣，`GetFootPhaseAt` 比較。）

### 6.2 逐一檢查五個 consumer

| | **Stop**（`LocomotionStopSelector.SelectTier`＋`SelectByEntryPhase`） | **Jump**（`JumpState.SelectSpeedTier`＋`TryGetCurrentLocomotionPhase`） | **FootIK**（規則⑦閘門） | **Footstep**（`AudioController`／`FootstepAudioBankSO`） | **ADR-011**（`ShouldAlignRunEntryPhase`） |
|---|---|---|---|---|---|
| **①真正需要的是什麼** | **actual speed**（放開前一刻的 smoother 速度）判斷哪一組 **authored stop clip** 能自然接上；再用主導時鐘＋**同一支 loop 的 Bake** 選 LU／RU | (a) **authored jump clip family**（Idle／Walk／Run 起跳；落地沿用起跳快照）——應對應起跳當下身體在播的東西；(b) LU／RU：需要的是「**被取樣的那個時鐘屬於哪支 clip**」，不是 tier | **committed stop tier**（D1-b 已裁決） | 一個 **Presentation policy**：每一步用哪一組音色。可能的輸入有 control gait（現行）、連續強度、這一步的運動學（tracker 已量到下降速度）——沒有哪一個是被迫的 | 這次起跑**Run child 會不會在畫面上被看見**（它的任意相位才會造成滑步）——是目標強度下 Run child 的**連續權重**，加上「從靜止起跑」與朝向條件 |
| **②需要離散 Walk／Run 嗎** | 需要——在離散的 authored 資產集合 {Walk Stop, Run Stop, 無} 中選一個 | (a) 需要（離散的 variant 集合）；(b) **不需要**，需要的是時鐘身分 | 需要，但它是**繼承** Stop 的決定，不是自己分類 | 音色池是離散的，但選擇規則可以是連續輸入的函數；**不需要**「身體 tier」 | 不需要；是一個 yes／no 閘門，底層量是連續權重 |
| **③邊界** | 0.35–0.50／0.75–0.875，**中間與兩端留空 ⇒ 無停步**；上界還受 RU 尖峰速度約束（`docs/07` §0.1：4.696 m/s 錨點） | 意圖 ≤ 0.2 Idle／< 0.75 Walk／≥ 0.75 Run（**分割、無空隙**，輸入是原始意圖） | 無自己的邊界 | 無數值邊界（按鍵） | 現行：按鍵 Run；若改成正確的量，會是「目標強度下 Run 權重 ≥ w*」——需要 mixer 門檻，而 Core 刻意不知道它 |
| **④更新時機** | 放開那一帧取樣一次，之後整段停步承諾（generation） | 進入 Jump 那一帧一次；落地沿用 | 停步承諾期間 | 每一個腳步事件、每隻腳 | 意圖上升緣（從靜止起跑）一次 |
| **⑤owner** | `LocomotionModel`（停步 policy） | `JumpState`＋`JumpStateParams`（玩家／敵人各一份） | 生產者 `LocomotionModel`；FootIK 只消費 | `AudioController`（Presentation） | `LocomotionModel`（ADR-011 D1） |

### 6.3 判定

| 判準 | 結果 |
|---|---|
| 語意一致？ | ❌ 五個問題：「哪組停步 clip 接得上」、「哪組跳躍 clip＋哪個時鐘」、「已承諾的停步是哪組」、「哪組音色」、「Run child 會不會被看見」 |
| 邊界一致？ | ❌ 帶狀有空隙／分割無空隙（輸入還不同：速度 vs 意圖）／繼承／無／連續權重 |
| 更新時機一致？ | ❌ 放開／起跳／承諾期間／每步／起跑緣 |
| owner 一致？ | ❌ Model（stop）／JumpState／Model（ADR-011）／Audio；FootIK 不是分類者 |

> **結論：`BodySpeedTier` 是 convenience representation，不是 canonical domain fact。不提升、不開 ADR（D2-d 的前提不成立）。**
> 五個 consumer 共用的真正 canonical 底層只有兩個，而且**都已經存在**：連續的 `MoveSpeed`（Core）與 mixer 的 child 集合（Presentation 資產）。
> 各 consumer 的離散化屬於它們自己的 policy，邊界不同是**正確的**，不是碎裂。
> `docs/47` F-02 的「四個定義」因此改判：Stop／Jump／ADR-011／Footstep 各自分類＝healthy specialization；**真正的問題是下面兩個，都不需要新的 tier**。

### 6.4 剩下的兩個真問題

**(1) FootIK 用 control gait 回答「停步 clip 是哪組」** ⇒ 已由 F1／D1-b 處理（M1.2／M1.3）。

**(2) 🆕 Jump 的 phase pairing 不變式沒有被保證——F2-J**

**不變式**：*腳相必須用「被取樣時鐘所屬的那支 clip」的 Bake 來解讀。* 這是一條**契約**，不是欄位。

- Stop **靠資料滿足**它：Stop 帶落在該 loop 主導的區間內（`docs/07` §0.2）——有文件，**沒有測試**。
- Jump **不滿足**：tier 由**原始意圖**的 0.2／0.75 決定；被取樣的時鐘由**實際速度驅動的 mixer 權重**決定。兩者在下列情況分歧，而 Walk↔Run 錯配會選錯腳 26.4%（§6.1 實測）：

| 情況 | 意圖 → Jump tier | 身體速度 → 主導時鐘 | 錯配 |
|---|---|---|---|
| 戰鬥中側移／後退（`XBotSpellDirectionalSpeedProfile`：R 0.488、L 0.467、B／BL／BR 0.477；model 內部取 `min`，黑板上的意圖不變） | 0.75 → **Run** | 0.47–0.49 → **Walk** 時鐘 | Run Bake 讀 Walk 時鐘 |
| 類比搖桿穩態 0.55–0.75 | **Walk** | **Run** 時鐘 | Walk Bake 讀 Run 時鐘 |
| 起步／放開 Walk 鍵後的加減速期（accel 0.12 s／decel 0.18 s） | 已是目標 tier | 仍是前一個時鐘 | 短暫 |
| Sprint（1.0） | Run | Sprint 時鐘 | 3.3%（輕微） |
| 敵人（Y Bot） | — | — | 不受影響：`EnemyJumpStateParams` 沒有 loop Bake ⇒ 一律退到 Idle 家族 |

- 嚴重度：**Medium**（同類缺陷 `docs/07` §0.2 的「同腳連踩」已經在 Stop 上修過一次）。⚠️ 以上是**資料＋實測的組合推導**，畫面上是否看得出來尚未 Play 驗證。
- 修法方向（列出、**不提案**、都不需要全域 tier）：
  - **J1**：Jump 比照 Stop，只在「該 loop 主導」的速度區間才承諾 Walk／Run 家族（輸入改成實際速度），其餘退到 Idle 家族——與 Stop 同一種資料約束。
  - **J2**：讓腳相查詢一併回報「被取樣的是哪個 child」，由 model 以自己持有的 loop Bake 對應——⚠️ 會碰 `docs/07`「Core 不認識 child index」的原則，需另議。
  - **J3**：先為現有的 Stop 約束補一條測試（Stop 帶 ⊂ 主導區間），再決定 Jump。⚠️ 讀 mixer 門檻是否觸及 Gate B（Animancer 私有序列化）要先確認；只讀不寫，與 B11 的「寫入」顧慮不同。

### 6.5 M2.x 的處置

| 步 | 處置 | 理由 |
|---|---|---|
| M2.0 | **不做** | 它要把 Gait／Stop tier／Jump tier 並排鎖住，前提是「三者是同一件事的不同版本」——§6.3 已否定 |
| M2.1–M2.4 | **停止** | 都以 `BodySpeedTier`／tier 表成立為前提 |
| M2.5 | **撤銷** | D2-a：音效獨立處理 |
| ADR-011 的 `Gait == Run` | **保留現狀，記為已知限制** | 起跑對齊是 Run-only 的窄範圍 Trial；類比半推時誤觸發的成本很低（後端只重設權重 0 的 child），不需要為它建立 tier。真正的量（Run child 權重）若要用，屬於 ADR-011 自己的 Trial 修訂 |

### 6.6 ❓ 決策點
1. **D2-e**：F2-J（Jump phase pairing）要不要開一個**量測 slice**（戰鬥側移起跳、類比 0.6 起跳的 LU／RU 實際對錯）？量完再在 J1／J2／J3 之間選。
2. **D2-f**：同意把 `docs/47` F-02 改判為「各 consumer 自有 policy＝healthy；真問題＝F1 的 gait 代理＋F2-J」嗎？

---

## 7. F2-J 量測結果：Jump 的 clock-family 錯配（2026-10-08，measurement-only，⛔ runtime 未改）

> 依 D2-e。Probe：`Assets/_Project/Tests/PlayMode/JumpClockFamilyProbe.cs`（throwaway）；資料與腳本：`docs/artifacts/jump-clock/`。
> 150 次起跳（10 情境 × 15 個時間 offset），X Bot、空曠平地、1/60 固定步長。

### 7.1 量測前先發現的三個輸入事實（會改變「哪些情境玩家真的碰得到」）
| 事實 | 證據 | 影響 |
|---|---|---|
| **X Bot 的 `MoveAction` 只綁鍵盤 WASD**，沒有搖桿 | prefab 序列化（唯讀） | 玩家目前**產生不了**類比強度；`docs/47` F-02 與本檔 D1-b 舉的「搖桿半推」是**假設情境**。本輪以「只在測試場景實例加一條搖桿綁定」量它，標為 hypothetical |
| **`SprintAction` 沒有綁定**；Shift 是 Roll | prefab；Sprint 情境 15／15 進了 `RollState` | 玩家目前到不了 Sprint ⇒ 玩家的意圖只有 0.3651（Walk）與 0.75（Run）兩個值 |
| X Bot `usePersistentCombatFacing = 0` | prefab；smoke test：戰鬥中面向在約 25 帧內轉向移動方向 | 戰鬥方向限速只在**轉向暫態**生效；穩態側移／後退＝一般 Run |

### 7.2 每個情境：是否真的發生 clock-family 錯配
| 情境 | 可達性 | 意圖 → Jump 家族 | 實際速度 | 被取樣的時鐘 | 家族錯配 | 腳選錯（對被取樣時鐘自己的 Bake） | 腳選錯（對動畫 pose） |
|---|---|---|---|---|---|---|---|
| Walk（Ctrl） | ✅ 鍵盤 | 0.3651 → Walk | 0.365 | Walk ×15 | 0／15 | 0 | 0 |
| Run（W） | ✅ | 0.75 → Run | 0.75 | Run ×15 | 0／15 | 0 | 0 |
| Combat 側移（轉 90° 後 0–28 帧起跳） | ✅ | 0.75 → Run | 0.75 | Run ×15 | 0／15 | 0 | 0 |
| Combat 後退（轉 180°） | ✅ | 0.75 → Run | **0.553–0.746** | Run ×15 | 0／15 | 0 | 1（混合區） |
| Turn 側移／後退（非戰鬥對照） | ✅ | 0.75 → Run | 0.75 | Run ×30 | 0／30 | 0 | 0 |
| **RunAccel**（靜止起跑後 0–14 帧起跳） | ✅ | 0.75 → Run | 0.02–0.69 | Idle ×4、Walk ×6、Run ×5 | **10／15** | 0 | 2 |
| **WalkToggleOn**（跑步中按 Ctrl，減速期起跳） | ✅ | 0.3651 → Walk | 0.46–0.73 | Run ×10、Walk ×5 | **10／15** | **4** | 9 |
| **WalkToggleOff**（走路中按 Ctrl，加速期起跳） | ✅ | 0.75 → Run | 0.41–0.72 | Walk ×6、Run ×9 | **6／15** | **3** | 6 |
| Analog 0.6（hypothetical） | ❌ 目前不可達 | 0.6 → Walk | 0.6 | Run ×15（權重 0.625） | **15／15** | **5** | 6 |

- **runtime 確實會發生錯配**，但只在 Walk↔Run 的**加減速暫態**（鍵盤可達：Walk 切換、靜止起跑）與假設的類比輸入。穩態 Walk／Run、戰鬥側移／後退都沒有（戰鬥後退最低降到 0.553，剛好在 Walk／Run 權重交叉點上方，Run 時鐘仍主導）。
- 敵人（Y Bot）不受影響：`EnemyJumpStateParams` 沒有 loop Bake ⇒ 一律 Idle 家族（§6.4）。
- RunAccel 的 10 次錯配**沒有一次選錯腳**：起跑頭幾帧 Walk 與 Run Bake 對同一個時鐘值給出相同腳相（Idle 時鐘那 4 次沒有真值可比）。

### 7.3 畫面上是否可見（代理量測）
代理：起跳後 10 帧內「左右腳最終骨骼的水平間距」最大單帧變化，對照起跳前 9 帧的正常步態（比值 > 1 ＝ 起跳時腳的相對位置比正常步伐跳得更猛）。

| 起跳時的錯誤類型 | n | 起跳後最大單帧變化 | 正常步態 | 比值（平均／最大） |
|---|---|---|---|---|
| 無錯誤 | 103 | 0.137 m／帧（max 0.243） | 0.163 | 0.88／1.75 |
| 只有混合區 pose 不一致 | 6 | 0.090（max 0.114） | 0.170 | 0.56／0.75 |
| 家族錯配、但腳相對 | 15 | 0.086（max 0.137） | 0.121 | 0.79／1.75 |
| 家族錯配、腳相對時鐘對、對 pose 錯 | 4 | 0.069（max 0.078） | 0.155 | 0.50／0.78 |
| **家族錯配 ＋ 對被取樣時鐘選錯腳** | **12** | **0.185（max 0.309）** | 0.120 | **1.52／2.05** |

- 表中共 140 次：RunAccel 最早的 10 次起跳前不足 9 帧正常步態可對照，未列入（它們都沒有選錯腳，見 §7.2）。
- **只有「用 A loop 的時鐘查 B loop 的 Bake、因此選錯腳」那一組出現不連續**（12 次＝WalkToggleOn 4、WalkToggleOff 3、Analog 5）；與 `docs/07` §0.2 修過的「同腳連踩」是同一類缺陷。
- 家族錯配本身（Jump 用 Walk 起跳 clip、身體在播 Run）**沒有**造成不連續；混合區 pose 與單一時鐘不一致也沒有。
- ⚠️ 這是幾何代理，**不是**人工 Play 的視覺判斷。玩家可達的觸發窗口很窄：按 Ctrl 後約 0.05–0.15 s 內起跳，且約一半的步相才會選錯腳。

### 7.4 解讀
- 可見問題的根因是 **phase pairing**（被取樣的時鐘 ↔ 解讀它的 Bake），不是 Jump 的家族 policy。⇒ 若要修，最小範圍是「腳相必須用被取樣時鐘那支 loop 的 Bake 讀」；起跳 clip 家族要不要跟意圖走，可以維持現狀（家族錯配本身沒有量到可見後果）。
- Stop 不受影響：它的速度帶在設計上落在該 loop 主導的區間（`docs/07` §0.2），本輪沒有量到任何 Stop 側的錯配。
- 🔎 附帶推導（**未量測**）：鍵盤可達的 D1-b 情境不是搖桿半推，而是「跑步中按 Ctrl 後立刻放開 W」——此時 `Gait = Walk`，但放開時的實際速度仍在 Run 帶 ⇒ 播 **Run Stop**，而現行 FootIK 用 `Gait == Walk` 判斷，會對 Run 停步套用只批准給 Walk 的規則⑦。M1.3 的驗收要把這個情境放進去。

### 7.5 ⏳ 人工 Play 確認（視覺）
1. 跑步（W）→ 按一下 Ctrl（切到 Walk）→ **約 0.1 s 內**按 Space。重複多次（步相不同才會觸發）。
2. 走路（Ctrl 開）→ 按一下 Ctrl（切回 Run）→ 約 0.1 s 內按 Space。
3. 對照：穩定跑步／走路中起跳。
看的是：起跳瞬間兩腳是否「交換位置」或剪刀狀閃一下。

### 7.6 ❓ 決策點
1. **D2-g**：要不要修 Jump 的 clock-family ownership？量測支持「有、但窄」——鍵盤可達、代理指標可見、觸發窗口 ≤ 0.15 s。
2. 若修，範圍選哪個（皆未實作）：
   - **J2′（建議的最小範圍）**：腳相一律用「被取樣時鐘所屬 loop」的 Bake 讀；起跳家族維持意圖選擇。需要讓 Core 知道被取樣的是哪支 loop——`docs/07` §0.2 的「Core 不認識 child index」原則要另議（例如由 model 回報「我讀的是哪支 loop」，而不是 facade 回報 child index）。
   - **J1**：Jump 比照 Stop，以實際速度＋主導區間選家族；家族與腳相一起跟身體走。會改變起跳 clip 家族（例：跑步中切 Walk 後立刻跳，會播 Run 起跳）。
