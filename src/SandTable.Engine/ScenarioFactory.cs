namespace SandTable.Engine;

public sealed class ContentValidationException(string message) : InvalidOperationException(message);

public sealed class ScenarioFactory
{
    public GameState CreateInitialState(
        MapDefinition map,
        ScenarioDefinition scenario,
        UnitCatalog unitCatalog,
        Side? playerSide = null,
        int? randomSeed = null)
    {
        Validate(map, scenario, unitCatalog);

        var actualPlayerSide = playerSide ?? scenario.DefaultSide;
        var enemySide = actualPlayerSide == Side.Axis ? Side.Allies : Side.Axis;
        var unitDefinitions = unitCatalog.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var regionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        var occupiedRegions = new HashSet<string>(StringComparer.Ordinal);
        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : null;
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

        var victoryRegionId = scenario.VictoryConditions
            .FirstOrDefault(condition => condition.Type == "ControlRegion")
            ?.RegionId;

        return new GameState(
            map.TheatreId,
            scenario.ScenarioId,
            scenario.Name,
            TurnNumber: 1,
            scenario.MaxTurns,
            scenario.StartDate,
            actualPlayerSide,
            enemySide,
            scenario.StartingResources,
            map.Regions.Select(region => new RegionState(
                region.Id,
                region.Name,
                region.Terrain,
                region.Owner,
                region.VictoryPoints,
                region.SupplyValue,
                region.Features,
                region.AdjacentRegionIds)).ToArray(),
            units,
            IsComplete: false,
            Result: null,
            victoryRegionId);
    }

    public void Validate(MapDefinition map, ScenarioDefinition scenario, UnitCatalog unitCatalog)
    {
        if (map.TheatreId != scenario.TheatreId)
        {
            throw new ContentValidationException($"Scenario '{scenario.ScenarioId}' targets theatre '{scenario.TheatreId}' but map is '{map.TheatreId}'.");
        }

        var regions = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var region in map.Regions)
        {
            foreach (var adjacentRegionId in region.AdjacentRegionIds)
            {
                if (!regions.Contains(adjacentRegionId))
                {
                    throw new ContentValidationException($"Region '{region.Id}' references missing adjacent region '{adjacentRegionId}'.");
                }
            }
        }

        foreach (var route in map.Routes)
        {
            if (!regions.Contains(route.FromRegionId) || !regions.Contains(route.ToRegionId))
            {
                throw new ContentValidationException($"Route '{route.FromRegionId}' -> '{route.ToRegionId}' references a missing region.");
            }
        }

        var units = unitCatalog.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        foreach (var unit in unitCatalog.Units)
        {
            foreach (var deploymentRegionId in unit.DeploymentRegionIds ?? Array.Empty<string>())
            {
                if (!regions.Contains(deploymentRegionId))
                {
                    throw new ContentValidationException($"Unit '{unit.Id}' references missing deployment region '{deploymentRegionId}'.");
                }
            }
        }

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

        foreach (var condition in scenario.VictoryConditions)
        {
            if (condition.Type == "ControlRegion" && !regions.Contains(condition.RegionId))
            {
                throw new ContentValidationException($"Victory condition references missing region '{condition.RegionId}'.");
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
        var availablePool = deploymentPool
            .Where(regionId => !occupiedRegions.Contains(regionId))
            .ToArray();
        var selectedPool = availablePool.Length > 0 ? availablePool : deploymentPool;
        var selectedRegionId = selectedPool[random.Next(selectedPool.Length)];
        occupiedRegions.Add(selectedRegionId);
        return selectedRegionId;
    }
}
