namespace IW4.Game.Pointers;

/// <summary>
/// Declares whether a serialized pointer field permits a null object target.
/// Pointer source form (reference, inline, or insert) is a separate contract.
/// </summary>
public enum XPointerNullability
{
    Unspecified = 0,
    Nullable,
    Required
}
