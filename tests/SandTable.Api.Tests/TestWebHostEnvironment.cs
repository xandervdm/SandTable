using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace SandTable.Api.Tests;

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "SandTable.Api.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
