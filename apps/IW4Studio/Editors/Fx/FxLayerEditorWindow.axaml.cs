using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IW4.Studio.Fx;
using IW4.Studio.Documents;
using IW4.Game.Assets.Fx;

namespace IW4.Studio.Desktop.Editors.Fx;

public sealed partial class FxLayerEditorWindow : Window
{
    private AssetEditorSession? _editorSession;
    private FxSourceDocument? _workingDocument;
    private AssetEditorSession _session => _editorSession ??
        throw new InvalidOperationException("This layer editor has no asset session.");
    private FxSourceDocument _document => _workingDocument ??
        throw new InvalidOperationException("This layer editor has no FX document.");
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<FxSourceField, TextBox> _inputs = [];
    private int _selectedIndex;
    private bool _refreshing;
    private bool _approvedCloseRetry;
    private bool _closePromptPending;

    public FxLayerEditorWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };
        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            Preview.Clear();
            Preview.Dispose();
        };
        UndoButton.IsEnabled = false;
        RedoButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        DuplicateLayerButton.IsEnabled = false;
        RemoveLayerButton.IsEnabled = false;
        SetStatus("Open this dialog from an editable FX asset.");
    }

    public FxLayerEditorWindow(AssetEditorSession session) : this()
    {
        _editorSession = session ?? throw new ArgumentNullException(nameof(session));
        FxDraft draft = session.OpenDraft<FxDraft>();
        _workingDocument = FxSourceDocument.FromJson(draft.Json, draft.AssetName);
        RefreshDocument();
        SetStatus("Edit layers, then apply them to the workspace. Source Dump is a separate action.");
    }

    private void RefreshDocument(int? selectIndex = null)
    {
        _refreshing = true;
        try
        {
            IReadOnlyList<FxSourceLayer> layers = _document.Layers;
            LayerList.ItemsSource = layers.Select(layer =>
                $"{layer.Index + 1:00}  {layer.Type}\n{layer.Group} · {layer.Timing}\n{layer.Material}").ToArray();
            _selectedIndex = layers.Count == 0 ? -1 : Math.Clamp(selectIndex ?? _selectedIndex, 0, layers.Count - 1);
            LayerList.SelectedIndex = _selectedIndex;
            RefreshHeader();
            BuildInspector();
        }
        finally { _refreshing = false; }
        SchedulePreview();
    }

    private void RefreshHeader()
    {
        string dirty = _document.IsDirty ? " • local edits" : "";
        AssetTitle.Text = _document.AssetName + dirty;
        PathText.Text = "Workspace FX · use Studio Source Dump to export files";
        Title = $"{_document.AssetName}{(_document.IsDirty ? " *" : "")} — Edit FX layers";
        UndoButton.IsEnabled = _document.CanUndo;
        RedoButton.IsEnabled = _document.CanRedo;
        ApplyButton.IsEnabled = _document.IsDirty;
    }

    private void BuildInspector()
    {
        Inspector.Children.Clear();
        _inputs.Clear();
        FxSourceLayer? layer = _document.Layers.ElementAtOrDefault(_selectedIndex);
        bool editable = layer?.Editable == true;
        DuplicateLayerButton.IsEnabled = editable;
        RemoveLayerButton.IsEnabled = editable;
        if (layer is null)
        {
            Inspector.Children.Add(new TextBlock
            {
                Text = "This FX has no layers. Choose a loaded effect as a starting point, or use Undo to restore removed layers.",
                TextWrapping = TextWrapping.Wrap
            });
            var start = new Button { Content = "Start from effect…", HorizontalAlignment = HorizontalAlignment.Left };
            start.Click += async (_, _) => await StartFromEffectAsync();
            Inspector.Children.Add(start);
            return;
        }
        Inspector.Children.Add(new TextBlock
        {
            Text = $"{layer.Type} · {layer.Group}", FontSize = 17,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        if (!editable)
        {
            Inspector.Children.Add(new TextBlock
            {
                Text = "This layer is kept exactly as authored in the source graph. Its specialized fields are read-only here.",
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        AddHeading("Appearance");
        AddInput("Material", FxSourceField.Material, browseMaterial: true);
        AddInput("Width", FxSourceField.SizeX);
        AddInput("Height", FxSourceField.SizeY);
        AddInput("Red (0–255)", FxSourceField.Red);
        AddInput("Green (0–255)", FxSourceField.Green);
        AddInput("Blue (0–255)", FxSourceField.Blue);
        AddInput("Opacity (0–255)", FxSourceField.Opacity);

        AddHeading("Timing");
        AddInput("Lifetime (ms)", FxSourceField.LifeBase);
        AddInput("Lifetime variation (ms)", FxSourceField.LifeAmplitude);
        AddInput(layer.Group == "Looping" ? "Spawn interval (ms)" : "One-shot base count",
            FxSourceField.SpawnInterval);
        AddInput(layer.Group == "Looping" ? "Spawn count (∞ allowed)" : "One-shot count variation",
            FxSourceField.SpawnCount);

        AddHeading("Motion");
        AddInput("Gravity", FxSourceField.Gravity);
        AddInput("Initial rotation (radians)", FxSourceField.Rotation);
        AddInput("Authored local speed", FxSourceField.Speed);
        Inspector.Children.Add(new TextBlock
        {
            Text = "Speed scales the authored velocity curve and its stored total deltas together. Layers with no authored motion need a moving template.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#99A5B4"))
        });
    }

    private void AddHeading(string text)
    {
        Inspector.Children.Add(new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 0, 0, 5),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.Parse("#405060")),
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold }
        });
    }

    private void AddInput(string label, FxSourceField field, bool browseMaterial = false)
    {
        var input = new TextBox
        {
            Text = _document.ReadField(_selectedIndex, field),
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = field
        };
        if (field is FxSourceField.SizeX or FxSourceField.SizeY)
            ToolTip.SetTip(input, "Uniform-size layers link Width and Height. Editing Height separates the two values.");
        input.LostFocus += (_, _) => CommitField(field);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitField(field);
                e.Handled = true;
            }
        };
        _inputs.Add(field, input);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(input);
        if (browseMaterial)
        {
            var browse = new Button { Content = "Choose…", MinWidth = 75 };
            browse.Click += async (_, _) => await ChooseMaterialAsync();
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);
        }
        Inspector.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontSize = 11 },
                row
            }
        });
    }

    private bool CommitField(FxSourceField field)
    {
        if (_refreshing || _selectedIndex < 0 || !_inputs.TryGetValue(field, out TextBox? input)) return true;
        string text = input.Text ?? "";
        if (text == _document.ReadField(_selectedIndex, field)) return true;
        try
        {
            _document.SetField(_selectedIndex, field, text);
            RefreshFieldValues();
            SetStatus("Change committed locally. Apply to add it to the workspace draft.");
            RefreshHeader();
            RefreshLayerLabels();
            SchedulePreview();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
            ArgumentException or System.Text.Json.JsonException)
        {
            SetStatus(exception.Message, error: true);
            input.Text = _document.ReadField(_selectedIndex, field);
            return false;
        }
    }

    private void RefreshFieldValues()
    {
        foreach ((FxSourceField field, TextBox input) in _inputs)
        {
            string value = _document.ReadField(_selectedIndex, field);
            if (input.Text == value) continue;
            int caret = input.CaretIndex;
            input.Text = value;
            if (input.IsFocused)
                input.CaretIndex = Math.Min(caret, value.Length);
        }
    }

    private bool CommitPendingFields()
    {
        foreach (FxSourceField field in _inputs.Keys.ToArray())
            if (!CommitField(field)) return false;
        return true;
    }

    private void RefreshLayerLabels()
    {
        _refreshing = true;
        try
        {
            int selection = _selectedIndex;
            LayerList.ItemsSource = _document.Layers.Select(layer =>
                $"{layer.Index + 1:00}  {layer.Type}\n{layer.Group} · {layer.Timing}\n{layer.Material}").ToArray();
            LayerList.SelectedIndex = selection;
        }
        finally { _refreshing = false; }
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        try
        {
            Preview.SetEffect(_session.Workspace, _document.Effect);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException)
        {
            Preview.Clear();
            SetStatus($"Preview unavailable: {exception.Message}", error: true);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.Parse(error ? "#F2A0A0" : "#9CAABB"));
    }

    private void LayerList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        int selected = LayerList.SelectedIndex;
        if (selected == _selectedIndex) return;
        if (!CommitPendingFields())
        {
            _refreshing = true;
            LayerList.SelectedIndex = _selectedIndex;
            _refreshing = false;
            return;
        }
        _selectedIndex = selected;
        BuildInspector();
    }

    private void DuplicateLayer_Click(object? sender, RoutedEventArgs e)
    {
        if (!CommitPendingFields()) return;
        try
        {
            _document.DuplicateLayer(_selectedIndex);
            RefreshDocument(_selectedIndex + 1);
            SetStatus("Layer duplicated.");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or OverflowException)
        { SetStatus(exception.Message, error: true); }
    }

    private void RemoveLayer_Click(object? sender, RoutedEventArgs e)
    {
        if (!CommitPendingFields()) return;
        try
        {
            _document.RemoveLayer(_selectedIndex);
            RefreshDocument(_selectedIndex);
            SetStatus("Layer removed. Undo restores it.");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or OverflowException)
        { SetStatus(exception.Message, error: true); }
    }

    private void Undo_Click(object? sender, RoutedEventArgs e)
    {
        _document.Undo();
        RefreshDocument();
    }

    private void Redo_Click(object? sender, RoutedEventArgs e)
    {
        _document.Redo();
        RefreshDocument();
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) => ApplyChanges();

    private bool ApplyChanges()
    {
        if (!CommitPendingFields()) return false;
        try
        {
            if (_document.IsDirty)
            {
                _session.Apply<FxDraft>(draft => draft.ReplaceJson(_document.Json));
                if (_session.Validation.HasErrors)
                    throw new InvalidDataException(_session.Validation.Issues[0].Message);
            }
            _approvedCloseRetry = true;
            Close(true);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
            ArgumentException or OverflowException or System.Text.Json.JsonException)
        {
            SetStatus($"Apply failed: {exception.Message}", error: true);
            return false;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _approvedCloseRetry = true;
        Close(false);
    }

    private async Task ChooseMaterialAsync()
    {
        string[] names = _session.Workspace.AssetCatalog.Entries
            .Where(entry => entry.AssetType == IW4.Game.Zone.XAssetType.Material && entry.HasDefinition)
            .Select(entry => entry.OriginalName)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith(','))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string? selected = await new MaterialNameDialog(_session.Workspace, names,
            _document.ReadField(_selectedIndex, FxSourceField.Material)).ShowDialog<string?>(this);
        if (selected is null || !_inputs.TryGetValue(FxSourceField.Material, out TextBox? input)) return;
        input.Text = selected;
        CommitField(FxSourceField.Material);
    }

    private async Task StartFromEffectAsync()
    {
        if (_document.Layers.Count != 0) return;
        string[] names = _session.Workspace.AssetCatalog.Entries
            .Where(entry => entry.AssetType == IW4.Game.Zone.XAssetType.Fx && entry.HasDefinition)
            .Select(entry => entry.OriginalName)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith(','))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var choices = new List<(string Name, FxEffectDefAsset Asset)>();
        foreach (string name in names)
            if (_session.TryResolveWorkspaceDefinition<FxEffectDefAsset>(name,
                    out FxEffectDefAsset? asset) && asset is { ElemDefs.Count: > 0 })
                choices.Add((name, asset));
        choices.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        if (choices.Count == 0)
        {
            SetStatus("No loaded FX with layers is available as a starting point.", error: true);
            return;
        }
        string? selected = await new FxTemplateDialog(choices.Select(choice => choice.Name).ToArray())
            .ShowDialog<string?>(this);
        FxEffectDefAsset? template = choices.FirstOrDefault(choice => choice.Name == selected).Asset;
        if (template is null) return;
        try
        {
            _document.StartFromTemplate(template);
            RefreshDocument(0);
            SetStatus($"Started from {selected}. Apply to add this copy to the workspace draft.");
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
            ArgumentException or OverflowException or System.Text.Json.JsonException)
        {
            SetStatus($"Could not use that FX: {exception.Message}", error: true);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_workingDocument is null)
        {
            base.OnKeyDown(e);
            return;
        }
        bool modifier = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (modifier && e.Key == Key.S)
        {
            ApplyChanges();
            e.Handled = true;
        }
        else if (modifier && e.Key == Key.Z)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) _document.Redo();
            else _document.Undo();
            RefreshDocument();
            e.Handled = true;
        }
        else if (modifier && e.Key == Key.Y)
        {
            _document.Redo();
            RefreshDocument();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_workingDocument is null)
        {
            base.OnClosing(e);
            return;
        }
        if (_approvedCloseRetry)
        {
            _approvedCloseRetry = false;
            base.OnClosing(e);
            return;
        }
        if (_closePromptPending)
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }
        if (!CommitPendingFields())
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }
        if (_document.IsDirty)
        {
            e.Cancel = true;
            if (!_closePromptPending)
            {
                _closePromptPending = true;
                _ = ConfirmCloseAsync();
            }
        }
        base.OnClosing(e);
    }

    private async Task ConfirmCloseAsync()
    {
        try
        {
            CloseDecision decision = await new UnsavedFxDialog(_document.AssetName).ShowDialog<CloseDecision>(this);
            if (decision == CloseDecision.Cancel) return;
            if (decision == CloseDecision.Apply)
            {
                ApplyChanges();
                return;
            }
            _approvedCloseRetry = true;
            Close(false);
        }
        finally { _closePromptPending = false; }
    }

    private enum CloseDecision { Cancel, Apply, Discard }

    private sealed class UnsavedFxDialog : Window
    {
        public UnsavedFxDialog(string name)
        {
            Title = "Unapplied FX edits";
            Width = 480;
            MinWidth = 480;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var actions = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            foreach ((string label, CloseDecision decision) in new[]
                { ("Keep editing", CloseDecision.Cancel), ("Discard", CloseDecision.Discard), ("Apply", CloseDecision.Apply) })
            {
                var button = new Button { Content = label, MinWidth = 90 };
                button.Click += (_, _) => Close(decision);
                actions.Children.Add(button);
            }
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 18,
                Children =
                {
                    new TextBlock { Text = $"Apply layer changes to {name}?", FontSize = 19,
                        FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "The changes are still local to this dialog.",
                        TextWrapping = TextWrapping.Wrap },
                    actions
                }
            };
        }
    }

    private sealed class FxTemplateDialog : Window
    {
        public FxTemplateDialog(IReadOnlyList<string> names)
        {
            Title = "Start from loaded FX";
            Width = 560;
            Height = 500;
            MinWidth = 440;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var search = new TextBox { PlaceholderText = "Search loaded FX" };
            var list = new ListBox();
            void Filter() => list.ItemsSource = names.Where(name =>
                name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Take(250).ToArray();
            search.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) Filter();
            };
            Filter();
            var cancel = new Button { Content = "Cancel", MinWidth = 90 };
            cancel.Click += (_, _) => Close((string?)null);
            var use = new Button { Content = "Use effect", MinWidth = 110 };
            use.Click += (_, _) =>
            {
                if (list.SelectedItem is string selected) Close(selected);
            };
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancel, use }
            };
            Content = new Grid
            {
                Margin = new Thickness(18),
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 8,
                Children = { search, list, actions }
            };
            Grid.SetRow(list, 1);
            Grid.SetRow(actions, 2);
        }
    }

    private sealed class MaterialNameDialog : Window
    {
        private readonly FastFileWorkspace _workspace;
        private readonly Image _image = new() { Stretch = Stretch.Uniform };
        private readonly TextBlock _previewStatus = new()
        {
            Text = "Select a material to preview it.", TextWrapping = TextWrapping.Wrap,
            FontSize = 11
        };
        private Bitmap? _bitmap;
        private int _previewRevision;

        public MaterialNameDialog(FastFileWorkspace workspace, IReadOnlyList<string> names, string current)
        {
            _workspace = workspace;
            Title = "Choose workspace material";
            Width = 760;
            Height = 620;
            MinWidth = 600;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var search = new TextBox { PlaceholderText = "Search materials", Text = current };
            var list = new ListBox();
            void Filter() => list.ItemsSource = names.Where(name =>
                name.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Take(250).ToArray();
            search.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) Filter();
            };
            Filter();
            list.SelectionChanged += (_, _) => _ = RefreshPreviewAsync(list.SelectedItem as string);
            list.SelectedItem = current;
            var use = new Button { Content = "Use material", HorizontalAlignment = HorizontalAlignment.Right };
            use.Click += (_, _) =>
            {
                if (list.SelectedItem is string selected) Close(selected);
            };
            var materialArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*,280"), ColumnSpacing = 12 };
            materialArea.Children.Add(list);
            var previewArea = new Grid { ColumnDefinitions = new ColumnDefinitions("*"),
                RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8 };
            previewArea.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#202830")),
                Child = _image, Padding = new Thickness(10)
            });
            Grid.SetRow(_previewStatus, 1);
            previewArea.Children.Add(_previewStatus);
            Grid.SetColumn(previewArea, 1);
            materialArea.Children.Add(previewArea);
            Content = new Grid
            {
                Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 8,
                Children = { search, materialArea, use }
            };
            Grid.SetRow(materialArea, 1);
            Grid.SetRow(use, 2);
            Closed += (_, _) =>
            {
                _previewRevision++;
                _image.Source = null;
                _bitmap?.Dispose();
                _bitmap = null;
            };
        }

        private async Task RefreshPreviewAsync(string? materialName)
        {
            int revision = ++_previewRevision;
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            if (materialName is null)
            {
                _previewStatus.Text = "Select a material to preview it.";
                return;
            }
            _previewStatus.Text = $"Loading {materialName}…";
            try
            {
                Bitmap? bitmap = await FxSourcePreviewControl.LoadMaterialBitmapAsync(
                    _workspace, materialName);
                if (revision != _previewRevision)
                {
                    bitmap?.Dispose();
                    return;
                }
                _bitmap = bitmap;
                _image.Source = bitmap;
                _previewStatus.Text = bitmap is null
                    ? "This material has no image preview; its specialized surface is preserved."
                    : materialName;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException or NotSupportedException or
                System.Text.Json.JsonException)
            {
                if (revision == _previewRevision)
                    _previewStatus.Text = $"Preview unavailable: {exception.Message}";
            }
        }
    }
}
