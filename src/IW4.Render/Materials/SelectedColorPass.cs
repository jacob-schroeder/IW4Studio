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

namespace IW4.Render.Materials;

internal sealed record SelectedColorPass(
    MaterialTextureDef Texture,
    GfxImageAsset Image,
    MapRenderMaterialPass Pass,
    MapRenderState State,
    int UnresolvedCodeSamplerCount,
    byte TexCoordSource,
    bool TexCoordSourceIsEngineRouted,
    bool AuthoredProgramExecutable);
