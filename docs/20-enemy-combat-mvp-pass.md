# 20 — Enemy Combat Presentation Minimum Viable Pass（實作切片規格）

> **這份文件是什麼**：一輪**已裁決、可直接實作**的最小修復切片。
> **這份文件不是什麼**：⛔ 不是 ADR、⛔ 不是敵人 AI 設計、⛔ 不是 roadmap 修訂。
> **這輪的目標不是完成敵人戰鬥 AI**，而是把現在「連基本表現都很糟」的三件事修到可接受。
> 建立日期：2026-09-11。程式對照基準：`71f5a5c` ＋ 當日工作樹。
> 上游：`docs/19-enemy-combat-facing-slice.md`（本檔修正它的一處錯誤選擇，見 §4-C4）。

---

## 0. 使用者裁決（2026-09-11）

| # | 裁決 |
|---|---|
| **E1** | `aggroEnterRadius = 8`、`aggroLeaveRadius = 12` 為**第一版測試值**，可調整，**不視為正式平衡定案** |
| **E2** | **未進 combat 的敵人保持 Idle**（不追、不轉、不 strafe）。⛔ 這輪不做 patrol / search state |
| **E3** | 受擊**先走 A1**（MeleeHitbox 加 kinematic Rigidbody，最小假設驗證）；**只有 A1 Play 仍失敗才進 A2**（OverlapBox） |
| **E4** | 優先序 **A 受擊 → B 面向 → C aggro/leash**；**A–C 全過後才評估** 敵人 2D locomotion |
| **E5** | ⛔ 本輪不碰：玩家 8-way、玩家 combat-facing、ADR-006／upper-lower body layering、完整 HP／死亡／血條、traversal、perception framework、patrol／search tree、新 Debug Framework、大型 FSM 重構、任何為了修表現而開的新抽象層 |

---

## 1. 三個必須在 PlayMode 明確看到的結果

1. **玩家攻擊命中敵人時，穩定觸發受擊反應動畫。**（不要求 HP／死亡／擊退／硬直）
2. **敵人在交戰時持續面向玩家**——Strafe／迂迴／攻擊時都不側身或背對。
3. **敵人的搜索／追擊不誇張**——遠處不追、進範圍才追、離開較大半徑可脫離，enter／leave 保有遲滯。

---

## 2. 只讀診斷結論（2026-09-11 磁碟核對，實作時可直接信任）

### 2.1 受擊鏈：**靜態可證的環節全部正常**

| 候選 | 結果 | 證據 |
|---|---|---|
| 程式斷點 | ❌ **排除** | `MeleeHitboxSink.OnTriggerEnter` → `GetComponentInParent<ActionRequestTarget>()` → `RequestAction(Reaction)` → `ActionState.TryResolveRequest` 讀 `_externalRequestTarget.PendingSlot` → `Config.GetActionDefinition(Reaction)` 全通 |
| Prefab 接線 | ❌ **排除** | X Bot：`hitbox: {fileID: -900000000000000003}` 已指派；`actionSinkBindings` 的 **Slot 1 → MeleeHitboxSink**（`-900000000000000004`）正確。Y Bot：`ActionRequestTarget` 與 `CharacterPipelineRunner`／`AIMovementSource`／`CharacterFacingSource` **同一 GameObject**（`fileID 7863296372082958677`） |
| Definition／Config | ❌ **排除** | `MeleeSlash1Definition`：`EmitsRelease: 1`、`ReleaseNormalizedTime: 0.4`；`DamageDefinition`（Slot 100）**已註冊**於 `EnemyStateMachineConfig.asset`。`ShouldFaceTargetOnEnter(Reaction) == false` 只影響 facing，**不擋進入** |
| **Physics layer／matrix** | ❌ **排除** | `ProjectSettings/DynamicsManager.asset` 的 `m_LayerCollisionMatrix` **全 `f`**（所有層互相碰撞）。hitbox 在 **layer 6 Player**、Y Bot 在 **layer 7 Enemy**、`m_IsTrigger: 1` |
| hitbox window | 🟡 無法靜態證明 | `_windowOpen` 由 runtime lifecycle（`Release`）開啟 |
| ~~物理時序~~ | ❌ **已推翻（2026-09-11）** | 見下方更正 |

> 🔴 **2026-09-11 更正：本表原先的「最強候選」是錯的，A1 建立在兩個錯誤事實上。**
>
> | 原本寫的 | 磁碟事實 |
> |---|---|
> | 「`MeleeHitbox` 掛在 `mixamorig:RightForeArm` 下，由動畫骨骼搬動」 | ❌ **它是角色 Root 的子物件**：`m_LocalPosition: {x: 0, y: 1, z: 1.05}`、`m_Size: {1.4, 1.5, 1.6}` ⇒ 一個**固定在身前**的攻擊盒，前緣離 Root **1.85 m**。它只隨角色 Root 移動，**不隨動畫骨骼** |
> | 「缺 Rigidbody ⇒ 移動中的 static trigger 不可靠 ⇒ 加 kinematic Rigidbody」 | ❌ 前提既然不成立，A1 就沒有立論基礎。**A1 已於 Play 驗證失敗，Rigidbody 撤回** |
>
> **錯在哪裡（方法層面）**：我從 prefab YAML 裡「下一個 GameObject 恰好是 `mixamorig:RightForeArm`」**推斷**父子關係，
> 而沒有實際查 `m_Father`／`m_LocalPosition`。**相鄰不等於從屬**——YAML 的區塊順序不是階層。
>
> **真正的根因見 §2.6**，與物理完全無關。

> 📌 **另一個獨立事實**：全 repo **沒有任何 HP／傷害數值系統**
> （`grep -rln "class Health|TakeDamage|ApplyDamage|currentHealth|hitPoints" Assets/Scripts/` 零命中）。
> 「受傷」目前**只有動畫意義**。依 E5，這輪**不補**。

### 2.2 面向鏈：`usePersistentCombatFacing` 從未被序列化

`grep -rln "usePersistentCombatFacing" Assets/` 只命中 `CharacterFacingSource.cs` 與 `LocomotionMixerWiringTests.cs`
—— **沒有任何 prefab 或 scene 寫過這個 key**。Y Bot 的 `CharacterFacingSource` 區塊只有 `facingAngleDeadzone: 8`。
新增欄位在既有序列化資料中缺席 ⇒ Unity 套用 C# 初始式 `= false`。

⇒ `hasCombatFacing` 恆 `false` ⇒ **Priority 2 永遠不命中** ⇒ facing 落到 Priority 3（`MoveDirection`）⇒ 側身／背對。

**消費端已排除嫌疑**：`MotionDriver` 是 `transform.rotation` 唯一寫入者（A31／A32 守），
且 `ApplyFacingRequest()` 在**四條**位移路徑都有呼叫（`ExecuteBaseMovement` / `ExecuteVerticalOnlyMovement` /
兩個 `ExecuteBakedCurveMovement`）；request 單幀壽命，4.6 在 Update、MotionDriver 在 LateUpdate ⇒ 同幀，時序正確。
**沒有任何系統覆蓋 target-facing。**

📌 **攻擊期間的朝向會自動解決**：敵人結構上拿不到 Priority 1
（`ActionState.CaptureReleaseContext` 的 `if (_aimSource == null) return;`，S3b 刻意留的邊界）。
但 **B + C 修好後，Priority 2 在整個交戰期間都成立** ⇒ 攻擊時自然朝向玩家。
⛔ **不需要碰 `IAimSource`，不需要新 facing 系統。**

### 2.3 追擊鏈：`CombatContext` 完全沒有參與移動決策

- `ResolveEngagementMovement(current, distance, min, max, hysteresis)` 的簽章裡**沒有** `CombatContext`。
- `Approach` 的唯一條件是 `distance > maximumEngagementDistance`（Y Bot ＝ **2 m**），**沒有距離上限**；
  唯一前置閘門是 `target == null`。⇒ **整張地圖追殺。**
- 🔴 **`docs/19` §3.1.1 選錯了半徑**：把 `InCombat` 的 enter 設成 `maximumEngagementDistance`(2 m)，
  那**正好等於 Approach 門檻** ⇒ `InCombat` 的實際語意變成「已經站在近戰帶裡」，
  **整段追擊過程 `InCombat == false`**。交戰語境的半徑不該等於近戰帶的半徑。

### 2.6 ⭐ 受擊鏈的**真正**根因（2026-09-11 Play ＋ 只讀診斷確認）

**AI 的攻擊意圖是「持續」的，把外部 Reaction 信箱餓死。**

```csharp
// AIInputSource.cs:57 —— AI 端
public void FetchRawInput(ref InputData data)
    => data.Slot1ButtonDown = WantsToAttack();      // 純距離判定，每幀為 true

// PlayerInputSource.cs:84 —— 玩家端（對照組）
data.Slot1ButtonDown = Slot1Action != null && Slot1Action.WasPressedThisFrame();   // ✅ 真正的邊緣
```

⇒ 玩家在 `attackRange`(1.8 m) 內時，敵人**每一幀**都送出 `Slot1ButtonDown`
⇒ 順序 2 每幀寫 `Intent.RequestedActionSlot = Slot1`
⇒ `ActionState.TryResolveRequest:306`：

```csharp
slot = data.Intent.RequestedActionSlot;                         // 恆為 Slot1
if (slot == ActionSlot.None && _externalRequestTarget != null)   // ⛔ 永遠不執行
    slot = _externalRequestTarget.PendingSlot;                   // ⛔ Reaction 永遠讀不到
```

而 `ClearAfterEvaluation()` 在每次 `Tick` 的 `finally` 無條件清空信箱
⇒ **Reaction 每幀被寫入、每幀被丟棄，從未被讀過一次。**

**決定性證據 ——「緩速有用但受擊沒有」**：`GroundEffectSink.ApplyToRoot` 裡這兩件事相隔 4 行、
用**同一個 `other`**、**同一種 `GetComponentInParent`**，而 `ActionRequestTarget` 與
`TemporaryGameplayEffectState` 都在 Y Bot 的**同一個根 GameObject**（`7863296372082958677`，
`CharacterController` 也在那裡）⇒ 緩速生效就證明 `RequestAction` **確實有被呼叫**。
Slow 走 `TemporaryGameplayEffectState`（不經 FSM）所以活下來；Reaction 走 FSM 所以餓死。

**⇒ 偵測層完全無辜。§2.1 整張表的方向就找錯了。**

**為什麼「之前可以、現在不行」**：C1／C2 之前敵人無上限追擊、很少穩定停在 1.8 m 內
⇒ 有些幀 `RequestedActionSlot == None` ⇒ Reaction **偶爾**讀得到。
C1／C2 修好接近行為後，敵人**可靠地收在 1.25–2.0 m 帶內**（整段都在 `attackRange` 1.8 m 裡）
⇒ **恆為 true ⇒ 從間歇性故障變成常駐故障。** 是修好追擊把它逼現形的。

### 2.7 🔴 Finding — `TryResolveRequest` 的 starvation 風險（**只記錄，本輪不修**）

**`external Reaction` 目前只有在 `RequestedActionSlot == None` 時才會被讀取**
⇒ **任何未來的持續型（level）intent producer 都可能再次把 Reaction 餓死**，而且**不會有任何錯誤訊息**。

- 現有註解寫的政策是：「玩家意圖優先於 external request——同幀兩者都有時，自己的操作不該被別人的受擊請求蓋掉。」
- 該政策**對玩家成立**（玩家的 `RequestedActionSlot` 是 trigger 邊緣，順序 7 每幀清掉 ⇒ 「同幀撞在一起」是罕見事件）。
- **對 level 型 producer 不成立**：同一條規則從「平手時的 tie-break」退化成「**永久餓死**」。

⚠️ **本輪刻意不改成「Reaction 全域優先於 intent」**（使用者 2026-09-11 裁決）：
那會同時改變**玩家**「攻擊是否可被受擊打斷」的手感，**該政策尚未裁決**。
⇒ 本輪只修 **AI producer 的 semantics**（見 §4-D）；此 finding 留給未來的 policy 決策。

📌 **缺陷家族**：這是本專案第三次踩到**「不連續的選擇餵給連續量」**——
前兩次是 `ComputeSoleHeight` 的 argmax（`docs/05`）與 `OnTriggerEnter` 的邊緣事件。
共同形狀：**一個為「邊緣／罕見事件」設計的規則，被餵進一個持續為真的訊號。**

### 2.4 NavMesh 無效：✅ 已正確，不動

`CombatContext` 在 NavMesh 守衛**之前**結算（語境真相保留）、`_engagementMovement` 強制回 `Hold`（不漂移）。
已由 `CombatContextTests.TC9` 守住。**本輪零改動。**

---

## 3. 人工項（**使用者在 Unity Editor 執行；Codex ⛔ 不得碰**）

### ~~A0 — 先分流~~ ✅ **已結案（2026-09-11）**

分流結果是**兩個都不是**：Console 沒有查表失敗警告（`Damage` 映射有效），偵測層也無辜。
真正的根因由「**緩速有用但受擊沒有**」這個觀察定位到 §2.6。**下方原始分流表保留作紀錄。**



Play 打一次敵人，看 Console：

| 觀察 | 結論 | 動作 |
|---|---|---|
| 出現 `[AnimancerFacade] 狀態機請求播放 'Damage'…查表失敗` | 命中**有**被偵測到，是動畫映射問題 | A 縮小為「補 Y Bot 的 transition mapping」，**不需要 A1** |
| Console 什麼都沒有 | 命中**沒有**被偵測到 | 走 **A1** |

### ~~A1 — MeleeHitbox 加 kinematic Rigidbody~~ ❌ **已撤回（2026-09-11）**

Play 驗證**失敗**，且事後查明它的兩個前提都是錯的（見 §2.1 更正）。
**處置**：X Bot 的 `MeleeHitbox` 上那顆 Rigidbody **請移除**（Inspector 右鍵 Remove Component）——
真正的根因在 §2.6，與物理偵測無關，留著只是一顆沒有理由的元件。

### ~~A2 — 改用 `Physics.OverlapBox`~~ ⏸ **暫停（2026-09-11 使用者裁決）**

根因已確認不在偵測層 ⇒ **不執行**。
⚠️ **重新啟用條件**：AI request semantics 修好、Play 驗證後，**若近戰仍有獨立於 Fireball／Ice 的命中問題**，才回頭做 A2。

### B1 — Y Bot 勾 `usePersistentCombatFacing`

Y Bot prefab 的 `CharacterFacingSource` 勾選 `usePersistentCombatFacing`。
**X Bot ⛔ 不要勾**（預設 `false` 保證玩家 facing 路徑逐位元不變）。

### 人工項執行順序

**A0 → A1（如需要）→ B1 → 跑 EditMode → Play 驗收。**

> ⚠️ **B1 必須在跑測試之前完成**：下方 W14 是 B1 的機器形式，
> **B1 沒做時它就是紅的**——那是刻意的，不是壞掉。

---

## 4. Codex 工作項（**只有程式與測試**）

> ⛔ **Codex 不得碰**：任何 `.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`／動畫美術資產，
> 以及**任何 Git 操作**。⛔ **不得實作 A1／A2**（A1 是 prefab；A2 依 E3 尚未獲准）。

| # | 檔案 | 內容 |
|---|---|---|
| **B2** | `_Project/Tests/EditMode/PrefabWiringTests.cs` | 新增 **W14**（⚠️ 規劃時寫成 W12，但 W12／W13 已被佔用 ⇒ 依 `A24` 撞號先例順延；**編號是引用鍵，不是排名**）：每個角色 Root 的 `CharacterFacingSource.usePersistentCombatFacing` —— **玩家必須 `false`、敵人必須 `true`**。<br>沿用既有機制：`LoadCharacterPrefabs()` 取得 `CharacterPrefab{ Path, Host }`；**玩家判別沿用既有慣例＝ Runner Host 上有 `PlayerLocomotionPolicy`**（⛔ 不得用 prefab 名稱判斷）；以 `SerializedObject.FindProperty` **唯讀**讀值，訊息格式比照 W11 的 `contract／expected／actual` 四段 |
| **C1** | `Core/Movement/AIMovementSource.cs` | 新增 `[SerializeField, Min(0f)] private float aggroEnterRadius = 8f;` 與 `aggroLeaveRadius = 12f;`（`[Header("Combat Engagement")]`，camelCase）。`ResolveCombatEngagement` 的簽章由 `(wasInCombat, distance, maximumEngagementDistance, hysteresis)` 改為 **`(wasInCombat, distance, enterRadius, leaveRadius)`**，內部以 `Mathf.Max(leaveRadius, enterRadius)` 保證 leave ≥ enter（比照 `PlayerCombatContextSource` 的 `RadiusEpsilon` 夾持精神）。⚠️ `maximumEngagementDistance` **回歸只管近戰帶**，不再兼任交戰半徑 |
| **C2** | 同上 | `!inCombat` 時 **`_engagementMovement = EngagementMovement.Hold;` ＋ early-return**，**在產生 `MovementIntent` 之前**——形狀與既有 NavMesh 守衛完全一致。`Hold` 已收斂成真正的零移動 fallback；距離帶內的側移另以 `Strafe` 明確命名。early-return 仍是權威邊界：未交戰時不得繼續解析 tactical mode。 |
| **C3** | `_Project/Tests/EditMode/CombatContextTests.cs` | ①更新 **TC8** 對應新簽章（enter 8／leave 12 的黏性真值表，含「脫離後重新進場必須回到 enter」與退化輸入）；②新增 **TC10**：`!inCombat` 時 `MovementIntent` 必須為 `default` 且 `_engagementMovement == Hold`（＝「遠處不追」的機器形式）；③**TC9 不得改動** |
| **C4** | `docs/19-enemy-combat-facing-slice.md` §3.1.1 | 更正半徑來源：改為 `aggroEnterRadius`／`aggroLeaveRadius`，並明記「交戰語境的半徑**不等於**近戰帶的半徑」及原選擇錯在哪 |
| **C5** | `docs/02-dev-spec.md` §2.1 順序 2.5 | 補一句：`CombatContext` 的進出由**獨立的 aggro 半徑**決定，與 `EngagementMovement` 的近戰帶門檻**分離**；`!InCombat` 時本步不產生 `MovementIntent` |

**交付前必跑**（不需要開 Unity）：

```bash
dotnet build Project.Runtime.csproj -v q --nologo -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true
dotnet build Project.Tests.EditMode.csproj -v q --nologo -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true
```

`-p:` 旗標必要：`nunit.framework` 建置於 .NET 4.7.2、csproj 目標 4.7.1，缺旗標會噴滿螢幕假的 `CS0246`。
⚠️ **編譯 ≠ 測試**：EditMode 測試需要 Unity Test Runner，Codex 驗不到。

---

## 5. ADR 判準檢查（逐條，結論：**不開 ADR**）

| 判準 | 本切片 | 說明 |
|---|---|---|
| ① 黑板 schema／ownership 變更 | ❌ 否 | `CombatContextData` 欄位組成**不動**；寫入者仍是 `AIMovementSource`（已於上一輪登記進 `WriterRules`）。ADR-007 **D3 補充條款**把「**進出規則的門檻與時間常數**」明列為**不凍結** |
| ② FSM 拓撲／hierarchy 變更 | ❌ 否 | 不新增 state、不新增 `EngagementMovement` 列舉成員 |
| ③ 核心驅動介面變更 | ❌ 否 | `IMovementIntentSource` 等六個介面全部不動 |
| ④ 推翻既有不變量 | ❌ 否 | A31／A32／A5／W11 皆維持成立 |

⇒ **0/4，走 Living Docs routing rule**（C4／C5）。
📌 附帶：即使想開也開不成 `Trial`——ADR-004 §0 的名額仍被 ADR-007 佔用。

---

## 6. 驗收

### 6.1 可自動測（EditMode）

| 測項 | 內容 |
|---|---|
| **W14** | 玩家 `usePersistentCombatFacing == false`、敵人 `== true`（⚠️ **B1 完成前為紅**，見 §3） |
| **TC8**（更新） | 黏性半徑：enter 8 外不進場／enter 上進場／8–12 之間維持／> 12 脫離／**脫離後須回到 8 才重新進場**／leave < enter 的退化輸入被夾持 |
| **TC10**（新增） | `!inCombat` ⇒ `MovementIntent == default` 且 `_engagementMovement == Hold` |
| **TC9**（既有，不得改） | NavMesh 無效 ⇒ combat truth 保留、模式回 `Hold`、不產生 `MovementIntent` |
| 既有 | A2／A5／A31／A32／A33／A34／W11／W13 全綠 |

### 6.2 必須 Unity Play（人工判斷）

- 敵人擺在**很遠**的位置時，**不會**立即跨整張地圖衝過來。
- 玩家進入**合理範圍**（enter 8）後，敵人才開始接近。
- 敵人 Strafe／迂迴時，**身體持續朝向玩家**。
- 敵人攻擊時，**不再明顯背對或側對**玩家。
- 玩家攻擊命中敵人時，**穩定看到 Damage／Reaction 動畫**。
- 玩家拉開足夠距離（leave 12）後，敵人**真正退出 combat**，不再無限追。
- ⭐ **Player locomotion / facing 行為不得因此改變**（回歸檢查）。

### 6.3 ⛔ 停止點

**Codex 完成 B2／C1–C5 後停在 Play 驗收，⛔ 不得自行繼續做敵人 2D locomotion。**
D（2D locomotion 是否值得接）**只有在 A–C 全部 Play 通過之後**才重新評估。

---

## 7. 修訂紀錄

| 日期 | 內容 |
|---|---|
| 2026-09-11 | 建立。依使用者裁決 E1–E5；只讀診斷結論見 §2；本檔同時修正 `docs/19` §3.1.1 的半徑選擇錯誤 |
