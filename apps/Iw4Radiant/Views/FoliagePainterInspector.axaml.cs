using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class FoliagePainterInspector : UserControl
{
    private readonly List<FoliagePaletteModel> _models = [];
    private bool _updatingPlacement;
    private bool _loadingPreset;
    private RadiantSettings? _settings;
    private Func<string, XModelSource?>? _resolveModel;
    private PrefabLibrary? _prefabs;
    private string? _mapPath;
    private int _prefabRevision = -1;
    private FoliagePainterPreset[] _shownPresets = [];
    private FoliagePainterPreset? _pendingDelete;
    private FoliagePainterPreset? _pendingReplace;

    public FoliagePainterInspector()
    {
        InitializeComponent();
        PresetChoice.SelectionChanged += (_, _) =>
        {
            _pendingDelete = null;
            _pendingReplace = null;
            DeletePresetButton.Content = "Delete";
            ConfirmSavePresetButton.Content = "Save palette";
            PresetNameEditor.IsVisible = false;
            UpdatePresetControls();
        };
        LoadPresetButton.Click += (_, _) => LoadSelectedPreset();
        SavePresetButton.Click += (_, _) =>
        {
            if (_models.Count == 0) return;
            _pendingReplace = null;
            ConfirmSavePresetButton.Content = "Save palette";
            PresetName.Text = SelectedPreset()?.Name ?? "";
            PresetNameEditor.IsVisible = true;
            PresetStatus.Text = "Name this brush to use it in a later session.";
            PresetName.Focus();
        };
        PresetName.TextChanged += (_, _) =>
        {
            _pendingReplace = null;
            ConfirmSavePresetButton.Content = "Save palette";
        };
        ConfirmSavePresetButton.Click += (_, _) => SavePreset();
        CancelSavePresetButton.Click += (_, _) =>
        {
            _pendingReplace = null;
            PresetNameEditor.IsVisible = false;
        };
        DeletePresetButton.Click += (_, _) => DeleteSelectedPreset();
        ChooseModelsButton.Click += (_, _) => ModelsRequested?.Invoke();
        AddModelsButton.Click += (_, _) => ModelsRequested?.Invoke();
        ChoosePrefabsButton.Click += (_, _) => PrefabsRequested?.Invoke();
        AddPrefabsButton.Click += (_, _) => PrefabsRequested?.Invoke();
        RemoveModelButton.Click += (_, _) =>
        {
            if (Palette.SelectedItem is not FoliagePaletteModel selected) return;
            int index = Palette.SelectedIndex;
            selected.Changed -= OnModelChanged;
            _models.Remove(selected);
            RefreshPalette(Math.Min(index, _models.Count - 1));
        };
        Palette.SelectionChanged += (_, _) => RefreshSelectedModel();
        CustomPlacement.IsCheckedChanged += (_, _) =>
        {
            if (_updatingPlacement || Palette.SelectedItem is not FoliagePaletteModel selected) return;
            selected.SetCustomPlacement(CustomPlacement.IsChecked == true, MinimumBrushScale,
                MaximumBrushScale, UsesRandomYaw, AlignsToSurface);
            RefreshSelectedModel();
        };
        ModelMinimumScale.ValueChanged += (_, _) => SaveSelectedPlacement();
        ModelMaximumScale.ValueChanged += (_, _) => SaveSelectedPlacement();
        ModelRandomYaw.IsCheckedChanged += (_, _) => SaveSelectedPlacement();
        ModelFixedYaw.ValueChanged += (_, _) => SaveSelectedPlacement();
        ModelAlignSurface.IsCheckedChanged += (_, _) => SaveSelectedPlacement();
        ModelSurfaceOffset.ValueChanged += (_, _) => SaveSelectedPlacement();
        PaintButton.IsCheckedChanged += (_, _) => NotifyChanged();
        Radius.ValueChanged += (_, _) => NotifyChanged();
        Density.ValueChanged += (_, _) => NotifyChanged();
        Spacing.ValueChanged += (_, _) => NotifyChanged();
        MinimumScale.ValueChanged += (_, _) => NotifyChanged();
        MaximumScale.ValueChanged += (_, _) => NotifyChanged();
        RandomYaw.IsCheckedChanged += (_, _) => NotifyChanged();
        AlignSurface.IsCheckedChanged += (_, _) => NotifyChanged();
    }

    internal event Action? Changed;
    internal event Action? ModelsRequested;
    internal event Action? PrefabsRequested;
    internal IReadOnlyList<FoliagePaletteModel> Models => _models;
    internal bool IsPainting => PaintButton.IsChecked == true;
    internal float BrushRadius => (float)(Radius.Value ?? 64);
    internal int BrushDensity => (int)(Density.Value ?? 1);
    internal float BrushSpacing => (float)(Spacing.Value ?? 32);
    internal float MinimumBrushScale => (float)(MinimumScale.Value ?? 0.8m);
    internal float MaximumBrushScale => (float)(MaximumScale.Value ?? 1.2m);
    internal bool UsesRandomYaw => RandomYaw.IsChecked == true;
    internal bool AlignsToSurface => AlignSurface.IsChecked == true;

    internal void InitializePresets(RadiantSettings settings, Func<string, XModelSource?> resolveModel)
    {
        _settings = settings;
        _resolveModel = resolveModel;
        RefreshPresetChoices(null);
    }

    internal void UpdateMapContext(string? mapPath, PrefabLibrary prefabs)
    {
        if (_mapPath == mapPath && ReferenceEquals(_prefabs, prefabs) && _prefabRevision == prefabs.Revision) return;
        _mapPath = mapPath;
        _prefabs = prefabs;
        _prefabRevision = prefabs.Revision;
        ResolvePrefabs();
    }

    internal void ResolvePrefabs()
    {
        if (_prefabs is null) return;
        int selectedIndex = Palette.SelectedIndex;
        _loadingPreset = true;
        try
        {
            foreach (FoliagePaletteModel item in _models) item.ResolvePrefab(_mapPath, _prefabs);
            RefreshPalette(selectedIndex);
        }
        finally { _loadingPreset = false; }
        NotifyChanged();
    }

    internal void MarkModelsUnavailable()
    {
        int selectedIndex = Palette.SelectedIndex;
        _loadingPreset = true;
        try
        {
            foreach (FoliagePaletteModel item in _models) item.ResolveModel(null);
            RefreshPalette(selectedIndex);
        }
        finally { _loadingPreset = false; }
        NotifyChanged();
    }

    internal void ResolveModels()
    {
        if (_resolveModel is null) return;
        int selectedIndex = Palette.SelectedIndex;
        _loadingPreset = true;
        try
        {
            foreach (FoliagePaletteModel item in _models) item.ResolveModel(_resolveModel(item.Name));
            RefreshPalette(selectedIndex);
        }
        finally { _loadingPreset = false; }
        NotifyChanged();
    }

    internal void StartPainting(XModelSource? selectedModel = null)
    {
        if (_models.Count == 0 && selectedModel is not null) AddModel(selectedModel);
        ResolvePrefabs();
        if (PaintButton.IsEnabled) PaintButton.IsChecked = true;
    }

    internal void StopPainting() => PaintButton.IsChecked = false;

    internal void AddModel(XModelSource model)
    {
        FoliagePaletteModel? existing = _models.FirstOrDefault(item => !item.IsPrefab && item.Name == model.Name);
        if (existing is not null)
        {
            existing.ResolveModel(model);
            RefreshPalette(_models.IndexOf(existing));
            return;
        }
        var item = new FoliagePaletteModel(model);
        item.Changed += OnModelChanged;
        _models.Add(item);
        RefreshPalette(_models.Count - 1);
    }

    internal FoliagePaletteModel AddPrefab(string path)
    {
        string fullPath = Path.GetFullPath(path);
        FoliagePaletteModel? existing = _models.FirstOrDefault(item => item.IsPrefab && item.Name == fullPath);
        if (existing is null)
        {
            existing = new FoliagePaletteModel(fullPath);
            existing.Changed += OnModelChanged;
            _models.Add(existing);
        }
        if (_prefabs is not null) existing.ResolvePrefab(_mapPath, _prefabs);
        RefreshPalette(_models.IndexOf(existing));
        return existing;
    }

    private void RefreshPalette(int selectedIndex)
    {
        Palette.ItemsSource = null;
        Palette.ItemsSource = _models.ToArray();
        Palette.SelectedIndex = selectedIndex >= 0 && selectedIndex < _models.Count ? selectedIndex : _models.Count - 1;
        RefreshSelectedModel();
        bool hasAssets = _models.Count > 0;
        EmptyPalette.IsVisible = !hasAssets;
        PaletteSettings.IsVisible = hasAssets;
        SavePresetButton.IsEnabled = hasAssets;
        if (!hasAssets) PresetNameEditor.IsVisible = false;
        UpdateBrushReadiness();
        NotifyChanged();
    }

    private void UpdateBrushReadiness()
    {
        FoliagePaletteModel[] unavailable = _models.Where(item => !item.IsAvailable).ToArray();
        UnavailableAssets.IsVisible = unavailable.Length > 0;
        UnavailableAssets.Text = unavailable.Length == 0 ? "" :
            $"{unavailable.Length} unavailable asset{(unavailable.Length == 1 ? " is" : "s are")} kept in this brush and skipped. " +
            (unavailable[0].AvailabilityError ?? "Load the model assets to use them.");
        bool canPaint = _models.Any(item => item.IsAvailable && item.Weight is > 0);
        PaintButton.IsEnabled = canPaint;
        if (!canPaint) PaintButton.IsChecked = false;
        BrushStatus.Text = _models.Count == 0 ? "Choose an asset to begin." : canPaint
            ? "Drag over camera surfaces to paint · Esc stops painting or cancels a stroke"
            : _models.Any(item => item.IsAvailable)
                ? "Set a positive weight on an available asset to paint."
                : "Choose an available model or prefab to paint.";
    }

    private FoliagePainterPreset? SelectedPreset()
    {
        int index = PresetChoice.SelectedIndex;
        return index >= 0 && index < _shownPresets.Length ? _shownPresets[index] : null;
    }

    private void RefreshPresetChoices(FoliagePainterPreset? selected)
    {
        _shownPresets = _settings?.FoliagePresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        PresetChoice.ItemsSource = _shownPresets.Select(preset => preset.Name).ToArray();
        PresetChoice.SelectedIndex = selected is null ? -1 : Array.IndexOf(_shownPresets, selected);
        UpdatePresetControls();
    }

    private void UpdatePresetControls()
    {
        FoliagePainterPreset? preset = SelectedPreset();
        LoadPresetButton.IsEnabled = DeletePresetButton.IsEnabled = preset is not null;
        PresetStatus.Text = preset is not null
            ? $"{preset.Models.Count} asset{(preset.Models.Count == 1 ? "" : "s")} saved. Load changes this brush, not the map."
            : _shownPresets.Length == 0 ? "No saved palettes yet." : "Choose a saved palette to load.";
    }

    private FoliagePainterPreset CapturePreset(string name) => new()
    {
        Name = name,
        Radius = BrushRadius,
        Density = BrushDensity,
        Spacing = BrushSpacing,
        MinimumScale = MinimumBrushScale,
        MaximumScale = MaximumBrushScale,
        RandomYaw = UsesRandomYaw,
        AlignToSurface = AlignsToSurface,
        Models = _models.Select(item => item.ToPreset()).ToList()
    };

    private void SavePreset()
    {
        if (_settings is not { } settings || _models.Count == 0) return;
        string name = (PresetName.Text ?? "").Trim();
        if (name.Length is 0 or > 64)
        {
            PresetStatus.Text = "Enter a palette name of 1–64 characters.";
            return;
        }
        FoliagePainterPreset? existing = settings.FoliagePresets.FirstOrDefault(preset =>
            preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !ReferenceEquals(_pendingReplace, existing))
        {
            _pendingReplace = existing;
            ConfirmSavePresetButton.Content = "Replace palette";
            PresetStatus.Text = $"‘{existing.Name}’ already exists. Choose Replace palette to update it.";
            return;
        }
        var saved = CapturePreset(name);
        if (existing is null) settings.FoliagePresets.Add(saved);
        else settings.FoliagePresets[settings.FoliagePresets.IndexOf(existing)] = saved;
        _pendingReplace = null;
        PresetNameEditor.IsVisible = false;
        RefreshPresetChoices(saved);
        PresetStatus.Text = settings.Save()
            ? $"Saved ‘{name}’ for later sessions."
            : "Could not write user settings. This palette is available only until the editor closes.";
    }

    private void LoadSelectedPreset()
    {
        if (SelectedPreset() is not { } preset || _resolveModel is null) return;
        _loadingPreset = true;
        try
        {
            StopPainting();
            foreach (FoliagePaletteModel model in _models) model.Changed -= OnModelChanged;
            _models.Clear();
            Radius.Value = (decimal)FiniteClamp(preset.Radius, 8, 2048, 64);
            Density.Value = Math.Clamp(preset.Density, 1, 32);
            Spacing.Value = (decimal)FiniteClamp(preset.Spacing, 1, 2048, 32);
            MinimumScale.Value = (decimal)FiniteClamp(preset.MinimumScale, 0.01f, 100, 0.8f);
            MaximumScale.Value = (decimal)FiniteClamp(preset.MaximumScale, 0.01f, 100, 1.2f);
            RandomYaw.IsChecked = preset.RandomYaw;
            AlignSurface.IsChecked = preset.AlignToSurface;
            var names = new HashSet<(bool IsPrefab, string Name)>();
            foreach (FoliagePresetModel saved in preset.Models)
            {
                if (string.IsNullOrWhiteSpace(saved.Name) || !names.Add((saved.IsPrefab, saved.Name))) continue;
                FoliagePaletteModel model = FoliagePaletteModel.FromPreset(saved,
                    saved.IsPrefab ? null : _resolveModel(saved.Name));
                if (_prefabs is not null) model.ResolvePrefab(_mapPath, _prefabs);
                model.Changed += OnModelChanged;
                _models.Add(model);
            }
            RefreshPalette(_models.Count > 0 ? 0 : -1);
        }
        finally { _loadingPreset = false; }
        NotifyChanged();
        PresetNameEditor.IsVisible = false;
        int unavailable = _models.Count(model => !model.IsAvailable);
        PresetStatus.Text = unavailable == 0
            ? $"Loaded ‘{preset.Name}’. Choose Paint brush when ready."
            : $"Loaded ‘{preset.Name}’. {unavailable} unavailable asset{(unavailable == 1 ? " is" : "s are")} listed and skipped.";
    }

    private void DeleteSelectedPreset()
    {
        if (_settings is not { } settings || SelectedPreset() is not { } preset) return;
        if (!ReferenceEquals(_pendingDelete, preset))
        {
            _pendingDelete = preset;
            DeletePresetButton.Content = "Confirm delete";
            PresetStatus.Text = $"Choose Confirm delete to remove ‘{preset.Name}’. The current brush stays here.";
            return;
        }
        settings.FoliagePresets.Remove(preset);
        _pendingDelete = null;
        DeletePresetButton.Content = "Delete";
        RefreshPresetChoices(null);
        PresetStatus.Text = settings.Save()
            ? $"Deleted ‘{preset.Name}’. The current brush is unchanged."
            : "Could not write user settings. This deletion lasts only until the editor closes.";
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private void RefreshSelectedModel()
    {
        bool hasSelection = Palette.SelectedItem is FoliagePaletteModel;
        RemoveModelButton.IsEnabled = hasSelection;
        SelectedModelSettings.IsVisible = hasSelection;
        if (Palette.SelectedItem is not FoliagePaletteModel selected) return;
        _updatingPlacement = true;
        try
        {
            SelectedModelType.Text = selected.IsPrefab ? "Selected prefab" : "Selected model";
            SelectedModelName.Text = selected.IsPrefab ? Path.GetFileNameWithoutExtension(selected.Name) : selected.Name;
            SelectedAssetPath.Text = selected.IsPrefab ? "Source · " + (selected.PrefabReference ?? selected.Name) : "";
            ToolTip.SetTip(SelectedAssetPath, selected.IsPrefab ? selected.Name : null);
            SelectedAssetPath.IsVisible = selected.IsPrefab;
            CustomPlacement.IsChecked = selected.UsesCustomPlacement;
            CustomPlacementFields.IsVisible = selected.UsesCustomPlacement;
            ModelMinimumScale.Value = (decimal)selected.MinimumScale;
            ModelMaximumScale.Value = (decimal)selected.MaximumScale;
            ModelRandomYaw.IsChecked = selected.RandomYaw;
            ModelFixedYaw.Value = (decimal)selected.FixedYaw;
            FixedYawSetting.IsVisible = !selected.RandomYaw;
            ModelAlignSurface.IsChecked = selected.AlignToSurface;
            ModelSurfaceOffset.Value = (decimal)selected.SurfaceOffset;
        }
        finally { _updatingPlacement = false; }
    }

    private void SaveSelectedPlacement()
    {
        if (_updatingPlacement || Palette.SelectedItem is not FoliagePaletteModel selected ||
            !selected.UsesCustomPlacement) return;
        bool randomYaw = ModelRandomYaw.IsChecked == true;
        FixedYawSetting.IsVisible = !randomYaw;
        selected.SetPlacement(
            (float)(ModelMinimumScale.Value ?? (decimal)selected.MinimumScale),
            (float)(ModelMaximumScale.Value ?? (decimal)selected.MaximumScale),
            randomYaw, (float)(ModelFixedYaw.Value ?? (decimal)selected.FixedYaw),
            ModelAlignSurface.IsChecked == true,
            (float)(ModelSurfaceOffset.Value ?? (decimal)selected.SurfaceOffset));
    }

    private void NotifyChanged()
    {
        if (!_loadingPreset) Changed?.Invoke();
    }

    private void OnModelChanged()
    {
        UpdateBrushReadiness();
        NotifyChanged();
    }
}
