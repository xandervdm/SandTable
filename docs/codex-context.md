# SandTable Codex Context

Read this first when starting a new Codex thread for SandTable.

## Project

SandTable is a browser-first WWII-inspired strategy game.

Workspace:

- `E:\GitHub\SandTable`

Primary source areas:

- `src/SandTable.Engine` - pure deterministic game engine.
- `src/SandTable.Api` - ASP.NET Core API using Dapper for SandTable persistence.
- `database` - PostgreSQL SQL source of truth.
- `content/theatres/north-africa` - V1 game content.
- `tests` - xUnit test projects.
- `docs` - durable project context and design notes.

## Current Architecture

The current backend is a thin vertical slice:

1. Load North Africa content.
2. Create a campaign for an implicit development user.
3. Persist the initial campaign snapshot.
4. Submit player commands.
5. Plan AI commands from the same starting snapshot.
6. Resolve the turn simultaneously in `SandTable.Engine`.
7. Generate strategic tension cards when the campaign remains active.
8. Persist commands, events, and a new latest snapshot.
9. Let the player choose a tension-card option.
10. Persist the resulting autosave snapshot and events.

## Non-Negotiable Constraints

- SQL files under `database/` are the reviewed schema source of truth.
- Do not use EF Core migrations for SandTable application tables.
- Do not reverse engineer EF Core entities unless the database has been published and the user explicitly asks.
- SandTable application persistence currently uses Dapper and Npgsql.
- OpenIddict is still required before V1 staging and may use its own EF Core DbContext later.
- Early V1 development may use the implicit development user path.
- `SandTable.Engine` must stay pure: no EF Core, no HTTP, no ASP.NET, no database code, and no filesystem dependencies.
- API handlers should be stateless between requests and rehydrate from persisted snapshots.
- Campaign snapshots are persisted as JSONB in PostgreSQL.
- Turn resolution is simultaneous: human and AI commands are planned from the same starting state and resolved together.
- Engine behavior should be deterministic from explicit inputs and seeds.

## Database Workflow

PostgreSQL schema lives under `database/`.

Important environment variables:

- `VULTR_POSTGRES_URL_SAND_TABLE_DEV` - SandTable development database.
- `POSTGRES_ATLAS_DEV` - temporary Atlas working database.

Important files:

- `database/main.sql`
- `database/db-refresh-main.ps1`
- `database/db-deploy-dev.ps1`
- `database/public/*.sql`

`pgcrypto` is represented by `database/public/extensions.sql`, but extension creation may be run manually depending on Atlas/Vultr constraints. The table SQL uses `gen_random_uuid()`.

## Public API Surface

Current API routes:

- `GET /api/health`
- `GET /api/content/theatres`
- `GET /api/content/theatres/{theatreId}`
- `GET /api/content/theatres/{theatreId}/scenarios/{scenarioId}`
- `POST /api/campaigns`
- `GET /api/campaigns`
- `GET /api/campaigns/{campaignUid}`
- `GET /api/campaigns/{campaignUid}/snapshot`
- `GET /api/campaigns/{campaignUid}/state`
- `GET /api/campaigns/{campaignUid}/events`
- `POST /api/campaigns/{campaignUid}/commands`
- `POST /api/campaigns/{campaignUid}/resolve-turn`
- `POST /api/campaigns/{campaignUid}/tensions/{cardId}/choose`

Manual samples live in:

- `src/SandTable.Api/SandTable.Api.http`

Dev database smoke test:

- `scripts/smoke-dev-api.ps1`
- Requires `VULTR_POSTGRES_URL_SAND_TABLE_DEV`.
- Builds with temp artifacts, starts the API on localhost, creates a campaign, submits a command, resolves a turn, and chooses a generated tension option when available.
- `tests/SandTable.Api.Tests/DevDatabaseSmokeTests.cs` also runs the Dapper service loop when the same env var is set; otherwise it returns without touching a database.

## Strategic Tension

Strategic Tension Points are also presented as Operational Opportunities.

They are engine state, not UI-only flavor. Active tension cards, decision history, and campaign modifiers live in `GameState` and are persisted through campaign snapshots.

Read:

- `docs/strategic-tension.md`
- `content/theatres/north-africa/tension-cards.json`
- `src/SandTable.Engine/BasicTensionGenerator.cs`
- `src/SandTable.Engine/TensionChoiceResolver.cs`
- `src/SandTable.Engine/GameEffectApplier.cs`

Design rule: no tension card should ever have an obviously correct option.

## Build And Test

Use temp artifacts to avoid local `bin/obj` and compiler-server permission issues:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet_cli_home_build')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$artifacts = Join-Path $env:TEMP ('SandTableArtifacts-' + [guid]::NewGuid().ToString('N'))
dotnet build SandTable.slnx --artifacts-path $artifacts /p:UseSharedCompilation=false
```

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet_cli_home_build')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$artifacts = Join-Path $env:TEMP ('SandTableArtifacts-' + [guid]::NewGuid().ToString('N'))
dotnet test SandTable.slnx --artifacts-path $artifacts /p:UseSharedCompilation=false
```

If `.dotnet_cli_home_build` is created, remove it after verification.

## Next Likely Priorities

- Smoke-test the live Dapper API against `VULTR_POSTGRES_URL_SAND_TABLE_DEV`.
- Fix any SQL/API mismatch found by create campaign -> submit command -> resolve turn -> choose tension option.
- Add explicit OpenAPI metadata/tags for the existing endpoints before frontend/API client generation.
- Decide whether campaign event reads should default to ascending or newest-turn-first ordering for the React UI.
- Keep tightening command validation as new command payload fields or costs are introduced.
- Keep OpenIddict as a staging prerequisite, separate from the early implicit dev user.
