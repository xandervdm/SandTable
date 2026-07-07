using System.Text.Json.Serialization;
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

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "SandTable.Api"
}));

app.MapSandTableEndpoints();

app.Run();

public partial class Program;
