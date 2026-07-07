# Game Loop V1

V1 should support a small but complete campaign loop before broad UI expansion.

## Core Loop

1. Player creates a campaign.
2. API creates the implicit dev ownership records if needed.
3. API loads theatre content and creates initial Engine state.
4. API persists turn 1 and an initial latest snapshot.
5. Player reviews map, regions, units, resources, and active state.
6. Player submits commands for the current planning turn.
7. AI plans commands from the same starting snapshot.
8. Engine resolves player and AI commands together.
9. Engine emits events and advances campaign state.
10. If campaign is still active, Engine generates up to two strategic tension cards.
11. API persists commands, events, and a new latest snapshot.
12. Player reviews the turn summary and active operational opportunities.
13. Player chooses tension-card options where available.
14. API persists each choice as an autosave snapshot and events.
15. Next planning turn begins.

## V1 Player Commands

Current command types:

- `Move`
- `Attack`
- `Support`
- `HoldPosition`
- `Resupply`
- `Recon`

Command validation should become stricter before frontend work depends on it. The API should reject impossible or malformed commands before insert where practical, while the Engine remains the final rules authority.

## Turn Summary

Turn results should be explainable from persisted data:

- `campaign_command` records human and AI orders.
- `campaign_event` records movement, battle, supply, recon, victory, scenario, and system events.
- `campaign_snapshot` records the resulting full Engine state.

## UI Readiness

Before building the React UI, add read endpoints that expose:

- theatre/scenario list
- current campaign summary
- latest map state
- unit state
- resources
- turn events
- active tensions
- tension history

The UI should not need to reconstruct rules from raw database tables.
