using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Codecs.GfxMap;

namespace Iw4Radiant.Compilation.Lighting;

internal static class BrushLightGridCompiler
{
    internal static GfxLightGrid BakeLightGrid(BrushLightingScene scene)
    {
        var mins = new ushort[3];
        var maxs = new ushort[3];
        for (int axis = 0; axis < 3; axis++)
        {
            int spacing = axis == 2 ? GfxLightGridCodec.VerticalSpacing : GfxLightGridCodec.HorizontalSpacing;
            float minimum = (scene.Minimum[axis] - GfxLightGridCodec.CoordinateOrigin) / spacing;
            float maximum = (scene.Maximum[axis] - GfxLightGridCodec.CoordinateOrigin) / spacing;
            if (minimum < 1 || maximum > ushort.MaxValue - 1)
                throw new NotSupportedException("The brush bounds exceed the native light-grid coordinate range.");
            mins[axis] = checked((ushort)(MathF.Floor(minimum) - 1));
            maxs[axis] = checked((ushort)(MathF.Ceiling(maximum) + 1));
        }
        int entryCount = checked((maxs[0] - mins[0] + 1) * (maxs[1] - mins[1] + 1) * (maxs[2] - mins[2] + 1));
        if (entryCount > ushort.MaxValue)
            throw new NotSupportedException("The direct-light brush profile supports at most 65535 light-grid samples. Reduce the compiled world bounds.");
        var entries = new List<GfxLightGridEntry>(entryCount);
        var colors = new List<GfxLightGridColors>
        {
            new(new byte[GfxLightGridColors.SerializedSize]),
            Encode(scene.AmbientDirections((scene.Minimum + scene.Maximum) * 0.5f))
        };
        var colorIndices = new Dictionary<string, ushort>(StringComparer.Ordinal);
        for (ushort index = 0; index < colors.Count; index++) colorIndices[Convert.ToHexString(colors[index].RgbBytes.ToArray())] = index;
        for (int x = mins[0]; x <= maxs[0]; x++)
        for (int y = mins[1]; y <= maxs[1]; y++)
        for (int z = mins[2]; z <= maxs[2]; z++)
        {
            scene.CancellationToken.ThrowIfCancellationRequested();
            Vector3 point = new(GfxLightGridCodec.CoordinateOrigin + x * GfxLightGridCodec.HorizontalSpacing,
                GfxLightGridCodec.CoordinateOrigin + y * GfxLightGridCodec.HorizontalSpacing,
                GfxLightGridCodec.CoordinateOrigin + z * GfxLightGridCodec.VerticalSpacing);
            if (scene.IsInsideSolid(point))
            {
                entries.Add(new GfxLightGridEntry(0, 0, 1));
                continue;
            }
            GfxLightGridColors sample = Encode(scene.AmbientDirections(point));
            string key = Convert.ToHexString(sample.RgbBytes.ToArray());
            if (!colorIndices.TryGetValue(key, out ushort colorIndex))
            {
                colorIndex = checked((ushort)colors.Count);
                colors.Add(sample);
                colorIndices.Add(key, colorIndex);
            }
            entries.Add(new GfxLightGridEntry(colorIndex, scene.SunVisibility(point, Vector3.Zero) > 0 ? (byte)1 : (byte)0, 0));
        }
        // Canonical BSP export omits the final linker-generated row. Keep that
        // row separate from the authored Colors[1] used by native fallback sampling.
        colors.Add(GfxLightGridCodec.CreateDefault());
        return GfxLightGridCodec.CreateDenseGrid(mins, maxs, entries, colors, 1);
    }

    private static GfxLightGridColors Encode(IReadOnlyList<Vector3> irradiance)
    {
        var bytes = new byte[GfxLightGridColors.SerializedSize];
        for (int sample = 0; sample < irradiance.Count; sample++)
        for (int channel = 0; channel < 3; channel++)
        {
            float value = irradiance[sample][channel];
            if (!float.IsFinite(value) || value < 0 || value > 4)
                throw new NotSupportedException("Calculated model lighting exceeds the native light-grid range.");
            // Native light-probe RGB decodes as (2 * byte / 255)^2.
            bytes[sample * 3 + channel] = (byte)Math.Clamp((int)MathF.Round(127.5f * MathF.Sqrt(value)), 0, 255);
        }
        return new GfxLightGridColors(bytes);
    }
}
