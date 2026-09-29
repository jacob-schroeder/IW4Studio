using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Avalonia.Threading;
using IW4.Render.EditorPreview;
using Iw4Radiant.Audio;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    // A visual walkthrough cadence, not the game's damage thresholds or timers.
    private const double DestructibleStageSeconds = 2;
    private readonly DispatcherTimer _destructibleTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _destructibleSequence = new();
    private readonly MapSoundPreview _destructibleAudio;
    private MapEntity? _destructibleEntity;
    private MapDocument? _destructibleDocument;
    private Matrix4x4 _destructibleAttachmentTransform;
    private DestructiblePreviewSettings _destructibleSettings = new();
    private bool _destructiblePlaying, _destructiblePartFx;
    private string? _destructibleNotice, _destructibleAudioNotice;
    private string? _destructibleFxRoot;
    private DestructiblePreset? _destructibleFxPreset;
    private Dictionary<string, (FxSpritePreview? Preview, string? Notice)> _destructibleFxAssets = [];
    private Task<Dictionary<string, (FxSpritePreview? Preview, string? Notice)>>? _destructibleFxPreparation;
    private int _destructibleRequest;
    private CancellationTokenSource? _destructiblePreparationCancellation;
    private MapEntity? _destructiblePreparationEntity;
    private bool _destructiblePreparing;
    private string? _destructiblePreparationNotice;

    private void InitializeDestructiblePreview()
    {
        Inspector.Destructibles.SettingsChanged += async settings =>
        {
            if (_dialogs.BlocksInput || SelectedDestructible() is not { } entity) return;
            if (ReferenceEquals(entity, _destructibleEntity) && settings.Stage == _destructibleSettings.Stage &&
                ReferenceEquals(settings.PartStates, _destructibleSettings.PartStates))
            {
                _destructibleSettings = settings;
                UpdateDestructiblePreviewPanel();
                return;
            }
            _destructiblePlaying = false;
            bool breakPart = (!ReferenceEquals(entity, _destructibleEntity) ||
                _destructibleSettings.StateFor(settings.PartIndex) != DestructiblePartState.Broken) &&
                settings.PartState == DestructiblePartState.Broken;
            bool transition = !ReferenceEquals(entity, _destructibleEntity) ||
                settings.Stage != _destructibleSettings.Stage;
            if (await PrepareDestructiblePreviewAsync(entity, settings))
                ApplyDestructiblePreview(entity, settings, transition: transition, breakPart: breakPart);
        };
        Inspector.Destructibles.PlayRequested += async () =>
        {
            if (_dialogs.BlocksInput || SelectedDestructible() is not { } entity) return;
            if (_destructiblePlaying && ReferenceEquals(entity, _destructibleEntity))
            {
                _destructiblePlaying = false;
                Workspace.Camera.SetFxPreviewPaused(true);
                _destructibleAudio.SetEnabled(false);
                UpdateDestructiblePreviewPanel("Playback stopped · current appearance held.");
                return;
            }
            if (!await PrepareDestructiblePreviewAsync(entity)) return;
            if (!ApplyDestructiblePreview(entity, new())) return;
            _destructibleSequence.Restart();
            _destructiblePlaying = true;
            UpdateDestructiblePreviewPanel();
        };
        Inspector.Destructibles.ResetRequested += () =>
        {
            if (_dialogs.BlocksInput) return;
            StopDestructiblePreview();
        };
        Workspace.Camera.DestructiblePreviewRequested += async entity =>
        {
            if (_dialogs.BlocksInput) return;
            if (entity is null) { StopDestructiblePreview(); return; }
            if (!_session.Document.Entities.Contains(entity) || !DestructiblePresets.HasDiscoveryName(entity)) return;
            _session.Select(entity);
            ShowInspectorSection(Inspector.ShowDestructiblePreview);
            if (ReferenceEquals(entity, _destructibleEntity)) UpdateDestructiblePreviewPanel();
            else if (await PrepareDestructiblePreviewAsync(entity)) ApplyDestructiblePreview(entity, new());
        };
        _destructibleTimer.Tick += (_, _) => TickDestructiblePreview();
        Workspace.Camera.NavigationChanged += () =>
            _destructibleAudio.UpdateListener(Workspace.Camera.Eye, Workspace.Camera.Right);
        Workspace.Camera.FxPreviewStatusChanged += () => Dispatcher.UIThread.Post(() =>
        {
            if (_destructibleEntity is null) return;
            _destructibleNotice = Workspace.Camera.FxPreviewNotice;
            UpdateDestructiblePreviewPanel();
        });
        Workspace.Models.CatalogReset += () =>
        {
            StopDestructiblePreview();
            Workspace.Camera.ClearDestructibleRenderingCache();
        };
        Workspace.FxBrowser.SourceLoaded += _ =>
        {
            CancelDestructiblePreparation();
            _destructibleFxRoot = null;
            _destructibleFxPreset = null;
            _destructibleFxPreparation = null;
            _destructibleFxAssets = [];
            Workspace.Camera.PrepareFxPreviewMaterials([]);
            if (_destructibleEntity is null) { UpdateDestructiblePreviewPanel(); return; }
            _destructiblePlaying = false;
            _destructiblePartFx = false;
            Workspace.Camera.StopFxPreview();
            _destructibleAudio.SetEnabled(false);
            _destructibleNotice = "FX library changed · choose an appearance or Play destruction to reload effects.";
            UpdateDestructiblePreviewPanel();
        };
        Workspace.SoundBrowser.SourceLoaded += _ =>
        {
            CancelDestructiblePreparation();
            _destructibleAudio.SetPreparedSources(null, new Dictionary<(string, SoundEmitterSettings), PreparedPreview>());
            _destructiblePreparationNotice = null;
            if (_destructibleEntity is null) return;
            _destructiblePlaying = _destructiblePartFx = false;
            Workspace.Camera.StopFxPreview();
            _destructibleAudio.Configure(null, []);
            _destructibleAudio.SetEnabled(false);
            _destructibleNotice = "Sound library changed · choose an appearance or Play destruction to prepare audio.";
            UpdateDestructiblePreviewPanel();
        };
        _destructibleAudio.StatusChanged += (message, detail) =>
        {
            if (_destructibleEntity is null) return;
            _destructibleAudioNotice = detail is not null && message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? detail : message.StartsWith("Choose ", StringComparison.Ordinal) ||
                    message.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ? message : null;
            UpdateDestructiblePreviewPanel();
        };
        Closed += (_, _) => { StopDestructiblePreview(); _destructibleAudio.Dispose(); };
    }

    private MapEntity? SelectedDestructible() => _session.Selection.Count == 1 &&
        _session.Selection.Active is MapEntity entity && _session.Document.Entities.Contains(entity) &&
        DestructiblePresets.HasDiscoveryName(entity) ? entity : null;

    private async Task<bool> PrepareDestructiblePreviewAsync(MapEntity entity,
        DestructiblePreviewSettings? settings = null)
    {
        DestructiblePreset preset = DestructiblePresets.Find(entity.Properties) ??
            throw new InvalidDataException("The destructible preset is unavailable.");
        settings ??= new();
        CancelDestructiblePreparation();
        int request = _destructibleRequest;
        var cancellation = new CancellationTokenSource();
        _destructiblePreparationCancellation = cancellation;
        _destructiblePreparationEntity = entity;
        MapDocument document = _session.Document;
        string? root = Workspace.FxBrowser.SourceDirectory;
        string? soundRoot = Workspace.SoundBrowser.SourceDirectory;
        _destructiblePlaying = false;
        _destructiblePreparing = true;
        _destructiblePreparationNotice = null;
        _destructibleAudio.SetPreparedSources(null, new Dictionary<(string, SoundEmitterSettings), PreparedPreview>());
        Workspace.Camera.SetFxPreviewPaused(true);
        _destructibleAudio.SetEnabled(false);
        UpdateDestructiblePreviewPanel();
        try
        {
            if (root is not null)
            {
                if (_destructibleFxRoot != root || !ReferenceEquals(_destructibleFxPreset, preset) ||
                    _destructibleFxPreparation is null)
                {
                    _destructibleFxRoot = root;
                    _destructibleFxPreset = preset;
                    _destructibleFxAssets = [];
                    _destructibleFxPreparation = Task.Run(() => LoadDestructibleFx(root, preset));
                }
                var preparation = _destructibleFxPreparation;
                var assets = await preparation.WaitAsync(cancellation.Token);
                if (request != _destructibleRequest || !ReferenceEquals(preparation, _destructibleFxPreparation))
                    return false;
                _destructibleFxAssets = assets;
            }
            string[] materials = root is null ? [] : _destructibleFxAssets.Values
                .Select(asset => asset.Preview).OfType<FxSpritePreview>().SelectMany(preview => preview.Materials)
                .Distinct(StringComparer.Ordinal).ToArray();
            (string Name, bool Looping)[] soundsToPrepare = PreviewSoundNames(preset).Distinct().ToArray();
            Task<PreparedPreview[]> audio = soundRoot is null ? Task.FromResult(Array.Empty<PreparedPreview>()) :
                Task.WhenAll(soundsToPrepare.Select(sound => _previewAudio.PrepareAsync(soundRoot, sound.Name,
                    DestructibleSoundSettings(sound.Looping), cancellation.Token)));
            Task<string?> rendering = Workspace.Camera.PrepareDestructibleRenderingAsync(entity, settings, materials, cancellation.Token);
            await Task.WhenAll(audio, rendering);
            bool current = request == _destructibleRequest && ReferenceEquals(document, _session.Document) &&
                document.Entities.Contains(entity) && DestructiblePresets.HasDiscoveryName(entity) &&
                root == Workspace.FxBrowser.SourceDirectory && soundRoot == Workspace.SoundBrowser.SourceDirectory &&
                !_dialogs.BlocksInput && _previewBspPath is null;
            if (!current) return false;
            PreparedPreview[] sounds = await audio;
            var preparedSources = new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>();
            for (int index = 0; index < sounds.Length; index++)
                preparedSources.Add((soundsToPrepare[index].Name,
                    DestructibleSoundSettings(soundsToPrepare[index].Looping)), sounds[index]);
            _destructibleAudio.SetPreparedSources(soundRoot, preparedSources);
            string[] notices = sounds.Select(sound => sound.Error)
                .Append(await rendering).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            _destructiblePreparationNotice = notices.Length == 0 ? null : string.Join(" ", notices);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is InvalidOperationException)
        {
            if (request == _destructibleRequest)
                _destructiblePreparationNotice = $"Cannot prepare preview: {exception.Message}";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_destructiblePreparationCancellation, cancellation))
            {
                _destructiblePreparationCancellation = null;
                _destructiblePreparationEntity = null;
                _destructiblePreparing = false;
                UpdateDestructiblePreviewPanel();
            }
            cancellation.Dispose();
        }
    }

    private void CancelDestructiblePreparation()
    {
        _destructibleRequest++;
        _destructiblePreparationCancellation?.Cancel();
        _destructiblePreparationCancellation = null;
        _destructiblePreparationEntity = null;
        _destructiblePreparing = false;
    }

    private static Dictionary<string, (FxSpritePreview? Preview, string? Notice)> LoadDestructibleFx(
        string root, DestructiblePreset preset)
    {
        var assets = new Dictionary<string, (FxSpritePreview? Preview, string? Notice)>(StringComparer.Ordinal);
        var names = preset.Preview.Stages.Select(stage => stage.FxName)
            .Concat(preset.Preview.Parts?.Select(part => part.FxName) ?? [])
            .OfType<string>().Distinct(StringComparer.Ordinal);
        foreach (string name in names)
        {
            try
            {
                var preview = FxSpritePreview.Load(root, name, Vector3.Zero, Matrix4x4.Identity);
                preview.SetPaused(true);
                assets.Add(name, (preview, preview.Notice));
            }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is JsonException)
            {
                assets.Add(name, (null, $"FX '{name}': {exception.Message}"));
            }
        }
        return assets;
    }

    private static IEnumerable<(string Name, bool Looping)> PreviewSoundNames(DestructiblePreset preset)
    {
        foreach (DestructiblePreviewStage stage in preset.Preview.Stages)
        {
            if (stage.SoundName is { } loop) yield return (loop, true);
            if (stage.TransitionSoundName is { } once) yield return (once, false);
        }
        if (preset.Preview.Parts is null) yield break;
        foreach (DestructiblePreviewPart part in preset.Preview.Parts)
            if (part.SoundName is { } once) yield return (once, false);
    }

    private bool ApplyDestructiblePreview(MapEntity entity, DestructiblePreviewSettings settings,
        bool transition = false, bool breakPart = false)
    {
        if (!_session.Document.Entities.Contains(entity) || !DestructiblePresets.HasDiscoveryName(entity) ||
            _previewBspPath is not null) return false;
        try
        {
            DestructiblePreset preset = DestructiblePresets.Find(entity.Properties) ??
                throw new InvalidDataException("The destructible preset is unavailable.");
            DestructiblePreviewStage stage = preset.Preview.Stages[settings.Stage];
            DestructiblePreviewPart? part = preset.Preview.Parts?.ElementAtOrDefault(settings.PartIndex);
            if (!ReferenceEquals(_destructibleEntity, entity))
            {
                if (!Workspace.Camera.WalkMode) FinishGestures();
                Inspector.Painter.StopPainting();
                if (_session.HasPlacement) _session.CancelPlacement();
                StopEmitterPreview("Destructible preview · map unchanged.");
                _destructiblePlaying = false;
                _destructibleSequence.Reset();
                _destructibleEntity = entity;
                _destructibleDocument = _session.Document;
            }
            bool geometryChanged = _session.Scene.SetDestructiblePreview(entity, settings);
            _destructibleSettings = settings;
            _destructibleNotice = _destructibleAudioNotice = null;
            _destructiblePartFx = breakPart;
            if (geometryChanged)
            {
                Workspace.Camera.RefreshDestructibleAppearance();
                foreach (var view in Workspace.GridViews) view.InvalidateVisual();
            }
            string? fxTag = stage.FxTag;
            if (fxTag is not null)
                _session.Scene.TryGetDestructibleTagTransform(fxTag, out _destructibleAttachmentTransform);
            StartDestructibleFx(breakPart ? part?.FxName : stage.FxName,
                breakPart ? part?.FxTag : stage.FxTag,
                repeat: !breakPart && stage.RepeatFx, worldUp: !breakPart && stage.WorldUpFx);
            ConfigureDestructibleAudio(stage, transition, breakPart ? part : null);
            _destructibleTimer.Start();
            UpdateDestructiblePreviewPanel();
            return true;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            StopDestructiblePreview();
            UpdateDestructiblePreviewPanel($"Preview unavailable: {exception.Message}");
            SetStatus($"Destructible preview unavailable: {exception.Message}");
            return false;
        }
    }

    private void StartDestructibleFx(string? name, string? tag, bool repeat = false, bool worldUp = false)
    {
        if (name is null) { Workspace.Camera.StopFxPreview(); return; }
        if (tag is null) { Workspace.Camera.StopFxPreview(); return; }
        string? root = Workspace.FxBrowser.SourceDirectory;
        if (root is null)
        {
            Workspace.Camera.StopFxPreview();
            _destructibleNotice = "Load the FX library to preview effects.";
            return;
        }
        if (root != _destructibleFxRoot || !_destructibleFxAssets.TryGetValue(name, out var asset) || asset.Preview is null)
        {
            Workspace.Camera.StopFxPreview();
            _destructibleNotice = _destructibleFxAssets.GetValueOrDefault(name).Notice ?? "Prepare the destruction effects before playback.";
            return;
        }
        if (!_session.Scene.TryGetDestructibleTagTransform(tag, out Matrix4x4 transform))
        {
            Workspace.Camera.StopFxPreview();
            _destructibleNotice = $"Model attachment '{tag}' is unavailable.";
            return;
        }
        Vector3 origin = transform.Translation;
        transform.Translation = Vector3.Zero;
        // The stock explosion uses PlayFX with a world-up forward axis rather than tag angles.
        if (worldUp) transform = Matrix4x4.CreateRotationY(-MathF.PI / 2);
        _destructibleNotice = Workspace.Camera.StartFxPreview(asset.Preview, origin, transform, blend: repeat);
        Workspace.Camera.SetFxPreviewPaused(false);
        Workspace.Camera.SetFxPreviewRepeat(repeat);
    }

    private void ConfigureDestructibleAudio(DestructiblePreviewStage stage,
        bool transition, DestructiblePreviewPart? brokenPart)
    {
        if (_destructibleEntity is not { } entity) return;
        var emitters = new List<MapSoundPreview.Emitter>();
        Vector3 origin = EditorSession.EntityOrigin(entity);
        if (stage.SoundName is { } loop) Add(loop, looping: true);
        if (transition && stage.TransitionSoundName is { } arrival) Add(arrival);
        if (brokenPart?.SoundName is { } partSound) Add(partSound);
        string? root = Workspace.SoundBrowser.SourceDirectory;
        if (transition && stage.TransitionSoundName is not null || brokenPart?.SoundName is not null)
            _destructibleAudio.Configure(root, []);
        _destructibleAudio.Configure(root, emitters);
        _destructibleAudio.UpdateListener(Workspace.Camera.Eye, Workspace.Camera.Right);
        _destructibleAudio.SetEnabled(true);

        void Add(string name, bool looping = false) => emitters.Add(new(entity, emitters.Count, name, origin,
            DestructibleSoundSettings(looping), null));
    }

    private void TickDestructiblePreview()
    {
        if (!DestructiblePreviewIsCurrent() || _destructibleEntity is not { } entity)
        { StopDestructiblePreview(); return; }
        if (_dialogs.BlocksInput) return;
        if (_destructiblePlaying)
        {
            DestructiblePreset? preset = DestructiblePresets.Find(entity.Properties);
            if (preset is null) { StopDestructiblePreview(); return; }
            int stage = Math.Min((int)(_destructibleSequence.Elapsed.TotalSeconds / DestructibleStageSeconds),
                preset.Preview.Stages.Count - 1);
            if (_destructibleSettings.Stage != stage)
            {
                if (!ApplyDestructiblePreview(entity,
                    _destructibleSettings with { Stage = stage }, transition: true)) return;
                if (stage == preset.Preview.Stages.Count - 1)
                { _destructiblePlaying = false; UpdateDestructiblePreviewPanel(); }
            }
        }
        DestructiblePreset? currentPreset = DestructiblePresets.Find(entity.Properties);
        DestructiblePreviewStage? currentStage = currentPreset?.Preview.Stages.ElementAtOrDefault(_destructibleSettings.Stage);
        if (currentStage?.RepeatFx == true && currentStage.FxTag is { } tag &&
            _session.Scene.TryGetDestructibleTagTransform(tag, out Matrix4x4 transform) &&
            transform != _destructibleAttachmentTransform)
        {
            _destructibleAttachmentTransform = transform;
            _destructiblePartFx = false;
            bool paused = Workspace.Camera.IsFxPreviewPaused;
            StartDestructibleFx(currentStage.FxName, tag, repeat: true);
            Workspace.Camera.SetFxPreviewPaused(paused);
            ConfigureDestructibleAudio(currentStage, transition: false, brokenPart: null);
            _destructibleAudio.SetEnabled(!paused);
        }
        if (_destructiblePartFx && (!Workspace.Camera.HasActiveFxPreview || Workspace.Camera.IsFxPreviewFinished))
        {
            _destructiblePartFx = false;
            StartDestructibleFx(currentStage?.FxName, currentStage?.FxTag,
                repeat: currentStage?.RepeatFx == true);
            UpdateDestructiblePreviewPanel();
        }
    }

    private bool DestructiblePreviewIsCurrent() => _destructibleEntity is not null &&
        ReferenceEquals(_destructibleDocument, _session.Document) &&
        _session.Document.Entities.Contains(_destructibleEntity) &&
        DestructiblePresets.HasDiscoveryName(_destructibleEntity);

    private void RefreshDestructiblePreview()
    {
        if (_destructibleEntity is not null && !DestructiblePreviewIsCurrent()) StopDestructiblePreview();
    }

    private void StopDestructiblePreview()
    {
        CancelDestructiblePreparation();
        _destructibleAudio.SetPreparedSources(null, new Dictionary<(string, SoundEmitterSettings), PreparedPreview>());
        _destructiblePreparationNotice = null;
        _destructibleTimer.Stop();
        _destructibleSequence.Reset();
        _destructiblePlaying = _destructiblePartFx = false;
        bool active = _destructibleEntity is not null;
        _destructibleEntity = null;
        _destructibleDocument = null;
        _destructibleSettings = new();
        _destructibleNotice = _destructibleAudioNotice = null;
        _destructibleAudio.Configure(null, []);
        _destructibleAudio.SetEnabled(false);
        Workspace.Camera.PrepareFxPreviewMaterials([]);
        if (active)
        {
            bool geometryChanged = _session.Scene.SetDestructiblePreview(null, null);
            Workspace.Camera.StopFxPreview();
            if (geometryChanged)
            {
                Workspace.Camera.RefreshDestructibleAppearance();
                foreach (var view in Workspace.GridViews) view.InvalidateVisual();
            }
        }
        UpdateDestructiblePreviewPanel("Authored appearance restored · map unchanged.");
    }

    private void UpdateDestructiblePreviewPanel(string? status = null)
    {
        MapEntity? selected = SelectedDestructible();
        DestructiblePreset? preset = selected is null ? null : DestructiblePresets.Find(selected.Properties);
        if (_destructiblePreparing && ReferenceEquals(SelectedDestructible(), _destructiblePreparationEntity))
        {
            Inspector.Destructibles.SetState(preset, ReferenceEquals(_destructibleEntity, _destructiblePreparationEntity)
                ? _destructibleSettings : new(), false, "Preparing preview… You can continue editing.", true);
            return;
        }
        if (_destructibleEntity is not null && !ReferenceEquals(SelectedDestructible(), _destructibleEntity))
        {
            Inspector.Destructibles.SetState(preset, new(), false);
            return;
        }
        status ??= _destructiblePreparing ? "Preparing preview… You can continue editing." : _destructiblePlaying ? "Playing visual sequence · timing is illustrative." :
            _destructibleEntity is not null ? "Appearance held while navigating · Reset restores the authored view." : null;
        string[] notices = new[] { _destructibleNotice, _destructibleAudioNotice, _destructiblePreparationNotice }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (notices.Length != 0) status = $"{status} {string.Join(" ", notices)}";
        Inspector.Destructibles.SetState(preset, _destructibleSettings, _destructiblePlaying, status, _destructiblePreparing);
    }
}
