using IW4.Render.Techniques;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using IW4.Render.Materials;
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

    internal void WriteSingles(ImmutableArray<float> values)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException(
                "Digest float storage is uninitialized.",
                nameof(values));
        }

        WriteInt32(values.Length);
        if (values.IsEmpty)
            return;
        if (BitConverter.IsLittleEndian)
        {
            _hash.AppendData(MemoryMarshal.AsBytes(values.AsSpan()));
            return;
        }

        Span<byte> bytes = stackalloc byte[256 * sizeof(float)];
        for (int first = 0; first < values.Length; first += 256)
        {
            int count = Math.Min(256, values.Length - first);
            Span<byte> chunk = bytes[..(count * sizeof(float))];
            for (int index = 0; index < count; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    chunk.Slice(index * sizeof(float), sizeof(float)),
                    BitConverter.SingleToInt32Bits(values[first + index]));
            }
            _hash.AppendData(chunk);
        }
    }

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

    internal void AppendRenderStateV1(RenderState state)
    {
        WriteBoolean(state.HasState);
        WriteUInt32(state.LoadBits0);
        WriteUInt32(state.LoadBits1);
        WriteUInt32(state.CommandWordCount);
        WriteBoolean(state.ShaderPackerSrgbEnabled);
        WriteUInt32((uint)state.ColorMask);
        WriteBoolean(state.AlphaTestEnabled);
        WriteUInt32((uint)state.AlphaFunc);
        WriteByte(state.AlphaRef);
        WriteBoolean(state.CullEnabled);
        WriteUInt32((uint)state.CullFace);
        WriteUInt32((uint)state.PolygonMode);
        WriteBoolean(state.BlendEnabled);
        WriteUInt32((uint)state.BlendEquationRgb);
        WriteUInt32((uint)state.BlendEquationAlpha);
        WriteUInt32((uint)state.BlendSourceRgb);
        WriteUInt32((uint)state.BlendSourceAlpha);
        WriteUInt32((uint)state.BlendDestinationRgb);
        WriteUInt32((uint)state.BlendDestinationAlpha);
        WriteBoolean(state.DepthTestEnabled);
        WriteBoolean(state.DepthWriteEnabled);
        WriteUInt32((uint)state.DepthFunc);
        WriteBoolean(state.Stencil.Enabled);
        WriteBoolean(state.Stencil.BackFaceStateIsIndependent);
        AppendMapRenderStencilFaceV1(state.Stencil.Front);
        AppendMapRenderStencilFaceV1(state.Stencil.Back);
        WriteByte((byte)state.PolygonOffsetMode);
        WriteSingle(state.PolygonOffsetFactor);
        WriteSingle(state.PolygonOffsetUnits);
    }

    internal string FinishHex()
    {
        if (_finished)
            throw new InvalidOperationException("The digest has already been finalized.");
        _finished = true;
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public void Dispose() => _hash.Dispose();

    private void AppendMapRenderStencilFaceV1(StencilFaceState state)
    {
        WriteUInt32((uint)state.Function);
        WriteInt32(state.Reference);
        WriteUInt32(state.CompareMask);
        WriteUInt32((uint)state.FailOperation);
        WriteUInt32((uint)state.DepthFailOperation);
        WriteUInt32((uint)state.PassOperation);
    }
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
