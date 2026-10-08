# 48 — Canonical Concept Proposal：Stop／Locomotion Tier／Foot Contact（2026-10-08）

| | |
|---|---|
| **狀態** | 🟠 **Proposed**——⛔ **不得作為實作基線**。本檔只定義概念、owner、producer、consumer 與 migration path；runtime／資產／測試皆未動 |
| **輸入** | `docs/47` F-01（Stop ownership）、F-02（Gait／locomotion tier）、F-03（plant／lock／landing） |
| **路由** | F1 → ADR-010（Trial）獨立擴充 D6，比照 D5；F2 → 需要**新 ADR**（黑板新欄位；ADR-003 已 Accepted、不可改）＋ ADR-011（Trial）原地修訂；F3 → Living Spec（`docs/05`／`docs/45`），不需 ADR |
| **閱讀方式** | 每一節最後的「❓ 決策點」是要你裁決的；其餘是推導 |

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

### 1.5 ❓ 決策點
1. **D1-a**：同意「骨盆的停步＝S-A（意圖），規則⑦的 Walk＝S-B（clip tier）」這個切法嗎？
2. **D1-b**：搖桿半推觸發的 Walk 停步，是否在規則⑦的批准範圍內？（M1.3 會讓它生效；不同意的話，閘門應寫成 `StopTier == Walk ∧ Gait == Walk`，代表規則⑦限縮為「Walk 鍵」，而且要在 `docs/46` §19 寫明原因）

---

## 2. F2 — Gait／Locomotion Tier

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
| K5 | **C-D 獨立於 C-B**：聲音 onset 與釘腳是不同問題（`docs/35` 各自實測），⛔ 不得互相取代；但**參考點要寫明**：C-D 用鞋底，C-B 的 stance 用踝（差值被吸收在 0.08 的門檻裡） | `FootstepDetector` 註解寫「同一個簡化」——不正確 | 改註解／文件；不改行為 |
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

### 3.5 開放問題（列出、不提案）
- **鎖的釋放能否參考 C-A？** 10-03 的裁決禁止的是 **Bake 依賴 T3**，不是 **T3 參考 C-A**。`docs/45` §22.4 的補回問題發生在釋放端：鎖住期間動畫腳已經離開 C-A 的接觸區。因此理論上可以讓 T3 在「C-A 的這次接觸已結束」時提早釋放。限制有三：只有停步 clip 有離散的 C-A（loop／blend 沒有）；R1c（d0 = 3 cm）剛裁決；這會讓 Presentation 的 IK 依賴 Core 發布的動畫事件，是新的耦合。**本提案不推薦現在做**，只記錄這個方向存在。

### 3.6 ❓ 決策點
1. **D3-a**：同意以「C-A 與 C-B 刻意分離＋K1 契約」作為正式定位（不再把兩者的差異當成 bug 追）嗎？
2. **D3-b**：M3.2 會改骨盆在階緣的數值——要排進下一個骨盆 slice，還是先擱置？
3. **D3-c**：M3.4 的改名要不要做（純詞彙債，成本是 `FormerlySerializedAs`）？

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

## 5. 本提案刻意不做的事
- 不建立 `StopService`／`GaitManager` 一類的集中服務；所有 owner 都是既有元件。
- 不新增 interface（唯一的介面變更是 `IFootPhaseSource` **刪掉**一個參數）。
- 不碰 grounding、失地時間窗、T3 的鎖定門檻與 R1c、烘焙 plant 判準。
- 不把骨盆的停步語意改綁到停步 clip（見 1.1）。
