# Database Workflow

SandTable uses a database-first workflow.

## Source Of Truth

The reviewed SQL files under `database/` are the schema source of truth.

Do not use EF Core migrations to create or evolve SandTable application tables.

## Layout

- `database/main.sql` imports public schema files.
- `database/public/*.sql` contains table definitions, constraints, indexes, and comments.
- `database/db-refresh-main.ps1` regenerates `main.sql` imports.
- `database/db-deploy-dev.ps1` deploys schema changes through Atlas.
- `database/public/extensions.sql` records manual extension setup such as `pgcrypto`.

## Environment Variables

- `VULTR_POSTGRES_URL_SAND_TABLE_DEV` - project development database.
- `POSTGRES_ATLAS_DEV` - temporary Atlas working database.

## Identifier Pattern

Tables follow the 80%/Schema Mapper style:

- `id bigint generated always as identity primary key` for internal joins.
- `uid uuid not null default gen_random_uuid()` for external/public identity.
- explicit primary keys, unique constraints, foreign keys, check constraints, indexes, timestamps, and version columns.

Keep using `bigint` for internal surrogate IDs and FKs unless there is a deliberate schema-wide decision to change the pattern.

Use `integer` for bounded domain values such as turn numbers, scores, counts, settings, and command/event sequence numbers.

## Auth Staging

The schema allows early development ownership through `Development` user rows. This is a staging shortcut only.

Before V1 staging:

- OpenIddict-backed ownership is required.
- Development-only users should not be the production ownership path.
- OpenIddict may use its own EF Core DbContext and schema management approach.

## Snapshots

`campaign_snapshot.game_state` stores serialized `SandTable.Engine.GameState` as JSONB.

The API should update existing latest snapshots to `is_latest = false` before inserting a new latest snapshot.

Snapshot writes are the main durability boundary for stateless API behavior.

The reviewed V2 reset keeps this JSON-snapshot boundary and derives timeline metrics from snapshots rather than adding history tables. See [`development-baseline-v2.md`](development-baseline-v2.md). SQL changes for that baseline are made directly in Phase 3 after the target database is verified.

## Extension Notes

The schema uses `gen_random_uuid()`, which requires `pgcrypto` on PostgreSQL.

If Atlas does not manage extension creation in the current setup, run extension setup manually against the target database:

```sql
create extension if not exists pgcrypto;
```
