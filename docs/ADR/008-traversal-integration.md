# ADR-008：Traversal FSM Integration（V1 → V3）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-09-14 marker／Animation Fitting／Hand IK binding／primary-contact root solve 已落地並由自動測試守住；待人類 Play quality 驗收） |
| 日期 | 2026-09-12（V3 修訂 2026-09-13） |
| 範圍 | `StateType.Traversal`、單一 `TraversalState`、stationary sensing、Entry legality、Jump-vs-Climb policy、committed contact-aware Motion Map、fixed-capsule corridor、collision profile ownership、early recovery handoff |
| 不含 | auto-navigation／bounded pre-alignment 啟用、cancel／interrupt policy、moving obstacle/platform、Free Climbing／Ledge Hang；IK 保留為 evidence-gated quality pass，不用來修 root/collision |
| 治理例外 | ADR-006／007 尚未 Accepted；本輪指令明確要求直接完成 topology integration，故另立最小 Trial。此為本工作包 scoped 例外，不放寬一般「同時一個 Trial」規則，也不虛構 006／007 的未完成驗收。 |

---

## 1. Problem

Environment Probe 與 committed 3D curve movement 已存在，但 Jump input 尚未能把合法
`TraversalCandidate` 轉成可播放、可位移、可完成的 gameplay 行為。把流程塞入 `JumpState` 會讓跳躍開始認識
environment classification；塞入 `ActionState` 則會把 traversal 的幾何候選與技能 identity／lifecycle 混成同一責任。

V1 實際 Play 後得到三項已驗證問題，不是理論假說：固定 baked trajectory 對環境尺寸敏感並造成起點／終點／
接觸點 alignment error；低平台會被 Climb 搶走，即使 Normal Jump 已可自然登上；主要位移完成後仍等待動畫
100% 才解鎖，造成可感知的 recovery latency。V2 因此在不重設 Probe／Classifier 的前提下補上 selection、warp、recovery。

V2 後續 Play 又把問題收斂成五個具體缺口：速度 gate 阻止 stationary sensing；Candidate 沒有 entry legality；
endpoint-only warp 沒有 contact／transfer constraints；standing capsule 與 animated body envelope 可能 phase mismatch；
animation-side contact 與 runtime hand target mapping 缺失。V3 解的是同一三種 traversal 的品質鏈路，不擴張玩法。

## 2. Decision

### D1 — Traversal 是獨立 FullBody State；kind 是資料

新增且只新增一個 `StateType.Traversal` 與一個 `TraversalState`。`Vault1m／Climb1m／Climb2m`
由同一 state 依 committed candidate 選三格 authored binding，不新增三個 state subclass。

### D2 — Candidate 與 commitment 分屬兩個 owner

- `TraversalProbe` 繼續是即時 `Candidate` 與所有 traversal physics query 的唯一 owner。
- `TraversalState.CanEnter` 先套用純 gameplay selection policy，再於成立時複製 candidate。
- `TraversalState` 擁有這份 committed copy；`OnEnter` 與執行期只消費 copy，不回讀 Probe、不重新 query／classify。

### D3 — Jump fallback 只靠既有 FSM arbitration

`Traversal` 的 authored priority 必須高於 `Jump`。Candidate 為 `None` 時 `TraversalState.CanEnter == false`，
原本 `JumpState.CanEnter` 照常成立。`JumpState` 不引用任何 traversal/environment 型別，不新增 arbitration framework。

### D4 — 執行只走 committed Motion Warp 與既有 movement authority

三種 kind 都在 `OnEnter` 由 committed candidate 與 entry pose 建立一次 `TraversalWarpPlan`。Plan 保留 baked
水平／垂直 trajectory shape，以 authored warp window 漸進吸收實際 destination 與 yaw correction；執行期輸出
`warpedCurrentPosition - warpedPreviousPosition`，仍由 `MotionDriver` 經 `CharacterController.Move` 執行，且不回讀 Probe。
退化資料回退既有 committed curve motion，不直接寫 `transform.position`，也不建立第二套 movement authority。

### D5 — Gameplay recovery 與 animation completion 分離

每格 binding author `recoverNormalizedTime`。有效 warp 已完成且 animation time 到 recovery 門檻後，
`CanTransitionAway` 即可成立，不必等待 normalized time 1.0；visual tail 交由既有 transition/blend 淡出。
無效 warp 不 early recover，而以 V1 completion fallback 安全結束。V2 仍不定義 cancel 或 hit interruption。

### D6 — Completion 交回現有 FSM

`TraversalState` 不硬寫 Idle／Move／Falling。`ValidTransitions` 以 `Jump` 優先、再 `Move／Idle`：
grounded 時 Jump gate 拒絕後回 ambient；airborne 時既有 `JumpState` fall-entry 接手。

### D7 — Jump-vs-Climb 是純 gameplay policy

`TraversalClassifier` 的 Vault／Climb 幾何分類不變。`TraversalSelectionPolicy` 只對 Climb 套用 capability 判斷：
從既有 ground Jump bake 的 launch velocity、gravity 與倍率推導 apex，再扣 authored safety margin。可安全到達則讓
Normal Jump 接手；超出才進 Traversal。Vault 永遠不受此 Climb policy 過濾；無效 jump authority 採保守 traversal。

### D8 — Candidate 與 Entry Executability 分離

Grounded Probe 不再用速度決定 sensing／legality：有 MoveDirection 用 Move，否則用 committed facing。Jump intent
只消費既有 snapshot。Pure `TraversalEntryPolicy` 以 desired entry root、longitudinal band、lateral 與 facing error 產生
`EntryEvaluation`；V3 超界 Reject，bounded pre-alignment 只保留 seam、不啟用導航。

### D9 — 固定多錨點 Motion Map；root 只採單一 primary contact

`MotionBakeData` optional traversal block 由 Editor author marker time／role／hand bone，bake pipeline 自動採樣 root pose 與
hand-in-root。Probe 提供 `TraversalLedgeFrame` 與 interval；PlanBuilder 在 commit 以單一 primary contact（雙手動作固定為 Left）
解 root constraint，固定輸出 Entry／Left Contact／Right Contact／Transfer／Exit／Recovery 六 knot。secondary hand 不參與 root
平均：primary root 決定後只查詢 Probe 已提交的 ledge surface；查不到合法 surface 就保留 authored pose、停用該手 IK，
不偽造壓縮後的雙手姿勢。Hot path 只做 piecewise evaluation 與 delta，沒有 runtime Physics query。

### D10 — Probe 擁有 fixed-capsule corridor evidence

Destination check 不再代表整段合法。Probe 是唯一 Physics owner，對 Entry→Clearance→Transfer→Exit 建立 standing capsule
corridor evidence，Candidate 保存結果與 reject reason；State／PlanBuilder／MotionDriver／debug 不得重查。Transfer knot 必須先
取得 vertical clearance，再跨 wall plane。

### D11 — Collision profile 仍由單一 movement authority 執行

每格 binding 可 commit `TraversalCollisionProfile`。C0 fixed capsule 是 baseline；C1 centerOffset 已啟用。只有
`MotionDriver` 可寫 `CharacterController` runtime properties，並在 Exit／early recovery／failure 做 idempotent restore。
height／radius 是 disabled、evidence-gated seam：不得在沒有 Play 證據時啟用，也不得另建 movement authority。

## 3. Trade-offs / Alternatives

- **選擇小型 `TraversalStateParamsSO` 三格 binding**：沿用既有 `StateParamsSO` seam，避免把內容欄位塞進 `StateRule`；代價是多一份 Editor asset 要接線。
- **不選 Jump 內部分支**：少一個 StateType，但會破壞 Jump 與 Environment 的依賴邊界，且 committed completion 生命週期會混入跳躍相位。
- **不選 Action identity**：可重用多 phase 框架，但 traversal 的候選 ownership、3D committed motion 與技能 cooldown／release 無關，重用會混淆 authority。
- **不選每 kind 一個 state**：Inspector 拓撲會隨資料種類線性膨脹，違反「kind 是資料」的本輪硬邊界。

## 4. Implementation Evidence

| 契約 | 實作／守衛 |
|---|---|
| 單一 state／三種 mapping | `TraversalState` ＋ `TraversalStateParamsSO` 三格 binding |
| CanEnter commit、Probe 後續變化不改寫 | `TraversalStatePlayModeTests` T2–T5 |
| None → Jump／空中不得進 | 同檔 T1／T6；`ArchitectureRegressionTests.A42` 守 Jump 無 Environment 引用 |
| Jump-vs-Climb capability／Vault 不被誤傷 | `TraversalSelectionPolicyTests` J1–J5 ＋ `TraversalStatePlayModeTests` selection cases |
| committed target／Probe 後續變化不改 plan | `TraversalWarpPlanTests` WARP1–WARP7 ＋ PlayMode committed-target case |
| warp delta 仍走 MotionDriver | `MotionDriver.ExecuteTraversalWarpedMovement`；A43／A46 守 movement authority 與禁止 direct position write |
| early recovery／Move／Idle handoff | `TraversalStatePlayModeTests` R1–R5 |
| query、policy 與黑板 ownership | A39–A46 ＋既有 A5 WriterRules |
| stationary candidate／asymmetric entry legality | `TraversalProbePlayModeTests` stationary case ＋ `TraversalSelectionPolicyTests` E1–E6／EN3–EN4（safe AlreadyClose／unsafe penetration） |
| ledge target／primary contact root／secondary committed-surface residual／piecewise vertical | `TraversalWarpPlanTests` M1–M8、HC2–HC6、V1–V5 |
| committed Hand IK quality pass | `TraversalHandIKController／Rig`；IK window tests；Architecture A49 守不查 Probe/Physics、不改 root/blackboard |
| destination clear 但 corridor blocked | `TraversalProbePlayModeTests.DestinationClearButEntryToTransferCorridorBlocked_IsRejected` |
| collision apply／execution evidence／restore | `MotionDriverCommittedMovementPlayModeTests` C0／CP1–CP4 ＋ State R1–R5 |
| V3 pure builders／controller shape writer | Architecture A47／A48 |

## 5. Acceptance Criteria

- [x] Unity 6000.5.1f1 import／compile 0 errors（2026-09-13）。
- [x] EditMode 全套與 architecture regression 全綠（394 total／393 passed／0 failed／1 existing skipped）。
- [x] PlayMode 全套（含 V1–V3、Jump／Roll／Action／Locomotion regression）全綠（41／41 passed）。
- [x] 玩家資產完成三格 animation＋bake binding 與 Animancer mapping；production wiring tests 3／3。
- [x] `StateMachineConfig` author Traversal rule：priority 30 > Jump 10、interrupt 空、valid transitions `Jump → Move → Idle`。
- [x] 為 Vault1m／Climb1m／Climb2m author Entry／hand contact／Transfer／Exit／Recovery markers，選 hand bones 並重烘。
- [ ] 調整 EntryPolicy band／ledge margin 與必要的 C1 centerOffset；C2／C3 只有證據要求時才啟用。
- [ ] Play 實測 stationary Jump、太遠／太近／側偏 Reject、三種 traversal；記錄 hand error、主體／肢體 penetration、requested-vs-actual delta、Exit error、recovery latency。

## 6. Revision Log

| 日期 | 修改 | 原因 |
|---|---|---|
| 2026-09-14 | D9 改為單一 primary contact 解 root；secondary hand 查詢 committed ledge surface、只做 residual/IK；窄 ledge 無 surface 時不壓縮 authored hand separation。移除 `SolveDual`，更新 M6 並新增 HC6；架構圖新增圖 19 | Climb2m 離線量測確認提前 release 已消除 reach>1，但舊雙手平均仍把兩手各留 9.64 mm residual；研究顯示成熟實作不以雙手同時硬約束 root，且每手 surface/contact 分工獨立 |
| 2026-09-13 | V3 新增 stationary sensing、pure EntryPolicy、ledge frame／interval、authored-marker+bake sampling、contact root solver、固定六 knot piecewise mapping、fixed-capsule corridor、C1 collision profile／restore／execution debug；完整 EditMode 394（393 pass／1 skip）、PlayMode 41／41 | Play 證據確認 speed gate、entry legality、endpoint-only warp、body-envelope mismatch 與 contact mapping 五個缺口；保留 Trial 至動畫 marker／collision quality Play 完成 |
| 2026-09-13 | Correctness／Debug pass：確認 production Bake 仍落入 endpoint fallback；safe close Entry、paired Contact correction＋capsule support clamp、piecewise Y、Exit hold、最小 Hand IK 與四組可觀測性落地；focused EditMode 80／80、Traversal PlayMode 31／31；full EditMode 407（406 pass／1 skip）、PlayMode 41／41 | 沿用既有 MotionDriver committed snapshot＋Presentation Pipeline／Animator IK seam，未建立新 ADR；保留 Trial 至 marker／binding／Play |
| 2026-09-12 | V2 新增純 Jump-vs-Climb policy、committed `TraversalWarpPlan`、MotionDriver delta execution 與 per-binding early recovery；完整 EditMode 379（378 pass／1 ignored）、PlayMode 36／36 | 使用者實際 Play 證明 fixed trajectory alignment error、Climb 搶 Normal Jump、animation tail recovery latency；保留 Trial 至人類 tuning／Play 完成 |
| 2026-09-12 | 使用者明確授權 production wiring；建立三組 direct-FBX TransitionAsset、30 FPS MotionBakeData 與 TraversalStateParams，接入 Player config／X Bot prefab；EditMode 363（362 pass／1 ignored）、PlayMode 26／26 | 勾選可由 asset inspection＋自動測試證明的兩項接線驗收；仍不把未進行的人類 Play／alignment quality 寫成通過 |
| 2026-09-12 | Runtime／Editor compile、EditMode 360（359 pass／1 ignored）、PlayMode 26／26 完成；勾選前三項自動驗收，保留 Editor wiring／Play quality 項未完成 | 只記錄可重跑的自動化證據，不把尚未進行的資產接線與人類 alignment 驗收寫成通過 |
| 2026-09-12 | 建立 ADR，狀態為 Trial；記錄 D1–D6 與本輪程式／測試 evidence 路徑 | 新增 `StateType.Traversal` 屬 FSM topology 變更；只記 Integration，不重寫已完成的 Probe／Classifier 決策 |
