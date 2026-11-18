using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

public sealed partial class BehaviorKeyHandlersView : UserControl
{
    public BehaviorKeyHandlersView() => AvaloniaXamlLoader.Load(this);

    private void AddKeyHandler_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BehaviorKeyHandlersViewModel viewModel)
            return;

        var handler = viewModel.AddKeyHandler();
        Dispatcher.UIThread.Post(() =>
        {
            var container = this.FindControl<ItemsControl>("HandlerList")?.ContainerFromItem(handler);
            var inputType = container?.GetVisualDescendants().OfType<ComboBox>()
                .FirstOrDefault(box => box.Name == "InputType");
            inputType?.BringIntoView();
            inputType?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
