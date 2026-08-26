using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class AddAssetDialogWindow : Window
{
    private readonly Func<XAssetType, string, string?> _validateName;
    private readonly Action<XAssetType, string> _addAsset;

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
        AssetTypeComboBox.ItemsSource = assetTypes;
        int preferredIndex = -1;
        if (preferredAssetType is { } preferred)
        {
            for (int index = 0; index < assetTypes.Count; index++)
            {
                if (assetTypes[index] != preferred)
                    continue;

                preferredIndex = index;
                break;
            }
        }
        AssetTypeComboBox.SelectedIndex = preferredIndex >= 0
            ? preferredIndex
            : assetTypes.Count == 0
                ? -1
                : 0;
        Opened += (_, _) => NameTextBox.Focus();
        RefreshValidation();
    }

    private void NameTextBox_TextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        RefreshValidation();

    private void AssetTypeComboBox_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e) => RefreshValidation();

    private void AddButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        string name = NameTextBox.Text ?? string.Empty;
        if (AssetTypeComboBox.SelectedItem is not XAssetType assetType)
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
        XAssetType? assetType = AssetTypeComboBox.SelectedItem is XAssetType selectedType
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
}
