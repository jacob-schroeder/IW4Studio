using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

public partial class GeometryInspector
{
    private object[] _bridgeSelection = [];
    private TerrainBridgeEdge[] _bridgeFirstEdges = [];
    private TerrainBridgeEdge[] _bridgeSecondEdges = [];
    private string? _bridgeIssue;

    private void InitializeBridge(EditorSession session, EditorDialogs dialogs, Action finishGestures,
        Action<string> setStatus)
    {
        BridgeFirstEdge.SelectionChanged += (_, _) => { if (!_updating) UpdateBridgeInfo(); };
        BridgeSecondEdge.SelectionChanged += (_, _) => { if (!_updating) UpdateBridgeInfo(); };
        BridgeRows.ValueChanged += (_, _) => { if (!_updating) UpdateBridgeInfo(); };
        BridgeRise.ValueChanged += (_, _) => { if (!_updating) UpdateBridgeInfo(); };
        CreateBridgeButton.Click += async (_, _) => await RunAsync(dialogs, finishGestures, () =>
        {
            if (BridgeRows.Value is not { } rows || BridgeRise.Value is not { } rise)
                throw new ArgumentException("Choose the bridge rows and center rise.");
            bool curved = _bridgeFirstEdges[BridgeFirstEdge.SelectedIndex].IsCurve;
            GeometryEditing.CreateBridge(session, BridgeFirstEdge.SelectedIndex, BridgeSecondEdge.SelectedIndex,
                (int)rows, (float)rise);
            BridgePanel.IsExpanded = false;
            setStatus(curved
                ? $"Curved bridge created with {(int)rows} control rows. Undo removes it."
                : $"Bridge created as editable solid terrain with {(int)rows} rows. Undo removes it.");
        });
    }

    private void RefreshBridge(EditorSession session)
    {
        object[] selected = session.Selection.Items.ToArray();
        bool newPair = !_bridgeSelection.SequenceEqual(selected);
        _bridgeSelection = selected;
        int chosenFirst = BridgeFirstEdge.SelectedIndex, chosenSecond = BridgeSecondEdge.SelectedIndex;
        _bridgeFirstEdges = _bridgeSecondEdges = [];
        string? issue = null;
        if (selected.Length == 2)
        {
            try
            {
                _bridgeFirstEdges = GeometryEditing.BridgeEdges(session.Document, selected[0]);
                _bridgeSecondEdges = GeometryEditing.BridgeEdges(session.Document, selected[1]);
                if (ReferenceEquals(EditorSelection.Owner(selected[0]), EditorSelection.Owner(selected[1])))
                    issue = "Choose boundaries on two different source surfaces.";
                else if (_bridgeFirstEdges.Length > 0 && _bridgeSecondEdges.Length > 0 &&
                    _bridgeFirstEdges[0].IsCurve != _bridgeSecondEdges[0].IsCurve)
                    issue = "Pair a curved patch with another curved patch, or pair terrain and brush edges.";
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                InvalidDataException or NotSupportedException or FormatException)
            { issue = exception.Message; }
        }
        BridgeFirstCaption.Text = "1 · Start " + SurfaceName(selected.ElementAtOrDefault(0));
        BridgeSecondCaption.Text = "2 · End " + SurfaceName(selected.ElementAtOrDefault(1));
        bool curvedPair = _bridgeFirstEdges.Length > 0 && _bridgeSecondEdges.Length > 0 &&
            _bridgeFirstEdges[0].IsCurve && _bridgeSecondEdges[0].IsCurve;
        BridgeRowsCaption.Text = curvedPair ? "Curve control rows" : "Rows across the gap";
        BridgeRows.Maximum = curvedPair ? 15 : 16;
        BridgeRows.Increment = curvedPair ? 2 : 1;
        if (curvedPair && BridgeRows.Value is { } rowCount && rowCount % 2 == 0)
            BridgeRows.Value = rowCount == 16 ? 15 : rowCount + 1;
        BridgeFirstEdge.ItemsSource = _bridgeFirstEdges.Select(edge => edge.Label).ToArray();
        BridgeSecondEdge.ItemsSource = _bridgeSecondEdges.Select(edge => edge.Label).ToArray();
        if (newPair)
        {
            (int First, int Second)? suggestion = TerrainBridge.Suggest(_bridgeFirstEdges, _bridgeSecondEdges);
            chosenFirst = suggestion?.First ?? -1;
            chosenSecond = suggestion?.Second ?? -1;
            if (suggestion is not null) BridgePanel.IsExpanded = true;
        }
        BridgeFirstEdge.SelectedIndex = chosenFirst >= 0 && chosenFirst < _bridgeFirstEdges.Length ? chosenFirst : -1;
        BridgeSecondEdge.SelectedIndex = chosenSecond >= 0 && chosenSecond < _bridgeSecondEdges.Length ? chosenSecond : -1;
        _bridgeIssue = issue;
        BridgeReadyText.Text = selected.Length != 2
            ? "Select two world curved patches, terrains, or brushes. Shift-click the second surface; Face mode picks a specific brush face."
            : issue ?? "Choose an edge on each surface. The nearest compatible pair is suggested when available.";
        UpdateBridgeInfo();
    }

    private void UpdateBridgeInfo()
    {
        int first = BridgeFirstEdge.SelectedIndex, second = BridgeSecondEdge.SelectedIndex;
        bool ready = _bridgeIssue is null && first >= 0 && first < _bridgeFirstEdges.Length &&
            second >= 0 && second < _bridgeSecondEdges.Length && BridgeRows.Value is not null && BridgeRise.Value is not null;
        if (!ready)
        {
            CreateBridgeButton.IsEnabled = false;
            BridgeRiseCaption.Text = "Center rise · units";
            BridgeShapeHelp.Text = "The source surfaces stay intact. One Undo removes the bridge. A closed rim can bulge outward or inward; an open edge can rise along Z.";
            BridgeMaterialText.Text = "Choose one edge on each surface. The start surface supplies the bridge material.";
            return;
        }
        TerrainBridgeEdge start = _bridgeFirstEdges[first], end = _bridgeSecondEdges[second];
        bool closedRims = start.IsCurve && start.IsClosed && end.IsClosed;
        BridgeRiseCaption.Text = closedRims ? "Center bulge · units" : "Center rise · units";
        BridgeShapeHelp.Text = closedRims
            ? "The source curves stay intact. The new curved patch follows both rims; one Undo removes it. Positive bulge pushes outward from the rim centers, negative bulge pulls inward."
            : start.IsCurve
                ? "The source curves stay intact. The new curved patch follows both boundaries; one Undo removes it. Positive rise lifts its center along Z."
                : "The source surfaces stay intact. The new terrain is solid and editable; one Undo removes it. Positive rise makes a hump, negative rise makes a dip.";
        try
        {
            if (CaulkMaterial.IsCaulk(start.Material) || ClipBrushMaterial.IsPlayerClip(start.Material))
                throw new ArgumentException("The start surface needs a visible material. Choose a textured surface first.");
            _ = TerrainBridge.Create(start, end, (int)BridgeRows.Value!.Value, (float)BridgeRise.Value!.Value);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            CreateBridgeButton.IsEnabled = false;
            BridgeReadyText.Text = exception.Message;
            BridgeMaterialText.Text = "No source geometry will change until the bridge can be created.";
            return;
        }
        CreateBridgeButton.IsEnabled = true;
        BridgeReadyText.Text = start.IsCurve
            ? "Ready · the chosen curves connect without folding. Create adds one editable curved patch."
            : "Ready · the chosen edges connect without folding. Create adds one editable terrain piece.";
        BridgeMaterialText.Text = string.Equals(start.Material, end.Material, StringComparison.Ordinal)
            ? $"Material · {start.Material}. Texture coordinates blend between the two matching-material edges."
            : $"Material · {start.Material} from the start surface. Texture continues across the gap; the end surface uses {end.Material}.";
    }

    private static string SurfaceName(object? selected) => selected switch
    {
        MapTerrain { IsCurve: true } curve => $"curved patch · {curve.Width} × {curve.Height}",
        MapTerrain { IsCurve: false } terrain => $"terrain · {terrain.Width} × {terrain.Height}",
        BrushFaceSelection => "brush face",
        MapBrush => "brush top face",
        _ => "surface"
    };
}
