using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using IW4.Game.Assets.Physics;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

public sealed partial class PhysCollmapEditorView : UserControl
{
    private AssetReferencePickerService? _picker;
    private PhysCollmapEditorViewModel? _editor;
    private bool _refreshing;
    private readonly List<NumberField> _fields = [];
    private sealed record NumberField(string Group, int Axis, TextBlock Label, TextBox Input)
    {
        public string Display { get; set; } = "";
    }

    public PhysCollmapEditorView()
    {
        InitializeComponent();
        MakeFields(ShapeFields, "Position", ["Across", "Depth", "Height"]);
        MakeFields(ShapeFields, "Size", ["Width", "Depth", "Height"]);
        MakeFields(ShapeFields, "Rotation", ["X", "Y", "Z"]);
        MakeFields(BalanceFields, "Balance", ["Across", "Depth", "Height"]);
        DataContextChanged += (_, _) => BindEditor();
        Viewport.EditRequested += edit => _editor?.HandleViewportEdit(edit);
        Viewport.SelectionRequested += index => _editor?.SelectShape(index);
        Panes.SizeChanged += (_, args) => ArrangePanes(args.NewSize.Width);
    }

    internal PhysCollmapEditorView(PhysCollmapEditorViewModel editor, AssetReferencePickerService? picker) : this()
    {
        _picker = picker;
        DataContext = editor;
    }

    private void BindEditor()
    {
        if (_editor is not null) _editor.SceneChanged -= Refresh;
        _editor = DataContext as PhysCollmapEditorViewModel;
        if (_editor is null) return;
        _editor.SceneChanged += Refresh;
        Refresh();
    }

    private void ArrangePanes(double width)
    {
        bool stacked = width < 620;
        if (stacked == (Grid.GetRow(InspectorPane) == 1)) return;
        Panes.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,250");
        Panes.RowDefinitions = new RowDefinitions(stacked ? "3*,2*" : "*");
        Panes.RowSpacing = stacked ? 6 : 0;
        Grid.SetColumn(InspectorPane, stacked ? 0 : 1);
        Grid.SetRow(InspectorPane, stacked ? 1 : 0);
    }

    private void MakeFields(StackPanel container, string group, string[] labels)
    {
        if (group != "Balance")
            container.Children.Add(new TextBlock { Text = group == "Rotation" ? "Rotation (°)" : group, Margin = new Thickness(0, 5, 0, 0) });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        container.Children.Add(grid);
        for (int axis = 0; axis < 3; axis++)
        {
            var column = new StackPanel { Spacing = 2 };
            var label = new TextBlock { Text = labels[axis], FontSize = 11 };
            var input = new TextBox { MinWidth = 0, Padding = new Thickness(4), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            AutomationProperties.SetName(input, $"{group} {labels[axis]}");
            var field = new NumberField(group, axis, label, input);
            _fields.Add(field);
            input.LostFocus += (_, _) => CommitField(field);
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { CommitField(field); Viewport.Focus(); e.Handled = true; }
                if (e.Key == Key.Escape) { input.Text = field.Display; Viewport.Focus(); e.Handled = true; }
            };
            column.Children.Add(label); column.Children.Add(input);
            Grid.SetColumn(column, axis); grid.Children.Add(column);
        }
    }

    private void CommitField(NumberField field)
    {
        if (_editor is null || _refreshing || !field.Input.IsEnabled || field.Input.Text == field.Display) return;
        string? input = field.Input.Text;
        field.Input.Text = field.Display;
        if (!float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) || !float.IsFinite(value))
        {
            _editor.Report("Enter a finite number. The previous value was restored.");
            return;
        }
        _editor.SetExactValue(field.Group, field.Axis, value);
    }

    private void Refresh()
    {
        if (_editor is not { } vm) return;
        _refreshing = true;
        try
        {
            AssetName.Text = vm.Name;
            StatusText.Text = vm.Status;
            ModelLabel.Text = vm.ModelDescription;
            ChooseModelButton.IsVisible = _picker is not null;
            HintText.Text = vm.Hint;
            UndoButton.IsEnabled = vm.CanUndo; RedoButton.IsEnabled = vm.CanRedo;
            ApplyButton.IsEnabled = vm.CanApply; AddButton.IsEnabled = vm.IsEditable;
            FitButton.IsEnabled = vm.CanFit;
            RemoveButton.IsEnabled = vm.IsEditable && !vm.EditingBalance && vm.SelectedIndex >= 0;
            MoveButton.IsEnabled = vm.IsEditable && (vm.EditingBalance || vm.CanTransform);
            ResizeButton.IsEnabled = vm.CanTransform; RotateButton.IsEnabled = vm.CanRotate;
            MoveButton.IsChecked = vm.Tool == PhysCollmapViewportTool.Move;
            ResizeButton.IsChecked = vm.Tool == PhysCollmapViewportTool.Resize;
            RotateButton.IsChecked = vm.Tool == PhysCollmapViewportTool.Rotate;
            OverlayLayer.IsChecked = vm.Layer == PhysCollmapViewportLayer.Overlay;
            ModelLayer.IsChecked = vm.Layer == PhysCollmapViewportLayer.Model;
            ShapeLayer.IsChecked = vm.Layer == PhysCollmapViewportLayer.Shape;
            OverlayLayer.IsEnabled = ModelLayer.IsEnabled = vm.ModelVertices.Count > 0;
            BalanceButton.IsChecked = vm.EditingBalance;
            BalanceButton.IsEnabled = vm.IsEditable;
            var shape = vm.SelectedIndex >= 0 ? vm.Shapes[vm.SelectedIndex] : null;
            BrushHint.IsVisible = shape?.Shape == PhysGeomShape.Brush && !vm.EditingBalance;
            FineTune.IsVisible = shape is not null && !vm.EditingBalance;
            EmptyMessage.IsVisible = vm.Shapes.Count == 0;
            string[] labels = vm.Shapes.Select((g, i) => $"{i + 1}.  {g.Shape}").ToArray();
            if (ShapeList.ItemsSource is not string[] current || !current.SequenceEqual(labels)) ShapeList.ItemsSource = labels;
            ShapeList.SelectedIndex = vm.EditingBalance ? -1 : vm.SelectedIndex;
            foreach (var f in _fields)
            {
                Vector3 v = f.Group switch { "Balance" => vm.BalancePoint, "Position" => shape?.Position ?? default,
                    "Size" => shape?.Size ?? default, _ => shape?.RotationDegrees ?? default };
                bool diameter = f.Group == "Size" && shape?.Shape == PhysGeomShape.Cylinder;
                if (f.Group == "Size" && f.Axis == 0) f.Label.Text = diameter ? "Diameter" : "Width";
                if (f.Group == "Size" && f.Axis == 1 && f.Input.Parent is StackPanel parent) parent.IsVisible = !diameter;
                f.Input.IsEnabled = f.Group switch { "Rotation" => vm.CanRotate, "Balance" => vm.IsEditable, _ => vm.CanTransform };
                string formatted = v[f.Axis].ToString("G7", CultureInfo.CurrentCulture);
                if (!f.Input.IsFocused || f.Input.Text == f.Display) f.Input.Text = formatted;
                f.Display = formatted;
            }
            Viewport.CanEdit = vm.IsEditable;
            Viewport.Layer = vm.Layer;
            Viewport.Tool = vm.Tool;
            Viewport.EditBalancePoint = vm.EditingBalance;
            Viewport.SetScene(vm.Shapes, vm.SelectedIndex, vm.BalancePoint, vm.ModelVertices, vm.ModelTriangles);
        }
        finally { _refreshing = false; }
    }

    private void ToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse<PhysCollmapViewportTool>(name, out var tool))
            _editor?.SetTool(tool);
        Viewport.Focus();
    }
    private void LayerClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string name } && Enum.TryParse<PhysCollmapViewportLayer>(name, out var layer))
            _editor?.SetLayer(layer);
    }
    private void ShapeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ShapeList.SelectedIndex >= 0) _editor?.SelectShape(ShapeList.SelectedIndex);
    }
    private void BalanceCollapsed(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source)) _editor?.SetEditingBalance(false);
    }
    private void BalanceClicked(object? sender, RoutedEventArgs e) => _editor?.SetEditingBalance(BalanceButton.IsChecked == true);
    private void UndoClicked(object? sender, RoutedEventArgs e) => _editor?.Undo();
    private void RedoClicked(object? sender, RoutedEventArgs e) => _editor?.Redo();
    private void ApplyClicked(object? sender, RoutedEventArgs e) => _editor?.ApplyChanges();
    private void FitClicked(object? sender, RoutedEventArgs e) => _editor?.FitToModel();
    private void RemoveClicked(object? sender, RoutedEventArgs e) => _editor?.RemoveShape();
    private void AddClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse<PhysGeomShape>(name, out var kind)) _editor?.AddShape(kind);
    }
    private void OrbitLeft(object? sender, RoutedEventArgs e) => Viewport.RotateView(-15);
    private void OrbitRight(object? sender, RoutedEventArgs e) => Viewport.RotateView(15);
    private void FrameClicked(object? sender, RoutedEventArgs e) => Viewport.FitView();
    private async void ChooseModelClicked(object? sender, RoutedEventArgs e)
    {
        if (_picker is null || _editor is not { } vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var name = await _picker.SelectNameAsync(owner, XAssetType.XModel, vm.ModelName);
        if (name is not null && ReferenceEquals(_editor, vm)) vm.ChooseModel(name);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editor is null || e.Source is TextBox) return;
        if (e.Key == Key.Z && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editor.Redo(); else _editor.Undo();
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.W or Key.R or Key.E)
            _editor.SetTool(e.Key switch { Key.W => PhysCollmapViewportTool.Move, Key.R => PhysCollmapViewportTool.Resize, _ => PhysCollmapViewportTool.Rotate });
        else return;
        e.Handled = true;
    }
}
