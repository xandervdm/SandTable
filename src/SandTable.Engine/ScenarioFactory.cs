namespace SandTable.Engine;

public sealed class ContentValidationException(string message) : InvalidOperationException(message);

public sealed class ScenarioFactory
{
    public GameState CreateInitialState(
        MapDefinition map,
        ScenarioDefinition scenario,
        UnitCatalog unitCatalog,
        ReserveCatalog reserveCatalog,
        Side playerSide,
        int? randomSeed = null)
    {
        Validate(map, scenario, unitCatalog, reserveCatalog);

        var enemySide = playerSide == Side.Axis ? Side.Allies : Side.Axis;
        var unitDefinitions = unitCatalog.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var reserveDefinitions = reserveCatalog.Reserves.ToDictionary(reserve => reserve.ReserveId, StringComparer.Ordinal);
        var regionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        var occupiedRegions = new HashSet<string>(StringComparer.Ordinal);
        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : null;
        var adjacency = map.Regions.ToDictionary(
            region => region.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var route in map.Routes)
        {
            adjacency[route.FromRegionId].Add(route.ToRegionId);
            adjacency[route.ToRegionId].Add(route.FromRegionId);
        }

        var units = scenario.StartingUnitIds
            .Select(unitId => unitDefinitions[unitId])
            .Select(unit => new UnitState(
                unit.Id,
                unit.Name,
                unit.Side,
                unit.Type,
                SelectDeploymentRegion(unit, regionIds, occupiedRegions, random),
                unit.Strength,
                unit.MaxStrength,
                unit.Movement,
                unit.Attack,
                unit.Defence,
                unit.Supply,
                unit.Morale,
                unit.Experience,
                unit.Status))
            .ToArray();
        var reserves = scenario.ReserveIds
            .Select(reserveId => reserveDefinitions[reserveId])
            .Select(reserve => new ReserveState(
                reserve.ReserveId,
                reserve.UnitId,
                reserve.Side,
                reserve.AvailableTurn <= 1 ? ReserveStatus.Available : ReserveStatus.Unavailable,
                reserve.AvailableTurn,
                DeploymentTurn: null,
                DeployedUnitId: null))
            .ToArray();

        return new GameState(
            map.TheatreId,
            scenario.ScenarioId,
            scenario.Name,
            TurnNumber: 1,
            scenario.MaxTurns,
            scenario.StartDate,
            playerSide,
            enemySide,
            new Dictionary<Side, Resources>(scenario.StartingResources),
            map.Regions.Select(region => new RegionState(
                region.Id,
                region.Name,
                region.Kind,
                region.Terrain,
                region.Owner,
                region.VictoryPoints,
                region.SupplyValue,
                region.Features,
                adjacency[region.Id].OrderBy(id => id, StringComparer.Ordinal).ToArray())).ToArray(),
            map.Routes.Select(route => new RouteState(
                route.Id,
                route.FromRegionId,
                route.ToRegionId,
                route.RouteType,
                route.MovementCost,
                route.SupplyCost)).ToArray(),
            units,
            reserves,
            new Dictionary<OrderType, CommandCostDefinition>(scenario.CommandCosts),
            scenario.DeploymentLimitPerSidePerTurn,
            scenario.VictoryRules,
            new Dictionary<string, int>(StringComparer.Ordinal),
            Array.Empty<string>(),
            IsComplete: false,
            Result: null);
    }

    public void Validate(
        MapDefinition map,
        ScenarioDefinition scenario,
        UnitCatalog unitCatalog,
        ReserveCatalog reserveCatalog)
    {
        if (map.TheatreId != scenario.TheatreId)
        {
            throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' targets theatre '{scenario.TheatreId}' but map is '{map.TheatreId}'.");
        }
        if (scenario.DefaultSide is not (Side.Axis or Side.Allies))
        {
            throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' must define a playable default side.");
        }
        if (!scenario.StartingResources.ContainsKey(Side.Axis) || !scenario.StartingResources.ContainsKey(Side.Allies))
        {
            throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' must define Axis and Allies starting resources.");
        }

        var regions = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var route in map.Routes)
        {
            if (!regions.Contains(route.FromRegionId) || !regions.Contains(route.ToRegionId))
            {
                throw new ContentValidationException($"Route '{route.Id}' references a missing region.");
            }
        }

        var units = unitCatalog.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        foreach (var startingUnitId in scenario.StartingUnitIds)
        {
            if (!units.TryGetValue(startingUnitId, out var unit))
            {
                throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' references missing unit '{startingUnitId}'.");
            }
            if (!regions.Contains(unit.RegionId))
            {
                throw new ContentValidationException($"Unit '{unit.Id}' starts in missing region '{unit.RegionId}'.");
            }
        }

        var reserves = reserveCatalog.Reserves.Select(reserve => reserve.ReserveId).ToHashSet(StringComparer.Ordinal);
        foreach (var reserveId in scenario.ReserveIds)
        {
            if (!reserves.Contains(reserveId))
            {
                throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' references missing reserve '{reserveId}'.");
            }
        }
    }

    private static string SelectDeploymentRegion(
        UnitDefinition unit,
        IReadOnlySet<string> regionIds,
        HashSet<string> occupiedRegions,
        Random? random)
    {
        if (random is null)
        {
            occupiedRegions.Add(unit.RegionId);
            return unit.RegionId;
        }

        var deploymentPool = (unit.DeploymentRegionIds is { Count: > 0 }
                ? unit.DeploymentRegionIds
                : [unit.RegionId])
            .Where(regionIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var availablePool = deploymentPool.Where(regionId => !occupiedRegions.Contains(regionId)).ToArray();
        var selectedPool = availablePool.Length > 0 ? availablePool : deploymentPool;
        var selectedRegionId = selectedPool[random.Next(selectedPool.Length)];
        occupiedRegions.Add(selectedRegionId);
        return selectedRegionId;
    }
}
