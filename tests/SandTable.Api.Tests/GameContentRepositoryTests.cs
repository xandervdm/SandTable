using Microsoft.Extensions.Configuration;
using SandTable.Api;

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
    }

    private static GameContentRepository CreateRepository()
    {
        return new GameContentRepository(
            new TestWebHostEnvironment { ContentRootPath = FindRepoRoot() },
            new ConfigurationManager());
    }

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
