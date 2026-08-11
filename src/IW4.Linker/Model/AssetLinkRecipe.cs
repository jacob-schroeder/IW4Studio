using System.Text;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen schema data and native traversal order for one provider definition.
/// Recipes contain no loader, runtime, or source-address identity.
/// </summary>
internal abstract class AssetLinkRecipe
{
    private readonly byte[] _nameBytes;

    protected AssetLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        bool requireReferencePlaceholder = false)
    {
        if (string.IsNullOrEmpty(originalSerializedName))
            throw new InvalidDataException("Asset name cannot be null or empty.");
        if (originalSerializedName.Contains('\0'))
            throw new InvalidDataException("Asset name cannot contain NUL.");
        if (originalSerializedName.Any(character => character > byte.MaxValue))
            throw new InvalidDataException("Asset name must be representable as Latin-1.");

        AssetKey wireKey = AssetKey.FromWireName(
            key.Family,
            originalSerializedName);
        if (wireKey != key)
        {
            throw new InvalidDataException(
                $"Asset name '{originalSerializedName}' does not normalize to {key}.");
        }

        IsReferencePlaceholder = originalSerializedName[0] == ',';
        if (requireReferencePlaceholder && !IsReferencePlaceholder)
        {
            throw new InvalidDataException(
                $"{key.Family} providers are currently supported only as comma-prefixed references.");
        }

        OriginalSerializedName = originalSerializedName;
        _nameBytes = EncodeCString(originalSerializedName);
    }

    public string OriginalSerializedName { get; }
    public bool IsReferencePlaceholder { get; }

    public virtual IReadOnlyList<AssetDependency> Dependencies =>
        Array.Empty<AssetDependency>();

    public abstract void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency);

    protected void EmitName(ZoneEmissionWriter output)
    {
        EmitFrozenXString(output, _nameBytes);
    }

    protected static byte[]? FreezeOptionalXString(
        string? value,
        string fieldPath)
    {
        if (value is null)
            return null;
        if (value.Contains('\0'))
            throw new InvalidDataException($"{fieldPath} cannot contain NUL.");
        if (value.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{fieldPath} must be representable as Latin-1.");
        }

        return EncodeCString(value);
    }

    protected static int XStringSourcePointer(byte[]? frozenValue) =>
        frozenValue is null ? 0 : -1;

    protected static void EmitFrozenXString(
        ZoneEmissionWriter output,
        byte[]? frozenValue)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (frozenValue is null)
            return;

        output.Allocate(
            XFileBlockType.LARGE,
            frozenValue.Length,
            alignment: 1);
        output.WriteBytes(frozenValue);
    }

    private static byte[] EncodeCString(string value)
    {
        byte[] result = new byte[checked(value.Length + 1)];
        int written = Encoding.Latin1.GetBytes(value, result);
        if (written != value.Length)
            throw new InvalidDataException("Asset name could not be encoded as Latin-1.");
        return result;
    }
}

internal readonly record struct AssetDependency(
    AssetKey Key,
    XAssetType SerializedType,
    string FieldPath);
