using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;

public sealed partial class ConsoleOutputView : UserControl
{
    public ConsoleOutputView() => AvaloniaXamlLoader.Load(this);
}
