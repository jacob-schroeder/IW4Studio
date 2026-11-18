using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Character;

public sealed class CharacterGraphGrid : Control
{
    private static readonly IBrush Dot = new SolidColorBrush(Color.FromArgb(67, 134, 146, 162));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const int spacing = 18;
        for (int y = spacing; y < Bounds.Height; y += spacing)
        {
            for (int x = spacing; x < Bounds.Width; x += spacing)
                context.DrawEllipse(Dot, null, new Point(x, y), 0.8, 0.8);
        }
    }
}
