# 00 — 專案導覽地圖（單頁索引）

> **這頁存在的理由**：專案 ~10k 行（docs 4k／code 6k），且註解密度高。沒有索引時，「要改 X 該讀哪裡」只能靠全檔掃描——上下文成本是實際需要的 5～40 倍。**先讀這頁，再精準取用。**
> 讀完這頁應該不必再猜任何檔案位置。維護規則：只記「模組 → 檔案 → 治理章節」，**不記細節**（細節會漂移，指標不會）。

## 文件分工（讀之前先確認你要的是哪一層）

| 文件 | 角色 | 什麼時候讀 |
| --- | --- | --- |
| `WORKLOG.md` 頂部「🔖 交辦」段 | 現在手上的工作、待使用者事項 | **每次會話開場必讀，且通常只需要讀這段** |
| **`docs/12-workflow.md`** 🆕 | **開發工作流的正本**：Feature Slice 工作單位、Integration Gate、**Verification Ladder（L0–L6）**、效能驗證的保留條款。`CLAUDE.md`／`AGENTS.md` 的 Preferred Workflow 只是它的摘要 | **決定「這件事該自動測還是人工測」時必讀**；不確定「現在該不該停下來交回使用者」時也讀這裡 |
| **`docs/16-review-protocol.md`** 🆕 | **Code Review 的正本**：審查順序（六類）、每條 finding 的必要欄位、結尾三段、本專案的四項加重（方向性資料流／單一真相／決策擁有權／下游重新推導）。`CLAUDE.md` 只放觸發指標 | **任何 review 之前必讀**；⚠️ 除非使用者更改，**所有會話持續適用** |
| `docs/ADR/*.md` | 不可變決策紀錄（為什麼這樣設計、否決了什麼） | 動到該決策範圍的架構時 |
| `docs/01-design-doc.md` | Living：當前架構、模組職責邊界、Trade-off 表 | 需要「為什麼」與職責界線時 |
| `docs/02-dev-spec.md` | Living：**跨領域契約**（§0 命名/結構、§1 黑板 schema、§2 管線順序、§3.1 驅動介面、§3.3 State Matrix、§7 架構回歸檢核） | 實作時對照 API 與契約 |
| `docs/05` / `docs/06` … | 子系統分卷（Dev Spec 層，只在做該子系統時讀） | 見下表 |
| `docs/08` / `docs/11` | **Action ／技能系統分卷**：`08`＝Throw vertical slice（ADR-004 Living Spec）；`11`＝Multi-Action ／ Action Identity（ADR-005 Living Spec，✅ **Accepted 2026-09-06**） | 做 Action ／技能相關工作時 |
| `docs/09` / `docs/10` | **相機／瞄準與 Lock-on 分卷**：`09`＝相機取景＋Aim（WP1）；`10`＝Lock-on（規劃中） | 做相機／瞄準相關工作時 |
| `docs/changelog.md` | 最近 4 版；更早在 `changelog-archive.md` | 查近期沿革；考古才開歸檔卷 |
| `docs/03-animation-roadmap.md` | 動畫 Runtime 品質路線（輪次順序） | 規劃下一輪時 |
| `docs/04-locomotion-foundation.md` | Kubold 資產盤點＋ADR-003 的四輪評審全紀錄（§11–14） | 需要 ADR-003 的推導過程時 |
| **`docs/17-benchmark-3c-demo.md`** 🆕 | **外部 3C Demo 對照筆記**（研究性質，非 ADR／非 roadmap）：①**Observability 設計**——四個顯示通道的判準、20 項資訊「debug 什麼問題／資料在哪一層／會不會撞 layer boundary」、三條紅線（⛔ 不得為了顯示新增黑板欄位）；②**環境互動／攀爬翻越的八層問題域**（Intent→Query→Classification→Affordance→Selection→Alignment→Coordination→Return）；③六分類能力矩陣；④Findings | 想知道「成熟 3C controller 比我們多了哪些**問題域**」時；規劃 observability 時；**看到「traversal / 環境查詢」相關想法前先讀 §4.3 三個架構衝擊** |
| `docs/artifacts/*.html` | **技術解說／架構圖／研究筆記的原始檔**（source of truth）。發布成 Claude Artifact 只是方便閱讀的副本，兩者必須同步。<br>目前收錄：**`architecture-tour.html`**＝全專案分層架構導覽（L1 全景 → L2 子系統 → L3 呼叫鏈，15 張圖，每張附「為什麼／否決了什麼／哪條測試守著」）；**`foot-ik.html`**＝Foot IK 單一子系統的深入圖解 | 想快速理解某個子系統的全貌時；**新會話想一次看懂整體架構時先讀 `architecture-tour.html`**；規格細節仍以對應的 `docs/NN-*.md` 為準 |

## 模組 → 檔案 → 治理章節

| 模組 | 主要檔案（`Assets/Scripts/`） | 治理章節 |
| --- | --- | --- |
| 黑板（資料層） | `Core/Blackboard/`：`PlayerRuntimeData`／`IntentData`／`MovementIntentData`／`InputData` | dev-spec §1.1～§1.5（**含讀寫權限表**） |
| 管線 Runner | `Core/Pipeline/`：`CharacterPipelineRunner`／`IInputSource`／`PlayerInputSource` | dev-spec §2.1 順序表＋生命週期脆弱點警告 |
| Movement 意圖層（producer） | `Core/Movement/`：`IMovementIntentSource`／`PlayerLocomotionPolicy`／`GaitProfileSO`／`LocomotionSpeedSmoother` | **ADR-003**；dev-spec §1.5／§3.1；design-doc §4.8 |
| Movement Model（dynamics） | `Core/Movement/Models/`：`IMovementModel`／`LocomotionModel`（B9 平滑＋運動輸出＋自驅動畫參數） | **ADR-003 D3／D4**；dev-spec §3.1／§2.1 順序 3；design-doc §4.8 |
| Locomotion 過渡段（Phase C1/C1.1） | `Core/Movement/Models/`：`LocomotionStopRuntime`／`LocomotionStopSelector`；`Presentation/Motion/MotionDriver` | **`docs/07-locomotion-transitions.md`**；`docs/04-locomotion-foundation.md` §15 |
| 狀態機 | `Core/StateMachine/`（＋`States/`） | dev-spec §3.1（`BaseState`）／§3.2（Config／Params）／§3.3 State Matrix |
| **Action ／技能系統** | `Core/StateMachine/Actions/`：`ActionDefinitionSO`／`ActionPhase`；`Core/StateMachine/States/ActionState`；`Core/Actions/`：`ActionRequestTarget`／`IActionLifecycleSink`；`Presentation/Actions/`：`ThrowProjectileEmitter`／`ThrownProjectile` | **ADR-004**（✅ Accepted）＋**`docs/08-skill-system.md`**；多 Action 走 **ADR-005**（✅ **Accepted 2026-09-06**）＋**`docs/11-multi-action.md`** |
| 跳躍物理 | `Core/StateMachine/JumpStateParams`＋`Presentation/Motion/JumpLaunchData` | **ADR-002**；dev-spec §3.2 跳躍注入 API |
| **上半身層（移動中出手）** 🆕 | 尚未實作 | **`docs/ADR/006-upper-body-layer.md`**（🔵 **Proposed**）。🔴 **2026-09-06 裁決：Trial 名額由 ADR-007 佔用 ⇒ 本 ADR 續留 Proposed、不得開工**，須等 ADR-007 `Accepted`（`docs/13` §5 已補上「持劍備戰」這個更嚴格的使用案例） |
| **戰鬥移動（面向敵人橫移）** 🆕 | 尚未實作 | **`docs/13-combat-locomotion.md`**（🔍 診斷／規劃）。**8 向 strafe 資產早就在專案裡但引用次數 0**；真正的阻擋是 `MotionDriver` 把移動方向綁死在 `transform.forward` ⇒ 根因已升級為 **ADR-007** |
| **戰鬥語境（無瞄準鍵的 combat facing）** 🆕 | 尚未實作 | **`docs/15-combat-context.md`**（🟡 **已採納，ADR-007 的 Living Spec**）。**一句話**：右鍵改留給 Block／Guard ⇒ facing 的來源必須從「按住的輸入模式」改成「戰鬥語境」。內含 facing 來源候選比較、Combat Context 最小進出規則、`AimResolver` 四責任拆解、右鍵退場清單。⇒ 這就是 **ADR-007 的 S3** |
| **方向權威（移動／朝向／瞄準）** 🆕 | `Core/Movement/`（producer＋model）／`Presentation/Motion/MotionDriver`／`Presentation/Camera/AimResolver`／`Core/StateMachine/States/ActionState`／`Core/Actions/IActionLifecycleSink` | **`docs/ADR/007-direction-authority.md`**（🟡 **Trial 2026-09-06**——已是實作基線，**尚未驗證**）＋ **`docs/14-direction-authority.md`**（Living Spec：契約面／切片／測試／Codex 邊界）。**一句話**：三個方向概念共用兩個載體 ⇒ strafe 不可能（`transform.forward` 冒充移動方向）＋ 連段人與火球分家（承諾與發射各自解讀）。**FU-6 於此結案** |
| **武器視覺** 🆕 | `Presentation/Equipment/WeaponSocket` | dev-spec §0.2。⚠️ **只做視覺附著，不是裝備系統** |
| 仲裁層（順序 4.5） | `Core/Arbitration/`：`ArbiterData`／`IArbiterSource`／`ArbiterPipeline`／`Sources/UiModeArbiterSource` | dev-spec §1.4（含來源契約）／§2.1 順序 4.5；design-doc §2.5／§4.5 |
| **應用層**（全域狀態，非角色） | `App/`：`GamePauseController`（`Time.timeScale` 的擁有者）／`CursorModeController`（**`Cursor` API 的唯一擁有者**） | **design-doc §4.9**（為什麼暫停與游標都不屬於角色）；dev-spec §0.2／§7.2-M8・M9／§7.3 |
| 位移驅動 | `Presentation/Motion/`：`MotionDriver`／`MotionBakeData` | dev-spec §2.1 順序 6／§3.2 MotionDriver |
| 動畫門面 | `Presentation/Animation/`：`AnimationFacadeBase`（抽象）／`AnimancerFacade` | 抽象在 dev-spec §3.1；**實作／Mixer／資料流在 `docs/06-animation-presentation.md`** |
| 表現層管線＋音效 | `Presentation/`：`IPresentationController`／`PresentationPipeline`／`Audio/` | dev-spec §3.4；design-doc §4.6 |
| **相機／瞄準（WP1）** 🆕 | `Presentation/Camera/`：`ThirdPersonCamera`／`AimResolver`；消費端 `Presentation/Actions/ThrowProjectileEmitter`；facing 由 `MotionDriver.RequestFacing`（D3(c)） | **`docs/09-camera-aim.md`**（🟢 規格定案，D1–D5 已裁決；**§2.3＝scope boundary 與完成線**、§6.2＝取景參數換算、§10.4＝驗收操作鏈）；相機的滑鼠閘門張力在 dev-spec §7.3 |
| **Lock-on（FU-13）** 🆕 | `Presentation/Camera/LockOnController`（規劃中） | **`docs/10-lock-on.md`**（🟡 待 D1 裁決）。**Stage 1 不開 ADR**（純 Presentation）／**Stage 2 含 strafe ⇒ 必開新 ADR** ⇒ 該 ADR 已於 2026-09-06 開出＝**ADR-007**（方向權威），FU-6 一併在該處結案 |
| Foot IK（Level 1 rigid sole） | `Presentation/IK/` | **`docs/05-foot-ik.md`**（原 §3.5，編號原樣保留；§3.5.5＝Level 1 約束模型與升級階梯）；哲學在 design-doc §4.6；**圖解導覽在 `docs/artifacts/foot-ik.html`** |
| 物件階層 | 角色 Prefab 的 Root／Model 兩層 | **ADR-001**；dev-spec §0.3 |
| 動畫資產治理 | FBX 子 clip 直引、匯入 preset | dev-spec §0.4；CLAUDE.md「Animation Assets」 |
| Editor 工具鏈 | `Editor/Stages/`（烘焙／特徵分析）、`Editor/Tools/`（Capsule／匯入 SOP） | dev-spec §4 |
| **架構不變量** | `_Project/Tests/EditMode/ArchitectureRegressionTests.cs` | dev-spec §7（A1~A10 自動／M1~M6 人工） |

## 常見問題的最短路徑（避免全檔掃描）

| 你想知道 | 讀這裡（而不是實作檔） | 成本差 |
| --- | --- | --- |
| 誰能寫黑板的某欄位 | `ArchitectureRegressionTests.WriterRules`（~15 行）或 dev-spec §1.1 權限表 | ~40× |
| 哪些跨層依賴是被禁的 | `ArchitectureRegressionTests.LayerRules`（~30 行） | — |
| 每帧的執行順序 | dev-spec §2.1 一張表 | — |
| 某設計「為什麼」是這樣 | 對應 ADR 的 §3 Decision ＋ §6 Alternatives | — |
| 某模組現在做到哪 | `WORKLOG.md` 交辦段 | — |
