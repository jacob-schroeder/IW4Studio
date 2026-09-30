using System.Numerics;
using System.Text;
using IW4.Formats.D3dbsp;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Physics;
using IW4.Game.Codecs.MapEnts;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Bounds = IW4.Game.Math.Bounds;

namespace Iw4Radiant.Compilation;

internal sealed class MapStageLighting
{
    private readonly MapSunProperties[] _suns;
    private readonly float[] _ambient;
    private readonly (Stage Metadata, (Bounds Bounds, IReadOnlyList<TriggerSlab> Slabs)[] Hulls)[] _regions;

    private MapStageLighting(MapSunProperties[] suns, float[] ambient,
        (Stage Metadata, (Bounds Bounds, IReadOnlyList<TriggerSlab> Slabs)[] Hulls)[] regions)
    {
        _suns = suns;
        _ambient = ambient;
        _regions = regions;
    }

    internal byte SunCount => checked((byte)_suns.Length);

    internal MapSunProperties Sun(byte sunIndex) => sunIndex > 0 && sunIndex <= SunCount
        ? _suns[sunIndex - 1] : throw new ArgumentOutOfRangeException(nameof(sunIndex));

    internal float AmbientScale(byte sunIndex) => sunIndex > 0 && sunIndex <= SunCount
        ? _ambient[sunIndex - 1] : throw new ArgumentOutOfRangeException(nameof(sunIndex));

    internal byte SunIndexAt(Vector3 position)
    {
        // Native Stage selection visits serialized nonzero stages in order. Each
        // trigger is a union of strict-interior hulls; Stage0 is the fallback.
        foreach (var (stage, hulls) in _regions)
        {
            Vector3 local = position - new Vector3(stage.Origin.X, stage.Origin.Y, stage.Origin.Z);
            foreach (var (bounds, slabs) in hulls)
                if (TriggerGeometry.ContainsPoint(bounds, slabs, local)) return stage.SunPrimaryLightIndex;
        }
        return 1;
    }

    internal static MapStageLighting Read(MapDocument document)
    {
        if (!MapSunProperties.TryRead(document.World, out MapSunProperties? worldSun, out string? error) ||
            worldSun is not { } sun)
            throw new InvalidDataException(error ?? "Worldspawn needs authored sunlight for compilation.");

        MapEntity[] authored = document.Entities.Where(entity => entity.ClassName == "stage").ToArray();
        if (authored.Length == 0) return new MapStageLighting([sun], [1], []);
        // One Sun per Stage. Ordinary light ordinals must stay below the top S
        // bytes reserved for occluded Suns, hence S <= 127 including world Sun1.
        if (authored.Length > 126)
            throw new NotSupportedException("Compilation supports at most 126 authored Stages plus the world Sun.");

        var suns = new MapSunProperties[authored.Length + 1];
        var ambient = new float[suns.Length];
        var regions = new (Stage Metadata, (Bounds Bounds, IReadOnlyList<TriggerSlab> Slabs)[] Hulls)[authored.Length];
        suns[0] = sun;
        ambient[0] = MapSunProperties.ReadAmbient(document.World, 1);
        var triggers = MapCompiler.BrushEntities(document).Where(IsMapTriggerEntity)
            .Select((entity, index) => (entity, index)).ToDictionary(item => item.entity, item => item.index);
        for (int index = 0; index < authored.Length; index++)
        {
            MapEntity stage = authored[index];
            if (stage.Brushes.Count == 0 || stage.Terrains.Count != 0 || stage.PreservedPrimitives.Count != 0)
                throw new NotSupportedException("Stage needs convex brushes and no terrain or preserved primitives.");
            if (!stage.TryGetOrigin(out Vector3 origin))
                throw new InvalidDataException("Stage needs a finite three-component origin.");
            _ = EntityOrientation.Read(stage);
            if (stage.Properties.ContainsKey("model"))
                throw new InvalidDataException("Stage has a manual model reference. Compilation assigns its brush model.");
            foreach (string key in stage.Properties.Keys)
                if (key is not ("classname" or "origin" or "angles" or "angle" or "targetname" or "sunlight" or
                    "suncolor" or "sundirection" or "ambient"))
                    throw new NotSupportedException($"Stage property '{key}' is outside the supported lighting profile.");

            if (!MapSunProperties.TryReadStage(stage, document.World, out MapSunProperties? stageSun, out error) ||
                stageSun is not { } localSun)
                throw new InvalidDataException($"Stage sunlight: {error}");
            suns[index + 1] = localSun;
            ambient[index + 1] = MapSunProperties.ReadAmbient(stage, ambient[0]);
            int ordinal = triggers[stage];
            if (ordinal > ushort.MaxValue)
                throw new NotSupportedException("Stage trigger index exceeds the native ushort range.");
            var hulls = new (Bounds Bounds, IReadOnlyList<TriggerSlab> Slabs)[stage.Brushes.Count];
            for (int brushIndex = 0; brushIndex < stage.Brushes.Count; brushIndex++)
            {
                // Stage rows have no rotation. Brush vertices already carry
                // authored rotation, so retain it in the local hull geometry.
                MapBrush local = stage.Brushes[brushIndex].Clone();
                local.Transform(Matrix4x4.CreateTranslation(-origin), textureLock: true);
                BrushGeometry.Validate(local);
                (Vector3 minimum, Vector3 maximum) = local.GetBounds();
                Bounds bounds = BrushCollisionCompiler.MakeBounds(minimum, maximum);
                if (!BrushGeometry.IsFinite(minimum) || !BrushGeometry.IsFinite(maximum) ||
                    minimum.X >= maximum.X || minimum.Y >= maximum.Y || minimum.Z >= maximum.Z)
                    throw new NotSupportedException("Stage hull dimensions must be finite and positive.");
                CBrushSide[] sides = local.GetPolygons().Where(polygon =>
                        BrushCollisionCompiler.GetAxialSide(polygon.Face.Normal) < 0)
                    .Select(polygon => new CBrushSide
                    {
                        Plane = BrushCollisionCompiler.MakePlane(polygon.Face.Normal,
                            (float)BrushGeometry.Dot(polygon.Face.Normal, polygon.Face.A))
                    }).ToArray();
                hulls[brushIndex] = (bounds, TriggerGeometry.CreateSlabs(sides, bounds, brushIndex));
            }
            regions[index] = (new Stage
            {
                StageName = $"stage {index + 1}",
                Origin = BrushCollisionCompiler.ToVec3(origin),
                TriggerIndex = (ushort)ordinal,
                SunPrimaryLightIndex = checked((byte)(index + 2))
            }, hulls);
        }
        return new MapStageLighting(suns, ambient, regions);
    }

    internal D3dbspFile AppendStageMetadata(D3dbspFile file)
    {
        if (SunCount == 1) return file;
        byte[] current = file.GetRequiredData(D3dbspLumpType.Entities).ToArray();
        if (current.Length == 0 || current[^1] != 0)
            throw new InvalidDataException("The compiled entity lump must end with a null byte.");
        var rows = new StringBuilder();
        foreach (var (stage, _) in _regions)
        {
            string origin = FormattableString.Invariant($"{stage.Origin.X:R} {stage.Origin.Y:R} {stage.Origin.Z:R}");
            rows.Append(FormattableString.Invariant(
                $"\n{{\n\"classname\" \"stage\"\n\"name\" \"{stage.StageName}\"\n\"origin\" \"{origin}\"\n\"script_index\" \"{stage.TriggerIndex}\"\n\"sunPrimaryLightIndex\" \"{stage.SunPrimaryLightIndex}\"\n}}\n"));
        }
        byte[] suffix = Encoding.Latin1.GetBytes(rows.ToString());
        byte[] entities = new byte[current.Length + suffix.Length];
        current.AsSpan(0, current.Length - 1).CopyTo(entities);
        suffix.CopyTo(entities, current.Length - 1);
        return D3dbspFile.Create(file.Lumps.Select(lump =>
            (lump.Type, lump.Type == D3dbspLumpType.Entities ? entities : lump.Data)).ToArray());
    }

    private static bool IsMapTriggerEntity(MapEntity entity) =>
        entity.ClassName.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) ||
        entity.ClassName.Equals("info_volume", StringComparison.OrdinalIgnoreCase) ||
        entity.ClassName.Equals("stage", StringComparison.OrdinalIgnoreCase);
}
