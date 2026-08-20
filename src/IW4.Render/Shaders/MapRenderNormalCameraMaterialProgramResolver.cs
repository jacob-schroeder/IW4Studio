using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Materials;
using IW4.Render.Scheduling.Lifecycle;
using IW4.Render.Techniques;

namespace IW4.Render.Shaders;

/// <summary>
/// Exact backend-neutral material program selected from one canonical asset
/// pool revision for a normal-camera fullscreen draw.
/// </summary>
internal sealed record MapRenderNormalCameraMaterialProgramResolution(
    RsxShaderTranslationResult Translation,
    RenderState RenderState);

/// <summary>
/// Resolves and validates the canonical material, technique, program, state,
/// and shader-argument identity shared by native fullscreen backends.
/// </summary>
internal static class MapRenderNormalCameraMaterialProgramResolver
{
    internal static MapRenderNormalCameraMaterialProgramResolution ResolveExact(
        RenderAssetLookup lookup,
        long revision,
        MapRenderNormalCameraMaterialAssetContract contract,
        IReadOnlyList<int> expectedVertexInputDestinations,
        IReadOnlyList<ushort> expectedCodePixelSourceRows,
        IReadOnlyList<int> expectedVertexConstantDestinations)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(expectedVertexInputDestinations);
        ArgumentNullException.ThrowIfNull(expectedCodePixelSourceRows);
        ArgumentNullException.ThrowIfNull(expectedVertexConstantDestinations);
        if (!lookup.HasCanonicalAssetPoolRevision(revision))
        {
            throw new InvalidOperationException(
                "Normal-camera fullscreen materials require the exact active canonical asset-pool revision.");
        }
        if (!lookup.TryResolveCanonicalMaterialTechniqueBinding(
                contract.MaterialName,
                revision,
                out MaterialTechniqueBinding? binding))
        {
            throw new InvalidOperationException(
                $"Canonical fullscreen material '{contract.MaterialName}' is unavailable at asset-pool revision {revision}.");
        }

        MaterialAsset material = binding.Material;
        MaterialTechniqueSetAsset techniqueSet = binding.TechniqueSet;
        MaterialTechniqueSlot slot = binding.TechniqueSlots.SingleOrDefault(
                candidate => candidate.Index == contract.TechniqueSlot) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no technique slot {contract.TechniqueSlot}.");
        MaterialTechniqueAsset technique = slot.Technique ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' technique slot {contract.TechniqueSlot} is unresolved.");
        if (!string.Equals(material.Info.Name, contract.MaterialName,
                StringComparison.Ordinal) ||
            !string.Equals(techniqueSet.Name, contract.TechniqueSetName,
                StringComparison.Ordinal) ||
            !string.Equals(technique.Name, contract.TechniqueName,
                StringComparison.Ordinal) ||
            technique.Flags != contract.TechniqueFlags ||
            technique.PassCount != contract.PassCount ||
            technique.Passes.Count != contract.PassCount)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' no longer matches its PS3 material/technique identity contract.");
        }

        MaterialPassAsset pass = technique.Passes.Single();
        MaterialShaderAsset vertex = lookup.ResolveVertexShader(
                pass.VertexShaderPointer,
                pass.VertexShader) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no vertex program.");
        MaterialShaderAsset pixel = lookup.ResolvePixelShader(
                pass.PixelShaderPointer,
                pass.PixelShader) ??
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no pixel program.");
        if (!string.Equals(vertex.Name, contract.VertexShaderName,
                StringComparison.Ordinal) ||
            !string.Equals(pixel.Name, contract.PixelShaderName,
                StringComparison.Ordinal) ||
            vertex.Data is not { } vertexData ||
            pixel.Data is not { } pixelData)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' shader identities or program bytes are unavailable.");
        }
        IReadOnlyList<MaterialShaderArgumentAsset> arguments =
            lookup.ResolveShaderArgs(pass);
        if (arguments.Count != contract.Arguments.Count ||
            !arguments.Select(argument =>
                    (argument.Type,
                     argument.Dest,
                     unchecked((uint)argument.ArgumentRaw)))
                .SequenceEqual(contract.Arguments.Select(argument =>
                    (argument.Type,
                     argument.Destination,
                     argument.RawValue))))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' shader arguments no longer match the PS3 contract.");
        }

        if ((uint)contract.TechniqueSlot >=
            (uint)material.StateBitsEntries.Count)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' has no exact state-bits slot row.");
        }
        int stateIndex = material.StateBitsEntries[contract.TechniqueSlot]
            .StateBitsIndex;
        if ((uint)stateIndex >= (uint)material.StateBits.Count)
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' state-bits row is unavailable.");
        }
        IReadOnlyList<uint> loadBits = lookup.ResolveStateLoadBits(
            material.StateBits[stateIndex]);
        if (loadBits.Count < 2 ||
            loadBits[0] != contract.StateBits0 ||
            loadBits[1] != contract.StateBits1)
        {
            string actual = loadBits.Count >= 2
                ? $"0x{loadBits[0]:X8}/0x{loadBits[1]:X8}"
                : $"unavailable ({loadBits.Count} words)";
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' state words " +
                $"{actual} no longer match the PS3 contract " +
                $"0x{contract.StateBits0:X8}/0x{contract.StateBits1:X8}.");
        }

        RsxShaderTranslationResult translation = RsxShaderTranslator.Translate(
            vertexData,
            pixelData,
            pass,
            material);
        if (!translation.ProgramIrReady ||
            translation.Blockers.Count != 0 ||
            !translation.ReadVertexInputDestinations.SequenceEqual(
                expectedVertexInputDestinations) ||
            !translation.ReadVertexConstantDestinations.SequenceEqual(
                expectedVertexConstantDestinations) ||
            !translation.ReadFragmentSamplerDestinations.SequenceEqual([0]) ||
            !translation.CodePixelConstantPatchPlans
                .Select(plan => plan.CodeIndex)
                .SequenceEqual(expectedCodePixelSourceRows) ||
            translation.CodePixelConstantPatchPlans.Any(plan =>
                !plan.IsDirectSourceResolved))
        {
            throw new InvalidOperationException(
                $"Fullscreen material '{contract.MaterialName}' is outside its exact translated program subset.");
        }

        RenderState state = RenderStateDecoder.Decode(
            contract.StateBits0,
            contract.StateBits1,
            commandWordCount: 0);
        return new MapRenderNormalCameraMaterialProgramResolution(
            translation,
            state);
    }
}
