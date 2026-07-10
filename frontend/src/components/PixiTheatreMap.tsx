import { useEffect, useRef, useState } from "react";
import { Application, Assets, Container, Graphics, Sprite, Text, Texture } from "pixi.js";
import type {
  CampaignEvent,
  CampaignStateResponse,
  MapDefinition,
  MapDisplayDefinition,
  RegionDefinition,
  Side,
  UnitState
} from "../runtime/types";

interface PixiTheatreMapProps {
  map: MapDefinition;
  display?: MapDisplayDefinition | null;
  state: CampaignStateResponse;
  selectedUnitId: string | null;
  selectedUnitRegionId: string | null;
  selectedTargetRegionId: string | null;
  validTargetIds: string[];
  plannedUnitIds: string[];
  replayEvent: CampaignEvent | null;
  onUnitSelect(unitId: string): void;
  onStackSelect(regionId: string): void;
  onRegionSelect(regionId: string): void;
}

interface PlayableRoute {
  from: RegionDefinition;
  to: RegionDefinition;
  routeType: string;
}

interface ViewportTransform {
  scale: number;
  x: number;
  y: number;
}

interface PixiSceneLayers {
  world: Container;
  background: Container;
  routes: Container;
  regionHits: Container;
  mode: Container;
  replay: Container;
  labels: Container;
  units: Container;
}

interface ResolvedRegionDisplay {
  labelOffset: { x: number; y: number };
  hitArea: { rx: number; ry: number };
  counterAnchor: { x: number; y: number };
  stackDirection: string;
}

interface LoadedBackgroundAsset {
  url: string;
  texture: Texture;
}

const UNIT_COUNTER_WIDTH = 40;
const UNIT_COUNTER_HEIGHT = 34;
const REGION_HIT_RADIUS_X = 44;
const REGION_HIT_RADIUS_Y = 30;
const DEFAULT_REGION_DISPLAY: ResolvedRegionDisplay = {
  labelOffset: { x: 0, y: -34 },
  hitArea: { rx: REGION_HIT_RADIUS_X, ry: REGION_HIT_RADIUS_Y },
  counterAnchor: { x: 0, y: 46 },
  stackDirection: "row"
};

export function PixiTheatreMap({
  map,
  display,
  state,
  selectedUnitId,
  selectedUnitRegionId,
  selectedTargetRegionId,
  validTargetIds,
  plannedUnitIds,
  replayEvent,
  onUnitSelect,
  onStackSelect,
  onRegionSelect
}: PixiTheatreMapProps) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const appRef = useRef<Application | null>(null);
  const layersRef = useRef<PixiSceneLayers | null>(null);
  const latestPropsRef = useRef<PixiTheatreMapProps | null>(null);
  const staticWorldKeyRef = useRef<string | null>(null);
  const backgroundAssetRef = useRef<LoadedBackgroundAsset | null>(null);
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) {
      return;
    }

    let cancelled = false;
    let initialized = false;
    const app = new Application();

    void app.init({
      antialias: true,
      autoDensity: true,
      backgroundAlpha: 0,
      resolution: Math.min(window.devicePixelRatio || 1, 2)
    }).then(() => {
      initialized = true;
      if (cancelled) {
        app.destroy(true, { children: true });
        return;
      }

      app.canvas.className = "pixi-theatre-map-canvas";
      host.appendChild(app.canvas);
      appRef.current = app;
      const layers = createSceneLayers(app);
      layersRef.current = layers;
      setIsReady(true);
      const latestProps = latestPropsRef.current;
      if (latestProps) {
        renderPixiScene(host, app, layers, latestProps, staticWorldKeyRef, backgroundAssetRef);
      }
    });

    return () => {
      cancelled = true;
      setIsReady(false);
      appRef.current = null;
      layersRef.current = null;
      latestPropsRef.current = null;
      staticWorldKeyRef.current = null;
      backgroundAssetRef.current = null;
      if (initialized) {
        app.destroy(true, { children: true });
      }
    };
  }, []);

  useEffect(() => {
    const host = hostRef.current;
    const app = appRef.current;
    if (!host || !app || !isReady) {
      return;
    }

    const resizeObserver = new ResizeObserver(() => {
      const latestProps = latestPropsRef.current;
      const layers = layersRef.current;
      if (latestProps && layers) {
        renderPixiScene(host, app, layers, latestProps, staticWorldKeyRef, backgroundAssetRef);
      }
    });
    resizeObserver.observe(host);
    const latestProps = latestPropsRef.current;
    const layers = layersRef.current;
    if (latestProps && layers) {
      renderPixiScene(host, app, layers, latestProps, staticWorldKeyRef, backgroundAssetRef);
    }

    return () => resizeObserver.disconnect();
  }, [isReady, map]);

  useEffect(() => {
    const backgroundUrl = display?.backgroundImage?.url;
    backgroundAssetRef.current = null;
    staticWorldKeyRef.current = null;
    if (!backgroundUrl || !isReady) {
      return;
    }

    let cancelled = false;
    void Assets.load<Texture>(backgroundUrl).then((texture) => {
      if (cancelled) {
        return;
      }

      backgroundAssetRef.current = { url: backgroundUrl, texture };
      staticWorldKeyRef.current = null;

      const host = hostRef.current;
      const app = appRef.current;
      const layers = layersRef.current;
      const latestProps = latestPropsRef.current;
      if (host && app && layers && latestProps) {
        renderPixiScene(host, app, layers, latestProps, staticWorldKeyRef, backgroundAssetRef);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [display?.backgroundImage?.url, isReady]);

  useEffect(() => {
    const host = hostRef.current;
    const app = appRef.current;
    const layers = layersRef.current;
    if (!host || !app || !layers || !isReady) {
      return;
    }

    const props = {
      map,
      display,
      state,
      selectedUnitId,
      selectedUnitRegionId,
      selectedTargetRegionId,
      validTargetIds,
      plannedUnitIds,
      replayEvent,
      onUnitSelect,
      onStackSelect,
      onRegionSelect
    };
    latestPropsRef.current = props;
    renderPixiScene(host, app, layers, props, staticWorldKeyRef, backgroundAssetRef);
  }, [
    isReady,
    map,
    display,
    state,
    selectedUnitId,
    selectedUnitRegionId,
    selectedTargetRegionId,
    validTargetIds,
    plannedUnitIds,
    replayEvent,
    onUnitSelect,
    onStackSelect,
    onRegionSelect
  ]);

  return (
    <div
      ref={hostRef}
      className="pixi-theatre-map"
      role="img"
      aria-label={`${map.name} theatre map`}
    />
  );
}

function createSceneLayers(app: Application): PixiSceneLayers {
  const world = new Container();
  world.label = "theatre-map-world";
  world.eventMode = "passive";

  const background = createLayer("theatre-map-background");
  const routes = createLayer("theatre-map-routes");
  const regionHits = createLayer("theatre-map-region-hits");
  const mode = createLayer("theatre-map-mode");
  const replay = createLayer("theatre-map-replay");
  const labels = createLayer("theatre-map-labels");
  const units = createLayer("theatre-map-units");

  world.addChild(background, routes, regionHits, mode, replay);
  app.stage.addChild(world, labels, units);

  return { world, background, routes, regionHits, mode, replay, labels, units };
}

function createLayer(label: string) {
  const layer = new Container();
  layer.label = label;
  layer.eventMode = "passive";
  return layer;
}

function renderPixiScene(
  host: HTMLDivElement,
  app: Application,
  layers: PixiSceneLayers,
  props: PixiTheatreMapProps,
  staticWorldKeyRef: { current: string | null },
  backgroundAssetRef: { current: LoadedBackgroundAsset | null }
) {
  const transform = fitViewport(host, app, layers.world, props.map);
  const regionStates = new Map(props.state.regions.map((region) => [region.id, region]));
  const routes = resolvePlayableRoutes(props.map);
  const staticWorldKey = resolveStaticWorldKey(props, regionStates, backgroundAssetRef.current);

  if (staticWorldKeyRef.current !== staticWorldKey) {
    clearLayer(layers.background);
    clearLayer(layers.routes);
    drawBackground(layers.background, props.map, props.display, backgroundAssetRef.current);
    drawRoutes(layers.routes, routes, props, { highlightsOnly: false });
    staticWorldKeyRef.current = staticWorldKey;
  }

  clearLayer(layers.regionHits);
  drawRegionHitAreas(layers.regionHits, props.map, regionStates, props);

  clearLayer(layers.mode);
  drawRoutes(layers.mode, routes, props, { highlightsOnly: true });
  drawRegionModeOverlays(layers.mode, props.map, props);

  clearLayer(layers.replay);
  drawReplayOverlay(layers.replay, props.map, props.replayEvent);

  clearLayer(layers.labels);
  clearLayer(layers.units);
  drawScreenOverlay(layers, transform, props);
}

function drawReplayOverlay(viewport: Container, map: MapDefinition, event: CampaignEvent | null) {
  if (!event || (event.eventType !== "Movement" && event.eventType !== "Battle")) {
    return;
  }

  const fromRegionId = payloadString(event.payload, "fromRegionId");
  const toRegionId = payloadString(event.payload, "toRegionId") ?? event.regionId;
  const from = map.regions.find((region) => region.id === fromRegionId);
  const to = map.regions.find((region) => region.id === toRegionId);
  if (!from || !to) {
    return;
  }

  const attack = event.eventType === "Battle";
  const color = attack ? 0xf0604d : 0xf6d06f;
  const dx = to.position.x - from.position.x;
  const dy = to.position.y - from.position.y;
  const length = Math.max(1, Math.hypot(dx, dy));
  const normalX = dx / length;
  const normalY = dy / length;
  const arrowSize = attack ? 18 : 14;
  const overlay = new Graphics();
  overlay
    .circle(from.position.x, from.position.y, 12)
    .stroke({ width: 4, color, alpha: 0.95 })
    .moveTo(from.position.x, from.position.y)
    .lineTo(to.position.x, to.position.y)
    .stroke({ width: attack ? 7 : 6, color: 0x100d08, alpha: 0.72, cap: "round" })
    .moveTo(from.position.x, from.position.y)
    .lineTo(to.position.x, to.position.y)
    .stroke({ width: attack ? 3.5 : 3, color, alpha: 0.98, cap: "round" })
    .moveTo(to.position.x, to.position.y)
    .lineTo(
      to.position.x - normalX * arrowSize - normalY * arrowSize * 0.55,
      to.position.y - normalY * arrowSize + normalX * arrowSize * 0.55)
    .lineTo(
      to.position.x - normalX * arrowSize + normalY * arrowSize * 0.55,
      to.position.y - normalY * arrowSize - normalX * arrowSize * 0.55)
    .closePath()
    .fill({ color, alpha: 0.98 });

  if (attack) {
    overlay.circle(to.position.x, to.position.y, 22).stroke({ width: 3, color, alpha: 0.9 });
    overlay
      .moveTo(to.position.x - 28, to.position.y)
      .lineTo(to.position.x + 28, to.position.y)
      .moveTo(to.position.x, to.position.y - 28)
      .lineTo(to.position.x, to.position.y + 28)
      .stroke({ width: 2, color, alpha: 0.78 });
  }

  viewport.addChild(overlay);
}

function payloadString(payload: Record<string, unknown>, key: string) {
  const value = payload[key];
  return typeof value === "string" ? value : null;
}

function clearLayer(layer: Container) {
  for (const child of layer.removeChildren()) {
    child.destroy({ children: true });
  }
}

function resolveStaticWorldKey(
  props: PixiTheatreMapProps,
  regionStates: Map<string, { owner: Side; victoryPoints: number; features: string[] }>,
  backgroundAsset: LoadedBackgroundAsset | null
) {
  return JSON.stringify({
    theatreId: props.map.theatreId,
    size: props.map.coordinateSystem,
    background: props.display?.backgroundImage?.url ?? null,
    backgroundReady: backgroundAsset?.url === props.display?.backgroundImage?.url,
    routes: props.map.routes,
    owners: props.map.regions.map((region) => ({
      id: region.id,
      owner: regionStates.get(region.id)?.owner ?? region.owner
    }))
  });
}

function fitViewport(host: HTMLDivElement, app: Application, viewport: Container, map: MapDefinition): ViewportTransform {
  const bounds = host.getBoundingClientRect();
  const width = Math.max(1, Math.floor(bounds.width));
  const height = Math.max(1, Math.floor(bounds.height));
  app.renderer.resize(width, height);

  const containScale = Math.min(width / map.coordinateSystem.width, height / map.coordinateSystem.height);
  const scale = containScale;
  const x = (width - map.coordinateSystem.width * scale) / 2;
  const y = (height - map.coordinateSystem.height * scale) / 2;
  viewport.scale.set(scale);
  viewport.position.set(x, y);
  return { scale, x, y };
}

function drawBackground(
  viewport: Container,
  map: MapDefinition,
  display: MapDisplayDefinition | null | undefined,
  backgroundAsset: LoadedBackgroundAsset | null
) {
  const { width, height } = map.coordinateSystem;
  const backgroundUrl = display?.backgroundImage?.url;

  if (backgroundUrl && backgroundAsset?.url === backgroundUrl) {
    const background = new Sprite(backgroundAsset.texture);
    background.x = 0;
    background.y = 0;
    background.width = width;
    background.height = height;
    viewport.addChild(background);

    const theatreTone = new Graphics();
    theatreTone
      .rect(0, 0, width, height)
      .fill({ color: 0x2a2015, alpha: 0.08 })
      .stroke({ width: 8, color: 0x120f0b, alpha: 0.38 });
    viewport.addChild(theatreTone);
    return;
  }

  const coastlineY = 118;
  const background = new Graphics();

  background.rect(0, 0, width, height).fill({ color: 0x141613 });
  background
    .moveTo(0, 0)
    .lineTo(width, 0)
    .lineTo(width, coastlineY + 34)
    .bezierCurveTo(width * 0.86, coastlineY + 8, width * 0.72, coastlineY + 50, width * 0.58, coastlineY + 18)
    .bezierCurveTo(width * 0.43, coastlineY - 16, width * 0.3, coastlineY + 34, width * 0.16, coastlineY + 12)
    .bezierCurveTo(width * 0.08, coastlineY, width * 0.04, coastlineY + 18, 0, coastlineY + 6)
    .closePath()
    .fill({ color: 0x84999d });
  background
    .moveTo(0, coastlineY + 6)
    .bezierCurveTo(width * 0.04, coastlineY + 18, width * 0.08, coastlineY, width * 0.16, coastlineY + 12)
    .bezierCurveTo(width * 0.3, coastlineY + 34, width * 0.43, coastlineY - 16, width * 0.58, coastlineY + 18)
    .bezierCurveTo(width * 0.72, coastlineY + 50, width * 0.86, coastlineY + 8, width, coastlineY + 34)
    .lineTo(width, height)
    .lineTo(0, height)
    .closePath()
    .fill({ color: 0xcaaa62 });

  for (let index = 0; index < 34; index += 1) {
    const x = (index * 97) % width;
    const y = coastlineY + 80 + ((index * 53) % Math.max(1, height - coastlineY - 110));
    const radius = 44 + ((index * 17) % 86);
    background.circle(x, y, radius).fill({
      color: index % 3 === 0 ? 0xe0bd70 : index % 3 === 1 ? 0x9d814b : 0xd2b168,
      alpha: 0.055
    });
  }

  for (let index = 0; index < 22; index += 1) {
    const x = 36 + ((index * 137) % (width - 72));
    const y = coastlineY + 94 + ((index * 71) % Math.max(1, height - coastlineY - 160));
    background
      .moveTo(x, y)
      .bezierCurveTo(x + 22, y - 8, x + 58, y - 8, x + 82, y + 2)
      .stroke({ width: 2, color: 0x8b7041, alpha: 0.1, cap: "round" });
  }

  background
    .moveTo(0, coastlineY + 6)
    .bezierCurveTo(width * 0.04, coastlineY + 18, width * 0.08, coastlineY, width * 0.16, coastlineY + 12)
    .bezierCurveTo(width * 0.3, coastlineY + 34, width * 0.43, coastlineY - 16, width * 0.58, coastlineY + 18)
    .bezierCurveTo(width * 0.72, coastlineY + 50, width * 0.86, coastlineY + 8, width, coastlineY + 34)
    .stroke({ width: 3, color: 0x3d3425, alpha: 0.42, cap: "round", join: "round" });

  background
    .moveTo(255, 470)
    .lineTo(273, 435)
    .lineTo(291, 470)
    .moveTo(292, 470)
    .lineTo(307, 442)
    .lineTo(322, 470)
    .moveTo(430, 545)
    .lineTo(448, 513)
    .lineTo(466, 545)
    .moveTo(575, 390)
    .bezierCurveTo(617, 372, 659, 378, 701, 397)
    .moveTo(810, 405)
    .bezierCurveTo(855, 393, 898, 399, 940, 421)
    .stroke({ width: 3, color: 0x4d3d23, alpha: 0.16, cap: "round", join: "round" });

  viewport.addChild(background);
}

function drawRoutes(
  viewport: Container,
  routes: PlayableRoute[],
  props: PixiTheatreMapProps,
  options: { highlightsOnly: boolean }
) {
  for (const route of routes) {
    const selected = Boolean(
      props.selectedUnitRegionId &&
        (route.from.id === props.selectedUnitRegionId || route.to.id === props.selectedUnitRegionId) &&
        (props.validTargetIds.includes(route.from.id) || props.validTargetIds.includes(route.to.id))
    );
    const target = Boolean(
      props.selectedTargetRegionId &&
        (route.from.id === props.selectedTargetRegionId || route.to.id === props.selectedTargetRegionId) &&
        (route.from.id === props.selectedUnitRegionId || route.to.id === props.selectedUnitRegionId)
    );
    if (options.highlightsOnly && !selected && !target) {
      continue;
    }

    const emphasizedSelected = options.highlightsOnly ? selected : false;
    const emphasizedTarget = options.highlightsOnly ? target : false;
    const routeStyle = routeStyleFor(route.routeType, emphasizedSelected, emphasizedTarget);
    const routeGraphic = new Graphics();

    routeGraphic
      .moveTo(route.from.position.x, route.from.position.y)
      .lineTo(route.to.position.x, route.to.position.y)
      .stroke({ width: routeStyle.width + 3, color: 0x20170f, alpha: routeStyle.shadowAlpha, cap: "round" });
    routeGraphic
      .moveTo(route.from.position.x, route.from.position.y)
      .lineTo(route.to.position.x, route.to.position.y)
      .stroke({ width: routeStyle.width, color: routeStyle.color, alpha: routeStyle.alpha, cap: "round" });

    if (route.routeType === "Railroad") {
      drawRailTicks(routeGraphic, route.from, route.to, emphasizedSelected || emphasizedTarget);
    }

    viewport.addChild(routeGraphic);
  }
}

function drawRegionHitAreas(
  viewport: Container,
  map: MapDefinition,
  regionStates: Map<string, { owner: Side; victoryPoints: number; features: string[] }>,
  props: PixiTheatreMapProps
) {
  for (const region of map.regions) {
    const stateRegion = regionStates.get(region.id);
    const owner = stateRegion?.owner ?? region.owner;
    const ownerColor = colorForSide(owner);
    const regionGraphic = new Graphics();
    const display = resolveRegionDisplay(props.display, region.id);

    regionGraphic
      .ellipse(region.position.x, region.position.y, display.hitArea.rx, display.hitArea.ry)
      .fill({ color: ownerColor.fill, alpha: 0.045 })
      .stroke({
        width: 1.2,
        color: ownerColor.stroke,
        alpha: 0.34
      });
    regionGraphic
      .circle(region.position.x, region.position.y, 4)
      .fill({ color: 0xf2ddb0, alpha: 0.86 })
      .stroke({ width: 1.3, color: 0x2a2116, alpha: 0.62 });
    regionGraphic.eventMode = "static";
    regionGraphic.cursor = "pointer";
    regionGraphic.on("pointertap", () => props.onRegionSelect(region.id));

    viewport.addChild(regionGraphic);
  }
}

function drawRegionModeOverlays(viewport: Container, map: MapDefinition, props: PixiTheatreMapProps) {
  for (const region of map.regions) {
    const valid = props.validTargetIds.includes(region.id);
    const target = props.selectedTargetRegionId === region.id;
    const source = props.selectedUnitRegionId === region.id;
    if (!source && !target && !valid) {
      continue;
    }

    const display = resolveRegionDisplay(props.display, region.id);
    const regionGraphic = new Graphics();
    regionGraphic
      .ellipse(region.position.x, region.position.y, display.hitArea.rx, display.hitArea.ry)
      .fill({ color: source || target ? 0xffefad : 0xf8d77d, alpha: source || target ? 0.15 : 0.1 })
      .stroke({
        width: source || target ? 3 : 2.3,
        color: source || target ? 0xffefad : 0xf8d77d,
        alpha: source || target ? 0.95 : 0.72
      });
    regionGraphic
      .ellipse(region.position.x, region.position.y, display.hitArea.rx + 9, display.hitArea.ry + 7)
      .stroke({
        width: source || target ? 2.3 : 1.5,
        color: source || target ? 0xfff2b8 : 0xf8d77d,
        alpha: source || target ? 0.43 : 0.28
      });

    viewport.addChild(regionGraphic);
  }
}

function drawScreenOverlay(layers: PixiSceneLayers, transform: ViewportTransform, props: PixiTheatreMapProps) {
  const regionDefinitions = new Map(props.map.regions.map((region) => [region.id, region]));
  const regionStates = new Map(props.state.regions.map((region) => [region.id, region]));
  const unitsByRegion = groupUnitsByRegion(props.state.units);

  drawRegionLabels(layers.labels, transform, props.map, regionStates, props);
  drawUnits(layers.units, transform, regionDefinitions, unitsByRegion, props);
}

function drawRegionLabels(
  viewport: Container,
  transform: ViewportTransform,
  map: MapDefinition,
  regionStates: Map<string, { owner: Side; victoryPoints: number; features: string[] }>,
  props: PixiTheatreMapProps
) {
  const labelLayer = new Container();

  for (const region of map.regions) {
    const stateRegion = regionStates.get(region.id);
    const source = props.selectedUnitRegionId === region.id;
    const target = props.selectedTargetRegionId === region.id;
    const victoryPoints = stateRegion?.victoryPoints ?? region.victoryPoints;
    const label = createRegionLabel(region, victoryPoints, source || target, resolveLabelPriority(region, victoryPoints));
    const display = resolveRegionDisplay(props.display, region.id);
    const position = worldToScreen(
      transform,
      region.position.x + display.labelOffset.x,
      region.position.y + display.labelOffset.y
    );
    label.position.set(position.x, position.y);
    label.eventMode = "static";
    label.cursor = "pointer";
    label.on("pointertap", () => props.onRegionSelect(region.id));
    labelLayer.addChild(label);
  }

  viewport.addChild(labelLayer);
}

function drawUnits(
  viewport: Container,
  transform: ViewportTransform,
  regionDefinitions: Map<string, RegionDefinition>,
  unitsByRegion: Map<string, UnitState[]>,
  props: PixiTheatreMapProps
) {
  const unitLayer = new Container();

  for (const [regionId, units] of unitsByRegion.entries()) {
    const region = regionDefinitions.get(regionId);
    if (!region) {
      continue;
    }

    const display = resolveRegionDisplay(props.display, region.id);
    const anchorX = region.position.x + display.counterAnchor.x;
    const anchorY = region.position.y + display.counterAnchor.y;
    const stackPosition = worldToScreen(transform, anchorX, anchorY);
    const visibleUnits = resolveVisibleStackUnits(units, props.selectedUnitId);

    if (units.length > 1) {
      const stackBackplates = createStackBackplates(visibleUnits[0].side, units.length, display.stackDirection);
      stackBackplates.position.set(stackPosition.x, stackPosition.y);
      stackBackplates.eventMode = "static";
      stackBackplates.cursor = "pointer";
      stackBackplates.on("pointertap", (event) => {
        event.stopPropagation();
        props.onStackSelect(region.id);
      });
      unitLayer.addChild(stackBackplates);
    }

    for (const unit of visibleUnits) {
      const counter = createUnitCounter(unit, {
        selected: unit.id === props.selectedUnitId,
        planned: props.plannedUnitIds.includes(unit.id)
      });
      counter.position.set(stackPosition.x, stackPosition.y);
      counter.eventMode = "static";
      counter.cursor = "pointer";
      counter.on("pointertap", (event) => {
        event.stopPropagation();
        props.onUnitSelect(unit.id);
        if (units.length > 1) {
          props.onStackSelect(region.id);
        }
      });
      unitLayer.addChild(counter);
    }

    if (units.length > 1) {
      const countBadge = createStackCountBadge(units.length);
      countBadge.position.set(
        stackPosition.x + UNIT_COUNTER_WIDTH / 2 - 1,
        stackPosition.y - UNIT_COUNTER_HEIGHT / 2 + 1
      );
      countBadge.eventMode = "static";
      countBadge.cursor = "pointer";
      countBadge.on("pointertap", (event) => {
        event.stopPropagation();
        props.onStackSelect(region.id);
      });
      unitLayer.addChild(countBadge);
    }
  }

  viewport.addChild(unitLayer);
}

function worldToScreen(transform: ViewportTransform, x: number, y: number) {
  return {
    x: transform.x + x * transform.scale,
    y: transform.y + y * transform.scale
  };
}

function createRegionLabel(
  region: RegionDefinition,
  victoryPoints: number,
  emphasized: boolean,
  priority: "primary" | "secondary" | "minor"
) {
  const label = new Container();
  const primary = priority === "primary";
  const minor = priority === "minor";
  const name = new Text({
    text: splitRegionName(region.name).join("\n"),
    style: {
      align: "center",
      fill: 0x17120c,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: primary ? 14 : minor ? 10 : 12,
      fontWeight: primary ? "900" : "800",
      lineHeight: primary ? 15 : minor ? 11 : 13,
      stroke: { color: 0xf5e2b2, width: 1 }
    },
    anchor: 0.5
  });
  const vp = new Text({
    text: `${victoryPoints} VP`,
    style: {
      fill: 0xffe7a0,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: primary ? 10 : 9,
      fontWeight: "900",
      stroke: { color: 0x24190e, width: 2 }
    },
    anchor: 0.5
  });

  const backing = new Graphics();
  const backingWidth = Math.max(primary ? 80 : minor ? 58 : 70, name.width + (minor ? 12 : 20));
  const backingHeight = name.height + (minor ? 8 : 14);
  backing
    .roundRect(-backingWidth / 2, -backingHeight / 2, backingWidth, backingHeight, 5)
    .fill({ color: emphasized ? 0xffe2a0 : 0xf1d08a, alpha: emphasized ? 0.62 : primary ? 0.46 : minor ? 0.24 : 0.34 })
    .stroke({ width: primary || emphasized ? 1.2 : 0.8, color: 0x352614, alpha: emphasized ? 0.52 : primary ? 0.3 : 0.18 });

  name.position.set(0, -2);
  vp.position.set(0, backingHeight / 2 + 7);
  label.addChild(backing, name, vp);
  return label;
}

function resolveLabelPriority(region: RegionDefinition, victoryPoints: number): "primary" | "secondary" | "minor" {
  if (victoryPoints >= 6 || region.features.includes("SupplyDepot")) {
    return "primary";
  }

  if (victoryPoints <= 2 && region.features.length === 0) {
    return "minor";
  }

  return "secondary";
}

function createUnitCounter(unit: UnitState, state: { selected: boolean; planned: boolean }) {
  const counter = new Container();
  const colors = colorForSide(unit.side);
  const textColor = unit.side === "Allies" ? 0xf4fbff : 0x17120c;
  const headerTextColor = unit.side === "Allies" ? 0xf4fbff : 0xf8e6b4;
  const body = new Graphics();
  const width = UNIT_COUNTER_WIDTH;
  const height = UNIT_COUNTER_HEIGHT;

  const shadow = new Graphics();
  shadow
    .roundRect(-width / 2 + 2, -height / 2 + 3, width, height, 4)
    .fill({ color: 0x060503, alpha: 0.38 });

  if (state.selected) {
    const glow = new Graphics();
    glow
      .roundRect(-width / 2 - 4, -height / 2 - 4, width + 8, height + 8, 7)
      .fill({ color: 0xffe9a6, alpha: 0.1 })
      .stroke({ width: 2.2, color: 0xfff1a8, alpha: 0.92 });
    counter.addChild(glow);
  }

  body
    .roundRect(-width / 2, -height / 2, width, height, 4)
    .fill({ color: colors.counterFill, alpha: 1 })
    .stroke({
      width: state.selected ? 2.5 : 1.6,
      color: state.selected ? 0xfff1a8 : colors.counterStroke,
      alpha: 1
    });
  body
    .roundRect(-width / 2 + 3, -height / 2 + 3, width - 6, 11, 3)
    .fill({ color: colors.counterHeaderFill, alpha: 0.94 });
  body
    .moveTo(-width / 2 + 5, -height / 2 + 15)
    .lineTo(width / 2 - 5, -height / 2 + 15)
    .stroke({ width: 1.2, color: colors.counterStroke, alpha: 0.5 });

  if (state.planned) {
    body
      .roundRect(-width / 2 - 3, -height / 2 - 3, width + 6, height + 6, 7)
      .stroke({ width: 2, color: 0xffdf7f, alpha: 0.82 });
    body
      .moveTo(width / 2 - 11, -height / 2 - 3)
      .lineTo(width / 2 + 3, -height / 2 - 3)
      .lineTo(width / 2 + 3, -height / 2 + 11)
      .closePath()
      .fill({ color: 0xffdf7f, alpha: 0.9 });
  }

  const code = new Text({
    text: unitCode(unit),
    style: {
      fill: headerTextColor,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 7,
      fontWeight: "900"
    },
    anchor: 0.5
  });
  code.position.set(0, -11);

  const strength = new Text({
    text: String(unit.strength),
    style: {
      fill: textColor,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 14,
      fontWeight: "900",
      stroke: { color: unit.side === "Allies" ? 0x163044 : 0xe2c271, width: 1 }
    },
    anchor: 0.5
  });
  strength.position.set(0, 10);

  const symbol = createUnitTypeSymbol(unit, textColor);
  symbol.position.set(0, 0);

  counter.addChild(shadow, body, code, symbol, strength);
  if (unit.status === "Disrupted") {
    const status = new Graphics();
    status
      .moveTo(-width / 2 + 5, height / 2 - 5)
      .lineTo(width / 2 - 5, -height / 2 + 18)
      .stroke({ width: 2.2, color: 0x8b1d16, alpha: 0.9, cap: "round" });
    const statusText = new Text({
      text: "D",
      style: {
        fill: 0xffeee0,
        fontFamily: "Arial, Helvetica, sans-serif",
        fontSize: 8,
        fontWeight: "900"
      },
      anchor: 0.5
    });
    statusText.position.set(width / 2 - 6, height / 2 - 6);
    counter.addChild(status, statusText);
  }
  return counter;
}

function createUnitTypeSymbol(unit: UnitState, color: number) {
  const symbol = new Graphics();
  if (unit.type === "Armour") {
    symbol
      .roundRect(-8, -3.5, 16, 7, 4)
      .stroke({ width: 1.6, color, alpha: 0.9 })
      .moveTo(-4.5, 0)
      .lineTo(4.5, 0)
      .stroke({ width: 1.3, color, alpha: 0.8, cap: "round" });
    return symbol;
  }

  if (unit.type === "Infantry") {
    symbol
      .moveTo(-6, -4.5)
      .lineTo(6, 4.5)
      .moveTo(6, -4.5)
      .lineTo(-6, 4.5)
      .stroke({ width: 1.7, color, alpha: 0.9, cap: "round" });
    return symbol;
  }

  if (unit.type === "Logistics") {
    symbol
      .rect(-6.5, -4.5, 13, 9)
      .stroke({ width: 1.6, color, alpha: 0.9 })
      .moveTo(-2.5, -4.5)
      .lineTo(-2.5, 4.5)
      .moveTo(2.5, -4.5)
      .lineTo(2.5, 4.5)
      .stroke({ width: 1.2, color, alpha: 0.65 });
    return symbol;
  }

  symbol
    .moveTo(-8, 1)
    .lineTo(0, -4.5)
    .lineTo(8, 1)
    .moveTo(-5.5, 4)
    .lineTo(0, 0)
    .lineTo(5.5, 4)
    .stroke({ width: 1.7, color, alpha: 0.9, cap: "round", join: "round" });
  return symbol;
}

function createStackBackplates(side: Side, count: number, stackDirection: string) {
  const stack = new Container();
  const colors = colorForSide(side);
  const visiblePlateCount = Math.min(count - 1, 3);
  const direction = stackDirection === "column" ? { x: 0, y: 4 } : { x: -4, y: 3 };

  for (let index = visiblePlateCount; index >= 1; index -= 1) {
    const offsetX = direction.x * index;
    const offsetY = direction.y * index;
    const plate = new Graphics();
    plate
      .roundRect(
        -UNIT_COUNTER_WIDTH / 2 + offsetX,
        -UNIT_COUNTER_HEIGHT / 2 + offsetY,
        UNIT_COUNTER_WIDTH,
        UNIT_COUNTER_HEIGHT,
        4
      )
      .fill({ color: colors.counterFill, alpha: 0.82 })
      .stroke({ width: 1.2, color: colors.counterStroke, alpha: 0.78 });
    stack.addChild(plate);
  }

  return stack;
}

function createStackCountBadge(count: number) {
  const badge = new Container();
  const body = new Graphics();
  body
    .circle(0, 0, 9)
    .fill({ color: 0x17120b, alpha: 0.92 })
    .stroke({ width: 1.4, color: 0xf3d99a, alpha: 0.82 });
  const text = new Text({
    text: String(count),
    style: {
      fill: 0xffe7a0,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 10,
      fontWeight: "900"
    },
    anchor: 0.5
  });
  badge.addChild(body, text);
  return badge;
}

function drawRailTicks(graphic: Graphics, from: RegionDefinition, to: RegionDefinition, emphasized: boolean) {
  const dx = to.position.x - from.position.x;
  const dy = to.position.y - from.position.y;
  const length = Math.hypot(dx, dy);
  const normalX = -dy / length;
  const normalY = dx / length;
  const tickCount = Math.max(2, Math.floor(length / 26));

  for (let index = 1; index < tickCount; index += 1) {
    const t = index / tickCount;
    const x = from.position.x + dx * t;
    const y = from.position.y + dy * t;
    graphic
      .moveTo(x - normalX * 5, y - normalY * 5)
      .lineTo(x + normalX * 5, y + normalY * 5)
      .stroke({ width: 2, color: emphasized ? 0xfff1b8 : 0x2f2920, alpha: emphasized ? 0.92 : 0.56, cap: "round" });
  }
}

function routeStyleFor(routeType: string, selected: boolean, target: boolean) {
  if (target) {
    return { width: 5.5, color: 0xfff1b8, alpha: 0.96, shadowAlpha: 0.42 };
  }

  if (selected) {
    return { width: 4.2, color: 0xf8d77d, alpha: 0.9, shadowAlpha: 0.32 };
  }

  if (routeType === "Railroad") {
    return { width: 3, color: 0x2f2920, alpha: 0.48, shadowAlpha: 0.12 };
  }

  if (routeType === "DesertTrack" || routeType === "OperationalRoute") {
    return { width: 2.4, color: 0x574329, alpha: 0.34, shadowAlpha: 0.08 };
  }

  return { width: 3, color: 0x503927, alpha: 0.46, shadowAlpha: 0.12 };
}

function colorForSide(side: Side) {
  if (side === "Axis") {
    return {
      fill: 0xc9993e,
      stroke: 0x75511d,
      counterFill: 0xc2a150,
      counterHeaderFill: 0x3a2a13,
      counterStroke: 0x302614
    };
  }

  if (side === "Allies") {
    return {
      fill: 0x517f96,
      stroke: 0x244a60,
      counterFill: 0x2f6d93,
      counterHeaderFill: 0x17364f,
      counterStroke: 0xc7d8df
    };
  }

  return {
    fill: 0xb8aa83,
    stroke: 0x766f5b,
    counterFill: 0xb8aa83,
    counterHeaderFill: 0x504936,
    counterStroke: 0x3d382c
  };
}

function resolvePlayableRoutes(map: MapDefinition) {
  const regionsById = new Map(map.regions.map((region) => [region.id, region]));
  return map.routes.flatMap((route) => {
    const from = regionsById.get(route.fromRegionId);
    const to = regionsById.get(route.toRegionId);
    return from && to ? [{ from, to, routeType: route.routeType }] : [];
  });
}

function resolveVisibleStackUnits(units: UnitState[], selectedUnitId: string | null) {
  if (units.length <= 1) {
    return units;
  }

  const selectedUnit = units.find((unit) => unit.id === selectedUnitId);
  return [selectedUnit ?? units[0]];
}

function resolveRegionDisplay(display: MapDisplayDefinition | null | undefined, regionId: string): ResolvedRegionDisplay {
  const authored = display?.regions?.[regionId];
  return {
    labelOffset: authored?.labelOffset ?? DEFAULT_REGION_DISPLAY.labelOffset,
    hitArea: authored?.hitArea ?? DEFAULT_REGION_DISPLAY.hitArea,
    counterAnchor: authored?.counterAnchor ?? DEFAULT_REGION_DISPLAY.counterAnchor,
    stackDirection: authored?.stackDirection ?? DEFAULT_REGION_DISPLAY.stackDirection
  };
}

function splitRegionName(name: string) {
  const words = name.split(" ");
  if (words.length < 2 || name.length <= 11) {
    return [name];
  }

  const midpoint = Math.ceil(words.length / 2);
  return [
    words.slice(0, midpoint).join(" "),
    words.slice(midpoint).join(" ")
  ];
}

function groupUnitsByRegion(units: UnitState[]) {
  const grouped = new Map<string, UnitState[]>();
  for (const unit of units.filter((item) => item.status !== "Destroyed")) {
    const regionUnits = grouped.get(unit.regionId) ?? [];
    regionUnits.push(unit);
    grouped.set(unit.regionId, regionUnits);
  }
  return grouped;
}

function unitCode(unit: UnitState) {
  if (unit.type === "Armour") {
    return "ARM";
  }
  if (unit.type === "Infantry") {
    return "INF";
  }
  if (unit.type === "Logistics") {
    return "LOG";
  }
  return "AIR";
}
