import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  Building2,
  CircleDot,
  Crosshair,
  Fuel,
  Loader2,
  MapPin,
  Package,
  Play,
  Plus,
  RefreshCw,
  Shield,
  Star,
  Trash2,
  Users
} from "lucide-react";
import { HttpGameClient } from "./runtime/httpGameClient";
import type {
  CampaignEvent,
  CampaignStateResponse,
  CampaignSummary,
  CampaignTurnSummary,
  GameClient,
  MapDefinition,
  OrderType,
  RegionDefinition,
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
  const [campaigns, setCampaigns] = useState<CampaignSummary[]>([]);
  const [activeCampaignUid, setActiveCampaignUid] = useState<string | null>(null);
  const [scenarioContent, setScenarioContent] = useState<ScenarioContent | null>(null);
  const [campaignState, setCampaignState] = useState<CampaignStateResponse | null>(null);
  const [events, setEvents] = useState<CampaignEvent[]>([]);
  const [turns, setTurns] = useState<CampaignTurnSummary[]>([]);
  const [selectedUnitId, setSelectedUnitId] = useState<string | null>(null);
  const [selectedTargetRegionId, setSelectedTargetRegionId] = useState<string | null>(null);
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
      setCampaignState(state);
      setEvents(campaignEvents);
      setTurns(campaignTurns);
      setActiveCampaignUid(campaign.campaignUid);
      setSelectedTargetRegionId(null);
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
    const theatre = theatres[0];
    const scenario = theatre?.scenarios[0];
    if (!scenario) {
      setError("No theatre scenario is available.");
      return;
    }

    try {
      setBusy("Creating campaign");
      setError(null);
      const detail = await client.createCampaign({
        name: `North Africa Campaign ${formatCampaignNameSuffix()}`,
        scenarioId: scenario.scenarioId,
        playerSide: scenario.defaultSide
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
          pendingOrders.map((order) => ({
            commandType: order.commandType,
            unitId: order.unitId,
            regionId: order.fromRegionId,
            targetRegionId: order.targetRegionId
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
            <span className="overline">North Africa Campaign</span>
            <strong>{activeCampaign?.currentCampaignDate ?? "1942-06-12"}</strong>
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
              <TheatreMap
                map={scenarioContent.map}
                state={campaignState}
                selectedUnitId={selectedUnitId}
                selectedTargetRegionId={selectedTargetRegionId}
                validTargetIds={validTargetIds}
                plannedUnitIds={pendingOrders.map((order) => order.unitId)}
                onUnitSelect={selectUnit}
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
                <span>Start a campaign to load live map state.</span>
                <button className="primary-button" onClick={createCampaign} disabled={Boolean(busy) || theatres.length === 0}>
                  <Plus size={16} />
                  Start North Africa
                </button>
              </div>
            )}
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
  const resources = state?.resources;
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
        <p>
          Capture Cairo and keep the supply chain alive across the desert.
        </p>
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

function TheatreMap({
  map,
  state,
  selectedUnitId,
  selectedTargetRegionId,
  validTargetIds,
  plannedUnitIds,
  onUnitSelect,
  onRegionSelect
}: {
  map: MapDefinition;
  state: CampaignStateResponse;
  selectedUnitId: string | null;
  selectedTargetRegionId: string | null;
  validTargetIds: string[];
  plannedUnitIds: string[];
  onUnitSelect(unitId: string): void;
  onRegionSelect(regionId: string): void;
}) {
  const regionDefinitions = new Map(map.regions.map((region) => [region.id, region]));
  const regionStates = new Map(state.regions.map((region) => [region.id, region]));
  const unitsByRegion = groupUnitsByRegion(state.units);
  const adjacencyTracks = resolveUnroutedAdjacencyTracks(map);

  return (
    <svg className="theatre-map" viewBox={`0 0 ${map.coordinateSystem.width} ${map.coordinateSystem.height}`} role="img">
      <defs>
        <filter id="paperNoise">
          <feTurbulence type="fractalNoise" baseFrequency="0.65" numOctaves="4" seed="18" />
          <feColorMatrix type="saturate" values="0" />
          <feComponentTransfer>
            <feFuncA type="table" tableValues="0 0.16" />
          </feComponentTransfer>
        </filter>
        <linearGradient id="seaGradient" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#78919a" />
          <stop offset="100%" stopColor="#b0b7ad" />
        </linearGradient>
        <radialGradient id="desertGlow" cx="50%" cy="58%" r="70%">
          <stop offset="0%" stopColor="#e8c982" />
          <stop offset="55%" stopColor="#cfaa61" />
          <stop offset="100%" stopColor="#8e7446" />
        </radialGradient>
      </defs>

      <rect width="1000" height="600" fill="#1b211e" />
      <path d="M0 0 H1000 V188 C910 175 850 168 790 184 C710 205 660 182 596 173 C520 163 470 182 398 166 C312 146 250 166 185 151 C110 133 70 149 0 139 Z" fill="url(#seaGradient)" />
      <path d="M0 138 C70 149 110 133 185 151 C250 166 312 146 398 166 C470 182 520 163 596 173 C660 182 710 205 790 184 C850 168 910 175 1000 188 V600 H0 Z" fill="url(#desertGlow)" />
      <rect width="1000" height="600" filter="url(#paperNoise)" opacity="0.75" />
      <path className="coastline" d="M0 138 C70 149 110 133 185 151 C250 166 312 146 398 166 C470 182 520 163 596 173 C660 182 710 205 790 184 C850 168 910 175 1000 188" />

      <g className="terrain-marks">
        <path d="M315 275 l18 -35 l18 35 M352 275 l15 -28 l15 28 M390 470 l18 -32 l18 32 M450 480 l14 -24 l14 24" />
        <path d="M620 365 c38 -20 76 -18 112 4 M205 400 c42 -18 84 -12 126 7 M750 430 c45 -12 88 -6 130 16" />
      </g>

      <g className="adjacency-routes">
        {adjacencyTracks.map((track) => (
          <line
            key={`${track.from.id}-${track.to.id}`}
            x1={track.from.position.x}
            y1={track.from.position.y}
            x2={track.to.position.x}
            y2={track.to.position.y}
          />
        ))}
      </g>

      <g className="routes">
        {map.routes.map((route) => {
          const from = regionDefinitions.get(route.fromRegionId);
          const to = regionDefinitions.get(route.toRegionId);
          if (!from || !to) {
            return null;
          }
          return (
            <line
              key={`${route.fromRegionId}-${route.toRegionId}`}
              x1={from.position.x}
              y1={from.position.y}
              x2={to.position.x}
              y2={to.position.y}
              className={route.routeType.toLowerCase()}
            />
          );
        })}
      </g>

      <g className="regions">
        {map.regions.map((region) => {
          const stateRegion = regionStates.get(region.id);
          const owner = stateRegion?.owner ?? region.owner;
          const valid = validTargetIds.includes(region.id);
          const target = selectedTargetRegionId === region.id;
          return (
            <g
              key={region.id}
              className={`region-node ${owner.toLowerCase()} ${valid ? "valid" : ""} ${target ? "target" : ""}`}
              onClick={() => onRegionSelect(region.id)}
            >
              <ellipse cx={region.position.x} cy={region.position.y} rx="48" ry="30" />
              <circle cx={region.position.x} cy={region.position.y} r="7" />
              <text x={region.position.x} y={region.position.y - 18}>{region.name}</text>
              <text className="vp-label" x={region.position.x} y={region.position.y + 26}>
                VP {stateRegion?.victoryPoints ?? region.victoryPoints}
              </text>
              <FeatureMarks region={region} />
            </g>
          );
        })}
      </g>

      <g className="units">
        {Array.from(unitsByRegion.entries()).map(([regionId, units]) => {
          const region = regionDefinitions.get(regionId);
          if (!region) {
            return null;
          }
          return units.map((unit, index) => {
            const offsetX = 22 + index * 36;
            const offsetY = index % 2 === 0 ? -6 : 36;
            const x = region.position.x + offsetX;
            const y = region.position.y + offsetY;
            return (
              <g
                key={unit.id}
                className={`unit-counter ${unit.side.toLowerCase()} ${unit.id === selectedUnitId ? "selected" : ""} ${plannedUnitIds.includes(unit.id) ? "planned" : ""}`}
                transform={`translate(${x} ${y})`}
                onClick={(event) => {
                  event.stopPropagation();
                  onUnitSelect(unit.id);
                }}
              >
                <rect x="-24" y="-21" width="48" height="42" rx="4" />
                <text className="unit-type" y="-4">{unitCode(unit)}</text>
                <text className="unit-strength" y="13">{unit.strength}</text>
              </g>
            );
          });
        })}
      </g>
    </svg>
  );
}

function FeatureMarks({ region }: { region: RegionDefinition }) {
  const marks = region.features.slice(0, 3);
  return (
    <>
      {marks.map((feature, index) => (
        <text
          key={feature}
          className="feature-mark"
          x={region.position.x - 26 + index * 16}
          y={region.position.y + 44}
        >
          {feature.charAt(0)}
        </text>
      ))}
    </>
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

function resolveUnroutedAdjacencyTracks(map: MapDefinition) {
  const regionsById = new Map(map.regions.map((region) => [region.id, region]));
  const routedEdges = new Set(map.routes.map((route) => edgeKey(route.fromRegionId, route.toRegionId)));

  return map.regions.flatMap((region) =>
    region.adjacentRegionIds.flatMap((adjacentRegionId) => {
      if (region.id > adjacentRegionId || routedEdges.has(edgeKey(region.id, adjacentRegionId))) {
        return [];
      }

      const adjacentRegion = regionsById.get(adjacentRegionId);
      return adjacentRegion ? [{ from: region, to: adjacentRegion }] : [];
    })
  );
}

function edgeKey(firstRegionId: string, secondRegionId: string) {
  return [firstRegionId, secondRegionId].sort().join("::");
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

function formatCampaignNameSuffix(date = new Date()) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hour = String(date.getHours()).padStart(2, "0");
  const minute = String(date.getMinutes()).padStart(2, "0");
  const second = String(date.getSeconds()).padStart(2, "0");
  return `${year}${month}${day}-${hour}${minute}${second}`;
}
