using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandTable.Engine.Tests;

public class Phase7OperationalDepthTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task North_africa_graph_exposes_coastal_and_desert_operational_axes()
    {
        var content = await LoadContentAsync();

        Assert.True(content.Map.Regions.Count >= 18);
        Assert.Contains(content.Map.Regions, region => region.Id == "mersamatruh" && region.Kind == Engine.RegionKind.Objective);
        Assert.Contains(content.Map.Regions, region => region.Id == "bir-hakeim" && region.Kind == Engine.RegionKind.OperationalPosition);
        Assert.Contains(content.Map.Routes, route => route.FromRegionId == "tobruk" && route.ToRegionId == "mersamatruh");
        Assert.Contains(content.Map.Routes, route => route.FromRegionId == "bir-hakeim" && route.ToRegionId == "siwa-oasis");
    }

    [Fact]
    public async Task Multi_node_move_spends_weighted_allowance_and_captures_the_route()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var unit = original.Units.Single(candidate => candidate.Id == "15th-panzer") with { RegionId = "tripoli" };
        var state = original with
        {
            Units = [unit],
            Regions = original.Regions.Select(region => region.Id is "sirte" or "ajdabiya" or "benghazi"
                ? region with { Owner = Engine.Side.Neutral }
                : region).ToArray()
        };
        var command = new Engine.SubmittedCommand(
            1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.MoveCommandPayload(unit.Id, "tripoli", ["sirte", "ajdabiya", "benghazi"]));

        var result = new Engine.TurnResolver().Resolve(state, [command], [], 1942);

        Assert.True(Assert.Single(result.Commands).Accepted);
        Assert.Equal("benghazi", result.NextState.Units.Single().RegionId);
        Assert.All(result.NextState.Regions.Where(region => region.Id is "sirte" or "ajdabiya" or "benghazi"),
            region => Assert.Equal(Engine.Side.Axis, region.Owner));
        var movement = Assert.Single(result.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Movement);
        Assert.Equal(3, movement.Payload["movementCost"]);
    }

    [Fact]
    public async Task Direct_engine_move_stops_before_first_enemy_contact()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var mover = original.Units.Single(candidate => candidate.Id == "15th-panzer") with { RegionId = "gazala" };
        var enemy = original.Units.Single(candidate => candidate.Id == "tobruk-garrison") with { RegionId = "mersamatruh" };
        var state = original with { Units = [mover, enemy] };
        var command = new Engine.SubmittedCommand(
            1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.MoveCommandPayload(mover.Id, "gazala", ["bir-hakeim", "mersamatruh"]));

        var result = new Engine.TurnResolver().Resolve(state, [command], [], 1942);

        Assert.Equal("bir-hakeim", result.NextState.Units.Single(unit => unit.Id == mover.Id).RegionId);
        var movement = Assert.Single(result.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Movement);
        Assert.Equal("mersamatruh", movement.Payload["contactRegionId"]);
    }

    [Fact]
    public async Task Multiple_attackers_and_defenders_resolve_as_one_battle_with_retreat()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var first = original.Units.Single(unit => unit.Id == "15th-panzer") with
        {
            RegionId = "gazala", Strength = 10, Attack = 9
        };
        var second = original.Units.Single(unit => unit.Id == "21st-panzer") with
        {
            RegionId = "ajdabiya", Strength = 10, Attack = 9
        };
        var defenderOne = original.Units.Single(unit => unit.Id == "tobruk-garrison") with
        {
            RegionId = "bir-hakeim", Strength = 6, Defence = 3, Morale = 5
        };
        var defenderTwo = original.Units.Single(unit => unit.Id == "7th-armoured") with
        {
            RegionId = "bir-hakeim", Strength = 6, Defence = 3, Morale = 5
        };
        var state = original with
        {
            Units = [first, second, defenderOne, defenderTwo],
            Resources = original.Resources.ToDictionary(pair => pair.Key, pair => pair.Value with { CommandPoints = 4 }),
            Regions = original.Regions.Select(region => region.Id == "bir-hakeim"
                ? region with { Owner = Engine.Side.Allies }
                : region).ToArray()
        };
        var commands = new Engine.SubmittedCommand[]
        {
            new(1, Engine.CommandSource.Human, Engine.Side.Axis,
                new Engine.AttackCommandPayload(first.Id, "gazala", ["bir-hakeim"])),
            new(2, Engine.CommandSource.Human, Engine.Side.Axis,
                new Engine.AttackCommandPayload(second.Id, "ajdabiya", ["bir-hakeim"]))
        };

        var result = new Engine.TurnResolver().Resolve(state, commands, [], 7);

        var battle = Assert.Single(result.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Battle);
        Assert.Equal(2, Assert.IsType<string[]>(battle.Payload["attackerUnitIds"]).Length);
        Assert.NotNull(battle.Payload["retreatRegionId"]);
        Assert.Equal(Engine.Side.Axis, result.NextState.Regions.Single(region => region.Id == "bir-hakeim").Owner);
        Assert.All(result.NextState.Units.Where(unit => unit.Side == Engine.Side.Allies && unit.Status != Engine.UnitStatus.Destroyed),
            unit => Assert.Equal(Engine.UnitStatus.Disrupted, unit.Status));
    }

    [Fact]
    public async Task Scored_ai_withdraws_damaged_frontline_unit_to_supplied_port()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var damaged = original.Units.Single(unit => unit.Id == "21st-panzer") with
        {
            RegionId = "gazala", Strength = 3, MaxStrength = 10, Morale = 2, Status = Engine.UnitStatus.Disrupted
        };
        var state = original with { Units = [damaged] };

        var commands = new Engine.BasicAiPlanner().Plan(state, Engine.Side.Axis, content.Reserves, content.Units);

        var withdrawal = Assert.Single(commands);
        Assert.Equal(Engine.OrderType.Move, withdrawal.CommandType);
        Assert.Equal("benghazi", withdrawal.TargetRegionId);
    }

    [Fact]
    public async Task Scored_ai_coordinates_multiple_formations_on_one_objective()
    {
        var content = await LoadContentAsync();
        var original = CreateState(content);
        var alliedArmour = original.Units.Single(unit => unit.Id == "7th-armoured") with { RegionId = "mersamatruh" };
        var alliedAir = original.Units.Single(unit => unit.Id == "desert-air-wing") with { RegionId = "bir-hakeim" };
        var axisDefender = original.Units.Single(unit => unit.Id == "15th-panzer") with
        {
            RegionId = "gazala", Strength = 2, Defence = 1
        };
        var state = original with { Units = [alliedArmour, alliedAir, axisDefender] };

        var commands = new Engine.BasicAiPlanner().Plan(state, Engine.Side.Allies, content.Reserves, content.Units);
        var attacks = commands.Where(command => command.CommandType == Engine.OrderType.Attack).ToArray();

        Assert.Equal(2, attacks.Length);
        Assert.All(attacks, command => Assert.Equal("gazala", command.TargetRegionId));
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
            await ReadAsync<Engine.ReserveCatalog>(Path.Combine(theatre, "reserves.json")));
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
        Engine.ReserveCatalog Reserves);
}
