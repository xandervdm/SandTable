using SandTable.Api;
using SandTable.Engine;

namespace SandTable.Api.Tests;

public class CommandValidatorTests
{
    [Fact]
    public void ValidateSubmitCommands_rejects_empty_submission()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(Array.Empty<SubmitCommandRequest>());

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Equal("Invalid command submission", exception.Title);
        Assert.Contains("commands", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_unknown_unit_and_non_adjacent_target()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new MoveCommandPayload("missing-unit", "base", ["frontline"])),
            new SubmitCommandRequest(2, new AttackCommandPayload("axis-armour", "base", ["distant"]))
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].command.unitId", exception.Errors.Keys);
        Assert.Contains("commands[1].command.pathRegionIds[0]", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_accepts_adjacent_player_unit_move()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new MoveCommandPayload("axis-armour", "base", ["open-desert"]))
        ]);

        CommandValidator.ValidateSubmitCommands(state, Side.Axis, request);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_move_into_enemy_occupied_region()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new MoveCommandPayload("axis-armour", "base", ["frontline"]))
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].command.pathRegionIds", exception.Errors.Keys);
        Assert.Contains("Use Attack instead.", exception.Errors["commands[0].command.pathRegionIds"][0]);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_remote_recon_target()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new ReconCommandPayload("axis-armour", "base", "distant"))
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].command.targetRegionId", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_resupply_target_region()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new ResupplyCommandPayload("axis-armour", "frontline"))
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].command.regionId", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_accepts_adjacent_attack_and_recon()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new AttackCommandPayload("axis-armour", "base", ["frontline"])),
            new SubmitCommandRequest(2, new ReconCommandPayload("axis-logistics", "base", "frontline"))
        ]);

        CommandValidator.ValidateSubmitCommands(state, Side.Axis, request);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_later_command_when_ordered_budget_is_exhausted()
    {
        var state = CreateState();
        state = state with
        {
            Resources = state.Resources.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == Side.Axis ? pair.Value with { CommandPoints = 1 } : pair.Value)
        };
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(1, new AttackCommandPayload("axis-armour", "base", ["frontline"])),
            new SubmitCommandRequest(2, new ReconCommandPayload("axis-logistics", "base", "frontline"))
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[1].command", exception.Errors.Keys);
        Assert.Contains("command points", exception.Errors["commands[1].command"][0]);
    }

    private static GameState CreateState()
    {
        return new GameState(
            "test-theatre",
            "test-scenario",
            "Test Scenario",
            1,
            5,
            new DateOnly(1942, 6, 12),
            Side.Axis,
            Side.Allies,
            new Dictionary<Side, Resources>
            {
                [Side.Axis] = new(10, 10, 10, 10, 3),
                [Side.Allies] = new(10, 10, 10, 10, 3)
            },
            [
                new RegionState("base", "Base", RegionKind.EntryPoint, "Desert", Side.Axis, 1, 1, [], ["frontline", "open-desert"]),
                new RegionState("frontline", "Frontline", RegionKind.Objective, "Desert", Side.Allies, 1, 1, [], ["base"]),
                new RegionState("open-desert", "Open Desert", RegionKind.OperationalPosition, "Desert", Side.Neutral, 0, 0, [], ["base"]),
                new RegionState("distant", "Distant", RegionKind.PrimaryObjective, "Desert", Side.Allies, 1, 1, [], [])
            ],
            [
                new RouteState("base-frontline", "base", "frontline", "Road", 1, 0),
                new RouteState("base-open", "base", "open-desert", "Track", 1, 1)
            ],
            [
                new UnitState("axis-armour", "Axis Armour", Side.Axis, UnitType.Armour, "base", 10, 10, 3, 5, 4, 8, 8, 5, UnitStatus.Ready),
                new UnitState("axis-logistics", "Axis Logistics", Side.Axis, UnitType.Logistics, "base", 6, 6, 2, 1, 3, 10, 6, 5, UnitStatus.Ready),
                new UnitState("allied-armour", "Allied Armour", Side.Allies, UnitType.Armour, "frontline", 10, 10, 3, 5, 4, 8, 8, 5, UnitStatus.Ready)
            ],
            [],
            Enum.GetValues<OrderType>().ToDictionary(
                type => type,
                _ => new CommandCostDefinition(1, 0, 0, 0, 0)),
            1,
            new VictoryRulesDefinition([]),
            new Dictionary<string, int>(),
            [],
            false,
            null);
    }
}
