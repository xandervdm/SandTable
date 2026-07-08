using Microsoft.Extensions.Configuration;
using SandTable.Api;
using SandTable.Engine;

namespace SandTable.Api.Tests;

public class DevDatabaseSmokeTests
{
    [Fact]
    [Trait("Category", "DatabaseSmoke")]
    public async Task Campaign_loop_persists_through_dapper_when_dev_database_is_configured()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VULTR_POSTGRES_URL_SAND_TABLE_DEV")))
        {
            return;
        }

        var service = CreateCampaignService();
        var cancellationToken = CancellationToken.None;
        var campaign = await service.CreateCampaignAsync(
            new CreateCampaignRequest(
                $"Smoke Test Campaign {Guid.NewGuid():N}",
                "north-africa-1942",
                Side.Axis,
                RandomSeed: 1942),
            cancellationToken);

        var campaignUid = campaign.Campaign.CampaignUid;
        var submitted = await service.SubmitCommandsAsync(
            campaignUid,
            new SubmitCommandsRequest(
            [
                new SubmitCommandRequest(
                    OrderType.Move,
                    "21st-panzer",
                    RegionId: null,
                    TargetRegionId: "gazala")
            ]),
            cancellationToken);

        var resolved = await service.ResolveTurnAsync(campaignUid, cancellationToken);
        var state = await service.GetCampaignStateAsync(campaignUid, cancellationToken);
        var events = await service.ListCampaignEventsAsync(
            campaignUid,
            turnNumber: null,
            limit: 100,
            cancellationToken);

        Assert.NotNull(submitted);
        Assert.Equal(1, submitted.AcceptedCommandCount);
        Assert.NotNull(resolved);
        Assert.Equal(1, resolved.ResolvedTurnNumber);
        Assert.Equal(2, resolved.NextTurnNumber);
        Assert.NotNull(state);
        Assert.Equal(2, state.TurnNumber);
        Assert.NotNull(events);
        Assert.NotEmpty(events);

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

    private static CampaignService CreateCampaignService()
    {
        var repository = new GameContentRepository(
            new TestWebHostEnvironment { ContentRootPath = FindRepoRoot() },
            new ConfigurationManager());
        var effectApplier = new GameEffectApplier();
        var tensionChoiceResolver = new TensionChoiceResolver(effectApplier);
        var tensionGenerator = new BasicTensionGenerator();

        return new CampaignService(
            new SandTableConnectionFactory(new ConfigurationManager()),
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
