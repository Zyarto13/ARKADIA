using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Arkadia.App.Controls;

public static class ButtonInteractionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ButtonInteractionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Button button)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            button.MouseEnter += ButtonMouseEnter;
            button.MouseLeave += ButtonMouseLeave;
            button.GotKeyboardFocus += ButtonGotKeyboardFocus;
            button.LostKeyboardFocus += ButtonLostKeyboardFocus;
            button.PreviewMouseLeftButtonDown += ButtonPreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += ButtonPreviewMouseLeftButtonUp;
        }
        else
        {
            button.MouseEnter -= ButtonMouseEnter;
            button.MouseLeave -= ButtonMouseLeave;
            button.GotKeyboardFocus -= ButtonGotKeyboardFocus;
            button.LostKeyboardFocus -= ButtonLostKeyboardFocus;
            button.PreviewMouseLeftButtonDown -= ButtonPreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp -= ButtonPreviewMouseLeftButtonUp;
        }
    }

    private static void ButtonMouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is Button button)
        {
            SetGlow(button, true);
        }
    }

    private static void ButtonMouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is Button button && !button.IsKeyboardFocusWithin)
        {
            SetGlow(button, false);
        }
    }

    private static void ButtonGotKeyboardFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button button)
        {
            SetGlow(button, true);
        }
    }

    private static void ButtonLostKeyboardFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button button && !button.IsMouseOver)
        {
            SetGlow(button, false);
        }
    }

    private static void SetGlow(Button button, bool isActive)
    {
        if (!button.IsEnabled)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            if (!isActive && button.Effect is DropShadowEffect existingGlow)
            {
                existingGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
                existingGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                existingGlow.BlurRadius = 0;
                existingGlow.Opacity = 0;
            }

            return;
        }

        var glow = isActive ? GetGlow(button) : button.Effect as DropShadowEffect;
        if (glow is null)
        {
            return;
        }

        Animate(glow, DropShadowEffect.BlurRadiusProperty, isActive ? 18 : 0, isActive ? 180 : 220);
        Animate(glow, DropShadowEffect.OpacityProperty, isActive ? 0.42 : 0, isActive ? 180 : 220);
        AnimateSurface(button, isActive ? 1.015 : 1, isActive ? 180 : 220);
    }

    private static void ButtonPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not Button button
            || !SystemParameters.ClientAreaAnimation
            || !button.IsEnabled
            || !IsNearestButton(button, args.OriginalSource as DependencyObject))
        {
            return;
        }

        var rippleHost = button.Template.FindName("InteractionRippleHost", button) as Canvas;
        var accentBrush = button.TryFindResource("AccentBrush") as Brush;
        if (rippleHost is null || accentBrush is null || rippleHost.ActualWidth <= 0 || rippleHost.ActualHeight <= 0)
        {
            return;
        }

        AnimateSurface(button, 0.985, 75);
        var point = args.GetPosition(rippleHost);
        var offsets = new[]
        {
            new Point(-8, -5),
            new Point(0, 0),
            new Point(8, 5)
        };

        for (var index = 0; index < offsets.Length; index++)
        {
            AddBubble(
                rippleHost,
                accentBrush,
                new Point(point.X + offsets[index].X, point.Y + offsets[index].Y),
                index);
        }
    }

    private static void ButtonPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (sender is Button button && SystemParameters.ClientAreaAnimation)
        {
            AnimateSurface(button, button.IsMouseOver ? 1.015 : 1, 120);
        }
    }

    private static void AnimateSurface(Button button, double scaleValue, int milliseconds)
    {
        if (button.Template.FindName("InteractionSurface", button) is not FrameworkElement surface)
        {
            return;
        }

        if (surface.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            surface.RenderTransform = scale;
            surface.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        Animate(scale, ScaleTransform.ScaleXProperty, scaleValue, milliseconds);
        Animate(scale, ScaleTransform.ScaleYProperty, scaleValue, milliseconds);
    }

    private static DropShadowEffect GetGlow(Button button)
    {
        if (button.Effect is DropShadowEffect existingGlow)
        {
            return existingGlow;
        }

        var accentColor = button.TryFindResource("AccentColor") is Color color
            ? color
            : Colors.LightSkyBlue;
        var glow = new DropShadowEffect
        {
            Color = accentColor,
            BlurRadius = 0,
            ShadowDepth = 0,
            Opacity = 0,
            RenderingBias = RenderingBias.Performance
        };
        button.Effect = glow;
        return glow;
    }

    private static void AddBubble(Canvas host, Brush accentBrush, Point center, int index)
    {
        const double diameter = 9;
        var bubble = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = accentBrush,
            Opacity = 0.32,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.25, 0.25)
        };
        Canvas.SetLeft(bubble, Math.Clamp(center.X - diameter / 2, 0, Math.Max(0, host.ActualWidth - diameter)));
        Canvas.SetTop(bubble, Math.Clamp(center.Y - diameter / 2, 0, Math.Max(0, host.ActualHeight - diameter)));
        host.Children.Add(bubble);

        var scale = (ScaleTransform)bubble.RenderTransform;
        var delay = TimeSpan.FromMilliseconds(index * 35);
        var duration = TimeSpan.FromMilliseconds(420);
        var grow = new DoubleAnimation(0.25, 2.7, duration)
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(0.32, 0, duration)
        {
            BeginTime = delay
        };
        fade.Completed += (_, _) => host.Children.Remove(bubble);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        bubble.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void Animate(Animatable target, DependencyProperty property, double value, int milliseconds)
    {
        target.BeginAnimation(
            property,
            new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static bool IsNearestButton(Button button, DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button sourceButton)
            {
                return ReferenceEquals(button, sourceButton);
            }

            source = GetVisualParent(source);
        }

        return true;
    }

    private static DependencyObject? GetVisualParent(DependencyObject element)
    {
        if (element is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(element);
        }

        return element is ContentElement contentElement
            ? ContentOperations.GetParent(contentElement)
            : LogicalTreeHelper.GetParent(element);
    }
}
