using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;

public sealed partial class DependencyGraphToolView : UserControl
{
    public DependencyGraphToolView() => AvaloniaXamlLoader.Load(this);
}
