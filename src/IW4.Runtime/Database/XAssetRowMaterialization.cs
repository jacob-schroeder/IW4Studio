using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;

namespace IW4.Runtime.Database;

public enum XAssetMaterializationDisposition
{
    FullDefinition,
    ResolvedReference,
    UnresolvedReference,
    OffsetAlias,
    Null,
    OpaqueNativeNoOp,
    Unsupported,
    FailedRolledBack
}

public enum XAssetProviderRegistrationDisposition
{
    FullDefinition,
    ReferencePlaceholder
}

/// <summary>
/// Minimal identity for the provider created by one serialized asset row.
/// Runtime pool state owns header bytes and native allocation data; row
/// materialization retains only what Studio needs to detach editable content.
/// </summary>
public sealed record XAssetProviderMaterialization
{
    public XAssetProviderMaterialization(
        XAssetProviderId providerId,
        XAssetStableIdentity identity,
        string originalName,
        BaseAsset asset,
        XAssetProviderRegistrationDisposition disposition)
    {
        if (providerId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(providerId));

        ArgumentNullException.ThrowIfNull(originalName);
        ArgumentNullException.ThrowIfNull(asset);

        ProviderId = providerId;
        Identity = identity;
        OriginalName = originalName;
        Asset = asset;
        Disposition = disposition;
    }

    public XAssetProviderId ProviderId { get; }

    public XAssetStableIdentity Identity { get; }

    /// <summary>Original serialized spelling, including any leading comma.</summary>
    public string OriginalName { get; }

    public BaseAsset Asset { get; }

    public XAssetProviderRegistrationDisposition Disposition { get; }
}

/// <summary>
/// Minimal result of publishing one serialized XAsset row. Serialized row
/// identity remains in XAssetListEntrySnapshot; this retains only publication
/// state needed by Studio to detach editable content.
/// </summary>
public sealed record XAssetRowMaterialization(
    XAssetMaterializationDisposition Disposition,
    XAssetProviderMaterialization? RootProvider,
    XAssetProviderId? ActiveProviderId);

internal sealed class XAssetRowMaterializationScope
{
    private readonly XAssetListEntrySnapshot _row;
    private readonly int _sourceStartOffset;
    private readonly List<XAssetProviderRegistrationCapture> _registrations = [];
    private bool _closed;

    public XAssetRowMaterializationScope(
        XAssetListEntrySnapshot row,
        int sourceStartOffset)
    {
        if (sourceStartOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceStartOffset));

        _row = row ?? throw new ArgumentNullException(nameof(row));
        _sourceStartOffset = sourceStartOffset;
    }

    public bool IsClosed => _closed;

    public void RecordRegistration(
        XBlockAddress pointerCellAddress,
        XAssetProviderMaterialization provider,
        XAssetProviderId activeProviderId)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(provider);
        if (activeProviderId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(activeProviderId));

        _registrations.Add(new(
            pointerCellAddress,
            provider,
            activeProviderId));
    }

    public XAssetRowMaterialization Complete(int sourceEndOffset)
    {
        EnsureOpen();
        _closed = true;
        return Build(sourceEndOffset, forcedDisposition: null);
    }

    public XAssetRowMaterialization Discard(
        int sourceEndOffset,
        bool unsupported = false)
    {
        EnsureOpen();
        _closed = true;
        return Build(
            sourceEndOffset,
            unsupported
                ? XAssetMaterializationDisposition.Unsupported
                : XAssetMaterializationDisposition.FailedRolledBack);
    }

    private XAssetRowMaterialization Build(
        int sourceEndOffset,
        XAssetMaterializationDisposition? forcedDisposition)
    {
        if (sourceEndOffset < _sourceStartOffset)
            throw new ArgumentOutOfRangeException(nameof(sourceEndOffset));

        XAssetProviderRegistrationCapture? rootCapture = forcedDisposition is null
            ? ResolveRootCapture()
            : TryResolveCapturedRoot();
        XAssetProviderMaterialization? rootProvider = rootCapture?.Provider;
        XAssetProviderId? activeProviderId = rootCapture?.ActiveProviderId;
        XAssetMaterializationDisposition disposition = forcedDisposition
            ?? DeterminePublishedDisposition(rootProvider, activeProviderId);

        return new XAssetRowMaterialization(
            disposition,
            rootProvider,
            activeProviderId);
    }

    private XAssetProviderRegistrationCapture? ResolveRootCapture()
    {
        if (_row.HeaderKind == XAssetHeaderKind.Opaque ||
            _row.AssetPointer.Type is PointerType.Null or PointerType.Offset)
        {
            if (_registrations.Count != 0)
            {
                throw new InvalidDataException(
                    $"XAsset row {_row.Index} cannot register providers for its {_row.HeaderKind}/{_row.AssetPointer.Type} disposition.");
            }

            return null;
        }

        XAssetProviderRegistrationCapture[] roots = _registrations
            .Where(capture => capture.PointerCellAddress == _row.AssetPointerCellAddress)
            .ToArray();
        if (roots.Length != 1)
        {
            throw new InvalidDataException(
                $"XAsset row {_row.Index} {_row.Type} requires exactly one root registration at " +
                $"{_row.AssetPointerCellAddress}, but observed {roots.Length}.");
        }

        return roots[0];
    }

    private XAssetProviderRegistrationCapture? TryResolveCapturedRoot() =>
        _registrations.FirstOrDefault(
            capture => capture.PointerCellAddress == _row.AssetPointerCellAddress);

    private XAssetMaterializationDisposition DeterminePublishedDisposition(
        XAssetProviderMaterialization? rootProvider,
        XAssetProviderId? activeProviderId)
    {
        if (_row.HeaderKind == XAssetHeaderKind.Opaque)
            return XAssetMaterializationDisposition.OpaqueNativeNoOp;

        return _row.AssetPointer.Type switch
        {
            PointerType.Null => XAssetMaterializationDisposition.Null,
            PointerType.Offset => XAssetMaterializationDisposition.OffsetAlias,
            PointerType.Inline or PointerType.Insert =>
                DetermineInlineDisposition(rootProvider, activeProviderId),
            _ => throw new InvalidDataException(
                $"XAsset row {_row.Index} has unsupported pointer disposition {_row.AssetPointer.Type}.")
        };
    }

    private static XAssetMaterializationDisposition DetermineInlineDisposition(
        XAssetProviderMaterialization? rootProvider,
        XAssetProviderId? activeProviderId)
    {
        if (rootProvider is null || activeProviderId is null)
            throw new InvalidDataException("Inline XAsset materialization has no root provider.");

        if (rootProvider.Disposition != XAssetProviderRegistrationDisposition.ReferencePlaceholder)
            return XAssetMaterializationDisposition.FullDefinition;

        return rootProvider.ProviderId == activeProviderId.Value
            ? XAssetMaterializationDisposition.UnresolvedReference
            : XAssetMaterializationDisposition.ResolvedReference;
    }

    private void EnsureOpen()
    {
        if (_closed)
            throw new InvalidOperationException("The XAsset row materialization scope is already closed.");
    }

    private sealed record XAssetProviderRegistrationCapture(
        XBlockAddress PointerCellAddress,
        XAssetProviderMaterialization Provider,
        XAssetProviderId ActiveProviderId);
}
