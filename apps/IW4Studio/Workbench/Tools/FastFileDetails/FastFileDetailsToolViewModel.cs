using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using IW4.Game.Database;
using IW4.Linker.Packaging;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileDetails;

/// <summary>
/// Edits the authored DB-header values carried by the current semantic
/// revision. Package sizes and streamed-image counts remain derived.
/// </summary>
public sealed class FastFileDetailsToolViewModel : ObservableObject
{
    private const string FileTimeFormat =
        "yyyy-MM-dd HH:mm:ss.fffffff 'UTC'";

    private static readonly string[] AcceptedFileTimeFormats =
    [
        FileTimeFormat,
        "yyyy-MM-dd HH:mm:ss 'UTC'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    private readonly FastFileEditingSession _editingSession;
    private readonly Action _showStreamedImages;
    private HeaderDraft? _baseline;
    private bool _allowOnlineUpdate;
    private string _fileCreationTimeUtc = string.Empty;
    private DateTimeOffset? _pickerDateUtc;
    private TimeSpan? _pickerTimeUtc;
    private bool _updatingPickers;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    public FastFileDetailsToolViewModel(
        FastFileEditingSession editingSession,
        Action showStreamedImages)
    {
        _editingSession = editingSession ??
            throw new ArgumentNullException(nameof(editingSession));
        _showStreamedImages = showStreamedImages ??
            throw new ArgumentNullException(nameof(showStreamedImages));
        FastFileWorkspace workspace = editingSession.Workspace;
        FileName = Path.GetFileName(workspace.SourcePath);
        StreamedImageCount = workspace.LoadedZone.Header.EntryCount.ToString(
            "N0",
            CultureInfo.InvariantCulture);
        Languages = Enum.GetValues<XFileLanguage>()
            .Where(language => language != XFileLanguage.COUNT)
            .Select(language => new FastFileLanguageOptionViewModel(
                GetDisplayName(language),
                1u << ((int)language - 1),
                DraftChanged))
            .ToArray();
        ApplyCommand = new ViewModelCommand(Apply, () => HasDraftChanges);
        RevertCommand = new ViewModelCommand(Revert, () => HasDraftChanges);
        ShowStreamedImagesCommand = new ViewModelCommand(_showStreamedImages);
        LoadDraftFromSession();
    }

    public string FileName { get; }

    public string Magic => DbHeader.UnsignedMagic;

    public string MagicType => Magic switch
    {
        DbHeader.UnsignedMagic => "Unsigned",
        "IWff0100" => "Signed",
        _ => "Unsupported"
    };

    public string Version => _editingSession.HeaderMetadata.Version.ToString();

    public bool AllowOnlineUpdate
    {
        get => _allowOnlineUpdate;
        set
        {
            if (SetProperty(ref _allowOnlineUpdate, value))
                DraftChanged();
        }
    }

    public string FileCreationTimeUtc
    {
        get => _fileCreationTimeUtc;
        set
        {
            if (SetProperty(ref _fileCreationTimeUtc, value ?? string.Empty))
            {
                if (!_updatingPickers)
                    SyncPickersFromText();
                DraftChanged();
            }
        }
    }

    public DateTimeOffset? PickerDateUtc
    {
        get => _pickerDateUtc;
        set
        {
            if (SetProperty(ref _pickerDateUtc, value) && !_updatingPickers)
                UpdateFileTimeFromPickers();
        }
    }

    public TimeSpan? PickerTimeUtc
    {
        get => _pickerTimeUtc;
        set
        {
            if (SetProperty(ref _pickerTimeUtc, value) && !_updatingPickers)
                UpdateFileTimeFromPickers();
        }
    }

    public IReadOnlyList<FastFileLanguageOptionViewModel> Languages { get; }

    public string StreamedImageCount { get; }

    public bool HasDraftChanges =>
        _baseline is null || !_baseline.Equals(CaptureDraft());

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public ViewModelCommand ApplyCommand { get; }

    public ViewModelCommand RevertCommand { get; }

    public ViewModelCommand ShowStreamedImagesCommand { get; }

    internal void RefreshAfterSave()
    {
        if (HasDraftChanges ||
            _editingSession.HasPendingHeaderPropertiesChange)
        {
            return;
        }

        ClearFeedback();
        LoadDraftFromSession();
    }

    private void Apply()
    {
        ClearFeedback();
        if (!TryCreateProperties(
                out DbHeaderAuthoringMetadata metadata,
                out uint languageMask,
                out string error))
        {
            ErrorMessage = error;
            return;
        }

        try
        {
            bool changed = _editingSession.UpdateHeaderProperties(
                metadata,
                languageMask);
            LoadDraftFromSession();
            StatusMessage = changed
                ? "Header properties were applied to the pending fastfile revision."
                : "Header properties already match the current revision.";
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            InvalidOperationException or
            OverflowException)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void Revert()
    {
        ClearFeedback();
        LoadDraftFromSession();
    }

    private bool TryCreateProperties(
        out DbHeaderAuthoringMetadata metadata,
        out uint languageMask,
        out string error)
    {
        DbHeaderAuthoringMetadata current = _editingSession.HeaderMetadata;
        metadata = current;
        languageMask = CaptureLanguageMask();

        if (!TryParseFileCreationTime(FileCreationTimeUtc, out ulong fileCreationTimeRaw))
        {
            error = "Creation time must be UTC as yyyy-MM-dd HH:mm:ss[.fffffff] UTC and fall within the Windows FILETIME range.";
            return false;
        }
        if (!DbLanguageMask.IsSupported(languageMask))
        {
            error = "Select at least one language.";
            return false;
        }

        metadata = current with
        {
            AllowOnlineUpdate = AllowOnlineUpdate,
            FileCreationTimeRaw = fileCreationTimeRaw
        };
        error = string.Empty;
        return true;
    }

    private void LoadDraftFromSession()
    {
        DbHeaderAuthoringMetadata metadata = _editingSession.HeaderMetadata;
        uint languageMask = _editingSession.LanguageMask;
        var draft = new HeaderDraft(
            metadata.AllowOnlineUpdate,
            FormatFileCreationTime(metadata.FileCreationTimeRaw),
            languageMask);
        _baseline = draft;
        AllowOnlineUpdate = draft.AllowOnlineUpdate;
        FileCreationTimeUtc = draft.FileCreationTimeUtc;
        foreach (FastFileLanguageOptionViewModel language in Languages)
            language.Restore((languageMask & language.Mask) != 0);
        RefreshDraftState();
    }

    private HeaderDraft CaptureDraft() => new(
        AllowOnlineUpdate,
        FileCreationTimeUtc,
        CaptureLanguageMask());

    private uint CaptureLanguageMask()
    {
        uint mask = 0;
        foreach (FastFileLanguageOptionViewModel language in Languages)
        {
            if (language.IsSelected)
                mask |= language.Mask;
        }

        return mask;
    }

    private void DraftChanged()
    {
        ClearFeedback();
        RefreshDraftState();
    }

    private void RefreshDraftState()
    {
        OnPropertyChanged(nameof(HasDraftChanges));
        ApplyCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
    }

    private void ClearFeedback()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    private void SyncPickersFromText()
    {
        _updatingPickers = true;
        try
        {
            if (TryParseFileCreationTime(FileCreationTimeUtc, out ulong raw) &&
                TryGetFileCreationDate(raw, out DateTime utc))
            {
                PickerDateUtc = new DateTimeOffset(utc);
                PickerTimeUtc = utc.TimeOfDay;
            }
            else
            {
                PickerDateUtc = null;
                PickerTimeUtc = null;
            }
        }
        finally
        {
            _updatingPickers = false;
        }
    }

    private void UpdateFileTimeFromPickers()
    {
        if (PickerDateUtc is not { } date)
            return;

        TimeSpan time = PickerTimeUtc ?? TimeSpan.Zero;
        long subMinuteTicks = 0;
        if (TryParseFileCreationTime(FileCreationTimeUtc, out ulong raw) &&
            TryGetFileCreationDate(raw, out DateTime current))
        {
            subMinuteTicks = current.TimeOfDay.Ticks % TimeSpan.TicksPerMinute;
        }

        DateTime utc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
            .AddHours(time.Hours)
            .AddMinutes(time.Minutes)
            .AddTicks(subMinuteTicks);
        _updatingPickers = true;
        try
        {
            FileCreationTimeUtc = utc.ToString(FileTimeFormat, CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingPickers = false;
        }
    }

    private static bool TryParseFileCreationTime(string text, out ulong raw)
    {
        raw = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                text.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out raw);
        }
        if (!DateTime.TryParseExact(
                text,
                AcceptedFileTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out DateTime utc))
        {
            return false;
        }

        try
        {
            long signedRaw = utc.ToFileTimeUtc();
            if (signedRaw < 0)
                return false;

            raw = checked((ulong)signedRaw);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryGetFileCreationDate(ulong raw, out DateTime utc)
    {
        try
        {
            utc = DateTime.FromFileTimeUtc(checked((long)raw));
            return true;
        }
        catch (Exception exception) when (exception is
            OverflowException or ArgumentOutOfRangeException)
        {
            utc = default;
            return false;
        }
    }

    private static string FormatFileCreationTime(ulong raw) =>
        TryGetFileCreationDate(raw, out DateTime utc)
            ? utc.ToString(FileTimeFormat, CultureInfo.InvariantCulture)
            : $"0x{raw:X16}";

    private static string GetDisplayName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        FieldInfo? field = typeof(TEnum).GetField(value.ToString());
        return field?.GetCustomAttribute<DisplayAttribute>()?.GetName()
            ?? value.ToString();
    }

    private sealed record HeaderDraft(
        bool AllowOnlineUpdate,
        string FileCreationTimeUtc,
        uint LanguageMask);
}

public sealed class FastFileLanguageOptionViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    internal FastFileLanguageOptionViewModel(
        string displayName,
        uint mask,
        Action selectionChanged)
    {
        DisplayName = displayName;
        Mask = mask;
        _selectionChanged = selectionChanged;
    }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                _selectionChanged();
        }
    }

    internal uint Mask { get; }

    internal void Restore(bool isSelected) =>
        SetProperty(ref _isSelected, isSelected, nameof(IsSelected));
}
