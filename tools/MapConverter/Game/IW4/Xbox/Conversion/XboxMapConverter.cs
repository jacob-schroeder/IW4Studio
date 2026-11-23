using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.ComWorld;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.GameMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.Localize;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.StringTable;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XAnim;
using IW4.Game.Assets.XModel;
using IW4.Studio.Documents;
using MapConverter.Game.IW4.Xbox.Animations;
using MapConverter.Game.IW4.Xbox.Collision;
using MapConverter.Game.IW4.Xbox.FastFiles;
using MapConverter.Game.IW4.Xbox.Images;
using MapConverter.Game.IW4.Xbox.Materials;
using MapConverter.Game.IW4.Xbox.Models;
using MapConverter.Game.IW4.Xbox.Physics;
using MapConverter.Game.IW4.Xbox.World;
using static MapConverter.Game.IW4.Xbox.FastFiles.XboxWire;

namespace MapConverter.Game.IW4.Xbox.Conversion;

internal static class XboxMapConverter
{
    internal static int Convert(string source, string imageTable, string destination)
    {
        if (File.Exists(destination)) throw new IOException($"Output already exists: {destination}");
        var streams = XboxImageStream.Read(imageTable);
        using var workspace = new FastFileDocumentService().Open(new FastFileDocumentOpenRequest(source, Isolated.Instance));
        var definitions = workspace.LoadedZone.Context.AssetPool.Slots
            .Select(s => s.CanonicalAsset).OfType<BaseAsset>()
            .Where(a => a.SerializedAssetName is { Length: > 0 } n && !n.StartsWith(',')).ToArray();
        string Name(BaseAsset a) => a.SerializedAssetName ?? throw new InvalidDataException("Unnamed map asset.");
        var images = definitions.OfType<GfxImageAsset>().ToDictionary(Name, StringComparer.OrdinalIgnoreCase);
        var roots = new List<XboxAsset>();
        // The target material references supply native shaders. LODs own their model surface data.
        var handled = new HashSet<Type> { typeof(GfxImageAsset), typeof(MaterialTechniqueSetAsset),
            typeof(MaterialShaderAsset), typeof(XModelSurfsAsset), typeof(StringTableAsset) };
        void Add<T>(int type, Action<XboxZoneStream, T> write) where T : BaseAsset
        {
            handled.Add(typeof(T));
            foreach (var a in definitions.OfType<T>()) roots.Add(new(type, Name(a), z => write(z, a)));
        }

        // Material technique sets contain platform shader programs. A comma reference asks
        // the Xbox DB for the registered technique set with that name.
        var requiredSets = definitions.OfType<MaterialAsset>().Select(m => m.TechniqueSet?.Name)
            .OfType<string>().Select(XboxMaterialWriter.NativeTechniqueSetName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        foreach (string set in requiredSets)
            roots.Add(new(XboxAssetType.TechniqueSet, set, z => {
                var header = new byte[0x90];
                U32(header, 0, Inline);
                z.Write(header, 4, XboxBlock.Temp); z.String("," + set);
            }));
        foreach (var image in streams)
        {
            if (!images.Remove(image.Name, out var asset))
                throw new InvalidDataException($"Image '{image.Name}' is absent from the source map.");
            roots.Add(new(XboxAssetType.Image, image.Name, z => image.Write(z, asset)));
        }
        if (images.Count != 0) throw new InvalidDataException($"Missing Xenon image stream for '{images.Keys.First()}'.");
        Add<MaterialAsset>(XboxAssetType.Material, XboxMaterialWriter.Write);
        Add<PhysPresetAsset>(XboxAssetType.PhysPreset, XboxMapAssets.Preset);
        Add<PhysCollmapAsset>(XboxAssetType.PhysCollmap, XboxPhysCollmapWriter.Write);
        Add<XModelAsset>(XboxAssetType.XModel, XboxXModelWriter.Write);
        Add<XAnimPartsAsset>(XboxAssetType.XAnim, XboxXAnimWriter.Write);
        Add<LightDefAsset>(XboxAssetType.LightDef, (z, a) => {
            byte[] h = new byte[16];
            U32(h, 0, Inline);
            if (a.Image?.Name is string image) U32(h, 4, z.Asset(XboxAssetType.Image, image));
            h[8] = (byte)a.SamplerState; U32(h, 12, a.LmapLookupStart);
            z.Write(h, 4, XboxBlock.Temp); z.String(Name(a));
        });
        Add<MapEntsAsset>(XboxAssetType.MapEnts, XboxMapAssets.MapEnts);
        Add<ComWorldAsset>(XboxAssetType.ComWorld, XboxMapAssets.ComWorld);
        Add<GameWorldMpAsset>(XboxAssetType.GameWorldMp, XboxMapAssets.GameWorld);
        Add<FxWorldAsset>(XboxAssetType.FxWorld, XboxMapAssets.FxWorld);
        if (definitions.OfType<ClipMapAsset>().Count() != 1 || definitions.OfType<GfxWorldAsset>().Count() != 1)
            throw new InvalidDataException("Xbox multiplayer map conversion requires one collision map and one render world.");
        Add<ClipMapAsset>(XboxAssetType.ClipMap, XboxClipMapWriter.Write);
        Add<GfxWorldAsset>(XboxAssetType.GfxWorld, XboxGfxWorldWriter.Write);
        Add<RawFileAsset>(XboxAssetType.RawFile, XboxMapAssets.RawFile);
        Add<LocalizeAsset>(XboxAssetType.Localize, (z, a) => {
            var h = new byte[8]; if (a.Value != null) U32(h, 0, Inline); U32(h, 4, Inline);
            z.Write(h, 4, XboxBlock.Temp); if (a.Value is string v) z.String(v); z.String(Name(a));
        });
        foreach (var table in definitions.OfType<StringTableAsset>())
        {
            string name = Name(table);
            const string prefix = "mp/configstrings/configstrings_ps3_";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = "mp/configstrings/configstrings_360_" + name[prefix.Length..];
            roots.Add(new(XboxAssetType.StringTable, name, z => WriteStringTable(z, name, table)));
        }

        if (Array.Find(definitions, asset => !handled.Contains(asset.GetType())) is { } unsupported)
            throw new NotSupportedException($"Xbox map conversion does not support {unsupported.SerializedAssetType} '{Name(unsupported)}'.");
        var zone = new XboxZoneStream(roots, workspace.LoadedZone.XAssetList.ScriptStrings.Select(s => s.Value).ToArray());
        zone.Save(destination, streams);
        Console.WriteLine($"Wrote {destination} with {roots.Count} Xbox assets and {streams.Count} imagefile7.pak streams.");
        Console.WriteLine($"Requires {requiredSets.Length} native Xbox material technique sets: {string.Join(", ", requiredSets)}");
        return 0;
    }

    private static void WriteStringTable(XboxZoneStream zone, string name, StringTableAsset table)
    {
        byte[] header = new byte[16];
        U32(header, 0, Inline); I32(header, 4, table.ColumnCount); I32(header, 8, table.RowCount);
        U32(header, 12, Present(table.Cells.Count)); zone.Write(header, 4, XboxBlock.Temp);
        zone.String(name);
        var cells = new byte[checked(table.Cells.Count * 8)];
        for (int i = 0; i < table.Cells.Count; i++)
        {
            if (table.Cells[i].String != null) U32(cells, i * 8, Inline);
            I32(cells, i * 8 + 4, table.Cells[i].Hash);
        }
        if (cells.Length > 0) zone.Write(cells, 4);
        foreach (var cell in table.Cells) if (cell.String is string value) zone.String(value);
    }
}
