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
