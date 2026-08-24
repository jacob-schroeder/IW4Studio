using IW4.AssetExchange.RawFile;
using IW4.FastFiles.Zone;
using IW4.Gsc.Analysis;
using IW4.Gsc.BuiltIns;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;
using System.Text;
using Avalonia.Media.Imaging;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.RawFile;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Gsc;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>Presentation only; changing it never mutates the RawFile draft.</summary>
public enum RawFileDisplayEncoding
{
    Text,
    Hex
}

/// <summary>
/// Desktop façade over one RawFile editor session. Import/export here means
/// moving logical text or bytes to/from the detached session draft; it
/// deliberately performs no filesystem write or runtime mutation.
/// </summary>
public sealed class RawFileEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState,
      IAssetEditorSourceDiagnostics,
      IAssetEditorSourceDiagnosticsPresentation,
      IDisposable
{
    private static readonly TimeSpan LiveGscAnalysisDelay =
        TimeSpan.FromMilliseconds(300);

    private readonly AssetEditorSession _editorSession;
    private readonly IGscAnalyzer _gscAnalyzer;
    private readonly GscEditorLanguageSession? _gscLanguageSession;
    private readonly GscEditorLanguageSession? _gscAnalysisLanguageSession;
    private readonly IGscSourceNavigator? _gscSourceNavigator;
    private readonly IGscUsagesPresenter? _gscUsagesPresenter;
    private RawFileDraft? _draft;
    private RawFileReadOnlySnapshot? _readOnlySnapshot;
    private RawFileContentClassification? _contentClassification;
    private RawFileDisplayEncoding _displayEncoding;
    private string _payloadInput = string.Empty;
    private string _draftPayloadPresentation = string.Empty;
    private string _exportedPayload = string.Empty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private IReadOnlyList<EditorSourceDiagnostic> _sourceDiagnostics = [];
    private CancellationTokenSource? _gscAnalysisCancellation;
    private CancellationTokenSource? _gscUsageSearchCancellation;
    private long _bufferVersion;
    private bool _isAnalyzingGsc;
    private bool _isApplyingPayload;
    private string _gscAnalysisStatusMessage = string.Empty;
    private Bitmap? _nativePreview;
    private RawFileNativeViewerKind? _nativeViewerKind;
    private bool _disposed;

    public event EventHandler? SourceDiagnosticsPresentationRequested;

    public RawFileEditorViewModel(
        AssetEditorSession editorSession,
        IGscAnalyzer gscAnalyzer,
        GscWorkspaceIndexService? gscWorkspace = null,
        IGscSourceNavigator? gscSourceNavigator = null,
        IGscUsagesPresenter? gscUsagesPresenter = null)
    {
        _editorSession = editorSession ?? throw new ArgumentNullException(nameof(editorSession));
        _gscAnalyzer = gscAnalyzer ?? throw new ArgumentNullException(nameof(gscAnalyzer));
        _gscLanguageSession = gscWorkspace is null
            ? null
            : new GscEditorLanguageSession(gscWorkspace);
        // Workspace overlay construction holds a session-local lock. Keeping
        // analysis separate prevents worker checks from delaying UI queries.
        _gscAnalysisLanguageSession = gscWorkspace is null
            ? null
            : new GscEditorLanguageSession(gscWorkspace);
        _gscSourceNavigator = gscSourceNavigator;
        _gscUsagesPresenter = gscUsagesPresenter;
        if (editorSession.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.RawFile)
            throw new InvalidDataException("The RawFile view model can host only RawFile editor sessions.");

        switch (editorSession.Mode)
        {
            case WorkspaceAssetAccess.Editable:
                _draft = editorSession.OpenDraft<RawFileDraft>();
                _diagnostics = editorSession.Validation.Issues;
                _statusMessage = "Detached target-owned draft.";
                break;

            case WorkspaceAssetAccess.ReadOnly:
                try
                {
                    _readOnlySnapshot = RawFileReadOnlySnapshot.CaptureResolvedProvider(editorSession);
                    _statusMessage = "Detached read-only copy of the catalog-resolved provider.";
                }
                catch (InvalidDataException exception)
                {
                    _statusMessage = exception.Message;
                    _diagnostics = [new AssetValidationIssue(
                        "provider",
                        exception.Message,
                        AssetValidationSeverity.Error)];
                }
                break;

            case WorkspaceAssetAccess.ContentUnavailable:
                _statusMessage = "RawFile content is unavailable because this reference has no resolved provider.";
                break;

            default:
                throw new InvalidDataException($"Unknown RawFile editor mode '{editorSession.Mode}'.");
        }

        _contentClassification = ReadContentClassification();
        _displayEncoding = _contentClassification?.IsTextual == true
            ? RawFileDisplayEncoding.Text
            : RawFileDisplayEncoding.Hex;
        RefreshPresentation();
    }

    public WorkspaceAssetAccess Mode => _editorSession.Mode;

    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;

    public bool IsReadOnly => Mode == WorkspaceAssetAccess.ReadOnly;

    public bool IsContentUnavailable => Mode == WorkspaceAssetAccess.ContentUnavailable;

    public bool IsInputReadOnly => !IsEditable;

    public bool CanImport => IsEditable && _draft is not null;

    public bool HasPendingPayloadChanges =>
        CanImport &&
        !string.Equals(
            PayloadInput,
            _draftPayloadPresentation,
            StringComparison.Ordinal);

    public bool HasUnappliedChanges => HasPendingPayloadChanges;

    public bool CanApply => HasPendingPayloadChanges && !IsApplyingPayload;

    public bool CanClearBuffer => CanImport && _draft!.Mode != RawFilePayloadMode.CompressedPayload;

    public bool CanRevert => IsEditable;

    public bool IsGscSource =>
        _contentClassification?.IsTextual == true &&
        Path.GetExtension(OriginalName) is string extension &&
        (extension.Equals(".gsc", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".csc", StringComparison.OrdinalIgnoreCase));

    public bool HasGscWorkspaceFeatures =>
        IsGscSource &&
        _gscLanguageSession is not null &&
        _gscSourceNavigator is not null &&
        _gscUsagesPresenter is not null;

    public bool CanAnalyzeGsc =>
        IsGscSource &&
        DisplayEncoding == RawFileDisplayEncoding.Text &&
        !IsAnalyzingGsc;

    /// <summary>
    /// File replacement is intentionally offered only for RawFiles whose
    /// logical content has been classified as binary.
    /// </summary>
    public bool CanReplaceFromFile =>
        CanImport && ContentKind == RawFileContentKind.Binary;

    public bool IsCompressedPayload =>
        PayloadMode == RawFilePayloadMode.CompressedPayload;

    public string OriginalName => _draft?.OriginalName ?? _readOnlySnapshot?.OriginalName ?? _editorSession.Entry.OriginalName ?? string.Empty;

    public string ModeText => Mode switch
    {
        WorkspaceAssetAccess.Editable => "EDITABLE TARGET DEFINITION",
        WorkspaceAssetAccess.ReadOnly => "READ-ONLY RESOLVED PROVIDER",
        WorkspaceAssetAccess.ContentUnavailable => "CONTENT UNAVAILABLE",
        _ => throw new InvalidDataException($"Unknown RawFile editor mode '{Mode}'.")
    };

    public RawFilePayloadMode? PayloadMode => _draft?.Mode ?? _readOnlySnapshot?.Mode;

    public string PayloadModeText => PayloadMode switch
    {
        RawFilePayloadMode.UncompressedText => "UNCOMPRESSED TEXT (+ terminal null)",
        RawFilePayloadMode.UncompressedBinary => "UNCOMPRESSED BINARY (+ terminal null)",
        RawFilePayloadMode.CompressedPayload => "ZLIB-COMPRESSED",
        null => "NO READABLE PAYLOAD",
        _ => "INVALID PAYLOAD MODE"
    };

    public RawFileContentKind? ContentKind => _contentClassification?.Kind;

    public string ContentKindText => _contentClassification switch
    {
        { Kind: RawFileContentKind.Binary } => "BINARY",
        { Kind: RawFileContentKind.Textual, TextEncoding: { } encoding } =>
            $"TEXT ({RawFileContentClassifier.GetDisplayName(encoding)})",
        { Kind: RawFileContentKind.Textual } => "TEXT",
        null => "UNKNOWN",
        _ => "INVALID"
    };

    public string PropertySectionName => "RawFile";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Type", ContentTypePropertyText),
        new("Encoding", TextEncodingPropertyText),
        new("Storage", StoragePropertyText),
        new("Logical size", $"{UncompressedLength:N0} bytes"),
        new("Stored size", StoredSizePropertyText)
    ];

    public bool HasBuffer => _draft?.HasBuffer ?? _readOnlySnapshot?.HasBuffer ?? false;

    public int CompressedLength => _draft?.CompressedLength ?? _readOnlySnapshot?.CompressedLength ?? 0;

    public int UncompressedLength => _draft?.UncompressedLength ?? _readOnlySnapshot?.UncompressedLength ?? 0;

    public string SerializedLengthText =>
        $"compressedLen: {CompressedLength}   len: {UncompressedLength}";

    public Bitmap? NativePreview => _nativePreview;

    public bool HasNativePreview => NativePreview is not null;

    public string NativePreviewTitle => _nativeViewerKind switch
    {
        RawFileNativeViewerKind.Png => "PNG PREVIEW",
        _ => "NATIVE PREVIEW"
    };

    public IReadOnlyList<RawFileDisplayEncoding> DisplayEncodings { get; } =
        Array.AsReadOnly([RawFileDisplayEncoding.Text, RawFileDisplayEncoding.Hex]);

    public RawFileDisplayEncoding DisplayEncoding
    {
        get => _displayEncoding;
        set
        {
            if (value == RawFileDisplayEncoding.Text &&
                _contentClassification?.IsTextual != true)
            {
                value = RawFileDisplayEncoding.Hex;
            }

            if (!SetProperty(ref _displayEncoding, value))
                return;

            // Display conversion reads a copied payload and never calls Apply.
            RefreshPresentation();
            OnPropertyChanged(nameof(CanAnalyzeGsc));
        }
    }

    /// <summary>
    /// Staged text/hex input. It is not a semantic edit until
    /// <see cref="ImportPayloadAsync"/> is explicitly invoked.
    /// </summary>
    public string PayloadInput
    {
        get => _payloadInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _payloadInput, value))
                return;

            NotifyStagingStateChanged();
            InvalidateGscBufferState();
        }
    }

    public string ExportedPayload
    {
        get => _exportedPayload;
        private set
        {
            if (!SetProperty(ref _exportedPayload, value))
                return;

            OnPropertyChanged(nameof(HasExportedPayload));
        }
    }

    public bool HasExportedPayload => ExportedPayload.Length != 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<AssetValidationIssue> Diagnostics
    {
        get => _diagnostics;
        private set
        {
            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));
        }
    }

    public bool HasDiagnostics => Diagnostics.Count != 0;

    public string DiagnosticsSummary => string.Join(
        Environment.NewLine,
        Diagnostics.Select(issue => $"{issue.Severity}: {issue.FieldPath} — {issue.Message}"));

    public IReadOnlyList<EditorSourceDiagnostic> SourceDiagnostics
    {
        get => _sourceDiagnostics;
        private set
        {
            if (_sourceDiagnostics.SequenceEqual(value))
                return;

            _sourceDiagnostics = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalyzingGsc
    {
        get => _isAnalyzingGsc;
        private set
        {
            if (!SetProperty(ref _isAnalyzingGsc, value))
                return;

            OnPropertyChanged(nameof(CanAnalyzeGsc));
        }
    }

    public bool IsApplyingPayload
    {
        get => _isApplyingPayload;
        private set
        {
            if (!SetProperty(ref _isApplyingPayload, value))
                return;

            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string GscAnalysisStatusMessage
    {
        get => _gscAnalysisStatusMessage;
        private set => SetProperty(ref _gscAnalysisStatusMessage, value);
    }

    /// <summary>
    /// Runs an immediate workspace-aware analysis of the current presentation
    /// buffer. This method never applies the buffer to the detached RawFile
    /// draft.
    /// </summary>
    public async Task AnalyzeGscAsync()
    {
        if (_disposed ||
            !IsGscSource ||
            DisplayEncoding != RawFileDisplayEncoding.Text)
        {
            return;
        }

        GscAnalysisOperation operation = BeginGscAnalysis();
        GscAnalysisOutcome? outcome = await RunGscAnalysisAsync(
            operation,
            useWorkspace: true,
            delay: TimeSpan.Zero,
            activeStatusMessage: "Analyzing GSC…");
        RequestSourceDiagnosticsPresentation(outcome);
    }

    public void GoToGscDefinition(int sourceOffset)
    {
        if (!TryGetDefinitionTargets(
                sourceOffset,
                out GscSymbolDefinition[] definitions,
                out Iw4GscBuiltInDefinition[] builtIns))
        {
            return;
        }

        if (definitions.Length == 0 && builtIns.Length == 0)
        {
            StatusMessage = "No GSC definition was found at the caret.";
            return;
        }

        if (definitions.Length != 0)
        {
            _gscSourceNavigator!.NavigateTo(definitions[0].Location);
            StatusMessage = definitions.Length == 1
                ? $"Navigated to '{definitions[0].SourceName}'."
                : $"Navigated to the first of {definitions.Length:N0} " +
                  "matching definitions.";
            return;
        }

        _gscSourceNavigator!.NavigateTo(builtIns[0]);
        StatusMessage = builtIns.Length == 1
            ? $"Opened engine definition for '{builtIns[0].Name}'."
            : $"Opened the first of {builtIns.Length:N0} engine registrations " +
              $"for '{builtIns[0].Name}'.";
    }

    public async Task FindGscUsagesAsync(int sourceOffset)
    {
        if (_disposed ||
            !HasGscWorkspaceFeatures ||
            sourceOffset < 0 ||
            sourceOffset > PayloadInput.Length)
        {
            return;
        }

        CancelGscUsageSearch();
        var cancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellation.Token;
        _gscUsageSearchCancellation = cancellation;
        long version = _bufferVersion;
        string assetName = OriginalName;
        string source = PayloadInput;
        RawFileTextEncoding? sourceEncoding =
            _contentClassification?.TextEncoding;

        StatusMessage = "Finding GSC references…";

        try
        {
            GscUsagePresentation? presentation = await Task.Run(
                () => FindGscUsagesSnapshot(
                    assetName,
                    source,
                    sourceEncoding,
                    version,
                    sourceOffset,
                    cancellationToken),
                cancellationToken);

            if (_disposed ||
                version != _bufferVersion ||
                !ReferenceEquals(_gscUsageSearchCancellation, cancellation))
            {
                return;
            }

            if (presentation is null)
            {
                StatusMessage = "No GSC symbol was found at the caret.";
                return;
            }

            _gscUsagesPresenter!.Present(presentation);
            StatusMessage = presentation.Items.Count == 1
                ? $"Found 1 reference to '{presentation.SymbolName}'."
                : $"Found {presentation.Items.Count:N0} references to '{presentation.SymbolName}'.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer buffer or search owns the visible references state.
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException)
        {
            if (!_disposed &&
                version == _bufferVersion &&
                ReferenceEquals(_gscUsageSearchCancellation, cancellation))
            {
                StatusMessage = $"GSC reference search failed: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_gscUsageSearchCancellation, cancellation))
                _gscUsageSearchCancellation = null;

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Queries context-appropriate GSC completions for an explicit request.
    /// All syntax and workspace work runs outside the UI thread.
    /// </summary>
    public Task<IReadOnlyList<GscEditorCompletion>>
        GetGscCompletionsAsync(
            int caretOffset,
            CancellationToken cancellationToken = default) =>
        GetGscCompletionsAsync(
            caretOffset,
            requireAutomaticContext: false,
            cancellationToken);

    /// <summary>
    /// Queries completions only when the captured caret is in a valid
    /// automatic-completion context.
    /// </summary>
    public Task<IReadOnlyList<GscEditorCompletion>>
        GetAutomaticGscCompletionsAsync(
            int caretOffset,
            CancellationToken cancellationToken = default) =>
        GetGscCompletionsAsync(
            caretOffset,
            requireAutomaticContext: true,
            cancellationToken);

    public async Task<GscEditorSignatureHelp?> GetGscSignatureHelpAsync(
        int caretOffset,
        CancellationToken cancellationToken = default)
    {
        GscEditorQuerySnapshot? query = CaptureGscEditorQuery(caretOffset);
        if (query is null)
            return null;

        try
        {
            GscEditorSignatureHelp? help = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return _gscLanguageSession!.GetSignatureHelp(
                        query.AssetName,
                        CreateGscSourceText(query.Source, query.Encoding),
                        query.BufferVersion,
                        query.CaretOffset,
                        cancellationToken);
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return OwnsGscEditorQuery(query) ? help : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException)
        {
            if (OwnsGscEditorQuery(query))
                StatusMessage = $"GSC signature help failed: {exception.Message}";
            return null;
        }
    }

    public async Task ImportPayloadAsync()
    {
        if (IsApplyingPayload)
            return;

        if (!CanImport || _draft is null)
        {
            StatusMessage = "This RawFile is read-only or content is unavailable.";
            return;
        }
        if (!HasPendingPayloadChanges)
        {
            StatusMessage = "The RawFile draft already matches the editor content.";
            return;
        }

        IsApplyingPayload = true;
        try
        {
            string payloadSnapshot = PayloadInput;
            RawFileDisplayEncoding displayEncoding = DisplayEncoding;
            RawFileContentClassification? classificationSnapshot =
                _contentClassification;
            GscAnalysisOutcome? gscOutcome = null;
            if (IsGscSource && displayEncoding == RawFileDisplayEncoding.Text)
            {
                StatusMessage = "Checking GSC before applying…";
                GscAnalysisOperation operation = BeginGscAnalysis();
                gscOutcome = await RunGscAnalysisAsync(
                    operation,
                    useWorkspace: true,
                    delay: TimeSpan.Zero,
                    activeStatusMessage: "Checking GSC before applying…");
                if (gscOutcome is null)
                {
                    StatusMessage =
                        "Apply canceled because the editor content changed during the GSC check.";
                    return;
                }

                RequestSourceDiagnosticsPresentation(gscOutcome);
            }

            bool changed;
            if (displayEncoding == RawFileDisplayEncoding.Text)
            {
                RawFileContentClassification classification =
                    classificationSnapshot
                    ?? throw new InvalidOperationException(
                        "RawFile content classification is unavailable.");
                if (!classification.IsTextual)
                {
                    throw new InvalidOperationException(
                        "Hex presentation is required for binary RawFile content.");
                }

                RawFileTextEncoding encoding =
                    classification.TextEncoding ?? RawFileTextEncoding.Utf8;
                changed = _editorSession.Apply<RawFileDraft>(
                    draft => draft.ReplaceCanonicalText(payloadSnapshot, encoding));
            }
            else
            {
                byte[] bytes = ParseHex(payloadSnapshot);
                changed = _editorSession.Apply<RawFileDraft>(
                    draft => draft.ReplaceCanonicalContent(bytes));
            }

            _draft = _editorSession.ReadDraft<RawFileDraft>();
            Diagnostics = _editorSession.Validation.Issues;
            StatusMessage = CreateApplyStatusMessage(changed, gscOutcome);
            RefreshPresentation();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or IOException or OverflowException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsApplyingPayload = false;
        }
    }

    public string ExportPayload()
    {
        if (PayloadMode is null)
        {
            StatusMessage = "No RawFile payload is available to export.";
            return string.Empty;
        }

        ExportedPayload = FormatPayload(
            CurrentLogicalContentCopy(),
            _contentClassification,
            DisplayEncoding);
        StatusMessage = "Exported a detached text/hex representation; no file was written.";
        return ExportedPayload;
    }

    public void ClearBuffer()
    {
        if (!CanClearBuffer)
        {
            StatusMessage = "Only an editable uncompressed RawFile can use the nullable empty buffer form.";
            return;
        }

        _editorSession.Apply<RawFileDraft>(draft => draft.ClearBuffer());
        _draft = _editorSession.ReadDraft<RawFileDraft>();
        Diagnostics = _editorSession.Validation.Issues;
        StatusMessage = "Replaced the detached payload with the nullable empty-buffer form.";
        RefreshPresentation();
    }

    /// <summary>
    /// Replaces the selected binary RawFile with exact bytes read from a local
    /// file. The draft remains detached until the normal save flow is used.
    /// </summary>
    public void ReplaceFromFile(ReadOnlySpan<byte> content, string fileName)
    {
        if (!CanReplaceFromFile || _draft is null)
        {
            StatusMessage = "Only an editable binary RawFile can be replaced from a file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            StatusMessage = "The selected replacement file has no name.";
            return;
        }

        try
        {
            byte[] replacement = content.ToArray();
            _editorSession.Apply<RawFileDraft>(
                draft => draft.ReplaceBinaryContent(replacement));
            _draft = _editorSession.ReadDraft<RawFileDraft>();
            Diagnostics = _editorSession.Validation.Issues;
            StatusMessage = $"Replaced the detached binary payload from '{fileName}'.";
            RefreshPresentation();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or OverflowException)
        {
            StatusMessage = exception.Message;
        }
    }

    public void ReportReplacementFailure(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            StatusMessage = message;
    }

    public void RevertDraft()
    {
        if (!CanRevert)
        {
            StatusMessage = "Read-only RawFile content cannot be reverted because it has no target-owned draft.";
            return;
        }

        _ = _editorSession.Revert();
        _draft = _editorSession.ReadDraft<RawFileDraft>();
        Diagnostics = _editorSession.Validation.Issues;
        StatusMessage = "Reverted the detached RawFile draft to its authored baseline.";
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (PayloadMode is not null)
        {
            _contentClassification = ReadContentClassification();
            byte[] logicalContent = CurrentLogicalContentCopy();
            RawFileDisplayEncoding inferredDisplay =
                _contentClassification?.IsTextual == true
                    ? RawFileDisplayEncoding.Text
                    : RawFileDisplayEncoding.Hex;
            if (_displayEncoding != inferredDisplay)
            {
                _displayEncoding = inferredDisplay;
                OnPropertyChanged(nameof(DisplayEncoding));
            }

            SetDraftPayloadPresentation(FormatPayload(
                logicalContent,
                _contentClassification,
                DisplayEncoding));
            RefreshNativePreview(logicalContent);
        }
        else
        {
            _contentClassification = null;
            SetDraftPayloadPresentation(string.Empty);
            RefreshNativePreview([]);
        }

        OnPropertyChanged(nameof(OriginalName));
        OnPropertyChanged(nameof(PayloadMode));
        OnPropertyChanged(nameof(PayloadModeText));
        OnPropertyChanged(nameof(ContentKind));
        OnPropertyChanged(nameof(ContentKindText));
        OnPropertyChanged(nameof(IsCompressedPayload));
        OnPropertyChanged(nameof(HasBuffer));
        OnPropertyChanged(nameof(CompressedLength));
        OnPropertyChanged(nameof(UncompressedLength));
        OnPropertyChanged(nameof(SerializedLengthText));
        OnPropertyChanged(nameof(EditorProperties));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanClearBuffer));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanReplaceFromFile));
        OnPropertyChanged(nameof(IsGscSource));
        OnPropertyChanged(nameof(HasGscWorkspaceFeatures));
        OnPropertyChanged(nameof(CanAnalyzeGsc));
    }

    private void SetDraftPayloadPresentation(string value)
    {
        _draftPayloadPresentation = value;
        if (!string.Equals(PayloadInput, value, StringComparison.Ordinal))
        {
            PayloadInput = value;
            return;
        }

        NotifyStagingStateChanged();
    }

    private void NotifyStagingStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingPayloadChanges));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelGscAnalysis();
        CancelGscUsageSearch();
        _nativePreview?.Dispose();
        _nativePreview = null;
        SourceDiagnosticsPresentationRequested = null;
    }

    private byte[] CurrentLogicalContentCopy() => _draft?.GetLogicalContentCopy()
        ?? _readOnlySnapshot?.GetLogicalContentCopy()
        ?? [];

    private RawFileContentClassification? ReadContentClassification() =>
        _draft?.GetContentClassification()
        ?? _readOnlySnapshot?.GetContentClassification();

    private void RefreshNativePreview(ReadOnlySpan<byte> logicalContent)
    {
        Bitmap? previousPreview = _nativePreview;
        _nativePreview = null;
        _nativeViewerKind = null;

        if (RawFileNativeViewerRegistry.TryCreatePreview(
                OriginalName,
                logicalContent,
                out RawFileNativeViewerKind viewerKind,
                out Bitmap? preview))
        {
            _nativePreview = preview;
            _nativeViewerKind = viewerKind;
        }

        previousPreview?.Dispose();
        OnPropertyChanged(nameof(NativePreview));
        OnPropertyChanged(nameof(HasNativePreview));
        OnPropertyChanged(nameof(NativePreviewTitle));
    }

    private static string FormatPayload(
        ReadOnlySpan<byte> content,
        RawFileContentClassification? classification,
        RawFileDisplayEncoding displayEncoding)
    {
        if (displayEncoding == RawFileDisplayEncoding.Text &&
            classification is { IsTextual: true, TextEncoding: { } encoding })
        {
            return RawFileContentClassifier.DecodeText(content, encoding);
        }

        return FormatHex(content);
    }

    private static string FormatHex(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
            return string.Empty;

        const int bytesPerLine = 16;
        var builder = new StringBuilder(
            checked(content.Length * 3 + content.Length / bytesPerLine));
        for (int index = 0; index < content.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(index % bytesPerLine == 0 ? '\n' : ' ');
            }
            builder.Append(content[index].ToString("X2"));
        }
        return builder.ToString();
    }

    private string ContentTypePropertyText => ContentKind switch
    {
        RawFileContentKind.Textual => "Text",
        RawFileContentKind.Binary => "Binary",
        null => "—",
        _ => "—"
    };

    private string TextEncodingPropertyText =>
        _contentClassification?.TextEncoding is { } encoding
            ? RawFileContentClassifier.GetDisplayName(encoding)
            : "—";

    private string StoragePropertyText => PayloadMode switch
    {
        RawFilePayloadMode.CompressedPayload => "Zlib compressed",
        RawFilePayloadMode.UncompressedText => "Plaintext",
        RawFilePayloadMode.UncompressedBinary => "Raw",
        null => "—",
        _ => "—"
    };

    private string StoredSizePropertyText
    {
        get
        {
            if (!HasBuffer)
                return "No buffer";

            int length = PayloadMode == RawFilePayloadMode.CompressedPayload
                ? CompressedLength
                : checked(UncompressedLength + 1);
            return $"{length:N0} bytes";
        }
    }

    private async Task<IReadOnlyList<GscEditorCompletion>>
        GetGscCompletionsAsync(
            int caretOffset,
            bool requireAutomaticContext,
            CancellationToken cancellationToken)
    {
        GscEditorQuerySnapshot? query = CaptureGscEditorQuery(caretOffset);
        if (query is null)
            return [];

        try
        {
            IReadOnlyList<GscEditorCompletion> completions = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return _gscLanguageSession!.GetCompletions(
                        query.AssetName,
                        CreateGscSourceText(query.Source, query.Encoding),
                        query.BufferVersion,
                        query.CaretOffset,
                        requireAutomaticContext,
                        cancellationToken);
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return OwnsGscEditorQuery(query) ? completions : [];
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException)
        {
            if (OwnsGscEditorQuery(query))
                StatusMessage = $"GSC completion failed: {exception.Message}";
            return [];
        }
    }

    private GscEditorQuerySnapshot? CaptureGscEditorQuery(int caretOffset)
    {
        string source = PayloadInput;
        if (_disposed ||
            _gscLanguageSession is null ||
            !IsGscSource ||
            caretOffset < 0 ||
            caretOffset > source.Length)
        {
            return null;
        }

        return new GscEditorQuerySnapshot(
            OriginalName,
            source,
            _contentClassification?.TextEncoding,
            _bufferVersion,
            caretOffset);
    }

    private bool OwnsGscEditorQuery(GscEditorQuerySnapshot query) =>
        !_disposed && query.BufferVersion == _bufferVersion;

    private void InvalidateGscBufferState()
    {
        _bufferVersion = checked(_bufferVersion + 1);
        CancelGscAnalysis();
        if (CancelGscUsageSearch())
            StatusMessage = "GSC reference search canceled because the editor changed.";

        SourceDiagnostics = [];
        if (!IsGscSource || DisplayEncoding != RawFileDisplayEncoding.Text)
        {
            GscAnalysisStatusMessage = string.Empty;
            return;
        }

        GscAnalysisStatusMessage = "GSC check pending…";
        GscAnalysisOperation operation = BeginGscAnalysis();
        _ = RunGscAnalysisAsync(
            operation,
            useWorkspace: false,
            delay: LiveGscAnalysisDelay,
            activeStatusMessage: "Checking GSC…");
    }

    private GscAnalysisOperation BeginGscAnalysis()
    {
        CancelGscAnalysis();
        var cancellation = new CancellationTokenSource();
        _gscAnalysisCancellation = cancellation;
        return new GscAnalysisOperation(
            OriginalName,
            PayloadInput,
            _contentClassification?.TextEncoding,
            _bufferVersion,
            cancellation);
    }

    private async Task<GscAnalysisOutcome?> RunGscAnalysisAsync(
        GscAnalysisOperation operation,
        bool useWorkspace,
        TimeSpan delay,
        string activeStatusMessage)
    {
        CancellationToken cancellationToken = operation.Cancellation.Token;
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            if (!OwnsGscAnalysis(operation))
                return null;

            IsAnalyzingGsc = true;
            GscAnalysisStatusMessage = activeStatusMessage;
            GscAnalysisResult result = await Task.Run(
                () => useWorkspace
                    ? AnalyzeAuthoritativeGscSnapshot(operation, cancellationToken)
                    : AnalyzeLocalGscSnapshot(operation, cancellationToken),
                cancellationToken);
            if (!OwnsGscAnalysis(operation))
                return null;

            IReadOnlyList<EditorSourceDiagnostic> diagnostics =
                Array.AsReadOnly(result.Diagnostics
                    .Select(ToEditorDiagnostic)
                    .ToArray());
            var outcome = new GscAnalysisOutcome(diagnostics, FailureMessage: null);
            SourceDiagnostics = diagnostics;
            GscAnalysisStatusMessage = CreateGscAnalysisStatusMessage(outcome);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer buffer or analysis owns the visible GSC state.
            return null;
        }
        catch (Exception exception)
        {
            if (!OwnsGscAnalysis(operation))
                return null;

            var outcome = new GscAnalysisOutcome([], exception.Message);
            SourceDiagnostics = [];
            GscAnalysisStatusMessage = CreateGscAnalysisStatusMessage(outcome);
            return outcome;
        }
        finally
        {
            if (ReferenceEquals(
                    _gscAnalysisCancellation,
                    operation.Cancellation))
            {
                _gscAnalysisCancellation = null;
                IsAnalyzingGsc = false;
            }

            operation.Cancellation.Dispose();
        }
    }

    private bool OwnsGscAnalysis(GscAnalysisOperation operation) =>
        !_disposed &&
        operation.BufferVersion == _bufferVersion &&
        ReferenceEquals(_gscAnalysisCancellation, operation.Cancellation);

    private void RequestSourceDiagnosticsPresentation(
        GscAnalysisOutcome? outcome)
    {
        if (outcome is { FailureMessage: null, Diagnostics.Count: > 0 })
            SourceDiagnosticsPresentationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelGscAnalysis()
    {
        CancellationTokenSource? cancellation = _gscAnalysisCancellation;
        _gscAnalysisCancellation = null;
        if (cancellation is not null)
            cancellation.Cancel();

        IsAnalyzingGsc = false;
    }

    private bool CancelGscUsageSearch()
    {
        CancellationTokenSource? cancellation = _gscUsageSearchCancellation;
        _gscUsageSearchCancellation = null;
        if (cancellation is null)
            return false;

        cancellation.Cancel();
        return true;
    }

    private static EditorSourceDiagnostic ToEditorDiagnostic(
        GscDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == GscDiagnosticSeverity.Error
                ? EditorSourceDiagnosticSeverity.Error
                : EditorSourceDiagnosticSeverity.Warning,
            diagnostic.Message,
            new EditorTextLocation(
                diagnostic.Span.Start,
                diagnostic.Span.Length,
                diagnostic.LineSpan.Start.Line,
                diagnostic.LineSpan.Start.Character));

    private static GscSourceText CreateGscSourceText(
        string source,
        RawFileTextEncoding? encoding)
    {
        if (encoding == RawFileTextEncoding.Windows1252)
        {
            try
            {
                return new GscSourceText(
                    source,
                    RawFileContentClassifier.GetTextEncoding(encoding.Value));
            }
            catch (EncoderFallbackException)
            {
                // RawFile canonical text uses the same UTF-8 fallback.
            }
        }

        return new GscSourceText(source);
    }

    private GscAnalysisResult AnalyzeLocalGscSnapshot(
        GscAnalysisOperation operation,
        CancellationToken cancellationToken)
    {
        GscSourceText sourceText = CreateGscSourceText(
            operation.Source,
            operation.Encoding);
        return _gscAnalyzer.Analyze(sourceText, cancellationToken);
    }

    private GscAnalysisResult AnalyzeAuthoritativeGscSnapshot(
        GscAnalysisOperation operation,
        CancellationToken cancellationToken)
    {
        GscSourceText sourceText = CreateGscSourceText(
            operation.Source,
            operation.Encoding);
        if (_gscAnalysisLanguageSession is null)
            return _gscAnalyzer.Analyze(sourceText, cancellationToken);

        return _gscAnalysisLanguageSession.Analyze(
            operation.AssetName,
            sourceText,
            operation.BufferVersion,
            cancellationToken);
    }

    private static string CreateGscAnalysisStatusMessage(
        GscAnalysisOutcome outcome)
    {
        if (outcome.FailureMessage is { } failureMessage)
            return $"GSC analysis failed: {failureMessage}";

        int errorCount = outcome.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == EditorSourceDiagnosticSeverity.Error);
        int warningCount = outcome.Diagnostics.Count - errorCount;
        if (errorCount == 0 && warningCount == 0)
            return "No GSC errors.";

        return $"GSC check found {CreateFindingCountText(errorCount, warningCount)}.";
    }

    private static string CreateApplyStatusMessage(
        bool changed,
        GscAnalysisOutcome? outcome)
    {
        const string applied = "Applied logical content to the RawFile draft. " +
            "Use Save As to write the fastfile.";
        if (!changed)
        {
            return "The RawFile draft already matched the editor content. " +
                "Use Save As to write the fastfile.";
        }
        if (outcome is null)
            return applied;
        if (outcome.FailureMessage is { } failureMessage)
            return $"{applied} GSC check was unavailable: {failureMessage}";

        int errorCount = outcome.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == EditorSourceDiagnosticSeverity.Error);
        int warningCount = outcome.Diagnostics.Count - errorCount;
        if (errorCount == 0 && warningCount == 0)
            return $"{applied} GSC check passed.";

        return $"{applied} GSC check reported " +
            $"{CreateFindingCountText(errorCount, warningCount)}; findings are advisory.";
    }

    private static string CreateFindingCountText(
        int errorCount,
        int warningCount)
    {
        var counts = new List<string>(2);
        if (errorCount != 0)
            counts.Add(errorCount == 1 ? "1 error" : $"{errorCount} errors");
        if (warningCount != 0)
            counts.Add(warningCount == 1 ? "1 warning" : $"{warningCount} warnings");
        return string.Join(" and ", counts);
    }

    private GscUsagePresentation? FindGscUsagesSnapshot(
        string assetName,
        string source,
        RawFileTextEncoding? sourceEncoding,
        long bufferVersion,
        int sourceOffset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _gscLanguageSession!.FindDefinitions(
            assetName,
            CreateGscSourceText(source, sourceEncoding),
            bufferVersion,
            sourceOffset,
            out GscWorkspaceSnapshot snapshot,
            out GscSymbolDefinition[] definitions,
            cancellationToken);
        if (definitions.Length == 0)
            return null;

        var items = new List<GscUsagePresentationItem>();
        var seen = new HashSet<(
            GscScriptPath Path,
            GscTextSpan Span,
            GscWorkspaceReferenceKind Kind)>();
        foreach (GscSymbolDefinition definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (GscSymbolReference reference in
                     snapshot.Index.FindUsages(definition.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Add((
                        reference.Location.Path,
                        reference.Location.Span,
                        reference.Kind)))
                {
                    items.Add(CreateUsagePresentation(snapshot, reference));
                }
            }
        }

        return new GscUsagePresentation(definitions[0].SourceName, items);
    }

    private bool TryGetDefinitions(
        int sourceOffset,
        out GscWorkspaceSnapshot? snapshot,
        out GscSymbolDefinition[] definitions)
    {
        snapshot = null;
        definitions = [];
        if (!HasGscWorkspaceFeatures ||
            sourceOffset < 0 ||
            sourceOffset > PayloadInput.Length)
        {
            return false;
        }

        try
        {
            _gscLanguageSession!.FindDefinitions(
                OriginalName,
                CreateGscSourceText(
                    PayloadInput,
                    _contentClassification?.TextEncoding),
                _bufferVersion,
                sourceOffset,
                out GscWorkspaceSnapshot foundSnapshot,
                out definitions);
            snapshot = foundSnapshot;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException)
        {
            StatusMessage = $"GSC workspace query failed: {exception.Message}";
            return false;
        }
    }

    private bool TryGetDefinitionTargets(
        int sourceOffset,
        out GscSymbolDefinition[] definitions,
        out Iw4GscBuiltInDefinition[] builtIns)
    {
        definitions = [];
        builtIns = [];
        if (!HasGscWorkspaceFeatures ||
            sourceOffset < 0 ||
            sourceOffset > PayloadInput.Length)
        {
            return false;
        }

        try
        {
            _gscLanguageSession!.FindDefinitionTargets(
                OriginalName,
                CreateGscSourceText(
                    PayloadInput,
                    _contentClassification?.TextEncoding),
                _bufferVersion,
                sourceOffset,
                out _,
                out definitions,
                out builtIns);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                InvalidOperationException)
        {
            StatusMessage = $"GSC workspace query failed: {exception.Message}";
            return false;
        }
    }

    private static GscUsagePresentationItem CreateUsagePresentation(
        GscWorkspaceSnapshot snapshot,
        GscSymbolReference reference)
    {
        GscIndexedDocument document = snapshot.Index.GetDocument(
            reference.Location.Path);
        GscLinePosition position = document.Snapshot.Source.GetLinePosition(
            reference.Location.Span.Start);
        return new GscUsagePresentationItem(
            reference.Location,
            reference.Location.Path.Value,
            $"Ln {position.Line + 1}, Col {position.Character + 1}",
            reference.Kind.ToString(),
            GetSourceLine(
                document.Snapshot.Source.Text,
                reference.Location.Span.Start));
    }

    private static string GetSourceLine(string source, int offset)
    {
        int start = source.LastIndexOf('\n', Math.Max(0, offset - 1));
        start = start < 0 ? 0 : start + 1;
        int end = source.IndexOf('\n', offset);
        if (end < 0)
            end = source.Length;
        if (end > start && source[end - 1] == '\r')
            end--;
        return source[start..end].Trim();
    }

    private static byte[] ParseHex(string value)
    {
        string compact = string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
        if (compact.Length % 2 != 0)
            throw new FormatException("Hex payloads must contain an even number of digits.");

        return compact.Length == 0 ? [] : Convert.FromHexString(compact);
    }

    private sealed record GscAnalysisOperation(
        string AssetName,
        string Source,
        RawFileTextEncoding? Encoding,
        long BufferVersion,
        CancellationTokenSource Cancellation);

    private sealed record GscAnalysisOutcome(
        IReadOnlyList<EditorSourceDiagnostic> Diagnostics,
        string? FailureMessage);

    private sealed record GscEditorQuerySnapshot(
        string AssetName,
        string Source,
        RawFileTextEncoding? Encoding,
        long BufferVersion,
        int CaretOffset);
}
