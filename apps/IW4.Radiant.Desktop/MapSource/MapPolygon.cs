using System.Numerics;

namespace IW4.Radiant.Desktop.MapSource;

internal sealed record MapPolygon(MapFace Face, Vector3[] Vertices);
