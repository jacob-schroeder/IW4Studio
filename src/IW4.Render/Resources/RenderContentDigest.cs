using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Resources;

internal static class RenderContentDigest
{
    internal static string Compute(
        Action<RenderContentDigestWriter> append)
    {
        ArgumentNullException.ThrowIfNull(append);
        using var writer = new RenderContentDigestWriter();
        append(writer);
        return writer.FinishHex();
    }
}

internal sealed class RenderContentDigestWriter : IDisposable
{
    private readonly IncrementalHash _hash =
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;

    internal void WriteBoolean(bool value) =>
        WriteByte(value ? (byte)1 : (byte)0);

    internal void WriteByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        _hash.AppendData(bytes);
    }

    internal void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    internal void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    internal void WriteInt64(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    internal void WriteSingle(float value) =>
        WriteInt32(BitConverter.SingleToInt32Bits(value));

    internal void WriteNullableInt32(int? value)
    {
        WriteBoolean(value.HasValue);
        if (value.HasValue)
            WriteInt32(value.Value);
    }

    internal void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(bytes.Length);
        _hash.AppendData(bytes);
    }

    internal void WriteIdentity(RenderSemanticIdentity identity)
    {
        WriteInt32((int)identity.Kind);
        WriteString(identity.Value);
    }

    internal void WriteBytes(ImmutableArray<byte> values)
    {
        if (values.IsDefault)
            throw new ArgumentException("Digest byte storage is uninitialized.", nameof(values));
        WriteInt32(values.Length);
        _hash.AppendData(values.AsSpan());
    }

    internal string FinishHex()
    {
        if (_finished)
            throw new InvalidOperationException("The digest has already been finalized.");
        _finished = true;
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public void Dispose() => _hash.Dispose();
}

internal static class RenderSnapshotCollections
{
    internal static ImmutableArray<T> Freeze<T>(
        IEnumerable<T> source,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);
        var builder = ImmutableArray.CreateBuilder<T>();
        builder.AddRange(source);
        return builder.ToImmutable();
    }
}
