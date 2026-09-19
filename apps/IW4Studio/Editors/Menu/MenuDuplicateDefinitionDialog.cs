using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IW4.Studio.Desktop.Editors.Menu;

/// <summary>
/// Names the independent Menu asset created by duplicating one inline
/// MenuFile definition.
/// </summary>
internal sealed class MenuDuplicateDefinitionDialog : Window
{
    private readonly Func<string, string?> _validateName;
    private readonly TextBox _nameTextBox;
    private readonly TextBlock _validationTextBlock;
    private readonly Button _duplicateButton;

    private MenuDuplicateDefinitionDialog(
        string sourceName,
        Func<string, string?> validateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _validateName = validateName ??
            throw new ArgumentNullException(nameof(validateName));

        Title = "Duplicate Menu as new";
        Icon = AppIcon.Create();
        Width = 500;
        MinWidth = 500;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameTextBox = new TextBox
        {
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Text = $"{sourceName}_copy",
            PlaceholderText = "Enter a unique Menu name"
        };
        _nameTextBox.TextChanged += (_, _) => RefreshValidation();

        _validationTextBlock = new TextBlock
        {
            MinHeight = 16,
            FontSize = 9,
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap
        };

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 92
        };
        cancel.Click += (_, _) => Close(null);

        _duplicateButton = new Button
        {
            Content = "Duplicate",
            IsDefault = true,
            MinWidth = 104
        };
        _duplicateButton.Classes.Add("primary");
        _duplicateButton.Click += (_, _) => Submit();

        Content = new Border
        {
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 5,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Duplicate as a new Menu",
                                FontSize = 19,
                                FontWeight = FontWeight.SemiBold
                            },
                            new TextBlock
                            {
                                Text =
                                    $"Clone the complete inline definition for " +
                                    $"'{sourceName}' under an independent asset identity.",
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "New Menu name",
                                FontSize = 10,
                                FontWeight = FontWeight.SemiBold
                            },
                            _nameTextBox,
                            _validationTextBlock
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, _duplicateButton }
                    }
                }
            }
        };

        Opened += (_, _) =>
        {
            _nameTextBox.Focus();
            _nameTextBox.SelectAll();
        };
        RefreshValidation();
    }

    public static Task<string?> ShowAsync(
        Window owner,
        string sourceName,
        Func<string, string?> validateName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new MenuDuplicateDefinitionDialog(
            sourceName,
            validateName).ShowDialog<string?>(owner);
    }

    private void RefreshValidation()
    {
        string name = _nameTextBox.Text ?? string.Empty;
        string? validationMessage = string.IsNullOrWhiteSpace(name)
            ? "Name is required."
            : _validateName(name);
        _validationTextBlock.Text = validationMessage ?? string.Empty;
        _duplicateButton.IsEnabled = validationMessage is null;
    }

    private void Submit()
    {
        string name = _nameTextBox.Text ?? string.Empty;
        string? validationMessage = string.IsNullOrWhiteSpace(name)
            ? "Name is required."
            : _validateName(name);
        if (validationMessage is not null)
        {
            RefreshValidation();
            return;
        }

        Close(name);
    }
}
