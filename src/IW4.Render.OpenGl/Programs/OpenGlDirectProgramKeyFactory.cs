using System.Globalization;
using System.Text;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Builds the exact key used by the renderer's direct RSX link path. The
/// sampler destination snapshot is link metadata because the cached
/// <see cref="GlRsxProgram"/> retains destination-specific uniform locations.
/// </summary>
internal static class OpenGlDirectProgramKeyFactory
{
    private const string ProfileSchema = "direct-rsx-link/v1";

    internal static OpenGlProgramKey Create(
        string vertexGlsl,
        string pixelGlsl,
        string compilerLinkProfileIdentity,
        bool usesVertexSourceVariant,
        ReadOnlySpan<int> samplerDestinations)
    {
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            compilerLinkProfileIdentity);

        return OpenGlProgramKey.Create(
            vertexGlsl,
            pixelGlsl,
            BuildProfileIdentity(
                compilerLinkProfileIdentity,
                usesVertexSourceVariant,
                samplerDestinations));
    }

    internal static string BuildProfileIdentity(
        string compilerLinkProfileIdentity,
        bool usesVertexSourceVariant,
        ReadOnlySpan<int> samplerDestinations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            compilerLinkProfileIdentity);

        // The base profile is length-prefixed so arbitrary profile text cannot
        // collide with the structural delimiters. The exact destination sequence
        // is retained in full, not digested. The direct caller supplies its
        // sorted, distinct metadata snapshot.
        var builder = new StringBuilder(
            compilerLinkProfileIdentity.Length +
            ProfileSchema.Length +
            samplerDestinations.Length * 3 +
            32);
        builder.Append(ProfileSchema);
        builder.Append('|');
        builder.Append(
            compilerLinkProfileIdentity.Length.ToString(
                CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(compilerLinkProfileIdentity);
        builder.Append('|');
        builder.Append(usesVertexSourceVariant ? '1' : '0');
        builder.Append('|');
        builder.Append(
            samplerDestinations.Length.ToString(
                CultureInfo.InvariantCulture));
        foreach (int destination in samplerDestinations)
        {
            builder.Append('|');
            builder.Append(destination.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
