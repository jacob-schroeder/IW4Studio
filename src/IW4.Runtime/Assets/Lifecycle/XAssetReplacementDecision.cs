namespace IW4.Runtime.Assets.Lifecycle;

public enum XAssetReplacementDecision
{
    /// <summary>
    /// The type-specific callback permits the generic source-root copy.
    /// </summary>
    CopySource,

    /// <summary>
    /// Preserve the destination runtime projection, but copy source name
    /// identity when GfxImage side records are equal.
    /// </summary>
    KeepDestinationWithSourceName,

    /// <summary>
    /// Required runtime state is unavailable. Callers must not commit the
    /// transaction with an assumed copy outcome.
    /// </summary>
    Unresolved
}
