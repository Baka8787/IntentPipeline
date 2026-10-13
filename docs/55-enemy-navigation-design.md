# 55 — Enemy Navigation & Tactical Movement：現況審查與設計（2026-10-13，Research + Design）

| | |
|---|---|
| **狀態** | 🟠 **Proposed**——設計審查。⛔ 不得作為實作基線；本輪**未修改** runtime、prefab、scene 或測試 |
| **輸入** | 使用者 2026-10-13：敵人增加「在一個區域迂迴或巡邏」的行動；六項行為（Waypoint Patrol／Area Roaming／Chase & Return／Combat Strafing／Tactical Positioning／Navigation Recovery） |
| **基線** | `docs/54`（第五版＋實作紀錄）：`AICombatContextSource` 順序 2.4、單一 target 權威 `AIMovementSource.Target`；`docs/21`（Enemy AI v1，FSM-lite）；ADR-003（Accepted）、ADR-007（Trial） |
| **ADR 判斷** | **建議不開新 ADR**（§7）。唯一可能碰到跨系統契約的是「巡邏點轉頭」若要轉**全身**——那是 ADR-007（Trial）的修訂，不是新 ADR，且需你裁決（N4） |
| **閱讀方式** | §1 現況（全部附檔案:行號）→ §2 缺口 → §3 方案比較 → §4 模組與資料流 → §5 行為契約 → §6 狀態轉移 → §7 ADR 判斷 → §8 驗證 → §9 分階段計畫 → §10 待裁決 |

> 行號以 commit `f5bafd5` 為準。沒有證據的結論標為 ❓**待確認**。

---

## 1. 現況盤點

### 1.1 敵人的每帧呼叫順序（`CharacterPipelineRunner.Update`／`LateUpdate`）

| 順序 | 呼叫 | 證據 | 敵人在這一步做什麼 |
|---|---|---|---|
| 0.5 | `_characterHealth?.PublishTo` | `CharacterPipelineRunner.cs:409` | 發布自己的 `Survivability`（`IsDead`／`JustTookDamage`） |
| 1 | `_inputSource?.FetchRawInput` | `:414` | `AIInputSource`：只產生 `Slot1ButtonDown`（攻擊請求） |
| 2⊘ | `BlockInput` 閘門 | `:439` | 死亡時 `DeathArbiterSource` 封鎖 ⇒ 輸入歸零 |
| 2 | `ProcessIntents` | `:445` | `Slot1ButtonDown` → `Intent.RequestedActionSlot` |
| 2.4 | `_aiCombatContextSource?.Tick` | `:456` | 寫 `CombatContext`（交戰、目標位置） |
| 2.5 | `_movementIntentSource?.ProduceIntent` | `:462` | `AIMovementSource`：寫 `MovementIntent`（**唯一**寫入者） |
| 3 | `_movementModel?.Tick` | `:475` | `LocomotionModel`：`MovementIntent` → `MoveSpeed`／`MoveDirection` |
| 4 | `_stateMachine.Tick` | `:479` | 同一套 FSM（`EnemyStateMachineConfig`） |
| 4.5 | `_arbiterPipeline?.Tick` | `:487` | 仲裁（死亡 ⇒ `BlockInput`） |
| 4.6 | `_facingSource?.Tick` | `:490` | `CharacterFacingSource` 送 facing request |
| 6 | `CurrentState.OnUpdateMotion` | `:521`（`LateUpdate`，`:514`） | 位移只經 `MotionDriver` |

### 1.2 `AIInputSource` 的責任邊界

- 唯一產出是 `InputData.Slot1ButtonDown` 的 request pulse（`AIInputSource.cs:65` `FetchRawInput`）；`WantsToAttack`（`:104`）只看「目標存在、活著、在 `attackRange` 內」（`:106–115`）。
- target 由 `AIMovementSource.Target` 讀（`:118` `ResolveTarget`），沒有自己的 target（WP-1b）。
- **A27** 機器化禁止它引用 `PlayerRuntimeData`、FSM、`IMovementIntentSource`（`ArchitectureRegressionTests.cs` `A27_*`）⇒ **它不能、也不該產生移動意圖**。
  順序 1 的 `InputData` 是「原始裝置輸入」；ADR-003 §6.3 已否決把 gameplay 修飾塞進 `InputData`。

### 1.3 移動、面向、目標的權威來源

| 概念 | 唯一權威 | 證據 |
|---|---|---|
| 目標（序列化） | `AIMovementSource.target` → 唯讀 `Target` | `AIMovementSource.cs:42`、`:77`；`AICombatContextSourceTests.AB1` |
| 交戰語境 | `AICombatContextSource.Tick`（順序 2.4） | `AICombatContextSource.cs:60`；A5 `WriterRules` |
| 移動意圖 | `AIMovementSource.ProduceIntent`（順序 2.5） | `AIMovementSource.cs:109`；A5 |
| 速度／方向 dynamics | `LocomotionModel`（順序 3） | A5（`MoveSpeed`／`MoveDirection` 只准 `LocomotionModel.cs`） |
| 位置 | `MotionDriver` → `CharacterController.Move` | `MotionDriver.cs:528`（`ExecuteBaseMovement`）、`:571–573`；A48 |
| 朝向 | `CharacterFacingSource`（唯一 sender）→ `MotionDriver.RequestFacing`（僅當帧） | `CharacterFacingSource.cs:31`、`:54–66`；`MotionDriver.cs:515–521`；A31／A32 |
| 朝向優先序 | ① Action commitment ② 持續 combat facing（Y Bot `usePersistentCombatFacing`）③ 跟隨 `MoveDirection` ④ 不送（維持） | `CharacterFacingSource.cs:107–122` |

### 1.4 跨層依賴限制

- **A4** `Core/Movement`（頂層）白名單＝ `Project.Core.Movement`／`Blackboard`／`Effects`；⛔ StateMachine、Presentation、**Combat**、**Survivability**（ADR-003 D2：producer context-free）。
- **A54**：`AIMovementSource` 不得出現 `AICombatContextSource`／`PlayerCombatContextSource`／`Project.Core.Combat`／`CharacterHealth`——移動只經黑板讀交戰結果。
- **A16**：`AIMovementSource` 只把 NavMesh 當查詢（`updatePosition`／`updateRotation` 必須 false）。
- `Core/Combat` 沒有 A4 規則（`AICombatContextSource` 合法引用 `Core.Movement`、`Core.Survivability`）。

### 1.5 Hurt／Death／Action／Traversal 對移動的中斷

| 狀態 | 位移路徑 | 是否消費 `MovementIntent` | 證據 |
|---|---|---|---|
| Idle／Move | `BaseState` → `ExecuteBaseMovement` | ✅ | `BaseState.cs:63` |
| Action（無 Bake） | `ExecuteBaseMovement` | ✅ | `ActionState.cs:309` |
| Action（有 Bake，敵人出拳） | `ExecuteBakedCurveMovement`（Punch Bake 水平位移 0） | ❌ | `ActionState.cs:313`；`docs/21` §1.3.1 |
| Hurt | `ExecuteVerticalOnlyMovement` | ❌ | `HurtState.cs:143` |
| Death | `ExecuteVerticalOnlyMovement` | ❌ | `DeathState.cs:114` |
| Traversal | 敵人沒有（`EnemyStateMachineConfig` 無 Traversal；A38 `hasTraversal: false`） | — | `ArchitectureRegressionTests.A38_*` |

⚠️ **producer 在 Hurt／Action 期間照常每帧執行**（Runner 無條件呼叫 `:462`），只是輸出不被消費。自己死亡時 `AIMovementSource` 收手（`AIMovementSource.cs:120`）。
⇒ 任何「隨時間推進」的巡邏狀態（停留計時、下一點選擇）若不看實際位置，會在硬直期間**空轉**。

### 1.6 尋路系統

- 套件：`com.unity.ai.navigation` 2.0.13（`Packages/manifest.json:4`）。
- 場景：一個 `NavMeshSurface`（`SampleScene.unity:8907`），烘焙資料 `Assets/Scenes/SampleScene/NavMesh-Navigation.asset`；**沒有** `NavMeshLink`／`OffMeshLink`／`NavMeshObstacle`（場景 grep 0 筆）。
- Y Bot `NavMeshAgent`：radius 0.35、speed 3.5、stoppingDistance 1.5、`ObstacleAvoidanceType` 4（HighQuality）、`AutoRepath` 1、`AutoTraverseOffMeshLink` 1（`Y Bot.prefab`）。
  ⚠️ speed／avoidance 在 `updatePosition = false` 下**不影響實際位移**——位移只由 `MovementIntent` → `MotionDriver` 決定。

### 1.7 導航如何與 `CharacterController`／`MotionDriver` 協作（現行）

| 步驟 | 證據 |
|---|---|
| `Awake`：`updatePosition = false`、`updateRotation = false` | `AIMovementSource.cs:89–90` |
| 未交戰 ⇒ `Hold`、**完全不碰 agent** | `:134–139` |
| 離網 ⇒ `Recover`：`NavMesh.SamplePosition` 找最近可走點，失敗則朝目標 | `:157–178` |
| 交戰中：`_agent.nextPosition = transform.position` → `SetDestination(targetPosition)` **每帧** | `:189–190` |
| Approach：`pathPending` ⇒ 這帧零意圖；無路徑／`PathInvalid` ⇒ 直接朝目標 | `:245–249` |
| Strafe：`NavMesh.Raycast` 檢查側移一步是否出網 | `:387` |
| 刻意不用 `NavMeshAgent.Warp()`（註解：會設 `transform.position`） | `:155` ❓**待確認**：`updatePosition = false` 時 `Warp` 是否真的移動 Transform——Unity 文件未明說，需實測 |

### 1.8 可重用的測試與護欄

| 類別 | 既有測試 |
|---|---|
| 架構 | A4（白名單）、A5（單一寫入者）、A16（NavMesh 只查詢）、A25（Slow 不外洩到意圖消費者）、A27（攻擊只由 input source 產生）、A30（AI 不讀相機）、A31／A32（facing 權威）、A48（位置唯一寫入者）、A54（combat 先於 movement；單向依賴） |
| 純函數 | `EnemyEngagementMovementTests`（距離帶遲滯、側移、自救方向）、`AICombatContextSourceTests`（Q4 規則） |
| 真元件 | `CombatContextTests.TC9／TC10`（離網、未交戰）、`AICombatContextSourceTests.AB5／AB8`（同帧一致） |
| 真場景 | `AICombatContextPlayModeTests`（真 Runner＋出貨場景）、`StairTraversalPlayModeTests.Enemy_TraversesGeneratedStair`（真 NavMesh 上走完樓梯） |
| Prefab | `PrefabWiringTests.W10／W15／W27` |

---

## 2. 缺口

| # | 缺口 | 證據 | 影響 |
|---|---|---|---|
| **G1** | **沒有「家」**：沒有出生點、活動區域或回家的概念。脫戰後 `Hold` 原地不動 | `AIMovementSource.cs:134–139` | Chase & Return、Roam、Patrol 全部缺 |
| **G2** | **未交戰時 agent 完全不更新**：`nextPosition` 只在交戰分支同步 | `:189` 只在 `:134` 之後 | 巡邏一加進來，agent 內部位置會落後 Transform ⇒ 路徑從舊位置算 |
| **G3** | **每帧 `SetDestination`** | `:190` | N 隻敵人 × 每帧重新請求路徑；目的地沒動也重算（❓**待確認**：Unity 是否對相同目的地 early-out——需 Profiler） |
| **G4** | **`PathPartial` 未處理**：只把 `PathInvalid`／`!hasPath` 當失敗 | `:247–249` | 目的地不可達時走到半路終點後停在那，或朝牆推 |
| **G5** | **沒有「卡住」偵測**：只有離網（`isOnNavMesh`）與側移出網檢查 | `:157`、`:387` | 在網上被 `CharacterController` 擋住（其他敵人、玩家、斜坡邊）時永遠推不動，沒有恢復 |
| **G6** | **多敵人沒有避讓**：方向取自 `steeringTarget`，不用 agent 的 avoidance 速度 | `:249` | 多隻敵人擠同一條路徑時互相卡住，只靠膠囊碰撞 |
| **G7** | **未交戰時沒有速度上限**：Y Bot 只有 walk clips（1.644 m/s），max-speed 參考是 Sprint（6.261 m/s）；方向限速政策是 `WhileInCombat` | `docs/21` §1.5；`Bake_WalkFwdLoop` 1.6443043、`Bake_SprintFwdLoop` 6.2613893；`LocomotionModel` 政策 | 巡邏若沿用 `desiredSpeedNormalized = 1` ⇒ 以約 6.3 m/s 播 walk clip ⇒ 嚴重滑步。walk 的 normalized ≈ **0.263** |
| **G8** | **戰術站位只有一維距離帶**：Approach／Strafe／Retreat 以「到目標的距離」決定，沒有「這個位置可不可達、適不適合」的評估 | `AIMovementSource.cs:181`（`ResolveEngagementMovement`） | 第 5 項目前不存在 |
| **G9** | **離網自救沒有「放棄」**：取樣失敗就朝目標走 | `AIMovementSource.cs:355`（`TryResolveRecoveryDirection`） | 掉進導航網外的坑會一直推 |
| **G10** | `AutoTraverseOffMeshLink = 1` 但位移不歸 agent | `Y Bot.prefab` | 目前場景沒有 link，無影響；之後加 link 時 agent 內部會自己「跳過去」而角色沒有 ⇒ 需要 off-link 處理規則 |

---

## 3. 方案比較

> ⚠️ 原題的 B「由 `AIInputSource` 輸出意圖」在本專案不成立：`AIInputSource` 在順序 1，只能寫原始輸入（A27、ADR-003 §6.3）。
> 移動意圖的唯一合法出口是順序 2.5 的 `IMovementIntentSource`（ADR-003 D2）。下表的 B 因此是「Behavior＋Navigation＋Steering，經既有 `IMovementIntentSource` 輸出 `MovementIntent`」。

| | **A** 塞進 `AIInputSource` | **B** 獨立 Behavior／Navigation／Steering（經 `IMovementIntentSource`） | **C** Behavior Tree／Utility 高層決策 |
|---|---|---|---|
| **做法** | 巡邏、尋路都在 `AIInputSource` | `AIMovementSource` 保持唯一 producer，內部拆成：模式選擇（純函數）＋目的地政策（純函數）＋`EnemyNavigator`（持有 agent 查詢）＋Steering（純函數） | 新增 BT／Utility runtime 選行為，下游仍是 B 的 Navigation／Steering |
| **依賴方向** | ⛔ 違反 A27（input source 不得寫移動）、ADR-003 D2（移動意圖單一 producer） | ✅ 全部住在 `Core/Movement`（A4 白名單內）；Combat→Movement 只有既有的唯讀 `Target` | ✅ 若 runtime 放 `Core/AI`；但要為新資料夾定 A4 規則 |
| **擴充性** | 差：兩種職責綁在一起 | 好：新模式＝新增一個純函數分支＋一組 authored 資料 | 最好：行為可組合 |
| **測試成本** | 高（要繞過 A27） | **低**：決策全是純函數（比照 `EnemyEngagementMovementTests`），NavMesh 只在 PlayMode 驗 | 高：要測 runtime 本身＋每棵樹 |
| **效能** | 同 B | 可控：路徑請求集中在 `EnemyNavigator`，加重算節流 | BT tick 成本＋配置；零 GC 要另外守 |
| **與現有架構相容** | ⛔ | ✅ 不改 Runner、黑板、FSM、核心介面 | ⚠️ `docs/21` §3 已把 Utility 定為「行為達 5–6 種且互相競爭時才升級」；現在非戰鬥模式只有 3 種（Patrol／Roam／Return），**提前建 runtime 違反「第二個使用者出現前不建 production abstraction」** |

**推薦：B**。它是 `docs/21` 方案 A（FSM-lite）的自然延伸。C 保留為升級路線，觸發條件沿用 `docs/21` §3。

### 3.1 `NavMeshAgent` 只當導航時，怎麼避免雙寫位置、怎麼同步

| 規則 | 做法 |
|---|---|
| **位置只有一個寫入者** | 維持 `updatePosition = false`、`updateRotation = false`（A16 守）；⛔ 不用 `Warp`（見 §1.7 待確認項）、⛔ 不讀 `agent.velocity` 去移動角色 |
| **每帧先同步再查詢** | `EnemyNavigator` 在**任何**路徑查詢之前 `agent.nextPosition = transform.position`——**包含未交戰的巡邏**（修 G2） |
| **偏離量是診斷，不是修正** | 記錄 `|agent.nextPosition − transform.position|`（`nextPosition` 會被夾到 NavMesh 上）；超過門檻 ⇒ 視為離網，進 `Recover`；⛔ 不反過來改 Transform |
| **路徑請求節流** | 只有「目的地移動超過 ε」或「距上次請求超過 Δt」或「路徑失效」才 `SetDestination`（修 G3） |
| **避讓（選配，N6）** | 若需要：讀 `agent.desiredVelocity` 的**方向**當 steering 輸入（仍只寫 `MovementIntent`），速度由 `MovementIntent` 決定；或自行做純函數 separation。⛔ 兩者都不得讓 agent 取得位移權 |

---

## 4. 建議模組與資料流

```text
            （authored，唯讀）                      （唯讀參照，既有）
   EnemyTerritory ／ PatrolRoute ──────┐      AIMovementSource.Target
             （N1 決定放哪）           │                 │
                                       ▼                 ▼
順序 2.4  AICombatContextSource ── 讀 home／leash（N2）── 寫 CombatContext（schema 不變）
                                                          │（黑板）
順序 2.5  AIMovementSource : IMovementIntentSource（唯一 MovementIntent writer，不變）
            ├─ ResolveMode（純函數）          InCombat ⇒ Engage（現行 Approach／Strafe／Retreat）
            │                                  !InCombat ⇒ Return ／ Patrol ／ Roam ／ Dwell
            ├─ DestinationPolicy（純函數）     下一個巡邏點／遊走點、停留、重選
            ├─ EnemyNavigator（持有 NavMeshAgent 查詢）
            │      同步 nextPosition → 節流請求 → pathStatus → 進度／卡住偵測
            └─ Steering（純函數）               轉角方向、抵達減速、（選配）separation
                              ▼
                       MovementIntent（schema 不變）
順序 3 LocomotionModel → 4 FSM → 4.6 CharacterFacingSource → 6 MotionDriver（全部不變）
```

| 模組 | 住在哪 | 為什麼 |
|---|---|---|
| `ResolveMode`／`DestinationPolicy`／`Steering` | `Core/Movement`，`AIMovementSource` 的 `internal static` 或同資料夾的 static class | 比照既有 `ResolveEngagementMovement`／`TryResolveStrafeDirection`：純函數、EditMode 可測、不需要 NavMesh |
| `EnemyNavigator` | `Core/Movement`，普通 C# 類別（不是 MonoBehaviour），由 `AIMovementSource` 持有 | 把 agent 的同步、節流、路徑狀態、卡住偵測集中一處；**不是**第二個 producer |
| `EnemyTerritory`（家、半徑、巡邏點） | 待 N1：場景物件（MonoBehaviour＋子 Transform）或 prefab 預設 | 世界座標是場景資料；`AIMovementSource` 以序列化引用讀（同 `target` 的模式） |
| leash 判斷 | `AICombatContextSource`（順序 2.4） | 「還在不在交戰」只有一個 owner；移動不得自己決定脫戰（A54 精神） |

⛔ **不新增**：第二個 `IMovementIntentSource`、黑板欄位、FSM 狀態、Runner 步驟、target 管理元件。

---

## 5. 行為契約

| 行為 | 進入 | 退出 | 輸入（唯一權威） | 目的地與重選 | 不可達 | 中斷／恢復／清理 | 更新與防抖 |
|---|---|---|---|---|---|---|---|
| **Waypoint Patrol** | `!InCombat` ∧ 有巡邏路線 ∧ 在領地內 | `InCombat`；或離開領地 ⇒ Return | 路線（authored）、自身位置 | 依序／往返；**以位置判定抵達**（距離 ≤ r_arrive），不以時間 | `PathInvalid`／`PathPartial` 或卡住 ⇒ 跳過該點並記錄；連續 N 點失敗 ⇒ Dwell 並等待 | 被交戰打斷時記住「下一個點」；脫戰回到領地後從**最近的點**接續 | 抵達半徑＋停留時間；目的地固定 ⇒ 只在換點時請求路徑 |
| **Dwell（停留／轉頭）** | 抵達巡邏點 | 停留時間到 | authored 停留秒數 | — | — | 交戰打斷 ⇒ 清掉計時 | 計時用**累積的未中斷時間**（Hurt 期間不累積，避免空轉）；轉頭見 N4 |
| **Area Roaming** | `!InCombat` ∧ 沒有巡邏路線 ∧ 在領地內 | 同上 | 領地中心＋半徑 | 在半徑內 `NavMesh.SamplePosition` 取點，要求 `CalculatePath` 為 `PathComplete`；抵達後停留再選 | 取樣失敗 K 次 ⇒ Dwell 一段時間再試 | 同 Patrol | 新點與舊點最小距離（避免原地小抖）；選點用帶種子的亂數，測試可重現 |
| **Chase**（現行 Approach） | `InCombat` ∧ 距離 > 近戰帶上界 | 進近戰帶 ⇒ Strafe；`!InCombat` | `CombatContext.TargetPosition`（同帧，A54） | 目標移動 > ε 或間隔 Δt 才重新請求 | `PathPartial`：走到部分終點 → 用 steering 直衝（現行 fallback）；持續卡住 ⇒ 交給 leash（N2）判斷脫戰 | 交戰語境由 2.4 決定，移動不自行脫戰 | 現行遲滯不變 |
| **Return**（新） | `!InCombat` ∧ 不在領地內（或剛脫戰） | 回到領地內圈 | home（authored／出生點） | 固定目的地 home；只在進入時請求一次，失效才重請求 | 不可達 ⇒ 走到最近可達點並在那裡 Dwell（❓是否「傳送回家」：⛔ 位置只由 MotionDriver 寫，不提供） | 途中受擊／再激怒依 N3 | 進入／離開用兩個半徑（遲滯），避免在邊界反覆切換 |
| **Combat Strafing**（現行） | `InCombat` ∧ 在近戰帶內 | 離開帶 | 同 Chase | 切線方向；換向間隔 2.5 s×[0.75, 1.25] | 兩側皆不可走 ⇒ 暫停（現行） | 同上 | 現行遲滯＋換向計時 |
| **Tactical Positioning** | 本輪建議**只做 MVP**（N7）：Approach 的目的地若不可達，改取目標周圍半徑＝近戰帶上界的環上、`PathComplete` 的最近點 | — | `CombatContext.TargetPosition`、NavMesh | 每 Δt 最多評估一次、固定候選數（如 8 個方位），零配置 | 全部不可達 ⇒ 維持現行 fallback | — | 選中的點有「黏性」，新點分數須好過舊點一個門檻才換 |
| **Navigation Recovery** | 離網（現行）；🆕 卡住：期望移動但 Δt 內實際位移 < d_min | 回到網上；或卡住解除 | 自身位置、agent 同步偏差 | 離網：最近可走點；卡住：先換目的地／側向避讓，再失敗 ⇒ Dwell | 取樣失敗 ⇒ 🆕 有上限的嘗試後放棄並 Dwell（修 G9） | 不推進其他模式的遲滯（現行原則） | 卡住判定用移動窗，不用單帧 |

**與 `CombatContext`／`MovementContext` 的關係**
- `CombatContext`：唯一回答「在不在交戰、目標在哪」。所有移動模式**只讀**它（A54）；脫戰（含 leash）由 `AICombatContextSource` 決定。⛔ 不另建 target 權威（`docs/54` §3.3.2；AB1 守）。
- `MovementContext`（ADR-003 D3／§13.2）：只回答「用哪個 movement model」（locomotion／swim／vehicle），**尚未實作**（全 repo 只在 Runner 註解出現，`CharacterPipelineRunner.cs:47`）。巡邏與它**無關**——巡邏是意圖層（producer）的決策，不是 model 選擇。

---

## 6. 狀態轉移規則（`ResolveMode`，純函數，每帧）

```text
自己死亡                                   ⇒ Hold（現行，不寫意圖）
離網 ∨ 卡住（§5 Recovery）                 ⇒ Recover（不推進其他模式的記憶）
InCombat ∧ HasTarget                       ⇒ Engage（現行 Approach／Strafe／Retreat 遲滯）
!InCombat ∧ 在 Return 中 ∧ 未進內圈        ⇒ Return
!InCombat ∧ 不在領地（外圈之外）           ⇒ Return
!InCombat ∧ 在領地 ∧ 停留計時未完          ⇒ Dwell
!InCombat ∧ 在領地 ∧ 有巡邏路線            ⇒ Patrol
!InCombat ∧ 在領地                          ⇒ Roam
```

- 領地用**內圈／外圈兩個半徑**（遲滯）：出外圈才觸發 Return，回到內圈才結束 Return。
- Engage → 脫戰：若仍在外圈內 ⇒ 直接接回 Patrol（最近點）或 Roam；否則 Return。
- Hurt／Action 期間 producer 照跑但輸出不被消費（§1.5）⇒ 模式切換只依位置與 `CombatContext`，計時只在「實際在移動或停留」時累積。
- 防抖：每個二選一的切換都有遲滯或最短持續時間；Strafe 換向沿用現行計時。

---

## 7. 是否需要 ADR

依 CLAUDE.md 判準逐條：

| 判準 | 本設計 | 結論 |
|---|---|---|
| ① 黑板 schema／ownership | 不新增欄位；`MovementIntent`／`CombatContext` 的寫入者不變 | 不觸發 |
| ② FSM 拓撲／GameObject 階層 | 巡邏是 ambient Idle／Move，不新增狀態；領地若是場景物件屬資料，不改角色階層 | 不觸發 |
| ③ 管線順序／核心驅動介面 | Runner、`IMovementIntentSource`、`IMovementModel` 都不改 | 不觸發 |
| ④ 推翻既有不變量 | A16（NavMesh 只查詢）、A54（單向依賴）都**維持**；leash 是交戰規則的門檻變化，ADR-007 D3 補充條款已把「進出規則的門檻與時間常數」列為**不凍結** | 不觸發 |

⇒ **不開 ADR**，走 Living Spec：`docs/21`（Enemy AI baseline）新增章節＋本檔。`docs/21` §2「Patrol 不屬於 v1」要改寫為「v2 加入」。

⚠️ **例外**：N4 若選「巡邏點全身原地轉向」，`CharacterFacingSource` 需要一個新的、非移動的朝向輸入——目前 facing 來源只有 Action commitment、combat target、`MoveDirection`（`CharacterFacingSource.cs:107–122`），**沒有合法管道表達「站著看某個方向」**。那會動到黑板（判準①）與 ADR-007 D3 的 facing 來源（Trial ⇒ 修訂紀錄，不是新 ADR）。

---

## 8. 驗證設計

| 要驗的事 | 自動化 | 層級 |
|---|---|---|
| 巡邏持續執行、不飄離領地 | 純函數：`ResolveMode` 在任意位置序列下，不在領地時一定回 Return；PlayMode：真場景 Y Bot 巡邏 N 圈，取樣位置**全程在外圈內** | EditMode＋PlayMode |
| 障礙物後的目的地經尋路抵達 | PlayMode：在 PlayMode 臨時建 NavMesh（`NavMeshBuilder` 已在 `MovementIntentTests` 用過）＋一道牆，目的地在牆後；斷言抵達且**從未穿牆**（直線距離 < 實際路徑長） | PlayMode |
| 不可達有明確恢復 | 純函數：`PathPartial`／`PathInvalid`／取樣失敗 ⇒ 預期的模式與次數上限；PlayMode：孤島目的地 ⇒ 最終 Dwell，不無限推牆 | EditMode＋PlayMode |
| 受擊／攻擊／死亡正確中斷 | 沿用 `AICombatContextSourceTests` 形狀：受擊 ⇒ 當帧 Engage；死亡 ⇒ 不寫意圖；Hurt 期間巡邏計時不累積（注入時間） | EditMode |
| 丟失目標後回到活動範圍 | 純函數：脫戰 ∧ 在外圈外 ⇒ Return，進內圈 ⇒ 結束；PlayMode：拉到領地外脫戰，取樣直到回到內圈（有時間上限） | EditMode＋PlayMode |
| 迂迴不會頻繁左右切換 | 純函數：以抖動距離序列餵 `ResolveMode`／Strafe，斷言每秒切換次數 ≤ 門檻（比照現行 `ResolveEngagementMovement` 遲滯測試） | EditMode |
| 導航不繞過既有移動權威 | 沿用 A16、A48、A5；🆕 擴充 A16：`EnemyNavigator` 也不得出現 `Warp`／`transform.position =`／`updatePosition = true` | EditMode |
| 多敵人無重複尋路、零 GC | PlayMode：N 隻敵人跑 T 秒，`EnemyNavigator` 的請求計數（內部計數器）≤ 預算；`GC.GetAllocatedBytesForCurrentThread` 在 producer 呼叫前後差值為 0 | PlayMode |

**人工 Play（保留給手感與可讀性）**：巡邏走路速度與腳步是否貼合（G7）、停留與轉頭是否自然（N4）、回家路線是否合理、多敵人是否互相卡住（N6）。

---

## 9. 分階段實作計畫

| 階段 | 內容 | 驗收（摘要） |
|---|---|---|
| **P0 導航基礎（行為中性）** | `EnemyNavigator`：每帧同步 `nextPosition`（修 G2）、路徑請求節流（G3）、`PathPartial` 處理（G4）、卡住偵測（G5）、自救上限（G9）。現行交戰行為逐字保留 | 既有 `EnemyEngagementMovementTests`／`CombatContextTests`／`AICombatContextPlayModeTests`／Stair 敵人測試全綠；請求次數下降有量測；A16 擴充 |
| **P1 領地＋Return** | `EnemyTerritory`（N1）、leash（N2，住 `AICombatContextSource`）、Return 模式（含遲滯）、未交戰速度＝walk（G7，N5） | 脫戰後回到內圈；領地邊界不抖；巡邏速度 ≤ walk 門檻（測試讀 Bake 推導，不手填） |
| **P2 Area Roaming** | 半徑內取點＋`PathComplete` 檢查＋停留 | 長時間遊走位置全在外圈內；選點可重現（種子） |
| **P3 Waypoint Patrol** | 路線、依序／往返、停留、（N4）轉頭 | 依序經過每個點；交戰後從最近點接續 |
| **P4 多敵人** | （N6）separation 或 avoidance 方向；效能預算 | N 隻敵人無互卡；請求計數與 GC 預算 |
| **P5 戰術站位 MVP** | （N7）近戰帶環上的可達點 | 目的地不可達時改走環上可達點；評估頻率有上限 |

每階段各自一個 commit；P0 為純基礎修整，可先行。人工 Editor 工作（擺放領地／巡邏點，若 N1 選場景物件）集中在 P1／P3 的 Integration Gate 一次交出。

---

## 10. 需要你裁決的問題

| # | 問題 | 選項 | 我的建議 |
|---|---|---|---|
| **N1** | 領地與巡邏點放在哪 | ①場景物件（MonoBehaviour＋子 Transform，可視化、每隻敵人不同；**要改 `SampleScene`**）②預設「出生點＋半徑」，巡邏點用相對出生點的偏移存在 prefab（不改場景，但每隻敵人路線相同） | ①（巡邏路線本來就是關卡資料）；擺放由你在 Editor 完成，或等你 commit 場景後由我經 Editor API 加 |
| **N2** | 追擊的 leash | ①以「離家距離」為準：超過 R_leash 就脫戰回家（受擊也一樣）②維持現行 Q4（以「到目標的距離」為準），不加 leash ③兩者都要，任一成立就脫戰 | ③；但受擊時 leash 是否仍生效要你決定（Q4 的 Provoked 20 m 是「到目標」，leash 是「到家」，兩者語意不同） |
| **N3** | Return 途中可否再被激怒 | ①任何 aggro 都可以②只有受擊可以③完全不行，回到內圈才恢復 | ②（避免在邊界被拉鋸，又不會被打不還手） |
| **N4** | 巡邏點「轉頭」 | ①不做②只轉頭部（presentation，Y Bot 目前沒有 `HeadLookController`）③全身原地轉向（需要 facing 新輸入 ⇒ 黑板＋ADR-007 Trial 修訂） | ①或②；③的架構成本與效果不成比例 |
| **N5** | 未交戰速度 | ①固定 walk（Bake 推導 ≈ 0.263）②authored 值，但測試守住 ≤ walk 門檻 | ①（Y Bot 沒有 run clip，否則必滑步，G7） |
| **N6** | 多敵人避讓 | ①不做（目前場景一隻敵人）②純函數 separation ③借 agent 的 avoidance 方向 | ①，等第二隻敵人真正出現再做（Gate A／B） |
| **N7** | 戰術站位範圍 | ①只做 MVP（不可達時改走近戰帶環上的可達點）②候選點評分（掩體、包夾、視線） | ①；②需要感知系統，`docs/21` §2 明確不做 |

⛔ 本輪沒有改任何程式、prefab、場景或測試。
