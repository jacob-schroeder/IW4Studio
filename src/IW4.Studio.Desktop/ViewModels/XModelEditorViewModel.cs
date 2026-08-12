using System.Globalization;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.SceneBuilding;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Read-only checkpoint projection of the current session-owned XModel draft.
/// Authoring controls are deliberately scaffolded but disabled until a later
/// checkpoint defines supported XModel mutations.
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
    private readonly MapSceneBuilder _sceneBuilder = new();
    private XModelAsset? _model;
    private XModelRenderScene? _scene;
    private IReadOnlyList<XModelLodItemViewModel> _lods = [];
    private XModelLodItemViewModel? _selectedLod;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _statusMessage = string.Empty;
    private bool _isWireframeEnabled;
    private bool _disposed;

    public XModelEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.XModel)
        {
            throw new InvalidDataException(
                "The XModel view model can host only XModel editor sessions.");
        }

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

            OnPropertyChanged(nameof(SelectedLodIndex));
            RefreshInspector();
            OnPropertyChanged(nameof(EditorProperties));
            PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedLodIndex => SelectedLod?.LodIndex ?? -1;

    public bool IsWireframeEnabled
    {
        get => _isWireframeEnabled;
        set => SetProperty(ref _isWireframeEnabled, value);
    }

    public bool CanRevert => IsEditable && _session.HasUnsavedChanges;

    public bool CanApply => false;

    public bool HasUnappliedChanges => false;

    public string PropertySectionName => "XModel preview";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Checkpoint", "Material-slot shading"),
        new("Selected LOD", SelectedLod?.DisplayName ?? "None"),
        new("Triangles", SelectedLod?.TriangleCount.ToString("N0") ?? "0"),
        new("Vertices", SelectedLod?.VertexCount.ToString("N0") ?? "0"),
        new("Texture fidelity", "Not applied")
    ];

    public InspectorSelectionViewModel? InspectorSelection =>
        _inspectorSelection;

    public IReadOnlyList<AssetValidationIssue> Diagnostics => _diagnostics;

    public event EventHandler? PropertiesRevealRequested;

    public void RevertDraft()
    {
        if (!IsEditable)
            return;
        if (!CanRevert)
            return;

        bool changed = _session.Revert();
        CaptureAndBuild();
        StatusMessage = changed
            ? "Reverted the XModel draft and rebuilt the material-slot preview."
            : "The XModel draft already matched its authored baseline.";
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

        XModelRenderScene? scene = null;
        string? buildFailure = null;
        try
        {
            scene = _sceneBuilder.BuildXModel(_model);
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
            issues.AddRange(scene.Diagnostics.Select(message =>
                new AssetValidationIssue(
                    "xmodel.preview",
                    message,
                    AssetValidationSeverity.Warning)));
        }
        if (!string.IsNullOrWhiteSpace(buildFailure))
        {
            issues.Add(new AssetValidationIssue(
                "xmodel.preview",
                buildFailure,
                AssetValidationSeverity.Error));
        }
        _diagnostics = Array.AsReadOnly(issues
            .GroupBy(issue =>
                (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());

        StatusMessage = buildFailure is not null
            ? $"Preview unavailable: {buildFailure}"
            : scene?.Lods.Count > 0
                ? $"{AccessText(Mode)} · Checkpoint 1 material-slot shading; textures are not applied."
                : $"{AccessText(Mode)} · No renderable LOD geometry is available.";
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
        OnPropertyChanged(nameof(InspectorSelection));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(HasUnappliedChanges));
    }

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
            "Read-only model metadata for the material-slot shaded checkpoint preview.");
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
