using System.Buffers.Binary;
using System.Collections.Immutable;
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

    internal void AppendMapRenderStateV1(MapRenderState state)
    {
        WriteBoolean(state.HasState);
        WriteUInt32(state.LoadBits0);
        WriteUInt32(state.LoadBits1);
        WriteUInt32(state.Tail);
        WriteBoolean(state.ShaderPackerSrgbEnabled);
        WriteUInt32(state.ColorMask);
        WriteBoolean(state.AlphaTestEnabled);
        WriteUInt32(state.AlphaFunc);
        WriteByte(state.AlphaRef);
        WriteBoolean(state.CullEnabled);
        WriteUInt32(state.CullFace);
        WriteUInt32(state.PolygonMode);
        WriteBoolean(state.BlendEnabled);
        WriteUInt32(state.BlendEquationRgb);
        WriteUInt32(state.BlendEquationAlpha);
        WriteUInt32(state.BlendSourceRgb);
        WriteUInt32(state.BlendSourceAlpha);
        WriteUInt32(state.BlendDestinationRgb);
        WriteUInt32(state.BlendDestinationAlpha);
        WriteBoolean(state.DepthTestEnabled);
        WriteBoolean(state.DepthWriteEnabled);
        WriteUInt32(state.DepthFunc);
        WriteBoolean(state.Stencil.Enabled);
        WriteBoolean(state.Stencil.BackFaceStateIsIndependent);
        AppendMapRenderStencilFaceV1(state.Stencil.Front);
        AppendMapRenderStencilFaceV1(state.Stencil.Back);
        WriteBoolean(state.PolygonOffsetEnabled);
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

    private void AppendMapRenderStencilFaceV1(MapRenderStencilFaceState state)
    {
        WriteUInt32(state.Function);
        WriteInt32(state.Reference);
        WriteUInt32(state.CompareMask);
        WriteUInt32(state.FailOperation);
        WriteUInt32(state.DepthFailOperation);
        WriteUInt32(state.PassOperation);
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
