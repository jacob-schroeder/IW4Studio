using System.Globalization;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

public enum MenuDebugValueKind
{
    Integer,
    Float,
    Boolean,
    String
}

/// <summary>A typed value supplied to or produced by the menu simulator.</summary>
public readonly record struct MenuDebugValue
{
    private readonly long _integer;
    private readonly double _float;
    private readonly string? _string;

    private MenuDebugValue(
        MenuDebugValueKind kind,
        long integer,
        double floatingPoint,
        string? text)
    {
        Kind = kind;
        _integer = integer;
        _float = floatingPoint;
        _string = text;
    }

    public MenuDebugValueKind Kind { get; }

    public static MenuDebugValue FromInt(int value) =>
        new(MenuDebugValueKind.Integer, value, value, null);

    public static MenuDebugValue FromFloat(float value) =>
        new(MenuDebugValueKind.Float, 0, value, null);

    public static MenuDebugValue FromBoolean(bool value) =>
        new(MenuDebugValueKind.Boolean, value ? 1 : 0, value ? 1 : 0, null);

    public static MenuDebugValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(MenuDebugValueKind.String, 0, 0, value);
    }

    public bool TryGetInt(out int value)
    {
        switch (Kind)
        {
            case MenuDebugValueKind.Integer:
            case MenuDebugValueKind.Boolean:
                value = unchecked((int)_integer);
                return true;
            case MenuDebugValueKind.Float when
                double.IsFinite(_float) &&
                _float >= int.MinValue && _float <= int.MaxValue:
                value = (int)_float;
                return true;
            case MenuDebugValueKind.String:
                value = ParseAtoi(_string);
                return true;
            default:
                value = default;
                return false;
        }
    }

    public bool TryGetFloat(out float value)
    {
        switch (Kind)
        {
            case MenuDebugValueKind.Integer:
            case MenuDebugValueKind.Boolean:
                value = _integer;
                return true;
            case MenuDebugValueKind.Float:
                value = (float)_float;
                return true;
            case MenuDebugValueKind.String:
                value = (float)ParseAtof(_string);
                return true;
            default:
                value = default;
                return false;
        }
    }

    public bool TryGetBoolean(out bool value)
    {
        bool success = TryGetInt(out int integer);
        value = integer != 0;
        return success;
    }

    public string AsString() => Kind switch
    {
        MenuDebugValueKind.String => _string ?? string.Empty,
        MenuDebugValueKind.Boolean => _integer != 0 ? "1" : "0",
        MenuDebugValueKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
        MenuDebugValueKind.Float => ((float)_float).ToString("F6", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    public override string ToString() => AsString();

    internal void GetEngineStringNumbers(out int integer, out double floatingPoint)
    {
        if (Kind != MenuDebugValueKind.String)
            throw new InvalidOperationException("Engine string coercion requires a string value.");
        integer = ParseAtoi(_string);
        floatingPoint = ParseAtof(_string);
    }

    private static int ParseAtoi(string? text)
    {
        ReadOnlySpan<char> source = text.AsSpan();
        int index = SkipWhitespace(source);
        bool negative = false;
        if (index < source.Length && source[index] is '+' or '-')
        {
            negative = source[index] == '-';
            index++;
        }

        int value = 0;
        while (index < source.Length && source[index] is >= '0' and <= '9')
        {
            value = unchecked(value * 10 + source[index] - '0');
            index++;
        }
        return negative ? unchecked(-value) : value;
    }

    private static double ParseAtof(string? text)
    {
        ReadOnlySpan<char> source = text.AsSpan();
        int index = SkipWhitespace(source);
        int start = index;
        bool negative = false;
        if (index < source.Length && source[index] is '+' or '-')
        {
            negative = source[index] == '-';
            index++;
        }

        ReadOnlySpan<char> special = source[index..];
        if (special.StartsWith("infinity", StringComparison.OrdinalIgnoreCase) ||
            special.StartsWith("inf", StringComparison.OrdinalIgnoreCase))
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (special.StartsWith("nan", StringComparison.OrdinalIgnoreCase))
            return double.NaN;

        bool hasDigits = false;
        while (index < source.Length && source[index] is >= '0' and <= '9')
        {
            hasDigits = true;
            index++;
        }
        if (index < source.Length && source[index] == '.')
        {
            index++;
            while (index < source.Length && source[index] is >= '0' and <= '9')
            {
                hasDigits = true;
                index++;
            }
        }
        if (!hasDigits)
            return 0;

        int end = index;
        if (index < source.Length && source[index] is 'e' or 'E')
        {
            int exponentStart = index++;
            if (index < source.Length && source[index] is '+' or '-')
                index++;
            int exponentDigits = index;
            while (index < source.Length && source[index] is >= '0' and <= '9')
                index++;
            end = exponentDigits == index ? exponentStart : index;
        }

        ReadOnlySpan<char> token = source[start..end];
        if (double.TryParse(
                token,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result))
        {
            return result;
        }
        return 0;
    }

    private static int SkipWhitespace(ReadOnlySpan<char> source)
    {
        int index = 0;
        while (index < source.Length && source[index] is
               ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
        {
            index++;
        }
        return index;
    }
}
