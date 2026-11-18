using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuDesignerView : UserControl
{
    public MenuDesignerView()
    {
        InitializeComponent();
        Workspace.PaneOpened += (_, _) => BrowseButton.Content = "Hide navigator";
        Workspace.PaneClosed += (_, _) => BrowseButton.Content = NavigationCaption;
        SizeChanged += (_, e) =>
        {
            bool compact = e.NewSize.Width < 780;
            if (compact == CompactToolbar.IsVisible)
                return;

            CompactToolbar.IsVisible = compact;
            Workspace.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
            Workspace.IsPaneOpen = !compact;
        };
    }

    public Control? NavigationPane
    {
        get => Workspace.Pane as Control;
        set => Workspace.Pane = value;
    }

    public string NavigationCaption { get; set; } = "Browse items";

    private void Browse_Click(object? sender, RoutedEventArgs e) =>
        Workspace.IsPaneOpen = !Workspace.IsPaneOpen;
}
