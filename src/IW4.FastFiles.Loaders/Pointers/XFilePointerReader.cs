using System.Collections.Concurrent;
using System.Reflection;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.Assets;
using IW4.Runtime.IO;
using IW4.Linker.Plans;

namespace IW4.FastFiles.Loaders.Pointers;

public sealed class XFilePointerReader
{
    private static readonly ConcurrentDictionary<Type, PointerTargetMetadata>
        TargetMetadata = new();
    private readonly DbStreamState _blocks;
    private readonly XAssetPool _assetPool;

    public XFilePointerReader(DbStreamState blocks, XAssetPool assetPool)
    {
        _blocks = blocks;
        _assetPool = assetPool;
    }

    public XPointerReference ReadCell(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None)
    {
        return ReadCellWithCaptureHandle(cursor, resolutionMode).Pointer;
    }

    internal XPointerRead ReadCellWithCaptureHandle(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None)
    {
        int cellOffset = cursor.Offset;
        int raw = cursor.ReadInt32();
        XPointerReference pointer = XPointerReference.FromRaw(
            raw,
            resolutionMode,
            cursor.AddressAt(cellOffset));
        XPointerReadHandle? captureHandle = RecordRead(cursor, cellOffset, pointer);
        ValidateOffsetPointer(pointer, null);
        return new XPointerRead(pointer, captureHandle);
    }

    public XPointerReference FromRaw(
        int raw,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None,
        XBlockAddress? cellAddress = null)
    {
        XPointerReference pointer = XPointerReference.FromRaw(raw, resolutionMode, cellAddress);
        ValidateOffsetPointer(pointer, null);
        return pointer;
    }

    public XPointer<T> FromRaw<T>(
        int raw,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None,
        XBlockAddress? cellAddress = null)
    {
        return FromRaw<T>(raw, resolutionMode, cellAddress, XPointerNullability.Unspecified);
    }

    public XPointer<T> FromRaw<T>(
        int raw,
        XPointerResolutionMode resolutionMode,
        XBlockAddress? cellAddress,
        XPointerNullability nullability)
    {
        ValidateNullability(nullability);
        XPointerReference pointer = XPointerReference.FromRaw(raw, resolutionMode, cellAddress);
        ValidateNullObjectPointer(pointer, typeof(T), nullability);
        ValidateOffsetPointer(pointer, typeof(T), nullability);
        return new XPointer<T>(raw, resolutionMode, cellAddress);
    }

    public XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None)
    {
        int cellOffset = cursor.Offset;
        int raw = cursor.ReadInt32();
        XPointerReference pointer = XPointerReference.FromRaw(raw, resolutionMode, cursor.AddressAt(cellOffset));
        RecordRead(cursor, cellOffset, pointer);
        ValidateNullObjectPointer(pointer, typeof(T), XPointerNullability.Unspecified);
        ValidateOffsetPointer(pointer, typeof(T), XPointerNullability.Unspecified);
        return pointer.AsPointer<T>();
    }

    public XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode,
        XPointerNullability nullability)
    {
        int cellOffset = cursor.Offset;
        int raw = cursor.ReadInt32();
        ValidateNullability(nullability);
        XPointerReference pointer = XPointerReference.FromRaw(raw, resolutionMode, cursor.AddressAt(cellOffset));
        RecordRead(cursor, cellOffset, pointer);
        ValidateNullObjectPointer(pointer, typeof(T), nullability);
        ValidateOffsetPointer(pointer, typeof(T), nullability);
        return pointer.AsPointer<T>();
    }

    /// <summary>
    /// Reads a typed pointer cell without validating an offset target that may
    /// not have been materialized yet. The caller must resolve the pointer
    /// through an operation that performs target validation before use.
    /// </summary>
    public XPointer<T> ReadDeferredPointer<T>(
        FastFileCursor cursor,
        XPointerResolutionMode resolutionMode = XPointerResolutionMode.None,
        XPointerNullability nullability = XPointerNullability.Unspecified)
    {
        ValidateNullability(nullability);

        int cellOffset = cursor.Offset;
        int raw = cursor.ReadInt32();
        XPointerReference pointer = XPointerReference.FromRaw(
            raw,
            resolutionMode,
            cursor.AddressAt(cellOffset));
        ValidateNullObjectPointer(pointer, typeof(T), nullability);
        RecordRead(cursor, cellOffset, pointer);
        return pointer.AsPointer<T>();
    }

    public bool HasInlinePayload(XPointerReference pointer)
    {
        return pointer.ConsumesSource;
    }

    public XBlockAddress PatchInlinePointerCell(
        XBlockAddress cellAddress,
        int raw,
        int alignment)
    {
        XPointerReference pointer = XPointerReference.FromRaw(raw);
        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Pointer cell {cellAddress} contains 0x{raw:X8}, not an inline/insert source sentinel.");

        if (alignment > 0)
            _blocks.AlignCurrent(alignment);

        XBlockAddress targetAddress = _blocks.CurrentAddress;
        _blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        return targetAddress;
    }

    public XBlockAddress PatchInlinePointerCell(
        XPointerReference pointer,
        int alignment)
    {
        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"Pointer 0x{pointer.Raw:X8} has no destination cell address to patch.");

        return PatchInlinePointerCell(cellAddress, pointer.Raw, alignment);
    }

    public XBlockAddress PatchInlinePointerCell<T>(
        XPointer<T> pointer,
        int alignment)
    {
        return PatchInlinePointerCell(pointer.Untyped, alignment);
    }

    /// <summary>
    /// Begins an inline payload after applying the requested destination
    /// alignment. A copied block pointer is patched in place; an external
    /// loader object has no XBlock destination cell and only resolves to the
    /// current block address.
    /// </summary>
    public XBlockAddress BeginInlinePayload(
        XPointerReference pointer,
        int alignment)
    {
        return BeginInlinePayload(pointer, alignment, null);
    }

    internal XBlockAddress BeginInlinePayload(
        XPointerRead pointer,
        int alignment) =>
        BeginInlinePayload(pointer.Pointer, alignment, pointer.CaptureHandle);

    private XBlockAddress BeginInlinePayload(
        XPointerReference pointer,
        int alignment,
        XPointerReadHandle? captureHandle)
    {
        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Pointer 0x{pointer.Raw:X8} is not an inline/insert source sentinel.");
        }

        if (alignment > 0)
            _blocks.AlignCurrent(alignment);

        XBlockAddress targetAddress = _blocks.CurrentAddress;
        if (pointer.CellAddress is { } cellAddress)
        {
            _blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        }
        else
        {
            _blocks.CaptureBridge?.BindInlineTarget(null, targetAddress, alignment, captureHandle);
        }

        return targetAddress;
    }

    public string? LoadXString(
        FastFileCursor cursor,
        XBlockAddress pointerCellAddress,
        XPointer<string> pointer,
        int alignment = 0)
    {
        return LoadXString(cursor, pointerCellAddress, pointer.Untyped, alignment);
    }

    public string? LoadXString(
        FastFileCursor cursor,
        XBlockAddress pointerCellAddress,
        XPointerReference pointer,
        int alignment = 0)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            ValidateOffsetPointer(pointer, typeof(string));
            return pointer.PackedAddress is { } packedAddress
                ? _blocks.ReadCString(packedAddress)
                : null;
        }

        if (!HasInlinePayload(pointer))
            return null;

        PatchInlinePointerCell(pointerCellAddress, pointer.Raw, alignment);
        return LoadXStringPayload(cursor);
    }

    public string? LoadXString(
        FastFileCursor cursor,
        XPointer<string> pointer,
        int alignment = 0)
    {
        return LoadXString(cursor, pointer.Untyped, alignment);
    }

    public string? LoadXString(
        FastFileCursor cursor,
        XPointerReference pointer,
        int alignment = 0)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            ValidateOffsetPointer(pointer, typeof(string));
            return pointer.PackedAddress is { } packedAddress
                ? _blocks.ReadCString(packedAddress)
                : null;
        }

        if (!HasInlinePayload(pointer))
            return null;

        PatchInlinePointerCell(pointer, alignment);
        return LoadXStringPayload(cursor);
    }

    internal string LoadXStringPayload(FastFileCursor cursor)
    {
        string value = _blocks.LoadCString(cursor, out _, out CStringMaterializationHandle? materialization);
        MarkXString(materialization);
        return value;
    }

    private void MarkXString(CStringMaterializationHandle? materialization)
    {
        if (_blocks.CaptureBridge is not { } capture)
            return;

        capture.MarkXString(materialization
            ?? throw new InvalidDataException(
                "An XString load did not return its captured CString materialization occurrence."));
    }

    public byte[]? LoadBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment = 0)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.PackedAddress is not null)
        {
            ValidateOffsetPointerRange<byte[]>(pointer, byteCount, "byte[]");
            return null;
        }

        if (!HasInlinePayload(pointer))
            return null;

        PatchInlinePointerCell(pointer, alignment);
        return _blocks.Load(cursor, byteCount);
    }

    public T? ReadNullableInline<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        Func<T> readPayload,
        int alignment = 0)
        where T : class
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (!HasInlinePayload(pointer))
            return null;

        BeginInlinePayload(pointer, alignment);
        return readPayload();
    }

    public T ReadRequiredInline<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        Func<T> readPayload,
        string ownerName,
        int alignment = 0)
    {
        if (!HasInlinePayload(pointer))
            throw new InvalidDataException($"{ownerName} pointer 0x{pointer.Raw:X8} does not reference inline payload data.");

        BeginInlinePayload(pointer, alignment);
        return readPayload();
    }

    public string? ReadCString(FastFileCursor cursor, XPointerReference pointer)
    {
        return ReadNullableInline(cursor, pointer, cursor.ReadCString);
    }

    public byte[]? ReadBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment = 0)
    {
        return ReadNullableInline(cursor, pointer, () => cursor.ReadBytes(byteCount), alignment);
    }

    public void ReadInlinePayload(
        FastFileCursor cursor,
        XPointerReference pointer,
        Action readPayload,
        int alignment = 0)
    {
        if (!HasInlinePayload(pointer))
            return;

        BeginInlinePayload(pointer, alignment);
        readPayload();
    }

    public void ValidateOffsetPointer<T>(XPointerReference pointer)
    {
        ValidateOffsetPointer(pointer, typeof(T));
    }

    public void ValidateOffsetPointerRange(
        XPointerReference pointer,
        int byteCount,
        string targetName)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        ValidateOffsetPointer(pointer, null, byteCount, targetName);
    }

    public void ValidateOffsetPointerRange<T>(
        XPointerReference pointer,
        int byteCount,
        string? targetName = null)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        ValidateOffsetPointer(pointer, typeof(T), byteCount, targetName);
    }

    internal void ValidateOffsetPointerRange<T>(
        XPointerRead pointer,
        int byteCount,
        string? targetName = null)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        ValidateOffsetPointer(
            pointer.Pointer,
            typeof(T),
            byteCount,
            targetName,
            XPointerNullability.Unspecified,
            pointer.CaptureHandle);
    }

    public void ValidateOffsetPointerRange<T>(
        XPointerReference pointer,
        int byteCount,
        XPointerNullability nullability,
        string? targetName = null)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        ValidateNullability(nullability);
        ValidateOffsetPointer(pointer, typeof(T), byteCount, targetName, nullability);
    }

    public void ValidateOffsetPointerRange(
        XPointerReference pointer,
        Type targetType,
        int byteCount,
        string? targetName = null)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        ValidateOffsetPointer(pointer, targetType, byteCount, targetName);
    }

    public int ReadAliasCellRaw(XPointerReference pointer)
    {
        if (pointer.Type != PointerType.Offset || pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            throw new InvalidDataException($"Pointer 0x{pointer.Raw:X8} is not a packed alias-cell pointer.");

        if (pointer.PackedAddress is not { } sourceCell)
            throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} has no packed source cell.");

        ValidateRange(pointer.Raw, sourceCell, sizeof(int), "alias pointer cell");
        return _blocks.ReadInt32(sourceCell);
    }

    private void ValidateOffsetPointer(
        XPointerReference pointer,
        Type? targetType)
    {
        ValidateOffsetPointer(pointer, targetType, null, null, XPointerNullability.Unspecified, null);
    }

    private void ValidateOffsetPointer(
        XPointerReference pointer,
        Type? targetType,
        XPointerNullability nullability)
    {
        ValidateOffsetPointer(pointer, targetType, null, null, nullability, null);
    }

    private void ValidateOffsetPointer(
        XPointerReference pointer,
        Type? targetType,
        int? byteCountOverride,
        string? targetNameOverride,
        XPointerNullability nullability = XPointerNullability.Unspecified,
        XPointerReadHandle? captureHandle = null)
    {
        if (_assetPool.TryResolve(pointer.Raw, out XAssetPoolEntry? directPoolEntry))
        {
            ValidateAssetPoolTarget(
                targetType,
                byteCountOverride,
                targetNameOverride,
                directPoolEntry,
                pointer.Raw);
            return;
        }

        if (pointer.Type == PointerType.Offset && pointer.PackedAddress is null)
        {
            throw new InvalidDataException(
                $"Runtime pointer 0x{unchecked((uint)pointer.Raw):X8} is neither a registered XAsset-pool address nor an XBlockAddress.");
        }

        if (pointer.PackedAddress is not { } address)
            return;

        string targetName = targetNameOverride ?? GetTargetName(targetType);
        bool hasTargetContract = targetType is not null || byteCountOverride.HasValue || targetNameOverride is not null;

        if (pointer.ResolutionMode == XPointerResolutionMode.AliasCell)
        {
            ValidateRange(pointer.Raw, address, sizeof(int), $"{targetName} alias cell");

            int aliasedRaw = _blocks.ReadInt32(address);
            if (aliasedRaw == 0)
            {
                if (nullability == XPointerNullability.Required)
                {
                    throw new InvalidDataException(
                        $"Required {targetName} pointer at {FormatCellAddress(pointer.CellAddress)} resolves through " +
                        $"alias cell {address} to a null object pointer.");
                }

                return;
            }

            if (_assetPool.TryResolve(aliasedRaw, out XAssetPoolEntry? poolEntry))
            {
                ValidateAssetPoolTarget(
                    targetType,
                    byteCountOverride,
                    targetNameOverride,
                    poolEntry,
                    aliasedRaw);
                return;
            }

            if (XPointerCodec.GetType(aliasedRaw) != PointerType.Offset)
                return;

            XBlockAddress aliasedAddress = XPointerCodec.Decode(aliasedRaw);
            if (!hasTargetContract)
                return;

            ValidateTarget(
                pointer,
                rawPointer: aliasedRaw,
                address: aliasedAddress,
                targetType: targetType,
                byteCountOverride: byteCountOverride,
                targetName: targetName,
                captureHandle: captureHandle);
            return;
        }

        if (!hasTargetContract)
            return;

        ValidateTarget(
            pointer,
            rawPointer: pointer.Raw,
            address: address,
            targetType: targetType,
            byteCountOverride: byteCountOverride,
            targetName: targetName,
            captureHandle: captureHandle);
    }

    private void ValidateAssetPoolTarget(
        Type? targetType,
        int? byteCountOverride,
        string? targetNameOverride,
        XAssetPoolEntry entry,
        int aliasedRaw)
    {
        string targetName = targetNameOverride ?? GetTargetName(targetType);
        if (targetType is not null && !targetType.IsAssignableFrom(entry.Asset.GetType()))
        {
            throw new InvalidDataException(
                $"Runtime pointer 0x{unchecked((uint)aliasedRaw):X8} resolves to {entry.Asset.GetType().Name} " +
                $"in the XAsset pool, not requested target {targetType.Name}.");
        }

        int byteCount = byteCountOverride ?? GetSerializedSize(targetType) ?? entry.HeaderBytes.Length;
        if (byteCount > entry.HeaderBytes.Length)
        {
            throw new InvalidDataException(
                $"Runtime pointer 0x{unchecked((uint)aliasedRaw):X8} resolves to canonical {entry.AssetType} " +
                $"header with 0x{entry.HeaderBytes.Length:X} bytes; requested 0x{byteCount:X} bytes for {targetName}.");
        }
    }

    private void ValidateTarget(
        XPointerReference sourcePointer,
        int rawPointer,
        XBlockAddress address,
        Type? targetType,
        int? byteCountOverride,
        string targetName,
        XPointerReadHandle? captureHandle)
    {
        if (targetType == typeof(string))
        {
            _blocks.ValidateMaterializedCString(address, targetName, rawPointer);
            return;
        }

        int byteCount = byteCountOverride ?? GetSerializedSize(targetType) ?? 1;
        if (byteCount == 0)
        {
            if (byteCountOverride.HasValue)
                BindEncodedTargetValidation(sourcePointer, address, byteCount, captureHandle);
            return;
        }

        ValidateRange(rawPointer, address, byteCount, targetName);
        if (byteCountOverride.HasValue)
            BindEncodedTargetValidation(sourcePointer, address, byteCount, captureHandle);
    }

    private void BindEncodedTargetValidation(
        XPointerReference sourcePointer,
        XBlockAddress address,
        int byteCount,
        XPointerReadHandle? captureHandle)
    {
        // An alias-cell source encodes the cell address, while ValidateTarget
        // is called with the object address read from that cell. The latter is
        // semantic validation, not the source relocation's encoded target.
        if (sourcePointer.ResolutionMode == XPointerResolutionMode.AliasCell)
            return;

        _blocks.CaptureBridge?.BindValidatedTarget(sourcePointer, address, byteCount, captureHandle);
    }

    private void ValidateRange(
        int rawPointer,
        XBlockAddress address,
        int byteCount,
        string targetName)
    {
        _blocks.ValidateMaterializedRange(address, byteCount, targetName, rawPointer);
    }

    private void ValidateNullObjectPointer(
        XPointerReference pointer,
        Type targetType,
        XPointerNullability nullability)
    {
        if (pointer.Type != PointerType.Null)
            return;

        if (nullability == XPointerNullability.Required)
        {
            throw new InvalidDataException(
                $"Required {GetTargetName(targetType)} pointer at {FormatCellAddress(pointer.CellAddress)} is null.");
        }

    }

    private static void ValidateNullability(XPointerNullability nullability)
    {
        if (!Enum.IsDefined(nullability))
            throw new ArgumentOutOfRangeException(nameof(nullability), nullability, "Unknown pointer nullability contract.");
    }

    private XPointerReadHandle? RecordRead(FastFileCursor cursor, int cellOffset, XPointerReference pointer)
    {
        CaptureOccurrence? occurrence = _blocks.CaptureBridge?.RecordPointer(
            cursor,
            cellOffset,
            pointer.CellAddress,
            pointer.Raw,
            pointer.ResolutionMode);
        return occurrence is { } value ? new XPointerReadHandle(value) : null;
    }

    private static string FormatCellAddress(XBlockAddress? cellAddress) =>
        cellAddress?.ToString() ?? "an unknown cell";

    private static string GetTargetName(Type? targetType)
    {
        if (targetType is null)
            return "untyped pointer target";

        return TargetMetadata.GetOrAdd(targetType, CreateTargetMetadata).Name;
    }

    private static int? GetSerializedSize(Type? targetType)
    {
        return targetType is null
            ? null
            : TargetMetadata.GetOrAdd(targetType, CreateTargetMetadata).SerializedSize;
    }

    private static PointerTargetMetadata CreateTargetMetadata(Type targetType)
    {
        bool isArray = targetType.IsArray;
        string name = isArray
            ? $"{targetType.GetElementType()?.Name ?? "unknown"}[]"
            : targetType.Name;
        if (targetType == typeof(string) || isArray)
            return new PointerTargetMetadata(name, SerializedSize: null);

        FieldInfo? field = targetType.GetField(
            "SerializedSize",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        int? serializedSize = field?.FieldType == typeof(int)
            ? (int?)field.GetRawConstantValue()
            : null;
        return new PointerTargetMetadata(name, serializedSize);
    }

    private readonly record struct PointerTargetMetadata(
        string Name,
        int? SerializedSize);
}
