using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class GameplayEntityInspector : UserControl
{
    private MapEntity? _shownEntity;

    public GameplayEntityInspector() => InitializeComponent();
    internal event Action<string>? PlacementRequested;

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        Category.ItemsSource = new[] { "All types", "Spawns", "Script", "Triggers" };
        Category.SelectedIndex = 0;
        EntityFilter.TextChanged += (_, _) => FilterTypes();
        Category.SelectionChanged += (_, _) => FilterTypes();
        EntityTypes.SelectionChanged += (_, _) =>
        {
            if (EntityTypes.SelectedItem is not GameplayEntityType type) return;
            TypeDescription.Text = type.Description;
            CreateButton.Content = type.UsesBrushes ? "Create from selected brushes" : type.Name == "script_model" ? "Convert selected models" : "Place entity";
        };
        FilterTypes();
        CreateButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || EntityTypes.SelectedItem is not GameplayEntityType type) return;
            finishGestures();
            await ActAsync(() =>
            {
                if (type.UsesBrushes) GameplayEntityEditing.CreateBrushEntity(session, type.Name);
                else if (type.Name == "script_model") GameplayEntityEditing.ConvertModels(session);
                else PlacementRequested?.Invoke(type.Name);
            });
        };
        ApplyButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || session.Selection.Active is not MapEntity entity) return;
            finishGestures();
            await ActAsync(() => GameplayEntityEditing.ApplyFields(session, entity,
                TargetName.Text ?? "", Target.Text ?? "", Angles.Text ?? "", SpawnFlags.Text ?? "",
                Radius.Text ?? "", HeightValue.Text ?? "", Damage.Text ?? ""));
        };
        ConnectButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures(); await ActAsync(() => GameplayEntityEditing.Connect(session));
        };
        DisconnectButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures(); await ActAsync(() => GameplayEntityEditing.Disconnect(session));
        };

        async Task ActAsync(Action action)
        {
            try { action(); RefreshSelection(session); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                { await dialogs.MessageAsync("Gameplay entity", exception.Message); }
        }
    }

    internal void RefreshSelection(EditorSession session)
    {
        MapEntity? entity = session.Selection.Active as MapEntity;
        if (entity?.ClassName == "worldspawn") entity = null;
        Fields.IsEnabled = ApplyButton.IsEnabled = entity is not null;
        RadiusFields.IsVisible = entity?.ClassName == "trigger_radius";
        DamageFields.IsVisible = entity?.ClassName == "trigger_hurt";
        bool fieldFocused = new[] { TargetName, Target, Angles, SpawnFlags, Radius, HeightValue, Damage }.Any(box => box.IsKeyboardFocusWithin);
        if (!ReferenceEquals(_shownEntity, entity) || !fieldFocused)
        {
            TargetName.Text = entity?.Properties.GetValueOrDefault("targetname", "") ?? "";
            Target.Text = entity?.Properties.GetValueOrDefault("target", "") ?? "";
            SpawnFlags.Text = entity?.Properties.GetValueOrDefault("spawnflags", "") ?? "";
            Radius.Text = entity?.Properties.GetValueOrDefault("radius", "") ?? "";
            HeightValue.Text = entity?.Properties.GetValueOrDefault("height", "") ?? "";
            Damage.Text = entity?.Properties.GetValueOrDefault("dmg", "") ?? "";
            try
            {
                var angles = entity is null ? System.Numerics.Vector3.Zero : EntityOrientation.Read(entity);
                Angles.Text = FormattableString.Invariant($"{angles.X:G9} {angles.Y:G9} {angles.Z:G9}");
            }
            catch (ArgumentException) { Angles.Text = entity?.Properties.GetValueOrDefault("angles", "") ?? ""; }
        }
        _shownEntity = entity;
        FieldInfo.Text = entity is null ? "Select an entity to edit its gameplay fields." :
            GameplayEntityEditing.Types.FirstOrDefault(type => type.Name == entity.ClassName)?.Description ?? $"{entity.ClassName} · Native target and orientation fields.";
        if (entity?.ClassName == "trigger_radius" && !GameplayEntityEditing.TryRadiusDimensions(entity, out _, out _))
            FieldInfo.Text = "Radius and height must be positive finite numbers before the trigger volume can be displayed.";
        int selectedEntities = session.Selection.Items.OfType<MapEntity>().Count(candidate => candidate.ClassName != "worldspawn");
        ConnectButton.IsEnabled = selectedEntities >= 2 && selectedEntities == session.Selection.Count;
        DisconnectButton.IsEnabled = session.Selection.Items.OfType<MapEntity>().Any(candidate => candidate.Properties.ContainsKey("target"));
        string name = entity?.Properties.GetValueOrDefault("targetname", "") ?? "";
        string target = entity?.Properties.GetValueOrDefault("target", "") ?? "";
        int incoming = name.Length > 0 ? session.Document.Entities.Count(candidate => candidate.Properties.GetValueOrDefault("target") == name) : 0;
        int destinations = target.Length > 0 ? session.Document.Entities.Count(candidate => candidate.Properties.GetValueOrDefault("targetname") == target) : 0;
        LinkInfo.Text = entity is null ? "Select sources, then add the destination last to connect." :
            $"{incoming} incoming links" + (target.Length == 0 ? " · no outgoing target" : $" · target {target}: {destinations} matches") +
            (target.Length > 0 && destinations == 0 ? " (unresolved in this map)" : "");
    }

    private void FilterTypes()
    {
        string filter = EntityFilter.Text ?? "", category = Category.SelectedItem as string ?? "All types";
        var types = GameplayEntityEditing.Types.Where(type => (category == "All types" || type.Category == category) &&
            (type.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || type.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
        EntityTypes.ItemsSource = types;
        EntityTypes.SelectedIndex = types.Length > 0 ? 0 : -1;
        CreateButton.IsEnabled = types.Length > 0;
        if (types.Length == 0) TypeDescription.Text = "No matching native entity types.";
    }
}
