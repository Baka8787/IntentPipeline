# 38 — Valid Standing Support（碰撞支撐 ≠ 可站支撐）

> # ⏹ 狀態：**`Deferred / Accepted Limitation`（2026-09-17，使用者明確裁決）**
>
> **這份文件現在是一份研究紀錄，不是一份規格。** §1–§12 的研究與量測全部保留；
> **所有會改變正式 gameplay 行為的實驗已從 runtime 撤回**（見 §13.1 的退回清單）。
>
> **正式行為 ＝ `data.IsGrounded` 就是 `CharacterController.isGrounded`，沒有任何額外的 support 規則。**
> ⇒ 極端平台邊緣時**允許膠囊側面被平台角支撐、允許角色部分腳掌／腳尖懸空，不額外強迫掉落**。
> 這是**已知且被接受的限制**，不是待修的 bug。
>
> ⛔ **不要再為這個問題新增局部 workaround。** 重開條件見 **§13.3**。
> 📌 先讀 **§13**，再讀其餘章節——§2–§12 描述的是**已被否決**的方案，不是現行架構。

## 0. 問題陳述

```
Collision support  ≠  Valid standing support
```

碰撞膠囊維持原尺寸（radius 0.32，它還負責牆面／肩膀／手臂的水平保護），
但 **gameplay 另外判定「這個位置還算不算可站」**。

實測現況（本輪 spike）：

| | |
|---|---|
| `CharacterController.isGrounded` 允許 root 超出邊緣 | **0.23 – 0.32 m**（隨沉降程序而異，量級 ≈ radius） |
| ⇒ 腳尖（ToeBase，距 root 0.114 m）懸空 | **0.34 – 0.43 m** |

⚠️ 原因**不是** Foot IK，也不是膠囊底半球撐在邊角上——
是 Unity `CharacterController` 會讓**膠囊側面**掛在平台角上。

---

## 1. Unreal：`FindFloor` / perch 的實際資料流

**來源**：Epic 官方 `CharacterMovementComponent.cpp`（公開鏡像
[gist NoirMorilec/1ebe1a6f…](https://gist.github.com/NoirMorilec/1ebe1a6f742c193101265a928b90c897)，
11,238 行，檔頭為 `// Copyright Epic Games, Inc. All Rights Reserved.`）。
交叉比對：[VRExpansionPlugin `VRCharacterMovementComponent.cpp`](https://github.com/ProteusVRpublic/SteamVR_Template_2.0-UE4.12.5/blob/master/Plugins/VRExpansionPlugin/Source/VRExpansionPlugin/Private/VRCharacterMovementComponent.cpp)（UE 4.12 衍生版，`FindFloor` 為逐行複製）。

### 1.1 資料流

```
PhysWalking
└─ FindFloor(CapsuleLocation, CurrentFloor)
   ├─ ComputeFloorDist(..., SweepRadius = 完整 capsule radius)
   │    ├─ FloorSweepTest：以 capsule 向下 sweep
   │    ├─ IsWithinEdgeTolerance(hit) 不過 → 縮半徑 SWEEP_EDGE_REJECT_DISTANCE 再 sweep 一次
   │    └─ IsWalkable(hit) → OutFloorResult.bWalkableFloor
   │
   ├─ if (ShouldComputePerchResult(hit, bCheckRadius = true))      ← 只有「踩在外圈」才進來
   │    └─ ComputePerchResult(GetValidPerchRadius(), …)            ← **用較小半徑重跑 ComputeFloorDist**
   │         ├─ 成功 → 修正 FloorDist（必要時把 normal 換成可走的）
   │         └─ 失敗 → **OutFloorResult.bWalkableFloor = false**   ← ⭐ 就是這一行
   │
   └─ CurrentFloor
      └─ PhysWalking: if (!CurrentFloor.IsWalkableFloor()) → CheckFall() → MOVE_Falling
```

原碼註解逐字：

> `// We had no floor (or an invalid one because it was unwalkable), and couldn't perch here, so invalidate floor (which will cause us to start falling).`

### 1.2 ⭐ 七個問題的回答

**① Unreal 為什麼不直接相信 capsule 的任意邊緣接觸？**
因為 capsule sweep 回報的 hit **可能只擦到膠囊最外圈**。UE 有兩道獨立的不信任機制：
- `IsWithinEdgeTolerance()`：`|impactPoint − capsuleLocation|₂D < (radius − 0.15)` ——
  命中點若落在半徑最外 0.15 內，**視為「旁邊的東西」而不是「腳下的地板」**，
  縮半徑重掃一次。`SWEEP_EDGE_REJECT_DISTANCE = 0.15`（UE 單位 cm）。
- perch：見下。

**② `PerchRadiusThreshold` 如何限制「只靠 capsule 外圈掛在邊緣」？**
它是**從半徑往內縮多少才算數**。`ShouldComputePerchResult` 先判斷命中點離中心的水平距離：
落在 `GetValidPerchRadius()` 之內 ⇒ 已經站得夠穩，**不必 perch 檢查**；
落在那之外 ⇒ 正是「只靠外圈掛著」，才去做 perch 檢查。

**③ `GetValidPerchRadius()` 與實際 capsule radius 的關係？**
```cpp
FMath::Clamp(PawnRadius - GetPerchRadiusThreshold(), 0.11f, PawnRadius)
```
**永遠 ≤ 實際半徑**，且下限 0.11（避免退化成 line trace）。
`GetPerchRadiusThreshold() = max(0, PerchRadiusThreshold)`。
📌 **UE 預設 `PerchRadiusThreshold = 0.0f`** ⇒ `ShouldComputePerchResult` 第二道閘門
（`<= SWEEP_EDGE_REJECT_DISTANCE`）直接 return false ⇒ **perch 預設是關的**，由遊戲自行開啟。

**④ `ComputePerchResult()` 為什麼需要較小有效半徑重新做 floor check？**
因為「有沒有地板」與「站不站得住」是兩個問題。原本的 sweep 用完整半徑，
只要外圈碰到就算有地板；用較小半徑重跑，就是在問
**「如果我的腳只有這麼寬，還踩得到東西嗎？」**
⚠️ 它**不是**改膠囊——`ActualSweepDist = PerchSweepDist + PawnRadius`，
掃得比要求的更遠，正是因為縮半徑會漏掉原半徑會碰到的東西。

**⑤ normal floor result 和 perch result 的 authority 關係？**
`FFindFloorResult` **只有一份**，是唯一權威。perch 是它的**否決權 ＋ 修正權**：
- 否決：perch 失敗 ⇒ `bWalkableFloor = false`（開始下墜）
- 修正：perch 成功但原本 normal 不可走 ⇒ 用 perch 的 line trace 結果覆蓋
⇒ **下游只讀 `CurrentFloor.IsWalkableFloor()`，完全不知道 perch 存在。**

**⑥ Unreal 怎麼處理這些？**
| | 作法 |
|---|---|
| 平台邊緣 | perch（上述） |
| slope | `IsWalkable()` 比對 `WalkableFloorZ`；地板搜尋深度用 `max(MAX_FLOOR_DIST, MaxStepHeight + adjust)` |
| step | `MaxStepHeight` ＋ `StepUp()`；`CanStepUp()` 檢查元件是否允許 |
| walkable normal | `IsWalkable(Hit)` → `Hit.ImpactNormal.Z >= GetWalkableFloorZ()` |
| floor distance | `MIN_FLOOR_DIST = 1.9` / `MAX_FLOOR_DIST = 2.4`；把角色維持在這個區間內浮貼 |

**⑦ 哪些是 UE 特有、哪些可移植？**

| 可移植 ✅ | UE 特有 ⛔ |
|---|---|
| **「floor query 與 collision shape 分離」的核心概念** | `FFindFloorResult` 這個資料結構本身 |
| 「用較小半徑重跑一次 floor query 當否決權」 | `MOVE_Falling` movement mode 機制 |
| `IsWithinEdgeTolerance` 的「外圈命中不算腳下」判準 | 浮貼區間（MIN/MAX_FLOOR_DIST）——Unity CC 自己貼地，我們沒有這層 |
| 地板搜尋深度 = `max(floorDist, stepHeight)` | `StepUp()` / `CanStepUp()`——Unity CC 內建 `stepOffset` |
| perch 只在「踩在外圈」時才跑（省 query） | `PerchAdditionalHeight` 等浮貼細節 |

⚠️ **最大的不可類比處**：UE 的 `CharacterMovementComponent` **自己做全部的移動與地板解算**，
`FindFloor` 的結果直接驅動 movement mode。
Unity 的 `CharacterController` 是**黑箱**：`Move()` 內部自己解算碰撞，
`isGrounded` 是它回報的結果，**我們無法替換它的內部 floor 邏輯**。
⇒ 我們只能在**它回報之後**加一層否決，不能像 UE 那樣取代它。

---

## 2. Unity 官方 `com.unity.charactercontroller`（DOTS）

**來源**：官方 registry tarball `com.unity.charactercontroller-1.4.5.tgz`
（`https://download.packages.unity.com/…`），實際 C# 原碼，非 README。
⚠️ [CharacterControllerSamples](https://github.com/Unity-Technologies/CharacterControllerSamples) repo 本身
**不含**核心原碼，它把 `Unity.CharacterController` 當 UPM 依賴。

### 2.1 Grounding 在哪裡算

`KinematicCharacterUtilities.Update_Grounding`（預設實作）→
`Default_IsGroundedOnHit` → `IsGroundedOnSlopeNormal(MaxGroundedSlopeDotProduct, hit.Normal, GroundingUp)`。
**每個 hit 各自判定 `IsGroundedOnHit`**，而不是一個全域布林。

### 2.2 ⭐ `PreventGroundingWhenMovingTowardsNoGrounding` 的實際 source chain

```
Update_PreventGroundingFromFutureSlopeChange(…)
└─ if (characterBody.IsGrounded && (PreventGroundingWhenMovingTowardsNoGrounding || HasMaxDownwardSlopeChangeAngle))
   └─ DetectFutureSlopeChange(…)
      ├─ rayStart = GroundHit.Position + GroundingUp * 0.05        // slopeDetectionVerticalOffset
      ├─ ① 往**速度方向**射 ray，長度 = |velocity| × deltaTime      // 「我下一帧會到哪」
      │     命中 → 算 slope change 角度
      ├─ ② 沒命中 → 移到該點，往**下**射 0.05                      // downDetectionDepth
      │     沒命中 → isMovingTowardsNoGrounding = true
      │     命中但 normal 不可走 → isMovingTowardsNoGrounding = true
      ├─ ③ 再往前 0.25 補一次向下 ray                              // secondaryNoGroundingCheckDistance
      │     命中且可走 → isMovingTowardsNoGrounding = false         // 小縫隙不算懸崖
      └─ ④ 往回射一次，描述斜率變化
   └─ if (isMovingTowardsNoGrounding) characterBody.IsGrounded = false;   ← ⭐ 同樣是「否決」
```

### 2.3 與 Unreal 的關鍵差異

| | Unreal perch | Unity `PreventGrounding…` |
|---|---|---|
| 判準 | **位置**：我現在站的地方夠不夠穩 | **速度**：我正要去的地方有沒有地 |
| 解的問題 | 站在邊緣看起來不對 | 走下邊緣時會「吸附」在邊上 |
| 需要速度嗎 | 否 | **是**（`rayLength = |velocity| × dt`，站著不動時整段不執行） |

📌 **我們的問題是靜態的**（「站在極限邊緣能站立」）⇒ **Unreal 的 perch 才是對應的模型**。
Unity 這套解的是另一個症狀（走下去時的吸附），兩者互補、不互斥。

---

## 3. Unity `OpenCharacterController`（Standard Assets，已停更）

**來源**：[Standard-Assets-Characters `OpenCharacterController.cs`](https://github.com/Unity-Technologies/Standard-Assets-Characters/blob/master/Assets/_Standard%20Assets/Characters/Scripts/Physics/OpenCharacterController.cs)，2,204 行。

```csharp
void UpdateGrounded(CollisionFlags movedCollisionFlags, bool doDownCast = true)
{
    if ((movedCollisionFlags & CollisionFlags.CollidedBelow) != 0) isGrounded = true;
    else if (doDownCast)
        isGrounded = CheckCollisionBelow(k_GroundedTestDistance, …);   // 獨立向下探測
    else isGrounded = false;
}
```

- `k_GroundedTestDistance = 0.001f`，**與移動用的碰撞查詢是分開的兩件事**
- `CheckCollisionBelow` 可選 raycast 或 sphere cast（`SmallSphereCast` / `BigSphereCast`）
- 另有 `CanStickToGround` / `k_MaxStickToGroundDownDistance = 1.0f` 的 stick-to-ground

**它證明了什麼**：Unity 自己也把「角色碰撞」與「腳下 ground test」拆成兩條路徑。
**它沒解決什麼**：它的 ground probe 仍用**完整半徑**的 sphere cast
⇒ 對 perch 問題無效。它解的是「有沒有失去接觸」，不是「這個接觸算不算數」。

---

## 4. 目前 IntentPipeline 的鏈路

```
CharacterController.Move(...)                     ← Unity 黑箱，膠囊 radius 0.32
   ↓
characterController.isGrounded                    ← Unity 回報的碰撞結果
   ↓
MotionDriver.SyncGroundedState()                  ← ⭐ 唯一寫入者（MotionDriver.cs:1848）
   bool grounded = characterController.isGrounded; //    ← ⭐⭐ 唯一的等號，就是這一行
   data.IsGrounded      = grounded
   data.JustLanded      = !_wasGrounded && grounded
   data.JustLeftGround  =  _wasGrounded && !grounded
   data.VerticalVelocity = _verticalVelocity
   ↓
MotionDriver.GetGravityThisFrame()
   if (data.IsGrounded && _verticalVelocity < 0f)
        _verticalVelocity = reboundForce;          ← 貼地夾持：角色「黏」在邊緣的直接原因
   ↓
黑板 data.IsGrounded 的消費者：
   JumpState.CanEnter        起跳資格 ＋ 非主動 Falling 進入（_enterAsFall）
   JumpState.OnTick          真實落地判定
   RollState.CanEnter
   ActionState               definition.RequiresGrounded
   LocomotionModel           停步／移動判定
   TraversalProbe / TraversalState / TraversalClassifier
   FootIKController          `IsGrounded && !BlockIK` 閘門
```

### 4.1 ⭐ 「碰撞支撐」被等同為「可以 Grounded」的**確切位置**

**`Assets/Scripts/Presentation/Motion/MotionDriver.cs:1852`**

```csharp
bool grounded = characterController.isGrounded;
```

**整條鏈路只有這一行做這個等號。** 這是本專案的運氣：
`MotionDriver` 早就是 `IsGrounded` 的單一寫入者（由架構測試守住），
所以插入點是**一個檔案、一行**。

### 4.2 最小侵入點

```
characterController.isGrounded
        │
        ├─── 🆕 ValidStandingSupport 否決層 ────┐   ← 插在這裡
        │     grounded && HasValidSupport(...)  │
        ↓                                       │
   data.IsGrounded  ←──────────────────────────┘
```

**Authority 不搬家**：`MotionDriver` 仍是唯一寫入者，下游一行都不必改。
與 Unreal 的形狀相同——perch 是 `FindFloor` 的內部細節，
`PhysWalking` 只讀 `CurrentFloor.IsWalkableFloor()`。

---

## 5. Spike 數據

Throwaway PlayMode spike：`Assets/_Project/Tests/PlayMode/ValidStandingSupportSpike.cs`
（合成場景：平台頂面 y=0 邊緣 z=0／4 階 0.18 高 × 0.30 深樓梯／30° 斜坡）。
floor query 深度 = **0.30**（對齊 UE 的 `max(MAX_FLOOR_DIST, MaxStepHeight)`）。

### 5.1 平台邊緣

| 模型 | root 最大 overhang | 腳尖懸空 | 對比基準 |
|---|---:|---:|---|
| **基準** `CharacterController.isGrounded` | **0.320** | **0.434** | — |
| A) supportRadius **0.10** | 0.070 | 0.184 | −58% |
| A) supportRadius **0.14** | 0.090 | 0.204 | −53% |
| A) supportRadius **0.18** | 0.120 | 0.234 | −46% |
| A) supportRadius **0.22** | 0.150 | 0.264 | −39% |
| B) footprint **3/3** 點（±0.114） | **−0.120** | **−0.006** | 腳尖一過邊緣就掉 |
| B) footprint **2/3** 點（±0.114） | 0.000 | 0.114 | root 剛好到邊緣 |

### 5.2 樓梯回歸（在 CC 認為站著的 6 個取樣點中，誤判成「無支撐」的數量）

| 模型 | 誤判數 |
|---|---:|
| A) 0.10 | **2** ⛔ |
| A) 0.14 | 0 ✅ |
| A) 0.18 | 0 ✅ |
| A) 0.22 | 0 ✅ |
| B) 3/3 | **1** ⛔ |
| B) 2/3 | 0 ✅ |

### 5.3 斜坡回歸（30°，11 個取樣點）

全部模型 **0 誤判**，坡度量測正確回報 30.0°。

### 5.4 🐛 兩個被 spike 自己抓到的錯誤（記下來，因為都很像真的）

1. **第一版 floor query 深度 0.05 ⇒ 斜坡 11/11 全部誤判。**
   膠囊停在 30° 斜面上時，底部離「正下方的斜面」比離接觸點遠 `r(1/cos30 − 1) ≈ 0.05 m`，
   探測距離剛好搆不到。**「探不到地面」≠「沒有地面」。**
   ⇒ UE 用 `max(MAX_FLOOR_DIST, MaxStepHeight)` 正是為了這個。改成 0.30 後歸零。
2. **第二版把邊緣掃描的起始點改成「先 raycast 找表面」⇒ 整段量測歸零。**
   邊緣外的 XZ 探不到地面，退回高空起點 ⇒ 永遠落不了地。
   **量測工具在待測區域失效**，而它看起來只是「數字變小」。

---

## 6. 四個方案的技術比較

| | **A. 縮 CC radius** | **B. Unreal 式較小 support radius** | **C. footprint 多點** | **D. Foot IK 左右腳判定** |
|---|---|---|---|---|
| **edge 正確性** | 好（線性改善） | 好（0.434 → 0.204） | **最好**（0.434 → 0.114） | 名義上最好 |
| **手臂／肩膀穿牆** | ⛔ **直接惡化**——radius 正是水平保護 | ✅ 零影響（碰撞膠囊不動） | ✅ 零影響 | ✅ 零影響 |
| **traversal 影響** | ⛔ corridor／entry 全部要重驗 | ⚠️ 只透過 `IsGrounded` 間接影響 | ⚠️ 同左 | ⚠️ 同左 |
| **enemy spacing** | ⛔ 會變 | ✅ 無 | ✅ 無 | ✅ 無 |
| **step 行為** | ⚠️ stepOffset 相對關係改變 | ✅ 實測 0 誤判（R ≥ 0.14） | ✅ 2/3 規則 0 誤判 | ⚠️ 未知 |
| **slope** | ⚠️ 需重驗 | ✅ 0 誤判 | ✅ 0 誤判 | ⚠️ 未知 |
| **Capsule Pose profile** | ⛔ **整套失效**（flesh radius 是對著 0.32 量的） | ✅ 不受影響 | ✅ 不受影響 | ✅ 不受影響 |
| **實作成本** | 低（改數字）但**驗證成本極高** | **低**：1 檔 ＋ 1 次 SphereCast | 中：1 檔 ＋ 3 次 Raycast | 高 |
| **每帧查詢成本** | 0 | **+1 SphereCast** | **+3 Raycast** | 0（複用） |
| **GC** | — | 0（`SphereCast` 非 NonAlloc 版本回傳 struct，無配置） | 0 | 0 |
| **tuning 難度** | 低但牽一髮動全身 | **低**：一個數字，且有實測曲線 | 中：點數／位置／門檻三個維度 | 高 |
| **可否參照成熟實作** | — | ✅ **UE 同形狀** | ⚠️ 無直接對應 | ⛔ 無 |

### 6.1 ⛔ D（用 Foot IK 左右腳判 Grounded）為什麼只適合當輔助訊號

1. **層級反向**：`FootIKController` 的閘門本身就是 `data.IsGrounded`
   ⇒ 拿它的輸出回去決定 `IsGrounded` 會形成**循環依賴**。
2. **它不是 gameplay 語意**：Foot IK 的 raycast 是為了「把腳貼到地面」，
   採樣點跟著**動畫姿勢**跑。同一個站位在不同動畫帧會得到不同答案
   ⇒ grounding 會隨動畫抖動。
3. **本專案明文禁止**：`docs` 的 Foot IK 鐵律是
   「Natural Pose > Terrain > Contact；禁 Fade/Gate/權重修正」——
   讓 gameplay 依賴它，等於把表現層的妥協變成規則。
4. **層規則會擋**：`ArchitectureRegressionTests` 的 LayerRules
   不允許 Core 反向依賴 `Presentation.IK`。

✅ **可以用來 cross-check**：debug gizmo 疊兩者，驗證 support probe 的採樣位置與實際腳位相符。

---

## 7. 建議

### 7.1 推薦：**B（Unreal 式較小 support radius）**，`supportRadius ≈ 0.14`

理由，按權重排序：

1. **它有成熟 AAA 實作背書，而且形狀一模一樣**——
   碰撞 shape 不動、另一次 floor query、失敗就否決。UE 用了十幾年。
2. **零跨模組風險**：碰撞膠囊一個位元都不動 ⇒ 牆面保護、traversal corridor、
   enemy spacing、Capsule Pose profile **全部不受影響**。
3. **實測 0 樓梯誤判、0 斜坡誤判**，且把腳尖懸空從 0.434 砍到 0.204（−53%）。
4. **只有一個可調數字**，且我們已經有 0.10／0.14／0.18／0.22 的實測曲線。
5. 每帧 **+1 次 SphereCast**，零配置。

⚠️ **不推薦 C（footprint）的理由不是它不準**——它的 edge 正確性最好（0.114）。
是因為它多兩個 tuning 維度（點數、位置）、3 倍查詢成本，
而且**沒有成熟實作可以對照**；B 已經把問題壓到可接受範圍。
📌 若 Play 後覺得 0.204 仍嫌寬，**C 的 2/3 規則是現成的下一階**（實測同樣 0 誤判）。

### 7.2 Authority 放哪一層

**不搬家。** 留在 `MotionDriver`——它已經是 `IsGrounded` 的單一寫入者。

```csharp
// MotionDriver.SyncGroundedState()
bool grounded = characterController.isGrounded;
if (grounded && !HasValidStandingSupport()) grounded = false;   // ← 唯一新增的語意
```

與 UE 同形狀：perch 是 `FindFloor` 的內部細節，`PhysWalking` 只讀最終結果。
**下游 8 個消費者一行都不必改。**

### 7.3 預計會改哪些檔案

| 檔案 | 改動 |
|---|---|
| `Assets/Scripts/Presentation/Motion/MotionDriver.cs` | `SyncGroundedState` 插入否決；＋3 個 `[SerializeField]`（enable／supportRadius／floorSearchDepth） |
| 🆕 `Assets/Scripts/Presentation/Motion/StandingSupportProbe.cs` | 純函式 support query（~60 行），無狀態、零配置 |
| 🆕 `Assets/_Project/Tests/PlayMode/StandingSupportTests.cs` | ledge／stairs／slope 三條行為測試（把 spike 的量測固化成斷言） |
| `Assets/Prefabs/X Bot.prefab`／`Y Bot.prefab` | **只填新欄位**，⛔ 不動 `m_Radius` |
| `docs/02-dev-spec.md` §1 | `IsGrounded` 欄位說明補一句「已含 valid-support 否決」 |

### 7.4 ⚠️ 接線前必須先想清楚的副作用

1. **`JustLeftGround` 會更早觸發** ⇒ `JumpState` 的非主動 Falling 進入更早。
   `FallEntryGrace` 的語意會與新規則疊加，需一起看。
2. **`FootIKController` 的閘門是 `IsGrounded`** ⇒ 站在邊緣時 Foot IK 會更早關閉。
   那可能讓邊緣的視覺**更差**（腳突然回到動畫姿勢）。**這一項只能 Play 驗。**
3. **Traversal entry** 讀 `IsGrounded` ⇒ 貼邊起攀的條件會變嚴。
4. **貼地夾持** `if (IsGrounded && vy < 0) vy = reboundForce` 會更早停止作用——
   那正是我們要的（角色開始下墜），但它同時是「站穩」的機制。

---

## 8. ✅ 落地紀錄（2026-09-17）

> ⚠️ **歷史紀錄。本節描述的程式已於同日全部移除**（`StandingSupportProbe.cs`、
> `StandingSupportTests.cs`、`MotionDriver` 的 4 個 `[SerializeField]` 與否決層、
> prefab 的 4 個欄位）。**現況見 §13。**

使用者裁決：**採用 B，supportRadius ≈ 0.14。**

| 檔案 | 實際改動 |
|---|---|
| 🆕 `Presentation/Motion/StandingSupportProbe.cs` | 純靜態函式（~90 行），`SphereCastNonAlloc` ＋ 呼叫端持有緩衝 ⇒ **零配置**。過濾自身 collider、過濾起始重疊、比對 slope limit |
| `Presentation/Motion/MotionDriver.cs` | `SyncGroundedState` 的等號改為 `isGrounded && HasValidStandingSupportThisFrame()`；＋4 個 `[SerializeField]`；＋每帧快取 |
| 🆕 `Tests/PlayMode/StandingSupportTests.cs` | 5 條：邊緣更嚴格／樓梯不誤判／斜坡不誤判／平地不否決／設定退化不否決 |
| `Prefabs/X Bot`、`Y Bot` | **只填新欄位**；已複查 `m_Radius` 仍為 **0.32**（前後皆印出比對） |
| ⛔ 刪除 | `Tests/PlayMode/ValidStandingSupportSpike.cs`（throwaway，數據已固化成斷言） |

### 8.1 兩個實作期的設計決定

**① 每帧只查一次。** `SyncGroundedState` 在一帧內會被多處呼叫（重力路徑、committed 垂直曲線、
traversal 各段共 9 個呼叫點），不快取就是每帧多次 SphereCast。
比照同檔 `GetGravityThisFrame` 既有的 `_gravityFrame` 慣例。

**② 只有否決權、沒有授予權。** `isGrounded == false` 時本層完全不介入；
設定退化（radius 0／depth 0／buffer null）時**一律回 true**——
失敗方向必須是「什麼都不做」，而不是讓角色憑空掉下去。由 `DegenerateSettings_DoNotVeto` 守住。

### 8.2 🐛 測試抓到的設定錯誤（與 spike 同一個坑，第二次踩）

樓梯／斜坡測試第一版從 y=3.0／6.0 放下，但沉降只走 12×0.03 = 0.36 m ⇒ **根本沒落地**
⇒ 取樣數 0、斷言空轉。**`Assert.Greater(checkedSamples, N)` 的前提斷言救了這次**——
沒有它，兩條測試會「通過」而實際什麼都沒驗。
⇒ 樓梯／斜坡改為先 raycast 找表面；**平台邊緣掃描刻意不用**（邊緣外探不到表面，
會退回高空起點 ⇒ 量測工具在待測區域失效）。

---

## 9. ⛔ 還沒做的：人工 Play 驗收

自動化涵蓋「數字對不對」，涵蓋不了「看起來對不對」。請實際 Play 看：

1. **平台邊緣**：站到極限邊緣，腳尖懸空應從 0.434 m 降到約 0.204 m。走過去會**更早**開始下墜。
2. 🔴 **Foot IK 的副作用**（最可疑的一項）：`FootIKController` 的閘門是 `data.IsGrounded`
   ⇒ 站在邊緣時 Foot IK 會**更早關閉**，腳可能突然彈回動畫姿勢。
   **這一項可能讓邊緣的視覺變差**，只能人眼判斷。
3. **樓梯／斜坡**：上下樓梯、走斜坡不得有任何憑空下墜（測試已覆蓋合成幾何，但實際場景的
   collider 組成不同）。
4. **Traversal**：貼邊起攀的條件會變嚴（`TraversalState` 讀 `IsGrounded`）。
5. **落地音效**：`JustLanded` 邊沿時機改變；目前 Landing 走 Footstep Bank，應照常。

⚠️ 若邊緣視覺因 Foot IK 反而變差，**先調 `standingSupportRadius` 往上**（0.18／0.22 的數據見 §5.1），
⛔ 不要去關 Foot IK 的閘門——那會動到 Foot IK 鐵律。

📌 若 0.204 仍嫌寬，**方案 C 的 2/3 footprint 是現成的下一階**（實測 0.114，同樣 0 誤判）。
外部原碼副本在 session scratchpad，未進 repo；本文已記錄來源與版本，重抓即可。

---

## 10. 🔴 回歸：**方案 B 單獨使用是不夠的，已停用**（2026-09-17，使用者 Play 回報）

> **狀態更正**：§8 說「已落地」是對的，但**落地的東西是淨負面的**。
> 當時的處置是把兩隻 prefab 的 `requireValidStandingSupport` 改回 **false**，程式與測試保留。
> ⚠️ **那個處置也已作廢**——2026-09-17 使用者裁決把整套程式移除，見 **§13**。

### 10.1 使用者回報的兩個症狀

1. **可以單腳完全浮空**
2. **由於碰撞箱沒實質變小所以下不去**

### 10.2 實測（throwaway spike，已刪）

**① 否決 grounded 之後，站著不動時角色下不去。**

| 起始 root.z | 60 個物理步後 Δy | Δz |
|---|---:|---:|
| 0.02 / 0.07 / 0.12 / 0.17 / 0.22 | **0.000（全部）** | **0.000** |

**Unity 的 `CharacterController.Move()` 不會從邊角滑下去。** 膠囊側面靠在角上，
往下推完全不動，水平也不漂移。

**② 但只要有水平輸入就沒事。**

| 前進速度 | 結果 |
|---|---|
| 0.5 m/s | ✅ 5/5 走出去（掉落 1.5 m） |
| 2.0 m/s | ✅ 5/5 走出去 |

⇒ **缺陷的精確形狀：只有「站定在帶狀區間內」才卡住。**

**③ 補 `IsWithinEdgeTolerance` 幫助有限**（腳尖懸空 0.204 → 0.184）。
⚠️ 我原本假設「SphereCast 擦到邊角」是單腳浮空的成因——**假設是錯的**。

### 10.3 ⭐ 真正的根因：否決層改變了狀態，卻無法改變位置

```
support 否決 → data.IsGrounded = false
   ├─ FSM 認為在 Falling，但角色物理上一動也不動
   └─ FootIKController 的閘門是 IsGrounded ⇒ **Foot IK 關閉**
        ⇒ 腳不再貼地、停在動畫姿勢 ⇒ **看起來完全浮空**
```

📌 **「單腳完全浮空」很可能是這次改動造成的，不只是被它暴露。**
這正是 §9 第 2 點事先標記為「最可疑」的那一項——**它成真了**。

⚠️ 教訓：**一個只能改變「狀態」而不能改變「位置」的否決層，在角色被物理撐住時
只會製造狀態與現實的矛盾。** 它沒有能力把角色從邊角上移開。

### 10.4 Unreal 有沒有提到怎麼「自然地下去」？——有，但那是結構性的答案

UE 原碼確實處理了「站著不動也要掉下去」：

```cpp
// PhysWalking：bZeroDelta ＝ 這一帧沒有水平位移
bool bMustJump = bZeroDelta || (OldBase == NULL || …);
if ((bMustJump || !bCheckedFall) && CheckFall(…, bMustJump)) return;

// CheckFall
if (bMustJump || CanWalkOffLedges())
{
    HandleWalkingOffLedge(…);
    if (IsMovingOnGround()) StartFalling(…);   // → SetMovementMode(MOVE_Falling)
}
```

**但它之所以真的會移動，靠的不是這段程式，而是 UE 的移動架構本身：**

| | Unreal | 我們（Unity CharacterController） |
|---|---|---|
| 走路時誰決定角色高度 | **`FindFloor` ＋ `AdjustFloorHeight`**——膠囊被**放置**在離地板 `MIN_FLOOR_DIST`(1.9)～`MAX_FLOOR_DIST`(2.4) 的浮貼區間 | **碰撞**——膠囊被地面「擋住」 |
| 地板失效時 | `MOVE_Falling` → `PhysFalling` → `SafeMoveUpdatedComponent` ＋ **`SlideAlongSurface`**（撞到邊角 ⇒ 沿法線滑開） | `Move()` 撞到邊角就**停住**，不滑開（實測 Δz = 0.000） |
| 有沒有「卡在邊角」這個狀態 | **沒有**——角色本來就不是被碰撞撐住的 | **有**，而且否決層製造了它 |

⇒ **UE 的答案是「結構上不讓這個狀態存在」，不是一個可以照抄的函式。**
要在 Unity `CharacterController` 上複製它，等於自己重寫整個移動解算（取代 `Move()`），
那是遠超本問題的工程量。

### 10.5 現況與可選的下一步

**現況**：`requireValidStandingSupport = false`（兩隻 prefab）。
行為與導入前**逐位元相同**。程式、測試、文件全部保留。

| 選項 | 內容 | 代價／風險 |
|---|---|---|
| **① 維持停用** | 接受「站在邊緣腳尖懸空 0.434 m」 | 零風險。原始觀感問題**沒解決** |
| **② 改成速度感知**（Unity DOTS 的 `PreventGroundingWhenMovingTowardsNoGrounding`） | **只在「正朝著沒有地面的方向移動」時才否決** | 實測顯示有水平速度時一定走得出去 ⇒ **卡住與浮空兩個症狀都消失**。但**站定時不否決** ⇒ 原始觀感問題仍然沒解決，只是不再惡化 |
| **③ 否決 ＋ 主動推離** | 否決的同時給一個遠離支撐邊緣的水平位移，讓膠囊脫離邊角 | 真的能解決站定的情況。但這是**新的移動權威**（MotionDriver 在 locomotion 沒要求時移動角色），會與 locomotion／traversal 互動，需要獨立裁決 |
| **④ 方案 C（footprint 多點）** | 換判準，但**不解決本節的問題**——一樣只能改狀態不能改位置 | 同樣會卡住。⛔ 不要以為換判準能繞過 |

📌 **②與③可以疊加**：②消除惡化、③解決站定。但③必須單獨裁決。

⚠️ **不要在沒解決 10.3 的前提下把 `requireValidStandingSupport` 打開。**

---

## 11. 方案③（否決 ＋ 主動推離）的可行性量測（2026-09-17）

> 使用者傾向方案③，提出三個問題。以下全部為 throwaway PlayMode spike 實測（已刪）。
> **結論：Q1 可解、Q3 乾淨、Q2 是代價所在。**

### 11.1 Q1 — 怎麼可靠算出 ledge outward direction

**作法**：以腳底為心、半徑 `ringRadius` 取 N 個方向，各做一次向下 raycast；
`outward = 無支撐方向的平均`（或 `−有支撐方向的平均`，兩者實測**完全一致**）。

| 參數 | 實測 |
|---|---|
| `ringRadius` | **不敏感**——0.16／0.20／0.26 結果逐項相同 ⇒ 取最小的 0.16 |
| 兩種估計法 | 逐項相同 ⇒ 任選 |
| 取樣數 N | **關鍵參數，見下** |

🐛 **第一版測出「誤差 0.0°」是對稱假象。** 測試幾何（軸對齊平台）與取樣環（軸對齊方向）
剛好同相位。改用**旋轉過的邊緣**重測才是真實誤差：

| 邊緣角度 | N=6 | N=8 | **N=12** | N=16 |
|---|---:|---:|---:|---:|
| 7° | 7.0° | 7.0° | 7.0° | 4.2° |
| 17° | 13.0° | 17.0° | **2.0°** | 5.7° |
| 23° | 7.0° | **22.0°** | 7.0° | 10.7° |
| 31° | 1.0° | 14.0° | **1.0°** | 2.8° |

⇒ **N=8 不可靠（最差 22°）。N=12 在取樣的四個角度中 ≤7°。**
⚠️ 誤差與邊緣角度**非單調**（量化相位效應），四個角度的樣本太小，不要當成保證。
📌 但即使差 22°，沿 outward 推的有效分量仍有 `cos22° = 0.93` ⇒ **對「脫離」夠用**，
影響的是「看起來像不像自然失足」。

**成本**：12 次 raycast，且**只在 support 被否決時才需要算**（平時零成本）。

### 11.2 🔴 Q2 — 最小水平修正：**這是方案③真正的代價**

**(a) 單次脈衝：完全無效。**

| 起始 root.z | d=0.01 | d=0.02 | d=0.04 | d=0.08 |
|---|---|---|---|---|
| 0.02 ～ 0.22（5 個位置） | 🔴 | 🔴 | 🔴 | 🔴 |

**20 個組合全部失敗。** 原因：要脫離必須讓 root 從否決線（0.09）走到碰撞線（0.32）
⇒ 需要位移 **~0.30 m**。那個量級的單次位移是**看得見的瞬移**，不是修正。

**(b) 持續速度：可行，但需要的量不小。**

| 起始 root.z | v=0.02 | v=0.05 | v=0.10 | v=0.20 | v=0.35 |
|---|---|---|---|---|---|
| 0.02 | 🔴 | 🔴 | 🔴 | 🔴 | ✅ |
| 0.07 | 🔴 | 🔴 | 🔴 | 🔴 | ✅ |
| 0.12 | 🔴 | 🔴 | 🔴 | ✅ | ✅ |
| 0.17 | 🔴 | 🔴 | 🔴 | ✅ | ✅ |
| 0.22 | 🔴 | 🔴 | ✅ | ✅ | ✅ |

⇒ **最壞情況（剛過否決線）需要持續 0.35 m/s**，且要持續到脫離為止（實測約 0.9 s）。

📌 **這不是「輕推」，是一段看得見的滑步。** 角色會在玩家沒有輸入的情況下
自己往外滑約 0.3 m、耗時近一秒。那是**手感決定**，不是工程決定：
可能讀成「失足滑下去」（好），也可能讀成「被看不見的手推開」（壞）。

### 11.3 ✅ Q3 — 誤推：乾淨

推的前提是 support 被否決，所以問題等於「這些情境會不會被否決」。

| 情境 | 站立取樣點 | 被否決次數 | 誤推 |
|---|---:|---:|---|
| 樓梯（4 階 0.18×0.30） | 6 | **0** | ✅ 無 |
| 斜坡 30° | 11 | **0** | ✅ 無 |

**沿邊緣側走**（平行邊緣移動，各種內縮距離）：

| 離邊緣 inset | 0.00 | 0.04 | 0.08 | 0.12 | 0.16 | 0.20 |
|---|---|---|---|---|---|---|
| support | 有 | 有 | 有 | 有 | 有 | 有 |
| 會不會推 | 不推 | 不推 | 不推 | 不推 | 不推 | 不推 |

⇒ **側向移動不會被誤推。** 要被推必須 root 已經超出邊緣 0.09 m 以上——
那時候本來就是「站在外面」。

### 11.4 三個問題的總結

| | 答案 | 難度 |
|---|---|---|
| **Q1 outward 方向** | 12 個環形 raycast，`ringRadius 0.16`，取無支撐方向平均 | ✅ **已解**，誤差 ≤7°（四個樣本），成本只在否決時發生 |
| **Q2 最小修正** | 脈衝無效；需**持續 0.35 m/s 約 0.9 s**（位移 ~0.3 m） | 🔴 **可行但不隱形**——是手感決定 |
| **Q3 誤推** | 樓梯 0／斜坡 0／側走 0 | ✅ **乾淨** |

### 11.5 ⚠️ 實作方案③之前必須先裁決的一件事

**這個推是新的移動權威**——`MotionDriver` 會在 locomotion 沒有要求的情況下移動角色。
需要決定：

1. **它與玩家輸入怎麼合成？** 玩家往內走、推往外 ⇒ 相消還是相加？
2. **它與 `TraversalState` 怎麼共存？** 貼邊起攀時角色刻意站在邊緣，不能被推走。
3. **Foot IK 怎麼辦？** 推的期間 `IsGrounded` 是 false ⇒ Foot IK 仍然關閉
   ⇒ **§10.3 的「腳浮空」在滑出去的那 0.9 秒內依然存在**，只是會結束。

📌 第 3 點特別重要：方案③解決「下不去」，但**不解決「浮空」**——
它把「永久浮空」變成「浮空 0.9 秒然後掉下去」。

**若要連浮空一起解，得讓 Foot IK 的閘門不要直接用 `IsGrounded`**，
而那會動到 Foot IK 鐵律，屬另一個獨立裁決。

---

## 12. ⛔ 方案⑤ Temporary Collision Release —— **實測後判定不值得採用**（2026-09-17）

> **狀態：REJECTED。** 這一節不是「還要再調一下」，是**這條路的形狀本身有問題**。
> 全部數據來自 throwaway PlayMode spike `TemporaryCollisionReleaseSpike.cs`（10 條，已刪）。
> ⛔ **沒有實作任何 release 邏輯**；`requireValidStandingSupport` 仍為 `false`，程式與資產零改動。

**構想**：support 已失效但大膠囊仍卡在 ledge corner 時，**短暫縮小** CharacterController 的
collision radius，讓重力自然把角色帶離邊角；真正離開後再安全恢復。
比 §10.5 的方案③好在「不主動推角色」——位移由重力產生，不是新的移動權威。

### 12.1 先查成熟參考實作

#### Unreal：`Crouch` / `UnCrouch` 的實際 source chain

原碼（`CharacterMovementComponent.cpp`）。**重點不是 crouch gameplay，是 resize 的協定：**

```cpp
// UnCrouch()
bool bUpdateOverlaps = false;                          // ① 先用「不觸發 overlap 事件」的方式長回去
CapsuleComponent->SetCapsuleSize(DefaultRadius, DefaultHalfHeight, bUpdateOverlaps);
CapsuleComponent->UpdateBounds();                      //    強制更新 bounds 才能測

const float SweepInflation = KINDA_SMALL_NUMBER * 10.f;
const FCollisionShape StandingCapsuleShape =
    GetPawnCapsuleCollisionShape(SHRINK_HeightCustom, -SweepInflation);  // ② 刻意**再放大一點**才測

bEncroached = GetWorld()->OverlapTest(StandingLocation, ..., StandingCapsuleShape, ...);
if (bEncroached)
{
    if (IsMovingOnGround() && CurrentFloor.bBlockingHit && CurrentFloor.FloorDist > MinFloorDist)
    {
        StandingLocation.Z -= CurrentFloor.FloorDist - MinFloorDist;   // ③ 先試著往地板靠一點再測
        bEncroached = GetWorld()->OverlapTest(StandingLocation, ...);
    }
}
if (bEncroached)                                        // ④ 還是不行 ⇒ **整個放棄，尺寸退回去**
{
    CapsuleComponent->SetCapsuleSize(Radius, OldUnscaledHalfHeight, false);
    CapsuleComponent->UpdateBounds();
    return;                                             //    ⑤ 沒有任何 depenetration、沒有強制擠出
}
```

七個問題的答案：

| 問題 | UE 的答案 |
|---|---|
| runtime capsule resize | `SetCapsuleSize(radius, halfHeight, bUpdateOverlaps)` ＋ `UpdateBounds()` |
| 恢復前怎麼測 clearance | **投機性先長大 → `OverlapTest` → 不行就退回**。測的形狀**再 inflate 一點**（`-SweepInflation`）以免剛好停在貼面 |
| center / half-height 怎麼處理 | `bCrouchMaintainsBaseLocation` ⇒ 位置補 `±ScaledHalfHeightAdjust`，讓**膠囊底部**不動。⭐ **radius 改變完全不需要補位置** |
| 空間不足怎麼辦 | **什麼都不做，直接 `return`** |
| 允許延後恢復嗎 | ✅ **是，而且延後就是唯一機制**：`PerformMovement` 每個 tick 都跑 `if ((!bAllowedToCrouch or !bWantsToCrouch) && IsCrouching()) UnCrouch(false);` ⇒ 這一帧失敗，下一帧再試 |
| depenetration 是不是主要手段 | ⛔ **不是，連保底都不是。** UnCrouch 路徑上沒有任何 depenetration 呼叫；`ResolvePenetration` 只在 `SafeMoveUpdatedComponent` 的 `bStartPenetrating` 時出現 |
| 有沒有「先掃到地板再站起來」 | 有：`SweepSingle(ShortCapsuleShape)` 往下掃找底，再 `OverlapTest(NewLoc)` —— 但那是 `!bCrouchMaintainsBaseLocation` 分支 |

#### Unity `OpenCharacterController`（Standard Assets，已停更）

`SetHeight(height, preserveFootPosition, ...)` 前面擋一道 `CanSetHeight()`；
`ValidateHeight()` 把高度夾到 **≥ 2 × radius**；resize 後走自己的 `Depenetrate()` 補救。
⇒ 形狀與 UE 一致（**先問能不能，再改**），但它的 depenetration 角色比 UE 重——
因為它是自己重寫的 solver，有權直接搬位置。**我們沒有那個權力**（`CharacterController.Move()` 是黑箱）。

#### 本專案現有的 collider authority（第 2、8 題）

| | 位置 | 現況 |
|---|---|---|
| **`radius` / `height` 的執行期寫入者** | `MotionDriver.RestoreTraversalCollisionProfile()`（`MotionDriver.cs:885-886`） | ⭐ **執行期目前沒有任何東西真的改過它們**——那兩行只是把 `BeginTraversalCollisionProfile` 擷取的 authored 值寫回。`TraversalCollisionProfile` 目前**只驅動 `center`**（原碼註解：「C1 is the first active channel. C2/C3 remain explicit serialized seams」） |
| **`center` 的執行期寫入者** | 全部在 `MotionDriver` 內，共三個：authored 基準 `_capsuleBaseCenter`／Capsule Offset V2（`UpdateCapsuleOffset`）／traversal profile（`ApplyTraversalCollisionProfile`） | 兩道 guard 保證不同時作用 |
| **編輯期 authority** | `Assets/Scripts/Editor/Tools/CharacterCapsuleFitter.cs:148-150` | 唯一寫 authored 三件套的地方 |
| **snapshot / authored baseline** | `MotionDriver.CaptureCapsuleBaseCenter()` → `_capsuleBaseCenter` / `_capsuleBaseRadius` / `_capsuleBaseHeight` | ✅ **已經存在**，不需要新建 |
| **既有 clearance utility** | `MotionDriver.ClampCapsuleOffsetAgainstGeometry()`（`CapsuleCast`，零配置，排除自身 layer） | ⚠️ 它刻意**縮掉 `skinWidth`** 再掃——那正是 §12.3 說明它不能當恢復閘門的原因 |
| **第二個假設 authored radius 的消費者** | `TraversalProbe.AuthoredRadius` / `AuthoredCenter` / `AuthoredHeight` | 縮小期間這些值會與實際不符 |

⇒ **若真要做，正確的接法是 `TraversalCollisionProfile` 尚未啟用的 C2/C3 radius/height 通道
＋ MotionDriver 內的 release override 層，⛔ 絕不是另一個 MonoBehaviour 在 `LateUpdate` 偷改 `radius`。**
`Authored Capsule Pose + Temporary Release Override → Final Capsule Geometry` 的形狀是對的，
owner 仍是 `MotionDriver`。**但下面的實測顯示不需要走到這一步。**

### 12.2 ✅ 乾淨的部分（先講好消息）

#### A. 最小脫離半徑：**規律極乾淨，但那正是壞消息**

`overhang` ＝ root 超出平台邊緣的水平距離；cell ＝ instant shrink 後 40 個 physics step 的 Δy。

| overhang \ r | 0.30 | 0.26 | 0.24 | 0.22 | 0.20 | 0.18 | 0.16 | 0.14 | 0.12 | 0.10 | 0.08 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **0.10** | 卡 | 卡 | 卡 | 卡 | 卡 | 卡 | 卡 | −0.03 | −0.04 | **✅** | ✅ |
| **0.14** | 卡 | 卡 | 卡 | 卡 | −0.02 | −0.03 | −0.05 | **✅** | ✅ | ✅ | ✅ |
| **0.18** | 卡 | 卡 | −0.03 | −0.04 | −0.06 | **✅** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **0.22** | 卡 | −0.03 | −0.06 | **✅** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **0.26** | 卡 | **✅** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **0.30** | **✅** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

⭐ **規律只有一條：`releaseRadius ≤ overhang` 才脫得掉。**
中間值（−0.02～−0.06）不是「慢慢掉」，是**往下沉一點點就在新半徑上重新卡住**。
有明顯 threshold，但門檻**不是一個常數，是 overhang 本身**。

🔴 **這就是致命處**：否決線（`supportRadius 0.14` ⇒ 幾何上 `0.14/√2 ≈ 0.099`，§5.1 實測 0.090）
**就是最壞情況的 overhang**。⇒ 最壞需要縮到 **≈0.10，也就是 authored 0.32 的 31%**，
**而且正好發生在這個功能唯一存在理由的那個位置。**

#### B. 開始下降：**2 個 physics step，instant 與 ramp 完全一樣**

| overhang | mode | firstDescendStep | Δxz@shrink | Δy@shrink | Δy(40 步) |
|---|---|---:|---:|---:|---:|
| 0.10–0.30 | instant | **2** | **0.0000** | −0.0039 | −3.22 |
| 0.10–0.30 | ramp3 | **2** | **0.0000** | −0.0039 | −3.22 |

⇒ **逐項完全相同。2–3 frame ramp 買不到任何東西**（它縮到 release 值的那一帧才是真正生效的那一帧）。
若真要做，**instant，理由是它更簡單，不是它比較好**。
**shrink 當帧沒有水平跳動、沒有 depenetrate、沒有瞬移**（Δxz = 0.0000）。

#### C. Resize 不變量：**乾淨，而且是這輪唯一無條件成立的好消息**

| case | Δroot.xz | Δroot.y | 腳底世界高度 before → after | `height ≥ 2r` |
|---|---:|---:|---|---|
| 開闊地 | **0.0000** | **0.0000** | 0.0300 → **0.0300** | ok（1.827 vs 0.64） |
| 貼牆 | **0.0000** | **0.0000** | 0.0300 → **0.0300** | ok |

⇒ **改 `radius` 不會移動 root，也不會移動 foot plane。**
幾何原因：Unity 的 `CharacterController` 膠囊底 = `center.y − height/2`，**與 radius 無關**
（只要 `height ≥ 2r`，本專案 1.827 vs 0.64 差很遠）。
⇒ **UE 那套 `ScaledHalfHeightAdjust` 位置補償只有改 height 才需要，改 radius 不需要。**
還原後 Δxz／Δy 也都是 0.0000。**問題不在 resize 本身。**

#### F. 誤觸：零

| 情境 | 站立取樣點 | support 否決 | stuck 判定 |
|---|---:|---:|---:|
| 平地 | 13 | **0** | **0** |
| 樓梯（4 階 0.18×0.30） | 12 | **0** | **0** |
| 30° 斜坡 | 11 | **0** | **0** |

stuck 判準＝`support 否決 ＋ 連續 3 個 physics step 重力想往下但 Δy > −0.0005`。
⇒ **正常地形永不觸發**（第 15 題的 1–4 項成立）。

#### G. 旋轉邊緣：門檻與角度無關

| yaw | overhang | 0.18 | 0.14 | 0.10 | 0.08 | 0.06 |
|---|---|---|---|---|---|---|
| 0° / 17° / 31° | 0.12 | −0.02 | −0.04 | ✅ | ✅ | ✅ |
| 0° / 17° / 31° | 0.20 | ✅ | ✅ | ✅ | ✅ | ✅ |

⇒ **三個角度逐格相同。** 脫離只看 `releaseRadius` vs `overhang`，不看邊緣朝向。
（與 §11.1 的 outward direction 不同——那個才對角度敏感。）

### 12.3 🔴 第一個致命發現：**沒有可用的 clearance 恢復判準**

UE 的模式是「先長大 → `OverlapTest` → 重疊就退回」。**在這個問題上它問錯了問題。**

實測：**角色卡在 ledge corner 時，authored 0.32 膠囊與平台「完全沒有重疊」。**

```
=== I. naive restore 逐步紀錄（overhang 0.12）===
step | radius | root.y  | Δy/step | support | clearance(authored−skin) | clearance(authored+0.02)
   0 |  0.320 |   0.003 |  0.0000 | False   | True                     | True
```

原因：CharacterController 用 `skinWidth`(0.03) 把膠囊**隔著**停在角上——
它是**靠著**邊角，不是**插進**邊角。`OverlapCapsule` 因此永遠回報「安全」。

⇒ **UE 的 `EncroachesBlockingGeometry` 問的是「有沒有東西插進我」**——
那對 crouch → stand（頭上有天花板，真的會重疊）是對的問題；
對「我還踩在我想離開的那個邊上嗎」**是錯的問題**。
📌 `MotionDriver.ClampCapsuleOffsetAgainstGeometry` 甚至刻意再縮一個 `skinWidth`（原碼註解說明理由），
**更不可能當恢復閘門**。

### 12.4 🔴 第二個致命發現：naive restore ＝ **25 Hz 鋸齒震盪**

把「clearance 通過就恢復」直接接上去（overhang 0.12，release 0.10）：

```
step | radius | root.y  | Δy/step
   2 |  0.100 |  -0.041 | -0.0439   ← 縮小：掉下去
   3 |  0.320 |   0.001 | +0.0427   ← 恢復：**被 Unity depenetration 抬回來**
   4 |  0.100 |  -0.043 | -0.0439
   5 |  0.320 |  -0.007 | +0.0357
   ...
→ 24 步內重新縮小 12 次 ⇒ 震盪迴圈 CONFIRMED
```

**每一帧 ±0.04 m、每兩帧一個週期（25 Hz）。** 角色確實有淨下降（24 步 −0.24 m），
但那是**靠震盪磨下去的**，而且往上的那一半**正是使用者明令不得當作恢復手段的 depenetration**。
⛔ 視覺上這是抖動，不是失足。

### 12.5 🔴 第三個致命發現：唯一可行的 latch，代價太大

唯一真的會動的規則是**「latch 到重新落地」**——縮小後完全不試恢復，直到 `isGrounded` 再次為真
且嚴格 clearance（authored + 0.02）通過。實測（2 m 高平台，崖底地面延伸到崖壁正下方）：

```
step | root.y  | root.z | radius | maxSafe(嚴格) | grounded | note
   0 |  -0.033 |  0.120 |  0.100 |          0.14 | False    |
  10 |  -0.649 |  0.120 |  0.100 |          0.10 | False    |
  23 |  -1.970 |  0.120 |  0.100 |          0.10 | True     | LANDED
  ...（第 80 步起玩家往外走 0.6 m/s）
  90 |  -1.970 |  0.252 |  0.100 |          0.22 | True     |
  98 |  -1.970 |  0.348 |  0.320 |          0.32 | True     | RESTORED
→ 落地步 = 23（0.46 s）
→ 恢復步 = 98，小尺寸持續 1.96 s（落地後還持續 1.50 s）
```

**它有效**——角色乾淨地掉了 1.94 m，沒有震盪、沒有瞬移。**但：**

1. **小尺寸持續 1.96 s，其中 1.50 s 是站在地上的**。
2. **`maxSafe` 在整段下墜與落地後都是 0.10**——因為角色**貼著崖面**往下掉（root.z 0.12，崖面 z=0），
   0.32 的膠囊在那條路徑上**任何一點都放不下**。
3. **恢復需要玩家自己走開 0.23 m。** 玩家不動 ⇒ **永遠維持 31% 的碰撞半徑站在崖底**。
   ⚠️ 崖底正是「牆最多」的地方——水平碰撞保護在最需要的時候消失。

### 12.6 🔴 第四個致命發現：縮小期間會鑽進 authored 膠囊永遠進不去的地方

牆面在 `x = +0.20`（authored 0.32 塞不進去），release 0.10，玩家往牆推 0.8 m/s：

| step | root.x | root.y | radius | maxSafe | 以 authored 計的重疊數 |
|---:|---:|---:|---:|---:|---:|
| 0 | −0.134 | 0.003 | 0.080 | 0.320 | 0 |
| 10 | **0.026** | −0.252 | 0.080 | 0.148 | **2** |
| 20 | **0.090** | −0.900 | 0.080 | 0.136 | **2** |
| 40 | **0.106** | −3.373 | 0.080 | 0.148 | **1** |

```
→ 最右可達 root.x = 0.410（authored 0.32 時最右應為 −0.090）
→ 縮小期間多鑽進去 0.500 m
```

⇒ **玩家可以在 release 期間把角色開進 authored 膠囊永遠不該到得了的幾何裡，深達 0.50 m。**
之後 `maxSafe` 長期卡在 0.136–0.148 ⇒ **恢復被無限期封鎖**；
若強制恢復，那就是一次 depenetration 擠出——**使用者明令不得依賴的東西**。
這不是調參數能解的：**只要碰撞體會變小，「小尺寸期間可達的空間」就一定大於 authored 可達空間。**

### 12.7 它也**不解決浮空**，而且時序 B 在結構上做不到（第 12 題）

| 時序 | 可行性 |
|---|---|
| **A**：support lost → `IsGrounded` false → shrink | ✅ 唯一做得到的 |
| **B**：support lost → release → 真的開始下降 → `IsGrounded` false | ⛔ **結構上不可能** |

理由：**release 的觸發條件就是否決本身**。要先否決才會縮，所以否決一定在下降之前。
B 需要「support 已失效但 `IsGrounded` 還是 true」這個中間狀態——
那就是 `SupportedButReleasing` / `CollisionReleaseActive` **另一個語意**，
而它的消費者是 `FootIKController` 的閘門 ⇒ **等於改 Foot IK 的 grounding 依賴**，
本輪明令不得碰，且屬獨立裁決。

⇒ 與方案③一樣：**把「永久浮空」變成「浮空 ~0.5 s 然後掉下去」**，浮空本身沒解決。

### 12.8 ⛔ 裁決：**不值得採用**

按使用者給的否決條件逐條對照：

| 使用者的否決條件 | 實測 |
|---|---|
| 必須縮得非常誇張 | ✅ **成立**：最壞 0.32 → **0.10**（31%），且正好在目標情境 |
| 恢復常常被牆卡住 | ✅ **成立**：沿崖面下墜全程 `maxSafe = 0.10`；靠牆時卡在 0.136–0.148 |
| collider 長時間維持小尺寸 | ✅ **成立**：1.96 s，其中 **1.50 s 站在地上**；玩家不動則無限期 |
| traversal 衝突難解 | ⚠️ 未量（見下） |
| 視覺比原本更差 | ✅ **成立**：naive ⇒ 25 Hz 抖動；latch ⇒ 浮空 0.46 s 後才掉，且浮空未解 |

**五條裡四條成立 ⇒ 判定 REJECTED。**

⛔ **因此刻意沒有做的事**（不是遺漏，是不為一個否決掉的方案付工程費）：
- ⛔ 沒有實作 traversal gating（第 10 題）。**seam 已經找到**：`TraversalProbe.Candidate`（每帧候選）
  與 `TraversalState.CommittedCandidate`，且 `TraversalState` 本來就是 push 方向呼叫 `MotionDriver`
  （`BeginTraversalCollisionProfile`／`OnUpdateMotion`）⇒ gating 應該由 Core **推**一個 claim 給
  `MotionDriver`，不是讓 `MotionDriver` 反向去讀 Core。**方向正確、成本低，但沒有理由現在做。**
- ⛔ 沒有實作玩家輸入 gating（第 11 題）。§10.2 已經證明**只要有 ≥0.5 m/s 水平速度角色一定走得出去**
  ⇒「有輸入就不介入」本來就是對的，但它保護的是一個不該存在的機制。
- ⛔ 沒有加第 15 題的回歸測試——**不為不存在的功能建測試**。
  （第 15 題的 1–4 項其實已被 §12.2-F 量到 0 誤觸；5–12 項描述的是被否決的行為。）
- ⛔ 沒有做第 14 題的安全 ring buffer trace——同上。

### 12.9 ⭐ 這一輪真正換到的東西

即使方案被否決，下面四件事是**可複用的知識**，不要重新踩：

1. **`CharacterController.radius` 的 resize 本身是免費且安全的**——
   Δroot = 0、foot plane 不動、`height ≥ 2r` 有大量餘裕、instant 無跳動（§12.2-C）。
   ⇒ 未來若有**真的需要動 radius** 的功能（例如 traversal C2/C3 通道），**幾何面不是障礙**。
2. **`OverlapCapsule` 類的 clearance 測不出「靠在邊角上」**——`skinWidth` 讓它不算重疊（§12.3）。
   ⇒ 任何「我離開那個邊了沒」的判斷**不能用重疊測試**，要用**距離**（到支撐邊緣的水平距離 > authored radius）。
3. **脫離門檻 = `releaseRadius ≤ overhang`，與邊緣朝向無關**（§12.2-A／G）。
   ⇒ 這是個幾何恆等式，不是可調參數。**任何「縮小碰撞體來脫離」的變體都會撞到同一條線。**
4. 🔴 **最重要的一條**：§10.3 說「否決層只能改狀態不能改位置」。
   這一輪證明了更強的版本——**在 Unity `CharacterController` 上，
   任何只改「碰撞體形狀」的手段都無法乾淨地製造「離開邊角」這個位移**，
   因為 `Move()` 不會沿邊角滑開（§10.2 實測 Δz = 0.000），
   而唯一會產生水平位移的機制是 depenetration，那是明令禁止的手段。
   ⇒ **剩下的選項只剩「不要讓角色站到那個位置」（方案②的速度感知否決）
   或「明確地移動角色」（方案③，代價已在 §11.2 量化）。沒有第三種。**

### 12.10 若使用者仍要推翻此裁決，最少要改哪些檔案（第 14 題）

留這份清單只是為了不必重查，**不代表建議實作**：

| 檔案 | 改動 |
|---|---|
| `Presentation/Motion/MotionDriver.cs` | release 狀態機（latch／觸發窗／退出）＋ radius override 層，接在 `_capsuleBaseRadius` 之上 |
| `Presentation/Motion/TraversalWarpPlan.cs`（`TraversalCollisionProfile`） | 啟用既有但未使用的 C2/C3 radius/height 通道，避免第二套 resize 機制 |
| 🆕 `Presentation/Motion/CollisionReleaseProbe.cs` | **距離式**分離判定（⛔ 不是重疊式，見 §12.3） |
| `Core/StateMachine/States/TraversalState.cs` ＋ `Core/Environment/TraversalProbe.cs` | push 一個 traversal claim 給 `MotionDriver` |
| `Presentation/IK/FootIKController.cs` | ⛔ **只有在同時裁決浮空問題時才動**——那會碰 Foot IK 鐵律 |
| `Prefabs/X Bot`、`Y Bot` | 新欄位 |

**應資產化**：`releaseRadius`（或 `releaseMargin`）、`stuckDetectionFrames`、`minInputToSkipRelease`。
**應固定（⛔ 不要外露）**：shrink 模式（instant，因為 ramp 實測無差異）、
clearance inflate 量、latch 規則（到重新落地）——這三個只要給旋鈕就會被調成錯的。

### 12.11 量測方法與可信度

| | |
|---|---|
| 工具 | PlayMode spike，10 條 `[UnityTest]`，合成場景（平台／樓梯／30° 斜坡／牆／崖底地面） |
| 物理 | `fixedDeltaTime = 0.02`（50 Hz，專案實際值）；`Time.timeScale = 8` **只加快每個真實 frame 跑幾個 fixed step，不改 `fixedDeltaTime`** ⇒ 每一步的物理與正常執行逐位元相同 |
| 角色幾何 | `radius 0.32`／`height 1.8266189`／`center.y 0.9134095`／`skinWidth 0.03`／`stepOffset 0.3`／`slopeLimit 45`（＝ prefab 實際值） |
| ⚠️ 未涵蓋 | 真實場景的 collider 組成（合成幾何都是 box）；斜坡邊緣（只測了平面邊緣的旋轉，沒測**傾斜**的邊緣）；與 Capsule Offset V2 同時作用；多角色互相碰撞 |
| ⚠️ 不需要涵蓋 | 因為結論是否決——上面那些只會讓結論更差，不會翻盤 |

---

## 13. ⏹ 最終決策：`Deferred / Accepted Limitation`（2026-09-17，使用者明確裁決）

> **這是本文件唯一描述「現行正式行為」的一節。** §2–§12 全部是**已被否決的方案**的研究紀錄，
> 保留它們是為了不必重做量測，⛔ **不是**可以照著實作的規格。

### 13.0 正式行為

```text
data.IsGrounded  ==  CharacterController.isGrounded
                     （沒有 support radius、沒有 footprint、沒有 collision release、
                       沒有 outward push、沒有 edge slip）

極端平台邊緣：
  → 允許 capsule 側面仍被平台邊角支撐
  → 角色可能出現部分腳掌／腳尖懸空
  → ⛔ 不額外強迫掉落
```

**Grounded authority 的最終位置**：`MotionDriver.SyncGroundedState()`
（`Assets/Scripts/Presentation/Motion/MotionDriver.cs`）。
`MotionDriver` 仍是 `data.IsGrounded` / `JustLanded` / `JustLeftGround` / `VerticalVelocity` 的
**唯一寫入者**（由 `ArchitectureRegressionTests` 的 `WriterRules` 守住），
而那一行的內容**回到 Unity 原本的碰撞規則**：

```csharp
bool grounded = characterController.isGrounded;
```

⇒ authority **沒有搬家、也沒有分裂**；改變的只是「那個等號裡有沒有額外條件」——現在沒有。

### 13.1 實際退回了哪些變更

| 檔案 | 退回內容 |
|---|---|
| `Presentation/Motion/MotionDriver.cs` | 移除否決層（`&& HasValidStandingSupportThisFrame()`）、整個 `HasValidStandingSupportThisFrame()`、4 個 `[SerializeField]`（`requireValidStandingSupport`／`standingSupportRadius`／`standingSupportSearchDepth`／`standingSupportMask`）、每帧快取欄位與 `StandingSupportProbeLift` 常數 |
| ⛔ 刪除 `Presentation/Motion/StandingSupportProbe.cs` | 整個檔案（＋`.meta`） |
| ⛔ 刪除 `Tests/PlayMode/StandingSupportTests.cs` | 整個檔案（＋`.meta`），5 條測試 |
| `Core/Blackboard/PlayerRuntimeData.cs` | `IsGrounded` 的文件註解改回「本欄位 ＝ `CharacterController.isGrounded`」，並就地寫明這是**被接受的限制** |
| `Prefabs/X Bot`、`Y Bot` | 透過 Editor API 重新序列化，丟掉 4 個孤兒欄位（diff 為**純刪除 6 行 × 2**，正好是 `7823fdd` 的反向）。⚠️ **已複查 `m_Radius` 前後皆 0.32**，`height` 1.8266／`center` (0, 0.9134, 0)／`stepOffset` 0.3／`slopeLimit` 45／`skinWidth` 0.03 皆未變 |

**⛔ 本輪沒有碰的（逐條確認）**：`CharacterController` 的 authored radius／height／center、
`FootIKController` 與它的 grounding gate、`JumpState`／Falling entry／`FallEntryGrace`、
`RollState`、`TraversalProbe`／`TraversalState`／`TraversalClassifier`／traversal collision profile、
`StateType`（沒有新增 `LedgeExit` 之類的狀態）、`LocomotionModel`、`CharacterPipelineRunner`。

### 13.2 決策原因

1. **原始問題是極端平台邊緣的視覺 overhang，不是一般 locomotion correctness。**
   平地／樓梯／斜坡在導入前後都正確——這一點被 §5.2／§5.3／§12.2-F 三次獨立量測確認
   （樓梯、30° 斜坡的誤判數皆為 **0**）。⇒ 要修的是觀感，不是正確性。
2. **單純否決 grounding 只改狀態、不改位置。** FSM 認為 Falling，但 `CharacterController`
   的膠囊側面仍卡在 ledge corner，**實測 60 個物理步 Δy = 0.000、Δz = 0.000**（§10.2）。
   角色會停在空中不動，那比原本的問題更糟。
3. **Foot IK 依賴 `IsGrounded`。** 提早否決 ⇒ `FootIKController` 的閘門提早關閉 ⇒
   腳離開地面回到動畫姿勢 ⇒ **視覺反而更差**。使用者 Play 回報的「可以單腳完全浮空」
   很可能就是這個改動造成的（§10.3）。而這一項在 §9 第 2 點就被事先標成「最可疑」——它成真了。
4. **Outward push 雖能離邊，但代價是可見的自動滑動。** 實測：單次脈衝 20 個組合**全部失敗**；
   持續速度最壞需要 **0.35 m/s 持續約 0.9 s**（位移 ~0.3 m）（§11.2）。
   那是**玩家沒有輸入時角色自己滑出去近一秒**——屬於**新的 movement authority ＋ 手感設計決定**，
   不是一個 bug fix。
5. **Temporary Collision Release 雖能讓 gravity 接手，但恢復無解。** 實測最壞要把 radius
   從 **0.32 縮到 ≈0.10**（31%）；恢復時若牆面空間不足會**長時間維持小碰撞體**
   （latch 版 1.96 s，其中 **1.50 s 是落地後**，且需要玩家自己走開才恢復）；
   更嚴重的是**縮小期間角色可進入 authored capsule 原本到不了的空間，實測深達 0.50 m**（§12.5／§12.6）。
6. **footprint／support radius 可以改善「何時算失去支撐」，但不解決核心問題。**
   它們改的是判準，而核心問題是 **Unity `CharacterController` 的實體碰撞仍卡在邊角**。
   ⇒ 換判準只會更快進入第 2 點描述的矛盾狀態（§10.5 選項④）。
7. **完整解法的工程成本已超過作品集可見收益。** 要真正解決，必須開始要求：
   自訂／Kinematic movement solver（取代 `CharacterController.Move()`）、
   `SlideAlongSurface` 式的沿面滑移、額外的 movement authority、
   Foot IK 的中間語意（`SupportedButReleasing`），以及與 traversal 的協調。
   **UE 之所以沒有這個狀態，是因為它的角色本來就不是被碰撞撐住的**
   （`FindFloor` ＋ `AdjustFloorHeight` 把膠囊**放置**在浮貼區間）——
   那是結構性的答案，不是可以照抄的函式（§10.4）。
8. **因此接受「極端邊緣允許部分腳掌懸空」作為已知限制。**

### 13.3 🔓 未來重開條件

**只有下列情況之一發生時才重新研究這個問題：**

1. 改用**自製／Kinematic Character Motor**（不再由 `CharacterController.Move()` 解算移動）
2. **正式重寫 grounded / floor solver**（例如引入 UE 式的 `FindFloor` ＋ 浮貼區間）
3. **traversal / ledge gameplay 本身**需要精確的 support model（不是為了觀感，是為了玩法）
4. **實際展示中證明**此 overhang 明顯影響觀感或玩法

⛔ **否則不要再次為這個問題新增局部 workaround。**
「局部 workaround」包含但不限於：較小的 support radius、footprint 多點、暫時縮小碰撞體、
outward push／edge slip、只在某些狀態下否決 grounded、為 Foot IK 開特例閘門。
**這四類這份文件都量過了，全部是淨負面或成本過高。**

📌 重開時**先讀 §12.9 的四條結論**——它們適用於**所有**「改碰撞體形狀」的變體，
可以省下整輪重新量測。
