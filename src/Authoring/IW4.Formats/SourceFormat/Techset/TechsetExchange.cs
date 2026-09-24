using System.Text.Json;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Techset;

/// <summary>Exchanges the native PS3 technique-set fields as versioned source.</summary>
public sealed class TechsetExchange
{
    private const string Format = "iw4-ps3-techset";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IReadOnlyList<string> Unlink(string sourceDirectory, MaterialTechniqueSetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "Techset");
        int count = (int)MaterialTechniqueType.Count;
        if (asset.TechniqueSlots.Count != count)
            throw new InvalidDataException($"Techset '{name}' requires {count} technique slots.");

        var names = new string?[count];
        for (int index = 0; index < count; index++)
        {
            MaterialTechniqueSlot slot = asset.TechniqueSlots[index];
            if (slot is null || slot.Index != index)
                throw new InvalidDataException($"Techset '{name}' slot {index} has the wrong type.");
            if (slot.Technique is null)
            {
                if (slot.Pointer.Type != IW4.Game.Pointers.PointerType.Null)
                    throw new InvalidDataException($"Techset '{name}' slot {index} is unresolved.");
                continue;
            }
            names[index] = SourceOutput.NormalizeReferencedAssetName(
                slot.Technique.Name, $"Techset '{name}' slot {index}");
        }

        var document = new Document
        {
            Format = Format,
            Version = 1,
            Name = name,
            WorldVertexFormat = (byte)asset.WorldVertexFormat,
            Techniques = names
        };
        string json = JsonSerializer.Serialize(document, JsonOptions);
        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"techsets/{name}.techset.json", writer => writer.WriteLine(json))
        ]);
    }

    public MaterialTechniqueSetAsset Link(string sourceDirectory, string assetName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Techset");
        string path = NativeSourcePath.Resolve(
            sourceDirectory, "techsets", name, ".techset.json");
        using FileStream stream = File.OpenRead(path);
        Document document = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Techset '{name}' has an empty source document.");
        if (document.Format != Format || document.Version != 1 || document.Name != name)
            throw new InvalidDataException($"Techset '{name}' has an unsupported format, version, or name.");
        if (!Enum.IsDefined((MaterialWorldVertexFormat)document.WorldVertexFormat))
            throw new InvalidDataException($"Techset '{name}' has an invalid world vertex format.");
        int count = (int)MaterialTechniqueType.Count;
        if (document.Techniques is null || document.Techniques.Length != count)
            throw new InvalidDataException($"Techset '{name}' requires {count} technique slots.");

        var exchange = new TechniqueExchange();
        var techniques = new Dictionary<string, MaterialTechniqueAsset>(StringComparer.Ordinal);
        var slots = new MaterialTechniqueSlot[count];
        for (int index = 0; index < count; index++)
        {
            string? techniqueName = document.Techniques[index];
            MaterialTechniqueAsset? technique = null;
            if (techniqueName is not null)
            {
                string referencedName = SourceOutput.NormalizeOwnedAssetName(
                    techniqueName, $"Techset '{name}' slot {index} technique");
                if (!techniques.TryGetValue(referencedName, out technique))
                {
                    technique = exchange.Link(sourceDirectory, referencedName);
                    techniques.Add(referencedName, technique);
                }
            }
            slots[index] = new MaterialTechniqueSlot(
                (MaterialTechniqueType)index, default, technique);
        }
        return new MaterialTechniqueSetAsset
        {
            Name = name,
            WorldVertexFormat = (MaterialWorldVertexFormat)document.WorldVertexFormat,
            TechniqueSlots = slots
        };
    }

    private sealed class Document
    {
        public Document() { }

        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string Name { get; init; }
        public required byte WorldVertexFormat { get; init; }
        public required string?[] Techniques { get; init; }
    }
}
