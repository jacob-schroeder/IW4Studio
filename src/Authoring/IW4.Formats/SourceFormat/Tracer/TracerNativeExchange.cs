using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Tracer;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Tracer;

/// <summary>Preserves native tracer values and its named material for disk linking.</summary>
public sealed class TracerNativeExchange
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public IReadOnlyList<string> Unlink(string sourceDirectory, TracerDefAsset tracer)
    {
        ArgumentNullException.ThrowIfNull(tracer);
        string name = SourceOutput.NormalizeOwnedAssetName(tracer.Name, "Tracer");
        Validate(tracer, name);
        string? material = tracer.Material is null ? null :
            SourceOutput.NormalizeOwnedAssetName(tracer.Material.Info.Name?.TrimStart(','), "Tracer material");
        if (tracer.MaterialPointer.Raw != 0 && material is null)
            throw new InvalidDataException($"Tracer '{name}' has an unresolved material.");
        var source = new NativeTracer
        {
            Format = "iw4-ps3-tracer", Version = 1, Name = name, Material = material,
            DrawInterval = tracer.DrawInterval, Speed = tracer.Speed,
            BeamLength = tracer.BeamLength, BeamWidth = tracer.BeamWidth,
            ScrewRadius = tracer.ScrewRadius, ScrewDistance = tracer.ScrewDistance,
            Colors = tracer.Colors.ToArray()
        };
        string json = JsonSerializer.Serialize(source, Options);
        return new SourceOutput(sourceDirectory).WriteTextBatch(
            [($"tracer_native/{name}.json", writer => writer.WriteLine(json))]);
    }

    public TracerDefAsset Link(string sourceDirectory, string tracerName,
        Func<string, MaterialAsset> resolveMaterial)
    {
        ArgumentNullException.ThrowIfNull(resolveMaterial);
        string name = SourceOutput.NormalizeOwnedAssetName(tracerName, "Tracer");
        string path = NativeSourcePath.Resolve(sourceDirectory, "tracer_native", name, ".json");
        NativeTracer source = JsonSerializer.Deserialize<NativeTracer>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Tracer '{name}' has an empty native source.");
        if (source.Format != "iw4-ps3-tracer" || source.Version != 1 || source.Name != name)
            throw new InvalidDataException($"Tracer '{name}' has invalid native source metadata.");
        MaterialAsset? material = source.Material is null ? null : resolveMaterial(
            SourceOutput.NormalizeOwnedAssetName(source.Material, "Tracer material"));
        var tracer = new TracerDefAsset
        {
            Name = name, Material = material,
            MaterialPointer = material is null ? default :
                new XPointer<MaterialAsset>(-1, XPointerResolutionMode.AliasCell),
            DrawInterval = source.DrawInterval, Speed = source.Speed,
            BeamLength = source.BeamLength, BeamWidth = source.BeamWidth,
            ScrewRadius = source.ScrewRadius, ScrewDistance = source.ScrewDistance,
            Colors = source.Colors
        };
        Validate(tracer, name);
        return tracer;
    }

    private static void Validate(TracerDefAsset tracer, string name)
    {
        if (tracer.Colors is null || tracer.Colors.Count != TracerDefAsset.ColorCount ||
            !float.IsFinite(tracer.Speed) || !float.IsFinite(tracer.BeamLength) ||
            !float.IsFinite(tracer.BeamWidth) || !float.IsFinite(tracer.ScrewRadius) ||
            !float.IsFinite(tracer.ScrewDistance) || tracer.Colors.Any(color =>
                !float.IsFinite(color.Red) || !float.IsFinite(color.Green) ||
                !float.IsFinite(color.Blue) || !float.IsFinite(color.Alpha)))
            throw new InvalidDataException($"Tracer '{name}' requires finite native values and five color rows.");
    }

    private sealed record NativeTracer
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string Name { get; init; }
        public required string? Material { get; init; }
        public required uint DrawInterval { get; init; }
        public required float Speed { get; init; }
        public required float BeamLength { get; init; }
        public required float BeamWidth { get; init; }
        public required float ScrewRadius { get; init; }
        public required float ScrewDistance { get; init; }
        public required TracerColor[] Colors { get; init; }
    }
}
