using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Deterministic Font root, external Material references, and glyph table.
/// Material links are deliberately represented only as comma-prefixed external
/// references; target-owned Material rows retain their own serialization root.</summary>
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
        ValidateReference(data.MaterialReference, "material", diagnostics, rowIndex);
        ValidateReference(data.GlowMaterialReference, "glowMaterial", diagnostics, rowIndex);
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
        var material = PlanMaterialReference(data.MaterialReference, plan, segments);
        var glowMaterial = PlanMaterialReference(data.GlowMaterialReference, plan, segments);
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
        writer.WriteInt32(material?.RootAddress is null ? 0 : -1); writer.WriteInt32(glowMaterial?.RootAddress is null ? 0 : -1); writer.WriteInt32(glyphAddress is null ? 0 : -1);
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); segments.Add(rootSegment);
        // Loader order is root, Font name, each nested Material body (each
        // with its own TEMP/LARGE transition), then the glyph table.
        var source = new List<EmissionBlockSegment> { rootSegment };
        source.AddRange(segments.Skip(beforeName).Take(afterName - beforeName));
        if (material is not null) source.AddRange(material.SourceSegments);
        if (glowMaterial is not null) source.AddRange(glowMaterial.SourceSegments);
        if (glyphSegment is not null) source.Add(glyphSegment);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static AssetBodyEmission? PlanMaterialReference(SymbolicXAssetReference? reference, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (reference is null) return null;
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0xa8, 4);
        plan.Push(XFileBlockType.LARGE); EmissionAddress name = plan.Allocate(checked(reference.OriginalSerializedName.Length + 1)); plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(-1); rootWriter.Reserve(0xa4);
        var nameWriter = new XSourceWriter(); nameWriter.WriteLatin1CString(reference.OriginalSerializedName);
        var emission = new AssetBodyEmission(XAssetType.Material, root, [new EmissionBlockSegment(root, rootWriter.ToArray()), new EmissionBlockSegment(name, nameWriter.ToArray())]);
        all.AddRange(emission.Segments);
        return emission;
    }

    private static void ValidateReference(SymbolicXAssetReference? reference, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (reference is null) return;
        if (reference.AssetType != XAssetType.Material || !reference.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(reference.OriginalSerializedName))
            diagnostics.Add(new(path, "Font Material references must be comma-prefixed Latin-1 external Material identities.", rowIndex, XAssetType.Font));
    }
}
