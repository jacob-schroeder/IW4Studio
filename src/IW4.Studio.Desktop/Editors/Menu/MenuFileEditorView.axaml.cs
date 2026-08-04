using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuFileEditorView : UserControl
{
    private AssetReferencePickerService? _assetReferencePicker;

    public MenuFileEditorView() => AvaloniaXamlLoader.Load(this);

    private void ApplyChangesButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        _ = (DataContext as MenuFileEditorViewModel)?
            .Designer.ApplyStagedInput();

    internal MenuFileEditorView(
        MenuFileEditorViewModel viewModel,
        AssetReferencePickerService assetReferencePicker)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(assetReferencePicker);
        _assetReferencePicker = assetReferencePicker;
        DataContext = viewModel;
        viewModel.AssetReferenceSelectionRequested +=
            async (_, args) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await assetReferencePicker.ShowAsync(owner, args.Row);
            };
    }

    private async void AddExistingMenu_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MenuFileEditorViewModel viewModel ||
            _assetReferencePicker is null ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        string? name = await _assetReferencePicker.SelectNameAsync(
            owner,
            XAssetType.Menu);
        if (!string.IsNullOrWhiteSpace(name))
            viewModel.AddExistingMenu(name);
    }

    private async void RetargetRegistration_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MenuFileEditorViewModel
            {
                SelectedRegistration: { } selected
            } viewModel ||
            _assetReferencePicker is null ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        string? name = await _assetReferencePicker.SelectNameAsync(
            owner,
            XAssetType.Menu,
            selected.MenuName);
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (viewModel.WouldDiscardInlineDefinitionOnRetarget(name) &&
            !await MenuInlineDefinitionDiscardDialog.ShowAsync(
                owner,
                selected.Name,
                name))
        {
            return;
        }

        viewModel.RetargetSelectedRegistration(name);
    }
}
