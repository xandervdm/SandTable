namespace SandTable.Engine.Tests;

public class EngineAssemblyTests
{
    [Fact]
    public void Engine_assembly_is_referenceable()
    {
        Assert.Equal("SandTable.Engine", typeof(Engine.EngineAssemblyMarker).Namespace);
    }
}
