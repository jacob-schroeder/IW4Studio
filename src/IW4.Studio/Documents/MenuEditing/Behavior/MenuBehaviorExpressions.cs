using System.Collections.Immutable;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>
/// Stable location information supplied when behavior owns an expression
/// reference. Expression authoring is deliberately implemented behind this
/// boundary so event/key editing does not depend on its representation.
/// </summary>
public readonly record struct MenuBehaviorExpressionSite(
    MenuBehaviorExpressionSiteKind Kind,
    ItemFloatExpressionTarget? FloatTarget = null,
    MenuBehaviorLocalValueType? LocalValueType = null)
{
    public static MenuBehaviorExpressionSite Float(
        ItemFloatExpressionTarget target) =>
        new(MenuBehaviorExpressionSiteKind.ItemFloat, target);

    public static MenuBehaviorExpressionSite Local(
        MenuBehaviorLocalValueType valueType) =>
        new(
            MenuBehaviorExpressionSiteKind.SetLocalVariable,
            LocalValueType: valueType);
}

public enum MenuBehaviorExpressionSiteKind
{
    Conditional,
    SetLocalVariable,
    ItemVisible,
    ItemDisabled,
    ItemText,
    ItemMaterial,
    ItemFloat
}

/// <summary>
/// Adapter seam between immutable behavior bindings and expression ownership.
/// Behavior uses the shared semantic <see cref="BehaviorExpression"/> model;
/// this interface only owns native statement translation.
/// </summary>
public interface IMenuBehaviorExpressionCodec
{
    BehaviorExpression? Import(
        Statement? source,
        MenuBehaviorExpressionSite site);

    BehaviorExpressionSupport SupportFor(BehaviorExpression? value);

    ImmutableArray<BehaviorExpressionDiagnostic> ImportDiagnosticsFor(
        BehaviorExpression? value);

    Statement? Export(
        BehaviorExpression? value,
        Statement? existing,
        MenuBehaviorExpressionSite site,
        BehaviorExpressionSupport support);

    IReadOnlyList<MenuBehaviorValidationIssue> Validate(
        MenuBehaviorExpressionBinding binding,
        MenuBehaviorExpressionSite site,
        string path,
        MenuBehaviorValidationMode mode);
}

/// <summary>
/// Conservative default used while expression lowering has not been wired.
/// It exposes imported statements as a shared opaque expression node and
/// round-trips the binding's internal source statement unchanged.
/// </summary>
public sealed class ImportedMenuBehaviorExpressionCodec
    : IMenuBehaviorExpressionCodec
{
    public static ImportedMenuBehaviorExpressionCodec Instance { get; } = new();

    private ImportedMenuBehaviorExpressionCodec()
    {
    }

    public BehaviorExpression? Import(
        Statement? source,
        MenuBehaviorExpressionSite site) =>
        source is null
            ? null
            : new BehaviorOpaqueExpression("The loaded statement has not been lowered by an expression codec.");

    public BehaviorExpressionSupport SupportFor(BehaviorExpression? value) =>
        BehaviorExpressionSupport.Empty;

    public ImmutableArray<BehaviorExpressionDiagnostic> ImportDiagnosticsFor(
        BehaviorExpression? value) => [];

    public Statement? Export(
        BehaviorExpression? value,
        Statement? existing,
        MenuBehaviorExpressionSite site,
        BehaviorExpressionSupport support) =>
        value switch
        {
            null => null,
            BehaviorOpaqueExpression => existing,
            _ => throw new InvalidOperationException(
                $"Expression type '{value.GetType().Name}' requires its owning expression codec.")
        };

    public IReadOnlyList<MenuBehaviorValidationIssue> Validate(
        MenuBehaviorExpressionBinding binding,
        MenuBehaviorExpressionSite site,
        string path,
        MenuBehaviorValidationMode mode)
    {
        ArgumentNullException.ThrowIfNull(binding);
        BehaviorExpression? value = binding.Value;
        if (value is null)
            return [];

        if (value is BehaviorOpaqueExpression)
        {
            return
            [
                new MenuBehaviorValidationIssue(
                    path,
                    "The imported expression is opaque and cannot be safely authored without an expression codec.",
                    mode == MenuBehaviorValidationMode.Authored
                        ? MenuBehaviorValidationSeverity.Error
                        : MenuBehaviorValidationSeverity.Warning)
            ];
        }

        return
        [
            new MenuBehaviorValidationIssue(
                path,
                "The configured expression reference requires its owning expression codec.",
                MenuBehaviorValidationSeverity.Error)
        ];
    }
}
