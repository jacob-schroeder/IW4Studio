using Avalonia.Controls;
using Avalonia.Media;
using Iw4Radiant.UserGuide;

namespace Iw4Radiant.Views.UserGuide;

public partial class GuidePathView : UserControl
{
    private GuideProgress? _progress;
    private GuidePath? _path;
    private bool _updating;
    internal event Action<GuidePath, GuideLesson>? LessonSelected;
    internal GuidePath? SelectedPath => _path;
    internal GuideLesson? SelectedLesson => Steps.SelectedItem is ListBoxItem { Tag: GuideLesson lesson } ? lesson : null;

    public GuidePathView() => InitializeComponent();

    internal void Initialize(GuideLibrary library, GuideProgress progress)
    {
        _progress = progress;
        _updating = true;
        PathPicker.Items.Clear();
        foreach (GuidePath path in library.Paths)
            PathPicker.Items.Add(new ComboBoxItem { Content = path.Title, Tag = path });
        PathPicker.SelectedIndex = Math.Max(0, library.Paths.ToList().FindIndex(path => path.Id == progress.Path));
        _updating = false;
        LoadPath(progress.Lesson);
    }

    internal void RefreshProgress()
    {
        if (_path is null || _progress is null) return;
        int complete = _path.Steps.Count(step => _progress.CompletedSteps.Contains(Key(_path, step)));
        Progress.Maximum = _path.Steps.Count;
        Progress.Value = complete;
        ProgressLabel.Text = $"{complete} of {_path.Steps.Count} steps complete";
        for (int i = 0; i < Steps.Items.Count; i++)
        {
            if (Steps.Items[i] is ListBoxItem { Tag: GuideLesson step } item)
                item.Content = new TextBlock { Text = $"{(_progress.CompletedSteps.Contains(Key(_path, step)) ? "✓" : (i + 1).ToString())}   {step.Title}", TextWrapping = TextWrapping.Wrap };
        }
    }

    internal void Move(int direction)
    {
        if (Steps.Items.Count > 0) Steps.SelectedIndex = Math.Clamp(Steps.SelectedIndex + direction, 0, Steps.Items.Count - 1);
    }

    internal static string Key(GuidePath path, GuideLesson lesson) => path.Id + "/" + lesson.Id;

    private void Path_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updating) LoadPath(null);
    }

    private void LoadPath(string? lessonId)
    {
        if (PathPicker.SelectedItem is not ComboBoxItem { Tag: GuidePath path }) return;
        _path = path;
        _updating = true;
        PathSummary.Text = path.Summary;
        Steps.Items.Clear();
        foreach (GuideLesson step in path.Steps) Steps.Items.Add(new ListBoxItem { Tag = step });
        Steps.SelectedIndex = Math.Max(0, path.Steps.FindIndex(step => step.Id == lessonId));
        RefreshProgress();
        _updating = false;
        if (SelectedLesson is { } selected) LessonSelected?.Invoke(path, selected);
    }

    private void Step_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updating && _path is { } path && SelectedLesson is { } step) LessonSelected?.Invoke(path, step);
    }
}
