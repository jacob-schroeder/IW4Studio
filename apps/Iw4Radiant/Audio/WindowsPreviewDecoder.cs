using System.Runtime.InteropServices;

namespace Iw4Radiant.Audio;

/// <summary>Media Foundation decoding and conversion, confined to the preparation worker.</summary>
internal static unsafe class WindowsPreviewDecoder
{
    private const uint AudioStream = 0xfffffffd; // MF_SOURCE_READER_FIRST_AUDIO_STREAM
    private const int MaxDecodedBytes = 32 * 1024 * 1024;
    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid AudioType = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatType = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid Rate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid Bits = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid Alignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid BytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid AttributesInterface = new("2cd2d921-c447-44a7-a13c-4adabfc247e3");
    private static readonly Guid ContentType = new("fc358289-3cb6-460c-a424-b6681260375a");

    internal static byte[] Decode(byte[] audio)
    {
        if (audio.Length == 0) throw new InvalidDataException("The sound contains no audio data.");
        int comStatus = CoInitializeEx(0, 0); // MTA; the loading worker normally already uses MTA.
        if (comStatus < 0 && comStatus != unchecked((int)0x80010106))
            throw Error(comStatus, "initialize the Windows audio decoder");
        bool releaseCom = comStatus >= 0;
        bool started = false;
        nint stream = 0, byteStream = 0, reader = 0, mediaType = 0;
        try
        {
            Require(MFStartup(0x20070, 0), "start Media Foundation");
            started = true;
            fixed (byte* input = audio)
                stream = SHCreateMemStream(input, checked((uint)audio.Length));
            if (stream == 0) throw new InvalidOperationException("Could not open the sound in memory.");
            Require(MFCreateMFByteStreamOnStream(stream, out byteStream), "open the sound byte stream");
            SetMpegContentType(byteStream);
            Require(MFCreateSourceReaderFromByteStream(byteStream, 0, out reader), "read the sound format");
            Require(MFCreateMediaType(out mediaType), "create the decoded audio format");
            SetGuid(mediaType, MajorType, AudioType);
            SetGuid(mediaType, Subtype, FloatType);
            SetNumber(mediaType, Channels, 2);
            SetNumber(mediaType, Rate, 48000);
            SetNumber(mediaType, Bits, 32);
            SetNumber(mediaType, Alignment, 8);
            SetNumber(mediaType, BytesPerSecond, 48000 * 8);
            Require(((delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)Slot(reader, 7))
                (reader, AudioStream, 0, mediaType), "convert audio to 48 kHz stereo float PCM");

            using var output = new MemoryStream();
            int emptyReads = 0;
            while (true)
            {
                nint sample = 0;
                uint flags = 0;
                try
                {
                    Require(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slot(reader, 9))
                        (reader, AudioStream, 0, null, &flags, null, &sample), "decode audio frames");
                    if ((flags & 0x1) != 0)
                        throw new InvalidDataException("The Windows audio decoder reported a stream error.");
                    if ((flags & 0x20) != 0)
                        throw new InvalidDataException("The sound changed format during preview decoding.");
                    if (sample != 0)
                    {
                        long previousLength = output.Length;
                        AppendSample(sample, output);
                        if (output.Length > previousLength) emptyReads = 0;
                        else if (++emptyReads > 64)
                            throw new InvalidDataException("The sound produced no decodable audio frames.");
                    }
                    else if ((flags & 0x2) == 0 && ++emptyReads > 64)
                        throw new InvalidDataException("The sound produced no decodable audio frames.");
                    if ((flags & 0x2) != 0) break; // MF_SOURCE_READERF_ENDOFSTREAM
                }
                finally { Release(sample); }
            }
            if (output.Length == 0 || output.Length % 8 != 0)
                throw new InvalidDataException("The sound contains no complete stereo audio frames.");
            return output.ToArray();
        }
        finally
        {
            Release(mediaType);
            Release(reader);
            Release(byteStream);
            Release(stream);
            if (started) MFShutdown();
            if (releaseCom) CoUninitialize();
        }
    }

    private static void SetMpegContentType(nint byteStream)
    {
        nint attributes = 0;
        Guid interfaceId = AttributesInterface;
        try
        {
            Require(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(byteStream, 0))
                (byteStream, &interfaceId, &attributes), "identify the MPEG byte stream");
            const string mime = "audio/mpeg";
            Guid key = ContentType;
            fixed (char* value = mime)
                Require(((delegate* unmanaged[Stdcall]<nint, Guid*, char*, int>)Slot(attributes, 25))
                    (attributes, &key, value), "identify the MPEG byte stream");
        }
        finally { Release(attributes); }
    }

    private static void AppendSample(nint sample, MemoryStream output)
    {
        nint buffer = 0;
        try
        {
            Require(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(sample, 41))
                (sample, &buffer), "assemble decoded audio");
            byte* data = null;
            uint maxLength = 0, length = 0;
            Require(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))
                (buffer, &data, &maxLength, &length), "access decoded audio");
            try
            {
                if (length > maxLength || length % 8 != 0 || output.Length + length > MaxDecodedBytes)
                    throw new InvalidDataException("Decoded sound exceeds the preview's 32 MiB per-sound limit or has incomplete frames.");
                output.Write(new ReadOnlySpan<byte>(data, checked((int)length)));
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer, 4))(buffer);
            }
        }
        finally { Release(buffer); }
    }

    private static void SetGuid(nint attributes, Guid key, Guid value)
    {
        Require(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 24))
            (attributes, &key, &value), "describe decoded audio");
    }

    private static void SetNumber(nint attributes, Guid key, uint value)
    {
        Require(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(attributes, 21))
            (attributes, &key, value), "describe decoded audio");
    }

    private static void Release(nint unknown)
    {
        if (unknown != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(unknown, 2))(unknown);
    }

    // Windows SDK mfobjects.h/mfreadwrite.h: IUnknown is slots 0-2 on each MF interface.
    private static nint Slot(nint instance, int index) => ((nint*)*(nint*)instance)[index];

    private static void Require(int result, string operation)
    {
        if (result < 0) throw Error(result, operation);
    }

    private static InvalidDataException Error(int result, string operation) =>
        new($"Could not {operation} (Media Foundation error 0x{result:X8}).");

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint coInit);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("shlwapi.dll", ExactSpelling = true)]
    private static extern nint SHCreateMemStream(byte* initial, uint length);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMFByteStreamOnStream(nint stream, out nint byteStream);
    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromByteStream(nint byteStream, nint attributes, out nint reader);
}
