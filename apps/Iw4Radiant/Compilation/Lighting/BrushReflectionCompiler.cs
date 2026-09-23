using System.Numerics;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Codecs.GfxMap;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushReflectionCompiler
{
    internal static (IReadOnlyList<GfxImageAsset> Images, IReadOnlyList<GfxReflectionProbe> Origins)
        CaptureProbes(BrushLightingScene scene, IReadOnlyList<Vector3> origins)
    {
        // The canonical v22 cell reserves 67 bytes for authored probe indices.
        if (origins.Count is < 1 or > 67)
            throw new NotSupportedException("The compiled lighting profile requires between 1 and 67 authored reflection probes.");
        var images = new List<GfxImageAsset> { GfxReflectionProbeCodec.CreateDefaultImage() };
        var outputOrigins = new List<GfxReflectionProbe> { new(0, 0, 0) };
        const int size = GfxReflectionProbeCodec.ReflectionProbeEdgeLength;
        const int faceCount = GfxReflectionProbeCodec.ReflectionProbeFaceCount;
        var directions = new Vector3[size * size * faceCount];
        var directionXs = new float[directions.Length];
        var directionYs = new float[directions.Length];
        var directionZs = new float[directions.Length];
        for (int face = 0; face < faceCount; face++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int pixel = (face * size + y) * size + x;
            Vector3 direction = BrushLightingScene.CubeDirection(face,
                (float)x / (size - 1), (float)y / (size - 1));
            directions[pixel] = direction;
            directionXs[pixel] = direction.X;
            directionYs[pixel] = direction.Y;
            directionZs[pixel] = direction.Z;
        }
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = scene.CancellationToken,
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 2)
        };

        foreach (Vector3 origin in origins)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            var pixels = new byte[directions.Length * 4];
            Parallel.For(0, faceCount * size, parallelOptions, row =>
            {
                scene.CancellationToken.ThrowIfCancellationRequested();
                int firstPixel = row * size;
                for (int pixel = firstPixel; pixel < firstPixel + size; pixel++)
                {
                    Vector3 radiance = scene.CaptureRadiance(origin, directions[pixel]);
                    for (int channel = 0; channel < 3; channel++)
                    {
                        if (!float.IsFinite(radiance[channel]) || radiance[channel] < 0)
                            throw new InvalidDataException("A captured reflection pixel has invalid radiance.");
                        // The native probe is an LDR square-root encoding. Saturation
                        // matches that finite framebuffer range, before mip filtering.
                        pixels[pixel * 4 + channel] = (byte)MathF.Round(255 * MathF.Sqrt(Math.Clamp(radiance[channel], 0, 1)));
                    }
                    pixels[pixel * 4 + 3] = 255;
                }
            });
            var mips = new List<ReadOnlyMemory<byte>> { pixels };
            for (int mip = 1; mip < GfxReflectionProbeCodec.ReflectionProbeMipCount; mip++)
            {
                scene.CancellationToken.ThrowIfCancellationRequested();
                int edge = size >> mip;
                var filtered = new byte[edge * edge * faceCount * 4];
                // Native cubemap cooking convolves every mip directly from the
                // 64px faces with max(dot(sample, direction)-cos(angle), 0).
                float minimumDot = MathF.Cos(edge switch
                {
                    32 => 0.08726646f, 16 => 0.19198622f, 8 => 0.38397244f,
                    4 => 0.78539819f, 2 => MathF.PI * 0.5f, 1 => MathF.PI,
                    _ => throw new InvalidOperationException("Unexpected native reflection mip size.")
                });
                Parallel.For(0, faceCount * edge, parallelOptions, row =>
                {
                    scene.CancellationToken.ThrowIfCancellationRequested();
                    int face = row / edge, y = row % edge;
                    for (int x = 0; x < edge; x++)
                    {
                        Vector3 direction = BrushLightingScene.CubeDirection(face,
                            edge == 1 ? 0.5f : (float)x / (edge - 1), edge == 1 ? 0.5f : (float)y / (edge - 1));
                        Vector3 sum = Vector3.Zero;
                        float totalWeight = 0;
                        int sample = 0;
                        if (Vector.IsHardwareAccelerated)
                        {
                            int width = Vector<float>.Count;
                            var xDirection = new Vector<float>(direction.X);
                            var yDirection = new Vector<float>(direction.Y);
                            var zDirection = new Vector<float>(direction.Z);
                            // This SIMD dot is only a conservative broadphase. The
                            // 1e-5 slack exceeds the maximum rounding difference
                            // between the vector and scalar three-term dot products.
                            var rejection = new Vector<float>(minimumDot - 1e-5f);
                            for (; sample <= directions.Length - width; sample += width)
                            {
                                var dot = new Vector<float>(directionXs, sample) * xDirection +
                                    new Vector<float>(directionYs, sample) * yDirection +
                                    new Vector<float>(directionZs, sample) * zDirection;
                                if (!Vector.GreaterThanAny(dot, rejection)) continue;
                                for (int candidate = sample; candidate < sample + width; candidate++)
                                {
                                    float weight = Vector3.Dot(directions[candidate], direction) - minimumDot;
                                    if (weight <= 0) continue;
                                    sum += new Vector3(pixels[candidate * 4], pixels[candidate * 4 + 1],
                                        pixels[candidate * 4 + 2]) * weight;
                                    totalWeight += weight;
                                }
                            }
                        }
                        for (; sample < directions.Length; sample++)
                        {
                            float weight = Vector3.Dot(directions[sample], direction) - minimumDot;
                            if (weight <= 0) continue;
                            sum += new Vector3(pixels[sample * 4], pixels[sample * 4 + 1], pixels[sample * 4 + 2]) * weight;
                            totalWeight += weight;
                        }
                        if (totalWeight <= 0) throw new InvalidDataException("Reflection mip filtering has no source samples.");
                        sum /= totalWeight;
                        int offset = ((face * edge + y) * edge + x) * 4;
                        for (int channel = 0; channel < 3; channel++) filtered[offset + channel] = (byte)Math.Clamp((int)sum[channel], 0, 255);
                        filtered[offset + 3] = 255;
                    }
                });
                if (edge == 1)
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int average = Enumerable.Range(0, faceCount).Sum(face => filtered[face * 4 + channel]) / faceCount;
                        for (int face = 0; face < faceCount; face++) filtered[face * 4 + channel] = (byte)average;
                    }
                mips.Add(filtered);
            }
            images.Add(GfxReflectionProbeCodec.CreateImage(images.Count, mips));
            outputOrigins.Add(new GfxReflectionProbe(origin.X, origin.Y, origin.Z));
        }
        return (images, outputOrigins);
    }
}
