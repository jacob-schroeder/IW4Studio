using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Emitter for the TechniqueSet tree. Shader links are kept as
/// external symbolic references; shader bytecode remains owned by its own row.</summary>
public sealed class TechniqueSetBodyEmitter : IXAssetBodyEmitter
{
    private const int SlotCount = 37;
    private const ushort LiteralVertexConst = 1;
    private const ushort LiteralPixelConst = 7;
    public XAssetType AssetType => XAssetType.Techset;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ITechniqueSetBuildData data) { diagnostics.Add(new("body", "Techset build data does not implement ITechniqueSetBuildData.", rowIndex, AssetType)); return diagnostics; }
        if (data.TechniqueSlots.Count != SlotCount) diagnostics.Add(new("techniqueSlots", "Techset must preserve all 37 technique slots.", rowIndex, AssetType));
        if (data.TechniqueSlotProvenance.Count is not (0 or SlotCount)) diagnostics.Add(new("techniqueSlotProvenance", "Techset pointer provenance must be empty for legacy build data or preserve all 37 technique slots.", rowIndex, AssetType));
        ValidateString(data.Name, "name", diagnostics, rowIndex);
        for (int slot = 0; slot < data.TechniqueSlots.Count; slot++) if (data.TechniqueSlots[slot] is { } technique) ValidateTechnique(technique, $"techniqueSlots[{slot}]", diagnostics, rowIndex);
        if (data.TechniqueSlotProvenance.Count == SlotCount)
        {
            for (int slot = 0; slot < SlotCount; slot++)
                ValidateSlotProvenance(
                    data.TechniqueSlots[slot],
                    data.TechniqueSlotProvenance[slot],
                    slot,
                    diagnostics,
                    rowIndex);
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); ITechniqueSetBuildData data = (ITechniqueSetBuildData)buildData;
        var segments = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x9c, 4); plan.Push(XFileBlockType.LARGE);
        int beforeName = segments.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases); int afterName = segments.Count;
        var techniques = new TechniquePlan?[SlotCount];
        var techniqueRaws = new int[SlotCount];
        bool hasProvenance = data.TechniqueSlotProvenance.Count == SlotCount;
        for (int index = 0; index < SlotCount; index++)
        {
            TechniqueBuildData? technique = data.TechniqueSlots[index];
            if (!hasProvenance)
            {
                if (technique is not null)
                {
                    techniques[index] = PlanTechnique(technique, plan, segments);
                    techniqueRaws[index] = -1;
                }
                continue;
            }

            MaterialTechniqueSlotBuildProvenance provenance =
                data.TechniqueSlotProvenance[index];
            switch (provenance.SourceForm)
            {
                case MaterialTechniquePointerSourceForm.Null:
                    techniqueRaws[index] = 0;
                    break;
                case MaterialTechniquePointerSourceForm.Inline:
                    techniques[index] = PlanTechnique(technique!, plan, segments);
                    techniqueRaws[index] = -1;
                    if (provenance.InlineOwnerRaw is { } ownerRaw)
                    {
                        plan.RegisterMaterialTechniqueOwner(
                            ownerRaw,
                            techniques[index]!.RootAddress);
                    }
                    break;
                case MaterialTechniquePointerSourceForm.PackedAlias:
                    int importedRaw = provenance.ImportedPackedRaw!.Value;
                    if (plan.TryGetMaterialTechniqueOwner(
                            importedRaw,
                            out EmissionAddress relocated))
                    {
                        techniqueRaws[index] = relocated.ToPackedPointer();
                    }
                    else if (plan.PreserveImportedXAssetPointerValues)
                    {
                        techniqueRaws[index] = importedRaw;
                    }
                    else
                    {
                        techniques[index] = PlanTechnique(
                            technique!,
                            plan,
                            segments);
                        techniqueRaws[index] = -1;
                        plan.RegisterMaterialTechniqueOwner(
                            importedRaw,
                            techniques[index]!.RootAddress);
                    }
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported MaterialTechnique slot source form {provenance.SourceForm}.");
            }
        }
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); rootWriter.WriteByte(data.WorldVertexFormat); rootWriter.Reserve(3);
        for (int index = 0; index < SlotCount; index++) rootWriter.WriteInt32(techniqueRaws[index]);
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray()); segments.Add(rootSegment); source.Add(rootSegment); source.AddRange(segments.Skip(beforeName).Take(afterName - beforeName));
        foreach (TechniquePlan? technique in techniques) if (technique is not null) source.AddRange(technique.Source);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static TechniquePlan PlanTechnique(TechniqueBuildData data, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        // The parent loader stays in LARGE while it loads the technique root,
        // all pass roots, their children, and finally the technique name.
        EmissionAddress root = plan.Allocate(0x08, 4); var passPlans = new PassPlan[data.Passes.Count];
        for (int index = 0; index < passPlans.Length; index++) passPlans[index] = PlanPassRoot(data.Passes[index], plan);
        for (int index = 0; index < passPlans.Length; index++) PlanPassChildren(passPlans[index], plan, all);
        int beforeName = all.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases); int afterName = all.Count;
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteUInt16(data.Flags); writer.WriteUInt16((ushort)data.Passes.Count);
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment);
        var source = new List<EmissionBlockSegment> { rootSegment }; source.AddRange(passPlans.Select(pass => pass.Root)); foreach (PassPlan pass in passPlans) source.AddRange(pass.SourceChildren); source.AddRange(all.Skip(beforeName).Take(afterName - beforeName));
        return new TechniquePlan(root, source);
    }

    private static PassPlan PlanPassRoot(TechniquePassBuildData data, EmissionPlan plan)
    {
        EmissionAddress root = plan.Allocate(0x18, 4); return new PassPlan(data, root);
    }

    private static void PlanPassChildren(PassPlan pass, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        DirectValuePlan vertexDeclaration = PlanVertexDeclaration(
            pass.Data.VertexDeclaration,
            pass.Data.VertexDeclarationProvenance,
            plan);
        pass.VertexDeclarationRaw = vertexDeclaration.PointerRaw;
        if (vertexDeclaration.Segment is { } vertexDeclarationSegment)
        {
            all.Add(vertexDeclarationSegment);
            pass.VertexDeclaration = vertexDeclarationSegment;
            pass.SourceChildren.Add(vertexDeclarationSegment);
        }
        EmissionAddress vertexShaderCell = new(
            pass.Root.Address.Block,
            checked(pass.Root.Address.Offset + 4));
        if (pass.Data.VertexShaderLink is { } vertexShaderLink)
        {
            NestedXAssetPlan shader = NestedXAssetEmission.Plan(
                vertexShaderLink,
                plan,
                all,
                vertexShaderCell,
                "TechniqueSet.VertexShader");
            pass.VertexShaderRaw = shader.PointerRaw;
            pass.SourceChildren.AddRange(shader.Source);
        }
        else
        {
            pass.VertexShader = PlanShaderReference(pass.Data.VertexShader, XAssetType.VertexShader, plan, all);
            if (pass.VertexShader is not null)
            {
                pass.VertexShaderRaw = -1;
                pass.SourceChildren.AddRange(pass.VertexShader.SourceSegments);
            }
        }
        EmissionAddress pixelShaderCell = new(
            pass.Root.Address.Block,
            checked(pass.Root.Address.Offset + 8));
        if (pass.Data.PixelShaderLink is { } pixelShaderLink)
        {
            NestedXAssetPlan shader = NestedXAssetEmission.Plan(
                pixelShaderLink,
                plan,
                all,
                pixelShaderCell,
                "TechniqueSet.PixelShader");
            pass.PixelShaderRaw = shader.PointerRaw;
            pass.SourceChildren.AddRange(shader.Source);
        }
        else
        {
            pass.PixelShader = PlanShaderReference(pass.Data.PixelShader, XAssetType.PixelShader, plan, all);
            if (pass.PixelShader is not null)
            {
                pass.PixelShaderRaw = -1;
                pass.SourceChildren.AddRange(pass.PixelShader.SourceSegments);
            }
        }
        if (pass.Data.Arguments.Count != 0)
        {
            EmissionAddress argsAddress = plan.Allocate(checked(pass.Data.Arguments.Count * 8), 4); var writer = new XSourceWriter();
            var literals = new List<EmissionBlockSegment>();
            foreach (TechniqueShaderArgumentBuildData arg in pass.Data.Arguments)
            {
                DirectValuePlan literal = PlanLiteral(
                    arg.Literal,
                    arg.LiteralProvenance,
                    plan);
                writer.WriteUInt16(arg.Type);
                writer.WriteUInt16(arg.Dest);
                writer.WriteInt32(
                    arg.Literal is null
                        ? arg.RawValue
                        : literal.PointerRaw);
                if (literal.Segment is { } literalSegment)
                    literals.Add(literalSegment);
            }
            var args = new EmissionBlockSegment(argsAddress, writer.ToArray()); all.Add(args); all.AddRange(literals); pass.Args = args; pass.SourceChildren.Add(args); pass.SourceChildren.AddRange(literals);
        }
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(pass.VertexDeclarationRaw); rootWriter.WriteInt32(pass.VertexShaderRaw); rootWriter.WriteInt32(pass.PixelShaderRaw); rootWriter.WriteByte(pass.Data.PerPrimArgCount); rootWriter.WriteByte(pass.Data.PerObjArgCount); rootWriter.WriteByte(pass.Data.StableArgCount); rootWriter.WriteByte(pass.Data.CustomSamplerFlags); rootWriter.WriteByte(pass.Data.PrecompiledIndex); rootWriter.Reserve(3); rootWriter.WriteInt32(pass.Args is null ? 0 : -1); pass.Root = new EmissionBlockSegment(pass.Root.Address, rootWriter.ToArray()); all.Add(pass.Root);
    }

    private static DirectValuePlan PlanVertexDeclaration(
        TechniqueVertexDeclarationBuildData? declaration,
        TechniqueDirectPointerBuildProvenance? provenance,
        EmissionPlan plan)
    {
        if (declaration is null)
            return new DirectValuePlan(0, null);

        TechniqueDirectPointerBuildProvenance effective =
            provenance ??
            new TechniqueDirectPointerBuildProvenance(
                TechniqueDirectPointerSourceForm.Inline);
        if (effective.SourceForm ==
                TechniqueDirectPointerSourceForm.PackedAlias &&
            effective.ImportedPackedRaw is { } importedRaw)
        {
            if (plan.TryGetMaterialVertexDeclarationOwner(
                    importedRaw,
                    out EmissionAddress relocated))
            {
                return new DirectValuePlan(
                    relocated.ToPackedPointer(),
                    null);
            }
            if (plan.PreserveImportedXAssetPointerValues)
                return new DirectValuePlan(importedRaw, null);
        }
        if (effective.SourceForm == TechniqueDirectPointerSourceForm.Null)
            return new DirectValuePlan(0, null);
        if (effective.SourceForm == TechniqueDirectPointerSourceForm.Insert)
        {
            plan.AllocateInsertPointerCell(
                "TechniqueSet",
                "MaterialVertexDeclaration.insert");
        }

        EmissionAddress address = plan.Allocate(0x1c, 4);
        var writer = new XSourceWriter();
        writer.WriteByte(declaration.Value.StreamCount);
        writer.WriteByte(declaration.Value.HasOptionalSource);
        foreach (MaterialVertexStreamRoutingBuildData route in
                 declaration.Value.Routing)
        {
            writer.WriteByte(route.Source);
            writer.WriteByte(route.Dest);
        }
        var segment = new EmissionBlockSegment(address, writer.ToArray());
        int pointerRaw =
            effective.SourceForm == TechniqueDirectPointerSourceForm.Insert
                ? -2
                : -1;
        int? ownerRaw =
            effective.SourceForm == TechniqueDirectPointerSourceForm.PackedAlias
                ? effective.ImportedPackedRaw
                : effective.InlineOwnerRaw;
        if (ownerRaw is { } original)
        {
            plan.RegisterMaterialVertexDeclarationOwner(
                original,
                address);
        }
        return new DirectValuePlan(pointerRaw, segment);
    }

    private static DirectValuePlan PlanLiteral(
        Float4BuildData? value,
        TechniqueDirectPointerBuildProvenance? provenance,
        EmissionPlan plan)
    {
        if (value is null)
            return new DirectValuePlan(0, null);

        TechniqueDirectPointerBuildProvenance effective =
            provenance ??
            new TechniqueDirectPointerBuildProvenance(
                TechniqueDirectPointerSourceForm.Inline);
        if (effective.SourceForm ==
                TechniqueDirectPointerSourceForm.PackedAlias &&
            effective.ImportedPackedRaw is { } importedRaw)
        {
            if (plan.TryGetMaterialLiteralOwner(
                    importedRaw,
                    out EmissionAddress relocated))
            {
                return new DirectValuePlan(
                    relocated.ToPackedPointer(),
                    null);
            }
            if (plan.PreserveImportedXAssetPointerValues)
                return new DirectValuePlan(importedRaw, null);
        }
        if (effective.SourceForm == TechniqueDirectPointerSourceForm.Null)
            return new DirectValuePlan(0, null);
        if (effective.SourceForm == TechniqueDirectPointerSourceForm.Insert)
        {
            plan.AllocateInsertPointerCell(
                "TechniqueSet",
                "MaterialShaderLiteralConstant.insert");
        }

        EmissionAddress address = plan.Allocate(0x10, 16);
        var writer = new XSourceWriter();
        writer.WriteSingle(value.Value.X);
        writer.WriteSingle(value.Value.Y);
        writer.WriteSingle(value.Value.Z);
        writer.WriteSingle(value.Value.W);
        var segment = new EmissionBlockSegment(address, writer.ToArray());
        int pointerRaw =
            effective.SourceForm == TechniqueDirectPointerSourceForm.Insert
                ? -2
                : -1;
        int? ownerRaw =
            effective.SourceForm == TechniqueDirectPointerSourceForm.PackedAlias
                ? effective.ImportedPackedRaw
                : effective.InlineOwnerRaw;
        if (ownerRaw is { } original)
            plan.RegisterMaterialLiteralOwner(original, address);
        return new DirectValuePlan(pointerRaw, segment);
    }

    private static AssetBodyEmission? PlanShaderReference(SymbolicXAssetReference? reference, XAssetType type, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (reference is null) return null; plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(type == XAssetType.PixelShader ? 0x18 : 0x0c, 4); plan.Push(XFileBlockType.LARGE); EmissionAddress name = plan.Allocate(checked(reference.OriginalSerializedName.Length + 1)); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(-1); rootWriter.WriteInt32(0); rootWriter.WriteUInt32(0); if (type == XAssetType.PixelShader) rootWriter.Reserve(12); var nameWriter = new XSourceWriter(); nameWriter.WriteLatin1CString(reference.OriginalSerializedName); var emission = new AssetBodyEmission(type, root, [new EmissionBlockSegment(root, rootWriter.ToArray()), new EmissionBlockSegment(name, nameWriter.ToArray())]); all.AddRange(emission.Segments); return emission;
    }

    private static void ValidateTechnique(TechniqueBuildData technique, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        ValidateString(technique.Name, $"{path}.name", diagnostics, rowIndex);
        if (technique.Passes.Count > ushort.MaxValue) diagnostics.Add(new($"{path}.passes", "Technique pass count exceeds UInt16.", rowIndex, XAssetType.Techset));
        for (int index = 0; index < technique.Passes.Count; index++)
        {
            TechniquePassBuildData pass = technique.Passes[index]; string passPath = $"{path}.passes[{index}]"; int count = pass.PerPrimArgCount + pass.PerObjArgCount + pass.StableArgCount;
            if (count != pass.Arguments.Count) diagnostics.Add(new($"{passPath}.arguments", "Pass argument count does not equal the three serialized count fields.", rowIndex, XAssetType.Techset));
            if (pass.VertexDeclaration is { } declaration && declaration.Routing.Count != 13) diagnostics.Add(new($"{passPath}.vertexDeclaration.routing", "Vertex declaration requires exactly 13 routing entries.", rowIndex, XAssetType.Techset));
            ValidateDirectPointerProvenance(
                pass.VertexDeclaration is not null,
                pass.VertexDeclarationProvenance,
                $"{passPath}.vertexDeclaration",
                diagnostics,
                rowIndex);
            if (pass.VertexShaderLink is { } vertexLink) diagnostics.AddRange(NestedXAssetEmission.Validate(vertexLink, XAssetType.VertexShader, $"{passPath}.vertexShader", rowIndex, XAssetType.Techset)); else ValidateShaderReference(pass.VertexShader, XAssetType.VertexShader, $"{passPath}.vertexShader", diagnostics, rowIndex);
            if (pass.PixelShaderLink is { } pixelLink) diagnostics.AddRange(NestedXAssetEmission.Validate(pixelLink, XAssetType.PixelShader, $"{passPath}.pixelShader", rowIndex, XAssetType.Techset)); else ValidateShaderReference(pass.PixelShader, XAssetType.PixelShader, $"{passPath}.pixelShader", diagnostics, rowIndex);
            for (int arg = 0; arg < pass.Arguments.Count; arg++)
            {
                TechniqueShaderArgumentBuildData value = pass.Arguments[arg];
                string argumentPath = $"{passPath}.arguments[{arg}]";
                bool literalKind =
                    value.Type is LiteralVertexConst or LiteralPixelConst;
                if (literalKind != value.Literal.HasValue)
                    diagnostics.Add(new(argumentPath, "Only literal argument kinds may carry a Float4 payload, and every literal kind requires one.", rowIndex, XAssetType.Techset));
                ValidateDirectPointerProvenance(
                    value.Literal.HasValue,
                    value.LiteralProvenance,
                    $"{argumentPath}.literal",
                    diagnostics,
                    rowIndex);
                if (value.Literal is { } literal &&
                    (!float.IsFinite(literal.X) ||
                     !float.IsFinite(literal.Y) ||
                     !float.IsFinite(literal.Z) ||
                     !float.IsFinite(literal.W)))
                {
                    diagnostics.Add(new(argumentPath, "Literal constants must be finite.", rowIndex, XAssetType.Techset));
                }
            }
        }
    }

    private static void ValidateDirectPointerProvenance(
        bool hasValue,
        TechniqueDirectPointerBuildProvenance? provenance,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        // Null provenance is the supported greenfield/legacy form. The
        // emitter materializes a present value inline.
        if (provenance is null)
            return;

        switch (provenance.SourceForm)
        {
            case TechniqueDirectPointerSourceForm.Null:
                if (hasValue)
                    diagnostics.Add(new(path, "Null direct-pointer provenance requires a null value.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is not null ||
                    provenance.ImportedPackedRaw is not null)
                {
                    diagnostics.Add(new(path, "Null direct-pointer provenance cannot retain an owner or packed raw.", rowIndex, XAssetType.Techset));
                }
                break;
            case TechniqueDirectPointerSourceForm.Inline:
            case TechniqueDirectPointerSourceForm.Insert:
                if (!hasValue)
                    diagnostics.Add(new(path, $"{provenance.SourceForm} direct-pointer provenance requires a value.", rowIndex, XAssetType.Techset));
                if (provenance.ImportedPackedRaw is not null)
                    diagnostics.Add(new(path, $"{provenance.SourceForm} direct-pointer provenance cannot retain an imported packed raw.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is { } ownerRaw &&
                    XPointerCodec.GetType(ownerRaw) != PointerType.Offset)
                {
                    diagnostics.Add(new(path, $"{provenance.SourceForm} direct-pointer owner identity must be a packed LARGE address.", rowIndex, XAssetType.Techset));
                }
                break;
            case TechniqueDirectPointerSourceForm.PackedAlias:
                if (!hasValue)
                    diagnostics.Add(new(path, "Packed direct-pointer provenance requires its resolved value.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is not null)
                    diagnostics.Add(new(path, "Packed direct-pointer provenance cannot declare an inline owner.", rowIndex, XAssetType.Techset));
                if (provenance.ImportedPackedRaw is not { } importedRaw ||
                    XPointerCodec.GetType(importedRaw) != PointerType.Offset)
                {
                    diagnostics.Add(new(path, "Packed direct-pointer provenance requires an exact packed source raw.", rowIndex, XAssetType.Techset));
                }
                break;
            default:
                diagnostics.Add(new(path, $"Unsupported direct-pointer source form {provenance.SourceForm}.", rowIndex, XAssetType.Techset));
                break;
        }
    }

    private static void ValidateSlotProvenance(
        TechniqueBuildData? technique,
        MaterialTechniqueSlotBuildProvenance provenance,
        int slot,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        string path = $"techniqueSlotProvenance[{slot}]";
        switch (provenance.SourceForm)
        {
            case MaterialTechniquePointerSourceForm.Null:
                if (technique is not null)
                    diagnostics.Add(new(path, "Null slot provenance requires a null technique.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is not null || provenance.ImportedPackedRaw is not null)
                    diagnostics.Add(new(path, "Null slot provenance cannot retain an owner or packed raw.", rowIndex, XAssetType.Techset));
                break;
            case MaterialTechniquePointerSourceForm.Inline:
                if (technique is null)
                    diagnostics.Add(new(path, "Inline slot provenance requires a technique definition.", rowIndex, XAssetType.Techset));
                if (provenance.ImportedPackedRaw is not null)
                    diagnostics.Add(new(path, "Inline slot provenance cannot retain an imported packed raw.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is { } ownerRaw &&
                    XPointerCodec.GetType(ownerRaw) != PointerType.Offset)
                    diagnostics.Add(new(path, "Inline slot owner identity must be a packed LARGE address.", rowIndex, XAssetType.Techset));
                break;
            case MaterialTechniquePointerSourceForm.PackedAlias:
                if (technique is null)
                    diagnostics.Add(new(path, "Packed slot provenance requires its resolved technique definition.", rowIndex, XAssetType.Techset));
                if (provenance.InlineOwnerRaw is not null)
                    diagnostics.Add(new(path, "Packed slot provenance cannot declare an inline owner.", rowIndex, XAssetType.Techset));
                if (provenance.ImportedPackedRaw is not { } importedRaw ||
                    XPointerCodec.GetType(importedRaw) != PointerType.Offset)
                    diagnostics.Add(new(path, "Packed slot provenance requires an exact packed source raw.", rowIndex, XAssetType.Techset));
                break;
            default:
                diagnostics.Add(new(path, $"Unsupported slot source form {provenance.SourceForm}.", rowIndex, XAssetType.Techset));
                break;
        }
    }
    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex) { if (value is { } text && !AssetBodyEmitterHelpers.IsLatin1CString(text)) diagnostics.Add(new(path, "Value must be a Latin-1 C string.", rowIndex, XAssetType.Techset)); }
    private static void ValidateShaderReference(SymbolicXAssetReference? value, XAssetType type, string path, List<EmissionError> diagnostics, int? rowIndex) { if (value is not null && (value.AssetType != type || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) diagnostics.Add(new(path, $"Reference must be a comma-prefixed external {type} identity.", rowIndex, XAssetType.Techset)); }
    private sealed class TechniquePlan(EmissionAddress rootAddress, List<EmissionBlockSegment> source) { public EmissionAddress RootAddress { get; } = rootAddress; public List<EmissionBlockSegment> Source { get; } = source; }
    private sealed record DirectValuePlan(
        int PointerRaw,
        EmissionBlockSegment? Segment);
    private sealed class PassPlan(TechniquePassBuildData data, EmissionAddress address) { public TechniquePassBuildData Data { get; } = data; public EmissionBlockSegment Root { get; set; } = new(address, []); public EmissionBlockSegment? VertexDeclaration { get; set; } public int VertexDeclarationRaw { get; set; } public AssetBodyEmission? VertexShader { get; set; } public int VertexShaderRaw { get; set; } public AssetBodyEmission? PixelShader { get; set; } public int PixelShaderRaw { get; set; } public EmissionBlockSegment? Args { get; set; } public List<EmissionBlockSegment> SourceChildren { get; } = []; }
}
