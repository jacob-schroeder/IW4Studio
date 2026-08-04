using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.UI;

public static partial class UiMaterialDrawPlanner
{
    private static bool HasExpectedPrograms(
        MapRenderSelectedPassProgramSources sources) =>
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

        return HasRoute(declaration.Routing[0], 0, 0) &&
               HasRoute(declaration.Routing[1], 1, 3) &&
               HasRoute(declaration.Routing[2], 2, 8);
    }

    private static bool HasRoute(
        MaterialVertexStreamRouting route,
        byte source,
        byte destination) =>
        route.Source == source && route.Dest == destination;

    private static bool IsSupportedTextureTarget(
        UiMaterialTextureResource resource) =>
        resource.Width > 0 &&
        resource.Height > 0 &&
        resource.Depth == 1 &&
        resource.MapType == 3 &&
        resource.DimensionCount == 2 &&
        resource.MultiFaceControl == 0;

    private static bool TryResolveExpectedSampler(
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        out int samplerArgumentIndex,
        out MaterialShaderArgumentAsset? samplerArgument)
    {
        bool hasWorld = arguments.Any(argument =>
            argument.Type == MaterialShaderArgumentType.CodePrimBegin &&
            argument.Dest == World0Destination &&
            unchecked((uint)argument.ArgumentRaw) == World0Argument);
        bool hasViewProjection = arguments.Any(argument =>
            argument.Type == MaterialShaderArgumentType.CodePrimBegin &&
            argument.Dest == ViewProjectionDestination &&
            unchecked((uint)argument.ArgumentRaw) ==
            ViewProjectionArgument);
        var matches = arguments
            .Select((argument, index) => (Argument: argument, Index: index))
            .Where(value =>
                value.Argument.Type ==
                    MaterialShaderArgumentType.MaterialPixelSampler &&
                value.Argument.Dest == BaseColorSamplerDestination &&
                unchecked((uint)value.Argument.ArgumentRaw) ==
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
        MapRenderState state)
    {
        if (!state.HasState)
            yield return "state missing";
        if (state.ShaderPackerSrgbEnabled)
            yield return "shader-packer sRGB enabled";
        if (state.ColorMask != 0x01010101)
            yield return $"color mask 0x{state.ColorMask:X8}";
        if (!state.AlphaTestEnabled ||
            state.AlphaFunc != 0x0204 ||
            state.AlphaRef != 0)
        {
            yield return
                $"alpha test {state.AlphaTestEnabled}/" +
                $"0x{state.AlphaFunc:X4}/{state.AlphaRef}";
        }
        if (!state.CullEnabled || state.CullFace != 0x0404)
        {
            yield return
                $"cull tuple {state.CullEnabled}/0x{state.CullFace:X4}";
        }
        if (state.PolygonMode != 0x1B02)
            yield return $"polygon mode 0x{state.PolygonMode:X4}";
        if (!state.BlendEnabled ||
            state.BlendEquationRgb != 0x8006 ||
            state.BlendEquationAlpha != 0x8006 ||
            state.BlendSourceRgb != 0x0302 ||
            state.BlendSourceAlpha != 0x0302 ||
            state.BlendDestinationRgb != 0x0303 ||
            state.BlendDestinationAlpha != 0x0303)
        {
            yield return "non-source-alpha blend tuple";
        }
        if (state.DepthTestEnabled || state.DepthWriteEnabled)
            yield return "depth test/write enabled";
        if (state.StencilEnabled)
            yield return "stencil enabled";
        if (state.PolygonOffsetEnabled)
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
                UiMaterialExecutionDiagnosticSeverity.Blocker,
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
                UiMaterialExecutionDiagnosticSeverity.Blocker,
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
            UiMaterialExecutionDiagnosticSeverity.Blocker,
            message));
        return new UiMaterialDrawPlan(null, diagnostics.ToArray());
    }
}
