using Microsoft.Extensions.Configuration;
using Npgsql;
using SandTable.Api;
using SandTable.Engine;
using Testcontainers.PostgreSql;

namespace SandTable.Api.Tests;

public class DockerPostgresSmokeTests
{
    [Fact]
    [Trait("Category", "DatabaseSmoke")]
    public async Task Campaign_loop_persists_through_dapper_against_docker_postgres()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sandtable")
            .WithUsername("sandtable")
            .WithPassword("sandtable")
            .Build();

        await postgres.StartAsync();

        var connectionString = postgres.GetConnectionString();
        var repoRoot = FindRepoRoot();
        await ApplySchemaAsync(connectionString, repoRoot, CancellationToken.None);

        var service = CreateCampaignService(connectionString, repoRoot);
        var cancellationToken = CancellationToken.None;
        var campaign = await service.CreateCampaignAsync(
            new CreateCampaignRequest(
                $"Docker Smoke Test Campaign {Guid.NewGuid():N}",
                "north-africa",
                "north-africa-1942",
                Side.Axis,
                RandomSeed: 1942),
            cancellationToken);
        var panzer = campaign.State.Units.Single(unit => unit.Id == "21st-panzer");
        var panzerRegion = campaign.State.Regions.Single(region => region.Id == panzer.RegionId);
        var targetRegionId = panzerRegion.AdjacentRegionIds.First(regionId =>
            !campaign.State.Units.Any(unit =>
                unit.RegionId == regionId &&
                unit.Side != Side.Axis &&
                unit.Status != UnitStatus.Destroyed));

        var campaignUid = campaign.Campaign.CampaignUid;
        var submitted = await service.SubmitCommandsAsync(
            campaignUid,
            new SubmitCommandsRequest(
            [
                new SubmitCommandRequest(
                    1,
                    new MoveCommandPayload("21st-panzer", panzer.RegionId, [targetRegionId]))
            ]),
            cancellationToken);

        var resolved = await service.ResolveTurnAsync(campaignUid, cancellationToken);
        var state = await service.GetCampaignStateAsync(campaignUid, cancellationToken);
        var events = await service.ListCampaignEventsAsync(
            campaignUid,
            turnNumber: null,
            limit: 100,
            CampaignEventOrder.Chronological,
            cancellationToken);
        var turns = await service.ListCampaignTurnsAsync(campaignUid, limit: 100, cancellationToken);
        var timeline = await service.GetCampaignTimelineAsync(campaignUid, cancellationToken);
        var resolvedTurn = await service.GetCampaignTurnAsync(campaignUid, turnNumber: 1, cancellationToken);

        Assert.NotNull(submitted);
        Assert.Equal(1, submitted.AcceptedCommandCount);
        Assert.NotNull(resolved);
        Assert.Equal(1, resolved.ResolvedTurnNumber);
        Assert.Equal(2, resolved.NextTurnNumber);
        Assert.NotNull(state);
        Assert.Equal(2, state.TurnNumber);
        Assert.NotNull(events);
        Assert.NotEmpty(events);
        Assert.Contains(events, gameEvent => gameEvent.Actor == CampaignEventActor.You);
        Assert.Contains(events, gameEvent =>
            gameEvent.Actor == CampaignEventActor.Enemy
            && gameEvent.EventType is GameEventType.Movement or GameEventType.Battle);
        Assert.NotNull(timeline);
        Assert.Equal(2, timeline.Points.Count);
        Assert.Null(timeline.Points[0].ResolvedTurnNumber);
        Assert.Equal(1, timeline.Points[1].ResolvedTurnNumber);
        Assert.True(timeline.Points[1].Sides[Side.Axis].MaximumStrength > 0);
        Assert.InRange(timeline.Points[1].Sides[Side.Axis].ForceStrengthPercent, 0m, 100m);
        Assert.True(timeline.Points[1].Sides[Side.Allies].ControlledVictoryPoints >= 0);
        Assert.NotNull(turns);
        Assert.Contains(turns, turn => turn.TurnNumber == 1 && turn.Status == "Resolved");
        Assert.NotNull(resolvedTurn);
        Assert.Equal(resolved.Summary, resolvedTurn.Summary);

        await using (var verificationConnection = new NpgsqlConnection(connectionString))
        {
            await verificationConnection.OpenAsync(cancellationToken);
            await using var verificationCommand = new NpgsqlCommand(
                """
                select
                    (select engine_version from public.campaign_snapshot where campaign_id = c.id and is_latest = true),
                    (select command_payload ->> 'commandType' from public.campaign_command where campaign_id = c.id and command_source = 'Human' limit 1),
                    (select command_payload ->> 'unitId' from public.campaign_command where campaign_id = c.id and command_source = 'Human' limit 1)
                from public.campaign c
                where c.uid = @campaignUid
                """,
                verificationConnection);
            verificationCommand.Parameters.AddWithValue("campaignUid", campaignUid);
            await using var reader = await verificationCommand.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.Equal(EngineBaseline.CurrentVersion, reader.GetString(0));
            Assert.Equal("Move", reader.GetString(1));
            Assert.Equal("21st-panzer", reader.GetString(2));
        }

        if (state.ActiveTensions.Count > 0)
        {
            var tension = state.ActiveTensions[0];
            var chosen = await service.ChooseTensionOptionAsync(
                campaignUid,
                tension.Id,
                new ChooseTensionOptionRequest(tension.Options[0].Id),
                cancellationToken);

            Assert.NotNull(chosen);
            Assert.Equal(tension.Id, chosen.Decision.CardId);
            Assert.DoesNotContain(chosen.State.ActiveTensions, card => card.Id == tension.Id);
        }
    }

    private static async Task ApplySchemaAsync(
        string connectionString,
        string repoRoot,
        CancellationToken cancellationToken)
    {
        var files = new[]
        {
            "extensions.sql",
            "user_account.sql",
            "player_profile.sql",
            "command_profile.sql",
            "campaign.sql",
            "campaign_turn.sql",
            "campaign_snapshot.sql",
            "campaign_command.sql",
            "campaign_event.sql",
            "career_record.sql"
        };

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var file in files)
        {
            var sqlPath = Path.Combine(repoRoot, "database", "public", file);
            var sql = await File.ReadAllTextAsync(sqlPath, cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static CampaignService CreateCampaignService(string connectionString, string repoRoot)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SandTableDatabase"] = connectionString
        });

        var repository = new GameContentRepository(
            new TestWebHostEnvironment { ContentRootPath = repoRoot },
            new ConfigurationManager());
        var effectApplier = new GameEffectApplier();
        var tensionChoiceResolver = new TensionChoiceResolver(effectApplier);
        var tensionGenerator = new BasicTensionGenerator();

        return new CampaignService(
            new SandTableConnectionFactory(configuration),
            new DevPlayerBootstrapper(),
            repository,
            new ScenarioFactory(),
            new BasicAiPlanner(),
            new TurnResolver(tensionGenerator),
            tensionChoiceResolver);
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SandTable.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SandTable.slnx.");
    }
}
