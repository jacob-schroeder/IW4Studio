using System.Numerics;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Picking;

public enum MapRenderPickCandidateLayer
{
    Textured,
    FallbackSolid,
    UntexturedSolid,
    Collision
}
