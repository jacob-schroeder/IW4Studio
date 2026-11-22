using System.Text;
using IW4.Game.Assets.Menu;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Menu;

internal sealed partial class MenuSourceReader
{
    private MenuEventHandlerSet ReadHandlers() => ReadHandlers(depth: 0);

    private MenuEventHandlerSet ReadHandlers(int depth)
    {
        if (depth >= MaximumNesting)
            throw Error("Menu event nesting is too deep.");

        Expect("{");
        var handlers = new List<MenuEventHandlerReference>();
        while (!Take("}"))
        {
            if (Take(";"))
                continue;
            if (Take("if"))
            {
                Statement condition = ReadStatement(parenthesized: true, consumeSemicolon: false);
                MenuEventHandlerSet body = ReadHandlers(depth + 1);
                Add(new MenuEventHandler
                {
                    EventType = MenuEventHandlerType.ConditionalScript,
                    EventData = new EventData { Value = new ConditionalScriptEventData { ConditionalScriptPointer = new(-1) } },
                    ConditionalScript = new ConditionalScript
                    {
                        EventHandlers = body, EventHandlerSet = Inline(body),
                        EventStatement = condition, EventExpression = Inline(condition)
                    }
                });
                if (Take("else"))
                {
                    Add(new MenuEventHandler
                    {
                        EventType = MenuEventHandlerType.ElseScript,
                        EventData = new EventData { Value = new ElseScriptEventData { EventHandlerSetPointer = new(-1) } },
                        ElseScriptSet = ReadHandlers(depth + 1)
                    });
                }
                continue;
            }
            if (At("else"))
                throw Error("An else block must follow an if block.");

            MenuEventHandlerType localType = Current.Kind == TokenKind.Atom
                ? Current.Value.ToLowerInvariant() switch
                {
                    "setlocalvarbool" => MenuEventHandlerType.SetLocalVarBool,
                    "setlocalvarint" => MenuEventHandlerType.SetLocalVarInt,
                    "setlocalvarfloat" => MenuEventHandlerType.SetLocalVarFloat,
                    "setlocalvarstring" => MenuEventHandlerType.SetLocalVarString,
                    _ => MenuEventHandlerType.UnconditionalScript
                }
                : MenuEventHandlerType.UnconditionalScript;

            if (localType == MenuEventHandlerType.UnconditionalScript)
            {
                Add(new MenuEventHandler
                {
                    EventType = localType,
                    EventData = new EventData { Value = new UnconditionalScriptEventData { Script = new(-1) } },
                    UnconditionalScript = ReadScriptCommand()
                });
            }
            else
            {
                _position++;
                string name = Text();
                Statement value = ReadStatementProperty();
                Add(new MenuEventHandler
                {
                    EventType = localType,
                    EventData = new EventData { Value = new SetLocalVarEventData { SetLocalVarDataPointer = new(-1) } },
                    SetLocalVarData = new SetLocalVarData
                    {
                        LocalVarNameString = name, LocalVarName = Inline(name),
                        ExpressionStatement = value, Expression = Inline(value)
                    }
                });
            }
        }

        return new MenuEventHandlerSet
        {
            EventHandlerCount = handlers.Count,
            EventHandlers = handlers.Count == 0 ? default : new(-1),
            Handlers = handlers.ToArray()
        };

        void Add(MenuEventHandler handler) => handlers.Add(new(handlers.Count, new(-1), handler));
    }

    private string ReadScriptCommand()
    {
        var script = new StringBuilder();
        while (!At(";") && !At("}"))
        {
            Token token = Current;
            if (token.Kind == TokenKind.End || At("{"))
                throw Error("Expected a semicolon or closing brace after the menu command.");
            _position++;
            if (script.Length != 0)
                script.Append(' ');
            if (token.Kind == TokenKind.Quoted)
            {
                script.Append(QuoteScriptToken(token.Value));
            }
            else
            {
                script.Append(token.Value);
            }
        }
        Take(";");
        script.Append(" ;");
        return script.ToString();
    }

    private static string QuoteScriptToken(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private void ReadKey(string property, List<(int Key, MenuEventHandlerSet Handlers)> keys)
    {
        int key;
        if (property == "execkeyint")
        {
            key = Integer();
        }
        else
        {
            string character = Text();
            if (character.Length != 1 || character[0] > 0x7f)
                throw Error("execKey expects one ASCII character. Use execKeyInt for a numeric key.");
            key = character[0];
        }
        keys.Add((key, ReadHandlers()));
    }

    private static ItemKeyHandler? BuildKeys(List<(int Key, MenuEventHandlerSet Handlers)> keys)
    {
        ItemKeyHandler? head = null;
        for (int index = keys.Count - 1; index >= 0; index--)
        {
            var (key, handlers) = keys[index];
            head = new ItemKeyHandler
            {
                Key = key,
                ActionSet = handlers, Action = Inline(handlers),
                NextHandler = head, Next = Inline(head)
            };
        }
        return head;
    }
}
