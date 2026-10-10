# 53 — Unity Editor 記憶體／日誌暴增調查（2026-10-10，診斷輪）

> **狀態：只診斷，⛔ 未修正。** 沒有改 Gameplay／Traversal／MotionDriver／任何 runtime 程式或資產。
> 對 live Editor 唯一的寫入是 §8 的 console 成本探針（寫 11 萬行 log ＋ 清一次 Console），已在 §8 記錄。
> 工具：`docs/artifacts/editor-memory/`（§11）。證據原檔在 `Logs/evidence-2026-10-10/`（gitignored，見 §1）。

---

## 0. 一段話結論

**日誌**：23.6 GB 的 `Editor.log` 有 **99.82%** 是同一行
`Access version should be odd when acquiring lock`（471,265,634 行）。這行字串**只存在於 `Unity.dll`**，
位於 **`Modules/Audio/Public/Utilities/CriticalSection.h`**（Unity 音訊模組 ScriptableProcessor／SampleProvider 一帶）——
**是 Unity 6000.5.1f1 原生音訊引擎的斷言**，不是本專案、不是 `com.unity.pipeline`、不是任何 C# 程式碼印的。
首次出現在 **2026-10-08 ≈05:06Z**，緊接一次 PlayMode 測試跑完之後；之後穩態約 **12–14k 行／秒（≈0.6–0.7 MB/s，≈2.4 GB/h）**。

**記憶體**：69.9 GB 是 Unity 原生配置器 **`ALLOC_DEFAULT` 的 used**（不是 Working Set、不是 managed heap）。
它與洪水行數**嚴格線性**：每一行洪水 ＝ **321–322 B** 原生記憶體（3 個成長週期、24 個取樣區間，誤差 <1%），
並且**正好在每次 Domain Reload 時歸零**（專案的 Console 旗標含 *Clear on Recompile*）。
受控探針證實機制：**Console 即使開著 Collapse，每一行 log 仍保留約 350 B 原生記憶體，直到 Console 被清空**。
崩潰時距最後一次 reload 累積約 2.17 億行 × 321 B ≈ 65–70 GB ⇒ 與 `ALLOC_DEFAULT` 69.9 GB 吻合。

**⇒ 兩件事是同一條因果鏈的上下游**：原生音訊斷言洗版（觸發條件未明）→ 每行留在 Console 的原生儲存 → 4.4 小時沒有 Domain Reload → OOM。

---

## 1. 證據保全（先做，因為 Unity 下次啟動就會覆蓋）

| 證據 | 位置 | 說明 |
|---|---|---|
| 23.6 GB 崩潰 log | `Logs/Editor-prev.log` ＋ **硬連結** `Logs/evidence-2026-10-10/Editor-prev-crash-1250.log` | Unity 每次啟動會把 `Editor.log` 改名覆蓋 `Editor-prev.log`。NTFS 硬連結＝同一份資料的第二個名字，**不佔額外空間、不改內容**，原名被覆蓋後資料仍在。要釋放 23.6 GB 時，兩個名字都刪掉才會真的釋放——**由使用者決定，本輪不刪** |
| 縮減版 log | `Logs/evidence-2026-10-10/census/reduced.log`（42 MB） | 每段 ≥50 行的相同連續行收成一行 `<<RUN xN bytes=a..b line=L>>`，其餘逐字保留 ⇒ 可直接 grep，且能換算回原檔 byte offset |
| 聚合結果 | `…/census/census.json`、`…/timeline.csv` | 前 40 名訊息、每 1% 位置直方圖、所有時間戳、912 個階段事件 |
| Windows 事件 | System log `Resource-Exhaustion-Detector` #2004（2026-10-10 12:49:58）；Application #1000（12:50:24） | 見 §2 |
| 崩潰 dump | `%LOCALAPPDATA%\Temp\Unity\Editor\Crashes\Crash_2026-10-10_045013678\crash.dmp`（3.2 MB） | 未分析（無 Unity 符號，§12） |
| live 取樣 | `Logs/evidence-2026-10-10/watch-live.csv` | 本輪 30 分鐘、目前 Editor（§6） |

**崩潰的那個 Editor session**：2026-10-06 09:28Z 啟動 → 2026-10-10 04:50Z（台北 12:50）崩潰，單一 session 連跑約 91 小時。

---

## 2. Q1：69.9 GB 到底是哪個指標

| 指標 | 值 | 來源 | 判讀 |
|---|---|---|---|
| **`ALLOC_DEFAULT` used** | **69,937,932,624 B（65.1 GiB／69.9 GB）** | log 末端 Unity 的 `Memory overview`（OOM 時印出） | **WORKLOG 的「69.9 GB」就是這個**。Unity 原生預設配置器實際配出去的量 |
| 其他 Unity 配置器 used | GFX 53.6 MB、CACHEOBJECTS 42.5 MB、TYPETREE 6.6 MB、MEMORYPROFILER 5.3 MB、PROFILER 2.1 MB、各 TEMP ≈0 | 同上 | 合計 <0.12 GB ⇒ **成長全在 `ALLOC_DEFAULT`** |
| `reserved: 884,525,688,160 B` | 每個配置器都印同一個值 | 同上 | **共用的虛擬位址保留量，不是記憶體**。⚠️ 不要拿來當「用了 884 GB」 |
| **行程 commit charge（≈Private Bytes）** | **98,636,414,976 B（91.9 GiB）** | Windows `Resource-Exhaustion-Detector` #2004，12:49:58，`Unity.exe (103548)` | OS 層的真實壓力來源；PID 103548 ＝ 0x1947C ＝ 12:50:24 崩潰的那個行程 |
| OOM 當下的請求 | 33,600 B，`MemoryLabel: Manager` | log | 小配置失敗 ⇒ 是整體耗盡，不是單次巨大配置 |
| 崩潰碼 | `0x80000003`（breakpoint）於 `Unity.dll+0x371ff1f` | Application #1000 | Unity 在無法配置時主動中斷 |
| Working Set | **Unknown** | 當時未取樣 | 系統只有 32 GB 實體記憶體，98.6 GB commit 大部分必然在 pagefile |
| Managed（Mono）heap | **Unknown** | OOM dump 不含 | — |
| 98.6 − 69.9 − 0.1 ≈ **28.6 GB 差額** | **Unknown（未拆）** | — | 候選：managed heap、配置器已 commit 但已釋放的空洞（前一週期在 00:24Z reload 前已到 44.5 GB，§8 證實 Clear 後 used 降但 private bytes 不降）、GPU 驅動、執行緒堆疊 |

> ⚠️ 目前機器的系統 commit 已 40.5／58.6 GB（Unity 只佔約 7.5 GB）——**其他行程平時就吃掉大半 commit 額度**，Editor 的實際餘裕比 32 GB RAM 看起來小。（pagefile 為系統管理、可成長，崩潰當時顯然已擴張到 >98 GB。）

---

## 3. Q2：`Editor.log` 為什麼是 23.6 GB

### 3.1 組成（`log_census.py`，串流、常數記憶體、248 秒讀完）

| 項目 | 值 |
|---|---|
| 總行數 | 471,568,821 |
| 洪水行 `Access version should be odd when acquiring lock` | **471,265,634 行，每行 50 B（CRLF），佔 99.82% bytes** |
| 其餘全部 | 303,187 行（≈42 MB，`reduced.log` 的內容） |
| 次多：TestRunner 堆疊、`Debug.Log` 堆疊框、`There are no audio listeners in the scene`（6,104） | 各 ≤0.02% |
| 洪水首次出現 | **byte 28,273,494**（第 213,906 行），在此之前**一行都沒有** |
| 是否出現在 AssetImportWorker log | **否**（HW0/1/2 及其 -prev 皆 0）⇒ 只有主 Editor |

**字串來源**：在 `Assets/`、`Packages/`、`Library/PackageCache/` 全部 C# 中 grep 為 0；在 `Unity.dll` 中命中一次，
緊鄰的字串是 `C:\build\output\unity\unity\Modules\Audio\Public\Utilities\CriticalSection.h`，
前後是 `MixScope must be ended with RealtimeEndMix…`、`DualThreadHandle.IsHandleValid()`、`ScriptableGeneratorFMODInstance::Read…`。
⇒ 音訊模組內「主執行緒 ↔ 音訊 realtime 執行緒」共用的 critical section（版本號為奇數＝已鎖）在**取得鎖時發現版本號是偶數**而斷言。

### 3.2 時間軸：用 Licensing token 當時鐘

log 本身沒有時間戳。可用的時鐘是 `[Licensing::Module] Successfully updated access token … (expires: T)`——
token 約每 2 小時換一次、有效期約 2 小時 ⇒ **換發時刻 ≈ T − 2h（誤差約 ±30 分）**。
`log_timeline.py` 用它對事件做分段線性插值。

| 區段（UTC） | log 成長 | 洪水速率 | `Memory consumption` 讀值 |
|---|---|---|---|
| 10/06 09:28 → 10/08 05:06（含 10/06 22:57 → 10/08 03:07 約 28 h 沒有任何 token ⇒ 機器休眠） | 28 MB | **0** | 0.55 → 1.39 GB（正常） |
| **10/08 ≈05:06** | — | **洪水開始**（緊接 PlayMode 96 項跑完、場景還原之後） | — |
| 10/08 05:06 → 07:05 | 0.66 GB／2 h（≈91 KB/s） | ≈1.8k 行/s | 1.39 → 5.24 GB |
| 10/08 07:05 Domain Reload | — | — | **5.24 → 1.39 GB** |
| 10/08 07:07 → 09:06 | 0.53 GB | ≈1.5k 行/s | → 4.32 GB |
| 10/08 09:06 → 11:05 | 4.27 GB（≈0.59 MB/s） | ≈12k 行/s | → 25.74 GB |
| 10/08 11:05 Domain Reload | — | — | **25.74 → 1.42 GB** |
| 10/08 11:05 → 10/09 20:15（33 h，期間**沒有 PlayMode run**） | ≈66 MB | **≈8 行/s（細流，未歸零）** | 1.4–1.6 GB |
| 10/09 20:15–20:16 連續測試（EditMode 870／883、PlayMode 96／97、9 次 2 項 PlayMode） | — | 洪水回升 | — |
| 10/09 20:16 → 22:15 | 1.97 GB | ≈5.6k 行/s | → 11.6 GB |
| 10/09 22:15 → 10/10 00:14 | 4.99 GB（≈0.69 MB/s） | ≈14k 行/s | → 35.4 GB（00:11Z） |
| 10/10 00:24 Domain Reload | — | — | **44.56 → 1.61 GB** |
| 10/10 00:24 → 02:13（之後又跑 EditMode 884、PlayMode 97） | 4.25 GB | ≈13k 行/s | → 23.2 GB |
| 10/10 02:13 → **04:50 OOM** | 6.4 GB（≈0.68 MB/s，2.4 GB/h） | **≈14k 行/s** | → 56.7 GB（最後取樣）→ `ALLOC_DEFAULT` 69.9 GB |

> ⚠️ 依 byte 位置分 1% 桶的直方圖（`census.json`）是**平的**（每桶 ≈4.72M 行），因為洪水佔 99.8%——**byte 位置 ≠ 時間**，速率只能從上表的 token 時鐘讀。

---

## 4. Q4：與各階段的關聯

| 階段 | 觀察 | 結論強度 |
|---|---|---|
| **PlayMode 測試結束** | 洪水首次出現、以及兩次「reload 後細流 → 全速」的回升，都緊接在 PlayMode run 之後（10/08 05:06、10/08 07:07、10/09 20:15–16、10/10 00:24） | 時間相關，強 |
| 但**不是每次 PlayMode 都觸發** | 10/06 與 10/08 早上同一套 96 項 PlayMode 跑了約 6 次、加上其他 PlayMode 子集，都**沒有**洪水；本日（10/10）重開後的 Editor 也已跑過多次 105 項 PlayMode，**0 行洪水** | ⇒ **非決定性**，像競態 |
| **Domain Reload** | 3 次大 reload 都讓記憶體回到 ≈1.4–1.6 GB；洪水降到細流（≈8 行/s）但**不歸零** | 強（記憶體）；中（洪水速率） |
| EditMode 測試 | 本身不觸發；在洪水期間照常跑 | — |
| AssetDatabase Refresh／匯入 | 每次 reload 前都是 `RefreshV2` → reload；匯入本身與洪水無關（import worker 無洪水） | 無關 |
| Editor Probe／`eval_file` | 無對應事件；`com.unity.pipeline` 的 `Failed to handle /api/exec request: Main thread operation timed out after 5000ms` 只出現 3 次（10/09 19:28Z ×2、10/10 00:11Z），**都在洪水開始 38 小時之後** ⇒ 是結果或巧合，不是原因 | 已排除為觸發者 |
| Editor 啟動 | 這個 session 從啟動到洪水前 47 小時都正常 | 無關 |

---

## 5. Q3：程式碼稽核（無限 Debug.Log／update callback／OnGUI／匯入循環／事件未解除）

| 檢查 | 結果 |
|---|---|
| `EditorApplication.update`／`delayCall` | `TraversalDebugWindow`（`OnEnable` +=／`OnDisable` -=，0.x 秒節流 Repaint）、`AnimancerFacade`（delayCall 成對 +=/-=）、第三方 `ToonShaderInstaller`／`ReadmeEditor`（一次性）——**都有解除，無每帧 log** |
| `[InitializeOnLoad]`、`AssetPostprocessor`、`ExecuteAlways`、`logMessageReceived` 於 `Assets/` | 無專案自寫的；沒有匯入循環跡象（refresh 次數 290，與 reload 一一對應） |
| 會保存 log 的訂閱者（PackageCache） | `com.unity.pipeline` `ConsoleLogBuffer`：**上限 2000 筆**；`com.kitwright.unity.mcp` `UnityLogsRepository`：**上限 200 筆**；test-framework `LogScope`：測試期間 ⇒ **都有界，不可能長到 GB 級** |
| 專案自己的 log 量 | 全部非洪水內容 ≈42 MB／4 天；`NullReferenceException` 1 次 |

⇒ **本專案程式碼與 23.6 GB／69.9 GB 都沒有因果關係**（洪水字串不在任何 C# 中）。

---

## 6. Q5：Managed／Native／GPU／OS 拆分

| 層 | 崩潰當時 | 依據 |
|---|---|---|
| **Native（Unity 配置器）** | **69.94 GB，幾乎全在 `ALLOC_DEFAULT`** | OOM dump |
| GPU（`ALLOC_GFX`，CPU 端的 GFX 帳） | 0.054 GB | OOM dump；GPU 顯存本身 **Unknown** |
| Managed（Mono heap） | **Unknown** | 未記錄；§8 探針顯示洪水型 log **不增加** managed（`monoUsed` 不變） |
| OS commit（行程） | 98.6 GB | Event #2004 |
| 未歸屬差額 | ≈28.6 GB **Unknown** | §2 |

**目前 Editor（PID 68464，13:22 重開）基線**：private 4.32–4.35 GB、working set 0.42–1.10 GB、native allocated 1.18 GB、Mono used 0.94 GB／heap 1.33 GB，30 分鐘取樣中 log 只因 §8 探針成長，**洪水 0 行**。

---

## 7. Q6：是否有其他 Claude session 同時操作 Editor

| 事實 | 來源 |
|---|---|
| 目前沒有任何 session 在跑（全部 `isRunning: false`） | `list_sessions` |
| 10/09–10/10 期間活躍：`IntentPipeline 概念碎裂審查`（母）＋三個子 session（Traversal／MotionDriver／Action-Combat 審查）、`Stop 吸附感診斷方案`（至 10/09 21:24Z） | 同上 |
| MotionDriver、Action/Combat 子 session 的 brief 明寫 **「不跑 `unity cmd run_tests`／`recompile`／`eval_file`：Editor 讓給 Traversal session」** | transcript 搜尋 |
| 會碰 Editor 的是 Traversal session，以及**可能**的 Stop session（10/09 20:15–16 的 `--filter` 2 項 PlayMode run 風格與其相符） | transcript 搜尋（未逐筆對時） |
| **洪水起點 10/08 05:06Z 早於上述多 session 並行期** | §3.2 |
| 兩個 session 都觀察到洪水並記錄「早於本輪」（MotionDriver 看到 5.5 GB、Traversal 撞到 OOM） | transcript |
| `Main thread operation timed out after 5000ms` 從 09/16 起在約 10 個 session 都出現過 | transcript ⇒ 是 `eval_file` 已知的 5 秒上限，不是本事件特有 |

⇒ **沒有證據顯示多 session 互相干擾造成洪水或記憶體成長**；並行期的兩個 Editor 使用者在時間上可能重疊（未逐筆證實），但觸發點更早。

---

## 8. 受控實驗：每行 log 的記憶體成本（因果機制驗證）

**目的**：區分「Console 保存每一行」與「其他東西每行配置」。
**探針**：`docs/artifacts/editor-memory/console_cost_probe.cs.txt`（`unity cmd eval_file`，一次 ≤5 萬行；Play Mode 中或 private >12 GB 拒跑）。
訊息形狀刻意與洪水一致：同一行字串、`LogOption.NoStacktrace`、約 48 字元。Console 旗標＝Collapse 開。

| 步驟 | native allocated（`Profiler.GetTotalAllocatedMemoryLong`） | Δ | 每行 |
|---|---|---|---|
| 起點（161 筆既有 entry） | 1,179,259,364 | — | — |
| 主執行緒 5 萬行 | 1,196,567,308 | +17.31 MB | **346 B** |
| 背景執行緒 5 萬行（模擬音訊執行緒來源） | 1,214,009,948 | +17.44 MB | **349 B** |
| `LogEntries.Clear()`（＝ Clear on Recompile 做的事） | 1,174,953,650 | **−39.06 MB** | 釋放 10.5 萬行＋原有 entry |
| Mono used | 934.2 → 934.4 MB | ≈0 | 不是 managed |
| Private bytes（OS） | 4.641 → 4.681 GB → Clear 後 4.681 GB | 不降 | 配置器保留已 commit 的頁 |

**結論**：
1. Console 即使開 Collapse，**每一行都保留 ≈350 B 原生記憶體**；只有清空 Console 才釋放 ⇒ 與崩潰 log 量到的 **321 B／行** 同量級（字串與欄位略異）。
2. 專案 Console 旗標 `kMonoEditorConsoleFlagsV2 = 8067` ＝ bit 0／1／7–12 ＝ **Collapse、Clear on Play、Log/Warn/Error、Timestamp、Clear on Build、Clear on Recompile**（**已在 live Editor 以反射讀 `UnityEditor.ConsoleWindow+ConsoleFlags` 列舉與 `LogEntries.consoleFlags` 核實**：`ClearOnRecompile=4096`，目前值 8067）⇒ **Domain Reload 會清 Console**，正好解釋三次歸零。
3. Clear 之後 used 降、OS commit 不降 ⇒ 解釋了為何 OS commit（98.6 GB）高於 used（69.9 GB）的一部分（§2 差額）。

**對 live Editor 的副作用（已發生、可接受）**：Console 被清空一次（內容仍在 `Editor.log`）；`Logs/Editor.log` 多了約 5.4 MB 的 `Diag53…` 行。

---

## 9. 根因候選（依證據強度排序）

| # | 候選 | 涉及 | 可重現條件 | 日誌速率 | 記憶體速率 | 因果 | 建議最小修正 | 修正後驗收 |
|---|---|---|---|---|---|---|---|---|
| **C1** | **Console 原生儲存無上限地保留洪水行** ⇒ 69.9 GB | Unity Editor Console（`LogEntries`），`ALLOC_DEFAULT` | 任何高頻 log ＋ 長時間沒有 Domain Reload／Clear | — | **321 B × 行速**；洪水 14k 行/s 時 ≈4.5 MB/s ≈**16 GB/h** | **機制已證實**（§8 探針：+346/349 B/行、Clear −39 MB）；**對崩潰的歸因為定量吻合**（24 區間斜率 320–322 B/行；三次 reload 歸零；2.17 億行 × 321 B ≈ 70 GB）。未直接重現原生洪水本身 | 不改 Unity：①**偵測到洪水就 Domain Reload／重開 Editor**（Clear Console 只能降 used，不降 commit）；②長時間自動化 session 定期重開 Editor | `editor_watch.ps1` 期間 `marker_in_delta` 一出現 > 0 即處置；處置後探針 `measure` 的 native allocated 回到 ≈1.2 GB 級 |
| **C2** | **Unity 6000.5.1f1 原生音訊 critical section 斷言洪水**（23.6 GB 的來源） | `Unity.dll` `Modules/Audio/Public/Utilities/CriticalSection.h`（ScriptableProcessor／SampleProvider 區） | 在 PlayMode run 之後**非決定性**進入；同一套測試多次不觸發；Domain Reload 降為 ≈8 行/s 但不歸零，下一次 PlayMode 後回升 | **穩態 12–14k 行/s ≈ 0.6–0.7 MB/s ≈ 2.4 GB/h**；初期 1.5–1.8k 行/s | 透過 C1 | **未證明**：只有時間相關＋字串歸屬。觸發機制在 Unity 原生碼內，無符號無法再深入 | ①查 6000.5 後續 patch 的 release notes（audio／CriticalSection）並評估升級；②**A/B 實驗**（尚未做）：同一套 PlayMode 在「Project Settings → Audio → Disable Unity Audio」vs 正常下各跑 K 次，`editor_watch.ps1` 監看 marker | A/B：停用音訊的 K 次 marker 皆 0、正常組出現 ≥1 次 ⇒ 支持 C2；升級後跑同樣 K 次 PlayMode，marker 皆 0 |
| C3 | 專案測試的音訊使用情境放大觸發機率（PlayMode 場景沒有 AudioListener、`AudioSourcePool` 在 teardown 時仍在播） | `Presentation/Audio/AudioSourcePool`／`AudioController`；PlayMode 測試（`There are no audio listeners` 6,104 次） | 推測 | — | — | **純推測**，無直接證據；本專案只用一般 `AudioClip`＋`AudioSource`，不直接用 ScriptableGenerator API | 不先改；若 C2 的 A/B 成立，再做「測試 fixture 停用音訊」的最小改動 | 同 C2 A/B |
| C4 | `com.unity.pipeline`／CLI（`unity cmd`）造成 | `BasePipelineServer.cs:2071` | — | 3 行 | — | **已排除為觸發者**：首次 timeout 在洪水開始 38 h 後；洪水字串不在其中 | — | — |
| C5 | 專案程式碼（無限 log、update callback、事件未解除、匯入循環） | `Assets/Scripts/**` | — | 非洪水全部 ≈42 MB／4 天 | — | **已排除**（§5） | — | — |
| C6 | 多 Claude session 互相干擾 | — | — | — | — | **無證據**；觸發早於並行期（§7） | 維持「同一時間只有一個 session 碰 Editor」的既有做法 | — |

---

## 10. 給後續的操作建議（不需改程式）

1. **做長時間自動化（大量 PlayMode／probe）時，背景開 `editor_watch.ps1`**：
   `powershell -NoProfile -ExecutionPolicy Bypass -File docs/artifacts/editor-memory/editor_watch.ps1 -Out Logs/watch.csv -IntervalSec 30 -MaxMinutes 120`
   `marker_in_delta > 0` ＝ 洪水已開始 ⇒ 結束當前工作、讓 Editor 做一次 Domain Reload 或重開。
2. **Clear Console 不等於回收 OS 記憶體**（§8）——洪水之後要的是重開 Editor。
3. **崩潰後先保全 `Logs/Editor-prev.log`**（硬連結或複製），再重開 Unity；否則第二次啟動就覆蓋。
4. 讀大 log 一律用 `log_census.py`（常數記憶體）→ 再對 `reduced.log` grep。

---

## 11. 診斷工具（`docs/artifacts/editor-memory/`）

| 檔案 | 作用 | 安全性 |
|---|---|---|
| `log_census.py` | 串流讀任意大小 log：連續相同行收成 RUN 標記（`reduced.log`），輸出前 40 名、首次 offset、每 1% 直方圖、所有時間戳（`census.json`）。23.6 GB 需 ≈4 分鐘、記憶體 ≈15 MB | 唯讀；只讀到啟動時的檔長（可用於正在寫的 log） |
| `log_timeline.py` | 從 `reduced.log` 還原每行的原始 byte offset，抽出 token／memory／tests／reload／refresh／pipeline／oom／crash 事件，用 token 時鐘插值估計 UTC | 唯讀 |
| `editor_watch.ps1` | 每 N 秒記錄每個 Unity.exe 的 private／working set／handles／threads／CPU、`Editor.log` 真實大小與新增段落中的洪水字串數、系統 commit；**超過時間／private／log 大小上限自動停止取樣**（只觀察，不動 Unity） | 唯讀 |
| `console_cost_probe.cs.txt` | `eval_file` 方法體：量 Console 每行成本與 Clear 的效果（§8） | **會寫 log、會清 Console**；一次 ≤5 萬行；Play Mode 中拒跑。Spike／Probe 例外：用完即棄、不進 production path |

---

## 12. 不知道的事（Unknown）與限制

- 原生洪水的**觸發機制**（哪個物件的 critical section、為何版本號是偶數）——需 Unity 符號或 Unity 回報；`crash.dmp` 未分析。
- 崩潰當下的 Working Set、managed heap、GPU 顯存、§2 的 28.6 GB 差額。
- `Memory consumption went from A to B` 的精確定義未查證（與 native used 同量級、斜率與探針一致）。
- token 時鐘誤差約 ±30 分；休眠期間無時鐘。
- 未做 C2 的 A/B 重現（需要跑多次 PlayMode，使用者明確要求本輪不要重跑測試重現峰值）。
