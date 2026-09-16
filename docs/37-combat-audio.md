# 37 — 戰鬥音效（Combat Audio）

> **狀態**：程式 ／ 資產 ／ **素材** ／ 測試皆已落地
> （EditMode **592 / 591 passed / 0 failed / 1 skipped**、PlayMode **61 / 61**）。
> **56 個 clip 已匯入並接線 ⇒ 現在 Play 應該聽得到聲音。**
> ⛔ **但選材沒有經過耳朵**——我無法試聽音訊，依據只有客觀量測、檔名語意與專案已知事實。
> 試聽頁：`D:/Unity Project/_AudioResearch/Combat/audition.html`（瀏覽器開啟，含 56 個選用 ＋ 31 個備援）。
> 素材來源、授權與重建程序見 §6。
>
> **routing**：本輪**不開 ADR**。唯一的跨系統契約變更是黑板加了一個 `SurvivabilityData.JustDied`，
> 它與既有的 `JustTookDamage` 同寫入者、同發布點、同生命週期，屬**加法**而非 ownership 變更
> ⇒ 依 CLAUDE.md 的 routing rule 直接寫 Living Docs（`docs/02` §1 已同步）。
> 先例：Phase C1 Forward Stop（`docs/07`）與 Footstep（`docs/35`／`36`）都是這樣落地的。

---

## 1. 這一輪要解的問題

腳步聲（`docs/35`／`36`）之後，遊戲裡**唯一有聲音的事情是走路**。
打人、被打、放法術、死掉全部無聲 —— 這是目前最大的「最有感」缺口。

**明確不做**（使用者裁決）：完整 Audio Framework、FMOD／Wwise、event bus、
mixer 路由、ducking、優先權／virtualization、腳步聲的進一步微調。

---

## 2. ⭐ 核心原則：不發明時間，只接既有的時間權威

使用者的原話是「**不要靠動畫時間硬猜**」。本輪的每一個事件都遵守這條，作法是：
**把音效掛在「已經有人在決定這件事什麼時候發生」的那個回呼上。**

| 決定者 | 它已經在那一刻做什麼 gameplay 的事 | 音效搭便車 |
|---|---|---|
| `ActionState.NotifyBegin()` | 進入 Action、取得 facing 承諾 | Cast |
| `ActionState.NotifyRelease()` | **生成投射物／開啟命中窗／結算 AoE** | Release／Swing |
| `ThrownProjectile.FixedUpdate` 掃掠命中 | 截斷位移、生成 impact VFX、結算傷害 | Projectile Impact |
| `MeleeHitboxSink.TryRequestHit` | 去重通過、`ApplyDamage` | Melee Hit |
| `GroundEffectSink.Release` | 探地、生成冰刺視覺、結算 AoE | Ice Impact |
| `CharacterHealth.ApplyDamage` | commit 受擊／死亡 | Hurt／Death |
| `MotionDriver.SyncGroundedState` | 觸地狀態邊沿偵測 | Jump Takeoff |

⇒ **聲音與 gameplay 結構上不可能不同步**，因為它們讀的是同一次呼叫、同一個欄位。
沒有任何一處是「等 0.3 秒之後大概就揮到了」。

---

## 3. 事件對照表（**這是本輪的交付清單**）

| 事件 | Trigger source（程式位置） | AudioDefinition 資產 | 3D 位置 | pitch 是否互相干擾 |
|---|---|---|---|---|
| **Fireball Cast** | `ActionAudioSink.Begin()` ← `ActionState.NotifyBegin`，Slot 2 | `Combat/Fireball_Cast` | ✅ `emitPoint`（未指派 ⇒ sink 所在物件，即角色 root） | ❌ 否（每發獨立 Source） |
| **Fireball Release** | `ActionAudioSink.Release()` ← `ActionState.NotifyRelease`，Slot 2 | `Combat/Fireball_Release` | ✅ 同上 | ❌ 否 |
| **Fireball Impact** | `ThrownProjectile.FixedUpdate` 掃掠命中，`hit.point` | `Combat/Fireball_Impact` | ✅ **`hit.point`**（與 impact VFX 同一點） | ❌ 否 |
| **Ice Cast** | `ActionAudioSink.Begin()`，Slot 3 | `Combat/Ice_Cast` | ✅ 角色 | ❌ 否 |
| **Ice Release**（施法完成） | `ActionAudioSink.Release()`，Slot 3 | `Combat/Ice_Release` | ✅ 角色 | ❌ 否 |
| **Ice Impact**（冰刺冒出來） | `GroundEffectSink.Release()`，探地後的 `center` | `Combat/Ice_Impact` | ✅ **落點**（身前 0.4–4.4 m，不是角色身上） | ❌ 否 |
| **Melee Swing** | `ActionAudioSink.Release()`，Slot 1 | `Combat/Melee_Swing` | ✅ 角色 | ❌ 否 |
| **Melee Hit** | `MeleeHitboxSink.TryRequestHit()` 去重通過後 | `Combat/Melee_Hit` | ✅ **被打者 root** | ❌ 否 |
| **Player Hurt** | `AudioController.Tick` ← `Survivability.JustTookDamage` | `Combat/Player_Hurt`（經 `PlayerAudioLibrary`） | ✅ 角色 | ❌ 否 |
| **Enemy Hurt** | 同上（**同一行程式**） | `Combat/Enemy_Hurt`（經 `EnemyAudioLibrary`） | ✅ 角色 | ❌ 否 |
| **Player Death** | `AudioController.Tick` ← `Survivability.JustDied` | `Combat/Player_Death` | ✅ 角色 | ❌ 否 |
| **Enemy Death** | 同上（**同一行程式**） | `Combat/Enemy_Death` | ✅ 角色 | ❌ 否 |
| **Jump Takeoff** | `AudioController.Tick` ← `JustLeftGround` | `Combat/Player_JumpTakeoff` | ✅ 角色 | ❌ 否 |
| ~~Landing~~ | 既有，未動（`FootstepAudioBank`，走 Drop 素材） | — | ✅ 角色 | ❌ 否 |

### 3.1 為什麼「pitch 互相干擾」整欄都是否

`pitch` 與**位置**都是 `AudioSource` 的**狀態**，不是 `PlayOneShot` 的參數
⇒ 共用一顆 Source 時，後一發設定的 pitch 會**改到仍在播的前一發**。
本輪把所有路徑（含原本用 `PlayOneShot` 的 Library 事件）都改走 `AudioSourcePool`，
**每一發借一顆獨立的 Source** ⇒ 結構上不可能互相改到。

⚠️ **代價**：池滿時會搶用最早借出的那一顆（例如長的死亡音被後續腳步切斷）。
本輪**刻意不加優先權**——那是 Audio Framework 的第一步。
緩解手段是把 `AudioController.audioSourceCount` 開大（已由 6 → **10**）。

### 3.2 玩家與敵人為什麼共用同一個 `AudioEventId`

`AudioEventId.Hurt`／`Death` **沒有** `EnemyHurt` 版本。
聽起來不同是因為兩隻角色掛的是**不同的 `AudioLibrarySO` 資產**。

⛔ 不得為敵人再開一個列舉值——那會把**陣營**寫進表現層的詞彙，
而陣營在本專案不存在（`CharacterHealth` 第一行註解：**生存語意不分陣營**）。

---

## 4. 哪些抽成共用、哪些留在 Footstep

使用者明確要求：**不要讓 Combat 直接依賴 Footstep 專用型別；要共用就抽最小通用層。**

### 4.1 ✅ 抽成共用的

| 元件 | 動作 | 理由 |
|---|---|---|
| **`AudioSourcePool`** | 🔄 由 `FootstepAudioSourcePool` **更名** | 它**原本就一行腳步知識都沒有**（`Play` 的參數只有 clip／位置／音量／音高）。舊名字描述的是它的**第一個呼叫者**，不是它的職責。行為逐位元不變 ⇒ 零測試改動 |
| **`AudioDefinitionSO`**（clip 池 ／ shuffle bag ／ volume jitter ／ pitch range） | **零改動** | 早就是通用的，且已在 `Presentation/Audio` 而非 Footstep 命名空間。戰鬥事件直接複用 |
| **`AudioLibrarySO`**（event → definition O(1) 查表） | **零改動**，只加三個 enum 值 | 同上 |
| **`AudioController`** | 🆕 加 `PlayAt(definition, worldPosition)` | 成為**本角色音效輸出的唯一持有者**（見 §4.3） |

> ⭐ **這一輪最便宜的發現**：使用者問「哪些可以抽成真正共用」——
> 答案是**四項裡有三項根本不用抽，它們早就是通用的**。
> 真正需要動的只有一個名字。
> 前一輪（Footstep Stage 2）把通用能力放在通用的位置，這一輪就只付了改名的成本。

### 4.2 ⛔ 留在 Footstep domain 的

| 元件 | 為什麼不抽 |
|---|---|
| `FootstepAudioBankSO` | 它的 key 是 **(Surface × Kind × Gait)**，三個維度對戰鬥全部無意義。硬抽成泛型 bank 只會得到一個到處是 `Unknown` 的表 |
| `FootstepDetector` ／ `FootPlantTracker` | 讀 Foot IK pose 判落腳。戰鬥事件有自己的時間權威（§2），不需要偵測 |
| `SurfaceResolver` ／ `SurfaceMapSO` ／ `SurfaceId` | 「踩在什麼材質上」。⚠️ 戰鬥**將來**可能要（冰刺打在木板 vs 石頭）——但**第二個使用者還沒出現**，CLAUDE.md 禁止先抽 |
| `FootstepKind` ／ `GaitTier` | 腳步的詞彙 |

⇒ **Combat 端沒有任何一行 `using` 指到 Footstep 型別。** 由 `ArchitectureRegressionTests` 的 LayerRules 白名單維持。

### 4.3 ⭐ 為什麼 `AudioController` 成為輸出持有者（而不是各元件自己開 AudioSource）

`PresentationPipeline` 的類別註解早就標記了這個問題：

> 「未來若多個 Controller 競爭同一輸出（例如兩個模組都要控制 `AudioSource`／同一根骨骼），
> 屆時升級為表現層的輸出仲裁並補 ADR」

戰鬥音效正是那個「未來」。若 `ActionAudioSink`／`MeleeHitboxSink`／`ThrownProjectile`
各自 `AddComponent<AudioSource>()`，一個角色身上會有五、六個彼此不知道對方存在的音源：
3D 設定要改六個地方、同時播放時誰也不讓誰、pitch 各自為政。

⇒ **本角色的 `AudioSource` 由一個元件持有**，其他人只說「播什麼、在哪裡播」。

**⛔ 這不是 singleton**：一個角色一顆，呼叫端一律靠 `[SerializeField]` 或
`GetComponentInParent` **沿階層**取得。沒有靜態欄位、沒有 `FindObjectOfType`、沒有全域註冊表。
玩家與敵人各有各的，兩者互不知道對方存在。

**⚠️ 已知侷限**：音源屬於**角色**，不屬於**聲音**。角色被銷毀時仍在播的聲音會一起消失
⇒ 射手若在火球飛行途中被回收，那一發命中音會沒了。
本輪接受；正確解是場景層音效服務，而那是 Framework。

---

## 5. 兩個實作過程中被證據糾正的判斷

> 這兩條記在這裡，是因為它們都是「看起來對、實際會壞」的那一類。

### 5.1 🐛 差點加錯的節流閘門（連段會變成只響一聲）

我原本在 `ActionAudioSink.Release` 加了 `_releasedThisExecution` 閘門（比照 `MeleeHitboxSink`）。
讀 `ActionState.TryAdvanceChain` 才發現它**刻意**在每個連段重置 `_releaseEmittedThisExecution`
並再發一次 Release（註解原文：「每段各出一顆投射物」）
⇒ 加閘門會讓三段連擊**只響第一聲**。

**「一次 Action 該響幾聲」已經由 `ActionState` 回答完了，sink 不得重複回答。**
這與 2026-09-05 `ThrowProjectileEmitter` 吞掉第 2、3 段（`docs/11` §4.3）是**同一個缺陷的複刻**。
由 `CA10` 釘住——它直接斷言 `ActionAudioSink` **不得存在**名為 `_releasedThisExecution` 的欄位。

### 5.2 為什麼死亡音需要新欄位，不能對 `IsDead` 做邊沿偵測

`IsDead` 是**狀態**（現在是不是屍體），死亡音需要的是**事件**（就是這一幀死的）。
讓 `AudioController` 自己快取上一幀的 `IsDead` 去比對，會是 `docs/16` 點名的加重缺陷
「**下游重新推導已 commit 的狀態**」——而且每多一個消費者（VFX、ragdoll、鏡頭震動）
就多一份 `_wasDead` 快取，它們會因讀取時機不同而**彼此不一致**。

⇒ 讓**知道的人說出來**：`CharacterHealth.ApplyDamage` 正是把 `_isDead` 翻成 true 的那一行。
`JustDied` 與 `JustTookDamage` 逐字同構（同寫入者、同發布點、寫出即清、不走 `ResetTransientState`）。
一個欄位換掉 N 份會漂移的推導。由 `CA1`–`CA6` 守住，其中 **`CA2` 是關鍵**：
它斷言 `JustDied` 只有一幀而 `IsDead` 維持為真——有人改回讀 `IsDead` 時它會立刻紅。

---

## 6. 素材：Sonniss GDC 2026 —— **已下載、已選材、已匯入**

> 🔄 **2026-09-17 修訂：本節的第一版結論「不建議下載」被自己的量測推翻，已改寫。**
> 那個結論建立在「346 檔 ÷ 121 library ≈ 每個 library 只有 2–4 顆 ⇒ 湊不出 shuffle 池」。
> **錯在把「檔案數」當成「素材數」。** 實際切開之後：`SWSH_SWING IMPACTS` **一個檔 61.8 秒、
> 內含 21 個獨立揮擊 take**；`WEAPBlnt_Spear And Stick Impact` 16 個；`FGHTImpt_4 x Punch` 4 個。
> production library 的慣例就是「一個檔放一整組 take」。
> ⚠️ **教訓：track list 只能告訴你有什麼，不能告訴你有多少。**

### 6.1 分卷對照（用 HTTP Range 讀 central directory，**沒有下載整包**）

| 分卷 | 大小 | 內容 | 戰鬥相關 |
|---|---:|---|---|
| part1 | 1.21 GB | `344 Audio` 自己的 library，**A→H** | ⚠️ 少量：Anime Fight Voices 1+2、Cinematic Fight、Historical Weapons、Elemental Palette |
| **part2** | 1.32 GB | Alexander Kopeikin／Cinematic Sound Design／David Dumais／Epic Stock Media／Federico Soler | ✅ **全部好東西都在這裡** |
| part3 | 1.39 GB | InMotionAudio／Ivo Vicic／Jake Fielding／Just Sound Effects | ⛔ 純環境音 |
| part4 | 1.73 GB | Sonic Bat／Sonik Sound Library | ⛔ 純環境音 |
| part5 | 0.80 GB | SoundBits／The Noisery／TheWorkRoom／Victor Ermakov | ⚠️ 只有 Vox Hominis ＋ Vox Bestiae |

> 📌 **使用者指定的 part1 剛好不是重點那一卷**——part1 是 344 Audio 的 A→H
> （古董／理髮廳／聖誕節／恐怖氛圍）。**part2 才是。**

### 6.2 實際下載量：**256 MB，不是 6.45 GB**

zip 支援 Range ⇒ 可以只抓需要的**檔案**，比「只抓需要的分卷」更細一級。
從 part1／part2／part5 共抽出 **63 個檔**（13 個 library）。

🐛 **踩過一次**：`probe.sh` 第一版沒驗證 `Content-Length`，抓不到時 `TAILSTART` 變負數
⇒ Range 標頭失效 ⇒ 伺服器回 200 整包 ⇒ **本來只想讀 1 MB 目錄，卻開始下載 1.2 GB**。
現在有兩道硬性驗證（見 `Tools/audio-pipeline/probe.sh`），⛔ 不要拿掉。

### 6.3 處理：63 個母帶 → **87 個 game-ready clip**

每個 clip 都做四件事（`Tools/audio-pipeline/cut.mjs`）：

| 步驟 | 為什麼 |
|---|---|
| 依靜音切出單一 take | 母帶是「一個檔 20 個 take」 |
| **砍掉前置靜音** | 本 repo 踩過的坑（`docs/34`：腳步 clip 29.7 ms 前置靜音是延遲預算最大單項）。這批母帶更糟——`ice, crack` **514 ms**、`magic, drone` **639 ms**。攻擊音晚到半秒等於沒有 |
| 48 kHz / mono / 16-bit PCM | 與既有腳步素材同規格。**mono 是 3D 音效的必要條件**——立體聲檔不會被正確空間化 |
| peak 正規化至 −1 dBFS | 讓同一個池裡的變化版本音量一致。⛔ **刻意不用 compressor／loudnorm**——它們會壓掉 transient，而打擊感就是 transient |

### 6.4 選材結果（Selected／可用）

| 事件 | 選用 | 來源 | 備註 |
|---|---:|---|---|
| Melee Swing | **9** / 22 | David Dumais `SWSH_SWING IMPACTS`（21 takes 同源） | ✅ 池內一致性最好 |
| Melee Hit | **10** / 28 | `4 x Punch, Body` ＋ `Sword Slide Cuts` ＋ `METAL SWING HIT` | ⛔ **排除 16 個木棍撞擊**——角色持劍（`slash1` ＋ WeaponSocket），語意不符 |
| Fireball Cast | 2 | `MAGAngl_Magic Light Spell`、`DSGNStngr_Power Up Bright` | ⚠️ 薄 |
| Fireball Release | 3 | `FIREWhsh_Whoosh Fire Deep Growl`、`Woosh Debris`、`Woosh Sweep` | |
| Fireball Impact | 4 | `EXPLDsgn_Explosion Small Blast`、Colossal/Ultra Impacts | |
| Ice Cast | 2 | `GLASMvmt_Whoosh Glass Crystal`、`ICEFric_Dry Ice Squeal` | ⚠️ 薄 |
| Ice Spawn | 3 | `ice, surface cracking, fissure`、`ice block snapping` | |
| Ice Impact | 5 | `ice, block of ice crushed`、`ICEFric`、`ELECMisc` | |
| Player Hurt | 4 | Anime Fight Voices（男 2／女 2） | |
| Enemy Hurt | 2 | `HMNBrth_Screeching Breath`、`CREAEthr_Grim Pain` | ⚠️ 薄 |
| Player Death | **1** | `VOXReac_Flutter Death Vocal` | 🔴 **最薄的一項** |
| Enemy Death | 2 | `CREAMnstr_Sea Beast Pain` ＋ 疊 `EXPLDsgn` 小爆炸 | |
| Jump Takeoff | 8 | `HMNBrth_Panting Male`（7 takes）＋ 吐氣 | |

⚠️ **法術與死亡仍然薄**，正如使用者預期的「Sonniss 適合真實 weapon／impact／body Foley，
法術需要另外找 designed／stylized 的素材」。⇒ 見 §6.6。

### 6.5 ⛔ wav **不進版控**——這是授權問題，不是檔案大小問題

License v2.0 明文禁止「distribute, publish, sub-license or otherwise supply the sound effects
**as sound effects** to any other person」，而本 repo 是**公開**的
（`github.com/Baka8787/IntentPipeline`）⇒ commit 原始 wav 等同以音效的形式散布。
授權允許的是散布**成品專案**，不是散布素材本身。

⇒ 比照既有的 `MovementAnimsetPro`（付費 Kubold 資產）處理：`.gitignore` 排除
`Assets/Audio/Combat/**/*.wav` 與 `*.wav.meta`。

**⭐ 素材不在 git 裡，但「怎麼得到它」在**：完整重建程序在 `Tools/audio-pipeline/`
（Range 盤點 → 選擇性抽取 → 量測 → 切段 → 選材 → 匯入 → 接線，10 支腳本）。

⚠️ **重跑之後 GUID 會變** ⇒ `AudioDefinition` 的 clip 參照會斷掉。
`WireClips.cs` 以「資料夾 → 定義」重新接線、不依賴 GUID，跑一次即恢復。

### 6.6 ⛔ Boolean SFX Free Sampler —— **沒有下載，建議也不要**

使用者指定的候選（`booleanbeats.itch.io`）查過了：**40 個 MP3，128 kbps，共 1.2 MB。**

| 問題 | 說明 |
|---|---|
| **128 kbps MP3** | ⛔ 不是可用的素材來源格式。MP3 有 encoder delay／padding ⇒ **天生帶前置靜音**，正是 §6.3 花力氣在砍的東西 |
| 有損疊有損 | 匯進 Unity 再壓一次 |
| 12 個分類 40 個音 | 每類約 3 個 ⇒ 又是 sampler 問題 |

📌 **下載本身需要走 itch.io 的 name-your-own-price 流程**（要人工點「No thanks, just take me
to the downloads」）——依使用者指示「需要登入／人工接受條款／付款頁就停下」，**沒有繞過**。
但即使繞過也不建議用：品質不足以當本專案的素材來源。

**法術素材缺口的正解**：一個真正的遊戲 SFX 包（每個事件 5–10 顆 wav 變化），
而不是 GDC bundle 也不是 128 kbps sampler。這是採購決定 ⇒ 留給使用者。
## 7. 已接線的資產（可還原清單）

| 資產 | 改了什麼 | 怎麼還原 |
|---|---|---|
| `Assets/ScriptableObjects/Audio/Combat/*.asset` ×13 | **全新建立**，clip 池已填（2–10 顆／事件）＋ volume／jitter／pitchRange | 刪資料夾 |
| `Assets/Audio/Combat/**` | **56 個 wav 匯入**（48k/mono/PCM/preload/forceToMono/normalize off）。⛔ wav 與 wav.meta 已 gitignore（授權，見 §6.5）；資料夾 .meta 與 `SOURCES.md` 有進版控 | 刪資料夾；重建走 `Tools/audio-pipeline/` |
| `Tools/audio-pipeline/` | **新建** 11 支腳本 ＋ README＝素材重建程序 | 刪資料夾 |
| `.gitignore` | ＋ Sonniss 衍生 wav 的排除規則（授權理由，非體積） | revert 該段 |
| `Assets/ScriptableObjects/Audio/PlayerAudioLibrary.asset` | **新建**，3 筆（Hurt／Death／JumpTakeoff） | 刪檔 |
| `Assets/ScriptableObjects/Audio/EnemyAudioLibrary.asset` | **新建**，2 筆（Hurt／Death；敵人不跳） | 刪檔 |
| `Assets/Prefabs/X Bot.prefab` | ＋3 顆 `ActionAudioSink` 子物件（Melee／Fireball／Ice）＋3 筆 slot binding；`MeleeHitboxSink.hitAudio`、`GroundEffectSink.impactAudio` 填入；`library` → `PlayerAudioLibrary` | `git checkout` 該檔 |
| `Assets/Prefabs/Y Bot.prefab` | `library` → `EnemyAudioLibrary`；`MeleeHitboxSink.hitAudio` 填入 | `git checkout` 該檔 |
| `Assets/Prefabs/Projectile_Fireball.prefab` | `ThrownProjectile.impactAudio` → `Fireball_Impact` | `git checkout` 該檔 |

⚠️ **`footstepSourceCount` → `audioSourceCount` 的更名會清掉既有序列化值。**
兩份 prefab 原本存的都是**預設值 6**，而新欄位預設 **10**（戰鬥＋腳步共用池需要更多）
⇒ **刻意不加 `[FormerlySerializedAs]`**：加了只會把 6 搬過來，而那正是我們不要的數字。
損失為零（沒有任何人調整過它）。

⚠️ 上述資產改動**不會**被 `dotnet build` 驗到——只有 Unity 匯入成功 ＋ 上述測試通過，
「聽起來對不對」完全沒有測試保護。

---

## 8. ⛔ 還沒做的：人工 Play 驗收

**clip 已經填好了 ⇒ 直接 Play 就有聲音。**

⚠️ **最重要的一項在第 0 條**——其餘七條是技術驗收，第 0 條才是這批素材真正的未知數。

0. 🔴 **選材對不對**（我沒有耳朵，這一項完全沒有被驗證過）：
   先開 `D:/Unity Project/_AudioResearch/Combat/audition.html` 逐個聽過，
   再進 Play 聽在**動畫與畫面尺度**下對不對。重點懷疑：
   ①近戰揮擊／命中是否夠「厚」且與 `slash1` 的重量相符；
   ②法術是否太 UI／太卡通（素材來自 designed library，不是實錄）；
   ③人聲是否出戲（Anime Fight Voices 是**動漫風**，男女混雜）。
   換素材只要把 `Cut/` 裡的備援拖進 `Assets/Audio/Combat/...` 再跑一次 `WireClips.cs`。

技術驗收：

1. **Fireball**：Cast 與 Release 是兩聲還是糊成一聲（`FallbackDuration 1.2 × ReleaseNormalizedTime 0.35` ⇒ 相隔 **0.42 s**）
2. **Fireball Impact**：聲音是否出現在**接觸點**而不是角色身上（火球飛遠時差異最明顯）
3. **Ice**：`Ice_Release`（角色身上）與 `Ice_Impact`（身前 0.4–4.4 m）**同時**播——會不會互相糊掉
4. **Melee 連段**：三段連擊是不是**三聲**（若只有一聲，§5.1 的閘門被重新加回去了）
5. **Hurt／Death**：玩家與敵人聽起來是否不同（不同的 Library 資產）；致死時**不得**同時聽到受擊聲
6. **Jump Takeoff**：⚠️ **走下平台邊緣會不會也有起跳聲**——`JustLeftGround` 是「離地」不是「起跳」（已知近似，見 `AudioEventId.JumpTakeoff`）
7. **池子夠不夠**：連續戰鬥中有沒有聲音被切斷；有就把 `audioSourceCount` 開大，⛔ **不要**去 `AudioSourcePool` 加優先權

---

## 9. 後續（本輪明確不做）

- **DSP buffer A/B**（`ProjectSettings/AudioManager.asset` 1024 → 256，可省 16–32 ms）——使用者裁決：**戰鬥音效完成後再做**
- 優先權 ／ virtualization ／ ducking ／ mixer 路由
- 戰鬥音效的 Surface 感知（冰刺打在木板 vs 石頭）——第二個使用者尚未出現
- Jump Takeoff 的精確事件源（目前借用 `JustLeftGround`）
