# 09 — Multi-Action ／ Action Identity（實作規格）

> **本檔是 `docs/ADR/005-multi-action-identity.md` 的 Living Spec。**
> ADR 只凍結 D1–D5 五條決策；**其餘所有細節在本檔，且允許在 Trial 期間依實作發現修改**（ADR-005 §8 清單）。
>
> ✅ **狀態：ADR-005 已於 2026-09-02 翻牌為 `Trial`**（前置的 ADR-004 同日 `Accepted`，P-0 結案）。
> 本檔自此**是目前的實作基線**，P-A 可以開工。
>
> 📌 引用本檔時必須註明狀態：**使用者已裁決方向 ≠ 工程上已驗證。**

---

## 1. 本輪範圍

### 1.1 目標

讓一個角色同時持有**多個 Action**，並以此支撐作品集的三個展示技能：

| 展示名 | 形狀 | 側效果 | 動畫來源 |
|---|---|---|---|
| **Quick Spell** | 短前搖 → 投射物 | 生成 projectile | Human Spellcasting Animations FREE（快速法術） |
| **Ice Spell** | 短前搖 → 投射物 → **命中後 Slow** | 生成 projectile ＋ 對目標施加減速 | Human Spellcasting Animations FREE（冰系施法） |
| **Melee Slash** | 揮擊 → 命中窗 | 開啟／關閉 hitbox | `EEJANAI_Team/FreeSwordAnimations` 既有 `slash*` |

### 1.2 In Scope

- Action **identity** 概念，及其五個消費者（輸入映射／冷卻／HUD／external request／中斷規則）
- 一個角色持有**多份** `ActionDefinitionSO`
- **per-action 冷卻** ＋ 冷卻 HUD
- **Action → Action 中斷**（FU-1）
- **Slow effect**：唯一使用者、最小實作
- **Targeting／Facing 降級版**：只做到三個技能可信所需的程度

### 1.3 Non-goals（明確不做）

| 不做 | 理由 |
|---|---|
| 完整 Aim 系統／瞄準準星／AimPoint 投射 | 原 WP1 的存在理由是救 Throw 手感，前提已消失（2026-09-02 裁決） |
| 長前搖／蓄力／channel 技能 | Throw 已證明 release timing，資訊增量不足以換 scope |
| 通用 `StatusEffect`／`Buff` framework | ADR-005 **D5**；本輪 Slow 是唯一使用者 |
| 血量／傷害數值／死亡 | 展示不需要。命中的可見結果由「敵人播 Damage 動畫」＋「Slow」承擔 |
| Throw 的任何手感調整 | 2026-09-02 裁決：Throw 僅作 ADR-004 Acceptance 證據，**不出現在作品集影片** |
| 刀光／特效商店資產 | 先用 `TrailRenderer` ＋ 既有 particle；可讀性是驗收目標，不綁特定資產 |
| 物件池（projectile） | 既有 polish 桶項目，未進入任一評估軸 |

---

## 2. 現況盤點（2026-09-02，依磁碟核對）

### 2.1 可直接重用（零或近零程式）

| 能力 | 現況 | 本輪如何用 |
|---|---|---|
| 多 phase 骨架 | `ActionPhaseEntry[]`：`Phase`／`AnimationKey`／`Bake`／`FallbackDuration`／`Interruptible`／`WaitForTrigger`／`EmitsRelease`／`ReleaseNormalizedTime` | **三個技能都只需要 `Start`（＋可選 `End`）**。不需要新 phase、不需要 `Loop`、不需要 `WaitForTrigger` |
| 側效果接縫 | `IActionLifecycleSink`：`Begin()`／`Release()`／`Cleanup()`，呼叫時點由 `ActionState` 單一持有 | **最大一筆重用**。法術發射器、近戰 hitbox、既有投擲器＝同一介面的三個實作。**不需要新 seam** |
| release 時點 | `EmitsRelease` ＋ `ReleaseNormalizedTime` ＋ `_releaseEmittedThisExecution` 去重 | 近戰「揮到 40% 開命中窗」與法術「35% 出手」是同一機制、同一欄位 |
| 動畫映射 | `AnimancerFacade.transitionMappings`：`List<{StateKey, Transition}>` 字串鍵查表 | **加動畫＝Inspector 加一列，零程式** |
| 位移 | `MotionBakeData` ＋ `MotionDriver.ExecuteBakedCurveMovement`；無 Bake 自動退回 `ExecuteBaseMovement` | 三者皆站定動作 ⇒ **可先不做 Bake**，用 `FallbackDuration` 即可跑通 |
| 命中 → 對方反應 | `ThrownProjectile.OnTriggerEnter` → `ActionRequestTarget.RequestAction()` → 敵人播 Damage | 法術投射物照抄同一條鏈 |
| 打斷矩陣 | `StateRule.CanBeInterruptedBy` ＋ 逐 phase `Interruptible` | 資產層可調，不需程式 |

> 📌 **盤點結論：Quick Spell 幾乎是免費的。** 它 ＝ Throw 砍掉 `Loop`／`WaitForTrigger` ＋ 換一份 Definition ＋ 換一個 prefab ＋ 加一列動畫映射。

### 2.2 真正的缺口

| # | 缺口 | 磁碟證據 | 擋住 |
|---|---|---|---|
| **B1** | 一角色只能有一份 `ActionDefinitionSO` | `ActionState.Initialize` → `config.GetStateParams<ActionDefinitionSO>(Type)`；根因在 `StateMachineConfigSO` 的 `_paramsMap`／`_bakeMap`／`_priorityMap`／`_interruptMap` **全以 `StateType` 為鍵** | **整個工作包**（＝FU-2） |
| **B2** | 輸入只有一顆 `FireRequested` | `IntentData` 僅有 `JumpRequested`／`RollRequested`／`FireRequested`；`CharacterPipelineRunner.ProcessIntents` 三行對應 | 三技能三個鍵（＝ADR-005 D4） |
| **B3** | **完全沒有 hit／damage／effect 系統** | `ThrownProjectile` 命中後唯一動作是 `target.RequestAction()`。**無血量、無傷害、無狀態** | Ice 的 Slow（本輪唯一「真的新東西」） |
| **B4** | 沒有朝向／瞄準 | `PlayerRuntimeData.AimTarget` 是**死欄位**——全 repo 僅 `CharacterPipelineRunnerEditor` 除錯面板讀取，**無任何 writer**（故未進 `WriterRules`）。投射物方向 ＝ `transform.rotation` | 法術往哪飛 |
| **B5** | Action→Action 中斷不可能 | `FullBodyStateMachine.EvaluateInterrupts` 首行 `if (targetState.Type == _currentState.Type) continue;` | 揮劍被打斷（＝FU-1） |

> B1／B2／B5 即 `docs/08` §11.1 已登記的 **FU-2／FU-3／FU-1**。
> **本輪方向沒有製造新問題，它精準落在既有登記表上。**

---

## 3. Action Identity

> ⚠️ **本節描述需求與候選，不定案。** 表示法與容器形狀屬 ADR-005 §8 的不凍結清單。

### 3.1 五個消費者

| 消費者 | 用 identity 做什麼 |
|---|---|
| **輸入映射** | Q／E／滑鼠左鍵 → 哪一個 Action |
| **冷卻** | 每個 Action 各自的到期時間 |
| **HUD** | 查詢某個 Action 的冷卻進度以繪製圖示 |
| **External request** | `ActionRequestTarget`：「我被打到」vs「我要出手」 |
| **中斷規則** | Action A 能否打斷 Action B |

**D1 的意思**：這五者用**同一把鍵**。任一方自造第二把鍵即違反 ADR-005 D1。

### 3.2 候選比較（2026-09-02 由 ADR-005 移入——實作分析不該住在 ADR）

| 方案 | R1 | R2 | R3 | R4 | R5 | R7 | 主要代價 |
|---|:--:|:--:|:--:|:--:|:--:|:--:|---|
| **A. `int` slot index** | ✅ | ✅ | ✅ | ⚠️ | ✅ | ✅ | **匿名**：資產排序改變即行為改變；「slot 3」對敵人的受擊語意毫無意義（R4 弱） |
| **B. `enum ActionSlot`** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 新增 slot 需改程式（enum 加一員）——比照 `StateType` 先例，且該成本**本來就該被看見** |
| **C. `ActionDefinitionSO` 參照** | ⚠️ | ✅ | ❌ | ⚠️ | ✅ | ✅ | **違反 R3**：Presentation 得持有 Core authored SO；同一份 Definition 無法被兩個 slot 共用 |
| **D. 每 Action 一個 `StateType`** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | **已由 ADR-004 §5.2 否決**（拓撲爆炸）。列出僅為完整性 |

**A 與 B 的真正差別不在效能，在「身分有沒有語意」。** 敵人的 `Damage` 是一個 **reaction**，不是「第 3 號技能」。R4 要求身分承載這個區別，A 做不到。

⇒ **實作採方案 B**（`enum ActionSlot`）。ADR-005 D1 不凍結表示法，若程式證偽可直接改。

### 3.3 開放問題（實作期必須回答，答案回填本節）

1. `Damage` 是主動 slot 還是獨立的 reaction 身分？
2. slot 數量固定或動態？固定的話上限是多少（YAGNI 判斷）？
3. 多份 Definition 掛在 `ActionState` 內、`StateMachineConfigSO` 索引擴張、或獨立 SO 清單？三者 Inspector 可維護性差異需實測。
4. identity 是否需要進入 `ActionDefinitionSO` 成為 authored 欄位，還是純粹由容器位置決定？

---

## 4. 三個 Action 的資產配置（草案，實作期可調）

> 皆為站定動作 ⇒ `Bake` 可留空，先用 `FallbackDuration`。

| Action | Phases | `EmitsRelease` | `Interruptible` | Sink 實作 |
|---|---|---|---|---|
| **Quick Spell** | `Start` | `Start` ＠ ~0.35 | `Start` = false | 法術投射物發射器 |
| **Ice Spell** | `Start` | `Start` ＠ ~0.35 | `Start` = false | 同上（不同 prefab） |
| **Melee Slash** | `Start` | `Start` ＠ ~0.40 | `Start` = false | 近戰 hitbox 開關 |

**唯二的新 runtime 程式**（都是 `IActionLifecycleSink` 實作，各約 40 行，形狀比照既有 `ThrowProjectileEmitter`）：

1. **法術投射物發射器** — `Release()` 生成 projectile。可能直接沿用 `ThrowProjectileEmitter`（僅換 prefab 與速度），實作期判斷是否需要獨立類別。
2. **近戰 hitbox 開關** — `Release()` 開啟命中窗、`Cleanup()` 關閉。

> ⚠️ **紅線**：VFX **不得**決定命中判定時機。命中窗由 `ActionPhase` ＋ `ReleaseNormalizedTime` 決定，
> particle collision **不得**成為命中來源。這是 `CLAUDE.md`「Do NOT put gameplay logic inside Animation」的同構延伸。

---

## 5. 輸入配置

### 5.1 鍵位（2026-09-02 使用者裁決：Q／E 為技能鍵）

| 鍵 | Action | 備註 |
|---|---|---|
| **Q** | Quick Spell | 新增 InputAction |
| **E** | Ice Spell | 新增 InputAction |
| **滑鼠左鍵**（既有 `FireAction`） | Melee Slash | **P-0 結案前仍指向 Throw**；ADR-005 實作時才移交 |

### 5.2 各層的改動點

| 層 | 改動 | 性質 |
|---|---|---|
| `InputSystem_Actions.inputactions` | 新增兩顆 action | **使用者側資產，AI 不碰** |
| `PlayerInputSource` | 新增對應 `InputAction` 欄位 ＋ `Enable`／`Disable` ＋ `FetchRawInput` 採樣（比照既有 `SprintAction`／`WalkAction` 先例：未綁定恆 false，不綁也能正常遊玩） | 加法 |
| `InputData`（`ref struct`） | 新增兩顆 `*ButtonDown` | 加法 |
| `IntentData` | **ADR-005 D4：`FireRequested` 改為帶 identity 的 request** | **黑板 schema 變更** |
| `CharacterPipelineRunner.ProcessIntents` | 對應改寫（順序 2） | 熱路徑，需零 GC 複驗 |

⚠️ **`InputData` 是 `ref struct`**（只能存活於 Stack）——新增欄位不影響該性質，但實作時不得改變它的 `ref struct` 宣告。

---

## 6. 冷卻與 HUD

### 6.1 現況 ownership

`ActionState._cooldownEndTime`，private 欄位，於 `OnExit` 寫入 `Time.time + _definition.Cooldown`，於 `CanEnter` 讀取比較。
**它是 per-state 的，不是 per-action**——今天只有一個 Action，兩者恰好重合。

`ActionState` 實例活在 `FullBodyStateMachine._stateRegistry` 內，與角色同生命週期 ⇒ **跨 `OnExit` 保存沒有問題**，本輪不需要處理「離開後誰持有」。

### 6.2 本輪的形狀

- per-action 冷卻的執行期狀態**住在 `ActionState` 內部**（ADR-005 **D2**）。儲存形式不凍結。
- HUD **按 identity 查詢**，不按 `ActionDefinitionSO` 參照（需求 **R3**：Presentation 不持有 Core authored SO）。
- 曝光給 Presentation 的路徑**是黑板 schema 變更**，走 ADR-005 **D4** 的同一次裁決，**不另開 ADR、也不因為「只是 UI」走例外**。

### 6.3 為什麼不是一組全域 `CooldownRemaining` ／ `CooldownDuration`

該方案（2026-09-02 早先提案，**已否決**）假設「同時只有一個冷卻」。多 Action 落地後即失效，且會讓 HUD 無法表達「三個技能各自的冷卻」——而那正是 HUD 存在的唯一理由（讓外行辨識「這是**多個獨立**技能」）。

**冷卻 HUD 在結構上是 B1 的下游，不能獨立設計。**

---

## 7. Slow Effect（唯一使用者，最小實作）

### 7.1 設計

敵人的移動意圖由 `AIMovementSource` 寫入 `MovementIntent`（`WriterRules` 已登記）。
`MovementIntent` 下游掛著**已經完成**的整條管線：`LocomotionSpeedSmoother` 平滑 → `LocomotionModel` 速度階層 → `LocomotionStopSelector` 停步選片 → Foot IK → 腳步音。

⇒ **Slow ＝ 在該 producer 上乘一個係數。**

### 7.2 為什麼這是本輪最強的架構證明

| 讀者 | 看到什麼 |
|---|---|
| **外行（HR）** | 敵人被冰打到 → 明顯變慢 → **而且跑步動畫自己變成走路動畫、腳步聲自己變疏**。他不知道為什麼，但看得出「這遊戲有反應」 |
| **技術主管** | Slow **沒有告訴任何人它存在**。速度階層不知道、Stop 選片不知道、Foot IK 不知道、音效不知道，**全部自動正確** |

**這比 Throw 的 release timing 強**：release timing 是時間精度（外行看不出難度），Slow 是**跨系統自動傳播**（外行看得到結果，內行看得懂原因）。

### 7.3 紅線

- ⛔ 不建立 `StatusEffect`／`Buff`／`Modifier` 通用機制（ADR-005 **D5**）
- ⛔ `MovementIntent` 的**寫入者仍然唯一**——Slow 是 producer 內部的係數，**不是第二個寫入者**。`WriterRules` 的 `MovementIntent` 白名單**不得**因此變長
- ⛔ 不得讓 Action 系統認識目標的移動系統（那是反向依賴）

### 7.4 驗收（＝ADR-005 Acceptance **G**）

`LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController`
**五個檔案零修改**，而敵人的速度階層、停步選片、腳步節奏全部自動正確。
**任一檔案為了 Slow 而被改動，本條即未通過。**

---

## 8. Targeting ／ Facing（降級版 supporting infrastructure）

> 2026-09-02 裁決：Camera／Aim **不完全砍掉**，降級為支援設施——**只做到三個技能展示所需**，不再以 Throw 為中心，不追求完整 Aim 系統。

### 8.1 本輪要達到的最低程度

- 出手瞬間角色**面向目標**，使投射物飛行方向可信、近戰命中窗有意義
- miss 可歸因於自己，而不是看起來像系統壞掉

### 8.2 邊界

- **朝向是 Presentation 關切**，比照 WP1 原本要證明的「相機／瞄準是純 Presentation」負面證明
- **不需要黑板 schema 變更**（與 §5 的 D4 變更無關，兩者不得混為一談）
- `PlayerRuntimeData.AimTarget` 死欄位：**本輪必須處置**——要嘛給它真正的 writer 並登記進 `WriterRules`，要嘛移除。留著一個無 writer 的公開 setter 是遲早會被 A5 抓到的破口

### 8.3 開放問題

朝向的觸發點（出手瞬間一次性轉向 vs 持續朝向最近敵人）與目標選擇規則，屬實作細節，實作期決定後回填本節。

---

## 9. 測試計畫

### 9.1 EditMode（擴充既有 `ActionStateTests.cs`）

- 多份 Definition 下，各 identity 的冷卻**互不干擾**
- identity 不匹配的 request **不觸發**任何 Action
- Action→Action 中斷依規則表生效（FU-1）
- 既有 Throw 行為等價回歸（P-0 的成果不得被破壞）

### 9.2 架構不變量（`ArchitectureRegressionTests.cs`，**Claude 獨佔**）

| 不變量 | 守什麼 |
|---|---|
| **A13′ 維持** | `StateType` 恆六員——ADR-005 **D3** 不新增成員 |
| **A19 維持** | `ActionState` 不長子類別 |
| **A5 擴充** | 新 schema 欄位登記進 `WriterRules`，寫入者唯一 |
| **🆕 identity 單一來源** | 守 ADR-005 **D1**：五個消費者不得各自造鍵 |
| **🆕 Slow 無擴散**（＝Acceptance G 的機器化） | 五個 locomotion／presentation 檔案不得出現 Slow 相關符號 |

> 📌 依 `CLAUDE.md`「Test-as-Spec」：**新增不變量優先寫成測試而非散文**——同一個 artifact 同時給你enforcement 與最便宜的摘要。

### 9.3 Play（證據不足，不得猜測）

- 三個技能各自可觸發、冷卻可見、互不干擾
- Slow 命中後敵人動畫**自動降階**（跑 → 走），腳步音節奏跟著變
- 零 GC：穩態 `0 B/frame`（dev-spec §7.4 SOP）

---

## 10. 檔案邊界

### 10.1 允許改動（Runtime）

`IntentData`／`InputData`／`PlayerInputSource`／`CharacterPipelineRunner.ProcessIntents`／
`ActionState`／`ActionDefinitionSO`／`StateMachineConfigSO`／`FullBodyStateMachine.EvaluateInterrupts`／
`ActionRequestTarget`／`AIMovementSource`（Slow 係數）／新增 `IActionLifecycleSink` 實作 ＋ HUD

### 10.2 ⛔ 不得改動

`LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController`
（＝Acceptance **G** 的名單）、`MotionDriver` 的位移路徑、`AnimationFacadeBase` 契約、`IPresentationController` 契約

### 10.3 使用者側（**AI 不碰**）

`.prefab`／`.asset`／`.meta`／場景／`InputSystem_Actions.inputactions`／所有 Git 操作

---

## 11. 工作包順序

| 包 | 內容 | 前置 |
|---|---|---|
| ~~**P-0**~~ | ~~ADR-004 Acceptance~~ ✅ **2026-09-02 結案** | — |
| **P-A** | ADR-005 翻牌 Trial ＋ identity 實作 | P-0 |
| **P-B** | 三個 Action 純資產化 | P-A |
| **P-C** | Slow effect | P-A |
| **P-D** | 冷卻 HUD | P-A |
| **P-E** | 可讀性 pass（`TrailRenderer` ＋ 既有 particle，不買資產） | P-B |
| **P-F** | 敵人遭遇（原 WP3） | P-B／P-C |

~~**P-0 是硬前置**~~ ✅ **已滿足**（2026-09-02）。**下一個開工項目是 P-A。**

---

## 12. FU 登記（本輪發現、超出範圍者）

| # | 發現 | 何時處理 |
|---|---|---|
| **FU-09-1** | `PlayerRuntimeData.AimTarget` 為無 writer 的死欄位 | 本輪 §8.2 一併處置（不得延後——A5 破口） |
| **FU-09-2** | `ThrownProjectile` 與未來法術投射物可能重複，是否抽共用 projectile | **第二個投射物出現後**再談；本輪先複製，禁止預先抽象 |
