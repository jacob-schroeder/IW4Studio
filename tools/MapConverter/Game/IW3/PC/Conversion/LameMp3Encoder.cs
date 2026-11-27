using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MapConverter.Game.IW3.PC.Conversion;

internal static class LameMp3Encoder
{
    private const string Library = "libmp3lame";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories;
    private static ReadOnlySpan<byte> Id3Header => "ID3\u0003\0\0\0\0\0\0"u8;

    internal static byte[]? ReadExistingMp3(byte[] source)
    {
        if (source.AsSpan().StartsWith("ID3"u8)) return source;
        if (source.Length <= 4 || source[0] != 0xff || (source[1] & 0xe6) != 0xe2 ||
            (source[2] & 0xf0) is 0x00 or 0xf0 || (source[2] & 0x0c) == 0x0c)
            return null;

        byte[] tagged = new byte[checked(Id3Header.Length + source.Length)];
        Id3Header.CopyTo(tagged);
        source.CopyTo(tagged, Id3Header.Length);
        return tagged;
    }

    internal static byte[] Encode(short[] samples, int channels, int sampleRate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using EncoderHandle encoder = lame_init();
        if (encoder.IsInvalid) throw new InvalidOperationException("LAME could not allocate an encoder.");
        Check(lame_set_num_channels(encoder, channels), "setting the channel count");
        Check(lame_set_in_samplerate(encoder, sampleRate), "setting the sample rate");
        lame_set_write_id3tag_automatic(encoder, 0);
        Check(lame_init_params(encoder), "configuring the encoder");

        using var output = new MemoryStream();
        output.Write(Id3Header);
        const int chunkFrames = 8192;
        // LAME specifies 1.25 bytes per input frame plus 7200 bytes of headroom.
        byte[] buffer = new byte[chunkFrames * 5 / 4 + 7200];
        int totalFrames = samples.Length / channels;
        for (int frame = 0; frame < totalFrames;)
        {
            token.ThrowIfCancellationRequested();
            int frames = Math.Min(chunkFrames, totalFrames - frame);
            int offset = frame * channels;
            int bytes = channels == 1
                ? lame_encode_buffer(encoder, ref samples[offset], ref samples[offset], frames, buffer, buffer.Length)
                : lame_encode_buffer_interleaved(encoder, ref samples[offset], frames, buffer, buffer.Length);
            output.Write(buffer, 0, Check(bytes, "encoding PCM"));
            frame += frames;
        }
        token.ThrowIfCancellationRequested();
        output.Write(buffer, 0, Check(lame_encode_flush(encoder, buffer, buffer.Length), "flushing the last frame"));

        // LAME reserves the first MPEG frame for the final seek table and gapless playback information.
        nuint tagBytes = lame_get_lametag_frame(encoder, buffer, (nuint)buffer.Length);
        if (tagBytes > (nuint)buffer.Length || tagBytes > (nuint)(output.Length - Id3Header.Length))
            throw new InvalidDataException("LAME returned an invalid seek frame length.");
        output.Position = Id3Header.Length;
        output.Write(buffer, 0, (int)tagBytes);
        return output.ToArray();
    }

    private static int Check(int result, string operation) => result >= 0 ? result :
        throw new InvalidDataException($"LAME failed while {operation} (error {result}).");

    private sealed class EncoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EncoderHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => lame_close(handle) == 0;
    }

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern EncoderHandle lame_init();

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_set_num_channels(EncoderHandle encoder, int channels);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_set_in_samplerate(EncoderHandle encoder, int sampleRate);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void lame_set_write_id3tag_automatic(EncoderHandle encoder, int enabled);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_init_params(EncoderHandle encoder);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_encode_buffer(EncoderHandle encoder, ref short left, ref short right,
        int frames, [Out] byte[] output, int capacity);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_encode_buffer_interleaved(EncoderHandle encoder, ref short samples,
        int frames, [Out] byte[] output, int capacity);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_encode_flush(EncoderHandle encoder, [Out] byte[] output, int capacity);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern nuint lame_get_lametag_frame(EncoderHandle encoder, [Out] byte[] output, nuint capacity);

    [DefaultDllImportSearchPaths(SearchPath), DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int lame_close(IntPtr encoder);
}
