using System.Numerics;
using Avalonia.Threading;
using IW4.Formats.SourceFormat.Material;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private readonly Dictionary<string, MaterialSource> _authoredWaterMaterials = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, WaterMaterialDefinition> _waterDefinitions =
        new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
    private bool _waterDefinitionsDirty = true;
    private readonly SoundAliasAudition _soundAudition = new();
    private readonly MapSoundPreview _mapSoundPreview = new();
    private readonly Dictionary<string, MaterialSource?> _emitterMaterials = new(StringComparer.Ordinal);
    private string? _emitterMaterialRoot;
    private string? _suggestedEmitterRoot;
    private string? _previewAssetName;
    private bool _previewIsSound;
    private MapEntity? _previewMarker;
    private MapDocument? _previewDocument;
    private FxPreviewWindow? _fxPreviewWindow;
    private MapDocument? _mapPreviewDocument;
    private string? _mapFxSource;
    private (MapEntity Owner, string Name, Vector3 Origin, Matrix4x4 Orientation)[] _mapFxEmitters = [];

    private void InitializeAuthoring()
    {
        Inspector.PlacementRequested += className => BeginPlacement(className,
            (position, _) => GameplayEntityEditing.Place(_session, className, position));
        Inspector.ModelBrowserRequested += Workspace.ShowModels;
        Inspector.PrefabBrowserRequested += Workspace.ShowPrefabs;
        Inspector.FxSoundBrowserRequested += (name, isSound) =>
        {
            Workspace.ShowFxSounds(isSound);
            BrowserFor(isSound).ShowReference(name, browseAlternatives: isSound);
        };
        Inspector.FxSoundPreviewRequested += (name, isSound) =>
        {
            Workspace.ShowFxSounds(isSound);
            BrowserFor(isSound).ShowReference(name);
            StartEmitterPreview(new FxSoundAsset(name, isSound));
        };
        Workspace.Models.InitializeActions(this, _session, _dialogs, FinishGestures, SetStatus);
        Workspace.Prefabs.InitializeActions(this, _session, _dialogs, FinishGestures, _files.OpenPathAsync);
        foreach (FxSoundBrowser browser in new[] { Workspace.FxBrowser, Workspace.SoundBrowser })
        {
            browser.InitializeActions(this, _dialogs, FinishGestures, SetStatus);
            browser.PlacementRequested += (name, isSound) => BeginPlacement(name,
                (position, _) => GameplayEntityEditing.PlaceFxSound(_session, name, isSound, position));
            browser.SelectedSoundRequested += name =>
            {
                if (_dialogs.BlocksInput) return;
                if (SelectedSoundMarker() is { } marker)
                    GameplayEntityEditing.SetFxSoundReference(_session, marker, name);
            };
            browser.PreviewRequested += StartEmitterPreview;
            browser.PreviewStopRequested += () => StopEmitterPreview("Preview stopped.");
            browser.SourceLoaded += root =>
            {
                StopEmitterPreview("Select an asset to preview.");
                _buildEmitterAssetsPath = root;
                if (!browser.IsSoundBrowser)
                {
                    Workspace.Camera.SetMapFxPreview(null, []);
                    _mapFxSource = null;
                    _mapFxEmitters = [];
                }
                lock (_emitterMaterials)
                {
                    _emitterMaterialRoot = root;
                    _emitterMaterials.Clear();
                }
                Workspace.Camera.ReloadTextures();
                FxSoundBrowser other = ReferenceEquals(browser, Workspace.FxBrowser)
                    ? Workspace.SoundBrowser : Workspace.FxBrowser;
                if (other.SourceDirectory != root)
                    _ = other.LoadDirectoryAsync(root, nonBlocking: true);
                RefreshMapPreviews();
            };
        }
        Workspace.BrowserTabChanged += () =>
        {
            if (_previewAssetName is not null) StopEmitterPreview("Preview stopped.");
        };
        _soundAudition.PlaybackEnded += () =>
        {
            if (!_previewIsSound) return;
            _previewAssetName = null;
            _previewMarker = null;
            _previewDocument = null;
            _previewIsSound = false;
            Workspace.SoundBrowser.SetPreviewState(false, "Sound preview finished.");
            _mapSoundPreview.SetSuspended(false);
        };
        Workspace.MapFxPauseRequested += () =>
        {
            Workspace.Camera.SetMapFxPreviewPaused(!Workspace.Camera.IsMapFxPreviewPaused);
            UpdateMapFxPreviewState();
        };
        Workspace.MapFxRestartRequested += () =>
        {
            bool finished = Workspace.Camera.IsMapFxPreviewFinished;
            Workspace.Camera.RestartMapFxPreview();
            if (finished) Workspace.Camera.SetMapFxPreviewPaused(false);
            UpdateMapFxPreviewState();
        };
        Workspace.Camera.MapFxPreviewStatusChanged += () => Dispatcher.UIThread.Post(UpdateMapFxPreviewState);
        Workspace.MapFxPreviewChanged += enabled =>
        {
            RefreshMapFxPreview();
            UpdateMapFxPreviewState();
            SetStatus(enabled ? "Placed FX preview on." : "Placed FX preview off.");
        };
        Workspace.MapSoundsPreviewChanged += enabled =>
        {
            RefreshMapSoundPreview();
            SetStatus(enabled ? "Placed sounds preview on. Move the camera near a sound marker to hear it."
                : "Placed sounds preview off.");
            _mapSoundPreview.SetEnabled(enabled);
        };
        Workspace.Camera.NavigationChanged += () =>
            _mapSoundPreview.UpdateListener(Workspace.Camera.Eye, Workspace.Camera.Right);
        _session.PointEntityPreviewChanged += _ =>
        {
            RefreshEmitterPreview();
            if (Workspace.MapFxEnabled) Workspace.Camera.UpdateMapFxPreviewTransforms();
        };
        _mapSoundPreview.StatusChanged += (message, detail) =>
        {
            Workspace.SoundBrowser.SetMapPreviewStatus(message, detail);
            SetStatus(message);
        };
        _mapPreviewDocument = _session.Document;
        Closed += (_, _) =>
        {
            _mapSoundPreview.Dispose();
            StopEmitterPreview("Preview stopped.");
            Workspace.Camera.SetMapFxPreview(null, []);
            _soundAudition.Dispose();
        };
        Workspace.Materials.CatalogChanged += RefreshAssets;
        Workspace.Models.CatalogChanged += RefreshAssets;
        var settings = RadiantSettings.Load();
        Workspace.Materials.InitializeFavorites(settings);
        Inspector.Painter.InitializePresets(settings, Workspace.Models.ResolveModel);
        Inspector.Painter.UpdateMapContext(_session.FilePath, _session.Prefabs);
        Workspace.Models.CatalogChanged += Inspector.Painter.ResolveModels;
        bool suppressRelatedModelRestore = false;
        Workspace.Materials.FolderLoaded += async (root, nonBlocking) =>
        {
            settings.MaterialFolder = root;
            settings.Save();
            SuggestEmitterSource(root);
            if (suppressRelatedModelRestore) return;
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) is "images" or "materials")
                root = Path.GetDirectoryName(root) ?? root;
            if (Directory.Exists(Path.Combine(root, "xmodel"))) await Workspace.Models.LoadFolderAsync(root, nonBlocking);
        };
        Workspace.Models.PlacementRequested += (model, align) => BeginPlacement(model.Name,
            (position, normal) => XModelEditing.Place(_session, model, position, align ? normal : null));
        Workspace.Models.DropRequested += DropModels;
        Workspace.Models.CatalogReset += Inspector.Painter.MarkModelsUnavailable;
        Workspace.Models.FoliageModelRequested += model =>
        {
            Inspector.Painter.AddModel(model);
            Inspector.Painter.StartPainting();
            ShowInspectorSection(Inspector.ShowPainter);
        };
        Workspace.Prefabs.PainterPrefabRequested += path =>
        {
            FoliagePaletteModel item = Inspector.Painter.AddPrefab(path);
            if (item.IsAvailable) Inspector.Painter.StartPainting();
            ShowInspectorSection(Inspector.ShowPainter);
            SetStatus(item.IsAvailable
                ? $"Added {Path.GetFileNameWithoutExtension(path)} to the brush · drag over camera surfaces to paint"
                : item.AvailabilityError ?? "This prefab is unavailable for the current map.");
        };
        Inspector.Painter.Changed += RefreshFoliagePainting;
        Inspector.Painter.ModelsRequested += Workspace.ShowModels;
        Inspector.Painter.PrefabsRequested += Workspace.ShowPrefabs;
        Workspace.Models.FolderLoaded += root =>
        {
            settings.XModelFolder = root;
            settings.Save();
            SuggestEmitterSource(root);
        };
        Workspace.Prefabs.PlacementRequested += path =>
        {
            PrefabLibrary.Reference(_session.FilePath ?? throw new ArgumentException("Save the map before placing a prefab."), path);
            BeginPlacement(Path.GetFileNameWithoutExtension(path), (position, _) => _session.Prefabs.Place(_session, path, position));
        };
        _session.Scene.ResolveModel = Workspace.Models.ResolveModel;
        _session.Scene.ResolveMaterial = ResolveMaterial;
        RefreshFoliagePainting();
        Opened += (_, _) => _ = RestoreAssetFoldersAsync(settings, () => suppressRelatedModelRestore = true,
            () => suppressRelatedModelRestore = false);
    }

    private async Task RestoreAssetFoldersAsync(RadiantSettings settings, Action markExplicitModelsRestored,
        Action clearExplicitModelsRestored)
    {
        try
        {
            if (settings.XModelFolder is { } models && Directory.Exists(models))
                if (await Workspace.Models.LoadFolderAsync(models, nonBlocking: true)) markExplicitModelsRestored();
            if (settings.MaterialFolder is { } material && Directory.Exists(material))
            {
                bool loaded = await Workspace.Materials.LoadFolderAsync(material, nonBlocking: true);
                if (loaded) settings.MaterialFolder = material;
            }
        }
        finally { clearExplicitModelsRestored(); }
    }

    private void StartEmitterPreview(FxSoundAsset asset)
    {
        if (!asset.IsSound && _fxPreviewWindow is { } openPreview && _previewAssetName == asset.Name)
        {
            openPreview.Activate();
            return;
        }
        FinishGestures();
        StopEmitterPreview("Preview stopped.", suspendMapSounds: asset.IsSound);
        FxSoundBrowser browser = BrowserFor(asset.IsSound);
        string? root = browser.SourceDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            _mapSoundPreview.SetSuspended(false);
            browser.SetPreviewState(false, "Choose the raw FX and sound library first.");
            return;
        }

        MapEntity? marker = _session.Selection.Active as MapEntity;
        bool matchesMarker = marker?.ClassName == "fx_origin" &&
            (marker.Properties.GetValueOrDefault("is_sound") == "1") == asset.IsSound &&
            marker.Properties.GetValueOrDefault(asset.IsSound ? "soundalias" : "fx") == asset.Name;
        if (!matchesMarker) marker = null;

        if (asset.IsSound)
        {
            _mapSoundPreview.SetSuspended(true);
            string? error = _soundAudition.Play(root, asset.Name);
            if (error is not null)
            {
                _mapSoundPreview.SetSuspended(false);
                browser.SetPreviewState(false, error);
                return;
            }
            _previewAssetName = asset.Name;
            _previewIsSound = true;
            _previewMarker = marker;
            _previewDocument = _session.Document;
            browser.SetPreviewState(true, $"Listening to {asset.DisplayName}. Stop whenever you like.");
            return;
        }

        var preview = new FxPreviewWindow(root, asset, ResolveEmitterMaterial);
        _fxPreviewWindow = preview;
        _previewAssetName = asset.Name;
        _previewDocument = _session.Document;
        preview.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_fxPreviewWindow, preview)) return;
            _fxPreviewWindow = null;
            _previewAssetName = null;
            _previewDocument = null;
            Workspace.FxBrowser.SetPreviewState(false, "Preview closed.");
        };
        browser.SetPreviewState(true, "Preview window open.");
        preview.Show(this);
    }

    private void UpdateMapFxPreviewState()
    {
        bool active = Workspace.Camera.HasActiveMapFxPreview;
        bool paused = Workspace.Camera.IsMapFxPreviewPaused;
        bool finished = Workspace.Camera.IsMapFxPreviewFinished;
        string? notice = Workspace.Camera.MapFxPreviewNotice;
        Workspace.SetMapFxPlaybackState(active, paused, finished);
        string message = !Workspace.MapFxEnabled ? "Map FX off · use FX above the camera." :
            Workspace.FxBrowser.SourceDirectory is null ? "Choose a library to preview map FX." :
            _mapFxEmitters.Length == 0 ? "No visible FX markers in this map." :
            !active ? "Map FX unavailable · see details." : finished ? "Map FX finished · replay above the camera." :
            paused ? "Map FX paused." : "Map FX playing.";
        if (active && !string.IsNullOrWhiteSpace(notice)) message += " Preview limited.";
        Workspace.FxBrowser.SetMapPreviewStatus(message,
            string.IsNullOrWhiteSpace(notice)
                ? "Editor preview of supported FX components. Game export support is separate." : notice);
    }

    private void StopEmitterPreview(string message, bool suspendMapSounds = false)
    {
        _soundAudition.Stop();
        FxPreviewWindow? preview = _fxPreviewWindow;
        _fxPreviewWindow = null;
        preview?.Close();
        _previewAssetName = null;
        _previewIsSound = false;
        _previewMarker = null;
        _previewDocument = null;
        Workspace.FxBrowser.SetPreviewState(false, message);
        Workspace.SoundBrowser.SetPreviewState(false, message);
        _mapSoundPreview.SetSuspended(suspendMapSounds);
    }

    private void RefreshEmitterPreview()
    {
        if (_previewAssetName is null) return;
        if (!ReferenceEquals(_session.Document, _previewDocument))
        {
            StopEmitterPreview("Map changed. Preview stopped.");
            return;
        }
        if (_previewMarker is not { } marker) return;
        if (!ReferenceEquals(_session.Selection.Active, marker))
        {
            StopEmitterPreview("Selection changed. Preview stopped.");
            return;
        }
        if (marker.Properties.GetValueOrDefault("is_sound") != "1" ||
            marker.Properties.GetValueOrDefault("soundalias") != _previewAssetName)
            StopEmitterPreview("Sound changed. Preview stopped.");
    }

    private void SuggestEmitterSource(string? assetFolder = null)
    {
        if (Workspace.FxBrowser.SourceDirectory is not null) return;
        string? root = _buildEmitterAssetsPath ??
            (_session.FilePath is { } mapPath ? FindEmitterAssetDirectory(mapPath) : null);
        if (root is null && assetFolder is not null)
        {
            string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetFolder));
            if (Path.GetFileName(candidate) is "images" or "materials" or "xmodel")
                candidate = Path.GetDirectoryName(candidate) ?? candidate;
            if (Directory.Exists(Path.Combine(candidate, "fx")) ||
                Directory.Exists(Path.Combine(candidate, "soundaliases")))
                root = candidate;
        }
        if (root is null || root == _suggestedEmitterRoot) return;
        _suggestedEmitterRoot = root;
        _ = Workspace.FxBrowser.LoadDirectoryAsync(root, nonBlocking: true);
    }

    private FxSoundBrowser BrowserFor(bool isSound) => isSound ? Workspace.SoundBrowser : Workspace.FxBrowser;

    private MapEntity? SelectedSoundMarker() => _session.Selection.Count == 1 &&
        _session.Selection.Active is MapEntity entity &&
        _session.Document.Entities.Contains(entity) &&
        entity.ClassName == "fx_origin" && entity.Properties.GetValueOrDefault("is_sound") == "1"
            ? entity : null;

    private void RefreshMapPreviews()
    {
        if (!ReferenceEquals(_session.Document, _mapPreviewDocument))
        {
            _mapPreviewDocument = _session.Document;
            Workspace.DisableMapPreviews();
        }
        RefreshMapFxPreview();
        RefreshMapSoundPreview();
    }

    private void RefreshMapFxPreview()
    {
        string? root = Workspace.FxBrowser.SourceDirectory;
        (MapEntity Owner, string Name, Vector3 Origin, Matrix4x4 Orientation)[] emitters = Workspace.MapFxEnabled
            ? PlacedEmitterEntities(isSound: false)
                .Select(entity => (entity, entity.Properties.GetValueOrDefault("fx") ?? "",
                    EditorSession.EntityOrigin(entity), EntityOrientation.Rotation(entity))).ToArray() : [];
        if (_mapFxSource == root && _mapFxEmitters.SequenceEqual(emitters)) return;
        _mapFxSource = root;
        _mapFxEmitters = emitters;
        Workspace.Camera.SetMapFxPreview(root, emitters);
        UpdateMapFxPreviewState();
    }

    private void RefreshMapSoundPreview()
    {
        var emitters = new List<(MapEntity Owner, int Slot, string Name, Vector3 Origin)>();
        if (Workspace.MapSoundsEnabled)
        {
            var slots = new Dictionary<MapEntity, int>();
            foreach (MapEntity entity in PlacedEmitterEntities(isSound: true))
            {
                // Scene copies change on edits. Keep voices keyed to their authored owner;
                // prefab children get separate slots within that instance.
                if (_session.Scene.Owner(entity) is not MapEntity owner) continue;
                int slot = slots.GetValueOrDefault(owner);
                slots[owner] = slot + 1;
                emitters.Add((owner, slot, entity.Properties.GetValueOrDefault("soundalias") ?? "",
                    EditorSession.EntityOrigin(entity)));
            }
        }
        _mapSoundPreview.Configure(Workspace.SoundBrowser.SourceDirectory,
            emitters);
        _mapSoundPreview.UpdateListener(Workspace.Camera.Eye, Workspace.Camera.Right);
    }

    private IEnumerable<MapEntity> PlacedEmitterEntities(bool isSound) =>
        _session.Scene.Document.Entities
            .Where(entity => entity.ClassName == "fx_origin" &&
                (entity.Properties.GetValueOrDefault("is_sound") == "1") == isSound &&
                entity.TryGetOrigin(out _));

    private MaterialSource? ResolveEmitterMaterial(string name)
    {
        lock (_emitterMaterials)
        {
            if (_emitterMaterialRoot is not { } root) return null;
            if (_emitterMaterials.TryGetValue(name, out MaterialSource? cached)) return cached;
            MaterialSource? material;
            try { material = MaterialCatalog.ReadOne(root, name); }
            catch (Exception exception) when (FileOperationErrors.IsExpected(exception) ||
                                              exception is System.Text.Json.JsonException)
            {
                material = null;
            }
            _emitterMaterials.Add(name, material);
            return material;
        }
    }

    private MaterialSource? ResolveMaterial(string name)
    {
        if (!WaterMaterialAuthoring.IsAuthoredMaterialName(name))
            return Workspace.Materials.ResolveMaterial(name) ?? Workspace.Models.ResolveMaterial(name) ?? ResolveEmitterMaterial(name);
        if (_waterDefinitionsDirty)
        {
            _waterDefinitionsDirty = false;
            try
            {
                var properties = new Dictionary<string, string>(_session.Document.World.Properties, StringComparer.Ordinal);
                foreach (MapEntity instance in _session.Document.Entities.Where(PrefabLibrary.IsPrefab))
                    if (_session.Prefabs.GetPreview(instance, _session.FilePath) is { } preview)
                        WaterMaterialAuthoring.MergeDefinitions(properties, preview.World.Properties);
                _waterDefinitions = WaterMaterialAuthoring.ReadDefinitions(properties);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                               FormatException or OverflowException)
            {
                _waterDefinitions = new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
            }
            foreach (string cached in _authoredWaterMaterials.Keys.Where(key => !_waterDefinitions.ContainsKey(key)).ToArray())
                _authoredWaterMaterials.Remove(cached);
        }
        return ResolveMaterial(name, _waterDefinitions);
    }

    private MaterialSource? ResolveMaterial(string name,
        IReadOnlyDictionary<string, WaterMaterialDefinition> definitions)
    {
        if (!definitions.TryGetValue(name, out WaterMaterialDefinition? definition))
            return WaterMaterialAuthoring.IsAuthoredMaterialName(name)
                ? null
                : Workspace.Materials.ResolveMaterial(name) ?? Workspace.Models.ResolveMaterial(name);
        if (_authoredWaterMaterials.TryGetValue(name, out MaterialSource? authored)) return authored;
        MaterialSource? source = Workspace.Materials.ResolveMaterial(definition.SourceMaterial) ??
            Workspace.Models.ResolveMaterial(definition.SourceMaterial);
        if (source?.Water is not { } water) return null;
        authored = new MaterialSource(definition.Name, source.ImagePath, source.IsSky, source.SamplerState)
        {
            TechniqueSet = source.TechniqueSet,
            Water = WaterMaterialAuthoring.CreateWater(water, definition),
            Ocean = definition.Ocean,
            OceanFoamImagePath = source.OceanFoamImagePath,
            WaterColor = new Vector4(definition.Red, definition.Green, definition.Blue, source.WaterColor.W),
            EnvMapParms = new Vector4(definition.FresnelMinimum, definition.FresnelMaximum,
                definition.FresnelExponent, source.EnvMapParms.W),
            Surface = source.Surface,
            GameFlags = source.GameFlags,
            SurfaceTypeBits = source.SurfaceTypeBits
        };
        _authoredWaterMaterials.Add(name, authored);
        return authored;
    }

    private void RefreshAssets()
    {
        _authoredWaterMaterials.Clear();
        _waterDefinitionsDirty = true;
        if (_session.HasPlacement) _session.CancelPlacement();
        else _session.Refresh();
        Workspace.Camera.ReloadTextures();
    }

    private void BeginPlacement(string label, Action<Vector3, Vector3?> place)
    {
        Inspector.Painter.StopPainting();
        SetTool(EditorTool.Select);
        _session.BeginPlacement(label, place);
        SetStatus($"Place {label} · Click a surface or grid · Shift repeats · Esc cancels");
        Workspace.FocusActiveView();
    }

    private void RefreshFoliagePainting()
    {
        var painter = Inspector.Painter;
        var camera = Workspace.Camera;
        PainterButton.IsChecked = painter.IsPainting;
        camera.FoliageModels = painter.Models;
        camera.FoliageRadius = painter.BrushRadius;
        camera.FoliageDensity = painter.BrushDensity;
        camera.FoliageSpacing = painter.BrushSpacing;
        camera.FoliageMinimumScale = painter.MinimumBrushScale;
        camera.FoliageMaximumScale = painter.MaximumBrushScale;
        camera.FoliageRandomYaw = painter.UsesRandomYaw;
        camera.FoliageAlignSurface = painter.AlignsToSurface;
        if (painter.IsPainting)
        {
            if (_session.Tool != EditorTool.Select || _session.HasPlacement)
            {
                _activatingFoliage = true;
                try { SetTool(EditorTool.Select); }
                finally { _activatingFoliage = false; }
            }
            SetStatus("Paint the brush in the camera · drag over map surfaces · Esc cancels a stroke");
        }
        camera.FoliagePaintingEnabled = painter.IsPainting;
        if (_ready) RefreshToolOptions();
    }

    private async void DropModels()
    {
        try
        {
            int count = XModelEditing.DropToSurface(_session, Workspace.Models.ResolveModel, ResolveMaterial);
            SetStatus(count == 0 ? "No surface was found below the selected models." : $"Dropped {count} models onto surfaces.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Drop models", exception.Message); }
    }
}
