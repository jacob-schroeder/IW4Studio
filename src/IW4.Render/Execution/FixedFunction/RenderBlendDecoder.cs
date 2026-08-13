using IW4.Render.Techniques;

namespace IW4.Render.Execution.FixedFunction;

/// <summary>
/// Decodes the supported PS3 blend state into backend-neutral fixed-function
/// rendering intent.
/// </summary>
internal static class RenderBlendDecoder
{
    internal static bool TryResolve(
        RenderState state,
        out RenderBlendStateDescriptor blend)
    {
        if (!state.BlendEnabled)
        {
            blend = RenderBlendStateDescriptor.Disabled;
            return true;
        }

        if (!TryResolveOperation(
                state.BlendEquationRgb,
                out RenderBlendOperation colorOperation) ||
            !TryResolveOperation(
                state.BlendEquationAlpha,
                out RenderBlendOperation alphaOperation) ||
            !TryResolveFactor(
                state.BlendSourceRgb,
                out RenderBlendFactor sourceColorFactor) ||
            !TryResolveFactor(
                state.BlendDestinationRgb,
                out RenderBlendFactor destinationColorFactor) ||
            !TryResolveFactor(
                state.BlendSourceAlpha,
                out RenderBlendFactor sourceAlphaFactor) ||
            !TryResolveFactor(
                state.BlendDestinationAlpha,
                out RenderBlendFactor destinationAlphaFactor))
        {
            blend = default;
            return false;
        }

        blend = new RenderBlendStateDescriptor(
            true,
            sourceColorFactor,
            destinationColorFactor,
            colorOperation,
            sourceAlphaFactor,
            destinationAlphaFactor,
            alphaOperation,
            System.Numerics.Vector4.Zero);
        return true;
    }

    private static bool TryResolveOperation(
        RsxBlendEquation value,
        out RenderBlendOperation operation)
    {
        switch (value)
        {
            case RsxBlendEquation.Add:
                operation = RenderBlendOperation.Add;
                return true;
            case RsxBlendEquation.Subtract:
                operation = RenderBlendOperation.Subtract;
                return true;
            case RsxBlendEquation.ReverseSubtract:
                operation = RenderBlendOperation.ReverseSubtract;
                return true;
            case RsxBlendEquation.Minimum:
                operation = RenderBlendOperation.Minimum;
                return true;
            case RsxBlendEquation.Maximum:
                operation = RenderBlendOperation.Maximum;
                return true;
            default:
                operation = default;
                return false;
        }
    }

    private static bool TryResolveFactor(
        RsxBlendFactor value,
        out RenderBlendFactor factor)
    {
        switch (value)
        {
            case RsxBlendFactor.Zero:
                factor = RenderBlendFactor.Zero;
                return true;
            case RsxBlendFactor.One:
                factor = RenderBlendFactor.One;
                return true;
            case RsxBlendFactor.SourceColor:
                factor = RenderBlendFactor.SourceColor;
                return true;
            case RsxBlendFactor.OneMinusSourceColor:
                factor = RenderBlendFactor.OneMinusSourceColor;
                return true;
            case RsxBlendFactor.SourceAlpha:
                factor = RenderBlendFactor.SourceAlpha;
                return true;
            case RsxBlendFactor.OneMinusSourceAlpha:
                factor = RenderBlendFactor.OneMinusSourceAlpha;
                return true;
            case RsxBlendFactor.DestinationAlpha:
                factor = RenderBlendFactor.DestinationAlpha;
                return true;
            case RsxBlendFactor.OneMinusDestinationAlpha:
                factor = RenderBlendFactor.OneMinusDestinationAlpha;
                return true;
            case RsxBlendFactor.DestinationColor:
                factor = RenderBlendFactor.DestinationColor;
                return true;
            case RsxBlendFactor.OneMinusDestinationColor:
                factor = RenderBlendFactor.OneMinusDestinationColor;
                return true;
            default:
                factor = default;
                return false;
        }
    }
}
