using Avalonia.Controls;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class DestructiblePreviewPanel : UserControl
{
    private DestructiblePreset? _preset;
    private DestructiblePreviewSettings _settings = new();
    private bool _updating;
    private bool _preparing;

    public DestructiblePreviewPanel()
    {
        InitializeComponent();
        AppearanceBox.SelectionChanged += (_, _) =>
        {
            if (_updating || AppearanceBox.SelectedIndex < 0) return;
            _settings = _settings with { Stage = AppearanceBox.SelectedIndex };
            UpdatePartAvailability();
            SettingsChanged?.Invoke(_settings);
        };
        PartBox.SelectionChanged += (_, _) =>
        {
            if (_updating || PartBox.SelectedIndex < 0) return;
            _settings = _settings with { PartIndex = PartBox.SelectedIndex };
            _updating = true;
            PartStateBox.SelectedIndex = (int)_settings.PartState;
            _updating = false;
            SettingsChanged?.Invoke(_settings);
        };
        PartStateBox.SelectionChanged += (_, _) =>
        {
            if (_updating || PartStateBox.SelectedIndex < 0) return;
            _settings = _settings.WithPartState((DestructiblePartState)PartStateBox.SelectedIndex);
            SettingsChanged?.Invoke(_settings);
        };
        PlayButton.Click += (_, _) => PlayRequested?.Invoke();
        ResetButton.Click += (_, _) => ResetRequested?.Invoke();
    }

    internal event Action<DestructiblePreviewSettings>? SettingsChanged;
    internal event Action? PlayRequested;
    internal event Action? ResetRequested;

    internal void SetState(DestructiblePreset? preset, DestructiblePreviewSettings settings,
        bool playing, string? status = null, bool preparing = false)
    {
        _updating = true;
        try
        {
            _settings = settings;
            _preparing = preparing;
            if (!ReferenceEquals(_preset, preset))
            {
                _preset = preset;
                PresetTitle.Text = preset?.Name ?? "Destructible preview";
                AppearanceBox.ItemsSource = preset?.Preview.Stages.Select(stage => stage.Label).ToArray() ?? [];
                PartBox.ItemsSource = preset?.Preview.Parts?.Select(part => part.Label).ToArray() ?? [];
                PartStateBox.ItemsSource = new[] { "Intact", "Damaged", "Broken" };
                PartsExpander.IsExpanded = preset?.Preview.Stages.Count == 1;
            }
            AppearanceBox.SelectedIndex = settings.Stage;
            PartBox.SelectedIndex = preset?.Preview.Parts is { Count: > 0 } ? settings.PartIndex : -1;
            PartStateBox.SelectedIndex = (int)settings.PartState;
            AppearanceBox.IsEnabled = !preparing && preset?.Preview.Stages.Count > 1;
            PlayButton.IsEnabled = !preparing && preset?.Preview.Stages.Count > 1;
            PlayButton.IsVisible = preset?.Preview.Stages.Count > 1;
            bool modelOnly = preset is not null && preset.Preview.Parts is not { Count: > 0 } &&
                preset.Preview.Stages.All(stage => stage.FxName is null && stage.SoundName is null &&
                    stage.TransitionSoundName is null);
            PreviewAvailabilityHint.IsVisible = modelOnly;
            PreviewAvailabilityHint.Text = preset?.Preview.Stages.Count == 1
                ? "Only the intact model can be previewed for this preset."
                : "Preview shows model states only; effects and sounds are not previewed for this preset.";
            PartsExpander.IsVisible = preset?.Preview.Parts is { Count: > 0 } || preset?.Preview.HasTires == true;
            PartSelectionRow.IsVisible = PartConditionRow.IsVisible = preset?.Preview.Parts is { Count: > 0 };
            UpdatePartAvailability();
            PlayButton.Content = preparing ? "Preparing…" : playing ? "Stop playback" : "Play destruction";
            PreviewStatus.Text = status;
            PreviewStatus.IsVisible = !string.IsNullOrWhiteSpace(status);
        }
        finally { _updating = false; }
    }

    private void UpdatePartAvailability()
    {
        bool replaceModel = _preset?.Preview.Stages.ElementAtOrDefault(_settings.Stage)?.ModelName is not null;
        bool hasParts = _preset?.Preview.Parts is { Count: > 0 };
        PartBox.IsEnabled = hasParts && !replaceModel && !_preparing;
        PartStateBox.IsEnabled = hasParts && !replaceModel && !_preparing;
        PartsAvailabilityHint.IsVisible = hasParts && replaceModel;
        TireAvailabilityHint.IsVisible = _preset?.Preview.HasTires == true;
    }
}
