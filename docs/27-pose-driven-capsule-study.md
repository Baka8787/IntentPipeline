# 27 — Pose-Driven Capsule Occupancy（研究輪，**非規格正本、未實作**）

> **狀態：研究與量測，⛔ 尚未實作、尚未裁決。**
> 觸發：使用者 2026-09-15 回報「動態碰撞箱好像沒用」，並提供外部參考畫面
> （主 capsule 明顯不固定在 root，隨 torso／head 佔用位置移動）。
> 相關：`docs/25` §3.3（W1 動態 Collider 規格）／`Presentation/Motion/CapsuleOffsetPolicy.cs`。
>
> ⚠️ 本檔的所有數字都是**實測**（preview scene 取樣 X Bot，32 samples／clip，
> 量核心骨骼相對 root 的位置）。⛔ 沒有一個數字是估的。

---

## 0. 先回答「開了沒」

**開了。** 場景實例實測 `capsuleOffset.Enabled = True`、`MaxOffset = 0.12`、
`overridesOnCapsuleOffset = 0`（沒有場景 override 蓋掉 prefab 值）。
`CharacterController`：**`radius = 0.32`**、`height = 1.827`、`center = (0, 0.913, 0)`、`skinWidth = 0.03`。

> 🔄 **2026-09-15 更正**：本檔原先全篇以 `radius = 0.22` 推算，那是錯的。
> 使用者裁決 **0.32 為 authored baseline**（prefab 值，`Y Bot` 亦同）；SampleScene 曾有一個
> 未 commit 的 `0.12` 實例 override，已於 2026-09-15 以 `PrefabUtility.RevertPropertyOverride` 清除。
> **下面所有含半徑的算術都已改用 0.32；若你看到 0.22，那是漏改，請回報。**

看不出效果有**兩個獨立原因**，下面 §1 與 §6 分別處理。

---

## 1. 現行模型 vs 參考效果：差在哪

### 1.1 現行模型

```csharp
// CapsuleOffsetSolver.SolveTargetOffset
planar = normalize(localMoveDirection.xz)
offset = planar * (MaxOffset * clamp01(speedNormalized) * turnWeight)
```

三個假設：**①方向 ＝ 移動方向**、**②量級 ∝ 正規化速度**、**③與姿勢無關**。
實測顯示**三個都不成立**。

### 1.2 ① 方向假設不成立

| clip | 移動方向（root-local） | 實測質心方向 | 夾角 |
|---|---|---|---|
| RunFwdLoop | (0, +1) | (0.09, 0.996) | ~5°（成立） |
| RunBwdLoop | (0, −1) | (0.28, −0.96) | ~16°（大致成立） |
| **StrafeLeftLoop** | **(−1, 0)** | **(0.64, −0.77)** | **~130°（方向幾乎相反）** |
| **RunStrafeLeft45** | (−0.71, +0.71) | (−0.41, 0.91) | ~21°（偏向 facing） |
| **RunStrafeRight135** | (+0.71, −0.71) | (0.92, −0.39) | ~22°（偏向側向） |

🔴 **純側移是最糟的情況**：模型要把膠囊往左推 0.12 m，而身體核心實際只偏了 0.028 m，
**而且是往右**。這不是調參數能解決的——模型對這支動畫的方向判斷本身就是錯的。

**為什麼**：strafe 動畫裡角色**面向不變、腳步橫跨**，軀幹柱幾乎不移動；
身體的傾斜是**動畫自己的力學**，不是世界移動方向的函數。

### 1.3 ② 量級假設不成立（而且不是線性）

`Locomotion.asset` 的 4-tier 正規化速度是 `0 / 0.265 / 0.574 / 1`。

| gait | 模型預測 `0.12 × speed` | 實測質心 Z | 誤差 |
|---|---|---|---|
| Walk（~0.265） | 0.032 | **0.000** | 模型**多推** 3.2 cm |
| Run（~0.574） | 0.069 | **0.161** | 模型**少推** 2.3× |
| Sprint（1.0） | 0.120 | **0.160** | 模型少推 25% |

⇒ 真實的傾斜是**隨 gait 階躍**的（走路完全不傾、跑起來就傾 16 cm），
不是隨速度連續成長。用一條線性關係去擬合，**必然在某一段是錯的**。

### 1.4 ③ 與姿勢無關 ⇒ 過渡與 committed 動作完全失守

| clip | 一個循環內質心 Z 的擺幅 |
|---|---|
| RunFwdLoop | 0.049 |
| RunFwdStop_RU | **0.240** |
| RunFwdTurn180 | **0.348** |
| Stand To Roll | **0.418** |
| Vault1m | **0.469** |
| Climb1m | **0.547** |

現行模型在這些動作期間一律 `locomotionHasAuthority = false` ⇒ **偏移歸零**。
也就是說：**身體位移最劇烈的時候，碰撞體完全不動。**

### 1.5 🔴 最重要的一條：質心對齊**也包不住頭**

使用者的原始症狀是「頭穿模」。但：

| | Sprint |
|---|---|
| 核心質心 Z | 0.160 |
| **頭部最前 Z** | **0.405** |
| 膠囊半徑 | **0.32** |

即使把膠囊中心移到核心質心（0.160），膠囊前緣也只到 `0.160 + 0.32 = 0.480`，
頭在 0.405 ⇒ **已經被包住，還有 7.5 cm 餘裕**。
（🔄 舊版以 r=0.22 算出「頭仍在外面 2.5 cm」，是錯的；但**不偏移**時膠囊前緣只到 0.32，
頭 0.405 ⇒ 仍超出 **8.5 cm**。⇒ 頭穿模在 r=0.32 下依然存在，只是量級從 18.5 cm 降到 8.5 cm。）
用 Walk 對照更清楚：頭只到 0.035，完全不是問題。

⇒ **「頭穿模」不是單一問題，是兩個問題疊加**：
① 膠囊沒跟著身體前移（本檔主題）；② **膠囊半徑本來就包不住衝刺時前伸的頭**。
⛔ 只做 pose-driven 不會完全解決症狀，必須同時決定「頭要不要被包進去」。

---

## 2. 參考畫面像什麼

⚠️ **先聲明不確定性**：只有兩張截圖、沒有原始碼，以下是**推測**，不得當成事實引用。

畫面上可辨識的：
- 角色身上有**多組** collider gizmo（頭部一顆黃色球、胸／髖一組綠色、以及一顆明顯較大的白色 capsule）。
- 白色 capsule **明顯不與 mesh 對齊**：第一張圖裡角色蹲伏貼牆、白色 capsule 直立在角色斜前方；
  第二張圖裡角色前傾衝刺、白色 capsule 位於頭／上半身那一側。

可能的兩種解釋：
1. **pose-driven 主 capsule**：白色是移動用 capsule，中心由軀幹／頭的佔用位置推導。
2. **多 collider 分工**：黃／綠是命中盒（ragdoll 或 hit capsule），白色是移動 capsule，
   而它的偏移可能來自**別的機制**（例如 root motion 與 capsule 的解耦、或 capsule 指向預測位置）。

📌 **但兩種解釋都指向同一個結論**：那個效果**不可能**由「移動方向 × 固定距離」產生——
第一張圖角色幾乎沒有水平移動速度（蹲伏貼牆），而 capsule 仍然大幅偏離 root。
**這正是 §1.3 的階躍現象：偏移來自姿勢，不是速度。**

⇒ 「更像 pose-driven / body-landmark-driven」這個判斷，**與我們自己的量測互相印證**，
不是被影片說服。

---

## 3. 少量核心骨骼估計是否比 per-animation bake 通用

**是，而且差距很大。**

實測已涵蓋 locomotion loop、過渡（Stop／Turn180）、committed（Roll／Punch／Throw）、
traversal（Vault／Climb）——**同一組 6 根骨骼、同一段程式**，全部都量得出合理的水平佔用。

| 做法 | 新增一支動畫的成本 | 動畫被換掉時 |
|---|---|---|
| per-animation bake collider curve | 必須烘焙 ＋ author ＋ 接線 | **靜默失效**（曲線還在，但對應的是舊動畫） |
| pose-driven | **零**（它讀的是當下的骨架） | 自動跟上 |

⇒ pose-driven 的通用性優勢**不是「比較聰明」，是「沒有需要同步的第二份資料」**。
這與本 repo 反覆出現的加重缺陷（下游重新推導／第二份真相）是同一條原則。

### 3.1 但它有一個 bake 沒有的新成本：**步頻抖動**

| clip | 循環內質心擺幅 Z |
|---|---|
| RunFwdLoop | 0.049 |
| SprintFwdLoop | **0.101** |

⇒ pose-driven 的 desired offset 會**隨每一步前後擺動 ±2.5–5 cm**。
碰撞體每步抖 5 cm 是可感知的（尤其貼牆時）。
**必須低通濾波**，而濾波時間常數又直接吃掉反應速度——這是 bake 曲線沒有的取捨。
📌 現行的 `SmoothTime = 0.12` 大致可以壓掉 stride 頻率（run 週期 0.767 s），但要實測確認。

---

## 4. 哪些骨骼該取樣

### 4.1 應納入：軀幹柱

`Hips`／`Spine`／`Spine1`／`Spine2`／`Neck`／`Head`。
本檔所有數字都是這 6 根的等權質心。

### 4.2 ⛔ 不應納入：四肢

實測（Sprint，所有骨骼 vs 核心骨骼）：

| | 所有 mixamorig 骨骼 | 核心 6 根 |
|---|---|---|
| 最大側向 X | **0.356** | 0.054（擺幅） |
| 最大前向 Z | **0.679** | 0.211 |

手腳擺到身前 0.68 m、側向 0.36 m。**膠囊本來就不該包住揮出去的手腳**——
包了會讓角色在揮拳時卡住門框。

### 4.3 ⚠️ `Head` 要不要算、以及**不要用 max**

- 納入 `Head` 會把質心往前拉（對「頭穿模」有利），但也會讓**抬頭／低頭**影響碰撞體。
- 🔴 **⛔ 不要用「核心骨骼的最大水平外擴點」當目標**。那是 `max()` 而不是加權平均，
  是一個**不連續的選擇器**：argmax 從 `Head` 跳到 `Spine2` 的那一幀，offset 會跳變。
  本 repo 已經有這一類缺陷的紀錄（memory `snap-to-default-defect-class`：
  「不連續選擇餵給連續量」）。**連續的加權質心才是對的形狀。**
- 若要讓頭有更大權重，正確做法是**調權重**（連續），不是改用 max（不連續）。

### 4.4 成本

每幀 3–6 次 `Transform.position` 讀取 ＋ `InverseTransformPoint`。
**零配置**，數量級遠低於現有的 Foot IK raycast。不是效能問題。

### 4.5 時序可行性 ✅

`UpdateCapsuleOffset` 由 `ExecuteBaseMovement` 在**管線順序 6（LateUpdate）**呼叫，
而 Animator 的評估發生在 Update 與 LateUpdate **之間**
⇒ **讀到的骨架就是本幀已經擺好的姿勢**，不需要任何新的時序安排。
（Foot IK 在順序 6.5 才改骨骼 ⇒ 我們讀到的是 pre-IK 姿勢，對軀幹而言正確。）

---

## 5. 與既有 authority 共存

### 5.1 ⭐ pose-driven 讓 `locomotionHasAuthority` 這個參數**變得多餘**

現行程式必須知道「誰在驅動位移」，因為它要讀 `MoveDirection`——
而 `MoveDirection` 在 Action／Roll／Traversal 期間**不是位移真相**（`docs/25` §1.4）。

pose-driven **讀的是結果不是原因**：不論誰在驅動，骨架就是骨架。
⇒ 概念上簡化一層。這是 B 案被低估的好處。

### 5.2 但仍有兩個必須排除的情境

| 情境 | 為什麼仍要排除 | 依據 |
|---|---|---|
| **Traversal committed** | `TraversalProbe` **逐幀讀** `center`／`radius`／`height` 做 ledge 偵測與 `EntryCapsuleWallClearance` ⇒ 動態 center **直接改變起攀合法性**。而 `docs/24` §19 記載那條合法帶已只剩約 20 cm 寬 | `docs/25` 副作用表 ＋ `docs/24` §19 |
| **Traversal collision profile 生效期間** | 它是 `center`／`height`／`radius` 的**另一個寫入者**。同一欄位兩個寫入者 | 現行 guard ①（`C10` 釘住） |

⇒ 這兩條**與 pose-driven 無關，是既有結構事實**，A／B／C 三案都要保留。

### 5.3 Roll／Action 反而應該**放行**

實測 Roll 的質心擺幅 0.418 m——那正是碰撞體最該跟上的時候。
現行模型在 Roll 期間歸零，是因為 `MoveDirection` 不可信；
pose-driven 沒有這個限制。**這是 B 案帶來的實際行為改善，不只是重構。**

---

## 6. `CharacterController.center` 不做 sweep 的正解

### 6.1 現行做法為什麼必須換掉

```csharp
if (!grounded || committedMotion || touchingWall) return Vector3.zero;
```

`touchingWall` 來自上一次 `Move` 的 `CollisionFlags.Sides`。
⇒ **貼牆那一刻偏移歸零**，而那正是穿模發生的時刻。
**這個 guard 讓整個功能只在不需要的時候生效。** 它是使用者回報「好像沒用」的主因。

⚠️ 但 guard 想防的風險是**真的**：`center` 賦值不做 sweep，會把膠囊直接搬進幾何體內，
下一次 `Move` 才暴力彈出。不能直接刪掉。

### 6.2 collision-aware clamp（建議）

把「要不要偏移」換成「**能偏多少**」：

```
p1, p2 = 目前 center 對應的兩顆球心（世界座標）
delta  = 目標 center − 目前 center（水平）
if (Physics.CapsuleCast(p1, p2, radius, delta.normalized, out hit, delta.magnitude, mask, Ignore))
    allowed = max(0, hit.distance - skinWidth)
else
    allowed = delta.magnitude
center = 目前 center + delta.normalized * allowed
```

| 性質 | 說明 |
|---|---|
| **零配置** | `Physics.CapsuleCast` 的 `out RaycastHit` 多載不配置 |
| **成本** | 每角色每幀一次 capsule cast。與 Foot IK 的 raycast 同數量級 |
| **自我排除** | 用 layer mask 排除角色自身層（`QueryTriggerInteraction.Ignore`） |
| **語意正確** | 它回答的正是 guard 想問的問題：「這段移動會不會撞進東西」 |
| **不再有懸崖** | 貼牆時偏移**被夾到剛好貼著牆**，不是歸零 ⇒ 前方覆蓋保留 |

📌 **殘留風險**：cast 只處理「從現在移到目標」這段；若角色**已經**與幾何體重疊
（被推擠、傳送、場景變動），cast 起點就在體內。
補救是 `Physics.ComputePenetration` 做一次去穿透——但那是第二階段，
**v1 不必做**（現行程式同樣沒有處理這個情況，不是新增的缺口）。

### 6.3 兩道仍要保留的 guard

`grounded`（空中沒有可信的佔用語意）與 traversal 互斥（§5.2）**保留**。
只有 `touchingWall → zero` 這一條被 collision-aware clamp 取代。

---

## 7. per-animation bake collision profile 還需要嗎

**需要，但職責要收窄。**

| 用途 | pose-driven 能不能取代 | 結論 |
|---|---|---|
| 「碰撞體跟著身體走」（Roll／Stop／Turn） | ✅ 能，而且免維護 | **交給 pose-driven**，不要 author 曲線 |
| 「碰撞體**刻意不同於**姿勢」 | ❌ 不能——pose-driven 只會忠實跟隨 | **保留 profile** |

第二類的實例（現行 `TraversalCollisionProfile` 正在做的事）：
- **Vault 期間縮小 height／radius** 好穿過欄杆——那是「刻意讓碰撞體比身體小」，姿勢推不出來。
- 未來的無敵幀／閃避：刻意讓碰撞體**不跟**身體。

⇒ 建議把 profile 的定位從「**per-animation 碰撞曲線**」改寫為
「**刻意偏離姿勢時的 override**」。這樣它從「每支動畫都要填」的維護負債，
變成「只有特例才填」的例外機制——**數量級地變小**。

---

## 8. 三案比較

| | **A：維持 MoveDirection，只修 wall clamp** | **B：Pose-driven ＋ wall clamp** | **C：MoveDirection baseline ＋ pose 修正** |
|---|---|---|---|
| 修掉「貼牆歸零」 | ✅ | ✅ | ✅ |
| 純側移方向正確 | ❌ 仍然反向推 0.12 m | ✅ | ⚠️ 取決於修正權重，兩個來源打架 |
| Walk 不誤推 | ❌ 仍推 3.2 cm | ✅ | ⚠️ |
| Run／Sprint 量級 | ❌ 少 2.3× | ✅ | ⚠️ |
| Roll／Stop／Turn 跟得上 | ❌ 一律歸零 | ✅ | ❌（baseline 在這些狀態沒有真相） |
| 新增接線 | 無 | 3–6 根骨骼引用（可用既有的名稱查找 pattern） | 同 B |
| 求解層可測性 | 維持純函式 | **維持純函式**（改吃骨骼 local 座標） | 純函式但**輸入變兩組**，測試組合數上升 |
| 新增每幀成本 | 一次 capsule cast | 一次 capsule cast ＋ 6 次 transform 讀取 | 同 B |
| 調參維度 | 1（MaxOffset） | 1–2（權重、平滑） | **≥3，且兩個來源會互相補償** |
| 真相來源 | 一個 | 一個 | 🔴 **兩個** |

### 8.1 ⛔ 為什麼否決 C

看起來「穩健」，實際上是本 repo review protocol 明文點名的加重項：**單一真相來源**。

具體會怎麼壞：baseline 說往左 0.12、pose 說往右 0.03，最後的數字是兩者的和，
**它既不是移動方向也不是身體位置**。調參時你無法回答「這個偏移是誰造成的」——
而這正是 `docs/24` §19 記載的那種「越改越差、又找不到是哪一層」的處境。

📌 hybrid 只有在**兩個來源回答不同問題**時才合理（例如 baseline 管方向、修正管量級）。
這裡兩者回答的是**同一個問題**，所以它不是 hybrid，是兩個競爭的答案。

### 8.2 ✅ 推薦：**分兩步，先 wall clamp，再 B**

**第一步（獨立成立，建議先做）：§6.2 的 collision-aware clamp。**
- 它在 A／B／C 三案都需要，**不會白做**。
- 它單獨就能解決使用者回報的主要症狀（貼牆時偏移消失）。
- 風險低、可獨立驗收：clamp 前後的行為差異只在貼牆時。
- 做完就能 Play 看看「只修這個夠不夠」——**如果夠，B 就可以不做**。

**第二步：B（pose-driven）。**
- 依據是 §1 的量測：現行模型在**三個假設上都被實測推翻**，不是調參數的問題。
- 求解層**仍然是純函式** ⇒ 現有 `C1`–`C9` 的測試形狀完全沿用，只換輸入。
- 它同時讓 `locomotionHasAuthority` 參數消失（§5.1），是**淨簡化**而不是加層。

**⚠️ 但 B 不解決「頭穿模」的全部**（§1.5）：衝刺時頭在質心前 0.245 m，
而膠囊半徑是 0.32（🔄 2026-09-15 更正，原寫 0.22）。要真的包住頭，還需要**獨立決定**：加大半徑、
或加重 `Head` 權重、或接受頭在外面。**這是一個需要裁決的取捨，不是實作細節。**

### 8.3 不建議現在做的

- ⛔ 多 collider（參考畫面可能是這個）——那是另一個量級的改動，
  且本專案的移動、traversal、Foot IK 全部建立在「一顆 CharacterController」上。
- ⛔ `Physics.ComputePenetration` 去穿透（§6.2 殘留風險）——現行程式同樣沒做，
  不是本輪新增的缺口，等真的遇到再說。

---

## 9.5 🔴 實作結果（2026-09-15）：clamp 已做，**Player integration 暫停**

> 使用者裁決：先做 §6.2 的 collision-aware clamp、不切 pose-driven。做完 Play 立刻撞到 traversal。

### 9.5.1 已落地

| | 內容 |
|---|---|
| **求解層** | `SolveTargetOffset` → **`SolveDesiredOffset`**，`touchingWall` 參數**移除** ⇒ 完全不認識幾何體，維持純函式 |
| **夾持** | `MotionDriver.ClampCapsuleOffsetAgainstGeometry`：從 authored 基準 center 掃一次 `Physics.CapsuleCast`，沿**同一方向**縮到 `hit.distance`。無狀態、零配置、每角色每幀一次 |
| **cast 半徑** | 刻意縮一個 `skinWidth`（0.32 → 0.29；🔄 2026-09-15 更正，原寫 0.22 → 0.19）。`CharacterController` 本來就允許 skinWidth 互穿；用原半徑掃，角色貼牆站定時**起點就已重疊** ⇒ cast 回 `distance = 0` ⇒ 又變回舊行為 |
| **排除自己** | `~(1 << gameObject.layer)` ＋ `QueryTriggerInteraction.Ignore`。Player=6／Enemy=7 分屬不同 layer ⇒ 玩家仍會被敵人夾住；近戰 hitbox 是 trigger ⇒ 自動排除 |
| **Gizmo** | 三層圓環：灰＝基準／黃＝Desired／綠＝Safe／洋紅＝Current。**黃≠綠＝牆吃掉多少；綠≠洋紅＝平滑滯後** |
| **測試** | `C6` 移除貼牆那一半；新增 `C13`（反向護欄：求解層不得再因貼牆歸零）、`C14`（夾持算術）、PlayMode `CO3`（真的蓋一面牆） |

### 9.5.2 🔴 Player integration 暫停 —— traversal geometry conflict

**Play 實測：按著 W 完全無法起攀。** 機制已確認，不是猜測：
> 🔄 **2026-09-15 全段更正**：下表原以 `r = 0.22` 寫成（0.22 → 0.34），而真實 authored 值是 **0.32**。
> 更重要的是**整段的歸因已被 §10 第 0 條的 PlayMode 探針推翻**——失敗與站位無關。
> 數字保留更正後版本，**但不要再引用本段的因果解釋**。

| | 站位（root 到牆面，含 `skinWidth`） |
|---|---|
| 偏移關閉 | `r + skin` ＝ 0.32 + 0.03 ＝ **0.35 m** |
| 偏移開啟（滿值） | `r + skin + 0.12` ＝ **0.47 m** |

⇒ 角色停在比原本遠 0.12 m 的位置。~~而 `docs/24` §19 記載可執行起攀站位帶**只剩約 20 cm 寬**
⇒ **直接吃掉 60%**。~~ 🔴 **這句是錯的**：那個 20 cm 是 `docs/24` §9.3 的 **Vault1m 舊值**
（`ForwardScanDistance` 還是 1.25 m 的時代），§10 工作包一已把它改成 0.70 m 寬；
Climb 路徑的實錄合法帶是 **[0.06, 0.81] m**，0.47 仍在帶內。**見 §10 第 0 條。**

📌 **clamp 在這裡確實不會介入**（這一句仍然成立）：cast 半徑是 `r − skin` = 0.29，
只有在 root 距牆 < `0.12 + 0.29 = 0.41 m` 時才掃得到牆，但角色在 0.47 m 就被
`CharacterController` 擋下了 ⇒ 偏移一路保持滿值。
⚠️ 但**這不是失敗的原因**——探針已證明同一個站位不偏移 center 就能起攀。
⇒ **這不是 clamp 沒做對，是「膠囊變大」與「起攀要夠近」本質衝突。**

⚠️ 另外觀察到「手穿模進牆」：起攀不觸發 ⇒ 跑步動畫繼續對著牆播 ⇒ 手臂揮進牆。
**手臂本來就從來不在膠囊內**（§4.2 實測：衝刺時手伸到身前 0.68 m，半徑 0.32）
——這是既有現象，只是現在因為起攀不接手而持續可見。

### 9.5.3 ✅ 裁決（2026-09-15 使用者）

> **Dynamic capsule 本身沒有被否決。暫停的是 Player integration，理由是 traversal geometry conflict。**

- `X Bot.capsuleOffset.Enabled = **false**`
- `Y Bot.capsuleOffset.Enabled = **true**`（敵人沒有 `TraversalProbe`，不受影響，可繼續觀察效果）
- **Traversal 維持 frozen** ——⛔ 不為了 capsule offset 去改 `TraversalProbe` ／ entry policy
  （`docs/24` §19：該子系統沒有任何 git 還原點，且已有「越改越差又找不到是哪一層」的紀錄）
- ⛔ **不刪除** solver／tests／gizmo。它們是完好的、有測試的、可隨時重新啟用的
- `MaxOffset` **未動**（0.12）

**重新啟用的前提**（任一）：
① traversal entry band 變寬到能吸收 0.12 m（那是 traversal 自己的工作，`docs/24` §10–§11 已指出
`ForwardScanDistance 1.25 m` < 理想進場距離 1.402 m 才是真正的瓶頸）；
② 改走 pose-driven（§8.2 第二步）並確認它在貼牆時的站位影響較小；
③ 為玩家單獨調低 `MaxOffset` 到 traversal 容忍得了的量。

---

## 9.6 Debug 可視化盤點（2026-09-15）

| 頻道 | 現況 |
|---|---|
| **Capsule Offset** | ✅ 三層圓環。🔄 本輪改吃**獨立顯示開關** `drawCapsuleOffsetDebug`，⛔ 不再用 `Enabled && isPlaying`——功能關掉時同樣需要看得到「基準膠囊在哪、為什麼沒有偏移」 |
| **Foot IK** | ✅ 既有 `drawFootIKRuntimeLines`／`drawFootIKSceneGizmos` |
| **Traversal Runtime Lines** | ✅ 既有 |
| **Traversal Scene Gizmos** | ✅ 既有（`#if UNITY_EDITOR`） |
| **Traversal Panel Text** | ✅ 既有 |
| **Direction Authority** | 🔴 **快照有記錄，但沒有任何繪製實作**。`CharacterFacingSource` 內零 `OnDrawGizmos`／`Debug.DrawLine`／`Handles`——`docs/18` §1 刻意收斂掉了 |
| **Ground Probe ／ Ground Normal** | 🔴 **沒有東西可畫**，見 §9.7 |

⇒ `App/RuntimeDebugPanel`（F1 開關）把可控的五項收攏成一個執行期面板。
**它是遙控器不是感測器**：只翻既有元件上的 `bool`，⛔ 不計算、不打 ray、不讀黑板、不持有狀態。
把 Panel 刪掉，那些旗標維持原樣。
不可用的兩項**也列在面板上並附理由**——空白會被讀成「還沒做」，於是每隔一陣子就有人重新調查一次
（同 `docs/22` §H-1 ⑤：「什麼都不畫」會被誤讀成「沒資料」）。

⚠️ Panel 用 IMGUI，每幀配置字串 ⇒ 這是零 GC 紀律的**明確例外**，成立條件有二：
①只在面板開著時發生；②整段包在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 內，release build 不存在。
⛔ 不得把這個藉口套用到 gameplay。

## 9.7 🔴 Grounded 的可觀測性（2026-09-15 使用者裁決，**不做**）

> **`Grounded source = Unity CharacterController internal grounding; hit point / ground normal currently unobservable.`**

`IsGrounded` 來自 `characterController.isGrounded`——Unity `CharacterController` **內部的 sweep**。
我們沒有自己的 ray、沒有 hit point，黑板也沒有 ground normal
（`docs/17` §3.5 另有明文：⛔ 不得因為「想在 gizmo 裡看見它」就把 normal 加進黑板）。

⛔ **不得為了 debug 自己打一條 ray／sphere cast 再把它畫成 Ground Probe。**
那條線畫的**不是決定 `IsGrounded` 的權威來源**，而是另外算的一條
⇒ 同時違反 `docs/17` 紅線 1（debug 不得成為第二個決策來源）與
`docs/18` §1 的「**記錄，不要重算**」。
⛔ 也不得為了可視化去改寫現有接地判定。

**未來的正路**：若 slope／sliding／gameplay **本身**需要自有 ground sensor，
就正式建立一個 `GroundProbe`（那是 gameplay 改動，要走 ADR 判準），
**Debug 層屆時只讀它的結果**。⇒ 順序是「先有權威，再有可視化」，不是反過來。

---

## 10. 裁決紀錄與仍然開著的問題

### ✅ 已裁決（2026-09-15）

| # | 問題 | 裁決 |
|---|---|---|
| 1 | 先做 collision-aware clamp 嗎？ | **是**，已落地（§9.5.1） |
| 2 | Player 要不要繼續開？ | **暫停**（§9.5.3）——`X Bot` 關、`Y Bot` 開。dynamic capsule 本身沒被否決 |
| 3 | 統一 Debug Panel？ | **做**，只控制既有可視化（§9.6） |
| 4 | Ground Probe ／ Normal？ | **不做**（§9.7）。先有權威，再有可視化 |

> ✅ **人類 Play 驗收（2026-09-15 使用者）：通過，無問題。**
> 驗的是**上表第 2、3 項生效後的狀態**——`X Bot` 偏移關閉、`Y Bot` 開啟、Debug Panel 五頻道可讀。
> ⚠️ 這條驗收**不代表** §9.5.2 的 traversal 幾何衝突已解；它證明的是
> 「暫停 Player integration 之後遊戲回到可玩狀態」，衝突本身仍列在下方「仍然開著」。

### ⛔ 仍然開著

> ## 🔴 0. **先解這一條：§9.5.2 的歸因與程式對不上**（2026-09-15 後續，**已由 PlayMode 探針證實**，見本條末的結果表）
>
> §9.5.2 把「按著 W 無法起攀」歸因於**站位退後 0.12 m 吃掉 entry 合法帶**。
> 但把數字對回 `docs/24` §19 記錄的**實際**合法帶之後，這個歸因**推不出失敗**：
>
> | | 值 | 出處 |
> |---|---|---|
> | Climb2m `desiredWallDistance` | **0.462 m** | `docs/24` §19.3（離線重建用的理想站位） |
> | entry 合法帶（root→牆） | **[0.06, 0.81] m** | `docs/24` §19.2 面板實錄 `legal 0.06–0.81`；＝ 0.462 −`MaximumCloseError 0.40` ／ ＋`MaximumFarError 0.35` |
> | 偏移關閉時實際站位 | 0.17–0.23 m | `docs/24` §19.3 觀察 B |
> | 偏移開啟時實際站位 | ~0.34 m | §9.5.2 |
>
> ⇒ **0.34 不但在帶內，而且比 0.22 更靠近理想的 0.462**
> （`longitudinalError` 從 −0.24 變成 −0.12）。單就 `TraversalEntryPolicy.Evaluate` 的距離判斷，
> 偏移開啟應該讓 entry **更容易**通過，不是更難。
> 📌 §9.5.2 引用的「站位帶只剩 20 cm」是 `docs/24` **§9.3 的 Vault1m 舊值**（`ForwardScanDistance` 還是 1.25 m 的時代）；
> §10 的工作包一已把它改成 **1.00–1.70 m（0.70 m 寬）**（`docs/24` §10.4 表）。⇒ **引用到已被自己推翻的數字。**
>
> **⚠️ `EntryCapsuleWallClearance` 也不是主因**：它是
> `dot(capsuleCenter − wallPoint, wallNormal) − radius + skinWidth`（`TraversalProbe.cs:453`）。
> 偏移把 root 往後推 0.12、又把 capsuleCenter 往前推 0.12 ⇒ **兩者相消，clearance 不變**。
>
> ### 👉 真正可疑的是另一條路徑：**Probe 用「當下的（已偏移的）`center`」重建 corridor 膠囊**
>
> `TraversalProbe.IsCapsuleSegmentClear`（`TraversalProbe.cs:665`）沿 entry→clearance→transfer→exit
> 取樣時，每一格都是 `root + TransformVector(_characterController.center)`。
> **`center` 在偏移生效期間是動的** ⇒ 整條 corridor 的檢查膠囊被整體往前推 0.12 m，
> 而 clearance／transfer 那幾格本來就貼著牆面與上緣 ⇒ 極可能 `CheckCapsule` 命中
> ⇒ `CorridorBlocked`，而這個 reject **在 `TraversalEntryPolicy.Evaluate` 的第一段就 return**，
> 距離／朝向根本沒被評估。同樣的 live-center 讀法還有 `TraversalProbe.cs:265`（掃描射線原點）
> 與 `:435`（destination 可站性）。
>
> ### 這條不解決，A／B／C 三條路都是在錯的前提上選
>
> - 若真凶是 corridor 重建讀了動態 center ⇒ §9.5.3 的「trap 2：只改 probe 只修一半」**不成立**，
>   因為站位本來就還在合法帶內 ⇒ 那可能是**全部**的修法，而且**不需要動 traversal 的任何門檻或 entry policy**
>   （只是讓 probe 讀 authored 基準 center，＝ 它在偏移出現前一直隱含假設的東西）。
> - 若真凶另有其人 ⇒ 至少 B（調低 `MaxOffset`）的「要調到多少」有了可計算的基準。
>
> ### ✅ 已用 PlayMode 探針證實（2026-09-15，**不需要人工 Play**）
>
> 探針：`Assets/_Project/Tests/PlayMode/CapsuleOffsetTraversalCorridorSpike.cs`
> （⚠️ **throwaway spike**，CLAUDE.md「Spike / Probe Exception」；只讀 `probe.Candidate`，不改 traversal 任何門檻）。
> 場景：地面 ＋ 高 2.0／深 1.0 障礙物（Climb2m），前面在 `z = 1.0`；
> root 放在 `CharacterController` 實際會停下的位置 `wallFront − (radius + skinWidth + offset + standoff)`。
>
> | case | radius | offset | standoff | root→wall | kind | rejectReason | corridorReason | clearance |
> |---|---:|---:|---:|---:|---|---|---|---:|
> | baseline | 0.12 | 0 | 0 | 0.150 | **Climb2m** | None | None | 0.0600 |
> | **offset-on** | 0.12 | **0.12** | 0 | 0.270 | **None** | **CorridorBlocked** | **EntryToClearanceBlocked** | 0.0600 |
> | **control** | 0.12 | 0 | **0.12** | **0.270** | **Climb2m** | None | None | 0.1800 |
> | baseline | 0.22 | 0 | 0 | 0.250 | Climb2m | None | None | 0.0600 |
> | **offset-on** | 0.22 | **0.12** | 0 | 0.370 | **None** | **CorridorBlocked** | **EntryToClearanceBlocked** | 0.0600 |
> | **control** | 0.22 | 0 | **0.12** | **0.370** | **Climb2m** | None | None | 0.1800 |
> | baseline | 0.32 | 0 | 0 | 0.350 | Climb2m | None | None | 0.0600 |
> | **offset-on** | 0.32 | **0.12** | 0 | 0.470 | **None** | **CorridorBlocked** | **EntryToClearanceBlocked** | 0.0600 |
> | **control** | 0.32 | 0 | **0.12** | **0.470** | **Climb2m** | None | None | 0.1800 |
>
> ### 🔴 三條結論
>
> 1. **⭐ `control` 與 `offset-on` 站在完全相同的位置，結果相反。**
>    同一個 root→wall，只差「`center` 有沒有被偏移」⇒ **站位不是原因，`center` 才是。**
>    ⇒ §9.5.2「站位退後 0.12 m 吃掉 entry 合法帶」**證實為錯誤歸因**。
> 2. **與 radius 無關**：0.12／0.22／0.32 三個半徑行為完全一致（連 `clearance` 的 0.0600 都一樣，
>    因為偏移把 root 往後推、又把 capsuleCenter 往前推，**兩者在該式子裡相消**）。
> 3. **reject 發生在 Probe，不在 entry policy**：`CorridorBlocked` 來自
>    `TraversalProbe.IsCapsuleSegmentClear` 的 entry→clearance 段
>    ——整條檢查膠囊被往前推一個 offset ⇒ 撞進牆。`TraversalEntryPolicy.Evaluate`
>    的距離／朝向判斷**從來沒有被執行到**。
>
> ### ⇒ trap 2「只改 probe 只修一半」不成立
>
> `control` 列已經證明：在**偏移開啟後的那個站位**上，只要 probe 讀的是 authored 基準 center，
> candidate 就完全合法（`Climb2m / None`）。**⇒ 讓 probe 讀基準 center 可能就是全部的修法**，
> 且**不需要動 `MaxOffset`、不需要動 traversal 任何門檻或 entry policy**。
>
> ### ⚠️ 但修法本身有一個尚未裁決的設計問題（**不要直接動手**）
>
> `TraversalProbe` 在 `Core`，`MotionDriver._capsuleBaseCenter` 在 `Presentation`
> ⇒ **probe 不能去問 MotionDriver**（依賴方向禁令）。兩個候選：
> (a) probe 在 `Awake` 自己快照 `center`（與 `CaptureCapsuleBaseCenter` 同 pattern，但**「authored 基準」會有兩個持有者**）；
> (b) 把 base center 變成**黑板欄位**由 `MotionDriver` 發布、probe 讀（單一真相，但新增黑板欄位＝要改 §1.1 權限表與 `WriterRules`）。
> **這是 traversal 的程式改動，而 traversal 仍是 frozen ⇒ 需要使用者裁決。**
>
> 📌 **探針的處置**：修法落地時把它**轉成正式回歸測試**（它剛好就是這個 bug 的最小重現）；
> 若決定不修，**刪掉它**。⛔ 不要讓它以 spike 的身分長期留在測試集裡。


> ## ✅ 0b. **radius 不一致已裁決並清理**（2026-09-15 使用者）
>
> **`radius = 0.32` 是刻意採用的正確值，即新的 authored baseline。** 之前只是忘了同步 Scene override 與文件。
>
> | 來源 | 原值 | 現況 |
> |---|---:|---|
> | `Assets/Prefabs/X Bot.prefab` | 0.32 | ✅ 正確，未動 |
> | `SampleScene.unity` X Bot 實例 override | 0.12 | ✅ **已用 `PrefabUtility.RevertPropertyOverride` 清除**，現跟隨 prefab ＝ 0.32 |
> | `Y Bot` | 0.32 | ✅ 一致 |
> | 本檔算術 | 0.22 | ✅ 已全面改為 0.32（§0 更正欄、§1.5、§8.2、§9.5.1、§9.5.2、§10-2） |
>
> ⚠️ **還原方式**：在 Inspector 對 X Bot 的 `CharacterController.Radius` 重新輸入 0.12（會再建立一個 override）。
> ⚠️ 本項**只有 Unity 記憶體態改了，尚未落盤**——需要使用者 Ctrl+S 存場景。
> 📌 探針結論不受影響：0.12／0.22／0.32 三個半徑都已證明失敗來自 live center 偏移，與 radius 無關。
1. **還要不要做 B（pose-driven）？**
   依據仍然成立（§1 三個假設被實測推翻），但**它不會自動解決 §9.5.2 的 traversal 衝突**
   ——pose-driven 在跑步時的前向質心是 **0.161 m**，比目前的 0.12 **更大**。
   ⇒ 除非它在**貼牆瞬間**的值明顯較小（那需要另外量：貼牆時角色其實在減速，質心會回收），
   否則換模型不等於換掉衝突。~~**做之前應先量「貼牆減速段」的質心軌跡。**~~
   🔄 **2026-09-15 更新（§11）**：①§10 第 0 條已證明 traversal 失敗與站位無關 ⇒ 這裡的「衝突」本身不成立；
   ②在 r = 0.32 下**核心質心從來沒有離開膠囊**（Sprint 最大 0.212 vs 0.32）
   ⇒ **pose-driven 要追的東西也在膠囊裡** ⇒ B 的依據同樣被削弱，不只是「不解決衝突」。
2. **頭要不要被膠囊包住？**（§1.5：衝刺時頭最前 0.405，而半徑 0.32 ⇒ 超出 **8.5 cm**；🔄 r=0.22 時是 18.5 cm）
   加大半徑會影響窄道與 traversal 合法帶——與 §9.5.2 是同一個張力。
3. **traversal entry band 什麼時候變寬？** 那是 `docs/24` §10–§11 的工作，
   不屬於本檔；但它是 Player 重新啟用 dynamic capsule 的前提之一。

---

## 11. ~~相對 `radius = 0.32` 的重新量測與 `MaxOffset` 再評估~~（2026-09-15，**⛔ 結論作廢，見 §12**）

> # ⛔ 本節的結論是錯的，已由 §12 取代。保留全文只為記錄「錯在哪」。
>
> **錯誤**：本節用「Hips/Spine 核心質心是否落在 radius 內」當完成條件，得出
> 「核心質心從來沒有離開膠囊 ⇒ dynamic capsule 不需要」。
>
> **為什麼錯（使用者 2026-09-15 指出）**：
> 1. **Play 畫面明確看得到頭／上半身穿牆** ⇒ 功能需求客觀成立，任何導出「不需要」的判準都必須先被懷疑。
> 2. **用平均去回答一個外緣問題。** 質心是 6 根骨骼的等權平均，它必然遠小於最外緣。
>    §12 的網格實測：Sprint 時**頭部網格超出 0.271 m**，而同一時刻核心質心只超出 −0.108（在膠囊內）。
> 3. **骨骼點不等於網格。** 即使只看 Head **骨骼**也只有 0.085——網格比骨骼多 3 倍以上，
>    因為顱骨與面部延伸在關節之外。
> 4. **忽略了膠囊在高處會變窄。** 本節（以及 §1.5）拿 0.32 當所有高度的可用半徑，
>    但 0.32 只在 `y ∈ [0.320, 1.507]` 成立；頭高 1.60 只有 **0.306**，顱頂 1.82 只剩 **0.067**。
>
> **仍然有效的部分**（§12 以網格重測後仍然成立，可繼續引用）：
> - §11.1 的**量測方法**（AnimationMode 取樣＋首尾去趨勢），含「root motion 烘在 hips 裡」這個坑。
> - §11.3 ③ 的**純側移方向錯誤**：`StrafeLeftLoop` 往左走、身體卻往右。§12 以網格複驗：純側移的
>   required offset ＝ **0**，而現行模型會推 0.12 到錯的一側。
>
> ⛔ **不得再以「核心質心是否在 radius 內」作為 dynamic capsule 的判準**（使用者明確裁決）。


> 觸發：使用者裁決 0.32 為 authored baseline（§10-0b）後，**§1 所有「超出量」結論的前提都變了**，
> 必須重算才能談 `MaxOffset`。⛔ 本節**不重新啟用**玩家 dynamic capsule——使用者明確要求先量再決定。

### 11.1 量測方法（可重現，未建成工具）

一次性 `unity cmd eval_file` 探針（**未進版控**，依 CLAUDE.md「Editor Tool vs Documented Process」：
一次性、不重複 ⇒ Gate A 不成立，改記錄流程）：

1. `PrefabUtility.LoadPrefabContents("Assets/Prefabs/X Bot.prefab")` —— 在 preview scene 取得 rig，
   **完全不碰使用者打開的場景**。
2. Animator 在子物件 **`Model`**（不是 prefab root，root 上只有 `CharacterController`）。
3. `AnimationMode.StartAnimationMode()` ＋ `AnimationMode.SampleAnimationClip(model, clip, t)`，每支 clip 取樣 **121 幀**。
4. 骨骼位置以 `model.transform.InverseTransformPoint()` 轉進 root 軸。
5. 🔴 **必須去趨勢**：humanoid clip 的 root motion **被烘在 hips 裡**，
   不去掉的話 Sprint 會量到「質心前移 4.16 m」（那是位移不是傾斜）。
   去趨勢用 **首尾核心質心的水平差 × t/length**（loop 首尾同姿勢 ⇒ 差值即整圈位移）。
   ⚠️ 用 `clip.averageSpeed` 去趨勢**會殘留誤差**（每支 clip 的最大值都落在最後一幀＝殘差累積的徵兆）。
6. core ＝ `Hips/Spine/Spine1/Spine2/Neck/Head` 等權質心（與 §4.1 同一組）。

**方法可信度**：反推出的速度是 Walk 1.64／Run 3.58／Sprint 6.26 m/s，對得上既有的 1.62／3.50／6.10；
Run／Sprint 的 `cZavg` 為 **0.162／0.159**，與 §1.3 舊值 **0.161／0.160** 差 1 mm ⇒ **重現了前一輪的量測**。

### 11.2 結果（`over` ＝ 水平半徑 − 0.32，正數＝超出膠囊表面）

| clip | 速度 | coreR_avg | coreR_max | **coreOver_max** | headR_max | **headOver** | cZavg | cXavg | cZ擺幅 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| WalkFwdLoop | 1.64 | 0.022 | 0.042 | **−0.278** | 0.057 | −0.263 | 0.000 | 0.018 | 0.027 |
| RunFwdLoop | 3.58 | 0.163 | 0.195 | **−0.125** | 0.340 | **+0.020** | 0.162 | 0.014 | 0.050 |
| **SprintFwdLoop** | 6.26 | 0.160 | 0.212 | **−0.108** | **0.405** | **+0.085** | 0.159 | 0.000 | 0.102 |
| RunBwdLoop | 2.21 | 0.129 | 0.159 | −0.161 | 0.129 | −0.191 | −0.123 | 0.036 | 0.055 |
| StrafeLeftLoop | 1.64 | 0.049 | 0.099 | −0.221 | 0.081 | −0.239 | −0.035 | **+0.029** | 0.070 |
| StrafeRightLoop | 1.64 | 0.034 | 0.051 | −0.269 | 0.043 | −0.277 | −0.018 | **−0.008** | 0.057 |
| RunLtLoop | 2.16 | 0.037 | 0.073 | −0.247 | 0.115 | −0.205 | 0.020 | −0.027 | 0.060 |
| RunRtLoop | 2.27 | 0.070 | 0.089 | −0.231 | 0.182 | −0.138 | 0.056 | +0.041 | 0.044 |
| RunStrafeLeft45Loop | 3.62 | 0.163 | 0.194 | −0.126 | 0.351 | **+0.031** | 0.149 | −0.066 | 0.044 |
| RunStrafeRight45Loop | 3.62 | 0.161 | 0.191 | −0.129 | 0.322 | **+0.002** | 0.126 | +0.100 | 0.032 |
| RunStrafeLeft135Loop | 2.21 | 0.079 | 0.106 | −0.214 | 0.071 | −0.249 | −0.073 | −0.024 | 0.021 |
| RunStrafeRight135Loop | 2.21 | 0.142 | 0.172 | −0.148 | 0.176 | −0.144 | −0.055 | +0.130 | 0.055 |

### 11.3 🔴 三個直接推翻 `MaxOffset = 0.12` 的讀法

**① 核心質心在 r = 0.32 下「從來沒有離開膠囊」。**
最糟的 Sprint 也只到 **0.212**，距離膠囊表面還有 **10.8 cm**。
⇒ dynamic capsule 的 stated purpose（`CapsuleOffsetSolver` 註解：讓碰撞體覆蓋移動側的身體）
在 r = 0.32 下**所需偏移量 ＝ 0**。這不是「調小一點」，是**前提消失**。
（r = 0.22 時 Sprint 0.212 − 0.22 ＝ −0.008，幾乎剛好貼齊 ⇒ 難怪當時看起來需要偏移。）

**② 唯一還在膠囊外的是「頭」，而它需要的是 0.085，不是 0.12。**
只有 Run（+0.020）、Sprint（**+0.085**）、RunStrafe45（+0.031／+0.002）為正。
⇒ 若把 `MaxOffset` 重新定義成「覆蓋衝刺時前伸的頭」，**實測上界是 0.085**；
0.12 是**超額 41%** 的量，且沒有任何一支 clip 需要它。
⚠️ 但這是**改變功能的定義**（從「跟著核心」變成「追著頭」），
而 §4.3 已裁決「⛔ 不要用 max 當目標」——追頭就是在追 argmax。**要改定義必須先裁決。**

**③ Strafe 的方向錯誤，現在在量級上勝過收益。**
`StrafeLeftLoop` 往左移動，但核心 `cXavg = **+0.029**`（往**右**傾）；
`StrafeRightLoop` 往右移動，`cXavg = −0.008`（往**左**）。**兩支都是反的。**
模型會把膠囊推 0.12 到錯誤的一側 ⇒ 誤差 ≈ 0.12 + 0.029 ≈ **0.149 m**，
而該 clip 原本的核心外擴只有 **0.099**（且完全在膠囊內）。
⇒ **偏移在純側移時製造的誤差，比它要修的問題大一個量級。**（與 §1.2 的 ~130° 夾角同源，
本輪以 0.32 為基準重測後，結論更強：那不只是「方向不準」，是「本來沒問題、偏移把它弄出問題」。）

### 11.4 ⇒ 對 `MaxOffset` 的評估結論

| 若 dynamic capsule 的目的是… | 實測需要的 `MaxOffset` | 現值 0.12 |
|---|---:|---|
| 讓膠囊跟著**核心質心**（現行 stated purpose） | **0.000** | ❌ 無依據 |
| 覆蓋**衝刺時的頭**（需要先改定義，見 §4.3 的 max 禁令） | ≤ **0.085**，且只在前向、只在 Sprint | ❌ 超額 41% |
| 覆蓋**四肢** | 不適用（§4.2 已否決：包住揮出的手會卡門框） | — |

**⇒ 在 r = 0.32 下，`MaxOffset = 0.12` 沒有任何量測支撐。**
`X Bot` 目前關閉（§9.5.3）**恰好是證據支持的狀態**，本輪不改。

**仍然開著、需要使用者裁決的**（⛔ 不要在這些有答案前改 `MaxOffset` 或重開玩家）：

1. **`Y Bot` 要不要跟著調？** 它現在 `Enabled = true`、`MaxOffset = 0.12`、radius 同為 0.32
   ⇒ 上面三條讀法**一字不改地適用於它**。它沒有 traversal，所以不會壞掉，
   但它同樣在純側移時把膠囊推向錯的一側。
2. **頭穿模要怎麼處理？** 三選一（§10-2）：加大 radius／加重 `Head` 權重（連續，非 max）／接受。
   ⚠️ 加大 radius 會再影響 traversal corridor 與窄道——**與 §10 第 0 條是同一個張力**。
3. **dynamic capsule 這個功能本身還要不要留？** 上面的 ① 等於說：在 0.32 這個半徑下，
   它要解決的幾何問題不存在。**這是「保留待未來半徑變小／改 pose-driven」還是「收掉」的取捨**，
   不是可以從數據自動推出來的。
   📌 但 §10 第 0 條的 probe bug **無論如何都該修**——那是 traversal 自己讀錯基準，
   與 dynamic capsule 去留無關（任何第二個寫 `center` 的人都會再踩到）。

---

## 12. Upper-body occupancy：以有限 landmarks 求最小必要 capsule-center correction（2026-09-15 研究輪）

> ✅ **已於同日實作落地，見 §13。白話教學見 `docs/28`。**

> **狀態：研究完成 ⇒ ✅ 已實作（§13）。** 本節保留為設計依據與量測證據。
> **取代 §11 的結論**；§11 的量測方法與「純側移方向錯誤」仍然有效。
>
> **約束（使用者給定）**：⛔ 不用手／腳／武器；⛔ 不直接用全身最大外擴點；
> ⛔ 不用 `MoveDirection` 猜姿勢；⛔ 不單純加大 radius；desired offset 必須連續可平滑；
> 最終仍要 collision-aware clamp；`TraversalProbe` 讀 base center 視為獨立 bug，不得用它否決本功能。

### 12.0 🔴 先修正一個一直被忽略的幾何事實：膠囊在高處會變窄

`R = 0.32`、`height = 1.8266`、`center.y = 0.9134` ⇒ 上半球中心在 **y = 1.5067**，膠囊頂端 y = 1.8267。
**`y > 1.5067` 之後水平可用半徑不再是 0.32**，而是 `sqrt(R² − (y − 1.5067)²)`：

| 位置 | rest y | 該高度可用半徑 |
|---|---:|---:|
| Spine1 | 1.2435 | 0.320 |
| Spine2 | 1.3357 | 0.320 |
| Neck | 1.5031 | 0.320 |
| **Head（顱底關節）** | 1.5993 | **0.306** |
| **HeadTop_End（顱頂）** | 1.8196 | **0.067** |

⇒ **頭整顆都活在上半球裡。** 本檔 §1.5／§11 一律拿 0.32 當所有高度的可用半徑，
因此系統性低估了頭部的超出量。以下所有 `over` 都改成對照**該頂點自己高度**的可用半徑。

### 12.1 量測方法

沿用 §11.1（AnimationMode 121／41 幀取樣、preview scene、首尾去趨勢），但量測對象換成**實際蒙皮網格**：

- `SkinnedMeshRenderer.BakeMesh()` 取當幀姿勢下的頂點（`Beta_Joints` 12,473 ＋ `Beta_Surface` 15,901）。
- 每個頂點依 **dominant bone weight** 歸區；⛔ **只保留 `Spine1`／`Spine2`／`Neck`／`Head` 四區**
  （手、前臂、手掌、腿、腳、武器全部剔除）。
- `over(v) = |v_xz − c| − Reff(v_y)`。
- **ground truth 最小偏移**：把頂點依 2 cm 高度分箱、每箱取 16 個方向極值當代表點，
  再以 **Dykstra 演算法**把原點投影到 `∩ D(代表點, Reff(箱高))` ——即「能覆蓋整個上半身的最小範數 c」。

📌 `HeadTop_End` **沒有任何頂點以它為 dominant bone**（它是 leaf marker，不參與蒙皮）
⇒ 顱頂的頂點全部歸在 `Head` 區。這也是為什麼 §10 的骨骼量測看到 `HeadTop_End` 超出 0.276——
那是**骨骼點**，不是網格。

### 12.2 Q1 — 各部位實際最大超出（網格，r = 0.32）

| clip | Spine1 | Spine2 | Neck | **Head** | 最小必要偏移 optC_max | 套用後殘餘 |
|---|---:|---:|---:|---:|---:|---:|
| WalkFwdLoop | 0.000 | 0.000 | 0.000 | **0.000** | 0.000 | 0.000 |
| RunFwdLoop | 0.010 | 0.010 | 0.036 | **0.204** | 0.207 | 0.000 |
| **SprintFwdLoop** | 0.029 | 0.042 | 0.092 | **0.271** | **0.271** | 0.000 |
| StrafeLeftLoop | 0.000 | 0.000 | 0.000 | **0.000** | 0.000 | 0.000 |
| RunStrafeLeft45Loop | 0.013 | 0.024 | 0.041 | **0.209** | 0.212 | 0.000 |

🔴 **兩個結論**：

1. **頭部網格超出 20.4–27.1 cm。** 對照同一時刻的**骨骼**量測（§10 的 4-landmark 表）只有
   **2.0–8.5 cm** ⇒ **骨骼點低估 3–10 倍**。使用者在 Play 看到的穿牆是網格，不是關節。
2. **殘餘全為 0** ⇒ **純水平的 center correction 在幾何上足以覆蓋整個上半身。**
   （先前擔心「顱頂在膠囊尖端、補不完」並不成立——顱頂頂點靠近軸心，前傾時仍可被涵蓋。）

### 12.3 Q2 — 哪組 landmarks 對應得上

| landmark | 佔超出量的比重（Sprint） | 角色 |
|---|---:|---|
| **Head** | **0.271**（100%） | **驅動者**——決定要往哪推、推多少 |
| Neck | 0.092（34%） | 次要 |
| Spine2 | 0.042（15%） | **限制者**——防止解過度前移把胸腔甩出去 |
| Spine1 | 0.029（11%） | 限制者 |

- **Head 主導，差距 3–5 倍**，與 Play 觀察一致。
- 但**單用 Head 不行**：只有一個約束時最小範數解會一路追著頭跑，胸腔／頸部沒有東西擋。
  四點聯立才會得到 §12.4 的「殘餘 < 0」。
- **實測 `argmaxFlips = 0`**（所有前向移動的 clip）⇒ binding constraint 整圈不換人。
  原因是算術上的：`ρ_Head ≈ 0.057` 而 `ρ_Spine1 = 0.116` ⇒ Head 永遠最緊。
  **⇒ 這也是為什麼「連續 vs argmax」的顧慮在這裡不會發作**（見 §12.4）。
- ⚠️ **關鍵**：landmark 必須配一個 **authored flesh radius**，否則就是上面那個低估 3–10 倍的錯誤。

### 12.4 Q3 — 連續的 required offset 怎麼求（不用 argmax）

**模型**：每個 landmark `i` 代表一顆半徑 `r_i` 的球（`r_i` ＝ rest pose 下該關節到其 dominant 頂點的最大距離，
**剛性蒙皮下 pose-invariant，離線量一次即可**）：

| landmark | `r_i`（實測） | `R − r_i` |
|---|---:|---:|
| Spine1 | 0.2038 | 0.1162 |
| Spine2 | 0.1867 | 0.1333 |
| Neck | 0.0952 | 0.2248 |
| Head | 0.2109 | 0.1091 |

**「半徑 `r_i` 的球完全落在膠囊內」的精確條件**（膠囊只平移、不變形 ⇒ 高度剖面不變）：

```
ρ_i = sqrt( (R − r_i)² − max(0, y_i − topC)² )      // 低於上半球中心時 ρ_i = R − r_i
約束： |q_i − c| ≤ ρ_i                                // q_i = landmark 的水平座標
```

**required offset `c*` ＝ 原點在 `∩ D(q_i, ρ_i)` 上的投影**（最小範數可行點）。
不可行時退化為 Chebyshev 中心（把最大超出壓到最小）。

**為什麼它連續、為什麼它不是 argmax**：

| 性質 | 理由 | 實測 |
|---|---|---|
| **連續** | 凸集投影對輸入連續；active constraint set 換人時解**不跳**（那正是 argmax 會跳而投影不會的差別） | `dEst/frame` 最大 **0.0124 m**（Sprint，≈60 fps 一幀）⇒ **0.8 m/s**，現有 `SmoothDamp` 綽綽有餘 |
| **自然歸零** | 全身在膠囊內時可行集包含原點 ⇒ `c* = 0`，且越界時從 0 **連續長出**，不是開關 | Walk／純側移實測 **0.000** |
| **不用 MoveDirection** | 方向完全由 `q_i` 決定 | RunStrafeLeft45 解出 `(Z +0.197, X −0.072)`，比例 **2.7 : 1**，**不是 45°** |
| **不用全身最大外擴點** | 只用 4 個 landmark ＋ 4 個離線常數 | — |
| **零配置** | 4 個圓的投影迭代，`Vector2` 值型別 | — |

**驗證**：拿這個 4-landmark 解回頭檢查**真實網格**，殘餘 `meshResid` 在所有 clip 都是**負的**
（−0.014 ~ −0.070）⇒ **完全覆蓋，還有 1.4–7.0 cm 餘裕**。
保守代價：比 ground-truth 最小解多推 **0.033–0.071 m**（球狀近似的必然過估）。

> **🔬 一個有用的反例（別再試）**：把頭部錨點從 `Head` 關節上移到 `mid(Head, HeadTop_End)`（y = 1.709）
> 並用較小的 `r = 0.1755`，**需求反而變大**（Sprint 0.296 → **0.354**，連 Walk 都從 0 變成 0.095）。
> 原因：`y = 1.709` 深入上半球，`(R − r)² − (y − topC)²` 直接變負 ⇒ `ρ = 0` ⇒ 約束變成「錨點必須正好等於 c」。
> **膠囊變窄的速度比 flesh radius 縮小的速度快** ⇒ 錨點要往**低**放，不是往高放。

### 12.5 Q4 — Run / Sprint / Strafe 各需要多少

| clip | `c*` 平均 | `c*` 最大 | 方向 (Z, X) |
|---|---:|---:|---|
| WalkFwdLoop | 0.000 | 0.000 | — |
| **RunFwdLoop** | 0.198 | **0.231** | (+0.197, +0.011) |
| **SprintFwdLoop** | 0.229 | **0.296** | (+0.226, +0.006) |
| RunBwdLoop | 0.029 | 0.066 | (−0.028, +0.007) |
| **StrafeLeftLoop（純側移）** | **0.000** | **0.000** | — |
| **StrafeRightLoop（純側移）** | **0.000** | **0.000** | — |
| RunStrafeLeft45Loop | 0.211 | 0.241 | (+0.197, −0.072) |
| RunStrafeRight45Loop | 0.184 | 0.215 | (+0.162, +0.085) |

🔴 **現行模型在兩端都錯，而且錯的方向相反**：

- **純側移需要 0，現行模型推 0.12 —— 而且推向錯的一側**（§11.3 ③：身體實際往反方向傾）。
- **前向跑步需要 0.20–0.30，現行模型最多給 0.12** —— 只補了 **40–50%**。
- ⇒ 這解釋了為什麼「開了看不出效果」同時「頭還是穿牆」：**它在不需要的地方動，在需要的地方不夠。**

### 12.6 Q5 — `MaxOffset` 改成姿勢決定的上限：可以，但它是一個取捨而不是一個數字

**可以，而且應該**：`MaxOffset` 從「驅動量」（`MaxOffset × speed × turnWeight` 沿 `MoveDirection` 推滿）
降級為**安全上限**，實際值由 §12.4 的解給。但資料顯示這**不是單純把 0.12 調大**——偏移有代價：

**膠囊是整顆平移的，底部支撐點也跟著前移。** cap 掃描（Sprint）：

| cap | 上半身殘餘超出 | 下半身（臀／大腿）外露 | 支撐點前移 |
|---:|---:|---:|---:|
| 0.00（現在 X Bot 的狀態） | **0.271** | 0.125 | 0.000 |
| 0.06 | 0.211 | **0.076** ⬅ 最小 | 0.060 |
| 0.12（現值） | 0.151 | 0.136 | 0.120 |
| 0.18 | 0.091 | 0.196 | 0.180 |
| 0.24 | 0.031 | 0.230 | 0.240 |
| **0.30** | **−0.023** ⬅ 完全消除 | 0.230 | **0.296** |

（RunFwd 與 RunStrafeLeft45 形狀相同，各自在 cap 0.24 / 0.24 達到殘餘 ≤ 0。）

**三個從這張表讀到的事實**：

1. **下半身外露不是偏移造成的**：`cap = 0` 時本來就有 **0.111–0.125**（衝刺時後擺的大腿）。
2. **小幅偏移反而讓下半身更貼合**：`cap = 0.06` 時 Sprint 的下半身外露從 0.125 降到 **0.076**
   ——膠囊往前一點其實更居中。⇒ 偏移不是「用下半身換上半身」的純零和。
3. **支撐點前移是真正的代價**：0.296 m ≈ 三分之一公尺。站在平台邊緣時會出現
   「看起來踩空卻站著」或「還沒走到邊就掉下去」。⚠️ 這一項**沒有被量測涵蓋**
   （需要 Play 才看得出可接受與否），但它會隨 cap 線性成長，必須列入裁決。

**⇒ 建議（待裁決，不是結論）**：

| 目標 | cap | 代價 |
|---|---:|---|
| 牆邊完全不露頭 | **0.30** | 支撐點前移 0.30；下半身外露 0.23 |
| **平衡（建議）** | **0.18** | 頭部殘餘 0.091（比現在的 0.271 少 66%）；支撐前移 0.18 |
| 只修最明顯的、代價最小 | 0.12（維持現值） | 頭部殘餘 0.151（少 44%）；下半身回到基準值 |

我傾向 **0.18**：它拿掉三分之二的穿模，而支撐點前移還在「一個腳掌」的量級。
但**剩下的 0.091 不該再靠加大 cap 去追**——根因是**膠囊頂端剛好切在頭頂**（§12.0），
那要靠**膠囊高度／`center.y`** 這個獨立槓桿，不是 radius（使用者已排除），也不是無限加大偏移。
⚠️ 動膠囊高度會影響 step-over、天花板檢查與 traversal corridor ⇒ **另一個裁決，不在本輪。**

### 12.7 仍然成立的兩個既有約束

- ✅ **collision-aware clamp 仍然必要，而且更必要**：偏移量從 0.12 升到 0.18–0.30 之後，
  把 center 推進牆裡的風險等比放大。現有 `MotionDriver.ClampCapsuleOffsetAgainstGeometry` 形狀不變可沿用
  （cast 半徑 `R − skinWidth` = 0.29）。
- ✅ **`TraversalProbe` 讀 live center 是獨立 bug**（§10 第 0 條）。它**不否決本功能**，
  但偏移變大之後它會壞得更明顯（0.30 的 corridor 位移遠比 0.12 嚴重）
  ⇒ **實作 dynamic capsule 之前應先修它**，否則會再得到一次「起攀完全不觸發」。

### 12.8 下一步（⛔ 本輪不做）

1. 使用者裁決 cap（0.12 / 0.18 / 0.30）與是否要另開「膠囊高度」的題。
2. 決定 `r_i` 的來源：寫死在 solver（4 個常數）vs 放進一個 authored SO（可換角色）。
   ⚠️ 目前 4 個數字是從 **X Bot 的網格**量出來的，**換角色就不對**。
3. 把 §12.4 的解法寫成純函式（維持現有 `CapsuleOffsetSolver` 的可測性形狀），
   輸入 4 個 landmark 的 root-local 座標 ＋ 4 個 `r_i` ＋ 膠囊參數，輸出 desired offset。
4. 這些量測腳本都是 throwaway `eval_file` 探針，**未進版控**（Editor Tool 兩道閘門不成立）。
   要重跑就照 §12.1 重寫——關鍵三個坑：Animator 在 `Model` 子物件、root motion 烘在 hips 裡、
   `HeadTop_End` 不參與蒙皮。

---

## 13. ✅ V2 已落地（2026-09-15）

> **狀態：實作完成、測試全綠、資產已接、Play 已在真實場景驗過。**
> **白話教學在 `docs/28`**——想理解這套系統先讀那份，本節只記「做了什麼、量到什麼」。
> §12 的研究結論**全數成立**，本節是它的實作紀錄。

### 13.1 落地清單

| 項目 | 內容 |
|---|---|
| **TraversalProbe base-center bug** | ✅ `Awake` 快照 `AuthoredCenter`／`AuthoredRadius`／`AuthoredHeight`，所有環境查詢改讀快照。🔒 不變量由 `TR1`（行為）＋`TR2`（欄位）守住 |
| **Pose solver** | ✅ `Presentation/Motion/CapsulePoseSolver.cs`——純函式、零配置（inline `Vec2x8` 緩衝，不用 `new[]`）、確定性（Dykstra 固定 24 次迭代，由 `CP10` 與 300 次比對釘住） |
| **Authored profile** | ✅ `CapsulePoseProfileSO` ＋ 兩份資產（X Bot／Y Bot 各自量測值） |
| **Collision-aware clamp** | ✅ 沿用既有 `ClampCapsuleOffsetAgainstGeometry`，改用 **authored** radius／height，且移到平滑**之後** |
| **Runtime integration** | ✅ `MotionDriver` 五層管線；`locomotionHasAuthority` 參數**消失**（V2 讀骨架，不需要知道位移來源） |
| **Debug 可視化** | ✅ 五個圓環（灰基準／黃 Desired／橙 Capped／青 Smoothed／洋紅 Safe）＋ 舊 MoveDirection 模型的紅色虛線對照 |
| **Tests** | ✅ EditMode `CP1–CP12`（solver）＋`CD1–CD5`（driver）；PlayMode `CO1–CO7`＋`TR1`（×3 radii）`TR2` |
| **資產接線** | ✅ X Bot／Y Bot 兩隻 prefab：`Enabled=true`、`Profile` 指派、`poseAnimator` 指派；SampleScene 的 radius override 已清除並存檔 |

### 13.2 🔴 順序裁決：Clamp 必須在 Smoothing **之後**

使用者問的是「clamp 先還是 smooth 先」。答案是 **smooth → clamp**，理由是安全性：

`Collision Clamp` 是整條管線裡**唯一認識場景幾何的一層**。排在它後面的任何處理
都可能把已經夾好的值再推回幾何體裡。V1 是 `clamp → smooth`，於是走向牆時
夾持把**目標**壓小、平滑值卻還停在上一幀的大值 ⇒ **寫進 `center` 的數字大於安全值**。

配套的第二個決定：**平滑器追 `Capped`，不追 `Safe`**。
把夾持結果餵回平滑器會讓牆變成平滑狀態的第二個 authority
——貼牆時平滑值被壓到 0，牆一消失還要重新慢慢長回來。由 `CO4` 守住。

### 13.3 guard 重新檢視（每一條都給出「現在仍然成立」的理由）

| guard | V1 | V2 | 理由 |
|---|---|---|---|
| traversal profile 生效 | 停用 | ✅ **保留** | 同一欄位兩個寫入者；且還原時會把偏移變成永久值 |
| 功能停用／缺 profile | 停用 | ✅ **保留** | 沒有 flesh radius 就沒有任何根據去移動膠囊。安全退化為 authored center |
| **死亡** | 無 | 🆕 **新增**（可由 profile 開關） | 倒地姿勢下「上半身佔用位置」與站立膠囊不是同一回事。**唯一仍按狀態關閉的 guard**，理由是姿勢本身離開了模型適用範圍 |
| **不在地面** | 停用 | ⛔ **移除** | V1 的理由是「空中沒有 locomotion 位移真相」——那是 `MoveDirection` 的問題。V2 讀骨架，空中的軀幹一樣會前傾 |
| **committed motion**（Roll／Action／Hurt） | 停用 | ⛔ **移除** | 同上。baked curve 的位移真相不影響「骨架現在擺成什麼樣」 |
| **`MinimumSpeedNormalized`** | 速度低於門檻歸零 | ⛔ **移除** | V2 站直時解出來**自然**是 0（`CP1` 實測）。V1 靠一個**不連續的開關**達成同樣效果，那是多餘且有害的 |
| **轉向角速度權重** | 保留 | ✅ **保留**（理由已更新） | clamp 只沿「偏移方向」掃一次，看不到角色原地轉身時膠囊繞 root 掃出的**弧線**。0.18 偏移在 720°/s 下弧線速度超過 2 m/s，那個方向沒有被 cast 檢查過 |

### 13.4 端到端量測（用 **shipping code** 跑真實動畫姿勢）

`CapsulePoseSolver` ＋ 真實 profile 資產 ＋ 真實 clip，cap = 0.18：

| | clip | solver 解出（max） | cap 後 | 上半身超出（前） | 上半身超出（後） | **改善** |
|---|---|---:|---:|---:|---:|---:|
| **X Bot** | Walk | 0.000 | 0.000 | −0.040 | −0.040 | 本來就沒穿模 |
| | **Run** | 0.231 | 0.180 | **0.204** | **0.024** | **88%** |
| | **Sprint** | 0.296 | 0.180 | **0.271** | **0.091** | **66%** |
| | StrafeLeft | 0.000 | 0.000 | −0.068 | −0.068 | 本來就沒穿模 |
| | RunStrafe45 | 0.241 | 0.180 | 0.209 | 0.030 | 85% |
| | RunBwd | 0.066 | 0.066 | −0.016 | −0.044 | 本來就沒穿模 |
| **Y Bot** | Run | 0.251 | 0.180 | 0.212 | 0.033 | 85% |
| | Sprint | 0.315 | 0.180 | 0.282 | 0.102 | 64% |
| | RunStrafe45 | 0.262 | 0.180 | 0.221 | 0.042 | 81% |

📌 **Sprint 殘餘 0.091 與 §12.6 對 cap 0.18 的預測（0.091）完全吻合** ——
研究輪的模型與實作的行為一致，不是巧合湊出來的。

📌 **純側移解出 0.000**，與 §12.5 一致。V1 在這裡會推 0.12 到錯的一側。

### 13.5 Infeasible 的實際行為（**不抖**）

Sprint 有 60%（X Bot）／100%（Y Bot）的幀回報 `Infeasible`——四個約束無法同時滿足。
擔心的是「解會不會在衝突的約束之間跳來跳去」。實測（181 取樣／clip）：

| prefab | clip | infeasible% | 每步最大變化 | 換算速度 |
|---|---|---:|---:|---:|
| X Bot | RunFwd | 0% | 0.0011 | 0.26 m/s |
| X Bot | **Sprint** | **60%** | 0.0025 | **0.71 m/s** |
| X Bot | RunStrafe45 | 12% | 0.0016 | 0.38 m/s |
| Y Bot | Run | **100%** | 0.0013 | 0.30 m/s |
| Y Bot | Sprint | **100%** | 0.0014 | 0.41 m/s |
| Y Bot | RunStrafe45 | 100% | 0.0019 | 0.44 m/s |

⇒ **Dykstra 在不可行時穩定收斂到一個折衷點，不是振盪。** 最壞 0.71 m/s，
而平滑時間常數是 0.12 s ⇒ 視覺上完全看不到。

🔴 但 `Infeasible` 本身是有意義的訊號：**它在說膠囊尺寸不夠**。
Y Bot 幾乎全程 infeasible，因為它的 Chest flesh radius 是 **0.2902**，
`allow = 0.32 − 0.2902 = 0.0298` ——**它的胸膛本來就快把膠囊塞滿了**。
⛔ **不要為了讓它「不 infeasible」去調小 flesh radius**，那是竄改量測值。

### 13.6 真實場景 Play 驗證

在 SampleScene 進 Play、以 `InputSystem.QueueStateEvent` 按住 W，讀 live 值：

```
X Bot: landmarks=4  status=Corrected  skip=0  resid=0.0000
  desired=(0.015, 0.000, 0.201)   ← solver 讀真實 Run 姿勢
  capped =(0.014, 0.000, 0.179)   ← MaxOffset 0.18 夾住
  smoothed=(-0.004, 0.000, 0.180)
  safe    =(-0.004, 0.000, 0.180) ← 無牆，未被夾
  TraversalProbe authored center=(0.000, 0.913, 0.000) r=0.32 h=1.826619
```

- `desired` 0.201 與離線量測的 0.198 平均值吻合 ⇒ **執行期與離線走的是同一條路**。
- 靜止時 `status=Neutral`、偏移 0 ⇒ 站著不會歪。
- `TraversalProbe` 回報的 authored 幾何未被偏移污染。
- 四個 landmark 透過 `Animator.GetBoneTransform` 在真實 prefab 上成功解析。

⛔ **仍待人類 Play 的**：視覺觀感（貼牆時頭是否還看得出穿模）、
底部支撐前移 0.18 在平台邊緣的手感、Roll／Hurt 期間碰撞行為是否合理。
**自動化測不到「看起來對不對」。**

### 13.7 本輪**刻意沒做**的事

| 沒做 | 為什麼 |
|---|---|
| **dynamic height／`center.y`** | 使用者明確要求本輪不混進來。Sprint 殘餘 0.091 的根因確實是膠囊頂端切在頭頂（§12.0），但那要動 height／`center.y`，會連帶影響 step-over、天花板檢查與 traversal corridor ⇒ **另一個裁決** |
| **加大 radius** | 使用者明確排除。而且加大 radius 會再吃掉 traversal 的通過性 |
| **調 Y Bot 的 flesh radius 讓它不 infeasible** | 那是量測值，不是旋鈕。要解決得動 Y Bot 的 radius |
| **per-animation bake 的碰撞曲線** | §7 已論證 pose-driven 讓它從「必需」降為「刻意偏離姿勢時的 override」。沒有第二個使用者之前不做 |
