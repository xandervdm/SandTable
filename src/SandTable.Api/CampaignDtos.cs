using SandTable.Engine;

namespace SandTable.Api;

public sealed record CreateCampaignRequest(
    string? Name,
    string? TheatreId,
    string? ScenarioId,
    Side? PlayerSide,
    int? RandomSeed);

public sealed record SubmitCommandsRequest(IReadOnlyList<SubmitCommandRequest> Commands);

public sealed record SubmitCommandRequest(
    int Sequence,
    CommandPayload Command);

public sealed record ChooseTensionOptionRequest(string OptionId);

public enum CampaignEventOrder
{
    Chronological,
    LatestTurnFirst
}

public enum CampaignEventActor
{
    You,
    Enemy,
    System
}

public enum CampaignTimelineMarkerType
{
    Casualty,
    Objective,
    Deployment,
    Tension,
    Victory
}

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
    TheatreMetadataResponse Theatre,
    MapDefinition Map,
    ScenarioDefinition Scenario,
    UnitCatalog Units,
    ReserveCatalog Reserves,
    DoctrineCatalog Doctrines,
    ScenarioEventCatalog Events,
    TensionCardCatalog TensionCards,
    MapAssetCatalogResponse Assets,
    MapDisplayDefinition? Display);

public sealed record MapDisplayDefinition(
    string TheatreId,
    CoordinateSystem CoordinateSystem,
    MapDisplayBackground BackgroundImage,
    IReadOnlyDictionary<string, RegionDisplayDefinition> Regions);

public sealed record MapDisplayBackground(string Url, string Fit);

public sealed record RegionDisplayDefinition(
    Coordinate? LabelOffset,
    DisplayHitArea? HitArea,
    Coordinate? CounterAnchor,
    string? StackDirection);

public sealed record DisplayHitArea(int Rx, int Ry);

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
    IReadOnlyDictionary<Side, Resources> Resources,
    IReadOnlyList<RegionState> Regions,
    IReadOnlyList<RouteState> Routes,
    IReadOnlyList<UnitState> Units,
    IReadOnlyList<ReserveState> Reserves,
    IReadOnlyDictionary<string, int> VictoryProgress,
    IReadOnlyList<string> ScenarioEventHistory,
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
    CampaignEventActor Actor,
    string? RegionId,
    string? UnitId,
    string Summary,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record CampaignTimelineResponse(
    Guid CampaignUid,
    Side PlayerSide,
    Side EnemySide,
    IReadOnlyList<CampaignTimelinePointResponse> Points);

public sealed record CampaignTimelinePointResponse(
    Guid SnapshotUid,
    int TurnNumber,
    int? ResolvedTurnNumber,
    DateOnly CampaignDate,
    IReadOnlyDictionary<Side, CampaignTimelineSideMetricsResponse> Sides,
    IReadOnlyList<CampaignTimelineMarkerResponse> Markers);

public sealed record CampaignTimelineSideMetricsResponse(
    int SurvivingStrength,
    int MaximumStrength,
    decimal ForceStrengthPercent,
    int ActiveUnitCount,
    int DestroyedUnitCount,
    int ControlledVictoryPoints,
    decimal AverageSupply,
    decimal AverageMorale);

public sealed record CampaignTimelineMarkerResponse(
    Guid EventUid,
    int Sequence,
    CampaignTimelineMarkerType MarkerType,
    Side? Side,
    CampaignEventActor Actor,
    string? RegionId,
    string? UnitId,
    string Summary,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record CampaignTurnSummaryResponse(
    Guid CampaignTurnUid,
    int TurnNumber,
    string Status,
    string ResolutionMode,
    string? Summary,
    DateTime? PlayerCommandsCommittedAt,
    DateTime? AiCommandsPlannedAt,
    DateTime? ResolvedAt);

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
