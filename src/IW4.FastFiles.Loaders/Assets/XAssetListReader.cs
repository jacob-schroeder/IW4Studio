using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using IW4.Runtime.Strings;

namespace IW4.FastFiles.Loaders.Assets;

public sealed class XAssetListReader
{
    private const int XAssetListSize = 0x10;
    private const int XAssetSize = 0x08;

    public XAssetListSnapshot Read(FastFileCursor cursor, DbLoadContext context)
    {
        int rootOffset = cursor.Offset;
        byte[] rootBytes = cursor.ReadBytes(XAssetListSize);

        // XAssetList is a separate loader object. Its serialized root is not
        // retained in TEMP; only child payloads are materialized through the
        // block streams.
        var rootCursor = new FastFileCursor(rootBytes);

        int scriptStringCount = rootCursor.ReadInt32();
        XPointerReference scriptStringsReference = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
        int assetCount = rootCursor.ReadInt32();
        XPointerReference assetsReference = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);

        if (rootCursor.Offset != XAssetListSize)
            throw new InvalidDataException($"XAssetList root consumed 0x{rootCursor.Offset:X} bytes instead of 0x{XAssetListSize:X}.");

        context.Blocks.Push(XFileBlockType.LARGE);
        IReadOnlyList<XScriptStringEntry> scriptStrings;
        IReadOnlyList<XAssetListEntrySnapshot> assets;

        try
        {
            scriptStrings = !context.PointerReader.HasInlinePayload(scriptStringsReference)
                ? ValidateSkippedScriptStringArray(scriptStringsReference, scriptStringCount, context)
                : ReadScriptStrings(cursor, scriptStringsReference, scriptStringCount, context);
            if (scriptStrings.Count != scriptStringCount)
            {
                throw new InvalidDataException(
                    $"XAssetList declares {scriptStringCount} script string(s), but " +
                    $"{scriptStrings.Count} were materialized.");
            }

            context.ZoneScriptStrings.Initialize(scriptStrings);

            assets = !context.PointerReader.HasInlinePayload(assetsReference)
                ? ValidateSkippedAssetArray(assetsReference, assetCount, context)
                : ReadAssets(cursor, assetsReference, assetCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        var snapshot = new XAssetListSnapshot(
            SerializedOffset: rootOffset,
            ScriptStringCount: scriptStringCount,
            ScriptStringsPointer: scriptStringsReference.AsPointer<XPointer<string>[]>(),
            ScriptStrings: scriptStrings,
            AssetCount: assetCount,
            AssetsPointer: assetsReference.AsPointer<XAsset[]>(),
            Assets: assets);

        return snapshot;
    }

    private static IReadOnlyList<XScriptStringEntry> ReadScriptStrings(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative script string count {count}.");

        context.PointerReader.BeginInlinePayload(pointer, alignment: 4);
        int pointerTableSourceOffset = cursor.Offset;
        byte[] pointerTable = context.Blocks.Load(cursor, checked(count * sizeof(int)), out XBlockAddress pointerTableAddress);
        var tableCursor = new FastFileCursor(pointerTable, pointerTableAddress);

        var pointerOffsets = new int[count];
        var pointers = new XPointerReference[count];

        for (int i = 0; i < count; i++)
        {
            pointerOffsets[i] = pointerTableSourceOffset + i * sizeof(int);
            pointers[i] = context.PointerReader.ReadCell(tableCursor, XPointerOffsetMode.Direct);
        }

        var entries = new XScriptStringEntry[count];
        for (int i = 0; i < entries.Length; i++)
        {
            string? value = context.PointerReader.LoadXString(cursor, pointers[i]);
            XBlockAddress pointerCell = pointers[i].CellAddress
                ?? throw new InvalidDataException($"Script string pointer {i} has no destination cell address.");

            // Resolve each XString, intern it in the process-wide SL table,
            // and overwrite the pointer-table destination cell with the
            // returned scr_string_t handle.
            ScriptStringHandle runtimeHandle = value is null
                ? ScriptStringHandle.Null
                : context.InternZoneString(value, ScriptStringUser.XZone).Handle;
            context.Blocks.WriteInt32(pointerCell, runtimeHandle.Value);

            entries[i] = new XScriptStringEntry(
                i,
                pointerOffsets[i],
                pointerCell,
                pointers[i].AsPointer<string>(),
                value,
                runtimeHandle);
        }

        return entries;
    }

    private static IReadOnlyList<XAssetListEntrySnapshot> ReadAssets(
        FastFileCursor cursor,
        XPointerReference assetsPointer,
        int count,
        DbLoadContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative asset count {count}.");

        context.PointerReader.BeginInlinePayload(assetsPointer, alignment: 4);
        int assetTableSourceOffset = cursor.Offset;
        byte[] assetTable = context.Blocks.Load(cursor, checked(count * XAssetSize), out XBlockAddress assetTableAddress);
        var tableCursor = new FastFileCursor(assetTable, assetTableAddress);

        var assets = new XAssetListEntrySnapshot[count];
        for (int i = 0; i < assets.Length; i++)
        {
            int offset = assetTableSourceOffset + i * XAssetSize;
            int rowStart = tableCursor.Offset;
            var type = (XAssetType)tableCursor.ReadInt32();
            XAssetHeaderKind headerKind;
            XPointerReference pointer;
            if (XAssetTopLevelDispatch.Classify(type) == XAssetTopLevelDispatchKind.NativeNoOp)
            {
                // This enum value has no XAssetType dispatch case. Preserve
                // its opaque header word without treating it as a packed
                // block or alias pointer.
                int cellOffset = tableCursor.Offset;
                pointer = XPointerReference.FromRaw(
                    tableCursor.ReadInt32(),
                    XPointerResolutionMode.AliasCell,
                    tableCursor.AddressAt(cellOffset));
                headerKind = XAssetHeaderKind.Opaque;
            }
            else
            {
                pointer = context.PointerReader.ReadCell(tableCursor, XPointerOffsetMode.AliasCell);
                headerKind = XAssetHeaderKind.Pointer;
            }

            if (tableCursor.Offset - rowStart != XAssetSize)
                throw new InvalidDataException($"XAsset row consumed 0x{tableCursor.Offset - rowStart:X} bytes instead of 0x{XAssetSize:X}.");

            XBlockAddress assetPointerCell = pointer.CellAddress
                ?? throw new InvalidDataException($"XAsset row {i} pointer has no destination cell address.");
            assets[i] = new XAssetListEntrySnapshot(
                i,
                offset,
                assetPointerCell,
                type,
                pointer.AsPointer<BaseAsset>(),
                headerKind);
        }

        return assets;
    }

    private static IReadOnlyList<XScriptStringEntry> ValidateSkippedScriptStringArray(
        XPointerReference pointer,
        int count,
        DbLoadContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative script string count {count}.");

        context.PointerReader.ValidateOffsetPointerRange<XPointer<string>[]>(pointer, checked(count * sizeof(int)), "XAssetList.scriptStrings");
        return [];
    }

    private static IReadOnlyList<XAssetListEntrySnapshot> ValidateSkippedAssetArray(
        XPointerReference pointer,
        int count,
        DbLoadContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative asset count {count}.");

        context.PointerReader.ValidateOffsetPointerRange<XAsset[]>(pointer, checked(count * XAssetSize), "XAsset[]");
        return [];
    }
}
