using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.UI;

/// <summary>
/// Builds exact, resource-neutral packets for the PS3-proven IW4 UI material
/// path: techset 2d, slot 4, pass trivial_vertcol_simple2d. Unsupported or
/// incomplete material graphs fail closed and leave texture approximation to
/// <see cref="UiMaterialPreviewPlanner"/>.
/// </summary>
public static partial class UiMaterialDrawPlanner
{
    public const int TechniqueSlot = 4;
    public const int PassIndex = 0;
    public const string TechniqueSetName = "2d";
    public const string TechniqueName = "trivial_vertcol_simple2d";
    public const string ProgramName = "trivial_vertcol_simple.hlsl";

    private const uint World0Argument = 0x005F0004;
    private const ushort World0Destination = 4;
    private const uint ViewProjectionArgument = 0x00530004;
    private const ushort ViewProjectionDestination = 0;
    private const uint BaseColorSamplerHash =
        MapRenderEditorMaterialTextureRoleClassifier.BaseColorHash;
    private const ushort BaseColorSamplerDestination = 0;

    private static readonly MapRenderShaderVertexInputBinding[] VertexInputs =
    [
        new(
            RouteIndex: 0,
            Source: 0,
            Destination: 0,
            StreamIndex: 0,
            Stride: 40,
            Offset: 0,
            ComponentCount: 4,
            RsxType: 0x02,
            RsxTypeName: "V32_FLOAT"),
        new(
            RouteIndex: 1,
            Source: 1,
            Destination: 3,
            StreamIndex: 0,
            Stride: 40,
            Offset: 16,
            ComponentCount: 4,
            RsxType: 0x02,
            RsxTypeName: "V32_FLOAT"),
        new(
            RouteIndex: 2,
            Source: 2,
            Destination: 8,
            StreamIndex: 0,
            Stride: 40,
            Offset: 32,
            ComponentCount: 2,
            RsxType: 0x02,
            RsxTypeName: "V32_FLOAT")
    ];

    public static UiMaterialDrawPlan Plan(
        UiMaterialDrawRequest request,
        IMaterialExecutionLookup assets,
        Func<int, MaterialTextureDef, UiMaterialTextureResource?>
            resolveTexture)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(resolveTexture);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MaterialName);
        ArgumentNullException.ThrowIfNull(request.Quad);
        if (request.DrawOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "UI material draw order cannot be negative.");
        }
        if (request.CanonicalPoolRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The canonical asset-pool revision cannot be negative.");
        }

        var diagnostics = new List<UiMaterialExecutionDiagnostic>();
        if (!assets.TryResolveCanonicalMaterialTechniqueBinding(
                request.MaterialName,
                request.CanonicalPoolRevision,
                out MapRenderMaterialTechniqueBinding? binding) ||
            binding is null)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.CanonicalMaterialUnavailable,
                $"Material '{request.MaterialName}' is not a complete active " +
                $"canonical provider at revision " +
                $"{request.CanonicalPoolRevision}.");
        }

        MaterialAsset material = binding.Material;
        MaterialTechniqueSetAsset techniqueSet = binding.TechniqueSet;
        if (material.RuntimeAddress?.AssetPoolAddress is not
            { } canonicalMaterialSlot)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.CanonicalMaterialUnavailable,
                $"Material '{request.MaterialName}' has no canonical asset-" +
                "pool slot identity.");
        }
        if (!string.Equals(
                techniqueSet.Name,
                TechniqueSetName,
                StringComparison.Ordinal))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedTechniqueSet,
                $"Material '{request.MaterialName}' uses technique set " +
                $"'{techniqueSet.Name ?? "<unnamed>"}'; exact UI execution " +
                $"currently supports only '{TechniqueSetName}'.");
        }

        MaterialTechniqueSlot[] matchingSlots = binding.TechniqueSlots
            .Where(slot => slot.Index == TechniqueSlot)
            .ToArray();
        if (matchingSlots.Length != 1 ||
            matchingSlots[0].Technique is not { } technique)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.TechniqueSlotUnavailable,
                $"Technique slot {TechniqueSlot} is not materialized exactly once.");
        }
        if (!string.Equals(
                technique.Name,
                TechniqueName,
                StringComparison.Ordinal))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedTechnique,
                $"Technique slot {TechniqueSlot} contains " +
                $"'{technique.Name ?? "<unnamed>"}'; expected " +
                $"'{TechniqueName}'.");
        }
        if (technique.PassCount != 1 || technique.Passes.Count != 1)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedPassCount,
                $"Technique '{TechniqueName}' must contain exactly one " +
                $"complete pass, but declares {technique.PassCount} and " +
                $"loads {technique.Passes.Count}.");
        }

        MaterialPassAsset sourcePass = technique.Passes[PassIndex];
        MapRenderSelectedPassProgramSources sources = assets.ResolveSources(
            techniqueSet,
            technique,
            new MapRenderSelectedTechniquePass(PassIndex, sourcePass));
        if (!sources.HasCompleteArguments)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.ShaderArgumentsIncomplete,
                $"The selected pass declares {sources.ExpectedArgumentCount} " +
                $"shader arguments but resolves {sources.LoadedArgumentCount}.");
        }
        if (!HasExpectedPrograms(sources))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedShaderProgram,
                $"The selected pass must resolve complete '{ProgramName}' " +
                "vertex and pixel programs.");
        }
        if (!HasExpectedVertexDeclaration(sources.VertexDeclaration))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedVertexDeclaration,
                "The selected pass does not route POSITION, COLOR, and " +
                "TEXCOORD_0 to RSX inputs 0, 3, and 8.");
        }
        if (!TryResolveExpectedSampler(
                sources.Arguments,
                out int samplerArgumentIndex,
                out MaterialShaderArgumentAsset? samplerArgument) ||
            samplerArgument is null)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedShaderArguments,
                "The selected pass does not contain the proven World0, " +
                "ViewProjection, and base-color sampler argument tuple.");
        }

        MaterialTextureDef[] textureRows = material.Textures
            .Where(row => row.NameHash == BaseColorSamplerHash)
            .ToArray();
        if (textureRows.Length != 1)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.TextureRowUnavailable,
                $"The base-color hash 0x{BaseColorSamplerHash:X8} must " +
                $"identify exactly one texture row; found {textureRows.Length}.");
        }

        MaterialTextureDef textureRow = textureRows[0];
        int textureOrdinal = IndexOfReference(material.Textures, textureRow);
        UiMaterialTextureResource? textureResource =
            resolveTexture(textureOrdinal, textureRow);
        if (textureResource is null ||
            string.IsNullOrWhiteSpace(textureResource.ResourceKey) ||
            string.IsNullOrWhiteSpace(textureResource.ImageName) ||
            textureResource.CanonicalPoolRevision !=
                request.CanonicalPoolRevision ||
            textureResource.CanonicalImageSlot.AssetType != XAssetType.Image ||
            textureRow.Image?.RuntimeAddress?.AssetPoolAddress is not
                { } textureRowImageSlot ||
            textureRowImageSlot != textureResource.CanonicalImageSlot ||
            !string.Equals(
                textureRow.Image.Name,
                textureResource.ImageName,
                StringComparison.Ordinal))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.TextureResourceUnavailable,
                "The selected texture row has no immutable canonical host " +
                "resource key and image identity.");
        }
        if (!IsSupportedTextureTarget(textureResource))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedTextureTarget,
                $"Image '{textureResource.ImageName}' is " +
                $"{textureResource.Width}x{textureResource.Height}x" +
                $"{textureResource.Depth}, map type " +
                $"0x{textureResource.MapType:X2}, dimensions " +
                $"{textureResource.DimensionCount}, multi-face " +
                $"0x{textureResource.MultiFaceControl:X2}; exact UI " +
                "execution requires one positive, depth-one PS3 2D " +
                "texture with no additional faces.");
        }

        if (!MapRenderStateDecoder.TryDecode(
                material,
                TechniqueSlot,
                PassIndex,
                assets,
                out MapRenderState state))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.MaterialStateUnavailable,
                "The selected material pass has no decodable PS3 state bits.");
        }
        string[] stateBlockers = FindStateBlockers(state).ToArray();
        if (stateBlockers.Length > 0)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.UnsupportedMaterialState,
                "The selected pass is outside the proven UI state contract: " +
                string.Join(", ", stateBlockers) + ".");
        }

        UiMaterialAtlasState atlas = ResolveAtlas(
            material,
            request.UvAuthority,
            diagnostics);
        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity ==
                UiMaterialExecutionDiagnosticSeverity.Blocker))
        {
            return new UiMaterialDrawPlan(null, diagnostics);
        }

        MapRenderSamplerState samplerState = MapRenderSamplerDecoder.Decode(
            textureRow.SamplerState,
            textureResource.DescriptorPad0F,
            textureResource.DescriptorPad1B);
        var textureBinding = new UiMaterialTextureBinding(
            textureResource.ResourceKey,
            textureResource.ImageName,
            textureResource.CanonicalPoolRevision,
            textureResource.CanonicalImageSlot,
            textureOrdinal,
            textureRow.NameHash,
            textureRow.Semantic,
            textureRow.SamplerState,
            samplerState);
        var passIdentity = new UiMaterialPassIdentity(
            material.Info.Name ?? request.MaterialName,
            request.CanonicalPoolRevision,
            canonicalMaterialSlot,
            techniqueSet.Name ?? TechniqueSetName,
            TechniqueSlot,
            technique.Name ?? TechniqueName,
            PassIndex,
            sources.VertexProgram.Name,
            sources.PixelProgram.Name);
        var renderPass = new MapRenderMaterialPass(
            passIdentity.MaterialName,
            passIdentity.TechniqueSetName,
            TechniqueSlot,
            passIdentity.TechniqueName,
            MapRenderPassClassifier.CameraColor,
            PassIndex,
            samplerArgumentIndex,
            samplerArgument.Dest,
            unchecked((uint)samplerArgument.ArgumentRaw),
            textureRow.Semantic,
            TexCoordSource: 2,
            sourcePass.CustomSamplerFlags);
        var uvRoute = new MapRenderUvRoute(
            "UI packet TEXCOORD_0",
            "UI_MATERIAL_VERTEX",
            TexCoordSource: 2,
            StreamIndex: 0,
            Stride: 40,
            Offset: 32,
            FormatByte0: 0,
            FormatByte1: 0x02,
            MapRenderUvBaseMode.Engine,
            ComponentA: 0,
            ComponentB: 1,
            ScaleU: 1f,
            ScaleV: 1f,
            AddU: 0f,
            AddV: 0f);
        MapRenderMaterialSamplerBinding[] materialSamplers =
        [
            new(
                samplerArgumentIndex,
                samplerArgument.Dest,
                unchecked((uint)samplerArgument.ArgumentRaw),
                textureRow.Semantic,
                textureResource.ImageName,
                Texture: null,
                uvRoute,
                EditorTextureRole:
                    MapRenderEditorMaterialTextureRole.BaseColor,
                TextureTableOrdinal: textureOrdinal,
                ExternalResourceIdentity: textureResource.ResourceKey)
        ];
        var selection = new MapRenderShaderExecutionPassSelection(
            renderPass,
            state,
            textureResource.ImageName);
        MapRenderShaderExecutionContract execution =
            MapRenderShaderExecutionContractFactory.Create(
                material,
                techniqueSet,
                assets,
                selection,
                materialSamplers,
                vertexInputPayloadReady: true,
                vertexInputPayloadBlocker: string.Empty,
                authoredSourcePassAvailable: true,
                explicitVertexInputs: VertexInputs);

        if (!assets.HasCanonicalAssetPoolRevision(
                request.CanonicalPoolRevision))
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.CanonicalRevisionChanged,
                "The canonical asset provider revision changed while the UI " +
                "draw packet was being constructed.");
        }
        if (!execution.ProgramExecutionReady)
        {
            return Block(
                diagnostics,
                UiMaterialExecutionDiagnosticCode.ShaderExecutionBlocked,
                execution.ProgramExecutionStatus);
        }

        var packet = new UiMaterialDrawPacket(
            request.DrawOrder,
            request.Quad,
            passIdentity,
            textureBinding,
            atlas,
            state,
            execution,
            Array.AsReadOnly(diagnostics.ToArray()));
        return new UiMaterialDrawPlan(packet, diagnostics);
    }
}
