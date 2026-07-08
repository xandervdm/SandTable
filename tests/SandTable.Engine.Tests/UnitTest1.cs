namespace SandTable.Engine.Tests;

public class NorthAfricaScenarioTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task North_africa_content_creates_initial_state()
    {
        var content = await LoadContentAsync();
        var factory = new Engine.ScenarioFactory();

        var state = factory.CreateInitialState(content.Map, content.Scenario, content.Units);

        Assert.Equal("north-africa", state.TheatreId);
        Assert.Equal("north-africa-1942", state.ScenarioId);
        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(Engine.Side.Axis, state.PlayerSide);
        Assert.Equal("cairo", state.VictoryRegionId);
        Assert.Contains(state.Regions, region => region.Id == "alexandria" && region.Owner == Engine.Side.Allies);
        Assert.Contains(state.Units, unit => unit.Id == "15th-panzer" && unit.RegionId == "tripoli");
    }

    [Fact]
    public async Task Turn_resolution_uses_human_and_ai_commands_from_same_starting_state()
    {
        var content = await LoadContentAsync();
        var startingState = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var humanCommands = new[]
        {
            new Engine.SubmittedCommand(1, Engine.CommandSource.Human, Engine.Side.Axis, Engine.OrderType.Move, "15th-panzer", "tripoli", "benghazi")
        };
        var aiCommands = new Engine.BasicAiPlanner().Plan(startingState, Engine.Side.Allies);

        var resolution = new Engine.TurnResolver().Resolve(startingState, humanCommands, aiCommands, randomSeed: 1942);

        Assert.NotEmpty(aiCommands);
        Assert.Equal(2, resolution.NextState.TurnNumber);
        Assert.Contains(resolution.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Movement);
        Assert.Contains(resolution.NextState.Units, unit => unit.Id == "15th-panzer" && unit.RegionId == "benghazi");
        Assert.Contains(startingState.Units, unit => unit.Id == "15th-panzer" && unit.RegionId == "tripoli");
    }

    [Fact]
    public async Task Seeded_initial_state_randomizes_unit_deployment_within_content_pools()
    {
        var content = await LoadContentAsync();
        var factory = new Engine.ScenarioFactory();

        var defaultState = factory.CreateInitialState(content.Map, content.Scenario, content.Units);
        var seededState = factory.CreateInitialState(content.Map, content.Scenario, content.Units, randomSeed: 1942);
        var repeatState = factory.CreateInitialState(content.Map, content.Scenario, content.Units, randomSeed: 1942);

        Assert.Equal(
            seededState.Units.Select(unit => (unit.Id, unit.RegionId)),
            repeatState.Units.Select(unit => (unit.Id, unit.RegionId)));
        Assert.NotEqual(
            defaultState.Units.Select(unit => (unit.Id, unit.RegionId)),
            seededState.Units.Select(unit => (unit.Id, unit.RegionId)));

        var unitDefinitions = content.Units.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        foreach (var unit in seededState.Units)
        {
            var definition = unitDefinitions[unit.Id];
            var deploymentPool = definition.DeploymentRegionIds is { Count: > 0 }
                ? definition.DeploymentRegionIds
                : new[] { definition.RegionId };
            Assert.Contains(unit.RegionId, deploymentPool);
        }
    }

    [Fact]
    public async Task Ai_moves_toward_distant_enemy_when_fronts_are_separated()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var separatedState = state with
        {
            Regions = state.Regions
                .Select(region => region.Id switch
                {
                    "tripoli" => region with { Owner = Engine.Side.Axis },
                    "cairo" => region with { Owner = Engine.Side.Allies },
                    _ => region with { Owner = Engine.Side.Neutral }
                })
                .ToArray(),
            Units =
            [
                state.Units.Single(unit => unit.Id == "15th-panzer") with { RegionId = "tripoli" },
                state.Units.Single(unit => unit.Id == "desert-air-wing") with { RegionId = "cairo" }
            ]
        };

        var commands = new Engine.BasicAiPlanner().Plan(separatedState, Engine.Side.Allies);

        Assert.Contains(commands, command =>
            command.CommandType == Engine.OrderType.Move
            && command.UnitId == "desert-air-wing"
            && command.RegionId == "cairo"
            && command.TargetRegionId == "alexandria");
    }

    private static async Task<(Engine.MapDefinition Map, Engine.ScenarioDefinition Scenario, Engine.UnitCatalog Units)> LoadContentAsync()
    {
        var theatrePath = Path.Combine(FindRepoRoot(), "content", "theatres", "north-africa");
        return (
            await ReadJsonAsync<Engine.MapDefinition>(Path.Combine(theatrePath, "map.json")),
            await ReadJsonAsync<Engine.ScenarioDefinition>(Path.Combine(theatrePath, "scenario-1942.json")),
            await ReadJsonAsync<Engine.UnitCatalog>(Path.Combine(theatrePath, "units.json")));
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        foreach (var candidate in new[] { sourceDirectory, Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SandTable.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate SandTable.slnx.");
    }
}
