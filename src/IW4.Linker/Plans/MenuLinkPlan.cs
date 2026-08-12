using System.Diagnostics.CodeAnalysis;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen Menu provider graph. Recursive UI nodes and their direct pointer
/// tables are rebuilt in native child order, while nested Materials and Sounds
/// remain logical provider dependencies.
/// </summary>
internal sealed class MenuLinkPlan : AssetLinkPlan
{
    private MenuLinkPlan(
        AssetKey key,
        string originalSerializedName,
        MenuDefAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = new StorageFreezer(freeze).FreezeMenu(definition, NameStorage);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        MenuDefAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition, originalSerializedName);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Menu,
                originalSerializedName,
                freeze);
        }

        if (!string.Equals(
                definition.Window.Name,
                originalSerializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Menu.Window.Name must equal the provider's exact serialized name.");
        }

        return new MenuLinkPlan(key, originalSerializedName, definition, freeze);
    }

    private static void ValidateReferenceShape(
        MenuDefAsset definition,
        string originalSerializedName)
    {
        if (!string.Equals(
                definition.Window.Name,
                originalSerializedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A comma-prefixed Menu provider must retain its exact name in Window.Name.");
        }

        if (HasNonzeroWindowBody(definition.Window, ignoreName: true) ||
            definition.FontPointer.Raw != 0 ||
            definition.Font is not null ||
            definition.Fullscreen != 0 ||
            definition.ItemCount != 0 ||
            definition.FontIndex != 0 ||
            HasNonzeroFixedInts(definition.CursorItems, 4) ||
            definition.FadeCycle != 0 ||
            definition.FadeClamp != 0 ||
            definition.FadeAmount != 0 ||
            definition.FadeInAmount != 0 ||
            definition.BlurRadius != 0 ||
            definition.OnOpen.Raw != 0 ||
            definition.OnOpenSet is not null ||
            definition.OnCloseRequest.Raw != 0 ||
            definition.OnCloseRequestSet is not null ||
            definition.OnClose.Raw != 0 ||
            definition.OnCloseSet is not null ||
            definition.OnEsc.Raw != 0 ||
            definition.OnEscSet is not null ||
            definition.ExecKeys.Raw != 0 ||
            definition.ExecKeyHandler is not null ||
            definition.VisibleExpression.Raw != 0 ||
            definition.VisibleStatement is not null ||
            definition.AllowedBinding.Raw != 0 ||
            definition.AllowedBindingString is not null ||
            definition.SoundName.Raw != 0 ||
            definition.SoundNameString is not null ||
            definition.ImageTrack != 0 ||
            HasNonzeroVec4(definition.FocusColor) ||
            definition.RectXExpression.Raw != 0 ||
            definition.RectXStatement is not null ||
            definition.RectYExpression.Raw != 0 ||
            definition.RectYStatement is not null ||
            definition.RectWExpression.Raw != 0 ||
            definition.RectWStatement is not null ||
            definition.RectHExpression.Raw != 0 ||
            definition.RectHStatement is not null ||
            definition.ItemsPointer.Raw != 0 ||
            definition.Items.Count != 0 ||
            HasNonzeroTransitions(definition.ScaleTransitions) ||
            HasNonzeroTransitions(definition.AlphaTransitions) ||
            HasNonzeroTransitions(definition.XTransitions) ||
            HasNonzeroTransitions(definition.YTransitions) ||
            definition.ExpressionData.Raw != 0 ||
            definition.ExpressionDataValue is not null)
        {
            throw new InvalidDataException(
                "A comma-prefixed Menu provider must have a zeroed reference body.");
        }
    }

    private static bool HasNonzeroWindowBody(
        WindowDef window,
        bool ignoreName)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (!ignoreName &&
                (window.NamePointer.Raw != 0 || window.Name is not null)) ||
            HasNonzeroRectangle(window.Rect) ||
            HasNonzeroRectangle(window.RectClient) ||
            window.GroupPointer.Raw != 0 ||
            window.Group is not null ||
            window.Style != 0 ||
            window.Border != 0 ||
            window.OwnerDraw != 0 ||
            window.OwnerDrawFlags != 0 ||
            window.BorderSize != 0 ||
            window.StaticFlags != 0 ||
            HasNonzeroFixedFlags(window.DynamicFlags, 4) ||
            window.NextTime != 0 ||
            HasNonzeroVec4(window.ForeColor) ||
            HasNonzeroVec4(window.BackColor) ||
            HasNonzeroVec4(window.BorderColor) ||
            HasNonzeroVec4(window.OutlineColor) ||
            HasNonzeroVec4(window.DisableColor) ||
            window.Background.Raw != 0 ||
            window.BackgroundMaterial is not null ||
            window.BackgroundMaterialName is not null;
    }

    private static bool HasNonzeroRectangle(RectangleDef value) =>
        value.X != 0 || value.Y != 0 || value.W != 0 || value.H != 0 ||
        value.HorzAlign != 0 || value.VertAlign != 0 || value.Pad12 != 0;

    private static bool HasNonzeroVec4(Vec4 value) =>
        value.A != 0 || value.R != 0 || value.G != 0 || value.B != 0;

    private static bool HasNonzeroFixedInts(
        IReadOnlyList<int> values,
        int expectedCount) =>
        values.Count != 0 &&
        (values.Count != expectedCount || values.Any(value => value != 0));

    private static bool HasNonzeroFixedFlags(
        IReadOnlyList<WindowDynamicFlags> values,
        int expectedCount) =>
        values.Count != 0 &&
        (values.Count != expectedCount || values.Any(value => value != 0));

    private static bool HasNonzeroTransitions(
        IReadOnlyList<MenuTransition> values) =>
        values.Count != 0 &&
        (values.Count != 4 || values.Any(value =>
            value is null ||
            value.TransitionType != 0 ||
            value.TargetField != 0 ||
            value.StartTime != 0 ||
            value.StartValue != 0 ||
            value.EndValue != 0 ||
            value.Time != 0 ||
            value.EndTriggerType != 0));

    private sealed class StorageFreezer
    {
        private readonly LinkAssetFreezeScope _freeze;
        private readonly Dictionary<object, LinkStorageSymbol> _frozenStorage =
            new(ReferenceEqualityComparer.Instance);

        public StorageFreezer(LinkAssetFreezeScope freeze) =>
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));

        public LinkStorageSymbol FreezeMenu(
            MenuDefAsset definition,
            LinkStorageSymbol nameStorage)
        {
            ValidateMenu(definition);

            var writer = new LinkTemplateWriter(MenuDefAsset.SerializedSize);
            WriteWindow(writer, definition.Window);
            writer.Skip(sizeof(int));
            writer.WriteInt32(definition.Fullscreen);
            writer.WriteInt32(definition.Items.Count);
            writer.WriteInt32(definition.FontIndex);
            WriteInts(writer, definition.CursorItems, 4, "Menu.CursorItems");
            writer.WriteInt32(definition.FadeCycle);
            writer.WriteSingle(definition.FadeClamp);
            writer.WriteSingle(definition.FadeAmount);
            writer.WriteSingle(definition.FadeInAmount);
            writer.WriteSingle(definition.BlurRadius);
            writer.Skip(6 * sizeof(int));
            writer.Skip(2 * sizeof(int));
            writer.WriteInt32(definition.ImageTrack);
            WriteVec4(writer, definition.FocusColor);
            writer.Skip(5 * sizeof(int));
            WriteTransitions(writer, definition.ScaleTransitions, "Menu.ScaleTransitions");
            WriteTransitions(writer, definition.AlphaTransitions, "Menu.AlphaTransitions");
            WriteTransitions(writer, definition.XTransitions, "Menu.XTransitions");
            WriteTransitions(writer, definition.YTransitions, "Menu.YTransitions");
            writer.Skip(sizeof(int));

            LinkStorageSymbol root = LinkStorageSymbol.CreateSourceBytes(
                XFileBlockType.TEMP,
                writer.Complete(),
                alignment: 4);
            var operations = new List<LinkOperation>();

            // Native Load_MenuDef children order is not field order.
            AddDirect(
                operations,
                root,
                0x2ec,
                FreezeOptionalNode(
                    definition.ExpressionData.Untyped,
                    definition.ExpressionDataValue,
                    FreezeSupportingData,
                    "Menu.ExpressionData"),
                "Menu.ExpressionData");
            operations.Add(XString(
                root,
                0x00,
                nameStorage,
                "Asset.Name"));
            AppendWindowOperations(
                operations,
                root,
                0,
                definition.Window,
                includeName: false,
                "Menu.Window");
            AddXString(
                operations,
                root,
                0xb0,
                definition.FontPointer.Untyped,
                definition.Font,
                "Menu.Font");
            AddDirect(operations, root, 0xe4,
                FreezeOptionalNode(definition.OnOpen.Untyped, definition.OnOpenSet,
                    FreezeEventSet, "Menu.OnOpen"), "Menu.OnOpen");
            AddDirect(operations, root, 0xec,
                FreezeOptionalNode(definition.OnClose.Untyped, definition.OnCloseSet,
                    FreezeEventSet, "Menu.OnClose"), "Menu.OnClose");
            AddDirect(operations, root, 0xe8,
                FreezeOptionalNode(definition.OnCloseRequest.Untyped,
                    definition.OnCloseRequestSet, FreezeEventSet,
                    "Menu.OnCloseRequest"), "Menu.OnCloseRequest");
            AddDirect(operations, root, 0xf0,
                FreezeOptionalNode(definition.OnEsc.Untyped, definition.OnEscSet,
                    FreezeEventSet, "Menu.OnEsc"), "Menu.OnEsc");
            AddDirect(operations, root, 0xf4,
                FreezeOptionalNode(definition.ExecKeys.Untyped,
                    definition.ExecKeyHandler, FreezeKeyHandler,
                    "Menu.ExecKeys"), "Menu.ExecKeys");
            AddDirect(operations, root, 0xf8,
                FreezeOptionalNode(definition.VisibleExpression.Untyped,
                    definition.VisibleStatement, FreezeStatement,
                    "Menu.VisibleExpression"), "Menu.VisibleExpression");
            AddXString(operations, root, 0xfc, definition.AllowedBinding.Untyped,
                definition.AllowedBindingString, "Menu.AllowedBinding");
            AddXString(operations, root, 0x100, definition.SoundName.Untyped,
                definition.SoundNameString, "Menu.SoundName");
            AddDirect(operations, root, 0x118,
                FreezeOptionalNode(definition.RectXExpression.Untyped,
                    definition.RectXStatement, FreezeStatement,
                    "Menu.RectXExpression"), "Menu.RectXExpression");
            AddDirect(operations, root, 0x11c,
                FreezeOptionalNode(definition.RectYExpression.Untyped,
                    definition.RectYStatement, FreezeStatement,
                    "Menu.RectYExpression"), "Menu.RectYExpression");
            AddDirect(operations, root, 0x120,
                FreezeOptionalNode(definition.RectWExpression.Untyped,
                    definition.RectWStatement, FreezeStatement,
                    "Menu.RectWExpression"), "Menu.RectWExpression");
            AddDirect(operations, root, 0x124,
                FreezeOptionalNode(definition.RectHExpression.Untyped,
                    definition.RectHStatement, FreezeStatement,
                    "Menu.RectHExpression"), "Menu.RectHExpression");
            AddDirect(operations, root, 0x128,
                FreezeItemTable(
                    definition.ItemsPointer.Untyped,
                    definition.Items,
                    "Menu.Items"),
                "Menu.Items");

            root.FreezeOperations(operations);
            return root;
        }

        private LinkStorageSymbol FreezeItem(
            XPointerReference pointer,
            ItemDefAsset item,
            string fieldPath)
        {
            if (TryGetFrozenStorage(item, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;

            ValidateItem(item, fieldPath);
            var writer = new LinkTemplateWriter(ItemDefAsset.SerializedSize);
            WriteWindow(writer, item.Window);
            WriteRectangles(writer, item.TextRect, 4, $"{fieldPath}.TextRect");
            writer.WriteInt32((int)item.Type);
            writer.WriteInt32(item.DataType);
            writer.WriteInt32((int)item.Align);
            writer.WriteInt32((int)item.FontEnum);
            writer.WriteInt32(item.TextAlignMode);
            writer.WriteSingle(item.TextAlignX);
            writer.WriteSingle(item.TextAlignY);
            writer.WriteSingle(item.TextScale);
            writer.WriteInt32((int)item.TextStyle);
            writer.WriteInt32(item.GameMsgWindowIndex);
            writer.WriteInt32(item.GameMsgWindowMode);
            writer.Skip(sizeof(int));
            writer.WriteInt32((int)item.ItemFlags);
            writer.Skip(sizeof(int)); // Runtime parent is patched by DB_AddXAsset.
            writer.Skip(8 * sizeof(int));
            writer.Skip(2 * sizeof(int));
            writer.Skip(sizeof(int));
            writer.Skip(sizeof(int));
            writer.WriteInt32((int)item.DvarFlags);
            writer.Skip(sizeof(int));
            writer.WriteSingle(item.Special);
            WriteInts(writer, item.CursorPos, 4, $"{fieldPath}.CursorPos");
            writer.Skip(sizeof(int));
            writer.WriteInt32(item.ImageTrack);
            writer.WriteInt32(item.LoadedFloatExpressions.Count);
            writer.Skip(5 * sizeof(int));
            WriteVec4(writer, item.GlowColor);
            writer.WriteByte(item.DecayActive);
            writer.WriteByte(item.DecayActivePad0);
            writer.WriteByte(item.DecayActivePad1);
            writer.WriteByte(item.DecayActivePad2);
            writer.Skip(sizeof(int)); // Runtime text-FX birth time.
            writer.WriteInt32(item.FxLetterTime);
            writer.WriteInt32(item.FxDecayStartTime);
            writer.WriteInt32(item.FxDecayDuration);
            writer.Skip(sizeof(int)); // Runtime sound-playback cache.

            return FreezeDirectStorage(
                pointer,
                item,
                writer.Complete(),
                (storage, operations) =>
                {
                    AppendWindowOperations(
                        operations,
                        storage,
                        0,
                        item.Window,
                        includeName: true,
                        $"{fieldPath}.Window");
                    AddXString(operations, storage, 0x12c, item.Text.Untyped,
                        item.TextString, $"{fieldPath}.Text");
                    AddDirect(operations, storage, 0x138,
                        FreezeOptionalNode(item.MouseEnterText.Untyped,
                            item.MouseEnterTextSet, FreezeEventSet,
                            $"{fieldPath}.MouseEnterText"),
                        $"{fieldPath}.MouseEnterText");
                    AddDirect(operations, storage, 0x13c,
                        FreezeOptionalNode(item.MouseExitText.Untyped,
                            item.MouseExitTextSet, FreezeEventSet,
                            $"{fieldPath}.MouseExitText"),
                        $"{fieldPath}.MouseExitText");
                    AddDirect(operations, storage, 0x140,
                        FreezeOptionalNode(item.MouseEnter.Untyped, item.MouseEnterSet,
                            FreezeEventSet, $"{fieldPath}.MouseEnter"),
                        $"{fieldPath}.MouseEnter");
                    AddDirect(operations, storage, 0x144,
                        FreezeOptionalNode(item.MouseExit.Untyped, item.MouseExitSet,
                            FreezeEventSet, $"{fieldPath}.MouseExit"),
                        $"{fieldPath}.MouseExit");
                    AddDirect(operations, storage, 0x148,
                        FreezeOptionalNode(item.Action.Untyped, item.ActionSet,
                            FreezeEventSet, $"{fieldPath}.Action"),
                        $"{fieldPath}.Action");
                    AddDirect(operations, storage, 0x14c,
                        FreezeOptionalNode(item.Accept.Untyped, item.AcceptSet,
                            FreezeEventSet, $"{fieldPath}.Accept"),
                        $"{fieldPath}.Accept");
                    AddDirect(operations, storage, 0x150,
                        FreezeOptionalNode(item.OnFocus.Untyped, item.OnFocusSet,
                            FreezeEventSet, $"{fieldPath}.OnFocus"),
                        $"{fieldPath}.OnFocus");
                    AddDirect(operations, storage, 0x154,
                        FreezeOptionalNode(item.LeaveFocus.Untyped, item.LeaveFocusSet,
                            FreezeEventSet, $"{fieldPath}.LeaveFocus"),
                        $"{fieldPath}.LeaveFocus");
                    AddXString(operations, storage, 0x158, item.Dvar.Untyped,
                        item.DvarString, $"{fieldPath}.Dvar");
                    AddXString(operations, storage, 0x15c, item.DvarTest.Untyped,
                        item.DvarTestString, $"{fieldPath}.DvarTest");
                    AddDirect(operations, storage, 0x160,
                        FreezeOptionalNode(item.OnKey.Untyped, item.OnKeyHandler,
                            FreezeKeyHandler, $"{fieldPath}.OnKey"),
                        $"{fieldPath}.OnKey");
                    AddXString(operations, storage, 0x164, item.EnableDvar.Untyped,
                        item.EnableDvarString, $"{fieldPath}.EnableDvar");
                    AddDependency(operations, storage, 0x16c,
                        FreezeProviderDependency(
                            item.FocusSound.Untyped,
                            item.FocusSoundAsset,
                            XAssetType.Sound,
                            $"{fieldPath}.FocusSound",
                            item.FocusSoundName,
                            allowExternalReference: _freeze.IsAuthoredDetached));
                    AddItemDataOperation(operations, storage, item, fieldPath);
                    AddDirect(operations, storage, 0x190,
                        FreezeFloatExpressionTable(
                            item.FloatExpressions.Untyped,
                            item.LoadedFloatExpressions,
                            $"{fieldPath}.FloatExpressions"),
                        $"{fieldPath}.FloatExpressions");
                    AddDirect(operations, storage, 0x194,
                        FreezeOptionalNode(item.VisibleExpression.Untyped,
                            item.VisibleStatement, FreezeStatement,
                            $"{fieldPath}.VisibleExpression"),
                        $"{fieldPath}.VisibleExpression");
                    AddDirect(operations, storage, 0x198,
                        FreezeOptionalNode(item.DisabledExpression.Untyped,
                            item.DisabledStatement, FreezeStatement,
                            $"{fieldPath}.DisabledExpression"),
                        $"{fieldPath}.DisabledExpression");
                    AddDirect(operations, storage, 0x19c,
                        FreezeOptionalNode(item.TextExpression.Untyped,
                            item.TextStatement, FreezeStatement,
                            $"{fieldPath}.TextExpression"),
                        $"{fieldPath}.TextExpression");
                    AddDirect(operations, storage, 0x1a0,
                        FreezeOptionalNode(item.MaterialExpression.Untyped,
                            item.MaterialStatement, FreezeStatement,
                            $"{fieldPath}.MaterialExpression"),
                        $"{fieldPath}.MaterialExpression");

                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeEventSet(
            XPointerReference pointer,
            MenuEventHandlerSet value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            if (value.EventHandlerCount < 0 ||
                value.EventHandlerCount != value.Handlers.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.EventHandlerCount must equal its nonnegative detached handler rows.");
            }

            var writer = new LinkTemplateWriter(MenuEventHandlerSet.SerializedSize);
            writer.WriteInt32(value.Handlers.Count);
            writer.Skip(sizeof(int));
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    LinkStorageSymbol? table = FreezeEventHandlerTable(
                        value.EventHandlers.Untyped,
                        value.Handlers,
                        $"{fieldPath}.Handlers");
                    AddDirect(
                        operations,
                        storage,
                        0x04,
                        table,
                        $"{fieldPath}.Handlers");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeEventHandler(
            XPointerReference pointer,
            MenuEventHandler value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            if (!Enum.IsDefined(value.EventType))
            {
                throw new InvalidDataException(
                    $"{fieldPath}.EventType has unsupported value {(byte)value.EventType}.");
            }

            var writer = new LinkTemplateWriter(MenuEventHandler.SerializedSize);
            writer.Skip(sizeof(int));
            writer.WriteByte((byte)value.EventType);
            writer.WriteByte(value.Pad05);
            writer.WriteByte(value.Pad06);
            writer.WriteByte(value.Pad07);
            ValidateEventHandlerUnion(value, fieldPath);
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    switch (value.EventType)
                    {
                        case MenuEventHandlerType.UnconditionalScript:
                            if (value.EventData.UnconditionalScript is not { } script)
                                throw new InvalidDataException($"{fieldPath} is missing its unconditional-script union arm.");
                            AddXString(operations, storage, 0, script.Script.Untyped,
                                value.UnconditionalScript, $"{fieldPath}.Script");
                            break;
                        case MenuEventHandlerType.ConditionalScript:
                            if (value.EventData.ConditionalScript is not { } conditional)
                                throw new InvalidDataException($"{fieldPath} is missing its conditional-script union arm.");
                            AddDirect(operations, storage, 0,
                                FreezeOptionalNode(
                                    conditional.ConditionalScriptPointer.Untyped,
                                    value.ConditionalScript,
                                    FreezeConditional,
                                    $"{fieldPath}.Conditional"),
                                $"{fieldPath}.Conditional");
                            break;
                        case MenuEventHandlerType.ElseScript:
                            if (value.EventData.ElseScript is not { } elseScript)
                                throw new InvalidDataException($"{fieldPath} is missing its else-script union arm.");
                            AddDirect(operations, storage, 0,
                                FreezeOptionalNode(
                                    elseScript.EventHandlerSetPointer.Untyped,
                                    value.ElseScriptSet,
                                    FreezeEventSet,
                                    $"{fieldPath}.Else"),
                                $"{fieldPath}.Else");
                            break;
                        case MenuEventHandlerType.SetLocalVarBool:
                        case MenuEventHandlerType.SetLocalVarInt:
                        case MenuEventHandlerType.SetLocalVarFloat:
                        case MenuEventHandlerType.SetLocalVarString:
                            if (value.EventData.SetLocalVarData is not { } local)
                                throw new InvalidDataException($"{fieldPath} is missing its set-local-variable union arm.");
                            AddDirect(operations, storage, 0,
                                FreezeOptionalNode(
                                    local.SetLocalVarDataPointer.Untyped,
                                    value.SetLocalVarData,
                                    FreezeSetLocalVar,
                                    $"{fieldPath}.SetLocalVar"),
                                $"{fieldPath}.SetLocalVar");
                            break;
                    }

                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeConditional(
            XPointerReference pointer,
            ConditionalScript value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                value,
                new byte[ConditionalScript.SerializedSize],
                (storage, operations) =>
                {
                    // Native order is expression first, then handler set.
                    AddDirect(operations, storage, 0x04,
                        FreezeOptionalNode(value.EventExpression.Untyped,
                            value.EventStatement, FreezeStatement,
                            $"{fieldPath}.Expression"),
                        $"{fieldPath}.Expression");
                    AddDirect(operations, storage, 0x00,
                        FreezeOptionalNode(value.EventHandlerSet.Untyped,
                            value.EventHandlers, FreezeEventSet,
                            $"{fieldPath}.Handlers"),
                        $"{fieldPath}.Handlers");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeSetLocalVar(
            XPointerReference pointer,
            SetLocalVarData value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                value,
                new byte[SetLocalVarData.SerializedSize],
                (storage, operations) =>
                {
                    AddXString(operations, storage, 0, value.LocalVarName.Untyped,
                        value.LocalVarNameString, $"{fieldPath}.Name");
                    AddDirect(operations, storage, 4,
                        FreezeOptionalNode(value.Expression.Untyped,
                            value.ExpressionStatement, FreezeStatement,
                            $"{fieldPath}.Expression"),
                        $"{fieldPath}.Expression");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeKeyHandler(
            XPointerReference pointer,
            ItemKeyHandler value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            var writer = new LinkTemplateWriter(ItemKeyHandler.SerializedSize);
            writer.WriteInt32(value.Key);
            writer.Skip(2 * sizeof(int));
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    AddDirect(operations, storage, 4,
                        FreezeOptionalNode(value.Action.Untyped, value.ActionSet,
                            FreezeEventSet, $"{fieldPath}.Action"),
                        $"{fieldPath}.Action");
                    AddDirect(operations, storage, 8,
                        FreezeOptionalNode(value.Next.Untyped, value.NextHandler,
                            FreezeKeyHandler, $"{fieldPath}.Next"),
                        $"{fieldPath}.Next");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeStatement(
            XPointerReference pointer,
            Statement value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            if (value.NumEntries < 0 || value.NumEntries != value.LoadedEntries.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.NumEntries must equal its nonnegative detached entry count.");
            }

            var writer = new LinkTemplateWriter(Statement.SerializedSize);
            writer.WriteInt32(value.LoadedEntries.Count);
            writer.Skip(2 * sizeof(int));
            writer.Skip(sizeof(int)); // Runtime expression clock cache.
            writer.Skip(Operand.SerializedSize); // Runtime result cache.
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    AddDirect(operations, storage, 4,
                        FreezeExpressionEntries(
                            value.Entries.Untyped,
                            value.LoadedEntries,
                            $"{fieldPath}.Entries"),
                        $"{fieldPath}.Entries");
                    AddDirect(operations, storage, 8,
                        FreezeOptionalNode(value.SupportingData.Untyped,
                            value.SupportingDataValue, FreezeSupportingData,
                            $"{fieldPath}.SupportingData"),
                        $"{fieldPath}.SupportingData");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeSupportingData(
            XPointerReference pointer,
            ExpressionSupportingData value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            UIFunctionList functions = value.UiFunctions ??
                throw new InvalidDataException($"{fieldPath}.UiFunctions cannot be null.");
            StaticDvarList dvars = value.StaticDvarList ??
                throw new InvalidDataException($"{fieldPath}.StaticDvarList cannot be null.");
            StringList strings = value.UiStrings ??
                throw new InvalidDataException($"{fieldPath}.UiStrings cannot be null.");
            ValidateCount(functions.TotalFunctions,
                functions.LoadedFunctions.Count, $"{fieldPath}.UiFunctions");
            ValidateCount(dvars.NumStaticDvars,
                dvars.LoadedStaticDvars.Count, $"{fieldPath}.StaticDvars");
            ValidateCount(strings.TotalStrings,
                strings.LoadedStrings.Count, $"{fieldPath}.UiStrings");

            var writer = new LinkTemplateWriter(ExpressionSupportingData.SerializedSize);
            writer.WriteInt32(functions.LoadedFunctions.Count);
            writer.Skip(sizeof(int));
            writer.WriteInt32(dvars.LoadedStaticDvars.Count);
            writer.Skip(sizeof(int));
            writer.WriteInt32(strings.LoadedStrings.Count);
            writer.Skip(sizeof(int));
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    AddDirect(operations, storage, 0x04,
                        FreezeStatementReferenceTable(
                            functions.Functions.Untyped,
                            functions.LoadedFunctions,
                            $"{fieldPath}.UiFunctions"),
                        $"{fieldPath}.UiFunctions");
                    AddDirect(operations, storage, 0x0c,
                        FreezeStaticDvarReferenceTable(
                            dvars.StaticDvars.Untyped,
                            dvars.LoadedStaticDvars,
                            $"{fieldPath}.StaticDvars"),
                        $"{fieldPath}.StaticDvars");
                    AddDirect(operations, storage, 0x14,
                        FreezeXStringReferenceTable(
                            strings.Strings.Untyped,
                            strings.LoadedStrings,
                            $"{fieldPath}.UiStrings"),
                        $"{fieldPath}.UiStrings");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeStaticDvar(
            XPointerReference pointer,
            StaticDvar value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                value,
                new byte[StaticDvar.SerializedSize],
                (storage, operations) =>
                {
                    AddXString(operations, storage, 4, value.DvarName.Untyped,
                        value.DvarNameString, $"{fieldPath}.Name");
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeEditField(
            XPointerReference pointer,
            EditFieldDef value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            ValidateFinite(value.MinVal, $"{fieldPath}.MinVal");
            ValidateFinite(value.MaxVal, $"{fieldPath}.MaxVal");
            ValidateFinite(value.DefVal, $"{fieldPath}.DefVal");
            ValidateFinite(value.Range, $"{fieldPath}.Range");
            if (value.MaxCharsGotoNext is not (0 or 1))
                throw new InvalidDataException($"{fieldPath}.MaxCharsGotoNext must be 0 or 1.");
            var writer = new LinkTemplateWriter(EditFieldDef.SerializedSize);
            writer.WriteSingle(value.MinVal);
            writer.WriteSingle(value.MaxVal);
            writer.WriteSingle(value.DefVal);
            writer.WriteSingle(value.Range);
            writer.WriteInt32(value.MaxChars);
            writer.WriteInt32(value.MaxCharsGotoNext);
            writer.WriteInt32(value.MaxPaintChars);
            writer.Skip(sizeof(int)); // Runtime text viewport offset.
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                freezeChildren: null,
                fieldPath);
        }

        private LinkStorageSymbol FreezeListBox(
            XPointerReference pointer,
            ListBoxDef value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            ValidateFixedCount(value.StartPos, 4, $"{fieldPath}.StartPos");
            ValidateFixedCount(value.EndPos, 4, $"{fieldPath}.EndPos");
            ValidateFixedCount(value.ColumnInfo, 16, $"{fieldPath}.ColumnInfo");
            if (value.NumColumns is < 0 or > 16)
                throw new InvalidDataException($"{fieldPath}.NumColumns must be in [0, 16].");
            if (value.NotSelectable is not (0 or 1) ||
                value.NoScrollbars is not (0 or 1) ||
                value.UsePaging is not (0 or 1))
            {
                throw new InvalidDataException(
                    $"{fieldPath} boolean fields must be 0 or 1.");
            }
            ValidateFinite(value.ElementWidth, $"{fieldPath}.ElementWidth");
            ValidateFinite(value.ElementHeight, $"{fieldPath}.ElementHeight");
            ValidateVec4(value.SelectBorder, $"{fieldPath}.SelectBorder");

            var writer = new LinkTemplateWriter(ListBoxDef.SerializedSize);
            writer.Skip(8 * sizeof(int)); // Per-client visible-range cursors.
            writer.WriteInt32(value.DrawPadding);
            writer.WriteSingle(value.ElementWidth);
            writer.WriteSingle(value.ElementHeight);
            writer.WriteInt32(value.ElementStyle);
            writer.WriteInt32(value.NumColumns);
            foreach (ColumnInfo column in value.ColumnInfo)
            {
                if (column is null)
                    throw new InvalidDataException($"{fieldPath}.ColumnInfo cannot contain null.");
                writer.WriteInt32(column.Pos);
                writer.WriteInt32(column.Width);
                writer.WriteInt32(column.MaxChars);
                writer.WriteInt32(column.Alignment);
            }
            writer.Skip(sizeof(int));
            writer.WriteInt32(value.NotSelectable);
            writer.WriteInt32(value.NoScrollbars);
            writer.WriteInt32(value.UsePaging);
            WriteVec4(writer, value.SelectBorder);
            writer.Skip(sizeof(int));
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    AddDirect(operations, storage, 0x134,
                        FreezeOptionalNode(value.DoubleClick.Untyped,
                            value.DoubleClickSet, FreezeEventSet,
                            $"{fieldPath}.DoubleClick"),
                        $"{fieldPath}.DoubleClick");
                    AddDependency(operations, storage, 0x154,
                        FreezeProviderDependency(
                            value.SelectIcon.Untyped,
                            value.SelectIconMaterial,
                            XAssetType.Material,
                            $"{fieldPath}.SelectIcon",
                            value.SelectIconMaterialName,
                            allowExternalReference: _freeze.IsAuthoredDetached));
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeMulti(
            XPointerReference pointer,
            MultiDef value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            ValidateFixedCount(value.DvarList, MultiDef.EntryCapacity,
                $"{fieldPath}.DvarList");
            ValidateFixedCount(value.DvarListStrings, MultiDef.EntryCapacity,
                $"{fieldPath}.DvarListStrings");
            ValidateFixedCount(value.DvarStr, MultiDef.EntryCapacity,
                $"{fieldPath}.DvarStr");
            ValidateFixedCount(value.DvarStrStrings, MultiDef.EntryCapacity,
                $"{fieldPath}.DvarStrStrings");
            ValidateFixedCount(value.DvarValue, MultiDef.EntryCapacity,
                $"{fieldPath}.DvarValue");
            if (value.Count is < 0 or > MultiDef.EntryCapacity)
                throw new InvalidDataException($"{fieldPath}.Count must be in [0, 32].");
            if (value.StrDef is not (0 or 1))
                throw new InvalidDataException($"{fieldPath}.StrDef must be 0 or 1.");

            var writer = new LinkTemplateWriter(MultiDef.SerializedSize);
            writer.Skip(2 * MultiDef.EntryCapacity * sizeof(int));
            foreach (float number in value.DvarValue)
            {
                ValidateFinite(number, $"{fieldPath}.DvarValue");
                writer.WriteSingle(number);
            }
            writer.WriteInt32(value.Count);
            writer.WriteInt32(value.StrDef);
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                (storage, operations) =>
                {
                    for (int index = 0; index < MultiDef.EntryCapacity; index++)
                    {
                        AddXString(operations, storage, checked(index * sizeof(int)),
                            value.DvarList[index].Untyped,
                            value.DvarListStrings[index],
                            $"{fieldPath}.DvarList[{index}]");
                    }
                    for (int index = 0; index < MultiDef.EntryCapacity; index++)
                    {
                        AddXString(operations, storage,
                            checked((MultiDef.EntryCapacity + index) * sizeof(int)),
                            value.DvarStr[index].Untyped,
                            value.DvarStrStrings[index],
                            $"{fieldPath}.DvarStr[{index}]");
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol FreezeNewsTicker(
            XPointerReference pointer,
            NewsTickerDef value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            var writer = new LinkTemplateWriter(NewsTickerDef.SerializedSize);
            writer.WriteInt32(value.FeedId);
            writer.WriteInt32(value.Speed);
            writer.WriteInt32(value.Spacing);
            writer.Skip(4 * sizeof(int)); // Runtime ticker traversal state.
            return FreezeDirectStorage(
                pointer,
                value,
                writer.Complete(),
                freezeChildren: null,
                fieldPath);
        }

        private LinkStorageSymbol FreezeTextScroll(
            XPointerReference pointer,
            TextScrollDef value,
            string fieldPath)
        {
            if (TryGetFrozenStorage(value, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                value,
                new byte[TextScrollDef.SerializedSize],
                freezeChildren: null,
                fieldPath);
        }

        private LinkStorageSymbol? FreezeItemTable(
            XPointerReference pointer,
            IReadOnlyList<ItemDefReference> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                values,
                new byte[checked(values.Count * sizeof(int))],
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        ItemDefReference row = values[index] ?? throw new InvalidDataException(
                            $"{fieldPath}[{index}] cannot be null.");
                        if (row.Index != index)
                            throw new InvalidDataException($"{fieldPath}[{index}] retains ordinal {row.Index}.");
                        ItemDefAsset item = row.Item ?? throw new InvalidDataException(
                            $"{fieldPath}[{index}] must retain its required ItemDef.");
                        operations.Add(Direct(table, checked(index * sizeof(int)),
                            FreezeItem(
                                row.Pointer.Untyped,
                                item,
                                $"{fieldPath}[{index}]"),
                            $"{fieldPath}[{index}]"));
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeEventHandlerTable(
            XPointerReference pointer,
            IReadOnlyList<MenuEventHandlerReference> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                values,
                new byte[checked(values.Count * sizeof(int))],
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        MenuEventHandlerReference row = values[index] ??
                            throw new InvalidDataException($"{fieldPath}[{index}] cannot be null.");
                        if (row.Index != index)
                            throw new InvalidDataException($"{fieldPath}[{index}] retains ordinal {row.Index}.");
                        LinkStorageSymbol? handler = FreezeOptionalNode(
                            row.Pointer.Untyped,
                            row.Handler,
                            FreezeEventHandler,
                            $"{fieldPath}[{index}]");
                        if (handler is not null)
                        {
                            operations.Add(Direct(table, checked(index * sizeof(int)),
                                handler, $"{fieldPath}[{index}]"));
                        }
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeExpressionEntries(
            XPointerReference pointer,
            IReadOnlyList<ExpressionEntry> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            var writer = new LinkTemplateWriter(
                checked(values.Count * ExpressionEntry.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                ExpressionEntry entry = values[index] ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null.");
                if (!Enum.IsDefined(entry.Kind))
                    throw new InvalidDataException($"{fieldPath}[{index}].Kind is unsupported.");
                writer.WriteInt32((int)entry.Kind);
                if (entry.Kind == ExpressionEntryKind.Operator)
                {
                    if (entry.StringValue is not null ||
                        entry.FunctionStatement is not null)
                    {
                        throw new InvalidDataException(
                            $"{fieldPath}[{index}] operator retains an inactive operand child.");
                    }
                    if (!Enum.IsDefined((OperationEnum)entry.OperationCode))
                        throw new InvalidDataException($"{fieldPath}[{index}].OperationCode is unsupported.");
                    writer.WriteInt32(entry.OperationCode);
                    writer.Skip(sizeof(int)); // Ignored operator-tail word.
                }
                else
                {
                    if (!Enum.IsDefined(entry.Operand.DataType))
                        throw new InvalidDataException($"{fieldPath}[{index}].Operand.DataType is unsupported.");
                    ValidateOperandUnion(entry, $"{fieldPath}[{index}]");
                    writer.WriteInt32((int)entry.Operand.DataType);
                    if (entry.Operand.DataType is ExpDataType.VAL_STRING or ExpDataType.VAL_FUNCTION)
                        writer.Skip(sizeof(int));
                    else
                        writer.WriteInt32(entry.Operand.EncodedValue);
                }
            }
            return FreezeDirectStorage(
                pointer,
                values,
                writer.Complete(),
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        ExpressionEntry entry = values[index];
                        int pointerOffset = checked(index * ExpressionEntry.SerializedSize + 0x08);
                        if (entry.Kind != ExpressionEntryKind.Operand)
                            continue;
                        if (entry.Operand.DataType == ExpDataType.VAL_STRING)
                        {
                            var operand = (StringOperandValue)entry.Operand.Value;
                            AddXString(operations, table, pointerOffset,
                                operand.StringPointer.Untyped,
                                entry.StringValue,
                                $"{fieldPath}[{index}].String");
                        }
                        else if (entry.Operand.DataType == ExpDataType.VAL_FUNCTION)
                        {
                            var operand = (FunctionOperandValue)entry.Operand.Value;
                            AddDirect(operations, table, pointerOffset,
                                FreezeOptionalNode(
                                    operand.StatementPointer.Untyped,
                                    entry.FunctionStatement,
                                    FreezeStatement,
                                    $"{fieldPath}[{index}].Function"),
                                $"{fieldPath}[{index}].Function");
                        }
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeStatementReferenceTable(
            XPointerReference pointer,
            IReadOnlyList<StatementReference> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                values,
                new byte[checked(values.Count * sizeof(int))],
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        StatementReference row = values[index] ?? throw new InvalidDataException(
                            $"{fieldPath}[{index}] cannot be null.");
                        if (row.Index != index)
                            throw new InvalidDataException($"{fieldPath}[{index}] retains ordinal {row.Index}.");
                        AddDirect(operations, table, checked(index * sizeof(int)),
                            FreezeOptionalNode(row.Pointer.Untyped, row.Statement,
                                FreezeStatement, $"{fieldPath}[{index}]"),
                            $"{fieldPath}[{index}]");
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeStaticDvarReferenceTable(
            XPointerReference pointer,
            IReadOnlyList<StaticDvarReference> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                values,
                new byte[checked(values.Count * sizeof(int))],
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        StaticDvarReference row = values[index] ?? throw new InvalidDataException(
                            $"{fieldPath}[{index}] cannot be null.");
                        if (row.Index != index)
                            throw new InvalidDataException($"{fieldPath}[{index}] retains ordinal {row.Index}.");
                        AddDirect(operations, table, checked(index * sizeof(int)),
                            FreezeOptionalNode(row.Pointer.Untyped, row.StaticDvar,
                                FreezeStaticDvar, $"{fieldPath}[{index}]"),
                            $"{fieldPath}[{index}]");
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeXStringReferenceTable(
            XPointerReference pointer,
            IReadOnlyList<IW4.Assets.Assets.Menu.XStringReference> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            return FreezeDirectStorage(
                pointer,
                values,
                new byte[checked(values.Count * sizeof(int))],
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        IW4.Assets.Assets.Menu.XStringReference row = values[index] ??
                            throw new InvalidDataException(
                            $"{fieldPath}[{index}] cannot be null.");
                        if (row.Index != index)
                            throw new InvalidDataException($"{fieldPath}[{index}] retains ordinal {row.Index}.");
                        AddXString(operations, table, checked(index * sizeof(int)),
                            row.Pointer.Untyped, row.Value, $"{fieldPath}[{index}]");
                    }
                },
                fieldPath);
        }

        private LinkStorageSymbol? FreezeFloatExpressionTable(
            XPointerReference pointer,
            IReadOnlyList<ItemFloatExpression> values,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;
            if (TryGetFrozenStorage(values, pointer, fieldPath, out LinkStorageSymbol? existing))
                return existing;
            var writer = new LinkTemplateWriter(
                checked(values.Count * ItemFloatExpression.SerializedSize));
            for (int index = 0; index < values.Count; index++)
            {
                ItemFloatExpression value = values[index] ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null.");
                if (!Enum.IsDefined(value.Target))
                    throw new InvalidDataException($"{fieldPath}[{index}].Target is unsupported.");
                writer.WriteInt32((int)value.Target);
                writer.Skip(sizeof(int));
            }
            return FreezeDirectStorage(
                pointer,
                values,
                writer.Complete(),
                (table, operations) =>
                {
                    for (int index = 0; index < values.Count; index++)
                    {
                        ItemFloatExpression value = values[index];
                        AddDirect(operations, table,
                            checked(index * ItemFloatExpression.SerializedSize + 4),
                            FreezeOptionalNode(value.Expression.Untyped, value.Statement,
                                FreezeStatement, $"{fieldPath}[{index}].Statement"),
                            $"{fieldPath}[{index}].Statement");
                    }
                },
                fieldPath);
        }

        private void AddItemDataOperation(
            ICollection<LinkOperation> operations,
            LinkStorageSymbol owner,
            ItemDefAsset item,
            string fieldPath)
        {
            const int pointerOffset = 0x184;
            switch (item.TypeData.Value)
            {
                case EditFieldItemDefData data:
                    RequireItemType(item.Type, IsEditFieldType(item.Type), fieldPath, "EditField");
                    AddDirect(operations, owner, pointerOffset,
                        FreezeOptionalNode(data.EditFieldPointer.Untyped,
                            item.EditField, FreezeEditField,
                            $"{fieldPath}.EditField"),
                        $"{fieldPath}.EditField");
                    break;
                case ListBoxItemDefData data:
                    RequireItemType(item.Type, item.Type == ItemDefType.ListBox, fieldPath, "ListBox");
                    AddDirect(operations, owner, pointerOffset,
                        FreezeOptionalNode(data.ListBoxPointer.Untyped,
                            item.ListBox, FreezeListBox,
                            $"{fieldPath}.ListBox"),
                        $"{fieldPath}.ListBox");
                    break;
                case MultiItemDefData data:
                    RequireItemType(item.Type, item.Type == ItemDefType.Multi, fieldPath, "Multi");
                    AddDirect(operations, owner, pointerOffset,
                        FreezeOptionalNode(data.MultiPointer.Untyped,
                            item.Multi, FreezeMulti,
                            $"{fieldPath}.Multi"),
                        $"{fieldPath}.Multi");
                    break;
                case DvarEnumItemDefData data:
                    RequireItemType(item.Type, item.Type == ItemDefType.DvarEnum, fieldPath, "DvarEnum");
                    AddXString(operations, owner, pointerOffset,
                        data.DvarEnumNamePointer.Untyped,
                        item.DvarEnumName,
                        $"{fieldPath}.DvarEnumName");
                    break;
                case NewsTickerItemDefData data:
                    RequireItemType(item.Type, item.Type == ItemDefType.NewsTicker, fieldPath, "NewsTicker");
                    AddDirect(operations, owner, pointerOffset,
                        FreezeOptionalNode(data.NewsTickerPointer.Untyped,
                            item.NewsTicker, FreezeNewsTicker,
                            $"{fieldPath}.NewsTicker"),
                        $"{fieldPath}.NewsTicker");
                    break;
                case TextScrollItemDefData data:
                    RequireItemType(item.Type, item.Type == ItemDefType.TextScroll, fieldPath, "TextScroll");
                    AddDirect(operations, owner, pointerOffset,
                        FreezeOptionalNode(data.TextScrollPointer.Untyped,
                            item.TextScroll, FreezeTextScroll,
                            $"{fieldPath}.TextScroll"),
                        $"{fieldPath}.TextScroll");
                    break;
                case NoItemDefData:
                    if (IsTypedItemData(item.Type))
                    {
                        throw new InvalidDataException(
                            $"{fieldPath}.TypeData must retain the {item.Type} union arm, even when its pointer is null.");
                    }
                    // IW4 never traverses the generic data arm. The source
                    // word is non-semantic for item types without a typed arm,
                    // so canonical output deliberately keeps the template zero.
                    break;
                default:
                    throw new InvalidDataException($"{fieldPath}.TypeData has an unsupported union arm.");
            }
        }

        private LinkStorageSymbol FreezeDirectStorage(
            XPointerReference pointer,
            object semanticIdentity,
            byte[] sourceTemplate,
            Action<LinkStorageSymbol, ICollection<LinkOperation>>? freezeChildren,
            string fieldPath)
        {
            LinkStorageTarget target = _freeze.FreezeStorage(
                pointer,
                sourceTemplate,
                XFileBlockType.LARGE,
                alignment: 4,
                (owner, addend) =>
                {
                    if (addend != 0)
                    {
                        throw new InvalidDataException(
                            $"{fieldPath} is a complete direct-storage owner, not an interior view.");
                    }

                    if (!_frozenStorage.TryAdd(semanticIdentity, owner))
                    {
                        throw new InvalidDataException(
                            $"{fieldPath} attempted to freeze an already-active semantic node.");
                    }
                    var operations = new List<LinkOperation>();
                    freezeChildren?.Invoke(owner, operations);
                    return operations;
                },
                fieldPath);

            if (!target.CanMaterializeRoot ||
                target.View.Addend != 0 ||
                target.View.Length != target.View.Storage.Definition.ByteLength)
            {
                throw new InvalidDataException(
                    $"{fieldPath} did not freeze as a complete direct-storage owner.");
            }

            LinkStorageSymbol finalStorage = target.View.Storage;
            if (!_frozenStorage.TryGetValue(
                    semanticIdentity,
                    out LinkStorageSymbol? provisionalStorage))
            {
                throw new InvalidDataException(
                    $"{fieldPath} completed without its active semantic storage registration.");
            }
            if (!ReferenceEquals(provisionalStorage, finalStorage))
            {
                _freeze.ValidateReusedStorage(pointer, finalStorage, fieldPath);
                _frozenStorage[semanticIdentity] = finalStorage;
            }
            return finalStorage;
        }

        private bool TryGetFrozenStorage(
            object semanticIdentity,
            XPointerReference pointer,
            string fieldPath,
            [NotNullWhen(true)] out LinkStorageSymbol? storage)
        {
            if (!_frozenStorage.TryGetValue(semanticIdentity, out storage))
                return false;

            _freeze.ValidateReusedStorage(pointer, storage, fieldPath);
            return true;
        }

        private void AppendWindowOperations(
            ICollection<LinkOperation> operations,
            LinkStorageSymbol owner,
            int baseOffset,
            WindowDef window,
            bool includeName,
            string fieldPath)
        {
            if (includeName)
            {
                AddXString(operations, owner, baseOffset,
                    window.NamePointer.Untyped, window.Name, $"{fieldPath}.Name");
            }
            AddXString(operations, owner, checked(baseOffset + 0x2c),
                window.GroupPointer.Untyped, window.Group, $"{fieldPath}.Group");
            AddDependency(operations, owner, checked(baseOffset + 0xac),
                FreezeProviderDependency(
                    window.Background.Untyped,
                    window.BackgroundMaterial,
                    XAssetType.Material,
                    $"{fieldPath}.Background",
                    window.BackgroundMaterialName,
                    allowExternalReference: _freeze.IsAuthoredDetached));
        }

        private LinkStorageSymbol? FreezeOptionalNode<T>(
            XPointerReference pointer,
            T? value,
            Func<XPointerReference, T, string, LinkStorageSymbol> freeze,
            string fieldPath)
            where T : class
        {
            if (value is null)
            {
                if (pointer.Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} has a retained direct pointer but no semantic child.");
                }
                return null;
            }
            return freeze(pointer, value, fieldPath);
        }

        private static void AddDirect(
            ICollection<LinkOperation> operations,
            LinkStorageSymbol owner,
            int pointerOffset,
            LinkStorageSymbol? target,
            string fieldPath)
        {
            if (target is not null)
                operations.Add(Direct(owner, pointerOffset, target, fieldPath));
        }

        private static DirectStorageLinkOperation Direct(
            LinkStorageSymbol owner,
            int pointerOffset,
            LinkStorageSymbol target,
            string fieldPath) =>
            new(
                new LinkStorageCell(owner, pointerOffset),
                LinkStorageView.Whole(target),
                CanMaterializeRoot: true,
                fieldPath);

        private void AddXString(
            ICollection<LinkOperation> operations,
            LinkStorageSymbol owner,
            int pointerOffset,
            XPointerReference pointer,
            string? value,
            string fieldPath)
        {
            if (value is null)
            {
                if (pointer.Type != PointerType.Null)
                {
                    throw new InvalidDataException(
                        $"{fieldPath} has a retained XString pointer but no text.");
                }
                return;
            }
            operations.Add(XString(owner, pointerOffset,
                _freeze.FreezeRequiredXString(value, pointer, fieldPath),
                fieldPath));
        }

        private static XStringLinkOperation XString(
            LinkStorageSymbol owner,
            int pointerOffset,
            LinkStorageSymbol target,
            string fieldPath) =>
            new(
                new LinkStorageCell(owner, pointerOffset),
                LinkStorageView.Whole(target),
                CanMaterializeRoot: true,
                fieldPath);

        private static void AddDependency(
            ICollection<LinkOperation> operations,
            LinkStorageSymbol owner,
            int pointerOffset,
            AssetDependency? dependency)
        {
            if (dependency is { } value)
            {
                operations.Add(new ProviderLinkOperation(
                    new LinkStorageCell(owner, pointerOffset),
                    value));
            }
        }

        private static void ValidateMenu(MenuDefAsset definition)
        {
            ArgumentNullException.ThrowIfNull(definition.Window);
            ValidateWindow(definition.Window, "Menu.Window");
            if (definition.Fullscreen is not (0 or 1))
                throw new InvalidDataException("Menu.Fullscreen must be 0 or 1.");
            if (definition.ItemCount < 0 || definition.ItemCount != definition.Items.Count)
                throw new InvalidDataException("Menu.ItemCount must equal its nonnegative detached Item rows.");
            ValidateFixedCount(definition.CursorItems, 4, "Menu.CursorItems");
            ValidateFinite(definition.FadeClamp, "Menu.FadeClamp");
            ValidateFinite(definition.FadeAmount, "Menu.FadeAmount");
            ValidateFinite(definition.FadeInAmount, "Menu.FadeInAmount");
            ValidateFinite(definition.BlurRadius, "Menu.BlurRadius");
            if (definition.BlurRadius < 0)
                throw new InvalidDataException("Menu.BlurRadius cannot be negative.");
            ValidateVec4(definition.FocusColor, "Menu.FocusColor");
            ValidateTransitionGroup(definition.ScaleTransitions, "Menu.ScaleTransitions");
            ValidateTransitionGroup(definition.AlphaTransitions, "Menu.AlphaTransitions");
            ValidateTransitionGroup(definition.XTransitions, "Menu.XTransitions");
            ValidateTransitionGroup(definition.YTransitions, "Menu.YTransitions");
        }

        private static void ValidateItem(ItemDefAsset item, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(item.Window);
            ValidateWindow(item.Window, $"{fieldPath}.Window");
            ValidateFixedCount(item.TextRect, 4, $"{fieldPath}.TextRect");
            foreach (RectangleDef rectangle in item.TextRect)
                ValidateRectangle(rectangle, $"{fieldPath}.TextRect");
            if (!Enum.IsDefined(item.Type))
                throw new InvalidDataException($"{fieldPath}.Type is unsupported.");
            ValidateItemDataUnion(item, fieldPath);
            bool hasDetachedTypeData = item.EditField is not null ||
                item.ListBox is not null ||
                item.Multi is not null ||
                item.DvarEnumName is not null ||
                item.NewsTicker is not null ||
                item.TextScroll is not null;
            if (hasDetachedTypeData && item.DataType != (int)item.Type)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.DataType must match ItemDef type {item.Type} " +
                    "when its detached type-data payload is present.");
            }
            if (unchecked((uint)item.TextAlignMode) >= 16u ||
                (item.TextAlignMode & 3) == 3)
                throw new InvalidDataException($"{fieldPath}.TextAlignMode is invalid.");
            if (item.Type == ItemDefType.GameMessageWindow &&
                (item.GameMsgWindowIndex is < 0 or > 3 ||
                 item.GameMsgWindowMode is < 0 or > 3))
            {
                throw new InvalidDataException(
                    $"{fieldPath} game-message window index and mode must be in [0, 3].");
            }
            ValidateFixedCount(item.CursorPos, 4, $"{fieldPath}.CursorPos");
            if (item.FloatExpressionCount < 0 ||
                item.FloatExpressionCount != item.LoadedFloatExpressions.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.FloatExpressionCount must equal its nonnegative detached rows.");
            }
            ValidateFinite(item.TextAlignX, $"{fieldPath}.TextAlignX");
            ValidateFinite(item.TextAlignY, $"{fieldPath}.TextAlignY");
            ValidateFinite(item.TextScale, $"{fieldPath}.TextScale");
            ValidateFinite(item.Special, $"{fieldPath}.Special");
            ValidateVec4(item.GlowColor, $"{fieldPath}.GlowColor");
        }

        private static void ValidateItemDataUnion(
            ItemDefAsset item,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(item.TypeData);
            ArgumentNullException.ThrowIfNull(item.TypeData.Value);
            bool edit = item.TypeData.Value is EditFieldItemDefData;
            bool listBox = item.TypeData.Value is ListBoxItemDefData;
            bool multi = item.TypeData.Value is MultiItemDefData;
            bool dvarEnum = item.TypeData.Value is DvarEnumItemDefData;
            bool newsTicker = item.TypeData.Value is NewsTickerItemDefData;
            bool textScroll = item.TypeData.Value is TextScrollItemDefData;

            if ((!edit && item.EditField is not null) ||
                (!listBox && item.ListBox is not null) ||
                (!multi && item.Multi is not null) ||
                (!dvarEnum && item.DvarEnumName is not null) ||
                (!newsTicker && item.NewsTicker is not null) ||
                (!textScroll && item.TextScroll is not null))
            {
                throw new InvalidDataException(
                    $"{fieldPath}.TypeData retains a semantic child outside its active union arm.");
            }
        }

        private static void ValidateEventHandlerUnion(
            MenuEventHandler value,
            string fieldPath)
        {
            bool unconditional =
                value.EventData.Value is UnconditionalScriptEventData;
            bool conditional =
                value.EventData.Value is ConditionalScriptEventData;
            bool elseScript = value.EventData.Value is ElseScriptEventData;
            bool local = value.EventData.Value is SetLocalVarEventData;
            bool expected = value.EventType switch
            {
                MenuEventHandlerType.UnconditionalScript => unconditional,
                MenuEventHandlerType.ConditionalScript => conditional,
                MenuEventHandlerType.ElseScript => elseScript,
                MenuEventHandlerType.SetLocalVarBool or
                    MenuEventHandlerType.SetLocalVarInt or
                    MenuEventHandlerType.SetLocalVarFloat or
                    MenuEventHandlerType.SetLocalVarString => local,
                _ => false
            };
            if (!expected)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.EventData does not match EventType {value.EventType}.");
            }

            if ((value.EventType != MenuEventHandlerType.UnconditionalScript &&
                 value.UnconditionalScript is not null) ||
                (value.EventType != MenuEventHandlerType.ConditionalScript &&
                 value.ConditionalScript is not null) ||
                (value.EventType != MenuEventHandlerType.ElseScript &&
                 value.ElseScriptSet is not null) ||
                (value.EventType is not (MenuEventHandlerType.SetLocalVarBool or
                    MenuEventHandlerType.SetLocalVarInt or
                    MenuEventHandlerType.SetLocalVarFloat or
                    MenuEventHandlerType.SetLocalVarString) &&
                 value.SetLocalVarData is not null))
            {
                throw new InvalidDataException(
                    $"{fieldPath} retains a semantic child outside its active event union arm.");
            }
        }

        private static void ValidateOperandUnion(
            ExpressionEntry entry,
            string fieldPath)
        {
            bool valueMatches = entry.Operand.DataType switch
            {
                ExpDataType.VAL_INT => entry.Operand.Value is IntOperandValue,
                ExpDataType.VAL_FLOAT => entry.Operand.Value is FloatOperandValue,
                ExpDataType.VAL_STRING => entry.Operand.Value is StringOperandValue,
                ExpDataType.VAL_FUNCTION => entry.Operand.Value is FunctionOperandValue,
                _ => false
            };
            if (!valueMatches)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.Operand value does not match its data-type discriminator.");
            }
            if ((entry.Operand.DataType != ExpDataType.VAL_STRING &&
                 entry.StringValue is not null) ||
                (entry.Operand.DataType != ExpDataType.VAL_FUNCTION &&
                 entry.FunctionStatement is not null))
            {
                throw new InvalidDataException(
                    $"{fieldPath}.Operand retains a semantic child outside its active union arm.");
            }
        }

        private static void ValidateWindow(WindowDef window, string fieldPath)
        {
            ValidateRectangle(window.Rect, $"{fieldPath}.Rect");
            ValidateRectangle(window.RectClient, $"{fieldPath}.RectClient");
            ValidateFinite(window.BorderSize, $"{fieldPath}.BorderSize");
            ValidateFixedCount(window.DynamicFlags, 4,
                $"{fieldPath}.DynamicFlags");
            ValidateVec4(window.ForeColor, $"{fieldPath}.ForeColor");
            ValidateVec4(window.BackColor, $"{fieldPath}.BackColor");
            ValidateVec4(window.BorderColor, $"{fieldPath}.BorderColor");
            ValidateVec4(window.OutlineColor, $"{fieldPath}.OutlineColor");
            ValidateVec4(window.DisableColor, $"{fieldPath}.DisableColor");
        }

        private static void ValidateRectangle(RectangleDef value, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(value);
            ValidateFinite(value.X, $"{fieldPath}.X");
            ValidateFinite(value.Y, $"{fieldPath}.Y");
            ValidateFinite(value.W, $"{fieldPath}.W");
            ValidateFinite(value.H, $"{fieldPath}.H");
            if (!Enum.IsDefined(value.HorzAlign) || !Enum.IsDefined(value.VertAlign))
                throw new InvalidDataException($"{fieldPath} has an invalid alignment discriminator.");
        }

        private static void ValidateTransitionGroup(
            IReadOnlyList<MenuTransition> values,
            string fieldPath)
        {
            ValidateFixedCount(values, 4, fieldPath);
            for (int index = 0; index < values.Count; index++)
            {
                MenuTransition value = values[index] ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null.");
                if (!Enum.IsDefined(value.TransitionType) ||
                    !Enum.IsDefined(value.EndTriggerType))
                    throw new InvalidDataException($"{fieldPath}[{index}] has an invalid discriminator.");
                ValidateFinite(value.StartValue, $"{fieldPath}[{index}].StartValue");
                ValidateFinite(value.EndValue, $"{fieldPath}[{index}].EndValue");
                ValidateFinite(value.Time, $"{fieldPath}[{index}].Time");
            }
        }

        private static void ValidateVec4(Vec4 value, string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(value);
            ValidateFinite(value.A, $"{fieldPath}.A");
            ValidateFinite(value.R, $"{fieldPath}.R");
            ValidateFinite(value.G, $"{fieldPath}.G");
            ValidateFinite(value.B, $"{fieldPath}.B");
        }

        private static void ValidateFinite(float value, string fieldPath)
        {
            if (!float.IsFinite(value))
                throw new InvalidDataException($"{fieldPath} must be finite.");
        }

        private static void ValidateCount(int declared, int actual, string fieldPath)
        {
            if (declared < 0 || declared != actual)
                throw new InvalidDataException($"{fieldPath} count must equal its nonnegative detached rows.");
        }

        private static void ValidateFixedCount<T>(
            IReadOnlyList<T> values,
            int expected,
            string fieldPath)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count != expected)
                throw new InvalidDataException($"{fieldPath} requires exactly {expected} values.");
        }

        private static void RequireItemType(
            ItemDefType actual,
            bool matches,
            string fieldPath,
            string payloadName)
        {
            if (!matches)
                throw new InvalidDataException($"{fieldPath}.{payloadName} does not match ItemDef type {actual}.");
        }

        private static bool IsTypedItemData(ItemDefType value) =>
            IsEditFieldType(value) ||
            value is ItemDefType.ListBox or ItemDefType.Multi or
                ItemDefType.DvarEnum or ItemDefType.NewsTicker or
                ItemDefType.TextScroll;

        private static bool IsEditFieldType(ItemDefType value) =>
            value is ItemDefType.Text or ItemDefType.EditField or
                ItemDefType.NumericField or ItemDefType.Slider or
                ItemDefType.YesNo or ItemDefType.Bind or
                ItemDefType.Validation or ItemDefType.DecimalField or
                ItemDefType.UpDown or ItemDefType.EmailField or
                ItemDefType.PassWordField;

        private static void WriteWindow(
            LinkTemplateWriter writer,
            WindowDef value)
        {
            writer.Skip(sizeof(int));
            WriteRectangle(writer, value.Rect);
            WriteRectangle(writer, value.RectClient);
            writer.Skip(sizeof(int));
            writer.WriteInt32((int)value.Style);
            writer.WriteInt32((int)value.Border);
            writer.WriteInt32((int)value.OwnerDraw);
            writer.WriteInt32(value.OwnerDrawFlags);
            writer.WriteSingle(value.BorderSize);
            writer.WriteInt32((int)value.StaticFlags);
            WriteFlags(writer, value.DynamicFlags, 4,
                "Window.DynamicFlags");
            writer.Skip(sizeof(int)); // Runtime next-time cache.
            WriteVec4(writer, value.ForeColor);
            WriteVec4(writer, value.BackColor);
            WriteVec4(writer, value.BorderColor);
            WriteVec4(writer, value.OutlineColor);
            WriteVec4(writer, value.DisableColor);
            writer.Skip(sizeof(int));
        }

        private static void WriteRectangle(
            LinkTemplateWriter writer,
            RectangleDef value)
        {
            writer.WriteSingle(value.X);
            writer.WriteSingle(value.Y);
            writer.WriteSingle(value.W);
            writer.WriteSingle(value.H);
            writer.WriteByte((byte)value.HorzAlign);
            writer.WriteByte((byte)value.VertAlign);
            writer.WriteUInt16(value.Pad12);
        }

        private static void WriteRectangles(
            LinkTemplateWriter writer,
            IReadOnlyList<RectangleDef> values,
            int expected,
            string fieldPath)
        {
            ValidateFixedCount(values, expected, fieldPath);
            for (int index = 0; index < values.Count; index++)
            {
                RectangleDef value = values[index] ?? throw new InvalidDataException(
                    $"{fieldPath}[{index}] cannot be null.");
                WriteRectangle(writer, value);
            }
        }

        private static void WriteVec4(LinkTemplateWriter writer, Vec4 value)
        {
            writer.WriteSingle(value.A);
            writer.WriteSingle(value.R);
            writer.WriteSingle(value.G);
            writer.WriteSingle(value.B);
        }

        private static void WriteInts(
            LinkTemplateWriter writer,
            IReadOnlyList<int> values,
            int expected,
            string fieldPath)
        {
            ValidateFixedCount(values, expected, fieldPath);
            foreach (int value in values)
                writer.WriteInt32(value);
        }

        private static void WriteFlags(
            LinkTemplateWriter writer,
            IReadOnlyList<WindowDynamicFlags> values,
            int expected,
            string fieldPath)
        {
            ValidateFixedCount(values, expected, fieldPath);
            foreach (WindowDynamicFlags value in values)
                writer.WriteInt32((int)value);
        }

        private static void WriteTransitions(
            LinkTemplateWriter writer,
            IReadOnlyList<MenuTransition> values,
            string fieldPath)
        {
            ValidateTransitionGroup(values, fieldPath);
            foreach (MenuTransition value in values)
            {
                writer.WriteInt32((int)value.TransitionType);
                writer.WriteInt32(value.TargetField);
                writer.WriteInt32(value.StartTime);
                writer.WriteSingle(value.StartValue);
                writer.WriteSingle(value.EndValue);
                writer.WriteSingle(value.Time);
                writer.WriteInt32((int)value.EndTriggerType);
            }
        }

    }
}
