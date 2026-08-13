namespace IW4.Render.Shaders;

/// <summary>
/// Reconciles the constants actually read by selected vertex microcode with
/// their two valid owners: authored pass arguments and compiler-owned program
/// defaults. Unknown, duplicate, and metadata-only rows remain fail-closed.
/// </summary>
internal static class SelectedProgramVertexConstantOwnership
{
    public static IReadOnlyList<string> FindBlockers(
        IReadOnlyList<int> readDestinations,
        IReadOnlyList<ShaderConstantDestination> passConstants,
        IReadOnlyList<EmbeddedVertexConstant> embeddedConstants)
    {
        ArgumentNullException.ThrowIfNull(readDestinations);
        ArgumentNullException.ThrowIfNull(passConstants);
        ArgumentNullException.ThrowIfNull(embeddedConstants);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        int[] reads = readDestinations.Distinct().Order().ToArray();
        foreach (int destination in reads)
        {
            if (destination is < 0 or >= RsxVertexConstantLayout.Count)
            {
                blockers.Add(
                    $"vertexConstantDest{destination}=" +
                    "INVALID_RSX_VERTEX_CONSTANT_DESTINATION");
                continue;
            }

            int passOwnerCount = passConstants.Count(constant =>
                constant.Destination == destination &&
                constant.ArgumentType.EndsWith(
                    "VertexConst",
                    StringComparison.Ordinal));
            int embeddedOwnerCount = embeddedConstants.Count(constant =>
                constant.Destination == destination);
            int ownerCount = passOwnerCount + embeddedOwnerCount;
            if (ownerCount == 0)
            {
                blockers.Add(
                    $"vertexConstantDest{destination}=" +
                    "SELECTED_PROGRAM_CONSTANT_OWNER_UNRESOLVED");
            }
            else if (ownerCount > 1)
            {
                blockers.Add(
                    $"vertexConstantDest{destination}=" +
                    "AMBIGUOUS_SELECTED_PROGRAM_CONSTANT_OWNER");
            }
        }

        foreach (EmbeddedVertexConstant embedded in
                 embeddedConstants)
        {
            if (!reads.Contains(embedded.Destination))
            {
                blockers.Add(
                    $"vertexEmbeddedConstantDest{embedded.Destination}=" +
                    "PROGRAM_METADATA_NOT_READ_BY_MICROCODE");
            }
            if (embedded.RawResourceIndex != embedded.Destination)
            {
                blockers.Add(
                    $"vertexEmbeddedConstantDest{embedded.Destination}=" +
                    "RESOURCE_INDEX_DESTINATION_MISMATCH");
            }
            if (!embedded.IsOperationallyResolved)
            {
                blockers.Add(
                    $"vertexEmbeddedConstantDest{embedded.Destination}=" +
                    "UNRESOLVED");
            }
        }

        return Array.AsReadOnly(blockers.ToArray());
    }
}
