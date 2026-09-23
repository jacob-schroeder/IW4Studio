namespace IW4.Gsc.Syntax;

internal static class GscParser
{
    private const int ProgramSelectorExternalToken = (int)GscTokenKind.Plus;
    private const int TerminalCount = 96;

    internal static GscParseResult Parse(
        IReadOnlyList<GscToken> tokens,
        int sourceLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var states = new List<int>(capacity: 200) { 0 };
        var values = new List<GscSyntaxElement>(capacity: 200);
        int cursor = -1;
        int externalToken = ProgramSelectorExternalToken;
        int lookahead = Translate(externalToken);
        int steps = 0;

        while (true)
        {
            if ((steps++ & 0xff) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            int state = states[^1];
            int pact = Iw4GscParserTables.Actions[state];
            int? action = null;
            if (pact != Iw4GscParserTables.PactDefault)
            {
                int index = pact + lookahead;
                if ((uint)index <= Iw4GscParserTables.LastTableIndex &&
                    Iw4GscParserTables.Check[index] == lookahead)
                {
                    action = Iw4GscParserTables.Table[index];
                }
            }

            int rule;
            if (action is null)
            {
                rule = Iw4GscParserTables.DefaultActions[state];
                if (rule == 0)
                {
                    return GscParseResult.Rejected(
                        GetLookahead(tokens, cursor, sourceLength),
                        GetExpectedTokens(state));
                }
            }
            else if (action == Iw4GscParserTables.FinalState)
            {
                return GscParseResult.Accepted(CreateSyntaxTree(values));
            }
            else if (action > 0)
            {
                states.Add(action.Value);
                values.Add(CreateTokenElement(
                    tokens,
                    cursor,
                    externalToken,
                    sourceLength));
                if (externalToken != (int)GscTokenKind.EndOfFile)
                    cursor++;
                externalToken = GetExternalToken(tokens, cursor);
                lookahead = Translate(externalToken);
                continue;
            }
            else if (action is 0 or Iw4GscParserTables.PactDefault)
            {
                return GscParseResult.Rejected(
                    GetLookahead(tokens, cursor, sourceLength),
                    GetExpectedTokens(state));
            }
            else
            {
                rule = -action.Value;
            }

            int rightHandSideLength = Iw4GscParserTables.RuleLengths[rule];
            int leftHandSideSymbol = Iw4GscParserTables.RuleSymbols[rule];
            GscSyntaxElement[] children = PopValues(values, rightHandSideLength);
            if (rightHandSideLength != 0)
                states.RemoveRange(states.Count - rightHandSideLength, rightHandSideLength);

            var production = (GscProduction)rule;
            values.Add(new GscSyntaxNode(
                production,
                GetReductionSpan(children, tokens, cursor, sourceLength),
                children));

            int previousState = states[^1];
            int nonterminal = leftHandSideSymbol - TerminalCount;
            int gotoIndex = Iw4GscParserTables.GotoOffsets[nonterminal] + previousState;
            int targetState = (uint)gotoIndex <= Iw4GscParserTables.LastTableIndex &&
                              Iw4GscParserTables.Check[gotoIndex] == previousState
                ? Iw4GscParserTables.Table[gotoIndex]
                : Iw4GscParserTables.DefaultGotos[nonterminal];
            states.Add(targetState);
        }
    }

    private static GscSyntaxTokenElement CreateTokenElement(
        IReadOnlyList<GscToken> tokens,
        int cursor,
        int externalToken,
        int sourceLength)
    {
        bool isProgramSelector = cursor == -1 &&
                                 externalToken == ProgramSelectorExternalToken;
        GscToken token = isProgramSelector
            ? new GscToken(GscTokenKind.Plus, new GscTextSpan(0, 0))
            : GetLookahead(tokens, cursor, sourceLength);
        return new GscSyntaxTokenElement(token, isProgramSelector);
    }

    private static GscSyntaxElement[] PopValues(
        List<GscSyntaxElement> values,
        int count)
    {
        if (count == 0)
            return [];

        int start = values.Count - count;
        if (start < 0)
            throw new InvalidOperationException("The recovered parser value stack underflowed.");

        GscSyntaxElement[] children = values.GetRange(start, count).ToArray();
        values.RemoveRange(start, count);
        return children;
    }

    private static GscTextSpan GetReductionSpan(
        IReadOnlyList<GscSyntaxElement> children,
        IReadOnlyList<GscToken> tokens,
        int cursor,
        int sourceLength)
    {
        if (children.Count == 0)
        {
            int position = GetLookahead(tokens, cursor, sourceLength).Span.Start;
            return new GscTextSpan(position, 0);
        }

        int start = children[0].Span.Start;
        int end = children[^1].Span.End;
        return new GscTextSpan(start, checked(end - start));
    }

    private static GscSyntaxTree CreateSyntaxTree(
        IReadOnlyList<GscSyntaxElement> values)
    {
        bool hasShiftedEndOfFile = values.Count == 2 &&
                                   values[1] is GscSyntaxTokenElement
                                   {
                                       Token.Kind: GscTokenKind.EndOfFile
                                   };
        if ((values.Count is not 1 && !hasShiftedEndOfFile) ||
            values[0] is not GscSyntaxNode root)
        {
            throw new InvalidOperationException(
                "The accepted parser value stack does not contain one root node.");
        }

        return new GscSyntaxTree(root);
    }

    private static int GetExternalToken(IReadOnlyList<GscToken> tokens, int cursor) =>
        cursor >= 0 && cursor < tokens.Count
            ? (int)tokens[cursor].Kind
            : (int)GscTokenKind.EndOfFile;

    private static GscToken GetLookahead(
        IReadOnlyList<GscToken> tokens,
        int cursor,
        int sourceLength) =>
        cursor >= 0 && cursor < tokens.Count
            ? tokens[cursor]
            : new GscToken(
                GscTokenKind.EndOfFile,
                new GscTextSpan(sourceLength, 0));

    private static int Translate(int externalToken)
    {
        if (externalToken <= 0)
            return 0;
        return externalToken < Iw4GscParserTables.Translate.Length
            ? Iw4GscParserTables.Translate[externalToken]
            : 124;
    }

    private static GscTokenKind[] GetExpectedTokens(int state)
    {
        int pact = Iw4GscParserTables.Actions[state];
        if (pact == Iw4GscParserTables.PactDefault)
            return [];

        var expected = new List<GscTokenKind>();
        for (int symbol = 0; symbol < TerminalCount; symbol++)
        {
            if (symbol is 1 or 2)
                continue;

            int index = pact + symbol;
            if ((uint)index > Iw4GscParserTables.LastTableIndex ||
                Iw4GscParserTables.Check[index] != symbol)
            {
                continue;
            }

            int action = Iw4GscParserTables.Table[index];
            if (action is 0 or Iw4GscParserTables.PactDefault)
                continue;

            GscTokenKind token = symbol == 0
                ? GscTokenKind.EndOfFile
                : (GscTokenKind)(symbol + 254);
            if (token != GscTokenKind.ParserOnlyTerminal)
                expected.Add(token);
        }

        return [.. expected];
    }
}

internal readonly record struct GscParseResult(
    bool IsAccepted,
    GscToken UnexpectedToken,
    IReadOnlyList<GscTokenKind> ExpectedTokens,
    GscSyntaxTree? SyntaxTree)
{
    internal static GscParseResult Accepted(GscSyntaxTree syntaxTree) => new(
        IsAccepted: true,
        default,
        [],
        syntaxTree);

    internal static GscParseResult Rejected(
        GscToken unexpectedToken,
        IReadOnlyList<GscTokenKind> expectedTokens) =>
        new(
            IsAccepted: false,
            unexpectedToken,
            expectedTokens,
            SyntaxTree: null);
}
