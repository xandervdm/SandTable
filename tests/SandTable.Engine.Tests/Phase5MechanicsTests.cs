using System.Text.Json;
using System.Text.Json.Serialization;
using Engine = SandTable.Engine;

namespace SandTable.Engine.Tests;

public class Phase5MechanicsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Commands_spend_route_costs_and_later_unaffordable_orders_are_rejected()
    {
        var state = await CreateStateAsync();
        state = state with
        {
            Resources = state.Resources.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == Engine.Side.Axis ? pair.Value with { CommandPoints = 1 } : pair.Value)
        };
        var axisUnits = state.Units.Where(unit => unit.Side == Engine.Side.Axis).Take(2).ToArray();
        var commands = axisUnits.Select((unit, index) => new Engine.SubmittedCommand(
            index + 1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.MoveCommandPayload(
                unit.Id,
                unit.RegionId,
                [state.Regions.Single(region => region.Id == unit.RegionId).AdjacentRegionIds.First()])))
            .ToArray();

        var resolution = new Engine.TurnResolver().Resolve(state, commands, [], 1942);

        Assert.True(resolution.Commands[0].Accepted);
        Assert.Equal(1, resolution.Commands[0].Cost.CommandPoints);
        var expectedSupplies = Engine.CommandEconomy.CalculatePathMovementCost(
            state.Routes,
            commands[0].RegionId!,
            ((Engine.MoveCommandPayload)commands[0].Payload).PathRegionIds);
        Assert.Equal(expectedSupplies, resolution.Commands[0].Cost.Supplies);
        Assert.False(resolution.Commands[1].Accepted);
        Assert.Contains("command points", resolution.Commands[1].RejectionReason);
        Assert.Equal(state.Resources[Engine.Side.Axis].Supplies - expectedSupplies, resolution.NextState.Resources[Engine.Side.Axis].Supplies);
        Assert.Equal(1, resolution.NextState.Resources[Engine.Side.Axis].CommandPoints);
    }

    [Fact]
    public async Task Supply_trace_uses_controlled_weighted_routes_and_breaks_when_an_intermediate_region_is_lost()
    {
        var state = await CreateStateAsync();
        Assert.True(Engine.SupplyTracer.Trace(state, Engine.Side.Axis, "gazala").IsConnected);

        var cutState = state with
        {
            Regions = state.Regions.Select(region => region.Id == "benghazi"
                ? region with { Owner = Engine.Side.Allies }
                : region).ToArray()
        };

        Assert.False(Engine.SupplyTracer.Trace(cutState, Engine.Side.Axis, "gazala").IsConnected);
    }

    [Fact]
    public async Task Repeated_out_of_supply_turns_disrupt_and_attrit_a_unit_with_a_persisted_event()
    {
        var state = await CreateStateAsync();
        var unit = state.Units.First(unit => unit.Side == Engine.Side.Axis) with
        {
            RegionId = "fezzan-desert",
            SupplyStatus = Engine.UnitSupplyStatus.OutOfSupply,
            OutOfSupplyTurns = 1,
            Supply = 4,
            Strength = 8
        };
        var blocker = state.Units.First(candidate => candidate.Side == Engine.Side.Allies) with
        {
            RegionId = "fezzan-desert"
        };
        state = state with { Units = [unit, blocker] };

        var resolution = new Engine.TurnResolver().Resolve(state, [], [], 7);
        var next = resolution.NextState.Units.Single(candidate => candidate.Id == unit.Id);

        Assert.Equal(2, next.OutOfSupplyTurns);
        Assert.Equal(7, next.Strength);
        Assert.Equal(Engine.UnitStatus.Disrupted, next.Status);
        Assert.Contains(resolution.Events, gameEvent =>
            gameEvent.EventType == Engine.GameEventType.Supply
            && Equals(gameEvent.Payload["strengthLoss"], 1));
    }

    [Fact]
    public async Task Recon_and_support_create_deterministic_attack_bonuses()
    {
        var state = await CreateStateAsync();
        var attacker = state.Units.Single(unit => unit.Id == "15th-panzer") with { RegionId = "gazala" };
        var supporter = state.Units.Single(unit => unit.Id == "21st-panzer") with { RegionId = "gazala" };
        var defender = state.Units.Single(unit => unit.Id == "tobruk-garrison") with { RegionId = "tobruk" };
        state = state with
        {
            Units = [attacker, supporter, defender],
            Resources = state.Resources.ToDictionary(pair => pair.Key, pair => pair.Value with { CommandPoints = 4 })
        };
        var commands = new Engine.SubmittedCommand[]
        {
            new(1, Engine.CommandSource.Human, Engine.Side.Axis, new Engine.ReconCommandPayload(attacker.Id, "gazala", "tobruk")),
            new(2, Engine.CommandSource.Human, Engine.Side.Axis, new Engine.SupportCommandPayload(supporter.Id, "gazala", "tobruk")),
            new(3, Engine.CommandSource.Human, Engine.Side.Axis, new Engine.AttackCommandPayload(attacker.Id, "gazala", ["tobruk"]))
        };

        var resolution = new Engine.TurnResolver().Resolve(state, commands, [], 11);
        var battle = Assert.Single(resolution.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Battle);

        Assert.Equal(2, battle.Payload["attackerSupport"]);
        Assert.Equal(1, battle.Payload["reconBonus"]);
        Assert.Contains(resolution.Events, gameEvent => gameEvent.EventType == Engine.GameEventType.Recon);
    }

    [Fact]
    public async Task Campaign_modifiers_change_command_capacity_and_fuel_costs()
    {
        var state = await CreateStateAsync();
        state = state with
        {
            CampaignModifiers =
            [
                new Engine.CampaignModifier("phase5", "Phase 5", 2, new Dictionary<string, int>
                {
                    ["commandPoints"] = 2,
                    ["fuelReserve"] = 2
                })
            ]
        };
        var unit = state.Units.Single(unit => unit.Id == "15th-panzer");
        var command = new Engine.SubmittedCommand(
            1,
            Engine.CommandSource.Human,
            Engine.Side.Axis,
            new Engine.AttackCommandPayload(unit.Id, unit.RegionId, ["benghazi"]));

        Assert.Equal(state.Resources[Engine.Side.Axis].CommandPoints + 2, Engine.CommandEconomy.CreateTurnBudget(state, Engine.Side.Axis).CommandPoints);
        Assert.Equal(0, Engine.CommandEconomy.CalculateCost(state, command).Fuel);
    }

    [Fact]
    public async Task Operational_victory_requires_two_consecutive_qualified_turns()
    {
        var state = await CreateStateAsync();
        state = state with
        {
            Regions = state.Regions.Select(region => region with { Owner = Engine.Side.Axis }).ToArray(),
            Units = state.Units.Where(unit => unit.Side == Engine.Side.Axis).ToArray()
        };

        var first = new Engine.TurnResolver().Resolve(state, [], [], 1);
        Assert.False(first.NextState.IsComplete);
        Assert.Equal(1, first.NextState.VictoryProgress["player-operational-victory:0"]);

        var second = new Engine.TurnResolver().Resolve(first.NextState, [], [], 2);
        Assert.True(second.NextState.IsComplete);
        Assert.Equal(Engine.VictoryResult.Victory, second.NextState.Result);
        Assert.Equal(2, second.NextState.VictoryProgress["player-operational-victory:0"]);
    }

    private static async Task<Engine.GameState> CreateStateAsync()
    {
        var root = FindRepoRoot();
        var theatre = Path.Combine(root, "content", "theatres", "north-africa");
        var map = await ReadAsync<Engine.MapDefinition>(Path.Combine(theatre, "map.json"));
        var scenario = await ReadAsync<Engine.ScenarioDefinition>(Path.Combine(theatre, "scenarios", "north-africa-1942.json"));
        var units = await ReadAsync<Engine.UnitCatalog>(Path.Combine(theatre, "units.json"));
        var reserves = await ReadAsync<Engine.ReserveCatalog>(Path.Combine(theatre, "reserves.json"));
        return new Engine.ScenarioFactory().CreateInitialState(map, scenario, units, reserves, Engine.Side.Axis);
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize {path}.");
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SandTable.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate SandTable.slnx.");
    }
}
