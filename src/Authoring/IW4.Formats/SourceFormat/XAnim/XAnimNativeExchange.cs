using System.Text.Json;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.XAnim;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;

namespace IW4.Formats.SourceFormat.XAnim;

/// <summary>Exchanges the retained PS3 XAnim graph without a playback projection.</summary>
public sealed class XAnimNativeExchange
{
    private const string Format = "iw4-ps3-xanim";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public IReadOnlyList<string> Unlink(string sourceDirectory, XAnimPartsAsset animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        string name = SourceOutput.NormalizeOwnedAssetName(animation.Name, "XAnim");
        NativeAnimation document = ToDocument(animation, name);
        string json = JsonSerializer.Serialize(document, Options);
        return new SourceOutput(sourceDirectory).WriteTextBatch(
            [($"xanim_native/{name}.json", writer => writer.WriteLine(json))]);
    }

    public XAnimPartsAsset Link(string sourceDirectory, string animationName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(animationName, "XAnim");
        string path = NativeSourcePath.Resolve(sourceDirectory, "xanim_native", name, ".json");
        NativeAnimation source = JsonSerializer.Deserialize<NativeAnimation>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"XAnim '{name}' has an empty native source document.");
        if (source.Format != Format || source.Version != 1 || source.Name != name)
            throw new InvalidDataException($"XAnim '{name}' has invalid native source fields.");
        XAnimPartsAsset asset = FromDocument(source, name);
        Validate(asset, name);
        return asset;
    }

    private static NativeAnimation ToDocument(XAnimPartsAsset asset, string name)
    {
        Validate(asset, name);
        return new NativeAnimation
        {
            Format = Format, Version = 1, Name = name,
            DataByteCount = asset.DataByteCount, DataShortCount = asset.DataShortCount,
            DataIntCount = asset.DataIntCount, RandomDataByteCount = asset.RandomDataByteCount,
            RandomDataIntCount = asset.RandomDataIntCount, RandomDataShortCount = asset.RandomDataShortCount,
            IndexCount = asset.IndexCount, NumFrames = asset.NumFrames, Flags = asset.Flags,
            DeltaFlags = asset.DeltaFlags, BoneCounts = asset.BoneCounts.ToArray(),
            BoneNameCount = asset.BoneNameCount, NotifyCount = asset.NotifyCount,
            AssetType = asset.AssetType, Pad1F = asset.Pad1F,
            Framerate = asset.Framerate, Frequency = asset.Frequency,
            Names = asset.Names.Select(value => RequireText(value?.Text)).ToArray(),
            Notify = asset.Notify.Select(value => value is null
                ? throw new InvalidDataException($"XAnim '{name}' has a null notify.")
                : new NativeNotify(RequireText(value.Name?.Text), value.Time)).ToArray(),
            PackedDataStreams = new NativeStreams(
                asset.PackedDataStreams.QuantizedBytes.ToArray(),
                asset.PackedDataStreams.QuantizedShorts.ToArray(),
                asset.PackedDataStreams.QuantizedInts.ToArray(),
                asset.PackedDataStreams.RandomizedQuantizedShorts.ToArray(),
                asset.PackedDataStreams.RandomizedQuantizedBytes.ToArray(),
                asset.PackedDataStreams.RandomizedQuantizedInts.ToArray()),
            Indices = asset.Indices.FrameIndices.ToArray(),
            DeltaPart = asset.DeltaPart is null ? null : ToDelta(asset.DeltaPart)
        };
    }

    private static XAnimPartsAsset FromDocument(NativeAnimation source, string name)
    {
        if (source.BoneCounts is null || source.Names is null || source.Notify is null ||
            source.PackedDataStreams is null || source.Indices is null)
            throw new InvalidDataException($"XAnim '{name}' has missing native arrays.");
        NativeStreams streams = source.PackedDataStreams;
        if (streams.QuantizedBytes is null || streams.QuantizedShorts is null || streams.QuantizedInts is null ||
            streams.RandomizedQuantizedShorts is null || streams.RandomizedQuantizedBytes is null ||
            streams.RandomizedQuantizedInts is null)
            throw new InvalidDataException($"XAnim '{name}' has missing packed streams.");
        return new XAnimPartsAsset
        {
            Name = name, DataByteCount = source.DataByteCount, DataShortCount = source.DataShortCount,
            DataIntCount = source.DataIntCount, RandomDataByteCount = source.RandomDataByteCount,
            RandomDataIntCount = source.RandomDataIntCount, RandomDataShortCount = source.RandomDataShortCount,
            IndexCount = source.IndexCount, NumFrames = source.NumFrames, Flags = source.Flags,
            DeltaFlags = source.DeltaFlags, BoneCounts = source.BoneCounts,
            BoneNameCount = source.BoneNameCount, NotifyCount = source.NotifyCount,
            AssetType = source.AssetType, Pad1F = source.Pad1F,
            Framerate = source.Framerate, Frequency = source.Frequency,
            Names = source.Names.Select(ScriptString).ToArray(),
            Notify = source.Notify.Select(value => value is null
                ? throw new InvalidDataException($"XAnim '{name}' has a null notify.")
                : new XAnimNotifyInfo(ScriptString(value.Name), value.Time)).ToArray(),
            PackedDataStreams = new XAnimPackedDataStreams
            {
                QuantizedBytes = streams.QuantizedBytes, QuantizedShorts = streams.QuantizedShorts,
                QuantizedInts = streams.QuantizedInts,
                RandomizedQuantizedShorts = streams.RandomizedQuantizedShorts,
                RandomizedQuantizedBytes = streams.RandomizedQuantizedBytes,
                RandomizedQuantizedInts = streams.RandomizedQuantizedInts
            },
            Indices = new XAnimFrameIndexStream { FrameIndices = source.Indices },
            DeltaPart = source.DeltaPart is null ? null : FromDelta(source.DeltaPart)
        };
    }

    private static string RequireText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl) ? text :
            throw new InvalidDataException("XAnim script strings must have semantic text.");

    private static ScriptStringReference ScriptString(string? text) =>
        new(0, RequireText(text), ScriptStringHandle.Null, default);

    private static NativeDelta ToDelta(XAnimDeltaPart delta) => new(
        delta.Trans is null ? null : new NativeTrans(
            delta.Trans.Size, delta.Trans.SmallTrans, delta.Trans.Pad3,
            delta.Trans.Frame0, delta.Trans.Frames is null ? null : new NativeTransFrames(
                delta.Trans.Frames.Mins, delta.Trans.Frames.Size,
                delta.Trans.Frames.DynamicFrames.FrameIndices.ToArray(),
                delta.Trans.Frames.FramePayload is SmallXAnimTransFramePayload small ? small.Frames.ToArray() : null,
                delta.Trans.Frames.FramePayload is LargeXAnimTransFramePayload large ? large.Frames.ToArray() : null)),
        delta.Quat2 is null ? null : new NativeQuat2(
            delta.Quat2.Size, delta.Quat2.Pad2, delta.Quat2.Pad3, delta.Quat2.Frame0,
            delta.Quat2.Frames is null ? null : new NativeQuat2Frames(
                delta.Quat2.Frames.DynamicFrames.FrameIndices.ToArray(), delta.Quat2.Frames.Frames.ToArray())),
        delta.Quat is null ? null : new NativeQuat(
            delta.Quat.Size, delta.Quat.Pad2, delta.Quat.Pad3, delta.Quat.Frame0,
            delta.Quat.Frames is null ? null : new NativeQuatFrames(
                delta.Quat.Frames.DynamicFrames.FrameIndices.ToArray(), delta.Quat.Frames.Frames.ToArray())));

    private static XAnimDeltaPart FromDelta(NativeDelta delta) => new()
    {
        Trans = delta.Trans is null ? null : new XAnimPartTrans
        {
            Size = delta.Trans.Size, SmallTrans = delta.Trans.SmallTrans, Pad3 = delta.Trans.Pad3,
            Frame0 = delta.Trans.Frame0,
            Frames = delta.Trans.Frames is null ? null : new XAnimPartTransFrames
            {
                Mins = delta.Trans.Frames.Mins ?? throw new InvalidDataException("XAnim translation mins are missing."),
                Size = delta.Trans.Frames.Size ?? throw new InvalidDataException("XAnim translation size is missing."),
                DynamicFrames = new XAnimDynamicFrames { FrameIndices = delta.Trans.Frames.Indices ??
                    throw new InvalidDataException("XAnim translation indices are missing.") },
                FramePayload = delta.Trans.SmallTrans == 0
                    ? new LargeXAnimTransFramePayload { Frames = delta.Trans.Frames.LargeFrames ??
                        throw new InvalidDataException("XAnim large translation frames are missing.") }
                    : new SmallXAnimTransFramePayload { Frames = delta.Trans.Frames.SmallFrames ??
                        throw new InvalidDataException("XAnim small translation frames are missing.") }
            }
        },
        Quat2 = delta.Quat2 is null ? null : new XAnimDeltaPartQuat2
        {
            Size = delta.Quat2.Size, Pad2 = delta.Quat2.Pad2, Pad3 = delta.Quat2.Pad3,
            Frame0 = delta.Quat2.Frame0,
            Frames = delta.Quat2.Frames is null ? null : new XAnimDeltaPartQuatDataFrames2
            {
                DynamicFrames = new XAnimDynamicFrames { FrameIndices = delta.Quat2.Frames.Indices ??
                    throw new InvalidDataException("XAnim quat2 indices are missing.") },
                Frames = delta.Quat2.Frames.Values ?? throw new InvalidDataException("XAnim quat2 frames are missing."),
                FrameCount = delta.Quat2.Frames.Values?.Length ?? 0
            }
        },
        Quat = delta.Quat is null ? null : new XAnimDeltaPartQuat
        {
            Size = delta.Quat.Size, Pad2 = delta.Quat.Pad2, Pad3 = delta.Quat.Pad3,
            Frame0 = delta.Quat.Frame0,
            Frames = delta.Quat.Frames is null ? null : new XAnimDeltaPartQuatDataFrames
            {
                DynamicFrames = new XAnimDynamicFrames { FrameIndices = delta.Quat.Frames.Indices ??
                    throw new InvalidDataException("XAnim quat indices are missing.") },
                Frames = delta.Quat.Frames.Values ?? throw new InvalidDataException("XAnim quat frames are missing."),
                FrameCount = delta.Quat.Frames.Values?.Length ?? 0
            }
        }
    };

    private static void Validate(XAnimPartsAsset asset, string name)
    {
        if (asset.BoneCounts is not { Count: 10 } || asset.Names is null || asset.Notify is null ||
            asset.PackedDataStreams is null || asset.Indices is null ||
            asset.BoneNameCount != asset.Names.Count || asset.NotifyCount != asset.Notify.Count ||
            asset.DataByteCount != asset.PackedDataStreams.QuantizedBytes?.Count ||
            asset.DataShortCount != asset.PackedDataStreams.QuantizedShorts?.Count ||
            asset.DataIntCount != asset.PackedDataStreams.QuantizedInts?.Count ||
            asset.RandomDataByteCount != asset.PackedDataStreams.RandomizedQuantizedBytes?.Count ||
            asset.RandomDataShortCount != asset.PackedDataStreams.RandomizedQuantizedShorts?.Count ||
            asset.RandomDataIntCount != asset.PackedDataStreams.RandomizedQuantizedInts?.Count ||
            asset.IndexCount != asset.Indices.FrameIndices?.Count ||
            !float.IsFinite(asset.Framerate) || !float.IsFinite(asset.Frequency))
            throw new InvalidDataException($"XAnim '{name}' has incomplete native streams or counts.");
        foreach (ScriptStringReference value in asset.Names)
            _ = ScriptString(value?.Text);
        foreach (XAnimNotifyInfo value in asset.Notify)
        {
            _ = ScriptString(value?.Name?.Text);
            if (value is null || !float.IsFinite(value.Time))
                throw new InvalidDataException($"XAnim '{name}' has a non-finite notify time.");
        }
        if (asset.NumFrames <= byte.MaxValue && asset.Indices.FrameIndices.Any(value => value > byte.MaxValue))
            throw new InvalidDataException($"XAnim '{name}' has an index outside the native byte encoding.");
        if (asset.DeltaPart is { } delta)
        {
            if (delta.Trans is null && delta.TransPointer.Type != PointerType.Null ||
                delta.Quat2 is null && delta.Quat2Pointer.Type != PointerType.Null ||
                delta.Quat is null && delta.QuatPointer.Type != PointerType.Null)
                throw new InvalidDataException($"XAnim '{name}' has an unmaterialized delta branch.");
            ValidateFrames(delta.Trans?.Size, delta.Trans?.Frame0, delta.Trans?.Frames,
                delta.Trans?.Frames?.DynamicFrames?.FrameIndices, asset.NumFrames, name, "translation");
            if (delta.Trans is { Frames: { } transFrames } trans)
            {
                int count = trans.Size + 1;
                if (transFrames.Mins is null || transFrames.Size is null ||
                    !Finite(transFrames.Mins) || !Finite(transFrames.Size) ||
                    trans.SmallTrans == 0 && transFrames.FramePayload is SmallXAnimTransFramePayload ||
                    trans.SmallTrans != 0 && transFrames.FramePayload is LargeXAnimTransFramePayload ||
                    (trans.SmallTrans == 0
                    ? transFrames.FramePayload is not LargeXAnimTransFramePayload large || large.Frames.Count != count
                    : transFrames.FramePayload is not SmallXAnimTransFramePayload small || small.Frames.Count != count))
                    throw new InvalidDataException($"XAnim '{name}' has incomplete translation frame payload.");
            }
            ValidateFrames(delta.Quat2?.Size, delta.Quat2?.Frame0, delta.Quat2?.Frames,
                delta.Quat2?.Frames?.DynamicFrames?.FrameIndices, asset.NumFrames, name, "quat2");
            if (delta.Quat2 is { Frames: { } quat2 } quat2Part &&
                (quat2.Frames?.Count != quat2Part.Size + 1 || quat2.Frames.Any(value => value is null)))
                throw new InvalidDataException($"XAnim '{name}' has incomplete quat2 frames.");
            ValidateFrames(delta.Quat?.Size, delta.Quat?.Frame0, delta.Quat?.Frames,
                delta.Quat?.Frames?.DynamicFrames?.FrameIndices, asset.NumFrames, name, "quat");
            if (delta.Quat is { Frames: { } quat } quatPart &&
                (quat.Frames?.Count != quatPart.Size + 1 || quat.Frames.Any(value => value is null)))
                throw new InvalidDataException($"XAnim '{name}' has incomplete quat frames.");
        }
        else if (asset.DeltaPartPointer.Type != PointerType.Null)
            throw new InvalidDataException($"XAnim '{name}' has an unmaterialized delta part.");
    }

    private static bool Finite(XAnimVec3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void ValidateFrames(ushort? size, object? frame0, object? frames,
        IReadOnlyList<ushort>? indices, ushort numFrames, string name, string kind)
    {
        if (size is null) return;
        if (size == 0 && (frame0 is null || frames is not null) ||
            size != 0 && (frame0 is not null || frames is null || indices?.Count != size + 1))
            throw new InvalidDataException($"XAnim '{name}' has incomplete {kind} delta frames.");
        if (numFrames <= byte.MaxValue && indices?.Any(value => value > byte.MaxValue) == true)
            throw new InvalidDataException($"XAnim '{name}' has a {kind} index outside the native byte encoding.");
    }

    private sealed class NativeAnimation
    {
        public NativeAnimation() { }

        public string? Format { get; init; }
        public int Version { get; init; }
        public string? Name { get; init; }
        public ushort DataByteCount { get; init; }
        public ushort DataShortCount { get; init; }
        public ushort DataIntCount { get; init; }
        public ushort RandomDataByteCount { get; init; }
        public ushort RandomDataIntCount { get; init; }
        public int RandomDataShortCount { get; init; }
        public int IndexCount { get; init; }
        public ushort NumFrames { get; init; }
        public byte Flags { get; init; }
        public byte DeltaFlags { get; init; }
        public byte[]? BoneCounts { get; init; }
        public byte BoneNameCount { get; init; }
        public byte NotifyCount { get; init; }
        public byte AssetType { get; init; }
        public byte Pad1F { get; init; }
        public float Framerate { get; init; }
        public float Frequency { get; init; }
        public string[]? Names { get; init; }
        public NativeNotify[]? Notify { get; init; }
        public NativeStreams? PackedDataStreams { get; init; }
        public ushort[]? Indices { get; init; }
        public NativeDelta? DeltaPart { get; init; }
    }

    private sealed record NativeNotify(string? Name, float Time);
    private sealed record NativeStreams(byte[]? QuantizedBytes, short[]? QuantizedShorts, int[]? QuantizedInts,
        short[]? RandomizedQuantizedShorts, byte[]? RandomizedQuantizedBytes, int[]? RandomizedQuantizedInts);
    private sealed record NativeDelta(NativeTrans? Trans, NativeQuat2? Quat2, NativeQuat? Quat);
    private sealed record NativeTrans(ushort Size, byte SmallTrans, byte Pad3, XAnimPartTransFrame0? Frame0,
        NativeTransFrames? Frames);
    private sealed record NativeTransFrames(XAnimVec3? Mins, XAnimVec3? Size, ushort[]? Indices,
        SmallXAnimTransFrame[]? SmallFrames, LargeXAnimTransFrame[]? LargeFrames);
    private sealed record NativeQuat2(ushort Size, byte Pad2, byte Pad3, XQuat2? Frame0, NativeQuat2Frames? Frames);
    private sealed record NativeQuat2Frames(ushort[]? Indices, XQuat2[]? Values);
    private sealed record NativeQuat(ushort Size, byte Pad2, byte Pad3, XQuat? Frame0, NativeQuatFrames? Frames);
    private sealed record NativeQuatFrames(ushort[]? Indices, XQuat[]? Values);
}
