using Sati.Services;
using Sati.ViewModels.Children;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sati.Views
{
    /// <summary>
    /// Lets a case manager choose the square of a photo that becomes a consumer's profile picture.
    /// The geometry lives in <see cref="PhotoCropViewModel"/>; this window only turns pointer and
    /// key input into photo pixels and draws the result.
    /// </summary>
    public partial class PhotoCropWindow : Window
    {
        private const double SurfaceLimit = 420;

        private BitmapSource? _photo;
        private PhotoCropViewModel? _crop;
        private double _scale = 1;
        private Point? _dragFrom;
        private bool _syncingSlider;

        public PhotoCropWindow()
        {
            InitializeComponent();
        }

        /// <summary>The prepared 512-pixel JPEG, or null when the case manager cancelled.</summary>
        public byte[]? PreparedPhoto { get; private set; }

        public PhotoCropViewModel? Crop => _crop;

        public void Configure(BitmapSource uprightPhoto)
        {
            ArgumentNullException.ThrowIfNull(uprightPhoto);
            _photo = uprightPhoto;
            _crop = new PhotoCropViewModel(uprightPhoto.PixelWidth, uprightPhoto.PixelHeight);
            _crop.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PhotoCropViewModel.Crop))
                    Redraw();
            };

            _scale = Math.Min(SurfaceLimit / uprightPhoto.PixelWidth, SurfaceLimit / uprightPhoto.PixelHeight);
            Surface.Width = uprightPhoto.PixelWidth * _scale;
            Surface.Height = uprightPhoto.PixelHeight * _scale;
            PhotoImage.Width = Surface.Width;
            PhotoImage.Height = Surface.Height;
            PhotoImage.Source = uprightPhoto;

            SizeSlider.Minimum = _crop.MinimumSideLength;
            SizeSlider.Maximum = _crop.MaximumSideLength;
            SizeSlider.IsEnabled = _crop.MaximumSideLength > _crop.MinimumSideLength;
            Redraw();
            Loaded += (_, _) => CropFrame.Focus();
        }

        private void Redraw()
        {
            if (_crop is null || _photo is null)
                return;

            var crop = _crop.Crop;
            var frame = new Rect(crop.X * _scale, crop.Y * _scale, crop.Width * _scale, crop.Height * _scale);
            Canvas.SetLeft(CropFrame, frame.X);
            Canvas.SetTop(CropFrame, frame.Y);
            CropFrame.Width = frame.Width;
            CropFrame.Height = frame.Height;
            DimOverlay.Data = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, Surface.Width, Surface.Height)),
                new RectangleGeometry(frame));

            PreviewImage.Source = new CroppedBitmap(_photo, crop);

            _syncingSlider = true;
            SizeSlider.Value = crop.Width;
            _syncingSlider = false;
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_crop is null)
                return;

            var point = e.GetPosition(Surface);
            var crop = _crop.Crop;
            var inside = point.X >= crop.X * _scale && point.X <= (crop.X + crop.Width) * _scale &&
                         point.Y >= crop.Y * _scale && point.Y <= (crop.Y + crop.Height) * _scale;
            // A click outside the square brings it there, so the first drag starts from where
            // the case manager is looking rather than from wherever the square happened to be.
            if (!inside)
                _crop.Move(point.X / _scale - (crop.X + crop.Width / 2.0),
                           point.Y / _scale - (crop.Y + crop.Height / 2.0));

            _dragFrom = point;
            Surface.CaptureMouse();
            CropFrame.Focus();
            e.Handled = true;
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            if (_crop is null || _dragFrom is not Point from || e.LeftButton != MouseButtonState.Pressed)
                return;

            var point = e.GetPosition(Surface);
            _crop.Move((point.X - from.X) / _scale, (point.Y - from.Y) / _scale);
            _dragFrom = point;
        }

        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragFrom = null;
            Surface.ReleaseMouseCapture();
        }

        private void Surface_LostMouseCapture(object sender, MouseEventArgs e) => _dragFrom = null;

        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_crop is null)
                return;
            // Wheel forward zooms in, as in a photo viewer: the square gets smaller.
            _crop.Resize(_crop.Side * (e.Delta > 0 ? 0.92 : 1 / 0.92));
            e.Handled = true;
        }

        private void CropFrame_KeyDown(object sender, KeyEventArgs e)
        {
            if (_crop is null)
                return;

            var step = Math.Max(1, _crop.MaximumSideLength * 0.02);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                step *= 5;

            switch (e.Key)
            {
                case Key.Left: _crop.Move(-step, 0); break;
                case Key.Right: _crop.Move(step, 0); break;
                case Key.Up: _crop.Move(0, -step); break;
                case Key.Down: _crop.Move(0, step); break;
                case Key.OemPlus or Key.Add: _crop.Resize(_crop.Side + step); break;
                case Key.OemMinus or Key.Subtract: _crop.Resize(_crop.Side - step); break;
                default: return;
            }
            e.Handled = true;
        }

        private void CropFrame_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
            // A visible focus cue that does not depend on colour alone: the frame thickens.
            CropFrame.BorderThickness = new Thickness(CropFrame.IsKeyboardFocused ? 4 : 2);

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_syncingSlider)
                _crop?.Resize(e.NewValue);
        }

        private void Reset_Click(object sender, RoutedEventArgs e) => _crop?.Reset();

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (_photo is null || _crop is null)
                return;
            PreparedPhoto = ProfilePhotoPreparer.Render(_photo, _crop.Crop);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            PreparedPhoto = null;
            DialogResult = false;
        }
    }
}
