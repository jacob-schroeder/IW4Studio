using System.Globalization;
using System.Numerics;
using System.Text;
using IW4.Formats.D3dbsp;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Compilation;

internal static class MapStaticModelCompiler
{
    internal static D3dbspFile Append(MapDocument document, D3dbspFile compiled)
    {
        MapEntity[] models = document.Entities.Where(entity => entity.ClassName == "misc_model").ToArray();
        if (models.Length == 0) return compiled;

        var output = new MapDocument();
        output.Header.Clear();
        foreach (var properties in compiled.GetEntities())
        {
            var entity = new MapEntity();
            foreach (var property in properties) entity.Properties.Add(property.Key, property.Value);
            output.Entities.Add(entity);
        }
        foreach (MapEntity source in models)
        {
            if (!XModelGeometry.IsModel(source) || string.IsNullOrWhiteSpace(source.Properties["model"]))
                throw new InvalidDataException("A misc_model needs a named XModel asset.");
            string name = source.Properties["model"];
            if (source.Brushes.Count != 0 || source.Terrains.Count != 0 || source.PreservedPrimitives.Count != 0)
                throw new NotSupportedException($"Static model '{name}' cannot contain map primitives.");
            foreach (string key in source.Properties.Keys)
                if (key is not ("classname" or "model" or "origin" or "angles" or "angle" or
                    "modelscale" or "modelscale_vec" or "spawnflags" or "gndLt"))
                    throw new NotSupportedException($"Static model '{name}' property '{key}' is not supported by compilation.");
            _ = CastsShadow(source);

            Vector3 scale = XModelGeometry.Scale(source);
            if (scale.X != scale.Y || scale.X != scale.Z)
                throw new NotSupportedException($"Static model '{name}' requires the same positive scale on all three axes.");
            Vector3 angles = EntityOrientation.Read(source);
            var normalized = new MapEntity();
            foreach (var property in source.Properties) normalized.Properties.Add(property.Key, property.Value);
            normalized.Properties.Remove("angle");
            normalized.Properties.Remove("modelscale_vec");
            normalized.Properties["angles"] = FormattableString.Invariant($"{angles.X:G9} {angles.Y:G9} {angles.Z:G9}");
            normalized.Properties["modelscale"] = scale.X.ToString("G9", CultureInfo.InvariantCulture);
            output.Entities.Add(normalized);
        }

        // The native linker owns static render/collision construction from these
        // placements and the full XModels supplied by the selected fastfiles.
        string text = MapWriter.Serialize(output);
        byte[] entities = Encoding.GetEncoding(Encoding.Latin1.CodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(text + '\0');
        return D3dbspFile.Create(compiled.Lumps.Select(lump =>
            (lump.Type, lump.Type == D3dbspLumpType.Entities ? entities : lump.Data)).ToArray());
    }

    internal static bool CastsShadow(MapEntity source)
    {
        if (!source.Properties.TryGetValue("spawnflags", out string? flags)) return true;
        if (!int.TryParse(flags, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value is not (0 or 2))
            throw new NotSupportedException($"Static model '{source.Properties.GetValueOrDefault("model")}' supports only spawnflags 0 or 2 (no cast shadow).");
        return value == 0;
    }
}
