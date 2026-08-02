namespace IW4.Gsc.Syntax;

public interface IGscSyntaxAnalyzer
{
    GscSyntaxResult Analyze(
        GscSourceText source,
        CancellationToken cancellationToken = default);
}
