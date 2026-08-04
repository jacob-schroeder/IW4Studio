using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Preview;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed class MenuPreviewNodeSelectedEventArgs(MenuNodeId nodeId)
    : EventArgs
{
    public MenuNodeId NodeId { get; } = nodeId;
}

public sealed class MenuPreviewGeometryCommittedEventArgs(
    MenuNodeId nodeId,
    MenuPreviewRect originalBounds,
    MenuPreviewRect candidateBounds) : EventArgs
{
    public MenuNodeId NodeId { get; } = nodeId;

    public MenuPreviewRect OriginalBounds { get; } = originalBounds;

    public MenuPreviewRect CandidateBounds { get; } = candidateBounds;
}

public sealed class MenuPreviewMaterialResolutionCompletedEventArgs(
    MenuPreviewMaterialStatus status)
    : EventArgs
{
    public MenuPreviewMaterialStatus Status { get; } =
        status ?? throw new ArgumentNullException(nameof(status));
}

public sealed record MenuPreviewTextStatus(
    MenuNodeId NodeId,
    string AuthoredText,
    bool UsesGameGlyphs,
    IReadOnlyList<string> Diagnostics)
{
    public int FidelityIssueCount => Diagnostics.Count;

    public string Detail
    {
        get
        {
            string renderer = UsesGameGlyphs
                ? "IW4 glyph metrics and font atlas"
                : "editor fallback font";
            return Diagnostics.Count == 0
                ? $"Text '{AuthoredText}' uses {renderer}."
                : $"Text '{AuthoredText}' uses {renderer}: " +
                    string.Join(" ", Diagnostics);
        }
    }
}

public sealed class MenuPreviewTextResolutionCompletedEventArgs(
    MenuPreviewTextStatus status)
    : EventArgs
{
    public MenuPreviewTextStatus Status { get; } =
        status ?? throw new ArgumentNullException(nameof(status));
}

/// <summary>
/// Avalonia renderer for a renderer-neutral authored or evaluated Menu scene.
/// Material and Font resources resolve asynchronously from the canonical pool;
/// unsupported owner-draw callbacks, models, cinematics, and event side
/// effects remain explicit diagnostics from the core projector/debugger.
/// </summary>
public sealed partial class MenuPreviewControl : Control
{
    public static readonly StyledProperty<MenuPreviewScene?> SceneProperty =
        AvaloniaProperty.Register<MenuPreviewControl, MenuPreviewScene?>(
            nameof(Scene));

    public static readonly StyledProperty<MenuNodeId?> SelectedNodeIdProperty =
        AvaloniaProperty.Register<MenuPreviewControl, MenuNodeId?>(
            nameof(SelectedNodeId));

    public static readonly StyledProperty<bool>
        IsDirectManipulationEnabledProperty =
            AvaloniaProperty.Register<MenuPreviewControl, bool>(
                nameof(IsDirectManipulationEnabled));

    public static readonly StyledProperty<IMenuPreviewMaterialResolver?>
        MaterialResolverProperty =
            AvaloniaProperty.Register<
                MenuPreviewControl,
                IMenuPreviewMaterialResolver?>(nameof(MaterialResolver));

    public static readonly StyledProperty<IMenuTextResourceResolver?>
        TextResourceResolverProperty =
            AvaloniaProperty.Register<
                MenuPreviewControl,
                IMenuTextResourceResolver?>(nameof(TextResourceResolver));

    private bool _isAttached;

    static MenuPreviewControl()
    {
        AffectsRender<MenuPreviewControl>(
            SceneProperty,
            SelectedNodeIdProperty,
            IsDirectManipulationEnabledProperty,
            MaterialResolverProperty,
            TextResourceResolverProperty);
    }

    public MenuPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerCaptureLost += MenuPreviewControl_PointerCaptureLost;
    }

    public MenuPreviewScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public MenuNodeId? SelectedNodeId
    {
        get => GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public bool IsDirectManipulationEnabled
    {
        get => GetValue(IsDirectManipulationEnabledProperty);
        set => SetValue(IsDirectManipulationEnabledProperty, value);
    }

    public IMenuPreviewMaterialResolver? MaterialResolver
    {
        get => GetValue(MaterialResolverProperty);
        set => SetValue(MaterialResolverProperty, value);
    }

    public IMenuTextResourceResolver? TextResourceResolver
    {
        get => GetValue(TextResourceResolverProperty);
        set => SetValue(TextResourceResolverProperty, value);
    }

    public event EventHandler<MenuPreviewNodeSelectedEventArgs>? NodeSelected;

    public event EventHandler<MenuPreviewGeometryCommittedEventArgs>?
        GeometryCommitted;

    public event EventHandler<
        MenuPreviewMaterialResolutionCompletedEventArgs>?
        MaterialResolutionCompleted;

    public event EventHandler<MenuPreviewTextResolutionCompletedEventArgs>?
        TextResolutionCompleted;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(16, 19, 24)),
            null,
            Bounds);

        if (Scene is not { } scene ||
            scene.Settings.CanvasWidth <= 0 ||
            scene.Settings.CanvasHeight <= 0)
        {
            DrawCenteredLabel(context, "No Menu preview available");
            return;
        }

        if (RefreshTextLayouts())
            RefreshMaterials();

        PreviewTransform transform = CreateTransform(scene.Settings);
        DrawStage(context, scene, transform);
        using (context.PushClip(StageBounds(scene.Settings, transform)))
        {
            foreach (MenuPreviewPrimitive primitive in scene.Primitives)
                DrawPrimitive(context, scene, primitive, transform);
            DrawSelection(context, scene, transform);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty ||
            change.Property == TextResourceResolverProperty)
        {
            if (change.Property == SceneProperty)
                CancelGeometryManipulation();
            RefreshTextLayouts(reportStatuses: true);
            RefreshMaterials(change.Property == SceneProperty);
        }
        else if (change.Property == SelectedNodeIdProperty ||
                 change.Property == IsDirectManipulationEnabledProperty)
        {
            CancelGeometryManipulation();
        }
        else if (change.Property == MaterialResolverProperty)
        {
            RefreshMaterials();
        }
    }

    protected override void OnDetachedFromVisualTree(
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        CancelGeometryManipulation();
        _isAttached = false;
        ResetMaterialState();
        ResetTextState();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RefreshTextLayouts(reportStatuses: true);
        RefreshMaterials();
    }
}
