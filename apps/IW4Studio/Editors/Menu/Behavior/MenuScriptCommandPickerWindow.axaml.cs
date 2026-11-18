using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

public sealed partial class MenuScriptCommandPickerWindow : Window
{
    private readonly IReadOnlyList<string> _menuNames;
    private readonly IReadOnlyList<string> _menuNamesWithSelf;
    private readonly IReadOnlyList<string> _itemNames;
    private readonly List<Control> _argumentEditors = [];
    private MenuScriptCommandChoice? _selectedCommand;
    private bool _updatingResults;

    public MenuScriptCommandPickerWindow() : this([], [])
    {
    }

    internal MenuScriptCommandPickerWindow(IReadOnlyList<string> menuNames, IReadOnlyList<string> itemNames)
    {
        _menuNames = menuNames;
        _menuNamesWithSelf = menuNames.Prepend("self").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _itemNames = itemNames;

        InitializeComponent();
        Icon = AppIcon.Create();
        Opened += (_, _) => SearchBox.Focus();
        AddHandler(KeyDownEvent, Shortcut_KeyDown, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(KeyDownEvent, SearchBox_KeyDown, RoutingStrategies.Tunnel);
        UpdateSearchResults();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSearchResults();

    private void UpdateSearchResults()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<MenuScriptCommandChoice> choices = terms.Length == 0
            ? MenuScriptCommandChoice.All
            : MenuScriptCommandChoice.All.Where(choice => terms.All(term =>
                choice.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                choice.Name.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        var exactMatch = choices.FirstOrDefault(choice =>
            choice.Name.Equals(query, StringComparison.OrdinalIgnoreCase));

        _updatingResults = true;
        CommandList.ItemsSource = choices;
        CommandList.SelectedItem = exactMatch ?? (_selectedCommand is not null && choices.Contains(_selectedCommand)
            ? _selectedCommand
            : choices.FirstOrDefault());
        _updatingResults = false;
        if (CommandList.SelectedIndex >= 0)
            CommandList.ScrollIntoView(CommandList.SelectedIndex);
        NoMatches.IsVisible = choices.Count == 0;
        ShowSelectedCommand();
    }

    private void CommandList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingResults)
        {
            ShowSelectedCommand();
        }
    }

    private void ShowSelectedCommand()
    {
        var command = CommandList.SelectedItem as MenuScriptCommandChoice;
        if (command == _selectedCommand)
        {
            return;
        }

        _selectedCommand = command;
        _argumentEditors.Clear();
        ArgumentFields.Children.Clear();
        ArgumentScroll.Offset = default;
        CommandDetails.IsVisible = command is not null;
        if (command is not null)
        {
            CommandLabel.Text = command.Label;
            ArgumentUsage.Text = command.Usage;
            PreviewNote.Text = command.PreviewNote;
            StockExamplePanel.IsVisible = command.StockExample.HasValue;
            if (command.StockExample is { } example)
            {
                StockExampleSource.Text = $"Stock example · {example.Zone} / {example.Menu}";
                StockExampleScript.Text = example.Script;
            }

            foreach (var argument in command.Arguments)
            {
                IReadOnlyList<string> suggestions = argument.Kind switch
                {
                    MenuScriptArgumentKind.Menu => _menuNames,
                    MenuScriptArgumentKind.MenuOrSelf => _menuNamesWithSelf,
                    MenuScriptArgumentKind.Item => _itemNames,
                    _ => argument.KnownValues ?? []
                };
                AddArgumentField(argument, suggestions);
            }
        }

        ArgumentFields.IsVisible = _argumentEditors.Count > 0;
        UpdateNativeScript();
    }

    private void AddArgumentField(MenuScriptCommandArgument argument, IReadOnlyList<string> suggestions)
    {
        Control editor;
        if (argument.Kind is MenuScriptArgumentKind.Choice or MenuScriptArgumentKind.ColorChannel)
        {
            var choices = new ComboBox
            {
                ItemsSource = suggestions,
                PlaceholderText = "Choose a value",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            choices.SelectionChanged += (_, _) => UpdateNativeScript();
            editor = choices;
        }
        else if (suggestions.Count == 0)
        {
            var textBox = new TextBox();
            textBox.TextChanged += (_, _) => UpdateNativeScript();
            editor = textBox;
        }
        else
        {
            var autoComplete = new AutoCompleteBox
            {
                ItemsSource = suggestions,
                MinimumPrefixLength = 0,
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MaxDropDownHeight = 170
            };
            autoComplete.TextChanged += (_, _) => UpdateNativeScript();
            editor = autoComplete;
        }

        AutomationProperties.SetName(editor, argument.Label);
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = argument.Label, FontSize = 11 });
        field.Children.Add(editor);
        if (!string.IsNullOrWhiteSpace(argument.Hint))
        {
            var help = new TextBlock { Text = argument.Hint };
            help.Classes.Add("commandArgumentHint");
            field.Children.Add(help);
        }

        ArgumentFields.Children.Add(field);
        _argumentEditors.Add(editor);
    }

    private bool TryFormatCommand(out string script, out string error)
    {
        if (_selectedCommand is null)
        {
            script = string.Empty;
            error = string.Empty;
            return false;
        }

        string[] values = _argumentEditors.Select(editor => editor switch
        {
            ComboBox choices => choices.SelectedItem as string ?? string.Empty,
            AutoCompleteBox autoComplete => autoComplete.Text ?? string.Empty,
            TextBox textBox => textBox.Text ?? string.Empty,
            _ => string.Empty
        }).ToArray();
        return _selectedCommand.TryFormat(values, out script, out error);
    }

    private void UpdateNativeScript()
    {
        bool canAdd = TryFormatCommand(out string script, out string error);
        NativeScriptBox.Text = script;
        NativeScriptPreview.IsVisible = canAdd;
        FormatError.Text = error;
        FormatError.IsVisible = !canAdd;
        AddButton.IsEnabled = canAdd;
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        int selectedIndex = CommandList.SelectedIndex;
        if (selectedIndex < 0 || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            int offset = e.Key == Key.Up ? -1 : 1;
            int nextIndex = Math.Clamp(selectedIndex + offset, 0, CommandList.Items.Count - 1);
            CommandList.SelectedIndex = nextIndex;
            CommandList.ScrollIntoView(nextIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            FocusCommandEditor();
            e.Handled = true;
        }
    }

    private void CommandList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _selectedCommand is not null)
        {
            FocusCommandEditor();
            e.Handled = true;
        }
    }

    private void FocusCommandEditor() =>
        (_argumentEditors.FirstOrDefault() ?? AddButton).Focus();

    private void Shortcut_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers is not (KeyModifiers.Control or KeyModifiers.Meta))
            return;

        AddSelectedCommand();
        e.Handled = true;
    }

    private async void CopyScriptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && TryFormatCommand(out string script, out _))
            await clipboard.SetTextAsync(script);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) =>
        Close((string?)null);

    private void AddButton_Click(object? sender, RoutedEventArgs e) => AddSelectedCommand();

    private void AddSelectedCommand()
    {
        if (TryFormatCommand(out string script, out _))
        {
            Close(script);
        }
    }
}
