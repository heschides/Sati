using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sati.Services;

/// <summary>
/// Turns whatever photo a case manager picks into the one shape the bio frame shows: an upright,
/// square, 512-pixel JPEG. Phones produce files the server rightly refuses to store as-is — over
/// 5 MB, over 4,096 pixels, sideways with an orientation tag, or with a motion-photo video appended
/// after the image — so the desktop decodes and re-encodes before upload instead.
/// </summary>
/// <remarks>
/// Re-encoding also drops the camera's metadata, including GPS location, which has no place on a
/// consumer's record. <see cref="Contracts.V1.PersonPhotoRules"/> still validates the result on
/// both the desktop and the API; this only guarantees that a reasonable photo passes.
/// </remarks>
public static class ProfilePhotoPreparer
{
    /// <summary>What a case manager may pick. Only the prepared output reaches the server.</summary>
    public const int MaximumInputBytes = 40 * 1024 * 1024;

    /// <summary>
    /// The long edge a picked photo is decoded to. Enough for a 512-pixel result from a
    /// quarter-size crop, and it keeps a 50-megapixel original from needing 200 MB to open.
    /// </summary>
    public const int WorkingLongEdge = 2_048;

    public const int OutputSize = 512;
    public const int JpegQuality = 90;

    /// <summary>
    /// Decodes <paramref name="content"/> upright and no larger than <see cref="WorkingLongEdge"/>.
    /// Null, with a sentence for the user, when it is not a photo Sati can open.
    /// </summary>
    public static BitmapSource? LoadUpright(byte[] content, out string? problem)
    {
        problem = null;
        if (content is not { Length: > 0 })
        {
            problem = "Choose a JPG or PNG photo.";
            return null;
        }
        if (content.Length > MaximumInputBytes)
        {
            problem = "That file is larger than 40 MB. Choose a smaller photo.";
            return null;
        }

        try
        {
            // Read the header and orientation tag first without decoding every pixel.
            var probe = BitmapFrame.Create(
                new MemoryStream(content, writable: false),
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);
            var orientation = ReadOrientation(probe.Metadata as BitmapMetadata);
            var width = probe.PixelWidth;
            var height = probe.PixelHeight;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = new MemoryStream(content, writable: false);
            if (Math.Max(width, height) > WorkingLongEdge)
            {
                if (width >= height) image.DecodePixelWidth = WorkingLongEdge;
                else image.DecodePixelHeight = WorkingLongEdge;
            }
            image.EndInit();
            image.Freeze();

            var upright = ApplyOrientation(image, orientation);
            return FlattenOntoWhite(upright);
        }
        catch (Exception error) when (error is NotSupportedException or FileFormatException
                                          or InvalidOperationException or ArgumentException
                                          or IOException or OverflowException)
        {
            problem = "Sati could not open that image. Choose a JPG or PNG photo.";
            return null;
        }
    }

    /// <summary>
    /// Crops <paramref name="upright"/> to <paramref name="crop"/> (a square, in its pixels) and
    /// scales it to <see cref="OutputSize"/> — smaller or larger — as a JPEG.
    /// </summary>
    public static byte[] Render(BitmapSource upright, Int32Rect crop)
    {
        ArgumentNullException.ThrowIfNull(upright);
        if (crop.Width <= 0 || crop.Height <= 0 ||
            crop.X < 0 || crop.Y < 0 ||
            crop.X + crop.Width > upright.PixelWidth || crop.Y + crop.Height > upright.PixelHeight)
            throw new ArgumentOutOfRangeException(nameof(crop), "The crop must lie inside the photo.");

        // Pure imaging, with no render pass: crop, then let WIC scale to the output size. A render
        // pass (RenderTargetBitmap) depends on the machine's composition state and can come back
        // blank, which would save a black square as someone's photo.
        var cropped = new CroppedBitmap(upright, crop);
        var factor = (double)OutputSize / crop.Width;
        BitmapSource scaled = new TransformedBitmap(cropped, new ScaleTransform(factor, factor));
        if (scaled.PixelWidth != OutputSize || scaled.PixelHeight != OutputSize)
        {
            // Rounding can land a pixel either side; trim, or scale once more, to exactly fit.
            var side = Math.Min(Math.Min(scaled.PixelWidth, scaled.PixelHeight), OutputSize);
            scaled = new CroppedBitmap(scaled, new Int32Rect(0, 0, side, side));
            if (side != OutputSize)
                scaled = new TransformedBitmap(scaled, new ScaleTransform((double)OutputSize / side, (double)OutputSize / side));
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(scaled, PixelFormats.Bgr24, null, 0)));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>
    /// An opaque, materialized copy with any transparency blended onto white. A JPEG has no
    /// alpha, and dropping it would turn a transparent background black.
    /// </summary>
    private static BitmapSource FlattenOntoWhite(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha == 255)
                continue;
            for (var channel = 0; channel < 3; channel++)
                pixels[i + channel] = (byte)((pixels[i + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
            pixels[i + 3] = 255;
        }

        var flattened = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        flattened.Freeze();
        return flattened;
    }

    /// <summary>EXIF orientation 1–8, or 1 when absent or unreadable.</summary>
    private static int ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null)
            return 1;
        foreach (var query in new[] { "System.Photo.Orientation", "/app1/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.GetQuery(query) is ushort value && value is >= 1 and <= 8)
                    return value;
            }
            catch (Exception error) when (error is NotSupportedException or ArgumentException
                                              or InvalidOperationException)
            {
                // Not every container answers every query; try the next spelling.
            }
        }
        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        // EXIF orientation: 2 mirror, 3 rotate 180, 4 flip, 5 transpose, 6 rotate 90 clockwise,
        // 7 transverse, 8 rotate 90 counter-clockwise.
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),
            6 => new RotateTransform(90),
            7 => Group(new RotateTransform(270), new ScaleTransform(-1, 1)),
            8 => new RotateTransform(270),
            _ => null
        };
        if (transform is null)
            return source;

        var turned = new TransformedBitmap(source, transform);
        turned.Freeze();
        return turned;
    }

    private static TransformGroup Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms) group.Children.Add(transform);
        return group;
    }
}
