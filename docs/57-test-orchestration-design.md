# 57 — Unity 測試調度（`unity-test-orchestrator`）：現況盤點與設計（2026-10-13，Research + Design）

| | |
|---|---|
| **狀態** | 🟢 **已裁決（2026-10-13，§12）——方案 B、命名 R0–R3**；Phase 1＋2 已實作並驗收（§13），**影子期進行中**（§14，2026-10-14）；Phase 3 **未啟用、需另行裁決**；Phase 4 暫不做。§1–§11 保留為設計原文，與 §12 衝突處以 §12 為準 |
| **輸入** | 使用者 2026-10-13：每次小修改都跑完整 EditMode／PlayMode 回歸，成本越來越高；評估專案專用 `unity-test-orchestrator` Skill。**目標是讓測試時機與範圍合理，不是減少覆蓋** |
| **不可動搖的前提** | 所有 Unity 測試仍經 `safe_unity_test.sh`（`docs/53` §17）；dirty scene／Editor busy／recompile 異常 ⇒ fail-closed；⛔ 不自動存檔、捨棄場景變更或重啟 Editor。2026-10-13 P5-0 因 dirty scene 被 `SAFETY_STOP` 是**正確行為**，本設計不降低它 |
| **閱讀方式** | §1 現況（全部有量測或檔案證據）→ §2 安全入口的篩選能力 → §3 能否推導受影響測試 → §4 L0–L3 → §5 強制升級 → §6 結果分類與已知失敗 → §7 方案比較 → §8 推薦設計與檔案結構 → §9 驗收 → §10 導入計畫 → §11 待裁決 |

---

## 1. 現況盤點

### 1.1 真正的成本在哪裡（`Logs/safe-test/` 實際紀錄，2026-10-12～13）

| 執行 | 測試數 | Test Runner 計時 | 入口總耗時（wall） | 來源 |
|---|---:|---:|---:|---|
| EditMode 全量（含真的重新編譯） | 956 | **5.4 s** | **66–79 s** | `20261013T095531Z-p13-editmode-final`、`…094533Z…`、`…093105Z…` |
| PlayMode 全量（`up_to_date`） | 113–118 | 246 s（最新一次） | **3 min 48 s – 5 min 37 s** | `20261013T100953Z-p13-playmode-final3`（5:04）、`…095642Z…`（5:37）、`…093329Z…`（3:48） |
| PlayMode 單一類別（`EnemyChasePlayModeTests`，3.6 s） | 2 | 3.6 s | **59 s**（`up_to_date`）／**94 s**（重新編譯） | `20261013T092703Z…`、`…092156Z…` |

PlayMode 全量的 246 s 裡：

| 類別 | 耗時 | 佔比 |
|---|---:|---:|
| `StairTraversalPlayModeTests`（28 案） | 133.6 s | 54% |
| `EnemyNonCombatPlayModeTests`（5 案） | 78.7 s | 32% |
| 其餘 16 個類別合計 | ≈ 34 s | 14% |

**三個結論，決定了整個設計的形狀：**
1. **EditMode 選子集幾乎省不到時間**——956 案只跑 5.4 s，每次呼叫的 60–75 s 是**固定開銷**（取樣器暖機、recompile、domain reload、穩定等待）。⇒ EditMode **永遠全量**，不做選擇。這同時消除了 EditMode 漏測的風險。
2. **每次呼叫約 55–90 s 的固定開銷**讓「多次小範圍呼叫」很快就比一次全量還貴：4 個 PlayMode 類別分開跑 ≈ 4 × 60 s ≈ 一次全量。
3. **可以省的只有 PlayMode，而且集中在兩個類別**（86%）。⇒ 選擇問題的本質是「這次改動需不需要 Stair／EnemyNonCombat 這兩個慢類別」，加上「其餘 16 個快類別能不能一次跑完」。

### 1.2 測試分類、命名、模組歸屬

| | EditMode | PlayMode |
|---|---|---|
| 檔案 | 62 | 22（含 5 個 `[Category("Probe")]` 探針，其中 1 個是 P5-0 未 commit 的量測） |
| 測試數（最近一次全量） | 956 | 118 |
| 命名 | `<子系統>Tests`（`EnemyNavigatorTests`、`FootIKTests`…）；架構契約集中在 `ArchitectureRegressionTests`（A*）、接線在 `PrefabWiringTests`（W*） | `<子系統>PlayModeTests`；探針為 `*Probe`／`*Metrics` |
| `[Category]` | **0 個** | 只有 `Probe`（5 個檔） |
| 載入出貨場景 `SampleScene` | 2 個檔（⇒ **場景 WIP 會影響結果**） | 9 個檔（⇒ 跑完整遊戲迴圈，等於覆蓋整條管線） |
| 歸屬 | 只能從**類別名稱**推，沒有任何機器可讀的模組對照 | 同左 |

**程式組件（asmdef）**：runtime 只有**一個** `Project.Runtime`（`Assets/Scripts/`），Editor 一個 `Project.Editor`，兩個測試組件。`Project.Tests.EditMode` 參照 Runtime＋Editor；`Project.Tests.PlayMode` **只參照 Runtime**（不參照 `Project.Editor`）。

### 1.3 文件／Skill 的重複與衝突

| # | 發現 | 證據 | 影響 |
|---|---|---|---|
| **K1** | **撞名**：`docs/12` §6 的 Verification Ladder 已經用 **L0–L6** 表示「驗證**種類**」（L0 架構掃描、L1 接線、…、L4 PlayMode、L6 人工）；本次要的 L0–L3 是「執行**範圍**」 | `docs/12-workflow.md:155–167` | 兩套 L1 意思完全不同，會在討論與紀錄裡混淆 ⇒ 建議正式命名改為 **R0–R3**（§11 Q1）；本文件暫依要求寫 L0–L3 |
| K2 | `CLAUDE.md`、`AGENTS.md`、`.claude/skills/unity-live-control`、`.agents/skills/unity-live-control`、`docs/53` §17、`docs/artifacts/editor-memory/README.md`、auto-memory 七處都描述「怎麼跑測試」；**沒有任何一處**描述「該跑哪些、何時跑全量」 | grep | 新規則若再寫七份會漂移 ⇒ 正本只放一處（Skill），其餘一行指標 |
| K3 | `.claude/skills` 與 `.agents/skills` 兩份 Skill **刻意鏡像**，唯一差異是 `CLAUDE.md`↔`AGENTS.md` 的稱呼 | `diff -rq`（兩檔各一行） | 新 Skill 也要照這個慣例雙份、並加入鏡像一致性檢查 |
| K4 | **已知失敗只存在於散文**：WORKLOG／`docs/55` 等 18 處以「唯一失敗＝既有場景 mask」「`C0`＋6 個 Stair 骨盆」描述；最近一次全量其實只剩 `C0`——六個 Stair 失敗已不再出現，但沒有任何紀錄更新 | `grep -r "唯一失敗\|已知失敗\|基線就失敗"`；`20261013T100953Z` 結果 | 人工比對＝靠記憶；清單已經過期一次 |
| K5 | 最近 10 次全量（EditMode 與 PlayMode）**全部** `TESTS_FAILED`——永遠有已知失敗 ⇒ exit code 本身不再能區分「沒退步」與「退步了」 | `Logs/safe-test/*/summary.txt` | 已知失敗比對必須機器化 |
| K6 | 工作樹長期混有使用者 WIP（目前：`SampleScene.unity` +5.9k 行、`FootstepAudioBank.asset`、`ProjectSettings/*`）；其中場景 WIP 直接造成 EditMode 已知失敗（`GroundLayers=503`） | `git status`；`StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` 訊息 | 單純用 `git diff` 推影響範圍會**永遠**被 WIP 拉到全量（§3.2 的 baseline snapshot 解決） |

### 1.4 `safe_unity_test.sh` 的安全檢查、參數、已知限制

| 類別 | 內容 | 來源 |
|---|---|---|
| 安全檢查 | 參數白名單（2）→ 唯一一個本專案 ready Editor（32）→ 取樣器健康（30）→ **recompile 並等嚴格乾淨＋主執行緒回應**（14／26）→ 提交前：不在 Play Mode、**場景不 dirty**、無在途 run（15／25）→ 單次受保護 run → 判讀 → 取樣器稽核（31）→ 清理（中斷也先停迴圈） | `safe_unity_test.sh:1–24`；`docs/53` §17.4 |
| 結果分類 | PASS 0、TESTS_FAILED 1（列名稱＋訊息）、CONFIG 2、NO_TESTS_MATCHED 3、MALFORMED 4、NOTHING_EXECUTED 5、SAFETY_STOP 10–27、取樣器 30／31、preflight 32、ABORTED 130 | `:192–215` |
| 參數 | `--mode EditMode\|PlayMode`、`--filter`、`--include-explicit`（必須搭 `--filter`）、`--timeout 10..7200`、`--label`、`--check-only` | `:48–66` |
| 紀錄 | `Logs/safe-test/<UTC>-<label>.XXXX/`：`meta.txt`（含 `git_head`、`recompile_trigger`）、`summary.txt`、**完整 test_status JSON（含每案 `FullName`／`Status`／`Duration`／`Message`）** | `:22`、`:69–72` |
| 已知限制（`docs/53` §17.8） | 入口是預設不是強制（P2 hook 未做）；每次只跑 1 次；部分異常路徑只有假 CLI 驗證 | — |

---

## 2. 安全入口的篩選能力夠不夠？——**不夠做調度，但足夠做安全**

對照 CLI 原始碼（`Library/PackageCache/com.unity.pipeline@ab05e9cd7f74/Editor/Testing/PipelineTestRunner.cs:556–640`）：

| # | 限制 | 證據 | 對調度的影響 |
|---|---|---|---|
| F1 | `--filter` 是**單一**、不分大小寫的 `FullName` **子字串**；沒有 OR／AND／NOT | `ShouldIncludeTest` `testname` 分支：`IndexOf(filter, OrdinalIgnoreCase)` | 一次呼叫只能選「名稱含某片段」的一群；選不了「A 類別＋B 類別」 |
| F2 | 驗證器允許 `\|`、`*`、`?`，但 CLI **逐字比對**——`"A\|B"` 不是「A 或 B」 | `safe_unity_test.sh:66` 的白名單；同上 `IndexOf` | 會被誤用；目前結果是 `NO_TESTS_MATCHED`（exit 3，fail-closed，**安全但浪費一次呼叫**） |
| F3 | CLI **支援** `filter_type=category\|assembly`（category 為完整名稱比對），但入口與迴圈**沒有傳遞**它 | `TestCommands.cs:25–26`；`playmode_ab_loop.sh` 無 `filter_type` | 「依模組標籤一次選一群」在 CLI 層可行，入口層不可行 |
| F4 | 一次呼叫只有一個 mode；EditMode＋PlayMode＝兩次固定開銷 | `:60` | — |
| F5 | **子字串的過度命中**（安全方向）：`--filter Enemy` 也選到 `StairTraversalPlayModeTests.Enemy_TraversesGeneratedStair` | 測試名稱 | 多跑，不漏 |
| F6 | **子字串的命中不足**（危險方向）：敵人行為的契約散在 `AIInputSourceTests`、`CombatContextTests`、`PrefabWiringTests.W10/W15/W27/W28`、`ArchitectureRegressionTests.A16/A54`——名字都不含 `Enemy` | 測試名稱 | **這是「只靠 Claude 挑 filter」最主要的漏測路徑** |
| F7 | 入口不知道「哪些測試**沒有**被選到」——它只回報選到的 | 結果 JSON 只有被執行的案例 | 「未執行清單＋原因」必須由上層產生；**測試全集**可取自最近一次全量的 `results[]`＋新增測試檔 |

**結論**：入口的**安全**部分不需要改；調度需要的「一次選多群」能力，在現有入口下只能用「多次呼叫」或「全量」達成。若要單次呼叫跑「快速 PlayMode 群」，需要 F3（`--filter-type category`）＋測試加 `[Category]`——那會動到安全腳本與既有測試，⛔ 本輪不做，列為 §10 Phase 4（需另外核准）。

---

## 3. 能否從 diff／asmdef／契約／模組歸屬推導受影響測試？

| 來源 | 能推出什麼 | 不能推出什麼 |
|---|---|---|
| **asmdef** | 只有一條有用的規則：`Assets/Scripts/Editor/**` 的程式**不可能**影響 PlayMode（PlayMode 組件不參照 `Project.Editor`） | runtime 只有一個組件 ⇒ 任何 runtime 改動在 asmdef 層面都「影響全部」——**沒有粒度** |
| **Git diff（路徑）** | 改了哪些檔、新增／刪除／改名 | 單純的 `git diff` 被長期 WIP 汙染（K6）；資產改動的影響要另外對照 |
| **Git diff（內容）** | 確定性的升級訊號：新的 `using Project.<他層>`、黑板 struct 的欄位增刪、`ArchitectureRegressionTests` 的 `WriterRules`／`LayerRules` 變動、新的核心介面實作者 | 語意影響（例如改了一個常數讓下游行為變了） |
| **架構契約（Test-as-Spec）** | **權威檔案清單可以直接從 `WriterRules.AllowedFiles` 解析**（`ArchitectureRegressionTests.cs:507–`）——`MotionDriver.cs`、`LocomotionModel.cs`、`AIMovementSource.cs`、`ArbiterPipeline.cs`、`CharacterPipelineRunner.cs`… ⇒ 高風險清單與架構測試**同源、不會漂移** | — |
| **模組歸屬** | 需要一份新的對照表（目前不存在）：模組 → 原始碼路徑 → 負責的 PlayMode 類別 | 對照表本身會漂移 ⇒ 必須有完整性自測（§9 AC2） |

### 3.1 為什麼「不能只靠 AI 判斷」在這裡是可執行的要求

決定「範圍」的每一步都是表格查詢或字串比對（路徑 glob、內容 regex、解析 `WriterRules`），結果寫成 `plan.json`，**同樣輸入必定得到同樣輸出**。Claude 只做兩件事：選擇呼叫哪個入口（Skill 規則），以及在計畫說「需要人」時停下來。

### 3.2 WIP 汙染：以「上一次已驗證狀態」為基準，而不是以 HEAD 為基準

每次 L3 完成時，在 `Logs/test-orchestrator/last-verified.json` 記下：HEAD、每個 dirty／untracked 檔的 blob hash（`git hash-object`，⛔ 不用 `stash`）。下次的影響集合＝
`(last-verified HEAD .. 目前 HEAD 的檔案) ∪ (目前 hash ≠ 快照 hash 的工作樹檔) ∪ (新增的 dirty 檔)`。

- 使用者早就在的場景 WIP，若在上次 L3 時就已存在且沒再變 ⇒ 不再觸發。
- 場景**又被改**了 ⇒ hash 不同 ⇒ 觸發（而且一定升級：場景是高風險）。
- 沒有快照、快照解析失敗 ⇒ **L3**。

---

## 4. L0／L1／L2／L3 分層策略

> ⚠️ 命名與 `docs/12` §6 撞名（K1），建議正式名稱 R0–R3（§11 Q1）。

| 層 | 內容 | 何時足夠 | 預估耗時 |
|---|---|---|---|
| **L0 靜態** | 不碰 Unity：`.cs` 變更 ⇒ `dotnet build Project.Runtime.csproj`（`compile-without-unity` 的既有方法）；測試基礎設施腳本變更 ⇒ 該腳本的 selftest；純文件 ⇒ 只做計畫並記錄「未執行 Unity 測試：純文件」 | 變更集**只有**純文件路徑（`docs/**/*.md`、`WORKLOG.md`、`LearningNotes/**`；⚠️ `docs/artifacts/**` 的腳本**不算**純文件） | 秒～1 min |
| **L1 EditMode 全量** | L0 ＋ EditMode **全部** 956 案（永不選子集，§1.1） | 只改 `Assets/Scripts/Editor/**`（asmdef 規則）、EditMode 測試檔本身、或只影響 EditMode 驗證的資產（例如純 Editor 工具資產） | ≈ 1.2 min |
| **L2 EditMode 全量＋PlayMode 受影響集合** | L1 ＋ 對照表選出的 PlayMode 類別。**Phase 1（入口不改）**：若「各自呼叫的估計成本」＜「全量估計成本」就逐類呼叫，否則直接跑 PlayMode 全量；估計值取自最近紀錄的 `Duration`＋固定開銷 60 s。**Phase 4 之後**：快速群（`[Category]`）一次跑完＋被選中的慢類別 | 變更全部落在已對照的模組內，且沒有任何 §5 升級訊號 | 2.5–4 min |
| **L3 完整回歸** | EditMode 全量＋PlayMode 全量＋已知失敗比對＋更新 `last-verified.json` | 見 §5（強制）；以及**每個 Feature Slice 的 Integration Gate／收尾 commit 之前一律 L3** | 5–7 min |
| （正交）**Probe** | `[Explicit]` 探針只在任務需要量測時、以**完整類別.方法**名稱＋`--include-explicit` 單獨呼叫；**永遠不被分層自動選入**、結果不計入回歸 | — | 依探針 |

**L2 為什麼可以接受**：EditMode 全量（含 A*／W* 架構與接線契約）**每一層都跑**；L2 只決定「哪些 PlayMode 類別可以留到 Gate 的 L3 再跑」。最壞情況是 PlayMode 退步晚幾輪才被抓到，**不會**被略過——Gate 一定 L3。

---

## 5. 強制升級規則（確定性，全部可測）

| # | 訊號 | 判定 | 升級到 |
|---|---|---|---|
| E1 | **路徑不在對照表內**（新資料夾、新檔案類型） | glob 全部未命中 | **L3** ＋ 計畫列出「未對照：<路徑>」 |
| E2 | **權威檔案**：`WriterRules.AllowedFiles` 解析出的每個檔（`MotionDriver.cs`、`LocomotionModel.cs`、`AIMovementSource.cs`、`ArbiterPipeline.cs`、`CharacterPipelineRunner.cs`、`*CombatContextSource.cs`、`PlayerLocomotionPolicy.cs`…） | 從測試原始碼解析；解析失敗 ⇒ L3 | **L3** |
| E3 | 黑板與核心介面：`Core/Blackboard/**`、`IMovementIntentSource`／`IMovementModel`／`AnimationFacadeBase`／`IPresentationController`／`IArbiterSource`／`IInputSource` 所在檔、`CharacterFacingSource.cs`、`StateMachine*.cs`、`StateType.cs` | 路徑 | **L3** |
| E4 | **新增跨層契約**：diff 新增行出現 `using Project.<X>` 且 `<X>` 不是該檔所在層；或新增 `: <核心介面>` 實作者；或黑板 struct 新增／刪除 `public` 欄位 | 內容 regex | **L3** ＋ 計畫標記「疑似跨層契約變更：需要 ADR 判斷」 |
| E5 | **架構測試本身**：`ArchitectureRegressionTests.cs`（`WriterRules`／`LayerRules` 變動）、`PrefabWiringTests.cs` | 路徑 | **L3** |
| E6 | **測試基礎設施**：任何 `*.asmdef`、`Packages/manifest.json`、`ProjectSettings/**`、`docs/artifacts/editor-memory/**`、本調度器自己的檔案、PlayMode／EditMode 共用 helper | 路徑 | **L3** ＋ 基礎設施自己的 selftest |
| E7 | **出貨資產**：`Assets/Scenes/**`、`Assets/Prefabs/**`、`Assets/ScriptableObjects/**`（StateMachine／Motion Bake）、`*.inputactions` | 路徑 | **L3**（9 個 PlayMode 類別載入出貨場景） |
| E8 | ADR 檔新增、或狀態欄改為 Trial／Accepted | 路徑＋內容 | **L3** |
| E9 | 刪除／改名 `.cs` 或測試檔 | `git diff --name-status` 的 D／R | **L3** |
| E10 | 沒有 `last-verified.json`、或它的 HEAD 不是目前 HEAD 的祖先 | — | **L3** |
| E11 | 計畫只選到 0 個 PlayMode 類別，但變更含 runtime `.cs`（非 Editor） | — | 至少 PlayMode 快速群（Phase 4 前：全量） |

**原則**：任何規則**無法評估**（解析失敗、glob 錯誤、快照讀不到）⇒ 往上升級，⛔ 不得往下。

---

## 6. 結果分類與已知失敗

### 6.1 三種「沒有通過」必須分開

| 調度器結論 | 來源 | 對使用者的意義 |
|---|---|---|
| **VERIFIED** | 所有計畫內呼叫 exit 0 | 本層驗證通過 |
| **VERIFIED_WITH_KNOWN_FAILURES** | exit 1，且**每一個**失敗都比對到已知清單（名稱＋症狀） | 沒有新退步；已知項仍在 |
| **FAILED** | exit 1，且有任何一項是新失敗、或已知名稱但症狀不同 | 有退步 |
| **BLOCKED（環境）** | exit 2、10–32、130（dirty scene 15、編譯 14、Editor 無回應 15、在途 run 25…） | **什麼都沒驗證**；原因＋需要誰處理。⛔ 不重試、不降級、不存場景、不重啟 Editor；計畫後續呼叫一律不執行並列入「未執行」 |
| **INCOMPLETE（未執行）** | exit 3（選不到——**計畫本身有錯**，視同 FAILED 計畫）、exit 5（全部 skipped）、或因前面 BLOCKED 而沒跑 | 列出未執行的呼叫與原因 |

報告**永遠**附一張「未執行清單」：每個沒被選到的 PlayMode 類別＋原因（「L2：模組未受影響，留待 Gate L3」）、每個因 BLOCKED 沒跑的呼叫。

### 6.2 已知失敗：比對「具體測試＋具體症狀」

`known-failures.json`（進版控）每一筆：

| 欄位 | 例：EditMode | 例：PlayMode |
|---|---|---|
| `mode` | EditMode | PlayMode |
| `fullName`（**完整比對**） | `Project.Tests.EditMode.StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` | `Project.Tests.PlayMode.MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact` |
| `symptom`（訊息必須包含的穩定片段，可多個） | `FootIKController.settings.GroundLayers=503` | `Expected: True` ＋ `But was:  False` |
| `cause` | 使用者未 commit 的 `SampleScene` WIP（環境相依） | 既有缺陷（引用來源文件） |
| `since`／`reviewBy`／`ref` | 2026-10-13 ／ 場景 commit 時 ／ WORKLOG | — |

比對結果每一項只會是：

| 分類 | 條件 | 處置 |
|---|---|---|
| `KNOWN_MATCH` | 名稱相同 ∧ 所有症狀片段都在訊息中 | 不算退步 |
| `KNOWN_NAME_NEW_SYMPTOM` | 名稱相同 ∧ 症狀不符（例如 `GroundLayers=511`） | **視為新失敗** |
| `NEW_FAILURE` | 不在清單 | 失敗 |
| `KNOWN_NOT_OBSERVED` | 清單裡的測試這次**通過** | 報告「可能已修好，請確認後移除」（K4 的六個 Stair 失敗就是這種） |
| `KNOWN_NOT_RUN` | 清單裡的測試這次沒被選到 | 列入未執行清單，不推論 |

⛔ 清單只能由使用者裁決或明確的工作包新增，並寫明原因；調度器**不會**自動把失敗加進清單。

---

## 7. 方案比較

| | **A 只有 Skill 指導** | **B Skill＋模組對照＋確定性選擇腳本（推薦）** | **C 完整變更影響分析＋調度系統** |
|---|---|---|---|
| **做法** | 在 Skill 寫「改 X 跑 Y」的指引，Claude 自行挑 `--filter` | Skill 管流程與規則；`test-map.json`＋`plan_tests.py` 產生計畫；`run_plan.sh` 只透過安全入口執行；`classify_results.py` 比對已知失敗 | 靜態呼叫圖（Roslyn）或覆蓋率（`com.unity.testtools.codecoverage`）建立「測試 ↔ 程式碼」對應，自動排程 |
| **符合「不能只靠 AI 主觀」** | ⛔ 不符合 | ✅ | ✅ |
| **漏測風險** | 高：F6（名稱不含模組字樣的契約測試）、F2（`\|` 誤用）、未執行無紀錄 | 低且有界：EditMode 永遠全量；只有 PlayMode 類別可能延後；未對照 ⇒ L3；Gate 一定 L3 | 理論最低，但本專案是**黑板資料驅動**：寫入者與讀者之間沒有靜態呼叫邊 ⇒ 呼叫圖會系統性漏掉；9 個 PlayMode 類別載入整個場景 ⇒ 覆蓋率會把它們對到幾乎所有檔案 |
| **實作成本** | 0.5 天 | **2.5–3.5 天**（§10 Phase 1–3） | 1–3 週＋新套件（`Packages/manifest.json`）＋覆蓋率執行本身很慢 |
| **維護** | 隨文字漂移 | 對照表有完整性自測；權威清單從 `WriterRules` 解析 | 高；第三方工具升級風險（CLAUDE.md Gate B） |
| **省下的時間** | 不穩定 | 小修改：5–7 min → 1.2–4 min；Gate 不變 | 與 B 相近——成本瓶頸是**每次呼叫的固定開銷**與兩個慢類別，不是選擇精度 |
| **與既有規則** | — | 符合「先 SOP 後工具」：Gate A（每天多次、人工容易漏、出錯沉默）成立；Gate B（不依賴第三方 private 結構）成立 | Gate B 不成立；違反「第二個使用者出現前不建 abstraction」 |

**推薦 B。** C 的額外精度換不到時間（瓶頸在固定開銷），而且在資料驅動架構下反而會**系統性漏測**。

---

## 8. 推薦設計（B）

### 8.1 職責切分

| 元件 | 職責 | ⛔ 不做 |
|---|---|---|
| **Skill**（`unity-test-orchestrator`） | 何時用哪一層、怎麼讀報告、BLOCKED 時怎麼跟使用者說、Gate 一定 L3、Probe 規則 | 不決定範圍（範圍由腳本算） |
| `plan_tests.py` | 讀變更集＋快照＋對照表＋`WriterRules` ⇒ 輸出 `plan.json`／`plan.md`（層級、每次呼叫、**每條升級的理由**、未執行清單） | 不碰 Unity、不執行測試 |
| `run_plan.sh` | 依序執行計畫中的呼叫，**每一次都經** `safe_unity_test.sh`；BLOCKED 就停 | ⛔ 不呼叫 `run_tests`、不重試、不改任何防護參數、不存場景 |
| `classify_results.py` | 讀每次呼叫保存的 test_status JSON ⇒ §6 的分類 ⇒ `report.md` | 不改 `known-failures.json` |
| `known-failures.json`、`test-map.json` | 資料（進版控） | — |

### 8.2 完整檔案結構

```text
.claude/skills/unity-test-orchestrator/
  SKILL.md                    何時使用、L0–L3、命令、報告解讀、BLOCKED 處置、Probe 規則、禁止事項
.agents/skills/unity-test-orchestrator/
  SKILL.md                    鏡像（只差 CLAUDE.md↔AGENTS.md 稱呼；鏡像一致性由 selftest 檢查）

docs/artifacts/test-orchestrator/          ← 腳本與資料的正本（與 editor-memory 同一慣例：進版控、可追溯）
  README.md                   用法、exit code、資料格式、改腳本後必跑的 selftest
  test-map.json               模組 → 原始碼 glob → 負責的 PlayMode 類別；高風險 glob；純文件 glob
  known-failures.json         §6.2
  plan_tests.py               變更集 → plan.json／plan.md（純 Python，-I 執行，不碰 Unity）
  run_plan.sh                 plan.json → 依序呼叫 safe_unity_test.sh → 收集紀錄目錄
  classify_results.py         紀錄 → report.md（VERIFIED／…／BLOCKED／INCOMPLETE）
  snapshot.py                 L3 結束時寫 last-verified.json（HEAD＋dirty 檔 blob hash）
  selftest_plan.sh            golden 變更集 → 預期計畫（含每條 E1–E11、對照表完整性、權威清單解析）
  selftest_classify.sh        真實 test_status fixture → 預期分類
  selftest_run_plan.sh        假的 safe_unity_test.sh：BLOCKED 停止、不重試、不呼叫 run_tests
  fixtures/
    diffs/                    name-status＋內容片段的 golden 輸入
    test-status/              從 Logs/safe-test 複製的真實結果（含 C0、GroundLayers=503）

Logs/test-orchestrator/                    （gitignored，比照 Logs/safe-test）
  <UTC>-<label>/plan.json plan.md invocations.tsv report.md
  last-verified.json
```

### 8.3 命令（草案）

```text
python -I docs/artifacts/test-orchestrator/plan_tests.py [--label X] [--force-tier L3]   # 只產生計畫（可隨時跑，不碰 Unity）
bash docs/artifacts/test-orchestrator/run_plan.sh <plan.json>                          # 執行；內部只呼叫 safe_unity_test.sh
```

- `--force-tier` 只能**往上**（L3），往下的參數不存在。
- `plan.md` 的第一行就是層級與「為什麼」，例如：`L3 — E2: Presentation/Motion/MotionDriver.cs（WriterRules: IsGrounded…）`。

### 8.4 如何接入目前工作流程

| 位置 | 變更（Phase 3） |
|---|---|
| `CLAUDE.md`／`AGENTS.md`「Unity 測試執行」 | 加**一行**：「選範圍用 `unity-test-orchestrator` Skill；入口規則不變」。安全入口的條文**不動** |
| `docs/12` §2（工作流 11 步）與 §6 | 「Implement＋automated verification」步驟引用 L0–L2；Integration Gate 步驟寫明「L3 必跑，報告路徑寫進交付訊息」；§6 加一行說明兩套編號的差別 |
| `unity-live-control` Skill「測試」一節 | 一行指標，不複製規則 |
| Feature Slice 收尾 commit | 收尾前 L3；commit 訊息附 `report.md` 的結論列（VERIFIED／VERIFIED_WITH_KNOWN_FAILURES） |
| WORKLOG fold-back | 「回歸」欄改引用報告的分類，而不是手寫「唯一失敗＝…」 |

---

## 9. 驗收標準（B）

| AC | 內容 | 驗證 |
|---|---|---|
| AC1 | 計畫**確定性**：同一變更集＋快照＋對照表 ⇒ 位元相同的 `plan.json` | selftest 跑兩次比對 |
| AC2 | **完整性**：`Assets/Scripts/**` 的每個資料夾、每個 PlayMode 測試類別都在對照表；缺漏 ⇒ 計畫為 L3 並列出 | selftest＋對目前 repo 實跑 |
| AC3 | E1–E11 每一條都有 golden 案例，且「規則無法評估 ⇒ 升級」有案例 | `selftest_plan.sh` |
| AC4 | 權威清單從 `WriterRules` 解析，與手寫清單比對一致；把 `WriterRules` 改壞 ⇒ 解析失敗 ⇒ L3 | selftest＋變異 |
| AC5 | 純文件 ⇒ L0，報告明列「未執行 Unity 測試：純文件」；`docs/artifacts/**/*.sh` ⇒ **不是**純文件 | selftest |
| AC6 | 每個未被選中的 PlayMode 類別、每個因 BLOCKED 沒跑的呼叫，都在報告的「未執行」表裡並附原因 | selftest |
| AC7 | `run_plan.sh` 只呼叫 `safe_unity_test.sh`（原始碼不含 `run_tests`）；任何 BLOCKED exit ⇒ 立即停止、不重試、後續呼叫列入未執行 | 假入口 selftest＋grep |
| AC8 | dirty scene 的真實環境：計畫回報 BLOCKED（15）並說明需要使用者處理；Editor 狀態前後一致（未存檔、未切場景） | live 一次 |
| AC9 | 已知失敗：用真實 fixture 驗證 `KNOWN_MATCH`／`KNOWN_NAME_NEW_SYMPTOM`／`NEW_FAILURE`／`KNOWN_NOT_OBSERVED`／`KNOWN_NOT_RUN` 五類 | `selftest_classify.sh` |
| AC10 | 快照：上次 L3 時已存在且未變的 WIP 不觸發；改過的觸發；沒有快照 ⇒ L3 | selftest |
| AC11 | **漏測稽核**：若 L3 在某個 PlayMode 類別失敗，而同一變更集先前的 L2 沒有選它 ⇒ 報告 `MAPPING_ESCAPE`，要求修對照表 | selftest＋實際運作紀錄 |
| AC12 | 鏡像：`.claude` 與 `.agents` 兩份 SKILL.md 只差稱呼 | selftest |
| AC13 | 實效：導入後 2 週內，迭代中的驗證中位耗時下降；Gate 的 L3 執行率 100%；`MAPPING_ESCAPE` 為 0，或每一筆都已修 | 紀錄統計 |

---

## 10. 逐步導入計畫

| Phase | 內容 | 改動範圍 | 風險 | 需核准 |
|---|---|---|---|---|
| **0（本輪）** | 本文件 | 只有文件 | — | — |
| **1 已知失敗比對** | `known-failures.json`＋`classify_results.py`＋selftest；手動指定紀錄目錄即可用 | 新增檔；**不碰**入口、測試 | 最低（唯讀） | ✅ 需要 |
| **2 計畫器（影子模式）** | `test-map.json`＋`plan_tests.py`＋`snapshot.py`＋selftest。**只產生計畫、不改執行方式**：照舊跑全量，同時記錄「L2 會選什麼」，事後比對有沒有漏 | 新增檔 | 低 | ✅ |
| **3 執行器＋Skill** | `run_plan.sh`、Skill 雙份、`CLAUDE.md`／`AGENTS.md`／`docs/12` 各一行指標 | 新增檔＋三處一行 | 中（開始實際少跑 PlayMode） | ✅；前提：Phase 2 影子期無漏測 |
| 4（選配） | 入口加 `--filter-type category`（CLI 已支援）＋PlayMode 快速類別加 `[Category]` ⇒ L2 一次呼叫 | **改安全腳本與既有測試**＋它們的 selftest | 中 | ✅ 另案 |
| 5（選配） | `docs/53` §16 的 P2 hook：強制所有 Unity 測試經入口 | `.claude/settings` | 低 | ✅ 另案 |

---

## 11. 需要你裁決的問題

| # | 問題 | 選項 | 建議 |
|---|---|---|---|
| **Q1** | 分層命名與 `docs/12` L0–L6 撞名 | ①沿用 L0–L3（文件註明）②改名 **R0–R3**（Run scope） | ② |
| **Q2** | 方案 | A／**B**／C | B |
| **Q3** | EditMode 是否永遠全量 | ①**是**（5.4 s，省不到時間，消除 EditMode 漏測）②也做選擇 | ① |
| **Q4** | Gate 的 L3 是否為硬規則 | ①**每個 Slice 的 Integration Gate／收尾 commit 前一律 L3** ②由計畫器決定 | ① |
| **Q5** | 影子模式要多久才允許 Phase 3 實際少跑 | ①**10 次真實變更集無漏測** ②固定 2 週 | ① |
| **Q6** | 已知失敗清單的維護權 | ①**只有使用者裁決或明確工作包可新增**，必須附原因與檢討時點 ②Claude 可在報告建議後自行新增 | ① |
| **Q7** | 是否核准 Phase 4（改安全入口＋測試加 Category） | ①先不做 ②與 Phase 3 一起 | ①：先用影子期資料證明需要 |

⛔ 本輪沒有改任何測試、安全腳本、CI 或 Skill 檔。

---

## 12. 裁決與實作基線（2026-10-13，使用者裁決）

### 12.1 Q1–Q7

| # | 裁決 | 對 §1–§11 的修正 |
|---|---|---|
| **Q1** | 命名 **R0–R3**（Run scope），避免與 `docs/12` §6 的 L0–L6 衝突 | 本節起一律 R0–R3；§4 的 L0–L3 依序對應 R0–R3 |
| **Q2** | 採 **B**：Skill＋測試對照表＋確定性選擇腳本；暫不做 C | — |
| **Q3** | 只要進入 Unity 測試流程，**EditMode 一律全量** | — |
| **Q4** | **必須 R3**：Feature Integration Gate、合併／發布、重大架構修改、高風險檔案變更。**普通開發 commit 不強制 R3**，由變更風險決定。⛔ 不得以拆小 commit 規避高風險規則 | §4「每個 Slice 收尾 commit 前一律 L3」改為本列；防規避的機制見 §12.3 |
| **Q5** | 影子模式至少 **10 次真實改動**，並加入**合成 Git diff fixtures**（高風險、未知檔案、失效快照、測試變更、跨層依賴）。影子期**仍跑全量**，並比對「未選中測試的新增失敗」 | §10 Phase 2 的放行條件 |
| **Q6** | 已知失敗清單的**新增、變更、移除都需使用者裁決**；比對完整測試名稱＋穩定症狀，不能只看 exit code | §6.2；工具不會自動改清單 |
| **Q7** | Phase 4 暫不做；先量 R2 實際節省，再決定 category 篩選與入口功能 | — |

### 12.2 補充裁決

| # | 內容 | 落地 |
|---|---|---|
| S1 | **Editor 資料夾不能一律視為不影響 PlayMode**：Bake、資產產生、Prefab／SO 修改工具要考慮下游產物 | §12.4 |
| S2 | 所有測試繼續經 `safe_unity_test.sh`；不得繞過 dirty scene 等防護 | 選擇器只產生計畫；影子期照舊由入口跑全量 |
| S3 | 沒有可信快照、範圍未知、規則無法評估 ⇒ **一律 R3** | §5 原則不變 |
| S4 | Explicit Probe 不自動納入 R0–R3，但必須能**明確指定**執行 | 選擇器 `--probe Class.Method` 產生獨立呼叫（帶 `--include-explicit`） |
| S5 | 快照要明確定義基準版本、工作樹內容 hash、失效條件 | §12.3 |
| S6 | `.claude` 與 `.agents` 的 Skill 必須一致，並有防漂移驗證 | `check_skill_mirror.py`（對既有三個 Skill 已生效） |

### 12.3 快照（`last-verified.json`）的精確定義

**內容**：`baseCommit`（完整 SHA）、`worktree`（快照當下**每個** dirty／untracked 路徑 → 內容 blob hash 或 `ABSENT`）、`mapHash`（`test-map.json` 的 SHA-256）、`plannerVersion`、`createdUtc`、`failures`（該次全量的失敗指紋：完整名稱＋正規化訊息）、`records`（兩個入口紀錄目錄）。

**「某路徑有沒有變」只看內容**：
`快照時的內容(P)` ＝ `worktree[P]`（若快照時 dirty）否則 `baseCommit:P` 的 blob（不存在＝`ABSENT`）；
`現在的內容(P)` ＝ 工作樹檔案的 blob hash（`git hash-object`，與 git 的換行正規化一致）。
兩者不同 ⇒ 變了。檢查的路徑集合＝`baseCommit..HEAD` 的變更 ∪ 現在的 dirty ∪ 快照時的 dirty。
⇒ 長期未 commit、內容沒再變的場景 WIP **不會**被當成改動；之後原封不動 commit 進去也不會。

**何時寫入**：只在一次**完整**的 EditMode＋PlayMode 全量之後（兩者都有結果 JSON、入口 exit 0 或 1，沒有 BLOCKED／INCOMPLETE），而且執行**前後**的工作樹狀態完全相同（執行中被改 ⇒ 不寫）。寫入時以 `git hash-object -w` 把 dirty 的 `.cs`／`.asmdef` 內容存進 git 物件庫（不動 index、ref、工作樹），供下次做內容規則比對。

**防規避（Q4）**：變更集永遠是「上一次全量已驗證狀態 → 現在」，**不是**「上一個 commit → 現在」。R2 不移動基準；拆成幾個小 commit 只會讓變更集累積，高風險路徑不論大小一律 R3。

**失效 ⇒ R3**：檔案不存在／無法解析／schema 不符；`baseCommit` 不存在或不是 HEAD 的祖先；`mapHash` 或 `plannerVersion` 不同；超過 `snapshotMaxAgeDays`（預設 7 天：Library／套件快取不在 git 裡）；需要做內容比對的檔案，其快照 blob 已不在物件庫。

### 12.4 Editor 程式的分級（S1）

PlayMode 組件不參照 `Project.Editor`，但 **PlayMode 測試是在 Editor 裡跑的**：自動執行的 Editor 程式、開著的 Inspector／視窗都可能在測試期間寫入狀態。分級以對照表宣告為準，並以原始碼記號做**一致性檢查**（宣告比記號寬鬆 ⇒ `MAP_INCONSISTENT` ⇒ R3）：

| 等級 | 判準（記號） | 層級 |
|---|---|---|
| 自動執行 | `InitializeOnLoad*`、`DidReloadScripts`、`AssetPostprocessor` | **R3** |
| 寫資產／場景／匯入設定 | `AssetDatabase.Create/Save/Import…`、`EditorUtility.SetDirty`、`ApplyModifiedProperties*`、`PrefabUtility.Save*`、`SaveAndReimport`、`ModelImporter`、`Undo.*`、`EditorSceneManager.MarkSceneDirty` | 對照表宣告的**下游產物**：通用動作真相（Motion Bake、批次動畫／prefab）⇒ **R3**；範圍明確（Traversal 參數、場景表面、樓梯幾何）⇒ **R2＋消費這些產物的 PlayMode 類別**；同一變更集裡下游產物本身也變了 ⇒ 依資產規則 **R3** |
| 唯讀（Inspector、分析、除錯視窗） | 以上皆無 | **R1** |
| 未列入對照表的 Editor 檔 | — | **R3** |

### 12.5 已知失敗清單的現況

`known-failures.json` 以**空清單**交付（Q6：新增需要你裁決）。目前全量中的兩個失敗是**候選**，待你裁決是否列入：

| 候選 | mode | 完整名稱 | 建議症狀片段 | 原因 |
|---|---|---|---|---|
| K-1 | EditMode | `Project.Tests.EditMode.StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` | `FootIKController.settings.GroundLayers=503` | 使用者未 commit 的 `SampleScene` WIP（環境相依） |
| K-2 | PlayMode | `Project.Tests.PlayMode.MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact` | `Expected: True` ＋ `But was: False` | 既有缺陷（WORKLOG／`docs/52`） |

未裁決前，比對器會把它們報為 `NEW_FAILURE`——這是刻意的。

> 🔄 2026-10-14：D-1 有條件批准；核對後已列入，見 §14.1。

---

## 13. 實作狀態：Phase 1＋Phase 2（2026-10-13）

> 程式在 `docs/artifacts/test-orchestrator/`（用法見該資料夾 `README.md`）。⛔ 沒有改 `safe_unity_test.sh`、任何 Unity 測試或 CI。
> **狀態：Automated Verification Passed（工具本身）／影子期尚未開始（0 個真實樣本）。** Phase 3 未啟用：選擇器目前不決定少跑任何測試。

### 13.1 交付內容

| Phase | 元件 | 對應裁決 |
|---|---|---|
| 1 | `classify_results.py`＋`known-failures.json`（空清單，§12.5 兩個候選待裁決） | Q6、§6.2 |
| 2 | `plan_tests.py`＋`test-map.json`＋`snapshot.py` | Q1、Q3、Q4、S1、S3、S4、S5 |
| 2 | `shadow_compare.py`（`Logs/test-orchestrator/shadow.tsv`，`countsTowardQ5` 欄） | Q5 |
| 2 | `check_skill_mirror.py` | S6（對既有三個鏡像 Skill 已生效：一致） |
| 2 | `selftest.py`（127 案）、`selftest_mutations.py`（18 種退化）、`fixtures/planner-cases.json`（50 個合成變更集） | Q5、AC1–AC12 |

### 13.2 自動化驗證結果

| 套件 | 內容 | 結果 |
|---|---|---|
| MAP | 真實 repo 的規則表完整性（每個 `Assets/Scripts` 檔案恰好落在一條規則、22 個 PlayMode 類別與 probe 旗標、Editor 分級與原始碼記號一致）；`WriterRules` 解析出 10 個權威檔；解析失敗 ⇒ `None` | **7／7** |
| PLAN | 50 個合成變更集：高風險（MotionDriver、Runner、CombatContext、核心介面、Movement／Facing 權威）、未知檔案／資料夾／資產、失效快照、EditMode／PlayMode 測試變更與刪除、Probe、跨層 `using`、新核心介面實作者、內容不可評估、Editor 四級＋自動執行＋宣告不一致、場景／prefab／`.meta`、asmdef／設定／套件、Gate context、基礎設施、`WriterRules` 解析失敗、成本取捨。每案另驗**不變量**：每個呼叫都經安全入口、tier 呼叫不含 Explicit、非 R0 一定 EditMode 全量、R3 一定 PlayMode 全量、每個 PlayMode 類別不是「會跑」就是列在「未執行」並附原因 | **50／50** |
| DET | 重算位元相同；變更順序不影響結果 | **2／2** |
| GIT | 暫存 git repo：未再變動的長期 WIP 不算變更、再改就算、原封 commit 不算、快照 blob 取回舊內容、blob 遺失 ⇒ 不可評估 ⇒ R3、新增／刪除、**拆成兩個小 commit 仍抓到 MotionDriver ⇒ R3**、七種失效（遺失、規則變更、過期、base 不存在、base 非祖先、planner 版本、schema、無法解析） | **18／18** |
| CLS | 用**真實** Unity 失敗訊息（`GroundLayers=503`、`C0` 中文訊息）：KNOWN_MATCH、NUnit 雙空白正規化、KNOWN_NAME_NEW_SYMPTOM、NEW_FAILURE、KNOWN_NOT_OBSERVED、KNOWN_NOT_RUN、KNOWN_MISSING_FROM_FULL_RUN、dirty scene exit 15 ⇒ BLOCKED、exit 2／3／4／5、check-only、缺 JSON、執行中、**BLOCKED 不得掩蓋新失敗**、取樣器告警後仍列失敗、Inconclusive、Skipped、清單格式錯誤七種 ⇒ 拒絕 | **32／32** |
| SHD | 未選中類別的新失敗 ⇒ ESCAPE；前次全量已有 ⇒ 不算；同名不同訊息 ⇒ ESCAPE_SYMPTOM；選中範圍內 ⇒ 不算；他 HEAD／過濾過／被阻擋的紀錄、計畫後工作樹被改 ⇒ 樣本無效且不寫快照；TSV 欄位數一致；只有「快照有效」的樣本計入 Q5 | **13／13** |
| MIR | 只差 `CLAUDE.md`↔`AGENTS.md` ⇒ 一致；其他差異、缺 skill、多檔 ⇒ 漂移；真實 repo 一致 | **5／5** |
| 變異 | 在副本上逐一拿掉：高風險表、`WriterRules`、內容規則、未知檔案 R3、失效快照 R3、Gate R3、成本比較、Probe 排除、Editor 自動執行、快照 WIP、祖先檢查、過期檢查、症狀比對、核准欄位、BLOCKED 優先序、影子選中判定、影子工作樹檢查、鏡像比較 | **18／18 被抓到** |

### 13.3 真實環境

| 項目 | 結果 |
|---|---|
| `--check-map`（live repo） | OK，0 問題 |
| 比對器 × 最新全量（EditMode `20261013T095531Z`、PlayMode `20261013T100953Z`）＋ P5-0 的停止紀錄 | `GroundLayers=503` 與 `C0` 皆為 **NEW_FAILURE**（清單為空，刻意）；P5-0 dirty scene ⇒ **BLOCKED**；另列出 EditMode 一個 Skipped 測試（`MovementIntentTests.AIMovementSource_WithoutCamera_ProducesNonZeroWorldDirection`）＝未執行 |
| 計畫器 × 目前工作樹 | **R3**（`SNAPSHOT_MISSING`；另有 `ProjectSettings` WIP、場景／音訊資產 WIP、本工具新檔）。1.3 s，不寫 `__pycache__` |
| 影子樣本 | **0**。需要一次完整 EditMode＋PlayMode；目前 `SampleScene` dirty ⇒ 入口正確地拒絕（BLOCKED）。第一個有效樣本只建立快照，第二個起才計入 Q5 |

### 13.4 已知限制（不隱藏）

- **模組 → PlayMode 類別的對照是一次性的人工判斷**（`test-map.json` `runtimeModules`），依據是各類別的 `using` 與載入的場景／prefab。它正是影子期要驗證的東西——`ESCAPE` 會直接指出哪條對照漏了。
- 內容規則只看 `.cs` **新增行**的 `using Project.*` 與核心介面實作；語意上的影響（改常數改變下游行為）偵測不到，由對照表＋Gate R3 承擔。
- R2 成本模型是常數（每次呼叫 60 s 固定開銷＋最近一次全量的各類別時間）；實際節省要由影子期量（Q7）。
- 長期 WIP 很多時（目前約 1000 個路徑，多數在 `.codex-temp/`），無快照的計畫會列出全部（皆為 R0 `UNITY_INVISIBLE`），報告冗長但不影響判定。
- 快照寫入要求「計畫到比對之間工作樹完全不變」：若 Unity 在測試期間重新序列化資產或產生 `.meta`，該樣本會被判無效——保守方向，需在影子期觀察發生頻率。

### 13.5 待你裁決

| # | 問題 | 建議 |
|---|---|---|
| D-1 | §12.5 的兩個已知失敗候選（K-1 場景 mask、K-2 `C0`）是否列入，症狀片段是否接受 | 由你決定；未列入前比對器持續報 FAILED |
| D-2 | `test-map.json` 的模組 → 類別對照是否需要你先過目，或直接進影子期由 ESCAPE 修正 | 直接進影子期（漏的會被量到） |
| D-3 | 何時開始累積影子樣本：等 P5-0 那次全量一起做，或另外安排 | 與下一次需要全量的工作一起做，不額外佔 Editor |

---

## 14. 影子驗證期（2026-10-14，使用者裁決）

> **狀態**：Phase 1／2 驗收通過，**影子期開始**；**Phase 3 未啟用**，且影子期達標**不會**自動啟用（需另行裁決）。⛔ 仍不改 `safe_unity_test.sh`、Unity 測試、CI。

### 14.1 D-1：已知失敗 K-1、K-2（有條件批准 → 已核對後列入）

**核對方法（可重跑）**：`classify_results.py --verify-entries <records>`——條目只有在「至少一份基準紀錄中失敗且所有症狀片段都在」**且**「沒有任何紀錄以其他症狀失敗」時才是 `CONFIRMED`。對 `Logs/safe-test/` 全部 47 份有結果的紀錄執行：

| 條目 | 完整名稱 | 症狀片段（全部必須出現） | 核對結果 | 追蹤來源 | 重新調查條件 |
|---|---|---|---|---|---|
| **K-1** | `Project.Tests.EditMode.StairAuthoringContractTests.Scene_InstanceMasks_MatchSemanticTable` | `scene/X Bot FootIKController.settings.GroundLayers=503`、`應為 -9` | **CONFIRMED**：8 份含此測試，8／8 Failed、訊息逐字相同（2026-10-12 `7c4c4e4` → 10-13 `a2841e1`） | `docs/54` AC-0.5、§726；`docs/44` 語意表 | 場景 WIP commit 或還原；出現其他值／角色；測試通過；2026-10-28 |
| **K-2** | `Project.Tests.PlayMode.MotionDriverCommittedMovementPlayModeTests.C0_TraversalDoesNotReportGroundEdgesFromIncidentalContact` | `陽性對照失敗：非 traversal 的 false→true 邊沿本來就該回報落地`、`Expected: True`、`But was: False` | **CONFIRMED**：11 份，11／11 Failed、訊息逐字相同（2026-10-11 `4fd0962` → 10-13 `a2841e1`） | `docs/50` M-02（:41、:158、:390、:495） | `docs/50` G-1 裁決或 M-02 開工；edge 抑制／grace 邏輯變更；訊息改變；測試通過；2026-10-28（`docs/50`:473：紅燈不得長期以「既有」存在） |

- 每筆條目帶 `approvedBy`／`approvedOn`／`approvalRef`（本節）／`source`／`reinvestigateWhen`；比對器現在把這五個欄位列為必填，缺任一欄位就拒絕整份清單。
- **症狀變更＝`NEW_FAILURE`**：已知測試以不同症狀失敗時，報告直接標 `NEW_FAILURE`，並註明 `symptomChangedFrom: K-n`（不再使用另一個分類名稱）。例：`GroundLayers=511` 不會被 K-1 吸收。
- 套用後，最近一次全量（EditMode `20261013T095531Z`、PlayMode `20261013T100953Z`）的分類由 `FAILED` 變為 **`VERIFIED_WITH_KNOWN_FAILURES`**（exit 10）。
- 兩筆的 `reviewBy` 2026-10-28 是**我提出的期限**，可由你改。

### 14.2 `AIMovementSource_WithoutCamera_ProducesNonZeroWorldDirection` 為何 Skipped（獨立調查，⛔ 不是通過、不是已知失敗）

| 事實 | 證據 |
|---|---|
| 測試在 `agent.Warp(origin.position)` 回傳 false 時呼叫 `Assert.Ignore`（「EditMode 無法把 NavMeshAgent 放上臨時 NavMesh——環境限制」） | `MovementIntentTests.cs:262–267` |
| 程式註解說 Warp「不定期」回傳 false；**實際上在所有留有紀錄的 8 次 EditMode 全量中 8／8 Skipped**（2026-10-12～13）——不是偶發，是每次 | `Logs/safe-test` |
| 被跳過的是**交戰分支**的行為：在沒有相機的情況下，`InCombat` 的 `CombatContext` 要產生非零的水平單位方向 | `:276–297` |
| 架構面「AI 不得依賴相機」由 `ArchitectureRegressionTests.A30` 靜態掃描守住 | 測試註解；A30 |
| 行為面的覆蓋：非戰鬥分支在**沒有相機的執行期場景**中有 PlayMode 覆蓋（`EnemyNonCombatPlayModeTests.Navigation_WaypointBehindWall_IsReachedByGoingAround`）；**交戰分支「沒有相機」目前沒有任何會執行的回歸測試**（出貨場景有玩家相機；`EnemyCombatPositioningMetrics` 是 `[Explicit]` Probe，不算回歸） | 測試原始碼 |

**結論**：這不是測試不穩定，而是一個**在 EditMode 永遠無法執行**的測試——它的覆蓋形同不存在，而報告裡長期只顯示「1 skipped」。比對器現在在報告標題列出「N 個選中但未執行的測試——未驗證」。
**建議（需你裁決，本輪不改測試）**：①把它移到 PlayMode（Play 生命週期下 agent 會註冊，行為面才真的被驗）；或 ②刪除 EditMode 版本並在 PlayMode 新增對等案例；或 ③維持現狀但正式記錄「交戰分支無相機行為只有 A30 靜態守門」。我建議 ①。

### 14.3 影子期規則（D-2、D-3、補充 1–5）

| 規則 | 落地 |
|---|---|
| D-2：直接進影子期；**每個 ESCAPE 都要調查**是映射缺漏、規則不足或測試不穩定，⛔ 不得只為歸零而擴大規則 | `escape-investigations.json`（分類限 `mapping-gap`／`rule-gap`／`flaky-test`，`evidence` 與 `action` 必填）；`shadow_report.py` 列出未調查的 ESCAPE |
| D-3：樣本隨正常完整回歸累積，不額外佔 Editor | 程序見 `docs/artifacts/test-orchestrator/README.md` |
| 補充 1：每個樣本記錄 Change Set、Planned Scope、Selected Tests、Full Results、ESCAPE、Snapshot Validity、預估節省 | `shadow_compare.py` 寫 `Logs/test-orchestrator/shadow/<id>/sample.json`＋`sample.md`；節省分**預估**（計畫的成本模型）與**實測**（真實全量的逐案時間：被略過的測試時間＋少掉的呼叫開銷） |
| 補充 2：有效樣本 ≥ 10 次真實改動，且合成案例覆蓋仍需滿足 | 報告的 Q5 進度只計「有效且計畫時快照可信」的樣本；合成案例由 `selftest.py` 守（50 個變更集） |
| 補充 3：大多數樣本被升級 R3 時，要報告**實際效益** | 報告列出層級分布、R3 比例與原因、預估／實測節省、有節省的樣本數；R3 過半時明示「效益小」 |
| 補充 4：Phase 3 需另行裁決 | 報告第一行固定聲明 |
| 補充 5：不改入口、Unity 測試、CI | 本節所有變更都在 `docs/artifacts/test-orchestrator/` |

### 14.4 驗證

自測 **151／151**（新增：條目必填欄位四種、症狀變更 ⇒ `NEW_FAILURE`、正式清單的 K-1／K-2 與真實訊息相符、`GroundLayers=511` 不被吸收、`--verify-entries` 三種狀態、樣本紀錄七大欄位、實測與預估節省、無效樣本仍留紀錄、報告 Q5 計數／R3 比例／實測節省比例／未調查 ESCAPE／調查格式錯誤）；變異 **22／22**（新增：症狀變更被當成已知、實測節省忽略略過範圍、報告計入無快照樣本、報告不要求調查）；`--check-map` OK。

### 14.5 目前狀態

- 影子樣本：**0**。2026-10-14 01:23Z 安全入口 `--check-only` 回 `PREFLIGHT_FAILED`（32，`editor-not-ready-for-this-project`）——沒有可用的 Editor，未嘗試重啟、存檔或捨棄場景。
- ⚠️ 預期的效益風險（尚未量到，先記下）：目前工作樹長期有 `ProjectSettings`、場景、音訊資產 WIP——只要它們在**下一個快照之後**又被改，計畫就會 R3。第一批樣本很可能以 R3 為主；報告會如實顯示。

### 14.6 Skipped 覆蓋缺口的處置：移到 PlayMode（2026-10-14，使用者同意；✅ 目標測試與突變驗證已在 Unity 通過，R3 見 §14.8）

**原測試的完整意圖**（讀完整檔案與歷史後確認）：它是 `ProduceIntent_WithoutCamera_ProducesZeroWorldDirection` 的對偶——玩家的方向投影自相機基底，沒有相機 ⇒ 零；AI 的方向是世界座標，沒有相機也必須是**非零、水平、單位**的方向。它把目標放在 1.5 m（交戰帶 [1.25, 2.0] 內），所以實際走的是 **Strafe 分支**，不是 Approach。2026-09-10 寫成時就帶著 `Assert.Ignore`。
**它比 A30 多守什麼**：Runner 對每個角色都把 `data.CameraTransform` 填成 `playerCamera ?? Camera.main`（`CharacterPipelineRunner.cs:226`）；A30 只掃 `Core/Movement` 有沒有 `CameraTransform` 這個字——改讀 `Camera.main` 或經別處取得相機，A30 都看不到。

**選擇：搬移並重新設計，不改成純邏輯測試**——方向來自 `EnemyNavigator`＋真 agent 的轉角點，純邏輯的部分（`ResolveEngagementMovement`、`ResolveStrafeDirection`、`EnemySteering`）已由 `EnemyEngagementMovementTests`／`EnemyNavigatorTests` 覆蓋；這個測試的價值正是「整條交戰分支在沒有相機的世界裡跑起來」，只能在 Play 生命週期裡驗。

| 要求 | `AIMovementNoCameraPlayModeTests.Engaged_WithoutAnyCamera_ProducesFiniteWorldDirectionTowardTarget` |
|---|---|
| 1 有效 NavMesh、agent 真的註冊 | 執行期建平地 NavMesh 並斷言建置／加入／出生點與目標可取樣；30 帧內 `agent.isOnNavMesh` 必須為 true；另驗 `updatePosition == false`（A16） |
| 2 進入交戰、沒有相機 | 場景建立後、生成敵人後、結束時三次斷言：`FindObjectsByType<Camera>(Include)` 為 0 且 `Camera.main == null`；黑板 `CameraTransform == null`；30 帧內 `InCombat ∧ HasTarget`（真 `AICombatContextSource`） |
| 3 意圖非零、有限、方向合理 | Approach（5 m → 2.5 m）：每帧有限、非零、水平、單位、與目標方向 dot ≥ 0.95，且 1 s 內實際前進 > 0.5 m；交戰帶內（原情境）：讀 producer 自己的模式逐帧檢查——Strafe ⇒ \|dot\| ≤ 0.25、Approach ⇒ ≥ 0.9、Retreat ⇒ ≤ −0.9、Hold／Recover ⇒ 失敗；至少 10 帧 Strafe（確保原測試的分支真的被執行） |
| 4 不掩蓋初始化失敗 | 全部前置條件都是 `Assert`，訊息以「前置失敗」開頭；沒有 `Assert.Ignore`／`Assume`／`Inconclusive` |
| 5 能抓到退步 | `docs/artifacts/ai-no-camera/mutation_check.py`：M1 無意圖、M2 錯誤方向、M3 只壞 Strafe 分支（三者 A30 都抓不到）。**已驗**：三個突變都能編譯（`--compile-only`；以刻意的語法錯誤確認 `dotnet build` 真的重編該檔），檔案以 SHA-256 還原為與 HEAD 相同。**Unity 實跑（2026-10-14）**：M1／M2／M3 皆 **CAUGHT**（各自命中預期斷言），還原後 **PASS**，`AIMovementSource.cs` 與 HEAD 位元相同 |
| 6 原 EditMode 測試 | **移除**（`MovementIntentTests.cs` 留一段註解指向新測試）。覆蓋由 PlayMode 接手：同一個主張（交戰、沒有相機 ⇒ 非零水平單位方向），且多驗 Approach、真的註冊、真的交戰、真的前進、帶內各模式的方向。未改成純邏輯版本，也不再依賴 `NavMeshAgent.Warp` |
| 7 範圍 | 只動：新 PlayMode 測試檔、`MovementIntentTests.cs`（刪一個測試＋一個不再使用的 `using`）、`test-map.json`（新類別歸入 `Core.Movement.Enemy`，耗時 4.7 s＝首次實跑）、本文件、`docs/artifacts/ai-no-camera/`。⛔ 沒有動 `AIMovementSource` 或導航架構 |

**靜態檢查（不需 Unity）**：`dotnet build` `Project.Tests.PlayMode`（暫時把新檔加入 gitignored 的 `.csproj`，完成後還原）與 `Project.Tests.EditMode`：**0 錯誤**，新檔 0 警告（原有一個 `FindObjectsByType` 排序參數的過時警告已改用新多載）。`--check-map` OK。
**依 §5，這是測試檔／測試基礎設施變更 ⇒ R3**（選擇器實際輸出 R3：E6＋無快照）。⛔ **在 Unity 中執行之前，不能宣告驗證通過。**

**Unity 驗證（2026-10-14，Editor 於 09:47 恢復後；全部經安全入口）**：

| 步驟 | 結果 | 紀錄 |
|---|---|---|
| `--check-only` | CHECK_OK | `Logs/safe-test/20261014T015223Z-nocam-check` |
| 新測試單獨執行 | **PASS**（1／1，4.66 s）；Approach 5.00 → 3.54 m、方向對齊 1.000；交戰帶內 Strafe 24 帧（全部切線）、Approach 66、Retreat 0 | `20261014T015250Z-nocam-first`；數字取自 `Logs/Editor.log` 的 `[AI no-camera]` 行 |
| `mutation_check.py`（M1／M2／M3＋還原） | 三者 **CAUGHT**、還原後 **PASS** | `…-nocam-m1`／`-m2`／`-m3`／`-clean` |
| R3（EditMode＋PlayMode 全量） | 見 §14.8 | — |

⚠️ **突變驗證工具本身第一次執行就有缺陷，已修正**：Windows 的 `CreateProcess` 會先找到 `System32ash.exe`（WSL）而不是 Git Bash ⇒ 安全入口根本沒有啟動，而工具把「exit 1」當成「測試失敗」⇒ 回報 `FAILED_OTHERWISE`（而非誤報 CAUGHT——預期斷言比對擋住了錯誤結論）。修正：明確使用 Git Bash 的路徑，並且**只採信安全入口自己印出的 `==== safe_unity_test: VERDICT (exit N) ====` 行**，沒有這一行一律 INCOMPLETE。

### 14.7 可信基準（快照）的推進規則與驗證

**本輪發現並修正一個缺口**：原本的 `shadow_compare.py` 只要求「完整的全量」就推進快照——**含新增失敗的全量也會推進**。現在快照只有在以下**全部**成立時才推進，否則前一個快照位元組不變：

| 條件 | 不成立時 |
|---|---|
| 樣本有效：兩份紀錄都是完整、未過濾、同 HEAD 的全量，計畫到比對之間工作樹完全相同 | 不推進（含 BLOCKED、exit 3／5、取樣器告警、其他 HEAD、過濾、工作樹被改） |
| 分類為 `VERIFIED` 或 `VERIFIED_WITH_KNOWN_FAILURES` | 不推進（新增失敗、已知測試症狀改變、INCOMPLETE） |
| 沒有任何「選中但未執行」的測試（Skipped／Ignored／Inconclusive） | 不推進（驗證不完整） |
| 不是 `--no-snapshot` | 不推進 |

ESCAPE 的判定也改為以比對器為準：只有 `NEW_FAILURE`（不是已核准的已知失敗＋已核准症狀）出現在計畫會略過的測試裡才算；已知失敗永遠不是 ESCAPE。

**合成案例（SNP，全部通過）**：N1 全部通過 ⇒ 推進；N2 只有已核准的已知失敗 ⇒ 推進；N3 未選中範圍的新失敗、N4 選中範圍內的新失敗、N5 已知測試症狀改變 ⇒ 不推進；N6 BLOCKED（dirty scene）、N7 取樣器告警、N10 什麼都沒執行、N13 工作樹被改、N14 其他 HEAD、N15 過濾過的執行 ⇒ 無效、不推進；N8 有 Skipped、N9 Inconclusive ⇒ 不推進；N11 `--no-snapshot` ⇒ 不推進；N12 沒有舊快照＋乾淨完整的全量 ⇒ 建立第一個快照。每個「不推進」案例都斷言舊快照檔案**位元組不變**。
**變異**：新增四種（新失敗仍推進、Skipped 仍推進、無效樣本仍推進、已知失敗算成 ESCAPE），全部被抓到。其中「無效樣本仍推進」第一次**存活**——原因是我的案例只覆蓋了也會被其他條件擋下的無效情境；補上 N14／N15（完整通過、但來自其他 HEAD／過濾過）後才被抓到。

自測 **162／162**、變異 **26／26**。

### 14.8 R3 結果與第一個影子樣本（2026-10-14）

| 項目 | 結果 | 紀錄 |
|---|---|---|
| 計畫 | **R3**（E6 `ProjectSettings` WIP、E7 場景／音訊資產 WIP、S3 無快照、未對照的材質 WIP） | `Logs/test-orchestrator/20261014T020133Z-shadow-1/` |
| EditMode 全量 | 955 案：954 通過、1 失敗（K-1，KNOWN_MATCH）、**0 Skipped**（原本唯一的 Skipped 已移除） | `Logs/safe-test/20261014T020133Z-shadow-1-edit` |
| PlayMode 全量 | 119 案（含新測試）：118 通過、1 失敗（K-2，KNOWN_MATCH） | `20261014T020232Z-shadow-1-play` |
| 分類 | **VERIFIED_WITH_KNOWN_FAILURES**，ESCAPE 0 | — |
| 影子樣本 | **無效**：計畫後 HEAD 改變 ⇒ 快照**未推進**、不計入 Q5（0／10） | `Logs/test-orchestrator/shadow/20261014T020753Z-…` |

**無效的原因（已查證）**：PlayMode 執行期間（10:06），平行進行的另一個工作提交了 `51aa360 docs(59)`。計畫時與比對時的工作樹**除了 HEAD 以外位元組完全相同**；該 commit 只含 `docs/`（Unity 看不到）。規則（§12.3：「同 HEAD 且工作樹位元組相同」）照字面判定無效——這是保守方向，判定本身正確。

**提案（❓ 待你裁決，未實作）**：平行工作頻繁提交文件時，照現行規則大多數樣本都會無效，Q5 可能長期無法累積。可選：
①維持現行（最嚴格；樣本要在沒有其他提交的時段取得）；
②放寬為「計畫到比對之間**所有**內容有變的路徑，依 `test-map.json` 都是 `UNITY_INVISIBLE`（或純 Skill 檔），且 Unity 看得到的內容逐位元組相同」——快照記錄的是**被測的狀態**（計畫時的 HEAD＋工作樹），不是比對當下；任何一個路徑無法判定 ⇒ 仍無效；
③同②，但只容許「HEAD 前進且新 commit 全是文件」，不容許工作樹變動。
建議 ③（範圍最小、可由 git 確定判定），並配合合成案例與變異測試。
