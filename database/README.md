# SandTable Database

SandTable is database-first. The reviewed source of truth is the SQL under `database/public`; application persistence uses Dapper/Npgsql and authoritative JSON snapshots.

## Development publish flow

1. Verify `VULTR_POSTGRES_URL_SAND_TABLE_DEV` identifies only the SandTable development database.
2. Provision `pgcrypto` from `database/public/extensions.sql` when the database is empty.
3. Refresh `database/main.sql` with `database/db-refresh-main.ps1` after changing reviewed SQL files.
4. Review the generated Atlas plan and deploy with `database/db-deploy-dev.ps1`.
5. Run the API/Dapper smoke loop and verify snapshot `engine_version`, typed `command_payload`, and the latest serialized state.

Phase 3 established a clean `sandtable-engine-v2` baseline. No V1 snapshot reader, transitional migration, EF migration, reserve-history table, event-history table, or health-history table is part of the design.

`database/main.sql` intentionally excludes `database/public/extensions.sql` because extension provisioning is a database/server prerequisite rather than part of Atlas schema application.
