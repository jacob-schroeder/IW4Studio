using IW4.AssetExchange.SourceFormat.Technique;
using IW4.Assets.Assets.TechniqueSet;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class TechniqueExchangeStateMapExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unlink_when_state_map_identity_is_unavailable_preserves_output(
        bool existingOutput)
    {
        string sourceDirectory = Directory.CreateTempSubdirectory(
            "IW4.Studio.Tests.TechniqueExchange.").FullName;
        try
        {
            string outputPath = Path.Combine(
                sourceDirectory,
                "techniques",
                "m07",
                "context.tech");
            const string previousSource = "existing technique source\n";
            if (existingOutput)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, previousSource);
            }

            var technique = new MaterialTechniqueAsset
            {
                Name = "m07/context",
                PassCount = 1,
                Passes = [new MaterialPassAsset()]
            };

            InvalidDataException failure = Assert.Throws<InvalidDataException>(
                () => new TechniqueExchange([]).Unlink(
                    sourceDirectory,
                    technique));

            Assert.Equal(
                "Technique 'm07/context' pass 0 cannot be exported: " +
                "PS3 source state-map identity cannot be recovered from a standalone technique.",
                failure.Message);
            if (existingOutput)
                Assert.Equal(previousSource, File.ReadAllText(outputPath));
            else
                Assert.False(File.Exists(outputPath));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(
                    sourceDirectory,
                    "*",
                    SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(
                    ".context.tech.",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }
}
