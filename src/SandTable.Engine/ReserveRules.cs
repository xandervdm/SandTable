namespace SandTable.Engine;

public static class ReserveRules
{
    public static Resources CalculateDeploymentCost(
        GameState state,
        DeployCommandPayload command,
        ReserveCatalog? catalog)
    {
        var commandCost = state.CommandCosts.TryGetValue(OrderType.Deploy, out var definition)
            ? new Resources(definition.FixedSupplies, 0, definition.FixedFuel, 0, definition.BaseCommandPoints)
            : CommandEconomy.Zero;
        var reserveCost = catalog?.Reserves.FirstOrDefault(reserve => reserve.ReserveId == command.ReserveId)?.Cost
            ?? CommandEconomy.Zero;

        return Add(commandCost, reserveCost);
    }

    public static string? ValidateDeployment(
        GameState state,
        Side side,
        DeployCommandPayload command,
        ReserveCatalog? reserves,
        UnitCatalog? units)
    {
        var reserveState = state.Reserves.FirstOrDefault(reserve => reserve.ReserveId == command.ReserveId);
        if (reserveState is null)
        {
            return $"Reserve '{command.ReserveId}' does not exist in the latest campaign state.";
        }
        if (reserveState.Side != side)
        {
            return $"Reserve '{command.ReserveId}' does not belong to side '{side}'.";
        }
        if (reserveState.Status != ReserveStatus.Available)
        {
            return $"Reserve '{command.ReserveId}' is '{reserveState.Status}', not Available.";
        }

        var definition = reserves?.Reserves.FirstOrDefault(reserve => reserve.ReserveId == command.ReserveId);
        if (definition is null)
        {
            return $"Reserve rules for '{command.ReserveId}' are unavailable.";
        }
        if (definition.Side != side
            || definition.ScenarioIds is { Count: > 0 }
            && !definition.ScenarioIds.Contains(state.ScenarioId, StringComparer.Ordinal))
        {
            return $"Reserve '{command.ReserveId}' is not eligible for this side and scenario.";
        }

        var target = state.Regions.FirstOrDefault(region => region.Id == command.TargetRegionIdValue);
        if (target is null)
        {
            return $"Region '{command.TargetRegionIdValue}' does not exist in the latest campaign state.";
        }
        if (!definition.EligibleRegionIds.Contains(target.Id, StringComparer.Ordinal))
        {
            return $"Region '{target.Id}' is not an eligible deployment position for reserve '{command.ReserveId}'.";
        }
        if (target.Owner != side)
        {
            return $"Region '{target.Id}' must be controlled by side '{side}' for deployment.";
        }
        var missingFeature = (definition.RequiredRegionFeatures ?? [])
            .FirstOrDefault(feature => !target.Features.Contains(feature, StringComparer.Ordinal));
        if (missingFeature is not null)
        {
            return $"Region '{target.Id}' requires feature '{missingFeature}' for deployment.";
        }
        if (!SupplyTracer.Trace(state, side, target.Id).IsConnected)
        {
            return $"Region '{target.Id}' has no controlled supply route for deployment.";
        }

        var unit = units?.Units.FirstOrDefault(candidate => candidate.Id == definition.UnitId);
        if (unit is null)
        {
            return $"Unit template '{definition.UnitId}' for reserve '{command.ReserveId}' is unavailable.";
        }
        if (unit.Side != side)
        {
            return $"Unit template '{unit.Id}' does not belong to side '{side}'.";
        }
        if (state.Units.Any(existing => existing.Id == unit.Id))
        {
            return $"Unit '{unit.Id}' has already been deployed.";
        }

        return null;
    }

    private static Resources Add(Resources left, Resources right) => new(
        left.Supplies + right.Supplies,
        left.Manpower + right.Manpower,
        left.Fuel + right.Fuel,
        left.Industry + right.Industry,
        left.CommandPoints + right.CommandPoints);
}
