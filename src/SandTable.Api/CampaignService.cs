using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Npgsql;
using SandTable.Engine;

namespace SandTable.Api;

public sealed class CampaignService(
    SandTableConnectionFactory connectionFactory,
    DevPlayerBootstrapper devPlayerBootstrapper,
    GameContentRepository contentRepository,
    ScenarioFactory scenarioFactory,
    BasicAiPlanner aiPlanner,
    TurnResolver turnResolver,
    TensionChoiceResolver tensionChoiceResolver)
{
    private const string Actor = "system:dev-user";
    private const string EngineVersion = "sandtable-engine-v1";

    public async Task<CampaignDetailResponse> CreateCampaignAsync(
        CreateCampaignRequest request,
        CancellationToken cancellationToken)
    {
        var scenarioId = string.IsNullOrWhiteSpace(request.ScenarioId) ? "north-africa-1942" : request.ScenarioId;
        var content = await contentRepository.LoadAsync("north-africa", scenarioId, cancellationToken);
        var playerSide = request.PlayerSide ?? content.Scenario.DefaultSide;
        var initialState = scenarioFactory.CreateInitialState(content.Map, content.Scenario, content.Units, playerSide);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var devPlayer = await devPlayerBootstrapper.EnsureAsync(connection, transaction, cancellationToken);
        var campaignName = string.IsNullOrWhiteSpace(request.Name) ? content.Scenario.Name : request.Name.Trim();
        var seed = request.RandomSeed ?? RandomNumberGenerator.GetInt32(1, int.MaxValue);

        var campaign = await connection.QuerySingleAsync<CampaignIdentity>(
            new CommandDefinition(
                """
                insert into public.campaign (
                    user_account_id,
                    player_profile_id,
                    command_profile_id,
                    theatre_id,
                    scenario_id,
                    name,
                    player_side,
                    enemy_side,
                    status,
                    current_turn_number,
                    max_turns,
                    campaign_start_date,
                    current_campaign_date,
                    created_by,
                    edited_by
                )
                values (
                    @UserAccountId,
                    @PlayerProfileId,
                    @CommandProfileId,
                    @TheatreId,
                    @ScenarioId,
                    @Name,
                    @PlayerSide,
                    @EnemySide,
                    'Active',
                    @CurrentTurnNumber,
                    @MaxTurns,
                    @CampaignStartDate,
                    @CurrentCampaignDate,
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new
                {
                    devPlayer.UserAccountId,
                    devPlayer.PlayerProfileId,
                    devPlayer.CommandProfileId,
                    initialState.TheatreId,
                    initialState.ScenarioId,
                    Name = campaignName,
                    PlayerSide = initialState.PlayerSide.ToString(),
                    EnemySide = initialState.EnemySide.ToString(),
                    CurrentTurnNumber = initialState.TurnNumber,
                    MaxTurns = initialState.MaxTurns,
                    CampaignStartDate = ToDatabaseDate(initialState.CampaignDate),
                    CurrentCampaignDate = ToDatabaseDate(initialState.CampaignDate),
                    Actor
                },
                transaction,
                cancellationToken: cancellationToken));

        var turn = await connection.QuerySingleAsync<CampaignIdentity>(
            new CommandDefinition(
                """
                insert into public.campaign_turn (
                    campaign_id,
                    turn_number,
                    status,
                    random_seed,
                    created_by,
                    edited_by
                )
                values (
                    @CampaignId,
                    @TurnNumber,
                    'Planning',
                    @RandomSeed,
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new
                {
                    CampaignId = campaign.Id,
                    TurnNumber = initialState.TurnNumber,
                    RandomSeed = seed,
                    Actor
                },
                transaction,
                cancellationToken: cancellationToken));

        var snapshot = await InsertSnapshotAsync(
            connection,
            transaction,
            campaign.Id,
            campaign.Uid,
            turn.Id,
            "Initial",
            initialState,
            seed,
            isLatest: true,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new CampaignDetailResponse(
            new CampaignSummaryResponse(
                campaign.Uid,
                campaignName,
                initialState.TheatreId,
                initialState.ScenarioId,
                initialState.PlayerSide,
                initialState.EnemySide,
                "Active",
                initialState.TurnNumber,
                initialState.MaxTurns,
                initialState.CampaignDate,
                null,
                null),
            snapshot.Uid,
            initialState);
    }

    public async Task<IReadOnlyList<CampaignSummaryResponse>> ListCampaignsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CampaignRow>(
            new CommandDefinition(
                """
                select
                    uid as CampaignUid,
                    name,
                    theatre_id as TheatreId,
                    scenario_id as ScenarioId,
                    player_side as PlayerSide,
                    enemy_side as EnemySide,
                    status,
                    current_turn_number as CurrentTurnNumber,
                    max_turns as MaxTurns,
                    current_campaign_date as CurrentCampaignDate,
                    result,
                    score
                from public.campaign
                order by edited_at desc
                limit 100
                """,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<CampaignDetailResponse?> GetCampaignAsync(Guid campaignUid, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, null, campaign.Id, cancellationToken);
        return new CampaignDetailResponse(campaign.ToSummary(), snapshot.SnapshotUid, snapshot.State);
    }

    public async Task<SnapshotResponse?> GetLatestSnapshotAsync(Guid campaignUid, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, null, campaign.Id, cancellationToken);
        return new SnapshotResponse(
            snapshot.SnapshotUid,
            campaign.CampaignUid,
            snapshot.SnapshotType,
            snapshot.TurnNumber,
            snapshot.IsLatest,
            snapshot.State);
    }

    public async Task<CampaignStateResponse?> GetCampaignStateAsync(Guid campaignUid, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, null, campaign.Id, cancellationToken);
        var state = snapshot.State;
        return new CampaignStateResponse(
            campaign.ToSummary(),
            snapshot.SnapshotUid,
            state.TurnNumber,
            state.CampaignDate,
            state.Resources,
            state.Regions,
            state.Units,
            state.ActiveTensions,
            state.TensionHistory,
            state.CampaignModifiers,
            state.IsComplete,
            state.Result);
    }

    public async Task<IReadOnlyList<CampaignEventResponse>?> ListCampaignEventsAsync(
        Guid campaignUid,
        int? turnNumber,
        int limit,
        CampaignEventOrder order,
        CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var orderBy = EventOrderSql(order);
        var rows = await connection.QueryAsync<CampaignEventRow>(
            new CommandDefinition(
                $"""
                select
                    ce.uid as EventUid,
                    ct.uid as CampaignTurnUid,
                    ct.turn_number as TurnNumber,
                    ce.event_sequence as Sequence,
                    ce.event_type as EventType,
                    ce.event_scope as EventScope,
                    ce.side,
                    ce.region_id as RegionId,
                    ce.unit_id as UnitId,
                    ce.summary,
                    ce.event_payload::text as PayloadJson
                from public.campaign_event ce
                inner join public.campaign_turn ct on ct.id = ce.campaign_turn_id
                where ce.campaign_id = @CampaignId
                    and (@TurnNumber is null or ct.turn_number = @TurnNumber)
                order by {orderBy}
                limit @Limit
                """,
                new
                {
                    CampaignId = campaign.Id,
                    TurnNumber = turnNumber,
                    Limit = safeLimit
                },
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToResponse()).ToArray();
    }

    public async Task<IReadOnlyList<CampaignTurnSummaryResponse>?> ListCampaignTurnsAsync(
        Guid campaignUid,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var rows = await connection.QueryAsync<CampaignTurnSummaryRow>(
            new CommandDefinition(
                """
                select
                    uid as CampaignTurnUid,
                    turn_number as TurnNumber,
                    status,
                    resolution_mode as ResolutionMode,
                    turn_summary as Summary,
                    player_commands_committed_at as PlayerCommandsCommittedAt,
                    ai_commands_planned_at as AiCommandsPlannedAt,
                    resolved_at as ResolvedAt
                from public.campaign_turn
                where campaign_id = @CampaignId
                order by turn_number
                limit @Limit
                """,
                new
                {
                    CampaignId = campaign.Id,
                    Limit = safeLimit
                },
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToResponse()).ToArray();
    }

    public async Task<CampaignTurnSummaryResponse?> GetCampaignTurnAsync(
        Guid campaignUid,
        int turnNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        var campaign = await LoadCampaignAsync(connection, null, campaignUid, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var row = await connection.QuerySingleOrDefaultAsync<CampaignTurnSummaryRow>(
            new CommandDefinition(
                """
                select
                    uid as CampaignTurnUid,
                    turn_number as TurnNumber,
                    status,
                    resolution_mode as ResolutionMode,
                    turn_summary as Summary,
                    player_commands_committed_at as PlayerCommandsCommittedAt,
                    ai_commands_planned_at as AiCommandsPlannedAt,
                    resolved_at as ResolvedAt
                from public.campaign_turn
                where campaign_id = @CampaignId
                    and turn_number = @TurnNumber
                """,
                new
                {
                    CampaignId = campaign.Id,
                    TurnNumber = turnNumber
                },
                cancellationToken: cancellationToken));

        return row?.ToResponse();
    }

    public async Task<SubmitCommandsResponse?> SubmitCommandsAsync(
        Guid campaignUid,
        SubmitCommandsRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var campaign = await LoadCampaignAsync(connection, transaction, campaignUid, cancellationToken, forUpdate: true);
        if (campaign is null)
        {
            return null;
        }

        var turn = await LoadCurrentPlanningTurnAsync(connection, transaction, campaign.Id, campaign.CurrentTurnNumber, cancellationToken);
        if (turn is null)
        {
            throw new ApiValidationException(
                "Invalid turn status",
                $"Campaign '{campaignUid}' is not accepting commands for turn {campaign.CurrentTurnNumber}.");
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, transaction, campaign.Id, cancellationToken);
        CommandValidator.ValidateSubmitCommands(snapshot.State, Enum.Parse<Side>(campaign.PlayerSide), request);

        var nextSequence = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                """
                select coalesce(max(command_sequence), 0) + 1
                from public.campaign_command
                where campaign_turn_id = @CampaignTurnId
                    and command_source = 'Human'
                """,
                new { CampaignTurnId = turn.Id },
                transaction,
                cancellationToken: cancellationToken));

        for (var index = 0; index < request.Commands.Count; index++)
        {
            var command = request.Commands[index];
            var regionId = command.RegionId ?? snapshot.State.Units.FirstOrDefault(unit => unit.Id == command.UnitId)?.RegionId;
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    insert into public.campaign_command (
                        campaign_id,
                        campaign_turn_id,
                        planned_from_snapshot_id,
                        command_sequence,
                        command_source,
                        side,
                        unit_id,
                        region_id,
                        command_type,
                        status,
                        command_payload,
                        created_by,
                        edited_by
                    )
                    values (
                        @CampaignId,
                        @CampaignTurnId,
                        @SnapshotId,
                        @CommandSequence,
                        'Human',
                        @Side,
                        @UnitId,
                        @RegionId,
                        @CommandType,
                        'Planned',
                        cast(@CommandPayload as jsonb),
                        @Actor,
                        @Actor
                    )
                    """,
                    new
                    {
                        CampaignId = campaign.Id,
                        CampaignTurnId = turn.Id,
                        SnapshotId = snapshot.Id,
                        CommandSequence = nextSequence + index,
                        Side = campaign.PlayerSide,
                        command.UnitId,
                        RegionId = regionId,
                        CommandType = command.CommandType.ToString(),
                        CommandPayload = JsonSerializer.Serialize(new CommandPayload(command.TargetRegionId), ApiJson.SerializerOptions),
                        Actor
                    },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_turn
                set
                    status = 'Committed',
                    player_commands_committed_at = now(),
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where id = @CampaignTurnId
                """,
                new { CampaignTurnId = turn.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new SubmitCommandsResponse(campaign.CampaignUid, turn.Uid, request.Commands.Count);
    }

    public async Task<ChooseTensionOptionResponse?> ChooseTensionOptionAsync(
        Guid campaignUid,
        string cardId,
        ChooseTensionOptionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OptionId))
        {
            throw new ApiValidationException(
                "Invalid tension choice",
                "A tension option id is required.");
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var campaign = await LoadCampaignAsync(connection, transaction, campaignUid, cancellationToken, forUpdate: true);
        if (campaign is null)
        {
            return null;
        }

        var turn = await LoadCurrentPlanningTurnAsync(connection, transaction, campaign.Id, campaign.CurrentTurnNumber, cancellationToken);
        if (turn is null)
        {
            throw new ApiValidationException(
                "Invalid turn status",
                $"Campaign '{campaignUid}' is not accepting operational opportunity choices for turn {campaign.CurrentTurnNumber}.");
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, transaction, campaign.Id, cancellationToken);
        var nextEventSequence = await LoadNextEventSequenceAsync(connection, transaction, turn.Id, cancellationToken);

        var result = tensionChoiceResolver.Choose(
            snapshot.State,
            new ChooseTensionOptionCommand(cardId, request.OptionId, snapshot.State.PlayerSide),
            nextEventSequence);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_snapshot
                set
                    is_latest = false,
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where campaign_id = @CampaignId
                    and is_latest = true
                """,
                new { CampaignId = campaign.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        var newSnapshot = await InsertSnapshotAsync(
            connection,
            transaction,
            campaign.Id,
            campaign.CampaignUid,
            turn.Id,
            "Autosave",
            result.State,
            turn.RandomSeed,
            isLatest: true,
            cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_turn
                set
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where id = @CampaignTurnId
                """,
                new { CampaignTurnId = turn.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        foreach (var gameEvent in result.Events)
        {
            await InsertGameEventAsync(
                connection,
                transaction,
                campaign.Id,
                turn.Id,
                gameEvent,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ChooseTensionOptionResponse(
            campaign.CampaignUid,
            turn.Uid,
            newSnapshot.Uid,
            result.Decision,
            result.State,
            result.Events);
    }

    public async Task<ResolveTurnResponse?> ResolveTurnAsync(Guid campaignUid, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var campaign = await LoadCampaignAsync(connection, transaction, campaignUid, cancellationToken, forUpdate: true);
        if (campaign is null)
        {
            return null;
        }

        var turn = await LoadCurrentTurnForResolutionAsync(connection, transaction, campaign.Id, campaign.CurrentTurnNumber, cancellationToken);
        if (turn is null)
        {
            throw new ApiValidationException(
                "Invalid turn status",
                $"Campaign '{campaignUid}' does not have a planning or committed turn {campaign.CurrentTurnNumber} to resolve.");
        }

        var snapshot = await LoadLatestSnapshotAsync(connection, transaction, campaign.Id, cancellationToken);
        var content = await contentRepository.LoadAsync(campaign.TheatreId, campaign.ScenarioId, cancellationToken);
        var humanCommands = await LoadCommandsAsync(connection, transaction, turn.Id, "Human", cancellationToken);
        var aiCommands = aiPlanner.Plan(snapshot.State, snapshot.State.EnemySide);

        await InsertAiCommandsAsync(connection, transaction, campaign.Id, turn.Id, snapshot.Id, aiCommands, cancellationToken);

        var resolution = turnResolver.Resolve(snapshot.State, humanCommands, aiCommands, turn.RandomSeed, content.TensionCards);
        foreach (var command in resolution.Commands)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    update public.campaign_command
                    set
                        status = @Status,
                        rejection_reason = @RejectionReason,
                        edited_at = now(),
                        edited_by = @Actor,
                        version = version + 1
                    where campaign_turn_id = @CampaignTurnId
                        and command_source = @CommandSource
                        and command_sequence = @CommandSequence
                    """,
                    new
                    {
                        CampaignTurnId = turn.Id,
                        CommandSource = command.Command.Source.ToString(),
                        CommandSequence = command.Command.Sequence,
                        Status = command.Accepted ? "Resolved" : "Rejected",
                        command.RejectionReason,
                        Actor
                    },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_snapshot
                set
                    is_latest = false,
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where campaign_id = @CampaignId
                    and is_latest = true
                """,
                new { CampaignId = campaign.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        var newSnapshot = await InsertSnapshotAsync(
            connection,
            transaction,
            campaign.Id,
            campaign.CampaignUid,
            turn.Id,
            "TurnResolved",
            resolution.NextState,
            turn.RandomSeed,
            isLatest: true,
            cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_turn
                set
                    status = 'Resolved',
                    ai_commands_planned_at = coalesce(ai_commands_planned_at, now()),
                    resolved_at = now(),
                    turn_summary = @TurnSummary,
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where id = @CampaignTurnId
                """,
                new { CampaignTurnId = turn.Id, TurnSummary = resolution.Summary, Actor },
                transaction,
                cancellationToken: cancellationToken));

        var campaignStatus = resolution.NextState.IsComplete ? "Completed" : "Active";
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign
                set
                    status = @Status,
                    current_turn_number = @CurrentTurnNumber,
                    current_campaign_date = @CurrentCampaignDate,
                    result = @Result,
                    completed_at = case when @Status = 'Completed' then now() else completed_at end,
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where id = @CampaignId
                """,
                new
                {
                    CampaignId = campaign.Id,
                    Status = campaignStatus,
                    CurrentTurnNumber = resolution.NextState.TurnNumber,
                    CurrentCampaignDate = ToDatabaseDate(resolution.NextState.CampaignDate),
                    resolution.NextState.Result,
                    Actor
                },
                transaction,
                cancellationToken: cancellationToken));

        if (!resolution.NextState.IsComplete && resolution.NextState.TurnNumber <= resolution.NextState.MaxTurns)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    insert into public.campaign_turn (
                        campaign_id,
                        turn_number,
                        status,
                        random_seed,
                        created_by,
                        edited_by
                    )
                    values (
                        @CampaignId,
                        @TurnNumber,
                        'Planning',
                        @RandomSeed,
                        @Actor,
                        @Actor
                    )
                    on conflict (campaign_id, turn_number) do nothing
                    """,
                    new
                    {
                        CampaignId = campaign.Id,
                        TurnNumber = resolution.NextState.TurnNumber,
                        RandomSeed = RandomNumberGenerator.GetInt32(1, int.MaxValue),
                        Actor
                    },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        var persistedEvents = new List<GameEvent>();
        var nextEventSequence = await LoadNextEventSequenceAsync(connection, transaction, turn.Id, cancellationToken);
        foreach (var gameEvent in resolution.Events)
        {
            var persistedEvent = gameEvent with { Sequence = nextEventSequence++ };
            persistedEvents.Add(persistedEvent);
            await InsertGameEventAsync(
                connection,
                transaction,
                campaign.Id,
                turn.Id,
                persistedEvent,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ResolveTurnResponse(
            campaign.CampaignUid,
            turn.Uid,
            newSnapshot.Uid,
            turn.TurnNumber,
            resolution.NextState.TurnNumber,
            resolution.NextState.IsComplete,
            resolution.NextState.Result,
            resolution.Summary,
            persistedEvents);
    }

    private static async Task<CampaignIdentity> InsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignId,
        Guid campaignUid,
        long campaignTurnId,
        string snapshotType,
        GameState state,
        int randomSeed,
        bool isLatest,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<CampaignIdentity>(
            new CommandDefinition(
                """
                insert into public.campaign_snapshot (
                    campaign_id,
                    campaign_turn_id,
                    snapshot_type,
                    turn_number,
                    game_state,
                    engine_version,
                    random_seed,
                    state_hash,
                    is_latest,
                    created_by,
                    edited_by
                )
                values (
                    @CampaignId,
                    @CampaignTurnId,
                    @SnapshotType,
                    @TurnNumber,
                    cast(@GameState as jsonb),
                    @EngineVersion,
                    @RandomSeed,
                    @StateHash,
                    @IsLatest,
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new
                {
                    CampaignId = campaignId,
                    CampaignUid = campaignUid,
                    CampaignTurnId = campaignTurnId,
                    SnapshotType = snapshotType,
                    TurnNumber = state.TurnNumber,
                    GameState = JsonSerializer.Serialize(state, ApiJson.SerializerOptions),
                    EngineVersion,
                    RandomSeed = randomSeed,
                    StateHash = CreateStateHash(state),
                    IsLatest = isLatest,
                    Actor
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<CampaignRow?> LoadCampaignAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid campaignUid,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
            select
                id,
                uid as CampaignUid,
                name,
                theatre_id as TheatreId,
                scenario_id as ScenarioId,
                player_side as PlayerSide,
                enemy_side as EnemySide,
                status,
                current_turn_number as CurrentTurnNumber,
                max_turns as MaxTurns,
                current_campaign_date as CurrentCampaignDate,
                result,
                score
            from public.campaign
            where uid = @CampaignUid
            """;

        if (forUpdate)
        {
            sql += " for update";
        }

        return await connection.QuerySingleOrDefaultAsync<CampaignRow>(
            new CommandDefinition(sql, new { CampaignUid = campaignUid }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task<SnapshotRow> LoadLatestSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        long campaignId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<SnapshotStorageRow>(
            new CommandDefinition(
                """
                select
                    id,
                    uid as SnapshotUid,
                    snapshot_type as SnapshotType,
                    turn_number as TurnNumber,
                    is_latest as IsLatest,
                    game_state::text as GameStateJson
                from public.campaign_snapshot
                where campaign_id = @CampaignId
                    and is_latest = true
                """,
                new { CampaignId = campaignId },
                transaction,
                cancellationToken: cancellationToken));

        return row.ToSnapshotRow();
    }

    private static async Task<CampaignTurnRow?> LoadCurrentPlanningTurnAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignId,
        int currentTurnNumber,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<CampaignTurnRow>(
            new CommandDefinition(
                """
                select id, uid, turn_number as TurnNumber, random_seed as RandomSeed, status
                from public.campaign_turn
                where campaign_id = @CampaignId
                    and turn_number = @TurnNumber
                    and status = 'Planning'
                for update
                """,
                new { CampaignId = campaignId, TurnNumber = currentTurnNumber },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<CampaignTurnRow?> LoadCurrentTurnForResolutionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignId,
        int currentTurnNumber,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<CampaignTurnRow>(
            new CommandDefinition(
                """
                select id, uid, turn_number as TurnNumber, random_seed as RandomSeed, status
                from public.campaign_turn
                where campaign_id = @CampaignId
                    and turn_number = @TurnNumber
                    and status in ('Planning', 'Committed')
                for update
                """,
                new { CampaignId = campaignId, TurnNumber = currentTurnNumber },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<SubmittedCommand>> LoadCommandsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignTurnId,
        string commandSource,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<CommandStorageRow>(
            new CommandDefinition(
                """
                select
                    command_sequence as Sequence,
                    command_source as Source,
                    side,
                    unit_id as UnitId,
                    region_id as RegionId,
                    command_type as CommandType,
                    command_payload::text as CommandPayloadJson
                from public.campaign_command
                where campaign_turn_id = @CampaignTurnId
                    and command_source = @CommandSource
                    and status = 'Planned'
                order by command_sequence
                """,
                new { CampaignTurnId = campaignTurnId, CommandSource = commandSource },
                transaction,
                cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSubmittedCommand()).ToArray();
    }

    private static async Task InsertAiCommandsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignId,
        long campaignTurnId,
        long snapshotId,
        IReadOnlyList<SubmittedCommand> aiCommands,
        CancellationToken cancellationToken)
    {
        foreach (var command in aiCommands)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    insert into public.campaign_command (
                        campaign_id,
                        campaign_turn_id,
                        planned_from_snapshot_id,
                        command_sequence,
                        command_source,
                        side,
                        unit_id,
                        region_id,
                        command_type,
                        status,
                        command_payload,
                        created_by,
                        edited_by
                    )
                    values (
                        @CampaignId,
                        @CampaignTurnId,
                        @SnapshotId,
                        @CommandSequence,
                        'AI',
                        @Side,
                        @UnitId,
                        @RegionId,
                        @CommandType,
                        'Planned',
                        cast(@CommandPayload as jsonb),
                        @Actor,
                        @Actor
                    )
                    on conflict (campaign_turn_id, command_source, command_sequence) do nothing
                    """,
                    new
                    {
                        CampaignId = campaignId,
                        CampaignTurnId = campaignTurnId,
                        SnapshotId = snapshotId,
                        CommandSequence = command.Sequence,
                        Side = command.Side.ToString(),
                        command.UnitId,
                        command.RegionId,
                        CommandType = command.CommandType.ToString(),
                        CommandPayload = JsonSerializer.Serialize(new CommandPayload(command.TargetRegionId), ApiJson.SerializerOptions),
                        Actor
                    },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                update public.campaign_turn
                set
                    ai_commands_planned_at = coalesce(ai_commands_planned_at, now()),
                    edited_at = now(),
                    edited_by = @Actor,
                    version = version + 1
                where id = @CampaignTurnId
                """,
                new { CampaignTurnId = campaignTurnId, Actor },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task InsertGameEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignId,
        long campaignTurnId,
        GameEvent gameEvent,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                insert into public.campaign_event (
                    campaign_id,
                    campaign_turn_id,
                    event_sequence,
                    event_type,
                    event_scope,
                    side,
                    region_id,
                    unit_id,
                    summary,
                    event_payload,
                    created_by,
                    edited_by
                )
                values (
                    @CampaignId,
                    @CampaignTurnId,
                    @EventSequence,
                    @EventType,
                    @EventScope,
                    @Side,
                    @RegionId,
                    @UnitId,
                    @Summary,
                    cast(@EventPayload as jsonb),
                    @Actor,
                    @Actor
                )
                """,
                new
                {
                    CampaignId = campaignId,
                    CampaignTurnId = campaignTurnId,
                    EventSequence = gameEvent.Sequence,
                    EventType = gameEvent.EventType.ToString(),
                    EventScope = gameEvent.EventScope.ToString(),
                    Side = gameEvent.Side?.ToString(),
                    gameEvent.RegionId,
                    gameEvent.UnitId,
                    gameEvent.Summary,
                    EventPayload = JsonSerializer.Serialize(gameEvent.Payload, ApiJson.SerializerOptions),
                    Actor
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<int> LoadNextEventSequenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long campaignTurnId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                """
                select coalesce(max(event_sequence), 0) + 1
                from public.campaign_event
                where campaign_turn_id = @CampaignTurnId
                """,
                new { CampaignTurnId = campaignTurnId },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static string CreateStateHash(GameState state)
    {
        var json = JsonSerializer.Serialize(state, ApiJson.SerializerOptions);
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static DateTime ToDatabaseDate(DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue);
    }

    private static string EventOrderSql(CampaignEventOrder order)
    {
        return order switch
        {
            CampaignEventOrder.LatestTurnFirst => "ct.turn_number desc, ce.event_sequence",
            _ => "ct.turn_number, ce.event_sequence"
        };
    }

    internal sealed record CommandPayload(string? TargetRegionId);
}

internal sealed class CampaignIdentity
{
    public long Id { get; init; }
    public Guid Uid { get; init; }
}

internal sealed class CampaignRow
{
    public long Id { get; init; }
    public Guid CampaignUid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TheatreId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public string PlayerSide { get; init; } = string.Empty;
    public string EnemySide { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int CurrentTurnNumber { get; init; }
    public int MaxTurns { get; init; }
    public DateOnly CurrentCampaignDate { get; init; }
    public string? Result { get; init; }
    public int? Score { get; init; }

    public CampaignSummaryResponse ToSummary()
    {
        return new CampaignSummaryResponse(
            CampaignUid,
            Name,
            TheatreId,
            ScenarioId,
            Enum.Parse<Side>(PlayerSide),
            Enum.Parse<Side>(EnemySide),
            Status,
            CurrentTurnNumber,
            MaxTurns,
            CurrentCampaignDate,
            Result,
            Score);
    }
}

internal sealed class CampaignTurnRow
{
    public long Id { get; init; }
    public Guid Uid { get; init; }
    public int TurnNumber { get; init; }
    public int RandomSeed { get; init; }
    public string Status { get; init; } = string.Empty;
}

internal sealed class SnapshotStorageRow
{
    public long Id { get; init; }
    public Guid SnapshotUid { get; init; }
    public string SnapshotType { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public bool IsLatest { get; init; }
    public string GameStateJson { get; init; } = string.Empty;

    public SnapshotRow ToSnapshotRow()
    {
        var state = JsonSerializer.Deserialize<GameState>(GameStateJson, ApiJson.SerializerOptions)
            ?? throw new InvalidOperationException("Stored snapshot does not contain a valid game state.");
        return new SnapshotRow(Id, SnapshotUid, SnapshotType, TurnNumber, IsLatest, state);
    }
}

internal sealed record SnapshotRow(
    long Id,
    Guid SnapshotUid,
    string SnapshotType,
    int TurnNumber,
    bool IsLatest,
    GameState State);

internal sealed class CommandStorageRow
{
    public int Sequence { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public string? UnitId { get; init; }
    public string? RegionId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public string CommandPayloadJson { get; init; } = "{}";

    public SubmittedCommand ToSubmittedCommand()
    {
        var payload = JsonSerializer.Deserialize<CampaignService.CommandPayload>(CommandPayloadJson, ApiJson.SerializerOptions);
        return new SubmittedCommand(
            Sequence,
            Enum.Parse<CommandSource>(Source),
            Enum.Parse<Side>(Side),
            Enum.Parse<OrderType>(CommandType),
            UnitId,
            RegionId,
            payload?.TargetRegionId);
    }
}

internal sealed class CampaignEventRow
{
    public Guid EventUid { get; init; }
    public Guid CampaignTurnUid { get; init; }
    public int TurnNumber { get; init; }
    public int Sequence { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string EventScope { get; init; } = string.Empty;
    public string? Side { get; init; }
    public string? RegionId { get; init; }
    public string? UnitId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = "{}";

    public CampaignEventResponse ToResponse()
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(PayloadJson, ApiJson.SerializerOptions)
            ?? new Dictionary<string, object?>();
        return new CampaignEventResponse(
            EventUid,
            CampaignTurnUid,
            TurnNumber,
            Sequence,
            Enum.Parse<GameEventType>(EventType),
            Enum.Parse<GameEventScope>(EventScope),
            Side is null ? null : Enum.Parse<Side>(Side),
            RegionId,
            UnitId,
            Summary,
            payload);
    }
}

internal sealed class CampaignTurnSummaryRow
{
    public Guid CampaignTurnUid { get; init; }
    public int TurnNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ResolutionMode { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public DateTime? PlayerCommandsCommittedAt { get; init; }
    public DateTime? AiCommandsPlannedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }

    public CampaignTurnSummaryResponse ToResponse()
    {
        return new CampaignTurnSummaryResponse(
            CampaignTurnUid,
            TurnNumber,
            Status,
            ResolutionMode,
            Summary,
            PlayerCommandsCommittedAt,
            AiCommandsPlannedAt,
            ResolvedAt);
    }
}
