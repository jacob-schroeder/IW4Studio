using IW4.Assets.Zone;
using IW4.Gsc.Analysis;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Gsc;

/// <summary>Detached, decoded text for the active provider of one script slot.</summary>
public sealed record GscWorkspaceRawFileSource(
    GscSourceText Text,
    RawFileTextEncoding Encoding,
    bool IsCompressed,
    int SerializedLength,
    int LogicalLength);

/// <summary>
/// One applied target-document script draft. Unlike a runtime slot, this is
/// addressed by its stable authoring row and can represent a newly added file
/// that has no XAssetPool provider yet.
/// </summary>
public sealed class GscWorkspaceAuthoredDocument
{
    internal GscWorkspaceAuthoredDocument(
        TargetZoneRowIdentity rowIdentity,
        string assetName,
        GscWorkspaceRawFileSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentNullException.ThrowIfNull(source);

        RowIdentity = rowIdentity;
        AssetName = assetName;
        Source = source;
    }

    public TargetZoneRowIdentity RowIdentity { get; }

    public string AssetName { get; }

    public GscWorkspaceRawFileSource Source { get; }
}

/// <summary>
/// One canonical RawFile slot whose name identifies a GSC or CSC document.
/// All providers are retained as scalar provenance, while only the active
/// full-definition provider can contribute source to the language index.
/// </summary>
public sealed class GscWorkspaceRawFileSlot
{
    internal GscWorkspaceRawFileSlot(
        XAssetPoolAddress address,
        string assetName,
        string normalizedAssetName,
        XAssetProviderId activeProviderId,
        bool activeProviderIsReferencePlaceholder,
        bool activeProviderIsTargetZone,
        GscWorkspaceRawFileSource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedAssetName);
        if ((source is null) != activeProviderIsReferencePlaceholder)
        {
            throw new ArgumentException(
                "Only a full-definition active provider can supply captured GSC source.",
                nameof(source));
        }

        Address = address;
        AssetName = assetName;
        NormalizedAssetName = normalizedAssetName;
        ActiveProviderId = activeProviderId;
        ActiveProviderIsTargetZone = activeProviderIsTargetZone;
        Source = source;
    }

    public XAssetPoolAddress Address { get; }

    public string AssetName { get; }

    public string NormalizedAssetName { get; }

    public XAssetProviderId ActiveProviderId { get; }

    public bool ActiveProviderIsTargetZone { get; }

    /// <summary>
    /// Null only when the slot contains reference placeholders but no loaded
    /// full definition.
    /// </summary>
    public GscWorkspaceRawFileSource? Source { get; }
}

/// <summary>Exact unsaved editor text used to replace one pool document.</summary>
public sealed record GscWorkspaceBufferOverlay
{
    public GscWorkspaceBufferOverlay(string assetName, GscSourceText source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentNullException.ThrowIfNull(source);
        if (!GscWorkspaceIndexService.IsScriptAssetName(assetName))
        {
            throw new ArgumentException(
                "A GSC workspace overlay must identify a .gsc or .csc RawFile.",
                nameof(assetName));
        }

        AssetName = assetName;
        Source = source;
    }

    public string AssetName { get; }

    public GscSourceText Source { get; }
}

/// <summary>
/// Immutable effective-revision snapshot consumed by the GSC language
/// workspace. Applied authoring drafts replace matching runtime documents;
/// the optional editor overlay never changes the detached base capture.
/// </summary>
public sealed class GscWorkspaceSnapshot
{
    private readonly IReadOnlyList<GscWorkspaceRawFileSlot> _slots;
    private readonly IReadOnlyList<GscWorkspaceAuthoredDocument>
        _authoredDocuments;

    internal GscWorkspaceSnapshot(
        long assetPoolRevision,
        long editingSessionRevision,
        IEnumerable<GscWorkspaceRawFileSlot> slots,
        IEnumerable<GscWorkspaceAuthoredDocument> authoredDocuments,
        GscWorkspaceIndex index,
        GscWorkspaceBufferOverlay? overlay = null)
    {
        if (assetPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(assetPoolRevision));
        if (editingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(editingSessionRevision));
        }
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(authoredDocuments);
        ArgumentNullException.ThrowIfNull(index);

        AssetPoolRevision = assetPoolRevision;
        EditingSessionRevision = editingSessionRevision;
        _slots = Array.AsReadOnly(slots.ToArray());
        _authoredDocuments = Array.AsReadOnly(authoredDocuments.ToArray());
        Index = index;
        Overlay = overlay;
    }

    public long AssetPoolRevision { get; }

    public long EditingSessionRevision { get; }

    public IReadOnlyList<GscWorkspaceRawFileSlot> Slots => _slots;

    public IReadOnlyList<GscWorkspaceAuthoredDocument> AuthoredDocuments =>
        _authoredDocuments;

    /// <summary>
    /// Language-neutral semantic index built from active provider sources and
    /// applied authoring drafts, with <see cref="Overlay"/> applied when
    /// present.
    /// </summary>
    public GscWorkspaceIndex Index { get; }

    public GscWorkspaceBufferOverlay? Overlay { get; }

    public GscAnalysisResult GetAnalysis(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        return Index.GetAnalysis(GscScriptPath.FromAssetName(
            XAssetStableIdentity.GetLookupSpelling(assetName)));
    }

    internal GscWorkspaceSnapshot WithOverlay(
        GscWorkspaceBufferOverlay overlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var document = new GscDocumentSnapshot(
            GscScriptPath.FromAssetName(
                XAssetStableIdentity.GetLookupSpelling(overlay.AssetName)),
            overlay.Source);
        return new GscWorkspaceSnapshot(
            AssetPoolRevision,
            EditingSessionRevision,
            _slots,
            _authoredDocuments,
            Index.WithDocument(document, cancellationToken),
            overlay);
    }
}
