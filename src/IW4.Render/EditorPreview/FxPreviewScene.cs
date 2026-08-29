using System.Numerics;
using IW4.Assets.Assets.Fx;
using IW4.Render.Transforms;

namespace IW4.Render.EditorPreview;

/// <summary>
/// One drawable element instance in a deterministic FX editor preview frame.
/// Positions and velocities use the renderer coordinate system.
/// </summary>
public readonly record struct FxPreviewInstance(
    int ElementIndex,
    FxElemType ElementType,
    Vector3 Position,
    Vector3 Velocity,
    Vector2 Size,
    float Scale,
    float Rotation,
    Vector4 Color);

/// <summary>Immutable output of sampling an <see cref="FxPreviewScene"/>.</summary>
public sealed class FxPreviewFrame
{
    internal FxPreviewFrame(
        float elapsedMilliseconds,
        IReadOnlyList<FxPreviewInstance> instances)
    {
        ElapsedMilliseconds = elapsedMilliseconds;
        Instances = instances;
    }

    public float ElapsedMilliseconds { get; }

    public IReadOnlyList<FxPreviewInstance> Instances { get; }
}

/// <summary>
/// Backend-neutral, bounded FX editor simulation. It schedules root looping
/// and one-shot rows, evaluates their compiled timing and visual samples, and
/// produces draw instances for the Desktop presentation layer. Specialized
/// engine integrations such as collision, trails, clouds, decals, audio,
/// nested runners, models, and scene lights remain explicit visual proxies.
/// </summary>
public sealed class FxPreviewScene
{
    private const int FixedEffectSeed = 173;
    private const int EngineRandomSeedPeriod = 479;
    private const int MinimumDurationMilliseconds = 1_200;
    private const int MaximumDurationMilliseconds = 8_000;
    private const int MaximumScheduledInstances = 768;
    private const int MaximumInstancesPerElement = 128;
    private const int MaximumDrawInstances = 512;
    private const int PositionIntegrationSteps = 12;
    private const int InfiniteLoopCount = int.MaxValue;

    private readonly FxEffectDefAsset _effect;
    private readonly IReadOnlyList<ScheduledInstance> _schedule;

    private FxPreviewScene(
        FxEffectDefAsset effect,
        int durationMilliseconds,
        bool durationWasCapped,
        IReadOnlyList<ScheduledInstance> schedule,
        bool instanceLimitWasApplied)
    {
        _effect = effect;
        DurationMilliseconds = durationMilliseconds;
        DurationWasCapped = durationWasCapped;
        _schedule = schedule;
        InstanceLimitWasApplied = instanceLimitWasApplied;
    }

    public int DurationMilliseconds { get; }

    public bool DurationWasCapped { get; }

    public bool InstanceLimitWasApplied { get; }

    public int ScheduledInstanceCount => _schedule.Count;

    public static bool TryCreate(
        FxEffectDefAsset effect,
        out FxPreviewScene? scene,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(effect);
        scene = null;

        int declaredCount;
        try
        {
            declaredCount = effect.ElemDefCount;
        }
        catch (OverflowException)
        {
            reason = "The FX element-count sum exceeds Int32.";
            return false;
        }

        if (effect.ElemDefCountLooping < 0 ||
            effect.ElemDefCountOneShot < 0 ||
            effect.ElemDefCountEmission < 0 ||
            declaredCount != effect.ElemDefs.Count)
        {
            reason =
                $"The FX declares {declaredCount} elements but retains " +
                $"{effect.ElemDefs.Count}.";
            return false;
        }

        int uncappedDuration = CalculateDuration(effect);
        int duration = Math.Clamp(
            uncappedDuration,
            MinimumDurationMilliseconds,
            MaximumDurationMilliseconds);
        bool durationWasCapped = uncappedDuration > duration;
        IReadOnlyList<ScheduledInstance> schedule = BuildSchedule(
            effect,
            duration,
            out bool instanceLimitWasApplied);
        scene = new FxPreviewScene(
            effect,
            duration,
            durationWasCapped,
            schedule,
            instanceLimitWasApplied);
        reason = string.Empty;
        return true;
    }

    public FxPreviewFrame Sample(
        float elapsedMilliseconds,
        int? isolatedElementIndex = null)
    {
        float time = float.IsFinite(elapsedMilliseconds)
            ? Math.Clamp(elapsedMilliseconds, 0f, DurationMilliseconds)
            : 0f;
        var instances = new List<FxPreviewInstance>(
            Math.Min(_schedule.Count, MaximumDrawInstances));
        foreach (ScheduledInstance scheduled in _schedule)
        {
            if (instances.Count == MaximumDrawInstances)
                break;
            if (isolatedElementIndex is { } isolated &&
                scheduled.ElementIndex != isolated)
            {
                continue;
            }

            float elapsed = time - scheduled.BeginMilliseconds;
            if (elapsed < 0f || elapsed >= scheduled.VisibleLifeMilliseconds)
                continue;

            FxElemDef element = _effect.ElemDefs[scheduled.ElementIndex];
            float normalizedLife = scheduled.LifeMilliseconds > 0f
                ? Math.Clamp(elapsed / scheduled.LifeMilliseconds, 0f, 1f)
                : 0f;
            VisualState visual = EvaluateVisualState(
                element,
                scheduled.RandomSeed,
                normalizedLife,
                scheduled.LifeMilliseconds);
            Vector3 gamePosition = EvaluateGamePosition(
                element,
                scheduled.RandomSeed,
                elapsed,
                scheduled.LifeMilliseconds);
            Vector3 gameVelocity = EvaluateGameVelocity(
                element,
                scheduled.RandomSeed,
                normalizedLife);
            gameVelocity = ApplyGravityVelocity(
                element,
                scheduled.RandomSeed,
                elapsed,
                gameVelocity);
            instances.Add(new FxPreviewInstance(
                scheduled.ElementIndex,
                element.ElemType,
                RenderCoordinateConverter.GameToRenderPosition(gamePosition),
                RenderCoordinateConverter.GameToRenderPosition(gameVelocity),
                visual.Size,
                visual.Scale,
                visual.Rotation,
                visual.Color));
        }

        return new FxPreviewFrame(
            time,
            Array.AsReadOnly(instances.ToArray()));
    }

    private static int CalculateDuration(FxEffectDefAsset effect)
    {
        long duration = 0;
        int loopingCount = effect.ElemDefCountLooping;
        for (int index = 0; index < loopingCount; index++)
        {
            FxElemDef element = effect.ElemDefs[index];
            if (element.Spawn.Count == InfiniteLoopCount ||
                effect.MsecLoopingLife == InfiniteLoopCount)
            {
                return MaximumDurationMilliseconds + 1;
            }

            long lastSpawn = element.Spawn.Count > 1 &&
                element.Spawn.LoopingIntervalMsec > 0
                    ? (long)element.Spawn.LoopingIntervalMsec *
                      (element.Spawn.Count - 1)
                    : 0;
            duration = Math.Max(
                duration,
                lastSpawn + Maximum(element.SpawnDelayMsec) +
                Math.Max(1, Maximum(element.LifeSpanMsec)));
        }

        int oneShotStop = checked(
            loopingCount + effect.ElemDefCountOneShot);
        for (int index = loopingCount; index < oneShotStop; index++)
        {
            FxElemDef element = effect.ElemDefs[index];
            duration = Math.Max(
                duration,
                (long)Maximum(element.SpawnDelayMsec) +
                Math.Max(1, Maximum(element.LifeSpanMsec)));
        }

        duration = Math.Max(duration, effect.MsecLoopingLife);
        return duration > int.MaxValue ? int.MaxValue : (int)duration;
    }

    private static IReadOnlyList<ScheduledInstance> BuildSchedule(
        FxEffectDefAsset effect,
        int durationMilliseconds,
        out bool instanceLimitWasApplied)
    {
        var schedule = new List<ScheduledInstance>();
        instanceLimitWasApplied = false;
        for (int index = 0;
             index < effect.ElemDefCountLooping &&
             schedule.Count < MaximumScheduledInstances;
             index++)
        {
            FxElemDef element = effect.ElemDefs[index];
            int interval = element.Spawn.LoopingIntervalMsec;
            int requestedCount = element.Spawn.Count;
            int count;
            if (requestedCount == InfiniteLoopCount)
            {
                count = interval > 0
                    ? durationMilliseconds / interval + 1
                    : 1;
            }
            else
            {
                count = Math.Max(1, requestedCount);
            }

            int boundedCount = Math.Min(count, MaximumInstancesPerElement);
            if (boundedCount != count)
                instanceLimitWasApplied = true;
            for (int sequence = 0;
                 sequence < boundedCount &&
                 schedule.Count < MaximumScheduledInstances;
                 sequence++)
            {
                float played = interval > 0
                    ? (float)((long)interval * sequence)
                    : 0f;
                AddScheduledInstance(
                    schedule,
                    element,
                    index,
                    sequence,
                    played);
            }
        }

        int oneShotStart = effect.ElemDefCountLooping;
        int oneShotStop = checked(
            oneShotStart + effect.ElemDefCountOneShot);
        for (int index = oneShotStart;
             index < oneShotStop &&
             schedule.Count < MaximumScheduledInstances;
             index++)
        {
            FxElemDef element = effect.ElemDefs[index];
            int count = SampleIntRange(
                element.Spawn.LoopingIntervalMsec,
                element.Spawn.Count,
                StableRandom(FixedEffectSeed, sequence: 0, key: 19));
            int boundedCount = Math.Min(
                Math.Max(0, count),
                MaximumInstancesPerElement);
            if (boundedCount != count)
                instanceLimitWasApplied = true;
            for (int sequence = 0;
                 sequence < boundedCount &&
                 schedule.Count < MaximumScheduledInstances;
                 sequence++)
            {
                AddScheduledInstance(
                    schedule,
                    element,
                    index,
                    sequence,
                    playedMilliseconds: 0f);
            }
        }

        if (schedule.Count == MaximumScheduledInstances)
            instanceLimitWasApplied = true;
        schedule.Sort(static (left, right) =>
        {
            int begin = left.BeginMilliseconds.CompareTo(
                right.BeginMilliseconds);
            if (begin != 0)
                return begin;
            int element = left.ElementIndex.CompareTo(right.ElementIndex);
            return element != 0
                ? element
                : left.Sequence.CompareTo(right.Sequence);
        });
        return Array.AsReadOnly(schedule.ToArray());
    }

    private static void AddScheduledInstance(
        ICollection<ScheduledInstance> schedule,
        FxElemDef element,
        int elementIndex,
        int sequence,
        float playedMilliseconds)
    {
        int delaySeed = PositiveModulo(
            (long)MathF.Round(playedMilliseconds) +
            element.SpawnDelayMsec.Base +
            FixedEffectSeed +
            296L * (byte)sequence,
            EngineRandomSeedPeriod);
        float delay = SampleIntRange(
            element.SpawnDelayMsec.Base,
            element.SpawnDelayMsec.Amplitude,
            StableRandom(delaySeed, sequence: 0, key: 18));
        float begin = Math.Max(0f, playedMilliseconds + delay);
        int randomSeed = PositiveModulo(
            (long)MathF.Round(begin) + FixedEffectSeed +
            296L * (byte)sequence,
            EngineRandomSeedPeriod);
        float life = Math.Max(
            0f,
            SampleIntRange(
                element.LifeSpanMsec.Base,
                element.LifeSpanMsec.Amplitude,
                StableRandom(randomSeed, sequence: 0, key: 17)));
        float visibleLife = life > 0f
            ? life
            : UsesSpecializedProxy(element.ElemType) ? 650f : 1f;
        schedule.Add(new ScheduledInstance(
            elementIndex,
            sequence,
            begin,
            life,
            visibleLife,
            randomSeed));
    }

    private static Vector3 EvaluateGamePosition(
        FxElemDef element,
        int randomSeed,
        float elapsedMilliseconds,
        float lifeMilliseconds)
    {
        Vector3 position = EvaluateSpawnOrigin(element, randomSeed);
        if (elapsedMilliseconds <= 0f || lifeMilliseconds <= 0f)
            return position;

        int steps = Math.Clamp(
            (int)MathF.Ceiling(
                PositionIntegrationSteps *
                elapsedMilliseconds / lifeMilliseconds),
            1,
            PositionIntegrationSteps);
        float stepMilliseconds = elapsedMilliseconds / steps;
        float previousNormalized = 0f;
        Vector3 previousVelocity = EvaluateGameVelocity(
            element,
            randomSeed,
            previousNormalized);
        for (int step = 1; step <= steps; step++)
        {
            float normalized = Math.Clamp(
                step * stepMilliseconds / lifeMilliseconds,
                0f,
                1f);
            Vector3 velocity = EvaluateGameVelocity(
                element,
                randomSeed,
                normalized);
            position += (previousVelocity + velocity) *
                (0.5f * stepMilliseconds / 1000f);
            previousVelocity = velocity;
        }

        if ((element.Flags & 0x04000000) != 0)
        {
            float gravity = SampleFloatRange(
                element.Gravity,
                StableRandom(randomSeed, sequence: 0, key: 15));
            float seconds = elapsedMilliseconds / 1000f;
            position.Z -= 0.5f * gravity * 800f * seconds * seconds;
        }
        return IsFinite(position) ? position : Vector3.Zero;
    }

    private static Vector3 EvaluateSpawnOrigin(
        FxElemDef element,
        int randomSeed)
    {
        Vector3 origin = Vector3.Zero;
        if (element.SpawnOrigin.Count >= 3)
        {
            origin = new Vector3(
                SampleFloatRange(
                    element.SpawnOrigin[0],
                    StableRandom(randomSeed, sequence: 0, key: 6)),
                SampleFloatRange(
                    element.SpawnOrigin[1],
                    StableRandom(randomSeed, sequence: 0, key: 7)),
                SampleFloatRange(
                    element.SpawnOrigin[2],
                    StableRandom(randomSeed, sequence: 0, key: 8)));
        }

        int offsetMode = element.Flags & 0x30;
        float radius = SampleFloatRange(
            element.SpawnOffsetRadius,
            StableRandom(randomSeed, sequence: 0, key: 11));
        if (offsetMode == 0x10)
        {
            float yaw = StableRandom(randomSeed, sequence: 0, key: 9) *
                MathF.Tau;
            float height = StableRandom(
                               randomSeed,
                               sequence: 0,
                               key: 10) * 2f - 1f;
            float planar = MathF.Sqrt(MathF.Max(0f, 1f - height * height));
            origin += new Vector3(
                planar * MathF.Cos(yaw),
                planar * MathF.Sin(yaw),
                height) * radius;
        }
        else if (offsetMode == 0x20)
        {
            float yaw = StableRandom(randomSeed, sequence: 0, key: 9) *
                MathF.Tau;
            float height = SampleFloatRange(
                element.SpawnOffsetHeight,
                StableRandom(randomSeed, sequence: 0, key: 10));
            origin += new Vector3(
                height,
                radius * MathF.Cos(yaw),
                radius * MathF.Sin(yaw));
        }

        return IsFinite(origin) ? origin : Vector3.Zero;
    }

    private static Vector3 EvaluateGameVelocity(
        FxElemDef element,
        int randomSeed,
        float normalizedLife)
    {
        int intervalCount = element.VelIntervalCount;
        if (intervalCount <= 0 ||
            element.VelSamples.Count < intervalCount + 1)
        {
            return Vector3.Zero;
        }

        float normalized = Math.Clamp(
            normalizedLife,
            0f,
            MathF.BitDecrement(1f));
        float samplePoint = intervalCount * normalized;
        int sampleIndex = Math.Clamp(
            (int)MathF.Floor(samplePoint),
            0,
            intervalCount - 1);
        float fraction = samplePoint - sampleIndex;
        float previousWeight = intervalCount * (1f - fraction);
        float nextWeight = intervalCount * fraction;
        Vector3 random = new(
            StableRandom(randomSeed, sequence: 0, key: 0),
            StableRandom(randomSeed, sequence: 0, key: 1),
            StableRandom(randomSeed, sequence: 0, key: 2));
        FxElemVelStateSample previous = element.VelSamples[sampleIndex];
        FxElemVelStateSample next = element.VelSamples[sampleIndex + 1];
        Vector3 velocity = Vector3.Zero;
        if ((element.Flags & 0x02000000) != 0)
        {
            velocity += InterpolateVelocity(
                previous.World,
                next.World,
                random,
                previousWeight,
                nextWeight);
        }
        if ((element.Flags & 0x01000000) != 0)
        {
            // The editor preview uses an identity effect orientation. The
            // compiled local curve therefore shares the world basis here.
            velocity += InterpolateVelocity(
                previous.Local,
                next.Local,
                random,
                previousWeight,
                nextWeight);
        }

        velocity *= 1000f;
        return IsFinite(velocity) ? velocity : Vector3.Zero;
    }

    private static Vector3 ApplyGravityVelocity(
        FxElemDef element,
        int randomSeed,
        float elapsedMilliseconds,
        Vector3 velocity)
    {
        if ((element.Flags & 0x04000000) == 0)
            return velocity;

        float gravity = SampleFloatRange(
            element.Gravity,
            StableRandom(randomSeed, sequence: 0, key: 15));
        velocity.Z -= gravity * 800f * elapsedMilliseconds / 1000f;
        return IsFinite(velocity) ? velocity : Vector3.Zero;
    }

    private static Vector3 InterpolateVelocity(
        FxElemVelStateInFrame previous,
        FxElemVelStateInFrame next,
        Vector3 random,
        float previousWeight,
        float nextWeight)
    {
        Vector3 previousValue = RangeValue(previous.Velocity, random);
        Vector3 nextValue = RangeValue(next.Velocity, random);
        return previousValue * previousWeight + nextValue * nextWeight;
    }

    private static VisualState EvaluateVisualState(
        FxElemDef element,
        int randomSeed,
        float normalizedLife,
        float lifeMilliseconds)
    {
        int intervalCount = element.VisStateIntervalCount;
        if (element.VisSamples.Count == 0 ||
            intervalCount > 0 &&
            element.VisSamples.Count < intervalCount + 1)
        {
            return FallbackVisualState(element.ElemType);
        }

        int sampleIndex;
        float fraction;
        if (intervalCount == 0)
        {
            sampleIndex = 0;
            fraction = 0f;
        }
        else
        {
            float normalized = Math.Clamp(
                normalizedLife,
                0f,
                MathF.BitDecrement(1f));
            float samplePoint = intervalCount * normalized;
            sampleIndex = Math.Clamp(
                (int)MathF.Floor(samplePoint),
                0,
                intervalCount - 1);
            fraction = samplePoint - sampleIndex;
        }

        FxElemVisStateSample previous = element.VisSamples[sampleIndex];
        FxElemVisStateSample next = intervalCount == 0
            ? previous
            : element.VisSamples[sampleIndex + 1];
        float alternate = StableRandom(randomSeed, sequence: 0, key: 23);
        Vector4 previousColor = InterpolateColorEndpoints(
            previous.Base.Color,
            previous.Amplitude.Color,
            alternate);
        Vector4 nextColor = InterpolateColorEndpoints(
            next.Base.Color,
            next.Amplitude.Color,
            alternate);
        Vector4 color = Vector4.Lerp(previousColor, nextColor, fraction);

        float sizeRandom0 = StableRandom(
            randomSeed,
            sequence: 0,
            key: 26);
        float sizeRandom1 = StableRandom(
            randomSeed,
            sequence: 0,
            key: 27);
        float size0 = InterpolateNumeric(
            previous.Base.Size0,
            previous.Amplitude.Size0,
            next.Base.Size0,
            next.Amplitude.Size0,
            sizeRandom0,
            fraction);
        float size1 = (element.Flags & 0x10000000) != 0
            ? InterpolateNumeric(
                previous.Base.Size1,
                previous.Amplitude.Size1,
                next.Base.Size1,
                next.Amplitude.Size1,
                sizeRandom1,
                fraction)
            : size0;
        float scale = InterpolateNumeric(
            previous.Base.Scale,
            previous.Amplitude.Scale,
            next.Base.Scale,
            next.Amplitude.Scale,
            StableRandom(randomSeed, sequence: 0, key: 28),
            fraction);

        float rotation = SampleFloatRange(
            element.InitialRotation,
            alternate);
        float rotationDeltaRandom = StableRandom(
            randomSeed,
            sequence: 0,
            key: 25);
        float weightNext = fraction * fraction * 0.5f;
        float weightPrevious = fraction - weightNext;
        float previousTotal = previous.Base.RotationTotal +
            previous.Amplitude.RotationTotal * rotationDeltaRandom;
        float previousDelta = previous.Base.RotationDelta +
            previous.Amplitude.RotationDelta * rotationDeltaRandom;
        float nextDelta = next.Base.RotationDelta +
            next.Amplitude.RotationDelta * rotationDeltaRandom;
        rotation += (previousTotal +
            previousDelta * weightPrevious +
            nextDelta * weightNext) * lifeMilliseconds;

        return new VisualState(
            new Vector2(
                SanitizeMagnitude(size0, fallback: 1f),
                SanitizeMagnitude(size1, fallback: 1f)),
            float.IsFinite(scale) ? MathF.Abs(scale) : 1f,
            float.IsFinite(rotation) ? rotation : 0f,
            ClampColor(color));
    }

    private static VisualState FallbackVisualState(FxElemType type)
    {
        Vector4 color = type switch
        {
            FxElemType.OmniLight or FxElemType.SpotLight =>
                new Vector4(1f, 0.78f, 0.28f, 0.9f),
            FxElemType.Sound => new Vector4(0.45f, 0.76f, 1f, 0.9f),
            FxElemType.Runner => new Vector4(0.75f, 0.55f, 1f, 0.9f),
            FxElemType.Decal => new Vector4(0.78f, 0.58f, 0.42f, 0.9f),
            FxElemType.Model => new Vector4(0.65f, 0.72f, 0.8f, 0.9f),
            _ => new Vector4(0.86f, 0.9f, 0.95f, 0.85f)
        };
        return new VisualState(Vector2.One * 4f, 1f, 0f, color);
    }

    private static bool UsesSpecializedProxy(FxElemType type) => type is not
        FxElemType.SpriteBillboard;

    private static int SampleIntRange(int @base, int amplitude, float random)
    {
        if (amplitude <= 0)
            return @base;
        long addition = Math.Min(
            amplitude,
            (long)Math.Floor((amplitude + 1d) * random));
        long value = (long)@base + addition;
        return value > int.MaxValue
            ? int.MaxValue
            : value < int.MinValue ? int.MinValue : (int)value;
    }

    private static int Maximum(FxIntRange range)
    {
        long maximum = (long)range.Base + Math.Max(0, range.Amplitude);
        return maximum > int.MaxValue
            ? int.MaxValue
            : maximum < int.MinValue ? int.MinValue : (int)maximum;
    }

    private static float SampleFloatRange(FxFloatRange range, float random)
    {
        float value = range.Base + range.Amplitude * random;
        return float.IsFinite(value) ? value : 0f;
    }

    private static Vector3 RangeValue(
        FxElemVec3Range range,
        Vector3 random) => new(
        range.Base.X + range.Amplitude.X * random.X,
        range.Base.Y + range.Amplitude.Y * random.Y,
        range.Base.Z + range.Amplitude.Z * random.Z);

    private static float InterpolateNumeric(
        float previousBase,
        float previousAmplitude,
        float nextBase,
        float nextAmplitude,
        float random,
        float fraction)
    {
        float previous = previousBase + previousAmplitude * random;
        float next = nextBase + nextAmplitude * random;
        float value = previous + (next - previous) * fraction;
        return float.IsFinite(value) ? value : 0f;
    }

    private static Vector4 InterpolateColorEndpoints(
        FxElemColor first,
        FxElemColor alternate,
        float fraction) => new(
        InterpolateByte(first.R, alternate.R, fraction),
        InterpolateByte(first.G, alternate.G, fraction),
        InterpolateByte(first.B, alternate.B, fraction),
        InterpolateByte(first.A, alternate.A, fraction));

    private static float InterpolateByte(byte first, byte second, float fraction) =>
        (first + (second - first) * fraction) / 255f;

    private static Vector4 ClampColor(Vector4 value) => new(
        Math.Clamp(float.IsFinite(value.X) ? value.X : 0f, 0f, 1f),
        Math.Clamp(float.IsFinite(value.Y) ? value.Y : 0f, 0f, 1f),
        Math.Clamp(float.IsFinite(value.Z) ? value.Z : 0f, 0f, 1f),
        Math.Clamp(float.IsFinite(value.W) ? value.W : 0f, 0f, 1f));

    private static float SanitizeMagnitude(float value, float fallback)
    {
        if (!float.IsFinite(value))
            return fallback;
        return MathF.Max(0.01f, MathF.Abs(value));
    }

    private static float StableRandom(int seed, int sequence, int key)
    {
        uint value = unchecked((uint)seed);
        value ^= unchecked((uint)sequence) * 0x9E3779B9u;
        value ^= unchecked((uint)key) * 0x85EBCA6Bu;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777216f;
    }

    private static int PositiveModulo(long value, int modulus)
    {
        long result = value % modulus;
        return (int)(result < 0 ? result + modulus : result);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private readonly record struct ScheduledInstance(
        int ElementIndex,
        int Sequence,
        float BeginMilliseconds,
        float LifeMilliseconds,
        float VisibleLifeMilliseconds,
        int RandomSeed);

    private readonly record struct VisualState(
        Vector2 Size,
        float Scale,
        float Rotation,
        Vector4 Color);
}
