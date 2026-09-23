using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal sealed class FoliagePaletteModel
{
    private decimal? _weight = 1;
    private bool _placementInitialized;

    internal FoliagePaletteModel(XModelSource model) : this(model.Name, model) { }

    private FoliagePaletteModel(string name, XModelSource? model)
    {
        Name = name;
        Model = model;
    }

    internal XModelSource? Model { get; private set; }
    public string Name { get; }
    public string DisplayName => Model is null ? $"Unavailable · {Name}" : Name;
    internal bool UsesCustomPlacement { get; private set; }
    internal float MinimumScale { get; private set; } = 0.8f;
    internal float MaximumScale { get; private set; } = 1.2f;
    internal bool RandomYaw { get; private set; } = true;
    internal float FixedYaw { get; private set; }
    internal bool AlignToSurface { get; private set; } = true;
    internal float SurfaceOffset { get; private set; }
    public decimal? Weight
    {
        get => _weight;
        set
        {
            decimal? weight = value is >= 0 and <= 100 ? value : null;
            if (_weight == weight) return;
            _weight = weight;
            Changed?.Invoke();
        }
    }

    internal void SetCustomPlacement(bool enabled, float brushMinimumScale, float brushMaximumScale,
        bool brushRandomYaw, bool brushAlignToSurface)
    {
        if (enabled && !_placementInitialized)
        {
            MinimumScale = brushMinimumScale;
            MaximumScale = brushMaximumScale;
            RandomYaw = brushRandomYaw;
            AlignToSurface = brushAlignToSurface;
            _placementInitialized = true;
        }
        if (UsesCustomPlacement == enabled) return;
        UsesCustomPlacement = enabled;
        Changed?.Invoke();
    }

    internal void SetPlacement(float minimumScale, float maximumScale, bool randomYaw,
        float fixedYaw, bool alignToSurface, float surfaceOffset)
    {
        if (!UsesCustomPlacement) return;
        MinimumScale = Math.Clamp(minimumScale, 0.01f, 100);
        MaximumScale = Math.Clamp(maximumScale, 0.01f, 100);
        RandomYaw = randomYaw;
        FixedYaw = Math.Clamp(fixedYaw, 0, 360);
        AlignToSurface = alignToSurface;
        SurfaceOffset = Math.Clamp(surfaceOffset, -100, 100);
        Changed?.Invoke();
    }

    internal void ResolveModel(XModelSource? model)
    {
        if (ReferenceEquals(Model, model)) return;
        Model = model;
        Changed?.Invoke();
    }

    internal FoliagePresetModel ToPreset() => new()
    {
        Name = Name,
        Weight = Weight,
        PlacementInitialized = _placementInitialized,
        UsesCustomPlacement = UsesCustomPlacement,
        MinimumScale = MinimumScale,
        MaximumScale = MaximumScale,
        RandomYaw = RandomYaw,
        FixedYaw = FixedYaw,
        AlignToSurface = AlignToSurface,
        SurfaceOffset = SurfaceOffset
    };

    internal static FoliagePaletteModel FromPreset(FoliagePresetModel preset, XModelSource? model)
    {
        var item = new FoliagePaletteModel(preset.Name, model)
        {
            _placementInitialized = preset.PlacementInitialized || preset.UsesCustomPlacement,
            UsesCustomPlacement = preset.UsesCustomPlacement,
            MinimumScale = FiniteClamp(preset.MinimumScale, 0.01f, 100, 0.8f),
            MaximumScale = FiniteClamp(preset.MaximumScale, 0.01f, 100, 1.2f),
            RandomYaw = preset.RandomYaw,
            FixedYaw = FiniteClamp(preset.FixedYaw, 0, 360, 0),
            AlignToSurface = preset.AlignToSurface,
            SurfaceOffset = FiniteClamp(preset.SurfaceOffset, -100, 100, 0)
        };
        item.Weight = preset.Weight;
        return item;
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    internal event Action? Changed;
}
