using Avalonia.Controls;
using IW4.FastFiles.Zone;
using IW4.Gsc.Analysis;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;
using IW4.Studio.Gsc;

namespace IW4.Studio.Desktop.Editors.RawFile;

/// <summary>Desktop host for the first concrete detached asset editor.</summary>
public sealed class RawFileViewFactory : IAssetEditorViewFactory
{
    private readonly IGscAnalyzer _gscAnalyzer;
    private readonly GscWorkspaceIndexService? _gscWorkspace;
    private readonly IGscSourceNavigator? _gscSourceNavigator;
    private readonly IGscUsagesPresenter? _gscUsagesPresenter;

    public RawFileViewFactory(
        IGscAnalyzer gscAnalyzer,
        GscWorkspaceIndexService? gscWorkspace = null,
        IGscSourceNavigator? gscSourceNavigator = null,
        IGscUsagesPresenter? gscUsagesPresenter = null)
    {
        _gscAnalyzer = gscAnalyzer
            ?? throw new ArgumentNullException(nameof(gscAnalyzer));
        _gscWorkspace = gscWorkspace;
        _gscSourceNavigator = gscSourceNavigator;
        _gscUsagesPresenter = gscUsagesPresenter;
    }

    public XAssetType AssetType => XAssetType.RawFile;

    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        var viewModel = new RawFileEditorViewModel(
            editorSession,
            _gscAnalyzer,
            _gscWorkspace,
            _gscSourceNavigator,
            _gscUsagesPresenter);
        var view = new RawFileEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(view, viewModel);
    }
}
