using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public sealed class MaterialShaderAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly byte[]? _data;
    private readonly byte[] _program;
    internal MaterialShaderAuthoredSnapshot(
        XAssetType type,
        string? name,
        byte[]? data,
        ReadOnlySpan<byte> program,
        MaterialShaderBytecodeBuildProvenance? bytecodeProvenance = null)
    {
        AssetType = type;
        Name = name;
        _data = data?.ToArray();
        _program = program.ToArray();
        BytecodeProvenance = bytecodeProvenance ??
            new MaterialShaderBytecodeBuildProvenance(
                data is null
                    ? MaterialShaderBytecodePointerSourceForm.Null
                    : MaterialShaderBytecodePointerSourceForm.Inline);
    }
    public XAssetType AssetType { get; }
    public string? Name { get; }
    public MaterialShaderBytecodeBuildProvenance BytecodeProvenance { get; }
    public byte[]? GetDataCopy() => _data?.ToArray();
    public byte[] GetProgramBytesCopy() => _program.ToArray();
    internal static MaterialShaderAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is MaterialShaderAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("Shader editing requires capture-time detached payload data.");
    internal static MaterialShaderAuthoredSnapshot FromLoaded(
        XAssetType type,
        MaterialShaderAsset asset) =>
        new(
            type,
            asset.Name,
            asset.Data,
            asset.ProgramBytes,
            CaptureBytecodeProvenance(asset));
    private static MaterialShaderBytecodeBuildProvenance
        CaptureBytecodeProvenance(
        MaterialShaderAsset asset) =>
        asset.DataPointer.Type switch
        {
            PointerType.Null => new(
                MaterialShaderBytecodePointerSourceForm.Null),
            PointerType.Inline => new(
                MaterialShaderBytecodePointerSourceForm.Inline),
            PointerType.Insert when asset.DataInsertCellAddress is { } cell =>
                new(
                    MaterialShaderBytecodePointerSourceForm.Insert,
                    InsertOwnerRaw: XPointerCodec.Encode(cell)),
            PointerType.Insert => throw new InvalidDataException(
                "Inserted MaterialShader bytecode has no captured alias-cell owner."),
            PointerType.Offset => new(
                MaterialShaderBytecodePointerSourceForm.PackedAlias,
                ImportedPackedRaw: asset.DataPointer.Raw),
            _ => throw new InvalidDataException(
                $"Unsupported MaterialShader bytecode pointer form {asset.DataPointer.Type}.")
        };
}
public sealed class MaterialShaderDraft
{
    private byte[]? _data;
    private byte[] _program;
    private MaterialShaderBytecodeBuildProvenance _bytecodeProvenance;
    internal MaterialShaderDraft(MaterialShaderAuthoredSnapshot source)
    {
        AssetType = source.AssetType;
        Name = source.Name;
        _data = source.GetDataCopy();
        _program = source.GetProgramBytesCopy();
        _bytecodeProvenance = source.BytecodeProvenance;
    }
    public XAssetType AssetType { get; }
    public string? Name { get; }
    internal MaterialShaderBytecodeBuildProvenance BytecodeProvenance =>
        _bytecodeProvenance;
    public byte[]? GetDataCopy() => _data?.ToArray();
    public byte[] GetProgramBytesCopy() => _program.ToArray();
    public void ReplaceBytecode(byte[]? data)
    {
        _data = data?.ToArray();
        if (data is null)
        {
            _bytecodeProvenance = new(
                MaterialShaderBytecodePointerSourceForm.Null);
        }
        else if (_bytecodeProvenance.SourceForm is
                 MaterialShaderBytecodePointerSourceForm.Null or
                 MaterialShaderBytecodePointerSourceForm.PackedAlias)
        {
            _bytecodeProvenance = new(
                MaterialShaderBytecodePointerSourceForm.Inline);
        }
    }
    public void ReplaceProgramBytes(ReadOnlySpan<byte> data) =>
        _program = data.ToArray();
    internal MaterialShaderDraft Clone() => new(
        new MaterialShaderAuthoredSnapshot(
            AssetType,
            Name,
            _data,
            _program,
            _bytecodeProvenance));
}
public sealed class MaterialShaderBuildData : IMaterialShaderBuildData
{
    private readonly byte[]? _data;
    private readonly byte[] _program;
    internal MaterialShaderBuildData(MaterialShaderDraft draft)
    {
        AssetType = draft.AssetType;
        Name = draft.Name;
        _data = draft.GetDataCopy();
        _program = draft.GetProgramBytesCopy();
        BytecodeProvenance = draft.BytecodeProvenance;
    }
    internal static MaterialShaderBuildData FromLoaded(
        XAssetType type,
        MaterialShaderAsset asset) =>
        new(new MaterialShaderDraft(
            MaterialShaderAuthoredSnapshot.FromLoaded(type, asset)));
    public XAssetType AssetType { get; }
    public string? Name { get; }
    public uint DataSize => checked((uint)(_data?.Length ?? 0));
    public MaterialShaderBytecodeBuildProvenance BytecodeProvenance { get; }
    public byte[]? GetDataCopy() => _data?.ToArray();
    public byte[] GetProgramBytesCopy() => _program.ToArray();
}
public sealed class MaterialShaderAuthoringAdapter : AssetAuthoringAdapter<MaterialShaderAuthoredSnapshot, MaterialShaderDraft, MaterialShaderBuildData>
{
    private readonly MaterialShaderBodyEmitter _validator;
    public MaterialShaderAuthoringAdapter(XAssetType type) { _validator = new MaterialShaderBodyEmitter(type); }
    public override XAssetType AssetType => _validator.AssetType; public override MaterialShaderAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MaterialShaderAuthoredSnapshot.Import(source); public override MaterialShaderDraft CreateDraft(MaterialShaderAuthoredSnapshot snapshot) => new(snapshot); public override MaterialShaderDraft CloneDraft(MaterialShaderDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MaterialShaderDraft draft) => _validator.Validate(new MaterialShaderBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(MaterialShaderDraft left, MaterialShaderDraft right) => left.AssetType == right.AssetType && left.Name == right.Name && Same(left.GetDataCopy(), right.GetDataCopy()) && left.GetProgramBytesCopy().SequenceEqual(right.GetProgramBytesCopy());
    public override MaterialShaderBuildData ExportBuildData(MaterialShaderDraft draft) { var data = new MaterialShaderBuildData(draft); if (_validator.Validate(data).Count != 0) throw new InvalidOperationException("Shader draft has validation errors."); return data; }
    private static bool Same(byte[]? left, byte[]? right) => (left is null) == (right is null) && (left is null || left.SequenceEqual(right!));
}

public sealed class LoadedSoundAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly byte[]? _seek; private readonly byte[]? _physical;
    internal LoadedSoundAuthoredSnapshot(string? name, ushort frames, ushort channels, ushort rate, ushort pad0E, ushort pad10, byte[]? seek, byte[]? physical) { Name = name; FrameCount = frames; ChannelCount = channels; SampleRate = rate; Pad0E = pad0E; Pad10 = pad10; _seek = seek?.ToArray(); _physical = physical?.ToArray(); }
    public XAssetType AssetType => XAssetType.LoadedSound; public string? Name { get; } public ushort FrameCount { get; } public ushort ChannelCount { get; } public ushort SampleRate { get; } public ushort Pad0E { get; } public ushort Pad10 { get; } public byte[]? GetSeekCopy() => _seek?.ToArray(); public byte[]? GetPhysicalCopy() => _physical?.ToArray();
    internal static LoadedSoundAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is LoadedSoundAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("LoadedSound editing requires capture-time detached payload data.");
    internal static LoadedSoundAuthoredSnapshot FromLoaded(LoadedSound asset) => new(asset.Name, asset.FrameCount, asset.ChannelCount, asset.SampleRate, asset.Pad0E, asset.Pad10, asset.SeekTable, asset.PhysicalData);
}
public sealed class LoadedSoundDraft
{
    private LoadedSoundAuthoredSnapshot _value; internal LoadedSoundDraft(LoadedSoundAuthoredSnapshot value) => _value = value;
    public LoadedSoundAuthoredSnapshot Value => _value; public void ReplacePhysicalData(byte[]? value) => _value = new(_value.Name, _value.FrameCount, _value.ChannelCount, _value.SampleRate, _value.Pad0E, _value.Pad10, _value.GetSeekCopy(), value); internal LoadedSoundDraft Clone() => new(new(_value.Name, _value.FrameCount, _value.ChannelCount, _value.SampleRate, _value.Pad0E, _value.Pad10, _value.GetSeekCopy(), _value.GetPhysicalCopy()));
}
public sealed class LoadedSoundBuildData : ILoadedSoundBuildData
{
    private readonly LoadedSoundAuthoredSnapshot _value; internal LoadedSoundBuildData(LoadedSoundDraft draft) => _value = draft.Value;
    internal static LoadedSoundBuildData FromLoaded(LoadedSound asset) =>
        new(new LoadedSoundDraft(LoadedSoundAuthoredSnapshot.FromLoaded(asset)));
    public XAssetType AssetType => XAssetType.LoadedSound; public string? Name => _value.Name; public ushort FrameCount => _value.FrameCount; public ushort ChannelCount => _value.ChannelCount; public ushort SampleRate => _value.SampleRate; public ushort Pad0E => _value.Pad0E; public ushort Pad10 => _value.Pad10; public byte[]? GetSeekTableCopy() => _value.GetSeekCopy(); public byte[]? GetPhysicalDataCopy() => _value.GetPhysicalCopy();
}
public sealed class LoadedSoundAuthoringAdapter : AssetAuthoringAdapter<LoadedSoundAuthoredSnapshot, LoadedSoundDraft, LoadedSoundBuildData>
{
    private static readonly LoadedSoundBodyEmitter Validator = new(); public override XAssetType AssetType => XAssetType.LoadedSound; public override LoadedSoundAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => LoadedSoundAuthoredSnapshot.Import(source); public override LoadedSoundDraft CreateDraft(LoadedSoundAuthoredSnapshot snapshot) => new(snapshot); public override LoadedSoundDraft CloneDraft(LoadedSoundDraft draft) => draft.Clone(); public override IReadOnlyList<AssetValidationIssue> ValidateDraft(LoadedSoundDraft draft) => Validator.Validate(new LoadedSoundBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray(); public override bool SemanticallyEquals(LoadedSoundDraft left, LoadedSoundDraft right) => left.Value.Name == right.Value.Name && left.Value.GetSeekCopy().AsSpan().SequenceEqual(right.Value.GetSeekCopy()) && left.Value.GetPhysicalCopy().AsSpan().SequenceEqual(right.Value.GetPhysicalCopy()); public override LoadedSoundBuildData ExportBuildData(LoadedSoundDraft draft) { var data = new LoadedSoundBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("LoadedSound draft has validation errors."); return data; }
}
