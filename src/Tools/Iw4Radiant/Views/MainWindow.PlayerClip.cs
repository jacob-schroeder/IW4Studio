using Avalonia.Interactivity;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private void DrawPlayerClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        SetTool(EditorTool.Brush);
        Workspace.Materials.UsePlayerClip(_session);
        SetStatus("Draw player clip in a grid view · Set Base and Depth, then shape the brush around the model · Invisible in game; blocks players");
        Workspace.FocusActiveView();
    }

    private async void ApplyPlayerClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        try
        {
            PlayerClipEditing.Apply(_session);
            Workspace.Materials.UsePlayerClip(_session);
            SetStatus("Selected brushes now block players and are invisible in game. Magenta outlines show the editable clip volumes.");
        }
        catch (ArgumentException exception)
        {
            await _dialogs.MessageAsync("Player clip", exception.Message);
        }
    }
}
