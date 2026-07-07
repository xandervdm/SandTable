# SandTable Codex Prompt: Add Strategic Tension / Operational Opportunities

We have completed and merged the first backend vertical slice for
**SandTable**.

Now add a new engine-level feature called **Strategic Tension Points**,
also referred to in-game as **Operational Opportunities**.

This system should create meaningful turn-based dilemmas that make each
campaign more compelling. The goal is not to add complexity, but to
ensure each turn can present difficult strategic trade-offs.

## Core Design Goal

Each campaign turn should sometimes present the player with 1--2
meaningful dilemmas.

A good dilemma has **no obviously correct answer**.

Example:

``` text
Overextended Armour

Your armour has outrun its supply line.

Option A: Push Forward
- Gain +1 movement this turn
- Risk morale loss if attack fails

Option B: Consolidate
- Restore supply
- Cannot attack this turn
```

The system must live primarily in `SandTable.Engine`.

Do not implement this as frontend-only flavour.

------------------------------------------------------------------------

## Engine Concepts

Implement:

-   StrategicTensionCard
-   TensionOption
-   TensionCategory
-   TensionTrigger
-   TensionDecision

## Effect System

Create a lightweight reusable effect system.

Suggested effects:

-   AddResourceEffect
-   AddCampaignModifierEffect
-   ModifyUnitStatEffect
-   ModifyRegionEffect
-   AddGameEventEffect

Keep it simple and deterministic.

## GameState

Extend GameState with:

-   ActiveTensions
-   TensionHistory

Preserve JSON compatibility.

## Commands

Introduce:

-   ChooseTensionOptionCommand

Validation must:

1.  Validate card exists.
2.  Validate option exists.
3.  Apply effects.
4.  Record decision.
5.  Remove active card.
6.  Emit readable game events.

## Generator

Create:

-   ITensionGenerator
-   BasicTensionGenerator

Rules:

-   Deterministic
-   Uses existing seed
-   Maximum two cards
-   No duplicates

## Initial V1 Cards

1.  Overextended Armour
2.  Hold The Line
3.  Sandstorm Forecast
4.  Political Pressure
5.  Enemy Fuel Convoy Spotted

Each card must provide two meaningful trade-offs with no obviously
correct answer.

## Effect Application

Create:

-   IGameEffectApplier
-   GameEffectApplier

## Turn Lifecycle

1.  Resolve player commands
2.  Resolve AI
3.  Resolve combat
4.  Resolve supply
5.  Resolve events
6.  Check victory
7.  Generate new strategic tensions

## API

Expose active tensions via campaign state.

If required add:

`POST /api/campaigns/{id}/tensions/{cardId}/choose`

Otherwise integrate with the existing command endpoint.

Keep business logic inside the engine.

## Persistence

Snapshots must preserve:

-   Active tensions
-   Tension history
-   Campaign modifiers

## Tests

Cover:

-   Card generation
-   Duplicate prevention
-   Maximum cards
-   Valid choice
-   Invalid choice
-   Deterministic effect application
-   JSON serialization
-   Turn integration

## Frontend

Add only a simple placeholder UI showing:

-   Title
-   Description
-   Option A
-   Option B

## Documentation

Create:

`docs/strategic-tension.md`

Include:

-   Purpose
-   Lifecycle
-   Data model
-   Adding new cards
-   V1 cards
-   Design rule:

> No tension card should ever have an obviously correct option.

## Constraints

-   Keep implementation small.
-   Keep the engine pure.
-   No LLMs.
-   No external rules engines.
-   Preserve current architecture.
-   Deterministic behaviour.

## Definition of Done

-   Active tensions supported.
-   Choices applied.
-   Effects recorded.
-   Turn resolver generates tensions.
-   Snapshots persist correctly.
-   Five initial cards implemented.
-   Tests passing.
-   API updated.
-   Documentation complete.
