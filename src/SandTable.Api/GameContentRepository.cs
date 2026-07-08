using System.Text.Json;
using SandTable.Engine;

namespace SandTable.Api;

public sealed record GameContentBundle(
    MapDefinition Map,
    ScenarioDefinition Scenario,
    UnitCatalog Units,
    DoctrineCatalog Doctrines,
    ScenarioEventCatalog Events,
    TensionCardCatalog TensionCards);

public sealed class GameContentRepository(IWebHostEnvironment environment, IConfiguration configuration)
{
    public async Task<IReadOnlyList<TheatreSummaryResponse>> ListTheatresAsync(CancellationToken cancellationToken = default)
    {
        var contentRoot = ResolveContentRoot();
        var theatresRoot = Path.Combine(contentRoot, "theatres");
        if (!Directory.Exists(theatresRoot))
        {
            return Array.Empty<TheatreSummaryResponse>();
        }

        var theatres = new List<TheatreSummaryResponse>();
        foreach (var theatrePath in Directory.EnumerateDirectories(theatresRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapPath = Path.Combine(theatrePath, "map.json");
            if (!File.Exists(mapPath))
            {
                continue;
            }

            var map = await ReadJsonAsync<MapDefinition>(mapPath, cancellationToken);
            var scenarios = await LoadScenarioSummariesAsync(theatrePath, cancellationToken);
            theatres.Add(new TheatreSummaryResponse(map.TheatreId, map.Name, scenarios));
        }

        return theatres;
    }

    public async Task<TheatreSummaryResponse?> GetTheatreSummaryAsync(
        string theatreId,
        CancellationToken cancellationToken = default)
    {
        var theatrePath = ResolveTheatrePath(theatreId);
        var map = await ReadJsonAsync<MapDefinition>(Path.Combine(theatrePath, "map.json"), cancellationToken);
        var scenarios = await LoadScenarioSummariesAsync(theatrePath, cancellationToken);
        return new TheatreSummaryResponse(map.TheatreId, map.Name, scenarios);
    }

    public async Task<ScenarioContentResponse> LoadScenarioContentAsync(
        string theatreId,
        string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var theatrePath = ResolveTheatrePath(theatreId);
        var content = await LoadAsync(theatreId, scenarioId, cancellationToken);
        var display = await ReadOptionalJsonAsync<MapDisplayDefinition>(
            Path.Combine(theatrePath, "display.json"),
            cancellationToken);

        return new ScenarioContentResponse(
            content.Map,
            content.Scenario,
            content.Units,
            content.Doctrines,
            content.Events,
            display);
    }

    public async Task<GameContentBundle> LoadAsync(
        string theatreId = "north-africa",
        string scenarioId = "north-africa-1942",
        CancellationToken cancellationToken = default)
    {
        var theatrePath = ResolveTheatrePath(theatreId);
        var map = await ReadJsonAsync<MapDefinition>(Path.Combine(theatrePath, "map.json"), cancellationToken);
        var scenarioFileName = scenarioId switch
        {
            "north-africa-1942" => "scenario-1942.json",
            _ => $"{scenarioId}.json"
        };
        var scenario = await ReadJsonAsync<ScenarioDefinition>(Path.Combine(theatrePath, scenarioFileName), cancellationToken);
        var units = await ReadJsonAsync<UnitCatalog>(Path.Combine(theatrePath, "units.json"), cancellationToken);
        var doctrines = await ReadJsonAsync<DoctrineCatalog>(Path.Combine(theatrePath, "doctrines.json"), cancellationToken);
        var events = await ReadJsonAsync<ScenarioEventCatalog>(Path.Combine(theatrePath, "events.json"), cancellationToken);
        var tensionCards = await ReadJsonAsync<TensionCardCatalog>(Path.Combine(theatrePath, "tension-cards.json"), cancellationToken);

        return new GameContentBundle(map, scenario, units, doctrines, events, tensionCards);
    }

    private string ResolveTheatrePath(string theatreId)
    {
        var theatrePath = Path.Combine(ResolveContentRoot(), "theatres", theatreId);
        if (!Directory.Exists(theatrePath))
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
            return configuredRoot;
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

    private static async Task<IReadOnlyList<ScenarioSummaryResponse>> LoadScenarioSummariesAsync(
        string theatrePath,
        CancellationToken cancellationToken)
    {
        var summaries = new List<ScenarioSummaryResponse>();
        foreach (var scenarioPath in Directory.EnumerateFiles(theatrePath, "scenario*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scenario = await ReadJsonAsync<ScenarioDefinition>(scenarioPath, cancellationToken);
            summaries.Add(new ScenarioSummaryResponse(
                scenario.ScenarioId,
                scenario.TheatreId,
                scenario.Name,
                scenario.StartDate,
                scenario.MaxTurns,
                scenario.DefaultSide));
        }

        return summaries
            .OrderBy(summary => summary.ScenarioId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, ApiJson.SerializerOptions, cancellationToken);
        return value ?? throw new InvalidOperationException($"Could not deserialize {path}.");
    }

    private static async Task<T?> ReadOptionalJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        return await ReadJsonAsync<T>(path, cancellationToken);
    }
}
