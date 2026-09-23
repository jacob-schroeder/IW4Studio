using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal sealed class FoliagePaletteModel
{
    private decimal? _weight = 1;
    private bool _placementInitialized;

    internal FoliagePaletteModel(XModelSource model) : this(model.Name, model, false) { }
    internal FoliagePaletteModel(string prefabPath) : this(Path.GetFullPath(prefabPath), null, true) { }

    private FoliagePaletteModel(string name, XModelSource? model, bool isPrefab)
    {
        Name = name;
        Model = model;
        IsPrefab = isPrefab;
    }

    internal XModelSource? Model { get; private set; }
    internal string? PrefabReference { get; private set; }
    internal string? AvailabilityError { get; private set; }
    internal bool IsAvailable => IsPrefab ? PrefabReference is not null : Model is not null;
    public bool IsPrefab { get; }
    public string Name { get; }
    public string DisplayName => (IsAvailable ? "" : "Unavailable · ") +
        (IsPrefab ? Path.GetFileNameWithoutExtension(Name) : Name);
    public string AssetType => IsPrefab ? "PREFAB" : "MODEL";
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
        if (IsPrefab) return;
        if (ReferenceEquals(Model, model)) return;
        Model = model;
        Changed?.Invoke();
    }

    internal void ResolvePrefab(string? mapPath, PrefabLibrary library)
    {
        if (!IsPrefab) return;
        string? reference = null, error = null;
        try
        {
            if (mapPath is null) throw new ArgumentException("Save this map before painting prefabs.");
            reference = library.ValidatePaintSource(mapPath, Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or
                                           ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            error = exception.Message;
            reference = null;
        }
        if (PrefabReference == reference && AvailabilityError == error) return;
        PrefabReference = reference;
        AvailabilityError = error;
        Changed?.Invoke();
    }

    internal FoliagePresetModel ToPreset() => new()
    {
        Name = Name,
        IsPrefab = IsPrefab,
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
        var item = new FoliagePaletteModel(preset.Name, preset.IsPrefab ? null : model, preset.IsPrefab)
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
