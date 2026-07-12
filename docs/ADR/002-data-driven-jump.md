# ADR-002：數據驅動跳躍與多段跳架構（Data-Driven Jump & Multi-Stage Jump）

| 欄位 | 內容 |
|---|---|
| 狀態 | **Accepted**（本輪落地：Jump Stages ＋ Designer Tuning 倍率 ＋ 物理逆推；Movement Assistance／Variable Jump 留待後續 ADR，見 §6-4） |
| 日期 | 2026-07-12 |
| 關聯文件 | `docs/ADR/001-root-model-hierarchy.md`、`docs/01-design-doc.md`、`docs/02-dev-spec.md`、`docs/changelog.md`（v0.10「VerticalVelocity 移入黑板」待補項） |
| 影響模組 | `JumpState`、`JumpStateParams`、`StateMachineConfigSO`、`MotionDriver`、`MotionBakeData`（唯讀消費） |
| 前置事實 | 烘焙器已能自動提取 `AutoTakeoffDelay` / `AutoApexHeight` / `AutoAirTime` / `AutoCalculatedGravity`（見 `MotionFeatureAnalysis.cs`） |

---

## 1. 背景 (Context)

烘焙工具（`MotionBakeEditor` + `MotionFeatureAnalysis`）已能逐 clip 自動提取跳躍物理特徵，但執行期的跳躍卻仍靠**手動硬編碼**，導致同一個物理量存在**兩個真相來源**：

| 物理量 | 烘焙端（自動、逐 clip） | 執行期端（手動硬編碼） | 現況 |
|---|---|---|---|
| 起跳前搖 | `MotionBakeData.AutoTakeoffDelay` | `JumpStateParams.TakeoffDelay`（例：0.75） | 兩份，靠人肉對齊 |
| 起跳初速 | 可由 `AutoApexHeight` + `AutoCalculatedGravity` 逆推 | `JumpStateParams.ImpulseForce`（例：7.5） | 手填，與動畫脫節 |
| 重力 | `MotionBakeData.AutoCalculatedGravity` | `MotionDriver.gravity = -9.81`（固定） | 烘焙值完全沒被用到 |

`RollState` 已示範正解：透過 `config.GetBakeData(Type)` 直接消費自己那支 clip 的 `MotionBakeData`。Jump 沒走這條路，才產生脫節。

同時，未來的「二段跳／前翻跳／不同前搖」會撞上三個硬編碼卡點：
1. `StateType` 是寫死的 enum（`None/Idle/Move/Jump/Roll`）。
2. `FullBodyStateMachine.Initialize()` 的狀態註冊是手列的（`new JumpState()`…）。
3. Interrupt 系統以 `targetState.Type == _currentState.Type` 跳過同型，故「跳躍中再按跳」**無法**靠狀態轉移實現。

---

## 2. 決策 (Decision)

### 2.1（Q1）單一真相來源：Jump 消費 `MotionBakeData`，`JumpStateParams` 瘦身

- Jump 的每段物理數據來自各段 clip 的 `MotionBakeData`；`JumpState.Initialize()` 透過既有的 `config.GetStateParams<JumpStateParams>(Type)` 取得 `JumpStateParams`，再由其 `Stages` 清單逐段取得 `MotionBakeData`（比照 `RollState` 以 config 查表消費 bake 數據的精神；地面跳 = `Stages[0]`，詳見 §2.2）。
- **拔除** `JumpStateParams.TakeoffDelay`（改吃各段 `MotionBakeData.AutoTakeoffDelay`）與 `JumpStateParams.ImpulseForce`（改由各段 apex + gravity 逆推）。
- **`JumpStateParams` 瘦身保留**，承載**跳躍內容（Content）**與**沒有動畫來源的「手感旋鈕」（game feel）**，完整分類見 §2.1.1。
- **原則**：物理量 → 烘焙資產（唯一真相）；手感量 → 參數資產；零重複。
- **明確劃線**：`AutoTakeoffDelay` / `AutoApexHeight` / `AutoCalculatedGravity` 是**逐 clip 靜態常數（config）**，一律走 `StateMachineConfigSO` 查表，**不進 `PlayerRuntimeData`（黑板）**。把靜態常數塞進黑板會污染黑板、憑空多出只有 Jump 在乎的欄位、破壞「每欄位單一寫入者」原則。

#### 2.1.1 `JumpStateParams` 內容完整分類（動畫資料 vs 設計師參數）

```
JumpStateParams
└── Game Feel
    ├── Movement Assistance（移動輔助）
    │   ├── Coyote Time                        離地後仍可起跳的寬容秒數
    │   └── Jump Buffer                        落地前預先輸入、落地即觸發的緩衝秒數
    ├── Variable Jump（可變跳躍高度）
    │   ├── Min Hold Time                      最短按住時間（早於此放開＝最低跳）
    │   ├── Max Hold Time                      最長按住時間（達此＝滿跳）
    │   └── Early Release Gravity Multiplier   提早放開時的重力倍率（加速下墜、縮短跳躍）
    ├── Multi Jump（多段跳）
    │   └── Jump Stages                        有序段清單；每段引用一份 MotionBakeData
    └── Designer Tuning（設計師微調倍率）
        ├── Height Multiplier                  apex 高度倍率（乘在 AutoApexHeight 上）
        ├── Gravity Multiplier                 重力倍率（乘在 AutoCalculatedGravity 上）
        └── Launch Velocity Multiplier         起跳初速倍率（乘在逆推出的 v 上）
```

**動畫資料 vs 設計師參數的區分（本分類的重點）：**

| 類別 | 內容 | 來源 | 承載處 |
|---|---|---|---|
| **動畫資料** | `AutoTakeoffDelay` / `AutoApexHeight` / `AutoCalculatedGravity` | 烘焙器自動產出，逐 clip | **各 `JumpStage` 引用的 `MotionBakeData`（唯一真相）** |
| **跳躍內容（Content）** | `Jump Stages`（有序段清單） | 設計師組裝 | `JumpStateParams` |
| **設計師參數（無動畫來源）** | Coyote Time、Jump Buffer、Min/Max Hold Time、Early Release Gravity Multiplier、Height / Gravity / Launch Velocity Multiplier | 設計師手調 | `JumpStateParams` |

- **動畫資料**藏在每段 `Jump Stage` 引用的 `MotionBakeData` 裡，`JumpStateParams` 只**引用不複製**——維持單一真相。
- **設計師參數**沒有動畫來源，是純 game feel。其中三個 `Multiplier` 是**疊在動畫資料之上的倍率**，於 §2.3 的 `v = √(2gh)` 推導中作為係數，**預設 1.0；當三者皆為 1.0 時，§2.3 的物理自洽性精準成立**（apex 精準命中 `AutoApexHeight`）。

> **本 ADR 的落地範圍**：`Multi Jump（Jump Stages）` 與 `Designer Tuning（三個 Multiplier）` 由本 ADR 定義並落地（皆不需新架構）。`Movement Assistance（Coyote Time / Jump Buffer）` 與 `Variable Jump（Min/Max Hold / Early Release Gravity Multiplier）` 的**執行期行為**依賴「跨幀計時狀態的擁有權」與「按住/放開輸入訊號」——兩者的 Owner/Writer/Reader 與輸入來源**本 ADR 尚未定義**，依專案規範不得自行發明，故其**欄位與行為留待後續 ADR**（見 §6），此處僅先在分類中定位，不落地成無行為的死設定。

### 2.2（Q2）多段跳：泛化 `JumpState` 為「數據驅動多段跳躍器」

核心：**段數是數據，不是狀態。** 一個 `JumpState` 內部持有一份**有序段清單**與一個「本次離地後已跳次數」計數器，計數器 index 進清單選當前段。

- **設定端**：`JumpStateParams` 進化為「跳躍定義資產」，持有 `有序 List<JumpStage>`。每個 `JumpStage` = { 該段的 `MotionBakeData` + 可選的每段手感 }。地面跳 = 第 0 段，空中跳 = 第 1、2… 段。
- **邏輯端（只寫一次）**：
  - `CanEnter`：`Intent.JumpRequested && IsGrounded`（地面起跳）。
  - `OnTick`：偵測空中再按跳（讀 `Intent.JumpRequested`，在 Update 消化，趕在 Pipeline 幀尾 `Reset` 前）；若 `已跳次數 < 段數`，推進段數並標記「本幀要注入下一段」。空中再跳**在 `JumpState` 內部消化，不走狀態轉移**（因 interrupt 不自我重入）。
  - `OnUpdateMotion`：以「當前段」的 `MotionBakeData` 注入該段初速與重力。
- **閉環**：新增「三段跳」或「第二段換成前翻跳」= 在資產清單**加一個元素 / 換一個 baked asset**，邏輯層一行不改。
- **誠實揭露成本**：第一次從單段 → 多段，`JumpState` 需改一次（加段迴圈與計數）；之後才是純資產閉環。「新增一段邏輯零改」在泛化完成後成立。
- **定案（段數的職責歸屬）**：`JumpStateParams.Stages` 是**跳躍內容（Content）的唯一來源**——「這個角色的跳躍由哪幾段組成」完全由 `Stages` 定義。執行期可跳段數的上限**就是 `Stages.Count`**，`JumpState` 以「已跳次數 `< Stages.Count`」為閘門；**本 ADR 不新增任何「可用段數 / 段數上限」欄位**。若未來需要 RPG 成長、能力解鎖、Buff 等**動態限制實際可用段數**（例如買到二段跳前只能跳一段），該邏輯屬於**能力系統（Ability System）的職責**，由它管理「當前可使用段數」並**另立 ADR** 明確定義其 Owner / Writer / Reader；本 ADR 不預先為此保留欄位或黑板狀態。
- **範圍外**：讓**所有動作**（不只跳躍）純資產化 = 走「States-as-Data / `StateType` 分層」，屬 changelog 開放問題，**另立 ADR**，不在本 ADR。

### 2.3（Q3）物理逆推 + 重力接縫（選項 A）

- **公式**：`v = √(2·g·h)`，其中 `g = AutoCalculatedGravity`、`h = AutoApexHeight`。此值恰等於 `g·t_air/2`；只要上升與下落用**同一顆** `AutoCalculatedGravity`，角色 apex 會**精準落在** `AutoApexHeight`（模型自洽：分析器本就以 `g = 8h/t_air²` 逆推）。
- **由誰算**：`JumpState` 算（`Initialize()` 逐段預算並快取），**不由 `MotionDriver` 算**。`MotionDriver` 維持「不知道跳躍／動畫、只做積分」的笨執行器（呼應 ADR-001 物理/表現隔離）。
- **施加時機**：沿用既有延遲注入——`_stateElapsedTime >= AutoTakeoffDelay` 時，注入初速 **並切換當前重力為 `AutoCalculatedGravity`**。延遲期間貼地走 `ExecuteBaseMovement`。此舉讓**物理離地時機與動畫前搖 100% 對齊**，正是脫節問題的根治。
- **接縫（選項 A）**：`JumpState` 注入時把「初速 + 重力」一起交給 `MotionDriver`，以一個 `readonly struct` **`JumpLaunchData`** 描述本次發射（`= { float InitialVerticalVelocity; float Gravity; }`）。命名採 `...Data` 尾綴，與 `MotionBakeData` / `JumpStateParams` / `PlayerRuntimeData` 一致——它是一份**資料契約（Data）**，不是事件（Event）或命令（Command）。`MotionDriver` 持有 `_activeGravity`，離地期間用它積分，`IsGrounded`（落地）時於 `GetGravityThisFrame` 內部自然重置回預設。**`_verticalVelocity` / `_activeGravity` 的寫入者仍只有 `MotionDriver`**——值是數據驅動的，欄位所有權不外流，守住「單一寫入者」。
- **水平/垂直隔離**：`MotionDriver` 現有結構已是 `horizontalVelocity + GetGravityThisFrame()`。跳躍時水平仍相機相對 procedural，垂直換 baked 拋物線，天然可分離，無需大動。

---

## 3. Zero-GC 合規

- `Mathf.Sqrt`、float 運算、`readonly struct` 傳值 → 全在 stack，零 heap 配置。
- `v` 於 `Initialize()` 逐段預算 → 一次性；即使每次起跳算也只是幾個 float op。
- `List<JumpStage>` 活在 config 資產（載入一次）；執行期只做 `list[index]` O(1) 取參考，零配置。
- **紀律**：不得在 `OnTick` / `OnUpdateMotion` 做 string interpolation 或 `new`；既有 `JumpState` 的彩色 `Debug.Log` 建議比照 `CharacterPipelineRunner` 用 `#if UNITY_EDITOR` 包起來。

---

## 4. 所有權與依賴合規

- 全案是 `State → Motion` 的方法呼叫（`OnUpdateMotion` 既有合法路徑），未觸犯 `Motion→Input`、`State→Controller`、`Controller→Animation API`。
- **本 ADR 不新增任何 `PlayerRuntimeData` 欄位**。垂直速度仍封裝於 `MotionDriver`。
- 既有欄位的 Owner/Writer/Readers 全部不變。

---

## 5. 後果 (Consequences)

### 正面
- 跳躍的前搖、初速、重力全部**單一真相來源（烘焙資產）**；改動畫重烘焙即自動生效，設計師不需再手對數字。
- 多段跳擴充在泛化後**純資產閉環**。
- 物理離地時機與動畫前搖精準對齊，根治「先蹲下再往上」類時序問題的殘餘。

### 代價
- 一次性泛化 `JumpState`（加段迴圈/計數）與擴充 `MotionDriver` 注入 API。
- `JumpStateParams.asset` 與 `Bake_Jump.asset` 需重新配置（見遷移）。
- 依賴 `Bake_Jump.asset` 的 Auto 特徵已正確烘焙；若該 clip 的 `Root Transform Position(Y) → Bake Into Pose` 被勾選會採不到上升量，`AutoApexHeight≈0`，此時逆推退化、初速退回 fallback（安全但非期望）。

---

## 6. 明確劃出的範圍外（Deferred）

1. **`VerticalVelocity` 移入黑板**（changelog v0.10 待補項）：等出現**第二個垂直速度消費者**（wall-slide／擊飛／電梯）再做，屆時再重新界定 owner/writer/readers。本 ADR 用選項 A 不提前打開封裝。
2. **`MotionBakeData` 命名空間搬遷**：目前位於 `Project.Presentation.Motion` 卻被 `Project.Core.StateMachine` 消費（Roll 已如此），屬 Core→Presentation 反向依賴。可搬到中性 Data/Config 命名空間，另立小 ADR。
3. **States-as-Data**：全動作資產化 / `StateType` 分層。
4. **`Movement Assistance`（Coyote Time / Jump Buffer）與 `Variable Jump`（Min/Max Hold / Early Release Gravity Multiplier）的執行期行為**：需要「跨幀計時狀態的擁有權（Coyote/Buffer 計時器該由誰持有並每幀推進）」與「按住/放開的輸入訊號（目前 `InputData` 僅有按下邊沿）」。兩者本 ADR 未定義，另立 ADR 明確其 Owner/Writer/Reader 與輸入擴充後再落地欄位與行為。
5. **能力系統驅動的「可用段數」限制**：RPG 成長／解鎖／Buff 動態限制實際可跳段數，屬能力系統職責，另立 ADR 定義 Owner/Writer/Reader（本 ADR 只以 `Stages.Count` 為內容上限，不含動態限制）。

---

## 7. 落地時的文件責任（實作時一併處理）

- `docs/01-design-doc.md`：新增「跳躍數據流 / 多段跳」小節，並在 Trade-off 表補一列。
- `docs/02-dev-spec.md`：補跳躍數據流與 `JumpStage` 資料結構規格、`MotionDriver` 注入 API 契約。
- `docs/changelog.md`：新增 v0.13 條目，並把「`VerticalVelocity` 入黑板」標註為「仍待第二消費者」。

---

## 8. 資料形狀（契約層描述，非實作）

> 以下僅描述欄位契約與職責，不含實作程式碼；實作時再依此落地。

**`JumpStage`（新，序列化結構）**
- `MotionBakeData Bake`：該段對應 clip 的烘焙資料（提供 `AutoTakeoffDelay` / `AutoApexHeight` / `AutoCalculatedGravity`）。
- 註：三個 `Multiplier` 屬 `JumpStateParams` 全域層級（Designer Tuning），**不放在每段**，故 `JumpStage` 僅持有 `Bake`。

**`JumpStateParams`（進化後——本 ADR 落地欄位）**
- 移除：`TakeoffDelay`、`ImpulseForce`。
- 新增（Content）：`有序 List<JumpStage> Stages`（第 0 段 = 地面跳；可跳段數上限即 `Stages.Count`，**不另設欄位**）。
- 新增（Designer Tuning，預設 1.0）：`HeightMultiplier`、`GravityMultiplier`、`LaunchVelocityMultiplier`。
- **不在本 ADR 落地**（見 §2.1.1 範圍註與 §6-4）：`CoyoteTime`、`JumpBuffer`、`MinHoldTime`、`MaxHoldTime`、`EarlyReleaseGravityMultiplier`。

**`JumpLaunchData`（新，`readonly struct`，資料契約）**
- `float InitialVerticalVelocity`：逆推並套用倍率後的起跳初速（向上為正）。
- `float Gravity`：本次發射採用的重力大小（正值；= `AutoCalculatedGravity × GravityMultiplier`）。

**`MotionDriver` 注入 API（契約）**
- 現有 `ApplyJumpImpulse(float)` → 改為 `ApplyJumpLaunch(in JumpLaunchData launch)`（唯一呼叫端為 `JumpState`）。
- `_activeGravity` 由 `ApplyJumpLaunch` 設定、落地（`IsGrounded`）時於 `GetGravityThisFrame` 內部重置回預設；寫入者僅 `MotionDriver`。

**`JumpState` 執行期狀態（內部，不進黑板）**
- 已跳次數計數器（int，落地清零）；閘門為「已跳次數 `< Stages.Count`」。
- 逐段預算的 `JumpLaunchData` 快取（於 `Initialize()` 依各段 `Bake` + 三個 Multiplier 算好）。
