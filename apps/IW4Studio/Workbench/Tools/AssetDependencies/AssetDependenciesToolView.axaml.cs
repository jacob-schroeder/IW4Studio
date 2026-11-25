using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

namespace IW4.Studio.Desktop.Workbench.Tools.AssetDependencies;

public sealed partial class AssetDependenciesToolView : UserControl
{
    private bool _exportingPackage;
    public AssetDependenciesToolView() => InitializeComponent();

    private void ScopeButton_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not AssetDependenciesToolViewModel viewModel)
            return;

        if (ReferenceEquals(sender, UsedByButton))
            viewModel.IsUsedByScope = true;
        else if (ReferenceEquals(sender, UsesButton))
            viewModel.IsUsesScope = true;
    }

    private async void ExportPackageButton_Click(object? sender, RoutedEventArgs args)
    {
        if (_exportingPackage || DataContext is not AssetDependenciesToolViewModel
            { CanExportPackage: true, Selection: { } asset } viewModel ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        args.Handled = true;
        _exportingPackage = true;
        try
        {
            await AssetPackageDesktopFlow.ExportAsync(
                owner, viewModel.EditingSession, [asset], viewModel.ReportPackageStatus);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            viewModel.ReportPackageStatus($"Package export failed. {exception.Message}");
        }
        finally
        {
            _exportingPackage = false;
        }
    }
}
