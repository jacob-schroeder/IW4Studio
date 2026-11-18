using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Vehicle;

public sealed partial class VehicleEditorView
{
    private static readonly string[] ReferenceSectionOrder =
        ["Physics preset", "Compass icons", "Engine sounds", "Suspension sounds", "Surface sounds", "Driving sounds", "Turret"];

    private void BuildReferenceGroups()
    {
        string? selected = ReferenceGroups.SelectedItem as string;
        string[] groups = ReferenceSectionOrder
            .Where(group => _fields.Any(field => field.ReferenceType is not null && field.Group == group))
            .ToArray();

        _updatingSlots = true;
        try
        {
            ReferenceGroups.ItemsSource = groups;
            ReferenceGroups.SelectedItem = groups.Contains(selected)
                ? selected
                : groups.Contains("Engine sounds") ? "Engine sounds" : groups.FirstOrDefault();
        }
        finally
        {
            _updatingSlots = false;
        }

        UpdateSlots();
    }

    private void ReferenceGroup_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_updatingSlots && ReferenceEquals(e.Source, ReferenceGroups))
            UpdateSlots();
    }

    private void ReferenceFilter_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initialized)
            UpdateSlots();
    }

    private void UpdateSlots()
    {
        string? group = ReferenceGroups.SelectedItem as string;
        string query = (SlotFilter.Text ?? string.Empty).Trim();
        var previous = Slots.SelectedItem as VehicleEditorField;
        VehicleEditorField[] candidates = _fields
            .Where(field => field.ReferenceType is not null && field.Group == group &&
                (ShowEmptySlots.IsChecked == true || !string.IsNullOrWhiteSpace(field.Value)) &&
                Matches(field, query))
            .ToArray();

        ReferenceTitle.Text = group ?? "References";
        AssignedHeading.Text = candidates.Length > 0 && candidates.All(field => field.ReferenceType == XAssetType.Sound)
            ? "Assigned alias" : "Assigned asset";
        NoSlotsMessage.IsVisible = candidates.Length == 0;

        _updatingSlots = true;
        try
        {
            Slots.ItemsSource = candidates;
            Slots.SelectedItem = previous is not null && candidates.Contains(previous) ? previous : candidates.FirstOrDefault();
        }
        finally
        {
            _updatingSlots = false;
        }

        UpdateSlotDetail();
    }

    private void Slot_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_updatingSlots && ReferenceEquals(Slots, e.Source))
            UpdateSlotDetail();
    }

    private void UpdateSlotDetail()
    {
        var field = Slots.SelectedItem as VehicleEditorField;
        SlotTitle.Text = field?.Label ?? "Select a slot";
        ToolTip.SetTip(SlotTitle, field?.Key);
        SlotDetail.Content = field is null ? null : BuildSlotValue(field);
        UpdateReferenceActions();

        VehicleEditorField? audioField = ReferencesPage.IsVisible && field?.ReferenceType == XAssetType.Sound
            ? field : null;
        if (!ReferenceEquals(_audioSelection, audioField))
        {
            _audioSelection = audioField;
            _viewModel?.SelectAudio(audioField);
        }
    }

    private VehicleEditorField? _audioSelection;

    private Control BuildSlotValue(VehicleEditorField field)
    {
        var value = new TextBox { Classes = { "vehicleInput" }, IsReadOnly = true, PlaceholderText = "Unassigned" };
        value.Bind(TextBox.TextProperty, new Binding(nameof(field.Value)) { Source = field, Mode = BindingMode.OneWay });
        string label = field.ReferenceType == XAssetType.Sound ? "Sound alias" : "Asset";
        AutomationProperties.SetName(value, label);
        return new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = label, Classes = { "vehicleMuted" } }, value }
        };
    }

    private void UpdateReferenceActions()
    {
        var field = Slots.SelectedItem as VehicleEditorField;
        bool canEdit = field is not null && _viewModel?.IsEditable == true;
        bool assigned = !string.IsNullOrWhiteSpace(field?.Value);

        ReferenceActions.IsVisible = field is not null;
        ReplaceSlot.IsVisible = canEdit;
        OpenSlot.IsEnabled = assigned;
        ClearSlot.IsVisible = canEdit;
        ClearSlot.IsEnabled = assigned;
        if (field?.ReferenceType == XAssetType.Sound)
        {
            ReplaceSlot.Content = "Replace alias…";
            OpenSlot.Content = "Open alias";
        }
        else
        {
            ReplaceSlot.Content = "Replace asset…";
            OpenSlot.Content = "Open asset";
        }

        AudioUnavailable.IsVisible = field?.ReferenceType == XAssetType.Sound && assigned &&
            _viewModel?.AudioPreview is null;
    }

    private async void ReplaceSlot_Click(object? sender, RoutedEventArgs e)
    {
        if (Slots.SelectedItem is VehicleEditorField field)
            await PickReferenceAsync(field);
    }

    private async Task PickReferenceAsync(VehicleEditorField field)
    {
        if (_viewModel is not { IsEditable: true } model || field.ReferenceType is not { } type ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        string? name = Picker is { } picker
            ? await picker.SelectNameAsync(owner, type, field.Value)
            : await SelectNameAsync(owner, $"Replace {field.Label}", model.ReferenceNames(field), field.Value);

        if (name is not null && ReferenceEquals(_viewModel, model))
            field.Value = name;
    }

    private void OpenSlot_Click(object? sender, RoutedEventArgs e)
    {
        if (Slots.SelectedItem is VehicleEditorField field)
            _viewModel?.OpenReference(field);
    }

    private void ClearSlot_Click(object? sender, RoutedEventArgs e)
    {
        if (Slots.SelectedItem is VehicleEditorField field)
            field.Value = string.Empty;
    }

    private void Audition_Click(object? sender, RoutedEventArgs e) => _viewModel?.AudioPreview?.TogglePlayback();

    private static Task<string?> SelectNameAsync(Window owner, string title, IReadOnlyList<string> names, string value)
    {
        var input = new AutoCompleteBox
        {
            ItemsSource = names,
            Text = value,
            MinimumPrefixLength = 0,
            PlaceholderText = "Search by name",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(input, title);
        var accept = new Button { Content = "Select", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(14), Spacing = 10, Children = { input, accept } }
        };
        accept.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
                dialog.Close(input.Text.Trim());
        };
        dialog.Opened += (_, _) => input.Focus();
        return dialog.ShowDialog<string?>(owner);
    }
}
