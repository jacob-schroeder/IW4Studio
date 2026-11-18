using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using IW4.Render.Shaders;

namespace IW4.Studio.Desktop.Editors.Shader;

public sealed partial class ShaderEditorView : UserControl
{
    private ShaderEditorViewModel? _model;

    public ShaderEditorView()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateSyntaxHighlighting();
        UpdateSyntaxHighlighting();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.PropertyChanged -= ModelChanged;
            _model = DataContext as ShaderEditorViewModel;
            if (_model is not null) _model.PropertyChanged += ModelChanged;
            CodeEditor.Text = _model?.Source ?? string.Empty;
            CodeEditor.Document.UndoStack.ClearAll();
        };
    }

    private void UpdateSyntaxHighlighting() =>
        CodeEditor.SyntaxHighlighting = CgSyntaxHighlighting.ForTheme(ActualThemeVariant == ThemeVariant.Dark);

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShaderEditorViewModel.Source) && _model is not null &&
            CodeEditor.Text != _model.Source)
            CodeEditor.Text = _model.Source;
    }

    private void Source_Changed(object? sender, EventArgs args)
    {
        if (_model is not null) _model.Source = CodeEditor.Text;
    }

    private async void Compile_Click(object? sender, RoutedEventArgs args)
    {
        if (_model is not null) await _model.CompileAndApplyAsync();
    }

    private static readonly FilePickerFileType CgFiles = new("Cg source") { Patterns = ["*.cg", "*.CG"] };
    private static readonly UTF8Encoding SourceEncoding = new(false, true);

    private async void LoadSource_Click(object? sender, RoutedEventArgs args)
    {
        if (_model is not { IsReadOnly: false } model || TopLevel.GetTopLevel(this) is not { } window) return;
        string initial = model.Source;
        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Cg source", FileTypeFilter = [CgFiles], AllowMultiple = false
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, SourceEncoding, detectEncodingFromByteOrderMarks: true);
            string text = await reader.ReadToEndAsync();
            if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
                throw new InvalidDataException("Choose a Cg text file. This file contains binary data.");
            if (model != _model || model.IsReadOnly || initial != model.Source)
            {
                model.Messages = "The source changed while the file was open. Load it again to replace the current edit.";
                return;
            }
            // One document edit lets the user undo a load before applying it.
            CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, text);
            model.Messages = $"Loaded {files[0].Name}.";
        }
        catch (OperationCanceledException) { }
        catch (DecoderFallbackException)
        {
            model.Messages = "Choose a Cg text file. This file contains binary data or an unsupported text encoding.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            model.Messages = exception.Message;
        }
    }

    private async void ExportSource_Click(object? sender, RoutedEventArgs args)
    {
        if (_model is not { } model || TopLevel.GetTopLevel(this) is not { } window) return;
        string source = model.Source;
        try
        {
            var destination = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Cg source", DefaultExtension = "cg", FileTypeChoices = [CgFiles],
                SuggestedFileName = SaveFilePickerName.WithoutDefaultExtension(Path.ChangeExtension(Path.GetFileName(model.Name), ".cg"), "cg"),
                ShowOverwritePrompt = true
            });
            if (destination is null) return;
            await AssetExportFile.WriteAsync(destination, SourceEncoding.GetBytes(source));
            model.Messages = $"Exported {destination.Name}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            model.Messages = exception.Message;
        }
    }

    private void Restore_Click(object? sender, RoutedEventArgs args) => _model?.RestoreSource();

    private void RefreshPasses_Click(object? sender, RoutedEventArgs args) => _model?.RefreshPasses();

    private void FindOutput_Click(object? sender, RoutedEventArgs args)
    {
        if (_model?.PrimaryOutput is { } output)
            SelectSource(_model.PrimaryOutputOffset, output.Length);
    }

    private void NextGap_Click(object? sender, RoutedEventArgs args)
    {
        string text = CodeEditor.Text;
        string marker = RsxCgSourceDecompiler.RecoveryDirective;
        int start = CodeEditor.SelectionStart + CodeEditor.SelectionLength;
        int offset = text.IndexOf(marker, start, StringComparison.Ordinal);
        if (offset < 0) offset = text.IndexOf(marker, StringComparison.Ordinal);
        if (offset < 0) return;
        int end = text.IndexOfAny(['\r', '\n'], offset);
        SelectSource(offset, (end >= 0 ? end : text.Length) - offset);
    }

    private void SelectSource(int offset, int length)
    {
        if (offset < 0) return;
        CodeEditor.TextArea.Focus();
        CodeEditor.Select(offset, length);
        var location = CodeEditor.Document.GetLocation(offset);
        CodeEditor.ScrollTo(location.Line, location.Column);
    }
}
