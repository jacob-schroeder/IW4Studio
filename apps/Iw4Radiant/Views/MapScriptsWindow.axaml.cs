using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using IW4.Gsc.Syntax;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MapScriptsWindow : Window
{
    private static readonly IBrush KeywordBrush = Brush.Parse("#C4A1EE");
    private static readonly IBrush StringBrush = Brush.Parse("#9ACA9E");
    private static readonly IBrush FunctionBrush = Brush.Parse("#9FC4EE");
    private static readonly IBrush NumberBrush = Brush.Parse("#DEC492");
    private static readonly IBrush CommentBrush = Brush.Parse("#A4AAB6");
    private readonly EditorSession _session;
    private readonly Func<string?> _findAssets;
    private readonly Func<string?> _findBootstrap;
    private readonly Func<IReadOnlyList<MapEntity>, string?> _showInMap;
    private readonly GscSyntaxAnalyzer _syntax = new();
    private MapScriptsSnapshot? _snapshot;
    private MapScriptSource? _selected;
    private MapDocument? _snapshotDocument;
    private string? _snapshotPath;
    private string? _displayPath;
    private long _snapshotRevision;
    private int[] _lineOffsets = [0];
    private int _searchOffset;
    private int _navigationRequest;
    private Task _refreshTask = Task.CompletedTask;
    private bool _updatingSelection, _refreshing, _closed;

    internal MapScriptsWindow(EditorSession session, Func<string?> findAssets, Func<string?> findBootstrap,
        Func<IReadOnlyList<MapEntity>, string?> showInMap)
    {
        _session = session;
        _findAssets = findAssets;
        _findBootstrap = findBootstrap;
        _showInMap = showInMap;
        InitializeComponent();
        SourceText.PropertyChanged += (_, e) =>
        {
            if (e.Property == SelectableTextBlock.SelectionStartProperty ||
                e.Property == SelectableTextBlock.SelectionEndProperty) UpdateMapNavigation();
        };
        _session.Changed += Session_Changed;
        Opened += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _session.Changed -= Session_Changed;
        };
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private Task RefreshAsync() => _refreshTask.IsCompleted ? _refreshTask = RefreshCoreAsync() : _refreshTask;

    private async Task RefreshCoreAsync()
    {
        if (_refreshing || _closed) return;
        if (_session.FilePath is not { } path)
        {
            _displayPath = null;
            ClearSource();
            MapNameText.Text = "Untitled map";
            FreshnessText.Text = "· Read only";
            ShowNotice("Save the map once to give its generated scripts an exact filename, then refresh.");
            return;
        }

        _refreshing = true;
        ClearLocations();
        UpdateMapNavigation();
        _displayPath = path;
        RefreshButton.IsEnabled = false;
        FreshnessText.Text = "· Refreshing · Read only";
        MapNameText.Text = Path.GetFileNameWithoutExtension(path);
        string? selectedName = _selected?.Name;
        int selectionStart = SourceText.SelectionStart, selectionEnd = SourceText.SelectionEnd;
        Vector scroll = SourceScroll.Offset;
        try
        {
            MapDocument document = _session.Document;
            long revision = _session.ContentRevision;
            MapDocument source = document.Clone();
            string? assets = _findAssets(), bootstrap = _findBootstrap();
            MapScriptsSnapshot snapshot = await Task.Run(() => MapScriptsSnapshot.Create(source, path, assets, bootstrap));
            if (_closed || _session.FilePath != path) return;
            _snapshot = snapshot;
            _snapshotDocument = document;
            _snapshotRevision = revision;
            _snapshotPath = path;
            MapNameText.Text = snapshot.MapName;
            _updatingSelection = true;
            GeneratedFiles.ItemsSource = snapshot.Generated;
            AnimationFiles.ItemsSource = snapshot.AnimationHelpers;
            NoAnimationsText.IsVisible = snapshot.AnimationHelpers.Count == 0;
            NoAnimationsText.Text = "No animation helpers for this map.";
            MapScriptSource selected = snapshot.Generated.Concat(snapshot.AnimationHelpers)
                .FirstOrDefault(file => file.Name == selectedName) ?? snapshot.Generated[0];
            if (snapshot.Generated.Contains(selected))
            {
                GeneratedFiles.SelectedItem = selected;
                AnimationFiles.SelectedItem = null;
            }
            else
            {
                AnimationFiles.SelectedItem = selected;
                GeneratedFiles.SelectedItem = null;
            }
            _updatingSelection = false;
            ShowSource(selected);
            if (selected.Name == selectedName && selected.Source is { } text)
            {
                SourceText.SelectionStart = Math.Clamp(selectionStart, 0, text.Length);
                SourceText.SelectionEnd = Math.Clamp(selectionEnd, 0, text.Length);
                Dispatcher.UIThread.Post(() => { if (!_closed && _selected == selected) SourceScroll.Offset = scroll; });
            }
            UpdateFreshness();
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            if (_closed || _session.FilePath != path) return;
            ClearSource();
            FreshnessText.Text = "· Source unavailable · Read only";
            ShowNotice(exception.Message);
        }
        finally
        {
            _updatingSelection = false;
            _refreshing = false;
            if (!_closed)
            {
                RefreshButton.IsEnabled = true;
                UpdateMapNavigation();
            }
        }
    }

    private void Session_Changed(object? sender, EventArgs e)
    {
        if (_displayPath != _session.FilePath)
        {
            _displayPath = _session.FilePath;
            ClearSource();
            MapNameText.Text = _session.FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : "Untitled map";
            FreshnessText.Text = "· Refresh needed · Read only";
            ShowNotice("The active map or its filename changed. Refresh to view its scripts.");
            return;
        }
        UpdateFreshness();
    }

    private void UpdateFreshness()
    {
        UpdateMapNavigation();
        if (_snapshot is null) return;
        FreshnessText.Text = IsSnapshotCurrent
            ? (_session.IsDirty ? "· Current map · Unsaved edits · Read only" : "· Current map · Read only")
            : "· Out of date · Read only";
    }

    private bool IsSnapshotCurrent => _snapshot is not null && ReferenceEquals(_snapshotDocument, _session.Document) &&
        _snapshotRevision == _session.ContentRevision && _snapshotPath == _session.FilePath;

    private void ClearSource()
    {
        _snapshot = null;
        _snapshotDocument = null;
        _snapshotPath = null;
        _selected = null;
        _updatingSelection = true;
        GeneratedFiles.ItemsSource = null;
        AnimationFiles.ItemsSource = null;
        _updatingSelection = false;
        NoAnimationsText.IsVisible = true;
        NoAnimationsText.Text = "Refresh to inspect animation dependencies.";
        SourceText.Inlines?.Clear();
        SourceText.ClearSelection();
        LineNumbers.Text = LineCountText.Text = "";
        AssetPathText.Text = "Map source";
        ToolTip.SetTip(AssetPathText, null);
        SourceKindText.Text = "Generated GSC";
        CopySourceButton.IsEnabled = false;
        FooterText.Text = "Read-only source · Ctrl+F to find · Ctrl+G to go to line";
        ToolTip.SetTip(FooterText, null);
        _lineOffsets = [0];
        FindBar.IsVisible = GoToBar.IsVisible = false;
        _searchOffset = 0;
        ClearLocations();
        UpdateMapNavigation();
    }

    private void GeneratedFiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || GeneratedFiles.SelectedItem is not MapScriptSource file) return;
        _updatingSelection = true;
        AnimationFiles.SelectedItem = null;
        _updatingSelection = false;
        ShowSource(file);
    }

    private void AnimationFiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || AnimationFiles.SelectedItem is not MapScriptSource file) return;
        _updatingSelection = true;
        GeneratedFiles.SelectedItem = null;
        _updatingSelection = false;
        ShowSource(file);
    }

    private void ShowSource(MapScriptSource file, bool keepLocations = false)
    {
        if (!keepLocations) ClearLocations();
        _selected = file;
        AssetPathText.Text = file.Name;
        ToolTip.SetTip(AssetPathText, file.Name);
        SourceKindText.Text = _snapshot?.AnimationHelpers.Contains(file) == true
            ? (file.Name.EndsWith(".atr", StringComparison.Ordinal) ? "Animation tree" : "Animation helper")
            : "Generated GSC";
        FooterText.Text = file.Origin ?? "Read-only source · Ctrl+F to find · Ctrl+G to go to line";
        ToolTip.SetTip(FooterText, file.Origin);
        ShowNotice(file.Error ?? _snapshot?.Warning);
        SourceText.ClearSelection();
        SourceText.Inlines?.Clear();
        SourceScroll.Offset = default;
        _searchOffset = 0;
        string source = file.Source ?? "";
        CopySourceButton.IsEnabled = file.Source is not null;
        var offsets = new List<int> { 0 };
        for (int index = 0; index < source.Length; index++)
            if (source[index] == '\n') offsets.Add(index + 1);
        _lineOffsets = offsets.ToArray();
        LineNumbers.Text = source.Length == 0 ? "" : string.Join('\n', Enumerable.Range(1, offsets.Count));
        LineCountText.Text = file.Source is null ? "Unavailable" : $"{offsets.Count} lines";
        UpdateMapNavigation();
        if (source.Length == 0) return;

        IReadOnlyList<GscToken> tokens = _syntax.Analyze(source).Tokens;
        int position = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            GscToken token = tokens[index];
            AddRun(source[position..token.Span.Start], CommentBrush);
            IBrush? brush = token.Kind switch
            {
                GscTokenKind.String or GscTokenKind.LocalizedString => StringBrush,
                GscTokenKind.Integer or GscTokenKind.Float => NumberBrush,
                GscTokenKind.IncludeDirective or GscTokenKind.UsingAnimTreeDirective or GscTokenKind.AnimTreeDirective => KeywordBrush,
                GscTokenKind.Identifier when index + 1 < tokens.Count && tokens[index + 1].Kind == GscTokenKind.OpenParenthesis => FunctionBrush,
                _ when token.Kind.ToString().EndsWith("Keyword", StringComparison.Ordinal) => KeywordBrush,
                _ => null
            };
            AddRun(source[token.Span.Start..token.Span.End], brush);
            position = token.Span.End;
        }
        AddRun(source[position..], null);
    }

    internal async Task ShowEntityAsync(MapEntity entity)
    {
        int request = ++_navigationRequest;
        MapDocument document = _session.Document;
        long revision = _session.ContentRevision;
        string? path = _session.FilePath;
        if (!IsSnapshotCurrent || _refreshing) await RefreshAsync();
        if (_closed || request != _navigationRequest) return;
        if (!ReferenceEquals(document, _session.Document) || revision != _session.ContentRevision || path != _session.FilePath)
        {
            ShowNotice("The map changed while its scripts were loading. Choose Show script again.");
            return;
        }
        if (!IsSnapshotCurrent || _snapshot is null) return;
        int entityIndex = document.Entities.IndexOf(entity);
        if (entityIndex < 0) return;
        var locations = _snapshot.Generated.SelectMany(file => _snapshot.EntitySpans
                .Where(span => span.ScriptName == file.Name && span.EntityIndex == entityIndex && span.IsPrimary)
                .OrderBy(span => span.Start)).Distinct().ToArray();
        ClearLocations();
        if (locations.Length == 0)
        {
            ShowNotice($"No generated code is linked to this {entity.ClassName}. Links are available for FX, sounds, moving lights, global fog and prefabs containing them.");
            return;
        }
        if (locations.Length > 1)
        {
            SourceLocations.ItemsSource = locations.Select(span =>
            {
                MapScriptSource file = _snapshot.Generated.First(file => file.Name == span.ScriptName);
                int line = file.Source is { } source ? 1 + source.AsSpan(0, span.Start).Count('\n') : 1;
                return new ComboBoxItem { Content = $"{file.Role} · line {line}", Tag = span };
            }).ToArray();
            SourceLocationsPanel.IsVisible = true;
            SourceLocations.SelectedIndex = 0;
        }
        else NavigateToSource(locations[0]);
    }

    private void ClearLocations()
    {
        SourceLocationsPanel.IsVisible = false;
        SourceLocations.ItemsSource = null;
    }

    private void SourceLocations_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SourceLocations.SelectedItem is ComboBoxItem { Tag: MapScriptEntitySpan span }) NavigateToSource(span);
    }

    private void NavigateToSource(MapScriptEntitySpan span)
    {
        if (!IsSnapshotCurrent || _refreshing || _snapshot?.Generated.FirstOrDefault(file => file.Name == span.ScriptName) is not { } file)
            return;
        _updatingSelection = true;
        GeneratedFiles.SelectedItem = file;
        AnimationFiles.SelectedItem = null;
        _updatingSelection = false;
        ShowSource(file, keepLocations: true);
        SourceText.SelectionStart = span.Start;
        SourceText.SelectionEnd = span.Start + span.Length;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed || !IsSnapshotCurrent || _selected != file) return;
            SelectRange(span.Start, span.Length);
            SourceText.Focus();
        }, DispatcherPriority.Loaded);
    }

    private int[] SelectedEntityIndices()
    {
        if (!IsSnapshotCurrent || _refreshing || _snapshot is null || _selected is null) return [];
        int start = Math.Min(SourceText.SelectionStart, SourceText.SelectionEnd);
        int end = Math.Max(SourceText.SelectionStart, SourceText.SelectionEnd);
        return _snapshot.EntitySpans.Where(span => span.ScriptName == _selected.Name &&
                (start == end ? start >= span.Start && start < span.Start + span.Length
                    : start < span.Start + span.Length && end > span.Start))
            .Select(span => span.EntityIndex).Distinct().ToArray();
    }

    private void UpdateMapNavigation()
    {
        int count = SelectedEntityIndices().Length;
        ShowInMapButton.IsEnabled = ShowInMapMenu.IsEnabled = count > 0;
        SourceLocations.IsEnabled = IsSnapshotCurrent && !_refreshing;
        string tip = !IsSnapshotCurrent || _refreshing ? "Refresh the scripts to navigate to the current map."
            : count == 0 ? "Place the cursor in generated entity code, or select a block, to locate its map source."
            : count == 1 ? "Select and frame the source entity or prefab instance in the map."
            : $"Select and frame the {count} source entities or prefab instances in the map.";
        ToolTip.SetTip(ShowInMapButton, tip);
        ToolTip.SetTip(ShowInMapMenu, tip);
    }

    private void ShowInMap_Click(object? sender, RoutedEventArgs e)
    {
        int[] indices = SelectedEntityIndices();
        if (indices.Length == 0) return;
        MapDocument document = _session.Document;
        if (indices.Any(index => index < 0 || index >= document.Entities.Count)) return;
        string? error = _showInMap(indices.Select(index => document.Entities[index]).ToArray());
        if (error is not null) ShowNotice(error);
        else WindowState = WindowState.Minimized;
    }

    private void AddRun(string text, IBrush? brush)
    {
        if (text.Length == 0) return;
        var run = new Run(text);
        if (brush is not null) run.Foreground = brush;
        SourceText.Inlines?.Add(run);
    }

    private void ShowNotice(string? text)
    {
        NoticeText.Text = text;
        NoticePanel.IsVisible = !string.IsNullOrEmpty(text);
    }

    private async void CopySource_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected?.Source is not { } source) return;
        try
        {
            if (Clipboard is not { } clipboard)
            {
                ShowNotice("The clipboard is unavailable.");
                return;
            }
            await clipboard.SetTextAsync(source);
            FooterText.Text = "Source copied.";
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            ShowNotice($"Cannot copy source: {exception.Message}");
        }
    }

    private void CopySelection_Click(object? sender, RoutedEventArgs e) => SourceText.Copy();
    private void SelectAll_Click(object? sender, RoutedEventArgs e) => SourceText.SelectAll();
    private void Find_Click(object? sender, RoutedEventArgs e) => ShowFind();
    private void ShowGoToLine_Click(object? sender, RoutedEventArgs e) => ShowGoToLine();
    private void FindPrevious_Click(object? sender, RoutedEventArgs e) => FindMatch(previous: true);
    private void FindNext_Click(object? sender, RoutedEventArgs e) => FindMatch(previous: false);
    private void CloseNavigation_Click(object? sender, RoutedEventArgs e) => CloseNavigation();

    private void ShowFind()
    {
        if (_selected?.Source is null) return;
        GoToBar.IsVisible = false;
        FindBar.IsVisible = true;
        _searchOffset = SourceText.SelectionStart;
        FindInput.Focus();
        FindInput.SelectAll();
    }

    private void ShowGoToLine()
    {
        if (_selected?.Source is null) return;
        FindBar.IsVisible = false;
        GoToBar.IsVisible = true;
        LineInput.Focus();
        LineInput.SelectAll();
    }

    private void CloseNavigation()
    {
        FindBar.IsVisible = GoToBar.IsVisible = false;
        SourceText.Focus();
    }

    private void FindMatch(bool previous)
    {
        if (_selected?.Source is not { Length: > 0 } source || string.IsNullOrEmpty(FindInput.Text)) return;
        string query = FindInput.Text;
        int offset = Math.Clamp(_searchOffset, 0, source.Length);
        int match = previous
            ? (offset > 0 ? source.LastIndexOf(query, offset - 1, StringComparison.OrdinalIgnoreCase) : -1)
            : source.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
        if (match < 0) match = previous
            ? source.LastIndexOf(query, StringComparison.OrdinalIgnoreCase)
            : source.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (match < 0)
        {
            FooterText.Text = "No matches in this file.";
            return;
        }
        _searchOffset = previous ? match : match + query.Length;
        SelectRange(match, query.Length);
        int lineIndex = Array.BinarySearch(_lineOffsets, match);
        FooterText.Text = $"Match on line {(lineIndex >= 0 ? lineIndex + 1 : ~lineIndex)}";
    }

    private void GoToLine_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected?.Source is not { } source) return;
        if (!int.TryParse(LineInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int line) ||
            line < 1 || line > _lineOffsets.Length)
        {
            FooterText.Text = $"Enter a line from 1 to {_lineOffsets.Length}.";
            return;
        }
        int start = _lineOffsets[line - 1];
        int end = line < _lineOffsets.Length ? _lineOffsets[line] : source.Length;
        SelectRange(start, end - start);
        SourceText.Focus();
        FooterText.Text = $"Line {line}";
    }

    private void SelectRange(int start, int length)
    {
        SourceText.SelectionStart = start;
        SourceText.SelectionEnd = start + length;
        Rect rect = SourceText.TextLayout.HitTestTextPosition(start);
        SourceScroll.Offset = new Vector(Math.Max(0, rect.X - 30), Math.Max(0, rect.Y - 40));
    }

    private void FindInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    private void LineInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        GoToLine_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        bool command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (command && e.Key == Key.F) { ShowFind(); e.Handled = true; }
        else if (command && e.Key == Key.G) { ShowGoToLine(); e.Handled = true; }
        else if (e.Key == Key.Escape && (FindBar.IsVisible || GoToBar.IsVisible)) { CloseNavigation(); e.Handled = true; }
        else if (e.Key == Key.F3) { FindMatch(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
    }
}

internal sealed class MapScriptTextBlock : SelectableTextBlock
{
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        // Reading selections and find highlights survive map edits and toolbar focus.
        int start = SelectionStart, end = SelectionEnd;
        base.OnLostFocus(e);
        SetCurrentValue(SelectionStartProperty, start);
        SetCurrentValue(SelectionEndProperty, end);
    }
}
