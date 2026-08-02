using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Render.Geometry;

internal readonly record struct PreparedWorldSurfaceVertex(
    Vector3 Position,
    Vector2 Uv0,
    Vector2 Uv1,
    Vector2 Uv2,
    Vector2 Uv3,
    Vector2 Uv4,
    Vector4 BlendWeights,
    Vector2 LightmapUv,
    bool LightmapUvReady,
    Vector3 Normal,
    bool UvSanitized,
    bool RsxInputsReady,
    string RsxInputBlocker)
{
    public Vector2 PrimaryUv => Uv0;
}
