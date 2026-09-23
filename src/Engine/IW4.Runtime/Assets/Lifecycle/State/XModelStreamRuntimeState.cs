namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed class XModelStreamRuntimeState : IXModelStreamRuntimeState
{
    private Dictionary<XAssetRuntimeAllocationKey, XModelStreamRuntimeRecord> _records = [];

    public bool TryGet(
        XAssetRuntimeAllocationKey allocation,
        out XModelStreamRuntimeRecord record) =>
        _records.TryGetValue(allocation, out record);

    public void Set(
        XAssetRuntimeAllocationKey allocation,
        XModelStreamRuntimeRecord record) =>
        _records[allocation] = record;

    public IXAssetRuntimeStateSnapshot CaptureSnapshot() =>
        new XModelStreamRuntimeSnapshot(new Dictionary<XAssetRuntimeAllocationKey, XModelStreamRuntimeRecord>(_records));

    public void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot)
    {
        if (snapshot is not XModelStreamRuntimeSnapshot typed)
            throw new ArgumentException("Snapshot does not belong to XModel stream runtime state.", nameof(snapshot));

        _records = new Dictionary<XAssetRuntimeAllocationKey, XModelStreamRuntimeRecord>(typed.Records);
    }
}
