using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Iw4Radiant.Views;

internal sealed class EditorDialogs(Window owner, Action<string> setStatus)
{
    private readonly Window _owner = owner;
    internal bool IsBusy { get; private set; }
    internal bool IsOpen { get; private set; }
    internal bool BlocksInput => IsBusy || IsOpen;

    internal void SetBusy(bool value)
    {
        IsBusy = value;
        _owner.IsEnabled = !value;
        if (value) setStatus("Working…");
    }

    internal Task MessageAsync(string title, string message) => ChoiceAsync(title, message, ["OK"]);
    internal async Task<string?> ChoiceAsync(string title, string message, string[] choices)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var dialog = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        foreach (string choice in choices)
        {
            var button = new Button { Content = choice, MinWidth = 76 };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 20,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons }
        };
        return await ShowModalAsync(() => dialog.ShowDialog<string?>(_owner));
    }

    internal async Task<T> ShowModalAsync<T>(Func<Task<T>> show)
    {
        IsOpen = true;
        try { return await show(); }
        finally { IsOpen = false; }
    }
}
