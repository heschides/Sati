using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace Sati.ViewModels.Children;

/// <summary>
/// The square a case manager chooses from a photo, in the upright photo's own pixels. It is
/// always square and always wholly inside the photo, whatever the drag, wheel, slider, or key
/// asked for, so the rendered result can never include area that is not in the picture.
/// </summary>
public sealed partial class PhotoCropViewModel : ObservableObject
{
    /// <summary>The smallest square offered, so a stray wheel turn cannot zoom into a few pixels.</summary>
    public const int MinimumSide = 48;

    public PhotoCropViewModel(int photoWidth, int photoHeight)
    {
        if (photoWidth <= 0 || photoHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(photoWidth), "The photo must have a size.");

        PhotoWidth = photoWidth;
        PhotoHeight = photoHeight;
        MaximumSideLength = Math.Min(photoWidth, photoHeight);
        MinimumSideLength = Math.Min(MinimumSide, MaximumSideLength);
        Reset();
    }

    public int PhotoWidth { get; }
    public int PhotoHeight { get; }
    public int MinimumSideLength { get; }
    public int MaximumSideLength { get; }

    [ObservableProperty] private double x;
    [ObservableProperty] private double y;
    [ObservableProperty] private double side;

    /// <summary>The whole-pixel square to render.</summary>
    public Int32Rect Crop
    {
        get
        {
            var length = (int)Math.Round(Side);
            var left = (int)Math.Round(X);
            var top = (int)Math.Round(Y);
            left = Math.Clamp(left, 0, PhotoWidth - length);
            top = Math.Clamp(top, 0, PhotoHeight - length);
            return new Int32Rect(left, top, length, length);
        }
    }

    /// <summary>
    /// The largest square the photo allows, centred across and placed a little high on a tall
    /// photo, where a face usually is, rather than dead centre, which cuts off heads.
    /// </summary>
    public void Reset()
    {
        var length = MaximumSideLength;
        var left = (PhotoWidth - length) / 2.0;
        var top = (PhotoHeight - length) * 0.25;
        Place(left, top, length);
    }

    public void Move(double dx, double dy) => Place(X + dx, Y + dy, Side);

    /// <summary>Resizes about the square's centre, then clamps it back inside the photo.</summary>
    public void Resize(double newSide)
    {
        var length = Math.Clamp(newSide, MinimumSideLength, MaximumSideLength);
        var centerX = X + Side / 2;
        var centerY = Y + Side / 2;
        Place(centerX - length / 2, centerY - length / 2, length);
    }

    private void Place(double left, double top, double length)
    {
        length = Math.Clamp(length, MinimumSideLength, MaximumSideLength);
        Side = length;
        X = Math.Clamp(left, 0, PhotoWidth - length);
        Y = Math.Clamp(top, 0, PhotoHeight - length);
        OnPropertyChanged(nameof(Crop));
    }
}
