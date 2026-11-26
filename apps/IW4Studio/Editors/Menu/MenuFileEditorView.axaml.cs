using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Menu.Behavior;
using IW4.Studio.Desktop.ViewModels.Menu;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuFileEditorView : UserControl
{
    private AssetReferencePickerService? _assetReferencePicker;
    private FastFileEditingSession? _editingSession;
    private Func<AssetPackageImportPreview,
        IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int>? _applyPackageImport;
    private bool _packageOperationInProgress;

    public MenuFileEditorView() => InitializeComponent();

    private void ApplyChangesButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        _ = (DataContext as MenuFileEditorViewModel)?
            .Designer.ApplyStagedInput();

    private void RegistrationRow_PointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left &&
            DataContext is MenuFileEditorViewModel viewModel)
        {
            viewModel.RequestPropertiesReveal();
        }
    }

    internal MenuFileEditorView(
        MenuFileEditorViewModel viewModel,
        AssetReferencePickerService assetReferencePicker,
        FastFileEditingSession editingSession,
        Func<AssetPackageImportPreview,
            IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, int> applyPackageImport)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(assetReferencePicker);
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(applyPackageImport);
        _assetReferencePicker = assetReferencePicker;
        _editingSession = editingSession;
        _applyPackageImport = applyPackageImport;
        DataContext = viewModel;
        if (viewModel.MenuCount == 1 && viewModel.Designer.HasDocument)
            NavigationTabs.SelectedItem = ItemOutlineTab;
        viewModel.AssetReferenceSelectionRequested +=
            async (_, args) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await assetReferencePicker.ShowAsync(owner, args.Row);
            };
        viewModel.ItemBehaviorEditRequested +=
            async (_, args) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    await MenuItemBehaviorBuilderService.ShowAsync(
                        owner,
                        args);
                }
            };
        viewModel.MenuBehaviorEditRequested +=
            async (_, args) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    await MenuItemBehaviorBuilderService.ShowAsync(
                        owner,
                        args);
                }
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

    private async void DuplicateRegistration_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MenuFileEditorViewModel
            {
                SelectedRegistration:
                {
                    IsEditableDefinition: true
                } selected
            } viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        string? name = await MenuDuplicateDefinitionDialog.ShowAsync(
            owner,
            selected.Name,
            viewModel.ValidateNewMenuName);
        if (!string.IsNullOrWhiteSpace(name))
            viewModel.DuplicateSelectedRegistration(name);
    }

    private async void ExportSelectedMenu_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_packageOperationInProgress ||
            DataContext is not MenuFileEditorViewModel { CanExportSelectedMenu: true } viewModel ||
            _editingSession is null ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        _packageOperationInProgress = true;
        try
        {
            var menu = viewModel.CaptureSelectedMenuForPackage();
            await AssetPackageDesktopFlow.ExportMenuAsync(
                owner, _editingSession, menu, viewModel.ReportPackageOperation);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException or KeyNotFoundException)
        {
            viewModel.ReportPackageOperation($"Menu package export failed. {exception.Message}");
        }
        finally
        {
            _packageOperationInProgress = false;
        }
    }

    private async void ImportAndRegister_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_packageOperationInProgress ||
            DataContext is not MenuFileEditorViewModel { CanAddRegistration: true } viewModel ||
            _editingSession is null ||
            _applyPackageImport is null ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        e.Handled = true;
        _packageOperationInProgress = true;
        string? importedName = null;
        try
        {
            importedName = await AssetPackageDesktopFlow.ImportAsync(
                owner, _editingSession, _applyPackageImport,
                viewModel.ReportPackageOperation, requireSingleMenu: true);
            if (importedName is not null)
                viewModel.RegisterImportedMenu(importedName);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException or KeyNotFoundException)
        {
            viewModel.ReportPackageOperation(importedName is null
                ? $"Menu package import failed. {exception.Message}"
                : $"Imported Menu '{importedName}', but registration failed. Use Add menu to register it later. {exception.Message}");
        }
        finally
        {
            _packageOperationInProgress = false;
        }
    }
}
