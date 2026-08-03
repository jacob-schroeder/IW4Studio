using IW4.Assets.Zone;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Selection;

/// <summary>
/// Identifies which workbench navigator published the current asset selection.
/// The source also determines how a center editor resolves the immutable key.
/// </summary>
public enum WorkbenchAssetSelectionSource
{
    None = 0,
    FastFileAssets,
    AssetPool,
    ImageFilePak
}

/// <summary>
/// Stable identity for one streamed-image definition in the current document
/// workspace. The ordinal follows dependency load and source-image order.
/// </summary>
public readonly record struct WorkbenchStreamedImageIdentity
{
    public WorkbenchStreamedImageIdentity(
        Guid documentId,
        int ordinal)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

        DocumentId = documentId;
        Ordinal = ordinal;
    }

    public Guid DocumentId { get; }

    public int Ordinal { get; }
}

/// <summary>
/// Stable, scalar-only locator for an asset selected in a workbench navigator.
/// A center editor can resolve a target row through the workspace catalog or a
/// runtime asset through the pool without either navigator retaining that
/// mutable backend object.
/// </summary>
public readonly record struct WorkbenchAssetSelectionIdentity
{
    private WorkbenchAssetSelectionIdentity(
        WorkbenchAssetSelectionSource source,
        TargetZoneRowIdentity? targetRowIdentity,
        XAssetPoolAddress? assetPoolAddress,
        WorkbenchStreamedImageIdentity? streamedImageIdentity)
    {
        int populatedIdentityCount =
            (targetRowIdentity is null ? 0 : 1) +
            (assetPoolAddress is null ? 0 : 1) +
            (streamedImageIdentity is null ? 0 : 1);
        if (source == WorkbenchAssetSelectionSource.None ||
            populatedIdentityCount != 1 ||
            source switch
            {
                WorkbenchAssetSelectionSource.FastFileAssets =>
                    targetRowIdentity is null,
                WorkbenchAssetSelectionSource.AssetPool =>
                    assetPoolAddress is null,
                WorkbenchAssetSelectionSource.ImageFilePak =>
                    streamedImageIdentity is null,
                _ => true
            })
        {
            throw new ArgumentException(
                "A workbench selection must identify exactly one source-compatible target row, asset-pool slot, or streamed image.");
        }

        Source = source;
        TargetRowIdentity = targetRowIdentity;
        AssetPoolAddress = assetPoolAddress;
        StreamedImageIdentity = streamedImageIdentity;
    }

    public WorkbenchAssetSelectionSource Source { get; }

    public TargetZoneRowIdentity? TargetRowIdentity { get; }

    public XAssetPoolAddress? AssetPoolAddress { get; }

    public WorkbenchStreamedImageIdentity? StreamedImageIdentity { get; }

    public bool IsEmpty => Source == WorkbenchAssetSelectionSource.None;

    public static WorkbenchAssetSelectionIdentity ForTargetRow(
        TargetZoneRowIdentity identity) =>
        new(
            WorkbenchAssetSelectionSource.FastFileAssets,
            identity,
            assetPoolAddress: null,
            streamedImageIdentity: null);

    public static WorkbenchAssetSelectionIdentity ForAssetPoolSlot(
        XAssetPoolAddress address) =>
        new(
            WorkbenchAssetSelectionSource.AssetPool,
            targetRowIdentity: null,
            address,
            streamedImageIdentity: null);

    public static WorkbenchAssetSelectionIdentity ForStreamedImage(
        WorkbenchStreamedImageIdentity identity) =>
        new(
            WorkbenchAssetSelectionSource.ImageFilePak,
            targetRowIdentity: null,
            assetPoolAddress: null,
            identity);
}

/// <summary>
/// Immutable metadata published by every asset navigator. It intentionally
/// contains only values and copied strings; the properties pane and center
/// workspace resolve richer content from <see cref="Identity"/> when needed.
/// </summary>
public sealed record WorkbenchAssetSelection
{
    public WorkbenchAssetSelection(
        WorkbenchAssetSelectionIdentity identity,
        XAssetType assetType,
        string displayName,
        string normalizedName,
        WorkspaceAssetAccess access,
        string origin,
        string? providerZone,
        bool hasEditor,
        XAssetProviderId? providerId = null)
    {
        if (identity.IsEmpty)
            throw new ArgumentException("A selection identity cannot be empty.", nameof(identity));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(normalizedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        Identity = identity;
        AssetType = assetType;
        DisplayName = displayName;
        NormalizedName = normalizedName;
        Access = access;
        Origin = origin;
        ProviderZone = providerZone;
        ProviderId = providerId;
        HasEditor = hasEditor;
    }

    public WorkbenchAssetSelectionIdentity Identity { get; }

    public WorkbenchAssetSelectionSource Source => Identity.Source;

    public XAssetType AssetType { get; }

    public string DisplayName { get; }

    public string NormalizedName { get; }

    public WorkspaceAssetAccess Access { get; }

    public string Origin { get; }

    public string? ProviderZone { get; }

    /// <summary>
    /// Active provider identity copied by the Asset Pool navigator. It lets
    /// the center route aliases and duplicate names without retaining a pool
    /// slot or runtime asset.
    /// </summary>
    public XAssetProviderId? ProviderId { get; }

    public bool HasEditor { get; }
}
