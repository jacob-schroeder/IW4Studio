using IW4.Render.Materials;

namespace IW4.Render.UI;

/// <summary>
/// The fixed-function portion of a 2D material draw that the Menu preview
/// CPU compositor can reproduce without relying on an Avalonia backend's
/// blend implementation. This is deliberately separate from
/// <see cref="UiMaterialDrawPacket"/>, which remains the stricter generic
/// renderer execution contract.
/// </summary>
[Flags]
public enum UiMaterialCpuPreviewColorWriteMask
{
    None = 0,
    RedGreenBlue = 1,
    Alpha = 2,
    RedGreenBlueAlpha = RedGreenBlue | Alpha
}

public enum UiMaterialCpuPreviewBlendEquation
{
    Add = 0,
    Subtract = 1,
    ReverseSubtract = 2,
    Minimum = 3,
    Maximum = 4
}

public enum UiMaterialCpuPreviewBlendFactor
{
    Zero = 0,
    One = 1,
    SourceColor = 2,
    OneMinusSourceColor = 3,
    SourceAlpha = 4,
    OneMinusSourceAlpha = 5,
    DestinationAlpha = 6,
    OneMinusDestinationAlpha = 7,
    DestinationColor = 8,
    OneMinusDestinationColor = 9
}

public readonly record struct UiMaterialCpuPreviewBlendState(
    bool IsEnabled,
    UiMaterialCpuPreviewBlendEquation RgbEquation,
    UiMaterialCpuPreviewBlendEquation AlphaEquation,
    UiMaterialCpuPreviewBlendFactor SourceRgbFactor,
    UiMaterialCpuPreviewBlendFactor DestinationRgbFactor,
    UiMaterialCpuPreviewBlendFactor SourceAlphaFactor,
    UiMaterialCpuPreviewBlendFactor DestinationAlphaFactor);

/// <summary>
/// Decoded primary-color writes and blend state for the CPU Menu-preview
/// compositor. Its source colour is the proven trivial_vertcol_simple2d
/// texture-times-vertex-colour output.
/// </summary>
public sealed record UiMaterialCpuPreviewCompositeState(
    UiMaterialCpuPreviewColorWriteMask ColorWriteMask,
    MapRenderAlphaTestMode AlphaTest,
    MapRenderCullMode CullMode,
    UiMaterialCpuPreviewBlendState Blend);

public sealed class UiMaterialCpuPreviewPlan
{
    private readonly UiMaterialExecutionDiagnostic[] _diagnostics;

    private UiMaterialCpuPreviewPlan(
        UiMaterialCpuPreviewCompositeState? compositeState,
        IEnumerable<UiMaterialExecutionDiagnostic> diagnostics)
    {
        CompositeState = compositeState;
        _diagnostics = diagnostics.ToArray();
        if (_diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A CPU preview plan cannot contain null diagnostics.",
                nameof(diagnostics));
        }

        bool blocked = _diagnostics.Any(diagnostic =>
            diagnostic.Severity ==
            UiMaterialExecutionDiagnosticSeverity.Blocker);
        if ((compositeState is null) != blocked)
        {
            throw new ArgumentException(
                "A CPU preview state is available exactly when its plan " +
                "contains no blockers.",
                nameof(diagnostics));
        }

        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    public UiMaterialCpuPreviewCompositeState? CompositeState { get; }

    public IReadOnlyList<UiMaterialExecutionDiagnostic> Diagnostics { get; }

    public bool IsExecutable => CompositeState is not null;

    public static UiMaterialCpuPreviewPlan Blocked(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new UiMaterialCpuPreviewPlan(
            null,
            [new UiMaterialExecutionDiagnostic(
                UiMaterialExecutionDiagnosticCode
                    .UnsupportedCpuPreviewCompositeState,
                UiMaterialExecutionDiagnosticSeverity.Blocker,
                message)]);
    }

    public static UiMaterialCpuPreviewPlan Plan(MapRenderState state)
    {
        var diagnostics = new List<UiMaterialExecutionDiagnostic>();
        if (!state.HasState)
            Block(diagnostics, "The selected material pass has no decoded PS3 state.");
        if (state.ShaderPackerSrgbEnabled)
        {
            Block(
                diagnostics,
                "The selected material enables PS3 shader-packer sRGB output.");
        }
        if (state.StencilEnabled)
        {
            Block(
                diagnostics,
                "The selected material enables stencil testing or writes; " +
                "the Menu preview does not emulate stencil behavior.");
        }
        if (state.DepthTestEnabled || state.DepthWriteEnabled)
        {
            Block(
                diagnostics,
                "The selected material owns depth state, which a 2D Menu " +
                "preview cannot reproduce.");
        }
        if (state.PolygonOffsetEnabled)
        {
            Block(
                diagnostics,
                "The selected material enables polygon offset, which is not " +
                "meaningful for the Menu CPU compositor.");
        }
        MapRenderCullMode? cullMode = MapRenderCull.Resolve(state);
        if (cullMode is null)
        {
            Block(
                diagnostics,
                "The selected material has an unsupported cull tuple " +
                $"{state.CullEnabled}/0x{state.CullFace:X4}.");
        }
        if (state.PolygonMode != 0x1B02)
        {
            Block(
                diagnostics,
                "The selected material does not use filled polygons.");
        }

        UiMaterialCpuPreviewColorWriteMask? colorWriteMask =
            DecodeColorWriteMask(state.ColorMask);
        if (colorWriteMask is null)
        {
            Block(
                diagnostics,
                $"Primary color mask 0x{state.ColorMask:X8} is not one of " +
                "the decoded PS3 RGB-only, alpha-only, or RGBA write masks.");
        }

        MapRenderAlphaTestMode? alphaTest = MapRenderAlphaTest.Resolve(state);
        if (alphaTest is null)
        {
            Block(
                diagnostics,
                "The selected material has an unsupported alpha-test tuple " +
                $"0x{state.AlphaFunc:X4}/0x{state.AlphaRef:X2}.");
        }

        UiMaterialCpuPreviewBlendState? blend = DecodeBlend(state);
        if (blend is null)
        {
            Block(
                diagnostics,
                "The selected material has an unsupported PS3 blend tuple " +
                $"(rgb 0x{state.BlendEquationRgb:X4}/" +
                $"0x{state.BlendSourceRgb:X4}/" +
                $"0x{state.BlendDestinationRgb:X4}, alpha " +
                $"0x{state.BlendEquationAlpha:X4}/" +
                $"0x{state.BlendSourceAlpha:X4}/" +
                $"0x{state.BlendDestinationAlpha:X4}).");
        }
        else if (colorWriteMask is { } writes &&
                 (writes & UiMaterialCpuPreviewColorWriteMask.RedGreenBlue) != 0 &&
                 !CanCompositeRgbOverOpaqueStage(blend.Value))
        {
            Block(
                diagnostics,
                "The selected RGB blend tuple depends on the existing color " +
                "target in a way that cannot be represented by an Avalonia " +
                "overlay. The Menu preview will not substitute source-over " +
                "blending for it.");
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity ==
                UiMaterialExecutionDiagnosticSeverity.Blocker))
        {
            return new UiMaterialCpuPreviewPlan(null, diagnostics);
        }

        return new UiMaterialCpuPreviewPlan(
            new UiMaterialCpuPreviewCompositeState(
                colorWriteMask!.Value,
                alphaTest!.Value,
                cullMode!.Value,
                blend!.Value),
            diagnostics);
    }

    private static UiMaterialCpuPreviewColorWriteMask? DecodeColorWriteMask(
        uint value) =>
        value switch
        {
            0x00010101 => UiMaterialCpuPreviewColorWriteMask.RedGreenBlue,
            0x01000000 => UiMaterialCpuPreviewColorWriteMask.Alpha,
            0x01010101 =>
                UiMaterialCpuPreviewColorWriteMask.RedGreenBlueAlpha,
            _ => null
        };

    private static UiMaterialCpuPreviewBlendState? DecodeBlend(
        MapRenderState state)
    {
        if (!state.BlendEnabled)
        {
            return new UiMaterialCpuPreviewBlendState(
                false,
                UiMaterialCpuPreviewBlendEquation.Add,
                UiMaterialCpuPreviewBlendEquation.Add,
                UiMaterialCpuPreviewBlendFactor.One,
                UiMaterialCpuPreviewBlendFactor.Zero,
                UiMaterialCpuPreviewBlendFactor.One,
                UiMaterialCpuPreviewBlendFactor.Zero);
        }

        UiMaterialCpuPreviewBlendEquation? rgbEquation = DecodeEquation(
            state.BlendEquationRgb);
        UiMaterialCpuPreviewBlendEquation? alphaEquation = DecodeEquation(
            state.BlendEquationAlpha);
        UiMaterialCpuPreviewBlendFactor? sourceRgb = DecodeFactor(
            state.BlendSourceRgb);
        UiMaterialCpuPreviewBlendFactor? destinationRgb = DecodeFactor(
            state.BlendDestinationRgb);
        UiMaterialCpuPreviewBlendFactor? sourceAlpha = DecodeFactor(
            state.BlendSourceAlpha);
        UiMaterialCpuPreviewBlendFactor? destinationAlpha = DecodeFactor(
            state.BlendDestinationAlpha);
        if (rgbEquation is null || alphaEquation is null ||
            sourceRgb is null || destinationRgb is null ||
            sourceAlpha is null || destinationAlpha is null)
        {
            return null;
        }

        return new UiMaterialCpuPreviewBlendState(
            true,
            rgbEquation.Value,
            alphaEquation.Value,
            sourceRgb.Value,
            destinationRgb.Value,
            sourceAlpha.Value,
            destinationAlpha.Value);
    }

    private static UiMaterialCpuPreviewBlendEquation? DecodeEquation(
        uint value) =>
        value switch
        {
            0x8006 => UiMaterialCpuPreviewBlendEquation.Add,
            0x800A => UiMaterialCpuPreviewBlendEquation.Subtract,
            0x800B => UiMaterialCpuPreviewBlendEquation.ReverseSubtract,
            0x8007 => UiMaterialCpuPreviewBlendEquation.Minimum,
            0x8008 => UiMaterialCpuPreviewBlendEquation.Maximum,
            _ => null
        };

    private static UiMaterialCpuPreviewBlendFactor? DecodeFactor(uint value) =>
        value switch
        {
            0 => UiMaterialCpuPreviewBlendFactor.Zero,
            1 => UiMaterialCpuPreviewBlendFactor.One,
            0x0300 => UiMaterialCpuPreviewBlendFactor.SourceColor,
            0x0301 => UiMaterialCpuPreviewBlendFactor.OneMinusSourceColor,
            0x0302 => UiMaterialCpuPreviewBlendFactor.SourceAlpha,
            0x0303 => UiMaterialCpuPreviewBlendFactor.OneMinusSourceAlpha,
            0x0304 => UiMaterialCpuPreviewBlendFactor.DestinationAlpha,
            0x0305 =>
                UiMaterialCpuPreviewBlendFactor.OneMinusDestinationAlpha,
            0x0306 => UiMaterialCpuPreviewBlendFactor.DestinationColor,
            0x0307 =>
                UiMaterialCpuPreviewBlendFactor.OneMinusDestinationColor,
            _ => null
        };

    /// <summary>
    /// Returns whether an RGB blend can be represented by the CPU overlay on
    /// an opaque Menu target. Its factor pair must preserve a valid overlay
    /// coverage value; arbitrary scalar additive pairs can exceed it.
    /// </summary>
    public static bool CanCompositeRgbOverOpaqueStage(
        UiMaterialCpuPreviewBlendState blend)
    {
        if (!blend.IsEnabled)
            return true;
        if (blend.RgbEquation != UiMaterialCpuPreviewBlendEquation.Add)
            return false;

        // RGB factors must stay scalar. The compositor keeps an Avalonia
        // overlay rather than a native color target, so SRC_COLOR and
        // DST_COLOR cannot be collapsed to an alpha value. Further, the
        // source/destination pair must either replace/attenuate one side or
        // be complementary; otherwise its result cannot be encoded as a
        // source-over overlay with coverage in [0, 1].
        if (blend.SourceRgbFactor == UiMaterialCpuPreviewBlendFactor.Zero)
            return IsScalarBlendFactor(blend.DestinationRgbFactor);
        if (blend.DestinationRgbFactor == UiMaterialCpuPreviewBlendFactor.Zero)
            return IsScalarBlendFactor(blend.SourceRgbFactor);

        return (blend.SourceRgbFactor, blend.DestinationRgbFactor) switch
        {
            (UiMaterialCpuPreviewBlendFactor.SourceAlpha,
                UiMaterialCpuPreviewBlendFactor.OneMinusSourceAlpha) => true,
            (UiMaterialCpuPreviewBlendFactor.OneMinusSourceAlpha,
                UiMaterialCpuPreviewBlendFactor.SourceAlpha) => true,
            (UiMaterialCpuPreviewBlendFactor.DestinationAlpha,
                UiMaterialCpuPreviewBlendFactor.OneMinusDestinationAlpha) => true,
            (UiMaterialCpuPreviewBlendFactor.OneMinusDestinationAlpha,
                UiMaterialCpuPreviewBlendFactor.DestinationAlpha) => true,
            _ => false
        };
    }

    private static bool IsScalarBlendFactor(
        UiMaterialCpuPreviewBlendFactor factor) =>
        factor is UiMaterialCpuPreviewBlendFactor.Zero or
                  UiMaterialCpuPreviewBlendFactor.One or
                  UiMaterialCpuPreviewBlendFactor.SourceAlpha or
                  UiMaterialCpuPreviewBlendFactor.OneMinusSourceAlpha or
                  UiMaterialCpuPreviewBlendFactor.DestinationAlpha or
                  UiMaterialCpuPreviewBlendFactor.OneMinusDestinationAlpha;

    private static void Block(
        ICollection<UiMaterialExecutionDiagnostic> diagnostics,
        string message) =>
        diagnostics.Add(new UiMaterialExecutionDiagnostic(
            UiMaterialExecutionDiagnosticCode
                .UnsupportedCpuPreviewCompositeState,
            UiMaterialExecutionDiagnosticSeverity.Blocker,
            message));
}
