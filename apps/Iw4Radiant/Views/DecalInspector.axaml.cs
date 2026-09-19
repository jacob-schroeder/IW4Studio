using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class DecalInspector : UserControl
{
    private Func<string, bool> _supportsAlpha = _ => false;

    public DecalInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures, Func<string, bool> supportsAlpha)
    {
        _supportsAlpha = supportsAlpha;
        ProjectButton.Click += async (_, _) =>
        {
            if (dialogs.BlocksInput) return;
            try
            {
                float width = Read(WidthBox), height = Read(HeightBox), rotation = Read(RotationBox);
                Vector2 offset = new(Read(OffsetUBox), Read(OffsetVBox));
                finishGestures();
                DecalEditing.Project(session, width, height, rotation, offset, _supportsAlpha);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                NotSupportedException or InvalidDataException or FormatException)
            { await dialogs.MessageAsync("Project decal", exception.Message); }
        };
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        bool supported = _supportsAlpha(session.Material);
        DecalMaterialText.Text = supported ? $"Decal material: {session.Material}" : "Choose a material with supported alpha blending in the browser.";
        ProjectButton.IsEnabled = supported && session.Selection.Count > 0 && session.Selection.Items.All(item => item is BrushFaceSelection);
    }

    private static float Read(TextBox input) => float.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
        float.IsFinite(value) ? value : throw new ArgumentException("Enter finite numbers for the decal size, rotation, and offsets.");
}
