using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class OrganizationInspector : UserControl
{
    private bool _updating;
    private MapEntity[] _groups = [];
    private string? _preferredLayer;

    public OrganizationInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        LayerBox.SelectionChanged += (_, _) => RefreshSelection(session);
        GroupBox.SelectionChanged += (_, _) => RefreshSelection(session);
        CreateLayerButton.Click += async (_, _) => await EditAsync(() =>
        {
            string name = LayerNameBox.Text?.Trim() ?? "";
            MapOrganization.CreateLayer(session.Document, name);
            foreach (object item in session.Selection.Items) MapOrganization.Assign(item, name);
            _preferredLayer = name;
            LayerNameBox.Text = "";
        });
        RenameLayerButton.Click += async (_, _) => await EditAsync(() =>
        {
            string name = LayerNameBox.Text?.Trim() ?? "";
            MapOrganization.RenameLayer(session.Document, CurrentLayer(), name);
            _preferredLayer = name;
            LayerNameBox.Text = "";
        });
        DeleteLayerButton.Click += async (_, _) => await EditAsync(() => MapOrganization.DeleteLayer(session.Document, CurrentLayer()));
        AssignLayerButton.Click += async (_, _) => await EditAsync(() =>
        {
            foreach (object item in session.Selection.Items) MapOrganization.Assign(item, CurrentLayer());
        });
        SelectLayerButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            string layer = CurrentLayer();
            session.SelectRange(MapOrganization.Objects(session.Document).Where(item =>
                (MapOrganization.Layer(item) == layer || MapOrganization.Layer(item).StartsWith(layer + "/", StringComparison.Ordinal)) &&
                session.Visibility.CanSelect(session.Document, item)));
        };
        HideLayerCheck.IsCheckedChanged += async (_, _) => await SetFlagAsync("hidden", HideLayerCheck.IsChecked == true);
        FreezeLayerCheck.IsCheckedChanged += async (_, _) => await SetFlagAsync("frozen", FreezeLayerCheck.IsChecked == true);
        GroupButton.Click += async (_, _) => await EditAsync(() =>
        {
            MapEntity group = MapOrganization.Group(session.Document, session.Selection.Items, GroupNameBox.Text?.Trim() ?? "");
            session.Selection.Set(group);
            GroupNameBox.Text = "";
        });
        SelectGroupButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || CurrentGroup() is not { } group) return;
            finishGestures();
            if (session.Visibility.CanSelect(session.Document, group)) session.Select(group);
        };
        UngroupButton.Click += async (_, _) => await EditAsync(() =>
        {
            if (CurrentGroup() is { } group) session.Selection.SetRange(MapOrganization.Ungroup(session.Document, group));
        });
        HideButton.Click += (_, _) => ChangeVisibility(() => session.Visibility.Hide(session.Selection.Items));
        IsolateButton.Click += (_, _) => ChangeVisibility(() => session.Visibility.Isolate(session.Selection.Items));
        FreezeButton.Click += (_, _) => ChangeVisibility(() => session.Visibility.Freeze(session.Selection.Items));
        RestoreButton.Click += (_, _) => ChangeVisibility(session.Visibility.Clear);
        RefreshSelection(session);

        async Task EditAsync(Action action)
        {
            if (_updating || dialogs.BlocksInput) return;
            try { finishGestures(); session.Edit(() => { action(); PruneSelection(); }); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
            { await dialogs.MessageAsync("Map organization", exception.Message); }
            RefreshSelection(session);
        }

        async Task SetFlagAsync(string flag, bool value) => await EditAsync(() =>
        {
            MapOrganization.SetLayerFlag(session.Document, CurrentLayer(), flag, value);
        });

        void PruneSelection()
        {
            session.Visibility.Invalidate();
            session.Selection.SetRange(session.Selection.Items.Where(item => session.Visibility.CanSelect(session.Document, item)));
        }
        void ChangeVisibility(Action action)
        {
            if (dialogs.BlocksInput) return;
            finishGestures(); action(); PruneSelection(); session.Refresh(); RefreshSelection(session);
        }
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            string layer = _preferredLayer ?? LayerBox.SelectedItem as string ??
                (session.Selection.Active is { } selected ? MapOrganization.Layer(selected) : MapOrganization.GlobalLayer);
            _preferredLayer = null;
            string[] layers = MapOrganization.Layers(session.Document);
            LayerBox.ItemsSource = layers;
            LayerBox.SelectedItem = layers.Contains(layer, StringComparer.Ordinal) ? layer : MapOrganization.GlobalLayer;
            HideLayerCheck.IsChecked = MapOrganization.LayerHasFlag(session.Document, CurrentLayer(), "hidden");
            FreezeLayerCheck.IsChecked = MapOrganization.LayerHasFlag(session.Document, CurrentLayer(), "frozen");
            AssignLayerButton.IsEnabled = session.Selection.Count > 0;
            RenameLayerButton.IsEnabled = DeleteLayerButton.IsEnabled = CurrentLayer() != MapOrganization.GlobalLayer;
            MapEntity? current = CurrentGroup();
            _groups = session.Document.Entities.Where(entity => entity.ClassName == "func_group").ToArray();
            GroupBox.ItemsSource = _groups.Select((entity, index) => entity.Properties.GetValueOrDefault("targetname", $"Group {index + 1}"))
                .ToArray();
            int index = session.Selection.Active is MapEntity active ? Array.IndexOf(_groups, active) : -1;
            if (index < 0 && current is not null) index = Array.IndexOf(_groups, current);
            GroupBox.SelectedIndex = index >= 0 ? index : _groups.Length > 0 ? 0 : -1;
            SelectGroupButton.IsEnabled = UngroupButton.IsEnabled = CurrentGroup() is not null;
            GroupButton.IsEnabled = session.Selection.Count > 0 && session.Selection.Items.All(item => item is MapBrush or MapTerrain);
            HideButton.IsEnabled = FreezeButton.IsEnabled = IsolateButton.IsEnabled = session.Selection.Count > 0;
            RestoreButton.IsEnabled = session.Visibility.IsActive;
        }
        finally { _updating = false; }
    }

    private string CurrentLayer() => LayerBox.SelectedItem as string ?? MapOrganization.GlobalLayer;
    private MapEntity? CurrentGroup() => (uint)GroupBox.SelectedIndex < _groups.Length ? _groups[GroupBox.SelectedIndex] : null;
}
