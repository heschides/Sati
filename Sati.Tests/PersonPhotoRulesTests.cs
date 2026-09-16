using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class PersonPhotoRulesTests
{
    // A real 1x1 PNG keeps this test independent of WPF's decoder and the file system.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void ARealPngIsIdentifiedFromItsBytes()
    {
        var result = PersonPhotoRules.Inspect(OnePixelPng, "image/png", out var problem);

        Assert.Null(problem);
        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(1, result.PixelWidth);
        Assert.Equal(1, result.PixelHeight);
    }

    [Fact]
    public void ADeclaredTypeThatDisagreesWithTheBytesIsRejected()
    {
        var result = PersonPhotoRules.Inspect(OnePixelPng, "image/jpeg", out var problem);

        Assert.Null(result);
        Assert.Contains("does not match", problem);
    }

    [Fact]
    public void AnOversizedUploadIsRejectedBeforeItCanBeStored()
    {
        var result = PersonPhotoRules.Inspect(
            new byte[PersonPhotoRules.MaximumBytes + 1],
            "image/png",
            out var problem);

        Assert.Null(result);
        Assert.Contains("5 MB", problem);
    }
}
