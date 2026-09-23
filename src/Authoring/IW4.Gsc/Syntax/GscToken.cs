namespace IW4.Gsc.Syntax;

/// <summary>External token identifiers returned by the recovered IW4 scanner.</summary>
public enum GscTokenKind
{
    EndOfFile = 0,
    BadToken = 257,
    Identifier = 258,
    String = 259,
    LocalizedString = 260,
    OpenBrace = 261,
    CloseBrace = 262,
    OpenParenthesis = 263,
    CloseParenthesis = 264,
    OpenBracket = 265,
    CloseBracket = 266,
    LogicalOr = 267,
    LogicalAnd = 268,
    BitwiseOr = 269,
    BitwiseXor = 270,
    BitwiseAnd = 271,
    Equals = 272,
    NotEquals = 273,
    LessThan = 274,
    GreaterThan = 275,
    LessThanOrEqual = 276,
    GreaterThanOrEqual = 277,
    ShiftLeft = 278,
    ShiftRight = 279,
    Plus = 280,
    Minus = 281,
    Multiply = 282,
    Divide = 283,
    Modulo = 284,
    LogicalNot = 285,
    BitwiseNot = 286,
    Integer = 287,
    Float = 288,
    Dot = 289,
    Comma = 290,
    Colon = 291,
    Semicolon = 292,
    Assign = 293,
    QuestionMark = 294,
    ReturnKeyword = 295,
    WaitKeyword = 296,
    ThreadKeyword = 297,
    ChildThreadKeyword = 298,
    CallKeyword = 299,
    UndefinedKeyword = 300,
    SelfKeyword = 301,
    ThisThreadKeyword = 302,
    LevelKeyword = 303,
    GameKeyword = 304,
    AnimKeyword = 305,
    IfKeyword = 306,
    ElseKeyword = 307,
    WhileKeyword = 308,
    ForKeyword = 309,
    ForeachKeyword = 310,
    InKeyword = 311,
    Increment = 312,
    Decrement = 313,
    OrAssign = 314,
    XorAssign = 315,
    AndAssign = 316,
    ShiftLeftAssign = 317,
    ShiftRightAssign = 318,
    AddAssign = 319,
    SubtractAssign = 320,
    MultiplyAssign = 321,
    DivideAssign = 322,
    ModuloAssign = 323,
    Size = 324,
    UsingAnimTreeDirective = 325,
    AnimTreeDirective = 326,
    IncludeDirective = 327,
    Scope = 328,
    Path = 329,
    WaitTillKeyword = 330,
    WaitTillMatchKeyword = 331,
    WaitTillFrameEndKeyword = 332,
    NotifyKeyword = 333,
    SwitchKeyword = 334,
    CaseKeyword = 335,
    DefaultKeyword = 336,
    BreakKeyword = 337,
    ContinueKeyword = 338,
    EndOnKeyword = 339,
    FalseKeyword = 340,
    TrueKeyword = 341,
    BreakpointKeyword = 342,
    ProfileBeginKeyword = 343,
    ProfileEndKeyword = 344,
    DeveloperBlockOpen = 345,
    DeveloperBlockClose = 346,
    Dollar = 347,
    BreakOnKeyword = 348,
    ParserOnlyTerminal = 349
}

/// <summary>A non-trivia token addressed to its UTF-16 source span.</summary>
public readonly record struct GscToken
{
    public GscToken(GscTokenKind kind, GscTextSpan span)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Kind = kind;
        Span = span;
    }

    public GscTokenKind Kind { get; }

    public GscTextSpan Span { get; }
}
