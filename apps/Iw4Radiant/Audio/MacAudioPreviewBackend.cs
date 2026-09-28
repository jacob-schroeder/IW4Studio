using System.Diagnostics;
using System.Runtime.InteropServices;
using Iw4Radiant.Views;

namespace Iw4Radiant.Audio;

/// <summary>Fixed-format AVAudioEngine execution; no native type escapes the Radiant audio boundary.</summary>
internal sealed class MacAudioPreviewBackend : IPreviewAudioBackend
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AvfAudio = "/System/Library/Frameworks/AVFAudio.framework/AVFAudio";
    private const int MaxVoices = 24;
    private static readonly Lazy<string?> Support = new(DetectSupport);
    private static nint s_framework;
    private readonly object _sync = new();
    private readonly List<Node> _nodes = [];
    private nint _engine;
    private nint _format;
    private bool _disposed;

    // Keep native framework loading and class lookup off the UI path.
    public bool IsSupported => OperatingSystem.IsMacOS();
    public string? UnavailableReason => !OperatingSystem.IsMacOS()
        ? "Sound preview playback currently requires macOS."
        : Support.IsValueCreated ? Support.Value : null;

    public unsafe PreparedSound Prepare(byte[] audio)
    {
        string? support = Support.Value;
        if (support is not null) throw new PlatformNotSupportedException(support);
        SoundPreviewPitch.EnginePcm pcm = SoundPreviewPitch.DecodeForEngine(audio);
        // Subsequent preparations allocate only independent PCM buffers. The graph is fixed at first prepare.
        nint format;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureEngine();
            format = ObjC.SendPtr(_format, S.Retain);
        }
        nint nativeBuffer = 0;
        try
        {
            nativeBuffer = ObjC.SendPtrPtrUInt(ObjC.SendPtr(ObjC.Class("AVAudioPCMBuffer"), S.Alloc),
                S.InitWithFormatCapacity, format, pcm.Frames);
            if (nativeBuffer == 0) throw new InvalidDataException("AVAudioPCMBuffer allocation failed.");
            ObjC.SendVoidUInt(nativeBuffer, S.SetFrameLength, pcm.Frames);
            nint channelPointers = ObjC.SendPtr(nativeBuffer, S.FloatChannelData);
            if (channelPointers == 0)
                throw new InvalidDataException("AVAudioPCMBuffer has no float channels.");
            nint left = Marshal.ReadIntPtr(channelPointers, 0);
            nint right = Marshal.ReadIntPtr(channelPointers, IntPtr.Size);
            if (left == 0 || right == 0)
                throw new InvalidDataException("AVAudioPCMBuffer has no stereo float storage.");
            fixed (byte* data = pcm.Bytes)
            {
                float* samples = (float*)data;
                float* leftSamples = (float*)left;
                float* rightSamples = (float*)right;
                for (int frame = 0; frame < pcm.Frames; frame++)
                {
                    leftSamples[frame] = samples[frame * 2];
                    rightSamples[frame] = samples[frame * 2 + 1];
                }
            }
            lock (_sync) ObjectDisposedException.ThrowIf(_disposed, this);
            PreparedSound prepared = new(new MacBuffer(nativeBuffer), pcm.Bytes.Length, pcm.Frames / 48000.0);
            nativeBuffer = 0;
            return prepared;
        }
        finally
        {
            if (nativeBuffer != 0) ObjC.SendVoid(nativeBuffer, S.Release);
            ObjC.SendVoid(format, S.Release);
        }
    }

    public PreviewVoice Play(PreparedSound sound, bool looping, float volume, float pan)
    {
        if (sound.NativeBuffer is not MacBuffer buffer)
            throw new ArgumentException("The sound belongs to another audio backend.", nameof(sound));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Node? node = _nodes.FirstOrDefault(candidate => !candidate.Busy);
            if (node is null)
                throw new InvalidOperationException("All prepared sound preview voices are in use.");
            node.Busy = true;
            try
            {
                ObjC.SendVoid(node.Handle, S.Stop);
                ObjC.SendVoidFloat(node.Handle, S.SetVolume, Math.Clamp(volume, 0, 1));
                ObjC.SendVoidFloat(node.Handle, S.SetPan, Math.Clamp(pan, -1, 1));
                ObjC.SendSchedule(node.Handle, S.ScheduleBuffer, buffer.Handle, 0, (nuint)(looping ? 1 : 0), 0);
                ObjC.SendVoid(node.Handle, S.Play);
                return new MacVoice(this, node, sound, looping);
            }
            catch { node.Busy = false; throw; }
        }
    }

    private void EnsureEngine()
    {
        if (_engine != 0) return;
        nint format = 0, engine = 0;
        try
        {
            format = ObjC.SendFormat(ObjC.SendPtr(ObjC.Class("AVAudioFormat"), S.Alloc),
                S.InitFormat, 1, 48000, 2, 0);
            if (format == 0) throw new InvalidOperationException("AVAudioFormat initialization failed.");
            engine = ObjC.SendPtr(ObjC.SendPtr(ObjC.Class("AVAudioEngine"), S.Alloc), S.Init);
            if (engine == 0) throw new InvalidOperationException("AVAudioEngine initialization failed.");
            nint mixer = ObjC.SendPtr(engine, S.MainMixerNode);
            for (int i = 0; i < MaxVoices; i++)
            {
                nint player = ObjC.SendPtr(ObjC.SendPtr(ObjC.Class("AVAudioPlayerNode"), S.Alloc), S.Init);
                if (player == 0) throw new InvalidOperationException("AVAudioPlayerNode allocation failed.");
                try
                {
                    ObjC.SendVoidPtr(engine, S.AttachNode, player);
                    ObjC.SendVoidPtrPtrPtr(engine, S.ConnectToFormat, player, mixer, format);
                    ObjC.SendVoidUInt(player, S.PrepareWithFrameCount, 4096);
                    _nodes.Add(new Node(player));
                }
                catch
                {
                    ObjC.SendVoidPtr(engine, S.DetachNode, player);
                    ObjC.SendVoid(player, S.Release);
                    throw;
                }
            }
            ObjC.SendVoid(engine, S.Prepare);
            if (!ObjC.SendBoolOut(engine, S.StartAndReturnError, out nint error))
                throw new InvalidOperationException("AVAudioEngine could not start: " + Error(error));
            _format = format;
            _engine = engine;
        }
        catch
        {
            foreach (Node node in _nodes)
            {
                ObjC.SendVoidPtr(engine, S.DetachNode, node.Handle);
                ObjC.SendVoid(node.Handle, S.Release);
            }
            _nodes.Clear();
            if (engine != 0) ObjC.SendVoid(engine, S.Release);
            if (format != 0) ObjC.SendVoid(format, S.Release);
            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Node node in _nodes)
            {
                ObjC.SendVoid(node.Handle, S.Stop);
                ObjC.SendVoidPtr(_engine, S.DetachNode, node.Handle);
                ObjC.SendVoid(node.Handle, S.Release);
            }
            _nodes.Clear();
            if (_engine != 0)
            {
                ObjC.SendVoid(_engine, S.Stop);
                ObjC.SendVoid(_engine, S.Release);
                _engine = 0;
            }
            if (_format != 0)
            {
                ObjC.SendVoid(_format, S.Release);
                _format = 0;
            }
        }
    }

    private static string? DetectSupport()
    {
        if (!OperatingSystem.IsMacOS()) return "Sound preview playback currently requires macOS.";
        if (!NativeLibrary.TryLoad(AvfAudio, out s_framework))
            return "The macOS AVFAudio framework could not be loaded.";
        return ObjC.Class("AVAudioEngine") == 0 || ObjC.Class("AVAudioFormat") == 0 ||
            ObjC.Class("AVAudioPlayerNode") == 0 || ObjC.Class("AVAudioPCMBuffer") == 0
            ? "The macOS AVAudioEngine runtime is unavailable." : null;
    }

    private static string Error(nint error)
    {
        if (error == 0) return "unknown native error";
        nint description = ObjC.SendPtr(error, S.LocalizedDescription);
        nint chars = ObjC.SendPtr(description, S.Utf8String);
        return Marshal.PtrToStringUTF8(chars) ?? "unknown native error";
    }

    private sealed class Node(nint handle)
    {
        internal nint Handle { get; } = handle;
        internal bool Busy;
    }

    private sealed class MacBuffer(nint handle) : IDisposable
    {
        private nint _handle = handle;
        internal nint Handle => _handle;
        ~MacBuffer() => Dispose();
        public void Dispose()
        {
            nint nativeHandle = Interlocked.Exchange(ref _handle, 0);
            if (nativeHandle != 0) ObjC.SendVoid(nativeHandle, S.Release);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class MacVoice(MacAudioPreviewBackend owner, Node node, PreparedSound sound,
        bool looping) : PreviewVoice
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _elapsed;
        private bool _paused;
        private bool _disposed;
        internal override bool HasEnded => !_disposed && !looping && !_paused &&
            _elapsed + _clock.Elapsed.TotalSeconds >= sound.Duration;
        internal override void Play()
        {
            lock (owner._sync)
            {
                if (_disposed || owner._disposed || !_paused) return;
                ObjC.SendVoid(node.Handle, S.Play);
                _paused = false;
                _clock.Restart();
            }
        }
        internal override void Pause()
        {
            lock (owner._sync)
            {
                if (_disposed || owner._disposed || _paused) return;
                ObjC.SendVoid(node.Handle, S.Pause);
                _elapsed += _clock.Elapsed.TotalSeconds;
                _clock.Stop();
                _paused = true;
            }
        }
        internal override void SetVolume(float volume)
        {
            lock (owner._sync)
                if (!_disposed && !owner._disposed)
                    ObjC.SendVoidFloat(node.Handle, S.SetVolume, Math.Clamp(volume, 0, 1));
        }
        internal override void SetPan(float pan)
        {
            lock (owner._sync)
                if (!_disposed && !owner._disposed)
                    ObjC.SendVoidFloat(node.Handle, S.SetPan, Math.Clamp(pan, -1, 1));
        }
        public override void Dispose()
        {
            lock (owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (!owner._disposed) ObjC.SendVoid(node.Handle, S.Stop);
                node.Busy = false;
            }
        }
    }

    private static class S
    {
        internal static readonly nint Alloc = ObjC.Selector("alloc"), Init = ObjC.Selector("init"),
            Release = ObjC.Selector("release"), Retain = ObjC.Selector("retain"),
            InitFormat = ObjC.Selector("initWithCommonFormat:sampleRate:channels:interleaved:"),
            InitWithFormatCapacity = ObjC.Selector("initWithPCMFormat:frameCapacity:"),
            FloatChannelData = ObjC.Selector("floatChannelData"),
            SetFrameLength = ObjC.Selector("setFrameLength:"),
            StartAndReturnError = ObjC.Selector("startAndReturnError:"),
            MainMixerNode = ObjC.Selector("mainMixerNode"), AttachNode = ObjC.Selector("attachNode:"),
            DetachNode = ObjC.Selector("detachNode:"), ConnectToFormat = ObjC.Selector("connect:to:format:"),
            PrepareWithFrameCount = ObjC.Selector("prepareWithFrameCount:"), Prepare = ObjC.Selector("prepare"),
            Stop = ObjC.Selector("stop"), Play = ObjC.Selector("play"), Pause = ObjC.Selector("pause"),
            SetVolume = ObjC.Selector("setVolume:"), SetPan = ObjC.Selector("setPan:"),
            ScheduleBuffer = ObjC.Selector("scheduleBuffer:atTime:options:completionHandler:"),
            LocalizedDescription = ObjC.Selector("localizedDescription"), Utf8String = ObjC.Selector("UTF8String");
    }

    private static class ObjC
    {
        [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
        internal static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
        internal static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern nint SendPtr(nint receiver, nint selector);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern nint SendPtrPtr(nint receiver, nint selector, nint value);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern nint SendPtrPtrUInt(nint receiver, nint selector, nint value, uint count);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern nint SendFormat(nint receiver, nint selector, nuint commonFormat,
            double sampleRate, uint channels, byte interleaved);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SendBoolOut(nint receiver, nint selector, out nint error);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoid(nint receiver, nint selector);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidPtr(nint receiver, nint selector, nint value);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidPtrPtrPtr(nint receiver, nint selector, nint first, nint second, nint third);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidUInt(nint receiver, nint selector, uint value);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendVoidFloat(nint receiver, nint selector, float value);
        [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
        internal static extern void SendSchedule(nint receiver, nint selector, nint buffer, nint time, nuint options, nint completion);
    }
}
