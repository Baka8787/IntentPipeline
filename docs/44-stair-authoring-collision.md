# 44 — 樓梯 Authoring／碰撞規格（Stair Authoring & Collision Contract）

> **狀態（2026-09-24）**：方向已由使用者裁決（見 `docs/43` §10 → 本檔）。**不是 ADR**——這是跨系統的
> physics-layer 契約，但不命中 ADR 四項觸發條件（使用者裁決：寫成獨立規格）。
> **已套用（2026-09-24）**：layer、碰撞矩陣、prefab 遮罩、兩道樓梯的移動面；EditMode 全綠。
> **尚未完成**：人工／執行期 Play 驗收（§9）。
> 研究背景與推導：`docs/43` §4（單一 ramp prototype）、§5–§9（Run／腳步聲診斷）、§10（推廣性分析）。

## 1. 目標與驗收

樓梯支援的驗收**不是**「某一道樓梯看起來不錯」，而是：

> **兩道幾何不同的樓梯**，走**同一套產生出來的 authoring 契約**，**不做逐物件手動調整**，
> 對 **Player 與 Enemy** 都成立，且相機／瞄準／traversal／VFX 的遮罩仍然正確。

最低案例：`Stairs_650_400_300_Prefab`（原 prototype，級高 0.195／踏面 0.342，29.7°）與
`Stairs (1)`（ProBuilder，級高 0.194／踏面 0.275，35.2°）。⛔ 不手擺第二塊 ramp。

## 2. 兩個語意 layer

| Layer | 內容 | 誰看得到 |
|---|---|---|
| `StairTread`（index 8，原 `TreadIKOnly`） | 樓梯原本的階梯碰撞體＝**視覺／接觸真相** | 所有「問看得到的幾何」的查詢；**角色移動看不到** |
| `CharacterMovementSurface`（index 3，原 `RampProto`） | 產生器產出的實心斜坡＝**移動真相** | **只有**角色的 CharacterController 碰撞與移動類查詢 |

沿用原 prototype 的 index，只改名稱——既有遮罩的位元意義不變。

## 3. 移動面的推導規則（沒有逐物件參數）

1. 沿樓梯根物件的 local right／forward（水平投影、兩個方向都試）以 5 mm 間距對**樓梯自己的碰撞體**
   `Collider.Raycast`，取得剖面 (s, y)。
2. 相鄰取樣高差 > 0.03 m 即為踢面；要求：至少 2 級、級高介於 0.05–0.4 m、各級與平均差 ≤ 0.01 m、
   每個踏面起伏 ≤ 0.01 m。
3. **參考線＝踏面前緣連線（nosing line）**：最低踏面的下坡端＋每道踢面的頂端，最小平方擬合直線，
   殘差 ≤ 0.01 m。（使用者裁決：用前緣連線，不用中線——角色根永遠不低於踏面，Foot IK 只需要往下補，
   最多一級。）
4. 橫向：在中間踏面量出連續寬度；在寬度 10% 與 90% 處重掃剖面，每一級位置與高度需與中線一致
   （≤ 0.01 m）——只接受直的等寬樓梯。
5. 移動面剖面（s, y）：入口在地面 → 最後一道踢面頂端 → 頂端踏面 → 垂直向下 → 回入口下方；
   底面沉入地面 0.1 m。沿橫向寬度拉伸成 10 個頂點，存成一顆 **convex MeshCollider**，
   作為樓梯根物件的子物件 `__StairMovementSurface`。

實作：`Assets/Scripts/Editor/Tools/StairProfileAnalyzer.cs`（純函數）、
`StairMovementSurfaceGenerator.cs`（場景讀寫）。

## 4. 專案碰撞政策（放在 `ProjectSettings/DynamicsManager.asset`）

| | `StairTread` | `CharacterMovementSurface` |
|---|---|---|
| Player | **忽略** | 碰撞 |
| Enemy | **忽略** | 碰撞 |
| 其他所有 layer | 碰撞（剛體會落在看得到的踏面上） | **忽略** |

- ⛔ 碰撞政策**不得**由執行期元件改寫（刪除 `RampPrototypeLayerSetup`——它在角色 `Awake` 時改全域物理）。
- 新增角色 layer 時，必須同步加入本表（測試 `Policy_*` 會擋）。

## 5. 查詢遮罩：依語意逐一決定（⛔ 不套「含 Default 就含 StairTread」的通則）

使用者裁決：每個查詢系統依它**在問什麼**來決定，不從 Default 推導。

| 語意 | 元件．欄位（所在 prefab） | 看 `StairTread` | 看 `CharacterMovementSurface` | 目前值 → 目標值 |
|---|---|---|---|---|
| 腳落在看得到的踏面 | `FootIKController.settings.GroundLayers`（X Bot、Y Bot） | ✅ | ❌ | X Bot −9（已正確）；**Y Bot −1 → −9** |
| 移動類查詢（step-down，prototype 關閉中） | `MotionDriver.stepDownQueryLayers`（X Bot、Y Bot） | ❌ | ✅ | −1 → −257 |
| 瞄準打在看得到的幾何 | `AimResolver.aimRayMask`（X Bot） | ✅ | ❌ | 129 → 385 |
| 相機被看得到的幾何擋住 | `ThirdPersonCamera.obstructionMask`（Main Camera） | ✅ | ❌ | 1 → 257 |
| traversal 障礙是看得到的幾何 | `TraversalProbe.settings.obstacleMask`（X Bot） | ✅ | ❌ | 19 → 275 |
| 地面技能落在看得到的地面 | `GroundEffectSink.groundMask`（X Bot 的 IceGroundEffect） | ✅ | ❌ | 51 → 307 |
| 投射物撞看得到的幾何 | `ThrownProjectile.sweepMask`（Projectile_Fireball、ThrownProjectile） | ✅ | ❌ | −1 → −9 |
| 膠囊偏移夾持＝移動類查詢 | `MotionDriver.ClampCapsuleOffsetAgainstGeometry`（程式推導，非序列化） | ❌ | ✅ | 原為「自身 layer 以外全部」→ 改為 `MotionDriver.MovementCollisionMask(layer)`：由專案碰撞矩陣推導，排除自身 |

最後一列是 Codex 審查抓到的遺漏：原遮罩會看見角色刻意忽略的 `StairTread`，在樓梯上用角色根本碰不到的
踢面夾住動態膠囊偏移。移動類查詢一律與 `Move()` 問同一個世界——從碰撞矩陣推導，而不是另寫一份遮罩。

**刻意不改**（不是幾何查詢）：`PlayerCombatContextSource.targetMask`、`GroundEffectSink.targetMask`
（找目標，不問地形）。**不受影響**：NavMesh（`NavMeshSurface` 用 render mesh 建置，移動面沒有 renderer）、
相機與燈光的 culling mask（目前是 Everything）。
⚠️ 已知限制：`TraversalProbe` 的 destination `CheckCapsule` 也用同一個 `obstacleMask`，因此看不到實心移動面；
對非凸的階梯 MeshCollider 只能偵測表面交疊。目前沒有觀察到問題，列為待 Play 驗證項。

## 6. 每道樓梯的不變量（驗證器逐條檢查）

| # | 不變量 | 容許值 |
|---|---|---|
| I1 | 恰好一個 `__StairMovementSurface` 子物件，layer＝`CharacterMovementSurface`，恰好一顆有 mesh 的 convex MeshCollider | — |
| I2 | 樓梯根物件（碰撞體）layer＝`StairTread` | — |
| I3 | 移動面與重新產生的結果一致（樓梯被改過就要重產） | 頂點偏差 ≤ 0.01 m |
| I4 | 坡度 ≤ 角色 `slopeLimit`（取場景中最小值）− 5° | — |
| I5 | 移動面與正下方可見表面的高差：不低於可見表面；高出不超過一級 | [−0.03, 級高 ＋ 0.03] m |
| I6 | **底端平台連續**：入口第一級（地面→最低踏面）等於樓梯級高；入口斜坡範圍內地面平整；移動面在地面高度開始（無凸緣） | ± 0.03 m |
| I7 | **頂端平台連續**：頂端外若有平台，必須與最高踏面同高（± 0.03 m）；若落差大於一級，視為開放邊緣（走出去＝墜落，與視覺一致）；介於兩者之間 ⇒ 不通過 | — |
| I8 | 寬度＝踏面寬度（角色忽略踏面，ramp 若較窄，從側邊走出去會穿過視覺樓梯） | 由量測決定 |

0.03 m ＝ CharacterController `skinWidth`：CC 感覺不到的落差。
⚠️ I5 的 [0, 一級] 是**帶容許值**的界線，不是數學上嚴格成立：移動面的斜邊由「地面交點」與「最高踏面前緣」
兩點決定，而前緣擬合允許 ±1 cm 殘差，因此局部可能略低於踏面或略超過一級——驗證器量的是場景中實際產出的
面，容許 ±0.03 m。實測（§9）：兩道樓梯最低 −0.001 m。
量測只看樓梯所在場景的碰撞體（其他同時開啟的場景不影響）；頂、底平台在寬度 10%／50%／90% 三處都要成立。
Foot IK 的骨盆補償上限 `MaxPelvisOffset` ＝ 0.35 m ＞ 一級 0.195 m，因此 I5 在 Foot IK 可達範圍內。

## 7. 工具與測試

- 選單 `Tools/Project/Stairs/Generate Movement Surface (Selected Stairs)`：量測 → 全部檢查通過才寫入
  （移動面子物件、樓梯 layer），**不通過就什麼都不寫**。可重複執行（更新同一個子物件）。
- 選單 `Tools/Project/Stairs/Validate Stairs In Open Scenes`：對每道已套用樓梯跑驗證器，並列出「剖面是樓梯
  但沒有移動面」的碰撞體。
- EditMode：`Assets/_Project/Tests/EditMode/StairAuthoringContractTests.cs`
  - `Rule_*`：合成剖面——兩種幾何同一規則、前緣連線高差 ∈ [0, 一級]、不均勻級高／斜坡／方向錯誤／
    **中段缺口**皆拒絕。
  - `Policy_*`：兩個 layer 存在、Player／Enemy 忽略踏面且碰撞移動面、移動面只與角色碰撞、
    **踏面仍與所有非角色 layer 碰撞**、`MotionDriver.MovementCollisionMask` 不看踏面／看移動面／排除自身、
    `RampPrototypeLayerSetup` 不存在。
  - `Masks_*`：§5 表格——**鎖住完整遮罩值**（不只兩個位元），並要求每個 prefab 帶著指定數量的查詢元件
    （少一個元件就代表表格有一列沒被檢查到）；表格數值本身也要與語意欄一致。
  - `Scene_*`：場景未存檔時回報 Inconclusive（驗證對象是磁碟上的場景）。
  - `Scene_RequiredStairs_AreAuthored`、`Scene_EveryAuthoredStair_SatisfiesContract`（I1–I8）。
  - `Scene_StairShapedColliders_WithoutMovementSurface_AreReported`：不擋驗收，但會列出來。

## 8. 前提與已知限制

- 只支援**直的、等寬、等級高、實心（踏面下到地面是實體）**的樓梯，碰撞體在樓梯根物件上。
  螺旋、懸空踏板、下方可穿越的樓梯不在範圍內（產生器會拒絕或需另案）。
- 樓梯必須貼在底端平台上（I6）。⚠️ 原 prototype 樓梯**懸空 0.302 m**（網格最低點 0.302、地面 0.000），
  入口第一級 0.497 m ＝ 2.5 級，產生器正確拒絕。修正方式是把樓梯放回地面（位移量＝入口第一級 − 級高，
  由量測決定），**不是**調整 ramp。✅ 2026-09-24 已下移 0.302 m，入口第一級變為 0.195。
- `Stairs (1)` 的入口第一級 0.223 vs 級高 0.194（ProBuilder 物件懸空 0.03 m），差 0.029 m，**剛好在容許值內**。
- 場景中另一道 ProBuilder `Stairs`（x≈7.9）入口第一級 0.229（差 0.035 m）不通過——本輪未要求處理。
- `Stairs_200x100x200_Prefab`（×2）的碰撞 mesh 本身就是斜坡（24.5°），產生器正確判定「不是階梯剖面」；
  它們的 Foot IK 會踩在斜坡上而非踏面，屬另一個問題，本規格不涵蓋。
- 本契約只推廣 **Walk 上下樓**（`docs/43` §4 已用 Play 證明的部分）。Run Down 的 `isGrounded` 流失、
  Run Up 的骨盆下潛、Walk 的腳步聲同腳連擊仍未解（`docs/43` §5–§9）。

## 9. 目前狀態與下一步

### 9.1 已套用（2026-09-24）

1. `TagManager`：layer 3 → `CharacterMovementSurface`、layer 8 → `StairTread`（沿用 index）。
2. `DynamicsManager`：§4 的碰撞矩陣。
3. prefab：§5 的遮罩（X Bot、Y Bot、Main Camera、Projectile_Fireball、ThrownProjectile）；
   自 `X Bot.prefab` 移除 `RampPrototypeLayerSetup` 並刪除腳本。
4. `MotionDriver`：膠囊偏移夾持改用碰撞矩陣推導的遮罩（§5 最後一列）。
5. 場景：還原診斷用的 `enableFootstepConsoleTrace` override；刪除手擺的 `Stairs_650_400_300_RampProto`；
   prototype 樓梯下移 0.302 m（量測值 ＝ 入口第一級 0.497 − 級高 0.195）使其貼地；對兩道樓梯執行產生器；存檔。

**驗證器結果（同一個工具、沒有逐物件參數）**：

| 樓梯 | 級數 × 級高 | 坡度 | 殘差 | 寬 | 入口第一級 | 頂端 | 移動面高差 | 偏離 |
|---|---|---|---|---|---|---|---|---|
| `Stairs_650_400_300_Prefab` | 18 × 0.195 | 29.68° | 1.3 mm | 3.000 | 0.195 | 開放邊緣 | [−0.001, 0.194] | 0 |
| `Stairs (1)` | 18 × 0.194 | 35.17° | 1.4 mm | 2.915 | 0.223 | 開放邊緣 | [−0.001, 0.218] | 0 |

`Stairs (1)` 最大高差 0.218 來自入口：地面比第一級踏面低 0.223 m，移動面在入口前方從地面爬升上去。

**EditMode**：649 個測試，648 通過、0 失敗、1 略過（既有的 NavMesh 測試，需 PlayMode）。
本規格的 25 個測試全數通過（含場景契約）。

### 9.2 Codex 審查（2026-09-24）

無 High。7 項 Medium 全部經讀碼確認並已修：
① 產生器改為交易式（寫入後跑完整驗證，不過就整組 Undo 還原）；
② 遺漏的移動類查詢：`MotionDriver` 膠囊偏移夾持（見 §5）；
③ 剖面缺口會被誤判為踢面 → 分析器拒絕缺口；
④ 頂／底平台只看中線 → 改為寬度 10%／50%／90%；
⑤ 遮罩測試只比兩個位元、元件缺席也會過 → 鎖完整值＋必要元件數；
⑥ 碰撞矩陣測試少了「踏面仍與非角色 layer 碰撞」 → 補上；
⑦ 場景測試受其他開啟場景／未存檔狀態影響 → 量測只看本場景、未存檔回報 Inconclusive。
1 項 Low（I5 界線是帶容許值的，不是嚴格成立）→ 寫入 §6 說明。

### 9.3 人工 Play 驗收清單
- Player：兩道樓梯各做 Walk 上／下、Run 上／下；在 `Stairs (1)` 頂端走出邊緣會正常墜落。
- Enemy：讓 Y Bot 追著玩家上下兩道樓梯，腳落在踏面上、不浮在斜坡上。
- 相機靠近樓梯側面會被擋住；瞄準樓梯會打在踏面上；Ice 地面技能在樓梯上生成於踏面；
  投射物撞到樓梯；在樓梯旁嘗試 traversal 的行為與改動前一致。
