using System.Text;
using IW4.Assets.Assets.RawFile;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Explicit RawFile payload representation. Uncompressed bodies are stored as
/// content plus one terminal null; compressed bodies are opaque bytes and
/// carry their separately declared uncompressed length.
/// </summary>
public enum RawFilePayloadMode
{
    UncompressedText,
    UncompressedBinary,
    CompressedPayload
}

/// <summary>
/// Immutable authored RawFile baseline reconstructed from the selected
/// target's detached source bytes. It does not retain a runtime asset, pool
/// provider, XZone block, or mutable payload array.
/// </summary>
public sealed class RawFileAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly byte[] _serializedPayload;

    internal RawFileAuthoredSnapshot(
        string originalName,
        string? normalizedIdentity,
        RawFilePayloadMode mode,
        bool hasBuffer,
        int compressedLength,
        int uncompressedLength,
        ReadOnlySpan<byte> serializedPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        OriginalName = originalName;
        NormalizedIdentity = normalizedIdentity;
        Mode = mode;
        HasBuffer = hasBuffer;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _serializedPayload = serializedPayload.ToArray();
    }

    public string OriginalName { get; }

    public XAssetType AssetType => XAssetType.RawFile;

    /// <summary>Stable lookup identity retained separately from source spelling.</summary>
    public string? NormalizedIdentity { get; }

    public RawFilePayloadMode Mode { get; }

    public bool HasBuffer { get; }

    public int CompressedLength { get; }

    public int UncompressedLength { get; }

    /// <summary>Returns a detached copy, never the adapter-owned byte array.</summary>
    public byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();

    /// <summary>
    /// Returns uncompressed content without its required terminal null, or
    /// the exact opaque bytes for compressed payloads.
    /// </summary>
    public byte[] GetContentCopy() => RawFilePayloadRules.GetContentCopy(
        Mode,
        HasBuffer,
        _serializedPayload);

    internal static RawFileAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SerializedType != XAssetType.RawFile ||
            source.State != TargetZoneRowSourceState.Definition ||
            source.AuthoredDefinition?.SemanticSnapshot is not RawFileAuthoredSnapshot snapshot)
        {
            throw new InvalidDataException(
                "RawFile editing requires a capture-time detached semantic snapshot; source-fragment replay is not an authoring input.");
        }

        return new RawFileAuthoredSnapshot(
            snapshot.OriginalName,
            snapshot.NormalizedIdentity,
            snapshot.Mode,
            snapshot.HasBuffer,
            snapshot.CompressedLength,
            snapshot.UncompressedLength,
            snapshot._serializedPayload);
    }

    internal static RawFileAuthoredSnapshot FromLoaded(RawFileAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = asset.Name ?? throw new InvalidDataException("Loaded RawFile has no logical name.");
        byte[] payload = asset.Buffer?.ToArray() ?? [];
        RawFilePayloadMode mode;
        if (asset.CompressedLen != 0)
        {
            mode = RawFilePayloadMode.CompressedPayload;
        }
        else
        {
            byte[] logicalContent = RawFilePayloadRules.GetContentCopy(
                RawFilePayloadMode.UncompressedBinary,
                asset.Buffer is not null,
                payload);
            mode = RawFileContentClassifier.Classify(name, logicalContent).IsTextual
                ? RawFilePayloadMode.UncompressedText
                : RawFilePayloadMode.UncompressedBinary;
        }
        return new RawFileAuthoredSnapshot(
            name,
            name,
            mode,
            asset.Buffer is not null,
            asset.CompressedLen,
            asset.Len,
            payload);
    }
}

/// <summary>
/// Detached mutable RawFile editor state. Public byte accessors always copy;
/// mutation is limited to explicit payload replacement operations.
/// </summary>
public sealed class RawFileDraft
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private byte[] _serializedPayload;

    public RawFileDraft(
        string originalName,
        string? normalizedIdentity,
        RawFilePayloadMode mode,
        bool hasBuffer,
        int compressedLength,
        int uncompressedLength,
        ReadOnlySpan<byte> serializedPayload)
        : this(
            originalName,
            normalizedIdentity,
            mode,
            hasBuffer,
            compressedLength,
            uncompressedLength,
            serializedPayload,
            preserveOpaqueCompressedPayload: false)
    {
    }

    private RawFileDraft(
        string originalName,
        string? normalizedIdentity,
        RawFilePayloadMode mode,
        bool hasBuffer,
        int compressedLength,
        int uncompressedLength,
        ReadOnlySpan<byte> serializedPayload,
        bool preserveOpaqueCompressedPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        OriginalName = originalName;
        NormalizedIdentity = normalizedIdentity;
        Mode = mode;
        HasBuffer = hasBuffer;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _serializedPayload = serializedPayload.ToArray();
        PreserveOpaqueCompressedPayload =
            preserveOpaqueCompressedPayload &&
            mode == RawFilePayloadMode.CompressedPayload;
    }

    public string OriginalName { get; }

    public string? NormalizedIdentity { get; }

    public RawFilePayloadMode Mode { get; private set; }

    public bool HasBuffer { get; private set; }

    public int CompressedLength { get; private set; }

    public int UncompressedLength { get; private set; }

    internal bool PreserveOpaqueCompressedPayload { get; private set; }

    public byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();

    public byte[] GetContentCopy() => RawFilePayloadRules.GetContentCopy(
        Mode,
        HasBuffer,
        _serializedPayload);

    /// <summary>
    /// Returns authored logical content. Compressed data is inflated only
    /// through the strict zlib codec and must match its declared length.
    /// </summary>
    public byte[] GetLogicalContentCopy() => Mode == RawFilePayloadMode.CompressedPayload
        ? RawFileContentCodec.DecodeCompressed(_serializedPayload, UncompressedLength)
        : GetContentCopy();

    public RawFileContentClassification GetContentClassification() =>
        RawFileContentClassifier.Classify(OriginalName, GetLogicalContentCopy());

    /// <summary>Performs an explicit representation conversion for a logical
    /// edit. This is the only path that converts compressed imports into a
    /// canonical output form.</summary>
    public void ReplaceLogicalContent(ReadOnlySpan<byte> content, RawFileCanonicalContentPolicy policy)
    {
        RawFileEncodedPayload encoded = RawFileContentCodec.Encode(content, policy);
        ApplyEncodedPayload(encoded);
    }

    /// <summary>
    /// Replaces logical editor content using name/content classification and
    /// the canonical binary/text storage policy.
    /// </summary>
    public void ReplaceCanonicalContent(ReadOnlySpan<byte> content)
    {
        RawFileContentClassification classification =
            RawFileContentClassifier.Classify(OriginalName, content);
        RawFileEncodedPayload encoded = RawFileContentCodec.EncodeCanonical(
            content,
            classification.Kind);
        ApplyEncodedPayload(encoded);
    }

    public void ReplaceCanonicalText(
        string text,
        RawFileTextEncoding preferredEncoding)
    {
        byte[] content = RawFileContentClassifier.EncodeText(
            text,
            preferredEncoding);
        ReplaceCanonicalContent(content);
    }

    private void ApplyEncodedPayload(RawFileEncodedPayload encoded)
    {
        Mode = encoded.Mode;
        HasBuffer = true;
        CompressedLength = encoded.CompressedLength;
        UncompressedLength = encoded.UncompressedLength;
        _serializedPayload = encoded.GetSerializedPayloadCopy();
        PreserveOpaqueCompressedPayload = false;
    }

    /// <summary>Replaces uncompressed content and writes the required terminal null.</summary>
    public void ReplaceUncompressedContent(ReadOnlySpan<byte> content)
    {
        if (Mode is RawFilePayloadMode.CompressedPayload)
        {
            throw new InvalidOperationException(
                "Compressed RawFile payloads cannot be reinterpreted as uncompressed content. Create an explicit conversion in a later codec-backed workflow.");
        }

        HasBuffer = true;
        CompressedLength = 0;
        UncompressedLength = content.Length;
        _serializedPayload = new byte[checked(content.Length + 1)];
        content.CopyTo(_serializedPayload);
        _serializedPayload[^1] = 0;
        PreserveOpaqueCompressedPayload = false;
    }

    /// <summary>
    /// Replaces the logical content as a binary buffer, intentionally moving
    /// any prior storage representation to uncompressed binary bytes.
    /// </summary>
    public void ReplaceBinaryContent(ReadOnlySpan<byte> content)
    {
        Mode = RawFilePayloadMode.UncompressedBinary;
        ReplaceUncompressedContent(content);
    }

    /// <summary>Replaces text content using strict UTF-8 and a terminal null.</summary>
    public void ReplaceText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Mode != RawFilePayloadMode.UncompressedText)
        {
            throw new InvalidOperationException(
                "Only an uncompressed text RawFile can accept text replacement without an explicit conversion.");
        }
        if (text.IndexOf('\0') >= 0)
            throw new ArgumentException("RawFile text cannot contain an embedded terminal null.", nameof(text));

        ReplaceUncompressedContent(StrictUtf8.GetBytes(text));
    }

    /// <summary>
    /// Replaces compressed bytes exactly after proving that they form a valid
    /// zlib stream whose logical size matches <paramref name="uncompressedLength"/>.
    /// The bytes are not recompressed.
    /// </summary>
    public void ReplaceCompressedPayload(ReadOnlySpan<byte> payload, int uncompressedLength)
    {
        if (Mode != RawFilePayloadMode.CompressedPayload)
        {
            throw new InvalidOperationException(
                "Uncompressed RawFile content cannot be converted to compressed bytes without an explicit codec-backed conversion workflow.");
        }
        if (payload.Length == 0)
            throw new ArgumentException("A compressed RawFile payload must contain at least one byte.", nameof(payload));
        if (uncompressedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(uncompressedLength));

        _ = RawFileContentCodec.DecodeCompressed(payload, uncompressedLength);

        HasBuffer = true;
        CompressedLength = payload.Length;
        UncompressedLength = uncompressedLength;
        _serializedPayload = payload.ToArray();
        PreserveOpaqueCompressedPayload = false;
    }

    /// <summary>Represents the nullable empty-buffer form.</summary>
    public void ClearBuffer()
    {
        if (Mode == RawFilePayloadMode.CompressedPayload)
        {
            throw new InvalidOperationException(
                "A compressed RawFile cannot have a null payload because compressedLen would no longer describe opaque bytes.");
        }

        HasBuffer = false;
        CompressedLength = 0;
        UncompressedLength = 0;
        _serializedPayload = [];
        PreserveOpaqueCompressedPayload = false;
    }

    internal RawFileDraft Clone() => new(
        OriginalName,
        NormalizedIdentity,
        Mode,
        HasBuffer,
        CompressedLength,
        UncompressedLength,
        _serializedPayload,
        PreserveOpaqueCompressedPayload);

    internal static RawFileDraft FromSnapshot(RawFileAuthoredSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new RawFileDraft(
            snapshot.OriginalName,
            snapshot.NormalizedIdentity,
            snapshot.Mode,
            snapshot.HasBuffer,
            snapshot.CompressedLength,
            snapshot.UncompressedLength,
            snapshot.GetSerializedPayloadCopy(),
            preserveOpaqueCompressedPayload: true);
    }
}

/// <summary>
/// Immutable detached build value consumed by the RawFile body emitter.
/// </summary>
public sealed class RawFileBuildData : IRawFileBuildData
{
    private readonly byte[] _serializedPayload;

    internal RawFileBuildData(RawFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        OriginalName = draft.OriginalName;
        NormalizedIdentity = draft.NormalizedIdentity;
        Mode = draft.Mode;
        HasBuffer = draft.HasBuffer;
        CompressedLength = draft.CompressedLength;
        UncompressedLength = draft.UncompressedLength;
        PreserveOpaqueCompressedPayload = draft.PreserveOpaqueCompressedPayload;
        _serializedPayload = draft.GetSerializedPayloadCopy();
    }

    public string OriginalName { get; }
    public XAssetType AssetType => XAssetType.RawFile;
    public string? NormalizedIdentity { get; }
    public RawFilePayloadMode Mode { get; }
    public bool HasBuffer { get; }
    public int CompressedLength { get; }
    public int UncompressedLength { get; }
    public bool PreserveOpaqueCompressedPayload { get; }
    public byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();
}

/// <summary>
/// Detached read-only copy of a currently resolved RawFile provider. It is a
/// viewer projection only: target ownership stays in the catalog and the
/// source runtime asset is never exposed or mutated.
/// </summary>
public sealed class RawFileReadOnlySnapshot
{
    private readonly byte[] _serializedPayload;

    private RawFileReadOnlySnapshot(
        string originalName,
        RawFilePayloadMode mode,
        bool hasBuffer,
        int compressedLength,
        int uncompressedLength,
        ReadOnlySpan<byte> serializedPayload)
    {
        OriginalName = originalName;
        Mode = mode;
        HasBuffer = hasBuffer;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _serializedPayload = serializedPayload.ToArray();
    }

    public string OriginalName { get; }
    public RawFilePayloadMode Mode { get; }
    public bool HasBuffer { get; }
    public int CompressedLength { get; }
    public int UncompressedLength { get; }
    public byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();
    public byte[] GetContentCopy() => RawFilePayloadRules.GetContentCopy(
        Mode,
        HasBuffer,
        _serializedPayload);
    public byte[] GetLogicalContentCopy() => Mode == RawFilePayloadMode.CompressedPayload
        ? RawFileContentCodec.DecodeCompressed(_serializedPayload, UncompressedLength)
        : GetContentCopy();
    public RawFileContentClassification GetContentClassification() =>
        RawFileContentClassifier.Classify(OriginalName, GetLogicalContentCopy());

    public static RawFileReadOnlySnapshot CaptureResolvedProvider(AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        WorkspaceAssetCatalogEntry entry = editorSession.Entry;
        WorkspaceAssetResolvedProvider provider = entry.ResolvedProvider
            ?? throw new InvalidDataException(
                "RawFile read-only viewing requires a catalog-resolved full-definition provider.");
        XAssetProviderContribution contribution = editorSession.Workspace.Runtime.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .SingleOrDefault(candidate => candidate.Id == provider.ProviderId)
            ?? throw new InvalidDataException(
                "The catalog-resolved RawFile provider is no longer present in this workspace runtime.");
        if (contribution.AssetType != XAssetType.RawFile ||
            contribution.IsReferencePlaceholder ||
            contribution.Owner != provider.Zone.Handle ||
            contribution.Asset is not RawFileAsset rawFile)
        {
            throw new InvalidDataException(
                "The catalog-resolved provider no longer matches a readable RawFile full definition.");
        }

        string name = rawFile.Name ?? contribution.Name;
        byte[] payload = rawFile.Buffer?.ToArray() ?? [];
        RawFilePayloadMode mode;
        if (rawFile.CompressedLen != 0)
        {
            mode = RawFilePayloadMode.CompressedPayload;
        }
        else
        {
            byte[] logicalContent = RawFilePayloadRules.GetContentCopy(
                RawFilePayloadMode.UncompressedBinary,
                rawFile.Buffer is not null,
                payload);
            mode = RawFileContentClassifier.Classify(name, logicalContent).IsTextual
                ? RawFilePayloadMode.UncompressedText
                : RawFilePayloadMode.UncompressedBinary;
        }
        return new RawFileReadOnlySnapshot(
            name,
            mode,
            rawFile.Buffer is not null,
            rawFile.CompressedLen,
            rawFile.Len,
            payload);
    }
}

/// <summary>Registered RawFile backend adapter; compiler support remains off.</summary>
public sealed class RawFileAuthoringAdapter
    : AssetAuthoringAdapter<RawFileAuthoredSnapshot, RawFileDraft, RawFileBuildData>
{
    public override XAssetType AssetType => XAssetType.RawFile;

    public override RawFileAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) =>
        RawFileAuthoredSnapshot.Import(source);

    public override RawFileDraft CreateDraft(RawFileAuthoredSnapshot authoredSnapshot) =>
        RawFileDraft.FromSnapshot(authoredSnapshot);

    public override RawFileDraft CloneDraft(RawFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Clone();
    }

    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(RawFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return RawFilePayloadRules.Validate(
            draft.OriginalName,
            draft.Mode,
            draft.HasBuffer,
            draft.CompressedLength,
            draft.UncompressedLength,
            draft.GetSerializedPayloadCopy(),
            draft.PreserveOpaqueCompressedPayload);
    }

    public override bool SemanticallyEquals(RawFileDraft baseline, RawFileDraft current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        return string.Equals(baseline.OriginalName, current.OriginalName, StringComparison.Ordinal) &&
               string.Equals(baseline.NormalizedIdentity, current.NormalizedIdentity, StringComparison.Ordinal) &&
               baseline.Mode == current.Mode &&
               baseline.HasBuffer == current.HasBuffer &&
               baseline.CompressedLength == current.CompressedLength &&
               baseline.UncompressedLength == current.UncompressedLength &&
               baseline.GetSerializedPayloadCopy().SequenceEqual(current.GetSerializedPayloadCopy());
    }

    public override RawFileBuildData ExportBuildData(RawFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        AssetValidationIssue[] errors = ValidateDraft(draft)
            .Where(issue => issue.Severity == AssetValidationSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("RawFile draft has validation errors and cannot produce build data.");

        return new RawFileBuildData(draft);
    }
}

internal static class RawFilePayloadRules
{
    public static IReadOnlyList<AssetValidationIssue> Validate(
        string originalName,
        RawFilePayloadMode mode,
        bool hasBuffer,
        int compressedLength,
        int uncompressedLength,
        ReadOnlySpan<byte> serializedPayload,
        bool preserveOpaqueCompressedPayload)
    {
        var issues = new List<AssetValidationIssue>();
        if (string.IsNullOrWhiteSpace(originalName))
            issues.Add(Error("name", "RawFile requires its original serialized name."));
        if (compressedLength < 0)
            issues.Add(Error("compressedLen", "compressedLen cannot be negative."));
        if (uncompressedLength < 0)
            issues.Add(Error("len", "len cannot be negative."));

        if (mode == RawFilePayloadMode.CompressedPayload)
        {
            if (compressedLength <= 0)
                issues.Add(Error("compressedLen", "Compressed payload mode requires compressedLen greater than zero."));
            if (!hasBuffer)
                issues.Add(Error("buffer", "Compressed payload mode requires a present buffer."));
            if (compressedLength >= 0 && serializedPayload.Length != compressedLength)
            {
                issues.Add(Error(
                    "buffer",
                    "Compressed RawFile bytes must exactly match compressedLen; no implicit recompression is available."));
            }
            else if (!preserveOpaqueCompressedPayload &&
                     compressedLength > 0 &&
                     uncompressedLength >= 0 &&
                     hasBuffer)
            {
                try
                {
                    _ = RawFileContentCodec.DecodeCompressed(serializedPayload, uncompressedLength);
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(Error("buffer", exception.Message));
                }
            }
        }
        else
        {
            if (compressedLength != 0)
                issues.Add(Error("compressedLen", "Uncompressed RawFile modes require compressedLen to be zero."));
            if (!hasBuffer)
            {
                if (uncompressedLength != 0)
                    issues.Add(Error("len", "A null uncompressed buffer requires len to be zero."));
                if (serializedPayload.Length != 0)
                    issues.Add(Error("buffer", "A null RawFile buffer cannot carry serialized bytes."));
            }
            else if (uncompressedLength >= 0)
            {
                int expectedLength;
                try
                {
                    expectedLength = checked(uncompressedLength + 1);
                }
                catch (OverflowException)
                {
                    issues.Add(Error("len", "len is too large to account for the required terminal null."));
                    expectedLength = -1;
                }

                if (expectedLength >= 0 && serializedPayload.Length != expectedLength)
                {
                    issues.Add(Error(
                        "buffer",
                        "Uncompressed RawFile bytes must be exactly len + 1, including the terminal null."));
                }
                if (serializedPayload.Length == 0 || serializedPayload[^1] != 0)
                    issues.Add(Error("buffer", "Uncompressed RawFile buffers must end in one terminal null byte."));
            }

            if (mode == RawFilePayloadMode.UncompressedText &&
                hasBuffer &&
                serializedPayload.Length > 0)
            {
                if (serializedPayload[..^1].Contains((byte)0))
                    issues.Add(Error("buffer", "Text-mode RawFile content cannot contain an embedded null byte."));
                else
                {
                    RawFileContentClassification classification =
                        RawFileContentClassifier.Classify(
                            originalName,
                            serializedPayload[..^1]);
                    if (classification.TextEncoding is null)
                    {
                        issues.Add(Error(
                            "buffer",
                            "Text-mode RawFile content must be valid UTF-8 or Windows-1252 text before its terminal null."));
                    }
                }
            }
        }

        return Array.AsReadOnly(issues.ToArray());
    }

    public static byte[] GetContentCopy(
        RawFilePayloadMode mode,
        bool hasBuffer,
        ReadOnlySpan<byte> serializedPayload)
    {
        if (mode == RawFilePayloadMode.CompressedPayload || !hasBuffer)
            return serializedPayload.ToArray();

        return serializedPayload.Length > 0 && serializedPayload[^1] == 0
            ? serializedPayload[..^1].ToArray()
            : serializedPayload.ToArray();
    }

    private static AssetValidationIssue Error(string field, string message) =>
        new(field, message, AssetValidationSeverity.Error);
}
