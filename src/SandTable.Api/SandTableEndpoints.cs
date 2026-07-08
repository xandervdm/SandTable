using SandTable.Engine;
using Microsoft.AspNetCore.Mvc;

namespace SandTable.Api;

public static class SandTableEndpoints
{
    public static IEndpointRouteBuilder MapSandTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/content/theatres", async (
            [FromServices] GameContentRepository contentRepository,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await contentRepository.ListTheatresAsync(cancellationToken));
        })
            .WithName("ListTheatres")
            .WithTags("Content")
            .Produces<IReadOnlyList<TheatreSummaryResponse>>();

        group.MapGet("/content/theatres/{theatreId}", async (
            string theatreId,
            [FromServices] GameContentRepository contentRepository,
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
        })
            .WithName("GetTheatre")
            .WithTags("Content")
            .Produces<TheatreSummaryResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/content/theatres/{theatreId}/scenarios/{scenarioId}", async (
            string theatreId,
            string scenarioId,
            [FromServices] GameContentRepository contentRepository,
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
        })
            .WithName("GetScenarioContent")
            .WithTags("Content")
            .Produces<ScenarioContentResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/campaigns", async (
            CreateCampaignRequest request,
            [FromServices] CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var campaign = await service.CreateCampaignAsync(request, cancellationToken);
            return Results.Created($"/api/campaigns/{campaign.Campaign.CampaignUid}", campaign);
        })
            .WithName("CreateCampaign")
            .WithTags("Campaigns")
            .Accepts<CreateCampaignRequest>("application/json")
            .Produces<CampaignDetailResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/campaigns", async ([FromServices] CampaignService service, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.ListCampaignsAsync(cancellationToken));
        })
            .WithName("ListCampaigns")
            .WithTags("Campaigns")
            .Produces<IReadOnlyList<CampaignSummaryResponse>>();

        group.MapGet("/campaigns/{campaignUid:guid}", async (
            Guid campaignUid,
            [FromServices] CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var campaign = await service.GetCampaignAsync(campaignUid, cancellationToken);
            return campaign is null ? Results.NotFound() : Results.Ok(campaign);
        })
            .WithName("GetCampaign")
            .WithTags("Campaigns")
            .Produces<CampaignDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/campaigns/{campaignUid:guid}/snapshot", async (
            Guid campaignUid,
            [FromServices] CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await service.GetLatestSnapshotAsync(campaignUid, cancellationToken);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        })
            .WithName("GetCampaignSnapshot")
            .WithTags("Campaigns")
            .Produces<SnapshotResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/campaigns/{campaignUid:guid}/state", async (
            Guid campaignUid,
            [FromServices] CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var state = await service.GetCampaignStateAsync(campaignUid, cancellationToken);
            return state is null ? Results.NotFound() : Results.Ok(state);
        })
            .WithName("GetCampaignState")
            .WithTags("Campaigns")
            .Produces<CampaignStateResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/campaigns/{campaignUid:guid}/events", async (
            Guid campaignUid,
            int? turnNumber,
            int? limit,
            CampaignEventOrder? order,
            [FromServices] CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var events = await service.ListCampaignEventsAsync(
                campaignUid,
                turnNumber,
                limit ?? 100,
                order ?? CampaignEventOrder.Chronological,
                cancellationToken);
            return events is null ? Results.NotFound() : Results.Ok(events);
        })
            .WithName("ListCampaignEvents")
            .WithTags("Campaigns")
            .Produces<IReadOnlyList<CampaignEventResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/campaigns/{campaignUid:guid}/commands", async (
            Guid campaignUid,
            SubmitCommandsRequest request,
            [FromServices] CampaignService service,
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
        })
            .WithName("SubmitCampaignCommands")
            .WithTags("Commands")
            .Accepts<SubmitCommandsRequest>("application/json")
            .Produces<SubmitCommandsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/campaigns/{campaignUid:guid}/tensions/{cardId}/choose", async (
            Guid campaignUid,
            string cardId,
            ChooseTensionOptionRequest request,
            [FromServices] CampaignService service,
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
        })
            .WithName("ChooseTensionOption")
            .WithTags("Tensions")
            .Accepts<ChooseTensionOptionRequest>("application/json")
            .Produces<ChooseTensionOptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/campaigns/{campaignUid:guid}/resolve-turn", async (
            Guid campaignUid,
            [FromServices] CampaignService service,
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
        })
            .WithName("ResolveCampaignTurn")
            .WithTags("Turns")
            .Produces<ResolveTurnResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
