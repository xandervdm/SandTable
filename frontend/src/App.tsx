import { useEffect, useMemo, useState } from "react";
import {
  Activity,
  ArrowRight,
  Building2,
  CircleDot,
  ChevronLeft,
  ChevronRight,
  Crosshair,
  Fuel,
  Flag,
  Layers3,
  Loader2,
  MapPin,
  Package,
  Pause,
  Play,
  Plus,
  RefreshCw,
  Shield,
  Skull,
  Star,
  Trash2,
  Users,
  Trophy,
  Zap,
  X
} from "lucide-react";
import { HttpGameClient } from "./runtime/httpGameClient";
import { PixiTheatreMap } from "./components/PixiTheatreMap";
import type {
  CampaignEvent,
  CampaignStateResponse,
  CampaignSummary,
  CampaignTimeline,
  CampaignTurnSummary,
  GameClient,
  OrderType,
  RegionState,
  ReserveDefinition,
  ScenarioContent,
  StrategicTensionCard,
  SubmitCommandPayload,
  TheatreSummary,
  UnitState
} from "./runtime/types";

const orderTypes: Array<{ value: OrderType; label: string }> = [
  { value: "Move", label: "Move" },
  { value: "Attack", label: "Attack" },
  { value: "Support", label: "Support" },
  { value: "Recon", label: "Recon" },
  { value: "Resupply", label: "Resupply" },
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

interface PendingDeployment {
  reserveId: string;
  unitId: string;
  unitName: string;
  targetRegionId: string;
  targetRegionName: string;
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
  const [timeline, setTimeline] = useState<CampaignTimeline | null>(null);
  const [replayEvent, setReplayEvent] = useState<CampaignEvent | null>(null);
  const [turns, setTurns] = useState<CampaignTurnSummary[]>([]);
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [selectedTargetRegionId, setSelectedTargetRegionId] = useState<string | null>(null);
  const [expandedStackRegionId, setExpandedStackRegionId] = useState<string | null>(null);
  const [orderType, setOrderType] = useState<OrderType>("Move");
  const [pendingOrders, setPendingOrders] = useState<PendingOrder[]>([]);
  const [pendingDeployments, setPendingDeployments] = useState<PendingDeployment[]>([]);
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
      const [content, state, campaignEvents, campaignTurns, campaignTimeline] = await Promise.all([
        client.loadScenarioContent(campaign.theatreId, campaign.scenarioId),
        client.loadCampaignState(campaign.campaignUid),
        client.loadEvents(campaign.campaignUid),
        client.loadTurns(campaign.campaignUid),
        client.loadTimeline(campaign.campaignUid)
      ]);
      setScenarioContent(content);
      setSelectedTheatreId(campaign.theatreId);
      setSelectedScenarioId(campaign.scenarioId);
      setSelectedPlayerSide(campaign.playerSide === "Allies" ? "Allies" : "Axis");
      setCampaignState(state);
      setEvents(campaignEvents);
      setTurns(campaignTurns);
      setTimeline(campaignTimeline);
      setReplayEvent(null);
      setActiveCampaignUid(campaign.campaignUid);
      setSelectedTargetRegionId(null);
      setExpandedStackRegionId(null);
      setSelectedUnitId(resolveInitialUnit(state));
      setPendingOrders([]);
      setPendingDeployments([]);
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
      setTimeline(null);
      setReplayEvent(null);
      setPendingOrders([]);
      setPendingDeployments([]);
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

    if (!orderUsesCurrentRegion(orderType) && !selectedTargetRegionId) {
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
      targetRegionId: orderUsesCurrentRegion(orderType) ? null : selectedTargetRegionId,
      targetRegionName: orderUsesCurrentRegion(orderType) ? null : targetRegion?.name ?? selectedTargetRegionId
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

    if (currentTurnStatus === "Planning" && pendingOrders.length === 0 && pendingDeployments.length === 0) {
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
          [
            ...pendingDeployments.map((deployment) => ({ command: toSubmitDeployment(deployment) })),
            ...pendingOrders.map((order) => ({ command: toSubmitCommand(order) }))
          ].map((item, index) => ({ sequence: index + 1, command: item.command }))
        );
        submittedOrders = true;
        setPendingOrders([]);
        setPendingDeployments([]);
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

  function queueDeployment(reserve: ReserveDefinition, targetRegionId: string) {
    if (!campaignState || !scenarioContent || currentTurnStatus !== "Planning") return;
    const unit = scenarioContent.units.units.find((candidate) => candidate.id === reserve.unitId);
    const target = campaignState.regions.find((candidate) => candidate.id === targetRegionId);
    if (!unit || !target) return;
    setPendingDeployments((deployments) => [
      ...deployments.filter((deployment) => deployment.reserveId !== reserve.reserveId),
      {
        reserveId: reserve.reserveId,
        unitId: reserve.unitId,
        unitName: unit.name,
        targetRegionId,
        targetRegionName: target.name
      }
    ]);
    setError(null);
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
                setTimeline(null);
                setReplayEvent(null);
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
                replayEvent={replayEvent}
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
            scenarioContent={scenarioContent}
            currentTurnStatus={currentTurnStatus}
            pendingOrders={pendingOrders}
            pendingDeployments={pendingDeployments}
            busy={busy}
            onOrderChange={changeOrder}
            onAddOrder={addPendingOrder}
            onRemoveOrder={removePendingOrder}
            onRemoveDeployment={(reserveId) => setPendingDeployments((items) => items.filter((item) => item.reserveId !== reserveId))}
            onClearOrders={() => { setPendingOrders([]); setPendingDeployments([]); }}
            onEndTurn={endTurn}
          />
        </section>

        <aside className="right-panel">
          <ReservePanel
            campaignState={campaignState}
            scenarioContent={scenarioContent}
            currentTurnStatus={currentTurnStatus}
            pendingOrders={pendingOrders}
            pendingDeployments={pendingDeployments}
            busy={busy}
            onQueue={queueDeployment}
          />
          <TensionPanel
            cards={campaignState?.activeTensions ?? []}
            busy={busy}
            onChoose={chooseTension}
          />
          <ProgressPanel timeline={timeline} />
          <EventsPanel events={events} onReplayEvent={setReplayEvent} />
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
              <dt>Supply State</dt>
              <dd className={selectedUnit.supplyStatus === "OutOfSupply" ? "supply-warning" : ""}>
                {formatSupplyStatus(selectedUnit)}
              </dd>
              <dt>Posture</dt>
              <dd>{selectedUnit.isEntrenched ? "Entrenched (+2)" : "Mobile"}</dd>
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
  scenarioContent,
  currentTurnStatus,
  pendingOrders,
  pendingDeployments,
  busy,
  onOrderChange,
  onAddOrder,
  onRemoveOrder,
  onRemoveDeployment,
  onClearOrders,
  onEndTurn
}: {
  orderType: OrderType;
  selectedUnit: UnitState | null;
  selectedTargetRegionId: string | null;
  validTargetIds: string[];
  campaignState: CampaignStateResponse | null;
  scenarioContent: ScenarioContent | null;
  currentTurnStatus: string;
  pendingOrders: PendingOrder[];
  pendingDeployments: PendingDeployment[];
  busy: string | null;
  onOrderChange(orderType: OrderType): void;
  onAddOrder(): void;
  onRemoveOrder(unitId: string): void;
  onRemoveDeployment(reserveId: string): void;
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
  const commandBudget = resolveCommandBudget(campaignState);
  const playerResources = resolvePlayerResources(campaignState);
  const deploymentCost = sumOrderCosts(pendingDeployments.map((item) => calculateDeploymentCost(item, scenarioContent)));
  const committedOrders = pendingOrders.filter((order) => order.unitId !== selectedUnit?.id);
  const unitOrderCost = sumOrderCosts(committedOrders.map((order) => calculateOrderCost(order, campaignState, scenarioContent)));
  const committedCost = sumOrderCosts([unitOrderCost, deploymentCost]);
  const draftOrder: PendingOrder | null = selectedUnit && campaignState
    ? {
        commandType: orderType,
        unitId: selectedUnit.id,
        unitName: selectedUnit.name,
        unitCode: unitCode(selectedUnit),
        fromRegionId: selectedUnit.regionId,
        fromRegionName: campaignState.regions.find((region) => region.id === selectedUnit.regionId)?.name ?? selectedUnit.regionId,
        targetRegionId: selectedTargetRegionId,
        targetRegionName: campaignState.regions.find((region) => region.id === selectedTargetRegionId)?.name ?? selectedTargetRegionId
      }
    : null;
  const draftCost = draftOrder ? calculateOrderCost(draftOrder, campaignState, scenarioContent) : zeroProjectedCost;
  const canAffordDraft = committedCost.commandPoints + draftCost.commandPoints <= commandBudget
    && committedCost.supplies + draftCost.supplies <= (playerResources?.supplies ?? 0)
    && committedCost.manpower + draftCost.manpower <= (playerResources?.manpower ?? 0)
    && committedCost.fuel + draftCost.fuel <= (playerResources?.fuel ?? 0)
    && committedCost.industry + draftCost.industry <= (playerResources?.industry ?? 0);
  const canAdd = Boolean(
    selectedUnit &&
      campaignState &&
      !busy &&
      currentTurnStatus === "Planning" &&
      selectedUnitIsPlayer &&
      (orderUsesCurrentRegion(orderType) || selectedTargetRegionId) &&
      canAffordDraft
  );
  const endTurnBlocker = resolveEndTurnBlocker(campaignState, currentTurnStatus, pendingOrders, pendingDeployments, busy);
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
            {orderIcon(item.value)}
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
                  ? "Entrench and restore morale"
                  : orderType === "Resupply"
                    ? "Restore supply from the controlled network"
                  : targetName
                    ? `Target: ${targetName}`
                    : `${validTargetIds.length} valid target${validTargetIds.length === 1 ? "" : "s"}`}
            </span>
            <small>
              Cost: {draftCost.commandPoints} CP · {draftCost.supplies} SUP · {draftCost.fuel} FUEL
              {` · ${Math.max(0, commandBudget - committedCost.commandPoints - draftCost.commandPoints)} CP left`}
              <br />Turn status: {currentTurnStatus}
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
              <strong>Pending Orders · {sumOrderCosts([
                ...pendingOrders.map((order) => calculateOrderCost(order, campaignState, scenarioContent)),
                ...pendingDeployments.map((item) => calculateDeploymentCost(item, scenarioContent))
              ]).commandPoints}/{commandBudget} CP</strong>
              <button onClick={onClearOrders} disabled={pendingOrders.length + pendingDeployments.length === 0 || Boolean(busy)}>
                Clear
              </button>
            </div>
            {pendingOrders.length === 0 && pendingDeployments.length === 0 ? (
              <span className="empty-orders">Queue unit orders before ending the turn.</span>
            ) : (
              <ol>
                {pendingOrders.map((order) => (
                  <li key={order.unitId}>
                    <div>
                      <strong>{order.unitCode} {order.unitName}</strong>
                      <span>
                        {describePendingOrder(order)}
                      </span>
                    </div>
                    <button onClick={() => onRemoveOrder(order.unitId)} disabled={Boolean(busy)} aria-label={`Remove ${order.unitName} order`}>
                      <Trash2 size={14} />
                    </button>
                  </li>
                ))}
                {pendingDeployments.map((deployment) => (
                  <li key={deployment.reserveId} data-testid={`pending-deployment-${deployment.reserveId}`}>
                    <div>
                      <strong>RES {deployment.unitName}</strong>
                      <span>Deploy at {deployment.targetRegionName}</span>
                    </div>
                    <button onClick={() => onRemoveDeployment(deployment.reserveId)} disabled={Boolean(busy)} aria-label={`Remove ${deployment.unitName} deployment`}>
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

function ReservePanel({
  campaignState,
  scenarioContent,
  currentTurnStatus,
  pendingOrders,
  pendingDeployments,
  busy,
  onQueue
}: {
  campaignState: CampaignStateResponse | null;
  scenarioContent: ScenarioContent | null;
  currentTurnStatus: string;
  pendingOrders: PendingOrder[];
  pendingDeployments: PendingDeployment[];
  busy: string | null;
  onQueue(reserve: ReserveDefinition, targetRegionId: string): void;
}) {
  const [targets, setTargets] = useState<Record<string, string>>({});
  const definitions = scenarioContent?.reserves.reserves ?? [];
  const playerSide = campaignState?.campaign.playerSide;
  const reserveStates = (campaignState?.reserves ?? []).filter((reserve) => reserve.side === playerSide);
  const pendingCost = sumOrderCosts([
    ...pendingOrders.map((order) => calculateOrderCost(order, campaignState, scenarioContent)),
    ...pendingDeployments.map((deployment) => calculateDeploymentCost(deployment, scenarioContent))
  ]);
  const resources = resolvePlayerResources(campaignState);
  const budget = resolveCommandBudget(campaignState);

  return (
    <Panel title="Reserves">
      {reserveStates.length === 0 ? <p>No reserves assigned to your side.</p> : (
        <div className="reserve-list">
          {reserveStates.map((reserveState) => {
            const definition = definitions.find((candidate) => candidate.reserveId === reserveState.reserveId);
            const unit = scenarioContent?.units.units.find((candidate) => candidate.id === reserveState.unitId);
            const eligibleRegions = (definition?.eligibleRegionIds ?? [])
              .map((regionId) => campaignState?.regions.find((region) => region.id === regionId))
              .filter((region): region is RegionState => Boolean(region
                && region.owner === playerSide
                && (definition?.requiredRegionFeatures ?? []).every((feature) => region.features.includes(feature))));
            const targetId = targets[reserveState.reserveId] ?? eligibleRegions[0]?.id ?? "";
            const cost = definition ? calculateReserveDefinitionCost(definition, scenarioContent) : zeroProjectedCost;
            const affordable = pendingCost.commandPoints + cost.commandPoints <= budget
              && pendingCost.supplies + cost.supplies <= (resources?.supplies ?? 0)
              && pendingCost.manpower + cost.manpower <= (resources?.manpower ?? 0)
              && pendingCost.fuel + cost.fuel <= (resources?.fuel ?? 0)
              && pendingCost.industry + cost.industry <= (resources?.industry ?? 0);
            const queued = pendingDeployments.some((item) => item.reserveId === reserveState.reserveId);
            const canQueue = reserveState.status === "Available"
              && currentTurnStatus === "Planning"
              && eligibleRegions.length > 0
              && pendingDeployments.length < (scenarioContent?.scenario.deploymentLimitPerSidePerTurn ?? 0)
              && affordable
              && !busy;

            return (
              <article className="reserve-card" key={reserveState.reserveId} data-testid={`reserve-${reserveState.reserveId}`}>
                <div>
                  <strong>{unit?.name ?? reserveState.unitId}</strong>
                  <span>{reserveState.status === "Unavailable" ? `Available turn ${reserveState.availableTurn}` : reserveState.status}</span>
                </div>
                {reserveState.status === "Available" && definition ? (
                  <>
                    <small>{cost.commandPoints} CP · {cost.supplies} SUP · {cost.manpower} MAN · {cost.fuel} FUEL</small>
                    <select
                      aria-label={`Deployment position for ${unit?.name ?? reserveState.unitId}`}
                      value={targetId}
                      onChange={(event) => setTargets((current) => ({ ...current, [reserveState.reserveId]: event.target.value }))}
                    >
                      {eligibleRegions.map((region) => <option key={region.id} value={region.id}>{region.name}</option>)}
                    </select>
                    <button
                      className="primary-button"
                      data-testid={`queue-reserve-${reserveState.reserveId}`}
                      disabled={!canQueue || queued}
                      onClick={() => onQueue(definition, targetId)}
                    >
                      {queued ? "Queued" : "Queue deployment"}
                    </button>
                  </>
                ) : null}
              </article>
            );
          })}
        </div>
      )}
    </Panel>
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

function ProgressPanel({ timeline }: { timeline: CampaignTimeline | null }) {
  if (!timeline || timeline.points.length === 0) {
    return <Panel title="Campaign Progress"><p>Timeline data will appear after the campaign loads.</p></Panel>;
  }

  const width = 232;
  const height = 92;
  const plotTop = 10;
  const plotBottom = 76;
  const pointX = (index: number) => timeline.points.length === 1
    ? width / 2
    : 10 + index * (width - 20) / (timeline.points.length - 1);
  const pointY = (percent: number) => plotBottom - Math.max(0, Math.min(100, percent)) / 100 * (plotBottom - plotTop);
  const line = (side: "Axis" | "Allies") => timeline.points
    .map((point, index) => `${pointX(index)},${pointY(point.sides[side].forceStrengthPercent)}`)
    .join(" ");
  const latest = timeline.points[timeline.points.length - 1];
  const player = latest.sides[timeline.playerSide];
  const enemy = latest.sides[timeline.enemySide];
  const markers = timeline.points.flatMap((point) => point.markers.map((marker) => ({ marker, point })));

  return (
    <Panel title="Campaign Progress">
      <div className="progress-chart" aria-label="Force Strength by turn">
        <div className="progress-legend">
          <span className="player-line">You {player.forceStrengthPercent}%</span>
          <span className="enemy-line">Enemy {enemy.forceStrengthPercent}%</span>
        </div>
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Player and enemy Force Strength lines">
          {[25, 50, 75, 100].map((value) => (
            <line key={value} x1="8" x2={width - 8} y1={pointY(value)} y2={pointY(value)} className="chart-grid-line" />
          ))}
          <polyline points={line(timeline.playerSide)} className="force-line player" />
          <polyline points={line(timeline.enemySide)} className="force-line enemy" />
          {timeline.points.map((point, index) => (
            <g key={point.snapshotUid}>
              <circle cx={pointX(index)} cy={pointY(point.sides[timeline.playerSide].forceStrengthPercent)} r="2.8" className="force-point player" />
              <circle cx={pointX(index)} cy={pointY(point.sides[timeline.enemySide].forceStrengthPercent)} r="2.8" className="force-point enemy" />
              <text x={pointX(index)} y="89" textAnchor="middle">{point.resolvedTurnNumber ?? "S"}</text>
            </g>
          ))}
        </svg>
        <div className="progress-metrics">
          <span><strong>{player.survivingStrength}/{player.maximumStrength}</strong> Your strength</span>
          <span><strong>{enemy.survivingStrength}/{enemy.maximumStrength}</strong> Enemy strength</span>
          <span><strong>{player.controlledVictoryPoints}</strong> Your VP</span>
          <span><strong>{enemy.controlledVictoryPoints}</strong> Enemy VP</span>
          <span><strong>{player.outOfSupplyUnitCount}</strong> Your OOS</span>
          <span><strong>{enemy.outOfSupplyUnitCount}</strong> Enemy OOS</span>
        </div>
        {markers.length > 0 ? (
          <div className="timeline-markers" aria-label="Campaign timeline markers">
            {markers.slice(-8).map(({ marker, point }) => (
              <span key={`${point.snapshotUid}-${marker.eventUid}-${marker.markerType}`} title={marker.summary}>
                {timelineMarkerIcon(marker.markerType)} T{point.resolvedTurnNumber} {marker.markerType}
              </span>
            ))}
          </div>
        ) : null}
      </div>
    </Panel>
  );
}

type EventFilter = "All" | "Movement" | "Attack" | "Casualty" | "Supply" | "Tension" | "Victory";

const eventFilters: EventFilter[] = ["All", "Movement", "Attack", "Casualty", "Supply", "Tension", "Victory"];

function EventsPanel({
  events,
  onReplayEvent
}: {
  events: CampaignEvent[];
  onReplayEvent(event: CampaignEvent | null): void;
}) {
  const [filter, setFilter] = useState<EventFilter>("All");
  const [replayTurn, setReplayTurn] = useState<number | null>(null);
  const [replayIndex, setReplayIndex] = useState(0);
  const [playing, setPlaying] = useState(false);
  const filteredEvents = useMemo(() => events.filter((event) => matchesEventFilter(event, filter)), [events, filter]);
  const groupedEvents = useMemo(() => Array.from(
    filteredEvents.reduce((groups, event) => {
      const group = groups.get(event.turnNumber) ?? [];
      group.push(event);
      groups.set(event.turnNumber, group);
      return groups;
    }, new Map<number, CampaignEvent[]>()).entries())
    .sort(([left], [right]) => right - left)
    .map(([turn, turnEvents]) => ({ turn, events: turnEvents.sort((left, right) => left.sequence - right.sequence) })), [filteredEvents]);
  const replaySequence = useMemo(() => events
    .filter((event) => event.turnNumber === replayTurn && (event.eventType === "Movement" || event.eventType === "Battle"))
    .sort((left, right) => left.sequence - right.sequence), [events, replayTurn]);

  useEffect(() => {
    onReplayEvent(replaySequence[replayIndex] ?? null);
  }, [onReplayEvent, replayIndex, replaySequence]);

  useEffect(() => () => onReplayEvent(null), [onReplayEvent]);

  useEffect(() => {
    if (!playing || replaySequence.length === 0) {
      return;
    }
    const timer = window.setTimeout(() => {
      if (replayIndex >= replaySequence.length - 1) {
        setPlaying(false);
      } else {
        setReplayIndex((index) => index + 1);
      }
    }, 950);
    return () => window.clearTimeout(timer);
  }, [playing, replayIndex, replaySequence]);

  function startReplay(turn: number) {
    setReplayTurn(turn);
    setReplayIndex(0);
    setPlaying(true);
  }

  return (
    <Panel title="Command Log">
      {events.length === 0 ? (
        <p>Resolved events will appear here.</p>
      ) : (
        <div className="event-log">
          <div className="event-filters" aria-label="Command log filters">
            {eventFilters.map((value) => (
              <button key={value} className={filter === value ? "selected" : ""} onClick={() => setFilter(value)} title={value}>
                {eventFilterIcon(value)}<span>{value}</span>
              </button>
            ))}
          </div>
          {replayTurn !== null && replaySequence.length > 0 ? (
            <div className="replay-controller" data-testid="replay-controller">
              <button onClick={() => setReplayIndex((index) => Math.max(0, index - 1))} disabled={replayIndex === 0} aria-label="Previous replay event"><ChevronLeft size={14} /></button>
              <button onClick={() => setPlaying((value) => !value)} aria-label={playing ? "Pause replay" : "Play replay"}>
                {playing ? <Pause size={14} /> : <Play size={14} />}
              </button>
              <button onClick={() => setReplayIndex((index) => Math.min(replaySequence.length - 1, index + 1))} disabled={replayIndex >= replaySequence.length - 1} aria-label="Next replay event"><ChevronRight size={14} /></button>
              <span>Turn {replayTurn} · {replayIndex + 1}/{replaySequence.length}</span>
            </div>
          ) : null}
          {groupedEvents.length === 0 ? <p>No events match this filter.</p> : groupedEvents.map((group) => {
            const replayable = events.some((event) => event.turnNumber === group.turn && (event.eventType === "Movement" || event.eventType === "Battle"));
            return (
              <section className="event-turn" key={group.turn}>
                <header>
                  <strong>Turn {group.turn}</strong>
                  {replayable ? <button onClick={() => startReplay(group.turn)}><Play size={12} /> Replay</button> : null}
                </header>
                <ol className="event-list">
                  {group.events.map((event) => (
                    <li key={event.eventUid} className={`actor-${event.actor.toLowerCase()}`}>
                      <span className="event-icon">{eventTypeIcon(event)}</span>
                      <div>
                        <span>{event.actor} · {eventCategory(event)}</span>
                        <strong>{event.summary}</strong>
                      </div>
                    </li>
                  ))}
                </ol>
              </section>
            );
          })}
        </div>
      )}
    </Panel>
  );
}

function matchesEventFilter(event: CampaignEvent, filter: EventFilter) {
  if (filter === "All") return true;
  if (filter === "Movement") return event.eventType === "Movement";
  if (filter === "Attack" || filter === "Casualty") return event.eventType === "Battle";
  return event.eventType === filter;
}

function eventCategory(event: CampaignEvent) {
  if (event.eventType === "Battle") return "Attack · Casualties";
  return event.eventType;
}

function eventFilterIcon(filter: EventFilter) {
  if (filter === "Movement") return <ArrowRight size={13} />;
  if (filter === "Attack") return <Crosshair size={13} />;
  if (filter === "Casualty") return <Skull size={13} />;
  if (filter === "Supply") return <Package size={13} />;
  if (filter === "Tension") return <Zap size={13} />;
  if (filter === "Victory") return <Trophy size={13} />;
  return <Activity size={13} />;
}

function eventTypeIcon(event: CampaignEvent) {
  if (event.eventType === "Movement") return <ArrowRight size={15} />;
  if (event.eventType === "Battle") return <Crosshair size={15} />;
  if (event.eventType === "Supply") return <Package size={15} />;
  if (event.eventType === "Tension") return <Zap size={15} />;
  if (event.eventType === "Victory") return <Trophy size={15} />;
  return <Activity size={15} />;
}

function timelineMarkerIcon(markerType: CampaignTimeline["points"][number]["markers"][number]["markerType"]) {
  if (markerType === "Casualty") return <Skull size={11} />;
  if (markerType === "Objective") return <Flag size={11} />;
  if (markerType === "Deployment") return <Users size={11} />;
  if (markerType === "Tension") return <Zap size={11} />;
  return <Trophy size={11} />;
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
  pendingDeployments: PendingDeployment[],
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
    return pendingOrders.length + pendingDeployments.length > 0 ? null : "Queue at least one order";
  }

  if (currentTurnStatus === "Committed") {
    return pendingOrders.length + pendingDeployments.length === 0 ? null : "Refresh the committed turn";
  }

  return `Turn is ${currentTurnStatus}`;
}

function formatCampaignResult(result: string | null | undefined) {
  return result?.trim() || "Complete";
}

interface ProjectedOrderCost {
  commandPoints: number;
  supplies: number;
  manpower: number;
  fuel: number;
  industry: number;
}

const zeroProjectedCost: ProjectedOrderCost = { commandPoints: 0, supplies: 0, manpower: 0, fuel: 0, industry: 0 };

function orderUsesCurrentRegion(orderType: OrderType) {
  return orderType === "HoldPosition" || orderType === "Resupply";
}

function toSubmitCommand(order: PendingOrder): SubmitCommandPayload {
  if (order.commandType === "Move" || order.commandType === "Attack") {
    return {
      commandType: order.commandType,
      unitId: order.unitId,
      fromRegionId: order.fromRegionId,
      pathRegionIds: order.targetRegionId ? [order.targetRegionId] : []
    };
  }
  if (order.commandType === "Support" || order.commandType === "Recon") {
    return {
      commandType: order.commandType,
      unitId: order.unitId,
      fromRegionId: order.fromRegionId,
      targetRegionId: order.targetRegionId ?? order.fromRegionId
    };
  }
  if (order.commandType === "Resupply") {
    return { commandType: "Resupply", unitId: order.unitId, regionId: order.fromRegionId };
  }
  return { commandType: "HoldPosition", unitId: order.unitId, regionId: order.fromRegionId };
}

function toSubmitDeployment(deployment: PendingDeployment): SubmitCommandPayload {
  return { commandType: "Deploy", reserveId: deployment.reserveId, targetRegionId: deployment.targetRegionId };
}

function calculateOrderCost(
  order: PendingOrder,
  state: CampaignStateResponse | null,
  content: ScenarioContent | null
): ProjectedOrderCost {
  const definition = content?.scenario.commandCosts[order.commandType];
  if (!definition) return zeroProjectedCost;
  const route = state?.routes.find((candidate) =>
    order.targetRegionId
    && (candidate.fromRegionId === order.fromRegionId && candidate.toRegionId === order.targetRegionId
      || candidate.fromRegionId === order.targetRegionId && candidate.toRegionId === order.fromRegionId));
  const movementCost = order.commandType === "Move" || order.commandType === "Attack" ? route?.movementCost ?? 0 : 0;
  const fuelReserve = state?.campaignModifiers.reduce((total, modifier) => total + (modifier.values.fuelReserve ?? 0), 0) ?? 0;
  return {
    commandPoints: definition.baseCommandPoints,
    supplies: definition.fixedSupplies + definition.suppliesPerMovementCost * movementCost,
    manpower: 0,
    fuel: Math.max(0, definition.fixedFuel + definition.fuelPerMovementCost * movementCost - fuelReserve),
    industry: 0
  };
}

function calculateDeploymentCost(deployment: PendingDeployment, content: ScenarioContent | null): ProjectedOrderCost {
  const definition = content?.reserves.reserves.find((reserve) => reserve.reserveId === deployment.reserveId);
  return definition ? calculateReserveDefinitionCost(definition, content) : zeroProjectedCost;
}

function calculateReserveDefinitionCost(definition: ReserveDefinition, content: ScenarioContent | null): ProjectedOrderCost {
  const command = content?.scenario.commandCosts.Deploy;
  return {
    commandPoints: (command?.baseCommandPoints ?? 0) + definition.cost.commandPoints,
    supplies: (command?.fixedSupplies ?? 0) + definition.cost.supplies,
    manpower: definition.cost.manpower,
    fuel: (command?.fixedFuel ?? 0) + definition.cost.fuel,
    industry: definition.cost.industry
  };
}

function sumOrderCosts(costs: ProjectedOrderCost[]): ProjectedOrderCost {
  return costs.reduce((total, cost) => ({
    commandPoints: total.commandPoints + cost.commandPoints,
    supplies: total.supplies + cost.supplies,
    manpower: total.manpower + cost.manpower,
    fuel: total.fuel + cost.fuel,
    industry: total.industry + cost.industry
  }), { ...zeroProjectedCost });
}

function resolvePlayerResources(state: CampaignStateResponse | null) {
  return state ? state.resources[state.campaign.playerSide as "Axis" | "Allies"] : null;
}

function resolveCommandBudget(state: CampaignStateResponse | null) {
  const resources = resolvePlayerResources(state);
  const modifier = state?.campaignModifiers.reduce((total, item) => total + (item.values.commandPoints ?? 0), 0) ?? 0;
  return Math.max(0, (resources?.commandPoints ?? 0) + modifier);
}

function describePendingOrder(order: PendingOrder) {
  if (order.commandType === "HoldPosition") return `Entrench at ${order.fromRegionName}`;
  if (order.commandType === "Resupply") return `Resupply at ${order.fromRegionName}`;
  return `${order.commandType} ${order.targetRegionName}`;
}

function orderIcon(orderType: OrderType) {
  if (orderType === "Move") return <ArrowRight size={17} />;
  if (orderType === "Attack") return <Crosshair size={17} />;
  if (orderType === "Support") return <Users size={17} />;
  if (orderType === "Recon") return <Activity size={17} />;
  if (orderType === "Resupply") return <Package size={17} />;
  return <Shield size={17} />;
}

function resolveValidTargets(orderType: OrderType, selectedUnit: UnitState | null, state: CampaignStateResponse | null) {
  if (!selectedUnit || !state || orderUsesCurrentRegion(orderType)) {
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

  if (orderType === "Support" || orderType === "Recon") {
    return [currentRegion.id, ...currentRegion.adjacentRegionIds];
  }

  return [];
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

function formatSupplyStatus(unit: UnitState) {
  if (unit.supplyStatus === "OutOfSupply") {
    return `Out (${unit.outOfSupplyTurns}t)`;
  }
  if (unit.supplyStatus === "LowSupply") {
    return "Low";
  }
  return "Connected";
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
  const outcome = content?.scenario.victoryRules.outcomes.find((candidate) => candidate.result === "Victory");
  if (!outcome) {
    return "Victory conditions are defined by the selected scenario.";
  }
  const name = (regionId: string) => content?.map.regions.find((region) => region.id === regionId)?.name ?? regionId;
  return outcome.allOf.map((condition) => {
    if (condition.type === "ControlRegion" && condition.regionId) return `control ${name(condition.regionId)}`;
    if (condition.type === "ControlAtLeast") {
      const turns = (condition.consecutiveTurns ?? 1) > 1 ? ` for ${condition.consecutiveTurns} turns` : "";
      return `control ${condition.requiredCount} of ${(condition.regionIds ?? []).map(name).join(", ")}${turns}`;
    }
    if (condition.type === "SupplyConnected") return `maintain supply to ${(condition.destinationRegionIds ?? []).map(name).join(", ")}`;
    if (condition.type === "VictoryPointsAtLeast") return `hold ${condition.threshold} VP`;
    return condition.type;
  }).join("; ") + ".";
}
