# 34 — 腳步聲系統升級：素材、參考實作、延遲診斷與方案（研究輪）

> **狀態**：研究輪已結案。**Stage 0 ＋ Stage 1 已於 2026-09-16 實作**，正本移交 [`docs/35-footstep-detector-stage1.md`](35-footstep-detector-stage1.md)。
> ⚠️ **本檔 §4.2 的延遲分解有一項被實作輪推翻**（「等下降停止才擊發」估為 0–17 ms 延遲 —— 實測是**擊發在錯的相位**，Walk 提前約 340 ms）。以 `docs/35` §1.2 為準。
> ⚠️ **§8-Q1 的建議（管線①②對調）已作廢**，理由見 `docs/35` §6。
> **產生日期**：2026-09-16
> **定位**：一次性的研究輪紀錄（同 `docs/24-traversal-reference-study.md` 的角色）。
> 真正落地時，durable 規格寫進 `docs/02-dev-spec.md` §3.4 或自己的分卷，**不是**寫回本檔。
> **⚠️ 本檔的「建議」不是已裁決的基線**——§8 有三個問題必須由使用者裁決後才能開工。

---

## 0. 一句話結論

> **「聽起來延遲」的最大單項不是 AudioSource，也不是偵測演算法，而是「現在唯一接上的那顆 clip 前面有 29.7 ms 的靜音」——
> 而「素材單調」的成因是三個事件（Landing／LeftFootstep／RightFootstep）全部指向同一顆 `Floor_step0.wav`，且 `pitchRange = (1,1)`。**
> 這兩件事都**不需要動架構**，也不需要新的 raycast。真正需要討論的架構問題只有三個，見 §8。

---

## 1. WASD Free Integrated Footstep SFX Bundle — 實際下載到什麼

| 項目 | 內容 |
|---|---|
| 來源 | `https://wasd-sound.itch.io/free-integrated-footstep-sfx-bundle`（itch.io 官方頁，"Name your own price" → $0） |
| 檔案 | `WASD Footstep SFX Free Bundle v3.zip`，13,589,163 bytes（12.96 MB） |
| **落地位置** | `D:/Unity Project/_AudioResearch/WASD/`（**Unity 專案之外**；`extracted/` 為解壓結果） |
| **⛔ 尚未 import** | **沒有**放進 `Assets/`，**沒有**動到既有的 `Assets/Classic Footstep SFX/` |

### 1.1 資料夾結構與命名

```
WASD Footstep SFX Free Bundle v3/
├─ Assets/
│   └─ <Action>/<Material>/          Action ∈ {Walk, Run, Sneak, Jump, Drop, Shuffle}
│                                    Material ∈ {Dirt, Grass, Stone, Wood}
│       ├─ WASD Sound <Material> <Action> 01..06.wav      ← 6 variations
│       └─ <Material>.asset                               ← WASDMaterial ScriptableObject
├─ Scripts/                          7 個 .cs（見 §1.4）
├─ License WASD Sound Bundles.pdf
└─ READ ME.txt
```

* 檔案數：**144 個 .wav**（6 actions × 4 materials × 6 variations），與官方說明一致。
* 格式：**48 kHz / 24-bit / PCM stereo**（`pcm_s24le`）。與專案既有的 `Classic Footstep SFX` 同規格。
* 命名完全規則化 → **可以用檔名直接程式化建表**，不需要人工一顆一顆拖。

### 1.2 ⭐ 素材品質實測（ffmpeg，`silencedetect=noise=-45dB`）

| 素材 | 前置靜音（transient 前的延遲） |
|---|---|
| **WASD Walk（48 顆，4 材質全部）** | **0.000 s — 全部 0** |
| **WASD Run（24 顆，4 材質全部）** | **0.000 s — 全部 0** |
| WASD 其餘（Sneak／Drop／Shuffle） | 144 顆中僅 20 顆有前置靜音，**最長 14.6 ms**（Grass Shuffle，本來就是軟起音的刮擦） |
| **專案現用的 `Floor_step0.wav`** | **29.7 ms** ← **整包 Classic 裡最差的一顆** |
| `Classic Footstep SFX/Floor` 其餘 20 顆 | 中位數約 5 ms；6 顆為 0 |

> **這就是「腳已經踩地、聲音才出來」最大的單一貢獻項，而且是純資產問題。**
> 更糟的是這顆 clip 同時被 `Audio_Landing` / `Audio_LeftFootstep` / `Audio_RightFootstep` 三個定義共用。

### 1.3 License

* **允許商用**、不需標註出處、可用於無限個專案。
* **⛔ 禁止用於 AI 訓練**（"No AI Training"）。
* 全文：bundle 內 `License WASD Sound Bundles.pdf`（已隨解壓保留）；線上副本見 itch 頁的 Google Drive 連結。
* 作者：Leon Regula（WASD Sound）。

### 1.4 它附帶的 Unity scripts — **建議整組不用**

| 檔案 | 評價 |
|---|---|
| `WASDFootstepSource.cs` | ⛔ `Awake` 內寫 `PlayerPrefs` 廣告計數器並 `Debug.Log` 推銷；單一 AudioSource；無 3D 距離設定 |
| `WASDRaycast.cs` | ⛔ 每個 `FixedUpdate` 都 raycast ＋ 對四個 `List<Material>` 線性掃描；`materialDic` **從未 `new`、`SetupMaterialDic()` 從未被呼叫** ⇒ 走到 fallback 分支必 NRE；`hit.transform.GetComponent<Renderer>()` 無 null 檢查 |
| `WASDAction.cs` / `WASDFootstepManager.cs` | ⛔ 用硬編索引（`materials[0]` = Dirt…）對應 enum，順序錯一格就整組錯位；`OnEnable` 裡的 `lookup` 整段是註解掉的死碼 |
| `WASDMaterial.cs` | ✅ **唯一值得吸收的東西**：`GetRandomClip()` 用的是 **shuffle bag**（6 個索引的排列，用完重洗，並嘗試避免接縫處重複），不是 `Random.Range`。這正是「avoid immediate repeat」的正解。⚠️ 但它的接縫處理有 bug：`ShuffleSeed[0]` 被連續賦值兩次，`ShuffleSeed[1]` 的值被丟掉。要抄的是**想法**，不是這段程式 |

> **結論：只取 144 顆 wav ＋ shuffle bag 的概念，scripts 全部不要。**

---

## 2. Sonniss GDC 2026 — 查了 track list，**結論是這一輪不值得下載**

| 項目 | 內容 |
|---|---|
| 頁面 | `https://gdc.sonniss.com/`（GDC2026 bundle，7.47 GB+，5 個 zip） |
| 官方直載 | `https://downloads.sonniss.com/Sonniss.com-GDC2026-GameAudioBundle{1..5}of5.zip` |
| **技術上可下載嗎？** | **可以。** 已實測：加瀏覽器 UA ＋ referer 後 `HTTP 200`，且**支援 Range**（`206 Partial Content`）。part1 = **1,301,592,146 bytes（1.21 GB）**。⚠️ 預設 curl UA 會被擋成 `403` |
| **⛔ 但沒有下載** | 見下方理由。**沒有動用任何非官方 mirror。** |
| License | `https://sonniss.com/gdc-bundle-license/`。royalty-free、可商用、不需標註；**⛔ 明文禁止 AI/ML 訓練** |

### 2.1 為什麼不下載：track list 裡**沒有腳步聲素材**

官方 track list（Google Sheet，共 **346 個檔案 / 121 個 library**）已抓下並逐欄比對使用者的清單：

| 需求 | track list 命中 |
|---|---|
| heavy footsteps | **0**（唯一含 "Footsteps" 的是 `Metro Station Entrance Hall Dings Walla Footsteps` —— 一段人群 **ambience**，不是可用的單步） |
| gravel | **0**（唯一命中是 `Tire Skids on Gravel` —— 汽車輪胎） |
| concrete | 2，皆非腳步（`CaseDown_Concrete`、`Concrete_MetalPipe`） |
| mud / rubble / sand / snow / dirt / stone | **0** |
| water | 只有海浪／泳池 **ambience**，無涉水腳步 |

### 2.2 真正值得考慮的少數幾個（若日後還是要下載）

| Library | Supplier | 用途 |
|---|---|---|
| **Climbing Gear Foley Vol. 1** | 344 Audio | **裝備／扣環晃動層**——疊在腳步上做「有背負重量」的角色 ✅ |
| **Foley T-Shirt** / **Velcro** / **Washing Basket Foley** | InMotionAudio | **布料摩擦層**（對應 cloth / gear movement） ✅ |
| **Vox Hominis – Human Effort Voices** | SoundBits | 落地／重摔的**吐氣與出力聲**，是「重落地」最有效的一層 ✅ |
| **HD Game Materials** | Epic Stock Media | 材質撞擊，可墊在 heavy landing 底下 |
| Colossal Impacts / Ultra Transitions & Impacts | Cinematic Sound Design | ⚠️ 是**設計過的電影感 impact**，不是自然材質音；拿來當落地會很出戲 |
| Melee Weapons Pack 2 | David Dumais Audio | body impact（受擊用，不是腳步） |

* **分卷是按 library 資料夾字母序切的**（已用 Range 抓 part1 的 zip central directory 驗證：part1 只有 `344 Audio - *`，A→H）。
  ⇒ 若要 `Climbing Gear Foley`，**只需要 part1（1.21 GB）**，不必抓滿 7.47 GB。

### 2.3 ⭐ 更好的替代方案

「特殊材質」清單（gravel / metal / concrete / mud / water / rubble）**恰好就是 WASD Standard Bundle 的內容**：

| | Free（已下載） | **Standard（付費）** |
|---|---|---|
| 材質 | Dirt, Grass, Stone, Wood | **Concrete, Dirt, Grass, Gravel, Metal, Mud, Planks, Rubble, Sand, Stone, Water, Wood** |
| 檔數 | 144 | 400+ |

> **判斷：為了 Climbing Gear Foley 抓 1.21 GB 是合理的；為了腳步材質抓 Sonniss 是白抓。**
> 材質缺口的正解是 WASD Standard（同一位錄音師、同一套 mix、同樣 0 前置靜音 ⇒ 與 Free Bundle **可直接混用**），
> 或去 **GDC 往年 archive**（200 GB+，往年確實有專門的 footstep library）。

---

## 3. 兩個 reference repo 的核心做法

> 兩個 repo 都 clone 到 session scratchpad（`.../scratchpad/refs/`），**沒有**進專案，用完即棄。

### 3.1 `dropecho/unity_footstep`（MIT）

**偵測**（`FootStepDetector.LateUpdate`）：

```
OnEnable: _originalLFHeight = LeftFoot.position.y − root.position.y        ← 基準高度（第一帧姿勢）
LateUpdate:
  currentHeight = LeftFoot.position.y − root.position.y                    ← post-IK 骨骼 Transform
  velocity      = currentHeight − _previousHeight                          ← 注意：沒有除 deltaTime
  if (!offGround && currentHeight > _original + 0.02) offGround = true     ← 抬腳閂鎖
  if (velocity < 0 && currentHeight <= _original + 0.02 && offGround
      && _timeSinceLastEvent > 0.33)                                       ← ⛔ 雙腳共用的時間閘
      → FootStepEvent{ velocity, position, surface }
```

* **左右腳**：兩份互相複製的 `CheckLeftFoot` / `CheckRightFoot`，各自持有 `offGround` 閂鎖，**但共用同一個 `_timeSinceLastEvent`**。
* **Animation Event 模式**：`LeftFootStep()` / `RightFootStep()` / `FootStep()` 三個 public 方法，讓 Animation Event 直接呼叫，只借用它的 surface detection。兩種模式由 `autoDetectFootSteps` 互斥切換。
* **Surface detection**：抽象 `FootStepSurfaceType : ScriptableObject`，四個子類 —— `ByTag` / `ByMaterial` / `ByTerrainTexture` / `ByComponent`。
* **debounce**：只有 `minTimeBetweenFootsteps = 0.33f`。
* **AudioClip variation**：**沒有**。`FootStepAudio` 是 surface index → 單一 clip 的一對一查表，連隨機都沒有（音量寫死 `0.5f`，velocity 對應音量的那行被註解掉了）。

**⛔ 三個對我們明確不適用的地方**（而且剛好都是我們已經反向決策過的）：

| 它的做法 | 我們的現況 | 為什麼我們是對的 |
|---|---|---|
| 讀 **post-IK** 的 `Transform.position` | 讀 **pre-IK** 的 `GetIKPosition()` | 我們有 Foot IK。讀 post-IK 會形成回授迴路——**這正是 changelog v0.18.1 腳踝抽搐的根因** |
| **雙腳共用** 0.33 s 時間閘 | `FootPlantTracker` **刻意不用時間閘** | 0.33 s 上限 = 3 步/秒。我們 sprint 前向 3.578 m/s × playback 1.313 的步頻會**直接被吃掉**。`FootPlantTracker` 的註解已明文拒絕過這個設計 |
| **每個 surface type 各自 raycast**（`CheckIfPointOnSurface` 內含 `Physics.Raycast`）＋ 線性掃描 | — | 4 種材質 = 每步最多 4 次 raycast ＋ 4 次 `GetComponent`。正是「每一步都重新做昂貴查詢」的具體樣貌 |

其餘缺陷：`FootStepSurfaceByTerrainTexture` 每次查詢 `GetAlphamaps` 配置 `float[,,]` ＋ `new float[]` ⇒ **每一步都 GC**。

### 3.2 `VilleOjala/FMOD-Unity-Tools`（MIT）

#### ⭐ 「它為什麼可以不依賴 Animation Event？」

> **因為它把判準從「時間軸上的標記」換成「姿勢的幾何量」。**
>
> Animation Event 是**寫死在 clip 上的時間戳**：它假設「這顆 clip 播到 0.37 秒時腳會在地上」。
> 這個假設在三種情況下會失效 —— ①playback speed 被改（時間戳仍在 0.37 s，但腳早就到了）
> ②兩顆 clip 在 blend（兩個時間戳都會觸發，或混出來的腳根本沒落到那個位置）
> ③clip 被換掉（時間戳留在舊 clip 上）。
>
> `ThirdPersonFootsteps` 改成**每個 tick 去量「腳現在離參考平面多高」**，用兩個門檻做遲滯：
> 高過 `raisedHeight` ⇒ 記為已抬起；之後低於 `triggerHeight` ⇒ 觸發並清除旗標。
> **腳的高度是姿勢算出來的結果，不是資料標出來的承諾** —— 不論怎麼加速、怎麼 blend、換哪顆 clip，
> 腳真的抬起來再放下去，就一定會依序穿過這兩條線。**這就是它不需要 Animation Event 的全部理由。**
>
> 代價是：它只知道「腳放下來了」，不知道「動畫作者本來想在哪一帧出聲」——所以**軟起音的動作（潛行、拖步）判得比較不準**，
> 這也是它要為 walk / run / crouch **各準備一組門檻**的原因（走路的抬腳幅度比跑步小得多，共用一組門檻必漏）。

**呼叫鏈／資料流**：

```
Animator (humanoid)
  │  GetBoneTransform(LeftFoot/RightFoot)                       ← Start 時解析一次
  ▼
ThirdPersonFootsteps.FixedUpdate
  ├─ animator.GetBool(groundedParameter) ─────► !grounded ⇒ ResetAllRaisedFeet()，整段跳過
  ├─ EventManager 送來的 jump/land tag ───────► 先播 Jump/Land，並把雙腳都設成 cooldown
  ├─ speed = √(vertical² + horizontal²)        ← 取 blend tree 的座標長度，不是實際速度
  │    └─► movementMode ∈ {None, Walk, Run, Crouch}   （crouch/walk/run 三個 speed 門檻）
  │         └─► thresholds = GetFootstepHeightThresholds(movementMode)   ← ⭐ 每個 gait 一組門檻
  └─ foreach foot:
       relativeHeight = |foot.position.y − referenceHeight.position.y|
       ├─ ≥ raisedHeight  && !isRaised ⇒ isRaised = true
       └─ ≤ triggerHeight &&  isRaised ⇒ isRaised = false
              │
              ▼
          SurfaceChecker.Instance.TryGetSurfaceType(footPos + rayOffset, down, mask, len, …)
              │   Physics.RaycastAll  →  對每個 hit：
              │     ├─ TerrainCollider ⇒ GetAlphamaps → 各 TerrainLayer 權重
              │     │      ⭐ 先把「對應到同一個 SurfaceType 的 layer 權重相加」，再取 argmax
              │     └─ 其他 ⇒ GetComponentInParent<SurfaceTag>()   ⭐ 註在父層，整組 collider 共用
              │   水面另外處理：水面 tag 與下方地面的距離 ⇒ WaterDepth {Shallow/Medium/Deep}
              ▼
          SurfaceInfo { surfaceType, waterDepth, position }
              ▼
          ParamRef[] { Movement=(int)mode, Surface=(int)type, WaterDepth=(int)depth }
              ▼
          AudioObject.Control(Start, parameters)  →  FMOD event（randomization 全部在 FMOD 裡）
              ▼
          StartFootCooldown(foot)   ← 每腳獨立 0.3 s，用 coroutine 實作
```

**⭐ 三個值得吸收的想法**：

1. **每個 gait 一組偵測門檻**（walk / run / crouch）。我們現在是**一組門檻打天下**（`ArmDescentSpeed 0.35`），
   這很可能就是「blend 中漏步」的來源之一 —— 見 §4.3。
2. **`GetComponentInParent<SurfaceTag>()`**：surface 標在父物件，一個被切成多塊 collider 的地板只要標一次。
3. **terrain 權重「先按 SurfaceType 合併、再 argmax」**：兩個都對應到 Dirt 的 layer 各 0.3，
   合併後 0.6 才去跟單一 0.4 的 Rock 比。dropecho 是逐 layer argmax，會判成 Rock。
   ⚠️ 這正是「snap-to-default／argmax」那一類缺陷的正確解法。

**⛔ 明確不能照抄的**：

| 它的做法 | 衝突點 |
|---|---|
| `SurfaceChecker.Instance` **singleton** | ⛔ `CLAUDE.md`「Do NOT introduce singleton managers」 |
| 在 **FixedUpdate** 取樣腳骨 Transform | 腳骨只在動畫／render 頻率更新；用 50 Hz 物理格取樣會把觸發時機量化到 20 ms 格上，還可能重複取到同一姿勢。**我們在 LateUpdate 取樣是對的** |
| 每腳 0.3 s cooldown 用 **coroutine ＋ `WaitForSeconds`** | ⛔ 每一步配置一個 coroutine ＋ 一個 `WaitForSeconds` ⇒ 每步 GC。違反 Zero GC Runtime |
| `GetParameters` 每步 `new ParamRef[3]`；`SurfaceChecker` 每次查詢 `Physics.RaycastAll` ＋ 兩個 `Dictionary` | ⛔ 同上，每步 GC |
| 固定 cooldown（0.3 s／腳） | 同 dropecho 的問題，只是比較不嚴重（單腳 3.3 步/秒） |

Occlusion / reverb / ambience 子系統本輪**只記存在，不展開**（`AudioTools/` 下另有完整的 room-based occlusion 與 ambience 系統）——這輪不擴張成 Audio Framework。

---

## 4. 我們目前的腳步聲延遲 —— 實際根因

### 4.1 完整鏈路（磁碟現況）

```
① Animator 評估（Animancer）
      ▼
② FootIKRig.OnAnimatorIK          ← Model 子物件，早於 LateUpdate
      _poseData.LeftFootPosition  = animator.GetIKPosition(LeftFoot)   ← pre-IK，未被 IK 污染
      _poseData.LeftFootBottomHeight = animator.leftFeetBottomHeight
      ▼  （FootIKPoseData，單寫多讀，owner = FootIKRig）
③ CharacterPipelineRunner.LateUpdate → 【順序 6.5】PresentationPipeline.Tick
      ├─ ① 依序驅動 IPresentationController
      │     └─ AudioController.Tick  ← ⚠️ 讀到的是【上一帧】發布的 PresentationEvents
      │           └─ Play(LeftFootstep) → AudioLibrarySO.Get → AudioDefinitionSO
      │                 → audioSource.pitch = 1.0（pitchRange (1,1)）
      │                 → audioSource.PlayOneShot(Floor_step0.wav, 1.0)
      └─ ② 詢問 IPresentationEventSource → OR 合併 → 整體覆寫 data.PresentationEvents
            └─ FootstepDetector.Evaluate → Detect(pose, JustLanded, Time.deltaTime)
                  footBottom = pose.LeftFootPosition.y − pose.LeftFootBottomHeight
                  FootPlantTracker.Advance(footBottom, dt, settings)
      ▼
④ AudioSource → Unity DSP mixer（buffer 1024 samples @ 48 kHz）→ 喇叭
```

### 4.2 ⭐ 延遲預算（@ 60 fps）

| # | 環節 | 機制 | 估計 | 證據等級 |
|---|---|---|---|---|
| **D** | **clip 前置靜音** | `Floor_step0.wav` 的 transient 在 **29.7 ms** 處 | **29.7 ms** | ✅ **實測**（ffmpeg） |
| **E** | **Unity DSP 輸出緩衝** | `m_DSPBufferSize: 1024` @48 kHz = 21.3 ms/buffer，通常雙緩衝 | **21 – 43 ms** | ✅ 設定已讀（`ProjectSettings/AudioManager.asset`），倍數為推論 |
| **C** | **管線發布延遲** | 事件在 6.5 的**步驟②**發布，`AudioController` 在**步驟①**消費 ⇒ **固定慢一帧** | **16.7 ms** | ✅ 程式與註解明文（`PresentationPipeline.Tick`：「代價是固定一帧延遲（約 16ms，聽不出來）」） |
| **A** | **速度用後向差分** | `(h[n]−h[n−1])/dt` 描述的是 [n−1, n] 這段區間，本質上落後半帧 | ~8 ms | ✅ 程式；數值為推論 |
| **B** | **「等下降停止」才擊發** | Schmitt 的擊發條件是下降**慢到 < 0.05 m/s**，即減速**完成之後**，不是接觸瞬間 | 0 – 17 ms | ⚠️ **推論**，需 Play 量測 |
| | **合計** | | **≈ 75 – 115 ms** | |

> **75–115 ms 是清楚可聽的延遲。** 人對 transient 的「同步」容忍大約在 20–30 ms 以內。
>
> **⭐ 注意 D 與 E 加起來就佔了 50–73 ms，兩者都與偵測演算法無關。**
> 這是為什麼「不要先假設問題一定是 AudioSource」的反面也成立 —— **也不要先假設問題一定是偵測器。**

### 4.3 逐條回答點名的懷疑項

| 懷疑 | 結論 |
|---|---|
| **Animation Event 本身標晚？** | **不適用。** 專案**完全沒有使用 Animation Event**（`grep` 全 `Assets/Scripts` 零命中）。腳步全部走 `FootstepDetector` 的幾何偵測 |
| **playback speed 讓 timing 異常？** | **不會造成偏移**——偵測是幾何量（世界空間腳底高度），playback 變快只讓腳降得更快，門檻照樣穿越。⚠️ **但反向有風險**：playback **變慢**時下降速度可能永遠達不到 `ArmDescentSpeed = 0.35 m/s` ⇒ **整步靜音**。需量測 |
| **blend 中漏步或重複？** | ⚠️ **這是最可疑的未量測項。** mixer 對 IK goal 做線性插值；兩顆相位不同的 clip 混起來時腳的垂直行程會被**削平**，可能打不到 `MinLiftExcursion = 0.03`。戰鬥 8 向 mixer 混不同方向的 clip，相位差更大。需量測 |
| **Foot IK 修正後的真正接觸時刻與偵測不一致？** | **刻意如此，而且是對的。** `FootstepDetector` 明文讀 **pre-IK** pose：「玩家聽到的腳步聲要對齊**看到的動畫落腳**」。斜坡上 IK 會把腳移到別處，但落腳**時機**不變。⛔ 不要為了「物理接觸」改讀 post-IK |
| **AudioSource / clip import 有多餘 latency？** | **clip import 沒問題**（`loadType: 0` Decompress On Load ＋ `preloadAudioData: 1`，無串流延遲）。**但 clip 內容有 29.7 ms 靜音**（項 D），且 **DSP buffer 1024**（項 E） |
| **每一步都做昂貴查詢？** | **沒有。** 目前熱路徑是純數值運算 —— 無 raycast、無 `GetComponent`、無配置。這一項**現況是乾淨的**，⚠️ 但一旦加上 surface detection 就會被破壞，見 §6 |

### 4.4 另外兩個「不是延遲、但就是實際聽到的問題」

1. **三個事件共用同一顆 clip。** `Audio_Landing`、`Audio_LeftFootstep`、`Audio_RightFootstep` 的 `clips` 陣列
   **都只有一個元素，而且 guid 相同（`Floor_step0.wav`）**。左腳、右腳、落地，聽起來完全一樣。
2. **`pitchRange: {x: 1, y: 1}`** —— 隨機音高機制存在但**被關掉**。
   ⇒ 「素材單調」是 **100% 的接線問題**，專案磁碟上其實已經有 **80 顆** footstep wav（`Classic Footstep SFX`，4 種地面），一顆都沒用上。

---

## 5. 現有 Foot IK 資訊有哪些可重用

### 5.1 ✅ 已經算好、可以直接重用的

| 資料 | 位置 | 現況 | 可重用性 |
|---|---|---|---|
| **pre-IK 腳部 world pose** | `FootIKPoseData.{Left,Right}FootPosition/Rotation` | owner = `FootIKRig`，**已明文允許多 Reader** | ✅ **已經在用**（`FootstepDetector`）。footstep event 的 `world position` 直接從這裡拿，零成本 |
| **腳踝→腳底常數** | `FootIKPoseData.{Left,Right}FootBottomHeight`（來自 Avatar） | 同上 | ✅ 已在用 |
| **⭐ 地面 raycast 結果** | `FootIKController.FootSample.{HasHit, HitPoint, Normal, SoleNormal, GroundY}` | ⛔ **`private struct`，未對外暴露** | ⚠️ **這是關鍵**：`FootIKController.Tick` 每帧已經對每隻腳發 raycast（甚至 heel/toe 兩點）。**footstep 不需要再發一次** |
| 地面法線 | 同上 `Normal` / `SoleNormal` | 同上 | ✅ 可給 footstep 判斷斜坡（拖步聲） |

### 5.2 ⛔ Foot IK **沒有**、footstep 需要另外取得的

| 需要 | 為什麼沒有 |
|---|---|
| **`Collider` / `GameObject` 的引用** | `FootSample` **只存了 `HitPoint` / `Normal` / `GroundY`，沒有存 `hit.collider`**。而 surface 解析（tag / material / terrain layer）**必須要有 collider**。這是唯一真正的缺口 |
| gait（Walk/Run/Sneak） | 不屬於 Foot IK。但黑板上有 **`MoveSpeed`**，夠用 |
| impact strength | 不屬於 Foot IK。落地可用黑板 **`VerticalVelocity`**；一般腳步可用 tracker 內部的下降速度 |

> ### ⭐ 結論：**可以重用，而且應該重用——只要在 `FootSample` 上多存一個 `Collider`。**
> 差別是「每步 0 次新 raycast」對上「每步 1–4 次新 raycast」（兩個參考 repo 都是後者）。
> **但這個重用有一個架構問題必須先裁決**，見 §8-Q2。

---

## 6. 建議保留 / 修改 / 淘汰

### ✅ 保留（不要動）

| 對象 | 理由 |
|---|---|
| `FootPlantTracker` 的**演算法骨架** | Schmitt trigger ＋ 最小垂直行程 ＋ **不用時間閘**。兩個參考 repo 都用了時間閘，而且**兩個都因此有步頻上限** —— 我們的選擇被外部實作反證為對的 |
| **讀 pre-IK pose** 的決策 | changelog v0.18.1 的血淚。dropecho 讀 post-IK 正是我們踩過的坑 |
| `IPresentationEventSource` 回傳值（不寫黑板） | 型別層保證，不是紀律。擴充 payload 不需要放寬這條 |
| `AudioEventId` / `AudioLibrarySO` 的 **enum → 攤平陣列 O(1) 查表** | 零 GC、零 boxing。比兩個參考 repo 的線性掃描都好 |
| `FootstepDetector` 的職責邊界（不 raycast、不持有黑板欄位引用、不認識 AudioController） | 這正是能乾淨擴充的原因 |
| `MotionFeatureAnalysis.FootPhaseCurveAnalyzer` | 已存在且其註解明文寫著「供 Start/Stop 選腳別與**未來 Footstep／相位同步**」。⭐ 「Animation/Bake → Expected Foot Contact」**半條已經在磁碟上了** |

### 🔧 修改

| 對象 | 改什麼 | 成本 |
|---|---|---|
| **`Audio_*.asset` × 3** | **最高優先**。換成 WASD Walk/Run 的 6 顆 variation；`pitchRange` 開成 `(0.95, 1.05)`；Landing 用 Drop 而非 step | 純資產，0 程式 |
| **`AudioDefinitionSO`** | `GetRandomClip()` → **shuffle bag**（抄 WASD 的想法，修掉它的接縫 bug）；加 `volumeRange` | 小 |
| **`AudioController`** | ⚠️ **必須配 AudioSource pool**。`audioSource.pitch` 是 **Source 層屬性**——M2 已知侷限：「連續觸發時後一發會改到仍在播的前一發」。Walk clip 0.39 s、跑步步頻 ~3 步/秒 ⇒ **重疊是必然**，隨機音高會把還在響的上一步一起變調。**這條 Future Work 現在被觸發了** | 中 |
| **`PresentationEventData`** | 從兩個 `bool` 擴成帶 payload（foot / world position / surface / gait / impact）。⚠️ OR 合併政策要跟著改 | 中 |
| **`PresentationPipeline.Tick` 順序** | ①②對調 ⇒ **直接省掉 16.7 ms**。見 §8-Q1 | 小但**需裁決** |
| **`FootstepDetectionSettings`** | ①`FireDescentSpeed` 0.05 → 試 0.10~0.15（提早擊發，項 B）②依 gait 分組門檻（抄 FMOD tools 的想法，解 §4.3 的漏步疑慮） | 小（純 tuning） |
| **`FootIKController.FootSample`** | 加 `Collider HitCollider`，並開一條唯讀輸出管道 | 小但**需裁決**（§8-Q2） |
| **AudioSource 接線** | `panLevelCustomCurve` 現在是 **0 = 完全 2D**；要 3D 播放需設成 1。另外 `OutputAudioMixerGroup` 為空，沒有走任何 mixer | 純資產 |
| **`ProjectSettings/AudioManager.asset`** | `m_DSPBufferSize: 1024` → 試 "Best latency"(256)。⚠️ 全域設定、影響所有音效、換 CPU 換延遲，**要單獨驗證** | 需裁決 |

### 🗑️ 淘汰

| 對象 | 說明 |
|---|---|
| **「三個事件都指向 `Floor_step0.wav`」的接線** | 淘汰的是**接線**，不是機制 |
| `Floor_step0.wav` 作為腳步音 | 整包 Classic 裡前置靜音最長的一顆（29.7 ms）。⚠️ **不要刪檔**，只是不再引用 |
| **WASD 的 7 個 script** | 全部不進專案（§1.4） |
| `Assets/Classic Footstep SFX/` | ⛔ **不要刪。** 它有 WASD Free 沒有的 Forest / Rocky ground，留作補充材質 |
| **架構元件** | **零。** 這次升級不需要淘汰任何現有類別 |

---

## 7. 最小可行升級方案（MVP，分三階）

> 原則：**先收割不需要架構決策的部分**，把 75–115 ms 砍到 ~30 ms，再決定要不要碰架構。

### 🟢 Stage 0 — 零程式、零架構（預計消掉 ~30 ms ＋ 徹底解決單調）

1. 用 Unity Editor API 把 WASD `Assets/` 的 144 顆 wav import 到 `Assets/Audio/Footsteps/WASD/`（**不覆蓋** `Classic Footstep SFX`）。
2. 匯入設定：`loadType = DecompressOnLoad`、`preloadAudioData = true`、`forceToMono = true`（3D 音效不需要 stereo，省一半記憶體）。
3. 改寫 `AudioDefinitionSO` 資產：`Audio_LeftFootstep` / `Audio_RightFootstep` 各填 **Stone Walk 01–06**；`Audio_Landing` 填 **Stone Drop 01–06**。
4. `pitchRange` → `(0.95, 1.05)`。
5. AudioSource `spatialBlend` → 1。

> **驗收**：延遲應從「明顯慢半拍」降到「大致跟得上」，左右腳不再同一個聲音。
> ⚠️ 步驟 4 會暴露 pitch 互相干擾的問題（§6），那是 Stage 2 要解的，**這裡刻意讓它先浮出來**。

### 🟡 Stage 1 — 小程式、需一次裁決（再消掉 ~17 ms）

6. `PresentationPipeline.Tick` 的①②對調（**§8-Q1 裁決後**）＋ 更新 `PresentationPipeline` 的註解與相關架構測試。
7. `FootstepDetectionSettings.FireDescentSpeed` 的 tuning 掃描。
8. **debug instrumentation**：一個 `[SerializeField] bool logFootsteps` 的環形緩衝，記錄
   `Time.realtimeSinceStartup / 左右腳 / footBottomHeight / verticalSpeed / 是否擊發 / 被哪一條抑制`，
   **只在擊發或抑制時寫一筆**，不每帧 log ⇒ 用來量測 §4.3 的兩個未驗證項（漏步、擊發時機）。

### 🔵 Stage 2 — 架構擴充（**本輪不做，等 Stage 0/1 驗收後再開會**）

9. `FootstepEventData`（Left/Right、world position、surface、gait、impact strength、ground collider）。
10. `FootIKGroundData` 唯讀管道（**§8-Q2 裁決後**）＋ `SurfaceLibrarySO`（`SurfaceTag` component + terrain layer 兩路，抄 FMOD tools 的父層查找與權重合併）。
11. AudioSource pool（解 pitch 互相干擾）。
12. gait 分組（Walk / Run / Sneak）。

> ⛔ **明確不做**：FMOD、event bus、改 Foot IK 演算法、改 locomotion、Sonniss 整包進 Assets、刪既有腳步系統。

---

## 8. 如果要實作，預計會動到哪些檔案

### ⚠️ 開工前必須先裁決的三個問題

| # | 問題 | 建議 | 為什麼需要裁決 |
|---|---|---|---|
| **Q1** | `PresentationPipeline.Tick` 的①②可以對調嗎？ | **建議可以。** 現有註解說一帧延遲是為了買「事件正確性與 Hierarchy 順序無關」。但**對調後這個性質仍然成立**——發布依然是**單一原子點**、依然在 controller 迴圈**之外**，只是移到迴圈**之前**。差別只在 consumer 看到的是本帧還是上帧。且 `IPresentationEventSource` 只讀黑板不寫、controller 對黑板只讀不寫 ⇒ **沒有相依危險** | 這動到 dev-spec §3.4.1 明文寫下的契約與其理由。**改契約是使用者的決定** |
| **Q2** | `FootstepDetector` 怎麼拿到 Foot IK 的 ground hit？ | **建議比照 `FootIKPoseData` 的先例**：由 `FootIKController`（唯一 writer）持有一份 `FootIKGroundData`，開 `public FootIKGroundData GroundData => _groundData`，讀方只讀 | ⚠️ 但 `FootIKPoseData` 的 owner 是 **`FootIKRig`（不是 Controller）**，而契約寫著「Controller 彼此不得互相引用」。`FootstepDetector` 是 `IPresentationEventSource` 不是 Controller，嚴格說沒違反字面，**但確實是往 Controller 裡伸手**。這是 §3.4.1 規則的邊界案例 |
| **Q3** | Sonniss 要不要下載？ | **建議：GDC2026 不下載。** 若要 cloth/gear 層，**只抓 part1（1.21 GB）**拿 `Climbing Gear Foley Vol. 1`。材質缺口建議走 WASD Standard 或 GDC 往年 archive | 1.21 GB 的下載 ＋ 這是採購決定 |

### 檔案影響清單

| 檔案 | Stage | 動作 |
|---|---|---|
| `Assets/Audio/Footsteps/WASD/**`（新） | 0 | 新增 144 wav ＋ import 設定 |
| `Assets/ScriptableObjects/Audio/Audio_LeftFootstep.asset` | 0 | 改 `clips` / `pitchRange` |
| `Assets/ScriptableObjects/Audio/Audio_RightFootstep.asset` | 0 | 同上 |
| `Assets/ScriptableObjects/Audio/Audio_Landing.asset` | 0 | 同上（改用 Drop） |
| `Assets/Prefabs/X Bot.prefab` / `Y Bot.prefab` | 0 | AudioSource `spatialBlend` 0 → 1 |
| `Assets/Scripts/Presentation/PresentationPipeline.cs` | 1 | **①②對調 ＋ 註解重寫**（Q1） |
| `Assets/Scripts/Presentation/Footstep/FootstepDetector.cs` | 1 | debug instrumentation（環形緩衝） |
| `Assets/Scripts/Presentation/Footstep/FootPlantTracker.cs` | 1 | 可能只改預設值，演算法不動 |
| `Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs` | 1 | `PresentationEvents` 的 `WriterRule` 不變；若順序寫成不變量則需更新 |
| `Assets/_Project/Tests/EditMode/FootstepDetectorTests.cs` | 1 | 新門檻的 baseline |
| `Assets/Scripts/Presentation/Audio/AudioDefinitionSO.cs` | 2 | shuffle bag ＋ `volumeRange` |
| `Assets/Scripts/Presentation/Audio/AudioController.cs` | 2 | AudioSource pool ＋ 消費 payload |
| `Assets/Scripts/Core/Blackboard/PresentationEventData.cs` | 2 | 擴成帶 payload |
| `Assets/Scripts/Presentation/IK/FootIKController.cs` | 2 | `FootSample` 加 `Collider`；開 `GroundData` 唯讀管道（Q2） |
| `Assets/Scripts/Presentation/Audio/SurfaceLibrarySO.cs`（新） | 2 | surface 查表 |
| `Assets/Scripts/Presentation/Audio/SurfaceTag.cs`（新） | 2 | 場景標記 component |
| `ProjectSettings/AudioManager.asset` | 待定 | DSP buffer 1024 → 256（⚠️ 全域，單獨驗證） |
| `docs/02-dev-spec.md` §3.4 | fold-back | Audio 子系統表 ＋ 管線順序說明 |
| `docs/00-map.md` | fold-back | 已加本檔指標 |

---

## 附錄 A — 本輪的證據等級標示

| 結論 | 等級 |
|---|---|
| `Floor_step0.wav` 有 29.7 ms 前置靜音；WASD Walk/Run 全為 0 | ✅ **ffmpeg 實測** |
| 三個 audio 事件共用同一顆 clip、`pitchRange (1,1)` | ✅ **資產 YAML 直讀** |
| `PresentationEvents` 慢一帧 | ✅ **程式碼 ＋ 原作者註解明文** |
| 專案完全未使用 Animation Event | ✅ **全 `Assets/Scripts` grep 零命中** |
| DSP buffer = 1024 samples | ✅ **設定檔直讀**（雙緩衝倍數為推論） |
| AudioSource `spatialBlend = 0`、無 mixer group | ✅ **prefab YAML 直讀** |
| Sonniss GDC2026 無腳步素材 | ✅ **官方 346 筆 track list 逐欄比對** |
| Sonniss part1 = 1.21 GB、分卷按字母序 | ✅ **HTTP HEAD ＋ Range 抓 zip central directory 驗證** |
| 「等下降停止才擊發」造成 0–17 ms | ⚠️ **推論，未 Play 量測** |
| blend 中可能漏步 | ⚠️ **推論，未 Play 量測** |
| 慢速 playback 可能整步靜音 | ⚠️ **推論，未 Play 量測** |
| 延遲總和 75–115 ms | ⚠️ **各項相加，未端到端量測** |

> ⚠️ **三個 Play 量測項正是 Stage 1 步驟 8 的 instrumentation 要回答的。**
> 但請注意：**即使這三項全部為 0，D＋E 仍有 50–73 ms** —— Stage 0 該先做，不需要等量測。
