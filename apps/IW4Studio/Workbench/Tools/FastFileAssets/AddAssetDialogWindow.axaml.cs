using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class AddAssetDialogWindow : Window
{
    private readonly Func<XAssetType, string, string?> _validateName;
    private readonly Action<XAssetType, string> _addAsset;
    private bool _showAllAssetTypes;

    public AddAssetDialogWindow()
        : this(
            [],
            preferredAssetType: null,
            (_, _) => "Name is required.",
            (_, _) => throw new InvalidOperationException(
                "Asset creation is unavailable."))
    {
    }

    internal AddAssetDialogWindow(
        IReadOnlyList<XAssetType> assetTypes,
        XAssetType? preferredAssetType,
        Func<XAssetType, string, string?> validateName,
        Action<XAssetType, string> addAsset)
    {
        ArgumentNullException.ThrowIfNull(assetTypes);
        _validateName = validateName
            ?? throw new ArgumentNullException(nameof(validateName));
        _addAsset = addAsset
            ?? throw new ArgumentNullException(nameof(addAsset));

        InitializeComponent();
        Icon = AppIcon.Create();
        XAssetType[] orderedAssetTypes = assetTypes
            .OrderBy(
                static assetType => assetType.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssetTypeAutoCompleteBox.ItemFilter = AssetTypeMatchesSearch;
        AssetTypeAutoCompleteBox.ItemsSource = orderedAssetTypes;
        XAssetType? selectedAssetType = null;
        if (preferredAssetType is { } preferred)
        {
            if (orderedAssetTypes.Contains(preferred))
                selectedAssetType = preferred;
        }
        if (selectedAssetType is null && orderedAssetTypes.Length > 0)
            selectedAssetType = orderedAssetTypes[0];

        AssetTypeAutoCompleteBox.SelectedItem = selectedAssetType;
        Opened += (_, _) => NameTextBox.Focus();
        RefreshValidation();
    }

    private void NameTextBox_TextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        RefreshValidation();

    private void AssetTypeAutoCompleteBox_TextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        _showAllAssetTypes = false;

    private void AssetTypeAutoCompleteBox_DropDownClosed(
        object? sender,
        EventArgs e) =>
        _showAllAssetTypes = false;

    private void AssetTypeAutoCompleteBox_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) => RefreshValidation();

    private void AssetTypeDropDownButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (AssetTypeAutoCompleteBox.IsDropDownOpen)
        {
            AssetTypeAutoCompleteBox.IsDropDownOpen = false;
        }
        else
        {
            _showAllAssetTypes = true;
            AssetTypeAutoCompleteBox.Focus();
            AssetTypeAutoCompleteBox.IsDropDownOpen = true;
        }

        e.Handled = true;
    }

    private void AddButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        string name = NameTextBox.Text ?? string.Empty;
        if (AssetTypeAutoCompleteBox.SelectedItem is not XAssetType assetType)
        {
            RefreshValidation();
            return;
        }
        string? validationMessage = Validate(assetType, name);
        if (validationMessage is not null)
        {
            RefreshValidation();
            return;
        }

        try
        {
            _addAsset(assetType, name);
            Close(true);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                NotSupportedException or
                OverflowException)
        {
            ValidationTextBlock.Text = exception.Message;
        }
    }

    private void CancelButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        Close(false);

    private void RefreshValidation()
    {
        if (ValidationTextBlock is null || AddButton is null)
            return;

        string name = NameTextBox.Text ?? string.Empty;
        XAssetType? assetType = AssetTypeAutoCompleteBox.SelectedItem is XAssetType selectedType
            ? selectedType
            : null;
        string? validationMessage = assetType is { } selectedAssetType
            ? Validate(selectedAssetType, name)
            : "Asset type is required.";
        ValidationTextBlock.Text = validationMessage ?? string.Empty;
        AddButton.IsEnabled =
            validationMessage is null &&
            assetType is not null;
    }

    private string? Validate(XAssetType assetType, string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "Name is required."
            : _validateName(assetType, name);

    private bool AssetTypeMatchesSearch(string? search, object? item) =>
        _showAllAssetTypes ||
        item is XAssetType assetType &&
        assetType.ToString().StartsWith(
            search ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
}
