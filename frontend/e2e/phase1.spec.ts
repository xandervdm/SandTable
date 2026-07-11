import { expect, test, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";

const contentRoot = path.resolve(process.cwd(), "..", "content", "theatres", "north-africa");
const readContent = (fileName: string) => JSON.parse(readFileSync(path.join(contentRoot, fileName), "utf8"));
const map = readContent("map.json");
const authoredDisplay = readContent("map-display.json");
const display = {
  ...authoredDisplay,
  backgroundImage: {
    url: "/theatres/north-africa/assets/map-base.png",
    fit: authoredDisplay.backgroundImage.fit
  }
};
const scenario = readContent("scenarios/north-africa-1942.json");
const units = readContent("units.json");
const reserves = readContent("reserves.json");
const doctrines = readContent("doctrines.json");
const events = readContent("events.json");
const tensionCards = readContent("tension-cards.json");
const assets = readContent("map-assets.json");
const manifest = readContent("theatre.json");

const campaign = {
  campaignUid: "phase-1-regression",
  name: "Phase 1 Regression Campaign",
  theatreId: map.theatreId,
  scenarioId: scenario.scenarioId,
  playerSide: scenario.defaultSide,
  enemySide: "Allies",
  status: "Active",
  currentTurnNumber: 1,
  maxTurns: scenario.maxTurns,
  currentCampaignDate: scenario.startDate,
  result: null,
  score: null
};

const state = {
  campaign,
  snapshotUid: "phase-1-snapshot",
  turnNumber: 1,
  campaignDate: scenario.startDate,
  resources: scenario.startingResources,
  regions: map.regions.map(({ position: _position, ...region }: { position: unknown; id: string }) => ({
    ...region,
    adjacentRegionIds: map.routes.flatMap((route: { fromRegionId: string; toRegionId: string }) =>
      route.fromRegionId === region.id
        ? [route.toRegionId]
        : route.toRegionId === region.id
          ? [route.fromRegionId]
          : [])
  })),
  routes: map.routes,
  units: units.units.filter((unit: { id: string }) => scenario.startingUnitIds.includes(unit.id)).map((unit: Record<string, unknown>) => ({
    ...unit,
    supplyStatus: "InSupply",
    outOfSupplyTurns: 0,
    isEntrenched: false
  })),
  reserves: [],
  victoryProgress: {},
  scenarioEventHistory: [],
  activeTensions: [],
  tensionHistory: [],
  campaignModifiers: [],
  isComplete: false,
  result: null
};

const viewports = [
  { width: 1280, height: 720 },
  { width: 1440, height: 900 }
];

for (const viewport of viewports) {
  test(`fresh session renders the full Phase 1 command table at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const browserMessages: string[] = [];
    page.on("console", (message) => {
      const browserDriverNoise = message.text().includes("GL Driver Message") && message.text().includes("ReadPixels");
      if (!browserDriverNoise && (message.type() === "warning" || message.type() === "error")) {
        browserMessages.push(`${message.type()}: ${message.text()}`);
      }
    });
    page.on("pageerror", (error) => browserMessages.push(`pageerror: ${error.message}`));
    await mockApi(page);

    const backgroundResponse = page.waitForResponse((response) =>
      response.url().endsWith("/theatres/north-africa/assets/map-base.png") && response.ok()
    );
    await page.goto("/");
    await backgroundResponse;

    const theatreMap = page.getByRole("img", { name: "North Africa theatre map" });
    await expect(theatreMap).toBeVisible();
    await expect(page.locator(".pixi-theatre-map-canvas")).toBeVisible();

    const resources = page.locator(".resource-item");
    await expect(resources).toHaveCount(5);
    for (const resource of await resources.all()) {
      await expect(resource).toBeVisible();
      const bounds = await resource.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds!.x).toBeGreaterThanOrEqual(0);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width);
    }

    // Chromium/WebGL can return a partially composited first capture on Windows.
    await page.screenshot({ animations: "disabled" });
    const screenshot = await page.screenshot({ animations: "disabled" });
    expect(screenshot).toMatchSnapshot(`phase-1-${viewport.width}x${viewport.height}.png`, {
      maxDiffPixelRatio: 0.01
    });

    const mapBounds = await theatreMap.boundingBox();
    expect(mapBounds).not.toBeNull();
    const scale = Math.min(mapBounds!.width / map.coordinateSystem.width, mapBounds!.height / map.coordinateSystem.height);
    const offsetX = (mapBounds!.width - map.coordinateSystem.width * scale) / 2;
    const offsetY = (mapBounds!.height - map.coordinateSystem.height * scale) / 2;
    const tripoli = map.regions.find((region: { id: string }) => region.id === "tripoli");
    const tripoliDisplay = display.regions.tripoli;
    await page.mouse.click(
      mapBounds!.x + offsetX + (tripoli.position.x + tripoliDisplay.counterAnchor.x) * scale,
      mapBounds!.y + offsetY + (tripoli.position.y + tripoliDisplay.counterAnchor.y) * scale
    );

    const stackSelector = page.getByTestId("stack-selector");
    await expect(stackSelector).toBeVisible();
    await expect(stackSelector.getByRole("button")).toHaveCount(3);
    const logistics = page.getByTestId("stack-unit-africa-corps-logistics");
    await logistics.click();
    await expect(logistics).toHaveClass(/selected/);
    expect(browserMessages).toEqual([]);
  });
}

test("Phase 4 command log groups actors, filters battles, and replays persisted events", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const campaignEvents = [
    {
      eventUid: "movement-you",
      campaignTurnUid: "phase-4-turn",
      turnNumber: 1,
      sequence: 1,
      eventType: "Movement",
      eventScope: "Unit",
      side: "Axis",
      actor: "You",
      regionId: "benghazi",
      unitId: "21st-panzer",
      summary: "21st Panzer Division moved to Benghazi.",
      payload: { fromRegionId: "tripoli", toRegionId: "benghazi", objectiveCaptured: false }
    },
    {
      eventUid: "battle-enemy",
      campaignTurnUid: "phase-4-turn",
      turnNumber: 1,
      sequence: 2,
      eventType: "Battle",
      eventScope: "Region",
      side: "Allies",
      actor: "Enemy",
      regionId: "gazala",
      unitId: "7th-armoured",
      summary: "7th Armoured Division attacked at Gazala.",
      payload: { fromRegionId: "tobruk", toRegionId: "gazala", attackerDamage: 1, defenderDamage: 3 }
    },
    {
      eventUid: "tension-system",
      campaignTurnUid: "phase-4-turn",
      turnNumber: 1,
      sequence: 3,
      eventType: "Tension",
      eventScope: "Campaign",
      side: null,
      actor: "System",
      regionId: null,
      unitId: null,
      summary: "Operational opportunity emerged.",
      payload: {}
    }
  ];
  const timeline = createTimeline([
    { player: 100, enemy: 100, playerVp: 20, enemyVp: 52, resolvedTurnNumber: null, markers: [] },
    {
      player: 94,
      enemy: 88,
      playerVp: 24,
      enemyVp: 48,
      resolvedTurnNumber: 1,
      markers: [{
        eventUid: "battle-enemy",
        sequence: 2,
        markerType: "Casualty",
        side: "Allies",
        actor: "Enemy",
        regionId: "gazala",
        unitId: "7th-armoured",
        summary: "7th Armoured Division attacked at Gazala.",
        payload: { attackerDamage: 1, defenderDamage: 3 }
      }]
    }
  ]);
  await mockApi(page, { campaignEvents, timeline });
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Campaign Progress" })).toBeVisible();
  await expect(page.getByText("You 94%")).toBeVisible();
  await expect(page.getByText("Enemy 88%")).toBeVisible();
  await expect(page.getByText("You · Movement")).toBeVisible();
  await expect(page.getByText("Enemy · Attack · Casualties")).toBeVisible();
  await expect(page.getByText("System · Tension")).toBeVisible();

  await page.getByRole("button", { name: "Attack" }).last().click();
  await expect(page.getByText("7th Armoured Division attacked at Gazala.")).toBeVisible();
  await expect(page.getByText("21st Panzer Division moved to Benghazi.")).toBeHidden();
  await page.getByRole("button", { name: "Replay" }).click();
  await expect(page.getByTestId("replay-controller")).toContainText("Turn 1 · 1/2");
});

test("Phase 5 exposes meaningful orders with projected command costs", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await mockApi(page);
  await page.goto("/");

  await expect(page.getByRole("button", { name: "Support", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Recon", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Resupply", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Hold", exact: true }).click();
  await expect(page.getByText("Entrench and restore morale")).toBeVisible();
  await expect(page.getByText(/Cost: 0 CP/)).toBeVisible();
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await expect(page.getByText(/Entrench at/)).toBeVisible();

  await page.getByRole("button", { name: "Resupply", exact: true }).click();
  await expect(page.getByText("Restore supply from the controlled network")).toBeVisible();
  await expect(page.getByText(/Cost: 1 CP · 2 SUP/)).toBeVisible();
  await page.getByRole("button", { name: "Update", exact: true }).click();
  await expect(page.getByText(/Resupply at/)).toBeVisible();
});

test("Phase 6 queues a bounded content-backed reserve deployment", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  let submittedBody: { commands?: Array<{ command: Record<string, string> }> } | null = null;
  const reserveState = {
    ...state,
    campaign: { ...campaign, currentTurnNumber: 2 },
    turnNumber: 2,
    reserves: reserves.reserves.map((reserve: { reserveId: string; unitId: string; side: string; availableTurn: number }) => ({
      reserveId: reserve.reserveId,
      unitId: reserve.unitId,
      side: reserve.side,
      status: reserve.reserveId === "90th-light-reserve" ? "Available" : "Unavailable",
      availableTurn: reserve.availableTurn,
      deploymentTurn: null,
      deployedUnitId: null
    }))
  };
  await mockApi(page, {
    campaignState: reserveState,
    onSubmit: (body) => { submittedBody = body; }
  });
  await page.goto("/");

  await expect(page.getByRole("heading", { name: "Reserves" })).toBeVisible();
  await expect(page.getByTestId("reserve-90th-light-reserve")).toContainText("90th Light Africa Division");
  await expect(page.getByTestId("reserve-90th-light-reserve")).toContainText("1 CP · 4 SUP · 5 MAN · 2 FUEL");
  await page.getByTestId("queue-reserve-90th-light-reserve").click();
  await expect(page.getByTestId("pending-deployment-90th-light-reserve")).toContainText("Deploy at");
  await page.getByRole("button", { name: "End Turn" }).click();

  expect(submittedBody).not.toBeNull();
  expect(submittedBody!.commands?.[0].command).toEqual({
    commandType: "Deploy",
    reserveId: "90th-light-reserve",
    targetRegionId: "tripoli"
  });
});

test("Phase 7 submits the weighted multi-node path selected on the operational map", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  let submittedBody: { commands?: Array<{ command: Record<string, unknown> }> } | null = null;
  await mockApi(page, { onSubmit: (body) => { submittedBody = body; } });
  await page.goto("/");

  await page.getByLabel("Order target").selectOption("ajdabiya");

  await expect(page.getByText("Target: Ajdabiya")).toBeVisible();
  await expect(page.getByText(/Cost: 1 CP · 2 SUP · 2 FUEL/)).toBeVisible();
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await expect(page.getByText("Move Ajdabiya via 2 positions")).toBeVisible();
  await page.getByRole("button", { name: "End Turn" }).click();

  expect(submittedBody).not.toBeNull();
  expect(submittedBody!.commands?.[0].command).toEqual({
    commandType: "Move",
    unitId: "15th-panzer",
    fromRegionId: "tripoli",
    pathRegionIds: ["sirte", "ajdabiya"]
  });
});

async function mockApi(page: Page, options: {
  campaignEvents?: unknown[];
  timeline?: unknown;
  campaignState?: typeof state;
  onSubmit?: (body: { commands?: Array<{ command: Record<string, unknown> }> }) => void;
} = {}) {
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    if (pathname === "/api/health") {
      await route.fulfill({ json: { status: "ok", service: "SandTable.Api" } });
      return;
    }
    if (pathname === "/api/content/theatres") {
      await route.fulfill({
        json: [{
          theatreId: map.theatreId,
          name: map.name,
          scenarios: [{
            scenarioId: scenario.scenarioId,
            theatreId: scenario.theatreId,
            name: scenario.name,
            startDate: scenario.startDate,
            maxTurns: scenario.maxTurns,
            defaultSide: scenario.defaultSide
          }]
        }]
      });
      return;
    }
    if (pathname === `/api/content/theatres/${map.theatreId}/scenarios/${scenario.scenarioId}`) {
      await route.fulfill({
        json: {
          theatre: {
            contractVersion: manifest.contractVersion,
            theatreId: manifest.theatreId,
            name: manifest.name,
            defaultScenarioId: manifest.defaultScenarioId
          },
          map,
          display,
          scenario,
          units,
          reserves,
          doctrines,
          events,
          tensionCards,
          assets: {
            assets: assets.assets.map((asset: { file: string }) => ({
              ...asset,
              url: `/theatres/${manifest.theatreId}/${asset.file}`
            }))
          }
        }
      });
      return;
    }
    if (pathname === "/api/campaigns") {
      await route.fulfill({ json: [campaign] });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/state`) {
      await route.fulfill({ json: options.campaignState ?? state });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/commands` && route.request().method() === "POST") {
      options.onSubmit?.(route.request().postDataJSON());
      await route.fulfill({ json: { accepted: true } });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/resolve-turn` && route.request().method() === "POST") {
      await route.fulfill({ json: { resolved: true } });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/events`) {
      await route.fulfill({ json: options.campaignEvents ?? [] });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/timeline`) {
      const metrics = (side: "Axis" | "Allies") => {
        const sideUnits = state.units.filter((unit: { side: string }) => unit.side === side);
        const activeUnits = sideUnits.filter((unit: { status: string; strength: number }) => unit.status !== "Destroyed" && unit.strength > 0);
        const survivingStrength = activeUnits.reduce((total: number, unit: { strength: number }) => total + unit.strength, 0);
        const maximumStrength = sideUnits.reduce((total: number, unit: { maxStrength: number }) => total + unit.maxStrength, 0);
        return {
          survivingStrength,
          maximumStrength,
          forceStrengthPercent: maximumStrength === 0 ? 0 : Math.round(1000 * survivingStrength / maximumStrength) / 10,
          activeUnitCount: activeUnits.length,
          destroyedUnitCount: sideUnits.length - activeUnits.length,
          outOfSupplyUnitCount: 0,
          controlledVictoryPoints: state.regions.filter((region: { owner: string }) => region.owner === side)
            .reduce((total: number, region: { victoryPoints: number }) => total + region.victoryPoints, 0),
          averageSupply: 0,
          averageMorale: 0
        };
      };
      await route.fulfill({ json: options.timeline ?? {
        campaignUid: campaign.campaignUid,
        playerSide: "Axis",
        enemySide: "Allies",
        points: [{
          snapshotUid: state.snapshotUid,
          turnNumber: 1,
          resolvedTurnNumber: null,
          campaignDate: state.campaignDate,
          sides: { Axis: metrics("Axis"), Allies: metrics("Allies") },
          markers: []
        }]
      } });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/turns`) {
      await route.fulfill({
        json: [{
          campaignTurnUid: "phase-1-turn",
          turnNumber: 1,
          status: "Planning",
          resolutionMode: "Simultaneous",
          summary: null,
          playerCommandsCommittedAt: null,
          aiCommandsPlannedAt: null,
          resolvedAt: null
        }]
      });
      return;
    }
    await route.fulfill({ status: 404, json: { title: `Unhandled mock route ${pathname}` } });
  });
}

function createTimeline(points: Array<{
  player: number;
  enemy: number;
  playerVp: number;
  enemyVp: number;
  resolvedTurnNumber: number | null;
  markers: unknown[];
}>) {
  const sideMetrics = (percent: number, victoryPoints: number) => ({
    survivingStrength: percent,
    maximumStrength: 100,
    forceStrengthPercent: percent,
    activeUnitCount: 5,
    destroyedUnitCount: percent < 100 ? 1 : 0,
    outOfSupplyUnitCount: 0,
    controlledVictoryPoints: victoryPoints,
    averageSupply: 7,
    averageMorale: 8
  });
  return {
    campaignUid: campaign.campaignUid,
    playerSide: "Axis",
    enemySide: "Allies",
    points: points.map((point, index) => ({
      snapshotUid: `phase-4-snapshot-${index}`,
      turnNumber: index + 1,
      resolvedTurnNumber: point.resolvedTurnNumber,
      campaignDate: scenario.startDate,
      sides: {
        Axis: sideMetrics(point.player, point.playerVp),
        Allies: sideMetrics(point.enemy, point.enemyVp)
      },
      markers: point.markers
    }))
  };
}
