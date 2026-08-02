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
/// Captures the runtime pool's active GSC/CSC documents into an immutable,
/// revision-keyed Studio snapshot. Unsaved editor content is represented as
/// an overlay and never mutates runtime assets or the cached base snapshot.
/// </summary>
public sealed class GscWorkspaceIndexService
{
    private readonly object _sync = new();
    private readonly FastFileWorkspace _workspace;
    private GscWorkspaceSnapshot? _cachedBaseSnapshot;

    public GscWorkspaceIndexService(FastFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public GscWorkspaceSnapshot GetSnapshot(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long revision = _workspace.Runtime.AssetPool.Revision;
            if (_cachedBaseSnapshot?.AssetPoolRevision == revision)
                return _cachedBaseSnapshot;

            GscWorkspaceSnapshot captured = CaptureStableSnapshot(
                revision,
                cancellationToken);
            _cachedBaseSnapshot = captured;
            return captured;
        }
    }

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

    private GscWorkspaceSnapshot CaptureStableSnapshot(
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

        return new GscWorkspaceSnapshot(expectedRevision, orderedSlots, index);
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

        RawFileContentClassification classification =
            RawFileContentClassifier.Classify(assetName, logicalContent);
        RawFileTextEncoding encoding = classification.TextEncoding
            ?? throw new InvalidDataException(
                $"RawFile '{assetName}' does not contain decodable script text.");
        string text = RawFileContentClassifier.DecodeText(logicalContent, encoding);
        return new GscWorkspaceRawFileSource(
            new GscSourceText(text, CreateSourceEncoding(encoding)),
            encoding,
            isCompressed,
            payload.Length,
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
}
