using System.Text;
using IW4.Formats.RawFile;
using IW4.Formats.SourceFormat.Menu;
using IW4.Formats.SourceFormat.PhysPreset;
using IW4.Formats.SourceFormat.StringTable;
using IW4.Game.Assets;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.StringTable;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>Converts individual asset definitions to and from their editable source files.</summary>
public static class AssetSourceTransfer
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<XAssetType, string> SourceSuffixes = new()
    {
        [XAssetType.RawFile] = string.Empty,
        [XAssetType.StringTable] = ".csv",
        [XAssetType.PhysPreset] = ".physic.json",
        [XAssetType.Menu] = ".menu",
        [XAssetType.MenuFile] = ".menu"
    };

    public static IReadOnlyList<XAssetType> SupportedAssetTypes { get; } =
        Array.AsReadOnly(SourceSuffixes.Keys.ToArray());

    public static bool CanImport(XAssetType type) => SourceSuffixes.ContainsKey(type);

    public static bool Supports(XAssetType type) =>
        CanImport(type) && type is not (XAssetType.Menu or XAssetType.MenuFile);

    public static string GetFileSuffix(XAssetType type) =>
        SourceSuffixes.TryGetValue(type, out string? suffix)
            ? suffix
            : throw UnsupportedType(type);

    public static string SuggestExportFileName(BaseAsset definition)
    {
        string name = definition.SerializedAssetName
            ?? throw new InvalidDataException("The asset has no source name.");
        string fileName = Path.GetFileName(name.Replace('\\', '/'));
        string suffix = GetFileSuffix(definition.SerializedAssetType);
        return definition.SerializedAssetType != XAssetType.PhysPreset &&
            fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + suffix;
    }

    public static string SuggestImportAssetName(XAssetType type, string fileName)
    {
        string suffix = GetFileSuffix(type);
        return type is XAssetType.PhysPreset or XAssetType.Menu &&
            fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : fileName;
    }

    public static BaseAsset Read(Stream source, XAssetType type, string assetName, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        switch (type)
        {
            case XAssetType.RawFile:
                using (var content = new MemoryStream())
                {
                    source.CopyTo(content);
                    var draft = new RawFileDraft(new RawFileAsset { Name = assetName });
                    draft.ReplaceCanonicalContent(content.GetBuffer().AsSpan(0, checked((int)content.Length)));
                    return draft.ToAsset();
                }
            case XAssetType.StringTable:
                using (var reader = new StreamReader(source, Utf8, leaveOpen: true))
                    return new StringTableExchange().ReadCsv(reader, assetName, allowEmpty: true);
            case XAssetType.PhysPreset:
                return new PhysPresetExchange().ReadNative(source, assetName);
            case XAssetType.Menu:
            case XAssetType.MenuFile:
                using (var reader = new StreamReader(source, Utf8, leaveOpen: true))
                    return MenuExchange.ReadSource(reader, type, assetName, sourcePath);
            default:
                throw UnsupportedType(type);
        }
    }

    public static byte[] Write(BaseAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        switch (definition)
        {
            case RawFileAsset rawFile:
                return RawFileContentCodec.DecodeStrictSerializedContent(
                    rawFile.Name ?? throw new InvalidDataException("The RawFile has no name."),
                    rawFile);
            case StringTableAsset stringTable:
                using (var output = new MemoryStream())
                {
                    using (var writer = new StreamWriter(output, Utf8, leaveOpen: true) { NewLine = "\n" })
                        new StringTableExchange().WriteCsv(writer, stringTable);
                    return output.ToArray();
                }
            case PhysPresetAsset physPreset:
                using (var output = new MemoryStream())
                {
                    new PhysPresetExchange().WriteNative(output, physPreset);
                    return output.ToArray();
                }
            default:
                throw UnsupportedType(definition.SerializedAssetType);
        }
    }

    private static NotSupportedException UnsupportedType(XAssetType type) =>
        new($"Source transfer is unavailable for {type} assets.");
}
