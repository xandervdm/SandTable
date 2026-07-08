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
            new SubmitCommandRequest(OrderType.Move, "missing-unit", null, "frontline"),
            new SubmitCommandRequest(OrderType.Attack, "axis-armour", "base", "distant")
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].unitId", exception.Errors.Keys);
        Assert.Contains("commands[1].targetRegionId", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_accepts_adjacent_player_unit_move()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(OrderType.Move, "axis-armour", null, "open-desert")
        ]);

        CommandValidator.ValidateSubmitCommands(state, Side.Axis, request);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_move_into_enemy_occupied_region()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(OrderType.Move, "axis-armour", null, "frontline")
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].targetRegionId", exception.Errors.Keys);
        Assert.Contains("Use Attack instead.", exception.Errors["commands[0].targetRegionId"][0]);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_remote_recon_target()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(OrderType.Recon, "axis-armour", null, "distant")
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].targetRegionId", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_rejects_resupply_target_region()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(OrderType.Resupply, "axis-armour", null, "frontline")
        ]);

        var exception = Assert.Throws<ApiValidationException>(() =>
            CommandValidator.ValidateSubmitCommands(state, Side.Axis, request));

        Assert.Contains("commands[0].targetRegionId", exception.Errors.Keys);
    }

    [Fact]
    public void ValidateSubmitCommands_accepts_adjacent_attack_and_recon()
    {
        var state = CreateState();
        var request = new SubmitCommandsRequest(
        [
            new SubmitCommandRequest(OrderType.Attack, "axis-armour", null, "frontline"),
            new SubmitCommandRequest(OrderType.Recon, "axis-logistics", null, "frontline")
        ]);

        CommandValidator.ValidateSubmitCommands(state, Side.Axis, request);
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
            new Resources(10, 10, 10, 10, 3),
            [
                new RegionState("base", "Base", "Desert", Side.Axis, 1, 1, [], ["frontline", "open-desert"]),
                new RegionState("frontline", "Frontline", "Desert", Side.Allies, 1, 1, [], ["base"]),
                new RegionState("open-desert", "Open Desert", "Desert", Side.Neutral, 0, 0, [], ["base"]),
                new RegionState("distant", "Distant", "Desert", Side.Allies, 1, 1, [], [])
            ],
            [
                new UnitState("axis-armour", "Axis Armour", Side.Axis, UnitType.Armour, "base", 10, 10, 3, 5, 4, 8, 8, 5, UnitStatus.Ready),
                new UnitState("axis-logistics", "Axis Logistics", Side.Axis, UnitType.Logistics, "base", 6, 6, 2, 1, 3, 10, 6, 5, UnitStatus.Ready),
                new UnitState("allied-armour", "Allied Armour", Side.Allies, UnitType.Armour, "frontline", 10, 10, 3, 5, 4, 8, 8, 5, UnitStatus.Ready)
            ],
            false,
            null,
            "frontline");
    }
}
