using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Pointers;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Material body with loader-owned source data. Nested Techset and GfxImage
/// pointers retain their imported inline/insert/packed source forms without
/// retaining imported runtime addresses.
/// </summary>
public sealed class MaterialBodyEmitter : IXAssetBodyEmitter
{
    private const int Slots = 37;

    public XAssetType AssetType => XAssetType.Material;

    public IReadOnlyList<EmissionError> Validate(
        IXAssetBuildData buildData,
        int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(
            buildData,
            AssetType,
            rowIndex);
        if (buildData is not IMaterialBuildData data)
        {
            diagnostics.Add(new(
                "body",
                "Material build data does not implement IMaterialBuildData.",
                rowIndex,
                AssetType));
            return diagnostics;
        }

        CheckString(data.Name, "name", diagnostics, rowIndex);
        if (data.SortKey >= 0x40)
        {
            diagnostics.Add(new(
                "sortKey",
                "Material sort key must fit the serialized 6-bit post-load draw-surface field.",
                rowIndex,
                AssetType));
        }
        if (data.StateBitsEntries.Count != Slots)
        {
            diagnostics.Add(new(
                "stateBitsEntries",
                "Material requires 37 state-bit entries.",
                rowIndex,
                AssetType));
        }

        if (data.TechniqueSetLink is { } techniqueSetLink)
        {
            diagnostics.AddRange(NestedXAssetEmission.Validate(
                techniqueSetLink,
                XAssetType.Techset,
                "techniqueSet",
                rowIndex,
                AssetType));
        }
        else
        {
            CheckReference(
                data.TechniqueSetReference,
                XAssetType.Techset,
                "techniqueSet",
                diagnostics,
                rowIndex);
        }

        if (data.Textures.Count > byte.MaxValue ||
            data.Constants.Count > byte.MaxValue ||
            data.StateBits.Count > byte.MaxValue ||
            data.XStrings.Count > byte.MaxValue)
        {
            diagnostics.Add(new(
                "counts",
                "Material child count exceeds its serialized byte field.",
                rowIndex,
                AssetType));
        }

        for (int index = 0; index < data.Textures.Count; index++)
            CheckTexture(data.Textures[index], index, diagnostics, rowIndex);
        for (int index = 0; index < data.Constants.Count; index++)
        {
            MaterialConstantBuildData constant = data.Constants[index];
            if (constant.NameBytes is null || constant.NameBytes.Length != 12)
            {
                diagnostics.Add(new(
                    $"constants[{index}].nameBytes",
                    "Material constant name storage is exactly 12 bytes.",
                    rowIndex,
                    AssetType));
            }
            CheckFinite(
                constant.Literal,
                $"constants[{index}].literal",
                diagnostics,
                rowIndex);
        }
        for (int index = 0; index < data.StateBits.Count; index++)
        {
            MaterialStateBitsBuildData state = data.StateBits[index];
            if (state.LoadBits.Count is not (0 or 2))
            {
                diagnostics.Add(new(
                    $"stateBits[{index}].loadBits",
                    "GfxStateBits load bits are absent or exactly two UInt32 values.",
                    rowIndex,
                    AssetType));
            }
            MaterialLoadBitsLinkerProvenance provenance =
                state.LinkerProvenance ??
                MaterialLoadBitsLinkerProvenance.Empty;
            if (state.LoadBits.Count == 0)
            {
                if (provenance.ImportedPackedRaw is not null ||
                    provenance.TargetAlias is not null ||
                    provenance.OwnerAlias is not null)
                {
                    diagnostics.Add(new(
                        $"stateBits[{index}].loadBits",
                        "Null load bits cannot retain non-null alias provenance.",
                        rowIndex,
                        AssetType));
                }
                continue;
            }
            if (provenance.SourceForm ==
                MaterialLoadBitsPointerSourceForm.PackedAlias)
            {
                if (provenance.ImportedPackedRaw is { } packedRaw &&
                    IW4.FastFiles.Pointers.XPointerCodec.GetType(
                        packedRaw) !=
                    IW4.FastFiles.Pointers.PointerType.Offset)
                {
                    diagnostics.Add(new(
                        $"stateBits[{index}].loadBits",
                        "Imported loadBits raw value is not a packed offset pointer.",
                        rowIndex,
                        AssetType));
                }
            }
            else if (provenance.ImportedPackedRaw is not null)
            {
                diagnostics.Add(new(
                    $"stateBits[{index}].loadBits",
                    "Only packed loadBits pointers can retain an imported raw value.",
                    rowIndex,
                    AssetType));
            }
            if (provenance.TargetAlias is { Value: <= 0 } ||
                provenance.OwnerAlias is { Value: <= 0 })
            {
                diagnostics.Add(new(
                    $"stateBits[{index}].loadBits",
                    "LoadBits alias tokens must be positive.",
                    rowIndex,
                    AssetType));
            }
        }
        for (int index = 0; index < data.XStrings.Count; index++)
            CheckString(data.XStrings[index], $"xstrings[{index}]", diagnostics, rowIndex);

        return diagnostics;
    }

    public AssetBodyEmission Plan(
        IXAssetBuildData buildData,
        EmissionPlan plan,
        int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(
            Validate(buildData, rowIndex));
        IMaterialBuildData data = (IMaterialBuildData)buildData;
        var all = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0xa8, 4);
        plan.Push(XFileBlockType.LARGE);

        int beforeName = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(
            data.Name,
            plan,
            all,
            plan.StringAliases);
        EmissionBlockSegment[] nameSource = all.Skip(beforeName).ToArray();

        // RUNTIME blocks are destination-only. The -1 source sentinel causes
        // the loader to materialize the 37-slot array without consuming bytes.
        bool hasRuntimeState = data.HasRuntimeTechniqueSlotState;
        if (hasRuntimeState)
        {
            plan.Push(XFileBlockType.RUNTIME);
            plan.Allocate(Slots * sizeof(ushort), 2);
            plan.Pop(XFileBlockType.RUNTIME);
        }

        EmissionAddress techniqueSetCell = new(
            root.Block,
            checked(root.Offset + 0x94));
        ChildPointerPlan techniqueSet = PlanNestedReference(
            data.TechniqueSetLink,
            data.TechniqueSetReference,
            XAssetType.Techset,
            0x9c,
            techniqueSetCell,
            plan,
            all,
            "Material.TechniqueSet");

        EmissionBlockSegment? textureTable = null;
        TexturePlan[] texturePlans = [];
        if (data.Textures.Count != 0)
        {
            EmissionAddress tableAddress = plan.Allocate(
                checked(data.Textures.Count * 0x0c),
                4);
            texturePlans = data.Textures
                .Select((texture, index) => PlanTexture(
                    texture,
                    new EmissionAddress(
                        tableAddress.Block,
                        checked(tableAddress.Offset + index * 0x0c + 8)),
                    plan,
                    all))
                .ToArray();
            textureTable = new EmissionBlockSegment(
                tableAddress,
                BuildTextureTable(texturePlans));
            all.Add(textureTable);
        }

        EmissionBlockSegment? constants = null;
        if (data.Constants.Count != 0)
        {
            EmissionAddress address = plan.Allocate(
                checked(data.Constants.Count * 0x20),
                16);
            constants = new EmissionBlockSegment(
                address,
                BuildConstants(data.Constants));
            all.Add(constants);
        }

        EmissionBlockSegment? stateTable = null;
        var stateSources = new List<EmissionBlockSegment>();
        if (data.StateBits.Count != 0)
        {
            EmissionAddress address = plan.Allocate(
                checked(data.StateBits.Count * 8),
                4);
            var writer = new XSourceWriter();
            for (int index = 0; index < data.StateBits.Count; index++)
            {
                MaterialStateBitsBuildData state = data.StateBits[index];
                MaterialLoadBitsLinkerProvenance provenance =
                    state.LinkerProvenance ??
                    MaterialLoadBitsLinkerProvenance.Empty;
                EmissionAddress ownerCell = new(
                    address.Block,
                    checked(address.Offset + index * 8));
                int pointerRaw = 0;
                bool materializeLoadBits = false;
                if (state.LoadBits.Count != 0)
                {
                    switch (provenance.SourceForm)
                    {
                        case MaterialLoadBitsPointerSourceForm.Inline:
                            pointerRaw = -1;
                            materializeLoadBits = true;
                            break;
                        case MaterialLoadBitsPointerSourceForm.Insert:
                            pointerRaw = -2;
                            materializeLoadBits = true;
                            plan.AllocateInsertPointerCell(
                                "Material",
                                $"stateBits[{index}].loadBitsInsert");
                            break;
                        case MaterialLoadBitsPointerSourceForm.PackedAlias:
                            // A captured alias token identifies a semantic
                            // owner in this link. Prefer its current emitted
                            // cell even in imported-compatibility mode: the
                            // old numeric offset becomes stale whenever an
                            // earlier typed payload changes cardinality.
                            if (provenance.TargetAlias is { } target &&
                                     plan.TryGetMaterialLoadBitsAliasCell(
                                         target.Value,
                                         out EmissionAddress existing))
                            {
                                pointerRaw = existing.ToPackedPointer();
                            }
                            else if (plan.PreserveImportedXAssetPointerValues &&
                                     provenance.ImportedPackedRaw is { } importedRaw)
                            {
                                // No current-zone owner was emitted. This is
                                // a dependency-zone alias whose imported
                                // address remains outside the rebuilt graph.
                                pointerRaw = importedRaw;
                            }
                            else
                            {
                                pointerRaw = -1;
                                materializeLoadBits = true;
                            }
                            break;
                        default:
                            throw new InvalidDataException(
                                $"Unsupported GfxStateBits loadBits source form {provenance.SourceForm}.");
                    }
                }
                writer.WriteInt32(pointerRaw);
                writer.WriteUInt32(state.Tail);
                if (materializeLoadBits)
                {
                    plan.Push(XFileBlockType.TEMP);
                    EmissionAddress loadAddress = plan.Allocate(8, 4);
                    plan.Pop(XFileBlockType.TEMP);
                    var loadWriter = new XSourceWriter();
                    loadWriter.WriteUInt32(state.LoadBits[0]);
                    loadWriter.WriteUInt32(state.LoadBits[1]);
                    var loadSegment = new EmissionBlockSegment(
                        loadAddress,
                        loadWriter.ToArray());
                    all.Add(loadSegment);
                    stateSources.Add(loadSegment);
                    if (provenance.TargetAlias is { } materializedTarget)
                    {
                        plan.RegisterMaterialLoadBitsAliasCell(
                            materializedTarget.Value,
                            ownerCell);
                    }
                }
                if (state.LoadBits.Count != 0 &&
                    provenance.OwnerAlias is { } owner)
                {
                    plan.RegisterMaterialLoadBitsAliasCell(
                        owner.Value,
                        ownerCell);
                }
            }
            stateTable = new EmissionBlockSegment(address, writer.ToArray());
            all.Add(stateTable);
        }

        EmissionBlockSegment? xstringTable = null;
        var xstringSources = new List<EmissionBlockSegment>();
        if (data.XStrings.Count != 0)
        {
            EmissionAddress address = plan.Allocate(
                checked(data.XStrings.Count * sizeof(int)),
                4);
            var writer = new XSourceWriter();
            var tableInlineStrings = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? value in data.XStrings)
            {
                if (value is null)
                {
                    writer.WriteInt32(0);
                    continue;
                }

                if (plan.StringAliases.TryGetValue(
                        value,
                        out EmissionAddress existing) &&
                    !tableInlineStrings.Contains(value))
                {
                    writer.WriteInt32(existing.ToPackedPointer());
                    continue;
                }

                EmissionAddress stringAddress = plan.Allocate(
                    checked(value.Length + 1));
                var stringWriter = new XSourceWriter();
                stringWriter.WriteLatin1CString(value);
                var stringSegment = new EmissionBlockSegment(
                    stringAddress,
                    stringWriter.ToArray());
                all.Add(stringSegment);
                xstringSources.Add(stringSegment);
                plan.StringAliases.TryAdd(value, stringAddress);
                tableInlineStrings.Add(value);
                writer.WriteInt32(-1);
            }
            xstringTable = new EmissionBlockSegment(address, writer.ToArray());
            all.Add(xstringTable);
        }

        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteByte(data.GameFlags);
        rootWriter.WriteByte(data.SortKey);
        rootWriter.WriteByte(data.TextureAtlasRowCount);
        rootWriter.WriteByte(data.TextureAtlasColumnCount);
        rootWriter.WriteUInt64(0);
        rootWriter.WriteUInt32(data.SurfaceTypeBits);
        rootWriter.WriteUInt16(data.HashIndex);
        rootWriter.WriteUInt16(data.Pad16);
        foreach (byte value in data.StateBitsEntries)
            rootWriter.WriteByte(value);
        rootWriter.WriteByte((byte)data.Textures.Count);
        rootWriter.WriteByte((byte)data.Constants.Count);
        rootWriter.WriteByte((byte)data.StateBits.Count);
        rootWriter.WriteByte(data.StateFlags);
        rootWriter.WriteByte(data.CameraRegion);
        rootWriter.WriteByte((byte)data.XStrings.Count);
        rootWriter.WriteByte(data.Pad43);
        rootWriter.Reserve(Slots * sizeof(ushort));
        rootWriter.WriteUInt16(data.Pad8E);
        rootWriter.WriteInt32(hasRuntimeState ? -1 : 0);
        rootWriter.WriteInt32(techniqueSet.PointerRaw);
        rootWriter.WriteInt32(textureTable is null ? 0 : -1);
        rootWriter.WriteInt32(constants is null ? 0 : -1);
        rootWriter.WriteInt32(stateTable is null ? 0 : -1);
        rootWriter.WriteInt32(xstringTable is null ? 0 : -1);
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray());
        all.Add(rootSegment);

        source.Add(rootSegment);
        source.AddRange(nameSource);
        source.AddRange(techniqueSet.Source);
        Add(source, textureTable);
        foreach (TexturePlan texture in texturePlans)
            source.AddRange(texture.Source);
        Add(source, constants);
        Add(source, stateTable);
        source.AddRange(stateSources);
        Add(source, xstringTable);
        source.AddRange(xstringSources);
        return new AssetBodyEmission(AssetType, root, all, source);
    }

    private static TexturePlan PlanTexture(
        MaterialTextureBuildData data,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (data.Semantic == 0x0b)
        {
            MaterialWaterPointerBuildProvenance provenance =
                data.WaterPointerProvenance
                ?? throw new InvalidDataException(
                    "Semantic 0x0b MaterialTextureDef has no water pointer provenance.");
            return provenance.SourceForm switch
            {
                MaterialWaterPointerSourceForm.Null =>
                    new TexturePlan(data, 0, []),
                MaterialWaterPointerSourceForm.Inline => PlanInlineWater(
                    data,
                    provenance,
                    plan,
                    all),
                MaterialWaterPointerSourceForm.PackedAlias => PlanPackedWater(
                    data,
                    provenance,
                    plan,
                    all),
                _ => throw new InvalidDataException(
                    $"Unsupported MaterialWater pointer source form {provenance.SourceForm}.")
            };
        }

        ChildPointerPlan image = PlanNestedReference(
            data.ImageLink,
            data.ImageReference,
            XAssetType.Image,
            0x50,
            ownerCell,
            plan,
            all,
            "Material.Texture.Image");
        return new TexturePlan(data, image.PointerRaw, image.Source);
    }

    private static TexturePlan PlanInlineWater(
        MaterialTextureBuildData texture,
        MaterialWaterPointerBuildProvenance provenance,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        WaterPlan water = PlanWater(
            texture.Water
            ?? throw new InvalidDataException(
                "Inline MaterialWater provenance has no water body."),
            plan,
            all);
        plan.RegisterMaterialWaterOwner(
            provenance.InlineOwnerRaw
            ?? throw new InvalidDataException(
                "Inline MaterialWater provenance has no owner raw value."),
            water.Root);
        return new TexturePlan(texture, -1, water.Source);
    }

    private static TexturePlan PlanPackedWater(
        MaterialTextureBuildData texture,
        MaterialWaterPointerBuildProvenance provenance,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        int importedRaw = provenance.ImportedPackedRaw
            ?? throw new InvalidDataException(
                "Packed MaterialWater provenance has no imported raw value.");
        if (plan.TryGetMaterialWaterOwner(importedRaw, out EmissionAddress existing))
            return new TexturePlan(texture, existing.ToPackedPointer(), []);

        if (plan.PreserveImportedXAssetPointerValues)
            return new TexturePlan(texture, importedRaw, []);

        WaterPlan water = PlanWater(
            texture.Water
            ?? throw new InvalidDataException(
                "Packed MaterialWater provenance has no detached water body."),
            plan,
            all);
        plan.RegisterMaterialWaterOwner(importedRaw, water.Root);
        return new TexturePlan(texture, -1, water.Source);
    }

    private static WaterPlan PlanWater(
        MaterialWaterBuildData data,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        EmissionAddress root = plan.Allocate(0x48, 4);
        EmissionBlockSegment? h0x = PlanFloatArray(data.H0X, plan, all);
        EmissionBlockSegment? h0y = PlanFloatArray(data.H0Y, plan, all);
        EmissionBlockSegment? wterm = PlanFloatArray(data.WTerm, plan, all);
        EmissionAddress imageCell = new(
            root.Block,
            checked(root.Offset + 0x44));
        ChildPointerPlan image = PlanNestedReference(
            data.ImageLink,
            data.ImageReference,
            XAssetType.Image,
            0x50,
            imageCell,
            plan,
            all,
            "Material.Water.Image");

        var writer = new XSourceWriter();
        writer.WriteUInt32(data.WritableRaw);
        writer.WriteInt32(h0x is null ? 0 : -1);
        writer.WriteInt32(h0y is null ? 0 : -1);
        writer.WriteInt32(wterm is null ? 0 : -1);
        writer.WriteInt32(data.M);
        writer.WriteInt32(data.N);
        writer.WriteSingle(data.Lx);
        writer.WriteSingle(data.Lz);
        writer.WriteSingle(data.Gravity);
        writer.WriteSingle(data.WindVelocity);
        writer.WriteSingle(data.WindDirection.X);
        writer.WriteSingle(data.WindDirection.Y);
        writer.WriteSingle(data.Amplitude);
        writer.WriteSingle(data.CodeConstant.X);
        writer.WriteSingle(data.CodeConstant.Y);
        writer.WriteSingle(data.CodeConstant.Z);
        writer.WriteSingle(data.CodeConstant.W);
        writer.WriteInt32(image.PointerRaw);
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray());
        all.Add(rootSegment);

        var source = new List<EmissionBlockSegment> { rootSegment };
        Add(source, h0x);
        Add(source, h0y);
        Add(source, wterm);
        source.AddRange(image.Source);
        return new WaterPlan(root, source);
    }

    private static ChildPointerPlan PlanNestedReference(
        NestedXAssetBuildLink? link,
        SymbolicXAssetReference? legacyReference,
        XAssetType type,
        int externalRootSize,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        string owner)
    {
        if (link is not null)
        {
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                link,
                plan,
                all,
                ownerCell,
                owner: owner);
            return new ChildPointerPlan(nested.PointerRaw, nested.Source);
        }
        if (legacyReference is null)
            return new ChildPointerPlan(0, []);

        AssetBodyEmission external = PlanExternal(
            legacyReference,
            type,
            externalRootSize,
            plan,
            all);
        return new ChildPointerPlan(-1, external.SourceSegments);
    }

    private static AssetBodyEmission PlanExternal(
        SymbolicXAssetReference reference,
        XAssetType type,
        int size,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(size, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(
            reference.OriginalSerializedName,
            plan,
            all,
            plan.StringAliases);
        EmissionBlockSegment[] nameSource = all.Skip(beforeName).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        if (type == XAssetType.Techset)
        {
            rootWriter.WriteInt32(
                AssetBodyEmitterHelpers.SourcePointer(name));
            rootWriter.Reserve(size - sizeof(int));
        }
        else
        {
            rootWriter.Reserve(0x4c);
            rootWriter.WriteInt32(
                AssetBodyEmitterHelpers.SourcePointer(name));
        }
        var rootSegment = new EmissionBlockSegment(
            root,
            rootWriter.ToArray());
        all.Add(rootSegment);
        return new AssetBodyEmission(
            type,
            root,
            [rootSegment, .. nameSource],
            [rootSegment, .. nameSource]);
    }

    private static byte[] BuildTextureTable(
        IReadOnlyList<TexturePlan> textures)
    {
        var writer = new XSourceWriter();
        foreach (TexturePlan plan in textures)
        {
            MaterialTextureBuildData texture = plan.Data;
            writer.WriteUInt32(texture.NameHash);
            writer.WriteByte(texture.NameStart);
            writer.WriteByte(texture.NameEnd);
            writer.WriteByte(texture.SamplerState);
            writer.WriteByte(texture.Semantic);
            writer.WriteInt32(plan.DataPointerRaw);
        }
        return writer.ToArray();
    }

    private static byte[] BuildConstants(
        IReadOnlyList<MaterialConstantBuildData> values)
    {
        var writer = new XSourceWriter();
        foreach (MaterialConstantBuildData value in values)
        {
            writer.WriteUInt32(value.NameHash);
            writer.WriteBytes(value.NameBytes);
            writer.WriteSingle(value.Literal.X);
            writer.WriteSingle(value.Literal.Y);
            writer.WriteSingle(value.Literal.Z);
            writer.WriteSingle(value.Literal.W);
        }
        return writer.ToArray();
    }

    private static EmissionBlockSegment? PlanFloatArray(
        IReadOnlyList<float> values,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (values.Count == 0)
            return null;

        EmissionAddress address = plan.Allocate(
            checked(values.Count * sizeof(float)),
            4);
        var writer = new XSourceWriter();
        foreach (float value in values)
            writer.WriteSingle(value);
        var segment = new EmissionBlockSegment(address, writer.ToArray());
        all.Add(segment);
        return segment;
    }

    private static void CheckTexture(
        MaterialTextureBuildData value,
        int index,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        bool water = value.Semantic == 0x0b;
        if (!water)
        {
            if (value.Water is not null || value.WaterPointerProvenance is not null)
            {
                diagnostics.Add(new(
                    $"textures[{index}]",
                    "Non-water semantics cannot retain MaterialWater data or provenance.",
                    rowIndex,
                    XAssetType.Material));
            }
            if (value.ImageLink is { } imageLink)
            {
                diagnostics.AddRange(NestedXAssetEmission.Validate(
                    imageLink,
                    XAssetType.Image,
                    $"textures[{index}].image",
                    rowIndex,
                    XAssetType.Material));
            }
            else
            {
                CheckReference(
                    value.ImageReference,
                    XAssetType.Image,
                    $"textures[{index}].image",
                    diagnostics,
                    rowIndex);
            }
            return;
        }

        if (value.ImageReference is not null || value.ImageLink is not null)
        {
            diagnostics.Add(new(
                $"textures[{index}]",
                "Semantic 0x0b stores only a direct MaterialWater pointer; outer image data is forbidden.",
                rowIndex,
                XAssetType.Material));
        }

        MaterialWaterPointerBuildProvenance? provenance =
            value.WaterPointerProvenance;
        if (provenance is null)
        {
            diagnostics.Add(new(
                $"textures[{index}].waterPointer",
                "Semantic 0x0b requires MaterialWater pointer provenance.",
                rowIndex,
                XAssetType.Material));
            return;
        }

        switch (provenance.SourceForm)
        {
            case MaterialWaterPointerSourceForm.Null:
                if (value.Water is not null ||
                    provenance.InlineOwnerRaw is not null ||
                    provenance.ImportedPackedRaw is not null)
                {
                    diagnostics.Add(new(
                        $"textures[{index}].waterPointer",
                        "Null MaterialWater provenance requires no water body or raw pointer values.",
                        rowIndex,
                        XAssetType.Material));
                }
                return;
            case MaterialWaterPointerSourceForm.Inline:
                if (value.Water is null ||
                    provenance.ImportedPackedRaw is not null ||
                    provenance.InlineOwnerRaw is not { } inlineOwnerRaw ||
                    !IsPackedLargePointer(inlineOwnerRaw))
                {
                    diagnostics.Add(new(
                        $"textures[{index}].waterPointer",
                        "Inline MaterialWater provenance requires a water body and a packed LARGE owner raw value only.",
                        rowIndex,
                        XAssetType.Material));
                }
                break;
            case MaterialWaterPointerSourceForm.PackedAlias:
                if (value.Water is null ||
                    provenance.InlineOwnerRaw is not null ||
                    provenance.ImportedPackedRaw is not { } importedPackedRaw ||
                    !IsPackedLargePointer(importedPackedRaw))
                {
                    diagnostics.Add(new(
                        $"textures[{index}].waterPointer",
                        "Packed MaterialWater provenance requires a water body and a packed LARGE imported raw value only.",
                        rowIndex,
                        XAssetType.Material));
                }
                break;
            default:
                diagnostics.Add(new(
                    $"textures[{index}].waterPointer",
                    $"Unsupported MaterialWater pointer source form {provenance.SourceForm}.",
                    rowIndex,
                    XAssetType.Material));
                return;
        }

        if (value.Water is not { } data)
            return;

        int count;
        try
        {
            count = checked(data.M * data.N);
        }
        catch (OverflowException)
        {
            diagnostics.Add(new(
                $"textures[{index}].water",
                "Water dimensions overflow.",
                rowIndex,
                XAssetType.Material));
            return;
        }
        if (data.M < 0 ||
            data.N < 0 ||
            data.H0X.Count != count ||
            data.H0Y.Count != count ||
            data.WTerm.Count != count)
        {
            diagnostics.Add(new(
                $"textures[{index}].water.spectra",
                "Water spectra must each contain M*N values.",
                rowIndex,
                XAssetType.Material));
        }

        if (data.ImageLink is { } waterImageLink)
        {
            diagnostics.AddRange(NestedXAssetEmission.Validate(
                waterImageLink,
                XAssetType.Image,
                $"textures[{index}].water.image",
                rowIndex,
                XAssetType.Material));
        }
        else
        {
            CheckReference(
                data.ImageReference,
                XAssetType.Image,
                $"textures[{index}].water.image",
                diagnostics,
                rowIndex);
        }

        foreach (float spectrum in data.H0X.Concat(data.H0Y).Concat(data.WTerm))
        {
            if (float.IsFinite(spectrum))
                continue;
            diagnostics.Add(new(
                $"textures[{index}].water.spectra",
                "Water spectra must be finite.",
                rowIndex,
                XAssetType.Material));
            break;
        }
        CheckFinite(
            new Float4BuildData(
                data.Lx,
                data.Lz,
                data.Gravity,
                data.WindVelocity),
            $"textures[{index}].water",
            diagnostics,
            rowIndex);
        CheckFinite(
            data.CodeConstant,
            $"textures[{index}].water.codeConstant",
            diagnostics,
            rowIndex);
    }

    private static bool IsPackedLargePointer(int raw) =>
        XPointerCodec.TryDecodeBlockAddress(
            raw,
            out XBlockAddress address) &&
        address.BlockType == XFileBlockType.LARGE;

    private static void CheckReference(
        SymbolicXAssetReference? value,
        XAssetType type,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        if (value is not null &&
            (value.AssetType != type ||
             !value.IsExternalReference ||
             !AssetBodyEmitterHelpers.IsLatin1CString(
                 value.OriginalSerializedName)))
        {
            diagnostics.Add(new(
                path,
                $"Reference must be a comma-prefixed external {type} identity.",
                rowIndex,
                XAssetType.Material));
        }
    }

    private static void CheckString(
        string? value,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        if (value is { } text &&
            !AssetBodyEmitterHelpers.IsLatin1CString(text))
        {
            diagnostics.Add(new(
                path,
                "Value must be a Latin-1 C string.",
                rowIndex,
                XAssetType.Material));
        }
    }

    private static void CheckFinite(
        Float4BuildData value,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W))
        {
            diagnostics.Add(new(
                path,
                "Floating-point values must be finite.",
                rowIndex,
                XAssetType.Material));
        }
    }

    private static void Add(
        List<EmissionBlockSegment> source,
        EmissionBlockSegment? value)
    {
        if (value is not null)
            source.Add(value);
    }

    private sealed record ChildPointerPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record TexturePlan(
        MaterialTextureBuildData Data,
        int DataPointerRaw,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record WaterPlan(
        EmissionAddress Root,
        IReadOnlyList<EmissionBlockSegment> Source);
}
