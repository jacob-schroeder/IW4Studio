using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private async Task RunPhaseBCommandAsync(string title, Action command, string success)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        try
        {
            command();
            SetStatus(success);
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            await _dialogs.MessageAsync(title, exception.Message);
        }
        Workspace.FocusActiveView();
    }

    private async void FlipSelection_Click(object? sender, RoutedEventArgs e)
    {
        int axis = AxisIndex(sender);
        await RunPhaseBCommandAsync("Flip selection", () => SelectionTransforms.Flip(_session, axis),
            $"Flipped the selection across {AxisName(axis)}. Texture lock was {(_session.TextureLock ? "applied" : "disabled")}.");
    }

    private async void RotateSelection90_Click(object? sender, RoutedEventArgs e)
    {
        int axis = AxisIndex(sender);
        await RunPhaseBCommandAsync("Rotate selection", () => SelectionTransforms.Rotate90(_session, axis),
            $"Rotated the selection 90° around {AxisName(axis)}. Texture lock was {(_session.TextureLock ? "applied" : "disabled")}.");
    }

    private async void TextureTransform_Click(object? sender, RoutedEventArgs e)
    {
        SurfaceEditing.TextureTransform transform = Enum.Parse<SurfaceEditing.TextureTransform>(CommandTag(sender));
        string description = transform switch
        {
            SurfaceEditing.TextureTransform.FlipU => "Flipped selected texture projections horizontally.",
            SurfaceEditing.TextureTransform.FlipV => "Flipped selected texture projections vertically.",
            _ => "Rotated selected texture projections 90°."
        };
        await RunPhaseBCommandAsync("Texture projection", () => SurfaceEditing.TransformTexture(_session, transform), description);
    }

    private void SelectionVolume_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        SelectionVolumeMode mode = Enum.Parse<SelectionVolumeMode>(CommandTag(sender));
        bool enable = RequestedCheck(sender, _session.SelectionVolumeMode != mode);
        if (_session.Tool != EditorTool.Select) SetTool(EditorTool.Select);
        else FinishGestures();
        _session.SelectionVolumeMode = enable ? mode : SelectionVolumeMode.None;
        _session.Refresh();
        SetStatus(enable
            ? $"{VolumeLabel(mode)} armed: drag a rectangle in a grid view. The selection mode clears after the drag or Escape."
            : "Volume selection cancelled.");
        Workspace.FocusActiveView();
    }

    private async void CsgMerge_Click(object? sender, RoutedEventArgs e) =>
        await RunPhaseBCommandAsync("CSG merge", () => CsgEditing.Merge(_session),
            "Merged two adjacent brushes into one convex brush.");

    private async void CsgHollow_Click(object? sender, RoutedEventArgs e) =>
        await RunPhaseBCommandAsync("CSG hollow", () => CsgEditing.Hollow(_session),
            $"Hollowed the selected box with {_session.GridSize:G} unit walls.");

    private async void PatchWeld_Click(object? sender, RoutedEventArgs e) =>
        await RunPhaseBCommandAsync("Weld patch control points", () => PatchEditing.Weld(_session),
            "Welded the selected patch control points to their shared center.");

    private async void PatchLock_Click(object? sender, RoutedEventArgs e) =>
        await RunPhaseBCommandAsync("Lock patch control points", () => PatchEditing.SetLocked(_session, locked: true),
            "Locked the selected patch control points for this editing session.");

    private async void PatchUnlock_Click(object? sender, RoutedEventArgs e) =>
        await RunPhaseBCommandAsync("Unlock patch control points", () => PatchEditing.SetLocked(_session, locked: false),
            "Unlocked the selected patch control points.");

    private void AxisLock_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        AxisLock axis = Enum.Parse<AxisLock>(CommandTag(sender));
        bool enable = RequestedCheck(sender, (_session.AxisLock & axis) == 0);
        if (enable) _session.AxisLock |= axis;
        else _session.AxisLock &= ~axis;
        _session.Refresh();
        SetStatus($"{axis} movement is now {(enable ? "locked" : "unlocked")}.");
        Workspace.FocusActiveView();
    }

    private void CubicClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        _session.CubicClipEnabled = RequestedCheck(sender, !_session.CubicClipEnabled);
        _session.Refresh();
        SetStatus(_session.CubicClipEnabled
            ? $"Camera cubic clipping enabled at {_session.CubicClipDistance:G} units."
            : "Camera cubic clipping disabled.");
        Workspace.FocusActiveView();
    }

    private void CubicClipDistance_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        int direction = int.Parse(CommandTag(sender), System.Globalization.CultureInfo.InvariantCulture);
        _session.CubicClipDistance = Math.Clamp(direction < 0
            ? _session.CubicClipDistance / 2 : _session.CubicClipDistance * 2, 256, 65536);
        _session.Refresh();
        SetStatus($"Camera cubic clipping distance: {_session.CubicClipDistance:G} units.");
        Workspace.FocusActiveView();
    }

    private void AlphaPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        _session.AlphaPreviewEnabled = RequestedCheck(sender, !_session.AlphaPreviewEnabled);
        _session.Refresh();
        SetStatus($"Alpha-blended material preview {(_session.AlphaPreviewEnabled ? "enabled" : "disabled")}.");
        Workspace.FocusActiveView();
    }

    private void VisibilityCategory_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        string category = CommandTag(sender);
        EditorFilter mask = VisibilityMask(category);
        bool show = RequestedCheck(sender, (_session.Visibility.ExcludedKinds & mask) != 0);
        if (show) _session.Visibility.ExcludedKinds &= ~mask;
        else _session.Visibility.ExcludedKinds |= mask;
        _session.Refresh();
        SetStatus($"{category} are now {(show ? "visible" : "hidden")}.");
        Workspace.FocusActiveView();
    }

    private void RefreshPhaseBControls()
    {
        bool canTransform = _session.CanTransformSelection;
        bool canFlip = canTransform && !_session.Selection.Items.Any(item => EditorSelection.Owner(item) is MapEntity);
        SetEnabled(canFlip, FlipXMenu, FlipYMenu, FlipZMenu, FlipXToolbar, FlipYToolbar, FlipZToolbar);
        SetEnabled(canTransform, RotateXMenu, RotateYMenu, RotateZMenu, RotateXToolbar, RotateYToolbar, RotateZToolbar);

        bool hasFaces = SurfaceEditing.GetFaces(_session).Any();
        SetEnabled(hasFaces, TextureFlipUMenu, TextureFlipVMenu, TextureRotateMenu,
            TextureFlipUToolbar, TextureFlipVToolbar, TextureRotateToolbar);
        SetEnabled(CsgEditing.CanMerge(_session), CsgMergeMenu, CsgMergeToolbar);
        SetEnabled(CsgEditing.CanHollow(_session), CsgHollowMenu, CsgHollowToolbar);

        TerrainVertexSelection[] patchVertices = PatchEditing.SelectedVertices(_session);
        bool hasLocked = patchVertices.Any(_session.IsPatchVertexLocked);
        bool hasUnlocked = patchVertices.Any(vertex => !_session.IsPatchVertexLocked(vertex));
        SetEnabled(patchVertices.Length >= 2 && !hasLocked, PatchWeldMenu, PatchWeldToolbar);
        SetEnabled(hasUnlocked, PatchLockMenu, PatchLockToolbar);
        SetEnabled(hasLocked, PatchUnlockMenu, PatchUnlockToolbar);

        SetChecked((_session.AxisLock & AxisLock.X) != 0, AxisLockXMenu, AxisLockXToolbar);
        SetChecked((_session.AxisLock & AxisLock.Y) != 0, AxisLockYMenu, AxisLockYToolbar);
        SetChecked((_session.AxisLock & AxisLock.Z) != 0, AxisLockZMenu, AxisLockZToolbar);
        SetChecked(_session.SelectionVolumeMode == SelectionVolumeMode.CompleteTall, CompleteTallMenu, CompleteTallToolbar);
        SetChecked(_session.SelectionVolumeMode == SelectionVolumeMode.PartialTall, PartialTallMenu, PartialTallToolbar);
        SetChecked(_session.SelectionVolumeMode == SelectionVolumeMode.Touching, TouchingMenu, TouchingToolbar);
        SetChecked(_session.SelectionVolumeMode == SelectionVolumeMode.Inside, InsideMenu, InsideToolbar);

        SetChecked(_session.CubicClipEnabled, CubicClipMenu, CubicClipToolbar);
        CubicClipDistanceText.Text = _session.CubicClipDistance.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        SetEnabled(_session.CubicClipDistance > 256, CubicClipNearMenu, CubicClipNearToolbar);
        SetEnabled(_session.CubicClipDistance < 65536, CubicClipFarMenu, CubicClipFarToolbar);
        SetChecked(_session.AlphaPreviewEnabled, AlphaPreviewMenu, AlphaPreviewToolbar);

        SetVisibilityCheck("Brushes", BrushesVisibleMenu, BrushesVisibleToolbar);
        SetVisibilityCheck("Patches", PatchesVisibleMenu, PatchesVisibleToolbar);
        SetVisibilityCheck("Models", ModelsVisibleMenu, ModelsVisibleToolbar);
        SetVisibilityCheck("Entities", EntitiesVisibleMenu, EntitiesVisibleToolbar);
    }

    private void SetVisibilityCheck(string category, MenuItem menu, ToggleButton button)
    {
        EditorFilter mask = VisibilityMask(category);
        SetChecked((_session.Visibility.ExcludedKinds & mask) == 0, menu, button);
    }

    private static EditorFilter VisibilityMask(string category) => category switch
    {
        "Brushes" => EditorFilter.Structural | EditorFilter.Detail | EditorFilter.NonColliding |
            EditorFilter.WeaponClip | EditorFilter.PlayerClip,
        "Patches" => EditorFilter.Terrain | EditorFilter.Curves,
        "Models" => EditorFilter.Models | EditorFilter.Prefabs,
        "Entities" => EditorFilter.Lights | EditorFilter.BrushEntities | EditorFilter.OtherEntities,
        _ => throw new ArgumentException($"Unknown visibility category '{category}'.")
    };

    private static int AxisIndex(object? sender) => int.Parse(CommandTag(sender), System.Globalization.CultureInfo.InvariantCulture);
    private static string AxisName(int axis) => axis switch { 0 => "X", 1 => "Y", 2 => "Z", _ => "?" };
    private static string VolumeLabel(SelectionVolumeMode mode) => mode switch
    {
        SelectionVolumeMode.CompleteTall => "Complete Tall",
        SelectionVolumeMode.PartialTall => "Partial Tall",
        SelectionVolumeMode.Touching => "Touching",
        SelectionVolumeMode.Inside => "Inside",
        _ => "Selection"
    };

    private static string CommandTag(object? sender) => sender is Control { Tag: { } tag }
        ? tag.ToString() ?? "" : throw new ArgumentException("The command has no tag.");

    private static bool RequestedCheck(object? sender, bool fallback) => sender switch
    {
        ToggleButton toggle => toggle.IsChecked == true,
        MenuItem menu => menu.IsChecked,
        _ => fallback
    };

    private static void SetEnabled(bool enabled, params Control[] controls)
    {
        foreach (Control control in controls) control.IsEnabled = enabled;
    }

    private static void SetChecked(bool value, params Control[] controls)
    {
        foreach (Control control in controls)
            if (control is ToggleButton toggle) toggle.IsChecked = value;
            else if (control is MenuItem menu) menu.IsChecked = value;
    }
}
