using IW4.Assets.Assets.GfxMap;
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
        MaterialTechniqueSetAsset? techset,
        MaterialTechniqueFlags techniqueFlags,
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
            { bindings.Add(new(index, route.Source, route.Dest, 0, 0, 0, 0, RsxVertexElementType.Disabled)); continue; }
            _ = WorldVertexLayout.TryGetStreamStride(format, source.StreamIndex, out byte stride);
            bindings.Add(new(index, route.Source, route.Dest, source.StreamIndex, stride, source.ByteOffset, source.ComponentCount, source.RsxType));
        }
        if (requiredInputs is null) return bindings.ToArray();
        var selected = bindings.Where(binding => requiredInputs.Contains((byte)binding.Destination)).ToList();
        foreach (int destination in requiredInputs.Where(destination => selected.All(binding => (byte)binding.Destination != destination)))
            selected.Add(new(-1, MaterialStreamSource.Position, checked((MaterialStreamDestination)destination), 0, 0, 0, 0, RsxVertexElementType.Disabled));
        return selected.OrderBy(binding => binding.Destination).ToArray();
    }
}
