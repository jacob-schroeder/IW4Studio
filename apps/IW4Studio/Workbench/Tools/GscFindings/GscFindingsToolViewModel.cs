using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.GscFindings;

public sealed class GscFindingActivatedEventArgs : EventArgs
{
    public GscFindingActivatedEventArgs(EditorSourceDiagnostic finding) =>
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));

    public EditorSourceDiagnostic Finding { get; }
}

/// <summary>Findings from the selected editor's most recent GSC syntax check.</summary>
public sealed class GscFindingsToolViewModel : ObservableObject
{
    private IReadOnlyList<EditorSourceDiagnostic> _items = [];
    private string _documentName = string.Empty;

    public event EventHandler<GscFindingActivatedEventArgs>? FindingActivated;

    public IReadOnlyList<EditorSourceDiagnostic> Items
    {
        get => _items;
        private set
        {
            if (!SetProperty(ref _items, value))
                return;

            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ResultText));
        }
    }

    public string DocumentName
    {
        get => _documentName;
        private set
        {
            if (!SetProperty(ref _documentName, value))
                return;

            OnPropertyChanged(nameof(ResultText));
        }
    }

    public int Count => Items.Count;

    public bool HasItems => Count != 0;

    public string ResultText => DocumentName.Length == 0
        ? "No GSC findings for the selected editor."
        : Count == 1
            ? $"1 finding in '{DocumentName}'"
            : $"{Count:N0} findings in '{DocumentName}'";

    public void Replace(
        string documentName,
        IEnumerable<EditorSourceDiagnostic> findings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        ArgumentNullException.ThrowIfNull(findings);

        EditorSourceDiagnostic[] snapshot = findings.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(finding => finding is null))
        {
            throw new ArgumentException(
                "A GSC findings snapshot must contain at least one non-null finding.",
                nameof(findings));
        }

        DocumentName = documentName;
        Items = Array.AsReadOnly(snapshot);
    }

    public void Clear()
    {
        if (DocumentName.Length == 0 && Items.Count == 0)
            return;

        DocumentName = string.Empty;
        Items = [];
    }

    public void Activate(EditorSourceDiagnostic finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (!Items.Any(item => ReferenceEquals(item, finding)))
            return;

        FindingActivated?.Invoke(this, new GscFindingActivatedEventArgs(finding));
    }
}
