using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Shaders;

public sealed record MapRenderShaderSamplerDestination(
    int ArgumentIndex,
    string ArgumentType,
    ushort Destination,
    uint Argument,
    string ResourceIdentity,
    bool IsOperationallyResolved,
    float? X = null,
    float? Y = null,
    float? Z = null,
    float? W = null,
    string TextureTarget = "Texture2D",
    MapRenderCodeMatrixSemantic? CodeMatrixSemantic = null,
    MapRenderCodeMatrixTransform CodeMatrixTransform = MapRenderCodeMatrixTransform.None,
    int CodeMatrixRow = -1,
    ushort? CodeConstantSourceRow = null);
