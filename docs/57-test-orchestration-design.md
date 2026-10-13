# 57 — Unity 測試調度（`unity-test-orchestrator`）：現況盤點與設計（2026-10-13，Research + Design）

| | |
|---|---|
| **狀態** | 🟠 **Proposed——未裁決、未實作**。本輪 ⛔ 沒有改任何測試、安全腳本、CI、Skill 檔 |
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
