using Avalonia.Controls;
using IW4.Studio.Desktop.Workbench.Docking;
using IW4.Studio.Desktop.Workbench.Tools.AssetDependencies;
using IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;
using IW4.Studio.Desktop.Workbench.Tools.Diagnostics;
using IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Desktop.Workbench.Tools.FastFileDetails;
using IW4.Studio.Desktop.Workbench.Tools.GscFindings;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;
using IW4.Studio.Desktop.Workbench.Tools.MapRender;
using IW4.Studio.Desktop.Workbench.Tools.PackFilePak;
using IW4.Studio.Desktop.Workbench.Tools.Properties;
using IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

namespace IW4.Studio.Desktop.Workbench.Tools;

/// <summary>
/// The complete Studio tool inventory. Adding a future tool is a registration
/// plus its isolated view/view-model folder; the workbench shell does not need
/// another special-case panel.
/// </summary>
public static class StudioToolRegistry
{
    public static IReadOnlyList<StudioToolRegistration> CreateDefault(
        StudioToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Array.AsReadOnly<StudioToolRegistration>(
        [
            Implemented(
                Descriptor(
                    StudioToolIds.FastFileAssets,
                    "Explorer",
                    "FileTreeOutline",
                    10,
                    defaultOpen: true,
                    DockRegion.Left,
                    DockRailGroup.LeftTop),
                new FastFileAssetsNavigatorView
                {
                    DataContext = context.FastFileAssets
                },
                context.FastFileAssets),
            Implemented(
                Descriptor(
                    StudioToolIds.ImageFilePak,
                    "Streamed Images",
                    "ImageMultipleOutline",
                    30,
                    defaultOpen: false,
                    DockRegion.Left,
                    DockRailGroup.LeftTop),
                new ImageFilePakToolView
                {
                    DataContext = context.ImageFilePak
                },
                context.ImageFilePak),
            Implemented(
                Descriptor(
                    StudioToolIds.PackFilePak,
                    "Streamed Sounds",
                    "MusicBoxMultipleOutline",
                    40,
                    defaultOpen: false,
                    DockRegion.Left,
                    DockRailGroup.LeftTop),
                new PackFilePakToolView
                {
                    DataContext = context.PackFilePak
                },
                context.PackFilePak),
            Implemented(
                Descriptor(
                    StudioToolIds.ConsoleOutput,
                    "Console Output",
                    "ConsoleLine",
                    10,
                    defaultOpen: false,
                    DockRegion.Bottom,
                    DockRailGroup.LeftBottom),
                new ConsoleOutputView
                {
                    DataContext = context.ConsoleOutput
                },
                context.ConsoleOutput),
            Implemented(
                Descriptor(
                    StudioToolIds.Diagnostics,
                    "Diagnostics",
                    "Pulse",
                    20,
                    defaultOpen: false,
                    DockRegion.Bottom,
                    DockRailGroup.LeftBottom),
                new DiagnosticsView
                {
                    DataContext = context.Diagnostics
                },
                context.Diagnostics),
            Implemented(
                Descriptor(
                    StudioToolIds.GscFindings,
                    "GSC Errors",
                    "CodeBracesBox",
                    30,
                    defaultOpen: false,
                    DockRegion.Bottom,
                    DockRailGroup.LeftBottom),
                new GscFindingsToolView
                {
                    DataContext = context.GscFindings
                },
                context.GscFindings),
            Implemented(
                Descriptor(
                    StudioToolIds.GscUsages,
                    "GSC References",
                    "FileFindOutline",
                    40,
                    defaultOpen: false,
                    DockRegion.Bottom,
                    DockRailGroup.LeftBottom),
                new GscUsagesToolView
                {
                    DataContext = context.GscUsages
                },
                context.GscUsages),
            .. CreateLivePreview(context),
            Implemented(
                Descriptor(
                    StudioToolIds.Properties,
                    "Properties",
                    "TuneVariant",
                    30,
                    defaultOpen: false,
                    DockRegion.Right,
                    DockRailGroup.Right),
                new PropertiesToolView
                {
                    DataContext = context.Properties
                },
                context.Properties),
            Implemented(
                Descriptor(
                    StudioToolIds.FastFileDetails,
                    "Inspector",
                    "FileDocumentOutline",
                    40,
                    defaultOpen: true,
                    DockRegion.Right,
                    DockRailGroup.Right),
                new TabControl
                {
                    Classes = { "studioInspectorTabs" },
                    Padding = new Avalonia.Thickness(0),
                    Items =
                    {
                        new TabItem
                        {
                            Header = "Fastfile",
                            Content = new FastFileDetailsToolView { DataContext = context.FastFileDetails }
                        },
                        new TabItem
                        {
                            Header = "Zone",
                            Content = new ZoneDetailsToolView { DataContext = context.ZoneDetails }
                        }
                    }
                },
                context),
            Implemented(
                Descriptor(
                    StudioToolIds.AssetDependencies,
                    "Dependencies",
                    "SourceBranch",
                    50,
                    defaultOpen: false,
                    DockRegion.Right,
                    DockRailGroup.Right),
                new AssetDependenciesToolView
                {
                    DataContext = context.AssetDependencies
                },
                context.AssetDependencies),
            Implemented(
                Descriptor(
                    StudioToolIds.DependencyGraph,
                    "Dependency Graph",
                    "GraphOutline",
                    60,
                    defaultOpen: false,
                    DockRegion.Right,
                    DockRailGroup.Right),
                new DependencyGraphToolView
                {
                    DataContext = context.DependencyGraph
                },
                context.DependencyGraph)
        ]);
    }

    private static IEnumerable<StudioToolRegistration> CreateLivePreview(
        StudioToolContext context)
    {
        if (!context.LivePreview.CanLaunch)
            yield break;

        yield return Implemented(
            Descriptor(
                StudioToolIds.LivePreview,
                "Live Preview",
                "MapOutline",
                10,
                defaultOpen: false,
                DockRegion.Right,
                DockRailGroup.Right),
            new MapRenderToolView
            {
                DataContext = context.LivePreview
            },
            context.LivePreview);
    }

    private static StudioToolRegistration Implemented(
        DockToolDescriptor descriptor,
        Avalonia.Controls.Control content,
        object viewModel) =>
        new(descriptor, content, viewModel);

    private static StudioToolRegistration Placeholder(
        DockToolDescriptor descriptor) =>
        new(descriptor, content: null, viewModel: null);

    private static DockToolDescriptor Descriptor(
        string id,
        string title,
        string iconToken,
        int order,
        bool defaultOpen,
        DockRegion region,
        DockRailGroup rail,
        bool implemented = true) =>
        new(
            id,
            title,
            iconToken,
            order,
            implemented,
            defaultOpen,
            region,
            rail);
}
