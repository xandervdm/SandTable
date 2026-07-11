namespace SandTable.Engine;

public static class CommandEconomy
{
    public static Resources CreateTurnBudget(GameState state, Side side)
    {
        var resources = state.Resources[side];
        return resources with
        {
            CommandPoints = Math.Max(0, resources.CommandPoints + CampaignModifierRules.Value(state, side, "commandPoints"))
        };
    }

    public static Resources CalculateCost(GameState state, SubmittedCommand command, ReserveCatalog? reserves = null)
    {
        if (command.Payload is DeployCommandPayload deploy)
        {
            return ReserveRules.CalculateDeploymentCost(state, deploy, reserves);
        }

        if (!state.CommandCosts.TryGetValue(command.CommandType, out var definition))
        {
            return Zero;
        }

        var movementCost = command.Payload switch
        {
            MoveCommandPayload move => CalculatePathMovementCost(state.Routes, move.FromRegionId, move.PathRegionIds),
            AttackCommandPayload attack => CalculatePathMovementCost(state.Routes, attack.FromRegionId, attack.PathRegionIds),
            _ => 0
        };
        var fuelDiscount = CampaignModifierRules.Value(state, command.Side, "fuelReserve");
        return new Resources(
            definition.FixedSupplies + definition.SuppliesPerMovementCost * movementCost,
            Manpower: 0,
            Math.Max(0, definition.FixedFuel + definition.FuelPerMovementCost * movementCost - fuelDiscount),
            Industry: 0,
            definition.BaseCommandPoints);
    }

    public static bool CanAfford(Resources available, Resources cost) =>
        available.Supplies >= cost.Supplies
        && available.Manpower >= cost.Manpower
        && available.Fuel >= cost.Fuel
        && available.Industry >= cost.Industry
        && available.CommandPoints >= cost.CommandPoints;

    public static Resources Spend(Resources available, Resources cost) => new(
        available.Supplies - cost.Supplies,
        available.Manpower - cost.Manpower,
        available.Fuel - cost.Fuel,
        available.Industry - cost.Industry,
        available.CommandPoints - cost.CommandPoints);

    public static string DescribeShortfall(Resources available, Resources cost)
    {
        var shortfalls = new List<string>();
        if (available.CommandPoints < cost.CommandPoints) shortfalls.Add("command points");
        if (available.Supplies < cost.Supplies) shortfalls.Add("supplies");
        if (available.Fuel < cost.Fuel) shortfalls.Add("fuel");
        if (available.Manpower < cost.Manpower) shortfalls.Add("manpower");
        if (available.Industry < cost.Industry) shortfalls.Add("industry");
        return string.Join(", ", shortfalls);
    }

    public static int CalculatePathMovementCost(
        IReadOnlyList<RouteState> routes,
        string fromRegionId,
        IReadOnlyList<string> pathRegionIds)
    {
        var total = 0;
        var current = fromRegionId;
        foreach (var next in pathRegionIds)
        {
            var route = routes.FirstOrDefault(candidate => Connects(candidate, current, next));
            if (route is null)
            {
                return 0;
            }
            total += route.MovementCost;
            current = next;
        }
        return total;
    }

    private static bool Connects(RouteState route, string left, string right) =>
        route.FromRegionId == left && route.ToRegionId == right
        || route.FromRegionId == right && route.ToRegionId == left;

    public static readonly Resources Zero = new(0, 0, 0, 0, 0);
}
