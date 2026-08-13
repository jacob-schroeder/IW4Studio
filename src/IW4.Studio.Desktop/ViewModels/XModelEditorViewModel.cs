using System.Globalization;
using System.Numerics;
using IW4.Assets.Assets;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.XModel.Export;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.Assets;
using IW4.Render.Geometry.XModel;
using IW4.Render.Export;
using IW4.Render.Materials;
using IW4.Render.OpenGl.XModel;
using IW4.Render.SceneBuilding;
using IW4.Render.Textures;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Runtime preview plus a local imported-geometry LOD assembly candidate. The
/// candidate compiles locally and publishes only when Apply succeeds.
/// </summary>
public sealed class XModelEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorPropertiesRevealSource,
      IAssetEditorInspectorSource,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState,
      IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly XModelSceneBuilder _sceneBuilder = new();
    private readonly WorkspaceGfxImagePayloadResolver _imagePayloads;
    private XModelAsset? _model;
    private XModelDraft? _workingDraft;
    private XModelRenderScene? _scene;
    private IReadOnlyList<XModelLodItemViewModel> _lods = [];
    private XModelLodItemViewModel? _selectedLod;
    private IReadOnlyList<XModelLodAssemblyItemViewModel> _assemblyLods = [];
    private IReadOnlyList<XModelImportedMaterialMappingItemViewModel> _importedMaterialMappings = [];
    private XModelAssemblyCompileResult? _compiledCandidate;
    private XModelLodAssemblyItemViewModel? _selectedAssemblyLod;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _buildDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _candidateDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _exportDiagnostics = [];
    private string _statusMessage = string.Empty;
    private string? _buildFailure;
    private int _rendererLodIndex = -1;
    private XModelViewerUploadResult? _rendererUploadResult;
    private string? _rendererFailure;
    private bool _isStudioEnvironmentEnabled = true;
    private bool _isWireframeEnabled;
    private bool _isCollisionEnabled;
    private bool _showBoneTags;
    private bool _disposed;

    public event EventHandler<AssetReferenceSelectionRequestedEventArgs>? AssetReferenceSelectionRequested;

    public XModelEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.XModel)
        {
            throw new InvalidDataException(
                "The XModel view model can host only XModel editor sessions.");
        }

        _imagePayloads = new WorkspaceGfxImagePayloadResolver(session.Workspace);
        CaptureAndBuild();
    }

    public WorkspaceAssetAccess Mode => _session.Mode;

    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;

    public string Name =>
        _model?.Name
        ?? _session.Entry.OriginalName
        ?? "Unnamed XModel";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public XModelRenderScene? Scene => _scene;

    public IReadOnlyList<XModelLodItemViewModel> Lods => _lods;

    public IReadOnlyList<XModelLodAssemblyItemViewModel> AssemblyLods => _assemblyLods;

    public IReadOnlyList<XModelLodAssemblyItemViewModel> ActiveAssemblyLods =>
        _assemblyLods.Where(lod => lod.IsOccupied).ToArray();

    public XModelLodAssemblyItemViewModel? SelectedAssemblyLod
    {
        get => _selectedAssemblyLod;
        set
        {
            // Both the toolbar ComboBox and the assembly ListBox observe the
            // same selection. Replacing their ItemsSource collections causes
            // Avalonia to transiently write null before the new selected row
            // is published. Preserve the live selection through that refresh;
            // there is no user-facing "no assembly LOD" choice.
            if (value is null &&
                _selectedAssemblyLod is not null &&
                _assemblyLods.Contains(_selectedAssemblyLod))
            {
                return;
            }
            if (value is not null && !AssemblyLods.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetProperty(ref _selectedAssemblyLod, value)) return;
            if (value is { IsBaseline: true })
                SelectedLod = Lods.FirstOrDefault(lod => lod.LodIndex == value.LodIndex) ?? SelectedLod;
            ResetRendererStatus();
            RefreshCandidateState();
            RefreshInspector();
            OnPropertyChanged(nameof(IsImportedLodSelected));
            OnPropertyChanged(nameof(ImportedLodNotice));
            OnPropertyChanged(nameof(MaterialExecutionBadge));
            OnPropertyChanged(nameof(EditorProperties));
            OnPropertyChanged(nameof(CanExportXModel));
            OnPropertyChanged(nameof(SelectedLodExportSummary));
            RefreshStatusMessage();
            PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsImportedLodSelected => SelectedAssemblyLod?.IsImported == true;

    public string ImportedLodNotice => !IsImportedLodSelected
        ? string.Empty
        : _compiledCandidate?.IsSuccess == true
            ? "STAGED GEOMETRY · COMPILED PREVIEW · APPLY TO PUBLISH"
            : "STAGED GEOMETRY · COMPILATION BLOCKED · REVIEW DIAGNOSTICS";

    public XModelLodItemViewModel? SelectedLod
    {
        get => _selectedLod;
        set
        {
            if (value is not null && !Lods.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetProperty(ref _selectedLod, value))
                return;

            _exportDiagnostics = [];
            ResetRendererStatus();
            OnPropertyChanged(nameof(SelectedLodIndex));
            OnPropertyChanged(nameof(CanExportXModel));
            OnPropertyChanged(nameof(SelectedLodExportSummary));
            OnPropertyChanged(nameof(MaterialExecutionBadge));
            RefreshInspector();
            OnPropertyChanged(nameof(EditorProperties));
            OnPropertyChanged(nameof(Diagnostics));
            RefreshStatusMessage();
            PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedLodIndex => SelectedLod?.LodIndex ?? -1;

    public bool IsStudioEnvironmentEnabled
    {
        get => _isStudioEnvironmentEnabled;
        set
        {
            if (!SetProperty(ref _isStudioEnvironmentEnabled, value))
                return;

            OnPropertyChanged(nameof(EditorProperties));
        }
    }

    public bool IsWireframeEnabled
    {
        get => _isWireframeEnabled;
        set => SetProperty(ref _isWireframeEnabled, value);
    }
    public bool CanShowCollision => _scene is not null && _workingDraft?.CollisionLod is byte collisionLod && collisionLod != 0xFF && _scene.Lods.Any(lod => lod.LodIndex == collisionLod && lod.CollisionTriangleCount > 0);
    public bool IsCollisionEnabled
    {
        get => _isCollisionEnabled;
        set
        {
            if (!SetProperty(ref _isCollisionEnabled, value) || !value) return;
            if (_workingDraft?.CollisionLod is byte collisionLod && collisionLod != 0xFF)
                SelectedLod = Lods.FirstOrDefault(lod => lod.LodIndex == collisionLod) ?? SelectedLod;
        }
    }

    public bool ShowBoneTags
    {
        get => _showBoneTags;
        set => SetProperty(ref _showBoneTags, value);
    }

    public bool CanRevert => IsEditable && _workingDraft is not null &&
        (HasUnappliedChanges || _session.HasUnsavedChanges);

    public bool CanApply => IsEditable && _workingDraft is not null && HasUnappliedChanges && _compiledCandidate?.IsSuccess == true;

    public IReadOnlyList<XModelImportedMaterialMappingItemViewModel> ImportedMaterialMappings => _importedMaterialMappings;

    public bool CanExportXModel => !IsImportedLodSelected && _model is not null && SelectedLod is not null;

    public string SelectedLodExportSummary => IsImportedLodSelected
        ? "Imported geometry is compiled locally; Apply it before re-exporting the native baseline."
        : SelectedLod is null
        ? "No loaded LOD selected"
        : $"LOD {SelectedLod.LodIndex} · {SelectedLod.VertexCount:N0} vertices · {SelectedLod.TriangleCount:N0} triangles";

    public bool HasUnappliedChanges => _workingDraft is not null && !_session.CandidateMatchesCurrent(_workingDraft);

    public string PropertySectionName => "Overview";

    public string MaterialExecutionBadge
    {
        get
        {
            if (IsImportedLodSelected)
                return _compiledCandidate?.IsSuccess == true
                    ? "STAGED GEOMETRY · COMPILED PREVIEW"
                    : "STAGED GEOMETRY · COMPILATION BLOCKED";

            LodAuthoredMaterialSummary summary =
                SummarizeAuthoredMaterials(SelectedLod?.Lod);
            if (summary.GroupCount == 0 ||
                summary.TechniqueSlots.Count == 0)
                return "NO AUTHORED CAMERA PASS";

            string slot = summary.TechniqueSlots.Count == 1
                ? $"SLOT {summary.TechniqueSlots[0]}"
                : $"SLOTS {string.Join("/", summary.TechniqueSlots)}";
            if (_rendererFailure is not null)
                return $"AUTHORED PASS · {slot} · OPENGL BLOCKED";
            if (_rendererUploadResult is { } upload)
            {
                int total = upload.ExecutableGroupCount +
                    upload.BlockedGroupCount;
                return $"AUTHORED PASS · {slot} · " +
                    $"{upload.ExecutableGroupCount}/{total} EXECUTABLE";
            }
            return $"AUTHORED PASS · {slot} · " +
                $"{summary.ReadyGroupCount}/{summary.GroupCount} PREPARED";
        }
    }

    public IReadOnlyList<AssetEditorProperty> EditorProperties
    {
        get
        {
            string skeleton = _model is null
                ? "Unavailable"
                : $"{_model.NumBones:N0} " +
                  (_model.NumBones == 1 ? "bone" : "bones") +
                  " · read-only";
            if (SelectedAssemblyLod is { IsImported: true } imported)
            {
                return
                [
                    new("Source", imported.SourceDisplay),
                    new("Selected LOD", $"LOD {imported.LodIndex} · staged import"),
                    new("Geometry", $"{imported.VertexCount:N0} vertices · {imported.TriangleCount:N0} triangles"),
                    new("Materials", imported.MaterialCount.ToString("N0")),
                    new("Skeleton", skeleton),
                    new("Status", _compiledCandidate?.IsSuccess == true ? "Ready to apply" : "Resolve compilation diagnostics")
                ];
            }

            LodAuthoredMaterialSummary summary =
                SummarizeAuthoredMaterials(SelectedLod?.Lod);
            return
            [
                new("Selected LOD", SelectedLod is null ? "None" : $"LOD {SelectedLod.LodIndex}"),
                new("Geometry", SelectedLod is null ? "No loaded geometry" : $"{SelectedLod.VertexCount:N0} vertices · {SelectedLod.TriangleCount:N0} triangles"),
                new("Materials", summary.GroupCount == 0 ? "No authored render groups" : $"{summary.ReadyGroupCount:N0} of {summary.GroupCount:N0} render groups ready"),
                new("Skeleton", skeleton)
            ];
        }
    }

    public InspectorSelectionViewModel? InspectorSelection =>
        _inspectorSelection;

    public IReadOnlyList<AssetValidationIssue> Diagnostics => _diagnostics;

    public event EventHandler? PropertiesRevealRequested;

    internal void UpdateRendererStatus(
        int lodIndex,
        XModelViewerUploadResult? uploadResult,
        string? rendererFailure)
    {
        if (_disposed)
            return;
        if (lodIndex >= 0 && lodIndex != SelectedLodIndex)
            return;
        if (_rendererLodIndex == lodIndex &&
            string.Equals(
                _rendererFailure,
                rendererFailure,
                StringComparison.Ordinal) &&
            UploadResultsEqual(_rendererUploadResult, uploadResult))
        {
            return;
        }

        _rendererLodIndex = lodIndex;
        _rendererUploadResult = uploadResult;
        _rendererFailure = rendererFailure;
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(MaterialExecutionBadge));
        RefreshStatusMessage();
    }

    public void RevertDraft()
    {
        if (!IsEditable)
            return;
        if (!CanRevert)
            return;

        if (HasUnappliedChanges)
        {
            CaptureAndBuild();
            StatusMessage = $"Discarded the staged XModel changes. {StatusMessage}";
            return;
        }

        bool reverted = _session.Revert();
        CaptureAndBuild();
        StatusMessage = reverted
            ? $"Reverted the XModel and its owned XModelSurfs, Materials, and Images to the saved baseline. {StatusMessage}"
            : $"The XModel already matched its saved baseline. {StatusMessage}";
    }

    public bool ApplyCompiledDraft()
    {
        if (!CanApply || _workingDraft is null)
            return false;
        bool applied;
        IReadOnlyList<AssetValidationIssue> issues;
        try
        {
            applied = _session.ApplyCompiledXModel(_workingDraft, out issues);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _candidateDiagnostics = [new AssetValidationIssue("xmodel.apply", exception.Message, AssetValidationSeverity.Error)];
            RebuildDiagnostics();
            OnPropertyChanged(nameof(Diagnostics));
            StatusMessage = $"XModel Apply blocked: {exception.Message}";
            return false;
        }
        _candidateDiagnostics = issues;
        if (applied)
        {
            CaptureAndBuild();
            StatusMessage = "Applied the XModel, XModelSurfs, imported Materials, and Images atomically.";
        }
        else
        {
            RebuildDiagnostics();
            OnPropertyChanged(nameof(Diagnostics));
            OnPropertyChanged(nameof(CanApply));
            RefreshStatusMessage();
        }
        return applied;
    }

    public bool TryStageImportedLod(XModelExportDocument document, string? source, bool replaceSelected, out string? error)
    {
        error = null;
        if (!IsEditable || _workingDraft is null) { error = "This XModel is not editable."; return false; }
        try
        {
            int lodIndex;
            if (replaceSelected && SelectedAssemblyLod is { IsOccupied: true } selected)
            {
                lodIndex = selected.LodIndex;
                _workingDraft.ReplaceLod(lodIndex, document, source);
            }
            else
            {
                lodIndex = _workingDraft.LodAssembly
                    .First(lod => !lod.IsOccupied)
                    .SlotIndex;
                _workingDraft.AppendImportedLod(document, source);
            }
            ResolveWorkspaceMaterialUsages(_workingDraft, lodIndex);
            ResolveImportedMaterialTemplates(_workingDraft, lodIndex);
            RefreshAssemblyProjection(lodIndex);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { error = exception.Message; return false; }
    }

    public bool TryStageReplacementModel(
        XModelExportDocument document,
        string? source,
        out string? error)
    {
        error = null;
        if (!IsEditable || _workingDraft is null)
        {
            error = "This XModel is not editable.";
            return false;
        }
        try
        {
            _workingDraft.ReplaceVisualModel(document, source);
            ResolveWorkspaceMaterialUsages(_workingDraft, 0);
            ResolveImportedMaterialTemplates(_workingDraft, 0);
            RefreshAssemblyProjection(0);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            error = exception.Message;
            return false;
        }
    }

    public void RemoveSelectedAssemblyLod()
    {
        if (!IsEditable || _workingDraft is null || SelectedAssemblyLod is not { IsOccupied: true } selected) return;
        _workingDraft.RemoveLod(selected.LodIndex);
        RefreshAssemblyProjection(Math.Min(selected.LodIndex, _workingDraft.LodAssembly.Count - 1));
    }

    public bool CanAddAssemblyLod => IsEditable && _workingDraft?.LodAssembly.Any(lod => !lod.IsOccupied) == true;
    public bool CanReplaceAssemblyLod => IsEditable && SelectedAssemblyLod?.IsOccupied == true;
    public bool CanRemoveAssemblyLod => IsEditable && SelectedAssemblyLod?.IsOccupied == true;

    /// <summary>Creates the complete export document before the desktop asks for a destination.</summary>
    public bool TryCreateXModelExportDocument(
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        document = null;
        if (_disposed || IsImportedLodSelected || _model is null || SelectedLod is null)
        {
            blockers = [IsImportedLodSelected
                ? "Apply the compiled imported LOD before exporting the native baseline."
                : "No loaded XModel LOD is selected for export."];
            return false;
        }

        return XModelExportProjector.TryProjectLoadedLod(
            _model,
            SelectedLod.LodIndex,
            out document,
            out blockers);
    }

    public bool TryCreateGlb(
        out byte[]? glb,
        out int texturedMaterialCount,
        out int materialCount,
        out IReadOnlyList<string> blockers)
    {
        glb = null;
        texturedMaterialCount = 0;
        materialCount = 0;
        if (!TryCreateXModelExportDocument(
                out XModelExportDocument? document,
                out blockers) || document is null)
        {
            return false;
        }
        materialCount = document.Materials.Count;

        try
        {
            XAssetPool pool = _session.Workspace.LoadedZone.Context.AssetPool;
            long revision = pool.Revision;
            var textures = new XModelGlbMaterialTexture?[document.Materials.Count];
            for (int materialIndex = 0;
                 materialIndex < document.Materials.Count;
                 materialIndex++)
            {
                XModelExportMaterial exportedMaterial =
                    document.Materials[materialIndex];
                if (!pool.TryResolve(
                        XAssetType.Material,
                        exportedMaterial.Name,
                        out MaterialAsset? material) ||
                    material is null)
                {
                    continue;
                }

                EditorMaterialTexturePlan texturePlan =
                    EditorMaterialTexturePlanner.Plan(
                        material.Textures,
                        (_, row) => new EditorMaterialTextureResolution(
                            TryResolveCurrentImage(pool, row),
                            null));
                if (!texturePlan.TryGetUniqueBinding(
                        EditorMaterialTextureRole.BaseColor,
                        out EditorMaterialTextureBinding? baseColor) ||
                    baseColor?.Image is not GfxImageAsset canonicalImage)
                {
                    continue;
                }

                if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                        canonicalImage,
                        _imagePayloads,
                        out GfxImagePreviewSnapshot? preview,
                        out _) ||
                    preview is null)
                {
                    continue;
                }

                textures[materialIndex] = new XModelGlbMaterialTexture(
                    preview.GetPngBytesCopy(),
                    preview.HasTransparency);
                texturedMaterialCount++;
            }

            if (pool.Revision != revision)
            {
                blockers = ["The active asset provider revision changed while GLB materials were being resolved."];
                return false;
            }

            using var output = new MemoryStream();
            XModelGlbWriter.Write(output, document, textures);
            glb = output.ToArray();
            blockers = [];
            return true;
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   InvalidOperationException or
                   ArgumentException or
                   OverflowException or
                   IOException)
        {
            blockers = [exception.Message];
            return false;
        }
    }

    private static GfxImageAsset? TryResolveCurrentImage(
        XAssetPool pool,
        MaterialTextureDef row)
    {
        GfxImageAsset? loadedImage = row.Water?.Image ?? row.Image;
        if (loadedImage is null)
            return null;
        try
        {
            return pool.ResolveCurrent(loadedImage);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void ReportXModelExportStatus(
        string message,
        AssetValidationSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _exportDiagnostics =
        [
            new AssetValidationIssue("xmodel.export", message, severity)
        ];
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Diagnostics));
        StatusMessage = message;
    }

    public void ReportXModelExportSuccess(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _exportDiagnostics = [];
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Diagnostics));
        StatusMessage = message;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PropertiesRevealRequested = null;
    }

    private void CaptureAndBuild()
    {
        int? previousLodIndex = SelectedAssemblyLod?.LodIndex ?? SelectedLod?.LodIndex;
        XModelDraft draft = _session.OpenDraft<XModelDraft>();
        _workingDraft = draft;
        _model = draft.Model;
        _exportDiagnostics = [];
        ResetRendererStatus();
        RefreshCompilation(draft);
        if (_compiledCandidate?.IsSuccess == true)
        {
            _model = _compiledCandidate.Definition;
        }

        XModelRenderScene? scene = null;
        string? buildFailure = null;
        try
        {
            RenderAssetSource source = CreateAssetSource(
                _session.Workspace);
            scene = _sceneBuilder.Build(
                _model,
                source,
                _imagePayloads,
                CapturePreviewProviders());
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   InvalidDataException or
                   ArgumentException or
                   OverflowException)
        {
            buildFailure = exception.Message;
        }

        _scene = scene;
        _buildFailure = buildFailure;
        _lods = scene?.Lods
            .Select(lod => new XModelLodItemViewModel(lod))
            .ToArray() ?? [];
        int preferredLodIndex = previousLodIndex
            ?? scene?.DefaultLodIndex
            ?? -1;
        _selectedLod = _lods.FirstOrDefault(lod =>
                lod.LodIndex == preferredLodIndex)
            ?? _lods.FirstOrDefault();
        _assemblyLods = draft.LodAssembly.Select(lod => new XModelLodAssemblyItemViewModel(lod)).ToArray();
        _selectedAssemblyLod = _assemblyLods.FirstOrDefault(lod => lod.LodIndex == previousLodIndex && lod.IsOccupied)
            ?? _assemblyLods.FirstOrDefault(lod => lod.IsOccupied);

        var issues = new List<AssetValidationIssue>();
        if (scene is not null)
        {
            var authoredDiagnosticMessages = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (XModelRenderLod lod in scene.Lods)
            {
                foreach (XModelRenderSurface surface in lod.Surfaces
                             .Where(surface => !surface.AuthoredGroupReady))
                {
                    string sceneMessage =
                        $"LOD {lod.LodIndex} surface " +
                        $"{surface.GeometrySurfaceIndex}: " +
                        $"{surface.AuthoredMaterialStatus}.";
                    authoredDiagnosticMessages.Add(sceneMessage);
                    issues.Add(new AssetValidationIssue(
                        AuthoredMaterialPath(surface),
                        $"LOD {lod.LodIndex} surface " +
                        $"{surface.GeometrySurfaceIndex} " +
                        $"'{surface.MaterialName}': " +
                        surface.AuthoredMaterialStatus,
                        AssetValidationSeverity.Warning));
                }
            }
            issues.AddRange(scene.Diagnostics
                .Where(message =>
                    !authoredDiagnosticMessages.Contains(message))
                .Select(message => new AssetValidationIssue(
                    "xmodel.preview.scene",
                    message,
                    AssetValidationSeverity.Warning)));
        }
        if (!string.IsNullOrWhiteSpace(buildFailure))
        {
            issues.Add(new AssetValidationIssue(
                "xmodel.preview.scene",
                buildFailure,
                AssetValidationSeverity.Error));
        }
        _buildDiagnostics = Array.AsReadOnly(issues
            .GroupBy(issue =>
                (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
        RebuildDiagnostics();
        RefreshStatusMessage();
        RefreshInspector(notify: false);
        NotifyProjectionChanged();
    }

    private void RefreshAssemblyProjection(int? preferredIndex)
    {
        if (_workingDraft is null) return;
        _assemblyLods = _workingDraft.LodAssembly.Select(lod => new XModelLodAssemblyItemViewModel(lod)).ToArray();
        _selectedAssemblyLod = preferredIndex is int index
            ? _assemblyLods.FirstOrDefault(lod => lod.LodIndex == index && lod.IsOccupied)
            : _assemblyLods.FirstOrDefault(lod => lod.IsOccupied);
        if (_selectedAssemblyLod is { IsBaseline: true } baseline)
            _selectedLod = _lods.FirstOrDefault(lod => lod.LodIndex == baseline.LodIndex) ?? _selectedLod;
        RefreshCandidateState();
        RefreshInspector(notify: false);
        NotifyProjectionChanged();
    }

    private void RefreshCandidateState(bool rebuildMaterialProjection = true)
    {
        if (_workingDraft is null) return;
        RefreshCompilation(_workingDraft, rebuildMaterialProjection);
        bool importedCandidate = _workingDraft.LodAssembly.Any(lod => lod.IsImported);
        if (importedCandidate && _compiledCandidate?.IsSuccess != true)
        {
            ClearInvalidImportedPreview();
        }
        else
        {
            RebuildCompiledPreview(_compiledCandidate?.Definition ?? _workingDraft.Model);
        }
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanAddAssemblyLod));
        OnPropertyChanged(nameof(CanReplaceAssemblyLod));
        OnPropertyChanged(nameof(CanRemoveAssemblyLod));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ImportedLodNotice));
        OnPropertyChanged(nameof(MaterialExecutionBadge));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void ClearInvalidImportedPreview()
    {
        // Do not fall back to an earlier native scene: it could be mistaken for
        // the selected staged geometry after its compiled candidate failed.
        _model = null;
        _scene = null;
        _lods = [];
        _selectedLod = null;
        _buildFailure = null;
        _buildDiagnostics = [];
        ResetRendererStatus();
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(Lods));
        OnPropertyChanged(nameof(SelectedLod));
        OnPropertyChanged(nameof(SelectedLodIndex));
        RefreshCollisionCapability();
    }

    private void RebuildCompiledPreview(XModelAsset candidate)
    {
        int selectedIndex = SelectedAssemblyLod?.LodIndex ?? SelectedLodIndex;
        try
        {
            _model = candidate;
            _scene = _sceneBuilder.Build(
                candidate,
                CreateAssetSource(_session.Workspace),
                _imagePayloads,
                CapturePreviewProviders());
            _buildFailure = null;
            _lods = _scene.Lods.Select(lod => new XModelLodItemViewModel(lod)).ToArray();
            _selectedLod = _lods.FirstOrDefault(lod => lod.LodIndex == selectedIndex) ?? _lods.FirstOrDefault();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
        {
            _scene = null;
            _lods = [];
            _selectedLod = null;
            _buildFailure = exception.Message;
            _buildDiagnostics =
            [
                new AssetValidationIssue(
                    "xmodel.preview.scene",
                    exception.Message,
                    AssetValidationSeverity.Error)
            ];
            ResetRendererStatus();
        }
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(Lods));
        OnPropertyChanged(nameof(SelectedLod));
        OnPropertyChanged(nameof(SelectedLodIndex));
        RefreshCollisionCapability();
    }

    private IReadOnlyList<BaseAsset> CapturePreviewProviders() =>
        (_compiledCandidate?.Providers ?? [])
            .Concat(_session.CaptureAppliedXModelProviders())
            .DistinctBy(provider => (
                provider.SerializedAssetType,
                provider.SerializedAssetName))
            .ToArray();

    private void RefreshCompilation(XModelDraft draft, bool rebuildMaterialProjection = true)
    {
        _compiledCandidate = XModelAssemblyCompiler.Compile(draft);
        _candidateDiagnostics = _session.ValidateCandidate(draft).Issues
            .Concat(_compiledCandidate.Issues)
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First()).ToArray();
        if (rebuildMaterialProjection)
            BuildMaterialMappingProjection(draft);
    }

    private void BuildMaterialMappingProjection(XModelDraft draft)
    {
        XModelLodDraft? lod = SelectedAssemblyLod is { IsImported: true } selected
            ? draft.LodAssembly[selected.LodIndex] : null;
        _importedMaterialMappings = lod?.ImportedDocument?.Materials.Select((material, index) =>
            new XModelImportedMaterialMappingItemViewModel(
                draft.Model.Name,
                index,
                material,
                _candidateDiagnostics.Where(issue => string.Equals(
                        issue.FieldPath,
                        $"xmodel.lods[{lod.SlotIndex}].materials[{index}]",
                        StringComparison.Ordinal) &&
                    issue.Severity == AssetValidationSeverity.Error)
                    .ToArray(),
                index < lod.MaterialMappings.Count
                    ? lod.MaterialMappings[index]
                    : null)).ToArray() ?? [];
        OnPropertyChanged(nameof(ImportedMaterialMappings));
    }

    private void ResolveWorkspaceMaterialUsages(XModelDraft draft, int lodIndex)
    {
        XModelLodDraft lod = draft.LodAssembly[lodIndex];
        if (lod.ImportedDocument is null)
            return;
        for (int materialIndex = 0; materialIndex < lod.ImportedDocument.Materials.Count; materialIndex++)
        {
            if (materialIndex < lod.MaterialMappings.Count && lod.MaterialMappings[materialIndex] is not null)
                continue;
            string name = lod.ImportedDocument.Materials[materialIndex].Name;
            if (_session.TryResolveWorkspaceXModelMaterialUsage(
                    name,
                    out MaterialAsset? material,
                    out ushort invHighMipRadius) &&
                material is not null)
            {
                draft.SetImportedMaterialMapping(
                    lodIndex,
                    materialIndex,
                    new XModelMaterialMapping(material, invHighMipRadius));
            }
        }
    }

    private void ResolveImportedMaterialTemplates(XModelDraft draft, int lodIndex)
    {
        XModelLodDraft importedLod = draft.LodAssembly[lodIndex];
        if (importedLod.ImportedDocument is null)
            return;

        MaterialAsset[] baselineMaterials = draft.Model.Materials
            .OfType<MaterialAsset>()
            .DistinctBy(material => material.Info.Name, StringComparer.Ordinal)
            .ToArray();
        MaterialAsset[] workspaceTemplates = _session
            .ResolveWorkspaceXModelMaterialUsages()
            .Select(mapping => mapping.Material)
            .ToArray();

        for (int materialIndex = 0;
             materialIndex < importedLod.ImportedDocument.Materials.Count;
             materialIndex++)
        {
            XModelExportMaterial source = importedLod.ImportedDocument.Materials[materialIndex];
            if (source.ImportMaterial is null)
                continue;
            XModelLodDraft currentLod = draft.LodAssembly[lodIndex];
            XModelMaterialMapping? current = materialIndex < currentLod.MaterialMappings.Count
                ? currentLod.MaterialMappings[materialIndex]
                : null;
            if (current is null && draft.Model.InvHighMipRadius.Count == 0)
                continue;
            ushort invHighMipRadius = current?.InvHighMipRadius ??
                draft.Model.InvHighMipRadius.ElementAtOrDefault(
                    Math.Min(materialIndex, Math.Max(0, draft.Model.InvHighMipRadius.Count - 1)));

            MaterialAsset? template = baselineMaterials
                .Concat(workspaceTemplates)
                .Where(candidate => XModelAssemblyCompiler.IsCompatibleImportTemplate(
                    source,
                    candidate,
                    out _))
                .DistinctBy(candidate => candidate.Info.Name, StringComparer.Ordinal)
                .OrderBy(candidate => ImportTemplateScore(candidate, baselineMaterials))
                .ThenBy(candidate => candidate.Info.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (template is not null)
            {
                draft.SetImportedMaterialMapping(
                    lodIndex,
                    materialIndex,
                    new XModelMaterialMapping(
                        template,
                        invHighMipRadius,
                        CreateOwnedMaterial: true));
            }
            else
            {
                draft.SetImportedMaterialMapping(lodIndex, materialIndex, null);
            }
        }
    }

    private static int ImportTemplateScore(
        MaterialAsset material,
        IReadOnlyList<MaterialAsset> baselineMaterials)
    {
        int score = material.Textures.Count == 1 ? 0 : 100 + material.Textures.Count * 10;
        if (material.Info.SortKey is not (MaterialSortKey.Opaque or MaterialSortKey.OpaqueAmbient))
            score += 50;
        if (!baselineMaterials.Contains(material, ReferenceEqualityComparer.Instance))
            score += 5;
        return score;
    }

    private void RefreshInspector(bool notify = true)
    {
        XModelRenderLod? inspectedLod = SelectedAssemblyLod?.IsBaseline == true
            ? SelectedLod?.Lod
            : null;
        XModelAsset? inspectedModel = _model ?? _workingDraft?.Model;
        _inspectorSelection = inspectedModel is null
            ? null
            : CreateInspectorSelection(inspectedModel, inspectedLod, SelectedAssemblyLod);
        if (notify)
            OnPropertyChanged(nameof(InspectorSelection));
    }

    private void NotifyProjectionChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(Lods));
        OnPropertyChanged(nameof(AssemblyLods));
        OnPropertyChanged(nameof(ActiveAssemblyLods));
        OnPropertyChanged(nameof(SelectedAssemblyLod));
        OnPropertyChanged(nameof(SelectedLod));
        OnPropertyChanged(nameof(SelectedLodIndex));
        OnPropertyChanged(nameof(MaterialExecutionBadge));
        OnPropertyChanged(nameof(InspectorSelection));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanExportXModel));
        OnPropertyChanged(nameof(SelectedLodExportSummary));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(IsImportedLodSelected));
        OnPropertyChanged(nameof(ImportedLodNotice));
        OnPropertyChanged(nameof(CanAddAssemblyLod));
        OnPropertyChanged(nameof(CanReplaceAssemblyLod));
        OnPropertyChanged(nameof(CanRemoveAssemblyLod));
        OnPropertyChanged(nameof(ImportedMaterialMappings));
        RefreshCollisionCapability();
    }

    private void RefreshCollisionCapability()
    {
        if (!CanShowCollision && _isCollisionEnabled)
        {
            _isCollisionEnabled = false;
            OnPropertyChanged(nameof(IsCollisionEnabled));
        }
        OnPropertyChanged(nameof(CanShowCollision));
    }

    private void RefreshStatusMessage()
    {
        if (SelectedAssemblyLod is { IsImported: true } imported)
        {
            StatusMessage =
                $"Editable draft · LOD {imported.LodIndex} staged from " +
                $"'{imported.SourceDisplay}'; " +
                (_compiledCandidate?.IsSuccess == true ? "compiled candidate ready for Apply." : "resolve the listed compilation blockers before Apply.");
            return;
        }
        if (_buildFailure is not null)
        {
            StatusMessage = $"Preview unavailable: {_buildFailure}";
            return;
        }
        if (SelectedLod?.Lod is not { } lod)
        {
            StatusMessage =
                $"{AccessText(Mode)} · No renderable LOD geometry is available.";
            return;
        }

        LodAuthoredMaterialSummary summary = SummarizeAuthoredMaterials(lod);
        string authoredStatus =
            $"{AccessText(Mode)} · {summary.TechniqueDisplay}; " +
            $"{summary.PassCount:N0} authored pass" +
            (summary.PassCount == 1 ? string.Empty : "es") +
            $", {summary.ReadyGroupCount:N0} of {summary.GroupCount:N0} " +
            "surface groups scene-ready";
        if (_rendererFailure is not null)
        {
            StatusMessage =
                $"{authoredStatus}; OpenGL blocked: {_rendererFailure}";
            return;
        }
        if (_rendererUploadResult is { } upload)
        {
            int total = upload.ExecutableGroupCount +
                upload.BlockedGroupCount;
            StatusMessage =
                $"{authoredStatus}; {upload.ExecutableGroupCount:N0} of " +
                $"{total:N0} groups OpenGL-executable, " +
                $"{upload.BlockedGroupCount:N0} blocked.";
            return;
        }

        StatusMessage = $"{authoredStatus}; OpenGL preflight pending.";
    }

    private void ResetRendererStatus()
    {
        _rendererLodIndex = -1;
        _rendererUploadResult = null;
        _rendererFailure = null;
        _diagnostics = _buildDiagnostics
            .Concat(_candidateDiagnostics)
            .Concat(_exportDiagnostics)
            .ToArray();
    }

    private void RebuildDiagnostics()
    {
        var issues = new List<AssetValidationIssue>(_buildDiagnostics);
        issues.AddRange(_candidateDiagnostics);
        issues.AddRange(_exportDiagnostics);
        XModelRenderLod? lod = _rendererLodIndex == SelectedLodIndex
            ? SelectedLod?.Lod
            : null;
        if (lod is not null && _rendererUploadResult is { } upload)
        {
            foreach (string diagnostic in upload.Diagnostics)
            {
                XModelRenderSurface? surface =
                    ResolveRendererDiagnosticSurface(lod, diagnostic);
                issues.Add(new AssetValidationIssue(
                    surface is null
                        ? "xmodel.preview.opengl"
                        : $"{AuthoredMaterialPath(surface)}.opengl",
                    $"OpenGL preflight: {diagnostic}",
                    AssetValidationSeverity.Warning));
            }
        }
        if (!string.IsNullOrWhiteSpace(_rendererFailure))
        {
            XModelRenderSurface? surface = lod is null
                ? null
                : ResolveRendererDiagnosticSurface(
                    lod,
                    _rendererFailure);
            issues.Add(new AssetValidationIssue(
                surface is null
                    ? "xmodel.preview.opengl"
                    : $"{AuthoredMaterialPath(surface)}.opengl",
                $"OpenGL execution: {_rendererFailure}",
                AssetValidationSeverity.Error));
        }

        _diagnostics = Array.AsReadOnly(issues
            .GroupBy(issue =>
                (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
    }

    private static RenderAssetSource CreateAssetSource(
        FastFileWorkspace workspace)
    {
        WorkspaceZone targetZone = workspace.LoadedZones.Single(zone =>
            zone.IsTarget);
        if (!targetZone.IsActive)
        {
            throw new InvalidOperationException(
                "The target fastfile is inactive and cannot supply XModel material assets.");
        }

        LoadedXZone target = targetZone.LoadResult;
        return new RenderAssetSource(
            target.Context.Blocks,
            target.Context.AssetPool,
            target.Context.GfxImagesByAddress,
            target.LoadedAssets,
            target.XAssetList.Assets);
    }

    private static LodAuthoredMaterialSummary SummarizeAuthoredMaterials(
        XModelRenderLod? lod)
    {
        if (lod is null)
            return LodAuthoredMaterialSummary.Empty;

        XModelRenderSurface[] groups = lod.Surfaces.ToArray();
        int ready = groups.Count(surface => surface.AuthoredGroupReady);
        int passes = groups.Sum(surface => surface.AuthoredPassCount);
        int[] slots = groups
            .Where(surface => surface.SelectedTechniqueSlot >= 0)
            .Select(surface => surface.SelectedTechniqueSlot)
            .Distinct()
            .Order()
            .ToArray();
        (int Slot, string Name)[] selections = groups
            .Where(surface => surface.SelectedTechniqueSlot >= 0)
            .Select(surface => (
                Slot: surface.SelectedTechniqueSlot,
                Name: surface.SelectedTechniqueName))
            .Distinct()
            .OrderBy(selection => selection.Slot)
            .ThenBy(selection => selection.Name, StringComparer.Ordinal)
            .ToArray();
        string techniqueDisplay = selections.Length == 0
            ? "No authored camera-color technique selected"
            : string.Join(
                "; ",
                selections.Select(selection =>
                    $"Slot {selection.Slot}" +
                    (string.IsNullOrWhiteSpace(selection.Name)
                        ? string.Empty
                        : $" · {selection.Name}")));
        return new LodAuthoredMaterialSummary(
            groups.Length,
            ready,
            passes,
            slots,
            techniqueDisplay);
    }

    private static string AuthoredMaterialPath(
        XModelRenderSurface surface) =>
        surface.SelectedTechniqueSlot >= 0
            ? $"xmodel.materials[{surface.ParentMaterialIndex}]." +
              $"techniques[{surface.SelectedTechniqueSlot}]"
            : $"xmodel.materials[{surface.ParentMaterialIndex}].cameraColor";

    private static XModelRenderSurface? ResolveRendererDiagnosticSurface(
        XModelRenderLod lod,
        string diagnostic)
    {
        foreach (XModelRenderSurface surface in lod.Surfaces)
        {
            string identity =
                $"surface{surface.GeometrySurfaceIndex}:" +
                $"{surface.MaterialName}:";
            if (diagnostic.StartsWith(
                    identity,
                    StringComparison.Ordinal))
            {
                return surface;
            }

            int groupId = checked(
                lod.LodIndex * 0x10000 +
                surface.GeometrySurfaceIndex);
            if (diagnostic.Contains(
                    $"group {groupId} ",
                    StringComparison.Ordinal))
            {
                return surface;
            }
        }

        return null;
    }

    private static bool UploadResultsEqual(
        XModelViewerUploadResult? left,
        XModelViewerUploadResult? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.ExecutableGroupCount == right.ExecutableGroupCount &&
        left.BlockedGroupCount == right.BlockedGroupCount &&
        left.Diagnostics.SequenceEqual(
            right.Diagnostics,
            StringComparer.Ordinal);

    private InspectorSelectionViewModel CreateInspectorSelection(
        XModelAsset model,
        XModelRenderLod? selectedLod,
        XModelLodAssemblyItemViewModel? selectedAssembly)
    {
        var modelRows = new List<InspectorPropertyRowViewModel>
        {
            ReadOnly("Name", "xmodel.name", Name),
            ReadOnly("Access", "xmodel.access", AccessText(Mode)),
            ReadOnly("Bones", "xmodel.numBones", model.NumBones.ToString("N0")),
            ReadOnly("Root bones", "xmodel.numRootBones", model.NumRootBones.ToString("N0")),
            ReadOnly("Surfaces", "xmodel.numSurfs", model.NumSurfs.ToString("N0")),
            ReadOnly("LOD count", "xmodel.numLods", model.NumLods.ToString("N0")),
            ReadOnly("Scale", "xmodel.scale", FormatFloat(model.Scale)),
            ReadOnly("Radius", "xmodel.radius", FormatFloat(model.Radius))
        };

        var sections = new List<InspectorSectionViewModel>
        {
            new("Model", modelRows)
        };

        if (selectedLod is not null)
        {
            sections.Add(new InspectorSectionViewModel(
                $"LOD {selectedLod.LodIndex}",
                [
                    ReadOnly("Index", "xmodel.lod.index", selectedLod.LodIndex.ToString("N0")),
                    ReadOnly("Distance", "xmodel.lod.distance", FormatFloat(selectedLod.Distance)),
                    ReadOnly("Surfaces", "xmodel.lod.surfaces", selectedLod.Surfaces.Count.ToString("N0")),
                    ReadOnly("Vertices", "xmodel.lod.vertices", selectedLod.VertexCount.ToString("N0")),
                    ReadOnly("Triangles", "xmodel.lod.triangles", selectedLod.TriangleCount.ToString("N0")),
                    ReadOnly("Bounds", "xmodel.lod.bounds", FormatBounds(selectedLod.Bounds))
                ]));

            var authoredMaterialRows = selectedLod.Surfaces
                .Select(surface => (InspectorPropertyRowViewModel)ReadOnly(
                    $"Surface {surface.GeometrySurfaceIndex} · material {surface.ParentMaterialIndex}",
                    AuthoredMaterialPath(surface),
                    surface.SelectedTechniqueSlot >= 0
                        ? $"Slot {surface.SelectedTechniqueSlot} · " +
                          $"{surface.SelectedTechniqueName} · " +
                          $"{surface.AuthoredPassCount:N0} pass" +
                          (surface.AuthoredPassCount == 1
                              ? string.Empty
                              : "es") +
                          $" · {(surface.AuthoredGroupReady ? "scene-ready" : "blocked")}"
                        : surface.AuthoredMaterialStatus))
                .ToArray();
            sections.Add(new InspectorSectionViewModel(
                "Authored camera material groups",
                authoredMaterialRows));
        }

        if (selectedAssembly is not null)
        {
            var assemblyRows = new List<InspectorPropertyRowViewModel>
            {
                ReadOnly("Slot", "xmodel.lods.slot", selectedAssembly.LodIndex.ToString(CultureInfo.InvariantCulture)),
                ReadOnly("Source", "xmodel.lods.source", selectedAssembly.SourceDisplay),
                ReadOnly("Vertices", "xmodel.lods.vertices", selectedAssembly.VertexCount.ToString("N0")),
                ReadOnly("Triangles", "xmodel.lods.triangles", selectedAssembly.TriangleCount.ToString("N0")),
                ReadOnly("Materials", "xmodel.lods.materials", selectedAssembly.MaterialCount.ToString("N0")),
                new InspectorFloatPropertyRowViewModel(
                    "Distance", "xmodel.lods.distance", selectedAssembly.Distance,
                    IsEditable && _workingDraft is not null
                        ? value => { _workingDraft.SetLodDistance(selectedAssembly.LodIndex, value); RefreshAssemblyProjection(selectedAssembly.LodIndex); }
                        : null,
                    "Active LOD distances must be finite, nonnegative, and strictly increasing.")
            };
            sections.Add(new InspectorSectionViewModel("LOD assembly", assemblyRows));
        }

        if (_workingDraft is not null)
        {
            var choices = new List<InspectorChoice> { new("none", "None") };
            choices.AddRange(_workingDraft.LodAssembly.Where(lod => lod.IsOccupied).Select(lod => new InspectorChoice(lod.SlotIndex.ToString(CultureInfo.InvariantCulture), $"LOD {lod.SlotIndex}")));
            var collisionRows = new List<InspectorPropertyRowViewModel> {
                new InspectorChoicePropertyRowViewModel(
                    "Collision LOD", "xmodel.collLod", choices,
                    _workingDraft.CollisionLod == 0xFF ? "none" : _workingDraft.CollisionLod.ToString(CultureInfo.InvariantCulture),
                    IsEditable ? value => { _workingDraft.SetCollisionLod(value == "none" ? (byte)0xFF : byte.Parse(value, CultureInfo.InvariantCulture)); RefreshAssemblyProjection(SelectedAssemblyLod?.LodIndex); } : null,
                    "Imported collision geometry compiles conservative collision trees only for this selected LOD."),
                ReadOnly("Preview", "xmodel.collision.preview", _scene is null ? "No compiled candidate" : $"{_scene.Lods.FirstOrDefault(lod => lod.LodIndex == _workingDraft.CollisionLod)?.CollisionTriangleCount ?? 0:N0} collision triangles"),
                ReadOnly("hitBoxModel", "xmodel.hitBoxModel", "Converter-only; not a runtime XModel field.")
            };
            for (int collisionIndex = 0; collisionIndex < _workingDraft.CollisionSurfaces.Count; collisionIndex++)
            {
                int rowIndex = collisionIndex;
                XModelCollSurf row = _workingDraft.CollisionSurfaces[rowIndex];
                void Update(Func<IW4.Assets.Math.Bounds, IW4.Assets.Math.Bounds> bounds, int? bone = null, int? contents = null, int? flags = null)
                {
                    XModelCollSurf current = _workingDraft.CollisionSurfaces[rowIndex];
                    _workingDraft.SetCollisionSurface(rowIndex, bounds(current.Bounds), bone ?? current.BoneIndex, contents ?? current.Contents, flags ?? current.SurfaceFlags);
                    RefreshCandidateState();
                }
                string prefix = $"xmodel.collSurfs[{rowIndex}]";
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} midpoint X", prefix + ".midpoint.x", row.Bounds.MidPoint.X, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = new IW4.Assets.Math.Vec3 { X = value, Y = bounds.MidPoint.Y, Z = bounds.MidPoint.Z }, HalfSize = bounds.HalfSize }) : null));
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} midpoint Y", prefix + ".midpoint.y", row.Bounds.MidPoint.Y, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = new IW4.Assets.Math.Vec3 { X = bounds.MidPoint.X, Y = value, Z = bounds.MidPoint.Z }, HalfSize = bounds.HalfSize }) : null));
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} midpoint Z", prefix + ".midpoint.z", row.Bounds.MidPoint.Z, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = new IW4.Assets.Math.Vec3 { X = bounds.MidPoint.X, Y = bounds.MidPoint.Y, Z = value }, HalfSize = bounds.HalfSize }) : null));
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} halfsize X", prefix + ".halfsize.x", row.Bounds.HalfSize.X, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = bounds.MidPoint, HalfSize = new IW4.Assets.Math.Vec3 { X = value, Y = bounds.HalfSize.Y, Z = bounds.HalfSize.Z } }) : null));
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} halfsize Y", prefix + ".halfsize.y", row.Bounds.HalfSize.Y, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = bounds.MidPoint, HalfSize = new IW4.Assets.Math.Vec3 { X = bounds.HalfSize.X, Y = value, Z = bounds.HalfSize.Z } }) : null));
                collisionRows.Add(new InspectorFloatPropertyRowViewModel($"CollSurf {rowIndex} halfsize Z", prefix + ".halfsize.z", row.Bounds.HalfSize.Z, IsEditable ? value => Update(bounds => new IW4.Assets.Math.Bounds { MidPoint = bounds.MidPoint, HalfSize = new IW4.Assets.Math.Vec3 { X = bounds.HalfSize.X, Y = bounds.HalfSize.Y, Z = value } }) : null));
                collisionRows.Add(new InspectorIntegerPropertyRowViewModel($"CollSurf {rowIndex} bone", prefix + ".bone", row.BoneIndex, IsEditable ? value => Update(bounds => bounds, bone: value) : null));
                collisionRows.Add(new InspectorIntegerPropertyRowViewModel($"CollSurf {rowIndex} contents", prefix + ".contents", row.Contents, IsEditable ? value => Update(bounds => bounds, contents: value) : null));
                collisionRows.Add(new InspectorIntegerPropertyRowViewModel($"CollSurf {rowIndex} surface flags", prefix + ".surfaceFlags", row.SurfaceFlags, IsEditable ? value => Update(bounds => bounds, flags: value) : null));
            }
            collisionRows.Add(new InspectorAssetReferencePropertyRowViewModel("PhysPreset", "xmodel.physPreset", XAssetType.PhysPreset, _workingDraft.PhysPreset?.Name, IsEditable ? name => { if (name is null) _workingDraft.SetPhysPreset(null); else if (_session.TryResolveWorkspaceDefinition<IW4.Assets.Assets.Physics.PhysPresetAsset>(name, out var asset)) _workingDraft.SetPhysPreset(asset); else throw new InvalidOperationException("Selected PhysPreset is not a live typed workspace definition."); RefreshCandidateState(); } : null, RequestAssetReferenceSelection));
            collisionRows.Add(new InspectorAssetReferencePropertyRowViewModel("PhysCollmap", "xmodel.physCollmap", XAssetType.PhysCollmap, _workingDraft.PhysCollmap?.Name, IsEditable ? name => { if (name is null) _workingDraft.SetPhysCollmap(null); else if (_session.TryResolveWorkspaceDefinition<IW4.Assets.Assets.Physics.PhysCollmapAsset>(name, out var asset)) _workingDraft.SetPhysCollmap(asset); else throw new InvalidOperationException("Selected PhysCollmap is not a live typed workspace definition."); RefreshCandidateState(); } : null, RequestAssetReferenceSelection));
            sections.Add(new InspectorSectionViewModel("Collision", collisionRows));
        }

        var materialRows = new List<InspectorPropertyRowViewModel>();
        for (int index = 0; index < model.Materials.Count; index++)
        {
            string name = model.Materials[index]?.Info.Name
                ?? "Unresolved material";
            string usage = selectedLod is null
                ? string.Empty
                : string.Join(
                    ", ",
                    selectedLod.Surfaces
                        .Where(surface => surface.ParentMaterialIndex == index)
                        .Select(surface => surface.GeometrySurfaceIndex)
                        .Distinct()
                        .Order());
            string value = string.IsNullOrEmpty(usage)
                ? name
                : $"{name} · surface {usage}";
            materialRows.Add(ReadOnly(
                $"Slot {index}",
                $"xmodel.materials[{index}]",
                value));
        }
        if (materialRows.Count == 0)
        {
            materialRows.Add(ReadOnly(
                "Slots",
                "xmodel.materials",
                "None"));
        }
        sections.Add(new InspectorSectionViewModel(
            "Material slots",
            materialRows));

        var boneRows = model.BoneNames
            .Select((bone, index) => (bone.Text, Index: index))
            .Where(value => !string.IsNullOrWhiteSpace(value.Text))
            .Select(value => (InspectorPropertyRowViewModel)ReadOnly(
                $"Bone {value.Index}",
                $"xmodel.boneNames[{value.Index}]",
                value.Text!))
            .ToList();
        if (boneRows.Count == 0)
        {
            boneRows.Add(ReadOnly(
                "Resolved names",
                "xmodel.boneNames",
                "None"));
        }
        sections.Add(new InspectorSectionViewModel(
            "Skeleton · read-only",
            boneRows,
            isExpanded: false));

        string selectionName = selectedAssembly is null
            ? Name
            : $"{Name} · LOD {selectedAssembly.LodIndex}";
        return new InspectorSelectionViewModel(
            selectionName,
            "XMODEL",
            sections,
            "Detailed XModel fields. Skeleton names, hierarchy, and bind pose are currently read-only.");
    }

    private static InspectorReadOnlyPropertyRowViewModel ReadOnly(
        string label,
        string fieldPath,
        string value) => new(label, fieldPath, value);

    private static string AccessText(WorkspaceAssetAccess access) => access switch
    {
        WorkspaceAssetAccess.Editable => "Editable draft",
        WorkspaceAssetAccess.ReadOnly => "Read-only provider",
        WorkspaceAssetAccess.ContentUnavailable => "Content unavailable",
        _ => "Unknown access"
    };

    private static string FormatFloat(float value) =>
        value.ToString("G9", CultureInfo.InvariantCulture);

    private void RequestAssetReferenceSelection(
        InspectorAssetReferencePropertyRowViewModel row) =>
        AssetReferenceSelectionRequested?.Invoke(
            this,
            new AssetReferenceSelectionRequestedEventArgs(row));

    private static string FormatBounds(RenderBounds bounds) =>
        bounds.IsValid
            ? $"({FormatFloat(bounds.Min.X)}, {FormatFloat(bounds.Min.Y)}, {FormatFloat(bounds.Min.Z)}) – " +
              $"({FormatFloat(bounds.Max.X)}, {FormatFloat(bounds.Max.Y)}, {FormatFloat(bounds.Max.Z)})"
            : "Unavailable";

    private sealed record LodAuthoredMaterialSummary(
        int GroupCount,
        int ReadyGroupCount,
        int PassCount,
        IReadOnlyList<int> TechniqueSlots,
        string TechniqueDisplay)
    {
        internal static LodAuthoredMaterialSummary Empty { get; } = new(
            0,
            0,
            0,
            [],
            "No authored camera-color technique selected");

    }
}

public sealed class XModelLodItemViewModel
{
    internal XModelLodItemViewModel(XModelRenderLod lod) =>
        Lod = lod ?? throw new ArgumentNullException(nameof(lod));

    internal XModelRenderLod Lod { get; }

    public int LodIndex => Lod.LodIndex;

    public int TriangleCount => Lod.TriangleCount;

    public int VertexCount => Lod.VertexCount;

    public string DisplayName =>
        $"LOD {LodIndex} · {TriangleCount:N0} tris";

    public override string ToString() => DisplayName;
}

public sealed class XModelLodAssemblyItemViewModel
{
    internal XModelLodAssemblyItemViewModel(XModelLodDraft lod)
    {
        Draft = lod;
        LodIndex = lod.SlotIndex;
        Distance = lod.Distance;
        IsOccupied = lod.IsOccupied;
        IsImported = lod.IsImported;
        IsBaseline = lod.BaselineLod is not null;
        SourceDisplay = lod.IsImported
            ? Path.GetFileName(lod.ImportSource) ?? "Imported geometry"
            : IsBaseline ? "Current XModel geometry" : "Empty slot";
        if (lod.ImportedDocument is { } imported)
        {
            VertexCount = imported.Vertices.Count; TriangleCount = imported.Triangles.Count; MaterialCount = imported.Materials.Count;
        }
        else if (lod.BaselineLod?.ModelSurfs is { } surfaces)
        {
            VertexCount = surfaces.Surfaces.Sum(surface => surface.VertCount); TriangleCount = surfaces.Surfaces.Sum(surface => surface.TriCount); MaterialCount = surfaces.Surfaces.Count;
        }
    }
    internal XModelLodDraft Draft { get; }
    public int LodIndex { get; }
    public float Distance { get; }
    public bool IsOccupied { get; }
    public bool IsImported { get; }
    public bool IsBaseline { get; }
    public int VertexCount { get; }
    public int TriangleCount { get; }
    public int MaterialCount { get; }
    public string SourceDisplay { get; }
    public string Title => $"LOD {LodIndex}";
    public string Detail => IsOccupied ? $"{TriangleCount:N0} tris" : "Empty";
    public string DisplayName => !IsOccupied ? $"LOD {LodIndex} · Empty" : $"LOD {LodIndex} · {TriangleCount:N0} tris · {SourceDisplay}";
    public override string ToString() => DisplayName;
}

public sealed class XModelImportedMaterialMappingItemViewModel
{
    private readonly string? _modelName;
    private readonly XModelExportMaterial _source;
    private readonly IReadOnlyList<AssetValidationIssue> _compilationIssues;
    private readonly XModelMaterialMapping? _mapping;

    internal XModelImportedMaterialMappingItemViewModel(
        string? modelName,
        int materialIndex,
        XModelExportMaterial source,
        IReadOnlyList<AssetValidationIssue> compilationIssues,
        XModelMaterialMapping? mapping)
    {
        _modelName = modelName;
        _source = source;
        _compilationIssues = compilationIssues;
        _mapping = mapping;
        MaterialIndex = materialIndex;
    }

    public int MaterialIndex { get; }
    public string MaterialName => _source.Name;
    public string TargetMaterialName => _source.ImportMaterial is not null &&
        _mapping is { } mapping
            ? XModelAssemblyCompiler.ImportedMaterialName(
                _modelName,
                _source,
                mapping.Material)
            : _mapping?.Material.Info.Name ?? "Automatic material unavailable";

    public string PreviewColor
    {
        get
        {
            XModelImportMaterial? imported = _source.ImportMaterial;
            if (imported is null)
                return "#FFFFFFFF";
            Vector4 factor = imported.BaseColorFactor;
            XModelImportImage? image = imported.BaseColorImage;
            float imageRed = image is { RgbaBytes.Count: >= 4 } ? image.RgbaBytes[0] / 255f : 1f;
            float imageGreen = image is { RgbaBytes.Count: >= 4 } ? image.RgbaBytes[1] / 255f : 1f;
            float imageBlue = image is { RgbaBytes.Count: >= 4 } ? image.RgbaBytes[2] / 255f : 1f;
            float imageAlpha = image is { RgbaBytes.Count: >= 4 } ? image.RgbaBytes[3] / 255f : 1f;
            byte red = (byte)MathF.Round(Math.Clamp(factor.X * imageRed, 0f, 1f) * 255f);
            byte green = (byte)MathF.Round(Math.Clamp(factor.Y * imageGreen, 0f, 1f) * 255f);
            byte blue = (byte)MathF.Round(Math.Clamp(factor.Z * imageBlue, 0f, 1f) * 255f);
            byte alpha = imported.AlphaMode == XModelImportAlphaMode.Opaque
                ? byte.MaxValue
                : (byte)MathF.Round(Math.Clamp(factor.W * imageAlpha, 0f, 1f) * 255f);
            return $"#{alpha:X2}{red:X2}{green:X2}{blue:X2}";
        }
    }
    public string ImportStatus => _source.ImportMaterial is null
        ? "Existing material mapping"
        : _compilationIssues.FirstOrDefault(issue =>
            issue.Severity == AssetValidationSeverity.Error) is { } error
            ? $"Blocked · {error.Message}"
        : _mapping is null
            ? "Blocked · no compatible IW4 render template found"
            : _source.ImportMaterial.Warnings.Count == 0
                ? "Ready · owned Material + Image"
                : $"Ready with {_source.ImportMaterial.Warnings.Count} warning(s)";
    public string WarningsText => string.Join(
        " ",
        _compilationIssues.Select(issue => issue.Message)
            .Concat(_source.ImportMaterial?.Warnings ?? []));
    public bool HasWarnings => _compilationIssues.Count > 0 ||
        _source.ImportMaterial?.Warnings.Count > 0;
}
