using IW4.FastFiles.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Model;

namespace IW4.Studio.Documents;

/// <summary>
/// One immutable imported or blank fastfile document. Imported documents keep
/// their loaded view for workbench consumers, while canonical Save As starts
/// only from the immediately frozen semantic link request.
/// </summary>
public sealed class FastFileDocument
{
    private readonly FastFileDocumentOpenRequest? _request;
    private readonly string? _sourcePath;
    private readonly LoadedXZone? _loadedZone;

    internal FastFileDocument(
        FastFileDocumentOpenRequest request,
        LoadedXZone loadedZone,
        ZoneLinkRequest initialLinkRequest)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loadedZone);
        ArgumentNullException.ThrowIfNull(initialLinkRequest);

        _request = request;
        _sourcePath = Path.GetFullPath(request.Path);
        _loadedZone = loadedZone;
        InitialLinkRequest = initialLinkRequest;
    }

    internal FastFileDocument(ZoneLinkRequest initialLinkRequest)
    {
        InitialLinkRequest = initialLinkRequest ??
            throw new ArgumentNullException(nameof(initialLinkRequest));
    }

    public bool IsBlank => _loadedZone is null;

    public FastFileDocumentOpenRequest Request => _request ??
        throw new InvalidOperationException("A blank fastfile document has no open request.");

    public string SourcePath => _sourcePath ??
        throw new InvalidOperationException("A blank fastfile document has no source path.");

    public LoadedXZone LoadedZone => _loadedZone ??
        throw new InvalidOperationException("A blank fastfile document has no loaded zone.");

    /// <summary>The immutable semantic state captured at open or blank creation.</summary>
    public ZoneLinkRequest InitialLinkRequest { get; }

    internal string? SourcePathOrNull => _sourcePath;

    /// <summary>
    /// The loader-frozen symbolic input for unchanged source-layout replay.
    /// It is not a canonical asset-link input.
    /// </summary>
    public ZoneObjectFile ZoneObjectFile => _loadedZone?.ZoneObjectFile ??
        throw new InvalidOperationException("A blank fastfile document has no source-layout object.");
}
