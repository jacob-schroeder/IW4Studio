using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Documents.MenuEditing.Behavior;

/// <summary>
/// Connects the shared expression authoring subsystem to MenuDef and ItemDef
/// behavior. Imported roots are associated with their lossless Statement
/// wrapper by identity; a newly parsed root is lowered as a new copy-on-write
/// Statement.
/// </summary>
public sealed class MenuBehaviorExpressionCodec : IMenuBehaviorExpressionCodec
{
    // Snapshot import and document compilation deliberately use separate
    // codec instances. Keep source provenance keyed by the immutable root;
    // export then rebinds that source to the compiler's fresh graph clone.
    // Weak keys avoid extending the lifetime of snapshots or drafts.
    private static readonly ConditionalWeakTable<
        BehaviorExpression,
        BehaviorExpressionStatement> Imported = new();
    private readonly Dictionary<object, Statement> _currentStatements = [];
    private readonly Dictionary<object, ExpressionSupportingData>
        _currentSupportTables = [];
    private bool _hasCurrentBaseline;

    public MenuBehaviorExpressionCodec(ExpressionSupportingData? supportingData)
    {
        SupportingData = supportingData;
        Support = BehaviorExpressionSupport.Import(supportingData);
    }

    public BehaviorExpressionSupport Support { get; }

    internal ExpressionSupportingData? SupportingData { get; }

    /// <summary>
    /// Registers the compiler's fresh graph clone. Snapshot values point at
    /// the previous draft graph; provenance maps those references to the
    /// corresponding current Statement/support objects before export.
    /// </summary>
    internal void UseCurrentBaseline(MenuItemBehaviorBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginCurrentBaseline();

        Index(value.Expressions.Visible);
        Index(value.Expressions.Disabled);
        Index(value.Expressions.Text);
        Index(value.Expressions.Material);
        foreach (MenuBehaviorFloatExpressionBinding binding in
                 value.Expressions.FloatExpressions.Entries)
        {
            Index(binding.Expression);
        }

        var visited = new HashSet<MenuBehaviorEventHandlerSet>(
            ReferenceEqualityComparer.Instance);
        foreach (MenuBehaviorEventBinding binding in EventBindings(value))
            Index(binding.Handlers, visited);
        foreach (MenuBehaviorKeyHandlerBinding key in value.KeyHandlers.Handlers)
            Index(key.Action, visited);
        _hasCurrentBaseline = true;
    }

    internal void UseCurrentBaseline(MenuDefinitionBehaviorBindings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginCurrentBaseline();

        var visited = new HashSet<MenuBehaviorEventHandlerSet>(
            ReferenceEqualityComparer.Instance);
        foreach (MenuBehaviorEventBinding binding in EventBindings(value))
            Index(binding.Handlers, visited);
        foreach (MenuBehaviorKeyHandlerBinding key in value.KeyHandlers.Handlers)
            Index(key.Action, visited);
        _hasCurrentBaseline = true;
    }

    private void BeginCurrentBaseline()
    {
        _hasCurrentBaseline = false;
        _currentStatements.Clear();
        _currentSupportTables.Clear();
        if (SupportingData is not null)
            IndexSupport(SupportingData);
    }

    public BehaviorExpression? Import(
        Statement? source,
        MenuBehaviorExpressionSite site)
    {
        if (source is null)
            return null;

        BehaviorExpressionImportResult imported =
            BehaviorExpressionStatementCodec.Import(source, SupportingData);
        Imported.Add(imported.Statement.Expression, imported.Statement);
        return imported.Statement.Expression;
    }

    public BehaviorExpressionSupport SupportFor(BehaviorExpression? value) =>
        value is not null &&
        Imported.TryGetValue(value, out BehaviorExpressionStatement? imported)
            ? imported.Support
            : Support;

    public ImmutableArray<BehaviorExpressionDiagnostic> ImportDiagnosticsFor(
        BehaviorExpression? value) =>
        value is not null &&
        Imported.TryGetValue(value, out BehaviorExpressionStatement? imported)
            ? imported.Diagnostics.ToImmutableArray()
            : [];

    public Statement? Export(
        BehaviorExpression? value,
        Statement? existing,
        MenuBehaviorExpressionSite site,
        BehaviorExpressionSupport support)
    {
        if (value is null)
            return null;

        if (TryGetReusableImportedStatement(
                value,
                existing,
                out BehaviorExpressionStatement? imported))
        {
            Statement importedSource = imported.Source!;
            if (!_hasCurrentBaseline)
                return existing;
            if (_currentStatements.TryGetValue(
                    MenuGraphClone.ProvenanceIdentity(importedSource),
                    out Statement? current))
            {
                return current;
            }

            throw new InvalidDataException(
                "The imported expression could not be rebound to the " +
                "current Menu graph clone.");
        }

        BehaviorExpressionSupport effectiveSupport = CurrentSupport(support);
        var statement = new BehaviorExpressionStatement(value, effectiveSupport);
        BehaviorExpressionResult<Statement> lowered =
            BehaviorExpressionStatementCodec.Lower(statement);
        if (lowered.HasErrors || lowered.Value is null)
        {
            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                lowered.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }

        return lowered.Value;
    }

    private BehaviorExpressionSupport CurrentSupport(
        BehaviorExpressionSupport support)
    {
        if (!_hasCurrentBaseline)
            return support.HasSourceTable ? support : Support;
        if (support.Source is null)
            return Support;
        if (_currentSupportTables.TryGetValue(
                MenuGraphClone.ProvenanceIdentity(support.Source),
                out ExpressionSupportingData? current))
        {
            return BehaviorExpressionSupport.Import(current);
        }

        throw new InvalidDataException(
            "The expression support table could not be rebound to the " +
            "current Menu graph clone.");
    }

    private void Index(MenuBehaviorExpressionBinding binding)
    {
        if (binding.SourceStatement is { } statement)
        {
            _currentStatements.TryAdd(
                MenuGraphClone.ProvenanceIdentity(statement),
                statement);
        }
        if (binding.Support.Source is { } support)
            IndexSupport(support);
    }

    private void IndexSupport(ExpressionSupportingData support) =>
        _currentSupportTables.TryAdd(
            MenuGraphClone.ProvenanceIdentity(support),
            support);

    private void Index(
        MenuBehaviorEventHandlerSet? set,
        HashSet<MenuBehaviorEventHandlerSet> visited)
    {
        if (set is null || !visited.Add(set))
            return;

        foreach (MenuBehaviorEventHandlerEntry entry in set.Handlers)
        {
            switch (entry.Handler)
            {
                case MenuBehaviorConditionalEventHandler conditional:
                    Index(conditional.Condition);
                    Index(conditional.Then, visited);
                    break;
                case MenuBehaviorElseEventHandler otherwise:
                    Index(otherwise.Handlers, visited);
                    break;
                case MenuBehaviorSetLocalVariableEventHandler local:
                    Index(local.Expression);
                    break;
            }
        }
    }

    private static IEnumerable<MenuBehaviorEventBinding> EventBindings(
        MenuItemBehaviorBindings value)
    {
        yield return value.MouseEnterText;
        yield return value.MouseExitText;
        yield return value.MouseEnter;
        yield return value.MouseExit;
        yield return value.Action;
        yield return value.Accept;
        yield return value.OnFocus;
        yield return value.LeaveFocus;
        yield return value.ListBoxDoubleClick;
    }

    private static IEnumerable<MenuBehaviorEventBinding> EventBindings(
        MenuDefinitionBehaviorBindings value)
    {
        yield return value.OnOpen;
        yield return value.OnCloseRequest;
        yield return value.OnClose;
        yield return value.OnEscape;
    }

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

        var diagnostics = new List<BehaviorExpressionDiagnostic>();
        BehaviorExpressionSupport effectiveSupport = binding.Support.Source is not null
            ? binding.Support
            : Support;
        bool isImported = Imported.TryGetValue(
            value,
            out BehaviorExpressionStatement? importedStatement);
        if (isImported && !HasSameSupportTable(
                importedStatement!.Support,
                effectiveSupport))
        {
            diagnostics.Add(new(
                BehaviorExpressionDiagnosticCode.InvalidStatementShape,
                BehaviorExpressionDiagnosticSeverity.Error,
                "The imported expression belongs to a different support table. " +
                "Recreate it for this binding before moving it."));
        }
        BehaviorExpressionValidation.Validate(
            value,
            effectiveSupport,
            BehaviorExpressionCatalog.Default,
            diagnostics);

        bool retainsReusableSource = TryGetReusableImportedStatement(
            value,
            binding.SourceStatement,
            out _);
        var issues = diagnostics.Select(diagnostic =>
            new MenuBehaviorValidationIssue(
                path,
                diagnostic.Message,
                retainsReusableSource ||
                diagnostic.Severity != BehaviorExpressionDiagnosticSeverity.Error
                    ? MenuBehaviorValidationSeverity.Warning
                    : MenuBehaviorValidationSeverity.Error))
            .ToList();

        BehaviorExpressionResultKind actual = ResultKind(value);
        BehaviorExpressionResultKind expected = ExpectedKind(site);
        if (!IsCompatible(actual, expected))
        {
            issues.Add(new MenuBehaviorValidationIssue(
                path,
                $"The expression returns {Display(actual)}, but this binding expects {Display(expected)}.",
                mode == MenuBehaviorValidationMode.Authored &&
                !retainsReusableSource
                    ? MenuBehaviorValidationSeverity.Error
                    : MenuBehaviorValidationSeverity.Warning));
        }

        return issues.AsReadOnly();
    }

    private static bool HasSameSupportTable(
        BehaviorExpressionSupport left,
        BehaviorExpressionSupport right)
    {
        if (left.Source is null || right.Source is null)
            return left.Source is null && right.Source is null;

        return ReferenceEquals(
            MenuGraphClone.ProvenanceIdentity(left.Source),
            MenuGraphClone.ProvenanceIdentity(right.Source));
    }

    /// <summary>
    /// An imported semantic root is only safe to preserve when it remains at
    /// the binding that supplied its source Statement. Comparing clone
    /// provenance avoids treating a copied/moved root as unchanged.
    /// </summary>
    private static bool TryGetReusableImportedStatement(
        BehaviorExpression value,
        Statement? source,
        out BehaviorExpressionStatement imported)
    {
        if (source is not null &&
            Imported.TryGetValue(value, out imported!) &&
            imported.CanReuseSource &&
            imported.Source is { } importedSource &&
            ReferenceEquals(
                MenuGraphClone.ProvenanceIdentity(importedSource),
                MenuGraphClone.ProvenanceIdentity(source)))
        {
            return true;
        }

        imported = null!;
        return false;
    }

    private static BehaviorExpressionResultKind ExpectedKind(
        MenuBehaviorExpressionSite site) => site.Kind switch
        {
            MenuBehaviorExpressionSiteKind.Conditional or
            MenuBehaviorExpressionSiteKind.ItemVisible or
            MenuBehaviorExpressionSiteKind.ItemDisabled =>
                BehaviorExpressionResultKind.Boolean,
            MenuBehaviorExpressionSiteKind.ItemText or
            MenuBehaviorExpressionSiteKind.ItemMaterial =>
                BehaviorExpressionResultKind.String,
            MenuBehaviorExpressionSiteKind.ItemFloat =>
                BehaviorExpressionResultKind.Number,
            MenuBehaviorExpressionSiteKind.SetLocalVariable =>
                site.LocalValueType switch
                {
                    MenuBehaviorLocalValueType.Boolean =>
                        BehaviorExpressionResultKind.Boolean,
                    MenuBehaviorLocalValueType.Integer =>
                        BehaviorExpressionResultKind.Integer,
                    MenuBehaviorLocalValueType.Float =>
                        BehaviorExpressionResultKind.Number,
                    MenuBehaviorLocalValueType.String =>
                        BehaviorExpressionResultKind.String,
                    _ => BehaviorExpressionResultKind.Unknown
                },
            _ => BehaviorExpressionResultKind.Unknown
        };

    private static BehaviorExpressionResultKind ResultKind(
        BehaviorExpression expression) => expression switch
        {
            BehaviorIntegerExpression => BehaviorExpressionResultKind.Integer,
            BehaviorFloatExpression => BehaviorExpressionResultKind.Float,
            BehaviorStringExpression => BehaviorExpressionResultKind.String,
            BehaviorUnaryExpression unary when unary.Operation == OperationEnum.OP_NOT =>
                BehaviorExpressionResultKind.Boolean,
            BehaviorUnaryExpression => BehaviorExpressionResultKind.Number,
            BehaviorBinaryExpression binary =>
                BehaviorExpressionCatalog.Default.Get(binary.Operation).ResultKind,
            BehaviorCallExpression call =>
                BehaviorExpressionCatalog.Default.Get(call.Operation).ResultKind,
            BehaviorStaticDvarExpression dvar =>
                BehaviorExpressionCatalog.Default.Get(dvar.Operation).ResultKind,
            _ => BehaviorExpressionResultKind.Unknown
        };

    private static bool IsCompatible(
        BehaviorExpressionResultKind actual,
        BehaviorExpressionResultKind expected) =>
        actual == BehaviorExpressionResultKind.Unknown ||
        expected == BehaviorExpressionResultKind.Unknown ||
        actual == expected ||
        expected == BehaviorExpressionResultKind.Number && actual is
            BehaviorExpressionResultKind.Integer or
            BehaviorExpressionResultKind.Float or
            BehaviorExpressionResultKind.Number ||
        expected == BehaviorExpressionResultKind.Boolean && actual is
            BehaviorExpressionResultKind.Boolean or
            BehaviorExpressionResultKind.Integer;

    private static string Display(BehaviorExpressionResultKind value) =>
        value.ToString().ToLowerInvariant();
}
