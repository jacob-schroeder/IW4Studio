using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>
/// Immutable Desktop projection of one serialized target-zone row.
/// </summary>
public sealed record FastFileAssetNavigatorRow
{
    internal FastFileAssetNavigatorRow(
        TargetZoneRowIdentity identity,
        int sourceIndex,
        XAssetType assetType,
        string displayName,
        string normalizedName,
        WorkspaceAssetOrigin origin,
        WorkspaceAssetAccess access,
        WorkspaceAssetContentSource contentSource,
        XAssetHeaderKind? headerKind,
        int? rawHeader,
        string? providerZone)
    {
        Identity = identity;
        SourceIndex = sourceIndex;
        AssetType = assetType;
        DisplayName = displayName;
        NormalizedName = normalizedName;
        Origin = origin;
        Access = access;
        ContentSource = contentSource;
        HeaderKind = headerKind;
        RawHeader = rawHeader;
        ProviderZone = providerZone;
    }

    public TargetZoneRowIdentity Identity { get; }

    /// <summary>The exact immutable target catalog/source position.</summary>
    public int SourceIndex { get; }

    public XAssetType AssetType { get; }

    public string DisplayName { get; }

    public string NormalizedName { get; }

    public WorkspaceAssetOrigin Origin { get; }

    public WorkspaceAssetAccess Access { get; }

    public WorkspaceAssetContentSource ContentSource { get; }

    public XAssetHeaderKind? HeaderKind { get; }

    public int? RawHeader { get; }

    public string? ProviderZone { get; }

    public string TypeName => AssetType.ToString();

    public string SourceIndexText => $"#{SourceIndex:N0}";

    public string Detail => string.IsNullOrWhiteSpace(ProviderZone)
        ? $"{TypeName} · {Access}"
        : $"{TypeName} · {ProviderZone}";

    public WorkbenchAssetSelection ToSelection() =>
        new(
            WorkbenchAssetSelectionIdentity.ForTargetRow(Identity),
            AssetType,
            DisplayName,
            NormalizedName,
            Access,
            Origin.ToString(),
            ProviderZone,
            hasEditor:
                AssetType == XAssetType.RawFile &&
                ContentSource != WorkspaceAssetContentSource.Unavailable &&
                Origin is not WorkspaceAssetOrigin.NullRow and
                    not WorkspaceAssetOrigin.OpaqueRow and
                    not WorkspaceAssetOrigin.OffsetAliasRow and
                    not WorkspaceAssetOrigin.UnsupportedRow);
}

/// <summary>
/// Read-only type grouping for tree-style navigator presentation.
/// Types and rows are ordered by their display names; <see cref="FastFileAssetsNavigatorSnapshot.Rows"/>
/// remains the source-order authority.
/// </summary>
public sealed record FastFileAssetNavigatorGroup(
    XAssetType AssetType,
    IReadOnlyList<FastFileAssetNavigatorRow> Rows)
{
    public string Name => AssetType.ToString();

    public int Count => Rows.Count;
}

/// <summary>
/// A homogeneous tree node keeps the Avalonia tree template small while retaining
/// the strongly typed row at asset leaves.
/// </summary>
public sealed class FastFileAssetNavigatorNode
{
    private FastFileAssetNavigatorNode(
        string name,
        string detail,
        string trailingText,
        bool isGroup,
        IReadOnlyList<FastFileAssetNavigatorNode> children,
        FastFileAssetNavigatorRow? row)
    {
        Name = name;
        Detail = detail;
        TrailingText = trailingText;
        IsGroup = isGroup;
        Children = children;
        Row = row;
    }

    public string Name { get; }

    public string Detail { get; }

    public string TrailingText { get; }

    public bool IsGroup { get; }

    public bool IsAsset => !IsGroup;

    public IReadOnlyList<FastFileAssetNavigatorNode> Children { get; }

    public FastFileAssetNavigatorRow? Row { get; }

    internal static FastFileAssetNavigatorNode ForGroup(
        FastFileAssetNavigatorGroup group) =>
        new(
            group.Name,
            $"{group.Count:N0} {(group.Count == 1 ? "asset" : "assets")}",
            group.Count.ToString("N0"),
            isGroup: true,
            Array.AsReadOnly(group.Rows.Select(ForRow).ToArray()),
            row: null);

    private static FastFileAssetNavigatorNode ForRow(
        FastFileAssetNavigatorRow row) =>
        new(
            row.DisplayName,
            row.Access.ToString(),
            row.SourceIndexText,
            isGroup: false,
            Array.Empty<FastFileAssetNavigatorNode>(),
            row);
}

/// <summary>
/// One-time copy of <see cref="WorkspaceAssetCatalog.TargetEntries"/>.
/// Dependency-only catalog entries and runtime-only pool slots are excluded.
/// </summary>
public sealed class FastFileAssetsNavigatorSnapshot
{
    private FastFileAssetsNavigatorSnapshot(
        IEnumerable<FastFileAssetNavigatorRow> rows)
    {
        Rows = Array.AsReadOnly(rows.ToArray());
    }

    public IReadOnlyList<FastFileAssetNavigatorRow> Rows { get; }

    public static FastFileAssetsNavigatorSnapshot Capture(
        FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        FastFileAssetNavigatorRow[] rows = workspace.AssetCatalog.TargetEntries
            .Select(Project)
            .ToArray();
        for (int index = 0; index < rows.Length; index++)
        {
            if (rows[index].SourceIndex != index)
            {
                throw new InvalidDataException(
                    "The target asset catalog no longer matches immutable source order.");
            }
        }

        return new FastFileAssetsNavigatorSnapshot(rows);
    }

    private static FastFileAssetNavigatorRow Project(
        WorkspaceAssetCatalogEntry entry)
    {
        TargetZoneRowIdentity identity = entry.TargetRowIdentity
            ?? throw new InvalidDataException(
                "The target asset navigator received a dependency-only catalog entry.");
        string displayName = entry.OriginalName ?? StructuralRowName(entry.Origin);
        string? providerZone = entry.ResolvedProviderZone?.LogicalZoneName;
        if (string.IsNullOrWhiteSpace(providerZone) &&
            entry.ProviderZone is { } providerHandle &&
            !providerHandle.IsNone)
        {
            providerZone = providerHandle.ToString();
        }

        return new FastFileAssetNavigatorRow(
            identity,
            identity.SerializedIndex,
            entry.AssetType,
            displayName,
            entry.NormalizedName ?? string.Empty,
            entry.Origin,
            entry.Access,
            entry.ContentSource,
            entry.HeaderKind,
            entry.RawHeader,
            providerZone);
    }

    private static string StructuralRowName(WorkspaceAssetOrigin origin) =>
        origin switch
        {
            WorkspaceAssetOrigin.NullRow => "<null row>",
            WorkspaceAssetOrigin.OpaqueRow => "<opaque row>",
            WorkspaceAssetOrigin.OffsetAliasRow => "<offset alias>",
            WorkspaceAssetOrigin.UnsupportedRow => "<unsupported row>",
            _ => "<unnamed asset>"
        };
}
