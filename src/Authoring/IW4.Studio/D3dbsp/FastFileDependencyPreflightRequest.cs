namespace IW4.Studio.D3dbsp;

public sealed record FastFileDependencyPreflightRequest
{
    public required string AssetLibraryDirectory { get; init; }
    public required string BootstrapDirectory { get; init; }
    public IReadOnlyCollection<string> MaterialNames { get; init; } = [];
    public IReadOnlyCollection<string> ModelNames { get; init; } = [];
    public IReadOnlyCollection<string> PhysPresetNames { get; init; } = [];
    public IReadOnlyCollection<string> FxNames { get; init; } = [];
    public IReadOnlyCollection<string> SoundNames { get; init; } = [];
    public IReadOnlyCollection<string> XAnimNames { get; init; } = [];
    public IReadOnlyDictionary<string, string> RawFilePaths { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> SoundDefinitionPaths { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> WorldProperties { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> AuthoredWaterNames { get; init; } = [];
    public IReadOnlyCollection<string> TurretWeaponNames { get; init; } = [];
    public string? CharacterAssetsDirectory { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
