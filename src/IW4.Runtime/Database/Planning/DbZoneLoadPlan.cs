namespace IW4.Runtime.Database.Planning;

public sealed class DbZoneLoadPlan
{
    public DbZoneLoadPlan(
        string targetZoneName,
        DbZonePlanScope scope,
        IReadOnlyList<DbLoadXAssetsBatch> batches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        ArgumentNullException.ThrowIfNull(batches);

        TargetZoneName = targetZoneName;
        Scope = scope;
        Batches = Array.AsReadOnly(batches.ToArray());
        Requests = Array.AsReadOnly(Batches.SelectMany(batch => batch.Requests).ToArray());
        if (Requests.Count(request => request.IsTarget) != 1)
            throw new InvalidDataException("A dependency plan must contain exactly one target request.");

        RequestsInScope = Scope == DbZonePlanScope.ThroughTarget
            ? Array.AsReadOnly(Requests.TakeWhile(request => !request.IsTarget).Append(Target).ToArray())
            : Requests;
    }

    public string TargetZoneName { get; }

    public DbZonePlanScope Scope { get; }

    public IReadOnlyList<DbLoadXAssetsBatch> Batches { get; }

    public IReadOnlyList<DbZonePlanRequest> Requests { get; }

    public IReadOnlyList<DbZonePlanRequest> RequestsInScope { get; }

    public DbZonePlanRequest Target => Requests.Single(request => request.IsTarget);
}
