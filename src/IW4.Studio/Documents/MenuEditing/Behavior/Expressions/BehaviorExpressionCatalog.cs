using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

public enum BehaviorExpressionOperationCategory
{
    Opaque,
    Grouping,
    Separator,
    Unary,
    Binary,
    Function
}

/// <summary>
/// One catalog row. Guided rows have an observed grammar shape; a result of
/// <see cref="BehaviorExpressionResultKind.Unknown"/> means the runtime type
/// is not yet proven and should not be over-validated by the editor.
/// </summary>
public sealed class BehaviorExpressionOperationMetadata
{
    private readonly IReadOnlyList<int> _allowedArgumentCounts;

    internal BehaviorExpressionOperationMetadata(
        OperationEnum operation,
        string formulaName,
        BehaviorExpressionOperationCategory category,
        BehaviorExpressionResultKind resultKind,
        bool isGuided,
        int observedCount,
        IEnumerable<int>? allowedArgumentCounts = null)
    {
        Operation = operation;
        FormulaName = formulaName;
        Category = category;
        ResultKind = resultKind;
        IsGuided = isGuided;
        ObservedCount = observedCount;
        HasVerifiedArity = allowedArgumentCounts is not null;
        int[] verifiedCounts = allowedArgumentCounts?.ToArray() ?? [];
        // Corpus declarations use an empty collection for a verified
        // zero-argument function; null alone means arity is unverified.
        if (allowedArgumentCounts is not null && verifiedCounts.Length == 0)
            verifiedCounts = [0];
        _allowedArgumentCounts = Array.AsReadOnly(verifiedCounts
            .Distinct()
            .Order()
            .ToArray());
    }

    public OperationEnum Operation { get; }
    public string FormulaName { get; }
    public BehaviorExpressionOperationCategory Category { get; }
    public BehaviorExpressionResultKind ResultKind { get; }
    public bool IsGuided { get; }
    public int ObservedCount { get; }
    public IReadOnlyList<int> AllowedArgumentCounts => _allowedArgumentCounts;
    public bool HasVerifiedArity { get; }

    public bool SupportsArgumentCount(int count) =>
        !HasVerifiedArity || _allowedArgumentCounts.Contains(count);
}

/// <summary>
/// Single source of operation metadata. Every <see cref="OperationEnum"/>
/// value is present. The 99 operations observed in patch_mp have guided
/// grammar metadata; other values deliberately remain available but opaque.
/// </summary>
public sealed class BehaviorExpressionCatalog
{
    private readonly IReadOnlyDictionary<OperationEnum, BehaviorExpressionOperationMetadata> _byOperation;
    private readonly IReadOnlyDictionary<string, BehaviorExpressionOperationMetadata> _byFormulaName;
    private readonly IReadOnlyCollection<BehaviorExpressionOperationMetadata> _operations;

    private BehaviorExpressionCatalog()
    {
        var rows = Enum.GetValues<OperationEnum>()
            .Distinct()
            .ToDictionary(
                operation => operation,
                operation => new BehaviorExpressionOperationMetadata(
                    operation,
                    FormulaName(operation),
                    DefaultCategory(operation),
                    DefaultResultKind(operation),
                    isGuided: false,
                    observedCount: 0));

        Guided(rows, OperationEnum.OP_LEFTPAREN, 1553, BehaviorExpressionOperationCategory.Grouping);
        Guided(rows, OperationEnum.OP_RIGHTPAREN, 2049, BehaviorExpressionOperationCategory.Grouping);
        Guided(rows, OperationEnum.OP_COMMA, 1206, BehaviorExpressionOperationCategory.Separator);

        GuidedBinary(rows, OperationEnum.OP_MULTIPLY, 146);
        GuidedBinary(rows, OperationEnum.OP_DIVIDE, 64);
        GuidedBinary(rows, OperationEnum.OP_MODULUS, 35);
        // The engine uses OP_ADD for both arithmetic and string concatenation.
        GuidedBinary(
            rows,
            OperationEnum.OP_ADD,
            388,
            BehaviorExpressionResultKind.Unknown);
        GuidedBinary(rows, OperationEnum.OP_SUBTRACT, 144);
        GuidedUnary(rows, OperationEnum.OP_NOT, 159, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_LESSTHAN, 27, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_LESSTHANEQUALTO, 4, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_GREATERTHAN, 17, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_GREATERTHANEQUALTO, 25, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_EQUALS, 166, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_NOTEQUAL, 141, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_AND, 405, BehaviorExpressionResultKind.Boolean);
        GuidedBinary(rows, OperationEnum.OP_OR, 140, BehaviorExpressionResultKind.Boolean);

        // Observed corpus functions and their observed argument forms.
        GuidedFunction(rows, OperationEnum.OP_ACTIONSLOTUSABLE, 2, [1]);
        GuidedFunction(rows, OperationEnum.OP_ADSJAVELIN, 2, []);
        GuidedFunction(rows, OperationEnum.OP_ALONEINPARTY, 2, []);
        GuidedFunction(rows, OperationEnum.OP_ANYNEWMAPPACKS, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_DEBUGPRINT, 17, [2]);
        GuidedFunction(rows, OperationEnum.OP_DOWEHAVEMAPPACK, 6, [1]);
        GuidedFunction(rows, OperationEnum.OP_DVARINT, 2, [1], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_FLASHBANGED, 1, []);
        GuidedFunction(rows, OperationEnum.OP_FLOAT, 6, [1], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_GAMETYPENAME, 2, [], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_GETADJUSTEDSAFEAREAHORIZONTAL, 3, [], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_GETADJUSTEDSAFEAREAVERTICAL, 1, [], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_GETFOCUSEDITEMNAME, 13, [], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_GETFOCUSEDITEMY, 6, [], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_GETLOCALIZEDNATTYPE, 1, [], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_GETMAPCUSTOM, 18, [1]);
        GuidedFunction(rows, OperationEnum.OP_GETPARTYSTATUS, 1, []);
        GuidedFunction(rows, OperationEnum.OP_GETPERK, 46, [1]);
        GuidedFunction(rows, OperationEnum.OP_GETPLAYERCARDINFO, 192, [3]);
        GuidedFunction(rows, OperationEnum.OP_GETPLAYERDATA, 170, [1, 2, 3, 4, 5, 6]);
        GuidedFunction(rows, OperationEnum.OP_GETPLAYERDATAANYBOOLTRUE, 8, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_GETSEARCHPARAMS, 1, []);
        GuidedFunction(rows, OperationEnum.OP_INKILLCAM, 6, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_INKILLCAMNPC, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_INLOBBY, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_INPRIVATEPARTY, 11, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_INT, 49, [1], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_ISEMPJAMMED, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_ISITEMUNLOCKED, 35, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_ISRELOADING, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_ISSELECTEDPLAYERFRIEND, 3, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_ISSPLITSCREENONLINEPOSSIBLE, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_LEVELFOREXPERIENCE, 14, [1]);
        GuidedFunction(rows, OperationEnum.OP_LOCALIZESTRING, 6, [1, 2, 3], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_LOCALVARBOOL, 8, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_LOCALVARFLOAT, 2, [1], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_LOCALVARINT, 112, [1], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_LOCALVARSTRING, 27, [1], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_MAX, 11, [2], BehaviorExpressionResultKind.Number);
        GuidedFunction(rows, OperationEnum.OP_MAXRECOMMENDEDPLAYERS, 1, []);
        GuidedFunction(rows, OperationEnum.OP_MENUISOPEN, 2, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_MILLISECONDS, 51, [], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_MIN, 12, [2], BehaviorExpressionResultKind.Number);
        GuidedFunction(rows, OperationEnum.OP_MISSILECAM, 3, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_OTHERTEAMFIELD, 1, [1]);
        GuidedFunction(rows, OperationEnum.OP_PARTYISMISSINGMAPPACK, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_PARTYMISSINGMAPPACKERROR, 1, []);
        GuidedFunction(rows, OperationEnum.OP_PLAYERFIELD, 21, [1]);
        GuidedFunction(rows, OperationEnum.OP_PRIVATEPARTYHOST, 16, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_PRIVATEPARTYHOSTINLOBBY, 3, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_RADARISENABLED, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_RADARISJAMMED, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_RADARJAMINTENSITY, 3, []);
        GuidedFunction(rows, OperationEnum.OP_SCOPED, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_SCOPEDTHERMAL, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_SCORE, 2, [1]);
        GuidedFunction(rows, OperationEnum.OP_SECONDSASCOUNTDOWN, 6, [1], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_SECONDSASTIME, 1, [1], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_SELECTINGDIRECTION, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_SELECTINGLOCATION, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_SIN, 17, [1], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_SPECTATINGCLIENT, 3, []);
        GuidedFunction(rows, OperationEnum.OP_SPECTATINGFREE, 2, []);
        GuidedFunction(rows, OperationEnum.OP_SPLITSCREENPLAYERCOUNT, 4, [], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_STATICDVARBOOL, 154, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_STATICDVARINT, 59, [1], BehaviorExpressionResultKind.Integer);
        GuidedFunction(rows, OperationEnum.OP_STATICDVARSTRING, 46, [1], BehaviorExpressionResultKind.String);
        GuidedFunction(rows, OperationEnum.OP_TABLELOOKUP, 141, [4]);
        GuidedFunction(rows, OperationEnum.OP_TABLELOOKUPBYROW, 39, [3]);
        GuidedFunction(rows, OperationEnum.OP_TEAMFIELD, 22, [1]);
        GuidedFunction(rows, OperationEnum.OP_TIMELEFT, 3, []);
        GuidedFunction(rows, OperationEnum.OP_UIACTIVE, 1, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPATTACKDIRECT, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPATTACKTOP, 2, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKBLINK, 6, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKED, 5, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKING, 4, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKSCREENPOSX, 3, [], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKSCREENPOSY, 3, [], BehaviorExpressionResultKind.Float);
        GuidedFunction(rows, OperationEnum.OP_WEAPLOCKTOOCLOSE, 3, [], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPONCLASSNEW, 2, [1], BehaviorExpressionResultKind.Boolean);
        GuidedFunction(rows, OperationEnum.OP_WEAPONNAME, 10, [], BehaviorExpressionResultKind.String);

        _byOperation = new Dictionary<OperationEnum, BehaviorExpressionOperationMetadata>(rows);
        _byFormulaName = rows.Values
            .Where(value => value.Category == BehaviorExpressionOperationCategory.Function)
            .GroupBy(value => Normalize(value.FormulaName), StringComparer.Ordinal)
            .ToDictionary(value => value.Key, value => value.First(), StringComparer.Ordinal);
        _operations = Array.AsReadOnly(rows.Values.OrderBy(value => (int)value.Operation).ToArray());
    }

    public static BehaviorExpressionCatalog Default { get; } = new();
    public IReadOnlyCollection<BehaviorExpressionOperationMetadata> Operations => _operations;

    public BehaviorExpressionOperationMetadata Get(OperationEnum operation) =>
        _byOperation.TryGetValue(operation, out BehaviorExpressionOperationMetadata? value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(operation));

    public bool TryGet(OperationEnum operation, out BehaviorExpressionOperationMetadata metadata) =>
        _byOperation.TryGetValue(operation, out metadata!);

    public bool TryGetFormulaOperation(string formulaName, out BehaviorExpressionOperationMetadata metadata) =>
        _byFormulaName.TryGetValue(Normalize(formulaName), out metadata!);

    public static bool IsStaticDvar(OperationEnum operation) => operation is
        OperationEnum.OP_STATICDVARINT or
        OperationEnum.OP_STATICDVARBOOL or
        OperationEnum.OP_STATICDVARFLOAT or
        OperationEnum.OP_STATICDVARSTRING;

    private static void Guided(
        Dictionary<OperationEnum, BehaviorExpressionOperationMetadata> rows,
        OperationEnum operation,
        int count,
        BehaviorExpressionOperationCategory category,
        BehaviorExpressionResultKind resultKind = BehaviorExpressionResultKind.Unknown) =>
        rows[operation] = new(
            operation,
            FormulaName(operation),
            category,
            resultKind,
            isGuided: true,
            observedCount: count);

    private static void GuidedUnary(
        Dictionary<OperationEnum, BehaviorExpressionOperationMetadata> rows,
        OperationEnum operation,
        int count,
        BehaviorExpressionResultKind resultKind = BehaviorExpressionResultKind.Number) =>
        Guided(rows, operation, count, BehaviorExpressionOperationCategory.Unary, resultKind);

    private static void GuidedBinary(
        Dictionary<OperationEnum, BehaviorExpressionOperationMetadata> rows,
        OperationEnum operation,
        int count,
        BehaviorExpressionResultKind resultKind = BehaviorExpressionResultKind.Number) =>
        Guided(rows, operation, count, BehaviorExpressionOperationCategory.Binary, resultKind);

    private static void GuidedFunction(
        Dictionary<OperationEnum, BehaviorExpressionOperationMetadata> rows,
        OperationEnum operation,
        int count,
        IEnumerable<int> arities,
        BehaviorExpressionResultKind resultKind = BehaviorExpressionResultKind.Unknown) =>
        rows[operation] = new(
            operation,
            FormulaName(operation),
            BehaviorExpressionOperationCategory.Function,
            resultKind,
            isGuided: true,
            observedCount: count,
            allowedArgumentCounts: arities);

    private static BehaviorExpressionOperationCategory DefaultCategory(OperationEnum operation) => operation switch
    {
        OperationEnum.OP_LEFTPAREN or OperationEnum.OP_RIGHTPAREN => BehaviorExpressionOperationCategory.Grouping,
        OperationEnum.OP_COMMA => BehaviorExpressionOperationCategory.Separator,
        OperationEnum.OP_NOT or OperationEnum.OP_BITWISENOT => BehaviorExpressionOperationCategory.Unary,
        OperationEnum.OP_MULTIPLY or OperationEnum.OP_DIVIDE or OperationEnum.OP_MODULUS or
        OperationEnum.OP_ADD or OperationEnum.OP_SUBTRACT or
        OperationEnum.OP_LESSTHAN or OperationEnum.OP_LESSTHANEQUALTO or
        OperationEnum.OP_GREATERTHAN or OperationEnum.OP_GREATERTHANEQUALTO or
        OperationEnum.OP_EQUALS or OperationEnum.OP_NOTEQUAL or
        OperationEnum.OP_AND or OperationEnum.OP_OR or
        OperationEnum.OP_BITWISEAND or OperationEnum.OP_BITWISEOR or
        OperationEnum.OP_BITSHIFTLEFT or OperationEnum.OP_BITSHIFTRIGHT => BehaviorExpressionOperationCategory.Binary,
        _ when BehaviorExpressionNativeGrammar.IsFunction(operation) => BehaviorExpressionOperationCategory.Function,
        _ => BehaviorExpressionOperationCategory.Opaque
    };

    private static BehaviorExpressionResultKind DefaultResultKind(OperationEnum operation) => operation switch
    {
        OperationEnum.OP_NOT or OperationEnum.OP_AND or OperationEnum.OP_OR or
        OperationEnum.OP_LESSTHAN or OperationEnum.OP_LESSTHANEQUALTO or
        OperationEnum.OP_GREATERTHAN or OperationEnum.OP_GREATERTHANEQUALTO or
        OperationEnum.OP_EQUALS or OperationEnum.OP_NOTEQUAL => BehaviorExpressionResultKind.Boolean,
        OperationEnum.OP_MULTIPLY or OperationEnum.OP_DIVIDE or OperationEnum.OP_MODULUS or
        OperationEnum.OP_SUBTRACT => BehaviorExpressionResultKind.Number,
        _ => BehaviorExpressionResultKind.Unknown
    };

    private static string FormulaName(OperationEnum operation)
    {
        string text = operation.ToString();
        return text.StartsWith("OP_", StringComparison.Ordinal)
            ? text[3..].ToLowerInvariant()
            : text.ToLowerInvariant();
    }

    private static string Normalize(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}
