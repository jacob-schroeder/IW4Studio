using System.Threading;
using IW4.FastFiles.Strings;

namespace IW4.FastFiles.Emitters.Emission;

/// <summary>
/// Resolves a detached script-string value to the zone-local index assigned
/// by the linker.  Raw local indices are accepted only by the imported-zone
/// compatibility policy and only when their retained table entry contains the
/// same value; canonical links never silently preserve a source-local index.
/// </summary>
public sealed class ScriptStringIndexResolver
{
    private readonly IReadOnlyDictionary<string, ushort> _indices;
    private readonly IReadOnlyList<string?> _values;
    private readonly bool _allowImportedRawIndex;

    public ScriptStringIndexResolver(
        IReadOnlyDictionary<string, ushort> indices,
        IReadOnlyList<string?> values,
        bool allowImportedRawIndex)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(values);
        _indices = new Dictionary<string, ushort>(indices, StringComparer.Ordinal);
        _values = Array.AsReadOnly(values.ToArray());
        _allowImportedRawIndex = allowImportedRawIndex;
    }

    public ushort Resolve(ScriptStringReference reference, string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        return Resolve(reference.RawLocalIndex, reference.Text, fieldPath);
    }

    public ushort Resolve(ushort rawLocalIndex, string? value, string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        // PS3 ZoneScriptStringTable.Resolve treats serialized zero as null
        // before consulting the table. It can therefore never name a
        // non-null value, even if an imported table happens to carry one in
        // slot zero.
        if (rawLocalIndex == 0 && value is null)
            return 0;
        if (_allowImportedRawIndex &&
            rawLocalIndex != 0 &&
            rawLocalIndex < _values.Count &&
            value is not null &&
            string.Equals(_values[rawLocalIndex], value, StringComparison.Ordinal))
        {
            return rawLocalIndex;
        }
        if (value is not null && _indices.TryGetValue(value, out ushort index))
        {
            if (index == 0)
            {
                throw new InvalidDataException(
                    $"Script-string field '{fieldPath}' cannot bind non-null value '{value}' to reserved index zero.");
            }
            return index;
        }
        throw new InvalidDataException(
            $"Script-string field '{fieldPath}' cannot be rebound: " +
            (value is null
                ? "it has no detached string value and its imported index cannot be retained."
                : $"value '{value}' is absent from the linker script-string table."));
    }

    /// <summary>
    /// Compatibility escape hatch for legacy detached payloads which lost
    /// their script-string text during import. It is deliberately unavailable
    /// to canonical links so such a model cannot silently carry a source-local
    /// number into a greenfield zone.
    /// </summary>
    public ushort ResolveOpaqueImportedRaw(ushort rawLocalIndex, string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        if (rawLocalIndex == 0)
            return 0;
        if (_allowImportedRawIndex &&
            rawLocalIndex < _values.Count &&
            _values[rawLocalIndex] is not null)
        {
            return rawLocalIndex;
        }
        throw new InvalidDataException(
            $"Script-string field '{fieldPath}' has only nonzero imported raw index " +
            $"0x{rawLocalIndex:X4} and " +
            (_allowImportedRawIndex
                ? "does not resolve to a non-null entry in the retained table."
                : "cannot be emitted canonically."));
    }
}

/// <summary>
/// Narrow emission scope that supplies the linker's script-string resolver to
/// existing body emitters without adding Studio or loader dependencies to the
/// emitter contracts.  Tests and legacy direct-emitter callers retain their
/// explicitly supplied raw indices when no linker scope is active.
/// </summary>
public static class ScriptStringEmissionScope
{
    private static readonly AsyncLocal<ScriptStringIndexResolver?> CurrentResolver = new();

    public static IDisposable Push(ScriptStringIndexResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ScriptStringIndexResolver? previous = CurrentResolver.Value;
        CurrentResolver.Value = resolver;
        return new Scope(previous);
    }

    public static ushort Resolve(ScriptStringReference reference, string fieldPath) =>
        CurrentResolver.Value?.Resolve(reference, fieldPath) ?? reference.RawLocalIndex;

    public static ushort Resolve(ushort rawLocalIndex, string? value, string fieldPath) =>
        CurrentResolver.Value?.Resolve(rawLocalIndex, value, fieldPath) ?? rawLocalIndex;

    public static ushort ResolveOpaqueImportedRaw(ushort rawLocalIndex, string fieldPath) =>
        CurrentResolver.Value?.ResolveOpaqueImportedRaw(rawLocalIndex, fieldPath) ?? rawLocalIndex;

    private sealed class Scope(ScriptStringIndexResolver? previous) : IDisposable
    {
        private readonly ScriptStringIndexResolver? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            CurrentResolver.Value = _previous;
            _disposed = true;
        }
    }
}
