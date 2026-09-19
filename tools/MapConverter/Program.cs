using MapConverter.CommandLine;
using MapConverter.Game.IW3.PC.Conversion;

CommandLineParseResult parse = CommandLineParser.Parse(args);
if (parse.HelpRequested)
{
    Console.WriteLine(CommandLineParser.HelpText);
    return 0;
}
if (!parse.IsSuccess || parse.Options is null)
{
    Console.Error.WriteLine($"error: {parse.ErrorMessage}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineParser.HelpText);
    return 2;
}

try
{
    if (parse.Options.WorldTemplatePath is not null)
    {
        var world = await Iw3PcMapConverter.ConvertWorldOnlyAsync(parse.Options);
        Console.WriteLine($"map: {world.MapPath}");
        if (world.LoadPath is not null)
            Console.WriteLine($"load: {world.LoadPath}");
        if (world.ImagePath is not null)
            Console.WriteLine($"imagefile: {world.ImagePath}");
        Console.WriteLine(world.ImagePath is null
            ? "conversion: fullbright world-first with native default materials"
            : $"conversion: world-first with source lighting and {world.TexturedMaterialCount} textured materials");
        Console.WriteLine("bootstrap: us_army/opforce_airborne factions and Shipment utility models");
        Console.WriteLine(world.ImagePath is null
            ? "omitted: source props, dynamic entities, custom scripts/FX, converted shaders"
            : "omitted: movable dynamic-entity physics, unsupported custom scripts/FX, converted shaders");
        if (world.DamageFxCount != 0)
            Console.WriteLine($"damage FX: {world.DamageFxCount} native replacements; paper props retain source placement/health but are stationary");
        if (world.DefaultMaterialNames.Count != 0)
            Console.Error.WriteLine("warning: native material fallbacks used for " +
                string.Join(", ", world.DefaultMaterialNames));
        if (world.FallbackImageNames.Count != 0)
            Console.Error.WriteLine("warning: fallback image payloads used for " +
                string.Join(", ", world.FallbackImageNames));
        return 0;
    }

    Iw3PcMapConversionResult result =
        await Iw3PcMapConverter.ConvertAsync(parse.Options);
    Console.WriteLine($"map: {result.MapFastFilePath}");
    Console.WriteLine($"load: {result.LoadFastFilePath}");
    if (result.ImageFilePath is not null)
        Console.WriteLine($"imagefile: {result.ImageFilePath}");
    if (result.SourceProvenancePath is not null)
        Console.WriteLine($"source-provenance: {result.SourceProvenancePath}");
    Console.WriteLine($"extraction-backend: {result.BackendDescription}");
    Console.WriteLine(
        $"assets: {result.ImageCount} owned images, " +
        $"{result.ExternalImageCount} external images, " +
        $"{result.TechniqueSetCount} technique sets, " +
        $"{result.ShaderCount} shaders, {result.MaterialCount} materials, " +
        $"{result.XModelCount} converted xmodels, " +
        $"{result.BootstrapXModelCount} bootstrapped xmodels, " +
        $"{result.ExternalXModelCount} external xmodels, " +
        $"{result.RawFileCount} rawfiles");
    if (result.BootstrapXModelCount == 0)
    {
        Console.WriteLine(
            "target-bootstrap-data: none; shaders were lowered from IW3 Direct3D bytecode");
    }
    else
    {
        Console.WriteLine(
            $"target-bootstrap-data: bundled gameplay assets ({result.BootstrapXModelCount} scoped xmodel closure); " +
            "map shaders were lowered from IW3 Direct3D bytecode");
    }
    Console.WriteLine($"dynamic-entities: {result.DynamicEntityCount}");
    if (result.DestroyFxFallbackCount != 0)
    {
        Console.Error.WriteLine(
            $"warning: dynamic-entity destroy FX omitted for " +
            $"{result.DestroyFxFallbackCount} definitions: " +
            string.Join(", ", result.DestroyFxFallbackNames));
    }
    if (result.DestroyPiecesFallbackCount != 0)
    {
        Console.Error.WriteLine(
            $"warning: IW3 destroy-pieces data has no IW4 field for " +
            $"{result.DestroyPiecesFallbackCount} definitions: " +
            string.Join(", ", result.DestroyPiecesFallbackNames));
    }
    if (result.ElectricBoxFallbackApplied)
    {
        Console.Error.WriteLine(
            $"warning: disabled initialization in IW3 helper " +
            $"'{Iw3PcMapConverter.ElectricBoxRawFileName}' because its constant loadfx dependency " +
            $"'{Iw3PcMapConverter.ElectricBoxFxName}' is not currently converted to IW4.");
    }
    if (result.FallbackImageNames.Count != 0)
    {
        Console.Error.WriteLine(
            "warning: neutral fallback images: " +
            string.Join(", ", result.FallbackImageNames));
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("error: conversion was canceled.");
    return 130;
}
catch (Exception exception) when (exception is
    ArgumentException or
    FileNotFoundException or
    DirectoryNotFoundException or
    IOException or
    InvalidDataException or
    InvalidOperationException or
    NotSupportedException or
    OverflowException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}
