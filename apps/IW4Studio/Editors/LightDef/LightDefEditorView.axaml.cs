using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Editors.LightDef;

public sealed partial class LightDefEditorView : UserControl
{
    public LightDefEditorView() => AvaloniaXamlLoader.Load(this);
}
