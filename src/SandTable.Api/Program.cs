var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "SandTable.Api"
}));

app.Run();

public partial class Program;
