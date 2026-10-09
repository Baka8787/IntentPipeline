# 52 — Traversal Concept Fragmentation / Semantic Duplication Audit（2026-10-10）

> **性質**：研究報告，**只讀不改**。runtime、資產、測試皆未動。格式與分類法比照 `docs/47`；判定前先對照既有裁決（`docs/48` §0 的做法），見 §0。
> **範圍**：`Core/Environment/Traversal*`、`Core/StateMachine/Traversal*`（含 `TraversalPlanBuilder`／`TraversalEntryPolicy`，它們分別住在 `TraversalStateParamsSO.cs`／`TraversalSelectionPolicy.cs`）、`States/TraversalState.cs`、`Presentation/Motion/TraversalWarpPlan.cs`、`Presentation/IK/TraversalHandIK*`、`Editor/Stages/TraversalContactDerivation.cs`、`MotionBakeData` 的 Traversal 區塊；治理 ADR-008（🟡 Trial）、`docs/22`／`23`／`30`／`39`／`40`。
> **邊界**：`MotionDriver.ExecuteTraversalWarpedMovement` 的**執行**屬 `docs/50`（MotionDriver 審查）。本報告只審到「選擇 → 計畫」與交給 MotionDriver 的契約；碰到執行面的現象只標記並轉交。
> **方法**：每個概念追 producer → ≥2 consumer 的 call site；數值取出貨資產（`X Bot.prefab`、`TraversalStateParams.asset`、`JumpStateParams.asset`、`Bake_*.asset`）。⭐ **關鍵 finding 皆在 live Unity Editor 上以原碼實測**（Edit Mode、不進 Play）：用 X Bot 的 `CharacterController`＋`TraversalProbe` 設定建臨時 rig、在臨時碰撞體上呼叫真實的 `Probe.Tick` → `TraversalState.CanEnter` → `TraversalPlanBuilder.TryBuild`／`TraversalWarpPlan.TryCreate`。腳本與輸出：`docs/artifacts/traversal-audit/`。
> **沒做的事**：沒有 Play、沒有人工觀察畫面；「執行後會怎樣」的敘述標為推導。
> **文件編號**：交辦原訂 `docs/49`，但該號已被 `docs/49-foot-contact-learning-guide.md` 佔用，故改用 **52**（避開平行的 50／51）。`docs/00-map.md` 未改，指標由使用者另補。

---

## 0. 本審查遵守的既有裁決（不重議）

| 來源 | 內容 | 對本審查的限制 |
|---|---|---|
| ADR-008 D2／I1／I5 | Probe 是唯一 physics owner；commit 後不回讀 Probe | 不把「plan 再查一次環境」當修法 |
| ADR-008 D7 | Jump-vs-Climb 是純 gameplay policy；Vault 永遠不受 Climb policy 過濾 | Vault 不走 jump-reach 是刻意的 |
| `docs/30` §F **I9**／§H | 感測範圍由動畫需求推導，`SetDerivedRangeRequirements` **單向推送兩個 float** | 推送方向本身不判成 ownership 倒置（見 T-07） |
| `docs/30` §C | Corridor 的 root 點是 **query evidence，不是 motion target** | Corridor 的 entry root ≠ committed entry 不算重複 |
| WORKLOG 2026-09-20 | Coverage 上下限放 binding（內容決策）；`climb1m` 下限依 jump **apex** 定為 1.00；1.20–1.65 是**已知缺口** | 缺口本身不是 finding；finding 只談「1.00 從哪來」 |
| WORKLOG 2026-09-20 ＋ `docs/22` §14.2 | Climb1m 測不到手部接觸 ⇒ **刻意**走 V2 endpoint fallback | 不把「Climb1m 沒有 piecewise」當缺陷；只審 fallback 內的語意 |
| `docs/22` §13.6、`docs/23` F-b | recovery 改用推導值：**延後、未裁決** | T-05 只記錄，不提案改值 |
| `docs/38` §13 | `IsGrounded` 不加否決層 | T-09 不碰 grounding 本身 |
| `docs/47` F-05 | 跳躍初速被 SelectionPolicy 複製 | 本報告以實測**更新**其嚴重度（T-03） |

---

## A. Executive Summary

**整體 fragmentation：Moderate（局部 High——集中在「落點／終點」一個概念）。**

Traversal 的骨架是本 repo 最健康的子系統之一：八層各自回答不同問題（`docs/30` §A）、Probe 是唯一 physics owner（A39 守）、commit 邊界清楚、每個否決都留理由（I8）。**碎裂不在層與層之間的職責，而在幾個「量」的定義上**——同一個量在 Probe、Fitter、PlanBuilder、endpoint fallback 各有一份算法，而且已在實測中分岔。

**最嚴重的 5 個概念：**

| # | 概念 | 一句話 | 實測 |
|---|---|---|---|
| 1 | **落點／committed exit**（T-01） | Probe 的 `LandingSurfaceDepth`、`DestinationPoint`、Fitter 的 `LandingCommittedDepth`、PlanBuilder 的 exit target 是四個數字；**plan 可以把終點放在已驗證落腳面之外** | 薄牆後方地面只剩 0.35 m：驗證到 0.600、面板顯示 committed **0.600**、plan 實際 exit **0.970**（地面在 0.63 就結束） |
| 2 | **Climb1m 的終點**（T-02） | endpoint fallback 直接用 Probe destination，而 Probe 的落腳掃描上限 = **所有 kind 自然落點的最大值（Vault 的 1.116）** ⇒ Climb1m 的終點由 Vault 的動畫決定；`docs/22` §18.4「終點用了探測常數」的修正只套到 piecewise | 深平台：終點 1.100 vs 動畫自然 0.898 ⇒ 多拉 **+0.20 m**；站 0.75 m 起攀 **+0.50 m**；0.8 m 平台 **−0.20 m** |
| 3 | **高度帶／「跳得過嗎」**（T-03） | 四套高度界線（Classifier、coverage、jump safeReach、人工抄入的 apex）；jump-reach 判斷**對出貨資料完全沒有決策力**，真正的界線是**手抄自 Jump bake 與 Hand IK guard 的 coverage 常數** | 高度掃描 0.40–2.40 m × 兩種深度：`ClimbWithinJumpReach` **一次都沒出現** |
| 4 | **兩種 plan 表示**（T-04） | piecewise 與 endpoint 在終點、朝向、recovery、post-exit 貼地、Hand IK、進場距離 6 個軸上各有一套語意；endpoint 對 Climb1m 是**正式路徑** | Climb1m 三個場景全部 `Endpoint`／`MissingTraversalBlock` |
| 5 | **Recovery 時刻**（T-05） | binding 0.85 與 bake 推導值取 max ⇒ Vault 的 bake recovery（0.4598）恆被蓋掉 | 資產值（已知未決，`docs/23` F-b） |

**`docs/47` F-05（跳躍初速 copy）的更新**：對玩家**逐位元一致**（v₀ 5.6564、g 16.777、apex 0.9535）；唯一分岔是敵人資產（JumpState fallback apex 2.867 vs policy「無法推導」），但 **Y Bot 沒有 `TraversalProbe`、敵人 config 沒有 Traversal rule ⇒ 不可達**。copy 仍是 copy，但更大的問題是 T-03：jump 能力真正生效的地方已經不是這段 copy，而是一個人工抄進 coverage 的常數。

**是否已出現「每條 function path 重新定義 gameplay truth」？** 有，**局限在「traversal 結束在哪」**：Probe 定義「面延伸多遠」、Fitter 定義「committed 多深」、PlanBuilder 定義「exit knot 在哪」、endpoint 定義「destination 就是終點」——四條路徑各回答一次，且 debug 面板顯示的是不會被執行的那一個。其餘概念（candidate、entry、selection 理由、commit）沒有這個問題。

**現在需要處理嗎？** T-01（終點越出驗證面）與 T-02（Climb1m 終點跨 kind 耦合）是**正確性**問題，建議下一個 traversal slice 開場先裁決語意（§G-1／G-2）；T-03 建議**現在決定 owner**，因為任何人調 Jump 倍率或 Hand IK guard 都會靜默讓 coverage 失效；T-04／T-05 屬 worth fixing later。

---

## B. Concept Inventory

| Concept | Canonical owner | Producers | Consumers | Alternate definitions | Risk |
|---|---|---|---|---|---|
| 障礙物幾何（height／depth／ledge frame） | `TraversalProbe` | `Probe.Tick`（`:293-640`） | Classifier、EntryPolicy、Selection、Fitter、PlanBuilder | 無（I1 成立） | Low |
| Traversal kind | `TraversalClassifier` | `Classify`（`:13-58`） | Selection、State（binding 選擇）、PlanBuilder（無） | Editor `ResolveObstacleTopHeight` 對 **clip** 判 vault/climb（不同對象，Healthy） | Low |
| **高度帶（哪個高度做哪個動作）** | **無單一 owner** | Classifier 界線（prefab 1.25／2.10＋遲滯 0.1）、binding coverage（1.00–1.20／1.65–2.05）、Selection jump safeReach（0.8035）、人工抄入的 apex | Selection | **4 份**（T-03） | **Medium** |
| Jump 能力（v₀、g、apex） | `JumpState._stageLaunch` | `JumpState.BuildStages`（`:112-163`） | `MotionDriver.ApplyJumpLaunch` | Selection 的 `TryGetGroundJumpLaunch`（copy，`:178-209`）；coverage floor 1.00（人工抄） | Medium（透過 T-03） |
| 進場距離（desired entry） | `MotionBakeData.TryGetDesiredEntryDistance` | bake（grip edge z ＋ 接觸前位移） | EntryPolicy、Probe 感測範圍（經 State 推送）、Fitter | 雙手時取**平均**，root solve 取 **primary（Left）**（T-06，潛在） | Low |
| 接觸時刻 | `TraversalContactDerivation`（Editor） | plant 偵測（手速 < 15% 自身最大速） | PlanBuilder、Fitter、DesiredEntry、Hand IK 窗 | 三個消費端各取 L／R／min／mean（T-06） | Low |
| 自然落點深度 | `MotionBakeData.TryGetNaturalExitDepth` | bake（exit 時位移 − desired entry） | Fitter、PlanBuilder、`GetRequiredLandingDepth` | — | Low |
| **落腳面延伸／committed exit** | **無單一 owner** | Probe（`LandingSurfaceDepth`、`DestinationPoint`）、Fitter（`LandingCommittedDepth`）、PlanBuilder（exit target）、endpoint（= `DestinationPoint`） | plan、debug 面板 | **4 份**（T-01、T-02） | **High** |
| 感測範圍需求 | `TraversalStateParamsSO`（由 bake 推導） | `GetRequiredSensingReach`／`GetRequiredLandingDepth` | Probe（經 `SetDerived*` 推送） | 落腳掃描上限是**跨 kind 的 max**（T-02 的根） | Medium |
| Traversal plan | `TraversalWarpPlan` | `TraversalPlanBuilder.TryBuild`（piecewise）、`TraversalWarpPlan.TryCreate`（endpoint，經 `MotionDriver`） | MotionDriver、Hand IK | **兩種表示**（T-04） | Medium |
| Recovery 時刻 | `TraversalState.GetRecoveryNormalizedTime` | binding `recoverNormalizedTime`、bake `RecoveryNormalizedTime` | State（`_canRecover`）、collision restore | max 合併（T-05） | Low–Medium |
| Grounded／落地事件 | `MotionDriver` | `SyncGroundedState` | State（CanEnter）、Probe（`NotGrounded`）、Audio／Footstep | traversal 期間 `JustLanded` 抑制；post-exit 貼地只給 piecewise（T-09） | Low |
| Hand IK 目標／權重 | plan 的 `ContactTargets` | PlanBuilder | `TraversalHandIKController` | 無（只讀 plan，A49） | Low |

### 主要概念的 Producer → Transform → Consumer

```text
落點／committed exit（❗四份）
Probe.Tick（:449-541）
  depthProbe 在 topHit + forward × Vault1mMaxDepth(0.6) 往下打
  landing 續掃：0.6 → EffectiveLandingScanDepth（= max(0.6, 所有 kind 自然落點最大值 1.116)），步長 0.1
     ⇒ LandingSurfaceDepth（已驗證的連續面延伸，最多 1.1）
  DestinationPoint =
     ├ 掃描有延伸：最遠的已驗證點
     ├ 沒延伸且薄牆：depthHit + forward × (radius 0.32 + margin 0.05)   ← ❗推到已驗證點之外
     └ depth ray 沒打到：capsuleBottom + forward × 0.6 (+0.37)            ← ❗憑空構造（在牆前方）
        ↓ candidate（兩個欄位都帶著）
TraversalAnimationFitter.Fit（:188-192）
  LandingCommittedDepth = min(bake 自然落點, LandingSurfaceDepth)    → 只給 debug 面板
TraversalPlanBuilder.TryBuild（TraversalStateParamsSO.cs:267-280）
  probeDepth = |Destination − Edge|；自然落點較近才用自然落點，否則用 Destination → exit/recovery knot
TraversalState.CommitWarpPlan（:328-342）piecewise 失敗時
  TraversalWarpPlan.TryCreate(target = candidate.DestinationPoint)  → 不看自然落點
```

```text
高度帶（❗四份，其中一份是人工抄寫）
Classifier：≤1.25(+0.1) ⇒ 1m 層（depth≤0.6 Vault／否則 Climb1m）；≤2.10(+0.1) ⇒ Climb2m；> ⇒ TooHigh
Selection：
  ① kindEnabled
  ② coverage（binding）：Climb1m [1.00, 1.20]、Climb2m [1.65, 2.05]、Vault 無限制
       1.00 ← 人工依 Jump apex 0.9535 選定（WORKLOG 2026-09-20）
       1.65 ← 人工依 Hand IK residual guard 0.35 m 的交叉點選定（docs/40 §13）
  ③ jump reach：apex(JumpStateParams.Stages[0].Bake) − margin 0.15 = 0.8035  ← 永遠在 ② 之下 ⇒ 不起作用
```

```text
Traversal candidate → commit（健康）
Probe.Tick（2.7）→ Classifier → Probe.Candidate
TraversalState.CanEnter（4）：EntryPolicy → Selection → Fitter → 鎖存三份結果
OnEnter／第一次 OnUpdateMotion：PlanBuilder（或 endpoint）一次 → MotionDriver（6）→ Hand IK（6.5）讀 plan
```

---

## C. Fragmentation Findings

### T-01 落點有四個定義，committed exit 可以越出已驗證的落腳面 — **High（正確性）**

**Canonical concept**：「本次 traversal 結束時 root 停在哪裡，而且那裡**確實有面**」。

**目前版本**
```text
TraversalProbe.cs:484-511   LandingSurfaceDepth：從 0.6 起每 0.1 m 往內打，直到沒打到或高度差 > 0.1
TraversalProbe.cs:513-541   DestinationPoint：見 §B 的三個分支
TraversalAnimationFit.cs:188-192,259-267   LandingCommittedDepth = min(自然落點, LandingSurfaceDepth)
TraversalStateParamsSO.cs:267-280          exitTarget：自然落點 < |Destination − Edge| ? 自然落點 : Destination
TraversalState.cs:328-342                  endpoint：target = DestinationPoint
```

**為什麼是同一概念**：四者都在回答「終點」。Fitter 的欄位名就叫 *committed*，面板把它和 `probe verified`／`animated` 並列（`TraversalDebugWindow.cs:168-170`），讀者會以為 plan 就停在那裡；但 plan 用的是另一條規則、另一個輸入（`DestinationPoint` 而非 `LandingSurfaceDepth`）。

**實測（live Editor，Vault1m，1.0 m 高 × 0.3 m 厚薄牆，站在理想距離 1.40 m）**

| 場景 | 牆後地面 | Probe 驗證 `LandingSurfaceDepth` | `DestinationPoint` 深度 | 面板 `LandingCommittedDepth` | **plan 實際 exit** |
|---|---|---|---|---|---|
| A | 8 m | 1.100 | 1.100 | 1.100 | 1.100 ✅ |
| B1 | 0.50 m（到 0.78） | 0.700 | 0.700 | 0.700 | 0.700 ✅ |
| **B2** | **0.35 m（到 0.63）** | **0.600** | **0.970** | **0.600** | **0.970** ❌ |
| **B3** | **無** | **NaN** | **−0.450（牆前方）** | **1.116** | plan **Failed**（piecewise `KnotRejected`；endpoint `HorizontalCorrectionExceeded` 2.289 m） |

- **B2**：Probe 只驗證到 0.600（0.7 那一根打空），但 `!surfaceContinues` 分支把 destination 再往前推 `radius + margin = 0.37` m——那是為了讓**淨空查詢**的膠囊完整越過薄牆（`:523-529` 註解），卻同時成了 **committed 終點**。plan 停在 0.970；地面在 0.63 就結束 ⇒ 膠囊整顆在虛空上（推導：exit 後進入 Falling；未 Play）。面板卻顯示 committed 0.600。
- **B3**：薄牆後沒有任何地面（例如懸崖邊的矮牆）仍被分類為 **Vault1m**（depth ray 沒打到 ⇒ `depth = 0.6 − ε` ⇒ 「薄」），Entry、Selection 全部通過，`CanEnter == true`；兩種 plan 都建不起來 ⇒ `TraversalState` 退回 `ExecuteCommittedCurveMovement`（未 warp 的 baked curve）。Fitter 同時回報 `Fitted`、committed 1.116。**沒有任何一層否決「沒有落腳面」**——Classifier 把「沒打到」當「薄」、`DestinationBlocked` 只查淨空不查支撐、Fitter 退回動畫值、plan 用 `Failed` 收尾。

**分類**：③ Representation fragmentation（同一終點四種表示）＋ ② Duplicate fact derivation（Fitter 與 PlanBuilder 各算一次 committed landing，輸入不同）。`DestinationPoint` 同時承擔「query 用的位置」與「motion target」——正是 `docs/30` §C 自己警告過的 query evidence／motion target 混用，只是發生在 destination 而不是 corridor。
**風險**：正確性（B2 終點越出地面）、可觀測性誤導（面板顯示的 committed ≠ 執行值）、B3 是未定義的 gameplay 行為（是否允許翻下懸崖從未被裁決）。
**最小修法（未實作）**：讓 Fitter 與 PlanBuilder 呼叫同一個 `ResolveCommittedExit(bake, candidate)`；committed exit 一律 ≤ `LandingSurfaceDepth`，capsule 前推只留在 query 用的 `destinationQueryRoot`。B3 需使用者裁決（見 §J）。

---

### T-02 Climb1m 的終點由 Vault 的動畫決定（跨 kind 耦合） — **Medium（正確性，Climb1m 未經 Play）**

**Canonical concept**：「**這支** clip 自然會停在邊緣內多遠」。

**目前版本**
```text
TraversalStateParamsSO.cs:851-863  GetRequiredLandingDepth = 所有啟用 kind 的自然落點「最大值」 = Vault1m 1.116
TraversalState.cs:95-97            推給 Probe（每次 CanEnter／Initialize）
TraversalProbe.cs:197-200          EffectiveLandingScanDepth = max(0.6, 1.116) ⇒ 對所有 kind 都掃到 1.1
TraversalState.cs:328-342          Climb1m（無 Traversal block）⇒ endpoint：target = DestinationPoint
```
piecewise 路徑用 `min(自然落點, probe)` 擋掉了這個耦合（`docs/22` §18.4「終點用了探測常數」的修法），**endpoint 路徑沒有**。

**實測（live Editor，Climb1m，1.1 m 高平台）**

| 場景 | 起攀距離 | destination | 動畫自然終點 | endpoint 需要的水平修正 |
|---|---|---|---|---|
| D 深平台 | 0.45 m | 1.100 | 0.898 | **+0.202 m** |
| D2 深平台 | 0.75 m（Entry 仍合法） | 1.100 | 0.598 | **+0.502 m** |
| E 0.8 m 平台 | 0.45 m | 0.700 | 0.898 | **−0.198 m** |

修正量在 warp 窗 [0, 0.569] 內被吃掉 ⇒ 推導為「爬上去時身體被往前（或往後）拖 20–50 cm」。若 Vault 被停用，同一個 Climb1m 的終點會跟著變——**一個 kind 的內容開關改變另一個 kind 的終點**。

**分類**：③ Representation fragmentation（「landing budget」是 per-clip 事實，卻被聚合成 per-probe 常數）＋ Special-case（修正只存在於 piecewise 路徑）。
**風險**：行為漂移（Climb1m 2026-09-20 啟用、零 Play 證據）、改一個 kind 的資產會影響另一個 kind。
**最小修法（未實作）**：endpoint 路徑也套 `min(該 clip 的自然終點, probe 驗證面)`；Climb1m 沒有 Traversal block，但自然終點可由 `GetHorizontalDisplacementAt(Duration) − desired entry` 求得（不需要手部接觸）。

---

### T-03 高度帶有四個定義；jump-reach 判斷已無決策力，真界線是手抄常數 — **Medium**

**Canonical concept**：「這個高度該由哪個動作處理（或沒人處理）」。

**版本**
```text
Classifier（X Bot.prefab）  climb1mMaxHeight 1.25、climb2mMaxHeight 2.10、heightHysteresis 0.1
Coverage（TraversalStateParams.asset）  Climb1m [1.00, 1.20]、Climb2m [1.65, 2.05]、Vault [0, 0] = 不限
Selection jump reach  apex 0.9535 − margin 0.15 = safeReach 0.8035（TraversalSelectionPolicy.cs:132-147）
人工抄寫  Climb1m 下限 1.00 ← Jump apex 0.9535（WORKLOG 2026-09-20「正確的基準是 apex」）
          Climb2m 下限 1.65 ← Hand IK maximumResidualDistance 0.35 的交叉點（docs/40 §13；X Bot.prefab:4911）
```

**實測（live Editor，真實 Probe＋Classifier＋Selection，高度 0.40–2.40 m，步長 0.05）**
```text
厚 0.3 m：0.40–1.25 Vault1m→ContextualVault [TRAV] ／ 1.30–1.65 Climb2m→OutsideAnimationCoverage [jump]
          ／ 1.70–2.05 Climb2m→ClimbAboveJumpReach [TRAV] ／ 2.10 Outside ／ ≥2.15 TooHigh
厚 3.0 m：0.40–1.00 Climb1m→OutsideAnimationCoverage [jump] ／ 1.05–1.20 Climb1m→ClimbAboveJumpReach [TRAV]
          ／ 1.25 Outside ／ 1.30–1.65 Climb2m→Outside ／ 1.70–2.05 [TRAV] ／ 2.10 Outside ／ ≥2.15 TooHigh
```
⇒ **`ClimbWithinJumpReach` 一次都沒有出現。** coverage 檢查排在 jump-reach 之前（`:74` vs `:82`），而兩個 coverage 下限都高於 safeReach，所以 jump-reach 分支對出貨資料是死路徑；`normalJumpReachSafetyMargin`（0.15）同樣無效果。

**為什麼是同一概念**：「Jump 搆得到就讓 Jump」本來由 policy 從 Jump bake **推導**（ADR-008 D7）。現在決定權實際上在 coverage，而 coverage 下限是**人把 Jump 的 apex 抄過來**——CLAUDE.md「從 Bake Data 讀，不要手抄進 config」的反例。同樣地 Climb2m 的下限是把 Hand IK 的 guard 抄過來。調 `JumpStateParams.HeightMultiplier` 或 `maximumResidualDistance` 都不會讓 coverage 跟著動。
另外 Classifier 的高度界線現在只剩「貼 1m／2m 標籤」與 `TooHigh`：1.20–1.25 被標成 Climb1m、1.25–1.65 被標成 Climb2m，但兩者都由 coverage 否決——kind 標籤已不代表「做得到」。

**分類**：② Duplicate fact derivation（apex、guard 被人工複製成 coverage 常數）＋ ③ Representation fragmentation（同一條高度軸四套界線）。
**`docs/47` F-05 的更新**：copy 本身逐位元一致（玩家）；敵人分岔（2.867 vs unknown）不可達（Y Bot 無 Probe、敵人 config 無 Traversal rule）⇒ F-05 降為 **Low**；`docs/47` G-4「單一 JumpLaunch 推導」仍成立但價值有限——真正該處理的是 coverage 下限的來源。
**風險**：靜默失效（調 Jump 或 Hand IK 參數後出現新的死區或重疊）、測試保護假象（J1／S1 測的分支在出貨資料不可達）。

---

### T-04 兩種 plan 表示在 6 個軸上語意不同；endpoint 是 Climb1m 的正式路徑 — **Medium（Special-case architecture）**

**Canonical concept**：「commit 時建立的 traversal 計畫」。

| 軸 | piecewise（`TraversalPlanBuilder`） | endpoint（`TraversalWarpPlan.TryCreate` 經 `MotionDriver`） |
|---|---|---|
| 起點 | `candidate.SensedRootPosition`（Probe 2.7） | `MotionDriver.transform.position`（commit 當下；同帧、目前數值相同） |
| 目標朝向 | `−wallNormal`（ledge frame） | `candidate.Forward`（移動方向或 facing；可偏牆法線 ≤ 35°） |
| 終點 | `min(自然落點, probe)` | `DestinationPoint`（T-01／T-02） |
| 進場距離 | bake 推導 | 全域 `desiredWallDistance` 0.45（Climb1m 無 bake） |
| Recovery | `max(binding 0.85, bake recovery)` | binding 0.85 |
| Post-exit 貼地 | `MotionDriver.cs:1617` 只對 `plan.IsPiecewise` 施加 rebound | 無 ⇒ `docs/47` F-08 型的「整段 isGrounded=false」仍在（推導，轉 `docs/50`） |
| Hand IK | 有 | `RootMappingUnavailable` |

**為什麼值得記**：endpoint 原本是「資料缺損時的退化路徑」（ADR-008 D4），2026-09-20 起因 Climb1m 測不到手部接觸而成為**正式出貨路徑**（刻意，§0）。退化路徑升格後，它對每個概念的回答（終點、朝向、recovery、貼地）都沒有隨 piecewise 的修正一起演進。
**分類**：Special-case architecture（同一概念兩套實作，以「bake 有沒有 Traversal block」為 bool 分支）。
**風險**：修 piecewise 時 endpoint 漏改（T-02 就是一例）；Climb1m 的品質問題不會在 Vault／Climb2m 的測試中出現。
**不建議**合併成一個 builder（見 §H）；建議把「終點」「朝向」這類**語意**抽成兩條路徑共用的規則。

---

### T-05 Recovery 時刻兩個來源、以 max 合併 — **Low–Medium（已知未決）**

`TraversalState.cs:402-408`：`recovery = max(binding.RecoverNormalizedTime, plan.RecoveryKnot.t)`。
出貨值：binding 三格皆 **0.85**；bake recovery Vault1m **0.4598**、Climb2m **0.8704**。⇒ Vault 的 bake 推導值恆被蓋掉（實際 0.85 ≈ 2.55 s／fit 0.966），只有 Climb2m 由 bake 決定。endpoint 路徑只用 0.85。
`docs/22` §13.6 明載「recovery 是 gameplay 交接時機，本階段不動」，`docs/23` F-b 提案 Arrival 語意未裁決 ⇒ **本報告不提案改值**，只記錄：bake 的 `recoveryNormalizedTime` 名義上是 recovery 的來源，實際對 Vault 不生效，屬「看起來可調但其實不能」。
**分類**：③。

---

### T-06 「接觸時刻／哪隻手」三種取法 — **Low（潛在）**

| 消費端 | 取法 | 位置 |
|---|---|---|
| `TryGetDesiredEntryDistance` | 使用中的手**平均**（grip z ＋ 該手接觸前位移） | `MotionBakeData.cs:849-875` |
| `TraversalAnimationFitter.GetApproachDistance` | **最早**接觸時刻的位移 | `TraversalAnimationFit.cs:270-293` |
| `TraversalPlanBuilder` root solve | **primary（雙手固定 Left）** | `TraversalStateParamsSO.cs:182-238` |

`MotionBakeData.cs:844` 的註解要求「必須與 PlanBuilder 的 root solve 用同一個基準點」——基準點（grip edge）一致了，但**手**沒有一致。出貨資料下差異為 0：Vault 只用左手；Climb2m 左右同時接觸（0.0648）、grip z 差 0.34 mm。一旦出現「雙手、先後接觸、接觸前 root 有位移」的 clip，站在理想距離也會在接觸瞬間被拉（`docs/22` §18.2 同型 bug）。
**分類**：② Duplicate fact derivation（潛在）。

---

### T-07 `SetDerivedRangeRequirements`／`SetDerivedCoverageBands`：consumer 回頭設定 producer — **Healthy specialization**（附兩個註記）

**判定**：不是 ownership 倒置。Probe 擁有的是「怎麼量」，「要量多遠」本來就是需求方的資訊；Core/Environment 不得認識 bake／binding（I10），所以需求以兩個 float 單向推入（I9、`docs/30` §H 的真實 bug 修法）。coverage 帶只用於畫環、不參與判定（`TraversalProbe.cs:175-188`）。單一 writer（每角色一個 State）。
**註記 1（時序）**：`CanEnter` 在 `!JumpRequested` 時第 112 行就 return，推送只發生在 `Initialize` 與「按下跳躍那一帧」；而 Probe 在 2.7 先 tick ⇒ Inspector 切換 kind 後，**按鍵那一帧用的是舊範圍**，下一帧才生效。影響極小，但與註解「每次 CanEnter 都重推」的字面不符。
**註記 2（真問題在聚合語意，不在方向）**：落腳掃描上限是跨 kind 的 **max** ——這讓 Probe 的 destination 帶有「最遠那支動畫」的偏好，T-02 由此而來。修法應在 consumer 端（endpoint 終點），不是取消推送。

---

### T-08 `Vault1mMaxDepth`（0.6）一個常數五個角色 — **Low**

`TraversalProbe.cs`：①Vault／Climb 分類門檻（`:466-470`）②depth ray 的前推量（`:451`）③落腳掃描起點（`:487-491`）④depth ray 打空時的假 destination 偏移（`:515`）⑤`EffectiveLandingScanDepth` 下限（`:197-200`）。`docs/22` §18 已把「分類常數 ≠ 落地預算」拆過一次（③⑤ 的由來），④ 仍沿用，是 T-01 B3 的直接成因。
同類「看起來可調但無作用」：`TraversalProbeSettings.probeMinSpeed`（只在 `Classifier.IsFinite` 被驗證，無任何判定使用，V3 起 dead）、binding `warpEndNormalizedTime` 0.8（`overrideWarpWindowEnd = 0` ⇒ 由 bake 推導，`docs/30` 附錄 A 已記）、三格 collision profile 通道全為 0。
**分類**：③（常數語意過載）。

---

### T-09 Grounded／落地語意在 traversal 期間 — **Low**（大部分 Healthy）

- `IsGrounded` 原值不被改寫（遵守 `docs/38` §13）；`TraversalState.CanEnter` 與 Probe 的 `NotGrounded` 讀同一個值——兩個閘門、同一來源，Healthy。
- `JustLanded`／`JustLeftGround` 以單一規則 `suppressGroundEdges = HasActiveTraversalPlan`（`MotionDriver.cs:2734`）抑制；`_wasGrounded` 照常前進 ⇒ 離開 traversal 時不補發。Healthy（committed traversal 擁有這段垂直語意）。
- **分岔**：exit 後的 rebound 貼地只給 piecewise（`MotionDriver.cs:1617`）⇒ endpoint（Climb1m）與 Failed plan（T-01 B3）仍是「整段 isGrounded=false、Foot IK 關閉」。屬執行面 ⇒ **轉 `docs/50`**。
**分類**：① Healthy ＋ T-04 的一個軸。

---

### Editor ↔ runtime（交辦線索 3）的結論

| 量 | Editor 端 | Runtime 端 | 判定 |
|---|---|---|---|
| Exit 時刻 | `TraversalContactDerivation.Measure:260` 呼叫 `bake.GetVerticalSettleNormalizedTime()` | `TraversalState.ResolveEndpointWarpEnd:396` 呼叫同一個方法 | ✅ 同源 |
| 接觸時刻／手 | plant 偵測寫入 bake | 三個消費端取法不同 | ⚠️ T-06 |
| 進場距離、自然落點 | 不計算 | `MotionBakeData` 單一實作，四個 consumer 共用 | ✅ 同源（消費端的用法分岔見 T-01） |
| 接近距離（approach） | 不計算 | Fitter 自算（最早接觸） | ⚠️ T-06 |
| 障礙物頂面（clip 內） | `ResolveObstacleTopHeight`（climb vs vault 以終高 < 峰值一半判） | 不使用 | ✅ 對象是 clip，不是世界幾何 |

⇒ **Editor 與 runtime 沒有各自一套「接觸點」定義**；bake 是單一來源。分岔都發生在 runtime 消費端。

---

## D. Special-case Object Inventory

| Item | Created for | Consumers | Independent domain meaning? | 建議 |
|---|---|---|---|---|
| endpoint plan（`TraversalWarpPlan.TryCreate`＋`MotionDriver.TryCreateTraversalWarpPlan`） | V2；資料缺損退化 | Climb1m（正式）、任何 piecewise 失敗 | 有（無接觸資料的 traversal） | Keep；把終點／朝向語意改為與 piecewise 共用（T-04） |
| `TraversalAnimationFit.LandingCommittedDepth` | `docs/24` §8 debug | 只有 `TraversalDebugWindow` | **無**：顯示一個不會被執行的值 | Merge candidate（→ 單一 committed exit） |
| `DestinationPoint` 的 capsule 前推（`:527-528`） | 薄牆的淨空查詢 | CheckCapsule、corridor、**plan 終點** | 只對 query 有 | Investigate（拆成 query 與 target） |
| `DestinationPoint` 的「沒打到」分支（`:515`） | 無地面時給 CheckCapsule 一個點 | plan（失敗） | 無 | Investigate（B3 裁決後決定） |
| coverage `minHeight` 1.00／1.65 | 讓 Jump／Hand IK 的極限可被 Selection 看到 | Selection | 有（內容邊界），但值是別人的事實 | Investigate（T-03：推導 vs authored） |
| `normalJumpReachSafetyMargin` | ADR-008 D7 | Selection（無決策力） | 有，但被 coverage 遮蔽 | Investigate |
| `TryGetGroundJumpLaunch` | D7 | Selection | 無（`docs/47` F-05） | Merge candidate（Low） |
| `TraversalProbeSettings.probeMinSpeed` | V1 速度閘門 | 無 | 無（V3 已移除速度閘門，ADR-008 D8） | 移除候選 |
| binding `warpEndNormalizedTime`／`overrideWarpWindowEnd` | V2 authored 窗 | `ResolveEndpointWarpEnd`（只在 override 時） | 有（覆寫入口） | Keep（已有 `docs/30` 附錄記錄） |
| Probe 的 `RecordEntry/Selection/AnimationFit` ＋ 半個檔案的 debug | `docs/23` §A10（否決要看得見） | `TraversalDebugWindow`、runtime lines | 有（I8 可觀測性） | Keep；註：評估型別宣告在 `Project.Core.Environment` 命名空間、檔案住在 `Core/StateMachine/`，是為了讓 Probe 能存它們而不違反 A44 |
| `TraversalExecutionModel.Segmented` | Climb2m（ADR-008 2026-09-21 修訂） | PlanBuilder、MotionDriver | 有，由 binding 選擇 | Keep（revert 路徑＝一個資產欄位） |

---

## E. Repeated Decision Rules

| 規則 | 版本數 | 位置與值 |
|---|---|---|
| 「終點在哪」 | **4** | Probe `LandingSurfaceDepth`；Probe `DestinationPoint`（三分支）；Fitter `min(自然, 驗證面)`；PlanBuilder `自然 < |Dest−Edge| ?`；（endpoint 直接用 Dest） |
| 「哪個高度做哪個動作」 | **4** | Classifier 1.25／2.10（±0.1）；coverage 1.00–1.20／1.65–2.05；safeReach 0.8035；人工抄寫的 apex／guard |
| 「Jump 能跳多高」 | **3** | `JumpState.BuildStages`；`TryGetGroundJumpLaunch`；coverage 1.00（人工） |
| 「接觸時刻」 | **3** | mean（desired entry）；min（approach）；primary Left（root solve） |
| 「Recovery 時刻」 | **2** | binding 0.85；bake recovery（max 合併；endpoint 只用 binding） |
| 「traversal 朝向」 | **2** | `−wallNormal`（piecewise、EntryPolicy）；`candidate.Forward`（endpoint） |
| 「有沒有落腳面」 | **3（且沒有人否決）** | Classifier 把「沒打到」當薄；`DestinationBlocked` 只查淨空；Fitter 退回動畫值 |
| 「角色在地上嗎」 | **2（同源）** | `TraversalState.CanEnter`、Classifier `NotGrounded`——皆讀 `data.IsGrounded` ⇒ Healthy |

---

## F. Data Plumbing

### F-P1：感測需求（bake → binding → State → Probe，3 跳）
```text
MotionBakeData（desired entry、自然落點）→ TraversalStateParamsSO.GetRequired*（跨 kind 聚合）
 → TraversalState.PushDerivedSensingRequirements → Probe._required*（兩個 float）
```
**判斷**：合理邊界（I9／I10）。問題只在「聚合成 max」這個語意（T-02），不是跳數。⇒ **保持 pipeline**，改 consumer。

### F-P2：Jump 能力（JumpStateParams → Selection；以及「人 → coverage」）
```text
JumpStateParams.Stages[0].Bake ─┬─ JumpState.BuildStages（正本）
                                ├─ TraversalSelectionPolicy.TryGetGroundJumpLaunch（copy，無決策力）
                                └─ 人眼讀 apex 0.9535 → 手填 coverageMinHeight 1.00（真正生效的那條）
```
**判斷**：canonical source 存在，但真正生效的通道是**人**。⇒ 二選一（§G-3）：讓 coverage 下限由 jump 能力推導，或明文宣告 coverage 是 authored 內容邊界並刪掉無效的 jump-reach 分支。

### F-P3：Hand IK guard → coverage（同上，跨層）
`X Bot.prefab TraversalHandIKController.maximumResidualDistance 0.35`（Presentation）→ 離線量測（`docs/40`）→ `TraversalStateParams.asset climb2m.coverageMinHeight 1.65`（Core 資料）。
**判斷**：這是**量測結論**被凍結成常數，不是 runtime 依賴——Core 不應反過來讀 Presentation 的 guard。⇒ **保持**，但在 binding tooltip／`docs/40` 標明「改 guard 要重量 coverage」（記錄層級的連結即可）。

### F-P4：committed exit（Probe → candidate → Fitter／PlanBuilder／endpoint，各自重算）
**判斷**：資料只走一跳，問題是**每個終點各自重新推導**（T-01）。⇒ 收斂為單一純函式，三個 consumer 共用。

---

## G. Top Refactor Candidates（最多 5 個，皆未實作）

### G-1 單一 committed exit 規則
```text
Current   Fitter、PlanBuilder、endpoint 各自決定終點；DestinationPoint 同時是 query 點與 motion target
→ Problem  終點越出已驗證面（B2）、面板顯示值 ≠ 執行值、無面時仍 commit（B3）
→ Canonical concept  「本次 traversal 的 committed exit：深度 ≤ 已驗證面延伸、≤ 本 clip 自然落點」
→ Desired ownership  一個純函式（Core/StateMachine），Fitter／PlanBuilder／endpoint 共用；Probe 只發布量測
→ What should disappear  Fitter 自己的 min()；PlanBuilder 的 |Dest−Edge| 比較；destination 的 capsule 前推作為 target
→ What should remain  destinationQueryRoot（淨空查詢用）；LandingSurfaceDepth（量測）
```
需使用者裁決：B3（無落腳面時要不要允許 Vault）。屬單一子系統內的語意修正 ⇒ Living Spec（`docs/22`／`docs/30`），**不需新 ADR**（ADR-008 Trial 範圍內）。⚠️ 會改 Vault／Climb1m 的終點 ⇒ 需 PlayMode／live probe 前後對照。

### G-2 per-clip 落地預算（Climb1m 終點脫鉤）
```text
Current   EffectiveLandingScanDepth = max over kinds；endpoint 用 Probe destination
→ Problem  Climb1m 被 Vault 的自然落點拉 +0.2～+0.5 m（T-02）
→ Canonical concept  「這支 clip 自然停在哪」
→ Desired ownership  endpoint 路徑也走 G-1 的規則；Climb1m 的自然終點由 bake 位移求得（不需手部接觸）
→ What should disappear  endpoint 對 DestinationPoint 的直接使用
→ What should remain  Probe 的跨 kind 掃描上限（它是「掃多遠」，不是「停多遠」）
```

### G-3 決定「Jump 搆得到」的 owner
```text
Current   policy 推導 safeReach（無決策力）＋ 人工抄 apex 進 coverage（真正生效）
→ Problem  調 Jump 倍率／Hand IK guard 不會反映到 coverage；J1/S1 測的分支在出貨資料不可達
→ 選項 A  coverage 下限 = max(authored, apex) 由 policy 在 runtime 推導（保留 authored 作上限）
→ 選項 B  明文宣告 coverage 是 authored 內容邊界，刪掉 jump-reach 分支與 margin（D7 改為「coverage 由人對照 apex 設定」）
→ What should disappear  （A）手填的 1.00；（B）TryGetGroundJumpLaunch 與 safety margin
```
⚠️ 選項 B 觸及 ADR-008 **D7** 的決策內容 ⇒ 因 ADR-008 仍是 Trial，可直接修訂並記入修訂紀錄（不開新 ADR）。

### G-4 兩種 plan 共用語意規則
```text
Current   piecewise／endpoint 在終點、朝向、recovery、貼地上各一套（T-04）
→ Canonical concept  commit 時的 traversal 計畫語意（終點、朝向、recovery）
→ Desired ownership  語意規則共用（G-1、朝向一律 −wallNormal）；兩種 trajectory 生成器保留
→ What should remain  兩個 builder 本身（資料能力不同）
```
執行面的 post-exit 貼地 ⇒ 轉 `docs/50`。

### G-5 Recovery 單一規則
等 `docs/23` F-b（Arrival 語意）裁決；裁決前至少讓面板顯示「bake recovery 被 binding 蓋掉」。

**刻意不列入**：T-06（出貨資料零影響，等出現第一支錯開接觸的雙手 clip 再處理）、T-07（Healthy）、T-08 的 dead 欄位清理（太小，併入下一個觸及 Probe 的 slice）。

---

## H. Things That Look Fragmented But Should NOT Be Merged

| 看起來重複 | 為什麼保持分離 |
|---|---|
| Probe → Classifier → Entry → Selection → Fit → Commit 六段 | 每段回答不同問題、各自純函式可測（`docs/30` §A「為什麼這八層不能合併」） |
| Classifier 的 kind vs Editor `ResolveObstacleTopHeight` 的 vault/climb 判斷 | 一個分類**世界幾何**，一個分類**動畫 clip**；對象不同 |
| Corridor 的 entry／transfer root vs committed entry／knot | Corridor 是 query evidence，不是 motion target（`docs/30` §C，2026-09-13 真實 bug 的修法） |
| piecewise 與 endpoint 兩個 trajectory builder | 資料能力不同（有無手部接觸資料）；應共用**語意規則**（G-4），不應合成一個 builder |
| `SetDerivedRangeRequirements` 推送 vs Probe 自己讀 bake | 推送是 I10 的必要結果；讓 Probe 讀 bake 會讓 Core/Environment 依賴 Presentation |
| Probe 存放 Entry／Selection／Fit 結果 | 只為 I8 可觀測性；Probe 不消費它們（`TraversalDebugWindow` 是唯一讀者） |
| `IsGrounded` 在 State 與 Classifier 各檢查一次 | 同一來源、兩個不同的拒絕點（准入 vs 量測有效性） |
| traversal 期間抑制 `JustLanded` vs 保留 `IsGrounded` 原值 | 「不回報事件」與「改寫事實」是兩件事（`docs/38` §13；MotionDriver 註解） |
| Hand IK 的 residual guard vs PlanBuilder 的 support-plane clamp | 一個是 Presentation 的品質閘門，一個是 root solve 的物理約束；兩者都觀測 residual，但決策對象不同 |
| `TraversalHandIKWindow`（bake）vs Hand IK runtime 權重 | runtime 只 `Evaluate` bake 的窗，不重新推導（`TraversalHandIKController.cs:259-267`） |
| 三格 binding 各自的 `maxHorizontal/VerticalCorrection` | per-clip 內容容差，不是同一事實的 copy |

---

## I. Architecture Health Verdict

> **Traversal 的層次切分仍然是「合理地把 domain 拆開」；碎裂集中在一個量——「traversal 結束在哪」——它在四條路徑上各被定義一次，而且退化路徑（endpoint）升格成正式路徑後，沒有跟上正式路徑的修正。**

**支持「骨架健康」的證據**
1. Probe 是唯一 physics owner（A39），commit 後不回讀（I5），每個否決都有理由（I8）——實測中每個場景都能讀出完整的 reject／selection／fit／plan 理由。
2. Editor 與 runtime 共用 bake 這個單一來源；exit 時刻甚至呼叫同一個方法。
3. `SetDerived*` 推送是有文件、有真實 bug 背書的邊界設計（I9），不是倒置。
4. Hand IK 只讀 plan（A49），沒有重新推導任何事實。

**支持「量的碎裂」的證據**
1. 終點：四個定義、面板顯示的不是被執行的那個、可以越出驗證面（T-01，實測）。
2. Climb1m 的終點由 Vault 的 bake 決定（T-02，實測）。
3. 「Jump 搆得到」的判斷在出貨資料上無決策力，真界線是人工抄寫的常數（T-03，實測）。
4. endpoint 與 piecewise 在 6 個軸上語意不同（T-04）。

**趨勢判斷**：與 `docs/47` 的觀察同型——每次 Play 修正都正確地修在**當時正在用的那條路徑**（`docs/22` §18.4 修 piecewise 終點、coverage 修模糊帶），但**另一條路徑或另一個入口沒有一起被收斂**。建議把「修正一個量的定義時，grep 所有回答同一問題的入口」列入 traversal slice 的收尾條件；這比另開重構專案便宜。

---

## J. ❓ 需要使用者裁決的點

| # | 問題 | 影響 |
|---|---|---|
| J1 | committed exit 是否**一律不得超過已驗證的落腳面**？（B2 現況會越出 0.34 m） | G-1 的核心；會改 Vault 窄後地面的終點 |
| J2 | 薄牆後**沒有落腳面**（懸崖邊矮牆）時，要不要允許 Vault？（現況：允許，plan Failed、走未 warp 的曲線） | 決定 B3 是 reject 還是新行為 |
| J3 | Climb1m 的終點跟**自己 clip 的自然終點**，還是跟 Probe？ | G-2；Climb1m 尚無 Play 證據，可與首次 Play 驗收一起做 |
| J4 | 「Jump 搆得到」的 owner：**推導（選項 A）還是 authored coverage（選項 B）**？ | G-3；選 B 需修訂 ADR-008 D7（Trial 內可直接修） |
| J5 | Recovery：是否在此輪一併裁決 `docs/23` F-b？ | G-5；否則維持現狀 |

---

## 附錄 A：live Editor 實測

- 腳本：`docs/artifacts/traversal-audit/trav_probe.eval.cs.txt`（`unity cmd eval_file`；eval 有 5 s 主執行緒上限，需拆段跑）。輸出：`docs/artifacts/traversal-audit/probe-output-2026-10-10.txt`。
- Rig：`X Bot.prefab` 的 `CharacterController`（center (0, 0.91, 0)、height 1.8266、radius 0.32、skin 0.03、step 0.3）＋ `TraversalProbe.settings` 反射複製；`TraversalState` 以 `PlayerStateMachineConfig` 初始化，呼叫真實 `CanEnter`；plan 以 `CommitWarpPlan` 相同參數呼叫 `TraversalPlanBuilder.TryBuild`／`TraversalWarpPlan.TryCreate`、`ResolveEndpointWarpEnd` 以反射呼叫原碼。
- 幾何：臨時 `HideAndDontSave` 方塊（遠離場景，(5000, 1000, 0)），結束時全部 `DestroyImmediate`；事後確認無殘留。**未建立或修改任何 `Assets/` 內容。**
- 限制：Edit Mode，不是 Play；`MoveDirection` 正對牆（未測側向接近）；未測執行（MotionDriver）與畫面。

## 附錄 B：本次讀過的關鍵 call site

`TraversalProbe.cs:143-200,293-640,646-676` ｜ `TraversalClassifier.cs:13-125` ｜ `TraversalProbeSettings.cs`（全） ｜ `TraversalSelectionPolicy.cs:17-212,489-633` ｜ `TraversalAnimationFit.cs:109-310` ｜ `TraversalStateParamsSO.cs:98-468,820-899` ｜ `States/TraversalState.cs`（全） ｜ `JumpState.cs:105-184` ｜ `TraversalWarpPlan.cs:347-420` ｜ `MotionBakeData.cs:399-504,785-933` ｜ `TraversalContactDerivation.cs:1-473` ｜ `TraversalHandIKController.cs:194-354` ｜ `MotionDriver.cs:1444-1487,1598-1625,2726-2760` ｜ `ArchitectureRegressionTests.cs:1290-1440` ｜ `TraversalSelectionPolicyTests.cs`（測試清單） ｜ `X Bot.prefab`／`TraversalStateParams.asset`／`JumpStateParams.asset`／`EnemyJumpStateParams.asset`／`EnemyStateMachineConfig.asset`／`Bake_Vault1m／Climb1m／Climb2m／Jump_place_ALL_short.asset`（唯讀 grep）

## 附錄 C：文件與磁碟不一致（只標記，未改）

| 位置 | 文件 | 磁碟 |
|---|---|---|
| `docs/30` 附錄 B | `climb1mEnabled = 0` | **1**（2026-09-20 啟用） |
| `docs/30` 附錄 B | Recovery「Bake……與 binding 取大」未提 Vault 實際恆為 0.85 | 見 T-05 |
| `TraversalState.cs:89-90` 註解 | 「每次 CanEnter 都重推」 | 只在按下跳躍且著地時推（T-07 註記 1） |
