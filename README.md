# IntentPipeline

**A learning-oriented Unity character framework built around one idea: gameplay reads data, never queries other gameplay systems.**
Input becomes a model-agnostic *intent* on a blackboard; a swappable *movement model* turns that intent into motion and drives its own animation parameters. Gait tuning and hold-vs-toggle semantics live in a ScriptableObject (key bindings stay in Unity Input Actions); swapping the input source (AI / replay / network) is swapping a component; and the core pipeline never names a concrete movement model — it depends only on `IMovementModel`, so a second one (strafe / swim / vehicle) is a new class plus a component swap. **Runtime model switching is designed (`MovementContext`) but deliberately not implemented until a second model exists.**

Every significant decision is recorded as an ADR, and the architecture's invariants are enforced by automated tests rather than by convention — **620 EditMode tests + 64 PlayMode tests, including an architecture-regression suite that turns 45 documented invariants (layer boundaries, blackboard single-writer ownership, zero-LINQ, intent contracts, committed-motion rules, debug-channel scope) into executable assertions. Break one and the suite goes red.**

What started as a locomotion spine (walk/run/sprint, jump, roll) has since grown — on the same blackboard-and-interfaces architecture, with no core pipeline changes — into a data-driven Action/skill system, combat with soft-target facing and an enemy AI baseline, ledge vault/climb traversal with root-warp + hand IK, a pose-driven dynamic capsule, surface-aware footstep and combat audio, and a Hurt/Death survivability layer. See [系統總覽](#系統總覽) below for the full list, or `docs/00-map.md` for the file-by-file map.

> ⚠️ This repository does **not** build from a fresh clone: **Animancer Pro is the only compile-time third-party dependency** (excluded — paid licence). Kubold Movement Animset Pro is **sample locomotion content, not a framework dependency** — remove it and movement, gait, state transitions, jump physics and roll all keep working; only the locomotion *animation* stops playing. See [第三方資產需求](#-第三方資產需求).

---

## 這個專案在做什麼

一個**學習導向**的 Unity 角色框架，目標**不是**做出一款遊戲，而是把「乾淨架構、資料驅動、零 GC 熱路徑」三件事做到**能被檢驗**——前兩者由 EditMode 測試守，第三者由 Development Build 的 Profiler 實測佐證。同時把每一個重要決策的**理由與被否決的替代方案**寫下來。

因此這個 repo 的重點有一半在 `docs/`：架構決策紀錄（ADR）、當前架構文件、開發規格、以及一份逐版本的開發日誌，記錄每一輪撞到什麼坑、為什麼那樣解。

**環境**：Unity 6000.5.1f1 ／ URP 17.5 ／ Input System 1.19 ／ Animancer Pro（動畫執行期）

---

## 架構主張

| 主張 | 具體做法 |
| --- | --- |
| **資料驅動** | Gameplay 讀黑板資料，不直接查詢其他 gameplay 系統 |
| **單一寫入者** | 黑板每個欄位有明確的 Owner／Writer／Readers，禁止第二個寫入者（由測試守） |
| **單向依賴** | `Input → Pipeline → 黑板`；`StateMachine`／`Animation`／`Motion` 皆為黑板的**平行消費者**，彼此不互相依賴（表現層反向依賴狀態機由 A4 擋下）。逐帧執行順序見 `docs/02-dev-spec.md` §2.1 |
| **零 GC 熱路徑（✅ Player 實測）** | `ref struct` 輸入採樣、值型別 dynamics、無 LINQ（由 A3 測試守）。**Development Build ＋ Profiler 實測：穩態移動下 `PlayerLoop` 的 `GC Alloc` = 0 B**（[存證截圖](docs/images/profiler/gc-alloc-zero-walk.png)；量測程序、排除項與已知邊界見 `docs/02-dev-spec.md` §7.4） |
| **動畫資產不可變** | FBX 子 clip 是唯一真相；調整依「資料 → 呈現層 → 換 clip → 改 clip 內容」的固定升級階梯，禁止跳級 |
| **不預造** | 第二個使用者出現前不抽象。每一次「先蓋好等以後用」都在文件裡被明確否決過 |

### 每帧的資料流

```mermaid
flowchart LR
    IN["裝置輸入<br/>(ref struct)"] --> POL["Movement Policy<br/>(IMovementIntentSource)"]
    POL -->|"MovementIntent<br/>模型無關 [0-1]+dir"| BB[("PlayerRuntimeData<br/>黑板")]
    BB --> MODEL["Movement Model<br/>(IMovementModel)"]
    MODEL -->|"Movement Output"| BB
    MODEL -->|"SetFloat(自己的參數)"| ANIM["AnimationFacade<br/>(通用 sink)"]
    MODEL -->|"IsProducingMotion<br/>門檻信號，不經黑板"| FSM["FullBodyStateMachine"]
    BB --> FSM
    FSM -->|"Play(AnimationKey)"| ANIM
    FSM -->|"OnUpdateMotion"| MOTION["MotionDriver<br/>(LateUpdate)"]
    MODEL -.->|"ambient state delegate"| MOTION
    MOTION -->|"IsGrounded / JustLanded"| BB
```

關鍵在於 **seam 的位置**：意圖（intent）是模型無關的 `{強度[0-1], 方向}`。因此「同樣的推桿量該換算成多強」與「walk 是按住生效還是按一下切換型態」這類 per-game 規則，住在可替換的 policy ＋ `GaitProfileSO` 裡，而不是燒進狀態機；「**哪顆鍵**做什麼」則留在 Unity Input Actions（ADR-003 §13.3：input routing 屬更上游的職責，不歸 producer）；「多快才算跑」是 movement model 的內部知識——通用管線完全不認識 `MoveSpeed` 這種概念，而且這件事由測試 A9 守著。

---

## 架構決策紀錄（ADR）

ADR 是**不可變的決策快照**——要改決策就開新 ADR 取代舊的，不改寫歷史。每份都含 Context／Decision／**Alternatives Considered（附明確的否決理由）**／Trade-offs／Consequences。

| # | 決策 | 一句話 | 狀態 |
| --- | --- | --- | --- |
| [001](docs/ADR/001-root-model-hierarchy.md) | GameObject 階層 Root/Model 分離 | 物理世界座標與 Animator 根動作徹底分開，骨骼操作一律掛 Model 層 | ✅ Accepted |
| [002](docs/ADR/002-data-driven-jump.md) | 數據驅動跳躍與多段跳 | 跳躍物理量從動畫烘焙資料反推，而非手填魔術數字；根治「兩個真相來源」 | ✅ Accepted |
| [003](docs/ADR/003-movement-intent-layering.md) | Movement Intent 分層 | Producer 介面 × 黑板中性契約 × Model via State；含四輪對抗式評審後被推翻的三個方案 | ✅ Accepted |
| [004](docs/ADR/004-action-in-fsm.md) | Action 進 FSM 拓撲 | 單一 `ActionState` × 資料驅動 `ActionDefinitionSO`，而非每招一個 subclass；本專案第一個走完 Trial→Accepted 全流程的 ADR | ✅ Accepted |
| [005](docs/ADR/005-multi-action-identity.md) | Action Identity | 多個 Action 並存時的單一身分概念，讓冷卻／連段互不撞號 | ✅ Accepted |
| [006](docs/ADR/006-upper-body-layer.md) | 上半身動畫層 | 移動中施法：Layer 1 upper-body mask 疊在 Layer 0 locomotion 之上 | 🟡 Trial |
| [007](docs/ADR/007-direction-authority.md) | Direction Authority | 移動方向／朝向／瞄準三個概念統一擁有權與更新時點，解開 strafe 與連段人火分家的根因 | 🟡 Trial |
| [008](docs/ADR/008-traversal-integration.md) | Traversal FSM Integration | Vault／Climb 進 FSM 為獨立 `StateType.Traversal`，root warp + hand IK 只消費 committed plan。**D10（2026-09-21）改述 committed 的定義**：固定的是 landmarks／timing／contact targets，不是絕對軌跡——執行可在 authored phase boundary 以角色自己的 `transform.position` 重取基準 | 🟡 Trial |
| [009](docs/ADR/009-survivability-and-lethal-interrupt.md) | Survivability ＋ Lethal 中斷 | Hurt／Death 黑板欄位與死亡對其他中斷授權的否決層 | 🟡 Trial |

> ADR 狀態意義：**Trial** ＝已裁決為目前實作基線，但尚未經第一個 vertical slice 完整 Play 驗證；**Accepted** ＝已實作＋Play＋測試驗證通過、決策內容凍結。詳見 `CLAUDE.md`「ADR Lifecycle」。

---

## 系統總覽

Locomotion 之外，這半年陸續在同一個黑板／介面架構上長出以下系統——**核心管線（`CharacterPipelineRunner`）沒有為任何一個新系統改過**，新系統一律是新的 producer／model／state／sink 插進既有 seam。

| 系統 | 解決什麼 | 正本 |
| --- | --- | --- |
| Locomotion | Walk/Run/Sprint 平滑過渡、hold/toggle 步態、零 GC 熱路徑 | `docs/04`、ADR-003 |
| Jump / Roll | 數據驅動的跳躍物理與多段跳，數值全部從動畫烘焙反推 | ADR-002、`docs/31` |
| Action／技能系統 | 資料驅動的招式（Fireball／Ice／Melee），單一 `ActionState` 取代每招一個 subclass；多招並存、連段窗、上半身施法層 | ADR-004／005／006、`docs/08`、`docs/11` |
| 戰鬥與方向權威 | 移動方向／朝向／瞄準三個概念的擁有權統一；戰鬥中面向敵人橫移、soft-target 選定 | ADR-007、`docs/13`、`docs/14`、`docs/15` |
| 敵人 AI（baseline） | FSM-lite 追擊／aggro／leash，與玩家共用同一條 IntentPipeline | `docs/21` |
| Traversal（Vault／Climb） | 翻越／攀爬矮牆的偵測、進場判定、root warp、手部 IK，FSM 拓撲獨立成 `StateType.Traversal`。**兩種 execution model**：`Absolute`（Vault1m／Climb1m）與 `Segmented`（Climb2m，段界重取基準把執行誤差收在 committed landmark 上）——由 binding 的一個資產欄位決定，切換不動程式 | ADR-008、`docs/22`、`docs/30`、`docs/39`、`docs/40` |
| 動態碰撞體 | 碰撞膠囊依當前姿勢（gait／traversal）即時收放，而非固定形狀 | `docs/27`、`docs/28` |
| Foot IK | 剛體腳底貼地校正，量測驅動而非美術手調權重 | `docs/05`、`docs/29` |
| 腳步偵測＋聲學材質 | 依世界空間腳部速度判斷真正落地（非擺盪低點），材質由黑板單一欄位決定，三類事件不可能不一致 | `docs/34`〜`docs/36` |
| 戰鬥音效 | 施法／命中／受擊／死亡的音效一律掛在既有的時間權威回呼上，不另發明時間。⚠️ **程式與接線完整，素材刻意不完整**——13 個 clip 池只有 3 個有素材，其餘靜音。盲找的命中率實測只有 23%（56 顆採用→13 顆存活），使用者原則是「不為了『有聲音』硬塞」 | `docs/37` |
| Survivability | Hurt／Death 黑板欄位與死亡對其他中斷的否決層 | ADR-009、`docs/25`、`docs/26` |
| 相機／瞄準 | 第三人稱取景＋準心解算 | `docs/09` |
| Editor 工具鏈 | 動畫特徵烘焙、碰撞體擬合、Traversal 接觸點推導、素材匯入 SOP（見下方「Editor 工具鏈」） | dev-spec §4 |

**Editor 工具鏈**（`Assets/Scripts/Editor/`）：這些是把「量測」變成「資產」的自製工具，取代手填魔術數字——

- `Stages/MotionBakeEditor.cs`／`MotionFeatureAnalysis.cs` — 從 FBX 子 clip 逐幀採樣，烘出 `MotionBakeData`（速度曲線、腳相、位移、反推重力），供 Jump／Locomotion／Foot IK 共用同一份真相
- `Stages/TraversalContactDerivation.cs` — 從動畫反推 Vault／Climb 的 ledge contact／手部朝向，寫入 `TraversalBakedAnchor`
- `Tools/CharacterCapsuleFitter.cs` — 依角色網格自動擬合碰撞膠囊尺寸
- `Tools/MotionClipImportSOP.cs`／`AnimationBatchOps.cs` — 動畫匯入設定與批次操作的標準流程
- `Tools/TraversalDebugWindow.cs`／`SceneSurfaceAuditWindow.cs`／`FootstepDetectorEditor.cs` — 各子系統的唯讀場景診斷視窗

多數子系統另有一份「寫給人讀」的架構教學（`docs/28`–`docs/33` 六份 architecture-guide）與對照的互動圖解（`docs/artifacts/*.html`，目前 11 份子系統頁），把資料流、擁有權、真實數值計算鏈畫成圖——這些原本是內部除錯用的文件，但正好也是解釋「這套架構在解什麼問題」最短的路徑。

⚠️ 圖解頁刻意**只描述 repo／docs／tests 已能證明的現況**：與正本分歧處逐條指名（例如 `docs/40` 推導的 coverage 下緣 1.72 已被 §13 改回 1.65，以資產為準），「有這個事實但沒有測試守」也會明說。

---

## 專案結構

```
Assets/
  Scripts/
    Core/
      Blackboard/          PlayerRuntimeData / IntentData / MovementIntentData / InputData
      Pipeline/            CharacterPipelineRunner（管線唯一驅動點）/ IInputSource
      Movement/            意圖 producer（context-free）＋ GaitProfileSO
        Models/            Movement Model：IMovementModel / LocomotionModel
      StateMachine/        FSM 本體與各 State（Idle / Move / Jump / Roll / Action / Traversal）
        Actions/           ActionDefinitionSO / ActionPhase（資料驅動招式）
      Actions/             ActionRequestTarget / IActionLifecycleSink
      Combat/              PlayerCombatContextSource（soft-target）
      Survivability/       CharacterHealth / Hurt-Death 黑板欄位
      Environment/         Traversal Probe / Classifier / EntryPolicy
      Arbitration/         ArbiterPipeline（暫停／UI 模式仲裁，順序 4.5）
      Facing/              CharacterFacingSource
    Presentation/          AnimationFacade（抽象＋Animancer 實作）/ MotionDriver / Foot IK
      Actions/             ThrowProjectileEmitter / ThrownProjectile
      Camera/               ThirdPersonCamera / AimResolver
      Footstep/             FootstepDetector / SurfaceResolver
      Equipment/            WeaponSocket / WeaponVisibilitySink
      Audio/                AudioController / AudioSourcePool
    App/                    全域狀態：GamePauseController / CursorModeController / PlayerHud / RuntimeDebugPanel
    Editor/                動畫烘焙、碰撞體擬合、Traversal 接觸點推導等工具鏈（見上方「系統總覽」）
  _Project/Tests/
    EditMode/               620 個測試（619 passed／1 skipped），含架構回歸測試
    PlayMode/               64 個測試，涵蓋帧迴圈／暫停／輸入生命週期／真實 rig 的 traversal
  ScriptableObjects/       Motion（烘焙資料）/ Animation / Movement（gait）/ StateMachine
docs/                      見下方導覽
```

---

## 文件導覽

**從 [`docs/00-map.md`](docs/00-map.md) 開始**——那是一頁式索引（模組 → 檔案 → 治理章節），設計目的就是讓你不必為了找一件事而掃全部檔案。`docs/` 目前有 40+ 份規格／研究／審查輪文件（累計自 2026-07 至今），全部靠這頁索引導航，**不建議從頭逐檔讀**。

| 文件 | 角色 |
| --- | --- |
| [`docs/00-map.md`](docs/00-map.md) | 單頁導覽索引（模組 → 檔案 → 治理章節） |
| [`docs/01-design-doc.md`](docs/01-design-doc.md) | 當前架構、模組職責邊界、**Trade-off 表**（含每個決策的代價） |
| [`docs/02-dev-spec.md`](docs/02-dev-spec.md) | 跨領域契約：黑板 schema、管線順序表、驅動介面、**架構回歸檢核清單** |
| [`docs/ADR/`](docs/ADR/) | 不可變決策紀錄（001–009） |
| [`docs/changelog.md`](docs/changelog.md) | 逐版本開發日誌（撞到的坑、為什麼那樣解、學到什麼）；更早的在 `changelog-archive.md` |
| `docs/03`–`docs/41` | 各子系統的規格／研究輪／審查輪（Locomotion、Action、Combat、Traversal、Footstep、Survivability…）——見 [系統總覽](#系統總覽) 對應表 |
| [`docs/artifacts/`](docs/artifacts/) | **16 份互動圖解（HTML）**，分三層：`architecture-tour.html` 是入口索引；**11 份 Subsystem Architecture**（每頁固定含 標註式資料流＋Authority 矩陣＋跨幀狀態盤點＋按真實比例的數值計算鏈＋failure case 追因）；**3 份 Cross-System**（跨系統契約／Authority 時間軸／失效模式圖鑑）。另有一份 Level 1 剛體腳底的專題圖解 |

---

## 測試：架構不變量是可執行的

一般測試守功能，這裡多了一類守**架構**——因為架構被破壞時**通常沒有任何症狀**，直到某天要換 producer 或加第二個 model 才發現 seam 已經爛了。

| ID | 守什麼 |
| --- | --- |
| A1～A3 | 組件依賴方向單向、Runtime 不得在 `#if UNITY_EDITOR` 外碰 `UnityEditor`、熱路徑零 LINQ |
| A4 | 跨層依賴禁令（表現層不得反向依賴狀態機、意圖 producer 不得回讀 gameplay state…） |
| A5 | 黑板每個欄位的**單一寫入者**白名單 |
| A6～A8 | 意圖契約：連續型 intent 不被單帧復位、輸出可由意圖完全重現、mode state 不得藏在 producer 私有欄位 |
| A9 | **通用管線不得認識任何 locomotion 概念**（`MoveSpeed`／`SmoothDamp`／gait 等 token 出現在 Runner 即失敗） |
| A10 | 跨帧平滑狀態的執行期持有者恰好一個（多於一個＝狀態切換時手感會斷） |
| A11～A27 | Foot IK pose 管道單一 owner、AI NavMesh 只查路不取得位移權、Action 層零 `Instantiate`／零 `CharacterController` 引用、`AnimationKey` 讀取零配置、Action Identity 單一來源、Slow 效果不外溢、攻擊觸發只能來自 `IInputSource`…… |
| A28～A38 | Direction Authority：程序式位移不得用 `transform.forward`、只有玩家 producer 可引用相機、`CharacterFacingSource` 是唯一 facing request 發送者、`MotionDriver` 不決定朝向；debug 通道「只讀不重算」 |
| A39～A50 | Traversal：環境查詢只存在於 Probe、執行期零 Physics 查詢與零重力退路、plan／policy／fitting 全為純函式、Hand IK 只消費 committed plan、**段界重取基準只能發生一次且不得在段內重取樣**、`MotionDriver` 是碰撞體形狀的唯一 runtime writer |

目前共 **45 條**（編號到 A50，中間有幾個已隨決策收斂而退場）。這些測試同時是**最便宜且不可能過期的架構摘要**——想知道誰能寫黑板某欄位，讀 15 行的 `WriterRules` 比讀三個實作檔（600+ 行）快 40 倍，而且它不可能與程式碼不同步，因為不同步就會變紅。

⭐ 其中幾條是**以原始碼規則**（而非執行期行為）表述的，因為它們要守的東西沒有可觀測的症狀——例如 A46 斷言 `TraversalExecutionSegment.TryCreate(` 在 `MotionDriver.cs` 裡**只能出現一次**，而且不得出現在段內逐幀求值的方法裡。想在段內偷偷重取樣，測試會在原始碼層變紅。完整清單見 `docs/02-dev-spec.md` §7.1。

在 Unity 中執行：`Window → General → Test Runner`，EditMode／PlayMode 兩頁分別 Run All（PlayMode 需先進 Play 模式相依的帧迴圈測試，見 `docs/02-dev-spec.md` §7.1.2）。

---

## ⚠️ 第三方資產需求

以下資產已從版本控制排除。**三者的角色與缺失後果完全不同，請分開看**：

| 資產 | 角色 | 缺失後果 | 放置位置 |
| --- | --- | --- | --- |
| **Animancer Pro**（Kybernetik，付費） | **Framework dependency** | 🔴 **Compile-blocking**——動畫播放後端，全 Runtime 唯一的 `using Animancer` 在 `AnimancerFacade.cs`。缺少時 `Project.Runtime` 編譯失敗，**測試也跑不了** | `Packages/com.kybernetik.animancer/`（本機 UPM 套件，`packages-lock.json` 已以 `file:` 引用） |
| **Kubold Movement Animset Pro**（付費） | **Sample content**（現行 locomotion 動畫來源） | 🟠 **Sample-content-only**——不影響編譯，也不影響 gameplay 邏輯。`Locomotion.asset` 的 4 支 mixer clip 與 4 顆 locomotion `Bake_*.asset` 的 `SourceClip` 會變 `None` → **角色仍以正確速度移動、狀態機仍正常轉換，但不播 locomotion 動畫**（Animancer facade 印警告後略過，不拋例外） | `Assets/MovementAnimsetPro/` |
| Unity StarterAssets（免費） | Scene dressing | 🟡 **Scene-only（可選）**——`SampleScene` 引用了它的材質（`GridOrange_01_Mat`），缺少時場景出現遺失材質，**不影響編譯與測試** | Package Manager 匯入 |

### Kubold 為什麼只是 sample content

這不是宣稱，是可以照著查的：

* **`Assets/Scripts/Core` 全層搜不到 `AnimationClip`／`ClipTransition`／`AnimancerState`** —— 核心層不認識 clip 型別。
* **烘焙資料是自足的**：`MotionBakeData` 的特徵欄位（速度曲線、腳相曲線、代表速度、動畫長度、起跳前搖、頂點高度、反推重力…）**全部是烘焙期寫入的序列化值**，clip 消失後仍在。`MotionDriver` 的滿速來源讀的是 `AutoAverageSpeed`，不是 clip；`SourceClip` 已降為 Editor-side provenance，**執行期程式碼對它的讀取數為 0**。
* **Jump 與 Roll 的動畫來源本來就不是 Kubold**：兩者的烘焙資產指向版控內的 Mixamo clip（`X Bot@Jump.fbx`／`X Bot@Stand To Roll.fbx`），因此跳躍物理與翻滾曲線位移完全不受影響。
* **角色 Prefab 對 Kubold 的引用數為 0** —— 模型與 Avatar 皆為 Mixamo X Bot。

換句話說：**要換一整套 locomotion 動畫，換的是 `Locomotion.asset` 的 4 支 clip ＋ 重跑一次烘焙**，程式碼一行不動。

> ⚠️ 但重匯入 Kubold 時要注意：clip 引用靠 `.meta` 的 GUID 對應，**重新匯入後 GUID 不會與原專案一致**，因此 `Locomotion.asset` 與 4 顆 locomotion `Bake_*.asset` 的 clip 欄位需在 Unity 內重新指定，並依 `docs/02-dev-spec.md` §4 重跑烘焙（門檻公式 `threshold = speed_i / speed_max`，數值從重烘後的 Bake Data 換算）。動畫匯入設定（Humanoid Root Transform 矩陣）見同文件 §0.4。

---

## License

程式碼採 [MIT](LICENSE)。

**License 不涵蓋上述第三方資產**——Animancer Pro 與 Kubold Movement Animset Pro 各自受其原始授權約束，本 repo 不包含也不轉散佈它們的任何內容。版控內的 Mixamo（Adobe）角色與動畫資產亦受其原始條款約束。
