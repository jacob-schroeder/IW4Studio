using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Shaders;

public sealed record MapRenderShaderFragmentExport(
    int ColorTarget,
    string Register,
    byte WrittenComponentMask,
    string WrittenComponents);
