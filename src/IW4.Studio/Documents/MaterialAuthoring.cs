using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached authored Material data.  Draw-surface keys and technique
/// state IDs are deliberately excluded: both are rebuilt by the runtime after
/// the material graph is registered.</summary>
public sealed class MaterialAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal MaterialAuthoredSnapshot(MaterialBuildData data) => Data = data.Copy();
    internal MaterialBuildData Data { get; }
    public XAssetType AssetType => XAssetType.Material;
    internal static MaterialAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is MaterialAuthoredSnapshot snapshot
        ? snapshot
        : throw new InvalidDataException("Material editing requires a capture-time detached semantic snapshot because nested graph pointers may be aliases.");
    internal static MaterialAuthoredSnapshot FromLoaded(MaterialAsset asset) =>
        FromLoaded(asset, new MaterialGraphClone());
    internal static MaterialAuthoredSnapshot FromLoaded(
        MaterialAsset asset,
        MaterialGraphClone graph) =>
        new(graph.CaptureDefinition(
            asset,
            () => new MaterialBuildData(
                asset.Info.Name, asset.Info.GameFlags, asset.Info.SortKey, asset.Info.TextureAtlasRowCount, asset.Info.TextureAtlasColumnCount,
                asset.Info.SurfaceTypeBits, asset.Info.HashIndex, asset.Info.Pad16,
                asset.StateBitsEntries.Select(entry => entry.StateBitsIndex), asset.StateFlags, asset.CameraRegion, asset.Pad43, asset.Pad8E,
                asset.RuntimeTechniqueSlotStateBitsPointer.Type != PointerType.Null,
                Reference(XAssetType.Techset, asset.TechniqueSet?.Name), asset.Textures.Select(Texture),
                asset.Constants.Select(constant => new MaterialConstantBuildData(constant.NameHash, constant.NameBytes.ToArray(), Float4(constant.Literal))),
                asset.StateBits.Select(state => StateBits(state, graph)),
                asset.XStrings.Select(entry => entry.Value),
                TechniqueSetLink(asset))));

    private static MaterialTextureBuildData Texture(MaterialTextureDef texture) =>
        texture.Semantic == 0x0b
            ? new MaterialTextureBuildData(
                texture.NameHash,
                texture.NameStart,
                texture.NameEnd,
                texture.SamplerState,
                texture.Semantic,
                imageReference: null,
                water: texture.Water is { } water ? Water(water) : null,
                imageLink: null,
                waterPointerProvenance: WaterPointerProvenance(texture))
            : new MaterialTextureBuildData(
                texture.NameHash,
                texture.NameStart,
                texture.NameEnd,
                texture.SamplerState,
                texture.Semantic,
                Reference(XAssetType.Image, texture.Image?.Name),
                water: null,
                imageLink: ImageLink(texture.DataPointer.Raw, texture.IncomingImage, texture.Image));

    private static MaterialWaterPointerBuildProvenance WaterPointerProvenance(
        MaterialTextureDef texture)
    {
        return texture.DataPointer.Type switch
        {
            PointerType.Null => new MaterialWaterPointerBuildProvenance(
                MaterialWaterPointerSourceForm.Null),
            PointerType.Inline => new MaterialWaterPointerBuildProvenance(
                MaterialWaterPointerSourceForm.Inline,
                XPointerCodec.Encode(
                    texture.Water?.DestinationAddress
                    ?? throw new InvalidDataException(
                        "Inline MaterialWater has no destination owner address."))),
            PointerType.Offset => new MaterialWaterPointerBuildProvenance(
                MaterialWaterPointerSourceForm.PackedAlias,
                ImportedPackedRaw: texture.DataPointer.Raw),
            _ => throw new InvalidDataException(
                $"Semantic 0x0b MaterialTextureDef uses unsupported source pointer {texture.DataPointer.Type}.")
        };
    }
    private static MaterialWaterBuildData Water(MaterialWater value) => new(value.Writable.RawValue, value.M, value.N, value.Lx, value.Lz, value.Gravity, value.WindVelocity, new MaterialVec2BuildData(value.WindDirection.X, value.WindDirection.Y), value.Amplitude, Float4(value.CodeConstant), value.H0X, value.H0Y, value.WTerm, Reference(XAssetType.Image, value.Image?.Name), ImageLink(value.ImagePointer.Raw, value.IncomingImage, value.Image));
    private static Float4BuildData Float4(MaterialVec4 value) => new(value.X, value.Y, value.Z, value.W);
    private static MaterialStateBitsBuildData StateBits(
        GfxStateBits value,
        MaterialGraphClone graph)
    {
        if (value.LoadBits.Count == 0)
            return new MaterialStateBitsBuildData([], value.Tail);

        MaterialLoadBitsPointerSourceForm sourceForm =
            value.LoadBitsPointer.Type switch
            {
                PointerType.Inline => MaterialLoadBitsPointerSourceForm.Inline,
                PointerType.Insert => MaterialLoadBitsPointerSourceForm.Insert,
                PointerType.Offset => MaterialLoadBitsPointerSourceForm.PackedAlias,
                _ => throw new InvalidDataException(
                    $"Non-empty GfxStateBits loadBits uses unsupported source pointer {value.LoadBitsPointer.Type}.")
            };
        XBlockAddress ownerAddress = value.LoadBitsPointer.CellAddress
            ?? throw new InvalidDataException(
                "Non-empty GfxStateBits loadBits has no persistent owner cell.");
        XBlockAddress targetAddress =
            value.LoadBitsPointer.Type == PointerType.Offset
                ? value.LoadBitsPointer.PackedAddress
                    ?? throw new InvalidDataException(
                        "Packed GfxStateBits loadBits has no decoded target alias cell.")
                : ownerAddress;
        return new MaterialStateBitsBuildData(
            value.LoadBits.ToArray(),
            value.Tail,
            new MaterialLoadBitsLinkerProvenance(
                sourceForm,
                value.LoadBitsPointer.Type == PointerType.Offset
                    ? value.LoadBitsPointer.Raw
                    : null,
                graph.AliasToken(targetAddress),
                graph.AliasToken(ownerAddress)));
    }
    private static NestedXAssetBuildLink? TechniqueSetLink(MaterialAsset asset)
    {
        string? name = asset.IncomingTechniqueSet?.Name ?? asset.TechniqueSet?.Name;
        if (name is null || asset.TechniqueSetPointer.Type == PointerType.Null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Techset, name),
            SourceForm(asset.TechniqueSetPointer.Type),
            asset.IncomingTechniqueSet is null
                ? null
                : new TechniqueSetBuildData(
                    new TechniqueSetDraft(
                        TechniqueSetAuthoredSnapshot.FromLoaded(
                            asset.IncomingTechniqueSet))),
            asset.TechniqueSetPointer.Type == PointerType.Offset
                ? asset.TechniqueSetPointer.Raw
                : null);
    }
    private static NestedXAssetBuildLink? ImageLink(
        int pointerRaw,
        GfxImageAsset? incoming,
        GfxImageAsset? canonical)
    {
        PointerType pointerType = XPointerCodec.GetType(pointerRaw);
        string? name = incoming?.Name ?? canonical?.Name;
        if (name is null || pointerType == PointerType.Null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Image, name),
            SourceForm(pointerType),
            incoming is null ? null : new GfxImageBuildData(incoming),
            pointerType == PointerType.Offset ? pointerRaw : null);
    }
    private static NestedXAssetPointerSourceForm SourceForm(PointerType type) => type switch
    {
        PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
        PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
        PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
        _ => throw new InvalidDataException(
            $"Unsupported nested Material pointer source form {type}.")
    };
    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) =>
        name is null
            ? null
            : new SymbolicXAssetReference(
                type,
                name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");
}

public sealed class MaterialBuildData : IMaterialBuildData
{
    private readonly byte[] _stateBitsEntries;
    private readonly MaterialTextureBuildData[] _textures;
    private readonly MaterialConstantBuildData[] _constants;
    private readonly MaterialStateBitsBuildData[] _stateBits;
    private readonly string?[] _xstrings;

    internal MaterialBuildData(string? name, byte gameFlags, byte sortKey, byte atlasRows, byte atlasColumns, uint surfaceTypeBits, ushort hashIndex, ushort pad16, IEnumerable<byte> stateBitsEntries, byte stateFlags, byte cameraRegion, byte pad43, ushort pad8E, bool hasRuntimeTechniqueSlotState, SymbolicXAssetReference? techniqueSetReference, IEnumerable<MaterialTextureBuildData> textures, IEnumerable<MaterialConstantBuildData> constants, IEnumerable<MaterialStateBitsBuildData> stateBits, IEnumerable<string?> xstrings, NestedXAssetBuildLink? techniqueSetLink = null)
    {
        Name = name; GameFlags = gameFlags; SortKey = sortKey; TextureAtlasRowCount = atlasRows; TextureAtlasColumnCount = atlasColumns; SurfaceTypeBits = surfaceTypeBits; HashIndex = hashIndex; Pad16 = pad16; _stateBitsEntries = stateBitsEntries.ToArray(); StateFlags = stateFlags; CameraRegion = cameraRegion; Pad43 = pad43; Pad8E = pad8E; HasRuntimeTechniqueSlotState = hasRuntimeTechniqueSlotState; TechniqueSetReference = techniqueSetReference; TechniqueSetLink = techniqueSetLink; _textures = textures.Select(Clone).ToArray(); _constants = constants.Select(Clone).ToArray(); _stateBits = stateBits.Select(Clone).ToArray(); _xstrings = xstrings.ToArray();
    }

    public XAssetType AssetType => XAssetType.Material;
    public string? Name { get; }
    public byte GameFlags { get; }
    public byte SortKey { get; }
    public byte TextureAtlasRowCount { get; }
    public byte TextureAtlasColumnCount { get; }
    public uint SurfaceTypeBits { get; }
    public ushort HashIndex { get; }
    public ushort Pad16 { get; }
    public IReadOnlyList<byte> StateBitsEntries => Array.AsReadOnly(_stateBitsEntries);
    public byte StateFlags { get; }
    public byte CameraRegion { get; }
    public byte Pad43 { get; }
    public ushort Pad8E { get; }
    public bool HasRuntimeTechniqueSlotState { get; }
    public SymbolicXAssetReference? TechniqueSetReference { get; }
    public NestedXAssetBuildLink? TechniqueSetLink { get; }
    public IReadOnlyList<MaterialTextureBuildData> Textures => Array.AsReadOnly(_textures.Select(Clone).ToArray());
    public IReadOnlyList<MaterialConstantBuildData> Constants => Array.AsReadOnly(_constants.Select(Clone).ToArray());
    public IReadOnlyList<MaterialStateBitsBuildData> StateBits => Array.AsReadOnly(_stateBits.Select(Clone).ToArray());
    public IReadOnlyList<string?> XStrings => Array.AsReadOnly(_xstrings);
    internal MaterialBuildData Copy() => new(Name, GameFlags, SortKey, TextureAtlasRowCount, TextureAtlasColumnCount, SurfaceTypeBits, HashIndex, Pad16, _stateBitsEntries, StateFlags, CameraRegion, Pad43, Pad8E, HasRuntimeTechniqueSlotState, TechniqueSetReference, _textures, _constants, _stateBits, _xstrings, TechniqueSetLink);
    internal static MaterialTextureBuildData Clone(MaterialTextureBuildData value) => new(value.NameHash, value.NameStart, value.NameEnd, value.SamplerState, value.Semantic, value.ImageReference, value.Water is { } water ? Clone(water) : null, value.ImageLink, value.WaterPointerProvenance);
    internal static MaterialWaterBuildData Clone(MaterialWaterBuildData value) => new(value.WritableRaw, value.M, value.N, value.Lx, value.Lz, value.Gravity, value.WindVelocity, value.WindDirection, value.Amplitude, value.CodeConstant, value.H0X, value.H0Y, value.WTerm, value.ImageReference, value.ImageLink);
    internal static MaterialConstantBuildData Clone(MaterialConstantBuildData value) => new(value.NameHash, value.NameBytes.ToArray(), value.Literal);
    internal static MaterialStateBitsBuildData Clone(MaterialStateBitsBuildData value) => new(value.LoadBits.ToArray(), value.Tail, value.LinkerProvenance);
}

internal sealed class MaterialGraphClone
{
    private const int TokenNamespace = 0x20000000;
    private readonly Dictionary<XBlockAddress, MaterialLoadBitsAliasToken>
        _aliases = [];
    private readonly Dictionary<MaterialAsset, MaterialBuildData>
        _definitions = new(ReferenceEqualityComparer.Instance);
    private readonly List<MaterialGraphDefinitionCapture> _captures = [];
    private int _nextToken = 1;
    private int? _containerSerializedIndex;
    private int _nextDefinitionOrdinal;

    internal IReadOnlyList<MaterialGraphDefinitionCapture>
        DefinitionCaptures => _captures.AsReadOnly();

    internal void BeginTopLevelRow(int serializedIndex)
    {
        if (serializedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedIndex));
        }
        _containerSerializedIndex = serializedIndex;
        _nextDefinitionOrdinal = 0;
    }

    internal MaterialBuildData CaptureDefinition(
        MaterialAsset asset,
        Func<MaterialBuildData> create)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(create);
        if (_definitions.TryGetValue(
                asset,
                out MaterialBuildData? existing))
        {
            return existing;
        }

        MaterialBuildData definition = create();
        _definitions.Add(asset, definition);
        if (_containerSerializedIndex is { } containerIndex)
        {
            _captures.Add(
                new MaterialGraphDefinitionCapture(
                    containerIndex,
                    _nextDefinitionOrdinal++,
                    definition));
        }
        return definition;
    }

    internal MaterialLoadBitsAliasToken AliasToken(XBlockAddress address)
    {
        if (_aliases.TryGetValue(
                address,
                out MaterialLoadBitsAliasToken existing))
        {
            return existing;
        }
        var created = new MaterialLoadBitsAliasToken(
            checked(TokenNamespace + _nextToken++));
        _aliases.Add(address, created);
        return created;
    }
}

internal sealed record MaterialGraphDefinitionCapture(
    int ContainerSerializedIndex,
    int DefinitionOrdinal,
    MaterialBuildData BuildData);

public sealed class MaterialDraft
{
    private MaterialBuildData _data;
    internal MaterialDraft(MaterialBuildData data) => _data = data.Copy();
    public MaterialBuildData Data => _data.Copy();
    public void SetInfo(byte gameFlags, byte sortKey, byte atlasRows, byte atlasColumns, uint surfaceTypeBits, ushort hashIndex, ushort pad16, byte stateFlags, byte cameraRegion, byte pad43, ushort pad8E) => _data = new MaterialBuildData(_data.Name, gameFlags, sortKey, atlasRows, atlasColumns, surfaceTypeBits, hashIndex, pad16, _data.StateBitsEntries, stateFlags, cameraRegion, pad43, pad8E, _data.HasRuntimeTechniqueSlotState, _data.TechniqueSetReference, _data.Textures, _data.Constants, _data.StateBits, _data.XStrings, _data.TechniqueSetLink);
    public void SetTechniqueSetReference(SymbolicXAssetReference? value) => _data = new MaterialBuildData(_data.Name, _data.GameFlags, _data.SortKey, _data.TextureAtlasRowCount, _data.TextureAtlasColumnCount, _data.SurfaceTypeBits, _data.HashIndex, _data.Pad16, _data.StateBitsEntries, _data.StateFlags, _data.CameraRegion, _data.Pad43, _data.Pad8E, _data.HasRuntimeTechniqueSlotState, value, _data.Textures, _data.Constants, _data.StateBits, _data.XStrings);
    public void ReplaceTextures(IEnumerable<MaterialTextureBuildData> value) { ArgumentNullException.ThrowIfNull(value); _data = Replace(textures: value); }
    public void ReplaceConstants(IEnumerable<MaterialConstantBuildData> value) { ArgumentNullException.ThrowIfNull(value); _data = Replace(constants: value); }
    public void ReplaceStateBits(IEnumerable<MaterialStateBitsBuildData> value) { ArgumentNullException.ThrowIfNull(value); _data = Replace(stateBits: value); }
    public void ReplaceXStrings(IEnumerable<string?> value) { ArgumentNullException.ThrowIfNull(value); _data = Replace(xstrings: value); }
    public void SetStateBitsEntry(int index, byte value) { byte[] entries = _data.StateBitsEntries.ToArray(); if ((uint)index >= entries.Length) throw new ArgumentOutOfRangeException(nameof(index)); entries[index] = value; _data = new MaterialBuildData(_data.Name, _data.GameFlags, _data.SortKey, _data.TextureAtlasRowCount, _data.TextureAtlasColumnCount, _data.SurfaceTypeBits, _data.HashIndex, _data.Pad16, entries, _data.StateFlags, _data.CameraRegion, _data.Pad43, _data.Pad8E, _data.HasRuntimeTechniqueSlotState, _data.TechniqueSetReference, _data.Textures, _data.Constants, _data.StateBits, _data.XStrings, _data.TechniqueSetLink); }
    internal MaterialDraft Clone() => new(_data);
    private MaterialBuildData Replace(IEnumerable<MaterialTextureBuildData>? textures = null, IEnumerable<MaterialConstantBuildData>? constants = null, IEnumerable<MaterialStateBitsBuildData>? stateBits = null, IEnumerable<string?>? xstrings = null) => new(_data.Name, _data.GameFlags, _data.SortKey, _data.TextureAtlasRowCount, _data.TextureAtlasColumnCount, _data.SurfaceTypeBits, _data.HashIndex, _data.Pad16, _data.StateBitsEntries, _data.StateFlags, _data.CameraRegion, _data.Pad43, _data.Pad8E, _data.HasRuntimeTechniqueSlotState, _data.TechniqueSetReference, textures ?? _data.Textures, constants ?? _data.Constants, stateBits ?? _data.StateBits, xstrings ?? _data.XStrings, _data.TechniqueSetLink);
}

public sealed class MaterialAuthoringAdapter : AssetAuthoringAdapter<MaterialAuthoredSnapshot, MaterialDraft, MaterialBuildData>
{
    private static readonly MaterialBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Material;
    public override MaterialAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MaterialAuthoredSnapshot.Import(source);
    public override MaterialDraft CreateDraft(MaterialAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override MaterialDraft CloneDraft(MaterialDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MaterialDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(MaterialDraft left, MaterialDraft right) => Same(left.Data, right.Data);
    public override MaterialBuildData ExportBuildData(MaterialDraft draft) { MaterialBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Material draft has validation errors and cannot produce build data."); return data; }

    private static bool Same(MaterialBuildData left, MaterialBuildData right) => left.Name == right.Name && left.GameFlags == right.GameFlags && left.SortKey == right.SortKey && left.TextureAtlasRowCount == right.TextureAtlasRowCount && left.TextureAtlasColumnCount == right.TextureAtlasColumnCount && left.SurfaceTypeBits == right.SurfaceTypeBits && left.HashIndex == right.HashIndex && left.Pad16 == right.Pad16 && left.StateBitsEntries.SequenceEqual(right.StateBitsEntries) && left.StateFlags == right.StateFlags && left.CameraRegion == right.CameraRegion && left.Pad43 == right.Pad43 && left.Pad8E == right.Pad8E && left.HasRuntimeTechniqueSlotState == right.HasRuntimeTechniqueSlotState && left.TechniqueSetReference == right.TechniqueSetReference && left.TechniqueSetLink == right.TechniqueSetLink && left.XStrings.SequenceEqual(right.XStrings) && left.Constants.Count == right.Constants.Count && left.Constants.Zip(right.Constants).All(pair => pair.First.NameHash == pair.Second.NameHash && pair.First.NameBytes.SequenceEqual(pair.Second.NameBytes) && pair.First.Literal == pair.Second.Literal) && left.StateBits.Count == right.StateBits.Count && left.StateBits.Zip(right.StateBits).All(pair => pair.First.LoadBits.SequenceEqual(pair.Second.LoadBits) && pair.First.Tail == pair.Second.Tail && pair.First.LinkerProvenance == pair.Second.LinkerProvenance) && left.Textures.Count == right.Textures.Count && left.Textures.Zip(right.Textures).All(pair => Same(pair.First, pair.Second));
    private static bool Same(MaterialTextureBuildData left, MaterialTextureBuildData right) => left.NameHash == right.NameHash && left.NameStart == right.NameStart && left.NameEnd == right.NameEnd && left.SamplerState == right.SamplerState && left.Semantic == right.Semantic && left.ImageReference == right.ImageReference && left.ImageLink == right.ImageLink && left.WaterPointerProvenance == right.WaterPointerProvenance && ((left.Water is null && right.Water is null) || (left.Water is { } a && right.Water is { } b && a.WritableRaw == b.WritableRaw && a.M == b.M && a.N == b.N && a.Lx == b.Lx && a.Lz == b.Lz && a.Gravity == b.Gravity && a.WindVelocity == b.WindVelocity && a.WindDirection == b.WindDirection && a.Amplitude == b.Amplitude && a.CodeConstant == b.CodeConstant && a.H0X.SequenceEqual(b.H0X) && a.H0Y.SequenceEqual(b.H0Y) && a.WTerm.SequenceEqual(b.WTerm) && a.ImageReference == b.ImageReference && a.ImageLink == b.ImageLink));
}
