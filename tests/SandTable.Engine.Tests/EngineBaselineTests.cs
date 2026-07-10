using SandTable.Engine;

namespace SandTable.Engine.Tests;

public sealed class EngineBaselineTests
{
    [Fact]
    public void Current_engine_version_identifies_the_v2_development_baseline()
    {
        Assert.Equal("sandtable-engine-v2", EngineBaseline.CurrentVersion);
    }
}
