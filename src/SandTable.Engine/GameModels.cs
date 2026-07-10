using System.Text.Json.Serialization;

namespace SandTable.Engine;

public enum Side
{
    Axis,
    Allies,
    Neutral
}

public enum UnitType
{
    Infantry,
    Armour,
    Logistics,
    AirWing
}

public enum UnitStatus
{
    Ready,
    Disrupted,
    Destroyed
}

public enum UnitSupplyStatus
{
    InSupply,
    LowSupply,
    OutOfSupply
}

public enum OrderType
{
    Move,
    Attack,
    Support,
    HoldPosition,
    Resupply,
    Recon,
    Deploy
}

public enum CommandSource
{
    Human,
    AI,
    System
}

public enum GameEventType
{
    Battle,
    Movement,
    Deployment,
    Supply,
    Recon,
    Tension,
    Victory,
    Scenario,
    System
}

public enum RegionKind
{
    PrimaryObjective,
    Objective,
    OperationalPosition,
    EntryPoint
}

public enum VictoryResult
{
    Victory,
    Defeat,
    Draw
}

public enum VictoryConditionType
{
    ControlRegion,
    ControlAtLeast,
    SupplyConnected,
    VictoryPointsAtLeast,
    TurnNumberAtLeast
}

public enum VictorySideSelector
{
    Player,
    Enemy,
    Axis,
    Allies
}

public enum ReserveStatus
{
    Unavailable,
    Available,
    Deployed,
    Removed
}

public enum ScenarioEventPhase
{
    BeforeResolution,
    AfterResolution
}

public enum GameEventScope
{
    Campaign,
    Turn,
    Region,
    Unit
}

public enum TensionCategory
{
    Operational,
    Logistics,
    Weather,
    Political,
    Intelligence
}

public enum TensionTrigger
{
    ArmourOverextended,
    DefensivePressure,
    WeatherForecast,
    PoliticalPressure,
    EnemyConvoy
}

public enum ResourceType
{
    Supplies,
    Manpower,
    Fuel,
    Industry,
    CommandPoints
}

public enum UnitStat
{
    Strength,
    Movement,
    Attack,
    Defence,
    Supply,
    Morale,
    Experience
}

public sealed record Resources(
    int Supplies,
    int Manpower,
    int Fuel,
    int Industry,
    int CommandPoints);

public sealed record CampaignModifier(
    string Id,
    string Name,
    int RemainingTurns,
    IReadOnlyDictionary<string, int> Values);

public sealed record Coordinate(int X, int Y);

public sealed record MapDefinition(
    string TheatreId,
    string Name,
    CoordinateSystem CoordinateSystem,
    IReadOnlyList<RegionDefinition> Regions,
    IReadOnlyList<RouteDefinition> Routes);

public sealed record CoordinateSystem(int Width, int Height);

public sealed record RegionDefinition(
    string Id,
    string Name,
    RegionKind Kind,
    Coordinate Position,
    string Terrain,
    Side Owner,
    int VictoryPoints,
    int SupplyValue,
    IReadOnlyList<string> Features);

public sealed record RouteDefinition(
    string Id,
    string FromRegionId,
    string ToRegionId,
    string RouteType,
    int MovementCost,
    int SupplyCost);

public sealed record ScenarioDefinition(
    string ScenarioId,
    string TheatreId,
    string Name,
    DateOnly StartDate,
    int MaxTurns,
    Side DefaultSide,
    IReadOnlyDictionary<Side, Resources> StartingResources,
    IReadOnlyDictionary<OrderType, CommandCostDefinition> CommandCosts,
    VictoryRulesDefinition VictoryRules,
    IReadOnlyList<string> StartingUnitIds,
    IReadOnlyList<string> ReserveIds,
    int DeploymentLimitPerSidePerTurn);

public sealed record CommandCostDefinition(
    int BaseCommandPoints,
    int FixedSupplies,
    int FixedFuel,
    int SuppliesPerMovementCost,
    int FuelPerMovementCost);

public sealed record VictoryRulesDefinition(IReadOnlyList<VictoryOutcomeDefinition> Outcomes);

public sealed record VictoryOutcomeDefinition(
    string Id,
    VictoryResult Result,
    int Priority,
    IReadOnlyList<VictoryConditionDefinition> AllOf);

public sealed record VictoryConditionDefinition(
    VictoryConditionType Type,
    VictorySideSelector? Side = null,
    string? RegionId = null,
    IReadOnlyList<string>? RegionIds = null,
    int? RequiredCount = null,
    IReadOnlyList<string>? SourceRegionIds = null,
    IReadOnlyList<string>? DestinationRegionIds = null,
    int? Threshold = null,
    int? TurnNumber = null,
    int ConsecutiveTurns = 1);

public sealed record UnitCatalog(IReadOnlyList<UnitDefinition> Units);

public sealed record UnitDefinition(
    string Id,
    string Name,
    Side Side,
    UnitType Type,
    string RegionId,
    int Strength,
    int MaxStrength,
    int Movement,
    int Attack,
    int Defence,
    int Supply,
    int Morale,
    int Experience,
    UnitStatus Status,
    IReadOnlyList<string>? DeploymentRegionIds = null);

public sealed record ReserveCatalog(IReadOnlyList<ReserveDefinition> Reserves);

public sealed record ReserveDefinition(
    string ReserveId,
    string UnitId,
    Side Side,
    int AvailableTurn,
    Resources Cost,
    IReadOnlyList<string> EligibleRegionIds,
    IReadOnlyList<string>? RequiredRegionFeatures = null,
    IReadOnlyList<string>? ScenarioIds = null);

public sealed record DoctrineCatalog(IReadOnlyList<DoctrineDefinition> Doctrines);

public sealed record DoctrineDefinition(
    string Id,
    string Name,
    IReadOnlyDictionary<string, int> Modifiers);

public sealed record ScenarioEventCatalog(IReadOnlyList<ScenarioEventDefinition> Events);

public sealed record ScenarioEventDefinition(
    string Id,
    ScenarioEventTrigger Trigger,
    IReadOnlyList<ScenarioEventCondition> Conditions,
    IReadOnlyList<ScenarioEventEffect> Effects,
    string Message);

public sealed record ScenarioEventTrigger(int Turn, ScenarioEventPhase Phase);

public sealed record ScenarioEventCondition(
    string Type,
    string? RegionId = null,
    string? ReserveId = null,
    VictorySideSelector? Side = null);

public sealed record ScenarioEventEffect(
    string EffectType,
    string Description,
    string? ReserveId = null,
    ReserveStatus? ReserveStatus = null,
    string? UnitId = null,
    string? RegionId = null);

public sealed record GameState(
    string TheatreId,
    string ScenarioId,
    string ScenarioName,
    int TurnNumber,
    int MaxTurns,
    DateOnly CampaignDate,
    Side PlayerSide,
    Side EnemySide,
    IReadOnlyDictionary<Side, Resources> Resources,
    IReadOnlyList<RegionState> Regions,
    IReadOnlyList<RouteState> Routes,
    IReadOnlyList<UnitState> Units,
    IReadOnlyList<ReserveState> Reserves,
    IReadOnlyDictionary<OrderType, CommandCostDefinition> CommandCosts,
    int DeploymentLimitPerSidePerTurn,
    VictoryRulesDefinition VictoryRules,
    IReadOnlyDictionary<string, int> VictoryProgress,
    IReadOnlyList<string> ScenarioEventHistory,
    bool IsComplete,
    VictoryResult? Result,
    IReadOnlyList<StrategicTensionCard>? ActiveTensions = null,
    IReadOnlyList<TensionDecision>? TensionHistory = null,
    IReadOnlyList<CampaignModifier>? CampaignModifiers = null)
{
    public IReadOnlyList<StrategicTensionCard> ActiveTensions { get; init; } = ActiveTensions ?? Array.Empty<StrategicTensionCard>();
    public IReadOnlyList<TensionDecision> TensionHistory { get; init; } = TensionHistory ?? Array.Empty<TensionDecision>();
    public IReadOnlyList<CampaignModifier> CampaignModifiers { get; init; } = CampaignModifiers ?? Array.Empty<CampaignModifier>();
}

public sealed record RegionState(
    string Id,
    string Name,
    RegionKind Kind,
    string Terrain,
    Side Owner,
    int VictoryPoints,
    int SupplyValue,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> AdjacentRegionIds);

public sealed record RouteState(
    string Id,
    string FromRegionId,
    string ToRegionId,
    string RouteType,
    int MovementCost,
    int SupplyCost);

public sealed record UnitState(
    string Id,
    string Name,
    Side Side,
    UnitType Type,
    string RegionId,
    int Strength,
    int MaxStrength,
    int Movement,
    int Attack,
    int Defence,
    int Supply,
    int Morale,
    int Experience,
    UnitStatus Status,
    UnitSupplyStatus SupplyStatus = UnitSupplyStatus.InSupply,
    int OutOfSupplyTurns = 0,
    bool IsEntrenched = false);

public sealed record ReserveState(
    string ReserveId,
    string UnitId,
    Side Side,
    ReserveStatus Status,
    int AvailableTurn,
    int? DeploymentTurn,
    string? DeployedUnitId);

public sealed record TensionCardCatalog(IReadOnlyList<TensionCardDefinition> Cards);

public sealed record TensionCardDefinition(
    string Id,
    string Title,
    string Description,
    TensionCategory Category,
    TensionTrigger Trigger,
    TensionTargeting Targeting,
    IReadOnlyList<TensionOptionDefinition> Options);

public sealed record TensionTargeting(
    string? UnitSelector,
    string? EnemyUnitSelector,
    string? RegionSelector);

public sealed record TensionOptionDefinition(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<GameEffectDefinition> Effects);

public sealed record GameEffectDefinition(
    string EffectType,
    string Description,
    ResourceType? Resource = null,
    int? Amount = null,
    string? ModifierId = null,
    string? Name = null,
    int? DurationTurns = null,
    IReadOnlyDictionary<string, int>? Values = null,
    string? UnitSelector = null,
    UnitStat? Stat = null,
    string? RegionSelector = null,
    Side? Owner = null,
    int? SupplyValueDelta = null,
    string? AddFeature = null,
    string? Summary = null,
    GameEventType? EventType = null,
    GameEventScope? EventScope = null,
    string? SideSelector = null,
    IReadOnlyDictionary<string, object?>? Payload = null);

public sealed record StrategicTensionCard(
    string Id,
    string Title,
    string Description,
    TensionCategory Category,
    TensionTrigger Trigger,
    IReadOnlyList<TensionOption> Options);

public sealed record TensionOption(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<GameEffect> Effects);

public sealed record TensionDecision(
    int TurnNumber,
    Side Side,
    string CardId,
    string CardTitle,
    string OptionId,
    string OptionLabel,
    IReadOnlyList<string> AppliedEffects);

public sealed record ChooseTensionOptionCommand(
    string CardId,
    string OptionId,
    Side Side);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "effectType")]
[JsonDerivedType(typeof(AddResourceEffect), "addResource")]
[JsonDerivedType(typeof(AddCampaignModifierEffect), "addCampaignModifier")]
[JsonDerivedType(typeof(ModifyUnitStatEffect), "modifyUnitStat")]
[JsonDerivedType(typeof(ModifyRegionEffect), "modifyRegion")]
[JsonDerivedType(typeof(AddGameEventEffect), "addGameEvent")]
public abstract record GameEffect(string Description);

public sealed record AddResourceEffect(
    ResourceType Resource,
    int Amount,
    string Description) : GameEffect(Description);

public sealed record AddCampaignModifierEffect(
    string ModifierId,
    string Name,
    int DurationTurns,
    IReadOnlyDictionary<string, int> Values,
    string Description) : GameEffect(Description);

public sealed record ModifyUnitStatEffect(
    string UnitId,
    UnitStat Stat,
    int Amount,
    string Description) : GameEffect(Description);

public sealed record ModifyRegionEffect(
    string RegionId,
    Side? Owner,
    int SupplyValueDelta,
    string? AddFeature,
    string Description) : GameEffect(Description);

public sealed record AddGameEventEffect(
    string Summary,
    GameEventType EventType,
    GameEventScope EventScope,
    Side? Side,
    string? RegionId,
    string? UnitId,
    IReadOnlyDictionary<string, object?> Payload,
    string Description) : GameEffect(Description);

public sealed record SubmittedCommand(
    int Sequence,
    CommandSource Source,
    Side Side,
    CommandPayload Payload)
{
    [JsonIgnore]
    public OrderType CommandType => Payload.CommandType;

    [JsonIgnore]
    public string? UnitId => Payload.UnitId;

    [JsonIgnore]
    public string? RegionId => Payload.RegionId;

    [JsonIgnore]
    public string? TargetRegionId => Payload.TargetRegionId;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "commandType")]
[JsonDerivedType(typeof(MoveCommandPayload), "Move")]
[JsonDerivedType(typeof(AttackCommandPayload), "Attack")]
[JsonDerivedType(typeof(SupportCommandPayload), "Support")]
[JsonDerivedType(typeof(HoldPositionCommandPayload), "HoldPosition")]
[JsonDerivedType(typeof(ResupplyCommandPayload), "Resupply")]
[JsonDerivedType(typeof(ReconCommandPayload), "Recon")]
[JsonDerivedType(typeof(DeployCommandPayload), "Deploy")]
public abstract record CommandPayload
{
    [JsonIgnore]
    public abstract OrderType CommandType { get; }

    [JsonIgnore]
    public virtual string? UnitId => null;

    [JsonIgnore]
    public virtual string? RegionId => null;

    [JsonIgnore]
    public virtual string? TargetRegionId => null;
}

public sealed record MoveCommandPayload(
    [property: JsonPropertyName("unitId")]
    string UnitIdValue,
    [property: JsonPropertyName("fromRegionId")]
    string FromRegionId,
    [property: JsonPropertyName("pathRegionIds")]
    IReadOnlyList<string> PathRegionIds) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Move;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => FromRegionId;
    [JsonIgnore]
    public override string? TargetRegionId => PathRegionIds.LastOrDefault();
}

public sealed record AttackCommandPayload(
    [property: JsonPropertyName("unitId")]
    string UnitIdValue,
    [property: JsonPropertyName("fromRegionId")]
    string FromRegionId,
    [property: JsonPropertyName("pathRegionIds")]
    IReadOnlyList<string> PathRegionIds) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Attack;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => FromRegionId;
    [JsonIgnore]
    public override string? TargetRegionId => PathRegionIds.LastOrDefault();
}

public sealed record SupportCommandPayload(
    [property: JsonPropertyName("unitId")]
    string UnitIdValue,
    [property: JsonPropertyName("fromRegionId")]
    string FromRegionId,
    [property: JsonPropertyName("targetRegionId")]
    string TargetRegionIdValue) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Support;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => FromRegionId;
    [JsonIgnore]
    public override string? TargetRegionId => TargetRegionIdValue;
}

public sealed record HoldPositionCommandPayload(
    [property: JsonPropertyName("unitId")] string UnitIdValue,
    [property: JsonPropertyName("regionId")] string RegionIdValue) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.HoldPosition;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => RegionIdValue;
}

public sealed record ResupplyCommandPayload(
    [property: JsonPropertyName("unitId")] string UnitIdValue,
    [property: JsonPropertyName("regionId")] string RegionIdValue) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Resupply;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => RegionIdValue;
}

public sealed record ReconCommandPayload(
    [property: JsonPropertyName("unitId")]
    string UnitIdValue,
    [property: JsonPropertyName("fromRegionId")]
    string FromRegionId,
    [property: JsonPropertyName("targetRegionId")]
    string TargetRegionIdValue) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Recon;
    [JsonIgnore]
    public override string? UnitId => UnitIdValue;
    [JsonIgnore]
    public override string? RegionId => FromRegionId;
    [JsonIgnore]
    public override string? TargetRegionId => TargetRegionIdValue;
}

public sealed record DeployCommandPayload(
    [property: JsonPropertyName("reserveId")] string ReserveId,
    [property: JsonPropertyName("targetRegionId")] string TargetRegionIdValue) : CommandPayload
{
    [JsonIgnore]
    public override OrderType CommandType => OrderType.Deploy;
    [JsonIgnore]
    public override string? TargetRegionId => TargetRegionIdValue;
}

public sealed record ResolvedCommand(
    SubmittedCommand Command,
    bool Accepted,
    string? RejectionReason,
    Resources Cost);

public sealed record GameEvent(
    int Sequence,
    GameEventType EventType,
    GameEventScope EventScope,
    Side? Side,
    string? RegionId,
    string? UnitId,
    string Summary,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record TurnResolution(
    GameState StartingState,
    GameState NextState,
    IReadOnlyList<ResolvedCommand> Commands,
    IReadOnlyList<GameEvent> Events,
    string Summary);
