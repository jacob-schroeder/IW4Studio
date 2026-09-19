using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Menu;

/// <summary>
/// Explicit destructive boundary for converting an inline MenuFile
/// definition into a reference to another Menu asset.
/// </summary>
internal sealed class MenuInlineDefinitionDiscardDialog : Window
{
    private MenuInlineDefinitionDiscardDialog(
        string currentName,
        string replacementName)
    {
        Title = "Retarget inline Menu";
        Icon = AppIcon.Create();
        Width = 500;
        MinWidth = 500;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 92
        };
        cancel.Click += (_, _) => Close(false);
        var retarget = new Button
        {
            Content = "Discard and retarget",
            MinWidth = 150
        };
        retarget.Classes.Add("primary");
        retarget.Click += (_, _) => Close(true);

        Content = new Border
        {
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Discard the inline Menu definition?",
                        FontSize = 19,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text =
                            $"'{currentName}' currently owns an inline definition. " +
                            $"Retargeting it to '{replacementName}' converts this " +
                            "registration to a reference and discards that inline " +
                            "definition. The MenuFile can still be reverted before Save As.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, retarget }
                    }
                }
            }
        };
    }

    public static Task<bool> ShowAsync(
        Window owner,
        string currentName,
        string replacementName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementName);
        return new MenuInlineDefinitionDiscardDialog(
            currentName,
            replacementName).ShowDialog<bool>(owner);
    }
}
