using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed class AssetPackageRowViewModel(AssetPackageRow row, bool isImport)
{
    public AssetPackageRow Row { get; } = row;
    public string AssetText => $"{Row.Type}: {Row.Name}";
    public string StatusText => Row.Status switch
    {
        AssetPackageStatus.Blocked => "Attention",
        AssetPackageStatus.Conflict when !isImport => "Attention",
        _ => Row.Status.ToString()
    };
    public string Detail => Row.Detail;
    public string UsedByText => $"Used by: {string.Join(", ", Row.UsedBy)}";
    public bool HasUsedBy => Row.UsedBy.Count > 0;
    public bool CanChooseReplace => isImport &&
        Row.Status == AssetPackageStatus.Conflict && Row.CanReplace;
    public bool MustKeepExisting => isImport &&
        Row.Status == AssetPackageStatus.Conflict && !Row.CanReplace;
    public bool Replace { get; set; }
}

public sealed partial class AssetPackagePreviewWindow : Window
{
    private readonly bool _isImport;
    private readonly Func<IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, Task>? _apply;
    private bool _busy;

    public AssetPackagePreviewWindow() : this([], false, null) { }

    internal AssetPackagePreviewWindow(
        IReadOnlyList<AssetPackageRow> rows,
        bool isImport,
        Func<IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, Task>? apply)
    {
        _isImport = isImport;
        _apply = apply;
        Rows = rows.Select(row => new AssetPackageRowViewModel(row, isImport)).ToArray();
        InitializeComponent();
        Icon = AppIcon.Create();
        DataContext = this;
        HeadingText.Text = isImport ? "Import package" : "Export asset package";
        SummaryText.Text = isImport
            ? "Review the changes. Conflicts keep the existing asset unless you select Replace."
            : "The package includes these assets and lists dependencies that must exist in the target workspace.";
        ConfirmButton.Content = isImport ? "Import Package" : "Save Package…";
        bool blocked = Rows.Any(row => row.Row.Status == AssetPackageStatus.Blocked);
        ConfirmButton.IsEnabled = !blocked;
        if (blocked)
            ErrorText.Text = "Resolve blocked assets before continuing.";
        Closing += (_, args) => args.Cancel = _busy;
    }

    public IReadOnlyList<AssetPackageRowViewModel> Rows { get; }

    private async void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || Rows.Any(row => row.Row.Status == AssetPackageStatus.Blocked))
            return;

        if (!_isImport)
        {
            Close(true);
            return;
        }

        if (_apply is null)
            return;

        _busy = true;
        ConfirmButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            var choices = Rows
                .Where(row => row.Row.Status == AssetPackageStatus.Conflict)
                .ToDictionary(
                    row => row.Row.Key!.Value,
                    row => row.CanChooseReplace && row.Replace
                        ? AssetPackageConflictChoice.Replace
                        : AssetPackageConflictChoice.KeepExisting);
            await _apply(choices);
            _busy = false;
            Close(true);
        }
        catch (OperationCanceledException)
        {
            ErrorText.Text = "The package import was canceled.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            _busy = false;
            ConfirmButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
