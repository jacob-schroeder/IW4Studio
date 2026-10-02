using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class SunlightInspector : UserControl
{
    private MapEntity? _shownEntity;
    private string?[] _shownValues = [];
    private Vector3 _sourceColor = Vector3.One;
    private bool _updating, _applying, _stageMode, _sourceValid = true;

    public SunlightInspector() => InitializeComponent();

    internal event Action? EditSourceRequested;

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures, bool stage = false)
    {
        _stageMode = stage;
        if (stage)
        {
            SunTitle.Text = "Stage lighting";
            SunEnabled.Content = "Override sun";
            ToolTip.SetTip(SunEnabled, "Unchecked inherits world sunlight. Override changes this Stage only.");
            ApplySunButton.Content = "Apply stage lighting";
            EditSourceButton.Content = "Edit stage properties";
            SunHint.Text = "Unchecked values inherit world lighting. Overlapping stages use the first Stage in entity order. Rotate or reshape the volume using its brushes. Bounced light requires a build.";
        }
        ApplySunButton.Click += async (_, _) => await ApplyAsync(session, dialogs, finishGestures);
        EarlierStage.Click += (_, _) => ReorderStage(-1);
        LaterStage.Click += (_, _) => ReorderStage(1);
        RevertSunButton.Click += (_, _) =>
        {
            if (!dialogs.BlocksInput && _shownEntity is { } entity) Refresh(session, entity, force: true);
        };
        EditSourceButton.Click += (_, _) => { if (!dialogs.BlocksInput) EditSourceRequested?.Invoke(); };
        SunEnabled.IsCheckedChanged += (_, _) => RefreshEnabled();
        AmbientOverride.IsCheckedChanged += (_, _) => RefreshEnabled();
        RedBox.TextChanged += (_, _) => RefreshColor();
        GreenBox.TextChanged += (_, _) => RefreshColor();
        BlueBox.TextChanged += (_, _) => RefreshColor();
        ColorPicker.SelectedColorChanged += PickerColorChanged;
        PitchBox.TextChanged += (_, _) => RefreshDirection();
        YawBox.TextChanged += (_, _) => RefreshDirection();
        DirectionPicker.DirectionChanged += () =>
        {
            if (_updating || dialogs.BlocksInput) return;
            _updating = true;
            PitchBox.Text = Number(DirectionPicker.Pitch);
            YawBox.Text = Number(DirectionPicker.Yaw);
            _updating = false;
        };

        void ReorderStage(int offset)
        {
            if (!_stageMode || dialogs.BlocksInput || _shownEntity is not { } entity) return;
            finishGestures();
            var stages = session.Document.Entities.Where(item => item.ClassName == "stage").ToArray();
            int index = Array.IndexOf(stages, entity), other = index + offset;
            if (index < 0 || other < 0 || other >= stages.Length) return;
            int first = session.Document.Entities.IndexOf(entity);
            int second = session.Document.Entities.IndexOf(stages[other]);
            session.Edit(() => (session.Document.Entities[first], session.Document.Entities[second]) =
                (session.Document.Entities[second], session.Document.Entities[first]));
        }
    }

    internal void RefreshWorld(EditorSession session, bool force = false)
        => Refresh(session, session.Document.World, force);

    internal void RefreshStage(EditorSession session, MapEntity? entity)
    {
        IsVisible = entity?.ClassName == "stage";
        if (IsVisible && entity is not null)
        {
            var stages = session.Document.Entities.Where(item => item.ClassName == "stage").ToArray();
            int index = Array.IndexOf(stages, entity);
            StageOrder.IsVisible = stages.Length > 1;
            StagePriority.Text = $"Overlap priority {index + 1} of {stages.Length}";
            EarlierStage.IsEnabled = index > 0;
            LaterStage.IsEnabled = index >= 0 && index + 1 < stages.Length;
            Refresh(session, entity);
        }
        else _shownEntity = null;
    }

    private void Refresh(EditorSession session, MapEntity entity, bool force = false)
    {
        if (_applying) return;
        var world = session.Document.World;
        bool staged = _stageMode || session.Document.Entities.Any(item => item.ClassName == "stage");
        string?[] values = [entity.Properties.GetValueOrDefault("sunlight"),
            entity.Properties.GetValueOrDefault("suncolor"), entity.Properties.GetValueOrDefault("sundirection"),
            entity.Properties.GetValueOrDefault("ambient"), world.Properties.GetValueOrDefault("sunlight"),
            world.Properties.GetValueOrDefault("suncolor"), world.Properties.GetValueOrDefault("sundirection"),
            world.Properties.GetValueOrDefault("ambient"), staged.ToString()];
        if (!force && ReferenceEquals(entity, _shownEntity) && values.SequenceEqual(_shownValues)) return;
        _shownEntity = entity;
        _shownValues = values;
        _updating = true;
        try
        {
            MapSunProperties? sun;
            string? error;
            _sourceValid = _stageMode
                ? MapSunProperties.TryReadStage(entity, world, out sun, out error)
                : MapSunProperties.TryRead(entity, out sun, out error);
            AmbientFields.IsVisible = staged;
            AmbientOverride.IsChecked = entity.Properties.ContainsKey("ambient");
            if (staged)
            {
                try
                {
                    float inherited = MapSunProperties.ReadAmbient(world, 1);
                    AmbientBox.Text = Number(_stageMode ? MapSunProperties.ReadAmbient(entity, inherited) : inherited);
                }
                catch (InvalidDataException exception) { _sourceValid = false; error = exception.Message; }
            }
            SourceError.Text = error;
            SourceError.IsVisible = EditSourceButton.IsVisible = !_sourceValid;
            SunEnabled.IsChecked = _stageMode
                ? new[] { "sunlight", "suncolor", "sundirection" }.Any(entity.Properties.ContainsKey)
                : sun is not null || !_sourceValid;
            if (_sourceValid)
            {
                // Initial fields are an authoring draft; absent source remains disabled until Apply.
                Vector3 color = sun?.Color ?? Vector3.One;
                _sourceColor = color;
                RedBox.Text = ColorNumber(color.X);
                GreenBox.Text = ColorNumber(color.Y);
                BlueBox.Text = ColorNumber(color.Z);
                IntensityBox.Text = Number(sun?.Intensity ?? 1);
                var angles = sun?.Angles ?? new Vector3(-45, 0, 0);
                PitchBox.Text = Number(angles.X);
                YawBox.Text = Number(angles.Y);
                RollBox.Text = Number(angles.Z);
            }
            RefreshEnabled();
        }
        finally { _updating = false; }
        RefreshColor();
        RefreshDirection();
    }

    private void RefreshEnabled()
    {
        SunFields.IsEnabled = _sourceValid && SunEnabled.IsChecked == true;
        AmbientBox.IsEnabled = AmbientOverride.IsChecked == true;
        ApplySunButton.IsEnabled = _sourceValid || SunEnabled.IsChecked != true;
    }

    private void RefreshDirection()
    {
        if (_updating) return;
        if (TryReadNumber(PitchBox, out float pitch) && TryReadNumber(YawBox, out float yaw))
            DirectionPicker.SetDirection(pitch, yaw);
    }

    private void RefreshColor()
    {
        if (_updating) return;
        if (!TryReadNumber(RedBox, out float red) || red < 0 ||
            !TryReadNumber(GreenBox, out float green) || green < 0 ||
            !TryReadNumber(BlueBox, out float blue) || blue < 0) return;

        Vector3 color = new(red, green, blue);
        if (ColorPicker.SelectedColor == color) return;
        _updating = true;
        try { ColorPicker.SelectedColor = color; }
        finally { _updating = false; }
    }

    private void PickerColorChanged()
    {
        if (_updating) return;
        Vector3 color = ColorPicker.SelectedColor;
        _sourceColor = color;
        _updating = true;
        try
        {
            RedBox.Text = ColorNumber(color.X);
            GreenBox.Text = ColorNumber(color.Y);
            BlueBox.Text = ColorNumber(color.Z);
        }
        finally { _updating = false; }
    }

    private async Task ApplyAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        MapEntity? entity = _stageMode ? _shownEntity : session.Document.World;
        if (entity is null || !session.Document.Entities.Contains(entity)) return;
        bool applied = false;
        try
        {
            _applying = true;
            MapSunProperties? sun = SunEnabled.IsChecked == true ? new MapSunProperties
            {
                Intensity = ReadNumber(IntensityBox, "intensity"),
                Color = new Vector3(ReadColor(RedBox, "red", _sourceColor.X),
                    ReadColor(GreenBox, "green", _sourceColor.Y), ReadColor(BlueBox, "blue", _sourceColor.Z)),
                Angles = new Vector3(ReadNumber(PitchBox, "pitch"), ReadNumber(YawBox, "yaw"), ReadNumber(RollBox, "roll"))
            } : null;
            float? ambient = AmbientFields.IsVisible && AmbientOverride.IsChecked == true
                ? ReadNumber(AmbientBox, "ambient") : null;
            if (ambient < 0) throw new ArgumentException("Ambient must be a nonnegative sky diffuse multiplier.");
            finishGestures();
            var world = session.Document.World;
            var proposed = entity.Clone();
            if (sun is { } value) value.ApplyTo(proposed, _stageMode ? world : null);
            else MapSunProperties.RemoveFrom(proposed);
            if (AmbientFields.IsVisible)
            {
                if (ambient is { } multiplier) proposed.Properties["ambient"] = Number(multiplier);
                else proposed.Properties.Remove("ambient");
            }
            if (proposed.Properties.Count != entity.Properties.Count ||
                proposed.Properties.Any(pair => entity.Properties.GetValueOrDefault(pair.Key) != pair.Value))
                session.Edit(() =>
                {
                    entity.Properties.Clear();
                    foreach (var pair in proposed.Properties) entity.Properties.Add(pair.Key, pair.Value);
                });
            applied = true;
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Sunlight", exception.Message); }
        finally
        {
            _applying = false;
            if (applied) Refresh(session, entity, force: true);
        }
    }

    private static float ReadNumber(TextBox input, string name) =>
        TryReadNumber(input, out float value) ? value : throw new ArgumentException($"Enter a finite number for sun {name}.");

    private static bool TryReadNumber(TextBox input, out float value) =>
        float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static float ReadColor(TextBox input, string name, float source) =>
        input.Text == ColorNumber(source) ? source : ReadNumber(input, name);

    private static string ColorNumber(float value) => value.ToString("G6", CultureInfo.InvariantCulture);
    private static string Number(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}
