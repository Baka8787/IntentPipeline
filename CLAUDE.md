# CLAUDE.md

# Unity Character Framework

This repository is a learning-oriented Unity character framework focused on
clean architecture, data-driven design, and zero-GC runtime.

The goal is NOT to ship a game.

The goal is to build a maintainable gameplay framework while documenting every
major architectural decision.

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

1. docs/01-design-doc.md

Purpose

Architecture decisions
Trade-offs
Why the system is designed this way

---

2. docs/02-dev-spec/

Purpose

Technical specifications
API contracts
Pipeline order
Data structures
Runtime rules

---

3. docs/ADR/

Purpose

Architecture Decision Records.

Always follow accepted ADRs.

---

4. CHANGELOG.md

Purpose

Development history
Refactoring rationale
Lessons learned

---

# Core Principles

## Data Driven

Gameplay reads data.

Gameplay does not query other gameplay systems directly.

---

## Single Responsibility

Each module owns exactly one responsibility.

Avoid "God Classes".

---

## Dependency Direction

Allowed

Input
    ↓

Pipeline
    ↓

RuntimeData
    ↓

StateMachine
    ↓

Animation
    ↓

Motion

Forbidden

Animation -> StateMachine

Motion -> Input

State -> Controller

Controller -> Animation API

---

## Zero GC Runtime

Runtime gameplay should avoid heap allocations.

Prefer:

struct

readonly struct

ref struct

Span

Avoid

new

LINQ

boxing

string interpolation inside Update

unless explicitly approved.

---

## Respect Ownership

Each RuntimeData field has

Owner

Writer

Readers

Do NOT introduce additional writers.

---

# AI Coding Rules

Before changing code:

1.

Understand existing architecture.

Do not rewrite systems because another design looks cleaner.

---

2.

Search for existing implementation first.

Avoid duplicate utilities.

---

3.

Preserve module boundaries.

Never bypass AnimationFacade.

Never bypass Pipeline.

Never access RuntimeData arbitrarily.

---

4.

Follow existing naming conventions.

Private fields

_camelCase

Public members

PascalCase

---

5.

If changing architecture,

explain

Problem

Trade-off

Reason

Impact

before modifying.

---

# When Implementing Features

Always think in this order.

1.

Does the design document already define this?

↓

If yes

Follow it.

↓

If no

Propose an ADR.

Do NOT invent architecture.

---

# Documentation Responsibilities

Whenever architecture changes,

update

Design Doc

Spec

ADR

Changelog

if necessary.

Code and documentation should never diverge.

---

# Things AI Should Never Do

Do not

Introduce singleton managers.

Do not

Access Unity API from data classes.

Do not

Put gameplay logic inside Animation.

Do not

Store frame-local data across frames.

Do not

Add public setters to RuntimeData without justification.

Do not

Break module ownership.

---

# Preferred Workflow

Read

↓

Understand

↓

Discuss architecture (if needed)

↓

Implement

↓

Verify

↓

Update documentation

---

# Expected Output Style

When proposing changes:

Explain

Why

Trade-offs

Alternatives

Risks

Avoid giving only code.

Architecture reasoning is more important than implementation.

---

# If Unsure

Do not guess.

Ask for clarification.

Architecture consistency is preferred over implementation speed.

---

# AI Working Mode

Unless explicitly requested,

prefer

- minimal changes
- incremental refactoring
- preserve existing architecture

Avoid large rewrites.

Always explain why a change is necessary.

If a proposal would affect multiple modules,

stop and discuss first.