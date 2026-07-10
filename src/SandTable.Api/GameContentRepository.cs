using System.Text.Json;
using SandTable.Engine;

namespace SandTable.Api;

public sealed record GameContentBundle(
    MapDefinition Map,
    ScenarioDefinition Scenario,
    UnitCatalog Units,
    ReserveCatalog Reserves,
    DoctrineCatalog Doctrines,
    ScenarioEventCatalog Events,
    TensionCardCatalog TensionCards);

public sealed class GameContentRepository(IWebHostEnvironment environment, IConfiguration configuration)
{
    public async Task<IReadOnlyList<TheatreSummaryResponse>> ListTheatresAsync(CancellationToken cancellationToken = default)
    {
        var theatresRoot = Path.Combine(ResolveContentRoot(), "theatres");
        if (!Directory.Exists(theatresRoot))
        {
            return Array.Empty<TheatreSummaryResponse>();
        }

        var theatres = new List<TheatreSummaryResponse>();
        foreach (var theatrePath in Directory.EnumerateDirectories(theatresRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(Path.Combine(theatrePath, "theatre.json")))
            {
                continue;
            }

            var package = await LoadPackageAsync(theatrePath, cancellationToken);
            theatres.Add(ToSummary(package));
        }

        return theatres;
    }

    public async Task<TheatreSummaryResponse?> GetTheatreSummaryAsync(
        string theatreId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(ResolveTheatrePath(theatreId), cancellationToken);
        return ToSummary(package);
    }

    public async Task<ScenarioContentResponse> LoadScenarioContentAsync(
        string theatreId,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(ResolveTheatrePath(theatreId), cancellationToken);
        var scenario = FindScenario(package, scenarioId);
        var assetById = package.Assets.Assets.ToDictionary(asset => asset.AssetId, StringComparer.Ordinal);
        var backgroundAsset = assetById[package.Display.BackgroundImage.AssetId];
        var display = new MapDisplayDefinition(
            package.Display.TheatreId,
            package.Display.CoordinateSystem,
            new MapDisplayBackground(ProjectAssetUrl(package.Manifest.TheatreId, backgroundAsset.File), package.Display.BackgroundImage.Fit),
            package.Display.Regions);
        var assets = new MapAssetCatalogResponse(package.Assets.Assets
            .Select(asset => new MapAssetResponse(
                asset.AssetId,
                asset.File,
                ProjectAssetUrl(package.Manifest.TheatreId, asset.File),
                asset.Origin,
                asset.Source,
                asset.CreatedDate,
                asset.License,
                asset.Attribution,
                asset.IntendedUse))
            .ToArray());

        return new ScenarioContentResponse(
            new TheatreMetadataResponse(
                package.Manifest.ContractVersion,
                package.Manifest.TheatreId,
                package.Manifest.Name,
                package.Manifest.DefaultScenarioId),
            package.Map,
            scenario,
            package.Units,
            package.Reserves,
            package.Doctrines,
            package.Events,
            package.TensionCards,
            assets,
            display);
    }

    public async Task<GameContentBundle> LoadAsync(
        string theatreId,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(ResolveTheatrePath(theatreId), cancellationToken);
        var scenario = FindScenario(package, scenarioId);
        return new GameContentBundle(
            package.Map,
            scenario,
            package.Units,
            package.Reserves,
            package.Doctrines,
            package.Events,
            package.TensionCards);
    }

    private async Task<LoadedTheatrePackage> LoadPackageAsync(
        string theatrePath,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadJsonAsync<TheatreManifest>(Path.Combine(theatrePath, "theatre.json"), "theatre.json", cancellationToken);
        var paths = TheatrePackageValidator.ValidateManifest(theatrePath, manifest);
        var map = await ReadJsonAsync<MapDefinition>(paths["files.map"], manifest.Files.Map, cancellationToken);
        var display = await ReadJsonAsync<MapDisplayContent>(paths["files.display"], manifest.Files.Display, cancellationToken);
        var assets = await ReadJsonAsync<MapAssetCatalog>(paths["files.assets"], manifest.Files.Assets, cancellationToken);
        var units = await ReadJsonAsync<UnitCatalog>(paths["files.units"], manifest.Files.Units, cancellationToken);
        var reserves = await ReadJsonAsync<ReserveCatalog>(paths["files.reserves"], manifest.Files.Reserves, cancellationToken);
        var doctrines = await ReadJsonAsync<DoctrineCatalog>(paths["files.doctrines"], manifest.Files.Doctrines, cancellationToken);
        var events = await ReadJsonAsync<ScenarioEventCatalog>(paths["files.events"], manifest.Files.Events, cancellationToken);
        var tensionCards = await ReadJsonAsync<TensionCardCatalog>(paths["files.tensionCards"], manifest.Files.TensionCards, cancellationToken);
        var scenarios = new Dictionary<string, ScenarioDefinition>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Scenarios.Count; index++)
        {
            var reference = manifest.Scenarios[index];
            scenarios.Add(reference.ScenarioId, await ReadJsonAsync<ScenarioDefinition>(
                paths[$"scenarios[{index}].file"],
                reference.File,
                cancellationToken));
        }

        var package = new LoadedTheatrePackage(
            theatrePath,
            manifest,
            map,
            display,
            assets,
            units,
            reserves,
            doctrines,
            events,
            tensionCards,
            scenarios);
        TheatrePackageValidator.ValidatePackage(package);
        return package;
    }

    private static TheatreSummaryResponse ToSummary(LoadedTheatrePackage package)
    {
        var scenarios = package.Manifest.Scenarios
            .Select(reference => package.Scenarios[reference.ScenarioId])
            .Select(scenario => new ScenarioSummaryResponse(
                scenario.ScenarioId,
                scenario.TheatreId,
                scenario.Name,
                scenario.StartDate,
                scenario.MaxTurns,
                scenario.DefaultSide))
            .ToArray();
        return new TheatreSummaryResponse(package.Manifest.TheatreId, package.Manifest.Name, scenarios);
    }

    private static ScenarioDefinition FindScenario(LoadedTheatrePackage package, string scenarioId)
    {
        if (!package.Scenarios.TryGetValue(scenarioId, out var scenario))
        {
            throw new FileNotFoundException(
                $"Theatre '{package.Manifest.TheatreId}' does not declare scenario '{scenarioId}'.",
                scenarioId);
        }

        return scenario;
    }

    private string ResolveTheatrePath(string theatreId)
    {
        if (string.IsNullOrWhiteSpace(theatreId))
        {
            throw new DirectoryNotFoundException("A theatre ID is required.");
        }

        var theatresRoot = Path.GetFullPath(Path.Combine(ResolveContentRoot(), "theatres"));
        var theatrePath = Path.GetFullPath(Path.Combine(theatresRoot, theatreId));
        if (!theatrePath.StartsWith(theatresRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(theatrePath))
        {
            throw new DirectoryNotFoundException($"Could not locate content/theatres/{theatreId}.");
        }

        return theatrePath;
    }

    private string ResolveContentRoot()
    {
        var configuredRoot = configuration["SandTable:ContentRoot"];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        var current = new DirectoryInfo(environment.ContentRootPath);
        while (current is not null)
        {
            var contentPath = Path.Combine(current.FullName, "content");
            if (Directory.Exists(contentPath))
            {
                return contentPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate content from {environment.ContentRootPath}.");
    }

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        string packageFile,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, ApiJson.SerializerOptions, cancellationToken);
            return value ?? throw new ContentValidationException($"{packageFile}: could not deserialize a value.");
        }
        catch (JsonException exception)
        {
            var location = exception.LineNumber.HasValue ? $" at line {exception.LineNumber + 1}" : string.Empty;
            throw new ContentValidationException($"{packageFile}: invalid JSON{location}: {exception.Message}");
        }
    }

    private static string ProjectAssetUrl(string theatreId, string relativePath)
    {
        var encodedPath = string.Join('/', relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        return $"/theatres/{Uri.EscapeDataString(theatreId)}/{encodedPath}";
    }
}
