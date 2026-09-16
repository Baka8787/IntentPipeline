# 38 — Valid Standing Support（碰撞支撐 ≠ 可站支撐）

> **狀態**：研究 ＋ 兩個 throwaway spike 已完成。**⛔ 尚未接線，停在設計裁決點。**
> 本輪**沒有**改 CharacterController radius／prefab／JumpState／Falling entry／traversal，
> **沒有**把 Foot IK 升成 grounding authority，**沒有**新增 StateType。
> EditMode 596／595 passed 不變。

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

## 8. 現況與下一步

- Spike 檔案**保留**（裁決未定，數據需可重現）。⚠️ **裁決後若不採用 B，刪掉它。**
- ⛔ **沒有任何 grounding authority 被接進正式 state machine。**
- 外部原碼副本在 session scratchpad（`refs/`），**未進 repo**——
  它們是第三方原碼，本文已記錄來源與版本，重抓即可。

**等待裁決**：採用 B（`supportRadius` 取多少）／改用 C／維持現況。
