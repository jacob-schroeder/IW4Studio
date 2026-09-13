using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class MapFace
{
    public Vector3 A { get; set; }
    public Vector3 B { get; set; }
    public Vector3 C { get; set; }
    public string Material { get; set; } = "";
    public string Projection { get; set; } = "64 64 0 0 0 0 lightmap_gray 16384 16384 0 0 0 0";
    public Vector3 Normal => Vector3.Normalize(Vector3.Cross(A - B, C - B));
    public MapFace Clone() => (MapFace)MemberwiseClone();
}
