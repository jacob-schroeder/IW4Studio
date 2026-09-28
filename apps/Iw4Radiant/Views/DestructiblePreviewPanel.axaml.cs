using Avalonia.Controls;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class DestructiblePreviewPanel : UserControl
{
    private DestructiblePreviewSettings _settings = new();
    private bool _updating;
    private bool _preparing;
    private bool _tireAvailable;
    private string? _tireUnavailableReason;

    public DestructiblePreviewPanel()
    {
        InitializeComponent();
        SetTireAvailability(false, "Flat-tire animation preview is not supported yet.");
        AppearanceBox.SelectionChanged += (_, _) =>
        {
            if (_updating || AppearanceBox.SelectedIndex < 0) return;
            _settings = _settings with { Appearance = (DestructibleAppearance)AppearanceBox.SelectedIndex };
            UpdatePartAvailability();
            SettingsChanged?.Invoke(_settings);
        };
        WindshieldBox.SelectionChanged += (_, _) =>
        {
            if (_updating || WindshieldBox.SelectedIndex < 0) return;
            _settings = _settings with { Windshield = (DestructibleWindowState)WindshieldBox.SelectedIndex };
            SettingsChanged?.Invoke(_settings);
        };
        TireBox.SelectionChanged += (_, _) =>
        {
            if (_updating || TireBox.SelectedIndex < 0) return;
            _settings = _settings with { FrontLeftTireFlat = TireBox.SelectedIndex == 1 };
            SettingsChanged?.Invoke(_settings);
        };
        PlayButton.Click += (_, _) => PlayRequested?.Invoke();
        ResetButton.Click += (_, _) => ResetRequested?.Invoke();
    }

    internal event Action<DestructiblePreviewSettings>? SettingsChanged;
    internal event Action? PlayRequested;
    internal event Action? ResetRequested;

    internal void SetState(DestructiblePreviewSettings settings, bool playing, string? status = null, bool preparing = false)
    {
        _updating = true;
        try
        {
            _settings = settings;
            _preparing = preparing;
            AppearanceBox.IsEnabled = !preparing;
            PlayButton.IsEnabled = !preparing;
            AppearanceBox.SelectedIndex = (int)settings.Appearance;
            WindshieldBox.SelectedIndex = (int)settings.Windshield;
            TireBox.SelectedIndex = settings.FrontLeftTireFlat ? 1 : 0;
            UpdatePartAvailability();
            PlayButton.Content = preparing ? "Preparing…" : playing ? "Stop playback" : "Play destruction";
            PreviewStatus.Text = status;
            PreviewStatus.IsVisible = !string.IsNullOrWhiteSpace(status);
        }
        finally { _updating = false; }
    }

    internal void SetTireAvailability(bool available, string? reason = null)
    {
        _tireAvailable = available;
        _tireUnavailableReason = reason;
        UpdatePartAvailability();
    }

    private void UpdatePartAvailability()
    {
        bool wreck = _settings.Appearance == DestructibleAppearance.Wreck;
        WindshieldBox.IsEnabled = !wreck && !_preparing;
        TireBox.IsEnabled = !wreck && _tireAvailable && !_preparing;
        PartsAvailabilityHint.IsVisible = wreck;
        TireAvailabilityHint.Text = _tireUnavailableReason;
        TireAvailabilityHint.IsVisible = !wreck && !_tireAvailable &&
            !string.IsNullOrWhiteSpace(_tireUnavailableReason);
    }
}
