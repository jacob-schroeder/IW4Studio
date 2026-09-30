using System.Globalization;
using System.Numerics;
using System.Text;
using IW4.Formats.D3dbsp;
using IW4.Formats.SourceFormat.Physics;
using IW4.Game.Assets.ComWorld;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Compilation;

internal static class MapModelCompiler
{
    internal static D3dbspFile Append(MapDocument document, D3dbspFile compiled,
        IReadOnlyDictionary<string, XModelSource> modelSources, IReadOnlyList<ComPrimaryLight> primaryLights,
        MapStageLighting stages)
    {
        MapEntity[] models = document.Entities.Where(entity =>
            entity.ClassName is "misc_model" or RuntimePhysicsAuthoring.ClassName).ToArray();
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
                throw new InvalidDataException($"A {source.ClassName} needs a named XModel asset.");
            string name = source.Properties["model"];
            if (source.Brushes.Count != 0 || source.Terrains.Count != 0 || source.PreservedPrimitives.Count != 0)
                throw new NotSupportedException($"Model '{name}' cannot contain map primitives.");
            if (source.ClassName == RuntimePhysicsAuthoring.ClassName)
                RuntimePhysicsAuthoring.Validate(source.Properties);
            else
            {
                foreach (string key in source.Properties.Keys)
                    if (key is not ("classname" or "model" or "origin" or "angles" or "angle" or
                        "modelscale" or "modelscale_vec" or "spawnflags" or "gndLt"))
                        throw new NotSupportedException($"Static model '{name}' property '{key}' is not supported by compilation.");
                _ = CastsShadow(source);
            }

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
            if (source.ClassName == "misc_model" && (stages.SunCount > 1 || primaryLights.Count > stages.SunCount + 1))
            {
                if (!modelSources.TryGetValue(name, out XModelSource? model))
                    throw new InvalidDataException($"Static model '{name}' is unavailable for primary-light assignment.");
                var (minimum, maximum) = XModelGeometry.Bounds(source, model);
                if (!BrushGeometry.IsFinite(minimum) || !BrushGeometry.IsFinite(maximum))
                    throw new InvalidDataException($"Static model '{name}' has no finite render bounds.");
                // Approximate the native lighting origin from the source model-bound center.
                var (localMinimum, localMaximum) = model.Bounds;
                Vector3 lightingOrigin = Vector3.Transform(localMinimum * 0.5f + localMaximum * 0.5f,
                    XModelGeometry.Transform(source));
                if (!BrushGeometry.IsFinite(lightingOrigin))
                    throw new InvalidDataException($"Static model '{name}' has no finite lighting origin.");
                byte primaryIndex = stages.SunIndexAt(lightingOrigin);
                for (int index = stages.SunCount + 1; index < primaryLights.Count; index++)
                {
                    ComPrimaryLight light = primaryLights[index];
                    Vector3 origin = new(light.Origin.X, light.Origin.Y, light.Origin.Z);
                    if (Vector3.DistanceSquared(origin, Vector3.Clamp(origin, minimum, maximum)) >= light.Radius * light.Radius)
                        continue;
                    if (light.Type == GfxLightType.Spot &&
                        !PrimaryLocalLightProfile.SpotIntersectsBounds(origin,
                            -new Vector3(light.Dir.X, light.Dir.Y, light.Dir.Z), light.CosHalfFovExpanded,
                            minimum, maximum))
                        continue;
                    primaryIndex = checked((byte)index);
                    break;
                }
                string groundLighting = source.Properties.GetValueOrDefault("gndLt", "00000000");
                if (groundLighting.Length is not (8 or 10) || groundLighting.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidDataException($"Static model '{name}' gndLt must contain four or five hex bytes.");
                // The native placement codec stores its primary index in byte five.
                // Keeping the first four bytes zero leaves grid lighting enabled.
                normalized.Properties["gndLt"] = groundLighting[..8] + primaryIndex.ToString("X2", CultureInfo.InvariantCulture);
            }
            output.Entities.Add(normalized);
        }

        // The native linker builds static render/collision or dynamic definitions
        // from these placements and the resolved native XModels.
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
