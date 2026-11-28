using IW4.Runtime.Diagnostics;
using System.Reflection;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class WelcomeViewModel : ObservableObject
{
    private string _selectedPath = string.Empty;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isPickingFile;
    private bool _hasError;

    public string DesktopVersionLabel => AssemblyConst.AssemblyVersion;

    public IReadOnlyList<RecentFastFileItem> RecentFiles { get; private set; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string SelectedPath
    {
        get => _selectedPath;
        private set
        {
            if (!SetProperty(ref _selectedPath, value))
                return;

            OnPropertyChanged(nameof(SelectedFileName));
            OnPropertyChanged(nameof(SelectedDirectory));
            OnPropertyChanged(nameof(IsChoosingMode));
            OnPropertyChanged(nameof(CanInteract));
        }
    }

    public string SelectedFileName => string.IsNullOrWhiteSpace(SelectedPath)
        ? "No fastfile selected"
        : Path.GetFileName(SelectedPath);

    public string SelectedDirectory => string.IsNullOrWhiteSpace(SelectedPath)
        ? "Select a fastfile"
        : Path.GetDirectoryName(SelectedPath) ?? SelectedPath;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(CanInteract));
        }
    }

    public bool IsChoosingMode => !string.IsNullOrEmpty(SelectedPath);

    public bool CanInteract => !IsBusy && !_isPickingFile && !IsChoosingMode;

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool TryBeginPicker()
    {
        if (!CanInteract)
            return false;

        _isPickingFile = true;
        OnPropertyChanged(nameof(CanInteract));
        ClearError();
        return true;
    }

    public void EndPicker()
    {
        _isPickingFile = false;
        OnPropertyChanged(nameof(CanInteract));
    }

    public bool TryChooseMode(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!CanInteract)
            return false;

        ClearError();
        SelectedPath = path;
        return true;
    }

    public void EndChoosingMode() => SelectedPath = string.Empty;

    public void BeginLoad(bool withDependencies)
    {
        ClearError();
        IsBusy = true;
        StatusText = withDependencies
            ? "Preparing the engine dependency plan..."
            : "Preparing the selected fastfile...";
    }

    public void BeginCreate()
    {
        ClearError();
        IsBusy = true;
        StatusText = "Creating and validating the new fastfile...";
    }

    public void ReportCreateProgress(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        StatusText = message;
    }

    public void ReportProgress(XAssetLoadProgress progress)
    {
        int completed = Math.Clamp(progress.AssetNumber, 0, progress.AssetCount);
        StatusText = $"{Path.GetFileName(progress.SourceName)}  ·  {progress.AssetType}  ·  " +
                     $"{completed:N0} of {progress.AssetCount:N0}";
    }

    public void FailLoad(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        IsBusy = false;
        StatusText = "The fastfile could not be opened.";
        ErrorMessage = exception.Message;
        HasError = true;
    }

    public void FailCreate(Exception exception, bool wasPublished)
    {
        ArgumentNullException.ThrowIfNull(exception);
        IsBusy = false;
        StatusText = wasPublished
            ? "The new fastfile was written but could not be opened."
            : "The fastfile could not be created.";
        ErrorMessage = exception.Message;
        HasError = true;
    }

    public void SetRecentFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        RecentFiles = paths
            .Select(path => new RecentFastFileItem(path))
            .ToArray();
        OnPropertyChanged(nameof(RecentFiles));
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }
}

public sealed class RecentFastFileItem
{
    public RecentFastFileItem(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? Path;
}
