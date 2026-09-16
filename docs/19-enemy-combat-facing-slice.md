# 19 — Enemy Combat Facing ＋ 2D Locomotion（實作切片規格）

> **這份文件是什麼**：一個**已裁決、可直接實作**的最小切片規格。
> **這份文件不是什麼**：⛔ 不是 ADR、⛔ 不是 roadmap 修訂、⛔ 不是研究筆記。
> 建立日期：2026-09-10。程式對照基準：`71f5a5c` ＋ 當日工作樹。
> 上游脈絡：`docs/18-benchmark-convergence.md`（候選盤點）、`docs/ADR/007-direction-authority.md`（🟡 Trial）。

---

## 0. 使用者裁決（2026-09-10，本切片的前提）

| # | 裁決 |
|---|---|
| **D1** | **敵人**在 combat 且有 target 時**持續 target-facing**。理由：這是敵人**既有** Strafe 行為本來就需要的 facing policy，不是新玩法 |
| **D2** | ⛔ **不接受全域以 2D 取代 1D**。**Player 正常 locomotion 維持 1D**；Enemy combat locomotion 可用 2D |
| **D3** | ⛔ **不做「依 separation angle 在 1D／2D 之間 runtime 切換」**。1D／2D 的選擇由 **locomotion presentation mode ／ actor policy** 決定，**不是角度門檻** |
| **D4** | 未來 Player committed casting 若完成 upper／lower body layering，再讓下半身使用 2D。**本切片不碰** |
| **D5** | ADR-007 §8 驗收**延後**。本切片**不做**驗收、**不翻** Trial → Accepted |

> 📌 **D3 的技術理由（不是品味）**：以角度門檻在兩個 mixer 之間切換＝**用不連續選擇餵給連續量**。
> 本 repo 已經被這一類缺陷咬過一次——`ComputeSoleHeight` 的註解明文禁止退回 argmax 選端點，
> 因為 `max()` 連續但 argmax 之後再取另一個屬性不連續。角度門檻是同一個形狀。

---

## 1. Scope

### ✅ In scope
1. **Enemy combat target-facing**：`CharacterFacingSource` 的 Priority 2 接上 combat target，並以 **actor policy** 控制誰啟用。
2. **Enemy 2D locomotion 的程式與測試前置**：讓敵人可以用 2D mixer，並補上唯讀接線測試。

### ⛔ Out of scope（明確不做）
- Player 的任何 facing／locomotion 行為改變 —— **player path 一個位元都不動**。
- ADR-006 ／ upper-body layering ／ 下半身分層。
- 任何 observability 擴張（Direction Authority 的四箭頭／HUD **不重開**；Foot IK debug **不動**）。
- Traversal ／ 前方 obstacle query（production 完全不存在該 query，見 `docs/18` §1.1）。
- ADR-007 §8 驗收與 Trial → Accepted。
- **開任何 ADR**（判準檢查見 §5）。

---

## 2. 磁碟事實（已於 2026-09-10 逐項核對，實作時可直接信任）

| # | 事實 | 出處 |
|---|---|---|
| F1 | **敵人已經在側移繞圈**：`EngagementMovement.Strafe` → `ResolveStrafeDirection(toTarget, _strafeDirectionSign)`；`Hold` 現在只表示零移動 fallback | `Core/Movement/AIMovementSource.cs` |
| F2 | **敵人 facing 目前跟隨 MoveDirection** ⇒ 繞圈時側身／背對玩家。因為 `TryResolveFacing` 的 **Priority 2 是空的**（註解：「S3a 沒有 producer，不建立假資料」） | `Core/Facing/CharacterFacingSource.cs:77` |
| F3 | **MoveX／MoveZ 已經每幀在發布**，敵我共用同一顆 `LocomotionModel`（順序 3 每幀無條件執行） | `Core/Movement/Models/LocomotionModel.cs:146-149` |
| F4 | 解算鏈**已經是** world → actor-local：`ProjectMoveParameters` ＝ `Quaternion.Inverse(actorRotation) * worldMoveDirection`，**不是** camera-relative | `LocomotionModel.ProjectMoveParameters` |
| F5 | ⭐ **`data.MoveSpeed` 是正規化 [0,1]**（`MotionDriver.ExecuteBaseMovement`：`data.MoveSpeed * moveSpeed`）⇒ **MoveX／MoveZ 落在單位圓盤上** | `Presentation/Motion/MotionDriver.cs` |
| F6 | ⭐ **2D 資產的參數已經對上**：`Locomotion_2D_Proto_*` 的 `_ParameterNameX/Y` guid ＝ `778a27b9…`／`ef428365…` ＝ `MoveX.asset`／`MoveZ.asset` ＝ `AnimationFacadeBase.ParamMoveX/ParamMoveZ` | 資產 YAML ＋ `.meta` |
| F7 | `Locomotion_2D_Proto_FullRing` ＝ **9 samples**（中心 idle ＋ 8 方向），threshold 半徑 **1.0**；`RunRing` 同樣 9 samples，半徑 **0.5714** | 資產 YAML |
| F8 | ⚠️ **兩個 2D 環都沒有速度階層**（單一半徑）。1D `Locomotion.asset` 是 4 階 `LinearMixerTransition`，threshold `0 / 0.35 / 0.75 / 1` | 資產 YAML |
| F9 | ⚠️ **敵人有兩種速度**：`desiredSpeedNormalized = 1.0`（Approach／Retreat）、`holdStrafeSpeedNormalized = 0.35`（Strafe；欄位沿用既有序列化名稱） | `AIMovementSource.cs:23,31` |
| F10 | `CombatContext` 目前唯一寫入者是 `PlayerCombatContextSource`，且 Runner 明文「`PlayerCombatContextSource` 與 `IAimSource` 都是玩家專屬，⛔ 不得順手加入」 | `WriterRules`；`CharacterPipelineRunner.cs:29-32` |
| F11 | `WriterRules` 的 `MovementIntent` **已經列兩個檔案**（`PlayerLocomotionPolicy.cs`、`AIMovementSource.cs`），Owner ＝「每隻角色當下唯一 active 的 …」。`CombatContext` 的 Owner 欄位**已是相同措辭**，只是目前一個檔案 | `ArchitectureRegressionTests.WriterRules` |
| F12 | `W11` 保證每個角色 Root 恰好一顆 `CharacterFacingSource` | `PrefabWiringTests.cs:245` |

---

## 3. 設計

### 3.1 敵人的 combat target 從哪裡來 —— **`AIMovementSource`，不新增元件**

> ✅ **2026-09-10 已實作並修正**。落地形狀與本節原文有兩處必須明確化的地方，記於下方 **3.1.1／3.1.2**。
> 上游依據：ADR-007 **D3 補充條款**明訂「`CombatContextData` 的唯一寫入者＝每角色唯一 active 的
> combat context producer」、「⛔ 禁止任何其他系統自建 `isInCombat` 旗標或第二份 target 清單」，
> 並把**進出規則的門檻與時間常數**明列為**不凍結**。

`AIMovementSource` **已經擁有** `target` 與接戰帶判定。若另建 `EnemyCombatContextSource`，它需要自己的
target 參考 ⇒ **敵人的「誰是我的目標」會出現第二個真相來源**，那正是 `docs/16` §4 的加重審查項第 2／3 條。

⇒ **由 `AIMovementSource` 一併寫 `CombatContext`**（它已經是該角色的 per-character 黑板寫入者）。
`WriterRules` 的 `CombatContext.AllowedFiles` 加上 `AIMovementSource.cs`，**完全比照 `MovementIntent` 的既有先例**（F11）。

- **不是**「第二個寫入者寫同一個欄位」——每隻角色有自己的 `PlayerRuntimeData` 實例，
  規則是「**每隻角色當下唯一 active 的 producer**」，與 `MovementIntent` 逐字相同。
- ⚠️ **記錄下來的取捨**：`AIMovementSource` 因此同時產 `MovementIntent` 與 `CombatContext`，SRP 上略寬。
  接受的理由是它避免了目標的第二份真相；若日後敵人的戰鬥語境長出與移動無關的判定，再拆。
- **寫入時機**：順序 **2.5**（`ProduceIntent` 內）。合法性：消費者在 4.6，2.5 < 4.6。
  玩家版之所以必須在 2.6，是因為它要讀順序 2 的 Action intent 且要趕在順序 4 清掉 external target 之前——
  **敵人版沒有這兩個約束**。dev-spec §2.1 的 2.5 列需補一句說明。

#### 3.1.1 ⭐ `InCombat` 必須是黏性半徑，⛔ 不得只由 `target != null` 決定

本節原文只寫「依既有的 `target` ＋ 接戰帶判定」，**不夠具體**——第一版實作據此只寫成
`target != null ⇒ InCombat = true`。因為 `target` 是 Inspector 上恆為非 null 的序列化欄位，
那等於**敵人從場景開始就永久 InCombat**，配上持續 target-facing ⇒ 隔著整張地圖也面向玩家，
§7.2 的「脫離 combat 回到 facing 跟隨 MoveDirection」**永遠驗不過**。

**2026-09-11 修正後的實作規則**（`AIMovementSource.ResolveCombatEngagement`，純函數）：

| | 值 |
|---|---|
| **enter 半徑** | 獨立的 `aggroEnterRadius`（第一版測試值 8 m） |
| **leave 半徑** | 獨立的 `aggroLeaveRadius`（第一版測試值 12 m；解算時不小於 enter） |
| **黏性核心** | 尚未進場用 **enter**、已進場用較大的 **leave** ——沿用 `PlayerCombatContextSource` 的
`effectiveEnterRadius` / `effectiveLeaveRadius` 形狀 |
| **前一幀狀態從哪來** | `data.CombatContext.InCombat`（讀自己上一幀發布的值，與玩家 producer 的 `wasInCombat` 同形） |

⚠️ **交戰語境的半徑不等於近戰帶的半徑。** 本節原先把 enter 設成
`maximumEngagementDistance`（2 m）、leave 設成它加 `distanceHysteresis`；這個選擇是錯的：
2 m 正好是 `EngagementMovement.Approach` 的近戰帶門檻，導致整段追擊期間 `InCombat == false`，
只有已站進近戰帶才算交戰。`maximumEngagementDistance`／`distanceHysteresis` 自此只管理
Approach／Retreat／Hold 的近戰移動帶；aggro／leash 由獨立的 8／12 m 半徑管理。

#### 3.1.2 ⭐ CombatContext 與 EngagementMovement 是**兩個層次**，NavMesh 只影響後者

**2026-09-10 使用者裁決**：

| 層 | 回答什麼 | 受 NavMesh 影響？ |
|---|---|---|
| **`CombatContext`** | 「還在不在跟這個目標交戰、有沒有有效 target」——**目標關係**的性質 | ❌ **不受**。agent 掉出 NavMesh 不代表脫離戰鬥 ⇒ 必須在 NavMesh 守衛**之前**結算 |
| **`EngagementMovement`**（Approach／Retreat／Hold） | 「**在可導航前提下**要怎麼移動」——**導航**的性質 | ✅ 受。agent 無效／離網時**回到並保持 `Hold`** |

⛔ **離網期間不得讓遲滯狀態機繼續推進**：Approach／Retreat 的遲滯記憶若被離網期間的距離變化污染，
回到 NavMesh 的那一幀會拿到一個**沒有任何一幀真正推導過**的模式。
⛔ 同時**不得**把「NavMesh 無效」等同於「沒有 target ／ 不在 combat」——語境真相要保留。

📌 這也修正了第一版實作的一個未回報副作用：它把 NavMesh 守衛下移，順帶讓
`ResolveEngagementMovement` 在離網時也會執行（舊版會強制 `Hold`）。現已還原為舊行為。

### 3.2 Facing Priority 2 —— actor policy 用 `[SerializeField]` 表達

`CharacterFacingSource.TryResolveFacing` 現況優先序：

```
0. 死亡否決層（2026-09-14 新增，見下）
1. Action commitment（有死區）
2. explicit target ← 空的
3. MoveDirection（無死區）
4. 不送 request
```

🆕 **2026-09-14 —— 死亡是否決層，不是第 0 順位。**
`CharacterFacingSource.Tick` 讀 `data.Survivability.IsDead`；為真時**不論哪一順位解出什麼方向都不送
request**，因此寫成 `0.` 只是位置示意——它不參與競爭，它取消整場競爭。
- **症狀**：屍體會不斷面向玩家（使用者 2026-09-14 Play 回報）。
- **⚠️ 為什麼 `DeathArbiterSource` 擋不住**：它封鎖的是 `BlockInput`，即**輸入產生的意圖**；
  但 Priority 2 讀的是 `CombatContext`，由 `AIMovementSource` 在順序 2.5 直接寫黑板，
  **根本不經過輸入**。這是「封鎖輸入 ≠ 封鎖所有行為」的具體案例。
- **不需要新機制**：不送 request 時 `MotionDriver` 維持既有 rotation，正是 Priority 4 既有語意。
- ⛔ 只讀已 commit 的 `IsDead`，不重算 `CurrentHealth <= 0`。
- 由 `CombatContextTests.TC6G` 在 `Tick` 層釘住（純函數層驗不到——缺陷不在優先序裡，
  而在「`CharacterFacingSource` 根本不認識死亡」）。
- 📌 **裁決**：使用者 2026-09-14 在兩個候選修法層之間選了這一個（最靠近症狀），
  否決了「死亡時停止產生 `CombatContext`」（更上游，但會連帶影響其他 consumer）。

**改動**：Priority 2 接上「持續 combat facing」，並以**每個角色自己的序列化開關**決定是否啟用。

```csharp
[SerializeField] private bool usePersistentCombatFacing = false;   // 預設 false ＝ 玩家行為零改變
```

- **預設 `false`** ⇒ **玩家 prefab 完全不需要任何 Editor 操作**，行為逐位元不變（D2 的硬保證）。
- 敵人 prefab 由**人工**勾選（見 §6）。
- 這個形狀沿用 `docs/01` §4.8 v0.22 的既有紀律：「**控制方案的可配置面在資產、不在 policy 程式碼**」。
- ⛔ **不要**為此建立 `IFacingPolicySource` 介面——只有一個使用者，違反 CLAUDE.md「第二個使用者出現前不得建立 production abstraction」。

**死區**：Priority 2 **不吃死區**，與 Priority 3 同列。
理由寫在既有註解裡：持續型 facing 是 continuous dynamics，套死區會複製「停住 → 飄出死區 → 猛轉」的極限環。

**與 Priority 1 的關係**：**不變**。Action commitment 仍然壓過 combat facing——
敵人出拳時朝向承諾方向是正確的，且 `EnemyPunchDefinition` 無 Bake（可自由移動），
正好是 committed facing 的合法案例。

### 3.3 1D／2D 的選擇 —— **actor policy，不是 runtime 角度切換**（D3）

`LocomotionModel` **不需要知道** 1D 還是 2D：它無條件同時發布 `MoveSpeed`、`MoveX`、`MoveZ`（F3）。
**mixer 的選擇完全發生在資產層**——`AnimancerFacade` 的 Transition Mappings 把 `Move` 這個
`AnimationKey` 對應到哪一份 transition，是**每個 prefab 各自的映射**。

⇒ **敵人 prefab 的 `Move` 映射指向 2D 資產、玩家維持 1D，這件事零程式碼。**

- ✅ 符合 D3：選擇來自 actor policy（哪個 prefab），不是任何 runtime 判斷。
- ✅ 符合 D2：沒有全域取代，玩家路徑不動。
- ✅ 沒有 mode 切換 ⇒ **沒有切換瞬間的爆點**，也不需要 crossfade 設計。
- ⚠️ **代價（F8／F9）**：2D 環只有單一速度半徑。敵人 Approach／Retreat 在正規化 1.0（＝`FullRing` 半徑，
  **正好落在環上**），但 Strafe 在 0.35 ⇒ 落在**中心 idle 與環之間**，會混出 35% 方向 clip ＋ 65% idle。
  加上 `_SynchronizeChildren` 全關，**混合區可能出現腳滑**。
  ⇒ 這是**表現調校問題，必須 Play 判斷**，不是程式問題。處理順序依 CLAUDE.md 動畫升級階梯：
  **① 先調 Data／Runtime 參數**（把 `holdStrafeSpeedNormalized` 調到環半徑）→ ② 再調 Presentation（Mixer／Transition）
  → ③ 換 clip → ④ 才輪到改 clip 內容。⛔ **不得**因此去複製 AnimationClip。

---

## 4. 交給 Codex 的工作項（**只有程式與測試**）

> ⛔ **Codex 不得碰**：任何 `.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`／動畫美術資產，
> 以及**任何 Git 操作**。那些一律由使用者在 Unity Editor ／ Terminal 執行（CLAUDE.md 鐵律）。

| # | 檔案 | 內容 |
|---|---|---|
| **W1** | `Core/Movement/AIMovementSource.cs` | 在既有 `ProduceIntent`（順序 2.5）內，依既有的 `target` ＋ 接戰帶判定，整體覆寫 `data.CombatContext`（`InCombat`／`HasTarget`／`TargetPosition`）。**沿用既有欄位，⛔ 不新增第二個 target 參考**。零 GC：只賦值 struct |
| **W2** | `Core/Facing/CharacterFacingSource.cs` | ①新增 `[SerializeField] private bool usePersistentCombatFacing = false;`（camelCase，CLAUDE.md 豁免底線規則）；②`TryResolveFacing` 加入 Priority 2 參數（`bool hasCombatFacing, Vector3 combatFacing`），維持 `internal static` 純函數形狀以便測試；③`Tick` 依開關 ＋ `data.CombatContext` 求出 combat facing 方向後傳入；④Priority 2 **不吃死區**（`ShouldRequestFacing` 的 `fromActionCommitment` 語意不變） |
| **W3** | `_Project/Tests/EditMode/ArchitectureRegressionTests.cs` | `WriterRules` 的 `CombatContext.AllowedFiles` 加入 `"AIMovementSource.cs"`（比照 `MovementIntent`），Owner 措辭不變 |
| **W4** | `_Project/Tests/EditMode/CombatContextTests.cs` | `TryResolveFacing` 新優先序的純函數測試（見 §7.1） |
| **W5** | `_Project/Tests/EditMode/LocomotionMixerWiringTests.cs` | **唯讀**（`AssetDatabase`／`SerializedObject`）2D 接線測試（見 §7.1）。⛔ 不得寫入任何資產 |
| **W6** | `docs/02-dev-spec.md` | §1.1 `CombatContext` 權限表列補 `AIMovementSource`；§2.1 順序 2.5 補一句「敵人 producer 一併發布 `CombatContext`（無 2.6 的兩個時序約束）」 |
| **W7** | `docs/01-design-doc.md` §4.8 | 補一句 actor policy：facing 來源政策的可配置面在 prefab 序列化欄位，不在 policy 程式碼（沿用 v0.22 紀律） |

**交付前必跑**（不需要開 Unity）：

```bash
dotnet build Project.Runtime.csproj -v q --nologo -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true
```

同樣跑一次 `Project.Tests.EditMode.csproj`。⚠️ **編譯 ≠ 測試**：EditMode 測試需要 Unity Test Runner，Codex 驗不到。

---

## 5. ADR 判準檢查（逐條，結論：**不開 ADR**）

| CLAUDE.md 判準 | 本切片 | 說明 |
|---|---|---|
| ① 黑板 schema／ownership 變更 | ❌ 否 | `CombatContext` 欄位**不變**；`AllowedFiles` 加一個檔名是**沿用 `MovementIntent` 的既有 per-character single-writer 先例**（F11），不是新規則 |
| ② FSM 拓撲／GameObject hierarchy 變更 | ❌ 否 | 零狀態、零階層改動 |
| ③ 核心驅動介面變更 | ❌ 否 | `IInputSource`／`IMovementIntentSource`／`IMovementModel`／`AnimationFacadeBase`／`IPresentationController`／`IArbiterSource` **全部不動**。`TryResolveFacing` 是 `internal static`，不是驅動介面 |
| ④ 推翻既有架構不變量 | ❌ 否 | A31（唯一 facing 送出者）／A32（MotionDriver 不決定 facing）**皆維持成立** |

⇒ **0/4，走 Living Docs routing rule**（W6／W7）。
📌 附帶事實：即使想開也開不成 `Trial`——ADR-004 §0「同時只允許一個 Trial」的名額仍被 ADR-007 佔用（D5 延後驗收）。

---

## 6. 人工項（**使用者在 Unity Editor 執行，Codex 不做**）

1. **Y Bot prefab**：勾選 `CharacterFacingSource.usePersistentCombatFacing`。
   （X Bot **不要勾**——預設 `false` 已保證玩家行為不變。）
2. **Y Bot 的 AnimancerFacade Transition Mappings**：把 `Move` 鍵改指向 `Locomotion_2D_Proto_FullRing`。
   X Bot 維持 `Locomotion.asset`（1D）。
3. **Play 調校**（§3.3 的已知代價）：觀察 Strafe 在正規化 0.35 的混合品質。
   若腳滑或看起來像滑行，**先調 `AIMovementSource.holdStrafeSpeedNormalized`**（Data 層，階梯 ①），
   不要先動 Mixer，更不要動 clip。
4. **跑 EditMode 全套**，確認含新增測試在內全綠。

---

## 7. 驗收

### 7.1 可自動測（EditMode，不需開 Editor 操作）

| 測項 | 內容 |
|---|---|
| **T1** | `TryResolveFacing` 新優先序真值表：①有 commitment ＋ 有 combat facing → **commitment 贏**；②無 commitment ＋ 有 combat facing → **combat 贏**、`fromActionCommitment == false`；③兩者皆無 ＋ 有 MoveDirection → **MoveDirection 贏**；④全無 → `false`（Priority 4）；⑤combat facing 為零向量／與自身重合 → 退回 MoveDirection |
| **T2** | Priority 2 **不吃死區**：`ShouldRequestFacing(forward, combatDir, fromActionCommitment: false, deadzone)` 在任意小角差下恆為 `true` |
| **T3** | `usePersistentCombatFacing == false` 時，`TryResolveFacing` 的結果與改動前**逐案相同**（玩家零回歸的機器保證） |
| **T4** | `WriterRules` 更新後 A5 仍綠（`CombatContext` 只有 `PlayerCombatContextSource` ＋ `AIMovementSource` 兩個檔案寫入） |
| **T5** | 既有 A31／A32 仍綠（唯一 facing 送出者、MotionDriver 不決定 facing） |
| **T8** ✅ | **接戰語境黏性半徑**（`CombatContextTests.TC8`）：未進場需走進 enter 半徑才進場；已進場在 enter–leave 之間維持（黏性）；超出 leave 才脫離；**脫離後重新進場必須回到 enter 半徑**（否則遲滯等於沒有）；負 hysteresis 夾成 0 不得反向 |
| **T9** ✅ | **NavMesh 無效的分層**（`CombatContextTests.TC9`）：agent 無效時 ①`InCombat`／`HasTarget`／`TargetPosition` **仍然成立**；②`_engagementMovement` **必須是 `Hold`**（不得漂移）；③不產生 `MovementIntent`。刻意用 `_agent = null` 表示離網 ⇒ **不需要臨時 NavMesh**，不受 `MovementIntentTests` 那條 EditMode NavMesh 限制影響 |
| **T6** | **唯讀資產測**：`Locomotion_2D_Proto_FullRing` 的 `_ParameterNameX/Y` 必須等於 `MoveX.asset`／`MoveZ.asset`；threshold 必須含中心 `(0,0)` ＋ 8 個方向；`_Animations` 無 `{fileID: 0}` 缺口 |
| **T7** | **唯讀 prefab 測**：X Bot 的 `usePersistentCombatFacing` 必須為 `false`（玩家 1D／非持續 facing 的機器保證）。⚠️ Y Bot 的對應斷言**等人工勾選後**才加，否則交接時是紅的 |

### 7.2 必須 Unity Play（人工判斷）

- 敵人在 combat 繞圈時**持續面向玩家**，側移／後退／出拳皆然；脫離 combat 回到 facing 跟隨 MoveDirection。
- 8 向動畫與實際位移方向一致（不是反的、不是鏡像的）。
- Strafe 在 0.35 的混合品質（§3.3 的已知風險）——**腳滑與否只能用看的**。
- 玩家零回歸的**手感**部分（自動測只能證明邏輯分支相同，證明不了感覺）。
- 零 GC 穩態（`docs/02` §7.4 SOP）。⚠️ 量測前先關掉 `drawFootIKRuntimeLines`（見 `docs/18` §8-F10 之外的 review finding）。

---

## 8. 修訂紀錄

| 日期 | 內容 |
|---|---|
| 2026-09-10 | 建立。依使用者 D1–D5 裁決；上游候選盤點見 `docs/18` §1／§3 |
