using IW4.Studio.Desktop.Workbench.Tools.AssetPool;
using IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;
using IW4.Studio.Desktop.Workbench.Tools.Diagnostics;
using IW4.Studio.Desktop.Workbench.Tools.DependencyGraph;
using IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;
using IW4.Studio.Desktop.Workbench.Tools.FastFileDetails;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;
using IW4.Studio.Desktop.Workbench.Tools.MapEditor;
using IW4.Studio.Desktop.Workbench.Tools.MapRender;
using IW4.Studio.Desktop.Workbench.Tools.Properties;
using IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

namespace IW4.Studio.Desktop.Workbench.Tools;

public sealed record StudioToolContext(
    FastFileAssetsNavigatorViewModel FastFileAssets,
    AssetPoolNavigatorViewModel AssetPool,
    ImageFilePakToolViewModel ImageFilePak,
    ConsoleOutputBuffer ConsoleOutput,
    DiagnosticsAggregator Diagnostics,
    GscUsagesToolViewModel GscUsages,
    MapRenderToolViewModel LivePreview,
    MapEditorToolViewModel MapEditor,
    PropertiesToolViewModel Properties,
    FastFileDetailsToolViewModel FastFileDetails,
    ZoneDetailsToolViewModel ZoneDetails,
    DependencyGraphToolViewModel DependencyGraph);
