namespace IW4.Studio.Desktop.Editors.Inspector;

/// <summary>
/// Explicit, reflection-free projection of the entity selected inside an
/// asset editor.
/// </summary>
public sealed class InspectorSelectionViewModel
{
    public InspectorSelectionViewModel(
        string title,
        string kind,
        IEnumerable<InspectorSectionViewModel> sections,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(sections);

        Title = title;
        Kind = kind;
        Description = description;
        Sections = Array.AsReadOnly(sections.ToArray());
    }

    public string Title { get; }

    public string Kind { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public IReadOnlyList<InspectorSectionViewModel> Sections { get; }
}

/// <summary>One ordered group of typed property rows.</summary>
public sealed class InspectorSectionViewModel
{
    public InspectorSectionViewModel(
        string title,
        IEnumerable<InspectorPropertyRowViewModel> rows,
        bool isExpanded = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(rows);

        Title = title;
        Rows = Array.AsReadOnly(rows.ToArray());
        IsExpanded = isExpanded;
    }

    public string Title { get; }

    public IReadOnlyList<InspectorPropertyRowViewModel> Rows { get; }

    public bool IsExpanded { get; }
}
