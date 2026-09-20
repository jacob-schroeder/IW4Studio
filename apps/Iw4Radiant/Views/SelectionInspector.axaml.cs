using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant.Viewports.Orthographic;

namespace Iw4Radiant.Views;

public partial class SelectionInspector : UserControl
{
    private MapDocument? _listedDocument;
    private int _listedEntityCount;
    private object[] _shownSelection = [];
    private MapEntity? _shownEntity;
    private bool _updating;

    public SelectionInspector() => InitializeComponent();
    internal event Action<string>? PlacementRequested;
    internal event Action? ModelBrowserRequested;
    internal event Action? PrefabBrowserRequested;

    internal void ShowTool(EditorTool tool)
    {
        if (IsKeyboardFocusWithin) return;
        InspectorTabs.SelectedItem = tool switch
        {
            EditorTool.Face => SurfaceTab,
            EditorTool.Terrain or EditorTool.Sculpt => TerrainTab,
            _ => SelectionTab
        };
    }

    internal void ShowEntity()
    {
        InspectorTabs.SelectedItem = EntityTab;
        PropertiesExpander.IsExpanded = true;
    }

    internal void ShowEnvironment() => InspectorTabs.SelectedItem = EnvironmentTab;
    internal void ShowGeometry() => InspectorTabs.SelectedItem = GeometryTab;
    internal void ShowOrganization() => InspectorTabs.SelectedItem = OrganizationTab;
    internal void ReleaseImages() => Skies.ReleaseImages();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures, MaterialBrowser materials,
        Func<string, MaterialSource?> resolveMaterial, Func<OrthoPlane> editPlane,
        Func<string, bool> supportsAlpha, Func<string, bool> supportsVertexColor)
    {
        Transforms.InitializeActions(session, dialogs, finishGestures);
        Surfaces.InitializeActions(session, dialogs, finishGestures, resolveMaterial);
        Lights.InitializeActions(session, dialogs, finishGestures);
        Terrain.InitializeActions(session, dialogs, finishGestures);
        Sunlight.InitializeActions(session, dialogs, finishGestures);
        Skies.InitializeActions(session, dialogs, finishGestures, materials);
        Geometry.InitializeActions(session, dialogs, finishGestures, editPlane);
        Organization.InitializeActions(session, dialogs, finishGestures);
        Gameplay.InitializeActions(session, dialogs, finishGestures);
        Gameplay.PlacementRequested += name => PlacementRequested?.Invoke(name);
        Gameplay.ModelBrowserRequested += () => ModelBrowserRequested?.Invoke();
        Gameplay.PrefabBrowserRequested += () => PrefabBrowserRequested?.Invoke();
        TerrainPaint.InitializeActions(session, dialogs, finishGestures, supportsAlpha, supportsVertexColor);
        Decals.InitializeActions(session, dialogs, finishGestures, supportsAlpha);
        Sunlight.EditSourceRequested += () =>
        {
            finishGestures();
            session.Select(session.Document.World);
            InspectorTabs.SelectedItem = EntityTab;
            PropertiesExpander.IsExpanded = true;
        };
        WorldButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            finishGestures();
            session.Select(session.Document.World);
        };
        EntityList.SelectionChanged += (_, _) => SelectEntity(session, dialogs, finishGestures);
        ApplyBoundsButton.Click += async (_, _) => await ApplyBoundsAsync(session, dialogs, finishGestures);
        ApplyPropertiesButton.Click += async (_, _) => await ApplyPropertiesAsync(session, dialogs, finishGestures);
        RevertBoundsButton.Click += (_, _) =>
        {
            if (!dialogs.BlocksInput) ShowBounds(session.SelectionBounds);
        };
        RevertPropertiesButton.Click += (_, _) =>
        {
            if (!dialogs.BlocksInput) ShowEntityProperties(session.Selection.Active as MapEntity);
        };
    }

    internal void RefreshSelection(EditorSession session)
    {
        _updating = true;
        try
        {
            bool changed = !_shownSelection.SequenceEqual(session.Selection.Items);
            _shownSelection = session.Selection.Items.ToArray();
            MapEntity? entity = session.Selection.Active as MapEntity;
            bool entityChanged = !ReferenceEquals(entity, _shownEntity);
            SelectionText.Text = SelectionSummary(session.Selection);
            EntitySelectionInfo.IsVisible = entity is not null && session.Selection.Count > 1;
            bool wholeBrush = session.Selection.Count == 1 && session.Selection.Active is MapBrush;
            ApplyBoundsButton.IsEnabled = RevertBoundsButton.IsEnabled = wholeBrush;
            foreach (TextBox box in BoundsBoxes()) box.IsReadOnly = !wholeBrush;
            var selectionBounds = session.SelectionBounds;
            if (selectionBounds is null || changed || !BoundsInputFocused()) ShowBounds(selectionBounds);
            ApplyPropertiesButton.IsEnabled = RevertPropertiesButton.IsEnabled = entity is not null;
            PropertiesBox.IsReadOnly = entity is null;
            if (entityChanged || !PropertiesBox.IsKeyboardFocusWithin)
                ShowEntityProperties(entity);
            _shownEntity = entity;
            if (!ReferenceEquals(_listedDocument, session.Document) || _listedEntityCount != session.Document.Entities.Count)
            {
                EntityList.ItemsSource = session.Document.Entities.Select((item, index) =>
                    new ComboBoxItem
                    {
                        Content = $"{index}: {item.ClassName}" + (item.Properties.TryGetValue("targetname", out string? name) ? $" · {name}" : ""),
                        Tag = item
                    }).ToArray();
                _listedDocument = session.Document;
                _listedEntityCount = session.Document.Entities.Count;
            }
            if (EntityList.ItemsSource is ComboBoxItem[] entityItems)
            {
                for (int index = 0; index < entityItems.Length; index++)
                    if (entityItems[index].Tag is MapEntity listed)
                    {
                        entityItems[index].Content = $"{index}: {listed.ClassName}" +
                            (listed.Properties.TryGetValue("targetname", out string? name) ? $" · {name}" : "");
                        entityItems[index].IsEnabled = ReferenceEquals(listed, session.Document.World) ||
                            session.Visibility.CanSelect(session.Document, listed);
                    }
                EntityList.SelectedItem = entityItems.FirstOrDefault(item => ReferenceEquals(item.Tag, entity));
            }
            Transforms.RefreshSelection(session);
            Surfaces.RefreshSelection(session);
            bool hadLightError = Lights.HasSourceError;
            Lights.RefreshSelection(session);
            if (Lights.HasSourceError && (entityChanged || !hadLightError)) PropertiesExpander.IsExpanded = true;
            Terrain.RefreshSelection(session);
            Geometry.RefreshSelection(session);
            Organization.RefreshSelection(session);
            Gameplay.RefreshSelection(session);
            TerrainPaint.RefreshSelection(session);
            Decals.RefreshSelection(session);
            Sunlight.RefreshWorld(session);
            Skies.RefreshSelection(session);
        }
        finally { _updating = false; }
    }

    private static string SelectionSummary(EditorSelection selection)
    {
        if (selection.Count == 0) return "No selection";
        if (selection.Count == 1) return Describe(selection.Active);
        string counts = string.Join(", ", selection.Items.GroupBy(item => item switch
        {
            MapBrush => "brush", MapTerrain => "patch", MapEntity => "entity",
            BrushFaceSelection => "face", BrushVertexSelection or TerrainVertexSelection => "vertex", _ => "item"
        }).Select(group => $"{group.Count()} " + (group.Count() == 1 ? group.Key : group.Key switch
        {
            "brush" => "brushes", "patch" => "patches", "entity" => "entities", "vertex" => "vertices", _ => group.Key + "s"
        })));
        return $"{selection.Count} selected · {counts}\nActive: {Describe(selection.Active)}";
    }

    private static string Describe(object? selected) => selected switch
    {
        MapBrush brush => $"Brush · {brush.Faces.Count} planes",
        MapTerrain terrain => $"{(terrain.IsCurve ? "Curve" : "Terrain")} · {terrain.Width} × {terrain.Height} {(terrain.IsCurve ? "controls" : "vertices")}\n{terrain.Material}",
        MapEntity entity => $"Entity · {entity.ClassName}",
        BrushFaceSelection face => $"Brush face {face.Brush.Faces.IndexOf(face.Face) + 1} · {face.Face.Material}",
        BrushVertexSelection vertex => FormattableString.Invariant($"Brush vertex · {vertex.Position.X:G6} / {vertex.Position.Y:G6} / {vertex.Position.Z:G6}"),
        TerrainVertexSelection vertex => $"Terrain vertex {vertex.Index} · {vertex.Terrain.Material}",
        _ => "No selection"
    };

    private TextBox[] BoundsBoxes() => [MinX, MinY, MinZ, SizeX, SizeY, SizeZ];
    private bool BoundsInputFocused() => BoundsBoxes().Any(box => box.IsKeyboardFocusWithin);

    private void ShowBounds((Vector3 Min, Vector3 Max)? selectionBounds)
    {
        if (selectionBounds is { } bounds)
        {
            SetVector(bounds.Min, MinX, MinY, MinZ);
            SetVector(bounds.Max - bounds.Min, SizeX, SizeY, SizeZ);
        }
        else
            foreach (TextBox box in BoundsBoxes()) box.Text = "";
    }

    private void ShowEntityProperties(MapEntity? entity) => PropertiesBox.Text = entity is not null
        ? string.Join('\n', entity.Properties.Select(pair => $"{pair.Key}={pair.Value}")) : "";

    private static void SetVector(Vector3 value, TextBox x, TextBox y, TextBox z)
    {
        x.Text = value.X.ToString("R", CultureInfo.InvariantCulture);
        y.Text = value.Y.ToString("R", CultureInfo.InvariantCulture);
        z.Text = value.Z.ToString("R", CultureInfo.InvariantCulture);
    }

    private static Vector3 ReadVector(TextBox x, TextBox y, TextBox z)
    {
        float Read(TextBox box)
        {
            if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new ArgumentException("Enter finite numeric coordinates using a decimal point.");
            return value;
        }
        return new Vector3(Read(x), Read(y), Read(z));
    }

    private void SelectEntity(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput || EntityList.SelectedItem is not ComboBoxItem { Tag: MapEntity entity }) return;
        int index = session.Document.Entities.IndexOf(entity);
        finishGestures();
        if ((uint)index < (uint)session.Document.Entities.Count) session.Select(session.Document.Entities[index]);
    }

    private async Task ApplyBoundsAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            Vector3 min = ReadVector(MinX, MinY, MinZ), size = ReadVector(SizeX, SizeY, SizeZ), max = min + size;
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0 || !float.IsFinite(max.X) || !float.IsFinite(max.Y) || !float.IsFinite(max.Z))
                throw new ArgumentException("Brush dimensions must be positive and produce finite bounds.");
            finishGestures();
            if (session.Selection.Count != 1 || session.Selection.Active is not MapBrush brush) return;
            var before = brush.GetBounds();
            if (before.Min == min && before.Max - before.Min == size) return;
            session.Edit(() => brush.Resize(min, max, session.TextureLock));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        { await dialogs.MessageAsync("Brush bounds", exception.Message); }
    }

    private async Task ApplyPropertiesAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in (PropertiesBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = line.IndexOf('=');
                if (equals < 1) throw new ArgumentException("Use one key=value pair per line.");
                string key = line[..equals].Trim(), value = line[(equals + 1)..].TrimEnd('\r');
                if (key.IndexOfAny(['\r', '\0']) >= 0 || value.IndexOfAny(['\r', '\0']) >= 0)
                    throw new ArgumentException("Entity properties cannot contain embedded line breaks or NUL characters.");
                if (key.Length == 0 || !properties.TryAdd(key, value)) throw new ArgumentException("Property names must be unique and nonempty.");
            }
            if (string.IsNullOrWhiteSpace(properties.GetValueOrDefault("classname"))) throw new ArgumentException("An entity needs a classname.");
            finishGestures();
            if (session.Selection.Active is not MapEntity entity) return;
            bool world = ReferenceEquals(entity, session.Document.World);
            if (world != (properties["classname"] == "worldspawn")) throw new ArgumentException("The map must retain its single worldspawn entity.");
            if (properties.Count == entity.Properties.Count && properties.All(pair => entity.Properties.GetValueOrDefault(pair.Key) == pair.Value)) return;
            session.Edit(() =>
            {
                entity.Properties.Clear();
                foreach (var pair in properties) entity.Properties.Add(pair.Key, pair.Value);
                _listedDocument = null;
            });
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Entity properties", exception.Message); }
    }
}
