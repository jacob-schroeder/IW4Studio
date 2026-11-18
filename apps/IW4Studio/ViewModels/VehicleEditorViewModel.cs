using Avalonia.Threading;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Render;
using IW4.Render.SceneBuilding;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class VehicleEditorViewModel : ObservableObject, IAssetEditorStagingState, IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly Action<XAssetType, string>? _openReference;
    private VehicleDraft _applied;
    private bool _linkAxles, _updatingAxles, _disposed;
    private string? _message;
    private string _previewModel = string.Empty, _previewStatus = string.Empty;
    private CancellationTokenSource? _previewRequest;
    private VehicleEditorField? _audioField;

    internal VehicleEditorViewModel(AssetEditorSession session, Action<XAssetType, string>? openReference)
    {
        _session = session;
        _openReference = openReference;
        _applied = session.OpenDraft<VehicleDraft>();
        var saved = session.ReadSavedDraft<VehicleDraft>();
        Fields = VehicleDraft.Fields.Select(field => new VehicleEditorField(this, field, _applied, saved)).ToArray();
        RefreshModels();
        string? suggested = SuggestPreview(Name, ModelNames);
        if (suggested is not null) PreviewModel = suggested;
        else PreviewStatus = "Choose an XModel to preview this vehicle.";
        _session.WorkspaceAssetsChanged += WorkspaceChanged;
    }

    public string Name => _session.Entry.OriginalName ?? _session.Entry.NormalizedName ?? "Vehicle";
    public bool IsEditable => _session.CanEdit;
    public IReadOnlyList<VehicleEditorField> Fields { get; }
    public bool HasUnappliedChanges => Fields.Any(item => item.IsChanged);
    public bool CanApply => IsEditable && HasUnappliedChanges && Fields.All(item => item.Error is null);
    public bool CanRevert => IsEditable && (HasUnappliedChanges || _session.HasUnsavedChanges);
    public string Status => Fields.FirstOrDefault(item => item.Error is not null) is { } invalid
        ? $"{invalid.Label} · {invalid.Error}" : _message ?? (!IsEditable ? "Read only" :
            HasUnappliedChanges ? "Unapplied changes" : _session.HasUnsavedChanges ? "Applied to fastfile" : "Up to date");
    public bool LinkAxles { get => _linkAxles; set => SetProperty(ref _linkAxles, value); }
    public IReadOnlyList<string> ModelNames { get; private set; } = [];
    public XModelRenderScene? PreviewScene { get; private set; }
    public SoundPreviewViewModel? AudioPreview { get; private set; }
    public string PreviewStatus { get => _previewStatus; private set => SetProperty(ref _previewStatus, value); }
    public string PreviewModel
    {
        get => _previewModel;
        set
        {
            if (SetProperty(ref _previewModel, value ?? string.Empty)) LoadPreview();
        }
    }

    internal void FieldEdited(VehicleEditorField field)
    {
        _message = null;
        if (LinkAxles && !_updatingAxles && field.Error is null &&
            field.Key.StartsWith("Phys.Suspension", StringComparison.Ordinal))
        {
            string? pair = field.Key.EndsWith("Front", StringComparison.Ordinal) ? field.Key[..^5] + "Rear" :
                field.Key.EndsWith("Rear", StringComparison.Ordinal) ? field.Key[..^4] + "Front" : null;
            if (Fields.FirstOrDefault(other => other.Key == pair) is { } other)
            {
                _updatingAxles = true;
                try { other.Value = field.Value; }
                finally { _updatingAxles = false; }
            }
        }
        if (ReferenceEquals(_audioField, field)) SelectAudio(field);
        NotifyDraft();
    }

    public void ApplyChanges()
    {
        if (!CanApply) return;
        try
        {
            var candidate = _applied.Clone();
            // Apply paired provider names first so an explicit independent name edit wins.
            foreach (var field in Fields.Where(item => item.IsChanged)
                .OrderByDescending(item => item.Definition.Kind == VehicleFieldKind.AssetReference))
            {
                if (field.Definition.Kind == VehicleFieldKind.AssetReference)
                    _session.AssignVehicleReference(candidate, field.Definition, field.Value);
                else if (field.TryParse(out var value))
                    candidate.SetValue(field.Definition, value);
            }
            var validation = _session.ValidateCandidate(candidate);
            if (validation.HasErrors)
            {
                foreach (var issue in validation.Issues.Where(issue => issue.Severity == AssetValidationSeverity.Error))
                    Fields.FirstOrDefault(item => item.Key == issue.FieldPath)?.SetError(issue.Message);
                _message = string.Join(" ", validation.Issues.Select(issue => issue.Message));
                NotifyDraft();
                return;
            }
            var applied = _session.ApplyAndRead<VehicleDraft>(current => current.ApplyEdits(_applied, candidate));
            Reset(applied);
        }
        catch (Exception ex) when (IsAssetError(ex))
        {
            _message = ex.Message;
            NotifyDraft();
        }
    }

    public void RevertChanges()
    {
        if (!CanRevert) return;
        if (!HasUnappliedChanges) _session.RevertDraft();
        Reset(_session.ReadDraft<VehicleDraft>());
    }

    private void Reset(VehicleDraft applied)
    {
        _applied = applied;
        _message = null;
        foreach (var field in Fields) field.Reset(applied);
        if (_audioField is not null) SelectAudio(_audioField);
        NotifyDraft();
    }

    internal void RefreshSavedValues()
    {
        var saved = _session.ReadSavedDraft<VehicleDraft>();
        foreach (var field in Fields) field.ReadSaved(saved);
        NotifyDraft();
    }

    private void NotifyDraft()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRevert));
    }

    public IReadOnlyList<string> ReferenceNames(VehicleEditorField field) =>
        field.ReferenceType is { } type ? _session.EditorReferenceNames(type) : [];

    public void OpenReference(VehicleEditorField field)
    {
        if (field.ReferenceType is { } type && !string.IsNullOrWhiteSpace(field.Value))
            _openReference?.Invoke(type, field.Value.TrimStart(','));
    }

    public void SelectAudio(VehicleEditorField? field)
    {
        _audioField = field;
        AudioPreview?.Dispose();
        AudioPreview = null;
        if (field?.ReferenceType == XAssetType.Sound && !string.IsNullOrWhiteSpace(field.Value) &&
            _session.ResolveEditorSound(field.Value, out var resolver, out var reason) is { } sound)
        {
            AudioPreview = new SoundPreviewViewModel(sound, resolver, reason);
        }
        OnPropertyChanged(nameof(AudioPreview));
    }

    private void RefreshModels()
    {
        ModelNames = _session.EditorReferenceNames(XAssetType.XModel);
        OnPropertyChanged(nameof(ModelNames));
    }

    private static string? SuggestPreview(string vehicleName, IReadOnlyList<string> names)
    {
        string key = string.Join('_', vehicleName.TrimStart(',').Split('_').Where(
            part => part is not ("mp" or "physics" or "player")));
        if (key.Length == 0) return null;
        return names.Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                (name.StartsWith("vehicle_", StringComparison.OrdinalIgnoreCase) &&
                 name.Contains(key, StringComparison.OrdinalIgnoreCase)))
            .Where(name => !name.Contains("_d_", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("destroyed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.Length).FirstOrDefault();
    }

    private void LoadPreview()
    {
        _previewRequest?.Cancel();
        PreviewScene = null;
        OnPropertyChanged(nameof(PreviewScene));
        if (string.IsNullOrWhiteSpace(PreviewModel))
        {
            PreviewStatus = "Choose an XModel to preview this vehicle.";
            return;
        }
        PreviewStatus = "Loading preview model…";
        var request = new CancellationTokenSource();
        _previewRequest = request;
        _ = LoadPreviewAsync(PreviewModel, request);
    }

    private async Task LoadPreviewAsync(string name, CancellationTokenSource request)
    {
        try
        {
            var token = request.Token;
            var scene = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var model = _session.ResolveEditorReference<XModelAsset>(XAssetType.XModel, name, out var providers);
                if (model is null || _session.Workspace.IsBlank) return null;
                var source = WorkspaceRenderAssetSource.Create(_session.Workspace, "vehicle preview");
                return new XModelSceneBuilder().Build(model, source,
                    new WorkspaceGfxImagePayloadResolver(_session.Workspace), providers);
            }, token);
            token.ThrowIfCancellationRequested();
            PreviewScene = scene?.DefaultLodIndex >= 0 ? scene : null;
            OnPropertyChanged(nameof(PreviewScene));
            PreviewStatus = PreviewScene is null ? $"No drawable geometry for {name} in this workspace." : string.Empty;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (IsAssetError(ex))
        {
            if (!request.IsCancellationRequested) PreviewStatus = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_previewRequest, request)) _previewRequest = null;
            request.Dispose();
        }
    }

    private void WorkspaceChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            RefreshModels();
            if (PreviewModel.Length > 0) LoadPreview();
            if (_audioField is not null) SelectAudio(_audioField);
            NotifyDraft();
        });

    private static bool IsAssetError(Exception ex) => ex is IOException or InvalidDataException or
        InvalidOperationException or ArgumentException or OverflowException or UnauthorizedAccessException;

    public void Dispose()
    {
        _disposed = true;
        _session.WorkspaceAssetsChanged -= WorkspaceChanged;
        _previewRequest?.Cancel();
        AudioPreview?.Dispose();
    }
}
