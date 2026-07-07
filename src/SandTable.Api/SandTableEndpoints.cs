namespace SandTable.Api;

public static class SandTableEndpoints
{
    public static IEndpointRouteBuilder MapSandTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

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

        group.MapPost("/campaigns/{campaignUid:guid}/commands", async (
            Guid campaignUid,
            SubmitCommandsRequest request,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SubmitCommandsAsync(campaignUid, request, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/campaigns/{campaignUid:guid}/resolve-turn", async (
            Guid campaignUid,
            CampaignService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ResolveTurnAsync(campaignUid, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }
}
