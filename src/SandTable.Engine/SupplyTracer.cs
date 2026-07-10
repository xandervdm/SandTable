namespace SandTable.Engine;

public sealed record SupplyTraceResult(bool IsConnected, int Cost, string? SourceRegionId);

public static class SupplyTracer
{
    private static readonly string[] SourceFeatures = ["Port", "SupplyDepot"];

    public static SupplyTraceResult Trace(GameState state, Side side, string destinationRegionId) =>
        Trace(state.Regions, state.Routes, side, destinationRegionId);

    public static SupplyTraceResult Trace(
        IReadOnlyCollection<RegionState> regionStates,
        IReadOnlyCollection<RouteState> routes,
        Side side,
        string destinationRegionId)
    {
        var regions = regionStates.ToDictionary(region => region.Id, StringComparer.Ordinal);
        if (!regions.TryGetValue(destinationRegionId, out var destination) || destination.Owner != side)
        {
            return new SupplyTraceResult(false, int.MaxValue, null);
        }

        var sources = regions.Values
            .Where(region => region.Owner == side
                && region.SupplyValue > 0
                && region.Features.Any(feature => SourceFeatures.Contains(feature, StringComparer.Ordinal)))
            .OrderBy(region => region.Id, StringComparer.Ordinal)
            .ToArray();

        SupplyTraceResult? best = null;
        foreach (var source in sources)
        {
            var cost = ShortestControlledPathCost(regions, routes, side, source.Id, destinationRegionId);
            if (cost <= source.SupplyValue && (best is null || cost < best.Cost))
            {
                best = new SupplyTraceResult(true, cost, source.Id);
            }
        }

        return best ?? new SupplyTraceResult(false, int.MaxValue, null);
    }

    public static bool HasConnection(
        GameState state,
        Side side,
        IReadOnlyCollection<string> sourceRegionIds,
        IReadOnlyCollection<string> destinationRegionIds)
    {
        var regions = state.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        foreach (var sourceId in sourceRegionIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!regions.TryGetValue(sourceId, out var source) || source.Owner != side || source.SupplyValue <= 0)
            {
                continue;
            }
            foreach (var destinationId in destinationRegionIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!regions.TryGetValue(destinationId, out var destination) || destination.Owner != side)
                {
                    continue;
                }
                if (ShortestControlledPathCost(regions, state.Routes, side, sourceId, destinationId) <= source.SupplyValue)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static int ShortestControlledPathCost(
        IReadOnlyDictionary<string, RegionState> regions,
        IReadOnlyCollection<RouteState> routes,
        Side side,
        string sourceRegionId,
        string destinationRegionId)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [sourceRegionId] = 0 };
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue(sourceRegionId, 0);

        while (queue.TryDequeue(out var current, out var currentCost))
        {
            if (current == destinationRegionId)
            {
                return currentCost;
            }
            if (distances[current] < currentCost)
            {
                continue;
            }

            foreach (var route in routes.Where(route => route.FromRegionId == current || route.ToRegionId == current))
            {
                var next = route.FromRegionId == current ? route.ToRegionId : route.FromRegionId;
                if (!regions.TryGetValue(next, out var nextRegion) || nextRegion.Owner != side)
                {
                    continue;
                }
                var candidate = currentCost + route.SupplyCost;
                if (!distances.TryGetValue(next, out var known) || candidate < known)
                {
                    distances[next] = candidate;
                    queue.Enqueue(next, candidate);
                }
            }
        }

        return int.MaxValue;
    }
}
