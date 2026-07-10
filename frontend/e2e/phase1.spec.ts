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
  regions: map.regions.map(({ position: _position, ...region }: { position: unknown }) => region),
  units: units.units,
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

async function mockApi(page: Page) {
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
      await route.fulfill({ json: state });
      return;
    }
    if (pathname === `/api/campaigns/${campaign.campaignUid}/events`) {
      await route.fulfill({ json: [] });
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
