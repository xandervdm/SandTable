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

public sealed record Resources(
    int Supplies,
    int Manpower,
    int Fuel,
    int Industry,
    int CommandPoints);

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
    string? VictoryRegionId);

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
