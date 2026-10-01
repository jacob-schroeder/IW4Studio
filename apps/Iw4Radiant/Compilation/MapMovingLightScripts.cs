using System.Globalization;
using System.Numerics;
using System.Text;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal sealed record MapMovingLightScripts(string Name, string Source)
{
    internal static MapMovingLightScripts? Create(MapDocument source, string sourcePath, string mapName)
    {
        MapDocument expanded = PrefabLibrary.ExpandForCompilation(source, sourcePath);
        MovingLight[] lights = Enumerate(expanded).ToArray();
        if (lights.Length == 0) return null;
        if (mapName.Length == 0 || mapName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidDataException("A map with moving lights needs a filename containing only letters, numbers, and underscores.");

        var script = new StringBuilder("main()\r\n{\r\n");
        foreach (MovingLight light in lights)
            script.Append("\tthread sweep_").Append(light.Index).Append("();\r\n");
        script.Append("}\r\n");
        foreach (MovingLight light in lights)
        {
            if (light.Light.SweepPlane != 0)
            {
                AppendTiltedSweep(script, light);
                continue;
            }
            float halfSeconds = light.Light.SweepSeconds / 2f;
            float easeSeconds = light.Light.SweepSeconds * MapLight.SweepEaseFraction;
            float halfEaseSeconds = halfSeconds * MapLight.SweepEaseFraction;
            script.Append("\r\nsweep_").Append(light.Index).Append("()\r\n{\r\n")
                .Append("\tspot = getent(\"").Append(light.TargetName).Append("\", \"targetname\");\r\n")
                .Append("\tif (!isdefined(spot)) return;\r\n")
                .Append("\twait ").Append(Number(MapLight.SweepWarmupSeconds)).Append(";\r\n")
                .Append("\tif (!isdefined(spot)) return;\r\n")
                .Append("\tspot rotateTo(").Append(Angles(light.Light.SweepStartAngles)).Append(", ")
                .Append(Number(halfSeconds)).Append(", ").Append(Number(halfEaseSeconds)).Append(", ")
                .Append(Number(halfEaseSeconds)).Append(");\r\n")
                .Append("\twait ").Append(Number(halfSeconds)).Append(";\r\n")
                .Append("\twhile (isdefined(spot))\r\n\t{\r\n")
                .Append("\t\tspot rotateTo(").Append(Angles(light.Light.SweepEndAngles)).Append(", ")
                .Append(Number(light.Light.SweepSeconds)).Append(", ").Append(Number(easeSeconds)).Append(", ")
                .Append(Number(easeSeconds)).Append(");\r\n")
                .Append("\t\twait ").Append(Number(light.Light.SweepSeconds)).Append(";\r\n")
                .Append("\t\tif (!isdefined(spot)) return;\r\n")
                .Append("\t\tspot rotateTo(").Append(Angles(light.Light.SweepStartAngles)).Append(", ")
                .Append(Number(light.Light.SweepSeconds)).Append(", ").Append(Number(easeSeconds)).Append(", ")
                .Append(Number(easeSeconds)).Append(");\r\n")
                .Append("\t\twait ").Append(Number(light.Light.SweepSeconds)).Append(";\r\n")
                .Append("\t}\r\n}\r\n");
        }
        return new MapMovingLightScripts($"maps/mp/{mapName}_lights.gsc", script.ToString());
    }

    internal static IEnumerable<MapEntity> CreateRuntimeEntities(MapDocument document)
    {
        foreach (MovingLight light in Enumerate(document))
        {
            Vector3 center = light.Light.SweepPlane == 0
                ? (light.Light.SweepStartAngles + light.Light.SweepEndAngles) / 2f
                : light.Light.AimAngles;
            var entity = new MapEntity();
            entity.Properties.Add("classname", "light");
            entity.Properties.Add("pl#", light.Index.ToString(CultureInfo.InvariantCulture));
            entity.Properties.Add("origin", light.Entity.Properties["origin"]);
            entity.Properties.Add("angles", Coordinates(center));
            entity.Properties.Add("targetname", light.TargetName);
            yield return entity;
        }
    }

    internal (string Name, string Path) WriteTo(string directory)
    {
        string path = Path.Combine(directory, Name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ??
            throw new InvalidDataException("The moving-light script output has no directory."));
        File.WriteAllText(path, Source, new UTF8Encoding(false));
        return (Name, path);
    }

    private static IEnumerable<MovingLight> Enumerate(MapDocument document)
    {
        var usedNames = document.Entities.Select(entity => entity.Properties.GetValueOrDefault("targetname"))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        int sunCount = document.Entities.Any(entity => entity.ClassName == "stage")
            ? MapStageLighting.Read(document).SunCount : 1;
        foreach (var (entity, light, index) in MapLight.EnumeratePrimary(document, sunCount))
        {
            if (!light.IsSpotlight || !light.IsMoving) continue;
            string baseName = $"iw4radiant_moving_light_{index:D3}";
            string name = baseName;
            for (int suffix = 1; !usedNames.Add(name); suffix++) name = $"{baseName}_{suffix}";
            yield return new MovingLight(entity, light, index, name);
        }
    }

    private static void AppendTiltedSweep(StringBuilder script, MovingLight light)
    {
        Vector3[] approach = light.Light.SampleSweepAngles(true);
        Vector3[] leg = light.Light.SampleSweepAngles(false);
        float approachStep = light.Light.SweepSeconds / (2 * (approach.Length - 1));
        float legStep = light.Light.SweepSeconds / (leg.Length - 1);
        script.Append("\r\nsweep_").Append(light.Index).Append("()\r\n{\r\n")
            .Append("\tspot = getent(\"").Append(light.TargetName).Append("\", \"targetname\");\r\n")
            .Append("\tif (!isdefined(spot)) return;\r\n")
            .Append("\twait ").Append(Number(MapLight.SweepWarmupSeconds)).Append(";\r\n")
            .Append("\tif (!isdefined(spot)) return;\r\n");
        for (int i = 1; i < approach.Length; i++)
            AppendStep(script, approach[i], approachStep, "\t");
        script.Append("\twhile (isdefined(spot))\r\n\t{\r\n");
        for (int i = 1; i < leg.Length; i++)
            AppendStep(script, leg[i], legStep, "\t\t");
        for (int i = leg.Length - 2; i >= 0; i--)
            AppendStep(script, leg[i], legStep, "\t\t");
        script.Append("\t}\r\n}\r\n");
    }

    private static void AppendStep(StringBuilder script, Vector3 angles, float seconds, string indent)
    {
        script.Append(indent).Append("spot rotateTo(").Append(Angles(angles)).Append(", ")
            .Append(Number(seconds)).Append(", 0, 0);\r\n")
            .Append(indent).Append("wait ").Append(Number(seconds)).Append(";\r\n")
            .Append(indent).Append("if (!isdefined(spot)) return;\r\n");
    }

    private static string Angles(Vector3 value) => "(" + Coordinates(value).Replace(' ', ',') + ")";
    private static string Coordinates(Vector3 value) =>
        $"{Number(value.X)} {Number(value.Y)} {Number(value.Z)}";
    private static string Number(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private readonly record struct MovingLight(MapEntity Entity, MapLight Light, int Index, string TargetName);
}
