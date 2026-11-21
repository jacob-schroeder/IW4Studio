using System.Runtime.InteropServices;

namespace IW4.Studio.Desktop.Editors.LoadedSounds;

/// <summary>Builds peak amplitudes from decoded audio, never from encoded MPEG metadata.</summary>
internal static class LoadedSoundWaveformExtractor
{
    private const string AudioToolbox =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const uint FileDataFormat = 0x66666D74; // 'ffmt'
    private const uint ClientDataFormat = 0x63666D74; // 'cfmt'
    private const uint FileLengthFrames = 0x2366726D; // '#frm'
    private const uint LinearPcm = 0x6C70636D; // 'lpcm'
    private const uint FloatAndPacked = 1 | 8;
    private const int FramesPerRead = 4096;

    private static readonly AudioFileReadProc ReadCallback = Read;
    private static readonly AudioFileGetSizeProc GetSizeCallback = GetSize;

    /// <summary>Returns null when the host or codec cannot decode the payload.</summary>
    public static Task<float[]?> ExtractAsync(
        byte[] encodedAudio,
        int peakCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encodedAudio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peakCount);
        if (!OperatingSystem.IsMacOS() || encodedAudio.Length == 0)
            return Task.FromResult<float[]?>(null);

        return Task.Run(() => Extract(encodedAudio, peakCount, cancellationToken), cancellationToken);
    }

    private static unsafe float[]? Extract(
        byte[] encodedAudio,
        int peakCount,
        CancellationToken cancellationToken)
    {
        GCHandle dataHandle = GCHandle.Alloc(encodedAudio);
        IntPtr audioFile = IntPtr.Zero;
        IntPtr extendedFile = IntPtr.Zero;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AudioFileOpenWithCallbacks(
                    GCHandle.ToIntPtr(dataHandle),
                    ReadCallback,
                    IntPtr.Zero,
                    GetSizeCallback,
                    IntPtr.Zero,
                    0,
                    out audioFile) != 0 || audioFile == IntPtr.Zero)
                return null;

            if (ExtAudioFileWrapAudioFileID(audioFile, 0, out extendedFile) != 0 ||
                extendedFile == IntPtr.Zero)
                return null;

            uint formatSize = (uint)Marshal.SizeOf<AudioStreamBasicDescription>();
            if (ExtAudioFileGetFormat(
                    extendedFile, FileDataFormat, ref formatSize, out var sourceFormat) != 0 ||
                sourceFormat.ChannelsPerFrame is 0 or > 32 ||
                !double.IsFinite(sourceFormat.SampleRate) || sourceFormat.SampleRate <= 0)
                return null;

            uint lengthSize = sizeof(long);
            if (ExtAudioFileGetLength(
                    extendedFile, FileLengthFrames, ref lengthSize, out long frameCount) != 0 ||
                frameCount <= 0)
                return null;

            int channels = checked((int)sourceFormat.ChannelsPerFrame);
            var clientFormat = new AudioStreamBasicDescription
            {
                SampleRate = sourceFormat.SampleRate,
                FormatId = LinearPcm,
                FormatFlags = FloatAndPacked,
                BytesPerPacket = (uint)(channels * sizeof(float)),
                FramesPerPacket = 1,
                BytesPerFrame = (uint)(channels * sizeof(float)),
                ChannelsPerFrame = (uint)channels,
                BitsPerChannel = 32
            };
            if (ExtAudioFileSetProperty(
                    extendedFile,
                    ClientDataFormat,
                    (uint)Marshal.SizeOf<AudioStreamBasicDescription>(),
                    ref clientFormat) != 0)
                return null;

            var peaks = new float[peakCount];
            var samples = new float[FramesPerRead * channels];
            long frameOffset = 0;
            fixed (float* samplePointer = samples)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    uint frames = FramesPerRead;
                    var buffers = new AudioBufferList
                    {
                        NumberBuffers = 1,
                        Buffer = new AudioBuffer
                        {
                            NumberChannels = (uint)channels,
                            DataByteSize = (uint)(samples.Length * sizeof(float)),
                            Data = (IntPtr)samplePointer
                        }
                    };
                    if (ExtAudioFileRead(extendedFile, ref frames, ref buffers) != 0)
                        return null;
                    if (frames == 0)
                        break;

                    for (int frame = 0; frame < frames; frame++)
                    {
                        float amplitude = 0;
                        int firstSample = frame * channels;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            float sample = samples[firstSample + channel];
                            if (float.IsFinite(sample))
                                amplitude = Math.Max(amplitude, Math.Abs(sample));
                        }

                        long currentFrame = frameOffset + frame;
                        int index = (int)Math.Min(
                            peakCount - 1,
                            (long)((double)currentFrame / frameCount * peakCount));
                        peaks[index] = Math.Max(peaks[index], Math.Min(1, amplitude));
                    }
                    frameOffset += frames;
                }
            }
            return frameOffset == 0 ? null : peaks;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unsupported codecs and absent native entry points leave a seekable timeline.
            return null;
        }
        finally
        {
            if (extendedFile != IntPtr.Zero)
                ExtAudioFileDispose(extendedFile);
            if (audioFile != IntPtr.Zero)
                AudioFileClose(audioFile);
            dataHandle.Free();
        }
    }

    private static int Read(
        IntPtr clientData,
        long position,
        uint requestedCount,
        IntPtr buffer,
        out uint actualCount)
    {
        actualCount = 0;
        if (position < 0 || buffer == IntPtr.Zero)
            return -50;

        var data = (byte[])GCHandle.FromIntPtr(clientData).Target!;
        if (position >= data.Length)
            return 0;

        int count = (int)Math.Min(requestedCount, (ulong)(data.LongLength - position));
        Marshal.Copy(data, (int)position, buffer, count);
        actualCount = (uint)count;
        return 0;
    }

    private static long GetSize(IntPtr clientData) =>
        ((byte[])GCHandle.FromIntPtr(clientData).Target!).LongLength;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct AudioBufferList
    {
        [FieldOffset(0)] public uint NumberBuffers;
        [FieldOffset(8)] public AudioBuffer Buffer;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AudioFileReadProc(
        IntPtr clientData, long position, uint requestedCount, IntPtr buffer, out uint actualCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long AudioFileGetSizeProc(IntPtr clientData);

    [DllImport(AudioToolbox)]
    private static extern int AudioFileOpenWithCallbacks(
        IntPtr clientData,
        AudioFileReadProc read,
        IntPtr write,
        AudioFileGetSizeProc getSize,
        IntPtr setSize,
        uint fileTypeHint,
        out IntPtr audioFile);

    [DllImport(AudioToolbox)]
    private static extern int AudioFileClose(IntPtr audioFile);

    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileWrapAudioFileID(
        IntPtr audioFile, byte forWriting, out IntPtr extendedFile);

    [DllImport(AudioToolbox, EntryPoint = "ExtAudioFileGetProperty")]
    private static extern int ExtAudioFileGetFormat(
        IntPtr extendedFile, uint property, ref uint size, out AudioStreamBasicDescription value);

    [DllImport(AudioToolbox, EntryPoint = "ExtAudioFileGetProperty")]
    private static extern int ExtAudioFileGetLength(
        IntPtr extendedFile, uint property, ref uint size, out long value);

    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileSetProperty(
        IntPtr extendedFile, uint property, uint size, ref AudioStreamBasicDescription value);

    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileRead(
        IntPtr extendedFile, ref uint frameCount, ref AudioBufferList buffers);

    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileDispose(IntPtr extendedFile);
}
