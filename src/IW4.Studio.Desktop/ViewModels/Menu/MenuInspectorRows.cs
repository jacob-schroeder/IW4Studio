using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorTextPropertyRowViewModel Text(
        string label,
        string fieldPath,
        string? value,
        Action<string>? apply,
        string? description = null,
        bool showInfoIcon = false) =>
        new(
            label,
            fieldPath,
            value,
            apply,
            ValidateLatin1,
            description)
        {
            ShowInfoIcon = showInfoIcon
        };

    private static InspectorReadOnlyPropertyRowViewModel ReadOnly(
        string label,
        string fieldPath,
        string? value,
        string? description = null) =>
        new(label, fieldPath, value, description);

    private static InspectorRectPropertyRowViewModel Rect(
        string label,
        string fieldPath,
        MenuRectangleValue value,
        Action<InspectorRectValue>? apply,
        string? description = null) =>
        new(
            label,
            fieldPath,
            new InspectorRectValue(value.X, value.Y, value.Width, value.Height),
            apply,
            description);

    private static InspectorColorPropertyRowViewModel Color(
        string label,
        string fieldPath,
        MenuColorValue value,
        Action<InspectorColorValue>? apply = null) =>
        new(
            label,
            fieldPath,
            new InspectorColorValue(value.R, value.G, value.B, value.A),
            apply);

    private static InspectorChoicePropertyRowViewModel Choice<TEnum>(
        string label,
        string fieldPath,
        TEnum value,
        Action<TEnum>? apply = null,
        string? description = null)
        where TEnum : struct, Enum =>
        new(
            label,
            fieldPath,
            Enum.GetValues<TEnum>().Select(option => new InspectorChoice(
                Convert.ToInt64(option, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture),
                DisplayEnum(option))),
            Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            apply is null
                ? null
                : selected => apply((TEnum)Enum.ToObject(
                    typeof(TEnum),
                    long.Parse(selected, CultureInfo.InvariantCulture))),
            description);

    private static InspectorFlagsPropertyRowViewModel Flags<TEnum>(
        string label,
        string fieldPath,
        TEnum value,
        Action<TEnum>? apply = null,
        string? description = null)
        where TEnum : struct, Enum =>
        new(
            label,
            fieldPath,
            EnumBits(value),
            Enum.GetValues<TEnum>()
                .Where(option => EnumBits(option) != 0)
                .Select(option => (
                    DisplayEnum(option),
                    EnumBits(option))),
            apply is null
                ? null
                : bits => apply(EnumFromBits<TEnum>(bits)),
            description);

    private static InspectorChoicePropertyRowViewModel BinaryChoice(
        string label,
        string fieldPath,
        int value,
        Action<int>? apply,
        string falseLabel = "No",
        string trueLabel = "Yes") =>
        new(
            label,
            fieldPath,
            [
                new InspectorChoice("0", falseLabel),
                new InspectorChoice("1", trueLabel)
            ],
            value.ToString(CultureInfo.InvariantCulture),
            apply is null
                ? null
                : selected => apply(int.Parse(
                    selected,
                    CultureInfo.InvariantCulture)));

    private static InspectorChoicePropertyRowViewModel IntegerChoice(
        string label,
        string fieldPath,
        int value,
        IEnumerable<InspectorChoice> choices,
        Action<int>? apply,
        string? description = null) =>
        new(
            label,
            fieldPath,
            choices,
            value.ToString(CultureInfo.InvariantCulture),
            apply is null
                ? null
                : selected => apply(int.Parse(
                    selected,
                    CultureInfo.InvariantCulture)),
            description);

    private static IReadOnlyList<InspectorChoice> TextAlignmentChoices() =>
    [
        new("0", "Legacy · Left"),
        new("1", "Legacy · Center"),
        new("2", "Legacy · Right"),
        new("4", "Top · Left"),
        new("5", "Top · Center"),
        new("6", "Top · Right"),
        new("8", "Middle · Left"),
        new("9", "Middle · Center"),
        new("10", "Middle · Right"),
        new("12", "Bottom · Left"),
        new("13", "Bottom · Center"),
        new("14", "Bottom · Right")
    ];

    private static ulong EnumBits<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum))) switch
        {
            TypeCode.SByte => unchecked((byte)Convert.ToSByte(
                value,
                CultureInfo.InvariantCulture)),
            TypeCode.Int16 => unchecked((ushort)Convert.ToInt16(
                value,
                CultureInfo.InvariantCulture)),
            TypeCode.Int32 => unchecked((uint)Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture)),
            TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture)),
            TypeCode.Byte => Convert.ToByte(value, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => Convert.ToUInt16(
                value,
                CultureInfo.InvariantCulture),
            TypeCode.UInt32 => Convert.ToUInt32(
                value,
                CultureInfo.InvariantCulture),
            TypeCode.UInt64 => Convert.ToUInt64(
                value,
                CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException(
                $"Enum '{typeof(TEnum).FullName}' has an unsupported underlying type.")
        };

    private static TEnum EnumFromBits<TEnum>(ulong bits)
        where TEnum : struct, Enum
    {
        object value = Type.GetTypeCode(
            Enum.GetUnderlyingType(typeof(TEnum))) switch
        {
            TypeCode.SByte => unchecked((sbyte)bits),
            TypeCode.Int16 => unchecked((short)bits),
            TypeCode.Int32 => unchecked((int)bits),
            TypeCode.Int64 => unchecked((long)bits),
            TypeCode.Byte => unchecked((byte)bits),
            TypeCode.UInt16 => unchecked((ushort)bits),
            TypeCode.UInt32 => unchecked((uint)bits),
            TypeCode.UInt64 => bits,
            _ => throw new InvalidDataException(
                $"Enum '{typeof(TEnum).FullName}' has an unsupported underlying type.")
        };
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }

    private static MenuRectangleValue Rect(
        MenuRectangleValue current,
        InspectorRectValue value) =>
        current with
        {
            X = value.X,
            Y = value.Y,
            Width = value.Width,
            Height = value.Height
        };

    private static MenuColorValue Color(InspectorColorValue value) =>
        new(value.Alpha, value.Red, value.Green, value.Blue);

    private static string? ValidateLatin1(string value)
    {
        if (value.Contains('\0'))
            return "XString values cannot contain a null character.";
        return value.Any(character => character > byte.MaxValue)
            ? "XString values must be representable in Latin-1."
            : null;
    }

    private static string DisplayEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        string text = value.ToString();
        DescriptionAttribute? description = typeof(TEnum)
            .GetField(text, BindingFlags.Public | BindingFlags.Static)?
            .GetCustomAttribute<DescriptionAttribute>();
        if (description is not null)
            return description.Description;

        int separator = text.LastIndexOf('_');
        return separator >= 0 && separator + 1 < text.Length
            ? text[(separator + 1)..]
            : text;
    }

    private static string? EmptyToNull(string value) =>
        value.Length == 0 ? null : value;

    private static string Bool(bool value) => value ? "Yes" : "No";

    private static string PayloadTypeTag(int value)
    {
        ItemDefType type = (ItemDefType)value;
        string number = value.ToString(CultureInfo.InvariantCulture);
        return Enum.IsDefined(type)
            ? $"{DisplayEnum(type)} ({number})"
            : $"Unknown ({number})";
    }

    private static string ImageTrack(int value) => value switch
    {
        0 => "Misc (0)",
        1 => "Debug (1)",
        2 => "Texture name (2)",
        3 => "UI (3)",
        4 => "Lightmap (4)",
        5 => "Light (5)",
        6 => "FX (6)",
        7 => "HUD (7)",
        8 => "Model (8)",
        9 => "World (9)",
        10 => "Count sentinel (10)",
        _ => $"Unknown ({value.ToString(CultureInfo.InvariantCulture)})"
    };

    private static string Count(params bool[] values) =>
        values.Count(value => value).ToString("N0");

    private static IReadOnlyList<T> ReplaceAt<T>(
        IReadOnlyList<T> values,
        int index,
        T replacement)
    {
        ArgumentNullException.ThrowIfNull(values);
        if ((uint)index >= (uint)values.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        T[] copy = values.ToArray();
        copy[index] = replacement;
        return Array.AsReadOnly(copy);
    }

    private static InvalidOperationException ChangedPayload() =>
        new("The Item type changed while its Properties editor was open.");
}
