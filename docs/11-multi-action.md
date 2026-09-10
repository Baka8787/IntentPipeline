# 09 — Multi-Action ／ Action Identity（實作規格）

> **本檔是 `docs/ADR/005-multi-action-identity.md` 的 Living Spec。**
> ADR 只凍結 D1–D5 五條決策；**其餘所有細節在本檔，且允許在 Trial 期間依實作發現修改**（ADR-005 §8 清單）。
>
> ✅ **狀態：ADR-005 已於 2026-09-06 翻牌為 `Accepted`**（A–G 七條全數由使用者實跑通過）。
> 2026-09-02 `Proposed → Trial`，2026-09-06 `Trial → Accepted`。本檔是**已驗證的實作基線**。
>
> 🔓 **§10.2 的 ⛔ 不得改動名單自此解除**——那是 Acceptance **G**（五個下游檔案零修改）的觀察期前提，
> 觀察期已結束並通過。`MotionDriver` 位移路徑／`AnimationFacadeBase` 契約／`LocomotionModel`
> 不再被本 ADR 擋住。**後續實作順序見 `docs/13` §7。**
>
> ⚠️ **ADR 的決策內容（D1／D2）自此凍結**——要改決策請開新 ADR 並 Supersede。
> 但**本檔（Living Spec）仍然是活的**，繼續隨實作更新。

---

## 1. 本輪範圍

### 1.1 目標

讓一個角色同時持有**多個 Action**，並以此支撐作品集的三個展示技能：

| 展示名 | 形狀 | 側效果 | 動畫來源 |
|---|---|---|---|
| **Fireball** | 短前搖 → 投射物 | 生成 projectile | Human Spellcasting Animations FREE（快速法術） |
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

> 📌 **盤點結論：Fireball 幾乎是免費的。** 它 ＝ Throw 砍掉 `Loop`／`WaitForTrigger` ＋ 換一份 Definition ＋ 換一個 prefab ＋ 加一列動畫映射。

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
| **Fireball** | `Slot2`（Q） | `Start` ＋ **2 段 Chain**（§4.3） | 留空 | 每段 ＠0.35 | 每段 = **true** | 重用 `ThrowProjectileEmitter`（法術 prefab／速度） |
| **Ice Spell** | `Slot3`（E） | `Start` | 留空 | `Start` ＠ ~0.35 | `Start` = false | **`GroundEffectSink`（地面 AoE，§4.4）** |

**為什麼三格都只有 `Start`（法術不用素材自帶的 Load／Cast 分離）**：
`Kevin Iglesias` 的 `MagicAttacks/` 每組都附 `Load`／`Cast`／合一三個檔，天生對應 `Start`／`End`。
**刻意不用**——ADR-004 的 Throw 已經展示過多 phase ＋ release timing，再做一次是重複展示同一個機制；
且需求是「**快速**施展」，快速本來就不該有蓄力段。改用合一檔、單一 `Start`。
⇒ 三格的展示價值不重疊：**Slot1 ＝ motion mapping**、**Slot2／3 ＝ 多身分共用一顆 `ActionState` ＋ per-slot 冷卻**、
**Slot3 額外 ＝ Slow 的跨系統自動傳播**。

#### 🔩 已落地的 Definition 資產（2026-09-04）

`Assets/ScriptableObjects/StateMachine/Actions/` 下已建立兩份 `ActionDefinitionSO`：

| 資產 | `Slot` | `AnimationKey` | `FallbackDuration` | `EmitsRelease` | `Cooldown` | 對應 clip |
|---|---|---|---|---|---|---|
| `FireballDefinition.asset` | `Slot2`（Q） | **`Spell_Fireball_1`** | 1.2 | ✅ ＠0.35 | **0.4** | `HumanF@MagicAttackDirect1H01_L`（36 frames） |
| `IceSpellDefinition.asset` | `Slot3`（E） | **`Spell_Ice`** | 1.366667 | ✅ ＠0.35 | **1.5** | `HumanF@MagicAttackCall1H01_L`（41 frames） |

> 🔄 **2026-09-04 使用者裁決（取代上表初版）**：Ice 改用 **Call**（單手上舉召喚），Fireball 改用 **Directional 的
> `HumanF` 版本並做成三連段**。全部改用 `HumanF` 素材——X Bot 是 Humanoid，`HumanF`／`HumanM` 只是動作風格差異，
> 不是骨架差異（兩者皆 `animationType: 3`，重定向路徑與現用的 `slash1.fbx` 相同）。
>
> 🏷️ **同時改名（2026-09-04）**：`Quick Spell`／`QuickSpellDefinition`／`Spell_Quick_N` 是**還沒決定法術種類前的暫名**
> （「快速施展」是形容詞，不是身分）。種類定案後一律改用種類命名：
>
> | 舊 | 新 |
> |---|---|
> | `QuickSpellDefinition.asset` | **`FireballDefinition.asset`** |
> | `Spell_Quick_1/2/3.asset`（＋同名 `AnimationKey`） | **`Spell_Fireball_1/2/3`** |
> | 投射物 prefab（尚未建立） | **`Projectile_Fireball`**／**`Projectile_Icebolt`** |
>
> `IceSpellDefinition`／`Spell_Ice` **不動**——它們本來就以種類命名。
> `ActionSlot.Slot2/Slot3` 也不動：slot 是位置身分，**刻意不叫技能名**（見 `ActionSlot.cs` 的說明）。
>
> **Fireball 三連段的 clip 分配**（三份 `TransitionAsset` 已建）：
>
> | 段 | `AnimationKey` | clip | frames → 秒 |
> |---|---|---|---|
> | 1 | `Spell_Fireball_1` | `HumanF@MagicAttackDirect1H01_L` | 36 → 1.2 |
> | 2 | `Spell_Fireball_2` | `HumanF@MagicAttackDirect1H01_R` | 36 → 1.2 |
> | 3 | `Spell_Fireball_3` | `HumanF@MagicAttackDirect2H01` | 48 → 1.6 |
>
> 🟡 **第 3 段用 `Direct2H01` 是我的補完**：使用者指定的 `Direct1H01` 只有 `_L`／`_R` 兩顆，湊不出三段。
> 選雙手版當收尾可讓三段有「左 → 右 → 雙手」的漸強，換掉只是換一個 clip guid。
>
> ⚠️ **連段需要 runtime 機制，目前尚未實作**——`FireballDefinition` 現在仍只有第 1 段，
> 可以先跑 ADR-005 的資產驗收。連段設計見 §4.3。

- **兩個 `AnimationKey` 是契約**：`AnimancerFacade.transitionMappings` 必須以**一字不差**的這兩個鍵註冊
  對應的 `TransitionAsset`，否則 `IsPlaying` 為 false（docs/11 §4.1 的安靜失敗①）。
  ⇒ 對應的 `TransitionAsset` 已建：`Assets/ScriptableObjects/Animation/` 下的 `Spell_Fireball_1/2/3.asset` ＋ `Spell_Ice.asset`
  （`_FadeDuration` 0.15、`_Speed` 1，比照 `Melee_Slash1.asset`）。**剩下只差 prefab 上補四列映射。**

> 🔧 **取得 FBX 子 clip `fileID` 的方法（往後可重用）**
>
> 🔄 **2026-09-05 更正——有更直接的一條**：`.fbx.meta` 裡的 **`fileIDToRecycleName`** 直接列出
> `<fileID>: <clip 名稱>` 的完整對照表（例：`7400016: Fists_Punch_R`）。
> 先前寫「該 ID 不在 `.meta` 裡」是**錯的**——我只查了 `internalIDToNameTable`（那個確實恆為 `[]`），
> 沒查同一份檔案裡的 `fileIDToRecycleName`。
> ⇒ **優先查 `fileIDToRecycleName`**；查不到時才退回下面的 AnimatorController 法。
> 📌 教訓：同一份 `.meta` 有兩張 ID 表，只看其中一張為空就下結論，等於用局部證據否定整體。
> Animancer 的 `ClipTransition._Clip` 需要 FBX 內 AnimationClip 的 `fileID`，而**該 ID 不在 `.fbx.meta` 裡**
> （`internalIDToNameTable` 即使匯入後仍是 `[]`），一般只能靠 Editor 拖曳。
> **但廠商 demo 的 `.controller` 會把它寫出來**——`Kevin Iglesias/.../AnimatorControllers/HumanM@MagicAttack*.controller`
> 的 `m_Motion:` 欄位同時給出 clip `fileID` 與 FBX `guid`。本包 5 個合一 clip 共用 `fileID: 3094330708855449807`
> （每個 FBX 只有單一 take）。
> ⇒ **凡是廠商附了 AnimatorController 的動畫包，clip 接線都不必開 Editor。**
- **冷卻刻意不同（1.5 vs 4）**：ADR-005 Acceptance **A** 驗的是 per-slot 冷卻互相獨立；
  兩格同值會看不出差異。
- 🟡 **`FallbackDuration` 由 frame 數 ÷ 30 推得**（`Bake` 留空 ⇒ 它就是 Action 的實際長度，
  也是 `ReleaseNormalizedTime` 的分母）。素材 fps 未經 Editor 確認，**Play 時若前搖過長／動畫被切掉，
  優先調這個值**——它是 Data 層 tunable，不需要動程式。

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

⇒ 原本的處置是「挑轉向量小的那一支」。

> 🔴 **2026-09-04 更正：上面那段歸因是錯的，滑步的成因與烘焙模型無關。**
>
> 實查 `MeleeSlash1Definition.asset`：`Start` phase 的 **`Bake` 欄位是空的**（`fileID: 0`），
> 用的是 `FallbackDuration: 0.6`。⇒ `ActionState.OnUpdateMotion` 的 `hasBake` 為 **false**
> ⇒ 走 `ExecuteBaseMovement` ⇒ 揮劍當下沒有移動輸入 ⇒ **身體完全原地不動**。
> 而 clip 的 root motion 因 `XZ Bake Into Pose = ❌` 被引擎抽出後丟棄，**腿部動作仍在播** ——
> **腳往前踏、人沒走，這就是滑步。**
>
> ⇒ **一維模型的「有損重建」從未被執行過**，上方以它解釋滑步的推論**不成立、應視為未驗證**。
> ⇒ **修法是一個欄位**：把 `Bake_slash1_Baked` 指派給該 phase 的 `Bake`。
> 指派後角色才會真的位移約 0.80m，屆時**才有可能**觀察到一維模型的真實殘差（也可能根本沒有）。
>
> 📌 **教訓**：症狀（滑步）與「一維模型有損」在描述上相容，但**相容不等於成因**。
> 下結論前應先確認**該路徑是否真的被執行**——這裡只要看一眼 Definition 的 `Bake` 欄位就能避免。
> ⚖️ 無論成因為何，皆不阻擋 ADR-005——滑步是表現品質問題，不影響「多 Action 共用一顆 `ActionState`」的驗證。

⚠️ 另一個獨立問題：`slash1` 只有 18 幀（0.6s）且**本身沒有收勢段**，而本卷 §4 只給了 `Start` 一個 phase
⇒ 揮完直接回 locomotion，看起來像「沒有收回動作」。需要時補一個 `End` phase 指向收勢動畫
（`EmitsRelease` 仍留在 `Start`——命中窗在揮擊，不在收勢）。

**實作結論（2026-09-03）**：只新增一支 runtime sink。

1. **法術投射物發射器不新增類別** — 直接沿用 `ThrowProjectileEmitter`。它已把 prefab、速度、壽命、spawn point、held visual 與 `AimResolver` 全部資料化；Fireball／Ice 的差異只在 prefab 與數值，另建同形類別只會複製 lifecycle 與瞄準邏輯。Ice 的 Slow 仍由 `ThrownProjectile.appliesSlow` 資產開關決定。
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
太近輸出背離目標的 `MovementIntent`、太遠沿 NavMesh steering direction 前進、距離帶內則沿水平 `toTarget`
的切線持續側移。`distanceHysteresis` 讓正在前進／後退的角色必須跨過帶內的第二道門檻才切入側移，
避免距離誤差在邊界逐幀抖動。

距離帶三個值與 `holdStrafeSpeedNormalized`／`strafeDirectionFlipInterval` 都是 producer 的
`[SerializeField]` 調整項。Hold 的順／逆時針符號是 producer 私有跨幀狀態，每次間隔加入 ±25% 隨機後翻轉，
避免多隻敵人長時間同向或同步繞圈；側移與既有速度輸出共用 Slow 倍率解析。
**不回讀 FSM、不新增黑板欄位、不讓 NavMeshAgent 取得 Transform authority**。後退也只輸出方向，
實際位移仍走 `LocomotionModel → MotionDriver`。

> **現階段的視覺邊界**：這裡只有模型無關的移動方向，沒有新增面向權威；`MotionDriver` 目前仍朝移動方向旋轉，
> 所以結果是「面朝切線繞行」，不是「身體持續面向玩家的專用 strafe 動畫」。後者需要獨立 combat-facing seam，
> 不得由 movement producer 回讀 FSM 或越層呼叫 Presentation 來偷渡。

### 4.3 連段（Chain，2026-09-04 落地）

**Fireball 是三連段**：按 Q 出第 1 段，在段內再按一次 Q 就接第 2 段，再一次接第 3 段；沒接到就自然收尾。

#### 為什麼不重用 `ActionPhase`

`ActionPhase` 只有 `Start`／`Loop`／`End`／`Cancel` 四個**語意固定**的階段，沒有「第 N 段」的位置。
`Loop` 的 `WaitForTrigger` 是 Throw 的**蓄力等待**（按住 → 放開），與連段的**重新按壓推進**不是同一件事——
硬套會把 Throw 的語意弄髒，且三段以上就再也塞不下。

替代方案「每段一份 `ActionDefinitionSO` ＋ 段間轉移表」更通用，但會打掉 slot ↔ definition 的 1:1 關係，
而那正是 ADR-005 D1 的核心（**不得有第二把鍵**）。代價遠大於收益，不採用。

#### 資料形狀（`ActionDefinitionSO`）

| 欄位 | 意義 |
|---|---|
| `ChainSegments` | 第 2 段起的 `ActionPhaseEntry[]`。**留空 ⇒ 這不是連段技**。依索引順序推進，元素的 `Phase` 欄位不被讀取 |
| `ChainInputOpenNormalized` | 接受「再按一次」的窗口**起點**（當前段的 normalized time）。窗口終點固定是該段結束。預設 0.25 |

第 1 段永遠是 `Phases` 的 `Start`，所以三連段 ＝ 1 個 `Start` ＋ 2 筆 `ChainSegments`。

#### 執行期規則（`ActionState`）

- **連段期間 `_phase` 一直是 `Start`**——連段是「同一個 `Start` 換素材重播」，不是新 phase。
  因此 `CanTransitionAway`／`CancelMoveIntentThreshold`／`Loop` 的既有語意**一字不必改**。
- **只排隊、不立刻切段**：窗口內偵測到再按 ⇒ 標記；實際切段固定發生在**當前段播完**。
  否則第 2 段會從第 1 段中途插進來，動作看起來像被吃掉。
- **冷卻整條共用一次**，在最後一段結束時提交（`Cooldown` 屬於「這次出手」，不屬於「每一段」）。
- **`Interruptible` 逐段 authored**：三段打滿約 4 秒不能移動，所以 `FireballDefinition` 的三段
  全部 `Interruptible = true`，讓翻滾／跳躍切得出去。

> 🔴 **唯一被改動的既有語意**：`_releaseEmittedThisExecution` 原本是「整次執行只發一次 Release」
> （Throw 只丟一顆）；連段改為**切段時重置**，因而**每段各發一次**（每段各出一顆投射物）。
> 單段 Action 走不到切段路徑，行為不變。

> 🐞 **2026-09-05，Play 才抓到的後續**：上面那條改動有一個**沒被 EditMode 測到的下游**——
> `ThrowProjectileEmitter` 自己也有一份 `_releasedThisExecution`，只在 `Begin()` 重置，
> 而 `Begin()` 一次執行只呼叫一次 ⇒ **第 2、3 段的 `Release()` 被 sink 靜默吞掉**，
> 連段動畫照播、但只會飛出第一顆投射物。
>
> **修法是移除 sink 那層去重，不是加一個 per-segment 重置回呼**：release 時點的唯一權威是
> `ActionState`（ADR-004 D2），sink 自帶第二套判斷正是該條決策要防的「第二個權威」。
> 📌 **教訓**：T26–T29 用 `CountingLifecycleSink` 驗到「`ActionState` 發了三次 Release」就收工，
> 但沒有任何測試驗「真實 sink 收到三次後真的做了三件事」。**測試驗到介面就停，缺口就落在介面之外。**

#### 為什麼不需要防「進場那一幀被誤判成連按」

`IntentData` 的 trigger 旗標是「當幀生、當幀死」（`PlayerRuntimeData.ResetTransientState`，管線順序 7），
而 `FullBodyStateMachine.Tick` 是**先 `OnTick` 才 `OnEnter`** ⇒ 新進入的 state 第一次 `OnTick` 已經是下一幀，
進場的那次按壓早被清掉。**不需要額外的 first-tick 守衛**——這是既有管線契約的自然結果，不是巧合。

#### 測試

`ActionStateTests` 的 **T26–T29**：依序推進且每段各一次 Release ／ 不連按就停在第 1 段 ／
窗口未開的按壓不推進 ／ 最後一段再按不會長出第 4 段。

#### 路由

**不開 ADR。** 對照 CLAUDE.md 的四條判準：沒動黑板 schema（連段狀態是 `ActionState` 私有欄位）、
沒動 FSM 拓撲、沒動管線順序或核心驅動介面、沒推翻既有不變量 ⇒ 走 Living Docs。

### 4.4 Ice ＝ 地面 AoE（2026-09-05 使用者裁決，取代「Ice 也是投射物」）

#### 為什麼改

原規劃 Ice 與 Fireball 都走投射物、只差 `appliesSlow` 開關。**使用者裁決推翻**：
「投射物 ＋ Slow 已經驗證過，再做一個飛行冰法只是重複同一條路徑，資訊增量太低。」

改為地面爆發之後，兩格的展示價值不再重疊：

| | Fireball（Slot2） | Ice（Slot3） |
|---|---|---|
| execution shape | 飛行投射物 | **地面 AoE 爆發** |
| 命中判定 | `OnTriggerEnter` | 一次 `OverlapSphere` |
| 目標數 | 單一 | 範圍內全部 |
| 證明什麼 | 連段 ＋ per-slot 冷卻 | **同一套 Action 架構可以接不同的 execution shape** |

📌 **這才是這一格真正的架構論證**：`ActionState`／`ActionDefinitionSO`／輸入層／冷卻
**一行都沒有為了 Ice 改動**，變的只有 `IActionLifecycleSink` 的實作。
換句話說——`docs/11` §2.1 說「側效果接縫是最大一筆重用」，這一格把它兌現了第三次。
選用的 `MagicAttackCall1H01_L`（單手上舉召喚）與地面爆發本來就是配套的動畫語意。

#### 形狀（`GroundEffectSink`）

`Presentation/Actions/GroundEffectSink.cs`，第三個 `IActionLifecycleSink` 實作。
`Release()` 做三件事：**解算落點 → 生成視覺 → 一次 `OverlapSphereNonAlloc` 投遞**。

| 決策 | 內容 |
|---|---|
| 落點 | `AimResolver` 的瞄準點 → **只夾水平距離**到 `maxCastRange` → 從上方往下探地 |
| 沒瞄準／瞄到腳下 | 退回「角色正前方 `defaultCastDistance`」。距離 0 會讓方向正規化除以零，這條退路同時是防呆 |
| 探不到地 | 用水平落點，**不放棄施放**——寧可高度不完美，也不要技能靜默消失 |
| 投遞 | `ActionRequestTarget.RequestAction(Reaction)` ＋ `TemporaryGameplayEffectState.ApplySlow(...)`，兩者都是**既有**機制 |
| 去重 | 以 root `Transform` 為鍵、固定容量 16 的陣列（比照 `MeleeHitboxSink`）。多 collider 的敵人只結算一次 |
| 零 GC | `Physics.OverlapSphereNonAlloc` ＋ 預配置緩衝，施放期間不配置 |
| 視覺 | `Instantiate` prefab 後定時 `Destroy`。**粒子必須自帶 `Play On Awake`**——本元件刻意不呼叫任何播放 API |

#### ⛔ 明確沒有做的事

**沒有 AoE framework**（只有球，沒有形狀抽象）、**沒有 Targeting framework**
（只有「瞄準點夾距離後探地」）、**沒有 StatusEffect framework**（只有既有的 Slow）。
第二個地面技能出現前不擴充——這與 ADR-005 D5、`docs/08` §11 的既有紅線一致。

#### 一個常數被提升了

`SlowMovementSpeedMultiplier = 0.3f` 原本是 `ThrownProjectile` 的 private const。
Ice 改走地面 AoE 後出現**第二個投遞者**，同一個「§7.4 明令不得更動」的數字散在兩個檔案
就是它開始漂移的方式 ⇒ 提升為 `TemporaryGameplayEffectState.SlowMovementSpeedMultiplier`。
⚖️ 這是**共用常數，不是 framework**：沒有新型別、沒有介面、沒有擴充點。

#### 測試

`SlowEffectTests` 三條：投遞走的是與投射物**相同**的兩條 seam ／ 同一目標只結算一次 ／
無瞄準時退回正前方且不產生 NaN。
`ArchitectureRegressionTests.A21` 的外部 seam 清單**已把 `GroundEffectSink.cs` 掛進去**——
新增 sink 就補進不變量，否則「外部 seam 不得持有動畫／轉移權威」會隨實作變多而失去覆蓋。

#### 路由

**不開 ADR。** `docs/11` §10.1 本來就明列「新增 `IActionLifecycleSink` 實作」屬於允許改動範圍；
沒動黑板 schema、FSM 拓撲、管線順序或核心驅動介面 ⇒ Living Docs。

---

## 5. 輸入配置

### 5.1 鍵位（2026-09-02 使用者裁決：Q／E 為技能鍵）

| 鍵 | Action | 備註 |
|---|---|---|
| **Q** | Fireball | ✅ 已綁（`Slot2Action`） |
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

### 8.3 Action Targeting Policy（🔄 **2026-09-08 使用者重新裁決，取代原「一律朝向目標」**）

> ## 核心規則（一句話）
> **普通招式永遠可預測地朝鏡頭方向；只有明確標成 soft-target 的技能，才會自動修正到敵人。**

#### 三種 policy（第一版就這三種，**不得擴充**）

| Policy | 語意 | 用在哪 |
|---|---|---|
| **`CameraForward`**（**預設**） | 方向 ＝ **camera forward**。**永不**自動修正到敵人 | 一般攻擊、指向技 |
| **`CameraConeSoftTarget`** | **僅當**鏡頭前方角錐內有合法敵人時修正到該敵人；**否則退回 `CameraForward`** | 明確標記的吸敵技能 |
| **`SelfCentered`** | **不需要** target／facing，不產生方向承諾 | 自身中心技。⚠️ **目前沒有合適素材 ⇒ 只保留概念，不做內容** |

#### 🔄 為什麼推翻原本的「全域規則、不得下放 Definition」

原規則的理由是：**「轉不轉向若做成 authored 欄位，等於把一致性交給資產填寫者」**——
**這個顧慮本身仍然成立**，但它針對的是一個**布林開關**（勾＝轉、不勾＝不轉）：
忘了勾就得到「這招不轉向」這種**不可預測**的行為，而且沒有任何線索。

新形狀不是布林，是**帶預設值的 enum**，而**預設值恰好是最可預測的那一個**：

- 沒填 ⇒ `CameraForward` ⇒ **朝鏡頭**，與其他普通招式**完全一致**
- 要吸敵**必須明確標記** ⇒ 例外是**顯性**的，不是遺漏造成的

⇒ **原本要防的失敗模式（忘了填就不一致）在新形狀下不存在**：忘了填得到的是**正確的預設行為**。
可預測性由「**一句話講得完的規則**」保證，而不是由「禁止任何 per-Action 差異」保證。

📌 這也讓 §8.1「miss 可歸因於自己」**更成立**——玩家永遠知道普通攻擊朝鏡頭飛，
吸敵是少數幾個**他自己選的**技能才有的特權。

#### 邊界

| 項目 | 裁決 |
|---|---|
| 觸發點 | **段落邊界取得一次承諾**（ADR-007 D5；連段每段各一次） |
| 適用範圍 | 所有 Action slot；**`Reaction` 不適用**（受擊不是出手，維持原裁決） |
| ⛔ 不做 | 不新增第四種 policy；不做 per-Action 的角度上限／吸敵強度旋鈕；**不為 `SelfCentered` 硬做技能** |

#### ✅ 那個開放問題的實作期答案（2026-09-08 落地）

> 原問題：`CameraConeSoftTarget` 的目標，要用**當下的相機錐查詢**，還是 **Combat Context 的黏性目標**？

**採第三種形狀：以 Combat Context 為目標的唯一供應商，但每個段落邊界重跑一次角錐閘門。**

| 為什麼不是「另外查一次相機錐」 | `docs/10` §3-D3 明文禁止新建 targeting service／目標列表／註冊表。combat context producer **已經是**目標的唯一供應商；再開一條查詢就是第二份真相，而它要回答的問題（「誰是敵人」）跟既有那條**一模一樣** |
|---|---|
| **為什麼不是「直接用黏性目標」** | 那就是 2026-09-08 之前的行為——無條件讓 target 壓過 aim point ⇒ **所有 Action 都等同 soft-target**，正是這次裁決要推翻的東西 |
| **實際形狀** | 目標**來源**是黏性的（Combat Context），但**方向修正的判定不是**：每次取得承諾時重測一次「target 在不在瞄準方向的角錐內」。⇒ 連段中甩相機把敵人甩出角錐，下一段就不再修正 |
| **角錐** | 水平半角 **30°**，`ActionState.SoftTargetConeHalfAngleDegrees`，⛔ **全專案一顆常數**（見上方「不做」欄）。垂直方向刻意不算進去——它由相機俯仰主導，與「敵人在不在正前方」無關，算進去只會讓低頭時吸不到人 |

**⇒ Combat Context 的黏性仍然有存在必要**，但理由換了：它現在服務的是 **facing／HeadLook 的穩定性**
（不要因為目標瞬間出視野就左右搖擺），**不再**是 action targeting 的決定者。

#### 🔩 落地紀錄（2026-09-08）

| 改動 | 檔案 |
|---|---|
| 新增 `ActionTargetingPolicy` enum（三個成員，`CameraForward = 0`） | `Core/StateMachine/Actions/ActionDefinitionSO.cs`（與 `ActionPhaseEntry` 同檔，**未新增檔案** ⇒ 不需要新的 `.meta`） |
| 新增 authored 欄位 `Targeting`，預設 `CameraForward` | 同上 |
| `CaptureReleaseContext` 依 policy 分支 | `Core/StateMachine/States/ActionState.cs` |
| `TrySelectAimPoint` → **`TrySoftTarget`**（無條件壓過 → 角錐閘門） | 同上 |
| `ShouldFaceTargetOnEnter` 的職責收斂為 **slot 層級閘門**（「會不會取得承諾」），policy 回答「朝哪取得」 | 同上 |
| 測試：`TC7` 改寫為角錐語意；新增 `TC7B` 釘住預設值 | `CombatContextTests.cs` |

⚠️ **既有 Definition 資產不需要重新接線**：`Targeting` 是**新增欄位** ⇒ 既有 `.asset` 吃程式預設
`CameraForward`。這也正是「忘了填得到正確行為」的實際兌現——三份既有 Definition
（Melee／Fireball／Ice）**自動從「全部吸敵」變成「全部朝鏡頭」**。
📌 ⇒ **要哪一招吸敵，是 Play 之後的手感決定**，在 Inspector 把該份 Definition 改成
`CameraConeSoftTarget` 即可，不需要改程式。

#### 🔄 目標來源已改（2026-09-02 使用者裁決）

**不採 persistent lock-on。** 改為 **Action-time soft auto-target**：**Action 發動的那一刻**依 camera forward、
距離與角度自動選最佳目標，**僅供 facing／targeting 使用**，不改 locomotion、不進入持續鎖定狀態。
⇒ 上文的「鎖定中的目標」應讀作「**本次出手當下自動選中的目標**」。

這與既有機制對得上：`AimResolver` 已經在做「沿相機射線 cast → 過濾 `ActionRequestTarget` → 排除自己 →
取角度偏差最小者」。⇒ **auto-target ＝ 在出手瞬間取一次那個結果**，不是新的 targeting 系統。
⛔ 不得新建 `ITargetable`／目標列表／註冊表／targeting service（`docs/10` §3-D3 既有禁令，仍然適用）。

~~⚠️ **本輪不實作**（2026-09-02 批次範圍外）——先讓三招打得順。若 Play 顯示 facing 明顯難看再補。~~
`docs/10-lock-on.md` 整體延後，狀態已同步更新。

#### ✅ 2026-09-05 已實作（觸發條件滿足）

上面那條「若 Play 顯示 facing 明顯難看再補」的條件在 2026-09-05 的 Play 錄影中成立：
相機朝著敵人施法，角色卻側著身把法術打向自己的正面方向。**規格未改一字，只是開始執行。**

| 職責 | 落點 |
|---|---|
| **何時轉**（哪些 Action、從何時到何時） | `ActionState` |
| **轉向哪裡**（soft auto-target 解算） | `AimResolver` ——`RequestFacing` 的唯一送出者不變（`docs/10` §4.3） |
| **怎麼轉**（slerp、deadzone、與 WASD 的優先權） | `MotionDriver.ApplyFacingRequest`，一行未改 |

**🔴 實作推翻的一個字面理解**：§8.3 寫「起手瞬間**一次性**轉向」，但
`MotionDriver.ApplyFacingRequest` **每幀只消化一次請求並 slerp 一小步**——只送一次等於幾乎沒轉。
⇒ 「一次性」鎖的是**目標方向**（`TryLatchAutoTargetFacing` 在 `OnEnter` 取一次快照），
送出則是**逐帧重送**（`SubmitLatchedFacing`）直到 Action 結束。
這樣相機在揮擊途中亂晃也不會讓角色跟著轉——**這正是「一次性」原本想要的性質**。

**時序**：重送點在 `ActionState.OnUpdateMotion` 的**最前面**，早於兩條位移路徑。
兩者都會呼叫 `ApplyFacingRequest`，而它只認當幀請求 ⇒ 朝向與位移必然同幀結算，
**不依賴 `AimResolver` 與 Runner 的 Unity 執行順序**。

**敵人沒有 `AimResolver`** ⇒ 整條安靜退化為「不轉向」，不是錯誤（`T31` 守）。
敵人要面向玩家是另一件事，不走這條路。

**測試**：`ActionStateTests.T30`（全域規則 ＋ `Reaction` 例外）／`T31`（無 resolver 仍走完）、
`CameraAimTests.T5/T6`（方向水平化、退化回零）。
⚠️ 「真的轉到面向目標」需要相機 ＋ Physics ＋ 帧迴圈，**留 Play 驗收**。

#### 開放（實作期決定，不在本輪）

> **2026-09-06：前兩項已由 `docs/ADR/007-direction-authority.md`（🟡 Trial）回答**，
> 第三項仍開放（登記於 `docs/14` §7-1）。

- ~~**強制點放哪**~~ → **ADR-007 D3／D5**：承諾由 `ActionState` 持有（它本來就擁有 release 時點，ADR-004 D2），
  但**送出者維持一個**（`docs/14` §2.2 的薄轉送），因此不會變成第二個 facing 權威。
- ~~揮劍**過程中**要不要持續追向目標~~ → **ADR-007 D5**：段落內**不**追蹤（保留「甩相機不跟著轉」的原意），
  承諾在**帶 release 的段落邊界**重取——這同時解掉 A6 的「人與火球分家」。
- 無目標時的行為（維持當前朝向 vs 朝相機正前方）。**仍開放。**

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

### 10.2 ~~⛔ 不得改動~~ → 🔓 **2026-09-06 解除**

> **這份名單是 Acceptance G 的觀察期前提，不是永久禁令。**
> G 要證明的是「Slow 跨系統自動傳播時，五個下游檔案零修改」——
> 那是一個**觀察**，觀察期間不能動被觀察的對象。
> ADR-005 於 2026-09-06 `Accepted`（G 已通過）⇒ **觀察期結束，名單失效。**

~~`LocomotionModel`／`LocomotionSpeedSmoother`／`LocomotionStopSelector`／`FootIKController`／`AudioController`
（＝Acceptance **G** 的名單）、`MotionDriver` 的位移路徑、`AnimationFacadeBase` 契約、`IPresentationController` 契約~~

⚠️ **解除的是「本 ADR 的凍結」，不是「可以隨便改」**：這些檔案仍受各自的 ADR 與架構不變量約束
（`LocomotionModel` → ADR-003；`AnimationFacadeBase` → ADR-001／A4；`MotionDriver` 位移路徑 → A20）。
動它們之前照舊走 routing rule 判斷要不要開 ADR。**下一步的順序見 `docs/13` §7。**

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
