using Avalonia.Controls;
using Avalonia.Interactivity;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class ViewportWorkspace
{
    private string? _buildProblem;
    private SelectionPath? _buildErrorLocation;
    private Func<SelectionPath, bool>? _buildNavigator;
    private Func<Task>? _previewBuild;

    internal void ShowConsole()
    {
        if (_dialogs?.BlocksInput == true) return;
        OutputTabs.SelectedIndex = 0;
        OutputDrawerToggle.IsChecked = true;
    }

    internal void SetBuildResult(string output, string? problem, SelectionPath? location,
        Func<SelectionPath, bool> navigate, Func<Task>? preview)
    {
        BuildOutputText.Text = output;
        _buildProblem = problem;
        _buildErrorLocation = location;
        _buildNavigator = navigate;
        _previewBuild = preview;
        BuildProblemText.Text = problem ?? "";
        BuildProblemText.IsVisible = problem is not null;
        ShowBuildProblemButton.IsVisible = problem is not null && location is not null;
        ShowBuildProblemButton.IsEnabled = ShowBuildProblemButton.IsVisible;
        PreviewBuildButton.IsVisible = preview is not null;
        RefreshProblemsSummary();
        if (problem is not null) ShowConsole();
    }

    private void RefreshConsoleOutput()
    {
        RendererErrorText.Text = string.Join(Environment.NewLine + Environment.NewLine,
            RendererProblems());
        RefreshProblemsSummary();
    }

    private string[] RendererProblems() =>
        new[] { CameraView.RendererError, _walkError, _glassShatterError, CameraView.WalkPlayerError }
            .OfType<string>().Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();

    private void RefreshProblemsSummary()
    {
        int count = RendererProblems().Length + (_buildProblem is null ? 0 : 1);
        ProblemsSummary.Text = count == 0 ? "No problems reported" :
            $"{count} {(count == 1 ? "problem" : "problems")}";
        OutputDrawerToggle.Content = count == 0 ? "Problems and output" :
            $"Problems and output · {count} {(count == 1 ? "problem" : "problems")}";
    }

    private void OutputDrawer_Changed(object? sender, RoutedEventArgs e)
    {
        if (OutputDrawer is not null)
            OutputDrawer.IsVisible = OutputDrawerToggle.IsChecked == true;
    }

    private void CloseOutput_Click(object? sender, RoutedEventArgs e) => OutputDrawerToggle.IsChecked = false;

    private void ShowBuildProblem_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput == true || _buildErrorLocation is not { } location || _buildNavigator is null) return;
        if (_buildNavigator(location)) return;
        ShowBuildProblemButton.IsEnabled = false;
        BuildProblemText.Text = (_buildProblem ?? "") + Environment.NewLine +
            "This object is no longer selectable. Check its visibility and rebuild.";
    }

    private async void PreviewBuild_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs?.BlocksInput == true || _previewBuild is null) return;
        await _previewBuild();
    }
}
