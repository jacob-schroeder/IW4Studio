using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IW4.Studio.Desktop.Editors.Sound;

internal sealed class SoundPreviewPlayer : IDisposable
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AvfAudioFramework =
        "/System/Library/Frameworks/AVFAudio.framework/AVFAudio";
    private const string AvFoundationFramework =
        "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    private static readonly Lazy<PlatformSupport> Platform = new(
        DetectPlatformSupport,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static IntPtr s_audioFrameworkHandle;

    private readonly object _sync = new();
    private IntPtr _player;
    private long _playStartedTimestamp;
    private double _playStartedAtSeconds;
    private bool _hasStarted;
    private bool _isPaused;
    private bool _disposed;

    public SoundPreviewPlayer(ReadOnlySpan<byte> data)
    {
        PlatformSupport support = Platform.Value;
        if (!support.IsSupported)
        {
            throw new PlatformNotSupportedException(
                support.UnavailableReason ?? "Sound preview playback is unavailable.");
        }

        if (data.IsEmpty)
            throw new ArgumentException("Sound preview data cannot be empty.", nameof(data));

        IntPtr nativeData = IntPtr.Zero;
        try
        {
            nativeData = CreateData(support.DataClass, data);
            if (nativeData == IntPtr.Zero)
                throw new InvalidOperationException("NSData could not copy the sound preview data.");

            IntPtr allocatedPlayer = SendIntPtr(
                support.AudioPlayerClass,
                Selectors.Alloc);
            if (allocatedPlayer == IntPtr.Zero)
                throw new InvalidOperationException("AVAudioPlayer could not be allocated.");

            IntPtr error;
            _player = SendIntPtrWithObjectAndOutObject(
                allocatedPlayer,
                Selectors.InitWithDataError,
                nativeData,
                out error);
            if (_player == IntPtr.Zero)
            {
                string errorDescription = GetErrorDescription(error);
                throw new InvalidDataException(
                    string.IsNullOrWhiteSpace(errorDescription)
                        ? "AVAudioPlayer could not decode the MPEG sound data."
                        : $"AVAudioPlayer could not decode the MPEG sound data: {errorDescription}");
            }

            SendVoidWithByte(_player, Selectors.SetMeteringEnabled, 1);
            if (!SendBool(_player, Selectors.PrepareToPlay))
            {
                throw new InvalidDataException(
                    "AVAudioPlayer could not prepare the MPEG sound data for playback.");
            }
        }
        catch
        {
            ReleasePlayer();
            throw;
        }
        finally
        {
            if (nativeData != IntPtr.Zero)
                SendVoid(nativeData, Selectors.Release);
        }
    }

    ~SoundPreviewPlayer()
    {
        Dispose(disposing: false);
    }

    public static bool IsPlatformSupported => Platform.Value.IsSupported;

    public static string? UnavailableReason => Platform.Value.UnavailableReason;

    internal static SoundPreviewPlayer? TryCreateWarmup()
    {
        try
        {
            if (!IsPlatformSupported)
                return null;

            return new SoundPreviewPlayer(CreateWarmupWaveData());
        }
        catch
        {
            return null;
        }
    }

    public bool HasEnded
    {
        get
        {
            lock (_sync)
            {
                return !_disposed && HasEndedCore();
            }
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (SendBool(_player, Selectors.IsPlaying))
                return;

            if (HasEndedCore())
                SendVoidWithDouble(_player, Selectors.SetCurrentTime, 0);

            double startTime = SendDouble(_player, Selectors.CurrentTime);
            if (!SendBool(_player, Selectors.Play))
                throw new InvalidOperationException("AVAudioPlayer could not start playback.");

            _hasStarted = true;
            _isPaused = false;
            _playStartedAtSeconds = Math.Max(0, startTime);
            _playStartedTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!SendBool(_player, Selectors.IsPlaying))
                return;

            SendVoid(_player, Selectors.Pause);
            _isPaused = true;
        }
    }

    public void Restart()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            SendVoid(_player, Selectors.Stop);
            SendVoidWithDouble(_player, Selectors.SetCurrentTime, 0);
            if (!SendBool(_player, Selectors.Play))
                throw new InvalidOperationException("AVAudioPlayer could not restart playback.");

            _hasStarted = true;
            _isPaused = false;
            _playStartedAtSeconds = 0;
            _playStartedTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public double ReadLevel()
    {
        lock (_sync)
        {
            if (_disposed || !SendBool(_player, Selectors.IsPlaying))
                return 0;

            SendVoid(_player, Selectors.UpdateMeters);
            nint channelCount = SendNInt(_player, Selectors.NumberOfChannels);
            if (channelCount <= 0)
                return 0;

            double power = 0;
            for (nint channel = 0; channel < channelCount; channel++)
            {
                float decibels = SendFloatWithNInt(
                    _player,
                    Selectors.AveragePowerForChannel,
                    channel);
                if (!float.IsFinite(decibels))
                    continue;

                power += Math.Pow(10, Math.Clamp(decibels, -160, 0) / 10.0);
            }

            return Math.Clamp(Math.Sqrt(power / (double)channelCount), 0, 1);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleasePlayer();
        }
    }

    private bool HasEndedCore()
    {
        if (!_hasStarted || _isPaused || SendBool(_player, Selectors.IsPlaying))
            return false;

        double duration = SendDouble(_player, Selectors.Duration);
        if (!double.IsFinite(duration) || duration <= 0)
            return false;

        double currentTime = SendDouble(_player, Selectors.CurrentTime);
        double elapsedTime = _playStartedAtSeconds;
        if (_playStartedTimestamp != 0)
        {
            elapsedTime += Stopwatch.GetElapsedTime(
                _playStartedTimestamp,
                Stopwatch.GetTimestamp()).TotalSeconds;
        }

        double endTolerance = Math.Min(0.05, duration * 0.01);
        return currentTime >= duration - endTolerance ||
               elapsedTime >= duration - endTolerance;
    }

    private void ReleasePlayer()
    {
        if (_player == IntPtr.Zero)
            return;

        SendVoid(_player, Selectors.Stop);
        SendVoid(_player, Selectors.Release);
        _player = IntPtr.Zero;
    }

    private static byte[] CreateWarmupWaveData()
    {
        const int headerSize = 44;
        const short channelCount = 1;
        const short bitsPerSample = 16;
        const int sampleRate = 8_000;
        const int sampleCount = 80;
        const int bytesPerSample = bitsPerSample / 8;
        const int dataLength = sampleCount * channelCount * bytesPerSample;

        byte[] waveData = new byte[headerSize + dataLength];
        "RIFF"u8.CopyTo(waveData);
        BinaryPrimitives.WriteInt32LittleEndian(
            waveData.AsSpan(4),
            waveData.Length - 8);
        "WAVEfmt "u8.CopyTo(waveData.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(22), channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(
            waveData.AsSpan(28),
            sampleRate * channelCount * bytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(
            waveData.AsSpan(32),
            channelCount * bytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(waveData.AsSpan(34), bitsPerSample);
        "data"u8.CopyTo(waveData.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(waveData.AsSpan(40), dataLength);
        return waveData;
    }

    private static unsafe IntPtr CreateData(
        IntPtr dataClass,
        ReadOnlySpan<byte> data)
    {
        IntPtr allocatedData = SendIntPtr(dataClass, Selectors.Alloc);
        if (allocatedData == IntPtr.Zero)
            return IntPtr.Zero;

        fixed (byte* dataPointer = data)
        {
            return SendIntPtrWithBytesAndLength(
                allocatedData,
                Selectors.InitWithBytesLength,
                (IntPtr)dataPointer,
                (nuint)data.Length);
        }
    }

    private static string GetErrorDescription(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return string.Empty;

        IntPtr description = SendIntPtr(error, Selectors.LocalizedDescription);
        if (description == IntPtr.Zero)
            return string.Empty;

        IntPtr utf8String = SendIntPtr(description, Selectors.Utf8String);
        return utf8String == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringUTF8(utf8String) ?? string.Empty;
    }

    private static PlatformSupport DetectPlatformSupport()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new PlatformSupport(
                false,
                "Sound preview playback currently requires macOS.",
                IntPtr.Zero,
                IntPtr.Zero);
        }

        try
        {
            if (!NativeLibrary.TryLoad(AvfAudioFramework, out s_audioFrameworkHandle) &&
                !NativeLibrary.TryLoad(AvFoundationFramework, out s_audioFrameworkHandle))
            {
                return new PlatformSupport(
                    false,
                    "The macOS AVAudioPlayer framework could not be loaded.",
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            IntPtr audioPlayerClass = GetClass("AVAudioPlayer");
            IntPtr dataClass = GetClass("NSData");
            if (audioPlayerClass == IntPtr.Zero || dataClass == IntPtr.Zero)
            {
                return new PlatformSupport(
                    false,
                    "The macOS AVAudioPlayer runtime is unavailable.",
                    IntPtr.Zero,
                    IntPtr.Zero);
            }

            return new PlatformSupport(
                true,
                null,
                audioPlayerClass,
                dataClass);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException)
        {
            return new PlatformSupport(
                false,
                "The macOS AVAudioPlayer runtime could not be initialized.",
                IntPtr.Zero,
                IntPtr.Zero);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record PlatformSupport(
        bool IsSupported,
        string? UnavailableReason,
        IntPtr AudioPlayerClass,
        IntPtr DataClass);

    private static class Selectors
    {
        public static readonly IntPtr Alloc = RegisterSelector("alloc");
        public static readonly IntPtr InitWithBytesLength =
            RegisterSelector("initWithBytes:length:");
        public static readonly IntPtr InitWithDataError =
            RegisterSelector("initWithData:error:");
        public static readonly IntPtr PrepareToPlay = RegisterSelector("prepareToPlay");
        public static readonly IntPtr SetMeteringEnabled =
            RegisterSelector("setMeteringEnabled:");
        public static readonly IntPtr IsPlaying = RegisterSelector("isPlaying");
        public static readonly IntPtr Play = RegisterSelector("play");
        public static readonly IntPtr Pause = RegisterSelector("pause");
        public static readonly IntPtr Stop = RegisterSelector("stop");
        public static readonly IntPtr CurrentTime = RegisterSelector("currentTime");
        public static readonly IntPtr SetCurrentTime = RegisterSelector("setCurrentTime:");
        public static readonly IntPtr Duration = RegisterSelector("duration");
        public static readonly IntPtr UpdateMeters = RegisterSelector("updateMeters");
        public static readonly IntPtr NumberOfChannels = RegisterSelector("numberOfChannels");
        public static readonly IntPtr AveragePowerForChannel =
            RegisterSelector("averagePowerForChannel:");
        public static readonly IntPtr LocalizedDescription =
            RegisterSelector("localizedDescription");
        public static readonly IntPtr Utf8String = RegisterSelector("UTF8String");
        public static readonly IntPtr Release = RegisterSelector("release");
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrWithBytesAndLength(
        IntPtr receiver,
        IntPtr selector,
        IntPtr bytes,
        nuint length);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrWithObjectAndOutObject(
        IntPtr receiver,
        IntPtr selector,
        IntPtr value,
        out IntPtr error);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidWithByte(
        IntPtr receiver,
        IntPtr selector,
        byte value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidWithDouble(
        IntPtr receiver,
        IntPtr selector,
        double value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendNInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern float SendFloatWithNInt(
        IntPtr receiver,
        IntPtr selector,
        nint value);
}
