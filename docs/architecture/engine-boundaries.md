# Engine Boundaries

`SandTable.Engine` is the deterministic game rules core.

The current implementation remains the V1 model until the coordinated Phase 3 reset. The reviewed breaking target, including per-side resources, typed commands, reserves, victory progress, and scenario-event history, is [`development-baseline-v2.md`](development-baseline-v2.md).

## Allowed

The Engine may contain:

- immutable game/content/state models
- scenario validation
- AI planning
- turn resolution
- strategic tension generation
- tension choice resolution
- game effect application
- pure deterministic helper logic

The Engine should accept data that has already been loaded by an outer layer.

## Not Allowed

The Engine must not reference:

- ASP.NET Core
- HTTP abstractions
- EF Core
- Dapper
- Npgsql
- database connection strings
- filesystem APIs for loading game content
- environment variables
- wall-clock time
- hidden randomness

Randomness must come from explicit seeds passed into Engine methods.

## State Rules

Engine methods should return new state rather than mutating hidden state.

`GameState` is the serialized campaign state and currently includes:

- campaign identity fields
- turn number and campaign date
- player/enemy sides
- resources
- region state
- unit state
- completion/result state
- active strategic tensions
- tension decision history
- campaign modifiers

## Turn Resolution

Turn resolution is simultaneous.

Player and AI commands must be planned from the same starting state. Resolution may process commands in a deterministic order, but planning must not depend on partial results from the other side.

## Strategic Tension

Strategic Tension Points are Engine state.

The API may load `tension-cards.json` and pass the catalog into the Engine, but card generation, option validation, effect application, and decision recording belong in `SandTable.Engine`.
