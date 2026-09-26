using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Character;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Linker.Linking;
using IW4.Studio.Documents;

namespace D3dbspLinker.Conversion;

internal static class CharacterImportCommand
{
    internal static int Import(
        string bodyPath,
        string handsPath,
        string bootstrapRoot,
        string outputRoot,
        string prefix)
    {
        if (!RangersAssaultAppearance.IsCustomAssetFolder(prefix))
            throw new ArgumentException("The model prefix must contain 1 to 64 lowercase letters, digits, or underscores.");

        string output = Path.GetFullPath(outputRoot);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException($"Character output '{output}' already exists. Choose a new import directory.");
        string bootstrap = Path.GetFullPath(bootstrapRoot);
        if (!Directory.Exists(bootstrap))
            throw new DirectoryNotFoundException($"Bootstrap source root '{bootstrap}' does not exist.");

        XModelExportDocument body = ReadGlb(bodyPath);
        XModelExportDocument hands = ReadGlb(handsPath);
        var materials = new MaterialSourceCompiler(bootstrap);
        var models = new ModelSourceCompiler(bootstrap, materials);
        XModelAsset bodyTemplate = models.LoadModel("mp_body_us_army_assault_a");
        XModelAsset handsTemplate = models.LoadModel("viewhands_us_army");
        string bodyName = prefix + "_body";
        string handsName = prefix + "_viewhands";
        XModelAssemblyCompileResult compiledBody = XModelCharacterImporter.Compile(
            bodyTemplate, body, bodyName, Path.GetFullPath(bodyPath), viewHands: false);
        XModelAssemblyCompileResult compiledHands = XModelCharacterImporter.Compile(
            handsTemplate, hands, handsName, Path.GetFullPath(handsPath), viewHands: true);

        try
        {
            Directory.CreateDirectory(output);
            WriteModel(output, compiledBody);
            WriteModel(output, compiledHands);
        }
        catch
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            throw;
        }
        Console.WriteLine($"body={bodyName}");
        Console.WriteLine($"viewhands={handsName}");
        return 0;
    }

    private static XModelExportDocument ReadGlb(string path)
    {
        string source = Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(source);
        if (!XModelGlbReader.TryRead(stream, imageDecoder: null,
                out XModelExportDocument? document, out IReadOnlyList<string> blockers))
            throw new InvalidDataException($"Character GLB '{source}' cannot be imported: {string.Join(" ", blockers)}");
        return document!;
    }

    private static void WriteModel(string output, XModelAssemblyCompileResult compiled)
    {
        new XModelNativeExchange().Unlink(output, compiled.Definition);
        foreach (MaterialAsset material in compiled.Providers.OfType<MaterialAsset>())
            new MaterialExchange().Unlink(output, material);
        foreach (GfxImageAsset image in compiled.Providers.OfType<GfxImageAsset>())
            new ImageExchange().UnlinkNative(output, image);
    }
}
