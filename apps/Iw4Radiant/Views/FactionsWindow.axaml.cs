using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IW4.Formats.SourceFormat.Character;
using Iw4Radiant.Viewports.Camera;
using Vector2 = System.Numerics.Vector2;

namespace Iw4Radiant.Views;

public partial class FactionsWindow : Window
{
    private sealed record FactionChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ModelChoice(string Name, string Label, string Detail)
    {
        public override string ToString() => Label;
    }

    private sealed record AppearanceChoice(string? Model, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LodChoice(int Index)
    {
        public override string ToString() => $"LOD{Index}";
    }

    private static readonly FactionChoice[] Factions =
    [
        new(MapFactionAuthoring.UsArmy, "Rangers"),
        new(MapFactionAuthoring.OpforceAirborne, "Spetsnaz")
    ];
    private readonly string? _bootstrapRoot;
    private readonly string? _mapFilePath;
    private readonly FactionModelPreview? _preview;
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private ModelChoice[] _models = [];
    private FactionAppearance? _alliesAssaultA, _axisAssaultA;
    private bool _editingAllies, _editingAxis;
    private Bitmap? _bitmap;
    private bool _ready, _axis, _updating, _panning, _navigationMoved, _interactive, _rendering, _pending, _closed;
    private int _previewRevision;
    private IPointer? _dragPointer;
    private MouseButton _dragButton;
    private Point _lastPointer, _pressPoint;
    private Vector2 _pan;
    private float _yaw = -45, _pitch = 10, _zoom = 1;

    public FactionsWindow() => InitializeComponent();

    internal FactionsWindow(MapFactionSettings settings, string? bootstrapRoot,
        string? mapFilePath) : this()
    {
        _mapFilePath = mapFilePath;
        _alliesAssaultA = settings.AlliesAssaultA;
        _axisAssaultA = settings.AxisAssaultA;
        _bootstrapRoot = bootstrapRoot;
        SaveBlenderKit.IsVisible = File.Exists(BlenderKitPath);
        _preview = bootstrapRoot is null ? null : new FactionModelPreview(bootstrapRoot);
        PreviewLod.ItemsSource = new[] { new LodChoice(0) };
        PreviewLod.SelectedIndex = 0;
        PreviewLod.IsEnabled = false;
        ReplacementBody.ItemsSource = RangersAssaultAppearance.Bodies
            .Select(choice => new AppearanceChoice(choice.Model, choice.Label)).ToArray();
        ReplacementHead.ItemsSource = new[] { new AppearanceChoice(null, "Stock random heads") }
            .Concat(RangersAssaultAppearance.Heads.Select(choice => new AppearanceChoice(choice.Model, choice.Label))).ToArray();
        ReplacementHands.ItemsSource = RangersAssaultAppearance.ViewHands
            .Select(choice => new AppearanceChoice(choice.Model, choice.Label)).ToArray();
        AlliesFaction.ItemsSource = Factions;
        AxisFaction.ItemsSource = Factions;
        AlliesFaction.SelectedItem = Factions.Single(faction => faction.Id == settings.Allies);
        AxisFaction.SelectedItem = Factions.Single(faction => faction.Id == settings.Axis);
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            _interactive = false;
            RequestPreview();
        };
        Opened += (_, _) => { _ready = true; ShowTeam(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _pending = false;
            _settleTimer.Stop();
            FinishPreviewGesture();
            PreviewImage.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (!_rendering) CleanupImportedFolders();
        };
        Closing += (_, e) => { if (_importing) e.Cancel = true; };
        Deactivated += (_, _) => FinishPreviewGesture();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    private static FactionChoice SelectedFaction(ComboBox picker) => picker.SelectedItem as FactionChoice ??
        throw new InvalidOperationException("Choose a faction for each team.");
    private FactionChoice ActiveFaction => SelectedFaction(_axis ? AxisFaction : AlliesFaction);
    private FactionAppearance? ActiveReplacement
    {
        get => _axis ? _axisAssaultA : _alliesAssaultA;
        set { if (_axis) _axisAssaultA = value; else _alliesAssaultA = value; }
    }
    private bool ActiveEditing
    {
        get => _axis ? _editingAxis : _editingAllies;
        set { if (_axis) _editingAxis = value; else _editingAllies = value; }
    }
    private bool EditableVariant => ActiveFaction.Id == MapFactionAuthoring.UsArmy &&
        Variants.SelectedItem is ModelChoice { Name: "mp_body_us_army_assault_a" };

    private void Allies_Click(object? sender, RoutedEventArgs e) { _axis = false; ShowTeam(); }
    private void Axis_Click(object? sender, RoutedEventArgs e) { _axis = true; ShowTeam(); }

    private void Faction_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _axis = ReferenceEquals(sender, AxisFaction);
        ShowTeam();
    }

    private void ShowTeam()
    {
        if (!_ready) return;
        _updating = true;
        try
        {
            AlliesCard.BorderBrush = _axis ? Brushes.DimGray : Brushes.LightSlateGray;
            AxisCard.BorderBrush = _axis ? Brushes.LightSlateGray : Brushes.DimGray;
            AlliesCard.Background = _axis ? Brushes.Transparent : new SolidColorBrush(Color.Parse("#2D333C"));
            AxisCard.Background = _axis ? new SolidColorBrush(Color.Parse("#2D333C")) : Brushes.Transparent;
            RosterTitle.Text = $"{ActiveFaction.Name} player models";
            ImportStatus.Text = "";
            Search.Text = "";
            string[] names = _bootstrapRoot is null ? [] : Directory.GetFiles(Path.Combine(_bootstrapRoot, "xmodel_native"), "*.json")
                .Select(path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal).ToArray();
            bool rangers = ActiveFaction.Id == MapFactionAuthoring.UsArmy;
            _models = names.Where(name => rangers
                    ? name.StartsWith("mp_body_us_army_", StringComparison.Ordinal) || name is "mp_body_army_sniper" or "mp_body_ally_sniper_ghillie_urban"
                    : name.StartsWith("mp_body_airborne_", StringComparison.Ordinal) || name is "mp_body_op_airborne_sniper" or "mp_body_riot_op_airborne" or "mp_body_op_sniper_ghillie_urban")
                .Select(name => new ModelChoice(name, BodyLabel(name), name.Contains("ghillie", StringComparison.Ordinal) ? "Ghillie suit" : "Stock appearance"))
                .OrderBy(model => model.Label, StringComparer.Ordinal).ToArray();
            Heads.ItemsSource = names.Where(name => rangers
                    ? name.StartsWith("head_us_army_", StringComparison.Ordinal) || name is "head_allies_us_army_sniper" or "head_allies_sniper_ghillie_urban"
                    : name.StartsWith("head_airborne_", StringComparison.Ordinal) || name is "head_op_airborne_sniper" or "head_riot_op_airborne" or "head_op_sniper_ghillie_urban")
                .OrderBy(name => name.StartsWith(rangers ? "head_us_army_" : "head_airborne_", StringComparison.Ordinal) ? 0 : 1)
                .Select((name, index) => new ModelChoice(name, $"Head {index + 1}", "")).ToArray();
            Heads.SelectedIndex = 0;
            HandModels.ItemsSource = new[]
                {
                    new ModelChoice(rangers ? "viewhands_us_army" : "viewhands_russian_airborne", "Standard", ""),
                    new ModelChoice(rangers ? "viewhands_sniper_us_army" : "viewhands_sniper_op_airborne", "Sniper", ""),
                    new ModelChoice("viewhands_ghillie_urban", "Urban ghillie", "")
                }.Where(model => names.Contains(model.Name, StringComparer.Ordinal)).ToArray();
            HandModels.SelectedIndex = 0;
            FilterModels();
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            _models = [];
            Variants.ItemsSource = _models;
            PreviewMessage.Text = exception.Message;
        }
        finally { _updating = false; }
        ShowReplacementState();
        RequestPreview(clear: true);
    }

    private static string BodyLabel(string name)
    {
        string role = name.Contains("ghillie", StringComparison.Ordinal) ? "Urban sniper" :
            name.Contains("sniper", StringComparison.Ordinal) ? "Sniper" :
            name.Contains("assault", StringComparison.Ordinal) ? "Assault" :
            name.Contains("shotgun", StringComparison.Ordinal) ? "Shotgun" :
            name.Contains("smg", StringComparison.Ordinal) ? "SMG" :
            name.Contains("lmg", StringComparison.Ordinal) ? "Machine gunner" : "Riot shield";
        return name.EndsWith("_b", StringComparison.Ordinal) ? role + " · 2" :
            name.EndsWith("_c", StringComparison.Ordinal) ? role + " · 3" : role + " · 1";
    }

    private void Search_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        FilterModels();
    }

    private void FilterModels()
    {
        string search = Search.Text?.Trim() ?? "";
        Variants.ItemsSource = _models.Where(model => model.Label.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        Variants.SelectedIndex = 0;
    }

    private void Variant_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        ShowReplacementState();
        RequestPreview(clear: true);
    }

    private void ShowReplacementState()
    {
        bool editable = EditableVariant;
        FactionAppearance? replacement = ActiveReplacement;
        bool custom = replacement?.CustomAssetFolder is not null;
        Summary.Text = $"Allies · {AlliesFaction.SelectedItem}{(SelectedFaction(AlliesFaction).Id == MapFactionAuthoring.UsArmy && _alliesAssaultA is not null ? " · Assault 1 replaced" : "")}" +
            $"     Axis · {AxisFaction.SelectedItem}{(SelectedFaction(AxisFaction).Id == MapFactionAuthoring.UsArmy && _axisAssaultA is not null ? " · Assault 1 replaced" : "")}";
        ReplaceAppearance.IsVisible = editable && !custom;
        RestoreStock.IsVisible = editable && replacement is not null;
        ReplacementEditor.IsVisible = editable && ActiveEditing && !custom;
        BlenderImportPanel.IsVisible = editable && (ActiveEditing || custom);
        ReplaceAppearance.Content = replacement is null ? "Replace appearance" : "Edit replacement";
        ReplacementStatus.Text = !editable
            ? "Only Rangers · Assault · 1 can be replaced in this version. Other models are available for preview."
            : custom
                ? $"{(_axis ? "Axis" : "Allies")} · Assault · 1 uses an imported body and matching hands. Head included."
            : replacement is null
                ? "This team slot uses its stock body, random heads, and view hands."
                : $"{(_axis ? "Axis" : "Allies")} · Assault · 1 has a replacement. Apply saves it to the map.";
        PreviewSubtitle.Text = editable && custom ? "Imported full body · head included" : editable && replacement is not null
            ? replacement.Head is null
                ? "Team slot replacement · one stock random head shown"
                : "Team slot replacement · default pose"
            : editable && ActiveEditing ? "Team slot draft · one stock random head shown"
            : "Appearance preview · default pose";
        UpdatePreviewPartControls();
        if (!editable || !ActiveEditing || custom) return;
        FactionAppearance current = replacement ?? RangersAssaultAppearance.StockA;
        _updating = true;
        try
        {
            ReplacementBody.SelectedItem = ChoiceFor(ReplacementBody, current.Body);
            ReplacementHead.SelectedItem = ChoiceFor(ReplacementHead, current.Head);
            ReplacementHands.SelectedItem = ChoiceFor(ReplacementHands, current.ViewHands);
        }
        finally { _updating = false; }
    }

    private static AppearanceChoice ChoiceFor(ComboBox picker, string? model) =>
        picker.ItemsSource is IEnumerable<AppearanceChoice> choices
            ? choices.Single(choice => choice.Model == model)
            : throw new InvalidOperationException("Appearance choices are unavailable.");

    private void UpdatePreviewPartControls()
    {
        bool hands = HandsPreview.IsChecked == true;
        bool replacementPreview = EditableVariant && (ActiveReplacement is not null || ActiveEditing);
        Heads.IsVisible = !hands && !replacementPreview;
        HandModels.IsVisible = hands && !replacementPreview;
        ReplacementPartPreview.IsVisible = replacementPreview;
        PartLabel.Text = hands ? "Preview hands" : "Preview head";
        if (!replacementPreview) return;
        FactionAppearance appearance = ActiveReplacement ?? RangersAssaultAppearance.StockA;
        if (appearance.CustomAssetFolder is not null)
        {
            ReplacementPartPreview.Text = hands ? "Imported matching hands" : "Head included in imported body";
            return;
        }
        ReplacementPartPreview.Text = hands
            ? RangersAssaultAppearance.ViewHands.Single(choice => choice.Model == appearance.ViewHands).Label
            : appearance.Head is null ? "Stock random heads · showing one" :
                RangersAssaultAppearance.Heads.Single(choice => choice.Model == appearance.Head).Label;
    }

    private void ReplaceAppearance_Click(object? sender, RoutedEventArgs e)
    {
        if (!EditableVariant) return;
        ActiveEditing = true;
        ShowReplacementState();
        RequestPreview(clear: true);
    }

    private void RestoreStock_Click(object? sender, RoutedEventArgs e)
    {
        if (!EditableVariant) return;
        ActiveReplacement = null;
        ActiveEditing = false;
        ShowReplacementState();
        RequestPreview(clear: true);
    }

    private void Replacement_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating || !EditableVariant || !ActiveEditing ||
            ReplacementBody.SelectedItem is not AppearanceChoice body ||
            ReplacementHead.SelectedItem is not AppearanceChoice head ||
            ReplacementHands.SelectedItem is not AppearanceChoice hands ||
            body.Model is null || hands.Model is null) return;
        var selected = new FactionAppearance(body.Model, head.Model, hands.Model);
        ActiveReplacement = selected == RangersAssaultAppearance.StockA ? null : selected;
        ShowReplacementState();
        RequestPreview(clear: true);
    }
    private void Head_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_updating) RequestPreview(clear: true);
    }
    private void Hands_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_ready || _updating) return;
        UpdatePreviewPartControls();
        RequestPreview(clear: true);
    }

    private void PreviewLod_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_updating) RequestPreview(clear: true);
    }

    private void RequestPreview(bool clear = false, bool interactive = false)
    {
        if (clear)
        {
            // A different appearance invalidates the old image. Camera motion must
            // still publish completed frames or continuous input starves the preview.
            _previewRevision++;
            PreviewImage.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            PreviewMessage.Text = "Loading appearance…";
            PreviewGeometry.Text = "";
        }
        if (!_ready || _closed) return;
        if (interactive)
        {
            _interactive = true;
            _settleTimer.Stop();
            _settleTimer.Start();
        }
        _pending = true;
        if (!_rendering) _ = RenderPendingAsync();
    }

    private async Task RenderPendingAsync()
    {
        if (_rendering || !_pending || _closed) return;
        _rendering = true;
        _pending = false;
        int revision = _previewRevision;
        try
        {
            if (_preview is null)
            {
                PreviewMessage.Text = "The included player models are unavailable. Install the bundled PS3 build assets to preview them.";
                return;
            }
            if (Variants.SelectedItem is not ModelChoice model)
            {
                PreviewTitle.Text = ActiveFaction.Name;
                PreviewMessage.Text = _models.Length == 0 ? "No player models are available for this faction." : "No models match your search.";
                return;
            }
            bool replacementPreview = EditableVariant && (ActiveReplacement is not null || ActiveEditing);
            FactionAppearance? replacement = replacementPreview ? ActiveReplacement : null;
            string body = replacement?.Body ?? model.Name;
            string? head = replacement?.HeadIncluded == true ? null :
                !replacementPreview ? (Heads.SelectedItem as ModelChoice)?.Name :
                replacement?.Head ?? "head_us_army_a";
            string? handModel = replacementPreview
                ? (replacement ?? RangersAssaultAppearance.StockA).ViewHands
                : (HandModels.SelectedItem as ModelChoice)?.Name;
            string? customRoot = replacement?.CustomAssetFolder is not { } folder ? null :
                Path.Combine(MapFactionAuthoring.GetCharacterAssetsDirectory(_mapFilePath ??
                    throw new InvalidOperationException("Save the map before previewing imported appearances.")), folder);
            bool hands = HandsPreview.IsChecked == true;
            PreviewTitle.Text = hands ? $"{ActiveFaction.Name} · View hands" :
                !replacementPreview ? model.Label : $"{model.Label} · Replacement";
            if (hands && handModel is null) throw new InvalidOperationException("No viewhands are available for this faction.");
            float yaw = _yaw, pitch = _pitch, zoom = _zoom;
            Vector2 pan = _pan;
            int size = _interactive ? 320 : 640;
            int lod = (PreviewLod.SelectedItem as LodChoice)?.Index ?? 0;
            FactionModelPreview.Frame frame = await Task.Run(() => hands && handModel is not null
                ? _preview.RenderHands(handModel, size, yaw, pitch, zoom, pan, customRoot, lod)
                : _preview.Render(body, head, size, yaw, pitch, zoom, pan, customRoot, lod));
            if (_closed || revision != _previewRevision) { frame.Bitmap.Dispose(); return; }
            UpdatePreviewLods(frame.AvailableLods, frame.Lod);
            PreviewImage.Source = frame.Bitmap;
            _bitmap?.Dispose();
            _bitmap = frame.Bitmap;
            PreviewGeometry.Text = $"{frame.Triangles:N0} triangles · {frame.Vertices:N0} vertices";
            PreviewMessage.Text = "";
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (!_closed && revision == _previewRevision) PreviewMessage.Text = $"This appearance could not be previewed.\n{exception.Message}";
        }
        finally
        {
            _rendering = false;
            if (_closed) CleanupImportedFolders();
            // Keep one render in flight and take the newest camera pose next,
            // without another timer delay or a queue of obsolete mouse positions.
            if (_pending && !_closed) Dispatcher.UIThread.Post(async () => await RenderPendingAsync());
        }
    }

    private void UpdatePreviewLods(IReadOnlyList<int> available, int selected)
    {
        if (PreviewLod.ItemsSource is not IEnumerable<LodChoice> current ||
            !current.Select(choice => choice.Index).SequenceEqual(available))
        {
            _updating = true;
            try
            {
                PreviewLod.ItemsSource = available.Select(index => new LodChoice(index)).ToArray();
                PreviewLod.SelectedItem = ((IEnumerable<LodChoice>)PreviewLod.ItemsSource)
                    .Single(choice => choice.Index == selected);
            }
            finally { _updating = false; }
        }
        else if (PreviewLod.SelectedItem is not LodChoice { Index: var index } || index != selected)
        {
            _updating = true;
            try { PreviewLod.SelectedItem = current.Single(choice => choice.Index == selected); }
            finally { _updating = false; }
        }
        PreviewLod.IsEnabled = available.Count > 1 && !_importing;
    }

    private void ResetView_Click(object? sender, RoutedEventArgs e) => ResetPreviewView();

    private void ResetPreviewView()
    {
        FinishPreviewGesture();
        _settleTimer.Stop();
        _interactive = false;
        _yaw = -45; _pitch = 10; _zoom = 1;
        _pan = Vector2.Zero;
        RequestPreview();
    }

    private void Preview_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.End) return;
        ResetPreviewView();
        e.Handled = true;
    }

    private void Preview_Pressed(object? sender, PointerPressedEventArgs e)
    {
        PreviewImage.Focus(NavigationMethod.Pointer, e.KeyModifiers);
        var kind = e.GetCurrentPoint(PreviewImage).Properties.PointerUpdateKind;
        if (kind is not (PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed)) return;
        FinishPreviewGesture();
        _lastPointer = _pressPoint = e.GetPosition(PreviewImage);
        _dragPointer = e.Pointer;
        bool middle = kind == PointerUpdateKind.MiddleButtonPressed;
        _dragButton = middle ? MouseButton.Middle : MouseButton.Right;
        _panning = middle || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _navigationMoved = false;
        e.Pointer.Capture(PreviewImage);
        e.Handled = true;
    }
    private void Preview_Moved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer)) return;
        Point point = e.GetPosition(PreviewImage);
        Avalonia.Vector fromPress = point - _pressPoint;
        if (!_navigationMoved && fromPress.SquaredLength < 16) return;
        _navigationMoved = true;
        Avalonia.Vector delta = point - _lastPointer;
        _lastPointer = point;
        if (_panning)
        {
            // The bitmap is square and uniformly fitted inside the preview control.
            float extent = (float)Math.Max(1, Math.Min(PreviewImage.Bounds.Width, PreviewImage.Bounds.Height));
            _pan += new Vector2((float)delta.X, (float)delta.Y) / extent;
        }
        else
        {
            const float degrees = 180 / MathF.PI;
            _yaw -= (float)delta.X * CameraNavigation.OrbitSensitivity * degrees;
            _pitch = Math.Clamp(_pitch + (float)delta.Y * CameraNavigation.OrbitSensitivity * degrees,
                -1.5f * degrees, 1.5f * degrees);
        }
        RequestPreview(interactive: true);
        e.Handled = true;
    }
    private void Preview_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _dragPointer) || e.InitialPressMouseButton != _dragButton) return;
        FinishPreviewGesture();
        e.Handled = true;
    }
    private void Preview_CaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishPreviewGesture();
    private void FinishPreviewGesture()
    {
        IPointer? pointer = _dragPointer;
        _dragPointer = null;
        pointer?.Capture(null);
    }
    private void Preview_Wheel(object? sender, PointerWheelEventArgs e)
    {
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(delta) || delta == 0) return;
        _zoom = Math.Clamp(_zoom * MathF.Exp((float)delta * CameraNavigation.ZoomSensitivity), 0.5f, 3);
        RequestPreview(interactive: true);
        e.Handled = true;
    }
    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        string allies = SelectedFaction(AlliesFaction).Id, axis = SelectedFaction(AxisFaction).Id;
        _appliedSettings = new MapFactionSettings(allies, axis,
            allies == MapFactionAuthoring.UsArmy ? _alliesAssaultA : null,
            axis == MapFactionAuthoring.UsArmy ? _axisAssaultA : null);
        Close(_appliedSettings);
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
