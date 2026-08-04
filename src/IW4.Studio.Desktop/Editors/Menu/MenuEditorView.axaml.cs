using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels.Menu;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuEditorView : UserControl
{
    public MenuEditorView() => AvaloniaXamlLoader.Load(this);

    internal MenuEditorView(
        MenuEditorViewModel viewModel,
        AssetReferencePickerService assetReferencePicker)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(assetReferencePicker);
        DataContext = viewModel;
        viewModel.AssetReferenceSelectionRequested +=
            async (_, args) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await assetReferencePicker.ShowAsync(owner, args.Row);
            };
    }
}
