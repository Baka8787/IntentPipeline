# CLAUDE.md

# Unity Character Framework

This repository is a learning-oriented Unity character framework focused on clean architecture, data-driven design, and zero-GC runtime.

The goal is NOT to ship a game.
The goal is to build a maintainable gameplay framework while documenting every major architectural decision.

---

# Project Philosophy

Priority:
1. Architecture
2. Readability
3. Maintainability
4. Performance
5. Features

Never sacrifice architecture simply to make something work.

---

# Source of Truth

Read these documents before making changes. **Start with `docs/00-map.md`** — it is the single-page index (module → file → governing section) and tells you which of the following to open, so you do not have to scan for it.

0. `docs/00-map.md`
   - Purpose: Navigation map. Read first; it is designed to make the rest of this list cheap to use.

1. `docs/01-design-doc.md`
   - Purpose: Architecture decisions, Trade-offs, Why the system is designed this way.

2. `docs/02-dev-spec.md`
   - Purpose: **Cross-cutting contracts only** — naming/structure (§0), blackboard schema (§1), pipeline order (§2), core driving interfaces (§3.1), State Matrix (§3.3), architecture regression checklist (§7).
   - Subsystem detail lives in its own file: `docs/05-foot-ik.md`, `docs/06-animation-presentation.md`, …

3. `docs/ADR/`
   - Purpose: Architecture Decision Records. Always follow accepted ADRs.

4. `docs/changelog.md`
   - Purpose: Development history, Refactoring rationale, Lessons learned. **Split-volume**: this file keeps only the latest ~4 versions; older history is in `docs/changelog-archive.md` (open only for archaeology).

---

# Context Discipline (decided 2026-07-25)

The project is ~10k lines (docs 4k / code 6k) with deliberately high comment density. Reading whole files is now the dominant context cost — one feature-sized task was measured at **~23% of the entire project read**, with a **5×–40× read amplification** over what was actually needed. These rules exist to cut that amplification, not to discourage reading.

**Reading protocol**
- **Session start**: read `WORKLOG.md`'s top 「🔖 交辦」 section and the relevant ADR. That is normally enough to begin. Do NOT pre-read design-doc / dev-spec / changelog "to get oriented" — `docs/00-map.md` tells you where things are.
- **Large documents** (`docs/02-dev-spec.md`, `docs/01-design-doc.md`, any 300+ line file): locate first, read second — `grep -n "^#"` for the heading map, then `Read` with `offset`/`limit` on the section you need. Never read a large doc end-to-end unless the task genuinely spans it.
- **Code**: prefer `Grep` for a symbol over `Read` of the file that contains it. When you do read, read the region, not the file.
- **Never re-read** a file already summarized in the current conversation.
- Changelog is history: consult it for version conventions or past rationale, not for current state. Current state is in the Living Docs.

**Test-as-Spec principle**
Architecture invariants are codified in `Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs`. When you need to know **who may write a blackboard field**, read `WriterRules` (~15 lines) — not `CharacterPipelineRunner` + `MotionDriver` + `PlayerRuntimeData` (~600 lines). Same for forbidden cross-layer dependencies (`LayerRules`). The test file is the cheapest accurate summary of the architecture that exists; it cannot drift, because drift makes it fail.
Corollary: when adding an invariant, prefer expressing it as a test over prose — you get enforcement and a cheap summary from the same artifact.

**Explore subagent (authorized)**
Broad fan-out searches — "where is X", "which files touch Y", "does anything else do Z" — are authorized to run in a read-only `Explore` subagent, which burns its own context and returns only conclusions. Use it when the answer requires sweeping many files and you need the conclusion, not the contents. Do NOT use it for targeted reads you already know the location of (a direct `Read` is cheaper), and do NOT use it to make decisions — it locates, it does not judge.

---

# Project Structure (Canonical — decided 2026-07-14)

The flat layout directly under `Assets/` is the FINAL, canonical structure:

- Runtime code: `Assets/Scripts/Core/`, `Assets/Scripts/Presentation/` (asmdef: `Project.Runtime`)
- Editor tooling: `Assets/Scripts/Editor/` (asmdef: `Project.Editor`)
- Tests: `Assets/_Project/Tests/EditMode/` (asmdef: `Project.Tests.EditMode`)
- Config assets: `Assets/ScriptableObjects/` (Motion / StateMachine)

Do NOT migrate scripts or assets into `Assets/_Project/`. The early plan to consolidate everything under `_Project/` is retired (migration risks GUID/.meta breakage for zero architectural gain). Full directory skeleton: `docs/02-dev-spec.md` §0.2.

---

# Core Principles

## Data Driven
Gameplay reads data. Gameplay does not query other gameplay systems directly.

## Single Responsibility
Each module owns exactly one responsibility. Avoid "God Classes".

## Dependency Direction
Allowed:
Input → Pipeline → RuntimeData → StateMachine → Animation → Motion

Forbidden:
- Animation -> StateMachine
- Motion -> Input
- State -> Controller
- Controller -> Animation API

## Zero GC Runtime
Runtime gameplay should avoid heap allocations.
Prefer: `struct`, `readonly struct`, `ref struct`, `Span`
Avoid: `new`, `LINQ`, `boxing`, string interpolation inside `Update` (unless explicitly approved).

## Respect Ownership
Each RuntimeData field has an Owner, Writer, and Readers. Do NOT introduce additional writers.

## Animation Assets: Immutable by Default (decided 2026-07-17)
**AnimationClip is immutable by default. The FBX sub-clip is the single source of truth.**
**Roles: an AnimationClip is a Presentation Resource; MotionBakeData is the source of truth for the animation's real motion values (displacement, speed, gravity, foot phase).** Read those numbers from Bake Data — do not hand-copy them into configs and do not bake gameplay constants into clips.
- Always reference FBX sub-clips directly (TransitionAssets, MotionBakeData.SourceClip, everything). Never duplicate an AnimationClip (Ctrl+D extraction) as part of the normal workflow.
- Ordinary adjustments — tuning values, Mixers, Transitions, playback speed, MotionDriver settings — belong to the Data / Presentation layer (TransitionAsset, Mixer, MotionDriver, ModelImporter settings). They NEVER justify creating a copied clip.
- **When a data change causes an animation-presentation problem, escalate in this fixed order — never jump straight to editing a clip:**
  1. Adjust **Data / Runtime parameters** (MotionDriver speed, Mixer thresholds, Bake-derived config).
  2. Adjust the **Presentation layer** (Mixer, Transition, playback Speed, Blend).
  3. **Swap** the AnimationClip (reference a different FBX sub-clip).
  4. Only as a last resort, **modify AnimationClip content** — and only when the content itself must change.
- Creating a standalone AnimationClip is allowed ONLY when the animation content itself must change (Animation Events, extra curves, keyframe edits, special variants unachievable via import settings) — and the reason MUST be documented.

---

# AI Coding Rules

Before changing code:
1. Understand existing architecture. Do not rewrite systems because another design looks cleaner.
2. Search for existing implementation first. Avoid duplicate utilities.
3. Preserve module boundaries. Never bypass `AnimationFacade`, `Pipeline`, or access `RuntimeData` arbitrarily.
4. Follow existing naming conventions:
   - Private fields: `_camelCase`
   - `[SerializeField]` private fields: `camelCase` — explicitly EXEMPT from the underscore rule (decided 2026-07-14; renaming would break Inspector serialization and force `FormerlySerializedAs` clutter)
   - Public members: `PascalCase`
5. If changing architecture, explain the **Problem**, **Trade-off**, **Reason**, and **Impact** before modifying.

---

# When Implementing Features

Always think in this order:
Does the design document already define this?
- ↓ If yes: Follow it.
- ↓ If no: Propose an ADR. Do NOT invent architecture.

---

# Documentation Responsibilities

Whenever architecture changes, update:
- Design Doc
- Spec
- ADR
- Changelog (if necessary)

## Documents Live in the Repo, Not in a Session（decided 2026-08-31，使用者明確裁決）

**任何需要長期保留的產出——技術文件、架構圖、研究筆記、HTML artifact——一律先寫進 repo，再談發布。**

| 規則 | 內容 |
|---|---|
| **1. 原始檔存 repo** | 原始檔寫入 `docs/`。HTML／圖解類放 `docs/artifacts/`（不佔 `docs/NN-*` 的工程編號序列）；純文字規格仍走既有的 `docs/NN-*.md` 分卷規則 |
| **2. repo 版＝ source of truth** | `docs/` 內的版本是唯一真相 |
| **3. Artifact 只是發布版** | Claude Artifact 僅作為方便閱讀／分享的副本，**不是**原始檔 |
| **4. 更新必須雙向同步** | 更新 Artifact 時**同步更新 `docs/` 內的原始檔**；反之亦然。以同一個檔案路徑重新發布可保留原 URL |
| **5. ⛔ 禁止只留在 session 目錄** | **不得**把需要長期保留的文件只留在 `%TEMP%`／scratchpad／worktree 等 session-specific 位置。scratchpad 只放用完即丟的中間產物 |
| **6. 本類寫入已獲授權** | 使用者**已明確批准**為此類文件寫入 `docs/`，不需要每次再問。（仍不得碰 `.asset`／`.prefab`／`.meta`／場景，Git 仍全由使用者執行） |

> **為什麼**：session scratchpad 會隨會話消失，Artifact 連結在 repo 之外——兩者都不是可以被 `git log` 追溯、可以被下一個會話讀到的地方。文件的價值來自「未來的人找得到它」，而不是「現在看得到它」。
> **落地**：新增的文件要在 `docs/00-map.md` 留一行指標，否則等同不存在。

## ADR Lifecycle：Proposed → Trial → Accepted（decided 2026-08-29）

| Status | 意義 | 可否作為實作基線 | 可否修改 |
|---|---|---|---|
| **Proposed** | 已寫下、尚未裁決 | ❌ **不得**作為實作基線 | 自由修改 |
| **Trial** | 已裁決為**目前的實作基線**，但**尚未由第一個 vertical slice 驗證** | ✅ 是 | ✅ **可依實作發現修訂**（記入該 ADR 的修訂紀錄，**不必開新 ADR**） |
| **Accepted** | 已經實作 ＋ Play ＋ Test 驗證通過 | ✅ 是 | ❌ **凍結**（進入 Immutable Log） |

- 🎯 **`Trial` 只適用於「會動到跨系統契約」的 ADR**（decided 2026-08-29）。判準：①黑板 schema ／ ownership 變更；②FSM 拓撲或 GameObject hierarchy 變更；③管線順序或核心驅動介面（`IInputSource`／`IMovementIntentSource`／`IMovementModel`／`AnimationFacadeBase`／`IPresentationController`／`IArbiterSource`）變更；④推翻既有架構不變量。**任一成立才需要 ADR，因而才需要 Trial。**
  - 其餘一律走既有 routing rule **直接寫 Living Docs**（`docs/01`／`docs/02`／子系統分卷），**不開 ADR、也不進 Trial**。為單一子系統的加法套上 Trial 的三段開場（Acceptance Criteria／失敗處置／不凍結清單）只是多一層儀式，沒有換到任何保護——先例：Phase C1 Forward Stop（`docs/07`）與 Footstep 都是這樣落地的，兩者都沒有開 ADR。
- **只有 `Accepted` 的 decision content 凍結；`Trial` 不凍結。**
- **Trial 期間實作暴露問題時：先修 Trial ADR ／ Living Spec → 再驗證。不得為了維護舊文字而在程式裡補 workaround。**
- Trial ADR **必須自帶 Acceptance Criteria**（通過條件）與**失敗處置**（含 revert 路徑）。
- Trial ADR **應明列「哪些內容不凍結」**——ADR 只保留「改錯會造成架構污染」的決策，實作細節（欄位組成、計時方式、冷卻細節等）一律下放 Living Spec。範本：`docs/ADR/004-action-in-fsm.md` §9。
- 引用 Trial 文件時**必須註明其狀態**：**使用者已裁決 ≠ 工程上已驗證。**
- 流程為 `Design → Trial → Implement → Observe → Revise → Accept`，**取代**舊的 `Design → Freeze → Implement`。

## Code / Documentation Fold-back（decided 2026-08-29，取代「never diverge」）

- **`Accepted` contract 不得與程式長期分歧**——Living Docs 描述的是當前架構，必須與程式一致。
- **Trial ／ Spike 期間允許短暫 code-first**：那正是驗證的一部分，不是失誤。
- **但同一工作包結束、交付使用者驗收之前，Living Docs ／ `WORKLOG.md` 必須 fold back 到實際程式狀態。**
- **不得把 Trial 中尚未實證的內容寫成已完成事實**——狀態欄、勾選框、changelog 條目皆適用。

## Architecture Invariants Track the Effective Baseline（decided 2026-08-29）

`Assets/_Project/Tests/EditMode/ArchitectureRegressionTests.cs` 驗證的是**「目前有效的 architecture baseline」**。
該 baseline 可以來自 **Accepted ADR**，**也可以來自已正式進入 Trial 的 ADR**。

- Trial 取代舊 invariant 時，**不是「暫停」舊 invariant，而是舊 invariant 已被新 baseline 正式取代**。**同一工作包內**把測試更新成新 baseline。
- ⛔ **不建立 generic 的 test suspension／disable 機制**——那會讓架構測試退化成「擋到就繞過」的軟閘門，等於廢掉它存在的理由。
- Trial **失敗** → **code ／ ADR ／ invariant 一起 revert**。Trial **通過** → 測試本身直接成為 Accepted baseline，**不需要恢復舊的**。
- **允許 agent 交接過程短暫紅燈**（不需要為了讓每個中間 commit 全綠而增加 transitional bypass）；**但交付使用者驗收時必須全綠。**

## Living Documents vs Immutable Log

- **Design Doc (`docs/01-design-doc.md`) and Dev Spec (`docs/02-dev-spec.md`) are Living Documents**: they describe the CURRENT architecture and API, and must be refactored in sync with the code — subject to the Fold-back rule above.
- **ADRs (`docs/ADR/`) are an Immutable Log — but only from `Accepted` onward**: once an ADR reaches **Accepted**, its decision content is frozen — do NOT rewrite it. To change a decision, open a NEW ADR that supersedes the old one (cross-link Supersedes / Superseded-by). (Purely mechanical maintenance, e.g. fixing a cross-reference path after a file move, is not a decision change and is allowed.) **A `Trial` ADR is explicitly NOT frozen** — see the ADR Lifecycle table above.
- **Where a change goes (routing rule)**:
  - *Non-architectural new feature / new state* (does not change the architecture) → write it directly into the Living Documents (`docs/01-design-doc.md` / `docs/02-dev-spec.md`). Do **NOT** open a new ADR, and do **NOT** modify any existing (frozen) ADR.
  - *Truly disruptive architectural change* (ownership shifts, hierarchy changes, cross-cutting breaks) → open a **NEW** ADR following the Supersede principle; the old ADR file stays frozen.
- **Documentation truth flows one way**: ADR (historical decision snapshot) → Design Doc (current architecture) → Dev Spec (implementation API).
- **Subsystem specs get their own file (size-driven split, decided 2026-07-21)**: when a subsystem's detailed spec would keep bloating `docs/02-dev-spec.md` (its §3 Presentation is already ~half the file and grows with every new subsystem), give the subsystem its own `docs/NN-<subsystem>.md` (precedent: `docs/04-locomotion-foundation.md`). `docs/02-dev-spec.md` then holds **cross-cutting contracts only** — §0 naming/file structure, §1 blackboard schema, §2 pipeline order, §3.1 core driving interfaces, §3.3 State Matrix. These subsystem files sit in the **Dev Spec (implementation API) tier** of the truth-flow above, for their subsystem. ~~**Apply going forward; do NOT retroactively split existing sections** (cross-reference-breakage risk > gain)~~ — **AMENDED 2026-07-25, see below.** The next subsystem's durable spec goes to its own file rather than into dev-spec §3.

> **Amendment (2026-07-25) — retroactive splitting is now allowed for frozen/stable subsystems.**
> **Why the reversal**: the original rule weighed "cross-reference-breakage risk > gain" — but *context exhaustion* was not yet an input. It is now (see Context Discipline above: measured 5×–40× read amplification, `docs/02-dev-spec.md` at 1,169 lines being the single largest cost). The gain side of the trade-off changed; the decision follows.
> **The breakage risk turned out to be avoidable**: split by *moving text verbatim and keeping the original section numbers/titles inside the new file*, leaving a stub at the original location with a summary + link. Existing references (`dev-spec §3.5.2`) then resolve in the new file by the same number. Executed this way for §3.5 → `docs/05-foot-ik.md` and §3.2's animation sections → `docs/06-animation-presentation.md` with zero reference rewrites.
> **Eligibility (all three)**: (a) the subsystem is **frozen or stable** — do not split something under active redesign, you will pay the move twice; (b) it is **not a cross-cutting contract** (§0/§1/§2/§3.1/§3.3 and §7 always stay in dev-spec); (c) the move is **verbatim, numbering preserved, stub left behind**.
> This amendment does NOT license reorganizing docs for tidiness. Splitting is a response to a measured cost, not an aesthetic preference — the "don't preempt, let scope decide" spirit is unchanged. This is the inverse of the ADR anti-explosion rule: there we avoid too many tiny files, here we avoid one unboundedly-growing file. Split only when a subsystem is genuinely big enough to warrant it (same "let scope/assets decide, don't preempt" spirit as the Locomotion speed-tier decision), never preemptively.

---

# Things AI Should Never Do

- Do NOT introduce singleton managers.
- Do NOT access Unity API from data classes.
- Do NOT put gameplay logic inside Animation.
- Do NOT store frame-local data across frames.
- Do NOT add public setters to RuntimeData without justification.
- Do NOT break module ownership.

---

# Preferred Workflow

Read Docs & Code
↓
Discuss Architecture & Specs (Mandatory before architectural file changes)
  └─ **Exception (2026-08-29)**: trivial / local changes — typo, comment, tooltip, a single
     tunable value, test-only edit, or anything confined to one file with no contract impact —
     do NOT require a full architecture discussion. Just make them and say what you did.
↓
Modify Files (Write changes directly to the local working tree)
↓
Stop (Do NOT perform any Git operations)

---

# Git Policy & Permissions (Solo Developer Mode)

Claude is NOT allowed to execute any Git mutation commands. The human developer owns 100% of the Git lifecycle.

## Strictly Forbidden Commands:
- `git checkout` / `git switch`
- `git branch`
- `git commit`
- `git merge` / `git rebase`
- `git push` / `git pull`
- `git stash`

## Working Rules:
- **Local Only**: Assume the current checked-out branch is correct and the local working tree is the only target.
- **File Changes Only**: Only edit, create, or delete physical files using file-system tools (e.g., `write_file`, `edit_file_multi`).
- **No PRs**: Never attempt to interact with the GitHub API to create Pull Requests or remote branches.
- **Stop After Edit**: Once files are modified, stop immediately. Leave verification, compilation checks, and Git commits to the human developer in the Unity Editor / Terminal.

## ⚠️ Remote Container Exception（decided 2026-09-02，使用者明確裁決）

> **本節只適用於「工作樹不是使用者本機」的遠端 session**（Claude Code on the web ／ 容器化 session）。
> **在使用者本機執行時，上方禁令一字不變、完全適用。**

**為什麼需要例外**：上方 Solo Developer Mode 的前提是「本機工作樹持久存在，使用者稍後會在 Terminal 接手」。
遠端容器**沒有這個前提**——容器回收後未 commit 的檔案直接消失，`git log` 追不到、下一個會話讀不到。
這與「Documents Live in the Repo, Not in a Session」是同一個問題的兩面：**只存在於 session 的東西等於不存在。**

| | 內容 |
|---|---|
| **✅ 允許** | 遠端 session 中，**純文件變更**可由 Claude `git add` ／ `git commit` ／ `git push -u origin <指定分支>` |
| **純文件的定義** | 變更集**只含** `docs/**`、`WORKLOG.md`、`*.md`、`LearningNotes/**` |
| **⛔ 一票否決** | 變更集若出現 `.cs`／`.asset`／`.prefab`／`.meta`／`.unity`／`.inputactions`／任何動畫或美術資產（`.fbx`／`.anim`／`.mat`／`.controller`）**任一項**，**整批退回上方禁令**，一律由使用者執行 |
| **強制前置檢查** | commit 前**必須**跑 `git status --porcelain` 並逐條確認副檔名，**檢查結果要報給使用者**。⛔ 不得憑印象判斷「應該只有文件吧」 |
| **分支** | 只准 push 到 session 指定的分支。⛔ 不得 `checkout`／`switch`／`branch`／`merge`／`rebase`／`stash`／force-push |
| **PR** | **仍然禁止**——除非使用者明確要求，否則不開 PR（上方 No PRs 規則在此不放寬） |

**為什麼把界線畫在「純文件」**：文件變更是加法、可讀、衝突易解，且它的價值完全來自「被 commit 進 repo」。
程式與 Unity 資產則相反——`.meta`／GUID／serialization 的破壞在 review 階段極難察覺，
而 Unity Editor 的編譯與 Play 驗證只有使用者做得到。**這條界線分的不是信任，是「錯了誰救得回來」。**

---

# Expected Output Style

When proposing changes:
Explain: Why, Trade-offs, Alternatives, Risks.
Avoid giving only code. Architecture reasoning is more important than implementation.

---

# If Unsure

Do not guess. Ask for clarification. Architecture consistency is preferred over implementation speed.

---

# AI Working Mode

## Document Consolidation Policy (Preventing ADR Explosion)
- **Do NOT create a new ADR for every feature.** Keep the total number of ADR files minimal.
- **Route non-architectural work to Living Docs (not ADRs)**: If a new feature or state shares an existing architectural pattern (e.g., dynamic movement states expanding on ADR-002) and does not change the architecture, write it into the Living Documents (`docs/01-design-doc.md` / `docs/02-dev-spec.md`). Do NOT generate a new ADR, and do NOT modify the existing (frozen) ADR.
- **Distinguish ADR from Spec**: Only propose a new ADR for systemic, cross-cutting breaking changes (e.g., ownership shifts, hierarchy changes). For incremental API or data layout changes, update `docs/02-dev-spec.md` or `docs/01-design-doc.md` directly.

## Editor Tool vs Documented Process (decided 2026-07-17)
Default to a **documented process / SOP** over building a new Editor Tool. Only build a tool when it clears BOTH gates:
- **Gate A — the investment is justified (at least ONE of):** high-frequency repetition (e.g. several times a week); human operation is error-prone (silent mistakes, easy to skip a step); it removes large amounts of repetitive input or ongoing maintenance cost.
- **Gate B — it does NOT depend on a third party's internal serialization or private structure** (keeps us insulated from third-party upgrade breakage).

⚠️ **兩道閘門只適用於「會留下來的工具」**；用完即丟的探針見下方 Spike / Probe Exception。

If Gate A holds but Gate B fails, still prefer the documented process. This rule crystallizes the **B11** decision (Locomotion Mixer threshold automation): the threshold count is tiny (Gate A weak) and writing it would touch Animancer's internal `_Thresholds` serialization (Gate B fails), so the formula stays documented (`threshold = speed_i / speed_max`) and designers fill it by hand. A threshold is a tunable presentation parameter, not a value Bake Data must uniquely dictate. Re-evaluate only if the count grows sharply (Strafe 2D, multiple locomotion sets).

## Spike / Probe Exception（decided 2026-08-29）

**用完即丟的 spike／probe——為了回答一個問題而寫、答案拿到就刪——不算「提前建 framework」，也不需要通過 Editor Tool 的兩道閘門。**

- 但它**必須明確不進 production path**：不被 runtime 程式引用、不當成功能交付、問題回答後即刪除或明確標記為 throwaway。
- 維持有效的禁令是：**第二個使用者出現前不得建立 production abstraction**。**用完即丟的實驗探針不是 abstraction，不在禁令範圍內。**

Unless explicitly requested, prefer:
- Minimal changes
- Incremental refactoring
- Preserve existing architecture

Avoid large rewrites. Always explain why a change is necessary. If a proposal would affect multiple modules, stop and discuss first.