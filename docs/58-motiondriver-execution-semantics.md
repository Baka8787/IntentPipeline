# 58 — MotionDriver 執行語意規格：入口政策矩陣／接觸事件契約／SpeedCurve 語意（WP-I1）

| | |
|---|---|
| **狀態** | 🟠 **可審查草案**（2026-10-14）。描述**現況**；已裁決但尚未落地的目標語意一律標 🎯，⛔ 不得把 🎯 當成現況引用 |
| **層級** | Living Spec（MotionDriver 子系統的 Dev Spec tier，依 CLAUDE.md 分卷規則）。**不是 ADR**：本檔不改任何契約，只把散在入口方法裡的政策寫成表 |
| **來源** | `docs/50` §C M-01／M-03、§L.1.3（C-E1～C-E4）、§L.5（WP-I1 範圍）、§L.9（2026-10-14 裁決 D-3／D-4／D-5） |
| **待回填** | §4.1 的 **producer 側**（誰寫入 SpeedCurve key 0、有無測試守）由烘焙管線審查 `docs/59`（research-only，進行中）回填 |
| **取代** | `docs/02` §3「#### MotionDriver（根運動與補償驅動）」的過期偽代碼（該節描述的 `OnAnimatorMove`／`_rootMotionDelta` 路徑已不存在）。本草案通過後，`docs/02` 該節改為摘要＋連結的 stub（與 WP-H 同包，見 §6） |
| **證據** | 讀碼，2026-10-14。`MotionDriver.cs` 自 `a1bafb2`（2026-10-06）未變，`docs/50` 引用的行號仍有效。⚠️ 未跑測試（Editor 無回應） |

**讀法**：§1 是「每個入口在本帧對七個子政策的答案」；§2 是兩個無 gameplay 呼叫者的入口為什麼保留、誰可以呼叫；§3 是接觸事件契約；§4 是 SpeedCurve 的共用語意；§5 是 step-down 失地時間的目標語意；§6 是守門測試計畫。

---

## 0. 共通規則（所有入口）

| # | 規則 | 出處／理由 |
|---|---|---|
| G0 | **唯一位移出口**：位置只經 `CharacterController.Move`（`Teleport` 是唯一瞬移入口）；旋轉只在 MotionDriver 內寫 | A20、A48 |
| G1 | **時間凍結**：每個入口第一行 `if (IsTimeFrozen) return;`（`Time.deltaTime <= 0`） | 零位移的 `Move` 會把 `isGrounded` 弄成 false（`IsTimeFrozen` 註解） |
| G2 | **grounded 讀的是上一次 `Move` 的接觸結果**：所有入口都在**本帧 `Move` 之前**呼叫 `SyncGroundedState`（直接或經 `GetGravityThisFrame`）⇒ 本帧發布的 `IsGrounded` 反映上一次 `Move` | `SyncGroundedState` 的 summary |
| G3 | **重力一帧只積分一次**：`GetGravityThisFrame` 以 `Time.frameCount` 快取；同帧多次呼叫回傳同一值 | `_gravityFrame` |
| G4 | **黑板接觸欄位單一寫入者**：`IsGrounded`／`VerticalVelocity`／`JustLanded`／`JustLeftGround` 只在 `SyncGroundedState` 寫 | A5 `WriterRules`（⚠️ 其 Owner 字串仍寫 `GetGravityThisFrame`，已過期——WP-I2 修正） |
| G5 | MotionDriver **不決定**運動模式：模式由呼叫端（State／Model）選擇入口決定；MotionDriver 只執行該入口的政策 | `docs/50` M-01；⛔ 本檔**不**引入 `MovementMode` 表示（沒有證據支持重構，L.0） |

---

## 1. 入口政策矩陣

### 1.1 七個子政策

| 子政策 | 問題 |
|---|---|
| **Facing** | 本帧是否消費 `CharacterFacingSource` 送來的 facing request（`ApplyFacingRequest`） |
| **Capsule offset** | 本帧是否跑姿勢驅動的膠囊偏移（`UpdateCapsuleOffset`；traversal collision profile 作用中時 Guard ① 讓路） |
| **水平** | 本帧水平位移從哪裡來 |
| **垂直** | 本帧垂直位移從哪裡來 |
| **grounded 同步** | 經哪條路徑呼叫 `SyncGroundedState` |
| **貼地** | step-down assist（只看移動面）或 traversal exit rebound |
| **`VerticalVelocity` 發布** | 發布到黑板的是本帧還是上一帧的垂直速度 |

### 1.2 矩陣（現況）

標記：**保留**＝刻意差異，有理由與出處；**已知**＝沒有設計理由、但目前不可達或無 consumer，記錄而不修；**刪除**＝已裁決刪除、待 WP-H。

| 入口 | 呼叫者（gameplay） | Facing | Capsule offset | 水平 | 垂直 | grounded 同步 | 貼地 | `VerticalVelocity` |
|---|---|---|---|---|---|---|---|---|
| `ExecuteBaseMovement`（`:528`） | `BaseState`、`JumpState`、`LocomotionModel`、`RollState`／`ActionState` 的「clip 未播／無 bake」防呆 | ✓ | ✓ | `MoveDirection × MoveSpeed × moveSpeed` | 重力積分 | `GetGravityThisFrame` | ✓ step-down（`enableStepDownAssist`） | 上一帧 |
| `ExecuteVerticalOnlyMovement`（`:804`） | `HurtState`、`DeathState`、`JumpState` HardRecovery | ✓ | ✓ | 0 | 重力積分 | `GetGravityThisFrame` | ✗ **保留**（R1：水平為 0，不需要貼地） | 上一帧 |
| `ExecuteBakedCurveMovement` 3-arg（`:823`） | `RollState`、`ActionState`（有 bake 且 clip 在播） | ✓ | ✓ **保留**（V2 讀骨架，committed motion 照常求解；`:830` 註解） | SpeedCurve(t) **瞬時** × dt（§4.3 a） | 重力積分 | `GetGravityThisFrame` | ✗ **保留**（`docs/44` §9.4：authored motion 不被貼地改寫） | 上一帧 |
| `ExecuteBakedCurveMovement` 4-arg（`:842`） | **無**（§2.1） | ✓ | ✓ | 播放頭區間單段梯形（§4.3 b） | 重力積分 | `GetGravityThisFrame` | ✗ | 上一帧 |
| `ExecuteLocomotionStopMovement`（`:866`） | `LocomotionModel`（停步 clip 在播） | ✓ | ✓ | 同 4-arg ＋ **略過 key 0 佔位**（§4.1） | 重力積分 | `GetGravityThisFrame` | ✓ step-down **保留**（停步是 locomotion 尾段，`docs/44` §9.4） | 上一帧 |
| `ExecuteCommittedCurveMovement`（`:1367`） | `TraversalState.ExecuteCommittedMotion` 的 **plan 無效防禦分支**（§2.2） | ✓ **已知** | ✗ **已知**（偏移停在進場前那一帧） | 播放頭單段梯形，**不**略過 key 0 | `VerticalCurve` 前後差（不積分重力） | 直接 `SyncGroundedState` | ✗ | **本帧**（先算後同步）**已知** |
| `ExecuteTraversalWarpedMovement`（`:1559`） | `TraversalState`（plan 有效；所有可達的 traversal） | ✗ **保留**（traversal 擁有 yaw：plan pose 差） | ✗ **保留**（collision profile 擁有膠囊；Guard ①） | plan 絕對 pose 前後差 | plan pose 差 ＋ exit rebound | 直接 `SyncGroundedState` | exit rebound **保留**（僅 `plan.IsPiecewise` 且過 exit knot；2026-09-13 playtest，`:1603` 註解）；endpoint 無 rebound ⇒ `docs/50` WP-C 量測中 | 上一帧（先同步後算）**已知**，由 `TraversalStatePlayModeTests` T7 以 characterization 鎖住 |
| `ApplyBakedCompensation`（`:2630`） | **無** | ✗ | ✗ | 瞬時 ＋ 每帧線性補償 | 重力積分 | `GetGravityThisFrame` | ✗ | 上一帧 |
| ↳ 處置 | **刪除**（D-5，WP-H）。Motion Warping 的先例是 `TraversalWarpPlan`（commit 時建 plan、執行期只消費、絕對取樣），⛔ 不得再「兌現」這個 seam | | | | | | | |

### 1.3 每個「保留」格的理由彙整

| 格 | 理由 | 出處 |
|---|---|---|
| 只有 Base／Stop 貼地 | step-down 只修「locomotion 在移動面上走下一階」；VerticalOnly 沒水平速度，Roll／Action 的 authored 位移不得被改寫 | `docs/42` R1、`docs/44` §9.4 |
| Base 系發布**上一帧** `VerticalVelocity` | 必須在 reboundForce 貼地夾持**之前**發布，否則落地帧的 impact velocity 被銷毀、所有落地都被分成 Normal | `SyncGroundedState` 末段註解；JumpState 落地分類 |
| Traversal warped 不消費 facing、不跑 capsule offset | traversal 的 yaw 與膠囊由 committed plan／collision profile 擁有；有兩個 owner 會互相覆寫 | ADR-008 D4／D11；`UpdateCapsuleOffset` Guard ① |
| Traversal 直接呼叫 `SyncGroundedState` 而非 `GetGravityThisFrame` | committed 垂直語意不積分重力，但接觸快照仍須發布 | `GetGravityThisFrame` summary |
| Piecewise exit rebound | exit knot 後 plan 位置凍結、`Move(0)` 不回報觸地 ⇒ Foot IK 全程關、離開時落地聲；改為真的貼上已驗證落腳面 | `:1603` 註解（2026-09-13 playtest） |
| 只有 Stop 略過 SpeedCurve key 0 | Stop 從 t=0 起播，首段梯形積到佔位值會半速（`docs/07` R8）；其他呼叫者不從 0 開始、或差 ≤ 6 mm | §4.1、`docs/50` M-03 |

### 1.4 「已知」格：為什麼不修、何時要重開

| 格 | 現況 | 為什麼不修 | 重開條件 |
|---|---|---|---|
| `ExecuteCommittedCurveMovement` 消費 facing、膠囊偏移凍結、不抑制接觸事件 | 原 `docs/50` M-02 的三個分岔 | `8a124dd` 後 plan 無效**不會被承諾**（ADR-008 D4／D5 Plan Validity），本入口在 gameplay 不可達；D-1／D-2 因此撤回 | 任何讓 plan 無效的 traversal 重新可被承諾的改動 ⇒ 重開 D-1／D-2 |
| 兩條 traversal 路徑 `VerticalVelocity` 發布時機不同 | Committed＝本帧；Warped＝上一帧 | traversal 期間沒有 consumer 讀 `VerticalVelocity`（JumpState 不在 traversal 中） | 出現 traversal 期間讀 `VerticalVelocity` 的 consumer |

---

## 2. 保留入口的 architecture contract（D-5，2026-10-14 裁決）

> 裁決：4-arg `ExecuteBakedCurveMovement` 與 `ExecuteCommittedCurveMovement` **保留並列入允許清單**；`ApplyBakedCompensation` 刪除。允許清單的每一項必須對應本節的 contract，⛔ 不得只寫「未來可能使用」。

### 2.1 `ExecuteBakedCurveMovement(bake, normalizedTime, previousNormalizedTime, data)`（4-arg）

| | |
|---|---|
| **語意** | committed baked motion 的**播放頭驅動**版本：本帧水平位移＝bake 在 `[previous, current]` 播放頭區間內的位移（單段梯形），yaw＝同區間的 `RotationCurve` 差。播放倍率（TransitionAsset `_Speed`、Animation Fitting 等）**自然包含**在播放頭差內 |
| **與 3-arg 的分界** | 3-arg 以 `previous = current − Time.deltaTime` 推算區間，**前提是 playback rate ＝ 1**（§4.2）。⇒ **任何 playback rate ≠ 1 的 committed baked motion 必須改用 4-arg**；這是保留它的唯一理由，也是它的合法使用條件 |
| **合法呼叫者** | 執行 committed baked clip 的 FullBody State（目前是 `RollState`、`ActionState` 的類別），且能從 `AnimationFacade` 取得**同一個播放實例**的本帧與前一帧 normalized time |
| **前提** | ①`previousNormalizedTime` 必須是同一播放實例的前一帧值（換 clip／重播時由呼叫端歸零）②呼叫端自己負責「clip 未播」的防呆（比照 `ActionState` 退回 `ExecuteBaseMovement`）③不略過 SpeedCurve key 0——若呼叫者從 t=0 起播且首段速度重要，必須先依 §4.1 決定略過規則，⛔ 不得在呼叫端自行補 `minSpeedSampleTime` 以外的修正 |
| **子政策** | 與 3-arg 相同（facing ✓、capsule ✓、重力積分、不貼地、上一帧 `VerticalVelocity`）——兩者只差「區間怎麼量」 |
| **⛔ 不可** | 作為 locomotion 停步入口（停步走 `ExecuteLocomotionStopMovement`，它多了 key 0 略過與 step-down）；作為 traversal 入口 |
| **守門** | WP-I2 ②：走 3-arg 的 transition（`Roll`、`Melee_Slash1`、`Throw_*`、`Enemy_Punch_R`）`_Speed == 1` 的資產測試——它紅的那一刻，就是該呼叫者必須遷到 4-arg 的時刻 |

### 2.2 `ExecuteCommittedCurveMovement(bake, normalizedTime, previousNormalizedTime, data)`

| | |
|---|---|
| **語意** | committed traversal 的**防禦分支**：plan 無效時，仍以 bake 的 3D 曲線（水平播放頭梯形＋`VerticalCurve` 前後差）執行，**不積分重力** |
| **為什麼保留** | `TraversalState` 的明文語意：「bake 有問題時仍不退回普通重力路徑」——committed traversal 一旦開始，垂直語意屬於 traversal，退回 `ExecuteBaseMovement`／`ExecuteVerticalOnlyMovement` 會讓角色在牆前掉落。`ArchitectureRegressionTests:1399-1405` 以字串鎖住「`TraversalState` 引用本方法、不引用那兩個重力入口」 |
| **合法呼叫者** | **只有** `TraversalState.ExecuteCommittedMotion` 的 `!_warpPlan.IsValid` 分支；另有 3 個 PlayMode 測試直接呼叫（`MotionDriverCommittedMovementPlayModeTests`）作為執行語意的合成宿主 |
| **前提／不變式** | `8a124dd` 後 `TraversalState.CanEnter` 在建不出有效 plan 時**不承諾**（ADR-008 D4／D5）⇒ gameplay 走到這裡代表 Plan Validity 不變式被破壞。WP-B（縮小版）把「`TraversalState` 只以有效 plan 執行 committed motion」寫成 invariant 測試 |
| **子政策** | 見 §1.2：facing ✓、capsule ✗、本帧 `VerticalVelocity`、不抑制接觸事件——全部是 §1.4 的「已知」格，**只在不可達的前提下被接受** |
| **bake 無效時** | `bakeData == null`、非有限值、`Duration <= 0` ⇒ `_verticalVelocity = 0`、同步 grounded、不移動（不退回重力） |
| **⛔ 不可** | 作為 Roll／Action 的入口；作為 traversal 的正常執行路徑 |

### 2.3 允許清單的形狀（WP-I2 實作）

架構測試：「MotionDriver 的公開位移入口都必須有 runtime 呼叫者；例外列入 allowlist」。allowlist 每一項必須帶**指向本檔 §2.x 的 contract 引用**，測試以字串檢查該引用存在。目前只有兩項：

| 入口 | contract |
|---|---|
| `ExecuteBakedCurveMovement`（4-arg） | `docs/58` §2.1 |
| `ExecuteCommittedCurveMovement` | `docs/58` §2.2 |

---

## 3. 接觸事件契約（C-E0～C-E4）

| # | 條文 | 現況 |
|---|---|---|
| **C-E0** | `JustLanded`／`JustLeftGround` 是**確認後**的邊沿：本帧原始 `isGrounded` 與「上次已確認回報值」`_wasGrounded` 的分歧，需在 Motion tick domain 累積滿 `groundEdgeEventGrace`（出貨 0.1 s）才回報；同一 Unity frame 的重複同步不重複累加（`_groundDivergingTickFrame`） | ✓（`TrackGroundStateDivergence`／`ShouldConfirmGroundStateChange`） |
| **C-E1** | **確認時刻**落在 traversal 執行期間的接觸變化**不回報**（落地與離地皆然）；`_wasGrounded` **照常前進**（不回報 ≠ 沒發生） | ✓。抑制鍵是 `HasActiveTraversalPlan`；所有可達結局下它與「traversal 執行中」逐帧相同（§1.4） |
| **C-E2** | traversal 結束後，相對於已前進的 `_wasGrounded` 的下一次分歧**照常回報**——不補發、不吞掉 | ✓ |
| **C-E3** | `IsGrounded` **永不抑制**、不加任何否決層 | ✓（`docs/38` §13） |
| **C-E4** | grace 只延遲確認，不改變 C-E1／C-E2 的判斷時刻——判斷時刻＝**確認時刻** | ✓ |

**守門**：`MotionDriverCommittedMovementPlayModeTests.C0_*` 是唯一守 C-E1 的測試，**自 2026-09-23 加入 grace 後陽性對照失效**（測試缺陷，非 runtime 缺陷；`docs/50` §L.1.3）。WP-A 改寫 C0 並新增 `C0b`（跨 grace 仍抑制）、`C0c`（C-E2）。⚠️ C0 目前也是測試調度器的已知失敗 K-2（`docs/57` §14.1）；WP-A 修綠後要通知那條線撤掉 K-2。

---

## 4. SpeedCurve 語意

### 4.1 key 0 是邊界佔位值

- **定義**：`MotionBakeData.SpeedCurve` 第 0 個 key 固定為 `(0, 0)`——烘焙器以後向差分取速度，第一個樣本沒有前一個樣本，所以寫入佔位值；它**不是** authored 速度（`docs/07` §8.5 的 SpeedCurve(0⁺) 同源）。第一個實測樣本在 `t = 1 / SampleRate`。
- **consumer 端的處理**（現況，4 處、2 種偵測法）：

| Consumer | 處理 | 偵測法 |
|---|---|---|
| `ExecuteLocomotionStopMovement` | 取速度樣本時略過 `[0, 1/SampleRate)`（`minSpeedSampleTime`） | **無條件**（假設 key 0 恆為佔位） |
| `MotionBakeData.GetLocomotionStopHorizontalDisplacementAt`（停步預測） | 同上 | **無條件** |
| `MotionBakeData.ComputeAverageSpeed`（`AutoAverageSpeed`） | 略過 key 0 | **只有 key0 == (0,0) 時** |
| 其餘（3-arg 瞬時、Committed、`GetHorizontalDisplacementAt`） | 不處理 | — |

- **契約**：key 0 的意義是**烘焙器的輸出格式**，⛔ 不是各 consumer 各自記住的知識。⚠️ 若烘焙器日後改成寫入真實的第 0 個樣本，兩個「無條件」略過會**靜默丟掉一個真實樣本**——WP-I2 ③ 的資產測試（所有出貨 bake `SpeedCurve[0] == (0,0)`）就是為了讓那一刻變紅。
- ⏳ **producer 側（待 `docs/59` 回填）**：哪個 baker／stage 寫入 key 0、是否有其他 stage 改寫 SpeedCurve、現有測試是否已守住格式。

### 4.2 「clip 時鐘」：本帧 clip 時間前進了多少

| 定義 | 使用者 | 前提 |
|---|---|---|
| **播放頭差**（`current − previous`，皆由 normalized time × Duration 換算）——**canonical** | 4-arg、`ExecuteLocomotionStopMovement`、`ExecuteCommittedCurveMovement` | 含 transition `_Speed` 與任何 playback rate 改變 |
| `previous = current − Time.deltaTime` | 3-arg（`RollState`、`ActionState`） | **playback rate ＝ 1**（隱含）。目前所有 3-arg 呼叫者的 transition `_Speed` 都是 1，所以不會錯；rate ≠ 1 時位移與 yaw 不會跟著縮放，且沒有 assert 會發現 ⇒ §2.1 的遷移條件 |

`ExecuteTraversalWarpedMovement` 不用 SpeedCurve：它消費 plan 的絕對 pose，「時鐘」就是 plan 的 normalized time。

### 4.3 「這段走多少」：積分方式與容差

| # | 方式 | 位置 | 用途 |
|---|---|---|---|
| a | 瞬時速度 × `Time.deltaTime` | 3-arg | Roll／Action 執行 |
| b | 播放頭區間**單段**梯形（兩端 `Evaluate` 平均 × Δclip） | 4-arg、Stop、Committed | 執行 |
| c | 逐 key **線性**梯形 | `MotionBakeData.GetHorizontalDisplacementAt` | `TraversalWarpPlan`、Animation Fit（預測） |
| d | c ＋ 略過 key 0 | `MotionBakeData.GetLocomotionStopHorizontalDisplacementAt` | 停步落點預測 |
| e | 算術平均（只在 key0 == (0,0) 時略過） | `MotionBakeData.ComputeAverageSpeed` | `AutoAverageSpeed`（gameplay 滿速分母） |

- **容差契約**：同一段 clip，**預測（c／d）與執行（a／b）的累計水平位移差 ≤ 1 cm**。離線算術（`docs/50` M-03，非引擎量測）：整段停步 ≤ 6 mm、Roll／slash1／Climb1m ≤ 3.2 mm，跨 30／60／144 fps 穩定。
- ⛔ **不為數值收斂而重構**（`docs/50` L.0：沒有證據不重構已正確運作的數值邏輯）。canonical 形狀已有正面範例——`TraversalWarpPlan` 用同一個絕對取樣函式算前後 pose 再相減，預測與執行同源；積分收斂（`docs/50` G-4）等「3-arg 需要 rate ≠ 1」或「烘焙格式改變」時再做，WP-I2 ②③ 會在那一刻變紅。

---

## 5. Step-down 失地時間（D-3 = (a)，🎯 已裁決、未落地）

| | 現況 | 🎯 目標（WP-E） |
|---|---|---|
| 計時器 `_stepDownTimeSinceGrounded` 的取樣域 | 只在 `ApplyStepDownAssist` 推進 ⇒ 只有 Base／Stop 入口；Roll／有 bake 的 Action／Traversal／VerticalOnly 期間**凍結** | 移到 `SyncGroundedState` 的取樣域：**每個入口**推進，沿用 `_groundDivergingTickFrame` 的帧守衛 |
| 語意 | 「Base／Stop 帧累計的失地時間」 | **真實 elapsed 失地時間**（`docs/42` R7「連續失地時間」的字面語意） |
| 可見差異 | 凍結模式在騰空時結束 ⇒ 回到 Base 後約 0.08 s 的磁吸窗，可能往下吸最多 0.25 m（只在產生式樓梯的移動面，🟠 推導，WP-E′ 量測） | 騰空超過 `stepDownRetryWindow` 就照彈道落下 |
| 不變 | Normal／Stop 帧**逐位元不變**（那些帧本來就每帧推進，`data.IsGrounded` 是同一個值）——WP-E 的 parity 驗收 | |
| ⛔ | 不合併任何時間窗（2026-10-07 裁決） | |

---

## 6. 守門計畫（WP-I2，尚未實作）與本檔的落地順序

| 規則 | 守門 | 環境 |
|---|---|---|
| G2／G4：每個 `Execute*` 都經 `SyncGroundedState` | 架構字串測試；同包修 `WriterRules` 過期的 Owner 字串 | Offline |
| 只有 Base／Stop 呼叫 `ApplyStepDownAssist`；traversal 入口不呼叫 `UpdateCapsuleOffset` | 架構字串測試 | Offline |
| §2.3 允許清單 | 架構測試（帶 `docs/58` §2.x 引用） | Offline |
| §2.1／§4.2：3-arg 呼叫者 `_Speed == 1` | 資產測試 | UTR-Edit |
| §4.1：出貨 bake `SpeedCurve[0] == (0,0)` | 資產測試（若 `docs/59` 發現已有守門，改為引用） | UTR-Edit |
| §4.3 容差 | PlayMode：每支出貨 Stop bake 在合成平地實際執行 vs 預測 ≤ 1 cm | UTR-Play |
| §3 C-E0～C-E4 | WP-A（C0／C0b／C0c） | UTR-Play |

**落地順序**（`docs/50` §L.9）：本草案審查 → 回填 `docs/59` 的 producer 事實 → Editor 恢復後 WP-A → WP-D → WP-E′ → WP-E → WP-F／G／H（含 `docs/02` stub）→ WP-I2。

**D-4 的前置確認**（WP-G 開工前）：`[StepDownAssist]` trace 與 Step-Up trace 只有在**同一個 decision point**（本帧 `Move()` 前後的觀測）時才合併；gate 判斷（Move 前）與碰撞旗標（Move 後）若屬不同 phase，按 phase 分組保留。

---

## 7. 修訂紀錄

| 日期 | 內容 |
|---|---|
| 2026-10-14 | 建立可審查草案（WP-I1）：入口矩陣、D-5 兩個保留入口的 contract、C-E0～C-E4、SpeedCurve 語意、D-3 目標語意、守門計畫。§4.1 producer 側待 `docs/59` |
