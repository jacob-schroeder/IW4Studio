using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;

namespace IW4.Studio.MapEditor.Editing.Collision;

/// <summary>
/// Editor projection of one immutable canonical authored collision source.
/// <see cref="EditorProvenanceBinding"/> is local document provenance only:
/// it never identifies an imported serialized field or compiled owner.
/// </summary>
public sealed class EditorAuthoredCollisionObject : EditorMapObject
{
    private readonly IReadOnlyList<EditorObjectProperty> _properties;

    public EditorAuthoredCollisionObject(
        AuthoredCollisionSource source,
        SourceBindingId editorProvenanceBinding)
        : base(
            RequireSource(source).ObjectId,
            ObjectKind(source.GeometryKind),
            CreateDisplayName(source),
            [editorProvenanceBinding])
    {
        if (editorProvenanceBinding.Value == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(editorProvenanceBinding));
        }

        Source = source;
        EditorProvenanceBinding = editorProvenanceBinding;
        _properties = CreateProperties(source, editorProvenanceBinding);
    }

    public AuthoredCollisionSource Source { get; }

    public SourceBindingId EditorProvenanceBinding { get; }

    public override IReadOnlyList<EditorObjectProperty> Properties =>
        _properties;

    public static EditorAuthoredCollisionObject Create(
        AuthoredCollisionSource source) =>
        new(
            source,
            new SourceBindingId(Guid.NewGuid()));

    internal EditorAuthoredCollisionObject WithSource(
        AuthoredCollisionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.ObjectId != Id)
        {
            throw new ArgumentException(
                "Replacement authored collision must preserve its semantic " +
                "object identity.",
                nameof(source));
        }

        return ReferenceEquals(source, Source)
            ? this
            : new EditorAuthoredCollisionObject(
                source,
                EditorProvenanceBinding);
    }

    private static AuthoredCollisionSource RequireSource(
        AuthoredCollisionSource? source) =>
        source ?? throw new ArgumentNullException(nameof(source));

    private static MapObjectKind ObjectKind(
        CollisionGeometryKind geometryKind) =>
        geometryKind switch
        {
            CollisionGeometryKind.ConvexBrush =>
                MapObjectKind.CollisionBrush,
            CollisionGeometryKind.TriangleMesh =>
                MapObjectKind.CollisionTriangle,
            CollisionGeometryKind.StaticModelHull =>
                MapObjectKind.CollisionStaticModel,
            _ => throw new ArgumentOutOfRangeException(
                nameof(geometryKind))
        };

    private static string CreateDisplayName(AuthoredCollisionSource source) =>
        source.GeometryKind switch
        {
            CollisionGeometryKind.ConvexBrush =>
                "Authored collision brush",
            CollisionGeometryKind.TriangleMesh =>
                "Authored collision mesh",
            CollisionGeometryKind.StaticModelHull =>
                "Authored static-model collision",
            _ => throw new ArgumentOutOfRangeException(
                nameof(source))
        };

    private static IReadOnlyList<EditorObjectProperty> CreateProperties(
        AuthoredCollisionSource source,
        SourceBindingId binding)
    {
        var properties = new List<EditorObjectProperty>
        {
            AuthoredProperty(
                "Geometry",
                source.GeometryKind.ToString(),
                binding),
            AuthoredProperty(
                "Ownership",
                source.Ownership.Category.ToString(),
                binding)
        };

        switch (source)
        {
            case AuthoredConvexBrushCollisionSource brush:
                properties.Add(
                    AuthoredProperty(
                        "Bounds",
                        brush.Bounds.ToString(),
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Faces",
                        brush.Faces.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Contents",
                        brush.Contents.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        binding));
                break;

            case AuthoredIndexedTriangleMeshCollisionSource mesh:
                properties.Add(
                    AuthoredProperty(
                        "Bounds",
                        mesh.Bounds.ToString(),
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Triangles",
                        mesh.Triangles.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Vertices",
                        mesh.Vertices.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        binding));
                break;

            case AuthoredPairedStaticModelCollisionSource staticModel:
                properties.Add(
                    AuthoredProperty(
                        "Model",
                        staticModel.ExactSerializedModelName,
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Origin",
                        staticModel.Origin.ToString(),
                        binding));
                properties.Add(
                    AuthoredProperty(
                        "Bounds",
                        staticModel.Bounds.ToString(),
                        binding));
                break;

            default:
                throw new ArgumentException(
                    $"Unsupported authored collision source type " +
                    $"{source.GetType().Name}.",
                    nameof(source));
        }

        return Array.AsReadOnly(properties.ToArray());
    }

    private static EditorObjectProperty AuthoredProperty(
        string name,
        string value,
        SourceBindingId binding) =>
        new(
            name,
            value,
            MapValueProvenance.Authored,
            binding);
}
