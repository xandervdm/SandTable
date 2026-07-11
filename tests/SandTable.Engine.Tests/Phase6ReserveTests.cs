using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandTable.Engine.Tests;

public class Phase6ReserveTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Available_reserve_deploys_authored_unit_and_spends_full_cost()
    {
        var content = await LoadContentAsync();
        var state = CreateState(content) with
        {
            TurnNumber = 2,
            Reserves = CreateState(content).Reserves.Select(reserve => reserve.ReserveId == "90th-light-reserve"
                ? reserve with { Status = Engine.ReserveStatus.Available }
                : reserve).ToArray()
        };
        var command = new Engine.SubmittedCommand(
            1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.DeployCommandPayload("90th-light-reserve", "tripoli"));

        var result = new Engine.TurnResolver().Resolve(
            state, [command], [], 1942, null, content.Reserves, content.Units, content.Events);

        var resolved = Assert.Single(result.Commands);
        Assert.True(resolved.Accepted);
        Assert.Equal(new Engine.Resources(4, 5, 2, 0, 1), resolved.Cost);
        Assert.Contains(result.NextState.Units, unit => unit.Id == "90th-light-reserve" && unit.RegionId == "tripoli");
        var reserve = Assert.Single(result.NextState.Reserves, item => item.ReserveId == "90th-light-reserve");
        Assert.Equal(Engine.ReserveStatus.Deployed, reserve.Status);
        Assert.Equal(2, reserve.DeploymentTurn);
        Assert.Equal("90th-light-reserve", reserve.DeployedUnitId);
        Assert.Contains(result.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Deployment);
        Assert.Equal(1196, result.NextState.Resources[Engine.Side.Axis].Supplies);
        Assert.Equal(845, result.NextState.Resources[Engine.Side.Axis].Manpower);
        Assert.Equal(428, result.NextState.Resources[Engine.Side.Axis].Fuel);
    }

    [Fact]
    public async Task Deployment_rejects_uncontrolled_or_ineligible_positions()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var state = original with
        {
            TurnNumber = 2,
            Reserves = original.Reserves.Select(reserve => reserve.ReserveId == "90th-light-reserve"
                ? reserve with { Status = Engine.ReserveStatus.Available }
                : reserve).ToArray()
        };
        var command = new Engine.SubmittedCommand(
            1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.DeployCommandPayload("90th-light-reserve", "alexandria"));

        var result = new Engine.TurnResolver().Resolve(
            state, [command], [], 1942, null, content.Reserves, content.Units, content.Events);

        Assert.False(Assert.Single(result.Commands).Accepted);
        Assert.DoesNotContain(result.NextState.Units, unit => unit.Id == "90th-light-reserve");
    }

    [Fact]
    public async Task Scheduled_event_is_applied_once_and_recorded_before_resolution()
    {
        var content = await LoadContentAsync();
        var state = CreateState(content) with { TurnNumber = 2 };

        var first = new Engine.TurnResolver().Resolve(
            state, [], [], 1942, null, content.Reserves, content.Units, content.Events);

        Assert.Contains("axis-reserve-released", first.NextState.ScenarioEventHistory);
        var scenarioEvent = Assert.Single(first.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Scenario);
        Assert.Equal("axis-reserve-released", scenarioEvent.Payload["scenarioEventId"]);

        var repeatedTurn = first.NextState with { TurnNumber = 2 };
        var second = new Engine.TurnResolver().Resolve(
            repeatedTurn, [], [], 1942, null, content.Reserves, content.Units, content.Events);
        Assert.DoesNotContain(second.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Scenario);
    }

    [Fact]
    public async Task Ai_uses_the_same_legal_reserve_and_budget_rules()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var state = original with
        {
            TurnNumber = 2,
            Reserves = original.Reserves.Select(reserve => reserve.ReserveId == "90th-light-reserve"
                ? reserve with { Status = Engine.ReserveStatus.Available }
                : reserve).ToArray()
        };

        var commands = new Engine.BasicAiPlanner().Plan(state, Engine.Side.Axis, content.Reserves, content.Units);
        var deployment = Assert.Single(commands, command => command.CommandType == Engine.OrderType.Deploy);
        Assert.Equal("90th-light-reserve", ((Engine.DeployCommandPayload)deployment.Payload).ReserveId);
        Assert.Contains(deployment.TargetRegionId, new[] { "tripoli", "benghazi" });
    }

    private static Engine.GameState CreateState(Content content) => new Engine.ScenarioFactory().CreateInitialState(
        content.Map, content.Scenario, content.Units, content.Reserves, Engine.Side.Axis);

    private static async Task<Content> LoadContentAsync()
    {
        var root = FindRepoRoot();
        var theatre = Path.Combine(root, "content", "theatres", "north-africa");
        return new Content(
            await ReadAsync<Engine.MapDefinition>(Path.Combine(theatre, "map.json")),
            await ReadAsync<Engine.ScenarioDefinition>(Path.Combine(theatre, "scenarios", "north-africa-1942.json")),
            await ReadAsync<Engine.UnitCatalog>(Path.Combine(theatre, "units.json")),
            await ReadAsync<Engine.ReserveCatalog>(Path.Combine(theatre, "reserves.json")),
            await ReadAsync<Engine.ScenarioEventCatalog>(Path.Combine(theatre, "events.json")));
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(source)!);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SandTable.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record Content(
        Engine.MapDefinition Map,
        Engine.ScenarioDefinition Scenario,
        Engine.UnitCatalog Units,
        Engine.ReserveCatalog Reserves,
        Engine.ScenarioEventCatalog Events);
}
