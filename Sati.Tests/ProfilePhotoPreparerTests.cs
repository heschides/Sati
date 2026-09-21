using Sati.Contracts.V1;
using Sati.Services;
using Sati.ViewModels.Children;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

public sealed class ProfilePhotoPreparerTests
{
    private static readonly Color Red = Color.FromRgb(220, 30, 30);
    private static readonly Color Blue = Color.FromRgb(30, 30, 220);

    [Fact]
    public void APhotoLargerThanTheServerAcceptsBecomesASmallSquareItAccepts()
    {
        WpfUiHarness.Run(() =>
        {
            var original = Png(SplitImage(5_000, 3_000));
            Assert.Null(PersonPhotoRules.Inspect(original, null, out _));

            var upright = ProfilePhotoPreparer.LoadUpright(original, out var problem);

            Assert.NotNull(upright);
            Assert.Null(problem);
            Assert.Equal(ProfilePhotoPreparer.WorkingLongEdge, Math.Max(upright!.PixelWidth, upright.PixelHeight));
            var prepared = Prepare(upright);
            AssertAccepted(prepared);

            // The square is centred on the split, so it keeps both colours in their places.
            var result = BitmapFrame.Create(new MemoryStream(prepared), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            AssertNear(Red, PixelAt(result, 100, 256));
            AssertNear(Blue, PixelAt(result, 412, 256));
        });
    }

    [Fact]
    public void AMotionPhotoWithDataAfterTheImageIsAccepted()
    {
        WpfUiHarness.Run(() =>
        {
            // Pixel and Samsung motion photos append a video after the JPEG's end marker.
            var original = Jpeg(SplitImage(400, 300)).Concat(new byte[4_096]).ToArray();
            Assert.Null(PersonPhotoRules.Inspect(original, null, out _));

            var upright = ProfilePhotoPreparer.LoadUpright(original, out _);

            Assert.NotNull(upright);
            AssertAccepted(Prepare(upright!));
        });
    }

    [Fact]
    public void ASidewaysPhonePhotoIsTurnedUprightAndLosesItsOrientationTag()
    {
        WpfUiHarness.Run(() =>
        {
            // Stored 200x100, red left and blue right, tagged "rotate 90 clockwise to view".
            var original = Jpeg(SplitImage(200, 100), orientation: 6);

            var upright = ProfilePhotoPreparer.LoadUpright(original, out _)!;

            Assert.Equal(100, upright.PixelWidth);
            Assert.Equal(200, upright.PixelHeight);
            // Turning clockwise puts the left edge on top.
            AssertNear(Red, PixelAt(upright, 50, 20));
            AssertNear(Blue, PixelAt(upright, 50, 180));

            // The output must not carry the tag, or a viewer would turn it a second time.
            var prepared = Prepare(upright);
            var frame = BitmapFrame.Create(new MemoryStream(prepared), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Null(TryQuery(frame.Metadata as BitmapMetadata, "/app1/ifd/{ushort=274}"));
            Assert.Null(TryQuery(frame.Metadata as BitmapMetadata, "/app1/ifd/gps"));
        });
    }

    [Fact]
    public void ATinyPhotoIsEnlargedToTheFrame()
    {
        WpfUiHarness.Run(() =>
        {
            var upright = ProfilePhotoPreparer.LoadUpright(Png(SplitImage(60, 40)), out _)!;
            var crop = new PhotoCropViewModel(upright.PixelWidth, upright.PixelHeight);

            Assert.Equal(40, crop.Crop.Width);
            AssertAccepted(ProfilePhotoPreparer.Render(upright, crop.Crop));
        });
    }

    [Fact]
    public void TransparencyBecomesWhiteRatherThanBlack()
    {
        WpfUiHarness.Run(() =>
        {
            var clear = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 64 * 4);
            var upright = ProfilePhotoPreparer.LoadUpright(Png(clear), out _)!;

            var prepared = BitmapFrame.Create(
                new MemoryStream(Prepare(upright)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            AssertNear(Colors.White, PixelAt(prepared, 256, 256));
        });
    }

    [Fact]
    public void AFileThatIsNotAnImageIsRefusedWithASentence()
    {
        WpfUiHarness.Run(() =>
        {
            var upright = ProfilePhotoPreparer.LoadUpright("not a photo"u8.ToArray(), out var problem);

            Assert.Null(upright);
            Assert.Contains("could not open", problem, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheDefaultCropIsTheLargestSquarePlacedHighOnATallPhoto()
    {
        var crop = new PhotoCropViewModel(600, 1_000);

        Assert.Equal(new Int32Rect(0, 100, 600, 600), crop.Crop);
        Assert.Equal(new Int32Rect(200, 0, 600, 600), new PhotoCropViewModel(1_000, 600).Crop);
    }

    [Fact]
    public void NoMoveOrResizeCanTakeTheSquareOutsideThePhoto()
    {
        var crop = new PhotoCropViewModel(800, 500);

        crop.Move(-10_000, -10_000);
        Assert.Equal(new Int32Rect(0, 0, 500, 500), crop.Crop);

        crop.Resize(100);
        crop.Move(10_000, 10_000);
        Assert.Equal(new Int32Rect(700, 400, 100, 100), crop.Crop);

        crop.Resize(10_000);
        Assert.Equal(new Int32Rect(300, 0, 500, 500), crop.Crop);

        crop.Resize(1);
        Assert.Equal(PhotoCropViewModel.MinimumSide, crop.Crop.Width);
        Assert.Equal(crop.Crop.Width, crop.Crop.Height);
    }

    private static byte[] Prepare(BitmapSource upright) =>
        ProfilePhotoPreparer.Render(upright, new PhotoCropViewModel(upright.PixelWidth, upright.PixelHeight).Crop);

    private static void AssertAccepted(byte[] prepared)
    {
        var inspection = PersonPhotoRules.Inspect(prepared, "image/jpeg", out var problem);
        Assert.True(inspection is not null, problem);
        Assert.Equal("image/jpeg", inspection!.ContentType);
        Assert.Equal(ProfilePhotoPreparer.OutputSize, inspection.PixelWidth);
        Assert.Equal(ProfilePhotoPreparer.OutputSize, inspection.PixelHeight);
    }

    /// <summary>Red on the left half, blue on the right.</summary>
    private static BitmapSource SplitImage(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = x < width / 2 ? Red : Blue;
            var i = y * stride + x * 4;
            pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
    }

    private static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] Jpeg(BitmapSource source, ushort? orientation = null)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        BitmapMetadata? metadata = null;
        if (orientation is ushort value)
        {
            metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", value);
        }
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color PixelAt(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    private static void AssertNear(Color expected, Color actual)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) < 40 && Math.Abs(expected.G - actual.G) < 40 &&
            Math.Abs(expected.B - actual.B) < 40,
            $"Expected about {expected}, found {actual}.");
    }

    private static object? TryQuery(BitmapMetadata? metadata, string query)
    {
        try { return metadata?.GetQuery(query); }
        catch (Exception) { return null; }
    }
}
