using IW4.Game.IO;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using ModelVec3 = IW4.Game.Math.Vec3;
using XString = IW4.Game.Pointers.XPointer<string>;

namespace IW4.Loaders.Assets.XModel;

internal static class XModelPayloadReader
{
    internal static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadUInt16Values(ReadRawBytes(cursor, pointer, checked(count * sizeof(ushort)), alignment: 2, context, out runtimeAddress));
    }

    internal static IReadOnlyList<byte> ReadRawBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        if (byteCount < 0)
            throw new InvalidDataException($"Invalid negative byte count {byteCount}.");

        if (pointer.Type == PointerType.Null)
        {
            runtimeAddress = null;
            return [];
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<byte[]>(pointer, byteCount, "byte[]");
            if (pointer.PackedAddress is { } address)
            {
                runtimeAddress = address;
                return context.Blocks.ReadBytes(address, byteCount);
            }

            runtimeAddress = null;
            return [];
        }

        runtimeAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        return context.Blocks.Load(cursor, byteCount);
    }

    internal static void RequireExactByteCount(IReadOnlyList<byte> bytes, int count, int stride, string rowName)
    {
        int expected = checked(count * stride);
        if (bytes.Count != expected)
            throw new InvalidDataException($"{rowName} array expected 0x{expected:X} byte(s), got 0x{bytes.Count:X}.");
    }

    internal static IReadOnlyList<ushort> ReadUInt16Values(IReadOnlyList<byte> bytes)
    {
        var cursor = new FastFileCursor(bytes.ToArray());
        var values = new ushort[bytes.Count / sizeof(ushort)];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt16();

        return values;
    }

    internal static ModelVec3 ReadVec3(FastFileCursor cursor)
    {
        return new ModelVec3
        {
            X = cursor.ReadSingle(),
            Y = cursor.ReadSingle(),
            Z = cursor.ReadSingle()
        };
    }

    internal static string? ReadXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    internal static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadDeferredPointer<T>(cursor, mode);

    internal static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);
}
