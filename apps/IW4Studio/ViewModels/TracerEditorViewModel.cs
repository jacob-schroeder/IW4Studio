using System.ComponentModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Tracer;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class TracerEditorViewModel : ObservableObject, IAssetEditorStagingState, IAssetEditorDiagnostics
{
    private readonly AssetEditorSession _session;
    private TracerDraft _original;
    private TracerDraft _working;
    private string? _operationError;

    public TracerEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _original = session.OpenDraft<TracerDraft>();
        _working = _original.Clone();
        CreateInputs();
    }

    public string Name => _original.Name ?? _session.Entry.OriginalName ?? "Tracer";
    public bool IsEditable => _session.CanEdit;
    public string? MaterialName => _working.Material?.SerializedAssetName?.TrimStart(',');
    public string MaterialDisplay => MaterialName ??
        (_working.MaterialPointer.Raw == 0 ? "None" : "Unresolved material");
    public bool CanClearMaterial => IsEditable && (_working.Material is not null || _working.MaterialPointer.Raw != 0);
    public IReadOnlyList<InspectorPropertyRowViewModel> Parameters { get; private set; } = [];
    public IReadOnlyList<InspectorColorPropertyRowViewModel> Colors { get; private set; } = [];
    public TracerDefAsset Preview => _working.ToAsset();
    public string? PreviewHint =>
        _working.BeamLength <= 0 || _working.BeamWidth <= 0 ? "Set a positive beam length and width to see the preview." :
        !_working.Colors.Any(color => color.Alpha > 0) ? "Raise a color sample’s alpha to see the preview." : null;

    public IReadOnlyList<AssetValidationIssue> Diagnostics { get; private set; } = [];
    public string? ErrorMessage => _operationError ?? Diagnostics.FirstOrDefault()?.Message;
    public bool HasUnappliedChanges => IsEditable && (!_working.Matches(_original) ||
        Parameters.Concat(Colors).Any(row => row.HasValidationError && row is IInspectorStagedPropertyRow { HasStagedValue: true }));
    public bool CanApply => HasUnappliedChanges && Diagnostics.Count == 0;
    public string Status => !IsEditable ? "Read only" : HasUnappliedChanges ? "Unapplied changes" : "Up to date";

    public void SelectMaterial(string? name)
    {
        if (!IsEditable) return;
        MaterialAsset? material = null;
        if (name is not null && !_session.TryResolveTracerMaterial(name, out material))
        {
            ShowError($"Material '{name}' is no longer available in this workspace.");
            return;
        }
        _working.SetMaterial(material);
        Refresh();
    }

    public void Apply()
    {
        if (!CanApply) return;
        try
        {
            var current = _session.ApplyAndRead<TracerDraft>(draft => draft.ApplyEdits(_original, _working),
                out bool changed);
            if (!changed)
            {
                // A concurrent edit may have changed the merged candidate's validity.
                var merged = current.Clone();
                merged.ApplyEdits(_original, _working);
                if (_session.ValidateCandidate(merged).Issues.FirstOrDefault() is { } issue)
                {
                    ShowError(issue.Message);
                    return;
                }
            }
            Load(current);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            ShowError(exception.Message);
        }
    }

    public void Revert()
    {
        if (IsEditable) Load(_session.ReadDraft<TracerDraft>());
    }

    private void Load(TracerDraft current)
    {
        _original = current;
        _working = current.Clone();
        CreateInputs();
    }

    private void CreateInputs()
    {
        Parameters =
        [
            new InspectorUnsignedIntegerPropertyRowViewModel("Draw interval", nameof(_working.DrawInterval),
                _working.DrawInterval, value => _working.DrawInterval = value, isReadOnly: !IsEditable),
            Number("Speed", nameof(_working.Speed), _working.Speed, value => _working.Speed = value),
            Number("Beam length", nameof(_working.BeamLength), _working.BeamLength, value => _working.BeamLength = value),
            Number("Beam width", nameof(_working.BeamWidth), _working.BeamWidth, value => _working.BeamWidth = value),
            Number("Screw radius", nameof(_working.ScrewRadius), _working.ScrewRadius, value => _working.ScrewRadius = value),
            Number("Screw distance", nameof(_working.ScrewDistance), _working.ScrewDistance, value => _working.ScrewDistance = value)
        ];
        foreach (var row in Parameters)
            row.PropertyChanged += ScalarInputChanged;

        Colors = _working.Colors.Select((color, index) =>
        {
            void Update(InspectorColorValue value) =>
                _working.SetColor(index, new TracerColor(value.Red, value.Green, value.Blue, value.Alpha));

            var row = new InspectorColorPropertyRowViewModel($"Sample {index + 1}", $"Colors[{index}]",
                new InspectorColorValue(color.Red, color.Green, color.Blue, color.Alpha), Update, isReadOnly: !IsEditable);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(row.CurrentValue)) return;
                if (!row.HasValidationError) Update(row.CurrentValue);
                Refresh();
            };
            return row;
        }).ToArray();
        OnPropertyChanged(nameof(Parameters));
        OnPropertyChanged(nameof(Colors));
        Refresh();
    }

    private InspectorFloatPropertyRowViewModel Number(string label, string field, float value, Action<float> update) =>
        new(label, field, value, update, isReadOnly: !IsEditable, valueFormat: "R");

    private void ScalarInputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(IInspectorStagedPropertyRow.HasStagedValue) ||
            sender is not IInspectorStagedPropertyRow row)
            return;

        // A successful commit updates the detached preview draft and raises this event again.
        if (row.HasStagedValue && row.CommitInput()) return;
        Refresh();
    }

    private void Refresh()
    {
        _operationError = null;
        var invalidInputs = Parameters.Concat(Colors).Where(row => row.HasValidationError)
            .Select(row => new AssetValidationIssue(row.FieldPath, $"{row.Label}: {row.ValidationMessage}",
                AssetValidationSeverity.Error));
        Diagnostics = invalidInputs.Concat(_session.ValidateCandidate(_working).Issues).ToArray();
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(MaterialName));
        OnPropertyChanged(nameof(MaterialDisplay));
        OnPropertyChanged(nameof(CanClearMaterial));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewHint));
    }

    private void ShowError(string message)
    {
        _operationError = message;
        OnPropertyChanged(nameof(ErrorMessage));
    }
}
