namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Managed projection of XModel-indexed runtime cells used during release,
/// replacement, and pool retirement.
/// </summary>
public readonly record struct XModelStreamRuntimeRecord(
    uint Word0,
    uint Word1,
    uint Word2,
    uint AuxiliaryWord,
    bool StreamMarked);
