using System.Runtime.InteropServices;

namespace Iw4Radiant.Audio;

/// <summary>Fixed-format XAudio2 graph for prepared Radiant previews.</summary>
internal sealed unsafe class WindowsAudioPreviewBackend : IPreviewAudioBackend
{
    private const int MaxVoices = 24;
    private const int SampleRate = 48000;
    private readonly object _sync = new();
    private readonly List<SourceVoice> _voices = [];
    private nint _engine;
    private nint _master;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindows();
    public string? UnavailableReason => IsSupported ? null : "Sound preview playback requires Windows or macOS.";

    public PreparedSound Prepare(byte[] audio)
    {
        if (!IsSupported) throw new PlatformNotSupportedException(UnavailableReason);
        byte[] pcm = WindowsPreviewDecoder.Decode(audio);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureGraphWithCom();
        }
        nint data = Marshal.AllocHGlobal(pcm.Length);
        try
        {
            Marshal.Copy(pcm, 0, data, pcm.Length);
            var buffer = new PcmBuffer(data, pcm.Length);
            data = 0;
            try
            {
                return new PreparedSound(buffer, pcm.Length, pcm.Length / (SampleRate * 8.0));
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }
        finally { if (data != 0) Marshal.FreeHGlobal(data); }
    }

    public PreviewVoice Play(PreparedSound sound, bool looping, float volume, float pan, float pitch)
    {
        if (sound.NativeBuffer is not PcmBuffer buffer)
            throw new ArgumentException("The sound belongs to another audio backend.", nameof(sound));
        if (!float.IsFinite(pitch) || pitch is < 0.0005f or > 16)
            throw new ArgumentOutOfRangeException(nameof(pitch), "Windows sound preview pitch must be between 0.0005 and 16.");
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SourceVoice? voice = _voices.FirstOrDefault(candidate => !candidate.Busy && IsDrained(candidate));
            if (voice is null)
                throw new InvalidOperationException("All prepared sound preview voices are in use.");
            voice.Busy = true;
            try
            {
                if (buffer.Data == 0)
                    throw new InvalidOperationException("The prepared sound was already released.");
                Reset(voice.Handle);
                Check(((delegate* unmanaged[Stdcall]<nint, float, uint, int>)Slot(voice.Handle, 26))
                    (voice.Handle, pitch, 0), "set sound pitch");
                SetVolume(voice.Handle, volume);
                SetPan(voice.Handle, pan);
                var submission = new XAudioBuffer
                {
                    Flags = 0x0040, // XAUDIO2_END_OF_STREAM
                    AudioBytes = checked((uint)buffer.Length),
                    AudioData = buffer.Data,
                    LoopCount = looping ? 255u : 0u // XAUDIO2_LOOP_INFINITE
                };
                Check(((delegate* unmanaged[Stdcall]<nint, XAudioBuffer*, nint, int>)Slot(voice.Handle, 21))
                    (voice.Handle, &submission, 0), "queue sound");
                voice.Sound = sound;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(voice.Handle, 19))
                    (voice.Handle, 0, 0), "start sound");
                return new WindowsVoice(this, voice, sound, looping);
            }
            catch
            {
                try { Reset(voice.Handle); }
                catch (InvalidOperationException) { /* Keep the buffer rooted until the queue drains. */ }
                voice.Busy = false;
                throw;
            }
        }
    }

    private static bool IsDrained(SourceVoice voice)
    {
        XAudioVoiceState state;
        ((delegate* unmanaged[Stdcall]<nint, XAudioVoiceState*, uint, void>)Slot(voice.Handle, 25))
            (voice.Handle, &state, 0);
        if (state.BuffersQueued != 0) return false;
        voice.Sound = null;
        return true;
    }

    private void EnsureGraphWithCom()
    {
        if (_engine != 0) return;
        int status = CoInitializeEx(0, 0);
        if (status < 0 && status != unchecked((int)0x80010106))
            throw new InvalidOperationException($"Could not initialize Windows audio (COM error 0x{status:X8}).");
        try { EnsureGraph(); }
        finally { if (status >= 0) CoUninitialize(); }
    }

    private void EnsureGraph()
    {
        if (_engine != 0) return;
        nint engine = 0, master = 0;
        try
        {
            Check(XAudio2Create(out engine, 0, 1), "initialize XAudio2");
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, uint, uint, uint, nint, nint, uint, int>)Slot(engine, 7))
                (engine, &master, 2, SampleRate, 0, 0, 0, 0), "open the default audio output");
            var format = new WaveFormat
            {
                Tag = 3, // WAVE_FORMAT_IEEE_FLOAT
                Channels = 2,
                SamplesPerSecond = SampleRate,
                AverageBytesPerSecond = SampleRate * 8,
                BlockAlign = 8,
                BitsPerSample = 32
            };
            for (int i = 0; i < MaxVoices; i++)
            {
                nint source = 0;
                int result = ((delegate* unmanaged[Stdcall]<nint, nint*, WaveFormat*, uint, float, nint, nint, nint, int>)Slot(engine, 5))
                    (engine, &source, &format, 0, 16, 0, 0, 0);
                if (result < 0)
                {
                    if (source != 0) DestroyVoice(source);
                    Check(result, "create a sound voice");
                }
                if (source == 0)
                    throw new InvalidOperationException("XAudio2 returned an empty sound voice.");
                _voices.Add(new SourceVoice(source));
            }
            _engine = engine;
            _master = master;
        }
        catch
        {
            DestroyVoices();
            if (master != 0) DestroyVoice(master);
            if (engine != 0) Release(engine);
            throw;
        }
    }

    private static void Reset(nint source)
    {
        Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(source, 20))(source, 0, 0),
            "stop sound");
        Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(source, 22))(source), "release queued sound");
    }

    private static void SetVolume(nint source, float volume) =>
        Check(((delegate* unmanaged[Stdcall]<nint, float, uint, int>)Slot(source, 12))
            (source, Math.Clamp(volume, 0, 1), 0), "set sound volume");

    private static void SetPan(nint source, float pan)
    {
        pan = Math.Clamp(pan, -1, 1);
        float* matrix = stackalloc float[4];
        // Matrix is destination-channel-major: LL, RL, LR, RR.
        matrix[0] = 1 - Math.Max(0, pan);
        matrix[1] = Math.Max(0, -pan);
        matrix[2] = Math.Max(0, pan);
        matrix[3] = 1 - Math.Max(0, -pan);
        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, float*, uint, int>)Slot(source, 16))
            (source, 0, 2, 2, matrix, 0), "set sound pan");
    }

    private void DestroyVoices()
    {
        foreach (SourceVoice voice in _voices)
        {
            DestroyVoice(voice.Handle);
            voice.Busy = false;
            voice.Sound = null;
        }
        _voices.Clear();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            DestroyVoices();
            if (_master != 0) DestroyVoice(_master);
            if (_engine != 0) Release(_engine);
            _master = _engine = 0;
        }
    }

    private static void DestroyVoice(nint voice) =>
        ((delegate* unmanaged[Stdcall]<nint, void>)Slot(voice, 18))(voice);

    private static void Release(nint unknown) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(unknown, 2))(unknown);

    // Windows SDK xaudio2.h: IXAudio2 has IUnknown slots; IXAudio2Voice begins at GetVoiceDetails.
    private static nint Slot(nint instance, int index) => ((nint*)*(nint*)instance)[index];

    private static void Check(int result, string operation)
    {
        if (result < 0)
            throw new InvalidOperationException($"Could not {operation} (XAudio2 error 0x{result:X8}).");
    }

    private sealed class SourceVoice(nint handle)
    {
        internal nint Handle { get; } = handle;
        internal bool Busy;
        internal PreparedSound? Sound;
    }

    private sealed class PcmBuffer(nint data, int length) : IDisposable
    {
        private nint _data = data;
        internal nint Data => _data;
        internal int Length { get; } = length;
        ~PcmBuffer() => Dispose();
        public void Dispose()
        {
            nint data = Interlocked.Exchange(ref _data, 0);
            if (data != 0) Marshal.FreeHGlobal(data);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class WindowsVoice(WindowsAudioPreviewBackend owner, SourceVoice voice,
        PreparedSound sound, bool looping) : PreviewVoice
    {
        private bool _paused;
        private bool _disposed;
        // Retain the prepared buffer until this voice is stopped and released.
        private readonly PreparedSound _sound = sound;

        internal override bool HasEnded
        {
            get
            {
                lock (owner._sync)
                {
                    if (_disposed || owner._disposed) return true;
                    if (looping || _paused) return false;
                    XAudioVoiceState state;
                    ((delegate* unmanaged[Stdcall]<nint, XAudioVoiceState*, uint, void>)Slot(voice.Handle, 25))
                        (voice.Handle, &state, 0);
                    return state.BuffersQueued == 0;
                }
            }
        }

        internal override void Play()
        {
            lock (owner._sync)
            {
                if (_disposed || owner._disposed || !_paused) return;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(voice.Handle, 19))
                    (voice.Handle, 0, 0), "resume sound");
                _paused = false;
            }
        }

        internal override void Pause()
        {
            lock (owner._sync)
            {
                if (_disposed || owner._disposed || _paused) return;
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(voice.Handle, 20))
                    (voice.Handle, 0, 0), "pause sound");
                _paused = true;
            }
        }

        internal override void SetVolume(float volume)
        {
            lock (owner._sync)
                if (!_disposed && !owner._disposed) WindowsAudioPreviewBackend.SetVolume(voice.Handle, volume);
        }

        internal override void SetPan(float pan)
        {
            lock (owner._sync)
                if (!_disposed && !owner._disposed) WindowsAudioPreviewBackend.SetPan(voice.Handle, pan);
        }

        public override void Dispose()
        {
            lock (owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                try { if (!owner._disposed) Reset(voice.Handle); }
                finally
                {
                    voice.Busy = false;
                    GC.KeepAlive(_sound);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        internal ushort Tag, Channels;
        internal uint SamplesPerSecond, AverageBytesPerSecond;
        internal ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    // xaudio2.h brackets its voice/buffer declarations with #pragma pack(push, 1).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct XAudioBuffer
    {
        internal uint Flags, AudioBytes;
        internal nint AudioData;
        internal uint PlayBegin, PlayLength, LoopBegin, LoopLength, LoopCount;
        internal nint Context;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct XAudioVoiceState
    {
        internal nint CurrentBufferContext;
        internal uint BuffersQueued;
        internal ulong SamplesPlayed;
    }

    [DllImport("xaudio2_9.dll", ExactSpelling = true)]
    private static extern int XAudio2Create(out nint engine, uint flags, uint processor);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint coInit);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
