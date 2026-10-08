# ADR-010：Locomotion Stop 預測快照（`PlayerRuntimeData.LocomotionStopPrediction`）

| 欄位 | 內容 |
|---|---|
| 狀態 (Status) | 🟡 **Trial**（2026-10-03 使用者裁決：D1 Yes／D2 A／D3 Confirmed；同日 D5 獨立擴充：next planned plant）。AC 見 §5——**使用者已裁決 ≠ 工程上已驗證**。 |
| ⚠️ 背景 | 程式**先於本 ADR 存在**：工作樹裡的原型（Codex，Predictive Planned Stop）已在黑板加欄位、改 WriterRule／A12，並在註解中引用「ADR-010」，但 `docs/ADR/` 從未有過 010。本 ADR 補上決策；正式 commit 只收本 ADR 允許的最小範圍（D2／D3），其餘原型留在工作樹。 |
| 日期 | 2026-10-03 |
| 範圍 | 黑板的 Locomotion Stop 預測快照：Writer（`LocomotionModel`）、資料來源（`MotionBakeData` 的停步結束站姿）、Presentation 端唯一消費者（FutureLandingProvider） |
| 不含 | 骨盆求解器本身（`docs/46` §6–§19，commit `1fc8017`）；Planned Stop 軌跡（D3）；Heel Lift；Run 的停步預測批准 |
| Living Spec | `docs/46` §16–§19（量測與驗收）；Bake 欄位需 fold back 至 Living Docs（docs commit） |
| 關聯 | ADR-003（Movement Intent Layering）、CLAUDE.md「MotionBakeData 是動畫實際運動值的唯一真相」 |

---

## 1. Problem

停步時骨盆要知道「尾腳**最後**會踩在哪裡」，才能在尾腳落地前就決定站多高（`docs/46` §15–§19）：

- **這個答案只有 Core 知道。** 選哪一支停步 clip（左／右腳先、Walk／Run）、從哪個 normalized time 開始播、
  實際播放倍率，全部由 `LocomotionModel`／`LocomotionStopRuntime` 決定。Presentation 不知道，也不該自己猜。
- **答案的原始數值在烘焙資料裡。** clip 結束站姿的踝位置、剩餘的 root 前進量，屬於 `MotionBakeData`。
- 現行黑板沒有任何欄位能把「本次停步的未來」從 Core 交給 Presentation。
  `ArchitectureRegressionTests.A12` 甚至明文禁止停步／腳相位擴張黑板。

## 2. Decision

### D1 — 以黑板快照傳遞停步預測；唯一 Writer 是 `LocomotionModel`（✅ 裁決）
- `PlayerRuntimeData.LocomotionStopPrediction`：固定大小的 `struct`，**所有欄位都是 value type**（不帶 `MotionBakeData`／`AnimationClip`／Animator 參照）。不參與 `ResetTransientState`。
- **一份快照 ＝ 寫入它的那一次順序 3 tick（publish）當下，對「本次停步結束時」的預測。** 它不是軌跡、不是 plan。消費端只能拿它回答「停步結束時腳在哪」，⛔ 不得依賴 producer 怎麼算出來（排程、時間軸、軌跡實作）。
  停步播放期間 producer **每帧重新 publish**；所以「publish 時點」是該帧，而不是放開按鍵那一帧。
- Reader：只有 `Presentation.IK`。Presentation **不得**反向取得 Bake 資產。

#### `Active` 的語意與失效條件
`Active == true` 的意思是：**producer 在「本帧」的順序 3 tick 中確認以下條件全部成立，並寫出了本帧的值**——不是「producer 最近有跑過」。
1. 有一段 Locomotion Stop 正在播放：`LocomotionStopRuntime.IsPlaying`（已選定 variant、不在 pending、尚未完成／中斷／失效）；
2. 播放頭已實際前進（量到正的播放倍率，`HasPlaybackRate`）——Play 當帧與第一次評估之前不猜；
3. 選定 variant 的 `MotionBakeData` 有有效的結束站姿（`ClipDuration > 0`）且 `Duration > 0`；
4. 目前 clip 時間 < clip 長度。

任何一條不成立 ⇒ 結束站姿欄位為 default（`Active = false`），消費端必須當作「沒有結束站姿預測」。

#### 🔄 D1-R1（2026-10-03 使用者裁決，Trial 期修訂；`docs/45` §14）——next plant 有自己的有效條件
`Active` 只描述**結束站姿**欄位（條件 1–4 不變；FutureLandingProvider／骨盆求解器的行為不變）。
`HasNextPlant` 描述 **next plant** 欄位，條件改為：**stop variant 已 committed**（`LocomotionStopRuntime.IsActive`，含 pending 與等待播放頭）∧ Bake 有結束站姿 ∧ 排程中仍有晚於目前 clip 時間的 planned plant——**不等播放頭前進**。
播放頭尚未前進時以 clip 0 為基準、沒有實測的 clip 前進量（D5 的時間基準修正那一項為 0）。
`RootWorldPositionAtPublish`／`RootYawDegreesAtPublish` 在任一旗標為 true 時有效；兩者皆 false ⇒ 整份為 default。
「每次 tick 先清除」與「消費端同帧讀取」的結構保證不變。

**為什麼不會讀到舊值（結構保證）**：
- Writer 在**每一次**順序 3 tick 的**最開頭**就把快照寫成 `default`，之後條件全部成立才寫入新值。
- 順序 3 在 `CharacterPipelineRunner.Update` 中**每帧無條件**執行（Jump／Roll 期間也是）。runner 直接呼叫方法，所以 model 元件即使被停用仍會被 tick、仍會清除。
- 唯一的消費者（`FootIKController`，經由 `PresentationPipeline`）在**同一個 runner** 的 `LateUpdate`（順序 6.x）讀取，必定在本帧順序 3 之後。runner 被停用時，兩者一起停。
- ⇒ 消費端不可能讀到前一帧的快照。⛔ 消費端也不得跨帧快取它。

### D2 — 正式 schema 只保留 FutureLandingProvider 實際需要的最小資料（✅ 裁決：A）
| 欄位 | 意義 |
|---|---|
| `Active` | 見上 |
| `RootWorldPositionAtPublish` | publish 當下的 root 世界位置 |
| `RootYawDegreesAtPublish` | publish 當下的 root yaw（度） |
| `RemainingForwardDisplacement` | 從 publish 到 clip 結束，root 沿 publish 朝向還會前進多少（m；Bake 位移積分） |
| `EndLeftAnkleInRoot`／`EndRightAnkleInRoot` | clip 結束站姿的左／右腳 Foot 骨骼位置（root 空間；Bake） |

- ⛔ 原型產生、但 FutureLandingProvider 不讀的欄位**不進正式 contract**：hip path、plant events、`Generation`、`RemainingRealTime`、結束站姿的髖位。未來若有正式 consumer 證明需要，再以獨立變更擴充。
- Bake 端同樣縮到最小：`MotionBakeData.FootPlantSchedule.EndPose` 只保留 `LeftAnkleInRoot`／`RightAnkleInRoot`／`ClipDuration`。
  型別與欄位名稱沿用原型，讓已重烘焙的 4 支 Stop 資產可以直接讀取。
  已知的命名債：容器名稱「FootPlantSchedule」現在只裝結束站姿；改名需要 `FormerlySerializedAs`，而且無法對應巢狀欄位路徑，因此不改。

### D3 — Planned Stop 軌跡／控制不進正式 commit（✅ 裁決）
依 `docs/46` §16.1（2026-09-29）：Planned Stop **只當 FutureLandingProvider**。正式 commit 只保留產生落點預測所必需的 provider 邏輯
（`TryPredictStopLandingGoal`、`StopLandingGoalInRoot`、`TryPredictGroundedAnkle`、`TryPredictGroundHeight`、`StopLanding*GoalOffset`）。
`PlannedStop*` 狀態與軌跡、`UpdatePlannedStop`／`TryBuildPlannedStopTrajectory`、`StandingPlanned*` 設定、plant 偵測器（`FootPlantScheduleBaker`）及其測試都**不納入**。
⛔ 不得因此形成第二套 stop／locomotion authority：停步的時序與骨盆仍分別由 `LocomotionModel` 與統一骨盆求解器決定。

### D5 — 下一次 planned plant（2026-10-03 獨立擴充；consumer：`LandingSurfaceResolver`）
**理由（正式 consumer）**：T3 landing-surface slice（`docs/45` §11）的 destination resolver 必須知道每一次落地的目的地。
只有結束站姿時，它判斷不出「這是不是這隻腳的最後一次落地」，Run 的煞車步也沒有任何可信來源（§11.7）。
D2 的原則不變——**只有正式 consumer 需要的資料才進 contract**；本擴充只加 resolver 實際讀取的四個欄位：

| 欄位 | 意義 |
|---|---|
| `HasNextPlant` | publish 之後仍有尚未發生的 planned plant；false ⇒ 其餘三欄為 default |
| `NextPlantIsLeft` | 下一次 planned plant 的腳別 |
| `NextPlantAnkleInPublishRoot` | 該 plant 落地當下的 Foot 骨骼位置，以 **publish 當下的 root** 為座標系；已含 root 從 publish 到該 plant 的前進量 ⇒ **不另存前進距離** |
| `NextPlantIsFinal` | 該 plant 是本次 stop sequence 進入 terminal standing pose 之前的**最後一次 planned foot plant**（Bake 排程的最後一個事件）。⛔ 不等於 clip 結束時刻，也不等於最後一次物理接地 |

- ⛔ 不暴露整條排程、hip path、其他事件。「下一次」＝排程中 clip 時間晚於目前 clip 時間的第一個事件。
- Bake：`FootPlantSchedule` 加入固定四格的 plant 事件（腳別、clip 時間、落地當下的 ankle-in-root），由 Editor-only 的 `FootPlantScheduleBaker` 偵測（plant ＝ 世界 XZ 速度 ≤ 0.2 m/s ∧ 腳踝在該腳 clip 最低點 2.5 cm 內，連續 ≥ 2 個 sample）。
  root 前進量由既有的 `GetLocomotionStopHorizontalDisplacementAt` 推導，不另存。欄位名稱沿用原型 ⇒ 已烘焙的 4 支 Stop 資產直接提供事件，**不需要重新烘焙**。
- `Active` 的語意（D1）不變；`HasNextPlant` 只在 `Active` 時可能為 true。
- Provider：`FootIKController.TryPredictNextPlantGoal`，與結束站姿使用同一套參考點轉換與三點落地規則。Phase 1b 只有量測 probe 讀取，T3 輸出不變。
- **時間基準**（2026-10-03 Phase 1c，`docs/45` §13；Trial 期修訂）：publish 時 root 的位置對應 `PreviousNormalizedTime`，畫面上的姿勢對應 `NormalizedTime`（Animator 的評估比 root 早一次）。
  ⇒ 位置用「姿勢顯示到 plant 那一刻 root 所在的 clip 時間」＝ plant − 本帧實測的 clip 前進量來算 root 前進量。這是時間定義，不是固定偏移。
- 🔄 **語意界定**（2026-10-08，Trial 期非 schema 修訂；`docs/48` §3.2 K1）：next plant 是 **authored contact**——停步 clip 裡這隻腳**動畫上**觸地的那一刻與位置（Bake 的判準，依 2026-10-03 裁決獨立於 T3）。
  ⛔ 消費端**不得**假設它就是 T3 鎖定（`FootLockPhase.Locked`）發生的時刻或位置：兩者的 onset 實測相近（`docs/45` §22.4，≤ 2 cm），但**鎖的持續期間**與**支撐踏面的選擇**都可能不同。
  需要「腳實際被釘在哪」的消費端以 T3 的鎖定支撐為準；next plant 只當**目的地意圖**（`LandingSurfaceResolver` 的低／高可信度＋即時驗證就是這樣用）。

### D6 — 已承諾的停步 tier（2026-10-08 獨立擴充；consumer：規則⑦，`docs/46` §19）
**理由（正式 consumer）**：規則⑦只批准「**Walk 停步**」的落點預測。那個 Walk 是**停步 clip 的家族**——只有 `LocomotionModel` 知道（§1）。
FootIK 原本以 `PlayerRuntimeData.Gait == Walk`（玩家選的步態）代理它；兩者在加減速時會不同（例：跑步中切 Walk 後立刻放開 ⇒ `Gait = Walk`、但播 Run Stop），
正是 §3 否決的「Presentation 自己猜 clip 家族」（`docs/47` F-01、`docs/48` §1）。

| 欄位 | 意義 |
|---|---|
| `StopTier`（`CommittedStopTier`：None／Walk／Run） | 本帧 committed 的 stop variant 屬於哪個家族；沒有 committed 的停步 ⇒ `None` |

- **有效條件獨立**：committed 即有效（含 pending、等待播放頭），**不看** `Active`／`HasNextPlant`。Producer 只覆寫這一欄 ⇒ 既有區塊逐位元不變；Bake 沒有排程時其他欄位仍是 default、tier 照樣發布。
- `CommittedStopTier` 定義在 `Project.Core.Blackboard`（黑板不得依賴 Movement 層型別）；數值與 `LocomotionStopTier` 相同（測試鎖住）。
- ⛔ **這是 contract 修補，不是新的全域 locomotion tier**（2026-10-08 使用者裁決；`docs/48` §6：全域 tier 不是 canonical fact）。它只描述「這一次停步承諾了哪個家族」，停步結束即為 None。
- 規則⑦改讀它是**另一個變更**（`docs/48` M1.3，行為變更，分開 commit）；本條只發布，沒有 consumer 改變行為。

### D4 — 消費端的批准範圍維持 `docs/46` §19 規則⑦
預測只批准 **Walk 下樓停步**當 `FutureReachCap`（只限制骨盆最高升到哪，不主動降低）。
擴大到 Run 或上樓屬於 Living Spec 層級的調整，但每次擴大都要附上同等的驗收。

## 3. Alternatives（否決）

| 方案 | 否決理由 |
|---|---|
| Presentation 自己從 Animancer state ＋ `MotionBakeData` 推算 | 選 clip、起始時間、播放倍率都是 Core 的決定 ⇒ Presentation 推算等於第二個真相來源；而且 Presentation 會因此需要持有 Bake 資產參照 |
| 只在停步開始那一帧發布事件（不持續） | 消費端得自己保存並判斷中斷／換代，等於把 Core 的停步狀態複製到 Presentation |
| 完整 schema（hip path、plant events、`Generation`） | 沒有正式消費者；違反「第二個使用者出現前不建 production abstraction」（D2 裁決） |
| 不做預測，只用即時 Landing（`LiveLanding`） | §15 已量測：Walk 下樓停步的 fallback 來得太晚，會出現 rise→drop |

## 4. Impact
- 黑板 schema 多一個欄位（判準①）；`ArchitectureRegressionTests` 的 WriterRule ＋ A12 baseline 改變（同一個 commit）。
- Bake：`MotionBakeEditor` 在既有的取樣迴圈中記下最後一個 sample 的左右踝（root 空間）。4 支 Stop clip 的資產需要重新烘焙；這些是 Unity 寫出的資產，整檔 commit。
  ⚠️ 現有資產是用原型烘焙器寫的，裡面還有正式程式不讀的鍵（plant events、pose curve、偵測門檻）。Unity 依正式程式重新序列化時才會消失；這不影響讀取。
- `LocomotionStopRuntime` 新增播放倍率量測（`HasPlaybackRate`），只用於 D1 的條件 2。
- 零 GC：快照是固定大小的 struct、每帧整體覆寫；Presentation 的地面查詢使用預先配置的 `RaycastHit[]`。

## 5. Acceptance Criteria（Trial → Accepted）
| # | 條件 | 現況證據 |
|---|---|---|
| AC1 | 只有 `LocomotionModel` 寫入；快照只含 value type；播放頭前進前、以及停步被中斷的同一 tick，`Active = false`（D5 欄位一併清除） | ✅ **2026-10-03 Unity Test Runner 通過**（工作樹對齊 `155b698` 後）：`A5` WriterRules、`A12`（欄位集合）、`Model_PublishesAndClearsNumericStopPrediction`（最小 schema 版） |
| AC2 | Walk 下樓停步的尾腳落點：水平誤差 ≤ 2 cm；翻階只允許「真值本身在階緣 ±5 mm」的邊界站姿，且只能偏低（保守） | §18.3（原型，數值相同）：水平 1.1 cm；23／26 正確階，3 次是同一個邊界站姿、預測偏低 |
| AC3 | 當 `FutureReachCap` 使用時，Walk 下樓停步沒有 rise→drop | §19.3（原型）：rise→drop 最大 0.01 cm（26 次） |
| AC4 | 人工 Play：Walk 下樓停步看起來沒有「站起來又蹲下」 | ⏳ 未做 |

AC2／AC3 的證據來自原型。最小版發布的六個欄位與原型的**值完全相同**（同一套條件、同一套算式），但仍應在正式版本上重跑一次 probe 才算數。

## 6. 失敗處置（revert 路徑）
移除欄位、Writer、FutureLandingProvider、Bake 結束站姿，以及 WriterRule／A12 的變更——一起 revert。
求解器端不需要改：Walk 下樓停步會停在 `PredictionPending`（保守，等尾腳鎖定）——也就是 commit `1fc8017` 單獨存在時的行為。

## 7. 不凍結的內容（Trial 期間可依實作修訂，不必開新 ADR）
`Active` 條件的實作細節（只要語意仍是「本帧確認並寫出」）、參考點轉換（`StopLanding*GoalOffset`）的數值與方法、Bake 結束站姿的取樣方式、批准範圍（D4）。
⚠️ **擴充 schema 欄位不屬於這裡**——那需要正式 consumer 的理由，並且是獨立變更（D2）。

## 8. 判準檢查（為什麼需要 ADR）
①黑板 schema 變更 —— **成立**。②FSM 拓撲／hierarchy —— 不成立。③管線順序／核心驅動介面 —— 不成立（Writer 在既有順序 3）。④推翻既有不變式 —— **成立**（A12「停步／腳相位不擴張黑板」）。

## 9. 裁決紀錄
- 2026-10-08（使用者，第二輪）：**D6** 開工——發布 `LocomotionModel` 已承諾的 stop tier（`StopTier`）。contract 修補，不是新的全域 locomotion tier。規則⑦改讀它（M1.3）在本條之後、分開 commit，完成後重跑下樓停步驗收。
- 2026-10-08（使用者，接受 `docs/48` F3 方向）：D5 補上語意界定——next plant ＝ authored contact，⛔ 不等於 T3 lock（文字修訂，schema／行為不變）。F1 方向接受、D1-b Yes：規則⑦的 Walk／Run 跟 committed stop tier；對應的 `StopTier` 欄位（D6）待開工時另行定案，**本條尚未改變 schema**。
- 2026-10-03（使用者，第三次）：D1-R1——next plant 在 stop variant committed 時即可發布，不等待播放頭前進；end-pose 定義與 Bake plant 門檻不變（Phase 1c 不讓 Baker 依賴 T3 門檻）。
- 2026-10-03（使用者，第二次）：D5——以 `LandingSurfaceResolver` 為正式 consumer，加入最小的 next-plant snapshot（`HasNextPlant`／腳別／publish-root 位置／`IsFinal`）；不推翻 D2。前進距離可由位置推導 ⇒ 不另存。
- 2026-10-03（使用者）：D1 Yes；D2 A（只保留六個欄位，未來需要時以獨立變更擴充；`Active` 的語意與失效條件必須明確定義）；D3 Confirmed（Planned Stop 軌跡／控制不進正式 commit，不得形成第二套 stop／locomotion authority）。Proposed → **Trial**。
