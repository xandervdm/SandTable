using SandTable.Engine;

namespace SandTable.Api;

public sealed record CreateCampaignRequest(
    string? Name,
    string? ScenarioId,
    Side? PlayerSide,
    int? RandomSeed);

public sealed record SubmitCommandsRequest(IReadOnlyList<SubmitCommandRequest> Commands);

public sealed record SubmitCommandRequest(
    OrderType CommandType,
    string? UnitId,
    string? RegionId,
    string? TargetRegionId);

public sealed record ChooseTensionOptionRequest(string OptionId);

public sealed record TheatreSummaryResponse(
    string TheatreId,
    string Name,
    IReadOnlyList<ScenarioSummaryResponse> Scenarios);

public sealed record ScenarioSummaryResponse(
    string ScenarioId,
    string TheatreId,
    string Name,
    DateOnly StartDate,
    int MaxTurns,
    Side DefaultSide);

public sealed record ScenarioContentResponse(
    MapDefinition Map,
    ScenarioDefinition Scenario,
    UnitCatalog Units,
    DoctrineCatalog Doctrines,
    ScenarioEventCatalog Events);

public sealed record CampaignSummaryResponse(
    Guid CampaignUid,
    string Name,
    string TheatreId,
    string ScenarioId,
    Side PlayerSide,
    Side EnemySide,
    string Status,
    int CurrentTurnNumber,
    int MaxTurns,
    DateOnly CurrentCampaignDate,
    string? Result,
    int? Score);

public sealed record CampaignDetailResponse(
    CampaignSummaryResponse Campaign,
    Guid LatestSnapshotUid,
    GameState State);

public sealed record SnapshotResponse(
    Guid SnapshotUid,
    Guid CampaignUid,
    string SnapshotType,
    int TurnNumber,
    bool IsLatest,
    GameState State);

public sealed record CampaignStateResponse(
    CampaignSummaryResponse Campaign,
    Guid SnapshotUid,
    int TurnNumber,
    DateOnly CampaignDate,
    Resources Resources,
    IReadOnlyList<RegionState> Regions,
    IReadOnlyList<UnitState> Units,
    IReadOnlyList<StrategicTensionCard> ActiveTensions,
    IReadOnlyList<TensionDecision> TensionHistory,
    IReadOnlyList<CampaignModifier> CampaignModifiers,
    bool IsComplete,
    string? Result);

public sealed record CampaignEventResponse(
    Guid EventUid,
    Guid CampaignTurnUid,
    int TurnNumber,
    int Sequence,
    GameEventType EventType,
    GameEventScope EventScope,
    Side? Side,
    string? RegionId,
    string? UnitId,
    string Summary,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record SubmitCommandsResponse(
    Guid CampaignUid,
    Guid CampaignTurnUid,
    int AcceptedCommandCount);

public sealed record ResolveTurnResponse(
    Guid CampaignUid,
    Guid CampaignTurnUid,
    Guid SnapshotUid,
    int ResolvedTurnNumber,
    int NextTurnNumber,
    bool IsComplete,
    string? Result,
    string Summary,
    IReadOnlyList<GameEvent> Events);

public sealed record ChooseTensionOptionResponse(
    Guid CampaignUid,
    Guid CampaignTurnUid,
    Guid SnapshotUid,
    TensionDecision Decision,
    GameState State,
    IReadOnlyList<GameEvent> Events);
