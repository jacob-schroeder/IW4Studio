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
    private const string DestructibleExplosionFx = "explosions/small_vehicle_explosion";
    private const string DestructibleWindowFx = "props/car_glass_large";
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
            _destructiblePlaying = false;
            bool breakWindow = (!ReferenceEquals(entity, _destructibleEntity) ||
                _destructibleSettings.Windshield != DestructibleWindowState.Broken) &&
                settings.Windshield == DestructibleWindowState.Broken && settings.Appearance != DestructibleAppearance.Wreck;
            if (await PrepareDestructiblePreviewAsync(entity))
                ApplyDestructiblePreview(entity, settings, breakWindow: breakWindow);
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
            CancelDestructiblePreparation();
            if (_destructibleEntity is null || ReferenceEquals(SelectedDestructible(), _destructibleEntity))
                StopDestructiblePreview();
            else UpdateDestructiblePreviewPanel();
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

    private async Task<bool> PrepareDestructiblePreviewAsync(MapEntity entity)
    {
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
                if (_destructibleFxRoot != root || _destructibleFxPreparation is null)
                {
                    _destructibleFxRoot = root;
                    _destructibleFxAssets = [];
                    _destructibleFxPreparation = Task.Run(() => LoadDestructibleFx(root));
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
            string[] soundNames = ["fire_vehicle_med", "fire_vehicle_flareup_med", "car_explode_police", "veh_glass_break_large"];
            Task<PreparedPreview[]> audio = soundRoot is null ? Task.FromResult(Array.Empty<PreparedPreview>()) :
                Task.WhenAll(soundNames.Select(name => _previewAudio.PrepareAsync(soundRoot, name, DestructibleSoundSettings(name), cancellation.Token)));
            Task<string?> rendering = Workspace.Camera.PrepareDestructibleRenderingAsync(entity, materials, cancellation.Token);
            await Task.WhenAll(audio, rendering);
            bool current = request == _destructibleRequest && ReferenceEquals(document, _session.Document) &&
                document.Entities.Contains(entity) && DestructiblePresets.HasDiscoveryName(entity) &&
                root == Workspace.FxBrowser.SourceDirectory && soundRoot == Workspace.SoundBrowser.SourceDirectory &&
                !_dialogs.BlocksInput && _previewBspPath is null;
            if (!current) return false;
            PreparedPreview[] sounds = await audio;
            var preparedSources = new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>();
            for (int index = 0; index < sounds.Length; index++)
                preparedSources.Add((soundNames[index], DestructibleSoundSettings(soundNames[index])), sounds[index]);
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

    private static Dictionary<string, (FxSpritePreview? Preview, string? Notice)> LoadDestructibleFx(string root)
    {
        var assets = new Dictionary<string, (FxSpritePreview? Preview, string? Notice)>(StringComparer.Ordinal);
        var names = Enum.GetValues<DestructibleAppearance>().Select(DestructibleLoopFx).OfType<string>()
            .Concat([DestructibleExplosionFx, DestructibleWindowFx]);
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

    private bool ApplyDestructiblePreview(MapEntity entity, DestructiblePreviewSettings settings, bool explosion = false,
        bool breakWindow = false)
    {
        if (!_session.Document.Entities.Contains(entity) || !DestructiblePresets.HasDiscoveryName(entity) ||
            _previewBspPath is not null) return false;
        try
        {
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
            _destructiblePartFx = breakWindow;
            if (geometryChanged)
            {
                Workspace.Camera.RefreshDestructibleAppearance();
                foreach (var view in Workspace.GridViews) view.InvalidateVisual();
            }
            _session.Scene.TryGetDestructibleTagTransform("tag_hood_fx", out _destructibleAttachmentTransform);
            StartDestructibleFx(explosion ? DestructibleExplosionFx :
                breakWindow ? DestructibleWindowFx : DestructibleLoopFx(settings.Appearance),
                explosion ? "tag_death_fx" : breakWindow ? "tag_glass_front_fx" : "tag_hood_fx",
                repeat: !explosion && !breakWindow, worldUp: explosion);
            ConfigureDestructibleAudio(explosion, breakWindow);
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

    private static string? DestructibleLoopFx(DestructibleAppearance appearance) => appearance switch
    {
        DestructibleAppearance.LightSmoke => "smoke/car_damage_whitesmoke",
        DestructibleAppearance.HeavySmoke => "smoke/car_damage_blacksmoke",
        DestructibleAppearance.Burning => "smoke/car_damage_blacksmoke_fire",
        _ => null
    };

    private void StartDestructibleFx(string? name, string tag, bool repeat = false, bool worldUp = false)
    {
        if (name is null) { Workspace.Camera.StopFxPreview(); return; }
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

    private void ConfigureDestructibleAudio(bool explosion, bool breakWindow, bool flareUp = true)
    {
        if (_destructibleEntity is not { } entity) return;
        var emitters = new List<MapSoundPreview.Emitter>();
        Vector3 origin = EditorSession.EntityOrigin(entity);
        if (_destructibleSettings.Appearance == DestructibleAppearance.Burning)
        {
            Add("fire_vehicle_med");
            if (_destructiblePlaying && flareUp) Add("fire_vehicle_flareup_med");
        }
        if (explosion) Add("car_explode_police");
        if (breakWindow) Add("veh_glass_break_large");
        string? root = Workspace.SoundBrowser.SourceDirectory;
        if (explosion || breakWindow) _destructibleAudio.Configure(root, []);
        _destructibleAudio.Configure(root, emitters);
        _destructibleAudio.UpdateListener(Workspace.Camera.Eye, Workspace.Camera.Right);
        _destructibleAudio.SetEnabled(true);

        void Add(string name) => emitters.Add(new(entity, emitters.Count, name, origin,
            DestructibleSoundSettings(name), null));
    }

    private void TickDestructiblePreview()
    {
        if (!DestructiblePreviewIsCurrent() || _destructibleEntity is not { } entity)
        { StopDestructiblePreview(); return; }
        if (_dialogs.BlocksInput) return;
        if (_destructiblePlaying)
        {
            int stage = Math.Min((int)(_destructibleSequence.Elapsed.TotalSeconds / DestructibleStageSeconds),
                (int)DestructibleAppearance.Wreck);
            if ((int)_destructibleSettings.Appearance != stage)
            {
                bool explosion = stage == (int)DestructibleAppearance.Wreck;
                if (!ApplyDestructiblePreview(entity,
                    _destructibleSettings with { Appearance = (DestructibleAppearance)stage }, explosion)) return;
                if (explosion) { _destructiblePlaying = false; UpdateDestructiblePreviewPanel(); }
            }
        }
        if (_session.Scene.TryGetDestructibleTagTransform("tag_hood_fx", out Matrix4x4 transform) &&
            transform != _destructibleAttachmentTransform)
        {
            _destructibleAttachmentTransform = transform;
            _destructiblePartFx = false;
            bool paused = Workspace.Camera.IsFxPreviewPaused;
            StartDestructibleFx(DestructibleLoopFx(_destructibleSettings.Appearance), "tag_hood_fx", repeat: true);
            Workspace.Camera.SetFxPreviewPaused(paused);
            ConfigureDestructibleAudio(explosion: false, breakWindow: false, flareUp: false);
            _destructibleAudio.SetEnabled(!paused);
        }
        if (_destructiblePartFx && (!Workspace.Camera.HasActiveFxPreview || Workspace.Camera.IsFxPreviewFinished))
        {
            _destructiblePartFx = false;
            StartDestructibleFx(DestructibleLoopFx(_destructibleSettings.Appearance), "tag_hood_fx", repeat: true);
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
        if (_destructiblePreparing && ReferenceEquals(SelectedDestructible(), _destructiblePreparationEntity))
        {
            Inspector.Destructibles.SetState(ReferenceEquals(_destructibleEntity, _destructiblePreparationEntity)
                ? _destructibleSettings : new(), false, "Preparing preview… You can continue editing.", true);
            return;
        }
        if (_destructibleEntity is not null && !ReferenceEquals(SelectedDestructible(), _destructibleEntity))
        {
            Inspector.Destructibles.SetState(new(), false);
            return;
        }
        status ??= _destructiblePreparing ? "Preparing preview… You can continue editing." : _destructiblePlaying ? "Playing visual sequence · timing is illustrative." :
            _destructibleEntity is not null ? "Appearance held while navigating · Reset restores the authored view." : null;
        string[] notices = new[] { _destructibleNotice, _destructibleAudioNotice, _destructiblePreparationNotice }
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (notices.Length != 0) status = $"{status} {string.Join(" ", notices)}";
        Inspector.Destructibles.SetState(_destructibleSettings, _destructiblePlaying, status, _destructiblePreparing);
    }
}
