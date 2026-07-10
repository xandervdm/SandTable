export type Side = "Axis" | "Allies" | "Neutral";
export type UnitType = "Infantry" | "Armour" | "Logistics" | "AirWing";
export type UnitStatus = "Ready" | "Disrupted" | "Destroyed";
export type OrderType = "Move" | "Attack" | "HoldPosition" | "Support" | "Resupply" | "Recon";

export interface Coordinate {
  x: number;
  y: number;
}

export interface CoordinateSystem {
  width: number;
  height: number;
}

export interface RegionDefinition {
  id: string;
  name: string;
  position: Coordinate;
  terrain: string;
  owner: Side;
  victoryPoints: number;
  supplyValue: number;
  features: string[];
  adjacentRegionIds: string[];
}

export interface RouteDefinition {
  fromRegionId: string;
  toRegionId: string;
  routeType: string;
}

export interface MapDefinition {
  theatreId: string;
  name: string;
  coordinateSystem: CoordinateSystem;
  regions: RegionDefinition[];
  routes: RouteDefinition[];
}

export interface MapDisplayBackground {
  url: string;
  fit: "stretch" | "cover" | string;
}

export interface RegionDisplayDefinition {
  labelOffset?: Coordinate | null;
  hitArea?: { rx: number; ry: number } | null;
  counterAnchor?: Coordinate | null;
  stackDirection?: "row" | "column" | "grid" | string | null;
}

export interface MapDisplayDefinition {
  theatreId: string;
  coordinateSystem: CoordinateSystem;
  backgroundImage: MapDisplayBackground;
  regions: Record<string, RegionDisplayDefinition>;
}

export interface TheatreMetadata {
  contractVersion: string;
  theatreId: string;
  name: string;
  defaultScenarioId: string;
}

export interface MapAssetDefinition {
  assetId: string;
  file: string;
  url: string;
  origin?: string | null;
  source?: string | null;
  createdDate?: string | null;
  license?: string | null;
  attribution?: string | null;
  intendedUse?: string | null;
}

export interface ScenarioSummary {
  scenarioId: string;
  theatreId: string;
  name: string;
  startDate: string;
  maxTurns: number;
  defaultSide: Side;
}

export interface TheatreSummary {
  theatreId: string;
  name: string;
  scenarios: ScenarioSummary[];
}

export interface Resources {
  supplies: number;
  manpower: number;
  fuel: number;
  industry: number;
  commandPoints: number;
}

export interface ScenarioDefinition {
  scenarioId: string;
  theatreId: string;
  name: string;
  startDate: string;
  maxTurns: number;
  defaultSide: Side;
  startingResources: Resources;
  victoryConditions: Array<{
    type: string;
    regionId: string;
    requiredOwner: Side;
  }>;
  startingUnitIds: string[];
}

export interface UnitDefinition {
  id: string;
  name: string;
  side: Side;
  type: UnitType;
  regionId: string;
  strength: number;
  maxStrength: number;
  movement: number;
  attack: number;
  defence: number;
  supply: number;
  morale: number;
  experience: number;
  status: UnitStatus;
  deploymentRegionIds?: string[] | null;
}

export interface ScenarioContent {
  theatre: TheatreMetadata;
  map: MapDefinition;
  scenario: ScenarioDefinition;
  units: { units: UnitDefinition[] };
  reserves: { reserves: unknown[] };
  doctrines: unknown;
  events: unknown;
  tensionCards: unknown;
  assets: { assets: MapAssetDefinition[] };
  display?: MapDisplayDefinition | null;
}

export interface CampaignSummary {
  campaignUid: string;
  name: string;
  theatreId: string;
  scenarioId: string;
  playerSide: Side;
  enemySide: Side;
  status: string;
  currentTurnNumber: number;
  maxTurns: number;
  currentCampaignDate: string;
  result: string | null;
  score: number | null;
}

export interface CampaignDetail {
  campaign: CampaignSummary;
  latestSnapshotUid: string;
  state: GameState;
}

export interface RegionState {
  id: string;
  name: string;
  terrain: string;
  owner: Side;
  victoryPoints: number;
  supplyValue: number;
  features: string[];
  adjacentRegionIds: string[];
}

export interface UnitState {
  id: string;
  name: string;
  side: Side;
  type: UnitType;
  regionId: string;
  strength: number;
  maxStrength: number;
  movement: number;
  attack: number;
  defence: number;
  supply: number;
  morale: number;
  experience: number;
  status: UnitStatus;
}

export interface TensionOption {
  id: string;
  label: string;
  description: string;
  effects: Array<{ description: string; effectType?: string }>;
}

export interface StrategicTensionCard {
  id: string;
  title: string;
  description: string;
  category: string;
  trigger: string;
  options: TensionOption[];
}

export interface TensionDecision {
  turnNumber: number;
  side: Side;
  cardId: string;
  cardTitle: string;
  optionId: string;
  optionLabel: string;
  appliedEffects: string[];
}

export interface GameState {
  theatreId: string;
  scenarioId: string;
  scenarioName: string;
  turnNumber: number;
  maxTurns: number;
  campaignDate: string;
  playerSide: Side;
  enemySide: Side;
  resources: Resources;
  regions: RegionState[];
  units: UnitState[];
  isComplete: boolean;
  result: string | null;
  victoryRegionId: string | null;
  activeTensions: StrategicTensionCard[];
  tensionHistory: TensionDecision[];
  campaignModifiers: Array<{
    id: string;
    name: string;
    remainingTurns: number;
    values: Record<string, number>;
  }>;
}

export interface CampaignStateResponse {
  campaign: CampaignSummary;
  snapshotUid: string;
  turnNumber: number;
  campaignDate: string;
  resources: Resources;
  regions: RegionState[];
  units: UnitState[];
  activeTensions: StrategicTensionCard[];
  tensionHistory: TensionDecision[];
  campaignModifiers: GameState["campaignModifiers"];
  isComplete: boolean;
  result: string | null;
}

export interface CampaignEvent {
  eventUid: string;
  campaignTurnUid: string;
  turnNumber: number;
  sequence: number;
  eventType: string;
  eventScope: string;
  side: Side | null;
  regionId: string | null;
  unitId: string | null;
  summary: string;
  payload: Record<string, unknown>;
}

export interface CampaignTurnSummary {
  campaignTurnUid: string;
  turnNumber: number;
  status: string;
  resolutionMode: string;
  summary: string | null;
  playerCommandsCommittedAt: string | null;
  aiCommandsPlannedAt: string | null;
  resolvedAt: string | null;
}

export interface SubmitCommand {
  commandType: OrderType;
  unitId: string;
  regionId?: string | null;
  targetRegionId?: string | null;
}

export interface GameClient {
  health(): Promise<{ status: string; service: string }>;
  loadTheatres(): Promise<TheatreSummary[]>;
  loadScenarioContent(theatreId: string, scenarioId: string): Promise<ScenarioContent>;
  listCampaigns(): Promise<CampaignSummary[]>;
  createCampaign(input: {
    name?: string;
    theatreId: string;
    scenarioId: string;
    playerSide: Exclude<Side, "Neutral">;
    randomSeed?: number;
  }): Promise<CampaignDetail>;
  loadCampaignState(campaignUid: string): Promise<CampaignStateResponse>;
  loadEvents(campaignUid: string): Promise<CampaignEvent[]>;
  loadTurns(campaignUid: string): Promise<CampaignTurnSummary[]>;
  submitCommands(campaignUid: string, commands: SubmitCommand[]): Promise<void>;
  resolveTurn(campaignUid: string): Promise<void>;
  chooseTensionOption(campaignUid: string, cardId: string, optionId: string): Promise<void>;
}
