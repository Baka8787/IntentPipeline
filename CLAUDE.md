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

Code and documentation should never diverge.

## Living Documents vs Immutable Log

- **Design Doc (`docs/01-design-doc.md`) and Dev Spec (`docs/02-dev-spec.md`) are Living Documents**: they describe the CURRENT architecture and API, and must be refactored in sync with the code. When code changes, update them so they never diverge.
- **ADRs (`docs/ADR/`) are an Immutable Log**: once an ADR is Accepted, its decision content is frozen — do NOT rewrite it. To change a decision, open a NEW ADR that supersedes the old one (cross-link Supersedes / Superseded-by). (Purely mechanical maintenance, e.g. fixing a cross-reference path after a file move, is not a decision change and is allowed.)
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
Discuss Architecture & Specs (Mandatory before making any file changes)
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

If Gate A holds but Gate B fails, still prefer the documented process. This rule crystallizes the **B11** decision (Locomotion Mixer threshold automation): the threshold count is tiny (Gate A weak) and writing it would touch Animancer's internal `_Thresholds` serialization (Gate B fails), so the formula stays documented (`threshold = speed_i / speed_max`) and designers fill it by hand. A threshold is a tunable presentation parameter, not a value Bake Data must uniquely dictate. Re-evaluate only if the count grows sharply (Strafe 2D, multiple locomotion sets).

Unless explicitly requested, prefer:
- Minimal changes
- Incremental refactoring
- Preserve existing architecture

Avoid large rewrites. Always explain why a change is necessary. If a proposal would affect multiple modules, stop and discuss first.