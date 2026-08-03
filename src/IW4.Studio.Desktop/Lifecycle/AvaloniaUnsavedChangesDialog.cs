using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace IW4.Studio.Desktop.Lifecycle;

/// <summary>
/// Avalonia implementation of the unsaved-change prompt. Closing the modal by
/// its chrome is equivalent to Cancel because Cancel is the enum default.
/// </summary>
internal sealed class AvaloniaUnsavedChangesDialog(Window owner) : IUnsavedChangesDialog
{
    public Task<UnsavedChangesDecision> ShowAsync(UnsavedChangesPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (!owner.IsVisible)
            return Task.FromResult(UnsavedChangesDecision.Cancel);

        return new UnsavedChangesDialogWindow(prompt).ShowDialog<UnsavedChangesDecision>(owner);
    }

    private sealed class UnsavedChangesDialogWindow : Window
    {
        public UnsavedChangesDialogWindow(UnsavedChangesPrompt prompt)
        {
            bool concernsEditorInput =
                prompt.Scope == UnsavedChangesScope.EditorInput;
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            actions.Children.Add(CreateButton("Cancel", UnsavedChangesDecision.Cancel, isDefault: true));
            if (prompt.CanSave)
                actions.Children.Add(CreateButton("Save As…", UnsavedChangesDecision.Save, isDefault: false));
            actions.Children.Add(CreateButton(
                concernsEditorInput ? "Discard input" : "Discard changes",
                UnsavedChangesDecision.DiscardChanges,
                isDefault: false));
            Title = concernsEditorInput ? "Unapplied changes" : "Unsaved changes";
            Width = 480;
            MinWidth = 480;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new Border
            {
                Padding = new Thickness(28, 24),
                Child = new StackPanel
                {
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = CreateHeading(prompt),
                            FontSize = 20,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = CreateDetail(prompt),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = CreateExplanation(prompt),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        actions
                    }
                }
            };
        }

        private Button CreateButton(
            string content,
            UnsavedChangesDecision decision,
            bool isDefault)
        {
            var button = new Button
            {
                Content = content,
                IsDefault = isDefault,
                MinWidth = 116
            };
            button.Click += (_, _) => Close(decision);
            return button;
        }

        private static string CreateHeading(UnsavedChangesPrompt prompt) =>
            prompt.Scope == UnsavedChangesScope.EditorInput
                ? "Discard unapplied editor input?"
                : prompt.CanSave
                    ? "Save changes before continuing?"
                    : "Discard unsaved changes?";

        private static string CreateDetail(UnsavedChangesPrompt prompt) =>
            prompt.Action switch
            {
                DestructiveNavigationAction.CloseEditorTab =>
                    "This tab has unapplied editor input.",
                DestructiveNavigationAction.CloseEditorTabs =>
                    $"{prompt.ChangedItemCount:N0} tabs have unapplied editor input.",
                _ when prompt.Scope == UnsavedChangesScope.EditorInput =>
                    prompt.ChangedItemCount == 1
                        ? "1 open editor tab has unapplied input."
                        : $"{prompt.ChangedItemCount:N0} open editor tabs have unapplied input.",
                _ =>
                    $"{prompt.FastFileName} has " +
                    $"{FormatChangedItemCount(prompt.ChangedItemCount)}."
            };

        private static string CreateExplanation(UnsavedChangesPrompt prompt) =>
            prompt.Action switch
            {
                DestructiveNavigationAction.CloseEditorTab =>
                    "Discarding closes the tab and drops only its unapplied " +
                    "input. Applied asset changes remain pending in the " +
                    "workspace until Save As.",
                DestructiveNavigationAction.CloseEditorTabs =>
                    "Discarding closes the tabs and drops only their unapplied " +
                    "input. Applied asset changes remain pending in the " +
                    "workspace until Save As.",
                _ when prompt.Scope == UnsavedChangesScope.EditorInput =>
                    "Continuing drops the unapplied editor input. Applied " +
                    "workspace changes are handled separately.",
                _ when prompt.CanSave =>
                    "Save writes a validated Save As candidate. Discarding " +
                    "allows this action to continue without saving.",
                _ =>
                    "This zone cannot be saved yet. Discarding allows this " +
                    "action to continue without saving."
            };

        private static string FormatChangedItemCount(int count) =>
            count == 1
                ? "1 unsaved change"
                : $"{count:N0} unsaved changes";
    }
}
