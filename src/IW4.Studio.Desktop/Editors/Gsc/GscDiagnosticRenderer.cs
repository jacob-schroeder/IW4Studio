using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace IW4.Studio.Desktop.Editors.Gsc;

/// <summary>
/// Draws the selected GSC buffer's immutable diagnostic snapshot without
/// participating in parsing or document mutation.
/// </summary>
internal sealed class GscDiagnosticRenderer : IBackgroundRenderer, IDisposable
{
    private const double MinimumSquiggleWidth = 5;
    private const double SquiggleAmplitude = 1.1;
    private const double SquiggleHalfWave = 2;

    private static readonly IPen ErrorPen = new ImmutablePen(
        new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x68)),
        1.2);

    private static readonly IPen WarningPen = new ImmutablePen(
        new ImmutableSolidColorBrush(Color.FromRgb(0xE2, 0xB8, 0x6B)),
        1.2);

    private readonly TextView _textView;
    private readonly TextBlock _toolTipContent = new()
    {
        MaxWidth = 520,
        TextWrapping = TextWrapping.Wrap
    };

    private EditorSourceDiagnostic[] _snapshot = [];
    private TextSegmentCollection<DiagnosticSegment> _segments = new();
    private object? _replacedToolTip;
    private int _snapshotTextLength;
    private bool _ownsToolTip;
    private bool _disposed;

    /// <summary>
    /// Creates a renderer for <paramref name="textView"/>. The caller owns
    /// registration in <see cref="TextView.BackgroundRenderers"/>.
    /// </summary>
    internal GscDiagnosticRenderer(TextView textView)
    {
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
        _textView.PointerHover += TextView_PointerHover;
        _textView.PointerHoverStopped += TextView_PointerHoverStopped;
    }

    public KnownLayer Layer => KnownLayer.Text;

    /// <summary>
    /// Replaces the displayed diagnostics with a defensive immutable copy.
    /// Source spans are clamped to the current document before indexing.
    /// </summary>
    internal void Replace(IReadOnlyList<EditorSourceDiagnostic> diagnostics)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(diagnostics);

        int textLength = _textView.Document?.TextLength ?? 0;
        if (SnapshotsEqual(_snapshot, diagnostics) &&
            (_snapshot.Length == 0 || _snapshotTextLength == textLength))
        {
            return;
        }

        EditorSourceDiagnostic[] snapshot = new EditorSourceDiagnostic[diagnostics.Count];
        TextSegmentCollection<DiagnosticSegment> segments = new();
        for (int index = 0; index < diagnostics.Count; index++)
        {
            EditorSourceDiagnostic diagnostic = diagnostics[index] ??
                throw new ArgumentException(
                    "A diagnostic snapshot cannot contain null entries.",
                    nameof(diagnostics));
            snapshot[index] = diagnostic;

            int start = Math.Min(diagnostic.Location.Start, textLength);
            int length = Math.Min(
                diagnostic.Location.Length,
                textLength - start);
            if (length == 0 && textLength != 0)
            {
                if (start == textLength)
                    start--;
                length = 1;
            }
            segments.Add(new DiagnosticSegment(diagnostic, start, length));
        }

        HideToolTip();
        _snapshot = snapshot;
        _segments = segments;
        _snapshotTextLength = textLength;
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_disposed ||
            !ReferenceEquals(textView, _textView) ||
            _segments.Count == 0 ||
            textView.Document is null ||
            !textView.VisualLinesValid ||
            textView.VisualLines.Count == 0)
        {
            return;
        }

        VisualLine firstLine = textView.VisualLines[0];
        VisualLine lastLine = textView.VisualLines[^1];
        int visibleStart = firstLine.FirstDocumentLine.Offset;
        int visibleEnd = Math.Min(
            textView.Document.TextLength,
            lastLine.LastDocumentLine.EndOffset);
        IReadOnlyList<DiagnosticSegment> visibleSegments =
            _segments.FindOverlappingSegments(
                visibleStart,
                visibleEnd - visibleStart);

        DrawSeverity(
            textView,
            drawingContext,
            visibleSegments,
            EditorSourceDiagnosticSeverity.Warning,
            WarningPen);
        DrawSeverity(
            textView,
            drawingContext,
            visibleSegments,
            EditorSourceDiagnosticSeverity.Error,
            ErrorPen);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        HideToolTip();
        _textView.PointerHover -= TextView_PointerHover;
        _textView.PointerHoverStopped -= TextView_PointerHoverStopped;
        if (_textView.BackgroundRenderers.Contains(this))
            _textView.BackgroundRenderers.Remove(this);

        _snapshot = [];
        _segments = new TextSegmentCollection<DiagnosticSegment>();
        _disposed = true;
    }

    private static bool SnapshotsEqual(
        IReadOnlyList<EditorSourceDiagnostic> current,
        IReadOnlyList<EditorSourceDiagnostic> replacement)
    {
        if (current.Count != replacement.Count)
            return false;

        for (int index = 0; index < current.Count; index++)
        {
            if (current[index] != replacement[index])
                return false;
        }

        return true;
    }

    private static void DrawSeverity(
        TextView textView,
        DrawingContext drawingContext,
        IReadOnlyList<DiagnosticSegment> segments,
        EditorSourceDiagnosticSeverity severity,
        IPen pen)
    {
        StreamGeometry geometry = new();
        bool hasFigures = false;
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach (DiagnosticSegment segment in segments)
            {
                if (segment.Diagnostic.Severity != severity)
                    continue;

                foreach (Rect rectangle in
                    BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    hasFigures |= AppendSquiggle(
                        context,
                        rectangle,
                        textView.Bounds.Size);
                }
            }
        }

        if (hasFigures)
            drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static bool AppendSquiggle(
        StreamGeometryContext context,
        Rect rectangle,
        Size viewport)
    {
        if (rectangle.Bottom < 0 ||
            rectangle.Top > viewport.Height ||
            rectangle.Right < 0 ||
            rectangle.Left > viewport.Width)
        {
            return false;
        }

        double left = Math.Max(0, rectangle.Left);
        double right = Math.Min(viewport.Width, rectangle.Right);
        if (right - left < MinimumSquiggleWidth)
        {
            left = Math.Clamp(
                rectangle.Left,
                0,
                Math.Max(0, viewport.Width - MinimumSquiggleWidth));
            right = Math.Min(viewport.Width, left + MinimumSquiggleWidth);
        }
        if (right <= left)
            return false;

        double centerY = Math.Clamp(
            rectangle.Bottom - SquiggleAmplitude - 0.5,
            SquiggleAmplitude,
            Math.Max(SquiggleAmplitude, viewport.Height - SquiggleAmplitude));
        context.BeginFigure(new Point(left, centerY), isFilled: false);

        double x = left;
        bool peakDown = true;
        while (x < right)
        {
            x = Math.Min(right, x + SquiggleHalfWave);
            context.LineTo(
                new Point(
                    x,
                    centerY + (peakDown
                        ? SquiggleAmplitude
                        : -SquiggleAmplitude)),
                isStroked: true);
            peakDown = !peakDown;
        }

        context.EndFigure(isClosed: false);
        return true;
    }

    private void TextView_PointerHover(object? sender, PointerEventArgs args)
    {
        if (_disposed || _segments.Count == 0 || _textView.Document is null)
        {
            HideToolTip();
            return;
        }

        Point viewportPoint = args.GetPosition(_textView);
        Point documentPoint = viewportPoint + _textView.ScrollOffset;
        TextViewPosition? position = _textView.GetPositionFloor(documentPoint);
        if (position is null)
        {
            HideToolTip();
            return;
        }

        int offset = _textView.Document.GetOffset(position.Value.Location);
        IReadOnlyList<DiagnosticSegment> candidates =
            _segments.FindSegmentsContaining(offset);
        string content = BuildToolTipContent(candidates, offset);
        if (content.Length == 0)
        {
            HideToolTip();
            return;
        }

        _toolTipContent.Text = content;
        if (!_ownsToolTip)
        {
            _replacedToolTip = ToolTip.GetTip(_textView);
            ToolTip.SetTip(_textView, _toolTipContent);
            _ownsToolTip = true;
        }

        ToolTip.SetIsOpen(_textView, true);
    }

    private void TextView_PointerHoverStopped(object? sender, PointerEventArgs args) =>
        HideToolTip();

    private static string BuildToolTipContent(
        IReadOnlyList<DiagnosticSegment> candidates,
        int offset)
    {
        StringBuilder builder = new();
        AppendToolTipDiagnostics(
            builder,
            candidates,
            offset,
            EditorSourceDiagnosticSeverity.Error);
        AppendToolTipDiagnostics(
            builder,
            candidates,
            offset,
            EditorSourceDiagnosticSeverity.Warning);
        return builder.ToString();
    }

    private static void AppendToolTipDiagnostics(
        StringBuilder builder,
        IReadOnlyList<DiagnosticSegment> candidates,
        int offset,
        EditorSourceDiagnosticSeverity severity)
    {
        foreach (DiagnosticSegment candidate in candidates)
        {
            if (candidate.Diagnostic.Severity != severity ||
                !ContainsOffset(candidate, offset))
            {
                continue;
            }

            if (builder.Length > 0)
                builder.AppendLine().AppendLine();
            builder.Append(candidate.Diagnostic.Code)
                .Append(": ")
                .Append(candidate.Diagnostic.Message);
        }
    }

    private static bool ContainsOffset(DiagnosticSegment segment, int offset) =>
        segment.Length == 0
            ? offset == segment.StartOffset
            : offset >= segment.StartOffset && offset < segment.EndOffset;

    private void HideToolTip()
    {
        if (!_ownsToolTip)
            return;

        if (ReferenceEquals(ToolTip.GetTip(_textView), _toolTipContent))
        {
            ToolTip.SetIsOpen(_textView, false);
            ToolTip.SetTip(_textView, _replacedToolTip);
        }

        _replacedToolTip = null;
        _ownsToolTip = false;
    }

    private sealed class DiagnosticSegment : TextSegment
    {
        internal DiagnosticSegment(
            EditorSourceDiagnostic diagnostic,
            int start,
            int length)
        {
            Diagnostic = diagnostic;
            StartOffset = start;
            Length = length;
        }

        internal EditorSourceDiagnostic Diagnostic { get; }
    }
}
