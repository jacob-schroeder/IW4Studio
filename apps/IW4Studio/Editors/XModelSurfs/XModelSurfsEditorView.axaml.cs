using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.XModelSurfs;

public sealed partial class XModelSurfsEditorView : UserControl
{
    private AssetReferencePickerService? _picker;
    private XModelSurfsEditorViewModel? _editor;
    private bool _refreshing;
    private readonly List<Field> _fields = [];
    private sealed record Field(TextBox Input, bool IsSize, int Axis)
    {
        public string Display { get; set; } = "";
    }

    public XModelSurfsEditorView()
    {
        InitializeComponent();
        AddFields(false, "Center", ["X", "Y", "Z"]);
        AddFields(true, "Size", ["Width", "Depth", "Height"]);
        DataContextChanged += (_, _) => BindEditor();
        Panes.SizeChanged += (_, e) => ArrangePanes(e.NewSize.Width);
        Viewport.SelectionRequested += index => _editor?.SelectSurface(index);
        Viewport.EditRequested += edit => _editor?.HandleViewportEdit(edit);
    }

    internal XModelSurfsEditorView(XModelSurfsEditorViewModel editor, AssetReferencePickerService? picker) : this()
    {
        _picker = picker;
        DataContext = editor;
    }

    private void BindEditor()
    {
        if (_editor is not null) _editor.Changed -= Refresh;
        _editor = DataContext as XModelSurfsEditorViewModel;
        if (_editor is null) return;
        _editor.Changed += Refresh;
        Geometry.IsExpanded = _editor.Scene.Count == 0;
        Refresh();
    }

    private void ArrangePanes(double width)
    {
        bool stack = width < 650;
        if (stack == (Grid.GetRow(Inspector) == 1)) return;
        Panes.RowDefinitions = new RowDefinitions(stack ? "3*,2*" : "*");
        Panes.ColumnDefinitions = new ColumnDefinitions(stack ? "*" : "*,256");
        Panes.RowSpacing = stack ? 6 : 0;
        Grid.SetRow(Inspector, stack ? 1 : 0);
        Grid.SetColumn(Inspector, stack ? 0 : 1);
    }

    private void AddFields(bool size, string group, string[] labels)
    {
        ExactFields.Children.Add(new TextBlock { Text = group, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        ExactFields.Children.Add(grid);
        for (int axis = 0; axis < labels.Length; axis++)
        {
            var col = new StackPanel { Spacing = 2 };
            col.Children.Add(new TextBlock { Text = labels[axis], FontSize = 11 });
            var input = new TextBox { MinWidth = 0, Padding = new Thickness(4) };
            AutomationProperties.SetName(input, $"{group} {labels[axis]}");
            var field = new Field(input, size, axis);
            _fields.Add(field);
            input.LostFocus += (_, _) => CommitField(field);
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) input.Text = field.Display;
                else if (e.Key == Key.Enter) CommitField(field);
                else return;
                Viewport.Focus();
                e.Handled = true;
            };
            col.Children.Add(input);
            Grid.SetColumn(col, axis);
            grid.Children.Add(col);
        }
    }

    private void CommitField(Field field)
    {
        if (_refreshing || _editor is null || !field.Input.IsEnabled || field.Input.Text == field.Display) return;
        string? input = field.Input.Text;
        field.Input.Text = field.Display;
        if (float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out float value) && float.IsFinite(value))
            _editor.SetExactValue(field.IsSize, field.Axis, value);
        else
            _editor.Report("Enter a finite number. The previous value was restored.");
    }

    private void Refresh()
    {
        if (_editor is not { } vm) return;
        _refreshing = true;
        try
        {
            ContextText.Text = vm.Context;
            Heading.Text = vm.Heading;
            FileName.Text = vm.Name;
            Hint.Text = vm.Hint;
            Status.Text = vm.Status;
            Counts.Text = vm.SurfaceInfo;
            UndoButton.IsEnabled = vm.CanUndo;
            RedoButton.IsEnabled = vm.CanRedo;
            ApplyButton.IsEnabled = vm.CanApply;
            MoveButton.IsEnabled = SizeButton.IsEnabled = vm.CanTransform;
            MoveButton.IsChecked = vm.Tool == XModelSurfsViewportTool.Move;
            SizeButton.IsChecked = vm.Tool == XModelSurfsViewportTool.Size;
            WireButton.IsChecked = vm.Wireframe;
            SurfaceList.ItemsSource = vm.Labels;
            SurfaceList.SelectedIndex = vm.SelectedIndex;
            bool empty = vm.Scene.Count == 0;
            SurfaceList.IsVisible = Counts.IsVisible = !empty;
            Geometry.Header = empty ? "Start from a collection" : "Replace geometry";
            Geometry.IsEnabled = vm.IsEditable;
            BrowseButton.IsVisible = _picker is not null;
            SourcePicker.ItemsSource = vm.Sources;
            SourcePicker.SelectedItem = vm.Sources.FirstOrDefault(c => c.Name == vm.DonorName);
            DonorLabel.IsVisible = DonorPicker.IsVisible = !empty;
            DonorPicker.ItemsSource = vm.DonorLabels;
            DonorPicker.SelectedIndex = vm.DonorIndex;
            PreviewButton.Content = empty ? "Use these surfaces" : "Preview replacement";
            PreviewButton.IsEnabled = vm.CanPreview;
            Precise.IsVisible = !empty;
            foreach (var field in _fields)
            {
                if (vm.SelectedIndex < 0) continue;
                var row = vm.Scene[vm.SelectedIndex];
                field.Input.IsEnabled = vm.CanTransform && (!field.IsSize || row.OriginalSize[field.Axis] > 0);
                float value = (field.IsSize ? row.Size : row.Position)[field.Axis];
                string formatted = value.ToString("G7", CultureInfo.CurrentCulture);
                if (!field.Input.IsFocused || field.Input.Text == field.Display) field.Input.Text = formatted;
                field.Display = formatted;
            }
            Viewport.CanEdit = vm.IsEditable;
            Viewport.Wireframe = vm.Wireframe;
            Viewport.Tool = vm.Tool;
            Viewport.SetScene(vm.Scene, vm.SelectedIndex);
        }
        finally { _refreshing = false; }
    }

    private void SurfaceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) _editor?.SelectSurface(SurfaceList.SelectedIndex);
    }
    private void DonorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) _editor?.SelectDonorSurface(DonorPicker.SelectedIndex);
    }
    private void SourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && SourcePicker.SelectedItem is XModelSurfsEditorViewModel.SourceChoice source)
            _editor?.SetDonor(source.Name);
    }
    private void GeometryOpened(object? sender, RoutedEventArgs e)
    {
        if (_editor is { Sources.Count: > 1 } vm && vm.DonorName == vm.Name)
            vm.SetDonor(vm.Sources[1].Name);
    }
    private async void Browse(object? sender, RoutedEventArgs e)
    {
        if (_picker is null || _editor is not { } vm || TopLevel.GetTopLevel(this) is not Window window) return;
        var name = await _picker.SelectNameAsync(window, XAssetType.XModelSurfs, vm.DonorName);
        if (name is not null && ReferenceEquals(_editor, vm)) vm.SetDonor(name);
    }
    private void ToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string value } && Enum.TryParse<XModelSurfsViewportTool>(value, out var tool))
            _editor?.SetTool(tool);
        Viewport.Focus();
    }
    private void WireClicked(object? sender, RoutedEventArgs e) => _editor?.SetWireframe(WireButton.IsChecked == true);
    private void Apply(object? sender, RoutedEventArgs e) => _editor?.ApplyChanges();
    private void Preview(object? sender, RoutedEventArgs e) => _editor?.PreviewReplacement();
    private void Undo(object? sender, RoutedEventArgs e) => _editor?.Undo();
    private void Redo(object? sender, RoutedEventArgs e) => _editor?.Redo();
    private void Frame(object? sender, RoutedEventArgs e) => Viewport.FitView();
    private void OrbitLeft(object? sender, RoutedEventArgs e) => Viewport.RotateView(-15);
    private void OrbitRight(object? sender, RoutedEventArgs e) => Viewport.RotateView(15);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editor is null || e.Source is TextBox) return;
        if (e.Key == Key.Z && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editor.Redo(); else _editor.Undo();
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F) Viewport.FitView();
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.W or Key.R)
            _editor.SetTool(e.Key == Key.W ? XModelSurfsViewportTool.Move : XModelSurfsViewportTool.Size);
        else return;
        e.Handled = true;
    }
}
