# editor-memory — Editor 記憶體／日誌診斷與「受保護的 Unity 測試入口」

背景與所有結論：`docs/53-editor-memory-log-investigation.md`（§15 fail-closed 契約、§17 安全入口）。

## 🔒 跑 Unity 測試：只用這一個命令

```bash
E=docs/artifacts/editor-memory/safe_unity_test.sh
bash $E --mode PlayMode --check-only                                  # 只做安全檢查，不提交測試
bash $E --mode EditMode                                               # EditMode 回歸
bash $E --mode PlayMode                                               # PlayMode 回歸
bash $E --mode PlayMode --filter CapsuleOffsetPlayModeTests           # 部分名稱（不分大小寫）
bash $E --mode PlayMode --filter <Probe 方法名> --include-explicit --label <名稱>   # [Explicit] Probe
bash $E --mode PlayMode --timeout 1800 ...                            # 預設逾時 EditMode 600 s／PlayMode 1200 s（10–7200）
```

| exit | verdict | 意義 |
|---|---|---|
| 0 | `PASS`／`CHECK_OK` | 全部通過／檢查通過（沒提交） |
| 1 | `TESTS_FAILED` | 跑完、有失敗；失敗名稱與訊息列在輸出與 `summary.txt` |
| 2 | `CONFIG_ERROR`／參數錯誤 | 什麼都沒跑 |
| 3 | `NO_TESTS_MATCHED` | filter 沒選到任何測試（`[Explicit]` 需 `--include-explicit`） |
| 5 | `NOTHING_EXECUTED` | 選到的測試全部 skipped／ignored——什麼都沒驗證到 |
| 4 | `RESULT_MISSING_OR_MALFORMED` | 跑完卻拿不到可信的結果 ⇒ 停 |
| 10–27 | `SAFETY_STOP` | 受保護迴圈或入口的停止原因（`reason:` 一行說明）：14 編譯未乾淨、15 主執行緒無回應／場景未存／Play Mode、18 run 逾時、25 上一輪仍在跑、26 recompile 逾時、27 迴圈鎖被占用或殘留（確認沒有迴圈在跑才手動刪 `Logs/.playmode_ab_loop.lock`）… |
| 130 | `ABORTED` | 入口被中斷：迴圈先被停掉、不再提交，取樣器已停 |
| 30 | `SAMPLER_UNHEALTHY` | 取樣器起不來，或第一筆就不健康（例如 log 已有洪水） |
| 31 | `SAMPLER_ALERT` | 執行中取樣器出現非 OK 列 |
| 32 | `PREFLIGHT_FAILED` | Editor 不是本專案、未 ready、或 CLI／python 無回應 |

紀錄：`Logs/safe-test/<UTC>-<label>.XXXX/`（`meta.txt`、`loop.txt`、`watch.csv`、`<label>1-run_tests.json`、`<label>1-test_status.json`、`summary.txt`）。

實跑（非 `--check-only`）前入口會先 `recompile`，確保測的是目前的原始碼。從 Bash 工具跑 PlayMode 請用 `run_in_background` 或 timeout ≥ 600000 ms。
`--filter` 是**完整測試名稱的子字串比對（不分大小寫）**：搭配 `--include-explicit` 時請給完整 `類別.方法`，避免一次選到多支 Probe。

⛔ 入口失敗時**不要**改用 `unity cmd run_tests` 直接跑——先診斷、回報，例外由使用者批准（`CLAUDE.md`）。

## 檔案

| 檔案 | 角色 |
|---|---|
| `safe_unity_test.sh` | **唯一入口**：參數驗證 → preflight → 啟動並確認取樣器 → 受保護迴圈跑 1 次 → 判讀 → 稽核取樣器 → 清理 |
| `playmode_ab_loop.sh` | 受保護迴圈（名稱是歷史遺留；EditMode／PlayMode 都跑）。只有需要連跑 N 次時才直接用 |
| `unity_test_json.py` | 所有 CLI `--json` 輸出的嚴格解析與驗證（Editor 身分、ping、編譯狀態、run 受理、test_status、報告、filter 驗證） |
| `ping_probe.cs.txt` | 主執行緒探針：isPlaying／isCompiling／compileFailed／isUpdating／sceneDirty |
| `editor_watch.ps1` | 取樣器（唯讀）：每列 `status`＝OK／ALERT／ERROR；`-PidFile` 供入口精準停止 |
| `selftest_parser.sh` | 解析器 × **真實 CLI 輸出**（`fixtures/real-*.json`） |
| `selftest_loop.sh` | 迴圈 × 假 CLI（停止路徑、編譯狀態、參數、逾時） |
| `selftest_safe_entry.sh` | 入口 × 假 CLI＋假取樣器（各 verdict、清理、參數錯誤） |
| `selftest_watch.ps1` | 取樣器 × 合成 log＋替身行程 |
| `selftest_fake_unity.sh`／`selftest_fake_sampler.sh` | 測試用替身（輸出形狀照真實 CLI） |
| `log_census.py`／`log_timeline.py` | 串流分析超大 `Editor.log`（§3–§4） |
| `console_cost_probe.cs.txt`、`audio_*_probe.cs.txt`、`editor_quit_probe.cs.txt` | 一次性研究探針（§8、§13；`audio_toggle` ⛔ 危險，勿用） |

**改任何一支工具後必跑**：

```bash
bash docs/artifacts/editor-memory/selftest_parser.sh
bash docs/artifacts/editor-memory/selftest_loop.sh          # 約 4 分鐘
bash docs/artifacts/editor-memory/selftest_safe_entry.sh    # 約 4 分鐘
powershell -NoProfile -ExecutionPolicy Bypass -File docs/artifacts/editor-memory/selftest_watch.ps1
```
