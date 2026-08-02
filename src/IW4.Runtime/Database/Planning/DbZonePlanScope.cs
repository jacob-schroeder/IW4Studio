namespace IW4.Runtime.Database.Planning;

public enum DbZonePlanScope
{
    /// <summary>Engine-authored requests through completion of the target zone.</summary>
    ThroughTarget,

    /// <summary>Continue engine-authored requests to the next stable runtime state.</summary>
    StableRuntime,

    /// <summary>Load only the selected file without its runtime dependency set.</summary>
    StructuralSingleZone
}
