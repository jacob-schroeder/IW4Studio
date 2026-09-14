using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class TerrainPaintInspector : UserControl
{
    private bool _updating;
    private Func<string, bool> _supportsAlpha = _ => false;
    private Func<string, bool> _supportsVertexColor = _ => false;
    private Action? _finishGestures;

    public TerrainPaintInspector() => InitializeComponent();

    internal void InitializeActions(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Func<string, bool> supportsAlpha, Func<string, bool> supportsVertexColor)
    {
        _supportsAlpha = supportsAlpha;
        _supportsVertexColor = supportsVertexColor;
        _finishGestures = finishGestures;
        foreach (NumericUpDown input in new[] { RedValue, GreenValue, BlueValue, AlphaValue, OpacityValue, RadiusValue })
            input.ValueChanged += (_, _) => UpdateSettings(session, dialogs, finishGestures);
        PaintColorButton.Click += (_, _) => BeginPainting(session, dialogs, finishGestures, TerrainSculptMode.PaintColor);
        PaintAlphaButton.Click += (_, _) => BeginPainting(session, dialogs, finishGestures, TerrainSculptMode.PaintAlpha);
        FillColorButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () => TerrainPainting.Fill(session, alphaOnly: false));
        FillAlphaButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () => TerrainPainting.Fill(session, alphaOnly: true));
        OverlayButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures,
            () => TerrainPainting.AddOverlay(session, name => _supportsAlpha(name) && _supportsVertexColor(name)));
        RefreshSelection(session);
    }

    internal void RefreshSelection(EditorSession session)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (!RedValue.IsKeyboardFocusWithin) RedValue.Value = (decimal)MathF.Round(session.PaintColor.X * 255);
            if (!GreenValue.IsKeyboardFocusWithin) GreenValue.Value = (decimal)MathF.Round(session.PaintColor.Y * 255);
            if (!BlueValue.IsKeyboardFocusWithin) BlueValue.Value = (decimal)MathF.Round(session.PaintColor.Z * 255);
            if (!AlphaValue.IsKeyboardFocusWithin) AlphaValue.Value = (decimal)(session.PaintAlpha * 100);
            if (!OpacityValue.IsKeyboardFocusWithin) OpacityValue.Value = (decimal)(session.PaintOpacity * 100);
            if (!RadiusValue.IsKeyboardFocusWithin) RadiusValue.Value = (decimal)session.SculptRadius;
            ColorPreview.Background = new SolidColorBrush(Color.FromRgb((byte)(session.PaintColor.X * 255),
                (byte)(session.PaintColor.Y * 255), (byte)(session.PaintColor.Z * 255)));
            MapTerrain[] terrains = TerrainPainting.SelectedTerrains(session);
            bool paintable = terrains.Length > 0 && terrains.All(terrain => _supportsVertexColor(terrain.Material));
            PaintColorButton.IsEnabled = PaintAlphaButton.IsEnabled = FillColorButton.IsEnabled = FillAlphaButton.IsEnabled = paintable;
            PaintHint.Text = terrains.Length == 0 ? "Select terrain patches, curve patches, or their vertices to paint or fill color and alpha." :
                paintable ? "Drag in Top view to paint the selected patches. Shift removes tint or erases alpha. Fill affects selected vertices, or the whole selected patch. Alpha is visible with alpha-blended materials." :
                "Choose a material with a wc_ technique set for every selected patch to paint vertex color or alpha.";
            if (!paintable && session.Tool == EditorTool.Sculpt &&
                session.SculptMode is TerrainSculptMode.PaintColor or TerrainSculptMode.PaintAlpha)
            {
                _finishGestures?.Invoke();
                session.Tool = EditorTool.Select;
                session.Refresh();
            }
            bool supported = _supportsAlpha(session.Material) && _supportsVertexColor(session.Material);
            OverlayMaterialText.Text = supported ? $"Overlay material: {session.Material}" :
                "Choose an alpha-blended material with a wc_ technique set in the browser.";
            OverlayButton.IsEnabled = supported && session.Selection.Count > 0 &&
                session.Selection.Items.All(item => item is MapTerrain { IsCurve: false });
        }
        finally { _updating = false; }
    }

    private void UpdateSettings(EditorSession session, EditorDialogs dialogs, Action finishGestures)
    {
        if (_updating || dialogs.BlocksInput) return;
        finishGestures();
        _updating = true;
        try
        {
            session.PaintColor = new Vector4((float)(RedValue.Value ?? 255) / 255, (float)(GreenValue.Value ?? 255) / 255,
                (float)(BlueValue.Value ?? 255) / 255, 1);
            session.PaintAlpha = (float)(AlphaValue.Value ?? 100) / 100;
            session.PaintOpacity = (float)(OpacityValue.Value ?? 25) / 100;
            session.SculptRadius = (float)(RadiusValue.Value ?? 128);
            session.Refresh();
        }
        finally { _updating = false; }
        RefreshSelection(session);
    }

    private static void BeginPainting(EditorSession session, EditorDialogs dialogs, Action finishGestures, TerrainSculptMode mode)
    {
        if (dialogs.BlocksInput) return;
        finishGestures();
        session.SculptMode = mode;
        session.Tool = EditorTool.Sculpt;
        session.Refresh();
    }

    private static async Task RunAsync(EditorDialogs dialogs, Action finishGestures, Action action)
    {
        if (dialogs.BlocksInput) return;
        try { finishGestures(); action(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            NotSupportedException or InvalidDataException or FormatException)
        { await dialogs.MessageAsync("Terrain painting", exception.Message); }
    }
}
