using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using SandTable.Api;

namespace SandTable.Api.Tests;

public class EndpointMetadataTests
{
    [Fact]
    public void SandTable_endpoints_have_stable_names_and_tags()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapSandTableEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        AssertEndpoint(endpoints, "ListTheatres", "Content");
        AssertEndpoint(endpoints, "GetTheatre", "Content");
        AssertEndpoint(endpoints, "GetScenarioContent", "Content");
        AssertEndpoint(endpoints, "CreateCampaign", "Campaigns");
        AssertEndpoint(endpoints, "ListCampaigns", "Campaigns");
        AssertEndpoint(endpoints, "GetCampaign", "Campaigns");
        AssertEndpoint(endpoints, "GetCampaignSnapshot", "Campaigns");
        AssertEndpoint(endpoints, "GetCampaignState", "Campaigns");
        AssertEndpoint(endpoints, "ListCampaignEvents", "Campaigns");
        AssertEndpoint(endpoints, "GetCampaignTimeline", "Campaigns");
        AssertEndpoint(endpoints, "ListCampaignTurns", "Turns");
        AssertEndpoint(endpoints, "GetCampaignTurn", "Turns");
        AssertEndpoint(endpoints, "SubmitCampaignCommands", "Commands");
        AssertEndpoint(endpoints, "ChooseTensionOption", "Tensions");
        AssertEndpoint(endpoints, "ResolveCampaignTurn", "Turns");
    }

    private static void AssertEndpoint(
        IReadOnlyCollection<RouteEndpoint> endpoints,
        string endpointName,
        string tag)
    {
        var endpoint = endpoints.SingleOrDefault(candidate => GetEndpointName(candidate) == endpointName);
        Assert.NotNull(endpoint);
        Assert.Contains(tag, GetTags(endpoint!));
    }

    private static string? GetEndpointName(RouteEndpoint endpoint)
    {
        return endpoint.Metadata
            .Select(metadata => metadata.GetType().GetProperty("EndpointName")?.GetValue(metadata) as string)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }

    private static IReadOnlyList<string> GetTags(RouteEndpoint endpoint)
    {
        return endpoint.Metadata
            .Select(metadata => metadata.GetType().GetProperty("Tags")?.GetValue(metadata))
            .OfType<IReadOnlyList<string>>()
            .SelectMany(tags => tags)
            .ToArray();
    }
}
