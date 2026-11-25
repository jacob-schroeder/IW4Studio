using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.XModel;

internal sealed class XModelImportWarningDialog : Window
{
    private XModelImportWarningDialog(string fileName, IReadOnlyList<string> reasons)
    {
        Title = "Import this model anyway?";
        Icon = AppIcon.Create();
        Width = 510;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel import", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var import = new Button { Content = "Import anyway" };
        import.Classes.Add("primary");
        import.Click += (_, _) => Close(true);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"{fileName} may run poorly on PS3 hardware.",
                    FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
                },
                new ScrollViewer
                {
                    MaxHeight = 240,
                    Content = new TextBlock { Text = string.Join("\n\n", reasons), TextWrapping = TextWrapping.Wrap }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, import }
                }
            }
        };
        Opened += (_, _) => cancel.Focus();
    }

    internal static async Task<bool> ConfirmAsync(Control editor, object viewModel, string fileName, IReadOnlyList<string> reasons)
    {
        if (TopLevel.GetTopLevel(editor) is not Window { IsVisible: true } owner ||
            !ReferenceEquals(editor.DataContext, viewModel))
            return false;
        if (reasons.Count == 0)
            return true;

        bool accepted = await new XModelImportWarningDialog(fileName, reasons).ShowDialog<bool>(owner);
        return accepted && ReferenceEquals(editor.DataContext, viewModel) && ReferenceEquals(TopLevel.GetTopLevel(editor), owner);
    }
}
