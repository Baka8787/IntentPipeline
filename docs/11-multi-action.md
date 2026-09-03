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
| ~~完整 Aim 系統／瞄準準星／AimPoint 投射~~ | 🔄 **2026-09-02 合併時修正**：原文以「WP1 的存在理由是救 Throw 手感，前提已消失」判它出局——**該判斷是在看不到本機工作樹的遠端容器裡做的**。事實是 WP1 已實作完成並 commit（`AimResolver`＋`ThirdPersonCamera` 改造＋`ThrowProjectileEmitter` 依 AimPoint 發射）。**它不是本輪要做的事，而是本輪的既有基礎**——本卷不再重做，但也不得寫成「不做」 |
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
| **B4** | 瞄準已有系統；黑板曾留有死欄位 | WP1 的 `Presentation/Camera/AimResolver` 以 `TryGetAimPoint()` 直供消費端，不寫黑板。`PlayerRuntimeData.AimTarget` 無 writer、僅被 Editor 面板讀取，已於 2026-09-03 連同面板列移除 | ✅ 法術方向與死欄位皆已解 |
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

### 3.3 開放問題 —— ✅ **已由實作回答（2026-09-02，code-first）**

| # | 問題 | 答案 | 依據 |
|---|---|---|---|
| 1 | `Damage` 是主動 slot 還是獨立 reaction 身分？ | **獨立的 `Reaction` 成員**，且**沒有輸入來源**——只由 `ActionRequestTarget` 提交 | 這正是 FU-3 要的區別。若讓 `Damage` 佔一個主動 slot，「我被打到」與「我要出手」又會混在一起 |
| 2 | slot 數量固定或動態？ | **固定 enum，目前五員**。冷卻陣列以 `(int)ActionSlot.Reaction + 1` 定尺寸 | 動態容器換來的彈性沒有使用者，且會犧牲 O(1) 查表與零配置 |
| 3 | 多份 Definition 放哪？ | **`StateMachineConfigSO` 新增一條平行的 `actionDefinitions` 清單 ＋ `ActionSlot` 索引** | 既有四張表全以 `StateType` 為鍵，**改它們的形狀會波及 Jump／Roll 等與 Action 無關的狀態**。平行索引是影響面最小的解 |
| 4 | identity 要不要成為 `ActionDefinitionSO` 的 authored 欄位？ | **要**。`ActionDefinitionSO.Slot`，因此清單是扁平的，不需要 mapping struct | 身分寫在資產自己身上，排序改變不影響行為（這正是否決 `int` index 的理由） |

### 3.4 🔴 實作推翻的假設（2026-09-02）

**① `ActionSlot` 的層級放錯了。**
原本放 `Core/StateMachine/Actions/`（與 `ActionDefinitionSO`／`ActionPhase` 同層）。但 `ThrownProjectile` 位於
Presentation，而 `LayerRules` 禁止 Presentation 出現 `Project.Core.StateMachine` ⇒ **A4 會紅**。

⇒ **移到 `Core/Actions/`**，與 `ActionRequestTarget`／`IActionLifecycleSink` 同層。
**身分屬於跨層 seam 層，不屬於 FSM 層**——`Core/Actions/` 存在的理由本來就是「Presentation 也被允許認識的最窄介面」。
📌 這個結論是**架構測試教的**，不是設計時想到的。它也是 A4 這條不變量第一次產生正面價值（過去只用來擋錯誤）。

**② Action→Action 重入的第一版有 priority 繞過。**
初版在 `EvaluateInterrupts` 迴圈裡遇到同型別就直接 `TransitionTo` 並 return。後果：
**字典迭代順序決定結果**，且重入會**繞過比它優先的狀態**（例：Roll 閃避應該打斷技能，卻可能被另一個技能的重入搶先）。
⇒ 改為「重入候選與其他候選走同一套 priority 比較」。

**③ `OnEnter` 產生了新的耦合（已接受，需留意）。**
Definition 不再於 `Initialize` 綁死，因此 `OnEnter` 必須**重新解析一次 request**。
結構上成立——intent 到順序 7 才復位、mailbox 到 Tick 結束才清，兩者都在 `TransitionTo` **之後**；
並加了 `Complete()` 保底（request 消失時不進入任何 phase）。
⚠️ 但這是 ADR-004 期沒有的耦合。副作用：直接呼叫 `OnEnter` 的測試現在必須先設 intent。

---

## 4. 三個 Action 的資產配置（草案，實作期可調）

> 🔄 **2026-09-02 修訂**：原文寫「皆為站定動作 ⇒ `Bake` 可留空」。**兩個法術維持如此，Melee Slash 不再是**——
> 它改為刻意承載 **motion mapping 展示**（位移由烘焙曲線驅動），見下方 4.1。

| Action | Slot | Phases | `Bake` | `EmitsRelease` | `Interruptible` | Sink 實作 |
|---|---|---|---|---|---|---|
| **Melee Slash** | `Slot1`（滑鼠左鍵） | `Start` | ✅ **必要** | `Start` ＠ ~0.40 | `Start` = false | 近戰 hitbox 開關 |
| **Quick Spell**（火／雷） | `Slot2`（Q） | `Start` | 留空 | `Start` ＠ ~0.35 | `Start` = false | 重用 `ThrowProjectileEmitter`（法術 prefab／速度） |
| **Ice Spell** | `Slot3`（E） | `Start` | 留空 | `Start` ＠ ~0.35 | `Start` = false | 同上（Ice prefab 開 `appliesSlow`） |

**為什麼三格都只有 `Start`（法術不用素材自帶的 Load／Cast 分離）**：
`Kevin Iglesias` 的 `MagicAttacks/` 每組都附 `Load`／`Cast`／合一三個檔，天生對應 `Start`／`End`。
**刻意不用**——ADR-004 的 Throw 已經展示過多 phase ＋ release timing，再做一次是重複展示同一個機制；
且需求是「**快速**施展」，快速本來就不該有蓄力段。改用合一檔、單一 `Start`。
⇒ 三格的展示價值不重疊：**Slot1 ＝ motion mapping**、**Slot2／3 ＝ 多身分共用一顆 `ActionState` ＋ per-slot 冷卻**、
**Slot3 額外 ＝ Slow 的跨系統自動傳播**。

### 4.1 Melee Slash 的 Bake（motion mapping 展示）

揮劍前衝的位移**由 `MotionBakeData` 的曲線驅動**，不是寫死的速度。這是三格裡唯一能讓外行**看得到**架構的一格
（劍揮出去、人跟著往前衝，而位移數字來自烘焙資料而非程式常數）。

> 🔴 **兩道 gate 都是安靜失敗**——`ActionState.OnUpdateMotion`：
> ```csharp
> bool hasBake = _currentEntry.Bake != null && _currentEntry.Bake.Duration > 0f;
> bool isActuallyPlaying = animationFacade.IsPlaying(AnimationKey);
> if (!hasBake || !isActuallyPlaying) { motionDriver.ExecuteBaseMovement(data); return; }
> ```
> ① **`AnimancerFacade` 的 Transition Mapping 沒接** ⇒ `IsPlaying` 為 false ⇒ 位移退回 base movement。
> ② **`BakedDuration = 0`** ⇒ `hasBake` 為 false ⇒ 同樣退回。
> 兩者都**不會 throw**。症狀是「動畫有播、人卻不往前衝」，很容易被誤判成烘焙壞掉。
> ⚠️ 重烘策略一直是「用到再烘」（目前只有 Roll 有 `BakedDuration`），**新烘 slash 務必確認它 > 0**。

**素材**：`Assets/EEJANAI_Team/FreeSwordAnimations/FBX/slash1–9.fbx`（9 顆可挑）。

#### 4.1.1 🔴 選 clip 的判準：轉向量越大，滑步越明顯

**烘焙模型是一維的**——`MotionBakeData` 把 root motion 壓成「一個純量速度曲線 ＋ 一條 yaw 曲線」，
重播時是 `transform.forward × speed` ＋ `Rotate(up, deltaYaw)`。被丟掉的是**側向位移**與**速度正負號**。

這對 `Stand To Roll`（直線前滾）吻合，對「一邊轉一邊位移」的攻擊是**有損重建**，殘差就是滑步。

> **2026-09-04 實測（`slash1`）**：`Duration 0.6s`／`AutoAverageSpeed 1.34`／峰值 `3.17`／總位移約 `0.80m`，
> 而 `RotationCurve` 由 `0` 走到 **`-94.5°`**——0.6 秒轉了 95 度。實機出現**明顯滑步**。
> ⚖️ 這不是參數問題，調不掉；**改 `Based Upon` 後重烘，數值幾乎不變**（1.3367407 → 1.3367412），
> 因為 `Based Upon` 改的是參考座標系、不是運動量。

⇒ **選 slash 時挑轉向量小、root motion 接近純前進的那一支。** 這是資產判準，不是程式問題。

⚠️ 另一個獨立問題：`slash1` 只有 18 幀（0.6s）且**本身沒有收勢段**，而本卷 §4 只給了 `Start` 一個 phase
⇒ 揮完直接回 locomotion，看起來像「沒有收回動作」。需要時補一個 `End` phase 指向收勢動畫
（`EmitsRelease` 仍留在 `Start`——命中窗在揮擊，不在收勢）。

**實作結論（2026-09-03）**：只新增一支 runtime sink。

1. **法術投射物發射器不新增類別** — 直接沿用 `ThrowProjectileEmitter`。它已把 prefab、速度、壽命、spawn point、held visual 與 `AimResolver` 全部資料化；Quick／Ice 的差異只在 prefab 與數值，另建同形類別只會複製 lifecycle 與瞄準邏輯。Ice 的 Slow 仍由 `ThrownProjectile.appliesSlow` 資產開關決定。
2. **`MeleeHitboxSink`** — `Release()` 開啟 Collider 命中窗、`Cleanup()` 關閉；固定容量記錄本次揮擊已命中的 `ActionRequestTarget`，同一目標只提交一次 `Reaction`，命中熱路徑不配置集合。

> ✅ **2026-09-03 接線缺口結案**：`CharacterPipelineRunner.actionSinkBindings` 以
> `List<ActionSinkBinding>` 表達 Slot → sink，組裝時轉成與 Definition／冷卻相同尺寸的稀疏陣列，
> `ActionState` 以已解析的 `_activeSlot` O(1) 選擇接收者。`IActionLifecycleSink` 簽章一字不動，
> sink 不接收 slot、不依身分分支；路由只決定既有 lifecycle side effect 送給誰，不形成新權威。
>
> **相容規則同 `actionDefinitions`，也是 all-or-nothing**：`actionSinkBindings` 只要有任何一筆，
> legacy `actionReleaseSinkComponent` 就完全不參與；清單為空時才把既有單顆 sink 套給所有 slot，維持舊 prefab 行為。
> 舊欄位名稱刻意保留，避免清空已序列化的 `ThrowProjectileEmitter` 引用。開始接第二／第三招時，必須一次填完整三格。

> ⚠️ **紅線**：VFX **不得**決定命中判定時機。命中窗由 `ActionPhase` ＋ `ReleaseNormalizedTime` 決定，
> particle collision **不得**成為命中來源。這是 `CLAUDE.md`「Do NOT put gameplay logic inside Animation」的同構延伸。

### 4.2 敵人近戰站位（2026-09-03 落地）

`AIMovementSource` 在 producer 內以 `minimumEngagementDistance`／`maximumEngagementDistance` 判斷站位：
太近輸出背離目標的 `MovementIntent`、太遠沿 NavMesh steering direction 前進、距離帶內輸出零意圖。
`distanceHysteresis` 讓正在前進／後退的角色必須跨過帶內的第二道門檻才停住，避免距離誤差在邊界逐幀抖動。

這三個值全是 producer 的 `[SerializeField]` 調整項；跨幀只保存 `Hold／Approach／Retreat` 私有模式，
**不回讀 FSM、不新增黑板欄位、不讓 NavMeshAgent 取得 Transform authority**。後退也只輸出方向，
實際位移仍走 `LocomotionModel → MotionDriver`。

---

## 5. 輸入配置

### 5.1 鍵位（2026-09-02 使用者裁決：Q／E 為技能鍵）

| 鍵 | Action | 備註 |
|---|---|---|
| **Q** | Quick Spell | ✅ 已綁（`Slot2Action`） |
| **E** | Ice Spell | ✅ 已綁（`Slot3Action`） |
| **滑鼠左鍵**（`Slot1Action`，原 `FireAction`） | Melee Slash | **P-0 結案前仍指向 Throw**；ADR-005 實作時才移交 |

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

### 7.4 語意（2026-09-02 使用者裁決，實作不得自行更動）

| 項目 | 定死的值／行為 |
|---|---|
| 身分 | **`Effect.Slow`**（tag 表示狀態種類） |
| 倍率 | **`MovementSpeedMultiplier = 0.3f`** |
| 語意 | **「速度剩原本的 30%」**，⛔ **不是**「降低 30%」。寫實作時最容易搞反的就是這一條 |
| 時長 | **有限時長**（`expiresAt`），不是永久。`ThrownProjectile.slowDuration` 是投遞端調整值；規格未定 N，最小切片先採 **3 秒預設** |
| 重複命中 | **刷新 duration，不疊層**。同一個 `Effect.Slow` 再命中只把 `expiresAt` 往後推 |
| 到期 | **自動移除，速度恢復**。不需要任何人來清 |
| 儲存形狀 | **tag ／ multiplier ／ expiresAt 分開存**，不是一個 effect 物件的欄位堆 |

### 7.5 投遞 seam（**刻意很薄**）

§7.1–7.3 講清楚了 Slow **住在哪**（敵人 producer 內部的係數），但沒講**命中的那一刻誰打開它**。這裡補上。

**形狀**：敵人身上掛一個「暫時 gameplay effect ／ tag 狀態」持有元件。
Projectile 命中時**只投遞**「你中了 `Effect.Slow`，倍率 0.3、持續 N 秒」，
**不知道對方拿去做什麼**；`AIMovementSource` 每幀只問「我現在有沒有 `Effect.Slow`」，有就把輸出速度乘上倍率。

```
ThrownProjectile ──(投遞 Effect.Slow)──▶ [效果持有元件] ◀──(只讀)── AIMovementSource
        │                                                              │
        └── 不認識 AIMovementSource                     └── 不認識 projectile
```

**掛載點**（唯一需要改的一行量級）：`AIMovementSource.ProduceIntent` 結尾
```csharp
data.MovementIntent.DesiredSpeedNormalized = Mathf.Clamp01(desiredSpeedNormalized);
```
⇒ 乘上「目前生效的倍率（無效果時為 1）」。**`MovementIntent` 的寫入者仍然只有它自己**（§7.3 第二條紅線成立）。

> ⛔ **這條 seam 的禁令**（比它的功能還重要）
> - **不抽介面。** 只有一個效果、一個投遞者、一個讀取者 ⇒ 具體型別直接引用。
>   第二個使用者出現前不得建 production abstraction（`CLAUDE.md` 既有禁令）。
> - **不做 stacking、抗性、優先級、複合效果、免疫、DoT。** ADR-005 **D5** ＋ 使用者 2026-09-02 明確裁決。
> - **不做 `StatusEffect` ／ `Buff` ／ GAS framework。** 沒有 effect 清單、沒有 `List<Effect>`、
>   沒有 apply/remove 生命週期回呼。**單一 slot 就夠**——「不疊層」正是這樣落地的。
> - **不得讓 Action 系統認識目標的移動系統**（§7.3 第三條）。projectile 投遞的是「狀態」，不是「速度」。

**最小實作形狀（2026-09-03）**：`TemporaryGameplayEffectState` 只有一個 slot，並以三個獨立欄位保存
`Effect` tag／`MovementSpeedMultiplier`／`expiresAt`。查詢時惰性清除到期狀態，不設 `Update`，也沒有
apply/remove callback。`ThrownProjectile` 直接投遞 `Effect.Slow` 的 0.3 倍與 `slowDuration`；
`AIMovementSource` 直接讀取同一具體元件。這是單一使用者 seam，不是 production abstraction。

### 7.6 驗收（＝ADR-005 Acceptance **G**）

> ✅ **本節已通過（2026-09-02 使用者 Play 實測）**：減速生效／到期自動恢復／重複命中不疊層／
> **且跨系統自動傳播成立**——敵人跑步動畫自己變走路、腳步聲自己變疏、停步選片自己降級，五檔零修改。
> 📌 **這條是本輪架構價值最高的證明**：Slow 沒有告訴任何人它存在。

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
- ✅ `PlayerRuntimeData.AimTarget` 已於 2026-09-03 移除，Editor 面板的唯一讀取列同步刪除。`AimResolver` 維持 `TryGetAimPoint()` 直供 Presentation 消費端；沒有為保留死欄位而虛構 writer

### 8.3 朝向規則（🟡 2026-09-02 使用者裁決方向，**尚未實作**）

> **規則：所有 Action 在起手前，一律先朝向鎖定中的目標。**
> 「所有」是重點——**不是每個 Action 自己決定要不要轉向**。

**為什麼是全域規則而不是逐 Action 的欄位**：只要有一個 Action 不轉向，玩家就無法預測命中方向，
「miss 可歸因於自己」（§8.1）當場破功。轉不轉向若做成 authored 欄位，等於把一致性交給資產填寫者，
那是遲早會不一致的地方。⇒ **由單一位置強制**，不下放到 Definition。

| 項目 | 裁決 |
|---|---|
| 觸發點 | **起手瞬間一次性轉向**（使用者用詞是「**先**朝向」） |
| 目標 | **鎖定中的目標** |
| 適用範圍 | **所有 Action**，含 Melee Slash、兩個法術；`Reaction` 不適用（受擊不是出手） |

#### 🔄 目標來源已改（2026-09-02 使用者裁決）

**不採 persistent lock-on。** 改為 **Action-time soft auto-target**：**Action 發動的那一刻**依 camera forward、
距離與角度自動選最佳目標，**僅供 facing／targeting 使用**，不改 locomotion、不進入持續鎖定狀態。
⇒ 上文的「鎖定中的目標」應讀作「**本次出手當下自動選中的目標**」。

這與既有機制對得上：`AimResolver` 已經在做「沿相機射線 cast → 過濾 `ActionRequestTarget` → 排除自己 →
取角度偏差最小者」。⇒ **auto-target ＝ 在出手瞬間取一次那個結果**，不是新的 targeting 系統。
⛔ 不得新建 `ITargetable`／目標列表／註冊表／targeting service（`docs/10` §3-D3 既有禁令，仍然適用）。

⚠️ **本輪不實作**（2026-09-02 批次範圍外）——先讓三招打得順。若 Play 顯示 facing 明顯難看再補。
`docs/10-lock-on.md` 整體延後，狀態已同步更新。

#### 開放（實作期決定，不在本輪）

- **強制點放哪**：`ActionState` 起手時統一 `MotionDriver.RequestFacing`（單一位置、但 Core 要碰 facing），
  或由各 sink 自行處理（分散、易不一致）。**傾向前者**，但需確認不會變成第二個 facing 權威。
- 揮劍**過程中**要不要持續追向目標（使用者只裁決了「先朝向」，未及於持續）。
- 無目標時的行為（維持當前朝向 vs 朝相機正前方）。

⚖️ 朝向仍是 **Presentation 關切**（§8.2），本規則不改變這一點，**不需要黑板 schema 變更**。

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
| **A26 近戰命中時機** | `MeleeHitboxSink` 必須走 `IActionLifecycleSink` ＋ `Reaction`，且不得出現 ParticleSystem／particle collision 命中來源 |

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

## 11.5 實作紀錄（2026-09-02，code-first 第一輪）

> ⏳ **尚未在任何 Unity 環境編譯過。** 本輪在遠端容器完成，容器內沒有 Unity 與 C# 編譯器，
> 全部為靜態撰寫；改動已推上分支 `claude/skill-system-showcase-6vqh6l`，待使用者本機拉取後驗證。
> ⚠️ 2026-09-02 更正：本節曾記為「編譯通過、EditMode 全綠」，但該次測試跑在**尚未拉取本分支**的
> 本機專案上，驗的是舊程式。已撤回——**這是本輪第二次「憑回報打勾」的失誤**（第一次是 A22）。

### 實際改動的 ownership ／ data flow

| 項目 | 改動 |
|---|---|
| 新增身分 | `Core/Actions/ActionSlot.cs`：`None`／`Slot1`／`Slot2`／`Slot3`（玩家觸發段，依序編號）／`Reaction = 100`（保留段，非輸入驅動）。<br>🔄 **2026-09-02 改名**：原 `Primary`／`Secondary`／`Tertiary` 自稱有語意、實為拉丁文序號，與語意命名的 `Reaction` 混在同一個 enum ⇒ 講不出成長規則、且把身分綁在「按哪顆鍵」。詳見該檔註解 |
| 身分的數值語意 | **enum 的 int 值＝穩定身分，且刻意不要求連續**。改名安全、**改值不安全**（Unity 以值序列化）；淘汰某一格要留著它的數值、不遞補。玩家段（1–3）與保留段（100+）之間的空隙是設計的一部分，讓兩段各自獨立成長。<br>⇒ 以 slot 當索引的表（`ActionState` 的 per-slot 冷卻陣列）**必然稀疏**，容量取 enum 最大值 +1（現為 101 格用 5 格 ＝ 404 B，每角色一次、非熱路徑）。**已裁決不加 slot→密集索引的對照**——那等於多一把內部的鍵 |
| 黑板 schema | `IntentData.FireRequested`（`bool`）→ **`RequestedActionSlot`（`ActionSlot`）**。**writer 仍是 Runner，`WriterRules` 不變** |
| 輸入 | `InputData` ＋2 顆 `*ButtonDown`；`PlayerInputSource` ＋2 個 `InputAction`（未綁定＝false） |
| 按鍵→身分映射 | **Runner 順序 2 `ProcessIntents`**。raw input 不知道技能、`ActionState` 不知道按鍵 |
| Config | 平行的 `actionDefinitions` ＋ `ActionSlot` 索引；`GetActionDefinition(slot)`／`ActionDefinitionCount` |
| 冷卻 | `float` → `float[SlotCount]`，**仍住在 `ActionState` 內** |
| Definition 綁定 | `Initialize` 綁死 → **每次進入依 request 現查**（`TryResolveRequest`） |
| mailbox | `RequestAction(ActionSlot)`；projectile 命中送 `Reaction`（FU-3 解） |
| 重入 | `BaseState.CanReenter(data)` 預設 `false`；`ActionState` override（FU-1 解） |
| Sink 路由 | Runner 的 `List<ActionSinkBinding>` 於組裝期轉成 `ActionState.SlotCount` 大小的稀疏陣列；`ActionState` 依 `_activeSlot` 查找。清單非空即完全取代 legacy 單顆欄位 |

### 沒有新增任何 abstraction

新增的只有**一個 enum ＋ 一個陣列 ＋ Config 上一條平行索引**。
零新介面、零 manager、零 framework。`IActionLifecycleSink` **一行未改**。

### 向後相容

`BuildActionSlotMap` 在 `actionDefinitions` 為空時，退回讀 `paramsMappings` 綁在 `StateType.Action` 的那份。
相容退路保住的是「**不必改資產結構、不必重建 Definition**」，**不是**「一個欄位都不用改」。
一份 ADR-004 時代的 Definition 若其身分**不是** `Slot1`（例如敵人的 Damage 實為 `Reaction`），
**必須在 Inspector 把 `Slot` 設對**——那是一個欄位的遷移，不是重做。

📌 **2026-09-02 實證**：`DamageDefinition.asset` 因缺 `Slot` 欄位而吃初始值 `Slot1`，
導致敵人**安靜地**不播 Damage。已改為 `Reaction` 並經 Play 確認。程式維持嚴格身分解析；
2026-09-03 起，缺少 request slot 對應 Definition 時會發出 Editor-only、同 slot 僅一次的警告，不再靜默。

> 🔴 **接線陷阱（`BuildActionSlotMap` 的 `if (_actionSlotMap.Count > 0) return;`）**
> 退路是 **all-or-nothing**：只要 `actionDefinitions` **有任何一筆**，相容路徑就整條不走。
> ⇒ 想加 Slot2／Slot3 時，**必須把現有的 Throw Definition 一併放進 `actionDefinitions`（Slot ＝ `Slot1`）**，
> 否則 Throw 會安靜地失效——`paramsMappings` 裡那份從此不再被查。
> 這不是 bug（半套遷移比隱性雙來源更該被看見），但它**不會報錯**，所以寫在這裡。

### 測試（✅ 2026-09-02 實跑全綠）

新增 **T18**（兩份 Definition 獨立觸發且共用同一 `ActionState` 實例）、**T19**（per-slot 冷卻不連坐）、
**T20**（同 slot 不重入／不同 slot 可互相打斷）、**T21**（舊 Slot1 資產相容）。2026-09-03 再補
**T22／T23** 成對鎖住 legacy 嚴格語意（Slot1 不得解析 Reaction；明設 Reaction 才能解析）、
**T24** 鎖住近戰命中窗 lifecycle 與單次去重；**T25** 鎖住 Slot2 出手只通知 Slot2 sink，
Slot1／Slot3 的 Begin／Release／Cleanup 全為零；架構測試新增 **A26** 守 VFX 紅線。
⚠️ T22–T25／A26 僅完成程式與編譯，尚未在 Unity Test Runner 實跑。

⚠️ **T19 當初是紅的，且抓到真 bug**：冷卻只寫在 `OnExit`，而 `Complete()` 會先清空 `_activeSlot`／`_definition`
⇒ **自然播完的 Action 永遠不進冷卻**。已抽出 `CommitCooldown()` 在兩個結束路徑各呼叫（冪等）。

### 🐞 順帶修掉的既存缺陷

**A22 自 ADR-004 Trial 期就一直是紅的。** 它斷言 `ActionState.cs` 含 `IActionReleaseSink`，
但該介面早已改名為 `IActionLifecycleSink` 並從 1 個方法擴為 3 個；**斷言與 `docs/08` §2.7 都沒同步**。
⇒ 已修斷言，並把 `docs/08` §2.7 fold back 到程式現況。

⚠️ **這件事同時說明 ADR-004 §10 的 D（EditMode 全綠）當時是在不成立的基礎上打勾的。**
教訓：**改名要一併 grep 測試與文件**——`docs` 的舊名不會自己壞給你看，而測試會，前提是有人真的在看它。

---

## 12. FU 登記（本輪發現、超出範圍者）

| # | 發現 | 何時處理 |
|---|---|---|
| **FU-11-1** | `PlayerRuntimeData.AimTarget` 為無 writer 的死欄位（WP1 落地後仍成立） | ✅ 2026-09-03 直接移除；未新增 writer／schema |
| **FU-09-2** | `ThrownProjectile` 與法術投射物是否重複 | ✅ 2026-09-03 判定無第二種行為：法術直接重用 `ThrowProjectileEmitter`／`ThrownProjectile`，只換 prefab 與速度；未新增 abstraction 或複製類別 |
