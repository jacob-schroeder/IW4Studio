using System.Text;
using IW4.Assets.Assets.Menu;

namespace IW4.AssetExchange.SourceFormat.Menu;

internal sealed partial class MenuSourceWriter
{
    private const int FirstFunctionOperation =
        (int)OperationEnum.OP_STATICDVARINT;

    private void WriteStatementProperty(
        string key,
        Statement? statement,
        bool isBoolean)
    {
        if (statement is null || statement.NumEntries < 0)
            return;

        ValidateStatement(statement);
        Indent();
        WriteKey(key);
        if (isBoolean)
        {
            _writer.Write("when(");
            WriteStatementSkipInitialUnnecessaryParenthesis(statement);
            _writer.WriteLine(");");
        }
        else
        {
            WriteStatementSkipInitialUnnecessaryParenthesis(statement);
            _writer.WriteLine(';');
        }
    }

    private void WriteStatement(Statement statement)
    {
        if (statement.NumEntries < 0)
            return;

        ValidateStatement(statement);
        WriteStatementEntryRange(statement, 0, statement.NumEntries);
    }

    private void WriteStatementSkipInitialUnnecessaryParenthesis(
        Statement statement)
    {
        ValidateStatement(statement);
        int end = statement.NumEntries;
        if (end >= 1 &&
            statement.LoadedEntries[0].IsOperator &&
            Operation(statement.LoadedEntries[0]) == OperationEnum.OP_LEFTPAREN)
        {
            int parenthesisEnd = FindStatementClosingParenthesis(statement, 0);
            if (parenthesisEnd >= end)
                WriteStatementEntryRange(statement, 1, end);
            else if (parenthesisEnd == end - 1)
                WriteStatementEntryRange(statement, 1, end - 1);
            else
                WriteStatementEntryRange(statement, 0, end);
        }
        else
        {
            WriteStatementEntryRange(statement, 0, end);
        }
    }

    private void WriteStatementEntryRange(
        Statement statement,
        int start,
        int end)
    {
        if (start < 0 || start > end || end > statement.NumEntries)
        {
            throw new InvalidDataException(
                $"Invalid Menu expression entry range [{start}, {end}).");
        }

        int current = start;
        bool spaceNext = false;
        while (current < end)
        {
            ExpressionEntry entry = statement.LoadedEntries[current];
            if (entry.IsOperator)
                WriteStatementOperator(statement, ref current, ref spaceNext);
            else if (entry.IsOperand)
                WriteStatementOperand(statement, ref current, ref spaceNext);
            else
                throw new InvalidDataException(
                    $"Unsupported Menu expression-entry kind {(int)entry.Kind}.");
        }
    }

    private void WriteStatementOperator(
        Statement statement,
        ref int current,
        ref bool spaceNext)
    {
        ExpressionEntry entry = statement.LoadedEntries[current];
        OperationEnum operation = Operation(entry);
        if (spaceNext && operation != OperationEnum.OP_COMMA)
            _writer.Write(' ');

        if (operation == OperationEnum.OP_LEFTPAREN)
        {
            int closing = FindStatementClosingParenthesis(statement, current);
            _writer.Write('(');
            WriteStatementEntryRange(statement, current + 1, closing);
            _writer.Write(')');
            current = Math.Min(closing + 1, statement.NumEntries);
            spaceNext = true;
            return;
        }

        if (IsStaticDvar(operation))
        {
            int closing = FindStatementClosingParenthesis(statement, current);
            _writer.Write(MenuExpressionOperationNames.Get(operation));
            _writer.Write('(');
            WriteStaticDvarName(statement, current + 1, closing);
            _writer.Write(')');
            current = Math.Min(closing + 1, statement.NumEntries);
            spaceNext = true;
            return;
        }

        _writer.Write(MenuExpressionOperationNames.Get(operation));
        if ((int)operation >= FirstFunctionOperation)
        {
            int closing = FindStatementClosingParenthesis(statement, current);
            _writer.Write('(');
            WriteStatementEntryRange(statement, current + 1, closing);
            _writer.Write(')');
            current = Math.Min(closing + 1, statement.NumEntries);
        }
        else
        {
            current++;
        }

        spaceNext = operation != OperationEnum.OP_NOT;
    }

    private void WriteStatementOperand(
        Statement statement,
        ref int current,
        ref bool spaceNext)
    {
        ExpressionEntry entry = statement.LoadedEntries[current];
        if (spaceNext)
            _writer.Write(' ');

        switch (entry.Operand.DataType)
        {
            case ExpDataType.VAL_FLOAT when entry.Operand.Value is FloatOperandValue value:
                WriteFloat(value.Value);
                break;

            case ExpDataType.VAL_INT when entry.Operand.Value is IntOperandValue value:
                WriteInt(value.Value);
                break;

            case ExpDataType.VAL_STRING when entry.Operand.Value is StringOperandValue:
                if (entry.StringValue is null)
                {
                    throw new InvalidDataException(
                        "A Menu string expression operand was not resolved.");
                }

                WriteEscapedString(entry.StringValue);
                break;

            case ExpDataType.VAL_FUNCTION when entry.Operand.Value is FunctionOperandValue value:
                WriteStatementOperandFunction(statement, entry, value);
                break;

            default:
                throw new InvalidDataException(
                    $"Menu expression operand {entry.Operand.DataType} has incompatible " +
                    $"payload '{entry.Operand.Value.GetType().Name}'.");
        }

        current++;
        spaceNext = true;
    }

    private void WriteStatementOperandFunction(
        Statement owner,
        ExpressionEntry entry,
        FunctionOperandValue value)
    {
        if (entry.FunctionStatement is null)
            return;

        int functionIndex = FindFunctionIndex(owner, entry.FunctionStatement, value);
        _writer.Write(functionIndex >= 0 ? $"FUNC_{functionIndex}()" : "INVALID_FUNC()");
    }

    private static int FindFunctionIndex(
        Statement owner,
        Statement function,
        FunctionOperandValue value)
    {
        ExpressionSupportingData? supportingData = owner.SupportingDataValue;
        if (supportingData is null)
            return -1;

        IReadOnlyList<StatementReference> functions =
            supportingData.UiFunctions.LoadedFunctions;
        StatementReference? referenceMatch = functions.FirstOrDefault(
            reference => ReferenceEquals(reference.Statement, function));
        if (referenceMatch is not null)
            return referenceMatch.Index;

        StatementReference[] pointerMatches = functions
            .Where(reference => reference.Pointer.Raw == value.StatementPointer.Raw)
            .ToArray();
        return pointerMatches.Length == 1 ? pointerMatches[0].Index : -1;
    }

    private void WriteStaticDvarName(
        Statement statement,
        int operandIndex,
        int closingIndex)
    {
        if (operandIndex >= closingIndex ||
            operandIndex >= statement.NumEntries ||
            !statement.LoadedEntries[operandIndex].IsOperand ||
            statement.LoadedEntries[operandIndex].Operand.DataType != ExpDataType.VAL_INT ||
            statement.LoadedEntries[operandIndex].Operand.Value is not IntOperandValue indexValue)
        {
            _writer.Write("#INVALID_DVAR_OPERAND");
            return;
        }

        StaticDvarList? dvars = statement.SupportingDataValue?.StaticDvarList;
        StaticDvarReference? reference = dvars?.LoadedStaticDvars.FirstOrDefault(
            row => row.Index == indexValue.Value);
        string? name = reference?.StaticDvar?.DvarNameString;
        _writer.Write(string.IsNullOrEmpty(name) ? "#INVALID_DVAR_INDEX" : name);
    }

    private static int FindStatementClosingParenthesis(
        Statement statement,
        int openingPosition)
    {
        ValidateStatement(statement);
        if ((uint)openingPosition >= (uint)statement.NumEntries)
        {
            throw new InvalidDataException(
                $"Menu expression opening position {openingPosition} is outside the statement.");
        }

        int depth = 1;
        for (int index = openingPosition + 1; index < statement.NumEntries; index++)
        {
            ExpressionEntry entry = statement.LoadedEntries[index];
            if (!entry.IsOperator)
                continue;

            OperationEnum operation = Operation(entry);
            if (operation == OperationEnum.OP_LEFTPAREN ||
                (int)operation >= FirstFunctionOperation)
            {
                depth++;
            }
            else if (operation == OperationEnum.OP_RIGHTPAREN)
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }

        return statement.NumEntries;
    }

    private static void ValidateStatement(Statement statement)
    {
        if (statement.NumEntries < 0)
            return;

        if (statement.NumEntries > statement.LoadedEntries.Count)
        {
            throw new InvalidDataException(
                $"Menu Statement declares {statement.NumEntries} entries but exposes " +
                $"{statement.LoadedEntries.Count} loaded entries.");
        }
    }

    private static OperationEnum Operation(ExpressionEntry entry)
    {
        if (!entry.IsOperator ||
            !Enum.IsDefined(typeof(OperationEnum), entry.OperationCode))
        {
            throw new InvalidDataException(
                $"Unsupported PS3 Menu expression opcode 0x{entry.OperationCode:X}.");
        }

        return (OperationEnum)entry.OperationCode;
    }

    private static bool IsStaticDvar(OperationEnum operation) =>
        operation is OperationEnum.OP_STATICDVARINT or
            OperationEnum.OP_STATICDVARBOOL or
            OperationEnum.OP_STATICDVARFLOAT or
            OperationEnum.OP_STATICDVARSTRING;

    private void WriteEventHandlerSetProperty(
        string key,
        MenuEventHandlerSet? eventHandlers)
    {
        if (eventHandlers is null)
            return;

        Indent();
        _writer.WriteLine(key);
        WriteEventHandlerSet(
            eventHandlers,
            new HashSet<MenuEventHandlerSet>(ReferenceEqualityComparer.Instance));
    }

    private void WriteEventHandlerSet(
        MenuEventHandlerSet eventHandlers,
        HashSet<MenuEventHandlerSet> activeSets)
    {
        if (!activeSets.Add(eventHandlers))
            throw new InvalidDataException("Menu event-handler sets contain a cycle.");

        try
        {
            if (eventHandlers.EventHandlerCount < 0)
            {
                throw new InvalidDataException(
                    $"Menu event-handler set has invalid count {eventHandlers.EventHandlerCount}.");
            }

            IReadOnlyDictionary<int, MenuEventHandlerReference> rows =
                HandlerRows(eventHandlers);
            Indent();
            _writer.WriteLine('{');
            _indent++;

            for (int index = 0; index < eventHandlers.EventHandlerCount; index++)
            {
                if (!rows.TryGetValue(index, out MenuEventHandlerReference? reference) ||
                    reference.Handler is null)
                {
                    continue;
                }

                WriteEventHandler(reference.Handler, activeSets);
            }

            EndScope();
        }
        finally
        {
            activeSets.Remove(eventHandlers);
        }
    }

    private void WriteEventHandler(
        MenuEventHandler eventHandler,
        HashSet<MenuEventHandlerSet> activeSets)
    {
        switch (eventHandler.EventType)
        {
            case MenuEventHandlerType.UnconditionalScript:
                if (eventHandler.UnconditionalScript is null)
                {
                    throw new InvalidDataException(
                        "A Menu unconditional-script event was not resolved.");
                }

                WriteUnconditionalScript(eventHandler.UnconditionalScript);
                break;

            case MenuEventHandlerType.ConditionalScript:
                ConditionalScript? conditional = eventHandler.ConditionalScript;
                if (conditional?.EventStatement is null ||
                    conditional.EventHandlers is null)
                {
                    return;
                }

                Indent();
                _writer.Write("if (");
                WriteStatementSkipInitialUnnecessaryParenthesis(
                    conditional.EventStatement);
                _writer.WriteLine(')');
                WriteEventHandlerSet(conditional.EventHandlers, activeSets);
                break;

            case MenuEventHandlerType.ElseScript:
                if (eventHandler.ElseScriptSet is null)
                    return;
                Indent();
                _writer.WriteLine("else");
                WriteEventHandlerSet(eventHandler.ElseScriptSet, activeSets);
                break;

            case MenuEventHandlerType.SetLocalVarBool:
                WriteSetLocalVar("setLocalVarBool", eventHandler.SetLocalVarData);
                break;
            case MenuEventHandlerType.SetLocalVarInt:
                WriteSetLocalVar("setLocalVarInt", eventHandler.SetLocalVarData);
                break;
            case MenuEventHandlerType.SetLocalVarFloat:
                WriteSetLocalVar("setLocalVarFloat", eventHandler.SetLocalVarData);
                break;
            case MenuEventHandlerType.SetLocalVarString:
                WriteSetLocalVar("setLocalVarString", eventHandler.SetLocalVarData);
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported PS3 Menu event type {(byte)eventHandler.EventType}.");
        }
    }

    private void WriteSetLocalVar(string function, SetLocalVarData? data)
    {
        if (data is null)
            return;
        if (string.IsNullOrEmpty(data.LocalVarNameString) ||
            data.ExpressionStatement is null)
        {
            throw new InvalidDataException(
                $"Menu {function} event has unresolved source data.");
        }

        Indent();
        _writer.Write(function);
        _writer.Write(' ');
        _writer.Write(data.LocalVarNameString);
        _writer.Write(' ');
        WriteStatement(data.ExpressionStatement);
        _writer.WriteLine(';');
    }

    private void WriteItemKeyHandlers(ItemKeyHandler? first)
    {
        var visited = new HashSet<ItemKeyHandler>(ReferenceEqualityComparer.Instance);
        for (ItemKeyHandler? current = first;
             current is not null;
             current = current.NextHandler)
        {
            if (!visited.Add(current))
                throw new InvalidDataException("Menu item-key handlers contain a cycle.");

            string property;
            if (current.Key is >= '!' and <= '~' && current.Key != '"')
            {
                var key = new StringBuilder("execKey ");
                key.Append('"');
                if (current.Key == '\\')
                    key.Append("\\\\");
                else
                    key.Append((char)current.Key);
                key.Append('"');
                property = key.ToString();
            }
            else
            {
                property = $"execKeyInt {current.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }

            WriteEventHandlerSetProperty(property, current.ActionSet);
        }
    }

    private void WriteUnconditionalScript(string script)
    {
        IReadOnlyList<string> tokens = CreateScriptTokenList(script);
        bool newStatement = true;
        foreach (string token in tokens)
        {
            if (newStatement)
            {
                if (token == ";")
                    continue;
                Indent();
            }

            if (token == ";")
            {
                _writer.WriteLine(';');
                newStatement = true;
                continue;
            }

            if (!newStatement)
                _writer.Write(' ');
            else
                newStatement = false;

            if (DoesTokenNeedQuotationMarks(token))
                WriteEscapedString(token);
            else
                _writer.Write(token);
        }

        if (!newStatement)
            _writer.WriteLine(';');
    }

    private void WriteMultiTokenStringProperty(string key, string? value)
    {
        if (value is null)
            return;

        Indent();
        WriteKey(key);
        IReadOnlyList<string> tokens = CreateScriptTokenList(value);
        _writer.Write("{ ");
        for (int index = 0; index < tokens.Count; index++)
        {
            if (index > 0)
                _writer.Write(';');
            WriteEscapedString(tokens[index]);
        }

        if (tokens.Count > 0)
            _writer.Write(' ');
        _writer.WriteLine('}');
    }

    private static IReadOnlyList<string> CreateScriptTokenList(string script)
    {
        var tokens = new List<string>();
        int index = 0;
        while (index < script.Length)
        {
            char current = script[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '"')
            {
                tokens.Add(ReadQuotedToken(script, ref index));
                continue;
            }

            if (IsAsciiLetter(current) || current == '_')
            {
                int start = index++;
                while (index < script.Length &&
                       (IsAsciiAlphaNumeric(script[index]) || script[index] == '_'))
                {
                    index++;
                }

                tokens.Add(script[start..index]);
                continue;
            }

            tokens.Add(current.ToString());
            index++;
        }

        return tokens;
    }

    private static string ReadQuotedToken(string script, ref int index)
    {
        index++;
        var value = new StringBuilder();
        bool escaped = false;
        while (index < script.Length)
        {
            char current = script[index++];
            if (!escaped && current == '"')
                return value.ToString();

            if (!escaped && current == '\\')
            {
                escaped = true;
                continue;
            }

            if (escaped)
            {
                value.Append(current switch
                {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    'f' => '\f',
                    _ => current
                });
                escaped = false;
            }
            else
            {
                value.Append(current);
            }
        }

        throw new InvalidDataException("Menu script contains an unclosed string literal.");
    }

    private static bool DoesTokenNeedQuotationMarks(string token)
    {
        if (token.Length == 0)
            return true;

        bool hasAlphaNumeric = token.Any(IsAsciiAlphaNumeric);
        return hasAlphaNumeric &&
            token.Any(character =>
                !IsAsciiAlphaNumeric(character) && character != '_');
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiAlphaNumeric(char value) =>
        IsAsciiLetter(value) || value is >= '0' and <= '9';

    private static IReadOnlyDictionary<int, StatementReference> FunctionRows(
        UIFunctionList functions)
    {
        var rows = new Dictionary<int, StatementReference>();
        foreach (StatementReference reference in functions.LoadedFunctions)
        {
            if (reference.Index < 0 || reference.Index >= functions.TotalFunctions)
            {
                throw new InvalidDataException(
                    $"Menu function row {reference.Index} is outside TotalFunctions " +
                    $"{functions.TotalFunctions}.");
            }

            if (!rows.TryAdd(reference.Index, reference))
            {
                throw new InvalidDataException(
                    $"Menu function row {reference.Index} is duplicated.");
            }
        }

        return rows;
    }

    private static IReadOnlyDictionary<int, MenuEventHandlerReference> HandlerRows(
        MenuEventHandlerSet handlers)
    {
        var rows = new Dictionary<int, MenuEventHandlerReference>();
        foreach (MenuEventHandlerReference reference in handlers.Handlers)
        {
            if (reference.Index < 0 || reference.Index >= handlers.EventHandlerCount)
            {
                throw new InvalidDataException(
                    $"Menu event-handler row {reference.Index} is outside count " +
                    $"{handlers.EventHandlerCount}.");
            }

            if (!rows.TryAdd(reference.Index, reference))
            {
                throw new InvalidDataException(
                    $"Menu event-handler row {reference.Index} is duplicated.");
            }
        }

        return rows;
    }

    private static bool SupportingDataValuesEquivalent(
        ExpressionSupportingData left,
        ExpressionSupportingData right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return FunctionListsEquivalent(left.UiFunctions, right.UiFunctions) &&
            StaticDvarListsEquivalent(left.StaticDvarList, right.StaticDvarList) &&
            StringListsEquivalent(left.UiStrings, right.UiStrings);
    }

    internal static bool SupportingDataEquivalent(
        ExpressionSupportingData? left,
        ExpressionSupportingData? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        return left is not null &&
            right is not null &&
            SupportingDataValuesEquivalent(left, right);
    }

    private static bool FunctionListsEquivalent(
        UIFunctionList left,
        UIFunctionList right)
    {
        if (left.TotalFunctions != right.TotalFunctions)
            return false;

        StatementReference[] leftRows = left.LoadedFunctions
            .OrderBy(reference => reference.Index)
            .ToArray();
        StatementReference[] rightRows = right.LoadedFunctions
            .OrderBy(reference => reference.Index)
            .ToArray();
        if (leftRows.Length != rightRows.Length)
            return false;

        var visited = new HashSet<(Statement Left, Statement Right)>();
        for (int index = 0; index < leftRows.Length; index++)
        {
            if (leftRows[index].Index != rightRows[index].Index ||
                !StatementsEquivalent(
                    leftRows[index].Statement,
                    rightRows[index].Statement,
                    visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StatementsEquivalent(
        Statement? left,
        Statement? right,
        HashSet<(Statement Left, Statement Right)> visited)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null ||
            left.NumEntries != right.NumEntries ||
            left.LoadedEntries.Count != right.LoadedEntries.Count)
        {
            return false;
        }

        if (!visited.Add((left, right)))
            return true;

        for (int index = 0; index < left.LoadedEntries.Count; index++)
        {
            ExpressionEntry leftEntry = left.LoadedEntries[index];
            ExpressionEntry rightEntry = right.LoadedEntries[index];
            if (leftEntry.Kind != rightEntry.Kind ||
                leftEntry.OperationCode != rightEntry.OperationCode ||
                leftEntry.OperatorTail != rightEntry.OperatorTail ||
                leftEntry.Operand.DataType != rightEntry.Operand.DataType ||
                !OperandValuesEquivalent(leftEntry, rightEntry, visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OperandValuesEquivalent(
        ExpressionEntry left,
        ExpressionEntry right,
        HashSet<(Statement Left, Statement Right)> visited)
    {
        if (left.IsOperator)
            return true;

        return (left.Operand.Value, right.Operand.Value) switch
        {
            (IntOperandValue a, IntOperandValue b) => a.Value == b.Value,
            (FloatOperandValue a, FloatOperandValue b) =>
                a.EncodedBits == b.EncodedBits,
            (StringOperandValue, StringOperandValue) =>
                string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            (FunctionOperandValue, FunctionOperandValue) =>
                StatementsEquivalent(
                    left.FunctionStatement,
                    right.FunctionStatement,
                    visited),
            (ReservedOperandValue a, ReservedOperandValue b) =>
                a.Reserved == b.Reserved,
            _ => false
        };
    }

    private static bool StaticDvarListsEquivalent(
        StaticDvarList left,
        StaticDvarList right)
    {
        if (left.NumStaticDvars != right.NumStaticDvars)
            return false;

        var leftRows = left.LoadedStaticDvars
            .OrderBy(reference => reference.Index)
            .Select(reference =>
                (reference.Index, reference.StaticDvar?.DvarNameString))
            .ToArray();
        var rightRows = right.LoadedStaticDvars
            .OrderBy(reference => reference.Index)
            .Select(reference =>
                (reference.Index, reference.StaticDvar?.DvarNameString))
            .ToArray();
        return leftRows.SequenceEqual(rightRows);
    }

    private static bool StringListsEquivalent(
        StringList left,
        StringList right)
    {
        if (left.TotalStrings != right.TotalStrings)
            return false;

        var leftRows = left.LoadedStrings
            .OrderBy(reference => reference.Index)
            .Select(reference => (reference.Index, reference.Value))
            .ToArray();
        var rightRows = right.LoadedStrings
            .OrderBy(reference => reference.Index)
            .Select(reference => (reference.Index, reference.Value))
            .ToArray();
        return leftRows.SequenceEqual(rightRows);
    }
}
