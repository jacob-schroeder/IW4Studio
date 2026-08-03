using System.Text;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Studio.Documents;

namespace IW4.Studio.Gsc;

/// <summary>
/// Builds an immutable GSC/CSC workspace from active runtime providers and
/// applied target-document drafts. Exact editor-buffer content is represented
/// by a final overlay and never mutates runtime assets or session drafts.
/// </summary>
public sealed class GscWorkspaceIndexService
{
    private static readonly RawFileAuthoringAdapter RawFileAuthoring = new();

    private readonly object _sync = new();
    private readonly FastFileWorkspace _workspace;
    private readonly FastFileEditingSession _editingSession;
    private readonly HashSet<TargetZoneRowIdentity> _originalTargetRows;
    private RuntimeWorkspaceCapture? _cachedRuntimeCapture;
    private GscWorkspaceSnapshot? _cachedBaseSnapshot;

    public GscWorkspaceIndexService(FastFileEditingSession editingSession)
    {
        _editingSession = editingSession
            ?? throw new ArgumentNullException(nameof(editingSession));
        _workspace = editingSession.Workspace;
        _originalTargetRows = _workspace.TargetSource.Rows
            .Select(row => row.Identity)
            .ToHashSet();
    }

    public GscWorkspaceSnapshot GetSnapshot(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long assetPoolRevision = _workspace.Runtime.AssetPool.Revision;
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
            FastFileEditingSaveSnapshot editingCapture =
                _editingSession.CaptureForSave();
            GscWorkspaceAuthoredDocument[] authoredDocuments =
                CaptureAuthoredDocuments(editingCapture, cancellationToken);
            GscWorkspaceIndex effectiveIndex = authoredDocuments.Length == 0
                ? runtimeCapture.Index
                : runtimeCapture.Index.WithDocuments(
                    authoredDocuments.Select(document =>
                        new GscDocumentSnapshot(
                            GscScriptPath.FromAssetName(document.AssetName),
                            document.Source.Text)),
                    cancellationToken);
            var captured = new GscWorkspaceSnapshot(
                runtimeCapture.AssetPoolRevision,
                editingCapture.Revision,
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
    /// serializes concurrent captures; editor buffer overlays remain
    /// demand-driven.
    /// </summary>
    public Task<GscWorkspaceSnapshot> WarmBaseSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => GetSnapshot(cancellationToken),
            cancellationToken);

    public GscWorkspaceSnapshot GetSnapshot(
        GscWorkspaceBufferOverlay overlay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return GetSnapshot(cancellationToken).WithOverlay(
            overlay,
            cancellationToken);
    }

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
        XAssetPool pool = _workspace.Runtime.AssetPool;
        if (pool.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                "The runtime asset pool changed before the GSC workspace capture began.");
        }

        IReadOnlyDictionary<DbZoneHandle, WorkspaceZone> zonesByOwner =
            _workspace.LoadedZones
                .Where(zone => !zone.RuntimeZoneHandle.IsNone)
                .ToDictionary(zone => zone.RuntimeZoneHandle);
        XAssetSlot[] runtimeSlots = pool.Slots.ToArray();
        var capturedSlots = new List<GscWorkspaceRawFileSlot>();
        foreach (XAssetSlot slot in runtimeSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slot.AssetType != XAssetType.RawFile || !IsScriptAssetName(slot.Name))
                continue;

            XAssetProviderContribution activeProvider = slot.ActiveProvider;
            XAssetProviderContribution[] providers = slot.Providers.ToArray();
            GscWorkspaceProviderProvenance[] provenance = providers
                .Select(provider => CaptureProvenance(
                    provider,
                    provider.Id == activeProvider.Id,
                    zonesByOwner))
                .ToArray();
            GscWorkspaceRawFileSource? source = activeProvider.IsReferencePlaceholder
                ? null
                : CaptureSource(slot.Name, activeProvider);
            capturedSlots.Add(new GscWorkspaceRawFileSlot(
                slot.Address,
                slot.Name,
                XAssetStableIdentity.NormalizeLookupName(slot.Name),
                activeProvider.Id,
                provenance,
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
                    slot.Source!.Text)),
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

    private GscWorkspaceAuthoredDocument[] CaptureAuthoredDocuments(
        FastFileEditingSaveSnapshot editingCapture,
        CancellationToken cancellationToken)
    {
        var documents = new List<GscWorkspaceAuthoredDocument>();
        foreach (TargetZoneRowSource row in editingCapture.TargetRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.SerializedType != XAssetType.RawFile ||
                !editingCapture.TryGetDraftObject(
                    row.Identity,
                    out object? draftObject))
            {
                continue;
            }
            if (draftObject is not RawFileDraft draft)
            {
                throw new InvalidDataException(
                    $"Applied RawFile row {row.SerializedIndex} has draft " +
                    $"type '{draftObject?.GetType().FullName ?? "null"}'.");
            }
            if (!IsScriptAssetName(draft.OriginalName))
                continue;
            if (!IsEffectiveAuthoredOverride(row, draft))
                continue;

            documents.Add(new GscWorkspaceAuthoredDocument(
                row.Identity,
                draft.OriginalName,
                CaptureSource(draft)));
        }

        return documents.ToArray();
    }

    private bool IsEffectiveAuthoredOverride(
        TargetZoneRowSource row,
        RawFileDraft draft)
    {
        if (!_originalTargetRows.Contains(row.Identity))
            return true;

        if (row.AuthoredDefinition?.SemanticSnapshot is not
            RawFileAuthoredSnapshot authoredSnapshot)
        {
            return true;
        }

        RawFileDraft original = RawFileDraft.FromSnapshot(authoredSnapshot);
        return !RawFileAuthoring.SemanticallyEquals(original, draft);
    }

    private static GscWorkspaceProviderProvenance CaptureProvenance(
        XAssetProviderContribution provider,
        bool isActive,
        IReadOnlyDictionary<DbZoneHandle, WorkspaceZone> zonesByOwner)
    {
        zonesByOwner.TryGetValue(provider.Owner, out WorkspaceZone? zone);
        return new GscWorkspaceProviderProvenance(
            provider.Id,
            provider.Owner,
            provider.RegistrationSequence,
            provider.AssetType,
            provider.Name,
            provider.StagingAddress,
            provider.IsReferencePlaceholder,
            isActive,
            provider.HeaderBytes.Length,
            provider.NativePoolCopyBytes.Length,
            provider.NativePoolCopyCapturedLength,
            provider.SourceBlocks is not null,
            zone?.LogicalZoneName,
            zone?.PhysicalPath,
            zone?.IsTarget,
            zone?.IsActive);
    }

    private static GscWorkspaceRawFileSource CaptureSource(
        string assetName,
        XAssetProviderContribution provider)
    {
        if (provider.Asset is not RawFileAsset rawFile)
        {
            throw new InvalidDataException(
                $"Active RawFile provider {provider.Id} for '{assetName}' has no RawFile asset.");
        }
        if (rawFile.CompressedLen < 0 || rawFile.Len < 0)
        {
            throw new InvalidDataException(
                $"RawFile '{assetName}' has negative serialized length metadata.");
        }

        byte[] payload = rawFile.Buffer?.ToArray() ?? [];
        byte[] logicalContent;
        bool isCompressed = rawFile.CompressedLen != 0;
        if (isCompressed)
        {
            if (payload.Length != rawFile.CompressedLen)
            {
                throw new InvalidDataException(
                    $"Compressed RawFile '{assetName}' has {payload.Length} payload bytes; expected {rawFile.CompressedLen}.");
            }
            logicalContent = RawFileContentCodec.DecodeCompressed(payload, rawFile.Len);
        }
        else if (rawFile.Buffer is null)
        {
            if (rawFile.Len != 0)
            {
                throw new InvalidDataException(
                    $"RawFile '{assetName}' has no buffer for its declared {rawFile.Len}-byte content.");
            }
            logicalContent = [];
        }
        else
        {
            int expectedLength;
            try
            {
                expectedLength = checked(rawFile.Len + 1);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException(
                    $"RawFile '{assetName}' length cannot include its terminal null.",
                    exception);
            }
            if (payload.Length != expectedLength || payload[^1] != 0)
            {
                throw new InvalidDataException(
                    $"Uncompressed RawFile '{assetName}' must contain exactly len + 1 bytes ending in a terminal null.");
            }
            logicalContent = payload[..rawFile.Len];
        }

        return CreateSource(
            assetName,
            logicalContent,
            isCompressed,
            payload.Length);
    }

    private static GscWorkspaceRawFileSource CaptureSource(
        RawFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        byte[] logicalContent = draft.GetLogicalContentCopy();
        return CreateSource(
            draft.OriginalName,
            logicalContent,
            draft.Mode == RawFilePayloadMode.CompressedPayload,
            draft.GetSerializedPayloadCopy().Length);
    }

    private static GscWorkspaceRawFileSource CreateSource(
        string assetName,
        byte[] logicalContent,
        bool isCompressed,
        int serializedLength)
    {
        RawFileContentClassification classification =
            RawFileContentClassifier.Classify(assetName, logicalContent);
        RawFileTextEncoding encoding = classification.TextEncoding
            ?? throw new InvalidDataException(
                $"RawFile '{assetName}' does not contain decodable script text.");
        string text = RawFileContentClassifier.DecodeText(
            logicalContent,
            encoding);
        return new GscWorkspaceRawFileSource(
            new GscSourceText(text, CreateSourceEncoding(encoding)),
            encoding,
            isCompressed,
            serializedLength,
            logicalContent.Length);
    }

    private static Encoding CreateSourceEncoding(RawFileTextEncoding encoding)
    {
        return encoding switch
        {
            RawFileTextEncoding.Utf8 => new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            RawFileTextEncoding.Windows1252 => Encoding.GetEncoding(
                1252,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
    }

    private sealed record RuntimeWorkspaceCapture(
        long AssetPoolRevision,
        IReadOnlyList<GscWorkspaceRawFileSlot> Slots,
        GscWorkspaceIndex Index);
}
