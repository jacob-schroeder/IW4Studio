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
