using System.Text.Json;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private WalkPlayerPreview? _walkPlayerPreview;
    private bool _showWalkPlayer = true, _walkPlayerLoading;
    private int _walkPlayerRevision;
    private double _walkPlayerSeconds;
    private string? _walkPlayerError;

    internal Func<string?>? ResolvePlayerAssets { get; set; }
    internal string? WalkPlayerError => _walkPlayerError;
    internal string WalkPlayerStatus => !ShowWalkPlayer ? "Player hidden" :
        _walkPlayerLoading ? "Loading player…" :
        _walkPlayerError is not null || _renderer.WalkPlayerNotice is not null
            ? "Player unavailable · see Console" : "Rangers · Beretta";

    internal bool ShowWalkPlayer
    {
        get => _showWalkPlayer;
        set
        {
            if (_showWalkPlayer == value) return;
            _showWalkPlayer = value;
            if (value && WalkMode) _ = LoadWalkPlayerAsync();
            NavigationModeChanged?.Invoke();
            RequestNextFrameRendering();
        }
    }

    private async Task LoadWalkPlayerAsync()
    {
        if (!WalkMode || !ShowWalkPlayer || _walkPlayerPreview is not null || _walkPlayerLoading) return;
        int revision = _walkPlayerRevision;
        _walkPlayerLoading = true;
        _walkPlayerError = null;
        NavigationModeChanged?.Invoke();
        try
        {
            string root = ResolvePlayerAssets?.Invoke() ??
                throw new DirectoryNotFoundException("The bundled player assets could not be found. Restore the bootstrap folder beside IW4Radiant.");
            WalkPlayerPreview preview = await Task.Run(() => WalkPlayerPreview.Load(root));
            if (revision != _walkPlayerRevision || !WalkMode) return;
            _walkPlayerPreview = preview;
            _walkPlayerSeconds = 0;
            _renderer.SetWalkPlayer(preview);
        }
        catch (Exception exception) when (IsWalkError(exception) || exception is JsonException or OverflowException)
        {
            if (revision != _walkPlayerRevision || !WalkMode) return;
            _walkPlayerError = $"Player preview unavailable. Walking remains available.{Environment.NewLine}{Environment.NewLine}{exception}";
            InteractionStatusChanged?.Invoke("Player preview unavailable. See Console Output for details; you can continue walking.");
        }
        finally
        {
            if (revision == _walkPlayerRevision)
            {
                _walkPlayerLoading = false;
                NavigationModeChanged?.Invoke();
                RequestNextFrameRendering();
            }
        }
    }

    private void ClearWalkPlayer()
    {
        _walkPlayerRevision++;
        _walkPlayerLoading = false;
        _walkPlayerPreview = null;
        _walkPlayerError = null;
        _walkPlayerSeconds = 0;
        _renderer.SetWalkPlayer(null);
    }
}
