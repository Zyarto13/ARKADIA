using System.Windows;
using System.Windows.Input;
using Arkadia.App.ViewModels;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace Arkadia.App.Controls;

public static class HomeTileDragDropBehavior
{
    private static readonly DependencyProperty DragStartPointProperty =
        DependencyProperty.RegisterAttached(
            "DragStartPoint",
            typeof(WpfPoint),
            typeof(HomeTileDragDropBehavior));

    public static readonly DependencyProperty IsDragDropEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDragDropEnabled",
            typeof(bool),
            typeof(HomeTileDragDropBehavior),
            new PropertyMetadata(false, OnIsDragDropEnabledChanged));

    public static readonly DependencyProperty ReorderCommandProperty =
        DependencyProperty.RegisterAttached(
            "ReorderCommand",
            typeof(ICommand),
            typeof(HomeTileDragDropBehavior));

    public static bool GetIsDragDropEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsDragDropEnabledProperty);

    public static void SetIsDragDropEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsDragDropEnabledProperty, value);

    public static ICommand? GetReorderCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ReorderCommandProperty);

    public static void SetReorderCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ReorderCommandProperty, value);

    private static void OnIsDragDropEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element)
        {
            return;
        }

        if (args.NewValue is true)
        {
            element.AllowDrop = true;
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            element.PreviewMouseMove += OnPreviewMouseMove;
            element.PreviewDragOver += OnPreviewDragOver;
            element.PreviewDrop += OnPreviewDrop;
            return;
        }

        element.AllowDrop = false;
        element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        element.PreviewMouseMove -= OnPreviewMouseMove;
        element.PreviewDragOver -= OnPreviewDragOver;
        element.PreviewDrop -= OnPreviewDrop;
        element.ClearValue(DragStartPointProperty);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is FrameworkElement element)
        {
            element.SetValue(DragStartPointProperty, args.GetPosition(element));
        }
    }

    private static void OnPreviewMouseMove(object sender, WpfMouseEventArgs args)
    {
        if (sender is not FrameworkElement element
            || args.LeftButton != MouseButtonState.Pressed
            || element.DataContext is not HomeTileViewModel tile)
        {
            return;
        }

        var start = (WpfPoint)element.GetValue(DragStartPointProperty);
        var current = args.GetPosition(element);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(element, tile.LayoutId, WpfDragDropEffects.Move);
    }

    private static void OnPreviewDragOver(object sender, WpfDragEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: HomeTileViewModel }
            && args.Data.GetDataPresent(typeof(string)))
        {
            args.Effects = WpfDragDropEffects.Move;
            args.Handled = true;
        }
    }

    private static void OnPreviewDrop(object sender, WpfDragEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: HomeTileViewModel target }
            || args.Data.GetData(typeof(string)) is not string sourceId
            || GetReorderCommand((DependencyObject)sender) is not { } command)
        {
            return;
        }

        var request = new HomeTileDropRequest(sourceId, target.LayoutId);
        if (command.CanExecute(request))
        {
            command.Execute(request);
            args.Handled = true;
        }
    }
}
