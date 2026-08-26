namespace IW4.Studio.Desktop.Editors.Sound;

internal sealed record MpegAudioPreviewInfo(
    string FormatName,
    int SampleRate,
    int ChannelCount,
    TimeSpan Duration,
    IReadOnlyList<double> Levels);

internal static class MpegAudioPreview
{
    private const int HeaderLength = 4;

    private static ReadOnlySpan<int> Mpeg1Layer3BitRates =>
    [
        0, 32, 40, 48, 56, 64, 80, 96,
        112, 128, 160, 192, 224, 256, 320, 0
    ];

    private static ReadOnlySpan<int> Mpeg2Layer3BitRates =>
    [
        0, 8, 16, 24, 32, 40, 48, 56,
        64, 80, 96, 112, 128, 144, 160, 0
    ];

    public static bool TryAnalyze(
        ReadOnlySpan<byte> data,
        int visualizationBarCount,
        out MpegAudioPreviewInfo? info)
    {
        info = null;
        if (data.IsEmpty || visualizationBarCount <= 0)
            return false;

        if (!TryGetAudioStart(data, out int offset))
            return false;

        MpegFrameHeader? streamHeader = null;
        var frameGains = new List<double>();
        long totalSamples = 0;

        while (offset < data.Length)
        {
            ReadOnlySpan<byte> remaining = data[offset..];
            if (IsId3V1Tag(remaining))
            {
                offset = data.Length;
                break;
            }

            if (!TryReadFrameHeader(remaining, out MpegFrameHeader header) ||
                header.FrameLength > remaining.Length)
            {
                return false;
            }

            if (streamHeader is { } firstHeader &&
                (header.Version != firstHeader.Version ||
                 header.SampleRate != firstHeader.SampleRate ||
                 header.ChannelCount != firstHeader.ChannelCount))
            {
                return false;
            }

            ReadOnlySpan<byte> frame = remaining[..header.FrameLength];
            if (!TryReadAverageGlobalGain(frame, header, out double averageGain))
                return false;

            streamHeader ??= header;
            frameGains.Add(averageGain);
            totalSamples += header.SamplesPerFrame;
            offset += header.FrameLength;
        }

        if (streamHeader is not { } parsedHeader || frameGains.Count == 0)
            return false;

        double durationSeconds = (double)totalSamples / parsedHeader.SampleRate;
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            return false;

        info = new MpegAudioPreviewInfo(
            FormatName(parsedHeader.Version),
            parsedHeader.SampleRate,
            parsedHeader.ChannelCount,
            TimeSpan.FromSeconds(durationSeconds),
            BuildLevels(frameGains, visualizationBarCount));
        return true;
    }

    private static bool TryGetAudioStart(ReadOnlySpan<byte> data, out int offset)
    {
        offset = 0;
        if (data.Length < 3 ||
            data[0] != (byte)'I' ||
            data[1] != (byte)'D' ||
            data[2] != (byte)'3')
        {
            return true;
        }

        if (data.Length < 10 ||
            data[3] is < 2 or > 4 ||
            data[4] == 0xFF ||
            (data[6] & 0x80) != 0 ||
            (data[7] & 0x80) != 0 ||
            (data[8] & 0x80) != 0 ||
            (data[9] & 0x80) != 0)
        {
            return false;
        }

        int payloadLength =
            (data[6] << 21) |
            (data[7] << 14) |
            (data[8] << 7) |
            data[9];
        int footerLength = data[3] == 4 && (data[5] & 0x10) != 0 ? 10 : 0;
        long audioOffset = 10L + payloadLength + footerLength;
        if (audioOffset > data.Length)
            return false;

        offset = (int)audioOffset;
        return true;
    }

    private static bool TryReadFrameHeader(
        ReadOnlySpan<byte> data,
        out MpegFrameHeader header)
    {
        header = default;
        if (data.Length < HeaderLength)
            return false;

        uint bits =
            ((uint)data[0] << 24) |
            ((uint)data[1] << 16) |
            ((uint)data[2] << 8) |
            data[3];
        if ((bits & 0xFFE0_0000u) != 0xFFE0_0000u)
            return false;

        MpegVersion version = ((bits >> 19) & 0x3) switch
        {
            0 => MpegVersion.Version25,
            2 => MpegVersion.Version2,
            3 => MpegVersion.Version1,
            _ => MpegVersion.Reserved
        };
        if (version == MpegVersion.Reserved || ((bits >> 17) & 0x3) != 1)
            return false;

        int bitRateIndex = (int)((bits >> 12) & 0xF);
        int sampleRateIndex = (int)((bits >> 10) & 0x3);
        int emphasis = (int)(bits & 0x3);
        if (bitRateIndex is 0 or 15 || sampleRateIndex == 3 || emphasis == 2)
            return false;

        int bitRate = version == MpegVersion.Version1
            ? Mpeg1Layer3BitRates[bitRateIndex]
            : Mpeg2Layer3BitRates[bitRateIndex];
        int sampleRate = SampleRate(version, sampleRateIndex);
        int frameLengthCoefficient = version == MpegVersion.Version1
            ? 144_000
            : 72_000;
        int padding = (int)((bits >> 9) & 0x1);
        int frameLength = checked(
            (frameLengthCoefficient * bitRate / sampleRate) + padding);
        int channelCount = ((bits >> 6) & 0x3) == 3 ? 1 : 2;
        bool hasCrc = ((bits >> 16) & 0x1) == 0;
        int sideInformationLength = version == MpegVersion.Version1
            ? channelCount == 1 ? 17 : 32
            : channelCount == 1 ? 9 : 17;
        int sideInformationOffset = HeaderLength + (hasCrc ? 2 : 0);
        if (frameLength < sideInformationOffset + sideInformationLength)
            return false;

        header = new MpegFrameHeader(
            version,
            sampleRate,
            channelCount,
            frameLength,
            version == MpegVersion.Version1 ? 1_152 : 576,
            sideInformationOffset,
            sideInformationLength);
        return true;
    }

    private static bool TryReadAverageGlobalGain(
        ReadOnlySpan<byte> frame,
        MpegFrameHeader header,
        out double averageGain)
    {
        averageGain = 0;
        int sideEnd = header.SideInformationOffset + header.SideInformationLength;
        if (frame.Length < sideEnd)
            return false;

        ReadOnlySpan<byte> sideInformation =
            frame.Slice(header.SideInformationOffset, header.SideInformationLength);
        int entryCount;
        int firstChannelInformationBit;
        int channelInformationLength;
        if (header.Version == MpegVersion.Version1)
        {
            entryCount = header.ChannelCount * 2;
            int privateBitCount = header.ChannelCount == 1 ? 5 : 3;
            firstChannelInformationBit =
                9 + privateBitCount + (header.ChannelCount * 4);
            channelInformationLength = 59;
        }
        else
        {
            entryCount = header.ChannelCount;
            int privateBitCount = header.ChannelCount == 1 ? 1 : 2;
            firstChannelInformationBit = 8 + privateBitCount;
            channelInformationLength = 63;
        }

        int gainTotal = 0;
        for (int index = 0; index < entryCount; index++)
        {
            int globalGainBit =
                firstChannelInformationBit +
                (index * channelInformationLength) +
                21;
            if (!TryReadBits(sideInformation, globalGainBit, 8, out int gain))
                return false;

            gainTotal += gain;
        }

        averageGain = (double)gainTotal / entryCount;
        return true;
    }

    private static bool TryReadBits(
        ReadOnlySpan<byte> data,
        int bitOffset,
        int bitCount,
        out int value)
    {
        value = 0;
        if (bitOffset < 0 ||
            bitCount is <= 0 or > sizeof(int) * 8 ||
            (long)bitOffset + bitCount > (long)data.Length * 8)
        {
            return false;
        }

        for (int index = 0; index < bitCount; index++)
        {
            int sourceBit = bitOffset + index;
            int bit = (data[sourceBit >> 3] >> (7 - (sourceBit & 7))) & 1;
            value = (value << 1) | bit;
        }

        return true;
    }

    private static IReadOnlyList<double> BuildLevels(
        IReadOnlyList<double> frameGains,
        int barCount)
    {
        double maximumGain = frameGains.Max();
        var amplitudes = new double[frameGains.Count];
        for (int index = 0; index < frameGains.Count; index++)
        {
            // MPEG Layer III applies global_gain in quarter-power-of-two steps.
            // Normalizing against the stream maximum preserves that relationship
            // without pretending that compressed bytes are decoded PCM samples.
            amplitudes[index] = Math.Clamp(
                Math.Pow(2, (frameGains[index] - maximumGain) / 4.0),
                0,
                1);
        }

        var levels = new double[barCount];
        for (int bar = 0; bar < barCount; bar++)
        {
            int start = (int)((long)bar * amplitudes.Length / barCount);
            int end = (int)((long)(bar + 1) * amplitudes.Length / barCount);
            if (end <= start)
            {
                int sample = Math.Min(
                    amplitudes.Length - 1,
                    (int)(((2L * bar) + 1) * amplitudes.Length / (2L * barCount)));
                levels[bar] = amplitudes[sample];
                continue;
            }

            double power = 0;
            for (int index = start; index < end; index++)
                power += amplitudes[index] * amplitudes[index];

            levels[bar] = Math.Sqrt(power / (end - start));
        }

        return levels;
    }

    private static int SampleRate(MpegVersion version, int index) =>
        version switch
        {
            MpegVersion.Version1 => index switch
            {
                0 => 44_100,
                1 => 48_000,
                _ => 32_000
            },
            MpegVersion.Version2 => index switch
            {
                0 => 22_050,
                1 => 24_000,
                _ => 16_000
            },
            _ => index switch
            {
                0 => 11_025,
                1 => 12_000,
                _ => 8_000
            }
        };

    private static string FormatName(MpegVersion version) =>
        version switch
        {
            MpegVersion.Version1 => "MPEG-1 Layer III",
            MpegVersion.Version2 => "MPEG-2 Layer III",
            _ => "MPEG-2.5 Layer III"
        };

    private static bool IsId3V1Tag(ReadOnlySpan<byte> data) =>
        data.Length == 128 &&
        data[0] == (byte)'T' &&
        data[1] == (byte)'A' &&
        data[2] == (byte)'G';

    private enum MpegVersion
    {
        Reserved,
        Version1,
        Version2,
        Version25
    }

    private readonly record struct MpegFrameHeader(
        MpegVersion Version,
        int SampleRate,
        int ChannelCount,
        int FrameLength,
        int SamplesPerFrame,
        int SideInformationOffset,
        int SideInformationLength);
}
