using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Iw4Radiant.UserGuide;
using Iw4Radiant.Views.UserGuide;

namespace Iw4Radiant.Views;

public partial class UserGuideWindow : Window
{
    private readonly GuideLibrary _library;
    private readonly GuideProgress _progress;
    private readonly Func<bool> _save;
    private readonly Action _showControls;
    private readonly List<(string Article, string? Section, double Offset)> _history = [];
    private readonly List<Button> _areaButtons = [];
    private int _historyIndex = -1;
    private string _mode = "learn";
    private string? _articleId;
    private string? _sectionId;
    private bool _ready, _updatingCheckpoint;

    internal UserGuideWindow(GuideLibrary library, GuideProgress progress, Func<bool> save, Action showControls)
    {
        _library = library;
        _progress = progress;
        _save = save;
        _showControls = showControls;
        _progress.Normalize();
        _progress.Bookmarks.RemoveAll(id => !library.Articles.ContainsKey(id));
        InitializeComponent();
        Version? version = typeof(UserGuideWindow).Assembly.GetName().Version;
        VersionText.Text = $"IW4Radiant {version?.Major}.{version?.Minor} · Offline guide";
        ArticleView.NavigateRequested += OpenArticle;
        WorkspaceView.NavigateRequested += OpenArticle;
        PathView.LessonSelected += Lesson_Selected;
        PathView.Initialize(library, progress);
        GuideTour tour = library.Tours[0];
        int selected = Math.Max(0, tour.Hotspots.FindIndex(area => area.Article == progress.Hotspot));
        WorkspaceView.Show(tour, selected);
        BuildAreaList(tour, selected);
        WorkspaceView.HotspotSelected += SelectArea;
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            CapturePosition();
            SaveProgress();
            ArticleView.ReleaseImages();
            WorkspaceView.ReleaseImage();
        };
        _ready = true;
        SetMode(progress.Mode, restore: true);
    }

    internal void OpenArticle(string id, string? section = null)
    {
        if (!_library.Articles.ContainsKey(id)) return;
        CapturePosition();
        SetMode("topics", render: false);
        ShowArticle(id, section);
        BuildTopics();
        SaveProgress();
    }

    private void SetMode(string mode, bool restore = false, bool render = true)
    {
        if (!_ready) return;
        if (!restore) CapturePosition();
        _mode = mode is "topics" or "explore" ? mode : "learn";
        _progress.Mode = _mode;
        LearnButton.Classes.Set("active", _mode == "learn");
        TopicsButton.Classes.Set("active", _mode == "topics");
        ExploreButton.Classes.Set("active", _mode == "explore");
        TopicsPanel.IsVisible = _mode == "topics";
        PathView.IsVisible = _mode == "learn";
        ExplorePanel.IsVisible = _mode == "explore";
        WorkspaceView.IsVisible = _mode == "explore";
        ArticleView.IsVisible = _mode != "explore";
        ArticleToolbar.IsVisible = _mode != "explore";
        LessonFooter.IsVisible = _mode == "learn";
        if (render && _mode == "learn" && PathView.SelectedPath is { } path && PathView.SelectedLesson is { } lesson)
            DisplayLesson(path, lesson, restore ? _progress.ArticleOffset : 0);
        else if (render && _mode == "topics")
        {
            string id = _progress.Article is { } last && _library.Articles.ContainsKey(last) ? last : _library.Articles.Keys.First();
            ShowArticle(id, _progress.Section, restore ? _progress.ArticleOffset : 0);
            BuildTopics();
        }
        StatusText.Text = _mode == "explore" ? "Choose an area of the editor to find its guide." : $"{_library.Articles.Count} guides · Keep this window open while mapping.";
        if (!restore) SaveProgress();
    }

    private void ShowArticle(string id, string? section, double offset = 0, bool addHistory = true)
    {
        CaptureHistoryPosition();
        _articleId = id;
        _sectionId = section;
        _progress.Article = id;
        _progress.Section = section;
        _progress.ArticleOffset = offset;
        ArticleView.Show(_library, _library.Articles[id], offset > 0 ? null : section, offset);
        if (addHistory)
        {
            if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            if (_history.Count == 0 || _history[^1].Article != id || _history[^1].Section != section)
                _history.Add((id, section, offset));
            _historyIndex = _history.Count - 1;
        }
        BookmarkButton.Content = _progress.Bookmarks.Contains(id) ? "✓ Saved article" : "Save article";
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    }

    private void BuildTopics()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        TopicTree.Children.Clear();
        int results = 0;
        foreach (GuideTopic topic in _library.Topics)
        {
            GuideArticle[] articles = _library.InTopic(topic).Where(article => _library.Matches(article, query) &&
                (SavedOnly.IsChecked != true || _progress.Bookmarks.Contains(article.Id))).ToArray();
            if (articles.Length == 0) continue;
            var items = new StackPanel { Spacing = 2 };
            foreach (GuideArticle article in articles)
            {
                var button = new Button { Classes = { "guideNav" }, Content = new TextBlock { Text = article.Title, TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
                button.Classes.Set("active", article.Id == _articleId);
                button.Click += (_, _) => OpenArticle(article.Id);
                items.Children.Add(button);
            }
            results += articles.Length;
            TopicTree.Children.Add(new Expander
            {
                Classes = { "guideTopic" }, Header = new TextBlock { Text = topic.Title, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                Content = items, IsExpanded = query.Length > 0 || SavedOnly.IsChecked == true || articles.Any(article => article.Id == _articleId)
            });
        }
        if (results == 0)
            TopicTree.Children.Add(new TextBlock { Text = SavedOnly.IsChecked == true ? "No saved guides match. Save an article or clear the filter." : "No matching guides. Try a tool name, a task, or a word such as floor, brush, or light.", TextWrapping = TextWrapping.Wrap, Classes = { "guideMuted" }, Margin = new Avalonia.Thickness(8) });
        StatusText.Text = query.Length > 0 ? $"{results} matching guides · Search includes steps and troubleshooting tips." : $"{_library.Articles.Count} guides · Keep this window open while mapping.";
    }

    private void BuildAreaList(GuideTour tour, int selected)
    {
        AreaList.Children.Clear();
        _areaButtons.Clear();
        AreaList.Children.Add(new TextBlock { Text = "WORKSPACE", FontSize = 11, Classes = { "guideMuted" }, Margin = new Avalonia.Thickness(8, 0, 0, 10) });
        for (int i = 0; i < tour.Hotspots.Count; i++)
        {
            int index = i;
            var button = new Button { Classes = { "guideNav" }, Content = new TextBlock { Text = $"{i + 1}. {tour.Hotspots[i].Title}", TextWrapping = TextWrapping.Wrap } };
            button.Classes.Set("active", i == selected);
            button.Click += (_, _) => { WorkspaceView.Select(index); SelectArea(index); };
            _areaButtons.Add(button);
            AreaList.Children.Add(button);
        }
        AreaList.Children.Add(new TextBlock { Text = "Choose a part of the editor even if you don’t know its name yet.", TextWrapping = TextWrapping.Wrap, Classes = { "guideMuted" }, FontSize = 12, Margin = new Avalonia.Thickness(8, 18, 8, 0) });
    }

    private void SelectArea(int index)
    {
        for (int i = 0; i < _areaButtons.Count; i++) _areaButtons[i].Classes.Set("active", i == index);
        _progress.Hotspot = _library.Tours[0].Hotspots[index].Article;
        SaveProgress();
    }

    private void Lesson_Selected(GuidePath path, GuideLesson lesson)
    {
        if (!_ready) return;
        CapturePosition();
        _progress.Path = path.Id;
        _progress.Lesson = lesson.Id;
        if (_mode == "learn") DisplayLesson(path, lesson);
        SaveProgress();
    }

    private void DisplayLesson(GuidePath path, GuideLesson lesson, double offset = 0)
    {
        _progress.Path = path.Id;
        _progress.Lesson = lesson.Id;
        ShowArticle(lesson.Article, lesson.Section, offset);
        _updatingCheckpoint = true;
        CheckpointText.Text = lesson.Checkpoint;
        Checkpoint.IsChecked = _progress.CompletedSteps.Contains(GuidePathView.Key(path, lesson));
        _updatingCheckpoint = false;
        int index = path.Steps.IndexOf(lesson);
        PreviousStep.IsEnabled = index > 0;
        NextStep.IsEnabled = index < path.Steps.Count - 1;
        NextStep.Content = index == path.Steps.Count - 1 ? "Last step" : "Next step →";
    }

    private void CapturePosition()
    {
        if (_articleId is not null && _mode != "explore")
        {
            _progress.Article = _articleId;
            _progress.Section = _sectionId;
            _progress.ArticleOffset = ArticleView.ScrollOffset;
            CaptureHistoryPosition();
        }
    }

    private void CaptureHistoryPosition()
    {
        if (_historyIndex >= 0 && _historyIndex < _history.Count && _mode != "explore")
        {
            var previous = _history[_historyIndex];
            _history[_historyIndex] = (previous.Article, previous.Section, ArticleView.ScrollOffset);
        }
    }

    private void SaveProgress()
    {
        if (!_save()) StatusText.Text = "Guide progress could not be saved. You can keep reading.";
    }

    private void Mode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode }) SetMode(mode);
    }

    private void Search_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (_mode != "topics") SetMode("topics");
        BuildTopics();
    }

    private void SavedOnly_Changed(object? sender, RoutedEventArgs e) { if (_ready) BuildTopics(); }

    private void Bookmark_Click(object? sender, RoutedEventArgs e)
    {
        if (_articleId is null) return;
        if (!_progress.Bookmarks.Remove(_articleId)) _progress.Bookmarks.Add(_articleId);
        BookmarkButton.Content = _progress.Bookmarks.Contains(_articleId) ? "✓ Saved article" : "Save article";
        if (_mode == "topics") BuildTopics();
        CapturePosition();
        SaveProgress();
    }

    private void Checkpoint_Changed(object? sender, RoutedEventArgs e)
    {
        if (_updatingCheckpoint || !_ready || PathView.SelectedPath is not { } path || PathView.SelectedLesson is not { } step) return;
        string key = GuidePathView.Key(path, step);
        _progress.CompletedSteps.Remove(key);
        if (Checkpoint.IsChecked == true) _progress.CompletedSteps.Add(key);
        PathView.RefreshProgress();
        CapturePosition();
        SaveProgress();
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => MoveHistory(-1);
    private void Forward_Click(object? sender, RoutedEventArgs e) => MoveHistory(1);
    private void MoveHistory(int direction)
    {
        int index = _historyIndex + direction;
        if (index < 0 || index >= _history.Count) return;
        CapturePosition();
        SetMode("topics", render: false);
        var destination = _history[index];
        ShowArticle(destination.Article, destination.Section, destination.Offset, addHistory: false);
        _historyIndex = index;
        BackButton.IsEnabled = index > 0;
        ForwardButton.IsEnabled = index < _history.Count - 1;
        BuildTopics();
        SaveProgress();
    }

    private void PreviousStep_Click(object? sender, RoutedEventArgs e) => PathView.Move(-1);
    private void NextStep_Click(object? sender, RoutedEventArgs e) => PathView.Move(1);
    private void Controls_Click(object? sender, RoutedEventArgs e) => _showControls();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.F && e.KeyModifiers == modifier)
        {
            SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right)
        {
            MoveHistory(e.Key == Key.Left ? -1 : 1); e.Handled = true;
        }
    }
}
