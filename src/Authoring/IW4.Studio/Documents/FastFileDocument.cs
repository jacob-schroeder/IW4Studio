using IW4.Game.Database;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;
using IW4.Linker.Packaging;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// The target as loaded, including its wire order and the definitions contributed by this zone.
/// </summary>
public sealed class FastFileDocument
{
    private readonly Guid _documentId = Guid.NewGuid();
    private readonly FastFileDocumentOpenRequest? _request;
    private readonly string? _sourcePath;
    private readonly LoadedXZone? _loadedZone;
    private readonly Dictionary<XAssetProviderId, int> _editableProviderIndices;

    internal FastFileDocument(
        FastFileDocumentOpenRequest request,
        WorkspaceZone targetZone,
        IReadOnlyList<XAssetProviderContribution> providers,
        LinkAssetPool? preparedAssets = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetZone);
        ArgumentNullException.ThrowIfNull(providers);
        if (!targetZone.IsTarget)
            throw new ArgumentException("The document target must be marked as target.", nameof(targetZone));

        _request = request;
        _sourcePath = targetZone.PhysicalPath;
        LoadedXZone target = targetZone.LoadResult;
        _loadedZone = target;
        InitialLinkRequest = new ZoneLinkRequest(
            preparedAssets is null
                ? new Lazy<LinkAssetPool>(() => target.PrepareTargetProviders(providers))
                : new Lazy<LinkAssetPool>(preparedAssets),
            target.FreezeLinkRoots(),
            target.Header.LanguageMask,
            target.Header.SelectedLanguageMask,
            target.Context.ZoneScriptStrings.UnreferencedValues);
        InitialHeaderMetadata = DbHeaderAuthoringMetadata.FromHeader(target.Header);
        _editableProviderIndices = providers
            .Select((provider, index) => (provider, index))
            .Where(item => !item.provider.IsReferencePlaceholder && item.provider.AssetType is
                (XAssetType.PhysPreset or XAssetType.PhysCollmap or XAssetType.XModelSurfs or
                 XAssetType.Tracer or XAssetType.SndCurve or XAssetType.LoadedSound or
                 XAssetType.VertexShader or XAssetType.PixelShader))
            .GroupBy(item => AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(item.provider.AssetType),
                LoadedZoneLinkExtensions.TargetProviderName(item.provider)))
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToDictionary(item => item.provider.Id, item => item.index);
    }

    internal FastFileDocument(
        ZoneLinkRequest initialLinkRequest,
        DbHeaderAuthoringMetadata initialHeaderMetadata)
    {
        InitialLinkRequest = initialLinkRequest ??
            throw new ArgumentNullException(nameof(initialLinkRequest));
        InitialHeaderMetadata = initialHeaderMetadata ??
            throw new ArgumentNullException(nameof(initialHeaderMetadata));
        _editableProviderIndices = [];
    }

    public bool IsBlank => _loadedZone is null;

    public Guid DocumentId => _documentId;

    public FastFileDocumentOpenRequest Request => _request ??
        throw new InvalidOperationException("A blank fastfile document has no open request.");

    public string SourcePath => _sourcePath ??
        throw new InvalidOperationException("A blank fastfile document has no source path.");

    public LoadedXZone LoadedZone => _loadedZone ??
        throw new InvalidOperationException("A blank fastfile document has no loaded zone.");

    internal int? TargetProviderIndex(XAssetProviderId id) =>
        _editableProviderIndices.TryGetValue(id, out int index) ? index : null;

    internal LinkAssetProvider TargetProviderAt(int index) => InitialLinkRequest.Assets.Providers[index];

    /// <summary>The target's ordered roots, script strings, and canonical linker inputs.</summary>
    public ZoneLinkRequest InitialLinkRequest { get; }

    /// <summary>The immutable authored header values captured at open or blank creation.</summary>
    internal DbHeaderAuthoringMetadata InitialHeaderMetadata { get; }

    /// <summary>
    /// The loader-frozen symbolic input for unchanged source-layout replay.
    /// It is not a canonical asset-link input.
    /// </summary>
    public ZoneObjectFile ZoneObjectFile => _loadedZone is { } zone
        ? zone.FreezeZoneObjectFile()
        :
        throw new InvalidOperationException(
            "A blank or runtime-only dependency document has no source-layout object.");
}
