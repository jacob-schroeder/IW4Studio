using System.Diagnostics;
using System.Numerics;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;

namespace IW4.Render.EditorPreview;

// Source FX preview. Sampling is deterministic and all source/model I/O happens during Load.
public sealed class FxSpritePreview
{
    private const int BoundsSampleWindowMilliseconds = 8_000;
    private const int MaximumSprites = 128;
    private const int MaximumSequencesPerElement = 256;
    private readonly EffectNode _effect;
    private readonly Stopwatch _clock = new();
    private readonly Matrix4x4 _orientation;
    private readonly string[] _materials;
    private double _timelineOffsetMilliseconds;
    private bool _repeat;

    private FxSpritePreview(EffectNode effect, Vector3 origin, Matrix4x4 orientation,
        IReadOnlyCollection<string> notices)
    {
        _effect = effect;
        Origin = origin;
        _orientation = orientation;
        _materials = effect.Materials.Distinct(StringComparer.Ordinal).ToArray();
        HasDrawableElements = _materials.Length != 0;
        Notice = string.Join("; ", notices);
        PreviewBounds = EstimateBounds(effect, out RenderBounds focusBounds);
        FocusBounds = focusBounds;
        _clock.Start();
    }

    private FxSpritePreview(FxSpritePreview prototype, Vector3 origin, Matrix4x4 orientation)
    {
        _effect = prototype._effect;
        _orientation = orientation;
        _materials = prototype._materials;
        Origin = origin;
        HasDrawableElements = prototype.HasDrawableElements;
        Notice = prototype.Notice;
        PreviewBounds = prototype.PreviewBounds;
        FocusBounds = prototype.FocusBounds;
        _clock.Start();
    }

    public Vector3 Origin { get; }
    public string? Notice { get; }
    public bool HasDrawableElements { get; }
    public bool IsPaused => !_clock.IsRunning;
    public bool IsLooping => double.IsPositiveInfinity(_effect.DurationMilliseconds);
    public bool IsFinished => !IsLooping && !Repeat &&
        TimelineMilliseconds >= _effect.DurationMilliseconds;
    public bool IsPlaying => !IsPaused && !IsFinished;
    public bool Repeat
    {
        get => _repeat;
        set
        {
            if (_repeat == value) return;
            if (!IsLooping && _effect.DurationMilliseconds > 0)
            {
                double elapsed = TimelineMilliseconds;
                if (_repeat)
                    _timelineOffsetMilliseconds -= Math.Floor(elapsed / _effect.DurationMilliseconds) *
                        _effect.DurationMilliseconds;
                else if (elapsed >= _effect.DurationMilliseconds)
                    _timelineOffsetMilliseconds -= elapsed;
            }
            _repeat = value;
        }
    }
    private double TimelineMilliseconds => _clock.Elapsed.TotalMilliseconds + _timelineOffsetMilliseconds;
    public IReadOnlyList<string> Materials => _materials;
    public (Vector3 Min, Vector3 Max) PreviewBounds { get; }
    internal RenderBounds FocusBounds { get; }

    public FxSpritePreview CreateInstance() => new(this, Origin, _orientation);

    public FxSpritePreview CreateInstance(Vector3 origin, Matrix4x4 orientation) =>
        new(this, origin, orientation);

    public void SetPaused(bool paused)
    {
        if (paused) _clock.Stop();
        else _clock.Start();
    }

    public void Restart()
    {
        bool paused = IsPaused;
        _clock.Reset();
        _timelineOffsetMilliseconds = 0;
        if (!paused) _clock.Start();
    }

    public static FxSpritePreview Load(string sourceDirectory, string assetName, Vector3 origin,
        Matrix4x4 orientation)
    {
        var exchange = new FxExchange();
        return LoadGraph(assetName, origin, orientation,
            name => exchange.Link(sourceDirectory, name),
            name => FxModelPreviewGeometry.Load(sourceDirectory, name),
            includeSimplifiedMaterialNotice: true);
    }

    public static FxSpritePreview Load(FxEffectDefAsset effect, Vector3 origin, Matrix4x4 orientation,
        Func<string, FxEffectDefAsset> resolveEffect,
        Func<string, IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)>> resolveModel,
        int? selectedElementIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effect.Name);
        return LoadGraph(effect.Name, origin, orientation,
            name => name == effect.Name ? effect : resolveEffect(name), resolveModel,
            selectedElementIndex);
    }

    private static FxSpritePreview LoadGraph(string assetName, Vector3 origin, Matrix4x4 orientation,
        Func<string, FxEffectDefAsset> resolveEffect,
        Func<string, IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)>> resolveModel,
        int? selectedElementIndex = null,
        bool includeSimplifiedMaterialNotice = false)
    {
        var cache = new Dictionary<string, EffectNode>(StringComparer.Ordinal);
        var modelCache = new Dictionary<string, IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)>>(StringComparer.Ordinal);
        var notices = new HashSet<string>(StringComparer.Ordinal);
        EffectNode LoadEffect(string name, HashSet<string> ancestry, bool root = false)
        {
            if (cache.TryGetValue(name, out EffectNode? cached)) return cached;
            if (ancestry.Count >= 4 || !ancestry.Add(name))
            {
                notices.Add("nested or cyclic runner graph omitted");
                return new EffectNode([], 0, 0);
            }
            FxEffectDefAsset effect = resolveEffect(name);
            ValidateCounts(effect);
            var elements = new List<ElementNode>();
            for (int index = 0; index < effect.ElemDefs.Count; index++)
            {
                if (root && selectedElementIndex is { } selected && index != selected)
                    continue;
                FxElemDef element = effect.ElemDefs[index];
                if (index >= effect.ElemDefCountLooping + effect.ElemDefCountOneShot)
                {
                    notices.Add("emitted elements omitted");
                    continue;
                }
                if (element.EffectEmitted.Name is not null || element.EffectOnImpact.Name is not null ||
                    element.EffectOnDeath.Name is not null)
                    notices.Add("emission and impact/death child effects omitted");
                if (element.Flags.UsesCollision)
                    notices.Add("collision omitted");
                if (element.Flags.UsesModelPhysics)
                    notices.Add("model physics omitted");
                if (element.ElemType is FxElemType.OmniLight or FxElemType.SpotLight)
                {
                    notices.Add("FX lights omitted");
                    continue;
                }
                if (element.ElemType is not (FxElemType.SpriteBillboard or FxElemType.SpriteOriented or
                    FxElemType.Tail or FxElemType.Cloud or FxElemType.SparkCloud or
                    FxElemType.Model or FxElemType.Runner))
                {
                    notices.Add($"{element.ElemType} elements omitted");
                    continue;
                }
                if (element.VisualCount == 0 || element.SpawnOrigin.Count != 3 ||
                    (element.ElemType != FxElemType.Runner && element.VisSamples.Count == 0))
                {
                    notices.Add($"incomplete {element.ElemType} element omitted");
                    continue;
                }
                var visuals = new List<VisualNode>();
                foreach (FxElemDefVisuals visual in element.VisualCount > 1
                    ? element.VisualArray : new[] { element.Visuals })
                {
                    string? material = visual.Material?.Material?.Info.Name?.TrimStart(',');
                    if (material is { Length: > 0 })
                        visuals.Add(new VisualNode(material, null, null));
                    else if (visual.Model?.Model?.Name is { Length: > 0 } modelName)
                    {
                        if (!modelCache.TryGetValue(modelName, out var geometry))
                        {
                            try { geometry = resolveModel(modelName); }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                UnauthorizedAccessException or ArgumentException or NotSupportedException or
                                System.Text.Json.JsonException)
                            {
                                notices.Add($"model {modelName} unavailable");
                                geometry = [];
                            }
                            modelCache.Add(modelName, geometry);
                        }
                        if (geometry.Count == 0) notices.Add($"model {modelName} unavailable");
                        visuals.Add(new VisualNode(null, geometry, null));
                    }
                    else if (visual.Effect?.EffectDef.Name is { Length: > 0 } childName)
                    {
                        try { visuals.Add(new VisualNode(null, null, LoadEffect(childName, ancestry))); }
                        catch (Exception exception) when (exception is IOException or InvalidDataException or
                            UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                        {
                            ancestry.Remove(childName);
                            notices.Add($"runner child {childName} unavailable");
                            visuals.Add(new VisualNode(null, null, null));
                        }
                    }
                    else
                        visuals.Add(new VisualNode(null, null, null));
                }
                if (visuals.Count != element.VisualCount)
                    notices.Add($"incomplete {element.ElemType} visual array");
                if (visuals.Count == 0) continue;
                elements.Add(new ElementNode(index, element, index < effect.ElemDefCountLooping, visuals));
            }
            ancestry.Remove(name);
            double loopingLife = effect.MsecLoopingLife == int.MaxValue
                ? double.PositiveInfinity : Math.Max(0, effect.MsecLoopingLife);
            var node = new EffectNode(elements, CalculateDuration(elements, loopingLife), loopingLife);
            cache.Add(name, node);
            return node;
        }
        EffectNode root = LoadEffect(assetName, new HashSet<string>(StringComparer.Ordinal), root: true);
        if (root.Elements.Any(node => node.Definition.ElemType is FxElemType.Cloud or FxElemType.SparkCloud))
            notices.Add("cloud and spark-cloud shapes are editor approximations");
        if (root.Materials.Any() && includeSimplifiedMaterialNotice)
            notices.Add("Particle lighting and soft intersections are approximated.");
        return new FxSpritePreview(root, origin, orientation, notices);
    }

    public IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> Sample(Vector3 eye,
        bool applyDistanceFade = false) =>
        Sample(eye, Origin, _orientation, MaximumSprites, applyDistanceFade);

    public IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> Sample(
        Vector3 eye, out float materialTimeSeconds,
        bool applyDistanceFade = false)
    {
        double time = SampleTimeMilliseconds();
        materialTimeSeconds = (float)(time / 1000d);
        return SampleAtTime(eye, Origin, _orientation, MaximumSprites,
            applyDistanceFade, variationSeed: 0, time);
    }

    public IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> Sample(Vector3 eye, Vector3 origin,
        Matrix4x4 orientation, int maximumSprites, bool applyDistanceFade = false,
        uint variationSeed = 0) =>
        SampleAtTime(eye, origin, orientation, maximumSprites,
            applyDistanceFade, variationSeed, SampleTimeMilliseconds());

    private double SampleTimeMilliseconds()
    {
        double time = TimelineMilliseconds;
        if (Repeat && !IsLooping && _effect.DurationMilliseconds > 0)
            time %= _effect.DurationMilliseconds;
        return time;
    }

    private IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> SampleAtTime(
        Vector3 eye, Vector3 origin, Matrix4x4 orientation, int maximumSprites,
        bool applyDistanceFade, uint variationSeed, double time)
    {
        int budget = Math.Clamp(maximumSprites, 0, MaximumSprites) * 6;
        if (budget == 0 || !HasDrawableElements ||
            (!IsLooping && !Repeat && time >= _effect.DurationMilliseconds)) return [];
        var batches = new Dictionary<string, List<FxPreviewVertex>>(StringComparer.Ordinal);
        SampleEffect(_effect, time, eye, origin, orientation, Mix(0x46585052u ^ variationSeed),
            applyDistanceFade, batches, ref budget);
        return batches.Select(batch => (batch.Key, batch.Value.ToArray())).ToArray();
    }

    private static void SampleEffect(EffectNode effect, double time, Vector3 eye, Vector3 origin,
        Matrix4x4 orientation, uint parentSeed, bool applyDistanceFade,
        Dictionary<string, List<FxPreviewVertex>> batches, ref int budget)
    {
        for (int elementIndex = 0; elementIndex < effect.Elements.Count; elementIndex++)
        {
            if (budget < 3) return;
            ElementNode node = effect.Elements[elementIndex];
            FxElemDef element = node.Definition;
            long first, last;
            int interval = 0;
            if (node.Looping)
            {
                FxSpawnDefLooping spawn = element.Spawn.Looping;
                interval = spawn.IntervalMsec;
                if (interval <= 0 || spawn.Count <= 0) continue;
                long count = spawn.Count == int.MaxValue ? long.MaxValue : spawn.Count;
                double lastSpawnTime = Math.Min(time, effect.LoopingLifeMilliseconds);
                last = Math.Min(count, Math.Max(0, (long)Math.Floor(lastSpawnTime / interval) + 1));
                double active = Maximum(element.SpawnDelayMsec) + ActiveDuration(node);
                first = double.IsPositiveInfinity(active) ? 0 :
                    Math.Max(0, (long)Math.Floor((time - active) / interval));
                first = Math.Max(first, last - MaximumSequencesPerElement);
            }
            else
            {
                FxSpawnDefOneShot spawn = element.Spawn.OneShotCount;
                long count = spawn.Base + (long)(spawn.Amplitude *
                    Random(parentSeed, (uint)(node.Index + 31)));
                first = 0;
                last = Math.Clamp(count, 0, MaximumSequencesPerElement);
            }
            int elementBudget = Math.Max(6, budget / (effect.Elements.Count - elementIndex));
            for (long sequence = first; sequence < last && elementBudget >= 3 && budget >= 3; sequence++)
            {
                uint seed = Mix(parentSeed ^ ((uint)(node.Index + 1) * 0x9e3779b9u) ^
                    ((uint)(sequence + 1) * 0x85ebca6bu));
                double birth = sequence * (double)interval;
                birth += SampleRange(element.SpawnDelayMsec.Base, element.SpawnDelayMsec.Amplitude,
                    Random(seed, 0));
                double elapsed = time - birth;
                if (elapsed < 0 || elapsed >= ActiveDuration(node)) continue;
                VisualNode visual = node.Visuals[Math.Min(node.Visuals.Count - 1,
                    (int)(Random(seed, 1) * node.Visuals.Count))];
                if (element.ElemType == FxElemType.Runner)
                {
                    if (visual.Child is null) continue;
                    Vector3 childOrigin = SampleOrigin(element, seed, origin, orientation);
                    int childBudget = Math.Min(budget, elementBudget);
                    SampleEffect(visual.Child, elapsed, eye, childOrigin,
                        ElementOrientation(element, orientation, seed), seed,
                        applyDistanceFade, batches, ref childBudget);
                    int used = Math.Min(budget, elementBudget) - childBudget;
                    budget -= used;
                    elementBudget -= used;
                    continue;
                }
                float life = SampleRange(element.LifeSpanMsec.Base, element.LifeSpanMsec.Amplitude,
                    Random(seed, 5));
                if (life <= 0 || elapsed >= life) continue;
                float age = (float)elapsed;
                float fractionLife = Math.Clamp(age / life, 0, MathF.BitDecrement(1));
                SampleVisual(element, fractionLife, out FxElemVisStateSample state,
                    out FxElemVisStateSample next, out float fraction);
                Vector4 color = Vector4.Lerp(SampleColor(state, seed), SampleColor(next, seed), fraction);
                if (color.W <= 0) continue;
                Vector3 spawned = SampleOrigin(element, seed, origin, orientation);
                Vector3 localMotion = SampleDisplacement(element, fractionLife, life, seed, world: false);
                Vector3 worldMotion = SampleDisplacement(element, fractionLife, life, seed, world: true);
                float gravity = SampleRange(element.Gravity.Base, element.Gravity.Amplitude, Random(seed, 19));
                Matrix4x4 elementOrientation = ElementOrientation(element, orientation, seed);
                Vector3 center = spawned + Vector3.TransformNormal(localMotion, elementOrientation) +
                    worldMotion - Vector3.UnitZ * (400f * gravity * age * age / 1_000_000f);
                if (applyDistanceFade)
                {
                    color.W *= DistanceFade(element, Vector3.Distance(eye, center));
                    if (color.W <= 0) continue;
                }
                float rotation = SampleRange(element.InitialRotation.Base, element.InitialRotation.Amplitude,
                    Random(seed, 20));
                float halfSquared = fraction * fraction * 0.5f;
                float rotationRandom = Random(seed, 21);
                rotation += life * (SampleRange(state.Base.RotationTotal, state.Amplitude.RotationTotal,
                    rotationRandom) + SampleRange(state.Base.RotationDelta, state.Amplitude.RotationDelta,
                    rotationRandom) * (fraction - halfSquared) +
                    SampleRange(next.Base.RotationDelta, next.Amplitude.RotationDelta,
                        rotationRandom) * halfSquared);
                if (element.ElemType == FxElemType.Model)
                {
                    float scale = VisualSize(state.Base.Scale, state.Amplitude.Scale,
                        next.Base.Scale, next.Amplitude.Scale, fraction, Random(seed, 7));
                    int before = budget;
                    budget = Math.Min(budget, elementBudget);
                    AddModel(batches, visual.Model, center, elementOrientation, element, age, seed,
                        scale, color, ref budget);
                    int modelUsed = Math.Min(before, elementBudget) - budget;
                    budget = before - modelUsed;
                    elementBudget -= modelUsed;
                    continue;
                }
                if (visual.Material is null || budget < 6 || elementBudget < 6) continue;
                float size0 = VisualSize(state.Base.Size0, state.Amplitude.Size0,
                    next.Base.Size0, next.Amplitude.Size0, fraction, Random(seed, 7));
                float size1 = element.Flags.HasNonuniformScale
                    ? VisualSize(state.Base.Size1, state.Amplitude.Size1,
                        next.Base.Size1, next.Amplitude.Size1, fraction, Random(seed, 8)) : size0;
                if (element.ElemType == FxElemType.Cloud)
                {
                    // Native cloud placement carries the visual scale separately
                    // from the two visual radii. A quad approximates that volume.
                    float scale = VisualSize(state.Base.Scale, state.Amplitude.Scale,
                        next.Base.Scale, next.Amplitude.Scale, fraction, Random(seed, 9));
                    size0 *= scale;
                    size1 *= scale;
                }
                if (!float.IsFinite(size0) || !float.IsFinite(size1) || size0 <= 0 || size1 <= 0) continue;
                Vector3 velocity = SampleVelocity(element, fractionLife, seed, elementOrientation);
                Vector4 uv = AtlasUv(element.Atlas, SampleAtlas(element.Atlas, fractionLife, age,
                    (int)sequence, seed));
                Matrix4x4 spriteOrientation = element.ElemType == FxElemType.SpriteOriented
                    ? ElementAxis(element, age, seed, elementOrientation) : elementOrientation;
                AddParticle(batches, visual.Material, element.ElemType, eye, center, velocity,
                    spriteOrientation, size0, size1, rotation, color, uv);
                budget -= 6;
                elementBudget -= 6;
            }
        }
    }

    private static void AddModel(Dictionary<string, List<FxPreviewVertex>> batches,
        IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)>? geometry, Vector3 center,
        Matrix4x4 orientation, FxElemDef element, float age, uint seed, float scale,
        Vector4 color, ref int budget)
    {
        if (geometry is null || !float.IsFinite(scale) || scale <= 0 || budget < 3 ||
            geometry.Sum(batch => batch.Vertices.Length) > budget) return;
        Matrix4x4 rotation = ElementAxis(element, age, seed, orientation);
        foreach ((string material, FxPreviewVertex[] vertices) in geometry)
        {
            if (!batches.TryGetValue(material, out List<FxPreviewVertex>? output))
                batches.Add(material, output = []);
            int triangles = vertices.Length / 3;
            for (int index = 0; index < triangles * 3; index++)
            {
                FxPreviewVertex vertex = vertices[index];
                Vector3 position = center + Vector3.TransformNormal(vertex.Position * scale, rotation);
                Vector3 normal = Vector3.TransformNormal(vertex.Normal, rotation);
                if (normal.LengthSquared() > 0.0001f) normal = Vector3.Normalize(normal);
                Vector3 tangent = vertex.HasTangent
                    ? Vector3.Normalize(Vector3.TransformNormal(vertex.Tangent, rotation))
                    : default;
                output.Add(new FxPreviewVertex(position, normal, vertex.Uv, vertex.Color * color,
                    tangent, vertex.HasTangent, vertex.PositionW));
            }
            budget -= triangles * 3;
            if (budget < 3) break;
        }
    }

    private static Matrix4x4 ElementAxis(FxElemDef element, float age, uint seed,
        Matrix4x4 orientation)
    {
        if (element.SpawnAngles.Count < 3 || element.AngularVelocity.Count < 3)
            return orientation;
        Vector3 angles = new(
            SampleRange(element.SpawnAngles[0].Base, element.SpawnAngles[0].Amplitude, Random(seed, 12)) +
                age * SampleRange(element.AngularVelocity[0].Base, element.AngularVelocity[0].Amplitude, Random(seed, 3)),
            SampleRange(element.SpawnAngles[1].Base, element.SpawnAngles[1].Amplitude, Random(seed, 13)) +
                age * SampleRange(element.AngularVelocity[1].Base, element.AngularVelocity[1].Amplitude, Random(seed, 4)),
            SampleRange(element.SpawnAngles[2].Base, element.SpawnAngles[2].Amplitude, Random(seed, 14)) +
                age * SampleRange(element.AngularVelocity[2].Base, element.AngularVelocity[2].Amplitude, Random(seed, 5)));
        return Matrix4x4.CreateRotationX(angles.Z) *
            Matrix4x4.CreateRotationY(angles.X) * Matrix4x4.CreateRotationZ(angles.Y) * orientation;
    }

    private static (Vector3 Min, Vector3 Max) EstimateBounds(EffectNode effect, out RenderBounds focusBounds)
    {
        // The map camera uses the travel envelope. The asset editor focuses on the
        // strongest visible moment, so a few falling shards cannot pull it off the burst.
        Vector3 min = new(-8), max = new(8);
        double window = double.IsPositiveInfinity(effect.DurationMilliseconds)
            ? BoundsSampleWindowMilliseconds : effect.DurationMilliseconds;
        double interval = window / 25;
        double focusWeight = 0;
        var framePoints = new List<(Vector3 Position, float Weight)>();
        var focusPoints = new List<(Vector3 Position, float Weight)>();
        for (int frame = 0; frame <= 24; frame++)
        {
            // Include the opening burst even when a long-lived layer dominates the duration.
            double time = frame == 0 ? Math.Min(32, interval * 0.5) : frame * interval;
            int budget = MaximumSprites * 6;
            var batches = new Dictionary<string, List<FxPreviewVertex>>(StringComparer.Ordinal);
            SampleEffect(effect, time, new Vector3(128, -128, 96),
                Vector3.Zero, Matrix4x4.Identity, Mix(0x46585052u), false, batches, ref budget);
            framePoints.Clear();
            double frameWeight = 0;
            foreach (var vertices in batches.Values)
            {
                for (int i = 0; i < vertices.Count; i += 3)
                {
                    var a = vertices[i];
                    var b = vertices[i + 1];
                    var c = vertices[i + 2];
                    if (frame != 0)
                    {
                        IncludeTravelVertex(a);
                        IncludeTravelVertex(b);
                        IncludeTravelVertex(c);
                    }
                    float area = Vector3.Cross(b.Position - a.Position, c.Position - a.Position).Length();
                    float weight = area * (a.Color.W + b.Color.W + c.Color.W) / 3f;
                    if (!float.IsFinite(weight) || weight <= 0) continue;
                    frameWeight += 3d * weight;
                    framePoints.Add((a.Position, weight));
                    framePoints.Add((b.Position, weight));
                    framePoints.Add((c.Position, weight));
                }
            }
            // Prefer the earlier burst when later samples have similar visible area.
            if (frameWeight > focusWeight * 1.2)
            {
                focusWeight = frameWeight;
                (focusPoints, framePoints) = (framePoints, focusPoints);
            }
        }
        var travel = (Min: min - new Vector3(8), Max: max + new Vector3(8));
        focusBounds = new RenderBounds(travel.Min, travel.Max);
        if (focusPoints.Count > 0)
        {
            var x = FocusRange(p => p.X);
            var y = FocusRange(p => p.Y);
            var z = FocusRange(p => p.Z);
            focusBounds = new RenderBounds(new(x.Min, y.Min, z.Min), new(x.Max, y.Max, z.Max));
        }
        return travel;

        void IncludeTravelVertex(FxPreviewVertex vertex)
        {
            if (vertex.Color.W < 0.1f || !float.IsFinite(vertex.Position.X) ||
                !float.IsFinite(vertex.Position.Y) || !float.IsFinite(vertex.Position.Z)) return;
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        (float Min, float Max) FocusRange(Func<Vector3, float> axis)
        {
            // Frame the middle 90% of visible area on each axis.
            focusPoints.Sort((a, b) => axis(a.Position).CompareTo(axis(b.Position)));
            double tail = focusWeight * 0.05;
            double accumulated = 0;
            int first = 0;
            while (first < focusPoints.Count - 1 && accumulated + focusPoints[first].Weight <= tail)
                accumulated += focusPoints[first++].Weight;
            accumulated = 0;
            int last = focusPoints.Count - 1;
            while (last > first && accumulated + focusPoints[last].Weight <= tail)
                accumulated += focusPoints[last--].Weight;
            return (axis(focusPoints[first].Position), axis(focusPoints[last].Position));
        }
    }

    private static double CalculateDuration(IReadOnlyList<ElementNode> elements, double loopingLife)
    {
        double duration = 0;
        foreach (ElementNode node in elements)
        {
            FxElemDef element = node.Definition;
            double active = ActiveDuration(node);
            if (node.Looping)
            {
                if (element.Spawn.Looping.Count <= 0 || element.Spawn.Looping.IntervalMsec <= 0) continue;
                double lastSpawn = element.Spawn.Looping.Count == int.MaxValue
                    ? loopingLife : Math.Min(loopingLife, (element.Spawn.Looping.Count - 1d) *
                        element.Spawn.Looping.IntervalMsec);
                duration = Math.Max(duration, lastSpawn + Maximum(element.SpawnDelayMsec) + active);
            }
            else if (element.Spawn.OneShotCount.Base > 0 || element.Spawn.OneShotCount.Amplitude > 0)
                duration = Math.Max(duration, Maximum(element.SpawnDelayMsec) + active);
        }
        return duration;
    }

    private static double ActiveDuration(ElementNode node)
    {
        if (node.Definition.ElemType == FxElemType.Runner)
            return node.Visuals.Max(visual => visual.Child?.DurationMilliseconds ?? 0);
        return Math.Max(0, Maximum(node.Definition.LifeSpanMsec));
    }

    private static long Maximum(FxIntRange range) =>
        Math.Max(range.Base, (long)range.Base + range.Amplitude);

    private static float DistanceFade(FxElemDef element, float distance)
    {
        float fade = 1;
        if (element.FadeInRange.Amplitude > 0)
            fade = MathF.Min(fade, Math.Clamp(1 -
                (distance - element.FadeInRange.Base) / element.FadeInRange.Amplitude, 0, 1));
        if (element.FadeOutRange.Amplitude > 0)
            fade = MathF.Min(fade, Math.Clamp(
                (distance - element.FadeOutRange.Base) / element.FadeOutRange.Amplitude, 0, 1));
        return fade;
    }

    private static void ValidateCounts(FxEffectDefAsset effect)
    {
        if (effect.ElemDefCountLooping < 0 || effect.ElemDefCountOneShot < 0 ||
            effect.ElemDefCountEmission < 0 || effect.ElemDefCount != effect.ElemDefs.Count)
            throw new InvalidDataException("FX element counts are invalid.");
    }

    private static Vector3 SampleOrigin(FxElemDef element, uint seed, Vector3 origin,
        Matrix4x4 orientation)
    {
        Vector3 source = new(
            SampleRange(element.SpawnOrigin[0].Base, element.SpawnOrigin[0].Amplitude, Random(seed, 2)),
            SampleRange(element.SpawnOrigin[1].Base, element.SpawnOrigin[1].Amplitude, Random(seed, 3)),
            SampleRange(element.SpawnOrigin[2].Base, element.SpawnOrigin[2].Amplitude, Random(seed, 4)));
        // Spawn-relative coordinates rotate with the placed effect; world-relative
        // coordinates stay on world axes. Static editor placements share spawn/now frames.
        Vector3 spawned = origin + (element.Flags.SpawnRelativeToEffect
            ? Vector3.TransformNormal(source, orientation) : source);
        return spawned + SampleOffset(element, seed, orientation);
    }

    private static Vector3 SampleOffset(FxElemDef element, uint seed, Matrix4x4 orientation)
    {
        float radius = SampleRange(element.SpawnOffsetRadius.Base, element.SpawnOffsetRadius.Amplitude,
            Random(seed, 24));
        float angle = Random(seed, 25) * MathF.Tau;
        switch (element.Flags.SpawnOffsetMode)
        {
            case FxSpawnOffsetMode.Sphere:
                float z = Random(seed, 26) * 2 - 1;
                float xy = MathF.Sqrt(Math.Max(0, 1 - z * z));
                return radius * new Vector3(MathF.Cos(angle) * xy, MathF.Sin(angle) * xy, z);
            case FxSpawnOffsetMode.Cylinder:
                float height = SampleRange(element.SpawnOffsetHeight.Base,
                    element.SpawnOffsetHeight.Amplitude, Random(seed, 27));
                return Vector3.TransformNormal(new Vector3(height,
                    radius * MathF.Cos(angle), radius * MathF.Sin(angle)), orientation);
        }
        return Vector3.Zero;
    }

    private static Matrix4x4 ElementOrientation(FxElemDef element, Matrix4x4 orientation, uint seed)
    {
        switch (element.Flags.RunRelativeTo)
        {
            case FxRunRelativeTo.Spawn:
            case FxRunRelativeTo.Effect: // Static placements share the same spawn and current frames.
                return orientation;
            case FxRunRelativeTo.Offset: // Align the local X axis with the sampled offset.
                Vector3 forward = SampleOffset(element, seed, orientation);
                if (forward.LengthSquared() < 0.0001f) return orientation;
                forward = Vector3.Normalize(forward);
                Vector3 reference = MathF.Abs(forward.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitY;
                Vector3 right = Vector3.Normalize(Vector3.Cross(reference, forward));
                Vector3 up = Vector3.Cross(forward, right);
                return new Matrix4x4(forward.X, forward.Y, forward.Z, 0,
                    right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, 0, 0, 0, 1);
            default: // world-relative particles retain world axes after spawning
                return Matrix4x4.Identity;
        }
    }

    private static Vector3 SampleDisplacement(FxElemDef element, float normalizedLife,
        float life, uint seed, bool world)
    {
        bool hasGraph = world ? element.Flags.HasWorldVelocityGraph : element.Flags.HasLocalVelocityGraph;
        if (!hasGraph || element.VelSamples.Count < 2) return Vector3.Zero;
        int segments = element.VelSamples.Count - 1;
        float progress = normalizedLife * segments;
        int full = Math.Min(segments - 1, (int)progress);
        Vector3 displacement = Vector3.Zero;
        for (int index = 0; index <= full; index++)
        {
            float end = index == full ? progress - index : 1;
            FxElemVelStateInFrame a = world ? element.VelSamples[index].World : element.VelSamples[index].Local;
            FxElemVelStateInFrame b = world ? element.VelSamples[index + 1].World : element.VelSamples[index + 1].Local;
            float weightB = end * end * 0.5f;
            float weightA = end - weightB;
            displacement += (SampleVector(a.Velocity, seed) * weightA +
                SampleVector(b.Velocity, seed) * weightB) * (life / segments);
        }
        return displacement;
    }

    private static Vector3 SampleVelocity(FxElemDef element, float normalizedLife, uint seed,
        Matrix4x4 orientation)
    {
        if (element.VelSamples.Count < 2) return Vector3.Zero;
        float progress = normalizedLife * (element.VelSamples.Count - 1);
        int index = Math.Min(element.VelSamples.Count - 2, (int)progress);
        float fraction = progress - index;
        Vector3 local = element.Flags.HasLocalVelocityGraph
            ? Vector3.Lerp(SampleVector(element.VelSamples[index].Local.Velocity, seed),
                SampleVector(element.VelSamples[index + 1].Local.Velocity, seed), fraction)
            : Vector3.Zero;
        Vector3 world = element.Flags.HasWorldVelocityGraph
            ? Vector3.Lerp(SampleVector(element.VelSamples[index].World.Velocity, seed),
                SampleVector(element.VelSamples[index + 1].World.Velocity, seed), fraction)
            : Vector3.Zero;
        return Vector3.TransformNormal(local, orientation) + world;
    }

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

    private static Vector4 SampleColor(FxElemVisStateSample sample, uint seed)
    {
        // The authored colors are endpoints of one range; preserve their hue relationship.
        float random = Random(seed, 14);
        return new Vector4(
            SampleColorChannel(sample.Base.Color.R, sample.Amplitude.Color.R, random),
            SampleColorChannel(sample.Base.Color.G, sample.Amplitude.Color.G, random),
            SampleColorChannel(sample.Base.Color.B, sample.Amplitude.Color.B, random),
            SampleColorChannel(sample.Base.Color.A, sample.Amplitude.Color.A, random));
    }

    private static float SampleColorChannel(byte first, byte last, float random) =>
        (first + (last - first) * random) / 255f;

    private static int SampleAtlas(FxElemAtlas atlas, float normalizedLife, float age,
        int sequence, uint seed)
    {
        if (atlas.EntryCount <= 1) return 0;
        int frame = atlas.GetStartMode() switch
        {
            FxAtlasBehavior.StartRandom => (int)(Random(seed, 22) * atlas.EntryCount),
            FxAtlasBehavior.StartIndexed or FxAtlasBehavior.StartIndexedAlternate =>
                sequence & (atlas.EntryCount - 1),
            _ => atlas.Index
        };
        frame += (atlas.Behavior & FxAtlasBehavior.PlayOverLife) != 0
            ? (int)(normalizedLife * atlas.EntryCount)
            : (int)(atlas.Fps * age / 1000);
        if ((atlas.Behavior & FxAtlasBehavior.LoopOnlyNTimes) != 0 && frame >= atlas.EntryCount * atlas.LoopCount)
            frame = atlas.EntryCount - 1;
        return ((frame % atlas.EntryCount) + atlas.EntryCount) % atlas.EntryCount;
    }

    private static Vector4 AtlasUv(FxElemAtlas atlas, int index)
    {
        int columns = 1 << atlas.ColIndexBits, rows = 1 << atlas.RowIndexBits;
        int column = index & (columns - 1), row = index >> atlas.ColIndexBits;
        return new Vector4((float)column / columns, (float)row / rows,
            (float)(column + 1) / columns, (float)(row + 1) / rows);
    }

    private static void AddParticle(Dictionary<string, List<FxPreviewVertex>> batches, string material,
        FxElemType type, Vector3 eye, Vector3 center, Vector3 velocity, Matrix4x4 orientation,
        float size0, float size1, float rotation, Vector4 color, Vector4 uv)
    {
        Vector3 forward = type == FxElemType.SpriteOriented
            ? Vector3.TransformNormal(Vector3.UnitX, orientation) : eye - center;
        if (forward.LengthSquared() < 0.0001f) forward = Vector3.UnitY;
        forward = Vector3.Normalize(forward);
        Vector3 right, up;
        if (type is FxElemType.Tail or FxElemType.SparkCloud)
        {
            if (velocity.LengthSquared() < 0.0001f) velocity = Vector3.UnitX;
            velocity = Vector3.Normalize(velocity);
            right = Vector3.Cross(velocity, forward);
            if (right.LengthSquared() < 0.0001f)
                right = Vector3.Cross(velocity, MathF.Abs(velocity.Z) < 0.9f
                    ? Vector3.UnitZ : Vector3.UnitY);
            // Spark-cloud vertices are a motion-aligned editor proxy; the
            // native SparkCloud geometry is not available in this renderer.
            right = Vector3.Normalize(right) * (type == FxElemType.SparkCloud ? size1 : size0);
            up = velocity * (type == FxElemType.SparkCloud ? size0 : size1 * 2);
        }
        else
        {
            if (type == FxElemType.SpriteOriented)
            {
                right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, orientation));
                up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, orientation));
            }
            else
            {
                right = Vector3.Cross(Vector3.UnitZ, forward);
                if (right.LengthSquared() < 0.0001f) right = Vector3.UnitX;
                right = Vector3.Normalize(right);
                up = Vector3.Normalize(Vector3.Cross(forward, right));
            }
            float cosine = MathF.Cos(rotation), sine = MathF.Sin(rotation);
            (right, up) = (right * cosine + up * sine, up * cosine - right * sine);
            right *= size0;
            up *= size1;
        }
        Vector3 a = center - right - up, b = center + right - up;
        Vector3 c = type == FxElemType.Tail ? center + right : center + right + up;
        Vector3 d = type == FxElemType.Tail ? center - right : center - right + up;
        if (!batches.TryGetValue(material, out List<FxPreviewVertex>? vertices))
            batches.Add(material, vertices = []);
        // FX code-mesh cards wind clockwise when viewed from the normal.
        // Keep the atlas UV attached to each corner when emitting the triangles.
        vertices.Add(new FxPreviewVertex(a, forward, new Vector2(uv.X, uv.W), color));
        vertices.Add(new FxPreviewVertex(d, forward, new Vector2(uv.X, uv.Y), color));
        vertices.Add(new FxPreviewVertex(c, forward, new Vector2(uv.Z, uv.Y), color));
        vertices.Add(new FxPreviewVertex(c, forward, new Vector2(uv.Z, uv.Y), color));
        vertices.Add(new FxPreviewVertex(b, forward, new Vector2(uv.Z, uv.W), color));
        vertices.Add(new FxPreviewVertex(a, forward, new Vector2(uv.X, uv.W), color));
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

    private sealed record VisualNode(string? Material,
        IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)>? Model, EffectNode? Child);
    private sealed record ElementNode(int Index, FxElemDef Definition, bool Looping, IReadOnlyList<VisualNode> Visuals);
    private sealed record EffectNode(IReadOnlyList<ElementNode> Elements,
        double DurationMilliseconds, double LoopingLifeMilliseconds)
    {
        public IEnumerable<string> Materials
        {
            get
            {
                foreach (ElementNode element in Elements)
                    foreach (VisualNode visual in element.Visuals)
                    {
                        if (visual.Material is { } material) yield return material;
                        if (visual.Model is { } model)
                            foreach (var batch in model) yield return batch.Material;
                        if (visual.Child is { } child)
                            foreach (string childMaterial in child.Materials) yield return childMaterial;
                    }
            }
        }
    }
}
