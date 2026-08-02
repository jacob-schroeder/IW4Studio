using IW4.Runtime.Database.Planning;

namespace IW4.Studio.Documents;

/// <summary>
/// Selects how Studio opens a fastfile document. This is distinct from a
/// dependency plan's execution scope: an isolated open directly loads the
/// selected file and must not be rewritten as StructuralSingleZone.
/// </summary>
public abstract record FastFileOpenMode;

/// <summary>Open only the selected fastfile as a valid document workspace.</summary>
public sealed record Isolated : FastFileOpenMode
{
    private Isolated()
    {
    }

    public static Isolated Instance { get; } = new();
}

/// <summary>Resolve and execute a named Runtime-owned dependency plan.</summary>
public sealed record ZonePlan : FastFileOpenMode
{
    public ZonePlan(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ProfileName = profileName;
    }

    public string ProfileName { get; }
}

public static class FastFileOpenProfiles
{
    public const string DefaultMp = "default_mp";
    public const string DefaultSp = "default_sp";

    /// <summary>
    /// Selects the executable dependency profile for a supported logical zone.
    /// </summary>
    public static string ResolveForTarget(string targetNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNameOrPath);

        if (DefaultMpZoneLoadPlanner.SupportsTarget(targetNameOrPath))
            return DefaultMp;
        if (DefaultSpZoneLoadPlanner.SupportsTarget(targetNameOrPath))
            return DefaultSp;

        string targetName = DbZoneCatalog.NormalizeZoneName(targetNameOrPath);
        throw new NotSupportedException(
            $"Zone '{targetName}' does not match the default multiplayer or " +
            "single-player dependency lifecycle. Open it in isolation.");
    }
}
