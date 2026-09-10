# 13 — 戰鬥移動診斷（Combat Locomotion）

> **狀態**：🔍 **診斷 ／ 規劃，未實作**（2026-09-06）
> 本檔不是規格，是一次**依磁碟核對**的現況盤點，用來回答「面向敵人橫移」到底缺什麼。
> 📌 結論先講：**動畫資產幾乎全部已經在專案裡，缺的是資料形狀與一條架構 seam。**

---

## 1. 劍看不到 —— 真因不是位置

### 1.1 量到的事實

| 檢查項 | 結果 |
|---|---|
| `Sword.obj` 存在 | ✅ 18,014 頂點 |
| 尺寸 | ✅ 合理：X 0.122 ／ Y 0.998 ／ Z 0.934（公尺）。**不是縮放問題** |
| 網格中心 | (0.000, −0.001, 0.000) ⇒ **樞紐在劍的正中央，不是握把** |
| 形狀走向 | Y 與 Z 都接近 1.0、X 只有 0.12 ⇒ **刀身在自身 YZ 平面上是斜的** |
| `WeaponSocket` 掛載 | ✅ X Bot 上存在且 `m_Enabled: 1` |
| socket 引用 | ✅ 指向 `mixamorig:RightHand` 的 Transform |
| **材質** | 🔴 **`Sword.obj` 的 `externalObjects` 把 `defaultMat` 重映到 guid `68eb37acd4747ac4ebb9b2999b0582bb`——該 GUID 在整個專案裡只出現在 `.obj.meta` 自己的重映條目中，也就是<b>指向一個不存在的材質</b>** |

### 1.2 結論

**主因是材質引用斷掉。** MeshRenderer 的材質槽解析為 null 時，Unity **什麼都不畫**——
不是洋紅色（洋紅是「材質在、shader 遺失」），是**完全看不見**。這與回報的症狀一致。

**次因（修好材質後仍會咬）**：
- 樞紐在劍的正中央 ⇒ `localPosition = 0` 會讓半把劍埋進手掌，另外半把往反方向穿出
- 刀身在 YZ 平面上是斜的 ⇒ `localEulerAngles = 0` 的朝向一定是歪的

⚠️ 還有一個潛在第三因：專案裡真正存在的 `swordmaterial.mat` 使用 shader guid
`be891319084e9d147b09d89e80ce60e0`，而本專案所有自有材質用的是 `933532a4fcc9baf4fa0491de14d08ed7`。
本專案是 **URP**（`GraphicsSettings` 已確認）⇒ 若前者是 Built-in／第三方 toon shader，
即使接上材質也可能是洋紅。**接上之後要看一眼。**

### 1.3 最低風險修法（全部在 Editor，不碰 prefab 階層）

1. Project 選 `Sword.obj` → Inspector **Materials** 分頁
2. `Material Creation Mode` 改 **Import via MaterialDescription**（或 Standard），
   `Location` 改 **Use Embedded Materials**；
   或維持 External，把 `Remapped Materials` 的 `defaultMat` 指到**同資料夾的 `swordmaterial.mat`**
3. **Apply**，看 Project 縮圖有沒有出現劍
4. 若是洋紅 ⇒ 選 `swordmaterial.mat`，Shader 改 **Universal Render Pipeline/Lit**，
   把 `Diffuse_Bake` 接 Base Map、`Normal_Bake` 接 Normal Map
5. 最後才調 `WeaponSocket` 的 `localPosition` ／ `localEulerAngles`
   （進 Play 後選中生成出來的劍調整，Inspector 右鍵 `Rebuild Attachment` 可即時重掛）

📌 **順序很重要**：材質沒修好之前調位置是白費工——你調的是一個不會被畫出來的東西。

---

## 2. 動畫資產盤點（依磁碟核對，2026-09-06）

### 2.1 🎉 8 向 strafe **早就在專案裡**

`Assets/MovementAnimsetPro/Animations/MovementAnimsetPro_RunStrafeUpdate.fbx`（`animationType: 3` Humanoid）：

| clip | 用途 |
|---|---|
| `RunBwdLoop` | 後退 |
| `RunLtLoop` ／ `RunRtLoop` | **純左右橫移** |
| `RunStrafeLeft45Loop` ／ `RunStrafeRight45Loop` | 前斜 |
| `RunStrafeLeft135Loop` ／ `RunStrafeRight135Loop` | 後斜 |
| `StrafeRight45Loop` ／ `StrafeLeft135Loop` | 追加變體 |
| `Crouch_Walk{Fwd,Bwd,Lt,Rt}` ＋ 45／135 | 蹲伏 8 向（本輪用不到） |

🔴 **這包目前只被 Throw 用到**（`Throw_Start`／`ThrowLoop`／`ThrowEndFar`／`ThrowCancel` 四支住在同一個 FBX）。
**所有 strafe clip 的引用次數是 0。**

### 2.2 轉身／起步／跳躍也都在

`MovementAnimsetPro.fbx`：
- **原地轉身**：`TurnRt90_Loop`／`TurnLt90_Loop`／`TurnRt180`／`TurnLt180`
- **帶轉向的起步**：`WalkFwdStart90_L/R`、`WalkFwdStart180_L/R`、`RunFwdStart90_L/R`、`RunFwdStart180_L/R`
- **跑動中轉身**：`RunFwdTurn180_{R,L}_{LU,RU}`
- **跳躍全套**：`Jump_place_ALL`、`Jump_walk_{ru,lu}_ALL`、`Jump_run_{ru,lu}_ALL`、
  `JumpIdleStart`、`JumpIdleLand`、`JumpIdleLandHard`、`JumpIdleLand2Walk`
- **傾斜／弧線**：`RunArchLoop_L/R`、`RunFwdLoop_Lean{L,R}`

⇒ 使用者提到的「跳躍起步」「大幅度轉身」**素材完全不缺**。

### 2.3 持劍動畫（EEJANAI）

`battle stance`／`deffensive stance`／`damaged (tired) stance`／`slash1`–`slash9`，皆 Humanoid。
**只有站姿與揮擊，沒有持劍的移動循環**——這正是使用者不想整套換掉的原因。

### 2.4 已烘好卻沒接的 Bake

| Bake | 內容 | 引用次數 |
|---|---|---|
| `Bake_TurnRt180` | `RotationFinishedTime` 1.5333 | **0** |
| `Bake_RunFwdTurn180_R_LU` | 0.7667、`TargetLocalDirection` (0,0,−1) | **0** |

---

## 3. 目前的 locomotion 為什麼用不到這些 clip

`Assets/ScriptableObjects/Animation/Locomotion.asset` 是
**`LinearMixerTransition`（1D）**，四個 threshold：`0 / 0.35 / 0.75 / 1`
＝ Idle → Walk → Run → Sprint。

**它的輸入只有「速度大小」，沒有「方向」。** 因此：
- 8 向 strafe clip 沒有地方可以插進去
- 要用必須換成 **2D mixer**（`MixerTransition2D` ／ Cartesian），輸入 (x, y) 移動向量

---

## 4. 🔴 真正的阻擋：移動方向與朝向被綁死

`MotionDriver.ExecuteBaseMovement`（順序 6a）：

```csharp
bool hasFacingRequest = ApplyFacingRequest();
...
Vector3 targetDirection = camForward * MoveDirection.y + camRight * MoveDirection.x;

if (!hasFacingRequest && targetDirection.sqrMagnitude > 0.001f)
    transform.rotation = Slerp(transform.rotation, LookRotation(targetDirection), 12f * dt);

// 4. 水平速度採用 transform.forward（角色當前肉體正前方）
horizontalVelocity = transform.forward * currentSpeed;   // ⚠️
```

**速度方向永遠是 `transform.forward`。** 這不是疏漏——原註解寫明是為了
「使轉彎具備流暢的體重弧線感」，是刻意的設計。但它有兩個後果：

### 4.1 沒有 facing request 時：看起來正確，但結構上不可能 strafe

先轉向輸入方向、再往前走 ⇒ 永遠是「面向自己走的方向」。
**這正是敵人繞圈時「面朝切線繞行」的成因**，也是玩家無法橫移的成因。

### 4.2 有 facing request 時：**這是一個現行的 bug，不只是缺功能**

瞄準或出手期間朝向被鎖在目標上，但速度**仍然是 `transform.forward`**
⇒ **按左鍵不會往左橫移，而是朝著敵人走過去。**

📌 目前之所以還沒被發現，是因為出手期間玩家通常不推搖桿。

### 4.3 🆕 同一個根因的第二個症狀：**連段中人與火球分家**（2026-09-06 Play 實測）

ADR-005 結案驗收時實際看到的：**Fireball 連段期間，敵人走動 ⇒
身體維持在起手鎖定的朝向，火球卻每一段重新瞄準** ⇒ 人朝 A、火球飛 B。

| 誰 | 什麼時候決定方向 | 依據 |
|---|---|---|
| **身體朝向** | `ActionState.OnEnter` **鎖一次** | `AimResolver.TryLatchAutoTargetFacing()` 的快照 |
| **發射方向** | `ThrowProjectileEmitter.Release()` **每段重算** | `ResolveThrowRotation()` → 當下的 `TryGetAimPoint()` |

**兩個擁有者、兩個時點、沒有單一權威。**

⚠️ **兩邊各自都是對的**：朝向鎖一次是為了「揮擊途中甩相機角色不要跟著轉」（`docs/11` §8.3 的原意）；
發射方向每段重算是為了「火球要打得中」。**衝突只在連段這種跨越多次 Release 的 Action 上才浮現。**

📌 這與 §4.1／§4.2 是**同一類問題**：`MotionDriver` 的移動方向、`AimResolver` 的瞄準方向、
`ActionState` 的鎖定朝向——三者沒有一個共同的擁有者。
⇒ **不要只修連段**（例如「每段重新 latch」）：那只是把兩個擁有者變成兩個時點一致，
下一個跨 Release 的機制出現時同樣的裂縫會再開一次。

> ### ✅ 2026-09-06 — 已升級為 **ADR-007（Direction Authority）**
>
> §4.1／§4.2／§4.3 三個症狀經核對確認是**同一個根因**：三個方向概念（移動／朝向／瞄準）
> 共用兩個載體，因此只能互相冒充。決策見 **`docs/ADR/007-direction-authority.md`**（🟡 **Trial**，2026-09-06 裁決；已是實作基線但尚未驗證），
> 契約面與切片見 **`docs/14-direction-authority.md`**。
> 盤點時另發現 `docs/09` §5.2 的 trip-wire ①（Core 讀 AimPoint）**已於 2026-09-05 被安靜跨過**
> ——`ActionState`／`FullBodyStateMachine`／`CharacterPipelineRunner` 都直接持有具體的 `AimResolver`。

### 4.4 缺的 seam

需要把兩件事分開：

| 概念 | 現況 | 應該是 |
|---|---|---|
| **移動方向** | `transform.forward`（＝朝向） | 由輸入／AI 產生的世界方向 |
| **朝向** | 由移動方向決定，或被 facing request 覆蓋 | 獨立決定（目標、相機、或移動方向） |

`AimResolver.RequestFacing` → `MotionDriver.ApplyFacingRequest` 這條**朝向** seam 已經存在。
缺的是**「朝向被覆蓋時，速度該用哪個方向」**——目前無條件用 `transform.forward`。

---

## 5. 上半身持劍 ＋ 下半身 Movement Animset Pro：可行性

### 5.1 方向合理

不必為了持劍重找一整套 Idle／Forward／Backward／Strafe，是**正確的成本判斷**。
`docs/03-animation-roadmap.md` 的輪 6 Combat 本來就預告了上身層。

### 5.2 主要風險（依嚴重度排序）

| # | 風險 | 說明 |
|---|---|---|
| 1 | **脊椎斷層** | EEJANAI 的 `battle stance` 是**全身**動畫，含骨盆與腿。AvatarMask 只取上半身後，上下兩段對骨盆朝向的假設不同 ⇒ 腰部可能出現扭轉或前傾。**這是最常見也最難調的那一個** |
| 2 | **兩套骨架的比例差異** | EEJANAI 與 Kubold 是不同來源。兩者都 Humanoid、可重定向，但**肩寬／手臂長度的重定向誤差會直接反映在武器位置上**——手的世界座標差一點，劍尖就差很多 |
| 3 | **遮罩切點** | 切在 `Spine` 太低（下半身被上身帶著轉）、切在 `Spine2`／`Chest` 較安全但跑步的軀幹擺動會消失 ⇒ 上半身看起來僵硬 |
| 4 | **武器附著點** | `WeaponSocket` 掛在 `mixamorig:RightHand`。手屬上半身層 ⇒ 劍會跟著 combat pose 走，**這是對的**。但層權重淡入淡出期間，手的位置會在兩個姿勢之間插值 ⇒ **劍會飄** |
| 5 | **Root motion** | 若 combat 姿勢 clip 帶位移曲線，遮罩後**仍可能透過 root 影響位移**（依 Animancer 的 root motion 設定）。必須確認上身層 `_ApplyAnimatorIK` ／ root motion 是關的 |

### 5.3 建議的緩解

- 遮罩從 **`Spine1`／`Spine2`** 開始試，不要從 `Hips`
- combat pose 只用**站姿**（`battle stance`），**不要**用揮擊 clip 當持續姿勢
- 上身層權重用**淡入**而非瞬切，並在淡入期間接受劍會飄一下
- 風險 2 只能靠**實際看**：把兩個 clip 同時播一次就知道重定向誤差有多大

### 5.4 這是不是 ADR-006 的核心使用案例

**是，而且比原本寫的更好。**

ADR-006 目前的動機是「移動中出手」——那是**瞬時**的（0.4–1.6 秒）。
「持劍備戰姿態」是**持續**的，對混合品質的要求高得多：瞬時的脊椎斷層看不太出來，
持續的會一直在畫面上。

⇒ **應該把 §5 的內容併進 ADR-006 §1，讓 Acceptance 以「持續持劍移動」為準**。
用嚴格的那個案例驗，寬鬆的那個自然成立。

### 5.5 Combat Idle／Move 應該是 Presentation 差異嗎

**是。** FSM 不該知道 Animation Layer 的存在（ADR-006 D1 已這樣主張）。

但「**現在是不是戰鬥狀態**」不是 Presentation 能自己回答的——它是 gameplay 概念。
最小形狀是：一個布林（或「最近一次戰鬥互動的時間戳」）由 gameplay 層擁有，
Presentation 讀它決定上身層權重。**這需要一個黑板欄位** ⇒ 觸及 dev-spec §1 schema
⇒ **屬於 ADR-006 應該一併裁決的範圍**，不要在實作時偷偷長出來。

---

## 6. ~~被 ADR-005 Trial 擋住的清單~~ → ✅ **2026-09-06 閘門已解除**

> **ADR-005 於 2026-09-06 由 `Trial` 翻牌為 `Accepted`**（A–G 七條全數通過）。
> Acceptance **G**（五個下游檔案零修改）的觀察期結束 ⇒ `docs/11` §10.2 的 ⛔ 名單**自此不再適用**。

| 想做的事 | 需要動 | 2026-09-05 | 現在 |
|---|---|---|---|
| 面向敵人橫移（movement／facing 分離） | `MotionDriver` 位移路徑 | 🔴 擋住 | ✅ **可做** |
| 2D locomotion mixer（用 strafe clip） | `LocomotionModel` 動畫參數驅動 | 🔴 擋住 | ✅ **可做** |
| 上半身持劍層 | `AnimationFacadeBase` 契約 | 🔴 擋住 | ✅ **可做**（仍需 ADR-006 裁決） |
| C2／C3 轉身 | `LocomotionModel`／`LocomotionStopSelector` | 🔴 擋住 | ✅ **可做** |
| 連段朝向／發射方向的單一權威（§4.3） | 跨 `MotionDriver`／`AimResolver`／`ActionState` | 🔴 擋住 | ✅ **ADR-007（🟡 Trial）** ⇒ 實作切片 **S2** |
| 戰鬥狀態黑板欄位 | dev-spec §1 schema | 🟡 需 ADR | 🟡 需 ADR（併入 ADR-006） |
| 修劍的材質與位置 | 資產 ／ Editor | ✅ 不擋 | ✅ 不擋 |

⚠️ **閘門解除 ≠ 全部同時開工。** §7 的順序仍然成立，而且比之前更重要——
現在沒有東西擋著，唯一防止「一次改三層然後分不清誰壞了」的就是這個順序本身。

---

## 7. ADR-005 結案後的最小實作順序

> 每一步都要能單獨 Play 驗收，且不與下一步混在一起。

> 🔄 **2026-09-06 修訂**：步驟 1 的形狀已由 **ADR-007** 取代——不是「`hasFacingRequest` 為真時改用 `targetDirection`」
> （那仍是 `MotionDriver` 在決定方向），而是**移動方向整條鏈改世界座標、由 movement chain 獨佔**。
> 且 **§4.3 的 A6 修法（ADR-007 S2）必須緊接在步驟 1 之後**——別名解除後，`GroundEffectSink`
> 的 `casterRoot.forward` 落點會短暫失準（ADR-007 R3）。實作清單見 `docs/14` §2／§4。

1. **movement／facing 分離**（最小、最關鍵）
   ~~`MotionDriver.ExecuteBaseMovement` 在 `hasFacingRequest` 為真時，
   速度改用 `targetDirection` 而不是 `transform.forward`。~~ → 見上方修訂，實作定形在 `docs/14` §2.1。
   **這一步不需要任何新動畫**——先讓「面向敵人、往左走」在物理上成立。
   ⚠️ 立刻可見的副作用：沒有 strafe 動畫，會是「面向敵人但播著前進動畫橫移」＝滑步。
   **這是預期中的中間狀態**，下一步解決。

2. **2D locomotion mixer**
   `Locomotion.asset` 從 `LinearMixerTransition` 換成 2D，接上 §2.1 的 8 向 clip。
   輸入是 (x, y) 移動向量在**角色本地座標**的投影。
   ⇒ 第 1 步的滑步在這一步消失。

3. **戰鬥狀態 ＋ 上身持劍層**（ADR-006）
   前兩步完成後才做——否則「持劍」與「橫移」兩組問題會混在一起難以歸因。

4. **C2／C3 轉身**（`Bake_TurnRt180` 等已備）

📌 **1 與 2 是同一個功能的兩半**，但刻意分開驗收：
第 1 步驗的是「方向對不對」，第 2 步驗的是「動畫對不對」。
合併會讓 Play 出問題時分不清是速度算錯還是混合權重錯。

---

## 8. 為什麼現在不該調速度

「速度感不對」目前有**六個候選成因**，而其中至少三個與數值無關：

| # | 候選 | 現在能不能判斷 |
|---|---|---|
| 1 | movement speed 數值本身 | ❌ 被 3／5 汙染 |
| 2 | 相機 FOV／距離／阻尼 | 🟡 可獨立調，但會補償掉 3 的症狀 |
| 3 | **動畫步幅與實際速度不匹配** | ❌ 1D mixer 只有四個 threshold，中間速度一定會滑步 |
| 4 | **沒有 strafe／後退動畫** | ❌ 已確認缺（§2.1 有資產但沒接） |
| 5 | **缺 combat-facing** | ❌ 已確認（§4） |
| 6 | 敵人 engagement 速度比例 | 🟡 可獨立調 |

🔴 **關鍵風險**：現在調數值，等於用速度去**補償**動畫與朝向的缺陷。
等第 7 節的 1／2 做完，補償過的數值全部要重調一次——而且屆時很難分辨
「當初為什麼調成這個值」。

⇒ **建議只在 §7 的步驟 1、2 完成後再統一調速度**，
唯一的例外是相機（第 2 項）——它與 locomotion 正交，想調隨時可以調。

---

## 9. 8 向 Locomotion 實作規格（S4a，2026-09-07）

> 對應 §7 步驟 2。**程式面與資產面刻意分開**：程式先讓參數存在且正確，資產換上去才會有畫面。
> 兩者分開之後，「動起來不對」可以立刻歸因是數值錯還是混合權重錯。

### 9.1 為什麼現在才做得成

`MotionDriver` 在 ADR-007 S1 之前把移動方向別名成 `transform.forward` ⇒
`InverseTransformDirection(moveDir)` **恆等於 (0, 0, 1)**，2D mixer 的橫軸永遠是 0。
S1 解除別名後，世界移動方向與朝向獨立，**本地投影才第一次有意義**。

### 9.2 程式面（AI 可做，最小改動）

| # | 檔案 | 改動 |
|---|---|---|
| 1 | `Presentation/Animation/AnimationFacadeBase.cs` | 新增兩個常數 `ParamMoveX = "MoveX"`／`ParamMoveZ = "MoveZ"`。**Facade 本身零改動**——它已是通用參數通道（`animancer.Parameters.SetValue`），訂閱關係在資產端 |
| 2 | `Core/Movement/Models/LocomotionModel.cs` | 順序 3 `Tick` 內，除既有 `SetFloat(ParamMoveSpeed, speed)` 外，**追加發布本地座標分量**：`local = transform.InverseTransformDirection(data.MoveDirection)` ⇒ `SetFloat(ParamMoveX, local.x * speed)`／`SetFloat(ParamMoveZ, local.z * speed)` |

⚠️ **`ParamMoveSpeed` 繼續發布，不得移除**——現行 1D `Locomotion.asset` 還在用它。
⇒ **程式先落地不會改變任何畫面**（新參數沒有訂閱者），資產換上去才生效。這是刻意的：**可獨立驗收、可獨立 revert**。

**為什麼乘上 `speed`**：與既有 1D 的門檻慣例一致（`threshold = speed_i / speed_max`）。
方向是單位向量、強度在 `MoveSpeed`，兩者相乘後 (x, z) 的**長度就是速度階層**，
於是 2D mixer 的取樣點與 1D 的門檻是同一套座標，不需要第二套換算。

### 9.3 資產面（✅ 2026-09-07 已接線）

1. `Locomotion.asset`：`LinearMixerTransition` → **`MixerTransition2D`（Cartesian）**（已完成）
2. 新增 `MoveX.asset`／`MoveZ.asset` 兩個 StringAsset，兩個 `ParameterName` 分別綁
   **`MoveX`** 與 **`MoveZ`**（已完成；字串與 §9.2 常數一致）
3. 取樣點接 8 向 clip：
   前 `RunFwdLoop`(0,1)／後 `RunBwdLoop`(0,-1)／左 `RunLtLoop`(-1,0)／右 `RunRtLoop`(1,0)／
   四斜角 `RunStrafeLeft45Loop`(-0.7,0.7)／`RunStrafeRight45Loop`(0.7,0.7)／
   `RunStrafeLeft135Loop`(-0.7,-0.7)／`RunStrafeRight135Loop`(0.7,-0.7)
4. 中心點（0,0）維持 Idle；全 Mixer 共 **9 點**（已完成）

📌 **實際 FBX 內容修正**：`MovementAnimsetPro_RunStrafeUpdate.fbx` 只有後／左／右／四斜角共 **7 支**，
沒有 `RunFwdLoop`；前進點因此直接引用 `MovementAnimsetPro.fbx` 的 `RunFwdLoop`。
其餘 7 個方向點才來自 `RunStrafeUpdate.fbx`。全部維持 FBX sub-clip 直引，沒有複製 clip。

📌 **速度階層（Walk／Run／Sprint）在本輪先不進 2D**——先把「方向對」做完再談「速度階層 × 方向」的二維展開，
否則取樣點會從 9 個變成 27 個，而**每一個都要人工填**。§8 的「先別調速度」仍然適用。

### 9.4 🔴 首次 Play 稽核（2026-09-07）—— 接線全對，問題全在資料

> 使用者回報「體驗非常詭異」。依磁碟稽核：**接線零問題**，兩個**資料層**缺陷。

**接線稽核結果（全部 ✅）**：`PlayerCombatContextSource`／`CharacterFacingSource`／`HeadLookController`
三者都掛在 **X Bot Root**（與 `CharacterPipelineRunner` 同一 GameObject ⇒ `GetComponent` 找得到）；
`headBone` → `mixamorig:Head`；2D mixer 的 `_ParameterNameX/Y` → `MoveX.asset`／`MoveZ.asset`；9 個取樣點座標與 §9.3 一致。

#### 缺陷 A：7 支 strafe clip **從未走過匯入 SOP**（dev-spec §0.4）

與正在用的 `RunFwdLoop` 逐項對照：

| 設定 | `RunFwdLoop`（已 SOP） | 7 支 strafe | §0.4 要求（Locomotion-位移） |
|---|---|---|---|
| `loopBlendOrientation`（Rot Bake Into Pose） | 1 | **0** | **1** |
| `loopBlendPositionY`（Y Bake Into Pose） | 1 | **0** | **1** |
| `keepOriginalOrientation`（Rot Based Upon） | 1 = Original | **0 = Body Orientation** | **Original** |
| `keepOriginalPositionXZ`（XZ Based Upon） | 1 = Original | **0** | **Original** |

§0.4 原文就是這個症狀：「**未 Bake Into Pose 的 root motion 成分會被引擎抽出後丟棄——輕則水平滑步
（hips 被錨定、雙腳反向滑動），重則垂直位移被抹平**」。
而 **Rot Based Upon = Body Orientation 對 strafe clip 特別致命**：strafe 的語意就是「身體朝前、往側面移動」，
用身體朝向當參考系會把整支 clip 轉掉 ⇒ **橫移時身體朝向歪掉**。

**修法**：Project 視窗**選那 7 支子 clip**（**⛔ 不要選整個 FBX**——`Throw_*` 七個條目也住在這支 FBX 裡，
ADR-004 已結案的東西會被一起灌壞；§0.4 規則 1 明文要求多類型 FBX 用子 clip 選取）
→ 右鍵 `Project 動畫匯入 SOP` → **Locomotion-位移** preset。

#### 缺陷 B：換成 2D 時，1D mixer 攜帶的兩樣東西被**靜默丟掉**

`git diff` 對照舊版：

| | 舊 1D | 新 2D |
|---|---|---|
| clip | Idle ／ **Walk** ／ Run ／ **Sprint**（第四支 FBX） | Idle ／ Run ＋ 8 向 |
| `_Speeds` | `1 / 1.3327742 / 1.3124558 / 1` | **全部 1** |

1. **Walk 與 Sprint 階層整個不見** ⇒ 半速移動變成「idle 與 run 各半的混合」＝慢動作跑步。
2. **`_Speeds` 的 1.33／1.31 是步幅↔實際速度的校準值**，歸 1 就直接滑步。

⚖️ **這是 §9.3 指示不完整造成的**：該節列了 9 個取樣點，卻沒說明「Walk／Sprint 與 `_Speeds` 校準會一併消失」。**已於此補上。**

~~**止血**：`_Speeds` 填回 1.3124558~~ 🔴 **2026-09-07 更正：這個數字是錯的，見 §9.5**
（同一組 24-frame run cycle，數量級一致），之後再依 Bake 個別校準。

**正解（下一步，不在本輪）**：**巢狀 mixer**——外層 1D 速度 mixer，子節點是 walk 的 2D 與 run 的 2D。
每個 ring 保有自己的 `_Speeds` 校準，取樣點是 8＋8 而不是手填 27 個。
⇒ 這也回答了 §9.3 「速度階層先不進 2D」的後續：**不是不做，是換一個不會爆炸的形狀做。**

#### 順帶：`_SynchronizeChildren` 全 0 —— **不是回歸**

舊 1D 也是 `00000000`（全 0），所以這不是換 2D 造成的。
但 8 向 blend 對腳步相位遠比 1D 敏感：**A／B 修完後若仍有腿部不協調，這是第二順位可調的東西。**

#### 修復順序（不要一次全改）

1. **先 A**（匯入 SOP）——它影響的是姿勢本身，不修的話後面的校準都是在錯的姿勢上調
2. **再 B 的止血**（`_Speeds` 填回）
3. 兩者都做完再看要不要動 `_SynchronizeChildren`

#### 9.4.1 精確清單與驗收機制（2026-09-07 補正）

⚠️ **是 7 支不是 8 支**：mixer 的 8 個方向裡，**前方用的是 `RunFwdLoop`（住在 `MovementAnimsetPro.fbx`，已 SOP、已烘焙）**，
只有另外 7 支住在 `MovementAnimsetPro_RunStrafeUpdate.fbx` 且未處理：

`RunBwdLoop`／`RunLtLoop`／`RunRtLoop`／`RunStrafeLeft45Loop`／`RunStrafeRight45Loop`／`RunStrafeLeft135Loop`／`RunStrafeRight135Loop`

**烘焙現況**：23 個 `MotionBakeData` 資產中，mixer 用到的 9 支只有 `Bake_Idle` 與 `Bake_RunFwdLoop` 存在
⇒ **上列 7 支全部沒有 Bake**。這與 `_Speeds` 全是 1 是**同一件事**：沒有 Bake 就算不出校準值。

**🔒 驗收已機器化**：`Assets/_Project/Tests/EditMode/LocomotionMixerWiringTests.cs`（**純唯讀稽核**，不改任何資產）

| 測項 | 驗什麼 | 現在 |
|---|---|---|
| **L-1** | mixer 引用的每支 clip 的 7 個 §0.4 匯入欄位（Idle 用原地型期望值、其餘用位移型） | 🔴 7 支 × 4 欄不符 |
| **L-2** | 每支 clip 都有 `MotionBakeData`（掃 `SourceClip` 對照） | 🔴 缺 7 支 |
| **L-3** | 非 Idle 的 `_Speeds` 不得全部是 1（校準遺失偵測） | 🔴 8 支全是 1 |

⇒ **三條紅燈就是這次的交付物。** 使用者做完 Editor 工作後轉綠 ＝ 驗收通過，不需要人工再對一次。

**執行順序（不可交換）**

1. **SOP**：Project 視窗選上列 7 支**子 clip**（⛔ 不要選整個 FBX，`Throw_*` 也在裡面）→ 右鍵 `Project 動畫匯入 SOP` → **Locomotion-位移**
2. **烘焙**：對那 7 支建立 `MotionBakeData`，`SourceClip` 指向原 FBX 子 clip
   ⚠️ **順序不能反**：§0.4 規則 1 明文「該 clip 若有烘焙資產，套用後**必須重烘焙**」——
   SOP 改的正是 root motion 的抽取方式，**先烘再 SOP 等於烘到一組即將失效的數字**
3. **`_Speeds` 校準**：`speed_i = (threshold_i 對應的實際速度) / MotionBakeData.GetRepresentativeSpeed()`
   （實際速度 ＝ `threshold_i` 的長度 × `MotionDriver.moveSpeed`）
4. 重跑 `LocomotionMixerWiringTests` ⇒ 三條應全綠

📌 **為什麼這三件事 AI 不做**：匯入 SOP 與烘焙都是 **Unity Editor 選單動作**，而 §0.4 規則 1 明文**禁止手改 `.meta`**；
`_Speeds` 住在 Animancer 的 `SerializeReference` 內部序列化，**CLAUDE.md 的 B11 先例已否決**自動化寫入這類第三方內部結構。
⇒ **能機器化的是「驗收」，不是「施工」**——所以本輪交付的是 L-1／L-2／L-3。

---

## 9.5 🔴 更正：單環 2D mixer 是**結構性**錯誤，不只是沒校準（2026-09-07）

### 9.5.1 先把校準公式從舊資產反推出來（已完全對上）

| 量 | 值 |
|---|---|
| `MotionDriver.moveSpeed`（執行期） | **6.2614**（`moveSpeedSource` ＝ `Bake_SprintFwdLoop`，`overrideMoveSpeed = 0` ⇒ 序列化的 5.66 是過期的編輯值） |
| `Bake_WalkFwdLoop.AutoAverageSpeed` | 1.6443 |
| `Bake_RunFwdLoop.AutoAverageSpeed` | 3.5781 |
| `Bake_SprintFwdLoop.AutoAverageSpeed` | 6.2614 |

驗證舊 1D 的 `_Speeds`：

- Walk：`0.35 × 6.2614 ÷ 1.6443 = 1.3328` ⇒ 資產值 **1.3327742** ✅
- Run：`0.75 × 6.2614 ÷ 3.5781 = 1.31246` ⇒ 資產值 **1.3124558** ✅

⇒ **公式確定**：`speed_i = 半徑_i × moveSpeed ÷ 天生速度_i`。

### 9.5.2 因此 §9.4 的「填回 1.3124558」是**錯的**

那個值是為**半徑 0.75** 算的。`RunFwdLoop` 在新的 2D 裡被放到**半徑 1.0**，正確值是
`1.0 × 6.2614 ÷ 3.5781 = **1.75**`。

**但填 1.75 也不該做**——把跑步循環加速 75% 播放，看起來就是「腳在亂踩」。
**這個數字本身就是結構錯誤的證據。**

### 9.5.3 真正的問題：**一個環撐不住整個速度域**

舊 1D 用**三支不同天生速度的 clip**覆蓋 0→6.26 m/s（1.64／3.58／6.26，各自只需 ~1.3 倍的小修正）。
新 2D 把**同一組 Run clip**放在半徑 1.0 的單一環上，於是那個環必須同時代表「站著」到「衝刺」：

| 做法 | 後果 |
|---|---|
| 半徑 1.0 ＋ `_Speeds ≈ 1.75` | 全速時跑步循環快轉 75%；半推搖桿時是 idle 與「快轉跑步」各半 ⇒ **就是回報的『非常詭異』** |
| 半徑 0.5715（＝3.5781÷6.2614）＋ `_Speeds = 1` | 動畫原速正確，但**環外沒有 clip**：超過 3.58 m/s 只能外插 ⇒ 滑步 |

⇒ **兩個選項都不對，因為問題不在數字。**

### 9.5.4 ~~建議形狀~~ → 🔵 **一個待驗假說，不是方案**（2026-09-07 使用者退回）

> 🔴 **本節已降級。** 使用者裁決：**現在資訊不足以做 gameplay 決策**——
> 8 向資產還沒 bake、沒校準、沒接成可 Play 的 2D mixer，此時要求裁決
> 「Combat 是否限速在 Run」「是否雙 mixer」「是否允許外環外插」，
> **等於用公式推導取代實測**。
>
> ⇒ 下文保留為**假說 H1**（值得驗，但**不得**當成既定方案引用）。
> **正確的下一步是 §10 的資產驗證與 prototype**，實測完才有資格談上面那三個問題。
>
> ⚠️ 特別是「戰鬥中限速在 run 階」——那是 **gameplay policy**，
> 有可能真正的解只是 **animation presentation 分層**，而**速度根本不需要被限制**。
> 兩者在畫面上可能很像，但架構後果完全不同。**沒有實測就分不出來。**

#### 假說 H1（待驗）：依戰鬥語境切換兩個 mixer

- **非戰鬥** → **維持現有的 1D**（walk／run／sprint 三階，**已經校準好、本來就正常**）
- **戰鬥中** → **8 向 2D，環放在 run 階（半徑 ≈ 0.5715、`_Speeds` 保持 1）**，
  並把戰鬥中的移動強度**上限夾在 run 階**（戰鬥中不衝刺——這是動作遊戲的常規，也讓單環在定義上正確）

**為什麼這個形狀對**：
1. **不需要新素材**——站立 8 向 walk clip 本來就不存在（`docs/10` §8 已盤點過），硬做多環會卡在這裡
2. **不需要 27 個取樣點**，兩個資產各自單純
3. **切換訊號已經存在**：`CombatContext.InCombat` 是黑板欄位（ADR-007 D3 補充條款），
   `LocomotionModel` 本來就讀黑板、本來就決定 `locomotionAnimationKey` ⇒ **加一個二選一，不是新機制**
4. 這正面回答了 `docs/13` §5.5 的「Combat Idle／Move 是不是 Presentation 差異」——**是資料差異**，
   而且**不需要** ADR-006 的分層就能成立（分層解的是「上下半身不同動作」，這裡解的是「整套 locomotion 換一份」）

⚠️ **速度上限夾在 run 階屬 gameplay 決策**（戰鬥中要不要能衝刺），**需要使用者裁決**，不由實作決定。

### 9.5.5 驗收機制也跟著更正

| 測項 | 變更 |
|---|---|
| **L-1**（匯入 SOP 7 欄） | ✅ **保留為硬斷言**——`.meta` 設定是 §0.4 的真不變量，現在就可檢、現在就該修 |
| **L-2** | 🔄 **從斷言改為報表（永遠通過）**。原本斷言「每支 mixer clip 都要有 Bake」是**發明規則**：Bake 是**量測工具**，不是 mixer child 的架構前提。改為印出「clip／半徑／天生速度／現在的 `_Speeds`／建議 A／建議 B」，**省掉人工算術** |
| **L-3** | 🗑️ **移除**。它斷言「`_Speeds` 全 1 ＝ 校準遺失」，但若取樣點正好落在天生速度環上，**1 才是對的**（見 9.5.3 選項二）。把可能正確的狀態斷言成錯誤，只會逼出「為了轉綠亂填數字」 |

📌 **教訓**：`docs/12` 的 Verification Ladder 是「能自動驗的就自動驗」，**不是「把還沒決定的事寫成測試」**。
L-2／L-3 驗的是一個**形狀還沒定案**的資產——那不是驗收，是把未定案凍結成規則。

---

## 10. 8 向資產驗證與 prototype（工作順序，2026-09-07 使用者裁決）

> **原則**：先確認**資產能做到什麼**，再談 gameplay 裁決。
> ⛔ 本節進行期間**不得**新增針對「尚未定案 shape」的硬測試。

### 10.0 先恢復可玩狀態（**第一件事**）

`Locomotion.asset` 已被就地改成 2D ⇒ **舊的 1D 三階 mixer 現在不存在了**，這是目前 Play 詭異的一部分。
它在 git 裡是完好的（未 commit 的 `M`）：

```
git checkout -- "Assets/ScriptableObjects/Animation/Locomotion.asset"
```

⇒ **1D 立刻恢復正常**，然後 **2D 走一份全新的 prototype 資產**（§10.3），
兩者互不干擾、隨時可切回。⚠️ Git 由使用者執行（CLAUDE.md）。

### 10.1 步驟 1：盤點（**離線已完成的部分**）

| clip | 來源 FBX | 影格 | 時長@30fps | 匯入設定符合 §0.4？ | 已有 Bake？ |
|---|---|---|---|---|---|
| `RunFwdLoop` | MovementAnimsetPro | 0–23 | 0.7667s | ✅ 是 | ✅ `Bake_RunFwdLoop`（native **3.5781** m/s） |
| `RunBwdLoop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ 4 欄不符 | ❌ |
| `RunLtLoop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |
| `RunRtLoop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |
| `RunStrafeLeft45Loop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |
| `RunStrafeRight45Loop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |
| `RunStrafeLeft135Loop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |
| `RunStrafeRight135Loop` | RunStrafeUpdate | 0–23 | 0.7667s | ❌ | ❌ |

**🎉 離線就量到的一個好消息**：**8 個方向的循環長度完全一致**（24 影格／0.7667 秒）。
⇒ 「能不能共用一個 ring」的疑慮**不包含 cadence 不匹配**，只剩**位移距離（native speed）的差異**——
而那正是 Bake 要量的東西。

⚠️ **另外兩支同名近似的變體不要混進來**：`StrafeRight45Loop`／`StrafeLeft135Loop` 是 **0–30 影格（31 格）**，
cadence 與上面 8 支**不同** ⇒ 放同一個 ring 會直接造成步頻打架。`docs/13` §2.1 稱它們「追加變體」，原因在此。

**還需要 Editor 才能完成的部分**：native speed（要 Bake）＋ 匯入設定修正（要 SOP）。

### 10.2 步驟 2：報表（工具已就緒）

`LocomotionMixerWiringTests.L2_Report_StrideCalibrationTable`（**永遠通過，只印表**）在 Bake 完成後會印出：

`clip │ threshold │ 半徑 │ native speed │ 現在的 _Speeds │ 建議 A（改 _Speeds） │ 建議 B（改半徑）`

**要從報表讀出的結論**：
1. 八支之間的 **native speed 落差**（最大／最小比值）
2. 落差大到什麼程度就**不能共用同一 ring** —— 判準建議：
   若必要的 playback scale 彼此差距 **> 約 15%**，同一環上就會出現「有些方向踩得對、有些明顯快或慢」
3. 若落差小 ⇒ **單環是可行的**，H1 的「雙 mixer」就不必要

### 10.3 步驟 3：最小 prototype（**不取代現有 1D**）

- 建立**新資產** `Locomotion_2D_Prototype.asset`（`MixerTransition2D`／Cartesian），**不要**再就地改 `Locomotion.asset`
- 切換方式：把 `PlayerStateMachineConfig` 的 Locomotion 動畫鍵**暫時**指向 prototype；驗完指回去。**一個欄位、隨時可逆**
- ⛔ **不綁 `CombatContext`**、⛔ **不改 gameplay 速度政策**、⛔ **不動 `LocomotionModel`**
- 程式面**零改動**：`MoveX`／`MoveZ` 參數已經在發布（S4a），prototype 直接就能吃

### 10.4 步驟 4：Play 觀察清單（**只觀察，不下結論**）

| # | 觀察項 |
|---|---|
| 1 | Forward／Back／Left／Right 四正向的方向是否正確 |
| 2 | 四個斜角是否正確（不是左右顛倒／不是 45 與 135 互換） |
| 3 | **小輸入時步頻**是否怪（環內插值靠近 idle 的區段） |
| 4 | **run 速度附近**是否自然 |
| 5 | **sprint 速度**時滑步有多嚴重 |
| 6 | 各方向之間是否有明顯的**速度／姿勢不一致** |

### 10.5 步驟 5：實測完才回答的問題（**現在不裁決**）

- 單一 2D mixer 是否可接受
- 是否真的需要 Free 1D ＋ Combat 2D（＝假說 H1）
- Combat 是否真的需要 run speed cap
- **或者其實只是 animation presentation 需要分層，gameplay 速度根本不必限制**

---

### 10.6 四個分類（使用者要求釐清）

#### ① Bake 是為了量測／校準哪些資料

| Bake 欄位 | 用途 | 8 向 loop 需要嗎 |
|---|---|---|
| `SpeedCurve` → `GetRepresentativeSpeed()`／`AutoAverageSpeed` | **clip 的天生水平速度** | ✅ **這次唯一真正需要的** |
| `FootPhaseCurve`／`EndPhase` | 收步選擇（`LocomotionStopSelector`）、腳步音相位 | 🟡 之後若要做各方向收步才需要 |
| `RotationCurve`／`RotationFinishedTime` | 曲線驅動的 yaw（原地轉身、Roll） | ❌ |
| `AutoTakeoffDelay`／`AutoApexHeight`／`AutoAirTime`／`AutoCalculatedGravity` | Jump 物理（ADR-002） | ❌ |
| `TargetLocalDirection`／`BakedDuration` | Warp 補償、曲線時間軸 | ❌ |

#### ② 哪些 clip **不需要 Bake** 也能當 mixer child

**只被 blend 播放的 clip，執行期完全不需要 Bake。** 需要 Bake 的只有三類：
1. `MotionDriver.moveSpeedSource`（目前＝`Bake_SprintFwdLoop`）
2. 走 `ExecuteBakedCurveMovement` 的 clip（Roll、收步、原地轉身）
3. 需要腳相的 clip（收步選擇、footstep）

⇒ **8 向 loop 屬於「不需要」那一類。** 這次要烘**純粹是為了量測**（要知道 native speed 才知道環放哪），
**不是執行期依賴** —— 這正是 L-2 不該寫成硬斷言的理由。

#### ③ 現在就成立的 invariant（可以硬斷言）

- **L-1**：匯入設定符合 dev-spec §0.4 —— 有明文規範、與 mixer 形狀無關、現在就可檢可修
- mixer 引用的 clip 必須存在且非 null（結構完整性）
- 既有的 A1–A31 不受本輪影響

#### ④ 只能在 prototype 之後才有資格斷言

- ring 半徑、`_Speeds` 數值、是否需要多環
- 「8 支能否共用一個 ring」——**要等速度差數據**
- 雙 mixer、combat speed cap、外環外插策略
- 「同一 ring 的 clip cadence 必須一致」——**目前是報表項，不是斷言**：它是「同一組動畫集」的性質，不是架構規則

---

## 10.7 量測結果（2026-09-07，Unity batchmode 實跑）

**執行方式**：`Unity.exe -batchmode -executeMethod Project.Editor.AnimationBatchOps.ApplyStrafeSopAndBake`
⇒ 7 支套 **Locomotion-位移** preset ＋ 以 **60 FPS** 批次烘焙。exit code 0。

### 10.7.1 native speed（**本輪的核心數據**）

| 方向 | clip | native speed (m/s) | 相對前進 |
|---|---|---|---|
| 前 | `RunFwdLoop`（既有） | **3.5781** | 1.000 |
| 前斜 45° 左／右 | `RunStrafeLeft45Loop`／`RunStrafeRight45Loop` | **3.6217** | 1.012 |
| 右 | `RunRtLoop` | **2.2654** | 0.633 |
| 後 | `RunBwdLoop` | **2.2136** | 0.619 |
| 後斜 135° 左／右 | `RunStrafeLeft135Loop`／`RunStrafeRight135Loop` | **2.2136** | 0.619 |
| 左 | `RunLtLoop` | **2.1647** | 0.605 |

（全部 `BakedDuration = 0.7666667`，與離線量到的 24 影格一致。）

### 10.7.2 結論：**這組資產有兩個速度群，不是一個**

| 群 | 成員 | 速度範圍 | 群內差異 |
|---|---|---|---|
| **快群** | 前 ＋ 前斜 45° | 3.578 – 3.622 | **1.2%** |
| **慢群** | 左／右／後 ＋ 後斜 135° | 2.165 – 2.265 | **4.6%** |
| **群間** | — | — | **1.60 – 1.67×** |

📌 **群內差異都小於 5%** ⇒ **同一群共用一個環在數據上完全成立**。
🔴 **群間差 1.6 倍** ⇒ **單一均勻環不可能同時對兩群**：要嘛慢群被加速約 1.6×（側移／後退腿部明顯快轉），
要嘛快群被放慢。

### 10.7.3 這個數字是**設計**，不是瑕疵

側移／後退 ≈ 前進的 **60–63%** —— 這正是動作遊戲常見的方向性速度配比。
Kubold 這組素材**本來就把「人往側面與後面移動比較慢」編進動畫裡**。

⇒ 因此「單環是否可接受」實際上是在問：**gameplay 的速度要不要跟著方向變？**

| 形狀 | 對動畫 | 對 gameplay |
|---|---|---|
| **統一世界速度 ＋ per-clip `_Speeds`** | 慢群 `_Speeds` ≈ **1.58–1.65**（腿部快轉） | 不變 |
| **方向性速度 profile** | `_Speeds` 全 **1**（永遠原速） | **速度隨方向變**＝政策變更 |
| **折衷** | 環放兩群之間，兩邊各分攤誤差 | 不變 |

⚠️ **本檔不裁決。** 上表只是把量到的數字換算成三種可能，**視覺可接受度仍要 prototype ＋ Play 才有答案**
（`docs/13` §10.4 的六項觀察）。特別是「1.6× 快轉到底多難看」——那是看的問題，不是算的問題。

### 10.7.4 順帶排除一個嫌疑：**方向指派是對的**

以重匯入後的 `internalID` 反查現有 2D mixer 的取樣點：

`(0,-1)→RunBwdLoop`／`(-1,0)→RunLtLoop`／`(1,0)→RunRtLoop`／`(-0.7,0.7)→StrafeLeft45`／
`(0.7,0.7)→StrafeRight45`／`(-0.7,-0.7)→StrafeLeft135`／`(0.7,-0.7)→StrafeRight135`

**全部正確**（左右沒反、45 與 135 沒互換）⇒ 先前 Play 的詭異**不是方向接錯**，
而是 §9.4 的匯入設定 ＋ §9.5 的單環結構問題。

### 10.7.5 🔴 附帶發現：**Kubold 素材夾不在版本控制內**

`.gitignore:100` 有 `/[Aa]ssets/MovementAnimsetPro/` ⇒ **FBX 與其 `.meta` 都沒被 git 追蹤**。

| 後果 | 說明 |
|---|---|
| **匯入設定沒有 git 還原路徑** | 這次改的 7 支設定，**`git checkout` 救不回來**。所幸 SOP 是決定性的、可重跑 ⇒ 恢復容易 |
| **重新 clone 無法重現** | 新環境重下素材包 ＝ 原廠設定，**每支 clip 都要重套 SOP** |
| **推導資料反而有版控** | `Assets/ScriptableObjects/Motion/` 的 Bake 資產**有**被追蹤 ⇒ **量測結果進 repo、來源設定沒進** |

⇒ **`LocomotionMixerWiringTests.L-1` 因此比原本設想的更重要**：
它是專案裡**唯一**能發現「匯入設定被改壞或從未套用」的機制——git 在這裡幫不上忙。
📌 這也正是 dev-spec §4 藍圖中 **Source Discovery ／ Validation** 兩個階段存在的理由。
