using System.Globalization;
using System.Numerics;
using System.Text;

namespace Iw4Radiant.MapSource;

internal static class MapWriter
{
    public static string Serialize(MapDocument document)
    {
        if (document.Entities.Count == 0 || document.Entities[0].ClassName != "worldspawn" ||
            document.Entities.Count(entity => entity.ClassName == "worldspawn") != 1)
            throw new FormatException("A map must begin with exactly one worldspawn entity.");
        var output = new StringBuilder();
        foreach (string line in document.Header)
            output.AppendLine(line);
        for (int index = 0; index < document.Entities.Count; index++)
        {
            MapEntity entity = document.Entities[index];
            output.AppendLine("{");
            foreach (string directive in entity.Directives)
                output.AppendLine(directive);
            foreach (var property in entity.Properties)
                output.Append(Quote(property.Key)).Append(' ').AppendLine(Quote(property.Value));
            foreach (MapBrush brush in entity.Brushes)
            {
                output.AppendLine("{");
                foreach (string directive in brush.Directives)
                    output.AppendLine(directive);
                foreach (MapFace face in brush.Faces)
                {
                    output.Append(" ( ").Append(Position(face.A)).Append(" ) ( ").Append(Position(face.B))
                        .Append(" ) ( ").Append(Position(face.C)).Append(" ) ").Append(Name(face.Material))
                        .Append(' ').AppendLine(face.Projection);
                }
                output.AppendLine("}");
            }
            foreach (MapTerrain terrain in entity.Terrains)
                WriteTerrain(output, terrain);
            foreach (string primitive in entity.PreservedPrimitives)
                output.AppendLine(primitive);
            output.AppendLine("}");
        }
        return output.ToString();
    }

    private static void WriteTerrain(StringBuilder output, MapTerrain terrain)
    {
        long count = (long)terrain.Width * terrain.Height;
        if (terrain.Width < 2 || terrain.Height < 2 || count != terrain.Vertices.Length ||
            count != terrain.TextureCoordinates.Length || count != terrain.LightmapCoordinates.Length ||
            count != terrain.Colors.Length || count != terrain.EdgeFlags.Length)
            throw new FormatException("Terrain dimensions and vertex attributes do not match.");
        if (terrain.IsCurve && (terrain.Width < 3 || terrain.Height < 3 || terrain.Width % 2 == 0 || terrain.Height % 2 == 0))
            throw new FormatException("Curves need odd control dimensions of at least three in each direction.");
        output.AppendLine("{").AppendLine(terrain.IsCurve ? " curve" : " mesh").AppendLine(" {");
        foreach (string directive in terrain.Directives)
            output.AppendLine(directive);
        output.Append("  ").AppendLine(Name(terrain.Material));
        output.Append("  ").AppendLine(Name(terrain.Lightmap));
        if (terrain.Smoothing is not null)
            output.Append("  smoothing ").AppendLine(Name(terrain.Smoothing));
        output.Append("  ").Append(terrain.Width).Append(' ').Append(terrain.Height).Append(' ')
            .Append(Number(terrain.LightmapSize)).Append(' ').Append(terrain.Subdivision).AppendLine();
        for (int column = 0; column < terrain.Width; column++)
        {
            output.AppendLine("  (");
            for (int row = 0; row < terrain.Height; row++)
            {
                int index = column * terrain.Height + row;
                Vector4 color = terrain.Colors[index];
                output.Append("   v ").Append(Position(terrain.Vertices[index]));
                if (color != Vector4.One)
                    output.Append(" c ").Append(ColorByte(color.Z)).Append(' ').Append(ColorByte(color.Y))
                        .Append(' ').Append(ColorByte(color.X)).Append(' ').Append(ColorByte(color.W));
                Vector2 texture = terrain.TextureCoordinates[index] * 1024;
                Vector2 lightmap = terrain.LightmapCoordinates[index] * 1024;
                output.Append(" t ").Append(Number(texture.X)).Append(' ').Append(Number(texture.Y))
                    .Append(' ').Append(Number(lightmap.X)).Append(' ').Append(Number(lightmap.Y));
                if (terrain.EdgeFlags[index] != 0)
                    output.Append(" f ").Append(terrain.EdgeFlags[index]);
                output.AppendLine();
            }
            output.AppendLine("  )");
        }
        output.AppendLine(" }").AppendLine("}");
    }

    private static string Position(Vector3 value) => $"{Number(value.X)} {Number(value.Y)} {Number(value.Z)}";

    private static string Number(float value)
    {
        if (!float.IsFinite(value))
            throw new FormatException("Map coordinates and projection values must be finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static int ColorByte(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new FormatException("Terrain colors must be between zero and one.");
        return (int)MathF.Round(value * 255);
    }

    private static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            throw new FormatException("A material name cannot be empty or contain null characters.");
        return value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("/*", StringComparison.Ordinal) ||
               value.Any(character => char.IsWhiteSpace(character) || "\"{}();".Contains(character))
            ? Quote(value) : value;
    }

    private static string Quote(string value)
    {
        if (value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
            throw new FormatException("Map strings cannot contain line breaks or null characters.");
        return '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
    }
}
