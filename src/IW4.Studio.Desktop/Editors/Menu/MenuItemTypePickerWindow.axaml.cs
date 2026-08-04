using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuItemTypePickerWindow : Window
{
    public MenuItemTypePickerWindow()
        : this(ItemDefType.Text)
    {
    }

    internal MenuItemTypePickerWindow(ItemDefType currentItemType)
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        DataContext = new MenuItemTypePickerViewModel(currentItemType);
    }

    private void CancelButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close((ItemDefType?)null);

    private void ConfirmButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MenuItemTypePickerViewModel
            {
                CanConfirm: true
            } viewModel)
        {
            return;
        }

        Close((ItemDefType?)viewModel.SelectedItemType);
    }
}
