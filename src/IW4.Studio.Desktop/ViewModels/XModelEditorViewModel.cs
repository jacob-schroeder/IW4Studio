using System.Globalization;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Render.SceneBuilding;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Read-only preview projection of the current session-owned XModel draft.
/// Authoring controls are deliberately scaffolded but disabled until a later
/// change defines supported XModel mutations.
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
    private XModelRenderScene? _scene;
    private IReadOnlyList<XModelLodItemViewModel> _lods = [];
    private XModelLodItemViewModel? _selectedLod;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _buildDiagnostics = [];
    private string _statusMessage = string.Empty;
    private string? _buildFailure;
    private int _rendererLodIndex = -1;
    private XModelViewerUploadResult? _rendererUploadResult;
    private string? _rendererFailure;
    private bool _isWireframeEnabled;
    private bool _showBoneTags;
    private bool _disposed;

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

    public XModelLodItemViewModel? SelectedLod
    {
        get => _selectedLod;
        set
        {
            if (value is not null && !Lods.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetProperty(ref _selectedLod, value))
                return;

            ResetRendererStatus();
            OnPropertyChanged(nameof(SelectedLodIndex));
            OnPropertyChanged(nameof(MaterialExecutionBadge));
            RefreshInspector();
            OnPropertyChanged(nameof(EditorProperties));
            OnPropertyChanged(nameof(Diagnostics));
            RefreshStatusMessage();
            PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedLodIndex => SelectedLod?.LodIndex ?? -1;

    public bool IsWireframeEnabled
    {
        get => _isWireframeEnabled;
        set => SetProperty(ref _isWireframeEnabled, value);
    }

    public bool ShowBoneTags
    {
        get => _showBoneTags;
        set => SetProperty(ref _showBoneTags, value);
    }

    public bool CanRevert => IsEditable && _session.HasUnsavedChanges;

    public bool CanApply => false;

    public bool HasUnappliedChanges => false;

    public string PropertySectionName => "XModel preview";

    public string MaterialExecutionBadge
    {
        get
        {
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
            LodAuthoredMaterialSummary summary =
                SummarizeAuthoredMaterials(SelectedLod?.Lod);
            return
            [
                new("Material execution", "Authored normal-camera pass group"),
                new("Selected LOD", SelectedLod?.DisplayName ?? "None"),
                new("Triangles", SelectedLod?.TriangleCount.ToString("N0") ?? "0"),
                new("Vertices", SelectedLod?.VertexCount.ToString("N0") ?? "0"),
                new("Selected technique", summary.TechniqueDisplay),
                new("Authored passes", summary.PassCount.ToString("N0")),
                new("Scene-ready groups", $"{summary.ReadyGroupCount:N0} / {summary.GroupCount:N0}"),
                new("Scene-blocked groups", summary.BlockedGroupCount.ToString("N0")),
                new("OpenGL-executable groups", RendererExecutableGroupsText()),
                new("OpenGL-blocked groups", RendererBlockedGroupsText()),
                new("Renderer status", RendererStatusText())
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

        bool changed = _session.Revert();
        CaptureAndBuild();
        StatusMessage = changed
            ? $"Reverted the XModel draft. {StatusMessage}"
            : $"The XModel draft already matched its authored baseline. {StatusMessage}";
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
        int? previousLodIndex = SelectedLod?.LodIndex;
        XModelDraft draft = _session.OpenDraft<XModelDraft>();
        _model = draft.Model;
        ResetRendererStatus();

        XModelRenderScene? scene = null;
        string? buildFailure = null;
        try
        {
            MapRenderAssetSource source = CreateAssetSource(
                _session.Workspace);
            scene = _sceneBuilder.Build(
                _model,
                source,
                _imagePayloads);
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

        var issues = new List<AssetValidationIssue>(
            _session.Validation.Issues);
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

    private void RefreshInspector(bool notify = true)
    {
        _inspectorSelection = _model is null
            ? null
            : CreateInspectorSelection(_model, SelectedLod?.Lod);
        if (notify)
            OnPropertyChanged(nameof(InspectorSelection));
    }

    private void NotifyProjectionChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Scene));
        OnPropertyChanged(nameof(Lods));
        OnPropertyChanged(nameof(SelectedLod));
        OnPropertyChanged(nameof(SelectedLodIndex));
        OnPropertyChanged(nameof(MaterialExecutionBadge));
        OnPropertyChanged(nameof(InspectorSelection));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(HasUnappliedChanges));
    }

    private void RefreshStatusMessage()
    {
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
        _diagnostics = _buildDiagnostics;
    }

    private void RebuildDiagnostics()
    {
        var issues = new List<AssetValidationIssue>(_buildDiagnostics);
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

    private string RendererExecutableGroupsText()
    {
        if (_rendererUploadResult is not { } upload)
            return _rendererFailure is null ? "Pending" : "Unavailable";

        int total = upload.ExecutableGroupCount + upload.BlockedGroupCount;
        return $"{upload.ExecutableGroupCount:N0} / {total:N0}";
    }

    private string RendererBlockedGroupsText() =>
        _rendererUploadResult is { } upload
            ? upload.BlockedGroupCount.ToString("N0")
            : _rendererFailure is null
                ? "Pending"
                : "Unavailable";

    private string RendererStatusText() =>
        _rendererFailure is not null
            ? $"Blocked · {_rendererFailure}"
            : _rendererUploadResult is not null
                ? "Preflight complete"
                : "Awaiting renderer preflight";

    private static MapRenderAssetSource CreateAssetSource(
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
        return new MapRenderAssetSource(
            target.Header,
            target.Context.Blocks,
            target.Context.AssetPool,
            target.Context.AssetRuntimeLifecycle.GfxWorld,
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
        XModelRenderLod? selectedLod)
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
            "Resolved bone names",
            boneRows,
            isExpanded: false));

        return new InspectorSelectionViewModel(
            selectedLod is null ? Name : $"{Name} · LOD {selectedLod.LodIndex}",
            "XMODEL",
            sections,
            "Read-only model metadata and authored normal-camera material execution status.");
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

    private static string FormatBounds(MapRenderBounds bounds) =>
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

        internal int BlockedGroupCount => GroupCount - ReadyGroupCount;
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
