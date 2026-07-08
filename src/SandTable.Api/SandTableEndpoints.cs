using SandTable.Engine;

namespace SandTable.Api;

public static class SandTableEndpoints
{
    public static IEndpointRouteBuilder MapSandTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/content/theatres", async (
            GameContentRepository contentRepository,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await contentRepository.ListTheatresAsync(cancellationToken));
        });

        group.MapGet("/content/theatres/{theatreId}", async (
            string theatreId,
            GameContentRepository contentRepository,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var theatre = await contentRepository.GetTheatreSummaryAsync(theatreId, cancellationToken);
                return theatre is null ? Results.NotFound() : Results.Ok(theatre);
            }
            catch (DirectoryNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet("/content/theatres/{theatreId}/scenarios/{scenarioId}", async (
            string theatreId,
            string scenarioId,
            GameContentRepository contentRepository,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await contentRepository.LoadScenarioContentAsync(theatreId, scenarioId, cancellationToken));
            }
            catch (DirectoryNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/campaigns", async (
            CreateCampaignRequest request,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var campaign = await service.CreateCampaignAsync(request, cancellationToken);
            return Results.Created($"/api/campaigns/{campaign.Campaign.CampaignUid}", campaign);
        });

        group.MapGet("/campaigns", async (CampaignService service, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.ListCampaignsAsync(cancellationToken));
        });

        group.MapGet("/campaigns/{campaignUid:guid}", async (
            Guid campaignUid,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var campaign = await service.GetCampaignAsync(campaignUid, cancellationToken);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        });

        group.MapGet("/campaigns/{campaignUid:guid}/snapshot", async (
            Guid campaignUid,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.GetLatestSnapshotAsync(campaignUid, cancellationToken);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        });

        group.MapGet("/campaigns/{campaignUid:guid}/state", async (
            Guid campaignUid,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var state = await service.GetCampaignStateAsync(campaignUid, cancellationToken);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        group.MapGet("/campaigns/{campaignUid:guid}/events", async (
            Guid campaignUid,
            int? turnNumber,
            int? limit,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var events = await service.ListCampaignEventsAsync(
                campaignUid,
                turnNumber,
                limit ?? 100,
                cancellationToken);
            return events is null ? Results.NotFound() : Results.Ok(events);
        });

        group.MapPost("/campaigns/{campaignUid:guid}/commands", async (
            Guid campaignUid,
            SubmitCommandsRequest request,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.SubmitCommandsAsync(campaignUid, request, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ApiValidationException exception)
            {
                return ApiProblemResults.From(exception);
            }
        });

        group.MapPost("/campaigns/{campaignUid:guid}/tensions/{cardId}/choose", async (
            Guid campaignUid,
            string cardId,
            ChooseTensionOptionRequest request,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.ChooseTensionOptionAsync(campaignUid, cardId, request, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ApiValidationException exception)
            {
                return ApiProblemResults.From(exception);
            }
            catch (TensionChoiceValidationException exception)
            {
                return ApiProblemResults.From(exception);
            }
        });

        group.MapPost("/campaigns/{campaignUid:guid}/resolve-turn", async (
            Guid campaignUid,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.ResolveTurnAsync(campaignUid, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ApiValidationException exception)
            {
                return ApiProblemResults.From(exception);
            }
        });

        return app;
    }
}
