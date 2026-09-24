# 35 — 腳步偵測 Stage 0／1：量測、判準與門檻的推導

> **定位**：`Presentation/Footstep/` 子系統的 **Dev Spec 分卷**（依 `CLAUDE.md`「Subsystem specs get their own file」）。
> 跨切面契約（黑板 schema／管線順序）仍以 `docs/02-dev-spec.md` 為準。
> **前身**：`docs/34-footstep-audio-upgrade-study.md`（研究輪：素材、參考實作、原始診斷）。
> ⚠️ `docs/34` §4 的延遲分解**有一項被本輪推翻**，見下方 §1.2。
>
> **狀態**：Stage 0 ＋ Stage 1 已實作，EditMode **565 total／564 passed／0 failed／1 skipped**。
> ⛔ **尚未經人工 Play 驗收**（animation feel／音色／3D 聽感）。Stage 2 未開工。
>
> 🔄 **2026-09-24（使用者裁決，`docs/44` §9.5–§9.6）：節奏的量測基準改為動畫自己的 pose 平面。**
> `clearance` ＝ 腳底 − **與腳位同時擷取**的 pose 平面（`FootIKPoseData.PosePlaneY`），不再是「腳底 − 腳下地面」。
> 地面觀測只用於材質解析與 Airborne 否決。參考系因此不會跳，§3 的 ①「參考系不連續」與
> `GroundStepTolerance` 整段移除。原因：樓梯移動面上 root 比腳下踏面高 0～一級，舊基準讓踩住的腳看起來一直在下降，
> 下樓的落腳大多被吃掉。§2 的門檻本來就是從動畫量的（平地上兩種基準相同），數值不變。

---

## 1. 這一輪真正修掉的是什麼

### 1.1 一句話

> **舊偵測器不是「慢」，是「在錯的相位擊發」。**
> 它只看「腳的世界高度不再下降」。但一次跨步的垂直軌跡**不是單調的**，
> 而世界高度在樓梯上根本不代表腳與地面的關係。

### 1.2 ⚠️ 對 `docs/34` §4.2 延遲分解的修正

`docs/34` 把「等下降停止才擊發」估為 **0–17 ms 的延遲**。**這個估計是錯的**——
實際重放真實動畫後，各 loop clip 的擊發相對真實最低點是：

| clip | 舊版擊發相對真實觸地 |
|---|---:|
| WalkFwdLoop | **提前 ~340 ms，且擊發在擺盪中低點** |
| RunFwdLoop / SprintFwdLoop | 落後 17 ms（這一項原估計是對的） |
| WalkFwdStop_LU / _RU | **提前 67 ms / 283 ms** |
| RunFwdStop_LU | **提前 333 ms** |

**根因不是門檻沒調好，是判準選錯了。** 詳見 §2.3。
⇒ `docs/34` §4.2 的 **A/B 兩項作廢**；D（clip 前置靜音 29.7 ms）與 E（DSP buffer）與 C（管線一帧）不變。

---

## 2. 量測（所有門檻的唯一依據）

> 方法：在 `EditorSceneManager.NewPreviewScene()` 裡實例化**執行期真正使用的 rig**（`Assets/Prefabs/X Bot.prefab`），
> `applyRootMotion = true`，以 60 fps 逐帧 `SampleAnimation`，記錄
> 「腳底世界位置 − root 平面」與「腳的世界水平速度」。
> 探針為 throwaway（`CLAUDE.md` Spike/Probe Exception），取得數據後已刪除。
> `FeetBottomHeight = 0.0873`（L/R 相同，Avatar 常數）。

### 2.1 抬腳行程（公尺，相對地面）

| 類別 | 實測 | 代表案例 |
|---|---:|---|
| **真實跨步** | **0.120 – 0.656** | Walk 收步 0.120／WalkBwd 0.130／RunLt 0.153／Sprint 0.656 |
| **停步腳部調整**（Shuffle） | **0.029 – 0.037** | `WalkFwdStop_RU` R t=1.40 → 0.037 |
| **支撐腳的腳掌滾動** | **0.0149** | `WalkFwdLoop` 每個週期都會出現 |
| **停步 settling 抖動** | 0.0026 – 0.0173 | `RunFwdStop_RU` L t=0.97／1.10／1.40 |
| **Idle 呼吸** | **0.0015** | `Bake_Idle` 全 6.67 s |

### 2.2 垂直下降速度（m/s，峰值）

| 類別 | 實測 |
|---|---|
| 真實跨步 | **0.85 – 4.31** |
| 停步調整 | 0.40 – 0.60 |
| settling／滾動 | **0.03 – 0.28** |

### 2.3 ⭐ 腳的世界水平速度 —— 分離兩類事件的關鍵訊號

| 類別 | 實測 vXZ |
|---|---|
| **真正落腳** | **0.01 – 0.91 m/s**（踩住的腳在世界空間幾乎靜止，root 從它旁邊走過去） |
| **擺盪途中的低點** | **4.00 – 7.11 m/s**（約兩倍角色速度掠過） |

**分離度 4.4 倍。** 對照之下，用「離地高度」分離同樣兩類只有 **1.27 倍**
（真實落腳最高 0.030 m vs 擺盪中低點最低 0.044 m），完全不夠用。

> ### 這就是整輪最重要的一件事
> **選對訊號比調對門檻重要。** 舊版不是門檻設錯，是**用了一個本質上分不開這兩類事件的量**。
> 在 1.27 倍的分離度上調門檻，無論怎麼調都會同時有誤報與漏報。

### 2.4 場景幾何

| 項目 | 實測 |
|---|---|
| 樓梯級高 | **0.1937 / 0.1950 m**（`Stairs`、`Stairs_650_400_300_Prefab`，各 18 階，均勻） |
| 鏡頭與角色距離 | **≈1.92 m**（`ThirdPersonCamera.offset = (0.44, −0.39, −1.83)`） |

---

## 3. Stage 1 的判準

每隻腳一個 `FootPlantTracker`（值型別，零配置），兩態 `Planted ⇄ Lifted`，**per-foot 完全獨立**。

```
每帧輸入：腳底世界 XZ、腳底相對 pose 平面的高度、deltaTime
         （🔄 2026-09-24：pose 平面與腳位同時擷取，見上方狀態欄）

clearance = 腳底世界 Y − PosePlaneY       ← ⭐ 動畫本身的抬腳高度；root 的任何垂直位移都會相消
                                            （舊版＝腳底 − 腳下地面，在樓梯移動面上失效）

① ~~參考系不連續~~（🔄 2026-09-24 移除：基準不會再跳）
② Planted → Lifted：clearance 相對上次落腳抬升 ≥ ReArmLift(0.025)
③ 上膛：下降速度 ≤ −ArmDescentSpeed(0.25)            ← Schmitt 上軌
④ 候選：下降速度回升到 ≥ −FireDescentSpeed(0.05)      ← Schmitt 下軌
⑤ 資格三檢（缺一不擊發，但**保留上膛**）：
     · clearance ≤ ContactClearance(0.045)            ← 腳真的貼到地面了
     · 水平速度 ≤ PlantHorizontalSpeedMax(2.0)        ← ⭐ 不是擺盪中低點
⑥ 擊發並分類：抬腳行程 ≥ NormalStepLift(0.060) → Normal，否則 → Shuffle
```

### 3.1 門檻與其推導

| 參數 | 值 | 下界（必須接受） | 上界（必須拒絕） | 餘裕 |
|---|---:|---|---|---|
| `ReArmLift` | **0.025** | 最小真實調整 0.033 | 最大 settling 0.017 | 1.3× / 1.5× |
| `NormalStepLift` | **0.060** | 最小真實跨步 0.120 | 最大停步調整 0.037 | 2.0× / 1.6× |
| `ContactClearance` | **0.045** | 真實落腳最高 0.030 | — | 1.5× |
| `PlantHorizontalSpeedMax` | **2.0** | 真實落腳最快 0.91 | 擺盪中低點最慢 4.00 | 2.2× / 2.0× |
| `ArmDescentSpeed` | **0.25** | 最慢真實跨步 0.85 | 最快 settling 0.28 | 3.4× / 1.1× |
| `FireDescentSpeed` | **0.05** | — | — | Schmitt 下軌 |
| ~~`GroundStepTolerance`~~ | — | 🔄 2026-09-24 移除（基準不再跳） | | |

> ⚠️ `ArmDescentSpeed` 對上界只有 1.1 倍餘裕，是全部門檻裡最緊的一條。
> 它之所以可以這麼緊，是因為**它不是唯一防線**——0.28 m/s 的 settling 就算上膛，
> 也還要通過行程(0.025)、接觸帶(0.045)與水平速度(2.0)三道。**縱深防禦，不是單點門檻。**

### 3.2 ⛔ 刻意沒有做的事

* **沒有時間閘、沒有 cooldown。** 兩個參考實作都用固定 cooldown（dropecho 雙腳共用 0.33 s；
  FMOD-Unity-Tools 每腳 0.3 s）掩蓋偵測缺陷，代價是**步頻上限**——本專案 Sprint 的步頻會被直接吃掉。
  用 cooldown 蓋掉碎步，等於拿「跑起來會漏步」換「停下來不吵」。
* **沒有為了排除碎步而把行程門檻拉高到會漏掉正常 Walk。** `NormalStepLift` 對最小真實跨步保有 2.0 倍餘裕。

---

## 4. 與 Foot IK 的共用：`FootIKGroundData`

```
FootIKController.Tick（管線順序 6.5 步驟①）
   └─ SampleGround ×2  → Physics.Raycast（本來就每帧在發）
         └─ 轉發 → FootIKGroundData { Left/Right: HasGround, Point, Normal, GroundY, Collider }
                      owner = FootIKController（＝唯一 Writer，擁有權跟著寫入權走）
                          ▼ 只讀
FootstepDetector.Evaluate（管線順序 6.5 步驟②）
```

* **零額外 raycast。** 兩個參考實作都是「每次腳步事件再射 1～4 條 ray」；本專案複用已經發過的那條。
* **時序保證**：管線步驟①（所有 `IPresentationController`）**必定**在步驟②（所有 `IPresentationEventSource`）之前跑完
  ⇒ 讀到的恆為**本帧**的觀測，且**與 Hierarchy 順序無關**。
* **`Collider` 目前沒有 runtime 讀取方**——它是 Stage 2 Surface Resolver 的接點，先把邊界收好，
  避免屆時為了拿 collider 而在腳步系統另建一套重複 raycast。
* 🔄 2026-09-24：地面觀測**只用於材質與 Airborne 否決**。沒有 Foot IK 時節奏不受影響（材質退回 Unknown），
  `ColdGround_StillDetects` 覆蓋。

---

## 5. 驗證

### 5.1 自動化（EditMode，565 total / 564 passed / 0 failed / 1 skipped）

新增的回歸鎖直接編碼 §2 的實測數值，因此**不會因為有人「順手調個參數」而悄悄退化**：

| 測項 | 鎖住什麼 |
|---|---|
| `MidSwingDip_IsNotAPlant_AndTheRealPlantStillFires` | WalkFwdLoop 的完整軌跡：中低點不得擊發、真正踩地必須擊發 |
| `SupportFootToeRoll_ProducesNothing` | 支撐腳滾動 0.0149 m 不得出聲（否則步頻加倍） |
| `StopSettlingChatter_ProducesNothing` | 0.017／0.003／0.0015 的抖動全靜音 |
| `StopAdjustment_IsClassifiedAsShuffle` | 0.033 m ＋ 0.40 m/s ⇒ Shuffle，不得用正常腳步音 |
| ~~`ClimbingStairs_*`／`StairEdgeCrossing_*`／`RootPlaneJump_*`~~ | 🔄 2026-09-24 由下面兩條取代 |
| `Timing_IsInvariantToCommonVerticalTranslation` | 常數高度／每帧 −8 cm 斜坡／每 7 帧跳一級，擊發帧完全相同 |
| `Timing_IgnoresGroundObservation` | 踏面高度 0～0.4 m、每 2～3 帧換階、觀測時有時無，擊發帧與平地相同 |
| `FootStartingAirborne_StillProducesSteps` | 腳從半空開始時基準要能下修 |
| `HighHorizontalSpeed_SuppressesPlant` | 水平速度超標一律不算落腳 |
| `JustLanded_ForcesBothFeetBackToPlanted` | 落地後不得殘留上膛而多播腳步 |

### 5.2 真實動畫 ＋ 真實場景重放（不進 Play Mode）

**平地 loop（每腳每週期應恰好 1 次 Normal）**

| clip | L | R |
|---|---|---|
| WalkFwdLoop / RunFwdLoop / SprintFwdLoop | 3 Normal / 0 Shuffle | 3 Normal / 0 Shuffle |
| WalkBwdLoop / RunLtLoop / RunRtLoop | 3 Normal / 0 Shuffle | 3 Normal / 0 Shuffle |

**收步（Stop clip 單獨重放）**

| clip | 結果 | clearance | vXZ |
|---|---|---|---|
| WalkFwdStop_LU | 1 Normal | 0.005 | 0.06 |
| WalkFwdStop_RU | 1 Normal ＋ 1 Shuffle | 0.006 / 0.005 | 0.12 / 0.03 |
| RunFwdStop_LU | 2 Normal | 0.011 / 0.006 | 0.13 / 0.01 |
| RunFwdStop_RU | 2 Normal | 0.009 / 0.007 | 0.43 / 0.14 |
| **Idle（6.67 s）** | **0 事件** | — | — |

⇒ 每一次擊發的 clearance 都在 0.005–0.011 m（腳確實在地上）、水平速度 0.01–0.43 m/s（腳確實靜止）。**沒有碎步。**

**真實樓梯**（走上場景 `Stairs`，300 帧 / 8.08 m，逐帧對真實 MeshCollider raycast）

```
L73  R107  L133  R167  L194  R227  L253  R287      ← 8 步，完整左右交替
間隔：34 / 26 / 34 / 27 / 33 / 26 / 34 帧           ← 節奏均勻（平地步距約 30 帧）
```

⚠️ 修正前同一條路徑會多出 `L81`、`L142` 兩次（正常步距之外、間隔僅 8–9 帧）——那正是「樓梯腳步過密」。

### 5.3 ⛔ 這些驗證**不能**取代的

音色好壞、左右腳差異的聽感、3D 衰減是否合適、Shuffle 的音量是否得體、
以及「聲音與畫面是否同步」的最終判斷——**只有人工 Play 能回答**。

---

## 6. Stage 1.8：那一帧延遲的裁決

> **結論：不動 `PresentationPipeline` 的契約。而且 Stage 1 之後，動它的代價比 `docs/34` 評估時更高。**

`docs/34` §8-Q1 曾建議「①②對調即可省下 16.7 ms，且不破壞 ordering invariant」。
**Stage 1 引入 `FootIKGroundData` 之後，這個建議不再成立**：

* `FootIKController` 是 `IPresentationController`（步驟①），`FootstepDetector` 是 `IPresentationEventSource`（步驟②）。
* 對調之後，偵測器會在 Foot IK 更新地面觀測**之前**執行 ⇒ 讀到**上一帧的地面高度**。
* 而「地面高度慢一帧」正是樓梯上最致命的一件事——它會讓級緣的不連續落在錯的帧上。

⇒ **拿「音訊早一帧」去換「地面資料晚一帧」，在樓梯上是淨虧。**

若人工 Play 後仍認為 16.7 ms 可聞，選項是**三段式管線**
（產生觀測的 Controller → 事件來源 → 消費事件的 Controller），那是比對調大得多的契約變更，
應另開討論、不在本輪範圍。

**剩餘延遲預算（Stage 0+1 之後）**

| 項目 | 之前 | 現在 |
|---|---:|---:|
| clip 前置靜音 | 29.7 ms | **0 ms**（WASD Walk/Run 全 72 顆皆為 0） |
| 擊發相位 | **錯相位（Walk 提前 ~340 ms）** | **對齊真實觸地** |
| 管線一帧 | 16.7 ms | 16.7 ms（未動） |
| DSP buffer | 21–43 ms | 21–43 ms（**未動**，見下） |

⚠️ **`ProjectSettings/AudioManager.asset` 的 `m_DSPBufferSize: 1024` 本輪沒有改。**
它現在是剩餘延遲中**最大的一項**，改成 256（"Best latency"）可再省約 16–32 ms，
但那是**全域設定**、影響所有音效、以 CPU 換延遲 ⇒ 需要單獨裁決與單獨驗證。

---

## 7. 已知缺口（Stage 2）

| 缺口 | 說明 |
|---|---|
| **⭐ gait 未分池** | 目前 Walk/Run/Sprint **共用同一組 Walk 素材**。Run 的 6 顆已匯入但未接線——`AudioEventId` 加兩格＋依 `MoveSpeed` 選事件即可，但那屬 Stage 2 的 gait-specific pools |
| **單一 AudioSource** | `pitch` 是 Source 層屬性 ⇒ 連續觸發時後一發會改到仍在播的前一發。目前以 **±6% 以內**的 pitch 範圍緩解（聽不出走音），真正解法是 Source 池 |
| **從角色中心播，不是從腳播** | 同上受限於單一 AudioSource。鏡頭距離僅 1.92 m，腳與中心的角度差在此距離下可察覺但不嚴重 |
| **只有 Stone 一種材質** | Surface Resolver 未做；`FootIKGroundData.Collider` 已備好接點 |
| **原地高速旋轉可能漏步** | 踩住的腳會繞軸畫弧而有殘速；寧可漏報也不誤報（`PlantHorizontalSpeedMax` 的取捨） |
| ~~**DSP buffer**~~ | ⛔ 2026-09-17 撤銷，不做（`docs/37` §12） |

---

## 8. 檔案

**新增**：`Presentation/IK/FootIKGroundData.cs`、`Presentation/Audio/`（`Audio_Shuffle.asset`）、
`Editor/FootstepDetectorEditor.cs`、`Assets/Audio/Footsteps/WASD/**`（24 顆）

**改寫**：`FootPlantTracker.cs`（判準全換）、`FootstepDetector.cs`、`FootIKController.cs`（轉發觀測）、
`PresentationEventData.cs`（`bool` → `FootstepKind`）、`PresentationPipeline.cs`（合併政策）、
`AudioController.cs`、`AudioDefinitionSO.cs`（shuffle bag ＋ 音量抖動）、`AudioEventId.cs`（＋`Shuffle`）、
`X Bot.prefab`／`Y Bot.prefab`（AudioSource 3D ＋ 偵測參數）

---

## 9. 🐛 修正：**攀爬途中有腳步聲**（2026-09-17，Play 回報）

### 9.1 症狀

ledge climb 進行中會聽到腳步聲。角色在爬牆，腳沒有踩在任何地面上。

### 9.2 根因：**空中的「離地高度」量的是一個跟著角色飛的參考點**

鏈路是三個各自正確的決定疊出來的：

```
TraversalState 執行 warp 位移
   ↓ MotionDriver.ExecuteTraversalWarpedMovement：exit knot 之後 plan 位置凍結
     ⇒ Move(Vector3.zero) 不回報觸地 ⇒ **整段 traversal isGrounded == false**
       （這句是該檔 2026-09-13 就寫下的既有註解，不是這次才發現）
   ↓ FootIKController 的閘門 = `data.IsGrounded && !BlockIK`
     ⇒ **Foot IK 全程關閉** ⇒ SampleGround 直接 `return default`
   ↓ FootIKGroundData.HasGround == false（雙腳）
   ↓ FootPlantTracker：`groundReference = hasGround ? groundY : fallbackGroundReferenceY`
     ⇒ 退回 **root 平面**
   ↓ root 平面在地上是腳底基準；**在空中只是一個跟著角色一起上升的參考點**
     ⇒ 攀爬動畫的腳相對它反覆進出 `ContactClearance`(0.045) 接觸帶
   ↓ 擊發
```

⭐ **關鍵在於那個 fallback 的適用範圍被默默擴大了。**
`hasGround == false ⇒ 用 root 平面` 當初是為了**「沒有掛 `FootIKController` 的退化配置」**
（§4）——那種情況下角色仍然站在地上，root 平面**確實**是地面。
但同一個條件在**離地**時也成立，而那時 root 平面不是地面。
⇒ 這不是「判準不夠嚴」，是**量測對象根本不存在**。

📌 同樣的機制對跳躍／墜落也成立，只是那些動畫的腳離 root 平面較遠，沒有被聽出來。

### 9.3 修法：一個條件，且刻意不認識 traversal

`FootstepDetector.Detect` 增加一個 `isGrounded` 參數（黑板 `IsGrounded`），
在 tracker **推進之後**做否決：

```csharp
if (!isGrounded)
{
    if (!leftHasGround)  { left  = FootstepKind.None; leftReason  = FootstepSuppressReason.Airborne; }
    if (!rightHasGround) { right = FootstepKind.None; rightReason = FootstepSuppressReason.Airborne; }
}
```

四個刻意的選擇：

| 選擇 | 理由 |
|---|---|
| **條件是「離地 **且** 沒有地面觀測」，不是單純「離地」** | 真的有地面觀測時就該相信它。若未來 Foot IK 在離地時仍開著，本規則不得反過來把真事件吃掉（由 `Airborne_WithGroundObservation_StillReports` 守住） |
| **抑制在推進之後，不是之前** | 與 `justLanded` 同一條既有規則：「不報告」與「沒發生」是兩件事。凍結跨帧狀態會讓落地後第一步的行程基準與上膛狀態錯亂（由 `AirborneSuppression_DoesNotCorruptTheNextGroundedStep` 守住） |
| ⛔ **不認識 traversal／jump／任何 StateType** | 本元件的契約就是「不認識狀態」。傳入的是一個**物理事實**（有沒有觸地），不是一個狀態名。⇒ 攀爬、跳躍、墜落**同一條規則涵蓋**，未來新增位移狀態零改動 |
| **`data == null` 視為 grounded** | 退化方向必須是「什麼都不做」（照常出聲），而不是把整個角色的腳步靜音。與 `StandingSupportProbe` 當初的失敗方向原則一致 |

⛔ **沒有用 `ArbiterData.BlockAudio`**：那是**整個角色**的音效總閘，攀爬時仍然要聽得到受擊／衝擊聲。
用它會把一個偵測器的誤判升級成全域靜音。
⛔ **沒有動 Foot IK 的閘門**——Foot IK 在 traversal 期間關閉是它自己的既有決定，與本 bug 無關。

### 9.4 驗證

| 測試 | 守什麼 |
|---|---|
| `Airborne_WithoutGroundObservation_ProducesNothing` | ⭐ **用與平地完全相同的 stride 資料**，只把 `isGrounded` 改成 false ⇒ 0 次擊發。**含前提斷言**：同一段資料在地面上必須先擊發 1 次，否則測試等於空轉 |
| `Airborne_WithGroundObservation_StillReports` | 有真實地面觀測時 `isGrounded` 不得否決 |
| `AirborneSuppression_DoesNotCorruptTheNextGroundedStep` | 空中被抑制的那段不得讓落地後第一步失準 |

EditMode **599 / 598 passed / 0 failed / 1 skipped**（+3）。
既有的 `WithoutGroundObservation_FallsBackToRootPlane`、`ColdGround_FallsBackAndStillDetects`、
`FootStartingAirborne_StillProducesSteps` 全部不變 ⇒ **退化路徑的契約沒有被這道閘門改掉**。

### 9.5 ⚠️ 自動化涵蓋不到、需要 Play 確認的

1. **攀爬途中確實不再有腳步聲**（本輪修的就是這個，但自動化驗的是機制，不是那段動畫）。
2. **翻越（vault）**：若某些 vault 途中 `isGrounded` 仍為 **true**（膠囊沒離開地面），
   本閘門**不會**介入 ⇒ 腳步聲仍可能出現。**若 Play 時 vault 也有問題請回報**——
   那會是另一個判準（需要的是「這個位移不是走路」而不是「離地」），不能靠加大這道閘門解決。
3. **離開 traversal 的落地聲**照舊（`JustLanded` 路徑未動）。
