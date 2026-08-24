using IW4.AssetExchange.RawFile;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Gsc;

/// <summary>
/// Builds an immutable GSC/CSC workspace from active runtime providers and
/// applied target-document drafts. Exact editor-buffer content is represented
/// by a final document and never mutates runtime assets or session drafts.
/// </summary>
public sealed class GscWorkspaceIndexService
{
    private static readonly XAssetType[] CapturedAssetTypes =
    [
        XAssetType.RawFile
    ];

    private readonly object _sync = new();
    private readonly FastFileWorkspace _workspace;
    private readonly FastFileEditingSession _editingSession;
    private RuntimeWorkspaceCapture? _cachedRuntimeCapture;
    private GscWorkspaceSnapshot? _cachedBaseSnapshot;

    public GscWorkspaceIndexService(FastFileEditingSession editingSession)
    {
        _editingSession = editingSession
            ?? throw new ArgumentNullException(nameof(editingSession));
        _workspace = editingSession.Workspace;
    }

    public GscWorkspaceSnapshot GetSnapshot(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long assetPoolRevision = _workspace.LoadedZone.Context.AssetPool.Revision;
            long editingSessionRevision = _editingSession.Revision;
            if (_cachedBaseSnapshot is
                {
                    AssetPoolRevision: var cachedPoolRevision,
                    EditingSessionRevision: var cachedSessionRevision
                } &&
                cachedPoolRevision == assetPoolRevision &&
                cachedSessionRevision == editingSessionRevision)
            {
                return _cachedBaseSnapshot;
            }

            RuntimeWorkspaceCapture runtimeCapture =
                GetRuntimeCapture(assetPoolRevision, cancellationToken);
            AppliedAssetDefinitionsCapture appliedCapture =
                _editingSession.CaptureAppliedAssets(CapturedAssetTypes);
            if (appliedCapture.Revision != editingSessionRevision)
            {
                throw new InvalidOperationException(
                    "The applied authoring assets changed during GSC workspace capture.");
            }
            GscWorkspaceAuthoredDocument[] authoredDocuments =
                CaptureAuthoredDocuments(appliedCapture, cancellationToken);
            GscWorkspaceIndex effectiveIndex = authoredDocuments.Length == 0
                ? runtimeCapture.Index
                : runtimeCapture.Index.WithDocuments(
                    authoredDocuments.Select(document =>
                        new GscDocumentSnapshot(
                            GscScriptPath.FromAssetName(document.AssetName),
                            document.Source)),
                    cancellationToken);
            var captured = new GscWorkspaceSnapshot(
                runtimeCapture.AssetPoolRevision,
                appliedCapture.Revision,
                runtimeCapture.Slots,
                authoredDocuments,
                effectiveIndex);
            _cachedBaseSnapshot = captured;
            return captured;
        }
    }

    /// <summary>
    /// Warms the immutable base snapshot for the current runtime-pool and
    /// editing-session revisions on a worker thread. The normal snapshot cache
    /// serializes concurrent captures; editor-buffer documents remain
    /// demand-driven.
    /// </summary>
    public Task<GscWorkspaceSnapshot> WarmBaseSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => GetSnapshot(cancellationToken),
            cancellationToken);

    internal static bool IsScriptAssetName(string name)
    {
        string extension = Path.GetExtension(name);
        return extension.Equals(".gsc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csc", StringComparison.OrdinalIgnoreCase);
    }

    private RuntimeWorkspaceCapture GetRuntimeCapture(
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (_cachedRuntimeCapture?.AssetPoolRevision == expectedRevision)
            return _cachedRuntimeCapture;

        RuntimeWorkspaceCapture captured = CaptureRuntimeSnapshot(
            expectedRevision,
            cancellationToken);
        _cachedRuntimeCapture = captured;
        _cachedBaseSnapshot = null;
        return captured;
    }

    private RuntimeWorkspaceCapture CaptureRuntimeSnapshot(
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        XAssetPool pool = _workspace.LoadedZone.Context.AssetPool;
        if (pool.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                "The runtime asset pool changed before the GSC workspace capture began.");
        }

        XAssetSlot[] runtimeSlots = pool.Slots.ToArray();
        var capturedSlots = new List<GscWorkspaceRawFileSlot>();
        foreach (XAssetSlot slot in runtimeSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slot.AssetType != XAssetType.RawFile || !IsScriptAssetName(slot.Name))
                continue;

            XAssetProviderContribution activeProvider = slot.ActiveProvider;
            WorkspaceZone? ownerZone = _workspace.LoadedZones.FirstOrDefault(zone =>
                zone.LoadResult.Context.ZoneOwner == activeProvider.Owner);
            GscSourceText? source = activeProvider.IsReferencePlaceholder
                ? null
                : CaptureSource(slot.Name, activeProvider);
            capturedSlots.Add(new GscWorkspaceRawFileSlot(
                slot.Address,
                slot.Name,
                XAssetStableIdentity.NormalizeLookupName(slot.Name),
                activeProvider.Id,
                activeProvider.IsReferencePlaceholder,
                ownerZone?.IsTarget == true,
                source));
        }

        if (pool.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"The runtime asset pool changed from revision {expectedRevision} during GSC workspace capture.");
        }

        GscWorkspaceRawFileSlot[] orderedSlots = capturedSlots
            .OrderBy(slot => slot.NormalizedAssetName, StringComparer.Ordinal)
            .ThenBy(slot => slot.Address.Slot)
            .ToArray();
        GscWorkspaceIndex index = GscWorkspaceIndex.Create(
            orderedSlots
                .Where(slot => slot.Source is not null)
                .Select(slot => new GscDocumentSnapshot(
                    GscScriptPath.FromAssetName(slot.AssetName),
                    slot.Source!)),
            cancellationToken);
        if (pool.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"The runtime asset pool changed from revision {expectedRevision} while the GSC language index was built.");
        }

        return new RuntimeWorkspaceCapture(
            expectedRevision,
            Array.AsReadOnly(orderedSlots),
            index);
    }

    private static GscWorkspaceAuthoredDocument[] CaptureAuthoredDocuments(
        AppliedAssetDefinitionsCapture capture,
        CancellationToken cancellationToken)
    {
        var documents = new List<GscWorkspaceAuthoredDocument>();
        foreach (AppliedAssetDefinition applied in capture.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applied.Definition is not RawFileAsset rawFile ||
                !IsScriptAssetName(rawFile.Name ?? string.Empty))
            {
                continue;
            }

            documents.Add(new GscWorkspaceAuthoredDocument(
                applied.RowIdentity,
                rawFile.Name!,
                CaptureSource(rawFile.Name!, rawFile)));
        }

        return documents.ToArray();
    }

    private static GscSourceText CaptureSource(
        string assetName,
        XAssetProviderContribution provider)
    {
        if (provider.Asset is not RawFileAsset rawFile)
        {
            throw new InvalidDataException(
                $"Active RawFile provider {provider.Id} for '{assetName}' has no RawFile asset.");
        }
        return CaptureSource(assetName, rawFile);
    }

    private static GscSourceText CaptureSource(
        string assetName,
        RawFileAsset rawFile)
    {
        return CreateSource(
            assetName,
            RawFileContentCodec.DecodeStrictSerializedContent(assetName, rawFile));
    }

    private static GscSourceText CreateSource(
        string assetName,
        byte[] logicalContent)
    {
        RawFileContentClassification classification =
            RawFileContentClassifier.Classify(assetName, logicalContent);
        RawFileTextEncoding encoding = classification.TextEncoding
            ?? throw new InvalidDataException(
                $"RawFile '{assetName}' does not contain decodable script text.");
        string text = RawFileContentClassifier.DecodeText(
            logicalContent,
            encoding);
        return new GscSourceText(
            text,
            RawFileContentClassifier.GetTextEncoding(encoding));
    }

    private sealed record RuntimeWorkspaceCapture(
        long AssetPoolRevision,
        IReadOnlyList<GscWorkspaceRawFileSlot> Slots,
        GscWorkspaceIndex Index);
}
