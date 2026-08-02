namespace IW4.Runtime.Diagnostics;

public sealed class LoadDiagnostics
{
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public void Warn(string message)
    {
        _warnings.Add(message);
    }
}
