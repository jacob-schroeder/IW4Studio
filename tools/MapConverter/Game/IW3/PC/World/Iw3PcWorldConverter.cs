using System.Globalization;
using System.Text;
using IW4.Formats.D3dbsp;
using IW4.Game.Math;
using IW4.Linker.D3dbsp;
using MapConverter.IW3.PC.Assets.XModel;
using MapConverter.IW3.PC.Assets.World;
using MapConverter.IW3.PC.Readers;
using MapConverter.IW3.PC.Readers.World;

namespace MapConverter.Game.IW3.PC.World;

internal sealed record Iw3PcWorldExtraction(Iw3DynamicEntitySourceData DynamicEntities,
    Iw3XModelCollisionSourceData ModelCollision, Iw3PcWorldLightingInfo? Lighting,
    D3dbspStaticModelVisibility? StaticModelVisibility, IReadOnlySet<string> DecalMaterials);

internal static class Iw3PcWorldConverter
{
    internal static Iw3PcWorldExtraction Convert(Iw3PcZone zone, string bspPath,
        bool textured, CancellationToken token, IReadOnlyDictionary<string, Iw3PcModelProvider>? modelSources = null)
    {
        var source = new Iw3PcWorldSource(zone, modelSources ?? new Dictionary<string, Iw3PcModelProvider>());
        var dynamics = source.ReadDynamicEntities(token);
        var lumps = new List<(D3dbspLumpType Type, byte[] Data)>();
        var (materials, decalMaterials) = Iw3PcWorldCollision.AddLumps(zone, source.Clip.Offset, source.Graphics.Offset, lumps, token);
        var (entities, modelIndexBySource, placementBounds) = EncodeEntities(source, textured, token);
        Iw3PcWorldGraphics.AddLumps(zone, source.Graphics.Offset, source.Common.Offset, materials, modelIndexBySource, lumps, token);
        lumps.Add((D3dbspLumpType.Entities, entities));
        var collision = source.ReadCollision(token);
        var lighting = textured ? Iw3PcWorldLightingInfoReader.Read(zone, source.Graphics.Offset) : null;
        token.ThrowIfCancellationRequested();
        D3dbspFile.Create(lumps).Write(bspPath);
        var visibility = textured ? new D3dbspStaticModelVisibility(placementBounds,
            source.ReadShadowModelIndices()) : null;
        return new(dynamics, collision, lighting, visibility, decalMaterials);
    }

    private static (byte[] Entities, int[] ModelIndexBySource, IReadOnlyList<Bounds> Bounds) EncodeEntities(
        Iw3PcWorldSource source, bool includeRenderModels, CancellationToken token)
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
        var modelIndexBySource = new List<int>();
        var placementBounds = new List<Bounds>();
        foreach (var placement in source.StaticPlacements())
        {
            int index = modelIndexBySource.Count;
            token.ThrowIfCancellationRequested();
            if (placement.Name.Length >= 64 || placement.Name.IndexOfAny(['"', '\r', '\n']) >= 0)
                throw new InvalidDataException($"Invalid IW3 PC static model name '{placement.Name}'.");
            // Source order is shared by the cell trees, shadow lists and misc_model entries.
            modelIndexBySource.Add(includeRenderModels ? index : -1);
            if (placement.Scale <= 0 || (placement.Flags & ~1) != 0)
                throw new InvalidDataException($"Invalid scale or flags for IW3 PC static placement {index} ('{placement.Name}').");
            if (!float.IsFinite(placement.CullDistance) || placement.CullDistance < 0)
                throw new InvalidDataException($"Invalid cull distance for IW3 PC static placement {index} ('{placement.Name}').");
            ushort cullDistance = placement.CullDistance >= ushort.MaxValue
                ? ushort.MaxValue : (ushort)placement.CullDistance;
            var lightingOrigin = new float[3];
            var halfSize = new float[3];
            for (int component = 0; component < 3; component++)
            {
                float min = placement.BoundsMins[component], max = placement.BoundsMaxs[component];
                if (!float.IsFinite(min) || !float.IsFinite(max) || min > max)
                    throw new InvalidDataException($"Invalid bounds for IW3 PC static placement {index} ('{placement.Name}').");
                lightingOrigin[component] = (float)(((double)min + max) * 0.5);
                halfSize[component] = (float)(((double)max - min) * 0.5);
            }
            if (includeRenderModels)
                placementBounds.Add(new Bounds
                {
                    MidPoint = new Vec3 { X = lightingOrigin[0], Y = lightingOrigin[1], Z = lightingOrigin[2] },
                    HalfSize = new Vec3 { X = halfSize[0], Y = halfSize[1], Z = halfSize[2] }
                });
            var axis = new Vec3[3];
            for (int row = 0; row < 3; row++)
                axis[row] = new() { X = placement.Axis[row * 3], Y = placement.Axis[row * 3 + 1], Z = placement.Axis[row * 3 + 2] };
            Vec3 angles = D3dbspAssetLinker.StaticModelAngles(axis, index);
            string block = string.Create(CultureInfo.InvariantCulture, $$"""

                {
                "classname" "misc_model"
                "model" "{{placement.Name}}"
                "origin" "{{placement.Origin[0]:g9}} {{placement.Origin[1]:g9}} {{placement.Origin[2]:g9}}"
                "angles" "{{angles.X:g9}} {{angles.Y:g9}} {{angles.Z:g9}}"
                "modelscale" "{{placement.Scale:g9}}"
                "spawnflags" "{{(placement.Flags & 1) * 2}}"
                "cullDist" "{{cullDistance}}"
                "reflectionProbeIndex" "{{placement.ReflectionProbeIndex}}"
                "lightingOrigin" "{{lightingOrigin[0]:g9}} {{lightingOrigin[1]:g9}} {{lightingOrigin[2]:g9}}"
                "gndLt" "{{placement.GroundLighting[2]:X2}}{{placement.GroundLighting[1]:X2}}{{placement.GroundLighting[0]:X2}}{{placement.GroundLighting[3]:X2}}{{placement.PrimaryLightIndex:X2}}"
                }

                """);
            output.Write(Encoding.Latin1.GetBytes(block));
        }
        output.WriteByte(0);
        return (output.ToArray(), modelIndexBySource.ToArray(), placementBounds);
    }

}
