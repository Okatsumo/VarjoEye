using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VarjoEye.Core.Models;
using Windows.UI;

namespace VarjoEye.App.Features.Dashboard.Components;

public sealed partial class CameraPreview : UserControl
{
    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(nameof(Camera), typeof(CameraState), typeof(CameraPreview),
            new PropertyMetadata(null, OnCameraChanged));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(CameraPreview),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(255, 52, 126, 255))));

    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;

    public CameraPreview()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    public CameraState? Camera
    {
        get => (CameraState?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private static void OnCameraChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CameraPreview preview)
        {
            preview.Apply();
        }
    }

    private void Apply()
    {
        if (CameraLabel is null)
        {
            return;
        }

        var camera = Camera;
        CameraLabel.Text = camera?.Label ?? "Camera";
        if (camera is { IsAvailable: true, Pixels: not null, Width: > 0, Height: > 0 })
        {
            WaitingOverlay.Visibility = Visibility.Collapsed;
            PreviewImage.Source = WriteFrame(camera);
            return;
        }

        var available = camera is { IsAvailable: true, ImageAssetKey: not null };
        WaitingOverlay.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        PreviewImage.Source = available
            ? new BitmapImage(new Uri($"ms-appx:///Assets/Images/{camera!.ImageAssetKey}"))
            : null;
        _bitmap = null;
    }

    private WriteableBitmap WriteFrame(CameraState camera)
    {
        if (_bitmap is null || _bitmapWidth != camera.Width || _bitmapHeight != camera.Height)
        {
            _bitmap = new WriteableBitmap(camera.Width, camera.Height);
            _bitmapWidth = camera.Width;
            _bitmapHeight = camera.Height;
        }

        var pixels = camera.Pixels!;
        using (var buffer = _bitmap.PixelBuffer.AsStream())
        {
            var row = new byte[checked(camera.Width * 4)];
            for (var y = 0; y < camera.Height; y++)
            {
                var source = y * camera.Width;
                var dest = 0;
                for (var x = 0; x < camera.Width; x++)
                {
                    var gray = pixels[source + x];
                    row[dest] = gray;
                    row[dest + 1] = gray;
                    row[dest + 2] = gray;
                    row[dest + 3] = 255;
                    dest += 4;
                }

                buffer.Write(row, 0, row.Length);
            }
        }

        _bitmap.Invalidate();
        return _bitmap;
    }
}
