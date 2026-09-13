using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class SelectionInspector : UserControl
{
    private MapDocument? _listedDocument;
    private int _listedEntityCount;
    private bool _updating;

    public SelectionInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        TerrainCount.ValueChanged += (_, _) => UpdateTerrainSettings(session, finishGestures);
        RadiusValue.ValueChanged += (_, _) => UpdateTerrainSettings(session, finishGestures);
        StrengthValue.ValueChanged += (_, _) => UpdateTerrainSettings(session, finishGestures);
        WorldButton.Click += (_, _) => SelectWorld(session, finishGestures);
        EntityList.SelectionChanged += (_, _) => SelectEntity(session, finishGestures);
        ApplyBoundsButton.Click += async (_, _) => await ApplyBoundsAsync(session, dialogs, finishGestures);
        ApplyPropertiesButton.Click += async (_, _) => await ApplyPropertiesAsync(session, dialogs, finishGestures);
    }

    internal void RefreshSelection(EditorSession session)
    {
        _updating = true;
        try
        {
            SelectionText.Text = session.Selection switch
            {
                MapBrush brush => $"Brush · {brush.Faces.Count} planes\n{brush.Faces.FirstOrDefault()?.Material}",
                MapTerrain terrain => $"Terrain · {terrain.Width} × {terrain.Height} vertices\n{terrain.Material}",
                MapEntity entity => entity.ClassName,
                _ => "No selection"
            };
            ApplyBoundsButton.IsEnabled = session.Selection is MapBrush;
            if (session.SelectionBounds is { } bounds && !BoundsInputFocused())
            {
                SetVector(bounds.Min, MinX, MinY, MinZ);
                SetVector(bounds.Max - bounds.Min, SizeX, SizeY, SizeZ);
            }
            else if (session.Selection is null)
                foreach (TextBox box in new[] { MinX, MinY, MinZ, SizeX, SizeY, SizeZ }) box.Text = "";
            ApplyPropertiesButton.IsEnabled = session.Selection is MapEntity;
            if (!PropertiesBox.IsKeyboardFocusWithin)
                PropertiesBox.Text = session.Selection is MapEntity selectedEntity
                    ? string.Join('\n', selectedEntity.Properties.Select(pair => $"{pair.Key}={pair.Value}")) : "";
            if (!ReferenceEquals(_listedDocument, session.Document) || _listedEntityCount != session.Document.Entities.Count)
            {
                EntityList.ItemsSource = session.Document.Entities.Select((entity, index) =>
                    new ComboBoxItem { Content = $"{index}: {entity.ClassName}", Tag = entity }).ToArray();
                _listedDocument = session.Document;
                _listedEntityCount = session.Document.Entities.Count;
            }
            if (EntityList.ItemsSource is ComboBoxItem[] entityItems)
                EntityList.SelectedItem = entityItems.FirstOrDefault(item => ReferenceEquals(item.Tag, session.Selection));
        }
        finally { _updating = false; }
    }

    private void UpdateTerrainSettings(EditorSession session, Action finishGestures)
    {
        finishGestures();
        session.TerrainVertices = Math.Clamp((int)(TerrainCount.Value ?? 5), 2, 16);
        session.SculptRadius = (float)(RadiusValue.Value ?? 128);
        session.SculptStrength = (float)(StrengthValue.Value ?? 8);
        session.Refresh();
    }

    private static void SelectWorld(EditorSession session, Action finishGestures)
    {
        finishGestures();
        session.Select(session.Document.World);
    }

    private bool BoundsInputFocused() => new[] { MinX, MinY, MinZ, SizeX, SizeY, SizeZ }.Any(box => box.IsKeyboardFocusWithin);
    private static void SetVector(Vector3 value, TextBox x, TextBox y, TextBox z)
    {
        x.Text = value.X.ToString("0.###", CultureInfo.InvariantCulture);
        y.Text = value.Y.ToString("0.###", CultureInfo.InvariantCulture);
        z.Text = value.Z.ToString("0.###", CultureInfo.InvariantCulture);
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

    private void SelectEntity(EditorSession session, Action finishGestures)
    {
        if (!_updating && EntityList.SelectedItem is ComboBoxItem { Tag: MapEntity entity })
        { finishGestures(); session.Select(entity); }
    }
    private async Task ApplyBoundsAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        try
        {
            finishGestures();
            if (session.Selection is not MapBrush brush) return;
            Vector3 min = ReadVector(MinX, MinY, MinZ), size = ReadVector(SizeX, SizeY, SizeZ);
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0 || !float.IsFinite((min + size).X) ||
                !float.IsFinite((min + size).Y) || !float.IsFinite((min + size).Z))
                throw new ArgumentException("Brush dimensions must be positive and produce finite bounds.");
            session.Edit(() => brush.Resize(min, min + size));
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Brush bounds", exception.Message); }
    }
    private async Task ApplyPropertiesAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        finishGestures();
        if (session.Selection is not MapEntity entity) return;
        try
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in (PropertiesBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = line.IndexOf('=');
                if (equals < 1) throw new ArgumentException("Use one key=value pair per line.");
                string key = line[..equals].Trim(), value = line[(equals + 1)..].TrimEnd('\r');
                if (key.Length == 0 || !properties.TryAdd(key, value)) throw new ArgumentException("Property names must be unique and nonempty.");
            }
            if (string.IsNullOrWhiteSpace(properties.GetValueOrDefault("classname"))) throw new ArgumentException("An entity needs a classname.");
            bool world = ReferenceEquals(entity, session.Document.World);
            if (world != (properties["classname"] == "worldspawn")) throw new ArgumentException("The map must retain its single worldspawn entity.");
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
