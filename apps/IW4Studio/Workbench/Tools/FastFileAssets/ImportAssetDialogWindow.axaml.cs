using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class ImportAssetDialogWindow : Window
{
    private readonly FastFileAssetsNavigatorViewModel? _viewModel;
    private IStorageFile? _source;
    private string? _suggestedName;
    private TargetZoneRowIdentity? _displayedReplacement;
    private bool _isBusy;

    public ImportAssetDialogWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        Closing += (_, args) => args.Cancel = _isBusy;
    }

    internal ImportAssetDialogWindow(
        FastFileAssetsNavigatorViewModel viewModel,
        XAssetType? preferredType,
        string? preferredName)
        : this()
    {
        _viewModel = viewModel;
        AssetTypeBox.ItemsSource = AssetSourceTransfer.SupportedAssetTypes;
        AssetTypeBox.SelectedItem = preferredType ?? AssetSourceTransfer.SupportedAssetTypes[0];
        NameTextBox.Text = preferredName;
        RefreshValidation();
    }

    private void AssetTypeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
            return;

        _source = null;
        _suggestedName = null;
        SourceFileText.Text = "Choose a source file";
        NameTextBox.Text = string.Empty;
        RefreshValidation();
    }

    private void NameTextBox_TextChanged(object? sender, TextChangedEventArgs e) => RefreshValidation();

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || AssetTypeBox.SelectedItem is not XAssetType type)
            return;

        SetBusy(true);
        string? failure = null;
        try
        {
            IStorageFile? file = await AssetSourceFilePicker.PickSourceAsync(StorageProvider, type);
            if (file is null)
                return;

            _source = file;
            SourceFileText.Text = file.Name;
            string name = AssetSourceTransfer.SuggestImportAssetName(type, file.Name);
            if (string.IsNullOrEmpty(NameTextBox.Text) || NameTextBox.Text == _suggestedName)
                NameTextBox.Text = name;
            _suggestedName = name;
        }
        catch (OperationCanceledException)
        {
            // The source picker was dismissed.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            failure = exception.Message;
        }
        finally
        {
            SetBusy(false);
            if (failure is not null)
                ShowValidation(failure);
        }
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _viewModel is null || _source is not { } file ||
            AssetTypeBox.SelectedItem is not XAssetType type)
            return;

        string name = NameTextBox.Text ?? string.Empty;
        string? error = _viewModel.ValidateAssetSourceImportName(
            type, name, out WorkspaceAssetCatalogEntry? replacement);
        if (error is not null || replacement?.TargetRowIdentity != _displayedReplacement)
        {
            RefreshValidation();
            return;
        }

        SetBusy(true);
        ImportButton.Content = "Importing…";
        string? failure = null;
        bool succeeded = false;
        try
        {
            await using Stream source = await file.OpenReadAsync();
            await _viewModel.ImportAssetSourceAsync(
                source,
                type,
                replacement?.OriginalName ?? name,
                replacement?.TargetRowIdentity,
                file.TryGetLocalPath());
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            failure = "The import was canceled.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or NotSupportedException or
            ArgumentException or OverflowException or JsonException)
        {
            failure = exception.Message;
        }
        finally
        {
            SetBusy(false);
            if (failure is not null)
                ShowValidation(failure);
        }

        if (succeeded)
            Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void RefreshValidation()
    {
        if (_viewModel is null || AssetTypeBox.SelectedItem is not XAssetType type || _isBusy)
            return;

        string name = NameTextBox.Text ?? string.Empty;
        string? error = _viewModel.ValidateAssetSourceImportName(
            type, name, out WorkspaceAssetCatalogEntry? replacement);
        _displayedReplacement = replacement?.TargetRowIdentity;
        ImportButton.Content = replacement is null ? "Import" : "Replace Existing";
        ImportButton.IsDefault = replacement is null;
        ImportButton.IsEnabled = _source is not null && error is null;
        ReplacementText.IsVisible = replacement is not null;
        ReplacementText.Text = replacement is null ? string.Empty :
            $"This will replace {replacement.OriginalName} in the active fastfile.";
        ShowValidation(name.Length == 0 ? null : error);
    }

    private void ShowValidation(string? message)
    {
        ValidationText.Text = message;
        ValidationText.IsVisible = message is not null;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        AssetTypeBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        NameTextBox.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        ImportButton.IsEnabled = false;
        if (busy)
            ShowValidation(null);
        else
            RefreshValidation();
    }
}
