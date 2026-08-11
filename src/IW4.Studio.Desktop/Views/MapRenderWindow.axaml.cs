using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using IW4.Studio.Documents;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Builds a render scene from the immutable loaded workspace, then hands it
/// to the Silk.NET-backed OpenGL surface owned by this window.
/// </summary>
public sealed partial class MapRenderWindow : Window
{
    private readonly FastFileWorkspace? _workspace;
    private FastFileRenderViewService? _renderViewService;
    private readonly CancellationTokenSource _buildWaitCancellation = new();
    private bool _ownsRenderViewService;
    private SilkMapRenderWindow? _nativeRenderWindow;
    private bool _closed;
    private bool _buildStarted;
    private bool _nativeRendererFailed;

    public MapRenderWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        StatusHeading.Text = "Preparing Live Preview";
        StatusMessage.Text = "Building the IW4 scene from the loaded workspace…";
        Opened += MapRenderWindow_Opened;
        Closed += MapRenderWindow_Closed;
    }

    public MapRenderWindow(FastFileWorkspace workspace, string documentName)
        : this(
            workspace,
            documentName,
            new FastFileRenderViewService(),
            ownsRenderViewService: true)
    {
    }

    internal MapRenderWindow(
        FastFileWorkspace workspace,
        string documentName,
        FastFileRenderViewService renderViewService)
        : this(
            workspace,
            documentName,
            renderViewService,
            ownsRenderViewService: false)
    {
    }

    private MapRenderWindow(
        FastFileWorkspace workspace,
        string documentName,
        FastFileRenderViewService renderViewService,
        bool ownsRenderViewService)
        : this()
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        ArgumentNullException.ThrowIfNull(renderViewService);
        _workspace = workspace;
        _renderViewService = renderViewService;
        _ownsRenderViewService = ownsRenderViewService;
        Title = $"Live Preview — {documentName} — IW4 Studio";
    }

    private async void MapRenderWindow_Opened(object? sender, EventArgs e)
    {
        if (_buildStarted)
            return;

        _buildStarted = true;
        if (_workspace is not { } workspace)
        {
            ShowStatus(
                "No workspace is loaded",
                "Open a fastfile in IW4 Studio before starting Live Preview.");
            return;
        }

        try
        {
            FastFileRenderViewService renderViewService =
                _renderViewService ??
                throw new InvalidOperationException(
                    "The Live Preview window has no scene service.");
            RenderViewSceneBuildResult result =
                await renderViewService.BuildSceneAsync(
                    workspace,
                    progress: UpdateBuildProgress,
                    cancellationToken: _buildWaitCancellation.Token);
            if (_closed)
                return;

            if (!result.IsRenderable)
            {
                ShowStatus(
                    "Map rendering is unavailable",
                    result.NonRenderableReason ??
                    "This fastfile does not contain map assets that can be rendered.");
                return;
            }

            StartNativeRenderer(result.Scene!, result.SceneSnapshot!);
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                ShowStatus(
                    "Could not prepare Live Preview",
                    exception.Message);
            }
        }
    }

    private void UpdateBuildProgress(string progress)
    {
        if (_closed || string.IsNullOrWhiteSpace(progress))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            StatusMessage.Text = progress;
        else
            Dispatcher.UIThread.Post(() => StatusMessage.Text = progress);
    }

    private void StartNativeRenderer(
        IW4.Render.MapRenderScene scene,
        IW4.Render.Resources.RenderSceneSnapshot sceneSnapshot)
    {
        ShowStatus(
            "Starting Live Preview",
            "Creating the native Silk.NET render window…");
        try
        {
            var nativeRenderWindow = new SilkMapRenderWindow(
                scene,
                sceneSnapshot,
                text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
            nativeRenderWindow.Failed += NativeRenderWindow_Failed;
            nativeRenderWindow.Stopped += NativeRenderWindow_Stopped;
            nativeRenderWindow.Show();
            _nativeRenderWindow = nativeRenderWindow;
            Hide();
        }
        catch (Exception exception)
        {
            ShowStatus("Could not start Live Preview", exception.Message);
        }
    }

    private void NativeRenderWindow_Failed(object? sender, Exception exception)
    {
        _nativeRendererFailed = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed)
                return;

            Show();
            ShowStatus("Map rendering stopped", exception.Message);
        });
    }

    private void NativeRenderWindow_Stopped(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && !_nativeRendererFailed)
                Close();
        });

    private void MapRenderWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _buildWaitCancellation.Cancel();
        if (_ownsRenderViewService)
            _renderViewService?.Dispose();
        _renderViewService = null;
        _nativeRenderWindow?.Dispose();
        _nativeRenderWindow = null;
    }

    private void ShowStatus(string heading, string message)
    {
        StatusHeading.Text = heading;
        StatusMessage.Text = message;
        StatusOverlay.IsVisible = true;
    }
}
