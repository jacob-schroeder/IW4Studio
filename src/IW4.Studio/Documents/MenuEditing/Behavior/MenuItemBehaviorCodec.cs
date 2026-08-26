using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>
/// Translates native MenuDef and ItemDef behavior graphs to and from the
/// immutable behavior domain. It deliberately does not mutate an asset;
/// compiler integration composes the native values into a cloned asset at the
/// document boundary.
/// </summary>
public sealed class MenuItemBehaviorCodec
{
    private readonly IMenuBehaviorExpressionCodec _expressions;

    public MenuItemBehaviorCodec(IMenuBehaviorExpressionCodec? expressions = null)
    {
        _expressions = expressions ?? ImportedMenuBehaviorExpressionCodec.Instance;
    }

    public MenuItemBehaviorBindings Import(ItemDefAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new MenuItemBehaviorBindings(
            ReadEventBinding(source.MouseEnterTextSet, source.MouseEnterText),
            ReadEventBinding(source.MouseExitTextSet, source.MouseExitText),
            ReadEventBinding(source.MouseEnterSet, source.MouseEnter),
            ReadEventBinding(source.MouseExitSet, source.MouseExit),
            ReadEventBinding(source.ActionSet, source.Action),
            ReadEventBinding(source.AcceptSet, source.Accept),
            ReadEventBinding(source.OnFocusSet, source.OnFocus),
            ReadEventBinding(source.LeaveFocusSet, source.LeaveFocus),
            ReadEventBinding(source.ListBox?.DoubleClickSet, source.ListBox?.DoubleClick ?? default),
            ReadKeyHandlers(source.OnKeyHandler, source.OnKey),
            new MenuItemBehaviorExpressionBindings(
                ReadExpression(source.VisibleStatement, source.VisibleExpression,
                    new(MenuBehaviorExpressionSiteKind.ItemVisible)),
                ReadExpression(source.DisabledStatement, source.DisabledExpression,
                    new(MenuBehaviorExpressionSiteKind.ItemDisabled)),
                ReadExpression(source.TextStatement, source.TextExpression,
                    new(MenuBehaviorExpressionSiteKind.ItemText)),
                ReadExpression(source.MaterialStatement, source.MaterialExpression,
                    new(MenuBehaviorExpressionSiteKind.ItemMaterial)),
                ReadFloatExpressions(source)));
    }

    public MenuDefinitionBehaviorBindings Import(MenuDefAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new MenuDefinitionBehaviorBindings(
            ReadEventBinding(source.OnOpenSet, source.OnOpen),
            ReadEventBinding(source.OnCloseRequestSet, source.OnCloseRequest),
            ReadEventBinding(source.OnCloseSet, source.OnClose),
            ReadEventBinding(source.OnEscSet, source.OnEsc),
            ReadKeyHandlers(source.ExecKeyHandler, source.ExecKeys));
    }

    public MenuItemBehaviorAssetBindings Export(MenuItemBehaviorBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        MenuItemBehaviorExpressionBindings expressions = bindings.Expressions;
        IReadOnlyList<ItemFloatExpression> floatExpressions = expressions.FloatExpressions.Entries
            .Select(expression =>
            {
                MenuBehaviorNativeExpressionBinding native = WriteExpression(
                    expression.Expression,
                    MenuBehaviorExpressionSite.Float(expression.Target));
                return new ItemFloatExpression
                {
                    Target = expression.Target,
                    Expression = native.Pointer,
                    Statement = native.Statement
                };
            })
            .ToArray();

        return new MenuItemBehaviorAssetBindings(
            WriteEventBinding(bindings.MouseEnterText),
            WriteEventBinding(bindings.MouseExitText),
            WriteEventBinding(bindings.MouseEnter),
            WriteEventBinding(bindings.MouseExit),
            WriteEventBinding(bindings.Action),
            WriteEventBinding(bindings.Accept),
            WriteEventBinding(bindings.OnFocus),
            WriteEventBinding(bindings.LeaveFocus),
            WriteEventBinding(bindings.ListBoxDoubleClick),
            PointerFor(bindings.KeyHandlers.RootPointer, bindings.KeyHandlers.Handlers.Length != 0),
            WriteKeyHandlers(bindings.KeyHandlers),
            WriteExpression(expressions.Visible, new(MenuBehaviorExpressionSiteKind.ItemVisible)),
            WriteExpression(expressions.Disabled, new(MenuBehaviorExpressionSiteKind.ItemDisabled)),
            WriteExpression(expressions.Text, new(MenuBehaviorExpressionSiteKind.ItemText)),
            WriteExpression(expressions.Material, new(MenuBehaviorExpressionSiteKind.ItemMaterial)),
            PointerFor(expressions.FloatExpressions.SourcePointer, floatExpressions.Count != 0),
            floatExpressions);
    }

    public MenuDefinitionBehaviorAssetBindings Export(
        MenuDefinitionBehaviorBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        return new MenuDefinitionBehaviorAssetBindings(
            WriteEventBinding(bindings.OnOpen),
            WriteEventBinding(bindings.OnCloseRequest),
            WriteEventBinding(bindings.OnClose),
            WriteEventBinding(bindings.OnEscape),
            PointerFor(bindings.KeyHandlers.RootPointer,
                bindings.KeyHandlers.Handlers.Length != 0),
            WriteKeyHandlers(bindings.KeyHandlers));
    }

    private MenuBehaviorEventBinding ReadEventBinding(
        MenuEventHandlerSet? source,
        XPointer<MenuEventHandlerSet> pointer) =>
        new(ReadEventSet(source, []), pointer);

    private MenuBehaviorEventHandlerSet? ReadEventSet(
        MenuEventHandlerSet? source,
        HashSet<MenuEventHandlerSet> ancestry)
    {
        if (source is null || !ancestry.Add(source))
            return null;

        try
        {
            MenuBehaviorEventHandlerEntry[] entries = source.Handlers
                .Select(reference =>
                    new MenuBehaviorEventHandlerEntry(
                        ReadEventHandler(reference.Handler, ancestry),
                        reference.Pointer))
                .ToArray();
            for (int index = 0; index < entries.Length; index++)
            {
                MenuBehaviorEventHandlerEntry entry = entries[index];
                MenuBehaviorEventHandler? previous = index == 0
                    ? null
                    : entries[index - 1].Handler;
                bool retainsImportedShape =
                    entry.Handler is MenuBehaviorOpaqueEventHandler ||
                    (entry.Handler is MenuBehaviorElseEventHandler &&
                     previous is not MenuBehaviorConditionalEventHandler);
                if (retainsImportedShape)
                {
                    entries[index] = entry with
                    {
                        ImportedShape = new MenuBehaviorImportedEventHandlerShape(
                            entry.Handler!,
                            entry.SourcePointer,
                            index)
                    };
                }
            }

            return new MenuBehaviorEventHandlerSet(
                entries,
                source.EventHandlers);
        }
        finally
        {
            ancestry.Remove(source);
        }
    }

    private MenuBehaviorFloatExpressionBindings ReadFloatExpressions(
        ItemDefAsset source)
    {
        MenuBehaviorFloatExpressionBinding[] entries = source.LoadedFloatExpressions
            .Select(expression => new MenuBehaviorFloatExpressionBinding(
                expression.Target,
                ReadExpression(
                    expression.Statement,
                    expression.Expression,
                    MenuBehaviorExpressionSite.Float(expression.Target))))
            .ToArray();

        foreach (IGrouping<ItemFloatExpressionTarget,
                     (MenuBehaviorFloatExpressionBinding Entry, int Index)> group in
                 entries.Select((entry, index) => (Entry: entry, Index: index))
                     .GroupBy(value => value.Entry.Target))
        {
            if (MenuBehaviorFloatExpressionBindings.AllTargets.Contains(group.Key) &&
                group.Count() == 1)
            {
                continue;
            }

            var shape = new MenuBehaviorImportedFloatExpressionShape(
                group.Select(value => (
                    value.Index,
                    value.Entry.Target,
                    value.Entry.Expression)));
            foreach ((MenuBehaviorFloatExpressionBinding entry, int index) in group)
                entries[index] = entry with { ImportedShape = shape };
        }

        return new MenuBehaviorFloatExpressionBindings(entries, source.FloatExpressions);
    }

    private MenuBehaviorEventHandler? ReadEventHandler(
        MenuEventHandler? source,
        HashSet<MenuEventHandlerSet> ancestry)
    {
        if (source is null)
            return null;

        MenuBehaviorRawEventHandlerShape raw = new(
            ReadEventDataPointer(source.EventData.Value),
            source.Pad05,
            source.Pad06,
            source.Pad07);

        return source.EventType switch
        {
            MenuEventHandlerType.UnconditionalScript =>
                new MenuBehaviorScriptEventHandler(source.UnconditionalScript, raw),
            MenuEventHandlerType.ConditionalScript => ReadConditional(source, raw, ancestry),
            MenuEventHandlerType.ElseScript => new MenuBehaviorElseEventHandler(
                ReadEventSet(source.ElseScriptSet, ancestry),
                source.EventData.ElseScript?.EventHandlerSetPointer ?? default,
                raw),
            MenuEventHandlerType.SetLocalVarBool or
            MenuEventHandlerType.SetLocalVarInt or
            MenuEventHandlerType.SetLocalVarFloat or
            MenuEventHandlerType.SetLocalVarString => ReadSetLocal(source, raw),
            _ => new MenuBehaviorOpaqueEventHandler((byte)source.EventType, raw)
        };
    }

    private MenuBehaviorConditionalEventHandler ReadConditional(
        MenuEventHandler source,
        MenuBehaviorRawEventHandlerShape raw,
        HashSet<MenuEventHandlerSet> ancestry)
    {
        ConditionalScript? conditional = source.ConditionalScript;
        return new MenuBehaviorConditionalEventHandler(
            ReadExpression(
                conditional?.EventStatement,
                conditional?.EventExpression ?? default,
                new(MenuBehaviorExpressionSiteKind.Conditional)),
            ReadEventSet(conditional?.EventHandlers, ancestry),
            conditional?.EventHandlerSet ?? default,
            raw);
    }

    private MenuBehaviorSetLocalVariableEventHandler ReadSetLocal(
        MenuEventHandler source,
        MenuBehaviorRawEventHandlerShape raw)
    {
        SetLocalVarData? local = source.SetLocalVarData;
        MenuBehaviorLocalValueType valueType = ToLocalValueType(
            source.EventType);
        return new MenuBehaviorSetLocalVariableEventHandler(
            valueType,
            local?.LocalVarNameString,
            ReadExpression(
                local?.ExpressionStatement,
                local?.Expression ?? default,
                MenuBehaviorExpressionSite.Local(valueType)),
            source.EventData.SetLocalVarData?.SetLocalVarDataPointer ?? default,
            local?.LocalVarName ?? default,
            raw);
    }

    private MenuBehaviorKeyHandlerBindings ReadKeyHandlers(
        ItemKeyHandler? source,
        XPointer<ItemKeyHandler> rootPointer)
    {
        List<MenuBehaviorKeyHandlerBinding> handlers = [];
        var visited = new HashSet<ItemKeyHandler>(ReferenceEqualityComparer.Instance);
        ItemKeyHandler? current = source;

        while (current is not null && visited.Add(current))
        {
            handlers.Add(new MenuBehaviorKeyHandlerBinding(
                current.Key,
                ReadEventSet(current.ActionSet, []),
                current.Action,
                current.Next));
            current = current.NextHandler;
        }

        return new MenuBehaviorKeyHandlerBindings(
            handlers,
            rootPointer,
            current is not null);
    }

    private MenuBehaviorExpressionBinding ReadExpression(
        Statement? source,
        XPointer<Statement> pointer,
        MenuBehaviorExpressionSite site)
    {
        BehaviorExpression? value = _expressions.Import(source, site);
        return new MenuBehaviorExpressionBinding(value, pointer)
        {
            SourceStatement = source,
            Support = _expressions.SupportFor(value),
            ImportDiagnostics = _expressions.ImportDiagnosticsFor(value)
        };
    }

    private MenuBehaviorNativeEventBinding WriteEventBinding(
        MenuBehaviorEventBinding binding)
    {
        MenuEventHandlerSet? handlers = WriteEventSet(binding.Handlers);
        return new MenuBehaviorNativeEventBinding(
            handlers,
            PointerFor(binding.SourcePointer, handlers is not null));
    }

    private MenuEventHandlerSet? WriteEventSet(MenuBehaviorEventHandlerSet? source)
    {
        if (source is null)
            return null;

        MenuEventHandlerReference[] handlers = source.Handlers
            .Select((entry, index) =>
            {
                MenuEventHandler? handler = WriteEventHandler(entry.Handler);
                return new MenuEventHandlerReference(
                    index,
                    PointerFor(entry.SourcePointer, handler is not null),
                    handler);
            })
            .ToArray();

        return new MenuEventHandlerSet
        {
            EventHandlerCount = handlers.Length,
            EventHandlers = PointerFor(source.HandlerTablePointer, handlers.Length != 0),
            Handlers = handlers
        };
    }

    private MenuEventHandler? WriteEventHandler(MenuBehaviorEventHandler? source) => source switch
    {
        null => null,
        MenuBehaviorScriptEventHandler script => new MenuEventHandler
        {
            EventData = new EventData
            {
                Value = new UnconditionalScriptEventData
                {
                    Script = PointerFor(
                        script.Raw.EventDataPointer.AsPointer<string>(),
                        script.Script is not null)
                }
            },
            UnconditionalScript = script.Script,
            EventType = MenuEventHandlerType.UnconditionalScript,
            Pad05 = script.Raw.Pad05,
            Pad06 = script.Raw.Pad06,
            Pad07 = script.Raw.Pad07
        },
        MenuBehaviorConditionalEventHandler conditional => WriteConditional(conditional),
        MenuBehaviorElseEventHandler @else => WriteElse(@else),
        MenuBehaviorSetLocalVariableEventHandler local => WriteSetLocal(local),
        MenuBehaviorOpaqueEventHandler opaque => new MenuEventHandler
        {
            EventData = new EventData
            {
                Value = new IgnoredEventData { Reserved = opaque.Raw.EventDataPointer.Raw }
            },
            EventType = (MenuEventHandlerType)opaque.EventType,
            Pad05 = opaque.Raw.Pad05,
            Pad06 = opaque.Raw.Pad06,
            Pad07 = opaque.Raw.Pad07
        },
        _ => throw new InvalidOperationException(
            $"Unsupported behavior handler type '{source.GetType().Name}'.")
    };

    private MenuEventHandler WriteConditional(MenuBehaviorConditionalEventHandler source)
    {
        MenuEventHandlerSet? then = WriteEventSet(source.Then);
        MenuBehaviorNativeExpressionBinding condition = WriteExpression(
            source.Condition,
            new(MenuBehaviorExpressionSiteKind.Conditional));
        XPointer<ConditionalScript> payloadPointer = PointerFor(
            source.Raw.EventDataPointer.AsPointer<ConditionalScript>(),
            then is not null || condition.Statement is not null);

        return new MenuEventHandler
        {
            EventData = new EventData
            {
                Value = new ConditionalScriptEventData
                {
                    ConditionalScriptPointer = payloadPointer
                }
            },
            ConditionalScript = new ConditionalScript
            {
                EventHandlerSet = PointerFor(source.ThenPointer, then is not null),
                EventHandlers = then,
                EventExpression = condition.Pointer,
                EventStatement = condition.Statement
            },
            EventType = MenuEventHandlerType.ConditionalScript,
            Pad05 = source.Raw.Pad05,
            Pad06 = source.Raw.Pad06,
            Pad07 = source.Raw.Pad07
        };
    }

    private MenuEventHandler WriteElse(MenuBehaviorElseEventHandler source)
    {
        MenuEventHandlerSet? handlers = WriteEventSet(source.Handlers);
        XPointer<MenuEventHandlerSet> pointer = PointerFor(
            source.HandlersPointer,
            handlers is not null);
        return new MenuEventHandler
        {
            EventData = new EventData
            {
                Value = new ElseScriptEventData { EventHandlerSetPointer = pointer }
            },
            ElseScriptSet = handlers,
            EventType = MenuEventHandlerType.ElseScript,
            Pad05 = source.Raw.Pad05,
            Pad06 = source.Raw.Pad06,
            Pad07 = source.Raw.Pad07
        };
    }

    private MenuEventHandler WriteSetLocal(MenuBehaviorSetLocalVariableEventHandler source)
    {
        MenuBehaviorNativeExpressionBinding expression = WriteExpression(
            source.Expression,
            MenuBehaviorExpressionSite.Local(source.ValueType));
        bool hasData = source.Name is not null || expression.Statement is not null;
        XPointer<SetLocalVarData> payloadPointer = PointerFor(source.DataPointer, hasData);

        return new MenuEventHandler
        {
            EventData = new EventData
            {
                Value = new SetLocalVarEventData { SetLocalVarDataPointer = payloadPointer }
            },
            SetLocalVarData = new SetLocalVarData
            {
                LocalVarName = PointerFor(source.NamePointer, source.Name is not null),
                LocalVarNameString = source.Name,
                Expression = expression.Pointer,
                ExpressionStatement = expression.Statement
            },
            EventType = ToNativeEventType(source.ValueType),
            Pad05 = source.Raw.Pad05,
            Pad06 = source.Raw.Pad06,
            Pad07 = source.Raw.Pad07
        };
    }

    private ItemKeyHandler? WriteKeyHandlers(MenuBehaviorKeyHandlerBindings source)
    {
        ItemKeyHandler? next = null;
        for (int index = source.Handlers.Length - 1; index >= 0; index--)
        {
            MenuBehaviorKeyHandlerBinding binding = source.Handlers[index];
            MenuEventHandlerSet? action = WriteEventSet(binding.Action);
            bool hasNext = next is not null;
            next = new ItemKeyHandler
            {
                Key = binding.Key,
                Action = PointerFor(binding.ActionPointer, action is not null),
                ActionSet = action,
                Next = hasNext
                    ? PointerFor(binding.NextPointer, true)
                    : source.HasTruncatedImportedTail
                        ? binding.NextPointer
                        : default,
                NextHandler = next
            };
        }

        return next;
    }

    private MenuBehaviorNativeExpressionBinding WriteExpression(
        MenuBehaviorExpressionBinding source,
        MenuBehaviorExpressionSite site)
    {
        Statement? statement = _expressions.Export(
            source.Value,
            source.SourceStatement,
            site,
            source.Support);
        return new MenuBehaviorNativeExpressionBinding(
            statement,
            PointerFor(source.SourcePointer, statement is not null));
    }

    private static XPointerReference ReadEventDataPointer(EventDataValue value) => value switch
    {
        UnconditionalScriptEventData script => script.Script.Untyped,
        ConditionalScriptEventData conditional => conditional.ConditionalScriptPointer.Untyped,
        ElseScriptEventData @else => @else.EventHandlerSetPointer.Untyped,
        SetLocalVarEventData local => local.SetLocalVarDataPointer.Untyped,
        IgnoredEventData ignored => XPointerReference.FromRaw(ignored.Reserved),
        _ => default
    };

    private static MenuBehaviorLocalValueType ToLocalValueType(
        MenuEventHandlerType type) => type switch
    {
        MenuEventHandlerType.SetLocalVarBool => MenuBehaviorLocalValueType.Boolean,
        MenuEventHandlerType.SetLocalVarInt => MenuBehaviorLocalValueType.Integer,
        MenuEventHandlerType.SetLocalVarFloat => MenuBehaviorLocalValueType.Float,
        MenuEventHandlerType.SetLocalVarString => MenuBehaviorLocalValueType.String,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static MenuEventHandlerType ToNativeEventType(
        MenuBehaviorLocalValueType type) => type switch
    {
        MenuBehaviorLocalValueType.Boolean => MenuEventHandlerType.SetLocalVarBool,
        MenuBehaviorLocalValueType.Integer => MenuEventHandlerType.SetLocalVarInt,
        MenuBehaviorLocalValueType.Float => MenuEventHandlerType.SetLocalVarFloat,
        MenuBehaviorLocalValueType.String => MenuEventHandlerType.SetLocalVarString,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static XPointer<T> PointerFor<T>(
        XPointer<T> source,
        bool hasValue) => !hasValue
        ? default
        : source.Raw == 0
            ? new XPointer<T>(-1)
            : source;
}
