using System.Collections.ObjectModel;
using System.Globalization;
using IW4.Studio.MapEditor.Editing.Entities;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Editing.Objects;

public enum MapObjectKind
{
    WorldSurface,
    RenderStaticModel,
    CollisionStaticModel,
    CollisionBrush,
    CollisionTriangle,
    Entity,
    PrimaryLight,
    FxGlass,
    GameplayGlass,
    Cell,
    Portal
}

public enum EditorObjectVisibility
{
    Visible,
    Hidden
}

public sealed record EditorObjectProperty(
    string Name,
    string Value,
    MapValueProvenance Provenance,
    SourceBindingId SourceBinding);

public abstract class EditorMapObject
{
    private readonly IReadOnlyList<SourceBindingId> _sourceBindings;
    private EditorObjectVisibility _visibility;

    protected EditorMapObject(
        MapObjectId id,
        MapObjectKind kind,
        string displayName,
        IEnumerable<SourceBindingId> sourceBindings)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(sourceBindings);

        Id = id;
        Kind = kind;
        DisplayName = displayName;
        _visibility = EditorObjectVisibility.Visible;
        _sourceBindings = new ReadOnlyCollection<SourceBindingId>(
            sourceBindings.Distinct().ToArray());
        if (_sourceBindings.Count == 0 ||
            _sourceBindings.Any(value => value.Value == Guid.Empty))
            throw new ArgumentException(
                "Semantic map objects require non-empty provenance bindings.",
                nameof(sourceBindings));
    }

    public MapObjectId Id { get; }

    public MapObjectKind Kind { get; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// Editor-only visibility. It is never interpreted as compiled render or
    /// collision suppression.
    /// </summary>
    public EditorObjectVisibility Visibility => _visibility;

    public bool IsVisible => _visibility == EditorObjectVisibility.Visible;

    public IReadOnlyList<SourceBindingId> SourceBindings => _sourceBindings;

    public abstract IReadOnlyList<EditorObjectProperty> Properties { get; }

    protected static EditorObjectProperty Property<T>(
        string name,
        MapValue<T> value) =>
        new(
            name,
            Format(value.Value),
            value.Provenance,
            value.SourceBinding);

    private static string Format<T>(T value) =>
        value switch
        {
            null => "(null)",
            float scalar => scalar.ToString("0.###", CultureInfo.InvariantCulture),
            double scalar => scalar.ToString("0.###", CultureInfo.InvariantCulture),
            bool flag => flag ? "Yes" : "No",
            _ => value.ToString() ?? string.Empty
        };

    internal void SetEditorVisibility(EditorObjectVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));

        _visibility = visibility;
    }

    internal void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }
}

public sealed class EditorWorldSurface : EditorMapObject
{
    private readonly IReadOnlyList<EditorObjectProperty> _properties;

    public EditorWorldSurface(
        MapObjectId id,
        MapValue<int> sourceOrdinal,
        MapValue<int> vertexCount,
        MapValue<int> triangleCount,
        MapValue<string?> materialName,
        MapValue<MapBounds?> bounds,
        MapValue<byte> lightmapIndex,
        MapValue<byte> reflectionProbeIndex,
        MapValue<byte> primaryLightIndex)
        : base(
            id,
            MapObjectKind.WorldSurface,
            $"World surface {sourceOrdinal.Value}",
            Bindings(
                sourceOrdinal,
                vertexCount,
                triangleCount,
                materialName,
                bounds,
                lightmapIndex,
                reflectionProbeIndex,
                primaryLightIndex))
    {
        SourceOrdinal = sourceOrdinal;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        MaterialName = materialName;
        Bounds = bounds;
        LightmapIndex = lightmapIndex;
        ReflectionProbeIndex = reflectionProbeIndex;
        PrimaryLightIndex = primaryLightIndex;
        _properties = Array.AsReadOnly(
        [
            Property("Source ordinal", sourceOrdinal),
            Property("Vertices", vertexCount),
            Property("Triangles", triangleCount),
            Property("Material", materialName),
            Property("Bounds", bounds),
            Property("Lightmap index", lightmapIndex),
            Property("Reflection probe", reflectionProbeIndex),
            Property("Primary light", primaryLightIndex)
        ]);
    }

    public MapValue<int> SourceOrdinal { get; }
    public MapValue<int> VertexCount { get; }
    public MapValue<int> TriangleCount { get; }
    public MapValue<string?> MaterialName { get; }
    public MapValue<MapBounds?> Bounds { get; }
    public MapValue<byte> LightmapIndex { get; }
    public MapValue<byte> ReflectionProbeIndex { get; }
    public MapValue<byte> PrimaryLightIndex { get; }
    public override IReadOnlyList<EditorObjectProperty> Properties => _properties;

    private static SourceBindingId[] Bindings(
        MapValue<int> ordinal,
        MapValue<int> vertices,
        MapValue<int> triangles,
        MapValue<string?> material,
        MapValue<MapBounds?> bounds,
        MapValue<byte> lightmap,
        MapValue<byte> probe,
        MapValue<byte> light) =>
        [
            ordinal.SourceBinding,
            vertices.SourceBinding,
            triangles.SourceBinding,
            material.SourceBinding,
            bounds.SourceBinding,
            lightmap.SourceBinding,
            probe.SourceBinding,
            light.SourceBinding
        ];
}

public enum StaticModelRepresentation
{
    Render,
    Collision
}

public enum StaticModelCompiledDisposition
{
    BaselinePresent,
    AuthoredPending,
    Suppressed,
    Removed
}

public readonly record struct StaticModelDuplicationOperationId
{
    public StaticModelDuplicationOperationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public static StaticModelDuplicationOperationId New() =>
        new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public enum StaticModelLineageKind
{
    Imported,
    AuthoredDuplicate
}

public abstract record StaticModelLineage(StaticModelLineageKind Kind);

public sealed record ImportedStaticModelLineage(int SourceOrdinal)
    : StaticModelLineage(StaticModelLineageKind.Imported)
{
    public int SourceOrdinal { get; } =
        SourceOrdinal >= 0
            ? SourceOrdinal
            : throw new ArgumentOutOfRangeException(nameof(SourceOrdinal));
}

/// <summary>
/// Shared semantic authority for the two authored rows created by one
/// constrained static-model duplication operation. Template bindings are
/// retained only as copy/journal authority; authored row values receive
/// distinct local binding identities.
/// </summary>
public sealed class AuthoredStaticModelDuplicatePairState
{
    private readonly IReadOnlyList<SourceBindingId> _templateRecordBindings;

    public AuthoredStaticModelDuplicatePairState(
        StaticModelDuplicationOperationId operationId,
        MapObjectId renderObjectId,
        MapObjectId collisionObjectId,
        MapObjectId renderTemplateObjectId,
        MapObjectId collisionTemplateObjectId,
        int gfxTemplateOrdinal,
        int clipTemplateOrdinal,
        int gfxProjectedOrdinal,
        int clipProjectedOrdinal,
        MapVector3 destination,
        MapAssetKind collisionAssetKind,
        string bundleBaselineDigest,
        SourceBindingId gfxTemplateRecordBinding,
        SourceBindingId clipTemplateRecordBinding)
    {
        if (operationId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(operationId));
        RequireObjectId(renderObjectId, nameof(renderObjectId));
        RequireObjectId(collisionObjectId, nameof(collisionObjectId));
        RequireObjectId(
            renderTemplateObjectId,
            nameof(renderTemplateObjectId));
        RequireObjectId(
            collisionTemplateObjectId,
            nameof(collisionTemplateObjectId));
        RequireOrdinal(gfxTemplateOrdinal, nameof(gfxTemplateOrdinal));
        RequireOrdinal(clipTemplateOrdinal, nameof(clipTemplateOrdinal));
        RequireOrdinal(gfxProjectedOrdinal, nameof(gfxProjectedOrdinal));
        RequireOrdinal(clipProjectedOrdinal, nameof(clipProjectedOrdinal));
        if (!destination.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(destination));
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleBaselineDigest);
        RequireBinding(
            gfxTemplateRecordBinding,
            nameof(gfxTemplateRecordBinding));
        RequireBinding(
            clipTemplateRecordBinding,
            nameof(clipTemplateRecordBinding));
        if (renderObjectId == collisionObjectId ||
            renderTemplateObjectId == collisionTemplateObjectId ||
            gfxTemplateRecordBinding == clipTemplateRecordBinding)
        {
            throw new ArgumentException(
                "Authored static-model pair identities and template bindings " +
                "must be distinct.");
        }

        OperationId = operationId;
        RenderObjectId = renderObjectId;
        CollisionObjectId = collisionObjectId;
        RenderTemplateObjectId = renderTemplateObjectId;
        CollisionTemplateObjectId = collisionTemplateObjectId;
        GfxTemplateOrdinal = gfxTemplateOrdinal;
        ClipTemplateOrdinal = clipTemplateOrdinal;
        GfxProjectedOrdinal = gfxProjectedOrdinal;
        ClipProjectedOrdinal = clipProjectedOrdinal;
        Destination = destination;
        CollisionAssetKind = collisionAssetKind;
        BundleBaselineDigest = bundleBaselineDigest;
        GfxTemplateRecordBinding = gfxTemplateRecordBinding;
        ClipTemplateRecordBinding = clipTemplateRecordBinding;
        _templateRecordBindings = Array.AsReadOnly(
        [
            gfxTemplateRecordBinding,
            clipTemplateRecordBinding
        ]);
    }

    public StaticModelDuplicationOperationId OperationId { get; }
    public MapObjectId RenderObjectId { get; }
    public MapObjectId CollisionObjectId { get; }
    public MapObjectId RenderTemplateObjectId { get; }
    public MapObjectId CollisionTemplateObjectId { get; }
    public int GfxTemplateOrdinal { get; }
    public int ClipTemplateOrdinal { get; }
    public int GfxProjectedOrdinal { get; }
    public int ClipProjectedOrdinal { get; }
    public MapVector3 Destination { get; }
    public MapAssetKind CollisionAssetKind { get; }
    public string BundleBaselineDigest { get; }
    public SourceBindingId GfxTemplateRecordBinding { get; }
    public SourceBindingId ClipTemplateRecordBinding { get; }
    public IReadOnlyList<SourceBindingId> TemplateRecordBindings =>
        _templateRecordBindings;

    public MapObjectId ObjectId(StaticModelRepresentation representation) =>
        representation switch
        {
            StaticModelRepresentation.Render => RenderObjectId,
            StaticModelRepresentation.Collision => CollisionObjectId,
            _ => throw new ArgumentOutOfRangeException(nameof(representation))
        };

    public int TemplateOrdinal(
        StaticModelRepresentation representation) =>
        representation switch
        {
            StaticModelRepresentation.Render => GfxTemplateOrdinal,
            StaticModelRepresentation.Collision => ClipTemplateOrdinal,
            _ => throw new ArgumentOutOfRangeException(nameof(representation))
        };

    public int ProjectedOrdinal(
        StaticModelRepresentation representation) =>
        representation switch
        {
            StaticModelRepresentation.Render => GfxProjectedOrdinal,
            StaticModelRepresentation.Collision => ClipProjectedOrdinal,
            _ => throw new ArgumentOutOfRangeException(nameof(representation))
        };

    private static void RequireObjectId(
        MapObjectId value,
        string parameterName)
    {
        if (value.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void RequireBinding(
        SourceBindingId value,
        string parameterName)
    {
        if (value.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void RequireOrdinal(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed record AuthoredDuplicateStaticModelLineage(
    AuthoredStaticModelDuplicatePairState Pair)
    : StaticModelLineage(StaticModelLineageKind.AuthoredDuplicate)
{
    public AuthoredStaticModelDuplicatePairState Pair { get; } =
        Pair ?? throw new ArgumentNullException(nameof(Pair));
}

/// <summary>
/// Exact compiled fields participating in supported static-model operations.
/// Render bounds occupy one serialized field, while collision midpoint and
/// half-size occupy separate fields.
/// </summary>
public sealed class StaticModelCompiledFieldBindings
{
    private readonly IReadOnlyList<SourceBindingId> _allBindings;

    private StaticModelCompiledFieldBindings(
        StaticModelRepresentation representation,
        SourceBindingId originBinding,
        SourceBindingId boundsMidpointBinding,
        SourceBindingId boundsHalfSizeBinding,
        SourceBindingId? cullDistanceBinding,
        SourceBindingId? flagsBinding,
        SourceBindingId? lightingOriginBinding,
        bool hasCompleteSuppressionBindings,
        bool hasCompleteTranslationBindings)
    {
        EnsureBinding(originBinding, nameof(originBinding));
        EnsureBinding(boundsMidpointBinding, nameof(boundsMidpointBinding));
        EnsureBinding(boundsHalfSizeBinding, nameof(boundsHalfSizeBinding));
        EnsureOptionalBinding(
            cullDistanceBinding,
            nameof(cullDistanceBinding));
        EnsureOptionalBinding(flagsBinding, nameof(flagsBinding));
        EnsureOptionalBinding(
            lightingOriginBinding,
            nameof(lightingOriginBinding));

        Representation = representation;
        OriginBinding = originBinding;
        BoundsMidpointBinding = boundsMidpointBinding;
        BoundsHalfSizeBinding = boundsHalfSizeBinding;
        CullDistanceBinding = cullDistanceBinding;
        FlagsBinding = flagsBinding;
        LightingOriginBinding = lightingOriginBinding;
        HasCompleteSuppressionBindings = hasCompleteSuppressionBindings;
        HasCompleteTranslationBindings =
            hasCompleteTranslationBindings;
        _allBindings = Array.AsReadOnly(
            new SourceBindingId?[]
            {
                originBinding,
                cullDistanceBinding,
                flagsBinding,
                boundsMidpointBinding,
                boundsHalfSizeBinding,
                lightingOriginBinding
            }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray());
    }

    public StaticModelRepresentation Representation { get; }

    public SourceBindingId OriginBinding { get; }

    public SourceBindingId BoundsMidpointBinding { get; }

    public SourceBindingId BoundsHalfSizeBinding { get; }

    public SourceBindingId? CullDistanceBinding { get; }

    public SourceBindingId? FlagsBinding { get; }

    public SourceBindingId? LightingOriginBinding { get; }

    /// <summary>
    /// True only when every field mutated by the representation's compiled
    /// tombstone operation has an exact source binding.
    /// </summary>
    public bool HasCompleteSuppressionBindings { get; }

    /// <summary>
    /// True only when every directly translated row field has an exact source
    /// binding. Derived Clip tree envelopes are authorized separately by the
    /// compilation-layer preservation evaluator.
    /// </summary>
    public bool HasCompleteTranslationBindings { get; }

    public IReadOnlyList<SourceBindingId> AllBindings => _allBindings;

    public static StaticModelCompiledFieldBindings ForRender(
        SourceBindingId placementOriginBinding,
        SourceBindingId cullDistanceBinding,
        SourceBindingId flagsBinding,
        SourceBindingId instanceBoundsBinding,
        SourceBindingId lightingOriginBinding) =>
        new(
            StaticModelRepresentation.Render,
            placementOriginBinding,
            instanceBoundsBinding,
            instanceBoundsBinding,
            cullDistanceBinding,
            flagsBinding,
            lightingOriginBinding,
            hasCompleteSuppressionBindings: true,
            hasCompleteTranslationBindings: true);

    public static StaticModelCompiledFieldBindings ForCollision(
        SourceBindingId originBinding,
        SourceBindingId boundsMidpointBinding,
        SourceBindingId boundsHalfSizeBinding) =>
        new(
            StaticModelRepresentation.Collision,
            originBinding,
            boundsMidpointBinding,
            boundsHalfSizeBinding,
            cullDistanceBinding: null,
            flagsBinding: null,
            lightingOriginBinding: null,
            hasCompleteSuppressionBindings: true,
            hasCompleteTranslationBindings: true);

    internal static StaticModelCompiledFieldBindings Legacy(
        StaticModelRepresentation representation,
        SourceBindingId originBinding,
        SourceBindingId boundsBinding) =>
        new(
            representation,
            originBinding,
            boundsBinding,
            boundsBinding,
            cullDistanceBinding: null,
            flagsBinding: null,
            lightingOriginBinding: null,
            hasCompleteSuppressionBindings: false,
            hasCompleteTranslationBindings: false);

    private static void EnsureBinding(
        SourceBindingId binding,
        string parameterName)
    {
        if (binding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void EnsureOptionalBinding(
        SourceBindingId? binding,
        string parameterName)
    {
        if (binding is SourceBindingId value &&
            value.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

public sealed class EditorStaticModel : EditorMapObject
{
    private readonly MapValue<MapVector3> _importedOrigin;
    private readonly MapValue<MapBounds?> _importedBounds;
    private MapValue<MapVector3> _origin;
    private MapValue<MapBounds?> _bounds;
    private StaticModelCompiledDisposition _compiledDisposition;
    private EditorStaticModelTransformState _transform;
    private IReadOnlyList<EditorObjectProperty> _properties;

    public EditorStaticModel(
        MapObjectId id,
        StaticModelRepresentation representation,
        MapValue<int> sourceOrdinal,
        MapValue<string?> modelName,
        MapValue<MapVector3> origin,
        MapValue<float?> scale,
        MapValue<MapBounds?> bounds,
        StaticModelCompiledFieldBindings? compiledFieldBindings = null,
        StaticModelCompiledDisposition compiledDisposition =
            StaticModelCompiledDisposition.BaselinePresent)
        : this(
            id,
            representation,
            sourceOrdinal,
            modelName,
            origin,
            scale,
            bounds,
            compiledFieldBindings,
            compiledDisposition,
            lineage: null)
    {
    }

    private EditorStaticModel(
        MapObjectId id,
        StaticModelRepresentation representation,
        MapValue<int> sourceOrdinal,
        MapValue<string?> modelName,
        MapValue<MapVector3> origin,
        MapValue<float?> scale,
        MapValue<MapBounds?> bounds,
        StaticModelCompiledFieldBindings? compiledFieldBindings,
        StaticModelCompiledDisposition compiledDisposition,
        StaticModelLineage? lineage)
        : base(
            id,
            representation == StaticModelRepresentation.Render
                ? MapObjectKind.RenderStaticModel
                : MapObjectKind.CollisionStaticModel,
            lineage is AuthoredDuplicateStaticModelLineage
                ? $"Authored {representation.ToString().ToLowerInvariant()} " +
                  $"static model {sourceOrdinal.Value}"
                : $"{representation} static model {sourceOrdinal.Value}",
            SourceBindingsFor(
                sourceOrdinal,
                modelName,
                origin,
                scale,
                bounds,
                compiledFieldBindings))
    {
        if (!Enum.IsDefined(representation))
            throw new ArgumentOutOfRangeException(nameof(representation));
        if (!Enum.IsDefined(compiledDisposition))
            throw new ArgumentOutOfRangeException(nameof(compiledDisposition));

        StaticModelCompiledFieldBindings normalizedBindings =
            compiledFieldBindings ??
            StaticModelCompiledFieldBindings.Legacy(
                representation,
                origin.SourceBinding,
                bounds.SourceBinding);
        if (normalizedBindings.Representation != representation)
        {
            throw new ArgumentException(
                "Static-model compiled field bindings must match the " +
                "model representation.",
                nameof(compiledFieldBindings));
        }
        StaticModelLineage normalizedLineage =
            lineage ??
            new ImportedStaticModelLineage(sourceOrdinal.Value);
        ValidateLineage(
            id,
            representation,
            sourceOrdinal,
            modelName,
            origin,
            scale,
            bounds,
            normalizedBindings,
            compiledDisposition,
            normalizedLineage);

        Representation = representation;
        SourceOrdinal = sourceOrdinal;
        ModelName = modelName;
        Scale = scale;
        CompiledFieldBindings = normalizedBindings;
        Lineage = normalizedLineage;
        _importedOrigin = origin;
        _importedBounds = bounds;
        _origin = origin;
        _bounds = bounds;
        _compiledDisposition = compiledDisposition;
        _transform = new EditorStaticModelTransformState(
            origin.Value,
            scale.Value,
            bounds.Value);
        _properties = CreateProperties();
    }

    public StaticModelRepresentation Representation { get; }
    public MapValue<int> SourceOrdinal { get; }
    public MapValue<string?> ModelName { get; }
    public MapValue<MapVector3> Origin => _origin;
    public MapValue<float?> Scale { get; }
    public MapValue<MapBounds?> Bounds => _bounds;
    public StaticModelCompiledFieldBindings CompiledFieldBindings { get; }
    public StaticModelLineage Lineage { get; }
    public StaticModelLineageKind LineageKind => Lineage.Kind;
    public bool IsImported =>
        LineageKind == StaticModelLineageKind.Imported;
    public AuthoredStaticModelDuplicatePairState? AuthoredDuplicatePair =>
        (Lineage as AuthoredDuplicateStaticModelLineage)?.Pair;

    /// <summary>
    /// Compiled render/collision presence. This is independent from
    /// <see cref="EditorMapObject.Visibility"/>, which only affects the
    /// editor projection.
    /// </summary>
    public StaticModelCompiledDisposition CompiledDisposition =>
        _compiledDisposition;

    /// <summary>
    /// Exact transform imported from the compiled baseline. Editor commands
    /// never replace this provenance snapshot.
    /// </summary>
    public EditorStaticModelTransformState ImportedTransform =>
        IsImported
            ? new(
                _importedOrigin.Value,
                Scale.Value,
                _importedBounds.Value)
            : throw new InvalidOperationException(
                "An authored duplicate has no imported transform.");

    /// <summary>
    /// Current immutable transform state used by backend-neutral scene
    /// projection. Translation commands replace this value atomically.
    /// </summary>
    public EditorStaticModelTransformState Transform => _transform;

    public override IReadOnlyList<EditorObjectProperty> Properties =>
        _properties;

    internal bool HasTransform(
        EditorStaticModelTransformState expected) =>
        _transform == expected;

    internal bool HasCompiledDisposition(
        StaticModelCompiledDisposition expected) =>
        _compiledDisposition == expected;

    internal void SetCompiledDisposition(
        StaticModelCompiledDisposition disposition)
    {
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));

        _compiledDisposition = disposition;
        _properties = CreateProperties();
    }

    internal void SetTransform(
        EditorStaticModelTransformState transform)
    {
        if (transform.Scale != Scale.Value)
        {
            throw new InvalidOperationException(
                "Phase 5 static-model transforms may change translation only.");
        }
        if (!transform.Origin.IsFinite ||
            transform.Bounds is { IsFinite: false })
        {
            throw new ArgumentOutOfRangeException(
                nameof(transform),
                "Static-model transform state must be finite.");
        }

        _transform = transform;
        _origin = new MapValue<MapVector3>(
            transform.Origin,
            transform.Origin == _importedOrigin.Value
                ? _importedOrigin.Provenance
                : MapValueProvenance.Authored,
            _importedOrigin.SourceBinding);
        _bounds = new MapValue<MapBounds?>(
            transform.Bounds,
            transform.Bounds == _importedBounds.Value
                ? _importedBounds.Provenance
                : MapValueProvenance.Authored,
            _importedBounds.SourceBinding);
        _properties = CreateProperties();
    }

    private IReadOnlyList<EditorObjectProperty> CreateProperties() =>
        Array.AsReadOnly<EditorObjectProperty>(
        [
            new EditorObjectProperty(
                "Lineage",
                LineageKind.ToString(),
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            new EditorObjectProperty(
                "Representation",
                Representation.ToString(),
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            new EditorObjectProperty(
                "Compiled disposition",
                CompiledDisposition.ToString(),
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            Property("Source ordinal", SourceOrdinal),
            Property("Model", ModelName),
            Property("Origin", Origin),
            Property("Scale", Scale),
            Property("Bounds", Bounds)
        ]);

    private static IEnumerable<SourceBindingId> SourceBindingsFor(
        MapValue<int> sourceOrdinal,
        MapValue<string?> modelName,
        MapValue<MapVector3> origin,
        MapValue<float?> scale,
        MapValue<MapBounds?> bounds,
        StaticModelCompiledFieldBindings? compiledFieldBindings) =>
        new[]
        {
            sourceOrdinal.SourceBinding,
            modelName.SourceBinding,
            origin.SourceBinding,
            scale.SourceBinding,
            bounds.SourceBinding
        }
        .Concat(
            compiledFieldBindings?.AllBindings ??
            Array.Empty<SourceBindingId>());

    internal static EditorStaticModel CreateAuthoredDuplicate(
        StaticModelRepresentation representation,
        AuthoredStaticModelDuplicatePairState pair,
        EditorStaticModel template,
        SourceBindingId sourceOrdinalBinding,
        SourceBindingId modelNameBinding,
        SourceBindingId originBinding,
        SourceBindingId scaleBinding,
        SourceBindingId boundsBinding)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(template);
        if (template.Representation != representation ||
            !template.IsImported)
        {
            throw new ArgumentException(
                "Authored static-model duplication requires an imported " +
                "template of the same representation.",
                nameof(template));
        }

        EditorStaticModelTransformState projected =
            template.ImportedTransform.WithOrigin(pair.Destination);
        return new EditorStaticModel(
            pair.ObjectId(representation),
            representation,
            new MapValue<int>(
                pair.ProjectedOrdinal(representation),
                MapValueProvenance.Authored,
                sourceOrdinalBinding),
            new MapValue<string?>(
                template.ModelName.Value,
                MapValueProvenance.Authored,
                modelNameBinding),
            new MapValue<MapVector3>(
                pair.Destination,
                MapValueProvenance.Authored,
                originBinding),
            new MapValue<float?>(
                template.Scale.Value,
                MapValueProvenance.Authored,
                scaleBinding),
            new MapValue<MapBounds?>(
                projected.Bounds,
                MapValueProvenance.Authored,
                boundsBinding),
            StaticModelCompiledFieldBindings.Legacy(
                representation,
                originBinding,
                boundsBinding),
            StaticModelCompiledDisposition.AuthoredPending,
            new AuthoredDuplicateStaticModelLineage(pair));
    }

    private static void ValidateLineage(
        MapObjectId id,
        StaticModelRepresentation representation,
        MapValue<int> sourceOrdinal,
        MapValue<string?> modelName,
        MapValue<MapVector3> origin,
        MapValue<float?> scale,
        MapValue<MapBounds?> bounds,
        StaticModelCompiledFieldBindings compiledFieldBindings,
        StaticModelCompiledDisposition compiledDisposition,
        StaticModelLineage lineage)
    {
        if (lineage is ImportedStaticModelLineage imported)
        {
            if (compiledDisposition ==
                    StaticModelCompiledDisposition.AuthoredPending ||
                imported.SourceOrdinal != sourceOrdinal.Value)
            {
                throw new ArgumentException(
                    "Imported static-model lineage must retain its exact " +
                    "source ordinal and cannot be authored-pending.",
                    nameof(lineage));
            }
            return;
        }
        if (lineage is not AuthoredDuplicateStaticModelLineage authored ||
            compiledDisposition !=
                StaticModelCompiledDisposition.AuthoredPending ||
            id != authored.Pair.ObjectId(representation) ||
            sourceOrdinal.Value !=
                authored.Pair.ProjectedOrdinal(representation) ||
            origin.Value != authored.Pair.Destination)
        {
            throw new ArgumentException(
                "Authored static-model lineage does not match its shared pair " +
                "authority.",
                nameof(lineage));
        }

        MapValueProvenance[] provenances =
        [
            sourceOrdinal.Provenance,
            modelName.Provenance,
            origin.Provenance,
            scale.Provenance,
            bounds.Provenance
        ];
        SourceBindingId[] valueBindings =
        [
            sourceOrdinal.SourceBinding,
            modelName.SourceBinding,
            origin.SourceBinding,
            scale.SourceBinding,
            bounds.SourceBinding
        ];
        if (provenances.Any(value =>
                value != MapValueProvenance.Authored) ||
            valueBindings.Distinct().Count() != valueBindings.Length ||
            valueBindings.Any(value =>
                authored.Pair.TemplateRecordBindings.Contains(value)) ||
            compiledFieldBindings.AllBindings.Any(value =>
                authored.Pair.TemplateRecordBindings.Contains(value)))
        {
            throw new ArgumentException(
                "Authored static-model values require distinct local binding " +
                "identities and cannot claim template record bindings.",
                nameof(lineage));
        }
    }
}

public enum CollisionObjectKind
{
    Brush,
    Triangle
}

public sealed class EditorCollisionObject : EditorMapObject
{
    private readonly IReadOnlyList<EditorObjectProperty> _properties;

    public EditorCollisionObject(
        MapObjectId id,
        CollisionObjectKind collisionKind,
        MapValue<int> sourceOrdinal,
        MapValue<MapBounds?> bounds,
        MapValue<uint?> contents,
        MapValue<int> supportingRecordCount)
        : base(
            id,
            collisionKind == CollisionObjectKind.Brush
                ? MapObjectKind.CollisionBrush
                : MapObjectKind.CollisionTriangle,
            $"Collision {collisionKind.ToString().ToLowerInvariant()} {sourceOrdinal.Value}",
            [
                sourceOrdinal.SourceBinding,
                bounds.SourceBinding,
                contents.SourceBinding,
                supportingRecordCount.SourceBinding
            ])
    {
        CollisionKind = collisionKind;
        SourceOrdinal = sourceOrdinal;
        Bounds = bounds;
        Contents = contents;
        SupportingRecordCount = supportingRecordCount;
        _properties = Array.AsReadOnly(
        [
            Property("Source ordinal", sourceOrdinal),
            Property("Bounds", bounds),
            Property("Contents", contents),
            Property(
                collisionKind == CollisionObjectKind.Brush
                    ? "Sides"
                    : "Vertices",
                supportingRecordCount)
        ]);
    }

    public CollisionObjectKind CollisionKind { get; }
    public MapValue<int> SourceOrdinal { get; }
    public MapValue<MapBounds?> Bounds { get; }
    public MapValue<uint?> Contents { get; }
    public MapValue<int> SupportingRecordCount { get; }
    public override IReadOnlyList<EditorObjectProperty> Properties => _properties;
}

public sealed class EditorEntityProperty
{
    private readonly IReadOnlyList<SourceBindingId> _sourceBindings;

    public EditorEntityProperty(
        MapEntPropertyOrdinal ordinal,
        MapValue<string> key,
        MapValue<string> value,
        MapEntSourceSpan span,
        MapEntSourceSpan keyTokenSpan,
        MapEntSourceSpan keyContentSpan,
        MapEntSourceSpan valueTokenSpan,
        MapEntSourceSpan valueContentSpan)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        Ordinal = ordinal;
        KeyValue = key;
        PropertyValue = value;
        Span = span;
        KeyTokenSpan = keyTokenSpan;
        KeyContentSpan = keyContentSpan;
        ValueTokenSpan = valueTokenSpan;
        ValueContentSpan = valueContentSpan;
        _sourceBindings = Array.AsReadOnly(
        [
            key.SourceBinding,
            value.SourceBinding
        ]);
    }

    public MapEntPropertyOrdinal Ordinal { get; }
    public string Key => KeyValue.Value;
    public string Value => PropertyValue.Value;
    public MapValue<string> KeyValue { get; }
    public MapValue<string> PropertyValue { get; }
    public MapValueProvenance KeyProvenance => KeyValue.Provenance;
    public MapValueProvenance ValueProvenance => PropertyValue.Provenance;
    public SourceBindingId KeySourceBinding => KeyValue.SourceBinding;
    public SourceBindingId ValueSourceBinding =>
        PropertyValue.SourceBinding;
    public MapValueProvenance Provenance =>
        KeyProvenance == ValueProvenance
            ? KeyProvenance
            : MapValueProvenance.Unknown;
    public SourceBindingId SourceBinding => ValueSourceBinding;
    public IReadOnlyList<SourceBindingId> SourceBindings => _sourceBindings;
    public MapEntSourceSpan Span { get; }
    public MapEntSourceSpan KeyTokenSpan { get; }
    public MapEntSourceSpan KeyContentSpan { get; }
    public MapEntSourceSpan ValueTokenSpan { get; }
    public MapEntSourceSpan ValueContentSpan { get; }

    internal EditorEntityProperty Project(
        MapEntsSyntaxProperty syntaxProperty) =>
        new(
            Ordinal,
            new MapValue<string>(
                syntaxProperty.Key,
                KeyValue.Provenance,
                KeyValue.SourceBinding),
            new MapValue<string>(
                syntaxProperty.Value,
                PropertyValue.Provenance,
                PropertyValue.SourceBinding),
            syntaxProperty.Span,
            syntaxProperty.KeyTokenSpan,
            syntaxProperty.KeyContentSpan,
            syntaxProperty.ValueTokenSpan,
            syntaxProperty.ValueContentSpan);
}

public sealed class EditorEntity : EditorMapObject
{
    private EditorEntityState _state;

    public EditorEntity(
        MapObjectId id,
        MapValue<int> sourceOrdinal,
        MapValue<int> sourceByteOffset,
        MapValue<int> sourceByteLength,
        string? className,
        MapEntityCompilationAssessment compilationAssessment,
        IEnumerable<EditorEntityProperty> keyValues)
        : this(CreateConstruction(
            id,
            sourceOrdinal,
            sourceByteOffset,
            sourceByteLength,
            className,
            compilationAssessment,
            keyValues))
    {
    }

    private EditorEntity(EditorEntityConstruction construction)
        : base(
            construction.Id,
            MapObjectKind.Entity,
            CreateDisplayName(
                construction.ClassName,
                construction.SourceOrdinal.Value),
            construction.KeyValues
                .SelectMany(property => property.SourceBindings)
                .Append(construction.SourceOrdinal.SourceBinding)
                .Append(construction.SourceByteOffset.SourceBinding)
                .Append(construction.SourceByteLength.SourceBinding))
    {
        SourceOrdinal = construction.SourceOrdinal;
        SyntaxOrdinal =
            new MapEntEntityOrdinal(construction.SourceOrdinal.Value);
        _state = CreateState(
            construction.SourceByteOffset,
            construction.SourceByteLength,
            construction.CompilationAssessment,
            construction.KeyValues);
    }

    public MapValue<int> SourceOrdinal { get; }
    public MapEntEntityOrdinal SyntaxOrdinal { get; }
    public MapValue<int> SourceByteOffset => _state.SourceByteOffset;
    public MapValue<int> SourceByteLength => _state.SourceByteLength;
    public string? ClassName => _state.ClassName;
    public MapEntityCommonKeyProjection CommonKeys => _state.CommonKeys;
    public MapEntityCompilationAssessment CompilationAssessment =>
        _state.CompilationAssessment;
    public IReadOnlyList<EditorEntityProperty> KeyValues =>
        _state.KeyValues;
    public override IReadOnlyList<EditorObjectProperty> Properties =>
        _state.Properties;

    public EditorEntityProperty GetProperty(
        MapEntPropertyOrdinal ordinal)
    {
        if ((uint)ordinal.Value >= (uint)_state.KeyValues.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                ordinal.Value,
                "Property ordinal is outside this semantic entity.");
        }

        return _state.KeyValues[ordinal.Value];
    }

    internal EditorEntityState CaptureState() => _state;

    internal EditorEntityState ProjectState(
        MapEntsSyntaxEntity syntaxEntity,
        MapEntityCompilationAssessment compilationAssessment)
    {
        ArgumentNullException.ThrowIfNull(syntaxEntity);
        ArgumentNullException.ThrowIfNull(compilationAssessment);
        if (syntaxEntity.Ordinal != SyntaxOrdinal)
        {
            throw new InvalidOperationException(
                $"Syntax entity {syntaxEntity.Ordinal} cannot project semantic entity {SyntaxOrdinal}.");
        }
        if (syntaxEntity.Properties.Count != _state.KeyValues.Count)
        {
            throw new InvalidOperationException(
                $"Syntax entity {SyntaxOrdinal} changed property cardinality.");
        }

        EditorEntityProperty[] projected = syntaxEntity.Properties
            .Select((property, index) =>
            {
                EditorEntityProperty current = _state.KeyValues[index];
                if (current.Ordinal != property.Ordinal)
                {
                    throw new InvalidOperationException(
                        $"Syntax property ordinal {property.Ordinal} does not match semantic ordinal {current.Ordinal}.");
                }

                return current.Project(property);
            })
            .ToArray();
        return CreateState(
            new MapValue<int>(
                syntaxEntity.Span.Offset,
                SourceByteOffset.Provenance,
                SourceByteOffset.SourceBinding),
            new MapValue<int>(
                syntaxEntity.Span.Length,
                SourceByteLength.Provenance,
                SourceByteLength.SourceBinding),
            compilationAssessment,
            projected);
    }

    internal bool HasState(EditorEntityState state) =>
        ReferenceEquals(_state, state);

    internal void SetState(EditorEntityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SetDisplayName(state.DisplayName);
        _state = state;
    }

    private EditorEntityState CreateState(
        MapValue<int> sourceByteOffset,
        MapValue<int> sourceByteLength,
        MapEntityCompilationAssessment compilationAssessment,
        IEnumerable<EditorEntityProperty> keyValues)
    {
        EditorEntityProperty[] values = keyValues.ToArray();
        IReadOnlyList<EditorEntityProperty> readOnlyValues =
            Array.AsReadOnly(values);
        MapEntityCommonKeyProjection commonKeys =
            MapEntityCommonKeyProjection.Create(readOnlyValues);
        string? className =
            commonKeys.ClassName.ParsedValue?.Value;

        IReadOnlyList<EditorObjectProperty> properties = Array.AsReadOnly(
        [
            Property("Source ordinal", SourceOrdinal),
            Property("Byte offset", sourceByteOffset),
            Property("Byte length", sourceByteLength),
            new EditorObjectProperty(
                "Compilation relationship",
                compilationAssessment.Relationship.ToString(),
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            new EditorObjectProperty(
                "Consumer evidence",
                compilationAssessment.Evidence,
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            .. commonKeys.Values
                .Where(value => value.IsPresent)
                .Select(value => new EditorObjectProperty(
                    $"Common {value.SerializedKey}",
                    value.DisplayValue,
                    value.ProjectionProvenance,
                    value.SourceBinding ??
                    SourceOrdinal.SourceBinding)),
            .. readOnlyValues.Select(value => new EditorObjectProperty(
                value.Key,
                value.Value,
                value.ValueProvenance,
                value.ValueSourceBinding))
        ]);
        return new EditorEntityState(
            sourceByteOffset,
            sourceByteLength,
            className,
            commonKeys,
            compilationAssessment,
            readOnlyValues,
            properties,
            CreateDisplayName(className, SourceOrdinal.Value));
    }

    private static string CreateDisplayName(
        string? className,
        int sourceOrdinal) =>
        string.IsNullOrWhiteSpace(className)
            ? $"Entity {sourceOrdinal}"
            : $"{className} ({sourceOrdinal})";

    private static EditorEntityConstruction CreateConstruction(
        MapObjectId id,
        MapValue<int> sourceOrdinal,
        MapValue<int> sourceByteOffset,
        MapValue<int> sourceByteLength,
        string? className,
        MapEntityCompilationAssessment compilationAssessment,
        IEnumerable<EditorEntityProperty> keyValues)
    {
        ArgumentNullException.ThrowIfNull(sourceOrdinal);
        ArgumentNullException.ThrowIfNull(sourceByteOffset);
        ArgumentNullException.ThrowIfNull(sourceByteLength);
        ArgumentNullException.ThrowIfNull(compilationAssessment);
        ArgumentNullException.ThrowIfNull(keyValues);
        EditorEntityProperty[] copy = keyValues.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Entity properties cannot contain null values.",
                nameof(keyValues));
        }
        string? projectedClassName =
            MapEntityCommonKeyProjection.Create(
                Array.AsReadOnly(copy))
                .ClassName
                .ParsedValue?.Value;
        if (!string.Equals(
                className,
                projectedClassName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Classname must be the fail-closed common-key projection " +
                "of the exact ordered entity properties.",
                nameof(className));
        }

        return new EditorEntityConstruction(
            id,
            sourceOrdinal,
            sourceByteOffset,
            sourceByteLength,
            className,
            compilationAssessment,
            Array.AsReadOnly(copy));
    }

    private sealed record EditorEntityConstruction(
        MapObjectId Id,
        MapValue<int> SourceOrdinal,
        MapValue<int> SourceByteOffset,
        MapValue<int> SourceByteLength,
        string? ClassName,
        MapEntityCompilationAssessment CompilationAssessment,
        IReadOnlyList<EditorEntityProperty> KeyValues);
}

internal sealed record EditorEntityState(
    MapValue<int> SourceByteOffset,
    MapValue<int> SourceByteLength,
    string? ClassName,
    MapEntityCommonKeyProjection CommonKeys,
    MapEntityCompilationAssessment CompilationAssessment,
    IReadOnlyList<EditorEntityProperty> KeyValues,
    IReadOnlyList<EditorObjectProperty> Properties,
    string DisplayName);

public sealed class EditorPrimaryLight : EditorMapObject
{
    private readonly MapValue<MapVector3> _importedColor;
    private readonly MapValue<byte> _importedExponent;
    private readonly MapValue<float> _importedCosHalfFovInner;
    private IReadOnlyList<EditorObjectProperty> _properties;
    private MapValue<MapVector3> _color;
    private MapValue<byte> _exponent;
    private MapValue<float> _cosHalfFovInner;

    public EditorPrimaryLight(
        MapObjectId id,
        MapValue<int> sourceOrdinal,
        MapValue<byte> lightType,
        MapValue<byte> canUseShadowMap,
        MapValue<byte> exponent,
        MapValue<byte> unused,
        MapValue<MapVector3> color,
        MapValue<MapVector3> direction,
        MapValue<MapVector3> origin,
        MapValue<float> radius,
        MapValue<float> cosHalfFovOuter,
        MapValue<float> cosHalfFovInner,
        MapValue<float> cosHalfFovExpanded,
        MapValue<float> rotationLimit,
        MapValue<float> translationLimit,
        MapValue<string?> definitionName)
        : base(
            id,
            MapObjectKind.PrimaryLight,
            string.IsNullOrWhiteSpace(definitionName.Value)
                ? $"Primary light {sourceOrdinal.Value}"
                : $"{definitionName.Value} ({sourceOrdinal.Value})",
            [
                sourceOrdinal.SourceBinding,
                lightType.SourceBinding,
                canUseShadowMap.SourceBinding,
                exponent.SourceBinding,
                unused.SourceBinding,
                color.SourceBinding,
                direction.SourceBinding,
                origin.SourceBinding,
                radius.SourceBinding,
                cosHalfFovOuter.SourceBinding,
                cosHalfFovInner.SourceBinding,
                cosHalfFovExpanded.SourceBinding,
                rotationLimit.SourceBinding,
                translationLimit.SourceBinding,
                definitionName.SourceBinding
            ])
    {
        if (exponent.Provenance !=
            MapValueProvenance.ExactDecodedRuntime)
        {
            throw new ArgumentException(
                "Imported primary-light exponent state requires exact " +
                "decoded-runtime provenance.",
                nameof(exponent));
        }
        if (cosHalfFovInner.Provenance !=
            MapValueProvenance.ExactDecodedRuntime)
        {
            throw new ArgumentException(
                "Imported primary-light inner-cone state requires exact " +
                "decoded-runtime provenance.",
                nameof(cosHalfFovInner));
        }

        SourceOrdinal = sourceOrdinal;
        LightType = lightType;
        CanUseShadowMap = canUseShadowMap;
        _importedExponent = exponent;
        _exponent = exponent;
        Unused = unused;
        _importedColor = color;
        _color = color;
        Direction = direction;
        Origin = origin;
        Radius = radius;
        CosHalfFovOuter = cosHalfFovOuter;
        _importedCosHalfFovInner = cosHalfFovInner;
        _cosHalfFovInner = cosHalfFovInner;
        CosHalfFovExpanded = cosHalfFovExpanded;
        RotationLimit = rotationLimit;
        TranslationLimit = translationLimit;
        DefinitionName = definitionName;
        _properties = CreateProperties();
    }

    public MapValue<int> SourceOrdinal { get; }
    public MapValue<byte> LightType { get; }
    public MapValue<byte> CanUseShadowMap { get; }
    public MapValue<byte> ImportedExponent => _importedExponent;
    public MapValue<byte> Exponent => _exponent;
    public MapValue<byte> Unused { get; }
    public MapValue<MapVector3> Color => _color;
    public MapValue<MapVector3> Direction { get; }
    public MapValue<MapVector3> Origin { get; }
    public MapValue<float> Radius { get; }
    public MapValue<float> CosHalfFovOuter { get; }
    public MapValue<float> ImportedCosHalfFovInner =>
        _importedCosHalfFovInner;
    public MapValue<float> CosHalfFovInner => _cosHalfFovInner;
    public MapValue<float> CosHalfFovExpanded { get; }
    public MapValue<float> RotationLimit { get; }
    public MapValue<float> TranslationLimit { get; }
    public MapValue<string?> DefinitionName { get; }
    public override IReadOnlyList<EditorObjectProperty> Properties => _properties;

    internal void SetColor(MapVector3 color)
    {
        _color = new MapValue<MapVector3>(
            color,
            color == _importedColor.Value
                ? _importedColor.Provenance
                : MapValueProvenance.Authored,
            _importedColor.SourceBinding);
        _properties = CreateProperties();
    }

    internal void SetExponent(byte exponent)
    {
        _exponent = new MapValue<byte>(
            exponent,
            exponent == _importedExponent.Value
                ? MapValueProvenance.ExactDecodedRuntime
                : MapValueProvenance.Authored,
            _importedExponent.SourceBinding);
        _properties = CreateProperties();
    }

    internal void SetCosHalfFovInner(float value)
    {
        _cosHalfFovInner = new MapValue<float>(
            value,
            SameBits(value, _importedCosHalfFovInner.Value)
                ? _importedCosHalfFovInner.Provenance
                : MapValueProvenance.Authored,
            _importedCosHalfFovInner.SourceBinding);
        _properties = CreateProperties();
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private IReadOnlyList<EditorObjectProperty> CreateProperties() =>
        Array.AsReadOnly(
        [
            Property("Source ordinal", SourceOrdinal),
            Property("Type", LightType),
            Property("Can use shadow map", CanUseShadowMap),
            Property("Exponent", Exponent),
            Property("Unused byte", Unused),
            Property("Color", Color),
            Property("Direction", Direction),
            Property("Origin", Origin),
            Property("Radius", Radius),
            Property("Cos half FOV outer", CosHalfFovOuter),
            Property("Cos half FOV inner", CosHalfFovInner),
            Property("Cos half FOV expanded", CosHalfFovExpanded),
            Property("Rotation limit", RotationLimit),
            Property("Translation limit", TranslationLimit),
            Property("Light definition", DefinitionName)
        ]);
}

public enum GlassRepresentation
{
    FxDefinition,
    FxInitialPiece,
    GameplayPiece
}

/// <summary>
/// Authoritative semantic state shared by one imported FxGlassDef and every
/// initial-piece projection that names it through DefIndex. HalfThickness is
/// derived by pieces; packed RGBA remains definition-only. Each serialized
/// value is owned by the definition exactly once.
/// </summary>
internal sealed class EditorFxGlassDefinitionState
{
    public EditorFxGlassDefinitionState(
        int definitionOrdinal,
        MapObjectId definitionObjectId,
        SourceBindingId halfThicknessSourceBinding,
        float importedHalfThickness,
        SourceBindingId colorSourceBinding,
        uint importedColor)
    {
        if (definitionOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(definitionOrdinal));
        if (definitionObjectId.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionObjectId));
        }
        if (halfThicknessSourceBinding.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfThicknessSourceBinding));
        }
        if (colorSourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(colorSourceBinding));
        if (!float.IsFinite(importedHalfThickness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(importedHalfThickness));
        }

        DefinitionOrdinal = definitionOrdinal;
        DefinitionObjectId = definitionObjectId;
        SourceBinding = halfThicknessSourceBinding;
        ColorSourceBinding = colorSourceBinding;
        ImportedHalfThickness = importedHalfThickness;
        CurrentHalfThickness = importedHalfThickness;
        ImportedColor = importedColor;
        CurrentColor = importedColor;
    }

    public int DefinitionOrdinal { get; }
    public MapObjectId DefinitionObjectId { get; }
    public SourceBindingId SourceBinding { get; }
    public SourceBindingId ColorSourceBinding { get; }
    public float ImportedHalfThickness { get; }
    public float CurrentHalfThickness { get; private set; }
    public uint ImportedColor { get; }
    public uint CurrentColor { get; private set; }

    public MapValue<float?> Project(GlassRepresentation representation) =>
        new(
            CurrentHalfThickness,
            representation == GlassRepresentation.FxDefinition
                ? SameBits(
                    CurrentHalfThickness,
                    ImportedHalfThickness)
                    ? MapValueProvenance.ExactDecodedRuntime
                    : MapValueProvenance.Authored
                : MapValueProvenance.Derived,
            SourceBinding);

    public MapValue<uint?> ProjectColor() =>
        new(
            CurrentColor,
            CurrentColor == ImportedColor
                ? MapValueProvenance.ExactDecodedRuntime
                : MapValueProvenance.Authored,
            ColorSourceBinding);

    internal void SetCurrentHalfThickness(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));

        CurrentHalfThickness = value;
    }

    internal void SetCurrentColor(uint value)
    {
        CurrentColor = value;
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}

public sealed class EditorGlassObject : EditorMapObject
{
    private readonly IReadOnlyList<EditorObjectProperty>
        _additionalProperties;
    private readonly MapValue<float?> _standaloneHalfThickness;
    private readonly MapValue<uint?> _standaloneColor;
    private readonly EditorFxGlassDefinitionState? _definitionState;

    public EditorGlassObject(
        MapObjectId id,
        GlassRepresentation representation,
        MapValue<int> sourceOrdinal,
        MapValue<int?> definitionIndex,
        MapValue<MapVector3?> origin,
        MapValue<float?> halfThickness,
        IEnumerable<EditorObjectProperty>? additionalProperties = null)
        : this(
            id,
            representation,
            sourceOrdinal,
            definitionIndex,
            origin,
            halfThickness,
            additionalProperties,
            definitionState: null)
    {
    }

    internal EditorGlassObject(
        MapObjectId id,
        GlassRepresentation representation,
        MapValue<int> sourceOrdinal,
        MapValue<int?> definitionIndex,
        MapValue<MapVector3?> origin,
        MapValue<float?> halfThickness,
        IEnumerable<EditorObjectProperty>? additionalProperties,
        EditorFxGlassDefinitionState? definitionState)
        : base(
            id,
            representation == GlassRepresentation.GameplayPiece
                ? MapObjectKind.GameplayGlass
                : MapObjectKind.FxGlass,
            $"{representation} {sourceOrdinal.Value}",
            [
                sourceOrdinal.SourceBinding,
                definitionIndex.SourceBinding,
                origin.SourceBinding,
                halfThickness.SourceBinding,
                .. definitionState is not null &&
                   representation == GlassRepresentation.FxDefinition
                    ? [definitionState.ColorSourceBinding]
                    : Array.Empty<SourceBindingId>(),
                .. (additionalProperties ?? [])
                    .Select(value => value.SourceBinding)
            ])
    {
        ArgumentNullException.ThrowIfNull(sourceOrdinal);
        ArgumentNullException.ThrowIfNull(definitionIndex);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(halfThickness);
        if (definitionState is not null)
        {
            if (representation == GlassRepresentation.GameplayPiece ||
                definitionIndex.Value !=
                    definitionState.DefinitionOrdinal ||
                halfThickness.SourceBinding !=
                    definitionState.SourceBinding ||
                halfThickness.Value is not { } projectedThickness ||
                !SameBits(
                    projectedThickness,
                    definitionState.ImportedHalfThickness) ||
                (representation == GlassRepresentation.FxDefinition &&
                 id != definitionState.DefinitionObjectId))
            {
                throw new ArgumentException(
                    "Fx glass objects must agree with their authoritative " +
                    "definition state.",
                    nameof(definitionState));
            }
        }

        Representation = representation;
        SourceOrdinal = sourceOrdinal;
        DefinitionIndex = definitionIndex;
        Origin = origin;
        _standaloneHalfThickness = halfThickness;
        _standaloneColor = new MapValue<uint?>(
            null,
            MapValueProvenance.Unknown,
            sourceOrdinal.SourceBinding);
        _definitionState = definitionState;
        _additionalProperties = Array.AsReadOnly(
            (additionalProperties ?? []).ToArray());
    }

    public GlassRepresentation Representation { get; }
    public MapValue<int> SourceOrdinal { get; }
    public MapValue<int?> DefinitionIndex { get; }
    public MapValue<MapVector3?> Origin { get; }
    public MapValue<float?> HalfThickness =>
        _definitionState?.Project(Representation) ??
        _standaloneHalfThickness;
    public MapValue<uint?> Color =>
        Representation == GlassRepresentation.FxDefinition
            ? _definitionState?.ProjectColor() ?? _standaloneColor
            : _standaloneColor;
    public override IReadOnlyList<EditorObjectProperty> Properties =>
        Array.AsReadOnly(
        [
            new EditorObjectProperty(
                "Representation",
                Representation.ToString(),
                MapValueProvenance.Derived,
                SourceOrdinal.SourceBinding),
            Property("Source ordinal", SourceOrdinal),
            Property("Definition index", DefinitionIndex),
            Property("Origin", Origin),
            Property("Half thickness", HalfThickness),
            .. Color.Value is { } color
                ?
                [
                    new EditorObjectProperty(
                        "Color",
                        $"0x{color:X8}",
                        Color.Provenance,
                        Color.SourceBinding)
                ]
                : Array.Empty<EditorObjectProperty>(),
            .. _additionalProperties
        ]);

    internal EditorFxGlassDefinitionState? DefinitionState =>
        _definitionState;

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}

public sealed class EditorSpatialObject : EditorMapObject
{
    private readonly IReadOnlyList<EditorObjectProperty> _properties;

    public EditorSpatialObject(
        MapObjectId id,
        MapObjectKind kind,
        MapValue<int> sourceOrdinal,
        MapValue<MapBounds?> bounds,
        MapValue<int> childCount)
        : base(
            id,
            kind is MapObjectKind.Cell or MapObjectKind.Portal
                ? kind
                : throw new ArgumentOutOfRangeException(nameof(kind)),
            $"{kind} {sourceOrdinal.Value}",
            [
                sourceOrdinal.SourceBinding,
                bounds.SourceBinding,
                childCount.SourceBinding
            ])
    {
        SourceOrdinal = sourceOrdinal;
        Bounds = bounds;
        ChildCount = childCount;
        _properties = Array.AsReadOnly(
        [
            Property("Source ordinal", sourceOrdinal),
            Property("Bounds", bounds),
            Property("Children / vertices", childCount)
        ]);
    }

    public MapValue<int> SourceOrdinal { get; }
    public MapValue<MapBounds?> Bounds { get; }
    public MapValue<int> ChildCount { get; }
    public override IReadOnlyList<EditorObjectProperty> Properties => _properties;
}
