using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;

namespace IW4.AssetExchange.SourceFormat.Techset;

/// <summary>Writes an IW4 technique set in the native .techset source format.</summary>
public sealed class TechsetExchange
{
    private static readonly string[] TechniqueTypeNames =
    [
        "depth prepass",
        "build floatz",
        "build shadowmap depth",
        "build shadowmap color",
        "unlit",
        "emissive",
        "emissive dfog",
        "emissive shadow",
        "emissive shadow dfog",
        "lit",
        "lit dfog",
        "lit sun",
        "lit sun dfog",
        "lit sun shadow",
        "lit sun shadow dfog",
        "lit spot",
        "lit spot dfog",
        "lit spot shadow",
        "lit spot shadow dfog",
        "lit omni",
        "lit omni dfog",
        "lit omni shadow",
        "lit omni shadow dfog",
        "lit instanced",
        "lit instanced dfog",
        "lit instanced sun",
        "lit instanced sun dfog",
        "light spot",
        "light omni",
        "light spot shadow",
        "fakelight normal",
        "fakelight view",
        "sunlight preview",
        "case texture",
        "solid wireframe",
        "shaded wireframe",
        "debug bumpmap"
    ];

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        MaterialTechniqueSetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Techset");

        int expectedSlotCount = (int)MaterialTechniqueType.Count;
        if (TechniqueTypeNames.Length != expectedSlotCount)
        {
            throw new InvalidOperationException(
                "The Techset source-name table does not match the PS3 IW4 technique-slot layout.");
        }
        if (asset.TechniqueSlots.Count != expectedSlotCount)
        {
            throw new InvalidDataException(
                $"Techset '{assetName}' requires {expectedSlotCount} materialized technique slots but has {asset.TechniqueSlots.Count}.");
        }

        var techniqueNames = new string?[expectedSlotCount];
        for (int index = 0; index < expectedSlotCount; index++)
        {
            MaterialTechniqueSlot slot = asset.TechniqueSlots[index];
            var expectedType = (MaterialTechniqueType)index;
            if (slot.Type != expectedType)
            {
                throw new InvalidDataException(
                    $"Techset '{assetName}' slot {index} is {slot.Type} instead of {expectedType}.");
            }

            if (slot.Technique is null)
            {
                if (slot.Pointer.Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"Techset '{assetName}' slot '{TechniqueTypeNames[index]}' has an unresolved technique pointer {slot.Pointer}.");
                }

                continue;
            }

            techniqueNames[index] = SourceOutput.NormalizeReferencedAssetName(
                slot.Technique.Name,
                $"Techset '{assetName}' slot '{TechniqueTypeNames[index]}' technique");
        }

        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"techsets/{assetName}.techset", writer =>
                WriteSource(writer, techniqueNames))
        ]);
    }

    private static void WriteSource(
        TextWriter writer,
        IReadOnlyList<string?> techniqueNames)
    {
        var writtenSlots = new bool[techniqueNames.Count];
        bool wroteTechnique = false;
        for (int index = 0; index < techniqueNames.Count; index++)
        {
            string? techniqueName = techniqueNames[index];
            if (techniqueName is null || writtenSlots[index])
                continue;

            if (wroteTechnique)
                writer.WriteLine();

            for (int matchingIndex = index;
                 matchingIndex < techniqueNames.Count;
                 matchingIndex++)
            {
                if (!string.Equals(
                        techniqueNames[matchingIndex],
                        techniqueName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                writtenSlots[matchingIndex] = true;
                writer.Write('"');
                writer.Write(TechniqueTypeNames[matchingIndex]);
                writer.WriteLine("\":");
            }

            writer.Write("  ");
            writer.Write(techniqueName);
            writer.WriteLine(';');
            wroteTechnique = true;
        }
    }
}
