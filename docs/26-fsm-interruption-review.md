# 26 — FullBody FSM 的 Action／Hurt／Death 與 Interruption 設計審查（2026-09-14）

> **性質**：一次性架構審查（同 `docs/23` 的體例），**非規格正本**、非 ADR。
> **範圍**：只讀。本輪**未修改任何程式或資產**。
> **前提聲明**：磁碟現況**包含上一輪（ADR-009）尚未經 Unity 驗證的變更**——
> `StateType.Death`／`DeathState`／`DeathArbiterSource`／`CharacterHealth`／`ActionState` 的 lethal 授權那一句。
> 本審查把它們標為 **[NEW-unverified]**，並**不預設它們是對的**。

---

## A. Current Architecture（以源碼為準）

### A.1 唯一的狀態切換入口

`FullBodyStateMachine` 是全專案**唯一**改變 FSM 狀態的地方。`TransitionTo` 是 `private`，
只有兩個呼叫點，且**沒有任何 public Force／Set／Request state API**
（`grep "TransitionTo\|_currentState ="` 的結果只落在本檔內）。

```
FullBodyStateMachine.Tick(data, dt)
 ├ _currentState.OnTick(data, dt)
 ├ if (!EvaluateInterrupts(data))  EvaluateTransitions(data)
 └ finally: _actionRequestTarget?.ClearAfterEvaluation()      ← mailbox 只有一次機會，不排隊
```

**`EvaluateInterrupts`（搶佔）** — 遍歷**整個 registry**，對每個候選：

```csharp
bool isSelf = targetState.Type == _currentState.Type;
if (isSelf)
{
    if (!_currentState.CanReenter(data)) continue;          // ⚠️ 不呼叫 CanEnter
}
else if (!targetState.CanEnter(data) ||
         !_currentState.CanBeInterruptedBy(targetState)) continue;

int priority = _config.GetPriority(targetState.Type);
if (priority > highestPriority) { highestPriority = priority; bestCandidate = targetState; }
```

**`EvaluateTransitions`（自然過渡）** — 只在沒有任何搶佔成立時執行，且需
`_currentState.CanTransitionAway`；然後依 config 的 `ValidTransitions` **順序**取第一個
`CanEnter` 為真的。

### A.2 誰能要求進入、誰決定合法性

| 角色 | 誰 | 備註 |
|---|---|---|
| **意圖來源** | `IInputSource`（順序 1）→ `ProcessIntents`（順序 2）寫 `Intent.JumpRequested`／`RollRequested`／`RequestedActionSlot` | 玩家是 `WasPressedThisFrame` 邊沿；敵人 `AIInputSource` 是**自製的 pulse**（持續 desire → 單幀 request，`attackRequestRetryInterval` 重送） |
| **外部事件來源** | `ActionRequestTarget`（單格 mailbox，`RequestAction(slot)`） | **目前唯一的呼叫者是 `CharacterHealth`** [NEW-unverified]；之前是三個攻擊 sink |
| **准入判定** | 各 state 自己的 `CanEnter` | ⚠️ **不是純函式**：`JumpState.CanEnter` 會寫 `_ungroundedSince`／`_enterAsFall`，而它**每幀都被呼叫**（`EvaluateInterrupts` 遍歷全 registry） |
| **中斷授權** | `BaseState.CanBeInterruptedBy` → `Config.CheckCanInterrupt(current, next)` | **只有 `ActionState` 覆寫它**；其餘五顆狀態純吃 config |
| **同型重入** | `BaseState.CanReenter` → 預設 `false` | **只有 `ActionState` 覆寫它** |
| **優先級** | `StateRule.Priority`（config 資產） | ⚠️ 比較是 **strict `>`** ⇒ **同分由 Dictionary 迭代順序（＝註冊順序）決定**。註冊順序：Idle, Move, Jump, Roll, Action, Traversal, Death |

### A.3 Runtime 實際存在的狀態

七顆（`StateType` ＋ `FullBodyStateMachine.Initialize` 的 `RegisterState`）：

```
Idle  Move  Jump  Roll  Action  Traversal  Death[NEW-unverified]
```

⚠️ **`Falling` 與 `Landing` 不是 state**，是 `JumpState` 內部的 `AnimationPhase`／`LandingPhase`。
「walk-off 落下」也是進 `JumpState`（`_enterAsFall`）。這是刻意的（`A13'` 守住 enum 不擴張）。

⚠️ **`Hurt` 不是 state**。受擊走 `ActionSlot.Reaction (=100)` → `DamageDefinition.asset` → `ActionState`。

### A.4 serialized config 實況

`PlayerStateMachineConfig.asset`（int 值：1 Idle／2 Move／3 Jump／4 Roll／5 Action／6 Traversal／7 Death）：

| State | Priority | CanBeInterruptedBy | ValidTransitions |
|---|---:|---|---|
| Idle | 0 | Roll, Jump, Action, Traversal | Move |
| Move | 0 | Jump, Roll, Action, Traversal | Idle |
| Jump | 10 | **（空）** | Move, Idle |
| Roll | 20 | **（空）** | Move, Idle |
| Action | 10 | Roll, Jump | Move, Idle |
| Traversal | 30 | **（空）** | Jump, Move, Idle |
| **Death** | — | **資產裡完全不存在這條規則** | — |

`EnemyStateMachineConfig.asset`：同上但**沒有 Traversal 與 Death**，且 **Action 的 `CanBeInterruptedBy` 是空的**。

### A.5 🔴 由 A.4 直接推出的事實

> **`StateType.Death` 與 `DeathState` 存在於程式中，但在遊戲裡永遠進不去。**

`CheckCanInterrupt(X, Death)` 對所有 X 都回 `false`（沒有任何清單含 7），
且沒有任何 `ValidTransitions` 含 Death ⇒ 兩條路都不通。
因此上一輪加在 `ActionState.CanBeInterruptedBy` 的 lethal 授權那一句，**目前是不可達的程式碼**：

```csharp
if (!base.CanBeInterruptedBy(other)) return false;   // ← 這裡就先擋掉了
if (other != null && other.Type == StateType.Death) return true;   // ← 到不了
```

這不是 bug，是「程式落地但資產未接線」的正常中間狀態（`docs/25` §7.2 已記）。
但它意味著：**本輪的設計判斷不應該以「Death 已經可用」為前提。**

---

## B. Current Transition Matrix

「Hurt」在下表一律指**現況的 `ActionSlot.Reaction`**（它是 `StateType.Action`）。

### B.1 搶佔矩陣（`EvaluateInterrupts`）—— 玩家 config

| From ↓ \ To → | Idle/Move | Jump | Roll | **Action（出手）** | **Hurt（Reaction）** | Traversal | **Death** |
|---|---|---|---|---|---|---|---|
| **Idle / Move** | — | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ 無規則 |
| **Jump / Falling / Landing** | ❌ 空清單 | — | ❌ | ❌ | ❌ **空中被打完全沒有受擊反應** | ❌ | ❌ 無規則 |
| **Roll** | ❌ 空清單 | ❌ | — | ❌ | ❌（刻意：無敵幀） | ❌ | ❌ 無規則 |
| **Action（出手中）** | ❌ | ✅ | ✅ | ⚠️ 見 B.2 | ⚠️ 見 B.2 | ❌ | ❌ 無規則 |
| **Hurt（硬直中）** | ❌ | ✅ | ✅ | ⚠️ 見 B.2 | ✅ 可再次受擊 | ❌ | ❌ 無規則 |
| **Traversal** | ❌ 空清單 | ❌ | ❌ | ❌ | ❌ | — | ❌ 無規則 |
| **Death** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | — |

敵人 config 差異：**Action 的 `CanBeInterruptedBy` 是空的** ⇒ 敵人出手時連 Roll／Jump 都打不斷它
（但那兩者敵人本來也不會用）。Reaction 走的是 B.2 的另一條路，不受此影響。

### B.2 ⭐ Action ↔ Hurt 走的是**完全不同的一條路**

因為兩者同為 `StateType.Action`，`EvaluateInterrupts` 走 `isSelf` 分支
⇒ **完全不看 config 的 `CanBeInterruptedBy`**，只問 `ActionState.CanReenter`：

```
Action → Hurt 必須通過的 gate（依序）：
  1. _phase != ActionPhase.None
  2. _currentEntry.Interruptible                      ← 資產（ActionPhaseEntry）
  3. TryResolveRequest 成功：
       3a. slot = Intent.RequestedActionSlot，為 None 才讀 external mailbox   ← ⚠️ 見 C.3
       3b. Config.GetActionDefinition(slot) != null
       3c. definition 有 Start phase
       3d. !RequiresGrounded || IsGrounded
       3e. Time.time >= _cooldownEndTime[slot]
  4. slot != _activeSlot  ||  AllowsSameSlotReentry(slot)    ← 只有 Reaction 為真
  5. priority(Action)=10 在本幀所有候選中最高（strict >，同分輸給先註冊的 Jump）
```

```
Action → Death 必須通過的 gate：
  1. DeathState.CanEnter  ＝ data.Survivability.IsDead
  2. Config.CheckCanInterrupt(Action, Death)           ← ❌ 目前恆 false（資產沒有這條）
  3. （若 2 通過）ActionState 的 lethal 例外直接放行，不看 Interruptible
  4. priority(Death) 最高                                ← ❌ 目前 GetPriority(Death)=0（預設值）
```

### B.3 使用者點名的六條，現在各自會發生什麼

| 情境 | 現況 | 為什麼 |
|---|---|---|
| `Action → Hurt` | ⚠️ **看資產**。`Interruptible: 1` ⇒ 成立；`EnemyPunchDefinition` 是 `0` ⇒ **不成立** | B.2 gate 2 |
| `Action → Death` | ❌ **不可能** | config 缺 Death 規則 |
| `Traversal → Death` | ❌ **不可能** | 同上（且 Traversal 清單本來就空） |
| `Jump → Hurt` | ❌ **不可能** | Jump 的 `CanBeInterruptedBy` 是空清單 ⇒ **空中受擊沒有任何反應** |
| `Roll → Hurt` | ❌ 不可能 | Roll 清單空（刻意的無敵幀，這條是**對的**） |
| `Hurt → Hurt` | ✅ 成立 | `AllowsSameSlotReentry(Reaction)` 的唯一存在理由 |
| `Hurt → Death` | ❌ 不可能 | 同 Action → Death |

---

## C. Semantic Problems

### C.1 ⭐ `ActionState` 裡已經有**兩處**「如果是 Reaction 就反過來」

這是本審查最硬的證據——**抽象已經在漏，不是將來可能漏**：

```csharp
// ①「這一擊朝哪」——Reaction 不取得方向承諾
internal static bool ShouldFaceTargetOnEnter(ActionSlot slot)
    => slot != ActionSlot.None && slot != ActionSlot.Reaction;

// ②「同身分不得重入」——Reaction 是唯一例外
internal static bool AllowsSameSlotReentry(ActionSlot slot) => slot == ActionSlot.Reaction;
```

兩條規則的形狀完全一樣：**「Action 的規則是 X；如果是 Reaction 就是 ¬X。」**
而且兩條的註解都明說了原因——`ActionState.cs:106`「**受擊不是出手**」、
`ActionState.cs:238`「**受擊不是出手**」。

> 程式裡已經寫著「它不是 Action」兩次，然後仍然把它放在 `ActionState` 裡。
> 這是 **responsibility 錯置**，不是命名問題。

### C.2 Reaction 繼承了大量它用不到的 Action 能力

| `ActionDefinitionSO` 能力 | Reaction 真的需要？ | 現況 |
|---|---|---|
| `AnimationKey` | ✅ | `Damage`（`Damage.asset` 已存在） |
| `Bake`（root motion） | ✅ | `Bake_Fists_Hit_Right.asset`——受擊有位移，這是真需求 |
| `FallbackDuration` | ✅ | 1.1667 s |
| `Interruptible` | ✅ | 1 |
| `Phases` Start/Loop/End/Cancel | ⚠️ 只用 Start | Loop/End/Cancel 全空 |
| `Cooldown` / `CooldownVariance` | ❌ | 0；但 `CommitCooldown()` 仍每次執行、仍佔 `_cooldownEndTime[100]` |
| `ChainSegments` / `ChainInputOpenNormalized` | ❌ | 連段對受擊無意義 |
| `Targeting`（`ActionTargetingPolicy`） | ❌ | **被 C.1 ① 硬性排除**——欄位存在但永遠不生效 |
| `CancelMoveIntentThreshold` | ❌ | 「移動可取消」對硬直是反語意 |
| `EmitsRelease` / `ReleaseNormalizedTime` / `IActionLifecycleSink` | ❌ | 受擊沒有要開的 hitbox |
| `WaitForTrigger` | ❌ | 沒有「再按一次」 |
| `RequiresGrounded` | ⚠️ | 設 0 是對的，但這是 Action 的概念 |

**真正重用的是四項：animation key、bake motion、duration、interruptible。**
其餘九項是「現成可以用」而被順便耦合的。

### C.3 ⭐ Reaction 與玩家意圖**競爭同一個單格 mailbox**——這已經造成過實際 bug

`TryResolveRequest`：

```csharp
slot = data.Intent.RequestedActionSlot;
if (slot == ActionSlot.None && _externalRequestTarget != null)
    slot = _externalRequestTarget.PendingSlot;      // ⚠️ 只有「這幀沒有主動意圖」才讀得到受擊
```

`docs/20` §2.6 記錄的**實際 Play bug**：敵人 AI 的攻擊意圖曾是 level（每幀 true）
⇒ `RequestedActionSlot` 恆為 `Slot1` ⇒ **Reaction 每幀被寫入、每幀被 `ClearAfterEvaluation` 丟棄，從未被讀過一次**。
症狀是「緩速有用但受擊沒有」，查了整整一輪才定位。
§2.7 把它記為未解的 starvation 風險，並明說「任何未來的持續型 intent producer 都可能再次把 Reaction 餓死」。

> **這不是實作疏忽，是結構後果**：把一個**外部事件**塞進一個為**主動意圖**設計的單格信箱，
> 兩者就必然要爭同一格。受擊是被動的，它本來不該跟「我要出手」排隊。

### C.4 中斷授權目前有**三條**路徑，而 Hurt 是唯一走第三條的東西

| # | 路徑 | 授權住在哪 | 誰用 |
|---|---|---|---|
| 1 | `CanBeInterruptedBy` → config | `StateMachineConfig.asset` | 所有跨 StateType 的中斷 |
| 2 | `CanBeInterruptedBy` → `_currentEntry.Interruptible` | `ActionDefinitionSO` | 只有 `ActionState` 疊加 |
| 3 | **`CanReenter`** | `ActionDefinitionSO` ＋ `AllowsSameSlotReentry` 硬編碼 | **只有 Action→Action，而實務上只有 Hurt** |

路徑 3 的存在理由（ADR-005 FU-1）是「兩個不同技能要能互相打斷」，
但實際上**最常走它的是受擊**——一個根本不是技能的東西。

### C.5 命名 vs 錯置的分界

| 項目 | 判定 |
|---|---|
| `Falling`／`Landing` 沒有自己的 StateType | ✅ **只是命名問題**。它們是 Jump 的內部相位，承載範圍與名稱的落差已由 `A13'` 明文記載，結構上正確 |
| `ActionSlot` 的「保留段 100 起」 | ✅ **只是命名問題**。分段規則清楚、成長規則明確 |
| `Reaction` 住在 `ActionState` | ❌ **真的是 responsibility 錯置**。證據：C.1 的兩處語意反轉、C.2 的九項無用耦合、C.3 的實際 bug |
| `Roll` 是獨立 StateType 而不是 Action | ✅ **既有且正確的先例**——見 D 節 |

---

## D. Candidate Designs

### D.0 先講一個決定性的既有先例：**`RollState`**

使用者自己給的 Action 定義包含 Roll。但這個 repo 裡 **Roll 已經是獨立 `StateType`，不是 `ActionState`**，
而且它需要的東西與受擊幾乎一模一樣：

| RollState 需要 | 怎麼拿到 |
|---|---|
| animation key | `BaseState.AnimationKey`（預設 `Type.ToString()`） |
| baked root motion | `Config.GetBakeData(Type)` ＋ `MotionDriver.ExecuteBakedCurveMovement` |
| 固定時長 ＋ fallback | `_rollTimer`，115 行裡的 3 行 |
| 不可被打斷的窗 | config 的 `CanBeInterruptedBy` 留空 |

⇒ **「共用底層 animation/motion execution，而不共用 Action semantic」在本 repo 不是新設計，
是已經在跑的既有作法。** `MotionDriver` 與 `AnimationFacadeBase` 本來就是所有 state 共用的執行層。

### D.1 Model A — Hurt 繼續是 Action

**優點**：零遷移成本；`DamageDefinition.asset` 已接線；重用 phase 機制（雖然只用到 Start）。

**缺點 ＋ 未來案例推演**：

| 未來需求 | Model A 下會發生什麼 |
|---|---|
| **Super Armor** | ✅ 已可用（`Interruptible: false`）——**這一項 Model A 沒有問題** |
| **Knockback** | 新增 `ActionSlot.Knockback = 101`，然後 `AllowsSameSlotReentry` 要不要含它？`ShouldFaceTargetOnEnter` 要不要排除它？⇒ **C.1 的兩處各長一個 `\|\|`** |
| **Stun** | 同上再一次。而且「Stun 可打斷普通 Hurt，普通 Hurt 不可打斷 Stun」是**同 StateType 的優先級**——`CanReenter` 只有一個 `Interruptible` bool，表達不出來 ⇒ 需要在 `ActionState` 裡加 slot 比較表 |
| **Guard Break** | 同上 |
| **Launch**（浮空） | 與 `Jump` 的滯空語意衝突：Launch 應該進入某種空中狀態，但它是 Action ⇒ 兩套滯空真相 |

**結論**：Model A 的失敗模式是**可預測且已經開始的**——C.1 的兩處反轉會線性增長，
而每一個都是「在 ActionState 裡加 if」，正是使用者明確要避免的東西。

### D.2 Model B — Hurt 是獨立 FullBody State

```
Idle  Move  Jump  Roll  Action  Hurt  Traversal  Death
```

`HurtState` 負責：reaction animation／duration／movement restriction／recovery／可被誰打斷。
Damage 與 Health 留在 FSM 外（`CharacterHealth`，已落地）。

**需要新增多少結構**：

| 項目 | 量 |
|---|---|
| `StateType.Hurt` | 1 個 enum 成員 |
| `HurtState.cs` | **~80 行**，`RollState`（115 行，其中約 50 行是警告註解）是現成模板 |
| 受擊 request seam | 沿用既有 `ActionRequestTarget`，或改成 `Intent` 的一個 bool（見 F 節） |
| config 資產 | 每份 config 加一條 `State: Hurt` 規則，並把 Hurt 加進他人的 `CanBeInterruptedBy` |
| 參數資產 | `HurtStateParams : StateParamsSO`（duration／bake），或直接沿用 `bakeMappings` ＋ 一個 `[SerializeField]` |

**與現有 FSM 模型是否自然一致**：✅ **高度一致**。
它與 `RollState` 是同一個形狀，走的是所有其他狀態走的那一條授權路徑（config），
並且**刪掉**路徑 3（`CanReenter` 只剩真正的 Action→Action 用途）。

⇒ Model B 是**移除一條政策路徑**，不是新增。

**同時修掉的兩件事**：
- C.3 的 mailbox 競爭（Hurt 不再與 `RequestedActionSlot` 搶同一格）。
- `Jump → Hurt` 目前不可能（B.3）——一旦 Hurt 是獨立 state，它就只是「把 Hurt 加進 Jump 的清單」這個**資產決定**，而不是「Jump 要不要被 Action 打斷」這個綁在一起的問題。

### D.3 Model C — Reaction 是獨立 family / overlay / 第二套 FSM

**判定：❌ 不採用。**

理由不是理論，是這個 repo 的具體條件：

1. `FullBodyStateMachine` 是**唯一**的狀態權威（A.1）。第二套 FSM 就是第二個權威，
   「死了還在播受擊」這類互斥只能靠紀律維持。
2. ADR-006 的 upper-body layer **已經是**這個專案處理「疊加表現」的方式，
   而它明確只做 **presentation layering**（Animancer layer ＋ mask），不做狀態仲裁。
   受擊需要**限制移動與取消 Action**，那是 gameplay 仲裁，不是 presentation overlay。
3. CLAUDE.md 明令「不得為了理論完整新增第二套 FSM」。

**唯一值得保留的 C 元素**：未來若要做「上半身受擊但下半身繼續跑」（輕型 flinch），
那是 **ADR-006 的 layer 問題**，與本審查的 full-body reaction 是兩件事，不要混談。

---

## E. Recommendation

> ## 採用 **Model B**：把 Hurt 抽成獨立 `StateType.Hurt` ＋ `HurtState`。

### E.1 這個結論不是從教科書來的，是從四項本 repo 的證據來的

| # | 證據 | 出處 |
|---|---|---|
| 1 | **`ActionState` 裡已經有兩處「如果是 Reaction 就反過來」**，且註解自己寫著「受擊不是出手」 | C.1 |
| 2 | **實際發生過的 Play bug**：Reaction 與主動意圖爭同一格 mailbox 被餓死，查了一整輪 | C.3、`docs/20` §2.6／§2.7 |
| 3 | **`RollState` 已經是這個形狀**：離散、有 bake、有時長、有不可打斷窗，而且**不是** Action | D.0 |
| 4 | **本專案使用的 Animancer，其官方 FSM 範例就是這樣分的**：`FlinchState` 與 `ActionState` 是**兩個獨立類別**，都繼承 `CharacterState`，`FlinchState` **完全不重用** `ActionState`，由 `HealthPool.OnHitReceived` 觸發，優先級 High > Action 的 Medium | 外部（見 §H） |

### E.2 ⚠️ 也要誠實說反對意見

CLAUDE.md 明令「不得因為另一個設計看起來更乾淨就重寫系統」「第二個使用者出現前不建 abstraction」。
**Stun／Knockback／Guard Break 目前都不存在。**

本建議之所以仍然成立，是因為**觸發條件不是那些未來需求**，而是 E.1 的 ①②——
**兩處語意反轉與一個已經發生的 bug，現在就在磁碟上。**
這是「把兩個因為方便而合併的東西分開」，不是「為了未來先建抽象」。

⇒ 若使用者判定「兩處反轉與一個已修掉的 bug 還不足以付遷移成本」，
**Model A 是可以接受的決定**，代價是 D.1 表格裡那條線性增長曲線。這一點由使用者裁決，不由本文件。

### E.3 Interruption policy：**現有機制夠用，但語意要重新整理**（回答 §5）

**先講結論：不需要 tag framework、bitmask 或 interrupt graph。**

| 需求 | 現有機制能不能表達 |
|---|---|
| 普通 Hurt 可以打斷 | ✅ config：把 Hurt 放進該狀態的 `CanBeInterruptedBy` |
| 普通 Hurt 不可以打斷 | ✅ 同上，不放 |
| **某 Action 前搖可斷／出手不可斷／recover 又可斷** | ✅ **今天就做得到**：`Interruptible` 是 **per-`ActionPhaseEntry`**，Start=true／Loop=false／End=true |
| Death 一定可以打斷 | ✅ config：Death 進每一份清單；priority 最高 |
| Super Armor | ✅ `Interruptible: false` |
| **Stun 可以打斷但普通 Hurt 不行** | ❌ **表達不出來**。`Interruptible` 是單一 bool，一旦 false 就對所有來源 false |

最後一項是 `Interruptible: bool` 的唯一真實極限。**但不必現在就改**，而且改法不是新框架：

> `Interruptible: bool` 其實就是 `MinimumInterruptPriority ∈ {0, ∞}`。
> 需要第三級時，把它加寬成一個 **int 門檻**（「要打斷我，你的 state priority 至少要這麼高」），
> 就自動涵蓋 Stun > Hurt、Death > 全部——**而且用的是 `StateRule.Priority`，一個 repo 已經有的東西**。
> 這也正是 Animancer 官方範例的作法（`CanExitState` 比較 `nextState.Priority > Priority`）。

⇒ **本輪不做這個加寬**（Stun 不存在）。但它說明了現有形狀是**可以長大的**，
不需要為了未來先建框架——這正是使用者要的答案。

**一個順帶的好處**：Model B ＋ priority 門檻之後，
上一輪那句 `if (other.Type == StateType.Death) return true;` **可以刪掉**——
Death 只要 priority 最高就自然穿透，不需要硬編碼的型別例外。

---

## F. Minimal Migration（**本輪不執行**）

### F.1 保留（一個字都不動）

- `FullBodyStateMachine` 的兩段評估邏輯與 `TransitionTo`
- `StateRule` / `StateMachineConfigSO` / `Priority` / `CanBeInterruptedBy` / `ValidTransitions`
- `MotionDriver` / `AnimationFacadeBase`（Hurt 與 Roll 共用同一組執行層）
- `CharacterHealth`（ADR-009 D1／D2 的決策擁有權**不受影響**——它仍然是「被打之後會發生什麼」的唯一決定者，只是改成請求 Hurt 而不是請求 Reaction）
- `ActionState` 的 Action 語意（phase／chain／cooldown／release／targeting）**全部保留**
- `RollState` 作為模板

### F.2 新增

| 項目 | 內容 |
|---|---|
| `StateType.Hurt` | 加在**最後**（值 8，不遞補；config 以 int 序列化） |
| `Core/StateMachine/States/HurtState.cs` | ~80 行，`RollState` 形狀 |
| 受擊 request seam | **建議沿用 `ActionRequestTarget`**，但改成「`Intent` 的一個 bool」更乾淨——見 F.5 的開放問題 |
| `HurtStateParams : StateParamsSO`（可選） | duration／bake；或直接用既有 `bakeMappings` ＋ 常數 fallback（比照 Roll） |

### F.3 淘汰的舊 seam

| 淘汰 | 之後長怎樣 |
|---|---|
| `ActionSlot.Reaction = 100` | ⚠️ **數值不得回收**（ADR-005：「要淘汰某一格請留著它的數值」）。標為 `[Obsolete]` 或註解為已退役 |
| `ActionState.ShouldFaceTargetOnEnter` 的 Reaction 例外 | **刪掉 `&& slot != ActionSlot.Reaction`** ⇒ 規則回到單純的「Action 都取得承諾」 |
| `ActionState.AllowsSameSlotReentry` | **整個方法可刪**，`CanReenter` 的最後一行回到 `return slot != _activeSlot` ⇒ 路徑 3 只剩真正的 Action→Action |
| `DamageDefinition.asset` | 不刪除；**內容遷移**到 Hurt 的參數（animation key `Damage`、bake `Bake_Fists_Hit_Right`、duration 1.1667） |
| ADR-009 D4 的 `if (other.Type == Death) return true;` | 改用 priority 門檻後可刪（E.3） |

### F.4 資產遷移

| 資產 | 動作 |
|---|---|
| `PlayerStateMachineConfig` / `EnemyStateMachineConfig` | 加 `State: Hurt` 規則（priority 介於 Action 與 Death 之間）；把 Hurt 加進 Idle／Move／Action／**Jump** 的 `CanBeInterruptedBy`；Roll／Traversal 是否加由設計決定 |
| 同上 | 順便補上 ADR-009 還欠的 Death 規則（A.5） |
| `EnemyPunchDefinition` | `Interruptible: 0 → 1`（與 Model 無關，B.3 已證明這是敵人打不斷的根因） |
| `DamageDefinition.asset` | 從 `actionDefinitions` 清單移除；內容轉成 Hurt 參數 |

### F.5 測試

| 測試 | 改動 |
|---|---|
| `A13'_StateTopologyMatchesAcceptedAndTrialADRs` | 加 `"Hurt"` |
| `ActionStateTests` 的 Reaction 相關（`T14`／§383/404/551/593/631 那幾條） | 改為驗 `HurtState`；**斷言本身的語意不變**（被打→播 Damage→恢復） |
| `SlowEffectTests.GroundEffect_DeliversSameReactionAndSlowSeams…` | 改讀新的 hurt seam |
| 新增 | `HurtStateTests`：進入／時長／可再次受擊／Super Armor 不進／Death 可穿透 |
| `ArchitectureRegressionTests` | 若 seam 改成 Intent bool，`WriterRules` 要跟著調 |

### F.6 ⚠️ 遷移期的開放問題（需要裁決，不要自己決定）

1. **受擊 request 用哪條 seam**：沿用 `ActionRequestTarget`（改動最小，但它的名字說自己是 Action 的信箱），
   還是在 `IntentData` 加一個 `HurtRequested` bool（語意最乾淨，但**外部事件寫 Intent** 與
   「Intent 由順序 2 從輸入產生」的既有契約衝突）。**兩者都有代價，不是明顯的一邊。**
2. **`Jump → Hurt` 要不要開**：現在是關的（空清單），且那是**無意的**——它是「Jump 不可被打斷」的副作用，
   不是有人決定過「空中不該有受擊反應」。
3. **Hurt 的 priority 放哪**：必須 > Action(10)、< Death。與 Roll(20)、Traversal(30) 的相對關係要裁決
   （翻滾中受擊？攀爬中受擊？）。

---

## G. Future Extension Check

| # | 案例 | Model A（現況） | **Model B（建議）** |
|---|---|---|---|
| 1 | 普通 Attack 被 Hurt 打斷 | ✅ `Interruptible: 1` ＋ `CanReenter` 路徑 | ✅ config 把 Hurt 放進 Action 的 `CanBeInterruptedBy`；`Interruptible` 仍可 per-phase 收窄 |
| 2 | Super Armor Attack 不被 Hurt 打斷 | ✅ `Interruptible: 0` | ✅ 同上 |
| 3 | Super Armor Attack 仍可死亡 | ⚠️ 需要**硬編碼的型別例外**（ADR-009 D4），且目前不可達 | ✅ Death priority 最高，**不需要例外**（E.3） |
| 4 | Hurt 中再次受擊 | ⚠️ 需要 `AllowsSameSlotReentry` 的硬編碼特例 | ✅ `HurtState.CanReenter => true`，與 Animancer 範例的 `CanInterruptSelf => true` 同形 |
| 5 | Stun 可以打斷 Super Armor | ❌ 表達不出來（單一 bool），且 Stun 與 Hurt 同為 Action ⇒ 要在 `ActionState` 建 slot 比較表 | ⚠️ 也需要把 `Interruptible` 加寬成 priority 門檻，**但加寬用的是既有的 `StateRule.Priority`**，不是新框架 |
| 6 | Death 後永遠不能回 Action | ✅ 三層鎖（`CanTransitionAway=false` ＋ 空 `CanBeInterruptedBy` ＋ 空 `ValidTransitions`） | ✅ 同（與 Model 無關） |
| 7 | Traversal 中受到 lethal damage | ❌ 目前**不可能**（A.5）。修法是資產：把 Death 加進 Traversal 的清單。⚠️ `TraversalState.OnExit` 會 `RestoreTraversalCollisionProfile` ＋ `EndTraversalPresentation`，所以**一旦開啟是安全的** | ✅ 同 |

---

## H. Death 專章（回答 §7）

**刻意區分兩件事**：

| | Death **transition authority** | Death **animation playback** |
|---|---|---|
| 誰 | `CharacterHealth.ApplyDamage` commit `IsDead` → 順序 0.5 發布 → `DeathState.CanEnter` 讀 → config 的 `CanBeInterruptedBy` 授權 → FSM `TransitionTo` | 順序 5 `SyncAnimation` → `AnimancerFacade.Play("Death")` |
| 失敗時 | 角色不會死 | **角色仍然是死的**，只是姿勢不對（`AnimancerFacade.Play` 查表失敗只 LogWarning 並 return） |

⇒ **兩者已經是解耦的**，這是目前設計的一個優點：缺動畫資產不會讓死亡失效。

| 問題 | 答案 |
|---|---|
| lethal damage 發生在哪一層 | `Core/Survivability/CharacterHealth.ApplyDamage`，由 Physics 階段的 `OnTriggerEnter`（三個 Presentation sink）觸發 |
| Death request 誰產生 | **沒有 request。** 與 Action 不同，Death 沒有 mailbox——`CharacterHealth` commit 一個持續狀態，`DeathState` 每幀輪詢它。這是**刻意的**：死亡不是一次性事件，是不可逆的狀態 |
| 能否打斷所有該打斷的 state | ❌ **目前一個都打不斷**（A.5）。修法純屬資產 |
| 死亡後 Input / AI intent 如何停止 | `DeathArbiterSource` → `BlockInput` → 順序 2 閘門把 `inputData` 歸零。⚠️ **有一幀延遲**（仲裁在 4.5 寫、閘門在下一幀 2 讀），刻意的既有取捨 |
| locomotion 是否停止 | ⚠️ **不是立刻停**。輸入歸零 → `MovementIntent` 歸零 → `LocomotionModel` 的 B9 減速把速度收到 0 ⇒ 角色會**滑行一小段才停**。`DeathState.OnUpdateMotion` 走 `ExecuteBaseMovement`，仍讀 `MoveSpeed × MoveDirection`。**這一點值得單獨裁決**：死亡要不要立刻歸零水平速度 |
| Action 是否 cleanup | ✅ `TransitionTo` 一定呼叫 `OnExit` ⇒ `ActionState.OnExit` → `ActiveLifecycleSink()?.Cleanup()`（關 hitbox）＋ `CommitCooldown()`。**死亡時正開著的近戰命中窗會被正確關閉** |
| Hurt → Death | 現況不可能；Model B 下是「把 Death 加進 Hurt 的清單」 |
| Traversal → Death | 現況不可能；一旦開啟是安全的（`OnExit` 會還原 collision profile） |
| 是否可能被正常 State 離開 | ❌ 雙重鎖。⚠️ **但其中一半住在資產裡**——若有人給 Death 填了非空的 `CanBeInterruptedBy`，鎖就開了。建議用測試釘住而不是靠紀律 |

---

## 附：外部來源與它們適用／不適用的部分

### Animancer 官方 FSM 範例（**本專案實際使用的動畫系統**）

- `ActionState` 與 `FlinchState` 是**兩個獨立的 state 類別**，都繼承 `CharacterState`；
  **`FlinchState` 完全不重用 `ActionState`**，只共用基底的中斷機制。
- 優先級：`ActionState` = Medium，`FlinchState` = **High**。
- `CharacterState.CanExitState` 的核心比較：
  `if (nextState == this) return CanInterruptSelf; else if (Priority == Low) return true; else return nextState.Priority > Priority;`
- **兩者都 `CanInterruptSelf => true`**——對應本 repo 的 `AllowsSameSlotReentry(Reaction)`，
  但 Animancer 把它給了 Action **與** Flinch，本 repo 只給了 Reaction。
- `FlinchState` 由 `HealthPool` 的 `OnHitReceived` 事件觸發 ⇒ **health 在 FSM 外，與 ADR-009 D1／D2 一致**。

**適用**：state 分離、priority 比較、`CanInterruptSelf`、health 在 FSM 外。
**不適用**：Animancer 的 FSM 是**程式碼組態**（priority 寫在 C# 屬性裡）；
本 repo 刻意把拓撲放進 **serialized config**（`StateRule`），那是本專案的 data-driven 原則，不要為了像範例而放棄。

### Unreal GAS

**適用的原則**：
- **Attribute（Health）／Gameplay Effect（Damage）／Ability（行動）是三件事** ——
  對應本 repo 的 `CharacterHealth` ／ 傷害投遞 ／ `ActionState`。ADR-009 D1／D2 與此一致。
- **取消與封鎖是 ability 的 authored metadata**（`CancelAbilitiesWithTag`／`BlockAbilitiesWithTag`），
  不是散在程式裡的 if ⇒ 支持「中斷授權留在資產」。

**不適用的部分**：
- GAS **可以**把 Hit Reaction 做成 Ability——但那是因為 GAS 的 ability 有完整的 tag 與
  cancel/block 矩陣可以表達「這個 ability 不是主動技能」。**本 repo 沒有那層**，
  Reaction 只能靠 `ActionState` 裡的兩處硬編碼反轉來假裝（C.1）。
  ⇒ **「GAS 把 hit reaction 當 ability」不能拿來支持 Model A**，因為前提條件不同。
- GAS 的一個已知坑值得記：**ability 被 hit reaction 中斷時若 `EndAbility` 沒跑完，
  `ActivationOwnedTags` 會殘留並永久封鎖其他 ability。**
  本 repo 的對應保護是 `TransitionTo` **一定**呼叫 `OnExit` ⇒ `Cleanup()` ＋ `CommitCooldown()`。
  **那條保護必須維持**——任何未來的「中途切換」若繞過 `OnExit`，就會重現 GAS 的這個坑。

**來源**
- Animancer — Interruptions sample：<https://kybernetik.com.au/animancer/docs/samples/fsm/interruptions/>
- Animancer — Characters sample（`CharacterState` 基底）：<https://kybernetik.com.au/animancer/docs/samples/fsm/characters/>
- Animancer — Changing States（`CanEnterState`／`CanExitState`／Try vs Force）：<https://kybernetik.com.au/animancer/docs/manual/fsm/changing-states>
- Unreal — Using Gameplay Abilities：<https://dev.epicgames.com/documentation/unreal-engine/using-gameplay-abilities-in-unreal-engine>
- Unreal — Understanding GAS：<https://dev.epicgames.com/documentation/en-us/unreal-engine/understanding-the-unreal-engine-gameplay-ability-system>

---

## I. Hurt request seam 專查（2026-09-14，使用者裁決 Model B 之後）

> **裁決前提**（使用者 2026-09-14）：採 Model B、新增 `HurtState`；v1 Hurt 可打斷
> **Locomotion／Action／Jump**，**不**打斷 Roll／Traversal；Death 打斷全部；
> **不擴充** `ActionPhaseEntry.Interruptible`。
> 本節只回答一件事：**受擊 request 從哪條既有 seam 進 FSM。** 仍未實作。

### I.1 候選清單（只列既有 seam）

| # | Seam | 機制 | 既有先例 |
|---|---|---|---|
| **S1** | `ActionRequestTarget`（`ActionSlot.Reaction`） | 單格 mailbox，FSM 於順序 4 讀、`Tick` 的 `finally` 清 | 現行受擊路徑 |
| **S2** | **`SurvivabilityData` 黑板區** | `CharacterHealth` 單一寫入者，順序 0.5 發布，順序 4 被 `CanEnter` 讀 | **`DeathState.CanEnter` ← `IsDead`（同區、同寫入者、同時序）** |
| **S3** | `IntentData.HurtRequested` | Runner 於順序 2 由 `InputData` 翻譯 | `JumpRequested`／`RollRequested` |
| **S4** | 建構子注入 `CharacterHealth` 給 `HurtState` | `TraversalState(probe)` | ADR-008 |
| **S5** | `PresentationEventData` | 順序 6.5 廣播快照 | 腳步音 |

### I.2 三個可以直接淘汰的

**S3 — 被三條獨立規則擋住，不是偏好問題**

1. **`A5` WriterRules**：`Intent` 的唯一寫入者是 `CharacterPipelineRunner.cs`。
   `CharacterHealth` 寫它會直接讓 `A5` 變紅。
2. **`A21_ExternalActionSeams_HaveNoAnimationOrTransitionAuthority`**：明文禁止外部 gameplay seam
   出現 `IntentData` 或 `.Intent`（目前涵蓋 `ActionRequestTarget` ＋ 三個 sink）。
   `CharacterHealth` 正是同一類 seam。
3. **語意**：`IntentData` 的檔頭寫著「記錄這一瞬間**想**做什麼」。**受擊不是想，是被。**
   把它塞進 Intent 等於重演本審查 §C 要修的那個錯誤，只是換一個區。

> 唯一的變形「`CharacterHealth` 寫 Survivability → Runner 於順序 2 翻譯成 `Intent.HurtRequested`」
> 是**下游重新推導已 commit 的狀態**——`docs/16` 點名的加重缺陷。一併否決。

**S4 — 已被使用者的既有裁決排除**

ADR-009 D3 原話：「**不採每個 reader 各自注入 `CharacterHealth` reference 的方式**」，
理由是 health／dead 是多個 runtime 系統共享的角色真相。注入還會讓 `HurtState` 取得
黑板以外的第二個真相來源。

**S5 — 層級與時序都錯**

`PresentationEvents` 於順序 6.5 **末尾**發布、**下一幀** 6.5 消費（廣播快照，consumer 不得清除）。
FSM 在順序 4 讀 ⇒ 永遠晚一幀，且它明確是**表現層**容器，不是 gameplay 仲裁輸入。

### I.3 真正的選擇：S1 vs S2

#### 🔎 先更正本審查 §C.3 的一個過度推論

§C.3 說「受擊與主動意圖爭同一格 mailbox」是 starvation bug 的結構成因。
**更精確的說法是**：bug 出在 `ActionState.TryResolveRequest` 的**偏好順序**
（`Intent` 優先、為 `None` 才讀 mailbox），不是 mailbox 本身被共用。

⇒ **只要 `HurtState` 直接讀 `PendingSlot`、不套那層偏好，S1 也能修掉 starvation。**
而且 mailbox 目前的**唯一寫入者就是 `CharacterHealth`**，競爭在現況下是假想的，不是實際的。

**這一點削弱了「一定要離開 mailbox」的論證，所以要靠別的證據決定。**

#### S1 — 沿用 `ActionRequestTarget`

| ✅ | ❌ |
|---|---|
| 零新 schema；FSM 已集中持有並清除，加第二個 reader 機制上免費 | mailbox 型別是 `ActionSlot` ⇒ **`ActionSlot.Reaction` 必須續命**，enum 繼續帶一個不是 Action 的成員 |
| **保住 `docs/15` §4.1 的既有契約**（見 I.4） | `ActionState` 需要停止回應 slot 100 |
| 單幀、不排隊、不重試——對受擊**正好正確**（翻滾中挨的那一下應該被丟棄，不是排隊等翻滾結束） | ⚠️ 若靠「把 `DamageDefinition` 從 config 的 `actionDefinitions` 移除」讓 `ActionState` 自然拒絕，會觸發 `WarnMissingDefinitionOnce` ⇒ **每次被打噴一次 Editor 警告**，還是得在 `ActionState` 加一條 slot 100 的例外 |

⇒ 淨效果：拿掉 §C.1 的兩處反轉，**再加回一處**。**−2 +1 = −1**，不是 −2。

#### S2 — `SurvivabilityData` 加一個單幀旗標

| ✅ | ❌ |
|---|---|
| ⭐ **與 Death 完全對稱**：`HurtState.CanEnter ← JustTookDamage`／`DeathState.CanEnter ← IsDead`，同寫入者、同區、同發布點（0.5）、同讀取點（4）。**整條 survivability → FSM 的邊只剩一種機制**，不是兩種 | 黑板 schema 變更（需修訂 ADR-009；但 §7 明文把「欄位組成」列為**不凍結**，甚至舉了 `JustDied` 為例 ⇒ 在 Trial 允許範圍內） |
| `ActionRequestTarget`／`ActionSlot` 回歸「只講 Action」⇒ `ActionSlot.Reaction` 可退役，`ActionState` **一條新例外都不必加**（淨 −2） | 把**單幀事件**放進文件寫著「持續型」的區。需要明確定義復位機制並更新 dev-spec §1.1 |
| `CharacterHealth` 不再需要持有 `ActionRequestTarget` 引用 ⇒ prefab 少一個要接的欄位 | **會打破 `docs/15` §4.1** ⇒ 必須一併遷移（見 I.4） |
| 任何來源的傷害都對**所有** reader 可見，不限於「記得戳 mailbox」的那些 | |

**復位機制**：`CharacterHealth` 持 `_pendingHurt`；`PublishTo`（順序 0.5）寫出後即清除。
因為 `PublishTo` **每幀整體覆寫整個區**，下一幀自動是 `false`。
⇒ 與 `PresentationEventData` 的理由**一字相同**：「每帧從 `default` 整體覆寫即是它的復位機制，
`ResetTransientState` 刻意不認識本欄位」。**不需要為它開順序 7 的例外。**

### I.4 🔴 兩條 seam 都必須處理的既有相依（本次調查最重要的發現）

`docs/15-combat-context.md` §4.1 **明文**把受擊列為戰鬥語境的進入條件：

| 條件 | 資料哪裡來 |
|---|---|
| **受到敵對 Action** | **`ActionRequestTarget.PendingSlot`（既有 seam）** |

而 §4.2 的離開規則用「距離上一次敵對互動（出手／**被打**）超過 `disengageSeconds`」當時間基準。
程式在 `PlayerCombatContextSource.cs:52–53`：

```csharp
bool interactionThisFrame = data.Intent.RequestedActionSlot != ActionSlot.None ||
                            (_selfRequestTarget != null && _selfRequestTarget.HasPendingRequest);
```

> ⇒ **mailbox 對 combat context 是 load-bearing 的，而且是寫進文件的契約。**
> 選 S2 卻不動這行，玩家「被打」將不再刷新交戰計時 ⇒ **被圍毆時反而會提早脫離戰鬥語境**。
> 這是一個安靜的行為退化，不會有任何錯誤訊息。

**S2 的必要遷移（一行）**：把該判斷改讀 `data.Survivability.JustTookDamage`。
⚠️ `PlayerCombatContextSource` 已經收到 `PlayerRuntimeData`，**不需要新引用**。

**順帶關掉一個既有 TODO**：`docs/15` §4.2 的 fold-back 寫著
「目前**沒有 Health／Death 契約** ⇒『死亡』只能以被摧毀／停用表示…等真的有死亡流程再回來接（§16-7）」。
ADR-009 之後那個契約存在了 ⇒ **S2 讓 §16-7 可以一併結案**（目標合法性可改讀 `IsDead`）。

### I.5 ✅ 結論：**S2 —— `SurvivabilityData` 加單幀旗標**

判準不是「哪個比較乾淨」，是這三點：

1. **對稱性**：Hurt 與 Death 是同一個來源（`CharacterHealth`）產生的兩種後果。
   讓它們走同一條 seam，`ActionRequestTarget` 才能回到只講 Action——
   而那正是本審查整份文件的主張。S1 會讓「受擊」繼續住在一個名字寫著 Action 的信箱裡。
2. **例外淨值**：S2 讓 `ActionState` 的兩處反轉**歸零**；S1 只能降到一處（I.3 的警告問題）。
3. **既有裁決一致**：ADR-009 D1／D2 已把「被打之後會發生什麼」交給 `CharacterHealth` 獨佔決定。
   S2 讓它用**同一個發布動作**表達兩種後果；S1 要它同時持有黑板與 mailbox 兩條出口。

### I.6 依此結論的最小形狀（**待使用者確認後才實作**）

```csharp
// Core/Blackboard/SurvivabilityData.cs —— 加一個欄位
public bool JustTookDamage;   // 單幀；由 CharacterHealth 於順序 0.5 發布後即清，
                              // 整區覆寫即復位（同 PresentationEventData，不進 ResetTransientState）

// Core/Survivability/CharacterHealth.cs
//   ApplyDamage 非致死分支：_pendingHurt = true   （取代 RequestAction(ActionSlot.Reaction)）
//   PublishTo：  data.Survivability.JustTookDamage = _pendingHurt;  _pendingHurt = false;
//   ⇒ 不再需要 reactionTarget 欄位

// Core/StateMachine/States/HurtState.cs —— RollState 的形狀，~80 行
//   CanEnter(data)   => data.Survivability.JustTookDamage
//   CanReenter(data) => data.Survivability.JustTookDamage      // 受擊中再次受擊
//   CanTransitionAway => _timer <= 0
//   OnUpdateMotion   => ExecuteBakedCurveMovement(bake, normalizedTime, data)
```

**參數來源：不需要新的資產型別。** `Bake_Fists_Hit_Right.BakedDuration = 1.1666667`，
與 `DamageDefinition.FallbackDuration = 1.166667` 完全一致
⇒ 比照 `RollState`：`Config.GetBakeData(StateType.Hurt)` 走既有的 `bakeMappings`，
時長取 `bake.Duration`，查無資料時退化為常數。
（`bakeMappings` 現在只有一筆 `State: 4`（Roll），加一筆 `State: 8`（Hurt）即可。）

**config 資產（依使用者裁決）**：

| State | Priority | `CanBeInterruptedBy` 要加什麼 |
|---|---|---|
| Idle / Move | 0 | **+ Hurt**、+ Death |
| Jump | 10 | **+ Hurt**、+ Death（⚠️ 目前是空清單，見 §B.3） |
| Roll | 20 | 只 + Death（**不加 Hurt** ← 裁決） |
| Action | 10 | **+ Hurt**、+ Death |
| Traversal | 30 | 只 + Death（**不加 Hurt** ← 裁決） |
| **Hurt** | **介於 Action(10) 與 Roll(20) 之間 ⇒ 建議 15** | **+ Hurt**（自我重入）、+ Death |
| Death | 100 | （空） |

> ⚠️ Hurt priority 必須 **> Action(10)**，否則同幀 Action 與 Hurt 都成立時
> `priority > highestPriority` 的 strict `>` 會讓先註冊的贏（§A.2）。
> 取 15 同時保證 **< Roll(20)** ⇒ 翻滾仍然壓過受擊，與「Hurt 不打斷 Roll」的裁決一致。

### I.7 仍需使用者確認的兩點

1. **`ActionSlot.Reaction = 100` 的處置**：ADR-005 明令「要淘汰某一格請**留著它的數值**」。
   建議標為已退役的註解＋不再有任何 Definition 使用，**不移除成員、不回收數值**。
2. **`docs/15` §16-7 要不要一併結案**（目標合法性改讀 `IsDead`）。
   它不是 Hurt 的必要條件，但 S2 讓它變成幾行的事。**可以延後，不建議偷偷做。**

---

## J. 實作結果（2026-09-14 fold back）

> **狀態**：✅ **Model B migration 已完成並驗證。**
> EditMode **468 / 467 passed / 0 failed / 1 skipped**（唯一 skip 是既有的 NavMesh 環境限制）；
> PlayMode **41 / 41**；live Editor recompile 0 errors。
> ⚠️ **人類 Play 驗收仍未做**（動畫好不好看、手感）——見 §J.5。

### J.1 程式

| 檔案 | 動作 |
|---|---|
| `Core/StateMachine/StateType.cs` | `+ Hurt = 8`（加在最後，不遞補） |
| `Core/StateMachine/States/HurtState.cs` | 🆕 ~120 行（含註解），`RollState` 形狀 |
| `Core/StateMachine/FullBodyStateMachine.cs` | 註冊 `HurtState`（無建構參數） |
| `Core/Blackboard/SurvivabilityData.cs` | `+ JustTookDamage`（單幀，publish-and-clear） |
| `Core/Survivability/CharacterHealth.cs` | 改為發布 `_pendingHurt`；**移除 `ActionRequestTarget` 依賴**與 `reactionTarget` 欄位 |
| `Core/Combat/PlayerCombatContextSource.cs` | 交戰互動改讀 `Survivability.JustTookDamage`；刪掉無人讀取的 `_selfRequestTarget` 與 `Awake` |
| `Core/Actions/ActionSlot.cs` | `Reaction = 100` 標 `[Obsolete]` ＋ 退役說明；**成員與數值保留** |
| `Core/StateMachine/States/ActionState.cs` | **刪掉兩處 Reaction 反轉**：`ShouldFaceTargetOnEnter` 回到無例外；`AllowsSameSlotReentry` 整個方法移除，`CanReenter` 回到 `slot != _activeSlot` |

### J.2 資產（全部走 Editor API，⛔ 未手改 YAML）

| 資產 | 動作 | 怎麼還原 |
|---|---|---|
| `PlayerStateMachineConfig` | rules 重寫為 §J.3 的矩陣；`bakeMappings` 加 `Hurt → Bake_Fists_Hit_Right` | 依 §A.4 的舊表回填 |
| `EnemyStateMachineConfig` | 同上；另**移除 `actionDefinitions` 裡的 `DamageDefinition`** | 同上 ＋ 把它加回清單 |
| `EnemyPunchDefinition` | `Phases[0].Interruptible: false → **true**` | 設回 `false` |
| `Y Bot.prefab` | `AnimancerFacade.transitionMappings[8]` 的 `StateKey` `"Damage" → "Hurt"` | 改回 `"Damage"` |
| `X Bot.prefab` | **新增** mapping `"Hurt" → Damage.asset` | 移除該筆 |

> 🔴 **盤點時發現的既有缺陷**（不是本輪造成的）：
> `DamageDefinition` 只掛在**敵人** config 的 `actionDefinitions`，**玩家 config 從來沒有**；
> 而 `X Bot.prefab` 的 37 筆 transition mapping 裡**也沒有任何受擊動畫**。
> ⇒ **玩家在此之前從來不會有受擊反應**，而且不會有任何錯誤訊息。本輪順帶修掉。

### J.3 實測 transition matrix（讀**出貨資產**，非測試自建）

```
=== PlayerStateMachineConfig ===
from\to     Idle  Move  Jump  Roll  Action  Traversal  Hurt  Death | prio  VT
Idle         --    .    YES   YES    YES      YES      YES   YES   |  0    [Move]
Move         .     --   YES   YES    YES      YES      YES   YES   |  0    [Idle]
Jump         .     .    --     .      .        .       YES   YES   | 10    [Move,Idle]
Roll         .     .     .    --      .        .        .    YES   | 20    [Move,Idle]
Action       .     .    YES   YES    --        .       YES   YES   | 10    [Move,Idle]
Traversal    .     .     .     .      .       --        .    YES   | 30    [Jump,Move,Idle]
Hurt         .     .     .     .      .        .       --    YES   | 15    [Move,Idle]
Death        .     .     .     .      .        .        .    --    | 100   []
Hurt bake = Bake_Fists_Hit_Right  duration = 1.166667
```

敵人 config 相同，唯二差異：沒有 Traversal 規則；`Action` 的清單是 `[Hurt, Death]`
（原本是**空的**——敵人出手時連 Roll／Jump 都打不斷）。

⚠️ 表中 `Hurt → Hurt` 顯示 `--` 只是因為對角線被略過。同型重入走 `CanReenter`（`HD3` 驗），
**不經過 `CheckCanInterrupt`**；config 裡 Hurt 自己清單中的 Hurt 是冗餘但無害的。

### J.4 測試

| 測試 | 守什麼 |
|---|---|
| `A13'` | `StateType` 名單 ＋ 順序（Hurt 必須在最後） |
| **`A37`** 🆕 | ①runtime 程式不得引用 `ActionSlot.Reaction`；②Slot-100 資產**只允許一個已知孤兒**（`DamageDefinition.asset`，指名釘住）；③**任何 config 都不得把它接回 `actionDefinitions`**（那才是真正會讓 Action 路徑復活的動作） |
| **`A38`** 🆕 | **出貨 config** 的 permission 矩陣與裁決一致（含 Roll／Traversal 不含 Hurt、所有 living state 含 Death、Death 吸收態、`Hurt priority > Action`） |
| `HurtAndDeathStateTests` 🆕 | `HD1`–`HD9`，含 **`HD8`（把 Hurt priority 拉到 999 仍不得進 Roll）** 與 **`HD9`（Roll 中受擊只扣血、不補播）** |
| `SurvivabilityTests` | 改寫為只驗 `CharacterHealth` 的 commit 行為；新增 `H5`（單幀不排隊）、`H6`（同幀多次命中＝一次硬直）、`H10`（投射物只送傷害） |
| **`CombatContextTests.TC1B`** 🆕 | 受擊必須刷新交戰語境——守住 `docs/15` §4.1 那條 seam 遷移不會安靜失效 |
| `TraversalIntegrationWiringTests` | 舊 invariant「Traversal 不得被任何狀態打斷」**更新為新 baseline**：唯一例外是 Death |
| `ActionStateTests` | `T14`／`T23`／`T32`／`T32b`／`T33`／`TD7` 移除或改寫（見檔內 ⚰️ 註記）；`T22`／`T35` 改用 live slot |

### J.5 ⛔ 仍未做／已知缺口

1. **人類 Play 驗收**：受擊動畫好不好看、硬直手感、死亡姿勢——只有 Play 看得出來。
2. **沒有 Death 動畫**：`Assets/ScriptableObjects/Animation/` 裡**沒有任何 Death transition 資產**
   ⇒ `Play("Death")` 會 LogWarning 並 return。
   ⚠️ **角色仍然是死的**（transition authority 與 animation playback 已解耦，見 §H），
   只是維持上一個姿勢。選哪一支死亡 clip 是 authoring 決定，**不代為挑選**。
3. **`DamageDefinition.asset` 仍在磁碟上**，Slot 仍是 100，但**沒有任何 config 引用它**，
   內容已全數遷移（bake → `bakeMappings`、key → prefab mapping、duration → bake）。
   刪除屬破壞性動作 ⇒ **等使用者裁決**；在那之前由 `A37` 指名釘住。
4. **`ActionRequestTarget` 現在沒有任何 runtime 寫入者**（它仍是公開 API，且
   `PlayerCombatContextSource` 仍用它當**敵對目標的身分標記**）。
   `docs/20` §2.7 的 starvation 風險依然登記為未解，由 `T35` 繼續守。

---

## K. Hurt ／ Death 垂直切片收尾（2026-09-14）

> **狀態**：✅ **遊戲內可用。** EditMode **471／470 passed／0 failed／1 skipped**（唯一 skip 是既有 NavMesh 環境限制）；
> PlayMode **45／45**；live Editor recompile 0 errors。
> ⚠️ 仍未做的只剩**人眼**的部分：動畫好不好看、硬直手感、死亡姿勢（§K.7）。

### K.1 使用的動畫素材

| 用途 | Clip | 來源 | 長度 | 備註 |
|---|---|---|---|---|
| **Hurt** | `Fists_Hit_Right` | `MovementAnimsetPro_Fighting.fbx` | **0.700 s**（trim 後） | 原 1.167 s |
| **Death** | `Death_1` | 同一支 FBX | 2.467 s | 同一套 Kubold rig，兩隻 bot 共用 |

**為什麼是 `Death_1`**：專案內窮舉 325 支 clip 後，死亡候選只有 `Death_1`／`Death_2`
（檔名完全看不出來——memory `kubold-fbx-name-does-not-predict-content` 再次應驗）。
兩者都能 Humanoid retarget、最終 `hipsY` 都落在 0.09–0.13 m（確實躺平）。
選 `Death_1`：**較短**（2.467 vs 3.433 s）、**位移較小**（1.040 vs 1.218 m）⇒ 最保守。
⛔ 沒有建立角色專屬 Death framework，Player／Enemy 共用同一支。

### K.2 ⭐ 本輪最重要的發現：**Hurt 的 bake 不能拿來驅動 gameplay**

使用者要求「不要因為 duration 一樣就自動認定 bake 正確」。實測結果證明這個懷疑是對的：

| 量測 | 值 |
|---|---|
| `Fists_Hit_Right` 原始 root 位移 | **1.150 m**（沿 −Z ＝**往後**），0 垂直、0 yaw |
| `Bake_Fists_Hit_Right` 積分位移 | **1.1342 m**，`GetHorizontalDisplacementAt(end)` = 1.1324，**峰值 2.27 m/s** |
| 烘焙器的速度取法 | `Vector3.Distance(...)` ⇒ **無號** |
| `ExecuteBakedCurveMovement` 的方向 | `transform.forward × worldSpeed` |

> ⇒ **一支「往後踉蹌」的動畫，會把受擊者以最高 2.27 m/s 往前推 1.13 m，撞進攻擊者。**
> 方向錯、量級也錯（那是 knockback，本輪明確排除）。

**量法已驗證**（控制組）：`Fists_Idle` 0.000、`Fists_Punch_R` 0.000（其 bake 也是 0.000）、
`WalkFwdLoop` 1.644 m/s、`RunFwdLoop` 2.743 m ⇒ 量到的 1.13 m 是真的。

**修法**（不在 state 裡乘 offset）：
1. **匯入設定**：`Fists_Hit_Right` 與 `Death_1` 的 XZ（Death 另含 Rot）改 **Bake Into Pose**
   —— 依 §0.4 首句「執行期用不到的成分一律 Bake Into Pose」。視覺保留後退，膠囊不動。
2. **位移路徑**：`HurtState`／`DeathState` 改用 `MotionDriver.ExecuteVerticalOnlyMovement`
   —— **既有**能力（首個使用者是 `JumpState` 的 HardRecovery），語意正好是「只結算朝向／重力／接地」。
3. **bake 只剩一個職責：提供時長。** 秒數因此仍住在資產，不在程式（`W20` 釘住）。

> ⚠️ **實測到的一個反直覺事實**：`lockRootPositionXZ`（Bake Into Pose）**不影響
> `AnimationClip.SampleAnimation` 讀到的 root 曲線** ⇒ 烘焙器仍會量到原始位移。
> 「Bake Into Pose」修的是**視覺**，不是 bake 的內容。這兩件事必須分開講，已寫進 dev-spec §0.4。

### K.3 手感第一版（沿用既有可配置資料，⛔ 未在 state 寫死秒數）

| 調整 | 前 | 後 | 依據 |
|---|---|---|---|
| `Fists_Hit_Right` 幀範圍 | `[0..35]`（1.167 s） | **`[0..21]`（0.700 s）** | 逐格量姿勢偏離：衝擊峰值在 **f=13**（head 偏離 0.389 m），其後 0.73 s 全是回穩 ⇒ 保留衝擊＋部分回穩，其餘交給 transition fade |
| `Bake_Fists_Hit_Right.Duration` | 1.1667 | **0.700**（用既有 baker 重烘） | 時長真相跟著 clip 走 |
| `Damage.asset` fade | 0.15 | **0.10** | 受擊要立刻看得見 |
| `Death.asset` fade | — | **0.20** | 死亡不需要急促 |

使用者裁決「不應讓角色每次被打都失去控制一秒以上」⇒ **0.700 s** 達標。Player／Enemy 共用同一 timing。

### K.4 Health ／ Damage 第一版數值（全部 authored，⛔ 程式無 magic number）

| 項目 | 值 | 住在哪 |
|---|---|---|
| Player HP | **100** | `X Bot.prefab` → `CharacterHealth.maxHealth` |
| Enemy HP | **100** | `Y Bot.prefab` → 同上 |
| 玩家近戰（Melee Slash） | **25** | `X Bot/MeleeHitbox` → `MeleeHitboxSink.damage` |
| 敵人近戰（Punch） | **25** | `Y Bot/MeleeHitbox` → 同上 |
| Fireball | **20** | `Projectile_Fireball.prefab` → `ThrownProjectile.damage` |
| Slow 投射物 | **15** | `ThrownProjectile.prefab` → 同上（低傷＋減速） |
| Ice 地面 AoE | **15** | `X Bot/IceGroundEffect` → `GroundEffectSink.damage` |

⇒ **雙方都是 4 下致死**：不會一碰就死，也不需要打幾十次。

### K.5 🔴 盤點時發現、本輪順帶修掉的三個既有靜默缺陷

1. **玩家從來沒有過受擊反應** —— `DamageDefinition` 只掛在**敵人** config；
   `X Bot` 的 37 筆 transition mapping 裡也**沒有任何受擊動畫**。失敗模式是「什麼都沒發生」。
2. **敵人的拳頭從來不造成傷害** —— `Y Bot` **完全沒有 `MeleeHitboxSink`**、
   `actionSinkBindings` 是空的、`EnemyPunchDefinition.EmitsRelease = false`（命中窗永遠不開）。
3. **兩隻角色都沒有 `CharacterHealth`／`DeathArbiterSource`** —— 上一輪列為人工項但未執行。
   ⇒ 現在由 `W18` 釘住：**每隻帶 Runner 的 prefab 都必須能受傷與死亡**。

### K.6 資產改動一覽（全走 Editor API，⛔ 未手改 YAML）

| 資產 | 改了什麼 | 怎麼還原 |
|---|---|---|
| `MovementAnimsetPro_Fighting.fbx` | `Fists_Hit_Right`：`lockRootPositionXZ=true`、幀範圍 `[0..21]`；`Death_1`：`lockRootPositionXZ=true`、`lockRootRotation=true` | 設回 false／`[0..35]` 後重烘 |
| `Bake_Fists_Hit_Right.asset` | 重烘 ⇒ `Duration` 1.1667 → **0.700** | 還原匯入設定後重跑 baker |
| `Death.asset` 🆕 | 由 `Damage.asset` 複製、`_Clip` 換成 `Death_1`、fade 0.2 | 刪除該資產 |
| `Damage.asset` | fade 0.15 → 0.10 | 設回 0.15 |
| `X Bot.prefab` | ＋`CharacterHealth`(100)、＋`DeathArbiterSource`、＋mapping `Death`、melee damage 25、Ice damage 15 | 移除元件／mapping；數值設回 10 |
| `Y Bot.prefab` | ＋`CharacterHealth`(100)、＋`DeathArbiterSource`、＋`MeleeHitbox` 子物件（複製自 X Bot，localPos (0,1,0.9)、size (0.9,1.2,1.2)）、＋`actionSinkBindings[Slot1]`、＋mapping `Death` | 移除上述 |
| `EnemyPunchDefinition.asset` | `EmitsRelease` false → **true** | 設回 false |
| `Projectile_Fireball` / `ThrownProjectile` | damage 10 → 20 / 15 | 設回 10 |
| ⚰️ `DamageDefinition.asset` | **已刪除**（GUID `f662023934bbb5e4d887242d6df1b9e9`，刪前確認 `grep -rl` 在 `Assets/` 零引用） | 重建一份 Slot=100 的 Definition（⛔ 不建議） |

### K.7 ⛔ 仍未做（人眼）

1. **人類 Play 驗收**：受擊動畫是否清楚、0.7 s 硬直是否過長或過短、死亡姿勢是否合理、
   屍體是否會因為 `Death_1` 的 1.04 m 姿勢位移而插進牆——**這些只有看得到的人能判斷**。
2. **屍體 mesh 會離膠囊約 1 m**：`Death_1` 的位移已 Bake Into Pose ⇒ **膠囊不動**（`PD1` 釘住），
   但 mesh 仍會倒向一側。貼牆死亡時有機會穿模。第一版接受，需要時再換 clip 或加位移限制。
3. **沒有屍體清理／respawn**：`DeathState` 是吸收態，死了就永久停在那裡（`HD7` 釘住）。

### K.8 新增／更新的不變量

| 測試 | 守什麼 |
|---|---|
| **`W18`** 🆕 | 每隻角色 prefab 都有 `CharacterHealth` ＋ `DeathArbiterSource` |
| **`W19`** 🆕 | `Hurt`／`Death` 動畫鍵在 transition mappings 裡解析得出來 |
| **`W20`** 🆕 | Hurt 時長來自 bakeMapping（不是程式常數）；**Death 刻意不要求 bake** |
| **`PD1`** 🆕 PlayMode | **死亡不得滑行**——刻意不歸零 `MoveSpeed` 來證明與 `BlockInput` 無關 |
| **`PD2`** 🆕 PlayMode | 死亡仍受重力（判準與幀率無關） |
| **`PD3`** 🆕 PlayMode | 硬直期間無水平位移 |
| **`PD4`** 🆕 PlayMode | 單幀發布語意在真實 Update 迴圈裡成立 |
| **`A37`** 收緊 | 由「只允許一個已知孤兒」改為**零容忍**：無 runtime 引用、無 Slot-100 資產、無 config 接線 |
