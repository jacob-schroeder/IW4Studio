using IW4.Render.Techniques;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Execution.FixedFunction;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.UI;

public static partial class UiMaterialDrawPlanner
{
    private static bool HasExpectedPrograms(
        SelectedPassProgramSources sources) =>
        sources.VertexProgram.HasProgramData &&
        sources.PixelProgram.HasProgramData &&
        string.Equals(
            sources.VertexProgram.Name,
            ProgramName,
            StringComparison.Ordinal) &&
        string.Equals(
            sources.PixelProgram.Name,
            ProgramName,
            StringComparison.Ordinal);

    private static bool HasExpectedVertexDeclaration(
        MaterialVertexDeclarationAsset? declaration)
    {
        if (declaration is null ||
            declaration.StreamCount != 3 ||
            declaration.Routing.Count < 3)
        {
            return false;
        }

        return HasRoute(
                   declaration.Routing[0],
                   MaterialStreamSource.Position,
                   MaterialStreamDestination.Position) &&
               HasRoute(
                   declaration.Routing[1],
                   MaterialStreamSource.Color,
                   MaterialStreamDestination.Color0) &&
               HasRoute(
                   declaration.Routing[2],
                   MaterialStreamSource.TexCoord0,
                   MaterialStreamDestination.TexCoord0);
    }

    private static bool HasRoute(
        MaterialVertexStreamRouting route,
        MaterialStreamSource source,
        MaterialStreamDestination destination) =>
        route.Source == source && route.Dest == destination;

    private static bool IsSupportedTextureTarget(
        UiMaterialTextureResource resource) =>
        resource.Width > 0 &&
        resource.Height > 0 &&
        resource.Depth == 1 &&
        resource.MapType == MapType.TwoDimensional &&
        resource.DimensionCount == GfxImageDimension.TwoDimensional &&
        !resource.IsCubemap;

    private static bool TryResolveExpectedSampler(
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        out int samplerArgumentIndex,
        out MaterialShaderArgumentAsset? samplerArgument)
    {
        bool hasWorld = arguments.Any(argument =>
            argument.Type == MaterialShaderArgumentType.CodeVertexConst &&
            argument.Dest == World0Destination &&
            argument.CodeConstant.PackedValue == World0Argument);
        bool hasViewProjection = arguments.Any(argument =>
            argument.Type == MaterialShaderArgumentType.CodeVertexConst &&
            argument.Dest == ViewProjectionDestination &&
            argument.CodeConstant.PackedValue ==
            ViewProjectionArgument);
        var matches = arguments
            .Select((argument, index) => (Argument: argument, Index: index))
            .Where(value =>
                value.Argument.Type ==
                    MaterialShaderArgumentType.MaterialPixelSampler &&
                value.Argument.Dest == BaseColorSamplerDestination &&
                value.Argument.MaterialNameHash ==
                    BaseColorSamplerHash)
            .ToArray();
        if (!hasWorld || !hasViewProjection || matches.Length != 1)
        {
            samplerArgumentIndex = -1;
            samplerArgument = null;
            return false;
        }

        samplerArgumentIndex = matches[0].Index;
        samplerArgument = matches[0].Argument;
        return true;
    }

    private static IEnumerable<string> FindStateBlockers(
        RenderState state)
    {
        if (!state.HasState)
            yield return "state missing";
        if (state.ShaderPackerSrgbEnabled)
            yield return "shader-packer sRGB enabled";
        if (state.ColorMask != RsxColorMask.Rgba)
            yield return $"color mask 0x{(uint)state.ColorMask:X8}";
        if (AlphaTest.Resolve(state) is null)
        {
            yield return
                $"alpha test {state.AlphaTestEnabled}/" +
                $"0x{(uint)state.AlphaFunc:X4}/{state.AlphaRef}";
        }
        if (Cull.Resolve(state) is null)
        {
            yield return
                $"cull tuple {state.CullEnabled}/0x{(uint)state.CullFace:X4}";
        }
        if (state.PolygonMode != RsxPolygonMode.Fill)
            yield return $"polygon mode 0x{(uint)state.PolygonMode:X4}";
        if (!RenderBlendDecoder.TryResolve(
                state,
                out RenderBlendStateDescriptor blend) ||
            !blend.Enabled ||
            blend.ColorOperation != RenderBlendOperation.Add ||
            blend.AlphaOperation != RenderBlendOperation.Add ||
            blend.SourceColorFactor != RenderBlendFactor.SourceAlpha ||
            blend.SourceAlphaFactor != RenderBlendFactor.SourceAlpha ||
            blend.DestinationColorFactor !=
                RenderBlendFactor.OneMinusSourceAlpha ||
            blend.DestinationAlphaFactor !=
                RenderBlendFactor.OneMinusSourceAlpha)
        {
            yield return "non-source-alpha blend tuple";
        }
        if (state.DepthTestEnabled || state.DepthWriteEnabled)
            yield return "depth test/write enabled";
        if (state.StencilEnabled)
            yield return "stencil enabled";
        if (state.PolygonOffsetMode == RenderPolygonOffsetMode.Explicit)
            yield return "polygon offset enabled";
    }

    private static UiMaterialAtlasState ResolveAtlas(
        MaterialAsset material,
        UiMaterialUvAuthority uvAuthority,
        ICollection<UiMaterialExecutionDiagnostic> diagnostics)
    {
        byte rows = material.Info.TextureAtlasRowCount;
        byte columns = material.Info.TextureAtlasColumnCount;
        if ((rows == 0) != (columns == 0))
        {
            diagnostics.Add(new UiMaterialExecutionDiagnostic(
                UiMaterialExecutionDiagnosticCode.InvalidTextureAtlas,
                UiDiagnosticSeverity.Blocker,
                $"Material atlas dimensions {rows}x{columns} are incomplete."));
            return new UiMaterialAtlasState(
                rows,
                columns,
                UiMaterialAtlasStatus.InvalidDimensions);
        }
        if (rows == 0)
        {
            return new UiMaterialAtlasState(
                rows,
                columns,
                UiMaterialAtlasStatus.NotAuthored);
        }
        if (rows == 1 && columns == 1)
        {
            return new UiMaterialAtlasState(
                rows,
                columns,
                UiMaterialAtlasStatus.SingleCellFullTexture);
        }
        if (uvAuthority != UiMaterialUvAuthority.EvaluatedAtlasFrame)
        {
            diagnostics.Add(new UiMaterialExecutionDiagnostic(
                UiMaterialExecutionDiagnosticCode
                    .TextureAtlasEvaluationRequired,
                UiDiagnosticSeverity.Blocker,
                $"Material atlas {rows}x{columns} requires evaluated frame " +
                "UVs before exact execution."));
            return new UiMaterialAtlasState(
                rows,
                columns,
                UiMaterialAtlasStatus.EvaluationRequired);
        }

        return new UiMaterialAtlasState(
            rows,
            columns,
            UiMaterialAtlasStatus.EvaluatedUvs);
    }

    private static int IndexOfReference(
        IReadOnlyList<MaterialTextureDef> rows,
        MaterialTextureDef target)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            if (ReferenceEquals(rows[index], target))
                return index;
        }

        throw new InvalidOperationException(
            "A selected material texture row lost its table identity.");
    }

    private static UiMaterialDrawPlan Block(
        ICollection<UiMaterialExecutionDiagnostic> diagnostics,
        UiMaterialExecutionDiagnosticCode code,
        string message)
    {
        diagnostics.Add(new UiMaterialExecutionDiagnostic(
            code,
            UiDiagnosticSeverity.Blocker,
            message));
        return new UiMaterialDrawPlan(null, diagnostics.ToArray());
    }
}
