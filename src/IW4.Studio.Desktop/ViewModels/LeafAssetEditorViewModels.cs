using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class ComWorldEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private ComWorldDraft? _draft;

    public ComWorldEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.ComMap)
            throw new InvalidDataException("The ComMap view model can host only ComMap sessions.");
        if (IsEditable)
            _draft = session.OpenDraft<ComWorldDraft>();
    }

    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int PrimaryLightCount => _draft?.PrimaryLights.Count ?? 0;
    public string StatusMessage => IsEditable
        ? "Detached ComMap light-table draft. Primary-light parameters and source XStrings are authored; renderer light-grid state is not."
        : "ComMap content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;

    public void SetIsInUse(int value) => Apply(draft => draft.SetIsInUse(value));
    public void ReplacePrimaryLights(IEnumerable<ComPrimaryLightBuildData> values) { if (!IsEditable) return; ComPrimaryLightBuildData[] copy = values.ToArray(); Apply(draft => draft.ReplacePrimaryLights(copy)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<ComWorldDraft>(); OnPropertyChanged(nameof(PrimaryLightCount)); OnPropertyChanged(nameof(Diagnostics)); }
    private void Apply(Action<ComWorldDraft> mutation) { if (!IsEditable) return; _session.Apply(mutation); _draft = _session.ReadDraft<ComWorldDraft>(); OnPropertyChanged(nameof(PrimaryLightCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class GameWorldMpEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private GameWorldMpDraft? _draft;
    public GameWorldMpEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.GameMapMp) throw new InvalidDataException("The GameMapMp view model can host only GameMapMp sessions."); if (IsEditable) _draft = session.OpenDraft<GameWorldMpDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int GlassPieceCount => _draft?.GlassData?.Pieces.Count ?? 0;
    public int GlassNameCount => _draft?.GlassData?.Names.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached GameMapMp draft. The complete glass root, arrays, source strings, script indices, and pad bytes are authored." : "GameMapMp content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetGlassData(GGlassDataBuildData? value) { if (!IsEditable) return; _session.Apply<GameWorldMpDraft>(draft => draft.SetGlassData(value)); _draft = _session.ReadDraft<GameWorldMpDraft>(); OnPropertyChanged(nameof(GlassPieceCount)); OnPropertyChanged(nameof(GlassNameCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<GameWorldMpDraft>(); OnPropertyChanged(nameof(GlassPieceCount)); OnPropertyChanged(nameof(GlassNameCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class GameWorldSpEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private GameWorldSpDraft? _draft;
    public GameWorldSpEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.GameMapSp)
            throw new InvalidDataException("The GameMapSp view model can host only GameMapSp sessions.");
        if (IsEditable) _draft = session.OpenDraft<GameWorldSpDraft>();
    }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int PathNodeCount => _draft?.Data.Path.Nodes.Count ?? 0;
    public int VehicleSegmentCount => _draft?.Data.VehicleTrack.Segments.Count ?? 0;
    public int GlassPieceCount => _draft?.Data.GlassData?.GlassPieces.Count ?? 0;
    public string StatusMessage => IsEditable
        ? "Detached GameMapSp draft. Path nodes/trees, vehicle-track topology, glass data, source strings, script indices, and stream placement are authored. Runtime path-base and search-pointer caches are excluded."
        : "GameMapSp content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void Replace(GameWorldSpBuildData value)
    {
        if (!IsEditable) return;
        ArgumentNullException.ThrowIfNull(value);
        _session.Apply<GameWorldSpDraft>(draft => draft.Replace(value));
        _draft = _session.ReadDraft<GameWorldSpDraft>();
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(PathNodeCount)); OnPropertyChanged(nameof(VehicleSegmentCount)); OnPropertyChanged(nameof(GlassPieceCount)); OnPropertyChanged(nameof(Diagnostics));
    }
    public void RevertDraft()
    {
        if (!IsEditable) return;
        _session.Revert(); _draft = _session.ReadDraft<GameWorldSpDraft>();
        OnPropertyChanged(nameof(PathNodeCount)); OnPropertyChanged(nameof(VehicleSegmentCount)); OnPropertyChanged(nameof(GlassPieceCount)); OnPropertyChanged(nameof(Diagnostics));
    }
}

public sealed class FxWorldEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private FxWorldDraft? _draft;
    public FxWorldEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.FxMap) throw new InvalidDataException("The FxMap view model can host only FxMap sessions."); if (IsEditable) _draft = session.OpenDraft<FxWorldDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int DefinitionCount => _draft?.Data.GlassSystem.Defs.Count ?? 0;
    public int PieceLimit => _draft?.Data.GlassSystem.PieceStates.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached FxMap draft. The full FxGlassSystem graph preserves LARGE/RUNTIME stream placement and symbolic material/physics links." : "FxMap content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void Replace(FxWorldBuildData value) { if (!IsEditable) return; ArgumentNullException.ThrowIfNull(value); _session.Apply<FxWorldDraft>(draft => draft.Replace(value)); _draft = _session.ReadDraft<FxWorldDraft>(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(DefinitionCount)); OnPropertyChanged(nameof(PieceLimit)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<FxWorldDraft>(); OnPropertyChanged(nameof(DefinitionCount)); OnPropertyChanged(nameof(PieceLimit)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class GfxWorldEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private GfxWorldDraft? _draft;
    public GfxWorldEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.GfxMap) throw new InvalidDataException("The GfxMap view model can host only GfxMap sessions."); if (IsEditable) _draft = session.OpenDraft<GfxWorldDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Definition.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int SurfaceCount => _draft?.Data.Definition.SurfaceCount ?? 0;
    public int CellCount => _draft?.Data.Definition.DpvsPlanes.CellCount ?? 0;
    public int ModelCount => _draft?.Data.Definition.ModelCount ?? 0;
    public string StatusMessage => IsEditable ? "Detached GfxMap draft. Authored world geometry, lights, DPVS tables, binary buffers, and symbolic image/material/model links are preserved; renderer-created texture and visibility buffers are rebuilt zero-filled." : "GfxMap content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void Replace(GfxWorldBuildData value) { if (!IsEditable) return; ArgumentNullException.ThrowIfNull(value); _session.Apply<GfxWorldDraft>(draft => draft.Replace(value)); _draft = _session.ReadDraft<GfxWorldDraft>(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(SurfaceCount)); OnPropertyChanged(nameof(CellCount)); OnPropertyChanged(nameof(ModelCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<GfxWorldDraft>(); OnPropertyChanged(nameof(SurfaceCount)); OnPropertyChanged(nameof(CellCount)); OnPropertyChanged(nameof(ModelCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class ClipMapEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private ClipMapDraft? _draft;

    public ClipMapEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType is not (IW4.FastFiles.Zone.XAssetType.ColMapSp or IW4.FastFiles.Zone.XAssetType.ColMapMp))
            throw new InvalidDataException("The ColMap view model can host only ColMapSp or ColMapMp sessions.");
        if (IsEditable)
            _draft = session.OpenDraft<ClipMapDraft>();
    }

    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Definition.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int PlaneCount => _draft?.Data.Definition.Planes.Count ?? 0;
    public int BrushCount => _draft?.Data.Definition.Brushes.Count ?? 0;
    public int DynamicEntityCount => _draft?.Data.Definition.DynEntDefList.Sum(list => list.Count) ?? 0;
    public string StatusMessage => IsEditable
        ? "Detached ColMap draft. Collision geometry, plane aliases, topology, and symbolic model/effect/physics/MapEnts links are authored; dynamic runtime caches are rebuilt as zero-filled allocations."
        : "ColMap content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;

    public void Replace(ClipMapBuildData value)
    {
        if (!IsEditable) return;
        ArgumentNullException.ThrowIfNull(value);
        _session.Apply<ClipMapDraft>(draft => draft.Replace(value));
        _draft = _session.ReadDraft<ClipMapDraft>();
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(PlaneCount)); OnPropertyChanged(nameof(BrushCount)); OnPropertyChanged(nameof(DynamicEntityCount)); OnPropertyChanged(nameof(Diagnostics));
    }

    public void RevertDraft()
    {
        if (!IsEditable) return;
        _session.Revert();
        _draft = _session.ReadDraft<ClipMapDraft>();
        OnPropertyChanged(nameof(PlaneCount)); OnPropertyChanged(nameof(BrushCount)); OnPropertyChanged(nameof(DynamicEntityCount)); OnPropertyChanged(nameof(Diagnostics));
    }
}

public sealed class VehicleEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private VehicleDraft? _draft;
    public VehicleEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Vehicle) throw new InvalidDataException("The Vehicle view model can host only Vehicle sessions."); if (IsEditable) _draft = session.OpenDraft<VehicleDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public int VehicleType => _draft?.Data.Type ?? 0;
    public int SurfaceSoundCount => _draft?.Data.SurfaceSoundAliases.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached Vehicle draft. Physics/tuning groups, source strings, nested sound cells, fixed tags, and symbolic asset links are authored." : "Vehicle content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void Replace(VehicleBuildData value) { if (!IsEditable) return; ArgumentNullException.ThrowIfNull(value); _session.Apply<VehicleDraft>(draft => draft.Replace(value)); _draft = _session.ReadDraft<VehicleDraft>(); OnPropertyChanged(nameof(VehicleType)); OnPropertyChanged(nameof(SurfaceSoundCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<VehicleDraft>(); OnPropertyChanged(nameof(VehicleType)); OnPropertyChanged(nameof(SurfaceSoundCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class WeaponEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private WeaponDraft? _draft;
    public WeaponEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Weapon) throw new InvalidDataException("The Weapon view model can host only Weapon sessions."); if (IsEditable) _draft = session.OpenDraft<WeaponDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Data.Variant.InternalName ?? _session.Entry.OriginalName ?? string.Empty;
    public int GunModelCount => _draft?.Data.References.GunModels.Count ?? 0;
    public int SoundAliasCount => _draft?.Data.Definition.SoundAliasNames.Count ?? 0;
    public int NoteTrackCount => _draft?.Data.Definition.NoteTrackMaps.SoundMapKeys.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached Weapon draft. Variant settings, organized definition groups, fixed note-track tables, nested sound cells, graph arrays, and symbolic resource links are authored." : "Weapon content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void Replace(WeaponBuildData value) { if (!IsEditable) return; ArgumentNullException.ThrowIfNull(value); _session.Apply<WeaponDraft>(draft => draft.Replace(value)); _draft = _session.ReadDraft<WeaponDraft>(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(GunModelCount)); OnPropertyChanged(nameof(SoundAliasCount)); OnPropertyChanged(nameof(NoteTrackCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<WeaponDraft>(); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(GunModelCount)); OnPropertyChanged(nameof(SoundAliasCount)); OnPropertyChanged(nameof(NoteTrackCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

/// <summary>Typed PhysPreset command host.  Mutations always flow through
/// the shared document draft, never through a view-local asset copy.</summary>
public sealed class PhysPresetEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private PhysPresetDraft? _draft;
    public PhysPresetEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.PhysPreset) throw new InvalidDataException("The PhysPreset view model can host only PhysPreset sessions.");
        if (IsEditable) _draft = session.OpenDraft<PhysPresetDraft>();
    }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public PhysPresetDraft? Draft => _draft;
    public string StatusMessage => IsEditable ? "Detached PhysPreset draft. Name is locked as serialized identity; exact scalar bits and flags are editable." : "PhysPreset content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetMass(float value) => Apply(draft => draft.SetMass(value));
    public void SetSndAliasPrefix(string? value) => Apply(draft => draft.SetSndAliasPrefix(value));
    public void SetType(int value) => Apply(draft => draft.SetType(value));
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<PhysPresetDraft>(); OnPropertyChanged(nameof(Draft)); OnPropertyChanged(nameof(Diagnostics)); }
    private void Apply(Action<PhysPresetDraft> mutation) { if (!IsEditable) return; _session.Apply(mutation); _draft = _session.ReadDraft<PhysPresetDraft>(); OnPropertyChanged(nameof(Draft)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class PhysCollmapEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private PhysCollmapDraft? _draft;
    public PhysCollmapEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.PhysCollmap) throw new InvalidDataException("The PhysCollmap view model can host only PhysCollmap sessions."); if (IsEditable) _draft = session.OpenDraft<PhysCollmapDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public int GeometryCount => _draft?.Data.Geoms.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached PhysCollmap draft. Nested brush, side, plane, and mass data are authored; name remains the serialized row identity." : "PhysCollmap content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceGeoms(IEnumerable<PhysGeomBuildData> value) { if (!IsEditable) return; PhysGeomBuildData[] copy = value.ToArray(); _session.Apply<PhysCollmapDraft>(draft => draft.ReplaceGeoms(copy)); _draft = _session.ReadDraft<PhysCollmapDraft>(); OnPropertyChanged(nameof(GeometryCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void SetMass(Float3BuildData centerOfMass, Float3BuildData momentsOfInertia, Float3BuildData productsOfInertia) { if (!IsEditable) return; _session.Apply<PhysCollmapDraft>(draft => draft.SetMass(centerOfMass, momentsOfInertia, productsOfInertia)); _draft = _session.ReadDraft<PhysCollmapDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<PhysCollmapDraft>(); OnPropertyChanged(nameof(GeometryCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class XAnimEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private XAnimDraft? _draft;
    public XAnimEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.XAnim) throw new InvalidDataException("The XAnim view model can host only XAnim sessions."); if (IsEditable) _draft = session.OpenDraft<XAnimDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public ushort FrameCount => _draft?.Data.NumFrames ?? 0; public string StatusMessage => IsEditable ? "Detached XAnim draft. Packed streams, notification tables, and static/dynamic delta payloads are authored; name remains the serialized row identity." : "XAnim content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetRates(float framerate, float frequency) { if (!IsEditable) return; _session.Apply<XAnimDraft>(draft => draft.SetRates(framerate, frequency)); _draft = _session.ReadDraft<XAnimDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<XAnimDraft>(); OnPropertyChanged(nameof(FrameCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class XModelEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private XModelDraft? _draft;
    public XModelEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.XModel) throw new InvalidDataException("The XModel view model can host only XModel sessions."); if (IsEditable) _draft = session.OpenDraft<XModelDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public int LodCount => _draft?.Data.Lods.Count ?? 0; public string StatusMessage => IsEditable ? "Detached XModel draft. Bones, nested surfaces, collision data, and symbolic asset links are preserved; runtime surface pointers and registration fixups are rebuilt on load." : "XModel content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetScale(float value) { if (!IsEditable) return; _session.Apply<XModelDraft>(draft => draft.SetScale(value)); _draft = _session.ReadDraft<XModelDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<XModelDraft>(); OnPropertyChanged(nameof(LodCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class SoundEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private SoundDraft? _draft;
    public SoundEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Sound) throw new InvalidDataException("The Sound view model can host only Sound sessions."); if (IsEditable) _draft = session.OpenDraft<SoundDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string AliasName => _draft?.Data.AliasName ?? _session.Entry.OriginalName ?? string.Empty; public int AliasCount => _draft?.Data.Aliases.Count ?? 0; public string StatusMessage => IsEditable ? "Detached Sound draft. Alias values, speaker maps, and loaded/streamed union arms are authored; linked sound and curve assets remain symbolic rows." : "Sound content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceAliases(IEnumerable<SoundAliasBuildData> aliases) { if (!IsEditable) return; SoundAliasBuildData[] copy = aliases.ToArray(); _session.Apply<SoundDraft>(draft => draft.ReplaceAliases(copy)); _draft = _session.ReadDraft<SoundDraft>(); OnPropertyChanged(nameof(AliasCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<SoundDraft>(); OnPropertyChanged(nameof(AliasCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class FxEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private FxDraft? _draft;
    public FxEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Fx) throw new InvalidDataException("The Fx view model can host only Fx sessions."); if (IsEditable) _draft = session.OpenDraft<FxDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public int ElementCount => _draft?.Data.Elements.Count ?? 0; public string StatusMessage => IsEditable ? "Detached Fx draft. Every visual and extended union arm is retained; material, model, sound, and child-Fx links remain symbolic." : "Fx content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceElements(IEnumerable<FxElementBuildData> values) { if (!IsEditable) return; FxElementBuildData[] copy = values.ToArray(); _session.Apply<FxDraft>(draft => draft.ReplaceElements(copy)); _draft = _session.ReadDraft<FxDraft>(); OnPropertyChanged(nameof(ElementCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<FxDraft>(); OnPropertyChanged(nameof(ElementCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class ImpactFxEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private ImpactFxDraft? _draft;
    public ImpactFxEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.ImpactFx) throw new InvalidDataException("The ImpactFx view model can host only ImpactFx sessions."); if (IsEditable) _draft = session.OpenDraft<ImpactFxDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public int EntryCount => _draft?.Data.Entries.Count ?? 0; public string StatusMessage => IsEditable ? "Detached ImpactFx matrix draft. All 15 ordered entries and their null/external Fx slots are retained." : "ImpactFx content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceEntries(IEnumerable<FxImpactEntryBuildData> values) { if (!IsEditable) return; FxImpactEntryBuildData[] copy = values.ToArray(); _session.Apply<ImpactFxDraft>(draft => draft.ReplaceEntries(copy)); _draft = _session.ReadDraft<ImpactFxDraft>(); OnPropertyChanged(nameof(EntryCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<ImpactFxDraft>(); OnPropertyChanged(nameof(EntryCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

/// <summary>Typed fixed-slot SndCurve command host; all sixteen slots remain
/// represented, including those beyond KnotCount.</summary>
public sealed class SndCurveEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private SndCurveDraft? _draft;
    public SndCurveEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.SndCurve) throw new InvalidDataException("The SndCurve view model can host only SndCurve sessions.");
        if (IsEditable) _draft = session.OpenDraft<SndCurveDraft>();
    }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Filename => _draft?.Filename ?? _session.Entry.OriginalName ?? string.Empty;
    public IReadOnlyList<SndCurveKnotBuildData> Knots => _draft?.Knots ?? [];
    public ushort KnotCount => _draft?.KnotCount ?? 0;
    public string StatusMessage => IsEditable ? "Detached fixed-slot SndCurve draft. Filename is locked as serialized identity." : "SndCurve content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetKnot(int index, SndCurveKnotBuildData value) { if (!IsEditable) return; _session.Apply<SndCurveDraft>(draft => draft.SetKnot(index, value)); _draft = _session.ReadDraft<SndCurveDraft>(); OnPropertyChanged(nameof(Knots)); OnPropertyChanged(nameof(Diagnostics)); }
    public void SetKnotCount(ushort value) { if (!IsEditable) return; _session.Apply<SndCurveDraft>(draft => draft.SetKnotCount(value)); _draft = _session.ReadDraft<SndCurveDraft>(); OnPropertyChanged(nameof(KnotCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<SndCurveDraft>(); OnPropertyChanged(nameof(Knots)); OnPropertyChanged(nameof(KnotCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

/// <summary>Typed LeaderboardDef command host; IDs, ordered columns, stored
/// padding, enum values, and hashes are never reinterpreted by the UI.</summary>
public sealed class LeaderboardEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private LeaderboardDraft? _draft;
    public LeaderboardEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.LeaderboardDef) throw new InvalidDataException("The Leaderboard view model can host only LeaderboardDef sessions.");
        if (IsEditable) _draft = session.OpenDraft<LeaderboardDraft>();
    }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public IReadOnlyList<LeaderboardColumnDraft> Columns => _draft?.Columns ?? [];
    public string StatusMessage => IsEditable ? "Detached LeaderboardDef draft. Name is locked as serialized identity; column order, padding, and stored values are preserved." : "LeaderboardDef content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetIds(int id, int xpColumnId, int prestigeColumnId) { if (!IsEditable) return; _session.Apply<LeaderboardDraft>(draft => draft.SetIds(id, xpColumnId, prestigeColumnId)); _draft = _session.ReadDraft<LeaderboardDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void SetColumn(int index, LeaderboardColumnDraft value) { if (!IsEditable) return; _session.Apply<LeaderboardDraft>(draft => draft.SetColumn(index, value)); _draft = _session.ReadDraft<LeaderboardDraft>(); OnPropertyChanged(nameof(Columns)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<LeaderboardDraft>(); OnPropertyChanged(nameof(Columns)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class TracerEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private TracerDraft? _draft;
    public TracerEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Tracer) throw new InvalidDataException("The Tracer view model can host only Tracer sessions."); if (IsEditable) _draft = session.OpenDraft<TracerDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty; public IReadOnlyList<TracerColorBuildData> Colors => _draft?.Colors ?? [];
    public string StatusMessage => IsEditable ? "Detached Tracer draft. Material is a symbolic external reference; it is never inlined into this row." : "Tracer content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetColor(int index, TracerColorBuildData value) { if (!IsEditable) return; _session.Apply<TracerDraft>(draft => draft.SetColor(index, value)); _draft = _session.ReadDraft<TracerDraft>(); OnPropertyChanged(nameof(Colors)); OnPropertyChanged(nameof(Diagnostics)); }
    public void SetMaterialReference(SymbolicXAssetReference? value) { if (!IsEditable) return; _session.Apply<TracerDraft>(draft => draft.SetMaterialReference(value)); _draft = _session.ReadDraft<TracerDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<TracerDraft>(); OnPropertyChanged(nameof(Colors)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class LightDefEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private LightDefDraft? _draft;
    public LightDefEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.LightDef) throw new InvalidDataException("The LightDef view model can host only LightDef sessions."); if (IsEditable) _draft = session.OpenDraft<LightDefDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public string StatusMessage => IsEditable ? "Detached LightDef draft. Image is a symbolic external reference; sampler, lmap lookup, and preserved padding remain authored." : "LightDef content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetSamplerState(byte value) { if (!IsEditable) return; _session.Apply<LightDefDraft>(draft => draft.SetSamplerState(value)); _draft = _session.ReadDraft<LightDefDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void SetImageReference(SymbolicXAssetReference? value) { if (!IsEditable) return; _session.Apply<LightDefDraft>(draft => draft.SetImageReference(value)); _draft = _session.ReadDraft<LightDefDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<LightDefDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class MapEntsEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private MapEntsDraft? _draft;
    public MapEntsEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.MapEnts) throw new InvalidDataException("The MapEnts view model can host only MapEnts sessions."); if (IsEditable) _draft = session.OpenDraft<MapEntsDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty; public int EntityByteCount => _draft?.GetEntityStringBytesCopy().Length ?? 0; public string StatusMessage => IsEditable ? "Detached MapEnts draft. Exact entity bytes are serialization authority; trigger/stage tables are structured values." : "MapEnts content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceEntityBytes(ReadOnlySpan<byte> value) { if (!IsEditable) return; byte[] copy = value.ToArray(); _session.Apply<MapEntsDraft>(draft => draft.ReplaceEntityStringBytes(copy)); _draft = _session.ReadDraft<MapEntsDraft>(); OnPropertyChanged(nameof(EntityByteCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<MapEntsDraft>(); OnPropertyChanged(nameof(EntityByteCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class AddonMapEntsEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private AddonMapEntsDraft? _draft;
    public AddonMapEntsEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.AddonMapEnts) throw new InvalidDataException("The AddonMapEnts view model can host only AddonMapEnts sessions."); if (IsEditable) _draft = session.OpenDraft<AddonMapEntsDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty; public int EntityByteCount => _draft?.GetEntityStringBytesCopy().Length ?? 0; public string StatusMessage => IsEditable ? "Detached AddonMapEnts draft. Exact entity bytes are serialization authority; no MapEnts Stage tail exists." : "AddonMapEnts content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceEntityBytes(ReadOnlySpan<byte> value) { if (!IsEditable) return; byte[] copy = value.ToArray(); _session.Apply<AddonMapEntsDraft>(draft => draft.ReplaceEntityStringBytes(copy)); _draft = _session.ReadDraft<AddonMapEntsDraft>(); OnPropertyChanged(nameof(EntityByteCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<AddonMapEntsDraft>(); OnPropertyChanged(nameof(EntityByteCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class BinaryResourceEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    public BinaryResourceEditorViewModel(AssetEditorSession session, IW4.FastFiles.Zone.XAssetType expected) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != expected) throw new InvalidDataException("The binary-resource editor was opened for the wrong asset type."); if (IsEditable) Open(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public string Name => _session.Entry.OriginalName ?? string.Empty;
    public string StatusMessage => IsEditable ? "Detached binary payload editor. Opaque bytes are preserved exactly; no GPU/audio runtime handle is exposed." : "Binary resource content is read-only or unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    private void Open() { switch (_session.Entry.AssetType) { case IW4.FastFiles.Zone.XAssetType.PixelShader: _session.OpenDraft<MaterialShaderDraft>(); break; case IW4.FastFiles.Zone.XAssetType.VertexShader: _session.OpenDraft<MaterialShaderDraft>(); break; case IW4.FastFiles.Zone.XAssetType.Image: _session.OpenDraft<GfxImageDraft>(); break; case IW4.FastFiles.Zone.XAssetType.LoadedSound: _session.OpenDraft<LoadedSoundDraft>(); break; } }
}

public sealed class FontEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private FontDraft? _draft;
    public FontEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Font) throw new InvalidDataException("The Font view model can host only Font sessions."); if (IsEditable) _draft = session.OpenDraft<FontDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty; public int GlyphCount => _draft?.Glyphs.Count ?? 0; public string StatusMessage => IsEditable ? "Detached Font draft. Glyph order and metrics are authored; material links remain symbolic external references." : "Font content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetGlyph(int index, FontGlyphBuildData value) { if (!IsEditable) return; _session.Apply<FontDraft>(draft => draft.SetGlyph(index, value)); _draft = _session.ReadDraft<FontDraft>(); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<FontDraft>(); OnPropertyChanged(nameof(GlyphCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class TechniqueSetEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private TechniqueSetDraft? _draft;
    public TechniqueSetEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Techset) throw new InvalidDataException("The Techset view model can host only Techset sessions."); if (IsEditable) _draft = session.OpenDraft<TechniqueSetDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty; public int OccupiedTechniqueCount => _draft?.TechniqueSlots.Count(slot => slot is not null) ?? 0; public string StatusMessage => IsEditable ? "Detached Techset graph. Shader links are symbolic external references; literals and routing remain structured authored data." : "Techset content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void SetTechniqueSlot(int index, TechniqueBuildData? value) { if (!IsEditable) return; _session.Apply<TechniqueSetDraft>(draft => draft.SetTechniqueSlot(index, value)); _draft = _session.ReadDraft<TechniqueSetDraft>(); OnPropertyChanged(nameof(OccupiedTechniqueCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<TechniqueSetDraft>(); OnPropertyChanged(nameof(OccupiedTechniqueCount)); OnPropertyChanged(nameof(Diagnostics)); }
}

public sealed class MaterialEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session; private MaterialDraft? _draft;
    public MaterialEditorViewModel(AssetEditorSession session) { _session = session ?? throw new ArgumentNullException(nameof(session)); if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Material) throw new InvalidDataException("The Material view model can host only Material sessions."); if (IsEditable) _draft = session.OpenDraft<MaterialDraft>(); }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable; public string Name => _draft?.Data.Name ?? _session.Entry.OriginalName ?? string.Empty; public int TextureCount => _draft?.Data.Textures.Count ?? 0; public int ConstantCount => _draft?.Data.Constants.Count ?? 0; public int StateBitsCount => _draft?.Data.StateBits.Count ?? 0;
    public string StatusMessage => IsEditable ? "Detached Material draft. Info, constants, textures, state bits, and water data are authored; draw-surface and technique-state cache values are rebuilt at load time." : "Material content is read-only or unavailable."; public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void ReplaceTextures(IEnumerable<MaterialTextureBuildData> value) { if (!IsEditable) return; MaterialTextureBuildData[] copy = value.ToArray(); _session.Apply<MaterialDraft>(draft => draft.ReplaceTextures(copy)); _draft = _session.ReadDraft<MaterialDraft>(); OnPropertyChanged(nameof(TextureCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void ReplaceConstants(IEnumerable<MaterialConstantBuildData> value) { if (!IsEditable) return; MaterialConstantBuildData[] copy = value.ToArray(); _session.Apply<MaterialDraft>(draft => draft.ReplaceConstants(copy)); _draft = _session.ReadDraft<MaterialDraft>(); OnPropertyChanged(nameof(ConstantCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void ReplaceStateBits(IEnumerable<MaterialStateBitsBuildData> value) { if (!IsEditable) return; MaterialStateBitsBuildData[] copy = value.ToArray(); _session.Apply<MaterialDraft>(draft => draft.ReplaceStateBits(copy)); _draft = _session.ReadDraft<MaterialDraft>(); OnPropertyChanged(nameof(StateBitsCount)); OnPropertyChanged(nameof(Diagnostics)); }
    public void RevertDraft() { if (!IsEditable) return; _session.Revert(); _draft = _session.ReadDraft<MaterialDraft>(); OnPropertyChanged(nameof(TextureCount)); OnPropertyChanged(nameof(ConstantCount)); OnPropertyChanged(nameof(StateBitsCount)); OnPropertyChanged(nameof(Diagnostics)); }
}
