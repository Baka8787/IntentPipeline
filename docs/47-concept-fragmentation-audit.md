# 47 — Concept Fragmentation / Semantic Duplication Audit（2026-10-07）

> **性質**：研究報告，**只讀不改**。runtime、資產、測試皆未動。
> **範圍**：`Assets/Scripts/Core`、`Assets/Scripts/Presentation`（33.6k 行中的 runtime 部分）、兩個角色 prefab 的序列化值（唯讀 grep）、`ArchitectureRegressionTests`、ADR-010、`docs/38`／`45`／`46`。
> **方法**：每個概念從 producer 追到 ≥2 個 consumer 的實際 call site；數值取自 prefab／`.asset` 的實際序列化值，不取 C# 預設值。
> **沒做的事**：沒有 Play／probe 量測——下面所有「會分岔」的敘述都是**靜態推導**，標明為推導；沒有逐行讀 Actions／Combat／Camera／Traversal 內部（只追到它們跟本次概念交界的地方）。

---

## A. Executive Summary

**整體 fragmentation：Moderate（局部 High）。**

分佈非常不均勻：

- **Core 層大致健康**。黑板單一寫入者由 `A5_BlackboardMembers_HaveSingleDocumentedWriter` 強制；`IsGrounded`、`MoveSpeed`／`MoveDirection`、`MovementIntent` 都只有一個 canonical producer，下游大多直接消費。Traversal 只有一個 probe、一份 candidate。圖層遮罩的語意被 `StairAuthoringContractTests.MaskTable` 寫成表格測試——這是本 repo 對抗 fragmentation 最好的範例。
- **Presentation/IK 是重災區**。`FootIKController`（1,703 行）＋ `MovingPelvisSolver`／`StandingPostureSolver`／`LandingSurfaceResolver`／`FootContactSolver` 這一帶，過去一個月為了停步、樓梯、收步吸附逐案加入的機制，開始**在下游重新推導 Core 已經 commit 的狀態**，以及**同一個物理事實各自量一次**。

**最嚴重的 5 個概念：**

| # | 概念 | 一句話 |
|---|---|---|
| 1 | **Stop（停步）狀態** | `LocomotionModel` 已 commit 停步（含 Walk/Run tier），但只發布「預測快照」；骨盆求解器自己用「意圖放開的邊沿」＋`Gait` 重建一份停步狀態——正是 ADR-010 §3 否決過的方案 |
| 2 | **Walk／Run 檔位** | 四個定義：`GaitTier`（按鍵）、停步 tier（實際速度帶）、跳躍 tier（意圖門檻）、FootIK 的 `Gait == Walk` 代理。類比搖桿半推、AI 角色都會讓它們互相矛盾 | 🔄 **改判（2026-10-08，D2-f）**：各自分類是合理 specialization；真問題＝FootIK 拿 Gait 代理 committed stop tier、Jump 可能用 A loop 的時鐘查 B loop 的 Bake（見 F-02 改判框）
| 3 | **腳的 plant／contact** | 烘焙 plant（≤0.20 m/s）、T3 lock（β≥0.95 ≈ ≤1.3 m/s）、Reliable（≤0.3 m/s）、footstep（≤2.0 m/s）——其中**烘焙 plant 與 T3 lock 被 `LandingSurfaceResolver` 當成同一件事配對使用**，但定義不同 |
| 4 | **骨盆高度** | 4 個 bool 選出 4 條共存的骨盆管線；出貨設定只走其中一條，`StandingPostureSolver`（625 行）在 X Bot 上是死路徑；AI 角色（Y Bot）走的是另一條（舊）管線 |
| 5 | **腳下地面** | 同一隻腳同一帧有兩個「地面高度」：`FootSample.GroundY`（兩點規則）與 T3 support（三點最大穿透規則），統一骨盆求解器在同一個式子裡混用兩者 |

**是否已出現「每條 function path 重新定義 gameplay truth」？** 有，但**局限在 locomotion-stop × foot/pelvis 這一條路徑**，且大多是「Presentation 重新定義 *presentation 需要的* Core 事實」，還沒有擴散到 FSM／Action／Combat。Core 內部唯一一個真正的 copy 是跳躍初速（F-05）。

**現在需要處理嗎？** F-01／F-02 建議**現在**決定方向（它們會讓下一個停步／樓梯 slice 繼續往錯的基準上加規則）；F-04 是累積成本，可以排在下一次 pelvis 裁決時一起收；其餘屬 *worth fixing later*。

---

## B. Concept Inventory

| Concept | Canonical owner | Producers | Consumers | Alternate definitions | Risk |
|---|---|---|---|---|---|
| Ground support（角色） | `MotionDriver`（`CharacterController.isGrounded`） | `MotionDriver.cs:2716,2768` | JumpState、RollState、ActionState、TraversalState/Probe、LocomotionModel、FootIK 閘門、FootstepDetector | 無（docs/38 §13 明文禁止加否決層）| Low |
| Ground under each foot | `FootIKController` | `SampleGround`（兩點）、`ResolveContactPosition`（三點）、`TryPredictGroundHeight`（預測三點）、`ComputeReferenceOffset` | T3、MovingPelvisSolver、FootstepDetector（材質） | **兩種同帧定義**（F-06） | Medium |
| Horizontal intended velocity | `LocomotionModel`（`MoveSpeed`/`MoveDirection`） | `LocomotionModel.cs:165-166` | MotionDriver、TraversalProbe、CharacterFacingSource | 無 | Low |
| Realized root velocity | **無 owner** | FootstepDetector、FootIK×2 各自從 `transform.position` 差分 | footstep swing gate、MovingPelvisSolver、StandingPostureSolver | 3 份（F-07） | Low |
| Vertical velocity / airborne | `MotionDriver` | `VerticalVelocity`、`IsGrounded`、`JustLanded`/`JustLeftGround` | JumpState、Audio、Footstep | 3 個時間窗（F-08） | Low–Medium |
| Movement direction | intent：policy／AI；output：`LocomotionModel` | — | — | 無 | Low |
| Gait（Walk/Run） | **名義上** `PlayerLocomotionPolicy`（`Gait`） | Policy（按鍵）、`LocomotionStopSelector.SelectTier`（速度帶）、`JumpState.SelectSpeedTier`（意圖門檻） | Audio、FootIK、LocomotionModel(ADR-011)、Jump、Stop | **4 份**（F-02） | **High** |
| Stop / braking | `LocomotionModel`／`LocomotionStopRuntime` | `_stop`（private）→ `LocomotionStopPrediction.Active` | FootIK、MovingPelvisSolver、StandingPostureSolver | Presentation 端重建（F-01） | **High** |
| Foot phase（loop 哪隻腳在下） | `MotionBakeData.FootPhaseCurve` | 烘焙 | JumpState、LocomotionModel（stop 選 variant） | loop bake 參照設定兩份（F-10） | Low |
| Foot plant / lock | 無單一 owner | Bake schedule、T3 β/lock、Reliable、FootPlantTracker | LandingSurfaceResolver、骨盆、footstep | 4 份（F-03） | Medium |
| Predicted next plant | `LocomotionModel`（ADR-010 D5, **Trial**） | `PublishStopPrediction` | `FootIKController.TryPredictNextPlantGoal` → LandingSurfaceResolver | — | Medium（plumbing，F 節） |
| Landing surface（擺盪腳目的踏面） | `LandingSurfaceResolver`（Presentation） | 同左 | T3 lift／reference raise | 與 jump landing 同名不同義 | Low |
| Root displacement | `MotionDriver`（唯一 `Move` 呼叫者） | 多個 `Execute*Movement` | — | 停步位移積分兩份（F-09） | Low |
| Foot IK lock target | `FootContactState.LockPosition` | `FootContactSolver.StepLock` | T3、Reliable、LandingSurfaceResolver | R1c 後 `LockPosition`≠輸出位置（docs/45 已逐一盤點） | Low |
| Jump launch（v₀, g） | `JumpState`（`_stageLaunch`） | `JumpState.cs:135-158` | `MotionDriver.ApplyJumpLaunch` | **`TraversalSelectionPolicy` 另算一份**（F-05） | Medium |
| Traversal candidate | `TraversalProbe` | `TraversalProbe.Tick` | TraversalState（唯一）→ Classifier → SelectionPolicy → AnimationFit | 無 | Low |

### 主要概念的 Producer → Transform → Consumer

```text
Ground Support（健康）
CharacterController.isGrounded（MotionDriver:2716）
        ↓ 原值，不過濾
data.IsGrounded ─┬─ JumpState.CanEnter / 落地判定（:172,:295）
                 ├─ Roll / Action.RequiresGrounded / TraversalState / TraversalProbe
                 ├─ FootIKController 閘門（:211）
                 └─ FootstepDetector（空中抑制）
        ↓ groundEdgeEventGrace 0.1 s 確認
data.JustLanded / JustLeftGround ─┬─ AudioController（JumpTakeoff）
                                  └─ FootstepDetector（landing 事件、ForcePlanted）
另外：JumpState 自己的 _ungroundedElapsed（fall entry 0.35/0.1）
      MotionDriver 自己的 _stepDownTimeSinceGrounded（retry 0.08）
⇒ 事實只有一份；分叉的是「確認時間窗」，屬政策（F-08）。
```

```text
Stop / Braking（❗分叉）
LocomotionModel.Tick
  isIntending = desired ≥ 0.001（:114）
  IsReleaseRequest(_wasIntending, desired<0.001, grounded…)（Selector:184）
  SelectTier(smoother.Speed, walk 0.35–0.50, run 0.75–0.875)（:142）
        ↓ _stop（private：Tier, Variant, Pending, Playing…）
        ↓ 只發布
data.LocomotionStopPrediction{Active, 落點, NextPlant…}  ← 無 tier、無「停步開始」
        ↓
FootIKController
  movementRequested = DesiredSpeedNormalized > 0（:867, :1059）   ← 重新判斷「是否停步」
  walkGait = data.Gait == Walk（:868）                             ← 用按鍵檔位代理「Walk 停步」
        ↓
MovingPelvisSolver.Step
  releaseEdge = Prev.MovementRequested && !MovementRequested（:283）← 重建「停步開始」
  StopDescending = RootVerticalSpeed < −0.2（:296）
  StopUsesPrediction = WalkGait && StopDescending（:297）
  StopSettled = 兩腳 Locked && rootSpeed < 0.05（:299）
StandingPostureSolver.Step（X Bot 上已旁路）
  releaseEdge / canStart = !MovementRequested && rootSpeed ≤ 1.35（:287-288）← 第三份
```

```text
Gait（❗四個答案）
PlayerLocomotionPolicy ── data.Gait（Walk/Run/Sprint，按鍵）─┬─ AudioController（腳步音色）
                                                           ├─ FootIK walkGait
                                                           └─ LocomotionModel ADR-011 起跑對齊（:131）
AIMovementSource ──（不寫 Gait）⇒ Y Bot 恆為預設值 Run
LocomotionModel ── SelectTier(實際速度) ── stop tier（不發布）
JumpState ── SelectSpeedTier(意圖, 0.2 / 0.75) ── takeoff tier（:360）
```

```text
Foot plant（❗烘焙與 runtime 不同定義，但被配對使用）
Editor: FootPlantScheduleBaker  ankle 在 clip 最低點 +2.5 cm 內 ∧ 水平 ≤0.20 m/s ∧ 連續 2 樣本
        ↓ MotionBakeData.FootPlantSchedule
        ↓ LocomotionModel → NextPlant*
        ↓ FootIK.TryPredictNextPlantGoal
        ↓ LandingSurfaceResolver：以 T3 的 β 開驗證窗、以 T3 lock 結束（Phase≠Unlocked ⇒ Reset）
Runtime T3: stance(踝高 0.08→0.25) × (1 − smoothstep(1, 4 m/s)) ≥ 0.95 ⇒ Locked
Reliable:   Locked ∧ β≥0.95 ∧ 腳速 <0.3 m/s ∧ 即時命中
Footstep:   鞋底離 pose 平面 ≤4.5 cm ∧ 行程 ≥6 cm ∧ 下降上膛 ∧（root≥0.5 時）腳速 ≤2.0 m/s
```

```text
Predicted next plant（plumbing，見 F 節）
Bake schedule → LocomotionModel.PublishStopPrediction（root 空間 + 前進量）
 → blackboard → FootIK.TryPredictNextPlantGoal（IK goal 偏移 + 三點射線）
 → LandingSurfaceInputs → LandingSurfaceResolver.Step → LandingSurfaceIntent
 → BlendLift / ReferenceRaise（ResolveContactPosition 內）
```

```text
Traversal candidate（健康）
TraversalProbe.Tick(data.MoveDirection, MoveSpeed, IsGrounded)
 → TraversalCandidate → TraversalClassifier(kind) → TraversalSelectionPolicy(vs jump)
 → TraversalState.CanEnter（唯一讀者）→ TraversalAnimationFit → TraversalWarpPlan → MotionDriver
但：SelectionPolicy 的「跳得過嗎」自己重算跳躍初速（F-05）
```

---

## C. Fragmentation Findings

### F-01 停步狀態在 Presentation 被重建 — **High**

**Canonical concept**：「本角色此刻處於一次已 commit 的 locomotion stop；它是 Walk 或 Run 版本；從第 N 帧開始」。唯一決定者是 `LocomotionModel`／`LocomotionStopRuntime`（`_stop.IsActive`、`_stop.Tier`）。

**目前版本**
```text
LocomotionModel.cs:137-145   IsReleaseRequest(...) && SelectTier(...) ⇒ TryStartStop(tier)
LocomotionModel.cs:222-267   PublishStopPrediction：只寫 Active / 落點 / NextPlant；Active 還要等播放頭前進
```

**重複版本**
```text
FootIKController.cs:867      movementRequested = DesiredSpeedNormalized > 0f
FootIKController.cs:868      walkGait = data.Gait == GaitTier.Walk
MovingPelvisSolver.cs:282-300 stopping / releaseEdge / StopDescending / StopUsesPrediction / StopSettled
StandingPostureSolver.cs:285-298 canStart / releaseEdge / ReleaseRootSpeed（第三份，X Bot 已旁路）
```

**為什麼它們是同一概念**：骨盆求解器想回答的就是「停步開始了嗎、是 Walk 停步嗎」。ADR-010 §1 自己寫明「選哪一支停步 clip（Walk／Run）……全部由 LocomotionModel 決定。Presentation 不知道，也不該自己猜」；§3 否決「只在停步開始那一帧發布事件」的理由是「消費端得自己保存並判斷中斷／換代，等於把 Core 的停步狀態複製到 Presentation」。現在的 `MovingPelvisState.PreviousMovementRequested`／`StopDescending` 鎖存，正是那份複製。

**已可推導出的分岔**（靜態，未 Play 驗證）：
- 意圖落在 (0, 0.001)：Core 判「無意圖」，FootIK 判「有意圖」。
- 釋放時強度在 0.50–0.75 或 >0.875（例：Sprint 1.0、AI 全速 1.0）：Core 不啟動任何停步 clip，骨盆照樣進入 stop 語意。
- 類比搖桿半推（`respectAnalogMagnitude=1`，Run×0.5 ≈ 0.375 落在 Walk 帶）：Core 播 **Walk** 停步，`Gait=Run` ⇒ `walkGait=false` ⇒ §19 批准的 Walk 下樓 FutureReachCap 不套用。

**分類**：② Duplicate fact derivation（停步開始／tier）＋ ③ Representation fragmentation（`_stop.Tier` vs `Gait`）。
`StopDescending`（root 垂直速度）則是 ① Healthy——那是 Presentation 自己的地形觀測，不是 Core 的決定。

**風險**：行為漂移、branch-specific bug（只在搖桿／AI／sprint 出現）、修改需同步 3 處（Model、MovingPelvis、StandingPosture）。

---

### F-02 Walk／Run 檔位有四個定義 — **High**

> 🔄 **改判（2026-10-08，使用者裁決 D2-f；依據 `docs/48` §6）**：
> - **各 consumer 各自分類本身是合理的 specialization**——Stop、Jump、Footstep、ADR-011 問的是不同問題，邊界、更新時機、owner 都不同（`docs/48` §6.2–§6.3）；⛔ 不建立全域 `BodySpeedTier`。
> - **真正的問題只有兩個**：①FootIK 用 control `Gait` 代理 **committed stop tier**（F1／ADR-010 D6 處理）；②Jump 可能用 **A loop 的時鐘**去查 **B loop 的 Bake**（F2-J，clock-family ownership；量測 slice 進行中）。
> - 以下原文保留為審查當時的推導；「High」嚴重度與「四個定義」的框架已不適用。

**Canonical concept**：需要先承認這裡其實有**兩個**合法概念，問題是它們被混用：
- (a) **控制方案的選擇**（玩家按了 Walk 鍵）——`GaitTier`，合法擁有者 `PlayerLocomotionPolicy`。
- (b) **locomotion 實際在哪個速度檔**（決定播 Walk 還是 Run 的 clip 家族）——應屬 `LocomotionModel`。

**目前版本**
```text
PlayerLocomotionPolicy.cs:56   data.Gait = walk ? Walk : sprint ? Sprint : Run     （a）
LocomotionStopSelector.cs:39   SelectTier(smoother.Speed, 0.35–0.50, 0.75–0.875)    （b，用實際速度）
JumpState.cs:529               SelectSpeedTier(DesiredSpeedNormalized, 0.2, 0.75)  （b，用意圖）
FootIKController.cs:868        Gait == Walk 當作「Walk 停步」                       （用 a 回答 b）
LocomotionModel.cs:131         ADR-011 起跑對齊用 Gait == Run                        （用 a 回答 b）
```

**為什麼是同一概念**：Jump 的 takeoff tier、Stop 的 tier、FootIK 的 walkGait 問的都是 (b)「現在的 locomotion clip 家族是哪個」。只有 Audio 問的可能是 (a)（「聽起來像走」——可辯論）。

**具體矛盾**（數值取自 prefab／asset）：

| 情境 | `Gait` | Stop tier | Jump tier |
|---|---|---|---|
| 玩家 Walk 鍵（0.3651） | Walk | Walk | Walk |
| 玩家搖桿半推 Run（≈0.375） | **Run** | **Walk** | Walk |
| 玩家 Sprint（1.0） | Sprint | **None** | Run |
| AI 全速（Y Bot `desiredSpeedNormalized: 1`） | **Run（從未寫入）** | **None** | Run |
| AI recover（0.5） | **Run** | Walk | Walk |

`AIMovementSource` 不寫 `Gait`（`WriterRules` 只允許 `PlayerLocomotionPolicy.cs`），Y Bot 也沒有掛 policy ⇒ 敵人的腳步音色永遠是 Run 組。
> 🔄 **更正（2026-10-08，`docs/48` §0.1）**：AI 預設 Run 是 `docs/36` §4.1 的**刻意設計**（「沒有 policy 的角色也應該聽起來在跑」），不是遺漏。本 finding 成立的部分是：`docs/36` 的理由「強度與檔位必須同源」在 `respectAnalogMagnitude = 1`（搖桿半推）時已不成立，以及 Jump／Stop／FootIK 把控制方案檔位當成身體速度檔使用。

**分類**：③ Representation fragmentation（a/b 混用）＋ ② Duplicate fact derivation（Stop 與 Jump 各用不同輸入、不同門檻算 b）。

**風險**：行為漂移（Jump 從 Walk 起跳但停步走 None）、AI path 長期被忽略、修改門檻需同步 `LocomotionModel` 序列化欄位＋兩份 `JumpStateParams` 資產。

---

### F-03 「腳踩住了」有四個規則，其中兩個被當同一件事配對 — **Medium**

**Canonical concept**：「這隻腳在動畫裡何時、在哪裡成為支撐」。

**版本**

| 規則 | 高度判準 | 水平速度判準 | 參考點 | 位置 |
|---|---|---|---|---|
| 烘焙 plant | clip 最低點 +2.5 cm | ≤ 0.20 m/s | Foot 骨骼 world | `FootPlantScheduleBaker.cs:22-24` |
| T3 lock | 踝離 pose 平面 0.08→0.25 ramp | β = 1 − smoothstep(1, 4 m/s)，lock ≥0.95 | IK goal（踝） | `FootContactSolver.cs:112`、`FootIKSettings` |
| Reliable | （Locked） | < 0.3 m/s | IK goal | `FootIKController.cs:697-701` |
| Footstep | **鞋底**離 pose 平面 ≤ 4.5 cm、行程 ≥ 6 cm | ≤ 2.0 m/s（root≥0.5 時） | 鞋底 | `FootPlantTracker` |

**為什麼烘焙 plant 與 T3 lock 應是同一概念**：`LandingSurfaceResolver` 拿烘焙的 next plant 當「這次擺盪的目的地」，再用 T3 的 β／Phase 決定驗證窗與結束（`Phase != Unlocked ⇒ Reset`）。它隱含的假設是「烘焙認定的 plant ＝ T3 會鎖的那一下」。但兩者門檻差約 6 倍——`docs/45` §22 量到的「慢速移動的腳被 β 鎖住、動畫在鎖下累積 9–11 cm」就是這兩個定義不一致的可見後果；R1c（鎖定點跟隨）是在 T3 這一側補償它，不是消除它。
> 🔄 **更正（2026-10-08，`docs/48` §0.1／§3）**：①`docs/45` §22.4 量到**鎖定當下**動畫腳離停點 ≤ 2 cm——onset 沒有差到 6 倍那種程度；真正分歧的是**鎖的持續期間／釋放**與**支撐踏面選擇規則**（§14.4）。②烘焙 plant 不依賴 T3 門檻是 2026-10-03 的明確裁決（ADR-010 §9、`docs/45` §16.1），`LandingSurfaceResolver` 也已經用可信度＋即時驗證處理兩者不等。因此本條改定位為「刻意分離，但契約沒寫下來」；真正的 duplicate 是支撐踏面的三份規則（`docs/48` §3.3，與 F-06 同源）。

**為什麼 footstep 與 T3 lock 不是同一概念**：footstep 問「鞋底何時碰到地（聲音 onset）」，T3 問「何時可以把腳釘住」——不同問題，門檻各自由 `docs/35` 實測。見 H 節。

**分類**：烘焙 vs T3 ＝ ② Duplicate fact derivation（不同定義、被同一個 consumer 配對）；Reliable ＝ ① Healthy（明確是 T3 之上的 policy）；Footstep ＝ ① Healthy。
附帶：stance 用**踝**高、footstep 用**鞋底**高，`FootstepDetector.cs` 註解說兩者「同一個簡化」——參考點其實不同，差值被吸收進門檻常數。

**風險**：debug 困難（「吸附感」花了 §22–§24 三輪才定位到這裡）、testing explosion（每個 R1x 實驗都得同時重播兩種定義）。

---

### F-04 骨盆高度：四條共存管線、由 bool 選擇 — **Medium（累積成本 High）**

**Canonical concept**：`PelvisOffsetY` 的單一求解規則。`docs/46` 已自行盤點出「四種『該多高』定義」並建立統一模型 Intent → Reach Constraints → Solver。

**目前版本**（`FootIKController.cs:254-276`）
```text
contact        = UseContactPrototype && enabled
unified        = contact && UseUnifiedMovingPelvis
unifiedStanding= unified && UseUnifiedStandingPelvis
posture        = contact && UseStandingPosturePrototype && !unifiedStanding
⇒ 路徑：① 舊包絡 MoveTowards ② contact＋舊包絡 ③ Unified Moving＋StandingPosture ④ Unified Moving＋Unified Standing
```

**實際出貨值**：X Bot 四個都是 1 ⇒ 走 ④；`posture` 被 `!unifiedStanding` 壓成 false ⇒ **`StandingPostureSolver`（625 行）＋ `ResolveStandingPosture` 在 X Bot 上是死路徑**，但每次改 T3 語意（例：R1c 的 `LockPosition` 會動）仍要為它做相容性分析（`docs/45:1097`）。Y Bot（AI）序列化裡沒有這些欄位 ⇒ 全部 C# 預設 false ⇒ 走 ①。**玩家與敵人跑的是兩套骨盆／腳部架構**。

**重複的低階推導**（同一個 class 內兩份）：`ResolveUnifiedMoving`（`:799-812`）與 `ResolveStandingPosture`（`:1015-1043`）各自維護 root／hip 歷史、瞬移判定、root 水平速度、hip 一帧外插；兩個 solver 各自偵測 release edge、Primary Support 邊沿、Previous*Reliable。

**分類**：④ Special-case architecture（A/B 開關升格成永久結構）＋ Boolean explosion：4 個 bool 只有 4 個有效組合，實際上是一個未建模的 `PelvisMode`。

**風險**：修改需同步 N 處、ownership 不明（哪條是正式？）、AI path 的 bug 不會在玩家 Play 中出現。

---

### F-05 跳躍初速被 TraversalSelectionPolicy 複製，且邊界已漂移 — **Medium**

**Canonical concept**：「這個角色的地面起跳能達到多高」＝ `JumpLaunchData(v₀, g)`，擁有者 `JumpState`。

**目前版本**：`JumpState.cs:135-158`
```text
if bake && AutoApexHeight>0 && AutoCalculatedGravity>0:  g = AutoGravity×gMul; v = √(2gh)×vMul
else: g = 9.81×gMul; v = 7.5×vMul          ← fallback：照樣能跳
```
**重複版本**：`TraversalSelectionPolicy.cs:178-209`（`TryGetGroundJumpLaunch`）
```text
g = |AutoGravity|×gMul; h = AutoApexHeight×hMul; v = √(2gh)×vMul
無效 ⇒ return false                        ← 無 fallback；用 |g| 而 JumpState 要求 g>0
```

**為什麼是同一概念**：policy 要回答「跳得過嗎」，用的必須是 JumpState 實際會用的 v₀、g。這是逐字 copy，但兩個邊界已經不同：bake 重力 ≤0 或缺 bake 時，JumpState 用 fallback 起跳，policy 卻判「跳不了」⇒ 偏好 traversal。只看 Stage[0]、`TraversalSelectionPolicyTests` 沒有與 JumpState 對拍的測試。

**分類**：② Duplicate fact derivation。**風險**：行為漂移（目前只在資料缺損時觸發，頻率低）。

---

### F-06 同一隻腳、同一帧兩個「地面高度」 — **Medium**

**Canonical concept**：「這隻腳此刻下方的支撐面高度」。

**版本**
```text
SampleGround（:382-460）       踝射線 + heel/toe（UseTwoPointSampling）→ ComputeSoleHeight → FootSample.GroundY
ResolveContactPosition（:575-620） movement-surface 參考 + heel/ankle/toe 三點 AccumulateContact → supportY / contactDelta
```
**混用點**：`ResolveUnifiedMoving`（`:829-846`）的 `MovingPelvisFoot`：
`Target` ＝ T3 解出的腳目標（三點規則），`SoleHeight = Target.y − bottom − left.GroundY`（兩點規則），`HasFloor = left.HasHit`（踝單點）。
另外 `FootIKGroundData`（footstep 材質來源）發布的是 `SampleGround` 的 collider，不是 T3 實際支撐的那一個。

**為什麼是同一概念**：骨盆求解器要的是「腳底離它真正踩的面多高」。T3 contact 開啟時，`SampleGround` 的**位置**結果已被 ③' 覆寫（只保留旋轉），它的 `GroundY` 卻仍流進骨盆。平地兩者相同；階緣上可以差一整階（`docs/46` §20 記錄過「地面取樣先翻」）。

**分類**：③ Representation fragmentation（推導：同事實兩種採樣規則被同一式子混用）。**風險**：階緣處的骨盆／腳不一致，難以從單一 debug 線看出。

---

### F-07 實際 root 速度沒有 owner，三處各自差分 — **Low**

`FootstepDetector.cs:137-147`、`FootIKController.cs:799`（moving 歷史）、`:1034`（posture 歷史）都從 `transform.position` 差分。後兩者在**同一個 class**、用同一個瞬移門檻、存兩份歷史。公式相同，但瞬移處理不同（footstep 沒有）。
**分類**：③。**風險**：低；若哪天改成以 `MotionDriver` 的實際位移為準，要改三處。

---

### F-08 空中／落地確認：一個事實、三個時間窗、兩種「落地」 — **Low–Medium**

- 事實：`IsGrounded` 原值（docs/38 §13 裁決不得加否決層）——**健康**。
- 時間窗：`JumpState._ungroundedElapsed`（fall entry 0.35 玩家／0.1 敵人）、`MotionDriver._groundDivergingElapsed`（edge event 0.1）、`MotionDriver._stepDownTimeSinceGrounded`（retry 0.08）。2026-10-07 已裁決不抽共用 timing primitive，**本報告不重議**。
- 唯一值得記下的語意分岔：**「落地」有兩個定義**——FSM 用 `airborne ≥ 0.15 s ∧ IsGrounded`（原值，`JumpState.cs:295`），聲音／footstep 用 `JustLanded`（0.1 s 確認，`MotionDriver.cs:2755`）。所以落地動畫與落地聲音之間存在最多約 0.1 s 的結構性延遲；`JustLeftGround` 同時代表「主動起跳」與「非主動失地」（`AudioController.cs:127` 一律播 `JumpTakeoff`）。`MotionDriver.cs:2746` 已將起跳延遲列為已知取捨。
- **分類**：① Healthy specialization（大部分）＋ ③（landing／takeoff 事件語意過載）。

---

### F-09 停步位移積分兩份 — **Low**

預測端 `MotionBakeData.GetLocomotionStopHorizontalDisplacementAt`（逐 key 梯形，`:987`）vs 執行端 `MotionDriver.ExecuteBakedCurveMovementAtTimes`（播放頭區間的單段梯形，`:2525-2535`）。兩者都特判「第 0 個 placeholder key」，但各寫一次。另有 `GetHorizontalDisplacementAt`（`:939`）只差 placeholder 處理。
**分類**：② Duplicate（同一條曲線三種積分）。根因是 placeholder key 的知識散在消費端，而不是由烘焙器或曲線本身封裝。**風險**：預測與實際位移的系統性小偏差（cm 級，推導未量）。

---

### F-10 Loop 烘焙參照設定兩份、`IFootPhaseSource` 要呼叫端提供 — **Low**

`LocomotionModel.walkLoopBakeData/runLoopBakeData` 與 `JumpStateParams.WalkLoopBakeData/RunLoopBakeData`（玩家、敵人各一份 asset）。`IFootPhaseSource.TryGetCurrentFootPhase(loopBakeData, …)` 要求 JumpState 告訴 model「你現在播的是哪個 loop」——那正是 model 自己知道的事。
**分類**：③。**風險**：換 loop clip 時漏改一處 ⇒ Jump 的 LU／RU 讀錯腳相（靜默錯誤）。

---

## D. Special-case Object Inventory

| Item | Created for | Consumers | Independent domain meaning? | 建議 |
|---|---|---|---|---|
| `LocomotionStopPrediction`（黑板） | Walk 下樓停步的骨盆落點（docs/46 §15–19） | FootIK only | **有**：「已 commit 停步的未來」 | Keep（ADR-010 Trial）；但缺 tier → 見 G-1 |
| `LocomotionStopPrediction.NextPlant*` | T3 擺盪腳目的踏面（docs/45 §17） | LandingSurfaceResolver | 部分：plant 定義與 T3 不同（F-03） | Investigate |
| `LandingSurfaceResolver` ＋ Intent/State/Inputs/Tuning | 停步時擺盪腳「跨到哪一階」 | FootIK | **有**（目的地踏面），但只在停步預測存在時有輸入 | Keep；改名避免與 jump landing 混淆 |
| `StandingPostureSolver`（625 行）＋ `ResolveStandingPosture` | 停步站直原型 | X Bot 上無（被 `UseUnifiedStandingPelvis` 旁路） | 已被 unified standing 取代 | **Merge candidate（＝移除候選，待裁決）** |
| `UseStandingPosturePrototype`／`UseUnifiedMovingPelvis`／`UseUnifiedStandingPelvis`／`UseContactPrototype` | A/B 開關 | FootIK | 是一個未建模的 mode | Merge candidate（一個 enum 或收斂後移除） |
| `PlantAwareExtrapolation` | docs/45 §21 A2 實驗 | FootIK | ~~無（X Bot 關閉）~~ 🔄 `58480b3` 起 X Bot 開啟（P8b） | Keep；名稱用了 `Plant` 卻屬 T3 lock 用語（`docs/48` K6） |
| `LockedFollowExperiment`／`LockedFollowDeadZone` | R1c（收步吸附） | FootContactSolver | 有（鎖定容許跟隨） | Keep；d0=3 cm 已裁決，未來不應再是 optional |
| `LockedRiserIntrusionRule` ＋ 額外踝射線 | 樓梯 riser 翻階（docs/46 §22–25） | T3 | 有（幾何規則），但只在樓梯有作用 | Keep |
| `MovingPelvisContext.WalkGait` | §19「只批准 Walk 下樓停步」 | MovingPelvisSolver | **無**：是 stop tier 的代理 | Merge candidate（→ stop tier） |
| `MovingPelvisState.StopDescending/StopUsesPrediction` | 同上 | 同上 | 部分（descending 是地形觀測） | Investigate |
| `MovingPelvisTuning.Default.DescendingStopSpeed`（硬編 0.2） | 下樓判定 | MovingPelvisSolver ×2 | 有 | Investigate（沒進 settings，是唯一不可調門檻） |
| `TraversalSelectionPolicy.TryGetGroundJumpLaunch` | 「跳得過就不 traversal」 | SelectionPolicy | 無：JumpLaunch 的 copy | Merge candidate |
| `JumpStateParams.Walk/RunLoopBakeData` | Jump 起跳選 LU／RU | JumpState | 無：model 已持有 | Merge candidate |
| `GetLocomotionStopHorizontalDisplacementAt` | placeholder key 半速 bug（docs/07 R8） | LocomotionModel | 無：同一條曲線的第二種積分 | Merge candidate |
| `MotionDriver.groundEdgeEventGrace` | 樓梯 isGrounded 閃爍造成落地聲 | JustLanded/LeftGround | 有 | Keep（已裁決） |
| `MotionDriver._stepDownTimeSinceGrounded`（標 🔬 PROTOTYPE） | step-down retry 窗 | MotionDriver | 有 | Keep；PROTOTYPE 標記是否該摘掉 → Investigate |
| `FootstepDetector._previousRootPosition` | 貼牆跑無聲（swing gate） | FootPlantTracker | 有，但與 FootIK 重複（F-07） | Keep |
| `FootIKGroundData` | footstep 材質 | FootstepDetector | 有（觀測發布，不是結論） | Keep（但來源是兩點規則，F-06） |

---

## E. Repeated Decision Rules

| 規則 | 版本數 | 位置與值 |
|---|---|---|
| 「有移動意圖」 | **5** | Model `≥0.001`（`:114`）；FootIK `>0`（`:867`,`:1059`）；Jump continue `≥0.2`；Action cancel `≥CancelMoveIntentThreshold`；`IsProducingMotion` 用 smoother `≥0.1` |
| 「停步開始」（意圖下降緣） | **3** | `LocomotionStopSelector.IsReleaseRequest`；`MovingPelvisSolver:283`；`StandingPostureSolver:288` |
| Walk／Run 分界 | **4** | Gait 按鍵；Stop 0.35–0.50／0.75–0.875（實際速度）；Jump 0.2／0.75（意圖）；FootIK `Gait==Walk` | 🔄 2026-10-08（D2-f）：各自 policy，不再計為重複；FootIK 那一份屬 F1
| 腳水平速度門檻（plant） | **5** | Bake 0.20；β smoothstep 1–4（lock≈1.3）；Reliable 0.3；Footstep 2.0；Extrapolation 2–6 |
| Root 水平速度門檻 | **4** | Footstep swing gate 0.5；Unified settle 0.05；Posture final support 1.35；Posture early release 0.1 |
| Root 垂直「下樓中」 | **2**（同常數） | `MovingPelvisSolver:296`、`:396`，皆 `DescendingStopSpeed=0.2`（硬編） |
| 腳離地面高度 | **3** | stance 踝 0.08–0.25；footstep 鞋底 4.5 cm；bake 踝 +2.5 cm（相對 clip 最低點） |
| 失地確認時間 | **3** | fall entry 0.35/0.1；edge event 0.1；step-down retry 0.08（已裁決保留） |
| 腳下三點取最高 | **2–3** | `ResolveContactPosition`（AccumulateContact×3）；`TryPredictGroundedAnkle`（:932）；`FootLengthReference`（movement surface 版） |
| 瞬移判定 | **2**（同常數） | `ResolveUnifiedMoving:806`、`ResolveStandingPosture:1015`，皆 `MaxPoseStepPerFrame` |
| 跳躍 v₀ = √(2gh)·k | **2** | JumpState；TraversalSelectionPolicy |
| SpeedCurve 積分 | **3** | `GetHorizontalDisplacementAt`；`GetLocomotionStopHorizontalDisplacementAt`；MotionDriver 單段梯形 |

---

## F. Data Plumbing

### F-P1：next plant（7 跳）
```text
FootPlantSchedule(bake) → LocomotionModel.PublishStopPrediction → PlayerRuntimeData
 → FootIKController.TryPredictNextPlantGoal → LandingSurfaceInputs
 → LandingSurfaceResolver.Step → LandingSurfaceIntent → BlendLift / ReferenceRaise
```
**判斷**：Core→Presentation 這一跳是**合理邊界**（ADR-010 的核心論點：Presentation 不得持有 Bake 參照、不得自己猜 clip）。Presentation 內的 Inputs／State／Intent／Tuning 是為了純函數 EditMode 測試，也合理。真正的問題不是跳數，而是**這條管道送的 plant 定義與終點（T3 lock）不同**（F-03）。⇒ **保持 pipeline**。

### F-P2：stop tier（0 跳——因為沒送）
```text
LocomotionModel._stop.Tier ──✗（不發布）
FootIK ← data.Gait（另一個概念）
```
**判斷**：這不是 plumbing 過多，是 **canonical source 存在但沒送到 consumer**，consumer 只好拿最近的替代品。⇒ **提升成 canonical context**（在 ADR-010 Trial 範圍內修訂 schema，見 G-1）。⚠️ `A12_StopAndFootPhase_OnlyExposeTrialPredictionSnapshot` 會擋——這是預期的，修訂時同一工作包更新。

### F-P3：settings → Tuning struct（每帧複製）
`MovingPelvisTuning`（15 欄）、`PostureTuning`（14 欄）、`LandingSurfaceTuning` 每帧從 `FootIKSettings` 搬一次。
**判斷**：合理的 architecture boundary（solver 不認識 MonoBehaviour／ScriptableObject），零 GC（struct）。唯一瑕疵是 `DescendingStopSpeed` 沒從 settings 來（用 `Default`），造成「看起來可調但其實不能」。⇒ **保持**。

### F-P4：loop bake 參照
`JumpStateParams` → JumpState → `IFootPhaseSource.TryGetCurrentFootPhase(loopBake)` → LocomotionModel。資訊從 asset 繞一圈送回本來就持有它的 model。⇒ **consumer 改直接問 owner**（介面去掉參數，見 F-10）。

---

## G. Top Refactor Candidates（最多 5 個，皆未實作）

### G-1 發布 committed stop 事實，撤掉 Presentation 端重建
```text
Current   FootIK 用 DesiredSpeed>0 判停步、Gait==Walk 判 Walk 停步；MovingPelvisSolver 自己鎖存 release edge
→ Problem  與 Core 的停步決定在搖桿半推／Sprint／AI／極小意圖時分岔（F-01, F-02）
→ Canonical concept  「一次已 commit 的停步：tier、開始帧、是否仍有效」
→ Desired ownership  LocomotionModel 單一寫入（現有 WriterRule 不變），經 LocomotionStopPrediction 發布
→ What should disappear  MovingPelvisContext.WalkGait；MovingPelvisSolver 的 PreviousMovementRequested／releaseEdge 重建；FootIK 的 `DesiredSpeedNormalized > 0` 作為停步判準
→ What should remain  StopDescending（Presentation 自己的地形觀測）、StopSettled（Presentation 自己的落定 policy）
```
需要使用者裁決：ADR-010 D2「最小 schema」是否放寬到含 tier／started（Trial，可修訂不需新 ADR）；`A12` 同包更新。另有一個語意選擇：停步 tier 為 None（Sprint 放開）時骨盆該不該進 stop 語意——現在是「進」，改成以 Core 為準就會變成「不進」，這是行為變更。

### G-2 拆開「控制方案檔位」與「locomotion 檔位」
```text
Current   GaitTier 同時被當作 (a) 按鍵選擇 與 (b) clip 家族；Stop、Jump 各自用不同輸入算 (b)
→ Problem  四個答案；AI 永遠 Run（F-02）
→ Canonical concept  (b) LocomotionTier（Walk/Run/None），由 LocomotionModel 依同一組門檻決定
→ Desired ownership  (a) PlayerLocomotionPolicy（不變）；(b) LocomotionModel
→ What should disappear  JumpState.SelectSpeedTier 的獨立門檻（改讀 model 的 tier）；FootIK／ADR-011 對 Gait 的使用
→ What should remain  GaitTier 作為輸入選擇；Audio 是否跟 (a) 還是 (b) 需裁決（AI 沒有 (a)）
```
⚠️ 這會碰黑板 schema（新增欄位或擴充既有快照）⇒ 依 CLAUDE.md 判準①需要 ADR／Trial 討論，**不得直接實作**。

### G-3 收斂骨盆管線
```text
Current   4 個 bool 選 4 條路徑；X Bot 走 ④、Y Bot 走 ①、StandingPostureSolver 是死路徑
→ Problem  每次改 T3 都要為死路徑做相容分析；玩家與敵人不同架構（F-04）
→ Canonical concept  Unified pelvis solver（docs/46 統一模型）
→ Desired ownership  FootIKController → MovingPelvisSolver（唯一）
→ What should disappear  StandingPostureSolver、ResolveStandingPosture、舊包絡路徑（或明確降級為「無 T3 時的 fallback」一條）、四個 bool
→ What should remain  一條 fallback（IK 不允許／腿長無效時），以及 Y Bot 要不要升級的明確決定
```
前置條件：先確認 unified standing 的 Play 驗收狀態（`docs/46` §14）。

### G-4 單一 JumpLaunch 推導
```text
Current   JumpState 與 TraversalSelectionPolicy 各算一次 v₀、g，fallback 不同（F-05）
→ Canonical concept  JumpLaunchData for stage i
→ Desired ownership  JumpStateParams（或其上的純函數）——JumpState 與 Policy 都呼叫它
→ What should disappear  TryGetGroundJumpLaunch 的公式本體
→ What should remain  Policy 的「reach vs height」比較
```
Core 內、無黑板影響、純函數可測 ⇒ 屬局部改動，風險最低。

### G-5 每隻腳一份地面觀測
```text
Current   SampleGround（兩點）與 T3（三點）各自取樣；骨盆混用（F-06）
→ Canonical concept  「這隻腳本帧的支撐面」
→ Desired ownership  T3 contact 開啟時以 T3 support 為準，FootSample 只保留旋轉／fallback 用途
→ What should disappear  MovingPelvisFoot.SoleHeight 對 FootSample.GroundY 的依賴；FootIKGroundData 改發布 T3 實際支撐的 collider（材質同源）
→ What should remain  SampleGround 作為 T3 關閉時的路徑（若 G-3 保留 fallback）
```
⚠️ 會改骨盆數值 ⇒ 需要樓梯 probe 前後對照（`unity-live-control`），不能只靠推導。

**刻意不列入**：F-03（烘焙 plant vs T3 lock）。它是設計問題而非重構——要先決定「預測的是動畫的 plant，還是 T3 的 lock」，而 R1c 這條線才剛裁決。建議在下一次 T3 slice 的開場把它寫成一句明確的契約，再決定要不要動。
> 🔄 2026-10-08：契約草案見 `docs/48` §3.2（K1：預測的是動畫的 plant，⛔ 不得假設它就是 T3 lock）。

---

## H. Things That Look Fragmented But Should NOT Be Merged

| 看起來重複 | 為什麼保持分離 |
|---|---|
| `IsGrounded`（膠囊）vs FootIK 每腳地面 | `docs/38` §⑥／§13：Foot IK 不能當 grounding authority（循環依賴＋隨動畫抖動）。一個是物理碰撞事實，一個是表現層觀測 |
| movement surface（代理坡）vs tread（看得到的踏面）兩套圖層 | 有意的雙幾何（`docs/44`）；語意被 `StairAuthoringContractTests.MaskTable` 逐元件寫死——這是**正確的** fragmentation 管理方式 |
| Footstep plant 事件 vs T3 lock | 不同問題（聲音 onset vs 釘腳）；門檻各自由 `docs/35` 實測；強行共用會讓其中一個失真 |
| 三個失地時間窗 | 不同語意（FSM 進 Falling／事件確認／step-down 重試）；2026-10-07 使用者已裁決暫不抽共用 |
| `MovementIntent.Desired*` vs `MoveSpeed`/`MoveDirection` | ADR-003 的刻意分層（意圖 vs model output）；A5 強制單寫入 |
| `FootPhase`／`FootLockPhase`／`FootPlantPhase`／`LandingPhase`／`PosturePhase` | 名字撞，概念不同（loop 腳相／IK 鎖／聲音偵測／跳躍落地／站直相位）。可考慮改名，**不要合併** |
| `LandingSurface*` vs Jump landing | 同上：擺盪腳目的踏面 ≠ 身體落地。改名即可 |
| `LocomotionStopRuntime` 的 `IsPending`／`IsPlaying`／`IsAwaitingPlayhead`／`HoldsEntryMotion` | 看似 bool 爆炸，實際只有 2 個儲存 bool，其餘是衍生屬性——已經是封裝好的狀態機，不需 enum |
| 各 solver 的 `*Inputs`／`*State`／`*Tuning` struct | 純函數測試邊界、零 GC；不是 DTO inflation |
| Jump continue 0.2、Action cancel threshold | 是 policy（「多大的意圖足以打斷／續走」），不是「有沒有意圖」這個事實的 copy |
| TraversalProbe → Classifier → SelectionPolicy → AnimationFit | 單一 candidate 的分階段處理，每階有獨立測試；不是重複推導 |
| `ArbiterPipeline`／`PresentationPipeline` 的合併寫入 | 多來源 OR 合併、單一寫入者，正是避免 fragmentation 的結構 |
| `MotionDriver` 的多個 `Execute*Movement` 入口 | 對應不同位移來源（曲線、warp、垂直），共用唯一 `Move` 呼叫點 |

---

## I. Architecture Health Verdict

> **IntentPipeline 的 Core 仍然是「合理地把 domain 拆開」；Presentation/IK 的 locomotion-stop × foot × pelvis 這一段，已經開始「因為局部 bug／feature 而複製 domain concepts」——而且複製的方向是 Core → Presentation 的反向重建。**

**支持「Core 健康」的證據**
1. `A5` 把 14 個黑板成員各綁一個寫入檔；`IsGrounded`、`MoveSpeed`、`MoveDirection`、`Gait`、`LocomotionStopPrediction` 都只有一個 producer。
2. `IsGrounded` 被明文禁止加否決層（docs/38 §13），所有 consumer 讀同一個原值，分歧只在各自的時間窗 policy。
3. Traversal 只有一個 probe、一份 candidate、單一讀者。
4. 圖層遮罩的「誰該看見什麼」被寫成表格測試，而不是散在各元件。

**支持「Presentation 局部碎裂」的證據**
1. ADR-010 自己否決的方案（Presentation 自己判斷停步開始與 clip 家族）在 `MovingPelvisSolver:282-300`／`FootIKController:867-868` 以另一種形式存在（F-01）。
2. ~~Walk／Run 有 4 個定義……互相矛盾~~ 🔄 改判（D2-f）：各自分類合理；剩下 FootIK 的 Gait 代理與 Jump 的 clock-family 錯配（F-02 改判框）。
3. 4 個 A/B bool 留成永久結構；出貨設定下 625 行 solver 是死路徑；玩家與敵人跑不同腳部／骨盆架構（F-04）。
4. 同一隻腳同一帧兩種地面高度被同一個式子混用（F-06）；同一個 class 裡兩份 root 速度歷史（F-07）。
5. ~~烘焙 plant 與 T3 lock 定義差約 6 倍，卻被 `LandingSurfaceResolver` 當作同一事件配對~~——🔄 更正（`docs/48` §0.1）：兩者分離是已裁決的設計，resolver 也已用即時驗證處理；剩下的證據是「腳踩哪一階」有三份規則（F-03 更正、F-06）。
6. Core 唯一一處 copy（跳躍初速，F-05）邊界已漂移，說明「copy 一次」在本 repo 不是穩定狀態。

**趨勢判斷**：碎裂幾乎全部出生在「為了讓某個 Play 觀察到的瑕疵消失而加一個開關／一個局部判準」的 slice 裡，且每個 slice 都附了 A/B 與測試——個別決定都站得住，問題在於**實驗結束後沒有回收**（開關沒收、被取代的 solver 沒刪、代理判準沒換成 canonical 來源）。建議把「Trial／A/B 結束 ⇒ 收開關、刪輸家、把代理換成 owner」列入 slice 收尾條件，而不是另開重構專案。

---

## 附錄：本次讀過的關鍵 call site

`MotionDriver.cs:171,203,612,650,2516-2535,2683-2803` ｜ `JumpState.cs:42,135-193,295,352-372,442-460,468-491,529-540` ｜ `LocomotionModel.cs:60-80,100-184,222-267,300-312` ｜ `LocomotionStopSelector.cs:39-50,170-197` ｜ `PlayerLocomotionPolicy.cs:32-71` ｜ `GaitProfileSO.cs:75-86` ｜ `TraversalSelectionPolicy.cs:150-210` ｜ `FootIKController.cs:185-367,382-466,560-620,690-701,794-881,893-1001,1005-1095,1112` ｜ `FootContactSolver.cs:104-118,217-256` ｜ `MovingPelvisSolver.cs:276-312,388-400` ｜ `StandingPostureSolver.cs:285-298` ｜ `LandingSurfaceResolver.cs`（全） ｜ `FootstepDetector.cs:133-260` ｜ `FootPlantTracker.cs`（全） ｜ `FootPlantScheduleBaker.cs`（全） ｜ `MotionBakeData.cs:935-1043` ｜ `ArchitectureRegressionTests.cs:278-330,500-560,701-730` ｜ `StairAuthoringContractTests.cs:198-216` ｜ `X Bot.prefab`／`Y Bot.prefab`／`Gait_ActionRPG.asset`／`*JumpStateParams.asset`（唯讀 grep）
