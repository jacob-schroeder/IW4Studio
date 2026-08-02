using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.SourceDocuments;

/// <summary>
/// Version of the Studio-owned canonical map-source contract. This version is
/// independent from FastFile and individual compiler-contract versions.
/// </summary>
public readonly record struct Iw4SceneFormatVersion
{
    public Iw4SceneFormatVersion(int major, int minor)
    {
        if (major <= 0)
            throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor));

        Major = major;
        Minor = minor;
    }

    public int Major { get; }

    public int Minor { get; }

    public override string ToString() => $"{Major}.{Minor}";
}

public static class Iw4SceneFormat
{
    public const string Identity = "iw4-studio.iw4scene";

    public static Iw4SceneFormatVersion CurrentVersion { get; } =
        new(major: 1, minor: 0);
}

/// <summary>
/// Immutable Studio source document for the first greenfield vertical slice.
/// It owns semantic geometry and stable identities only; compiled ordinals,
/// visibility, lighting, asset rows, pointers, and FastFile bytes are derived.
///
/// Version 1.0 deliberately supports standalone render meshes and standalone
/// collision brushes/triangle meshes. Additional source domains must be added
/// through an explicit format-version change rather than silently inferred.
/// </summary>
public sealed class Iw4SceneDocument
{
    private readonly IReadOnlyList<AuthoredIndexedRenderMeshSource>
        _renderMeshes;
    private readonly IReadOnlyList<AuthoredCollisionSource>
        _collisionSources;

    public Iw4SceneDocument(
        Iw4SceneFormatVersion formatVersion,
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        string targetZoneName,
        MapCompilerProfileIdentity compilerProfile,
        string entityProfileIdentity,
        MinimalMultiplayerMapTargetStartupProfile startupProfile,
        IEnumerable<AuthoredIndexedRenderMeshSource> renderMeshes,
        IEnumerable<AuthoredCollisionSource> collisionSources)
    {
        if (formatVersion != Iw4SceneFormat.CurrentVersion)
        {
            throw new NotSupportedException(
                $"IW4 Studio supports .iw4scene version " +
                $"{Iw4SceneFormat.CurrentVersion}; received " +
                $"{formatVersion}.");
        }
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        ArgumentNullException.ThrowIfNull(compilerProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityProfileIdentity);
        if (entityProfileIdentity.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "An entity-profile identity cannot contain NUL bytes.",
                nameof(entityProfileIdentity));
        }
        if (!Enum.IsDefined(startupProfile))
            throw new ArgumentOutOfRangeException(nameof(startupProfile));
        ArgumentNullException.ThrowIfNull(renderMeshes);
        ArgumentNullException.ThrowIfNull(collisionSources);

        string normalizedMapName =
            MapCompilerContentIdentityInput
                .NormalizeMultiplayerMapAssetName(mapAssetName);
        string normalizedZoneName =
            NormalizeTargetZoneName(targetZoneName);
        if (!string.Equals(
                normalizedMapName,
                $"maps/mp/{normalizedZoneName}.d3dbsp",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The target zone must be the basename of the canonical map " +
                "asset identity.",
                nameof(targetZoneName));
        }

        AuthoredIndexedRenderMeshSource[] renderCopy =
            renderMeshes.ToArray();
        AuthoredCollisionSource[] collisionCopy =
            collisionSources.ToArray();
        ValidateSources(renderCopy, collisionCopy);

        FormatVersion = formatVersion;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
        MapAssetName = normalizedMapName;
        TargetZoneName = normalizedZoneName;
        CompilerProfile = compilerProfile;
        EntityProfileIdentity = entityProfileIdentity;
        StartupProfile = startupProfile;
        _renderMeshes =
            new ReadOnlyCollection<AuthoredIndexedRenderMeshSource>(
                OrderByStableId(renderCopy));
        _collisionSources =
            new ReadOnlyCollection<AuthoredCollisionSource>(
                OrderByStableId(collisionCopy));
    }

    public Iw4SceneFormatVersion FormatVersion { get; }

    public MapDocumentId DocumentId { get; }

    public long DocumentRevision { get; }

    public string MapAssetName { get; }

    public string TargetZoneName { get; }

    public MapCompilerProfileIdentity CompilerProfile { get; }

    public string EntityProfileIdentity { get; }

    public MinimalMultiplayerMapTargetStartupProfile StartupProfile
    {
        get;
    }

    public IReadOnlyList<AuthoredIndexedRenderMeshSource> RenderMeshes =>
        _renderMeshes;

    public IReadOnlyList<AuthoredCollisionSource> CollisionSources =>
        _collisionSources;

    private static void ValidateSources(
        IReadOnlyList<AuthoredIndexedRenderMeshSource> renderMeshes,
        IReadOnlyList<AuthoredCollisionSource> collisionSources)
    {
        if (renderMeshes.Any(value => value is null))
        {
            throw new ArgumentException(
                "Render-source rows cannot be null.",
                nameof(renderMeshes));
        }
        if (collisionSources.Any(value => value is null))
        {
            throw new ArgumentException(
                "Collision-source rows cannot be null.",
                nameof(collisionSources));
        }
        if (renderMeshes.Any(value =>
                value.Ownership is not
                    StandaloneWorldRenderMeshOwnership))
        {
            throw new NotSupportedException(
                ".iw4scene 1.0 supports standalone-world render-mesh " +
                "ownership only.");
        }
        if (collisionSources.Any(value =>
                value is not
                    AuthoredConvexBrushCollisionSource and not
                    AuthoredIndexedTriangleMeshCollisionSource ||
                value.Ownership is not
                    StandaloneWorldCollisionSourceOwnership))
        {
            throw new NotSupportedException(
                ".iw4scene 1.0 supports standalone-world convex-brush and " +
                "triangle-mesh collision sources only.");
        }

        Guid[] objectIds = renderMeshes
            .Select(value => value.ObjectId.Value)
            .Concat(
                collisionSources.Select(value => value.ObjectId.Value))
            .ToArray();
        if (objectIds.Distinct().Count() != objectIds.Length)
        {
            throw new ArgumentException(
                "Every .iw4scene source object must own one globally unique " +
                "stable GUID.");
        }
    }

    private static T[] OrderByStableId<T>(IEnumerable<T> sources)
        where T : class =>
        sources
            .OrderBy(
                value => value switch
                {
                    AuthoredIndexedRenderMeshSource render =>
                        render.ObjectId.ToString(),
                    AuthoredCollisionSource collision =>
                        collision.ObjectId.ToString(),
                    _ => throw new InvalidOperationException(
                        "Unsupported .iw4scene source row.")
                },
                StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeTargetZoneName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized =
            value.Trim().Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains('/') ||
            normalized.EndsWith(".ff", StringComparison.Ordinal) ||
            !normalized.StartsWith("mp_", StringComparison.Ordinal) ||
            normalized.Any(character =>
                character != '_' &&
                (character < 'a' || character > 'z') &&
                (character < '0' || character > '9')))
        {
            throw new ArgumentException(
                "A target zone name must be a bare normalized ASCII mp_ " +
                "identity.",
                nameof(value));
        }

        return normalized;
    }
}
