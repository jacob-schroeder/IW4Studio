using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Studio.Desktop.Editors.Sound;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.LoadedSounds;

public sealed partial class LoadedSoundEditorView : UserControl
{
    private LoadedSoundEditorViewModel? _editor;
    private bool _choosing;

    public LoadedSoundEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindEditor();
        DetachedFromVisualTree += (_, _) => _editor?.StopPlayback();
        OriginalWave.SeekRequested += (_, time) => _editor?.Seek(_editor.Original, time);
        ReplacementWave.SeekRequested += (_, time) => _editor?.Seek(_editor.Replacement, time);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
        Panes.SizeChanged += (_, e) => ArrangePanes(e.NewSize.Width);
    }

    private void BindEditor()
    {
        if (_editor is not null)
        {
            _editor.Changed -= Refresh;
            _editor.PlayheadChanged -= RefreshPlayheads;
            _editor.StopPlayback();
        }
        _editor = DataContext as LoadedSoundEditorViewModel;
        if (_editor is null) return;
        _editor.Changed += Refresh;
        _editor.PlayheadChanged += RefreshPlayheads;
        Refresh();
    }

    private void ArrangePanes(double width)
    {
        bool stacked = width < 630;
        if (stacked == (Grid.GetRow(Inspector) == 1)) return;
        Panes.RowDefinitions = new RowDefinitions(stacked ? "3*,2*" : "*");
        Panes.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,258");
        Panes.RowSpacing = stacked ? 8 : 0;
        Grid.SetRow(Inspector, stacked ? 1 : 0);
        Grid.SetColumn(Inspector, stacked ? 0 : 1);
    }

    private void Refresh()
    {
        if (_editor is not { } vm) return;
        Heading.Text = vm.Heading;
        AssetName.Text = vm.Name;
        Status.Text = vm.Status;
        OriginalDetails.Text = vm.Original.Details;
        ReplacementDetails.Text = vm.Replacement?.Details;
        ReplacementDetails.IsVisible = ReplacementPlay.IsVisible = ReplacementWave.IsVisible = vm.Replacement is not null;
        EmptyMessage.IsVisible = vm.Replacement is null;
        EmptyMessage.Text = vm.IsEditable ? "Drop an MP3 here to compare it." : "This sound is read only.";
        PlaybackNote.Text = SoundPreviewPlayer.UnavailableReason;
        PlaybackNote.IsVisible = !SoundPreviewPlayer.IsPlatformSupported;
        FileName.Text = vm.Replacement?.Label ?? (vm.IsEditable ? "Choose a clip to preview before applying." : "This sound belongs to a read-only provider.");
        AliasCount.Text = vm.AliasNames.Count switch
        {
            0 => "No referring aliases found in this workspace.",
            1 => vm.IsEditable ? "Replacing this clip changes what this alias plays." : "1 alias in this workspace",
            var count => vm.IsEditable ? $"Replacing this clip changes what all {count:N0} aliases play." : $"{count:N0} aliases in this workspace"
        };
        Aliases.ItemsSource = vm.AliasNames;
        ApplyButton.IsEnabled = vm.CanApply;
        UndoButton.IsEnabled = vm.CanUndo;
        ChooseButton.IsEnabled = vm.CanImport && !_choosing;
        SetTrack(OriginalWave, OriginalPlay, vm.Original);
        SetTrack(ReplacementWave, ReplacementPlay, vm.Replacement);
        RefreshPlayheads();
    }

    private void SetTrack(LoadedSoundWaveformControl wave, Button play, LoadedSoundClip? clip)
    {
        wave.Peaks = clip?.Peaks;
        wave.Duration = clip?.Duration ?? TimeSpan.Zero;
        play.Content = _editor?.IsPlaying(clip) == true ? "Pause" : "Play";
        play.IsEnabled = SoundPreviewPlayer.IsPlatformSupported && clip?.HasAudio == true;
    }

    private void RefreshPlayheads()
    {
        OriginalWave.Position = _editor?.Original.Position ?? TimeSpan.Zero;
        ReplacementWave.Position = _editor?.Replacement?.Position ?? TimeSpan.Zero;
    }

    private async void Choose(object? sender, RoutedEventArgs e) => await ChooseOrImportAsync(null);

    private async Task ChooseOrImportAsync(IStorageFile? file)
    {
        if (_choosing || _editor is not { CanImport: true } vm || TopLevel.GetTopLevel(this) is not { } owner) return;
        _choosing = true;
        Refresh();
        try
        {
            if (file is null)
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a replacement MP3", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("MP3 audio") { Patterns = ["*.mp3", "*.MP3"], MimeTypes = ["audio/mpeg"] }]
                });
                file = files.FirstOrDefault();
            }
            if (file is null || vm != _editor || TopLevel.GetTopLevel(this) != owner) return;
            await using var stream = await file.OpenReadAsync();
            await vm.ImportAsync(stream, file.Name);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            if (vm == _editor) vm.Report(ex.Message);
        }
        finally { _choosing = false; Refresh(); }
    }

    private static IStorageFile? DroppedMp3(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is [IStorageFile file] &&
        string.Equals(Path.GetExtension(file.Name), ".mp3", StringComparison.OrdinalIgnoreCase) ? file : null;

    private void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _editor?.CanImport == true && !_choosing && DroppedMp3(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedMp3(e) is { } file) await ChooseOrImportAsync(file);
    }

    private void Undo(object? sender, RoutedEventArgs e) => _editor?.Undo();
    private void Apply(object? sender, RoutedEventArgs e) => _editor?.Apply();
    private void PlayOriginal(object? sender, RoutedEventArgs e) => _editor?.TogglePlayback(_editor.Original);
    private void PlayReplacement(object? sender, RoutedEventArgs e) => _editor?.TogglePlayback(_editor.Replacement);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editor is null || e.Key != Key.Z || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
        _editor.Undo();
        e.Handled = true;
    }
}
