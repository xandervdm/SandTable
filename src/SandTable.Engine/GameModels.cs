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

public enum OrderType
{
    Move,
    Attack,
    Support,
    HoldPosition,
    Resupply,
    Recon
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
    Supply,
    Recon,
    Victory,
    Scenario,
    System
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
    Coordinate Position,
    string Terrain,
    Side Owner,
    int VictoryPoints,
    int SupplyValue,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> AdjacentRegionIds);

public sealed record RouteDefinition(
    string FromRegionId,
    string ToRegionId,
    string RouteType);

public sealed record ScenarioDefinition(
    string ScenarioId,
    string TheatreId,
    string Name,
    DateOnly StartDate,
    int MaxTurns,
    Side DefaultSide,
    Resources StartingResources,
    IReadOnlyList<VictoryConditionDefinition> VictoryConditions,
    IReadOnlyList<string> StartingUnitIds);

public sealed record VictoryConditionDefinition(
    string Type,
    string RegionId,
    Side RequiredOwner);

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
    UnitStatus Status);

public sealed record DoctrineCatalog(IReadOnlyList<DoctrineDefinition> Doctrines);

public sealed record DoctrineDefinition(
    string Id,
    string Name,
    IReadOnlyDictionary<string, int> Modifiers);

public sealed record ScenarioEventCatalog(IReadOnlyList<ScenarioEventDefinition> Events);

public sealed record ScenarioEventDefinition(
    string Id,
    ScenarioEventTrigger Trigger,
    ScenarioEventEffect Effect,
    string Message);

public sealed record ScenarioEventTrigger(int Turn);

public sealed record ScenarioEventEffect(
    string Type,
    string UnitId,
    string RegionId);

public sealed record GameState(
    string TheatreId,
    string ScenarioId,
    string ScenarioName,
    int TurnNumber,
    int MaxTurns,
    DateOnly CampaignDate,
    Side PlayerSide,
    Side EnemySide,
    Resources Resources,
    IReadOnlyList<RegionState> Regions,
    IReadOnlyList<UnitState> Units,
    bool IsComplete,
    string? Result,
    string? VictoryRegionId,
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
    string Terrain,
    Side Owner,
    int VictoryPoints,
    int SupplyValue,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> AdjacentRegionIds);

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
    UnitStatus Status);

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
    OrderType CommandType,
    string? UnitId,
    string? RegionId,
    string? TargetRegionId);

public sealed record ResolvedCommand(
    SubmittedCommand Command,
    bool Accepted,
    string? RejectionReason);

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
