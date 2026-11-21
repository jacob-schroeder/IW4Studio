using System.Numerics;
using IW4.Game.Assets.ColMap;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

internal static class MissionBaseMap
{
    internal static ClipMapAsset? Find(IEnumerable<ClipMapAsset> maps, string mission)
    {
        var baseNames = CandidateNames(mission).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return maps.Where(m => m.Name is { Length: > 0 } name && !name.StartsWith(',') &&
                baseNames.Contains(Path.GetFileNameWithoutExtension(name)) && (m.BrushBounds.Count > 0 || m.TriIndices.Count > 0))
            .OrderByDescending(m => Path.GetFileNameWithoutExtension(m.Name)!.Length).FirstOrDefault();
    }

    internal static string? Nearby(FastFileWorkspace? workspace, string mission)
    {
        if (workspace is null || workspace.Document.IsBlank || Path.GetDirectoryName(workspace.Document.SourcePath) is not { } dir) return null;
        foreach (string name in CandidateNames(mission))
        {
            var candidate = Path.Combine(dir, name + ".ff");
            if (!string.Equals(candidate, workspace.Document.SourcePath, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // Standalone mission files commonly end with the full base-map name, including its underscores.
    private static IEnumerable<string> CandidateNames(string mission)
    {
        var name = Path.GetFileNameWithoutExtension(mission);
        yield return name;
        if (!name.StartsWith("so_", StringComparison.OrdinalIgnoreCase)) yield break;
        for (int i = name.IndexOf('_', 3); i >= 0 && i + 1 < name.Length; i = name.IndexOf('_', i + 1))
            yield return name[(i + 1)..];
    }

    internal static IReadOnlyList<AddonEntitySceneOutline> Load(string path, string mission)
    {
        using var session = new DbLoadSession();
        var zone = session.DB_LoadXZone(path, XZoneFlags.DB_ZONE_DEV);
        var map = Find(zone.Context.AssetPool.Entries.Select(e => e.Asset).OfType<ClipMapAsset>(), mission)
            ?? throw new InvalidDataException("This fastfile contains no matching collision geometry.");
        return Outlines(map);
    }

    internal static IReadOnlyList<AddonEntitySceneOutline> Outlines(ClipMapAsset map)
    {
        var outlines = new List<AddonEntitySceneOutline>(map.BrushBounds.Count + map.TriIndices.Count / 3);
        foreach (var bounds in map.BrushBounds)
        {
            var center = MissionObject.FromNative(bounds.MidPoint);
            var half = MissionObject.FromNative(bounds.HalfSize);
            Vector3 a = center - half, b = center + half;
            outlines.Add(new(Rect(a.X, a.Y, b.X, b.Y), Rect(a.X, a.Z, b.X, b.Z)));
        }
        for (int i = 0; i + 2 < map.TriIndices.Count; i += 3)
        {
            int ia = map.TriIndices[i], ib = map.TriIndices[i + 1], ic = map.TriIndices[i + 2];
            if (ia >= map.Verts.Count || ib >= map.Verts.Count || ic >= map.Verts.Count) continue;
            var a = map.Verts[ia]; var b = map.Verts[ib]; var c = map.Verts[ic];
            outlines.Add(new([new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y)],
                             [new(a.X, a.Z), new(b.X, b.Z), new(c.X, c.Z)]));
        }
        return outlines;
    }
    private static Vector2[] Rect(float x, float y, float u, float v) => [new(x, y), new(u, y), new(u, v), new(x, v)];
}
