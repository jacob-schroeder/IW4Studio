using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Transforms;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Renderer-facing semantic projection of one primary light. The source
/// ordinal is the stable bridge to the compiled scene-light table; it is not
/// exposed as editor object identity.
/// </summary>
public sealed record MapRenderLivePrimaryLight
{
    public MapRenderLivePrimaryLight(
        int sourceOrdinal,
        Vector3 color,
        byte exponent,
        float cosHalfFovInner,
        bool isVisible = true)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!IsFiniteNonNegative(color))
        {
            throw new ArgumentOutOfRangeException(
                nameof(color),
                "Live Preview light colors must be finite and nonnegative.");
        }
        if (!float.IsFinite(cosHalfFovInner))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cosHalfFovInner),
                "Live Preview inner-cone cosine must be finite.");
        }

        SourceOrdinal = sourceOrdinal;
        Color = color;
        Exponent = exponent;
        CosHalfFovInner = cosHalfFovInner;
        IsVisible = isVisible;
    }

    public int SourceOrdinal { get; }

    public Vector3 Color { get; }

    /// <summary>
    /// Exact ComPrimaryLight exponent byte consumed by the Event20 scene-light
    /// path. It is retained while the editor-only visibility is disabled.
    /// </summary>
    public byte Exponent { get; }

    /// <summary>
    /// Exact ComPrimaryLight inner-cone cosine. Reconciliation accepts authored
    /// changes only for loaded type-2 spot lights whose immutable outer cone
    /// still satisfies <c>0 &lt; outer &lt; inner &lt;= 1</c>.
    /// </summary>
    public float CosHalfFovInner { get; }

    /// <summary>
    /// Editor-only visibility. A hidden light retains its row and authored
    /// color identity but contributes zero color in Live Preview.
    /// </summary>
    public bool IsVisible { get; }

    private static bool IsFiniteNonNegative(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        value.X >= 0f &&
        value.Y >= 0f &&
        value.Z >= 0f;
}

/// <summary>
/// Renderer-facing translation of one Gfx static-model draw instance. The
/// source ordinal is the authoritative Gfx draw-instance/object index. The
/// origin remains in native game coordinates at this boundary.
/// </summary>
public sealed record MapRenderLiveStaticModelTranslation
{
    public MapRenderLiveStaticModelTranslation(
        int sourceOrdinal,
        Vector3 origin,
        bool isVisible = true)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!IsFinite(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                "Live Preview static-model origins must be finite.");
        }

        SourceOrdinal = sourceOrdinal;
        Origin = origin;
        IsVisible = isVisible;
    }

    public int SourceOrdinal { get; }

    public Vector3 Origin { get; }

    /// <summary>
    /// Editor-only visibility. Hidden instances retain their authoritative
    /// ordinal and transform so the loaded catalog remains one-to-one.
    /// </summary>
    public bool IsVisible { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

/// <summary>
/// Immutable editor-to-renderer update. It deliberately contains no map
/// document, FastFile asset, OpenGL handle, or mutable runtime state.
/// </summary>
public sealed class MapRenderLiveSceneProjection
{
    private readonly IReadOnlyList<MapRenderLivePrimaryLight> _primaryLights;
    private readonly IReadOnlyList<MapRenderLiveStaticModelTranslation>
        _staticModelTranslations;

    public MapRenderLiveSceneProjection(
        long revision,
        IEnumerable<MapRenderLivePrimaryLight> primaryLights,
        IEnumerable<MapRenderLiveStaticModelTranslation>?
            staticModelTranslations = null)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(primaryLights);

        MapRenderLivePrimaryLight[] supplied = primaryLights.ToArray();
        if (supplied.Any(value => value is null))
        {
            throw new ArgumentException(
                "Live Preview primary-light projections cannot contain null entries.",
                nameof(primaryLights));
        }
        MapRenderLivePrimaryLight[] copy = supplied
            .OrderBy(value => value.SourceOrdinal)
            .ToArray();

        var ordinals = new HashSet<int>();
        foreach (MapRenderLivePrimaryLight light in copy)
        {
            if (!ordinals.Add(light.SourceOrdinal))
            {
                throw new ArgumentException(
                    $"Live Preview contains duplicate primary-light source ordinal {light.SourceOrdinal}.",
                    nameof(primaryLights));
            }
        }

        HasStaticModelTranslationCatalog =
            staticModelTranslations is not null;
        MapRenderLiveStaticModelTranslation[] suppliedTranslations =
            staticModelTranslations?.ToArray() ?? [];
        if (suppliedTranslations.Any(value => value is null))
        {
            throw new ArgumentException(
                "Live Preview static-model translations cannot contain null entries.",
                nameof(staticModelTranslations));
        }
        MapRenderLiveStaticModelTranslation[] translationCopy =
            suppliedTranslations
                .OrderBy(value => value.SourceOrdinal)
                .ToArray();
        ordinals.Clear();
        foreach (MapRenderLiveStaticModelTranslation translation in
                 translationCopy)
        {
            if (!ordinals.Add(translation.SourceOrdinal))
            {
                throw new ArgumentException(
                    $"Live Preview contains duplicate static-model source ordinal {translation.SourceOrdinal}.",
                    nameof(staticModelTranslations));
            }
        }

        Revision = revision;
        _primaryLights =
            new ReadOnlyCollection<MapRenderLivePrimaryLight>(copy);
        _staticModelTranslations =
            new ReadOnlyCollection<MapRenderLiveStaticModelTranslation>(
                translationCopy);
        ContentIdentity = ComputeContentIdentity(
            copy,
            translationCopy,
            HasStaticModelTranslationCatalog);
    }

    public long Revision { get; }

    public IReadOnlyList<MapRenderLivePrimaryLight> PrimaryLights =>
        _primaryLights;

    /// <summary>
    /// True only when the producer supplied the complete Gfx static-model
    /// ordinal catalog. A missing catalog preserves compatibility with
    /// light-only producers and must not be interpreted as an empty map.
    /// </summary>
    public bool HasStaticModelTranslationCatalog { get; }

    public IReadOnlyList<MapRenderLiveStaticModelTranslation>
        StaticModelTranslations => _staticModelTranslations;

    /// <summary>
    /// Revision-independent content identity used to reject divergent updates
    /// that incorrectly claim the same editor revision.
    /// </summary>
    public string ContentIdentity { get; }

    private static string ComputeContentIdentity(
        IEnumerable<MapRenderLivePrimaryLight> lights,
        IEnumerable<MapRenderLiveStaticModelTranslation> translations,
        bool hasStaticModelTranslationCatalog)
    {
        var content = new StringBuilder("iw4/live-scene/v4");
        foreach (MapRenderLivePrimaryLight light in lights)
        {
            content
                .Append("|light:")
                .Append(light.SourceOrdinal.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(light.Color.X)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(light.Color.Y)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(light.Color.Z)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(light.Exponent.ToString(
                    "X2",
                    CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(
                        light.CosHalfFovInner)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(light.IsVisible ? '1' : '0');
        }
        content.Append(
            hasStaticModelTranslationCatalog
                ? "|static-catalog:present"
                : "|static-catalog:absent");
        foreach (MapRenderLiveStaticModelTranslation translation in
                 translations)
        {
            content
                .Append("|static:")
                .Append(translation.SourceOrdinal.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(
                        translation.Origin.X)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(
                        translation.Origin.Y)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(BitConverter.SingleToInt32Bits(
                        translation.Origin.Z)
                    .ToString("X8", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(translation.IsVisible ? '1' : '0');
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}

/// <summary>
/// Pure, fully validated translation plan. It updates placement rows and
/// scheduling bounds without touching backend resources, allowing an OpenGL
/// implementation to stage every CPU/GPU consumer before committing.
/// </summary>
internal sealed class MapRenderLiveStaticModelTranslationReconciliation
{
    private readonly IReadOnlyDictionary<int, StaticModelState>
        _stateBySourceOrdinal;

    internal MapRenderLiveStaticModelTranslationReconciliation(
        IReadOnlyDictionary<int, StaticModelState> stateBySourceOrdinal)
    {
        _stateBySourceOrdinal =
            stateBySourceOrdinal ??
            throw new ArgumentNullException(
                nameof(stateBySourceOrdinal));
    }

    internal int Count => _stateBySourceOrdinal.Count;

    internal bool IsVisible(int sourceOrdinal) =>
        RequireState(sourceOrdinal).IsVisible;

    internal MapRenderStaticModelInstance Reconcile(
        MapRenderStaticModelInstance instance)
    {
        if (!IsFinite(instance.TransformRow0) ||
            !IsFinite(instance.TransformRow1) ||
            !IsFinite(instance.TransformRow2))
        {
            throw new InvalidOperationException(
                $"Loaded static-model object index {instance.ObjectIndex} has a non-finite transform and cannot be reconciled.");
        }

        Vector3 origin = RequireState(instance.ObjectIndex).RenderOrigin;
        return instance with
        {
            TransformRow0 = instance.TransformRow0 with { W = origin.X },
            TransformRow1 = instance.TransformRow1 with { W = origin.Y },
            TransformRow2 = instance.TransformRow2 with { W = origin.Z }
        };
    }

    internal MapRenderStaticModelSchedulingInfo Reconcile(
        MapRenderStaticModelSchedulingInfo scheduling)
    {
        ArgumentNullException.ThrowIfNull(scheduling);
        Vector3 origin =
            RequireState(scheduling.ObjectIndex).RenderOrigin;
        if (!IsFinite(scheduling.Origin))
        {
            throw new InvalidOperationException(
                $"Loaded static-model object index {scheduling.ObjectIndex} has a non-finite scheduling origin and cannot be reconciled.");
        }

        Vector3 delta = origin - scheduling.Origin;
        if (!IsFinite(delta))
        {
            throw new InvalidOperationException(
                $"Static-model object index {scheduling.ObjectIndex} produces a non-finite scheduling translation.");
        }

        MapRenderBounds bounds = scheduling.Bounds;
        if (bounds.IsValid)
        {
            if (!IsFinite(bounds.Min) || !IsFinite(bounds.Max))
            {
                throw new InvalidOperationException(
                    $"Loaded static-model object index {scheduling.ObjectIndex} has non-finite scheduling bounds.");
            }

            Vector3 translatedMin = bounds.Min + delta;
            Vector3 translatedMax = bounds.Max + delta;
            if (!IsFinite(translatedMin) || !IsFinite(translatedMax))
            {
                throw new InvalidOperationException(
                    $"Static-model object index {scheduling.ObjectIndex} produces non-finite scheduling bounds.");
            }

            bounds = new MapRenderBounds(
                translatedMin,
                translatedMax);
        }

        return scheduling with
        {
            Origin = origin,
            Bounds = bounds
        };
    }

    private StaticModelState RequireState(int sourceOrdinal)
    {
        if (!_stateBySourceOrdinal.TryGetValue(
                sourceOrdinal,
                out StaticModelState state))
        {
            throw new InvalidOperationException(
                $"Loaded static-model object index {sourceOrdinal} is outside the reconciled Live Preview catalog.");
        }

        return state;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    internal readonly record struct StaticModelState(
        Vector3 RenderOrigin,
        bool IsVisible);
}

/// <summary>
/// Establishes exact cardinality and ordinal correspondence before any
/// renderer-owned static-model state may be changed.
/// </summary>
internal static class MapRenderLiveStaticModelTranslationReconciler
{
    internal static MapRenderLiveStaticModelTranslationReconciliation
        Reconcile(
            MapRenderLiveSceneProjection projection,
            int loadedSourceCount)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (loadedSourceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(loadedSourceCount));
        if (!projection.HasStaticModelTranslationCatalog)
        {
            throw new InvalidOperationException(
                "Live Preview did not receive an authoritative static-model translation catalog.");
        }
        if (projection.StaticModelTranslations.Count != loadedSourceCount)
        {
            throw new InvalidOperationException(
                $"Live Preview projected {projection.StaticModelTranslations.Count} static models, but the loaded Gfx scene contains {loadedSourceCount}. The update was rejected without changing renderer state.");
        }

        var states = new Dictionary<
            int,
            MapRenderLiveStaticModelTranslationReconciliation
                .StaticModelState>(
            loadedSourceCount);
        for (int sourceOrdinal = 0;
             sourceOrdinal < loadedSourceCount;
             sourceOrdinal++)
        {
            MapRenderLiveStaticModelTranslation translation =
                projection.StaticModelTranslations[sourceOrdinal];
            if (translation.SourceOrdinal != sourceOrdinal)
            {
                throw new InvalidOperationException(
                    $"Live Preview has no static-model translation for loaded Gfx source ordinal {sourceOrdinal}. The update was rejected without changing renderer state.");
            }

            states.Add(
                sourceOrdinal,
                new(
                    MapRenderCoordinateConverter.GameToRenderPosition(
                        translation.Origin),
                    translation.IsVisible));
        }

        return new MapRenderLiveStaticModelTranslationReconciliation(
            new ReadOnlyDictionary<
                int,
                MapRenderLiveStaticModelTranslationReconciliation
                    .StaticModelState>(states));
    }
}

internal sealed record MapRenderLiveSceneReconciliation(
    MapRenderEditorPreviewLightingPlan Lighting,
    MapRenderWorldEvent20SceneLightFrameInput? SceneLightFrame);

/// <summary>
/// Pure reconciliation policy shared by the live renderer and focused tests.
/// It updates only light values; all scene-resource identities remain owned
/// by the existing render scene and snapshot.
/// </summary>
internal static class MapRenderLiveSceneProjectionReconciler
{
    internal static MapRenderLiveSceneReconciliation Reconcile(
        MapRenderLiveSceneProjection projection,
        MapRenderEditorPreviewLightingPlan lighting,
        MapRenderWorldEvent20SceneLightFrameInput? sceneLightFrame)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(lighting);

        IReadOnlyDictionary<int, MapRenderLivePrimaryLight> byOrdinal =
            projection.PrimaryLights.ToDictionary(
                value => value.SourceOrdinal);
        MapRenderWorldEvent20SceneLightFrameInput? reconciledFrame =
            sceneLightFrame is null
                ? null
                : ReconcileSceneLightFrame(sceneLightFrame, byOrdinal);
        MapRenderEditorPreviewLightingPlan reconciledLighting =
            ReconcileDirectionalLighting(lighting, byOrdinal);
        return new MapRenderLiveSceneReconciliation(
            reconciledLighting,
            reconciledFrame);
    }

    private static MapRenderWorldEvent20SceneLightFrameInput
        ReconcileSceneLightFrame(
            MapRenderWorldEvent20SceneLightFrameInput frame,
            IReadOnlyDictionary<int, MapRenderLivePrimaryLight> byOrdinal)
    {
        if (byOrdinal.Count != frame.SceneLightCount)
        {
            throw new InvalidOperationException(
                $"Live Preview projected {byOrdinal.Count} primary lights, but the loaded render scene contains {frame.SceneLightCount}. The update was rejected without changing renderer state.");
        }

        var lights =
            new MapRenderWorldEvent20SceneLight[frame.SceneLightCount];
        for (int index = 0; index < lights.Length; index++)
        {
            if (!byOrdinal.TryGetValue(
                    index,
                    out MapRenderLivePrimaryLight? projected))
            {
                throw new InvalidOperationException(
                    $"Live Preview has no primary-light projection for loaded source ordinal {index}. The update was rejected without changing renderer state.");
            }

            MapRenderWorldEvent20SceneLight source =
                frame.GetSceneLight(index);
            ValidateSpotFalloff(source, projected, index);
            Vector3 effectiveColor = projected.IsVisible
                ? projected.Color
                : Vector3.Zero;
            lights[index] = new MapRenderWorldEvent20SceneLight(
                source.Type,
                source.CanUseShadowMap,
                projected.Exponent,
                effectiveColor,
                source.Direction,
                source.Origin,
                source.Radius,
                source.CosHalfFovOuter,
                projected.CosHalfFovInner,
                source.DefinitionName,
                source.Definition);
        }

        return new MapRenderWorldEvent20SceneLightFrameInput(
            lights,
            frame.EyeOffset,
            frame.DynamicInput,
            frame.AssetPoolRevision);
    }

    private static void ValidateSpotFalloff(
        MapRenderWorldEvent20SceneLight source,
        MapRenderLivePrimaryLight projected,
        int sourceOrdinal)
    {
        if (source.Type != 2)
        {
            if (BitConverter.SingleToInt32Bits(
                    source.CosHalfFovInner) !=
                BitConverter.SingleToInt32Bits(
                    projected.CosHalfFovInner))
            {
                throw new InvalidOperationException(
                    $"Live Preview attempted to change CosHalfFovInner for " +
                    $"non-spot primary-light source ordinal {sourceOrdinal}. " +
                    "The update was rejected without changing renderer state.");
            }

            return;
        }

        if (!float.IsFinite(source.CosHalfFovOuter) ||
            !float.IsFinite(source.CosHalfFovInner) ||
            !float.IsFinite(projected.CosHalfFovInner) ||
            source.CosHalfFovOuter <= 0f ||
            source.CosHalfFovInner <= source.CosHalfFovOuter ||
            source.CosHalfFovInner > 1f ||
            projected.CosHalfFovInner <= source.CosHalfFovOuter ||
            projected.CosHalfFovInner > 1f)
        {
            throw new InvalidOperationException(
                $"Live Preview type-2 primary-light source ordinal " +
                $"{sourceOrdinal} violates the required spot-falloff domain " +
                "0 < outer < inner <= 1. The update was rejected without " +
                "changing renderer state.");
        }
    }

    private static MapRenderEditorPreviewLightingPlan
        ReconcileDirectionalLighting(
            MapRenderEditorPreviewLightingPlan lighting,
            IReadOnlyDictionary<int, MapRenderLivePrimaryLight> byOrdinal)
    {
        if (lighting.DirectionalSunPrimaryLightIndex is not { } index)
            return lighting;
        if (!byOrdinal.TryGetValue(
                index,
                out MapRenderLivePrimaryLight? projected))
        {
            throw new InvalidOperationException(
                $"Live Preview has no projection for directional primary-light source ordinal {index}. The update was rejected without changing renderer state.");
        }

        return new MapRenderEditorPreviewLightingPlan(
            lighting.Status,
            lighting.AmbientColor,
            index,
            lighting.DirectionalSunCodeDirection,
            lighting.DirectionalSunDirection,
            projected.IsVisible
                ? projected.Color
                : Vector3.Zero,
            lighting.Reason);
    }
}
