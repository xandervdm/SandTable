using SandTable.Engine;

namespace SandTable.Api;

public static class CommandValidator
{
    public static void ValidateSubmitCommands(
        GameState state,
        Side playerSide,
        SubmitCommandsRequest? request,
        ReserveCatalog? reserveCatalog = null,
        UnitCatalog? unitCatalog = null)
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
        var sequences = new HashSet<int>();
        var commandedUnitIds = new HashSet<string>(StringComparer.Ordinal);
        var commandedReserveIds = new HashSet<string>(StringComparer.Ordinal);
        var deploymentCount = 0;

        for (var index = 0; index < request.Commands.Count; index++)
        {
            var requestCommand = request.Commands[index];
            var prefix = $"commands[{index}]";
            if (requestCommand is null)
            {
                AddError(errors, prefix, "Command is required.");
                continue;
            }
            if (requestCommand.Sequence <= 0)
            {
                AddError(errors, $"{prefix}.sequence", "Sequence must be positive.");
            }
            else if (!sequences.Add(requestCommand.Sequence))
            {
                AddError(errors, $"{prefix}.sequence", $"Sequence '{requestCommand.Sequence}' is duplicated.");
            }
            if (requestCommand.Command is null)
            {
                AddError(errors, $"{prefix}.command", "A typed command payload is required.");
                continue;
            }

            var payload = requestCommand.Command;
            if (payload is DeployCommandPayload deploy)
            {
                ValidateDeploy(errors, prefix, deploy, state, playerSide, reserveCatalog, unitCatalog, commandedReserveIds);
                deploymentCount++;
                if (deploymentCount > state.DeploymentLimitPerSidePerTurn)
                {
                    AddError(errors, $"{prefix}.command.reserveId", $"Side '{playerSide}' may deploy at most {state.DeploymentLimitPerSidePerTurn} reserve(s) per turn.");
                }
                continue;
            }

            UnitState? unit = null;
            if (string.IsNullOrWhiteSpace(payload.UnitId))
            {
                AddError(errors, $"{prefix}.command.unitId", "Unit is required.");
            }
            else if (!units.TryGetValue(payload.UnitId, out unit))
            {
                AddError(errors, $"{prefix}.command.unitId", $"Unit '{payload.UnitId}' does not exist in the latest campaign state.");
            }
            else
            {
                if (!commandedUnitIds.Add(unit.Id))
                {
                    AddError(errors, $"{prefix}.command.unitId", $"Unit '{unit.Id}' already has a command in this submission.");
                }
                if (unit.Side != playerSide)
                {
                    AddError(errors, $"{prefix}.command.unitId", $"Unit '{unit.Id}' does not belong to side '{playerSide}'.");
                }
                if (unit.Status == UnitStatus.Destroyed || unit.Strength <= 0)
                {
                    AddError(errors, $"{prefix}.command.unitId", $"Unit '{unit.Id}' has been destroyed.");
                }
            }

            switch (payload)
            {
                case MoveCommandPayload move:
                    ValidatePath(errors, prefix, move.FromRegionId, move.PathRegionIds, unit, state, regions, isMove: true);
                    break;
                case AttackCommandPayload attack:
                    ValidatePath(errors, prefix, attack.FromRegionId, attack.PathRegionIds, unit, state, regions, isMove: false);
                    break;
                case SupportCommandPayload support:
                    ValidateNearbyTarget(errors, prefix, support.FromRegionId, support.TargetRegionIdValue, unit, regions);
                    break;
                case ReconCommandPayload recon:
                    ValidateNearbyTarget(errors, prefix, recon.FromRegionId, recon.TargetRegionIdValue, unit, regions);
                    break;
                case HoldPositionCommandPayload hold:
                    ValidateCurrentRegion(errors, prefix, hold.RegionIdValue, unit, regions);
                    break;
                case ResupplyCommandPayload resupply:
                    ValidateCurrentRegion(errors, prefix, resupply.RegionIdValue, unit, regions);
                    if (unit is not null && !SupplyTracer.Trace(state, playerSide, unit.RegionId).IsConnected)
                    {
                        AddError(errors, $"{prefix}.command.regionId", $"Unit '{unit.Id}' has no controlled supply route and cannot resupply.");
                    }
                    break;
                default:
                    AddError(errors, $"{prefix}.command.commandType", $"Command payload '{payload.GetType().Name}' is not supported.");
                    break;
            }
        }

        ValidateBudget(errors, state, playerSide, request.Commands, reserveCatalog);

        ThrowIfInvalid(errors);
    }

    private static void ValidateBudget(
        Dictionary<string, List<string>> errors,
        GameState state,
        Side side,
        IReadOnlyList<SubmitCommandRequest> commands,
        ReserveCatalog? reserveCatalog)
    {
        var available = CommandEconomy.CreateTurnBudget(state, side);
        foreach (var indexed in commands
            .Select((command, index) => (Command: command, Index: index))
            .Where(item => item.Command?.Command is not null)
            .OrderBy(item => item.Command.Sequence))
        {
            var submitted = new SubmittedCommand(
                indexed.Command.Sequence,
                CommandSource.Human,
                side,
                indexed.Command.Command);
            var cost = CommandEconomy.CalculateCost(state, submitted, reserveCatalog);
            if (!CommandEconomy.CanAfford(available, cost))
            {
                AddError(
                    errors,
                    $"commands[{indexed.Index}].command",
                    $"Command sequence {submitted.Sequence} is unaffordable: insufficient {CommandEconomy.DescribeShortfall(available, cost)}.");
                continue;
            }
            available = CommandEconomy.Spend(available, cost);
        }
    }

    private static void ValidatePath(
        Dictionary<string, List<string>> errors,
        string prefix,
        string fromRegionId,
        IReadOnlyList<string> path,
        UnitState? unit,
        GameState state,
        IReadOnlyDictionary<string, RegionState> regions,
        bool isMove)
    {
        ValidateCurrentRegion(errors, prefix, fromRegionId, unit, regions);
        if (path is not { Count: > 0 })
        {
            AddError(errors, $"{prefix}.command.pathRegionIds", "Move and Attack commands require a non-empty ordered path.");
            return;
        }

        var current = fromRegionId;
        var movementCost = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal) { fromRegionId };
        for (var index = 0; index < path.Count; index++)
        {
            var next = path[index];
            if (!visited.Add(next))
            {
                AddError(errors, $"{prefix}.command.pathRegionIds[{index}]", $"Path revisits region '{next}'.");
            }
            if (!regions.ContainsKey(next))
            {
                AddError(errors, $"{prefix}.command.pathRegionIds[{index}]", $"Region '{next}' does not exist in the latest campaign state.");
                current = next;
                continue;
            }
            var route = state.Routes.FirstOrDefault(candidate => Connects(candidate, current, next));
            if (route is null)
            {
                AddError(errors, $"{prefix}.command.pathRegionIds[{index}]", $"No route connects '{current}' to '{next}'.");
            }
            else
            {
                movementCost += route.MovementCost;
            }
            current = next;
        }

        var movementAllowance = unit is null
            ? 0
            : OperationalPathfinder.EffectiveMovement(state, unit);
        if (unit is not null && movementCost > movementAllowance)
        {
            AddError(errors, $"{prefix}.command.pathRegionIds", $"Path movement cost {movementCost} exceeds unit '{unit.Id}' effective allowance {movementAllowance}.");
        }

        if (isMove && unit is not null)
        {
            var contact = path.FirstOrDefault(regionId => state.Units.Any(other =>
                other.Side != unit.Side
                && other.Side != Side.Neutral
                && other.Status != UnitStatus.Destroyed
                && other.RegionId == regionId));
            if (contact is not null)
            {
                AddError(errors, $"{prefix}.command.pathRegionIds", $"Path contacts enemy forces at '{contact}'. Use Attack instead.");
            }
        }
    }

    private static void ValidateNearbyTarget(
        Dictionary<string, List<string>> errors,
        string prefix,
        string fromRegionId,
        string targetRegionId,
        UnitState? unit,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        ValidateCurrentRegion(errors, prefix, fromRegionId, unit, regions);
        if (!regions.TryGetValue(targetRegionId, out var target))
        {
            AddError(errors, $"{prefix}.command.targetRegionId", $"Region '{targetRegionId}' does not exist in the latest campaign state.");
            return;
        }
        if (unit is not null && target.Id != unit.RegionId && !regions[unit.RegionId].AdjacentRegionIds.Contains(target.Id, StringComparer.Ordinal))
        {
            AddError(errors, $"{prefix}.command.targetRegionId", $"Target region '{target.Id}' must be current or adjacent to '{unit.RegionId}'.");
        }
    }

    private static void ValidateCurrentRegion(
        Dictionary<string, List<string>> errors,
        string prefix,
        string regionId,
        UnitState? unit,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        if (!regions.ContainsKey(regionId))
        {
            AddError(errors, $"{prefix}.command.regionId", $"Region '{regionId}' does not exist in the latest campaign state.");
        }
        else if (unit is not null && unit.RegionId != regionId)
        {
            AddError(errors, $"{prefix}.command.regionId", $"Region '{regionId}' is not the current region for unit '{unit.Id}'.");
        }
    }

    private static void ValidateDeploy(
        Dictionary<string, List<string>> errors,
        string prefix,
        DeployCommandPayload deploy,
        GameState state,
        Side playerSide,
        ReserveCatalog? reserveCatalog,
        UnitCatalog? unitCatalog,
        HashSet<string> commandedReserveIds)
    {
        if (!commandedReserveIds.Add(deploy.ReserveId))
        {
            AddError(errors, $"{prefix}.command.reserveId", $"Reserve '{deploy.ReserveId}' already has a command in this submission.");
        }

        var rejection = ReserveRules.ValidateDeployment(state, playerSide, deploy, reserveCatalog, unitCatalog);
        if (rejection is not null)
        {
            var field = rejection.StartsWith("Region '", StringComparison.Ordinal)
                ? $"{prefix}.command.targetRegionId"
                : $"{prefix}.command.reserveId";
            AddError(errors, field, rejection);
        }
    }

    private static bool Connects(RouteState route, string left, string right) =>
        route.FromRegionId == left && route.ToRegionId == right || route.FromRegionId == right && route.ToRegionId == left;

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
            errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }
}
