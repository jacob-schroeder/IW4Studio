using System.Collections.Immutable;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

/// <summary>
/// Immutable scene-lifetime resource inputs. Backends independently lower
/// these semantic descriptions into their own API resources.
/// </summary>
public sealed class RenderResourceSnapshot
{
    private readonly ImmutableArray<RenderVertexLayoutDescriptor>
        _vertexLayoutsByIdentity;
    private readonly ImmutableArray<RenderInstanceLayoutDescriptor>
        _instanceLayoutsByIdentity;
    private readonly ImmutableArray<RenderGeometryDescriptor>
        _geometriesByIdentity;
    private readonly ImmutableArray<RenderInstanceDescriptor>
        _instancesByIdentity;
    private readonly ImmutableArray<RenderTextureDescriptor>
        _texturesByIdentity;
    private readonly ImmutableArray<RenderSamplerDescriptor>
        _samplersByIdentity;

    public RenderResourceSnapshot(
        IEnumerable<RenderVertexLayoutDescriptor> vertexLayouts,
        IEnumerable<RenderGeometryDescriptor> geometries,
        IEnumerable<RenderTextureDescriptor> textures,
        IEnumerable<RenderSamplerDescriptor> samplers)
        : this(
            vertexLayouts,
            Array.Empty<RenderInstanceLayoutDescriptor>(),
            geometries,
            Array.Empty<RenderInstanceDescriptor>(),
            textures,
            samplers)
    {
    }

    public RenderResourceSnapshot(
        IEnumerable<RenderVertexLayoutDescriptor> vertexLayouts,
        IEnumerable<RenderInstanceLayoutDescriptor> instanceLayouts,
        IEnumerable<RenderGeometryDescriptor> geometries,
        IEnumerable<RenderInstanceDescriptor> instances,
        IEnumerable<RenderTextureDescriptor> textures,
        IEnumerable<RenderSamplerDescriptor> samplers)
    {
        VertexLayouts = FreezeAndValidate(
            vertexLayouts,
            nameof(vertexLayouts),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.VertexLayout);
        InstanceLayouts = FreezeAndValidate(
            instanceLayouts,
            nameof(instanceLayouts),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.InstanceLayout);
        Geometries = FreezeAndValidate(
            geometries,
            nameof(geometries),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.Geometry);
        Instances = FreezeAndValidate(
            instances,
            nameof(instances),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.Instances);
        Textures = FreezeAndValidate(
            textures,
            nameof(textures),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.Texture);
        Samplers = FreezeAndValidate(
            samplers,
            nameof(samplers),
            descriptor => descriptor.Identity,
            RenderSemanticResourceKind.Sampler);

        _vertexLayoutsByIdentity = SortByIdentity(
            VertexLayouts,
            descriptor => descriptor.Identity);
        _instanceLayoutsByIdentity = SortByIdentity(
            InstanceLayouts,
            descriptor => descriptor.Identity);
        _geometriesByIdentity = SortByIdentity(
            Geometries,
            descriptor => descriptor.Identity);
        _instancesByIdentity = SortByIdentity(
            Instances,
            descriptor => descriptor.Identity);
        _texturesByIdentity = SortByIdentity(
            Textures,
            descriptor => descriptor.Identity);
        _samplersByIdentity = SortByIdentity(
            Samplers,
            descriptor => descriptor.Identity);

        foreach (RenderGeometryDescriptor geometry in Geometries)
        {
            RenderVertexLayoutDescriptor layout =
                RequireVertexLayout(geometry.VertexLayout);
            if (layout.StrideBytes != geometry.VertexStrideBytes ||
                !string.Equals(
                    layout.ContentDigest,
                    geometry.VertexLayoutContentDigest,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Geometry {geometry.Identity} does not match vertex " +
                    $"layout {layout.Identity} content.",
                    nameof(geometries));
            }
        }

        foreach (RenderInstanceDescriptor descriptor in Instances)
        {
            RenderInstanceLayoutDescriptor layout =
                RequireInstanceLayout(descriptor.Layout);
            if (layout.StrideBytes != descriptor.StrideBytes ||
                !string.Equals(
                    layout.ContentDigest,
                    descriptor.LayoutContentDigest,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Instance resource {descriptor.Identity} does not match " +
                    $"layout {layout.Identity} content.",
                    nameof(instances));
            }
        }

        ContentDigest = RenderContentDigest.Compute(AppendContent);
    }

    public ImmutableArray<RenderVertexLayoutDescriptor> VertexLayouts { get; }

    public ImmutableArray<RenderInstanceLayoutDescriptor> InstanceLayouts
        { get; }

    public ImmutableArray<RenderGeometryDescriptor> Geometries { get; }

    public ImmutableArray<RenderInstanceDescriptor> Instances { get; }

    public ImmutableArray<RenderTextureDescriptor> Textures { get; }

    public ImmutableArray<RenderSamplerDescriptor> Samplers { get; }

    public string ContentDigest { get; }

    public RenderVertexLayoutDescriptor RequireVertexLayout(
        RenderSemanticIdentity identity) =>
        Require(
            _vertexLayoutsByIdentity,
            identity,
            RenderSemanticResourceKind.VertexLayout,
            descriptor => descriptor.Identity);

    public RenderGeometryDescriptor RequireGeometry(
        RenderSemanticIdentity identity) =>
        Require(
            _geometriesByIdentity,
            identity,
            RenderSemanticResourceKind.Geometry,
            descriptor => descriptor.Identity);

    public RenderInstanceLayoutDescriptor RequireInstanceLayout(
        RenderSemanticIdentity identity) =>
        Require(
            _instanceLayoutsByIdentity,
            identity,
            RenderSemanticResourceKind.InstanceLayout,
            descriptor => descriptor.Identity);

    public RenderInstanceDescriptor RequireInstances(
        RenderSemanticIdentity identity) =>
        Require(
            _instancesByIdentity,
            identity,
            RenderSemanticResourceKind.Instances,
            descriptor => descriptor.Identity);

    public RenderTextureDescriptor RequireTexture(
        RenderSemanticIdentity identity) =>
        Require(
            _texturesByIdentity,
            identity,
            RenderSemanticResourceKind.Texture,
            descriptor => descriptor.Identity);

    public RenderSamplerDescriptor RequireSampler(
        RenderSemanticIdentity identity) =>
        Require(
            _samplersByIdentity,
            identity,
            RenderSemanticResourceKind.Sampler,
            descriptor => descriptor.Identity);

    internal void AppendContent(RenderContentDigestWriter writer)
    {
        writer.WriteString("render-resources/v5");
        writer.WriteInt32(VertexLayouts.Length);
        foreach (RenderVertexLayoutDescriptor descriptor in VertexLayouts)
            writer.WriteString(descriptor.ContentDigest);

        writer.WriteInt32(InstanceLayouts.Length);
        foreach (RenderInstanceLayoutDescriptor descriptor in InstanceLayouts)
            writer.WriteString(descriptor.ContentDigest);

        writer.WriteInt32(Geometries.Length);
        foreach (RenderGeometryDescriptor descriptor in Geometries)
            writer.WriteString(descriptor.ContentDigest);

        writer.WriteInt32(Instances.Length);
        foreach (RenderInstanceDescriptor descriptor in Instances)
            writer.WriteString(descriptor.ContentDigest);

        writer.WriteInt32(Textures.Length);
        foreach (RenderTextureDescriptor descriptor in Textures)
            writer.WriteString(descriptor.ContentDigest);

        writer.WriteInt32(Samplers.Length);
        foreach (RenderSamplerDescriptor descriptor in Samplers)
            writer.WriteString(descriptor.ContentDigest);
    }

    private static ImmutableArray<T> FreezeAndValidate<T>(
        IEnumerable<T> source,
        string parameterName,
        Func<T, RenderSemanticIdentity> getIdentity,
        RenderSemanticResourceKind expectedKind)
        where T : class
    {
        ImmutableArray<T> frozen =
            RenderSnapshotCollections.Freeze(source, parameterName);
        if (frozen.Any(item => item is null))
        {
            throw new ArgumentException(
                "A resource catalog cannot contain null entries.",
                parameterName);
        }

        var identities = new HashSet<RenderSemanticIdentity>();
        foreach (T item in frozen)
        {
            RenderSemanticIdentity identity = getIdentity(item);
            RenderVertexLayoutDescriptor.RequireIdentity(identity, expectedKind);
            if (!identities.Add(identity))
            {
                throw new ArgumentException(
                    $"Duplicate resource identity {identity}.",
                    parameterName);
            }
        }
        return frozen;
    }

    private static T Require<T>(
        ImmutableArray<T> resources,
        RenderSemanticIdentity identity,
        RenderSemanticResourceKind expectedKind,
        Func<T, RenderSemanticIdentity> getIdentity)
        where T : class
    {
        RenderVertexLayoutDescriptor.RequireIdentity(identity, expectedKind);
        int lower = 0;
        int upper = resources.Length - 1;
        while (lower <= upper)
        {
            int midpoint = lower + ((upper - lower) / 2);
            T candidate = resources[midpoint];
            int comparison = CompareIdentity(
                getIdentity(candidate),
                identity);
            if (comparison == 0)
                return candidate;
            if (comparison < 0)
                lower = midpoint + 1;
            else
                upper = midpoint - 1;
        }

        throw new KeyNotFoundException(
            $"No {expectedKind} resource exists for {identity}.");
    }

    private static ImmutableArray<T> SortByIdentity<T>(
        ImmutableArray<T> resources,
        Func<T, RenderSemanticIdentity> getIdentity) => resources
            .OrderBy(
                resource => getIdentity(resource).Kind)
            .ThenBy(
                resource => getIdentity(resource).Value,
                StringComparer.Ordinal)
            .ToImmutableArray();

    private static int CompareIdentity(
        RenderSemanticIdentity left,
        RenderSemanticIdentity right)
    {
        int kind = left.Kind.CompareTo(right.Kind);
        return kind != 0
            ? kind
            : string.Compare(
                left.Value,
                right.Value,
                StringComparison.Ordinal);
    }
}
