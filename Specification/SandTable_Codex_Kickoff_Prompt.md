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

**Do not build these in V1.**

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
-   OpenIddict-ready authentication
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
-   Career statistics
-   Post-game debrief

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

------------------------------------------------------------------------

# API

``` text
GET    /api/health
POST   /api/campaigns
GET    /api/campaigns/{id}
POST   /api/campaigns/{id}/commands
POST   /api/campaigns/{id}/resolve-turn
POST   /api/campaigns/{id}/autosave
GET    /api/profile
PUT    /api/profile
GET    /api/career/summary
```

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
Resolve Turn
↓
AI Responds
↓
Save Snapshot
↓
Display Turn Summary
```
