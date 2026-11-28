using IW4.Studio.Desktop.Editors.XModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Vehicle;

public sealed partial class VehicleEditorView : UserControl
{
    private const string FrontTravel = "Phys.SuspensionTravelFront";
    private const string RearTravel = "Phys.SuspensionTravelRear";
    private const string FourWheelType = "Wheels4";

    private static readonly string[] HandlingSections =
        ["Drivetrain", "Suspension", "Friction", "Stability", "Steering"];

    private VehicleEditorViewModel? _viewModel;
    private IReadOnlyList<VehicleEditorField> _fields = [];
    private bool _initialized;
    private bool _allFieldsOpen;
    private bool _slotRefreshQueued;
    private bool _updatingSlots;

    internal AssetReferencePickerService? Picker { get; init; }

    public VehicleEditorView()
    {
        InitializeComponent();
        Preview.AttachCameraInput(PreviewInput);
        HandlingGroups.ItemsSource = HandlingSections;
        HandlingGroups.SelectedItem = "Suspension";
        _initialized = true;
        DataContextChanged += (_, _) =>
        {
            if (this.IsAttachedToVisualTree())
                AttachViewModel(DataContext as VehicleEditorViewModel);
        };
        ShowPage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Preview.RendererStatusChanged += PreviewRenderer_Changed;
        AttachViewModel(DataContext as VehicleEditorViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Preview.RendererStatusChanged -= PreviewRenderer_Changed;
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachViewModel(VehicleEditorViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;
        if (_viewModel is { } previous)
        {
            previous.SelectAudio(null);
            previous.PropertyChanged -= ViewModel_Changed;
        }

        _viewModel = viewModel;
        if (_viewModel is { } current)
            current.PropertyChanged += ViewModel_Changed;
        RefreshFields();
        SelectPreviewLod();
        UpdatePreviewStatus();
    }

    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VehicleEditorViewModel.Fields) or nameof(VehicleEditorViewModel.IsEditable) or null)
            RefreshFields();

        if (e.PropertyName is nameof(VehicleEditorViewModel.PreviewScene) or null)
            SelectPreviewLod();

        if (e.PropertyName is nameof(VehicleEditorViewModel.PreviewStatus) or nameof(VehicleEditorViewModel.PreviewScene) or null)
            UpdatePreviewStatus();

        if (e.PropertyName is nameof(VehicleEditorViewModel.AudioPreview) or null)
        {
            AudioPanel.IsVisible = _viewModel?.AudioPreview is not null;
            UpdateReferenceActions();
        }
    }

    private void RefreshFields()
    {
        foreach (VehicleEditorField field in _fields)
            field.PropertyChanged -= Field_Changed;

        _fields = _viewModel?.Fields ?? [];
        foreach (VehicleEditorField field in _fields)
            field.PropertyChanged += Field_Changed;

        BuildOverview();
        if (FindField("Type") is { } kind && kind.Value != FourWheelType && Equals(HandlingGroups.SelectedItem, "Suspension"))
            HandlingGroups.SelectedItem = "Stability";
        BuildHandling();
        UpdateSuspension();
        BuildReferenceGroups();
        if (_allFieldsOpen)
            BuildAllFields();
    }

    private void Field_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VehicleEditorField field ||
            e.PropertyName is not (nameof(VehicleEditorField.Value) or nameof(VehicleEditorField.SavedValue) or null))
        {
            return;
        }

        if (field.Key is FrontTravel or RearTravel or "Type")
            UpdateSuspension();
        if (field.ReferenceType is null)
            return;

        if (ReferenceEquals(Slots.SelectedItem, field))
            UpdateReferenceActions();
        if (ReferencesPage.IsVisible && (ShowEmptySlots.IsChecked != true || !string.IsNullOrWhiteSpace(SlotFilter.Text)) &&
            !_slotRefreshQueued)
        {
            _slotRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _slotRefreshQueued = false;
                UpdateSlots();
            });
        }
    }

    private void Pages_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !ReferenceEquals(e.Source, Pages))
            return;
        _allFieldsOpen = false;
        AllFieldsButton.IsChecked = false;
        ShowPage();
    }

    private void AllFields_Click(object? sender, RoutedEventArgs e)
    {
        _allFieldsOpen = AllFieldsButton.IsChecked == true;
        ShowPage();
        if (_allFieldsOpen)
        {
            BuildAllFields();
            FieldFilter.Focus();
        }
    }

    private void ShowPage()
    {
        OverviewPage.IsVisible = !_allFieldsOpen && Pages.SelectedIndex == 0;
        HandlingPage.IsVisible = !_allFieldsOpen && Pages.SelectedIndex == 1;
        ReferencesPage.IsVisible = !_allFieldsOpen && Pages.SelectedIndex == 2;
        AllFieldsPage.IsVisible = _allFieldsOpen;
        if (ReferencesPage.IsVisible)
            UpdateSlots();
        else
            UpdateSlotDetail();
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) => _viewModel?.ApplyChanges();

    private void Revert_Click(object? sender, RoutedEventArgs e) => _viewModel?.RevertChanges();

    private void Fit_Click(object? sender, RoutedEventArgs e) => Preview.Fit();

    private void PreviewMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (Preview is not null && sender is ComboBox viewMode)
            Preview.ShowWireframe = viewMode.SelectedIndex == 1;
    }

    private void PreviewRenderer_Changed(object? sender, EventArgs e) => UpdatePreviewStatus();

    private void SelectPreviewLod()
    {
        int lod = _viewModel?.PreviewScene?.DefaultLodIndex ?? -1;
        Preview.SelectedLodIndex = lod;
        HandlingPreview.SelectedLodIndex = lod;
    }

    private void UpdatePreviewStatus()
    {
        UpdateHandlingPreview(Suspension.IsVisible);
        string message = Preview.RendererFailure ?? _viewModel?.PreviewStatus ?? string.Empty;
        PreviewMessage.Text = message;
        PreviewMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
        PreviewMessage.VerticalAlignment = _viewModel?.PreviewScene is null || Preview.RendererFailure is not null
            ? VerticalAlignment.Center
            : VerticalAlignment.Bottom;
        PreviewMessage.Margin = new Thickness(12);
    }

    private async void PickPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } model || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        string? name = Picker is { } picker
            ? await picker.SelectNameAsync(owner, XAssetType.XModel, model.PreviewModel)
            : await SelectNameAsync(owner, "Choose preview model", model.ModelNames, model.PreviewModel);
        if (name is not null && ReferenceEquals(_viewModel, model))
            model.PreviewModel = name;
    }

    private void BuildOverview()
    {
        OverviewFields.Children.Clear();
        AddGroup(OverviewFields, "Definition", ["Type", "Health", "PlayerProtected", "UseHintString"], expanded: true);

        var camera = new StackPanel { Spacing = 5 };
        AddRows(camera, FieldsFor("CamLookEnabled", "CamPitchInfluence", "CamRollInfluence", "CamLerp",
            "CamFovIncrease", "CamFovOffset", "CamFovSpeed"));
        AddKillcamOffset(camera);
        if (camera.Children.Count > 0)
            OverviewFields.Children.Add(CreateGroup("Camera", camera, expanded: true));

        AddGroup(OverviewFields, "Collision damage",
            ["CollisionDamage", "CollisionSpeed", "BulletDamage", "ArmorPiercingDamage", "GrenadeDamage",
             "ProjectileDamage", "ProjectileSplashDamage", "HeavyExplosiveDamage"], expanded: false);
        AddGroup(OverviewFields, "Boost", ["BoostDuration", "BoostRechargeTime", "BoostAcceleration"], expanded: false);
    }

    private void AddKillcamOffset(StackPanel camera)
    {
        var offset = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        foreach ((string axis, int index) in new[] { ("X", 0), ("Y", 1), ("Z", 2) })
        {
            if (FindField($"KillcamOffset.{axis}") is not { } field)
                continue;
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(new TextBlock { Text = axis, Classes = { "vehicleMuted" } });
            cell.Children.Add(CreateFieldEditor(field));
            Grid.SetColumn(cell, index);
            offset.Children.Add(cell);
        }
        if (offset.Children.Count > 0)
        {
            camera.Children.Add(new TextBlock { Text = "Killcam offset" });
            camera.Children.Add(offset);
        }
    }

    private void HandlingGroup_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initialized && ReferenceEquals(e.Source, HandlingGroups))
            BuildHandling();
    }

    private void BuildHandling()
    {
        HandlingFields.Children.Clear();
        switch (HandlingGroups.SelectedItem as string)
        {
            case "Drivetrain":
                AddGroup(HandlingFields, "Drivetrain",
                    ["Phys.PhysicsEnabled", "Phys.SteeringAxle", "Phys.PowerAxle", "Phys.BrakingAxle",
                     "TopSpeed", "Phys.TopSpeed", "Phys.ReverseSpeed", "Phys.MaxVelocity", "Accel", "Phys.AccelGraphName"], expanded: true);
                break;
            case "Suspension":
                if (BuildSuspensionTable() is { } table)
                    HandlingFields.Children.Add(CreateGroup("Suspension", table, expanded: true));
                AddGroup(HandlingFields, "Drivetrain",
                    ["Phys.SteeringAxle", "Phys.PowerAxle", "Phys.BrakingAxle"], expanded: true);
                break;
            case "Friction":
                AddGroup(HandlingFields, "Friction",
                    ["Phys.FrictionBraking", "Phys.FrictionCoasting", "Phys.FrictionTopSpeed", "Phys.FrictionSide",
                     "Phys.FrictionSideRear", "Phys.VelocityDependentSlip", "Phys.SlipThresholdFront", "Phys.SlipThresholdRear",
                     "Phys.SlipFricScaleFront", "Phys.SlipFricScaleRear", "Phys.SlipFricRateFront", "Phys.SlipFricRateRear", "Phys.SlipYawTorque"],
                    expanded: true);
                break;
            case "Stability":
                AddGroup(HandlingFields, "Stability",
                    ["MaxBodyPitch", "MaxBodyRoll", "Phys.MaxPitch", "Phys.MaxRoll", "Phys.RollStability",
                     "Phys.RollResistance", "Phys.PitchResistance", "Phys.YawResistance", "Phys.UprightStrengthPitch",
                     "Phys.UprightStrengthRoll", "Phys.TargetAirPitch", "Phys.AirYawTorque", "Phys.AirPitchTorque"], expanded: true);
                break;
            case "Steering":
                AddGroup(HandlingFields, "Steering",
                    ["Phys.SteeringAxle", "MaxSteeringAngle", "SteeringLerp", "MinSteeringScale",
                     "MinSteeringSpeed", "RotRate", "RotAccel"], expanded: true);
                break;
        }
    }

    private Control? BuildSuspensionTable()
    {
        if (_viewModel is not { } model || FindField(FrontTravel) is null || FindField(RearTravel) is null)
            return null;

        var table = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 7,
            RowSpacing = 5
        };
        table.Children.Add(Cell(new TextBlock { Text = "Front", HorizontalAlignment = HorizontalAlignment.Center }, 0, 1));
        table.Children.Add(Cell(new TextBlock { Text = "Rear", HorizontalAlignment = HorizontalAlignment.Center }, 0, 2));

        AddPair("Travel", 1, FrontTravel, RearTravel);
        AddPair("Strength", 2, "Phys.SuspensionStrengthFront", "Phys.SuspensionStrengthRear");
        AddPair("Damping", 3, "Phys.SuspensionDampingFront", "Phys.SuspensionDampingRear");

        var link = new CheckBox { Content = "Link front and rear", IsEnabled = model.IsEditable };
        link.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(model.LinkAxles)) { Source = model, Mode = BindingMode.TwoWay });
        return new StackPanel { Spacing = 8, Children = { table, link } };

        void AddPair(string label, int row, string frontKey, string rearKey)
        {
            table.Children.Add(Cell(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, 0));
            if (FindField(frontKey) is { } front)
                table.Children.Add(Cell(WithFieldError(front), row, 1));
            if (FindField(rearKey) is { } rear)
                table.Children.Add(Cell(WithFieldError(rear), row, 2));
        }
    }

    private static Control Cell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private void CompareSaved_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initialized)
            UpdateSuspension();
    }

    private void UpdateSuspension()
    {
        VehicleEditorField? front = FindField(FrontTravel);
        VehicleEditorField? rear = FindField(RearTravel);
        bool useDiagram = FindField("Type")?.Value == FourWheelType && front is not null && rear is not null;
        Suspension.IsVisible = useDiagram;
        HandlingPreviewTitle.Text = useDiagram ? "Suspension travel" : "Model geometry";
        UpdateHandlingPreview(useDiagram);
        CompareFooter.IsVisible = useDiagram;
        SavedLegend.IsVisible = CompareSaved.IsChecked == true;
        Suspension.ShowTravel(
            ParseTravel(front?.Value), ParseTravel(rear?.Value),
            ParseTravel(front?.SavedValue), ParseTravel(rear?.SavedValue),
            CompareSaved.IsChecked == true);
    }

    private void UpdateHandlingPreview(bool useDiagram)
    {
        HandlingPreview.IsVisible = _viewModel?.PreviewScene is not null;
        HandlingPreview.CameraPreset = useDiagram ? XModelCameraPreset.Side : XModelCameraPreset.Vehicle;
        Grid.SetRowSpan(HandlingPreview, useDiagram ? 1 : 2);
        Grid.SetRow(Suspension, HandlingPreview.IsVisible ? 1 : 0);
        Grid.SetRowSpan(Suspension, HandlingPreview.IsVisible ? 1 : 2);
        SuspensionUnavailable.IsVisible = !useDiagram && !HandlingPreview.IsVisible;
        if (SuspensionUnavailable.IsVisible)
        {
            SuspensionUnavailable.Text = _viewModel?.PreviewStatus is { Length: > 0 } status
                ? status : "Choose a preview model on the Overview tab.";
        }
    }

    private static double? ParseTravel(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) &&
        double.IsFinite(result) ? result : null;

    private VehicleEditorField? FindField(string key) => _fields.FirstOrDefault(field => field.Key == key);

    private IEnumerable<VehicleEditorField> FieldsFor(params string[] keys) =>
        keys.Select(FindField).OfType<VehicleEditorField>();

    private void AddGroup(StackPanel target, string title, string[] keys, bool expanded)
    {
        var rows = new StackPanel { Spacing = 5 };
        AddRows(rows, FieldsFor(keys));
        if (rows.Children.Count > 0)
            target.Children.Add(CreateGroup(title, rows, expanded));
    }

    private static Expander CreateGroup(string title, Control rows, bool expanded) =>
        new() { Header = title, Content = rows, IsExpanded = expanded, Classes = { "vehicleGroup" } };

    private void AddRows(StackPanel target, IEnumerable<VehicleEditorField> fields, bool showKey = false)
    {
        foreach (VehicleEditorField field in fields)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,*"), ColumnSpacing = 7 };
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap });
            if (showKey)
            {
                label.Children.Add(new TextBlock
                {
                    Text = field.Key,
                    Classes = { "vehicleMuted" },
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            ToolTip.SetTip(label, field.Key);
            row.Children.Add(label);
            row.Children.Add(Cell(WithFieldError(field), 0, 1));
            target.Children.Add(row);
        }
    }

    private Control WithFieldError(VehicleEditorField field)
    {
        var error = new TextBlock { Classes = { "vehicleFieldError" }, TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(field.Error)) { Source = field });
        error.Bind(IsVisibleProperty, new Binding(nameof(field.Error))
        {
            Source = field,
            Converter = StringConverters.IsNotNullOrEmpty
        });
        return new StackPanel { Children = { CreateFieldEditor(field), error } };
    }

    private Control CreateFieldEditor(VehicleEditorField field)
    {
        var value = new Binding(nameof(field.Value))
        {
            Source = field,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        bool editable = _viewModel?.IsEditable == true;
        Control editor;
        if (field.ReferenceType is not null)
        {
            bool canTypeAlias = editable && field.ReferenceType == XAssetType.Sound;
            var current = new TextBox { IsReadOnly = !canTypeAlias, Classes = { "vehicleInput" }, PlaceholderText = "Unassigned" };
            current.Bind(TextBox.TextProperty, canTypeAlias ? value :
                new Binding(nameof(field.Value)) { Source = field, Mode = BindingMode.OneWay });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5 };
            row.Children.Add(current);
            if (editable)
            {
                var choose = new Button { Content = "Choose…" };
                choose.Click += async (_, _) => await PickReferenceAsync(field);
                row.Children.Add(Cell(choose, 0, 1));
            }
            AutomationProperties.SetName(current, field.Label);
            return row;
        }
        if (field.IsBoolean)
        {
            var choice = new CheckBox
            {
                IsEnabled = editable,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            value.Converter = BooleanValueConverter.Instance;
            choice.Bind(CheckBox.IsCheckedProperty, value);
            editor = choice;
        }
        else if (field.Choices.Count > 0)
        {
            IReadOnlyList<string> choices = field.Choices.Contains(field.Value)
                ? field.Choices
                : [field.Value, .. field.Choices];
            var choice = new ComboBox
            {
                ItemsSource = choices,
                IsEnabled = editable,
                Classes = { "vehicleInput" }
            };
            choice.Bind(ComboBox.SelectedItemProperty, value);
            editor = choice;
        }
        else
        {
            var text = new TextBox { IsReadOnly = !editable, Classes = { "vehicleInput" } };
            text.Bind(TextBox.TextProperty, value);
            editor = text;
        }
        AutomationProperties.SetName(editor, field.Label);
        return editor;
    }

    private void FieldFilter_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_initialized && _allFieldsOpen)
            BuildAllFields();
    }

    private void BuildAllFields()
    {
        AllFields.Children.Clear();
        string query = (FieldFilter.Text ?? string.Empty).Trim();
        foreach (IGrouping<string, VehicleEditorField> group in _fields
            .Where(field => Matches(field, query) || Contains(field.Group, query))
            .GroupBy(field => field.Group))
        {
            var rows = new StackPanel { Spacing = 6 };
            AddRows(rows, group, showKey: true);
            AllFields.Children.Add(CreateGroup(group.Key, rows, expanded: true));
        }

        if (AllFields.Children.Count == 0)
            AllFields.Children.Add(new TextBlock { Text = "No matching fields.", Classes = { "vehicleMuted" } });
    }

    private static bool Matches(VehicleEditorField field, string query) =>
        Contains(field.Label, query) || Contains(field.Key, query) || Contains(field.Value, query);

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private sealed class BooleanValueConverter : IValueConverter
    {
        internal static readonly BooleanValueConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string input && bool.TryParse(input, out bool enabled) && enabled;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? bool.TrueString : bool.FalseString;
    }
}
