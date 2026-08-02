using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Materials;

public enum MapRenderUvBaseMode
{
    Engine,
    Stream0BaseVertexGfxStride,
    Stream0BaseVertexSourceStride,
    Stream0GlobalIndexSourceStride,
    Stream0LocalIndexOnly,
    Stream1VertexLayerData,
    Stream1ZeroBase
}
