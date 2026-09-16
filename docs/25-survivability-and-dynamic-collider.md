# 25 — Survivability（Hurt／Death）＋ Dynamic Collider（實作切片規格）

> **狀態**：🟡 D1–D3 **已裁決**（§5）；Stage A／B／C **程式已落地並通過 `dotnet build`**，
> **但測試未跑、資產未改** —— 見 **§7 實作狀態**。⛔ 不得據此宣稱任何 Acceptance Criteria 已通過。
> **治理**：W2＋W3 由 **ADR-009（Trial）** 管；W1 不開 ADR，正本就是本檔 ＋ dev-spec §3.2。
> ⚠️ **§3.1 的檔案清單是規劃時的版本**，實際落地與它有差異（`DamageInfo`／`SurvivabilityProfileSO`
> 未建立、`JustDied` 未加入、多了 `DeathArbiterSource`）——**以 §7.1 與 ADR-009 §4.1 為準**。
> **性質**：實作切片規格（同 `docs/19`／`docs/20` 的體例），不是 ADR、不是 Living Doc 正本。
> **前置**：Traversal 已暫停（`docs/24` §19）。本輪不碰 traversal 的任何修法。
> **日期**：2026-09-14

---

## 0. 本輪範圍

| # | 工作方向 | 一句話 |
|---|---|---|
| **W1** | 動態 Collider / Capsule | 讓碰撞體在角色實際佔用的空間上更貼合，**不是單純放大** |
| **W2** | Health／Damage／Hurt／Death | 玩家與敵人共用同一套底層語意 |
| **W3** | Action 被受傷／死亡打斷 | 中斷與否是**可擴充的 policy**，不是寫死的 `if` |

### ⛔ 明確不做

- 不建 Combat Framework／Effect Framework／Status Effect 系統。
- 不另造 Health State Machine 或獨立的 Interrupt System（見 §2：現有架構承載得住）。
- 不重新設計 `FullBodyStateMachine`。
- 不做數值平衡（placeholder 數值即可，但**必須可由資產調整**）。

---

## 1. 現況盤點（2026-09-14 磁碟核對，可直接信任）

### 1.1 ⭐ 最重要的三個發現

> **這三點決定了本輪「真正需要新增的東西」比預期少很多。**

#### F1 — **受擊反應已經存在，而且是一個 Action**

`ActionSlot.Reaction = 100`（保留段，無輸入來源）＋ `DamageDefinition.asset`（Slot 100、
`AnimationKey: Damage`、`FallbackDuration 1.1667`、`Interruptible: 1`）已經在 repo 裡，
由三個投遞者經 **同一條 seam** 送出：

```
MeleeHitboxSink.TryRequestHit    ─┐
ThrownProjectile                  ├─→ ActionRequestTarget.RequestAction(ActionSlot.Reaction)
GroundEffectSink.ApplyToRoot     ─┘        → FSM → ActionState → 播 "Damage"
```

⇒ **不需要新增 Hurt state。** 「受傷反應」在本專案的既有語意裡就是「一個沒有輸入來源的 Action」。

#### F2 — **「這個 Action 能不能被打斷」的 policy 欄位已經存在**

`ActionPhaseEntry.Interruptible`（per-phase，authored 在 `ActionDefinitionSO`）。
兩條路徑都讀它：

| 中斷來源 | 路徑 | 讀哪裡 |
|---|---|---|
| 別的 **StateType**（Roll／Jump／未來的 Death） | `EvaluateInterrupts` → `CanBeInterruptedBy` | `ActionState.cs:202` → `base`（config 的 `CanBeInterruptedBy` 清單）**且** `_currentEntry.Interruptible` |
| 另一個 **Action**（含 Reaction） | `EvaluateInterrupts` → `CanReenter` | `ActionState.cs:218` → `_currentEntry.Interruptible` ＋ slot 差異 ＋ 冷卻 |

⇒ 使用者問的「**之後有一個 Action 不會被普通 Hurt 打斷，要在哪裡定義？**」
**答案已經存在：那份 `ActionDefinitionSO` 的該 phase 把 `Interruptible` 設為 false。零新程式。**
Super Armor／Uninterruptible Action／Boss committed action 全部走這一格。

#### F3 — 🔴 **敵人攻擊打不斷，根因是一個既有資產欄位**

```yaml
# Assets/ScriptableObjects/StateMachine/Actions/EnemyPunchDefinition.asset
- Phase: 1
  AnimationKey: Enemy_Punch_R
  Interruptible: 0        # ⛔ 這一格
```

⇒ `CanReenter` 第一關就 `return false` ⇒ **`Reaction` 永遠無法重入敵人的攻擊。**
使用者要的「Enemy Attack → Damage → Attack interrupted → Hurt」**是這個布林值，不是新程式**。

⚠️ 同時證明 F2 的機制是**雙向有效**的：現在它剛好被設成「敵人有 Super Armor」。

### 1.2 完全不存在的東西

| 缺什麼 | 證據 |
|---|---|
| **Health／HP** | `grep -rn "\b(Health\|TakeDamage\|ApplyDamage\|IsDead)\b" Assets/Scripts/` ⇒ **零命中**（唯一的 "Damage" 是資產名） |
| **Damage 數值** | 命中 seam 是 `RequestAction(slot)`——**只有身分，沒有量值** |
| **Death state** | `StateType` 只有 `None/Idle/Move/Jump/Roll/Action/Traversal` |
| **死亡後的行動封鎖** | 無 |

### 1.3 Collider 現況

| 問題 | 答案 | 出處 |
|---|---|---|
| Capsule authority 在哪 | **Root 上的 `CharacterController`＝「物理碰撞體與世界座標的唯一權威」** | **ADR-001（Accepted）** |
| Model 與 Root 的關係 | Root＝邏輯／物理；Model＝子物件，美術／骨骼，`applyRootMotion` 必為 false；**遊戲邏輯禁止直接引用 Model** | ADR-001 §2 |
| 已有 runtime resize 機制嗎 | ✅ **有。** `MotionDriver.BeginTraversalCollisionProfile` ／ `ApplyTraversalCollisionProfile(n)` ／ `RestoreTraversalCollisionProfile`（`MotionDriver.cs:277–304`） | — |
| 那個機制的資料形狀 | `TraversalCollisionProfile`：`enableCenterShift` ＋ `centerX/Y/Z` 三條曲線；`height`／`radius` 兩條通道**保留但預設關閉**（`MotionBakeData.cs:27`） | — |
| 現在有在用嗎 | ❌ **三個 binding 全部關閉、曲線皆空** ⇒ 三個方法每幀在跑、效果為零（`docs/24` §19 C5） | — |
| 編輯期 fitter | `Editor/Tools/CharacterCapsuleFitter.cs`：`height`／`radius`／`center = (0, h/2 + skin, 0)` | — |
| 誰**讀**這顆 capsule | `TraversalProbe`（**逐幀**，14 處：ledge 偵測、corridor、`EntryCapsuleWallClearance`）、`FootIKController`、`MotionDriver.SyncGroundedState`（`characterController.isGrounded`） | — |

**⇒ W1 不是「從零做一個 offset 機制」，是「把既有的 per-animation center-shift 通道，
接上第二個驅動來源（移動方向），並處理它與 Probe／grounding 的一致性」。**

### 1.4 方向資料現況（W1 要挑驅動來源）

| 資料 | 座標系 | 寫入者 | 語意 |
|---|---|---|---|
| `data.MoveDirection` | **世界** | `LocomotionModel`（WriterRules 釘住） | locomotion 的移動輸出方向 |
| `data.MoveSpeed` | 純量 | `LocomotionModel` | 同上 |
| Root `transform.forward` | — | `MotionDriver.ApplyFacingRequest` ／ `transform.Rotate` | **facing**（朝向） |
| committed 位移方向 | — | `MotionDriver.ExecuteBakedCurveMovement` 沿 `transform.forward` | Action／Traversal 期間位移**只沿 facing** |

⚠️ **strafe／後退時 facing ≠ movement**（ADR-007 方向權威）。
⚠️ **Action／Traversal 期間 `MoveDirection` 不是位移真相**——那時位移由 bake 曲線沿 facing 前進。

---

## 2. 可以直接沿用的東西（不要重造）

| 需求 | 沿用什麼 | 要新增嗎 |
|---|---|---|
| 受傷動畫／硬直 | `ActionSlot.Reaction` ＋ `DamageDefinition.asset` ＋ `ActionState` | ❌ |
| 受傷可重入（連續被打） | `ActionState.AllowsSameSlotReentry(Reaction)`（已裁決的例外） | ❌ |
| 受傷不轉向 | `ActionState.SlotTakesFacingCommitment` 的 Reaction 例外（`docs/11` §8.3） | ❌ |
| **「這招不可被打斷」** | `ActionPhaseEntry.Interruptible` | ❌ |
| 敵人攻擊可被打斷 | 改 `EnemyPunchDefinition.Interruptible: 0 → 1` | ❌（資產） |
| 命中投遞 | `MeleeHitboxSink` ／ `ThrownProjectile` ／ `GroundEffectSink` 三個既有 sink | ❌ |
| 狀態優先權／中斷授權 | `StateMachineConfigSO` 的 `Priority` ／ `CanBeInterruptedBy` ／ `ValidTransitions`（per-state，authored） | ❌ |
| gameplay 數值放哪 | `ScriptableObject`（`ActionDefinitionSO`／`StateParamsSO`／`GaitProfileSO` 的既有慣例） | ❌ |
| 角色身上的 runtime 狀態元件 | `TemporaryGameplayEffectState`（掛 Root、由投遞者呼叫、查詢時才結算） | ❌（形狀可仿） |
| runtime capsule center 位移 | `MotionDriver` 的 traversal collision profile 三方法 | ❌（擴充驅動源） |

**現有架構承載得住 W2／W3，不需要另造 Health State Machine 或 Interrupt System。**

---

## 3. 真正需要新增的最小結構

### 3.1 W2 — Survivability

```
Core/Survivability/
  CharacterHealth.cs          MonoBehaviour（掛 Root，玩家與敵人共用同一顆）
  SurvivabilityProfileSO.cs   ScriptableObject：MaxHealth／死亡後保留秒數…（placeholder 可調）
  DamageInfo.cs               readonly struct：Amount ＋（保留）來源方向／類型
Core/Blackboard/
  SurvivabilityData.cs        struct：CurrentHealth／MaxHealth／IsDead／JustDied
Core/StateMachine/
  StateType.Death             新成員（值 7，不遞補既有數值）
  States/DeathState.cs        吸收態
```

#### ⭐ 決策擁有權：**Health 是「被打之後會發生什麼」的唯一決定者**

```
攻擊方 sink ──→ CharacterHealth.ApplyDamage(in DamageInfo)
                      │
                      ├─ 未致死 ─→ ActionRequestTarget.RequestAction(ActionSlot.Reaction)   ← 既有鏈路原樣
                      └─ 致死   ─→ 寫 SurvivabilityData.IsDead = true（不送 Reaction）
                                     → DeathState.CanEnter 為真 → FSM 以最高優先權接管
```

**為什麼不是「sink 同時呼叫 ApplyDamage 與 RequestAction」**：那會讓「我被打到了」有兩個真相來源，
致死那一幀必然出現「受擊動畫先播、死亡再蓋上去」的競爭。
本專案 review protocol 的加重項之一正是**決策擁有權**與**下游重新推導已 commit 的狀態**。
⇒ **三個 sink 改成只呼叫 `ApplyDamage`；`RequestAction(Reaction)` 由 Health 發出。**

#### 黑板欄位與單一寫入者

| 欄位 | 唯一寫入者 | 讀者 |
|---|---|---|
| `Survivability` | **`CharacterHealth.cs`** | `DeathState.CanEnter`、`AIInputSource.WantsToAttack`、（未來）HUD |

⇒ 需在 `ArchitectureRegressionTests.WriterRules` 加一條，並更新 dev-spec §1.1 權限表。

### 3.2 W3 — 中斷授權

**普通受傷**：零新程式（F2）。
**死亡**：唯一的真實缺口——`Interruptible: false` 的 Action 目前會**連 Death 一起擋掉**。

`ActionState.CanBeInterruptedBy` 現況：

```csharp
if (!base.CanBeInterruptedBy(other)) return false;              // config 的狀態層授權
return _phase == ActionPhase.None || _currentEntry.Interruptible; // 資產的 Action 層授權
```

最小修法（一個概念，講一次，不是散落的 `if`）：

> **資產的 `Interruptible` 只管「普通中斷」；`config` 的 `CanBeInterruptedBy` 才是狀態層授權。
> Death 屬於 lethal 層，只受 config 管，不受資產的 `Interruptible` 否決。**

```csharp
if (!base.CanBeInterruptedBy(other)) return false;
if (other.Type == StateType.Death) return true;   // lethal 授權：只由 config 決定
return _phase == ActionPhase.None || _currentEntry.Interruptible;
```

⇒ **中斷權限仍然 100% authored**（config 的 `CanBeInterruptedBy` 清單 ＋ 資產的 `Interruptible`），
程式裡只多一句「死亡不吃資產的否決權」。要加第三層（例如「霸體但可被處決打斷」）時，
擴充的是 config 清單，不是這裡。

### 3.3 W1 — Dynamic Collider

```
Presentation/Motion/
  CapsuleOffsetPolicySO.cs    ScriptableObject：啟用開關／最大位移／平滑時間／各情境權重
  （實作寄生在 MotionDriver，沿用既有的 center-shift 通道，不新增第二個 capsule 寫入者）
```

#### ⭐ 先把「兩個不同的穿模」分開

| 症狀 | 成因 | 該用哪個驅動 |
|---|---|---|
| **移動中**身體超出膠囊 | 膠囊固定在 root 原點，模型重心隨移動前傾 | **移動方向**（runtime，連續量） |
| **大幅度動畫**（攻擊前撲、Roll、翻越）超出膠囊 | 那段位移與姿勢是**動畫自己的**，與 locomotion 移動方向無關 | **per-animation bake 曲線**（＝既有的 `TraversalCollisionProfile`，只是目前沒填） |

**⇒ 不要用一個機制解兩件事。** 使用者說「不要假設永遠朝 forward」是對的——
而且比那更強：**Action／Traversal 期間 `MoveDirection` 根本不是位移真相**（§1.4）。

#### 驅動選擇（✅ D1 已裁決，見 §5）

**語意**：collider 跟隨**當下真正具有位移 authority 的 motion source**。
v1 只實作 locomotion 這一種 source，其餘（Roll／Action／Traversal 的 committed motion）**權重 0**。

⚠️ **不要為了「未來還有別的 motion source」先建 abstraction**——
第二個 source（Stage D 的 per-animation bake 曲線）出現時再談形狀。v1 是一個 `if`＋一個求解，不是一個介面。

locomotion source 的求解：**`MoveDirection`（世界）投影到 Root local**：

```
localDir = transform.InverseTransformDirection(data.MoveDirection);   // strafe／後退自然得到側向／後向
offset   = clamp(localDir * k * normalizedSpeed, maxOffset);
center   = baseCenter + SmoothDamp(offset);
```

**不是 facing**，理由：facing 在 strafe／後退時與實際佔用空間脫節，
用 facing 會在「面向敵人往後退」時把膠囊往**錯的方向**推。

#### 🔴 副作用清單（必須在實作前逐條處理，不是實作後再說）

| 情境 | 風險 | 對策 |
|---|---|---|
| **牆面** | `CharacterController.center` 改變**不做 sweep**——它會把膠囊直接搬進幾何體內，下一次 `Move` 才暴力彈出 | 每幀位移量上限 ＋ `SmoothDamp`；貼牆時（上一幀 `CollisionFlags.Sides`）**收斂回 0** |
| **斜坡／邊緣** | 膠囊底面前移 ⇒ 站在台階邊時**失去接地** ⇒ `isGrounded` false | **只位移水平分量，`center.y` 永不改**；且 `FootIKController` 的閘門是**二值無淡入**（`FootIKController.cs:140`），一次誤判就是可見的腳部彈跳 |
| **轉向** | Root 旋轉時 local center 固定 ⇒ 膠囊繞 root 掃過弧線，可能掃進牆裡 | 以 local 存放 ＋ 轉向角速度大時降低權重 |
| **停止／後退／strafe** | 速度趨零時 offset 若不歸零 ⇒ 站著卻偏一邊 | 權重 ∝ `normalizedSpeed`，停止即歸零 |
| **跳躍** | 空中沒有 locomotion 位移真相 | `!IsGrounded` ⇒ 權重 0 |
| ⭐ **Traversal** | `TraversalProbe` **逐幀讀** `center`／`radius`／`height` 做 ledge 偵測與 `EntryCapsuleWallClearance` ⇒ 動態 center 會**直接改變起攀合法性**（`docs/24` §19 C1 已經在講這條帶太寬） | **traversal committed 期間強制權重 0**；且 `MotionDriver` 的 traversal profile 與本機制**不得同時寫 center**（同一個欄位、兩個寫入者） |
| **Action** | committed 位移沿 facing，`MoveDirection` 可能仍是舊值 | Action 期間權重 0，改由 bake 曲線（Stage 3 才做） |

⇒ 實質上 **v1 只在「地面 ＋ locomotion ＋ 非 committed」時作用**。這正是「可配置、可停用、可測試」的具體形狀。

---

## 4. Stage 規劃（四段，每段都有可驗收的閉環）

> 刻意不拆成大量小 ticket。每一段結束時都應該有**畫面上看得到的差異**或**一條紅轉綠的測試**。

### Stage A — Survivability 骨架（W2 的一半）

- `DamageInfo`／`SurvivabilityData`／`SurvivabilityProfileSO`／`CharacterHealth`。
- 三個 sink 改呼叫 `ApplyDamage`；`RequestAction(Reaction)` 移進 `CharacterHealth`。
- `WriterRules` 加 `Survivability`；dev-spec §1.1 權限表同步。
- **驗收（EditMode）**：扣血、致死、非致死送出 Reaction、致死**不**送 Reaction、重複命中只結算一次。
- **此時還沒有 Death state** —— 死亡只是一個布林值，行為上還看不出來。

### Stage B — Death state ＋ 中斷授權（W2 另一半 ＋ W3）

- `StateType.Death`（值 7）＋ `DeathState`（`CanEnter = IsDead`；`CanTransitionAway = false`；
  `ValidTransitions` 空；`Priority` 最高）。
- 兩份 config 資產加 Death 規則，並把 Death 加進其他狀態的 `CanBeInterruptedBy`。
- `ActionState.CanBeInterruptedBy` 加 lethal 授權那一句（§3.2）。
- `AIInputSource.WantsToAttack` ＋ 玩家輸入在 `IsDead` 時停止產生 intent。
- **改資產**：`EnemyPunchDefinition.Interruptible: 0 → 1`（F3）。
- **驗收**：
  - EditMode：Action 中收到致死傷害 ⇒ 下一次 Tick 進 Death；`Interruptible: false` 的 Action **仍然**被 Death 打斷但**不被** Reaction 打斷；Death 之後任何 `CanEnter` 都進不去。
  - PlayMode／Play：敵人攻擊中被打 ⇒ 攻擊中斷、播受擊；致死 ⇒ 倒地且不再行動。

### Stage C — Dynamic collider v1（W1，只做 locomotion 情境）

- `CapsuleOffsetPolicySO`（預設**停用**）＋ `MotionDriver` 內的 center-shift 求解。
- 全部 §3.3 的 gating（grounded／速度／貼牆／轉向／traversal／Action）。
- **驗收**：
  - EditMode：純函式測 offset 求解（各情境權重、上限夾持、停止歸零）。
  - Editor（不進 Play）：以 `unity cmd eval_file` 在真實場景上量「模型 bounds 超出膠囊的量」修正前後對照——
    **這是本輪唯一能證明「畫面上真的不一樣」的數字**（`docs/24` §19 的教訓：數字變好 ≠ 看得到）。
  - Play：前進／後退／strafe／貼牆／上下坡／停止，確認沒有卡住或抖動。

### Stage D — per-animation capsule（W1 的另一半，**可延後**）

- 把既有的 `TraversalCollisionProfile` 通道從 traversal 推廣到一般 `MotionBakeData`，
  由 bake 期量測模型 bounds 產生 center 曲線。
- **⚠️ 這段與 traversal 共用同一個欄位** ⇒ 在 traversal 解除暫停之前，**建議不做**。

---

## 5. 使用者裁決（2026-09-14，D1–D3 已決）

### D1 — Collider 驅動來源：**當下真正具有位移 authority 的 motion source**

> 使用者原話收斂：「Collider 跟隨目前真正具有位移 authority 的 motion source」為準，
> **不要把「永遠使用 `MoveDirection`」寫死成長期規則。**

| | 內容 |
|---|---|
| **v1 支援範圍** | 只支援**普通 locomotion**：世界 `MoveDirection` → Root local → 依 local X/Z 決定 offset |
| **⛔ 不用 `transform.forward`** | strafe／後退／斜移時會偏到 facing 而非實際移動側 |
| **v1 明確不支援（必須記錄，不得為此先建 abstraction）** | `Roll`／`Action`／`Traversal` 等 **committed motion** 的位移真相是 baked curve／action motion，**不是當幀 `MoveDirection`** ⇒ 這些狀態下權重強制 0 |
| **actual displacement 的定位** | ⭐ **只能當診斷／fallback 比較，不得當第一優先來源**——撞牆時實際位移趨近 0，會讓 collider **在最需要覆蓋移動側時失去方向** |

#### 必須提供的最小 Debug Visualization

| 要看到 | 說明 |
|---|---|
| Facing direction | Root `transform.forward` |
| MoveDirection | 黑板的世界移動方向 |
| Collider **target** offset direction | 求解出來、尚未平滑的目標方向 |
| Collider **current** offset | 實際套到 `center` 上的值 |

#### 驗收情境（至少這七項）

`forward` ／ `backward` ／ `left`＋`right` strafe ／ `diagonal` ／
**敵人持續面向玩家但側移／後退／繞圈** ／ 停止後回 neutral ／
**貼牆移動時不因 offset 造成更嚴重的卡牆或穿模**。

### D2 — 玩家攻擊可否被受擊打斷：**這輪不動，維持現狀**

⇒ 本輪**只改** `EnemyPunchDefinition.Interruptible: 0 → 1`，
**玩家側的既有 `Interruptible` 值一個都不碰**。`docs/20` §2.7 的政策問題繼續掛著。

### D3 — Survivability：**黑板欄位 ＋ `CharacterHealth` 單一寫入者**

> 使用者原話：「Health／Dead 屬於多個 runtime 系統需要共享且必須一致的角色真相」，
> 不採每個 reader 各自注入 `CharacterHealth` reference 的方式。
> **scope 只涵蓋目前需要的 health／death shared state，不順便擴張成完整 Damage Framework。**

#### 第一版最小欄位（就是全部）

```csharp
public struct SurvivabilityData
{
    public float CurrentHealth;   // 目前生命值
    public float MaxHealth;       // 上限；沒有它 CurrentHealth 無法被任何讀者解讀
    public bool  IsDead;          // 已 commit 的死亡判定
}
```

- ⛔ **不放**：hit direction、damage source、invulnerability、death cause、hit stun 剩餘時間、
  受擊次數統計——全部是**尚未裁決**的資訊，第二個需求出現前不進黑板。
- ⭐ **`IsDead` 刻意不是讓讀者自己算 `CurrentHealth <= 0`**：那正是本 repo review protocol 的加重項
  「**下游重新推導已 commit 的狀態**」。死亡是 `CharacterHealth` 做出並 commit 的**決定**，
  讀者只消費結果。
- 讀者：`DeathState.CanEnter`／`ActionState`（lethal 授權判斷不需要，靠 StateType 即可）／
  `AIInputSource.WantsToAttack`／`PlayerInputSource`（死後停止產生 intent）。
  **全部只讀同一份**，`CharacterHealth.cs` 為唯一寫入者。

## 6. ADR 判準檢查

| 判準 | 命中？ |
|---|---|
| ① 黑板 schema ／ ownership 變更 | ✅ 新增 `Survivability` 欄位與唯一寫入者（若 D3 選 (a)） |
| ② FSM 拓撲變更 | ✅ 新增 `StateType.Death` |
| ③ 管線順序／核心驅動介面變更 | ❌ |
| ④ 推翻既有架構不變量 | ❌（`Interruptible` 的語意是**收窄**，不是推翻） |

⇒ **W2＋W3 需要一份 ADR（Trial）**，涵蓋 Death state 與 lethal 中斷授權兩件事——
依 anti-explosion 規則**合併成一份，不開兩份**。
⇒ **W1 不開 ADR**：它擴充既有的 center-shift 通道，不動黑板 ownership、不動 FSM 拓撲、不動管線順序
⇒ 依 routing rule 直接寫 Living Docs（dev-spec §3.2 MotionDriver ＋ 本檔）。

---

## 7. 實作狀態（2026-09-14 fold back）

> 依 Code/Documentation Fold-back 規則：以下是**磁碟實際狀態**，不是計畫。
> ⚠️ 三個 assembly 都 `dotnet build` **0 errors**，但 **EditMode 測試尚未跑過**——
> live Editor 在本輪後段主執行緒無回應（`recompile`／`recompile_status`／`console` 連續 timeout），
> 因此測試與**所有資產改動**都留在 Integration Gate。**不得據此宣稱任何 Acceptance Criteria 已通過。**

### 7.1 已落地（程式）

| Stage | 檔案 | 內容 |
|---|---|---|
| **A** | `Core/Blackboard/SurvivabilityData.cs` | `CurrentHealth`／`MaxHealth`／`IsDead`，就這三個 |
| **A** | `Core/Blackboard/PlayerRuntimeData.cs` | 新增 `Survivability` 欄位（不參與 `ResetTransientState`） |
| **A** | `Core/Survivability/CharacterHealth.cs` | 唯一寫入者；`ApplyDamage` ／ `PublishTo`；**惰性初始化**（EditMode 不呼叫 `Awake`） |
| **A** | `Core/Pipeline/CharacterPipelineRunner.cs` | 順序 **0.5** `_characterHealth?.PublishTo(_runtimeData)` |
| **A** | `Presentation/Actions/{MeleeHitboxSink, ThrownProjectile, GroundEffectSink}.cs` | 改為只送 `ApplyDamage(damage)`；各自新增 `[SerializeField] damage = 10` |
| **B** | `Core/StateMachine/StateType.cs` | `Death = 7`（加在最後，不遞補既有數值） |
| **B** | `Core/StateMachine/States/DeathState.cs` | 吸收態三層保證 |
| **B** | `Core/StateMachine/FullBodyStateMachine.cs` | 註冊 `DeathState`（無建構參數） |
| **B** | `Core/StateMachine/States/ActionState.cs` | lethal 中斷授權一句（ADR-009 D4） |
| **B** | `Core/Arbitration/Sources/DeathArbiterSource.cs` | 死亡 ⇒ `BlockInput`（`A27` 逼出來的唯一解） |
| **C** | `Presentation/Motion/CapsuleOffsetPolicy.cs` | `CapsuleOffsetSettings` ＋ 純函式 `CapsuleOffsetSolver` |
| **C** | `Presentation/Motion/MotionDriver.cs` | `UpdateCapsuleOffset` ＋ 三道 guard ＋ `BeginTraversalCollisionProfile` 的 reset ＋ 四向量 Gizmo |
| 測試 | `_Project/Tests/EditMode/SurvivabilityTests.cs` | `H1`–`H9`（AC1–AC4） |
| 測試 | `_Project/Tests/EditMode/CapsuleOffsetTests.cs` | `C1`–`C9`（求解層） |
| 測試 | `ArchitectureRegressionTests.cs` | `WriterRules` ＋ 兩層白名單 ＋ `A13'` ＋ `A26` |
| 測試 | `ActionStateTests`／`SlowEffectTests` | 三條改讀新 seam（`T14`／`T24`／GroundEffect） |

### 7.2 ⛔ 尚未做，且**必須**在 Unity Editor 做

見 `WORKLOG.md` 的接線清單。重點：**沒有這些資產改動，Stage B 在遊戲裡完全不會發生**——
`DeathState` 沒有 config 規則就永遠進不去，這正是 `docs/24` §14.2 記過的
「有程式、有測試、但遊戲裡從來沒跑過」。

### 7.3 已知邊界（不是缺陷，是已裁決的範圍）

- Collider 偏移**預設停用**，且 v1 只在「地面 ＋ locomotion ＋ 非 committed ＋ 未貼牆」時作用。
- 玩家攻擊是否可被受擊打斷：**本輪不動**（D2），`docs/20` §2.7 的政策問題繼續掛著。
- ~~沒有復活流程。`DeathState` 是吸收態，`H7` 明確守住「把 `IsDead` 改回 false 也出不去」。~~
  **🔄 2026-09-14 已取代 —— 見下方 §8。**

---

## 8. Respawn（2026-09-14 使用者裁決）

> **狀態：程式、接線、測試皆已完成** —— EditMode **500／499 passed／0 failed／1 skipped**、
> PlayMode **45／45**、live Editor recompile 0 errors。
> ⛔ **人類 Play 驗收尚未做**：站得起來嗎、傳送的位置對嗎、重生後還能正常打嗎。
>
> **接線現況**：`X Bot.prefab` 根物件掛 `RespawnController`，`RespawnAction` 綁 **`<Keyboard>/r`**，
> `health`／`motionDriver` 顯式指派，`respawnPoint` **留空**（＝退回場景開場姿態）。
> 由 `PrefabWiringTests.W23` 守住這三件事都不得缺。

### 8.1 ⭐ 核心：authority ／ orchestration 分離

這是本節唯一真正的架構內容，其餘都是它的後果。

| 角色 | 誰 | 負責 | **不**負責 |
|---|---|---|---|
| **Survivability authority** | `CharacterHealth.Revive()` | 把 `IsDead` commit 回 false、血量回滿 | 重生點、傳送、速度歸零、FSM |
| **Position authority** | `MotionDriver.Teleport()` | 實際搬動角色 ＋ 清掉會跟著跑的跨幀狀態 | 什麼時候該搬、搬去哪 |
| **Orchestration** | `RespawnController` | **順序**：先整理世界、再宣告活著 | 任何真相（它自己零狀態） |

⛔ **`RespawnController` 不得自己寫 `transform.position`。** `MotionDriver` 是角色 position 的
單一寫入者（全專案沒有任何地方直接寫 `transform.position`，位移一律走 `characterController.Move`）
——繞過它等於當場多出第二個寫入者。

### 8.2 FSM 出口：只開對應「死亡」的那一層鎖

`DeathState` 原本的三層保證裡，**只有第 ② 層改了**：

| 層 | 舊 | 新 |
|---|---|---|
| ① 進得去（`CanEnter` ＋ Priority） | `IsDead` | **一字未動** |
| ② 出不來（自然過渡 `CanTransitionAway`） | 恆 `false` | **`!IsDead`** |
| ③ 出不來（被中斷 `CanBeInterruptedBy`） | 空 | **一字未動，仍然是空的** |

**不變量的原意完整保留**：跳躍、翻滾、出手、受擊、external request——全都**仍然**拉不出 Death。
能把角色救出來的只有 `CharacterHealth.Revive()`。
⇒ 這不是「把門打開」，是讓**門的條件與進門的條件對稱**：
`CanEnter => IsDead` ／ `CanTransitionAway => !IsDead`。

**為什麼離開走「自然過渡」而不是「中斷」**：中斷的語意是「別的狀態比我更該發生、把我打斷」。
沒有任何狀態「打斷」死亡——是死亡這個**前提本身**消失了。所以第 ③ 層完全沒被碰過。

**出口只到 Idle／Move**（config 資產的 `ValidTransitions`）。⛔ 不直接允許 Death → Action／Jump／Roll：
那會讓「重生瞬間就能出手」變成沒人設計過的能力，而且**它看起來像 bug**
——按著攻擊鍵等重生，角色一活過來就揮一拳。

**實作細節**：`CanTransitionAway` 是無參數屬性（`BaseState` 的既有形狀，六個狀態共用），拿不到黑板
⇒ 旗標在 `DeathState.OnTick` 內從已 commit 的 `IsDead` 抄一次。這與 `HurtState._isFinished`
**是同一個既有慣例**，因此**不需要**為了取得 `data` 去改 `BaseState` 的介面形狀。
時序安全：`Tick` 的順序是 `OnTick` → `EvaluateInterrupts` → `EvaluateTransitions`，同一幀生效。

### 8.3 🔴 為什麼重生鍵**不能**走 `InputData`

`BlockInput` 的語意是「本幀管線看不到任何輸入」——`CharacterPipelineRunner` 在順序 2 的閘門
直接把整份 `InputData` 歸零（dev-spec §7-M5 已裁決的簡化）。
而 `DeathArbiterSource` 正是在 `IsDead` 時抬起 `BlockInput`
⇒ **死著的時候任何 gameplay 輸入都收不到，包括重生鍵。**

⇒ `RespawnController` 比照 `GamePauseController`：**自己持有 `InputAction`、自己在 `Update` 讀**。
重生與暫停是同一類東西——**system-level 指令，不是角色的動作**。
`Respawn()` 開成 public，未來的 UI 按鈕可直接以 Inspector 引用綁定（不需要 Singleton）。

### 8.4 順序：先傳送、再救活

反過來做會壞，而且是只在特定一幀才看得出來的壞：先 `Revive()` 再傳送，角色會有至少一幀是
**「活著、站在屍體原地、輸入已解封」**——那一幀足以讓玩家按出一個動作、或讓敵人打中他，
而他其實應該已經在重生點了。

### 8.5 `Teleport` 順帶清掉的跨幀狀態

都是「傳送後還留著就會立刻出事」的那種：

| 清什麼 | 不清會怎樣 |
|---|---|
| 垂直速度 | 墜落致死時它是很大的負值 ⇒ 重生第一幀就以墜落速度往下衝，看起來像「重生點在地板下面」 |
| 動態膠囊偏移 | 偏移是相對 authored 基準的平滑值，換地點後沒有意義 |
| traversal collision profile | 死在翻越途中時膠囊還套著 traversal 尺寸 ⇒ 帶著錯尺寸的膠囊重生 |
| 殘留 facing request | 傳送後立刻被轉回死前的方向 |

⛔ **刻意不碰**：`_wasGrounded`（它是傳送**前**的接地真相，下一幀的邊沿偵測因此仍回答
「我是不是到地上了」＝`JustLanded` 的語意；硬清成 false 反而會在原地傳送時偽造一次落地）、
以及 `MoveSpeed`／平滑器（那是 `IMovementModel` 的內部狀態，為了重生去動**核心驅動介面**並不划算
——死亡期間輸入已被歸零，順序 3 每幀無條件推進的 B9 減速本來就會把它收到 0）。

### 8.6 ⛔ 本輪明確不做（使用者裁決）

- **不新增 `RespawnState`。** 等到重生真的需要一段**有持續時間的 gameplay phase**
  （起身動畫、無敵窗、不可操作期）時再考慮升格。現在升格只會多一列 State Matrix
  與兩份 config 要 author，而沒有任何動畫可放。
- **不建 checkpoint 系統。** 只有一個 `respawnPoint`（留空 ⇒ 退回**場景開場**位置。
  ⚠️ 退路刻意不是「當下位置」——那會讓重生點跟著角色漂移，等於沒有重生點）。
- **不做 encounter reset**（敵人不回血、不歸位）。那是未來獨立的 **Restart Encounter** 功能，
  scene／character re-instantiation 保留給它，⛔ 不得拿來取代正常的 checkpoint respawn。
- **不清除暫時效果**（死前中的 Slow 會帶到重生後直到過期）。已知邊界，第一版接受。
