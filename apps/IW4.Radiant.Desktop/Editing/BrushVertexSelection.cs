using System.Numerics;
using IW4.Radiant.Desktop.MapSource;

namespace IW4.Radiant.Desktop.Editing;

internal sealed class BrushVertexSelection(MapBrush brush, Vector3 position)
{
    internal MapBrush Brush { get; } = brush;
    internal Vector3 Position { get; set; } = position;
}
