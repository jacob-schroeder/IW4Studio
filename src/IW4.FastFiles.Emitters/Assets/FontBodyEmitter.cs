using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Deterministic Font root, Material pointer topology, and glyph table.</summary>
public sealed class FontBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.Font;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IFontBuildData data)
        {
            diagnostics.Add(new("body", "Font build data does not implement IFontBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "Font name must be a Latin-1 C string.", rowIndex, AssetType));
        ValidateMaterial(data.MaterialReference, data.MaterialLink, "material", diagnostics, rowIndex);
        ValidateMaterial(data.GlowMaterialReference, data.GlowMaterialLink, "glowMaterial", diagnostics, rowIndex);
        for (int index = 0; index < data.Glyphs.Count; index++)
        {
            FontGlyphBuildData glyph = data.Glyphs[index];
            if (!float.IsFinite(glyph.S0) || !float.IsFinite(glyph.T0) || !float.IsFinite(glyph.S1) || !float.IsFinite(glyph.T1))
                diagnostics.Add(new($"glyphs[{index}]", "Font glyph texture metrics must be finite.", rowIndex, AssetType));
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IFontBuildData data = (IFontBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x18, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = segments.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        int afterName = segments.Count;
        plan.Pop(XFileBlockType.LARGE);
        MaterialPlan material = PlanMaterial(
            data.MaterialReference,
            data.MaterialLink,
            plan,
            segments,
            new EmissionAddress(root.Block, checked(root.Offset + 0x0c)),
            "Font.Material");
        MaterialPlan glowMaterial = PlanMaterial(
            data.GlowMaterialReference,
            data.GlowMaterialLink,
            plan,
            segments,
            new EmissionAddress(root.Block, checked(root.Offset + 0x10)),
            "Font.GlowMaterial");
        EmissionAddress? glyphAddress = null;
        EmissionBlockSegment? glyphSegment = null;
        if (data.Glyphs.Count != 0)
        {
            plan.Push(XFileBlockType.LARGE);
            glyphAddress = plan.Allocate(checked(data.Glyphs.Count * 0x18), 4);
            plan.Pop(XFileBlockType.LARGE);
            var glyphWriter = new XSourceWriter();
            foreach (FontGlyphBuildData glyph in data.Glyphs)
            {
                glyphWriter.WriteUInt16(glyph.Letter); glyphWriter.WriteByte(unchecked((byte)glyph.X0)); glyphWriter.WriteByte(unchecked((byte)glyph.Y0)); glyphWriter.WriteByte(glyph.Dx); glyphWriter.WriteByte(glyph.PixelWidth); glyphWriter.WriteByte(glyph.PixelHeight); glyphWriter.WriteByte(glyph.Padding);
                glyphWriter.WriteSingle(glyph.S0); glyphWriter.WriteSingle(glyph.T0); glyphWriter.WriteSingle(glyph.S1); glyphWriter.WriteSingle(glyph.T1);
            }
            glyphSegment = new EmissionBlockSegment(glyphAddress.Value, glyphWriter.ToArray());
            segments.Add(glyphSegment);
        }
        plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(data.PixelHeight); writer.WriteInt32(data.Glyphs.Count);
        writer.WriteInt32(material.PointerRaw); writer.WriteInt32(glowMaterial.PointerRaw); writer.WriteInt32(glyphAddress is null ? 0 : -1);
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); segments.Add(rootSegment);
        // Loader order is root, Font name, each nested Material body (each
        // with its own TEMP/LARGE transition), then the glyph table.
        var source = new List<EmissionBlockSegment> { rootSegment };
        source.AddRange(segments.Skip(beforeName).Take(afterName - beforeName));
        source.AddRange(material.SourceSegments);
        source.AddRange(glowMaterial.SourceSegments);
        if (glyphSegment is not null) source.Add(glyphSegment);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static MaterialPlan PlanMaterial(
        SymbolicXAssetReference? reference,
        NestedXAssetBuildLink? link,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        EmissionAddress ownerCell,
        string owner)
    {
        if (link is { } retained)
        {
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                retained,
                plan,
                all,
                ownerCell,
                owner);
            return new MaterialPlan(nested.PointerRaw, nested.Source);
        }
        if (reference is null) return new MaterialPlan(0, []);
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0xa8, 4);
        plan.Push(XFileBlockType.LARGE); EmissionAddress name = plan.Allocate(checked(reference.OriginalSerializedName.Length + 1)); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(-1); rootWriter.Reserve(0xa4);
        var nameWriter = new XSourceWriter(); nameWriter.WriteLatin1CString(reference.OriginalSerializedName);
        var emission = new AssetBodyEmission(XAssetType.Material, root, [new EmissionBlockSegment(root, rootWriter.ToArray()), new EmissionBlockSegment(name, nameWriter.ToArray())]);
        all.AddRange(emission.Segments);
        return new MaterialPlan(-1, emission.SourceSegments);
    }

    private static void ValidateMaterial(
        SymbolicXAssetReference? reference,
        NestedXAssetBuildLink? link,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        if (link is not null)
        {
            diagnostics.AddRange(NestedXAssetEmission.Validate(
                link,
                XAssetType.Material,
                path,
                rowIndex,
                XAssetType.Font));
            if (reference != link.Reference)
            {
                diagnostics.Add(new(
                    path,
                    "Retained Material link identity must equal the parallel Material reference.",
                    rowIndex,
                    XAssetType.Font));
            }
            return;
        }
        if (reference is null) return;
        if (reference.AssetType != XAssetType.Material || !reference.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(reference.OriginalSerializedName))
            diagnostics.Add(new(path, "Font Material references must be comma-prefixed Latin-1 external Material identities.", rowIndex, XAssetType.Font));
    }

    private sealed record MaterialPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> SourceSegments);
}
