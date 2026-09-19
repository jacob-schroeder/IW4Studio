namespace IW4.Studio.Desktop.Rendering;

public enum MenuFontRole
{
    Small = 0,
    Normal = 1,
    Big = 2,
    ExtraBig = 3,
    Bold = 4,
    Console = 5,
    Objective = 6,
    HudBig = 7,
    HudSmall = 8
}

public enum MenuFontEnumResolutionStatus
{
    Known = 0,
    Unknown = 1
}

/// <summary>
/// Runtime inputs required by the scale-dependent IW4 font-enum branches.
/// Thresholds correspond to ui_smallFont, ui_bigFont, and ui_extraBigFont;
/// callers must supply the active scenario values rather than assuming their
/// registered defaults.
/// </summary>
public sealed class MenuFontSelectionContext
{
    public MenuFontSelectionContext(
        float textScale,
        float virtualToPhysicalScaleY,
        float smallFontThreshold,
        float bigFontThreshold,
        float extraBigFontThreshold)
    {
        ValidateNonNegativeFinite(textScale, nameof(textScale));
        if (!float.IsFinite(virtualToPhysicalScaleY) ||
            virtualToPhysicalScaleY <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualToPhysicalScaleY),
                "The virtual-to-physical Y scale must be finite and positive.");
        }
        ValidateNonNegativeFinite(
            smallFontThreshold,
            nameof(smallFontThreshold));
        ValidateNonNegativeFinite(
            bigFontThreshold,
            nameof(bigFontThreshold));
        ValidateNonNegativeFinite(
            extraBigFontThreshold,
            nameof(extraBigFontThreshold));
        if (!float.IsFinite(textScale * virtualToPhysicalScaleY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualToPhysicalScaleY),
                "The effective text scale must be finite.");
        }

        TextScale = textScale;
        VirtualToPhysicalScaleY = virtualToPhysicalScaleY;
        SmallFontThreshold = smallFontThreshold;
        BigFontThreshold = bigFontThreshold;
        ExtraBigFontThreshold = extraBigFontThreshold;
    }

    public float TextScale { get; }

    public float VirtualToPhysicalScaleY { get; }

    public float SmallFontThreshold { get; }

    public float BigFontThreshold { get; }

    public float ExtraBigFontThreshold { get; }

    public float EffectiveTextScale => TextScale * VirtualToPhysicalScaleY;

    private static void ValidateNonNegativeFinite(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record MenuFontEnumResolution
{
    private MenuFontEnumResolution(
        int fontEnum,
        MenuFontEnumResolutionStatus status,
        MenuFontRole? role,
        string? lookupName,
        string? failure)
    {
        FontEnum = fontEnum;
        Status = status;
        Role = role;
        LookupName = lookupName;
        Failure = failure;
    }

    public int FontEnum { get; }

    public MenuFontEnumResolutionStatus Status { get; }

    public MenuFontRole? Role { get; }

    public string? LookupName { get; }

    public string? Failure { get; }

    public bool IsKnown => Status == MenuFontEnumResolutionStatus.Known;

    internal static MenuFontEnumResolution Known(
        int fontEnum,
        MenuFontRole role,
        string lookupName) =>
        new(
            fontEnum,
            MenuFontEnumResolutionStatus.Known,
            role,
            lookupName,
            null);

    internal static MenuFontEnumResolution Unknown(
        int fontEnum,
        string failure) =>
        new(
            fontEnum,
            MenuFontEnumResolutionStatus.Unknown,
            null,
            null,
            failure);
}

/// <summary>
/// Resolves the font-enum behavior established by IW4 UI_GetFontHandle.
/// Named selectors 2 through 10 resolve directly; every other value follows
/// the native scale-adaptive fallback used by selectors 0 and 1.
/// </summary>
public static class MenuFontEnumMapper
{
    public static MenuFontEnumResolution Resolve(
        int fontEnum,
        MenuFontSelectionContext? context = null)
    {
        return fontEnum switch
        {
            2 => Known(fontEnum, MenuFontRole.Big),
            3 => Known(fontEnum, MenuFontRole.Small),
            4 => Known(fontEnum, MenuFontRole.Bold),
            5 => Known(fontEnum, MenuFontRole.Console),
            6 => Known(fontEnum, MenuFontRole.Objective),
            7 => Known(fontEnum, MenuFontRole.Normal),
            8 => Known(fontEnum, MenuFontRole.ExtraBig),
            9 => Known(fontEnum, MenuFontRole.HudBig),
            10 => Known(fontEnum, MenuFontRole.HudSmall),
            _ when context is null =>
                MenuFontEnumResolution.Unknown(
                    fontEnum,
                    $"Font enum {fontEnum} uses IW4's scale-adaptive fallback and requires an explicit MenuFontSelectionContext."),
            _ => Known(
                fontEnum,
                SelectDefault(context!))
        };
    }

    private static MenuFontEnumResolution Known(
        int fontEnum,
        MenuFontRole role) =>
        MenuFontEnumResolution.Known(
            fontEnum,
            role,
            role switch
            {
                MenuFontRole.Small => "fonts/smallFont",
                MenuFontRole.Normal => "fonts/normalFont",
                MenuFontRole.Big => "fonts/bigFont",
                MenuFontRole.ExtraBig => "fonts/extraBigFont",
                MenuFontRole.Bold => "fonts/boldFont",
                MenuFontRole.Console => "fonts/consoleFont",
                MenuFontRole.Objective => "fonts/objectiveFont",
                MenuFontRole.HudBig => "fonts/hudBigFont",
                MenuFontRole.HudSmall => "fonts/hudSmallFont",
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            });

    private static MenuFontRole SelectDefault(
        MenuFontSelectionContext context)
    {
        float scale = context.EffectiveTextScale;
        if (context.SmallFontThreshold >= scale)
            return MenuFontRole.Small;
        if (context.ExtraBigFontThreshold <= scale)
            return MenuFontRole.ExtraBig;
        return context.BigFontThreshold > scale
            ? MenuFontRole.Normal
            : MenuFontRole.Big;
    }
}
