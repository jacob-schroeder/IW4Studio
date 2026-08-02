namespace IW4.Render.SceneBuilding.Batching;

/// <summary>
/// Build-local interning for immutable geometry payloads shared by camera
/// color and receiver technique variants. Native rendering keeps geometry
/// ownership independent from the selected material technique; retaining one
/// exact array for byte-identical host payloads preserves that ownership and
/// avoids multiplying the scene working set by every receiver channel.
/// </summary>
internal sealed class MapRenderImmutableGeometryArrayPool
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly Lock _gate = new();
    private readonly Dictionary<ArraySignature, List<float[]>>
        _floats = [];
    private readonly Dictionary<ArraySignature, List<uint[]>>
        _uints = [];

    internal float[] InternFloats(IReadOnlyList<float> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArraySignature signature = FloatSignature(source);
        lock (_gate)
        {
            if (_floats.TryGetValue(
                    signature,
                    out List<float[]>? candidates))
            {
                foreach (float[] candidate in candidates)
                {
                    if (FloatBitsEqual(source, candidate))
                        return candidate;
                }
            }
            else
            {
                candidates = [];
                _floats.Add(signature, candidates);
            }

            float[] retained = source as float[] ?? source.ToArray();
            candidates.Add(retained);
            return retained;
        }
    }

    internal uint[] InternUInts(IReadOnlyList<uint> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArraySignature signature = UIntSignature(source);
        lock (_gate)
        {
            if (_uints.TryGetValue(
                    signature,
                    out List<uint[]>? candidates))
            {
                foreach (uint[] candidate in candidates)
                {
                    if (UIntsEqual(source, candidate))
                        return candidate;
                }
            }
            else
            {
                candidates = [];
                _uints.Add(signature, candidates);
            }

            uint[] retained = source as uint[] ?? source.ToArray();
            candidates.Add(retained);
            return retained;
        }
    }

    private static ArraySignature FloatSignature(
        IReadOnlyList<float> source)
    {
        ulong hash = FnvOffsetBasis;
        for (int index = 0; index < source.Count; index++)
        {
            hash ^= BitConverter.SingleToUInt32Bits(source[index]);
            hash *= FnvPrime;
        }
        return new(source.Count, hash);
    }

    private static ArraySignature UIntSignature(
        IReadOnlyList<uint> source)
    {
        ulong hash = FnvOffsetBasis;
        for (int index = 0; index < source.Count; index++)
        {
            hash ^= source[index];
            hash *= FnvPrime;
        }
        return new(source.Count, hash);
    }

    private static bool FloatBitsEqual(
        IReadOnlyList<float> source,
        IReadOnlyList<float> candidate)
    {
        if (source.Count != candidate.Count)
            return false;
        for (int index = 0; index < source.Count; index++)
        {
            if (BitConverter.SingleToUInt32Bits(source[index]) !=
                BitConverter.SingleToUInt32Bits(candidate[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool UIntsEqual(
        IReadOnlyList<uint> source,
        IReadOnlyList<uint> candidate)
    {
        if (source.Count != candidate.Count)
            return false;
        for (int index = 0; index < source.Count; index++)
        {
            if (source[index] != candidate[index])
                return false;
        }
        return true;
    }

    private readonly record struct ArraySignature(
        int Count,
        ulong Hash);
}
