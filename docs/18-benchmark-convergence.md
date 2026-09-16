# 18 — Benchmark 收斂輪：Observability 最小設計、Motion Authority 分類、Traversal 假說重檢

> **這份文件是什麼**：`docs/17-benchmark-3c-demo.md` 的**收斂輪**。17 負責「看到了什麼」，18 負責「所以現在該做什麼、該先懂什麼、什麼還不該碰」。
> **這份文件不是什麼**：⛔ 不是 ADR、⛔ 不是 roadmap 修訂、⛔ 不是實作規格、⛔ 不排程。
> 下方所有 ticket 都是**候選工作項**，排不排、什麼時候排，由使用者決定。
> 建立日期：2026-09-10。程式對照基準：當日工作樹（未 commit，見 `docs/17` §7-F1）。

---

## 0. 本輪的三個結論（先讀這段）

| # | 結論 | 依據 |
|---|---|---|
| **C1** | **「traversal 是 jump 的特化」只有一半成立。** 玩家入口（entry）成立且已有現成先例；執行模型（execution）**不成立**，而且不成立的理由是可驗證的、不是品味問題 | §2 |
| **C2** | **Continuous / Committed 這組分類在 repo 裡已經存在，而且已經被實作了兩次、形狀不同**（`RollState` 是一個 state；`LocomotionStopRuntime` 是 model 內的一個 struct）。**這個不一致本身就是 `docs/03` §6.4 的 G1 問題**，分類只是讓它第一次講得出來 | §3 |
| **C3** | **需要 ADR 的觸發點不是「環境查詢存在」，而是「world-derived candidate 開始被第二個消費者依賴」。** 在那之前，一個能翻越的 spike 可以完全不碰黑板 | §5 |

---

## 1. Observability：world-space 範圍與最小實作（不做 UI）

### 1.0 共通前提

**四條紀律**，其中第 3 條是本節的核心：

1. **只存在於 `UNITY_EDITOR || DEVELOPMENT_BUILD`。** Game View runtime rendering 可在 Editor Play／Development Build 使用；
   Scene View 的 Gizmo 程式仍只在 `UNITY_EDITOR`。Release build 不含 snapshot 與 renderer。
2. **Presentation 寫在「擁有資料的那個元件」自己身上。** owner 內部可直接讀 private 欄位，
   不論是 runtime renderer 或 `OnDrawGizmos`，都能維持**零新 API、零跨層、零黑板變更**。
   只有「已存在的決策資料離開當幀就會消失」時才需要 debug-only private snapshot。
3. ⭐ **「記錄，不要重算」（record, don't recompute）。**
   debug 顯示**只能畫決策當下被記下來的值**，⛔ 不得由 drawer 自己重新推導。
   **理由不是潔癖**：`docs/16-review-protocol.md` 明列本專案的四項加重審查之一就是「**下游重新推導已 commit 的狀態**」——
   一個會自己重發 physics query 或重算 candidate 的 gizmo，就是第二套決策實作；
   它在兩邊結果不同時會讓人相信錯的那一邊，比沒有 debug 更糟。
   ⇒ **這條就是「debug 不會變成第二套 decision system」的具體保證**，而不是一句承諾。
4. **debug 欄位單向**：production query 的 owner 寫、debug presentation 讀，**任何 gameplay 決策不得回讀**。
   A33／A34 已把 Direction snapshot 與 Foot IK 兩條 presentation 的對應邊界機器化。

**一個必須知道的組裝事實**（磁碟核對 2026-09-10）：
`InternalsVisibleTo("Project.Tests.EditMode")` 存在（宣告於 `AimResolver.cs`，作用於整個 assembly），
但**沒有** `InternalsVisibleTo("Project.Editor")`。
⇒ **Editor 端（Inspector／EditorWindow）看不到 `internal`**；要給 Editor 讀就必須是 `public`。
⇒ 這正是「gizmo 寫在元件內部」比「Editor 端畫」便宜的第二個理由。

---

### 1.1 Observability 範圍收斂（✅ 2026-09-10 使用者裁決）

**world-space debug visualization 只保留兩類：**

1. **Traversal／可跳上平台檢測**：前方 probe、obstacle hit point、top／standable candidate、clearance、最終判定。
2. **Foot IK**：ankle／heel／toe probes、ground hit point、原始與夾限後 normal、必要的 target／corrected-pose visualization。

判準只有一個：**玩家肉眼看角色行為時，無法知道系統實際採樣了什麼世界幾何資料。**
呈現分成兩條明確通道：

* **Scene View 詳查**：可以用 `Gizmos`／`Handles`，供近距離檢查。
* **Game View／PlayMode 主通道**：必須用真正的 runtime-visible renderer；正常按 Play 就能看到，
  不依賴選取角色，也不以手動開啟 Game View `Gizmos` 為必要條件。

兩條通道都只能畫 production query 已記下的快照；不得為 debug 重新發 physics query。

Direction Authority 的 Editor-only snapshot 與 A33 回歸測試保留，但**收回四箭頭、deadzone、
label、transition history 與 PlayMode direction HUD**。Facing／MoveDirection／Authority 不再投入
presentation 成本；日後只有 combat 8-way 或 facing 問題提供具體需求時才重開。

✅ **2026-09-12 O-T 前置條件已解除。** `TraversalProbe` 現在是角色前方 obstacle／top／depth／
destination clearance 的唯一 production physics-query owner，並在決策點記錄量測與最終 candidate snapshot；
`TraversalClassifier` 保持不讀 Physics／Time／黑板的純函式。Game View runtime renderer 與 Scene View Gizmo
都只讀該 snapshot，呈現 probe、obstacle hit、top／standable、clearance 與最終 `Kind／RejectReason`，
不會為 debug 重發 query。兩條通道仍待 Unity Play 做視覺品質與 tuning 驗收。

---
### 1.2 Ticket O-2：Foot IK / Ground Probe 探測可視化（✅ 2026-09-10 已實作，待 Unity Play 驗收）

> 實作位於 `FootIKController` 的 `UNITY_EDITOR || DEVELOPMENT_BUILD` private snapshot ＋ runtime `LineRenderer`，
> 並在 Editor 另保留 `OnDrawGizmos` 詳查通道。
> 顯示 ankle／heel／toe rays（命中與落空）、hit point、raw／clamped normal、heel→toe origin span、
> production offset 實際使用的 corrected foot forward、pre-IK goal 到最終 target 的修正線、target sole pose
> 與 residual lift。Game View 正常 Play 即可直接看，
> 即使關閉 `Gizmos` 仍然可見且不需選取角色。A34 禁止兩條 presentation 重發 physics query。

| 項目 | 內容 |
|---|---|
| **要回答的 debug 問題** | ①腳戳穿／浮空，是 **ankle ray 錯**、**heel/toe 殘差錯**、還是 **normal clamp 把它掰回去了**？<br>②骨盆為什麼沉這麼多／完全不沉？<br>③`docs/05` §3.5.5 的 L1 約束模型**現在只能靠讀程式想像** |
| **應顯示的資料** | ① ankle ray：origin、方向、`RaycastDistance`、命中點<br>② heel／toe ray：`worldHeel`／`worldToe` 起點與命中點、**任一落空**（此時整段殘差被丟棄、退回 ankle-only）<br>③ heel origin → toe origin 直線，以及同次 offset 計算使用的 `sample.TargetRotation × Vector3.forward` 箭頭<br>④ **`Normal`（原始）vs `SoleNormal`（clamp 後）兩支法線**——`MaxFootAlignAngle` 有沒有在作用，一眼可見<br>⑤ `lift` 殘差量<br>⑥ pre-IK goal → 最終 IK target 的位置修正與 target sole pose（透明度編碼 position weight） |
| **資料現在在哪** | 最終 target 已在 `FootIKController.TargetData`；probes／normals／lift 原本是 `SampleGround` 的逐幀區域值，現由同一 owner 快取進 Editor／Development-only private snapshot |
| **宿主** | **`Presentation/IK/FootIKController.cs`** 自己（MonoBehaviour）。它算出全部這些值 |
| **通道** | **Game View 主通道**＝runtime `LineRenderer`，不依賴 `Gizmos`；**Scene View 詳查**＝`OnDrawGizmos`（非 selected-only）。兩者讀同一份快照 |
| **現在不值得做的通道** | ⛔ **trace/log**——每腳每幀多條射線，log 是不可讀的<br>⛔ **正式 HUD／debug framework**——runtime 線段只是局部 world-space renderer，不承擔狀態 UI<br>⛔ **曲線記錄器**（`PelvisOffsetY` 隨時間的圖）——有價值但屬第二輪，先看得到幾何再說 |
| **需要新增 debug snapshot 嗎** | **不需要新 public API。** 只在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 內把兩腳的 `FootSample`、ray 起點／命中與最終 target 快取成 private 欄位 |
| **layer boundary** | ✅ **不碰**。`Presentation` 內部自畫，不讀狀態機、不讀輸入 |
| **不變成第二套決策的保證** | runtime renderer 與 gizmo 都讀 `SampleGround` **已經算完並存下來**的 `FootSample`，⛔ 不重新發射任何射線（重射一次 = 第二次物理查詢 = 可能得到不同答案 = 最糟的 debug） |

---

### 1.3 Ticket O-3：Stop / Animation Selection trace（⏸ 不在目前 observability scope）

| 項目 | 內容 |
|---|---|
| **要回答的 debug 問題** | ①收步為什麼選這支變體、或**為什麼靜默不播**？（`SelectByEntryPhase` 回 `-1` ⇒ `default` ⇒ 保留現有鍵、`_landDuration = 0` ⇒ 立即退場——**畫面上什麼都不會發生**）<br>②動畫鍵請求被 `TryGetTransition` 查表失敗擋掉了嗎？（W13 那一輪的整個診斷都在補這件事）<br>③落地為什麼被分類成 hard／normal？ |
| **應顯示的資料** | ①**事件時刻**：`Begin`／`BeginPending`／`StartPlaying`／`TryRequestCompletion`／`Invalidate`／timeout<br>②每次選片的**輸入**（`Tier`、entry `FootPhase`、`DesiredSpeedNormalized`）與**輸出**（`VariantIndex`，含 `-1`）<br>③`Generation`（判斷是不是舊世代的回呼被丟掉）<br>④`NormalizedTime` vs `TargetNormalizedTime`<br>⑤順序 5 實際送出的 `AnimationKey` 與**是否查表成功** |
| **資料現在在哪** | ①②③④ `LocomotionModel._stop`（**private**，型別 `LocomotionStopRuntime`——**該 struct 的所有查詢屬性都已經是 public 唯讀**：`IsActive`／`IsPending`／`IsPlaying`／`Tier`／`VariantIndex`／`Generation`／`NormalizedTime`／`TargetNormalizedTime`／`ElapsedRealTime`）<br>⑤ `CharacterPipelineRunner._lastPlayedKey`（private）＋ `AnimancerFacade.TryGetTransition`（private，失敗時已 `LogWarning`） |
| **宿主** | **`Core/Movement/Models/LocomotionModel.cs`**（MonoBehaviour） |
| **通道** | ⭐ **trace／log 是這一類的正解**，因為它問的是「**剛剛發生了什麼、順序是什麼**」，不是「現在是什麼」。<br>形狀：`[SerializeField] private bool traceStopSelection;` ＋ `#if UNITY_EDITOR` 的條件式 `Debug.Log`，**預設關閉**<br>次要：Inspector 顯示當前 `_stop` 的狀態列（IsActive／Tier／VariantIndex／NormalizedTime） |
| **現在不值得做的通道** | ⛔ **Gizmo**——選片是離散事件，畫在世界裡沒有位置語意<br>⛔ **自建 trace 視窗／EditorWindow**——Console 已經夠用，做視窗要過 Editor Tool 的兩道閘門（`CLAUDE.md`），現在過不了 Gate A<br>⛔ **runtime HUD** |
| **需要新增 debug snapshot 嗎** | **不需要**（trace 從擁有者內部印）。<br>若日後要在 Inspector 顯示：`public LocomotionStopRuntime StopDebug => _stop;` 回傳**值複製**即可——該 struct 對外只有唯讀屬性，複製體被改也影響不到本體 |
| **layer boundary** | ✅ **不碰** |
| **⚠️ 零 GC 的正確說法** | `Debug.Log` 會配置（dev-spec §7.4 已量到狀態切換幀約 2.6 KB，其中 2.4 KB 是 Unity 的 `StackTraceUtility`）。<br>**但這不構成零 GC 回歸**：整段在 `#if UNITY_EDITOR` 內、Player build 不存在，而零 GC 的驗收 SOP 本來就跑 **Development Build**。<br>⇒ 條件仍是：**預設關閉 ＋ `#if UNITY_EDITOR`**，且**不得**在文件裡把它寫成「零 GC 例外」 |
| **不變成第二套決策的保證** | trace 印的是 `SelectByEntryPhase` 的**回傳值**與 `_stop` 的**現值**，⛔ drawer 不得自己再呼叫一次 selector（那會在兩次呼叫之間 phase 已變時給出假訊息） |

---

### 1.4 目前**刻意不做**的 observability

| 項目 | 為什麼不做 |
|---|---|
| **Release 事件的世界標記**（A6 驗收用） | 需要 `ActionState` 開一個 public debug snapshot，或在三個 sink 各畫一次。**兩條路都要先決定「誰擁有 release 的可觀測性」**——那是設計決定，不是 ticket |
| **正式 runtime HUD / `Debug/` asmdef** | 「第二個使用者出現前不得建立 production abstraction」。Foot IK 的局部 runtime 線段只服務 Editor／Development world-space debug，不是 HUD |
| **額外 debug architecture framework** | A33／A34 已直接守住現有 snapshot 的單向性與「記錄，不重算」；不建立抽象層 |
| **統一的 debug 開關／設定資產** | `FootIKController.drawFootIKRuntimeLines` 與 `drawFootIKSceneGizmos` 已分流。做成共用資產＝提前建 framework |

---

## 2. Traversal 假說重檢：把「jump 的特化」拆成兩個問題

> 上一輪我寫下「traversal 是跳躍的特化，不是平行系統」。
> **本輪把它降級為假說並分兩層檢查——結論是：入口成立，執行模型不成立。**

### 2.1 A. 玩家入口 — ✅ 假說成立，而且已有現成先例

**主張**：`Intent.JumpRequested` → 有合法 traversal candidate → traversal；否則 normal jump。

**支持證據（磁碟）**：`JumpState.CanEnter` **現在就已經有兩條入口**，而且兩條的語意差異比「jump vs traversal」還大：

| | 主動 Jump | 非主動失地 |
|---|---|---|
| 觸發 | `JumpRequested && IsGrounded` | 連續離地 ≥ `FallEntryGrace` |
| 起始 phase | `Start` | `Falling` |
| launch 注入 | ✅ | ⛔ |
| 空中按跳 | ✅ 可推進 stage | ⛔ 拒絕（守 ADR-002） |

而且 `CanEnter` **已經在用「把本幀裁決存成私有欄位、`OnEnter` 只消費不重推」的 pattern**（`_enterAsFall`），
註解明寫：「`OnEnter` 刻意不從 data 重新推導進入原因，避免下游重算已承諾決策的反模式」。

⇒ **這正是 traversal candidate 需要的形狀**：`CanEnter` 解出 candidate → 存私有欄位 → `OnEnter` 消費。
⇒ **入口這一層不需要新機制、不需要 ADR、不需要改介面。**

📌 **順帶解掉一個介面疑慮**：`BaseState.CanEnter(data) → bool` 只能回一個 bit，帶不動 candidate 參數。
**但 `_enterAsFall` 證明了不需要改簽名**——裁決存在 state 自己身上即可。

### 2.2 B. 執行模型 — ❌ 假說不成立

**⚠️ 這裡有一個必須明說的推理錯誤**：上一輪我從「偵測失敗回退到跳躍」推論「traversal 從屬於 jump」。
**那是把 UX fallback 當成架構從屬。** 兩者無關——`RollState` 失敗也會退回 `ExecuteBaseMovement`，
但沒有人會說「Roll 從屬於 locomotion」。

**四條磁碟證據**：

| # | 事實 | 為什麼它否證「traversal 屬於 JumpState」 |
|---|---|---|
| **B1** | `JumpState.OnUpdateMotion` 的水平位移是 `motionDriver.ExecuteBaseMovement(data)`——**吃當幀的 Movement Output**，而 `LocomotionModel.Tick` 每幀無條件更新它 | **Jump 的水平軸是 continuous**。traversal 的水平軸是**對世界目標的承諾**。兩者的 authority 模型相反 |
| **B2** | `JumpLaunchData` 只有 `InitialVerticalVelocity` ＋ `Gravity`，由烘焙高度逆推（`v = √(2gh)`） | **Jump 沒有 world target**。它的落點是**結果**，不是**輸入** |
| **B3** | 影片的「距离过远✕」是**進入前的拒絕理由** | traversal 的落點是**准入條件**。Jump 的准入是 1 bit（`JumpRequested && IsGrounded`），traversal 的准入需要一個**帶參數的 candidate 通過驗證** |
| **B4** | `JumpState.cs` 目前 **603 行**、2 個 enum、13 個私有欄位、18 格變體表、5 個 phase 概念（AnimationPhase × LandingPhase） | **最強的反對理由不是架構潔癖，是 JumpState 已經到複雜度上限。** 再塞 entry／alignment／execution／exit ＋ collision profile，它會變成第二個 God Class |

### 2.3 可以共用 / 必須分開 / 證據不足

| | 內容 |
|---|---|
| ✅ **可以共用** | ①**入口觸發**：`Intent.JumpRequested` 當唯一按鍵（**不新增輸入**）<br>②**FSM 的承諾詞彙**：`CanTransitionAway`（自然過渡閘門）＋ `CanBeInterruptedBy`（config 矩陣）——**已經是現成的 commitment 機制**<br>③**有界時長**：`MotionBakeData.BakedDuration` ＋「值 > 0 才算可用」的退化判準（`RollState` 的教訓）<br>④**軌跡執行**：既有 `ExecuteBakedCurveMovement` 仍是水平曲線＋重力；✅ **Traversal V1（2026-09-12）已補上** `MotionBakeData.VerticalCurve` 與尚無 gameplay 呼叫端的 `MotionDriver.ExecuteCommittedCurveMovement(...)`，以播放頭前後差值同時消費水平／垂直曲線，committed 期間不積分重力。`ApplyBakedCompensation` 仍是平面 warp<br>⑤**出場交還**：Jump 落地的 `normalLandToMove`／`walk[]`／`run[]` 變體選擇是**已經解過的問題** |
| ⛔ **必須分開** | ①**candidate（world target ＋ 分類 ＋ 拒絕理由）**——Jump 完全沒有這個概念<br>②**alignment／warping**——Jump 沒有<br>③**執行期 collision profile 變更**——Jump 沒有，且會動到 `IsGrounded`（黑板欄位、單一寫入者）<br>④**逐軸 authority**：Jump 是「垂直 committed／水平 continuous」的**固定**組合；traversal 需要「全軸 committed」 |
| 🔴 **證據不足，現在不判斷** | ①traversal 執行中**該不該可被中斷**（受擊？取消？）——取決於還不存在的戰鬥設計<br>②candidate 需不需要**跨幀存活**，還是每幀重查（**這一條直接決定要不要 ADR**，見 §5）<br>③alignment 是**獨立 phase** 還是併進 execution 的前段——取決於實際動畫資產的起手容差<br>④影片的「由下向上的 while 迴圈」是**每幀跑**還是**按鍵時跑**——`docs/17` §8 已列，未解 |

**⇒ 本節的可交付結論**：入口共用、執行分開。**但「分開之後住哪裡」＝ `docs/03` §6.4 的 G1，本輪不回答。**

---

## 3. Continuous Motion vs Committed Motion（重點分析）

### 3.1 先回答：repo 裡是不是已經有這兩類？— **是，而且已經實作了兩次、形狀不同**

| 案例 | 類別 | 磁碟證據 |
|---|---|---|
| **Locomotion**（Walk／Run／Idle） | **Continuous** | `LocomotionModel.Tick` 每幀由 `MovementIntent` 重解 `MoveSpeed`／`MoveDirection`；`IMovementModel` 註解明寫「**每幀無條件推進**」；`BaseState.OnUpdateMotion` 預設 → `ExecuteBaseMovement` |
| **RollState** | **Committed** | `_rollTimer` 在 `OnEnter` 由 `BakedDuration` 一次latch；`CanTransitionAway => IsRollFinished`；位移＝`ExecuteBakedCurveMovement(bake, normalizedTime)`；⭐ **執行期完全不讀 `MovementIntent`** |
| **ActionState** | **Committed × 2 通道** | 位移：per-phase `Bake` ＋ `ExecuteBakedCurveMovement`；`CanTransitionAway => _phase == None`<br>⭐ **方向也是 committed**：`_releaseContext` 在段落邊界 `CaptureReleaseContext` 一次取得，facing 與世界效果**讀同一份**（ADR-007 D5） |
| **JumpState** | **跨兩類，逐軸不同** | 垂直：`ApplyJumpLaunch` 注入一次後由 `MotionDriver` 積分 ⇒ **committed**<br>水平：`ExecuteBaseMovement(data)` 讀當幀 Movement Output ⇒ **continuous** |
| ⭐ **`LocomotionStopRuntime`** | **Committed，但住在 model 裡** | `Begin`／`BeginPending`／`StartPlaying`／`Advance`／`TryRequestCompletion`／`Invalidate`／`HasTimedOut`／`HasPendingTimedOut` ＋ `Generation`（防舊世代回呼）＋ `TargetNormalizedTime` —— **這是一套完整的 committed motion 生命週期**，只是被實作成 `LocomotionModel` 內的一個 struct |

> ⭐ **這張表最重要的一列是最後一列。**
> 同一個概念在 repo 裡被實作了**兩次、放在兩個不同的層**：
> **Roll ＝ 一個 FSM state；Stop ＝ 一個 model 內部的 struct。**
> 這不是誰對誰錯——`docs/03` §6.4 的 **G1**（「承載該是 model 內部 phase、獨立 Presentation FSM、還是 Gameplay State？」）
> 問的正是這件事。**分類的價值在於：它讓這個問題第一次講得出口。**

### 3.2 ⚠️ 對使用者分類的一處修正：**committed ≠ targeted**（這是兩個獨立的軸）

使用者列的 committed 清單（Roll／Vault／Mantle／attack lunge／combat snap／interaction alignment／motion warp）
把兩件事混在一起了。用 repo 證據拆開：

| | **有 world target** | **無 world target** |
|---|---|---|
| **Committed** | Vault／Mantle／combat snap／interaction alignment／motion warp | ⭐ **Roll**（軌跡是烘焙的、相對角色自身）、Action 的位移段 |
| **Continuous** | （罕見：持續追蹤的 homing 移動） | Walk／Run／Strafe／Falling／Jump 的水平軸 |

**⇒ `RollState` 是「committed 但 untargeted」。**
它證明了**承諾與目標可以分開**：Roll 已經有「開始／執行／結束／不可中斷窗／交還」全套，卻**沒有任何 world target**。

**⇒ 這也順便回答了 §3.5「Motion Warping 為什麼靠近 committed motion」**：
warping 就是**把 untargeted committed motion 變成 targeted 的那個機制**。它不是新的能力層級，是 Roll 已有形狀的加參數版本。

### 3.3 第三個狀態：**Suspended（授權暫扣）**

兩類不夠。`JumpState.LandingPhase.HardRecovery` 是第三種：

```
_landingPhase == HardRecovery ⇒ motionDriver.ExecuteVerticalOnlyMovement(data)
```

註解明寫：「保留 facing／重力／grounded／collision 更新，但不施加 Movement Output 的水平速度。
**不得藉由清空 MoveDirection 破壞黑板單一寫入者**」。

⇒ 這既不是 continuous（不吃輸入）也不是 committed（沒有軌跡、沒有目標）——它是**授權被暫時收回**。
⇒ 所以**一個運動通道有三種狀態：Continuous／Committed／Suspended**，
且 `ExecuteVerticalOnlyMovement` 就是「水平 suspended」的既有實作。

📌 **附帶價值**：這條也給了 traversal 一個現成答案——執行期「不吃玩家輸入」不需要新機制，
`ExecuteVerticalOnlyMovement` 已經示範了**怎麼在不破壞黑板單一寫入者的前提下扣掉一個軸**。

### 3.4 和 `IMovementModel` contract 差在哪

| | `IMovementModel` | Committed motion 需要的 |
|---|---|---|
| 生命週期 | **無**。只有 `Tick`（每幀）＋ `UpdateMotion`（每幀） | start / execution / completion |
| 完成訊號 | **無**。只有 `IsProducingMotion`，而它是**門檻**（`Speed >= 0.1 \|\| _stop.IsActive`），不是狀態 | 「我做完了」 |
| 目標 | **無** | 可選的 world target |
| 授權 | 隱含「永遠可被新意圖覆寫」 | 明確的不可中斷窗 |
| 交還 | 不適用（從不結束） | 必須定義出場條件 |

⇒ **committed motion 在契約上表達不出來，所以它從來沒走 `IMovementModel`。**
Roll 與 Action 走的是 `BaseState.OnUpdateMotion` **override**；Stop 則走 model 內的私有 struct。
**這不是繞過契約，是契約沒有這個概念。**

⚠️ **但這不表示該去改 `IMovementModel`。** 改它＝ADR 判準 ③（核心驅動介面變更）。
現在的證據只支持「命名這個概念」，**不支持改介面**。

### 3.5 Motion Warping 為什麼自然靠近 committed motion

三個理由，其中第三個是磁碟事實：

1. warping 的前提是「有一條**已編寫的軌跡**」——continuous motion 沒有軌跡可以扭。
2. warping 的前提是「有一個**與軌跡落點不同的世界目標**」——continuous motion 沒有目標。
3. ⭐ **`MotionDriver.ApplyBakedCompensation(MotionBakeData bakeData, Vector3 actualTarget, float normalizedTime, PlayerRuntimeData data)` 已經存在**——
   它接受**烘焙曲線 ＋ 一個世界目標**，**是 targeted committed motion 的原始形狀**，
   而且**目前沒有任何真實使用者**（`RollState` 走的是不帶 target 的 `ExecuteBakedCurveMovement`）。

⇒ **Motion Warping 不是要新建的能力，是要接上的既有 seam。**（`docs/03` §6.3 步驟 6 已排；原文已指名 traversal 對齊為第一個案例。）

### 3.6 這個概念值不值得寫進文件？— **值得，但只當詞彙**（✅ **2026-09-10 已裁決並落地**）

> ✅ **使用者裁決（2026-09-10）**：同意寫進 `docs/01-design-doc.md`，**已落地為 §2.9**。
> 落地內容嚴格限於本節建議的範圍：**三個詞彙定義 ＋ 現有案例對照 ＋ committed/targeted 兩軸 ＋ Jump 逐軸混合**。
> 明列的不主張事項（不改 `IMovementModel`／state contract、不進 dev-spec §1／§2／§3.1、不開 ADR、**目前不是正式 taxonomy**）
> 已原文寫進 `docs/01` §2.9 的開頭與結尾。
> **若未來這套分類真的導致 contract／ownership／execution model 改變，再走 ADR。**

| 問題 | 回答 |
|---|---|
| **是不是真正有用的架構概念** | ✅ **是**。判準不是「聽起來合理」，而是「**它讓某個原本問不出口的問題變得可問**」。它讓「Stop 的 committed motion 放在 model 裡、Roll 的放在 state 裡，這兩個該一致嗎」第一次成為一個可以討論的問題 |
| **該寫哪裡** | 建議 **`docs/01-design-doc.md`**（Living：當前架構與職責邊界）新增一節詞彙定義，**或**先只留在本檔。⚠️ **⛔ 不進 dev-spec §1／§2／§3.1**——那些是契約，而這只是詞彙 |
| **要寫什麼** | 只寫**三個狀態的定義 ＋ 現有案例對照表（§3.1）＋ committed/targeted 兩軸（§3.2）**。⛔ 不寫「未來該怎麼做」 |
| **現在需要 ADR 嗎** | ❌ **不需要**，逐條對照 `CLAUDE.md` 判準：<br>① 黑板 schema／ownership 變更 → **否**（命名不改任何欄位）<br>② FSM 拓撲／hierarchy 變更 → **否**<br>③ 核心驅動介面變更 → **否**（明確不改 `IMovementModel`）<br>④ 推翻既有不變量 → **否**<br>⇒ **0/4，走 Living Docs routing rule** |
| **⚠️ 附帶的硬限制** | 一般規則仍是同時一個 Trial。2026-09-11 ADR-006／007 同時 Trial 是使用者為 combined vertical slice 批准的**一次性例外**，不授權其他 ADR 或 scope expansion 並行 |

---

## 4. Environment Query 的理解順序（不是實作順序）

> **每一層的重點是「它和上一層差在哪」**——工具（`CapsuleCast`）是最不重要的部分。

| 層 | 白話：這層在解什麼 | **和上一層差在哪**（關鍵） | 輸入 → 輸出 | Unity 可能用到 | IntentPipeline 現有類似概念 | 全新問題域？ |
|---|---|---|---|---|---|---|
| **1. Environment Query** | 「我前面有什麼形狀」 | — | 角色 pose／capsule／LayerMask → 命中點、法線、距離、高度 | `Raycast`／`SphereCast`／`CapsuleCast`／`OverlapCapsule`／`*NonAlloc`／`QueryTriggerInteraction` | ✅ 既有查詢只服務相機遮擋、鏡頭瞄準、Foot IK、地面特效、combat target。⛔ 沒有任何「描述角色前方世界」的查詢 | 🟡 **工具已熟，用途全新** |
| **2. Classification** | 「那是什麼」 | ⭐ query 回**量測值**；classification 回**一個名字 ＋ 一個理由**。從「1.2m 處有東西」到「那是可翻越矮牆／太高**因為**超過最大攀爬高度」 | 量測值 ＋ 能力參數 → 類別 ＋ **拒絕理由** | 純資料判斷（不需 Unity API）；能力參數應是 SO 而非常數 | ✅ `LocomotionStopSelector.SelectByEntryPhase`（連續量→離散選擇，**且明確定義退化值 −1**）是同一種函式，且已是純函式／可 EditMode 測 | 🟢 **形狀已有** |
| **3. Affordance / Candidate** | 「以我**現在**的狀態，那允許我做什麼」 | ⭐ classification 描述**世界**；affordance 描述**這個角色此刻**。同一面牆，走路與衝刺的 candidate 不同（影片：「不同移速影响…」）。而且 candidate **帶參數**（起跳點、對齊目標、時長），不只是一個 enum | 類別 ＋ 角色速度／狀態 → **candidate 清單（可為空）** | 資料驅動的動作定義（SO） | ✅ `ActionDefinitionSO` ＋ `ActionSlot` ＋ `ActionState` 已證明「一份資產＝一個動作、加動作零 runtime 程式」（ADR-005 Acceptance B） | 🟡 **candidate 的來源從按鍵變成環境** |
| **4. Selection / Arbitration** | 「有好幾個怎麼選、一個都沒有怎麼辦」 | ⭐ affordance 說**什麼可能**；selection 說**什麼贏**，並且**必須定義輸的情況**（fallback）。空清單是**有效答案**，不是錯誤 | candidate 清單 ＋ 當前狀態 → 一個決定，或 fallback | 優先序 vs 評分；fallback 鏈 | ✅ `CanEnter`／`EvaluateInterrupts` 就是這層；`JumpState.CanEnter` 的**兩條入口 ＋ `_enterAsFall` 私有裁決**是現成 pattern（§2.1） | 🟢 **機制已有** |
| **5. Alignment** | 「動畫假設角色在某個相對位置，但它不在」 | ⭐ selection 產出**一個決定**；alignment 產出**讓決定可執行的 transform 軌跡**。世界目標在這裡從「數字」變回「幾何」 | world target ＋ 當前 transform ＋ 時間窗 → 受控的 transform 修正 | `Animator.MatchTarget`／`StateMachineBehaviour` 時間窗／Motion Warping／distance matching | 🟡 `MotionDriver.ApplyBakedCompensation(…, actualTarget, …)` **已存在但無使用者**（§3.5） | 🟡 **seam 有，語意全新** |
| **6. Motion Execution** | 「播放期間，位移／碰撞／IK／重力各自做什麼」 | ⭐ alignment 只管**第一個時刻**；execution 管**中間每一個時刻**，包含可不可被中斷 | 動畫進度 ＋ 烘焙資料 → 每幀位移 ＋ 碰撞設定 ＋ 其他系統抑制 | 動畫事件、`CharacterController.height`／`center`、IK 權重、重力抑制 | ✅ `RollState` 是完整範例；`ArbiterData.BlockIK` 已有；`ExecuteVerticalOnlyMovement` 已示範「扣掉一個軸」（§3.3） | 🔴 **執行期改碰撞體是全新的**（會動到 `IsGrounded`，黑板欄位、單一寫入者） |
| **7. Return to Locomotion** | 「結束時把角色乾淨交還」 | ⭐ execution 管**動作本身**；return 決定**下一幀的 continuous motion 從什麼初始條件開始** | 結束時的位置／朝向／速度 ＋ 玩家當下輸入 → locomotion 初始條件 | 出場速度繼承、blend、foot phase 對齊 | ✅ **這一層 IntentPipeline 反而成熟**：Jump 落地的 `normalLandToMove`／`walk[]`／`run[]` 變體、Stop 的 entry-phase 選片、`LocomotionSpeedSmoother` 的連續性 | 🟢 **可直接沿用經驗** |

**一句話總結**：`CapsuleCast` 只是第 1 層的工具；
**真正要學會回答的是第 3 層的問題——「什麼條件才構成一個可執行的 candidate」**，
而那個答案有一半來自**角色當下的狀態**，不只是幾何。

---

## 5. 世界資料要不要進架構：精確化

### 5.1 兩者的判準（**不是**「資料是否來自世界」）

| | **局部 query implementation detail** | **正式 world-derived gameplay concept** |
|---|---|---|
| 判準 | **同一幀內、由發出查詢的同一個元件消費完畢，其他人觀察不到** | **有第二個消費者，而且兩者的決定必須互相一致** |
| repo 現有例子 | `ThirdPersonCamera` 的遮擋 `SphereCast`（當幀被 `ResolveCameraDistance` 吃掉）<br>`FootIKController.RaycastGround`（當幀被 `SampleGround` 吃掉）<br>`GroundEffectSink` 的探地 `RaycastNonAlloc` | ⭐ **`CombatContextData.TargetPosition`**——**已經是黑板成員**，因為 facing、AI 移動、Action 目標**必須指向同一個目標** |
| 需要 ADR 嗎 | ❌ 不需要 | ✅ 進黑板＝判準 ①（schema／ownership），需要 |

> ⚠️ **這條同時修正了 `docs/17` §4.3-1 的一句話**（見 §7-S4）：
> 「環境查詢結果是第一份『角色以外的世界』資料」**是錯的**——`CombatContext.TargetPosition` 已經是了。
> 正確的說法是：**它會是第一份需要「跨幀存活 ＋ 參與 selection ＋ 被多方消費」的世界資料。**

**⇒ 所以 `Ground Normal` 現在不進黑板是對的**（`docs/17` §7-F3 標成 🔴 略嫌重了）：
它今天只有一個消費者（Foot IK 自己），符合左欄。**它會在有第二個消費者的那一天才需要決定。**

### 5.2 什麼時候才真的需要 ADR（觸發條件，不是時程）

**任一條成立 ⇒ 停下來開 ADR。全部不成立 ⇒ spike 可以繼續，不碰黑板。**

| # | 觸發條件 | 對應 `CLAUDE.md` 判準 |
|---|---|---|
| **T1** | candidate 需要**跨幀存活**，且**不只發起者需要知道它** | ①（若因此要進黑板）。<br>⚠️ **單純跨幀不觸發**——`_enterAsFall` 就是跨幀（`CanEnter` 寫、`OnEnter` 讀）的私有裁決，完全合法 |
| **T2** | 出現**第二個消費者**且兩者必須一致（相機要預先取景／動畫要預混／AI 要規劃路徑） | ① |
| **T3** | candidate 需要**參與仲裁**：與 Action／Jump／Roll 競爭同一份 authority，需要共用詞彙（`ArbiterData` 或 FSM 層） | ①／④ |
| **T4** | 需要改 `IMovementModel`／`BaseState.CanEnter` 的**簽名或語意** | ③ |
| **T5** | 新增**黑板 region**（例如 `EnvironmentData`） | ① |
| **T6** | 新增 **StateType**（`TraversalState`）＝改 FSM 拓撲 | ② |

**⇒ 一個能翻越的 spike，可以在 T1–T6 全部不成立的情況下做出來**：
candidate 存成單一元件的私有欄位、走 `JumpState` 已有的 `_enterAsFall` pattern、
位移走既有的 `ExecuteBakedCurveMovement`、不新增 StateType、不碰黑板。
**這就是「prototype 不需要 ADR」的具體路徑。**

⚠️ **但要誠實記住**：這樣做出來的東西 **B4（JumpState 複雜度）會立刻惡化**，
所以它**只能是 spike，不能是交付**。spike 的產出是**答案**，不是程式。

### 5.3 若未來真要 publish candidate，必須先問的 ownership / lifetime / authority

**⛔ 現在不要回答這些，但要知道它們存在**：

| 類別 | 問題 |
|---|---|
| **Ownership** | 誰計算它？一個角色一份還是全域一份？誰是唯一寫入者（`WriterRules` 要加哪一列）？ |
| **Lifetime** | 有效多久？每幀重算還是 latch？誰負責 invalidate？——**`LocomotionStopRuntime.Generation` 已經是這個問題的一個現成答案形狀**（世代號防舊裁決） |
| **Authority** | 承諾之後世界變了（障礙被移走／另一個角色擋住）怎麼辦？是**繼續執行**還是**中止**？<br>gameplay 能不能否決它（capability／status）？——⚠️ 這會撞上 ADR-003 的**三軸互不否決**原則 |
| **形狀** | 一個 candidate 還是一個清單？平手怎麼解？`confidence` 是真需求還是提前設計？ |
| **可快照性** | 需不需要能被 replay／rewind？（ADR-003 D5 對 **mode state** 有這條要求；per-frame candidate 未必適用） |

---

## 6. 學習 / 實作分離路線

### A. Observability scope 的目前裁決

| # | Ticket | 風險 | 為什麼現在 |
|---|---|---|---|
| **O-1** | Direction Authority snapshot | — | **presentation 已撤回**：只保留被動 snapshot／A33，不畫箭頭、不做 HUD／history |
| **O-2** | Foot IK / Ground Probe 可視化（§1.2） | **最低**（資料全在同一檔） | ✅ Game View runtime lines ＋ Scene View Gizmo 已落地，待 Unity Play 驗收 |
| **O-T** | Traversal prerequisite probe（§1.1） | **低**（query 與 snapshot owner 已確立） | ✅ production query＋Game View runtime renderer＋Scene View Gizmo 已落地；兩條 debug 通道只讀 snapshot，待 Unity Play 驗收 |
| **O-3** | Stop / Animation Selection trace（§1.3） | — | **不在目前 scope** |

O-2 與 O-T 均已實作 presentation；兩者只存在於 `UNITY_EDITOR || DEVELOPMENT_BUILD`、不動黑板，
且 debug 都遵守「記錄，不重算」。O-T 的 production query／candidate 本身位於 Core，呈現端只消費快照。

### B. 現在只需要理解 / spike（**產出是答案，不是功能**）

| # | Spike | **要回答的問題**（不是「做出 X」） | 完成即丟 |
|---|---|---|---|
| **S-1** | **Continuous／Committed／Suspended 詞彙落地** | 「Stop 的 committed motion 住在 model 裡、Roll 的住在 state 裡——這兩個該不該一致？」<br>⇒ 產出：`docs/01` 的一節詞彙 ＋ §3.1 對照表。**不改任何程式** | 否（文件保留） |
| **S-2** | **Physics query 工具箱** | 「`Raycast`／`SphereCast`／`CapsuleCast` 的幾何與成本差在哪？`CharacterController` 的 `stepOffset`／`slopeLimit` **實際上**做了什麼？」<br>⚠️ 第二問是真缺口——目前完全倚賴它，**沒有人量過它的邊界** | ✅ 用完即刪 |
| **S-3** | **Candidate 形狀探針** | 「一個**最小**的 traversal candidate 需要幾個欄位才夠？」<br>做法：只寫 query＋classification，**只畫 gizmo、不驅動任何位移**。⇒ 直接產出 §4 第 2/3 層的實際答案，且 **T1–T6 全不觸發** | ✅ 用完即刪 |
| **S-4** | **Motion Warping 語意** | 「`MatchTarget` 與本專案『烘焙曲線 ＋ 程序位移』第三條路的關係是什麼？`ApplyBakedCompensation` 的既有形狀夠不夠用？」 | ✅ |
| **S-5** | *(可選)* **G1 承載問題** | 「一段有始有終的 authoritative 位移該由誰承載？」——**先在 Stop／Pivot 這個簡單案例上回答**，不要在 traversal 這個困難案例上第一次回答 | 否（結論進 `docs/03`） |

⚠️ **S-2／S-3／S-4 都適用 `CLAUDE.md` 的 Spike / Probe Exception**：
用完即丟的探針**不受 Editor Tool 兩道閘門限制**，但**必須明確不進 production path**。

### C. 本輪 benchmark **不展開**的項目（scope 清單）

> ⚠️ **2026-09-12 文案修訂（不改任何裁決，只消除語意歧義）。**
> **舊標題「暫時不要做」造成後續會話誤判**：它被讀成「永久禁令」，於是新的 traversal 提案一碰到 X-1／X-3／X-6
> 就直接停止，而不是去問「這條的解除條件成立了沒」。**本清單從來不是禁令，是「benchmark 當下不展開」的 scope freeze。**
>
> 為了讓下一個會話（含 AI）能正確判讀，**引用本清單時必須先確認狀態欄**，狀態只有五種：
>
> | 狀態 | 意義 | 可以動嗎 |
> |---|---|---|
> | 🧊 **benchmark-freeze** | 只是**本輪 benchmark 不展開**，不代表 roadmap 拒絕 | ✅ 解除條件成立即可動，**不需要回來改本檔** |
> | ⏸ **roadmap-defer** | 目前 roadmap 主動排在後面 | 🟡 要改 roadmap（`docs/03`）才動 |
> | 🔬 **spike-done** | 已做過用完即丟的驗證，結論已在別處 | ✅ 可依結論推進 |
> | 🚧 **in-implementation** | 已正式進入實作 | ✅ 本清單不再管轄 |
> | ⛔ **prohibited** | 真正的禁令（撞架構不變量） | ❌ 需要新 ADR 才能翻案 |
>
> **⛔ 不得把 🧊 讀成 ⛔。** 判斷方式永遠是「解除條件成立了沒」，不是「這裡有沒有寫不做」。

| # | 項目 | 狀態（2026-09-12 核對） | 為什麼 | 解除條件 |
|---|---|---|---|---|
| **X-1** | **完整 traversal / climb 系統** | 🧊 **benchmark-freeze** | 撞上 §2.3 的三個「證據不足」＋ G1 ＋ Motion Warping | S-3／S-4／S-5 有答案之後。⚠️ **部分已解**：`docs/22` §2 給出不依賴 G1 的承載方式；仍未解的只有「執行中可否被中斷」（等戰鬥設計） |
| **X-2** | **新的 Blackboard world layer**（`EnvironmentData` 之類） | 🧊 **benchmark-freeze** | T1–T6 一條都還沒成立。**現在加＝先付治理成本再找用途** | T2（第二個消費者）真的出現時 |
| **X-3** | **新增 `TraversalState`（StateType）** | 🧊 **benchmark-freeze**（⚠️ **但解除條件目前仍未成立**） | 判準 ②（FSM 拓撲）⇒ 需要 ADR。**當時的附帶理由「Trial 名額被 ADR-007 佔用」** | ADR-007 `Accepted`，**或**使用者另行批准一次 Trial 例外（ADR-006 已有先例）。<br>📌 2026-09-12 核對：ADR-006／007 **仍同為 Trial**（見兩檔 §6／§8 的證據欄），故此條**仍然擋著**——但擋點是**治理**，不是技術 |
| **X-4** | **大型 additive animation layer 系統**（影片的 BUFF 疊加） | 🧊 **benchmark-freeze** | ADR-006 Trial 只涵蓋 masked upper-body Spell；additive BUFF selector／stack 仍是新的大範圍 | Spell layering Trial 驗收且出現真實需求 |
| **X-5** | **runtime HUD / `Debug/` asmdef** | 🧊 **benchmark-freeze** | 「第二個使用者出現前不得建立 production abstraction」 | 出現「只有 Player build 才復現」的問題時 |
| **X-6** | **執行期改變碰撞體** | ⏸ **roadmap-defer** | 會動到 `IsGrounded`（黑板欄位、單一寫入者、`MotionDriver` 唯一寫入） | X-1 進場時一併處理。⚠️ **`docs/22` §4.5 論證 Traversal V1 可以先不需要它**——先試「沿牆上升→過頂再前推」，穿模了再開 |
| **X-7** | **改 `IMovementModel` 加生命週期** | 🧊 **benchmark-freeze** | 判準 ③。**目前證據只支持命名概念，不支持改介面**（§3.4） | S-1／S-5 的結論指向它時 |

> ⚠️ **唯一的例外條款**（依使用者指示）：以上任一項若被證明**已經是現有 roadmap 的必要 blocker**，則不受此清單約束。
> **2026-09-10 檢查：沒有任何一項符合。** `docs/03` §6.3 的下一步（步驟 1–3）**不依賴**上述任何一項。
> **2026-09-12 複查：結論不變**，但若使用者把 Traversal 正式排進 roadmap，X-1／X-3 即自動適用本例外條款。

---

## 7. 對 `docs/17-benchmark-3c-demo.md` 的建議修改（✅ **S1–S7 已於 2026-09-10 全數套用**）

| # | 位置 | 現況 | 建議 | 嚴重度 |
|---|---|---|---|---|
| **S1** | §1 時間軸整表 | 章節時間全數偏移。實際（依 5 秒取樣重算）：**0:00–0:35** 基础LOCOMOTION／**0:35–0:50** 跳躍落地／**0:50–1:35** 腳步IK／**1:40–2:00** TRAVERSAL動作／**2:05–4:00** 障礙物檢測 · 調試預覽／**4:00–4:15** 運動碰撞體／**4:15–4:55** 音效／**5:00–5:30** 戰鬥動畫／**5:30–5:55** BUFF／**5:55–6:35** 相機／**6:40–6:55** 二級動畫 | 換掉整個時間欄。<br>⭐ **重點不是精度**：修正後可見 **debug 預覽段約佔全片 28%**、traversal 全段約 37%——原表把它寫成 10 秒的小節，嚴重低估了作者投注的比重 | **中** |
| **S2** | §4.1-L5「缺什麼」欄 | 「⇒ 對方的 traversal 是**跳躍的特化**，不是平行系統」 | 降級為：「**入口回退鏈**是 jump（失敗回退）；⚠️ **不足以推論執行模型從屬**——理由見 `docs/18` §2.2」 | **高**（這是本輪主動更正的推理錯誤） |
| **S3** | §5 ① 表「腳步IK」列 | 「**概念完全對上**」 | 改為：「**雙點採樣**對上；⚠️ **『提前采样脚部落地』沒有對上**——IntentPipeline 採的是**當幀 pre-IK pose**，不做預測性採樣（`FootIKController.SampleGround` 讀 `FootIKPoseData`）。字幕殘句不足以判斷對方指的是預測、預先計算還是別的，**列為未定**」 | **中** |
| **S4** | §4.3-1 | 「環境查詢結果會是**第一份**『角色以外的世界』資料」 | **事實錯誤**。`CombatContextData.TargetPosition` 已經是黑板裡的 world-derived 資料。改為：「**第一份需要跨幀存活、參與 selection、被多方消費的**世界資料」，並補上 `CombatContext` 作為既有先例（判準見 `docs/18` §5.1） | **高** |
| **S5** | §3.4 表 #10 Ground Normal | 標 🔴（缺口） | 改為 🟡：**不是缺口，是刻意未發布**——今天只有一個消費者，符合「局部 query implementation detail」。⛔ 不應因為「想看見它」而發布它（`docs/18` §5.1） | **中** |
| **S6** | §7-F3 | 同上，語氣偏向「缺陷」 | 同 S5 校準；保留「它是環境資訊缺席的最小前哨」這個判斷，那部分成立 | 低 |
| **S7** | §6 學習順序 #2 | 「Unity query 工具箱」 | 補一句：**`CharacterController.stepOffset`／`slopeLimit` 的實際行為是真缺口**——目前完全倚賴，沒有人量過邊界（`docs/18` §6-S-2） | 低 |

✅ **2026-09-10：使用者裁決「可以套用」，S1–S7 已全數寫入 `docs/17`。**
S2／S4（高嚴重度，皆為**推理錯誤的主動更正**）另在 `docs/17` 檔頭加了修訂提示；
S3 的「未定」結論已進 `docs/17` §8；S5／S6 的嚴重度標記已由 🔴 校準為 🟡／🟢。
⛔ **未動 roadmap**（`docs/03` 不受影響）——這是套用時的明確界線。

> 📌 **順帶 fold back**（同輪）：`docs/17` §7-F1 已由 🔴 改為 🟢 **已解決**——
> 該條記載的 211 筆未 commit 工作已由使用者於同日 commit `71f5a5c`，風險陳述失效。

---

## 8. Findings（本輪新增）

> 承 `docs/17` §7 的 F1–F6，本輪新增 F7–F9。**只回報，不裁決。**

### F7 — ✅ **已 fold back（2026-09-10）** ~~🔴 `docs/02-dev-spec.md` §1.1 的 `MoveDirection` 型別與程式不符~~

> **處置**：dev-spec §1.1 的程式碼區塊與權限表型別欄皆已改為 `Vector3`，並註明世界化語意（ADR-007 S1）。
> ⇒ **ADR-007 翻 `Accepted` 的這項阻礙已排除。**

- **文件**（§1.1 程式碼區塊與權限表）：`public Vector2 MoveDirection { get; set; }`，型別欄寫 `float`／**`Vector2`**／`float`
- **程式**（`PlayerRuntimeData.cs`）：`public Vector3 MoveDirection { get; set; }`
- **成因**：ADR-007 **S1「移動方向世界化」** 已於 2026-09-08 落地（`MotionDriver.ExecuteBaseMovement` 已改讀 `data.MoveDirection`，不再是 `transform.forward`），dev-spec §1.1 未跟上。
- **為什麼是問題**：dev-spec §1 是 `CLAUDE.md` 明訂「**永遠留在 dev-spec 的跨領域契約**」之一，而型別欄正是別人會信任的地方。而且**同一個程式碼區塊在 2026-09-10 才被更新過**（`VerticalVelocity` 那幾行有當日註記）——不是「文件普遍陳舊」，是**這一處被漏掉**。
- **狀態**：ADR-007 仍是 **Trial**，`CLAUDE.md` Fold-back 規則允許 Trial 期短暫 code-first ⇒ **現在不算違規**，但**在 ADR-007 翻 `Accepted` 之前必須 fold back**。

### F8 — ✅ **已 fold back（2026-09-10）** ~~🟡 `CombatContext` 在 `WriterRules` 有、在 dev-spec §1.1 沒有~~

> **處置**：dev-spec §1.1 已補 `CombatContext` 欄位（程式碼區塊 ＋ 權限表列），
> §2.1 已補**順序 2.6（Combat Context Producer）**與**順序 4.6（CharacterFacingSource）**兩列，
> 兩列都寫明「為什麼只能在這個夾縫」的時序理由。子系統細節仍指向 `docs/15-combat-context.md`。
> ⇒ **人／機兩邊的欄位表重新對齊，漂移關閉。**

- `ArchitectureRegressionTests.WriterRules` 有 `CombatContext → PlayerCombatContextSource.cs`（Owner 註明 ADR-007 D3）。
- dev-spec §1.1 權限表列了 Intent／MovementIntent／Movement Output／CurrentWeapon／Arbitration／PresentationEvents／IsGrounded／VerticalVelocity／JustLanded・JustLeftGround —— **沒有 CombatContext**。
- 同理，dev-spec §2.1 的管線順序表**沒有 2.6（Combat Context Producer）與 4.6（CharacterFacingSource）**，而兩者都在 `CharacterPipelineRunner.Update()` 裡。
- ⚠️ **公平地說**：`docs/15-combat-context.md` **有**完整記載 2.6／4.6（§145／§156／§303）⇒ 契約**有被寫下來**，只是寫在子系統分卷而非跨領域卷。
- **這正是那條「刻意重複」的規則在起作用**：`CLAUDE.md` 要求「新增黑板欄位或改所有權時，必須**同步**改 §1.1 權限表與測試的規則表（刻意重複，讓漂移現形）」——**漂移已經現形，機器那半邊先到了。**
- **狀態**：與 F7 同屬 ADR-007 Trial 的 fold-back 範圍。

### F9 — 🟢 `LocomotionStopRuntime` 是一套完整的 committed-motion 生命週期，但沒有任何文件說它是

- 該 struct 具備 `Begin`／`BeginPending`／`StartPlaying`／`Advance`／`TryRequestCompletion`／`Invalidate`／`HasTimedOut`／`HasPendingTimedOut` ＋ `Generation` 世代號 ＋ `TargetNormalizedTime`。
- `docs/07-locomotion-transitions.md` 描述的是「收步」這個**功能**；沒有任何地方指出**它同時是一個可複用的承諾式運動生命週期模型**。
- **不是缺陷**——是本輪 §3 想命名的東西的**最好證據**，也是 §3.1 那張表最後一列的來源。列在這裡是為了讓它不再只存在於程式裡。

### F10 — 🟡 `docs/02-dev-spec.md` §7.1 的自動項清單**漏了 9 條已存在的架構測試**（2026-09-10 新增，**只回報不裁決**）

- **程式**（`ArchitectureRegressionTests.cs`）實際宣告：`A1 A2 A3 A4 A5 A9 A10 A11 **A12** A13′ **A14 A15** A16 A19 A20 A21 A22 A23 A24 A25 A26 **A27 A28 A29 A30 A31 A32** A33`
- **dev-spec §7.1 表**列出：`A1–A5`（A6／A7／A8 住在 `MovementIntentTests`，屬正常）、`A9 A10 A11 A16 A13′ A19–A26`，以及本輪補上的 `A33`
- ⇒ **漏記 9 條**：`A12`、`A14`、`A15`、`A27`、`A28`、`A29`、`A30`、`A31`、`A32`。
  其中 **`A31`（`CharacterFacingSource` 是唯一 facing request 送出者）與 `A32`（`MotionDriver` 不決定 facing）
  正是 ADR-007 D3 的兩條核心不變量**——它們在機器那邊守得好好的，在人讀的清單裡卻不存在。
- **為什麼是問題（本 repo 特有）**：`CLAUDE.md` 的 Context Discipline 明白把 §7.1 表與 `WriterRules`／`LayerRules`
  一起當成「**最便宜的正確架構摘要**」。摘要漏掉三分之一，就不再能被信任為摘要——
  下一個會話會以為 facing authority 沒有測試守著。
- **狀態**：**不是本輪造成的**（本輪只新增 A33 並已登記）。**⛔ 未自行補寫**——
  逐條補要讀 9 支測試並精確描述，屬獨立工作包，依本輪界線交由使用者裁決是否排程。

---

## 附錄 — 本輪的磁碟核對清單（可重跑）

本文所有「程式現況」敘述皆來自 2026-09-10 對以下檔案的直接讀取：

```
Core/StateMachine/BaseState.cs              Core/Movement/Models/IMovementModel.cs
Core/StateMachine/States/RollState.cs       Core/Movement/Models/LocomotionModel.cs
Core/StateMachine/States/JumpState.cs       Core/Movement/Models/LocomotionStopRuntime.cs
Core/StateMachine/States/ActionState.cs     Core/Movement/Models/LocomotionStopSelector.cs
Core/StateMachine/FullBodyStateMachine.cs   Core/Facing/CharacterFacingSource.cs
Core/Pipeline/CharacterPipelineRunner.cs    Core/Actions/{IActionLifecycleSink,ActionReleaseContext,IAimSource}.cs
Presentation/Motion/MotionDriver.cs         Presentation/IK/FootIKController.cs
Presentation/Animation/AnimancerFacade.cs   _Project/Tests/EditMode/ArchitectureRegressionTests.cs
所有 *.asmdef                                docs/02-dev-spec.md §1.1／§2.1
```
