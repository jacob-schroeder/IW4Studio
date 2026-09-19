using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Keeps icon names in the Studio tool registry so registering a tool does not
/// require another shell-specific XAML branch.
/// </summary>
public sealed class MaterialIconKindConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is string token &&
        Enum.TryParse(token, ignoreCase: true, out MaterialIconKind kind)
            ? kind
            : MaterialIconKind.HelpCircleOutline;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
