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

Read these documents before making changes.

1. `docs/01-design-doc.md`
   - Purpose: Architecture decisions, Trade-offs, Why the system is designed this way.

2. `docs/02-dev-spec.md`
   - Purpose: Technical specifications, API contracts, Pipeline order, Data structures, Runtime rules.

3. `docs/ADR/`
   - Purpose: Architecture Decision Records. Always follow accepted ADRs.

4. `docs/changelog.md`
   - Purpose: Development history, Refactoring rationale, Lessons learned.

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
- Always reference FBX sub-clips directly (TransitionAssets, MotionBakeData.SourceClip, everything). Never duplicate an AnimationClip (Ctrl+D extraction) as part of the normal workflow.
- Ordinary adjustments — tuning values, Mixers, Transitions, playback speed, MotionDriver settings — belong to the Data / Presentation layer (TransitionAsset, Mixer, MotionDriver, ModelImporter settings). They NEVER justify creating a copied clip.
- If data and animation presentation disagree, fix it in the Data / Presentation layer first. Do NOT create a new clip to paper over the mismatch.
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

Unless explicitly requested, prefer:
- Minimal changes
- Incremental refactoring
- Preserve existing architecture

Avoid large rewrites. Always explain why a change is necessary. If a proposal would affect multiple modules, stop and discuss first.