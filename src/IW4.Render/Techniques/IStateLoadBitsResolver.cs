using IW4.Assets.Assets.Material;

namespace IW4.Render.Techniques;

/// <summary>
/// Resolves the two PS3 load-bits words owned by one material state row.
/// Implementations may read the row's retained payload or its canonical
/// dependency-owned runtime allocation.
/// </summary>
public interface IStateLoadBitsResolver
{
    IReadOnlyList<uint> ResolveStateLoadBits(GfxStateBits state);
}
