using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed class BrushVertexSelection(MapBrush brush, Vector3 position)
{
    internal MapBrush Brush { get; } = brush;
    internal Vector3 Position { get; set; } = position;
}
