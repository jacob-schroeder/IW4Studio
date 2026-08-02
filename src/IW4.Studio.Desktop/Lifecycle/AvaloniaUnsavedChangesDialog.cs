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
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            actions.Children.Add(CreateButton("Cancel", UnsavedChangesDecision.Cancel, isDefault: true));
            if (prompt.CanSave)
                actions.Children.Add(CreateButton("Save As…", UnsavedChangesDecision.Save, isDefault: false));
            actions.Children.Add(CreateButton("Discard changes", UnsavedChangesDecision.DiscardChanges, isDefault: false));
            Title = "Unsaved changes";
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
                            Text = prompt.CanSave ? "Save changes before continuing?" : "Discard unsaved changes?",
                            FontSize = 20,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"{prompt.FastFileName} has {FormatChangedItemCount(prompt.ChangedItemCount)}.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = prompt.CanSave
                                ? "Save writes a validated Save As candidate. Discarding allows this action to continue without saving."
                                : "This zone cannot be saved yet. Discarding allows this action to continue without saving.",
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

        private static string FormatChangedItemCount(int count) =>
            count == 1
                ? "1 unsaved change"
                : $"{count:N0} unsaved changes";
    }
}
