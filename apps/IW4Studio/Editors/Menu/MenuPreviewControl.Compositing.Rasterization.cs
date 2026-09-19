using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using IW4.Render.Materials;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.UI;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private bool DrawCpuCompositeRun(
        DrawingContext context,
        MenuPreviewScene scene,
        IReadOnlyList<MenuPreviewPrimitive> primitives,
        int start,
        int end,
        PreviewTransform transform)
    {
        Rect nativeStage = NativeStageBounds(scene.Settings);
        Rect displayStage = StageBounds(scene.Settings, transform);
        if (nativeStage.Width <= 0 || nativeStage.Height <= 0 ||
            displayStage.Width <= 0 || displayStage.Height <= 0)
            return false;

        // Blend and alpha-mask operations are defined in the game's native
        // 1280x720 target. Rasterize once there, then scale the finished
        // image to the editor stage so filtering cannot change the state
        // sequence's behavior.
        CpuCompositeSurface surface = EnsureCpuCompositeSurface(nativeStage);
        surface.Clear();
        for (int index = start; index < end; index++)
        {
            var material = (MenuPreviewMaterial)primitives[index];
            MenuPreviewMaterialSnapshot snapshot =
                _materialSnapshots[material.MaterialName];
            ReadOnlyMemory<byte> pixels =
                _materialPixels[material.MaterialName];
            Rect bounds = ToNativeRect(EffectivePlacement(
                material,
                scene.Settings).OutputBounds);
            if (!CompositeMaterial(
                    surface,
                    nativeStage,
                    bounds,
                    material,
                    snapshot,
                    pixels))
            {
                return false;
            }
        }

        surface.Upload();
        context.DrawImage(surface.Bitmap, displayStage);
        return true;
    }

    private static Rect NativeStageBounds(MenuPreviewSettings settings) =>
        new(0, 0, settings.CanvasWidth, settings.CanvasHeight);

    private static Rect ToNativeRect(MenuPreviewRect value)
    {
        double right = value.X + value.Width;
        double bottom = value.Y + value.Height;
        return new Rect(
            Math.Min(value.X, right),
            Math.Min(value.Y, bottom),
            Math.Abs(right - value.X),
            Math.Abs(bottom - value.Y));
    }

    private static bool CompositeMaterial(
        CpuCompositeSurface surface,
        Rect stage,
        Rect bounds,
        MenuPreviewMaterial material,
        MenuPreviewMaterialSnapshot snapshot,
        ReadOnlyMemory<byte> pixels)
    {
        if (snapshot.CpuPreviewCompositeState is not { } state ||
            snapshot.SamplerState is not { } sampler ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return false;
        }
        if (IsMaterialCulled(material, state.CullMode))
            return true;
        if ((state.ColorWriteMask &
                UiMaterialCpuPreviewColorWriteMask.RedGreenBlue) != 0 &&
            !UiMaterialCpuPreviewPlan.CanCompositeRgbOverOpaqueStage(
                state.Blend))
        {
            return false;
        }

        int firstX = Math.Clamp(
            (int)Math.Floor((bounds.Left - stage.Left) *
                surface.Width / stage.Width),
            0,
            surface.Width);
        int lastX = Math.Clamp(
            (int)Math.Ceiling((bounds.Right - stage.Left) *
                surface.Width / stage.Width),
            0,
            surface.Width);
        int firstY = Math.Clamp(
            (int)Math.Floor((bounds.Top - stage.Top) *
                surface.Height / stage.Height),
            0,
            surface.Height);
        int lastY = Math.Clamp(
            (int)Math.Ceiling((bounds.Bottom - stage.Top) *
                surface.Height / stage.Height),
            0,
            surface.Height);
        if (firstX >= lastX || firstY >= lastY)
            return true;

        bool useLinearFiltering = ResolveFilter(
            sampler,
            bounds,
            snapshot);
        float tintR = Channel(material.Tint.R) / 255f;
        float tintG = Channel(material.Tint.G) / 255f;
        float tintB = Channel(material.Tint.B) / 255f;
        float tintA = Channel(material.Tint.A) / 255f;
        for (int y = firstY; y < lastY; y++)
        {
            double screenY = stage.Top + ((y + 0.5) * stage.Height /
                surface.Height);
            float v = (float)((screenY - bounds.Top) / bounds.Height);
            if (material.FlipVertical)
                v = 1f - v;
            for (int x = firstX; x < lastX; x++)
            {
                double screenX = stage.Left + ((x + 0.5) * stage.Width /
                    surface.Width);
                float u = (float)((screenX - bounds.Left) / bounds.Width);
                if (material.FlipHorizontal)
                    u = 1f - u;

                SampleTexture(
                    pixels.Span,
                    snapshot.Width,
                    snapshot.Height,
                    sampler,
                    u,
                    v,
                    useLinearFiltering,
                    out float sourceR,
                    out float sourceG,
                    out float sourceB,
                    out float sourceA);
                sourceR *= tintR;
                sourceG *= tintG;
                sourceB *= tintB;
                sourceA *= tintA;
                if (!PassesAlphaTest(sourceA, state.AlphaTest))
                    continue;

                int offset = checked((y * surface.Width) + x);
                float destinationAlpha = surface.DestinationAlpha[offset];
                if ((state.ColorWriteMask &
                        UiMaterialCpuPreviewColorWriteMask.RedGreenBlue) != 0)
                {
                    CompositeRgb(
                        surface,
                        offset,
                        sourceR,
                        sourceG,
                        sourceB,
                        sourceA,
                        destinationAlpha,
                        state.Blend);
                }
                if ((state.ColorWriteMask &
                        UiMaterialCpuPreviewColorWriteMask.Alpha) != 0)
                {
                    surface.DestinationAlpha[offset] = BlendAlpha(
                        sourceA,
                        destinationAlpha,
                        state.Blend);
                }
            }
        }

        return true;
    }

    private static bool ResolveFilter(
        RsxSamplerState sampler,
        Rect bounds,
        MenuPreviewMaterialSnapshot snapshot)
    {
        bool magnifies = bounds.Width >= snapshot.Width &&
            bounds.Height >= snapshot.Height;
        return (magnifies ? sampler.MagFilter : sampler.MinFilter) ==
            TextureFilter.Linear;
    }

    private static bool PassesAlphaTest(
        float sourceAlpha,
        AlphaTestMode alphaTest) =>
        alphaTest switch
        {
            AlphaTestMode.Disabled => true,
            AlphaTestMode.GreaterZero => sourceAlpha > 0,
            AlphaTestMode.Less128 => sourceAlpha < 128f / 255f,
            AlphaTestMode.GreaterEqual128 =>
                sourceAlpha >= 128f / 255f,
            _ => false
        };

    private static void CompositeRgb(
        CpuCompositeSurface surface,
        int offset,
        float sourceR,
        float sourceG,
        float sourceB,
        float sourceAlpha,
        float destinationAlpha,
        UiMaterialCpuPreviewBlendState blend)
    {
        Debug.Assert(
            UiMaterialCpuPreviewPlan.CanCompositeRgbOverOpaqueStage(blend),
            "CPU RGB compositing requires an overlay-safe scalar ADD blend.");
        if (!blend.IsEnabled)
        {
            surface.PremultipliedRed[offset] = sourceR;
            surface.PremultipliedGreen[offset] = sourceG;
            surface.PremultipliedBlue[offset] = sourceB;
            surface.Coverage[offset] = 1f;
            return;
        }

        float sourceFactor = BlendFactor(
            blend.SourceRgbFactor,
            sourceAlpha,
            destinationAlpha);
        float destinationFactor = BlendFactor(
            blend.DestinationRgbFactor,
            sourceAlpha,
            destinationAlpha);
        surface.PremultipliedRed[offset] = ClampUnit(
            sourceR * sourceFactor +
            surface.PremultipliedRed[offset] * destinationFactor);
        surface.PremultipliedGreen[offset] = ClampUnit(
            sourceG * sourceFactor +
            surface.PremultipliedGreen[offset] * destinationFactor);
        surface.PremultipliedBlue[offset] = ClampUnit(
            sourceB * sourceFactor +
            surface.PremultipliedBlue[offset] * destinationFactor);
        surface.Coverage[offset] = ClampUnit(
            1f - ((1f - surface.Coverage[offset]) * destinationFactor));
    }

    private static float BlendAlpha(
        float sourceAlpha,
        float destinationAlpha,
        UiMaterialCpuPreviewBlendState blend)
    {
        if (!blend.IsEnabled)
            return sourceAlpha;

        return Blend(
            sourceAlpha,
            destinationAlpha,
            blend.AlphaEquation,
            BlendFactor(
                blend.SourceAlphaFactor,
                sourceAlpha,
                destinationAlpha),
            BlendFactor(
                blend.DestinationAlphaFactor,
                sourceAlpha,
                destinationAlpha));
    }

    private static float BlendFactor(
        UiMaterialCpuPreviewBlendFactor factor,
        float sourceAlpha,
        float destinationAlpha) =>
        factor switch
        {
            UiMaterialCpuPreviewBlendFactor.Zero => 0f,
            UiMaterialCpuPreviewBlendFactor.One => 1f,
            UiMaterialCpuPreviewBlendFactor.SourceColor or
            UiMaterialCpuPreviewBlendFactor.SourceAlpha => sourceAlpha,
            UiMaterialCpuPreviewBlendFactor.OneMinusSourceColor or
            UiMaterialCpuPreviewBlendFactor.OneMinusSourceAlpha =>
                1f - sourceAlpha,
            UiMaterialCpuPreviewBlendFactor.DestinationColor or
            UiMaterialCpuPreviewBlendFactor.DestinationAlpha =>
                destinationAlpha,
            UiMaterialCpuPreviewBlendFactor.OneMinusDestinationColor or
            UiMaterialCpuPreviewBlendFactor.OneMinusDestinationAlpha =>
                1f - destinationAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(factor))
        };

    private static float Blend(
        float source,
        float destination,
        UiMaterialCpuPreviewBlendEquation equation,
        float sourceFactor,
        float destinationFactor) =>
        equation switch
        {
            UiMaterialCpuPreviewBlendEquation.Add => ClampUnit(
                source * sourceFactor + destination * destinationFactor),
            UiMaterialCpuPreviewBlendEquation.Subtract => ClampUnit(
                source * sourceFactor - destination * destinationFactor),
            UiMaterialCpuPreviewBlendEquation.ReverseSubtract => ClampUnit(
                destination * destinationFactor - source * sourceFactor),
            UiMaterialCpuPreviewBlendEquation.Minimum => Math.Min(
                source,
                destination),
            UiMaterialCpuPreviewBlendEquation.Maximum => Math.Max(
                source,
                destination),
            _ => throw new ArgumentOutOfRangeException(nameof(equation))
        };

    private static float ClampUnit(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    private static void SampleTexture(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        RsxSamplerState sampler,
        float u,
        float v,
        bool linear,
        out float red,
        out float green,
        out float blue,
        out float alpha)
    {
        float textureX = u * width - 0.5f;
        float textureY = v * height - 0.5f;
        if (!linear)
        {
            ReadTexel(
                pixels,
                width,
                height,
                (int)MathF.Floor(textureX + 0.5f),
                (int)MathF.Floor(textureY + 0.5f),
                sampler,
                out red,
                out green,
                out blue,
                out alpha);
            return;
        }

        int x0 = (int)MathF.Floor(textureX);
        int y0 = (int)MathF.Floor(textureY);
        float ratioX = textureX - x0;
        float ratioY = textureY - y0;
        ReadTexel(
            pixels,
            width,
            height,
            x0,
            y0,
            sampler,
            out float topLeftR,
            out float topLeftG,
            out float topLeftB,
            out float topLeftA);
        ReadTexel(
            pixels,
            width,
            height,
            x0 + 1,
            y0,
            sampler,
            out float topRightR,
            out float topRightG,
            out float topRightB,
            out float topRightA);
        ReadTexel(
            pixels,
            width,
            height,
            x0,
            y0 + 1,
            sampler,
            out float bottomLeftR,
            out float bottomLeftG,
            out float bottomLeftB,
            out float bottomLeftA);
        ReadTexel(
            pixels,
            width,
            height,
            x0 + 1,
            y0 + 1,
            sampler,
            out float bottomRightR,
            out float bottomRightG,
            out float bottomRightB,
            out float bottomRightA);
        red = Interpolate(
            topLeftR,
            topRightR,
            bottomLeftR,
            bottomRightR,
            ratioX,
            ratioY);
        green = Interpolate(
            topLeftG,
            topRightG,
            bottomLeftG,
            bottomRightG,
            ratioX,
            ratioY);
        blue = Interpolate(
            topLeftB,
            topRightB,
            bottomLeftB,
            bottomRightB,
            ratioX,
            ratioY);
        alpha = Interpolate(
            topLeftA,
            topRightA,
            bottomLeftA,
            bottomRightA,
            ratioX,
            ratioY);
    }

    private static float Interpolate(
        float topLeft,
        float topRight,
        float bottomLeft,
        float bottomRight,
        float ratioX,
        float ratioY) =>
        (topLeft + ((topRight - topLeft) * ratioX)) +
        ((bottomLeft + ((bottomRight - bottomLeft) * ratioX) -
          (topLeft + ((topRight - topLeft) * ratioX))) * ratioY);

    private static void ReadTexel(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int x,
        int y,
        RsxSamplerState sampler,
        out float red,
        out float green,
        out float blue,
        out float alpha)
    {
        int column = ResolveTexelCoordinate(x, width, sampler.AddressU);
        int row = ResolveTexelCoordinate(y, height, sampler.AddressV);
        int offset = checked(((row * width) + column) * 4);
        red = pixels[offset] / 255f;
        green = pixels[offset + 1] / 255f;
        blue = pixels[offset + 2] / 255f;
        alpha = pixels[offset + 3] / 255f;
    }

    private static int ResolveTexelCoordinate(
        int coordinate,
        int length,
        TextureAddressMode addressMode)
    {
        if (addressMode == TextureAddressMode.Clamp)
            return Math.Clamp(coordinate, 0, length - 1);

        int wrapped = coordinate % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }
}
