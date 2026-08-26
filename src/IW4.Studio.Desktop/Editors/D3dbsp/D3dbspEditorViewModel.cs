using IW4.Assets.Assets.GfxMap;
using IW4.Assets.D3dbsp;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.D3dbsp;

public sealed record D3dbspGeneratedAssetTypeItem(string Name, string Role);

/// <summary>Desktop presentation and group import/export operations for one D3DBSP.</summary>
public sealed class D3dbspEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics
{
    private static readonly IReadOnlyList<D3dbspGeneratedAssetTypeItem>
        GeneratedAssetTypesValue = Array.AsReadOnly<D3dbspGeneratedAssetTypeItem>(
            D3dbspAssetTypeFacts.MultiplayerTypes
                .Select(assetType => new D3dbspGeneratedAssetTypeItem(
                    assetType.ToString(),
                    DescribeAssetType(assetType)))
                .ToArray());

    private readonly AssetEditorSession _session;
    private readonly string _assetName;
    private readonly bool _supportsFileRoundTrip;
    private bool _forceFullbright;
    private bool _isBusy;
    private string _statusMessage;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];

    public D3dbspEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (!D3dbspAssetTypeFacts.IsMultiplayerType(session.Entry.AssetType) ||
            !D3dbspAssetTypeFacts.IsD3dbspName(session.Entry.OriginalName))
        {
            throw new InvalidDataException(
                "The D3DBSP view model can host only a multiplayer .d3dbsp asset group.");
        }

        _assetName = session.CaptureD3dbspGroup().AssetName;
        _supportsFileRoundTrip =
            D3dbspAssetTypeFacts.IsOwnedD3dbspName(_assetName);
        _statusMessage = _supportsFileRoundTrip
            ? "Import creates or replaces synchronized target rows; export uses the current resolved group."
            : "This grouped entry contains .d3dbsp in its name, but file import and export require an owned wire name ending in .d3dbsp.";
    }

    public string AssetName => _assetName;

    public string SelectedAssetType => _session.Entry.AssetType.ToString();

    public bool IsEditable => _session.CanEdit;

    public bool CanImport => !IsBusy && _supportsFileRoundTrip;

    public bool CanExport => !IsBusy && _supportsFileRoundTrip;

    public bool ForceFullbright
    {
        get => _forceFullbright;
        set => SetProperty(ref _forceFullbright, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<D3dbspGeneratedAssetTypeItem> GeneratedAssetTypes =>
        GeneratedAssetTypesValue;

    public string PropertySectionName => "D3DBSP group";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Wire name", AssetName),
        new("Selected asset type", SelectedAssetType),
        new("Group members", GeneratedAssetTypes.Count.ToString()),
        new("D3DBSP file I/O", _supportsFileRoundTrip ? "Available" : "Unavailable for this wire name"),
        new("Selected entry access", IsEditable ? "Editable target row" : "Resolved read-only entry")
    ];

    public IReadOnlyList<AssetValidationIssue> Diagnostics => _diagnostics;

    public async Task<bool> ImportAsync(string path, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (!CanImport)
            return false;

        IsBusy = true;
        SetDiagnostics([]);
        StatusMessage = "Compiling the D3DBSP into its synchronized asset group...";
        try
        {
            D3dbspWorkspaceAssetGroup current = _session.CaptureD3dbspGroup();
            int fragmentProgramUploadCapacity = current.Assets
                .OfType<GfxWorldAsset>()
                .Single()
                .FragmentProgramUploadCapacity;
            bool forceFullbright = ForceFullbright;
            D3dbspWorkspaceImportResult imported =
                await _session.ImportD3dbspAsync(
                    path,
                    forceFullbright,
                    fragmentProgramUploadCapacity);
            string discardedLighting = imported.DiscardedLightByteCount == 0
                ? string.Empty
                : $" Discarded {imported.DiscardedLightByteCount:N0} compiled light bytes via the lossy fullbright workaround.";
            StatusMessage =
                $"Imported {sourceName} as {imported.AssetName}: " +
                $"replaced {imported.ReplacedRowCount} and added {imported.AddedRowCount} target rows." +
                discardedLighting;
            return true;
        }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException or InvalidDataException or
                   InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            ReportFailure($"D3DBSP import failed: {exception.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<D3dbspFile?> CreateExportAsync()
    {
        if (!CanExport)
            return null;

        IsBusy = true;
        bool handedOffForWrite = false;
        SetDiagnostics([]);
        StatusMessage = "Reconstructing a compiled D3DBSP from the synchronized asset group...";
        try
        {
            D3dbspFile file = await Task.Run(
                _session.CreateD3dbspFile);
            StatusMessage = "The synchronized asset group is ready to export.";
            handedOffForWrite = true;
            return file;
        }
        catch (Exception exception) when (exception is InvalidDataException or
                   InvalidOperationException or NotSupportedException or
                   ArgumentException or OverflowException)
        {
            ReportFailure($"D3DBSP export failed: {exception.Message}");
            return null;
        }
        finally
        {
            if (!handedOffForWrite)
                IsBusy = false;
        }
    }

    public void ReportExportSuccess(string destinationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationName);
        IsBusy = false;
        SetDiagnostics([]);
        StatusMessage = $"Exported the synchronized D3DBSP group to {destinationName}.";
    }

    public void ReportExportCancelled()
    {
        IsBusy = false;
        SetDiagnostics([]);
        StatusMessage = "D3DBSP export cancelled; no file was written.";
    }

    public void ReportFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        IsBusy = false;
        StatusMessage = message;
        SetDiagnostics(
        [
            new AssetValidationIssue(
                "d3dbsp",
                message,
                AssetValidationSeverity.Error)
        ]);
    }

    private void SetDiagnostics(IReadOnlyList<AssetValidationIssue> diagnostics)
    {
        _diagnostics = diagnostics;
        OnPropertyChanged(nameof(Diagnostics));
    }

    private static string DescribeAssetType(XAssetType assetType) => assetType switch
    {
        XAssetType.ColMapMp => "Collision geometry and spatial queries",
        XAssetType.ComMap => "Primary lights and shared world data",
        XAssetType.GameMapMp => "Multiplayer game-world data",
        XAssetType.MapEnts => "Entity text and map stages",
        XAssetType.FxMap => "Map effects and glass-world data",
        XAssetType.GfxMap => "Renderable world geometry and lighting",
        _ => throw new ArgumentOutOfRangeException(nameof(assetType))
    };
}
