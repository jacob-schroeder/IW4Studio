using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using IW4.Game.Assets.ColMap;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

public sealed partial class AddonMapEntsEditorView : UserControl
{
    private AddonMapEntsEditorViewModel Editor => (AddonMapEntsEditorViewModel)DataContext!;
    private MissionObject? _drawn;
    private bool _refreshing;
    private FieldEditor? _pendingField;
    private readonly Dictionary<StackPanel, int> _fieldCounts = [];
    private bool _adding;
    private bool _compact;
    private bool _basePrepared;
    private string? _basePath;
    private ClipMapAsset? _loadedBase;

    public AddonMapEntsEditorView()
    {
        InitializeComponent();
        PlanePicker.SelectedIndex = 0;
        Scene.SizeChanged += FirstSceneLayout;
        Scene.SelectionRequested += i => { CloseDrawer(); Editor.Select(i); };
        Scene.EditRequested += edit => Editor.CommitGesture(edit);
        Scene.PlacementRequested += p => Editor.Place(p);
        Scene.RelinkRequested += i => Editor.ChooseTarget(i);
        Scene.CancelRequested += Cancel;
        BrowseButton.Click += (_, _) => ToggleDrawer(false);
        AddButton.Click += (_, _) => ToggleDrawer(true);
        CloseDrawerButton.Click += (_, _) => CloseDrawer();
        SearchBox.TextChanged += (_, _) => { if (Drawer.IsVisible) FillDrawer(); };
        UndoButton.Click += (_, _) => Editor.Undo();
        ApplyButton.Click += (_, _) =>
        {
            _pendingField?.Commit();
            if (_pendingField == null && !Editor.NeedsConfirmation) Editor.Apply();
        };
        CancelToolButton.Click += (_, _) => Cancel();
        FitButton.Click += (_, _) => Scene.FitView();
        FrameButton.Click += (_, _) => Scene.FrameSelected();
        ZoomInButton.Click += (_, _) => Scene.Zoom(1.5);
        ZoomOutButton.Click += (_, _) => Scene.Zoom(1 / 1.5);
        PlanePicker.SelectionChanged += (_, _) => Scene.Plane = PlanePicker.SelectedIndex == 1 ? AddonEntityScenePlane.Side : AddonEntityScenePlane.Top;
        DuplicateButton.Click += (_, _) => { CloseDrawer(); Editor.StartPlace("copy"); Scene.Focus(); };
        ChooseTargetButton.Click += (_, _) => { CloseDrawer(); Editor.StartRelink(); Scene.Focus(); };
        LinkChoices.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && LinkChoices.SelectedItem is LinkChoice link) ShowObject(link.Index);
        };
        KeepButton.Click += (_, _) => Cancel();
        ConfirmButton.Click += (_, _) => Editor.Confirm();
        DeleteButton.Click += (_, _) => Editor.DeleteObject();
        AddFieldButton.Click += (_, _) =>
        {
            Editor.AddField(NewKey.Text ?? "");
            if (Editor.Selected?.Properties.Any(p => p.Key == NewKey.Text?.Trim()) == true) NewKey.Text = "";
        };
        BaseButton.Click += async (_, _) => await ToggleBase();
        SizeChanged += (_, _) => UpdateLayoutMode();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is AddonMapEntsEditorViewModel)
            { Cancel(); e.Handled = true; }
        };
    }

    internal AddonMapEntsEditorView(AddonMapEntsEditorViewModel editor, FastFileWorkspace? workspace) : this()
    {
        DataContext = editor;
        editor.Changed += Refresh;
        if (workspace?.LoadedZones.FirstOrDefault() is { } zone)
            _loadedBase = MissionBaseMap.Find(zone.LoadResult.Context.AssetPool.Entries.Select(e => e.Asset).OfType<ClipMapAsset>(), editor.Heading);
        _basePath = MissionBaseMap.Nearby(workspace, editor.Heading);
        BaseButton.IsVisible = _loadedBase != null || _basePath != null;
        BaseButton.Content = "Show base map";
        ToolTip.SetTip(BaseButton, "Show read-only collision triangles and brush bounds from the matching map");
        Refresh();
        if (_loadedBase != null) _ = ToggleBase();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is AddonMapEntsEditorViewModel editor) editor.RefreshWorkspace();
    }

    private void FirstSceneLayout(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 1 || e.NewSize.Height < 1 || DataContext is not AddonMapEntsEditorViewModel editor) return;
        Scene.SizeChanged -= FirstSceneLayout;
        if (editor.Selected?.Scene is null) return;
        Scene.FrameSelected();
        Scene.Zoom(0.35);
    }

    private void UpdateLayoutMode()
    {
        bool compact = Bounds.Width < 720;
        if (compact == _compact) return;
        _compact = compact;
        BodyGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,285");
        BodyGrid.RowDefinitions = new RowDefinitions(compact ? "3*,2*" : "*");
        BodyGrid.RowSpacing = compact ? 5 : 0;
        Grid.SetColumn(InspectorPane, compact ? 0 : 1);
        Grid.SetRow(InspectorPane, compact ? 1 : 0);
    }

    private async Task ToggleBase()
    {
        if (!BaseButton.IsEnabled) return;
        BaseButton.IsEnabled = false;
        try
        {
            if (!_basePrepared)
            {
                BaseButton.Content = "Loading base map…";
                var loaded = _loadedBase;
                var path = _basePath;
                var mission = Editor.Heading;
                var paths = await Task.Run(() => loaded != null ? MissionBaseMap.Outlines(loaded)
                    : MissionBaseMap.Load(path ?? throw new InvalidDataException("The base map is unavailable."), mission));
                Scene.SetBackdrop(paths);
                _basePrepared = true;
            }
            Scene.ShowBackdrop = !Scene.ShowBackdrop;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Editor.Message($"Couldn’t open the base map. {ex.Message}"); }
        finally
        {
            BaseButton.Content = Scene.ShowBackdrop ? "Hide base map" : "Show base map";
            BaseButton.IsEnabled = true;
        }
    }

    private void Cancel()
    {
        CloseDrawer();
        if (_pendingField is { } field)
        {
            _pendingField = null;
            field.Input.Text = field.Original;
        }
        Editor.Cancel();
    }

    private void ToggleDrawer(bool add)
    {
        if (Drawer.IsVisible && _adding == add) { CloseDrawer(); return; }
        if (Editor.Mode == AddonEntitySceneMode.Place || (add && Editor.Mode != AddonEntitySceneMode.Normal)) Editor.Cancel();
        _adding = add;
        DrawerTitle.Text = add ? "Add to this mission" : "Mission objects";
        SearchBox.IsVisible = !add;
        SearchBox.Text = "";
        Drawer.IsVisible = true;
        FillDrawer();
        if (!add) SearchBox.Focus();
    }

    private void CloseDrawer() => Drawer.IsVisible = false;

    private void ShowObject(int index)
    {
        var relinking = Editor.Mode == AddonEntitySceneMode.Relink;
        CloseDrawer();
        Editor.Select(index);
        if (!relinking && Editor.Selected?.Scene != null) Scene.FrameSelected();
    }

    private void FillDrawer()
    {
        DrawerItems.Children.Clear();
        if (_adding)
        {
            AddChoice("Marker with radius", "Mark a location and the area around it", () => Start("marker"));
            AddChoice("Player start", "Place a starting point and choose its facing", () => Start("start"));
            return;
        }
        var groups = Editor.Objects.Where(o => o.Contains(SearchBox.Text?.Trim() ?? ""))
            .GroupBy(o => o.Group).OrderBy(g => g.Key);
        foreach (var group in groups)
        {
            var items = new StackPanel { Spacing = 3 };
            bool searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
            var category = new Expander
            {
                Header = $"{group.Key} · {group.Count()}", Content = items,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = searching || group.Key == Editor.Selected?.Group
            };
            DrawerItems.Children.Add(category);
            foreach (var names in group.GroupBy(o => o.Get("targetname")))
            {
                var list = items;
                if (!string.IsNullOrEmpty(names.Key) && names.Count() > 1)
                {
                    list = new StackPanel { Spacing = 3 };
                    items.Children.Add(new Expander
                    {
                        Header = $"{names.Key} · {names.Count()}", Content = list,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = searching
                    });
                }
                foreach (var o in names) AddChoice(o.Title, o.Detail, () => ShowObject(o.Index), list);
            }
        }
        if (DrawerItems.Children.Count == 0) DrawerItems.Children.Add(Caption("No matching objects."));
    }

    private void Start(string template)
    {
        CloseDrawer();
        Editor.StartPlace(template);
        Scene.Focus();
    }

    private void AddChoice(string title, string detail, Action action, StackPanel? list = null)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(7, 3),
            Content = new StackPanel { Children = { new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis }, Caption(detail) } }
        };
        AutomationProperties.SetName(button, $"{title} · {detail}");
        button.Click += (_, _) => action();
        (list ?? DrawerItems).Children.Add(button);
    }

    private static TextBlock Caption(string text)
    {
        var result = new TextBlock { Text = text };
        result.Classes.Add("caption");
        return result;
    }

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var vm = Editor;
            var o = vm.Selected;
            AssetName.Text = vm.Heading;
            StatusText.Text = vm.Status;
            UndoButton.IsEnabled = vm.CanUndo;
            ApplyButton.IsEnabled = (vm.CanApply || _pendingField != null) && !vm.NeedsConfirmation;
            AddButton.IsEnabled = vm.IsEditable;
            BrowseButton.Content = $"Objects ({vm.Objects.Count})";
            SelectedGroup.Text = o?.Group;
            SelectedTitle.Text = o?.Title ?? "No object selected";
            SelectedDetail.Text = o?.Detail;
            FrameButton.IsEnabled = o?.Scene != null;
            DuplicateButton.IsVisible = vm.CanDuplicate;
            Scene.SetScene(vm.SceneObjects, vm.SelectedIndex);
            Scene.Ghost = vm.Ghost;
            Scene.Mode = vm.Mode;
            Hint.Text = vm.Hint;
            CancelToolButton.IsVisible = vm.Mode != AddonEntitySceneMode.Normal;
            ShapeText.IsVisible = o?.Shape != null;
            ShapeText.Text = o?.Shape;
            bool selectedChanged = !ReferenceEquals(o, _drawn);
            if (selectedChanged) { RenderFields(o); _drawn = o; }
            LinksSection.IsVisible = o is { Kind: not "world" };
            ChooseTargetButton.IsVisible = vm.IsEditable;
            if (o != null && selectedChanged)
            {
                var targets = vm.Destinations(o).ToArray(); var sources = vm.Sources(o).ToArray();
                LinkSummary.Text = !string.IsNullOrEmpty(o.Get("target")) && targets.Length == 0
                    ? $"“{o.Get("target")}” has no match in this overlay. Incoming: {sources.Length}."
                    : targets.Length + sources.Length == 0 ? "No connections in this overlay." : $"{targets.Length} outgoing · {sources.Length} incoming";
                LinkChoices.ItemsSource = targets.Select(t => new LinkChoice(t.Index, $"To · {t.Title} (#{t.Index + 1})"))
                    .Concat(sources.Select(s => new LinkChoice(s.Index, $"From · {s.Title} (#{s.Index + 1})"))).ToArray();
                LinkChoices.SelectedIndex = -1;
                LinkChoices.IsVisible = targets.Length + sources.Length > 0;
            }
            ConfirmPane.IsVisible = vm.NeedsConfirmation;
            ConfirmText.Text = vm.ConfirmationText;
            ConfirmButton.Content = vm.ConfirmationAction;
            if (vm.NeedsConfirmation) ConfirmPane.BringIntoView();
            PositionSection.IsVisible = o?.Scene != null;
            AdvancedSection.IsVisible = o != null;
            DeleteButton.IsVisible = vm.IsEditable && o is { Kind: not "world" };
            AddFieldButton.IsVisible = NewKey.IsVisible = vm.IsEditable;
            if (Drawer.IsVisible && selectedChanged) FillDrawer();
        }
        finally { _refreshing = false; }
    }

    private void RenderFields(MissionObject? o)
    {
        _pendingField = null;
        _fieldCounts[MainFields] = _fieldCounts[PositionFields] = _fieldCounts[RawFields] = 0;
        if (o == null)
        {
            MainFields.Children.Clear(); PositionFields.Children.Clear(); RawFields.Children.Clear();
            return;
        }
        if (o.Scene is { } scene)
        {
            if (scene.Radius is { } radius)
                Field(MainFields, "Radius", MissionObject.Number(radius), v => Editor.SetField("radius", v));
            if (MissionObject.ParseVector(o.Get("angles")) is { } angles)
                FloatField(MainFields, "Facing (°)", angles.Y, yaw => Editor.SetField("angles", MissionObject.Vector(angles with { Y = yaw })));
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                FloatField(PositionFields, new[] { "X", "Y", "Height (Z)" }[i], scene.Origin[i], v =>
                {
                    var point = scene.Origin;
                    point[axis] = v;
                    Editor.SetField("origin", MissionObject.Vector(point));
                });
            }
        }
        foreach (var (key,label) in FriendlyFields)
            if (o.Get(key) is string value) Field(MainFields, label, value, v => Editor.SetField(key, v));
        for (int i = 0; i < o.Properties.Count; i++)
        {
            var p = o.Properties[i]; int index = i;
            Field(RawFields, p.Key, p.Value, v => Editor.SetField(p.Key, v, index), () => Editor.RemoveField(index));
        }
        foreach (var (panel,count) in _fieldCounts)
            while (panel.Children.Count > count) panel.Children.RemoveAt(panel.Children.Count - 1);
    }

    private static readonly (string Key, string Label)[] FriendlyFields =
    [
        ("script_flag", "Mission flag"), ("script_ammo_max", "Maximum ammo"), ("modelscale", "Model scale"),
        ("ambient", "Ambient light"), ("sunlight", "Sun brightness"), ("suncolor", "Sun color"),
        ("sundirection", "Sun direction"), ("skylight", "Sky brightness"), ("skycolor", "Sky color"), ("_color", "Ambient color")
    ];

    private void FloatField(StackPanel panel, string label, float value, Action<float> save) =>
        Field(panel, label, MissionObject.Number(value), v =>
        {
            if (MissionObject.ParseFloat(v) is float f) save(f); else Editor.Message("Enter a finite number.");
        });

    private void Field(StackPanel panel, string label, string value, Action<string> save, Action? remove = null)
    {
        int slot = _fieldCounts[panel]++;
        if (slot == panel.Children.Count)
        {
            var input = new TextBox { MinWidth = 0 };
            var caption = Caption(label);
            var removeButton = new Button { Content = "×", Padding = new Thickness(6, 0) };
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
            row.Children.Add(input);
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);
            var wrapper = new StackPanel { Spacing = 2, Children = { caption, row } };
            var field = new FieldEditor(input, caption, removeButton);
            wrapper.DataContext = field;
            panel.Children.Add(wrapper);
            removeButton.Click += (_, _) => field.Remove?.Invoke();
            input.TextChanged += (_, _) =>
            {
                if (_refreshing || !input.IsFocused || input.IsReadOnly) return;
                _pendingField = input.Text == field.Original ? null : field;
                ApplyButton.IsEnabled = (_pendingField != null || Editor.CanApply) && !Editor.NeedsConfirmation;
            };
            input.LostFocus += (_, _) => { if (!_refreshing && ReferenceEquals(field, _pendingField)) field.Commit(); };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { field.Commit(); e.Handled = true; }
            };
        }
        var current = (FieldEditor)panel.Children[slot].DataContext!;
        current.Caption.Text = label;
        current.Original = value;
        current.Save = v =>
        {
            if (!Editor.IsEditable || _refreshing) return;
            _pendingField = null;
            save(v);
            current.Input.Text = current.Original;
        };
        current.Remove = remove;
        current.RemoveButton.IsVisible = remove != null && Editor.IsEditable;
        current.Input.IsReadOnly = !Editor.IsEditable;
        current.Input.Text = value;
        AutomationProperties.SetName(current.Input, label);
        AutomationProperties.SetName(current.RemoveButton, $"Remove {label}");
        ToolTip.SetTip(current.RemoveButton, $"Remove {label}");
    }

    private sealed class FieldEditor(TextBox input, TextBlock caption, Button removeButton)
    {
        internal TextBox Input { get; } = input;
        internal TextBlock Caption { get; } = caption;
        internal Button RemoveButton { get; } = removeButton;
        internal string Original { get; set; } = "";
        internal Action<string>? Save { get; set; }
        internal Action? Remove { get; set; }
        internal void Commit()
        {
            if ((Input.Text ?? "") != Original) Save?.Invoke(Input.Text ?? "");
        }
    }

    private sealed record LinkChoice(int Index, string Text)
    { public override string ToString() => Text; }
}
