using Avalonia.Controls;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// View-owned modal launcher shared by Menu and MenuFile editors. Documents
/// remain unaware of Avalonia; both hosts use the same isolated draft and
/// atomic apply boundary.
/// </summary>
internal static class MenuItemBehaviorBuilderService
{
    public static async Task ShowAsync(
        Window owner,
        MenuItemBehaviorEditRequestedEventArgs request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var session = new MenuItemBehaviorBuilderSessionViewModel(
            request.Value,
            request.ItemTitle,
            request.ExpressionSupport,
            request.SupportsListBoxDoubleClick);
        var window = new MenuItemBehaviorBuilderWindow(
            session,
            request.Apply);
        _ = await window.ShowDialog<bool>(owner);
    }
}
