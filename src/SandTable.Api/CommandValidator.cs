using SandTable.Engine;

namespace SandTable.Api;

public static class CommandValidator
{
    public static void ValidateSubmitCommands(
        GameState state,
        Side playerSide,
        SubmitCommandsRequest? request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (request?.Commands is null)
        {
            AddError(errors, "commands", "Commands are required.");
            ThrowIfInvalid(errors);
            return;
        }

        if (request.Commands.Count == 0)
        {
            AddError(errors, "commands", "At least one command is required.");
            ThrowIfInvalid(errors);
            return;
        }

        var units = state.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var regions = state.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var commandedUnitIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < request.Commands.Count; index++)
        {
            var command = request.Commands[index];
            var prefix = $"commands[{index}]";
            if (command is null)
            {
                AddError(errors, prefix, "Command is required.");
                continue;
            }

            if (!Enum.IsDefined(command.CommandType))
            {
                AddError(errors, $"{prefix}.commandType", $"Command type '{command.CommandType}' is not supported.");
            }

            UnitState? unit = null;
            if (string.IsNullOrWhiteSpace(command.UnitId))
            {
                AddError(errors, $"{prefix}.unitId", "Unit is required.");
            }
            else if (!units.TryGetValue(command.UnitId, out unit))
            {
                AddError(errors, $"{prefix}.unitId", $"Unit '{command.UnitId}' does not exist in the latest campaign state.");
            }
            else
            {
                if (!commandedUnitIds.Add(unit.Id))
                {
                    AddError(errors, $"{prefix}.unitId", $"Unit '{unit.Id}' already has a command in this submission.");
                }

                if (unit.Side != playerSide)
                {
                    AddError(errors, $"{prefix}.unitId", $"Unit '{unit.Id}' does not belong to side '{playerSide}'.");
                }

                if (unit.Status == UnitStatus.Destroyed || unit.Strength <= 0)
                {
                    AddError(errors, $"{prefix}.unitId", $"Unit '{unit.Id}' has been destroyed.");
                }
            }

            if (!string.IsNullOrWhiteSpace(command.RegionId))
            {
                if (!regions.ContainsKey(command.RegionId))
                {
                    AddError(errors, $"{prefix}.regionId", $"Region '{command.RegionId}' does not exist in the latest campaign state.");
                }
                else if (unit is not null && !string.Equals(command.RegionId, unit.RegionId, StringComparison.Ordinal))
                {
                    AddError(errors, $"{prefix}.regionId", $"Region '{command.RegionId}' is not the current region for unit '{unit.Id}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(command.TargetRegionId)
                && !regions.ContainsKey(command.TargetRegionId))
            {
                AddError(errors, $"{prefix}.targetRegionId", $"Target region '{command.TargetRegionId}' does not exist in the latest campaign state.");
            }

            ValidateCommandTypeRules(errors, prefix, command, unit, state, regions);
        }

        ThrowIfInvalid(errors);
    }

    private static void ValidateCommandTypeRules(
        Dictionary<string, List<string>> errors,
        string prefix,
        SubmitCommandRequest command,
        UnitState? unit,
        GameState state,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        switch (command.CommandType)
        {
            case OrderType.Move:
                ValidateMoveOrAttackTarget(errors, prefix, command, unit, regions);
                ValidateMoveDestination(errors, prefix, command, unit, state);
                break;

            case OrderType.Attack:
                ValidateMoveOrAttackTarget(errors, prefix, command, unit, regions);
                break;

            case OrderType.Recon:
            case OrderType.Support:
                ValidateOptionalNearbyTarget(errors, prefix, command, unit, regions);
                break;

            case OrderType.HoldPosition:
            case OrderType.Resupply:
                ValidateNoTarget(errors, prefix, command);
                break;
        }
    }

    private static void ValidateMoveOrAttackTarget(
        Dictionary<string, List<string>> errors,
        string prefix,
        SubmitCommandRequest command,
        UnitState? unit,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        if (string.IsNullOrWhiteSpace(command.TargetRegionId))
        {
            AddError(errors, $"{prefix}.targetRegionId", $"{command.CommandType} commands require a target region.");
            return;
        }

        if (unit is null
            || !regions.TryGetValue(unit.RegionId, out var currentRegion)
            || !regions.TryGetValue(command.TargetRegionId, out var targetRegion))
        {
            return;
        }

        if (!currentRegion.AdjacentRegionIds.Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            AddError(
                errors,
                $"{prefix}.targetRegionId",
                $"Target region '{targetRegion.Id}' is not adjacent to unit '{unit.Id}' in '{currentRegion.Id}'.");
        }
    }

    private static void ValidateMoveDestination(
        Dictionary<string, List<string>> errors,
        string prefix,
        SubmitCommandRequest command,
        UnitState? unit,
        GameState state)
    {
        if (unit is null || string.IsNullOrWhiteSpace(command.TargetRegionId))
        {
            return;
        }

        var occupiedByEnemy = state.Units.Any(other =>
            other.Side != unit.Side
            && other.Side != Side.Neutral
            && other.Status != UnitStatus.Destroyed
            && string.Equals(other.RegionId, command.TargetRegionId, StringComparison.Ordinal));

        if (occupiedByEnemy)
        {
            AddError(
                errors,
                $"{prefix}.targetRegionId",
                $"Target region '{command.TargetRegionId}' is occupied by enemy forces. Use Attack instead.");
        }
    }

    private static void ValidateOptionalNearbyTarget(
        Dictionary<string, List<string>> errors,
        string prefix,
        SubmitCommandRequest command,
        UnitState? unit,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        if (string.IsNullOrWhiteSpace(command.TargetRegionId)
            || unit is null
            || !regions.TryGetValue(unit.RegionId, out var currentRegion)
            || !regions.TryGetValue(command.TargetRegionId, out var targetRegion))
        {
            return;
        }

        if (!string.Equals(targetRegion.Id, currentRegion.Id, StringComparison.Ordinal)
            && !currentRegion.AdjacentRegionIds.Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            AddError(
                errors,
                $"{prefix}.targetRegionId",
                $"{command.CommandType} target region '{targetRegion.Id}' must be the unit's current region or adjacent to '{currentRegion.Id}'.");
        }
    }

    private static void ValidateNoTarget(
        Dictionary<string, List<string>> errors,
        string prefix,
        SubmitCommandRequest command)
    {
        if (string.IsNullOrWhiteSpace(command.TargetRegionId))
        {
            return;
        }

        AddError(
            errors,
            $"{prefix}.targetRegionId",
            $"{command.CommandType} commands do not accept a target region.");
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static void ThrowIfInvalid(Dictionary<string, List<string>> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        throw new ApiValidationException(
            "Invalid command submission",
            "One or more commands are invalid.",
            errors.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));
    }
}
