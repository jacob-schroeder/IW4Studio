using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class TransformInspector : UserControl
{
    private object[] _shownSelection = [];
    private TransformMode? _shownMode;
    private bool _updating;

    public TransformInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        ApplyTransformButton.Click += async (_, _) => await ApplyAsync(session, dialogs, finishGestures);
        AngleSnapValue.ValueChanged += (_, _) => UpdateSnapping(session, dialogs, finishGestures);
        ScaleSnapValue.ValueChanged += (_, _) => UpdateSnapping(session, dialogs, finishGestures);
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (_shownMode != session.TransformMode || !_shownSelection.SequenceEqual(session.Selection.Items))
                ResetValues(session.TransformMode);
            _shownMode = session.TransformMode;
            _shownSelection = session.Selection.Items.ToArray();
            TransformCaption.Text = session.TransformMode switch
            {
                TransformMode.Move => $"Move offset · X / Y / Z · grid {session.GridSize:G6}",
                TransformMode.Rotate => "Rotation in degrees · X / Y / Z",
                _ => "Scale multipliers · X / Y / Z"
            };
            ApplyTransformButton.Content = $"Apply {session.TransformMode.ToString().ToLowerInvariant()}";
            ApplyTransformButton.IsEnabled = session.CanTransformSelection;
            XValue.IsEnabled = YValue.IsEnabled = ZValue.IsEnabled = session.CanTransformSelection;
            if (session.SelectionBounds is { } bounds)
            {
                Vector3 pivot = bounds.Min * 0.5f + bounds.Max * 0.5f;
                PivotText.Text = FormattableString.Invariant($"Rotation / scale pivot: {pivot.X:G6} / {pivot.Y:G6} / {pivot.Z:G6}");
            }
            else PivotText.Text = "Select objects or vertices to transform.";
            if (!AngleSnapValue.IsKeyboardFocusWithin) AngleSnapValue.Value = (decimal)session.AngleSnap;
            if (!ScaleSnapValue.IsKeyboardFocusWithin) ScaleSnapValue.Value = (decimal)session.ScaleSnap;
        }
        finally { _updating = false; }
    }

    private void UpdateSnapping(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        decimal? angle = AngleSnapValue.Value, scale = ScaleSnapValue.Value;
        _updating = true;
        try
        {
            finishGestures();
            if (angle is { } angleValue) session.AngleSnap = (float)angleValue;
            if (scale is { } scaleValue) session.ScaleSnap = (float)scaleValue;
            session.Refresh();
        }
        finally { _updating = false; }
    }

    private async Task ApplyAsync(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (dialogs.BlocksInput) return;
        try
        {
            Vector3 values = new(ReadValue(XValue), ReadValue(YValue), ReadValue(ZValue));
            TransformMode mode = session.TransformMode;
            finishGestures();
            if (!session.CanTransformSelection || session.SelectionBounds is not { } bounds) return;
            Matrix4x4 matrix;
            if (mode == TransformMode.Move)
            {
                values = Snap(values, session.GridSize);
                if (values == Vector3.Zero) return;
                matrix = Matrix4x4.CreateTranslation(values);
            }
            else
            {
                Vector3 pivot = bounds.Min * 0.5f + bounds.Max * 0.5f;
                Matrix4x4 operation;
                if (mode == TransformMode.Rotate)
                {
                    values = Snap(values, session.AngleSnap);
                    values = new Vector3(values.X % 360, values.Y % 360, values.Z % 360);
                    if (values == Vector3.Zero) return;
                    values *= MathF.PI / 180;
                    operation = Matrix4x4.CreateRotationX(values.X) * Matrix4x4.CreateRotationY(values.Y) * Matrix4x4.CreateRotationZ(values.Z);
                }
                else
                {
                    if (values.X <= 0 || values.Y <= 0 || values.Z <= 0)
                        throw new ArgumentException("Scale multipliers must be positive.");
                    values = Vector3.One + Snap(values - Vector3.One, session.ScaleSnap);
                    if (values.X <= 0 || values.Y <= 0 || values.Z <= 0)
                        throw new ArgumentException("Scale multipliers must remain positive after snapping.");
                    if (values == Vector3.One) return;
                    operation = Matrix4x4.CreateScale(values);
                }
                matrix = Matrix4x4.CreateTranslation(-pivot) * operation * Matrix4x4.CreateTranslation(pivot);
            }
            session.Edit(() => SelectionTransforms.Apply(session, matrix));
            ResetValues(mode);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        { await dialogs.MessageAsync("Transform selection", exception.Message); }
    }

    private void ResetValues(TransformMode mode)
    {
        string value = mode == TransformMode.Scale ? "1" : "0";
        XValue.Text = YValue.Text = ZValue.Text = value;
    }

    private static Vector3 Snap(Vector3 value, float step)
    {
        if (!float.IsFinite(step) || step <= 0) throw new ArgumentException("The snap increment must be positive and finite.");
        float Component(float component)
        {
            float snapped = (float)(Math.Round((double)component / step, MidpointRounding.AwayFromZero) * step);
            if (!float.IsFinite(snapped)) throw new ArgumentException("The snapped transform exceeds the supported numeric range.");
            return snapped;
        }
        return new Vector3(Component(value.X), Component(value.Y), Component(value.Z));
    }

    private static float ReadValue(TextBox input)
    {
        if (!float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
            throw new ArgumentException("Enter finite X, Y and Z transform values using a decimal point.");
        return value;
    }
}
