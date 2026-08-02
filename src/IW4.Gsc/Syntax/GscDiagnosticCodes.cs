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
}
