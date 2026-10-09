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
