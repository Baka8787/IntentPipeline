# 42 — Near-Floor Following / Step-Down 研究輪（2026-09-23，研究性質，⛔ 未實作、未裁決）

> **這份文件是什麼**：使用者在下樓梯 jitter 排查後明確要求——**不要針對 Stairs 寫特例**，
> 改參考 Unreal `FindFloor` / `MoveAlongFloor` / `FStepDownResult` 的責任劃分，研究
> `MotionDriver` 是否缺少一個**通用的** near-floor following／step-down 層。
> 本輪只做兩件事：①**量測**現況缺口，②**提出最小方案**。**不實作、不裁決**。
> 🔄 **2026-09-23 續**：使用者裁決 Q1／Q2（§6.0）後，對「單次 Move 加下壓分量」提出一個
> blocking question 並要求先 Spike——**已用 `unity cmd eval_file` 在隔離 preview scene 跑 4 組
> `CharacterController.Move()` 對照實驗回答（§6.1）：原方案不是 probe，會在懸崖邊緣造成一次性
> 單幀瞬移，且原本的 gating 條件會吃掉主動跳躍。§6.2 是修正後的 Query→Apply 兩段式方案。**
>
> 對應程式：`Assets/Scripts/Presentation/Motion/MotionDriver.cs`
> （`ExecuteBaseMovement`／`ExecuteVerticalOnlyMovement`／`GetGravityThisFrame`／`SyncGroundedState`）。
> 銜接文件：`docs/31`（Jump/Falling/Landing，剛修過的 grace 過濾屬事件層，不是本輪的運動層）、
> `docs/38`（Valid Standing Support，處理的是「邊緣部分懸空」，與本輪問題**方向相反**，見 §5）、
> `docs/29`（Foot IK 的閘門即讀 `IsGrounded`，本輪缺口的下游症狀之一）。

---

## 0. 一句話結論

**我們現在完全沒有「Walking 模式」——`ExecuteBaseMovement` 永遠等同 UE 的 `PhysFalling`：
水平與垂直在同一次 `CharacterController.Move()` 裡彈道式（ballistic）積分，沒有任何主動的
「往下探一小段、找到就直接貼齊」機制。** 下樓梯的 jitter、上次修的落地聲/Falling 動畫閃爍，
是同一個缺口的三個下游症狀，不是三個獨立 bug。

---

## 1. UE 的責任劃分（已用 WebSearch/WebFetch 核對官方文件與原始碼摘錄）

UE `UCharacterMovementComponent` 把「貼地移動」拆成三層，彼此不互相知道對方的實作細節：

| 層 | 函式 | 職責 | 關鍵字 |
|---|---|---|---|
| **Query（查）** | `FindFloor(CapsuleLocation, OutFloorResult, bCanUseCachedLocation, DownwardSweepResult)` | **純查詢**：對膠囊向下掃一次，回報目前站的面是什麼、距離多遠、能不能站；**不改變任何狀態** | [官方 API 文件](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/UCharacterMovementComponent/FindFloor)："Sweeps a vertical trace to find the floor for the capsule at the given location" |
| **Apply（用）** | `MoveAlongFloor` → `StepUp` → `AdjustFloorHeight` | 先沿目前已知的地面移動水平分量；卡到台階呼叫 `StepUp`；最後 `AdjustFloorHeight` 用一個**遲滯帶** `[MIN_FLOOR_DIST, MAX_FLOOR_DIST] = [1.9, 2.4]`（UE 單位＝公分）判斷要不要把膠囊 Z **直接改位置**貼到地面——這是**位置修正，不是速度／重力**的事 | 常數經 WebSearch 核對：`MIN_FLOOR_DIST=1.9f`／`MAX_FLOOR_DIST=2.4f`（定義於 `CharacterMovementComponent.cpp`） |
| **Fallback（放棄）** | `PhysFalling` | 只有 `FindFloor` 在搜尋距離內都找不到可站的面時，才會離開 Walking、進入彈道重力積分 | —— |
| **效能細節** | `FStepDownResult` | `StepUp` 內部本來就會做一次向下掃描判斷落點；把那次掃描的結果**轉發**給 `MoveAlongFloor`／`AdjustFloorHeight`，讓它們**不必重掃一次**。純粹是「同一幀內兩處都需要地面資訊時避免重複 sweep」的轉發結構，不是額外的職責層 | [VRExpansionPlugin 原碼摘錄](https://docs.pixovr.com/UnrealEngineSDK-Demo/_v_r_character_movement_component_8cpp_source.html)：`if (StepDownResult.bComputedFloor) { CurrentFloor = StepDownResult.FloorResult; } else { FindFloor(...); }` |

**兩個遲滯帶的常數不要混淆**（這是我第一次查證時差點搞混的地方，特此記錄）：
- `[MIN_FLOOR_DIST, MAX_FLOOR_DIST] = [1.9, 2.4] cm`——**只是**在幾乎平地時避免「浮空 2mm／插入 2mm」這種浮點雜訊造成的無意義修正，量級極小。
- **真正決定「能不能吃掉一階樓梯」的是 `MaxStepHeight`**（UE 預設 45 單位＝45cm）——這個值同時是 `FindFloor` 向下搜尋的距離上限，也是 `StepUp` 的高度上限。社群踩坑貼（[bugnet.io](https://bugnet.io/blog/how-to-fix-unreal-character-movement-jitter-on-slope-with-step-down)）證實：**下樓梯／陡坡 jitter 的標準修法就是拉大 `MaxStepHeight` 與 floor sweep distance**，原文："the capsule keeps losing floor contact and re-snapping"⇒"vertical buzzing and animation flickering between walk and fall cycles"——**跟我們這次的症狀逐字對應**。

---

## 2. Unity 官方是否有同一個概念？——有，而且命名幾乎直譯

Unity 自己的（DOTS／ECS 實驗性）`com.unity.charactercontroller` 套件把同一件事叫
**`SnapToGround` + `GroundSnappingDistance`**（[`KinematicCharacterProperties` API](https://docs.unity3d.com/Packages/com.unity.charactercontroller@1.0/api/Unity.CharacterController.KinematicCharacterProperties.html)）：

> `GroundSnappingDistance`（float）："Distance to snap to ground, if SnapToGround is enabled"
> 概念說明："the character will adjust its position to always stick to the ground surface,
> instead of launching off into the air when encountering downward slope changes."

⇒ **這不是 UE 專屬的架構癖好，是「膠囊型角色控制器」這個問題域裡的通用解法**，只是我們用的
舊版 `UnityEngine.CharacterController`（非 DOTS 套件）沒有內建。

**我們自己的 `CharacterController.stepOffset` 對下樓完全沒有幫助**——查證 Unity 官方文件與社群討論串
（[討論串](https://discussions.unity.com/threads/charactercontroller-and-walking-down-a-stairs.101859/)）
結論一致：`stepOffset` 的官方語意只涵蓋「往上踩」；下樓的標準 workaround 是**主動加一段下壓位移**
（"Force player downwards the distance of a step as defined by the character controller"），
本質上就是簡化版的 UE `AdjustFloorHeight`。

> 🔄 **2026-09-23 修正**：本段原本樂觀地推論「不需要重蓋一整套 UE 分層，只需要在既有的
> `characterController.Move()` sweep 前面多塞一段有界的向下分量，讓 Unity 自己的碰撞掃描『順便』
> 把下一階找到」——**這個判斷已被 §6.1 的 Spike 推翻**：`characterController.Move()` 的 sweep 找不到
> 面時**不會「什麼都不做」，而是無條件把請求的下壓距離全額提交成真實位移**（懸崖邊緣會造成一次性
> 單幀瞬移）。UE 分成 `FindFloor`（Query）／`AdjustFloorHeight`（Apply）兩段**不是可以省略的架構
> 潔癖，是必要的**——我們一樣需要一次獨立、非提交式的 `Physics.CapsuleCast` 當 Query 層，
> 才能安全地只在「查到面」時才提交下壓。見 §6.1／§6.2 的修正版方案。

---

## 3. 我們的 `MotionDriver` 現況量測（code-proven，逐行對照）

### 3.1 水平與垂直在同一次 Move 裡彈道式積分——沒有 Query 層

[`ExecuteBaseMovement`（MotionDriver.cs:471-495）](Assets/Scripts/Presentation/Motion/MotionDriver.cs:471)：
```csharp
Vector3 horizontalVelocity = data.MoveDirection * (data.MoveSpeed * moveSpeed);
Vector3 finalMovement = horizontalVelocity + GetGravityThisFrame(data);
characterController.Move(finalMovement * Time.deltaTime);
```
水平分量與 `GetGravityThisFrame` 回傳的垂直分量**合併成一次 Move**——沒有「先查地面在哪、
決定要不要貼齊」的獨立步驟。這就是 UE 語彙裡的 `PhysFalling`（彈道），我們**沒有** `PhysWalking`
（主動貼地）這個對應狀態。

### 3.2 `IsGrounded` 是上一幀 Move 的**副作用**，不是本幀決策的輸入

[`GetGravityThisFrame` / `SyncGroundedState`（MotionDriver.cs:2280-2349）](Assets/Scripts/Presentation/Motion/MotionDriver.cs:2280)：
```csharp
SyncGroundedState(data);   // 讀 characterController.isGrounded —— 上一次 Move() 的碰撞結果
if (data.IsGrounded && _verticalVelocity < 0f)
{
    _verticalVelocity = reboundForce;   // −2，貼地力
}
else
{
    _verticalVelocity += _activeGravity * Time.deltaTime;   // 彈道積分，無上限
}
```
一旦上一幀 `isGrounded` 翻成 false（哪怕只是台階邊緣的單幀雜訊），本幀就從 `reboundForce(−2)`
開始**無界彈道積分**，沒有任何「往下探一段距離、找到了就直接貼齊」的機制。這正是 UE 缺了
`AdjustFloorHeight` 時會發生的行為。

### 3.3 三個症狀，同一個根因

| 症狀 | 你觀察到的位置 | 直接原因 | 都指向同一件事 |
|---|---|---|---|
| 高頻率落地聲 | `AudioController`（已修，見 `docs/31`） | `JustLanded` 無去抖動的邊沿偵測 | 上游 `isGrounded` 本來就每階閃一次 |
| 反覆進 Falling 動畫 | `JumpState`（已調 `fallEntryGrace`，見 `docs/31`） | 連續離地時間超過 grace | 同上，只是換一個消費者 |
| **Walk 下樓梯明顯抖動**（本輪） | `FootIKController`（[docs/29 §D 已知限制](docs/29-foot-ik-architecture-guide.md)：`PelvisOffsetY` 上下樓梯骨盆抽動） | `ikAllowed = data.IsGrounded` 每階翻動，`targetPosition` 無平滑直接跳（[FootIKController.cs:326-347](Assets/Scripts/Presentation/IK/FootIKController.cs:326)） | 同上，第三個消費者 |

上兩個已經用**事件層 grace** 修掉（不動 `IsGrounded` 本身，只延遲事件確認）。但 Foot IK 讀的是
`IsGrounded` 本身（[docs/29 §J](docs/29-foot-ik-architecture-guide.md)：「`IsGrounded` 是 IK 的閘門，
不是 IK 的輸出」），**事件層 grace 對它沒有幫助**——這也是為什麼你在音效/FSM 都修好之後，
Foot IK 層的抖動依然存在。**要根治第三個症狀，必須讓 `IsGrounded` 本身在下樓梯時不要閃，
而不是再找一層 grace 去擋它的下游。**

### 3.4 為什麼 Walk 比 Run 更誇張（本輪新量測，物理模型）

| | Walk | Run |
|---|---|---|
| 世界水平速度（`docs/33` 已驗算） | 2.1917 m/s | 4.6963 m/s |
| 樓梯 `run`（踏面深度，`docs/35` 實測） | 0.3 m | 0.3 m |
| 單階水平跨越時間 | **0.137 s** | **0.064 s** |
| `WeightSmoothSpeed`（[FootIKSettings.cs:50](Assets/Scripts/Presentation/IK/FootIKSettings.cs:50)＝8）跑滿 0→1 所需時間 | 0.125 s | 0.125 s |

Walk 的單階跨越時間（0.137s）**長於**權重跑滿一趟所需的時間（0.125s）⇒ 每一階，權重都有
充裕時間**完整跑完** 0→1→0，你看到的是乾脆、完整的「腳/骨盆瞬間歸位」。Run 的單階跨越時間
（0.064s）**短於** 0.125s ⇒ 權重根本來不及掉到 0 就又被拉回 1，停在中段小幅擺動，視覺上被
自然低通掉——**底層的 `IsGrounded` 閃爍頻率可能一樣高甚至更高，只是 Run 沒給權重時間把每一次
閃爍完整演出來**。這與 UE 那篇踩坑貼的機制描述（capsule 反覆失去/重新貼地 ⇒ 在 walk 與 fall
動畫之間視覺震盪）是同一件事，只是我們的「動畫層」多了一個權重平滑，把 Run 的震盪掩蓋掉了，
但 Walk 沒有。

⚠️ **這組數字是理論推導（tread crossing time vs weight-smooth time），不是本輪直接量到的
walk-vs-run 落地事件頻率**——舊 Console log（2026-09-22 場次）沒有標記當時是哪個步態，
無法回溯拆分。**若要精確驗證，需要下一輪分別在 Walk／Run 各錄一段 `recordDebugTrace`
並標記步態**（見 §6 待裁決 Q5）。

---

## 4. 缺口總表

| UE 概念 | Unity `com.unity.charactercontroller` 對應 | 我們現況 | 缺口 |
|---|---|---|---|
| `FindFloor`（查詢） | `EvaluateGrounding` + 向下 sweep | ❌ 沒有獨立查詢——`isGrounded` 只是 `Move()` 的副作用 | **完全缺** |
| `MoveAlongFloor` + `AdjustFloorHeight`（有界貼齊） | `SnapToGround` + `GroundSnappingDistance` | ❌ 沒有——垂直分量只有彈道重力 | **完全缺** |
| `MaxStepHeight`（上下都用的邊界） | 無對應欄位查到（該套件文件頁未列出） | `characterController.stepOffset = 0.3`，但**只對「往上」有效**（Unity 官方語意＋社群共識） | **有欄位、語意不對稱，不能直接重用** |
| `PhysWalking` vs `PhysFalling`（模式切換） | 隱含在 `SnapToGround` 開關 | ✅ **意外地已經有對應的方法切換**：`ExecuteBaseMovement`（一般行走）vs `ExecuteVerticalOnlyMovement`（`DeathState`／`HurtState`／Jump 空中段專用，[呼叫點清單](Assets/Scripts/Core/StateMachine/States/JumpState.cs:328)） | **不是缺口，是現成的掛載點**（見 §6） |
| `FStepDownResult`（避免重複 sweep） | —— | 🔄 **§6.1 Spike 後變成真實顧慮**：Query（`Physics.CapsuleCast`）＋ Apply（`characterController.Move()` 自己的 sweep）現在是兩次獨立的向下探測，同一幀可能重複掃描同一塊地——UE 用 `FStepDownResult` 轉發結果來避免；我們的最小方案第一版先接受這個重複成本（見 §6.4 Q3 的效能角度未列，補在此），要不要做等價的「轉發」留給 prototype 量出實際成本後再決定 | 不是本輪的阻斷項，但不再是「不適用」 |

---

## 5. 為什麼這跟 `docs/38`（Valid Standing Support）不是同一件事、甚至方向相反

`docs/38` 研究的是「**膠囊已經站在一個邊緣上，要不要因為腳掌部分懸空就判定不可站**」——那次
使用者裁決 **Deferred / Accepted Limitation**，理由是「否決層只能改變狀態、不能改變位置」，
硬加否決反而讓角色卡在牆角動不了（實測 Δy=0.000）。

**本輪要研究的方向完全相反**：不是「要不要更嚴格地判定不可站」，而是「**站得住的下一階，
要不要主動把角色帶過去，而不是讓它先彈道式掉下去再被動接住**」——這是**位置修正**，
不是狀態否決，因此不會撞到 `docs/38` §13 的「否決層只能改狀態不能改位置」那條反對理由。
换句話說：`docs/38` 反對的是「該踩的地方判定成不能踩」，本輪要做的是「不該掉的地方不要讓它掉」，
兩者不衝突。

---

## 6. 最小方案（提案，⛔ 未實作）

### 6.0 使用者裁決（2026-09-23）

- **Q1**：`maxStepDownDistance` 放 `MotionDriver` 的 Grounding/Movement 設定區（不進黑板）。
  Prototype 先用 **0.22–0.25m**，以目前樓梯實測 0.195m 為驗證基準，**不視為最終 tuning 值**。
- **Q2**：靜止不生效。**第一版只在「上一幀已 grounded」且「本幀存在有效 horizontal locomotion」
  時允許 step-down assist**，避免破壞 `docs/38` 的 edge-standing 裁決。

### 6.1 Blocking Question 的 Spike 結果（2026-09-23，用完即丟，程式已刪）

> 使用者在拍板 Q1／Q2 之後，明確指出 §6.1 原稿（「單次 Move 加下壓分量」）有一個沒驗證的假設：
> **這個下壓如果在 `maxStepDownDistance` 內找不到面，它到底會不會被真的提交成位移？**
> 若會，就不是「純 probe」，而是每一幀都在冒著「莫名其妙往下多移動一截」的風險。
> 這裡用 `unity cmd eval_file` 在一個隔離的 Editor preview scene 裡直接跑 4 組
> `CharacterController.Move()` 對照實驗回答，不進正式測試套件、跑完即刪。

**直接回答：會，而且是無條件全額提交。**

| 測試 | 設置 | 結果 |
|---|---|---|
| **①單次 Move 下壓，懸崖（範圍內完全沒有面）** | 要求下壓 −0.25m，正下方到 −∞都是空的 | `actualDeltaY = −0.25000`（**逐位元等於請求值，全額提交**），`isGrounded` 之後為 `false` |
| **②Query→Apply，同一個懸崖** | 先用 `PhysicsScene.CapsuleCast`（隔離場景、非全域 `Physics.*`）探 0.25m，沒找到才退回原本的貼地力 `-0.033m`（一幀 `reboundForce` 的量級） | `queryFoundSurface = False` ⇒ 套用 fallback，`actualDeltaY = −0.03300`——**與現行已上線行為逐位元一致，沒有額外下墜** |
| **③naive 夾值套用在「跳躍起跳」那一幀** | 起跳當幀真實應為 `+0.09427`（向上，`Bake_Jump_place_ALL_short` 初速 5.6564 m/s × dt），若不分方向直接套用「至少 −0.25m」的夾值邏輯 | `actualDeltaY = 0.00000`——**起跳被整個吃掉**，角色停在原地不動，不是變矮或變慢，是完全沒跳 |

**結論**：
1. **①證實「單次 Move 下壓」不是 probe，是真實位移**——在 `maxStepDownDistance` 找不到面的那一幀，
   角色會被**無條件**往下移動 0.22–0.25m。這在懸崖邊緣是一次性的視覺瞬移（約 60fps 下相當於單幀
   13–15 m/s 的下墜速度），比現行「平滑加速的彈道墜落」更突兀，**不能接受**。
2. **②證實「先查、找到才套用，找不到就退回現行小位移」是安全的**——`queryFoundSurface=False`
   時退回的 `-0.033m` 與現行 `reboundForce` 貼地力（[MotionDriver.cs:162](Assets/Scripts/Presentation/Motion/MotionDriver.cs:162)＝−2 m/s）在
   60fps 下的單幀量級完全一致，**逐位元不改變懸崖邊緣的現有行為**。
3. **③是本輪額外揪出的第二個坑**：§6.1 原稿只寫了「grounded 條件」，沒寫「方向條件」。
   起跳當幀 `data.IsGrounded`（上一幀）確實還是 `true`（起跳前搖仍在地上），如果 assist 邏輯只檢查
   grounded、不檢查**這一幀重力積分出來的垂直分量本來是不是已經 ≤ 0**，就會把主動起跳的正向速度
   直接改寫成負的，等於**讓角色永遠跳不起來**。

⇒ **§6.1 的核心構造從「單次 Move 加下壓分量」改為「Query → Apply 兩段式」，且 Apply 前必須額外檢查
「本幀重力積分出來的垂直分量已經 ≤ 0」（不只檢查 grounded）。**Unity `CharacterController.Move()`
把 Query 與 Apply 合一的簡化空間**不成立**——這點推翻了本文件 §2 原本對 UE 「不需要另外寫 Query
函式」的樂觀判斷，**必須有一次獨立、非提交式的向下 Query**（`Physics.CapsuleCast`，遮罩與
`FootIKSettings.GroundLayers` 或 `characterController` 自身的碰撞遮罩一致）。

**方法論副產物（記錄避免下次重踩）**：`CharacterController.Move()` 不會查詢
`EditorSceneManager.NewPreviewScene()` 的隔離 `PhysicsScene`（它似乎只認全域／目前開啟場景的物理
世界），即使物件本身用 `MoveGameObjectToScene` 放進 preview scene 也一樣。用 preview scene 隔離測
`CharacterController.Move()`（而非顯式 `PhysicsScene.CapsuleCast`／`Raycast`）**會誤中目前開著的真實
場景幾何**，得到看似合理但其實是量到別的東西的數字。本輪吃過一次虧（Test 3 的「合理」殘留值後來
證實是撞到真實場景，不是合成的樓梯平台）。**下次要獨立驗證 `CharacterController.Move()` 對合成幾何
的反應，只能用 Play Mode 的 `SceneManager.CreateScene(LocalPhysicsMode.Physics3D)`，或直接借用目前
開著場景裡的真實樓梯（`HideAndDontSave` 臨時角色，不存檔）。**

### 6.2 修訂後的核心構造

在 `ExecuteBaseMovement`（**只有這個方法**，不動 `ExecuteVerticalOnlyMovement`）合併水平/垂直分量
之前，插入一段**兩階段**流程，只有同時滿足才生效：
1. 上一幀 `data.IsGrounded == true`；
2. **本幀 horizontal locomotion 有效**（`data.MoveDirection.sqrMagnitude` 不為零，呼應 Q2）；
3. **本幀 `GetGravityThisFrame` 算出的垂直分量已經 ≤ 0**（不是正在主動起跳／被向上彈飛）。

三者同時成立才做：
- **Query**：從目前膠囊位置，用與 `FootIKController.SampleGround` 同精神但獨立的一次
  `Physics.CapsuleCast`（非 `Move()` 內建 sweep），向下探 `maxStepDownDistance`（0.22–0.25m，見 §6.0 Q1）。
- **Apply**：查到 ⇒ 把本幀垂直分量**設為查到的實際距離**（不是盲設 `maxStepDownDistance`）；
  沒查到 ⇒ **完全不介入**，照舊吃 `GetGravityThisFrame` 的彈道值——這正是 Spike ②驗證過的
  「安全退化＝現行行為」路徑。

之後才丟給 `characterController.Move()`。**Query 用的是同一個 `characterController` 的碰撞遮罩／
半徑／高度**，不是另開一套幾何定義，維持「單一真相」。

### 6.3 明確排除的做法（避免重蹈覆轍，含本輪新增兩條）

- ⛔ 不修改 `IsGrounded` 的寫入語意本身（它仍然＝`characterController.isGrounded`，
  `docs/38` §13 的裁決維持不變）——本方案是讓**真實的碰撞結果**因為多探了一點距離而變成
  「有碰到」，不是在既有結果上加否決/加分。
- ⛔ 不在 Foot IK／Audio／FSM 任何一層加 fade／權重修正去掩蓋這個斷點——那正是
  `docs/29` §I 明文禁止的手法。本方案要修的是**斷點本身在運動層是否存在**，不是下游怎麼
  平滑一個仍然存在的斷點。
- ⛔ 不重用 `characterController.stepOffset` 當作下探距離——它的官方語意只保證「往上」，
  混用會讓這個欄位變成「兩個方向都要改就要一起改」的隱性耦合。
- 🆕 ⛔ **不做「單次 Move 加無條件下壓分量」**——Spike ①已證實這在懸崖邊緣會造成一次性的
  單幀瞬移，不是安全的簡化。必須是 Query（非提交）→ Apply（提交）兩段式。
- 🆕 ⛔ **assist 的 gating 條件不能只看 `IsGrounded`**——必須額外檢查本幀垂直分量的正負號
  （Spike ③）。否則主動起跳／`ApplyJumpLaunch` 剛注入的正向速度會被寫死成負值，角色永遠跳不起來。

### 6.4 待裁決 / 待量測（下一輪要先回答，才能談實作）

| # | 問題 | 狀態 | 為什麼要先問 |
|---|---|---|---|
| ~~Q1~~ | `maxStepDownDistance` 放哪、預設多少？ | ✅ **已裁決**（§6.0）：`MotionDriver` 設定，prototype 0.22–0.25m | — |
| ~~Q2~~ | assist 該不該在原地站立時生效？ | ✅ **已裁決**（§6.0）：不生效，只在有效 horizontal locomotion 時允許 | — |
| **Q3** | 與 `RootReachConstraint`／Dynamic Capsule Offset（`docs/27`／`docs/28`）／Traversal Collision Profile 是否有交互？ | ✅ **已查**（§6.5.1）：無交互，三者互斥執行 | 這三者也會在同一幀動 `characterController.center`／位移，本輪未讀那幾份程式碼確認相容性 |
| **Q4** | `fallEntryGrace`／`groundEdgeEventGrace`（這次剛調的兩個 grace）要不要保留？ | 建議保留 | 它們是事件層的防禦，即使運動層不再閃，仍能擋住「其他角色沒套用這個 assist」「極端幾何」等邊界情況，屬於 defense-in-depth，不衝突 |
| **Q5** | 需不需要先補一輪「Walk／Run 分開標記」的 Console 量測，把 §3.4 的理論模型換成實測？ | 待查 | 目前 walk-vs-run 的解釋是推導，不是量到的；實作前建議至少用 `recordDebugTrace` 各錄一段驗證量級對不對 |
| **Q6** | 敵人（`AIMovementSource` 也走 `ExecuteBaseMovement`）要不要一起吃到這個 assist？ | 待查 | `MotionDriver` 是共用類別，理論上會自動套用；需要確認敵人的樓梯使用情境是否有不同需求 |
| **Q7** | Query 用的 `Physics.CapsuleCast` 遮罩該對齊哪一份既有設定？（`FootIKSettings.GroundLayers`？`characterController` 自身碰撞遮罩？另開一份？） | ✅ **已查**（§6.5.2）：獨立新欄位，預設 `~0` | Spike 為求簡化用 `~0`（全遮罩），正式實作不能這樣做 |
| **Q8** | Query→Apply 對「真實樓梯」的貼齊平滑度，需要在 Play Mode 或真實開啟場景中驗證——本輪 Spike 受限於 `CharacterController.Move()` 不認 preview scene 隔離物理，只驗證了「找不到面時安全退化」，**沒有**驗證「找到面時貼齊是否平滑無感」 | 待驗 | 見 §6.1 方法論副產物；下一輪需要 Play Mode 或借用真實場景幾何 |

### 6.5 Q3／Q7 查證結果（2026-09-23，讀碼，非 Spike）

#### 6.5.1 Q3：與其他 `characterController.center`／位移寫入者的交互——**無交互，三者互斥**

讀 [MotionDriver.cs:559-636](Assets/Scripts/Presentation/Motion/MotionDriver.cs:559)（`BeginTraversalCollisionProfile`／`UpdateCapsuleOffset`）與
[TraversalState.cs:376-413](Assets/Scripts/Core/StateMachine/States/TraversalState.cs:376) 後確認：

1. **Traversal Collision Profile 與 Dynamic Capsule Offset 已經是單一寫入者、彼此已有 guard**：
   `UpdateCapsuleOffset` 開頭就是 `if (_traversalCollisionProfileActive) return;`（程式註解自己寫著
   「兩個寫入者同幀寫同一個欄位＝後寫的贏，行為取決於呼叫順序」，所以刻意用旗標讓其中一個直接讓路）。
   這不是本輪才發現的問題，是既有架構已經解決的問題。
2. **`UpdateCapsuleOffset(data)` 已經是 `ExecuteBaseMovement` 的第一行**
   （[MotionDriver.cs:479](Assets/Scripts/Presentation/Motion/MotionDriver.cs:479)）——只要把 Query
   步驟放在它**之後**（合併水平/垂直分量**之前**），Query 用的 `characterController.center/radius/height`
   自動是本幀姿勢調整過的正確值，不需要額外同步。
3. **Traversal Collision Profile 只在 `TraversalState` 執行期間有效**，而 `TraversalState` 有自己獨立的
   執行路徑（不呼叫 `ExecuteBaseMovement`，見 §4 表格）——兩者在狀態機層級就是互斥的，
   我的 assist 只掛在 `ExecuteBaseMovement`，永遠不會跟 Traversal Collision Profile 同一幀執行。
4. **`RootReachConstraint` 根本不是 `characterController.center`／位移的寫入者**——它是
   Traversal Warp Plan 求解階段（`TraversalWarpPlan.TryEvaluate`，Editor 烘焙與執行期共用的純函數）
   對「目標腕點位置」的夾持，作用對象是**手部 IK 目標**，不是膠囊。跟 `characterController` 完全無關，
   Q3 原本的疑慮是誤把它跟另外兩個「真的會寫 center」的系統歸在一起。
5. **State 切換的時序也已經被既有紀律覆蓋**：`RestoreTraversalCollisionProfile()` 在
   `TraversalState` 的退出路徑同步呼叫（[TraversalState.cs:413](Assets/Scripts/Core/StateMachine/States/TraversalState.cs:413)），
   FSM 的 `OnExit`／下一狀態 `Update` 在同一影格內依序執行（不是非同步），所以不存在「本幀
   `ExecuteBaseMovement` 跑的時候 profile 旗標還沒清乾淨」的競態。

⇒ **Q3 結論：不需要新的協調機制。把 Query／Apply 放在 `UpdateCapsuleOffset(data)` 之後、
`characterController.Move()` 之前即可，現有的三道 guard 已經確保這個位置讀到的膠囊幾何永遠是對的。**

#### 6.5.2 Q7：Query 遮罩該對齊哪一份既有設定——**獨立新欄位，初值 `~0`**

盤點了本專案所有已存在的 `LayerMask` 欄位：

| 系統 | 欄位 | 值 |
|---|---|---|
| `FootIKSettings.GroundLayers`（[FootIKSettings.cs:18](Assets/Scripts/Presentation/IK/FootIKSettings.cs:18)） | `LayerMask`，程式碼預設 `~0` | X Bot 實際 authored 值：`m_Bits: 4294967295`（**Everything**，逐位元核對） |
| `TraversalProbeSettings.ObstacleMask`（[TraversalProbeSettings.cs:9](Assets/Scripts/Core/Environment/TraversalProbeSettings.cs:9)） | 獨立 `LayerMask` 欄位 | 另外一份，不與 Foot IK 共用 |
| `characterController.Move()` 本身 | 不吃 LayerMask 參數，走 Physics **Layer Collision Matrix** | `ProjectSettings/DynamicsManager.asset` 的 `m_LayerCollisionMatrix` **全部是 `f`**（逐位元核對）＝專案目前沒有停用任何一對 layer 的碰撞 |
| `ProjectSettings/TagManager.asset` | 專案定義的 layer 清單 | 只有 `Default／TransparentFX／Ignore Raycast／Water／UI／Player／Enemy`——**沒有專用的 Ground／Environment layer**，場景幾何預設全部在 `Default` |

三個事實疊起來：
1. **這個專案還沒有「地面」專屬 layer 可以縮小範圍**——場景所有靜態幾何都在 `Default`，跟角色、
   敵人共用的 layer 空間沒有做區分。
2. **`characterController.Move()` 本身的真實碰撞範圍已經是「全部 layer」**（Collision Matrix 全開），
   所以就算 Query 遮罩縮小，`Move()` 實際落地的表面還是可能包含 Query 沒查到的東西——**縮小 Query
   遮罩換不到語意上的精確，只會讓 Query 跟 `Move()` 的認知不一致**（Query 說沒有、`Move()` 卻真的
   撞到了）。在目前的 layer 配置下，`~0` 才是與 `Move()` 行為**一致**的選擇，不是偷懶。
3. **既有慣例是「每個系統各自一份獨立欄位」**——`FootIKSettings` 與 `TraversalProbeSettings`
   各自持有自己的 `LayerMask`，沒有互相引用。呼應這次 `groundEdgeEventGrace` 沒有重用
   `JumpStateParams.FallEntryGrace` 的同一條理由：**Motion 層不該伸手進 Presentation/IK 或
   Core/Environment 拿設定**，即使數值上一樣，也要各自宣告。

⇒ **Q7 結論：`MotionDriver` 開一個新的、獨立序列化 `LayerMask` 欄位（暫名
`stepDownQueryLayers`），初值 `~0`，與 `FootIKSettings.GroundLayers` 的現行慣例一致——這不是在
「複製一個已知的不精確設定」，是在目前專案的 layer 配置下，`~0` 本來就是唯一與 `Move()` 真實
碰撞範圍一致的選擇。若使用者未來新增專用的 Ground layer，三個系統（Foot IK／Traversal
Probe／本欄位）需要**各自**更新，不會因為欄位獨立而漏改（因為每個都在 Inspector 上看得到）。**

### 6.6 這是否構成需要開 ADR 的架構變更？

對照 CLAUDE.md 的 Trial 判準（黑板 schema／FSM 拓撲／管線順序／核心驅動介面／推翻既有不變量）：
**本方案目前的最小形狀都不命中**——不新增黑板欄位、不改 FSM 拓撲、不改管線順序（`ExecuteBaseMovement`
內部的合併方式改變，呼叫時機不變）、不改任何核心驅動介面。**依現有 routing rule，屬於
`docs/02`／`MotionDriver` 所在的子系統文件（`docs/29`／`docs/31`）的 Living Spec 增補，
不需要開新 ADR。** Q3 查證後確認不涉及所有權重新分配，這條「若牽扯到所有權衝突再重新評估」的
但書可以拿掉——**本方案不會觸發 ADR 判準**。

---

## 7. 下一步

1. ~~使用者裁決 Q1／Q2~~ ✅ 已裁決（§6.0）。
2. ~~Q3／Q7 讀碼查證~~ ✅ 已完成（§6.5）。
3. 若要精確化 §3.4，安排一輪標記步態的 Console 量測（Q5）。
4. **Q8 是目前唯一剩下的技術性阻斷項**：Query→Apply 對真實樓梯的貼齊平滑度需要 Play Mode 或
   真實場景驗證（`CharacterController.Move()` 不認 preview scene 隔離物理，本輪 Spike 驗不到這塊）。
5. Q6（敵人要不要吃到 assist）是產品決策，不是技術阻斷項，可以留到 prototype 出來後再談。
6. 落地後記得：`docs/29`（Foot IK 已知限制表）與 `docs/31`（附錄 B authored 值）需要 fold back。
