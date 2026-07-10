using SandTable.Engine;

namespace SandTable.Api;

internal static class TheatrePackageValidator
{
    public const string ContractVersion = "sandtable-content-v2";

    private static readonly HashSet<string> UnitTargetSelectors = new(StringComparer.Ordinal)
    {
        "unit",
        "enemyUnit",
        "playerArmourLowestSupply",
        "playerLineLowestDefence",
        "playerAirOrArmourLowestMorale"
    };

    private static readonly HashSet<string> EnemyUnitTargetSelectors = new(StringComparer.Ordinal)
    {
        "enemyHighestSupply"
    };

    private static readonly HashSet<string> RegionTargetSelectors = new(StringComparer.Ordinal)
    {
        "region",
        "playerFrontlineHighestVictory"
    };

    public static IReadOnlyDictionary<string, string> ValidateManifest(
        string theatrePath,
        TheatreManifest manifest)
    {
        Require(manifest.ContractVersion == ContractVersion, "theatre.json", "contractVersion",
            $"must be '{ContractVersion}'.");
        Require(!string.IsNullOrWhiteSpace(manifest.TheatreId), "theatre.json", "theatreId", "is required.");
        Require(string.Equals(Path.GetFileName(theatrePath), manifest.TheatreId, StringComparison.Ordinal),
            "theatre.json", "theatreId", $"'{manifest.TheatreId}' must match directory '{Path.GetFileName(theatrePath)}'.");
        Require(!string.IsNullOrWhiteSpace(manifest.Name), "theatre.json", "name", "is required.");
        Require(!string.IsNullOrWhiteSpace(manifest.DefaultScenarioId), "theatre.json", "defaultScenarioId", "is required.");
        Require(manifest.Files is not null, "theatre.json", "files", "is required.");
        Require(manifest.Scenarios is { Count: > 0 }, "theatre.json", "scenarios", "must contain at least one scenario.");

        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["files.map"] = ResolvePackageFile(theatrePath, manifest.Files.Map, "files.map"),
            ["files.display"] = ResolvePackageFile(theatrePath, manifest.Files.Display, "files.display"),
            ["files.assets"] = ResolvePackageFile(theatrePath, manifest.Files.Assets, "files.assets"),
            ["files.units"] = ResolvePackageFile(theatrePath, manifest.Files.Units, "files.units"),
            ["files.reserves"] = ResolvePackageFile(theatrePath, manifest.Files.Reserves, "files.reserves"),
            ["files.doctrines"] = ResolvePackageFile(theatrePath, manifest.Files.Doctrines, "files.doctrines"),
            ["files.events"] = ResolvePackageFile(theatrePath, manifest.Files.Events, "files.events"),
            ["files.tensionCards"] = ResolvePackageFile(theatrePath, manifest.Files.TensionCards, "files.tensionCards")
        };

        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Scenarios.Count; index++)
        {
            var scenario = manifest.Scenarios[index];
            Require(!string.IsNullOrWhiteSpace(scenario.ScenarioId), "theatre.json", $"scenarios[{index}].scenarioId", "is required.");
            Require(scenarioIds.Add(scenario.ScenarioId), "theatre.json", $"scenarios[{index}].scenarioId",
                $"duplicates '{scenario.ScenarioId}'.");
            paths[$"scenarios[{index}].file"] = ResolvePackageFile(theatrePath, scenario.File, $"scenarios[{index}].file");
        }

        Require(scenarioIds.Contains(manifest.DefaultScenarioId), "theatre.json", "defaultScenarioId",
            $"references missing scenario '{manifest.DefaultScenarioId}'.");

        var duplicatePath = paths
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        Require(duplicatePath is null, "theatre.json", duplicatePath?.First().Key ?? "files",
            $"resolves to the same file as {string.Join(", ", duplicatePath?.Select(pair => pair.Key) ?? [])}.");

        return paths;
    }

    public static void ValidatePackage(LoadedTheatrePackage package)
    {
        ValidateMap(package.Manifest, package.Map);
        ValidateAssets(package.TheatrePath, package.Manifest, package.Assets);
        ValidateDisplay(package.Manifest, package.Map, package.Display, package.Assets);
        ValidateUnits(package.Manifest, package.Map, package.Units);
        ValidateScenarios(package.Manifest, package.Map, package.Units, package.Scenarios);
        ValidateReserves(package.Manifest, package.Map, package.Units, package.Reserves);
        ValidateDoctrines(package.Manifest, package.Doctrines);
        ValidateEvents(package.Manifest, package.Map, package.Units, package.Events);
        ValidateTensionCards(package.Manifest, package.TensionCards);
    }

    private static void ValidateMap(TheatreManifest manifest, MapDefinition map)
    {
        var file = manifest.Files.Map;
        Require(map.TheatreId == manifest.TheatreId, file, "theatreId", $"must be '{manifest.TheatreId}'.");
        Require(!string.IsNullOrWhiteSpace(map.Name), file, "name", "is required.");
        Require(map.CoordinateSystem is not null, file, "coordinateSystem", "is required.");
        Require(map.CoordinateSystem.Width > 0, file, "coordinateSystem.width", "must be positive.");
        Require(map.CoordinateSystem.Height > 0, file, "coordinateSystem.height", "must be positive.");
        Require(map.Regions is { Count: > 0 }, file, "regions", "must contain at least one region.");
        Require(map.Routes is not null, file, "routes", "is required.");

        var regions = new Dictionary<string, RegionDefinition>(StringComparer.Ordinal);
        for (var index = 0; index < map.Regions.Count; index++)
        {
            var region = map.Regions[index];
            var field = $"regions[{index}]";
            Require(!string.IsNullOrWhiteSpace(region.Id), file, $"{field}.id", "is required.");
            Require(regions.TryAdd(region.Id, region), file, $"{field}.id", $"duplicates region '{region.Id}'.");
            Require(!string.IsNullOrWhiteSpace(region.Name), file, $"{field}.name", "is required.");
            Require(region.Position is not null, file, $"{field}.position", "is required.");
            Require(region.Position.X >= 0 && region.Position.X <= map.CoordinateSystem.Width,
                file, $"{field}.position.x", $"must be between 0 and {map.CoordinateSystem.Width}.");
            Require(region.Position.Y >= 0 && region.Position.Y <= map.CoordinateSystem.Height,
                file, $"{field}.position.y", $"must be between 0 and {map.CoordinateSystem.Height}.");
            Require(!string.IsNullOrWhiteSpace(region.Terrain), file, $"{field}.terrain", "is required.");
            Require(region.VictoryPoints >= 0, file, $"{field}.victoryPoints", "must not be negative.");
            Require(region.SupplyValue >= 0, file, $"{field}.supplyValue", "must not be negative.");
            Require(region.Features is not null, file, $"{field}.features", "is required.");
            Require(region.AdjacentRegionIds is not null, file, $"{field}.adjacentRegionIds", "is required.");
        }

        var adjacencyEdges = new HashSet<string>(StringComparer.Ordinal);
        for (var regionIndex = 0; regionIndex < map.Regions.Count; regionIndex++)
        {
            var region = map.Regions[regionIndex];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var adjacentIndex = 0; adjacentIndex < region.AdjacentRegionIds.Count; adjacentIndex++)
            {
                var adjacentId = region.AdjacentRegionIds[adjacentIndex];
                var field = $"regions[{regionIndex}].adjacentRegionIds[{adjacentIndex}]";
                Require(seen.Add(adjacentId), file, field, $"duplicates region '{adjacentId}'.");
                Require(adjacentId != region.Id, file, field, "must not reference the same region.");
                Require(regions.TryGetValue(adjacentId, out var adjacent), file, field,
                    $"references missing region '{adjacentId}'.");
                Require(adjacent!.AdjacentRegionIds.Contains(region.Id, StringComparer.Ordinal), file, field,
                    $"is not symmetric; region '{adjacentId}' does not reference '{region.Id}'.");
                adjacencyEdges.Add(EdgeKey(region.Id, adjacentId));
            }
        }

        var routeEdges = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < map.Routes.Count; index++)
        {
            var route = map.Routes[index];
            var field = $"routes[{index}]";
            Require(regions.ContainsKey(route.FromRegionId), file, $"{field}.fromRegionId",
                $"references missing region '{route.FromRegionId}'.");
            Require(regions.ContainsKey(route.ToRegionId), file, $"{field}.toRegionId",
                $"references missing region '{route.ToRegionId}'.");
            Require(route.FromRegionId != route.ToRegionId, file, field, "must connect two different regions.");
            Require(!string.IsNullOrWhiteSpace(route.RouteType), file, $"{field}.routeType", "is required.");
            var edge = EdgeKey(route.FromRegionId, route.ToRegionId);
            Require(routeEdges.Add(edge), file, field, $"duplicates route '{edge}'.");
            Require(adjacencyEdges.Contains(edge), file, field, "is not declared by region adjacency.");
        }

        foreach (var edge in adjacencyEdges)
        {
            Require(routeEdges.Contains(edge), file, "routes", $"is missing adjacency route '{edge}'.");
        }
    }

    private static void ValidateAssets(string theatrePath, TheatreManifest manifest, MapAssetCatalog catalog)
    {
        var file = manifest.Files.Assets;
        Require(catalog.Assets is { Count: > 0 }, file, "assets", "must contain at least one asset.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < catalog.Assets.Count; index++)
        {
            var asset = catalog.Assets[index];
            var field = $"assets[{index}]";
            Require(!string.IsNullOrWhiteSpace(asset.AssetId), file, $"{field}.assetId", "is required.");
            Require(ids.Add(asset.AssetId), file, $"{field}.assetId", $"duplicates asset '{asset.AssetId}'.");
            var fullPath = ResolvePackageFile(theatrePath, asset.File, $"{field}.file", file);
            var assetsRoot = Path.GetFullPath(Path.Combine(theatrePath, "assets")) + Path.DirectorySeparatorChar;
            Require(fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase), file, $"{field}.file",
                "must be inside the theatre assets directory.");
            Require(paths.Add(fullPath), file, $"{field}.file", $"duplicates asset file '{asset.File}'.");
            Require(!string.IsNullOrWhiteSpace(asset.IntendedUse), file, $"{field}.intendedUse", "is required.");
        }

        var assetsDirectory = Path.Combine(theatrePath, "assets");
        Require(Directory.Exists(assetsDirectory), file, "assets", "requires an assets directory.");
        foreach (var actualFile in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
        {
            Require(paths.Contains(Path.GetFullPath(actualFile)), file, "assets",
                $"does not declare asset file '{Path.GetRelativePath(theatrePath, actualFile).Replace('\\', '/')}'.");
        }
    }

    private static void ValidateDisplay(
        TheatreManifest manifest,
        MapDefinition map,
        MapDisplayContent display,
        MapAssetCatalog assets)
    {
        var file = manifest.Files.Display;
        Require(display.TheatreId == manifest.TheatreId, file, "theatreId", $"must be '{manifest.TheatreId}'.");
        Require(display.CoordinateSystem == map.CoordinateSystem, file, "coordinateSystem", "must match map.json.");
        Require(display.BackgroundImage is not null, file, "backgroundImage", "is required.");
        Require(assets.Assets.Any(asset => asset.AssetId == display.BackgroundImage.AssetId),
            file, "backgroundImage.assetId", $"references missing asset '{display.BackgroundImage.AssetId}'.");
        Require(!string.IsNullOrWhiteSpace(display.BackgroundImage.Fit), file, "backgroundImage.fit", "is required.");
        Require(display.Regions is not null, file, "regions", "is required.");

        var mapRegionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var mapRegionId in mapRegionIds)
        {
            Require(display.Regions.ContainsKey(mapRegionId), file, "regions", $"is missing region '{mapRegionId}'.");
        }

        foreach (var pair in display.Regions)
        {
            Require(mapRegionIds.Contains(pair.Key), file, $"regions.{pair.Key}", $"references missing region '{pair.Key}'.");
            var region = pair.Value;
            if (region.HitArea is not null)
            {
                Require(region.HitArea.Rx > 0, file, $"regions.{pair.Key}.hitArea.rx", "must be positive.");
                Require(region.HitArea.Ry > 0, file, $"regions.{pair.Key}.hitArea.ry", "must be positive.");
            }

            if (region.StackDirection is not null)
            {
                Require(region.StackDirection is "row" or "column" or "grid", file,
                    $"regions.{pair.Key}.stackDirection", "must be 'row', 'column', or 'grid'.");
            }
        }
    }

    private static void ValidateUnits(TheatreManifest manifest, MapDefinition map, UnitCatalog catalog)
    {
        var file = manifest.Files.Units;
        Require(catalog.Units is { Count: > 0 }, file, "units", "must contain at least one unit.");
        var regionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        var unitIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < catalog.Units.Count; index++)
        {
            var unit = catalog.Units[index];
            var field = $"units[{index}]";
            Require(!string.IsNullOrWhiteSpace(unit.Id), file, $"{field}.id", "is required.");
            Require(unitIds.Add(unit.Id), file, $"{field}.id", $"duplicates unit '{unit.Id}'.");
            Require(!string.IsNullOrWhiteSpace(unit.Name), file, $"{field}.name", "is required.");
            Require(unit.Side is Side.Axis or Side.Allies, file, $"{field}.side", "must be Axis or Allies.");
            Require(regionIds.Contains(unit.RegionId), file, $"{field}.regionId", $"references missing region '{unit.RegionId}'.");
            Require(unit.MaxStrength > 0, file, $"{field}.maxStrength", "must be positive.");
            Require(unit.Strength >= 0 && unit.Strength <= unit.MaxStrength, file, $"{field}.strength",
                $"must be between 0 and maxStrength {unit.MaxStrength}.");
            Require(unit.Movement >= 0, file, $"{field}.movement", "must not be negative.");
            Require(unit.Attack >= 0, file, $"{field}.attack", "must not be negative.");
            Require(unit.Defence >= 0, file, $"{field}.defence", "must not be negative.");
            Require(unit.Supply >= 0, file, $"{field}.supply", "must not be negative.");
            Require(unit.Morale >= 0, file, $"{field}.morale", "must not be negative.");
            Require(unit.Experience >= 0, file, $"{field}.experience", "must not be negative.");
            for (var deploymentIndex = 0; deploymentIndex < (unit.DeploymentRegionIds?.Count ?? 0); deploymentIndex++)
            {
                var deploymentRegionId = unit.DeploymentRegionIds![deploymentIndex];
                Require(regionIds.Contains(deploymentRegionId), file, $"{field}.deploymentRegionIds[{deploymentIndex}]",
                    $"references missing region '{deploymentRegionId}'.");
            }
        }
    }

    private static void ValidateScenarios(
        TheatreManifest manifest,
        MapDefinition map,
        UnitCatalog units,
        IReadOnlyDictionary<string, ScenarioDefinition> scenarios)
    {
        var regionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        var unitIds = units.Units.Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var reference in manifest.Scenarios)
        {
            Require(scenarios.TryGetValue(reference.ScenarioId, out var scenario), reference.File, "scenarioId",
                $"could not load manifest scenario '{reference.ScenarioId}'.");
            Require(scenario!.ScenarioId == reference.ScenarioId, reference.File, "scenarioId",
                $"must be '{reference.ScenarioId}'.");
            Require(scenario.TheatreId == manifest.TheatreId, reference.File, "theatreId", $"must be '{manifest.TheatreId}'.");
            Require(!string.IsNullOrWhiteSpace(scenario.Name), reference.File, "name", "is required.");
            Require(scenario.MaxTurns > 0, reference.File, "maxTurns", "must be positive.");
            Require(scenario.DefaultSide is Side.Axis or Side.Allies, reference.File, "defaultSide", "must be Axis or Allies.");
            ValidateResources(reference.File, "startingResources", scenario.StartingResources);
            Require(scenario.StartingUnitIds is { Count: > 0 }, reference.File, "startingUnitIds", "must contain at least one unit.");
            var startingIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < scenario.StartingUnitIds.Count; index++)
            {
                var unitId = scenario.StartingUnitIds[index];
                Require(startingIds.Add(unitId), reference.File, $"startingUnitIds[{index}]", $"duplicates unit '{unitId}'.");
                Require(unitIds.Contains(unitId), reference.File, $"startingUnitIds[{index}]", $"references missing unit '{unitId}'.");
            }

            Require(scenario.VictoryConditions is { Count: > 0 }, reference.File, "victoryConditions", "must contain at least one condition.");
            for (var index = 0; index < scenario.VictoryConditions.Count; index++)
            {
                var condition = scenario.VictoryConditions[index];
                Require(condition.Type == "ControlRegion", reference.File, $"victoryConditions[{index}].type",
                    $"uses unsupported type '{condition.Type}'.");
                Require(regionIds.Contains(condition.RegionId), reference.File, $"victoryConditions[{index}].regionId",
                    $"references missing region '{condition.RegionId}'.");
                Require(condition.RequiredOwner is Side.Axis or Side.Allies, reference.File,
                    $"victoryConditions[{index}].requiredOwner", "must be Axis or Allies.");
            }
        }
    }

    private static void ValidateReserves(
        TheatreManifest manifest,
        MapDefinition map,
        UnitCatalog units,
        ReserveCatalog catalog)
    {
        var file = manifest.Files.Reserves;
        Require(catalog.Reserves is not null, file, "reserves", "is required.");
        var regionById = map.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var unitById = units.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var scenarioIds = manifest.Scenarios.Select(scenario => scenario.ScenarioId).ToHashSet(StringComparer.Ordinal);
        var reserveIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < catalog.Reserves.Count; index++)
        {
            var reserve = catalog.Reserves[index];
            var field = $"reserves[{index}]";
            Require(!string.IsNullOrWhiteSpace(reserve.ReserveId), file, $"{field}.reserveId", "is required.");
            Require(reserveIds.Add(reserve.ReserveId), file, $"{field}.reserveId", $"duplicates reserve '{reserve.ReserveId}'.");
            Require(unitById.TryGetValue(reserve.UnitId, out var unit), file, $"{field}.unitId",
                $"references missing unit '{reserve.UnitId}'.");
            Require(unit!.Side == reserve.Side, file, $"{field}.side", $"must match unit side '{unit.Side}'.");
            Require(reserve.Side is Side.Axis or Side.Allies, file, $"{field}.side", "must be Axis or Allies.");
            Require(reserve.AvailableTurn > 0, file, $"{field}.availableTurn", "must be positive.");
            ValidateResources(file, $"{field}.cost", reserve.Cost);
            Require(reserve.EligibleRegionIds is { Count: > 0 }, file, $"{field}.eligibleRegionIds", "must contain at least one region.");
            for (var regionIndex = 0; regionIndex < reserve.EligibleRegionIds.Count; regionIndex++)
            {
                var regionId = reserve.EligibleRegionIds[regionIndex];
                Require(regionById.ContainsKey(regionId), file, $"{field}.eligibleRegionIds[{regionIndex}]",
                    $"references missing region '{regionId}'.");
            }

            foreach (var feature in reserve.RequiredRegionFeatures ?? [])
            {
                Require(map.Regions.Any(region => region.Features.Contains(feature, StringComparer.Ordinal)), file,
                    $"{field}.requiredRegionFeatures", $"references feature '{feature}' that no region provides.");
            }

            for (var scenarioIndex = 0; scenarioIndex < (reserve.ScenarioIds?.Count ?? 0); scenarioIndex++)
            {
                var scenarioId = reserve.ScenarioIds![scenarioIndex];
                Require(scenarioIds.Contains(scenarioId), file, $"{field}.scenarioIds[{scenarioIndex}]",
                    $"references missing scenario '{scenarioId}'.");
            }
        }
    }

    private static void ValidateDoctrines(TheatreManifest manifest, DoctrineCatalog catalog)
    {
        var file = manifest.Files.Doctrines;
        Require(catalog.Doctrines is not null, file, "doctrines", "is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < catalog.Doctrines.Count; index++)
        {
            var doctrine = catalog.Doctrines[index];
            Require(!string.IsNullOrWhiteSpace(doctrine.Id), file, $"doctrines[{index}].id", "is required.");
            Require(ids.Add(doctrine.Id), file, $"doctrines[{index}].id", $"duplicates doctrine '{doctrine.Id}'.");
            Require(!string.IsNullOrWhiteSpace(doctrine.Name), file, $"doctrines[{index}].name", "is required.");
            Require(doctrine.Modifiers is not null, file, $"doctrines[{index}].modifiers", "is required.");
        }
    }

    private static void ValidateEvents(
        TheatreManifest manifest,
        MapDefinition map,
        UnitCatalog units,
        ScenarioEventCatalog catalog)
    {
        var file = manifest.Files.Events;
        Require(catalog.Events is not null, file, "events", "is required.");
        var regionIds = map.Regions.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        var unitIds = units.Units.Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < catalog.Events.Count; index++)
        {
            var definition = catalog.Events[index];
            var field = $"events[{index}]";
            Require(!string.IsNullOrWhiteSpace(definition.Id), file, $"{field}.id", "is required.");
            Require(ids.Add(definition.Id), file, $"{field}.id", $"duplicates event '{definition.Id}'.");
            Require(definition.Trigger is not null && definition.Trigger.Turn > 0, file, $"{field}.trigger.turn", "must be positive.");
            Require(definition.Effect is not null, file, $"{field}.effect", "is required.");
            Require(definition.Effect.Type == "AddUnit", file, $"{field}.effect.type",
                $"uses unsupported type '{definition.Effect.Type}'.");
            Require(unitIds.Contains(definition.Effect.UnitId), file, $"{field}.effect.unitId",
                $"references missing unit '{definition.Effect.UnitId}'.");
            Require(regionIds.Contains(definition.Effect.RegionId), file, $"{field}.effect.regionId",
                $"references missing region '{definition.Effect.RegionId}'.");
            Require(!string.IsNullOrWhiteSpace(definition.Message), file, $"{field}.message", "is required.");
        }
    }

    private static void ValidateTensionCards(TheatreManifest manifest, TensionCardCatalog catalog)
    {
        var file = manifest.Files.TensionCards;
        Require(catalog.Cards is not null, file, "cards", "is required.");
        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        for (var cardIndex = 0; cardIndex < catalog.Cards.Count; cardIndex++)
        {
            var card = catalog.Cards[cardIndex];
            var field = $"cards[{cardIndex}]";
            Require(!string.IsNullOrWhiteSpace(card.Id), file, $"{field}.id", "is required.");
            Require(cardIds.Add(card.Id), file, $"{field}.id", $"duplicates card '{card.Id}'.");
            Require(!string.IsNullOrWhiteSpace(card.Title), file, $"{field}.title", "is required.");
            Require(!string.IsNullOrWhiteSpace(card.Description), file, $"{field}.description", "is required.");
            ValidateOptionalSelector(file, $"{field}.targeting.unitSelector", card.Targeting?.UnitSelector, UnitTargetSelectors);
            ValidateOptionalSelector(file, $"{field}.targeting.enemyUnitSelector", card.Targeting?.EnemyUnitSelector, EnemyUnitTargetSelectors);
            ValidateOptionalSelector(file, $"{field}.targeting.regionSelector", card.Targeting?.RegionSelector, RegionTargetSelectors);
            Require(card.Options is { Count: 2 }, file, $"{field}.options", "must contain exactly two options.");
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var optionIndex = 0; optionIndex < card.Options.Count; optionIndex++)
            {
                var option = card.Options[optionIndex];
                var optionField = $"{field}.options[{optionIndex}]";
                Require(!string.IsNullOrWhiteSpace(option.Id), file, $"{optionField}.id", "is required.");
                Require(optionIds.Add(option.Id), file, $"{optionField}.id", $"duplicates option '{option.Id}'.");
                Require(!string.IsNullOrWhiteSpace(option.Label), file, $"{optionField}.label", "is required.");
                Require(!string.IsNullOrWhiteSpace(option.Description), file, $"{optionField}.description", "is required.");
                Require(option.Effects is { Count: > 0 }, file, $"{optionField}.effects", "must contain at least one effect.");
                for (var effectIndex = 0; effectIndex < option.Effects.Count; effectIndex++)
                {
                    ValidateTensionEffect(file, $"{optionField}.effects[{effectIndex}]", option.Effects[effectIndex]);
                }
            }
        }
    }

    private static void ValidateTensionEffect(string file, string field, GameEffectDefinition effect)
    {
        Require(!string.IsNullOrWhiteSpace(effect.Description), file, $"{field}.description", "is required.");
        switch (effect.EffectType)
        {
            case "addResource":
                Require(effect.Resource.HasValue, file, $"{field}.resource", "is required.");
                Require(effect.Amount.HasValue, file, $"{field}.amount", "is required.");
                break;
            case "addCampaignModifier":
                Require(!string.IsNullOrWhiteSpace(effect.ModifierId), file, $"{field}.modifierId", "is required.");
                Require(!string.IsNullOrWhiteSpace(effect.Name), file, $"{field}.name", "is required.");
                Require(effect.DurationTurns > 0, file, $"{field}.durationTurns", "must be positive.");
                Require(effect.Values is not null, file, $"{field}.values", "is required.");
                break;
            case "modifyUnitStat":
                ValidateOptionalSelector(file, $"{field}.unitSelector", effect.UnitSelector, UnitTargetSelectors, required: true);
                Require(effect.Stat.HasValue, file, $"{field}.stat", "is required.");
                Require(effect.Amount.HasValue, file, $"{field}.amount", "is required.");
                break;
            case "modifyRegion":
                ValidateOptionalSelector(file, $"{field}.regionSelector", effect.RegionSelector, RegionTargetSelectors, required: true);
                Require(effect.Owner.HasValue || effect.SupplyValueDelta.HasValue || !string.IsNullOrWhiteSpace(effect.AddFeature),
                    file, field, "must change owner, supplyValue, or features.");
                break;
            case "addGameEvent":
                Require(!string.IsNullOrWhiteSpace(effect.Summary), file, $"{field}.summary", "is required.");
                Require(effect.EventType.HasValue, file, $"{field}.eventType", "is required.");
                Require(effect.EventScope.HasValue, file, $"{field}.eventScope", "is required.");
                Require(effect.SideSelector is null or "unit" or "enemyUnit" or "player" or "enemy",
                    file, $"{field}.sideSelector", $"uses unsupported selector '{effect.SideSelector}'.");
                if (effect.UnitSelector is not null)
                {
                    ValidateOptionalSelector(file, $"{field}.unitSelector", effect.UnitSelector, UnitTargetSelectors);
                }
                if (effect.RegionSelector is not null)
                {
                    ValidateOptionalSelector(file, $"{field}.regionSelector", effect.RegionSelector, RegionTargetSelectors);
                }
                break;
            default:
                Require(false, file, $"{field}.effectType", $"uses unsupported effect '{effect.EffectType}'.");
                break;
        }
    }

    private static void ValidateOptionalSelector(
        string file,
        string field,
        string? selector,
        IReadOnlySet<string> allowed,
        bool required = false)
    {
        Require(!required || !string.IsNullOrWhiteSpace(selector), file, field, "is required.");
        if (!string.IsNullOrWhiteSpace(selector))
        {
            Require(allowed.Contains(selector), file, field, $"uses unsupported selector '{selector}'.");
        }
    }

    private static void ValidateResources(string file, string field, Resources resources)
    {
        Require(resources is not null, file, field, "is required.");
        Require(resources.Supplies >= 0, file, $"{field}.supplies", "must not be negative.");
        Require(resources.Manpower >= 0, file, $"{field}.manpower", "must not be negative.");
        Require(resources.Fuel >= 0, file, $"{field}.fuel", "must not be negative.");
        Require(resources.Industry >= 0, file, $"{field}.industry", "must not be negative.");
        Require(resources.CommandPoints >= 0, file, $"{field}.commandPoints", "must not be negative.");
    }

    private static string ResolvePackageFile(
        string theatrePath,
        string relativePath,
        string field,
        string manifestFile = "theatre.json")
    {
        Require(!string.IsNullOrWhiteSpace(relativePath), manifestFile, field, "is required.");
        Require(!Path.IsPathRooted(relativePath), manifestFile, field, "must be relative.");
        var theatreRoot = Path.GetFullPath(theatrePath) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(theatrePath, relativePath));
        Require(fullPath.StartsWith(theatreRoot, StringComparison.OrdinalIgnoreCase), manifestFile, field,
            $"escapes theatre directory with path '{relativePath}'.");
        Require(File.Exists(fullPath), manifestFile, field, $"references missing file '{relativePath}'.");
        return fullPath;
    }

    private static string EdgeKey(string left, string right) =>
        string.CompareOrdinal(left, right) < 0 ? $"{left}<->{right}" : $"{right}<->{left}";

    private static void Require(bool condition, string file, string field, string message)
    {
        if (!condition)
        {
            throw new ContentValidationException($"{file}: {field} {message}");
        }
    }
}
