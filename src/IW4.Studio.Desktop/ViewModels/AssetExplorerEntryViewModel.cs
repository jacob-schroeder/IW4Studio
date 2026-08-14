using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Stable Desktop identity for one catalog entry. Target entries retain their
/// serialized row identity; dependency entries retain only their explicitly
/// workspace-local provider identity.
/// </summary>
public readonly record struct AssetExplorerItemIdentity
{
    private AssetExplorerItemIdentity(
        TargetZoneRowIdentity? targetRowIdentity,
        WorkspaceDependencyAssetIdentity? dependencyIdentity)
    {
        if ((targetRowIdentity is null) == (dependencyIdentity is null))
        {
            throw new ArgumentException(
                "An explorer item requires exactly one target-row or dependency identity.");
        }

        TargetRowIdentity = targetRowIdentity;
        DependencyIdentity = dependencyIdentity;
    }

    public TargetZoneRowIdentity? TargetRowIdentity { get; }

    public WorkspaceDependencyAssetIdentity? DependencyIdentity { get; }

    public static AssetExplorerItemIdentity From(WorkspaceAssetCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new AssetExplorerItemIdentity(entry.TargetRowIdentity, entry.DependencyIdentity);
    }
}

/// <summary>
/// Immutable Desktop projection of catalog provenance. All strings derive
/// from the catalog; it never queries runtime pool entries or addresses.
/// </summary>
public sealed class AssetExplorerEntryViewModel
{
    public AssetExplorerEntryViewModel(
        WorkspaceAssetCatalogEntry entry,
        bool hasBackendAdapter,
        bool hasDesktopView)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

        Identity = AssetExplorerItemIdentity.From(entry);
        HasBackendAdapter = hasBackendAdapter;
        HasDesktopView = hasDesktopView;
        Name = entry.OriginalName ?? StructuralRowName(entry.Origin);
        NormalizedName = entry.NormalizedName ?? string.Empty;
        AssetType = entry.AssetType;
        Origin = entry.Origin;
        Access = entry.Access;
        ProviderZone = entry.ResolvedProviderZone?.LogicalZoneName
            ?? string.Empty;
        OwnershipBadge = GetOwnershipBadge(entry.Origin);
        ResolutionBadge = GetResolutionBadge(entry, ProviderZone);
        HasUsableEditor = hasDesktopView &&
                          entry.HasDefinition &&
                          entry.ContentSource != WorkspaceAssetContentSource.Unavailable &&
                          entry.Origin is not WorkspaceAssetOrigin.NullRow and
                              not WorkspaceAssetOrigin.OpaqueRow;
        EditorBadge = HasUsableEditor ? "EDITOR AVAILABLE" : "EDITOR UNAVAILABLE";
        AccessBadge = entry.Access switch
        {
            WorkspaceAssetAccess.Editable => "EDITABLE",
            WorkspaceAssetAccess.ReadOnly => "READ ONLY",
            WorkspaceAssetAccess.ContentUnavailable => "CONTENT UNAVAILABLE",
            _ => throw new InvalidDataException($"Unknown workspace access '{entry.Access}'.")
        };
        Description = BuildDescription(entry, ResolutionBadge, EditorBadge);
        ToolTipText = string.Join(
            Environment.NewLine,
            $"Type: {AssetType}",
            $"Origin: {Origin}",
            $"Access: {AccessBadge}",
            $"Ownership: {OwnershipBadge}",
            $"Resolution: {ResolutionBadge}",
            $"Editor: {EditorBadge}");
    }

    public WorkspaceAssetCatalogEntry Entry { get; }

    public AssetExplorerItemIdentity Identity { get; }

    public XAssetType AssetType { get; }

    public WorkspaceAssetOrigin Origin { get; }

    public WorkspaceAssetAccess Access { get; }

    public string Name { get; }

    public string NormalizedName { get; }

    public string ProviderZone { get; }

    public bool HasBackendAdapter { get; }

    public bool HasDesktopView { get; }

    public bool HasUsableEditor { get; }

    public string OwnershipBadge { get; }

    public string ResolutionBadge { get; }

    public string EditorBadge { get; }

    public string AccessBadge { get; }

    public string Description { get; }

    public string ToolTipText { get; }

    public string Detail => string.IsNullOrEmpty(ProviderZone)
        ? $"{OwnershipBadge} · {AccessBadge}"
        : $"{OwnershipBadge} · {ProviderZone}";

    public string Icon => Origin switch
    {
        WorkspaceAssetOrigin.TargetOwnedDefinition => "◇",
        WorkspaceAssetOrigin.TargetResolvedReference => "↗",
        WorkspaceAssetOrigin.TargetUnresolvedReference => "?",
        WorkspaceAssetOrigin.DependencyOnly => "◆",
        WorkspaceAssetOrigin.NullRow => "∅",
        WorkspaceAssetOrigin.OpaqueRow => "◌",
        _ => "?"
    };

    public bool IsTargetRow => Identity.TargetRowIdentity is not null;

    private static string StructuralRowName(WorkspaceAssetOrigin origin) => origin switch
    {
        WorkspaceAssetOrigin.NullRow => "<null row>",
        WorkspaceAssetOrigin.OpaqueRow => "<opaque row>",
        _ => "<unnamed asset>"
    };

    private static string GetOwnershipBadge(WorkspaceAssetOrigin origin) => origin switch
    {
        WorkspaceAssetOrigin.TargetOwnedDefinition => "TARGET DEFINITION",
        WorkspaceAssetOrigin.TargetResolvedReference or WorkspaceAssetOrigin.TargetUnresolvedReference => "TARGET REFERENCE",
        WorkspaceAssetOrigin.DependencyOnly => "DEPENDENCY ONLY",
        WorkspaceAssetOrigin.NullRow or WorkspaceAssetOrigin.OpaqueRow => "TARGET STRUCTURAL",
        _ => throw new InvalidDataException($"Unknown catalog origin '{origin}'.")
    };

    private static string GetResolutionBadge(
        WorkspaceAssetCatalogEntry entry,
        string providerZone) => entry.Origin switch
    {
        WorkspaceAssetOrigin.TargetOwnedDefinition => "AUTHORED BASELINE",
        WorkspaceAssetOrigin.TargetResolvedReference => string.IsNullOrEmpty(providerZone)
            ? "DEPENDENCY CONTENT"
            : $"RESOLVED · {providerZone}",
        WorkspaceAssetOrigin.TargetUnresolvedReference => "UNRESOLVED CONTENT",
        WorkspaceAssetOrigin.DependencyOnly => string.IsNullOrEmpty(providerZone)
            ? "DEPENDENCY CONTENT"
            : $"DEPENDENCY · {providerZone}",
        WorkspaceAssetOrigin.NullRow or WorkspaceAssetOrigin.OpaqueRow => "STRUCTURAL ONLY",
        _ => throw new InvalidDataException($"Unknown catalog origin '{entry.Origin}'.")
    };

    private static string BuildDescription(
        WorkspaceAssetCatalogEntry entry,
        string resolution,
        string editor) =>
        $"{entry.Origin} · {entry.Access}. {resolution}. {editor}.";
}
