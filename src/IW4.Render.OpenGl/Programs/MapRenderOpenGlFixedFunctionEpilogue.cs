using IW4.Render.Materials;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Composes the retained EditorPreview alpha-test and RSX shader-packer
/// lowering at the translated fragment-program insertion point.
/// </summary>
internal static class MapRenderOpenGlFixedFunctionEpilogue
{
    internal static bool TryCompose(
        MapRenderState state,
        uint fragmentProgramControl,
        bool suppressShaderPackerForDiagnosticOutput,
        out MapRenderAlphaTestMode alphaTestMode,
        out MapRenderOpenGlRsxShaderPackerMode shaderPackerMode,
        out string epilogue)
    {
        if (!TryAlphaTestMode(
                state,
                out alphaTestMode,
                out string alphaTestEpilogue))
        {
            shaderPackerMode = default;
            epilogue = string.Empty;
            return false;
        }

        shaderPackerMode = ResolveShaderPackerMode(
            fragmentProgramControl,
            state,
            suppressShaderPackerForDiagnosticOutput);
        if (shaderPackerMode ==
                MapRenderOpenGlRsxShaderPackerMode
                    .LinearToSrgbProgramEpilogue &&
            RequiresPremultipliedSourceRgb(state))
        {
            shaderPackerMode = MapRenderOpenGlRsxShaderPackerMode
                .PremultipliedLinearToSrgbProgramEpilogue;
        }
        string shaderPackerEpilogue = shaderPackerMode switch
        {
            MapRenderOpenGlRsxShaderPackerMode
                .LinearToSrgbProgramEpilogue =>
                CreateShaderPackerEpilogue(),
            MapRenderOpenGlRsxShaderPackerMode
                .PremultipliedLinearToSrgbProgramEpilogue =>
                CreatePremultipliedShaderPackerEpilogue(),
            _ => string.Empty
        };
        epilogue = string.Concat(
            alphaTestEpilogue,
            alphaTestEpilogue.Length != 0 && shaderPackerEpilogue.Length != 0
                ? Environment.NewLine
                : string.Empty,
            shaderPackerEpilogue);
        return true;
    }

    internal static MapRenderOpenGlAuthoredFragmentSource Apply(
        MapRenderOpenGlAuthoredFragmentSource fragmentSource,
        string epilogue)
    {
        ArgumentNullException.ThrowIfNull(fragmentSource);
        string fragmentGlsl = fragmentSource.ExactGlsl;
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentGlsl);
        string marker =
            RsxFragmentGlsl330Lowerer.FragmentEpilogueInsertionPoint;
        int first = fragmentGlsl.IndexOf(marker, StringComparison.Ordinal);
        if (first < 0 ||
            fragmentGlsl.IndexOf(
                marker,
                first + marker.Length,
                StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(
                "Translated fragment GLSL must contain exactly one fixed-function epilogue insertion point.");
        }

        return fragmentSource.WithBackendComposition(
            fragmentGlsl
                .Remove(first, marker.Length)
                .Insert(first, epilogue));
    }

    private static bool TryAlphaTestMode(
        MapRenderState state,
        out MapRenderAlphaTestMode mode,
        out string epilogue)
    {
        MapRenderAlphaTestMode? resolved = MapRenderAlphaTest.Resolve(state);
        if (!resolved.HasValue)
        {
            mode = default;
            epilogue = string.Empty;
            return false;
        }

        mode = resolved.Value;
        switch (mode)
        {
            case MapRenderAlphaTestMode.Disabled:
                epilogue = string.Empty;
                return true;
            case MapRenderAlphaTestMode.GreaterZero:
                epilogue = "  if (!(FragColor.a > 0.0)) discard;";
                return true;
            case MapRenderAlphaTestMode.Less128:
                epilogue =
                    "  if (!(FragColor.a < (128.0 / 255.0))) discard;";
                return true;
            case MapRenderAlphaTestMode.GreaterEqual128:
                epilogue =
                    "  if (!(FragColor.a >= (128.0 / 255.0))) discard;";
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    internal static MapRenderOpenGlRsxShaderPackerMode ResolveShaderPackerMode(
        uint fragmentProgramControl,
        MapRenderState state,
        bool suppressForDiagnosticOutput)
    {
        if (!state.ShaderPackerSrgbEnabled)
            return MapRenderOpenGlRsxShaderPackerMode.DisabledByState;

        // Integer render targets honor NV4097_SET_SHADER_PACKER only when the
        // fragment program does not use
        // CELL_GCM_SHADER_CONTROL_32_BITS_EXPORTS (our preserved control 0x40).
        if ((fragmentProgramControl & 0x40u) != 0)
            return MapRenderOpenGlRsxShaderPackerMode.SuppressedForFp32Exports;
        if (suppressForDiagnosticOutput)
        {
            return MapRenderOpenGlRsxShaderPackerMode
                .SuppressedForDiagnosticOutput;
        }
        return MapRenderOpenGlRsxShaderPackerMode
            .LinearToSrgbProgramEpilogue;
    }

    internal static bool ConsumesLinearFogColor(
        MapRenderOpenGlRsxShaderPackerMode mode) =>
        mode is MapRenderOpenGlRsxShaderPackerMode
                    .LinearToSrgbProgramEpilogue or
                MapRenderOpenGlRsxShaderPackerMode
                    .PremultipliedLinearToSrgbProgramEpilogue;

    /// <summary>
    /// The retained ADD + ONE / ONE_MINUS_SRC_ALPHA RGB tuple consumes a
    /// premultiplied source. Translated authored shaders produce that source
    /// themselves; the generic material shader must reproduce the same final
    /// export contract after its straight-color lighting and fog work.
    /// </summary>
    internal static bool RequiresPremultipliedSourceRgb(
        MapRenderState state) =>
        state.BlendEnabled &&
        state.BlendEquationRgb == 0x8006u &&
        state.BlendSourceRgb == 1u &&
        state.BlendDestinationRgb == 0x0303u;

    private static string CreateShaderPackerEpilogue()
    {
        string[] outputNames =
            ["FragColor", "rsxMrtColor1", "rsxMrtColor2", "rsxMrtColor3"];
        var builder = new System.Text.StringBuilder();
        for (int colorTarget = 0;
             colorTarget < outputNames.Length;
             colorTarget++)
        {
            string name = outputNames[colorTarget];
            string suffix = colorTarget.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            builder.Append("  vec3 rsxPackerLow").Append(suffix)
                .Append(" = ").Append(name).AppendLine(".rgb * 12.92;");
            builder.Append("  vec3 rsxPackerHigh").Append(suffix)
                .Append(" = 1.055 * pow(").Append(name)
                .AppendLine(".rgb, vec3(1.0 / 2.4)) - 0.055;");
            builder.Append("  bvec3 rsxPackerSelectLow").Append(suffix)
                .Append(" = lessThan(").Append(name)
                .AppendLine(".rgb, vec3(0.0031308));");
            builder.Append("  ").Append(name)
                .Append(".rgb = clamp(mix(rsxPackerHigh").Append(suffix)
                .Append(", rsxPackerLow").Append(suffix)
                .Append(", rsxPackerSelectLow").Append(suffix)
                .AppendLine("), vec3(0.0), vec3(1.0));");
        }
        return builder.ToString().TrimEnd();
    }

    private static string CreatePremultipliedShaderPackerEpilogue()
    {
        // OpenGL compositing adaptation: the authored ADD + ONE /
        // ONE_MINUS_SRC_ALPHA state requires premultiplied source RGB. The
        // exact physical RSX shader-packer ordering relative to blending is
        // still OPEN. With the current linear GL_RGBA8 target, encoding the
        // already-premultiplied value directly would break RGB <= alpha and
        // inflate fractional-alpha edges. Encode straight RGB, then restore
        // the authored premultiplication before OpenGL blending.
        string[] outputNames =
            ["FragColor", "rsxMrtColor1", "rsxMrtColor2", "rsxMrtColor3"];
        var builder = new System.Text.StringBuilder();
        for (int colorTarget = 0;
             colorTarget < outputNames.Length;
             colorTarget++)
        {
            string name = outputNames[colorTarget];
            string suffix = colorTarget.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            builder.Append("  float rsxPackerAlpha").Append(suffix)
                .Append(" = ").Append(name).AppendLine(".a;");
            builder.Append("  if (rsxPackerAlpha").Append(suffix)
                .AppendLine(" > 0.0)");
            builder.AppendLine("  {");
            builder.Append("    vec3 rsxPackerStraight").Append(suffix)
                .Append(" = max(").Append(name)
                .Append(".rgb / rsxPackerAlpha").Append(suffix)
                .AppendLine(", vec3(0.0));");
            builder.Append("    vec3 rsxPackerLow").Append(suffix)
                .Append(" = rsxPackerStraight").Append(suffix)
                .AppendLine(" * 12.92;");
            builder.Append("    vec3 rsxPackerHigh").Append(suffix)
                .Append(" = 1.055 * pow(rsxPackerStraight")
                .Append(suffix)
                .AppendLine(", vec3(1.0 / 2.4)) - 0.055;");
            builder.Append("    bvec3 rsxPackerSelectLow").Append(suffix)
                .Append(" = lessThan(rsxPackerStraight").Append(suffix)
                .AppendLine(", vec3(0.0031308));");
            builder.Append("    ").Append(name)
                .Append(".rgb = clamp(mix(rsxPackerHigh").Append(suffix)
                .Append(", rsxPackerLow").Append(suffix)
                .Append(", rsxPackerSelectLow").Append(suffix)
                .Append("), vec3(0.0), vec3(1.0)) * rsxPackerAlpha")
                .Append(suffix).AppendLine(";");
            builder.AppendLine("  }");
            builder.AppendLine("  else");
            builder.AppendLine("  {");
            builder.Append("    ").Append(name)
                .AppendLine(".rgb = vec3(0.0);");
            builder.AppendLine("  }");
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Scalar reference for the generated premultiplied shader-packer
    /// contract. Alpha is intentionally returned to the caller unchanged.
    /// </summary>
    internal static float EncodePremultipliedLinearChannel(
        float premultipliedLinear,
        float alpha)
    {
        if (!(alpha > 0.0f))
            return 0.0f;

        float straightLinear = MathF.Max(
            premultipliedLinear / alpha,
            0.0f);
        float encoded = straightLinear < 0.0031308f
            ? straightLinear * 12.92f
            : 1.055f * MathF.Pow(straightLinear, 1.0f / 2.4f) - 0.055f;
        return Math.Clamp(encoded, 0.0f, 1.0f) * alpha;
    }
}
