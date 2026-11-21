using System.Globalization;
using System.Numerics;
using IW4.Game.Assets.MapEnts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

internal sealed class MissionObject
{
    internal MissionObject(int index, AddonMapEntsEntityRow row, MapTriggers triggers, bool editable,
        Func<int, bool> canResize, Func<int, IReadOnlyList<AddonEntitySceneOutline>> getOutlines)
    {
        Index = index;
        Properties = row.Properties;
        ClassName = Get("classname") ?? "";
        Kind = ClassName switch
        {
            "worldspawn" => "world",
            "script_struct" or "script_origin" => "marker",
            _ when ClassName.StartsWith("info_player_start", StringComparison.OrdinalIgnoreCase) => "start",
            _ when ClassName.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) => "trigger",
            _ when ClassName.StartsWith("actor_", StringComparison.OrdinalIgnoreCase) => "actor",
            _ when ClassName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) => "pickup",
            _ when ClassName.Contains("model", StringComparison.OrdinalIgnoreCase) => "model",
            _ => "other"
        };
        Group = Kind switch
        {
            "world" => "Map settings", "marker" => "Markers", "start" => "Player starts", "trigger" => "Trigger areas",
            "actor" => "Actors", "pickup" => "Pickups", "model" => "Models", _ => "Other objects"
        };
        var friendly = Kind switch
        {
            "world" => "Map settings", "start" => "Player start", "marker" => "Script marker",
            "trigger" when Get("script_flag") is { Length: > 0 } flag => Humanize(flag) + " trigger",
            "trigger" => ClassName switch
            {
                "trigger_multiple_spawn" => "Spawn trigger", "trigger_use" => "Use trigger",
                "trigger_multiple" => "Repeatable trigger", _ => "Trigger area"
            },
            "actor" => Humanize(ClassName["actor_".Length..]),
            "pickup" => Humanize(ClassName["weapon_".Length..]) + " pickup",
            _ => Humanize(ClassName)
        };
        Title = Get("script_noteworthy") is { Length: > 0 } note ? Humanize(note) : friendly;
        var nativeModel = Get("model");
        ModelIndex = nativeModel?.StartsWith('?') == true && int.TryParse(nativeModel.AsSpan(1), NumberStyles.None,
            CultureInfo.InvariantCulture, out var m) && (uint)m < (uint)triggers.Models.Count ? m : -1;
        AddonEntitySceneBox? box = null;
        IReadOnlyList<AddonEntitySceneOutline>? outlines = null;
        if (ModelIndex >= 0)
        {
            outlines = getOutlines(ModelIndex);
            var model = triggers.Models[ModelIndex];
            var hulls = triggers.Hulls.Skip(model.FirstHull).Take(model.HullCount).ToArray();
            var slabs = hulls.Sum(h => (int)h.SlabCount);
            if (hulls.Length == 1 && canResize(ModelIndex))
            {
                var bounds = hulls[0].Bounds;
                box = new(FromNative(bounds.MidPoint), FromNative(bounds.HalfSize));
                outlines = null;
            }
            Shape = box is null
                ? $"Trigger area · {hulls.Length} {(hulls.Length == 1 ? "hull" : "hulls")} · {slabs} {(slabs == 1 ? "slab" : "slabs")}"
                : "Box trigger · drag a corner to resize";
        }
        else if (nativeModel?.StartsWith('?') == true)
            Shape = $"Trigger model {nativeModel} is missing from this overlay.";
        if (Kind != "world" && ParseVector(Get("origin")) is { } p)
            Scene = new(Index, Title, Kind, p, ParseFloat(Get("radius")), ParseVector(Get("angles"))?.Y, box,
                outlines, Get("targetname"), Get("target"), editable);
    }

    internal int Index { get; }
    internal string ClassName { get; }
    internal string Kind { get; }
    internal string Group { get; }
    internal string Title { get; }
    internal string Detail => (Get("targetname") is { Length: > 0 } name ? name + "  ·  " : "") +
        $"{(ClassName.Length == 0 ? "No class" : ClassName)}  ·  #{Index + 1}";
    internal int ModelIndex { get; }
    internal string? Shape { get; }
    internal IReadOnlyList<KeyValuePair<string, string>> Properties { get; }
    internal AddonEntitySceneObject? Scene { get; }
    internal string? Get(string key) => Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    internal bool Contains(string search) => Title.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Detail.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Properties.Any(p => p.Value.Contains(search, StringComparison.OrdinalIgnoreCase));

    internal static string Humanize(string raw)
    {
        var words = raw.Trim().Replace('_', ' ');
        return words.Length == 0 ? "Unnamed object" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    internal static float? ParseFloat(string? text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
        && float.IsFinite(f) ? f : null;

    internal static Vector3? ParseVector(string? text)
    {
        var parts = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts?.Length == 3 && ParseFloat(parts[0]) is float x && ParseFloat(parts[1]) is float y && ParseFloat(parts[2]) is float z
            ? new Vector3(x, y, z) : null;
    }

    internal static Vector3 FromNative(IW4.Game.Math.Vec3 v) => new(v.X, v.Y, v.Z);
    internal static IW4.Game.Math.Vec3 ToNative(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
    internal static string Number(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    internal static string Vector(Vector3 p) => $"{Number(p.X)} {Number(p.Y)} {Number(p.Z)}";
}
