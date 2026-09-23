# 42 — Near-Floor Following / Step-Down 研究輪（2026-09-23，研究性質，⛔ 未實作、未裁決）

> **這份文件是什麼**：使用者在下樓梯 jitter 排查後明確要求——**不要針對 Stairs 寫特例**，
> 改參考 Unreal `FindFloor` / `MoveAlongFloor` / `FStepDownResult` 的責任劃分，研究
> `MotionDriver` 是否缺少一個**通用的** near-floor following／step-down 層。
> 本輪只做兩件事：①**量測**現況缺口，②**提出最小方案**。**不實作、不裁決**。
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
本質上就是簡化版的 UE `AdjustFloorHeight`——**不需要重蓋一整套 UE 分層，只需要在既有的
`characterController.Move()` sweep 前面多塞一段有界的向下分量**，讓 Unity 自己的碰撞掃描
「順便」把下一階找到。這比照搬 UE 三層架構要小得多。

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
| `FStepDownResult`（避免重複 sweep） | —— | 不適用——我們沒有獨立 Query 層可以重複 sweep | 等 §6 方案定案後才有意義討論 |

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

### 6.1 核心構造

在 `ExecuteBaseMovement`（**只有這個方法**，不動 `ExecuteVerticalOnlyMovement`）合併水平/垂直分量
之前，若滿足：
- 上一幀 `data.IsGrounded == true`（已經在走，不是剛跳起來／剛被打飛）

則把本幀垂直分量的下探距離**夾到至少** `maxStepDownDistance`（新欄位，非重用 `stepOffset`——
理由見 §6.3 Q1），而不是純粹吃 `GetGravityThisFrame` 算出來的彈道值。之後照舊丟給
`characterController.Move()`——**利用 `Move()` 自帶的 sweep 幫我們找地面，不需要另外開一次
`Physics.Raycast` 當 Query 層**。

這樣設計的原因：
- `CharacterController.Move()` 的 sweep 本來就會在**第一個碰到的表面**停下——多給的下探距離
  在平地上不會有任何效果（sweep 一碰到當前地面就停，跟現在行為逐位元相同），**只有在
  `maxStepDownDistance` 內真的有更低的面時才會產生差異**。這與 UE `AdjustFloorHeight` 的
  「在範圍內才修正、範圍外交給 Falling」精神一致，但**不需要另外寫一個 Query 函式**——
  這是 Unity `CharacterController.Move()` 把 Query 與 Apply 合而為一的簡化空間。
- **`ExecuteBaseMovement` vs `ExecuteVerticalOnlyMovement` 這個既有的方法切分，剛好就是
  UE `PhysWalking` vs `PhysFalling` 的切分**——`DeathState`／`HurtState`／Jump 空中段本來就
  不走 `ExecuteBaseMovement`，所以**這個方案完全不需要碰 StateMachine 或任何黑板欄位**，
  只動 `MotionDriver` 一個類別內部。這是本輪最重要的發現：**掛載點已經存在，不必新建**。

### 6.2 明確排除的做法（避免重蹈覆轍）

- ⛔ 不修改 `IsGrounded` 的寫入語意本身（它仍然＝`characterController.isGrounded`，
  `docs/38` §13 的裁決維持不變）——本方案是讓**真實的碰撞結果**因為多探了一點距離而變成
  「有碰到」，不是在既有結果上加否決/加分。
- ⛔ 不在 Foot IK／Audio／FSM 任何一層加 fade／權重修正去掩蓋這個斷點——那正是
  `docs/29` §I 明文禁止的手法。本方案要修的是**斷點本身在運動層是否存在**，不是下游怎麼
  平滑一個仍然存在的斷點。
- ⛔ 不重用 `characterController.stepOffset` 當作下探距離——它的官方語意只保證「往上」，
  混用會讓這個欄位變成「兩個方向都要改就要一起改」的隱性耦合。

### 6.3 待裁決 / 待量測（下一輪要先回答，才能談實作）

| # | 問題 | 為什麼要先問 |
|---|---|---|
| **Q1** | `maxStepDownDistance` 放哪、預設多少？（新 `MotionDriver` 欄位，還是黑板？初值抄 `stepOffset` 的 0.3，還是另外量測？） | 值太大可能吃掉「應該要有 Falling 動畫」的小落差（例如故意跳下一個 0.4m 平台），值太小則涵蓋不了較陡的樓梯 |
| **Q2** | 這個 assist 該不該在 `MoveDirection` 為零（原地站立）時也生效？ | 目前 §6.1 沒有特別排除，需要確認站在樓梯邊緣不動時會不會被緩慢「吸」下去，視覺是否奇怪 |
| **Q3** | 與 `RootReachConstraint`／Dynamic Capsule Offset（`docs/27`／`docs/28`）／Traversal Collision Profile 是否有交互？ | 這三者也會在同一幀動 `characterController.center`／位移，本輪未讀那幾份程式碼確認相容性 |
| **Q4** | `fallEntryGrace`／`groundEdgeEventGrace`（這次剛調的兩個 grace）要不要保留？ | 建議保留——它們是事件層的防禦，即使運動層不再閃，仍能擋住「其他角色沒套用這個 assist」「極端幾何」等邊界情況，屬於 defense-in-depth，不衝突 |
| **Q5** | 需不需要先補一輪「Walk／Run 分開標記」的 Console 量測，把 §3.4 的理論模型換成實測？ | 目前 walk-vs-run 的解釋是推導，不是量到的；實作前建議至少用 `recordDebugTrace` 各錄一段驗證量級對不對 |
| **Q6** | 敵人（`AIMovementSource` 也走 `ExecuteBaseMovement`）要不要一起吃到這個 assist？ | `MotionDriver` 是共用類別，理論上會自動套用；需要確認敵人的樓梯使用情境是否有不同需求 |

### 6.4 這是否構成需要開 ADR 的架構變更？

對照 CLAUDE.md 的 Trial 判準（黑板 schema／FSM 拓撲／管線順序／核心驅動介面／推翻既有不變量）：
**本方案目前的最小形狀都不命中**——不新增黑板欄位、不改 FSM 拓撲、不改管線順序（`ExecuteBaseMovement`
內部的合併方式改變，呼叫時機不變）、不改任何核心驅動介面。**依現有 routing rule，屬於
`docs/02`／`MotionDriver` 所在的子系統文件（`docs/29`／`docs/31`）的 Living Spec 增補，
不需要開新 ADR。** 若後續發現 Q3 牽扯到與 Dynamic Capsule／Traversal 的所有權衝突到需要重新分配
`characterController.center` 的寫入權，屆時再重新評估。

---

## 7. 下一步

1. 使用者裁決 §6.3 Q1／Q2（至少要有初步方向才能寫 prototype）。
2. 若要精確化 §3.4，安排一輪標記步態的 Console 量測（Q5）。
3. 讀過 `docs/27`／`docs/28`（Dynamic Capsule）與 `docs/30`（Traversal）確認 Q3 沒有寫入衝突後，
   才進入 prototype／Trial。
4. 落地後記得：`docs/29`（Foot IK 已知限制表）與 `docs/31`（附錄 B authored 值）需要 fold back。
