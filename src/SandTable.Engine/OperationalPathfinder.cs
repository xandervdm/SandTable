namespace SandTable.Engine;

public static class OperationalPathfinder
{
    public static int EffectiveMovement(GameState state, UnitState unit) => Math.Max(0,
        unit.Movement
        - CampaignModifierRules.Value(state, unit.Side, "tempoCost")
        - (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 1 : 0)
        - (unit.Status == UnitStatus.Disrupted ? 1 : 0));

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> FindReachablePaths(
        GameState state,
        UnitState unit,
        bool includeEnemyOccupiedDestinations)
    {
        var regions = state.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var enemyOccupied = state.Units
            .Where(candidate => candidate.Side != unit.Side
                && candidate.Side != Side.Neutral
                && candidate.Status != UnitStatus.Destroyed
                && candidate.Strength > 0)
            .Select(candidate => candidate.RegionId)
            .ToHashSet(StringComparer.Ordinal);
        var allowance = EffectiveMovement(state, unit);
        var costs = new Dictionary<string, int>(StringComparer.Ordinal) { [unit.RegionId] = 0 };
        var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var queue = new PriorityQueue<(string RegionId, string[] Path), (int Cost, string TieBreak)>();
        queue.Enqueue((unit.RegionId, []), (0, unit.RegionId));

        while (queue.TryDequeue(out var current, out var priority))
        {
            if (costs[current.RegionId] < priority.Cost)
            {
                continue;
            }

            foreach (var route in state.Routes
                .Where(route => route.FromRegionId == current.RegionId || route.ToRegionId == current.RegionId)
                .OrderBy(route => route.Id, StringComparer.Ordinal))
            {
                var next = route.FromRegionId == current.RegionId ? route.ToRegionId : route.FromRegionId;
                if (!regions.ContainsKey(next))
                {
                    continue;
                }
                var nextCost = priority.Cost + route.MovementCost;
                if (nextCost > allowance || costs.TryGetValue(next, out var known) && known <= nextCost)
                {
                    continue;
                }

                var nextPath = current.Path.Append(next).ToArray();
                costs[next] = nextCost;
                paths[next] = nextPath;

                var isEnemyContact = enemyOccupied.Contains(next);
                if (!isEnemyContact)
                {
                    queue.Enqueue((next, nextPath), (nextCost, string.Join('/', nextPath)));
                }
                else if (!includeEnemyOccupiedDestinations)
                {
                    paths.Remove(next);
                }
            }
        }

        return paths;
    }

    public static string? FirstEnemyContact(
        IReadOnlyDictionary<string, UnitState> units,
        Side movingSide,
        IReadOnlyList<string> path) => path.FirstOrDefault(regionId => units.Values.Any(unit =>
            unit.RegionId == regionId
            && unit.Side != movingSide
            && unit.Side != Side.Neutral
            && unit.Status != UnitStatus.Destroyed
            && unit.Strength > 0));

    public static bool TryValidatePath(
        GameState state,
        UnitState unit,
        string fromRegionId,
        IReadOnlyList<string> path,
        out int movementCost,
        out string? error)
    {
        movementCost = 0;
        error = null;
        if (fromRegionId != unit.RegionId)
        {
            error = $"Region '{fromRegionId}' is not the current region for unit '{unit.Id}'.";
            return false;
        }
        if (path.Count == 0)
        {
            error = "Movement path must not be empty.";
            return false;
        }

        var current = fromRegionId;
        foreach (var next in path)
        {
            var route = state.Routes.FirstOrDefault(candidate => Connects(candidate, current, next));
            if (route is null)
            {
                error = $"No route connects '{current}' to '{next}'.";
                return false;
            }
            movementCost += route.MovementCost;
            current = next;
        }

        var allowance = EffectiveMovement(state, unit);
        if (movementCost > allowance)
        {
            error = $"Movement cost {movementCost} exceeds effective allowance {allowance}.";
            return false;
        }
        return true;
    }

    private static bool Connects(RouteState route, string left, string right) =>
        route.FromRegionId == left && route.ToRegionId == right
        || route.FromRegionId == right && route.ToRegionId == left;
}
