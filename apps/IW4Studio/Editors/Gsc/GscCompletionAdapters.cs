using System.ComponentModel;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Material.Icons;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Thin AvaloniaEdit adapter over one language-service completion suggestion.
/// It owns no workspace or editor state.
/// </summary>
public sealed class GscCompletionData : ICompletionData
{
    private readonly string _insertionText;

    public GscCompletionData(
        string insertionText,
        string displayText,
        string description,
        GscEditorCompletionKind kind,
        string? filterText = null,
        double priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(insertionText);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (filterText is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(filterText);

        _insertionText = insertionText;
        Text = filterText ?? insertionText;
        DisplayText = displayText;
        DetailText = description;
        (IconKind, KindLabel) = GetPresentation(kind);
        Content = displayText;
        Description = new GscCompletionDescription(
            displayText,
            KindLabel,
            description,
            IconKind);
        Priority = priority;
    }

    public IImage? Image => null;

    public string Text { get; }

    public object Content { get; }

    public object Description { get; }

    public double Priority { get; }

    public string DisplayText { get; }

    public string DetailText { get; }

    public MaterialIconKind IconKind { get; }

    public string KindLabel { get; }

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        ArgumentNullException.ThrowIfNull(completionSegment);
        textArea.Document.Replace(completionSegment, _insertionText);
    }

    private static (MaterialIconKind Icon, string Label) GetPresentation(
        GscEditorCompletionKind kind) => kind switch
    {
        GscEditorCompletionKind.Function =>
            (MaterialIconKind.Function, "Function"),
        GscEditorCompletionKind.ObservedFunction =>
            (MaterialIconKind.FunctionVariant, "Observed function"),
        GscEditorCompletionKind.Field =>
            (MaterialIconKind.VariableBox, "Field"),
        GscEditorCompletionKind.BuiltIn =>
            (MaterialIconKind.PropertyTag, "Built-in"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

/// <summary>Presentation model for the completion details card.</summary>
public sealed record GscCompletionDescription(
    string Title,
    string KindLabel,
    string DetailText,
    MaterialIconKind IconKind);

/// <summary>Presentation pair for one callable overload.</summary>
public sealed record GscOverloadItem
{
    public GscOverloadItem(object header, object content)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public object Header { get; }

    public object Content { get; }
}

/// <summary>AvaloniaEdit overload provider backed by an immutable snapshot.</summary>
public sealed class GscOverloadProvider : IOverloadProvider
{
    private readonly IReadOnlyList<GscOverloadItem> _items;
    private int _selectedIndex;

    public GscOverloadProvider(IEnumerable<GscOverloadItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        GscOverloadItem[] copiedItems = items.ToArray();
        if (copiedItems.Length == 0)
        {
            throw new ArgumentException(
                "Signature help requires at least one overload.",
                nameof(items));
        }
        if (copiedItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Signature help cannot contain a null overload.",
                nameof(items));
        }

        _items = Array.AsReadOnly(copiedItems);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if ((uint)value >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value)
                return;

            _selectedIndex = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentContent)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentIndexText)));
        }
    }

    public int Count => _items.Count;

    public object CurrentHeader => _items[SelectedIndex].Header;

    public object CurrentContent => _items[SelectedIndex].Content;

    public string CurrentIndexText => Count == 1
        ? string.Empty
        : $"{SelectedIndex + 1} of {Count}";
}
