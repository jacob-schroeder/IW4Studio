using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Assets.D3dbsp;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed partial class ImportD3dbspDialogWindow : Window
{
    private readonly string _inputPath;
    private readonly Func<string, string, bool, int,
        Task<D3dbspWorkspaceImportResult>> _import;
    private bool _isImporting;

    public ImportD3dbspDialogWindow()
        : this(
            "design.d3dbsp",
            "maps/mp/design.d3dbsp",
            suggestedCapacity: null,
            (_, _, _, _) => throw new InvalidOperationException(
                "D3DBSP import is unavailable."))
    {
    }

    internal ImportD3dbspDialogWindow(
        string inputPath,
        string suggestedAssetName,
        int? suggestedCapacity,
        Func<string, string, bool, int,
            Task<D3dbspWorkspaceImportResult>> import)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(suggestedAssetName);
        _inputPath = inputPath;
        _import = import ?? throw new ArgumentNullException(nameof(import));

        InitializeComponent();
        Icon = AppIcon.Create();
        SourceTextBlock.Text = $"Source: {Path.GetFileName(inputPath)}";
        NameTextBox.Text = suggestedAssetName;
        CapacityTextBox.Text = suggestedCapacity is { } capacity
            ? $"0x{capacity:X8}"
            : string.Empty;
        Opened += (_, _) => NameTextBox.Focus();
        Closing += (_, args) => args.Cancel = _isImporting;
        RefreshValidation();
    }

    private void Input_TextChanged(object? sender, TextChangedEventArgs e) =>
        RefreshValidation();

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isImporting || ValidateInputs() is not null ||
            !TryParseCapacity(CapacityTextBox.Text, out int capacity))
        {
            RefreshValidation();
            return;
        }

        _isImporting = true;
        SetInputEnabled(false);
        ValidationTextBlock.Text = string.Empty;
        ImportButton.Content = "Importing...";
        try
        {
            await _import(
                _inputPath,
                NameTextBox.Text!,
                ForceFullbrightCheckBox.IsChecked == true,
                capacity);
            _isImporting = false;
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or InvalidDataException or
                   InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            ValidationTextBlock.Text = exception.Message;
        }
        finally
        {
            _isImporting = false;
            SetInputEnabled(true);
            ImportButton.Content = "Import";
            RefreshImportButton();
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) =>
        Close(false);

    private void RefreshValidation()
    {
        if (ValidationTextBlock is null)
            return;

        ValidationTextBlock.Text = ValidateInputs() ?? string.Empty;
        RefreshImportButton();
    }

    private string? ValidateInputs()
    {
        string name = NameTextBox?.Text ?? string.Empty;
        if (!D3dbspAssetTypeFacts.IsOwnedD3dbspName(name))
        {
            return "Name must be an owned wire name ending in .d3dbsp, without a comma prefix.";
        }
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            return "Name cannot contain leading or trailing whitespace.";
        if (name.Any(character => character > byte.MaxValue))
            return "Name must use Latin-1 characters.";
        if (name.Replace('\\', '/').Split('/').Any(segment => segment.Length == 0))
            return "Name cannot contain empty path segments.";
        if (!TryParseCapacity(CapacityTextBox?.Text, out _))
            return "Enter a positive capacity in decimal or 0x-prefixed hexadecimal form.";

        return null;
    }

    private void RefreshImportButton()
    {
        if (ImportButton is not null)
            ImportButton.IsEnabled = !_isImporting && ValidateInputs() is null;
    }

    private void SetInputEnabled(bool enabled)
    {
        NameTextBox.IsEnabled = enabled;
        CapacityTextBox.IsEnabled = enabled;
        ForceFullbrightCheckBox.IsEnabled = enabled;
        CancelButton.IsEnabled = enabled;
    }

    private static bool TryParseCapacity(string? text, out int capacity)
    {
        capacity = 0;
        string value = text?.Trim() ?? string.Empty;
        bool parsed = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(
                value.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out capacity)
            : int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out capacity);
        return parsed && capacity > 0;
    }
}
