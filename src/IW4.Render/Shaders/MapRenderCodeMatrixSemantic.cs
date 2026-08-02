using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Shaders;

public enum MapRenderCodeMatrixSemantic
{
    View,
    Projection,
    ViewProjection,
    ShadowLookup,
    WorldOutdoorLookup,
    World0,
    WorldView0,
    WorldViewProjection0,
    World1,
    WorldView1,
    WorldViewProjection1,
    World2,
    WorldView2,
    WorldViewProjection2
}
