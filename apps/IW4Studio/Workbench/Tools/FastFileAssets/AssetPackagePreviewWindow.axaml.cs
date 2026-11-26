using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

public sealed class AssetPackageRowViewModel(AssetPackageRow row, bool isImport) : INotifyPropertyChanged
{
    private bool _replace;

    public event PropertyChangedEventHandler? PropertyChanged;
    public AssetPackageRow Row { get; } = row;
    public string AssetText => $"{Row.Type}: {Row.Name}";
    public string StatusText => Row.Status.ToString();
    public string Detail => Row.Detail;
    public string UsedByText => $"Used by: {string.Join(", ", Row.UsedBy)}";
    public bool HasUsedBy => Row.UsedBy.Count > 0;
    public bool CanChooseReplace => isImport &&
        Row.Status == AssetPackageStatus.Conflict && Row.CanReplace;
    public bool MustKeepExisting => isImport &&
        Row.Status == AssetPackageStatus.Conflict && !Row.CanReplace;
    public bool Replace
    {
        get => _replace;
        set
        {
            if (_replace == value) return;
            _replace = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Replace)));
        }
    }
}

public sealed partial class AssetPackagePreviewWindow : Window
{
    private readonly bool _isImport;
    private readonly Func<IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, Task>? _apply;
    private readonly Func<Task<IReadOnlyList<AssetPackageRow>>>? _loadDependencies;
    private bool _busy;
    private bool _loadedDependencies;

    public AssetPackagePreviewWindow() : this([], false, null) { }

    internal AssetPackagePreviewWindow(
        IReadOnlyList<AssetPackageRow> rows,
        bool isImport,
        Func<IReadOnlyDictionary<AssetKey, AssetPackageConflictChoice>, Task>? apply,
        Func<Task<IReadOnlyList<AssetPackageRow>>>? loadDependencies = null,
        string? menuToRegister = null)
    {
        _isImport = isImport;
        _apply = apply;
        _loadDependencies = loadDependencies;
        InitializeComponent();
        Icon = AppIcon.Create();
        DataContext = this;
        HeadingText.Text = menuToRegister is not null ? $"Import menu “{menuToRegister}”"
            : isImport ? "Import package" : "Export asset package";
        Title = HeadingText.Text;
        ConfirmButton.Content = menuToRegister is not null ? "Import and register"
            : isImport ? "Import Package" : "Save Package…";
        UpdateRows(rows, showBlockedInitially: true);
        Closing += (_, args) => args.Cancel = _busy;
    }

    public IReadOnlyList<AssetPackageRowViewModel> Rows { get; private set; } = [];
    public IReadOnlyList<AssetPackageRowViewModel> BlockedRows { get; private set; } = [];

    private void UpdateRows(IReadOnlyList<AssetPackageRow> rows, bool showBlockedInitially = false)
    {
        HashSet<AssetKey> selectedReplacements = Rows
            .Where(row => row.CanChooseReplace && row.Replace && row.Row.Key.HasValue)
            .Select(row => row.Row.Key.GetValueOrDefault()).ToHashSet();
        Rows = rows.Select(row => new AssetPackageRowViewModel(row, _isImport)
        {
            Replace = row.Key is { } key && selectedReplacements.Contains(key)
        }).ToArray();
        BlockedRows = Rows.Where(row => row.Row.Status == AssetPackageStatus.Blocked)
            .DistinctBy(row => (row.Row.Type, row.Row.Name, row.Row.Detail)).ToArray();
        int conflictCount = Rows.Count(row => row.Row.Status == AssetPackageStatus.Conflict);
        SummaryText.Text = _isImport && conflictCount > 0
            ? $"{conflictCount:N0} {(conflictCount == 1 ? "conflict" : "conflicts")}. Existing assets are kept unless you select Replace."
            : _isImport ? "Review the assets to add or reuse." : "Required dependencies must be available in the destination.";
        bool blocked = BlockedRows.Count > 0;
        BlockedSummaryText.IsVisible = blocked;
        BlockedSummaryText.Text = $"{BlockedRows.Count:N0} {(BlockedRows.Count == 1 ? "asset blocks" : "assets block")} " +
            $"{(_isImport ? "import" : "export")}. See each blocked row for the reason.";
        ShowBlockedOnlyCheckBox.IsVisible = blocked;
        ShowBlockedOnlyCheckBox.IsChecked = blocked &&
            (showBlockedInitially || ShowBlockedOnlyCheckBox.IsChecked == true);
        AssetRowsList.ItemsSource = ShowBlockedOnlyCheckBox.IsChecked == true ? BlockedRows : Rows;
        bool hasReplaceableConflicts = _isImport && Rows.Any(row => row.CanChooseReplace);
        ReplaceAllButton.IsVisible = hasReplaceableConflicts;
        KeepAllButton.IsVisible = hasReplaceableConflicts;
        bool missingDependencies = Rows.Any(row => row.Row.IsMissingDependency);
        LoadDependenciesButton.IsVisible = _isImport && _loadDependencies is not null &&
            missingDependencies && !_loadedDependencies;
        ErrorText.Text = LoadDependenciesButton.IsVisible
            ? "Load the destination’s dependencies to check the missing assets."
            : blocked ? "Review the blocked rows above for the next step." : string.Empty;
        if (_loadedDependencies && !blocked)
            SummaryText.Text = "All required dependencies are available. " + SummaryText.Text;
        SetBusy(_busy);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConfirmButton.IsEnabled = !busy && BlockedRows.Count == 0;
        CancelButton.IsEnabled = !busy;
        LoadDependenciesButton.IsEnabled = !busy;
        ReplaceAllButton.IsEnabled = !busy;
        KeepAllButton.IsEnabled = !busy;
    }

    private void ShowBlockedOnlyCheckBox_Changed(object? sender, RoutedEventArgs e) =>
        AssetRowsList.ItemsSource = ShowBlockedOnlyCheckBox.IsChecked == true ? BlockedRows : Rows;

    private void ReplaceAllButton_Click(object? sender, RoutedEventArgs e) => SetAllReplace(true);

    private void KeepAllButton_Click(object? sender, RoutedEventArgs e) => SetAllReplace(false);

    private void SetAllReplace(bool replace)
    {
        if (_busy) return;
        foreach (AssetPackageRowViewModel row in Rows)
            if (row.CanChooseReplace)
                row.Replace = replace;
    }

    private async void LoadDependenciesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || _loadDependencies is null || !LoadDependenciesButton.IsVisible)
            return;

        SetBusy(true);
        ErrorText.Text = "Loading dependencies…";
        try
        {
            IReadOnlyList<AssetPackageRow> rows = await _loadDependencies();
            _loadedDependencies = true;
            UpdateRows(rows);
        }
        catch (OperationCanceledException)
        {
            ErrorText.Text = "Dependency loading was canceled. You can try again.";
        }
        catch (FileNotFoundException exception) when (exception.FileName is { } path)
        {
            ErrorText.Text = $"Could not find {path}. Place the fastfile there, then choose Load dependencies again.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                   InvalidDataException or InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

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

        SetBusy(true);
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
            SetBusy(false);
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
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
