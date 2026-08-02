using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed record GfxPortalVertex(float X, float Y, float Z)
{
    public const int SerializedSize = 0x0C;
}
