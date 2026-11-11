using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arkadia.App.Controls;

public sealed class RoundedClipBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        var radius = CornerRadius.TopLeft;
        Clip = new RectangleGeometry(
            new Rect(RenderSize),
            radius,
            radius);
    }
}
