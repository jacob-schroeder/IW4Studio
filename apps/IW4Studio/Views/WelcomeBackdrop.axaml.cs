using Avalonia;
using Avalonia.Controls;

namespace IW4.Studio.Desktop.Views;

public sealed partial class WelcomeBackdrop : Panel
{
    internal const double CompactWidth = 590;

    public WelcomeBackdrop() => InitializeComponent();

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        // The foreground determines the window layout. The artwork only fills its allotment.
        return default;
    }

    protected override Size ArrangeOverride(Size size)
    {
        bool compact = size.Width <= CompactWidth;
        double width = compact ? Math.Clamp(size.Width - 330, 0, 250) : Math.Min(0.4 * size.Width, 470);
        double height = compact ? 74 : size.Height * 0.55;
        var center = new Point(compact ? size.Width - width / 2 - 18 : size.Width * 0.79,
                               compact ? 44 : size.Height / 2);

        // Leave a gutter beyond the clip for the drifting grid.
        DraftingGrid.Arrange(new Rect(-8, -8, size.Width + 16, size.Height + 16));
        Profile.Arrange(new Rect(center.X - width / 2, center.Y - height / 2, width, height));
        ScanField.Arrange(new Rect(size));
        return size;
    }
}
