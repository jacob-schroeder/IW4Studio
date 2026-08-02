namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Exact PS3 predicate outcome that selects the Z component of sun-shadow
/// direct row 0x1E. The semantic owner of the tested native vector remains
/// open; this value records its operational zero/nonzero result without
/// assigning a speculative field name.
/// </summary>
public enum MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
{
    TestedVectorNonZero,
    TestedVectorZero
}
