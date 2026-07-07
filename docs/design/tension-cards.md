# Tension Cards

Strategic Tension Points are presented to the player as Operational Opportunities.

The detailed current design lives in:

- `docs/strategic-tension.md`

This file is a short design index for future UI and content work.

## Design Rule

No tension card should ever have an obviously correct option.

Each card should ask the player to choose between two useful but imperfect plans.

## Current Implementation

Current code and content:

- `content/theatres/north-africa/tension-cards.json`
- `src/SandTable.Engine/BasicTensionGenerator.cs`
- `src/SandTable.Engine/TensionChoiceResolver.cs`
- `src/SandTable.Engine/GameEffectApplier.cs`
- `tests/SandTable.Engine.Tests/StrategicTensionTests.cs`

Active cards, decision history, and campaign modifiers are serialized inside `GameState` and persisted through `campaign_snapshot.game_state`.

## API

The current choice endpoint is:

- `POST /api/campaigns/{campaignUid}/tensions/{cardId}/choose`

The request chooses one option from an active card. The API calls the Engine, records events, and writes an autosave snapshot.

## Content Guidance

Cards should use stable IDs and theatre-appropriate selectors. Keep V1 effects small and visible:

- resource changes
- campaign modifiers
- unit stat changes
- region changes
- readable game events

Prefer content JSON over hard-coded C# rules for theatre-specific situations.
