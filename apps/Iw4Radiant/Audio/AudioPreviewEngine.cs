using Iw4Radiant.MapSource;
using Iw4Radiant.Views;

namespace Iw4Radiant.Audio;

/// <summary>Shared, bounded preparation and playback for Radiant sound previews.</summary>
internal sealed class AudioPreviewEngine : IDisposable
{
    private const int MaxPreparedSounds = 96;
    private const long MaxDecodedBytes = 128L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _preparationSlots = new(2);
    private readonly IPreviewAudioBackend _backend;
    private readonly SoundPreviewRandom _random = new();
    private readonly Dictionary<SourceKey, Task<PreparedPreview>> _preparations = [];
    private readonly LinkedList<SourceKey> _recent = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.Ordinal);
    private long _cachedBytes;
    private int _generation;
    private bool _disposed;

    internal AudioPreviewEngine() : this(OperatingSystem.IsMacOS()
        ? new MacAudioPreviewBackend()
        : OperatingSystem.IsWindows() ? new WindowsAudioPreviewBackend() : new UnavailablePreviewBackend()) { }

    private AudioPreviewEngine(IPreviewAudioBackend backend) => _backend = backend;

    internal bool IsSupported => _backend.IsSupported;
    internal string? UnavailableReason => _backend.UnavailableReason;
    internal int Generation { get { lock (_sync) return _generation; } }

    internal Task<PreparedPreview> PrepareAsync(string rawRoot, string exactAliasName,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
            return Task.FromResult(new PreparedPreview([], UnavailableReason));
        SourceKey key = MakeKey(rawRoot, exactAliasName);
        Task<PreparedPreview> task;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            WatchRoot(key.Root);
            if (!_preparations.TryGetValue(key, out task!))
            {
                while (_preparations.Count >= MaxPreparedSounds && EvictOldestCompleted()) { }
                if (_preparations.Count >= MaxPreparedSounds)
                    return Task.FromResult(new PreparedPreview([],
                        "Sound preview preparation is busy; try this sound again shortly."));
                int generation = _generation;
                task = PrepareCoreAsync(key, generation);
                _preparations.Add(key, task);
            }
            _recent.Remove(key);
            _recent.AddLast(key);
        }
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    internal bool TryGetPrepared(string rawRoot, string exactAliasName,
        out PreparedPreview preview)
    {
        SourceKey key = MakeKey(rawRoot, exactAliasName);
        lock (_sync)
        {
            if (!_disposed && _preparations.TryGetValue(key, out Task<PreparedPreview>? task) &&
                task.IsCompletedSuccessfully)
            {
                _recent.Remove(key);
                _recent.AddLast(key);
                preview = task.Result;
                return true;
            }
        }
        preview = new PreparedPreview([], null);
        return false;
    }

    private async Task<PreparedPreview> PrepareCoreAsync(SourceKey key, int generation)
    {
        await _preparationSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var catalogues = new Dictionary<string, PreparedPreview>(StringComparer.Ordinal);
                var sounds = new List<PreparedSound>();
                long decodedBytes = 0;
                try
                {
                    PreparedPreview PrepareAlias(string name)
                    {
                        if (catalogues.TryGetValue(name, out PreparedPreview? cached)) return cached;
                        if (catalogues.Count >= MaxPreparedSounds)
                            throw new InvalidDataException("This alias has too many secondary or chain references to preview.");
                        var loaded = SoundAliasAudition.LoadVariants(key.Root, name);
                        if (loaded.Error is not null) throw new InvalidDataException($"{name}: {loaded.Error}");
                        PreparedPreview.Validate(loaded.Variants.Select(variant => variant.Alias));
                        var variants = new List<PreparedVariant>();
                        foreach (var variant in loaded.Variants)
                        {
                            if (sounds.Count >= MaxPreparedSounds)
                                throw new InvalidDataException("This alias has more than the preview's 96 prepared variants and linked sounds.");
                            PreparedSound sound = _backend.Prepare(variant.Audio);
                            sounds.Add(sound);
                            variants.Add(new PreparedVariant(variant.Alias, sound));
                            decodedBytes += sound.DecodedBytes;
                            if (decodedBytes > MaxDecodedBytes)
                                throw new InvalidDataException("This alias's decoded variants exceed the preview's 128 MiB limit.");
                        }
                        var prepared = new PreparedPreview(variants.ToArray(), null);
                        // Insert before following references; native secondary recursion has a playback depth limit.
                        catalogues.Add(name, prepared);
                        foreach (PreparedVariant variant in variants)
                        {
                            if (!string.IsNullOrEmpty(variant.Alias.SecondaryAliasName))
                                variant.Secondary = PrepareAlias(variant.Alias.SecondaryAliasName);
                            if (!string.IsNullOrEmpty(variant.Alias.ChainAliasName))
                                variant.Chain = PrepareAlias(variant.Alias.ChainAliasName);
                        }
                        return prepared;
                    }
                    PreparedPreview preview = PrepareAlias(key.Name);
                    preview.DecodedBytes = decodedBytes;
                    lock (_sync)
                    {
                        if (_disposed || generation != _generation)
                        {
                            throw new InvalidOperationException("Sound source changed during preparation; try again.");
                        }
                        while (_cachedBytes + decodedBytes > MaxDecodedBytes &&
                            EvictOldestCompleted(key)) { }
                        if (_cachedBytes + decodedBytes > MaxDecodedBytes)
                        {
                            // A budget refusal must be retryable once the other preparation completes.
                            _preparations.Remove(key);
                            _recent.Remove(key);
                            throw new InvalidOperationException("Sound preview preparation is busy; try this sound again shortly.");
                        }
                        _cachedBytes += decodedBytes;
                    }
                    return preview;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                    InvalidOperationException or PlatformNotSupportedException or DllNotFoundException or
                    EntryPointNotFoundException or BadImageFormatException or IOException or ObjectDisposedException or
                    UnauthorizedAccessException or OverflowException)
                {
                    foreach (PreparedSound sound in sounds) sound.DisposeNative();
                    return new PreparedPreview([],
                        $"Cannot preview this sound: {exception.Message}");
                }
            }).ConfigureAwait(false);
        }
        finally { _preparationSlots.Release(); }
    }

    internal PreviewVoice Play(PreparedSound sound, bool looping, float volume, float pan, float pitch) =>
        _backend.Play(sound, looping, volume, pan, pitch);

    internal SoundPreviewPlayback Play(PreparedPreview preview, SoundEmitterSettings? settings,
        float? distance = null, float pan = 0)
    {
        return new SoundPreviewPlayback(this, Select(preview, settings), settings, distance, pan);
    }

    internal List<PreparedSelection> Select(PreparedPreview preview, SoundEmitterSettings? settings,
        PreparedVariant? chainingFrom = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return preview.Select(_random, settings, chainingFrom);
        }
    }

    /// <summary>Clears source identity after a library reload. Existing handles remain valid for active cues.</summary>
    internal void Invalidate()
    {
        lock (_sync)
        {
            _generation++;
            _preparations.Clear();
            _recent.Clear();
            _cachedBytes = 0;
        }
    }

    private bool EvictOldestCompleted(SourceKey? except = null)
    {
        for (LinkedListNode<SourceKey>? node = _recent.First; node is not null; node = node.Next)
        {
            if (node.Value == except || !_preparations[node.Value].IsCompleted) continue;
            Task<PreparedPreview> task = _preparations[node.Value];
            if (task.IsCompletedSuccessfully)
                _cachedBytes -= task.Result.DecodedBytes;
            _preparations.Remove(node.Value);
            _recent.Remove(node);
            return true;
        }
        return false;
    }

    private void WatchRoot(string root)
    {
        if (_watchers.ContainsKey(root) || !Directory.Exists(root)) return;
        if (_watchers.Count >= 4)
        {
            string oldest = _watchers.Keys.First();
            _watchers[oldest].Dispose();
            _watchers.Remove(oldest);
            Invalidate();
        }
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnSourceChanged;
        watcher.Created += OnSourceChanged;
        watcher.Deleted += OnSourceChanged;
        watcher.Renamed += OnSourceRenamed;
        _watchers.Add(root, watcher);
    }

    private void OnSourceChanged(object sender, FileSystemEventArgs args)
    {
        if (IsSoundSource(args.FullPath)) Invalidate();
    }
    private void OnSourceRenamed(object sender, RenamedEventArgs args)
    {
        if (IsSoundSource(args.FullPath) || IsSoundSource(args.OldFullPath)) Invalidate();
    }

    private static bool IsSoundSource(string path) =>
        path.Split(Path.DirectorySeparatorChar).Contains("soundaliases", StringComparer.OrdinalIgnoreCase);

    private static SourceKey MakeKey(string root, string name)
    {
        string fullRoot = string.IsNullOrWhiteSpace(root) ? root : Path.GetFullPath(root);
        return new SourceKey(fullRoot ?? "", name);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _preparations.Clear();
            _recent.Clear();
            _cachedBytes = 0;
            foreach (FileSystemWatcher watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
        }
        _backend.Dispose();
    }

    private sealed record SourceKey(string Root, string Name);
}

internal sealed class PreparedSound
{
    private int _disposed;
    internal object NativeBuffer { get; }
    internal long DecodedBytes { get; }
    internal double Duration { get; }

    internal PreparedSound(object nativeBuffer, long decodedBytes, double duration)
    {
        NativeBuffer = nativeBuffer;
        DecodedBytes = decodedBytes;
        Duration = duration;
        GC.AddMemoryPressure(decodedBytes);
    }

    ~PreparedSound() => DisposeNative();
    internal void DisposeNative()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (NativeBuffer is IDisposable disposable) disposable.Dispose();
        GC.RemoveMemoryPressure(DecodedBytes);
        GC.SuppressFinalize(this);
    }
}

internal abstract class PreviewVoice : IDisposable
{
    internal abstract bool HasEnded { get; }
    internal abstract void Play();
    internal abstract void Pause();
    internal abstract void SetVolume(float volume);
    internal abstract void SetPan(float pan);
    public abstract void Dispose();
}

internal interface IPreviewAudioBackend : IDisposable
{
    bool IsSupported { get; }
    string? UnavailableReason { get; }
    PreparedSound Prepare(byte[] audio);
    PreviewVoice Play(PreparedSound sound, bool looping, float volume, float pan, float pitch);
}

internal sealed class UnavailablePreviewBackend : IPreviewAudioBackend
{
    public bool IsSupported => false;
    public string UnavailableReason => "Sound preview playback currently requires macOS or Windows.";
    public PreparedSound Prepare(byte[] audio) => throw new PlatformNotSupportedException(UnavailableReason);
    public PreviewVoice Play(PreparedSound sound, bool looping, float volume, float pan, float pitch) =>
        throw new PlatformNotSupportedException(UnavailableReason);
    public void Dispose() { }
}
