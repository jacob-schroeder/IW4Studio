using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Materials;

/// <summary>
/// Decodes the supported PS3 blend state into backend-neutral fixed-function
/// rendering intent.
/// </summary>
internal static class MapRenderBlend
{
    internal static bool TryResolve(
        MapRenderState state,
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
        uint value,
        out RenderBlendOperation operation)
    {
        switch (value)
        {
            case 0x8006:
                operation = RenderBlendOperation.Add;
                return true;
            case 0x800A:
                operation = RenderBlendOperation.Subtract;
                return true;
            case 0x800B:
                operation = RenderBlendOperation.ReverseSubtract;
                return true;
            case 0x8007:
                operation = RenderBlendOperation.Minimum;
                return true;
            case 0x8008:
                operation = RenderBlendOperation.Maximum;
                return true;
            default:
                operation = default;
                return false;
        }
    }

    private static bool TryResolveFactor(
        uint value,
        out RenderBlendFactor factor)
    {
        switch (value)
        {
            case 0:
                factor = RenderBlendFactor.Zero;
                return true;
            case 1:
                factor = RenderBlendFactor.One;
                return true;
            case 0x0300:
                factor = RenderBlendFactor.SourceColor;
                return true;
            case 0x0301:
                factor = RenderBlendFactor.OneMinusSourceColor;
                return true;
            case 0x0302:
                factor = RenderBlendFactor.SourceAlpha;
                return true;
            case 0x0303:
                factor = RenderBlendFactor.OneMinusSourceAlpha;
                return true;
            case 0x0304:
                factor = RenderBlendFactor.DestinationAlpha;
                return true;
            case 0x0305:
                factor = RenderBlendFactor.OneMinusDestinationAlpha;
                return true;
            case 0x0306:
                factor = RenderBlendFactor.DestinationColor;
                return true;
            case 0x0307:
                factor = RenderBlendFactor.OneMinusDestinationColor;
                return true;
            default:
                factor = default;
                return false;
        }
    }
}
