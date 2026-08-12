namespace IW4.Studio.Documents;

/// <summary>Selects the lifecycle used to open a fastfile document.</summary>
public abstract record FastFileOpenMode;

/// <summary>Open exactly the selected fastfile.</summary>
public sealed record Isolated : FastFileOpenMode
{
    private Isolated()
    {
    }

    public static Isolated Instance { get; } = new();
}

/// <summary>Open the target through one default engine dependency lifecycle.</summary>
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
    public static string ResolveForTarget(string targetNameOrPath) =>
        IW4.FastFiles.Loaders.Database.Planning.DbDefaultZoneDependencyLoader
            .ResolveProfile(targetNameOrPath);
}
