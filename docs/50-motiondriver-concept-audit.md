# 50 — MotionDriver Concept Fragmentation / Semantic Duplication Audit（2026-10-10）

> **性質**：研究報告，**只讀不改**。runtime、資產、測試皆未動；未使用 Unity Editor（Editor 讓給 Traversal session）。
> **範圍**：`Assets/Scripts/Presentation/Motion/MotionDriver.cs`（2,812 行，唯一的 `CharacterController.Move` 呼叫者）、`CapsulePoseSolver.cs`、`CapsuleOffsetPolicy.cs`、`CapsulePoseProfileSO.cs`；`MotionBakeData` 只追與位移積分相關的查詢。治理文件：ADR-001、`docs/07` §3.4、`docs/38` §13、`docs/42` §8、`docs/44` §9.4、`docs/02` §4.4.4、`ArchitectureRegressionTests`（A5／A28／A32／A48）、`StairAuthoringContractTests`、`LocomotionMixerWiringTests`。
> **方法**：比照 `docs/47`——每個概念從 producer 追到 ≥2 個 consumer 的實際 call site；數值取自 `X Bot.prefab`／`Y Bot.prefab`／`.asset` 的序列化值（唯讀 grep），不取 C# 預設值。**先對照既有裁決再下結論**（`docs/48` §0 的做法）：凡是已被裁決的分離，列在 §0 與 §H，不當成缺陷。
> **邊界**：Traversal 的「選擇 → 計畫」屬 Traversal 審查（另一份文件）；本文件只管執行端（`ExecuteTraversalWarpedMovement`／`ExecuteCommittedCurveMovement`／collision profile）。Jump 落地／失地時間窗已在 `docs/47` F-08 審過且有裁決，這裡只引用。
> **沒做的事**：沒有 Play／probe 量測。下文「會分岔」的敘述都是**靜態推導**，有標明；唯一的數值計算是 §C M-03 的**離線算術**（Python 讀 bake YAML 曲線，非引擎量測），已標明其假設。

---

## 0. 本審查遵守的既有裁決（不重議）

| 來源 | 內容 | 對本審查的限制 |
|---|---|---|
| `docs/38` §13（2026-09-17） | `IsGrounded` ＝ `CharacterController.isGrounded`，⛔ 不加 ledge／perch 否決層 | 不把 step-down query 結果、capsule 狀態提議成 grounding 來源 |
| 2026-10-07（`docs/47` F-08） | 三個失地時間窗（fall entry／edge event／step-down retry）**暫不抽共用 timing primitive** | M-05 只談 retry 計時器的**取樣域**（哪些 path 會推進它），不提議合併時間窗 |
| `docs/42` §8.1 R1–R10 ＋ `docs/44` §9.4（2026-09-24） | step-down assist 的十條機制規則；只掛 locomotion（Base ＋ Stop），Roll／Action 不貼地；Query 遮罩只看 `CharacterMovementSurface` | 不重議「Roll／Action 不貼地」與遮罩語意 |
| `docs/07` §3.4 ＋ CLAUDE.md B11 | 世界速度 ＝ 強度 × Sprint bake 代表速度；mixer `_Speeds` 與 Stop playback 由人工依公式推導（⛔ 不自動寫 Animancer 序列化） | M-07 不把「強度不等於 speed_i/speed_max」當缺陷 |
| ADR-007（**Trial**）D2／D3 | MotionDriver 是 rotation **執行者**，朝向由 `CharacterFacingSource` 決定 | 不提議 MotionDriver 自己判斷要不要轉 |
| ADR-002 | 跳躍 v₀／g 由 JumpState 依 bake 推導後注入；MotionDriver 只積分 | 只看注入後的執行，不重審 `docs/47` F-05 |
| `docs/27`／`docs/28`（2026-09-15，使用者裁決） | Capsule V2 管線順序 Pose→Cap→Smooth→Clamp；`center.y` 不動；`drawCapsuleOffsetDebug` 預設開 | 不重議 V2 管線本身 |

---

## A. Executive Summary

**整體 fragmentation：Low–Moderate。**

MotionDriver 的**事實層**是健康的：position／rotation 只經它寫（A48、A20、`Teleport` 是唯一瞬移入口）；`IsGrounded`／`VerticalVelocity`／`JustLanded`／`JustLeftGround` 單一寫入者由 A5 `WriterRules` 強制；capsule shape 只有它寫（A48）；它**沒有**在下游重新推導 Core 已 commit 的狀態——停步是 `LocomotionModel` 選好入口才呼叫、facing 是 `CharacterFacingSource` 送 request、死亡讀 `Survivability.IsDead`。`docs/47` 在 Presentation/IK 看到的「反向重建 Core 狀態」**在這裡不存在**。

碎裂出在另外兩處：

1. **每個 `Execute*` 入口各自決定「這一帧的運動模式」**。7 個入口 × 7 個子政策（朝向、膠囊偏移、水平來源、垂直來源、grounded 同步時機、貼地、`VerticalVelocity` 發布時機）沒有任何一處寫成表；它們是「呼叫哪個方法」的副作用。大多數差異是刻意的，但有幾格是**意外**，集中在 traversal 的 fallback 路徑（M-01、M-02）。
2. **實驗結束後沒有回收**——跟 `docs/47` §I 的趨勢判斷完全同型：step-down assist 已在 2026-09-24 正式接線、兩個 prefab 都開、有契約測試，但程式裡仍有 6 個 `🔬 PROTOTYPE` 標記、`docs/42` §8.3 要求「正式接線時必須移除」的 FootIK 除錯參照還在；`ApplyBakedCompensation` 零呼叫者卻仍被 dev-spec 與 roadmap 當成 Motion Warping 的 seam（M-05、M-08、M-09）。

**最嚴重的 5 項：**

| # | 項目 | 一句話 |
|---|---|---|
| M-01 | **運動模式沒有表示法** | 7 個入口各自重新決定 7 個子政策；`VerticalVelocity` 有兩種發布時機、grounded 同步從 3 個地方進入 |
| M-02 | **「正在 committed traversal」有三個表示、選錯了一個** | 落地／離地事件的抑制鍵在 `HasActiveTraversalPlan`（＝plan 有效），fallback 路徑（plan 無效）不抑制、會被 facing 轉向、膠囊偏移凍結在進場前；守這條契約的 `C0` PlayMode 測試自 2026-09-23 起一直紅 |
| M-05 | **Step-down assist 已畢業、標記沒摘** | 6 處 PROTOTYPE 標記與「正式接線前刪除」的註解跟 `docs/44` §9.4 的接線事實矛盾；retry 計時器只在會呼叫 assist 的 path 上推進，跨 Roll／Action／Traversal 會帶著過期值 |
| M-03 | **SpeedCurve「這帧走多少」有 4 種積分、2 種「clip 時間過了多少」** | 離線算術顯示數值差 ≤ 0.5 cm（無害）；問題是 placeholder key 的知識散在 4 處、用 2 種偵測法，且 3 參數版本隱含「playback rate = 1」 |
| M-09 | **`ApplyBakedCompensation` 是零呼叫者的第三種對齊模型** | 實際的 warp 已由 `TraversalWarpPlan` 實作；這個方法跳過 facing／膠囊、不走 playhead，仍被文件當 seam 引用 |

**是否需要現在處理？** 沒有 High。M-02 是唯一會產生**可聽見／可看見**行為差異的項目（推導，待量測），建議與 Traversal 審查一起看；M-05／M-08 是一次性的回收清理，需要使用者先確認「step-down assist 已畢業」；其餘屬 *worth fixing later*。

---

## B. Concept Inventory

| Concept | Canonical owner | Producers | Consumers | Alternate definitions | Risk |
|---|---|---|---|---|---|
| Root position／rotation | `MotionDriver`（唯一 `Move`／`Rotate`／`Teleport`） | 7 個 `Execute*`／`ApplyBakedCompensation`／`Teleport` | 全專案（讀 `transform`） | 無（A20／A48） | Low |
| Ground support（膠囊） | `MotionDriver.SyncGroundedState`（`:2701`） | `characterController.isGrounded` 原值 | JumpState、FootIK、Footstep、step-down gate ① | 無（`docs/38` §13） | Low |
| Ground edge events | 同上（`groundEdgeEventGrace` 0.1 s 確認） | `:2738-2765` | Audio、Footstep | traversal 抑制鍵選錯表示（M-02） | Low–Medium |
| Vertical velocity | `MotionDriver._verticalVelocity` | `GetGravityThisFrame`、`ExecuteCommittedCurveMovement:1417`、`ExecuteTraversalWarpedMovement:1645` | JumpState 落地分類（impact） | **兩種發布時機**（M-01） | Low |
| Active gravity | `MotionDriver._activeGravity` | `ApplyJumpLaunch`（覆寫）、`GetGravityThisFrame`（落地回復） | 重力積分 | 無 | Low |
| 本帧水平位移 | 各入口 | Base：`MoveSpeed × moveSpeed`；Baked：SpeedCurve；Traversal：plan pose 差 | `CharacterController.Move` | **4 種積分**（M-03） | Low |
| Gameplay 滿速（m/s 分母） | `Bake_SprintFwdLoop.AutoAverageSpeed` = 6.2614 | `MotionDriver.Awake:487`（`moveSpeedSource`）、`CombatDirectionalSpeedProfile.maximumSpeedSource`、人工推導的 `_Speeds`／Stop `_Speed` | Base 位移、方向限速、mixer | 1 自動 ＋ 2 自動引用 ＋ N 人工（M-07，已有測試與裁決） | Low |
| Capsule center | `MotionDriver`（A48） | `UpdateCapsuleOffsetTowards:1035`、`ApplyTraversalCollisionProfile:1289`、`Restore:1355-1357`、`ClearCapsuleOffsetState:1168` | 物理、step-down query、clamp | 兩個寫入者，仲裁明確（M-04） | Low |
| Authored capsule geometry | `_capsuleBase*`（Awake 擷取） | `CaptureCapsuleBaseCenter:1142` | 偏移基準 | `_traversalOriginal*` 再擷取一份（M-04） | Low |
| Root turn rate | `UpdateCapsuleTurnRate:1267`（差分 `transform.eulerAngles`） | 同左 | capsule turn weight | 只在呼叫 `UpdateCapsuleOffset` 的 path 取樣（M-05 同型） | Low |
| Lost-support age（step-down） | `_stepDownTimeSinceGrounded` | `ApplyStepDownAssist:612-613` | gate ① | 取樣域不完整（M-05） | Low–Medium |
| Committed traversal active | **無單一表示** | `HasActiveTraversalPlan`、`_traversalCollisionProfileActive`、`_segmentedExecutionEnabled` | edge 抑制、capsule guard、segmented 執行 | 3 個（M-02） | Medium |
| Facing execution | `ApplyFacingRequest:2581`（只執行） | `CharacterFacingSource.RequestFacing` | rotation | 有些入口不消費 request（M-01） | Low |

### 主要概念的 Producer → Transform → Consumer

```text
Ground support（健康，與 docs/47 相同）
Move(...) 的副作用 → characterController.isGrounded
        ↓ SyncGroundedState（3 個入口：GetGravityThisFrame／CommittedCurve／TraversalWarped）
data.IsGrounded（原值）
data.JustLanded / JustLeftGround（groundEdgeEventGrace 0.1 s 確認 ∧ !suppressGroundEdges）
data.VerticalVelocity（見 M-01：GetGravity／TraversalWarped 發布上一帧值，CommittedCurve 發布本帧值）
```

```text
本帧位移（❗入口決定）
LocomotionModel.UpdateMotion ─┬─ stop playing → ExecuteLocomotionStopMovement（4-arg ＋ placeholder 略過 ＋ 貼地）
                              └─ else          → ExecuteBaseMovement（MoveSpeed × moveSpeed ＋ 重力 ＋ 貼地）
BaseState / JumpState         → ExecuteBaseMovement；JumpState hard recovery → ExecuteVerticalOnlyMovement
Roll / Action                 → ExecuteBakedCurveMovement（3-arg：瞬時速度、prev = cur − dt）
Hurt / Death                  → ExecuteVerticalOnlyMovement
Traversal（plan 有效）         → ExecuteTraversalWarpedMovement（plan 絕對 pose 差）
Traversal（plan 無效）         → ExecuteCommittedCurveMovement（單段梯形、垂直曲線差）
（無呼叫者）                    → ApplyBakedCompensation
```

```text
Capsule center（仲裁明確）
Awake: CaptureCapsuleBaseCenter → _capsuleBase*
locomotion／baked／vertical-only 入口 → UpdateCapsuleOffset → Pose→Cap→Smooth→Clamp → center
TraversalState.CommitWarpPlan（只在 plan 有效時）→ BeginTraversalCollisionProfile
        → ResetCapsuleOffsetImmediate（偏移歸零、center = base）→ _traversalOriginal*（＝base 的第二份）
        → ApplyTraversalCollisionProfile(n) → center = original + profile(n)
recovery／OnExit／Teleport → RestoreTraversalCollisionProfile
⇒ profile 作用中時 UpdateCapsuleOffset Guard ① 直接 return。
⚠️ plan 無效時不 Begin，但 traversal 入口也不呼叫 UpdateCapsuleOffset ⇒ center 停在進場前那一帧的偏移（M-02）。
```

---

## C. Fragmentation Findings

### M-01 「這一帧的運動模式」沒有表示法：7 個入口 × 7 個子政策 — **Medium**

**Canonical concept**：「本帧角色處於哪種運動模式，以及該模式下的每一個子政策」。UE 用 `MovementMode`（Walking／Falling／Custom）表達；`docs/42` §0 的結論也正是「`ExecuteBaseMovement` 永遠等同 `PhysFalling`，沒有 `PhysWalking` 層」——模式概念存在，只是沒被表示。

**目前版本**（`MotionDriver.cs`，靜態讀碼）

| 入口 | 呼叫者 | Facing | Capsule offset | 水平 | 垂直 | grounded 同步 | 貼地 | `VerticalVelocity` 發布 |
|---|---|---|---|---|---|---|---|---|
| `ExecuteBaseMovement:528` | Base／Jump／LocomotionModel／Roll·Action fallback | ✓ | ✓ | `MoveDirection × MoveSpeed × moveSpeed` | 重力積分 | `GetGravityThisFrame` | ✓（flag） | 上一帧（貼地夾持前） |
| `ExecuteVerticalOnlyMovement:804` | Hurt／Death／Jump recovery | ✓ | ✓ | 0 | 重力積分 | 同上 | ✗（R1） | 上一帧 |
| `ExecuteBakedCurveMovement`（3-arg）`:823` | Roll／Action | ✓ | ✓ | SpeedCurve(t) 瞬時 | 重力積分 | 同上 | ✗（`docs/44` §9.4） | 上一帧 |
| `ExecuteBakedCurveMovement`（4-arg）`:842` | **無 runtime 呼叫者**（Stop 改走下一列） | ✓ | ✓ | playhead 單段梯形 | 重力積分 | 同上 | ✗ | 上一帧 |
| `ExecuteLocomotionStopMovement:866` | LocomotionModel | ✓ | ✓ | 同上 ＋ placeholder 略過 | 重力積分 | 同上 | ✓ | 上一帧 |
| `ExecuteCommittedCurveMovement:1367` | Traversal（plan 無效） | ✓ | **✗** | playhead 單段梯形（無 placeholder 略過） | VerticalCurve 差 | 直接 `SyncGroundedState:1423` | ✗ | **本帧**（`:1417` 先算後發布） |
| `ExecuteTraversalWarpedMovement:1559` | Traversal（plan 有效） | **✗** | **✗** | plan pose 差 | plan pose 差 ＋ exit rebound | 直接 `SyncGroundedState:1625` | ✗（自帶 rebound） | 上一帧（`:1645` 在發布之後才算） |
| `ApplyBakedCompensation:2630` | **無** | **✗** | **✗** | 瞬時 ＋ 補償 | 重力積分 | `GetGravityThisFrame` | ✗ | 上一帧 |

**哪些格子是刻意的**：貼地欄（R1、`docs/44` §9.4）、traversal 不跑 capsule offset（profile 擁有膠囊，`:947` Guard ①）、traversal 自帶 rebound（`:1606-1620` 的註解，2026-09-13 playtest）、Base 發布上一帧 `VerticalVelocity`（`:2770-2772`：保留落地 impact velocity）——都有文件理由。

**哪些格子沒有理由（推導）**：
- **Facing**：plan 有效時 traversal 不消費 facing request，plan 無效時消費 ⇒ 同一個 traversal 是否會被 `CharacterFacingSource`（`MoveDirection`／combat target）轉向，取決於 plan 能不能建起來。`TraversalState` 沒有 facing commitment、也沒有 block input（`FullBodyStateMachine.TryGetActiveFacingCommitment` 只問 ActionState），所以 traversal 期間按著方向鍵就會送 request。
- **`VerticalVelocity` 發布時機**：兩條 traversal 路徑一個發布本帧、一個發布上一帧。目前 traversal 期間沒有讀它的 consumer（JumpState 不在 traversal 中），所以**今天無害**；但它說明「發布時機」是逐入口寫出來的，不是契約。
- **WriterRule 的 Owner 字串過期**：`ArchitectureRegressionTests:521-530` 寫 `Owner = "MotionDriver.GetGravityThisFrame"`，實際寫入點是 `SyncGroundedState`，且有 3 個入口直接呼叫它。測試只檢查檔名，所以不會紅，但它是這份契約唯一的「便宜摘要」（CLAUDE.md Test-as-Spec），摘要本身錯了。

**為什麼是同一概念**：每一列都在回答同一組問題——「本帧誰擁有朝向、膠囊、水平、垂直；接觸事件要不要報」。答案應該由**模式**決定，而不是由「呼叫端碰巧選了哪個方法」決定。

**分類**：③ Representation fragmentation（模式只存在於方法選擇）＋ ④ Special-case architecture（`ExecuteLocomotionStopMovement` 是為一個 flag 存在的公開入口）。

**風險**：新增入口時漏掉某個子政策（`ApplyBakedCompensation` 就是一個漏了 facing／capsule 的例子）；M-02 的三個意外差異都是這張表的空格。

---

### M-02 「正在 committed traversal」有三個表示，事件抑制鍵在錯的那一個 — **Medium（行為面 Low–Medium，待量測）**

**Canonical concept**：「本角色正處於一次已 commit 的 traversal，traversal 擁有本帧的垂直語意與 yaw」。owner 是 `TraversalState`（OnEnter→OnExit）。

**目前版本**
```text
MotionDriver.cs:1519/1572   HasActiveTraversalPlan = plan.IsValid        ← plan 有效才 true
MotionDriver.cs:899-904     _traversalCollisionProfileActive             ← 只在 plan 有效時 Begin（TraversalState.cs:375-378），recovery 時就 Restore
MotionDriver.cs:1532        _segmentedExecutionEnabled = useSegmented && plan.IsPiecewise
MotionDriver.cs:2734        suppressGroundEdges = HasActiveTraversalPlan  ← 抑制落地/離地事件
```

`TraversalState.CommitWarpPlan`（`TraversalState.cs:299-378`）有三種結局：Piecewise／EndpointFallback（plan 有效）與 **Failed**（plan 無效，走 `ExecuteCommittedCurveMovement`）。`docs/23` §R1 明說 Failed 是「最需要被看見的狀態」，所以它是真的會發生的路徑，不是理論分支。

**已可推導出的分岔**（靜態，未 Play 驗證）——全部只發生在 **Failed** 結局：
1. **落地／離地事件不抑制**：`:2720-2733` 的註解記錄 Climb2m 爬牆時膠囊擦牆會讓 `isGrounded` 瞬間 true；plan 有效時被擋掉，Failed 時照報（若擦牆持續 ≥ `groundEdgeEventGrace` 0.1 s 才會真的出聲——grace 已過濾掉大部分短擦碰）。
2. **Facing 會轉角色**：見 M-01 facing 欄。
3. **膠囊偏移凍結在進場前**：沒有 `BeginTraversalCollisionProfile` ⇒ 沒有 `ResetCapsuleOffsetImmediate`；traversal 入口又不呼叫 `UpdateCapsuleOffset` ⇒ `center` 停在進場前那一帧的 locomotion 偏移（Run 前傾約 0.2–0.3 m，`docs/27` §12.5 的量級），整段 traversal 都朝前偏。

**測試守門的狀態**：唯一守「traversal 期間不報 edge」的是 `MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact`。它的**陽性對照**在 2026-09-23 加入 `groundEdgeEventGrace` 之後就不成立（對照組現在需要等 grace 才會報），從那天起每一輪回歸都記成「只有既有 `C0`」（`WORKLOG.md` 10-08 多處）。也就是說，**這條抑制契約已經大約 17 天沒有任何測試在守**——這不是本次 traversal 改動造成的，但它讓 M-02 的任何修法都沒有回歸保護。

**為什麼是同一概念**：三個欄位都想回答「traversal 是否擁有本帧」，只是各自在不同時間點、因不同理由被加進來（edge 抑制 2026-09-21、collision profile、segmented 2026-09-20）。抑制要的是「traversal 期間」，選到的是「plan 有效」。

**分類**：③ Representation fragmentation（三個 bool）＋ ② Duplicate fact derivation（「traversal 期間」由 plan 有效性代理）。

**風險**：branch-specific bug（只在 Failed 出現，而 Failed 正是最少被 Play 看到的那條）；修 C0 時若沒先決定語意，只會把測試改綠而不是修對。

> ⚠️ 與 Traversal 審查的交界：「Failed 實際上多常發生」屬 selection／plan 側，由該審查回答；本項只描述**一旦發生**時執行端的行為。

---

### M-03 SpeedCurve「這帧走多少」有 4 種積分、「clip 時間過了多少」有 2 種定義 — **Low（數值）／Medium（知識分散）**

延伸 `docs/47` F-09（該條只看了停步預測 vs 執行）。

**目前版本**

| # | 積分方式 | 位置 | placeholder key（第 0 個 (0,0)） |
|---|---|---|---|
| a | 瞬時速度 × `Time.deltaTime` | `ExecuteBakedCurveMovementAtTimes:2525`（`usePlayheadDeltaSpeed=false`，Roll／Action） | 不處理 |
| b | 播放頭區間**單段**梯形（`Evaluate` 兩端平均 × Δclip） | 同上 `:2528-2535`（Stop）、`ExecuteCommittedCurveMovement:1396-1403` | Stop 略過（`minSpeedSampleTime`，`:883`）；Committed 不略過 |
| c | 逐 key **線性**梯形 | `MotionBakeData.GetHorizontalDisplacementAt:939`（TraversalWarpPlan、AnimationFit） | 不處理 |
| d | 逐 key 線性梯形 ＋ 略過 placeholder | `MotionBakeData.GetLocomotionStopHorizontalDisplacementAt:987`（LocomotionModel 停步預測） | 無條件略過 |
| （e） | 算術平均 | `MotionBakeData.ComputeAverageSpeed:1094`（`AutoAverageSpeed`） | **只有** key0 == (0,0) 時略過 |

另外「本帧 clip 時間前進了多少」有兩個定義：3-arg 版用 `previous = current − Time.deltaTime`（`:835`，**假設 playback rate = 1**），4-arg／Committed 用播放頭差（含 transition `_Speed`）。

**離線算術**（Python 讀 YAML 的 Hermite 曲線、模擬固定 fps 播放頭從 0 起播；**不是引擎量測**——沒有模擬 fade、Animancer 起播偏移或可變 dt）：

| Bake | rate | d（預測） | b（執行 60 fps） | 差 | 30／144 fps |
|---|---|---|---|---|---|
| WalkFwdStop_LU | 1.3328 | 0.6776 m | 0.6799 m | +2.3 mm | 0.6774／0.6786 |
| WalkFwdStop_RU | 1.3328 | 0.7280 | 0.7306 | +2.6 mm | 0.7278／0.7292 |
| RunFwdStop_LU | 1.3125 | 1.5384 | 1.5440 | +5.6 mm | 1.5380／1.5409 |
| RunFwdStop_RU | 1.2588 | 1.9789 | 1.9841 | +5.2 mm | 1.9789／1.9812 |
| Stand To Roll（a） | 1 | — | 4.5974 | vs c 4.5965：+0.9 mm | 4.5932／4.5974 |
| slash1（a） | 1 | — | 0.8020 | vs c 0.7988：+3.2 mm | 0.8020／0.8022 |
| Climb1m（b，fallback） | 1 | — | 1.3684 | vs c 1.3677：+0.7 mm | 1.3677／1.3686 |

⇒ **`docs/47` F-09 估的「cm 級」高估了**：整段停步的預測 vs 執行差 ≤ 0.6 cm，且跨 fps 穩定。數值上無害。

**仍然成立的問題**：
- **placeholder 的知識散在 4 處、用 2 種偵測法**（d 與 Stop 執行端無條件略過；e 檢查值；a／b(committed)／c 不處理）。烘焙器哪天改成寫真實的第 0 個樣本，d 與 Stop 執行端會**靜默略過一個真實樣本**，e 不會。根因跟 F-09 相同：這是烘焙器的輸出格式，卻由消費端各自記住。
- **3-arg 版的 rate = 1 假設是隱含契約**：今天所有呼叫者（Roll、Action）的 transition `_Speed` 都是 1（唯讀 grep `ScriptableObjects/Animation/*.asset`：只有 4 支 Stop ≠ 1，而它們走 4-arg），所以**目前不會錯**。但 traversal 已經有改 playback rate 的先例（`TraversalState.cs:275` Animation Fitting）；Action 一旦採用（攻速、命中停頓），3-arg 版的位移與 yaw 不會跟著縮放，而且沒有任何測試或 assert 會發現。
- 4-arg `ExecuteBakedCurveMovement` 已**沒有 runtime 呼叫者**（Stop 2026-09-24 改走專用入口），但它是 3-arg 版的正確替代。

**分類**：② Duplicate fact derivation（同一條曲線、同一個「區間位移」問題、多種答案）。repo 裡**已經有** canonical 形狀：TraversalWarpPlan 用「同一個絕對取樣函式算前後 pose 再相減」（`MotionBakeData.cs:936-937` 註解），而且 warped 執行端完全消費 plan，所以 warped traversal 的預測與執行**同源**——這是本項的正面範例。

---

### M-04 Capsule center 兩個寫入者：ownership 清楚，基準擷取兩份 — **Low**

**任務線索 3 的結論：ownership 是清楚的。**
- 跨檔：A48 保證只有 `MotionDriver.cs` 寫 `center/height/radius`。
- 檔內：「profile 作用中 ⇒ 姿勢偏移讓路」由 `UpdateCapsuleOffset:947` Guard ① 實作；交接點 `BeginTraversalCollisionProfile:888-905` 先 `ResetCapsuleOffsetImmediate` 再擷取基準，註解自己點出這是唯一會撞的點，處理正確；`Teleport:1323` 兩邊都清。

**剩下的小碎裂**：
- **authored 幾何擷取兩份**：`_capsuleBaseCenter/Radius/Height`（Awake）與 `_traversalOriginalCenter/Height/Radius`（Begin 時，`ResetCapsuleOffsetImmediate` 之後 ⇒ 數值上恆等於前者）。後者存在的唯一理由是「Begin 之前 center 可能被別人改過」，而 A48 已排除那個「別人」。③，Low。
- **Restore 還原它從沒改過的 height／radius**（`:1356-1357`）；`ApplyTraversalCollisionProfile` 只寫 center（`:1285`「C2/C3 remain explicit serialized seams」）。無害，但讀者會以為 profile 有動尺寸。
- **turn rate 取樣域**：`UpdateCapsuleTurnRate` 只在呼叫 `UpdateCapsuleOffset` 的入口推進；traversal 結束後第一帧的 turn rate ＝ 整段 traversal 的 yaw 變化 ÷ 一帧 dt ⇒ 一帧尖峰 ⇒ turn weight 歸零一帧。偏移在 Begin 時已歸零，所以**可見影響為零**；列出來是因為它跟 M-05 的 retry 計時器是同一種缺陷形狀（見 E 節）。

**分類**：① Healthy specialization（兩個寫入者語意不同、有仲裁）＋ ③（基準兩份）。M-02 的「Failed 時偏移凍結」是 traversal 表示的問題，不是 center ownership 的問題。

---

### M-05 Step-down assist：已畢業、標記沒摘、計時器取樣域不完整 — **Low–Medium**

**事實**：`docs/44` §9.4（2026-09-24）把 `docs/42` 的 prototype **正式接線**——欄位改名（`FormerlySerializedAs`）、log 拆到 `logStepDownAssist`、遮罩改為只看移動面、Stop 路徑加掛；`X Bot`／`Y Bot` 都是 `enableStepDownAssist: 1`、`stepDownQueryLayers: 8`、`stepDownRetryWindow: 0.08`；`StairAuthoringContractTests:182-185` 與 `MaskTable:214` 守住 prefab 設定；`StairTraversalPlayModeTests` 20／20。

**與事實矛盾的標記與文件**（逐項見 D 節表）：
- 程式：`_stepDownTimeSinceGrounded`、hit buffer、debug snapshot 仍標 `🔬 PROTOTYPE`；`LogStepDownAssistTrace:705` 寫「正式接線前整段連同呼叫點一起刪除」——但 `docs/44` §9.4 是**刻意保留**它（拆成 `logStepDownAssist`）。
- `docs/42` 檔頭：「預設關閉的實驗開關……⛔ 仍只是提案，尚未實作」；`docs/00-map.md:47` 同樣寫「預設關閉、⛔ 未升格正式架構」。C# 預設確實是 false，但**兩個出貨 prefab 都開著**——照字面讀會得到相反的結論（這正是背景說明裡「用 prefab 值、不要用 C# 預設值」的教訓）。
- `docs/42` §8.3 要求正式接線時移除 `_debugFootIKController`——沒有移除（M-08）。
- `docs/42` §8.4 建議的 EditMode gate 測試（`Gate_RejectsWhenAscending`、`RetryWindow_*`、`WalkableGate_*`）**沒有寫**；gate 邏輯仍是 `ApplyStepDownAssist` 內的區域變數，沒有抽成純函數。R6（起跳不被覆寫）只由 PlayMode 樓梯跳躍案例間接覆蓋。

**計時器取樣域**（推導）：`_stepDownTimeSinceGrounded` 只在 `ApplyStepDownAssist:612-613` 推進，而它只被 Base 與 Stop 入口呼叫。Roll／Action／Traversal／VerticalOnly 期間計時器**凍結**在最後一次 locomotion 帧的值（通常是 0，因為進入那些狀態時多半著地）。因此：
- 從樓梯移動面上 Roll 出去、Roll 結束時仍在空中 ⇒ 回到 Base 的第一帧 `lostSupportAge ≈ dt`，gate ① 判「剛失地」，可能把角色往下吸最多 `maxStepDownDistance` 0.25 m——雖然實際已騰空遠超過 0.08 s。
- traversal exit 後若未著地，同理。
- 影響被遮罩限制在 `CharacterMovementSurface`（只有產生式樓梯），所以只會在「樓梯上做 committed motion」時出現。

`_wasGrounded`／`_groundDivergingElapsed` 是**正面對照**：它們經 `SyncGroundedState` 在**每一個**入口推進，且用 `_groundDivergingTickFrame` 防同帧重複累加。

**分類**：④ Special-case architecture（畢業後仍以 prototype 形態存在）＋ ③（失地時間的取樣域與事實的定義域不一致）。⛔ 不涉及「三個時間窗要不要合併」——那已裁決不合併。

---

### M-06 （併入 D 節）PROTOTYPE／實驗標記盤點

任務線索 4 要求的 11 處標記逐項分類見 **D 節**。摘要：**畢業但標記沒摘 6 處；仍是實驗／診斷工具 4 處（全屬 Step-Up Diagnostics）；序列化遷移 1 處**。

---

### M-07 Move speed 與 Bake 速度：資料流健康、已被測試管理 — **Low（大部分是 §H）**

任務線索 5 的結論：**沒有違反「Bake Data 是動畫真實數值的唯一來源」。**

```text
Bake_SprintFwdLoop.AutoAverageSpeed = 6.2613893（唯讀 grep）
  ├─ MotionDriver.moveSpeedSource → Awake:487 覆寫 moveSpeed（序列化值 5.66 只是過期顯示值，docs/33 附錄 A 已記）
  ├─ XBotSpellDirectionalSpeedProfile.maximumSpeedSource（分母）
  └─ YBotCombatDirectionalSpeedProfile.maximumSpeedSource（分母）
LocomotionMixerWiringTests:403-410  Assert.AreSame(driver.moveSpeedSource, profile.maximumSpeedSource)
                                    「不能形成第二速度權威」
SpellLayeringWiringTests:208-209    玩家 overrideMoveSpeed == false、moveSpeedSource == Sprint bake
人工推導（B11 刻意）：Gait 0.3651／0.75、mixer _Speeds 1.3328／1.3125、Stop _Speed 1.3328／1.3125／1.2588
```

- 「強度 ≠ speed_i／speed_max」是 `docs/07` §3.4 的已裁決校準（mixer playback 補償），`docs/33` 附錄 A 已說明「不是錯」。
- 「人工抄數字、沒有 Dependency Update」是 `docs/02` §4.4.4 已登記、且一半由 B11 刻意決定的缺口。

**唯一沒被管住的縫**：`overrideMoveSpeed = true` 時 MotionDriver 改用手填值，但方向限速 profile 仍用 bake 分母正規化 ⇒ 第二速度權威。玩家側有測試斷言它為 false；**敵人側只斷言「引用同一份 asset」、沒斷言 override 為 false**。今天 Y Bot 是 0，所以只是一道沒上鎖的門。③，Low。

---

### M-08 MotionDriver 內的 Presentation 除錯參照：沒有滲進決策 — **Low**

任務線索 6 的結論：**沒有滲進決策。**

| 參照 | 位置 | 讀什麼 | 進入決策？ |
|---|---|---|---|
| `_debugFootIKController`（`FootIKController`） | 宣告 `:336`；使用 `:711-717`、`:754-773` | `TargetData.PelvisOffsetY`、腳權重、`GroundData.HasGround` | ❌ 只進 `Debug.Log` 字串，兩個呼叫點都在 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` 內 |
| `TraversalHandIKController` | `:1998-2005` | `Status`、手權重 | ❌ 只進 Game View 面板文字（debug 區塊內） |

**剩下的問題**（cleanup，不是架構違規）：
- 欄位宣告在 `#if` **外面** ⇒ Release build 仍編進 `Presentation.Motion → Presentation.IK` 的型別依賴（只是永遠是 null）。目前沒有 `LayerRules` 禁止這條依賴，所以測試不會紅。
- `docs/42` §8.3 明文要求正式接線時移除、並建議接線後加一條架構回歸「MotionDriver 沒有對 `Presentation/IK` 的持久參考」——兩者都沒做（M-05）。
- 量測所需的 `PelvisOffsetY` 在 `docs/44` 之後已由 PlayMode 探針（`PlantSettleProbe` 等）直接讀 FootIK，MotionDriver 這條 log 不再是唯一管道。

**分類**：① Healthy（read-only observation）＋ ④（過期的 prototype 殘留）。

---

### M-09 `ApplyBakedCompensation`：零呼叫者的第三種對齊模型 — **Low–Medium**

**目前版本**：`MotionDriver.cs:2630-2667`。全專案 runtime **零呼叫者**（唯讀 grep；測試也沒有）。它定義了一套與 `TraversalWarpPlan` 不同的 warp 語意：瞬時 SpeedCurve ＋ 每帧 `distanceToGo / remainingTime` 線性補償、yaw 以 `Slerp(…, dt/remaining)` 追目標、**不呼叫 `ApplyFacingRequest`／`UpdateCapsuleOffset`**、不走播放頭（`currentTime = normalizedTime × Duration`）、`remainingTime ≤ 0.001` 時退回 3-arg 版。

**仍被文件引用為活的 seam**：`docs/02` §MotionDriver 的 code block（該節開頭已自承「規劃版偽代碼、文件落後於實作」，連 🛑 那段 `OnAnimatorMove` 風險說明描述的都是已不存在的路徑）、`docs/03` roadmap「Motion Warping（`ApplyBakedCompensation` 兌現）」、`docs/17`／`docs/18` §「seam 已存在」、`docs/14` A28 的允許清單。

**為什麼是碎裂**：「把 authored 位移對齊到世界目標」這個概念在 repo 裡已經有**一個實作且被驗證過的** canonical 版本（`TraversalWarpPlan`：commit 時建 plan、執行期只消費、絕對取樣、I11 段界取樣、碰撞阻擋可觀測）。`ApplyBakedCompensation` 是它之前的、每帧重算目標的版本——`docs/22`／`docs/39` 已經論證過「逐帧重算 correction」是要避免的。新的 Action 貼靶若照 roadmap 去「兌現」這個 seam，會引入第二套 warp 語意。

**分類**：④ Special-case architecture（死 seam）＋ ② 潛在（第二套對齊語意）。**風險**：roadmap 誤導；入口矩陣（M-01）多一列無人守的格子。

---

## D. Special-case Object Inventory

### D.1 PROTOTYPE／實驗標記（任務線索 4，`grep -n "PROTOTYPE\|🔬\|實驗\|MEASUREMENT-ONLY"`）

| 行 | 標記對象 | 現況 | 判定 | 建議 |
|---|---|---|---|---|
| `:181` | `FormerlySerializedAs("enableStepDownAssistPrototype")` | 兩個 prefab 已用新名序列化 | 序列化遷移 | Keep 或移除皆可（低價值） |
| `:197` | `stepDownRetryWindow` tooltip「🔬 實驗值，非最終 tuning」 | 機制已畢業；`docs/42` §8.2 的 tuning pass 沒做 | **一半成立** | 改述為「機制已定、數值未經 tuning pass」 |
| `:205`／`:212` | Step-Up Diagnostics header ＋ `enableStepUpDiagnosticsPrototype` | `docs/43` 研究輪；上樓問題後由移動面（`docs/44`）處理；prefab 皆 0 | **仍是診斷工具** | 使用者裁決：保留為診斷，或依 Spike 例外刪除 |
| `:333-337` | `_debugFootIKController` | `docs/42` §8.3 要求接線時刪 | **畢業未清** | Merge candidate（移除，見 M-08） |
| `:339-341` | `_stepDownQueryHitBuffer` | 正式路徑在用 | **畢業未摘標** | 摘標 |
| `:343-345` | `_stepDownTimeSinceGrounded` | 正式路徑在用（R7） | **畢業未摘標** | 摘標；取樣域見 M-05 |
| `:568` | `MEASUREMENT-ONLY` CollisionFlags 接值 | 只餵 Step-Up log | 診斷 | 跟 `:205` 同命運 |
| `:690` | `StepDownAssistDebugSnapshot`「PROTOTYPE 專用」 | `logStepDownAssist` 正式保留的診斷 | **畢業未摘標** | 摘標 |
| `:705` | `LogStepDownAssistTrace`「正式接線前整段刪除」 | 與 `docs/44` §9.4「拆成獨立 log 開關」矛盾 | **註解與裁決矛盾** | 改註解；移除其中的 FootIK 讀取 |
| `:743` | `LogStepUpDiagnosticsTrace` | 同 `:205` | 診斷 | 同 `:205` |

### D.2 其他 special-case 物件

| Item | Created for | Consumers | Independent domain meaning? | 建議 |
|---|---|---|---|---|
| `ExecuteLocomotionStopMovement` | Stop 需要貼地＋略過 placeholder（`docs/44` §9.4） | LocomotionModel | 部分：「locomotion 延續模式」是真概念，方法本身是 flag 包裝 | Keep（已裁決）；placeholder 知識移到 bake（M-03） |
| `ExecuteBakedCurveMovement`（4-arg） | Stop 原入口 | **無 runtime 呼叫者** | 有：playhead 驅動的 authored motion | Investigate：作為 Roll／Action 的 3-arg 替代（M-03），否則刪 |
| `ApplyBakedCompensation` | 早期 Motion Warping 規劃 | **無** | 有，但已被 `TraversalWarpPlan` 取代 | **Merge／刪除候選，待裁決**（M-09） |
| `HasActiveTraversalPlan`／`_traversalCollisionProfileActive`／`_segmentedExecutionEnabled` | 各自的 traversal 子功能 | edge 抑制／capsule guard／segment 執行 | 合起來才是「traversal 擁有本帧」 | Investigate（M-02） |
| `_traversalOriginalCenter/Height/Radius` | traversal profile 基準 | `ApplyTraversalCollisionProfile`、`Restore` | 無：恆等於 `_capsuleBase*` | Merge candidate（Low） |
| `LastTraversal*` 留存欄位（7 個） | 「執行期 plan 是否與離線相同」（`docs/23` §R1） | Traversal Debug window、PlayMode 測試 | 有（觀測發布，不是結論） | Keep |
| `groundEdgeEventGrace` | 樓梯 isGrounded 閃爍造成落地聲 | JustLanded／JustLeftGround | 有 | Keep（已裁決） |
| `MaxCommittedFrameDisplacement = 10f` | committed 路徑的 NaN／爆衝防線 | Committed／Warped | 有（安全閥） | Keep |
| `IsTimeFrozen` 守衛（每個入口各寫一次） | 暫停時 `Move(0)` 毀掉 `isGrounded`（2026-07-27 實測） | 7 個入口 | 有 | Keep；是 M-01「每入口重寫」的一列，但每列都一致 |

---

## E. Repeated Decision Rules

| 規則 | 版本數 | 位置與值 |
|---|---|---|
| 「traversal 擁有本帧」 | **3** | `HasActiveTraversalPlan`（plan 有效）；`_traversalCollisionProfileActive`（Begin→recovery）；`_segmentedExecutionEnabled`（authored model ∧ piecewise） |
| 「本帧是否套用 facing request」 | **2** | 6 個入口消費；`ExecuteTraversalWarpedMovement`／`ApplyBakedCompensation` 不消費（request 在下一帧被 `:2583-2588` 丟棄） |
| 「本帧是否更新 capsule offset」 | **2** | 5 個入口呼叫；Committed／Warped／Compensation 不呼叫 |
| `VerticalVelocity` 發布時機 | **2** | 上一帧值（GetGravity、Warped）；本帧值（Committed） |
| 「clip 時間前進了多少」 | **2** | `Time.deltaTime`（3-arg，rate=1 假設）；播放頭差（4-arg、Stop、Committed、Warped） |
| SpeedCurve 區間積分 | **4**（＋平均 1） | 瞬時；單段梯形；逐 key 線性；逐 key 線性＋略過 placeholder（M-03） |
| placeholder key 偵測 | **2** | 無條件略過（Stop 執行、Stop 預測）；值檢查（`ComputeAverageSpeed`） |
| 「失地多久」 | **3**（已裁決不合併） | fall entry（JumpState）；edge grace（`_groundDivergingElapsed`，每入口推進）；step-down retry（`_stepDownTimeSinceGrounded`，**只在 Base／Stop 推進**） |
| 跨幀取樣器的取樣域 | **3 種** | 每入口（`SyncGroundedState` 系列）✓；只在 capsule 入口（turn rate）；只在 assist 入口（retry 計時器）——後兩者跨 committed motion 會過期 |
| 貼地速度 `reboundForce` −2 m/s | 2 處用、同語意 | `GetGravityThisFrame:2685`；traversal exit `:1619` | ① 同一概念重用，健康 |
| 「角色所在的碰撞世界」遮罩 | 2（刻意） | capsule clamp：由 Layer Collision Matrix 推導（`MovementCollisionMask:1250`）；step-down：序列化遮罩只看移動面 | ① 不同問題，`MaskTable` 已逐元件寫死 |

---

## F. Data Plumbing

### F-P1：滿速分母（1 自動 ＋ 2 自動引用 ＋ N 人工）
```text
Bake_SprintFwdLoop ─┬─ MotionDriver.moveSpeedSource ──Awake──▶ moveSpeed（runtime）
                    ├─ X/Y DirectionalSpeedProfile.maximumSpeedSource ──▶ 正規化分母
                    └─（人工）Gait 強度、mixer _Speeds、Stop _Speed
```
**判斷**：自動的三條由 `LocomotionMixerWiringTests` 斷言同源 ⇒ **保持**。人工那幾條是 B11 的刻意決定 ⇒ 保持。唯一可補的是 M-07 的 enemy `overrideMoveSpeed` 斷言（test-only）。

### F-P2：除錯讀值（Motion ← IK）
```text
FootIKController.TargetData / GroundData ──GetComponentInChildren──▶ MotionDriver Debug.Log
```
**判斷**：方向是 Presentation 內部側讀，不進決策；但它是 prototype 殘留，量測需求已由 PlayMode 探針承接 ⇒ **移除**（M-08）。

### F-P3：traversal plan → 執行
```text
TraversalState.CommitWarpPlan → BeginTraversalPresentation(plan, diag, segmented)
  → ExecuteTraversalWarpedMovement（只消費 plan，不重查環境）→ MoveTraversalAndRecord → LastTraversal*
```
**判斷**：**健康**。預測與執行同源（同一個 plan 的絕對取樣），阻擋量可觀測，I11 只在段界取樣 actual root。這是 M-03 想推廣的形狀。

---

## G. Top Refactor Candidates（最多 5 個，皆未實作）

### G-1 單一「traversal 擁有本帧」表示，事件抑制改鍵
```text
Current   edge 抑制看 plan 有效；Failed 結局不抑制、會被 facing 轉、膠囊偏移凍結（M-02）
→ Problem  同一個 committed traversal，三個子政策取決於 plan 能不能建起來
→ Canonical concept  「committed traversal 期間」＝ TraversalState OnEnter→OnExit
→ Desired ownership  TraversalState 宣告（例如 BeginTraversalPresentation 無論 plan 是否有效都設一個 active 旗標——它本來就無論如何都被呼叫）；MotionDriver 的 edge 抑制、facing、capsule 改讀它
→ What should disappear  suppressGroundEdges 對 HasActiveTraversalPlan 的依賴
→ What should remain  HasActiveTraversalPlan（真的表示 plan 有效，debug／Hand IK 用）、profile 旗標（profile 的生命週期本來就比 traversal 短）
```
前置：先修 `C0` 的陽性對照（test-only：對照組要先推進超過 `groundEdgeEventGrace`），讓這條契約重新有守門。⚠️ Failed 時「要不要讓 facing 轉角色」與「膠囊要歸零還是繼續求解」是**語意選擇**，需使用者裁決；不碰黑板、不碰 FSM 拓撲 ⇒ Living Spec，不需 ADR。

### G-2 Step-down assist 畢業收尾
```text
Current   正式接線但仍以 prototype 形態存在（M-05、M-08、D.1）
→ Canonical concept  docs/42 R1–R10 ＋ docs/44 §9.4 的正式機制
→ What should disappear  6 處過期標記、_debugFootIKController 與兩個 log 裡的 IK 讀取、「正式接線前刪除」的矛盾註解
→ What should change  retry 計時器改在每個入口推進（比照 SyncGroundedState），或在 SyncGroundedState 內維護——⚠️ 這只改取樣域，不合併時間窗
→ What should be added  docs/42 §8.4 的 EditMode gate 測試（抽純函數）；docs/42 §8.3 建議的「Motion 不持有 IK 參照」架構回歸
→ Docs  docs/42 檔頭狀態與 00-map 那一列（由負責 map 的 session／使用者改）
```
需要使用者先確認「step-down assist 已畢業」這個事實判定；Step-Up Diagnostics 的去留另行裁決。

### G-3 入口子政策表（Living Spec ＋ 架構測試）
```text
Current   7 入口 × 7 子政策只存在於程式（M-01）；dev-spec MotionDriver 節是過期偽代碼
→ Canonical concept  運動模式：LocomotionContinuation／AuthoredCommitted／TraversalCommitted／VerticalOnly
→ Desired  把 M-01 的表寫進 Living Spec（取代 docs/02 MotionDriver 節的偽代碼），並以 Test-as-Spec 斷言關鍵格（例：traversal 入口不得呼叫 UpdateCapsuleOffset；所有入口都經 SyncGroundedState）
→ What should disappear  docs/02 的 OnAnimatorMove 偽代碼與已不存在路徑的 🛑 風險說明；WriterRule 過期的 Owner 字串
→ What should remain  多個公開入口（H 節：它們對應不同位移來源）
```
⛔ 不建議現在就把入口收成一個 `Execute(mode, …)`——那是大範圍重寫，換不到行為保護；先讓表存在、讓測試守它，等第二個「漏格」出現再談。

### G-4 位移積分收斂到 bake 擁有的「區間位移」
```text
Current   4 種積分、placeholder 知識在 4 處（M-03）
→ Canonical concept  MotionBakeData：「clip 時間 t0→t1 的水平位移」，placeholder 由 bake 自己知道
→ Desired ownership  MotionBakeData（純查詢，零 GC）；執行端一律 D(t1) − D(t0) × 播放頭（TraversalWarpPlan 已是此形）
→ What should disappear  ExecuteLocomotionStopMovement 的 minSpeedSampleTime、GetLocomotionStopHorizontalDisplacementAt 與 GetHorizontalDisplacementAt 的分家、3-arg 的 dt 推播放頭
→ What should remain  ComputeAverageSpeed 的「平均」語意（那是另一個問題）
```
⚠️ 會改 Roll／Action 的逐帧位移（離線估計整段 ≤ 3 mm，但逐帧分佈不同）⇒ 需 PlayMode 前後對照；數值收益小，主要收益是「烘焙格式只有一個人知道」。可排在 3-arg 版真正遇到 playback rate ≠ 1 時一起做。

### G-5 `ApplyBakedCompensation` 去留
```text
Current   零呼叫者；文件仍當 Motion Warping seam（M-09）
→ Canonical concept  committed-plan warp（TraversalWarpPlan）
→ Option A  刪除，roadmap／docs/17／docs/18 改指向 TraversalWarpPlan 作為 warp 先例
→ Option B  保留但明標「未使用、語意與 TraversalWarpPlan 不同、兌現前需重新設計」
```
需使用者裁決（roadmap 項目的歸屬是產品決定）。

**刻意不列入**：M-04（基準兩份）、M-07（enemy override 斷言）——都是一行等級的 cleanup，不值得佔候選名額。

---

## H. Things That Look Fragmented But Should NOT Be Merged

| 看起來重複 | 為什麼保持分離 |
|---|---|
| 多個公開 `Execute*` 入口 | 對應不同位移來源（procedural／authored 曲線／committed plan／純垂直），共用唯一 `Move` 出口；`docs/47` §H 已列。要收斂的是**子政策的表示**（G-3），不是入口數 |
| Roll／Action 不貼地、locomotion 貼地 | `docs/44` §9.4 裁決：authored motion 不被貼地改寫；Codex round 4 確認 |
| `reboundForce` 同時用於 locomotion 與 traversal exit | 同一個概念（「站在地上時的貼地速度」）在兩處使用；exit 段刻意沿用，讓 grounded 由物理回答（`:1606-1620`） |
| capsule clamp 遮罩（由碰撞矩陣推導）vs step-down 遮罩（序列化、只看移動面） | 不同問題：前者「什麼會擋住膠囊」，後者「哪些面允許貼地」；`docs/44` §5、R10，`StairAuthoringContractTests.MaskTable` 逐元件寫死 |
| `groundEdgeEventGrace` vs `stepDownRetryWindow` vs `FallEntryGrace` | 不同語意（事件確認／運動重試預算／FSM 進 Falling）；2026-10-07 已裁決不抽共用。M-05 只談取樣域 |
| `IsGrounded` 原值 vs `_wasGrounded`（已確認） | 一個是 gating 事實、一個是事件基準；`docs/38` §13 禁止把確認延遲套到 `IsGrounded` |
| 強度（0.3651／0.75）≠ speed_i／speed_max | `docs/07` §3.4 的校準設計，mixer `_Speeds` 補償；`docs/33` 附錄 A |
| 人工推導的 mixer／Stop playback 速度 | CLAUDE.md B11 / Gate B：不寫 Animancer 內部序列化 |
| Capsule V2 五層 offset（Desired／Capped／Smoothed／Safe／Current） | 每層有獨立 debug 意義；`_capsuleSmoothedOffset` 刻意只追 Capped，避免牆變成平滑器的第二 authority（`:250-256` 註解） |
| `CapsuleOffsetSolver`（夾持算術）vs `CapsulePoseSolver`（姿勢求解） | 名字相近，責任不同；都是純函數、EditMode 可測。可考慮改名，**不要合併** |
| `LastTraversal*` 留存欄位 vs `Active*` | 前者刻意在 `EndTraversalPresentation` 後保留，回答「執行期用的 plan 跟離線算的是否同一個」 |
| `TraversalExecutionSegment` 的 carried error 只在段界取樣 | I11（`docs/39`）：段內重取樣 actual root 會把碰撞誤差回授進 plan |
| `Teleport` 不重設 `_wasGrounded` | `Teleport` 的 remarks（`:1315-1319`）：原地傳送時硬清會偽造一次落地 |

---

## I. Architecture Health Verdict

> **MotionDriver 是一個健康的「執行者」：它擁有的事實（位置、旋轉、膠囊形狀、接觸）都只有一個寫入者且被測試守住，也沒有反向重建 Core 的決定。它的碎裂不在事實，而在政策與生命週期——「這一帧是哪種運動模式」只活在方法選擇裡，以及實驗畢業後沒有回收。**

**支持「健康」的證據**
1. A5／A20／A48 讓 position、rotation、shape、`IsGrounded`／`VerticalVelocity`／edge events 都只有 `MotionDriver.cs` 能寫；`Teleport` 是唯一瞬移入口。
2. MotionDriver 讀的 Core 狀態（`MoveSpeed`／`MoveDirection`／`IsDead`）都是 committed 值，沒有自己重判；停步、facing、跳躍初速都是上游決定後注入。
3. 滿速分母的三條自動引用被斷言同源（`LocomotionMixerWiringTests`），人工推導的部分有明文裁決（B11）。
4. Traversal warped 執行完全消費 commit 時的 plan，預測與執行同源——這是全專案「位移預測 vs 執行」最乾淨的一處。
5. Capsule 兩個 center 寫入者有明確仲裁與交接點，且有 PlayMode 測試。

**支持「政策層碎裂」的證據**
1. 7 個入口各自寫出 7 個子政策（M-01）；`ApplyBakedCompensation` 證明新入口會漏格。
2. 「traversal 擁有本帧」有三個表示，事件抑制選了 plan 有效性；守它的測試已紅 17 天（M-02）。
3. 4 種 SpeedCurve 積分、placeholder 知識 4 處 2 法（M-03）——數值無害，知識分散。
4. 跨幀取樣器有三種取樣域，只有 `SyncGroundedState` 系列是「每入口都推進」（E 節）。

**趨勢判斷**：與 `docs/47` §I 同一個模式——**每個 slice 的決定都站得住，問題在實驗結束後沒有回收**：step-down assist 畢業了但 prototype 形態還在、`ApplyBakedCompensation` 被 `TraversalWarpPlan` 取代但沒刪、4-arg `ExecuteBakedCurveMovement` 被專用入口取代但沒收、C0 紅了但被記成「既有」。`docs/47` 已建議把「Trial／A/B 結束 ⇒ 收開關、刪輸家、把代理換成 owner」列入 slice 收尾條件；本審查補一條：**「測試因為合理的行為變更而紅」不得記成「既有失敗」超過一個工作包——要嘛同包改測試，要嘛登記為待裁決**。

---

## J. 待量測清單（⛔ 本次未做，需 Editor／PlayMode）

| # | 要回答的問題 | 量什麼 | 怎麼量 |
|---|---|---|---|
| T1 | Failed 結局的實際頻率 | `TraversalWarpPlanDiagnostics.Mode` 分佈 | Traversal probe 對各 kind × 進場距離／角度掃描（Traversal 審查的探針可順帶記） |
| T2 | Failed 時是否出現假落地聲 | `JustLanded`／`JustLeftGround` 在 traversal 期間的次數 | 強制 Failed（把 binding 的 `MaxHorizontalCorrection` 設 0 的測試實例，不改資產），對照 plan 有效的同一場景 |
| T3 | Failed 時 facing 是否轉角色 | 每帧 `transform.eulerAngles.y` vs bake yaw 積分 | 同 T2，traversal 期間持續送 45° 斜向的 `MoveDirection` |
| T4 | Failed 時膠囊偏移 | `characterController.center − CapsuleBaseCenter` 整段 | 同 T2，進場前以 Run 前傾 |
| T5 | retry 計時器過期是否造成吸附 | Roll／traversal exit 後第一個 Base 帧的 `lostSupportAge`、真實騰空時間、`AssistApplied`、`actualDY` | 開 `logStepDownAssist`，在產生式樓梯移動面上從中段 Roll 下樓；`StairTraversalPlayModeTests` 架構可直接加案例 |
| T6 | M-03 離線估計在引擎內是否成立 | 停步預測落點 vs 實際 root 終點 | 沿用 `PlantSettleProbe` 的停步案例，記 `LocomotionStopPrediction` 與停步結束時的 root 位移（離線估 ≤ 6 mm） |
| T7 | 3-arg 版在 playback ≠ 1 時的誤差 | 位移 vs 動畫 root 實際行進 | 只有 Action 要採用 playback rate 時才需要；測試實例上改 transition speed（不改資產） |

---

## K. 需要使用者裁決的點

1. **Step-down assist 是否確認為已畢業的正式機制？** 若是 ⇒ G-2 的收尾（摘標、移除 FootIK 除錯參照、補 §8.4 gate 測試、retry 計時器取樣域）可作為一個 cleanup 工作包；`docs/42` 檔頭與 `docs/00-map.md` 那一列需更新。
2. **Step-Up Diagnostics（`docs/43`）要保留為診斷工具還是依 Spike 例外刪除？**
3. **Failed 結局的 traversal 語意**（G-1）：edge 抑制、facing、膠囊偏移要不要跟「traversal 期間」走？以及 `C0` 是否同包修（test-only）。
4. **`ApplyBakedCompensation` 刪除或明標未使用**（G-5）；roadmap 的 Motion Warping 是否改以 `TraversalWarpPlan` 為先例。
5. **（可延後）** 入口子政策表要不要寫成 Living Spec ＋ 架構測試、並取代 `docs/02` MotionDriver 節的過期偽代碼（G-3）。

---

## 附錄：本次讀過的關鍵 call site

`MotionDriver.cs:140-460（欄位）,460-500,504-590,600-690,700-800,804-886,888-905,943-1050,1140-1170,1250-1300,1323-1438,1494-1935,2516-2812` ｜ `CapsuleOffsetPolicy.cs`（全）｜ `CapsulePoseSolver.cs`／`CapsulePoseProfileSO.cs`（公開面）｜ `MotionBakeData.cs:720-775,930-1043,1070-1106` ｜ `LocomotionModel.cs:95-170,300-340` ｜ `TraversalState.cs:215-440` ｜ `RollState.cs:85-116` ｜ `ActionState.cs:285-305` ｜ `CharacterFacingSource.cs:40-100` ｜ `FullBodyStateMachine.cs:47-50` ｜ `ArchitectureRegressionTests.cs:515-530,1074-1084,1556-1590` ｜ `MotionDriverCommittedMovementPlayModeTests.cs:105-175` ｜ `LocomotionMixerWiringTests.cs:395-415` ｜ `SpellLayeringWiringTests.cs:200-210` ｜ `StairAuthoringContractTests.cs:160-215` ｜ `X Bot.prefab:4352-4373`／`Y Bot.prefab:426-447`（MotionDriver 序列化值）｜ `ScriptableObjects/Animation/*.asset`（`_Speed`）｜ `ScriptableObjects/Motion/Bake_{SprintFwdLoop,RunFwdLoop,WalkFwdLoop,*Stop_*,Stand To Roll,slash1_Baked,Climb1m,Climb2m,Vault1m}.asset`（`AutoAverageSpeed`／`SpeedCurve`）｜ `Gait_ActionRPG.asset` ｜ `docs/02` §4.4.4、§MotionDriver ｜ `docs/07` §3.4 ｜ `docs/33` 附錄 A／B ｜ `docs/42` 檔頭、§8 ｜ `docs/43` 檔頭 ｜ `docs/44` §9.4 ｜ ADR-001

---

## L. 技術債收斂規劃（2026-10-10，同日第二輪；⛔ 只規劃，runtime 未改）

> **輸入**：使用者對 §K 的裁決（本節 L.0）。**本輪優先 M-02、M-05**，其餘建立工作清單。
> **證據分級**（每一項都標）：🔴 **已證實缺陷**（讀碼即可完整證明，且有可觀測後果）｜🟠 **推導風險**（讀碼證明條件成立，行為後果未量測）｜📄 **文件不一致**｜🧹 **純清理**（無行為後果）。
> **執行環境分級**：**Offline**＝`dotnet build`＋.NET 反射跑純函數／純字串 fixture（不碰 `GameObject`／`ScriptableObject`／`AnimationCurve.Evaluate`；做法見 memory `commit-from-mixed-worktree`）｜**UTR-Edit**＝Unity Test Runner EditMode（Editor 進程：live `unity cmd run_tests`，或 Editor 關閉時 batch）｜**UTR-Play**＝Unity Test Runner PlayMode｜**Human**＝人工 Play。⚠️ 本計畫**沒有任何一步需要改資產**。

### L.0 使用者裁決（2026-10-10）

| # | 裁決 |
|---|---|
| M-02 | 區分 **Traversal 執行狀態** 與 **WarpPlan 有效性**；先建真值表（含 Failed fallback）；⛔ 不以單一旗標全面取代既有條件；先查 C0 長期失敗的原因、確認契約，再決定改測試還是 runtime |
| M-05 | 狀態定為 **Integrated（已正式接線），尚未 Stable**；先分析失地計時器在 Normal／Stop／Roll／Action／Traversal 的生命週期，特別是模式切換時的過期狀態與向下吸附；補齊自動化＋PlayMode 驗證**之後**才清 Prototype 標記與過期文件 |
| Step-Up Diagnostics | 保留可重複使用的診斷能力；盤點一次性探針與依賴、提出建議，⛔ 不立即全刪 |
| M-09 | 確認沒有程式／測試／其他實際依賴後，規劃刪除並同步規格文件 |
| M-01／M-03 | 先建立入口政策矩陣與 SpeedCurve 共用語意規格；保留合理的入口差異；⛔ 沒有證據不重構已正確運作的數值邏輯 |
| 限制 | 先分析與規劃、不改 runtime；每項修改附回歸測試；不破壞 Jump／Traversal／Locomotion 契約；標出與 Traversal 會話的檔案重疊；標出哪些需要 Editor |

---

### L.1 M-02：Traversal 執行狀態 vs WarpPlan 有效性

#### L.1.1 四個相關概念（不合併）

| 概念 | 定義 | 生命週期 | 目前表示 | 正確的 consumer |
|---|---|---|---|---|
| **Traversal 執行中**（execution active） | `TraversalState` 已 commit，MotionDriver 本帧在執行 committed traversal motion（不論 plan 結局） | `BeginTraversalPresentation`（`CommitWarpPlan` 內**無條件**呼叫，`TraversalState.cs:382`）→ `EndTraversalPresentation`（`OnExit`，`:231`） | **不存在**（目前以 `HasActiveTraversalPlan` 代理） | 接觸事件抑制、facing 消費、capsule pose offset 暫停 |
| **WarpPlan 有效**（plan valid） | commit 時建出可用的 `TraversalWarpPlan` | 同上，但只在 `plan.IsValid` 時為 true | `HasActiveTraversalPlan`（`MotionDriver.cs:1519/1572`） | warp 位移來源、Hand IK（`TraversalHandIKController.cs:208/577`）、Traversal Debug Window |
| **Collision profile 作用中** | binding 的膠囊 profile 正持有 `center` | Begin（plan 有效時）→ recovery 時刻或 OnExit | `_traversalCollisionProfileActive` | `UpdateCapsuleOffset` Guard ①、`ApplyTraversalCollisionProfile` |
| **Segmented 執行** | binding 選 Segmented ∧ plan 是 piecewise | Begin → End | `_segmentedExecutionEnabled` | `TryExecuteSegmentedTraversal`、Hand IK（`:484`） |

⇒ 建議**新增**一個 execution-active 表示（MotionDriver 私有狀態＋唯讀屬性），**保留**另外三個不動。`HasActiveTraversalPlan` 的語意**正是**「plan 有效」，它的兩個外部 consumer 依賴這個語意——改它會破壞 Hand IK（`ArchitectureRegressionTests:1626` 也以字串鎖住 Hand IK 讀它）。

#### L.1.2 真值表：每個關注點在每種結局下的現況與建議判斷條件

結局：**PA**＝Piecewise／Absolute（Vault1m）｜**PS**＝Piecewise／Segmented（Climb2m）｜**EP**＝Endpoint（Climb1m 出貨路徑，以及任何 piecewise 被拒者，`docs/52` T-04）｜**FL**＝Failed（plan 無效，走 `ExecuteCommittedCurveMovement`；`docs/52` T-01 B3「牆後無地面仍 Vault」是確定的觸發條件）。

| 關注點 | PA | PS | EP | FL（現況） | 建議判斷條件 | FL 建議 | 分級 |
|---|---|---|---|---|---|---|---|
| Warp 位移來源 | plan | plan（分段） | plan（endpoint） | bake 曲線 | **plan valid**（不變） | 不變 | — |
| Segmented 執行 | ✗ | ✓ | ✗ | ✗ | binding ∧ piecewise（不變） | 不變 | — |
| 接觸事件抑制（`JustLanded`／`JustLeftGround`） | ✓ | ✓ | ✓ | **✗** | **execution active**（新） | ✓ | 🟠 |
| `IsGrounded` 原值 | 原值 | 原值 | 原值 | 原值 | 不變（`docs/38` §13） | 不變 | — |
| Facing request 消費 | 丟棄 | 丟棄 | 丟棄 | **套用** | execution active（**待裁決 D-1**） | 丟棄 | 🟠 |
| Capsule pose offset | Begin 歸零→profile | 同 | 同 | **凍結在進場前** | execution active ⇒ 暫停並歸零（**待裁決 D-2**） | 歸零 | 🟠 |
| Collision profile | binding profile 至 recovery | 同 | 同 | 無 | plan valid（不變） | 不變 | — |
| Post-exit rebound 貼地 | exit knot 後 | 同 | **無** | **無** | 現為 `plan.IsPiecewise`；EP／FL **先量測**（WP-C） | 先量測 | 🟠（`docs/52` T-09） |
| Hand IK | plan contacts | ＋segmented | `RootMappingUnavailable` | 無 | plan valid（不變） | 不變 | — |
| `VerticalVelocity` 發布時機 | 上一帧 | 上一帧 | 上一帧 | **本帧** | 不改；寫進規格（traversal 期間目前沒有 consumer） | 不變 | 📄 |

**相位補充**（同一結局內也會變）：
- **播放前**（`isActuallyPlaying == false`，`TraversalState.cs:265-272`）：Absolute／EP／FL 會發出 `Move(Vector3.zero)` ⇒ 依 `IsTimeFrozen` 註解的實測，`isGrounded` 下一帧變 false ⇒ 產生一次分歧（PS 不發 Move，不受影響）。plan 有效時被抑制；**FL 不被抑制 ⇒ 0.1 s 後 `JustLeftGround` ⇒ `JumpTakeoff` 音效**（🟠 推導）。
- **recovery 之後**（`_canRecover`）：profile 已還原、plan 仍有效、FSM 可能下一帧就離開；抑制與 plan valid 同生命週期，不受影響。
- **EP 的 warp 結束後**：plan 位置凍結 ⇒ 每帧 `Move(0)` ⇒ `isGrounded` 恆 false、Foot IK 關閉直到離開狀態（`docs/52` T-09 推導）。

**對 Jump／Locomotion／Traversal 契約的影響**：PA／PS／EP 下，「execution active」與「plan valid」的生命週期**逐帧相同**（都在 Begin 設、End 清）⇒ 事件抑制**逐位元不變**；只有 FL 改變。Jump 的 fall entry 讀 `IsGrounded`（不變）；`T9_FinishedWhileAirborne_TransitionsToExistingJumpFallEntry` 守 traversal→Jump 交接。

#### L.1.3 C0 長期失敗：原因與契約

**原因（🔴 已證實，讀碼完整證明）**：`C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact`（`MotionDriverCommittedMovementPlayModeTests.cs:121`）的陽性對照是「設 `_wasGrounded=false` 後呼叫**一次** `SyncGroundedState`，期待 `JustLanded`」。2026-09-23 加入 `groundEdgeEventGrace`（測試宿主吃 C# 預設 0.1）之後：
`TrackGroundStateDivergence` 第一次分歧回傳 **0** → `ShouldConfirmGroundStateChange(0, 0.1)` → **false** ⇒ 對照組永遠拿不到 `JustLanded` ⇒ 測試在對照組就失敗。
更糟的是：就算拿掉對照組，受測段（同一帧第二次呼叫）**也一定拿不到 `JustLanded`**（grace 未滿）⇒ 斷言 `IsFalse(JustLanded)` 會**空洞地通過**。這正是該測試註解警告的情況。

**契約本身沒有被推翻**（`SyncGroundedState:2734-2765` 的語意與 2026-09-21 的裁決一致）。寫成明確條文：
- **C-E1**：確認時刻落在 traversal 執行期間的接觸變化**不回報**（落地與離地皆然）；`_wasGrounded` **照常前進**（不回報≠沒發生）。
- **C-E2**：traversal 結束後，相對於已前進的 `_wasGrounded` 的下一次分歧**照常回報**——不補發、不吞掉。⇒ EP／FL 若以「未觸地」離開 traversal，離開後落地會正常報 `JustLanded`（那是 rebound 軸的問題，見 WP-C，不是抑制軸）。
- **C-E3**：`IsGrounded` 永不抑制（`docs/38` §13）。
- **C-E4**：grace 只延遲確認，不改變 C-E1／C-E2 的判斷時刻＝**確認時刻**。

**結論：先修測試，不修 runtime**（WP-A）。修法：陽性對照與受測段都**逐帧**呼叫 `SyncGroundedState`（`yield return null` 推進 `frameCount`／`deltaTime`），直到超過 production 的 `groundEdgeEventGrace`（上限 1 s 防無窮迴圈）；對照組必須真的拿到 `JustLanded`，受測段在同樣的帧數內必須拿不到。

#### L.1.4 M-02 工作包

| WP | 內容 | 分級 | 行為變更 | 回歸測試 | 環境 |
|---|---|---|---|---|---|
| **WP-A** C0 修復（test-only） | 依 L.1.3 改寫 C0；新增 `C0b_GroundEdgeSuppressedAcrossGraceWindow`（分歧在 traversal 中開始、grace 滿時仍在 traversal ⇒ 不報）、`C0c_EdgesResumeAfterEndTraversalPresentation`（C-E2） | 🔴 測試缺陷 | 無 | 本身即測試；PlayMode 基線 7 → 6 failed（剩 6 個 Stair 骨盆，屬 Foot IK 範圍） | UTR-Play |
| **WP-B** execution-active 表示 | 新增私有旗標（Begin 設、End 清；`Teleport` 不碰——死亡時 `OnExit` 會呼叫 End）；`SyncGroundedState` 的抑制改讀它；**D-1／D-2 裁決後**，`ExecuteCommittedCurveMovement` 依裁決不消費 facing、`BeginTraversalPresentation` 無條件 `ResetCapsuleOffsetImmediate` | 🟠 | **只有 FL**：不再出假的離地／落地事件；（D-1）不再被 facing 轉；（D-2）膠囊不再前偏 | 新增（皆放 `MotionDriverCommittedMovementPlayModeTests`，合成宿主）：`M02_Failed_SuppressesGroundEdges`（舊碼紅）、`M02_Failed_IgnoresFacingRequest`（D-1）、`M02_Failed_CapsuleCenterAtBase`（D-2，舊碼紅）、`M02_PlanValidity_UnchangedForFailed`（`HasActiveTraversalPlan==false`，守 Hand IK 語意）；架構測試：`SyncGroundedState` 的抑制條件不得引用 `HasActiveTraversalPlan`（字串，Offline）；既有 `C0*`、`T7`、`T9`、`V2_*`、`Climb2m_RealPrefab_*` 必須維持 | UTR-Play ＋ Offline |
| **WP-B′** 整合驗證 | `docs/52` B3 場景（牆後無地面 ⇒ FL）在 `TraversalStatePlayModeTests` 的 rig 上跑一次：整段 0 個 edge event | 🟠 | — | 新增 1 個 PlayMode | UTR-Play；⚠️ 與 Traversal 會話檔案重疊 |
| **WP-C** EP／FL 的 post-exit 貼地 | **先量測、不修**：Climb1m（EP）真實 prefab 的 `isGrounded` 時間線、Foot IK 啟用帧、離開後是否出現 `JustLanded` | 🟠（`docs/52` T-09） | 量測本身無 | 量測腳本可直接升格為 PlayMode 測試（若裁決要修） | UTR-Play；修正後需 **Human**（Climb1m 上平台觀感） |

**驗收（M-02）**：WP-A 後 C0 綠，且對照組確實能紅（在 scratch 暫時拿掉抑制，C0 必須紅——驗一次、不 commit）；WP-B 後新測試全綠、PA／PS／EP 既有測試全綠、`HasActiveTraversalPlan` 外部 consumer 行為不變；FL 情境人工可聽（選做）。

#### L.1.5 與 Traversal 會話的重疊

| 檔案 | 本計畫的動作 | Traversal 會話（`docs/52`） | 處置 |
|---|---|---|---|
| `MotionDriver.cs` traversal 區段（`Begin`／`EndTraversalPresentation`、`SyncGroundedState`、`ExecuteCommittedCurveMovement`、`:1617` rebound） | WP-B 改；WP-C 可能改 rebound | 尚未改過；T-04 已把 rebound 軸**轉給本文件**（`docs/52` K.1） | 本計畫擁有；開工前確認 Traversal 會話沒有在改 |
| `TraversalState.cs` | **不改**（WP-B 只在 MotionDriver 內加旗標；Begin／End 本來就無條件呼叫） | `a330bb8` 已改；J1／J2／J3′ 若裁決會再改 | 無寫入重疊 |
| `TraversalStatePlayModeTests.cs` | WP-B′ 新增 1 個測試 | `a330bb8` 已改；J1／J2 若修會再改 | ⚠️ **重疊**：WP-B′ 排在 Traversal 會話閒置時做，或交給它做 |
| `docs/22-traversal-v1.md` | M-09 只牽涉歷史表格（可不改） | `6dc24c9` 剛改 §21 | 建議**不改** |
| `docs/00-map.md`／`WORKLOG.md` | 要補 `docs/50`（目前**沒有**索引列）、更新 `docs/42`／`docs/43` 兩列 | 兩者都改過 | 由使用者或最後收尾的會話一次改 |
| Unity Editor | WP-A／B／D／E 都要用 UTR | Traversal 會話是唯一用過 live Editor 的 | **同一時間只能有一個會話用 Editor** |
| `TraversalExitValidityPlayModeTests.cs`（**新檔，未 commit，Traversal 會話進行中**） | 不碰 | test-first：J1／J2 與 `PlanValidity_NoBuildablePlan_DoesNotCommitTraversal`（「沒有有效 plan 時不得承諾 traversal」）；runtime 尚未改 | 見 L.1.6 |

#### L.1.6 ⚠️ 進行中的相依：Traversal 會話可能讓 FL 不可達

撰寫本節時（2026-10-10），工作樹出現 Traversal 會話的未 commit 測試檔 `TraversalExitValidityPlayModeTests.cs`，其中 `PlanValidity_NoBuildablePlan_DoesNotCommitTraversal` 斷言「**建不出有效 plan 就不得進入 Traversal**」、`J2_Vault_NoGroundBehind_IsNotACandidate` 斷言 B3 場景不再是候選。對應的 runtime 尚未落地。

| 若該工作落地 | 對本計畫的影響 |
|---|---|
| FL 透過正常 gameplay **不可達** | L.1.2 的 FL 欄改標「設計上不可達」；**WP-B 縮小**：不新增 execution-active 旗標（所有可達結局下它與 plan valid 逐帧相同），改為加一條 invariant 測試「`TraversalState` 只在 plan 有效時執行 committed motion」；**WP-B′ 取消**（被該會話的測試取代）；D-1／D-2 **撤回** |
| `ExecuteCommittedCurveMovement` 失去 gameplay 呼叫者（只剩 FL 使用） | 變成第二個零呼叫者入口 ⇒ 併入 D-5 的 allowlist／刪除討論（它目前還被 3 個 PlayMode 測試直接呼叫，A-test `:1381` 也以字串鎖住 TraversalState 引用它） |
| WP-A（C0）、WP-C（EP 貼地） | **不受影響**：C0 守的是 PA／PS／EP 都會用到的抑制契約；EP 仍是 Climb1m 出貨路徑 |

⇒ **WP-B 在 Traversal 會話的 plan-validity 工作有結論之前不開工**；WP-A 可以先做。

---

### L.2 M-05：Step-down assist（Integrated，尚未 Stable）

#### L.2.1 失地計時器 `_stepDownTimeSinceGrounded` 的生命週期

計時器只在 `ApplyStepDownAssist:612-613` 推進（grounded ⇒ 0，否則 `+= deltaTime`）；`ApplyStepDownAssist` 只被 Base 與 Stop 兩個入口在 `enableStepDownAssist` 時呼叫（兩個出貨 prefab 都開）。

| 模式 | 實際入口 | 計時器 | 貼地介入 | 備註 |
|---|---|---|---|---|
| Normal（LocomotionModel／BaseState） | `ExecuteBaseMovement` | ✓ 推進 | ✓ | 基準 |
| Stop（停步曲線播放中） | `ExecuteLocomotionStopMovement` | ✓ | ✓ | `docs/44` §9.4 |
| Pending Stop／`HoldsEntryMotion` | `ExecuteBaseMovement` | ✓ | ✓ | |
| Jump（前搖、空中、Normal Land／Land2Move） | `ExecuteBaseMovement` | ✓ | gate ③ 擋上升段；下降時已超出 0.08 s | R6 由 `Player_JumpFromGeneratedStair_*` 守 |
| Jump HardRecovery | `ExecuteVerticalOnlyMovement` | **凍結** | ✗ | 落地後才進入 ⇒ 凍結值為 0，無害 |
| Roll（播放中） | `ExecuteBakedCurveMovement`（3-arg） | **凍結** | ✗（已裁決） | 🟠 **過期風險** |
| Roll（clip 未播的防呆） | `ExecuteBaseMovement` | ✓ | ✓ | 只有 1–2 帧 |
| Action 有 bake（Melee_Slash1、Throw×4、Enemy_Punch_R） | `ExecuteBakedCurveMovement`（3-arg） | **凍結** | ✗（已裁決） | 🟠 **過期風險** |
| Action 無 bake（Fireball×3、Ice） | `ExecuteBaseMovement` | ✓ | **✓** | 📄 `docs/44` §9.4 寫「Action 不貼地」——無 bake 的施法其實是 locomotion 延續，**會**貼地（合理，但文件沒寫） |
| Hurt／Death | `ExecuteVerticalOnlyMovement` | **凍結** | ✗ | 水平為 0 ⇒ 之後 gate ② 也擋；低風險 |
| Traversal（全部結局） | Warped／Committed | **凍結** | ✗ | 🟠 **過期風險**（EP／FL 以未觸地離開時） |
| `Teleport`（重生） | — | **不重設** | — | 重生後意圖為 0 ⇒ gate ② 擋；低風險 |
| 暫停（`IsTimeFrozen`） | 入口提早 return | 不推進 | — | dt=0，無害 |

#### L.2.2 模式切換的過期狀態與向下吸附（🟠 推導）

**機制**：進入「凍結」模式時，計時器停在最後一個 Base／Stop 帧的值——通常是 **0**（多半從著地的 locomotion 進入）。若該模式在**騰空**時結束、回到 Base：
1. 第一個 Base 帧：`lostSupportAge = 0 + dt ≤ 0.08` ⇒ gate ① 判「剛失地」，雖然實際已騰空了整段 Roll／攻擊／traversal 的時間。
2. gate ② 有水平速度（玩家按著方向）、gate ③ 下降中（騰空期間重力已積分成負值）⇒ 成立。
3. 本帧水平落點下方 0.25 m 內若有 `CharacterMovementSurface` 且坡度合格 ⇒ 本帧垂直速度被設成 `−distance/dt` ⇒ **一帧內往下吸最多 0.25 m**。
4. 第一帧若沒查到，計時器從過期值繼續累加 ⇒ 接下來最多 0.08 s 每帧都會查 ⇒ 等於「結束後 0.08 s 的磁吸窗」。

**範圍**：遮罩只看移動面（產生式樓梯）⇒ 只在「樓梯上做 Roll／有 bake 的攻擊／traversal 結束在樓梯上空」時可能出現。Roll 平均 1.94 m/s（4.60 m／2.37 s），在 35° 移動面上需要約 1.36 m/s 的貼地速度，峰值段會超過 `reboundForce` 的 2 m/s ⇒ Roll 下樓**有可能**騰空；Roll 結束時是否仍騰空需要量測（WP-E′）。

**正面對照**：`_groundDivergingElapsed` 經 `SyncGroundedState` 在**每一個**入口推進，並以 `_groundDivergingTickFrame` 防同帧重複累加——同一份程式裡已經有正確的取樣域樣板。

#### L.2.3 修正選項（待裁決 D-3）

| 選項 | 內容 | Normal／Stop／Jump | 凍結模式結束時騰空 |
|---|---|---|---|
| **(a)（建議）** 實時語意 | 計時器移到 `SyncGroundedState` 的取樣域（每個入口推進，帧守衛） | **逐位元不變**（那些帧本來就每帧呼叫 assist，`data.IsGrounded` 是同一個值） | 不再吸附：騰空超過 0.08 s 就照彈道落下 |
| (b) 失效語意 | 進入任何不呼叫 assist 的入口時，把計時器設為「已過期」 | 不變 | 不吸附；但語意變成「committed motion 會花掉重試預算」，而不是「失地多久」 |
| (c) 維持現狀 | 只把現象寫進規格 | 不變 | 維持磁吸窗 |

`docs/42` R7 的原文是「失地後的重試資格必須用**連續失地時間**判斷」——字面語意是 (a)。⛔ 三個選項都**不合併**時間窗（2026-10-07 裁決）。

#### L.2.4 M-05 工作包

| WP | 內容 | 分級 | 行為變更 | 回歸測試 | 環境 |
|---|---|---|---|---|---|
| **WP-D** 特性測試（先寫，舊碼全綠） | ①把三個 gate 與計時推進抽成 `internal static` 純函數（**不改行為**；比照 `TrackGroundStateDivergence`）②`docs/42` §8.4 的 EditMode 測試：`Gate_RejectsWhenAscending`、`Gate_RejectsWhenNoHorizontalMotion`、`RetryWindow_Accepts／RejectsAtBoundary`、`RetryWindow_ResetsWhenGrounded`、`WalkableGate_*`③PlayMode 合成宿主（`CharacterController`＋MotionDriver＋layer 3 合成斜面，反射設 `enableStepDownAssist`／`stepDownQueryLayers`）：`SD2_WithinWindow_SnapsToMovementSurface`（陽性對照）、`SD3_JumpLaunch_NotClobbered`（R6）、`SD4_StationaryAtEdge_NoIntervention`（`docs/38`）、`SD5_FlatGround_NoOp`（R8）、`SD6_NonMovementLayer_NoOp`（遮罩） | 🧹＋測試缺口 | 無（抽出的純函數必須逐位元等價） | 本身即測試；`StairTraversalPlayModeTests` 的 step-down 案例（`Player_TraversesGeneratedStair` 各組合、`RunDown_StopMidStair`、`JumpFromGeneratedStair`、`WalkOffOpenTopEdge`、`Enemy_TraversesGeneratedStair`）必須維持綠 | 純函數：**Offline**＋UTR-Edit；合成 PlayMode：UTR-Play |
| **WP-E** 計時器取樣域（D-3 裁決後） | 依 D-3 修改；與 `SD1` 放同一包 | 🟠 | 依 D-3：(a) 只有「凍結模式結束時騰空且下方 0.25 m 內有移動面」會改變 | 新增 `SD1_AfterCommittedMotionAirborne_DoesNotSnap`（舊碼紅）；Normal／Stop 逐帧 parity：在合成斜面上錄 Base／Stop 各 120 帧的 `actualDY`，改前改後逐位元相同；WP-D 全部 | UTR-Play |
| **WP-E′** 真實 prefab 量測 | 從產生式樓梯中段 Roll 下樓、在樓梯上做 Melee_Slash1：記錄 `lostSupportAge`、實際騰空時間、`AssistApplied`、`actualDY` | 🟠 → 證實或否證 | 無 | 若證實，升格為 `StairTraversalPlayModeTests` 的案例 | UTR-Play（InputTestFixture；⚠️ memory：同帧送兩個鍵會卡鍵） |
| **WP-F** 收尾清理（WP-D／E 綠之後） | 摘 6 處過期 PROTOTYPE 標記（D.1）；`LogStepDownAssistTrace` 註解改為「正式診斷」並**移除** FootIK 欄位；`stepDownRetryWindow` tooltip 改為「機制已定、數值未經 tuning pass」；新增架構測試「MotionDriver 不得持有 `Presentation.IK` 型別的**欄位**」（`docs/42` §8.3 的建議；debug 區塊內的區域讀取見 L.3） | 🧹＋📄 | 無 | 架構測試（Offline）；`StairAuthoringContractTests:182-185`（不變） | Offline（編譯＋字串測試）＋UTR-Edit |
| **WP-F′** 文件 | `docs/42` 檔頭：Integrated（`docs/44` §9.4），尚非 Stable；`docs/44` §9.4 補「無 bake 的 Action 走 locomotion、會貼地」；`docs/00-map.md` 第 47 列改述（⚠️ 與其他會話重疊） | 📄 | 無 | — | — |

**Stable 條件**（建議寫進 `docs/42` 檔頭；全部成立才標 Stable）：①WP-D／E 綠；②`docs/42` §8.2 的 tuning pass 完成或明確豁免（`maxStepDownDistance` 只驗過 0.195 m 級高；`stepDownRetryWindow` 沒有掃過 Walk／Run）；③人工 Play：樓梯上的 Roll、攻擊、停步、跳躍；④沒有未結的過期狀態項。

---

### L.3 Step-Up Diagnostics：盤點與建議（⛔ 本輪不刪）

| 元件 | 位置 | 依賴 | 可重用？ | 建議 |
|---|---|---|---|---|
| `enableStepUpDiagnosticsPrototype`（序列化，兩個 prefab 都是 0） | `MotionDriver.cs:205-212` | — | 開關本身 | **保留但改名**為通用的 Move trace 開關（`FormerlySerializedAs` 保住值；兩個 prefab 都是 0，無資產風險） |
| 接住 `Move()` 回傳的 `CollisionFlags`、`posBeforeMove`、`requestedMovementThisFrame` | `:563-583`（`#if`） | — | **是**：「要求位移 vs 實際位移 vs 碰撞旗標」對任何 Move 相關問題都有用（`docs/43` 的 `stepOffset` 推擠就是這樣量到的） | **保留**為核心診斷 |
| `LogStepUpDiagnosticsTrace` 的 root 欄位（`requestedDY`／`actualDY`／`stepOffsetDelta`／`isGrounded`／`collisionFlags`） | `:751-800` | — | 是 | **保留** |
| 同一個 log 的 IK 欄位（`pelvisY`、左右腳權重、`HasGround`） | `:760-774` | **`FootIKController`**（`_debugFootIKController`） | 否：為 `docs/43` 一次性的相關性分析而加；IK 有自己的 runtime debug 線，PlayMode 探針（`PlantSettleProbe` 等）直接讀 FootIK | **刪除**（與 WP-F 一起） |
| `_debugFootIKController`／`_debugFootIKControllerResolved` | `:333-337` | `Presentation.IK` | 否 | **刪除**（`docs/42` §8.3 本來就要求） |
| `[StepDownAssist]` log 的 `pelvisY` 欄位 | `:711-717` | 同上 | 否 | **刪除**；其餘欄位保留（`docs/44` 刻意保留的 `logStepDownAssist`） |
| `TraversalHandIKController` 讀值 | `:1998-2005`（traversal debug 面板） | `Presentation.IK`（`GetComponent`，不是欄位） | 是（面板要顯示 Hand IK 狀態） | **保留**；WP-F 的架構測試只禁**欄位**，不禁 debug 區塊內的區域讀取 |
| 消費端 | 無：全 repo 沒有任何腳本或測試解析這兩種 log，只靠人讀 Console | — | — | 刪欄位不會弄壞任何工具 |
| 文件 | `docs/43`（:43、:269、:856、:907）、`docs/00-map.md:48` | — | — | 改名時加一行註記；研究內容不改 |

**待裁決 D-4**：兩個 log 要**合併**成一個 Move trace（assist 開啟時附加 gate 欄位），還是維持兩個開關？建議合併——兩者回答的都是「這一帧的 Move 發生了什麼」。

---

### L.4 M-09：`ApplyBakedCompensation` 刪除規劃

**依賴確認**（全 repo grep，排除 `Library`／`Temp`／`.claude/worktrees` 等過期副本）：
- 程式：**零呼叫者**，只有定義本身（`MotionDriver.cs:2630`）。
- 測試：**零引用**（含反射字串）。
- 資料：`docs/03` 說它要消費 `WarpedMotionExtractor` 的輸出——該 extractor **只有規格、沒有實作**（`Assets/Scripts` 裡沒有這個類別）⇒ 刪除不會讓任何 bake 欄位變成孤兒。
- 文件：`docs/02` §MotionDriver（過期偽代碼）、`docs/03:74` roadmap、`docs/14:232` A28 允許清單、`docs/17:452`、`docs/18:260`、`docs/22:133/574`（歷史表格）、`WORKLOG.md:7956`、`_MentorQuiz`（學習紀錄）。

| WP | 內容 | 分級 | 行為變更 | 回歸測試 | 環境 |
|---|---|---|---|---|---|
| **WP-H** | 刪除方法；`docs/02` MotionDriver 節的偽代碼與 🛑 `OnAnimatorMove` 風險說明改為指向 WP-I1 的入口矩陣；`docs/03` roadmap 的 Motion Warping 改以 `TraversalWarpPlan`（commit 時建 plan、執行期只消費）為先例；`docs/14` A28 移除這個名稱；`docs/17`／`docs/18`／`docs/22` 是研究紀錄，**只加一行「已刪除」註記，或不改** | 🧹＋📄 | 無 | 新增架構測試「MotionDriver 的公開位移入口都必須有 runtime 呼叫者，例外需列入 allowlist 並附理由」（Offline 字串掃描）——⚠️ 4-arg `ExecuteBakedCurveMovement` 也是零呼叫者 ⇒ 必須同時裁決 D-5 | Offline（`dotnet build`＋字串測試） |

---

### L.5 M-01／M-03：入口政策矩陣與 SpeedCurve 共用語意規格

**原則**：先讓規則**存在並被測試守住**，⛔ 不改數值邏輯。§C M-03 的離線算術已顯示積分差 ≤ 6 mm——**沒有數值上的修正理由**。

| WP | 內容 | 分級 | 行為變更 | 回歸測試 | 環境 |
|---|---|---|---|---|---|
| **WP-I1** 規格（**最先做**，作為 WP-B／E 的共同詞彙） | 新的子系統規格檔（依 CLAUDE.md 分卷規則，取代 `docs/02` MotionDriver 節的過期偽代碼；檔號待定，⚠️ 平行會話可能搶號）：①入口政策矩陣（§C M-01 的表，**每一格附理由與出處**；刻意的差異標「保留」）②接觸事件契約 C-E1～C-E4（L.1.3）③SpeedCurve 語意：key 0 是後向差分的邊界佔位值 (0,0)；「clip 時鐘」＝播放頭差（含 transition `_Speed`）；各 consumer 用哪一種積分與容差（預測 vs 執行 ≤ 1 cm）；3-arg 入口的前提（playback rate = 1） | 📄 | 無 | — | — |
| **WP-I2** 守門測試（WP-B／E／H 之後） | ①架構測試（字串）：每個 `Execute*` 都經 `SyncGroundedState`（直接或經 `GetGravityThisFrame`）；traversal 入口不呼叫 `UpdateCapsuleOffset`；只有 Base／Stop 呼叫 `ApplyStepDownAssist`；修正 `WriterRules` 過期的 `Owner` 字串（`:521-530` → `SyncGroundedState`）②資產測試：走 3-arg 路徑的 transition（`Roll`、`Melee_Slash1`、`Throw_*`、`Enemy_Punch_R`）`_Speed == 1`（把隱含前提變成會紅的測試）③資產測試：所有出貨 bake 的 `SpeedCurve[0] == (0,0)`（Stop 兩端無條件略過 key 0 所依賴的前提）④PlayMode：每支出貨 Stop bake 在合成平地上實際執行 `ExecuteLocomotionStopMovement` 到結束，root 位移與 `GetLocomotionStopHorizontalDisplacementAt(Duration)` 差 ≤ 1 cm（**引擎內**驗證離線估計，取代待量測 T6） | 📄＋🟠（隱含前提） | 無 | 本身即測試 | ①Offline；②③UTR-Edit；④UTR-Play |
| **（不做）** 積分收斂（§G G-4） | 等「3-arg 路徑真的需要 playback rate ≠ 1」或「烘焙格式改變」時再做；WP-I2 ②③ 會在那一刻變紅提醒 | — | — | — | — |

---

### L.6 修正順序

| 順序 | WP | 依賴 | 需要裁決 | 需要 Editor？ |
|---|---|---|---|---|
| 1 | **WP-A** C0 修復（test-only） | — | 無 | UTR-Play |
| 2 | **WP-I1** 規格（入口矩陣／C-E1～4／SpeedCurve 語意） | — | 無（純文件） | 否 |
| 3 | **WP-D** step-down 特性測試＋純函數抽取 | — | 無 | Offline＋UTR |
| 4 | **WP-E′** 真實 prefab 量測 | WP-D | 無 | UTR-Play |
| 5 | **WP-E** 計時器取樣域 | WP-D、D-3 | **D-3** | UTR-Play |
| 6 | **WP-B** execution-active 表示（或縮小為 invariant 測試，見 L.1.6） | WP-A、**Traversal 會話 plan-validity 工作的結論**、D-1、D-2 | **D-1、D-2**（FL 不可達時撤回） | UTR-Play |
| 7 | **WP-B′** FL 整合測試（FL 不可達時取消） | WP-B、Traversal 會話閒置 | — | UTR-Play |
| 8 | **WP-C** EP／FL post-exit 量測 | WP-B | 修不修＝**D-6** | UTR-Play（修了要 Human） |
| 9 | **WP-F／F′** step-down 收尾＋文件（Integrated） | WP-D、WP-E | — | Offline＋UTR-Edit |
| 10 | **WP-G** Step-Up Diagnostics 整併 | WP-F、D-4 | **D-4** | Offline |
| 11 | **WP-H** 刪 `ApplyBakedCompensation`＋文件 | D-5 | **D-5** | Offline |
| 12 | **WP-I2** 守門測試 | WP-B、E、H | — | Offline＋UTR |

**為什麼這個順序**：WP-A 先恢復唯一守 M-02 契約的測試，之後所有 M-02 修正才有回歸保護；WP-D 在 WP-E 之前，確保修計時器時 Normal／Stop 的 parity 有基準；WP-B 排在 WP-E 之後，因為兩者都改 `SyncGroundedState` 附近——分開 commit、分開驗證，出錯時可以單獨 revert。

**commit 粒度**：每個 WP 一顆 commit；WP-E 與 WP-B 的行為變更**不得**與清理（WP-F／G／H）混在同一顆。

---

### L.7 可能的行為變更總表

| WP | 變更 | 誰會注意到 | 回退 |
|---|---|---|---|
| WP-A、D、F、G、H、I | **無** | — | — |
| WP-E（D-3 = a） | 樓梯上 Roll／有 bake 的攻擊／traversal 結束時若騰空，不再被吸到移動面，改為彈道落下 | 只在產生式樓梯上；可能出現原本沒有的短暫 Falling／落地聲 | revert 該 commit（計時器回到 assist 取樣域） |
| WP-B（FL 結局） | 不再出假的離地／落地事件（音效）；（D-1）不再被方向鍵轉向；（D-2）膠囊不再前偏 | 只在 Failed traversal（例：牆後無地面的 Vault） | revert 該 commit；`HasActiveTraversalPlan` 語意不變，Hand IK 不受影響 |
| WP-C（若 D-6 要修） | Climb1m（出貨路徑）結尾開始貼地 ⇒ Foot IK 提早接手、離開時不再有落地聲 | 所有 Climb1m | 需要人工 Play；revert 一處條件 |

---

### L.8 需要使用者裁決

| # | 問題 | 建議 | 為什麼需要你 |
|---|---|---|---|
| **D-0** | Traversal 會話的「沒有有效 plan 就不承諾」（L.1.6）會落地嗎？ | 若落地，D-1／D-2 撤回、WP-B 縮小為 invariant 測試 | 決定 FL 是正式狀態還是不可達；這是 Traversal 的准入語意，不是 MotionDriver 能決定的 |
| **D-1** | Failed traversal 期間，facing request 要不要丟棄（與 plan 有效時一致）？ | 丟棄（executor 層一致；不動 ADR-007 的 facing source） | 另一種做法是讓 `CharacterFacingSource` 在 traversal 期間不送 request——那會動到 ADR-007（Trial）的優先序 |
| **D-2** | Failed traversal 期間，膠囊 pose offset 要歸零（與 plan 有效時一致）還是繼續求解？ | 歸零 | 攀爬姿勢是否在 V2 pose solver 的適用範圍內，沒有量測過 |
| **D-3** | 失地計時器採 (a) 實時、(b) 失效、(c) 維持？ | (a) | 會改變樓梯上「Roll／攻擊結束時騰空」的落地手感 |
| **D-4** | 兩個診斷 log 合併成一個 Move trace，還是維持兩個開關？ | 合併 | 開發工具的使用習慣 |
| **D-5** | 刪 `ApplyBakedCompensation` 時，4-arg `ExecuteBakedCurveMovement`（同樣零呼叫者）要刪，還是保留為「3-arg 的正確替代」並列入 allowlist？ | 保留＋allowlist（Roll／Action 未來需要 playback rate 時的現成入口） | API 保留策略 |
| **D-6** | WP-C 量測若證實 Climb1m 結尾整段未觸地，要不要給 EP 加 post-exit 貼地？ | 量測後再決定 | 會改變出貨 Climb1m 的觀感；也是 `docs/52` T-04 的一個軸，與 Traversal 會話的 J3′ 相鄰 |
| — | `docs/00-map.md` 目前沒有 `docs/50` 的索引列（也要更新 `docs/42`、`docs/43` 兩列）：由哪個會話收尾？ | 三個審查會話都閒置後，由一個會話一次補 | 三個會話都會改這個檔 |
