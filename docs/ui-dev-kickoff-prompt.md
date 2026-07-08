# SandTable UI Dev Kickoff Prompt

Use this prompt to start the first SandTable browser UI implementation in a new Codex thread.

```text
We are working in E:\GitHub\SandTable on the SandTable browser-first WWII-inspired strategy game. Please start by reading the design and architecture docs in docs/, especially:

- docs/codex-context.md
- docs/design/game-loop-v1.md
- docs/design/tension-cards.md
- docs/strategic-tension.md
- docs/architecture/backend-v1.md
- docs/architecture/engine-boundaries.md
- docs/architecture/database-workflow.md

Also open and inspect the visual mockup before designing or implementing the frontend:

- Specification\SandTable-Mockup.png

Current backend state:

- Backend API is already running locally for UI dev:
  - https://localhost:7003
  - http://localhost:5171
- Prefer the HTTP backend URL for Vite proxying: http://localhost:5171
- Database for local API/UI dev is Vultr Postgres via VULTR_POSTGRES_URL_SAND_TABLE_DEV.
- Automated DB tests use Docker/Testcontainers Postgres.
- SandTable schema is database-first under database/.
- SandTable app persistence uses Dapper/Npgsql.
- Do not use EF migrations for SandTable schema.
- Do not reverse engineer EF for SandTable app tables unless explicitly asked.
- OpenIddict auth is still required before V1 staging, but early UI dev uses the implicit dev user/profile/command profile.
- SandTable.Engine must remain pure: no EF Core, no HTTP, no ASP.NET, no database code, no filesystem dependencies.
- API should stay stateless between requests and persist campaign snapshots after resolved turns.
- Turn resolution is simultaneous: player and AI commands are planned from the same starting state and resolved together.
- Strategic tension cards are engine state and content-driven from content/theatres/<theatreId>/tension-cards.json, not hardcoded UI flavor.

Current API surface for the UI:

- GET /api/health
- GET /api/content/theatres
- GET /api/content/theatres/{theatreId}
- GET /api/content/theatres/{theatreId}/scenarios/{scenarioId}
- POST /api/campaigns
- GET /api/campaigns
- GET /api/campaigns/{campaignUid}
- GET /api/campaigns/{campaignUid}/snapshot
- GET /api/campaigns/{campaignUid}/state
- GET /api/campaigns/{campaignUid}/events
- GET /api/campaigns/{campaignUid}/turns
- GET /api/campaigns/{campaignUid}/turns/{turnNumber}
- POST /api/campaigns/{campaignUid}/commands
- POST /api/campaigns/{campaignUid}/resolve-turn
- POST /api/campaigns/{campaignUid}/tensions/{cardId}/choose

Suggested UI implementation direction:

- Create a React/Vite frontend in the repo unless one already exists.
- Use React Router if there is more than one meaningful screen/view, for example campaign setup, active campaign, and campaign log.
- Use Tailwind CSS for styling.
- Use shadcn/ui where it helps for accessible, polished primitives such as buttons, dialogs, tabs, selects, tooltips, cards, sheets, and scroll areas.
- Use lucide-react icons where appropriate.
- Use a Vite dev proxy for /api to http://localhost:5171 rather than adding CORS first.
- Build the actual playable command interface as the first screen, not a marketing landing page.
- Make the first UI slice support:
  1. Load theatre/scenario content.
  2. Create or select a campaign.
  3. Render current campaign state: map, regions, units, resources, turn number, active tensions.
  4. Let the player select a unit and submit one simple command, initially Move/Attack/HoldPosition if practical.
  5. Resolve the turn.
  6. Show turn events/summary.
  7. Show active tension cards and allow choosing an option.
- Keep the UI honest to backend state. Do not reconstruct rules from raw database tables.
- If the first UI exposes awkward backend response shapes, make the smallest backend read-model addition needed, but preserve the Dapper/database-first architecture.

Visual target:

- The UI should intentionally follow the mockup at Specification\SandTable-Mockup.png.
- Match the mockup's overall command-table feel: military operations map, dark tactical interface, side panels, unit/order/event surfaces, and WWII strategy-board mood.
- The first screen should look like the actual command interface from the mockup, not a generic SaaS dashboard or landing page.
- Use dense tactical panels matching the mockup for units, orders, events, resources, and operational opportunities.
- Keep shadcn components adapted to the SandTable visual style rather than leaving them with a generic app-template look.
- Make interactions obvious: selected unit, valid target, pending order, resolving state, errors.
- Use icons for controls where appropriate.
- Ensure mobile/desktop text and panels do not overlap.
- Make the interface responsive, but prioritize the desktop command-table experience first.

Start by:

1. Inspecting git status and current file structure.
2. Reading the docs listed above plus src/SandTable.Api/SandTableEndpoints.cs and src/SandTable.Api/CampaignDtos.cs.
3. Opening Specification\SandTable-Mockup.png and using it as the visual north star.
4. Checking whether a frontend package already exists.
5. If none exists, scaffold the smallest React/Vite app that fits this repo.
6. Configure /api proxy to http://localhost:5171.
7. Implement the first playable campaign loop against the running backend.
8. Run frontend build/tests.
9. If touching backend, run:
   dotnet build SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false
   dotnet test SandTable.slnx --artifacts-path <temp> /p:UseSharedCompilation=false

Do not start OpenIddict/auth work in this UI thread unless explicitly asked. Keep this slice focused on the first playable browser UI against the existing dev-user backend.
```
