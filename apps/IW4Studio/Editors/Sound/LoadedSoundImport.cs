using System.Buffers.Binary;
using IW4.Game.Assets.Sound;

namespace IW4.Studio.Desktop.Editors.Sound;

internal sealed record LoadedSoundImport(LoadedSound Replacement, MpegAudioPreviewInfo Analysis)
{
    internal static async Task<byte[]> ReadMp3Async(Stream stream, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + count > SoundFile.MaxInMemoryPayloadBytes)
                throw new InvalidDataException($"The MP3 exceeds the {SoundFile.MaxInMemoryPayloadBytes / (1024 * 1024)} MiB in-memory sound limit.");
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }

    internal static LoadedSoundImport Compile(LoadedSound template, ReadOnlyMemory<byte> sourceBytes, int visualizationBars)
    {
        if (!MpegAudioPreview.TryAnalyze(
                sourceBytes.Span,
                visualizationBars,
                out MpegAudioPreviewInfo? analysis) ||
            analysis is null)
        {
            throw new InvalidDataException(
                "The file is not a supported contiguous MPEG Layer III stream.");
        }

        if (analysis.AudioByteCount <= 0 || analysis.FrameOffsets.Count == 0)
            throw new InvalidDataException("The MP3 contains no MPEG audio frames.");
        if (analysis.FrameOffsets.Count > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "The MP3 contains too many MPEG frames for an IW4 LoadedSound.");
        }

        long playableSamples = analysis.TotalSamples - template.InitialSkipSamples - template.EndTrimSamples;
        long durationMilliseconds = checked(
            playableSamples * 1000L / analysis.SampleRate);
        if (durationMilliseconds is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "Embedded IW4 sounds must be between 1 ms and 65.535 seconds long.");
        }
        if (analysis.SampleRate > ushort.MaxValue ||
            analysis.ChannelCount > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "The MP3 sample layout cannot be represented by an IW4 LoadedSound.");
        }

        byte[] physicalData = sourceBytes.Slice(
            analysis.AudioStartOffset,
            analysis.AudioByteCount).ToArray();
        byte[] seekTable = new byte[checked(
            analysis.FrameOffsets.Count * sizeof(uint))];
        for (int index = 0; index < analysis.FrameOffsets.Count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                seekTable.AsSpan(index * sizeof(uint), sizeof(uint)),
                checked((uint)analysis.FrameOffsets[index]));
        }

        var replacement = new LoadedSound
        {
            Offset = template.Offset,
            RuntimeAddress = template.RuntimeAddress,
            NamePointer = template.NamePointer,
            Name = template.Name,
            PhysicalDataByteCount = physicalData.Length,
            DurationMilliseconds = checked((ushort)durationMilliseconds),
            ChannelCount = checked((ushort)analysis.ChannelCount),
            SampleRate = checked((ushort)analysis.SampleRate),
            InitialSkipSamples = template.InitialSkipSamples,
            EndTrimSamples = template.EndTrimSamples,
            SeekTableCount = checked((ushort)analysis.FrameOffsets.Count),
            SeekTablePointer = template.SeekTablePointer,
            SeekTable = seekTable,
            PhysicalDataPointer = template.PhysicalDataPointer,
            PhysicalData = physicalData
        };
        return new LoadedSoundImport(replacement, analysis);
    }
}
