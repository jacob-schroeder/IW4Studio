using System.Text.Json;
using IW4.Formats.SourceFormat.Character;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private WalkPlayerPreview? _walkPlayerPreview;
    private bool _showWalkPlayer = true, _walkPlayerLoading;
    private int _walkPlayerRevision;
    private double _walkPlayerSeconds;
    private float _walkPlayerMotionAmount;
    private bool _walkPlayerRunning;
    private string? _walkPlayerError;
    private string _walkPlayerFaction = "Rangers";

    internal Func<string?>? ResolvePlayerAssets { get; set; }
    internal string? WalkPlayerError => _walkPlayerError;
    internal string WalkPlayerStatus => !ShowWalkPlayer ? "Player hidden" :
        _walkPlayerLoading ? "Loading player…" :
        _walkPlayerError is not null || _renderer.WalkPlayerNotice is not null
            ? "Player unavailable · see Console" : $"{_walkPlayerFaction} · Beretta";

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
            if (_session is not { } session)
                throw new InvalidOperationException("Open a map before loading the Walk player.");
            MapFactionSettings settings = MapFactionAuthoring.Read(session.Document.World.Properties);
            bool rangers = settings.Allies == MapFactionAuthoring.UsArmy;
            FactionAppearance? appearance = rangers ? RangersAssaultAppearance.Resolve(settings, "allies") : null;
            _walkPlayerFaction = !rangers ? "Spetsnaz" : appearance?.CustomAssetFolder is null ? "Rangers" : "Rangers custom";
            string hands = rangers
                ? appearance?.ViewHands ?? RangersAssaultAppearance.StockA.ViewHands
                : "viewhands_russian_airborne";
            string? customRoot = appearance?.CustomAssetFolder is not { } folder ? null :
                Path.Combine(MapFactionAuthoring.GetCharacterAssetsDirectory(session.FilePath ??
                    throw new InvalidOperationException("Save the map before previewing imported character hands.")), folder);
            WalkPlayerPreview preview = await Task.Run(() => WalkPlayerPreview.Load(root, hands, customRoot));
            if (revision != _walkPlayerRevision || !WalkMode) return;
            _walkPlayerPreview = preview;
            _walkPlayerSeconds = 0;
            _walkPlayerMotionAmount = 0;
            _walkPlayerRunning = false;
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
        _walkPlayerMotionAmount = 0;
        _walkPlayerRunning = false;
        _renderer.SetWalkPlayer(null);
    }

    internal void RefreshWalkPlayerAppearance()
    {
        if (!WalkMode) return;
        ClearWalkPlayer();
        if (ShowWalkPlayer) _ = LoadWalkPlayerAsync();
        NavigationModeChanged?.Invoke();
        RequestNextFrameRendering();
    }
}
