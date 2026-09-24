using System.Text.Json;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Math;
using IW4.Game.ScriptStrings;

namespace IW4.Formats.SourceFormat.XModel;

/// <summary>One linkable XModel and its owned, separately named XModelSurfs providers.</summary>
public sealed record XModelNativeImport(
    XModelAsset Model,
    IReadOnlyList<XModelSurfsAsset> ModelSurfs);

/// <summary>
/// Exchanges the retained PS3 XModel graph without projecting it through
/// XMODEL_EXPORT. Source files contain semantic values; runtime addresses and
/// fastfile pointer encodings are deliberately rebuilt by the linker.
/// </summary>
public sealed class XModelNativeExchange
{
    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        XModelAsset model,
        Func<string, XModelSurfsAsset>? resolveModelSurfs = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        string name = SourceOutput.NormalizeOwnedAssetName(model.Name, "XModel");
        XModelSurfsAsset?[] modelSurfs = model.Lods.Select((lod, index) =>
            ResolveSurfs(lod, index, name, resolveModelSurfs)).ToArray();
        NativeModel source = ToSource(model, name, modelSurfs);
        var files = new List<(string RelativePath, Action<TextWriter> Write)>();
        string modelJson = JsonSerializer.Serialize(source, XModelNativeSurfs.JsonOptions);
        files.Add(($"xmodel_native/{name}.json", writer => writer.WriteLine(modelJson)));
        var writtenSurfs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XModelSurfsAsset? surfs in modelSurfs)
        {
            if (surfs is null)
                continue;
            string surfsName = SourceOutput.NormalizeOwnedAssetName(surfs.Name, "XModelSurfs");
            string json = XModelNativeSurfs.Serialize(surfs);
            if (writtenSurfs.TryGetValue(surfsName, out string? prior))
            {
                if (!string.Equals(prior, json, StringComparison.Ordinal))
                    throw new InvalidDataException($"XModelSurfs '{surfsName}' has conflicting native bodies.");
                continue;
            }
            writtenSurfs.Add(surfsName, json);
            files.Add((XModelNativeSurfs.PathFor(surfsName), writer => writer.WriteLine(json)));
        }
        return new SourceOutput(sourceDirectory).WriteTextBatch(files);
    }

    private static XModelSurfsAsset? ResolveSurfs(
        XModelLodInfo lod,
        int index,
        string modelName,
        Func<string, XModelSurfsAsset>? resolveModelSurfs)
    {
        if (lod.ModelSurfs is not { } retained)
        {
            if (lod.ModelSurfsPointer.Value != 0)
                throw new InvalidDataException($"XModel '{modelName}' LOD {index} has an unnamed XModelSurfs pointer.");
            return null;
        }
        ushort count = lod.SerializedNumSurfs ?? lod.NumSurfs;
        if (retained.NumSurfs == count && retained.Surfaces.Count == count &&
            retained.PartBits.Count == 6)
            return retained;
        string name = SourceOutput.NormalizeReferencedAssetName(
            retained.Name, $"XModel '{modelName}' LOD {index} XModelSurfs");
        XModelSurfsAsset full = resolveModelSurfs?.Invoke(name) ??
            throw new InvalidDataException($"XModel '{modelName}' LOD {index} needs full XModelSurfs '{name}'.");
        if (full.Name != name || full.NumSurfs != count ||
            full.Surfaces.Count != count || full.PartBits.Count != 6)
            throw new InvalidDataException($"XModel '{modelName}' LOD {index} resolved XModelSurfs '{name}' is incomplete.");
        return full;
    }

    public XModelNativeImport Link(
        string sourceDirectory,
        string modelName,
        Func<string, MaterialAsset> loadMaterial,
        Func<string, PhysPresetAsset> loadPhysPreset,
        Func<string, PhysCollmapAsset> loadPhysCollmap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(loadMaterial);
        ArgumentNullException.ThrowIfNull(loadPhysPreset);
        ArgumentNullException.ThrowIfNull(loadPhysCollmap);
        string name = SourceOutput.NormalizeOwnedAssetName(modelName, "XModel");
        string path = Path.Combine(Path.GetFullPath(sourceDirectory), $"xmodel_native/{name}.json");
        NativeModel source = JsonSerializer.Deserialize<NativeModel>(
            File.ReadAllText(path), XModelNativeSurfs.JsonOptions) ??
            throw new InvalidDataException($"XModel '{name}' has no native source body.");
        Validate(source, name);

        var surfsByName = new Dictionary<string, XModelSurfsAsset>(StringComparer.Ordinal);
        XModelLodInfo[] lods = source.Lods.Select((lod, index) =>
        {
            XModelSurfsAsset? surfs = null;
            if (lod.ModelSurfs is { } reference)
            {
                string surfsName = SourceOutput.NormalizeOwnedAssetName(
                    reference, $"XModel '{name}' LOD {index} XModelSurfs");
                if (!surfsByName.TryGetValue(surfsName, out surfs))
                {
                    surfs = XModelNativeSurfs.Read(sourceDirectory, surfsName);
                    surfsByName.Add(surfsName, surfs);
                }
            }
            return new XModelLodInfo
            {
                Dist = lod.Dist,
                NumSurfs = lod.NumSurfs,
                SurfIndex = lod.SurfIndex,
                PartBits = lod.PartBits,
                ModelSurfs = surfs
            };
        }).ToArray();
        MaterialAsset?[] materials = source.Materials.Select((reference, index) =>
            reference is null ? null : loadMaterial(
                SourceOutput.NormalizeReferencedAssetName(
                    reference, $"XModel.Materials[{index}]")) ??
                throw new InvalidDataException($"XModel.Materials[{index}] could not be resolved.")).ToArray();
        PhysPresetAsset? preset = source.PhysPreset is null ? null :
            loadPhysPreset(SourceOutput.NormalizeReferencedAssetName(
                source.PhysPreset, "XModel.PhysPreset")) ??
            throw new InvalidDataException("XModel.PhysPreset could not be resolved.");
        PhysCollmapAsset? collmap = source.PhysCollmap is null ? null :
            loadPhysCollmap(SourceOutput.NormalizeReferencedAssetName(
                source.PhysCollmap, "XModel.PhysCollmap")) ??
            throw new InvalidDataException("XModel.PhysCollmap could not be resolved.");

        var model = new XModelAsset
        {
            Name = name,
            NumBones = source.NumBones,
            NumRootBones = source.NumRootBones,
            NumSurfs = source.NumSurfs,
            LodRampType = source.LodRampType,
            Scale = source.Scale,
            NoScalePartBits = source.NoScalePartBits,
            BoneNames = source.BoneNames.Select(text =>
                new ScriptStringReference(0, text, ScriptStringHandle.Null, default)).ToArray(),
            ParentList = source.ParentList,
            Quats = source.Quats,
            Trans = source.Trans,
            PartClassification = source.PartClassification,
            BaseMat = source.BaseMat,
            Materials = materials,
            Lods = lods,
            MaxLoadedLod = source.MaxLoadedLod,
            NumLods = source.NumLods,
            CollLod = source.CollLod,
            Flags = source.Flags,
            NumCollSurfs = source.CollSurfs.Length,
            Contents = source.Contents,
            CollSurfs = source.CollSurfs,
            BoneInfo = source.BoneInfo,
            Radius = source.Radius,
            Bounds = source.Bounds,
            InvHighMipRadius = source.InvHighMipRadius,
            MemUsage = source.MemUsage,
            PhysPreset = preset,
            PhysCollmap = collmap
        };
        return new XModelNativeImport(model, surfsByName.Values.ToArray());
    }

    private static NativeModel ToSource(
        XModelAsset model,
        string name,
        IReadOnlyList<XModelSurfsAsset?> modelSurfs)
    {
        byte numSurfs = model.SerializedNumSurfs ?? model.NumSurfs;
        if (model.Lods.Count != 4 || model.NoScalePartBits.Count != 6 ||
            model.NumRootBones > model.NumBones ||
            model.BoneNames.Count != model.NumBones ||
            model.ParentList.Count != model.NumBones - model.NumRootBones ||
            model.Quats.Count != (model.NumBones - model.NumRootBones) * 4 ||
            model.Trans.Count != (model.NumBones - model.NumRootBones) * 3 ||
            model.PartClassification.Count != model.NumBones ||
            model.BaseMat.Count != model.NumBones ||
            model.Materials.Count != numSurfs ||
            model.BoneInfo.Count != model.NumBones ||
            model.InvHighMipRadius.Count != numSurfs ||
            model.CollSurfs.Count != model.NumCollSurfs)
            throw new InvalidDataException($"XModel '{name}' has incomplete native model arrays.");
        string?[] boneNames = model.BoneNames.Select((reference, index) =>
        {
            if (reference is null || (reference.Text is null && reference.RawLocalIndex != 0))
                throw new InvalidDataException($"XModel '{name}' bone {index} has no resolved script-string text.");
            return reference.Text;
        }).ToArray();
        NativeLod[] lods = model.Lods.Select((lod, index) =>
        {
            IReadOnlyList<uint> bits = lod.SerializedPartBits ?? lod.PartBits;
            if (bits.Count != 6)
                throw new InvalidDataException($"XModel '{name}' LOD {index} has no native part bits.");
            ushort count = lod.SerializedNumSurfs ?? lod.NumSurfs;
            if (modelSurfs[index] is { } surfs && count != surfs.Surfaces.Count)
                throw new InvalidDataException($"XModel '{name}' LOD {index} surface count disagrees with its provider.");
            return new NativeLod(
                lod.Dist, count, lod.SerializedSurfIndex ?? lod.SurfIndex,
                bits.ToArray(), modelSurfs[index] is null ? null :
                    SourceOutput.NormalizeReferencedAssetName(
                        modelSurfs[index]!.Name, $"XModel '{name}' LOD {index} XModelSurfs"));
        }).ToArray();
        string?[] materials = model.Materials.Select((material, index) =>
        {
            if (material is null && model.MaterialPointers.Count > index &&
                model.MaterialPointers[index].Value != 0)
                throw new InvalidDataException($"XModel '{name}' material {index} has an unresolved pointer.");
            return material is null ? null :
                SourceOutput.NormalizeReferencedAssetName(
                    material.Info.Name, $"XModel '{name}' material {index}");
        }).ToArray();
        if (model.PhysPreset is null && model.PhysPresetPointer.Value != 0)
            throw new InvalidDataException($"XModel '{name}' has an unresolved PhysPreset pointer.");
        if (model.PhysCollmap is null && model.PhysCollmapPointer.Value != 0)
            throw new InvalidDataException($"XModel '{name}' has an unresolved PhysCollmap pointer.");
        return new NativeModel(
            1, name, model.NumBones, model.NumRootBones, numSurfs,
            model.LodRampType, model.Scale, model.NoScalePartBits.ToArray(),
            boneNames, model.ParentList.ToArray(), model.Quats.ToArray(),
            model.Trans.ToArray(), model.PartClassification.ToArray(),
            model.BaseMat.ToArray(), materials, lods, model.MaxLoadedLod,
            model.NumLods, model.CollLod, model.Flags, model.Contents,
            model.CollSurfs.ToArray(), model.BoneInfo.ToArray(), model.Radius,
            model.Bounds, model.InvHighMipRadius.ToArray(), model.MemUsage,
            OptionalName(model.PhysPreset?.Name, "XModel.PhysPreset"),
            OptionalName(model.PhysCollmap?.Name, "XModel.PhysCollmap"));
    }

    private static string? OptionalName(string? name, string field) =>
        name is null ? null : SourceOutput.NormalizeReferencedAssetName(name, field);

    private static void Validate(NativeModel source, string name)
    {
        int partCount = source.NumBones - source.NumRootBones;
        if (source.Version != 1 || source.Name != name || partCount < 0 ||
            source.NoScalePartBits?.Length != 6 ||
            source.BoneNames?.Length != source.NumBones ||
            source.ParentList?.Length != partCount ||
            source.Quats?.Length != partCount * 4 ||
            source.Trans?.Length != partCount * 3 ||
            source.PartClassification?.Length != source.NumBones ||
            source.BaseMat?.Length != source.NumBones ||
            source.Materials?.Length != source.NumSurfs ||
            source.Lods?.Length != 4 ||
            source.CollSurfs is null || source.BoneInfo?.Length != source.NumBones ||
            source.InvHighMipRadius?.Length != source.NumSurfs ||
            source.Bounds is null ||
            source.Lods.Any(lod => lod is null || lod.PartBits?.Length != 6))
            throw new InvalidDataException($"XModel '{name}' has an invalid native source body.");
    }

    private sealed record NativeLod(
        float Dist, ushort NumSurfs, ushort SurfIndex, uint[] PartBits,
        string? ModelSurfs);

    private sealed record NativeModel(
        int Version, string Name, byte NumBones, byte NumRootBones,
        byte NumSurfs, XModelLodRampType LodRampType, float Scale,
        uint[] NoScalePartBits, string?[] BoneNames, byte[] ParentList,
        short[] Quats, float[] Trans, byte[] PartClassification,
        DObjAnimMat[] BaseMat, string?[] Materials, NativeLod[] Lods,
        byte MaxLoadedLod, byte NumLods, byte CollLod, XModelFlags Flags,
        int Contents, XModelCollSurf[] CollSurfs, XBoneInfo[] BoneInfo,
        float Radius, Bounds Bounds, ushort[] InvHighMipRadius,
        int MemUsage, string? PhysPreset, string? PhysCollmap);
}
