using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Boundary facade between detached linker-native Menu assets and immutable
/// editor snapshots. Native lowering is delegated to the typed compilers.
/// </summary>
internal static class MenuAssetProjector
{
    public static MenuEditorSnapshot Project(
        MenuDefAsset definition,
        MenuDocumentIdentity identity) =>
        MenuSnapshotFactory.Create(definition, identity);

    public static MenuFileEditorSnapshot Project(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity) =>
        MenuFileSnapshotProjector.Create(definition, identity);

    public static MenuDefAsset Apply(
        MenuDefAsset definition,
        MenuDocumentIdentity identity,
        MenuEdit edit,
        out MenuDocumentIdentity nextIdentity)
    {
        MenuDocumentCompiler.MenuEditResult result =
            MenuDocumentCompiler.Apply(definition, identity, edit);
        nextIdentity = result.Identity;
        return result.Data;
    }

    public static MenuFileAsset Apply(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity,
        MenuFileEdit edit,
        out MenuFileDocumentIdentity nextIdentity)
    {
        MenuFileEditResultAsset result = MenuFileDocumentCompiler.Apply(
            definition,
            identity,
            edit);
        nextIdentity = result.Identity;
        return result.Definition;
    }

    public static MenuFileAsset Clone(MenuFileAsset definition) => new()
    {
        NamePointer = definition.NamePointer,
        Name = definition.Name,
        MenuCount = definition.MenuCount,
        MenusPointer = definition.MenusPointer,
        Menus = definition.Menus.Select(CloneRegistration).ToArray()
    };

    internal static MenuDefReference CloneRegistration(MenuDefReference reference)
    {
        MenuDefAsset? source = reference.Pointer.ConsumesSource
            ? reference.SourceMenu ?? reference.CanonicalMenu
            : reference.CanonicalMenu;
        return new MenuDefReference(
            reference.Index,
            reference.Pointer,
            source is null
                ? null
                : new MenuGraphClone(false).CloneMenu(source));
    }

    public static bool SemanticallyEquals(MenuDefAsset left, MenuDefAsset right)
    {
        MenuEditorSnapshot leftSnapshot = Project(left, MenuDocumentIdentity.Create(left));
        MenuEditorSnapshot rightSnapshot = Project(right, MenuDocumentIdentity.Create(right));
        return leftSnapshot.Settings == rightSnapshot.Settings &&
            leftSnapshot.Window.Value == rightSnapshot.Window.Value &&
            SameDefinitionBehavior(
                leftSnapshot.DefinitionBehavior,
                rightSnapshot.DefinitionBehavior) &&
            leftSnapshot.Items.Select(item => (item.Value, item.Behavior))
                .SequenceEqual(rightSnapshot.Items.Select(item =>
                    (item.Value, item.Behavior)));
    }

    private static bool SameDefinitionBehavior(
        MenuDefinitionBehaviorBindings left,
        MenuDefinitionBehaviorBindings right)
    {
        var visited = new HashSet<(
            MenuBehaviorEventHandlerSet Left,
            MenuBehaviorEventHandlerSet Right)>();
        return SameEventBinding(left.OnOpen, right.OnOpen, visited) &&
            SameEventBinding(
                left.OnCloseRequest,
                right.OnCloseRequest,
                visited) &&
            SameEventBinding(left.OnClose, right.OnClose, visited) &&
            SameEventBinding(left.OnEscape, right.OnEscape, visited) &&
            SameKeyHandlers(left.KeyHandlers, right.KeyHandlers, visited);
    }

    private static bool SameEventBinding(
        MenuBehaviorEventBinding left,
        MenuBehaviorEventBinding right,
        HashSet<(MenuBehaviorEventHandlerSet Left,
            MenuBehaviorEventHandlerSet Right)> visited) =>
        SameEventSet(left.Handlers, right.Handlers, visited);

    private static bool SameEventSet(
        MenuBehaviorEventHandlerSet? left,
        MenuBehaviorEventHandlerSet? right,
        HashSet<(MenuBehaviorEventHandlerSet Left,
            MenuBehaviorEventHandlerSet Right)> visited)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (!visited.Add((left, right)))
            return true;
        if (left.Handlers.Length != right.Handlers.Length)
            return false;

        for (int index = 0; index < left.Handlers.Length; index++)
        {
            MenuBehaviorEventHandlerEntry leftEntry = left.Handlers[index];
            MenuBehaviorEventHandlerEntry rightEntry = right.Handlers[index];
            if (leftEntry.Handler is null || rightEntry.Handler is null)
            {
                if (leftEntry.Handler is not null || rightEntry.Handler is not null ||
                    leftEntry.SourcePointer != rightEntry.SourcePointer)
                {
                    return false;
                }
                continue;
            }

            if (!SameEventHandler(
                    leftEntry.Handler,
                    rightEntry.Handler,
                    visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameEventHandler(
        MenuBehaviorEventHandler left,
        MenuBehaviorEventHandler right,
        HashSet<(MenuBehaviorEventHandlerSet Left,
            MenuBehaviorEventHandlerSet Right)> visited) =>
        (left, right) switch
        {
            (MenuBehaviorScriptEventHandler first,
             MenuBehaviorScriptEventHandler second) =>
                string.Equals(first.Script, second.Script, StringComparison.Ordinal),
            (MenuBehaviorConditionalEventHandler first,
             MenuBehaviorConditionalEventHandler second) =>
                SameExpression(first.Condition, second.Condition) &&
                SameEventSet(first.Then, second.Then, visited),
            (MenuBehaviorElseEventHandler first,
             MenuBehaviorElseEventHandler second) =>
                SameEventSet(first.Handlers, second.Handlers, visited),
            (MenuBehaviorSetLocalVariableEventHandler first,
             MenuBehaviorSetLocalVariableEventHandler second) =>
                first.ValueType == second.ValueType &&
                string.Equals(first.Name, second.Name, StringComparison.Ordinal) &&
                SameExpression(first.Expression, second.Expression),
            (MenuBehaviorOpaqueEventHandler first,
             MenuBehaviorOpaqueEventHandler second) =>
                first.EventType == second.EventType &&
                first.Raw.EventDataPointer == second.Raw.EventDataPointer &&
                first.Raw.Pad05 == second.Raw.Pad05 &&
                first.Raw.Pad06 == second.Raw.Pad06 &&
                first.Raw.Pad07 == second.Raw.Pad07,
            _ => false
        };

    private static bool SameKeyHandlers(
        MenuBehaviorKeyHandlerBindings left,
        MenuBehaviorKeyHandlerBindings right,
        HashSet<(MenuBehaviorEventHandlerSet Left,
            MenuBehaviorEventHandlerSet Right)> visited)
    {
        if (left.HasTruncatedImportedTail != right.HasTruncatedImportedTail ||
            left.Handlers.Length != right.Handlers.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Handlers.Length; index++)
        {
            MenuBehaviorKeyHandlerBinding first = left.Handlers[index];
            MenuBehaviorKeyHandlerBinding second = right.Handlers[index];
            if (first.Key != second.Key ||
                !SameEventSet(first.Action, second.Action, visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameExpression(
        MenuBehaviorExpressionBinding left,
        MenuBehaviorExpressionBinding right)
    {
        if (left.Value is BehaviorOpaqueExpression ||
            right.Value is BehaviorOpaqueExpression)
        {
            return left.Value is BehaviorOpaqueExpression first &&
                right.Value is BehaviorOpaqueExpression second &&
                string.Equals(first.Reason, second.Reason, StringComparison.Ordinal) &&
                SameOpaqueStatement(left.SourceStatement, right.SourceStatement);
        }

        return SameExpression(left.Value, right.Value);
    }

    private static bool SameExpression(
        BehaviorExpression? left,
        BehaviorExpression? right) => (left, right) switch
        {
            (null, null) => true,
            (BehaviorIntegerExpression first,
             BehaviorIntegerExpression second) => first.Value == second.Value,
            (BehaviorFloatExpression first,
             BehaviorFloatExpression second) =>
                BitConverter.SingleToInt32Bits(first.Value) ==
                BitConverter.SingleToInt32Bits(second.Value),
            (BehaviorStringExpression first,
             BehaviorStringExpression second) =>
                string.Equals(first.Value, second.Value, StringComparison.Ordinal),
            (BehaviorUnaryExpression first,
             BehaviorUnaryExpression second) =>
                first.Operation == second.Operation &&
                SameExpression(first.Operand, second.Operand),
            (BehaviorBinaryExpression first,
             BehaviorBinaryExpression second) =>
                first.Operation == second.Operation &&
                SameExpression(first.Left, second.Left) &&
                SameExpression(first.Right, second.Right),
            (BehaviorCallExpression first,
             BehaviorCallExpression second) =>
                first.Operation == second.Operation &&
                first.Arguments.Count == second.Arguments.Count &&
                first.Arguments.Zip(second.Arguments)
                    .All(pair => SameExpression(pair.First, pair.Second)),
            (BehaviorReusableExpressionReferenceExpression first,
             BehaviorReusableExpressionReferenceExpression second) =>
                first.ReferenceId == second.ReferenceId,
            (BehaviorStaticDvarExpression first,
             BehaviorStaticDvarExpression second) =>
                first.Operation == second.Operation &&
                first.Dvar.Index == second.Dvar.Index &&
                string.Equals(
                    first.Dvar.Name,
                    second.Dvar.Name,
                    StringComparison.Ordinal),
            _ => false
        };

    private static bool SameOpaqueStatement(Statement? left, Statement? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (not null, not null) => ReferenceEquals(
                MenuGraphClone.ProvenanceIdentity(left),
                MenuGraphClone.ProvenanceIdentity(right)),
            _ => false
        };

    public static bool SemanticallyEquals(MenuFileAsset left, MenuFileAsset right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.MenuCount == right.MenuCount &&
        left.Menus.Count == right.Menus.Count &&
        left.Menus.Zip(right.Menus).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.Pointer == pair.Second.Pointer &&
            (pair.First.CanonicalMenu, pair.Second.CanonicalMenu) switch
            {
                (null, null) => true,
                (MenuDefAsset first, MenuDefAsset second) =>
                    SemanticallyEquals(first, second),
                _ => false
            });

    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuEditorSnapshot snapshot) => MenuEditorValidation.Validate(snapshot);

    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuFileEditorSnapshot snapshot) => MenuEditorValidation.Validate(snapshot);
}
