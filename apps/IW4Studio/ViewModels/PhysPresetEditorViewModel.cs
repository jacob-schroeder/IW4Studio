using System.ComponentModel;
using IW4.Game.Assets.Physics;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class PhysPresetEditorViewModel : ObservableObject, IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private PhysPresetDraft _original;
    private PhysPresetDraft _working;
    private bool _infiniteFriction;
    private bool _hasSoundPrefix;
    private string? _applyError;

    public PhysPresetEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _original = session.OpenDraft<PhysPresetDraft>();
        _working = _original.Clone();
        _infiniteFriction = PhysPresetAsset.IsInfiniteFriction(_original.Friction);
        _hasSoundPrefix = _original.SndAliasPrefix is not null;

        Mass = Float("Mass", _original.Mass, value => _working.Mass = value);
        Bounce = Float("Bounce", _original.Bounce, value => _working.Bounce = value, slider: true);
        Friction = Float("Friction", FiniteFrictionInput, value => _working.Friction = value, slider: true);
        BulletForceScale = Float("Bullet force scale", _original.BulletForceScale, value => _working.BulletForceScale = value);
        ExplosiveForceScale = Float("Explosive force scale", _original.ExplosiveForceScale, value => _working.ExplosiveForceScale = value);
        PiecesSpreadFraction = Float("Spread fraction", _original.PiecesSpreadFraction, value => _working.PiecesSpreadFraction = value);
        PiecesUpwardVelocity = Float("Upward velocity", _original.PiecesUpwardVelocity, value => _working.PiecesUpwardVelocity = value);
        Type = new("Type", nameof(PhysPresetAsset.Type), _original.Type, value => _working.Type = value, isReadOnly: !IsEditable);
        SoundPrefix = new("Sound alias prefix", nameof(PhysPresetAsset.SndAliasPrefix), _original.SndAliasPrefix,
            value => _working.SndAliasPrefix = value, PhysPresetDraft.ValidateSoundPrefix, isReadOnly: !IsEditable);
        _inputs = [Mass, Bounce, Friction, BulletForceScale, ExplosiveForceScale, PiecesSpreadFraction, PiecesUpwardVelocity, Type, SoundPrefix];
        foreach (var input in _inputs)
            input.PropertyChanged += InputChanged;
        UpdateInputAccess();
    }

    private readonly InspectorPropertyRowViewModel[] _inputs;
    public string Name => _original.Name ?? _session.Entry.OriginalName ?? "PhysPreset";
    public bool IsEditable => _session.CanEdit;
    public string SourceZone => (!IsEditable ? _session.Entry.ResolvedProviderZone?.LogicalZoneName : null)
        ?? (_session.Workspace.IsBlank ? "New fastfile" : Path.GetFileName(_session.Workspace.SourcePath));
    public InspectorFloatPropertyRowViewModel Mass { get; }
    public InspectorFloatPropertyRowViewModel Bounce { get; }
    public InspectorFloatPropertyRowViewModel Friction { get; }
    public InspectorFloatPropertyRowViewModel BulletForceScale { get; }
    public InspectorFloatPropertyRowViewModel ExplosiveForceScale { get; }
    public InspectorFloatPropertyRowViewModel PiecesSpreadFraction { get; }
    public InspectorFloatPropertyRowViewModel PiecesUpwardVelocity { get; }
    public InspectorIntegerPropertyRowViewModel Type { get; }
    public InspectorTextPropertyRowViewModel SoundPrefix { get; }

    private float FiniteFrictionInput => PhysPresetAsset.IsInfiniteFriction(_original.Friction) ? 0f : _original.Friction;
    public double PreviewBounce => Bounce.SliderValue;
    public double PreviewFriction => Friction.SliderValue;

    public bool InfiniteFriction
    {
        get => _infiniteFriction;
        set
        {
            if (!IsEditable || !SetProperty(ref _infiniteFriction, value)) return;
            UpdateInputAccess();
            NotifyChanges();
        }
    }

    public bool HasSoundPrefix
    {
        get => _hasSoundPrefix;
        set
        {
            if (!IsEditable || !SetProperty(ref _hasSoundPrefix, value)) return;
            UpdateInputAccess();
            NotifyChanges();
        }
    }

    public bool PerSurfaceSndAlias
    {
        get => _working.PerSurfaceSndAlias != 0;
        set
        {
            if (!IsEditable || value == PerSurfaceSndAlias) return;
            _working.PerSurfaceSndAlias = FlagByte(value, _original.PerSurfaceSndAlias);
            OnPropertyChanged();
            NotifyChanges();
        }
    }

    public bool TempDefaultToCylinder
    {
        get => _working.TempDefaultToCylinder != 0;
        set
        {
            if (!IsEditable || value == TempDefaultToCylinder) return;
            _working.TempDefaultToCylinder = FlagByte(value, _original.TempDefaultToCylinder);
            OnPropertyChanged();
            NotifyChanges();
        }
    }

    private static byte FlagByte(bool enabled, byte original) =>
        enabled ? (original == 0 ? (byte)1 : original) : (byte)0;

    private IEnumerable<InspectorPropertyRowViewModel> StagedInputs =>
        _inputs.Where(row => row.IsEditable && row is IInspectorStagedPropertyRow { HasStagedValue: true });

    public bool HasUnappliedChanges => IsEditable && (
        StagedInputs.Any() || !_working.Matches(_original) ||
        InfiniteFriction != PhysPresetAsset.IsInfiniteFriction(_original.Friction) ||
        HasSoundPrefix != (_original.SndAliasPrefix is not null));

    public string? InputError => StagedInputs.FirstOrDefault(row => row.HasValidationError) is { } row
        ? $"{row.Label}: {row.ValidationMessage}"
        : _applyError;
    public bool HasInputError => InputError is not null;
    public bool CanApply => HasUnappliedChanges && !StagedInputs.Any(row => row.HasValidationError);
    public string Status => !IsEditable ? "Read only" : HasUnappliedChanges ? "Unapplied changes" : "Up to date";

    public void ApplyChanges()
    {
        if (!CanApply) return;
        if (!InfiniteFriction && PhysPresetAsset.IsInfiniteFriction(_working.Friction))
            _working.Friction = FiniteFrictionInput;
        foreach (var input in StagedInputs.Cast<IInspectorStagedPropertyRow>().ToArray())
            input.CommitInput();

        if (InfiniteFriction)
            _working.Friction = PhysPresetAsset.IsInfiniteFriction(_original.Friction) ? _original.Friction : float.MaxValue;
        _working.SndAliasPrefix = HasSoundPrefix ? SoundPrefix.Input : null;

        try
        {
            var applied = _session.ApplyAndRead<PhysPresetDraft>(current => current.ApplyEdits(_original, _working));
            Load(applied);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            _applyError = exception.Message;
            NotifyChanges();
        }
    }

    public void Revert()
    {
        if (IsEditable) Load(_session.ReadDraft<PhysPresetDraft>());
    }

    private void Load(PhysPresetDraft draft)
    {
        _original = draft;
        _working = draft.Clone();
        _infiniteFriction = PhysPresetAsset.IsInfiniteFriction(draft.Friction);
        _hasSoundPrefix = draft.SndAliasPrefix is not null;
        _applyError = null;
        Mass.RefreshAppliedValue(draft.Mass);
        Bounce.RefreshAppliedValue(draft.Bounce);
        Friction.RefreshAppliedValue(FiniteFrictionInput);
        BulletForceScale.RefreshAppliedValue(draft.BulletForceScale);
        ExplosiveForceScale.RefreshAppliedValue(draft.ExplosiveForceScale);
        PiecesSpreadFraction.RefreshAppliedValue(draft.PiecesSpreadFraction);
        PiecesUpwardVelocity.RefreshAppliedValue(draft.PiecesUpwardVelocity);
        Type.RefreshAppliedValue(draft.Type);
        SoundPrefix.RefreshAppliedValue(draft.SndAliasPrefix ?? string.Empty);
        UpdateInputAccess();
        OnPropertyChanged(nameof(InfiniteFriction));
        OnPropertyChanged(nameof(HasSoundPrefix));
        OnPropertyChanged(nameof(TempDefaultToCylinder));
        OnPropertyChanged(nameof(PerSurfaceSndAlias));
        NotifyChanges();
    }

    private InspectorFloatPropertyRowViewModel Float(string label, float value, Action<float> update, bool slider = false)
    {
        // Display native nonfinite values unchanged, with a finite position for the preview slider.
        bool needsSliderPosition = slider && !float.IsFinite(value);
        var row = new InspectorFloatPropertyRowViewModel(label, label, needsSliderPosition ? 0f : value, update, isReadOnly: !IsEditable,
            sliderMinimum: slider ? 0f : null, sliderMaximum: slider ? 1f : null, valueFormat: "R");
        if (needsSliderPosition) row.RefreshAppliedValue(value);
        return row;
    }

    private void UpdateInputAccess()
    {
        Friction.SetInteractionBlocked(InfiniteFriction);
        SoundPrefix.SetInteractionBlocked(!HasSoundPrefix);
    }

    private void InputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InspectorFloatPropertyRowViewModel.SliderValue))
        {
            if (sender == Bounce) OnPropertyChanged(nameof(PreviewBounce));
            if (sender == Friction) OnPropertyChanged(nameof(PreviewFriction));
        }
        if (args.PropertyName == nameof(IInspectorStagedPropertyRow.HasStagedValue))
        {
            _applyError = null;
            NotifyChanges();
        }
    }

    private void NotifyChanges()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(InputError));
        OnPropertyChanged(nameof(HasInputError));
        OnPropertyChanged(nameof(Status));
    }
}
