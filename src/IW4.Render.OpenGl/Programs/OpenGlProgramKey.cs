using System.Security.Cryptography;
using System.Text;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Content identity for one exact generated GLSL pair under one explicit link
/// profile. SHA-256 digests are only lookup prefilters: equality always compares
/// the retained immutable source strings and exact profile using ordinal string
/// equality, so a digest collision cannot return another GL program resource.
/// </summary>
public readonly struct OpenGlProgramKey : IEquatable<OpenGlProgramKey>
{
    private readonly string? _vertexGlsl;
    private readonly string? _pixelGlsl;

    private OpenGlProgramKey(
        string vertexGlsl,
        string vertexGlslSha256,
        string pixelGlsl,
        string pixelGlslSha256,
        string linkProfileIdentity,
        string linkProfileSha256)
    {
        _vertexGlsl = vertexGlsl;
        VertexGlslSha256 = vertexGlslSha256;
        _pixelGlsl = pixelGlsl;
        PixelGlslSha256 = pixelGlslSha256;
        LinkProfileIdentity = linkProfileIdentity;
        LinkProfileSha256 = linkProfileSha256;
    }

    public string VertexGlslSha256 { get; }

    public string PixelGlslSha256 { get; }

    public string LinkProfileIdentity { get; }

    public string LinkProfileSha256 { get; }

    public static OpenGlProgramKey Create(
        string vertexGlsl,
        string pixelGlsl,
        string linkProfileIdentity)
    {
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkProfileIdentity);

        return new OpenGlProgramKey(
            vertexGlsl,
            HashExactText(vertexGlsl),
            pixelGlsl,
            HashExactText(pixelGlsl),
            linkProfileIdentity,
            HashExactText(linkProfileIdentity));
    }

    public bool MatchesSources(
        string vertexGlsl,
        string pixelGlsl,
        string linkProfileIdentity)
    {
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        ArgumentNullException.ThrowIfNull(linkProfileIdentity);

        // Cache callers retain and pass the same immutable generated-source
        // strings that created the key. Preserve the exact-comparison
        // contract while avoiding three redundant UTF-8 encodes and SHA-256
        // passes on every binary-cache load/store request.
        if (IsValid &&
            ReferenceEquals(_vertexGlsl, vertexGlsl) &&
            ReferenceEquals(_pixelGlsl, pixelGlsl) &&
            ReferenceEquals(LinkProfileIdentity, linkProfileIdentity))
        {
            return true;
        }

        return IsValid &&
               ExactTextEquals(
                   LinkProfileSha256,
                   LinkProfileIdentity,
                   HashExactText(linkProfileIdentity),
                   linkProfileIdentity) &&
               ExactTextEquals(
                   VertexGlslSha256,
                   _vertexGlsl,
                   HashExactText(vertexGlsl),
                   vertexGlsl) &&
               ExactTextEquals(
                   PixelGlslSha256,
                   _pixelGlsl,
                   HashExactText(pixelGlsl),
                   pixelGlsl);
    }

    internal bool MatchesSourcesForCompilerProfile(
        string vertexGlsl,
        string pixelGlsl,
        string compilerLinkProfileIdentity)
    {
        ArgumentNullException.ThrowIfNull(compilerLinkProfileIdentity);
        if (!IsValid)
            return false;
        string qualifiedLinkProfileIdentity = LinkProfileIdentity;
        bool compatibleProfile = string.Equals(
                                     qualifiedLinkProfileIdentity,
                                     compilerLinkProfileIdentity,
                                     StringComparison.Ordinal) ||
                                 qualifiedLinkProfileIdentity.StartsWith(
                                      compilerLinkProfileIdentity + "|",
                                      StringComparison.Ordinal);
        return compatibleProfile && MatchesSources(
            vertexGlsl,
            pixelGlsl,
            qualifiedLinkProfileIdentity);
    }

    public bool Equals(OpenGlProgramKey other) =>
        ExactTextEquals(
            VertexGlslSha256,
            _vertexGlsl,
            other.VertexGlslSha256,
            other._vertexGlsl) &&
        ExactTextEquals(
            PixelGlslSha256,
            _pixelGlsl,
            other.PixelGlslSha256,
            other._pixelGlsl) &&
        ExactTextEquals(
            LinkProfileSha256,
            LinkProfileIdentity,
            other.LinkProfileSha256,
            other.LinkProfileIdentity);

    public override bool Equals(object? obj) =>
        obj is OpenGlProgramKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(VertexGlslSha256 ?? string.Empty),
        StringComparer.Ordinal.GetHashCode(PixelGlslSha256 ?? string.Empty),
        StringComparer.Ordinal.GetHashCode(LinkProfileIdentity ?? string.Empty),
        StringComparer.Ordinal.GetHashCode(LinkProfileSha256 ?? string.Empty));

    public override string ToString() =>
        IsValid
            ? $"{LinkProfileSha256}:{VertexGlslSha256}:{PixelGlslSha256}"
            : "INVALID_OPENGL_PROGRAM_KEY";

    public static bool operator ==(
        OpenGlProgramKey left,
        OpenGlProgramKey right) => left.Equals(right);

    public static bool operator !=(
        OpenGlProgramKey left,
        OpenGlProgramKey right) => !left.Equals(right);

    internal bool IsValid =>
        _vertexGlsl is not null &&
        _pixelGlsl is not null &&
        !string.IsNullOrEmpty(VertexGlslSha256) &&
        !string.IsNullOrEmpty(PixelGlslSha256) &&
        !string.IsNullOrEmpty(LinkProfileIdentity) &&
        !string.IsNullOrEmpty(LinkProfileSha256);

    internal static bool ExactTextEquals(
        string? leftDigest,
        string? leftText,
        string? rightDigest,
        string? rightText) =>
        string.Equals(leftDigest, rightDigest, StringComparison.Ordinal) &&
        string.Equals(leftText, rightText, StringComparison.Ordinal);

    internal static string HashExactText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
