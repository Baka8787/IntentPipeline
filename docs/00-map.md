# 00 — 專案導覽地圖（單頁索引）

> **這頁存在的理由**：專案 ~10k 行（docs 4k／code 6k），且註解密度高。沒有索引時，「要改 X 該讀哪裡」只能靠全檔掃描——上下文成本是實際需要的 5～40 倍。**先讀這頁，再精準取用。**
> 讀完這頁應該不必再猜任何檔案位置。維護規則：只記「模組 → 檔案 → 治理章節」，**不記細節**（細節會漂移，指標不會）。

## 文件分工（讀之前先確認你要的是哪一層）

| 文件 | 角色 | 什麼時候讀 |
| --- | --- | --- |
| `WORKLOG.md` 頂部「🔖 交辦」段 | 現在手上的工作、待使用者事項 | **每次會話開場必讀，且通常只需要讀這段** |
| `docs/ADR/*.md` | 不可變決策紀錄（為什麼這樣設計、否決了什麼） | 動到該決策範圍的架構時 |
| `docs/01-design-doc.md` | Living：當前架構、模組職責邊界、Trade-off 表 | 需要「為什麼」與職責界線時 |
| `docs/02-dev-spec.md` | Living：**跨領域契約**（§0 命名/結構、§1 黑板 schema、§2 管線順序、§3.1 驅動介面、§3.3 State Matrix、§7 架構回歸檢核） | 實作時對照 API 與契約 |
| `docs/05` / `docs/06` … | 子系統分卷（Dev Spec 層，只在做該子系統時讀） | 見下表 |
| `docs/08` / `docs/09` | **Action ／技能系統分卷**：`08`＝Throw vertical slice（ADR-004 Living Spec）；`09`＝Multi-Action ／ Action Identity（ADR-005 Living Spec，🟡 Trial） | 做 Action ／技能相關工作時 |
| `docs/changelog.md` | 最近 4 版；更早在 `changelog-archive.md` | 查近期沿革；考古才開歸檔卷 |
| `docs/03-animation-roadmap.md` | 動畫 Runtime 品質路線（輪次順序） | 規劃下一輪時 |
| `docs/04-locomotion-foundation.md` | Kubold 資產盤點＋ADR-003 的四輪評審全紀錄（§11–14） | 需要 ADR-003 的推導過程時 |
| `docs/artifacts/*.html` | **技術解說／架構圖／研究筆記的原始檔**（source of truth）。發布成 Claude Artifact 只是方便閱讀的副本，兩者必須同步 | 想快速理解某個子系統的全貌時；規格細節仍以對應的 `docs/NN-*.md` 為準 |

## 模組 → 檔案 → 治理章節

| 模組 | 主要檔案（`Assets/Scripts/`） | 治理章節 |
| --- | --- | --- |
| 黑板（資料層） | `Core/Blackboard/`：`PlayerRuntimeData`／`IntentData`／`MovementIntentData`／`InputData` | dev-spec §1.1～§1.5（**含讀寫權限表**） |
| 管線 Runner | `Core/Pipeline/`：`CharacterPipelineRunner`／`IInputSource`／`PlayerInputSource` | dev-spec §2.1 順序表＋生命週期脆弱點警告 |
| Movement 意圖層（producer） | `Core/Movement/`：`IMovementIntentSource`／`PlayerLocomotionPolicy`／`GaitProfileSO`／`LocomotionSpeedSmoother` | **ADR-003**；dev-spec §1.5／§3.1；design-doc §4.8 |
| Movement Model（dynamics） | `Core/Movement/Models/`：`IMovementModel`／`LocomotionModel`（B9 平滑＋運動輸出＋自驅動畫參數） | **ADR-003 D3／D4**；dev-spec §3.1／§2.1 順序 3；design-doc §4.8 |
| Locomotion 過渡段（Phase C1/C1.1） | `Core/Movement/Models/`：`LocomotionStopRuntime`／`LocomotionStopSelector`；`Presentation/Motion/MotionDriver` | **`docs/07-locomotion-transitions.md`**；`docs/04-locomotion-foundation.md` §15 |
| 狀態機 | `Core/StateMachine/`（＋`States/`） | dev-spec §3.1（`BaseState`）／§3.2（Config／Params）／§3.3 State Matrix |
| **Action ／技能系統** | `Core/StateMachine/Actions/`：`ActionDefinitionSO`／`ActionPhase`；`Core/StateMachine/States/ActionState`；`Core/Actions/`：`ActionRequestTarget`／`IActionLifecycleSink`；`Presentation/Actions/`：`ThrowProjectileEmitter`／`ThrownProjectile` | **ADR-004**（✅ Accepted）＋**`docs/08-skill-system.md`**；多 Action 走 **ADR-005**（🟡 Trial）＋**`docs/09-multi-action.md`** |
| 跳躍物理 | `Core/StateMachine/JumpStateParams`＋`Presentation/Motion/JumpLaunchData` | **ADR-002**；dev-spec §3.2 跳躍注入 API |
| 仲裁層（順序 4.5） | `Core/Arbitration/`：`ArbiterData`／`IArbiterSource`／`ArbiterPipeline`／`Sources/UiModeArbiterSource` | dev-spec §1.4（含來源契約）／§2.1 順序 4.5；design-doc §2.5／§4.5 |
| **應用層**（全域狀態，非角色） | `App/`：`GamePauseController`（`Time.timeScale` 的擁有者）／`CursorModeController`（**`Cursor` API 的唯一擁有者**） | **design-doc §4.9**（為什麼暫停與游標都不屬於角色）；dev-spec §0.2／§7.2-M8・M9／§7.3 |
| 位移驅動 | `Presentation/Motion/`：`MotionDriver`／`MotionBakeData` | dev-spec §2.1 順序 6／§3.2 MotionDriver |
| 動畫門面 | `Presentation/Animation/`：`AnimationFacadeBase`（抽象）／`AnimancerFacade` | 抽象在 dev-spec §3.1；**實作／Mixer／資料流在 `docs/06-animation-presentation.md`** |
| 表現層管線＋音效 | `Presentation/`：`IPresentationController`／`PresentationPipeline`／`Audio/` | dev-spec §3.4；design-doc §4.6 |
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
