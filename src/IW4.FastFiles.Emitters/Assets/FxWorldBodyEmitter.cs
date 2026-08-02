using IW4.Assets.Assets.FxMap;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Structured FxMap serializer.  FxGlassSystem deliberately keeps the
/// loader's block transitions: definition and initial-state data belong to
/// LARGE; the mutable glass working arrays belong to RUNTIME.  Every pointer
/// is either null, inline for data emitted in that stream, or a symbolic
/// nested XAsset link. Imported packed raws are retained only under the
/// exact-source policy and only until a local persistent owner cell exists.
/// </summary>
public sealed class FxWorldBodyEmitter : IXAssetBodyEmitter
{
    private const int RootSize = FxWorldAsset.SerializedSize;

    public XAssetType AssetType => XAssetType.FxMap;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IFxWorldBuildData data)
        {
            errors.Add(Error("body", "FxMap build data does not implement IFxWorldBuildData.", rowIndex));
            return errors;
        }

        String(data.Name, "name", errors, rowIndex);
        FxGlassSystem glass = data.GlassSystem ?? throw new InvalidDataException("FxMap glass system cannot be null.");
        Count(glass.DefCount, glass.Defs.Count, "glassSystem.defCount", errors, rowIndex);
        Count(glass.PieceLimit, glass.PiecePlaces.Count, "glassSystem.piecePlaces", errors, rowIndex);
        Count(glass.PieceLimit, glass.PieceStates.Count, "glassSystem.pieceStates", errors, rowIndex);
        Count(glass.PieceLimit, glass.PieceDynamics.Count, "glassSystem.pieceDynamics", errors, rowIndex);
        Count(glass.GeoDataLimit, glass.GeoData.Count, "glassSystem.geoData", errors, rowIndex);
        Count(glass.PieceWordCount, glass.IsInUse.Count, "glassSystem.isInUse", errors, rowIndex);
        Count(Product(glass.CellCount, glass.PieceWordCount, "glassSystem.cellBits", errors, rowIndex), glass.CellBits.Count, "glassSystem.cellBits", errors, rowIndex);
        Count(AlignCount(glass.PieceLimit, 16, "glassSystem.visData", errors, rowIndex), glass.VisData.Count, "glassSystem.visData", errors, rowIndex);
        Count(glass.PieceLimit, glass.LinkOrg.Count, "glassSystem.linkOrg", errors, rowIndex);
        Count(AlignCount(glass.PieceLimit, 4, "glassSystem.halfThickness", errors, rowIndex), glass.HalfThickness.Count, "glassSystem.halfThickness", errors, rowIndex);
        Count(glass.InitPieceCount, glass.LightingHandles.Count, "glassSystem.lightingHandles", errors, rowIndex);
        Count(glass.InitPieceCount, glass.InitPieceStates.Count, "glassSystem.initPieceStates", errors, rowIndex);
        Count(glass.InitGeoDataCount, glass.InitGeoData.Count, "glassSystem.initGeoData", errors, rowIndex);
        Count(glass.DefCount, data.DefinitionReferences.Count, "definitionReferences", errors, rowIndex);
        if (glass.GeoDataCount > glass.GeoDataLimit)
            errors.Add(Error("glassSystem.geoDataCount", "Geo-data count cannot exceed the serialized geo-data limit.", rowIndex));
        if (glass.ActivePieceCount > glass.PieceLimit)
            errors.Add(Error("glassSystem.activePieceCount", "Active-piece count cannot exceed the piece limit.", rowIndex));

        for (int index = 0; index < glass.Defs.Count; index++)
        {
            FxGlassDef value = glass.Defs[index];
            if (value.TexVecs.Count != 2)
                errors.Add(Error($"glassSystem.defs[{index}].texVecs", "Each glass definition requires exactly two texture vectors.", rowIndex));
            Finite(value.HalfThickness, $"glassSystem.defs[{index}].halfThickness", errors, rowIndex);
            foreach (FxVec2 texVec in value.TexVecs) { Finite(texVec.X, $"glassSystem.defs[{index}].texVec.x", errors, rowIndex); Finite(texVec.Y, $"glassSystem.defs[{index}].texVec.y", errors, rowIndex); }
            Finite(value.InvHighMipRadius, $"glassSystem.defs[{index}].invHighMipRadius", errors, rowIndex);
            Finite(value.ShatteredInvHighMipRadius, $"glassSystem.defs[{index}].shatteredInvHighMipRadius", errors, rowIndex);
            if (index < data.DefinitionReferences.Count)
            {
                FxGlassDefReferenceBuildData links = data.DefinitionReferences[index];
                NestedOrReference(
                    links.MaterialLink,
                    links.Material,
                    XAssetType.Material,
                    $"definitionReferences[{index}].material",
                    errors,
                    rowIndex);
                NestedOrReference(
                    links.ShatteredMaterialLink,
                    links.ShatteredMaterial,
                    XAssetType.Material,
                    $"definitionReferences[{index}].shatteredMaterial",
                    errors,
                    rowIndex);
                NestedOrReference(
                    links.PhysPresetLink,
                    links.PhysPreset,
                    XAssetType.PhysPreset,
                    $"definitionReferences[{index}].physPreset",
                    errors,
                    rowIndex);
            }
        }
        // DbStreamState proves that RUNTIME blocks are zero-filled without
        // source consumption.  These tables describe allocated cache shape,
        // not fastfile bytes, so accepting source values here would silently
        // promise an impossible preservation contract.
        if (glass.PiecePlaces.Any(value => !IsZero(value))) errors.Add(Error("glassSystem.piecePlaces", "RUNTIME piece-place cache must be zero-initialized.", rowIndex));
        if (glass.PieceStates.Any(value => value.Pad11.Count != 5 || !IsZero(value))) errors.Add(Error("glassSystem.pieceStates", "RUNTIME piece-state cache must have five zero pad bytes and otherwise be zero-initialized.", rowIndex));
        if (glass.PieceDynamics.Any(value => !IsZero(value))) errors.Add(Error("glassSystem.pieceDynamics", "RUNTIME piece-dynamics cache must be zero-initialized.", rowIndex));
        if (glass.GeoData.Any(value => value.PackedValue != 0) || glass.IsInUse.Any(value => value != 0) || glass.CellBits.Any(value => value != 0) || glass.VisData.Any(value => value != 0) || glass.LinkOrg.Any(value => !IsZero(value)) || glass.HalfThickness.Any(value => !IsZero(value)))
            errors.Add(Error("glassSystem.runtime", "RUNTIME glass caches must be zero-initialized.", rowIndex));
        foreach (FxGlassInitPieceState value in glass.InitPieceStates) { Frame(value.Frame, "glassSystem.initPieceStates.frame", errors, rowIndex); Finite(value.Radius, "glassSystem.initPieceStates.radius", errors, rowIndex); Vec2(value.TexCoordOrigin, "glassSystem.initPieceStates.texCoordOrigin", errors, rowIndex); Finite(value.AreaX2, "glassSystem.initPieceStates.areaX2", errors, rowIndex); }
        Finite(glass.EffectChanceAccum, "glassSystem.effectChanceAccum", errors, rowIndex);
        return errors;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IFxWorldBuildData data = (IFxWorldBuildData)buildData;
        FxGlassSystem glass = data.GlassSystem;
        var all = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(RootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = String(data.Name, plan, all, source);
        EmissionAddress? definitionsAddress = glass.Defs.Count == 0
            ? null
            : plan.Allocate(checked(glass.Defs.Count * FxGlassDef.SerializedSize), 4);
        EmissionBlockSegment? definitions = null;
        ExternalPlan?[] physPresets = new ExternalPlan?[glass.Defs.Count];
        ExternalPlan?[] materials = new ExternalPlan?[glass.Defs.Count];
        ExternalPlan?[] shatteredMaterials = new ExternalPlan?[glass.Defs.Count];
        var definitionChildren = new List<EmissionBlockSegment>();
        for (int index = 0; index < glass.Defs.Count; index++)
        {
            FxGlassDefReferenceBuildData refs = data.DefinitionReferences[index];
            EmissionAddress definitionAddress = new(
                definitionsAddress!.Value.Block,
                checked(
                    definitionsAddress.Value.Offset +
                    index * FxGlassDef.SerializedSize));
            physPresets[index] = NestedOrExternal(
                refs.PhysPresetLink,
                refs.PhysPreset,
                XAssetType.PhysPreset,
                PhysPresetSize,
                Offset(definitionAddress, 0x20),
                plan,
                all);
            materials[index] = NestedOrExternal(
                refs.MaterialLink,
                refs.Material,
                XAssetType.Material,
                MaterialSize,
                Offset(definitionAddress, 0x18),
                plan,
                all);
            shatteredMaterials[index] = NestedOrExternal(
                refs.ShatteredMaterialLink,
                refs.ShatteredMaterial,
                XAssetType.Material,
                MaterialSize,
                Offset(definitionAddress, 0x1c),
                plan,
                all);
            Add(definitionChildren, physPresets[index]); Add(definitionChildren, materials[index]); Add(definitionChildren, shatteredMaterials[index]);
        }
        if (definitionsAddress is EmissionAddress address)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < glass.Defs.Count; index++)
                WriteDefinition(writer, glass.Defs[index], materials[index], shatteredMaterials[index], physPresets[index]);
            definitions = new EmissionBlockSegment(address, writer.ToArray()); all.Add(definitions);
        }

        plan.Push(XFileBlockType.RUNTIME);
        bool piecePlaces = RuntimeArray(glass.PiecePlaces.Count, FxGlassPiecePlace.SerializedSize, 4, plan);
        bool pieceStates = RuntimeArray(glass.PieceStates.Count, FxGlassPieceState.SerializedSize, 4, plan);
        bool pieceDynamics = RuntimeArray(glass.PieceDynamics.Count, FxGlassPieceDynamics.SerializedSize, 4, plan);
        bool geoData = RuntimeArray(glass.GeoData.Count, FxGlassGeometryData.SerializedSize, 4, plan);
        bool isInUse = RuntimeArray(glass.IsInUse.Count, sizeof(uint), 4, plan);
        bool cellBits = RuntimeArray(glass.CellBits.Count, sizeof(uint), 4, plan);
        bool visData = RuntimeArray(glass.VisData.Count, 1, 16, plan);
        bool linkOrg = RuntimeArray(glass.LinkOrg.Count, 0x0c, 4, plan);
        bool halfThickness = RuntimeArray(glass.HalfThickness.Count, sizeof(float), 16, plan);
        plan.Pop(XFileBlockType.RUNTIME);

        EmissionBlockSegment? lighting = Array(glass.LightingHandles, sizeof(ushort), 2, plan, all, static (w, x) => w.WriteUInt16(x));
        EmissionBlockSegment? initialPieces = Array(glass.InitPieceStates, FxGlassInitPieceState.SerializedSize, 4, plan, all, WriteInitialPieceState);
        EmissionBlockSegment? initialGeo = Array(glass.InitGeoData, FxGlassGeometryData.SerializedSize, 4, plan, all, static (w, x) => w.WriteUInt32(x.PackedValue));
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var root = new XSourceWriter();
        root.WriteInt32(Pointer(name));
        root.WriteInt32(glass.Time); root.WriteInt32(glass.PrevTime);
        root.WriteUInt32(glass.DefCount); root.WriteUInt32(glass.PieceLimit); root.WriteUInt32(glass.PieceWordCount); root.WriteUInt32(glass.InitPieceCount); root.WriteUInt32(glass.CellCount); root.WriteUInt32(glass.ActivePieceCount); root.WriteUInt32(glass.FirstFreePiece); root.WriteUInt32(glass.GeoDataLimit); root.WriteUInt32(glass.GeoDataCount); root.WriteUInt32(glass.InitGeoDataCount);
        root.WriteInt32(Pointer(definitions)); root.WriteInt32(Pointer(piecePlaces)); root.WriteInt32(Pointer(pieceStates)); root.WriteInt32(Pointer(pieceDynamics)); root.WriteInt32(Pointer(geoData)); root.WriteInt32(Pointer(isInUse)); root.WriteInt32(Pointer(cellBits)); root.WriteInt32(Pointer(visData)); root.WriteInt32(Pointer(linkOrg)); root.WriteInt32(Pointer(halfThickness)); root.WriteInt32(Pointer(lighting)); root.WriteInt32(Pointer(initialPieces)); root.WriteInt32(Pointer(initialGeo));
        root.WriteByte(glass.NeedToCompactData); root.WriteByte(glass.InitCount); root.WriteUInt16(glass.Pad66); root.WriteSingle(glass.EffectChanceAccum); root.WriteInt32(glass.LastPieceDeletionTime);
        Exact(root, RootSize, "FxWorld");
        var rootSegment = new EmissionBlockSegment(rootAddress, root.ToArray()); all.Add(rootSegment);

        // ReadFxWorld consumes these in this exact order.  Allocation order is
        // intentionally irrelevant: source order models the native loader.
        var ordered = new List<EmissionBlockSegment> { rootSegment };
        ordered.AddRange(source); // Name only at this point.
        Add(ordered, definitions); ordered.AddRange(definitionChildren);
        // RUNTIME segments intentionally have no source bytes. The native
        // loader only patches their pointer cells and zero-fills destination
        // memory before advancing each allocation.
        Add(ordered, lighting); Add(ordered, initialPieces); Add(ordered, initialGeo);
        return new AssetBodyEmission(AssetType, rootAddress, all, ordered);
    }

    private const int MaterialSize = 0xa8;
    private const int PhysPresetSize = 0x2c;
    private static PlannedString? String(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source)
    {
        int before = all.Count;
        PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases);
        source.AddRange(all.Skip(before));
        return result;
    }
    private static ExternalPlan? NestedOrExternal(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? reference,
        XAssetType type,
        int size,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (link is { } nested)
        {
            NestedXAssetPlan child = NestedXAssetEmission.Plan(
                nested,
                plan,
                all,
                ownerCell,
                $"FxMap.{type}");
            return new ExternalPlan(child.PointerRaw, child.Source);
        }
        return External(reference, type, size, ownerCell, plan, all);
    }
    private static ExternalPlan? External(
        SymbolicXAssetReference? reference,
        XAssetType type,
        int size,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (reference is null) return null;
        string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
            type,
            reference.OriginalSerializedName);
        if (plan.PersistentXAssetAliasCells.TryGetValue(
                aliasKey,
                out EmissionAddress existingCell))
        {
            return new ExternalPlan(existingCell.ToPackedPointer(), []);
        }
        plan.Push(XFileBlockType.TEMP); EmissionAddress address = plan.Allocate(size, 4);
        plan.Push(XFileBlockType.LARGE); int before = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases);
        EmissionBlockSegment[] strings = all.Skip(before).ToArray();
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(Pointer(name)); writer.Reserve(size - sizeof(int));
        var root = new EmissionBlockSegment(address, writer.ToArray()); all.Add(root);
        if (ownerCell.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new ExternalPlan(-1, [root, .. strings]);
    }
    private static EmissionBlockSegment? Array<T>(IReadOnlyList<T> values, int stride, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all, Action<XSourceWriter, T> write)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * stride), alignment);
        var writer = new XSourceWriter(); foreach (T value in values) write(writer, value);
        Exact(writer, checked(values.Count * stride), "FxMap array");
        var result = new EmissionBlockSegment(address, writer.ToArray()); all.Add(result); return result;
    }
    private static bool RuntimeArray(int count, int stride, int alignment, EmissionPlan plan)
    {
        if (count == 0) return false;
        plan.Allocate(checked(count * stride), alignment);
        return true;
    }
    private static void WriteDefinition(XSourceWriter writer, FxGlassDef value, ExternalPlan? material, ExternalPlan? shatteredMaterial, ExternalPlan? physPreset)
    {
        writer.WriteSingle(value.HalfThickness); WriteVec2(writer, value.TexVecs[0]); WriteVec2(writer, value.TexVecs[1]); writer.WriteUInt32(value.Color); writer.WriteInt32(Pointer(material)); writer.WriteInt32(Pointer(shatteredMaterial)); writer.WriteInt32(Pointer(physPreset)); writer.WriteSingle(value.InvHighMipRadius); writer.WriteSingle(value.ShatteredInvHighMipRadius);
    }
    private static void WriteInitialPieceState(XSourceWriter writer, FxGlassInitPieceState value) { WriteFrame(writer, value.Frame); writer.WriteSingle(value.Radius); WriteVec2(writer, value.TexCoordOrigin); writer.WriteUInt32(value.SupportMask); writer.WriteSingle(value.AreaX2); writer.WriteByte(value.DefIndex); writer.WriteByte(value.VertCount); writer.WriteByte(value.FanDataCount); writer.WriteByte(value.Pad33); }
    private static void WriteFrame(XSourceWriter writer, FxSpatialFrame value) { writer.WriteSingle(value.Quat.X); writer.WriteSingle(value.Quat.Y); writer.WriteSingle(value.Quat.Z); writer.WriteSingle(value.Quat.W); WriteVec3(writer, value.Origin); }
    private static void WriteVec3(XSourceWriter writer, FxVec3 value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void WriteVec2(XSourceWriter writer, FxVec2 value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); }
    private static int Pointer(PlannedString? value) => AssetBodyEmitterHelpers.SourcePointer(value);
    private static int Pointer(EmissionBlockSegment? value) => value is null ? 0 : -1;
    private static int Pointer(ExternalPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(bool isPresent) => isPresent ? -1 : 0;
    private static void Add(List<EmissionBlockSegment> source, EmissionBlockSegment? value) { if (value is not null) source.Add(value); }
    private static void Add(List<EmissionBlockSegment> source, ExternalPlan? value) { if (value is not null) source.AddRange(value.Source); }
    private static void Exact(XSourceWriter writer, int expected, string name) { if (writer.Position != expected) throw new InvalidDataException($"{name} emission was 0x{writer.Position:X} bytes, expected 0x{expected:X}."); }

    private static void Count(uint expected, int actual, string path, List<EmissionError> errors, int? rowIndex) { if (expected > int.MaxValue || expected != (uint)actual) errors.Add(Error(path, $"Serialized count {expected} does not match detached array length {actual}.", rowIndex)); }
    private static uint Product(uint left, uint right, string path, List<EmissionError> errors, int? rowIndex) { try { return checked(left * right); } catch (OverflowException) { errors.Add(Error(path, "Serialized array count overflows uint.", rowIndex)); return uint.MaxValue; } }
    private static uint AlignCount(uint value, uint alignment, string path, List<EmissionError> errors, int? rowIndex) { try { return checked((value + alignment - 1) / alignment * alignment); } catch (OverflowException) { errors.Add(Error(path, "Aligned serialized count overflows uint.", rowIndex)); return uint.MaxValue; } }
    private static void String(string? value, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex)); }
    private static void Reference(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && (value.AssetType != expected || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) errors.Add(Error(path, $"Requires a Latin-1 comma-prefixed {expected} external reference.", rowIndex)); }
    private static void NestedOrReference(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? reference,
        XAssetType expected,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (link is null)
        {
            Reference(reference, expected, path, errors, rowIndex);
            return;
        }
        errors.AddRange(NestedXAssetEmission.Validate(
            link,
            expected,
            path,
            rowIndex,
            XAssetType.FxMap));
        if (reference is null)
        {
            errors.Add(Error(
                path,
                "Imported nested provenance requires its symbolic dependency reference.",
                rowIndex));
            return;
        }
        Reference(reference, expected, path, errors, rowIndex);
        if (AssetBodyEmitterHelpers.XAssetAliasKey(
                reference.AssetType,
                reference.OriginalSerializedName) != link.AliasKey)
        {
            errors.Add(Error(
                path,
                "Imported nested provenance and external identity must agree.",
                rowIndex));
        }
    }
    private static void Finite(float value, string path, List<EmissionError> errors, int? rowIndex) { if (!float.IsFinite(value)) errors.Add(Error(path, "Value must be finite.", rowIndex)); }
    private static void Vec2(FxVec2 value, string path, List<EmissionError> errors, int? rowIndex) { Finite(value.X, $"{path}.x", errors, rowIndex); Finite(value.Y, $"{path}.y", errors, rowIndex); }
    private static void Vec3(FxVec3 value, string path, List<EmissionError> errors, int? rowIndex) { Finite(value.X, $"{path}.x", errors, rowIndex); Finite(value.Y, $"{path}.y", errors, rowIndex); Finite(value.Z, $"{path}.z", errors, rowIndex); }
    private static void Frame(FxSpatialFrame value, string path, List<EmissionError> errors, int? rowIndex) { Finite(value.Quat.X, $"{path}.quat.x", errors, rowIndex); Finite(value.Quat.Y, $"{path}.quat.y", errors, rowIndex); Finite(value.Quat.Z, $"{path}.quat.z", errors, rowIndex); Finite(value.Quat.W, $"{path}.quat.w", errors, rowIndex); Vec3(value.Origin, $"{path}.origin", errors, rowIndex); }
    private static bool IsZero(FxGlassPiecePlace value) => IsZero(value.Frame) && IsZero(value.Radius) && value.NextFree == 0;
    private static bool IsZero(FxGlassPieceState value) => IsZero(value.TexCoordOrigin) && value.SupportMask == 0 && value.InitIndex == 0 && value.GeoDataStart == 0 && value.DefIndex == 0 && value.Pad11.All(value => value == 0) && value.VertCount == 0 && value.HoleDataCount == 0 && value.CrackDataCount == 0 && value.FanDataCount == 0 && value.Flags == 0 && IsZero(value.AreaX2);
    private static bool IsZero(FxGlassPieceDynamics value) => value.FallTime == 0 && value.PhysObjId == 0 && value.PhysJointId == 0 && IsZero(value.Vel) && IsZero(value.AVel);
    private static bool IsZero(FxSpatialFrame value) => IsZero(value.Quat.X) && IsZero(value.Quat.Y) && IsZero(value.Quat.Z) && IsZero(value.Quat.W) && IsZero(value.Origin);
    private static bool IsZero(FxVec2 value) => IsZero(value.X) && IsZero(value.Y);
    private static bool IsZero(FxVec3 value) => IsZero(value.X) && IsZero(value.Y) && IsZero(value.Z);
    private static bool IsZero(float value) => BitConverter.SingleToInt32Bits(value) == 0;
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.FxMap);
    private static EmissionAddress Offset(EmissionAddress address, int relativeOffset) =>
        new(address.Block, checked(address.Offset + relativeOffset));
    private sealed record ExternalPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> Source);
}
