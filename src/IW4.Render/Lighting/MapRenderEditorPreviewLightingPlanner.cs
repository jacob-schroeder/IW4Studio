using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Render.Materials;
using IW4.Render.Transforms;

namespace IW4.Render.Lighting;

/// <summary>
/// Produces a conservative editor-lighting policy from loaded ComWorld data.
/// Index zero remains the engine sentinel. Without exactly one usable type-1
/// light at a nonzero index, preview rendering stays ambient-only.
/// </summary>
public static class MapRenderEditorPreviewLightingPlanner
{
    public const byte DirectionalLightType = 1;
    public const float NeutralAmbientChannel = 0.25f;

    private const float MinimumDirectionLengthSquared = 1e-12f;

    public static Vector3 NeutralAmbientColor =>
        new(NeutralAmbientChannel, NeutralAmbientChannel, NeutralAmbientChannel);

    /// <summary>
    /// The editor-owned generic shader must leave preferred-slot emissive
    /// camera color unlit. Other passes remain eligible for the preview light
    /// plan.
    /// </summary>
    public static bool ShouldApplyGenericMaterialLighting(
        MapRenderMaterialPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        return pass.TechniqueSlot !=
            MapRenderEditorTechniquePolicy.PreferredEmissiveTechniqueSlot;
    }

    public static MapRenderEditorPreviewLightingPlan Create(
        ComWorldAsset? comWorld)
    {
        if (comWorld is null)
        {
            return AmbientOnly(
                MapRenderEditorPreviewLightingStatus
                    .AmbientOnlyComWorldUnavailable,
                "No ComWorld is available; Live Preview uses explicit neutral ambient only.");
        }

        IReadOnlyList<ComPrimaryLight> primaryLights =
            comWorld.PrimaryLights;
        if (comWorld.PrimaryLightCount < 0 ||
            comWorld.PrimaryLightCount != primaryLights.Count)
        {
            return AmbientOnly(
                MapRenderEditorPreviewLightingStatus
                    .AmbientOnlyPrimaryLightTableInvalid,
                $"ComWorld declares {comWorld.PrimaryLightCount} primary lights but retains {primaryLights.Count}; Live Preview uses explicit neutral ambient only.");
        }

        var candidates = new List<DirectionalSunCandidate>();
        for (int primaryLightIndex = 1;
             primaryLightIndex < primaryLights.Count;
             primaryLightIndex++)
        {
            ComPrimaryLight? light = primaryLights[primaryLightIndex];
            if (TryCreateCandidate(
                    primaryLightIndex,
                    light,
                    out DirectionalSunCandidate candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            return AmbientOnly(
                MapRenderEditorPreviewLightingStatus
                    .AmbientOnlyNoUsableDirectionalSun,
                "ComWorld contains no usable nonzero type-1 directional light; Live Preview uses explicit neutral ambient only.");
        }

        if (candidates.Count > 1)
        {
            string indices = string.Join(", ",
                candidates.Select(candidate => candidate.PrimaryLightIndex));
            return AmbientOnly(
                MapRenderEditorPreviewLightingStatus
                    .AmbientOnlyDirectionalSunAmbiguous,
                $"ComWorld contains multiple usable nonzero type-1 directional lights at indices [{indices}]; active stage ownership is unavailable, so Live Preview uses explicit neutral ambient only.");
        }

        DirectionalSunCandidate sun = candidates[0];
        return new MapRenderEditorPreviewLightingPlan(
            MapRenderEditorPreviewLightingStatus.AmbientAndDirectionalSun,
            NeutralAmbientColor,
            sun.PrimaryLightIndex,
            sun.CodeDirection,
            sun.Direction,
            sun.Color,
            $"Live Preview selected the sole usable nonzero type-1 directional light at primary-light index {sun.PrimaryLightIndex}.");
    }

    private static bool TryCreateCandidate(
        int primaryLightIndex,
        ComPrimaryLight? light,
        out DirectionalSunCandidate candidate)
    {
        candidate = default;
        if (primaryLightIndex <= 0 ||
            light is null ||
            light.Type != DirectionalLightType)
        {
            return false;
        }

        Vector3 gameDirection = new(
            light.Dir.X,
            light.Dir.Y,
            light.Dir.Z);
        Vector3 color = new(
            light.Color.X,
            light.Color.Y,
            light.Color.Z);
        if (!IsFinite(gameDirection) ||
            gameDirection.LengthSquared() <= MinimumDirectionLengthSquared ||
            !IsFiniteNonNegative(color))
        {
            return false;
        }

        Vector3 renderDirection =
            MapRenderCoordinateConverter.GameToRenderPosition(gameDirection);
        Vector3 codeDirection = renderDirection;
        float lengthSquared = renderDirection.LengthSquared();
        if (!float.IsFinite(lengthSquared) ||
            lengthSquared <= MinimumDirectionLengthSquared)
        {
            return false;
        }

        renderDirection /= MathF.Sqrt(lengthSquared);
        if (!IsFinite(renderDirection))
            return false;

        candidate = new DirectionalSunCandidate(
            primaryLightIndex,
            codeDirection,
            renderDirection,
            color);
        return true;
    }

    private static MapRenderEditorPreviewLightingPlan AmbientOnly(
        MapRenderEditorPreviewLightingStatus status,
        string reason) =>
        new(
            status,
            NeutralAmbientColor,
            directionalSunPrimaryLightIndex: null,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.Zero,
            reason);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFiniteNonNegative(Vector3 value) =>
        IsFinite(value) && value.X >= 0f && value.Y >= 0f && value.Z >= 0f;

    private readonly record struct DirectionalSunCandidate(
        int PrimaryLightIndex,
        Vector3 CodeDirection,
        Vector3 Direction,
        Vector3 Color);
}
