using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Execution;
using IW4.Render.Shaders;
using IW4.Render.Assets;
using IW4.Render.Materials;

namespace IW4.Render.Geometry;

internal static class MaterialVertexInputBindingPlanner
{
    internal static ShaderVertexInputBinding[] Resolve(
        MaterialTechniqueSetAsset? techniqueSet, RenderAssetLookup lookup,
        MaterialPassIdentity pass, int? fixedVertexSourceBackendRow = null)
    {
        if (techniqueSet is null || pass.TechniquePass.TechniqueSlot < 0 || pass.TechniquePass.PassIndex < 0) return [];
        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techniqueSet).FirstOrDefault(candidate => candidate.Index == pass.TechniquePass.TechniqueSlot);
        if (slot?.Technique is not { } technique || (uint)pass.TechniquePass.PassIndex >= (uint)technique.Passes.Count) return [];
        MaterialPassAsset sourcePass = technique.Passes[pass.TechniquePass.PassIndex];
        SelectedPassProgramSources sources = lookup.ResolveSources(techniqueSet, technique, pass.TechniquePass.PassIndex, sourcePass);
        return Create(techniqueSet, technique.Flags, sources.VertexDeclaration,
            sources.VertexProgram.HasProgramData ? RsxShaderTranslator.ReadVertexInputDestinations(sources.VertexProgram.Data.ToArray()) : null,
            fixedVertexSourceBackendRow);
    }
    internal static ShaderVertexInputBinding[] Create(
        MaterialTechniqueSetAsset? techset, ushort techniqueFlags,
        MaterialVertexDeclarationAsset? vertexDecl,
        IReadOnlyList<int>? requiredInputs, int? fixedVertexSourceBackendRow = null)
    {
        if (vertexDecl is null || (techset is null && !fixedVertexSourceBackendRow.HasValue)) return [];
        int format = fixedVertexSourceBackendRow ?? WorldVertexLayout.ResolveEffectiveBackendRow(techniqueFlags, techset!.WorldVertexFormat);
        var bindings = new List<ShaderVertexInputBinding>();
        int routeCount = Math.Min(vertexDecl.StreamCount, (byte)vertexDecl.Routing.Count);
        for (int index = 0; index < routeCount; index++)
        {
            MaterialVertexStreamRouting route = vertexDecl.Routing[index];
            if (!WorldVertexLayout.TryGetSource(format, route.Source, out WorldVertexSource source))
            { bindings.Add(new(index, route.Source, route.Dest, 0, 0, 0, 0, 0, "Unknown")); continue; }
            _ = WorldVertexLayout.TryGetStreamStride(format, source.StreamIndex, out byte stride);
            bindings.Add(new(index, route.Source, route.Dest, source.StreamIndex, stride, source.ByteOffset, source.ComponentCount, source.RsxType, TypeName(source.RsxType)));
        }
        if (requiredInputs is null) return bindings.ToArray();
        var selected = bindings.Where(binding => requiredInputs.Contains(binding.Destination)).ToList();
        foreach (int destination in requiredInputs.Where(destination => selected.All(binding => binding.Destination != destination)))
            selected.Add(new(-1, 0, checked((byte)destination), 0, 0, 0, 0, 0, "Unknown"));
        return selected.OrderBy(binding => binding.Destination).ToArray();
    }

    private static string TypeName(byte type) => type switch { 0x00 => "B8G8R8A8_UNORM", 0x01 => "V16_SNORM", 0x02 => "V32_FLOAT", 0x03 => "V16_FLOAT", 0x04 => "U8_UNORM", 0x05 => "V16_SSCALED", 0x06 => "S11_11_10_NR", 0x07 => "U8_USCALED", _ => "Unknown" };
}
