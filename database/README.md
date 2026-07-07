# SandTable Database

SandTable starts database-first. The reviewed source of truth is the SQL in this folder; EF Core migrations are not the initial schema source.

Publish flow:

1. Ensure required PostgreSQL extensions are installed manually. For V1 this is `create extension if not exists pgcrypto;`, kept in `database/public/extensions.sql` as a reference script.
2. Review and publish the SQL table files to the development PostgreSQL database with `database/db-deploy-dev.ps1`.
3. Confirm the live database shape.
4. Reverse engineer EF Core `DbContext` and entities from the live schema into the API/data layer.

Do not place generated EF entities in `SandTable.Engine`. The engine must stay pure: no EF Core, no HTTP, no ASP.NET, and no database code.

The early V1 development path may use an implicit development user row in `public.user_account`. Before V1 staging, ownership must be backed by real OpenIddict authentication and the staged development path must be removed or disabled.

Apply table files in the order listed in `database/main.sql`. `database/main.sql` intentionally excludes `database/public/extensions.sql` because extension provisioning is a database/server prerequisite rather than part of the free Atlas schema apply path.
