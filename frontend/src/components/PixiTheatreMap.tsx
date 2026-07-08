import { useEffect, useRef, useState } from "react";
import { Application, Assets, Container, Graphics, Sprite, Text } from "pixi.js";
import type {
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
  onUnitSelect(unitId: string): void;
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

interface ResolvedRegionDisplay {
  labelOffset: { x: number; y: number };
  hitArea: { rx: number; ry: number };
  counterAnchor: { x: number; y: number };
  stackDirection: string;
}

const UNIT_COUNTER_WIDTH = 38;
const UNIT_COUNTER_HEIGHT = 32;
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
  onUnitSelect,
  onRegionSelect
}: PixiTheatreMapProps) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const appRef = useRef<Application | null>(null);
  const latestPropsRef = useRef<PixiTheatreMapProps | null>(null);
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
      setIsReady(true);
      const latestProps = latestPropsRef.current;
      if (latestProps) {
        renderPixiScene(host, app, latestProps);
      }
    });

    return () => {
      cancelled = true;
      setIsReady(false);
      appRef.current = null;
      latestPropsRef.current = null;
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
      if (latestProps) {
        renderPixiScene(host, app, latestProps);
      }
    });
    resizeObserver.observe(host);
    const latestProps = latestPropsRef.current;
    if (latestProps) {
      renderPixiScene(host, app, latestProps);
    }

    return () => resizeObserver.disconnect();
  }, [isReady, map]);

  useEffect(() => {
    const backgroundUrl = display?.backgroundImage?.url;
    if (!backgroundUrl || !isReady) {
      return;
    }

    let cancelled = false;
    void Assets.load(backgroundUrl).then(() => {
      if (cancelled) {
        return;
      }

      const host = hostRef.current;
      const app = appRef.current;
      const latestProps = latestPropsRef.current;
      if (host && app && latestProps) {
        renderPixiScene(host, app, latestProps);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [display?.backgroundImage?.url, isReady]);

  useEffect(() => {
    const host = hostRef.current;
    const app = appRef.current;
    if (!host || !app || !isReady) {
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
      onUnitSelect,
      onRegionSelect
    };
    latestPropsRef.current = props;
    renderPixiScene(host, app, props);
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
    onUnitSelect,
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

function renderPixiScene(host: HTMLDivElement, app: Application, props: PixiTheatreMapProps) {
  for (const child of app.stage.removeChildren()) {
    child.destroy({ children: true });
  }

  const worldLayer = new Container();
  worldLayer.label = "theatre-map-world";
  worldLayer.eventMode = "passive";
  app.stage.addChild(worldLayer);

  const overlayLayer = new Container();
  overlayLayer.label = "theatre-map-overlay";
  overlayLayer.eventMode = "passive";
  app.stage.addChild(overlayLayer);

  drawWorldScene(worldLayer, props);
  const transform = fitViewport(host, app, worldLayer, props.map);
  drawScreenOverlay(overlayLayer, transform, props);
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

function drawWorldScene(viewport: Container, props: PixiTheatreMapProps) {
  const { map, state } = props;
  const routes = resolvePlayableRoutes(map);
  const regionStates = new Map(state.regions.map((region) => [region.id, region]));

  drawBackground(viewport, map, props.display);
  drawRoutes(viewport, routes, props);
  drawRegionOverlays(viewport, map, regionStates, props);
}

function drawBackground(viewport: Container, map: MapDefinition, display: MapDisplayDefinition | null | undefined) {
  const { width, height } = map.coordinateSystem;
  const backgroundUrl = display?.backgroundImage?.url;

  if (backgroundUrl) {
    const background = Sprite.from(backgroundUrl);
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

function drawRoutes(viewport: Container, routes: PlayableRoute[], props: PixiTheatreMapProps) {
  const routeLayer = new Container();

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
    const routeStyle = routeStyleFor(route.routeType, selected, target);
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
      drawRailTicks(routeGraphic, route.from, route.to, selected || target);
    }

    routeLayer.addChild(routeGraphic);
  }

  viewport.addChild(routeLayer);
}

function drawRegionOverlays(
  viewport: Container,
  map: MapDefinition,
  regionStates: Map<string, { owner: Side; victoryPoints: number; features: string[] }>,
  props: PixiTheatreMapProps
) {
  const regionLayer = new Container();

  for (const region of map.regions) {
    const stateRegion = regionStates.get(region.id);
    const owner = stateRegion?.owner ?? region.owner;
    const valid = props.validTargetIds.includes(region.id);
    const target = props.selectedTargetRegionId === region.id;
    const source = props.selectedUnitRegionId === region.id;
    const ownerColor = colorForSide(owner);
    const regionGraphic = new Graphics();
    const highlight = source || target || valid;
    const display = resolveRegionDisplay(props.display, region.id);

    regionGraphic
      .ellipse(region.position.x, region.position.y, display.hitArea.rx, display.hitArea.ry)
      .fill({ color: ownerColor.fill, alpha: source || target ? 0.14 : valid ? 0.11 : 0.045 })
      .stroke({
        width: source || target ? 3 : valid ? 2.4 : 1.2,
        color: source || target ? 0xffefad : valid ? 0xf8d77d : ownerColor.stroke,
        alpha: source || target ? 0.9 : valid ? 0.72 : 0.34
      });
    if (highlight) {
      regionGraphic
        .ellipse(region.position.x, region.position.y, display.hitArea.rx + 9, display.hitArea.ry + 7)
        .stroke({
          width: source || target ? 2.4 : 1.5,
          color: source || target ? 0xfff2b8 : 0xf8d77d,
          alpha: source || target ? 0.42 : 0.28
        });
    }
    regionGraphic
      .circle(region.position.x, region.position.y, 4)
      .fill({ color: 0xf2ddb0, alpha: 0.86 })
      .stroke({ width: 1.3, color: 0x2a2116, alpha: 0.62 });
    regionGraphic.eventMode = "static";
    regionGraphic.cursor = "pointer";
    regionGraphic.on("pointertap", () => props.onRegionSelect(region.id));

    regionLayer.addChild(regionGraphic);
  }

  viewport.addChild(regionLayer);
}

function drawScreenOverlay(viewport: Container, transform: ViewportTransform, props: PixiTheatreMapProps) {
  const regionDefinitions = new Map(props.map.regions.map((region) => [region.id, region]));
  const regionStates = new Map(props.state.regions.map((region) => [region.id, region]));
  const unitsByRegion = groupUnitsByRegion(props.state.units);

  drawRegionLabels(viewport, transform, props.map, regionStates, props);
  drawUnits(viewport, transform, regionDefinitions, unitsByRegion, props);
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
    const label = createRegionLabel(region, stateRegion?.victoryPoints ?? region.victoryPoints, source || target);
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
    const slots = resolveUnitSlotPositions(units.length, display.stackDirection);
    const anchorX = region.position.x + display.counterAnchor.x;
    const anchorY = region.position.y + display.counterAnchor.y;
    if (units.length > 1) {
      const stackBase = new Graphics();
      const stackWidth = display.stackDirection === "column"
        ? UNIT_COUNTER_WIDTH + 18
        : Math.min(118, Math.max(54, slots.length * 40 - 2));
      const stackHeight = display.stackDirection === "column"
        ? Math.min(118, Math.max(42, slots.length * 30 + 4))
        : 40;
      const stackPosition = worldToScreen(transform, anchorX, anchorY);
      stackBase
        .roundRect(stackPosition.x - stackWidth / 2, stackPosition.y - stackHeight / 2, stackWidth, stackHeight, 6)
        .fill({ color: 0x12100c, alpha: 0.26 })
        .stroke({ width: 1.2, color: 0xf0d28a, alpha: 0.18 });
      unitLayer.addChild(stackBase);
    }

    for (const [index, unit] of units.slice(0, slots.length).entries()) {
      const slot = slots[index];
      const counter = createUnitCounter(unit, {
        selected: unit.id === props.selectedUnitId,
        planned: props.plannedUnitIds.includes(unit.id)
      });
      const position = worldToScreen(transform, anchorX + slot.x, anchorY + slot.y);
      counter.position.set(position.x, position.y);
      counter.eventMode = "static";
      counter.cursor = "pointer";
      counter.on("pointertap", (event) => {
        event.stopPropagation();
        props.onUnitSelect(unit.id);
      });
      unitLayer.addChild(counter);
    }

    if (units.length > slots.length) {
      const overflow = new Text({
        text: `+${units.length - slots.length}`,
        style: {
          fill: 0xfff0b0,
          fontFamily: "Inter, Arial, sans-serif",
          fontSize: 14,
          fontWeight: "900",
          stroke: { color: 0x20160d, width: 3 }
        },
        anchor: 0.5
      });
      const overflowPosition = worldToScreen(transform, anchorX + 48, anchorY);
      overflow.position.set(overflowPosition.x, overflowPosition.y);
      unitLayer.addChild(overflow);
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

function createRegionLabel(region: RegionDefinition, victoryPoints: number, emphasized: boolean) {
  const label = new Container();
  const name = new Text({
    text: splitRegionName(region.name).join("\n"),
    style: {
      align: "center",
      fill: 0x17120c,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 13,
      fontWeight: "900",
      lineHeight: 14,
      stroke: { color: 0xf5e2b2, width: 1 }
    },
    anchor: 0.5
  });
  const vp = new Text({
    text: `${victoryPoints} VP`,
    style: {
      fill: 0xffe7a0,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 10,
      fontWeight: "900",
      stroke: { color: 0x24190e, width: 2 }
    },
    anchor: 0.5
  });

  const backing = new Graphics();
  const backingWidth = Math.max(76, name.width + 20);
  const backingHeight = name.height + 14;
  backing
    .roundRect(-backingWidth / 2, -backingHeight / 2, backingWidth, backingHeight, 5)
    .fill({ color: emphasized ? 0xffe2a0 : 0xf1d08a, alpha: emphasized ? 0.58 : 0.42 })
    .stroke({ width: 1.2, color: 0x352614, alpha: emphasized ? 0.52 : 0.26 });

  name.position.set(0, -2);
  vp.position.set(0, backingHeight / 2 + 7);
  label.addChild(backing, name, vp);
  return label;
}

function createUnitCounter(unit: UnitState, state: { selected: boolean; planned: boolean }) {
  const counter = new Container();
  const colors = colorForSide(unit.side);
  const body = new Graphics();
  const width = UNIT_COUNTER_WIDTH;
  const height = UNIT_COUNTER_HEIGHT;

  body
    .roundRect(-width / 2, -height / 2, width, height, 5)
    .fill({ color: colors.counterFill, alpha: 1 })
    .stroke({
      width: state.selected ? 3 : 1.8,
      color: state.selected ? 0xfff1a8 : colors.counterStroke,
      alpha: 1
    });

  if (state.planned) {
    body
      .roundRect(-width / 2 - 3, -height / 2 - 3, width + 6, height + 6, 7)
      .stroke({ width: 1.8, color: 0xffdf7f, alpha: 0.72 });
  }

  const code = new Text({
    text: unitCode(unit),
    style: {
      fill: unit.side === "Allies" ? 0xf4fbff : 0x19130b,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 8,
      fontWeight: "900"
    },
    anchor: 0.5
  });
  code.position.set(0, -6);

  const strength = new Text({
    text: String(unit.strength),
    style: {
      fill: unit.side === "Allies" ? 0xf4fbff : 0x19130b,
      fontFamily: "Arial, Helvetica, sans-serif",
      fontSize: 15,
      fontWeight: "900"
    },
    anchor: 0.5
  });
  strength.position.set(0, 8);

  counter.addChild(body, code, strength);
  return counter;
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
      counterFill: 0xb99c56,
      counterStroke: 0x302614
    };
  }

  if (side === "Allies") {
    return {
      fill: 0x517f96,
      stroke: 0x244a60,
      counterFill: 0x315b78,
      counterStroke: 0xc7d8df
    };
  }

  return {
    fill: 0xb8aa83,
    stroke: 0x766f5b,
    counterFill: 0xb8aa83,
    counterStroke: 0x3d382c
  };
}

function resolvePlayableRoutes(map: MapDefinition) {
  const regionsById = new Map(map.regions.map((region) => [region.id, region]));
  const routeTypesByEdge = new Map(map.routes.map((route) => [
    edgeKey(route.fromRegionId, route.toRegionId),
    route.routeType
  ]));

  return map.regions.flatMap((region) =>
    region.adjacentRegionIds.flatMap((adjacentRegionId) => {
      if (region.id > adjacentRegionId) {
        return [];
      }

      const adjacentRegion = regionsById.get(adjacentRegionId);
      if (!adjacentRegion) {
        return [];
      }

      const key = edgeKey(region.id, adjacentRegionId);
      return [{
        from: region,
        to: adjacentRegion,
        routeType: routeTypesByEdge.get(key) ?? "OperationalRoute"
      }];
    })
  );
}

function edgeKey(firstRegionId: string, secondRegionId: string) {
  return [firstRegionId, secondRegionId].sort().join("::");
}

function resolveUnitSlotPositions(count: number, stackDirection: string) {
  if (count <= 1) {
    return [{ x: 0, y: 0 }];
  }

  if (stackDirection === "column") {
    const visibleCount = Math.min(count, 4);
    const startY = -((visibleCount - 1) * 25) / 2;
    return Array.from({ length: visibleCount }, (_, index) => ({ x: 0, y: startY + index * 25 }));
  }

  if (count === 2) {
    return [
      { x: -20, y: 0 },
      { x: 20, y: 0 }
    ];
  }

  if (count === 3) {
    return [
      { x: -39, y: 0 },
      { x: 0, y: 0 },
      { x: 39, y: 0 }
    ];
  }

  return [
    { x: -22, y: -18 },
    { x: 22, y: -18 },
    { x: -22, y: 18 },
    { x: 22, y: 18 }
  ];
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
