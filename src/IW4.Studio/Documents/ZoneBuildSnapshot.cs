using IW4.FastFiles.Database;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Loaders.Assets;
using IW4.Runtime.Assets;
using System.Security.Cryptography;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Documents;

public sealed record ZoneBuildError(int RowIndex, string FieldPath, string Message)
{
    public override string ToString() => $"row {RowIndex} {FieldPath}: {Message}";
}

public sealed class ZoneBuildValidation
{
    private readonly IReadOnlyList<ZoneBuildError> _errors;
    public ZoneBuildValidation(IEnumerable<ZoneBuildError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors = Array.AsReadOnly(errors.OrderBy(blocker => blocker.RowIndex).ThenBy(blocker => blocker.FieldPath, StringComparer.Ordinal).ThenBy(blocker => blocker.Message, StringComparer.Ordinal).ToArray());
    }
    public IReadOnlyList<ZoneBuildError> Errors => _errors;
    public bool IsValid => _errors.Count == 0;
}

/// <summary>One preserved external package required by the frozen build. The
/// asset model keeps only the logical imagefile index; this document-level
/// plan binds it to the opened source container and verifies exact bytes.</summary>
public sealed record ResourceOutputPlan
{
    public ResourceOutputPlan(
        int rowIndex,
        string fileName,
        long sourceLength,
        string sourceSha256Hex)
    {
        if (rowIndex < 0) throw new ArgumentOutOfRangeException(nameof(rowIndex));
        if (sourceLength < 0) throw new ArgumentOutOfRangeException(nameof(sourceLength));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256Hex);
        RowIndex = rowIndex;
        FileName = ValidateFileName(fileName);
        SourceLength = sourceLength;
        SourceSha256Hex = sourceSha256Hex;
    }
    public int RowIndex { get; }
    public string FileName { get; }
    public long SourceLength { get; }
    public string SourceSha256Hex { get; }
    private static string ValidateFileName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) || value.IndexOf(Path.DirectorySeparatorChar) >= 0 || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException("A resource output plan must contain a relative file name only.", nameof(value));
        return value;
    }
}

public abstract class ZoneBuildRow
{
    protected ZoneBuildRow(
        int index,
        XAssetType assetType,
        int rawHeader,
        string? originalSerializedName)
    {
        Index = index;
        AssetType = assetType;
        RawHeader = rawHeader;
        OriginalSerializedName = originalSerializedName;
    }
    public int Index { get; }
    public XAssetType AssetType { get; }
    public int RawHeader { get; }
    public string? OriginalSerializedName { get; }
}
public sealed class OwnedDefinitionBuildRow : ZoneBuildRow
{
    public OwnedDefinitionBuildRow(int index, XAssetType assetType, int rawHeader, IXAssetBuildData buildData, string? originalSerializedName = null) : base(index, assetType, rawHeader, originalSerializedName) => BuildData = buildData ?? throw new ArgumentNullException(nameof(buildData));
    public IXAssetBuildData BuildData { get; }
}
public sealed class ExternalReferenceBuildRow : ZoneBuildRow
{
    public ExternalReferenceBuildRow(int index, XAssetType assetType, int rawHeader, TargetZoneExternalReferenceIdentity reference, string? originalSerializedName = null) : base(index, assetType, rawHeader, originalSerializedName) => Reference = reference ?? throw new ArgumentNullException(nameof(reference));
    public TargetZoneExternalReferenceIdentity Reference { get; }
}
public sealed class NullBuildRow(int index, XAssetType assetType, int rawHeader, string? originalSerializedName = null) : ZoneBuildRow(index, assetType, rawHeader, originalSerializedName);
public sealed class OpaqueNativeNoOpBuildRow(int index, XAssetType assetType, int rawHeader, string? originalSerializedName = null) : ZoneBuildRow(index, assetType, rawHeader, originalSerializedName);
public sealed class UnsupportedBuildRow(int index, XAssetType assetType, int rawHeader, string reason, string? originalSerializedName = null) : ZoneBuildRow(index, assetType, rawHeader, originalSerializedName) { public string Reason { get; } = reason; }

/// <summary>Frozen revision input. It contains no runtime, pool, workspace, or editor-control reference.</summary>
public sealed class ZoneBuildSnapshot
{
    private readonly IReadOnlyList<ZoneBuildRow> _rows;
    private readonly IReadOnlyList<TargetZoneScriptStringSource> _scriptStrings;
    public ZoneBuildSnapshot(
        Guid documentId,
        long capturedRevision,
        DbHeader containerEnvelope,
        TargetZoneDecodedZoneMetadata decodedMetadata,
        IEnumerable<TargetZoneScriptStringSource> scriptStrings,
        IEnumerable<ZoneBuildRow> rows,
        ZoneBuildValidation validation,
        IEnumerable<ResourceOutputPlan>? resourceOutputs = null,
        string? sourcePhysicalPath = null)
    {
        ArgumentNullException.ThrowIfNull(containerEnvelope); ArgumentNullException.ThrowIfNull(decodedMetadata); ArgumentNullException.ThrowIfNull(scriptStrings); ArgumentNullException.ThrowIfNull(rows); ArgumentNullException.ThrowIfNull(validation);
        if (documentId == Guid.Empty || capturedRevision < 0) throw new ArgumentOutOfRangeException(nameof(documentId));
        ZoneBuildRow[] rowArray = rows.OrderBy(row => row.Index).ToArray();
        if (rowArray.Select(row => row.Index).SequenceEqual(Enumerable.Range(0, rowArray.Length)) is false)
            throw new InvalidDataException("Zone build rows must be unique contiguous source row indices.");
        DocumentId = documentId; CapturedRevision = capturedRevision; ContainerEnvelope = containerEnvelope; DecodedMetadata = decodedMetadata; SourcePhysicalPath = sourcePhysicalPath is null ? null : Path.GetFullPath(sourcePhysicalPath);
        _scriptStrings = Array.AsReadOnly(scriptStrings.OrderBy(value => value.Index).Select(value => value with { }).ToArray());
        _rows = Array.AsReadOnly(rowArray);
        ResourceOutputs = Array.AsReadOnly((resourceOutputs ?? []).OrderBy(value => value.FileName, StringComparer.Ordinal).ToArray());
        if (ResourceOutputs.Select(value => value.FileName).Distinct(StringComparer.Ordinal).Count() != ResourceOutputs.Count)
            throw new InvalidDataException("A build snapshot cannot contain duplicate external resource outputs.");
        Validation = validation;
    }
    public Guid DocumentId { get; }
    public long CapturedRevision { get; }
    public DbHeader ContainerEnvelope { get; }
    public TargetZoneDecodedZoneMetadata DecodedMetadata { get; }
    public IReadOnlyList<TargetZoneScriptStringSource> ScriptStrings => _scriptStrings;
    public IReadOnlyList<ZoneBuildRow> Rows => _rows;
    public ZoneBuildValidation Validation { get; }
    /// <summary>Opened source path used only to stage preserved sidecars. It
    /// is not an authored field and never reaches the emitted fastfile.</summary>
    public string? SourcePhysicalPath { get; }
    public IReadOnlyList<ResourceOutputPlan> ResourceOutputs { get; }
}

public sealed class ZoneBuildSnapshotBuilder
{
    private readonly AssetAuthoringAdapterRegistry _adapters;
    private readonly AssetBodyEmitterRegistry _emitters;
    private readonly AssetReferenceShapeRegistry _referenceShapes;

    public ZoneBuildSnapshotBuilder(
        AssetAuthoringAdapterRegistry? adapters = null,
        AssetBodyEmitterRegistry? emitters = null,
        AssetReferenceShapeRegistry? referenceShapes = null)
    {
        _adapters = adapters ?? AssetAuthoringAdapterRegistry.CreateDefault();
        _emitters = emitters ?? AssetBodyEmitterRegistry.CreateDefault();
        _referenceShapes = referenceShapes ?? AssetReferenceShapeRegistry.CreateDefault();
    }

    public ZoneBuildSnapshot Capture(FastFileEditingSession editingSession)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        FastFileEditingSaveSnapshot save = editingSession.CaptureForSave();
        return Capture(editingSession, save);
    }

    /// <summary>Builds from a caller-owned revision capture so transactional
    /// Save As can acknowledge exactly the same immutable revision it emits.</summary>
    public ZoneBuildSnapshot Capture(
        FastFileEditingSession editingSession,
        FastFileEditingSaveSnapshot save)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(save);
        ValidateSaveCapture(editingSession, save);
        TargetZoneSourceSnapshot source = editingSession.Workspace.TargetSource;
        MenuCompilationBatch menuBatch = MenuCompilationBatch.Compile(
            save,
            _adapters);
        var rows = new List<ZoneBuildRow>(save.TargetRows.Count);
        var errors = new List<ZoneBuildError>();
        for (int outputIndex = 0; outputIndex < save.TargetRows.Count; outputIndex++)
        {
            rows.Add(CreateRow(
                outputIndex,
                save.TargetRows[outputIndex],
                save,
                menuBatch,
                errors));
        }
        PreserveDetachedSemanticGraphIdentity(rows);
        ValidateSoundLanguageCounts(
            source.ContainerEnvelope,
            rows,
            errors);
        ResourceOutputPlan[] resourceOutputs = CreateResourceOutputPlans(source, rows, errors);
        var validation = new ZoneBuildValidation(errors);
        return new ZoneBuildSnapshot(source.DocumentId, save.Revision, source.ContainerEnvelope, source.DecodedMetadata, source.ScriptStrings, rows, validation, resourceOutputs, source.PhysicalPath);
    }

    private static void ValidateSaveCapture(
        FastFileEditingSession editingSession,
        FastFileEditingSaveSnapshot save)
    {
        if (save.DocumentId != editingSession.Document.DocumentId)
            throw new InvalidOperationException("The supplied save capture belongs to a different editing document.");
    }

    private ZoneBuildRow CreateRow(
        int outputIndex,
        TargetZoneRowSource row,
        FastFileEditingSaveSnapshot save,
        MenuCompilationBatch menuBatch,
        List<ZoneBuildError> errors)
    {
        int index = outputIndex;
        switch (row.State)
        {
            case TargetZoneRowSourceState.Definition:
                return CreateOwned(
                    outputIndex,
                    row,
                    save,
                    menuBatch,
                    errors);
            case TargetZoneRowSourceState.ResolvedReference:
            case TargetZoneRowSourceState.UnresolvedReference:
                if (row.RawHeader != -1)
                {
                    string headerReason = "External-reference rows require an inline (-1) top-level header; insert-cell emission is not enabled.";
                    errors.Add(new ZoneBuildError(index, "header", headerReason));
                    return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, headerReason, row.OriginalSerializedName);
                }
                if (row.ExternalReference is null)
                {
                    errors.Add(new ZoneBuildError(index, "reference", "Reference row has no immutable external-reference identity."));
                    return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, "Missing external reference identity.", row.OriginalSerializedName);
                }
                if (!_referenceShapes.TryGet(row.SerializedType, out _))
                {
                    string referenceReason =
                        $"No top-level external-reference shape is registered for '{row.SerializedType}'.";
                    errors.Add(new ZoneBuildError(index, "reference", referenceReason));
                    return new UnsupportedBuildRow(
                        index,
                        row.SerializedType,
                        row.RawHeader,
                        referenceReason,
                        row.OriginalSerializedName);
                }
                return new ExternalReferenceBuildRow(index, row.SerializedType, row.RawHeader, row.ExternalReference, row.OriginalSerializedName);
            case TargetZoneRowSourceState.Null:
                if (row.RawHeader != 0)
                {
                    string nullHeaderReason = "Null rows require a zero serialized header.";
                    errors.Add(new ZoneBuildError(index, "header", nullHeaderReason));
                    return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, nullHeaderReason, row.OriginalSerializedName);
                }
                return new NullBuildRow(index, row.SerializedType, row.RawHeader, row.OriginalSerializedName);
            case TargetZoneRowSourceState.OpaqueNativeNoOp:
                if (row.HeaderKind != XAssetHeaderKind.Opaque ||
                    XAssetTypeRuntimeMetadataCatalog.Get(row.SerializedType).Disposition !=
                    XAssetRuntimeDisposition.NativeNoOp ||
                    XAssetTopLevelDispatch.Classify(row.SerializedType) !=
                    XAssetTopLevelDispatchKind.NativeNoOp)
                {
                    string opaqueReason =
                        "Opaque rows require a native no-op asset type and an exact opaque header.";
                    errors.Add(new ZoneBuildError(index, "header", opaqueReason));
                    return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, opaqueReason, row.OriginalSerializedName);
                }
                return new OpaqueNativeNoOpBuildRow(index, row.SerializedType, row.RawHeader, row.OriginalSerializedName);
            default:
                string reason = $"Target row classification '{row.State}' is not compiler-supported.";
                errors.Add(new ZoneBuildError(index, "classification", reason));
                return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }
    }

    private ZoneBuildRow CreateOwned(
        int outputIndex,
        TargetZoneRowSource row,
        FastFileEditingSaveSnapshot save,
        MenuCompilationBatch menuBatch,
        List<ZoneBuildError> errors)
    {
        int index = outputIndex;
        if (row.RawHeader != -1)
        {
            string reason = "Owned definitions require an inline (-1) top-level header; insert-cell emission is not enabled.";
            errors.Add(new ZoneBuildError(index, "header", reason));
            return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }
        if (XAssetTopLevelDispatch.Classify(row.SerializedType) !=
            XAssetTopLevelDispatchKind.PointerWrapper)
        {
            string reason =
                $"Owned type '{row.SerializedType}' has no top-level pointer-wrapper loader.";
            errors.Add(new ZoneBuildError(index, "loader", reason));
            return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }
        if (!_adapters.TryGetAdapter(row.SerializedType, out IAssetAuthoringAdapter? adapter) || adapter is null)
        {
            string reason = $"Owned type '{row.SerializedType}' has no detached authoring adapter.";
            errors.Add(new ZoneBuildError(index, "adapter", reason));
            return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }
        if (!_emitters.TryGet(row.SerializedType, out _))
        {
            string reason = $"Owned type '{row.SerializedType}' has no body emitter.";
            errors.Add(new ZoneBuildError(index, "emitter", reason));
            return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }

        if (menuBatch.TryGet(
                row.Identity,
                out IXAssetBuildData? menuBuildData,
                out IReadOnlyList<AssetValidationIssue> menuValidation,
                out string? menuFailure))
        {
            foreach (AssetValidationIssue issue in menuValidation.Where(
                         issue => issue.Severity == AssetValidationSeverity.Error))
            {
                errors.Add(new ZoneBuildError(
                    index,
                    issue.FieldPath,
                    issue.Message));
            }

            if (menuBuildData is null || menuBuildData.AssetType != row.SerializedType)
            {
                string reason = menuFailure ??
                    "Menu compilation did not produce a matching emitter build model.";
                errors.Add(new ZoneBuildError(index, "buildData", reason));
                return new UnsupportedBuildRow(
                    index,
                    row.SerializedType,
                    row.RawHeader,
                    reason,
                    row.OriginalSerializedName);
            }

            return new OwnedDefinitionBuildRow(
                index,
                row.SerializedType,
                row.RawHeader,
                menuBuildData,
                row.OriginalSerializedName);
        }

        try
        {
            object? capturedDraft;
            object? authoredBaseline = null;
            object buildData;
            IReadOnlyList<AssetValidationIssue> validation;
            if (!save.TryGetDraftObject(row.Identity, out capturedDraft) || capturedDraft is null)
            {
                authoredBaseline = adapter.ImportAuthoredSnapshot(row);
                object draft = adapter.CreateDraft(authoredBaseline);
                validation = adapter.ValidateDraft(draft);
                // Weapon definitions retain their capture-wide identity graph
                // until the complete row set is detached below. Menu and
                // MenuFile rows were already normalized by MenuCompilationBatch.
                buildData = authoredBaseline switch
                {
                    WeaponAuthoredSnapshot weapon => weapon.Data,
                    _ => adapter.ExportBuildData(draft)
                };
            }
            else
            {
                validation = adapter.ValidateDraft(capturedDraft);
                buildData = adapter.ExportBuildData(capturedDraft);
            }

            foreach (AssetValidationIssue issue in validation.Where(issue => issue.Severity == AssetValidationSeverity.Error))
                errors.Add(new ZoneBuildError(index, issue.FieldPath, issue.Message));
            if (buildData is not IXAssetBuildData detached || detached.AssetType != row.SerializedType)
            {
                string reason = "Adapter exported a non-emitter build model or contradictory serialized type.";
                errors.Add(new ZoneBuildError(index, "buildData", reason));
                return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
            }
            return new OwnedDefinitionBuildRow(index, row.SerializedType, row.RawHeader, detached, row.OriginalSerializedName);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or OverflowException or ArgumentException)
        {
            string reason = exception.Message;
            errors.Add(new ZoneBuildError(index, "buildData", reason));
            return new UnsupportedBuildRow(index, row.SerializedType, row.RawHeader, reason, row.OriginalSerializedName);
        }
    }

    private static void PreserveDetachedSemanticGraphIdentity(IList<ZoneBuildRow> rows)
    {
        var weaponGraph = new WeaponGraphClone();
        for (int index = 0; index < rows.Count; index++)
        {
            if (rows[index] is not OwnedDefinitionBuildRow owned)
                continue;

            IXAssetBuildData? detached = owned.BuildData switch
            {
                WeaponBuildData weapon => weapon.Copy(weaponGraph),
                _ => null
            };
            if (detached is not null)
            {
                rows[index] = new OwnedDefinitionBuildRow(
                    owned.Index,
                    owned.AssetType,
                    owned.RawHeader,
                    detached,
                    owned.OriginalSerializedName);
            }
        }
    }

    private static void ValidateSoundLanguageCounts(
        DbHeader containerEnvelope,
        IEnumerable<ZoneBuildRow> rows,
        ICollection<ZoneBuildError> errors)
    {
        int expectedCount = checked((int)containerEnvelope.LanguageCount);
        foreach (OwnedDefinitionBuildRow row in rows
                     .OfType<OwnedDefinitionBuildRow>())
        {
            if (row.BuildData is not ISoundAliasListBuildData sound)
                continue;

            for (int aliasIndex = 0;
                 aliasIndex < sound.Aliases.Count;
                 aliasIndex++)
            {
                int actualCount = sound.Aliases[aliasIndex].SoundFiles.Count;
                if (actualCount == 0 || actualCount == expectedCount)
                    continue;

                errors.Add(new ZoneBuildError(
                    row.Index,
                    $"aliases[{aliasIndex}].soundFiles",
                    $"SoundFile array has {actualCount} records, but the " +
                    $"container language mask requires {expectedCount}."));
            }
        }
    }

    private static ResourceOutputPlan[] CreateResourceOutputPlans(TargetZoneSourceSnapshot source, IReadOnlyList<ZoneBuildRow> rows, List<ZoneBuildError> errors)
    {
        var plans = new List<ResourceOutputPlan>();
        foreach (OwnedDefinitionBuildRow row in rows.OfType<OwnedDefinitionBuildRow>().Where(value => value.AssetType == XAssetType.Image))
        {
            foreach (ResourceOutputRequirement requirement in
                     EnumerateResourceOutputRequirements(source, row, errors))
            {
                using FileStream stream = File.OpenRead(requirement.PhysicalPath);
                plans.Add(new ResourceOutputPlan(
                    row.Index,
                    requirement.FileName,
                    stream.Length,
                    Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return plans.GroupBy(plan => plan.FileName, StringComparer.Ordinal).Select(group => group.First()).ToArray();
    }

    private static IEnumerable<ResourceOutputRequirement>
        EnumerateResourceOutputRequirements(
            TargetZoneSourceSnapshot source,
            OwnedDefinitionBuildRow row,
            ICollection<ZoneBuildError> errors)
    {
        if (row.AssetType != XAssetType.Image ||
            row.BuildData is not IGfxImageBuildData image ||
            !image.StreamData.Any(value => value.HasStreamingData))
        {
            yield break;
        }

        if (image.ExternalStreamPackageIndices.Count == 0)
        {
            errors.Add(new ZoneBuildError(
                row.Index,
                "streamData",
                "Streamed GfxImage has no preserved external imagefile package identity."));
            yield break;
        }

        string sourceDirectory = Path.GetDirectoryName(source.PhysicalPath)
            ?? throw new InvalidDataException(
                "Opened source fastfile has no containing directory.");
        foreach (uint index in image.ExternalStreamPackageIndices)
        {
            if (index == 0)
            {
                errors.Add(new ZoneBuildError(
                    row.Index,
                    "streamData",
                    "A streamed GfxImage may not use file index zero because the candidate compiler does not append stream payloads to the fastfile."));
                continue;
            }

            string fileName = $"imagefile{index}.pak";
            string path = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(path))
            {
                errors.Add(new ZoneBuildError(
                    row.Index,
                    "streamData",
                    $"Required preserved image sidecar '{fileName}' is missing beside the opened source fastfile."));
                continue;
            }

            yield return new ResourceOutputRequirement(fileName, path);
        }
    }

    private readonly record struct ResourceOutputRequirement(
        string FileName,
        string PhysicalPath);
}
