using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using IW4.Game.Assets.ImpactFx;
using IW4.Game.Assets.Material;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using static IW4.Game.Assets.Material.MaterialSurfaceType;

namespace IW4.Studio.Desktop.Editors.ImpactFx;

public sealed partial class ImpactFxEditorView : UserControl
{
    private readonly Button[] _swatches = new Button[ImpactFxEditorViewModel.SlotCount];
    private readonly TextBlock[] _dots = new TextBlock[ImpactFxEditorViewModel.SlotCount];
    private readonly AssetReferencePickerService? _picker;
    private ImpactFxEditorViewModel? _editor;
    private bool _refreshing;
    private bool _choosing;
    private int _layout = -1;

    public ImpactFxEditorView()
    {
        InitializeComponent();
        AddMaterials("General", Default);
        AddMaterials("Hard surfaces", Metal, PaintedMetal, Glass, Ice, Ceramic, Plastic, Rubber, RiotShield);
        AddMaterials("Ground and masonry", Brick, Concrete, Dirt, Gravel, Mud, Plaster, Rock, Sand, Asphalt, Slush);
        AddMaterials("Living materials and wood", Bark, Flesh, Foliage, Grass, Wood, Fruit);
        AddMaterials("Soft materials", Carpet, Cloth, Paper, Cushion);
        AddMaterials("Weather and water", Snow, Water);
        AddFamily("Character-hit variants", Enumerable.Range(FxImpactEntry.SurfaceEffectCount, FxImpactEntry.FleshEffectCount));
        CompactMaterialPicker.ItemsSource = Enumerable.Range(0, _swatches.Length).Select(SlotName).ToArray();
        Body.SizeChanged += (_, args) => ArrangePanels(args.NewSize.Width);
        DataContextChanged += (_, _) => BindEditor();
    }

    internal ImpactFxEditorView(ImpactFxEditorViewModel editor, AssetReferencePickerService? picker) : this()
    {
        _picker = picker;
        DataContext = editor;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _editor?.RefreshWorkspace();
    }

    private void BindEditor()
    {
        if (_editor is not null) _editor.Changed -= Refresh;
        _editor = DataContext as ImpactFxEditorViewModel;
        if (_editor is null) return;
        _editor.Changed += Refresh;
        EntryPicker.ItemsSource = Enumerable.Range(0, _editor.RowCount).Select(index => $"Impact {index:00}").ToArray();
        Refresh();
    }

    private void ArrangePanels(double width)
    {
        int layout = width >= 900 ? 2 : width >= 590 ? 1 : 0;
        if (_layout == layout) return;
        _layout = layout;
        bool wide = layout == 2, compact = layout == 0;
        Body.ColumnDefinitions = new ColumnDefinitions(wide ? "284,*,216" : compact ? "*" : "254,*");
        Body.RowDefinitions = new RowDefinitions(wide ? "*" : "3*,2*");
        Library.IsVisible = !compact;
        CompactMaterialPicker.IsVisible = compact;
        Grid.SetRowSpan(Library, wide ? 1 : 2);
        Grid.SetColumn(PreviewPanel, compact ? 0 : 1);
        Grid.SetColumn(Inspector, wide ? 2 : compact ? 0 : 1);
        Grid.SetRow(Inspector, wide ? 0 : 1);
    }

    private void AddMaterials(string family, params MaterialSurfaceType[] types) =>
        AddFamily(family, types.Select(type => (int)type));

    private static string SlotName(int slot) => slot >= FxImpactEntry.SurfaceEffectCount
        ? $"Flesh variant {slot - FxImpactEntry.SurfaceEffectCount}"
        : (MaterialSurfaceType)slot switch
        {
            PaintedMetal => "Painted metal",
            RiotShield => "Riot shield",
            var surface => surface.ToString()
        };

    private void AddFamily(string name, IEnumerable<int> slots)
    {
        var section = new StackPanel { Spacing = 4 };
        var heading = new TextBlock { Text = name, FontWeight = FontWeight.SemiBold };
        section.Children.Add(heading);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, RowSpacing = 3 };
        int index = 0;
        foreach (int slot in slots)
        {
            if (index % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = SlotName(slot), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            var dot = new TextBlock { Text = "•", VerticalAlignment = VerticalAlignment.Center };
            dot.Classes.Add("dot");
            var texture = new ImpactMaterialPreviewControl { IsSwatch = true, Slot = slot, Width = 22, Height = 22 };
            var item = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 4 };
            item.Children.Add(texture);
            Grid.SetColumn(label, 1);
            item.Children.Add(label);
            Grid.SetColumn(dot, 2);
            item.Children.Add(dot);
            var button = new Button { Content = item };
            button.Classes.Add("swatch");
            ToolTip.SetTip(button, label.Text);
            button.Click += (_, _) => _editor?.Select(_editor.Row, slot);
            _swatches[slot] = button;
            _dots[slot] = dot;
            Grid.SetColumn(button, index % 2);
            Grid.SetRow(button, index / 2);
            grid.Children.Add(button);
            index++;
        }
        section.Children.Add(grid);
        Families.Children.Add(section);
    }

    private void Refresh()
    {
        if (_editor is not { } vm) return;
        _refreshing = true;
        try
        {
            Heading.Text = vm.Heading;
            EntryPicker.SelectedIndex = vm.Row;
            CompactMaterialPicker.SelectedIndex = vm.Slot;
            int assigned = 0;
            for (int slot = 0; slot < _swatches.Length; slot++)
            {
                bool filled = vm.IsAssigned(slot);
                if (filled) assigned++;
                _dots[slot].IsVisible = filled;
                _swatches[slot].Classes.Set("current", vm.Slot == slot);
                AutomationProperties.SetName(_swatches[slot], $"{SlotName(slot)}, {(filled ? "assigned" : "empty")}{(vm.Slot == slot ? ", selected" : "")}");
            }
            AssignmentCount.Text = $"{assigned} of {_swatches.Length} assigned";
            MaterialTitle.Text = SlotName(vm.Slot);
            bool surface = vm.Slot < FxImpactEntry.SurfaceEffectCount;
            SlotInfo.Text = surface ? $"Surface slot {vm.Slot}" : $"Character-hit variant {vm.Slot - FxImpactEntry.SurfaceEffectCount}";
            FleshGuide.IsVisible = vm.Slot == (int)Flesh;
            bool hasReference = vm.IsAssigned(vm.Slot);
            EffectName.Text = vm.EffectName ?? (hasReference ? "Unresolved effect" : "No effect assigned");
            Sample.Slot = vm.Slot;
            Sample.AssignedEffect = vm.PreviewEffect;
            SampleHint.IsVisible = hasReference;
            SampleHint.Text = !hasReference ? null :
                vm.PreviewEffect is null ? "Load the referenced effect’s provider to preview it." :
                !Sample.CanPreview ? "This effect has no visual elements that the sample can preview." :
                "Click the material to preview the effect.";
            ChooseButton.IsEnabled = vm.IsEditable && _picker is not null && !_choosing;
            ClearButton.IsEnabled = vm.IsEditable && hasReference;
            ApplyButton.IsEnabled = vm.CanApply;
            UndoButton.IsEnabled = vm.CanUndo;
            Status.Text = vm.Status;
        }
        finally { _refreshing = false; }
    }

    private void EntryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && _editor is { } vm) vm.Select(EntryPicker.SelectedIndex, vm.Slot);
    }

    private void MaterialPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && _editor is { } vm) vm.Select(vm.Row, CompactMaterialPicker.SelectedIndex);
    }

    private async void Choose(object? sender, RoutedEventArgs e)
    {
        if (_choosing || _picker is null || _editor is not { IsEditable: true } vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        int row = vm.Row, slot = vm.Slot;
        _choosing = true;
        Refresh();
        try
        {
            var name = await _picker.SelectNameAsync(owner, XAssetType.Fx, vm.EffectName);
            if (name is not null && vm == _editor && row == vm.Row && slot == vm.Slot && TopLevel.GetTopLevel(this) == owner)
                vm.Assign(name);
        }
        finally { _choosing = false; Refresh(); }
    }

    private void ShowFlesh(object? sender, RoutedEventArgs e)
    {
        if (_editor is not { } vm) return;
        vm.Select(vm.Row, FxImpactEntry.SurfaceEffectCount);
        if (Library.IsVisible) _swatches[vm.Slot].Focus();
    }

    private void Clear(object? sender, RoutedEventArgs e) => _editor?.Clear();
    private void Apply(object? sender, RoutedEventArgs e) => _editor?.Apply();
    private void Undo(object? sender, RoutedEventArgs e) => _editor?.Undo();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Z || (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0 || _editor?.CanUndo != true) return;
        _editor.Undo();
        e.Handled = true;
    }
}
