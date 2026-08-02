using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Keeps managed semantic nodes associated with stable block-stream addresses
/// for one load. A packed direct pointer can then reuse a previously loaded
/// node without consuming new source bytes.
/// </summary>
internal sealed class TypedMaterializationRegistry
{
    private readonly Dictionary<(XBlockAddress Address, Type SemanticType, string View), object> _values = new();

    public T Register<T>(XBlockAddress address, T value, string targetName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var key = (address, typeof(T), string.Empty);
        if (_values.TryGetValue(key, out object? existing))
        {
            if (!ReferenceEquals(existing, value))
            {
                throw new InvalidDataException(
                    $"{targetName} at {address} was materialized more than once as {typeof(T).Name}.");
            }

            return (T)existing;
        }

        _values.Add(key, value);
        return value;
    }

    public T ResolveDirectOffset<T>(XPointerReference pointer, string targetName)
        where T : class
    {
        if (pointer.Type != PointerType.Offset ||
            pointer.ResolutionMode != XPointerResolutionMode.Direct ||
            pointer.PackedAddress is not { } address)
        {
            throw new InvalidDataException(
                $"{targetName} pointer 0x{unchecked((uint)pointer.Raw):X8} is not a packed direct pointer.");
        }

        if (_values.TryGetValue((address, typeof(T), string.Empty), out object? value) && value is T typed)
            return typed;

        throw new InvalidDataException(
            $"Packed {targetName} target {address} has no earlier materialized semantic owner.");
    }

    public bool TryGet<T>(XBlockAddress address, out T? value)
        where T : class
    {
        if (_values.TryGetValue((address, typeof(T), string.Empty), out object? stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    public T RegisterView<T>(
        XBlockAddress address,
        string view,
        T value,
        string targetName)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(view);
        ArgumentNullException.ThrowIfNull(value);
        var key = (address, typeof(T), view);
        if (_values.TryGetValue(key, out object? existing))
        {
            if (!ReferenceEquals(existing, value))
            {
                throw new InvalidDataException(
                    $"{targetName} view '{view}' at {address} was materialized more than once as {typeof(T).Name}.");
            }
            return (T)existing;
        }
        _values.Add(key, value);
        return value;
    }

    public bool TryGetView<T>(
        XBlockAddress address,
        string view,
        out T? value)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(view);
        if (_values.TryGetValue((address, typeof(T), view), out object? stored) &&
            stored is T typed)
        {
            value = typed;
            return true;
        }
        value = null;
        return false;
    }
}
