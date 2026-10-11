# 53 — Unity Editor 記憶體／日誌暴增調查（2026-10-10，診斷輪）

> **狀態：只診斷，⛔ 未修正。** 沒有改 Gameplay／Traversal／MotionDriver／任何 runtime 程式或資產。
> **2026-10-10／11 追加**：§13＝C2 A/B 實驗（Group A 8 次未重現；**Group B 因無法驗證音訊已停用而中止**）；§14＝18:40 Editor 崩潰（**獨立事件**，由本輪探針觸發）。
> **2026-10-11**：§13.5＝使用者裁決（C2 Inconclusive／Deferred）；§15＝自動化測試安全性（監控 fail-closed、自測 W1–W12／L1–L32、獨立審查）。
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

**目前 Editor（PID 68464，13:22 重開）基線**：private 4.32–4.35 GB、working set 0.42–1.10 GB、native allocated 1.18 GB、Mono used 0.94 GB／heap 1.33 GB，30 分鐘取樣中 log 只因 §8 探針成長，**洪水 0 行**（⚠️ 更正：此「0 行」的依據是對 `Editor.log` 直接 `grep -c`；當時的 `editor_watch.ps1` 有變數碰撞 bug、`marker_in_delta` 恆為 0，**不能**當證據——見 §11）。

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
| **C2** | **Unity 6000.5.1f1 原生音訊 critical section 斷言洪水**（23.6 GB 的來源） | `Unity.dll` `Modules/Audio/Public/Utilities/CriticalSection.h`（ScriptableProcessor／SampleProvider 區） | 在 PlayMode run 之後**非決定性**進入；同一套測試多次不觸發；Domain Reload 降為 ≈8 行/s 但不歸零，下一次 PlayMode 後回升 | **穩態 12–14k 行/s ≈ 0.6–0.7 MB/s ≈ 2.4 GB/h**；初期 1.5–1.8k 行/s | 透過 C1 | **Inconclusive／Deferred（2026-10-11 使用者裁決，§13.5）**：只有時間相關＋字串歸屬。觸發機制在 Unity 原生碼內，無符號無法再深入。**§13 A/B：音訊開啟 8 次未重現（≠ 已排除）；音訊停用組無法建立可驗證的條件而中止** | ①查 6000.5 後續 patch 的 release notes（audio／CriticalSection）並評估升級；②A/B 需要**可驗證的音訊停用手段**（`m_DisableAudio` 只作用於 standalone；`-disableaudio` 啟動參數**無法驗證**（≠ 無效；`isPlaying` 不足以證明 FMOD 狀態），§13.3） | 升級或新的停用手段確立後，各跑足夠次數（§13.4 的檢定力）並以 `editor_watch.ps1`（修正版）監看 |
| C3 | 專案測試的音訊使用情境放大觸發機率（PlayMode 場景沒有 AudioListener、`AudioSourcePool` 在 teardown 時仍在播） | `Presentation/Audio/AudioSourcePool`／`AudioController`；PlayMode 測試（`There are no audio listeners` 6,104 次） | 推測 | — | — | **純推測**，無直接證據；本專案只用一般 `AudioClip`＋`AudioSource`，不直接用 ScriptableGenerator API。§13 的 8 次 PlayMode（同樣有 `no audio listeners` 警告）皆未觸發 | 不先改 | 同 C2 |
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
3a. ⛔ **不要在執行中的 Editor 切換 Project Settings → Audio（`m_DisableAudio`），也不要呼叫 `AudioSettings.Reset`**——§14 的崩潰就是這條路徑。
4. 讀大 log 一律用 `log_census.py`（常數記憶體）→ 再對 `reduced.log` grep。

---

## 11. 診斷工具（`docs/artifacts/editor-memory/`）

| 檔案 | 作用 | 安全性 |
|---|---|---|
| `log_census.py` | 串流讀任意大小 log：連續相同行收成 RUN 標記（`reduced.log`），輸出前 40 名、首次 offset、每 1% 直方圖、所有時間戳（`census.json`）。23.6 GB 需 ≈4 分鐘、記憶體 ≈15 MB | 唯讀；只讀到啟動時的檔長（可用於正在寫的 log） |
| `log_timeline.py` | 從 `reduced.log` 還原每行的原始 byte offset，抽出 token／memory／tests／reload／refresh／pipeline／oom／crash 事件，用 token 時鐘插值估計 UTC | 唯讀 |
| `editor_watch.ps1` | 每 N 秒記錄每個 Unity.exe 的 private／working set／handles／threads／CPU、`Editor.log` 真實大小與新增段落中的洪水字串數、系統 commit；**超過時間／private／log 大小上限自動停止取樣**（只觀察，不動 Unity）；**每列有 `status`（`OK`／`ALERT:*`／`ERROR:*`），讀不到就報 ERROR，不寫假 0**（§15.1） | 唯讀 |
| `console_cost_probe.cs.txt` | `eval_file` 方法體：量 Console 每行成本與 Clear 的效果（§8） | **會寫 log、會清 Console**；一次 ≤5 萬行；Play Mode 中拒跑。Spike／Probe 例外：用完即棄、不進 production path |
| `audio_state_probe.cs.txt` | `eval_file`：在 Edit Mode 以隱藏的暫時 `AudioSource` 播一段生成的靜音 clip，回報 `isPlaying`、命令列是否含 `-disableaudio`、取樣率（§13.3） | 不改任何設定；建立後立即 `DestroyImmediate` |
| `audio_toggle_probe.cs.txt` | `eval_file`：讀／寫 `m_DisableAudio`（SerializedObject） | ⛔ **危險：在執行中的 Editor 切換這個值會走 `AudioManager::ShutdownReinitializeAndReload`，§14 的崩潰就是它觸發的。保留只為存證，不要再用** |
| `playmode_ab_loop.sh` | 串行跑 PlayMode 直到 N 次有效；**fail-closed**：每次前後確認 Editor ready＋主執行緒 ping＋洪水 0＋log／private 在門檻內＋取樣器 CSV 新鮮且 `OK`＋recompile idle，任何讀數取不到也算停止；`CHECK_ONLY=1` 只檢查不提交（§15） | 只排測試，不改任何檔案或設定 |
| `ping_probe.cs.txt` | 主執行緒存活探針（迴圈啟動時複製成暫存 `.cs`） | 唯讀 |
| `selftest_watch.ps1`／`selftest_loop.sh`／`selftest_fake_unity.sh` | 取樣器 W1–W12、迴圈 L1–L32 的自我測試；假 CLI＋合成檔案，**不碰 Unity**（§15.2） | 改工具後必跑 |
| `editor_quit_probe.cs.txt` | `eval_file`：有未存場景就拒絕，否則排程 `EditorApplication.Exit(0)` | ⚠️ 實測 `delayCall` 中的 `Exit` **沒有執行**；實際關閉改用 `Process.CloseMainWindow()`（等同按視窗關閉鈕，正常關閉流程） |

> 🐞 **`editor_watch.ps1` 修正紀錄（2026-10-10，smoke test 抓到）**：①PowerShell 變數不分大小寫，`$marker = Count-MarkerSince … $Marker` 在第一次取樣後把搜尋字串覆寫成 `0` ⇒ **修正前 `marker_in_delta` 恆為 0、偵測失效**（§6 的 30 分鐘取樣、§13 前 3 次 run 期間的 v1 取樣都受影響）；改名 `$markerHits`。②`unity` CLI（`…\Unity\bin\unity.exe`）也叫 `Unity.exe`，原本被當成 `editor`；改標 `cli`。修正後以合成 log 注入 100／400 行驗證：計數精確、log 大小上限會停。

---

## 12. 不知道的事（Unknown）與限制

- 原生洪水的**觸發機制**（哪個物件的 critical section、為何版本號是偶數）——需 Unity 符號或 Unity 回報；`crash.dmp` 未分析。
- 崩潰當下的 Working Set、managed heap、GPU 顯存、§2 的 28.6 GB 差額。
- `Memory consumption went from A to B` 的精確定義未查證（與 native used 同量級、斜率與探針一致）。
- token 時鐘誤差約 ±30 分；休眠期間無時鐘。
- C2 的 A/B 只完成音訊開啟組（§13）；音訊停用組沒有可驗證的手段。
- `-disableaudio` 到底有沒有作用、`isPlaying` 是否足以反映 FMOD 輸出狀態——兩者無法區分（§13.3）。
- §14 崩潰的根因（為何 `Reset` 失敗後重新啟用會在 `releaseOutput` 存取違規）在 Unity 原生碼內。

---

## 13. C2 A/B 實驗：音訊停用 vs 正常（2026-10-10 10:48Z → 10-11 01:55Z）

### 13.1 設計與使用者追加條件

使用者裁決：Restart-based、每組 K＝8；⛔ 不得在執行中的 Editor 切換音訊；B 組須完整退出後以停用狀態重啟、結束後恢復並重啟；每次重啟後**以行為驗證實際音訊狀態**；兩組測試內容、操作條件、停止門檻相同；先做 smoke test；出現原生崩潰／洪水／資源異常即停、不自動重試；無法可靠驗證音訊已停用就**中止 B 組並回報**。

| 項目 | 內容 |
|---|---|
| 測試內容 | 整套 PlayMode（107 項），`unity cmd run_tests --mode PlayMode --async_tests true`，逐次串行 |
| 停止門檻（每次 run 前後都檢查） | Editor 非 `ready`、`Editor.log` 出現洪水字串（直接 `grep -c`）、log > 500 MB、Editor private bytes > 12 GB、recompile 非 idle；任一成立即停，不重試、不重啟 |
| 背景取樣 | `editor_watch.ps1`（修正版 v2），15 s，private > 12 GB／log > 1 GB／240 分鐘自動停 |
| 無效 run | 60 s 內沒有開始或沒有 summary ⇒ 記 INVALID、不計入 K |

### 13.2 Smoke test（低風險、不碰 Unity）

| 對象 | 方法 | 結果 |
|---|---|---|
| `editor_watch.ps1` | 指向合成 log，執行中注入 100、400 行洪水字串，log 上限設 20 KB | **第一次抓到 bug**：`marker_in_delta` 恆 0（變數碰撞，§11）。修正後計數 100／400 精確、`# stopped: log>…` 正確觸發 |
| 停止迴圈 | `DRYRUN=1`，對含洪水的合成 log 與乾淨 log 各跑（⚠️ `DRYRUN` 已在 §15 移除並改為拒絕執行；現行請用 `CHECK_ONLY=1`） | 洪水 ⇒ `STOP flood-onset`（exit 11）；乾淨 ⇒ 全部通過 |
| 未驗證的停止路徑 | 「Editor 非 ready」 | 無法在不殺 Editor 的前提下演練，**標為未驗證** |

> ⚠️ 程序偏離（如實記錄）：使用者的 smoke-test 要求送達時 Group A 已在跑；A2、A3 在 smoke test 完成前執行。A1–A3 期間的背景取樣是有 bug 的 v1，**洪水判定只依迴圈對 `Editor.log` 的直接 grep**（不受該 bug 影響）。

### 13.3 結果

**Group A（音訊開啟；PID 100044，18:40 崩潰後全新啟動、場景 Untitled）**

| run | 開始（UTC） | 時長 | 結果 | 洪水 |
|---|---|---|---|---|
| A1 | 10:48:04 | 136 s | 102 pass／5 fail | 0 |
| A2 | 10:50:56 | 163 s | 101／6 | 0 |
| A3 | 10:53:51 | 159 s | 102／5 | 0 |
| A4 | 10:56:53 | — | **INVALID**：請求在 A3 結束後的 domain reload 期間送出而被丟棄（手動交接時跳過了冷卻） | — |
| A5 | 11:13:17 | 144 s | 101／6 | 0 |
| A6 | 11:15:58 | 146 s | 101／6 | 0 |
| A7 | 11:18:41 | 150 s | 102／5 | 0 |
| A8 | 11:21:28 | 147 s | 102／5 | 0 |
| A9 | 11:24:44 | 144 s | 101／6 | 0 |

- **有效 8 次、洪水 0 次。** `Editor.log` 每次約 +1.42 MB（正常測試輸出）；Editor private 3.72 → 3.98 GB（A4–A9，約 +40 MB／run，無洪水成分）；v2 取樣（A5–A9）`marker_in_delta` 合計 0。
- 5–6 個 PlayMode 失敗是既有測試狀態（Untitled 場景、GroundLayers／Stair 骨盆類），與本實驗無關，未處理。

**Group B（音訊停用）——中止**

| 步驟 | 結果 |
|---|---|
| 原核准手段 `m_DisableAudio=1` ＋ 重啟 | `Unity.dll` 內該欄位的說明字串為 **「Completely disable audio (in standalone builds only)」** ⇒ 在 Editor 內不保證生效；改用前已回報並取得同意 |
| 替代手段 `-disableaudio` 啟動參數 | `Unity.dll` 在 `AudioManager` FMOD 初始化區有 `disableaudio` 字串（與 `nographics`／`batchmode` 同型），經使用者同意後採用；Editor 以 `Unity.exe -projectpath … -disableaudio` 啟動（PID 10964） |
| 行為驗證（`audio_state_probe`） | 正常 Editor：`isPlaying=True cmdline_disableaudio=False`；**`-disableaudio` Editor：`isPlaying=True cmdline_disableaudio=True`** ⇒ 這個檢查**無法證實音訊已停用**。⚠️ **不能據此宣稱 `-disableaudio` 無效**——`AudioSource.isPlaying` 只反映來源的播放狀態，不足以直接證明底層 FMOD 輸出是否啟用 |
| 判定 | **無法可靠驗證音訊已停用 ⇒ 依使用者條件中止 Group B，0 次 run** |
| 恢復 | 確認場景無未存變更 → `CloseMainWindow()` 正常關閉 → `unity open` 一般啟動（PID 99620，無旗標）→ 驗證 `isPlaying=True cmdline_disableaudio=False`；`git status` 與 B 組前快照**逐行相同**；`AudioManager.asset` 無 diff（`m_DisableAudio: 0`，從未寫入） |

附帶觀察：`-disableaudio` Editor 閒置約 3.4 h（取樣涵蓋）＋其後約 11 h，洪水 0、private 持平 3.20–3.23 GB。閒置時本來就不預期觸發，**不構成證據**。

### 13.4 解讀：未重現 ≠ 已排除

| 結論 | 強度 |
|---|---|
| 音訊開啟、全新 Editor、8 次 PlayMode：**未重現** | 事實 |
| 0／8 的 95% 上界：每次 run 觸發機率 ≤ 31% | 統計 |
| 以崩潰 session 的歷史（約 20 次 PlayMode 才首次觸發，p≈5%），8 次全不觸發的機率 ≈ 66% | ⇒ **本結果與「C2 成立」完全相容，也與「C2 不成立」相容**；C2 **既未支持也未排除** |
| C2（音訊為觸發條件） | **維持未證明**；本輪沒有建立可驗證的音訊停用條件 |
| 若要有統計意義 | 在 p≈5% 下，要讓「正常組至少觸發一次」的機率達 80% 需約 32 次 run（約 80 分鐘）；而且仍需先找到**可驗證**的音訊停用手段 |

### 13.5 使用者裁決（2026-10-11，接受本輪結果與 commit `672b540`）

1. **C2 ＝ Inconclusive／Deferred**，不再增加 A/B 次數。
2. Group A 的 0／8 **只代表未重現**，不能視為排除音訊因素。
3. Group B 因無法證實音訊停用而中止，**不能宣稱 `-disableaudio` 無效**；`AudioSource.isPlaying` 不足以直接證明底層 FMOD 是否啟用。
4. 18:40 的 FMOD 崩潰保留為**獨立事件**（§14），不與日誌洪水合併認定根因。
5. 暫不修改 Gameplay Audio，也不因本輪結果升級 Unity。

後續只做「自動化測試安全性」小工作輪（§15）。

---

## 14. 獨立事件：18:40 Editor 崩潰（由本輪探針觸發，⛔ 不與洪水視為同根因）

| 項目 | 內容 |
|---|---|
| 時間 | 2026-10-10 18:40:11（UTC 10:40），PID 0x10B70 |
| 觸發序列（皆在 Edit Mode、經 `unity cmd eval_file`） | ① `m_DisableAudio=true`（`SerializedObject.ApplyModifiedPropertiesWithoutUndo`）——未崩潰，取樣率仍 48000 ② `AudioSettings.Reset(GetConfiguration())` 回傳 **False** ③ `m_DisableAudio=false`（同 API）⇒ **崩潰** |
| 堆疊 | `FMOD::System::releaseOutput` ← `AudioManager::CloseFMOD` ← `AudioManager::ShutdownReinitializeAndReload` ← `AudioManager::AwakeFromLoad` ← `AwakeFromLoadQueue::AwakeFromLoadAllQueues` ← `SerializedObject::ApplyModifiedPropertiesWithoutUndo` ← `com.unity.pipeline` eval |
| Windows 事件 | Application #1000，`0xc0000005`（存取違規）於 `Unity.dll+0x40607a0` |
| 與 12:50 崩潰的差異 | 12:50＝`0x80000003`（OOM 後主動中斷，§2）；18:40＝存取違規、記憶體正常（private ≈4.7 GB）⇒ **不同類型** |
| 與洪水的關係 | 同屬音訊模組，但**沒有任何證據顯示共用根因**；這是非常規操作序列（停用後 Reset 失敗、再重新初始化）誘發的 |
| 證據 | `Logs/evidence-ab/crash-1840/`（`crash.dmp` 3.2 MB、`Editor.log`）；硬連結 `Logs/evidence-ab/Editor-crash-1840.log` |
| 影響 | Editor 重開後為 Untitled 場景；專案檔案無變動（`AudioManager.asset` 未寫入、`Temp/__Backupscenes` 不存在 ⇒ 無場景復原資料可失）；Console 內容遺失（log 仍在） |
| 教訓 | ⛔ 不在執行中的 Editor 切換音訊設定或呼叫 `AudioSettings.Reset`（已寫入 §10、§11 與工具註解） |

---

## 15. 自動化測試安全性：監控 fail-closed 驗證（2026-10-11）

> 範圍（使用者裁決）：只處理自動化測試的安全防護。⛔ 不重啟 C2 A/B、不改 Traversal／MotionDriver／Gameplay Audio。
> 停止路徑以**模擬程序＋合成日誌／合成取樣 CSV**驗證，**沒有為了驗證防護而讓 Unity 崩潰**。哪些路徑有測試、哪些沒有，見 §15.2 與 §15.6——**不是全部**。

### 15.1 fail-open 缺口與修正

| # | 位置 | 修正前 | 修正後 |
|---|---|---|---|
| G1 | 迴圈：`grep -c`／`stat` 失敗 | 變數為空 ⇒ `[ "" -gt 0 ]` 判假 ⇒ **繼續提交** | 讀數取不到或不是整數 ⇒ `STOP monitor-error:*`／`log-unreadable` |
| G2 | 迴圈：private bytes 讀不到 | 空值比較為假 ⇒ 繼續 | 必須是數字，否則 `STOP monitor-error:private-read` |
| G3 | 迴圈：CLI 無逾時 | 卡住就永遠卡住、不回報 | 每次 CLI 呼叫 `timeout CLI_TIMEOUT` ⇒ `STOP cli-timeout:*` |
| G4 | 迴圈：只看 `unity status` 的 `ready` | HTTP 執行緒活著、主執行緒卡死仍算安全 | 主執行緒 ping（`ping_probe`）須回 `pong`（容許 1 次重試，§13 實測長閒置首次常逾時）⇒ 否則 `STOP editor-unresponsive` |
| G5 | 迴圈：run 超時 | 輪詢完照樣進下一輪 | `RUN_TIMEOUT` ⇒ `STOP run-timeout`；連續 2 次 INVALID ⇒ `STOP` |
| G6 | 迴圈：不看取樣器 | 取樣器死掉／停止／回報異常都不知道 | 必須有 `WATCH_CSV`；不存在／`# stopped`／最後一列格式錯／過期／狀態非 `OK` ⇒ `STOP` |
| G7 | 取樣器：洪水計數 | 變數碰撞 ⇒ 恆為 0（§11） | 已修；4 MB 分塊順向掃描、塊間重疊防跨界漏算 |
| G8 | 取樣器：新增量 > 64 MB | 只掃**最後** 64 MB ⇒ 前段**靜默漏掉** | 掃前段 `ScanCapBytes`，超過就 `ALERT:delta-exceeds-scan-cap` |
| G9 | 取樣器：Editor 重開換新 log | log 變短才算、`delta=0` 不掃；**新 log 比舊的大時完全看不出來**（審查 F3） | 以「大小變小 **或** 前 512 bytes 改變」判定（每個 session 的 log 開頭含唯一時間戳）⇒ `ALERT:log-rotated`，新 log 從 0 掃 |
| G10 | 取樣器：沒有 Editor 行程 | 一列都不寫 | 寫一列 `ERROR:no-editor-process` |
| G11 | 取樣器：讀取失敗 | 沒有狀態欄；**CIM 查詢錯誤在 PS 5.1 不進 catch，`OK` 照寫**（審查 F6） | 每列有 `status`；`$ErrorActionPreference='Stop'`＋`-ErrorAction Stop` ⇒ `ERROR:sample-exception:*` |
| G12 | 迴圈：ping 檔案格式 | 真實 CLI 只接受 `.cs`；repo 存 `.cs.txt`（live 驗證抓到） | 啟動時複製成暫存 `.cs`＋Windows 路徑；假 CLI 照真實契約拒收非 `.cs` |
| G13 | 迴圈：只看取樣器**最後一筆**（審查 F1） | run 期間出現又消失的 ERROR／ALERT 被忽略 | 第一次檢查看最後一筆；之後每次檢查要求**上次檢查以來的每一列**都是 `OK` |
| G14 | 迴圈：已移除的 `DRYRUN=1`（審查 F2） | 被靜默忽略 ⇒ **真的提交測試** | 設了就 exit 2、拒絕執行 |
| G15 | 迴圈：門檻設定非數字（審查 F4） | `LOG_LIMIT_BYTES=1kB` 讓比較出錯 ⇒ 門檻失效、照跑 | 啟動時驗證所有數值設定，錯就 exit 2 |
| G16 | 迴圈：盯錯 Editor／錯取樣器／兩個迴圈並行（審查 F5） | 不檢查 | `unity status` 須符合 `PROJECT_MATCH`；取樣器最後一筆須含同一 pid 的 `editor` 列；`mkdir` 鎖（另一個在跑就拒絕，且不刪別人的鎖） |
| G17 | 取樣器：啟動時就存在的洪水 | 第一筆只當基線，不掃 | 第一筆可讀的取樣掃整個現行 log；啟動後才出現的 log ⇒ `ALERT:log-reappeared` 並從 0 掃 |

**契約**：
- 取樣器的 `status` 只有剛好 `OK` 才算安全；消費端必須把 `ALERT:*`／`ERROR:*`、CSV 不存在、停止、過期、格式錯都當成不安全。
- **權威的洪水閘門是迴圈對整份 `Editor.log` 的 `grep -c`**；取樣器的計數是第二道、偏即時的訊號。⛔ 不要把迴圈的 grep 當成「重複」刪掉。
- 迴圈在任何停止條件成立時**不提交下一輪**；進行中的 run 不取消（避免在 Play Mode 中途打斷），只阻止後續提交。
- 洪水字串在兩處定義（取樣器 `-Marker` 預設值、迴圈 `MARK`），彼此註明須一致。

### 15.2 自動化驗證（不碰 Unity）

| 測試 | 內容 | 結果 |
|---|---|---|
| `selftest_watch.ps1`（W1–W12） | 合成 log＋替身行程（`PING.EXE` 扮 Editor）：精確計數 500、跨 4 MB 邊界只算一次、log 不可讀＝ERROR、超過掃描上限＝ALERT、變短輪替、**變大輪替（W10）**、**log 後來才出現（W9）**、**CIM 錯誤（W11）**、**啟動前已存在的洪水（W12）**、無 Editor 行程＝ERROR、log 門檻停止、健康＝`OK` | **12／12 PASS** |
| `selftest_loop.sh`（L1–L32） | 假 `unity` CLI（`selftest_fake_unity.sh`）＋合成 log＋合成取樣 CSV；每案斷言 exit code、STOP 原因、**提交次數**（L30／L32 另驗鎖） | **32／32 PASS** |

| 情境 | 期望 | 提交次數 |
|---|---|---|
| L1／L32 健康 | 跑完 2 次；鎖已釋放 | 2 |
| L2 第 1 次後出現洪水 | `flood-onset` | **1（不提交第 2 次）** |
| L3 log 超過門檻 | `log-limit` | 1 |
| L4 private 超過門檻 | `private-limit` | 0 |
| L5／L29 Editor 非 ready／屬於別的專案 | `editor-not-ready` | 0 |
| L6 主執行緒無回應 | `editor-unresponsive` | 0 |
| L7 CLI 卡住 | `cli-timeout:status` | 0 |
| L8 log 不可讀 | `log-unreadable` | 0 |
| L9 記憶體讀不到 | `monitor-error:private-read` | 0 |
| L10–L14 取樣器 CSV 不存在／過期／ERROR／已停止／部分掃描 | `watcher-*` | 0 |
| L15 Editor 在 run 中消失 | `editor-not-ready` | 1 |
| L16 run 一直沒開始 | `two-consecutive-invalid-runs` | 2 |
| L17 正在編譯 | `recompile-not-idle` | 0 |
| L18／L23 `test_status`／`run_tests` 卡住 | `cli-timeout:*` | 1 |
| L19 沒給 `WATCH_CSV` | `no-watcher` | 0 |
| L20／L21 `CHECK_ONLY` 健康／主執行緒無回應 | 0／`editor-unresponsive` | 0 |
| L22 run 永遠不結束 | `run-timeout` | 1 |
| L24 **run 期間取樣器短暫 ERROR、檢查時已恢復** | `watcher-status:ERROR` | **1** |
| L25–L27 `DRYRUN=1`／`LOG_LIMIT_BYTES=1kB`／`PRIVATE_LIMIT_GB=abc` | exit 2 拒絕 | 0 |
| L28 取樣器盯的是別的 pid | `watcher-pid-mismatch` | 0 |
| L30 另一個迴圈持有鎖 | `another-loop-running`，**對方的鎖保留** | 0 |
| L31 取樣器最後一列格式錯 | `watcher-bad-row` | 0 |

**變異測試（拿掉防護 ⇒ 對應測試變紅）**：取樣器 m1 還原變數碰撞 → W1 紅；m2 拿掉 scan-cap 警報 → W4 紅；m3 拿掉 no-editor 錯誤 → W6 紅。迴圈 n1 拿掉 log 可讀／數字檢查 → L8 紅（下一道 size 檢查仍擋住，縱深有效）；n2 拿掉過期檢查 → L11 紅且提交 2 次；n3 拿掉主執行緒 ping → L6 紅且提交 2 次；**n4 改回「只看最後一筆」 → L24 紅且提交 3 次**。審查者另做：overlap 設 0 → W2 紅；拿掉 `flood-onset` → L2 紅（提交 3 次）；拿掉 `watcher-status` → L12／L14 紅。

### 15.3 Live 整合檢查（只讀，不提交任何測試）

| 檢查 | 結果 |
|---|---|
| 取樣器（最終版）對真實 Editor（PID 99620） | 第一筆掃整個現行 log（34 KB、洪水 0）、角色正確、status `OK` |
| `CHECK_ONLY=1`＋新鮮取樣器（最終版） | **PASS**：專案符合、pid 與取樣器一致、ping、flood 0、private 3.58 GB、watcher age 5 s、recompile idle；鎖已釋放 |
| `CHECK_ONLY=1`＋已停止的取樣器 | **STOP `watcher-stopped`**（exit 17） |
| 第一次 live 檢查 | **STOP `editor-unresponsive`**——原因是 G12（`.cs` 契約），不是 Editor；fail-closed 正確拒絕放行 |

### 15.4 Editor 場景與專案基準

| 項目 | 狀態 |
|---|---|
| 崩潰重開後的場景 | Untitled（`Library/LastSceneManagerSetup.txt` 為空） |
| 崩潰前的工作場景 | `Assets/Scenes/SampleScene.unity`（13:22 session 載入 119 次；Build Settings 唯一場景） |
| 處置 | 確認 Untitled 無未存變更 → `unity cmd open_scene` 開回 SampleScene → `isDirty=False`（審查者獨立確認 `count=1 playing=False`）；未存檔、未改任何資產（`SampleScene.unity` 的工作樹修改是使用者原有的，檔案時間早於本輪） |
| Untitled 是否影響過驗收 | PlayMode 測試自行載入場景；§13 A9 的失敗清單＝`C0`＋5 個 Stair 骨盆測試，為既有基線（`C0`＋6 個 Stair）的子集 ⇒ 未見影響（此項審查者未獨立驗證） |

### 15.5 使用方式

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/artifacts/editor-memory/editor_watch.ps1 -Out Logs/watch.csv -IntervalSec 15 -MaxMinutes 240
WATCH_CSV=Logs/watch.csv CHECK_ONLY=1 bash docs/artifacts/editor-memory/playmode_ab_loop.sh A 1 1 Logs/runs.txt   # 先確認全部檢查通過
WATCH_CSV=Logs/watch.csv bash docs/artifacts/editor-memory/playmode_ab_loop.sh A 1 8 Logs/runs.txt
powershell -NoProfile -ExecutionPolicy Bypass -File docs/artifacts/editor-memory/selftest_watch.ps1   # 改工具後必跑
bash docs/artifacts/editor-memory/selftest_loop.sh                                                     # 改工具後必跑
```

### 15.6 已知限制與**未測試**的路徑

- **沒有測試案例的停止路徑**：迴圈 `monitor-error:flood-count`、`monitor-error:log-size`、`ping-file-unreadable`；取樣器 `ERROR:marker-scan-failed`（log 在取得長度與讀取之間消失的競態）。程式路徑存在，未以測試證明。
- 進行中的 PlayMode run 在停止條件成立時**不取消**（只阻止下一輪）；需要中止時人工 `unity cmd cancel_tests`。
- 主執行緒 ping 容許 1 次重試；兩次都失敗才停。
- 取樣器只能觀察；它本身的死亡由迴圈以「CSV 過期」偵測（`WATCH_MAX_AGE` 預設 60 s，需大於取樣間隔＋取樣耗時）。
- 迴圈讀到取樣器**正在寫入的半行**時會判為格式錯或非 `OK` ⇒ 停止（偏向誤停，不會誤放行）。
- 迴圈被強制終止時鎖目錄 `Logs/.playmode_ab_loop.lock` 會殘留，下一次會拒絕執行，需人工刪除（刻意：無法分辨「殘留」與「另一個迴圈正在跑」）。
- 「Editor 真的崩潰」只用假 CLI 模擬，未在真實 Unity 上演練（刻意不製造崩潰）。

### 15.7 獨立審查（未參與實作的子代理，依 `docs/16`）

審查者自行重跑兩套自測、對 live Editor 只做 `CHECK_ONLY` 與一次唯讀場景查詢、在暫存副本上做變異測試。

| 編號 | 嚴重度 | 內容 | 處置 |
|---|---|---|---|
| F1 | Medium | 迴圈只看取樣器最後一筆，run 期間的短暫 ERROR 會被忽略 | **已修**（G13）＋L24＋變異 n4 |
| F2 | Medium | 舊 `DRYRUN=1` 被靜默忽略 ⇒ 真的提交 | **已修**（G14）＋L25；§13.2 已加註 |
| F3 | Medium | 取樣器對「後來才可讀」與「變大的新 log」寫 `OK` 卻沒掃 | **已修**（G9、G17）＋W9／W10／W12 |
| F4 | Low | 非數字門檻設定讓門檻失效 | **已修**（G15）＋L26／L27；最後一列時間戳格式檢查＋L31 |
| F5 | Low | 不檢查專案／pid、無單例鎖、未檢查 `run_tests` 回應 | 專案、pid、鎖**已修**（G16）＋L28–L30；`run_tests` 回應內容仍未檢查（以「`START_GRACE` 內須看到 running」＋INVALID 計數兜底） |
| F6 | Low | CIM 錯誤不進 catch，寫出 `OK` | **已修**（G11）＋W11 |
| T1 | Low | 多個停止路徑沒有測試 | 補了 `run-timeout`、`cli-timeout:run_tests`、變大輪替；其餘列在 §15.6 |
| ④ | — | 洪水字串兩處定義；未說明誰是權威閘門 | 已在兩處加註、§15.1 契約寫明 |
| 文件 | — | §15 前言、G9、G11、W2 註解過度宣稱 | 已更正 |
