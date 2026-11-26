using MapConverter.CommandLine;
using MapConverter.Game.IW3.PC.Conversion;
using MapConverter.Game.IW3.PS3.Conversion;

namespace MapConverter;

/// <summary>A completed IW3 to IW4 map conversion and its user-facing report.</summary>
public sealed record MapConversionReport(
    IReadOnlyList<string> OutputLines,
    IReadOnlyList<string> WarningLines);

/// <summary>Runs the existing IW3 PC or PS3 conversion in the calling process.</summary>
public static class MapConversion
{
    public static async Task<MapConversionReport> ConvertAsync(
        MapConverterOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Game != SourceGame.Iw3 ||
            options.Platform is not (SourcePlatform.Pc or SourcePlatform.Ps3))
            throw new NotSupportedException("Map conversion supports only IW3 PC and PS3 input.");
        var output = new List<string>();
        var warnings = new List<string>();

        void AddPath(string name, string path)
        {
            output.Add($"{name}: {path}");
        }

        if (options.Platform == SourcePlatform.Ps3)
        {
            var converted = Iw3Ps3MapConverter.Convert(options, cancellationToken);
            AddPath("map", converted.Map);
            AddPath("load", converted.Load);
            AddPath("imagefile", converted.Images);
            AddPath("soundfile", converted.Sounds);
            AddPath("ui", converted.Ui);
            output.Add($"assets: {converted.AssetCount}; native PS3 shader programs retained");
            output.Add("omitted: source impact table; native IW4 impact effects retained");
            if (converted.DynamicShadowMaskDisabled)
                warnings.Add("warning: COD4 dynamic-shadow visibility masks use neutral white; sun-shadow-map bindings are retained");
            return new MapConversionReport(output, warnings);
        }

        if (options.WorldTemplatePath is not null)
        {
            var world = await Iw3PcMapConverter.ConvertWorldOnlyAsync(options, cancellationToken).ConfigureAwait(false);
            AddPath("map", world.MapPath);
            if (world.LoadPath is not null)
                AddPath("load", world.LoadPath);
            if (world.ImagePath is not null)
                AddPath("imagefile", world.ImagePath);
            output.Add(world.ImagePath is null
                ? "conversion: fullbright world-first with native default materials"
                : $"conversion: world-first with source lighting and {world.TexturedMaterialCount} textured materials");
            output.Add("bootstrap: us_army/opforce_airborne factions and Shipment utility models");
            output.Add(world.ImagePath is null
                ? "omitted: source props, dynamic entities, custom scripts/FX, converted shaders"
                : "omitted: movable dynamic-entity physics, unsupported custom scripts/FX, converted shaders");
            if (world.DamageFxCount != 0)
                output.Add($"damage FX: {world.DamageFxCount} native replacements; paper props retain source placement/health but are stationary");
            if (world.DefaultMaterialNames.Count != 0)
                warnings.Add("warning: native material fallbacks used for " + string.Join(", ", world.DefaultMaterialNames));
            if (world.FallbackImageNames.Count != 0)
                warnings.Add("warning: fallback image payloads used for " + string.Join(", ", world.FallbackImageNames));
            return new MapConversionReport(output, warnings);
        }

        Iw3PcMapConversionResult result = await Iw3PcMapConverter.ConvertAsync(options, cancellationToken).ConfigureAwait(false);
        AddPath("map", result.MapFastFilePath);
        AddPath("load", result.LoadFastFilePath);
        if (result.ImageFilePath is not null)
            AddPath("imagefile", result.ImageFilePath);
        AddPath("soundfile", result.SoundFilePath);
        AddPath("ui", result.UiFastFilePath);
        if (result.SourceProvenancePath is not null)
            AddPath("source-provenance", result.SourceProvenancePath);
        output.Add($"extraction-backend: {result.BackendDescription}");
        output.Add($"assets: {result.ImageCount} owned images, " +
            $"{result.ExternalImageCount} external images, " +
            $"{result.TechniqueSetCount} technique sets, " +
            $"{result.ShaderCount} shaders, {result.MaterialCount} materials, " +
            $"{result.XModelCount} converted xmodels, " +
            $"{result.BootstrapXModelCount} bootstrapped xmodels, " +
            $"{result.ExternalXModelCount} external xmodels, " +
            $"{result.RawFileCount} rawfiles");
        output.Add(result.BootstrapXModelCount == 0
            ? "target-bootstrap-data: none; shaders were lowered from IW3 Direct3D bytecode"
            : $"target-bootstrap-data: bundled gameplay assets ({result.BootstrapXModelCount} scoped xmodel closure); " +
              "map shaders were lowered from IW3 Direct3D bytecode");
        output.Add($"dynamic-entities: {result.DynamicEntityCount}");
        if (result.DestroyFxFallbackCount != 0)
            warnings.Add($"warning: dynamic-entity destroy FX omitted for " +
                $"{result.DestroyFxFallbackCount} definitions: " + string.Join(", ", result.DestroyFxFallbackNames));
        if (result.DestroyPiecesFallbackCount != 0)
            warnings.Add($"warning: IW3 destroy-pieces data has no IW4 field for " +
                $"{result.DestroyPiecesFallbackCount} definitions: " + string.Join(", ", result.DestroyPiecesFallbackNames));
        if (result.ElectricBoxFallbackApplied)
            warnings.Add($"warning: disabled initialization in IW3 helper " +
                $"'{Iw3PcMapConverter.ElectricBoxRawFileName}' because its constant loadfx dependency " +
                $"'{Iw3PcMapConverter.ElectricBoxFxName}' is not currently converted to IW4.");
        if (result.FallbackImageNames.Count != 0)
            warnings.Add("warning: neutral fallback images: " + string.Join(", ", result.FallbackImageNames));
        foreach (string sound in result.MissingSoundReferences)
            warnings.Add($"Warning: Sound '{sound}' was excluded from the build because its source or a required sound was missing.");
        return new MapConversionReport(output, warnings);
    }
}
