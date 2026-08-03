using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class AddAssetDialogWindow : Window
{
    private readonly Func<string, string?> _validateName;
    private readonly Action<XAssetType, string> _addAsset;

    public AddAssetDialogWindow()
        : this(
            [],
            _ => "Name is required.",
            (_, _) => throw new InvalidOperationException(
                "Asset creation is unavailable."))
    {
    }

    internal AddAssetDialogWindow(
        IReadOnlyList<XAssetType> assetTypes,
        Func<string, string?> validateName,
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
        AssetTypeComboBox.SelectedIndex = assetTypes.Count == 0 ? -1 : 0;
        Opened += (_, _) => NameTextBox.Focus();
        RefreshValidation();
    }

    private void NameTextBox_TextChanged(
        object? sender,
        TextChangedEventArgs e) =>
        RefreshValidation();

    private void AddButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        string name = NameTextBox.Text ?? string.Empty;
        string? validationMessage = Validate(name);
        if (validationMessage is not null ||
            AssetTypeComboBox.SelectedItem is not XAssetType assetType)
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
        string? validationMessage = Validate(name);
        ValidationTextBlock.Text = validationMessage ?? string.Empty;
        AddButton.IsEnabled =
            validationMessage is null &&
            AssetTypeComboBox.SelectedItem is XAssetType;
    }

    private string? Validate(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "Name is required."
            : _validateName(name);
}
