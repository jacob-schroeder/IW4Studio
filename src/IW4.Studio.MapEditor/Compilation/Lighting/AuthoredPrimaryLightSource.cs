using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// Primary-light kinds admitted by the initial greenfield lighting source.
/// Directional sun ownership and placement are deliberately outside M5-A.
/// </summary>
public enum AuthoredPrimaryLightKind : byte
{
    Spot = 2,
    Omni = 3
}

/// <summary>
/// Editor-owned semantic source for one greenfield non-sun primary light.
/// This is intentionally separate from <see cref="EditorPrimaryLight"/>,
/// whose values and ordinal are imported compiled-map state.
/// </summary>
public sealed class AuthoredPrimaryLightSource
{
    public AuthoredPrimaryLightSource(
        MapObjectId sourceId,
        AuthoredPrimaryLightKind kind,
        bool canUseShadowMap,
        byte exponent,
        MapVector3 color,
        MapVector3 direction,
        MapVector3 origin,
        float radius,
        float cosHalfFovOuter,
        float cosHalfFovInner,
        float cosHalfFovExpanded,
        float rotationLimit,
        float translationLimit,
        string? definitionName)
    {
        if (sourceId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceId),
                "An authored primary light requires a nonempty stable source ID.");
        }
        if (kind is not (
            AuthoredPrimaryLightKind.Spot or
            AuthoredPrimaryLightKind.Omni))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "M5-A admits only non-sun spot and omni primary lights.");
        }
        RequireFinite(color, nameof(color));
        RequireFinite(direction, nameof(direction));
        RequireFinite(origin, nameof(origin));
        if (color.X < 0 ||
            color.Y < 0 ||
            color.Z < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color),
                "Primary-light color channels cannot be negative.");
        }
        if (!float.IsFinite(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Primary-light radius must be finite and nonnegative.");
        }
        RequireFinite(cosHalfFovOuter, nameof(cosHalfFovOuter));
        RequireFinite(cosHalfFovInner, nameof(cosHalfFovInner));
        RequireFinite(cosHalfFovExpanded, nameof(cosHalfFovExpanded));
        RequireFinite(rotationLimit, nameof(rotationLimit));
        RequireFinite(translationLimit, nameof(translationLimit));
        if (kind == AuthoredPrimaryLightKind.Spot &&
            (cosHalfFovOuter <= 0 ||
             cosHalfFovOuter >= cosHalfFovInner ||
             cosHalfFovInner > 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cosHalfFovInner),
                "A spot light requires 0 < outer cone cosine < inner cone cosine <= 1.");
        }
        if (definitionName is not null &&
            string.IsNullOrWhiteSpace(definitionName))
        {
            throw new ArgumentException(
                "A primary-light definition name must be null or nonempty.",
                nameof(definitionName));
        }

        SourceId = sourceId;
        Kind = kind;
        CanUseShadowMap = canUseShadowMap;
        Exponent = exponent;
        Color = color;
        Direction = direction;
        Origin = origin;
        Radius = radius;
        CosHalfFovOuter = cosHalfFovOuter;
        CosHalfFovInner = cosHalfFovInner;
        CosHalfFovExpanded = cosHalfFovExpanded;
        RotationLimit = rotationLimit;
        TranslationLimit = translationLimit;
        DefinitionName = definitionName?.Trim();
    }

    public MapObjectId SourceId { get; }
    public AuthoredPrimaryLightKind Kind { get; }
    public bool CanUseShadowMap { get; }
    public byte Exponent { get; }
    public MapVector3 Color { get; }
    public MapVector3 Direction { get; }
    public MapVector3 Origin { get; }
    public float Radius { get; }
    public float CosHalfFovOuter { get; }
    public float CosHalfFovInner { get; }
    public float CosHalfFovExpanded { get; }
    public float RotationLimit { get; }
    public float TranslationLimit { get; }
    public string? DefinitionName { get; }

    internal ComPrimaryLightBuildData Compile() =>
        new(
            Type: (byte)Kind,
            CanUseShadowMap: CanUseShadowMap ? (byte)1 : (byte)0,
            Exponent,
            // This byte is serialized padding, not editor source.
            Unused: 0,
            Color: ToBuildVector(Color),
            Direction: ToBuildVector(Direction),
            Origin: ToBuildVector(Origin),
            Radius,
            CosHalfFovOuter,
            CosHalfFovInner,
            CosHalfFovExpanded,
            RotationLimit,
            TranslationLimit,
            DefName: DefinitionName);

    private static Float3BuildData ToBuildVector(MapVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static void RequireFinite(
        MapVector3 value,
        string parameterName)
    {
        if (!value.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Primary-light vectors must contain only finite components.");
        }
    }

    private static void RequireFinite(
        float value,
        string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Primary-light scalars must be finite.");
        }
    }
}
