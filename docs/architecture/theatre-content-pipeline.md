# Theatre Content Pipeline

SandTable should be able to add new theatres without rebuilding the game UI or leaking visual map data into the Engine.

## Goal

Support two needs at the same time:

- get to a playable UI quickly for North Africa
- establish a repeatable process for adding the next theatre after the Engine loop has been tested

The long-term content model should assume the team may not have dedicated cartography or graphic design capacity. AI-generated assets are acceptable and expected, but they must remain presentation assets rather than gameplay truth.

## Canonical V2 Package

The exact package and model contract is frozen in [`development-baseline-v2.md`](development-baseline-v2.md). Each theatre separates rules, visual layout, and generated assets using this one layout:

```text
content/theatres/<theatreId>/
  theatre.json          # manifest and sandtable-content-v2 identifier
  map.json              # gameplay topology and region facts
  map-display.json      # visual geometry and render hints
  map-assets.json       # generated/open asset manifest and attribution
  units.json
  reserves.json
  tension-cards.json
  doctrines.json
  events.json
  scenarios/
    <scenarioId>.json
  assets/
    map-base.png
    paper-texture.png
    terrain-*.png
```

`theatre.json` is the only discovery entry point. It declares the catalogue files and maps each scenario ID to its file. Loaders must not scan for aliases or derive a North Africa-specific filename.

`map.json` remains the rules source of truth:

- region ids
- region anchor positions
- terrain
- owners
- victory and supply values
- features
- region kind
- routes, movement cost, and supply cost

Adjacency is derived from routes rather than duplicated on regions.

`map-display.json` is presentation content:

- land and sea outline paths
- region polygons or SVG paths
- route polylines
- label anchors and offsets
- counter slots
- feature marker positions
- decorative terrain marks
- references to stable asset IDs from `map-assets.json`

`map-assets.json` records where visual assets came from:

- generation prompt or source reference
- generation date/tool when known
- input references used
- license or attribution notes for non-AI/open-source material
- intended use, for example background texture, coastline reference, or terrain stamp

The authoring assets live only under the theatre package. A validated sync step copies declared files into `frontend/public/theatres/<theatreId>/assets` for Vite development and builds. Public output is generated and must not become the authoring source.

## AI Asset Guidance

Use AI for visual style and texture, not for gameplay authority.

Good AI-generated assets:

- parchment or paper texture
- desert, sea, mountain, and rough-terrain texture layers
- decorative staff-map base art
- command counter visual treatments
- theatre mood references

Avoid baking these into a generated background:

- unit positions
- region ownership
- selectable targets
- route validity
- active tensions or events
- UI labels that need to be readable and exact
- game state that changes turn to turn

For generated theatre base maps, prefer no embedded text. Let the UI render labels from structured data so spelling, localization, scaling, and hover states remain controlled.

## Better Source Options

AI is useful for the parched staff-map look, but it is not the only option.

Possible source inputs for later theatre work:

- open geographic datasets for coastlines and rough land shapes
- public-domain or permissively licensed historical maps as references
- hand-traced simplified shapes from open references
- commissioned or purchased cartography if the project later needs commercial-grade fidelity

Licenses should be checked and recorded before non-AI assets are committed. If the source is uncertain, use it only as inspiration and author a new derived display layer.

## Current Rendering Path

North Africa already uses PixiJS with a raster theatre background and structured live overlays. Preserve that split:

Use the existing `map.json` point-and-route graph to render:

- a content-authored background asset with a code-rendered fallback
- region labels
- region nodes
- dashed routes
- terrain hints
- ownership tinting
- unit counters
- selected-unit and valid-target overlays

The raster remains decorative. Routes, ownership, labels, counters, selection, valid targets, and replay state continue to come from structured content and campaign state.

## Add-Theatre Procedure

1. Define the theatre brief.
   Capture scope, period, playable sides, core regions, victory goals, and visual mood.

2. Author `theatre.json` and `map.json`.
   Create the manifest plus stable region IDs, kinds, anchor positions, terrain, features, and costed routes. Keep this graph playable before making it beautiful.

3. Add scenarios, units, and reserves.
   Create `scenarios/<scenarioId>.json`, `units.json`, and `reserves.json`. Validate starting units, reserve references, eligible deployment positions, and both sides' resources.

4. Add theatre-specific tensions.
   Create `tension-cards.json` with effects and selectors that make sense for the theatre.

5. Add a first visual pass.
   Generate or assemble parched/staff-map assets, record them in `map-assets.json`, then create `map-display.json` for asset IDs, visual geometry, and label/counter placement. Do not change Engine rules to fit art.

6. Validate the complete package.
   Check manifest paths, coordinates, costed routes, display regions, asset IDs/files, unit starts, reserves, events/effects, tension selectors, and victory rules with precise file/field/ID errors.

7. Run engine/API smoke tests.
   Create a campaign, submit at least one valid command, resolve a turn, inspect events, and choose an active tension option when available.

8. Run visual smoke tests.
   Verify the theatre loads in the UI, labels fit, counters do not overlap badly, valid targets are obvious, and the map remains usable at desktop and mobile widths.

## Rework Avoidance Rules

- Keep `SandTable.Engine` independent from display geometry and assets.
- Keep UI components dependent on game/runtime view models, not raw HTTP endpoints or database rows.
- Keep raster backgrounds decorative and replaceable.
- Keep labels, counters, ownership, events, and command affordances as live overlays.
- Keep all generated asset prompts and source notes with the theatre content.
- Add a validation step before relying on new theatre content in UI or tests.
