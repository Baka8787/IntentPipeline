# 23 — Traversal Motion Mapping／Entry／IK／Debug 模型審查（2026-09-13）

> **本檔性質**：一次性的 **model correctness review**，不是規格正本。
> Traversal 規格正本仍是 `docs/22-traversal-v1.md`；本檔的結論確認後才 fold back 進 `docs/22`。
>
> **本輪只做**：外部資料查閱 → repo 靜態追值 → Playtest 症狀對照 → 收斂方案。
> **未修改任何 runtime code、未修改任何 `.asset`／`.prefab`／`.meta`／場景、未執行 Git。**
>
> **審查前提被推翻**：`WORKLOG.md` 與 `docs/22` §12 把 V3 描述為「runtime／tests／docs 完成，只剩 Editor 接線」。
> 本輪核對磁碟後確認：**V3 的 contact／piecewise／IK 三條路徑在正式資產上全部無法到達**，
> 因此「只剩 tuning」不成立。詳見 §A、§D、§E。

---

## A. Executive Summary

以下 15 條全部有磁碟證據，逐條在 §C–§E 展開。

| # | 結論 | 證據位置 |
|---|---|---|
| **A1** | **三支正式 Bake 的 YAML 完全沒有 `Traversal` block**（`Bake_Vault1m`／`Bake_Climb1m`／`Bake_Climb2m` 序列化到 `BakedDuration` 就結束）。⇒ `Traversal.Enabled=false` ⇒ `HasValidAuthoredMarkers=false` ⇒ `HasValidBakedData=false` ⇒ `TraversalPlanBuilder.TryBuild` **第一個 guard 就 return false**。**Play 走的永遠是 V2 endpoint fallback。** | `Assets/ScriptableObjects/Motion/Bake_*.asset`；`TraversalStateParamsSO.cs:111-116` |
| **A2** | **最大的單一缺陷不是 contact，是 root／pose 的時間去同步。** endpoint 分支把整條 baked trajectory 壓進 warp window（`trajectoryNormalized = Lerp(warpStart, 1, t)`），但**沒有同步改動畫播放速度**。即使平台高度與 bake 完全吻合（correction = 0），root 相對 animation pose 的垂直偏差實測為：**Climb1m +0.82 m、Climb2m +0.53 m、Vault1m −1.02 m**。 | `TraversalWarpPlan.cs:425`；§E-2 數值表 |
| **A3** | Horizon Zero Dawn 對同一問題的做法是**改 playback speed**（"Modification of playback speed allows you to preserve original animation velocity"），不是只改 root 的播放頭。本 repo 缺的正是這一步。 | §B-3 |
| **A4** | **Vault1m 的 baked 水平總位移是 3.26 m**（含助跑與跑出），baked 垂直在 n≈0.48 就回到 0。以 `DesiredWallDistance=0.45 m` 的 entry band 進場時，水平 correction 需求 ≈ −2.4 m，**遠超 `maxHorizontalCorrection` 預設 1.5** ⇒ `TryCreate` return false ⇒ 整段退回 `ExecuteCommittedCurveMovement`（**零 warp**）。 | §E-2；`TraversalWarpPlan.cs:303-307` |
| **A5** | 三支 clip 的實際動作窗都很短：Climb1m 垂直動作只佔 n∈[0.20, 0.585]（之後 41.5% 是站定）；Climb2m 佔 n∈[0.05, 0.45]（之後 55% 站定）；Vault1m 佔 n∈[0.15, 0.48]（之後 52% 是跑出）。而 warp window 預設 [0, 0.8]、recover 0.85 —— **修正窗比動作窗寬 2–5 倍**，結構性地把修正推到尾段。 | §E-2 曲線表 |
| **A6** | **Contact 數學本身是對的。** `root = T_world − R(yaw)·handInRoot` 與 UE 的 Bone Warp Point 同型（§B-1）。bake 端 `root.InverseTransformPoint(hand)` 與 runtime 端的 root frame 也一致（Animator 物件是 root 的 local-identity 子物件）。**KEEP。** | `TraversalStateParamsSO.cs:33-46`；`MotionBakeEditor.cs:881-913` |
| **A7** | 但 `SolveSingle` 回傳的 `LeftError／RightError` 是 `Distance(root + R·h, target)`，而 `root` 才剛由 `target − R·h` 定義 —— **恆等式，永遠是 0**。這是假指標，不是誤差量。真正的殘差只有在 `ClampOutsideWall` 與 shared-correction 之後、由 `CreateContactMeasurement` 量出來的那一份才算數。 | `TraversalStateParamsSO.cs:42, 76-79` |
| **A8** | **`TraversalHandIKController` 與 `TraversalHandIKRig` 在 Unity Editor 裡無法掛上。** 兩者分別是 `FootIKController.cs`／`FootIKRig.cs` 內的**第二個 MonoBehaviour class**；Unity 一個 `.cs` 只產生一個 MonoScript（與檔名同名的那個），其餘 MonoBehaviour 不會出現在 Add Component、也無法序列化進 prefab。全 repo **只有這兩處**違反此規則。`docs/22` §12.7 Gate 第 2 步不可執行。 | §B-5；`FootIKController.cs:804`／`FootIKRig.cs:103`；prefab 內無此二元件 |
| **A9** | **測試綠燈不等於能力存在。** `A49` 是純文字比對（`StringAssert.Contains`），A8 成立時它照樣全綠；31/31 traversal PlayMode 與 piecewise EditMode 全部用 `ScriptableObject.CreateInstance<MotionBakeData>()` ＋手工填的 `Traversal` block —— 驗的是**演算法**，不是**出貨資料**。 | `ArchitectureRegressionTests.cs:1474-1511`；`TraversalWarpPlanTests.cs:16, 207, 405` |
| **A10** | **P3 最可能的原因是一條完全沒有任何 debug 輸出的否決**：`TraversalSelectionPolicy.PreferTraversal`。它回 false 時不寫 reject reason、不進 `EntryEvaluation`、不上畫面 —— 畫面會顯示 `Climb1m` ＋ `Entry: Accept`，然後什麼都沒發生。實測門檻：`safeReach = AutoApexHeight(0.9535) × 1 × 1² − 0.15 = **0.8035 m**`，**任何量到 ≤0.80 m 的 ledge，Climb 一律靜默讓給普通 Jump**。 | `TraversalSelectionPolicy.cs:58-62`；`Bake_Jump_place_ALL_short.asset` |
| **A11** | 反過來，最常被懷疑的 `TooCloseUnsafe`／capsule penetration，用實際 `radius=0.12 / skinWidth=0.03` 計算**在物理上不可達**：貼牆時 longitudinal ≈ 0.17 m，而 close 下限是 0.05 m；capsule clearance ≈ +0.06 m。**不要往那裡修。** | §E-3 trace 表；`X Bot.prefab` CharacterController |
| **A12** | 真正與「貼牆」相關的是兩條**角度／側向**容差：`MaximumFacingAngle = 35°`、`MaximumLateralError = 0.2 m`。兩者都以 `forwardHit.normal` 當牆面法線 —— 斜角靠近、打到箱體稜角、或站定瞬間 facing 尚未收斂時會超界。 | `TraversalSelectionPolicy.cs:326, 349-352` |
| **A13** | **模型層次的差異**：Horizon 用「obstacle contact position 與 animation contact position 的差」當 **transition scoring**；本 repo 用**硬性 reject**。系統沒有「次佳選擇」，所以任何不合格都直接退化成普通 Jump —— 這正是 P3 的體感。 | §B-3 vs §C |
| **A14** | **Debug 不是「只有文字」。** Probe 已經畫了 step rays／top probe／depth probe／wall＆top normal／ledge interval／corridor 三段／destination capsule；MotionDriver 已經畫了 original vs warped trajectory＋六個 knot＋hand target＋requested/blocked 向量。**真正的問題是：這些幾乎全在 `OnDrawGizmos`（Scene View only），Game View 只剩三塊互相重疊的 `TextMesh`**；而且 Gizmo 畫的 piecewise 內容在 endpoint fallback 下**全部是空的**（`DrawTraversalKnot` 第一行就 `if (!IsPiecewise) return`）。 | §H |
| **A15** | `TraversalCollisionProfile` 在 `TraversalStateParams.asset` 完全未 author ⇒ `enableCenterShift=false` ⇒ `ApplyTraversalCollisionProfile` 每幀是 **no-op**。`docs/22` §12.4 的「C1 已接入」在資料上等於未啟用。 | `TraversalStateParams.asset`；`MotionBakeData.cs:62-66` |

---

## B. External Reference Findings

每一則分三段：**Source fact**（可引用的事實）／**對本專案的意義**（我方推論）／**不應照搬的部分**。

### B-1 Unreal Engine — Motion Warping（官方文件）

**Source fact**

- Motion Warping 由 **Motion Warping Component** ＋ 動畫 Montage 上的 **Motion Warping AnimNotifyState**（定義 warp window）兩層組成。
- 兩種 Root Motion Modifier：
  - **Scale Warp** — "alters an animation's scale in a uniform manner"（等比縮放播放速度與距離）。
  - **Skew Warp** — "warps the game object's root motion so that it matches the animation's location and rotation in the level **at the end of the warping window**"。
- **Warp Target** 是具名的 location／rotation 配對，runtime 以 `Add or Update Warp Target` 註冊；`follow_component` 決定 warping 期間 target 是否持續更新。
- **Warp Point Anim Provider** 三選一：`None`／`Static`（notify 上直接填的 transform）／**`Bone`**（由 `warp_point_anim_bone_name` 指定的骨骼）。
- 其他關鍵旋鈕：`ignore_z_axis`（**明確允許排除垂直 warp**）、`rotation_type`（Default＝對齊 target 旋轉／Facing＝朝向 target）、`warp_rotation_time_multiplier`（旋轉可比 window 更早收斂）、`warp_max_rotation_rate`。

**Bone Warp Point 到底在算什麼（白話 ＋ 數學）**

一般的 warp 是「讓 **root** 在 window 結束時落在 target」。Bone Warp Point 把這句話換成「讓 **某根骨頭** 在 window 結束時落在 target」。

做法是把那根骨頭的位置**表示成「相對於同一瞬間 root 的偏移」**，再反解 root：

```
在動畫自己的座標系裡，window 結束那一瞬間：
    R_anim   = root 的 transform
    P_anim   = warp point（Bone 模式下＝該骨骼的 transform）

先把 warp point 換算成「相對 root」：
    P_rel    = R_anim⁻¹ · P_anim        ← 這就是「手相對於根」

世界端要求：
    R_world · P_rel = T_world           ← 手要落在世界目標上

於是：
    R_world  = T_world · P_rel⁻¹
```

白話：**「我知道這支動畫在那一格，手離根有多遠、朝哪邊。既然手必須貼在世界上這個點，根就只能待在『那個點退回這段偏移』的位置。」**
`Static` 模式只是把 `P_rel` 從骨骼改成手填的常數 transform。

**對本專案的意義**

- 本 repo 的 `TraversalRootConstraintSolver.SolveSingle` —— `root = worldTarget − Quaternion.Euler(0, yaw, 0) · handInRoot` —— **就是上式的 position-only、yaw-only 版本**。方向正確，**KEEP**。
- UE 是**每個 window 結束時精確落在該 window 的 target**，多個 target 靠**串接多個 window**達成。本 repo 的六 knot piecewise 在數學上等價（每個 knot 時刻 correction 恰好命中該 knot 的 target，knot 之間 smoothstep 內插）。**這個模型是對的，KEEP。**
- `ignore_z_axis` 的存在證明：**成熟系統把垂直當成可以獨立開關的軸**，而不是和水平綁同一條權重。本 repo 的 endpoint 分支水平用加法、垂直用乘法，兩軸模型不一致（§E-2）。

**不應照搬的部分**

- UE 的 Warp Target 可以 `follow_component` 逐幀更新。本 repo 的 **commit-once** 是刻意的架構決定（ADR-008／`docs/22` §11.3），不要為了像 UE 而改成逐幀回讀 Probe。
- 不要引進 `MotionWarpingComponent` 這種「通用 warping framework」抽象 —— 目前只有三支 traversal 一個使用者，CLAUDE.md「第二個使用者出現前不得建立 production abstraction」仍有效。

---

### B-2 Gears of War 4 — *Motion Warping: Doing More with Less*（GDC 2017, Steven Dickinson, The Coalition）

**Source fact**

- 問題陳述：在大型 AAA 裡，「為了在不平整、動態的環境中保持角色貼地」所需的動畫資產量「can be crushing」。
- 解法：把過去**針對個別動作 ad-hoc 的 warping**，一般化成**暴露給動畫師的 warp points**，成為可重用的 trajectory modification framework。
- 效果：**更少的動畫資產、更高的視覺保真度**；相較 blend-space 做法，warp-point 驅動的 traversal 系統**速度快兩倍以上**（SIGGRAPH 2017 *High Performance Animation in Gears of War 4* 摘要）。
- 關鍵取捨：blend 做法會把動畫**限制在相近的風格與時序**；warp point 不會。

**對本專案的意義**

- Gears 的核心價值主張是「**用少量動畫涵蓋大量環境變異**」。本 repo 只有 3 支 clip、卻要涵蓋任意高度／深度的障礙 —— **這正是 warp point 最該發揮的場景**，方向正確。
- 「warp point 暴露給動畫師」對應本 repo 的 `TraversalMotionBakeBlock` authored markers。**概念對，但目前三支正式 Bake 一格都沒填**（A1）。

**不應照搬的部分**

- Gears 的 warp point 是**一整套 animation authoring pipeline**（動畫師在 DCC／Editor 裡標點）。本 repo 的 CLAUDE.md 明訂 AnimationClip 不可變、FBX 子 clip 是唯一真相 —— 標記只能放在 `MotionBakeData` 這一側。目前的做法（marker 在 MotionBakeData、空間值由 `MotionBakeEditor` 自動採樣）是對的，**不要改成加 Animation Events 或改 clip**。
- **不要把「概念名字」當成「數學模型已對齊」。** Gears 的 warp point 解決的是 trajectory 形狀；本 repo 目前壞掉的是 **root／pose 時間對齊**（A2），那是 Gears 這套東西之外的另一個問題。

---

### B-3 Horizon Zero Dawn — *Player Traversal Mechanics in the Vast World of HZD*（GDC 2017, Guerrilla Games）

**這是四份資料裡與本輪三個症狀最直接對應的一份。**

**Source fact（投影片原文重點）**

1. **定義**：Animation warping ＝ "Bending and stretching animated motion – **To reach a specific position at a specific time**"。好處：需要的動畫變體更少、播放中可調整目的地。
2. **Guided bone / Trajectory bone**："**Calculate offset of the guided bone to trajectory bone each frame and subtract this offset from the destination**." —— 任何骨頭都能當 guided bone。
3. **Warp Time Range**："**Warping looks best when feet are not touching ground**" —— warp 只在動畫的特定時間範圍內套用。
4. **Arrival Time**："Support **user defined arrival time**"（由動畫事件指定），"**Allowing for post arrival motion**" —— **動畫不必在結尾才抵達目的地**。
5. **Playback Speed**："**Modification of playback speed allows you to preserve original animation velocity**" —— warp 距離變成兩倍，播放速度就減半。
6. **Transition Selection**：評分＝"**Difference between obstacle contact position and animation contact position**" ＋ climbable 加分。
7. **Eligibility**："Allowed to vault? Schedule probes"；形狀分析＝"Raycasts with fixed offsets determine depth and height fluctuation"。
8. **職責切分**：**動畫決定 WHEN**（event-based，腳著地／肢體休止）；**程式決定 WHERE**（game state ＋ physics probe ＋ collision check）。

**對本專案的意義（逐條對照）**

| Horizon | 本 repo 現況 | 判定 |
|---|---|---|
| ①「在特定時間抵達特定位置」 | 六 knot piecewise 正是這個 | ✅ 對齊 |
| ② guided bone 每幀反推 offset | `SolveSingle` 只在 commit 時算一次。**在 commit-once 架構下這是合理簡化**（Probe 快照不變、hand-in-root 來自同一支 bake），差別是 Horizon 能吸收執行期的姿勢誤差，本 repo 只能靠 IK 收殘差 | ⚠️ 可接受的偏離，但**必須有 IK 收尾**，否則殘差無處可去 |
| ③ warp 只在腳離地的時間範圍 | warp window 預設 **[0, 0.8]**，涵蓋整支 clip，包含站定段（Climb1m 最後 41.5%）| ❌ **這是 P2 的結構成因之一** |
| ④ arrival time 由動畫事件指定、允許 post-arrival motion | `recoverNormalizedTime` 預設 **0.85**，幾乎是結尾；`exitNormalizedTime` 從未 author | ❌ **這正是「最後才修正到平台高度」的定義** |
| ⑤ 改 playback speed 保留原始速度 | **完全沒有**。取而代之的是改 root 播放頭（`trajectoryNormalized`），pose 不動 | ❌ **這是 A2，本輪最大缺陷** |
| ⑥ 以 contact position 誤差評分選 transition | `TraversalSelectionPolicy` 只比 jump apex vs height；`TraversalEntryPolicy` 只做 **reject**，不做 **score** | ❌ **這是 P3 的模型成因**（A13） |
| ⑦ 固定 offset raycast 量 depth／height | `TraversalProbe` 的 step scan ＋ top probe ＋ depth probe 正是這個 | ✅ 對齊 |
| ⑧ 動畫決定 WHEN、程式決定 WHERE | `MotionBakeData.Traversal` marker ＝ WHEN、`TraversalLedgeFrame`／`Corridor` ＝ WHERE。**架構分工正確**，但 WHEN 那一半是空的 | ⚠️ 架構對、資料缺 |

**不應照搬的部分**

- Horizon 的 warp target 逐幀重算、可在播放中改目的地。本 repo 的 commit-once 是刻意決定，**不要因為 Horizon 這樣就改**；改的應該是「commit 的內容是否足夠」，不是 commit 的時機。
- Horizon 的 transition 庫有數十支動畫，評分才有意義。本 repo 只有 3 支 —— **評分要落地成「三選一 ＋ 一個保底」，不是引進通用 scoring framework**。

---

### B-4 Avatar: Frontiers of Pandora — *Obstacle Traversal in the Organic World of Pandora*（GDC 2024, Joel Nilsson, Ubisoft Massive）

**Source fact（公開摘要）**

- 目標：在極度密集的有機環境裡，讓移動「flowing and unobstructed as you would expect from a Na'vi」。
- 手段：**"highly generic and (almost) markup-free system for stepups and mantling"**，「relying on a **minimum of assumptions** while trying to **maximize freedom of movement**」。
- 動機：世界越複雜，**傳統 markup（手工標記）方案越不可行**。

> ⚠️ 深入的技術細節（capsule feasibility、standable region 判定）只在 GDC Vault 影片內，公開摘要沒有。本節只引用摘要可支撐的結論。

**對本專案的意義**

- 「maximize freedom of movement / minimum of assumptions」與本 repo 目前的 Entry policy 方向**相反**：本 repo 的 entry band 是 `[0.05, 0.80] m` 的窄帶 ＋ 35° facing ＋ 0.2 m lateral，**四道硬 reject 且沒有保底**，不合格就退化成普通 Jump（A13）。
- 「markup-free」支持一個具體建議：**`MotionBakeData.Traversal` 的六個 normalized time 不應該靠人手填**。bake pipeline 已經有 `IMotionFeatureAnalyzer` 框架，逐幀採樣 root world Y ＋ 雙腳 world Y（`MotionFeatureAnalysis.cs`）。垂直動作窗、腳離地窗都可以從既有採樣**自動推導**。詳見 §I-2。

**不應照搬的部分**

- Pandora 是**完全程序化**的 stepup／mantle（沒有固定 clip 對應固定高度）。本 repo 是 **fixed-clip ＋ warp**，這是刻意的規模取捨，**不要因為這場 talk 就轉向 procedural climbing**。

---

### B-5 Unity — 一個 `.cs` 只有一個 MonoBehaviour（官方文件）

**Source fact**

- Unity Manual, *Naming scripts*：「If multiple classes are defined in your script, Unity selects the class with the same name as the file.」
- 推論（Unity 的 MonoScript 模型）：一個 `.cs` 檔只產生一個 MonoScript，即與檔名同名的那個類別。其餘 MonoBehaviour 類別**不會出現在 Add Component**，也**無法被序列化進 scene／prefab**。

**對本專案的意義**

- `Assets/Scripts/Presentation/IK/FootIKController.cs` 內含 `FootIKController`（主）＋ **`TraversalHandIKController`（次）**。
- `Assets/Scripts/Presentation/IK/FootIKRig.cs` 內含 `FootIKRig`（主）＋ **`TraversalHandIKRig`（次）**。
- 全 repo 掃描：**只有這兩處**的 MonoBehaviour 類別名與檔名不符。
- ⇒ `docs/22` §12.7 Gate 第 2 步（「在 Player Root 掛 `TraversalHandIKController`，在 Animator 同物件掛 `TraversalHandIKRig`」）**不可執行**。

**驗證方式（10 秒）**：在 Unity 選任一 GameObject → Add Component → 搜尋 `TraversalHandIK`。如果搜不到，本條確認。

---

## C. Current Codex Architecture（repo 真實 call chain）

### C-1 資料流（依磁碟，不是依文件）

```
Update ─ 順序 2.7 ──────────────────────────────────────────────
  TraversalProbe.Tick(data)                    [MonoBehaviour, Physics query 唯一擁有者]
    ├─ forward = MoveDirection ?: transform.forward      → DirectionSource{Move|Facing}
    ├─ !IsGrounded → 早退（gated measurement）
    ├─ step scan: Raycast × N  (從 feet+stepOffset 起，step 0.1，ceiling 2.7，dist 1.25)
    │     → hasForwardHit / forwardHit / firstClearHeight
    ├─ top probe:   Raycast 向下  → topHit          → height
    ├─ depth probe: Raycast 向下(edge + forward*0.6) → depth（二值：>0.6 或 <0.6）
    ├─ destination: CheckCapsule                    → destinationBlocked
    ├─ entryCapsuleWallClearance = dot(center−hit, n) − radius + skin
    ├─ MeasureLedgeInterval  (雙向 raycast，同 collider 條件)  → TangentMin/Max
    └─ ValidateFixedCapsuleCorridor  (Entry→Clearance→Transfer→Exit，每段 8 次 CheckCapsule)
  ↓ TraversalProbeMeasurement (readonly struct)
  TraversalClassifier.Classify(measurement, settings, previousKind)     [pure static]
    NotGrounded → NoForwardHit → NoValidTop → TopTooSteep → TooHigh
    → DestinationBlocked → CorridorBlocked → 高度分層 → 深度分 Vault/Climb1m
  ↓ TraversalCandidate (readonly struct, 每幀重算, 不進黑板)
  TraversalProbe.Candidate  [快取]

Update ─ 順序 4（FullBodyStateMachine.Tick）─────────────────────
  TraversalState.CanEnter(data)
    ├─ JumpRequested && IsGrounded && probe != null
    ├─ TraversalEntryPolicy.Evaluate(candidate, settings)   [pure static]
    │     InvalidCandidate / InvalidLedgeFrame / CorridorBlocked
    │     / TooFar / TooCloseUnsafe / LateralOutOfRange / FacingOutOfRange
    │     → probe.RecordEntryEvaluation(…)   ← ⚠️ 這是唯一會上畫面的 reject
    ├─ if (!evaluation.Executable) return false
    └─ TraversalSelectionPolicy.PreferTraversal(candidate, jumpParams, 0.15)
          ⚠️⚠️ **回 false 時完全靜默：不寫 reason、不上畫面、無任何紀錄**
    → _committedCandidate / _entryEvaluation 鎖存
  TraversalState.OnEnter(data)
    └─ CommitWarpPlan(_entryMotionDriver)
         ├─ TraversalPlanBuilder.TryBuild(...)          ← ❌ 正式資產一律 false（A1）
         │     guard: bake.Traversal.HasValidBakedData
         │     → hand targets → RootConstraintSolver → ClampOutsideWall
         │     → 6 × TryCreateKnot → TryCreatePiecewise
         └─ fallback: MotionDriver.TryCreateTraversalWarpPlan(...)   ← ✅ 實際走這條
               → TraversalWarpPlan.TryCreate(endpoint only)
         └─ motionDriver.BeginTraversalPresentation(plan)
            motionDriver.BeginTraversalCollisionProfile(profile)  ← profile 全零 = no-op

LateUpdate ─ 順序 6 ────────────────────────────────────────────
  TraversalState.OnUpdateMotion(motionDriver, animationFacade, data)
    ├─ isActuallyPlaying = facade.IsPlaying(AnimationKey)
    ├─ n = facade.GetNormalizedTime()
    ├─ motionDriver.ApplyTraversalCollisionProfile(n)     ← no-op
    └─ motionDriver.ExecuteTraversalWarpedMovement(plan, n, nPrev, data)
         ├─ plan.TryEvaluate(nPrev) / plan.TryEvaluate(n)   ← 絕對 pose
         ├─ delta = cur − prev                              ← 相對執行
         ├─ transform.Rotate(deltaYaw)
         └─ characterController.Move(delta)                 ← ⚠️ 開環，被擋不回授
              → LastTraversalExecutionResult (requested/actual/flags)  ← 只有 debug 讀

LateUpdate ─ 順序 6.5 ──────────────────────────────────────────
  PresentationPipeline.Tick → IPresentationController[]
    └─ TraversalHandIKController.Tick(data)        ← ❌ 元件掛不上（A8）
         !plan.IsPiecewise → Status = RootMappingUnavailable, weights = 0

Animator evaluation（Update 與 LateUpdate 之間，= 下一幀才吃到 6.5 寫的 target）
  TraversalHandIKRig.OnAnimatorIK(0)              ← ❌ 元件掛不上（A8）
```

### C-2 逐層屬性表

| 層 | class / struct | Owner | Input | Output | Physics? | Mutable? | Committed? | 每幀重算? | 真正進 execution? |
|---|---|---|---|---|---|---|---|---|---|
| Probe | `TraversalProbe` (MB) | 自己 | `PlayerRuntimeData`, `Transform`, `CharacterController` | `TraversalCandidate` | **✅ 唯一** | 快取欄位 | ❌ | ✅ | ✅ |
| Ledge | `TraversalLedgeFrame` (readonly struct) | Probe | topHit / wallNormal / tangent 掃描 | edge/tangent/normals/interval | — | ❌ | ❌ | ✅ | ✅（給 PlanBuilder；但 PlanBuilder 走不到） |
| Corridor | `TraversalCorridorEvidence` | Probe | 4 個 root sample | IsClear ＋ reason ＋ 4 點 | ✅（在 Probe 內） | ❌ | ❌ | ✅ | ✅（classifier reject） |
| 分類 | `TraversalClassifier` (static) | — | Measurement ＋ Settings ＋ previousKind | `TraversalCandidate` | ❌ | ❌ | ❌ | ✅ | ✅ |
| 入場合法性 | `TraversalEntryPolicy` (static) | — | Candidate ＋ Settings | `TraversalEntryEvaluation` | ❌ | ❌ | ✅（被鎖存） | 只在按鍵當幀 | ✅ |
| 選擇 | `TraversalSelectionPolicy` (static) | — | Candidate ＋ `JumpStateParams` | `bool` | ❌ | ❌ | ❌ | 只在按鍵當幀 | ✅ 但 **零可觀測性** |
| 狀態 | `TraversalState` | FSM | Candidate/Evaluation/Binding | 動畫鍵 ＋ 位移指令 | ❌ | ✅ | ✅ | ❌ | ✅ |
| 計畫（V3） | `TraversalPlanBuilder` (static) | — | Bake.Traversal ＋ Candidate ＋ Entry | 6 knot piecewise plan | ❌ | ❌ | ✅ | ❌ | **❌ 正式資產走不到** |
| 計畫（V2） | `TraversalWarpPlan.TryCreate` | — | Bake ＋ start ＋ target ＋ window | endpoint plan | ❌ | ❌ | ✅ | ❌ | ✅ **實際執行的就是這條** |
| 執行 | `MotionDriver` | 自己 | plan ＋ n ＋ nPrev | `CharacterController.Move` | ❌ | ✅ | — | ✅ | ✅ |
| 碰撞 profile | `TraversalCollisionProfile` | binding | normalizedTime | centerOffset | ❌ | ❌ | ✅ | ✅ | **❌ 全零 = no-op** |
| Hand IK | `TraversalHandIKController` | — | MotionDriver committed plan | `TraversalHandIKTargetData` | ❌ | ✅ | — | ✅ | **❌ 元件掛不上** |
| Hand IK Rig | `TraversalHandIKRig` | — | TargetData | `Animator.SetIK*` | ❌ | — | — | ✅ | **❌ 元件掛不上** |

---

## D. KEEP / FIX / REPLACE / NOT ACTUALLY IMPLEMENTED

| Component | Verdict | Evidence | Reason |
|---|---|---|---|
| `TraversalProbe` step scan ＋ top probe ＋ depth probe | **KEEP** | `TraversalProbe.cs:214-300` | 與 Horizon「固定 offset raycast 量 depth／height fluctuation」同型；Probe 是唯一 query owner 的紀律正確 |
| `TraversalLedgeFrame`（EdgeOrigin／Tangent／WallNormal／TopNormal／合法 interval） | **KEEP** | `TraversalCandidate.cs:20-61` | 這是唯一能支撐「手該摸哪裡」的幾何表示，且 Probe 只給幾何不選點，分層正確 |
| `TraversalClassifier` 純函式 ＋ hysteresis | **KEEP** | `TraversalClassifier.cs` | 純、可測、零 query。`TooSlow` 已成死枚舉值（classifier 不再產生），清掉即可 |
| `TraversalEntryPolicy` 純函式 ＋ 非對稱距離帶 | **FIX** | `TraversalSelectionPolicy.cs:285-372` | 數學與分層對，但（a）只 reject 不 score，沒有保底；（b）`FiniteNonNegative` 接受 author 0 為合法，`UnsafeCapsulePenetrationTolerance`／`LedgeEndMargin` **沒有程式層下限**（其餘五項用 `FinitePositive` 有）——是潛在陷阱 |
| `TraversalSelectionPolicy.PreferTraversal` | **FIX（優先度最高）** | `TraversalSelectionPolicy.cs:38-63` | 邏輯本身合理（低台讓給 Jump），但**回 false 時零輸出**。以實測 `safeReach=0.8035 m`，任何 ≤0.80 m 的 ledge 靜默失效。必須回 enum／evaluation，不能回 `bool` |
| `TraversalRootConstraintSolver.SolveSingle` | **KEEP** | `TraversalStateParamsSO.cs:33-46` | 與 UE Bone Warp Point 同型（§B-1）。數學正確 |
| `SolveSingle`／`SolveDual` 的 `LeftError／RightError` | **REPLACE** | 同上 `:42, 76-79` | 恆等式，永遠 0。假指標，會誤導診斷。真誤差只在 `CreateContactMeasurement` |
| `SolveDual`（雙手 centroid 解） | **NOT ACTUALLY IMPLEMENTED** | 全 repo 無呼叫點 | `TraversalPlanBuilder` 走的是「兩次 `SolveSingle` ＋ shared correction 平均」，`SolveDual` 從未被呼叫。死碼 |
| 六 knot piecewise 模型（Entry／L／R／Transfer／Exit／Recovery） | **KEEP（模型）／NOT ACTUALLY IMPLEMENTED（能力）** | `TraversalWarpPlan.cs:473-539`；`Bake_*.asset` | 模型與 UE 串接 window 等價、且 `TryEvaluatePiecewise` **不做時間重映**（正確）。但正式 bake 沒有 marker，永遠進不去 |
| `TraversalWarpPlan.TryEvaluate` **非 piecewise 分支** | **REPLACE** | `TraversalWarpPlan.cs:405-441` | 三個獨立缺陷疊加：①`trajectoryNormalized` 重映 root 時間但不改動畫播放速度（root／pose 差 0.5–1.0 m）；②垂直用乘法、水平用加法，兩軸模型不一致；③修正窗覆蓋整支 clip 含站定段 |
| `TraversalCollisionProfile` C0/C1 | **NOT ACTUALLY IMPLEMENTED** | `TraversalStateParams.asset` 無 `collisionProfile` | `enableCenterShift=false` ⇒ `EvaluateCenterOffset` 恆回 `Vector3.zero` ⇒ `ApplyTraversalCollisionProfile` 每幀 no-op |
| `TraversalCollisionProfile` C2 height／C3 radius | **KEEP（刻意 disabled seam）** | `MotionBakeData.cs:33-36` | 明確標 Reserved、runtime 不讀，這是誠實的 seam，不是假完成 |
| `TraversalBakedAnchor.RootLocalPosition` / `RootLocalYaw` | **NOT ACTUALLY IMPLEMENTED** | `TraversalStateParamsSO.cs:118-313` 只讀 `*HandInRoot` 與 `.IsFinite` | 採樣了、序列化了，但只當有效性 gate，數值從未使用 |
| `MotionBakeEditor.BakeTraversalAnchors` | **FIX** | `MotionBakeEditor.cs:835-913` | 自動採樣 hand-in-root 的設計正確（不改 FBX、不加 Animation Event）。缺陷：`InverseTransformPoint` 含 root **完整旋轉**（pitch/roll），runtime 只用 **yaw** 還原 —— 若 climb clip 的 root 有傾角，手會偏。應存 yaw-only 或連同旋轉一起存 |
| `TraversalHandIKController` / `TraversalHandIKRig`（程式邏輯） | **KEEP** | `FootIKController.cs:804-1085`；`FootIKRig.cs:103-145` | 只讀 committed plan、不查 Physics、不改 root、weight 安全歸零、狀態枚舉完整 —— 設計正確 |
| `TraversalHandIKController` / `TraversalHandIKRig`（作為能力） | **NOT ACTUALLY IMPLEMENTED** | §B-5；prefab 無此二元件 | 次要 MonoBehaviour 類別，Unity 無法掛載。**整條 IK 能力在 runtime 不存在** |
| `MotionDriver.LastTraversalExecutionResult` | **FIX** | `MotionDriver.cs:91, 476, 644` | requested/actual/blocked 有量到，但**沒有任何 runtime 消費者**（只有 debug 與一條 PlayMode 測試）。plan 是絕對 pose、執行是相對 delta，被擋掉的位移永遠不回授 —— 開環 |
| `TraversalRejectReason.TooSlow` | **REPLACE（刪）** | classifier 從不產生 | V3 移除 speed gate 後遺留的死值；debug label 仍為它保留一行 |
| `TraversalProbe` runtime LineRenderer debug | **KEEP** | `TraversalProbe.cs:654-800` | Game View 能看到的世界空間繪製只有這一套，是對的方向 |
| `MotionDriver` / `TraversalHandIKController` 的 `OnDrawGizmos` | **FIX** | `MotionDriver.cs:573-700` | 內容正確（original vs warped trajectory、六 knot、hand target、requested/blocked）但**只在 Scene View**；且 `DrawTraversalKnot` 第一行 `if (!IsPiecewise) return` ⇒ endpoint fallback 下全空 |
| 三塊 `TextMesh` 面板（Probe／MotionDriver／HandIK） | **REPLACE** | §H | 三個元件各自建 label、各自朝向相機、垂直位置 0.25／2.25／2.75 —— 會互相重疊；內容大量是 `Vector3` 全值 |

---

## E. Root Cause of Current Playtest Problems

### E-1 P1 — 手完全沒有可靠對到平台邊

**§5 要求的完整 call chain，逐段判定：**

| 段 | 檔案 / 方法 | 資料欄位 | 誰寫 | 誰讀 | 狀態 |
|---|---|---|---|---|---|
| 1. Hand marker | `MotionBakeData.TraversalMotionBakeBlock`（Inspector） | `contactMode` / `leftHandBone` / `rightHandBone` / `*ContactNormalizedTime` | 人（Editor） | Bake ＋ PlanBuilder | **❌ 三支正式 Bake 全空** |
| 2. Bake 採樣 | `MotionBakeEditor.BakeTraversalAnchors` | `leftHandInRoot` / `rightHandInRoot` | Bake tool | PlanBuilder | **❌ 被 `if (!authored.HasValidAuthoredMarkers) return authored;` 跳過** |
| 3. Plan Builder | `TraversalPlanBuilder.TryBuild` | `leftTarget` / `rightTarget` / `leftRootTarget` / `rightRootTarget` | — | — | **❌ 第一個 guard `!bake.Traversal.HasValidBakedData` 直接 return false** |
| 4. World Hand Target | `TraversalContactTargets` | `LeftHandWorldTarget` 等 | PlanBuilder | MotionDriver debug ＋ HandIK | **❌ `default`（全零）** |
| 5. Root Constraint | `TraversalRootConstraintSolver` | `RootPosition` | — | Knot | **❌ 未執行** |
| 6. Warp Plan | `TraversalWarpPlan.TryCreatePiecewise` | 六個 knot | — | MotionDriver | **❌ 未執行；走 `TryCreate` endpoint** |
| 7. MotionDriver | `ExecuteTraversalWarpedMovement` | `movementDelta` | MotionDriver | `CharacterController.Move` | ✅ 執行，但**輸入裡沒有任何手部資訊** |
| 8. Hand IK | `TraversalHandIKController` → `Rig.OnAnimatorIK` | weights / targets | — | Animator | **❌ 元件無法掛載（A8）；即使掛上，`!plan.IsPiecewise` ⇒ `RootMappingUnavailable`，weight = 0** |

> ### 目前 LeftHand／RightHand target 到底有沒有真正改變角色運動？
>
> # **No — 斷在第 1 段（資料），且第 8 段（元件掛載）另有第二道獨立的斷點。**
>
> 手部目標既不影響 root position、不影響 rotation、也不影響 IK。
> 畫面上會看到的 hand target Gizmo 在 endpoint fallback 下**連畫都不會畫**
> （`contacts.HasLeftHand` 為 false）。**目前沒有任何東西在冒充 IK —— 是整條都沒有。**

**但即使把 marker 補齊，手仍然對不準**，因為還有三個獨立問題：

1. **root／pose 差 0.5–1.0 m**（E-2）。手的世界位置由 pose 驅動、root 由 plan 驅動；root 錯半公尺時任何 hand target 都沒有意義。**必須先修 E-2。**
2. **`InverseTransformPoint` 存全旋轉、runtime 只還原 yaw**（§D）。climb clip 的 root 若有 pitch/roll，手就偏。
3. **`ClampOutsideWall` 會靜默犧牲手部精度**：`minimumRootWallDistance = entry.LongitudinalDistance − entry.CapsuleWallClearance`，以實際數值 ≈ `0.17 − 0.06 = 0.11 m`。任何要求 root 更靠近牆的 contact solve 都會被推回去，殘差交給 IK —— 但 IK 不存在（A8）。

### E-2 P2 — 垂直 Motion Warp（**本輪最重要的發現**）

**§8 問的是：現在的 vertical mapping 實際是 A（整條 baked Y curve scale）／B（window 內補 endpoint delta）／C（piecewise Y constraints）／D（其他）？**

> **答案：A ＋ 一個沒被命名的第四項 —— root 播放頭時間重映。**
> 而且**第四項的破壞力遠大於前三項**。

**沿執行數值確認（`TraversalWarpPlan.TryEvaluate`，非 piecewise 分支）：**

```csharp
// 生效參數（TraversalStateParams.asset 未 author ⇒ 全走 code 預設）
warpStart = 0.0    warpEnd = 0.8    maxHorizontal = 1.5    maxVertical = 0.75

t                    = (n − 0) / (0.8 − 0)                  = n / 0.8
warpWeight           = smoothstep(t)
trajectoryNormalized = Lerp(0, 1, t) = n / 0.8              ← ⚠️ root 時間 ≠ 動畫時間
horizontal           = Bake.GetHorizontalDisplacementAt(traj × Duration)
bakedVertical        = Bake.GetVerticalAt(traj × Duration)

vertical = useVerticalScale
    ? Lerp(bakedVertical, bakedVertical × verticalScale, warpWeight)   ← ⚠️ 乘法
    : bakedVertical + verticalCorrection × warpWeight                  ← 加法（僅 Vend≈0 時）
position = start + startForward × horizontal + horizontalCorrection × warpWeight + up × vertical
                                               ↑ 加法
```

**缺陷 ①（主因）：root 播放頭被壓進 warp window，但動畫播放速度沒變。**

`normalizedTime` 來自 `animationFacade.GetNormalizedTime()` —— 那是**動畫的**進度。
root 卻用 `n / 0.8` 去查 baked curve。root 比 pose **快 25%**。

**實測（平台高度與 bake 完全吻合、correction = 0、純粹只有這一項）：**

| n (動畫進度) | Climb1m pose Y | Climb1m root Y | **差** | Vault1m pose Y | Vault1m root Y | **差** |
|---|---|---|---|---|---|---|
| 0.10 | 0.000 | 0.000 | +0.000 | 0.000 | 0.000 | +0.000 |
| **0.20** | 0.008 | 0.627 | **+0.619** | 0.956 | 1.020 | +0.064 |
| 0.30 | 1.139 | 1.148 | +0.009 | 1.020 | 1.020 | +0.000 |
| **0.40** | 1.090 | 1.011 | −0.079 | 1.020 | **0.000** | **−1.020** |
| 0.50 | 1.011 | 1.013 | +0.002 | 0.000 | 0.000 | +0.000 |
| ≥0.60 | 1.013 | 1.013 | 0.000 | 0.000 | 0.000 | 0.000 |
| — | **峰值：root 高於 pose +0.817 m** | | | **峰值：root 低於 pose −1.020 m** | | |
| Climb2m | | | **root 高於 pose 最大 +0.527 m** | | | |

白話翻譯成畫面：
- **Climb1m**：n≈0.2 時 root 已經升到 0.63 m，但角色的身體還蹲在地上 —— 看起來就是**角色維持原動畫高度**，root 先跑掉。之後動畫追上來，在 n≈0.3 之後對齊。
- **Vault1m**：n≈0.4 時 root 已經落回地面，但動畫姿勢還騎在障礙上 —— **整整 1 公尺**的不一致。
- **這一切都發生在「平台高度完全正確、修正量為零」的情況下。** 這不是 tuning 問題，是模型缺陷。

> **Horizon 對這件事的答案很明確（§B-3 ⑤）：要壓縮／拉伸位移，就同步改 playback speed。**
> 只改 root 播放頭、不改動畫播放速度，必然產生 root／pose 去同步。

**缺陷 ②：垂直用乘法、水平用加法。**

`vertical = Lerp(V(t), V(t)×scale, w)` ⇒ 絕對修正量 = `V(t) × (scale−1) × w(t)`。
**修正量正比於動畫自己已經爬了多高。** 動畫前段 V≈0 ⇒ 修正量≈0；動畫後段 V 最大 ⇒ 修正量最大。
**這是結構性的後段集中**，與 `warpWeight` 的 smoothstep 疊加後更嚴重。

水平則是 `horizontalCorrection × w`（純加法，可以前置）。**兩軸模型不一致，沒有理由。**
UE 的 `ignore_z_axis` 證明垂直應該是**可獨立控制的軸**，而不是綁在動畫自己的 Y 上。

**缺陷 ③：修正窗遠寬於動作窗。**

| Clip | Duration | 垂直動作窗（n） | 動作後的靜止尾段 | warp window | recover |
|---|---|---|---|---|---|
| Vault1m | 2.900 s | [0.15, 0.48] | **52%**（跑出） | [0, 0.8] | 0.85 |
| Climb1m | 2.167 s | [0.20, 0.585] | **41.5%**（站定） | [0, 0.8] | 0.85 |
| Climb2m | 3.600 s | [0.05, 0.45] | **55%**（站定） | [0, 0.8] | 0.85 |

Horizon 的 **arrival time**（§B-3 ④）就是為了這件事存在：**抵達時刻由動畫指定，抵達後允許 post-arrival motion。**
本 repo 的 `recoverNormalizedTime` 預設 0.85 —— 幾乎是結尾，語意剛好相反。

**缺陷 ④：Vault1m 很可能連 endpoint plan 都建不起來。**

`Bake_Vault1m` 的 baked 水平總位移 = **3.259 m**（含助跑 ＋ 跑出）。
以 `DesiredWallDistance = 0.45 m` 進場、destination 約在 edge 前 0.6 m，`targetHorizontal ≈ 1.0 m`：

```
horizontalCorrection = 1.0 − 3.259 = −2.26 m   →  |−2.26| > maxHorizontalCorrection(1.5)
⇒ TryCreate return false  ⇒  plan.IsValid == false
⇒ ExecuteCommittedMotion 走 ExecuteCommittedCurveMovement（零 warp、純 baked 曲線）
⇒ 角色沿原始動畫往前衝 3.26 m
```

同時 `Bake_Vault1m` 的 `Vend = 0.0`（vault 結束回到地面高度），所以 `useVerticalScale = false`、走加法分支 —— **Vault 與兩支 Climb 的垂直模型根本不同**，這一點目前沒有任何文件記錄。

**P2 根因排序**：① 時間去同步（0.5–1.0 m）> ③ 修正窗過寬 > ② 乘法後段集中 > ④ Vault 建不起 plan。
**修 ① 之前，② 和 ③ 的 tuning 沒有意義。**

### E-3 P3 — 貼牆時反而不能爬

**§10 要求的 Close-wall Jump Decision Trace（以磁碟實際常數建立）**

場景假設：`X Bot`（`radius=0.12`／`height=1.827`／`skinWidth=0.03`／`stepOffset=0.3`／`slopeLimit=45`），
面向一面 1.0 m 高、深 > 0.6 m 的平台，緊貼牆站定（root 距牆面 ≈ 0.15 m），無移動輸入。

| # | 關卡 | 實際判定式 ＋ 生效常數 | 貼牆時的值 | 結果 | **與距離相關？** |
|---|---|---|---|---|---|
| 1 | `data.IsGrounded` | — | true | PASS | ❌ |
| 2 | forward 來源 | `MoveDirection` ?: `transform.forward` | 無輸入 ⇒ **Facing** | PASS | ⚠️ **是**（站定才會切換） |
| 3 | step scan | 從 feet+0.30 起，step 0.1，ceiling 2.7，dist **1.25** | 命中，`firstClearHeight ≈ 1.1` | PASS | ❌（越近越容易命中） |
| 4 | top probe | 由 `forwardHit + forward×0.02` 向下 | `topHit` ＝ 邊緣內 2 cm | PASS | ❌ |
| 5 | `height ≤ Climb2mMaxHeight(2.1)` | `dot(topHit − capsuleBottom, up)` | 1.00 | PASS | ❌ |
| 6 | `DestinationBlocked` | `CheckCapsule` @ destination | false | PASS | ❌ |
| 7 | `Corridor.IsClear` | Entry→Clearance→Transfer→Exit，各 8 次 `CheckCapsule`；**entryRoot = edge + wallNormal × (0.12+0.03+0.05) = 0.20 m** | 膠囊前緣距牆面 ≈ 0.06 m ⇒ clear | PASS | ❌ **entryRoot 與玩家位置無關**（只取玩家高度） |
| 8 | 分類 depth | `topHit + forward×0.6` 向下；`surfaceContinues` 容差 0.1 | 深平台 ⇒ `Climb1m` | PASS | ❌ |
| 9 | `LedgeFrame.IsValid` | `MeasureLedgeInterval` 需 `sample.collider == topHit.collider` | 單一 collider ⇒ OK | PASS | ❌（多 collider 平台會截斷） |
| 10 | `longitudinalError > MaximumFarError(0.35)` | `long = dot(root−edge, n) ≈ 0.17`；`err = 0.17−0.45 = −0.28` | −0.28 | PASS | ✅ 但**越近越安全** |
| 11 | **`TooCloseUnsafe`** | `−err > MaximumCloseError(0.4)` **或** `clearance < −tol` | `0.28 < 0.4`；`clearance = 0.15−0.12+0.03 = +0.06` | **PASS — 物理上不可達** | ✅ 但**證明不會是它** |
| 12 | `lateralError > 0.2` | `\|tangentCoord − clamp(...)\|` | 正對牆 ⇒ ≈ 0 | PASS | ⚠️ **斜角靠近會超界** |
| 13 | **`facingError > 35°`** | `Angle(facing, −wallNormal)`；`wallNormal` 來自 `forwardHit.normal` | 正對 ⇒ ≈ 0 | PASS | ⚠️ **斜角／稜角／轉身未收斂會超界** |
| 14 | **`PreferTraversal`** | `height > AutoApexHeight(0.9535) × 1 × 1² − 0.15 = **0.8035**` | `1.00 > 0.8035` ⇒ true | PASS | ❌ 但 **≤0.80 m 的 ledge 一律靜默 false** |
| 15 | FSM 仲裁 | Traversal priority **30** > Jump **10** | — | 進 Traversal | ❌ |

**結論（不猜，照數字說）：**

- **A11 — 把「貼牆 ⇒ TooClose」當前提是錯的。** 以 `radius=0.12 / skin=0.03`，貼牆時 longitudinal 最小 ≈ 0.17 m，close 下限是 0.05 m，capsule clearance ≈ +0.06 m。**這兩條在物理上都達不到。** 不要調 `maximumCloseError`。
- **剩下三個真正可能的關卡**，按可疑度排序：

  | 排序 | 關卡 | 為什麼與「貼牆 ＋ 站定」相關 | 目前可觀測性 |
  |---|---|---|---|
  | **1** | `TraversalSelectionPolicy.PreferTraversal`（#14） | 與距離無關，但**與量到的 height 有關**，且 **≤0.80 m 直接靜默否決**。若測試平台實測高度落在 0.80 附近，會呈現「有時可以有時不行」 | **零。畫面顯示 `Climb1m` ＋ `Entry: Accept`，然後什麼都不發生。** |
  | **2** | `facingError > 35°`（#13） | 走到牆邊停下時，facing 常常還在收斂；`wallNormal` 取自 `forwardHit.normal`，打到稜角時法線會跳 | ✅ 已顯示 `face=` 與 `FacingOutOfRange` |
  | **3** | forward 來源切換 Move→Facing（#2） | 站定才會發生，會整組改變 `forwardHit`／`topHit`／`depth`，可能翻轉 Vault↔Climb 分類 | ✅ 已顯示 `Direction: Move/Facing` |

- **最小 instrumentation（唯一缺的一項）**：
  `TraversalSelectionPolicy.PreferTraversal` 回 `bool`，被否決時**不留任何痕跡**。
  應改回一個含理由的結果（例如 `TraversalSelectionResult { PreferTraversal, Reason, SafeReach, CandidateHeight }`），
  由 `TraversalState.CanEnter` 一併交給 `_probe.RecordEntryEvaluation` 之類的既有通道顯示。
  **這一條加上去之後，P3 就不需要再猜了 —— 畫面會直接說是誰擋的。**

### E-4 IK

**不存在。** 兩層獨立的斷點：

1. `TraversalHandIKController` / `TraversalHandIKRig` 是次要 MonoBehaviour 類別，**Unity 無法掛載**（A8／§B-5）。
2. 即使能掛，`!plan.IsPiecewise` ⇒ `Status = RootMappingUnavailable` ⇒ weight 恆 0（A1）。

程式邏輯本身**沒有問題**（只讀 committed plan、不查 Physics、不改 root、殘差超限安全歸零、狀態枚舉完整）。
問題純粹是**檔案組織**與**資料缺失**。

### E-5 Debug UX

見 §H。一句話：**畫的東西是對的，但畫在 Scene View；Game View 只剩三塊互相重疊的字**，
而且所有 piecewise 相關的繪製在 endpoint fallback 下**全部被 early-return 掉**，
於是「看不到東西」被誤讀成「沒有資料」，而不是「走了另一條路徑」。

---

## F. Correct Motion Mapping Model

### F-1 先回答「現有詞彙要不要留」

**Entry／Contact／Transfer／Exit／Recovery 五個語意要留** —— 它們與 UE 的多 warp window、Horizon 的 contact + arrival time 都能對上，不是憑空發明。
**但要換三樣東西：時間的來源、垂直的處理方式、以及「抵達」的定義。**

### F-2 修正後的模型

```
                 ┌─────────────────── 由「資料」決定，不是由人猜 ───────────────────┐
                 │                                                                │
  Entry ─────────┼── Contact(L) ── Contact(R) ── Transfer ── **Arrival** ──────────┼─ Recovery ─ End
    n=0          │      ↑             ↑            ↑           ↑                  │
                 │  手離地窗       手離地窗     clearance   **垂直動作結束**        │
                 │                                          （bake 自動推導）      │
                 └────────────── warp 只在這一段作用 ─────────────────────────────┘
                                                              ↑
                                            Arrival 之後：root 鎖定 destination，
                                            動畫尾段只提供視覺，零 root correction
```

**五項具體改變：**

| # | 現在 | 改成 | 理由 |
|---|---|---|---|
| **F-a** | root 播放頭重映（`traj = n/warpEnd`），動畫速度不變 | **root 時間 ≡ 動畫時間**（`TryEvaluatePiecewise` 已經是這樣）。要壓縮位移就**改動畫 playback speed** | Horizon §B-3 ⑤。這是唯一能同時修好 root／pose 的做法 |
| **F-b** | `recoverNormalizedTime` 0.85 ≈ 結尾 | 引進 **Arrival**（≈ 現在的 Exit knot），定義為**垂直動作結束的那一格**（Climb1m≈0.585、Climb2m≈0.45、Vault1m≈0.48）。Arrival 後 root 鎖定 destination | Horizon §B-3 ④「arrival time ＋ post-arrival motion」。`TryEvaluatePiecewise` 的 Exit-hold 已經實作了這個語意，只是時間值沒人給 |
| **F-c** | 垂直 `Lerp(V, V×scale, w)`（乘法、後段集中） | 垂直改**加法 knot correction**，與水平同型；兩軸共用同一組 knot | UE `ignore_z_axis` 證明垂直應可獨立控制。piecewise 分支已經是加法，這條只是「刪掉 endpoint 分支」 |
| **F-d** | 六個 normalized time 由人手填（18 個數字 × 3 支 clip） | **由 bake 自動推導**（§I-2），人只負責覆寫與挑 hand bone | Pandora §B-4「markup-free」。`MotionFeatureAnalysis` 已有逐幀 root Y ＋ 雙腳 Y 採樣框架 |
| **F-e** | Entry 不合格 ⇒ 硬 reject ⇒ 退化成普通 Jump | **先 score 再 reject**：三支 clip 各算一個 fit 分數（contact position 誤差 ＋ 需要的 correction 量），取最佳；只有**全部**超過硬上限才 reject | Horizon §B-3 ⑥。這是 P3 體感的根治，不是調 tolerance |

### F-3 明確**不要**改的

- **commit-once 不改**（不要為了像 Horizon 而逐幀回讀 Probe）。
- **`MotionDriver` 是唯一 movement authority 不改**（不要為了修 drift 而讓 IK 或 plan 直接寫 transform）。
- **不建立通用 warping framework**（只有三支 clip 一個使用者）。
- **不改 FBX／AnimationClip／加 Animation Event**（CLAUDE.md 動畫資產鐵律）。

---

## G. IK Recommendation

### G-1 三個選項

| 方案 | 優點 | 缺點 | 本專案適配度 |
|---|---|---|---|
| **Animator IK（`OnAnimatorIK` ＋ `SetIKPosition/Weight`）** | ①**專案已有完整、已驗證的同款基礎設施**（`FootIKRig`／`FootIKTargetData`／`FootIKPoseData`，含「IK 套用前採樣原始 goal」避免反饋迴路的既有教訓）；②零新增套件依賴；③已與 Animancer 相容（`facade.SetApplyAnimatorIK(0, true)` 已在用）；④零 GC | ①只限 Humanoid；②只有 4 個 goal（雙手雙腳），無法做手指／多點接觸；③一幀延遲（既有文件已接受） | **✅ 推薦** |
| **Animation Rigging（`com.unity.animation.rigging`）** | ①任意骨骼、多點約束、chain IK；②Rig Layer 可分層開關；③權重曲線更靈活 | ①**目前未安裝**（`Packages/manifest.json` 只有 `com.unity.modules.animation`）——新增第三方套件依賴；②與 Animancer/Playables 的 RigBuilder 建圖順序有已知坑；③需要在 prefab 上建一整組 rig 物件（違反本專案「AI 不碰 prefab」的工作界線，全部得由使用者手工建）；④與既有 FootIK 兩套 IK 並存，多一層心智負擔 | ❌ 目前不值得 |
| **既有專案方案重用** | 就是 Animator IK 那一套 | — | 等同方案一 |

### G-2 推薦

> **維持 Animator IK，但把 `TraversalHandIKController` 與 `TraversalHandIKRig` 各自搬到同名檔案。**

理由：
1. **目前的程式邏輯完全正確**（§D）—— 它從來不是「選錯 IK 技術」的問題，是**檔案放錯位置導致元件無法掛載**。
2. 專案已經為 Foot IK 付過一次 Animator IK 的學費（`docs/05-foot-ik.md`、changelog v0.18.1 的腳踝抽搐反饋迴路），那些教訓在 `TraversalHandIKRig` 裡已經正確沿用（`OnAnimatorIK` 開頭先採樣未污染的 goal）。
3. Animation Rigging 的額外能力（多點約束、手指）**目前沒有使用者**。
4. **Hand IK 是 quality pass，不是 correctness 修復。** 在 root／pose 差 0.5–1.0 m 的情況下（E-2），任何 IK 都只會把手拉成怪異姿勢。**IK 必須排在 root 修好之後。**

**明確反對**：不要因為「以前 prompt 說最小 IK」就硬做 —— 但本輪的結論**獨立地**指向同一個答案，理由是既有基礎設施與零新依賴，不是慣性。

**旋轉權重**：第一輪保持 `handIKRotationWeight = 0`（只做位置）。原因見 §F-2 與下節 —— 手掌朝向需要 ledge tangent ＋ top normal，那是 §7 的問題，不是 IK 的問題。

---

## G-3 §7 — 左右手只有兩個 Position 夠不夠？

**判定：目前這個階段夠，但資料結構已經不夠，而且缺口不在「要不要 full IK」。**

| 需要的量 | 目前有嗎 | 是否已使用 |
|---|---|---|
| Ledge tangent | ✅ `LedgeFrame.Tangent` | ✅ 用來沿邊緣佈左右手 target |
| Wall normal | ✅ `LedgeFrame.WallNormal` | ✅ 用來定 `traversalForward` ＋ `ClampOutsideWall` |
| Top normal | ✅ `LedgeFrame.TopNormal` | ⚠️ 只用於 `Quaternion.LookRotation(forward, topNormal)` |
| authored hand separation | ✅ 由 `RightContactAnchor` 的左右手差算出 | ✅ `authoredSeparation` |
| character lateral offset | ✅ `centerCoordinate` 由 `entry.DesiredRootPosition` 投影 | ✅ |
| **hand orientation** | ⚠️ 有 `LeftHandWorldRotation`／`RightHandWorldRotation`，但**兩手共用同一個 `LookRotation(traversalForward, topNormal)`** | ❌ 送進 IK 的 rotation weight 預設會是 0 |

**結論**：幾何資料**已經足夠**（tangent／wall normal／top normal／separation 全都在 `TraversalLedgeFrame` 裡）。
真正不足的是兩點，兩點都**不需要 full IK**：

1. **兩手共用同一個旋轉**是明顯的簡化。真實抓握時左右手的掌心朝向會沿 tangent 鏡像。但在 `rotationWeight = 0` 的第一輪，這不會被觀察到 —— **先不要修**。
2. **ledge 不是直線時 tangent 只有一個值**。目前 `MeasureLedgeInterval` 沿 tangent 直線採樣，隱含「邊緣是直的」。圓柱／有機體會失效。**這是 Pandora 那場 talk 的主題，但本專案目前只有方塊平台 —— 不要提前做。**

**所以：不升級到 full IK，不加手指，不加多點約束。** 先把兩個 position target 真正送進 root constraint 與 Animator IK。

---

## H. Debug Visualization Redesign

### H-1 現況盤點（§13 的五個問題，逐條回答）

**1. 哪些文字應刪掉？**

| 位置 | 內容 | 處置 |
|---|---|---|
| `TraversalProbe.UpdateTraversalRuntimeLabel` | `Wall distance=… ideal=… far+=… close-=…`（四個容差常數每幀重印） | **刪** — 常數屬 Inspector，不屬 HUD |
| `TraversalProbe.OnDrawGizmos` Handles.Label | `current={Vector3} desired={Vector3}` | **刪** — 這兩點已經有線 ＋ 球在畫，數值多餘 |
| `MotionDriver.UpdateTraversalMotionRuntimeDebug` | `L animated={V3} target={V3} error={V3}`、`R …` 同樣三個 Vector3 × 2 手 | **刪** — 改畫誤差向量 |
| `MotionDriver.UpdateTraversalMotionRuntimeDebug` | `Y original=… warped=… platform=…` | **改** — 只留一個 `ΔY` 純量 |
| `MotionDriver.OnDrawGizmos` Handles.Label | `center={Vector3} h=… r=…`（CharacterController 參數每幀重印） | **刪** — 改畫實際 capsule |
| `TraversalHandIKController.UpdateDebug` | `L target={V3} L animated={V3} solved={V3}`、`R …` 共 6 個 Vector3 | **刪** — 三個點都該畫出來 |
| `GetTraversalDebugLabel` 的 `TooSlow` 分支 | classifier 已不產生 | **刪** |

**粗估**：目前三塊面板合計每幀輸出 **14 個 `Vector3` 全值 ＋ 11 個純量**。壓縮後應為 **0 個 Vector3 ＋ 6 個純量**。

**2. 哪些應改為 gizmo / LineRenderer？**

| 資訊 | 現在 | 改成 |
|---|---|---|
| 左右手 animated / target / error | 面板三個 Vector3 | **世界空間：animated ● ── error 向量 ── ◎ target**，長度即誤差 |
| current root vs desired entry root | 有線（✅），但同時印兩個 Vector3 | 保留線，**加 desired facing 箭頭 ＋ current facing 箭頭**，刪數值 |
| 合法 entry 距離帶 | **完全沒畫** | **在地面畫兩條沿 tangent 的平行線**（`MinimumWallDistance` / `MaximumWallDistance`），角色落在帶內＝綠、帶外＝紅 |
| lateral error | 混在 current→desired 那一條線裡 | **拆成兩段**：沿 wall normal 的 longitudinal 段 ＋ 沿 tangent 的 lateral 段（L 形），一眼看出偏哪個軸 |
| original vs warped trajectory | ✅ 已畫（Gizmos） | **搬到 LineRenderer**，Game View 也要看得到 |
| 六個 knot | ✅ 已畫，但 `if (!IsPiecewise) return` | **endpoint fallback 時也要畫**（至少畫 Entry／End 兩點 ＋ 一個明顯的「ENDPOINT FALLBACK」標記） |
| 實際 CharacterController capsule | ✅ 已畫（Gizmos, 紅色兩球） | 搬到 LineRenderer；**兩球改成完整 capsule 輪廓** |
| corridor 掃過的 capsule | 只畫 3 條中心線 | **畫被擋住的那一個 sample 的 capsule**（目前 `IsCapsuleSegmentClear` 一 false 就丟棄 `t`，應保留） |
| requested vs blocked 位移 | ✅ 已畫（白／紅線） | 保留，搬到 LineRenderer |

**3. 哪些需要同時顯示 original vs warped？**

- Root trajectory（已有，但只在 Scene View）
- **每一格的 root 位置**：`original ●` 與 `warped ◎` 同時畫，中間連線 —— 這樣 E-2 的 0.5–1.0 m 去同步**會直接在畫面上長出一條半公尺的線**，不需要看任何數字。
- **animated hand vs target hand**（同上）

**4. 哪些資訊現在根本沒有被 visualized？**

- ⛔ **合法 entry 距離帶**（`[MinimumWallDistance, MaximumWallDistance]`）
- ⛔ **facing 箭頭 ＋ desired facing 箭頭 ＋ 35° 容差扇形**
- ⛔ **ledge tangent 的方向**（只畫了線段，沒畫箭頭，看不出正負向）
- ⛔ **corridor 被擋住的那個 capsule sample**
- ⛔ **`TraversalSelectionPolicy` 的否決**（連文字都沒有 —— 見 A10）
- ⛔ **實際 pose 的手位置**（`TraversalHandIKPoseData` 在 IK 元件掛不上時根本沒被寫）
- ⛔ **animation 進度 vs root 進度的差**（E-2 的根因，目前完全不可見）

**5. 有沒有 Debug 顯示某 target，但 target 根本沒進 execution？**

**有，而且是最誤導的一項：**

| 顯示 | 實際 | 誤導程度 |
|---|---|---|
| `MotionDriver` 面板 `Traversal Piecewise V3 / Endpoint fallback` | ✅ **這一行是誠實的** —— 它正確顯示 `Endpoint fallback` | ✅ 沒問題 |
| `Hand IK: {Status}` | 顯示 `Not Bound`（元件掛不上）或 `RootMappingUnavailable` | ⚠️ 誠實，但**狀態名稱不足以讓人知道「元件根本掛不上」** |
| `contactText` 在 endpoint 下顯示 `Contact Mapping: unavailable (endpoint fallback)` | ✅ 誠實 | ✅ |
| **`DrawTraversalKnot` / hand target Gizmo 在 endpoint 下完全不畫** | 看起來像「畫面壞了」或「沒資料」 | ❌ **應改為畫一個明顯的「此路徑未啟用」標記**，而不是什麼都不畫 |
| `entryCapsuleWallClearance` 顯示 `capsule clearance=0.060` | 值正確，但**這個數字永遠不會導致 reject**（A11） | ⚠️ 會讓人往錯的方向調 |

**整體判定：現有 debug 沒有「假裝 target 有作用」的謊言 —— 它的問題是沉默與位置，不是欺騙。**
唯一真正的謊是 `SolveSingle` 的 `LeftError/RightError` 恆等於 0（A7），但那個值目前沒有上畫面。

### H-2 Debug 視覺規格

> **原則：空間資訊畫在空間裡。數值資訊才放面板。面板上不出現任何 `Vector3`。**

#### World-space（必畫）

**Environment 群組**
```
Wall hit point           ● 小球
Wall normal              → 箭頭（含箭頭尖，長 0.3 m）
Ledge edge line          ━━━ 沿 tangent，從 TangentMin 到 TangentMax
Ledge tangent            → 箭頭（標示正向，畫在 edge 中點）
Top normal               → 箭頭
Valid ledge interval     ┣━━━━┫ 端點加 T 形帽（含 LedgeEndMargin 縮減後的實際可用段）
```

**Entry 群組**
```
Current Root             ● 實心
Desired Entry Root       ◎ 空心
                         兩者之間 ── 但拆成 L 形兩段：
                           沿 wall normal 的 longitudinal 段（顏色＝距離帶判定）
                           沿 tangent   的 lateral 段     （顏色＝lateral 判定）
Legal entry band         ▒▒▒ 地面上兩條平行線（MinimumWallDistance / MaximumWallDistance）
Facing                   → 從 Current Root 出發
Desired facing           ⇢ 從 Current Root 出發（虛線或較細）
Facing tolerance         ∠ 以 desired facing 為中心的 ±35° 扇形（兩條邊線即可）
```

**Contact 群組**（每手一組）
```
Animated Hand   ●────────────◎  Hand Target
                 error vector
                 （線長＝誤差；超過 maximumResidualDistance 時換色）
```

**Motion Warp 群組**
```
Original Root Trajectory   ━━━ （24 段取樣）
Warped  Root Trajectory    ━━━ （不同色）
每個 knot 畫 ◎ 並標字母：  E  L  R  T  X  V
                          Entry / Left / Right / Transfer / eXit / recoVery
⭐ 新增：當前幀的 original ● 與 warped ◎ 同時畫並連線
         ← 這一條線的長度就是 E-2 的去同步量，直接可見
⚠️ endpoint fallback 時：畫 Entry 與 End 兩點 ＋ 一條標著
   「ENDPOINT FALLBACK — no contact/piecewise」的線，不要什麼都不畫
```

**Collision 群組**
```
實際 CharacterController capsule   ○──○ 完整輪廓（非只有兩顆球）
Planned destination capsule        ○──○ 另一色
Corridor sampled capsules          只在 blocked 時畫「被擋住的那一個 sample」
Requested displacement             ─→ 白
Blocked  displacement              ─→ 紅（＝ requested − actual）
```

**IK 群組**（只有在 IK 真的 active 時才畫）
```
IK Target      ◎
Animated hand  ●
Solved hand    ✕
error line     ●──◎  ＋  ✕──◎
weight         在 target 旁一個小字 "w=0.82"
⛔ IK 未啟用／未掛載時：一律不畫任何 target，避免冒充
```

#### Compact panel（只留這些，**零 Vector3**）

```
Traversal: Climb1m                    ← 或 "None: CorridorBlocked"
Plan:      ENDPOINT FALLBACK          ← 或 "Piecewise V3"
Entry:     ACCEPT                     ← 或 "REJECT — FacingOutOfRange"
Select:    TRAVERSAL                  ← ⭐ 新增：或 "NORMAL JUMP (height 0.78 < reach 0.80)"
Phase:     Contact      t: 0.34
Root Δ:    +0.62 m                    ← ⭐ 新增：root 相對 pose 的垂直去同步
L Hand:    6.2 cm       R Hand: 4.8 cm
IK:        NOT BOUND                  ← 或 "ON 0.82"
```

Reject 時：
```
Traversal: Climb1m
Entry:     REJECT — TooFar
Distance:  +0.31 m  (band 0.05–0.80)
```

#### 三塊面板合併

目前 `TraversalProbe`（y+0.25）／`TraversalHandIKController`（y+2.25）／`MotionDriver`（y+2.75）
各自建立 `TextMesh`、各自朝向相機。**應合併為單一面板**，由其中一個擁有者持有、其餘透過既有唯讀屬性取值
（`probe.Candidate`／`probe.EntryEvaluation`／`motionDriver.ActiveTraversalPlan`／`handIK.Status` 都已是 public）。

---

## I. Minimal Repair Plan

> **排序依據＝本輪找到的根因，不是舊的 WP1/WP2/WP3。**
> 每一階段都標明「必須等前面驗證」還是「可以並行」。

### R1 — 讓「為什麼沒發生」變得可見（**第一個，因為它最便宜且解鎖 R2 的判斷**）

1. `TraversalSelectionPolicy.PreferTraversal` 改回含理由的結果（`bool` → 結果 struct，含 `CandidateHeight` / `SafeReach` / `Reason`）；`TraversalState.CanEnter` 把它送進既有的 debug 通道。
2. Panel 加 `Select:` 與 `Root Δ:` 兩行（H-2）。
3. `DrawTraversalKnot` / hand target Gizmo 在 `!IsPiecewise` 時改為畫「路徑未啟用」標記，不要靜默 early-return。

**代價**：小。**解鎖**：P3 不再需要猜；E-2 的去同步量變成畫面上一條線。
**並行性**：可與 R2 同時做（不同檔案）。

### R2 — 修 root／pose 時間去同步（**P2 的根因，也是 P1 的前置**）

1. `TraversalWarpPlan.TryEvaluate` 的非 piecewise 分支：**移除 `trajectoryNormalized` 重映**，root 時間 ≡ 動畫時間。
2. 若仍需要壓縮／拉伸位移，改由 **animation playback speed** 承擔（Horizon §B-3 ⑤）—— 這會動到 `AnimancerFacade`／TransitionAsset 的播放速度，屬 Presentation 層，**需要先確認 `AnimationFacadeBase` 有沒有設定 speed 的 seam**。
3. 垂直改為與水平同型的加法 correction（刪掉 `useVerticalScale` 乘法分支）。

**必須等**：R1 完成後，用畫面上的 `Root Δ` 線驗證修好了。
**⚠️ 這一步會改 runtime 行為，需要使用者 Play 驗證。**

### R3 — 讓 piecewise 路徑真正可達（**P1 的必要條件**）

1. **`MotionBakeData.Traversal` 的六個 normalized time 改為自動推導**（§F-2 F-d）：
   在既有 `IMotionFeatureAnalyzer` 框架加一個 traversal analyzer，從已採樣的 root world Y ＋ 雙腳 world Y 推出：
   - **垂直動作窗**（root Y 開始上升／停止變化）→ Entry / **Arrival**
   - **雙手離開 rest 的窗**（需要加採樣手骨 Y，與腳同一趟迴圈）→ Left / Right Contact
   - **Transfer** ＝ root Y 達到 ledge 高度 ＋ clearance 的那一格
   人只需要挑 `contactMode` 與 hand bone，並在必要時覆寫。
2. 修 `MotionBakeEditor.SampleTraversalAnchor` 的旋轉空間（存 yaw-only 或連旋轉一起存），與 runtime 的 `Quaternion.Euler(0, yaw, 0)` 對齊。
3. 移除假指標 `SolveSingle`／`SolveDual` 的 `LeftError/RightError`；刪掉從未呼叫的 `SolveDual`。
4. **重烘三支 Bake**（使用者在 Editor 執行）。

**必須等**：R2 驗證通過。在 root 差 0.5–1.0 m 時把 contact 接上只會產生更難診斷的疊加誤差。

### R4 — 讓 Hand IK 元件能掛上

1. `TraversalHandIKController` 移到 `Assets/Scripts/Presentation/IK/TraversalHandIKController.cs`
2. `TraversalHandIKRig` 移到 `Assets/Scripts/Presentation/IK/TraversalHandIKRig.cs`
3. `ArchitectureRegressionTests.A49` 的 `FindSingleScript("FootIKController.cs")` 要跟著改檔名
4. **新增一條架構不變量**：掃描所有 `Assets/Scripts/**/*.cs`，斷言**每個 MonoBehaviour 類別名必須等於檔名** —— 這正是 CLAUDE.md「Test-as-Spec」說的：把不變量寫成測試而不是散文，drift 會讓它紅。
5. 使用者在 prefab 掛上兩個元件（**AI 不碰 prefab**）。

**可並行**：R4 的 1–4 與 R2／R3 無衝突，可同時做；第 5 步併進同一次 Editor Gate。

### R5 — Entry 從 reject 改為 score（**P3 的根治**）

在三支 clip 之間以 fit 分數選擇（contact position 誤差 ＋ 需要的 correction 量），只有全部超過硬上限才 reject。

**必須等**：R3 完成（沒有 contact position 就沒有分數可算）。

### R6 — Debug 視覺重做（§H-2 完整規格）

**可並行**，但 IK 群組要等 R4、Contact 群組要等 R3。

### R7 — Collision representation（**最後**）

見 §J。

---

## J. What NOT to Touch Yet

> **只列會妨礙根因診斷的。不排除高品質 Vault／Climb 本身需要的技術。**

| 項目 | 為什麼現在不要碰 |
|---|---|
| **`TraversalEntryPolicySettings` 的距離容差**（`desiredWallDistance`／`maximumFarError`／`maximumCloseError`） | §E-3 已用實際 capsule 數值證明 `TooCloseUnsafe` 在物理上不可達。現在調它只會遮蔽真正的關卡（`PreferTraversal` 靜默否決 ／ facing 35°）。**R1 之後畫面會直接說是誰擋的，屆時再調。** |
| **`TraversalCollisionProfile` C1 centerOffset** | 它現在是 no-op（A15）。在 root trajectory 還差 0.5–1.0 m 時開 center shift，會把兩個誤差疊在一起，無法分辨。**R2 驗證通過後再評估。** |
| **C2 heightScale / C3 radiusScale** | 同上，且它們目前是誠實標記的 disabled seam —— 保持這樣。 |
| **hand rotation weight**（`handIKRotationWeight`） | 位置都還沒對上，先談朝向沒有意義。第一輪保持 0。 |
| **`SolveDual` 的雙手聯合解** | 從未被呼叫；先確認單手 solve ＋ shared correction 在真實資料上的行為，再決定要不要真的用 centroid 解。 |
| **`MeasureLedgeInterval` 的曲線／有機體支援** | 目前只有方塊平台。Pandora 的 markup-free 是對的方向，但**不要提前做**（CLAUDE.md：第二個使用者出現前不得建立 production abstraction）。 |

**明確**不**在此列（＝這些是 Vault／Climb 做好本來就需要的，不要排除）：

- 六 knot piecewise 模型本身（模型正確，只是走不到）
- Hand contact constraint（P1 的正解，只是資料與元件缺失）
- Hand IK（quality pass，只是順序在 root 之後）
- Ledge frame／corridor evidence（幾何表示正確）
- Arrival time 的概念（正是 P2 的解，Horizon 的核心做法之一）

---

## Sources

**明確區分 `Source Fact`（可引用的外部事實）與 `Our Inference`（本文推論）：以上各節凡標「對本專案的意義」「判定」「結論」者皆為 Our Inference。**

1. **Epic Games** — *Motion Warping in Unreal Engine*（官方文件）
   https://dev.epicgames.com/documentation/en-us/unreal-engine/motion-warping-in-unreal-engine
2. **Epic Games** — *RootMotionModifier_Warp*（Python API，屬性完整表）
   https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/RootMotionModifier_Warp
3. **Epic Games** — *Pose Warping in Unreal Engine*（Orientation Warping／與 Motion Warping 的區別）
   https://dev.epicgames.com/documentation/en-us/unreal-engine/pose-warping-in-unreal-engine
4. **GDC Vault / The Coalition** — Steven Dickinson, *Motion Warping in 'Gears of War 4': Doing More with Less*（GDC 2017）
   https://gdcvault.com/play/1024219/Motion-Warping-in-Gears-of
5. **The Coalition / SIGGRAPH 2017** — David Bollo, *High Performance Animation in Gears of War 4*（warp-point 系統較 blend space 快兩倍以上）
   https://dl.acm.org/doi/10.1145/3084363.3085069
6. **Guerrilla Games** — Paul van Grinsven, *Player Traversal Mechanics in the Vast World of Horizon Zero Dawn*（GDC 2017，投影片）
   https://www.guerrilla-games.com/read/player-traversal-mechanics-in-the-vast-world-of-horizon-zero-dawn
   https://www.slideshare.net/guerrillagames/player-traversal-mechanics-in-the-vast-world-of-horizon-zero-dawn
7. **GDC Vault / Ubisoft Massive** — Joel Nilsson, *Obstacle Traversal in the Organic World of Pandora*（GDC 2024）
   https://gdcvault.com/play/1034304/Obstacle-Traversal-in-the-Organic
8. **Unity** — *Manual: Naming scripts*（一個 `.cs` 只選與檔名同名的類別）
   https://docs.unity3d.com/6000.5/Documentation/Manual/naming-scripts.html
9. **Unity** — *Manual: Inverse Kinematics*（Humanoid Animator IK 的前提與限制）
   https://docs.unity3d.com/6000.2/Documentation/Manual/InverseKinematics.html

**Repo 內部證據**（全部為本輪磁碟核對，非文件轉述）：
`Assets/ScriptableObjects/Motion/Bake_{Vault1m,Climb1m,Climb2m}.asset`、
`Assets/ScriptableObjects/StateMachine/{TraversalStateParams,JumpStateParams,PlayerStateMachineConfig}.asset`、
`Assets/Prefabs/X Bot.prefab`、
`Assets/Scripts/Core/Environment/{TraversalProbe,TraversalCandidate,TraversalClassifier,TraversalProbeSettings}.cs`、
`Assets/Scripts/Core/StateMachine/{TraversalSelectionPolicy,TraversalStateParamsSO}.cs`、
`Assets/Scripts/Core/StateMachine/States/TraversalState.cs`、
`Assets/Scripts/Presentation/Motion/{TraversalWarpPlan,MotionDriver,MotionBakeData}.cs`、
`Assets/Scripts/Presentation/IK/{FootIKController,FootIKRig,FootIKTargetData,FootIKPoseData}.cs`、
`Assets/Scripts/Editor/Stages/{MotionBakeEditor,MotionFeatureAnalysis}.cs`、
`Assets/_Project/Tests/EditMode/{ArchitectureRegressionTests,TraversalWarpPlanTests}.cs`。
