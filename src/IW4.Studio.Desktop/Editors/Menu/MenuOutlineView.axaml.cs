using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuOutlineView : UserControl
{
    public MenuOutlineView() => AvaloniaXamlLoader.Load(this);

    private async void ChangeItemTypeButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MenuDesignerViewModel
            {
                CanChangeSelectedItemType: true,
                SelectedItemType: { } currentType
            } viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var selectedNodeId = viewModel.SelectedNode?.NodeId;
        var dialog = new MenuItemTypePickerWindow(currentType);
        ItemDefType? selectedType =
            await dialog.ShowDialog<ItemDefType?>(owner);
        if (selectedType is { } type &&
            viewModel.CanChangeSelectedItemType &&
            viewModel.SelectedItemType == currentType &&
            viewModel.SelectedNode?.NodeId == selectedNodeId)
        {
            viewModel.ChangeSelectedItemType(type);
        }
    }
}
