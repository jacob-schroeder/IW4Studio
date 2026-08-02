using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// One exact imported ClipMaterial row offered as an authored-collision
/// source input. Selecting a row is explicit; names never imply contents or
/// surface flags.
/// </summary>
public sealed record CollisionAuthoringMaterialOption(
    int ImportedOrdinal,
    AuthoredCollisionMaterialInput Material)
{
    public string Label =>
        $"#{ImportedOrdinal} · {Material.ExactName} · " +
        $"contents 0x{unchecked((uint)Material.Contents):X8}";
}

/// <summary>
/// Immutable authoring palette projected from the imported ColMap material
/// catalog. It exposes value-only semantic inputs and never leaks or mutates
/// the detached compilation baseline.
/// </summary>
public sealed class CollisionAuthoringMaterialCatalog
{
    private readonly IReadOnlyList<CollisionAuthoringMaterialOption>
        _options;

    private CollisionAuthoringMaterialCatalog(
        IEnumerable<CollisionAuthoringMaterialOption> options)
    {
        _options =
            new ReadOnlyCollection<CollisionAuthoringMaterialOption>(
                options.ToArray());
    }

    public IReadOnlyList<CollisionAuthoringMaterialOption> Options =>
        _options;

    public static CollisionAuthoringMaterialCatalog Create(
        CompiledMapBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!bundle.TryGetBaseline(
                MapAssetKind.ColMapMp,
                out ClipMapBuildData? clip) ||
            clip is null)
        {
            return new CollisionAuthoringMaterialCatalog([]);
        }

        return new CollisionAuthoringMaterialCatalog(
            clip.Definition.Materials
                .Select((material, ordinal) => CreateOption(
                    material,
                    ordinal))
                .Where(value => value is not null)
                .Select(value => value!));
    }

    private static CollisionAuthoringMaterialOption? CreateOption(
        ClipMaterial material,
        int ordinal)
    {
        string? name = material.Name;
        return string.IsNullOrWhiteSpace(name) ||
               name.IndexOf('\0') >= 0
            ? null
            : new CollisionAuthoringMaterialOption(
                ordinal,
                new AuthoredCollisionMaterialInput(
                    name,
                    material.SurfaceFlags,
                    material.Contents));
    }
}
