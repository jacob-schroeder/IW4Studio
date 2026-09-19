using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuDesignerView : UserControl
{
    public MenuDesignerView() => AvaloniaXamlLoader.Load(this);
}
