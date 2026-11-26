using System.Globalization;
using System.Text;
using IW4.Formats.D3dbsp;
using IW4.Game.Math;
using IW4.Linker.D3dbsp;
using MapConverter.IW3.PC.Assets.XModel;
using MapConverter.IW3.PC.Assets.World;
using MapConverter.IW3.PC.Readers;
using MapConverter.IW3.PC.Readers.World;
using MapConverter.IW3.PC.Rules;

namespace MapConverter.Game.IW3.PC.World;

internal sealed record Iw3PcWorldExtraction(Iw3DynamicEntitySourceData DynamicEntities,
    Iw3XModelCollisionSourceData ModelCollision, Iw3PcWorldLightingInfo? Lighting);

internal static class Iw3PcWorldConverter
{
    internal static Iw3PcWorldExtraction Convert(Iw3PcZone zone, string bspPath,
        bool textured, CancellationToken token, IReadOnlyDictionary<string, Iw3PcModelProvider>? modelSources = null)
    {
        var source = new Iw3PcWorldSource(zone, modelSources ?? new Dictionary<string, Iw3PcModelProvider>());
        var dynamics = source.ReadDynamicEntities(token);
        var lumps = new List<(D3dbspLumpType Type, byte[] Data)>();
        ushort[] materials = Iw3PcWorldCollision.AddLumps(zone, source.Clip.Offset, source.Graphics.Offset, lumps, token);
        Iw3PcWorldGraphics.AddLumps(zone, source.Graphics.Offset, source.Common.Offset, materials, lumps, token);
        lumps.Add((D3dbspLumpType.Entities, EncodeEntities(source, token)));
        var collision = source.ReadCollision(token);
        var lighting = textured ? Iw3PcWorldLightingInfoReader.Read(zone, source.Graphics.Offset) : null;
        token.ThrowIfCancellationRequested();
        D3dbspFile.Create(lumps).Write(bspPath);
        return new(dynamics, collision, lighting);
    }

    private static byte[] EncodeEntities(Iw3PcWorldSource source, CancellationToken token)
    {
        byte[] retained = D3dbspAssetLinker.FilterEntities(source.EntityText(), entities =>
        {
            var keep = new bool[entities.Count];
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                string? classname = entity.GetValueOrDefault("classname");
                keep[i] = classname != "misc_model" && entity.GetValueOrDefault("targetname") != "oldschool_pickup";
                if (keep[i] && classname == "script_model" && entity.GetValueOrDefault("model") is { Length: > 0 } name &&
                    name[0] is not ('*' or ','))
                    source.IncludeScriptModel(name);
            }
            if (!keep.Contains(true)) throw new InvalidDataException("IW3 PC MapEnts contains no retained entities.");
            return keep;
        });
        using var output = new MemoryStream();
        output.Write(retained);
        if (retained.Length > 0 && retained[^1] != (byte)'\n') output.WriteByte((byte)'\n');
        int index = -1;
        foreach (var placement in source.StaticPlacements())
        {
            index++;
            token.ThrowIfCancellationRequested();
            if (placement.Name.Length >= 64 || placement.Name.IndexOfAny(['"', '\r', '\n']) >= 0)
                throw new InvalidDataException($"Invalid IW3 PC static model name '{placement.Name}'.");
            var name = Iw3PcModelRules.StaticEntityModelName(placement.Name);
            if (name == null) continue;
            if (placement.Scale <= 0 || (placement.Flags & ~1) != 0)
                throw new InvalidDataException($"Invalid scale or flags for IW3 PC static placement {index} ('{placement.Name}').");
            var axis = new Vec3[3];
            for (int row = 0; row < 3; row++)
                axis[row] = new() { X = placement.Axis[row * 3], Y = placement.Axis[row * 3 + 1], Z = placement.Axis[row * 3 + 2] };
            Vec3 angles = D3dbspAssetLinker.StaticModelAngles(axis, index);
            string block = string.Create(CultureInfo.InvariantCulture, $$"""

                {
                "classname" "misc_model"
                "model" "{{name.Value}}"
                "origin" "{{placement.Origin[0]:g9}} {{placement.Origin[1]:g9}} {{placement.Origin[2]:g9}}"
                "angles" "{{angles.X:g9}} {{angles.Y:g9}} {{angles.Z:g9}}"
                "modelscale" "{{placement.Scale:g9}}"
                "spawnflags" "{{(placement.Flags & 1) * 2}}"
                }

                """);
            output.Write(Encoding.Latin1.GetBytes(block));
        }
        output.WriteByte(0);
        return output.ToArray();
    }

}
