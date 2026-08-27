using System.Numerics;

namespace IW4.AssetExchange.SourceFormat.XAnim;

/// <summary>
/// One sampled local XAnim bone transform. Rotation replaces the model-local
/// rotation for every animation-mapped bone; a missing quaternion stream is
/// the native identity rotation. Translation is a delta from the model's local
/// bind offset.
/// </summary>
public readonly record struct XAnimLocalBoneTransform(
    Quaternion Rotation,
    Vector3 Translation);

/// <summary>
/// Cached semantic projection of one console XAnim. The compressed streams
/// are reconstructed once and can then be sampled without reparsing them.
/// </summary>
public sealed class XAnimPlaybackClip
{
    private const float QuaternionScale = 1.0f / short.MaxValue;
    private readonly XAnimSourceParts _parts;
    private readonly IReadOnlyList<string> _boneNames;

    internal XAnimPlaybackClip(XAnimSourceParts parts)
    {
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        _boneNames = Array.AsReadOnly(parts.Bones
            .Select(bone => bone.Name)
            .ToArray());
    }

    public int NumFrames => _parts.NumFrames;

    public float Framerate => _parts.Framerate;

    public bool Looped => _parts.Looped;

    public int BoneCount => _parts.Bones.Count;

    public IReadOnlyList<string> BoneNames => _boneNames;

    public double DurationSeconds => Framerate > 0.0f
        ? NumFrames / (double)Framerate
        : 0.0;

    /// <summary>
    /// Samples a frame in the inclusive range 0..NumFrames. Callers own time
    /// wrapping so paused and non-looping clips can hold an exact end pose.
    /// </summary>
    public void Sample(
        float frame,
        Span<XAnimLocalBoneTransform> destination)
    {
        if (!float.IsFinite(frame))
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (destination.Length < BoneCount)
        {
            throw new ArgumentException(
                $"XAnim sampling requires {BoneCount} destination transforms.",
                nameof(destination));
        }

        float clampedFrame = Math.Clamp(frame, 0.0f, NumFrames);
        for (int index = 0; index < BoneCount; index++)
        {
            XAnimSourceBoneTrack bone = _parts.Bones[index];
            destination[index] = new XAnimLocalBoneTransform(
                SampleRotation(bone.Quat, clampedFrame),
                SampleTranslation(bone.Trans, clampedFrame));
        }
    }

    private static Quaternion SampleRotation(
        XAnimSourceQuatTrack track,
        float frame)
    {
        if (track.Type == XAnimSourceQuatType.None)
            return Quaternion.Identity;

        if (track.Type == XAnimSourceQuatType.Simple)
        {
            IReadOnlyList<XAnimSourceQuat2> frames = track.SimpleFrames;
            if (track.IsConstant || frames.Count == 1)
                return Decode(frames[0]);

            FindKeyPair(track.Indices, frame, out int first, out int second, out float amount);
            return NormalizeLerp(
                Decode(frames[first]),
                Decode(frames[second]),
                amount);
        }

        IReadOnlyList<XAnimSourceQuat> normalFrames = track.NormalFrames;
        if (track.IsConstant || normalFrames.Count == 1)
            return Decode(normalFrames[0]);

        FindKeyPair(track.Indices, frame, out int from, out int to, out float fraction);
        return NormalizeLerp(
            Decode(normalFrames[from]),
            Decode(normalFrames[to]),
            fraction);
    }

    private static Vector3 SampleTranslation(
        XAnimSourceTransTrack track,
        float frame)
    {
        if (track.Type == XAnimSourceTransType.None)
            return Vector3.Zero;
        if (track.Type == XAnimSourceTransType.Constant)
            return ToVector(track.Constant);

        FindKeyPair(track.Indices, frame, out int first, out int second, out float amount);
        Vector3 from;
        Vector3 to;
        if (track.Type == XAnimSourceTransType.Small)
        {
            from = Decode(track.SmallFrames[first], track.Mins, track.Size);
            to = Decode(track.SmallFrames[second], track.Mins, track.Size);
        }
        else
        {
            from = Decode(track.LargeFrames[first], track.Mins, track.Size);
            to = Decode(track.LargeFrames[second], track.Mins, track.Size);
        }

        return Vector3.Lerp(from, to, amount);
    }

    private static void FindKeyPair(
        IReadOnlyList<ushort> indices,
        float frame,
        out int first,
        out int second,
        out float amount)
    {
        if (indices.Count == 0)
            throw new InvalidDataException("A dynamic XAnim track has no frame indices.");
        if (frame <= indices[0])
        {
            first = second = 0;
            amount = 0.0f;
            return;
        }

        int last = indices.Count - 1;
        if (frame >= indices[last])
        {
            first = second = last;
            amount = 0.0f;
            return;
        }

        int low = 0;
        int high = last;
        while (high - low > 1)
        {
            int middle = low + (high - low) / 2;
            if (frame < indices[middle])
                high = middle;
            else
                low = middle;
        }

        first = low;
        second = high;
        amount = (frame - indices[first]) /
            (indices[second] - indices[first]);
    }

    private static Quaternion Decode(XAnimSourceQuat2 value) =>
        Normalize(new Quaternion(
            0.0f,
            0.0f,
            value.Value0 * QuaternionScale,
            value.Value1 * QuaternionScale));

    private static Quaternion Decode(XAnimSourceQuat value) =>
        Normalize(new Quaternion(
            value.Value0 * QuaternionScale,
            value.Value1 * QuaternionScale,
            value.Value2 * QuaternionScale,
            value.Value3 * QuaternionScale));

    private static Vector3 Decode(
        XAnimSourceSmallTrans value,
        XAnimSourceVec3 mins,
        XAnimSourceVec3 size) =>
        ToVector(mins) + new Vector3(
            value.X * size.X,
            value.Y * size.Y,
            value.Z * size.Z);

    private static Vector3 Decode(
        XAnimSourceLargeTrans value,
        XAnimSourceVec3 mins,
        XAnimSourceVec3 size) =>
        ToVector(mins) + new Vector3(
            unchecked((ushort)value.X) * size.X,
            unchecked((ushort)value.Y) * size.Y,
            unchecked((ushort)value.Z) * size.Z);

    private static Vector3 ToVector(XAnimSourceVec3 value) =>
        new(value.X, value.Y, value.Z);

    private static Quaternion NormalizeLerp(
        Quaternion from,
        Quaternion to,
        float amount)
    {
        if (Quaternion.Dot(from, to) < 0.0f)
            to = new Quaternion(-to.X, -to.Y, -to.Z, -to.W);
        return Normalize(new Quaternion(
            from.X + (to.X - from.X) * amount,
            from.Y + (to.Y - from.Y) * amount,
            from.Z + (to.Z - from.Z) * amount,
            from.W + (to.W - from.W) * amount));
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
            return Quaternion.Identity;
        float inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
        return new Quaternion(
            value.X * inverseLength,
            value.Y * inverseLength,
            value.Z * inverseLength,
            value.W * inverseLength);
    }
}
