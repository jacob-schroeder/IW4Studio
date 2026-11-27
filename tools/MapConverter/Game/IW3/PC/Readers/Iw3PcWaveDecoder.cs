using System.Buffers.Binary;

namespace MapConverter.Game.IW3.PC.Readers;

internal static class Iw3PcWaveDecoder
{
    internal static (short[] Samples, int Channels, int SampleRate) DecodeRawPcm(
        ReadOnlySpan<byte> pcm, int channels, int sampleRate, int bits)
    {
        if (bits is not (8 or 16))
            throw new NotSupportedException($"Unsupported loaded PCM: {channels} channel(s), {sampleRate} Hz, {bits} bits.");
        return DecodeSamples(pcm, 1, channels, sampleRate, bits, channels * (bits / 8));
    }

    internal static (short[] Samples, int Channels, int SampleRate) Decode(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 12 || !wave[..4].SequenceEqual("RIFF"u8) ||
            !wave.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Sound is not a RIFF/WAVE file.");

        uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(4, 4));
        if (riffSize < 4 || (long)riffSize + 8 > wave.Length)
            throw new InvalidDataException("Sound has an invalid RIFF length.");

        int limit = checked((int)riffSize + 8);
        int format = 0, channels = 0, sampleRate = 0, bits = 0, blockAlign = 0;
        ReadOnlySpan<byte> data = default;
        bool hasFormat = false, hasData = false;
        for (int offset = 12; offset < limit;)
        {
            if (limit - offset < 8)
                throw new InvalidDataException("Sound has a truncated WAV chunk header.");
            ReadOnlySpan<byte> id = wave.Slice(offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(offset + 4, 4));
            offset += 8;
            if (size > limit - offset)
                throw new InvalidDataException("Sound has a truncated WAV chunk.");
            ReadOnlySpan<byte> chunk = wave.Slice(offset, checked((int)size));

            if (id.SequenceEqual("fmt "u8))
            {
                if (hasFormat || chunk.Length < 16)
                    throw new InvalidDataException("Sound has an invalid WAV format chunk.");
                hasFormat = true;
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(12));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14));
                if (format == 0xfffe)
                {
                    if (chunk.Length < 40)
                        throw new InvalidDataException("Sound has a truncated WAV extensible format.");
                    int extensionSize = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(16));
                    // KSDATAFORMAT_SUBTYPE_WAVEFORMATEX stores the format identifier in the GUID's first word.
                    ReadOnlySpan<byte> waveSubtypeSuffix = [0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71];
                    if (extensionSize < 22 || extensionSize > chunk.Length - 18 ||
                        !chunk.Slice(26, 14).SequenceEqual(waveSubtypeSuffix))
                        throw new NotSupportedException("Unsupported WAV extensible format.");
                    format = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(24));
                    int validBits = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(18));
                    if (validBits is 0 || validBits > bits)
                        throw new InvalidDataException("Sound has invalid WAV valid bits.");
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (hasData)
                    throw new NotSupportedException("Sound has multiple WAV data chunks.");
                hasData = true;
                data = chunk;
            }
            offset += (int)size;
            if ((size & 1) != 0 && offset < limit)
                offset++;
        }

        if (!hasFormat || !hasData || data.IsEmpty)
            throw new InvalidDataException("Sound is missing WAV format or sample data.");
        return DecodeSamples(data, format, channels, sampleRate, bits, blockAlign);
    }

    private static (short[] Samples, int Channels, int SampleRate) DecodeSamples(
        ReadOnlySpan<byte> data, int format, int channels, int sampleRate, int bits, int blockAlign)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("Sound has no PCM samples.");
        if (channels is < 1 or > 2 || sampleRate <= 0 ||
            !(format == 1 && bits is 8 or 16 or 24 or 32 ||
              format == 3 && bits is 32 or 64))
            throw new NotSupportedException(
                $"Unsupported sound format {format}: {channels} channel(s), {sampleRate} Hz, {bits} bits.");
        int sampleBytes = bits / 8;
        if (blockAlign != channels * sampleBytes || data.Length % blockAlign != 0)
            throw new InvalidDataException(
                $"Sound has invalid sample alignment: {channels} channel(s), {sampleRate} Hz, {bits} bits, block {blockAlign}.");

        var samples = new short[data.Length / sampleBytes];
        for (int i = 0, offset = 0; i < samples.Length; i++, offset += sampleBytes)
        {
            ReadOnlySpan<byte> sample = data.Slice(offset, sampleBytes);
            samples[i] = (format, bits) switch
            {
                (1, 8) => (short)((sample[0] - 128) << 8),
                (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample),
                (1, 24) => (short)((sample[0] | sample[1] << 8 | (sbyte)sample[2] << 16) >> 8),
                (1, 32) => (short)(BinaryPrimitives.ReadInt32LittleEndian(sample) >> 16),
                (3, 32) => FloatToPcm(BinaryPrimitives.ReadSingleLittleEndian(sample)),
                (3, 64) => FloatToPcm(BinaryPrimitives.ReadDoubleLittleEndian(sample)),
                _ => throw new InvalidDataException("Sound has an unsupported WAV sample format.")
            };
        }
        return (samples, channels, sampleRate);
    }

    private static short FloatToPcm(double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException("Sound contains a non-finite WAV sample.");
        return (short)Math.Clamp(Math.Round(value * 32768), short.MinValue, short.MaxValue);
    }
}
