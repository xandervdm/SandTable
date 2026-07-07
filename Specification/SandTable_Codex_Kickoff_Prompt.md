# SandTable -- Codex Kickoff Prompt

> You are helping me build a new browser-first WWII-inspired strategy
> game project called **SandTable**.

## Project Vision

SandTable is a lightweight, browser-playable, 30-minute strategy game
where the player acts as a theatre commander rather than micromanaging
individual units. V1 focuses on a single North Africa campaign to prove
the core gameplay loop.

The game should feel like a WWII command table: planning orders,
resolving turns, viewing battle reports, saving/resuming campaigns, and
building a command career over time.

### Long-term goals (design seams only)

-   Offline browser/PWA play
-   Steam desktop packaging
-   Additional theatres
-   Scenario editor
-   Async multiplayer
-   Cloud saves
-   Career analytics
-   AI coaching
-   Optional 3D/isometric renderer

**Do not build these in V1.** Create clear boundaries only. In particular,
do not build Electron, Steam packaging, async multiplayer, cloud saves,
LLM/AI coaching, or a 3D/isometric renderer in V1.

------------------------------------------------------------------------

# Technology Stack

## Frontend

-   React
-   TypeScript
-   Vite
-   React Router
-   Tailwind CSS
-   shadcn/ui
-   Zustand
-   TanStack Query
-   SVG/Canvas renderer
-   No Three.js in V1
-   No physics engine

## Backend

-   ASP.NET Core Web API
-   .NET solution namespace: `SandTable`
-   Entity Framework Core
-   PostgreSQL
-   OpenIddict authentication before V1 staging deployment
-   REST API

## Game Engine

Create a separate class library:

`SandTable.Engine`

Rules:

-   No UI dependencies
-   No EF Core
-   No HTTP
-   No ASP.NET
-   No database code

Core rule:

``` text
GameState + PlayerCommands + RandomSeed
            ↓
GameState + GameEvents
```

V1 turn resolution should make enemy planning explicit:

``` text
GameState + HumanCommands + IAiPlanner + RandomSeed
            ↓
TurnResolution
            ↓
Next GameState + GameEvents
```

`TurnResolution` should contain the next state, accepted/rejected commands,
AI commands, battle events, supply events, victory events, and a readable
turn summary.

The same engine should eventually be usable:

-   Server
-   Browser (WASM)
-   Steam desktop

------------------------------------------------------------------------

# Solution Structure

``` text
SandTable/
│
├── src/
│   ├── SandTable.Engine/
│   ├── SandTable.Api/
│   └── SandTable.Client/
│
├── content/
│   └── theatres/
│       └── north-africa/
│           ├── map.json
│           ├── scenario-1942.json
│           ├── units.json
│           ├── doctrines.json
│           └── events.json
│
├── tests/
│   ├── SandTable.Engine.Tests/
│   └── SandTable.Api.Tests/
│
└── docs/
    ├── architecture.md
    ├── game-design.md
    └── roadmap.md
```

------------------------------------------------------------------------

# V1 Scope

-   One playable theatre (North Africa)
-   Single-player
-   Planning phase
-   Simultaneous turn resolution
-   Save/resume
-   Basic AI
-   Command profile
-   Lightweight campaign record
-   Post-game debrief
-   Full OpenIddict authentication before staging deployment

V1 should keep career tracking small: enough to show completed campaigns,
basic win/loss history, and a simple post-game summary. Deeper career
analytics and coaching remain later goals.

------------------------------------------------------------------------

# Turn Flow

``` text
Planning
↓
Issue Orders
↓
Commit Orders
↓
Resolve Turn
↓
Battle Reports
↓
Next Turn
```

Turn resolution is simultaneous. During planning, the player submits intent
without knowing the enemy's current-turn orders. When the player commits,
the AI plans from the same starting state, then the engine resolves player
commands, AI commands, movement, combat, supply, events, and victory checks
as one combined turn. This should allow unforeseen clashes, contested moves,
failed attacks, intercepted advances, and other surprises that make the turn
feel like a real command decision rather than an alternating board-game move.

------------------------------------------------------------------------

# Map Model

Each region contains:

-   Id
-   Name
-   Position
-   Terrain
-   Owner
-   Victory Points
-   Supply Value
-   Features
-   Adjacent Regions

Terrain:

-   Desert
-   Rough
-   Mountain
-   City
-   Coast
-   Oasis

Features:

-   Port
-   Airfield
-   City
-   Supply Depot
-   Fortified

------------------------------------------------------------------------

# Content Contracts

Content should be data-driven JSON loaded from `content/theatres/north-africa`.
Keep the schema simple, explicit, and friendly to manual editing.

## ID conventions

-   Use stable kebab-case string IDs, such as `tobruk`, `15th-panzer`,
    and `north-africa-1942`.
-   Do not use display names as foreign keys.
-   Every referenced region, unit, doctrine, event, or scenario ID must
    exist in the same content set.

## `map.json`

Define the theatre map and region graph.

``` json
{
  "theatreId": "north-africa",
  "name": "North Africa",
  "coordinateSystem": {
    "width": 1000,
    "height": 600
  },
  "regions": [
    {
      "id": "tobruk",
      "name": "Tobruk",
      "position": { "x": 720, "y": 310 },
      "terrain": "Coast",
      "owner": "Allies",
      "victoryPoints": 8,
      "supplyValue": 2,
      "features": ["Port", "Fortified"],
      "adjacentRegionIds": ["gazala", "benghazi", "el-alamein"]
    }
  ],
  "routes": [
    {
      "fromRegionId": "gazala",
      "toRegionId": "tobruk",
      "routeType": "CoastalRoad"
    }
  ]
}
```

Validation expectations:

-   Region positions are within the declared coordinate system.
-   Adjacency is symmetrical, or the loader normalizes it and reports errors.
-   Routes only connect adjacent regions.
-   Owners are `Axis`, `Allies`, or `Neutral`.

## `scenario-1942.json`

Define the playable scenario setup.

``` json
{
  "scenarioId": "north-africa-1942",
  "theatreId": "north-africa",
  "name": "North Africa Campaign - 1942",
  "startDate": "1942-06-12",
  "maxTurns": 15,
  "defaultSide": "Axis",
  "startingResources": {
    "supplies": 1200,
    "manpower": 850,
    "fuel": 430,
    "industry": 210,
    "commandPoints": 3
  },
  "victoryConditions": [
    {
      "type": "ControlRegion",
      "regionId": "alexandria",
      "requiredOwner": "Axis"
    }
  ],
  "startingUnitIds": ["15th-panzer"]
}
```

## `units.json`

Define reusable starting units.

``` json
{
  "units": [
    {
      "id": "15th-panzer",
      "name": "15th Panzer Division",
      "side": "Axis",
      "type": "Armour",
      "regionId": "tripoli",
      "strength": 10,
      "maxStrength": 10,
      "movement": 3,
      "attack": 6,
      "defence": 4,
      "supply": 8,
      "morale": 8,
      "experience": 6,
      "status": "Ready"
    }
  ]
}
```

## `doctrines.json`

Define small modifiers only. Avoid doctrine rules that require special-case
UI or complex simulations in V1.

``` json
{
  "doctrines": [
    {
      "id": "mobile-warfare",
      "name": "Mobile Warfare",
      "modifiers": {
        "armourAttackBonus": 1,
        "fuelConsumptionPenalty": 1
      }
    }
  ]
}
```

## `events.json`

Define scenario events that the engine can evaluate deterministically.

``` json
{
  "events": [
    {
      "id": "enemy-reinforcements-el-alamein",
      "trigger": { "turn": 6 },
      "effect": {
        "type": "AddUnit",
        "unitId": "allied-armoured-reserve",
        "regionId": "el-alamein"
      },
      "message": "Enemy reinforcements are arriving near El Alamein."
    }
  ]
}
```

Content loaders should fail fast with clear validation errors during
development. Tests should cover content loading, graph validity, scenario
startup, and at least one turn resolution using the seeded content.

------------------------------------------------------------------------

# Initial North Africa Regions

-   Casablanca
-   Algiers
-   Tunis
-   Kasserine Pass
-   Tripoli
-   Fezzan Desert
-   Benghazi
-   Tobruk
-   Gazala
-   El Alamein
-   Alexandria
-   Cairo

------------------------------------------------------------------------

# Units

-   Infantry
-   Armour
-   Artillery
-   Air Wing
-   Logistics
-   Recon

Each unit stores:

-   Strength
-   Movement
-   Attack
-   Defence
-   Supply
-   Morale
-   Experience
-   Status

------------------------------------------------------------------------

# Orders

-   Move
-   Attack
-   Support
-   Hold Position
-   Resupply
-   Recon

------------------------------------------------------------------------

# Combat

Simple deterministic combat using:

-   Attack
-   Defence
-   Terrain
-   Supply
-   Morale
-   Experience
-   Seeded randomness

Produce a readable battle log.

------------------------------------------------------------------------

# Supply

Supply originates from:

-   Ports
-   Supply depots

Supply travels through friendly adjacent regions.

Out-of-supply units suffer penalties.

------------------------------------------------------------------------

# AI

Implement `IAiPlanner`.

Responsibilities:

-   Protect objectives
-   Maintain supply
-   Attack weak enemies
-   Reinforce threatened regions
-   Advance toward objectives

No machine learning.

------------------------------------------------------------------------

# Command Profile

Fields:

-   Display Name
-   Preferred Doctrine
-   Default Side
-   Animation Speed
-   Hints Enabled
-   Auto Save Enabled

Doctrines:

-   Balanced
-   Mobile Warfare
-   Defensive Logistics
-   Air Superiority
-   Attrition

------------------------------------------------------------------------

# Career Analytics

V1 should track lightweight campaign history only.

Track:

-   Campaign
-   Doctrine
-   Side
-   Result
-   Turns
-   Units Lost
-   Battles Won
-   Battles Lost
-   Supply Breakdowns

Display:

-   Victory
-   Score
-   Strengths
-   Weaknesses
-   Recommendations

Do not build deep trend analytics, cross-campaign coaching, or LLM-generated
advice in V1. A simple deterministic recommendation is enough.

------------------------------------------------------------------------

# Database

Entities:

-   UserAccount
-   PlayerProfile
-   CommandProfile
-   Campaign
-   CampaignTurn
-   CampaignSnapshot
-   CampaignCommand
-   CampaignEvent
-   CareerRecord

Store snapshots as JSON.

Authentication may start with an implicit development user during early V1
development so gameplay, persistence, and UI can move quickly. Before V1 is
deployed to staging, replace the implicit user path with full OpenIddict
authentication and real user ownership for profiles, campaigns, snapshots,
commands, events, and career records.

------------------------------------------------------------------------

# API

``` text
GET    /api/health
POST   /api/campaigns
GET    /api/campaigns
GET    /api/campaigns/{id}
POST   /api/campaigns/{id}/commands
POST   /api/campaigns/{id}/resolve-turn
POST   /api/campaigns/{id}/autosave
GET    /api/campaigns/{id}/snapshot
PUT    /api/campaigns/{id}/snapshot
GET    /api/profile
PUT    /api/profile
GET    /api/career/summary
```

For V1, save/load can be implemented through campaign snapshots. Resolving
a turn should persist a snapshot, and autosave/manual save should write the
latest command/profile/UI-safe campaign state without changing engine rules.

------------------------------------------------------------------------

# Frontend Routes

-   /
-   /new-campaign
-   /campaign/:id
-   /career
-   /profile
-   /settings

------------------------------------------------------------------------

# Main UI

Top: - Resources - Turn - End Turn

Left: - Objectives - Weather - Unit panel

Centre: - Map - Units - Supply - Front line

Right: - Events - Command log

Bottom: - Orders - Reinforcements - Save/Load

Use `Specification/SandTable-Mockup.png` as the V1 visual direction, but
treat it as a functional wireframe rather than a pixel-perfect target.

V1 UI acceptance checklist:

-   Show campaign name, scenario year, turn number, and current date.
-   Show supplies, manpower, fuel, industry, and command points in the top bar.
-   Show objective, weather, selected unit, unit stats, and terrain guide.
-   Render a stylised North Africa region map with routes, ownership,
    unit counters, supply/front indicators, and a compact legend.
-   Show turn summary, events, and command log in a right-side panel.
-   Show available orders, available units, reinforcements, save/load, and
    command profile access.
-   Include a small theatre overview/minimap if it can be done cheaply.
-   Keep the visual tone close to a WWII command table: dark metal UI,
    parchment map, restrained military colours, readable counters, and
    high contrast for selected units and legal actions.

------------------------------------------------------------------------

# Rendering

Use SVG.

React components:

-   MapRenderer
-   RegionNode
-   RouteLine
-   UnitCounter
-   SupplyLine
-   FrontLine
-   SelectedUnitPanel
-   OrderPanel
-   TurnSummaryPanel

------------------------------------------------------------------------

# State

Zustand:

-   Selected unit
-   Pending orders
-   Zoom
-   Pan
-   UI state

TanStack Query:

-   Campaign
-   Profile
-   Career
-   Content

------------------------------------------------------------------------

# Tests

-   Movement
-   Combat
-   Supply
-   AI
-   Victory
-   Serialization

------------------------------------------------------------------------

# Principles

1.  Keep V1 small.
2.  Engine contains all rules.
3.  Frontend never decides outcomes.
4.  Server stores snapshots.
5.  Data-driven content.
6.  Build fun before polish.

------------------------------------------------------------------------

# First End-to-End Milestone

``` text
Create Campaign
↓
Load North Africa
↓
Select Unit
↓
Issue Order
↓
Commit Orders
↓
Resolve Player + AI Orders Simultaneously
↓
Save Snapshot
↓
Display Turn Summary
```
