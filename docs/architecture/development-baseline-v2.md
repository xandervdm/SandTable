# SandTable Development Baseline V2

## Status

This document is the reviewed Phase 0 contract for the next SandTable development baseline.

Implementation status: **Active since the verified Phase 3 development reset on 11 July 2026.** The canonical package, Engine state, typed command payload, Dapper persistence, and reviewed SQL shapes below are now the only supported development contracts.

- Content contract: `sandtable-content-v2`
- Engine and snapshot contract: `sandtable-engine-v2`
- Compatibility: development reset only; V1 content, snapshots, commands, and API payloads will not be read after the coordinated Phase 3 reset
- Implementation order: Phase 2 establishes the package and validation contract; Phase 3 introduces the breaking Engine, API, and SQL shapes; later phases activate the documented mechanics

This document freezes the active shapes. The pre-reset V1 content, command, snapshot, and API payloads are intentionally unsupported.

## Architectural Ownership

| Concern | Owner |
| --- | --- |
| Deterministic rules, state transitions, command costs, event effects, victory evaluation, AI legality | `SandTable.Engine` |
| Content discovery, JSON loading, validation orchestration, HTTP DTOs, Dapper persistence, asset URL projection | `SandTable.Api` |
| Reviewed application schema and check constraints | SQL under `database/` |
| Theatre rules and presentation metadata | `content/theatres/<theatreId>` |
| React shell and PixiJS presentation | `frontend/` |

The Engine receives already-loaded typed data. It must not load files, inspect environment variables, use wall-clock time, or depend on ASP.NET, Dapper, Npgsql, or hidden randomness.

## Canonical Theatre Package

There is one supported V2 package layout:

```text
content/theatres/<theatreId>/
  theatre.json
  map.json
  map-display.json
  map-assets.json
  units.json
  reserves.json
  doctrines.json
  events.json
  tension-cards.json
  scenarios/
    <scenarioId>.json
  assets/
    map-base.png
    ...
```

No alternate V2 filenames or North Africa aliases are supported. Files are located through `theatre.json`; application code must not derive a special scenario filename.

### Theatre manifest

`theatre.json` contains:

```json
{
  "contractVersion": "sandtable-content-v2",
  "theatreId": "north-africa",
  "name": "North Africa",
  "defaultScenarioId": "north-africa-1942",
  "files": {
    "map": "map.json",
    "display": "map-display.json",
    "assets": "map-assets.json",
    "units": "units.json",
    "reserves": "reserves.json",
    "doctrines": "doctrines.json",
    "events": "events.json",
    "tensionCards": "tension-cards.json"
  },
  "scenarios": [
    { "scenarioId": "north-africa-1942", "file": "scenarios/north-africa-1942.json" }
  ]
}
```

All manifest paths are relative, must remain within the theatre directory, and must resolve to exactly one file.

### Asset authoring and serving

`content/theatres/<theatreId>/assets` is the authoring source of truth. `frontend/public` is generated output, not an authoring location.

`map-assets.json` assigns stable asset IDs to relative files and records origin, generation prompt or source, date when known, licence/attribution, and intended use. `map-display.json` refers to an `assetId`, never a hardcoded public URL.

A repository script run by frontend development/build commands copies declared assets to:

```text
frontend/public/theatres/<theatreId>/assets/<file>
```

The API projects the browser URL as `/theatres/<theatreId>/assets/<file>`. The copy step deletes stale generated files only inside that theatre's generated public directory. Validation runs before copying and fails on undeclared, missing, escaping, or duplicate asset paths.

## Content and Engine Models

### Regions and routes

`RegionDefinition` adds a required `kind` with these V2 values:

- `PrimaryObjective`
- `Objective`
- `OperationalPosition`
- `EntryPoint`

Features such as `Port`, `Airfield`, `City`, `Fortified`, and `SupplyDepot` remain separate from region kind.

Every route has a stable `id`, `fromRegionId`, `toRegionId`, `routeType`, positive `movementCost`, and non-negative `supplyCost`. Adjacency is defined by routes; the duplicate `adjacentRegionIds` collection is removed from region content and derived by the Engine/API. Routes are bidirectional unless an explicit future contract version adds directionality.

`movementCost` spends a unit's movement allowance. `supplyCost` is the cost used by deterministic supply tracing. A unit cannot traverse beyond its allowance, and enemy contact stops movement according to the later Phase 7 resolution rules.

### Side resources and command costs

`GameState` stores resources per playable side rather than one player-only resource object. Scenario `startingResources` therefore contains both `Axis` and `Allies` entries.

Each scenario contains a `commandCosts` object keyed by command type. A cost may define base command points, fixed supplies or fuel, and supplies or fuel per route movement-cost point.

The allowed V2 commands are `Move`, `Attack`, `Support`, `HoldPosition`, `Resupply`, `Recon`, and `Deploy`.

Human and AI commands use the same typed Engine command variants:

| Command | Required payload |
| --- | --- |
| `Move` | `unitId`, `fromRegionId`, ordered `pathRegionIds` |
| `Attack` | `unitId`, `fromRegionId`, ordered `pathRegionIds`, final target implied by the path |
| `Support` | `unitId`, `fromRegionId`, `targetRegionId` |
| `HoldPosition` | `unitId`, `regionId` |
| `Resupply` | `unitId`, `regionId` |
| `Recon` | `unitId`, `fromRegionId`, `targetRegionId` |
| `Deploy` | `reserveId`, `targetRegionId` |

The Engine calculates and records the resolved cost. The API may reject an obviously over-budget human submission early, but the Engine remains authoritative. If a side submits an over-budget set, commands are considered in their explicit sequence and later unaffordable commands are rejected deterministically. Both sides are planned from the same starting state.

### Victory rules

The single `VictoryConditionDefinition` is replaced by an ordered `victoryRules.outcomes` collection. Each outcome has a stable ID, a result from the player's perspective (`Victory`, `Defeat`, or `Draw`), a unique priority, and an `allOf` collection. The first fully satisfied outcome wins; duplicate priority is invalid content.

V2 condition types are:

- `ControlRegion` — side selector and region ID;
- `ControlAtLeast` — side selector, region IDs, and required count;
- `SupplyConnected` — side selector, source region IDs, and destination region IDs;
- `VictoryPointsAtLeast` — side selector and threshold;
- `TurnNumberAtLeast` — turn threshold.

Side selectors are `Player`, `Enemy`, `Axis`, or `Allies`. Any condition may require `consecutiveTurns`; the Engine stores the counters needed to evaluate that requirement in `GameState.VictoryProgress`. Scenarios must define achievable outcomes for both sides and an explicit turn-limit result.

### Reserves and deployment

`units.json` is the catalogue of every unit template, including units that do not start on the map. Starting units are selected by scenario `startingUnitIds`.

`reserves.json` contains stable reserve entries with `reserveId`, referenced `unitId`, side, availability turn, one-time resource cost, eligible region IDs, optional required region features, and optional scenario IDs.

Each scenario identifies its `reserveIds` and `deploymentLimitPerSidePerTurn`. A reserve can be deployed once. The target must be eligible, controlled by the deploying side, and satisfy its required features. Deployment uses the normal `Deploy` command and is resolved, persisted, and emitted as an event through the same human/AI pipeline.

`GameState.Reserves` records each reserve's status (`Unavailable`, `Available`, `Deployed`, or `Removed`), deployment turn, and deployed unit ID when applicable.

### Scenario events

`events.json` contains ordered, stable event definitions. An event has an ID, a deterministic trigger, optional conditions, an ordered effects collection, and display text.

V2 trigger phases are `BeforeResolution` and `AfterResolution`, with a required turn number. Events with the same phase and turn execute in file order; duplicate IDs are invalid. Event effects use the same typed effect catalogue as strategic tensions, extended with reserve availability/deployment effects where required. Direct mutation through an API-only special case is not allowed.

Events are evaluated from explicit state/content/seed inputs, recorded in `GameState.ScenarioEventHistory`, and emitted as persisted `Scenario` events. A referenced unit, reserve, region, selector, effect, or scenario must pass package validation before campaign creation.

## API Contract

### Campaign creation

`CreateCampaignRequest` requires:

```json
{
  "name": "North Africa Campaign",
  "theatreId": "north-africa",
  "scenarioId": "north-africa-1942",
  "playerSide": "Axis",
  "randomSeed": 12345
}
```

`theatreId`, `scenarioId`, and `playerSide` have no application defaults. `randomSeed` may be omitted only when the API generates it and persists the generated value before Engine use.

Content endpoints are manifest-driven. Scenario content returns theatre metadata, map, display metadata, projected asset URLs, scenario, unit/reserve catalogues, doctrines, events, and tension cards without North Africa-specific application code.

### Commands

The command submission request contains the common sequence and a discriminated command payload matching the typed variants above. The API does not accept V1's flat nullable command shape after the Phase 3 reset.

### Timeline projection

`GET /api/campaigns/{campaignUid}/timeline` is derived from `Initial` and `TurnResolved` snapshots in ascending turn order. No health-history table is added.

Each timeline point contains snapshot UID, turn number, campaign date, and these per-side metrics:

- surviving and maximum strength;
- `forceStrengthPercent`, calculated as `100 * sum(current strength) / sum(max strength)` across all scenario units for that side, including destroyed units as zero current strength;
- active and destroyed unit counts;
- controlled victory points;
- average supply and morale for active units;
- persisted casualty, objective, deployment, tension, and victory markers for that turn.

The API returns explicit metrics and does not invent a composite `Health` score.

## Persistence Contract

PostgreSQL continues to store the authoritative serialized `GameState` in `campaign_snapshot.game_state`. The API remains stateless and rehydrates on every request.

Phase 3 updates the reviewed SQL definitions directly:

- add `Deploy` to `campaign_command.command_type`;
- use the V2 Engine event types `Battle`, `Movement`, `Deployment`, `Supply`, `Recon`, `Tension`, `Victory`, `Scenario`, and `System`, adding `Deployment` and `Tension` to the SQL check constraint;
- keep the type-specific command body in `command_payload`, while existing `unit_id` and `region_id` columns remain derived/indexed audit fields;
- add an index supporting ordered `Initial`/`TurnResolved` snapshot projection if query evidence requires it;
- persist `sandtable-engine-v2` for every V2 snapshot.

No new reserve, scenario-event-history, modifier-history, or timeline table is introduced initially. Those are authoritative snapshot state plus commands/events. A later table requires measured query or concurrency evidence.

## Reset and Compatibility Rules

- V2 is a clean development baseline.
- No V1 snapshot reader, JSON alias, duplicate endpoint, dual DTO, or transitional SQL migration is permitted.
- Phase 2 may change package filenames and content JSON directly.
- Phase 3 may change Engine/API/SQL contracts directly and then wipe/recreate only the verified SandTable development database or a disposable test database.
- The running development API need not be stopped for this Phase 0 documentation/version change. It must be restarted before exercising newly built code, and it must be stopped before a verified Phase 3 database reset.

## Phase 0 Exit Gate

Phase 0 is complete when:

- this contract and the aligned theatre-pipeline documentation are reviewed in the repository;
- `docs/game-review-roadmap.md` points to this baseline;
- the Engine-owned current version is `sandtable-engine-v2`;
- the solution build and tests pass without changing the running development database.
