using System.Numerics;

namespace IW4.Render.Lighting;

/// <summary>
/// Typed outcome of the editor-only lighting policy. These values describe a
/// practical preview and must never be treated as native PS3 renderer state.
/// </summary>
public enum MapRenderEditorPreviewLightingStatus
{
    AmbientOnlyComWorldUnavailable = 1,
    AmbientOnlyPrimaryLightTableInvalid = 2,
    AmbientOnlyNoUsableDirectionalSun = 3,
    AmbientOnlyDirectionalSunAmbiguous = 4,
    AmbientAndDirectionalSun = 5
}

/// <summary>
/// Immutable, side-effect-free lighting inputs for the generic editor
/// preview. Direction retains the authored <c>ComPrimaryLight.Dir</c> sign;
/// it is only converted into viewer axes and normalized here.
/// </summary>
public sealed class MapRenderEditorPreviewLightingPlan
{
    internal MapRenderEditorPreviewLightingPlan(
        MapRenderEditorPreviewLightingStatus status,
        Vector3 ambientColor,
        int? directionalSunPrimaryLightIndex,
        Vector3 directionalSunCodeDirection,
        Vector3 directionalSunDirection,
        Vector3 directionalSunColor,
        string reason)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!IsFiniteNonNegative(ambientColor) ||
            !IsFinite(directionalSunDirection) ||
            !IsFinite(directionalSunCodeDirection) ||
            !IsFiniteNonNegative(directionalSunColor))
        {
            throw new ArgumentException(
                "Editor-preview lighting vectors must be finite and colors cannot be negative.");
        }

        bool hasDirectionalSun = directionalSunPrimaryLightIndex.HasValue;
        if (ambientColor == Vector3.Zero)
        {
            throw new ArgumentException(
                "An enabled preview-lighting plan requires a nonzero explicit ambient policy.");
        }

        if (hasDirectionalSun)
        {
            if (status != MapRenderEditorPreviewLightingStatus
                    .AmbientAndDirectionalSun ||
                directionalSunPrimaryLightIndex!.Value <= 0 ||
                directionalSunCodeDirection.LengthSquared() <= 0f ||
                MathF.Abs(directionalSunDirection.LengthSquared() - 1f) >
                    0.0001f)
            {
                throw new ArgumentException(
                    "A directional preview sun requires one nonzero primary-light index and a normalized direction.");
            }
        }
        else if (status == MapRenderEditorPreviewLightingStatus
                     .AmbientAndDirectionalSun ||
                 directionalSunDirection != Vector3.Zero ||
                 directionalSunCodeDirection != Vector3.Zero ||
                 directionalSunColor != Vector3.Zero)
        {
            throw new ArgumentException(
                "An ambient-only preview-lighting plan cannot retain directional-light inputs.");
        }

        Status = status;
        AmbientColor = ambientColor;
        DirectionalSunPrimaryLightIndex = directionalSunPrimaryLightIndex;
        DirectionalSunCodeDirection = directionalSunCodeDirection;
        DirectionalSunDirection = directionalSunDirection;
        DirectionalSunColor = directionalSunColor;
        Reason = reason;
    }

    public MapRenderEditorPreviewLightingStatus Status { get; }

    public Vector3 AmbientColor { get; }

    public int? DirectionalSunPrimaryLightIndex { get; }

    public bool HasDirectionalSun => DirectionalSunPrimaryLightIndex.HasValue;

    /// <summary>
    /// Authored <c>ComPrimaryLight.Dir</c> transformed into viewer axes without
    /// normalization. The PS3 scene-light writer copies this vector verbatim
    /// into direct code row 0x00; the normalized DirectionalSunDirection is
    /// retained separately for the generic editor shader.
    /// </summary>
    public Vector3 DirectionalSunCodeDirection { get; }

    /// <summary>
    /// Normalized authored light direction in viewer/render coordinates. Its
    /// sign is unchanged; a later shader policy must explicitly choose whether
    /// it needs the ray direction or its inverse.
    /// </summary>
    public Vector3 DirectionalSunDirection { get; }

    public Vector3 DirectionalSunColor { get; }

    public string Reason { get; }

    public bool IsAmbientOnly => !HasDirectionalSun;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFiniteNonNegative(Vector3 value) =>
        IsFinite(value) && value.X >= 0f && value.Y >= 0f && value.Z >= 0f;
}
