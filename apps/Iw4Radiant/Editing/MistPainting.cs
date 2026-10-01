using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class MistPainting
{
    internal const string AssetName = "smoke/iw4radiant_mist_dense";
    private const string Marker = "_iw4radiant_mist";
    private const string Spacing = "_iw4radiant_mist_spacing";

    internal static bool IsPainted(MapEntity entity) => IsFxOrigin(entity) &&
        entity.Properties.GetValueOrDefault(Marker) == "1";

    internal static bool IsMist(MapEntity entity) => IsFxOrigin(entity) &&
        (IsPainted(entity) || entity.Properties.GetValueOrDefault("fx") is
            AssetName or "smoke/room_smoke_200");

    internal static MapEntity Create(Vector3 surface, float spacing)
    {
        var entity = new MapEntity();
        entity.Properties["classname"] = "fx_origin";
        entity.Properties["angles"] = "0 0 0";
        entity.Properties["origin"] = Format(surface + Vector3.UnitZ * 32);
        entity.Properties["is_sound"] = "0";
        entity.Properties["fx"] = AssetName;
        entity.Properties["fx_playback"] = "start";
        entity.Properties[Marker] = "1";
        entity.Properties[Spacing] = spacing.ToString("G9", CultureInfo.InvariantCulture);
        return entity;
    }

    internal static bool Overlaps(MapEntity entity, Vector3 surface, float spacing)
    {
        if (!IsMist(entity) || !entity.TryGetOrigin(out Vector3 origin)) return false;
        float existingSpacing = spacing;
        if (entity.Properties.TryGetValue(Spacing, out string? text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float stored) &&
            float.IsFinite(stored) && stored > 0)
            existingSpacing = stored;
        Vector3 candidateOrigin = surface + Vector3.UnitZ * 32;
        float separation = MathF.Max(spacing, existingSpacing);
        return Vector3.DistanceSquared(candidateOrigin, origin) < separation * separation;
    }

    private static bool IsFxOrigin(MapEntity entity) => entity.ClassName == "fx_origin" &&
        entity.Properties.GetValueOrDefault("is_sound") is null or "" or "0" &&
        !string.IsNullOrWhiteSpace(entity.Properties.GetValueOrDefault("fx"));

    private static string Format(Vector3 value) => FormattableString.Invariant(
        $"{value.X:G9} {value.Y:G9} {value.Z:G9}");
}
