using IW4.Assets.Assets.Menu;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// The single editable authority for one behavior expression. Formula text is
/// parsed into the Documents semantic tree; a failed parse never replaces the
/// last valid immutable binding.
/// </summary>
public sealed class BehaviorExpressionDraftViewModel : ObservableObject
{
    private readonly BehaviorExpressionSupportDraftViewModel _support;
    private readonly BehaviorExpressionSupport _bindingSupport;
    private readonly Action _changed;
    private MenuBehaviorExpressionBinding _binding;
    private string _formula;
    private IReadOnlyList<BehaviorExpressionDiagnostic> _diagnostics = [];
    private bool _hasFormulaEdit;
    private BehaviorExpressionOperationOption? _selectedOperation;
    private BehaviorExpressionOperationOption? _selectedStaticDvarOperation;
    private BehaviorStaticDvarOption? _selectedStaticDvar;
    private BehaviorReusableExpressionOption? _selectedReusableExpression;

    internal BehaviorExpressionDraftViewModel(
        MenuBehaviorExpressionBinding binding,
        BehaviorExpressionSupportDraftViewModel support,
        Action changed)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        ArgumentNullException.ThrowIfNull(support);
        _support = support;
        _bindingSupport = binding.Support.HasSourceTable
            ? binding.Support
            : support.Resolve(BehaviorExpressionSupport.Empty);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _formula = Format(
            binding.Value,
            out IReadOnlyList<BehaviorExpressionDiagnostic> formatDiagnostics);
        _diagnostics = binding.ImportDiagnostics
            .Concat(formatDiagnostics)
            .ToArray();
        OperationOptions = Array.AsReadOnly(
            BehaviorExpressionCatalog.Default.Operations
                .Where(operation =>
                    operation.Category ==
                    BehaviorExpressionOperationCategory.Function &&
                    !BehaviorExpressionCatalog.IsStaticDvar(
                        operation.Operation))
                .OrderByDescending(operation => operation.IsGuided)
                .ThenBy(
                    operation => operation.FormulaName,
                    StringComparer.Ordinal)
                .Select(operation => new BehaviorExpressionOperationOption(
                    operation,
                    FormulaTemplate(operation)))
                .ToArray());
        StaticDvarOperationOptions = Array.AsReadOnly(
            BehaviorExpressionCatalog.Default.Operations
                .Where(operation =>
                    operation.Category ==
                    BehaviorExpressionOperationCategory.Function &&
                    BehaviorExpressionCatalog.IsStaticDvar(
                        operation.Operation))
                .OrderByDescending(operation => operation.IsGuided)
                .ThenBy(
                    operation => operation.FormulaName,
                    StringComparer.Ordinal)
                .Select(operation => new BehaviorExpressionOperationOption(
                    operation,
                    FormulaTemplate(operation)))
                .ToArray());
        StaticDvars = _support.AppliesTo(_bindingSupport)
            ? _support.StaticDvars
            : Array.AsReadOnly(_bindingSupport.StaticDvars
                .Select(dvar => new BehaviorStaticDvarOption(dvar))
                .ToArray());
        ReusableExpressions = Array.AsReadOnly(_bindingSupport.ReusableExpressions
            .Select(reference => new BehaviorReusableExpressionOption(
                reference))
            .ToArray());
        _selectedOperation = OperationOptions.FirstOrDefault();
        _selectedStaticDvarOperation = StaticDvarOperationOptions
            .FirstOrDefault();
        _selectedStaticDvar = StaticDvars.FirstOrDefault();
        _selectedReusableExpression = ReusableExpressions.FirstOrDefault();
        BeginReplacementCommand = new ViewModelCommand(BeginReplacement);
        ClearCommand = new ViewModelCommand(Clear, () => HasExpression);
        UseOperationTemplateCommand = new ViewModelCommand(
            UseSelectedOperationTemplate,
            () => SelectedOperation is not null && IsFormulaEditable);
        UseStaticDvarTemplateCommand = new ViewModelCommand(
            UseSelectedStaticDvarTemplate,
            () => SelectedStaticDvarOperation is not null &&
                SelectedStaticDvar is not null &&
                IsFormulaEditable);
        UseReusableExpressionTemplateCommand = new ViewModelCommand(
            UseSelectedReusableExpressionTemplate,
            () => SelectedReusableExpression is not null &&
                IsFormulaEditable);
        _support.SupportChanged += OnSupportChanged;
    }

    public string Formula
    {
        get => _formula;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _formula, value))
                return;

            _hasFormulaEdit = true;
            Reparse();
            NotifyExpressionStateChanged();
            _changed();
        }
    }

    public bool HasExpression => _binding.Value is not null;

    /// <summary>
    /// Imported opaque nodes have no source representation that can safely be
    /// edited. The user can explicitly replace or remove them.
    /// </summary>
    public bool IsOpaqueImported => !_hasFormulaEdit &&
        _binding.Value is { } value &&
        BehaviorExpressionValidation.ContainsOpaque(value);

    public bool IsFormulaEditable => !IsOpaqueImported;

    public bool HasValidationError => _hasFormulaEdit && _diagnostics.Any(
        diagnostic => diagnostic.Severity ==
            BehaviorExpressionDiagnosticSeverity.Error);

    public string DiagnosticsText => string.Join(
        Environment.NewLine,
        _diagnostics.Select(diagnostic => diagnostic.Message));

    public bool HasDiagnostics => _diagnostics.Count != 0;

    public string ImportedStateText => IsOpaqueImported
        ? "Imported expression is unsupported. Replace or remove it to edit."
        : string.Empty;

    public string ResultKindText => _binding.Value is null
        ? "Not set"
        : ResultKind() == BehaviorExpressionResultKind.Unknown
            ? "Unverified"
            : ResultKind().ToString();

    public IReadOnlyList<BehaviorExpressionOperationOption> OperationOptions
        { get; }

    public IReadOnlyList<BehaviorExpressionOperationOption>
        StaticDvarOperationOptions { get; }

    public IReadOnlyList<BehaviorStaticDvarOption> StaticDvars { get; }

    public IReadOnlyList<BehaviorReusableExpressionOption>
        ReusableExpressions { get; }

    public bool HasStaticDvars => StaticDvars.Count != 0 &&
        StaticDvarOperationOptions.Count != 0;

    public bool HasReusableExpressions => ReusableExpressions.Count != 0;

    /// <summary>
    /// A formula template deliberately replaces the current formula. Inserting
    /// a function after an arbitrary expression would create invalid syntax.
    /// </summary>
    public string TemplateActionText => string.IsNullOrWhiteSpace(Formula)
        ? "Use formula"
        : "Replace formula";

    public string TemplateActionToolTip => string.IsNullOrWhiteSpace(Formula)
        ? "Use the selected expression template as the formula"
        : "Replace the current formula with the selected expression template";

    public BehaviorExpressionOperationOption? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (!SetProperty(ref _selectedOperation, value))
                return;

            UseOperationTemplateCommand.RaiseCanExecuteChanged();
        }
    }

    public BehaviorExpressionOperationOption? SelectedStaticDvarOperation
    {
        get => _selectedStaticDvarOperation;
        set
        {
            if (!SetProperty(ref _selectedStaticDvarOperation, value))
                return;

            UseStaticDvarTemplateCommand.RaiseCanExecuteChanged();
        }
    }

    public BehaviorStaticDvarOption? SelectedStaticDvar
    {
        get => _selectedStaticDvar;
        set
        {
            if (!SetProperty(ref _selectedStaticDvar, value))
                return;

            UseStaticDvarTemplateCommand.RaiseCanExecuteChanged();
        }
    }

    public BehaviorReusableExpressionOption? SelectedReusableExpression
    {
        get => _selectedReusableExpression;
        set
        {
            if (!SetProperty(ref _selectedReusableExpression, value))
                return;

            UseReusableExpressionTemplateCommand.RaiseCanExecuteChanged();
        }
    }

    public ViewModelCommand BeginReplacementCommand { get; }

    public ViewModelCommand ClearCommand { get; }

    public ViewModelCommand UseOperationTemplateCommand { get; }

    public ViewModelCommand UseStaticDvarTemplateCommand { get; }

    public ViewModelCommand UseReusableExpressionTemplateCommand { get; }

    public MenuBehaviorExpressionBinding ToBinding() => _binding;

    public void BeginReplacement()
    {
        if (!IsOpaqueImported)
            return;

        _binding = _binding with { Value = null, ImportDiagnostics = [] };
        _formula = string.Empty;
        _diagnostics = [];
        _hasFormulaEdit = true;
        OnPropertyChanged(nameof(Formula));
        NotifyExpressionStateChanged();
        ClearCommand.RaiseCanExecuteChanged();
        _changed();
    }

    public void Clear()
    {
        if (_binding.Value is null && string.IsNullOrEmpty(Formula))
            return;

        _binding = _binding with { Value = null, ImportDiagnostics = [] };
        _formula = string.Empty;
        _diagnostics = [];
        _hasFormulaEdit = true;
        OnPropertyChanged(nameof(Formula));
        NotifyExpressionStateChanged();
        ClearCommand.RaiseCanExecuteChanged();
        _changed();
    }

    private void UseSelectedOperationTemplate()
    {
        if (SelectedOperation is not { } selected || !IsFormulaEditable)
            return;

        Formula = selected.Template;
    }

    private void UseSelectedStaticDvarTemplate()
    {
        if (SelectedStaticDvarOperation is not { } operation ||
            SelectedStaticDvar is not { } dvar ||
            !IsFormulaEditable)
        {
            return;
        }

        Formula = FormulaTemplate(operation.Operation, dvar.Reference);
    }

    private void UseSelectedReusableExpressionTemplate()
    {
        if (SelectedReusableExpression is not { } reference ||
            !IsFormulaEditable)
        {
            return;
        }

        Formula = $"expressionRef({reference.Reference.Id.Index})";
    }

    internal IReadOnlyList<string> Validate(
        string label,
        bool required,
        BehaviorExpressionResultKind? expected = null)
    {
        var messages = new List<string>();
        if (HasValidationError)
        {
            messages.AddRange(_diagnostics
                .Where(diagnostic => diagnostic.Severity ==
                    BehaviorExpressionDiagnosticSeverity.Error)
                .Select(diagnostic => $"{label}: {diagnostic.Message}"));
            return messages;
        }

        if (_binding.Value is null)
        {
            if (required)
                messages.Add($"{label}: enter an expression.");
            return messages;
        }

        if (IsOpaqueImported)
            return messages;

        if (expected is not null && !MatchesExpectedKind(ResultKind(), expected.Value))
        {
            messages.Add(
                $"{label}: the expression result is {ResultKindText}, " +
                $"not {expected.Value}.");
        }

        return messages;
    }

    private void Reparse()
    {
        if (string.IsNullOrWhiteSpace(Formula))
        {
            _diagnostics =
            [
                new BehaviorExpressionDiagnostic(
                    BehaviorExpressionDiagnosticCode.EmptyExpression,
                    BehaviorExpressionDiagnosticSeverity.Error,
                    "Enter an expression.")
            ];
            return;
        }

        BehaviorExpressionResult<BehaviorExpression> parsed =
            BehaviorExpressionFormulaParser.Parse(Formula, CurrentSupport);
        var diagnostics = parsed.Diagnostics.ToList();
        if (parsed.Value is { } expression)
        {
            BehaviorExpressionValidation.Validate(
                expression,
                CurrentSupport,
                BehaviorExpressionCatalog.Default,
                diagnostics);
        }

        _diagnostics = diagnostics
            .DistinctBy(diagnostic =>
                (diagnostic.Code, diagnostic.Message, diagnostic.Position))
            .ToArray();
        if (!_diagnostics.Any(diagnostic => diagnostic.Severity ==
                BehaviorExpressionDiagnosticSeverity.Error) &&
            parsed.Value is { } validExpression)
        {
            _binding = _binding with
            {
                Value = validExpression,
                Support = CurrentSupport,
                ImportDiagnostics = []
            };
        }
    }

    private BehaviorExpressionSupport CurrentSupport => _support.Resolve(
        _bindingSupport);

    private void OnSupportChanged(object? sender, EventArgs e)
    {
        if (!_support.AppliesTo(_bindingSupport))
            return;

        OnPropertyChanged(nameof(HasStaticDvars));
        if (SelectedStaticDvar is null && StaticDvars.Count != 0)
            SelectedStaticDvar = StaticDvars[0];
        if (_hasFormulaEdit && !string.IsNullOrWhiteSpace(Formula))
        {
            // A user may deliberately type a future dvar name before adding
            // its support row. Re-evaluate that same formula against the
            // shared draft table without manufacturing a second edit.
            Reparse();
            NotifyExpressionStateChanged();
        }
        UseStaticDvarTemplateCommand.RaiseCanExecuteChanged();
    }

    private BehaviorExpressionResultKind ResultKind() => ResultKind(
        _binding.Value);

    private static BehaviorExpressionResultKind ResultKind(
        BehaviorExpression? expression) => expression switch
    {
        BehaviorIntegerExpression => BehaviorExpressionResultKind.Integer,
        BehaviorFloatExpression => BehaviorExpressionResultKind.Float,
        BehaviorStringExpression => BehaviorExpressionResultKind.String,
        BehaviorStaticDvarExpression value =>
            BehaviorExpressionCatalog.Default.Get(value.Operation).ResultKind,
        BehaviorUnaryExpression value =>
            BehaviorExpressionCatalog.Default.Get(value.Operation).ResultKind,
        BehaviorBinaryExpression value =>
            BehaviorExpressionCatalog.Default.Get(value.Operation).ResultKind,
        BehaviorCallExpression value =>
            BehaviorExpressionCatalog.Default.Get(value.Operation).ResultKind,
        _ => BehaviorExpressionResultKind.Unknown
    };

    private static bool MatchesExpectedKind(
        BehaviorExpressionResultKind actual,
        BehaviorExpressionResultKind expected) =>
        actual == BehaviorExpressionResultKind.Unknown ||
        actual == expected ||
        expected == BehaviorExpressionResultKind.Float &&
        actual is BehaviorExpressionResultKind.Integer or
            BehaviorExpressionResultKind.Number ||
        expected == BehaviorExpressionResultKind.Number &&
        actual is BehaviorExpressionResultKind.Integer or
            BehaviorExpressionResultKind.Float ||
        expected == BehaviorExpressionResultKind.Boolean &&
        actual == BehaviorExpressionResultKind.Integer;

    private static string Format(
        BehaviorExpression? expression,
        out IReadOnlyList<BehaviorExpressionDiagnostic> diagnostics)
    {
        if (expression is null)
        {
            diagnostics = [];
            return string.Empty;
        }

        BehaviorExpressionResult<string> formatted =
            BehaviorExpressionFormatter.Format(expression);
        diagnostics = formatted.Diagnostics;
        return formatted.Value ?? string.Empty;
    }

    private void NotifyExpressionStateChanged()
    {
        OnPropertyChanged(nameof(HasExpression));
        OnPropertyChanged(nameof(IsOpaqueImported));
        OnPropertyChanged(nameof(IsFormulaEditable));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(DiagnosticsText));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(ImportedStateText));
        OnPropertyChanged(nameof(ResultKindText));
        ClearCommand.RaiseCanExecuteChanged();
        UseOperationTemplateCommand.RaiseCanExecuteChanged();
        UseStaticDvarTemplateCommand.RaiseCanExecuteChanged();
        UseReusableExpressionTemplateCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TemplateActionText));
        OnPropertyChanged(nameof(TemplateActionToolTip));
    }

    private static string FormulaTemplate(
        BehaviorExpressionOperationMetadata operation)
    {
        int argumentCount = operation.HasVerifiedArity
            ? operation.AllowedArgumentCounts[0]
            : 0;
        return $"{operation.FormulaName}(" +
            string.Join(", ", Enumerable.Repeat("0", argumentCount)) +
            ")";
    }

    private static string FormulaTemplate(
        BehaviorExpressionOperationMetadata operation,
        BehaviorStaticDvarReference dvar)
    {
        string reference = !string.IsNullOrWhiteSpace(dvar.Name)
            ? Quote(dvar.Name)
            : dvar.Index.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        return $"{operation.FormulaName}({reference})";
    }

    private static string Quote(string value) =>
        '"' + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}

public sealed record BehaviorExpressionOperationOption(
    BehaviorExpressionOperationMetadata Operation,
    string Template)
{
    public string Label =>
        $"{Operation.FormulaName}({ArityText}) · " +
        $"{ResultText} · " +
        (Operation.IsGuided
            ? $"observed {Operation.ObservedCount:N0}"
            : "advanced / unverified");

    private string ArityText => Operation.HasVerifiedArity
        ? string.Join("/", Operation.AllowedArgumentCounts)
        : "0–10";

    private string ResultText =>
        Operation.ResultKind == BehaviorExpressionResultKind.Unknown
            ? "result unverified"
            : Operation.ResultKind.ToString();

    public override string ToString() => Label;
}

public sealed record BehaviorStaticDvarOption(
    BehaviorStaticDvarReference Reference)
{
    public string Label => string.IsNullOrWhiteSpace(Reference.Name)
        ? $"Static dvar #{Reference.Index}"
        : $"{Reference.Name} (#{Reference.Index})";

    public override string ToString() => Label;
}

public sealed record BehaviorReusableExpressionOption(
    BehaviorReusableExpressionReference Reference)
{
    public string Label => $"Expression reference #{Reference.Id.Index}";

    public override string ToString() => Label;
}
