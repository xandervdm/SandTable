using Microsoft.Extensions.Configuration;
using SandTable.Api;
using SandTable.Engine;

namespace SandTable.Api.Tests;

public class GameContentRepositoryTests
{
    [Fact]
    public async Task ListTheatresAsync_returns_north_africa_scenario_summary()
    {
        var repository = CreateRepository();

        var theatres = await repository.ListTheatresAsync();

        var theatre = Assert.Single(theatres);
        Assert.Equal("north-africa", theatre.TheatreId);
        Assert.Equal("North Africa", theatre.Name);
        var scenario = Assert.Single(theatre.Scenarios);
        Assert.Equal("north-africa-1942", scenario.ScenarioId);
        Assert.Equal(new DateOnly(1942, 6, 12), scenario.StartDate);
    }

    [Fact]
    public async Task LoadScenarioContentAsync_returns_map_scenario_and_unit_catalog()
    {
        var repository = CreateRepository();

        var content = await repository.LoadScenarioContentAsync("north-africa", "north-africa-1942");

        Assert.Equal("north-africa", content.Map.TheatreId);
        Assert.Equal("north-africa-1942", content.Scenario.ScenarioId);
        Assert.Contains(content.Units.Units, unit => unit.Id == "15th-panzer");
        Assert.Contains(content.Reserves.Reserves, reserve => reserve.ReserveId == "allied-armoured-reserve");
        Assert.Equal("sandtable-content-v2", content.Theatre.ContractVersion);
        Assert.Contains(content.Assets.Assets, asset => asset.AssetId == "map-base");
        Assert.NotNull(content.Display);
        Assert.Equal("/theatres/north-africa/assets/map-base.png", content.Display.BackgroundImage.Url);
        Assert.True(content.Display.Regions.ContainsKey("gazala"));
    }

    [Fact]
    public async Task Manifest_driven_renamed_fixture_loads_without_application_aliases()
    {
        var fixture = CreateRenamedFixture();
        try
        {
            var repository = CreateRepository(fixture.ContentRoot);

            var theatres = await repository.ListTheatresAsync();
            var content = await repository.LoadScenarioContentAsync("renamed-theatre", "renamed-scenario");

            var theatre = Assert.Single(theatres);
            Assert.Equal("renamed-theatre", theatre.TheatreId);
            Assert.Equal("renamed-scenario", Assert.Single(theatre.Scenarios).ScenarioId);
            Assert.Equal("renamed-theatre", content.Map.TheatreId);
            Assert.Equal("renamed-scenario", content.Scenario.ScenarioId);
            Assert.Equal("/theatres/renamed-theatre/assets/map-base.png", content.Display!.BackgroundImage.Url);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_reference_reports_package_file_field_and_missing_id()
    {
        var fixture = CreateRenamedFixture();
        try
        {
            var mapPath = Path.Combine(fixture.TheatreRoot, "map.json");
            var mapJson = await File.ReadAllTextAsync(mapPath);
            await File.WriteAllTextAsync(mapPath, mapJson.Replace(
                "\"toRegionId\": \"algiers\"",
                "\"toRegionId\": \"missing-region\"",
                StringComparison.Ordinal));
            var repository = CreateRepository(fixture.ContentRoot);

            var exception = await Assert.ThrowsAsync<ContentValidationException>(() =>
                repository.LoadScenarioContentAsync("renamed-theatre", "renamed-scenario"));

            Assert.Contains("map.json: routes[0].toRegionId", exception.Message, StringComparison.Ordinal);
            Assert.Contains("missing-region", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Unsupported_campaign_modifier_reports_exact_content_path()
    {
        var fixture = CreateRenamedFixture();
        try
        {
            var path = Path.Combine(fixture.TheatreRoot, "tension-cards.json");
            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace("\"supplyRisk\": 2", "\"unknownRule\": 2", StringComparison.Ordinal));
            var repository = CreateRepository(fixture.ContentRoot);

            var exception = await Assert.ThrowsAsync<ContentValidationException>(() =>
                repository.LoadScenarioContentAsync("renamed-theatre", "renamed-scenario"));

            Assert.Contains("tension-cards.json: cards[0].options[0].effects[2].values.unknownRule", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static GameContentRepository CreateRepository(string? contentRoot = null)
    {
        var configuration = new ConfigurationManager();
        if (contentRoot is not null)
        {
            configuration["SandTable:ContentRoot"] = contentRoot;
        }

        return new GameContentRepository(
            new TestWebHostEnvironment { ContentRootPath = FindRepoRoot() },
            configuration);
    }

    private static FixturePaths CreateRenamedFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sandtable-content-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(root, "content");
        var theatreRoot = Path.Combine(contentRoot, "theatres", "renamed-theatre");
        var sourceRoot = Path.Combine(FindRepoRoot(), "content", "theatres", "north-africa");
        Directory.CreateDirectory(theatreRoot);

        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (relativePath == Path.Combine("scenarios", "north-africa-1942.json"))
            {
                relativePath = Path.Combine("scenarios", "renamed-scenario.json");
            }

            var destinationPath = Path.Combine(theatreRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = File.ReadAllText(sourcePath)
                    .Replace("north-africa-1942", "renamed-scenario", StringComparison.Ordinal)
                    .Replace("north-africa", "renamed-theatre", StringComparison.Ordinal)
                    .Replace("North Africa", "Renamed Fixture", StringComparison.Ordinal);
                File.WriteAllText(destinationPath, json);
            }
            else
            {
                File.Copy(sourcePath, destinationPath);
            }
        }

        return new FixturePaths(root, contentRoot, theatreRoot);
    }

    private sealed record FixturePaths(string Root, string ContentRoot, string TheatreRoot);

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SandTable.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SandTable.slnx.");
    }

}
