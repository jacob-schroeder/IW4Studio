using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Iw4Radiant.Views;

/// <summary>Changes sample playback speed and pitch together, without altering source audio.</summary>
internal static class SoundPreviewPitch
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>Decodes a preview source to the engine's fixed 48 kHz, stereo, float PCM format.</summary>
    internal static unsafe EnginePcm DecodeForEngine(byte[] audio)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Sound preview currently requires macOS.");
        string path = Path.Combine(Path.GetTempPath(), $"iw4-radiant-{Guid.NewGuid():N}.audio");
        IntPtr url = IntPtr.Zero, file = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, audio);
            byte[] utf8 = Encoding.UTF8.GetBytes(path);
            url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, utf8, utf8.Length, 0);
            if (url == IntPtr.Zero) throw new InvalidOperationException("Cannot open the preview audio.");
            Require(ExtAudioFileOpenURL(url, out file));
            var source = new AudioStreamBasicDescription();
            uint size = (uint)sizeof(AudioStreamBasicDescription);
            Require(ExtAudioFileGetProperty(file, 0x66666d74, ref size, &source)); // 'ffmt'
            if (source.Channels is < 1 or > 2 || !double.IsFinite(source.SampleRate) || source.SampleRate <= 0)
                throw new InvalidDataException("Preview requires mono or stereo audio with a valid sample rate.");
            uint channels = source.Channels;
            uint sourceFrameBytes = channels * sizeof(float);
            var client = new AudioStreamBasicDescription
            {
                SampleRate = 48000,
                FormatId = 0x6c70636d, // 'lpcm'
                FormatFlags = 9, // IEEE float, packed, native little endian
                BytesPerPacket = sourceFrameBytes,
                FramesPerPacket = 1,
                BytesPerFrame = sourceFrameBytes,
                Channels = channels,
                BitsPerChannel = 32
            };
            Require(ExtAudioFileSetProperty(file, 0x63666d74, size, &client)); // 'cfmt'
            using var output = new MemoryStream();
            byte[] chunk = new byte[8192 * sourceFrameBytes];
            byte[] stereo = channels == 1 ? new byte[8192 * 8] : chunk;
            fixed (byte* data = chunk)
            {
                while (true)
                {
                    uint frames = 8192;
                    var buffers = new AudioBufferList
                    {
                        Count = 1,
                        Buffer = new AudioBuffer { Channels = channels, ByteCount = (uint)chunk.Length, Data = (IntPtr)data }
                    };
                    Require(ExtAudioFileRead(file, ref frames, ref buffers));
                    if (frames == 0) break;
                    if (frames > 8192 || output.Length + frames * 8 > 32L * 1024 * 1024)
                        throw new InvalidDataException("Decoded sound exceeds the preview's 32 MiB per-sound limit.");
                    if (channels == 1)
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            Buffer.BlockCopy(chunk, i * 4, stereo, i * 8, 4);
                            Buffer.BlockCopy(chunk, i * 4, stereo, i * 8 + 4, 4);
                        }
                    }
                    output.Write(stereo, 0, checked((int)(frames * 8)));
                }
            }
            if (output.Length == 0) throw new InvalidDataException("The sound contains no decodable audio frames.");
            return new EnginePcm(output.ToArray(), checked((uint)(output.Length / 8)));
        }
        finally
        {
            if (file != IntPtr.Zero) ExtAudioFileDispose(file);
            if (url != IntPtr.Zero) CFRelease(url);
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    internal readonly record struct EnginePcm(byte[] Bytes, uint Frames);

    // Called on the audio-loading worker, never during camera navigation.
    internal static unsafe byte[] Apply(byte[] audio, float pitch)
    {
        if (!float.IsFinite(pitch) || pitch <= 0) throw new ArgumentException("Sound pitch must be positive.");
        if (pitch == 1) return audio;
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Sound preview currently requires macOS.");

        string path = Path.GetTempFileName();
        IntPtr url = IntPtr.Zero, file = IntPtr.Zero;
        try
        {
            File.WriteAllBytes(path, audio);
            byte[] utf8 = Encoding.UTF8.GetBytes(path);
            url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, utf8, utf8.Length, 0);
            if (url == IntPtr.Zero) throw new InvalidOperationException("Cannot open the preview audio.");
            Require(ExtAudioFileOpenURL(url, out file));
            var format = new AudioStreamBasicDescription();
            uint size = (uint)sizeof(AudioStreamBasicDescription);
            Require(ExtAudioFileGetProperty(file, 0x66666d74, ref size, &format)); // 'ffmt'
            if (format.Channels is < 1 or > 2 || !double.IsFinite(format.SampleRate) || format.SampleRate <= 0)
                throw new InvalidDataException("Pitch preview requires mono or stereo audio with a valid sample rate.");
            double pitchedRate = Math.Round(format.SampleRate * pitch);
            if (pitchedRate is < 1000 or > 384000)
                throw new InvalidDataException("The pitched sample rate is outside the preview range.");

            uint channels = format.Channels;
            uint frameBytes = channels * 2;
            format = new AudioStreamBasicDescription
            {
                SampleRate = format.SampleRate,
                FormatId = 0x6c70636d, // 'lpcm'
                FormatFlags = 12, // signed integer, packed, little endian
                BytesPerPacket = frameBytes,
                FramesPerPacket = 1,
                BytesPerFrame = frameBytes,
                Channels = channels,
                BitsPerChannel = 16
            };
            Require(ExtAudioFileSetProperty(file, 0x63666d74, size, &format)); // 'cfmt'
            using var output = new MemoryStream();
            output.Write(new byte[44]);
            byte[] buffer = new byte[8192 * frameBytes];
            fixed (byte* data = buffer)
            {
                while (true)
                {
                    uint frames = 8192;
                    var buffers = new AudioBufferList
                    {
                        Count = 1,
                        Buffer = new AudioBuffer { Channels = channels, ByteCount = (uint)buffer.Length, Data = (IntPtr)data }
                    };
                    Require(ExtAudioFileRead(file, ref frames, ref buffers));
                    if (frames == 0) break;
                    if (frames > 8192 || output.Length + frames * frameBytes > 128 * 1024 * 1024)
                        throw new InvalidDataException("The decoded sound is too large for pitch preview.");
                    output.Write(buffer, 0, checked((int)(frames * frameBytes)));
                }
            }
            byte[] wave = output.ToArray();
            if (wave.Length == 44) throw new InvalidDataException("The sound contains no decodable audio frames.");
            "RIFF"u8.CopyTo(wave);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
            "WAVEfmt "u8.CopyTo(wave.AsSpan(8));
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), (short)channels);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), (int)pitchedRate);
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), checked((int)(pitchedRate * frameBytes)));
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), (short)frameBytes);
            BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
            "data"u8.CopyTo(wave.AsSpan(36));
            BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), wave.Length - 44);
            return wave;
        }
        finally
        {
            if (file != IntPtr.Zero) ExtAudioFileDispose(file);
            if (url != IntPtr.Zero) CFRelease(url);
            File.Delete(path);
        }
    }

    private static void Require(int status)
    {
        if (status != 0) throw new InvalidDataException($"Cannot decode audio for pitch preview (audio error {status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        internal double SampleRate;
        internal uint FormatId, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, Channels, BitsPerChannel, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioBuffer
    {
        internal uint Channels, ByteCount;
        internal IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioBufferList
    {
        internal uint Count;
        internal AudioBuffer Buffer;
    }

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] bytes, nint length, byte isDirectory);
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileOpenURL(IntPtr url, out IntPtr file);
    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileDispose(IntPtr file);
    [DllImport(AudioToolbox)]
    private static extern unsafe int ExtAudioFileGetProperty(IntPtr file, uint property, ref uint size, void* data);
    [DllImport(AudioToolbox)]
    private static extern unsafe int ExtAudioFileSetProperty(IntPtr file, uint property, uint size, void* data);
    [DllImport(AudioToolbox)]
    private static extern int ExtAudioFileRead(IntPtr file, ref uint frames, ref AudioBufferList buffers);
}
