using Avalonia.Controls;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class FoliagePainterInspector : UserControl
{
    private readonly List<XModelSource> _models = [];

    public FoliagePainterInspector()
    {
        InitializeComponent();
        ChooseModelsButton.Click += (_, _) => ModelsRequested?.Invoke();
        AddModelsButton.Click += (_, _) => ModelsRequested?.Invoke();
        RemoveModelButton.Click += (_, _) =>
        {
            if (Palette.SelectedItem is not XModelSource selected) return;
            _models.Remove(selected);
            RefreshPalette();
        };
        Palette.SelectionChanged += (_, _) => RemoveModelButton.IsEnabled = Palette.SelectedItem is not null;
        PaintButton.IsCheckedChanged += (_, _) => Changed?.Invoke();
        Radius.ValueChanged += (_, _) => Changed?.Invoke();
        Density.ValueChanged += (_, _) => Changed?.Invoke();
        Spacing.ValueChanged += (_, _) => Changed?.Invoke();
        MinimumScale.ValueChanged += (_, _) => Changed?.Invoke();
        MaximumScale.ValueChanged += (_, _) => Changed?.Invoke();
        RandomYaw.IsCheckedChanged += (_, _) => Changed?.Invoke();
        AlignSurface.IsCheckedChanged += (_, _) => Changed?.Invoke();
    }

    internal event Action? Changed;
    internal event Action? ModelsRequested;
    internal IReadOnlyList<XModelSource> Models => _models;
    internal bool IsPainting => PaintButton.IsChecked == true;
    internal float BrushRadius => (float)(Radius.Value ?? 64);
    internal int BrushDensity => (int)(Density.Value ?? 1);
    internal float BrushSpacing => (float)(Spacing.Value ?? 32);
    internal float MinimumBrushScale => (float)(MinimumScale.Value ?? 0.8m);
    internal float MaximumBrushScale => (float)(MaximumScale.Value ?? 1.2m);
    internal bool UsesRandomYaw => RandomYaw.IsChecked == true;
    internal bool AlignsToSurface => AlignSurface.IsChecked == true;

    internal void StartPainting(XModelSource? selectedModel = null)
    {
        if (_models.Count == 0 && selectedModel is not null) AddModel(selectedModel);
        if (_models.Count > 0) PaintButton.IsChecked = true;
    }

    internal void StopPainting() => PaintButton.IsChecked = false;

    internal void AddModel(XModelSource model)
    {
        if (_models.Any(item => item.Name == model.Name)) return;
        _models.Add(model);
        RefreshPalette();
    }

    internal void ClearModels()
    {
        _models.Clear();
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        Palette.ItemsSource = null;
        Palette.ItemsSource = _models.ToArray();
        Palette.SelectedIndex = _models.Count - 1;
        bool hasModels = _models.Count > 0;
        EmptyPalette.IsVisible = !hasModels;
        PaletteSettings.IsVisible = hasModels;
        if (!hasModels) PaintButton.IsChecked = false;
        Changed?.Invoke();
    }
}
