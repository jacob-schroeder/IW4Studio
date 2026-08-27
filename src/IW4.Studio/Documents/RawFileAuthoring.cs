using IW4.AssetExchange.RawFile;
using IW4.Assets.Assets.RawFile;

namespace IW4.Studio.Documents;

public enum RawFilePayloadMode
{
    UncompressedText,
    UncompressedBinary,
    CompressedPayload
}

/// <summary>Detached mutable RawFile state; byte accessors never expose its backing array.</summary>
public sealed class RawFileDraft
{
    private byte[] _serializedPayload;

    public RawFileDraft(RawFileAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        OriginalName = asset.Name ?? throw new InvalidDataException(
            "RawFile has no name.");
        Mode = asset.CompressedLen != 0
            ? RawFilePayloadMode.CompressedPayload
            : RawFileContentClassifier.Classify(OriginalName, Content(asset)).IsTextual
                ? RawFilePayloadMode.UncompressedText
                : RawFilePayloadMode.UncompressedBinary;
        HasBuffer = asset.Buffer is not null;
        CompressedLength = asset.CompressedLen;
        UncompressedLength = asset.Len;
        _serializedPayload = asset.Buffer?.ToArray() ?? [];
    }

    private RawFileDraft(RawFileDraft source)
    {
        OriginalName = source.OriginalName;
        Mode = source.Mode;
        HasBuffer = source.HasBuffer;
        CompressedLength = source.CompressedLength;
        UncompressedLength = source.UncompressedLength;
        _serializedPayload = source._serializedPayload.ToArray();
    }

    public string OriginalName { get; }
    public RawFilePayloadMode Mode { get; private set; }
    public bool HasBuffer { get; private set; }
    public int CompressedLength { get; private set; }
    public int UncompressedLength { get; private set; }
    internal byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();
    private byte[] GetContentCopy() => Mode == RawFilePayloadMode.CompressedPayload
        ? _serializedPayload.ToArray()
        : Content(this);

    public byte[] GetLogicalContentCopy() =>
        Mode == RawFilePayloadMode.CompressedPayload
            ? RawFileContentCodec.DecodeCompressed(
                _serializedPayload,
                UncompressedLength)
            : GetContentCopy();

    public RawFileContentClassification GetContentClassification() =>
        RawFileContentClassifier.Classify(OriginalName, GetLogicalContentCopy());
    public void ReplaceCanonicalContent(ReadOnlySpan<byte> content)
    {
        Mode = RawFileContentClassifier.Classify(OriginalName, content).IsTextual
            ? RawFilePayloadMode.UncompressedText
            : RawFilePayloadMode.UncompressedBinary;
        ReplaceUncompressedContent(content);
    }

    public void ReplaceCanonicalText(
        string text,
        RawFileTextEncoding preferredEncoding) =>
        ReplaceCanonicalContent(
            RawFileContentClassifier.EncodeText(text, preferredEncoding));

    public void ReplaceBinaryContent(ReadOnlySpan<byte> content)
    {
        Mode = RawFilePayloadMode.UncompressedBinary;
        ReplaceUncompressedContent(content);
    }
    public void ClearBuffer()
    {
        if (Mode == RawFilePayloadMode.CompressedPayload)
            throw new InvalidOperationException(
                "Compressed RawFiles require a payload.");
        HasBuffer = false;
        CompressedLength = 0;
        UncompressedLength = 0;
        _serializedPayload = [];
    }

    internal RawFileDraft Clone() => new(this);

    internal RawFileAsset ToAsset() => new()
    {
        Name = OriginalName,
        Buffer = HasBuffer ? _serializedPayload.ToArray() : null,
        CompressedLen = CompressedLength,
        Len = UncompressedLength
    };

    private void ReplaceUncompressedContent(ReadOnlySpan<byte> content)
    {
        HasBuffer = true;
        CompressedLength = 0;
        UncompressedLength = content.Length;
        _serializedPayload = new byte[checked(content.Length + 1)];
        content.CopyTo(_serializedPayload);
    }

    private static byte[] Content(RawFileAsset asset) =>
        TolerantUncompressedContent(asset.Buffer, asset.Len);

    private static byte[] Content(RawFileDraft draft) =>
        TolerantUncompressedContent(
            draft.HasBuffer ? draft._serializedPayload : null,
            draft.UncompressedLength);

    private static byte[] TolerantUncompressedContent(
        byte[]? serializedPayload,
        int declaredContentLength) => serializedPayload is null
            ? []
            : serializedPayload.Take(
                Math.Min(declaredContentLength, serializedPayload.Length)).ToArray();
}

public sealed class RawFileReadOnlySnapshot
{
    private RawFileReadOnlySnapshot(RawFileDraft draft) => Draft = draft;
    private RawFileDraft Draft { get; }
    public string OriginalName => Draft.OriginalName;
    public RawFilePayloadMode Mode => Draft.Mode;
    public bool HasBuffer => Draft.HasBuffer;
    public int CompressedLength => Draft.CompressedLength;
    public int UncompressedLength => Draft.UncompressedLength;
    public byte[] GetLogicalContentCopy() => Draft.GetLogicalContentCopy();
    public RawFileContentClassification GetContentClassification() =>
        Draft.GetContentClassification();

    public static RawFileReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        if (editorSession.Definition is not RawFileAsset definition)
            throw new InvalidDataException("The selected provider is not a RawFile definition.");
        return new RawFileReadOnlySnapshot(new RawFileDraft(definition));
    }
}
