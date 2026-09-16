# 36 — 腳步 Stage 2：Surface 解析、Gait 分池、音量集中與 AudioSource 池

> **定位**：`Presentation/Footstep/` ＋ `Presentation/Audio/` 的 Dev Spec 分卷。
> **前置**：`docs/35`（Stage 0／1：偵測判準）。偵測邏輯本輪**未改動**。
> **狀態**：已實作。⛔ **尚未經人工 Play 驗收**（音色、材質切換聽感、3D 定位）。

---

## 0. 本輪要解的問題

> 使用者回報的首要問題不是「素材不夠」，而是 **「Walk 播 Stone，收步卻冒出草地感」**——
> 也就是**同一塊地面上，不同事件用了不同材質**。

成因很單純：Stage 1 的三個事件各自指向自己的 clip 池，而那些池子是**憑素材命名**接上去的，
沒有任何機制保證它們屬於同一種材質。**這不是調音量能修的，是結構問題。**

**Stage 2 的解法是把「材質」變成事件的一部分**：`FootstepDetector` 每帧解析一次材質，
`Normal` / `Shuffle` / `Landed` 三者讀**黑板上同一個欄位**。
⇒ 它們在**型別層**就不可能不一致，不需要靠紀律維持。

---

## 1. 場景實際盤點（不是假設，是掃出來的）

目前展示場景是 **Unity StarterAssets 的 Playground demo map**。實測：

| 項目 | 實際情況 |
|---|---|
| **Terrain** | **0 個**（完全沒有） |
| **非 trigger collider** | **39 個**（另有 2 個 trigger hitbox、2 個 CharacterController） |
| **PhysicMaterial** | **一個都沒有**（全部 `(none)`） |
| **Collider 型別** | BoxCollider ×33、MeshCollider ×6、CapsuleCollider ×1 |
| **Renderer 位置** | **全部在 collider 自己身上**（`self`）——沒有 parent/child 錯位的情形 |
| **多材質 Renderer** | **0 個**（地面全是單材質） |
| **Layer** | 全部 `Default` |

### 1.1 ⭐ 地面實際只用了 6 顆 Material

| Material | 用於幾個 collider | 資產路徑 |
|---|---:|---|
| `GridOrange_01_Mat` | **26** | `Assets/StarterAssets/Environment/Art/Materials/` |
| `GridWhite_01_Mat` | 6 | 同上（樓梯、斜坡、Structure） |
| `Blue_Mat` | 4 | 同上（border 圍牆） |
| `GreyBlue_Mat` | 1 | 同上（**2051 m × 2051 m 的主地面**） |
| `ProBuilderDefault` | 1 | Packages（ProBuilder 做的 `Stairs`） |
| `Lit` | 1 | Packages（一個 `Cube`） |

> **這就是「不必逐塊掛 Tag」的實證**：43 個物件、6 筆設定。
> 26 個箱子共用一顆 Material ⇒ **設定一次，26 個全部生效**。

### 1.2 ⚠️ 順帶發現（非本輪處理範圍）

`FootIKController.settings.GroundLayers` 目前是 **Everything（-1）**，其中包含 `Player` 與 `Enemy` layer。

> 🔄 **2026-09-17 實測修正**：先前這裡寫「理論上腳部 ray 可能打到角色自己的 `CharacterController`」——
> **實測後這句是錯的**，已修正。量到的實際行為：
>
> | 情境 | 結果 |
> |---|---|
> | 從自己膠囊**內部**往下射 | **打不到自己**（Unity 的 ray 起點在凸面體內部時不回報該碰撞體） |
> | 從別的角色膠囊**內部**往下射 | 只打到 Ground，打不到那個膠囊 |
> | 從別的角色**上方**往下射 | ⚠️ **會打到**（Y Bot 膠囊頂 y=1.588，比 Ground 更近 ⇒ `Physics.Raycast` 取它） |
>
> ⇒ 唯一會發生的情境是**角色踩在另一個角色頭上**（`RaycastUpOffset 0.25`／`RaycastDistance 0.85`，
> 腳要高於對方膠囊頂才構得到）。此時 Foot IK 會把對方的膠囊當成地面。
> 對**音效**無害（角色 Material 未列入 Surface Map ⇒ 落到 Default Stone），
> 但對 **Foot IK 本身**是錯的。屬 Foot IK 設定、非腳步系統範圍；
> 要修就是把 GroundLayers 收窄成地面 layer，⛔ 不要在腳步系統裡特判。

---

## 2. Surface 解析

### 2.1 優先級

```
FootIKGroundData.Collider（Stage 1 已有，零額外 raycast）
   ↓
① SurfaceOverride（GetComponentInParent）     ← 顯式，最高
② Collider.sharedMaterial（PhysicsMaterial）
③ TerrainLayer（僅 TerrainCollider；權重最高者）
④ Renderer.sharedMaterial[0]（self → parent → child）   ← **主要配置管道**
⑤ SurfaceMapSO.DefaultSurface                 ← 保證有聲音
```

### 2.2 Default Surface ＝ **Stone**

語意是「**Generic Hard Floor**」：concrete / tile / stone / 一般室內硬地 / 未分類的 demo-map 地板。
⇒ **即使一塊地板完全沒配置，也不會沒聲音**，而且落到的是最不容易出戲的那一種。

### 2.3 ⛔ runtime 不做名稱比對

`SurfaceMapSO` 的 key 是 **Material／PhysicsMaterial／TerrainLayer 的資產參考**，不是字串。
名稱只在 **Editor 審計工具**裡當「建議」，由人按下按鈕才寫進表。

**為什麼**：名稱會改、會被美術換規則、會本地化，失效時**完全無聲**（地板仍有聲音，只是錯的）。
而且「叫什麼」本來就不等於「聽起來像什麼」——貼石紋貼圖的木橋是常見美術做法。
**建議與權威是兩回事。**

### 2.4 效能

`SurfaceResolver` 是值型別，每腳一份，**以 collider 參考做記憶化**：
腳在同一塊地板上停留數十帧，這段期間的成本是一次 `ReferenceEquals`。
⚠️ `TerrainCollider` 例外不快取（同一個 collider 上不同位置是不同 layer）。
實測：`Ground` 連續解析 1000 次，結果穩定且只解析一次。

### 2.5 Terrain

⚠️ **目前場景沒有 Terrain**，本輪只留**乾淨的擴充點**，沒有過度實作：
取腳下權重最高的 TerrainLayer，且**先把對應到同一材質的 layer 權重相加再取最大**
（兩個都對應 Dirt 的 layer 各 0.3 應該贏過單一 0.4 的 Rock；`dropecho` 逐 layer argmax 會判錯）。
⛔ **不做**多材質音效混合。
⚠️ `GetAlphamaps` 會配置陣列 ⇒ 該路徑**有 GC**；真的用上 Terrain 時要先做 splat 快取。

---

## 3. ⭐ 三種事件共用同一次 Surface 決策

```
FootstepDetector.Detect（管線 6.5 步驟②）
  ├─ 解析左腳材質（記憶化）
  ├─ 解析右腳材質（記憶化）
  └─ PickSurface() → PresentationEventData.Surface        ← 單一欄位
                     PresentationEventData.Landed         ← 🆕 落地也由此轉報
        ▼ 下一帧
AudioController.Tick（管線 6.5 步驟①）
  ├─ Landed        → Bank(Surface, Landing)
  ├─ LeftFootstep  → Bank(Surface, Kind, Gait)
  └─ RightFootstep → Bank(Surface, Kind, Gait)            ← 三者讀同一個 Surface
```

### 3.1 為什麼落地改由偵測器轉報

原本 `AudioController` 直接讀黑板的 `JustLanded`（**當帧值**），而事件快照**慢一帧**。
兩者混用的後果：落地那一瞬間拿到的是「上一帧還在空中、沒有地面觀測」的材質 ⇒ 必然錯。
改成轉報之後兩者同源同帧。

⚠️ **代價**：落地音也承擔了那一帧（約 16 ms）。落地是較重較慢的聲音，這個代價可接受——
而且**材質正確**比早 16 ms 重要得多。

### 3.2 素材缺漏時的 fallback：**寧可換 kind，也不換 surface**

```
(surface, kind)              例：Wood Shuffle
  ↓ 缺
(surface, 較通用的 kind)      例：Wood Walk（音量再壓 0.7×）  ← **仍然是木頭**
  ↓ 缺
(defaultSurface, kind)        例：Stone Shuffle
  ↓ 缺
(defaultSurface, Walk)
```

音色類別不精準只是不夠細緻；**材質跑掉會直接出戲**——那正是本輪要修的問題。
⛔ 絕不靜默挑一個不相干的材質。

---

## 4. Gait 分池

### 4.1 步態的權威來源

⚠️ **沒有自己用 world speed 猜一套。**

`MovementIntentData` 明文是 model-agnostic、**不含 gait 語意**；
真正決定步態的是 `PlayerLocomotionPolicy`（持有 `GaitProfileSO`，實作「預設 Run／Sprint 鍵／Walk 鍵」
這套 per-game 控制方案的那一顆）。**所以就由它 commit**：

```csharp
data.Gait = walkActive ? GaitTier.Walk
          : input.SprintButtonHeld ? GaitTier.Sprint
          : GaitTier.Run;
```

與 `GaitProfileSO.ResolveIntensity` 的「Walk 優先於 Sprint」規則**逐字一致**——
強度與檔位必須同源，否則會出現「聽起來在衝刺、實際在走」。

⛔ **音效端不得從 `MoveSpeed` 反推步態**（`docs/16` 加重項：下游重新推導已 commit 的狀態）。
而且會推錯：B9 平滑讓 `MoveSpeed` 要數十帧才爬到目標，反推會在整段加速期給出錯的檔位。
`ArchitectureRegressionTests` 的 `WriterRules` 已鎖住這一條。

**預設值刻意是 `Run` 而非 enum 的 0 值**：本作控制方案是「不按修飾鍵＝Run」，
且沒有 policy 的角色（AI 敵人）也應該聽起來在跑。

### 4.2 WASD Free Bundle 實際有什麼

實測資料夾：`Drop / Jump / Run / Shuffle / Sneak / Walk` × `Dirt / Grass / Stone / Wood` × 6。
⇒ **沒有 Sprint 素材**。依使用者預先裁決：**Sprint 直接用 Run 池**，
⛔ 不建立 pitch 合成的假 Sprint。差異只體現在音量（`sprintMultiplier`）。

### 4.3 最終對照表（實跑 Bank 產出）

| Surface | Walk | Run | Sprint | Shuffle | Landing |
|---|---|---|---|---|---|
| Stone | Stone_Walk | Stone_Run | **Stone_Run** | Stone_Shuffle | Stone_Landing |
| Wood | Wood_Walk | Wood_Run | **Wood_Run** | Wood_Shuffle | Wood_Landing |
| Grass | Grass_Walk | Grass_Run | **Grass_Run** | Grass_Shuffle | Grass_Landing |
| Dirt | Dirt_Walk | Dirt_Run | **Dirt_Run** | Dirt_Shuffle | Dirt_Landing |
| *Unknown* | Stone_Walk | Stone_Run | Stone_Run | Stone_Shuffle | Stone_Landing |

Shuffle 與 Landing **不分 gait**（使用者明確指定）。

---

## 5. 音量集中處：`FootstepAudioBank.asset`

> **要調整整體腳步音量，只需要動 `Master Volume` 一個欄位。**

| 欄位 | 預設 | 作用 |
|---|---:|---|
| `masterVolume` | 1.00 | ⭐ 腳步聲總音量 |
| `normalStepVolume` | 1.00 | Normal Step |
| `shuffleVolume` | 0.45 | Shuffle／Scuff |
| `landingVolume` | 1.10 | Landing |
| `walkMultiplier` | 0.90 | 只作用於 Normal Step |
| `runMultiplier` | 1.00 | 同上 |
| `sprintMultiplier` | 1.05 | 同上 |
| `leftFootPitchScale` | 0.98 | 左右腳音高微差 |
| `rightFootPitchScale` | 1.02 | 同上 |

**最終音量 = clip 池自身音量 × master × 類別係數 × gait 係數**。

分層原則：**個別池的相對音量留在各自的 `AudioDefinitionSO`**（那是音色的一部分，例如某組素材天生較悶），
**跨類別的相對關係與總音量集中在 Bank**。16 個池的 `volume` 目前一律 1.0 ⇒ 不需要一顆一顆調。

---

## 6. AudioSource 池

`FootstepAudioSourcePool`：6 顆（可調 2–16），依 `AudioController.audioSource` 這顆**樣板**複製 3D 設定。
⇒ **調 3D 設定只要改樣板那一顆，整池跟著變。**

解掉三件事：

| 問題 | 解法 |
|---|---|
| 後一發的 `pitch` 改到仍在播的前一發 | 每發借一顆**獨立** Source |
| 只能從角色中心播 | 左腳→左腳位置、右腳→右腳位置、落地→角色位置 |
| 左右腳聽起來一樣 | 各自的 pitch scale（0.98／1.02）＋ 獨立洗牌袋 |

池滿時搶用最早借出的那一顆——腳步是高頻短音，**讓新的一步沒聲音比讓舊的一步播完更糟**。
⛔ 沒有優先權／virtualization／category routing——**這不是通用 Audio Framework**。

---

## 7. ~~DSP Buffer（Stage 2F）~~ ⛔ **2026-09-17 撤銷，不做**

> **使用者明確裁決：不需要 DSP buffer A/B，腳步聲維持現況不再微調。**
> 本節以下內容保留為當時的分析紀錄，⛔ **不是待辦**。正本：`docs/37` §12。


### 7.1 實測現況

```
dspBufferSize = 1024 samples
sampleRate    = 48000 Hz
numBuffers    = 4            ← ⚠️ 不是 2
單一 buffer   = 21.33 ms
4 個 buffer   = 85.33 ms
speakerMode   = Stereo
```

> ⚠️ **這修正了 `docs/34`／`docs/35` 的估計。** 先前我寫「21–43 ms（假設雙緩衝）」，
> 實際是 **4 個 buffer**。輸出延遲不等於 85 ms 的全部（其中一部分是佇列深度而非額外延遲），
> 但**它確實是目前剩餘延遲中最大的一項**，比管線那一帧（16.7 ms）大得多。

### 7.2 可選模式與代價

| 設定 | buffer | 單 buffer | ×4 | 代價 |
|---|---:|---:|---:|---|
| Best performance | 1024 | 21.3 ms | 85.3 ms | **目前值**。最不容易爆音 |
| Default | 通常 1024 | 21.3 ms | 85.3 ms | 同上 |
| **Good latency** | 512 | 10.7 ms | 42.7 ms | **建議先試這個**。約砍一半 |
| Best latency | 256 | 5.3 ms | 21.3 ms | 砍到 1/4，但 audio thread 被喚醒的頻率變 4 倍 |

**代價的本質**：buffer 越小，audio thread 必須越頻繁地在固定期限內填滿資料。
一旦某次來不及（GC spike、主執行緒卡頓、其他程序搶 CPU）就會**爆音／破音（crackle）**。
在編輯器裡尤其容易發生，因為 Editor 本身的負載比 build 高。

### 7.3 ⛔ 本輪沒有改

這是**全域設定**，影響所有音效，而且使用者明確說「不要為了腳步聲直接把整個專案改成極端 low-latency」。
**建議**：先完成 Play 驗收；若仍覺得腳步慢半拍，再單獨把 DSP Buffer Size 改成 **Good latency (512)** 做一次 A/B，
確認沒有爆音後再考慮是否要到 256。**這是一個欄位的改動，隨時可回復。**

---

## 8. 場地製作工作流（使用者問的重點）

### 8.1 新增一張場景時

```
1. 做完場景
2. Tools ▸ Project ▸ Scene Surface Audit
3. 指定 Surface Map 資產 → 按 Scan Scene
4. 看「未對應」清單：
     · 數量通常是個位數（一顆 Material 被幾十個物件共用）
     · 工具會依名稱給**建議**，但由你確認
5. 逐筆下拉選材質，或按「把 N 個未對應的 Material 依建議加入」再逐筆修正
6. 完成 —— 整張場景自動生效
7. 只有少數特例才掛 SurfaceOverride
```

### 8.2 審計工具會告訴你什麼

```
共 6 種 Material；已對應 6，未對應 0
 ✔  GridOrange_01_Mat    26 collider   → Stone    Box_100x100x100_Prefab
 ✔  GridWhite_01_Mat      6 collider   → Stone    Stairs_650_400_300_Prefab
 ✔  Blue_Mat              4 collider   → Stone    border
 ✔  GreyBlue_Mat          1 collider   → Stone    Ground
 ?   Bridge_Mat           2 collider   建議 Wood   WoodBridge
```

另會額外警告：
* 取不到 Material 的 collider（沒有 Renderer）＝**這些要靠 Override**
* 場景中的 TerrainCollider 數量（提醒去設 Terrain Layer 對應）
* 已被 SurfaceOverride 接管的 collider 數量（預期行為，不是問題）

### 8.3 什麼時候該用 Override、什麼時候不該

| 該用 Override | **不**該用 Override |
|---|---|
| 美術 Material ≠ 聲學材質（貼石紋的木橋、鋪地毯的木地板） | 一般地板——**設 Material 對應就好** |
| 純碰撞代理（有 collider 沒 Renderer） | 「這塊地板比較特別」——先問是不是該給它自己的 Material |
| 一個 Mesh 含多材質、第一槽取不到正確資訊 | 大量物件——**那代表 Material 對應沒設好** |
| 單一特例，開一筆 Material 對應反而更難維護 | |

> ⚠️ **判斷準則**：如果你發現自己在掛第 5 個 Override，停下來回去看 Material 對應。

---

## 9. 驗證

### 9.1 對真實場景實跑解析

```
DefaultSurface = Stone
地面 collider 39 個 → 解析出非 Unknown 的 39 個
   39x → Stone
'Ground' 連續解析 1000 次 → Stone，穩定 = True
```

⇒ **沒有任何一塊地板會沒有聲音**。

### 9.2 EditMode 測試

新增 `FootstepSurfaceTests`（13 條），關鍵幾條：

| 測項 | 鎖住什麼 |
|---|---|
| `AllThreeEventKinds_ShareOneSurfaceDecision` | ⭐ Normal／Shuffle／Landing 不得有任何一個掉回別的材質 |
| `MissingShuffle_FallsBackWithinSameSurface_NotToAnotherSurface` | ⭐ 缺素材時寧可換 kind 不換 surface |
| `UnmappedMaterial_FallsBackToDefaultSurface` | 沒配置的地板仍有聲音 |
| `SurfaceOverride_BeatsMaterialMapping` / `_OnParent_CoversChildColliders` | 優先級與「掛父層即可」 |
| `NoCollider_ResolvesToUnknown_AndDoesNotReuseStaleResult` | 空中不得沿用上一次的材質 |
| `SwitchingCollider_ReResolves` | 記憶化不得吃掉材質變化 |
| `Sprint_UsesRunPool` | 沒有 Sprint 素材時的既定取捨 |
| `MasterVolume_ScalesEverything` | 音量集中控制確實生效 |

**Stage 1 的回歸鎖全數保留**（樓梯、碎步、擺盪中低點…）。

---

## 10. 已知缺口（Stage 3）

| 缺口 | 說明 |
|---|---|
| **demo map 全是 Stone** | 這是**誠實**的對應——greybox 場景本來就沒有木頭或草地。想在 demo 裡聽到其他材質，把 `GridWhite_01_Mat`（樓梯／斜坡）改成 Wood 即可，**一個下拉選單**。素材已全部備妥 |
| **Sneak / Jump 素材未接** | WASD 有 Sneak 與 Jump，但目前沒有對應的 gait／事件 |
| **`FootIK.GroundLayers = Everything`** | 腳部 ray 可能打到角色自己；屬 Foot IK 設定，本輪未動 |
| **Terrain 路徑有 GC** | `GetAlphamaps` 每次配置；目前場景無 Terrain 故未觸發 |
| **非腳步音效仍走單一 AudioSource** | 那些事件不會高頻重疊，沒有理由開池 |
| **DSP buffer** | 見 §7 |
| **Reverb / occlusion / 多材質混合** | 使用者明確排除 |

---

## 11. 檔案

**新增**：`Footstep/SurfaceId`（在 `Core.Blackboard`）、`Footstep/SurfaceOverride.cs`、
`Footstep/SurfaceMapSO.cs`、`Footstep/SurfaceResolver.cs`、`Audio/FootstepAudioBankSO.cs`、
`Audio/FootstepAudioSourcePool.cs`、`Editor/SceneSurfaceAuditWindow.cs`、
`Tests/EditMode/FootstepSurfaceTests.cs`、96 顆 wav、16 個 clip pool 資產、
`FootstepAudioBank.asset`、`SceneSurfaceMap.asset`

**改寫**：`PresentationEventData.cs`（＋`Surface`／`Landed`／腳部位置／`SurfaceId`／`GaitTier`）、
`PlayerRuntimeData.cs`（＋`Gait`）、`PlayerLocomotionPolicy.cs`（commit `Gait`）、
`FootstepDetector.cs`、`PresentationPipeline.cs`、`AudioController.cs`、
`ArchitectureRegressionTests.cs`（`Gait` 的 WriterRule）、兩個角色 prefab

**刪除**：`Audio_LeftFootstep/RightFootstep/Landing/Shuffle.asset`（已由 per-surface pool 取代）、
`MainAudioLibrary` 的 4 筆 entry（腳步已改由 Bank 負責；Library 保留給未來的非腳步事件）
