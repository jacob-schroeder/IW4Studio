using Avalonia.Interactivity;
using IW4.Formats.SourceFormat.Character;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private async void Factions_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        try
        {
            MapFactionSettings before = MapFactionAuthoring.Read(_session.Document.World.Properties);
            var window = new FactionsWindow(before, FindBootstrapAssets());
            MapFactionSettings? selected = await _dialogs.ShowModalAsync(() => window.ShowDialog<MapFactionSettings?>(this));
            if (selected is null || selected == before) return;
            _session.Edit(() => MapFactionAuthoring.Write(_session.Document.World.Properties, selected));
            SetStatus("Faction defaults updated. Save the map and rebuild to use them in game.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            await _dialogs.MessageAsync("Cannot open factions", exception.Message);
        }
    }

    private string? FindBootstrapAssets()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
        if (Directory.Exists(Path.Combine(local, "xmodel_native"))) return local;
        if ((_buildLinkerPath ?? FindBuildLinker()) is { } linker)
        {
            string candidate = Path.Combine(Path.GetDirectoryName(linker) ?? "", "bootstrap", "ps3");
            if (Directory.Exists(Path.Combine(candidate, "xmodel_native"))) return candidate;
        }
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Resources", "Bootstrap", "ps3");
            if (Directory.Exists(Path.Combine(candidate, "xmodel_native"))) return candidate;
        }
        return null;
    }
}
