using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using SandTable.Api;
using SandTable.Engine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<SandTableConnectionFactory>();
builder.Services.AddSingleton<GameContentRepository>();
builder.Services.AddSingleton<DevPlayerBootstrapper>();
builder.Services.AddSingleton<ScenarioFactory>();
builder.Services.AddSingleton<BasicAiPlanner>();
builder.Services.AddSingleton<IGameEffectApplier, GameEffectApplier>();
builder.Services.AddSingleton<TensionChoiceResolver>();
builder.Services.AddSingleton<ITensionGenerator, BasicTensionGenerator>();
builder.Services.AddSingleton<TurnResolver>();
builder.Services.AddScoped<CampaignService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        if (exception is NpgsqlException)
        {
            await Results.Problem(
                    title: "Database unavailable",
                    detail: "SandTable could not reach its PostgreSQL database.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(context);
            return;
        }

        if (exception is ContentValidationException)
        {
            await Results.Problem(
                    title: "Invalid theatre content",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
            return;
        }

        await Results.Problem(
                title: "Unexpected API error",
                detail: "SandTable could not complete the request.",
                statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(context);
    });
});

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "SandTable.Api"
}))
    .WithName("GetHealth")
    .WithTags("Health")
    .Produces(StatusCodes.Status200OK);

app.MapSandTableEndpoints();

app.Run();

public partial class Program;
