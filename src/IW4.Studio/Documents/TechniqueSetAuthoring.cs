using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached editable Techset graph.  Inline shader definitions are
/// never absorbed here: only comma-prefixed external shader identities are
/// represented, so target shader rows retain independent ownership.</summary>
public sealed class TechniqueSetAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly TechniqueBuildData?[] _slots;
    private readonly MaterialTechniqueSlotBuildProvenance[] _provenance;
    internal TechniqueSetAuthoredSnapshot(
        string? name,
        byte worldVertexFormat,
        IEnumerable<TechniqueBuildData?> slots,
        IEnumerable<MaterialTechniqueSlotBuildProvenance>? provenance = null)
    {
        Name = name;
        WorldVertexFormat = worldVertexFormat;
        _slots = slots.Select(Clone).ToArray();
        _provenance = provenance?.ToArray()
            ?? _slots.Select(value => new MaterialTechniqueSlotBuildProvenance(
                value is null
                    ? MaterialTechniquePointerSourceForm.Null
                    : MaterialTechniquePointerSourceForm.Inline)).ToArray();
        if (_provenance.Length != _slots.Length)
            throw new ArgumentException(
                "Technique slot provenance must match the technique slot count.",
                nameof(provenance));
    }
    public XAssetType AssetType => XAssetType.Techset;
    public string? Name { get; }
    public byte WorldVertexFormat { get; }
    public IReadOnlyList<TechniqueBuildData?> TechniqueSlots =>
        Array.AsReadOnly(_slots.Select(Clone).ToArray());
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<MaterialTechniqueSlotBuildProvenance> TechniqueSlotProvenance =>
        Array.AsReadOnly(_provenance);
    internal static TechniqueSetAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is TechniqueSetAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("Techset editing requires a capture-time detached semantic snapshot because technique roots and literals can be packed aliases.");
    internal static TechniqueSetAuthoredSnapshot FromLoaded(MaterialTechniqueSetAsset asset) => new(
        asset.Name,
        (byte)asset.WorldVertexFormat,
        asset.TechniqueSlots.Select(slot =>
            slot.Technique is null ? null : FromLoaded(slot.Technique)),
        asset.TechniqueSlots.Select(Provenance));
    private static MaterialTechniqueSlotBuildProvenance Provenance(
        MaterialTechniqueSlot slot) =>
        slot.Pointer.Type switch
        {
            PointerType.Null => new(
                MaterialTechniquePointerSourceForm.Null),
            PointerType.Inline when slot.Technique?.DestinationAddress is { } owner =>
                new(
                    MaterialTechniquePointerSourceForm.Inline,
                    InlineOwnerRaw: XPointerCodec.Encode(owner)),
            PointerType.Inline => throw new InvalidDataException(
                $"Inline MaterialTechnique slot {slot.Index} has no destination owner."),
            PointerType.Offset => new(
                MaterialTechniquePointerSourceForm.PackedAlias,
                ImportedPackedRaw: slot.Pointer.Raw),
            _ => throw new InvalidDataException(
                $"MaterialTechnique slot {slot.Index} has unsupported source form {slot.Pointer.Type}.")
        };
    private static TechniqueBuildData FromLoaded(MaterialTechniqueAsset value) => new(value.Name, value.Flags, value.Passes.Select(FromLoaded).ToArray());
    private static TechniquePassBuildData FromLoaded(MaterialPassAsset value)
    {
        TechniqueVertexDeclarationBuildData? declaration = value.VertexDeclaration is { } vertex ? new(vertex.StreamCount, vertex.HasOptionalSource, vertex.Routing.Select(route => new MaterialVertexStreamRoutingBuildData(route.Source, route.Dest)).ToArray()) : null;
        return new TechniquePassBuildData(
            declaration,
            Reference(XAssetType.VertexShader, value.VertexShader?.Name),
            Reference(XAssetType.PixelShader, value.PixelShader?.Name),
            value.PerPrimArgCount,
            value.PerObjArgCount,
            value.StableArgCount,
            value.CustomSamplerFlags,
            value.PrecompiledIndex,
            value.Args.Select(Argument).ToArray(),
            ShaderLink(
                XAssetType.VertexShader,
                value.VertexShaderPointer.Raw,
                value.IncomingVertexShader,
                value.VertexShader),
            ShaderLink(
                XAssetType.PixelShader,
                value.PixelShaderPointer.Raw,
                value.IncomingPixelShader,
                value.PixelShader),
            DirectProvenance(
                value.VertexDeclPointer.Untyped,
                value.VertexDeclaration?.DestinationAddress,
                "MaterialVertexDeclaration"));
    }
    private static TechniqueShaderArgumentBuildData Argument(
        MaterialShaderArgumentAsset value) =>
        new(
            (ushort)value.Type,
            value.Dest,
            value.ArgumentRaw,
            value.LiteralConstant is { } literal
                ? new Float4BuildData(
                    literal.X,
                    literal.Y,
                    literal.Z,
                    literal.W)
                : null,
            value.LiteralConstant is null
                ? null
                : DirectProvenance(
                    value.ArgumentPointer,
                    value.LiteralDestinationAddress,
                    "MaterialShaderLiteralConstant"));
    private static TechniqueDirectPointerBuildProvenance DirectProvenance(
        XPointerReference pointer,
        XBlockAddress? destinationAddress,
        string memberName) =>
        pointer.Type switch
        {
            PointerType.Null => new(
                TechniqueDirectPointerSourceForm.Null),
            PointerType.Inline when destinationAddress is { } owner => new(
                TechniqueDirectPointerSourceForm.Inline,
                InlineOwnerRaw: XPointerCodec.Encode(owner)),
            PointerType.Insert when destinationAddress is { } owner => new(
                TechniqueDirectPointerSourceForm.Insert,
                InlineOwnerRaw: XPointerCodec.Encode(owner)),
            PointerType.Inline or PointerType.Insert =>
                throw new InvalidDataException(
                    $"{memberName} inline source has no destination owner."),
            PointerType.Offset => new(
                TechniqueDirectPointerSourceForm.PackedAlias,
                ImportedPackedRaw: pointer.Raw),
            _ => throw new InvalidDataException(
                $"{memberName} has unsupported source form {pointer.Type}.")
        };
    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) =>
        name is null
            ? null
            : new SymbolicXAssetReference(
                type,
                name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");
    private static NestedXAssetBuildLink? ShaderLink(XAssetType type, int pointerRaw, MaterialShaderAsset? incoming, MaterialShaderAsset? canonical)
    {
        IW4.FastFiles.Pointers.PointerType pointerType =
            IW4.FastFiles.Pointers.XPointerCodec.GetType(pointerRaw);
        string? name = incoming?.Name ?? canonical?.Name;
        if (pointerType == IW4.FastFiles.Pointers.PointerType.Null || name is null)
            return null;
        NestedXAssetPointerSourceForm form = pointerType switch
        {
            IW4.FastFiles.Pointers.PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            IW4.FastFiles.Pointers.PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            IW4.FastFiles.Pointers.PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException($"Unsupported nested shader pointer source form {pointerType}.")
        };
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(type, name),
            form,
            incoming is null ? null : MaterialShaderBuildData.FromLoaded(type, incoming),
            form == NestedXAssetPointerSourceForm.PackedAlias
                ? pointerRaw
                : null);
    }
    internal static TechniqueBuildData? Clone(TechniqueBuildData? value) => value is null ? null : new TechniqueBuildData(value.Name, value.Flags, value.Passes.Select(pass => new TechniquePassBuildData(pass.VertexDeclaration is { } declaration ? new TechniqueVertexDeclarationBuildData(declaration.StreamCount, declaration.HasOptionalSource, declaration.Routing.ToArray()) : null, pass.VertexShader, pass.PixelShader, pass.PerPrimArgCount, pass.PerObjArgCount, pass.StableArgCount, pass.CustomSamplerFlags, pass.PrecompiledIndex, pass.Arguments.ToArray(), pass.VertexShaderLink, pass.PixelShaderLink, pass.VertexDeclarationProvenance)).ToArray());
}

public sealed class TechniqueSetDraft
{
    private TechniqueBuildData?[] _slots;
    private MaterialTechniqueSlotBuildProvenance[] _provenance;
    internal TechniqueSetDraft(TechniqueSetAuthoredSnapshot source)
    {
        Name = source.Name;
        WorldVertexFormat = source.WorldVertexFormat;
        _slots = source.TechniqueSlots
            .Select(TechniqueSetAuthoredSnapshot.Clone)
            .ToArray();
        _provenance = source.TechniqueSlotProvenance.ToArray();
    }
    public string? Name { get; }
    public byte WorldVertexFormat { get; private set; }
    public IReadOnlyList<TechniqueBuildData?> TechniqueSlots =>
        Array.AsReadOnly(_slots.Select(TechniqueSetAuthoredSnapshot.Clone).ToArray());
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<MaterialTechniqueSlotBuildProvenance> TechniqueSlotProvenance =>
        Array.AsReadOnly(_provenance.ToArray());
    public void SetWorldVertexFormat(byte value) => WorldVertexFormat = value;
    public void SetTechniqueSlot(int index, TechniqueBuildData? value)
    {
        if ((uint)index >= _slots.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        _slots[index] = TechniqueSetAuthoredSnapshot.Clone(value);
        _provenance[index] = new MaterialTechniqueSlotBuildProvenance(
            value is null
                ? MaterialTechniquePointerSourceForm.Null
                : MaterialTechniquePointerSourceForm.Inline);
    }
    internal TechniqueSetDraft Clone() => new(new TechniqueSetAuthoredSnapshot(
        Name,
        WorldVertexFormat,
        _slots,
        _provenance));
}

public sealed class TechniqueSetBuildData : ITechniqueSetBuildData
{
    private readonly TechniqueBuildData?[] _slots;
    private readonly MaterialTechniqueSlotBuildProvenance[] _provenance;
    internal TechniqueSetBuildData(TechniqueSetDraft draft)
    {
        Name = draft.Name;
        WorldVertexFormat = draft.WorldVertexFormat;
        _slots = draft.TechniqueSlots
            .Select(TechniqueSetAuthoredSnapshot.Clone)
            .ToArray();
        _provenance = draft.TechniqueSlotProvenance.ToArray();
    }
    public XAssetType AssetType => XAssetType.Techset;
    public string? Name { get; }
    public byte WorldVertexFormat { get; }
    public IReadOnlyList<TechniqueBuildData?> TechniqueSlots =>
        Array.AsReadOnly(_slots.Select(TechniqueSetAuthoredSnapshot.Clone).ToArray());
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<MaterialTechniqueSlotBuildProvenance> TechniqueSlotProvenance =>
        Array.AsReadOnly(_provenance);
}

public sealed class TechniqueSetAuthoringAdapter : AssetAuthoringAdapter<TechniqueSetAuthoredSnapshot, TechniqueSetDraft, TechniqueSetBuildData>
{
    private static readonly TechniqueSetBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Techset; public override TechniqueSetAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => TechniqueSetAuthoredSnapshot.Import(source); public override TechniqueSetDraft CreateDraft(TechniqueSetAuthoredSnapshot snapshot) => new(snapshot); public override TechniqueSetDraft CloneDraft(TechniqueSetDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(TechniqueSetDraft draft) => Validator.Validate(new TechniqueSetBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(TechniqueSetDraft left, TechniqueSetDraft right) => left.Name == right.Name && left.WorldVertexFormat == right.WorldVertexFormat && left.TechniqueSlots.SequenceEqual(right.TechniqueSlots);
    public override TechniqueSetBuildData ExportBuildData(TechniqueSetDraft draft) { var data = new TechniqueSetBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Techset draft has validation errors."); return data; }
}
