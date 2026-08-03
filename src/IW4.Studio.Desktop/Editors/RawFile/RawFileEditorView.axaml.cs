using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Highlighting;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.RawFile;

public sealed partial class RawFileEditorView : UserControl, IEditorTextNavigator
{
    private static readonly TimeSpan AutomaticCompletionDelay =
        TimeSpan.FromMilliseconds(100);

    private TextEditor? _contentEditor;
    private RawFileEditorViewModel? _viewModel;
    private bool _isSynchronizingEditorText;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _signatureWindow;
    private GscDiagnosticRenderer? _diagnosticRenderer;
    private CancellationTokenSource? _gscIntelligenceCancellation;

    public RawFileEditorView()
    {
        AvaloniaXamlLoader.Load(this);
        _contentEditor = this.FindControl<TextEditor>("ContentEditor");
        if (_contentEditor is not null)
            _contentEditor.TextArea.TextEntered += ContentEditor_TextEntered;
        DataContextChanged += RawFileEditorView_DataContextChanged;
    }

    private async void ImportPayloadButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is RawFileEditorViewModel viewModel)
            await viewModel.ImportPayloadAsync();
    }

    private async void AnalyzeGscButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RawFileEditorViewModel viewModel)
            await viewModel.AnalyzeGscAsync();
    }

    public void NavigateTo(EditorTextLocation location)
    {
        TextEditor? editor = _contentEditor;
        if (editor is null)
            return;

        int textLength = editor.Text.Length;
        int start = Math.Min(location.Start, textLength);
        int end = Math.Min(location.End, textLength);

        editor.TextArea.Focus();
        editor.Select(start, end - start);
        if (editor.Document is { } document)
        {
            var line = document.GetLineByOffset(start);
            editor.ScrollTo(line.LineNumber, start - line.Offset + 1);
        }
    }

    private void ContentEditor_TextChanged(object? sender, EventArgs e)
    {
        CancelGscIntelligenceRequest();
        if (!_isSynchronizingEditorText &&
            sender is TextEditor editor &&
            _viewModel is not null)
        {
            _viewModel.PayloadInput = editor.Text;
        }
    }

    private void GoToGscDefinitionMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_contentEditor is not null)
            _viewModel?.GoToGscDefinition(_contentEditor.TextArea.Caret.Offset);
    }

    private async void FindGscUsagesMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_contentEditor is not null && _viewModel is { } viewModel)
        {
            int caretOffset = _contentEditor.TextArea.Caret.Offset;
            await viewModel.FindGscUsagesAsync(caretOffset);
        }
    }

    private async void ContentEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (_contentEditor is not null && _viewModel is { } viewModel)
            {
                int caretOffset = _contentEditor.TextArea.Caret.Offset;
                await viewModel.FindGscUsagesAsync(caretOffset);
            }
            return;
        }

        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            if (_contentEditor is not null)
            {
                _viewModel?.GoToGscDefinition(
                    _contentEditor.TextArea.Caret.Offset);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await ShowGscCompletionAsync(isAutomatic: false);
        }
    }

    private async void ContentEditor_TextEntered(
        object? sender,
        TextInputEventArgs e)
    {
        if (_contentEditor is null || _viewModel?.IsGscSource != true)
            return;

        string text = e.Text ?? string.Empty;
        if (text is "(" or ",")
        {
            await ShowGscSignatureHelpAsync();
            return;
        }
        if (text == ")")
        {
            CancelGscIntelligenceRequest();
            CloseSignatureWindow();
            return;
        }

        int caret = _contentEditor.TextArea.Caret.Offset;
        if (text == ":" &&
            caret >= 2 &&
            _contentEditor.Text.AsSpan(caret - 2, 2).SequenceEqual("::"))
        {
            await ShowGscCompletionAsync(isAutomatic: false);
            return;
        }

        if (_completionWindow is null &&
            EndsWithIdentifierCharacter(text))
        {
            await ShowGscCompletionAsync(isAutomatic: true);
        }
    }

    private async Task ShowGscCompletionAsync(bool isAutomatic)
    {
        if (_contentEditor is not { } editor ||
            _viewModel is not { } viewModel ||
            !viewModel.IsGscSource)
        {
            return;
        }

        int caret = editor.TextArea.Caret.Offset;
        CancellationTokenSource cancellation = BeginGscIntelligenceRequest();
        try
        {
            if (isAutomatic)
                await Task.Delay(AutomaticCompletionDelay, cancellation.Token);

            IReadOnlyList<GscEditorCompletion> suggestions = isAutomatic
                ? await viewModel.GetAutomaticGscFunctionCompletionsAsync(
                    caret,
                    cancellation.Token)
                : await viewModel.GetGscFunctionCompletionsAsync(
                    caret,
                    cancellation.Token);
            if (!IsCurrentGscIntelligenceRequest(
                    cancellation,
                    editor,
                    viewModel,
                    caret))
            {
                return;
            }

            CloseCompletionWindow();
            if (suggestions.Count == 0)
                return;

            ShowGscCompletionWindow(editor, suggestions, caret);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer edit or request owns the editor UI.
        }
        finally
        {
            CompleteGscIntelligenceRequest(cancellation);
        }
    }

    private void ShowGscCompletionWindow(
        TextEditor editor,
        IReadOnlyList<GscEditorCompletion> suggestions,
        int caret)
    {
        int replacementStart = suggestions[0].ReplacementStart;
        if (replacementStart < 0 ||
            replacementStart > caret ||
            caret > editor.Text.Length ||
            suggestions.Any(suggestion =>
                suggestion.ReplacementStart != replacementStart))
        {
            return;
        }

        var window = new CompletionWindow(editor.TextArea)
        {
            StartOffset = replacementStart,
            EndOffset = caret
        };
        foreach (GscEditorCompletion suggestion in suggestions)
        {
            window.CompletionList.CompletionData.Add(new GscCompletionData(
                suggestion.InsertionText,
                suggestion.DisplayText,
                suggestion.Description,
                filterText: GetCompletionFilterText(
                    suggestion.InsertionText)));
        }
        window.CompletionList.SelectItem(
            editor.Text[replacementStart..caret]);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
                _completionWindow = null;
        };
        _completionWindow = window;
        window.Show();
    }

    private static bool EndsWithIdentifierCharacter(string text) =>
        text.Length > 0 &&
        (char.IsLetterOrDigit(text[^1]) || text[^1] == '_');

    private static string GetCompletionFilterText(string insertionText)
    {
        int separator = insertionText.LastIndexOf(
            "::",
            StringComparison.Ordinal);
        return separator < 0
            ? insertionText
            : insertionText[(separator + 2)..];
    }

    private async Task ShowGscSignatureHelpAsync()
    {
        if (_contentEditor is not { } editor ||
            _viewModel is not { } viewModel ||
            !viewModel.IsGscSource)
        {
            return;
        }

        int caret = editor.TextArea.Caret.Offset;
        CancellationTokenSource cancellation = BeginGscIntelligenceRequest();
        try
        {
            GscEditorSignatureHelp? help =
                await viewModel.GetGscSignatureHelpAsync(
                    caret,
                    cancellation.Token);
            if (!IsCurrentGscIntelligenceRequest(
                    cancellation,
                    editor,
                    viewModel,
                    caret))
            {
                return;
            }

            CloseSignatureWindow();
            if (help is null)
                return;

            var provider = new GscOverloadProvider(help.Signatures.Select(
                signature => new GscOverloadItem(
                    signature.Header,
                    signature.ActiveParameterText)));
            var window = new OverloadInsightWindow(editor.TextArea)
            {
                Provider = provider
            };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_signatureWindow, window))
                    _signatureWindow = null;
            };
            _signatureWindow = window;
            window.Show();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer edit or request owns the editor UI.
        }
        finally
        {
            CompleteGscIntelligenceRequest(cancellation);
        }
    }

    private CancellationTokenSource BeginGscIntelligenceRequest()
    {
        CancelGscIntelligenceRequest();
        var cancellation = new CancellationTokenSource();
        _gscIntelligenceCancellation = cancellation;
        return cancellation;
    }

    private void CancelGscIntelligenceRequest()
    {
        CancellationTokenSource? cancellation = _gscIntelligenceCancellation;
        _gscIntelligenceCancellation = null;
        cancellation?.Cancel();
    }

    private bool IsCurrentGscIntelligenceRequest(
        CancellationTokenSource cancellation,
        TextEditor editor,
        RawFileEditorViewModel viewModel,
        int caret) =>
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(_gscIntelligenceCancellation, cancellation) &&
        ReferenceEquals(_contentEditor, editor) &&
        ReferenceEquals(_viewModel, viewModel) &&
        editor.TextArea.Caret.Offset == caret;

    private void CompleteGscIntelligenceRequest(
        CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_gscIntelligenceCancellation, cancellation))
            _gscIntelligenceCancellation = null;

        cancellation.Dispose();
    }

    private void CloseCompletionWindow()
    {
        _completionWindow?.Hide();
        _completionWindow = null;
    }

    private void CloseSignatureWindow()
    {
        _signatureWindow?.Hide();
        _signatureWindow = null;
    }

    private async void ExportPayloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RawFileEditorViewModel viewModel)
            return;

        string exported = viewModel.ExportPayload();
        if (viewModel.PayloadMode is not null &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(exported);
        }
    }

    private void ClearBufferButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as RawFileEditorViewModel)?.ClearBuffer();

    private void RevertDraftButton_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as RawFileEditorViewModel)?.RevertDraft();

    private async void ReplaceFromFileButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not RawFileEditorViewModel viewModel ||
            !viewModel.CanReplaceFromFile ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files =
                await storageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Replace binary RawFile",
                        AllowMultiple = false,
                        FileTypeFilter = [FilePickerFileTypes.All]
                    });
            IStorageFile? file = files.FirstOrDefault();
            if (file is null)
                return;

            await using Stream stream = await file.OpenReadAsync();
            using var content = new MemoryStream();
            await stream.CopyToAsync(content);
            viewModel.ReplaceFromFile(content.ToArray(), file.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            viewModel.ReportReplacementFailure(
                $"Could not read the replacement file: {exception.Message}");
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachDiagnosticRenderer();
        RebindViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGscIntelligenceRequest();
        CloseCompletionWindow();
        CloseSignatureWindow();
        DetachViewModel();
        DetachDiagnosticRenderer();
        base.OnDetachedFromVisualTree(e);
    }

    private void RawFileEditorView_DataContextChanged(object? sender, EventArgs e) =>
        RebindViewModel();

    private void RebindViewModel()
    {
        RawFileEditorViewModel? viewModel = DataContext as RawFileEditorViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        SynchronizeEditorText();
        ConfigureSyntaxHighlighting();
        RefreshDiagnosticMarkers();
    }

    private void DetachViewModel()
    {
        CancelGscIntelligenceRequest();
        CloseCompletionWindow();
        CloseSignatureWindow();
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = null;
        RefreshDiagnosticMarkers();
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(RawFileEditorViewModel.PayloadInput))
            SynchronizeEditorText();

        if (e.PropertyName is null or nameof(RawFileEditorViewModel.IsGscSource))
            ConfigureSyntaxHighlighting();

        if (e.PropertyName is null or
            nameof(RawFileEditorViewModel.SourceDiagnostics))
        {
            RefreshDiagnosticMarkers();
        }
    }

    private void ConfigureSyntaxHighlighting()
    {
        if (_contentEditor is not null)
        {
            _contentEditor.SyntaxHighlighting = _viewModel?.IsGscSource == true
                ? GscSyntaxHighlighting.Definition
                : null;
        }
    }

    private void AttachDiagnosticRenderer()
    {
        if (_contentEditor is null || _diagnosticRenderer is not null)
            return;

        var renderer = new GscDiagnosticRenderer(
            _contentEditor.TextArea.TextView);
        _contentEditor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        _diagnosticRenderer = renderer;
        RefreshDiagnosticMarkers();
    }

    private void DetachDiagnosticRenderer()
    {
        if (_contentEditor is null || _diagnosticRenderer is null)
            return;

        _contentEditor.TextArea.TextView.BackgroundRenderers.Remove(
            _diagnosticRenderer);
        _diagnosticRenderer.Dispose();
        _diagnosticRenderer = null;
    }

    private void RefreshDiagnosticMarkers() =>
        _diagnosticRenderer?.Replace(
            _viewModel?.IsGscSource == true
                ? _viewModel.SourceDiagnostics
                : []);

    private void SynchronizeEditorText()
    {
        if (_contentEditor is null)
            return;

        string payload = _viewModel?.PayloadInput ?? string.Empty;
        if (string.Equals(_contentEditor.Text, payload, StringComparison.Ordinal))
            return;

        _isSynchronizingEditorText = true;
        try
        {
            _contentEditor.Text = payload;
        }
        finally
        {
            _isSynchronizingEditorText = false;
        }
    }
}
