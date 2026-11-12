using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Arkadia.Core.Services;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace Arkadia.App.Controls;

public sealed class HomeTileGridPanel : WpfPanel
{
    private const int MaximumColumnCount = 3;
    private const double TileWidth = 260;
    private const double TileHeight = 260;
    private const double Gap = 12;

    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached(
            "ColumnSpan",
            typeof(int),
            typeof(HomeTileGridPanel),
            new FrameworkPropertyMetadata(
                1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure
                | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.RegisterAttached(
            "RowSpan",
            typeof(int),
            typeof(HomeTileGridPanel),
            new FrameworkPropertyMetadata(
                1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure
                | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetColumnSpan(DependencyObject element) =>
        (int)element.GetValue(ColumnSpanProperty);

    public static void SetColumnSpan(DependencyObject element, int value) =>
        element.SetValue(ColumnSpanProperty, value);

    public static int GetRowSpan(DependencyObject element) =>
        (int)element.GetValue(RowSpanProperty);

    public static void SetRowSpan(DependencyObject element, int value) =>
        element.SetValue(RowSpanProperty, value);

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var width = ResolveWidth(availableSize.Width);
        var columnCount = ResolveColumnCount(width);
        var placements = CreatePlacements(columnCount);
        var cellWidth = Math.Max(0, (width - Gap * (columnCount - 1)) / columnCount);

        foreach (var placement in placements)
        {
            var child = Children[ParseIndex(placement.Id)];
            child.Measure(new WpfSize(
                cellWidth * placement.ColumnSpan + Gap * (placement.ColumnSpan - 1),
                TileHeight * placement.RowSpan + Gap * (placement.RowSpan - 1)));
        }

        var rowCount = placements.Count == 0
            ? 0
            : placements.Max(item => item.Row + item.RowSpan);
        var height = rowCount * (TileHeight + Gap);
        return new WpfSize(width, height);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var width = ResolveWidth(finalSize.Width);
        var columnCount = ResolveColumnCount(width);
        var cellWidth = Math.Max(0, (width - Gap * (columnCount - 1)) / columnCount);

        foreach (var placement in CreatePlacements(columnCount))
        {
            var child = Children[ParseIndex(placement.Id)];
            child.Arrange(new Rect(
                placement.Column * (cellWidth + Gap),
                placement.Row * (TileHeight + Gap),
                cellWidth * placement.ColumnSpan + Gap * (placement.ColumnSpan - 1),
                TileHeight * placement.RowSpan + Gap * (placement.RowSpan - 1)));
        }

        return finalSize;
    }

    private IReadOnlyList<HomeTileGridPosition> CreatePlacements(int columnCount)
    {
        var items = Children
            .Cast<UIElement>()
            .Select((child, index) => new HomeTileGridItem(
                index.ToString(CultureInfo.InvariantCulture),
                Math.Min(GetColumnSpan(child), columnCount),
                GetRowSpan(child)))
            .ToArray();
        return HomeTileGridLayout.Arrange(items, columnCount);
    }

    private static int ParseIndex(string id) =>
        int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture);

    private static int ResolveColumnCount(double width)
    {
        var fittingColumns = (int)Math.Floor((width + Gap) / (TileWidth + Gap));
        return Math.Clamp(fittingColumns, 1, MaximumColumnCount);
    }

    private static double ResolveWidth(double width) =>
        double.IsNaN(width) || double.IsInfinity(width)
            ? MaximumColumnCount * TileWidth + (MaximumColumnCount - 1) * Gap
            : Math.Max(0, width);
}
