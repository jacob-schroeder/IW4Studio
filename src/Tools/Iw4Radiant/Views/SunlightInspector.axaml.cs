using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class SunlightInspector : UserControl
{
    private MapEntity? _shownWorld;
    private string?[] _shownValues = [];
    private bool _updating, _applying, _sourceValid = true;

    public SunlightInspector() => InitializeComponent();

    internal event Action? EditSourceRequested;

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        ApplySunButton.Click += async (_, _) => await ApplyAsync(session, dialogs, finishGestures);
        RevertSunButton.Click += (_, _) => { if (!dialogs.BlocksInput) RefreshWorld(session, force: true); };
        EditSourceButton.Click += (_, _) => { if (!dialogs.BlocksInput) EditSourceRequested?.Invoke(); };
        SunEnabled.IsCheckedChanged += (_, _) => RefreshEnabled();
        RedBox.TextChanged += (_, _) => RefreshColor();
        GreenBox.TextChanged += (_, _) => RefreshColor();
        BlueBox.TextChanged += (_, _) => RefreshColor();
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
    }

    internal void RefreshWorld(EditorSession session, bool force = false)
    {
        if (_applying) return;
        var world = session.Document.World;
        string?[] values = [world.Properties.GetValueOrDefault("sunlight"),
            world.Properties.GetValueOrDefault("suncolor"), world.Properties.GetValueOrDefault("sundirection")];
        if (!force && ReferenceEquals(world, _shownWorld) && values.SequenceEqual(_shownValues)) return;
        _shownWorld = world;
        _shownValues = values;
        _updating = true;
        try
        {
            _sourceValid = MapSunProperties.TryRead(world, out var sun, out string? error);
            SourceError.Text = error;
            SourceError.IsVisible = EditSourceButton.IsVisible = !_sourceValid;
            SunEnabled.IsChecked = sun is not null || !_sourceValid;
            if (_sourceValid)
            {
                // Initial fields are an authoring draft; absent source remains disabled until Apply.
                Vector3 color = sun?.Color ?? Vector3.One;
                RedBox.Text = Number(color.X);
                GreenBox.Text = Number(color.Y);
                BlueBox.Text = Number(color.Z);
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
        ColorSwatch.Background = TryReadNumber(RedBox, out float red) && red >= 0 &&
            TryReadNumber(GreenBox, out float green) && green >= 0 &&
            TryReadNumber(BlueBox, out float blue) && blue >= 0
            ? new SolidColorBrush(Color.FromRgb(Channel(red), Channel(green), Channel(blue)))
            : Brushes.Transparent;

        static byte Channel(float value) => (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255);
    }

    private async Task ApplyAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        bool applied = false;
        try
        {
            _applying = true;
            MapSunProperties? sun = SunEnabled.IsChecked == true ? new MapSunProperties
            {
                Intensity = ReadNumber(IntensityBox, "intensity"),
                Color = new Vector3(ReadNumber(RedBox, "red"), ReadNumber(GreenBox, "green"), ReadNumber(BlueBox, "blue")),
                Angles = new Vector3(ReadNumber(PitchBox, "pitch"), ReadNumber(YawBox, "yaw"), ReadNumber(RollBox, "roll"))
            } : null;
            finishGestures();
            var world = session.Document.World;
            var proposed = world.Clone();
            if (sun is { } value) value.ApplyTo(proposed);
            else MapSunProperties.RemoveFrom(proposed);
            if (proposed.Properties.Count != world.Properties.Count ||
                proposed.Properties.Any(pair => world.Properties.GetValueOrDefault(pair.Key) != pair.Value))
                session.Edit(() =>
                {
                    if (sun is { } appliedSun) appliedSun.ApplyTo(world);
                    else MapSunProperties.RemoveFrom(world);
                });
            applied = true;
        }
        catch (ArgumentException exception) { await dialogs.MessageAsync("Sunlight", exception.Message); }
        finally
        {
            _applying = false;
            if (applied) RefreshWorld(session, force: true);
        }
    }

    private static float ReadNumber(TextBox input, string name) =>
        TryReadNumber(input, out float value) ? value : throw new ArgumentException($"Enter a finite number for sun {name}.");

    private static bool TryReadNumber(TextBox input, out float value) =>
        float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static string Number(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}
