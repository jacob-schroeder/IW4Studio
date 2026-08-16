using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace IW4.Render.Shaders;

internal readonly record struct RsxProgramContentDigest(
    ulong Word0,
    ulong Word1,
    ulong Word2,
    ulong Word3)
{
    internal static RsxProgramContentDigest Compute(
        ReadOnlySpan<byte> programData)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(programData, digest);
        return new RsxProgramContentDigest(
            BinaryPrimitives.ReadUInt64BigEndian(digest),
            BinaryPrimitives.ReadUInt64BigEndian(digest[8..]),
            BinaryPrimitives.ReadUInt64BigEndian(digest[16..]),
            BinaryPrimitives.ReadUInt64BigEndian(digest[24..]));
    }
}

/// <summary>
/// Immutable, backend-neutral semantic view of one exact vertex/fragment
/// program pair. Each stage is decoded lazily through a collision-safe shared
/// cache, so name-based sampler routes still avoid an unnecessary vertex
/// decode.
/// </summary>
internal sealed class RsxProgramSemanticSnapshot
{
    private readonly Lazy<RsxVertexProgramSemanticSnapshot> _vertexProgram;
    private readonly Lazy<RsxFragmentProgramSemanticSnapshot>
        _fragmentProgram;

    internal RsxProgramSemanticSnapshot(
        ProgramDataCacheIdentity vertexProgramIdentity,
        ProgramDataCacheIdentity fragmentProgramIdentity,
        Func<RsxVertexProgramSemanticSnapshot> resolveVertexProgram,
        Func<RsxFragmentProgramSemanticSnapshot> resolveFragmentProgram)
    {
        ArgumentNullException.ThrowIfNull(vertexProgramIdentity);
        ArgumentNullException.ThrowIfNull(fragmentProgramIdentity);
        ArgumentNullException.ThrowIfNull(resolveVertexProgram);
        ArgumentNullException.ThrowIfNull(resolveFragmentProgram);
        VertexProgramIdentity = vertexProgramIdentity;
        FragmentProgramIdentity = fragmentProgramIdentity;
        _vertexProgram = new Lazy<RsxVertexProgramSemanticSnapshot>(
            resolveVertexProgram,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _fragmentProgram = new Lazy<RsxFragmentProgramSemanticSnapshot>(
            resolveFragmentProgram,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal ProgramDataCacheIdentity VertexProgramIdentity { get; }

    internal ProgramDataCacheIdentity FragmentProgramIdentity { get; }

    internal RsxVertexProgramSemanticSnapshot VertexProgram =>
        _vertexProgram.Value;

    internal RsxFragmentProgramSemanticSnapshot FragmentProgram =>
        _fragmentProgram.Value;

    internal byte[] CloneVertexProgramData() =>
        VertexProgramIdentity.CloneData();

    internal byte[] CloneFragmentProgramData() =>
        FragmentProgramIdentity.CloneData();
}

/// <summary>
/// Retained decode and input-routing analysis for one exact vertex program.
/// Consumer-specific interpretations remain in the existing analyzer.
/// </summary>
internal sealed class RsxVertexProgramSemanticSnapshot
{
    private readonly Lazy<RsxVertexOutputDependencyAnalysis>
        _outputDependencyAnalysis;

    internal RsxVertexProgramSemanticSnapshot(
        ProgramDataCacheIdentity identity,
        RsxVertexProgramIr? programIr)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
        ProgramIr = programIr;
        _outputDependencyAnalysis = new Lazy<
            RsxVertexOutputDependencyAnalysis>(
            () =>
            {
                if (ProgramIr is null)
                    return RsxVertexOutputDependencyAnalysis.Empty;

                return RsxShaderInputRouter.AnalyzeVertexOutputDependencies(
                    ProgramIr);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal ProgramDataCacheIdentity Identity { get; }

    internal RsxVertexProgramIr? ProgramIr { get; }

    internal RsxVertexOutputDependencyAnalysis OutputDependencyAnalysis =>
        _outputDependencyAnalysis.Value;
}

/// <summary>
/// Retained raw semantic decode for one exact fragment program before any
/// material/static specialization. Texture routing consumes the same decoded
/// instructions later used as the translator's specialization input.
/// </summary>
internal sealed class RsxFragmentProgramSemanticSnapshot
{
    private readonly ImmutableDictionary<int, int>
        _instructionIndexByInlineConstantPayloadOffset;

    internal RsxFragmentProgramSemanticSnapshot(
        ProgramDataCacheIdentity identity,
        int uploadOffset,
        ImmutableArray<RsxFragmentInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (uploadOffset < -1)
            throw new ArgumentOutOfRangeException(nameof(uploadOffset));
        if (instructions.IsDefault)
        {
            throw new ArgumentException(
                "The fragment instruction array must be initialized.",
                nameof(instructions));
        }
        if (uploadOffset < 0 && !instructions.IsEmpty)
        {
            throw new ArgumentException(
                "An invalid fragment upload cannot contain instructions.",
                nameof(instructions));
        }

        Identity = identity;
        UploadOffset = uploadOffset;
        Instructions = instructions;
        var inlineConstantInstructions =
            ImmutableDictionary.CreateBuilder<int, int>();
        for (int instructionIndex = 0;
             instructionIndex < instructions.Length;
             instructionIndex++)
        {
            RsxFragmentInstruction instruction =
                instructions[instructionIndex];
            if (instruction.ByteCount != 0x20 ||
                !instruction.Constant.HasValue)
            {
                continue;
            }

            int payloadOffset = checked(instruction.Offset + 0x10);
            if (!inlineConstantInstructions.TryAdd(
                    payloadOffset,
                    instructionIndex))
            {
                throw new InvalidOperationException(
                    "Decoded fragment instructions cannot share an " +
                    "inline-constant payload offset.");
            }
        }
        _instructionIndexByInlineConstantPayloadOffset =
            inlineConstantInstructions.ToImmutable();
        TextureOps = instructions
            .Where(instruction => instruction.IsTexture)
            .Select(instruction => new PixelTextureOp(
                instruction.TextureUnit,
                instruction.Index,
                instruction.Source0Operand,
                instruction.SourceAttribute))
            .ToImmutableArray();
    }

    internal ProgramDataCacheIdentity Identity { get; }

    internal int UploadOffset { get; }

    internal bool HasValidUpload => UploadOffset >= 0;

    internal ImmutableArray<RsxFragmentInstruction> Instructions { get; }

    internal ImmutableArray<PixelTextureOp> TextureOps { get; }

    /// <summary>
    /// Returns whether an exact program-relative byte offset names the first
    /// byte of a decoded 16-byte inline-constant payload. Runtime patch-table
    /// offsets are not allowed to target instruction words, the middle of a
    /// payload, or bytes the authored decode did not consume.
    /// </summary>
    internal bool IsExactInlineConstantPayloadOffset(int programOffset) =>
        _instructionIndexByInlineConstantPayloadOffset.ContainsKey(
            programOffset);

    /// <summary>
    /// Produces an immutable specialization of the authored decode by reading
    /// only resolved inline-constant payloads from effective program
    /// bytes. Instruction boundaries and raw instruction words remain those
    /// decoded once from the authored program.
    /// </summary>
    internal ImmutableArray<RsxFragmentInstruction>
        SpecializeInlineConstants(byte[] effectiveProgramData)
    {
        ArgumentNullException.ThrowIfNull(effectiveProgramData);
        if (effectiveProgramData.Length != Identity.ByteCount)
        {
            throw new ArgumentException(
                "Effective fragment-program bytes must retain the exact " +
                "authored byte count.",
                nameof(effectiveProgramData));
        }
        if (_instructionIndexByInlineConstantPayloadOffset.Count == 0)
            return Instructions;

        var specialized = Instructions.ToBuilder();
        foreach ((int payloadOffset, int instructionIndex) in
                 _instructionIndexByInlineConstantPayloadOffset)
        {
            if (payloadOffset < 0 ||
                payloadOffset > effectiveProgramData.Length - 0x10)
            {
                throw new InvalidOperationException(
                    "A decoded inline-constant payload falls outside the " +
                    "captured fragment program.");
            }

            specialized[instructionIndex] =
                specialized[instructionIndex] with
                {
                    Constant = new RsxFragmentInlineConstant(
                        ReadFragmentConstantWord(
                            effectiveProgramData,
                            payloadOffset),
                        ReadFragmentConstantWord(
                            effectiveProgramData,
                            payloadOffset + 4),
                        ReadFragmentConstantWord(
                            effectiveProgramData,
                            payloadOffset + 8),
                        ReadFragmentConstantWord(
                            effectiveProgramData,
                            payloadOffset + 12))
                };
        }

        return specialized.ToImmutable();
    }

    private static uint ReadFragmentConstantWord(
        byte[] data,
        int offset) =>
        RsxProgramDecoder.FragmentWord(
            BinaryPrimitives.ReadUInt32BigEndian(
                data.AsSpan(offset, sizeof(uint))));
}

/// <summary>
/// Bounded exact-content cache for backend-neutral RSX stage decodes. Digests
/// are dictionary prefilters only; <see cref="ProgramDataCacheIdentity"/>
/// compares every retained byte before an entry can be reused.
/// </summary>
internal sealed class RsxProgramSemanticCache
{
    internal const int DefaultStageEntryCapacity = 256;

    internal static RsxProgramSemanticCache Shared { get; } = new();

    private readonly RsxProgramSemanticStageCache<
        RsxVertexProgramSemanticSnapshot> _vertexPrograms;
    private readonly RsxProgramSemanticStageCache<
        RsxFragmentProgramSemanticSnapshot> _fragmentPrograms;

    internal RsxProgramSemanticCache(
        int stageEntryCapacity = DefaultStageEntryCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stageEntryCapacity, 1);
        _vertexPrograms = new RsxProgramSemanticStageCache<
            RsxVertexProgramSemanticSnapshot>(
            stageEntryCapacity,
            DecodeVertexProgram);
        _fragmentPrograms = new RsxProgramSemanticStageCache<
            RsxFragmentProgramSemanticSnapshot>(
            stageEntryCapacity,
            DecodeFragmentProgram);
    }

    internal RsxProgramSemanticSnapshot Resolve(
        byte[]? vertexProgramData,
        byte[]? fragmentProgramData) =>
        Resolve(
            CaptureProgramIdentity(vertexProgramData),
            CaptureProgramIdentity(fragmentProgramData));

    internal ProgramDataCacheIdentity CaptureProgramIdentity(
        byte[]? programData) =>
        ProgramDataCacheIdentity.Capture(programData);

    internal RsxProgramSemanticSnapshot Resolve(
        ProgramDataCacheIdentity vertexProgram,
        ProgramDataCacheIdentity fragmentProgram)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        ArgumentNullException.ThrowIfNull(fragmentProgram);
        return new RsxProgramSemanticSnapshot(
            vertexProgram,
            fragmentProgram,
            () => _vertexPrograms.GetOrAdd(vertexProgram).GetValue(),
            () => _fragmentPrograms.GetOrAdd(fragmentProgram).GetValue());
    }

    internal RsxVertexProgramSemanticSnapshot ResolveVertex(
        byte[] vertexProgramData)
    {
        ArgumentNullException.ThrowIfNull(vertexProgramData);
        ProgramDataCacheIdentity identity = CaptureProgramIdentity(
            vertexProgramData);
        return _vertexPrograms.GetOrAdd(identity).GetValue();
    }

    private RsxVertexProgramSemanticSnapshot DecodeVertexProgram(
        ProgramDataCacheIdentity identity)
    {
        if (!identity.HasData)
        {
            return new RsxVertexProgramSemanticSnapshot(
                identity,
                programIr: null);
        }

        return new RsxVertexProgramSemanticSnapshot(
            identity,
            RsxProgramDecoder.DecodeVertexProgram(identity.CloneData()));
    }

    private RsxFragmentProgramSemanticSnapshot DecodeFragmentProgram(
        ProgramDataCacheIdentity identity)
    {
        if (!identity.HasData)
        {
            return new RsxFragmentProgramSemanticSnapshot(
                identity,
                uploadOffset: -1,
                ImmutableArray<RsxFragmentInstruction>.Empty);
        }

        byte[] data = identity.CloneData();
        int uploadOffset = RsxProgramDecoder.ShaderUploadOffset(data);
        ImmutableArray<RsxFragmentInstruction> instructions =
            uploadOffset < 0
                ? ImmutableArray<RsxFragmentInstruction>.Empty
                : RsxProgramDecoder.DecodeFragment(data, uploadOffset);
        return new RsxFragmentProgramSemanticSnapshot(
            identity,
            uploadOffset,
            instructions);
    }
}

internal sealed class RsxProgramSemanticCacheEntry<T>
    where T : class
{
    private const int Pending = 0;
    private const int Executing = 1;
    private const int Completed = 2;
    private const int Faulted = 3;

    private readonly Lazy<T> _value;
    private readonly Action<RsxProgramSemanticCacheEntry<T>> _onCompletion;
    private int _completionReported;
    private int _executionState = Pending;

    internal RsxProgramSemanticCacheEntry(
        ProgramDataCacheIdentity identity,
        Func<ProgramDataCacheIdentity, T> factory,
        Action<RsxProgramSemanticCacheEntry<T>> onCompletion)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(onCompletion);
        Identity = identity;
        _onCompletion = onCompletion;
        _value = new Lazy<T>(
            () => Execute(factory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal ProgramDataCacheIdentity Identity { get; }

    internal bool IsEvictable =>
        Volatile.Read(ref _executionState) >= Completed;

    internal bool IsFaulted =>
        Volatile.Read(ref _executionState) == Faulted;

    internal T GetValue()
    {
        try
        {
            return _value.Value;
        }
        finally
        {
            if (Volatile.Read(ref _executionState) >= Completed &&
                Interlocked.Exchange(ref _completionReported, 1) == 0)
            {
                _onCompletion(this);
            }
        }
    }

    private T Execute(Func<ProgramDataCacheIdentity, T> factory)
    {
        Volatile.Write(ref _executionState, Executing);
        try
        {
            T value = factory(Identity);
            Volatile.Write(ref _executionState, Completed);
            return value;
        }
        catch
        {
            Volatile.Write(ref _executionState, Faulted);
            throw;
        }
    }
}

internal sealed class RsxProgramSemanticStageCache<T>
    where T : class
{
    private readonly object _gate = new();
    private readonly int _entryCapacity;
    private readonly Func<ProgramDataCacheIdentity, T> _factory;
    private readonly Dictionary<ProgramDataCacheIdentity,
        RsxProgramSemanticCacheEntry<T>> _entries = [];
    private readonly LinkedList<RsxProgramSemanticCacheEntry<T>>
        _retentionOrder = [];

    internal RsxProgramSemanticStageCache(
        int entryCapacity,
        Func<ProgramDataCacheIdentity, T> factory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entryCapacity, 1);
        ArgumentNullException.ThrowIfNull(factory);
        _entryCapacity = entryCapacity;
        _factory = factory;
    }

    internal RsxProgramSemanticCacheEntry<T> GetOrAdd(
        ProgramDataCacheIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            if (_entries.TryGetValue(
                    identity,
                    out RsxProgramSemanticCacheEntry<T>? cached))
            {
                return cached;
            }

            var entry = new RsxProgramSemanticCacheEntry<T>(
                identity,
                _factory,
                Complete);
            _entries.Add(identity, entry);
            _retentionOrder.AddLast(entry);
            TrimToCapacityLocked();
            return entry;
        }
    }

    private void Complete(RsxProgramSemanticCacheEntry<T> entry)
    {
        lock (_gate)
        {
            if (entry.IsFaulted &&
                _entries.TryGetValue(entry.Identity, out var retained) &&
                ReferenceEquals(retained, entry))
            {
                RemoveLocked(entry);
            }

            TrimToCapacityLocked();
        }
    }

    private void TrimToCapacityLocked()
    {
        int candidatesToInspect = _retentionOrder.Count;
        while (_entries.Count > _entryCapacity &&
               candidatesToInspect-- > 0 &&
               _retentionOrder.First is { } oldestNode)
        {
            _retentionOrder.RemoveFirst();
            RsxProgramSemanticCacheEntry<T> oldest = oldestNode.Value;
            if (!oldest.IsEvictable)
            {
                _retentionOrder.AddLast(oldestNode);
                continue;
            }

            if (_entries.TryGetValue(oldest.Identity, out var retained) &&
                ReferenceEquals(retained, oldest))
            {
                _entries.Remove(oldest.Identity);
            }
        }
    }

    private void RemoveLocked(RsxProgramSemanticCacheEntry<T> entry)
    {
        _entries.Remove(entry.Identity);
        _retentionOrder.Remove(entry);
    }
}
