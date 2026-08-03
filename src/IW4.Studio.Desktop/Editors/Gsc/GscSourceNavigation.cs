using IW4.Gsc.BuiltIns;
using IW4.Gsc.Workspace;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Desktop-facing navigation seam for a workspace GSC source location.
/// Language services can request navigation without retaining the workbench,
/// an editor control, or a runtime asset.
/// </summary>
public interface IGscSourceNavigator
{
    void NavigateTo(GscSourceLocation location);

    void NavigateTo(Iw4GscBuiltInDefinition builtIn);
}

public sealed class GscSourceNavigationRequestedEventArgs : EventArgs
{
    public GscSourceNavigationRequestedEventArgs(GscSourceLocation location) =>
        Location = location;

    public GscSourceLocation Location { get; }
}

public sealed class GscEngineBuiltInNavigationRequestedEventArgs : EventArgs
{
    public GscEngineBuiltInNavigationRequestedEventArgs(
        Iw4GscBuiltInDefinition builtIn) =>
        BuiltIn = builtIn ?? throw new ArgumentNullException(nameof(builtIn));

    public Iw4GscBuiltInDefinition BuiltIn { get; }
}

/// <summary>
/// Window-local broker. The composition root resolves the document identity,
/// opens its RawFile editor, and selects the requested source span.
/// </summary>
public sealed class GscSourceNavigationBroker : IGscSourceNavigator
{
    public event EventHandler<GscSourceNavigationRequestedEventArgs>?
        NavigationRequested;

    public event EventHandler<GscEngineBuiltInNavigationRequestedEventArgs>?
        EngineBuiltInNavigationRequested;

    public void NavigateTo(GscSourceLocation location) =>
        NavigationRequested?.Invoke(
            this,
            new GscSourceNavigationRequestedEventArgs(location));

    public void NavigateTo(Iw4GscBuiltInDefinition builtIn) =>
        EngineBuiltInNavigationRequested?.Invoke(
            this,
            new GscEngineBuiltInNavigationRequestedEventArgs(builtIn));
}
