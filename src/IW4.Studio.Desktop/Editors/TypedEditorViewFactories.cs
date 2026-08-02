using Avalonia.Controls;
using Avalonia.Data;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors;

public sealed class MenuEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Menu;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new MenuEditorViewModel(editorSession), ["Name", "ItemCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class MenuFileEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.MenuFile;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new MenuFileEditorViewModel(editorSession), ["Name", "MenuCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class LocalizeEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Localize;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => CreateHost(new LocalizeEditorViewModel(editorSession));
    private static AssetEditorViewHost CreateHost(LocalizeEditorViewModel viewModel)
    {
        var input = new TextBox { AcceptsReturn = true, MinHeight = 90 };
        input.Bind(TextBox.TextProperty, new Binding("ValueInput") { Mode = BindingMode.TwoWay });
        input.Bind(TextBox.IsReadOnlyProperty, new Binding("IsInputReadOnly"));
        var apply = new Button { Content = "Apply value" }; apply.Click += (_, _) => viewModel.ApplyValue();
        var revert = new Button { Content = "Revert" }; revert.Click += (_, _) => viewModel.RevertDraft();
        var panel = new StackPanel { Spacing = 8, Children = { Text("Key"), Text("KeyPolicy"), Text("StatusMessage"), input, apply, revert, Text("DiagnosticsSummary") } };
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
    private static TextBlock Text(string path) { var control = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; control.Bind(TextBlock.TextProperty, new Binding(path)); return control; }
}

public sealed class StructuredDataEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.StructuredDataDef;
    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        var viewModel = new StructuredDataEditorViewModel(editorSession);
        var panel = new StackPanel { Spacing = 8 };
        foreach (string path in new[] { "StatusMessage", "DefinitionCount" }) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class PhysPresetEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.PhysPreset;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new PhysPresetEditorViewModel(editorSession), ["Name", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class PhysCollmapEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.PhysCollmap;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new PhysCollmapEditorViewModel(editorSession), ["Name", "GeometryCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class XAnimEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.XAnim;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new XAnimEditorViewModel(editorSession), ["Name", "FrameCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class XModelEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.XModel;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new XModelEditorViewModel(editorSession), ["Name", "LodCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class SoundEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Sound;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new SoundEditorViewModel(editorSession), ["AliasName", "AliasCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class FxEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Fx;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new FxEditorViewModel(editorSession), ["Name", "ElementCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class ImpactFxEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.ImpactFx;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new ImpactFxEditorViewModel(editorSession), ["Name", "EntryCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class SndCurveEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.SndCurve;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new SndCurveEditorViewModel(editorSession), ["Filename", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class LeaderboardEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.LeaderboardDef;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new LeaderboardEditorViewModel(editorSession), ["Name", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class TracerEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Tracer;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new TracerEditorViewModel(editorSession), ["Name", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class LightDefEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.LightDef;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new LightDefEditorViewModel(editorSession), ["Name", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class ComWorldEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.ComMap;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new ComWorldEditorViewModel(editorSession), ["Name", "PrimaryLightCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class GameWorldMpEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.GameMapMp;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new GameWorldMpEditorViewModel(editorSession), ["Name", "GlassPieceCount", "GlassNameCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class GameWorldSpEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.GameMapSp;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new GameWorldSpEditorViewModel(editorSession), ["Name", "PathNodeCount", "VehicleSegmentCount", "GlassPieceCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class FxWorldEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.FxMap;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new FxWorldEditorViewModel(editorSession), ["Name", "DefinitionCount", "PieceLimit", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class GfxWorldEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.GfxMap;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new GfxWorldEditorViewModel(editorSession), ["Name", "SurfaceCount", "CellCount", "ModelCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class ClipMapEditorViewFactory : IAssetEditorViewFactory
{
    public ClipMapEditorViewFactory(XAssetType assetType)
    {
        if (assetType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
            throw new ArgumentOutOfRangeException(nameof(assetType));
        AssetType = assetType;
    }

    public XAssetType AssetType { get; }

    public AssetEditorViewHost Create(AssetEditorSession editorSession) =>
        Summary(new ClipMapEditorViewModel(editorSession), ["Name", "PlaneCount", "BrushCount", "DynamicEntityCount", "StatusMessage"]);

    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (string path in paths)
        {
            var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, new Binding(path));
            panel.Children.Add(text);
        }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class VehicleEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Vehicle;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new VehicleEditorViewModel(editorSession), ["Name", "VehicleType", "SurfaceSoundCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class WeaponEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Weapon;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new WeaponEditorViewModel(editorSession), ["Name", "GunModelCount", "SoundAliasCount", "NoteTrackCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class MapEntsEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.MapEnts;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new MapEntsEditorViewModel(editorSession), ["Name", "EntityByteCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class AddonMapEntsEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.AddonMapEnts;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => Summary(new AddonMapEntsEditorViewModel(editorSession), ["Name", "EntityByteCount", "StatusMessage"]);
    private static AssetEditorViewHost Summary(object viewModel, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel); }
}

public sealed class BinaryResourceEditorViewFactory : IAssetEditorViewFactory
{
    public BinaryResourceEditorViewFactory(XAssetType type) => AssetType = type;
    public XAssetType AssetType { get; }
    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        var viewModel = new BinaryResourceEditorViewModel(editorSession, AssetType); var panel = new StackPanel { Spacing = 8 };
        foreach (string path in new[] { "Name", "StatusMessage" }) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); }
        return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = viewModel }, viewModel);
    }
}

public sealed class FontEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Font;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => CreateSummary(new FontEditorViewModel(editorSession), ["Name", "GlyphCount", "StatusMessage"]);
    private static AssetEditorViewHost CreateSummary(object model, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = model }, model); }
}

public sealed class TechniqueSetEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Techset;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => CreateSummary(new TechniqueSetEditorViewModel(editorSession), ["Name", "OccupiedTechniqueCount", "StatusMessage"]);
    private static AssetEditorViewHost CreateSummary(object model, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = model }, model); }
}

public sealed class MaterialEditorViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Material;
    public AssetEditorViewHost Create(AssetEditorSession editorSession) => CreateSummary(new MaterialEditorViewModel(editorSession), ["Name", "TextureCount", "ConstantCount", "StateBitsCount", "StatusMessage"]);
    private static AssetEditorViewHost CreateSummary(object model, IEnumerable<string> paths) { var panel = new StackPanel { Spacing = 8 }; foreach (string path in paths) { var text = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding(path)); panel.Children.Add(text); } return new AssetEditorViewHost(new UserControl { Content = panel, DataContext = model }, model); }
}
