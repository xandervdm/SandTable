# SandTable Game Roadmap Implementation Kickoff

> Status update — 11 July 2026: Phases 0 through 6 are complete. This original kickoff is retained as implementation history and should not be reused as a current start prompt. Continue with Phase 7 from `docs/game-review-roadmap.md` and `docs/architecture/development-baseline-v2.md`.

Paste the prompt below into a new Codex task to begin implementation.

```text
We are working in E:\GitHub\SandTable on SandTable, a browser-first WWII-inspired operational strategy game.

Your objective is to execute the reviewed development roadmap in:

- E:\GitHub\SandTable\docs\game-review-roadmap.md

Treat that file as the governing product and implementation plan. Work through it in order as reviewable vertical slices. Do not collapse the entire roadmap into one uncontrolled rewrite. Complete and verify the earliest safe phase before widening scope, and keep the roadmap updated if implementation evidence requires a sequencing adjustment.

Start by reading:

- E:\GitHub\SandTable\docs\game-review-roadmap.md
- E:\GitHub\SandTable\docs\codex-context.md
- E:\GitHub\SandTable\docs\design\game-loop-v1.md
- E:\GitHub\SandTable\docs\strategic-tension.md
- E:\GitHub\SandTable\docs\architecture\backend-v1.md
- E:\GitHub\SandTable\docs\architecture\engine-boundaries.md
- E:\GitHub\SandTable\docs\architecture\database-workflow.md
- E:\GitHub\SandTable\docs\architecture\theatre-content-pipeline.md
- E:\GitHub\SandTable\Specification\SandTable-Mockup.png

Also inspect the current implementation before editing:

- E:\GitHub\SandTable\src\SandTable.Engine
- E:\GitHub\SandTable\src\SandTable.Api
- E:\GitHub\SandTable\database
- E:\GitHub\SandTable\content\theatres\north-africa
- E:\GitHub\SandTable\frontend\src\App.tsx
- E:\GitHub\SandTable\frontend\src\components\PixiTheatreMap.tsx
- E:\GitHub\SandTable\frontend\src\runtime
- E:\GitHub\SandTable\tests

Important current findings:

- PixiJS is the chosen renderer and React remains the application shell.
- The Pixi scene already has persistent background, route, hit-area, mode, label, and unit layers.
- In a fresh browser session, the theatre background can remain dark because `Sprite.from()` is called before `Assets.load()` completes and the static-world cache key prevents the background layer from being rebuilt.
- Compact stacks currently expose only one representative unit.
- Campaign events already include side/unit/region/type information, but the command log discards most of it.
- Existing snapshots are sufficient to derive a Force Strength/VP timeline.
- The current Engine is too shallow for the 20-30 minute USP: free orders, one-edge movement, mostly inert resources/modifiers, placeholder Support/Recon/Hold behavior, simple sequential combat, immediate single-objective victory, and greedy per-unit AI.
- Campaign/content/frontend code still contains North Africa special cases.
- `content\theatres\north-africa\events.json` references `allied-armoured-reserve`, which is missing from the unit catalogue.

Architecture constraints:

- SQL under `database/` is the SandTable schema source of truth.
- Use Dapper/Npgsql for SandTable persistence.
- Do not introduce EF migrations or EF entities for SandTable application tables.
- `SandTable.Engine` must remain pure: no HTTP, ASP.NET, Dapper, Npgsql, filesystem, environment, wall-clock, or hidden randomness dependencies.
- The API must remain stateless and rehydrate campaigns from persisted JSON snapshots.
- Keep Engine behavior deterministic from explicit state/content/commands/seeds.
- Human and AI commands must be planned from the same starting state.
- Theatre-specific gameplay and presentation belong in content JSON/assets, not hardcoded C# or React.
- Keep PixiJS; do not switch to Phaser, Mapbox, React Flow, a 3D engine, or a full hex-grid rules model.
- Use `Specification\SandTable-Mockup.png` as the visual north star, but keep live overlays driven by structured content/state.

Development-mode authority:

- No backward compatibility is required for current development campaigns, snapshots, command payloads, DTOs, or content JSON.
- You may make direct breaking changes to the development contracts.
- You may update the SQL table definitions directly and wipe/recreate the SandTable development database if the relevant phase requires it.
- Reset permission applies only to the verified SandTable development database identified by `VULTR_POSTGRES_URL_SAND_TABLE_DEV` or to a disposable Docker/Testcontainers database.
- Before any destructive database action, verify the target is the SandTable development database. Never touch production, staging, another project's database, or an unverified target.
- Do not build legacy JSON converters, old-snapshot readers, duplicate API versions, obsolete filename aliases, or transitional EF migrations.

Implementation sequence:

1. Inspect git status, current processes, and the current merged source.
2. Re-ground in the roadmap and decide whether the Phase 0 contract decisions are already sufficiently explicit. If a concrete contract remains ambiguous, document the smallest decision in the roadmap before implementing it.
3. Implement Phase 1 first: stabilise the Pixi background lifecycle, add stack selection, correct command-table sizing/resource clipping, and add fresh-session visual regression coverage.
4. Proceed to Phase 2 only after Phase 1 is verified: establish the theatre manifest/package, align display/asset filenames, remove North Africa hardcoding, and add comprehensive content validation.
5. Then perform the coordinated breaking Engine/API/SQL reset in Phase 3. Prefer one deliberate clean baseline over compatibility scaffolding.
6. Work through the remaining roadmap phases in order, keeping each slice testable and mergeable.

Phase 1 acceptance checks:

- A fresh browser session renders the North Africa background art with no Pixi asset warnings.
- Art, routes, labels, ownership, counters, selection, and valid targets all render.
- Every unit in a multi-unit stack can be selected through an explicit stack UI.
- All five resources remain visible at 1280x720 and 1440x900.
- The map remains the visual hero and gains usable vertical space.
- `npm run build` passes from `E:\GitHub\SandTable\frontend`.
- Relevant Engine/API tests remain green.
- Browser console has no errors or warnings.

Database and theatre reset expectations for later phases:

- Establish one package convention under `content\theatres\<theatreId>` with a theatre manifest, map, display, assets manifest, units, reserves, scenarios, events, doctrines, and tension cards.
- Make `CreateCampaignRequest` identify the theatre/scenario rather than defaulting to North Africa internally.
- Ensure adding a theatre does not require edits to `CampaignService`, `GameContentRepository`, React headings, or Pixi renderer code.
- Add fail-fast validation for coordinates, adjacency, routes, scenarios, unit/reserve IDs, event effects, tension selectors, display metadata, and assets.
- Update reviewed SQL definitions and check constraints directly when new commands such as deployment require them.
- After a verified dev reset, smoke-test create campaign -> submit commands -> resolve turn -> choose tension -> reload from snapshot.

Gameplay direction for later phases:

- Target roughly 8-10 turns and 3-5 meaningful player decisions per turn.
- Make command points, supply, fuel, reserves, scenario events, and operational opportunities affect resolution.
- Use a bounded reserve pool with availability, cost, legal deployment positions, and per-turn limits; apply the same rules to AI.
- Add only meaningful intermediate operational positions. Do not add nodes merely to lengthen movement.
- Keep the graph rather than moving to a hex grid unless later playtesting proves a concrete graph limitation.
- Derive Force Strength, active/destroyed units, controlled VP, supply, and morale history from authoritative snapshots initially.
- Show both player and enemy movements/attacks in the command log and replay persisted events in deterministic order.

Verification commands:

- From `E:\GitHub\SandTable\frontend`:
  - `npm run build`
- From `E:\GitHub\SandTable` using a temporary artifacts directory where practical:
  - `dotnet build SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false`
  - `dotnet test SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false`
- After SQL/API changes:
  - run the Docker/Testcontainers Dapper campaign smoke path;
  - run an optional hosted-dev smoke only against the verified SandTable dev database.
- For Pixi/UI work:
  - run the frontend against the API;
  - inspect at 1280x720 and 1440x900;
  - test a genuinely fresh browser session;
  - verify selection, valid targets, stack expansion, and absence of console warnings.

Working style:

- Start with evidence from the current repo, not assumptions from the older specification.
- Keep changes narrowly scoped to the active roadmap phase.
- Preserve unrelated user changes in a dirty worktree.
- Update or add tests with each rules/content contract change.
- Report concrete changed files, verification results, remaining risks, and the next roadmap phase at each handoff.
- Do not claim a phase complete until its exit gate in `docs\game-review-roadmap.md` is satisfied.

Begin now by inspecting git status and reading the roadmap and current Pixi component. Then implement and verify Phase 1.
```
