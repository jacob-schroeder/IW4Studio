using System.Globalization;
using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class SurfaceInspector : UserControl
{
    private MapFace[] _shownFaces = [];
    private string[] _shownProjections = [];
    private MapFace? _shownReference;
    private bool _updating;

    public SurfaceInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        WireAdjustment(ShiftXDecrease, ShiftXIncrease, ShiftXValue, "horizontal shift", 1);
        WireAdjustment(ShiftYDecrease, ShiftYIncrease, ShiftYValue, "vertical shift", 1);
        WireAdjustment(WidthDecrease, WidthIncrease, WidthValue, "horizontal repeat size", 1);
        WireAdjustment(HeightDecrease, HeightIncrease, HeightValue, "vertical repeat size", 1);
        WireAdjustment(RotationDecrease, RotationIncrease, RotationValue, "rotation", 15);
        WireAdjustment(SkewDecrease, SkewIncrease, SkewValue, "skew", 0.1f);
        ApplyProjectionButton.Click += async (_, _) => await ApplyProjectionAsync(session, dialogs, finishGestures);
        FitButton.Click += async (_, _) => await FitAsync(session, dialogs, finishGestures);
        RevertProjectionButton.Click += (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            _shownReference = null;
            RepeatsXValue.Text = RepeatsYValue.Text = "1";
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
        }
        finally { _updating = false; }
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

    private TextBox[] ProjectionBoxes() => [WidthValue, HeightValue, ShiftXValue, ShiftYValue, RotationValue, SkewValue];

    private static void SetValue(TextBox box, float value) => box.Text = value.ToString("R", CultureInfo.InvariantCulture);

    private static float ReadValue(TextBox box, string name)
    {
        if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException($"Enter a finite number for {name}, using a decimal point.");
        return value;
    }
}
