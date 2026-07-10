import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  Building2,
  CircleDot,
  Crosshair,
  Fuel,
  Layers3,
  Loader2,
  MapPin,
  Package,
  Play,
  Plus,
  RefreshCw,
  Shield,
  Star,
  Trash2,
  Users,
  X
} from "lucide-react";
import { HttpGameClient } from "./runtime/httpGameClient";
import { PixiTheatreMap } from "./components/PixiTheatreMap";
import type {
  CampaignEvent,
  CampaignStateResponse,
  CampaignSummary,
  CampaignTurnSummary,
  GameClient,
  OrderType,
  RegionState,
  ScenarioContent,
  StrategicTensionCard,
  TheatreSummary,
  UnitState
} from "./runtime/types";

const orderTypes: Array<{ value: OrderType; label: string }> = [
  { value: "Move", label: "Move" },
  { value: "Attack", label: "Attack" },
  { value: "HoldPosition", label: "Hold" }
];

interface PendingOrder {
  commandType: OrderType;
  unitId: string;
  unitName: string;
  unitCode: string;
  fromRegionId: string;
  fromRegionName: string;
  targetRegionId: string | null;
  targetRegionName: string | null;
}

export function App() {
  const client = useMemo<GameClient>(() => new HttpGameClient(), []);
  const [apiHealthy, setApiHealthy] = useState(false);
  const [theatres, setTheatres] = useState<TheatreSummary[]>([]);
  const [selectedTheatreId, setSelectedTheatreId] = useState("");
  const [selectedScenarioId, setSelectedScenarioId] = useState("");
  const [selectedPlayerSide, setSelectedPlayerSide] = useState<"Axis" | "Allies">("Axis");
  const [campaigns, setCampaigns] = useState<CampaignSummary[]>([]);
  const [activeCampaignUid, setActiveCampaignUid] = useState<string | null>(null);
  const [scenarioContent, setScenarioContent] = useState<ScenarioContent | null>(null);
  const [campaignState, setCampaignState] = useState<CampaignStateResponse | null>(null);
  const [events, setEvents] = useState<CampaignEvent[]>([]);
  const [turns, setTurns] = useState<CampaignTurnSummary[]>([]);
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [selectedTargetRegionId, setSelectedTargetRegionId] = useState<string | null>(null);
  const [expandedStackRegionId, setExpandedStackRegionId] = useState<string | null>(null);
  const [orderType, setOrderType] = useState<OrderType>("Move");
  const [pendingOrders, setPendingOrders] = useState<PendingOrder[]>([]);
  const [busy, setBusy] = useState<string | null>("Loading command table");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void bootstrap();
  }, []);

  async function bootstrap() {
    try {
      setBusy("Checking backend");
      setError(null);
      await client.health();
      setApiHealthy(true);

      const [loadedTheatres, loadedCampaigns] = await Promise.all([
        client.loadTheatres(),
        client.listCampaigns()
      ]);
      setTheatres(loadedTheatres);
      setCampaigns(loadedCampaigns);

      if (loadedCampaigns.length > 0) {
        await loadCampaign(loadedCampaigns[0]);
      } else if (loadedTheatres[0]?.scenarios[0]) {
        const theatre = loadedTheatres[0];
        const scenario = theatre.scenarios[0];
        setSelectedTheatreId(theatre.theatreId);
        setSelectedScenarioId(scenario.scenarioId);
        setSelectedPlayerSide(scenario.defaultSide === "Allies" ? "Allies" : "Axis");
        const content = await client.loadScenarioContent(theatre.theatreId, scenario.scenarioId);
        setScenarioContent(content);
      }
    } catch (err) {
      setApiHealthy(false);
      setError(err instanceof Error ? err.message : "Could not load SandTable.");
    } finally {
      setBusy(null);
    }
  }

  async function loadCampaign(campaign: CampaignSummary) {
    try {
      setBusy("Loading campaign");
      setError(null);
      const [content, state, campaignEvents, campaignTurns] = await Promise.all([
        client.loadScenarioContent(campaign.theatreId, campaign.scenarioId),
        client.loadCampaignState(campaign.campaignUid),
        client.loadEvents(campaign.campaignUid),
        client.loadTurns(campaign.campaignUid)
      ]);
      setScenarioContent(content);
      setSelectedTheatreId(campaign.theatreId);
      setSelectedScenarioId(campaign.scenarioId);
      setSelectedPlayerSide(campaign.playerSide === "Allies" ? "Allies" : "Axis");
      setCampaignState(state);
      setEvents(campaignEvents);
      setTurns(campaignTurns);
      setActiveCampaignUid(campaign.campaignUid);
      setSelectedTargetRegionId(null);
      setExpandedStackRegionId(null);
      setSelectedUnitId(resolveInitialUnit(state));
      setPendingOrders([]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load campaign.");
    } finally {
      setBusy(null);
    }
  }

  async function refreshActiveCampaign() {
    const active = campaigns.find((campaign) => campaign.campaignUid === activeCampaignUid);
    if (active) {
      await loadCampaign(active);
      return;
    }

    await bootstrap();
  }

  async function createCampaign() {
    const theatre = theatres.find((item) => item.theatreId === selectedTheatreId);
    const scenario = theatre?.scenarios.find((item) => item.scenarioId === selectedScenarioId);
    if (!theatre || !scenario) {
      setError("No theatre scenario is available.");
      return;
    }

    try {
      setBusy("Creating campaign");
      setError(null);
      const detail = await client.createCampaign({
        name: `${scenario.name} ${formatCampaignNameSuffix()}`,
        theatreId: theatre.theatreId,
        scenarioId: scenario.scenarioId,
        playerSide: selectedPlayerSide
      });
      const nextCampaigns = await client.listCampaigns();
      setCampaigns(nextCampaigns);
      await loadCampaign(detail.campaign);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create campaign.");
    } finally {
      setBusy(null);
    }
  }

  async function selectScenario(theatreId: string, scenarioId: string) {
    const theatre = theatres.find((item) => item.theatreId === theatreId);
    const scenario = theatre?.scenarios.find((item) => item.scenarioId === scenarioId);
    if (!theatre || !scenario) {
      return;
    }

    try {
      setBusy("Loading scenario");
      setError(null);
      const content = await client.loadScenarioContent(theatreId, scenarioId);
      setSelectedTheatreId(theatreId);
      setSelectedScenarioId(scenarioId);
      setSelectedPlayerSide(scenario.defaultSide === "Allies" ? "Allies" : "Axis");
      setScenarioContent(content);
      setCampaignState(null);
      setActiveCampaignUid(null);
      setEvents([]);
      setTurns([]);
      setPendingOrders([]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load scenario.");
    } finally {
      setBusy(null);
    }
  }

  function addPendingOrder() {
    if (!campaignState || !activeCampaignUid || !selectedUnitId) {
      return;
    }

    if (currentTurnStatus !== "Planning") {
      setError("Refresh the campaign before adding orders to the next planning turn.");
      return;
    }

    const unit = campaignState.units.find((item) => item.id === selectedUnitId);
    if (!unit) {
      return;
    }

    if (unit.side !== campaignState.campaign.playerSide) {
      setError("Only player units can receive orders.");
      return;
    }

    if (orderType !== "HoldPosition" && !selectedTargetRegionId) {
      setError("Select a valid target region before adding the order.");
      return;
    }

    const fromRegion = campaignState.regions.find((region) => region.id === unit.regionId);
    const targetRegion = selectedTargetRegionId
      ? campaignState.regions.find((region) => region.id === selectedTargetRegionId)
      : null;

    const nextOrder: PendingOrder = {
      commandType: orderType,
      unitId: unit.id,
      unitName: unit.name,
      unitCode: unitCode(unit),
      fromRegionId: unit.regionId,
      fromRegionName: fromRegion?.name ?? unit.regionId,
      targetRegionId: orderType === "HoldPosition" ? null : selectedTargetRegionId,
      targetRegionName: orderType === "HoldPosition" ? null : targetRegion?.name ?? selectedTargetRegionId
    };

    setPendingOrders((orders) => [
      ...orders.filter((order) => order.unitId !== unit.id),
      nextOrder
    ]);
    setSelectedTargetRegionId(null);
    setError(null);
  }

  async function endTurn() {
    if (!campaignState || !activeCampaignUid) {
      return;
    }

    if (currentTurnStatus === "Planning" && pendingOrders.length === 0) {
      setError("Queue at least one order before ending the turn.");
      return;
    }

    if (currentTurnStatus !== "Planning" && currentTurnStatus !== "Committed") {
      setError("This turn cannot be ended from its current status.");
      return;
    }

    let submittedOrders = false;
    try {
      setBusy("Ending turn");
      setError(null);

      if (currentTurnStatus === "Planning") {
        await client.submitCommands(
          activeCampaignUid,
          pendingOrders.map((order, index) => ({
            sequence: index + 1,
            command: order.commandType === "Move" || order.commandType === "Attack"
              ? {
                  commandType: order.commandType,
                  unitId: order.unitId,
                  fromRegionId: order.fromRegionId,
                  pathRegionIds: order.targetRegionId ? [order.targetRegionId] : []
                }
              : {
                  commandType: "HoldPosition" as const,
                  unitId: order.unitId,
                  regionId: order.fromRegionId
                }
          }))
        );
        submittedOrders = true;
        setPendingOrders([]);
      }

      await client.resolveTurn(activeCampaignUid);
      await refreshActiveCampaign();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not end turn.");
      if (submittedOrders) {
        await refreshActiveCampaign();
      }
    } finally {
      setBusy(null);
    }
  }

  function removePendingOrder(unitId: string) {
    setPendingOrders((orders) => orders.filter((order) => order.unitId !== unitId));
  }

  async function chooseTension(card: StrategicTensionCard, optionId: string) {
    if (!activeCampaignUid) {
      return;
    }

    try {
      setBusy("Applying opportunity");
      setError(null);
      await client.chooseTensionOption(activeCampaignUid, card.id, optionId);
      await refreshActiveCampaign();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not choose opportunity.");
    } finally {
      setBusy(null);
    }
  }

  const selectedUnit = campaignState?.units.find((unit) => unit.id === selectedUnitId) ?? null;
  const validTargetIds = useMemo(
    () => resolveValidTargets(orderType, selectedUnit, campaignState),
    [orderType, selectedUnit, campaignState]
  );
  const selectedRegion = selectedUnit
    ? campaignState?.regions.find((region) => region.id === selectedUnit.regionId) ?? null
    : null;
  const activeCampaign = campaignState?.campaign ?? campaigns.find((campaign) => campaign.campaignUid === activeCampaignUid);
  const currentTurn = campaignState
    ? turns.find((turn) => turn.turnNumber === campaignState.turnNumber)
    : undefined;
  const currentTurnStatus = resolveCurrentTurnStatus(campaignState, currentTurn);
  const expandedStackRegion = campaignState?.regions.find((region) => region.id === expandedStackRegionId) ?? null;
  const expandedStackUnits = campaignState?.units.filter(
    (unit) => unit.regionId === expandedStackRegionId && unit.status !== "Destroyed"
  ) ?? [];
  const selectedTheatre = theatres.find((theatre) => theatre.theatreId === selectedTheatreId);
  const selectedScenario = selectedTheatre?.scenarios.find((scenario) => scenario.scenarioId === selectedScenarioId);

  function selectUnit(unitId: string) {
    const clickedUnit = campaignState?.units.find((unit) => unit.id === unitId);
    if (
      clickedUnit &&
      campaignState &&
      clickedUnit.side !== campaignState.campaign.playerSide &&
      validTargetIds.includes(clickedUnit.regionId)
    ) {
      setSelectedTargetRegionId(clickedUnit.regionId);
      return;
    }

    setSelectedUnitId(unitId);
    const pendingOrder = pendingOrders.find((order) => order.unitId === unitId);
    if (pendingOrder) {
      setOrderType(pendingOrder.commandType);
      setSelectedTargetRegionId(pendingOrder.targetRegionId);
    } else {
      setSelectedTargetRegionId(null);
    }
  }

  function changeOrder(nextOrderType: OrderType) {
    setOrderType(nextOrderType);
    setSelectedTargetRegionId(null);
  }

  return (
    <div className="app-shell">
      <header className="top-bar">
        <div className="brand-lockup">
          <button className="icon-button" aria-label="Open menu">
            <CircleDot size={22} />
          </button>
          <div>
            <span className="overline">{scenarioContent?.scenario.name ?? activeCampaign?.name ?? "SandTable Campaign"}</span>
            <strong>{activeCampaign?.currentCampaignDate ?? scenarioContent?.scenario.startDate ?? "Awaiting scenario"}</strong>
          </div>
        </div>

        <div className="turn-block">
          <span>Turn</span>
          <strong>
            {campaignState?.turnNumber ?? activeCampaign?.currentTurnNumber ?? 1} /{" "}
            {campaignState?.campaign.maxTurns ?? activeCampaign?.maxTurns ?? 15}
          </strong>
        </div>

        <ResourceStrip state={campaignState} />

        <div className="top-actions">
          <select
            className="campaign-select"
            value={activeCampaignUid ?? ""}
            onChange={(event) => {
              const campaign = campaigns.find((item) => item.campaignUid === event.target.value);
              if (campaign) {
                void loadCampaign(campaign);
              } else {
                setCampaignState(null);
                setActiveCampaignUid(null);
                setEvents([]);
                setTurns([]);
              }
            }}
          >
            <option value="">No campaign</option>
            {campaigns.map((campaign) => (
              <option key={campaign.campaignUid} value={campaign.campaignUid}>
                {campaign.name}
              </option>
            ))}
          </select>
          <button className="primary-button" onClick={createCampaign} disabled={Boolean(busy)}>
            <Plus size={16} />
            Campaign
          </button>
          <button className="icon-button" onClick={refreshActiveCampaign} disabled={Boolean(busy)} aria-label="Refresh">
            <RefreshCw size={18} />
          </button>
        </div>
      </header>

      <main className="command-grid">
        <aside className="left-panel">
          <StatusPanel
            apiHealthy={apiHealthy}
            campaignState={campaignState}
            scenarioContent={scenarioContent}
            selectedUnit={selectedUnit}
            selectedRegion={selectedRegion}
          />
        </aside>

        <section className="map-stage">
          <div className="map-frame">
            {scenarioContent && campaignState ? (
              <PixiTheatreMap
                map={scenarioContent.map}
                display={scenarioContent.display}
                state={campaignState}
                selectedUnitId={selectedUnitId}
                selectedUnitRegionId={selectedUnit?.regionId ?? null}
                selectedTargetRegionId={selectedTargetRegionId}
                validTargetIds={validTargetIds}
                plannedUnitIds={pendingOrders.map((order) => order.unitId)}
                onUnitSelect={selectUnit}
                onStackSelect={setExpandedStackRegionId}
                onRegionSelect={(regionId) => {
                  if (validTargetIds.includes(regionId)) {
                    setSelectedTargetRegionId(regionId);
                  }
                }}
              />
            ) : (
              <div className="empty-map">
                <MapPin size={32} />
                <strong>Command table awaiting campaign</strong>
                <span>Choose a theatre, scenario, and command side.</span>
                <div className="campaign-setup-controls">
                  <label>
                    <span>Theatre</span>
                    <select
                      aria-label="Theatre"
                      value={selectedTheatreId}
                      onChange={(event) => {
                        const theatre = theatres.find((item) => item.theatreId === event.target.value);
                        const scenario = theatre?.scenarios[0];
                        if (theatre && scenario) {
                          void selectScenario(theatre.theatreId, scenario.scenarioId);
                        }
                      }}
                    >
                      {theatres.map((theatre) => (
                        <option key={theatre.theatreId} value={theatre.theatreId}>{theatre.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <span>Scenario</span>
                    <select
                      aria-label="Scenario"
                      value={selectedScenarioId}
                      onChange={(event) => void selectScenario(selectedTheatreId, event.target.value)}
                    >
                      {selectedTheatre?.scenarios.map((scenario) => (
                        <option key={scenario.scenarioId} value={scenario.scenarioId}>{scenario.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <span>Side</span>
                    <select
                      aria-label="Player side"
                      value={selectedPlayerSide}
                      onChange={(event) => setSelectedPlayerSide(event.target.value as "Axis" | "Allies")}
                    >
                      <option value="Axis">Axis</option>
                      <option value="Allies">Allies</option>
                    </select>
                  </label>
                </div>
                <button className="primary-button" onClick={createCampaign} disabled={Boolean(busy) || theatres.length === 0}>
                  <Plus size={16} />
                  Start {selectedTheatre?.name ?? "Campaign"}
                </button>
              </div>
            )}
            {expandedStackRegion && expandedStackUnits.length > 1 ? (
              <StackSelector
                regionName={expandedStackRegion.name}
                units={expandedStackUnits}
                selectedUnitId={selectedUnitId}
                plannedUnitIds={pendingOrders.map((order) => order.unitId)}
                onSelect={selectUnit}
                onClose={() => setExpandedStackRegionId(null)}
              />
            ) : null}
          </div>
          <OrderDock
            orderType={orderType}
            selectedUnit={selectedUnit}
            selectedTargetRegionId={selectedTargetRegionId}
            validTargetIds={validTargetIds}
            campaignState={campaignState}
            currentTurnStatus={currentTurnStatus}
            pendingOrders={pendingOrders}
            busy={busy}
            onOrderChange={changeOrder}
            onAddOrder={addPendingOrder}
            onRemoveOrder={removePendingOrder}
            onClearOrders={() => setPendingOrders([])}
            onEndTurn={endTurn}
          />
        </section>

        <aside className="right-panel">
          <TensionPanel
            cards={campaignState?.activeTensions ?? []}
            busy={busy}
            onChoose={chooseTension}
          />
          <EventsPanel events={events} />
        </aside>
      </main>

      <footer className="footer-bar">
        <span>{busy ? <Loader2 className="spin" size={15} /> : <Star size={15} />} {busy ?? "Command profile ready"}</span>
        <span>{error ? <strong className="error-text">{error}</strong> : "Runtime: HTTP adapter through /api proxy"}</span>
      </footer>
    </div>
  );
}

function ResourceStrip({ state }: { state: CampaignStateResponse | null }) {
  const resources = state
    ? state.resources[state.campaign.playerSide as "Axis" | "Allies"]
    : undefined;
  const items = [
    { label: "Supplies", value: resources?.supplies ?? 0, icon: Package },
    { label: "Manpower", value: resources?.manpower ?? 0, icon: Users },
    { label: "Fuel", value: resources?.fuel ?? 0, icon: Fuel },
    { label: "Industry", value: resources?.industry ?? 0, icon: Building2 },
    { label: "Command", value: resources?.commandPoints ?? 0, icon: Star }
  ];

  return (
    <div className="resource-strip">
      {items.map((item) => {
        const Icon = item.icon;
        return (
          <div className="resource-item" key={item.label}>
            <Icon size={18} />
            <strong>{item.value}</strong>
            <span>{item.label}</span>
          </div>
        );
      })}
    </div>
  );
}

function StatusPanel({
  apiHealthy,
  campaignState,
  scenarioContent,
  selectedUnit,
  selectedRegion
}: {
  apiHealthy: boolean;
  campaignState: CampaignStateResponse | null;
  scenarioContent: ScenarioContent | null;
  selectedUnit: UnitState | null;
  selectedRegion: RegionState | null;
}) {
  const playerVp = campaignState?.regions
    .filter((region) => region.owner === campaignState.campaign.playerSide)
    .reduce((total, region) => total + region.victoryPoints, 0) ?? 0;
  const enemyVp = campaignState?.regions
    .filter((region) => region.owner === campaignState.campaign.enemySide)
    .reduce((total, region) => total + region.victoryPoints, 0) ?? 0;

  return (
    <>
      <Panel title="Overview">
        <div className="score-grid">
          <div>
            <span>{campaignState?.campaign.playerSide ?? "Axis"}</span>
            <strong>{playerVp}</strong>
            <small>VP</small>
          </div>
          <div>
            <span>{campaignState?.campaign.enemySide ?? "Allies"}</span>
            <strong>{enemyVp}</strong>
            <small>VP</small>
          </div>
        </div>
      </Panel>

      <Panel title="Victory Condition">
        <p>{describeVictoryCondition(scenarioContent)}</p>
      </Panel>

      {campaignState?.isComplete ? (
        <Panel title="Campaign Result">
          <p className="campaign-result-copy">
            {formatCampaignResult(campaignState.result)}. The campaign objective has been decided.
          </p>
        </Panel>
      ) : null}

      <Panel title="Status">
        <div className="status-list">
          <span>API <strong>{apiHealthy ? "Online" : "Offline"}</strong></span>
          <span>Theatre <strong>{scenarioContent?.map.name ?? "Not loaded"}</strong></span>
          <span>Scenario <strong>{scenarioContent?.scenario.name ?? "Awaiting campaign"}</strong></span>
        </div>
      </Panel>

      <Panel title="Selected Unit">
        {selectedUnit ? (
          <div className="unit-detail">
            <div className={`unit-emblem ${selectedUnit.side.toLowerCase()}`}>{unitCode(selectedUnit)}</div>
            <div>
              <strong>{selectedUnit.name}</strong>
              <span>{selectedRegion?.name ?? selectedUnit.regionId}</span>
            </div>
            <dl>
              <dt>Strength</dt>
              <dd>{selectedUnit.strength}/{selectedUnit.maxStrength}</dd>
              <dt>Move</dt>
              <dd>{selectedUnit.movement}</dd>
              <dt>Attack</dt>
              <dd>{selectedUnit.attack}</dd>
              <dt>Defence</dt>
              <dd>{selectedUnit.defence}</dd>
              <dt>Supply</dt>
              <dd>{selectedUnit.supply}</dd>
              <dt>Status</dt>
              <dd>{selectedUnit.status}</dd>
            </dl>
          </div>
        ) : (
          <p>Select a player unit counter on the map.</p>
        )}
      </Panel>
    </>
  );
}

function StackSelector({
  regionName,
  units,
  selectedUnitId,
  plannedUnitIds,
  onSelect,
  onClose
}: {
  regionName: string;
  units: UnitState[];
  selectedUnitId: string | null;
  plannedUnitIds: string[];
  onSelect(unitId: string): void;
  onClose(): void;
}) {
  return (
    <section className="stack-selector" aria-label={`${regionName} unit stack`} data-testid="stack-selector">
      <header>
        <div>
          <span><Layers3 size={14} /> Unit stack</span>
          <strong>{regionName}</strong>
        </div>
        <button onClick={onClose} aria-label={`Close ${regionName} unit stack`}>
          <X size={16} />
        </button>
      </header>
      <div className="stack-selector-list">
        {units.map((unit) => (
          <button
            key={unit.id}
            className={unit.id === selectedUnitId ? "selected" : ""}
            data-testid={`stack-unit-${unit.id}`}
            onClick={() => onSelect(unit.id)}
          >
            <span className={`stack-unit-code ${unit.side.toLowerCase()}`}>{unitCode(unit)}</span>
            <span>
              <strong>{unit.name}</strong>
              <small>{unit.type} · {unit.strength}/{unit.maxStrength} strength</small>
            </span>
            {plannedUnitIds.includes(unit.id) ? <em>Planned</em> : null}
          </button>
        ))}
      </div>
    </section>
  );
}

function OrderDock({
  orderType,
  selectedUnit,
  selectedTargetRegionId,
  validTargetIds,
  campaignState,
  currentTurnStatus,
  pendingOrders,
  busy,
  onOrderChange,
  onAddOrder,
  onRemoveOrder,
  onClearOrders,
  onEndTurn
}: {
  orderType: OrderType;
  selectedUnit: UnitState | null;
  selectedTargetRegionId: string | null;
  validTargetIds: string[];
  campaignState: CampaignStateResponse | null;
  currentTurnStatus: string;
  pendingOrders: PendingOrder[];
  busy: string | null;
  onOrderChange(orderType: OrderType): void;
  onAddOrder(): void;
  onRemoveOrder(unitId: string): void;
  onClearOrders(): void;
  onEndTurn(): void;
}) {
  const targetName = campaignState?.regions.find((region) => region.id === selectedTargetRegionId)?.name;
  const campaignResult = campaignState?.isComplete ? formatCampaignResult(campaignState.result) : null;
  const selectedUnitIsPlayer = Boolean(
    selectedUnit &&
      campaignState &&
      selectedUnit.side === campaignState.campaign.playerSide &&
      selectedUnit.status !== "Destroyed"
  );
  const existingOrder = pendingOrders.find((order) => order.unitId === selectedUnit?.id);
  const canAdd = Boolean(
    selectedUnit &&
      campaignState &&
      !busy &&
      currentTurnStatus === "Planning" &&
      selectedUnitIsPlayer &&
      (orderType === "HoldPosition" || selectedTargetRegionId)
  );
  const endTurnBlocker = resolveEndTurnBlocker(campaignState, currentTurnStatus, pendingOrders, busy);
  const canEndTurn = !endTurnBlocker;

  return (
    <div className="order-dock">
      <div className="order-buttons">
        {orderTypes.map((item) => (
          <button
            key={item.value}
            className={orderType === item.value ? "order-button selected" : "order-button"}
            onClick={() => onOrderChange(item.value)}
          >
            {item.value === "Move" ? <ArrowRight size={20} /> : item.value === "Attack" ? <Crosshair size={20} /> : <Shield size={20} />}
            {item.label}
          </button>
        ))}
      </div>
      <div className={campaignResult ? "order-summary campaign-complete-summary" : "order-summary"}>
        <strong>{campaignResult ? `Campaign ${campaignResult}` : selectedUnit?.name ?? "No unit selected"}</strong>
        {campaignResult ? (
          <>
            <span>The final objective has been reached.</span>
            <small>Start a new campaign to play again.</small>
          </>
        ) : (
          <>
            <span>
              {!selectedUnitIsPlayer && selectedUnit
                ? "Enemy unit selected"
                : orderType === "HoldPosition"
                  ? "Hold current position"
                  : targetName
                    ? `Target: ${targetName}`
                    : `${validTargetIds.length} valid target${validTargetIds.length === 1 ? "" : "s"}`}
            </span>
            <small>
              Turn status: {currentTurnStatus}
              {existingOrder ? " · order queued" : ""}
            </small>
          </>
        )}
      </div>

      <div className="pending-orders">
        {campaignResult ? (
          <div className="campaign-result-card">
            <strong>{campaignResult}</strong>
            <span>Operations have concluded. No further orders can be issued in this campaign.</span>
          </div>
        ) : (
          <>
            <div className="pending-header">
              <strong>Pending Orders</strong>
              <button onClick={onClearOrders} disabled={pendingOrders.length === 0 || Boolean(busy)}>
                Clear
              </button>
            </div>
            {pendingOrders.length === 0 ? (
              <span className="empty-orders">Queue unit orders before ending the turn.</span>
            ) : (
              <ol>
                {pendingOrders.map((order) => (
                  <li key={order.unitId}>
                    <div>
                      <strong>{order.unitCode} {order.unitName}</strong>
                      <span>
                        {order.commandType === "HoldPosition"
                          ? `Hold at ${order.fromRegionName}`
                          : `${order.commandType} ${order.targetRegionName}`}
                      </span>
                    </div>
                    <button onClick={() => onRemoveOrder(order.unitId)} disabled={Boolean(busy)} aria-label={`Remove ${order.unitName} order`}>
                      <Trash2 size={14} />
                    </button>
                  </li>
                ))}
              </ol>
            )}
          </>
        )}
      </div>

      <button className="primary-button" onClick={onAddOrder} disabled={!canAdd}>
        <Play size={16} />
        {existingOrder ? "Update" : "Add"}
      </button>
      <button
        className="gold-button"
        onClick={onEndTurn}
        disabled={!canEndTurn}
        title={endTurnBlocker ?? "Submit orders and resolve the turn"}
      >
        End Turn
      </button>
    </div>
  );
}

function TensionPanel({
  cards,
  busy,
  onChoose
}: {
  cards: StrategicTensionCard[];
  busy: string | null;
  onChoose(card: StrategicTensionCard, optionId: string): void;
}) {
  return (
    <Panel title="Operational Opportunities">
      {cards.length === 0 ? (
        <p>No active operational opportunities.</p>
      ) : (
        <div className="tension-list">
          {cards.map((card) => (
            <article className="tension-card" key={card.id}>
              <strong>{card.title}</strong>
              <p>{card.description}</p>
              {card.options.map((option) => (
                <button key={option.id} onClick={() => onChoose(card, option.id)} disabled={Boolean(busy)}>
                  <span>{option.label}</span>
                  <small>{option.description}</small>
                </button>
              ))}
            </article>
          ))}
        </div>
      )}
    </Panel>
  );
}

function EventsPanel({ events }: { events: CampaignEvent[] }) {
  return (
    <Panel title="Command Log">
      {events.length === 0 ? (
        <p>Resolved events will appear here.</p>
      ) : (
        <ol className="event-list">
          {events.slice(0, 10).map((event) => (
            <li key={event.eventUid}>
              <span>Turn {event.turnNumber}</span>
              <strong>{event.summary}</strong>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="panel">
      <h2>{title}</h2>
      {children}
    </section>
  );
}

function resolveInitialUnit(state: CampaignStateResponse) {
  return state.units.find((unit) => unit.side === state.campaign.playerSide && unit.status !== "Destroyed")?.id ?? null;
}

function resolveCurrentTurnStatus(
  campaignState: CampaignStateResponse | null,
  currentTurn: CampaignTurnSummary | undefined
) {
  if (!campaignState) {
    return "Unknown";
  }

  if (
    currentTurn?.status === "Resolved" &&
    campaignState.campaign.status === "Active" &&
    campaignState.campaign.currentTurnNumber === campaignState.turnNumber &&
    !campaignState.isComplete
  ) {
    return "Planning";
  }

  if (currentTurn?.status) {
    return currentTurn.status;
  }

  return campaignState.isComplete ? "Resolved" : "Planning";
}

function resolveEndTurnBlocker(
  campaignState: CampaignStateResponse | null,
  currentTurnStatus: string,
  pendingOrders: PendingOrder[],
  busy: string | null
) {
  if (busy) {
    return busy;
  }

  if (!campaignState) {
    return "No campaign loaded";
  }

  if (campaignState.isComplete) {
    return `Campaign ${formatCampaignResult(campaignState.result)}`;
  }

  if (currentTurnStatus === "Planning") {
    return pendingOrders.length > 0 ? null : "Queue at least one order";
  }

  if (currentTurnStatus === "Committed") {
    return pendingOrders.length === 0 ? null : "Refresh the committed turn";
  }

  return `Turn is ${currentTurnStatus}`;
}

function formatCampaignResult(result: string | null | undefined) {
  return result?.trim() || "Complete";
}

function resolveValidTargets(orderType: OrderType, selectedUnit: UnitState | null, state: CampaignStateResponse | null) {
  if (!selectedUnit || !state || orderType === "HoldPosition") {
    return [];
  }

  if (selectedUnit.side !== state.campaign.playerSide || selectedUnit.status === "Destroyed") {
    return [];
  }

  const currentRegion = state.regions.find((region) => region.id === selectedUnit.regionId);
  if (!currentRegion) {
    return [];
  }

  if (orderType === "Move") {
    return currentRegion.adjacentRegionIds.filter((regionId) => {
      const enemyUnit = state.units.some(
        (unit) => unit.regionId === regionId && unit.side !== state.campaign.playerSide && unit.status !== "Destroyed"
      );
      return !enemyUnit;
    });
  }

  if (orderType === "Attack") {
    return currentRegion.adjacentRegionIds.filter((regionId) =>
      state.units.some((unit) => unit.regionId === regionId && unit.side !== state.campaign.playerSide && unit.status !== "Destroyed")
    );
  }

  return currentRegion.adjacentRegionIds;
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

function formatCampaignNameSuffix(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hour = String(date.getHours()).padStart(2, "0");
  const minute = String(date.getMinutes()).padStart(2, "0");
  const second = String(date.getSeconds()).padStart(2, "0");
  return `${year}${month}${day}-${hour}${minute}${second}`;
}

function describeVictoryCondition(content: ScenarioContent | null) {
  const condition = content?.scenario.victoryRules.outcomes
    .find((outcome) => outcome.result === "Victory")
    ?.allOf[0];
  if (!condition) {
    return "Victory conditions are defined by the selected scenario.";
  }

  if (condition.type === "ControlRegion") {
    const region = content?.map.regions.find((item) => item.id === condition.regionId);
    return `${condition.side ?? "Player"} must control ${region?.name ?? condition.regionId}.`;
  }

  return "Complete the selected scenario's victory conditions.";
}
