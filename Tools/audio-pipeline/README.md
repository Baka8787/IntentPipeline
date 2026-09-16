# 戰鬥音效素材管線（2026-09-17）

**這是 `Assets/Audio/Combat/**/*.wav` 的重建程序。**

那些 wav **刻意不進版控**——不是因為太大，而是 Sonniss License v2.0 禁止
「distribute, publish, sub-license or otherwise supply the sound effects **as sound effects**」，
而本 repo 是公開的。授權允許散布**成品專案**，不允許散布素材本身。
（比照既有的 `MovementAnimsetPro` 處理方式，見 `.gitignore`。）

⇒ **素材本身不在 git 裡，但「怎麼得到它」在。** 這幾支腳本就是那個答案。
正本說明與選材理由：`docs/37-combat-audio.md` §6。

---

## 需求

- `node` ≥ 18（用內建的 `zlib`／`fetch` 以外的 API，實際只用 `zlib` 與 `child_process`）
- `ffmpeg` / `ffprobe`（本機用 winget 的 Gyan build）
- `curl`
- 一個連著的 Unity Editor（`unity status` 要 ready）

⚠️ **node 的內建 `fetch` 會被 Sonniss 的 CDN 擋成 403**，所有 HTTP 一律走 `curl`
（需要瀏覽器 UA ＋ `Referer: https://gdc.sonniss.com/`）。

---

## 步驟

```bash
# 0) 研究資料夾（Unity 專案外）
mkdir -p "D:/Unity Project/_AudioResearch/Combat"/{Sonniss,Selected,_meta}

# 1) 只用 HTTP Range 讀各分卷的 central directory，得到完整檔案清單（**不下載整包**）
#    ⚠️ probe.sh 內有兩道硬性驗證，別拿掉：抓不到 Content-Length 或 Range 未被遵守時
#       會中止。第一版沒有這兩道，結果 TAILSTART 變負數 ⇒ 本來只想讀 1 MB 卻開始下載 1.2 GB。
./probe.sh 1 ; ./probe.sh 2 ; ./probe.sh 3 ; ./probe.sh 4 ; ./probe.sh 5

# 2) 只抽出戰鬥相關的 library（清單寫在 extract.mjs 的 WANT）
#    實測：63 檔 / 256 MB，而不是整包 6.45 GB
node extract.mjs

# 3) 客觀量測（長度／取樣率／聲道／峰值／RMS／**前置靜音**）
node analyze.mjs

# 4) 數出每個母帶裡有幾個獨立 take
#    ⭐ 這一步推翻了「每個 library 只有 2–4 檔 ⇒ 湊不出池」的推論：
#      production library 的慣例是「一個檔放 20 個 take」。
#      SWSH_SWING IMPACTS 一個檔就有 21 個揮擊。**檔案數不是素材數。**
node segment.mjs

# 5) 切成 game-ready 一次性 clip
#    切段 → 砍前置靜音 → 48 kHz / mono / 16-bit → peak 正規化 -1 dBFS
#    ⛔ 不用 compressor／loudnorm：會壓掉 transient，而打擊感就是 transient
node cut.mjs

# 6) 挑進 Selected/（每個事件 ≤10）＋ 產生 SOURCES.md
node select.mjs
node sources.mjs

# 6.5) ⭐ **池內響度對齊**（2026-09-17 補，修上一版的錯）
#      cut.mjs 只做了 peak 正規化，而 peak ≠ 響度：
#      實測 Melee/Hit 池內 RMS 差 **15.0 dB**、Character/Hurt 差 8.6 dB
#      ⇒ shuffle bag 每抽一次音量就跳一次，「近戰有沒有重量」根本無法被公平試聽。
#      改用「最響的 300 ms 窗口 RMS」對齊到池中位數，峰值超過 -1 dBFS 就夾住。
#      ⚠️ 不要用 integrated LUFS 當指標：它的 gating block 是 400 ms，
#         對 <0.4s 的 clip 一律回 -70 ⇒ 量測代理答非所問（本輪差點據此誤報三顆「壞掉」）。
node loudnorm-pool.mjs

# 6.6) 驗證：量測實際採用的 clip（前置靜音／響度／離散度）＋ 預測遊戲內相對電平
node measure.mjs        # 前置靜音 ＋ EBUR128（含上述 <0.4s 的已知限制）
node loud2.mjs          # RMS ／ M-max（與長度無關，可跨 clip 比較）
node mix.mjs            # 池目標 × SO volume ⇒ 預測電平 ＋ 同時發生組合的遮蔽分析

# 7) 複製進 Unity
#    Assets/Audio/Combat/{Spells,Melee,Character,Movement}

# 8) 匯入設定 ＋ 接線（在連著的 Editor 上跑）
unity cmd eval_file --file Tools/audio-pipeline/ImportAudio.cs
unity cmd eval_file --file Tools/audio-pipeline/WireClips.cs

# 9) 產生情境試聽頁（Selected 內嵌為 data URI，可直接雙擊開啟）
node audition.mjs
#    ⚠️ 為什麼要內嵌：以 file:// 開啟時 `fetch()` 會被 CORS 擋掉，
#       而情境排程（本頁最重要的功能）靠 fetch + decodeAudioData ⇒ 會**靜默失敗**。
#       data: URI 不受此限。備援 clip 走 <audio src>，那條路不經過 fetch，所以維持檔案參照。
#    若情境播放仍無聲，改用本機伺服器：
node serve-audition.mjs   # → http://localhost:8731/
```

---

## ⚠️ 重跑之後一定要做的事

wav 的 `.meta` 也在 `.gitignore` 裡 ⇒ **重新匯入會得到新的 GUID**，
`Assets/ScriptableObjects/Audio/Combat/*.asset` 裡的 clip 參照會全部斷掉。
**這正是 step 8 的 `WireClips.cs` 存在的理由**——它以「資料夾 → 定義」重新接一次，
不依賴任何 GUID。跑完就恢復。

---

## ⛔ 這些腳本不是 production 程式

它們是**用完即丟的 spike**（CLAUDE.md「Spike / Probe Exception」）：
不被 runtime 引用、不進 `Project.Runtime`／`Project.Editor` 組件、沒有測試。
留在 repo 裡的理由只有一個：**素材不能進版控，所以重建程序必須進版控。**
