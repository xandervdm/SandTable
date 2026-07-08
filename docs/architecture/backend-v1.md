# Backend V1 Architecture

SandTable V1 uses a small ASP.NET Core API over a pure Engine and a PostgreSQL database.

## Shape

- `SandTable.Api` owns HTTP, Dapper persistence, connection handling, content file loading, and request/response DTOs.
- `SandTable.Engine` owns game state, command resolution, AI planning, strategic tension generation, and effect application.
- PostgreSQL stores campaign ownership, turns, commands, events, career records, and JSONB snapshots.

The API is intentionally thin. It rehydrates a `GameState` from the latest snapshot, calls Engine services, then persists the new state and audit trail.

## Persistence

SandTable application tables use Dapper and hand-written SQL. This keeps the application aligned with the database-first workflow where SQL files are reviewed before publishing.

The API may later use:

- Dapper for SandTable application persistence and targeted query paths.
- A separate EF Core DbContext for OpenIddict auth tables.

Do not use EF migrations as the SandTable schema source of truth.

## Authentication Stage

Early development uses an implicit development user:

- `user_account.auth_provider = 'Development'`
- `user_account.is_development_user = true`
- `auth_subject` remains null

Before V1 staging, real auth must be implemented with OpenIddict-backed ownership.

## API Statelessness

The API should not keep campaign state in memory between requests.

Each state-changing operation should:

1. Load the campaign and latest snapshot.
2. Validate the operation.
3. Call `SandTable.Engine`.
4. Persist new commands/events/snapshots in a transaction.
5. Return the persisted result.

## Current Endpoint Responsibilities

- `POST /api/campaigns` creates the implicit dev account/profile if needed, creates a campaign, creates turn 1, and persists an initial snapshot.
- `POST /api/campaigns/{campaignUid}/commands` stores player commands for the current planning turn.
- `POST /api/campaigns/{campaignUid}/resolve-turn` loads the latest state, plans AI commands, resolves the turn, generates active tensions, persists events, and writes a latest snapshot.
- `POST /api/campaigns/{campaignUid}/tensions/{cardId}/choose` applies an active tension option, records the decision, emits events, and writes an autosave snapshot.

## Endpoint Metadata

Minimal API endpoints use explicit names, tags, response metadata, request-body metadata, and `[FromServices]` service parameters in `src/SandTable.Api/SandTableEndpoints.cs`.
This prepares the surface for OpenAPI/client generation without adding Swagger UI or an OpenAPI document package yet.

## Dev Database Smoke Coverage

`tests/SandTable.Api.Tests/DevDatabaseSmokeTests.cs` is gated by `VULTR_POSTGRES_URL_SAND_TABLE_DEV`.
When the variable is absent, it returns without database access. When present, it creates a dev campaign through `CampaignService`, submits a command, resolves a turn, reads campaign state/events, and chooses a generated tension option when available.

## Error Handling

Current endpoints return problem responses for:

- unknown campaigns
- invalid command submissions
- invalid turn status
- invalid tension card or option choice
- database connectivity failures

Keep future validation paths on the same problem-details shape instead of returning ad hoc error payloads.
