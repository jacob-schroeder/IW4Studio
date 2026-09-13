using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class SkyInspector : UserControl
{
    private MaterialBrowser? _browser;
    private MaterialSource[] _listedSkies = [];
    private (string Name, int Faces, bool Available)[] _listedUses = [];
    private MaterialSource? _previewSource;
    private Bitmap? _preview;
    private bool _updating;

    public SkyInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures, MaterialBrowser browser)
    {
        _browser = browser;
        SkyList.SelectionChanged += (_, _) =>
        {
            if (!_updating) RefreshSelection(session);
        };
        UsedSkyList.SelectionChanged += (_, _) => SelectUsedFacesButton.IsEnabled = UsedSkyList.SelectedItem is ListBoxItem;
        ApplySkyButton.Click += async (_, _) => await EditAsync(session, dialogs, finishGestures, enclosure: false);
        CreateEnclosureButton.Click += async (_, _) => await EditAsync(session, dialogs, finishGestures, enclosure: true);
        ChooseBrowserButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput || ChosenSource() is not { } material) return;
            try { browser.ChooseMaterial(material.Name); }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
            { await dialogs.MessageAsync("Choose sky", exception.Message); }
        };
        SelectUsedFacesButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput || UsedSkyList.SelectedItem is not ListBoxItem { Tag: string material }) return;
            finishGestures();
            SkyEditing.SelectFaces(session, material);
        };
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating || _browser is not { } browser) return;
        _updating = true;
        try
        {
            MaterialSource[] skies = browser.AvailableSkies.ToArray();
            if (!skies.SequenceEqual(_listedSkies, ReferenceEqualityComparer.Instance))
            {
                string? chosen = (SkyList.SelectedItem as ComboBoxItem)?.Tag as string;
                _listedSkies = skies;
                var items = skies.Select(material => new ComboBoxItem { Content = material.Name, Tag = material.Name }).ToArray();
                SkyList.ItemsSource = items;
                SkyList.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, chosen));
            }
            RefreshPreview(ChosenSource());
            int selectedFaces = SkyEditing.SelectedWorldFaces(session).Length;
            SelectionInfo.Text = selectedFaces == 0 ? "Select world brush faces or brushes to apply a sky." :
                $"Applies only to {selectedFaces} selected world brush {(selectedFaces == 1 ? "face" : "faces")}.";
            ApplySkyButton.IsEnabled = _preview is not null && selectedFaces > 0;
            ChooseBrowserButton.IsEnabled = _preview is not null;
            bool hasGeometry = SkyEditing.EnclosureBounds(session) is not null;
            CreateEnclosureButton.IsEnabled = _preview is not null && hasGeometry;
            EnclosureInfo.Text = hasGeometry ? $"Six walls · thickness {session.GridSize:G6} world units" :
                "Select brush or terrain geometry to enclose.";
            var available = skies.Select(material => material.Name).ToHashSet(StringComparer.Ordinal);
            var used = session.Document.World.Brushes.SelectMany(brush => brush.Faces)
                .Where(face => browser.ResolveMaterial(face.Material) is { IsSky: true })
                .GroupBy(face => face.Material, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Name: group.Key, Faces: group.Count(), Available: available.Contains(group.Key))).ToArray();
            if (!used.SequenceEqual(_listedUses))
            {
                string? chosen = (UsedSkyList.SelectedItem as ListBoxItem)?.Tag as string;
                _listedUses = used;
                var items = used.Select(sky => new ListBoxItem
                {
                    Tag = sky.Name,
                    Content = new TextBlock
                    {
                        Text = $"{sky.Name}\n{sky.Faces} {(sky.Faces == 1 ? "face" : "faces")}" + (sky.Available ? "" : " · Unavailable"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }).ToArray();
                UsedSkyList.ItemsSource = items;
                UsedSkyList.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, chosen));
            }
            UsedSkyInfo.Text = used.Length == 0 ? "No loaded sky materials are used by world brush faces." :
                $"{used.Length} sky {(used.Length == 1 ? "material" : "materials")}";
            UsedSkyList.IsVisible = used.Length > 0;
            SelectUsedFacesButton.IsEnabled = UsedSkyList.SelectedItem is ListBoxItem;
        }
        finally { _updating = false; }
    }

    private MaterialSource? ChosenSource() => SkyList.SelectedItem is ComboBoxItem { Tag: string name }
        ? _browser?.ResolveMaterial(name) : null;

    private void RefreshPreview(MaterialSource? source)
    {
        if (ReferenceEquals(source, _previewSource)) return;
        ReleaseImages();
        _previewSource = source;
        SkyError.IsVisible = false;
        PreviewInfo.Text = source is null ? "Choose a sky to preview." : "Sky cube · +X face";
        if (source is null) return;
        try
        {
            _preview = MaterialImages.Load(source, 192);
            SkyPreview.Source = _preview;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            SkyError.Text = exception.Message;
            SkyError.IsVisible = true;
        }
    }

    private async Task EditAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool enclosure)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            MaterialSource material = ChosenSource() ?? throw new ArgumentException("Choose a readable sky material.");
            float padding = 0;
            if (enclosure && (!float.TryParse(PaddingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out padding) || !float.IsFinite(padding)))
                throw new ArgumentException("Enter finite sky enclosure padding using a decimal point.");
            finishGestures();
            if (enclosure) SkyEditing.CreateEnclosure(session, material, padding);
            else SkyEditing.Apply(session, material);
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await dialogs.MessageAsync(enclosure ? "Create sky enclosure" : "Apply sky", exception.Message); }
    }

    internal void ReleaseImages()
    {
        SkyPreview.Source = null;
        _preview?.Dispose();
        _preview = null;
        _previewSource = null;
    }
}
