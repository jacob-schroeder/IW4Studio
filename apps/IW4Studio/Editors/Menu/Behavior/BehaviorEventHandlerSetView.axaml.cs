using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

public sealed partial class BehaviorEventHandlerSetView : UserControl
{
    public BehaviorEventHandlerSetView() => AvaloniaXamlLoader.Load(this);

    private async void ChooseCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BehaviorEventHandlerSetDraftViewModel set ||
            TopLevel.GetTopLevel(this) is not Window { DataContext: MenuItemBehaviorBuilderSessionViewModel session } owner)
            return;

        var after = (sender as Button)?.DataContext as BehaviorScriptEventHandlerDraftViewModel;
        var picker = new MenuScriptCommandPickerWindow(session.MenuNames, session.ItemNames);
        if (await picker.ShowDialog<string?>(owner) is { } script)
            set.AddScript(script, after);
    }
}
