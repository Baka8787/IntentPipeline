# test-orchestrator — Phase 1 + Phase 2（影子期進行中，`docs/57` §14）

正本設計：`docs/57`（§12 裁決、§14 影子期）。**狀態：影子模式——選擇器只產生計畫，不決定少跑任何測試。Phase 3 未啟用，需另行裁決。**
所有 Unity 測試照舊經 `docs/artifacts/editor-memory/safe_unity_test.sh`（本資料夾沒有任何程式會呼叫 `run_tests`，也不改入口的任何防護）。

| 檔案 | 角色 |
|---|---|
| `classify_results.py` | **Phase 1**：讀 `Logs/safe-test/<record>/`，以已核准清單比對「完整測試名稱＋穩定症狀」→ VERIFIED／VERIFIED_WITH_KNOWN_FAILURES／FAILED／BLOCKED／INCOMPLETE |
| `known-failures.json` | 已核准的已知失敗（K-1、K-2，`docs/57` §14.1）。**新增、變更、移除都需使用者裁決（Q6）**；每筆必須有核准紀錄、追蹤來源、重新調查條件；加入前先 `--verify-entries` 對基準紀錄核對 |
| `plan_tests.py` | **Phase 2**：變更集（上次全量已驗證快照 → 現在）→ R0–R3 計畫（`plan.json`／`plan.md`） |
| `test-map.json` | 規則表：高風險、模組 → PlayMode 類別、Editor 程式分級、成本模型、樣本有效性的白名單／禁止清單／計畫輸入。改它＝R3，且讓快照失效 |
| `snapshot.py` | 快照定義與內容比對（`docs/57` §12.3） |
| `sample_validity.py` | **Shadow Sample Validity**（`docs/57` §14.9）：計畫到比對之間，HEAD 只能不變或以**純文件 fast-forward** 前進（白名單 `sampleValidity.docOnlyAllow`，禁止清單優先）、紀錄跑在那條鏈上、未提交狀態／計畫輸入雜湊／`test-map.json` 與計畫時相同 |
| `shadow_compare.py` | 影子比對：計畫會略過的測試，在真實全量裡有沒有新增失敗（ESCAPE）；每個樣本寫 `Logs/test-orchestrator/shadow/<id>/sample.json`＋`.md`，**樣本有效性與快照有效性分開記錄** |
| `shadow_report.py` | 影子期報告：Q5 進度（樣本有效 ∧ 計畫時快照可信）、樣本有效性／快照有效性兩節、層級分布與 R3 原因、預估／實測節省、未調查的 ESCAPE |
| `escape-investigations.json` | 每個 ESCAPE 的調查（`mapping-gap`／`rule-gap`／`flaky-test`＋證據＋處置）。⛔ 只為歸零而擴大規則不算調查 |
| `check_skill_mirror.py` | `.claude/skills` 與 `.agents/skills` 一致性（唯一允許的差異：`CLAUDE.md`↔`AGENTS.md`） |
| `selftest.py`／`selftest_mutations.py` | 自測（202 案）／變異測試（36 種退化必須全被抓到） |
| `fixtures/planner-cases.json` | 50 個合成變更集與預期決策 |

所有腳本以 `python -I` 執行、只用標準函式庫、不寫 `__pycache__`。唯一的寫入動作：`plan.json`／`report`／`shadow.tsv`／`last-verified.json` 進 `Logs/test-orchestrator/`（gitignored），以及寫快照時 `git hash-object -w`（只加 loose object，不動 index、ref、工作樹）。

## 用法

```bash
python -I docs/artifacts/test-orchestrator/classify_results.py Logs/safe-test/<edit-record> Logs/safe-test/<play-record>
python -I docs/artifacts/test-orchestrator/classify_results.py --verify-entries Logs/safe-test/<record> ...
python -I docs/artifacts/test-orchestrator/shadow_report.py
python -I docs/artifacts/test-orchestrator/plan_tests.py --label <name> [--context gate|merge|release|architecture] [--probe Class.Method]
python -I docs/artifacts/test-orchestrator/plan_tests.py --check-map
python -I docs/artifacts/test-orchestrator/check_skill_mirror.py
python -I docs/artifacts/test-orchestrator/selftest.py
python -I docs/artifacts/test-orchestrator/selftest_mutations.py
```

| 腳本 | exit |
|---|---|
| `classify_results.py` | 0 VERIFIED · 10 VERIFIED_WITH_KNOWN_FAILURES · 1 FAILED · 20 BLOCKED · 30 INCOMPLETE · 2 用法／清單無效 |
| `plan_tests.py` | 0 已產生計畫（層級看輸出）· 2 用法／計畫錯誤 · 3 `--check-map` 有問題 |
| `shadow_compare.py` | 0 有效樣本、無漏測 · 1 有 ESCAPE · 3 樣本無效（什麼都不下結論）· 2 用法 |
| `classify_results.py --verify-entries` | 0 每筆都 CONFIRMED · 1 有 SYMPTOM_MISMATCH／NOT_OBSERVED · 2 用法 |
| `shadow_report.py` | 0 已產生 · 1 調查檔格式錯誤 · 2 用法 |
| `check_skill_mirror.py` | 0 一致 · 1 漂移 · 2 用法 |

## 影子樣本（Q5：至少 10 次真實改動）

1. 改動告一段落、**準備跑全量時**：`plan_tests.py --label shadow-<n>`
2. 照舊跑全量（不要在第 1 步之後再改任何檔案——包括讓 Unity 產生新的 `.meta`）：
   `safe_unity_test.sh --mode EditMode --label shadow-<n>-edit`，再 `--mode PlayMode --label shadow-<n>-play`
3. `shadow_compare.py --plan Logs/test-orchestrator/<dir>/plan.json --edit-record Logs/safe-test/<edit> --play-record Logs/safe-test/<play>`

4. 有 ESCAPE ⇒ 調查後在 `escape-investigations.json` 加一筆（分類＋證據＋處置）；`shadow_report.py` 看整體。

第 1 步之後若有**平行工作只提交文件**（`docs/NN-*.md`、`docs/ADR/*.md`、changelog、`WORKLOG.md`），樣本仍有效；改到其他任何東西（含 `docs/artifacts/**`、`CLAUDE.md`、未提交的工作樹）⇒ 樣本無效（`docs/57` §14.9）。

符合 §14.7 的樣本會寫入新的 `last-verified.json`，記錄的是**計畫時的 HEAD＋工作樹**（被測的狀態）。`shadow.tsv` 的 `countsTowardQ5=yes` 只給「樣本有效且計畫時快照有效」的樣本——沒有快照時計畫依規則就是 R3，什麼都沒略過，不能證明選擇器。⇒ **第一個有效樣本只建立快照，從第二個開始才計數。**

入口回 BLOCKED（例如 dirty scene）＝這次沒有樣本；照入口的規則處理，⛔ 不存場景、不繞過。

## 改了這個資料夾之後

跑 `selftest.py` 與 `selftest_mutations.py`。改 `test-map.json` 另跑 `--check-map`。
