using Avalonia.Interactivity;
using Iw4Radiant.Compilation;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private string? _buildLinkerPath;
    private string _buildTemplatePath = "";
    private string[] _buildProviderPaths = [];
    private string? _buildOutputFolder;

    private async void Build_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        if (!await _files.SaveAsync(false) || _session.FilePath is not { } sourcePath) return;
        try
        {
            MapDocument document = _session.Document.Clone();
            var models = new Dictionary<string, XModelSource>(StringComparer.Ordinal);
            foreach (string name in document.Entities.Where(entity => entity.ClassName == "misc_model" && MapStaticModelCompiler.CastsShadow(entity))
                         .Select(entity => entity.Properties.GetValueOrDefault("model") ?? "").Distinct(StringComparer.Ordinal))
                models.Add(name, Workspace.Models.ResolveModel(name) ??
                    throw new InvalidDataException($"Model '{name}' is unavailable. Load it in the model browser before building."));
            var materials = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
            foreach (string name in document.World.Brushes.SelectMany(brush => brush.Faces)
                         .Select(face => face.Material).Concat(document.World.Terrains.Select(terrain => terrain.Material))
                         .Concat(models.Values.SelectMany(model => model.Document.Materials).Select(material => material.Name))
                         .Distinct(StringComparer.Ordinal))
            {
                if (ClipBrushMaterial.IsPlayerClip(name)) continue;
                materials.Add(name, ResolveMaterial(name) ??
                    throw new InvalidDataException($"Material '{name}' is unavailable. Load it in the asset browser before building."));
            }
            string sourceFolder = Path.GetDirectoryName(sourcePath) ??
                throw new InvalidDataException("The saved map has no containing directory.");
            string buildFolder = Path.Combine(sourceFolder, "map_build");
            var dialog = new MapBuildWindow(document, sourcePath, materials, models,
                _buildLinkerPath ?? FindBuildLinker() ?? "", _buildTemplatePath, _buildProviderPaths,
                _buildOutputFolder ?? (Directory.Exists(buildFolder) ? buildFolder : sourceFolder));
            await _dialogs.ShowModalAsync(() => dialog.ShowDialog<object?>(this));
            if (dialog.CompletedDirectory is not { } completedDirectory) return;
            _buildLinkerPath = dialog.LinkerPath;
            _buildTemplatePath = dialog.TemplatePath;
            _buildProviderPaths = dialog.ProviderPaths.ToArray();
            _buildOutputFolder = dialog.OutputFolder;
            SetStatus($"Built PS3 map: {completedDirectory}");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            await _dialogs.MessageAsync("Cannot build map", exception.Message);
        }
    }

    private static string? FindBuildLinker()
    {
#if DEBUG
        string[] configurations = ["Debug", "Release"];
#else
        string[] configurations = ["Release", "Debug"];
#endif
        foreach (string file in new[] { "D3dbspLinker.dll", "D3dbspLinker", "D3dbspLinker.exe" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, file);
            if (File.Exists(path)) return path;
        }
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string project = Path.Combine(directory.FullName, "src", "Tools", "D3dbspLinker");
            if (!File.Exists(Path.Combine(project, "D3dbspLinker.csproj"))) continue;
            foreach (string configuration in configurations)
            {
                string path = Path.Combine(project, "bin", configuration, "net10.0", "D3dbspLinker.dll");
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }
}
