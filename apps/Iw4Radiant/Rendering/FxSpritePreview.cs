using System.Diagnostics;
using System.Numerics;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;

namespace Iw4Radiant.Rendering;

// A deliberately narrow viewport renderer for source FX material sprites.
internal sealed class FxSpritePreview
{
    private const int LoopMilliseconds = 8_000;
    private const int MaximumSprites = 128;
    private readonly FxEffectDefAsset _effect;
    private readonly FxEffectDefAsset? _sandChild;
    private readonly int _drawableRunnerVisual;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(int Index, string Material)> _elements = [];
    private readonly Matrix4x4 _orientation;

    private FxSpritePreview(FxEffectDefAsset effect, IReadOnlyList<FxEffectDefAsset>? runnerChildren,
        Vector3 origin, Matrix4x4 orientation)
    {
        ValidateCounts(effect);
        _effect = effect;
        Origin = origin;
        _orientation = orientation;
        if (IsSandChild(effect))
        {
            _sandChild = effect;
            _drawableRunnerVisual = -1;
        }
        else if (runnerChildren is not null)
        {
            _drawableRunnerVisual = FindDrawableRunnerVisual(effect, runnerChildren);
            if (_drawableRunnerVisual < 0)
            {
                Notice = "this effect's particle setup is not supported by the editor preview yet";
                return;
            }
            _sandChild = runnerChildren[_drawableRunnerVisual];
        }
        if (_sandChild is not null)
        {
            for (int index = 0; index < _sandChild.ElemDefs.Count; index++)
                _elements.Add((index, _sandChild.ElemDefs[index].Visuals.Material?.Material?.Info.Name
                    ?? throw new InvalidDataException("Sand child material is missing.")));
            Notice = _drawableRunnerVisual < 0
                ? "one-shot preview repeats every 8 seconds; lighting and soft intersections may differ in game"
                : "animated preview; lighting, distance fading, and soft intersections may differ in game";
            return;
        }

        var unsupported = new List<string>();
        for (int index = 0; index < effect.ElemDefs.Count; index++)
        {
            FxElemDef element = effect.ElemDefs[index];
            if (index >= effect.ElemDefCountLooping)
            {
                unsupported.Add($"element {index}: one-shot or emitted child");
                continue;
            }
            if (element.ElemType != FxElemType.SpriteBillboard)
            {
                unsupported.Add($"element {index}: {element.ElemType}");
                continue;
            }
            if (element.VisualCount != 1 || element.Visuals.Material?.Material?.Info.Name is not { Length: > 0 } material)
            {
                unsupported.Add($"element {index}: material visual");
                continue;
            }
            if (element.Flags is not (0 or 134) || element.Spawn.LoopingIntervalMsec <= 0 ||
                element.Spawn.Count <= 0 || element.Atlas.EntryCount != 1 || element.SpawnOrigin.Count != 3 ||
                element.VisSamples.Count != element.VisStateIntervalCount + 1 ||
                element.SpawnOrigin.Any(range => range.Base != 0 || range.Amplitude != 0) ||
                element.Gravity.Base != 0 || element.Gravity.Amplitude != 0 ||
                element.VelSamples.Any(sample => HasMotion(sample.Local) || HasMotion(sample.World)) ||
                (element.Flags & (0x30 | 0x07000000 | 0x10000000)) != 0 ||
                element.SpawnDelayMsec.Amplitude != 0 || element.LifeSpanMsec.Amplitude != 0 ||
                element.InitialRotation.Base != 0 || element.InitialRotation.Amplitude != 0 ||
                element.VisSamples.Any(sample => sample.Amplitude.Size0 != 0 ||
                    sample.Base.Scale != 0 || sample.Amplitude.Scale != 0 ||
                    sample.Base.RotationDelta != 0 || sample.Base.RotationTotal != 0 ||
                    sample.Amplitude.RotationDelta != 0 || sample.Amplitude.RotationTotal != 0 ||
                    sample.Base.Color != sample.Amplitude.Color))
            {
                unsupported.Add($"element {index}: flags, motion, random range, rotation, or atlas");
                continue;
            }
            if (element.Spawn.Count > MaximumSprites &&
                LoopMilliseconds / element.Spawn.LoopingIntervalMsec + 1 > MaximumSprites)
                unsupported.Add($"element {index}: preview limits each 8-second cycle to {MaximumSprites} spawns");
            _elements.Add((index, material));
        }
        if (_elements.Count != 0)
            unsupported.Insert(0, "FX sprite preview loops an 8-second source-timed window with exported size, color, and material; native distance fade and eye-offset shading are unavailable");
        Notice = string.Join("; ", unsupported);
    }

    internal Vector3 Origin { get; }
    internal string? Notice { get; }
    internal bool HasDrawableElements => _elements.Count != 0;
    internal IReadOnlyList<string> Materials => _elements.Select(element => element.Material).Distinct(StringComparer.Ordinal).ToArray();

    internal static FxSpritePreview Load(string sourceDirectory, string assetName, Vector3 origin,
        Matrix4x4 orientation)
    {
        var exchange = new FxExchange();
        FxEffectDefAsset effect = exchange.Link(sourceDirectory, assetName);
        IReadOnlyList<FxEffectDefAsset>? children = null;
        if (HasRunnerVisuals(effect))
        {
            var loaded = new Dictionary<string, FxEffectDefAsset>(StringComparer.Ordinal);
            children = effect.ElemDefs[0].VisualArray.Select(visual =>
            {
                string name = visual.Effect?.EffectDef.Name
                    ?? throw new InvalidDataException("Runner child reference is missing.");
                if (!loaded.TryGetValue(name, out FxEffectDefAsset? child))
                    loaded.Add(name, child = exchange.Link(sourceDirectory, name));
                return child ?? throw new InvalidDataException("Runner child failed to load.");
            }).ToArray();
        }
        return new FxSpritePreview(effect, children, origin, orientation);
    }

    internal IReadOnlyList<(string Material, SceneVertex[] Vertices)> Sample(Vector3 eye) =>
        Sample(eye, Origin, _orientation, MaximumSprites);

    internal IReadOnlyList<(string Material, SceneVertex[] Vertices)> Sample(Vector3 eye, Vector3 origin,
        Matrix4x4 orientation, int maximumSprites)
    {
        maximumSprites = Math.Clamp(maximumSprites, 0, MaximumSprites);
        if (maximumSprites == 0) return [];
        float time = (float)(_clock.Elapsed.TotalMilliseconds % LoopMilliseconds);
        return _sandChild is { } child
            ? SampleSand(child, eye, origin, orientation, time, maximumSprites)
            : SampleGlow(eye, origin, time, maximumSprites);
    }

    private IReadOnlyList<(string Material, SceneVertex[] Vertices)> SampleSand(FxEffectDefAsset child,
        Vector3 eye, Vector3 origin, Matrix4x4 orientation, float time, int maximumSprites)
    {
        var batches = new Dictionary<string, List<SceneVertex>>(StringComparer.Ordinal);
        bool directChild = _drawableRunnerVisual < 0;
        FxElemDef? runner = directChild ? null : _effect.ElemDefs[0];
        int interval = runner?.Spawn.LoopingIntervalMsec ?? LoopMilliseconds;
        int runnerCount = runner is null ? 1 : Math.Min(LoopMilliseconds / interval, runner.Spawn.Count);
        int spriteCount = 0;
        for (int sequence = 0; sequence < runnerCount && spriteCount < maximumSprites; sequence++)
        {
            float age = time - sequence * interval;
            if (age < 0) age += LoopMilliseconds;
            uint seed = Mix((uint)sequence + 0x53414E44u);
            if (runner is not null &&
                (int)(Random(seed, 0) * runner.VisualCount) != _drawableRunnerVisual) continue;
            Vector3 runnerPosition = runner is null ? origin :
                origin + Vector3.TransformNormal(SampleOrigin(runner, seed), orientation);
            for (int elementIndex = 0; elementIndex < 2 && spriteCount < maximumSprites; elementIndex++)
            {
                FxElemDef element = child.ElemDefs[elementIndex];
                int count = element.Spawn.LoopingIntervalMsec; // one-shot count uses this union field
                string material = _elements[elementIndex].Material;
                for (int particle = 0; particle < count && spriteCount < maximumSprites; particle++)
                {
                    uint particleSeed = Mix(seed ^ (uint)(elementIndex * 32 + particle + 1));
                    float life = SampleRange(element.LifeSpanMsec.Base, element.LifeSpanMsec.Amplitude,
                        Random(particleSeed, 1));
                    if (age >= life || life <= 0) continue;
                    float normalizedLife = Math.Clamp(age / life, 0, MathF.BitDecrement(1));
                    Vector3 localOrigin = SampleOrigin(element, particleSeed);
                    Vector3 displacement = SampleDisplacement(element, normalizedLife, life, particleSeed);
                    Vector3 center = runnerPosition + Vector3.TransformNormal(localOrigin + displacement, orientation);
                    SampleVisual(element, normalizedLife, out FxElemVisStateSample visual,
                        out FxElemVisStateSample next, out float fraction);
                    float size0 = VisualSize(visual.Base.Size0, visual.Amplitude.Size0,
                        next.Base.Size0, next.Amplitude.Size0, fraction, Random(particleSeed, 7));
                    float size1 = element.ElemType == FxElemType.Tail
                        ? VisualSize(visual.Base.Size1, visual.Amplitude.Size1,
                            next.Base.Size1, next.Amplitude.Size1, fraction, Random(particleSeed, 8))
                        : size0;
                    if (!float.IsFinite(size0) || !float.IsFinite(size1) || size0 <= 0 || size1 <= 0)
                        continue;
                    Vector4 color = Vector4.Lerp(
                        SampleColor(visual, particleSeed), SampleColor(next, particleSeed), fraction);
                    if (color.W <= 0) continue;
                    float rotation = SampleRange(element.InitialRotation.Base,
                        element.InitialRotation.Amplitude, Random(particleSeed, 9));
                    int atlasIndex = SampleAtlas(element.Atlas, normalizedLife, age, particleSeed);
                    Vector4 uv = AtlasUv(element.Atlas, atlasIndex);
                    Vector3 direction = element.ElemType == FxElemType.Tail
                        ? Vector3.TransformNormal(SampleVelocity(element, normalizedLife, particleSeed), orientation)
                        : Vector3.Zero;
                    AddParticle(batches, material, element.ElemType, eye, center, direction,
                        size0, size1, rotation, color, uv, sourceSizesAreHalfExtents: true);
                    spriteCount++;
                }
            }
        }
        return batches.Select(batch => (batch.Key, batch.Value.ToArray())).ToArray();
    }

    private IReadOnlyList<(string Material, SceneVertex[] Vertices)> SampleGlow(Vector3 eye, Vector3 origin,
        float time, int maximumSprites)
    {
        var batches = new Dictionary<string, List<SceneVertex>>(StringComparer.Ordinal);
        int spriteCount = 0;
        foreach (var (index, material) in _elements)
        {
            FxElemDef element = _effect.ElemDefs[index];
            int interval = element.Spawn.LoopingIntervalMsec;
            int count = Math.Min(MaximumSprites,
                Math.Min(element.Spawn.Count, LoopMilliseconds / interval + 1));
            for (int sequence = 0; sequence < count && spriteCount < maximumSprites; sequence++)
            {
                float start = sequence * interval;
                start += element.SpawnDelayMsec.Base;
                float age = time - start;
                float life = element.LifeSpanMsec.Base;
                if (age < 0 || life <= 0 || age >= life) continue;
                float normalizedLife = Math.Clamp(age / life, 0, MathF.BitDecrement(1));
                SampleVisual(element, normalizedLife, out FxElemVisStateSample visual,
                    out FxElemVisStateSample next, out float fraction);
                float size = visual.Base.Size0 + (next.Base.Size0 - visual.Base.Size0) * fraction;
                if (!float.IsFinite(size) || size <= 0) continue;
                Vector4 color = Vector4.Lerp(Color(visual.Base.Color), Color(next.Base.Color), fraction);
                if (color.W <= 0) continue;
                AddParticle(batches, material, FxElemType.SpriteBillboard, eye, origin, Vector3.Zero,
                    size, size, 0, color, new Vector4(0, 0, 1, 1), sourceSizesAreHalfExtents: false);
                spriteCount++;
            }
        }
        return batches.Select(batch => (batch.Key, batch.Value.ToArray())).ToArray();
    }

    private static bool HasRunnerVisuals(FxEffectDefAsset effect) =>
        effect.ElemDefs.Count == 1 && effect.ElemDefCountLooping == 1 &&
        effect.ElemDefs[0].ElemType == FxElemType.Runner &&
        effect.ElemDefs[0].VisualCount == 3 && effect.ElemDefs[0].VisualArray.Count == 3;

    private static int FindDrawableRunnerVisual(FxEffectDefAsset runner,
        IReadOnlyList<FxEffectDefAsset> children)
    {
        if (runner.ElemDefCountLooping != 1 || runner.ElemDefCountOneShot != 0 ||
            runner.ElemDefCountEmission != 0 || children.Count != 3)
            return -1;
        FxElemDef trigger = runner.ElemDefs[0];
        if (trigger.ElemType != FxElemType.Runner || trigger.Flags != 0x06000086 ||
            trigger.Spawn.LoopingIntervalMsec != 100 || trigger.Spawn.Count <= 0 ||
            trigger.VisualCount != 3 || trigger.VisualArray.Count != 3 ||
            trigger.SpawnOrigin.Count != 3 || trigger.SpawnDelayMsec.Base != 0 ||
            trigger.SpawnDelayMsec.Amplitude != 0 ||
            !NoAngles(trigger) || trigger.EffectOnImpact.Name is not null ||
            trigger.EffectOnDeath.Name is not null || trigger.EffectEmitted.Name is not null)
            return -1;
        int drawable = -1;
        for (int index = 0; index < children.Count; index++)
        {
            FxEffectDefAsset child = children[index];
            if (IsSandChild(child))
            {
                if (drawable >= 0) return -1;
                drawable = index;
            }
            else if (!IsEmptyChild(child))
                return -1;
        }
        return drawable;
    }

    private static bool IsSandChild(FxEffectDefAsset child)
    {
        ValidateCounts(child);
        if (child.ElemDefCountLooping != 0 || child.ElemDefCountOneShot != 2 ||
            child.ElemDefCountEmission != 0)
            return false;
        return IsSandElement(child.ElemDefs[0], FxElemType.Tail, 0x11000086, 3,
                   5, 0, 16, 2, 2) &&
               IsSandElement(child.ElemDefs[1], FxElemType.SpriteBillboard, 0x01000086, 1,
                   1, 24, 32, 3, 2);
    }

    private static bool IsEmptyChild(FxEffectDefAsset child)
    {
        ValidateCounts(child);
        return child.ElemDefCountLooping == 0 && child.ElemDefCountOneShot == 1 &&
            child.ElemDefCountEmission == 0 && child.ElemDefs[0].ElemType == FxElemType.SpriteBillboard &&
            child.ElemDefs[0].Flags == 0x04000042 &&
            child.ElemDefs[0].EffectOnImpact.Name is null &&
            child.ElemDefs[0].EffectOnDeath.Name is null &&
            child.ElemDefs[0].EffectEmitted.Name is null &&
            child.ElemDefs[0].VisSamples.Count > 0 &&
            child.ElemDefs[0].VisSamples.All(sample =>
                sample.Base.Size0 == 0 && sample.Base.Size1 == 0 &&
                sample.Amplitude.Size0 == 0 && sample.Amplitude.Size1 == 0);
    }

    private static bool IsSandElement(FxElemDef element, FxElemType type, int flags, int count,
        byte atlasBehavior, byte fps, short atlasEntries, byte columnBits, byte rowBits) =>
        element.ElemType == type && element.Flags == flags &&
        element.Spawn.LoopingIntervalMsec == count && element.Spawn.Count == 0 &&
        element.VisualCount == 1 &&
        element.Visuals.Material?.Material?.Info.Name is { Length: > 0 } &&
        element.SpawnOrigin.Count == 3 && element.VelIntervalCount == 1 &&
        element.VelSamples.Count == 2 && element.VisSamples.Count == element.VisStateIntervalCount + 1 &&
        element.Atlas.Behavior == atlasBehavior && element.Atlas.Index == 10 &&
        element.Atlas.Fps == fps && element.Atlas.LoopCount == 1 &&
        element.Atlas.EntryCount == atlasEntries &&
        element.Atlas.ColIndexBits == columnBits && element.Atlas.RowIndexBits == rowBits &&
        element.SpawnDelayMsec.Base == 0 && element.SpawnDelayMsec.Amplitude == 0 &&
        element.LifeSpanMsec.Base > 0 && element.LifeSpanMsec.Amplitude >= 0 &&
        (long)element.LifeSpanMsec.Base + element.LifeSpanMsec.Amplitude < LoopMilliseconds &&
        element.Gravity.Base == 0 && element.Gravity.Amplitude == 0 &&
        NoAngles(element) && element.VelSamples.All(sample =>
            !HasMotion(sample.World)) && element.VisSamples.All(sample =>
            sample.Base.RotationDelta == 0 && sample.Base.RotationTotal == 0 &&
            sample.Amplitude.RotationDelta == 0 && sample.Amplitude.RotationTotal == 0 &&
            sample.Base.Scale == 0 && sample.Amplitude.Scale == 0) &&
        element.EffectOnImpact.Name is null && element.EffectOnDeath.Name is null &&
        element.EffectEmitted.Name is null;

    private static bool NoAngles(FxElemDef element) =>
        element.SpawnAngles.Count == 3 && element.AngularVelocity.Count == 3 &&
        element.SpawnAngles.All(range => range.Base == 0 && range.Amplitude == 0) &&
        element.AngularVelocity.All(range => range.Base == 0 && range.Amplitude == 0);

    private static void ValidateCounts(FxEffectDefAsset effect)
    {
        if (effect.ElemDefCountLooping < 0 || effect.ElemDefCountOneShot < 0 ||
            effect.ElemDefCountEmission < 0 || effect.ElemDefCount != effect.ElemDefs.Count)
            throw new InvalidDataException("FX element counts are invalid.");
    }

    private static Vector3 SampleOrigin(FxElemDef element, uint seed) => new(
        SampleRange(element.SpawnOrigin[0].Base, element.SpawnOrigin[0].Amplitude, Random(seed, 2)),
        SampleRange(element.SpawnOrigin[1].Base, element.SpawnOrigin[1].Amplitude, Random(seed, 3)),
        SampleRange(element.SpawnOrigin[2].Base, element.SpawnOrigin[2].Amplitude, Random(seed, 4)));

    private static Vector3 SampleDisplacement(FxElemDef element, float normalizedLife,
        float life, uint seed)
    {
        FxElemVelStateInFrame first = element.VelSamples[0].Local;
        FxElemVelStateInFrame last = element.VelSamples[1].Local;
        float u = normalizedLife;
        float firstWeight = u - u * u * 0.5f, lastWeight = u * u * 0.5f;
        return life * (SampleVector(first.Velocity, seed) * firstWeight +
            SampleVector(last.Velocity, seed) * lastWeight);
    }

    private static Vector3 SampleVelocity(FxElemDef element, float normalizedLife, uint seed) =>
        Vector3.Lerp(SampleVector(element.VelSamples[0].Local.Velocity, seed),
            SampleVector(element.VelSamples[1].Local.Velocity, seed), normalizedLife);

    private static Vector3 SampleVector(FxElemVec3Range range, uint seed) => new(
        SampleRange(range.Base.X, range.Amplitude.X, Random(seed, 11)),
        SampleRange(range.Base.Y, range.Amplitude.Y, Random(seed, 12)),
        SampleRange(range.Base.Z, range.Amplitude.Z, Random(seed, 13)));

    private static float SampleRange(float value, float amplitude, float random) => value + amplitude * random;

    private static float VisualSize(float firstBase, float firstAmplitude, float lastBase,
        float lastAmplitude, float fraction, float random)
    {
        float first = firstBase + firstAmplitude * random;
        return first + (lastBase + lastAmplitude * random - first) * fraction;
    }

    private static Vector4 SampleColor(FxElemVisStateSample sample, uint seed) => new(
        SampleColorChannel(sample.Base.Color.R, sample.Amplitude.Color.R, Random(seed, 14)),
        SampleColorChannel(sample.Base.Color.G, sample.Amplitude.Color.G, Random(seed, 15)),
        SampleColorChannel(sample.Base.Color.B, sample.Amplitude.Color.B, Random(seed, 16)),
        SampleColorChannel(sample.Base.Color.A, sample.Amplitude.Color.A, Random(seed, 17)));

    private static float SampleColorChannel(byte first, byte last, float random) =>
        (first + (last - first) * random) / 255f;

    private static int SampleAtlas(FxElemAtlas atlas, float normalizedLife, float age, uint seed)
    {
        int frame = (int)(Random(seed, 22) * atlas.EntryCount);
        frame += (atlas.Behavior & 4) != 0
            ? (int)(normalizedLife * atlas.EntryCount)
            : atlas.Fps * (int)age / 1000;
        return frame & (atlas.EntryCount - 1);
    }

    private static Vector4 AtlasUv(FxElemAtlas atlas, int index)
    {
        int columns = 1 << atlas.ColIndexBits, rows = 1 << atlas.RowIndexBits;
        int column = index & (columns - 1), row = index >> atlas.ColIndexBits;
        return new Vector4((float)column / columns, (float)row / rows,
            (float)(column + 1) / columns, (float)(row + 1) / rows);
    }

    private static void AddParticle(Dictionary<string, List<SceneVertex>> batches, string material,
        FxElemType type, Vector3 eye, Vector3 center, Vector3 velocity, float size0, float size1,
        float rotation, Vector4 color, Vector4 uv, bool sourceSizesAreHalfExtents)
    {
        Vector3 forward = eye - center;
        if (forward.LengthSquared() < 0.0001f) forward = Vector3.UnitY;
        forward = Vector3.Normalize(forward);
        Vector3 right, up;
        if (type == FxElemType.Tail)
        {
            if (velocity.LengthSquared() < 0.0001f) velocity = Vector3.UnitX;
            velocity = Vector3.Normalize(velocity);
            right = Vector3.Cross(velocity, forward);
            if (right.LengthSquared() < 0.0001f)
                right = Vector3.Cross(velocity, MathF.Abs(velocity.Z) < 0.9f
                    ? Vector3.UnitZ : Vector3.UnitY);
            right = Vector3.Normalize(right) * (sourceSizesAreHalfExtents ? size0 : size0 * 0.5f);
            up = velocity * (sourceSizesAreHalfExtents ? size1 * 2 : size1);
        }
        else
        {
            right = Vector3.Cross(Vector3.UnitZ, forward);
            if (right.LengthSquared() < 0.0001f) right = Vector3.UnitX;
            right = Vector3.Normalize(right);
            up = Vector3.Normalize(Vector3.Cross(forward, right));
            float cosine = MathF.Cos(rotation), sine = MathF.Sin(rotation);
            (right, up) = (right * cosine + up * sine, up * cosine - right * sine);
            right *= sourceSizesAreHalfExtents ? size0 : size0 * 0.5f;
            up *= sourceSizesAreHalfExtents ? size1 : size1 * 0.5f;
        }
        Vector3 a, b, c, d;
        if (type == FxElemType.Tail)
        {
            a = center - right - up;
            b = center + right - up;
            c = center + right;
            d = center - right;
        }
        else
        {
            a = center - right - up;
            b = center + right - up;
            c = center + right + up;
            d = center - right + up;
        }
        if (!batches.TryGetValue(material, out List<SceneVertex>? vertices))
            batches.Add(material, vertices = []);
        vertices.Add(new SceneVertex(a, forward, new Vector2(uv.X, uv.W), color));
        vertices.Add(new SceneVertex(b, forward, new Vector2(uv.Z, uv.W), color));
        vertices.Add(new SceneVertex(c, forward, new Vector2(uv.Z, uv.Y), color));
        vertices.Add(new SceneVertex(a, forward, new Vector2(uv.X, uv.W), color));
        vertices.Add(new SceneVertex(c, forward, new Vector2(uv.Z, uv.Y), color));
        vertices.Add(new SceneVertex(d, forward, new Vector2(uv.X, uv.Y), color));
    }

    private static void SampleVisual(FxElemDef element, float normalizedLife,
        out FxElemVisStateSample visual, out FxElemVisStateSample next, out float fraction)
    {
        if (element.VisStateIntervalCount == 0)
        {
            visual = next = element.VisSamples[0];
            fraction = 0;
            return;
        }
        float sample = normalizedLife * element.VisStateIntervalCount;
        int index = Math.Min(element.VisStateIntervalCount - 1, (int)sample);
        fraction = sample - index;
        visual = element.VisSamples[index];
        next = element.VisSamples[index + 1];
    }

    private static Vector4 Color(FxElemColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352du;
        value ^= value >> 15;
        value *= 0x846ca68bu;
        return value ^ (value >> 16);
    }

    private static float Random(uint seed, uint channel) =>
        (Mix(seed ^ (channel * 0x9e3779b9u)) >> 8) * (1f / 16777216f);

    private static bool HasMotion(FxElemVelStateInFrame frame) =>
        HasVector(frame.Velocity.Base) || HasVector(frame.Velocity.Amplitude) ||
        HasVector(frame.TotalDelta.Base) || HasVector(frame.TotalDelta.Amplitude);

    private static bool HasVector(Vec3 vector) =>
        vector.X != 0 || vector.Y != 0 || vector.Z != 0;
}
