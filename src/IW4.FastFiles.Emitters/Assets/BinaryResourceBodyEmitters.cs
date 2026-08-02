using IW4.FastFiles.Zone;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class MaterialShaderBodyEmitter : IXAssetBodyEmitter
{
    public MaterialShaderBodyEmitter(XAssetType assetType) { if (assetType is not (XAssetType.PixelShader or XAssetType.VertexShader)) throw new ArgumentOutOfRangeException(nameof(assetType)); AssetType = assetType; }
    public XAssetType AssetType { get; }
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IMaterialShaderBuildData data) { diagnostics.Add(new("body", "Shader build data does not implement IMaterialShaderBuildData.", rowIndex, AssetType)); return diagnostics; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "Shader name must be a Latin-1 C string.", rowIndex, AssetType));
        byte[]? bytecode = data.GetDataCopy(); if ((bytecode is null) != (data.DataSize == 0)) diagnostics.Add(new("data", "Shader DataSize must be zero exactly when bytecode is null.", rowIndex, AssetType)); else if (bytecode is not null && bytecode.Length != data.DataSize) diagnostics.Add(new("data", "Shader DataSize must equal the opaque bytecode length.", rowIndex, AssetType));
        int expectedProgram = AssetType == XAssetType.PixelShader ? 12 : 0; if (data.GetProgramBytesCopy().Length != expectedProgram) diagnostics.Add(new("programBytes", $"{AssetType} requires exactly {expectedProgram} preserved program byte(s).", rowIndex, AssetType));
        ValidateBytecodeProvenance(
            bytecode is not null,
            data.BytecodeProvenance,
            diagnostics,
            rowIndex);
        return diagnostics;
    }
    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); IMaterialShaderBuildData data = (IMaterialShaderBuildData)buildData;
        var segments = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>(); int rootSize = AssetType == XAssetType.PixelShader ? 0x18 : 0x0C;
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(rootSize, 4); plan.Push(XFileBlockType.LARGE); int beforeName = segments.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases); int afterName = segments.Count; plan.Pop(XFileBlockType.LARGE);
        byte[]? bytes = data.GetDataCopy();
        MaterialShaderBytecodeBuildProvenance provenance =
            data.BytecodeProvenance ??
            new MaterialShaderBytecodeBuildProvenance(
                bytes is null
                    ? MaterialShaderBytecodePointerSourceForm.Null
                    : MaterialShaderBytecodePointerSourceForm.Inline);
        int bytecodeRaw = 0;
        EmissionAddress? bytecode = null;
        plan.Push(XFileBlockType.TEMP, $"{AssetType}.bytecode");
        try
        {
            switch (provenance.SourceForm)
            {
                case MaterialShaderBytecodePointerSourceForm.Null:
                    break;
                case MaterialShaderBytecodePointerSourceForm.PackedAlias:
                    int importedRaw = provenance.ImportedPackedRaw!.Value;
                    if (plan.TryGetMaterialShaderBytecodeAliasCell(
                            importedRaw,
                            out EmissionAddress relocated))
                    {
                        bytecodeRaw = relocated.ToPackedPointer();
                    }
                    else if (plan.PreserveImportedXAssetPointerValues)
                    {
                        bytecodeRaw = importedRaw;
                    }
                    else
                    {
                        EmissionAddress fallbackCell =
                            plan.AllocateInsertPointerCell(
                                "MaterialShader",
                                "bytecode.insert");
                        bytecode = plan.Allocate(bytes!.Length, 16);
                        bytecodeRaw = -2;
                        plan.RegisterMaterialShaderBytecodeAliasCell(
                            importedRaw,
                            fallbackCell);
                    }
                    break;
                case MaterialShaderBytecodePointerSourceForm.Insert:
                    EmissionAddress insertCell =
                        plan.AllocateInsertPointerCell(
                            "MaterialShader",
                            "bytecode.insert");
                    bytecode = plan.Allocate(bytes!.Length, 16);
                    bytecodeRaw = -2;
                    if (provenance.InsertOwnerRaw is { } insertOwnerRaw)
                    {
                        plan.RegisterMaterialShaderBytecodeAliasCell(
                            insertOwnerRaw,
                            insertCell);
                    }
                    break;
                case MaterialShaderBytecodePointerSourceForm.Inline:
                    bytecode = plan.Allocate(bytes!.Length, 16);
                    bytecodeRaw = -1;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported MaterialShader bytecode source form {provenance.SourceForm}.");
            }
        }
        finally
        {
            plan.Pop(XFileBlockType.TEMP);
            plan.Pop(XFileBlockType.TEMP);
        }
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(bytecodeRaw); writer.WriteUInt32(data.DataSize); writer.WriteBytes(data.GetProgramBytesCopy()); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); segments.Add(rootSegment); source.Add(rootSegment); source.AddRange(segments.Skip(beforeName).Take(afterName - beforeName)); if (bytecode is { } addr) { var segment = new EmissionBlockSegment(addr, bytes!); segments.Add(segment); source.Add(segment); }
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private void ValidateBytecodeProvenance(
        bool hasBytecode,
        MaterialShaderBytecodeBuildProvenance? provenance,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        if (provenance is null)
            return;

        switch (provenance.SourceForm)
        {
            case MaterialShaderBytecodePointerSourceForm.Null:
                if (hasBytecode)
                    diagnostics.Add(new("dataPointer", "Null shader bytecode provenance requires null bytecode.", rowIndex, AssetType));
                if (provenance.InsertOwnerRaw is not null ||
                    provenance.ImportedPackedRaw is not null)
                {
                    diagnostics.Add(new("dataPointer", "Null shader bytecode provenance cannot retain an insert owner or packed raw.", rowIndex, AssetType));
                }
                break;
            case MaterialShaderBytecodePointerSourceForm.Inline:
                if (!hasBytecode)
                    diagnostics.Add(new("dataPointer", "Inline shader bytecode provenance requires bytecode.", rowIndex, AssetType));
                if (provenance.InsertOwnerRaw is not null ||
                    provenance.ImportedPackedRaw is not null)
                {
                    diagnostics.Add(new("dataPointer", "Inline shader bytecode provenance cannot retain an insert owner or packed raw.", rowIndex, AssetType));
                }
                break;
            case MaterialShaderBytecodePointerSourceForm.Insert:
                if (!hasBytecode)
                    diagnostics.Add(new("dataPointer", "Insert shader bytecode provenance requires bytecode.", rowIndex, AssetType));
                if (provenance.ImportedPackedRaw is not null)
                    diagnostics.Add(new("dataPointer", "Insert shader bytecode provenance cannot retain an imported packed raw.", rowIndex, AssetType));
                if (provenance.InsertOwnerRaw is { } ownerRaw &&
                    XPointerCodec.GetType(ownerRaw) != PointerType.Offset)
                {
                    diagnostics.Add(new("dataPointer", "Shader bytecode insert owner must be a packed LARGE address.", rowIndex, AssetType));
                }
                break;
            case MaterialShaderBytecodePointerSourceForm.PackedAlias:
                if (!hasBytecode)
                    diagnostics.Add(new("dataPointer", "Packed shader bytecode provenance requires its resolved bytecode.", rowIndex, AssetType));
                if (provenance.InsertOwnerRaw is not null)
                    diagnostics.Add(new("dataPointer", "Packed shader bytecode provenance cannot declare an insert owner.", rowIndex, AssetType));
                if (provenance.ImportedPackedRaw is not { } importedRaw ||
                    XPointerCodec.GetType(importedRaw) != PointerType.Offset)
                {
                    diagnostics.Add(new("dataPointer", "Packed shader bytecode provenance requires an exact packed alias-cell raw.", rowIndex, AssetType));
                }
                break;
            default:
                diagnostics.Add(new("dataPointer", $"Unsupported shader bytecode source form {provenance.SourceForm}.", rowIndex, AssetType));
                break;
        }
    }
}

public sealed class LoadedSoundBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.LoadedSound;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ILoadedSoundBuildData data) { diagnostics.Add(new("body", "LoadedSound build data does not implement ILoadedSoundBuildData.", rowIndex, AssetType)); return diagnostics; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "LoadedSound name must be a Latin-1 C string.", rowIndex, AssetType));
        byte[]? seek = data.GetSeekTableCopy(); if (seek is not null && seek.Length % 4 != 0) diagnostics.Add(new("seekTable", "LoadedSound seek table must contain whole uint32 entries.", rowIndex, AssetType));
        return diagnostics;
    }
    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); ILoadedSoundBuildData data = (ILoadedSoundBuildData)buildData; var segments = new List<EmissionBlockSegment>();
        byte[]? seek = data.GetSeekTableCopy(); byte[]? physical = data.GetPhysicalDataCopy(); plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x1C, 4); plan.Push(XFileBlockType.LARGE); PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases); EmissionAddress? seekAddress = seek is null ? null : plan.Allocate(seek.Length, 4); if (seekAddress is { } seekSegmentAddress) segments.Add(new(seekSegmentAddress, seek)); plan.Pop(XFileBlockType.LARGE); plan.Push(XFileBlockType.PHYSICAL); EmissionAddress? physicalAddress = physical is null ? null : plan.Allocate(physical.Length, 64); if (physicalAddress is { } physicalSegmentAddress) segments.Add(new(physicalSegmentAddress, physical)); plan.Pop(XFileBlockType.PHYSICAL); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(physical?.Length ?? 0); writer.WriteUInt16(data.FrameCount); writer.WriteUInt16(data.ChannelCount); writer.WriteUInt16(data.SampleRate); writer.WriteUInt16(data.Pad0E); writer.WriteUInt16(data.Pad10); writer.WriteUInt16((ushort)((seek?.Length ?? 0) / 4)); writer.WriteInt32(seekAddress is null ? 0 : -1); writer.WriteInt32(physicalAddress is null ? 0 : -1); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); segments.Add(rootSegment); return new AssetBodyEmission(AssetType, root, segments, [rootSegment, .. segments.Where(segment => segment.Address.Block == XFileBlockType.LARGE).OrderBy(segment => segment.Address.Offset), .. segments.Where(segment => segment.Address.Block == XFileBlockType.PHYSICAL).OrderBy(segment => segment.Address.Offset)]);
    }
}

public sealed class GfxImageBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.Image;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IGfxImageBuildData data) { diagnostics.Add(new("body", "GfxImage build data does not implement IGfxImageBuildData.", rowIndex, AssetType)); return diagnostics; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "GfxImage name must be a Latin-1 C string.", rowIndex, AssetType));
        if (data.StreamData.Count != GfxImageStreamData.EntryCount) diagnostics.Add(new("streamData", $"GfxImage requires exactly {GfxImageStreamData.EntryCount} stream records.", rowIndex, AssetType));
        byte[]? payload = data.GetPayloadCopy(); int expected = GfxImagePixelLayout.ComputePayloadByteCount(data.Format, data.LevelCount, data.MultiFaceControl, data.TextureFlags, data.Width, data.Height, data.Depth);
        if (data.StreamData.Any(entry => entry.HasStreamingData))
        {
            if (payload is not null) diagnostics.Add(new("payload", "A streamed GfxImage must not also emit an inline payload.", rowIndex, AssetType));
            if (string.IsNullOrWhiteSpace(data.Name))
                diagnostics.Add(new("name", "A streamed GfxImage requires a nonempty logical identity.", rowIndex, AssetType));
            if (data.ExternalStreamPackageIndices.Count == 0 || data.ExternalStreamPackageIndices.Any(index => index == 0) || data.ExternalStreamPackageIndices.Distinct().Count() != data.ExternalStreamPackageIndices.Count)
                diagnostics.Add(new("streamData", "A streamed GfxImage requires distinct nonzero external imagefile package indices.", rowIndex, AssetType));
            ValidateSelectedLanguageStreamEntries(data, diagnostics, rowIndex);
        }
        else if (payload is null && expected != 0) diagnostics.Add(new("payload", $"GfxImage requires {expected} inline payload byte(s) for this layout.", rowIndex, AssetType));
        else if (payload is not null && payload.Length != expected) diagnostics.Add(new("payload", $"GfxImage inline payload is {payload.Length} byte(s), expected {expected} from the serialized layout.", rowIndex, AssetType));
        return diagnostics;
    }
    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); IGfxImageBuildData data = (IGfxImageBuildData)buildData; var segments = new List<EmissionBlockSegment>();
        if (data.StreamData.Any(entry => entry.HasStreamingData))
            plan.AppendStreamedGfxImageContribution(
                data.Name!,
                data.SelectedLanguageStreamEntries);
        byte[]? payload = data.GetPayloadCopy(); plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x50, 4); plan.Push(XFileBlockType.LARGE); PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases); plan.Pop(XFileBlockType.LARGE); XFileBlockType payloadBlock = data.TextureSemantic == 0x0b ? XFileBlockType.RUNTIME : XFileBlockType.PHYSICAL; EmissionAddress? payloadAddress = null; if (payload is not null) { plan.Push(payloadBlock); payloadAddress = plan.Allocate(payload.Length, 128); if (payload.Length != 0) segments.Add(new(payloadAddress.Value, payload)); plan.Pop(payloadBlock); } plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteByte(data.Format); writer.WriteByte(data.LevelCount); writer.WriteByte(data.DimensionCount); writer.WriteByte(data.MultiFaceControl); writer.WriteUInt32(data.TextureFlags); writer.WriteUInt16(data.Width); writer.WriteUInt16(data.Height); writer.WriteUInt16(data.Depth); writer.WriteByte(data.PixelDataBlock); writer.WriteByte(data.Pad0F); writer.WriteUInt32(data.RenderTargetPitch); writer.WriteUInt32(data.PixelsOffset); writer.WriteByte(data.MapType); writer.WriteByte(data.TextureSemantic); writer.WriteByte(data.Category); writer.WriteByte(data.Pad1B); writer.WriteUInt32(data.CardMemory); writer.WriteUInt16(data.BaseWidth); writer.WriteUInt16(data.BaseHeight); writer.WriteUInt16(data.BaseDepth); writer.WriteByte(data.BaseLevelCount); writer.WriteByte(data.Cached); writer.WriteInt32(payloadAddress is null ? 0 : -1); foreach (GfxImageStreamBuildData entry in data.StreamData) { writer.WriteUInt16(entry.Width); writer.WriteUInt16(entry.Height); writer.WriteUInt32(entry.LevelSizeAndOffset); } writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); segments.Add(rootSegment);
        IEnumerable<EmissionBlockSegment> large = segments.Where(segment => segment.Address.Block == XFileBlockType.LARGE).OrderBy(segment => segment.Address.Offset); IEnumerable<EmissionBlockSegment> dataSegments = segments.Where(segment => segment.Address.Block == payloadBlock).OrderBy(segment => segment.Address.Offset); return new AssetBodyEmission(AssetType, root, segments, [rootSegment, .. large, .. dataSegments]);
    }

    private static void ValidateSelectedLanguageStreamEntries(
        IGfxImageBuildData data,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        IReadOnlyList<DbHeaderImageStreamEntry> entries =
            data.SelectedLanguageStreamEntries;
        if (entries.Count != GfxImageStreamData.EntryCount)
        {
            diagnostics.Add(new(
                "selectedLanguageStreamEntries",
                $"A streamed GfxImage requires exactly {GfxImageStreamData.EntryCount} selected-language DB-header entries; found {entries.Count}.",
                rowIndex,
                XAssetType.Image));
            return;
        }

        if (data.StreamData.Count == GfxImageStreamData.EntryCount)
        {
            for (int index = 0; index < GfxImageStreamData.EntryCount; index++)
            {
                bool streamDataPresent =
                    data.StreamData[index].HasStreamingData;
                bool headerEntryPresent = !entries[index].IsEmpty;
                if (streamDataPresent != headerEntryPresent)
                {
                    diagnostics.Add(new(
                        $"selectedLanguageStreamEntries[{index}]",
                        "DB-header entry presence must match the corresponding inline GfxImage stream record.",
                        rowIndex,
                        XAssetType.Image));
                }
                if (headerEntryPresent && entries[index].FileIndex == 0)
                {
                    diagnostics.Add(new(
                        $"selectedLanguageStreamEntries[{index}].fileIndex",
                        "A non-empty DB-header stream entry requires a nonzero imagefile package index.",
                        rowIndex,
                        XAssetType.Image));
                }
            }
        }

        uint[] selectedPackages = entries
            .Where(entry => !entry.IsEmpty)
            .Select(entry => entry.FileIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        uint[] declaredPackages = data.ExternalStreamPackageIndices
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (!selectedPackages.SequenceEqual(declaredPackages))
        {
            diagnostics.Add(new(
                "externalStreamPackageIndices",
                "External imagefile package indices must exactly match the non-empty selected-language DB-header entries.",
                rowIndex,
                XAssetType.Image));
        }
    }
}
