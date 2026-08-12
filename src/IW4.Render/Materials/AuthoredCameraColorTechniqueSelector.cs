using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Shaders;

namespace IW4.Render.Materials;

/// <summary>
/// Resolves one deterministic EditorPreview camera-color technique group.
/// The selected slot and authored pass order are shared by map static models
/// and the standalone XModel viewer.
/// </summary>
internal static class AuthoredCameraColorTechniqueSelector
{
    internal static AuthoredCameraColorTechniqueSelection Select(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techniqueSet,
        RenderAssetLookup lookup,
        int? exactTechniqueSlot = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(lookup);
        if (techniqueSet is null)
        {
            return AuthoredCameraColorTechniqueSelection.Blocked(
                "techniqueSet=unresolved");
        }

        IReadOnlyList<MaterialTechniqueSlot> slots =
            lookup.ResolveTechniqueSlots(techniqueSet);
        IReadOnlyList<int> orderedSlots = exactTechniqueSlot is { } exact
            ? [exact]
            : MapRenderEditorTechniquePolicy.OrderCandidateSlots(slots);
        string lastBlocker = "cameraColorTechnique=notFound";
        foreach (int slotIndex in orderedSlots)
        {
            MaterialTechniqueSlot? slot = slots.FirstOrDefault(candidate =>
                candidate.Index == slotIndex);
            if (slot?.Technique is not { } technique)
            {
                lastBlocker = $"techniqueSlot{slotIndex}=unresolved";
                if (exactTechniqueSlot.HasValue)
                {
                    return new AuthoredCameraColorTechniqueSelection(
                        slotIndex,
                        string.Empty,
                        [],
                        lastBlocker);
                }
                continue;
            }
            if (technique.PassCount != technique.Passes.Count)
            {
                lastBlocker =
                    $"techniqueSlot{slotIndex}=passCountMismatch(" +
                    $"declared={technique.PassCount},loaded={technique.Passes.Count})";
                return new AuthoredCameraColorTechniqueSelection(
                    slotIndex,
                    technique.Name ?? string.Empty,
                    [],
                    lastBlocker);
            }

            var passes = new List<AuthoredCameraColorPassSelection>(
                technique.Passes.Count);
            string? groupBlocker = null;
            for (int passIndex = 0;
                 passIndex < technique.Passes.Count;
                 passIndex++)
            {
                MaterialPassAsset sourcePass = technique.Passes[passIndex];
                IReadOnlyList<MaterialShaderArgumentAsset> arguments =
                    lookup.ResolveShaderArgs(sourcePass);
                int unresolvedCodeSamplerCount = arguments.Count(argument =>
                    argument.Type ==
                        MaterialShaderArgumentType.CodePixelSampler &&
                    !IsMappedRuntimeCodeSampler(
                        unchecked((uint)argument.ArgumentRaw)));
                bool stateReady = MapRenderStateDecoder.TryDecode(
                    material,
                    slotIndex,
                    passIndex,
                    lookup,
                    out MapRenderState state);
                if (!stateReady)
                    state = MapRenderState.Default;
                string passClass = MapRenderPassClassifier.Classify(
                    technique.Name ?? string.Empty,
                    state,
                    unresolvedCodeSamplerCount);
                if (!MapRenderPassClassifier.CanSubmitToCameraColor(passClass))
                {
                    groupBlocker =
                        $"techniqueSlot{slotIndex}.pass{passIndex}=" +
                        $"nonCameraColor({passClass})";
                    break;
                }

                MaterialVertexDeclarationAsset? vertexDeclaration =
                    sourcePass.VertexDeclaration ??
                    lookup.ResolveVertexDeclaration(
                        sourcePass.VertexDeclPointer);
                SelectPrimaryMaterialSampler(
                    material,
                    lookup,
                    sourcePass,
                    vertexDeclaration,
                    arguments,
                    out int samplerArgumentIndex,
                    out ushort samplerDestination,
                    out uint samplerHash,
                    out MaterialTextureDef? primaryTexture,
                    out GfxImageAsset? primaryImage,
                    out byte textureCoordinateSource,
                    out bool textureCoordinateSourceIsEngineRouted);
                passes.Add(new AuthoredCameraColorPassSelection(
                    sourcePass,
                    arguments,
                    new MapRenderMaterialPass(
                        material.Info.Name ?? string.Empty,
                        techniqueSet.Name ?? string.Empty,
                        slotIndex,
                        technique.Name ?? string.Empty,
                        passClass,
                        passIndex,
                        samplerArgumentIndex,
                        samplerDestination,
                        samplerHash,
                        primaryTexture?.Semantic ?? 0,
                        textureCoordinateSource,
                        sourcePass.CustomSamplerFlags),
                    state,
                    primaryTexture,
                    primaryImage,
                    unresolvedCodeSamplerCount,
                    textureCoordinateSourceIsEngineRouted,
                    stateReady));
            }

            if (groupBlocker is null &&
                passes.Count == technique.Passes.Count &&
                passes.Count > 0)
            {
                return new AuthoredCameraColorTechniqueSelection(
                    slotIndex,
                    technique.Name ?? string.Empty,
                    passes,
                    string.Empty);
            }

            lastBlocker = groupBlocker ??
                $"techniqueSlot{slotIndex}=noCameraColorPass";
            // EditorPreview owns one explicit normal-camera selector policy:
            // the first populated candidate slot is authoritative. A blocked
            // selected group must remain visible as blocked instead of
            // silently changing the material technique.
            return new AuthoredCameraColorTechniqueSelection(
                slotIndex,
                technique.Name ?? string.Empty,
                [],
                lastBlocker);
        }

        return AuthoredCameraColorTechniqueSelection.Blocked(lastBlocker);
    }

    internal static bool TryResolveMaterialTexture(
        MaterialAsset material,
        RenderAssetLookup lookup,
        uint? preferredHash,
        bool requireColor,
        out MaterialTextureDef? texture,
        out GfxImageAsset? image) =>
        MapRenderMaterialTextureSelector.TryResolveFirst(
            material.Textures,
            preferredHash,
            requireColor ? (byte)0x02 : null,
            candidate => ResolveCanonicalImage(candidate, lookup),
            out texture,
            out image);

    private static GfxImageAsset? ResolveCanonicalImage(
        MaterialTextureDef candidate,
        RenderAssetLookup lookup)
    {
        GfxImageAsset? loaded = candidate.Water?.Image ?? candidate.Image;
        if (loaded is not null &&
            lookup.TryResolveCanonicalImage(
                loaded,
                out GfxImageAsset? canonicalLoaded))
        {
            return canonicalLoaded;
        }

        GfxImageAsset? resolved = candidate.Water is { } water
            ? lookup.ResolveImage(water.ImagePointer.Untyped)
            : lookup.ResolveImage(candidate.DataPointer);
        return resolved is not null &&
            lookup.TryResolveCanonicalImage(
                resolved,
                out GfxImageAsset? canonicalResolved)
                ? canonicalResolved
                : null;
    }

    internal static int FindMaterialTextureOrdinal(
        MaterialAsset material,
        MaterialTextureDef? texture)
    {
        if (texture is null)
            return -1;

        for (int ordinal = 0; ordinal < material.Textures.Count; ordinal++)
        {
            if (ReferenceEquals(material.Textures[ordinal], texture))
                return ordinal;
        }

        return -1;
    }

    private static void SelectPrimaryMaterialSampler(
        MaterialAsset material,
        RenderAssetLookup lookup,
        MaterialPassAsset sourcePass,
        MaterialVertexDeclarationAsset? vertexDeclaration,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        out int samplerArgumentIndex,
        out ushort samplerDestination,
        out uint samplerHash,
        out MaterialTextureDef? primaryTexture,
        out GfxImageAsset? primaryImage,
        out byte textureCoordinateSource,
        out bool textureCoordinateSourceIsEngineRouted)
    {
        samplerArgumentIndex = -1;
        samplerDestination = 0;
        samplerHash = 0;
        primaryTexture = null;
        primaryImage = null;
        textureCoordinateSource = XSurfaceVertexDecoder.DefaultTexCoordSourceIndex;
        textureCoordinateSourceIsEngineRouted = false;
        (int SemanticRank, int RouteRank, int DestinationRank, int ArgumentIndex)
            bestRank = (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

        for (int argumentIndex = 0;
             argumentIndex < arguments.Count;
             argumentIndex++)
        {
            MaterialShaderArgumentAsset argument = arguments[argumentIndex];
            if (argument.Type !=
                MaterialShaderArgumentType.MaterialPixelSampler)
            {
                continue;
            }

            uint candidateHash = unchecked((uint)argument.ArgumentRaw);
            if (!TryResolveMaterialTexture(
                    material,
                    lookup,
                    candidateHash,
                    requireColor: false,
                    out MaterialTextureDef? candidateTexture,
                    out GfxImageAsset? candidateImage) ||
                candidateTexture is null ||
                candidateImage is null)
            {
                continue;
            }

            bool engineRouted = RsxShaderInputRouter.TrySelectSamplerSource(
                sourcePass,
                argument,
                vertexDeclaration,
                candidateTexture.Semantic,
                out byte routedSource);
            var rank = (
                SemanticRank: candidateTexture.Semantic == 0x02 ? 0 : 1,
                RouteRank: engineRouted ? 0 : 1,
                DestinationRank: argument.Dest == 0 ? 0 : 1,
                ArgumentIndex: argumentIndex);
            if (rank.CompareTo(bestRank) >= 0)
                continue;

            bestRank = rank;
            samplerArgumentIndex = argumentIndex;
            samplerDestination = argument.Dest;
            samplerHash = candidateHash;
            primaryTexture = candidateTexture;
            primaryImage = candidateImage;
            textureCoordinateSource = engineRouted
                ? routedSource
                : XSurfaceVertexDecoder.DefaultTexCoordSourceIndex;
            textureCoordinateSourceIsEngineRouted = engineRouted;
        }
    }

    private static bool IsMappedRuntimeCodeSampler(uint raw) =>
        MapRenderCodePixelSamplerAbi.TryResolve(
            raw,
            out MapRenderCodePixelSamplerAbiEntry entry) &&
        entry.HasRuntimeRequirement;
}

internal sealed record AuthoredCameraColorTechniqueSelection(
    int TechniqueSlot,
    string TechniqueName,
    IReadOnlyList<AuthoredCameraColorPassSelection> Passes,
    string Blocker)
{
    internal static AuthoredCameraColorTechniqueSelection Blocked(
        string blocker) => new(-1, string.Empty, [], blocker);
}

internal sealed record AuthoredCameraColorPassSelection(
    MaterialPassAsset SourcePass,
    IReadOnlyList<MaterialShaderArgumentAsset> Arguments,
    MapRenderMaterialPass Pass,
    MapRenderState State,
    MaterialTextureDef? PrimaryTexture,
    GfxImageAsset? PrimaryImage,
    int UnresolvedCodeSamplerCount,
    bool TexCoordSourceIsEngineRouted,
    bool StateReady);
