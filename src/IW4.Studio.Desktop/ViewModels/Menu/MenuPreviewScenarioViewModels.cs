using System.Globalization;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public enum MenuPreviewMode
{
    Authored,
    Scenario
}

public sealed record MenuPreviewFocusOption(MenuNodeId? ItemId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One explicit value in the editor-only scenario. IsSet distinguishes an
/// intentionally empty String from a value omitted to expose Unknown state.
/// </summary>
public sealed class MenuPreviewScenarioInputViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSet;
    private string _valueInput;

    internal MenuPreviewScenarioInputViewModel(
        MenuDebugDependency dependency,
        Action changed,
        bool isSet = false,
        string? valueInput = null)
    {
        Dependency = dependency ?? throw new ArgumentNullException(
            nameof(dependency));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _isSet = isSet;
        _valueInput = valueInput ?? DefaultValue(dependency.ValueKind);
    }

    internal MenuDebugDependency Dependency { get; }

    internal string Identity => Dependency.Kind ==
        MenuDebugDependencyKind.Environment
            ? string.Join(
                ':',
                Dependency.Kind,
                Dependency.Operation?.ToString() ?? string.Empty,
                Dependency.Name)
            : string.Join(':', Dependency.Kind, Dependency.Name);

    public string KindLabel => Dependency.Kind switch
    {
        MenuDebugDependencyKind.Dvar => "Dvar",
        MenuDebugDependencyKind.LocalVariable => "Local",
        MenuDebugDependencyKind.Environment => "Environment",
        MenuDebugDependencyKind.Menu => "Open Menu",
        _ => Dependency.Kind.ToString()
    };

    public string Name => Dependency.Name;

    public string ValueHint => Dependency.ValueKind?.ToString() ?? "String";

    public bool IsSet
    {
        get => _isSet;
        set
        {
            if (!SetProperty(ref _isSet, value))
                return;
            NotifyValidationChanged();
            _changed();
        }
    }

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _valueInput, value))
                return;
            NotifyValidationChanged();
            _changed();
        }
    }

    public bool HasValidationError =>
        IsSet && !TryGetValue(out _, out _);

    public string? ValidationMessage
    {
        get
        {
            TryGetValue(out _, out string? error);
            return error;
        }
    }

    internal bool TryGetValue(out MenuDebugValue value, out string? error)
    {
        error = null;
        MenuDebugValueKind kind = Dependency.ValueKind ??
            MenuDebugValueKind.String;
        switch (kind)
        {
            case MenuDebugValueKind.Integer when
                int.TryParse(
                    ValueInput,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int integer):
                value = MenuDebugValue.FromInt(integer);
                return true;
            case MenuDebugValueKind.Float when
                float.TryParse(
                    ValueInput,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float floatingPoint) &&
                float.IsFinite(floatingPoint):
                value = MenuDebugValue.FromFloat(floatingPoint);
                return true;
            case MenuDebugValueKind.Boolean when
                TryBoolean(ValueInput, out bool boolean):
                value = MenuDebugValue.FromBoolean(boolean);
                return true;
            case MenuDebugValueKind.String:
                value = MenuDebugValue.FromString(ValueInput);
                return true;
            default:
                value = default;
                error = $"Enter a valid {kind} value.";
                return false;
        }
    }

    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private static string DefaultValue(MenuDebugValueKind? kind) => kind switch
    {
        MenuDebugValueKind.Boolean => "false",
        MenuDebugValueKind.Integer => "0",
        MenuDebugValueKind.Float => "0",
        _ => string.Empty
    };

    private static bool TryBoolean(string text, out bool value)
    {
        if (bool.TryParse(text, out value))
            return true;
        if (text == "1")
        {
            value = true;
            return true;
        }
        if (text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }
}
