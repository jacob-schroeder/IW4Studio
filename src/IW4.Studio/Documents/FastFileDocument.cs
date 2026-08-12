using IW4.FastFiles.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;

namespace IW4.Studio.Documents;

/// <summary>
/// One immutable imported or blank fastfile document. Imported documents keep
/// their loaded view for workbench consumers, while canonical Save As starts
/// only from the immediately frozen semantic link request.
/// </summary>
public sealed class FastFileDocument
{
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly FastFileDocumentOpenRequest? _request;
    private readonly string? _sourcePath;
    private readonly LoadedXZone? _loadedZone;

    internal FastFileDocument(
        FastFileDocumentOpenRequest request,
        WorkspaceZone targetZone,
        ZoneLinkRequest initialLinkRequest,
        LinkAssetPool targetAssets,
        LinkAssetPool dependencyAssets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetZone);
        ArgumentNullException.ThrowIfNull(initialLinkRequest);
        ArgumentNullException.ThrowIfNull(targetAssets);
        ArgumentNullException.ThrowIfNull(dependencyAssets);

        _request = request;
        if (!targetZone.IsTarget)
            throw new ArgumentException("The document target must be marked as target.", nameof(targetZone));
        _sourcePath = targetZone.PhysicalPath;
        _loadedZone = targetZone.LoadResult;
        InitialLinkRequest = initialLinkRequest;
        TargetAssets = targetAssets;
        DependencyAssets = dependencyAssets;
    }

    internal FastFileDocument(ZoneLinkRequest initialLinkRequest)
    {
        InitialLinkRequest = initialLinkRequest ??
            throw new ArgumentNullException(nameof(initialLinkRequest));
        TargetAssets = initialLinkRequest.Assets;
        DependencyAssets = new LinkAssetPool([]);
    }

    public bool IsBlank => _loadedZone is null;

    public Guid DocumentId => _documentId;

    public FastFileDocumentOpenRequest Request => _request ??
        throw new InvalidOperationException("A blank fastfile document has no open request.");

    public string SourcePath => _sourcePath ??
        throw new InvalidOperationException("A blank fastfile document has no source path.");

    public LoadedXZone LoadedZone => _loadedZone ??
        throw new InvalidOperationException("A blank fastfile document has no loaded zone.");

    /// <summary>The immutable semantic state captured at open or blank creation.</summary>
    public ZoneLinkRequest InitialLinkRequest { get; }

    internal LinkAssetPool TargetAssets { get; }

    internal LinkAssetPool DependencyAssets { get; }

    internal string? SourcePathOrNull => _sourcePath;

    /// <summary>
    /// The loader-frozen symbolic input for unchanged source-layout replay.
    /// It is not a canonical asset-link input.
    /// </summary>
    public ZoneObjectFile ZoneObjectFile => _loadedZone?.ZoneObjectFile ??
        throw new InvalidOperationException("A blank fastfile document has no source-layout object.");
}
