using IW4.Gsc.Workspace;

namespace IW4.Studio.Desktop.Workbench.Tools.GscUsages;

/// <summary>One immutable row in the GSC Usages tool.</summary>
public sealed record GscUsagePresentationItem
{
    public GscUsagePresentationItem(
        GscSourceLocation location,
        string documentPath,
        string locationText,
        string containerName,
        string previewText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationText);
        ArgumentNullException.ThrowIfNull(containerName);
        ArgumentNullException.ThrowIfNull(previewText);

        Location = location;
        DocumentPath = documentPath;
        LocationText = locationText;
        ContainerName = containerName;
        PreviewText = previewText;
    }

    public GscSourceLocation Location { get; }

    public string DocumentPath { get; }

    public string LocationText { get; }

    public string ContainerName { get; }

    public string PreviewText { get; }
}

/// <summary>Atomic result set presented for one symbol lookup.</summary>
public sealed record GscUsagePresentation
{
    private readonly IReadOnlyList<GscUsagePresentationItem> _items;

    public GscUsagePresentation(
        string symbolName,
        IEnumerable<GscUsagePresentationItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolName);
        ArgumentNullException.ThrowIfNull(items);

        GscUsagePresentationItem[] copiedItems = items.ToArray();
        if (copiedItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "A GSC usage presentation cannot contain a null item.",
                nameof(items));
        }

        SymbolName = symbolName;
        _items = Array.AsReadOnly(copiedItems);
    }

    public string SymbolName { get; }

    public IReadOnlyList<GscUsagePresentationItem> Items => _items;
}
