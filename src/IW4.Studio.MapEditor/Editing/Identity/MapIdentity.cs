using System.Security.Cryptography;
using System.Text;

namespace IW4.Studio.MapEditor.Editing.Identity;

public readonly record struct MapDocumentId
{
    public MapDocumentId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}

public readonly record struct MapObjectId
{
    public MapObjectId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}

public readonly record struct SourceBindingId
{
    public SourceBindingId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// Creates repeatable editor identities from compiled-map semantic identity.
/// The opened document GUID and runtime pool addresses are deliberately absent.
/// </summary>
public static class DeterministicMapIdentity
{
    private const string Domain = "IW4.Studio.MapEditor/v1";

    public static MapDocumentId Document(
        string mapIdentity,
        string baselineDigest) =>
        new(CreateGuid("document", mapIdentity, baselineDigest));

    public static MapObjectId Object(
        string mapIdentity,
        string assetType,
        string assetName,
        string semanticRole,
        int sourceOrdinal)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));

        return new MapObjectId(CreateGuid(
            "object",
            mapIdentity,
            assetType,
            assetName,
            semanticRole,
            sourceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static SourceBindingId Binding(
        string mapIdentity,
        string assetType,
        string assetName,
        string fieldPath,
        int? sourceOrdinal) =>
        new(CreateGuid(
            "binding",
            mapIdentity,
            assetType,
            assetName,
            fieldPath,
            sourceOrdinal?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "-"));

    private static Guid CreateGuid(params string[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Deterministic identity components cannot be empty.",
                nameof(parts));
        }

        string input = string.Join(
            '\u001f',
            parts.Prepend(Domain).Select(part => $"{part.Length}:{part}"));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // RFC 9562 UUID version 8: application-defined deterministic payload.
        digest[7] = (byte)((digest[7] & 0x0f) | 0x80);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        return new Guid(digest.AsSpan(0, 16));
    }
}
