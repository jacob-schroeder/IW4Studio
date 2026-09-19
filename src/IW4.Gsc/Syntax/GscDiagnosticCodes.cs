namespace IW4.Gsc.Syntax;

public static class GscDiagnosticCodes
{
    public const string BadToken = "GSC1001";
    public const string UnexpectedEndOfFile = "GSC1002";
    public const string BadSyntax = "GSC1003";
    public const string MaximumStringLengthExceeded = "GSC1004";

    public const string UninitialisedVariable = "GSC2001";
    public const string VariableAlreadyDeclaredAsDefine = "GSC2002";
    public const string FunctionAlreadyDefined = "GSC2003";
    public const string DuplicateDefine = "GSC2004";
    public const string DuplicateInclude = "GSC2005";
    public const string IllegalBreakStatement = "GSC2006";
    public const string IllegalContinueStatement = "GSC2007";
    public const string IllegalCaseStatement = "GSC2008";
    public const string IllegalDefaultStatement = "GSC2009";
    public const string InvalidExpressionListArity = "GSC2010";
    public const string ParameterCountExceeded = "GSC2011";
    public const string MissingCaseStatement = "GSC2012";
    public const string BuiltInOverride = "GSC2013";
    public const string UnreachableCode = "GSC2014";
    public const string InvalidCaseExpression = "GSC2015";
    public const string DuplicateCaseExpression = "GSC2016";
    public const string ConstantExpressionError = "GSC2017";
    public const string ConstantFalseCondition = "GSC2018";
    public const string InvalidDefineExpression = "GSC2019";
    public const string AnimationTreeRequired = "GSC2020";
    public const string InvalidAnimationTreeName = "GSC2021";
    public const string DeveloperSectionError = "GSC2022";
    public const string DeveloperOnlyReference = "GSC2023";
    public const string DebuggerOnlyExpression = "GSC2024";
    public const string InvalidObjectExpression = "GSC2025";
    public const string IllegalFunctionName = "GSC2026";
    public const string CompilerCapacityExceeded = "GSC2027";
    public const string UnknownFunction = "GSC2028";
    public const string AnimationTreeError = "GSC2029";
    public const string DependencyCompilationError = "GSC2030";
    public const string ValidationIncomplete = "GSC3001";
}
