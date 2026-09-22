# 41 — 模組級 Runtime 效能審查（2026-09-22）

> **性質**：一次性靜態審查輪。**只讀不改**——本輪沒有動任何一行程式、任何一個資產。
> **目的不是最佳化**，是回答「**哪些地方值得花時間去 Profile**」，以及把「看得出來的」與「看不出來的」分乾淨。
>
> **⚠️ 證據紀律（本文件的核心規則）**
> 每一條結論都標了證據等級，**不得混用**：
>
> | 等級 | 意義 | 可以拿來做什麼 |
> |---|---|---|
> | 🟢 **Code-proven** | 直接從磁碟上的程式／authored 資產讀出來的事實。查詢次數、迴圈上界、編譯條件、資產數值 | 可以直接引用為事實 |
> | 🟡 **Likely risk** | 程式結構讓它**有可能**貴，但真實成本取決於場景內容／執行期分支／引擎內部 | 只能當成「Profile 的優先序」，⛔ 不得當成缺陷 |
> | 🔴 **Profiler required** | 靜態分析**原理上**答不出來（Animator 評估、PhysX 內部、CharacterController.Move、IL2CPP 裝箱） | 只能列出「要在 Profiler 看什麼」 |
>
> ⛔ **本文件刻意不提出任何修法。** 看到 `Update()` 不等於有問題，0 GC 不等於快，一次 Raycast 不等於貴。

---

## §0 這個專案的實際形狀（審查前提）

在談任何成本之前，先把「誰每幀在跑」講清楚——本專案的架構讓這件事比一般 Unity 專案**好回答很多**。

### §0.1 幾乎沒有自由的 `Update()`

🟢 **Code-proven.** 全 runtime（`Assets/Scripts/Core` + `Presentation` + `App`，119 個 `.cs`）只有這些 Unity 訊息迴圈：

| 檔案 | 訊息 | 性質 |
|---|---|---|
| `Core/Pipeline/CharacterPipelineRunner.cs:391` | `Update` | **每角色的唯一 gameplay tick**（順序 0.5→5） |
| `Core/Pipeline/CharacterPipelineRunner.cs:501` | `LateUpdate` | **每角色的唯一表現 tick**（順序 6／6.5／7） |
| `Presentation/Motion/MotionDriver.cs:1821` | `LateUpdate` | **純 debug**（capsule offset 環），非 gameplay |
| `Presentation/Camera/ThirdPersonCamera.cs:125` | `LateUpdate` | 場上唯一一顆 |
| `Presentation/Actions/ThrownProjectile.cs:165` | `FixedUpdate` | 每顆飛行中的投射物 |
| `Presentation/IK/FootIKRig.cs:63`、`TraversalHandIKRig.cs:36` | `OnAnimatorIK` | Animator IK pass 回呼 |
| `App/*`（`CursorMode`／`GamePause`／`Respawn`／`PlayerHud`／`WorldSpaceHealthBar`／`RuntimeDebugPanel`） | `Update`／`LateUpdate` | 應用層，**單一實例或每敵人一個輕量 billboard** |

⇒ **所有 gameplay 與表現成本都收斂在兩個入口**（`Runner.Update` / `Runner.LateUpdate`）。
這對 Profiler 是**巨大的優勢**：一支 `IntentPipeline.Frame` marker 就能把整個角色成本圈起來，
不需要在 Hierarchy view 裡追散落的 MonoBehaviour。

### §0.2 兩種角色的實際元件組成

🟢 **Code-proven**（由 `.prefab` 的 `m_Script` GUID 逐一解析）：

| | **X Bot（玩家）** | **Y Bot（敵人）** |
|---|---|---|
| 腳本元件數 | 54 | 21 |
| Pipeline / Motion / Locomotion / Animancer / Facing / Health / Death | ✅ | ✅ |
| **FootIKController ＋ FootIKRig** | ✅ | ✅ **敵人也有** |
| **FootstepDetector ＋ AudioController** | ✅ | ✅ **敵人也有** |
| **TraversalProbe / TraversalHandIK(×2)** | ✅ | ❌ |
| AimResolver / HeadLook / PlayerCombatContextSource | ✅ | ❌ |
| GroundEffectSink / ThrowProjectileEmitter | ✅ | ❌ |
| AIInputSource / AIMovementSource(NavMeshAgent) | ❌ | ✅ |
| WorldSpaceHealthBar | ❌ | ✅ |
| RuntimeDebugPanel / GamePause / Cursor / Respawn / PlayerHud | ✅（應用層寄居在玩家上） | ❌ |

**這張表就是整份審查的 scaling 基礎**：
- **會隨敵人數線性放大的**：Pipeline tick、MotionDriver、LocomotionModel、**Foot IK（6 rays）**、FootstepDetector、AudioController（＋**10 顆 AudioSource**）、Animancer graph、NavMeshAgent、WorldSpaceHealthBar。
- **恆為 1×（玩家專屬）**：TraversalProbe（**最貴的查詢來源**）、Hand IK、AimResolver、CombatContext soft-target 掃描、HeadLook、Camera。

⚠️ **這個分佈是好消息**：最貴的那一塊（Traversal）恰好**不隨角色數放大**。
真正的 scaling 風險在 Foot IK、Animator 與 AudioSource 數量，不在 Traversal。

### §0.3 目前場景規模

🟢 `Assets/Scenes/SampleScene.unity`：**Y Bot 實例 = 1**。
⇒ **目前所有「N 隻敵人」的成本都還沒有被實際觀測過。** 本文件 §D 的 benchmark matrix 就是為了補這個洞。

### §0.4 不存在／未啟用的模組（使用者點名的審查範圍內）

| 使用者點名 | 現況 |
|---|---|
| **Lock-on** | 🟢 **程式不存在**。`grep -rn "LockOn"` 在 `Assets/Scripts` 零命中；`docs/10-lock-on.md` 標註「規劃中」。**零 runtime 成本。** |
| **Spell / upper-body layer** | 🟢 存在但**事件驅動**：`AnimancerFacade` 的 overlay layer 只在 `Play(stateKey)` 對應到帶 `AvatarMask` 的 mapping 時建立（`AnimancerFacade.cs:244–259`），**沒有常駐第二層**。 |
| **Terrain 材質解析** | 🟢 程式存在（`SurfaceResolver.ResolveTerrain`）但**目前場景沒有 Terrain ⇒ 路徑不可達**。原碼自己標註 `GetAlphamaps` 會配置陣列（有 GC），屬**未來風險**，非現況。 |
| **物件池（投射物／VFX）** | 🟢 **不存在**。`ThrowProjectileEmitter` / `GroundEffectSink` 皆為 `Instantiate` + 延時 `Destroy`。唯一的池是 `AudioSourcePool`。 |
| **Addressables / Resources.Load** | 🟢 **runtime 零使用**。全部走 Inspector 直接引用 ⇒ 沒有「首次載入」型的 streaming spike，但改由**場景載入時一次付清**。 |

---

## §1 模組總表

> **讀法**：`Per-character?` 欄的 `N+1` 代表「玩家 1 ＋ 敵人 N」；`1×` 代表恆為一個實例。
> `Physics Queries` 欄的數字是**每次 tick**，不是每秒。

| Module | Update Frequency | Per-character? | Physics Queries（min / worst） | Animator Work | Allocation Risk | CPU Risk | Spike Risk | Scaling Risk | Evidence | Needs Profiling? |
|---|---|---|---|---|---|---|---|---|---|---|
| **CharacterPipelineRunner**（順序 0.5–5） | 每 frame `Update` | **N+1** | 0（自身） | 順序 5 一次 `string.Equals` 比對；變更才 `Play()` | 🟢 無（`InputData` 為 stack struct；`ProcessIntents` 的 `Debug.Log` 包在 `#if UNITY_EDITOR`） | 低：純委派派送 | 無 | 線性，係數極小 | `CharacterPipelineRunner.cs:391–487` | ❌ 不需要（但值得當 marker 的外框） |
| **PlayerInputSource / AIInputSource** | 每 frame（順序 1） | N+1 | 0 | — | 🟢 無 | 極低。AI 僅一次 `sqrMagnitude` 比距離，**單一序列化 target，無掃描** | 無 | O(1)/角色 | `AIInputSource.cs:96–110` | ❌ |
| **Intent Processor**（順序 2） | 每 frame | N+1 | 0 | — | 🟢 無 | 極低（數個 bool） | 無 | 無 | `CharacterPipelineRunner.cs:560–590` | ❌ |
| **PlayerLocomotionPolicy**（順序 2.5，玩家） | 每 frame | **1×** | 0 | — | 🟢 無 | 低：camera-relative 基底轉換 | 無 | 無 | `Core/Movement/PlayerLocomotionPolicy.cs` | ❌ |
| **AIMovementSource**（順序 2.5，敵人） | 每 frame | **N** | **0 / 3**：`SetDestination` 1×（隱含 path 請求）＋ Strafe 時 `NavMesh.Raycast` ×1–2；離網時 `NavMesh.SamplePosition` ×1 | — | 🟢 無（全部 struct 覆寫） | 🟡 **每幀無條件 `_agent.SetDestination(targetPosition)`**——玩家在動⇒每幀都是新目標⇒每幀觸發一次非同步 path 計算 | 🟡 **NavMesh path recalculation**；目標不可達時 `PathInvalid` 每幀重算 | **線性，且係數由 NavMesh job 決定（非本專案控制）** | `AIMovementSource.cs:196–197, 214–218, 418` | ✅ **高優先**（`Navigation` / `AI` Profiler 模組） |
| **PlayerCombatContextSource**（順序 2.6） | 每 frame | **1×** | **1 / 2**：`OverlapSphereNonAlloc`（soft target，**無條件每幀**，r=12 m，buffer 32）＋（無 sticky target 時）第二次 | — | 🟡 **需驗**：`OverlapSphereNonAlloc` 本身零配置；但每個命中都 `GetComponentInParent<ActionRequestTarget>()`（hierarchy 上行 + 型別比對） | 🟡 **O(K) hierarchy walk／幀**，K＝半徑 12 m 內的 targetMask collider 數 ⇒ **敵人愈多愈貴，而且是玩家單點承擔** | 無 | **這是唯一「N 隻敵人 × 玩家」的交叉成本**（但只有 1 個掃描者，不是 N×N） | `PlayerCombatContextSource.cs:88–94, 134–152` | ✅ **高優先** |
| **TraversalProbe**（順序 2.7） | 每 frame，**grounded 時全跑**；空中早退 | **1×（僅玩家）** | 見 §2 明細。**空曠處 25；面牆可攀 ≈ 78** | — | 🟢 gameplay 路徑零配置（全 `out` 多載）。🔴 debug 路徑有字串（見 §5） | 🟡 見 §2 | 🟡 首次建立 debug LineRenderer 池（`new GameObject` ×128） | **不放大**（玩家專屬） | `TraversalProbe.cs:293–645` | ✅ **最高優先** |
| **LocomotionModel**（順序 3） | 每 frame | **N+1** | 0 | **3× `SetFloat`／幀**（MoveSpeed／MoveX／MoveZ）→ Animancer `Parameters.SetValue(string,…)` | 🟡 **需驗**：原碼聲稱 `string→StringReference` 走 intern 快取只配置一次。**這是 Animancer 內部行為，不是本專案可靜態證明的** | 低：一次 `Quaternion.Inverse` ＋ `SmoothDamp` | 無 | 線性 | `LocomotionModel.cs:146–154`；`AnimancerFacade.cs:306–313` | ✅（GC Alloc 欄位看有沒有每幀漏） |
| **FullBodyStateMachine**（順序 4） | 每 frame | **N+1** | 0 | — | 🔴 **需驗**：`Dictionary<StateType,…>` ×6，enum key。現代 .NET/IL2CPP 應已特化不裝箱，**但這在 Mono 後端上不保證** | 🟡 **每幀遍歷整個 state registry**，對每個 state 呼 `CanEnter()` ＋ `_config.GetPriority()`（Dictionary 查表）。registry ≈ 7 ⇒ 每角色每幀 ≈ 7 次 dict 查表 ＋ 7 次虛擬呼叫 | 無 | 線性，係數小但**常數項 ×7** | `FullBodyStateMachine.cs:105–160`；`StateMachineConfigSO.cs:44–49` | ✅（GC Alloc；若為 0 則忽略 CPU） |
| **ArbiterPipeline**（順序 4.5） | 每 frame | N+1 | 0 | — | 🟢 無（預先配置陣列，索引迴圈） | 極低 | 無 | 線性，極小 | `ArbiterPipeline.cs:40` | ❌ |
| **CharacterFacingSource**（順序 4.6） | 每 frame | N+1 | 0 | — | 🟢 無（debug 記錄包在 `#if UNITY_EDITOR`） | 低：一次 `Vector3.Angle` 等級的三角 | 無 | 線性，極小 | `CharacterFacingSource.cs:40–100` | ❌ |
| **MotionDriver — gameplay**（順序 6） | 每 frame `LateUpdate` | **N+1** | **2**：`CharacterController.Move` ×1 ＋ `Physics.CapsuleCast` ×1（capsule offset 幾何夾持） | 讀 N 根骨骼 `Transform.position`（landmark 取樣） | 🟢 無（landmark Transform **只在首次解析一次**，`ResolveCapsuleLandmarks` 有 `_capsuleLandmarksResolved` 閘） | 🟡 每幀 `transform.InverseTransformPoint()` × landmark 數 ＋ `SmoothDamp` ＋ `CapsuleCast`。**`CapsuleCast` 的 mask 是 `~(1 << gameObject.layer)`＝除自己層外全開** | 無 | **線性，且每角色帶一次 CapsuleCast ＋ 一次 CC.Move** | `MotionDriver.cs:456–480, 599–730, 853–893` | ✅ **高優先**（`CharacterController.Move` 見 §B） |
| **Foot IK（Controller + Rig）** | 每 frame（順序 6.5 Tick）＋ `OnAnimatorIK` 回呼 | **N+1，含所有敵人** | **0（空中） / 6（著地）**：每腳 ankle 1 ＋ heel 1 ＋ toe 1（`UseTwoPointSampling: 1`） | `OnAnimatorIK`：4× `GetIKPosition/Rotation` ＋ 2× `feetBottomHeight` 讀取 ＋ `bodyPosition` 寫入 ＋ 8× `SetIK*` | 🟢 gameplay 零配置 | 🟡 **`GroundLayers` = `m_Bits: 4294967295`（Everything）** ⇒ 6 條射線對全部層做 broad-phase | 🟡 首次啟用 debug 時建立 48 個 `new GameObject($"Line {index}")`（含字串插值） | 🔴 **最主要的線性放大源**：30 敵人 ⇒ **186 raycast/frame**，**且沒有任何距離／可見性剔除** | `FootIKController.cs:136–318`；`FootIKRig.cs:63–96`；prefab `GroundLayers` | ✅ **最高優先** |
| **FootstepDetector（+ FootPlantTracker + SurfaceResolver）** | 每 frame（6.5） | **N+1** | **0** —— 🟢 **完全複用 Foot IK 已發的射線**（`FootIKGroundData` 單向管道） | 讀 `FootIKPoseData`（不碰 Animator） | 🟡 **需驗**：`SurfaceResolver` 以 collider 參考記憶化；**cache 只有一格**。跨 collider 邊界（磁磚地）會每次 miss ⇒ `GetComponentInParent<SurfaceOverride>()` ＋ 最多 3 次 `GetComponent*<Renderer>()` | 低（命中 cache 時就是一次 `ReferenceEquals`） | 🟡 Terrain 路徑 `GetAlphamaps` **會配置**（目前不可達） | 線性 | `FootstepDetector.cs:113–192`；`SurfaceResolver.cs:38–95` | 🟡 中（只有在多 collider 地板上才需要） |
| **AudioController + AudioSourcePool** | 每 frame（6.5，只讀事件旗標） | **N+1** | 0 | — | 🟢 tick 零配置 | 極低（讀 3–5 個 bool） | 🔴 **`Start` 時每角色建立 10 個 `new GameObject` ＋ `AddComponent<AudioSource>()`** | 🔴 **30 敵人 = 310 顆 AudioSource**。閒置成本低，但會撞 Unity 的 Real/Virtual voice 上限並造成 voice 搶奪 | `AudioController.cs:45, 86`；`AudioSourcePool.cs:48–60`；prefab `audioSourceCount: 10`（兩者皆是） | ✅（`Audio` Profiler 模組） |
| **HeadLookController** | 每 frame（6.5） | **1×** | 0 | 寫 1 根 `headBone` 的 rotation | 🟢 無 | 低（一次 `atan2` 級三角） | 無 | 不放大 | `HeadLookController.cs:39–80` | ❌ |
| **TraversalHandIK（Controller + Rig）** | 每 frame（6.5） | **1×** | **0** —— 全部消費 committed plan，零查詢 | `OnAnimatorIK` 套 hand goal；**只在 traversal 期間有權重** | 🟡 **需驗**：`UpdateDebug()` 在**非 traversal 時也會跑**（只是立刻早退）；面板文字路徑每幀 6 段字串插值（`drawTraversalPanelText: 0`，目前關閉） | 🟡 `ActiveTraversalPlan` 是**大型 readonly struct**（6 個 knot ＋ contact targets），每次屬性存取整份複製 | 無 | 不放大 | `TraversalHandIKController.cs:186–240, 530–566` | 🟡 中（只在 traversal 窗內） |
| **TraversalState / SelectionPolicy / AnimationFitter** | **事件觸發**（`JumpRequested` 那一幀） | 1× | 0（只讀 Probe 已算好的 Candidate） | — | 🟢 無 | 🟢 **`CanEnter` 第一行就是 `if (!data.Intent.JumpRequested …) return false`** ⇒ 每幀遍歷 registry 時**不會跑進重邏輯** | 🟡 進入 traversal 那一幀建 warp plan（一次性） | 不放大 | `TraversalState.cs:110–112` | ❌ |
| **JumpState / RollState / HurtState / DeathState / Idle / Move** | State active 時 | N+1 | 0（位移全部委派 MotionDriver） | — | 🟢 無（`AnimationKey` 已由 **A23 回歸測試**守住不得每次配置） | 極低 | 無 | 線性，極小 | `States/*.cs`；`ArchitectureRegressionTests.cs:856` | ❌ |
| **ActionState** | State active 時 | N+1 | **0 / 1**：`RefreshEffectContext` → `IAimSource.TryGetAimPoint()`（**以 `Time.frameCount` 去重**） | 切 phase 時 `Play()` | 🟢 無 | 低 | 無 | 線性 | `ActionState.cs:355–397, 499–530`；`AimResolver.cs:47–58` | ❌ |
| **AimResolver** | **被詢問時**（frame-deduped） | **1×** | **≤1 `Physics.Raycast`／幀**（60 m，`aimRayMask`） | — | 🟢 無 | 極低 | 無 | 不放大 | `AimResolver.cs:47–82` | ❌ |
| **ThirdPersonCamera** | 每 frame `LateUpdate` | **1×** | **1 `Physics.SphereCast`**（防穿牆） | — | 🟢 無 | 低。`ApplyTargetProximitySuppression` 只在狀態翻轉時寫 Renderer（原碼明確如此設計） | 無 | 不放大 | `ThirdPersonCamera.cs:125–200, 256` | ❌ |
| **MeleeHitboxSink** | **事件觸發**（`OnTriggerEnter`） | N+1 | 由 PhysX trigger 驅動，非主動查詢 | — | 🟢 無 | 低 | 無 | 線性（trigger pair 數） | `MeleeHitboxSink.cs:87` | ❌ |
| **GroundEffectSink（AoE 法術）** | **事件觸發**（Action release） | 1× | **每次施放**：`RaycastNonAlloc` ×1 ＋ `OverlapCapsuleNonAlloc` ×1 ＋ 每個地面命中 1 次 `GetComponentInParent` | — | 🔴 **`Instantiate(effectPrefab)` ＋ 延時 `Destroy`**（無池） | 低 | 🔴 **每次施放一次 Instantiate/Destroy**；**首次施放**另含 prefab 反序列化＋材質／shader variant 初始化 | 隨同時存在的 AoE 數 | `GroundEffectSink.cs:161, 216–217, 241` | ✅（Spell spam 場景） |
| **ThrownProjectile / Emitter** | **`FixedUpdate`**，每顆飛行中的投射物 | **每顆投射物** | **1 `SphereCastNonAlloc`／FixedUpdate**（buffer `SweepBufferSize`）＋每個命中候選 1 次 `GetComponentInParent<CharacterHealth>()` | — | 🔴 **確認有配置**：`ExtinguishVisual()` 的 `GetComponentsInChildren<ParticleSystem>(true)` **回傳新陣列**（`ThrownProjectile.cs:142`）；另 `Instantiate` ×2（投射物、impact VFX） | 低 | 🔴 **每發 Instantiate ×2 ＋ Destroy ×2–3**；首發另含 VFX／shader 初始化 | 隨同時飛行數；**FixedUpdate ⇒ 低幀率時每幀跑多次** | `ThrownProjectile.cs:142, 165–232, 248`；`ThrowProjectileEmitter.cs:54` | ✅（Spell spam 場景） |
| **WorldSpaceHealthBar** | 每 frame `LateUpdate` | **N（每敵人）** | 0 | — | 🟢 無 | 極低（`Camera.main` **已快取**；不可見時早退） | 無 | 線性，極小 | `WorldSpaceHealthBar.cs:77–110` | ❌ |
| **PlayerHud** | 每 frame `LateUpdate` | 1× | 0 | — | 🟡 需驗（HUD 文字更新路徑） | 低 | 無 | 不放大 | `PlayerHud.cs:87–120` | ❌ |
| **RuntimeDebugPanel** | 每 frame `Update`／`LateUpdate`；`OnGUI` **僅面板開啟時** | 1× | 0 | 讀 `DebugSelectedStateKey`／`DebugDominantClipName`（原碼註明只在 dominant leaf 變更時才取 `clip.name`） | 🔴 **IMGUI `GUILayout.*` 每幀配置**（面板開啟時） | 🟡 `FindObjectsByType` ×4 —— 🟢 **只在開面板／切頻道時，不是每幀** | 🟡 開面板那一幀 4 次全場景掃描 | 不放大 | `RuntimeDebugPanel.cs:62, 116–147, 257–270, 368` | 🟡（只在 Editor / Dev Build） |
| **Debug 可視化（Traversal / FootIK / Capsule 環 / Gizmos）** | 每 frame，`#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` | 見 §5 | **0**（原碼紀律：「**記錄，不要重算**」——debug 一律讀已算好的快照） | 無 | 🔴 面板文字路徑每幀字串插值（目前 authored 為關） | 🟡 見 §5 | 🟡 LineRenderer 池首次建立 | 🟡 見 §5 | 見 §5 | ✅（**必須同時量 Debug ON / OFF**） |

---

## §2 深入：Traversal（使用者點名 #1）

### §2.1 逐 phase 的查詢次數

🟢 **Code-proven.** 常數來自 `TraversalProbe.cs` 與 `X Bot.prefab` 的 authored 值：
`scanStep 0.1`、`scanCeiling 2.7`、`forwardScanDistance 1.25`、`vault1mMaxDepth 0.6`、
`CharacterController.stepOffset 0.3`、`radius 0.32`、`MaximumScanRayCount 64`、`SamplesPerSegment 8`。

| Phase | 觸發條件 | 查詢明細 | 合計 |
|---|---|---|---|
| **空中**（`!data.IsGrounded`） | `Tick` 早退（`TraversalProbe.cs:325–351`） | — | **0** |
| **著地・面前空曠**（無任何命中） | 前向掃描跑滿 | `Physics.Raycast` × `(2.7−0.3)/0.1 + 1` | **25** |
| **著地・面前是實心牆**（命中後續掃到牆頂） | 前向掃描 ＋ 頂面 ＋ 深度 | 前向 ≤25、top 1、depth 1 | **≤27** |
| **著地・偵測到可攀候選**（完整路徑） | 前向 ＋ top ＋ depth ＋ landing 續掃 ＋ ledge 區間 ＋ 終點膠囊 ＋ corridor | 前向 ≤25<br>top `Raycast` 1<br>depth `Raycast` 1<br>**landing 續掃 `Raycast` ≈ 12**（`(1.86−0.6)/0.1`，上限由 bake 推導的 `EffectiveLandingScanDepth`）<br>**ledge 區間 `Raycast` ≤16**（雙向各 `max(0.75, r×2)/0.1 = 8`）<br>終點 `CheckCapsule` 1<br>**corridor `CheckCapsule` 24**（3 段 × 8 取樣） | **≈ 80** |
| **traversal 執行中**（`TraversalState` active） | Probe 仍在 `Update` 順序 2.7 被呼叫，但 `data.IsGrounded` 在 committed 段為 `false`（原碼 §`SyncGroundedState` 註解明載） | 早退 | **≈ 0** |

⚠️ **注意最後一列的反直覺結論**：**執行 traversal 的那段反而最便宜**。
最貴的是「站在牆前面、還沒按跳」——也就是**玩家最常待的那個狀態**。

### §2.2 三個 code-proven 的觀察

🟢 **① 「明明沒在 Traversal，仍然一直掃」——是的，而且是刻意的。**
`TraversalProbe.cs:324` 的原碼註解寫得很明白：
> `V3: sensing is continuous while grounded. JumpPressed is the intent boundary; stationary characters still receive a current geometry snapshot from facing.`

這是**設計決定，不是疏漏**——面板要在按跳之前就顯示「這面牆爬不爬得上去」。
本文件不主張改它；只指出**它的成本是每幀 25–80 次查詢，而且完全獨立於玩家有沒有攀爬意圖**。

🟢 **② `probeMinSpeed` 是一個從未被用作閘門的欄位。**
`grep -rn "ProbeMinSpeed"` 全 repo 只有四處：宣告、getter、建構子參數、以及
`TraversalClassifier.cs:152` 的一次 `IsFinite()` **有效性檢查**。
**沒有任何地方拿它跟實際速度比較。** authored 值 `0.1` 目前不影響任何行為。
⇒ 這條值得記下來，不是因為效能，而是因為**一個看起來像節流閥的設定其實沒有接線**。

🟢 **③ 有重複 probe，但是刻意的且各自有讀者。**
- `depth probe`（`:455`）與 `landing 續掃`（`:496`）打的是同一個方向上的不同深度——前者做**分類**證據（二元：是不是實心平台），後者找**落腳點**。原碼 `:475–483` 明確說明為何不能合併。
- `終點 CheckCapsule`（`:548`）與 `corridor 最後一段`（`:779` 的第 8 個取樣）**幾何上非常接近**——前者用 `destinationQueryRoot`，後者 lerp 到 `exitRoot`（＝同一個點）。🟡 **這一對看起來確實重複**，但確認是否真的等價需要在執行期比對兩者的輸入，**靜態分析不足以下結論**。

### §2.3 前向掃描的早退邏輯

🟢 值得專門記一筆，因為原碼 `:399–415` 自己標註了成本：
2026-09-20 修掉「懸空板偵測不到」時，把「第一根沒打到就 break」改成 `if (!hasForwardHit) continue`。
原碼註解：
> ⚠️ 代價：面向空曠處時由 1 根射線變成掃滿整個 ceiling（0.3→2.7，24 根）。

⇒ **面向空曠處的成本從 1 條射線變成 25 條，是一次已知且已文件化的取捨。**
這在單人場景不痛，但它是「玩家 99% 時間所處的狀態」的成本，**值得在 Profiler 上實際看一眼它到底是 0.01 ms 還是 0.3 ms**。

---

## §3 深入：Foot IK（使用者點名 #2）

### §3.1 每隻腳幾次 cast

🟢 **Code-proven**（`FootIKController.cs:224–318`，authored 值來自兩個 prefab）：

| 條件 | 左腳 | 右腳 | 合計 |
|---|---|---|---|
| `ikAllowed == false`（空中／`BlockIK`） | 0 | 0 | **0** |
| ankle ray 落空 | 1 | 1 | **2** |
| ankle 命中 ＋ `UseTwoPointSampling: 1`（**兩個 prefab 皆是**） | 1 + 2 | 1 + 2 | **6** |

閘門是 `bool ikAllowed = data.IsGrounded && !data.Arbitration.BlockIK;`（`:159`）。
🟢 原碼註解明載 **`BlockIK` 目前沒有任何 `IArbiterSource` 會抬起它，恆為 false** ⇒ 實務上閘門只剩 `IsGrounded`。

### §3.2 四個 scaling 事實

🟢 **① 所有角色每幀都執行。** 敵人 prefab（Y Bot）帶 `FootIKController` + `FootIKRig`。
30 敵人 + 玩家全部著地 ⇒ **186 raycast/frame** 純粹來自 Foot IK。

🟢 **② 沒有任何距離剔除。** 全檔 grep 不到距離／可見性／LOD 判斷；閘門只有 `IsGrounded` 與 `BlockIK`。
⇒ **場景另一端、螢幕外、100 m 遠的敵人，一樣每幀 6 條射線。**

🟢 **③ 射線遮罩是 Everything。** 兩個 prefab 的 `settings.GroundLayers` 都是 `m_Bits: 4294967295`。
⇒ 每條射線都對**全部 32 層**做 broad-phase，包含其他角色的 collider。

🔴 **④ Animator IK callback 成本無法靜態評估。** `FootIKRig.OnAnimatorIK` 每次做：
4× `GetIKPosition/Rotation`、2× `leftFeetBottomHeight`/`rightFeetBottomHeight`、
1× `bodyPosition` 讀改寫、8× `SetIK*`。
這些都是 native 呼叫，**成本在 Unity 內部**。
⚠️ 更重要的是：**開啟 IK pass 本身**（`SetApplyAnimatorIK(layer, true)`）會讓 Animancer/Playables 在該層多跑一個 evaluation pass ——
**這個成本不在本專案的程式裡，只能由 Profiler 的 `Animators.ProcessGraph` / `MonoBehaviour.OnAnimatorIK` 量到。**

### §3.3 「之後加 slope / toe / heel probe 會怎麼放大」

🟢 **heel / toe probe 已經在了**（`UseTwoPointSampling: 1`），所以「加上去」的問題已經發生過一次：
從 ankle-only 的 **2 rays** 變成 **6 rays**，是 **3 倍**。

用同一個算式外推（🟡 推導，非量測）：

| 假想擴充 | 每角色 rays/frame | 相對現況 | 30 敵人 + 玩家 |
|---|---|---|---|
| 現況（ankle + heel + toe） | 6 | 1.0× | 186 |
| ＋每腳 1 條 slope 法線取樣 | 8 | 1.33× | 248 |
| ＋每腳改成 4 點腳掌多邊形 | 10 | 1.67× | 310 |
| ＋改用 `SphereCast` 取代 `Raycast`（更穩的斜坡法線） | 6（但每條更貴） | 🔴 **需量測**——`SphereCast` 的 broad/narrow phase 成本不是 `Raycast` 的常數倍 |

⚠️ **這張表的用途不是反對加 probe**，而是指出：
**在 `GroundLayers = Everything` 且沒有距離剔除的前提下擴充，倍率會直接吃在 N 上。**

---

## §4 深入：Enemy AI（使用者點名 #3）

### §4.1 ⭐ 有沒有 N enemies × N targets？——**沒有。**

🟢 **Code-proven，而且是這次審查最值得記下的好消息。**

| 元件 | 目標從哪來 | 掃描？ |
|---|---|---|
| `AIInputSource`（攻擊意圖） | `[SerializeField] private Transform target`（`:29`） | ❌ **零掃描**。只做一次 `offset.sqrMagnitude <= range²`（`:110`） |
| `AIMovementSource`（移動意圖） | 同一個序列化 `target`，**共用同一份真相** | ❌ **零掃描** |
| `CharacterFacingSource`（朝向） | 讀黑板 `CombatContext.TargetPosition` | ❌ **零掃描** |

原碼 `AIMovementSource.cs:130–133` 明確解釋為什麼不另建 target source：
> 敵人的移動與戰鬥語境共用同一個 target 真相；另建 context source 只會複製 target reference
> ⇒ 目標會出現第二份真相（ADR-007 D3 補充條款明文禁止「任何其他系統自建 isInCombat 旗標或第二份 target 清單」）。

⇒ **一條架構規則（單一真相來源）順帶消滅了 O(N²) 的 sensing 成本。**
唯一的「掃描者」是玩家身上的 `PlayerCombatContextSource`（**1 個掃描者，不是 N 個**）。

⚠️ **代價寫在同一處**：目前的 target 是 Inspector 硬接的，**不支援執行期換目標**。
未來若要做「敵人自己找目標」，**那才是引入 N×N 的時刻**——現在還沒有。

### §4.2 NavMesh 成本

🟢 **Code-proven**（`AIMovementSource.cs`）：

| 呼叫 | 條件 | 頻率 |
|---|---|---|
| `_agent.nextPosition = transform.position` | 每幀（在網上且交戰中） | N/frame |
| **`_agent.SetDestination(targetPosition)`** | **每幀，無條件**（`:197`） | **N/frame** |
| `NavMesh.Raycast`（`IsStrafeStepWalkable`） | Strafe 模式，優先側不可走時再問反側（`:214–216`） | **1–2/frame/enemy** |
| `NavMesh.SamplePosition` | **僅離網時**（`:170`） | 0 in steady state |

🟡 **`SetDestination` 每幀是主要風險**。Unity 對「目的地未變」有內部短路，但**玩家在動時目的地每幀都變** ⇒ 每幀一次非同步路徑請求。
🔴 **成本落在 NavMesh job 系統內，靜態分析看不到。** Profiler 的 `Navigation` 模組 / `NavMeshManager.Update` 是唯一答案。

🟡 **Strafe 是穩態模式。** 原碼 `:207–212` 指出交戰帶 `[min, max]` 很窄、打鬥期間幾乎全程在 Strafe
⇒ **那 1–2 次 `NavMesh.Raycast` 實務上接近「每幀都有」**，不是罕見分支。

### §4.3 敵人的 8-way locomotion

🟢 敵人有 `LocomotionModel` ⇒ 每幀 3 次 `SetFloat`（MoveSpeed / MoveX / MoveZ），與玩家完全同一條路徑。
🔴 **blend tree 的實際評估成本不在本專案程式裡。**
2D mixer 每幀要對 N 個子節點做權重內插並採樣 N 個 clip ——
這是 `Animators.ProcessGraph` 裡的成本，**只能 Profile**。§B 有要看什麼。

---

## §5 深入：Debug（使用者點名 #5）

### §5.1 編譯條件盤點

🟢 **Code-proven.** 這是本輪最乾淨的一塊——編譯條件用得很有紀律：

| 檔案 | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` | `#if UNITY_EDITOR` | Release 有沒有？ |
|---|---|---|---|
| `TraversalProbe.cs` | 8 處 | 3 處 | ❌ 完全移除 |
| `FootIKController.cs` | 8 處 | 3 處 | ❌ 完全移除 |
| `MotionDriver.cs` | 9 處 | 7 處 | ❌ 完全移除 |
| `TraversalHandIKController.cs` | 5 處 | 1 處 | ❌ 完全移除 |
| `RuntimeDebugPanel.cs` | **整個類別包在 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`**（`:62`） | — | ❌ 完全移除 |

⇒ ✅ **對使用者問題「是不是 Development Build / Editor 很重、Release 沒問題」的直接回答：是，而且這正是預期行為。**
所有 debug 可視化的**編譯條件是 `UNITY_EDITOR || DEVELOPMENT_BUILD`，不是 `UNITY_EDITOR`**
⇒ **Development Build 會完整背上 debug 成本。**

⚠️ **這對 profiling 方法論有直接後果**：
**Unity Profiler 連的通常就是 Development Build。**
⇒ **你量到的每一個數字都含 debug 成本，除非你先把開關關掉。**
這正是 §D benchmark matrix 一定要有 Debug ON / Debug OFF 兩排的理由。

### §5.2 Debug 成本的實際組成

🟢 **Code-proven**，authored 開關來自兩個 prefab：

| 通道 | authored 值 | 每幀做什麼 | 成本性質 |
|---|---|---|---|
| **Traversal runtime lines** | X Bot `drawTraversalRuntimeLines: 1` ✅ **開** | 最多 **96 個 LineRenderer** ＋ **32 個 shape LineRenderer**（每個 24 段環）的 `SetPositions` ＋ `enabled` 切換 | 🟡 **128 個 LineRenderer 的每幀更新 ＋ draw call**。**⚠️ `AppendTraversalKeyHeights` 是無條件呼叫的**（`:942`），連沒有候選時也畫 |
| **Traversal 面板文字** | `drawTraversalPanelText: 0` ❌ **關** | （若開啟）每幀 `BuildCompactTraversalPanel` **3–4 段字串插值** ＋ `TextMesh.text` 賦值（觸發 mesh 重建）＋ `Camera.main` | 🔴 **確認會配置**——但目前 authored 為關 |
| **Foot IK runtime lines** | **X Bot `1` ✅ ／ Y Bot `1` ✅**（**敵人也開**） | 最多 **48 個 LineRenderer**／角色 | 🔴 **這一條會隨敵人數線性放大**：30 敵人 ⇒ 最多 1488 個 LineRenderer |
| **Capsule offset 環** | **X Bot `1` ✅ ／ Y Bot `1` ✅** | `MotionDriver.LateUpdate` 每幀更新 **5 個環 × 32 段** | 🔴 **線性放大**：30 敵人 ⇒ 155 個環 |
| **Hand IK debug** | `drawTraversalPanelText: 0` | `UpdateDebug()` **無論如何都會被呼叫**，但內部立刻早退 | 🟢 可忽略 |
| **Scene Gizmos**（`OnDrawGizmos` ×5 個檔案） | `drawTraversalSceneGizmos: 1`、`drawFootIKSceneGizmos: 1`、`drawOverlapDebug: 1` | 只在 **Editor Scene View 可見時**繪製 | 🟢 **不影響 Build，也不影響 Game View 專注模式** |
| **RuntimeDebugPanel `OnGUI`** | 預設關（按 `` ` `` 開） | IMGUI `GUI.Box` + 11 行 `GUILayout.Label` | 🔴 **IMGUI 每幀配置**，但需手動開啟 |

### §5.3 ⭐ 一條值得記下的紀律

🟢 原碼 `TraversalProbe.cs:1041–1044` 自己寫下的規則：
> ⛔ 只讀，不重算。全部取自 authored `settings` 與**已經算好的** `_debugSnapshot`。
> 這裡沒有任何 `Raycast`／`CapsuleCast`——`docs/18` §1「**記錄，不要重算**」：
> debug 畫出來的必須是**真正做決定的那份資料**，否則它會變成第二個看起來很權威的來源。

⇒ **Debug 的成本是「繪製」，不是「查詢」。** 這是很好的性質：
關掉 debug **不會**改變任何 gameplay 查詢次數 ⇒ **Debug ON/OFF 的差值就是純繪製成本，可以乾淨地分離出來。**

### §5.4 gameplay cost vs debug-only cost 的明確分界

| | Gameplay cost（Release 也有） | Debug-only cost（Release 沒有） |
|---|---|---|
| Physics 查詢 | **全部** | **0** —— 🟢 已由「記錄，不要重算」紀律保證 |
| Animator 呼叫 | 全部 | 0 |
| LineRenderer 更新／draw call | 0 | **全部**（128 ×1 玩家 ＋ (48+5環) ×(N+1)） |
| 字串配置 | 0 | 面板文字路徑（目前 authored 為關） |
| IMGUI | 0 | `RuntimeDebugPanel`（手動開啟） |
| `FindObjectsByType` | 0 | 開面板／切頻道那一幀 ×4 |

---

## §6 深入：Animation / Animancer（使用者點名 #4）

### §6.1 「真正 active 的 runtime graph」，不是 asset 數量

🟢 **Code-proven 的部分**：

| 事實 | 證據 |
|---|---|
| **Base layer 恆為 1 層** | `AnimancerFacade.cs:241` `animancer.Layers[0].Play(...)` |
| **Overlay layer 按需建立、用完 fade out** | `:244–268`。`_activeOverlayLayerIndex` 為 `-1` 時不存在第二層 ⇒ **沒有常駐的 upper-body layer** |
| **每幀對 Animancer 的呼叫：3 次 `SetFloat`** | `LocomotionModel.cs:148–153` |
| **`Play()` 只在 animation key 變更時呼叫** | `CharacterPipelineRunner.cs:491–497`（`string.Equals(..., Ordinal)` 比對） |
| **Foot IK pass 需要開啟對應層的 `ApplyAnimatorIK`** | `AnimancerFacade.cs:322–334`，由 `FootIKController.Start` 一次性設定 |
| **`Animator.CullingMode` = `AlwaysAnimate`（玩家）** | `X Bot.prefab:2714` `m_CullingMode: 0` |
| **敵人的 Animator 沒有 culling mode override** | `Y Bot.prefab` 無 `m_CullingMode` 條目 ⇒ 沿用 FBX 匯入的預設（`AlwaysAnimate`）。🟡 **建議在 Editor 確認一次** |

🔴 **無法靜態評估的部分**——這些是整份審查裡**最需要 Profiler** 的項目：

1. **2D mixer 的子節點數與採樣成本。** `Locomotion.asset` 的 mixer 有幾個節點、每個節點權重非零時採樣幾個 clip，決定 `ProcessGraph` 的實際成本。靜態讀 asset 只能得到節點數，**得不到執行期實際被採樣的數量**。
2. **Humanoid retargeting 成本。** 兩個 prefab 都是 Humanoid rig ⇒ 每幀有一次 muscle-space → bone-space 轉換，成本與骨骼數成正比。
3. **`AlwaysAnimate` 的真實代價。** 螢幕外的敵人是否仍完整評估 graph、寫回所有 Transform，**必須從 Profiler 的 `Animators.ProcessGraph` / `Animators.WriteJob` 確認**。
4. **`Parameters.SetValue(string, float)` 是否真的零配置。** 原碼註解聲稱走 intern 快取只在首次配置，**但那是 Animancer v8 的內部實作**。3 次/幀 × (N+1) 是一個值得在 GC Alloc 欄位盯一眼的數量級。

### §6.2 為什麼「asset 數量」不是答案

🟢 專案有 2D locomotion、8-way combat locomotion、upper-body spell layer、Hurt/Death、Jump family、stop animations、traversal animations。
但**同一時刻 active 的 graph 是**：
- base layer 1 層 × 1 個 transition（可能是 mixer）
- overlay layer 0 或 1 層（僅法術期間）
- IK pass（Foot IK 恆開；Hand IK 僅 traversal）

⇒ **stop animations、traversal animations、Hurt/Death 在非該狀態時完全不在 graph 上。**
Animancer 的 `TransitionAsset` 只在 `Play()` 時建立 state，fade 完成後舊 state 會被停用。
**資產數量 ≠ 執行期節點數。**

⚠️ 🟡 **但有一個例外值得 Profile**：`AnimancerFacade` 有 `_stateCache`（`Dictionary<string, AnimancerState>`），
播放過的 state **會留在 cache 裡**。這意味著 graph 上累積的 state 數量會隨**玩過的動作種類**單調增長，
而不是隨**當下播放的動作**。**Animancer 對已停用 state 的 `ProcessGraph` 成本，需要實測。**

---

## §A. 已從程式碼確認的問題（🟢 Code-proven）

> 「問題」在這裡指**可量化的成本或明確的接線缺口**，不一定代表「需要修」。
> 每一條都有 file:line。

---

### A1 — 🟠 **Medium** — Foot IK 的 ground raycast 遮罩是 `Everything`，且沒有任何距離剔除

- **模組**：Foot IK
- **問題**：`settings.GroundLayers` 在**兩個 prefab**上都是 `m_Bits: 4294967295`（Everything）。
  每個著地角色每幀發 6 條射線，每條都對全部 32 層做 broad-phase，**包含其他角色的 collider**。
  閘門只有 `IsGrounded`（`BlockIK` 恆為 false，原碼自述），**沒有距離、可見性或 LOD 判斷**。
- **為什麼**：Foot IK 是**純視覺修正**（`docs/29` §C 已明確界定它不改位置也不改接地）。
  螢幕外 100 m 的敵人，它的腳踝骨骼修正**沒有任何觀察者**。
- **對角色數的 scaling**：**嚴格線性，係數 6**。
  1 玩家 = 6；+1 敵人 = 12；+10 = 66；**+30 = 186 raycast/frame**。
  這是本專案**最大的單一線性放大源**。
- **檔案／method／line**：
  `Assets/Scripts/Presentation/IK/FootIKController.cs:311–314`（`RaycastGround`）
  `Assets/Scripts/Presentation/IK/FootIKController.cs:159`（`ikAllowed` 閘門）
  `Assets/Prefabs/X Bot.prefab:4531–4533`、`Assets/Prefabs/Y Bot.prefab`（`GroundLayers`）

---

### A2 — 🟠 **Medium** — `PlayerCombatContextSource` 的 soft-target 掃描每幀無條件執行，並對每個命中做 `GetComponentInParent`

- **模組**：Targeting / Combat Context
- **問題**：`TrySelectSoftTarget` **沒有任何 sticky 短路**——與黏性的 combat target 不同（後者有
  `if (_targetMarker == null)` 閘門，`:87`），soft target **每幀都重新 `OverlapSphereNonAlloc`**
  （r = 12 m，buffer 32），然後對**每一個命中的 collider** 呼 `GetComponentInParent<ActionRequestTarget>()`。
- **為什麼**：`OverlapSphereNonAlloc` 本身零配置（這點做對了），但
  `GetComponentInParent<T>` 是**向上走 Transform 階層 ＋ 逐層 `GetComponent`** 的 managed 呼叫。
  在 K 個候選上每幀重複，且 candidate 的階層深度不受控（角色 collider 通常掛在 Model 子物件下）。
- **對角色數的 scaling**：**O(K)／幀，K ＝ 12 m 內的 targetMask collider 數**。
  ⚠️ **不是 N²** —— 只有玩家一個掃描者。但**係數隨敵人聚集程度上升**，
  且 buffer 上限 32 意味著超過 32 個目標時會**靜默截斷**（正確性問題，非效能問題，一併記下）。
- **檔案／method／line**：
  `Assets/Scripts/Core/Combat/PlayerCombatContextSource.cs:88–94`（無條件呼叫）
  `Assets/Scripts/Core/Combat/PlayerCombatContextSource.cs:134–152`（Overlap ＋ `GetComponentInParent`）
  `Assets/Scripts/Core/Combat/PlayerCombatContextSource.cs:14`（`CandidateBufferSize = 32`）

---

### A3 — 🟡 **Low（成本）／🟠 Medium（接線）** — `TraversalProbe` 每幀 25–80 次查詢，且 `probeMinSpeed` 從未被用作閘門

- **模組**：Traversal / Ground & obstacle probing
- **問題**：
  **(a)** Probe 在**著地期間每幀全跑**，與玩家有無攀爬意圖無關（§2.1 表）。
  面向空曠處 25 次 `Raycast`；面向可攀候選約 **80 次**（含 24 次 `CheckCapsule`）。
  **(b)** `probeMinSpeed`（authored `0.1`）**在全 repo 沒有任何地方被用作速度閘門**——
  只有 `TraversalClassifier.cs:152` 的一次 `IsFinite()` 有效性檢查。
- **為什麼**：
  (a) 是**刻意的設計**（原碼 `:324` 註解說明面板需要在按跳前就有答案），**不是缺陷**；本文件只量化它。
  (b) 是真正的接線缺口：**一個看起來像節流閥的 Inspector 欄位其實不接任何東西**。
  任何未來想靠它降成本的人會得到「調了沒有變化」的結果。
- **對角色數的 scaling**：**不放大**（`TraversalProbe` 只在 X Bot 上）。
  這是為什麼嚴重度定在 Low——**它貴，但它是常數項，不是斜率**。
- **檔案／method／line**：
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:293`（`Tick` 入口）
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:364–416`（前向掃描迴圈，上界 `MaximumScanRayCount = 64`，實際由 `scanCeiling 2.7 / scanStep 0.1` 決定 = 25）
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:487–511`（landing 續掃）
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:693–710`（ledge 區間，≤16）
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:763–787`（corridor，3 × 8 = 24 `CheckCapsule`）
  `Assets/Scripts/Core/Environment/TraversalProbeSettings.cs:18`（`probeMinSpeed` 宣告）
  `Assets/Scripts/Core/Environment/TraversalClassifier.cs:152`（唯一使用處，僅 `IsFinite`）

---

### A4 — 🟠 **Medium** — Debug 可視化在 Development Build 完整存在，且 **Foot IK / Capsule 通道在敵人 prefab 上是開的**

- **模組**：Debug / runtime overlay
- **問題**：所有 debug 的編譯條件是 `UNITY_EDITOR || DEVELOPMENT_BUILD`（**不是** `UNITY_EDITOR`）。
  而 authored 開關中，**`drawFootIKRuntimeLines: 1` 與 `drawCapsuleOffsetDebug: 1` 在 Y Bot（敵人）prefab 上都是開的**。
- **為什麼**：這代表 **Profiler 量到的每一個數字都含 debug 成本**（Profiler 通常連 Development Build）。
  且 Foot IK 的 48 個 LineRenderer ＋ Capsule 的 5 個 32 段環是**每角色的**：
  30 敵人 ⇒ 最多 **1488 + 155 個 LineRenderer 每幀更新**。
- **對角色數的 scaling**：**線性，且是 debug-only 的線性**——
  這正是最容易誤導 profiling 結論的形狀：**「加敵人就變慢」可能有一大半來自 debug 繪製。**
- **檔案／method／line**：
  `Assets/Prefabs/Y Bot.prefab:619`（`drawFootIKRuntimeLines: 1`）
  `Assets/Prefabs/Y Bot.prefab:438`（`drawCapsuleOffsetDebug: 1`）
  `Assets/Scripts/Presentation/IK/FootIKController.cs:485`（`new LineRenderer[48]`）
  `Assets/Scripts/Presentation/Motion/MotionDriver.cs:1804, 1821–1843`（5 環 × 32 段，每幀 `LateUpdate`）
  `Assets/Scripts/Core/Environment/TraversalProbe.cs:871, 1290–1291`（96 + 32 LineRenderer，24 段環）

---

### A5 — 🟡 **Low** — 每個角色在 `Start` 建立 10 顆 `AudioSource` GameObject

- **模組**：Audio
- **問題**：`AudioController.audioSourceCount = 10`（**兩個 prefab 皆為 10**），
  `AudioSourcePool` 建構時為每一顆 `new GameObject($"FootstepAudioSource_{i}")` ＋ `AddComponent<AudioSource>()`。
- **為什麼**：閒置 `AudioSource` 的每幀成本很低（`playOnAwake = false`），
  但 **30 敵人 = 310 顆 AudioSource**，會接近 Unity 的 Virtual Voice 預設上限（512），
  且原碼自己標註了淘汰規則的代價：
  > ⚠️ 戰鬥音效共用同一個池之後，這條淘汰規則就有代價了：一發長的死亡音可能被後續的腳步搶掉。
- **對角色數的 scaling**：**線性，係數 10**（GameObject 數與 AudioSource 數）。
  同時是**生成 spike**：N 隻敵人同時生成 ⇒ 10N 個 GameObject 在同一幀建立。
- **檔案／method／line**：
  `Assets/Scripts/Presentation/Audio/AudioController.cs:45, 86`
  `Assets/Scripts/Presentation/Audio/AudioSourcePool.cs:48–60`
  `Assets/Prefabs/X Bot.prefab:4517`、`Assets/Prefabs/Y Bot.prefab:591`

---

### A6 — 🟡 **Low** — 投射物／AoE 完全沒有物件池，且 `ExtinguishVisual` 確認會配置

- **模組**：Projectile / Spell
- **問題**：
  - `ThrowProjectileEmitter.cs:54` `Instantiate(projectilePrefab, …)`
  - `ThrownProjectile.cs:248` `Instantiate(impactEffectPrefab, …)` ＋ `Destroy`
  - `GroundEffectSink.cs:216–217` `Instantiate(effectPrefab, …)` ＋ `Destroy`
  - `ThrownProjectile.cs:142` **`GetComponentsInChildren<ParticleSystem>(true)` 回傳新陣列 ⇒ 確認 GC**
- **為什麼**：每發法術 = 2 次 `Instantiate` + 2–3 次 `Destroy` + 1 次陣列配置。
  **首發**另含 prefab 反序列化、材質實例化、shader variant 編譯——這是典型的「第一次施放卡一下」。
- **對角色數的 scaling**：**與角色數無關，與施放頻率成正比。**
  `FixedUpdate` 驅動意味著低幀率時每 render frame 會跑多次掃掠。
- **檔案／method／line**：見上（四處）

---

### A7 — 🟡 **Low** — `AIMovementSource` 每幀無條件 `SetDestination`

- **模組**：Enemy AI
- **問題**：`_agent.SetDestination(targetPosition)` 在 `ProduceIntent` 中**每幀無條件呼叫**（`:197`），
  目標是移動中的玩家 ⇒ 目的地每幀都不同 ⇒ 每幀觸發一次路徑重算請求。
- **為什麼**：Unity 對相同目的地有短路，**但這裡不會命中**。
  這是 NavMesh 成本的主要來源，而不是 `NavMesh.Raycast`。
- **對角色數的 scaling**：**線性**。路徑計算在 job 系統上，
  🔴 **實際成本與 NavMesh 複雜度（polygon 數、area cost）相關，靜態分析給不出數字。**
- **檔案／method／line**：`Assets/Scripts/Core/Movement/AIMovementSource.cs:196–197`

---

### A8 — 🟢 **Info（非問題，但值得記錄）** — 沒有發現的三件事

這三項是**特地去找、確認不存在**的：

| 找什麼 | 結論 | 證據 |
|---|---|---|
| **LINQ 在 runtime** | 🟢 **零** | `grep -rn "using System.Linq"` 在 `Core`/`Presentation`/`App` 零命中。**且有回歸測試守著**：`ArchitectureRegressionTests.A3_RuntimeScripts_DoNotUseLinq` |
| **N enemies × N targets 掃描** | 🟢 **不存在** | 見 §4.1。敵人的 target 是單一序列化 `Transform` |
| **執行期 `GetComponent` 在角色 tick 熱路徑** | 🟢 **幾乎零** | 所有 `GetComponent*` 都在 `Awake`/`Start`/`Resolve*`。唯二的執行期使用是 A2 的 soft-target 與碰撞回呼（事件驅動） |

⚠️ 另記一條**已經被 Profiler 抓到並修掉的先例**，值得作為方法論參考：
`ArchitectureRegressionTests.A23_StateAnimationKey_DoesNotAllocatePerAccess` 的註解寫著
> `Enum.ToString()` 每次呼叫都裝箱 ＋ 配置字串，於是熱路徑每角色每帧漏 40 B（玩家＋敵人 = 80 B/frame，**Profiler 實測**）。

⇒ **這個專案已經有一次「靜態看不出來、Profiler 抓到、然後寫成回歸測試」的完整循環。**
本文件 §C 的 marker 建議就是為了讓下一次也能走完同一個循環。

---

## §B. 高風險但不能只靠靜態分析確認（🔴 Profiler required）

> 這一節每一條都附「**要在 Profiler 看什麼**」——指到具體的模組與 marker 名稱。

---

### B1 — Animator / Animancer 的 graph 評估成本

**為什麼靜態分析不夠**：mixer 子節點數、實際被採樣的 clip 數、humanoid retargeting、
`AlwaysAnimate` 對螢幕外角色的影響——**全部在 Unity native 端**，本專案程式裡看不到。

**Profiler 要看什麼**：
| 位置 | Marker / 欄位 | 判讀 |
|---|---|---|
| CPU Usage → Timeline → Worker threads | `Animators.ProcessGraph` | 這是 blend tree 採樣的真身。**看它佔 frame 的比例，以及它隨敵人數的斜率** |
| 同上 | `Animators.WriteJob` | Transform 寫回。與骨骼數成正比 |
| Hierarchy view | `MonoBehaviour.OnAnimatorIK` | **Foot IK pass 的真實成本**。⚠️ 這個數字**不含**開啟 IK pass 讓 Animancer 多跑的 evaluation |
| Hierarchy view | `PlayerLoop → PreLateUpdate.DirectorUpdateAnimationBegin/End` | Playables graph 的整體窗口 |
| **對照實驗** | 把一半敵人移到螢幕外 | 若 `ProcessGraph` **不變** ⇒ 確認 `AlwaysAnimate` 生效且無剔除；若下降 ⇒ 有剔除 |
| **對照實驗** | 把敵人 `Animator.cullingMode` 改 `CullUpdateTransforms` | 差值就是 `AlwaysAnimate` 的代價。⚠️ **只是量測，不是建議**——culling 會改變動畫行為 |

---

### B2 — `CharacterController.Move` 的實際成本

**為什麼靜態分析不夠**：`Move` 內部是 PhysX 的 sweep + depenetration + slide 迭代。
成本取決於**接觸到的 collider 數、斜坡、階梯、與其他角色重疊**，
**同一行程式在空曠地與在牆角的成本可以差很多倍**。

**Profiler 要看什麼**：
| 位置 | 看什麼 | 判讀 |
|---|---|---|
| Hierarchy view | `CharacterController.Move` | 每角色每幀一次（`MotionDriver` 有 4 個呼叫點，但同幀只走一條） |
| Physics 模組 | `Physics.Processing` / `Physics.UpdateBodies` | CC 的 sweep 反映在這裡 |
| **對照實驗** | 敵人互相擠在一起 vs 散開 | CC 之間互推會顯著增加 depenetration 迭代 |
| **對照實驗** | 貼牆走 vs 空地走 | 量「最壞情況」而不是「平均情況」 |

⚠️ **這一項有一個本專案特有的加乘**：`MotionDriver.UpdateCapsuleOffset` 每幀改 `characterController.center`
（`:690–697`）。**動態改變 CC 幾何是否會讓 PhysX 重建內部狀態，靜態分析答不出來。**
→ 建議對照：`capsuleOffset.Enabled = false` vs `true` 的 `Physics.Processing` 差值。

---

### B3 — PhysX 查詢的真實單價

**為什麼靜態分析不夠**：§2／§3 給的是**查詢次數**，不是**查詢成本**。
一次 `Raycast` 的成本取決於 BVH 深度、遮罩命中的層數、射線長度、場景 collider 密度。
`CheckCapsule` 比 `Raycast` 貴多少，**不是常數倍**。

**Profiler 要看什麼**：
| 位置 | 看什麼 | 判讀 |
|---|---|---|
| Physics 模組 | **`Physics.Queries`（查詢次數）** | **先驗證 §2／§3 的靜態計數對不對**。若實測遠高於推算，代表有我沒找到的查詢路徑 |
| Physics 模組 | `Physics.Processing` | 總體物理時間 |
| Hierarchy view | 自訂 marker（見 §C） | **把查詢次數歸因到模組**——這是 Physics 模組給不了的 |
| **對照實驗** | Foot IK `GroundLayers` 由 Everything 改成只有 Ground 層 | 差值 = 寬遮罩的代價。⚠️ **只是量測** |

---

### B4 — `Dictionary<StateType, …>` 的 enum key 是否裝箱

**為什麼靜態分析不夠**：`EqualityComparer<TEnum>.Default` 在現代 .NET 與 IL2CPP 上有特化路徑不裝箱，
**但這取決於 scripting backend 與 Unity 版本**。專案有 6 個 enum-keyed Dictionary，
其中 `_priorityMap` 在 `EvaluateInterrupts` 裡**每角色每幀被查 ≈7 次**。

**Profiler 要看什麼**：
- **GC Alloc 欄位**：在 `FullBodyStateMachine.Tick` 的 marker 下看有沒有非零配置。
- 若非零：每幀每角色 ~7 次裝箱 × (N+1)，**這正是 A23 抓到的那種 40 B/frame 級別的漏**。
- 若為零：**這條完全不用管**，CPU 上 7 次 dict 查表可忽略。

**檔案**：`FullBodyStateMachine.cs:127`、`StateMachineConfigSO.cs:44–49`

---

### B5 — `Animancer.Parameters.SetValue(string, float)` 是否真的零配置

**為什麼靜態分析不夠**：原碼註解聲稱 `string → StringReference` 走 intern 快取只在首次配置，
**但這是 Animancer v8 的內部實作，不是本專案可證明的**。

**Profiler 要看什麼**：
- **GC Alloc 欄位**在 `LocomotionModel.Tick` 的 marker 下。
- 呼叫量是 **3 × (N+1) 次/幀**——如果每次漏哪怕 8 B，31 個角色就是 744 B/frame。

**檔案**：`AnimancerFacade.cs:306–313`、`LocomotionModel.cs:148–153`

---

### B6 — NavMesh path 計算的分佈（不是平均，是 P99）

**為什麼靜態分析不夠**：`SetDestination` 是非同步的——**成本不出現在呼叫的那一幀**，
而是散在 NavMesh job 上。且「目標不可達」（`PathInvalid`）會讓 agent **每幀重試完整搜尋**。

**Profiler 要看什麼**：
| 位置 | 看什麼 |
|---|---|
| CPU Timeline → Worker threads | NavMesh job 的**佔用寬度與分佈** |
| Hierarchy | `NavMeshManager.Update` |
| **對照實驗** | 讓玩家站到**不可達位置**（NavMesh 外／被 obstacle 切斷） | 這會觸發 `:255` 的 `PathInvalid` 分支，**每幀重算** |

---

### B7 — `AnimancerFacade._stateCache` 的單調增長

**為什麼靜態分析不夠**：`_stateCache` 保留所有播放過的 `AnimancerState`。
**Animancer 對已停用 state 的 `ProcessGraph` 成本需要實測**——
如果非零，那麼「玩得愈久愈慢」會是一個真實的長時間 session 效應。

**Profiler 要看什麼**：
- 長時間 session（把所有動作各觸發一次：走跑跳翻滾攻擊法術受擊 traversal）後，
  比對 `Animators.ProcessGraph` 與剛開場的數值。
- Memory Profiler：`AnimancerState` 實例數是否單調增長。

**檔案**：`AnimancerFacade.cs:38`

---

## §C. 建議新增的 Profiler Marker（⛔ 本輪不實作，只列清單）

> 設計原則：**marker 的邊界要對齊「誰擁有這段成本」**，否則 Hierarchy view 上看到的數字歸不了因。
> 本專案的管線順序（`docs/02` §2）已經是天然的 marker 邊界——**沿用它，不要自創切法。**

### C.1 骨幹 marker（對齊管線順序）

| Marker | 包住哪一段 | 為什麼是這個邊界 |
|---|---|---|
| **`IntentPipeline.Frame`** | `CharacterPipelineRunner.Update` 全部（順序 0.5–5） | **最外框**。一支就能回答「整個角色的 gameplay tick 佔多少」。⚠️ 建議附角色名以區分玩家／敵人 |
| **`IntentPipeline.Presentation`** | `CharacterPipelineRunner.LateUpdate` 全部（順序 6–7） | 與上面成對 ⇒ 兩者相加就是「一個角色的全部成本」 |
| `IntentPipeline.Input` | 順序 1–2（`FetchRawInput` + `ProcessIntents`） | 應該極小；若不小代表輸入系統有異常 |
| `IntentPipeline.MovementIntent` | 順序 2.5（`_movementIntentSource.ProduceIntent`） | **玩家與敵人在這裡分岔**——敵人的 NavMesh 成本全在這格 |
| `IntentPipeline.Model` | 順序 3（`_movementModel.Tick`） | 含 3 次 `SetFloat` ⇒ **B5 的 GC Alloc 看這一格** |
| `IntentPipeline.FSM` | 順序 4（`_stateMachine.Tick`） | **B4 的 enum 裝箱看這一格** |

### C.2 子系統 marker

| Marker | 包住哪一段 | 要回答什麼 |
|---|---|---|
| **`Traversal.Probe`** | `TraversalProbe.Tick` **整個方法**（`:293–645`） | §2.1 那張表的實測版。**這一格的 P99 就是「站在牆前」的成本** |
| `Traversal.Probe.ForwardScan` | `:364–416` 的掃描迴圈 | 分離「空曠 25 條」與「命中後早退」兩種形狀 |
| `Traversal.Probe.Corridor` | `ValidateFixedCapsuleCorridor`（`:719–760`） | **24 次 `CheckCapsule` 是單一最密集的查詢叢集**，值得獨立 |
| `Traversal.Probe.LedgeInterval` | `MeasureLedgeInterval`（`:678–717`） | ≤16 條射線 |
| **`Traversal.IK`** | `TraversalHandIKController.Tick` | ⚠️ **要包含早退路徑**——這樣才看得出「非 traversal 期間它到底有沒有成本」 |
| **`FootIK.Solve`** | `FootIKController.Tick`（`:136–208`） | **A1 的實測版。這一格 × (N+1) 是最大的線性項** |
| `FootIK.Sample` | `SampleGround` ×2（含 6 條射線） | 把「查詢」與「求解」分開 —— 前者是 PhysX，後者是我們的數學 |
| `FootIK.AnimatorPass` | `FootIKRig.OnAnimatorIK` | 與 B1 的 `MonoBehaviour.OnAnimatorIK` 對照 |
| **`EnemyAI.Tick`** | `AIMovementSource.ProduceIntent` + `AIInputSource.FetchRawInput` | ⚠️ 兩者在管線上**不相鄰**（順序 1 與 2.5），若 Profiler 允許可拆成兩支 |
| `EnemyAI.NavMesh` | `SetDestination` + `IsStrafeStepWalkable` + `SamplePosition` | **B6 的歸因點** |
| **`AimResolver.Resolve`** | `AimResolver.Resolve`（不含 frameCount 去重的外層） | 驗證「每幀最多一次」的去重真的生效 |
| **`MotionDriver.Move`** | `ExecuteBaseMovement` / `ExecuteVerticalOnlyMovement` / `ExecuteBakedCurveMovement` 的共同出口 | **B2 的歸因點** |
| `MotionDriver.CapsuleOffset` | `UpdateCapsuleOffset`（含 landmark 取樣 + `CapsuleCast`） | 分離「動態膠囊」的成本，供 B2 的對照實驗用 |
| `CombatContext.SoftTarget` | `TrySelectSoftTarget` | **A2 的實測版**。看它隨敵人數的斜率 |
| `Footstep.Detect` | `FootstepDetector.Evaluate` | 含 `SurfaceResolver` cache miss 的成本 |
| **`Presentation.Debug`** | ⚠️ **把每個 `UpdateRuntimeDebugLines` / `UpdateCapsuleDebugRing` / `UpdateDebug` 包起來** | **這是 §5 與 §D 的關鍵**——有了它，Debug ON/OFF 的差值不用靠兩次跑，一次就能讀出來 |

### C.3 兩條紀律

🟢 **① Marker 必須包含早退路徑。**
例：`Traversal.IK` 如果只包在早退之後，就永遠回答不了「非 traversal 期間它有沒有成本」——
而那正是我們想知道的（§1 表裡 `UpdateDebug()` 無條件呼叫那一條）。

🟢 **② Debug marker 自己要在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 裡。**
否則 marker 本身會變成 Release 的成本，而且會量到一個 Release 裡不存在的東西。
⚠️ 但 **gameplay marker 應該在 Release 也存在**（`ProfilerMarker` 在非 Development Build 會被 strip 成 no-op）。

---

## §D. 建議實測場景（Benchmark Matrix）

> ⚠️ **兩條方法論前提，不遵守的話量出來的數字沒有意義**：
>
> 1. 🔴 **Debug ON / Debug OFF 必須成對跑**。§5.1 已證明 debug 編譯進 Development Build，
>    而 Profiler 通常連 Development Build ⇒ **不分離的話，「加敵人變慢」會被 debug 繪製污染**。
>    OFF 的定義：`RuntimeDebugPanel` 的 **F6 全部可視化一鍵關**（`RuntimeDebugPanel.cs:143`），
>    而不是只關面板。
> 2. 🔴 **本專案目前場景只有 1 隻敵人**（§0.3）⇒ **10 / 30 敵人的場景需要先建立**。
>    建議用 EditMode/PlayMode 測試程式生成，**不要手動改 prefab**（會污染 authored 狀態）。

### D.1 場景矩陣

| # | 場景 | 角色組成 | 玩家行為 | 想回答什麼 |
|---|---|---|---|---|
| **S0** | **Baseline: 空場** | 玩家站定，無敵人 | Idle，面向空曠 | **常數項下限**。Traversal 25 rays + FootIK 6 rays + Camera 1 sweep + CombatContext 1 overlap |
| **S1** | **Player only — 面牆** | 玩家站定 | **站在可攀牆前不動** | ⭐ **§2.1 的最壞情況（≈80 查詢/frame）**。這是玩家最常待的狀態，也是 Traversal 的真實峰值 |
| **S2** | **Player + 1 enemy** | 1 敵人 | 正常戰鬥 | **求斜率的第一個點**。S2 − S0 = 一隻敵人的完整邊際成本 |
| **S3** | **Player + 10 enemies** | 10 敵人，**分散** | 正常戰鬥 | 線性度驗證。應 ≈ S0 + 10 × (S2 − S0) |
| **S4** | **Player + 30 enemies** | 30 敵人，**分散** | 正常戰鬥 | ⭐ **主要 scaling 測試**。偏離線性的量就是「超線性成本」的證據 |
| **S4b** | **Player + 30 enemies — 擠成一團** | 30 敵人，**全部在 12 m 內** | 站在人堆中央 | ⭐ **A2 的壓力測試**（soft-target 的 K 最大化）＋ **B2 的 CC 互推最壞情況**。⚠️ 也會撞到 32 buffer 上限 |
| **S5** | **Traversal active** | 玩家 + 0 敵人 | **連續攀爬（每 2 秒一次 Climb2m）** | Traversal 執行期 vs 感測期的成本對比（§2.1 預測：執行期**更便宜**）＋ Hand IK 成本 |
| **S6** | **Combat active** | 玩家 + 10 敵人 | **連段攻擊 + 被打** | Hurt/Death 路徑、`ActionState` phase 切換、overlay layer 建立/銷毀 |
| **S7** | **Spell spam** | 玩家 + 10 敵人 | **持續施放火球 + AoE，每 0.3 s 一發** | ⭐ **A6 的 Instantiate/Destroy spike**。看 GC Alloc 尖峰與 P99 |
| **S8** | **Foot IK 壓力** | 玩家 + 30 敵人 | **全部在斜坡/階梯上移動** | Foot IK 的最壞情況（heel/toe 全命中 ⇒ 6 rays 全發，且法線計算全走） |
| **S9** | **首次觸發全集**（冷啟動） | 玩家 + 1 敵人 | **依序第一次**：跳、翻滾、攻擊、法術、受擊、traversal | ⭐ **spike 專測**。每一個「第一次」都應該在時間軸上看得到 |
| **S10** | **長時間 session** | 玩家 + 10 敵人 | 全動作循環 **15 分鐘** | **B7 的 `_stateCache` 增長**；以及任何累積型洩漏 |

### D.2 每個場景要觀察的指標

| 指標 | 在哪看 | 為什麼要看 |
|---|---|---|
| **Main Thread ms** | Profiler 頂部 / CPU 模組 | 總體預算 |
| **CPU frame time** | CPU Usage 模組 | 與 Main Thread 的差 = worker thread 的貢獻（Animator job、NavMesh job） |
| **`Animators.ProcessGraph` ms** | Timeline / Hierarchy | **B1**。看它隨 N 的斜率 |
| **`Animators.WriteJob` ms** | Timeline | Transform 寫回 |
| **`Physics.Processing` ms** | Physics 模組 | **B2 + B3** |
| **⭐ `Physics.Queries`（次數）** | Physics 模組 | **先驗證 §2／§3 的靜態計數。** 若實測 ≫ 推算 ⇒ 有我沒找到的查詢路徑 |
| **`NavMeshManager.Update` ms** | Hierarchy | **B6** |
| **GC Alloc / frame（B）** | CPU 模組 | **B4 + B5**。⚠️ 目標不是「0」而是「**不隨 N 增長**」——`AnimancerFacade` 的 intern cache 首次配置是合法的 |
| **⭐ P95 / P99 frame time** | ⚠️ Unity Profiler **不直接提供** | 見 D.3 |
| **Audio Voices（Playing / Paused）** | Audio 模組 | **A5**。看 30 敵人時是否撞上限 |
| **`IntentPipeline.Frame` 的每角色總和** | Hierarchy（自訂 marker） | 把「總體變慢」歸因到「每角色成本」還是「角色數」 |
| **`Presentation.Debug` ms** | Hierarchy（自訂 marker） | **A4**。一次跑就能分離 debug 成本 |

### D.3 關於 P95 / P99

🔴 **Unity Profiler 的 Hierarchy view 給的是「當前選中幀」，不是分佈。** 要拿到 P95/P99 有三個途徑：

1. **Profile Analyzer 套件**（`com.unity.performance.profile-analyzer`）——
   讀 Profiler capture，直接給 median / min / max / **百分位分佈**與兩份 capture 的比較。**這是最直接的答案。**
2. **`ProfilerRecorder` API** 自行採樣 `Main Thread` marker，累積 N 幀後自算百分位。
3. **`Unity.PerformanceTesting`**（Performance Testing Extension）寫成 PlayMode 測試 ⇒ 可進 CI。

⚠️ **P99 才是 spike 的所在。** S7（spell spam）與 S9（首次觸發）的平均值會很好看，
**因為 spike 是稀有事件——它藏在 P99 裡，被平均值稀釋掉。**

### D.4 建議的推導表（跑完之後填）

這張表的目的是**把「總體慢」拆成「常數項」與「斜率」**：

| 量 | 算法 | 它回答什麼 |
|---|---|---|
| **常數項** | S0 | 與敵人數無關的底價（Traversal + Camera + 玩家自己） |
| **Traversal 的真實峰值** | S1 − S0 | §2.1 那 80 次查詢到底值多少 ms |
| **單隻敵人邊際成本** | S2 − S0 | 每隻敵人的完整代價 |
| **線性度** | (S4 − S0) ÷ 30 ÷ (S2 − S0) | **= 1.0 代表完美線性；> 1.0 代表有超線性成本**（CC 互推、Audio voice 搶奪、soft-target 的 K） |
| **聚集懲罰** | S4b − S4 | A2 + B2 的交互作用 |
| **Debug 稅** | 各場景 (Debug ON − Debug OFF) ÷ Debug ON | ⭐ **這個比例若 > 20%，所有沒分離 debug 的 profiling 結論都要重做** |
| **Foot IK 佔比** | `FootIK.Solve` marker 總和 ÷ Main Thread | 驗證 A1 的嚴重度是否如推測 |

---

## §E. 三層證據等級總表

> 使用者明確要求不要混在一起。這是本文件所有結論的分類索引。

### 🟢 Code-proven（可以直接當事實引用）

| # | 結論 | 出處 |
|---|---|---|
| P1 | 全 runtime 只有 2 個 gameplay 迴圈入口（`Runner.Update` / `LateUpdate`） | §0.1 |
| P2 | 敵人（Y Bot）帶 Foot IK、Footstep、AudioController；**不帶** TraversalProbe / HandIK / AimResolver | §0.2 |
| P3 | Foot IK：著地時每角色 **6 raycast/frame**，遮罩 = Everything，**無距離剔除** | A1 |
| P4 | Traversal Probe：著地時每幀 **25（空曠）– 80（可攀候選）** 次查詢；**空中為 0** | §2.1 |
| P5 | Traversal 的成本**不隨角色數放大**（玩家專屬） | §0.2 |
| P6 | `probeMinSpeed` **從未被用作閘門**，只有一次 `IsFinite` 檢查 | A3 |
| P7 | **不存在 N enemies × N targets 掃描**；敵人 target 是單一序列化 `Transform` | §4.1 |
| P8 | 所有 debug 編譯條件是 `UNITY_EDITOR \|\| DEVELOPMENT_BUILD` ⇒ **Development Build 完整背上** | §5.1 |
| P9 | `drawFootIKRuntimeLines` 與 `drawCapsuleOffsetDebug` 在**敵人 prefab 上是開的** ⇒ debug 成本線性放大 | A4 |
| P10 | Debug 路徑**零 physics 查詢**（「記錄，不要重算」紀律）⇒ ON/OFF 差值是純繪製 | §5.3 |
| P11 | 每角色 `Start` 建立 **10 顆 AudioSource GameObject** | A5 |
| P12 | 投射物／AoE **無物件池**；`ExtinguishVisual` 的 `GetComponentsInChildren` **確認配置** | A6 |
| P13 | `AIMovementSource` 每幀無條件 `SetDestination` | A7 |
| P14 | **runtime 零 LINQ**，且有回歸測試 `A3` 守著 | A8 |
| P15 | 熱路徑**幾乎零執行期 `GetComponent`**（唯二例外：soft-target、碰撞回呼） | A8 |
| P16 | Lock-on **程式不存在**；Terrain splat 路徑**不可達**；Addressables/Resources **零使用** | §0.4 |
| P17 | Overlay（upper-body）layer **按需建立，非常駐** | §6.1 |
| P18 | 玩家 Animator `m_CullingMode: 0`（AlwaysAnimate） | §6.1 |
| P19 | 場景目前只有 **1 隻敵人** ⇒ 所有 scaling 都未被觀測過 | §0.3 |
| P20 | `TraversalState.CanEnter` 第一行就 gate 在 `JumpRequested` ⇒ 每幀 FSM 遍歷**不會**跑進重邏輯 | §1 表 |

### 🟡 Likely risk（只能當 Profile 優先序，⛔ 不得當缺陷）

| # | 風險 | 為什麼只是 likely |
|---|---|---|
| L1 | `PlayerCombatContextSource` 的每幀 `GetComponentInParent` × K | K 取決於場景敵人密度；階層深度未知 |
| L2 | `SurfaceResolver` 單格 cache 在多 collider 地板上 thrash | 取決於地板是否被切成多個 collider |
| L3 | `FullBodyStateMachine` 每幀 7 次 enum dict 查表 | 成本極小，除非 B4 證實裝箱 |
| L4 | `ActiveTraversalPlan` 大型 struct 每次屬性存取複製 | 只在 traversal 窗內，且只有玩家 |
| L5 | `MotionDriver.CapsuleCast` 的遮罩 `~(1 << layer)`（除自己層外全開） | 寬遮罩的代價取決於場景 collider 分佈 |
| L6 | `AppendTraversalKeyHeights` 無條件每幀畫（即使無候選） | Editor/Dev only；成本是繪製不是查詢 |
| L7 | Traversal 的終點 `CheckCapsule` 與 corridor 末段取樣**看起來**幾何重疊 | 需執行期比對輸入才能確認是否真的等價 |
| L8 | `AIMovementSource` Strafe 的 `NavMesh.Raycast` 實務上接近「每幀都有」 | 原碼指出 Strafe 是穩態模式，但實際佔比需量 |

### 🔴 Profiler required（靜態分析原理上答不出來）

| # | 未知 | 要看什麼（§B 有完整版） |
|---|---|---|
| R1 | Animator / mixer graph 的真實評估成本 | `Animators.ProcessGraph` / `WriteJob` 的**斜率** |
| R2 | `AlwaysAnimate` 對螢幕外角色的代價 | 螢幕外對照實驗 |
| R3 | `OnAnimatorIK` ＋ 開啟 IK pass 的成本 | `MonoBehaviour.OnAnimatorIK` ＋ IK on/off 對照 |
| R4 | `CharacterController.Move` 的實際成本（含動態 center 變更） | `Physics.Processing`；`capsuleOffset` on/off 對照 |
| R5 | PhysX 每種查詢的單價（`Raycast` vs `CheckCapsule`） | `Physics.Queries` 次數 ＋ 遮罩寬窄對照 |
| R6 | enum-keyed `Dictionary` 是否裝箱 | `IntentPipeline.FSM` marker 下的 **GC Alloc** |
| R7 | `Animancer.Parameters.SetValue(string, …)` 是否零配置 | `IntentPipeline.Model` marker 下的 **GC Alloc** |
| R8 | NavMesh path 計算的**分佈**（不是平均） | worker thread timeline；`PathInvalid` 壓力測試 |
| R9 | `AnimancerFacade._stateCache` 單調增長的代價 | 15 分鐘 session 前後對照（S10） |
| R10 | Audio voice 上限在 30 敵人時是否被撞到 | Audio 模組的 Playing / Virtual voices |

---

## §F. 本輪明確**沒有**做的事

依使用者的限制，逐條記錄：

- ⛔ 沒有做任何最佳化
- ⛔ 沒有重構
- ⛔ 沒有改 architecture
- ⛔ 沒有新增任何 cache
- ⛔ 沒有刪除任何 debug
- ⛔ 沒有假設「某東西很慢」——每一個數字都是從磁碟讀出來的，或明確標為需 Profile
- ⛔ 沒有因為看到 `Update()` 就判定有問題（§0.1 反而證明這個專案的 `Update` 極少）
- ⛔ 沒有因為看到 0 GC 就判定效能好（A1 的 6 rays/frame/character 是零 GC 的，但它是最大的線性項）
- ⛔ **沒有實作任何 Profiler marker**（§C 只是清單）
- ⛔ **沒有跑任何 Profiler**——本文件全部是靜態分析 ＋ authored 資產讀取

### 下一步（如果要繼續）

依序，且**每一步都可以獨立停下來**：

1. **建立 §D 的敵人生成測試場景**（10 / 30，PlayMode 生成，不動 prefab）。
2. **實作 §C.1 的 6 支骨幹 marker ＋ `Presentation.Debug`**——最小集合，先讓「歸因」成為可能。
3. **跑 S0 / S1 / S2 / S4，Debug ON 與 OFF 各一次**，填 §D.4 的推導表。
4. 依推導表的結果**再決定**要不要往下挖——⚠️ 這一步之前不做任何最佳化決定。

---

## 附錄 — 本次審查讀過的檔案

程式（全讀或讀關鍵區段）：
`CharacterPipelineRunner` / `AIInputSource` / `AIMovementSource` / `PlayerCombatContextSource` /
`TraversalProbe` / `TraversalClassifier` / `TraversalProbeSettings` / `TraversalState` /
`FullBodyStateMachine` / `StateMachineConfigSO` / `ActionState` / `JumpState` /
`LocomotionModel` / `MotionDriver` / `TraversalWarpPlan` /
`FootIKController` / `FootIKRig` / `TraversalHandIKController` /
`FootstepDetector` / `SurfaceResolver` / `AudioController` / `AudioSourcePool` /
`AnimancerFacade` / `PresentationPipeline` / `ArbiterPipeline` / `CharacterFacingSource` /
`HeadLookController` / `ThirdPersonCamera` / `AimResolver` /
`ThrownProjectile` / `GroundEffectSink` / `MeleeHitboxSink` /
`WorldSpaceHealthBar` / `RuntimeDebugPanel`

資產：
`Assets/Prefabs/X Bot.prefab`、`Assets/Prefabs/Y Bot.prefab`、`Assets/Scenes/SampleScene.unity`

測試：
`ArchitectureRegressionTests`（A3 零 LINQ、A23 AnimationKey 不得每幀配置）＋ EditMode/PlayMode 測試清單

文件：
`CLAUDE.md` / `AGENTS.md` / `WORKLOG.md` 交辦段 / `docs/00-map.md`
