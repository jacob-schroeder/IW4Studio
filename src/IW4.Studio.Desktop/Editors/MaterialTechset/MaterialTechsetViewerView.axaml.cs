using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Editors.MaterialTechset;

public sealed partial class MaterialTechsetViewerView : UserControl
{
    public MaterialTechsetViewerView() => AvaloniaXamlLoader.Load(this);

    private void FitGraphButton_Click(object? sender, RoutedEventArgs e) =>
        this.FindControl<MaterialTechniqueGraphControl>("TechniqueGraph")?.Fit();
}
