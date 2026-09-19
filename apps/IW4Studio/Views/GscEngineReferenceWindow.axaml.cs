using Avalonia.Controls;
using AvaloniaEdit;
using IW4.Gsc.BuiltIns;
using IW4.Studio.Desktop.Editors.Gsc;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Reusable read-only view over the generated executable-owned GSC surface.
/// One window is retained per workspace and subsequent definitions select the
/// corresponding registration in the same document.
/// </summary>
internal sealed partial class GscEngineReferenceWindow : Window
{
    private readonly Iw4GscBuiltInReferenceDocument _document;
    private readonly TextEditor _editor;

    internal GscEngineReferenceWindow(Iw4GscBuiltInDefinition builtIn)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        InitializeComponent();
        Icon = AppIcon.Create();
        _document = Iw4GscBuiltInCatalog.Multiplayer;
        _editor = this.FindControl<TextEditor>("ReferenceEditor")
            ?? throw new InvalidOperationException(
                "The engine reference editor control was not created.");
        _editor.SyntaxHighlighting = GscSyntaxHighlighting.Definition;
        _editor.Text = _document.Text;
        Opened += (_, _) => NavigateTo(builtIn);
    }

    internal void NavigateTo(Iw4GscBuiltInDefinition builtIn)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        if (!_document.Definitions.Contains(builtIn) ||
            builtIn.ReferenceSpan.End > _editor.Text.Length)
        {
            throw new ArgumentException(
                "The built-in definition does not belong to this reference document.",
                nameof(builtIn));
        }

        int start = builtIn.ReferenceSpan.Start;
        _editor.TextArea.Focus();
        _editor.Select(start, builtIn.ReferenceSpan.Length);
        if (_editor.Document is { } editorDocument)
        {
            var line = editorDocument.GetLineByOffset(start);
            _editor.ScrollTo(line.LineNumber, start - line.Offset + 1);
        }
    }
}
