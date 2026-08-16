using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.LightDef;

namespace IW4.Render.Scheduling.Lighting;

/// <summary>
/// Immutable managed projection of one PS3 runtime GfxLight row. Per-frame
/// spot-shadow allocation and lookup state are published separately.
/// </summary>
public sealed class MapRenderWorldEvent20SceneLight
{
    internal MapRenderWorldEvent20SceneLight(
        GfxLightType type,
        bool canUseShadowMap,
        byte exponent,
        Vector3 color,
        Vector3 direction,
        Vector3 origin,
        float radius,
        float cosHalfFovOuter,
        float cosHalfFovInner,
        string? definitionName,
        LightDefAsset? definition,
        int? attenuationImageWidth)
    {
        if (attenuationImageWidth is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(attenuationImageWidth));

        Type = type;
        CanUseShadowMap = canUseShadowMap;
        Exponent = exponent;
        Color = color;
        Direction = direction;
        Origin = origin;
        Radius = radius;
        CosHalfFovOuter = cosHalfFovOuter;
        CosHalfFovInner = cosHalfFovInner;
        DefinitionName = definitionName;
        Definition = definition;
        AttenuationImageWidth = attenuationImageWidth;
    }

    public GfxLightType Type { get; }

    public bool CanUseShadowMap { get; }

    public byte Exponent { get; }

    public Vector3 Color { get; }

    public Vector3 Direction { get; }

    public Vector3 Origin { get; }

    public float Radius { get; }

    public float CosHalfFovOuter { get; }

    public float CosHalfFovInner { get; }

    public string? DefinitionName { get; }

    public LightDefAsset? Definition { get; }

    /// <summary>
    /// Width of the exact canonical LightDef texture projected for source 13
    /// in this scene revision. Keeping it with the adapted light prevents
    /// allocated row 0x05 from consulting a different loader-time image.
    /// </summary>
    public int? AttenuationImageWidth { get; }
}
