using System.Diagnostics;
using System.Numerics;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;

namespace Iw4Radiant.Rendering;

// A deliberately narrow viewport renderer for source FX material billboards.
// Native assets with motion, atlases, or specialized elements are reported as unsupported.
internal sealed class FxSpritePreview
{
    private const int LoopMilliseconds = 8_000;
    private const int MaximumSprites = 128;
    private readonly FxEffectDefAsset _effect;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(int Index, string Material)> _elements = [];

    private FxSpritePreview(FxEffectDefAsset effect, Vector3 origin)
    {
        if (effect.ElemDefCountLooping < 0 || effect.ElemDefCountOneShot < 0 ||
            effect.ElemDefCountEmission < 0 || effect.ElemDefCount != effect.ElemDefs.Count)
            throw new InvalidDataException("FX element counts are invalid.");
        _effect = effect;
        Origin = origin;
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

    internal static FxSpritePreview Load(string sourceDirectory, string assetName, Vector3 origin) =>
        new(new FxExchange().Link(sourceDirectory, assetName), origin);

    internal IReadOnlyList<(string Material, SceneVertex[] Vertices)> Sample(Vector3 eye) =>
        Sample(eye, Origin, MaximumSprites);

    internal IReadOnlyList<(string Material, SceneVertex[] Vertices)> Sample(Vector3 eye, Vector3 origin,
        int maximumSprites)
    {
        maximumSprites = Math.Clamp(maximumSprites, 0, MaximumSprites);
        float time = (float)(_clock.Elapsed.TotalMilliseconds % LoopMilliseconds);
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
                FxElemVisStateSample visual = SampleVisual(element, normalizedLife, out float fraction);
                FxElemVisStateSample next = element.VisSamples.Count > 1
                    ? element.VisSamples[Math.Min(element.VisSamples.Count - 1,
                        Math.Min(element.VisStateIntervalCount - 1, (int)(normalizedLife * element.VisStateIntervalCount)) + 1)]
                    : visual;
                float size = visual.Base.Size0 + (next.Base.Size0 - visual.Base.Size0) * fraction;
                if (!float.IsFinite(size) || size <= 0) continue;
                Vector4 color = Vector4.Lerp(Color(visual.Base.Color), Color(next.Base.Color), fraction);
                if (color.W <= 0) continue;
                Vector3 forward = eye - origin;
                if (forward.LengthSquared() < 0.0001f) forward = Vector3.UnitY;
                forward = Vector3.Normalize(forward);
                Vector3 right = Vector3.Cross(Vector3.UnitZ, forward);
                if (right.LengthSquared() < 0.0001f) right = Vector3.UnitX;
                right = Vector3.Normalize(right);
                Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
                right *= size * 0.5f;
                up *= size * 0.5f;
                Vector3 a = origin - right - up, b = origin + right - up;
                Vector3 c = origin + right + up, d = origin - right + up;
                if (!batches.TryGetValue(material, out List<SceneVertex>? vertices))
                    batches.Add(material, vertices = []);
                vertices.Add(new SceneVertex(a, forward, new Vector2(0, 1), color));
                vertices.Add(new SceneVertex(b, forward, new Vector2(1, 1), color));
                vertices.Add(new SceneVertex(c, forward, new Vector2(1, 0), color));
                vertices.Add(new SceneVertex(a, forward, new Vector2(0, 1), color));
                vertices.Add(new SceneVertex(c, forward, new Vector2(1, 0), color));
                vertices.Add(new SceneVertex(d, forward, new Vector2(0, 0), color));
                spriteCount++;
            }
        }
        return batches.Select(batch => (batch.Key, batch.Value.ToArray())).ToArray();
    }

    private static FxElemVisStateSample SampleVisual(FxElemDef element, float normalizedLife, out float fraction)
    {
        if (element.VisStateIntervalCount == 0)
        {
            fraction = 0;
            return element.VisSamples[0];
        }
        float sample = normalizedLife * element.VisStateIntervalCount;
        int index = Math.Min(element.VisStateIntervalCount - 1, (int)sample);
        fraction = sample - index;
        return element.VisSamples[index];
    }

    private static Vector4 Color(FxElemColor color)
    {
        return new Vector4(
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    private static bool HasMotion(FxElemVelStateInFrame frame) =>
        HasVector(frame.Velocity.Base) || HasVector(frame.Velocity.Amplitude) ||
        HasVector(frame.TotalDelta.Base) || HasVector(frame.TotalDelta.Amplitude);

    private static bool HasVector(Vec3 vector) =>
        vector.X != 0 || vector.Y != 0 || vector.Z != 0;
}
