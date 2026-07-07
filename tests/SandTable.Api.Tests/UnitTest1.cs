namespace SandTable.Api.Tests;

public class ApiAssemblyTests
{
    [Fact]
    public void Api_assembly_is_referenceable()
    {
        Assert.Equal("SandTable.Api", typeof(Program).Assembly.GetName().Name);
    }
}
