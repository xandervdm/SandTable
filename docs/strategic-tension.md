# Strategic Tension Points

Strategic Tension Points are turn-based dilemmas surfaced to the player as Operational Opportunities. They are engine state, not UI flavour. Each card asks the player to choose between two useful but imperfect options.

## Purpose

The feature makes turns more interesting without adding a large rules layer. A card should create a real trade-off: tempo versus supply, defence versus flexibility, political capital versus reserves, or risk versus intelligence.

Design rule: no tension card should ever have an obviously correct option.

## Lifecycle

1. The player submits normal campaign commands.
2. The AI plans its commands.
3. `TurnResolver` resolves movement, combat, supply-style command effects, events, and victory.
4. If the campaign is still active, the API loads the theatre's `tension-cards.json` catalogue and `BasicTensionGenerator` deterministically generates up to two active cards using the turn seed, current state, and that catalogue.
5. The API exposes active cards through the latest campaign state snapshot.
6. The player chooses an option through `POST /api/campaigns/{id}/tensions/{cardId}/choose`.
7. `TensionChoiceResolver` validates the card and option, applies engine effects, records a decision, removes the active card, and emits readable game events.
8. The API persists the resulting state as a new latest snapshot using the existing Dapper snapshot flow.

## Data Model

- `StrategicTensionCard`: card id, title, description, category, trigger, and options.
- `TensionOption`: option id, label, description, and game effects.
- `TensionCategory`: operational, logistics, weather, political, or intelligence grouping.
- `TensionTrigger`: deterministic reason the card exists.
- `TensionDecision`: turn, side, selected card, selected option, and applied effect summaries.
- `GameState.ActiveTensions`: current unresolved cards.
- `GameState.TensionHistory`: choices already made.
- `GameState.CampaignModifiers`: lightweight recorded modifiers for current or future rules.

## Effects

V1 effects are deliberately small and deterministic:

- `AddResourceEffect`
- `AddCampaignModifierEffect`
- `ModifyUnitStatEffect`
- `ModifyRegionEffect`
- `AddGameEventEffect`

The engine applies effects to copied state and returns a new `GameState`. Snapshots persist the full state as JSON, including active tensions, history, and campaign modifiers.

## Adding New Cards

Add cards to the theatre catalogue, for example `content/theatres/north-africa/tension-cards.json`.

Use stable ids and exactly two meaningful options. Prefer effects that are immediately visible in state. If a card references a unit or region, use one of the supported selectors so the engine can resolve the target deterministically from the current state. Do not use random values outside the provided seed.

Theatre-specific cards belong only in the theatre that can produce them. For example, `Sandstorm Forecast` is North Africa content, not a global C# rule.

Before adding a card, ask:

- Does each option help the player in a different way?
- Does each option have a cost or risk?
- Is either option always correct in normal play?
- Does the card stay understandable from the campaign state alone?

## V1 Cards

- Overextended Armour: exploit armour tempo or rebuild supply.
- Hold The Line: harden a frontline position or preserve mobile reserves.
- Sandstorm Forecast: move under weather cover or wait out the storm.
- Political Pressure: promise visible results or manage expectations.
- Enemy Fuel Convoy Spotted: raid the convoy or shadow it for intelligence.
