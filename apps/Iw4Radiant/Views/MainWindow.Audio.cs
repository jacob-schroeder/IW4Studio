using Iw4Radiant.Audio;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private readonly AudioPreviewEngine _previewAudio = new();
    private CancellationTokenSource? _mapAudioPreparationCancellation;
    private string? _mapAudioRoot;
    private MapDocument? _mapAudioDocument;
    private int _mapAudioGeneration;
    private HashSet<(string Name, SoundEmitterSettings Settings)> _mapAudioDependencies = [];

    private static SoundEmitterSettings DestructibleSoundSettings(bool looping) => new()
    {
        Looping = looping,
        HasPlaybackOverride = true
    };

    private void RefreshMapAudioDependencies()
    {
        string? root = Workspace.SoundBrowser.SourceDirectory;
        var dependencies = new HashSet<(string Name, SoundEmitterSettings Settings)>();
        if (root is not null)
        {
            foreach (var preset in _session.Scene.Document.Entities
                .Select(entity => DestructiblePresets.Find(entity.Properties)).OfType<DestructiblePreset>().Distinct())
            {
                foreach (var stage in preset.Preview.Stages)
                {
                    if (stage.SoundName is { } loop) dependencies.Add((loop, DestructibleSoundSettings(true)));
                    if (stage.TransitionSoundName is { } once) dependencies.Add((once, DestructibleSoundSettings(false)));
                }
                foreach (var part in preset.Preview.Parts ?? [])
                    if (part.SoundName is { } once) dependencies.Add((once, DestructibleSoundSettings(false)));
            }
            foreach (var entity in PlacedEmitterEntities(isSound: true))
            {
                string? name = entity.Properties.GetValueOrDefault("soundalias");
                if (string.IsNullOrWhiteSpace(name)) continue;
                try { dependencies.Add((name, SoundEmitterSettings.Read(entity))); }
                catch (ArgumentException) { /* The existing marker inspector reports invalid overrides. */ }
            }
        }
        int generation = _previewAudio.Generation;
        if (_mapAudioRoot == root && _mapAudioGeneration == generation && ReferenceEquals(_mapAudioDocument, _session.Document) &&
            _mapAudioDependencies.SetEquals(dependencies)) return;
        CancelMapAudioPreparation();
        _mapAudioRoot = root;
        _mapAudioDocument = _session.Document;
        _mapAudioGeneration = generation;
        _mapAudioDependencies = dependencies;
        if (root is null || dependencies.Count == 0 || !_previewAudio.IsSupported) return;
        var cancellation = new CancellationTokenSource();
        _mapAudioPreparationCancellation = cancellation;
        _ = PrepareMapAudioAsync(root, dependencies.ToArray(), cancellation);
    }

    private async Task PrepareMapAudioAsync(string root,
        (string Name, SoundEmitterSettings Settings)[] dependencies, CancellationTokenSource cancellation)
    {
        try
        {
            // Queue one speculative dependency at a time so an interactive preview can take priority.
            foreach (var dependency in dependencies)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await _previewAudio.PrepareAsync(root, dependency.Name, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_mapAudioPreparationCancellation, cancellation))
                _mapAudioPreparationCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelMapAudioPreparation()
    {
        _mapAudioPreparationCancellation?.Cancel();
        _mapAudioPreparationCancellation = null;
    }
}
