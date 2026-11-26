using MapConverter.CommandLine;
using MapConverter.Game.IW4.Xbox.Conversion;

if (args is ["to-xbox360", string source, string imageTable, string destination])
{
    try
    {
        return XboxMapConverter.Convert(source, imageTable, destination);
    }
    catch (Exception exception) when (exception is
        ArgumentException or
        InvalidDataException or
        NotSupportedException or
        IOException or
        UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

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
    MapConverter.MapConversionReport report = await MapConverter.MapConversion.ConvertAsync(parse.Options);
    foreach (string line in report.OutputLines)
        Console.WriteLine(line);
    foreach (string warning in report.WarningLines)
        Console.Error.WriteLine(warning);
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
