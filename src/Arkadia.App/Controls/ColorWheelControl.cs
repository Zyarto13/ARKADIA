using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Arkadia.App.Controls;

public sealed class ColorWheelControl : FrameworkElement
{
    private const int BitmapSize = 240;
    private static readonly WriteableBitmap WheelBitmap = CreateWheelBitmap();
    private double _hue;
    private double _saturation;
    private bool _isUpdatingColor;

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorWheelControl),
            new FrameworkPropertyMetadata(
                Color.FromRgb(141, 170, 255),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                SelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public ColorWheelControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0)
        {
            return;
        }

        var origin = new Point((ActualWidth - diameter) / 2, (ActualHeight - diameter) / 2);
        drawingContext.DrawImage(WheelBitmap, new Rect(origin, new Size(diameter, diameter)));

        var radius = diameter / 2 - 2;
        var angle = _hue * Math.PI / 180d;
        var marker = new Point(
            origin.X + diameter / 2 + Math.Cos(angle) * radius * _saturation,
            origin.Y + diameter / 2 + Math.Sin(angle) * radius * _saturation);
        drawingContext.DrawEllipse(
            null,
            new Pen(Brushes.Black, 3),
            marker,
            7,
            7);
        drawingContext.DrawEllipse(
            null,
            new Pen(Brushes.White, 2),
            marker,
            7,
            7);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        UpdateSelection(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSelection(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void UpdateSelection(Point point)
    {
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > radius && distance > 0)
        {
            dx *= radius / distance;
            dy *= radius / distance;
            distance = radius;
        }

        _hue = (Math.Atan2(dy, dx) * 180d / Math.PI + 360d) % 360d;
        _saturation = Math.Clamp(distance / radius, 0d, 1d);
        _isUpdatingColor = true;
        try
        {
            SetCurrentValue(SelectedColorProperty, FromHsv(_hue, _saturation));
        }
        finally
        {
            _isUpdatingColor = false;
        }

        InvalidateVisual();
    }

    private static void SelectedColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheelControl)sender;
        if (!wheel._isUpdatingColor)
        {
            var color = (Color)e.NewValue;
            (wheel._hue, wheel._saturation) = ToHsv(color);
        }

        wheel.InvalidateVisual();
    }

    private static WriteableBitmap CreateWheelBitmap()
    {
        var pixels = new byte[BitmapSize * BitmapSize * 4];
        var center = (BitmapSize - 1) / 2d;
        var radius = center;
        for (var y = 0; y < BitmapSize; y++)
        {
            for (var x = 0; x < BitmapSize; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var index = (y * BitmapSize + x) * 4;
                if (distance > radius)
                {
                    continue;
                }

                var hue = (Math.Atan2(dy, dx) * 180d / Math.PI + 360d) % 360d;
                var color = FromHsv(hue, distance / radius);
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(
            BitmapSize,
            BitmapSize,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, BitmapSize, BitmapSize),
            pixels,
            BitmapSize * 4,
            0);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color FromHsv(double hue, double saturation)
    {
        var chroma = saturation;
        var x = chroma * (1 - Math.Abs(hue / 60d % 2 - 1));
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var match = 1 - chroma;
        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static (double Hue, double Saturation) ToHsv(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var hue = delta == 0
            ? 0
            : maximum == red
                ? 60 * (((green - blue) / delta) % 6)
                : maximum == green
                    ? 60 * ((blue - red) / delta + 2)
                    : 60 * ((red - green) / delta + 4);
        return ((hue + 360) % 360, maximum == 0 ? 0 : delta / maximum);
    }
}
