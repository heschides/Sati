using Xunit;

namespace Sati.Api.Tests;

public sealed class SyntheticPipelineSafetyTests
{
    [Theory]
    [InlineData("SatiProduction")]
    [InlineData("SatiDemo")]
    [InlineData("master")]
    [InlineData("SatiApiTests")]
    [InlineData("SatiSyntheticPipeline_")]
    [InlineData("SatiSyntheticPipeline_00000000000000000000000000000000;DROP DATABASE master")]
    [InlineData("SatiSyntheticPipeline_00000000000000000000000000000000\n")]
    public void SqlHarnessRefusesAnyNonOwnedDatabaseName(string name)
        => Assert.Throws<InvalidOperationException>(() => SyntheticPipelineDatabase.ValidateOwnedName(name));

    [Fact]
    public void SqlHarnessAcceptsOnlyItsExactGuidNamespace()
        => SyntheticPipelineDatabase.ValidateOwnedName("SatiSyntheticPipeline_" + Guid.NewGuid().ToString("N"));
}
