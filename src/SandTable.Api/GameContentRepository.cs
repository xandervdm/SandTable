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
        var configuredRoot = configuration["SandTable:ContentRoot"];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.Combine(configuredRoot, "theatres", theatreId);
        }

        var current = new DirectoryInfo(environment.ContentRootPath);
        while (current is not null)
        {
            var contentPath = Path.Combine(current.FullName, "content", "theatres", theatreId);
            if (Directory.Exists(contentPath))
            {
                return contentPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate content/theatres/{theatreId} from {environment.ContentRootPath}.");
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, ApiJson.SerializerOptions, cancellationToken);
        return value ?? throw new InvalidOperationException($"Could not deserialize {path}.");
    }
}
