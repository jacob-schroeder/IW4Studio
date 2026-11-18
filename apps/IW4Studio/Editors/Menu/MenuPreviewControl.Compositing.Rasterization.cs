using Avalonia;
using Avalonia.Controls;
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
        {
            ReleaseCpuCompositeSurface();
            return false;
        }

        // Preserve native-space placement and sampler selection, while
        // avoiding a full game-resolution scratch image in smaller views.
        double displayScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = RasterSize(nativeStage.Width, displayStage.Width * displayScale);
        int height = RasterSize(nativeStage.Height, displayStage.Height * displayScale);
        var draws = new List<CpuCompositeDraw>(end - start);
        for (int index = start; index < end; index++)
        {
            var material = (MenuPreviewMaterial)primitives[index];
            Rect bounds = CompositeBounds(material, scene.Settings);
            if (!HasZeroArea(bounds))
                draws.Add(new CpuCompositeDraw(bounds, material,
                    _materialSnapshots[material.MaterialName], _materialPixels[material.MaterialName]));
        }

        CpuCompositeRequest request = RequestCpuComposite(
            new CpuCompositeRequest(nativeStage, width, height, draws.ToArray()));
        if (_displayedCpuComposite is null && request.HasSameImage(_completedCpuComposite))
            return false;

        for (int index = 0; index < start; index++)
            DrawPrimitive(context, scene, primitives[index], transform);
        if (_cpuCompositeBitmap is { } bitmap && request.HasSameTarget(_displayedCpuComposite))
            context.DrawImage(bitmap, displayStage);
        return true;
    }

    private static bool RasterizeCpuComposite(
        CpuCompositeRaster raster,
        CpuCompositeRequest request,
        CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
            return false;

        raster.Clear();
        foreach (CpuCompositeDraw draw in request.Draws)
        {
            if (!CompositeMaterial(raster, request.NativeStage, draw.Bounds, draw.Material,
                    draw.Snapshot, draw.Pixels, cancellation))
            {
                return false;
            }
        }

        return raster.ConvertToRgba(cancellation);
    }

    private static Rect NativeStageBounds(MenuPreviewSettings settings) =>
        new(0, 0, settings.CanvasWidth, settings.CanvasHeight);

    private static int RasterSize(double nativeSize, double displaySize) =>
        Math.Max(1, (int)Math.Ceiling(Math.Min(nativeSize, displaySize)));

    private Rect CompositeBounds(MenuPreviewMaterial material, MenuPreviewSettings settings) =>
        ToNativeRect(EffectivePlacement(material, settings).OutputBounds);

    private static bool HasZeroArea(Rect bounds) =>
        (bounds.Width == 0 || bounds.Height == 0) &&
        double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height);

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
        CpuCompositeRaster surface,
        Rect stage,
        Rect bounds,
        MenuPreviewMaterial material,
        MenuPreviewMaterialSnapshot snapshot,
        ReadOnlyMemory<byte> pixels,
        CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested ||
            snapshot.CpuPreviewCompositeState is not { } state ||
            snapshot.SamplerState is not { } sampler ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return false;
        }
        if (IsMaterialCulled(state.CullMode))
            return true;
        bool writesRgb = (state.ColorWriteMask & UiMaterialCpuPreviewColorWriteMask.RedGreenBlue) != 0;
        bool writesAlpha = (state.ColorWriteMask & UiMaterialCpuPreviewColorWriteMask.Alpha) != 0;
        if (writesRgb &&
            !UiMaterialCpuPreviewPlan.CanCompositeRgbOverOpaqueStage(
                state.Blend))
        {
            return false;
        }

        Rect visible = VisibleMaterialBounds(material, bounds);
        int firstX = Math.Clamp(
            (int)Math.Floor((visible.Left - stage.Left) *
                surface.Width / stage.Width),
            0,
            surface.Width);
        int lastX = Math.Clamp(
            (int)Math.Ceiling((visible.Right - stage.Left) *
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

        if (ClearsAlpha(state))
        {
            for (int y = firstY; y < lastY; y++)
            {
                if (cancellation.IsCancellationRequested)
                    return false;
                Array.Clear(surface.DestinationAlpha, y * surface.Width + firstX, lastX - firstX);
            }
            return true;
        }

        bool useLinearFiltering = ResolveFilter(sampler, bounds.Width, bounds.Height, snapshot) == TextureFilter.Linear;
        int mip = snapshot.SelectMipLevel(bounds.Width, bounds.Height);
        int textureWidth = Math.Max(1, snapshot.Width >> mip);
        int textureHeight = Math.Max(1, snapshot.Height >> mip);
        if (mip > 0)
            pixels = snapshot.MipPixels(mip);
        int textureStride = checked(textureWidth * 4);
        PrepareTextureAxis(surface.Columns, firstX, lastX,
            stage.Left, stage.Width, bounds.Left, bounds.Width, material.FlipHorizontal,
            textureWidth, 4, sampler.AddressU, useLinearFiltering);
        PrepareTextureAxis(surface.Rows, firstY, lastY,
            stage.Top, stage.Height, bounds.Top, bounds.Height, material.FlipVertical,
            textureHeight, textureStride, sampler.AddressV, useLinearFiltering);

        float tintR = Channel(material.Tint.R) / 255f;
        float tintG = Channel(material.Tint.G) / 255f;
        float tintB = Channel(material.Tint.B) / 255f;
        float tintA = Channel(material.Tint.A) / 255f;
        ReadOnlySpan<byte> texels = pixels.Span;
        UiMaterialCpuPreviewBlendState blend = state.Blend;
        for (int y = firstY; y < lastY; y++)
        {
            if (cancellation.IsCancellationRequested)
                return false;

            CpuTextureAxisSample row = surface.Rows[y];
            ReadOnlySpan<byte> top = texels.Slice(row.FirstOffset, textureStride);
            ReadOnlySpan<byte> bottom = texels.Slice(row.SecondOffset, textureStride);
            int rowOffset = y * surface.Width;
            for (int x = firstX; x < lastX; x++)
            {
                CpuTextureAxisSample column = surface.Columns[x];
                float sourceA = SampleChannel(top, bottom, column, row.Ratio, 3, useLinearFiltering) * tintA;
                if (!PassesAlphaTest(sourceA, state.AlphaTest))
                    continue;

                int offset = rowOffset + x;
                float destinationAlpha = surface.DestinationAlpha[offset];
                if (writesRgb)
                {
                    CompositeRgb(
                        surface,
                        offset,
                        SampleChannel(top, bottom, column, row.Ratio, 0, useLinearFiltering) * tintR,
                        SampleChannel(top, bottom, column, row.Ratio, 1, useLinearFiltering) * tintG,
                        SampleChannel(top, bottom, column, row.Ratio, 2, useLinearFiltering) * tintB,
                        sourceA,
                        destinationAlpha,
                        blend);
                }
                if (writesAlpha)
                {
                    surface.DestinationAlpha[offset] = BlendAlpha(
                        sourceA,
                        destinationAlpha,
                        blend);
                }
            }
        }

        return true;
    }

    private static TextureFilter ResolveFilter(
        RsxSamplerState sampler,
        double drawWidth,
        double drawHeight,
        MenuPreviewMaterialSnapshot snapshot)
    {
        return drawWidth >= snapshot.Width && drawHeight >= snapshot.Height
            ? sampler.MagFilter : sampler.MinFilter;
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
        CpuCompositeRaster surface,
        int offset,
        float sourceR,
        float sourceG,
        float sourceB,
        float sourceAlpha,
        float destinationAlpha,
        UiMaterialCpuPreviewBlendState blend)
    {
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

    private static void PrepareTextureAxis(
        CpuTextureAxisSample[] samples,
        int first,
        int last,
        double stageStart,
        double stageExtent,
        double boundsStart,
        double boundsExtent,
        bool flipped,
        int texelCount,
        int texelStride,
        TextureAddressMode addressMode,
        bool linear)
    {
        for (int index = first; index < last; index++)
        {
            double screen = stageStart + ((index + 0.5) * stageExtent / samples.Length);
            float uv = (float)((screen - boundsStart) / boundsExtent);
            if (flipped)
                uv = 1f - uv;

            float texel = uv * texelCount - 0.5f;
            int firstTexel = (int)MathF.Floor(linear ? texel : texel + 0.5f);
            int firstOffset = ResolveTexelCoordinate(firstTexel, texelCount, addressMode) * texelStride;
            int secondOffset = linear
                ? ResolveTexelCoordinate(firstTexel + 1, texelCount, addressMode) * texelStride
                : firstOffset;
            samples[index] = new CpuTextureAxisSample(
                firstOffset, secondOffset, linear ? texel - firstTexel : 0f);
        }
    }

    private static float SampleChannel(
        ReadOnlySpan<byte> top,
        ReadOnlySpan<byte> bottom,
        CpuTextureAxisSample column,
        float ratioY,
        int channel,
        bool linear)
    {
        int first = column.FirstOffset + channel;
        float topLeft = top[first] / 255f;
        if (!linear)
            return topLeft;

        int second = column.SecondOffset + channel;
        float topRight = top[second] / 255f;
        float bottomLeft = bottom[first] / 255f;
        float bottomRight = bottom[second] / 255f;
        float topValue = topLeft + ((topRight - topLeft) * column.Ratio);
        float bottomValue = bottomLeft + ((bottomRight - bottomLeft) * column.Ratio);
        return topValue + ((bottomValue - topValue) * ratioY);
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
