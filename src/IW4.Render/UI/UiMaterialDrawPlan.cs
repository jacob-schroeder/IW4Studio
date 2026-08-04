namespace IW4.Render.UI;

public sealed class UiMaterialDrawPlan
{
    private readonly UiMaterialExecutionDiagnostic[] _diagnostics;

    internal UiMaterialDrawPlan(
        UiMaterialDrawPacket? packet,
        IReadOnlyList<UiMaterialExecutionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Packet = packet;
        _diagnostics = diagnostics.ToArray();
        if (_diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A UI material plan cannot contain null diagnostics.",
                nameof(diagnostics));
        }
        bool blocked = _diagnostics.Any(diagnostic =>
            diagnostic.Severity ==
            UiMaterialExecutionDiagnosticSeverity.Blocker);
        if ((packet is null) != blocked)
        {
            throw new ArgumentException(
                "An executable UI material plan cannot contain blockers, " +
                "and a blocked plan must contain at least one blocker.",
                nameof(diagnostics));
        }
        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    public UiMaterialDrawPacket? Packet { get; }

    public IReadOnlyList<UiMaterialExecutionDiagnostic> Diagnostics { get; }

    public bool IsExecutable => Packet is not null;
}
