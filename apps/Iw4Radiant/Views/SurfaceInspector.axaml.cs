using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using IW4.AssetExchange.SourceFormat.Material;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class SurfaceInspector : UserControl
{
    private MapFace[] _shownFaces = [];
    private string[] _shownProjections = [];
    private MapFace? _shownReference;
    private string? _shownWaterMaterial;
    private Func<string, MaterialSource?>? _resolveMaterial;
    private bool _updating;

    public SurfaceInspector()
    {
        InitializeComponent();
        WaterColorPicker.PreserveColorScale = true;
        OceanEnabled.IsCheckedChanged += (_, _) => OceanFields.IsVisible = OceanEnabled.IsChecked == true;
    }

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Func<string, MaterialSource?> resolveMaterial, Action<string> setStatus)
    {
        _resolveMaterial = resolveMaterial;
        WireAdjustment(ShiftXDecrease, ShiftXIncrease, ShiftXValue, "horizontal shift", 1);
        WireAdjustment(ShiftYDecrease, ShiftYIncrease, ShiftYValue, "vertical shift", 1);
        WireAdjustment(WidthDecrease, WidthIncrease, WidthValue, "horizontal repeat size", 1);
        WireAdjustment(HeightDecrease, HeightIncrease, HeightValue, "vertical repeat size", 1);
        WireAdjustment(RotationDecrease, RotationIncrease, RotationValue, "rotation", 15);
        WireAdjustment(SkewDecrease, SkewIncrease, SkewValue, "skew", 0.1f);
        ApplyProjectionButton.Click += async (_, _) => await ApplyProjectionAsync(session, dialogs, finishGestures);
        ApplyWaterButton.Click += async (_, _) => await ApplyWaterAsync(session, dialogs, finishGestures);
        FitButton.Click += async (_, _) => await FitAsync(session, dialogs, finishGestures);
        AxialButton.Click += async (_, _) => await AxialAsync(session, dialogs, finishGestures);
        AutoCaulkButton.Click += async (_, _) => await AutoCaulkAsync(session, dialogs, finishGestures, setStatus);
        RevertProjectionButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            _shownReference = null;
            RepeatsXValue.Text = RepeatsYValue.Text = "1";
            RefreshSelection(session);
        };
        RevertWaterButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            _shownWaterMaterial = null;
            RefreshSelection(session);
        };
        TextureLockValue.IsCheckedChanged += (_, _) =>
        {
            if (_updating || dialogs.BlocksInput) return;
            bool textureLock = TextureLockValue.IsChecked == true;
            finishGestures();
            session.TextureLock = textureLock;
            session.Refresh();
        };

        void WireAdjustment(Button decrease, Button increase, TextBox box, string name, float step)
        {
            decrease.Click += async (_, _) => await AdjustAsync(-step);
            increase.Click += async (_, _) => await AdjustAsync(step);

            async Task AdjustAsync(float amount)
            {
                if (dialogs.BlocksInput) return;
                try
                {
                    float value = ReadValue(box, name) + amount;
                    if (!float.IsFinite(value)) throw new ArgumentException($"The {name} exceeds the supported numeric range.");
                    SetValue(box, value);
                }
                catch (ArgumentException exception) { await dialogs.MessageAsync("Surface projection", exception.Message); }
            }
        }
    }

    internal void RefreshSelection(EditorSession session)
    {
        _updating = true;
        try
        {
            var faces = SurfaceEditing.GetFaces(session).Select(selection => selection.Face).ToArray();
            MapFace? reference = session.Selection.Active is BrushFaceSelection active && faces.Contains(active.Face)
                ? active.Face : faces.FirstOrDefault();
            string[] projections = faces.Select(face => face.Projection).ToArray();
            bool changed = !_shownFaces.SequenceEqual(faces) || !ReferenceEquals(_shownReference, reference) ||
                !_shownProjections.SequenceEqual(projections);
            _shownFaces = faces;
            _shownProjections = projections;
            _shownReference = reference;
            TextureLockValue.IsChecked = session.TextureLock;
            ProjectionFields.IsEnabled = reference is not null;
            AutoCaulkButton.IsEnabled = session.Selection.Items.Count(item => item is MapBrush or MapEntity) > 1 ||
                session.Selection.Items.OfType<MapEntity>().Any(entity => entity.Brushes.Count > 1);
            SurfaceSummary.Text = faces.Length == 0 ? "Select a brush or face." :
                $"{faces.Length} selected surface{(faces.Length == 1 ? "" : "s")}";
            bool mixedMaterials = faces.Select(face => face.Material).Distinct(StringComparer.Ordinal).Take(2).Count() > 1;
            TextureName.Text = mixedMaterials ? "" : reference?.Material ?? "";
            TextureName.PlaceholderText = mixedMaterials ? "Mixed materials" : "No selected surface";
            if (reference is null)
            {
                ProjectionInfo.Text = "";
                ProjectionInfo.IsVisible = false;
                foreach (var box in ProjectionBoxes()) box.Text = "";
                RefreshWater(session, faces);
                return;
            }
            try
            {
                var projection = SurfaceProjection.Parse(reference.Projection);
                bool mixed = faces.Any(face => (SurfaceProjection.Parse(face.Projection) with { Suffix = "" }) !=
                    (projection with { Suffix = "" }));
                ProjectionInfo.Text = mixed
                    ? "Mixed projections. Fields show one selected surface; Apply replaces all six values on every selected surface."
                    : "";
                ProjectionInfo.IsVisible = mixed;
                if (changed)
                {
                    SetValue(WidthValue, projection.Width);
                    SetValue(HeightValue, projection.Height);
                    SetValue(ShiftXValue, projection.ShiftX);
                    SetValue(ShiftYValue, projection.ShiftY);
                    SetValue(RotationValue, projection.Rotation);
                    SetValue(SkewValue, projection.Skew);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                ProjectionFields.IsEnabled = false;
                ProjectionInfo.Text = exception.Message;
                ProjectionInfo.IsVisible = true;
                foreach (var box in ProjectionBoxes()) box.Text = "";
            }
            RefreshWater(session, faces);
        }
        finally { _updating = false; }
    }

    private void RefreshWater(EditorSession session, IReadOnlyList<MapFace> faces)
    {
        string[] names = faces.Select(face => face.Material).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        string? name = names.Length == 1 ? names[0] : null;
        IReadOnlyDictionary<string, WaterMaterialDefinition> definitions =
            new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
        if (name is not null)
            try
            {
                definitions = WaterMaterialAuthoring.ReadDefinitions(session.Document.World.Properties);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                               FormatException or OverflowException)
            {
                WaterFields.IsEnabled = false;
                ApplyWaterButton.IsEnabled = RevertWaterButton.IsEnabled = false;
                WaterInfo.Text = $"Authored water definition error: {exception.Message}";
                foreach (TextBox box in WaterBoxes()) box.Text = "";
                WaterColorPicker.SelectedColor = Vector3.Zero;
                _shownWaterMaterial = name;
                return;
            }
        MaterialSource? material = name is null ? null : _resolveMaterial?.Invoke(name);
        bool enabled = material?.Water is not null;
        WaterFields.IsEnabled = enabled;
        ApplyWaterButton.IsEnabled = RevertWaterButton.IsEnabled = enabled;
        if (faces.Count == 0)
            WaterInfo.Text = "Select a brush or face using a native water material.";
        else if (names.Length != 1)
            WaterInfo.Text = "Selected surfaces use mixed materials. Select surfaces with one water material.";
        else if (!enabled)
            WaterInfo.Text = material is null
                ? $"Material '{name}' is unavailable. Load its source assets to edit water."
                : $"Material '{name}' is not native water.";
        else if (name is { } selectedName && material is { } waterMaterial)
        {
            WaterMaterialDefinition? definition = definitions.GetValueOrDefault(selectedName);
            WaterInfo.Text = definition is null
                ? $"Stock water · {selectedName}"
                : $"Authored water · source {definition.SourceMaterial}";
            bool changed = !string.Equals(_shownWaterMaterial, selectedName, StringComparison.Ordinal);
            if (changed || !WaterInputFocused())
            {
                WaterColorPicker.SelectedColor = new Vector3(definition?.Red ?? waterMaterial.WaterColor.X,
                    definition?.Green ?? waterMaterial.WaterColor.Y, definition?.Blue ?? waterMaterial.WaterColor.Z);
                SetValue(WaveIntensity, definition?.WaveIntensity ?? 1);
                SetValue(AnimationSpeed, definition?.AnimationSpeed ?? 1);
                OceanEnabled.IsChecked = definition?.Ocean is not null;
                SetValue(OceanHeight, definition?.Ocean?.Height ?? 8);
                SetValue(OceanWavelength, definition?.Ocean?.Wavelength ?? 128);
                SetValue(OceanSpeed, definition?.Ocean?.Speed ?? 32);
                SetValue(OceanDirection, definition?.Ocean?.Direction ?? 0);
                SetValue(FresnelMinimum, definition?.FresnelMinimum ?? waterMaterial.EnvMapParms.X);
                SetValue(FresnelMaximum, definition?.FresnelMaximum ?? waterMaterial.EnvMapParms.Y);
                SetValue(FresnelExponent, definition?.FresnelExponent ?? waterMaterial.EnvMapParms.Z);
            }
        }
        if (!enabled && !WaterInputFocused())
        {
            foreach (TextBox box in WaterBoxes()) box.Text = "";
            WaterColorPicker.SelectedColor = Vector3.Zero;
        }
        _shownWaterMaterial = name;
    }

    private async Task ApplyWaterAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            Vector3 color = WaterColorPicker.SelectedColor;
            float red = color.X, green = color.Y, blue = color.Z;
            float intensity = ReadValue(WaveIntensity, "wave intensity");
            float speed = ReadValue(AnimationSpeed, "animation speed");
            float fresnelMinimum = ReadValue(FresnelMinimum, "reflection minimum");
            float fresnelMaximum = ReadValue(FresnelMaximum, "reflection maximum");
            float fresnelExponent = ReadValue(FresnelExponent, "reflection exponent");
            finishGestures();
            MapFace[] faces = SurfaceEditing.GetFaces(session).Select(selection => selection.Face).ToArray();
            string[] names = faces.Select(face => face.Material).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (faces.Length == 0 || names.Length != 1)
                throw new ArgumentException("Select brush faces using one native water material.");
            IReadOnlyDictionary<string, WaterMaterialDefinition> saved =
                WaterMaterialAuthoring.ReadDefinitions(session.Document.World.Properties);
            WaterMaterialDefinition? previous = saved.GetValueOrDefault(names[0]);
            MaterialSource source = _resolveMaterial?.Invoke(previous?.SourceMaterial ?? names[0]) ??
                throw new ArgumentException("Load the selected water material's source assets before editing it.");
            if (source.Water is null) throw new ArgumentException("The selected material is not native water.");
            WaterMaterialDefinition definition = WaterMaterialAuthoring.CreateDefinition(source.Name, red, green, blue,
                intensity, speed, fresnelMinimum, fresnelMaximum, fresnelExponent,
                OceanEnabled.IsChecked == true ? new OceanWaveSettings(ReadValue(OceanHeight, "ocean height"),
                    ReadValue(OceanWavelength, "ocean wavelength"), ReadValue(OceanSpeed, "ocean speed"),
                    ReadValue(OceanDirection, "ocean direction")) : null);
            if (definition.Ocean is { } ocean)
            {
                foreach (MapBrush brush in session.Document.Brushes.Where(brush => brush.Faces.Any(faces.Contains)))
                {
                    if (!session.Document.World.Brushes.Contains(brush) ||
                        !brush.Faces.All(face => _resolveMaterial?.Invoke(face.Material)?.IsWater == true))
                        throw new ArgumentException("Ocean waves require a closed world brush with water on every face.");
                    MapPolygon[] tops = brush.GetPolygons().Where(OceanSurfaceGeometry.IsTop).ToArray();
                    if (!tops.Any(top => faces.Contains(top.Face)))
                        throw new ArgumentException("Select the horizontal top surface or the whole water brush to enable ocean waves.");
                    foreach (MapPolygon top in tops) _ = OceanSurfaceGeometry.Subdivide(top, ocean).Count();
                }
            }
            if (previous == definition) return;
            session.Edit(() =>
            {
                foreach (MapFace face in faces) face.Material = definition.Name;
                var retained = saved.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                retained[definition.Name] = definition;
                HashSet<string> used = session.Document.Brushes.SelectMany(brush => brush.Faces).Select(face => face.Material)
                    .Concat(session.Document.Terrains.Select(terrain => terrain.Material)).ToHashSet(StringComparer.Ordinal);
                WaterMaterialAuthoring.WriteDefinitions(session.Document.World.Properties,
                    retained.Where(pair => used.Contains(pair.Key)).Select(pair => pair.Value));
            });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or FormatException or OverflowException)
        { await dialogs.MessageAsync("Water material", exception.Message); }
    }

    private async Task ApplyProjectionAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            var edits = new SurfaceProjection(ReadValue(WidthValue, "width"), ReadValue(HeightValue, "height"),
                ReadValue(ShiftXValue, "S shift"), ReadValue(ShiftYValue, "T shift"),
                ReadValue(RotationValue, "rotation"), ReadValue(SkewValue, "skew"), "");
            finishGestures();
            SurfaceEditing.ApplyProjection(session, edits);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { await dialogs.MessageAsync("Surface projection", exception.Message); }
    }

    private async Task FitAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            float repeatsX = ReadValue(RepeatsXValue, "S repeats"), repeatsY = ReadValue(RepeatsYValue, "T repeats");
            if (repeatsX <= 0 || repeatsY <= 0)
                throw new ArgumentException("Texture fit requires positive repeat counts.");
            finishGestures();
            SurfaceEditing.Fit(session, repeatsX, repeatsY);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { await dialogs.MessageAsync("Fit texture", exception.Message); }
    }

    private static async Task AxialAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            finishGestures();
            SurfaceEditing.Axial(session);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { await dialogs.MessageAsync("Axial texture alignment", exception.Message); }
    }

    private async Task AutoCaulkAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Action<string> setStatus)
    {
        if (dialogs.BlocksInput || _resolveMaterial is not { } resolveMaterial) return;
        try
        {
            finishGestures();
            int count = SurfaceEditing.AutoCaulk(session, resolveMaterial);
            setStatus(count == 0 ? "Auto Caulk found no fully covered faces between selected opaque brushes." :
                $"Caulked {count} face{(count == 1 ? "" : "s")}.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        { await dialogs.MessageAsync("Auto Caulk", exception.Message); }
    }

    private TextBox[] ProjectionBoxes() => [WidthValue, HeightValue, ShiftXValue, ShiftYValue, RotationValue, SkewValue];
    private TextBox[] WaterBoxes() => [WaveIntensity, AnimationSpeed,
        FresnelMinimum, FresnelMaximum, FresnelExponent, OceanHeight, OceanWavelength, OceanSpeed, OceanDirection];
    private bool WaterInputFocused() => WaterFields.IsKeyboardFocusWithin;

    private static void SetValue(TextBox box, float value) => box.Text = value.ToString("R", CultureInfo.InvariantCulture);

    private static float ReadValue(TextBox box, string name)
    {
        if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for {name}, using a decimal point.");
        return value;
    }
}
