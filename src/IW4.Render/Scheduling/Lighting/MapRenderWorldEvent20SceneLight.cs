using System.Numerics;
using IW4.Assets.Assets.LightDef;

namespace IW4.Render.Scheduling.Lighting;

/// <summary>
/// Immutable managed projection of one PS3 runtime GfxLight row. Dynamic
/// spotShadowIndex is deliberately absent from the current all-clear branch.
/// </summary>
public sealed class MapRenderWorldEvent20SceneLight
{
    internal MapRenderWorldEvent20SceneLight(
        byte type,
        byte canUseShadowMap,
        byte exponent,
        Vector3 color,
        Vector3 direction,
        Vector3 origin,
        float radius,
        float cosHalfFovOuter,
        float cosHalfFovInner,
        string? definitionName,
        LightDefAsset? definition)
    {
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
    }

    public byte Type { get; }

    public byte CanUseShadowMap { get; }

    public byte Exponent { get; }

    public Vector3 Color { get; }

    public Vector3 Direction { get; }

    public Vector3 Origin { get; }

    public float Radius { get; }

    public float CosHalfFovOuter { get; }

    public float CosHalfFovInner { get; }

    public string? DefinitionName { get; }

    public LightDefAsset? Definition { get; }
}
