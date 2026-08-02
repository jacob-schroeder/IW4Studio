namespace IW4.Studio.Documents;

/// <summary>
/// One successfully opened fastfile document. Construction means the file is
/// a valid asset/document workspace;
/// </summary>
public sealed record FastFileDocument
{
    internal FastFileDocument(
        FastFileDocumentOpenRequest request,
        WorkspaceZone targetZone,
        TargetZoneSourceSnapshot targetSource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targetZone);
        ArgumentNullException.ThrowIfNull(targetSource);
        if (!targetZone.IsTarget)
        {
            throw new ArgumentException(
                "A fastfile document target must be marked as the target zone.",
                nameof(targetZone));
        }
        if (!string.Equals(
                targetZone.LogicalZoneName,
                targetSource.LogicalZoneName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The target source snapshot does not belong to the target zone.",
                nameof(targetSource));
        }

        Request = request;
        TargetZone = targetZone;
        TargetSource = targetSource;
    }

    public FastFileDocumentOpenRequest Request { get; }

    public WorkspaceZone TargetZone { get; }

    public Guid DocumentId => TargetSource.DocumentId;

    public TargetZoneSourceSnapshot TargetSource { get; }

}
