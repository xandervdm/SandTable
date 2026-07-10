# SandTable Game Review and Development Roadmap

## Purpose

This roadmap records the July 2026 review of the SandTable documentation, Engine, API, PostgreSQL model, React frontend, PixiJS renderer, live command-table UI, current North Africa content, and the earlier PixiJS implementation task.

The project is still in development mode. The development database may be wiped and recreated when required. There is no requirement to preserve existing campaign rows, snapshots, command payloads, content JSON shapes, or API compatibility during this work.

That freedom should be used to establish clean contracts early. It does not change these architectural boundaries:

- SQL under `database/` remains the reviewed schema source of truth.
- SandTable application persistence remains Dapper/Npgsql; do not introduce EF migrations for SandTable tables.
- `SandTable.Engine` remains pure and deterministic: no HTTP, ASP.NET, Dapper, Npgsql, filesystem, environment, wall-clock, or hidden randomness dependencies.
- The API remains stateless between requests and persists authoritative JSON snapshots.
- Theatre-specific gameplay and presentation remain content-authored rather than hardcoded into C# or React.
- Human and AI orders are planned from the same starting state and resolved through the Engine.

## Product Direction

SandTable should remain a concise operational command game with a target session length of approximately 20-30 minutes for an experienced player.

The target is not more turns for their own sake. Aim for approximately 8-10 resolved turns with 3-5 meaningful decisions per turn. Depth should come from command capacity, supply, reinforcement timing, contested objectives, enemy activity, and trade-offs rather than busywork.

Keep PixiJS as the theatre renderer and React as the surrounding application shell. Keep the map as a content-authored operational graph for now. Do not migrate to a general hex grid unless later playtesting demonstrates that uniform tactical manoeuvre is essential and cannot be expressed by the graph.

For North Africa, expand the graph with a limited number of meaningful operational positions such as passes, junctions, defensive lines, airfields, supply hubs, and desert approaches. Do not add intermediate positions that merely require extra clicks.

## Review Findings

### Rendering and UI

- PixiJS is viable and its persistent scene-layer structure is the correct direction.
- A fresh browser session can render a dark map because `Sprite.from()` is called before the asynchronous background asset is cached, while the static background key prevents a later rebuild.
- Compact stacks expose only a representative unit; secondary units need an explicit selection surface.
- The command log already receives events containing side, unit, region, and event type, but currently presents only plain summaries.
- At a 1280x720 viewport, top resources can be clipped, the command dock consumes substantial height, and the map is smaller than it should be.
- Region labels and counters need authored placement plus level-of-detail behavior; minor positions should not compete visually with primary objectives.
- Development/runtime information should move out of the core gameplay HUD.
- The obsolete SVG renderer and its CSS remain in `App.tsx` and should be removed after PixiJS regression coverage is in place.

### Game Depth

- Orders do not currently spend command points or the displayed strategic resources.
- Movement is one adjacent edge regardless of the unit's movement statistic.
- `Support`, `Recon`, and `HoldPosition` have little or no meaningful rules effect.
- Campaign modifiers can be recorded without influencing turn resolution.
- Combat resolves commands sequentially and generally selects one defender; it does not yet model a coherent simultaneous regional engagement.
- Victory is effectively immediate control of one target region.
- The basic AI is a deterministic, per-unit nearest-enemy planner. It does not plan around supply, reserves, force preservation, multiple objectives, or coordinated attacks.
- Existing play evidence shows a campaign can finish after roughly seven resolved turns with too few meaningful player decisions and repeated enemy hold actions.

### Observability

- Opponent movement and battle information already exists in persisted events and can be exposed in the UI without an initial database change.
- Historical campaign progress can be projected from `Initial` and `TurnResolved` snapshots.
- Prefer explicit metrics over a vague health score:
  - total surviving strength divided by total maximum strength per side;
  - active and destroyed unit counts;
  - controlled victory points;
  - average supply and morale where useful.
- Label the primary measure `Force Strength` or `Operational Readiness`, not `Health`.

### Reinforcements and Scenario Content

- The player needs a bounded, theatre-authored reserve pool rather than unlimited unit purchasing.
- Reserve definitions should include availability, cost, deployment rules, and eligible regions.
- Deployment should require a controlled port, supply depot, or designated entry position and should normally be limited per turn.
- The AI must use the same reserve and deployment rules.
- Phase 2 adds the authored Allied armoured reserve and validates its unit, scenario, region, side, feature, and event references before campaign creation.
- Scenario events are loaded but not integrated into the turn lifecycle.

### Theatre Portability

- Phase 2 removes theatre/scenario defaults from campaign creation and makes discovery manifest-driven.
- React campaign setup now submits the selected theatre, scenario, and playable side; headings and victory copy come from content.
- Authored assets now live in the theatre package and are validated and copied to generated frontend public output by the frontend development/build lifecycle.
- Package loading validates coordinates, symmetric adjacency, route agreement, scenarios, units, reserves, events, tension selectors/effects, display metadata, declared assets, and cross-catalogue IDs with file/field/ID diagnostics.

## Target Theatre Package

Phase 0 freezes the package and breaking contract details in [`architecture/development-baseline-v2.md`](architecture/development-baseline-v2.md). There is one supported V2 convention after the development reset.

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

The manifest identifies the theatre, supported scenarios, catalogue files, display file, asset manifest, and `sandtable-content-v2` contract version. A validated build/package step copies declared assets from the theatre package into generated frontend public output; `frontend/public` is not the authoring source of truth.

Content validation must fail fast with actionable paths and IDs. Adding a theatre must not require changes to `CampaignService`, `GameContentRepository`, React headings, or renderer code.

## Delivery Sequence

Each phase should remain a reviewable vertical slice. Development-mode freedom permits breaking changes and database resets, but it should not be used as a reason for an uncontrolled rewrite.

### Phase 0 - Establish the New Development Baseline

Status: **Complete — 10 July 2026.** The reviewed target contract is [`architecture/development-baseline-v2.md`](architecture/development-baseline-v2.md), the theatre pipeline is aligned to it, and the Engine-owned snapshot identifier is `sandtable-engine-v2`.

1. Confirm and document the new content, Engine, API, and persistence contracts.
2. Decide the final theatre package filenames and asset-serving flow.
3. Define the breaking model additions required for:
   - region kinds;
   - route movement and supply costs;
   - multi-condition victory rules;
   - reserve pools and deployments;
   - meaningful command costs and payloads;
   - scenario events;
   - progress/timeline projections.
4. Update the Engine version identifier for the new baseline.
5. Do not add legacy snapshot readers, obsolete JSON aliases, dual command payloads, or transitional API adapters.

Exit gate:

- The target contracts are written down and reviewed before broad implementation starts.

### Phase 1 - Stabilise PixiJS and the Command Table

Status: **Complete — 10 July 2026.** Fresh-session browser regression coverage verifies the required desktop viewports, background art, stack selection, resource visibility, and clean Pixi console.

1. Fix the background asset lifecycle so a fresh browser session always renders the theatre art.
2. Add a regression check that opens a fresh local browser session and confirms the background renders without Pixi asset warnings.
3. Add unit-stack selection through a stack popover, drawer, or side-panel list.
4. Fix top-resource clipping and reclaim vertical room from the command dock.
5. Add label priority/level-of-detail behavior for primary objectives versus minor positions.
6. Remove the old SVG renderer and obsolete CSS only after PixiJS passes build, interaction, and fresh-session visual checks.

Exit gate:

- Every visible unit is selectable.
- The art, routes, labels, ownership, counters, selection, and valid targets render correctly at 1280x720 and 1440x900.
- The browser console has no Pixi errors or warnings.

### Phase 2 - Theatre Contract and Content Validation

Status: **Complete — 10 July 2026.** The North Africa package uses the canonical manifest and filenames, authored assets sync from content, campaign setup is theatre-driven, and validation coverage includes a renamed fixture plus precise invalid-reference diagnostics.

1. Add the theatre manifest and align filenames with the chosen package convention.
2. Move authored assets into the theatre package and add the defined serving/copy step.
3. Remove North Africa defaults and special cases from campaign creation, content loading, and React UI.
4. Add `theatreId` to campaign creation and make campaign setup use the selected theatre/scenario.
5. Add a comprehensive content validator covering all cross-references and display metadata.
6. Correct or complete the missing Allied reserve and any other invalid North Africa content.
7. Add validation tests and one package-loading test that does not rely on the North Africa ID.

Exit gate:

- A renamed fixture theatre can be loaded without code changes.
- Invalid content fails with a precise file/field/id message.

### Phase 3 - Breaking Engine and Persistence Reset

Status: **Complete — 11 July 2026.** The V2 Engine/content/command/state contracts are active without compatibility adapters, the reviewed SQL was deployed to a verified clean `sand_table` development database, and a fresh Dapper campaign completed create -> typed command -> resolve -> tension choice -> snapshot reload.

1. Introduce the revised Engine models and commands without backward-compatibility adapters.
2. Update database table definitions and check constraints directly under `database/public`.
3. Keep JSON snapshots as the authoritative campaign-state durability boundary.
4. Do not add a special health-history table; derive campaign progress from snapshots unless profiling later proves that projection too expensive.
5. Update Dapper persistence and DTOs for the new command/state contracts.
6. Wipe and recreate only the SandTable development database when required.
7. Create a fresh campaign and verify create -> commands -> resolve -> tension choice -> reload against the reset database.

Database safety:

- The reset permission applies only to the SandTable development database identified by `VULTR_POSTGRES_URL_SAND_TABLE_DEV` or a disposable Docker/Testcontainers database.
- Verify the target before destructive work.
- Do not touch any production, staging, shared non-SandTable, or unrelated database.

Exit gate:

- The fresh schema deploys from reviewed SQL.
- No legacy campaign data or compatibility code is required.
- The full Dapper campaign loop passes against a fresh database.

### Phase 4 - Command Log, Turn Replay, and Progress Timeline

Status: **Complete — 11 July 2026.** Persisted events are grouped by turn and labelled `You`, `Enemy`, or `System`; the command log has movement, attack, casualty, supply, tension, and victory filters; and the live UI replays stored movement/battle events through PixiJS. `GET /api/campaigns/{campaignUid}/timeline` projects Force Strength, unit counts, controlled VP, supply, morale, and persisted markers from `Initial`/`TurnResolved` snapshots without a history table.

1. Group persisted events by turn and label them as `You`, `Enemy`, or `System`.
2. Add movement, attack, casualty, supply, tension, and victory icons/filters.
3. Ensure enemy moves and attacks are visible in the same resolved-turn narrative as player actions.
4. Add a campaign timeline endpoint derived from initial/resolved snapshots.
5. Display player and enemy Force Strength lines plus controlled VP and casualty markers.
6. Add a turn-replay controller that can animate movement and attacks through PixiJS in persisted event order.
7. Keep command/event persistence as the audit trail; the UI must not reconstruct outcomes from guesses.

Exit gate:

- A player can explain what both sides did and why the force/VP lines changed after each resolved turn.

### Phase 5 - Make Existing Systems Matter

Status: **Complete — 11 July 2026.** Ordered per-side command budgets now spend authored command/supply/fuel costs; controlled weighted routes trace supply from ports and depots; and disconnected units suffer visible escalating disruption and attrition. Support, Recon, Resupply, and Hold Position have deterministic effects, authored campaign modifiers change resolution, and multi-condition outcomes use persisted consecutive-turn progress. The command table projects costs and supply state before submission, while Engine and API tests cover each rule.

1. Make command points constrain the number or type of orders/actions each turn.
2. Implement route-based supply tracing from controlled ports, depots, and designated sources.
3. Apply fuel and supply costs plus escalating, visible out-of-supply effects.
4. Give `Support`, `Recon`, `Resupply`, and `HoldPosition` meaningful deterministic behavior.
5. Apply campaign modifiers during resolution rather than merely recording them.
6. Replace single-region instant victory with content-driven conditions such as:
   - control the primary objective;
   - maintain a connected supply route;
   - hold objectives for a required number of turns;
   - reach a VP threshold;
   - provide achievable conditions for both sides.
7. Add focused Engine tests for movement costs, supply, command capacity, modifier application, and victory.

Exit gate:

- A straight eastward rush is not reliably optimal.
- Resources and operational opportunities visibly affect decisions and outcomes.

### Phase 6 - Reserves, Reinforcements, and Scenario Events

1. Add theatre/scenario-authored reserve pools with stable unit IDs.
2. Define availability turn, resource/command cost, eligible deployment positions, and per-turn limits.
3. Require appropriate controlled supply/entry positions.
4. Resolve deployment through the Engine and persist it as an auditable command/event.
5. Integrate scheduled scenario events into deterministic turn resolution.
6. Give the AI access to the same reserve and deployment rules.
7. Add the UI reserve panel only after the underlying rules and validation are complete.

Exit gate:

- The player and AI can deploy bounded reserves without unit spam or special-case API mutations.

### Phase 7 - Combat, AI, and Operational Map Depth

1. Add approximately 6-10 meaningful North Africa operational positions.
2. Add explicit region kinds and route movement/supply costs.
3. Permit multi-node movement where allowance and route state permit, with enemy contact stopping or contesting movement as defined by the rules.
4. Resolve regional engagements coherently so multiple attackers, defenders, and support orders interact in one deterministic battle.
5. Add retreat, disruption, supply-cut, and force-preservation decisions.
6. Replace greedy AI behavior with scored plans covering objectives, supply, threatened positions, reserves, damaged-unit withdrawal, and coordinated attacks.
7. Keep the graph rather than adopting a full hex grid unless playtests prove the graph cannot express the required manoeuvre.

Exit gate:

- Both sides can pursue at least two credible operational axes.
- AI activity creates a coherent counter-campaign rather than repeated isolated moves or holds.

### Phase 8 - UI Polish and Additional Theatre Readiness

1. Refine ownership, front, supply, selection, valid-target, and replay overlays.
2. Move developer status out of the gameplay HUD.
3. Move campaign/scenario creation into a proper setup flow.
4. Add keyboard-accessible DOM unit/order surfaces alongside PixiJS.
5. Add per-theatre visual smoke tests at supported desktop breakpoints.
6. Add the second theatre only after the package validator and code-free theatre-loading path are proven.

Exit gate:

- The North Africa command table is readable, explainable, and visually close to `Specification/SandTable-Mockup.png` while remaining honest to the live rules.
- A second theatre is a content-authoring task, not an application rewrite.

## Playtest Metrics

Capture these for every balancing playtest:

- real session duration;
- resolved turn count;
- meaningful player orders per turn;
- enemy moves, attacks, holds, deployments, and rejected commands;
- player/enemy Force Strength by turn;
- controlled VP by turn;
- out-of-supply units by turn;
- reserves deployed and their timing;
- turns spent with only one clearly optimal action;
- final result and decisive objective/condition.

The target is a 20-30 minute session with sustained uncertainty and no long sequence of automatic eastward movement.

## Verification Baseline

For relevant slices, run:

```powershell
npm run build
```

from `frontend/`, and:

```powershell
dotnet build SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false
dotnet test SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false
```

Also run:

- content-package validation;
- Docker/Testcontainers Dapper campaign smoke tests after SQL/API changes;
- optional hosted-development smoke tests only against the verified SandTable dev database;
- browser visual and interaction checks at 1280x720 and 1440x900;
- fresh-browser Pixi asset-loading checks;
- at least one recorded end-to-end balancing playtest after each major mechanics phase.

## Decisions Already Made

- Development data may be discarded.
- No backward compatibility is required for prototype snapshots, APIs, command payloads, or content JSON.
- No EF migrations for SandTable application tables.
- Keep PixiJS.
- Keep the operational graph; do not introduce a general hex grid now.
- Use a bounded reserve pool rather than unlimited unit creation.
- Derive timeline metrics from authoritative snapshots initially.
- Build one clean theatre package method before adding another theatre.
