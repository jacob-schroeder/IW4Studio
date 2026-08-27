using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IW4.Render;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Weapon;

internal sealed partial class WeaponAnimationPreviewWindow : Window
{
    private readonly XModelPreviewControl? _preview;
    private XAnimPreviewViewModel? _previewViewModel;
    private bool _hasPreviewScene;
    private bool _closed;

    public WeaponAnimationPreviewWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        _preview = this.FindControl<XModelPreviewControl>("Preview");
        if (_preview is not null)
        {
            _preview.RendererStatusChanged += Preview_RendererStatusChanged;
            if (this.FindControl<Border>("PreviewInputSurface") is { } input)
                _preview.AttachCameraInput(input);
        }

        Opened += Window_Opened;
        Closed += Window_Closed;
    }

    internal WeaponAnimationPreviewWindow(
        XAnimPreviewViewModel preview,
        XModelRenderScene? scene,
        int selectedLodIndex,
        string assemblyDisplay,
        string camoDisplay,
        bool isMaterialAnimationEnabled)
        : this()
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (scene is not null && selectedLodIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(selectedLodIndex));

        _previewViewModel = preview;
        DataContext = preview;
        Title = $"{preview.Name} — Weapon Animation Preview — IW4 Studio";
        AnimationNameText.Text = preview.Name;
        AssemblyDisplayText.Text = string.IsNullOrWhiteSpace(assemblyDisplay)
            ? "Weapon assembly"
            : assemblyDisplay;
        CamoDisplayText.Text = string.IsNullOrWhiteSpace(camoDisplay)
            ? "View model / camo not specified"
            : camoDisplay;
        _hasPreviewScene = scene is not null;
        PreviewUnavailableOverlay.IsVisible = !_hasPreviewScene;
        if (_preview is not null)
        {
            _preview.Scene = scene;
            _preview.SelectedLodIndex = scene is null
                ? -1
                : selectedLodIndex;
            _preview.IsMaterialAnimationEnabled =
                isMaterialAnimationEnabled;
        }
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        _preview?.Fit();
        if (_previewViewModel is { CanPlay: true, IsPlaying: false } preview)
            preview.TogglePlayback();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (_closed)
            return;

        _closed = true;
        Opened -= Window_Opened;
        Closed -= Window_Closed;
        if (_preview is not null)
            _preview.RendererStatusChanged -= Preview_RendererStatusChanged;
        _previewViewModel?.Dispose();
        _previewViewModel = null;
        DataContext = null;
    }

    private void FitButton_Click(object? sender, RoutedEventArgs e) =>
        _preview?.Fit();

    private void PlayPauseButton_Click(object? sender, RoutedEventArgs e) =>
        _previewViewModel?.TogglePlayback();

    private void RestartButton_Click(object? sender, RoutedEventArgs e) =>
        _previewViewModel?.RestartPlayback();

    private void Preview_RendererStatusChanged(object? sender, EventArgs e)
    {
        if (!_hasPreviewScene ||
            sender is not XModelPreviewControl preview)
        {
            return;
        }

        string? failure = preview.RendererFailure;
        long revision = preview.RendererStatusRevision;
        XModelRenderScene? scene = preview.Scene;
        void Apply()
        {
            if (_closed ||
                !ReferenceEquals(preview.Scene, scene) ||
                preview.RendererStatusRevision != revision)
            {
                return;
            }

            bool failed = !string.IsNullOrWhiteSpace(failure);
            PreviewUnavailableOverlay.IsVisible = failed;
            if (failed)
            {
                PreviewUnavailableTitleText.Text = "OpenGL preview unavailable";
                PreviewUnavailableReasonText.Text = failure;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }
}
