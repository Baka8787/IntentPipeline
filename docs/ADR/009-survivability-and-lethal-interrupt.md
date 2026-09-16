# ADR-009：Survivability 黑板欄位、`StateType.Death`、與 Lethal 中斷授權

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-14 使用者裁決 D3）。**AC1–AC4 ＋ D2 已由自動測試驗證通過**（EditMode 471／470、PlayMode 45／45）；**AC5／AC6 待人類 Play 驗收** ⇒ 依 CLAUDE.md「Accepted ＝ 已實作 ＋ Play ＋ Test 驗證通過」，**尚不得升為 Accepted**。 |
| 後續修訂 | **`docs/26`（Model B）** 把受擊由 `ActionSlot.Reaction` 遷出為獨立的 `StateType.Hurt`。本 ADR 的 D1／D2（Survivability 單一寫入者、後果由 `CharacterHealth` 獨佔決定）**完全不受影響**，只是「未致死」那一支改為發布 `JustTookDamage` 而不是送 `Reaction`。D4 的 lethal 授權那一句仍然保留（見 §4.2）。 |
| 日期 | 2026-09-14 |
| 範圍 | `PlayerRuntimeData.Survivability` 欄位與其唯一寫入者、`StateType.Death` 與 `DeathState`、`ActionState` 的 lethal 中斷授權 |
| 不含 | Damage Framework（傷害類型／抗性／DoT）、invulnerability／i-frame、hit direction／knockback、死亡後的 respawn／loot／UI、玩家攻擊可否被受擊打斷的政策（`docs/20` §2.7，使用者 2026-09-14 明確裁決「這輪不動」） |
| Living Spec | `docs/25-survivability-and-dynamic-collider.md` |
| 關聯 | ADR-004（Action in FSM，Accepted）、ADR-005（Action Identity，Accepted）、`docs/20` §2.6／§2.7 |

---

## 1. Problem

repo 裡**已經有受擊反應**（`ActionSlot.Reaction` ＋ `DamageDefinition.asset`，由三個 sink 經
`ActionRequestTarget` 投遞），但**完全沒有生命值**：命中 seam `RequestAction(slot)` **只有身分、沒有量值**，
因此「被打」與「被打死」在整條鏈路上不可區分。三個後果：

1. 沒有任何角色會死；`StateType` 也沒有可以承載「死了之後不能再行動」的吸收態。
2. 攻擊方 sink 目前**自己決定**被打者要播受擊（直接呼叫 `RequestAction(Reaction)`）。
   一旦加入致死判定，這會變成兩個真相來源：致死那一幀必然出現「受擊動畫先播、死亡再蓋上去」的競爭。
3. `ActionPhaseEntry.Interruptible` 目前是**單一層級**的否決權。把它設為 `false`
   （現況：`EnemyPunchDefinition`）會同時擋掉普通受擊**與未來的死亡**——
   「霸體」不該等於「打不死」。

## 2. Decision

### D1 — `Survivability` 是黑板欄位，`CharacterHealth` 是唯一寫入者

```csharp
public struct SurvivabilityData
{
    public float CurrentHealth;
    public float MaxHealth;
    public bool  IsDead;
}
```

- 掛在角色 **Root** 的 `CharacterHealth`（玩家與敵人**同一顆元件**）是 `Survivability` 的唯一寫入者，
  登記進 `ArchitectureRegressionTests.WriterRules` 並同步 dev-spec §1.1 權限表。
- 讀者：`DeathState.CanEnter`、`AIInputSource`、`PlayerInputSource`（死後停止產生 intent）。
- **`IsDead` 是 commit 的決定，不是讓讀者算 `CurrentHealth <= 0`。**
  下游重新推導已 commit 的狀態是本 repo review protocol 的加重項。

**為什麼是黑板而不是注入 seam**（使用者裁決）：health／dead 是「多個 runtime 系統需要共享且必須一致的
角色真相」。`TraversalProbe` 那種注入 seam 適合**單一讀者**（只有 `TraversalState` 要它）；
`Survivability` 有三個以上讀者，注入會讓真相分散、且權限無法被 `A5` 機器化守住。

### D2 — **「被打之後會發生什麼」由 `CharacterHealth` 獨佔決定**

```
攻擊方 sink ──→ CharacterHealth.ApplyDamage(in DamageInfo)
                      ├─ 未致死 ─→ ActionRequestTarget.RequestAction(ActionSlot.Reaction)
                      └─ 致死   ─→ 寫 IsDead = true，**不送 Reaction**
```

三個既有 sink（`MeleeHitboxSink`／`ThrownProjectile`／`GroundEffectSink`）
**不再直接呼叫 `RequestAction(Reaction)`**。

**為什麼**：攻擊方知道「我打中了、打多少」，**不知道**「這一下會不會致死」。
讓 sink 同時送傷害與受擊請求 ＝ 讓不掌握資訊的一方做決定 ＝ 致死幀的動畫競爭。

### D3 — `StateType.Death` 是吸收態，授權來自 config 資產

- `StateType.Death = 7`（**不遞補既有數值**）。
- `DeathState`：`CanEnter = data.Survivability.IsDead`；~~`CanTransitionAway = false`~~；
  ~~`ValidTransitions` 空~~；`Priority` 高於所有既有狀態。（刪除線處見 **D3-R1**）
- 兩份 `StateMachineConfig` 資產新增 Death 規則，並把 Death 加進其他狀態的 `CanBeInterruptedBy` 清單。
  ⇒ **中斷授權仍然 100% authored，不寫死在程式裡。**

#### 🔄 D3-R1 — 修訂（2026-09-14，使用者裁決；respawn）

> **依 CLAUDE.md ADR Lifecycle：本 ADR 仍是 `Trial`，`Trial` 的 decision content 不凍結，
> 可依實作發現修訂並記入修訂紀錄，不必開新 ADR。**
> ⚠️ 本 ADR §7 曾把「D3 的吸收態語意」列入凍結清單——使用者 2026-09-14 明確裁決走**修訂**路線
> （而非開 ADR-010 supersede），理由是 anti-explosion 規則 ＋ Trial 尚未凍結。
>
> **狀態：程式與接線已落地；EditMode 500／499 passed／0 failed／1 skipped、PlayMode 45／45。
> ⛔ 但 AC5／AC6 仍待人類 Play 驗收 —— 本 ADR 依舊是 `Trial`，不得升為 `Accepted`。**

**改什麼**：`CanTransitionAway` 由恆 `false` 改為 **`!IsDead`**；config 的 `ValidTransitions`
由空改為 **`[Idle, Move]`**（兩份資產皆是）。

**不改什麼**（同樣重要）：`CanEnter` 與 `Priority` 一字未動；
**`CanBeInterruptedBy` 仍然留空**——沒有任何狀態「打斷」死亡，是死亡這個**前提本身**消失了，
所以離開走的是自然過渡，三層鎖裡的中斷那一層完全沒被碰過。

**為什麼這不是推翻不變量，而是讓它對稱**：
原本的語意是「進得去、出不來」，而「出不來」其實一直是「**死著就出不來**」的簡寫
——當時沒有任何東西能讓 `IsDead` 變回 false，所以兩者等價。
現在 `CharacterHealth.Revive()` 存在了，兩者才分家。
修訂後是 `CanEnter => IsDead` ／ `CanTransitionAway => !IsDead`，**進門與出門問同一個已 commit 的事實**。
跳躍、翻滾、出手、受擊、external request 仍然全都拉不出 Death。

**權威歸屬（使用者裁決的 authority ／ orchestration 分離）**：
`CharacterHealth.Revive()` 是唯一能把 `IsDead` commit 回 false 的 API；
FSM **不被通知**，它只是照常讀黑板。傳送／速度歸零／collider 還原由薄的
`RespawnController` 在呼叫 `Revive()` **之前**完成。完整規格：`docs/25` §8。

**revert 路徑**：把 `CanTransitionAway` 改回 `false`、兩份 config 的 Death `ValidTransitions`
清空，並移除 `RespawnController`／`Revive()`／`MotionDriver.Teleport`。
`HD7`／`HD9`／`HD10`／`A38` 的對應斷言一起回退。

### D4 — Lethal 中斷授權：`Interruptible` 只管**普通**中斷

`ActionState.CanBeInterruptedBy` 的規則收窄為：

> **資產的 `Interruptible` 是「普通中斷」的否決權；`config` 的 `CanBeInterruptedBy` 才是狀態層授權。
> Death 屬於 lethal 層，只受 config 管，不受資產的 `Interruptible` 否決。**

```csharp
if (!base.CanBeInterruptedBy(other)) return false;   // config（authored）
if (other.Type == StateType.Death) return true;      // lethal：不吃資產的否決權
return _phase == ActionPhase.None || _currentEntry.Interruptible;
```

**這不是推翻既有不變量，是收窄語意**：`Interruptible` 原本從未被要求涵蓋死亡（當時沒有死亡）。
要新增第三層授權（例如「霸體但可被處決打斷」）時，擴充的是 **config 的 `CanBeInterruptedBy` 清單**，
不是這段程式。

### D5 — 「這個 Action 不會被普通 Hurt 打斷」的定義位置：**`ActionPhaseEntry.Interruptible`**

**不新增欄位、不新增型別。** Super Armor／Uninterruptible Action／Boss committed action
全部設該 phase 的 `Interruptible = false`。兩條中斷路徑都已讀它：

| 中斷來源 | 路徑 |
|---|---|
| 別的 StateType | `EvaluateInterrupts` → `CanBeInterruptedBy`（D4 後 Death 除外） |
| 另一個 Action（含 `Reaction`） | `EvaluateInterrupts` → `CanReenter` |

## 3. Alternatives（否決）

| 方案 | 為什麼否決 |
|---|---|
| 獨立的 Health State Machine | 與 `FullBodyStateMachine` 形成第二個狀態權威；死亡與 locomotion／action 的互斥必須由**同一個** FSM 仲裁，否則「死了還在播攻擊」只能靠紀律 |
| 獨立的 Interrupt System | 中斷仲裁已經在 `EvaluateInterrupts` ＋ config 的 priority／`CanBeInterruptedBy`。再造一層會出現兩個中斷權威 |
| 新增 `ActionDefinitionSO.CanBeInterruptedByHurt` 布林 | 與既有 `Interruptible` 語意重疊 ⇒ 兩個欄位描述同一件事，必然漂移 |
| `BaseState.InterruptAuthority` 數值分級 | 為了一個目前只有兩級（normal／lethal）的需求引入通用分級制；第二個非致死高權中斷出現前不建 |
| Hurt 做成獨立 `StateType` | 受擊反應在本 repo 已經是 Action（`ActionSlot.Reaction`），另立 StateType 會讓同一件事有兩種表示 |
| sink 同時呼叫 `ApplyDamage` ＋ `RequestAction` | 見 D2：致死幀的動畫競爭 |

## 4. Impact

| 模組 | 變更 |
|---|---|
| `Core/Blackboard` | 新增 `SurvivabilityData` 與 `PlayerRuntimeData.Survivability` |
| `Core/Survivability`（新） | `CharacterHealth` |
| `Core/Arbitration/Sources` | 新增 `DeathArbiterSource`（死亡 ⇒ `BlockInput`） |
| `Core/StateMachine` | `StateType.Death`；`States/DeathState`；`ActionState.CanBeInterruptedBy` 一句 |
| `Presentation/Actions` | 三個 sink 改呼叫 `ApplyDamage` |
| `Core/Pipeline` | 順序 0.5 `_characterHealth?.PublishTo(_runtimeData)` |
| 資產 | 兩份 `StateMachineConfig` 加 Death 規則；`EnemyPunchDefinition.Interruptible: 0 → 1` |
| 測試 | `WriterRules` 加 `Survivability`；`A13'` StateType 清單同步；`A26` 改釘 `ApplyDamage`；新增 `SurvivabilityTests` |

### 4.1 🔧 Trial 期實作修訂（2026-09-14，依 §7 允許，不開新 ADR）

| 原本規劃 | 實際落地 | 理由 |
|---|---|---|
| `DamageInfo` readonly struct | **未建立**；簽章是 `ApplyDamage(float amount)` | 單欄位 struct 是純儀式。需要 direction／source 時加多載即可，三個 sink 不必同時改 |
| `SurvivabilityProfileSO` | **未建立**；`maxHealth` 是 `CharacterHealth` 的 `[SerializeField]` | `[SerializeField]` 本來就是本 repo 的既有配置慣例（`ThrownProjectile.slowDuration`／`MotionDriver.moveSpeed`），且玩家與敵人的血量本來就不共用。少一份要接線的 `.asset` |
| 「`IsDead` 時停止產生 intent」寫在輸入層 | 改走 **`DeathArbiterSource` → `BlockInput`** | ⛔ `A27` 明文禁止 `AIInputSource` 出現 `PlayerRuntimeData`（producer 不得回讀 gameplay state）。⇒ 走仲裁層**不是選擇，是既有不變量推出來的唯一解**。`ArbiterPipeline` 檔頭本來就寫著「不知道有死亡……新增封鎖來源＝實作介面掛上階層，管線與 Runner 零改動」 |
| 傷害數值來源 | 三個 sink 各自的 `[SerializeField] damage`（預設 10） | 同上：既有慣例，數值在資產不在程式 |

## 5. Acceptance Criteria（Trial → Accepted 的通過條件）

| # | 條件 | 驗證層 |
|---|---|---|
| **AC1** | 非致死傷害 ⇒ 扣血 ＋ 送出 `Reaction`；致死傷害 ⇒ 扣血 ＋ `IsDead` ＋ **不送** `Reaction` | EditMode |
| **AC2** | `Interruptible: false` 的 Action **不被** `Reaction` 打斷、**但被** Death 打斷 | EditMode |
| **AC3** | 進入 `Death` 後，任何狀態的 `CanEnter` 都無法把它換掉（吸收態）。🔄 **D3-R1 修訂後收斂為：角色仍是 `IsDead` 時**任何狀態都換不掉它；唯一出口是 `CharacterHealth.Revive()` 把 `IsDead` commit 回 false | EditMode |
| **AC4** | `Survivability` 的唯一寫入者是 `CharacterHealth.cs`（`A5` 綠） | EditMode |
| **AC5** | 敵人攻擊中被打 ⇒ **攻擊 Action 中斷並播受擊**，不是把攻擊播完 | PlayMode ＋ 人工 Play |
| **AC6** | 致死 ⇒ 倒地，之後**不再回到 locomotion 或 attack** | 人工 Play |

## 6. 失敗處置（revert 路徑）

Trial 失敗時 **code ／ ADR ／ invariant 一起 revert**，不留 workaround：

1. `PlayerRuntimeData.Survivability` 與 `WriterRules` 的該條目一起移除。
2. `StateType.Death` 與 `DeathState` 移除；兩份 config 資產移除 Death 規則
   （**經 Unity Editor API**，不手改 YAML）。
3. `ActionState.CanBeInterruptedBy` 的 lethal 那一句移除。
4. 三個 sink 改回直接 `RequestAction(ActionSlot.Reaction)`。
5. `EnemyPunchDefinition.Interruptible` 設回 `0`。

⚠️ 這些檔案目前**都在版控裡**（與 traversal 不同——見 `docs/24` §19.1），
所以本 ADR 的 revert 是真的可執行的，不是「照文字重寫」。

## 7. 不凍結的內容（Trial 期間可依實作發現修訂，不必開新 ADR）

- `SurvivabilityData` 的**欄位組成**（例如之後要不要加 `JustDied`）。
- 血量／傷害的 placeholder 數值與它們住在哪份資產（§4.1 已用掉這條）。
- 傷害 seam 的簽章（目前是 `ApplyDamage(float)`；要帶方向就加多載）。
- `DeathState` 的 `Priority` 數值、動畫鍵、倒地後的保留秒數。
- 三個 sink 的傷害數值來源（authored 在哪份資產）。
- **凍結的只有**：D1 的單一寫入者、D2 的決策擁有權、D3 的吸收態語意（🔄 **已由 D3-R1 修訂為「死著的時候是吸收態」**——Trial 不凍結，修訂紀錄見 D3-R1）、D4 的兩層授權、D5 的定義位置。

## 8. 判準檢查（為什麼需要 ADR）

| 判準 | 命中 |
|---|---|
| ① 黑板 schema ／ ownership 變更 | ✅ `Survivability` ＋ 新的唯一寫入者 |
| ② FSM 拓撲變更 | ✅ `StateType.Death` |
| ③ 管線順序／核心驅動介面變更 | ❌ |
| ④ 推翻既有架構不變量 | ❌（`Interruptible` 是收窄，不是推翻） |

⇒ 命中 ①②，合併成**一份** ADR（anti-explosion 規則）。
動態 Collider（`docs/25` W1）**不命中任何一條**，走 routing rule 直接寫 Living Docs，不開 ADR。

---

## 4.2 🔧 Trial 期第二次修訂（2026-09-14，`docs/26` Model B ＋ 垂直切片收尾）

| 原本 | 實際落地 | 理由 |
|---|---|---|
| 未致死 ⇒ `RequestAction(ActionSlot.Reaction)` | 未致死 ⇒ **發布 `Survivability.JustTookDamage`** | 受擊已遷出為 `StateType.Hurt`（`docs/26`）。**D2 的決策擁有權一字未改**——仍然是「後果由持有生命值的一方決定」，只是出口從 mailbox 換成同一個黑板區，與 `IsDead` **對稱** |
| `CharacterHealth` 持有 `ActionRequestTarget` | **不再認識任何 Action 概念** | 同上；prefab 少一個要接的欄位 |
| `SurvivabilityData` ＝ 三個欄位 | ＋ `JustTookDamage`（單幀，publish-and-clear） | §7 明文把「欄位組成」列為不凍結 |
| `DeathState` 位移 ＝ `ExecuteBaseMovement` | **`ExecuteVerticalOnlyMovement`** | 全速奔跑中死亡會滑行：`BlockInput` 有一幀延遲、B9 減速還要時間。改用既有能力後水平速度在進入的那一幀就消失（PlayMode `PD1` 釘住） |
| D4 的 `if (other.Type == Death) return true;` | **保留** | `docs/26` §E.3 指出改用 priority 門檻後可以刪掉它，但那需要把 `Interruptible` 加寬成 int——**使用者裁決本輪不動該 schema** ⇒ 維持現狀 |

### AC 驗證狀態

| # | 條件 | 狀態 |
|---|---|---|
| AC1 | 非致死扣血＋送受擊；致死不送 | ✅ `SurvivabilityTests.H1`／`H2` |
| AC2 | `Interruptible: false` 擋 Hurt 但不擋 Death | ✅ `HurtAndDeathStateTests.HD5` |
| AC3 | Death 吸收態 | ✅ `HD7` ＋ `A38`（出貨資產） |
| AC4 | `Survivability` 單一寫入者 | ✅ `A5` |
| AC5 | 敵人攻擊中被打 ⇒ 中斷並播受擊 | 🟡 機制已驗（`HD4`／`A38`／`W18`–`W20`），**整條鏈的 Play 驗收未做** |
| AC6 | 致死 ⇒ 倒地且不再行動 | 🟡 同上（`PD1`／`HD7` 已驗位移與吸收態） |
