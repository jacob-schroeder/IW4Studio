using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed partial class ScriptStringsWindow : Window
{
    private readonly ObservableCollection<ZoneScriptStringViewModel> _rows = [];
    private readonly ZoneDetailsToolViewModel? _details;
    private ScriptStringEditSnapshot? _snapshot;
    private int _usedStringCount;

    public ScriptStringsWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
    }

    internal ScriptStringsWindow(ZoneDetailsToolViewModel details) : this()
    {
        _details = details;
        Title = $"Edit script strings — {details.ZoneName}";
        Opened += LoadScriptStrings;
    }

    private async void LoadScriptStrings(object? sender, EventArgs e)
    {
        if (_details is null)
            return;
        try
        {
            var snapshot = await Task.Run(_details.CaptureScriptStrings);
            if (!IsVisible)
                return;

            _snapshot = snapshot;
            foreach (var row in snapshot.Rows)
                _rows.Add(new ZoneScriptStringViewModel(row.Index, row.Text, row.AssetUseCount));

            StringsList.ItemsSource = _rows;
            _usedStringCount = snapshot.Rows.Count(row => row.AssetUseCount > 0);
            RefreshSummary();
            ApplyButton.IsEnabled = true;
            if (_rows.Count > 0)
                StringsList.SelectedIndex = _rows.Count > 1 ? 1 : 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or ArgumentException or NotSupportedException or OverflowException or KeyNotFoundException)
        {
            UsageSummary.Text = "Asset usage could not be determined.";
            ShowError(exception.Message);
        }
    }

    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var row = StringsList.SelectedItem as ZoneScriptStringViewModel;
        ValueEditor.DataContext = row;
        ValueEditor.IsEnabled = row is not null;
        ValueEditor.IsReadOnly = row?.CanEdit != true;
        RemoveButton.IsEnabled = row?.CanEdit == true;
        EditLabel.Text = row switch
        {
            { Index: 0 } => "Slot 0 is reserved.",
            { AssetUseCount: 1 } => "Required by 1 asset.",
            { AssetUseCount: > 1 } => $"Required by {row.AssetUseCount:N0} assets.",
            _ => "Selected string"
        };
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
            _rows.Add(new ZoneScriptStringViewModel(0, null));
        var added = new ZoneScriptStringViewModel(_rows.Count, string.Empty);
        _rows.Add(added);
        StringsList.SelectedItem = added;
        StringsList.ScrollIntoView(added);
        ValueEditor.Focus();
        RefreshSummary();
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (StringsList.SelectedItem is not ZoneScriptStringViewModel { CanEdit: true } selected)
            return;
        int index = selected.Index;
        _rows.RemoveAt(index);
        for (int i = index; i < _rows.Count; i++)
            _rows[i].Index = i;
        if (_rows.Count == 1 && _snapshot?.Rows.Count == 0)
            _rows.Clear();
        StringsList.SelectedIndex = Math.Min(index, _rows.Count - 1);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        UsageSummary.Text = $"{_usedStringCount:N0} of {_rows.Count:N0} strings used by assets";
        AddButton.IsEnabled = _rows.Count <= ushort.MaxValue;
    }

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (_details is null || _snapshot is null)
            return;
        try
        {
            string?[] values = _rows.Select(row => row.Text).ToArray();
            _details.ApplyScriptStrings(_snapshot, values);
            Close();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or ArgumentException or NotSupportedException or OverflowException)
        {
            ShowError(exception.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
