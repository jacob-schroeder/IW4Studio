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
        string? providerZone,
        bool hasEditor)
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
        HasEditor = hasEditor;
    }

    public TargetZoneRowIdentity Identity { get; }

    /// <summary>The row's current position in the authored target document.</summary>
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

    public bool HasEditor { get; }

    public string TypeName => AssetType.ToString();

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
            hasEditor: HasEditor);
}

/// <summary>
/// Read-only type grouping for tree-style navigator presentation.
/// Types and rows are ordered by display name; <see cref="FastFileAssetsNavigatorSnapshot.Rows"/>
/// remains the current document-order authority.
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
            string.Empty,
            group.Count.ToString("N0"),
            isGroup: true,
            Array.AsReadOnly(group.Rows.Select(ForRow).ToArray()),
            row: null);

    private static FastFileAssetNavigatorNode ForRow(
        FastFileAssetNavigatorRow row) =>
        new(
            row.DisplayName,
            row.Access.ToString(),
            string.Empty,
            isGroup: false,
            Array.Empty<FastFileAssetNavigatorNode>(),
            row);
}

/// <summary>
/// Detached copy of the current target document rows. Dependency-only catalog
/// entries and runtime-only pool slots are excluded.
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
        TargetZoneDocument document,
        Func<XAssetType, bool> hasDesktopEditor)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Capture(document.Rows, hasDesktopEditor);
    }

    private static FastFileAssetsNavigatorSnapshot Capture(
        IEnumerable<WorkspaceAssetCatalogEntry> entries,
        Func<XAssetType, bool> hasDesktopEditor)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(hasDesktopEditor);

        FastFileAssetNavigatorRow[] rows = entries
            .Select((entry, sourceIndex) =>
                Project(entry, sourceIndex, hasDesktopEditor))
            .ToArray();
        return new FastFileAssetsNavigatorSnapshot(rows);
    }

    private static FastFileAssetNavigatorRow Project(
        WorkspaceAssetCatalogEntry entry,
        int sourceIndex,
        Func<XAssetType, bool> hasDesktopEditor)
    {
        TargetZoneRowIdentity identity = entry.TargetRowIdentity
            ?? throw new InvalidDataException(
                "The target asset navigator received a dependency-only catalog entry.");
        string displayName = entry.OriginalName ?? StructuralRowName(entry.Origin);
        string? providerZone = entry.ResolvedProviderZone?.LogicalZoneName;

        return new FastFileAssetNavigatorRow(
            identity,
            sourceIndex,
            entry.AssetType,
            displayName,
            entry.NormalizedName ?? string.Empty,
            entry.Origin,
            entry.Access,
            entry.ContentSource,
            entry.HeaderKind,
            entry.RawHeader,
            providerZone,
            hasDesktopEditor(entry.AssetType) &&
            entry.ContentSource != WorkspaceAssetContentSource.Unavailable &&
            entry.Origin is not WorkspaceAssetOrigin.NullRow and
                not WorkspaceAssetOrigin.OpaqueRow);
    }

    private static string StructuralRowName(WorkspaceAssetOrigin origin) =>
        origin switch
        {
            WorkspaceAssetOrigin.NullRow => "<null row>",
            WorkspaceAssetOrigin.OpaqueRow => "<opaque row>",
            _ => "<unnamed asset>"
        };
}
