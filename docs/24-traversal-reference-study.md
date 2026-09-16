# 24 — Traversal 參考實作研究 ＋ 本 repo 鏈路診斷（2026-09-13）

> **狀態**：研究／診斷輪。**本輪不改 runtime**（使用者明確指示）。
> **產出**：五個問題的答案（附實際 source path）、一張對照表、本 repo 鏈路的 KEEP／改語意／缺層清單，
> 以及**本輪在使用者真實場景上實測到的三項證據**（§9）。
> **前情**：`docs/22` §13–§18（Phase 1／2 落地）、`docs/23`（模型審查）。
> **結論先講**：`docs/22` §18.7 之後的計畫（「下一步進 Phase 3 Hand IK」）**應該改掉**。
> 實測顯示 root mapping 已經是對的，**擋住玩家的是 entry legality 與感測範圍互相矛盾**，
> Hand IK 修不到那個問題。理由見 §9 與 §10。

---

## 1. 研究方法與實際讀過的東西

只列**真的讀到原始碼／原始文件**的部分，二手轉述另外標註。

| 參考 | 實際讀的檔案／頁面 |
|---|---|
| **AitorSimona/Traverser** | `Runtime/Scripts/Controller/TraverserAnimationController.cs`（`WarpToTarget`、`OnAnimatorMove`、`GetPositionAtTime`）、`Utilities/TraverserTransition.cs`、`Abilities/TraverserParkourAbility.cs`、`Abilities/TraverserClimbingAbility.cs`（`AdjustHandIK`／`OnAnimatorIK`）、`Environment/TraverserLedgeObject.cs` |
| **knela96/Dynamic-Parkour-System** | `System Controllers/AnimationCharacterController.cs`（`SetMatchTarget`）、`System Controllers/ClimbController.cs`（全部 `SetMatchTarget` 呼叫點、`CalculateIKPositions`）、`Helpers/Action Base Scripts/Action.cs`、`Vaulting Actions/VaultAction.cs` |
| **KINEMATION Motion Warping** | 官方文件 `concept/how-this-asset-works`、`fundametals/motion-warping-asset`、`fundametals/motion-warping-ik`；demo repo `Assets/Demo/Scripts/InteractorComponent.cs`（付費套件本體不可讀，演算法以官方文件為準） |
| **Unreal Motion Warping** | `dev.epicgames.com` Motion Warping 文件（warp target／AnimNotifyState 窗／Skew Warp／`Ignore Z Axis`） |
| **UE Game Animation Sample traversal** | 官方文件（章節極簡）＋ 社群資料。**二手**：warp target 名稱 `FrontLedge`／`BackLedge`／`BackFloor`、Chooser Table 以障礙物尺寸選動畫。標為二手，未讀到 uasset 內容 |
| **ALS（Sixze/ALS-Refactored）** | `Source/ALS/Private/RootMotionSources/AlsRootMotionSource_Mantling.cpp`、`Source/ALS/Public/Settings/AlsMantlingSettings.h` |

---

## 2. Q1 — Contact target 到底代表什麼？

### 2.1 結論

**沒有任何一個成熟實作把「finger 精確貼 surface」當成 root traversal constraint。** 一個都沒有。

實際使用的 target 由「最粗」到「最細」排序：

| 實作 | root warp 的 target 是什麼 | 精度期待 |
|---|---|---|
| **ALS mantling** | **單一 `TargetLocation` ＋ `TargetRotation`**（角色 actor transform），由 trace 到的 ledge ＋ **authored 純量 offset** `TargetLocationOffset = 15 cm`／`StartLocationOffset = 55 cm` 推出。**完全沒有手的 target，mantling 全程沒有 hand IK** | 公分級、authored |
| **UE Game Animation Sample** | **ledge 位置** warp target：`FrontLedge`／`BackLedge`／`BackFloor`（二手） | ledge 幾何點 |
| **Traverser（parkour）** | `contactTransform` ＝ **character controller 預測到的身體碰撞點**，再加 per-transition 的 `contactOffset`（沿 forward 的純量）；`targetTransform` 常常只是 `contactTransform.t.y += capsuleHeight * 0.55f` 這種**粗略身體高度偏移** | 身體級 |
| **Traverser（climbing 手部）** | IK target ＝ ledge 線上的點 `ledgeGeometry.GetPosition(hook)`，再 `handPosition -= forward * handLength; handPosition.y += handIKYDistance;`——**兩個 authored 純量**把「ledge 點」換算成「手腕該在哪」 | 由 authored offset 吸收 |
| **Dynamic Parkour System** | `AvatarTarget.LeftHand`（Unity `MatchTarget`）＋ **authored `BracedHangOffset`／`FreeHangOffset`（serialized Vector3，旋到 ledge 空間）**。很多情況只校正 Y：`HandPosition = LeftHand.position; HandPosition.y = curLedge.transform.position.y;`——**保留動畫自己的 X／Z，只把高度貼齊** | 單軸貼齊 |
| **KINEMATION** | `WarpPoint`（世界 transform）＋ **per-axis 開關**（`Use Warping` 可逐軸關閉） | 逐軸選擇性 |

### 2.2 兩個關鍵觀察

1. **「用手當 target」存在，但那是「用手當 goal 去解 root」，不是「把手釘死」。**
   Unity 的 `Animator.MatchTarget(pos, rot, AvatarTarget.LeftHand, …)` 做的事就是**移動 root**，讓指定的 avatar target
   在指定 normalized time 落到指定位置。DPS 用的正是這個。所以我們「解 root 讓某個身體點落在 ledge 上」**在種類上是對的、是主流做法**。
2. **但主流做法只用「一隻手」、只用「骨骼點」、而且允許 authored offset 吸收誤差。**
   DPS 即使是**雙手 braced hang**，`SetMatchTarget` 也**只給 `AvatarTarget.LeftHand`**——
   `MatchTarget` API 本身一次只接受一個 avatar target。**沒有人同時約束兩隻手再取平均。**

---

## 3. Q2 — Root warping 與 Hand IK 如何分工？

### 3.1 各實作的實際分工

**ALS**（`AlsRootMotionSource_Mantling.cpp`，最乾淨的公式）：

```cpp
WarpTransform       = Lerp(StartTransform, TargetTransform, LocationWarpAmount);
RootTransform       = ExtractRootTransformFromMontage(Montage, MontageTime);
TargetActorTransform= RootTransform * WarpTransform;          // 動畫 root motion 在「被 warp 的座標系」裡播
FinalRootMotion     = (TargetActorTransform.Location - Actor.Location) / DeltaTime;
```

- warp 的是**參考座標系**，不是位置加法；動畫自己的 root motion 原封不動地在那個座標系裡播放。
- `MotionWarpingTimeRange { 0.0, 0.3 }` 秒 ⇒ **對齊在前 0.3 秒內做完**，之後動畫照原樣播。
- location 與 rotation **各自有 blend option ／ custom curve**。
- rotation 取 twist-only（只留繞重力軸的分量）——與我們的 yaw-only 一致。
- **mantling 沒有任何 hand IK。**

**KINEMATION**（官方文件）：

```
offset = desiredDelta × (accumulatedRootMotion / totalRootMotion)
```

- correction 的內插參數是**動畫自己在該軸已經走了多少比例**，不是時間。
- 另外**縮放 play rate**：「障礙物長度是動畫設計長度的兩倍 ⇒ play rate 減半」，避免動量感錯亂。
- IK：四個 effector（雙手雙腳），weight 由 **Animator float curve** 控制，`Interp Speed` 平滑，
  用途明寫是「**防止 warp 後手腳滑動**，在 warped 動畫超出原始動作範圍時有用」。

**Traverser**：
- root warp 追的是 **hips 骨骼位置**（`skeletonPosition = GetBoneTransform(Hips).position`），
  沿一條 11 點曲線逐點追，剩餘時間由 animator normalized time 算；時間不夠就 **teleport**。
- `warpingValidDistance`（0.01–0.1 m）＝「**夠近就算成功**」的容差，到了就停止 warp。
- hand IK（climbing）：weight 取自動畫曲線參數 `IKLeftHandWeight`，
  target 由 ledge 點 ＋ authored offset ＋ **一條往牆面的 raycast** 修正，再以 `handsAdjustmentSpeed * dt` **平滑逼近**。

**DPS**：`MatchTargetWeightMask(Vector3.one, **0**)`——**position 全權重、rotation 零權重**；
IK target 由**每隻手各自一條 raycast**得到（`leftHandPosition = hit1.point`）。

### 3.2 對我們原則的驗證

> 我們的原則：「Warping 解決大尺度 body alignment；IK 只解 contact residual。」

**這條原則與成熟實作一致，可以保留。** 但成熟實作在它之上還有兩層我們沒有的東西：

| 層 | 成熟實作怎麼做 | 我們有沒有 |
|---|---|---|
| **① 動畫選擇（空間適配）** | 依量到的障礙物尺寸挑 clip（UE Chooser；ALS `MantlingHighHeightThreshold=125cm` 分 High／Low；DPS 每個 Action 一個 clip） | ❌ 每個 kind 只有一支 clip |
| **② 時間域適配** | ALS 依高度**推算 montage 起始時間**（`bAutoCalculateStartTime`、`StartTimeReferenceHeight{50,100}→StartTime{0.5,0}`）；KINEMATION 依長度**縮放 play rate** | ❌ 完全沒有 |
| ③ 空間域 warp | 全部都有 | ✅ 有（piecewise） |
| ④ IK residual | curve-driven、公分級、平滑 | ❌ 元件掛不上（Phase 3） |

**最重要的一句**：ALS 與 KINEMATION 都在用「**改動畫的時間**」去吸收「**動畫本來就走不到／走過頭**」的量。
我們在 Phase 1 正確地**移除了假的時間重映**（root 時間 ≠ pose 時間那個 bug），
但**沒有補上真正的時間域工具（play rate ／ start time）**，於是所有尺寸落差只能由位置 correction 硬吃。
`docs/22` §18.6 記的 Vault1m 0.666 m run-out，就是這個缺口的帳單。

### 3.3 IK 有沒有 correction 上限？

沒有人用「距離上限」當主要閘門，他們用的是**更早的三道防線**：

1. **weight 由動畫曲線給**（Traverser `IKLeftHandWeight`、KINEMATION `Control Curve Name`）——動畫說沒抓就不解。
2. **平滑逼近**（`handsAdjustmentSpeed`、`Interp Speed`）——不會瞬間拉。
3. **殘差本來就小**（warp ＋ 動畫選擇之後只剩公分級）。

我們的 `TraversalHandIKController.maximumResidualDistance = 0.35` 放棄門檻**方向正確**（拒絕用 IK 搬幾十公分），
但它是在補前面三道防線都沒有的洞。

---

## 4. Q3 — Ledge contact frame 如何定義？

| 實作 | 存了什麼 | 左右手 target 怎麼來 |
|---|---|---|
| **Traverser** | `TraverserLedgeGeometry` ＝ **box 頂面四個頂點 ⇒ 四條邊**；`TraverserLedgeHook` ＝ `(edge index, distance along edge)`；`GetNormal(index) = -Cross(edge, up)` | **每隻手各自求 hook**：`GetHook(IKPosition - forward*0.3)` ⇒ 各自的 ledge 點，再各自 raycast 修正 |
| **DPS** | ledge 上的 `Point` 物件陣列（`HandlePoints`），`GetClosestPoint(handPosition)` | **每隻手各自 `GetClosestPoint` ＋ 各自 raycast**（`hit1`／`hit2`） |
| **ALS** | `TargetPrimitive`（可移動）＋ `TargetLocation` ＋ `TargetRotation` ＋ `MantlingHeight` | 不存手 |
| **UE GASP** | `FrontLedge` / `BackLedge` / `BackFloor`（位置＋法線，二手） | 不存手 |
| **KINEMATION** | `WarpPoint` transform ＋ per-phase `T Offset`／`R Offset` | 不存手 |

**答案**：
- 會建立完整 frame 的只有 Traverser（edge＋normal＋沿邊參數）與我們。ALS／UE 只存 **transform ＋ 高度**。
- **左右手 target 沒有人用「同一點 ＋ 固定 X offset」**——有手 target 的兩家（Traverser／DPS）都是
  **每隻手獨立對環境查詢**（raycast／最近點），因為真實牆面不是平面。
- 我們 §18.3 從「對稱擺放」改成「每隻手用自己的動畫橫向偏移」，**方向與他們一致**；
  但我們仍然是**從動畫推目標**，沒有像他們一樣**再對真實表面查一次**。

---

## 5. Q4 — 動畫 contact timing 如何取得？

| 手段 | 誰在用 | 評語 |
|---|---|---|
| **Animation notify / notify state 窗** | UE Motion Warping（`AnimNotifyState_MotionWarping`，start/end time）、UE GASP | 產業標準。**窗是 authored 的** |
| **Authored warp 時間區間** | ALS `MotionWarpingTimeRange{0,0.3}`、KINEMATION per-phase `Start Time`／`End Time`、DPS 硬寫的 `0.56` | 全都是人填 |
| **曲線** | Traverser／KINEMATION 的 **IK weight curve**（動畫自己說「這時候手是黏的」） | 用在 IK weight，不是用在 root 窗 |
| **Bone trajectory 分析（bake 期自動測）** | **沒有人**（在這五個裡） | 我們是唯一一個 |
| **Runtime 偵測** | Traverser hand IK 的 raycast（對表面，不是對時間） | 只修空間，不定時間 |
| **查詢動畫「某時刻某 AvatarTarget 在哪」** | Traverser `GetPositionAtTime()`：`animator.SetTarget(target, t); animator.Update(0); position = animator.targetPosition;`；Unity `MatchTarget` 內部同理 | **值得移植的觀念** |

### 值得移植 / 不值得移植

**值得**：
- ✅ **「動畫決定 WHEN」用曲線表達**——尤其 **IK weight curve**。我們 `TraversalHandIKWindow` 已經有
  `start/full/release`，方向對，但目前三支 bake 全是 0（`handIKEnabled: 0`）。
- ✅ **「查詢動畫在某 normalized time 的某個 target 在哪」**當成一級 API。我們的
  `MotionBakeData.Traversal.*Anchor` 就是它的離線版，這個設計是好的。
- ✅ **per-phase「Total Root Motion」**（KINEMATION）：把「這支動畫在這一段自己能走多遠」變成可讀資料，
  才有辦法回答「要不要改 play rate」。我們的 bake 有曲線但沒有把它做成 per-phase 預算。

**不值得**：
- ❌ **把自動測出的 marker 當成「精確的物理接觸事實」**。`TraversalContactDerivation` 用
  「速度 < 自身最大速度 15% 且持續 3 格」測出來的窗，是一個**啟發式**，不是接觸的真值。
  §9.2 的實測顯示：在被判定為「抓握中」的 `n=0.065→0.269` 內，手腕在世界空間仍然**上升了約 11 cm**。
  那不是 bug（手腕本來就會在指尖不動時繞著邊緣轉上去），但它證明
  **「plant window」≠「該凍結 root correction 的區間」**。我們 §16 把兩者當同一件事。
- ❌ **把六個 knot 全部自動推導**。ALS 只有兩個 transform ＋ 一個時間區間就做到了穩定的 mantle。
  knot 越多，每個都可能錯，而且**互相之間的一致性沒有人檢查**——§18 的三個 bug 全是這一類。

---

## 6. Q5 — 「手一定貼牆」方案的結構性問題

先講一句最重要的：**我們目前的鏈路裡，root solve 用的不是骨骼點，而是一個合成點。**

`TraversalContactDerivation.SampleGripEdge` 回傳的 `gripEdgeInRoot` ＝

```
x = 手腕的橫向座標
y = 「動畫自己的障礙物頂面高度」− 接觸瞬間 root 已上升的高度      ← 不是手的高度
z = 五根手指 distal 之中最前面那根的 z                            ← 不是手腕的 z
```

**這三個分量來自三個不同的身體部位／概念。** 它是一個為了對齊而發明的虛擬點，
不對應任何骨骼，因此**沒有任何一條 IK 鏈可以驗證它**，也沒有辦法用「手腕該在哪」反推它對不對。
這是我們與五個參考實作最大的結構差異——他們對齊的都是**真實骨骼**或**純粹的世界 transform**。

### 依 A–E 分類的診斷

| 類別 | 我們現在的狀態 | 會不會造成使用者看到的症狀 |
|---|---|---|
| **A. Root mapping** | ✅ **目前是對的**。§9.2 實測：站在 0.15–0.80 m 任一位置，接觸瞬間手腕相對真實邊緣**永遠**是 −0.162 m（下方）／+0.09 m（外側），與動畫在自己障礙物上的關係完全一致，且**與站位無關** | ❌ 不是現在的主因 |
| **B. Contact frame** | ⚠️ **語意混淆**。`gripEdge` 是三部位合成點；且我們只從動畫推、**不對真實表面二次查詢**（Traverser／DPS 都查）。牆面若有厚度差、傾角、貼皮，誤差直接進 root | ⚠️ 潛在，目前量級小 |
| **C. Animation timing** | ⚠️ **缺一整層**。沒有 play rate、沒有 start time scrub、沒有多 clip 選擇。**所有尺寸落差只能由位置 correction 吃** | ✅ **是 Vault run-out 0.666 m 的直接原因** |
| **D. IK solver** | ❌ 不存在（元件掛不上、bake 的 `handIKEnabled=0`、window 全 0） | ❌ 但它**不是**現在的瓶頸 |
| **E. Finger pose** | 動畫自己是對的（`docs/22` §17.1 已量測）。我們沒有、也不該去改手指 | ❌ |

### 針對使用者點名的六個現象

| 現象 | 真正的歸因 | 理由 |
|---|---|---|
| 手貼到了但**肩膀變形**／**elbow 異常** | **D**，但前提是 IK 真的開了。現在沒開 ⇒ **目前不可能是這個** | 兩段式 IK 把手拉到超出臂長／超出關節極限時必然扭曲。所以 IK 的殘差預算必須是公分級 |
| **身體離牆不自然** | **B**（對齊點選錯會平移整個角色）。§17 已修：改對齊 grip edge 之後 root 從牆後 0.359 m 退到 0.462 m | 對齊手腕等於把角色往前推 10 cm |
| **手滑** | **C** ＋ **B**：correction 在「動畫自己 root 不動」的區間內變化，手就被拖著滑。§16 用「抓握期間凍結」硬解 | KINEMATION 用 `accumulatedRootMotion / totalRootMotion` 當內插參數，動畫不動時 correction 自動不動——**同樣的效果，但不需要一條特例規則** |
| **左右手不同步** | **B**：雙手同時當硬約束再取平均，是我們獨有的做法。`MatchTarget` 一次只能一個 target；DPS 雙手 braced hang 也只約束左手 | 兩個絕對解平均 ⇒ 兩手都不準，且誤差方向互相抵銷後看起來像「整體偏移」 |
| **root endpoint 錯** | **C**：終點是「動畫自然落點 vs 探測落腳面」二選一，而動畫的自然落點無法縮放 | §18.4 已修一半（取較近者），剩下的要靠 play rate |
| **動畫結束時 snap** | 設計上被 `TryEvaluatePiecewise` 的 `normalized >= exit ⇒ position = TargetPosition` 硬夾住 | 這是刻意的 positional contract；只要 exit 前後不連續就會 snap |

---

## 7. 對照表

`Reference → Detection → Warp Target → Root Warp → Contact → IK`

| Reference | Detection | Warp Target | Root Warp | Contact | IK |
|---|---|---|---|---|---|
| **ALS mantling** | capsule/sphere trace，`LedgeHeight{50,225}`、`ReachDistance 75cm`、坡度／角度閘門 | **1 個** `TargetLocation`＋`TargetRotation`（＋`TargetPrimitive` 支援移動物） | 座標系 Lerp ＋ 動畫 root motion 疊加；窗 `{0,0.3}s`；location/rotation 各自 blend curve；yaw-only | **無手 target**；用 `TargetLocationOffset 15cm` 吸收 | **無** |
| **UE Motion Warping / GASP** | trace ⇒ 障礙物尺寸；Chooser Table 選 montage（二手） | **多個具名 target**：`FrontLedge`／`BackLedge`／`BackFloor` | `AnimNotifyState_MotionWarping` 窗 ＋ Skew Warp；可 `Ignore Z Axis`、分開開關 translation／rotation | ledge 幾何點 | 文件未提；靠**選對動畫** |
| **KINEMATION** | 由 demo 的 interactor 決定 | `WarpPoint` ＋ per-phase `T/R Offset`，多段（JumpOver ＝ 近緣／遠緣／落點） | `offset = desiredDelta × (accumRootMotion / totalRootMotion)`，**per-axis 開關**，**play rate 縮放**，warp 期間 `CharacterController.enabled = false` | warp point | 四 effector，weight 由 **animator curve**，`Interp Speed` 平滑，用途＝**防滑** |
| **Traverser** | 自家 character controller 的碰撞預測 ＋ `TraverserLedgeGeometry`（頂面四邊） | `contactTransform`（身體碰撞點＋`contactOffset`）→ `targetTransform`（＋`targetOffset`） | 追 **hips**，11 點曲線，剩餘時間由 normalized time 算，不夠就 teleport；`warpingValidDistance 1–10cm` 即算成功 | 身體級；手部另外處理 | `IK*HandWeight` 動畫曲線 ＋ ledge hook ＋ **raycast** ＋ authored `handLength`／`handIKYDistance` ＋ 平滑 |
| **DPS** | 多條 raycast（knee ray、ledge points） | `AvatarTarget.LeftHand` ＋ authored `BracedHangOffset`（Vector3） | Unity `MatchTarget`，`MatchTargetWeightMask(Vector3.one, 0)`（rotation 權重 0），窗 `[startTime, 0.56]` | **單手**；常只貼齊 Y | 每手各自 raycast ⇒ `AvatarIKGoal` |
| **本 repo（現況）** | `TraversalProbe` 階梯掃描＋頂面／深度探線＋corridor capsule | **6 個 knot**：entry／L contact／R contact／transfer／exit／recovery；contact target ＝ **合成 grip edge** | piecewise：knot 間 smoothstep（**時間**參數），絕對 plan／相對 delta 經 `CharacterController.Move` | **雙手同時硬約束再平均** ＋ `ClampOutsideWall` | **不存在**（元件掛不上、bake `handIKEnabled=0`） |

---

## 8. 本 repo 鏈路審查

`Probe → Selection → TraversalPlan → Piecewise Root Warp → Contact Mapping → Animator IK`

### ✅ KEEP（與成熟實作一致，不要動）

| 項目 | 為什麼 |
|---|---|
| **Probe 產出完整 ledge frame**（edge／wallNormal／tangent／topNormal／tangent 區間） | 比 ALS／UE 存得更完整；Traverser 同級。這是資產 |
| **Selection 帶理由**（`TraversalSelectionReason`） | 沒人做得比這更好；靜默否決是這個領域最大的除錯黑洞 |
| **多段 warp（piecewise）** | KINEMATION 的 phases、UE 的多 warp target 都是同一結構。**方向完全正確** |
| **root 時間 ≡ 動畫時間**（Phase 1 的修正） | ALS 的公式本質相同：動畫 root motion 原樣播，warp 只改座標系 |
| **yaw-only 旋轉空間** | ALS 明寫 `GetTwist(WarpRotation, -GravityDirection)` |
| **bake 存 anchor（某時刻某部位在哪）** | Traverser `GetPositionAtTime` 的離線版；Unity `MatchTarget` 的前提 |
| **IK 有放棄門檻** | 方向對（IK 不搬大位移） |

### 🔄 需要改語意（不是砍掉，是改定義）

| 項目 | 現在的語意 | 應該的語意 | 依據 |
|---|---|---|---|
| **Contact target** | 合成 `gripEdge`（三部位混合），雙手同時硬約束、取平均 | **主手一個骨骼點 ＋ authored offset**；另一隻手降為 IK／debug 用途 | DPS 只約束 `LeftHand`；`MatchTarget` 天生單 target（§2.2） |
| **Correction 內插參數** | 時間（knot 間 smoothstep）＋ 一條「抓握期間凍結」特例 | **該軸的 root motion 進度**：`accumRootMotion / totalRootMotion` | KINEMATION 公式（§3.1）；自動得到「動畫不動時 correction 不動」 |
| **Correction 權重** | 單一純量吃三軸 | **per-axis**（UE `Ignore Z Axis`、KINEMATION per-axis 開關） | §7 |
| **接觸精度** | 追求解析上的 0 誤差 | **「夠近就算成功」的容差**（Traverser `warpingValidDistance` 1–10 cm） | §3.1 |
| **Entry legality** | 從動畫反推「玩家必須站在哪」，站不對就**拒絕** | 動畫去配合玩家：**選 clip／改起始時間／改 play rate** | ALS `bAutoCalculateStartTime`；§9.3 是這條的帳單 |
| **`TraversalContactDerivation` 的 plant window** | 當成「該凍結 root 的區間」 | 當成 **IK weight 窗**（動畫說手黏著的期間） | §5；實測手腕在窗內仍移動 11 cm |

### ❌ 缺了一層（這是最重要的結論）

**缺的是「時間域／動畫選擇」層，位置在 `Selection` 與 `TraversalPlan` 之間。**

```
Probe → Selection →  ❌【Animation Fitting】  → TraversalPlan → Root Warp → Contact → IK
                      ├ 依量到的尺寸選 clip（多支）
                      ├ 依高度推算起始 normalized time（ALS）
                      └ 依長度縮放 play rate（KINEMATION）
```

五個參考實作**全部**都有這一層的某種形式，我們**一個都沒有**。
沒有它，任何「動畫想要的尺寸」與「現場尺寸」的落差都只剩一個出口：加大位置 correction。
於是就會出現「把 `maxHorizontalCorrection` 從 1.5 調到 2.5 再調回來」這種反覆
（`docs/22` §14.4 vs §15.2），以及 Vault 永遠吃不掉的 0.666 m run-out。

**次要缺口**：Contact mapping 之後**沒有對真實表面再查詢一次**（Traverser 的 hand raycast、DPS 的雙手 raycast）。
目前完全信任「動畫推出來的點」。

---

## 9. 本輪在使用者真實場景上的實測（新證據）

工具：`unity cmd eval_file` 驅動使用者開著的 Editor；臨時 rig 以 `HideFlags.HideAndDontSave` 建立，
用完即刪、**不動使用者場景**；動畫取樣在 `NewPreviewScene` 內。

### 9.1 補跑測試：**有一條紅的**

`docs/22` §18.7 記「尚未跑過測試」。本輪補跑（EditMode）：

**428 total／426 passed／1 failed／1 existing skipped**

```
Project.Tests.EditMode.TraversalWarpPlanTests.M6_NarrowLedge_ShrinksAndClampsHandSeparation
  Expected: greater than or equal to -0.0700999945f
  But was:  -0.200000003f
```

§18.3 把「authored separation 對稱分到中心兩側」改成「每隻手用自己的橫向偏移」之後，
**窄 ledge 不再壓縮雙手間距**，於是手會落在合法區間外。這是真實的行為改變，不是測試 flake。
（研究結論支持**不要壓縮間距**——壓縮等於偽造動畫做不到的姿勢——但那代表這條測試編碼的是**已被取代的 baseline**，
必須在同一個工作包內更新，而不是留著紅燈。本輪依指示不動。）

### 9.2 Climb2m 的 root mapping：**是對的，而且與站位無關**

離線重建完整鏈路（真實 probe ＋ 真實 params ＋ 真實 2 m 箱 ＋ 逐格取樣真實 clip 的手骨）：

| 站位（離牆） | 接觸瞬間手腕相對真實邊緣 |
|---|---|
| 0.15 m | 外側 +0.092 m／下方 −0.162 m |
| 0.30 m | +0.094／−0.162 |
| 0.46 m（理想 0.462） | +0.097／−0.162 |
| 0.60 m | +0.099／−0.162 |
| 0.80 m | +0.102／−0.162 |

而動畫自身在自己的障礙物上就是 **−0.163 m／+0.1025 m**（`docs/22` §17.1 的量測）。
⇒ **誤差 ≤ 1 cm，且五個站位幾乎一致。**

**結論：root mapping 已經不是問題。** 「手抓的位置看起來不對」剩下的量級是
「手腕本來就在邊緣下方 16 cm、後方 10 cm」——那是**這支動畫的抓握姿勢**，不是誤差。

### 9.3 ⛔ 真正擋住玩家的：**entry 合法窗與感測範圍互相矛盾**

| 量 | 值 | 來源 |
|---|---|---|
| `TraversalProbeSettings.ForwardScanDistance` | **1.25 m** | 場景 prefab 實際序列化值 |
| Vault1m 由 bake 推導的理想進場距離 | **1.402 m** | `MotionBakeData.TryGetDesiredEntryDistance()`（§15／§18.5） |
| Vault1m 的合法帶 | **[1.00, 1.75] m** | ideal − `MaximumCloseError 0.40` ／ ＋ `MaximumFarError 0.35` |

**動畫要求玩家站在 1.402 m，但 probe 只看得到 1.25 m 以內。理想點在感測範圍之外。**

在使用者場景的 1 m 薄牆（`Wall_Prefab` x=15.255，站在 ramp 上 groundY=0.31）實測：

```
d=0.20 … 0.97   kind=Vault1m  corridor=clear  entry exec=False  reason=TooCloseUnsafe
d=1.10          kind=Vault1m  corridor=clear  entry exec=True
d=1.25          kind=Vault1m  corridor=clear  entry exec=True
d≥~1.3          （超出 ForwardScanDistance，量不到）
```

⇒ **可執行的站位只剩 1.10–1.30 m 這條約 20 cm 寬的縫。**
玩家走到牆前按 Jump（0.2–0.9 m）**一定**被 `TooCloseUnsafe` 拒絕。
加上 `climb1mEnabled = 0`（使用者 2026-09-13 裁決停用 Climb1m），
**場景中所有 1 m 級障礙物在實際遊玩中幾乎都不會有反應。**

這解釋了「改了跟沒給一樣」：**Phase 1／2 修的是 traversal 開始之後的事，
而玩家多數時候根本沒有觸發 traversal。** Hand IK（Phase 3）同樣修不到這個。

> ⚠️ 過程中的一個自我更正：第一次掃描時我把臨時 rig 放在 y=0、並把箱子遠面當成近面，
> 得到「所有 1 m 障礙物都 CorridorBlocked」的假結論。改成**先射線找地面高度、再用真實近面**之後，
> corridor 其實**全部是 clear 的**。這正是 `WORKLOG` 已經記過的「拿自己編的測試資料當證據」。
> 上表是修正後的資料。

---

## 10. 建議的下一步順序（本輪不執行）

依「擋住玩家的程度」排序，不是依技術趣味：

1. **修好 entry 合法窗 vs 感測範圍的矛盾**（§9.3）。
   最小修法：`ForwardScanDistance` 至少要 ≥ 各 clip 推導進場距離的最大值 ＋ `MaximumFarError`。
   但這只是止血——真正的解在第 2 點。
2. **補上 Animation Fitting 層**（§8「缺了一層」）：先做 **play rate 縮放**（KINEMATION 式），
   讓「動畫想要 1.402 m、玩家站 0.6 m」不再是拒絕理由，而是把助跑段壓縮掉。
   這同時解掉 Vault1m 的 0.666 m run-out（§18.6 自己也寫了「要靠修剪播放區間或改 playback speed」）。
3. **把 correction 的內插參數從「時間」改成「該軸 root motion 進度」**，
   並把 §16 的「抓握期間凍結」特例移除（會自動成立）。
4. **Contact target 降為單主手 ＋ authored offset**；另一手交給 IK／debug。
5. **修掉 `M6` 紅燈**（§9.1）——連同上面第 4 點一起改測試語意。
6. **最後**才是 Phase 3 Hand IK：拆檔案、bake 的 `handIKEnabled`／window 補值、
   weight 走動畫曲線、平滑逼近。**在 1–4 完成前做它，等於用 IK 去蓋前面每一層的誤差。**

---

## 10. 工作包一 ——「感測範圍由動畫需求推導」＋「Animation Fitting v1」（2026-09-13）

> **狀態**：runtime ＋ tests 完成，live Editor recompile 0 errors。
> **EditMode 441 total／439 passed／1 failed（`M6`，屬工作包二）／1 existing skipped；PlayMode 41／41。**
> ⚠️ **尚未 Play 驗收**——以下數字全部來自離線重建（真實場景幾何 ＋ 真實資產 ＋ 逐格取樣真實 clip）。

### 10.1 為什麼不是「把 1.25 換成另一個數字」

使用者明確要求：**detection range 與 animation-derived entry requirements 之間要有明確關係。**
因此感測範圍改成**推導量**，公式寫在 `TraversalStateParamsSO`：

```
RequiredSensingReach = max over 啟用中的 kind (
        該支 bake 推導的理想進場距離          // TryGetDesiredEntryDistance()
      + EntryPolicy.MaximumFarError           // 合法進場帶的遠端
  ) / cos(EntryPolicy.MaximumFacingAngle)     // 射線沿角色 forward 打，站歪時路徑更長

RequiredLandingDepth = max over 啟用中的 kind ( 該支 bake 的自然落點深度 )
```

- 推不出進場距離的 kind（缺 Traversal block）退回全域 `DesiredWallDistance`，與 EntryPolicy 的退化路徑一致。
- **停用的 kind 不計入**：停用是內容決策，不該讓感測範圍替不會發生的動作買單。
- Probe 取 `max(authored, derived)`——**authored 值變成下限，不是上限**。

**方向與層次**：`TraversalStateParamsSO` 算出來，`TraversalState` 推給 `TraversalProbe`
（`SetDerivedRangeRequirements(reach, landingDepth)`，`Initialize` 與每次 `CanEnter` 各推一次）。
Probe 只收兩個 `float`，**不反向認識 bake／binding**——LayerRule（`Core/Environment` 禁止依賴
StateMachine／Presentation）原樣成立。每次 `CanEnter` 重推，是為了避免又出現
「推導值從來沒生效」那類靜默失效（`docs/22` §13.8 的教訓）。

實測推導值（正式資產，Climb1m 停用中）：

| 量 | 舊（authored 常數） | 新（推導） |
|---|---|---|
| 前掃距離 | 1.25 m | **2.138 m** |
| 落腳面掃描上限 | 0.60 m（`Vault1mMaxDepth`） | **1.116 m** |

### 10.2 落地預算與分類證據分離

`Vault1mMaxDepth`（0.6 m）同時被當成兩件事用：**分類證據**（表面是否延續 ⇒ 實心平台 ⇒ Climb）
與 **committed 終點**。後者是錯的——動畫的自然落點與這個常數無關。

現在：`depth`（分類）維持原樣不動；另外新增 `TraversalCandidate.LandingSurfaceDepth`，
由 Probe 沿落腳面往內**續掃到動畫需要的深度**為止，回報最遠仍連續的落腳點。
⚠️ 兩種動作都要掃，而且**連續性要對照落腳面本身**：

- **Climb**（表面延續）：落腳面＝平台頂面 ⇒ 沿頂面往內掃。
- **Vault**（薄牆）：落腳面＝**牆另一側的地面** ⇒ 沿那個地面往外掃。
  只掃前者的話，Vault 的 run-out 永遠掃不到。

`TraversalPlanBuilder` 的 `min(自然落點, 探測到的落腳面)` 規則不變——現在它拿到的是**量出來的**落腳面，
而不是一個常數。

### 10.3 Animation Fitting v1：只做 playback rate

新增 `Assets/Scripts/Core/StateMachine/TraversalAnimationFit.cs`：
純函式 `TraversalAnimationFitter.Fit(bake, candidate, entry, settings) → TraversalAnimationFit`。

```
approachAnimated = H(第一隻手接觸的時刻)            // 動畫自己的助跑
approachRequired = approachAnimated + 站位誤差       // 這次實際要走的助跑
rate             = approachAnimated / approachRequired   （夾在 [0.6, 1.6]）
```

語意來自 KINEMATION：**讓世界速度維持動畫本來描繪的速度**。需求較短 ⇒ 播快（時間同比例縮短）。

⚠️ **rate 不會改變動畫的位移量**，它改變的是走完那段位移所花的時間。
因此它**不能取代 correction**，只能讓殘餘 correction 的速度合理、滑步不明顯。
真正把 correction 量降下來的是 §10.2 與 §11。

**說得出口的退化**：`NotFitted`（關閉，或這支動畫接觸前 root 根本不動——`Climb2m` 就是 `H(contact)=0`）／
`RateClampedFast`／`RateClampedSlow`（夾限也保留 `RawPlaybackRate`）／`MissingBakeData`／
`DegenerateRequirement`。⛔ 不會靜默生出一個數字。

**套用點**：`TraversalState` 在動畫真的開始播之後呼叫
`AnimationFacadeBase.SetPlaybackSpeed(key, rate)`（新增的 virtual，預設 no-op；`AnimancerFacade` 覆寫）。
⚠️ Animancer 依 transition key 重用 state 物件，**速率會留存** ⇒ `OnExit` 必須設回 1。

### 10.4 實測結果（真實場景，離線重建）

**1 m 薄牆（`Wall_Prefab`，站在 ramp 上，ledge 實高 0.699 m）：**

| | 修正前 | 修正後 |
|---|---|---|
| 可執行站位 | 1.10–1.25 m（約 **20 cm**） | **1.00–1.70 m（0.70 m）** |
| 理想站位（1.402 m）能否偵測 | ❌ 超出 1.25 m 前掃 | ✅ |
| 理想站位・**水平** contact correction | — | **0.015 m** |
| 理想站位・**水平** exit correction | 0.765 m | **0.001 m** |
| playback rate | 不存在 | 1.60（夾）@1.00 ／ 0.97 @1.40 ／ 0.62 @1.70 |

水平 correction 現在與「站位偏離理想多少」**一比一**（0.385 @1.00、0.015 @1.40、0.315 @1.70）——
這是誠實的：站偏多少就補多少，不再額外吃動畫的尺寸差。

⚠️ **仍然存在且不屬本工作包的殘留**：同一面牆的 **垂直** correction 固定 −0.33 m，
因為 `Vault1m` 是對約 1.02 m 高的障礙物做的，而這面牆實高只有 0.699 m。
它在所有站位上都一樣大 ⇒ **是障礙物與動畫不匹配，不是玩家站錯**。
正解是 **clip selection**（依量到的高度挑動畫），已明確延後。

### 10.5 Debug

`Window ▸ Project ▸ Traversal Debug` 決策鏈新增第四行：

```
4 Fit    rate 0.971
   approach animated 0.519 m → required 0.519 m
   landing animated 1.116 m   probe verified 1.100 m   committed 1.100 m
   sensing reach 2.14 m   landing scan 1.12 m   (derived from bakes)
```

被夾住時顯示 `(raw 3.114, CLAMPED)`；未 fit 時顯示 `rate 1.000 — <Reason>`。
Scene View 與 Game View 的規則不變（`docs/22` §16.2：Scene View 只畫幾何，Game View 什麼都沒有）。

### 10.6 測試

| 測試 | 驗什麼 |
|---|---|
| `F1`–`F7`（`TraversalAnimationFitTests`） | 關閉／缺 bake／理想站位 rate≈1／近播快遠播慢且數值符合公式／夾限要回報不得靜默／`Climb2m` 不得憑空生 rate／落點取小 |
| **`R1`** | **感測範圍必須涵蓋每個啟用中 kind 的合法進場帶** ← 這條就是本次 bug 的守門員 |
| `R2` | 落腳面掃描上限必須涵蓋每支動畫的自然落點 |
| `R3` | Probe 取 `max(authored, derived)`；推導值較小或 `NaN` 時安全退回 authored |
| `B12` | 自然落點深度必須量在 **Exit marker**，且與「量在 clip 結尾」明顯不同（見 §11） |
| `B13` | 理想站位的**水平** contact／exit correction < 5 cm |
| `A50` | Animation Fitting 是純函式：不得出現 `Physics.`／`Time.`／`transform.`／`PlayerRuntimeData`／`TraversalProbe`／`AnimationFacadeBase`／`MonoBehaviour` |
| `A44`（更新） | `Core/StateMachine → Core/Environment` 的 seam 白名單加入 `TraversalAnimationFit.cs`，理由寫在測試裡 |

---

## 11. 一個被 §10 暴露出來的舊 bug：自然落點量錯了時間點

把終點交給「動畫自己的落點」之後，Vault 的 exit correction 反而更大（0.765 m）。追下去發現：

`MotionBakeData.TryGetNaturalExitDepth()` 原本量在 **clip 結尾**：

```
Vault1m：H(1.000) = 3.259 − 進場 1.402 ⇒ 舊值 1.857 m
```

但這個值唯一的消費者是 warp plan 的 **Exit knot**，而那個 knot 在 `ExitNormalizedTime = 0.460` 觸發，
當下動畫只走到 `H(0.460) = 2.519` ⇒ 實際落點深度是 **1.116 m**。
**差的 0.74 m 全部變成 exit correction 把角色往前扯。**

> 這是第三次同型的缺陷：**兩處用了不同基準**（§18.2 進場距離 vs 接觸解算、§13.8 authored vs 建構子預設）。
> 判準很簡單：**一個量要在哪個時刻被消費，就得在那個時刻量。**

修法：改量在 `ExitNormalizedTime`。`Climb2m` 的 exit 之後是靜止站姿，兩種量法同值（0.213），
因此這個 bug 只在 Vault 顯現——這也是它活到現在的原因。回歸測試 `B12`。

---

## 12. 工作包一之後仍然開著的項目

| 項目 | 歸屬 |
|---|---|
| `M6_NarrowLedge_ShrinksAndClampsHandSeparation` 紅燈 | **工作包二**（contact 語意） |
| 合成 `gripEdge` 的語意、單一 primary contact、secondary hand 表面查詢 | **工作包二** |
| Vault 在**矮**障礙物上的垂直 correction（−0.33 m） | clip selection（Animation Fitting v2） |
| Exit 之後 root 被凍結、run-out 在原地播（`TryEvaluatePiecewise` 的 `normalized >= exit ⇒ TargetPosition`） | 待評估：屬 root warp 語意，可能與工作包二一起 |
| Hand IK（Phase 3） | **工作包二完成後**再評估 |

---

## 13. Playtest 點名的兩個可見缺陷（2026-09-13）

> 使用者回饋原話：**「我只知道我看不順眼的地方只有上去的瞬間手指插進牆面，以及動畫結束會有微小浮空，
> 這點也有落地聲佐證。」**
> §10–§11 修的東西（觸發窗、correction 量）**使用者完全感受不到**——那是誠實的回饋，
> 記在這裡提醒：**改動的價值由「看不看得到」決定，不是由「數字有沒有變好」決定。**

### 13.1 手指插進牆面 —— grip edge 的 Y 基準取錯了

`SampleGripEdge` 的 Y 原本取**動畫自己的障礙物頂面**（由 `VerticalCurve` 推出的統計量）。
實測 `Climb2m` 抓握格（執行期 rig `Y Bot`），四根手指的**指尖**（distal 的葉節點）相對那條平面是：

| 指尖 | 距牆面（負＝牆裡） | 相對頂面 |
|---|---|---|
| 食指 | −0.034 | **−0.002** |
| 中指 | −0.046 | **+0.009** |
| 無名指 | −0.054 | **−0.001** |
| 小指 | −0.047 | **−0.013** |

**越過牆面 3–5 cm 是對的**——手指本來就要扣過邊緣。錯的是它們同時**騎在頂面上下各約 1 cm**：
越過邊緣 ＋ 高度在頂面以下 ＝ 指尖落在轉角的實心裡。再加上手指有厚度，骨骼點貼齊平面＝網格埋一半。

**修法**：grip edge 的 Y 改由**真正會壓在頂面上的四根手指指尖**決定（拇指排除——它按的是牆面，
實測低 12.6 cm），取最低的那一根，再讓出 `FingerContactClearance = 0.01 m` 給手指厚度。

⚠️ 這個 clearance 是 **animation／contact fitting 的 authored 量**，不是 probe geometry；
它只在 bake 期進入 grip edge，執行期不會有第二個地方再加減一次。

**結果**（同一支 clip、同一個 rig，重新烘焙後）：

| 指尖 | 修正前 | 修正後 |
|---|---|---|
| 食指 | −0.002 | **+0.021** |
| 中指 | +0.009 | **+0.032** |
| 無名指 | −0.001 | **+0.022** |
| 小指 | −0.013 | **+0.010** |

四指全部浮在頂面上方 1–3.2 cm，水平上仍越過邊緣 1–3 cm（扣過去），拇指落在牆面（out +0.001、dy −0.089）。
回歸測試 `B14_GripEdgeAlignment_KeepsFingertipsAboveTheTopFace`。

### 13.2 結尾微小浮空 ＋ 落地聲 —— 同一個原因

證據鏈（全部可在程式裡指到行）：

1. Traversal 期間角色由 plan 的**逐格位移**推動；過了 Exit knot 之後 plan 位置**凍結**
   （`TryEvaluatePiecewise` 的 positional contract）⇒ 每格位移正好是 0。
2. `CharacterController.Move(Vector3.zero)` **不會回報觸地** ⇒ `isGrounded` 整段 traversal 都是 false。
3. `FootIKController` 的閘門就是 `data.IsGrounded && !BlockIK` ⇒ **Foot IK 全程關閉**。
4. 而 clip 的最後一格**不是完全站定的姿勢**：實測腳趾相對 root 是 0.048，`Idle` 站姿是 0.036，
   **高 1.2 cm**。這種姿勢差平常正是 Foot IK 在吸收的 ⇒ 關著就變成看得見的浮空。
5. 狀態結束、重力恢復，第一次往下 `Move` 才觸地 ⇒ `JustLanded` ⇒ **落地聲**，
   Foot IK 同時接手 ⇒ 那一下小小的沉下去。

**修法**：Exit 之後不再硬凍結成零位移，改為沿用 locomotion 既有的貼地力
（`MotionDriver.reboundForce = −2`）讓角色**真的坐到已驗證的落腳面上**。
不是宣稱自己觸地，是讓物理回答；Foot IK 因此在**還在 traversal 內**就接手。

### 13.3 ⚠️ 事故與修正：推導用的 rig 不是隨便哪一個

重烘焙時我先用了 `Y Bot`，結果把 `Bake_Vault1m` 寫壞——接觸窗從 `0.207–0.253` 跳到 `0.356–0.414`，
`reach` 變成 **−0.430**（手在 root **後方**）。查下去發現兩件事：

**① `X Bot` 與 `Y Bot` 的手不一樣。** 同一支 clip、同一格：

| | 小指指尖 Y | 小指指尖 Z |
|---|---|---|
| `X Bot` | 1.9276 | 0.4785 |
| `Y Bot` | 1.9650 | 0.5088 |

**差 3.7 cm 高、3 cm 前**（不同 Avatar）。**場景實際使用的是 `Y Bot`**
⇒ grip edge 必須在 `Y Bot` 上推導，否則遊戲裡一開始就偏 3.7 cm。

**② 接觸偵測對 rig 過度敏感。** `DetectPlant` 原本只取「搜尋窗內最長的低速段」，
而低速不等於接觸——`Y Bot` 上被選中的那段，手在身體**後方** 0.43 m，那是翻過去之後手往後擺的減速。

修法：加一條**物理合理性過濾**——*扶在前方障礙物上的手不可能在身體後面*
（`ConsiderPlantRun` 只收 `hand.z − root.z > 0` 的低速段）。這條與 rig 無關、不需要調任何門檻。
加上之後**兩個 rig 對兩支 clip 的結果完全一致**（`Vault1m` 皆為 `0.207–0.253`）。

**資產狀態**：`Bake_Climb2m` 與 `Bake_Vault1m` 已用 **`Y Bot`（執行期 rig）** 重新推導並寫入。
marker 與修正前相同（`Climb2m` 0.065／0.269／0.833；`Vault1m` 0.207／0.253／0.460），
只有 grip edge 的 Y 改變。⚠️ 這兩個 `.asset` **未進版控**（`git status` 顯示 `??`），
所以**沒有 git 還原路徑**——要還原只能重跑推導。這也是為什麼上面那條合理性過濾必須加：
它讓「重跑推導」變成一個可重現的操作，而不是碰運氣。

### 13.4 驗證

live Editor recompile 0 errors；**EditMode 442 total／440 passed／1 failed／1 existing skipped**、
**PlayMode 41／41**。唯一紅燈仍是 `M6_NarrowLedge_ShrinksAndClampsHandSeparation`（屬工作包二）。
新增 `B14`。
⚠️ 兩項都**尚未 Play 驗收**——尤其 §13.2 的貼地修正，只有在 Play 裡才看得出
「Foot IK 有沒有在 traversal 內就接手」。

---

## 14. Phase 3 —— Hand IK 真的接上去了（2026-09-13）

> 觸發：使用者截圖，**攀爬途中**手掌壓進箱子側面。那不是接觸那一格的問題——
> 我們只在接觸的**單一格**把身體對齊，其餘每一格的手完全跟著動畫走，**沒有任何東西把它釘在牆上**。
> 這正是 §3 的研究結論：warp 解 body alignment，**IK 解 contact residual**。五個參考實作都有這一層，我們沒有。

### 14.1 ⚠️ 先更正一個我自己造成的錯誤：玩家不是 `Y Bot`

場景裡有**兩個**角色：

| | 元件 | layer | 結論 |
|---|---|---|---|
| **`X Bot`** | `PlayerInputSource` ＋ `TraversalProbe` | Player | **這是玩家** |
| `Y Bot` | `AIInputSource` ＋ `AIMovementSource` | Enemy | 敵人 |

我在 §13 用 `FindAnyObjectByType<CharacterController>()` 拿到 `Y Bot` 就當成玩家，**沒有驗證**，
於是把兩支 bake 都重推到敵人的 rig 上。已全部改回 `X Bot` 重推。

⛔ **更正 §13.3 的錯誤結論**：原本出貨的 grip edge **本來就是在 `X Bot` 上烘的，是對的**；
是我改壞又改回來。`X Bot`／`Y Bot` 指尖差 3.7 cm 這個測量本身成立，
但「出貨資料用錯 rig」這個推論是錯的。
（`TraversalIntegrationWiringTests` 早就寫著 `PlayerPrefabPath = "Assets/Prefabs/X Bot.prefab"`——
**repo 裡已經有答案，我沒去看。**）

### 14.2 三個讓 Hand IK 完全是死路徑的原因

1. **元件掛不上去**：`TraversalHandIKController`／`TraversalHandIKRig` 是
   `FootIKController.cs`／`FootIKRig.cs` 裡的**次要類別**。Unity 要求 MonoBehaviour 的類名等於檔名，
   所以這兩個元件**無法 AddComponent**。⇒ 已各自拆成同名檔案。
2. **bake 沒有授權**：`handIKEnabled = 0`、三個窗全是 0 ⇒ `HasValidHandIK` 恆 false
   ⇒ controller 停在 `NotConfigured`。⇒ 已由 `TraversalContactDerivation` 自動寫入。
3. **prefab 沒掛**：⇒ 已掛在 `X Bot`（controller 在 root、rig 在 `Model`），`motionDriver` 已接。

### 14.3 IK 窗由測出來的 plant 換算，不是人填

```
full    = plant 開始（手落下那一格就是滿權重，不會在接觸瞬間彈一下）
start   = full − HandIKFadeSeconds/duration     （淡入是混合時長，用秒才跨 clip 一致）
release = plant 結束（動畫說手離開的那一刻）
```

寫入結果：`Climb2m` L=[0.037, 0.065, 0.269] R=[0.037, 0.065, 0.259]；`Vault1m` L=[0.172, 0.207, 0.253]。

**`rotationWeight = 0`——只釘位置，不轉手腕。** 與參考實作一致
（Dynamic Parkour System 的 `MatchTargetWeightMask(Vector3.one, 0)` 旋轉權重就是 0）：
把手腕硬轉到 ledge frame 會扭曲前臂，而抓握的朝向動畫本來就做對了。

### 14.4 順手修掉的設計缺陷：淡出終點被綁在 Exit marker

`TraversalHandIKWindow.Evaluate` 原本從 `release` 一路淡出到 **Exit marker**。
對 `Climb2m` 那是災難：手在 `transfer = 0.269` 就放開，`exit` 卻在 `0.833`
⇒ 淡出長達整支 clip 的 **56%（約 2 秒）**，手會被黏在邊緣上跟著身體往上拖。
改成**淡出與淡入等長**（對稱，不引入新的 authored 數字），再用 Exit 夾住上限——
「IK 不得存活到 Recovery」這個保證原樣成立。

### 14.5 實測：IK 到底有沒有事做、會不會放棄

真實 2 m 箱、理想站位（0.462 m）、玩家 rig `X Bot`，逐格量「動畫的手」與「固定世界目標」的距離：

| n | IK 權重 | 左手殘差 | 右手殘差 |
|---|---|---|---|
| 0.042 | 0.074 | 0.072 | 0.085 |
| 0.083 | **1.000** | 0.019 | 0.023 |
| 0.167 | **1.000** | 0.069 | 0.062 |
| 0.250 | **1.000** | 0.070 | 0.079 |
| 0.292 | 0.074 | 0.148 | 0.166 |
| 0.333+ | 0 | 0.171 → 1.1 | （已放手，正常） |

- 權重曲線與抓握窗完全吻合。
- **抓握期間手會自己漂移 1.9 – 7.0 cm** ——這就是以前沒人管、手因此鑽進牆裡的量。
- IK 生效期間**最大殘差 0.166 m < 放棄門檻 0.35 m** ⇒ **IK 會接手**，不會罷工。
- 放手之後殘差長到 1.1 m 是對的：手已經離開，權重是 0。

### 14.6 驗證

live Editor recompile 0 errors；**EditMode 443 total／441 passed／1 failed／1 existing skipped**、
**PlayMode 41／41**。唯一紅燈仍是 `M6`（工作包二）。

新增 `PlayerPrefab_TraversalHandIK_IsAttachedWiredAndAuthored`——
守住「元件有掛 ＋ `motionDriver` 有接 ＋ bake 真的啟用了窗」。
**這條的存在理由就是 §14.2**：有程式、有測試、但遊戲裡從來沒跑過。
`A49` 同步改讀新檔，並把「必須是自己檔案的主類別」一起釘住（掛不上去的元件等於不存在）。

⚠️ **尚未 Play 驗收。** IK 是否好看、手臂會不會扭，只有 Play 看得出來。

### 14.7 資產改動一覽（都走 Editor API，未手改 YAML）

| 資產 | 改了什麼 | 怎麼還原 |
|---|---|---|
| `Bake_Climb2m` / `Bake_Vault1m` | grip edge Y（指尖基準）＋ Hand IK 窗 | 重跑 `TraversalContactDerivation.Apply(bake, XBotPrefab, out report)` |
| `Assets/Prefabs/X Bot.prefab` | 新增 `TraversalHandIKController`（root）與 `TraversalHandIKRig`（Model） | 移除這兩個元件 |
| `Assets/Scenes/SampleScene.unity` | `X Bot` 的 `WeaponSocket.weaponPrefab` 設為 null（拿掉擋視野的劍） | 把 `Sword` prefab 指回該欄位 |

---

## 15. Climb2m 撐起階段：固定 Wrist IK 活得比支撐語意久（2026-09-13）

> Play 回饋：整體與結尾貼地已可接受，但**「手把身體撐起來的部分有問題」**。
> 本輪只處理 Climb2m 的這一段；已驗收的 root mapping、接觸點、結尾貼地、
> Vault／Climb1m、FBX 與 Animancer 都不動。

### 15.1 先量真實場景，不用猜

沿用 §14 的離線方法，但這次把 `X Bot` 的肩、肘、腕與四指末端一起逐格採樣：

1. 在旋轉後的 `Box_350x250x200_Prefab` 外側先向下射線找 `Ground y=0`。
2. 依箱體局部軸選近面；從理想站位射線的**第一個 hit** 就是箱體近面，
   距離 **0.4616 m**（不是遠面）。
3. 臨時 `CharacterController + TraversalProbe` 複製場景 `X Bot` 的 settings，
   得到合法 `Climb2m` candidate；再串 Entry → Animation Fit → Plan。
4. preview scene 內以 `X Bot` 與 `animator.applyRootMotion=true` 逐格取樣。

排除結果：

- contact→transfer 的 root correction 漂移 **0.000 cm**，§16 的 freeze 正常。
- root 相對地面由 n=0.0648 的 **0.076 m** 升到 n=0.2685 的 **1.767 m**；
  上升量 1.691 m 與 bake anchor 一致。
- 動畫 authored 腕部在這段本來會移動：L **11.52 cm**、R **11.03 cm**。
  「plant 低速」不等於「腕部該固定在單一世界點直到低速窗結束」。

真正的失真是固定 wrist target 開始反過來**拉長**肩—腕鏈：

| n | 舊 IK 權重 L/R | 左肘 authored→固定目標 | 左 reach ratio | 右肘 authored→固定目標 |
|---:|---:|---:|---:|---:|
| 0.2315 | 1 / 1 | 121°→120° | 0.867 | 116°→118° |
| 0.2407 | 1 / 1 | 123°→132° | 0.914 | 114°→126° |
| 0.2593 | 1 / 1 | **130°→180°** | **1.030** | **113°→153°** |
| 0.2685 | 1 / 0.74 | **134°→180°** | **1.086** | 111°→148° |

`reach ratio >= 1` 代表 wrist goal 已到／超過直臂可達距離。現有 residual 放棄門檻
0.35 m 抓不到這個問題：腕部殘差只有約 7–11 cm，距離門檻很安全，
但肩肘已經被固定點拉直。這也說明不能靠再放寬／縮小全域 residual 門檻解。

### 15.2 修法：固定目標開始延長手臂之前完成淡出

`TraversalContactDerivation` 現在額外量一個純幾何事件：

> 同一格的 `shoulder → fixed contact wrist` 距離，第一次**連續三格**大於
> `shoulder → authored wrist` 距離。

它不引入公尺或角度 tuning。三格沿用既有 `MinimumPlantFrames`，只排除單格取樣雜訊。
也只套在 `BothHands`：雙手 target ＋ frozen root 同時形成三個硬約束，正是本輪證據；
已 Play 通過的單手 Vault window 不改。

既有 0.1 s fade 的語意維持，只把 fade **往前搬**，讓權重在上述事件發生時歸零。
Contact／Transfer marker 不變，root freeze 不變。

`Bake_Climb2m`（X Bot）重推結果：

| | 舊窗 `[start, full, release]` | 新窗 `[start, full, release]` | 歸零時刻 |
|---|---|---|---:|
| L | `[0.037037, 0.064815, 0.268519]` | **`[0.037037, 0.064815, 0.212963]`** | 0.240741 |
| R | `[0.037037, 0.064815, 0.259259]` | **`[0.037037, 0.064815, 0.203704]`** | 0.231481 |

重跑同一真實場景後，n=0.2407 起兩手 IK 都是 0；原本 n=0.2593 的
左肘 130°→180°／reach 1.030，現在維持 authored 130°／0.906。

**Trade-off**：後半段不再把腕部釘死在單一世界點；這是刻意的，因為那時動畫已開始
滾掌／推起，固定點才是錯誤約束。若未來需要沿頂面滑動的動態 contact target，
那是更大的 runtime contact-mapping 語意；本輪沒有用它擴張範圍，也沒有改 AnimationClip。

### 15.3 自動驗證與資產還原

- 新增 `B15_Climb2m_HandIK_IsZeroBeforeFixedWristGoalExtendsTheArms`：
  用 production `Bake_Climb2m` ＋ `X Bot` 重新量肩—腕幾何，守住：
  ① extension event 真實存在；②新窗在事件前歸零；③舊 plant-end 窗當時仍會滿權重。
- EditMode：**444 total／442 passed／1 failed／1 skipped**。唯一失敗仍是既有 `M6`。
- PlayMode：**41／41 passed**。

資產改動只有 `Assets/ScriptableObjects/Motion/Bake_Climb2m.asset` 的兩個 IK release。
要還原舊行為，必須經 Unity Editor API 把 block `WithHandIK(...)` 設回：

```text
L = [0.037037, 0.064815, 0.268519]
R = [0.037037, 0.064815, 0.259259]
rotationWeight = 0
```

不可手改 YAML。現在直接重跑
`TraversalContactDerivation.Apply(bake, XBotPrefab, out report)` 會重現**新值**，不是舊值。

數值／推導不是「只有 Unity 匯入成功、沒有測試保護」——B15 會讀正式資產守住它；
但**畫面是否真的更自然**沒有自動化視覺測試保護，仍只有 Unity 匯入／數值量測成功，
必須由人類 Play 驗收。

---

## 16. 工作包二落地：Primary Contact Root ＋ Secondary Surface Residual（2026-09-14）

### 16.1 先量 A：不再調 IK window

沿用 §15 的 `X Bot` preview rig／production `Bake_Climb2m`，以 30 FPS 逐格比較「舊 plant-end 窗」與
目前 kinematic release。goal 位移是 animated wrist 與 fixed wrist 依實際 smoothstep weight 混合；
角速度比較的是 shoulder→goal 方向，目的只在量 release 是否新增不連續，不冒充 Play 視覺評分。

| 指標 | 舊窗 L / R | kinematic release L / R |
|---|---:|---:|
| IK 生效期間最大 reach ratio | **1.1019** / 0.9584 | **0.8706** / **0.8849** |
| reach ratio > 1 的格數 | **3** / 0 | **0** / **0** |
| release 段相對 authored 的最大 goal step 增量 | 0 / 0 mm | **10.2 / 3.7 mm** 每格 |
| release 段相對 authored 的方向角速度增量 | 65.6 / 70.9°/s | 63.9 / 69.5°/s |
| authored 動畫本身角速度峰值 | — | 403.6 / 436.0°/s |

⚠️ 表中的 1.1019 是同一套 preview rig 上用 goal-envelope 重建的**離線 proxy**，用途是「改窗前後用同一套方法比較」，
**不取代** §15 full-chain 量到的實際峰值 1.086。兩套量法結論一致：舊窗左臂超伸，現窗已無 `reach ratio > 1` 的格。

結論：§15 已把 reach>1 與直臂失真完全消掉；現有 envelope 本來就是 smoothstep，再挪 release
只會交換「固定點殘差」與「回到 authored pose 的混合位移」。剩餘量不是一個更好的窗門檻可以消除，
因此遵照工作包邊界，不再調資產或 IK 上限，直接修 root/contact 結構。

### 16.2 Root 只由單一 primary contact 決定

舊 `TraversalPlanBuilder` 對左右手各 `SolveSingle`，再平均兩份 correction。新契約：

- `BothHands` 固定 Left 為 primary；單手動作使用唯一 contact。
- primary grip edge 解唯一 root，wall-support clamp 後產生唯一 correction；Left／Right contact knot
  與 Transfer 都沿用它，§16 的 correction freeze 不變。
- 已無 `SolveDual` API；`A47` 明確防止它回來。

production Climb2m、理想進場 0.46155 m 的同資料比較：

| | root correction | primary L residual | secondary R residual |
|---|---|---:|---:|
| 修正前：雙手平均 | `(0, 0.075772, 0)` | 9.64 mm | 9.64 mm |
| 修正後：Left primary | `(0, 0.085415, 0.000171)` | **0 mm** | **19.29 mm** |

這不是宣稱 secondary 更準；是把決策誠實拆開：root 精確服務 primary，secondary 的公分級 residual
才交給 Hand IK，而不是偷偷反向改 root。contact→transfer correction 差仍為 **0 mm**。

### 16.3 Secondary 查真實 surface snapshot；窄 ledge 不偽造姿勢

secondary animated grip 在 primary root 已決定後，查詢 Probe commit 的
`TraversalLedgeFrame { EdgeOrigin, Tangent, WallNormal, TopNormal, TangentMin/Max }`：

- 落在含 safety margin 的 verified interval 內 ⇒ 投影到該 committed edge surface，
  再加 bake-time wrist↔grip offset 形成 Hand IK target。
- 落在 interval 外 ⇒ contact unavailable、該手 IK 不啟用；保留動畫自己的手距。

因此沒有新增 runtime Physics query：Probe 仍是唯一 Physics owner（`A39`），Plan 是 pure data transform
（`A47`），Hand IK 只讀 committed target（`A49`）。這也關閉原本唯一紅燈：
`M6_NarrowLedge_KeepsPrimaryAndRejectsUnsupportedSecondaryContact`。

`FingerContactClearance`、grip edge 與 wrist↔grip offset 的歸屬不變：它們明確是
**animation/contact fitting**，不是 Probe geometry。

### 16.4 驗證與資產狀態

- focused `TraversalWarpPlanTests`：24／24（新增 `HC6`，更新 `HC4／M6`）。
- 完整 EditMode：**445 total／444 passed／0 failed／1 existing skipped**；PlayMode：**41／41**。
- 本工作包**沒有修改任何 `.asset／.prefab／.unity／.meta`**；`Bake_Climb2m` 的 IK window 仍是
  §15 的 L `[0.037037,0.064815,0.212963]`、R `[0.037037,0.064815,0.203704]`。

⚠️ Climb2m 撐起自然度仍只有 Unity 匯入／離線數值量測，沒有自動化視覺測試保護；要由人類 Play 驗收。

---

## 附：來源

- Traverser — <https://github.com/AitorSimona/Traverser>
- Dynamic Parkour System — <https://github.com/knela96/Dynamic-Parkour-System>
- KINEMATION Motion Warping 文件 — <https://kinemation.gitbook.io/motion-warping-for-unity>（demo repo <https://github.com/kinemation/motion-warping>）
- Unreal Motion Warping — <https://dev.epicgames.com/documentation/en-us/unreal-engine/motion-warping-in-unreal-engine>
- Game Animation Sample — <https://dev.epicgames.com/documentation/en-us/unreal-engine/game-animation-sample-project-in-unreal-engine>
- ALS-Refactored — <https://github.com/Sixze/ALS-Refactored>

---

## 17. 錨點不是一個點，是一個面（2026-09-14）

> 使用者 Playtest 回饋（排序過的三項，本節做第 1 項）：
> 「角色有『伸手抓邊』的語意，可是看起來比較像動畫剛好把手伸到附近，**而不是手真的把角色固定在那個 ledge 上**。
> 尤其**雙手進入支撐到上身抬起時**，缺少那種『這裡是固定點，身體繞著它移動』的感覺。」

### 17.1 量到的破綻

真實 2 m 箱、理想進場 0.462 m、玩家 rig `X Bot`：

| n | IK 權重 L | reach ratio L | root 相對平台頂面 | 骨盆 |
|---|---|---|---|---|
| 0.20 | 1.000 | 0.799 | −0.913 | −0.733 |
| **0.24** | **0.002** | 0.917 | **−0.485** | −0.270 |
| 0.26 | 0 | **1.048** | −0.297 | −0.044 |
| 0.30 | 0 | 1.209 | −0.021 | +0.236 |

**手在 n=0.24 就完全放開，而身體距平台頂面還有 0.485 m。**「手撐起身體」的整個後半段沒有任何錨定。

放手的理由也看得見：放開之後 reach ratio 立刻衝過 1.0（最高 1.265）。
§15 當時選擇提前放手 —— 那是**用失去錨定換不超伸**。

### 17.2 語意修正：contact 是面約束，不是點約束

舊模型把手腕釘在**單一固定世界點**上。但真實的 mantle 是手掌從「四指扣邊」滾成「掌心壓頂面」：
**接觸點留在邊緣上，手腕會跟著身體移動。** 釘死一個點，身體一升起就必然超過手臂長度。

因此改為兩段式，兩者**依 reach ratio 連續混合**（不是開關，所以沒有放手的瞬間）：

| 階段 | 目標 |
|---|---|
| 手臂構得到固定錨點時 | 錨定該點（身體繞著它動＝玩家要的「固定點」感） |
| 快構不到時（reach 0.90→1.00） | 平滑退化成「動畫自己的手腕 ＋ 只把離開合法面的量拉回」 |

新增 `TraversalContactSurface`（`Presentation.Motion`，純資料）把「不得穿進頂面」「不得滑出已驗證的邊緣區間」
變成**執行期每一格都成立的保證**，同時**允許手掌沿頂面往內滾**——那是正確動作，不該被當成誤差修掉。
表現層因此不需要認識 `Project.Core.Environment`，也不做任何 Physics 查詢（`A39`／`A49` 不變）。

### 17.3 窗還原成「動畫量到的抓握結束」

超伸既然改由執行期處理，`TraversalContactDerivation` 的提前放手（§15 的 `releaseOverride`）就不再需要。
release 回到各手自己測出來的 plant 結束點：

| | 修正前 | 修正後 |
|---|---|---|
| `Bake_Climb2m` L | `[0.0370, 0.0648, 0.2130]` | `[0.0370, 0.0648, **0.2685**]` |
| `Bake_Climb2m` R | `[0.0370, 0.0648, 0.2037]` | `[0.0370, 0.0648, **0.2593**]` |
| `Bake_Vault1m` | 不變（單手，本來就沒有 override） | 不變 |

⚠️ **這一步才讓 §16 的 `RootReachConstraint` 真正開始工作**——它是「讓身體配合手臂可及範圍」的 root 修正項，
但手在 0.213 就放開時它無事可做。窗延長之後它才有作用區間。
（這也是為什麼 `B11` 會紅：它原本斷言「抓握期間 warp 完全不推身體」，而現在**身體本來就該動**。）

### 17.4 結果

| | 修正前 | 修正後 |
|---|---|---|
| IK 權重歸零時 root 距平台頂面 | −0.485 m | **−0.156 m** |
| 歸零時骨盆 | 平台下方 0.270 m | **已越過平台 +0.002 m** |
| 錨定期間最大 reach ratio | **1.048**（手臂被拉直） | **0.957** |

也就是：**錨定現在一路撐到骨盆越過平台為止，而且全程留有手肘餘量。**

### 17.5 使用者第 2 項的量測結論：root 收斂本來就是平滑的

逐格一階差分（每 0.01n）顯示：`drootY` 從 −0.08 平滑到峰值 −0.34（n≈0.45）再收到 0，
**n≈0.81 時 `rootDy` 已經是 +0.000**，之後完全靜止。**沒有 snap，也沒有突變點。**

⇒ 使用者感覺到的「被接管」**不是 root warp 造成的**。最可能的來源是
**Foot IK 的啟用是離散事件**：`isGrounded` 要等 exit knot（0.8333）之後的貼地力才變 true，
Foot IK 那時才突然接手 clip 尾段那 1.1 cm 的姿勢差。要修的是**接手時機與斜坡**，不是 warp。
**本節未動它**，留給下一輪。

### 17.6 已知殘留

- 錨定退場時 `drootY` 在 n≈0.38 有一個約 **0.5 cm/step** 的小凸起（修正前是平滑的 −0.08）。
  那是 reach 約束淡出的導數不連續。量級很小（≈14 cm/s，持續一格），但**是本節新引入的**，記在這裡。
- 使用者排序的第 3 項（**攀爬入口手感**）完全未動——那是設計題，需要先定運動邏輯再實作。

### 17.7 驗證

live Editor recompile 0 errors；**EditMode 447 total／446 passed／0 failed／1 existing skipped**、
**PlayMode 41／41**。
新增 `SURF1`／`SURF2`（接觸面只補違規量、退化資料是 no-op）；
`B15` 改為守「窗必須覆蓋整個測出來的抓握期」；
`B11` 改為守「抓握期間**除了宣告過的 reach 約束之外**不得有第二個東西推身體」（更嚴格，也更誠實）。

**資產改動**：`Bake_Climb2m` 的 IK 窗（上表）。還原方式：把
`TraversalContactDerivation` 的 `sharedRelease` 改回 `measurement.TransferNormalizedTime` 再重跑
`Apply(bake, XBotPrefab, out report)`。⚠️ 只有 Unity 匯入與離線量測保護，畫面好不好看沒有自動化測試。

---

## 18. 錨定的是「接觸點」，不是「手腕」（2026-09-14）

> §17 之後使用者回報：**「仍不像固定點。」**
> §17 把錨定從 n=0.24 延長到骨盆越過平台、reach 也壓在 0.957 以內——數字都對，畫面還是不對。
> 那通常代表**量錯了東西**。

### 18.1 量到的主因：釘錯目標

`AvatarIKGoal.LeftHand` 的 goal 是**手腕**，而我們 commit 的也是一個固定的**手腕**世界點。
但抓握期間手掌會從「四指扣邊」轉成「掌心壓頂面」——`Climb2m` 實測手骨轉了 **61.4°**，
而手腕到接觸點約 **14.5 cm**：

| n | 手骨相對接觸格的旋轉 | 手腕釘死時接觸點掃出 |
|---|---|---|
| 0.17 | 6.0° | 1.5 cm |
| 0.22 | 22.5° | 5.6 cm |
| 0.25 | 53.8° | 13.0 cm |
| 0.269 | **61.4°** | **15.2 cm** |

**即使手腕完美不動，指尖仍會在邊緣上掃過 15 cm。** 那是 §14–§17 一路在修的漂移量（1.9–7 cm）的 2–7 倍，
也是「看起來不像固定點」的主因。**先前每一輪都在把手腕釘得更準，但釘的本來就不是該固定的東西。**

### 18.2 修法

把 commit 的「手腕→接觸點」偏移換算到**手骨自己的座標系**（抓住那一刻 latch 一次手部朝向），
再用**當下的手部旋轉**還原：

```
offsetInHand   = Inverse(latchRotation) * wristToContact       // 抓住時算一次
wristTarget(n) = anchoredContact − handRotation(n) * offsetInHand
```

⇒ 手掌翻轉時**手腕跟著讓位，接觸點留在原地**。

同時把 §17 的退化起點從 reach 0.90 移到 **0.98**：`RootReachConstraint` 已經把肩—腕鏈壓在可及範圍內，
從 0.90 就開始退化等於在**最需要錨定的撐起段**把權重砍掉一半。

### 18.3 結果：更穩，而且更輕鬆

| | 舊（釘手腕） | 新（釘接觸點） |
|---|---|---|
| IK 生效期間 reach 峰值 | 0.950 | **0.874** |
| 接觸點最大偏移 | **15.2 cm** | **0（由建構保證）** |
| 抓握窗內的錨定權重 | 受 reach 影響而衰減 | **全程 1.0** |

⭐ 反直覺但合理：**釘接觸點比釘手腕更省手臂**——手掌翻轉時手腕會自然往肩膀側讓位。
先前為了「不超伸」而提前放手（§15）、以及為此付出的失去錨定，其實是釘錯目標造成的假兩難。

### 18.4 ⚠️ 仍未驗證的前提

**從來沒有人確認過執行期 `TraversalHandIKController.Status` 真的等於 `Active`。**
所有量測都是離線重建 plan 的結果。而這個子系統的歷史正是「有程式、有測試、但遊戲裡從來沒跑過」
（§14.2 一次列出三個死路徑）。

Play 時請開 `Window ▸ Project ▸ Traversal Debug` 確認 Hand IK 那一行顯示 `Active`。
若顯示 `NotBound`／`AwaitingPose`／`RootMappingUnavailable`／`OutsideContactWindow`／`ResidualTooLarge`，
**那才是真正的問題，本節的所有數字都還沒機會生效。**

### 18.5 驗證

live Editor recompile 0 errors；**EditMode 447 total／446 passed／0 failed／1 existing skipped**、
**PlayMode 41／41**。本節**未改任何資產**（只改執行期的目標解析）。

---

## 19. Regression Diagnosis（2026-09-14；只讀不改，未提出修法）

> 觸發：使用者「**先暫停繼續修改攀爬系統**。主觀觀察是最近幾輪為了改善 ledge 對齊、hand IK、
> warp／correction 與 mantle exit 之後，整體效果可能反而比之前更差。這一輪先做 regression diagnosis。」
>
> **本節不新增 special case、不新增 offset、不新增 fallback、不調參數、不重寫 traversal。**
> 所有結論都來自現有 git history、磁碟上的程式與序列化資產、以及 §13–§18 自己記下的量測。

### 19.1 🔴 先講最重要的一件事：**這個子系統沒有任何 git 還原點**

```
git log --oneline -40        → 最新 commit 是 71f5a5c，內容與 traversal 無關
git ls-tree -r <每一個分支>  → 四個分支（main / integrate-adr005 / wp1-camera-aim /
                               claude/silly-lamarr-33d1df）沒有任何一個含
                               traversal / climb / vault 檔案
git stash list               → 空
```

**整個 traversal 子系統——18 個 `.cs`、9 個測試檔、3 支 bake `.asset`、
`TraversalStateParams.asset`、`X Bot.prefab` 的接線——全部是 `??` untracked 或未 commit 的 `M`。**

後果，直接回答使用者問的「哪一次修改開始退化」與「回退哪些修改」：

| 問題 | 誠實的答案 |
|---|---|
| 哪個 commit 開始退化 | **不存在這樣的 commit。** §13→§18 六輪改動全部發生在同一份未 commit 的工作樹上，彼此覆蓋 |
| 可以 revert 到哪一版 | **一個都不行。** 沒有 `git revert` ／ `git checkout <sha> -- <file>` 的對象 |
| §16 的「已退版」是什麼 | 是**照著 §16 的文字把程式重新寫回去**，不是還原。WORKLOG 自己也註明「寫法與當時不同」 |
| bake `.asset` 怎麼還原 | **只能重跑 `TraversalContactDerivation.Apply`**，而它現在會產出**最新**推導結果，不是舊值（§15.3 已記） |

⚠️ **這就是「效果好像越改越差，卻說不出是哪一次」的結構性原因**：六輪改動之間沒有任何一個可觀測、
可比較、可回頭的狀態。每一輪都只能與「記憶中的上一輪」比較，而記憶不是證據。

> **這一點優先於本節其他所有結論。** 在建立還原點之前，下一輪不論做什麼，都會重現同一個處境。
> （Git 由使用者執行——本節只陳述事實，不代為操作。）

---

### 19.2 Current pipeline —— 現在**實際**的執行順序

以下是逐幀真正跑的路徑（`CharacterPipelineRunner.cs:348–418`），不是設計意圖：

```
Update()
 ├ 1   PlayerInputSource
 ├ 2   PlayerCombatContextSource
 ├ 2.5 TraversalProbe.Tick              ← 唯一的 Physics owner，逐幀感測
 ├ 3   MovementModel.Tick
 ├ 4   FullBodyStateMachine.Tick        → TraversalState.CanEnter ／ OnTick
 ├ 4.5 ArbiterPipeline
 └ 5   CharacterFacingSource

LateUpdate()
 ├ 6  動畫參數同步
 ├ 7  CurrentState.OnUpdateMotion(motionDriver, facade, data)
 │    └ TraversalState.OnUpdateMotion（States/TraversalState.cs:225）
 │       ├ a. CommitWarpPlan —— **整段 traversal 只跑一次**
 │       │     ① TraversalPlanBuilder.TryBuild（piecewise，六個 knot）
 │       │     ② 失敗才退 MotionDriver.TryCreateTraversalWarpPlan（endpoint 單段 blend）
 │       │     ③ BeginTraversalPresentation ＋ BeginTraversalCollisionProfile
 │       ├ b. n ← animationFacade.GetNormalizedTime()（動畫是時間權威）
 │       ├ c. ApplyTraversalCollisionProfile(n)  ← 三個 binding 的 profile **全部關閉**，無作用
 │       └ d. ExecuteCommittedMotion → MotionDriver.ExecuteTraversalWarpedMovement
 │             （MotionDriver.cs:480）
 └ 8  PresentationPipeline.Tick
      ├ FootIKController.Tick           gate = data.IsGrounded && !BlockIK（二值，無淡入）
      └ TraversalHandIKController.Tick  讀 committed plan 的**固定世界目標**

Animator 評估 → OnAnimatorIK
 ├ FootIKRig 套用
 └ TraversalHandIKRig 套用（TraversalHandIKRig.cs:29）
```

#### 19.2.1 一幀之內，root 位置被幾層東西決定

`ExecuteTraversalWarpedMovement` 內的實際疊加順序（`TraversalWarpPlan.cs:692–737`）：

| # | 層 | 作用區間（Climb2m） | 現況 |
|---|---|---|---|
| 1 | **baked 原始曲線** `TryEvaluateOriginal` | 全程 | 以動畫進度直接取樣，**無時間重映**（§R1 的修正仍成立） |
| 2 | **knot correction**（smoothstep 內插） | 見 19.2.2 | 生效 |
| 3 | **RootReachConstraint**（root-local 加法） | `[0.213, 0.380]` | ⚠️ **曲線 109 個取樣點全為 0 —— 這一層目前完全沒有作用** |
| 4 | **Exit 硬接管**：`n ≥ 0.8333 ⇒ position = TargetPosition` | `[0.833, 1.0]` | 生效；動畫尾段 root motion 被整段取消 |
| 5 | **reboundForce 貼地**：`Δy += −2 × dt` | `[0.833, 1.0]` | 生效（§13.2 加入） |
| 6 | `CharacterController.Move` 的碰撞裁切 | 全程 | 生效；被擋的量**不回授** plan |

**第 3 層值得單獨講**：`Bake_Climb2m.rootReachConstraint` 是 `enabled: 1`、`maximumReachRatio: 0.95`、
`IsValid == true`，而 `localX/Y/Z` 三條曲線各 109 格**全部是 0.000000**。
這不是壞掉——是 §15 把 IK 窗提前之後 reach 峰值降到 0.87，這條約束**沒有東西要修**，
`TryBuildRootReachConstraint` 仍照常寫出一條全零曲線（`TraversalContactDerivation.cs:891`，無 early-out）。

⇒ **`docs/24` §18 的前提「`RootReachConstraint` 已壓住伸展，從 reach 0.90 開始退化等於砍掉一半權重」
對現在磁碟上的資產不成立。** §18 已被退版，但這個前提如果被下一輪沿用，會再推出一次錯的結論。

#### 19.2.2 Climb2m 的 correction 時間分配（本節最關鍵的數字）

`Bake_Climb2m`：`BakedDuration = 3.6 s`，30 fps ⇒ 108 格。

| knot | n | 秒 | 格 | correction |
|---|---:|---:|---:|---|
| Entry | 0.0000 | 0.00 | 0 | **0**（target ＝ SensedRootPosition） |
| LeftContact | 0.06481 | 0.233 | 7 | **完整的水平＋垂直 correction** |
| RightContact | 0.06481 | 0.233 | 7 | 同上（與左手**同格**，span = 0） |
| Transfer | 0.26852 | 0.967 | 29 | 沿用 RightContact ⇒ **抓握期間凍結**（§16 的 freeze） |
| Exit | 0.83333 | 3.000 | 90 | 指向 exitTarget |
| Recovery | 0.87037 | 3.133 | 94 | 同 Exit |

**⇒ 從站定到雙手上緣，只有 7 格（0.233 s）可以吃掉全部的入場誤差。**

對照使用者影片的實測站位：

```
面板：2 Entry ACCEPT — AlreadyClose / wall 0.23 m (legal 0.06–0.81)
      4 Fit: approach animated 0.000 m → required −0.230 m
```

- 動畫**自己在接觸前完全不前進**（`approach animated 0.000 m`；Climb2m 第 7 格手就上緣了）
  ⇒ `TraversalAnimationFit` 的 playback rate **無法分擔任何一公分**（rate 只改時間，不改位移）。
- 0.232 m ÷ 0.233 s ＝ 平均 **1.0 m/s**，smoothstep 峰值 ×1.5 ＝ **1.49 m/s** 的純 warp 速度。
- 同一支動畫自己的 `AutoAverageSpeed` 是 **0.187 m/s**。
- **⇒ 起攀那 0.23 秒，角色被 warp 以自身動畫速度的 8 倍推向牆面。**

對照 **endpoint fallback** 的同一段：窗是 `[warpStart 0, warpEnd（bake 推導）≈ 0.45]`
（`States/TraversalState.cs:354` 的註解記著 Climb2m ≈ 0.45）＝ **1.62 s**。

> **同樣的 0.232 m，piecewise 用 0.233 s，endpoint 用 1.62 s —— 壓縮 7 倍。**

Vault1m 完全沒有這個問題：contact 在 `n = 0.2069 × 2.9 s = 0.60 s`（18 格），窗長 2.6 倍。
**這正好解釋為什麼 Vault「已 Play 通過」而 Climb2m 一直不對——問題不在 Climb2m 的資產，
在「piecewise 把 correction 綁死在 contact 那一格」這個結構，而 Climb2m 的 contact 特別早。**

---

### 19.3 修改編年史：每一項原本要解決什麼、實際動到哪個階段

沒有 commit 可以對照，改用**檔案 mtime ＋ §13–§18 自己的紀錄**重建。

| 輪 | 日期 | 原本要解決 | 實際動到的階段 |
|---|---|---|---|
| **§10 ／ §11** | 09-13 | 感測範圍由動畫需求推導；Animation Fitting v1（playback rate） | traversal entry、接近牆面 |
| **§13.1** | 09-13 | 手指插進牆面 | hand ／ ledge contact（grip edge 的 Y 基準） |
| **§13.2** | 09-13 | 結尾微小浮空 ＋ 落地聲 | **exit ／ stand alignment**（加入 reboundForce 貼地） |
| **§13.3** | 09-13 | 推導 rig 用錯（Y Bot） | 全部重推 → §14.1 又推翻，**淨改動為零**，但兩支 bake 被重寫過兩次 |
| **§14** | 09-13 | Hand IK 是死路徑（三個原因） | hand ／ ledge contact（IK 第一次真的啟用） |
| **§15** | 09-13 | 撐起段固定 wrist 把手臂拉直（reach 1.086） | **body ／ pelvis vertical path** ＋ hand contact（IK 窗提前 release） |
| **§16** | 09-14 | 雙手平均 root 是假精確 | **起跳 ＋ hand contact**（root 改為只服務 primary ＝ 左手） |
| **§17** | 09-14 | 「不像固定點」 | hand contact（錨點改成面約束） |
| **§18** | 09-14 | 仍「不像固定點」 | hand contact（錨接觸點而非手腕） |
| **退版** | 09-14 07:02 | 使用者：「這修的比我提出前面那段反饋時還爛」 | §17／§18 全部移除，回到 §16 的**行為** |

**觀察 A：§14 之後的四輪（§15／§16／§17／§18）全部集中在同一個階段——hand contact。**
而使用者從頭到尾點名的兩個可見缺陷裡，**只有一個**在這個階段。

**觀察 B：每一輪的驗收都是「離線在理想站位 0.462 m 上重建 plan」。**
使用者實際玩到的站位是 **0.17–0.23 m**，而 entry 合法帶是 `[0.06, 0.81]`。
⇒ **四輪的量測都不在使用者玩的情境裡**（WORKLOG 已認定這是方法錯誤；本節補上 19.2.2 的機制：
站位差直接決定 entry 那 7 格要吃多少 correction，而這正是離線量測從沒掃過的維度）。

---

### 19.4 What improved —— 最近修改**真正**改善了什麼

這些有明確的機制或量測支撐，**建議全部保留**：

| 項目 | 證據 | 為什麼是真的改善 |
|---|---|---|
| **Root／Pose 時間同步**（§R1，piecewise 之前） | `TrajectoryNormalizedAt(n) ≡ n`，由 `TraversalBakedAssetTests` 釘住 | 修正前垂直去同步達 0.5–1.0 m。這是整條鏈路最大的單一修正，且有回歸測試保護 |
| **exit 終點由動畫自然落點決定**（`TryGetNaturalExitDepth`） | Climb2m 0.212 vs 探測 0.600 | 移除了 0.39 m「動畫裡沒有的位移」＝放手後的腳滑 |
| **grip edge 的 Y 以四指指尖為基準**（§13.1） | 四指由 −1.3~+0.9 cm → +1.0~+3.2 cm | 直接消掉使用者點名的「手指插進牆面」，有 `B14` 守住 |
| **IK 窗提前 release**（§15） | reach ratio 峰值 1.086 → 0.871，`reach>1` 的格數 3 → 0 | 消掉直臂失真。**這是唯一一次改動同時有 full-chain 量測與離線 proxy 兩套方法互相印證** |
| **Hand IK 從死路徑變成活路徑**（§14） | 三個死因逐一修掉，`PlayerPrefab_TraversalHandIK_IsAttachedWiredAndAuthored` 守住 | 抓握期間手本來會自漂 1.9–7.0 cm，現在有東西管它 |
| **診斷可見度**（Plan mode／rejection／Traversal Debug 視窗） | `DescribePlanMode` 三種結局都說得出口 | 「靜默退回 endpoint」不再看不見。本節能寫出來就是靠它 |

---

### 19.5 What regressed —— 哪些行為反而惡化

#### R1 ⭐ **traversal entry ／ 接近牆面：correction 窗被壓縮 7 倍**（最大嫌疑）

- **機制**：19.2.2。piecewise 把全部 correction 綁在 `LeftContact` 那一格；Climb2m 的 contact 在第 7 格。
- **哪一輪開始**：**不是 §15–§18，是更早的 piecewise 落地**（工作包一之後、§13 之前）。
  §13–§18 從來沒有量過這一段，所以它一路存活到現在。
- **為什麼與使用者的體感吻合**：「攀爬起手／貼牆入口」是使用者排序的第 3 項，但它發生在**最前面**——
  一次 traversal 的頭 0.23 秒就被 1.5 m/s 的推力破壞，後面所有的 contact 精度都是在已經歪掉的起點上做的。
- **為什麼 Vault 沒事**：Vault 的 contact 在第 18 格，窗長 2.6 倍。

#### R2 **exit ／ stand alignment：把「浮空＋落地聲」換成了「Foot IK 離散接管」**

§13.2 的修法（Exit 之後 `Δy += reboundForce × dt`）**確實**消掉了浮空與落地聲，但它做的事是
把 `isGrounded` 的翻轉時刻從「狀態結束後」搬到「traversal 內的 n = 0.8333」。
而 `FootIKController` 的閘門是**純二值**、沒有任何淡入（`FootIKController.cs:140`）：

```csharp
bool ikAllowed = data.IsGrounded && !data.Arbitration.BlockIK;
```

⇒ 在 n = 0.8333 的某一幀，Foot IK 從權重 0 直接跳到全權重，吸收掉那 1.2 cm 的姿勢差。
**這就是使用者第 2 項「翻越結尾 → 站立收斂被接管」的感覺**——§17.5 已量到「root 本身是平滑的」，
本節補上另一半：**不平滑的不是 root，是 Foot IK 的啟用。**

同一區間還疊著兩件事：

- Exit 之後 plan 位置**凍結**在 `TargetPosition`，動畫尾段（0.833 → 1.0 ＝ 0.6 s ／ 18 格）的
  root motion 被整段取消 ⇒ 若尾段有前踏，**會在原地滑步**。
- 同時 `reboundForce = −2 m/s` 持續往下壓，`_verticalVelocity` 被寫成 −2 並帶出狀態。

#### R3 **hand contact：§16 把誤差全部集中到 secondary（右手）**

§16 自己的表：

| | root correction | primary L residual | secondary R residual |
|---|---|---:|---:|
| §16 之前（雙手平均） | `(0, 0.0758, 0)` | 9.64 mm | 9.64 mm |
| §16 之後（Left primary） | `(0, 0.0854, 0.000171)` | **0 mm** | **19.29 mm** |

架構上這是對的（誠實拆開決策擁有權；`AvatarTarget` 一次只吃一個目標）。
**但視覺上，人眼看的是「有沒有一隻手沒抓到」，不是「平均誤差」。**
從「兩手各差 1 cm」變成「一手完美、一手差 2 cm」，在**可信度**這個指標上是退步——
使用者原話「**左手扣在頂面、右手攤在立面上**」描述的正是這個不對稱。

⚠️ 同時 root 被抬高了 **0.96 cm**（0.0758 → 0.0854），這個位移沒有任何一輪單獨驗收過。

#### R4 **§17 ／ §18 兩輪淨值為負，且已退版**

使用者裁決：「這修的比我提出前面那段反饋時還爛」。已回到 §16 行為。
**教訓值得留著**：§17／§18 都是在「數字變好」的驅動下做的（reach 0.950 → 0.874、
接觸點偏移 15.2 cm → 0），而使用者體感變差。§13 開頭自己寫的那句話在這裡再次應驗：
**改動的價值由「看不看得到」決定，不是由「數字有沒有變好」決定。**

---

### 19.6 Suspected conflict —— 哪些系統在互相競爭

#### C1 ⭐ **entry 合法帶 vs 動畫的零 approach**（結構性矛盾，不是參數問題）

| | 值 | 出處 |
|---|---|---|
| Climb2m 需要的站位 | **0.462 m** | bake 推導（`TryGetDesiredEntryDistance`） |
| entry 允許的站位 | **0.06 – 0.81 m** | `TraversalStateParams.entryPolicy`：`desired 0.45 ± (close 0.40 ／ far 0.35)` |
| 動畫自己能吸收的量 | **0.000 m** | `approach animated 0.000 m` |
| 誰吃掉差額 | **entry 的 7 格 warp** | 19.2.2 |

⇒ **系統承諾了一個 0.75 m 寬的入場帶，但動畫的吸收能力是 0，補償窗是 0.23 s。**
`maximumCloseError = 0.4` 這個值允許玩家在**比動畫需求近 0.4 m** 的地方起攀，而那 0.4 m
必須在 7 格內被硬推回去。**這兩個數字從來沒有被放在一起檢查過。**

#### C2 **三層東西同時決定 exit 區間的垂直位置**（19.2.1 的第 4／5／6 層）

`plan 凍結（絕對）` × `reboundForce（持續向下）` × `CharacterController 碰撞裁切（不回授）`
—— 三者在 `[0.833, 1.0]` 同時作用，而 plan 的「committed destination contract」
在加入 reboundForce 之後**已經不再成立**（每幀都被往下推）。文件仍寫著它成立。

#### C3 **Foot IK（二值）與 traversal（連續）在同一區間交接**

即 R2。Foot IK 沒有淡入機制（M3.2~M3.4 的 fade 族已被刻意移除——見 `FootIKController.cs:24`，
那是既有的架構決定），而 traversal 的 exit 是連續的。**離散 × 連續的交界必然可見。**

#### C4 **Hand IK 的固定世界目標 vs root 的持續 correction**

抓握期間 root correction 是凍結的（§16），所以那一段沒有競爭——**這是設計對的地方**。
但在 `[Entry, LeftContact]` 這 7 格內，root 正以 1.5 m/s 被推動，而 IK 窗
`start = 0.037` 就已經開始淡入（第 4 格）⇒ **IK 在 root 還在高速 warp 的時候就開始拉手**。
兩者不是同一個權威，卻作用在同一條肩—腕鏈上。

#### C5 **Collision profile 完全沒有作用**

三個 binding 的 `collisionProfile` 全部 `enableCenterShift: 0 ／ enableHeightAdjustment: 0 ／
enableRadiusAdjustment: 0`，曲線皆空。`BeginTraversalCollisionProfile` ／
`ApplyTraversalCollisionProfile` ／ `RestoreTraversalCollisionProfile` 三個方法**每幀在跑，效果為零**。
⇒ 使用者第 4 項「Capsule 穿模」目前**沒有任何機制在處理**（不是調壞了，是從沒啟用）。

#### C6 **`Bake_Climb1m` 沒有 Traversal block**

`Bake_Climb1m.asset` 整支沒有 `Traversal:` 區塊（mtime 09-12 19:00，三支 bake 裡唯一沒被重推的）。
⇒ 它永遠走 endpoint fallback、沒有 Hand IK。
**目前 `climb1mEnabled: 0`，所以不影響玩**——但它是一顆等著被打開的地雷。

---

### 19.7 ⚠️ 一個對不上的數字：影片上的 `R 19.0 cm`

WORKLOG 把影片面板的 `Execution: hand residual  L 0.0 cm   R 19.0 cm` 當成「右手沒抓到邊緣」的量化證據。
**本節把這個數字從程式推了一遍，推不出 19 cm。**

推導（`TraversalStateParamsSO.cs:303–345` ＋ `CreateContactMeasurement`）：

```
residual_R = | rightAnimatedGrip − queriedRight |
           = | R·(gripRight − gripLeft) − Tangent·(該向量在 Tangent 上的分量) |
```

`Bake_Climb2m` 的兩個 grip edge：

```
leftGripEdgeInRoot  = (−0.20638, 1.91758, 0.46138)
rightGripEdgeInRoot = ( 0.37259, 1.93687, 0.46172)
差                  = ( 0.57897, 0.01929, 0.00034)
```

場景裡的 `Box_350x250x200_Prefab` 也查過：四元數
`(w 0.6637226, x −0.6637226, y −0.2438697, z −0.2438697)` 展開後，
物件 local +Z 對到**世界正上方 (0,1,0)**，local +X 是水平的 `(0.762, 0, 0.647)`
⇒ **箱子只有 40.35° 的 yaw，頂面是水平的、邊緣是水平的**
（原本懷疑是傾斜造成投影洩漏，**查證後不成立**）。

在水平邊緣上，`Tangent` ⟂ 世界上方、`R` 的 local-x ∥ `Tangent`，於是 0.57897 的橫向分量**完全被投影吃掉**，
剩下的就是兩手 grip edge 的**高度差**：

> **residual_R ＝ 0.01929 m ＝ 19.3 mm** —— 與 §16 記的 19.29 mm **完全吻合**。

而且這個量**與站位無關**：Climb2m 的 `leftTime == rightTime == 0.06481`，
兩手共用同一個 `originalRoot`，站位差只進入 `primaryCorrection`，而它對兩手同時作用、相減時消掉。

**⇒ 對一個成形良好的 ledge，這段程式產不出 19 cm，只能產出 1.93 cm。**
（超出 verified interval 時 `TryQueryContactSurface` 是**直接 return false**、residual 記 0，
不是夾到 19 cm；`ClampOutsideWall` 若作用會讓 **L 也非 0**，而影片上 L ＝ 0.0 cm。）

所以 `R 19.0 cm` 只可能來自這三者之一：

1. **committed 的 `TraversalLedgeFrame` 不是我們以為的那個面**
   （probe 打到倒角／另一個箱子／`TangentMin/Max` 退化／`WallNormal` 非水平）——**感測問題，不是 IK 問題**；
2. 影片那一次跑的不是這支 Climb2m ／ 不是這個箱子；
3. 面板讀到的是上一次 traversal 的 `LastCommittedTraversalPlan`。

⚠️ **`TraversalDebugWindow` 目前完全不顯示 committed ledge frame**
（`Section` 只有 Decision chain／Plan／Execution，沒有 `EdgeOrigin`／`Tangent`／`TangentMin/Max`／
`TopNormal`／`WallNormal`）。**唯一能分辨這三種可能的證據，剛好是唯一沒被顯示的東西。**

> 這一點會改變下一輪的方向：如果 19 cm 是真的，**問題在 Probe committed 的那個面**，
> 而 §15–§18 四輪全部在改 IK ／ 錨定 —— 那是在正確的數字上修錯的層。

---

### 19.8 Last known good ／ best compromise —— 目前最適合當 baseline 的版本

**沒有 last known good。**（19.1）能談的只有 best compromise，而且只能靠 toggle 逼出來：

| 候選 baseline | 怎麼到達 | 內容 | 代價 |
|---|---|---|---|
| **B0 — endpoint-only**（建議作為對照組） | `Bake_Climb2m.Traversal.enabled = false` | correction 窗回到 `[0, 0.45]` ＝ 1.62 s；**同時關掉** piecewise／Hand IK／Exit 凍結／reboundForce 四層 | 回到 §13 之前：手指可能插牆、結尾浮空＋落地聲 |
| **B1 — piecewise，無 Hand IK** | `Bake_Climb2m.handIKEnabled = false`（或 X Bot 上 `enableHandIK = false`） | 保留 root 對齊與 exit 貼地，拿掉整個 IK 層 | 抓握期間手自漂 1.9–7.0 cm |
| **B2 — 現況（§16）** | 不動 | 全部層都在 | 使用者已回報不滿意 |

> B0 之所以能一鍵關掉四層：`Traversal.enabled = false` ⇒ `HasValidBakedData` 為 false
> ⇒ `TryBuild` 以 `MissingTraversalBlock` 退出 ⇒ 走 endpoint plan；而 Exit 凍結與 reboundForce
> 的條件都是 `plan.IsPiecewise`，Hand IK 的條件是 `plan.IsPiecewise && HandIKConfigured`。
> 面板會直接顯示 `Plan: ENDPOINT (piecewise: MissingTraversalBlock) window 0.00–0.45 [from bake]`。

**本節的判斷：B2 ＝ 目前最好的「功能完整度」，但沒有證據支持它是最好的「體感」。**
B0 從來沒有在 §13–§18 的任何一輪被當成對照組跑過——
**這正是「效果好像變差了卻說不出哪一次」的直接後果：沒有對照組。**

---

### 19.9 建議保留 ／ 停用 ／ 回退

#### ✅ 保留（有機制或量測支撐，且有測試守住）

- Root／Pose 時間同步（`TrajectoryNormalizedAt ≡ n`）
- exit 終點由 `TryGetNaturalExitDepth` 決定
- grip edge 以四指指尖為基準（§13.1，`B14`）
- IK 窗提前 release（§15，`B15`）—— 唯一一個有兩套獨立量法互相印證的改動
- 診斷可見度（plan mode／rejection／Debug 視窗）
- 抓握期間 root correction 凍結（§16 的 freeze，實測漂移 0.000 cm）

#### ⏸️ 建議**停用觀察**（不是刪除，是當對照組跑一次）

- **`RootReachConstraint`** —— 現在是全零 no-op（19.2.1）。它的存在只會讓下一輪繼續以為
  「已經有東西在壓住伸展」。建議在 bake 端關掉，或至少明確記錄「§15 之後它恆為零」。
- **Exit 之後的 plan 位置凍結 ＋ reboundForce** —— 兩者同時作用於同一區間（C2）。
  至少要能單獨關掉其中一個，才分辨得出 R2 是哪一個造成的。
- **Collision profile 的三個方法** —— 目前每幀在跑、效果為零（C5）。

#### ↩️ 建議**回退／重新評估**

- **§16 的「root 只服務 primary」** —— 架構上正確，**視覺上把誤差集中成不對稱**（R3）。
  這是唯一一個與「使用者第 1 項抱怨」直接對應得上的改動。
  ⚠️ 但**不要在拿到 19.7 的答案之前動它**——如果 19 cm 來自 ledge frame，改 primary／dual 沒有用。
- **§17 ／ §18** —— 已退版，維持退版。**不要在沒有新證據前重試同一個方向。**

---

### 19.10 可逆實驗 —— 優先於任何重寫

全部只動**既有欄位**，不加程式、不加參數、不改 clip。每一項都能一句話還原。

| # | 實驗 | 怎麼做 | 回答什麼問題 | 還原 |
|---|---|---|---|---|
| **E0** | 🔴 **先建立還原點** | 使用者 `git add` ＋ commit 目前整包（含 untracked 資產） | 讓後續每個實驗第一次有 diff 可看 | — |
| **E1** | **顯示 committed ledge frame** | `TraversalDebugWindow` 加一段印 `EdgeOrigin／Tangent／TangentMin/Max／TopNormal／WallNormal`（唯讀，不影響執行） | **19.7 的三選一**。這是整份報告裡唯一「非做不可」的一項 | 刪掉那一段 |
| **E2** | **endpoint 對照組** | `Bake_Climb2m.Traversal.enabled = false`（Editor API） | 起攀手感是不是 piecewise 的 7 格窗造成的（R1／C1） | 設回 `true` |
| **E3** | **關掉 Hand IK** | X Bot 的 `TraversalHandIKController.enableHandIK = false`（**Play 中即可切**） | IK 這一層到底是幫忙還是幫倒忙 | 打勾回來 |
| **E4** | **縮窄 entry 合法帶** | `entryPolicy.maximumCloseError` 0.4 → 0.10 | 如果只准在 0.35–0.81 起攀就變好 ⇒ 確認 C1 是主因，且**不需要改任何 IK** | 設回 0.4 |
| **E5** | **確認 Hand IK 真的在跑** | Play 時開 `TraversalDebugWindow`，或 X Bot 上勾 `drawTraversalHandIKDebug` ＋ `drawTraversalPanelText`，看 `Hand IK:` 那行是不是 `Active` | WORKLOG 點名的未驗證前提（§14.2 的歷史正是「有程式、有測試、遊戲裡沒跑過」） | 取消勾選 |
| **E6** | **站位掃描** | 跑 `docs/artifacts/residual_sweep.cs.txt`（0.15 → 0.80 m，每 5 cm） | 19.7 的推導說 residual 與站位無關；**掃描若證實，就排除「站位造成 19 cm」這條路** | 刪暫存檔 |

**建議順序：E0 → E1 → E5 → E2／E3／E4（A/B）→ E6。**
E1 與 E5 各只需要一次 Play，而它們決定後面三個實驗有沒有意義。

---

### 19.11 下一輪最多三個大方向

> **排序依據是「使用者看得到」×「證據強度」，不是「架構上多漂亮」。**

#### 方向 1 ⭐ **先把 19.7 的矛盾解決掉 —— 不做任何修改**

`R 19.0 cm` 與程式推導的 1.93 cm 差 10 倍。**在分辨出是「ledge frame 錯了」還是「數字讀錯了」之前，
任何對 IK ／ 錨定 ／ primary-vs-dual 的改動都是在猜。** §15–§18 四輪都是這樣做的，結果是使用者說更爛。
**成本：E1 ＋ E5，一次 Play。**

#### 方向 2 ⭐ **entry 契約：讓「允許起攀的範圍」與「動畫能吸收的範圍」對齊**（C1／R1）

現在系統承諾 0.75 m 寬的入場帶，動畫吸收能力 0，補償窗 0.23 s。
這不是「再加一層 correction」能解的，而是**兩個數字之間的契約問題**。
三條可走的路（下一輪選一條討論，本輪不選）：

- **(a) 收窄 entry 帶**（E4）—— 最便宜，代價是玩家更常被拒絕；
- **(b) 把 correction 窗從「contact 那一格」解耦** —— 讓 warp 在 contact **之前**就開始收斂，
  而不是在 contact 那一格才全部到位；這會動到 piecewise 的語意，**需要先談**；
- **(c) 接受 Climb2m 這支 clip 沒有 approach** —— 用一支帶 approach 的 clip，或在進入 traversal
  之前先做一段位置對齊（參考實作普遍有這個「approach／adjust」階段 —— §3，**我們沒有**）。

> §8 的「❌ 缺了一層」講的正是這件事。19.2.2 給了它第一個量化證據。

#### 方向 3 **exit 交接：把離散的那一層找出來**（R2／C2／C3）

§17.5 已證明 root 是平滑的。剩下三個離散來源：Foot IK 二值啟用、plan 位置凍結、reboundForce。
**先用 E2 分辨是不是 piecewise 那一側造成的，再決定要不要碰 Foot IK 的閘門**
（注意：fade 族是 M3.2~M3.4 刻意移除的，**要重新引入必須當成架構決定討論，不能順手加**）。

---

### 19.12 本節沒有做的事（明確聲明）

- 沒有改任何 `.cs` ／ `.asset` ／ `.prefab` ／ `.unity` ／ `.meta`。
- 沒有執行任何 git 操作。
- 沒有跑測試（本輪是純靜態診斷，未連線 live Editor）。
- 沒有提出修法——19.9／19.10／19.11 全部是**實驗與待裁決的方向**，不是實作計畫。
