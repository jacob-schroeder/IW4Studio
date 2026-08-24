using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Assets.Assets.XModel;
using IW4.AssetExchange.XModel;

namespace IW4.AssetExchange.SourceFormat.XModel;

/// <summary>
/// Writes a materialized IW4 XModel to the OpenAssetTools source layout.
/// XMODEL_EXPORT retains the model skeleton, so the redundant rootBoneName
/// metadata field is intentionally omitted.
/// </summary>
public sealed class XModelExchange
{
    private const string Schema =
        "http://openassettools.dev/schema/xmodel.v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IndentCharacter = ' ',
        IndentSize = 4,
        WriteIndented = true
    };

    /// <summary>
    /// Writes each loaded LOD followed by the XModel metadata JSON and returns
    /// the resulting full paths in write order.
    /// </summary>
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        XModelAsset asset,
        IReadOnlyDictionary<int, XModelExportDocument> loadedLods)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(loadedLods);

        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "XModel");
        int lodCount = ValidateLoadedLods(asset, loadedLods);
        string metadata = SerializeMetadata(asset, assetName, lodCount);
        var output = new SourceOutput(sourceDirectory);
        var files = new List<(
            string RelativePath,
            Action<TextWriter> Write)>(lodCount + 1);

        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            XModelExportDocument document = loadedLods[lodIndex];
            string relativePath = ModelExportPath(assetName, lodIndex);
            files.Add((
                relativePath,
                writer => XModelExportWriter.Write(writer, document)));
        }

        files.Add((
            $"xmodel/{assetName}.json",
            writer => writer.WriteLine(metadata)));
        return output.WriteTextBatch(files);
    }

    private static int ValidateLoadedLods(
        XModelAsset asset,
        IReadOnlyDictionary<int, XModelExportDocument> loadedLods)
    {
        int lodCount = asset.NumLods == 0
            ? asset.Lods.Count
            : asset.NumLods;
        if (lodCount is < 1 or > 4 || asset.Lods.Count < lodCount)
        {
            throw new InvalidDataException(
                $"XModel '{asset.Name}' has an invalid active LOD count of {lodCount}.");
        }

        if (loadedLods.Count != lodCount)
        {
            throw new InvalidDataException(
                $"XModel '{asset.Name}' requires exactly {lodCount} loaded LOD documents, " +
                $"but {loadedLods.Count} were supplied.");
        }

        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            if (!loadedLods.TryGetValue(lodIndex, out XModelExportDocument? document) ||
                document is null)
            {
                throw new InvalidDataException(
                    $"XModel '{asset.Name}' has no export document for loaded LOD {lodIndex}.");
            }

            XModelLodInfo lod = asset.Lods[lodIndex] ??
                throw new InvalidDataException(
                    $"XModel '{asset.Name}' has no native LOD row at index {lodIndex}.");
            if (lod.ModelSurfs is null)
            {
                throw new InvalidDataException(
                    $"XModel '{asset.Name}' LOD {lodIndex} has no loaded XModelSurfs asset.");
            }

            SourceOutput.NormalizeReferencedAssetName(
                lod.ModelSurfs.Name,
                $"XModel '{asset.Name}' LOD {lodIndex} XModelSurfs");
            if (!float.IsFinite(lod.Dist))
            {
                throw new InvalidDataException(
                    $"XModel '{asset.Name}' LOD {lodIndex} has a non-finite distance.");
            }
        }

        if (asset.CollLod != byte.MaxValue && asset.CollLod >= lodCount)
        {
            throw new InvalidDataException(
                $"XModel '{asset.Name}' collision LOD {asset.CollLod} is not an active LOD.");
        }

        return lodCount;
    }

    private static string SerializeMetadata(
        XModelAsset asset,
        string assetName,
        int lodCount)
    {
        var lods = new XModelSourceLod[lodCount];
        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            lods[lodIndex] = new XModelSourceLod(
                ModelExportPath(assetName, lodIndex),
                asset.Lods[lodIndex].Dist);
        }

        var metadata = new XModelSourceMetadata(
            Schema,
            "xmodel",
            2,
            "iw4",
            GetModelType(asset, lodCount),
            lods,
            asset.CollLod == byte.MaxValue ? null : asset.CollLod,
            OptionalReferencedAssetName(asset.PhysPreset?.Name, "PhysPreset"),
            OptionalReferencedAssetName(asset.PhysCollmap?.Name, "PhysCollmap"),
            (uint)asset.Flags);

        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static string GetModelType(XModelAsset asset, int lodCount)
    {
        if (!IsAnimated(asset, lodCount))
            return "rigid";

        int partCount = asset.NumBones - asset.NumRootBones;
        if (partCount < 0)
        {
            throw new InvalidDataException(
                $"XModel '{asset.Name}' has more root bones than bones.");
        }

        int transCount = checked(partCount * 3);
        bool hasNulledTrans;
        if (asset.Trans.Count == 0)
        {
            hasNulledTrans = true;
        }
        else
        {
            if (asset.Trans.Count < transCount)
            {
                throw new InvalidDataException(
                    $"XModel '{asset.Name}' has fewer translation values than its bone counts require.");
            }

            hasNulledTrans = true;
            for (int index = 0; index < transCount; index++)
                hasNulledTrans &= asset.Trans[index] == 0f;
        }

        bool hasBoneInfoTranslation = asset.BoneInfo.Any(info =>
            info.Bounds.MidPoint.X != 0f ||
            info.Bounds.MidPoint.Y != 0f ||
            info.Bounds.MidPoint.Z != 0f);
        return hasNulledTrans && hasBoneInfoTranslation
            ? "viewhands"
            : "animated";
    }

    private static bool IsAnimated(XModelAsset asset, int lodCount)
    {
        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            XModelSurfsAsset modelSurfs = asset.Lods[lodIndex].ModelSurfs!;
            foreach (XSurface surface in modelSurfs.Surfaces)
            {
                XSurfaceVertexInfo vertexInfo = surface.VertexInfo;
                if (vertexInfo.VertsBlendPointer.Value != 0 ||
                    vertexInfo.VertsBlend.Count != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? OptionalReferencedAssetName(
        string? name,
        string field)
    {
        if (name is null)
            return null;

        return SourceOutput.NormalizeReferencedAssetName(name, field);
    }

    private static string ModelExportPath(string assetName, int lodIndex) =>
        $"model_export/{assetName}_lod{lodIndex}.xmodel_export";

    private sealed record XModelSourceLod(
        [property: JsonPropertyName("file")]
        string File,
        [property: JsonPropertyName("distance")]
        float Distance);

    private sealed record XModelSourceMetadata(
        [property: JsonPropertyName("$schema"), JsonPropertyOrder(0)]
        string Schema,
        [property: JsonPropertyName("_type"), JsonPropertyOrder(1)]
        string AssetType,
        [property: JsonPropertyName("_version"), JsonPropertyOrder(2)]
        int Version,
        [property: JsonPropertyName("_game"), JsonPropertyOrder(3)]
        string Game,
        [property: JsonPropertyName("type"), JsonPropertyOrder(4)]
        string Type,
        [property: JsonPropertyName("lods"), JsonPropertyOrder(5)]
        IReadOnlyList<XModelSourceLod> Lods,
        [property: JsonPropertyName("collLod"), JsonPropertyOrder(6)]
        byte? CollLod,
        [property: JsonPropertyName("physPreset"), JsonPropertyOrder(7)]
        string? PhysPreset,
        [property: JsonPropertyName("physCollmap"), JsonPropertyOrder(8)]
        string? PhysCollmap,
        [property: JsonPropertyName("flags"), JsonPropertyOrder(9)]
        uint Flags);
}
