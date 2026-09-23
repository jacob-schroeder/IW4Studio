namespace IW4.Gsc.Syntax;

/// <summary>
/// Stable names for the recovered IW4 yacc productions. Values are the exact
/// one-based production numbers used by the parser tables.
/// </summary>
internal enum GscProduction
{
    Program = 1,
    ExpressionFragment = 2,
    SimpleStatementFragment = 3,
    StatementFragment = 4,

    ExpressionFromPrimary = 5,
    LogicalOrExpression = 6,
    LogicalAndExpression = 7,
    BitwiseOrExpression = 8,
    BitwiseXorExpression = 9,
    BitwiseAndExpression = 10,
    EqualsExpression = 11,
    NotEqualsExpression = 12,
    LessThanExpression = 13,
    GreaterThanExpression = 14,
    LessThanOrEqualExpression = 15,
    GreaterThanOrEqualExpression = 16,
    ShiftLeftExpression = 17,
    ShiftRightExpression = 18,
    AddExpression = 19,
    SubtractExpression = 20,
    MultiplyExpression = 21,
    DivideExpression = 22,
    ModuloExpression = 23,
    LogicalNotExpression = 24,
    BitwiseNotExpression = 25,

    OptionalExpressionPresent = 26,
    OptionalExpressionEmpty = 27,
    ScriptPathIdentifier = 28,
    ScriptPathPath = 29,
    NamedFunctionQualified = 30,
    NamedFunctionLocal = 31,
    FunctionReferenceQualified = 32,
    FunctionReferenceLocal = 33,
    CallableNamedFunction = 34,
    CallableFunctionPointer = 35,
    CallKindDirect = 36,
    CallKindThread = 37,
    CallKindChildThread = 38,
    CallKindCallPointer = 39,
    CallExpression = 40,
    MethodCallExpression = 41,

    ParenthesizedExpressionList = 42,
    IntegerLiteral = 43,
    FloatLiteral = 44,
    NegativeIntegerLiteral = 45,
    NegativeFloatLiteral = 46,
    StringLiteral = 47,
    LocalizedStringLiteral = 48,
    PrimaryCallExpression = 49,
    PrimaryLValueExpression = 50,
    UndefinedLiteral = 51,
    SelfExpression = 52,
    ThisThreadExpression = 53,
    LevelExpression = 54,
    GameExpression = 55,
    AnimExpression = 56,
    SizeExpression = 57,
    FunctionReferenceExpression = 58,
    EmptyArrayExpression = 59,
    AnimationExpression = 60,
    FalseLiteral = 61,
    TrueLiteral = 62,
    AnimTreeExpression = 63,
    BreakOnExpression = 64,

    FieldLValue = 65,
    IndexLValue = 66,
    LocalLValue = 67,
    DebuggerObjectLValue = 68,
    DebuggerSelfFieldLValue = 69,

    AssignmentStatement = 70,
    ReturnValueStatement = 71,
    ReturnStatement = 72,
    WaitStatement = 73,
    IncrementStatement = 74,
    DecrementStatement = 75,
    OrAssignmentStatement = 76,
    XorAssignmentStatement = 77,
    AndAssignmentStatement = 78,
    ShiftLeftAssignmentStatement = 79,
    ShiftRightAssignmentStatement = 80,
    AddAssignmentStatement = 81,
    SubtractAssignmentStatement = 82,
    MultiplyAssignmentStatement = 83,
    DivideAssignmentStatement = 84,
    ModuloAssignmentStatement = 85,
    WaitTillStatement = 86,
    WaitTillMatchStatement = 87,
    WaitTillFrameEndStatement = 88,
    NotifyStatement = 89,
    EndOnStatement = 90,
    BreakStatement = 91,
    ContinueStatement = 92,
    BreakpointStatement = 93,
    ProfileBeginStatement = 94,
    ProfileEndStatement = 95,

    StatementCoreCall = 96,
    StatementCoreSimple = 97,
    OptionalStatementCoreEmpty = 98,
    OptionalStatementCorePresent = 99,
    TerminatedStatement = 100,
    BlockStatement = 101,
    IfStatement = 102,
    IfElseStatement = 103,
    WhileStatement = 104,
    ForStatement = 105,
    KeyValueForeachStatement = 106,
    ValueForeachStatement = 107,
    SwitchStatement = 108,
    DeveloperBlockStatement = 109,

    EmptyBlockItem = 110,
    CaseLabel = 111,
    DefaultLabel = 112,
    BlockItemStatement = 113,
    StatementListAppend = 114,
    StatementListEmpty = 115,
    ExpressionListAppend = 116,
    ExpressionListSingle = 117,
    OptionalExpressionListPresent = 118,
    OptionalExpressionListEmpty = 119,
    ParameterListAppend = 120,
    ParameterListSingle = 121,
    OptionalParameterListPresent = 122,
    OptionalParameterListEmpty = 123,
    WaitTillArgumentsAppendOutput = 124,
    WaitTillArgumentsInitialExpression = 125,
    WaitTillMatchArgumentsAppend = 126,
    WaitTillMatchArgumentsSingle = 127,
    NotifyArgumentsAppend = 128,
    NotifyArgumentsSingle = 129,

    FunctionDefinition = 130,
    UsingAnimTreeDirective = 131,
    DeveloperSectionOpen = 132,
    DeveloperSectionClose = 133,
    DefineDeclaration = 134,
    TopLevelItemListAppend = 135,
    TopLevelItemListEmpty = 136,
    IncludeDirective = 137,
    IncludeListAppend = 138,
    IncludeListEmpty = 139
}

internal enum GscNonterminal
{
    Root,
    Expression,
    OptionalExpression,
    ScriptPath,
    NamedFunction,
    FunctionReference,
    Callable,
    CallKind,
    CallExpression,
    PrimaryExpression,
    LValue,
    SimpleStatement,
    StatementCore,
    OptionalStatementCore,
    Statement,
    BlockItem,
    StatementList,
    ExpressionList,
    OptionalExpressionList,
    ParameterList,
    OptionalParameterList,
    WaitTillArguments,
    WaitTillMatchArguments,
    NotifyArguments,
    TopLevelItem,
    TopLevelItemList,
    IncludeDirective,
    IncludeList
}

internal static class GscProductionFacts
{
    private const int FirstNonterminalSymbol = 96;

    internal const int Count = 139;

    internal static int GetRightHandSideLength(GscProduction production) =>
        Iw4GscParserTables.RuleLengths[GetRuleNumber(production)];

    internal static GscNonterminal GetLeftHandSide(GscProduction production) =>
        (GscNonterminal)(
            Iw4GscParserTables.RuleSymbols[GetRuleNumber(production)] -
            FirstNonterminalSymbol);

    private static int GetRuleNumber(GscProduction production)
    {
        if (!Enum.IsDefined(production))
            throw new ArgumentOutOfRangeException(nameof(production));

        return (int)production;
    }
}
