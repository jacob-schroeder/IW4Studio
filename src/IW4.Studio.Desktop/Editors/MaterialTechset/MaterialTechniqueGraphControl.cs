using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.MaterialTechset;

public sealed class MaterialTechniqueGraphControl : Control
{
    public static readonly StyledProperty<MaterialTechniqueSlot?> SlotProperty =
        AvaloniaProperty.Register<MaterialTechniqueGraphControl, MaterialTechniqueSlot?>(
            nameof(Slot));

    private const double NodeWidth = 280;
    private const double NodeHeaderHeight = 31;
    private const double NodeRowHeight = 18;
    private const double NodeBodyPadding = 10;
    private const double GraphMargin = 72;
    private const double SourceX = GraphMargin;
    private const double PassX = 460;
    private const double OutputX = 850;
    private const double BlockGap = 54;

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(18, 21, 27));
    private static readonly Pen GridPen =
        new(new SolidColorBrush(Color.FromArgb(32, 151, 161, 175)), 1);
    private static readonly IBrush NodeBodyBrush =
        new SolidColorBrush(Color.FromRgb(43, 47, 54));
    private static readonly Pen NodeBorderPen =
        new(new SolidColorBrush(Color.FromRgb(84, 92, 104)), 1);
    private static readonly IBrush TitleBrush =
        new SolidColorBrush(Color.FromRgb(242, 245, 248));
    private static readonly IBrush RowBrush =
        new SolidColorBrush(Color.FromRgb(207, 214, 222));
    private static readonly IBrush EmptyBrush =
        new SolidColorBrush(Color.FromRgb(171, 181, 194));
    private static readonly Color DeclarationColor = Color.FromRgb(115, 104, 196);
    private static readonly Color VertexShaderColor = Color.FromRgb(82, 151, 111);
    private static readonly Color PixelShaderColor = Color.FromRgb(190, 111, 67);
    private static readonly Color ArgumentColor = Color.FromRgb(181, 149, 62);
    private static readonly Color PassColor = Color.FromRgb(62, 126, 176);
    private static readonly Color TechniqueColor = Color.FromRgb(125, 78, 109);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    private IPointer? _panPointer;
    private Point _lastPointerPosition;
    private double _zoom = 1;
    private Vector _pan;
    private bool _needsFit = true;

    static MaterialTechniqueGraphControl() =>
        AffectsRender<MaterialTechniqueGraphControl>(SlotProperty);

    public MaterialTechniqueGraphControl()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerCaptureLost += (_, _) => EndPan();
    }

    public MaterialTechniqueSlot? Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public void Fit()
    {
        _needsFit = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, Bounds);
        DrawGrid(context);

        if (Slot?.Technique is not { } technique)
        {
            DrawCenteredLabel(
                context,
                Slot is null
                    ? "Select a technique slot"
                    : $"{FormatIdentifier(Slot.Type.ToString())} is not populated");
            return;
        }

        GraphVisual graph = BuildGraph(Slot.Type, technique);
        if (_needsFit)
            FitCore(graph.Size);

        var transform = new Matrix(
            _zoom,
            0,
            0,
            _zoom,
            _pan.X,
            _pan.Y);
        using (context.PushClip(Bounds))
        using (context.PushTransform(transform))
        {
            foreach (EdgeVisual edge in graph.Edges)
                DrawCable(context, edge);
            foreach (NodeVisual node in graph.Nodes)
                DrawNode(context, node);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SlotProperty)
            _needsFit = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed)
            return;

        Focus(NavigationMethod.Pointer, e.KeyModifiers);
        _panPointer = e.Pointer;
        _lastPointerPosition = e.GetPosition(this);
        _needsFit = false;
        Cursor = PanCursor;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer, _panPointer))
            return;

        Point position = e.GetPosition(this);
        Vector delta = position - _lastPointerPosition;
        _lastPointerPosition = position;
        if (double.IsFinite(delta.X) && double.IsFinite(delta.Y))
        {
            _pan += delta;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!ReferenceEquals(e.Pointer, _panPointer))
            return;

        e.Pointer.Capture(null);
        EndPan();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double wheel = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(wheel) || wheel == 0)
            return;

        Point cursor = e.GetPosition(this);
        double nextZoom = Math.Clamp(
            _zoom * Math.Exp(wheel * 0.14),
            0.18,
            2.5);
        var logicalAtCursor = new Point(
            (cursor.X - _pan.X) / _zoom,
            (cursor.Y - _pan.Y) / _zoom);
        _pan = new Vector(
            cursor.X - logicalAtCursor.X * nextZoom,
            cursor.Y - logicalAtCursor.Y * nextZoom);
        _zoom = nextZoom;
        _needsFit = false;
        InvalidateVisual();
        e.Handled = true;
    }

    private void EndPan()
    {
        _panPointer = null;
        Cursor = null;
    }

    private void FitCore(Size graphSize)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        double availableWidth = Math.Max(1, Bounds.Width - 52);
        double availableHeight = Math.Max(1, Bounds.Height - 52);
        _zoom = Math.Clamp(
            Math.Min(availableWidth / graphSize.Width, availableHeight / graphSize.Height),
            0.18,
            1.15);
        _pan = new Vector(
            (Bounds.Width - graphSize.Width * _zoom) * 0.5,
            (Bounds.Height - graphSize.Height * _zoom) * 0.5);
        _needsFit = false;
    }

    private void DrawGrid(DrawingContext context)
    {
        double step = 30 * _zoom;
        while (step < 16)
            step *= 2;
        while (step > 64)
            step *= 0.5;

        double startX = PositiveModulo(_pan.X, step);
        double startY = PositiveModulo(_pan.Y, step);
        for (double x = startX; x < Bounds.Width; x += step)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = startY; y < Bounds.Height; y += step)
            context.DrawLine(GridPen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private static double PositiveModulo(double value, double divisor)
    {
        double result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static GraphVisual BuildGraph(
        MaterialTechniqueType type,
        MaterialTechniqueAsset technique)
    {
        var nodes = new List<NodeVisual>();
        var passVisuals = new List<PassVisual>();
        double blockY = GraphMargin;

        for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
        {
            MaterialPassAsset pass = technique.Passes[passIndex];
            var sources = new List<(NodeVisual Node, Color CableColor)>();
            double sourceY = blockY;

            AddSource(
                "VERTEX DECLARATION",
                VertexDeclarationRows(pass.VertexDeclaration),
                DeclarationColor);
            AddSource(
                "VERTEX SHADER",
                ShaderRows(pass.VertexShader),
                VertexShaderColor);
            AddSource(
                "PIXEL SHADER",
                ShaderRows(pass.PixelShader),
                PixelShaderColor);
            AddSource(
                "MATERIAL ARGUMENTS",
                ArgumentRows(pass.Args),
                ArgumentColor);

            double sourceBottom = sources[^1].Node.Bounds.Bottom;
            double passHeight = NodeHeight(PassRows(pass).Count);
            double passY = blockY + Math.Max(0, (sourceBottom - blockY - passHeight) * 0.5);
            var passNode = new NodeVisual(
                new Rect(PassX, passY, NodeWidth, passHeight),
                $"PASS {passIndex + 1}",
                PassRows(pass),
                PassColor,
                sources.Count,
                HasOutput: true);
            nodes.Add(passNode);
            passVisuals.Add(new PassVisual(passNode, sources));
            blockY = Math.Max(sourceBottom, passNode.Bounds.Bottom) + BlockGap;

            void AddSource(string title, IReadOnlyList<string> rows, Color color)
            {
                var node = new NodeVisual(
                    new Rect(SourceX, sourceY, NodeWidth, NodeHeight(rows.Count)),
                    title,
                    rows,
                    color,
                    InputCount: 0,
                    HasOutput: true);
                nodes.Add(node);
                sources.Add((node, color));
                sourceY = node.Bounds.Bottom + 18;
            }
        }

        double contentBottom = passVisuals.Count == 0 ? GraphMargin + 110 : blockY - BlockGap;
        IReadOnlyList<string> techniqueRows =
        [
            technique.Name ?? "<unnamed technique>",
            $"Slot: {FormatIdentifier(type.ToString())}",
            $"Flags: {(technique.Flags == MaterialTechniqueFlags.None ? "None" : technique.Flags)}",
            $"Passes: {technique.PassCount}"
        ];
        double outputHeight = NodeHeight(techniqueRows.Count);
        var outputNode = new NodeVisual(
            new Rect(
                OutputX,
                GraphMargin + Math.Max(0, (contentBottom - GraphMargin - outputHeight) * 0.5),
                NodeWidth,
                outputHeight),
            "TECHNIQUE OUTPUT",
            techniqueRows,
            TechniqueColor,
            Math.Max(1, passVisuals.Count),
            HasOutput: false);
        nodes.Add(outputNode);

        var edges = new List<EdgeVisual>();
        foreach (PassVisual visual in passVisuals)
        {
            for (int sourceIndex = 0; sourceIndex < visual.Sources.Count; sourceIndex++)
            {
                (NodeVisual source, Color cableColor) = visual.Sources[sourceIndex];
                edges.Add(new EdgeVisual(
                    source.Output,
                    visual.Pass.Input(sourceIndex),
                    cableColor));
            }
        }
        for (int passIndex = 0; passIndex < passVisuals.Count; passIndex++)
        {
            edges.Add(new EdgeVisual(
                passVisuals[passIndex].Pass.Output,
                outputNode.Input(passIndex),
                PassColor));
        }

        double graphHeight = Math.Max(contentBottom, outputNode.Bounds.Bottom) + GraphMargin;
        return new GraphVisual(
            nodes,
            edges,
            new Size(OutputX + NodeWidth + GraphMargin, graphHeight));
    }

    private static IReadOnlyList<string> VertexDeclarationRows(
        MaterialVertexDeclarationAsset? declaration)
    {
        if (declaration is null)
            return ["<unresolved>"];

        var rows = new List<string>
        {
            $"Streams: {declaration.StreamCount}",
            $"Optional source: {(declaration.HasOptionalSource ? "Yes" : "No")}"
        };
        rows.AddRange(declaration.Routing
            .Take(Math.Min(declaration.StreamCount, declaration.Routing.Count))
            .Select(route =>
            $"{FormatIdentifier(route.Source.ToString())} → {FormatIdentifier(route.Dest.ToString())}"));
        return rows;
    }

    private static IReadOnlyList<string> ShaderRows(MaterialShaderAsset? shader)
    {
        if (shader is null)
            return ["<unresolved>"];

        long byteCount = shader.Data is { } data
            ? data.Length
            : shader.DataSize;
        return
        [
            shader.Name ?? "<unnamed shader>",
            $"Bytecode: {byteCount:N0} bytes"
        ];
    }

    private static IReadOnlyList<string> ArgumentRows(
        IReadOnlyList<MaterialShaderArgumentAsset> arguments)
    {
        if (arguments.Count == 0)
            return ["No retained arguments"];

        const int displayLimit = 12;
        var rows = arguments
            .Take(displayLimit)
            .Select(ArgumentLabel)
            .ToList();
        if (arguments.Count > displayLimit)
            rows.Add($"+ {arguments.Count - displayLimit} more arguments");
        return rows;
    }

    private static string ArgumentLabel(MaterialShaderArgumentAsset argument)
    {
        string value = argument.Type switch
        {
            MaterialShaderArgumentType.CodeVertexConst or
            MaterialShaderArgumentType.CodePixelConst =>
                $"{argument.CodeConstant.Source} [{argument.CodeConstant.FirstRow}+{argument.CodeConstant.RowCount}]",
            MaterialShaderArgumentType.CodePixelSampler =>
                argument.CodeTextureSource.ToString(),
            MaterialShaderArgumentType.MaterialVertexConst or
            MaterialShaderArgumentType.MaterialPixelSampler or
            MaterialShaderArgumentType.MaterialPixelConst =>
                $"hash 0x{argument.MaterialNameHash:X8}",
            MaterialShaderArgumentType.LiteralVertexConst or
            MaterialShaderArgumentType.LiteralPixelConst when argument.LiteralConstant is { } literal =>
                $"({literal.X:G4}, {literal.Y:G4}, {literal.Z:G4}, {literal.W:G4})",
            _ => $"0x{unchecked((uint)argument.ArgumentRaw):X8}"
        };
        return $"r{argument.Dest} · {FormatIdentifier(argument.Type.ToString())} · {value}";
    }

    private static IReadOnlyList<string> PassRows(MaterialPassAsset pass) =>
    [
        $"Per primitive: {pass.PerPrimArgCount}",
        $"Per object: {pass.PerObjArgCount}",
        $"Stable: {pass.StableArgCount}",
        $"Custom samplers: {(pass.CustomSamplerFlags == MaterialCustomSamplerFlags.None ? "None" : pass.CustomSamplerFlags)}",
        $"Precompiled VS: {pass.PrecompiledVertexShader}"
    ];

    private static double NodeHeight(int rowCount) =>
        NodeHeaderHeight + NodeBodyPadding * 2 + Math.Max(1, rowCount) * NodeRowHeight;

    private static void DrawCable(DrawingContext context, EdgeVisual edge)
    {
        double reach = Math.Max(54, Math.Abs(edge.End.X - edge.Start.X) * 0.46);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext cable = geometry.Open())
        {
            cable.BeginFigure(edge.Start, isFilled: false);
            cable.CubicBezierTo(
                new Point(edge.Start.X + reach, edge.Start.Y),
                new Point(edge.End.X - reach, edge.End.Y),
                edge.End);
        }
        context.DrawGeometry(
            null,
            new Pen(new SolidColorBrush(edge.Color), 2.4),
            geometry);
    }

    private static void DrawNode(DrawingContext context, NodeVisual node)
    {
        context.FillRectangle(NodeBodyBrush, node.Bounds, 7);
        context.DrawRectangle(NodeBorderPen, node.Bounds, 7);
        var header = new Rect(
            node.Bounds.X,
            node.Bounds.Y,
            node.Bounds.Width,
            NodeHeaderHeight);
        context.FillRectangle(new SolidColorBrush(node.Accent), header, 7);
        context.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1),
            new Point(header.Left, header.Bottom),
            new Point(header.Right, header.Bottom));
        DrawText(
            context,
            node.Title,
            new Rect(header.X + 11, header.Y + 6, header.Width - 22, 19),
            10,
            TitleBrush,
            FontWeight.SemiBold);

        double rowY = header.Bottom + NodeBodyPadding;
        foreach (string row in node.Rows)
        {
            DrawText(
                context,
                row,
                new Rect(node.Bounds.X + 12, rowY, node.Bounds.Width - 24, NodeRowHeight),
                9,
                RowBrush);
            rowY += NodeRowHeight;
        }

        var portBrush = new SolidColorBrush(node.Accent);
        for (int index = 0; index < node.InputCount; index++)
            DrawPort(context, node.Input(index), portBrush);
        if (node.HasOutput)
            DrawPort(context, node.Output, portBrush);
    }

    private static void DrawPort(DrawingContext context, Point center, IBrush brush) =>
        context.DrawEllipse(
            brush,
            new Pen(new SolidColorBrush(Color.FromRgb(24, 27, 33)), 1.2),
            new Rect(center.X - 5, center.Y - 5, 10, 10));

    private static void DrawText(
        DrawingContext context,
        string text,
        Rect bounds,
        double fontSize,
        IBrush brush,
        FontWeight fontWeight = default)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                Typeface.Default.FontFamily,
                FontStyle.Normal,
                fontWeight == default ? FontWeight.Normal : fontWeight),
            fontSize,
            brush)
        {
            MaxTextWidth = Math.Max(1, bounds.Width),
            MaxTextHeight = Math.Max(1, bounds.Height),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        using (context.PushClip(bounds))
            context.DrawText(formatted, bounds.Position);
    }

    private void DrawCenteredLabel(DrawingContext context, string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            EmptyBrush)
        {
            MaxTextWidth = Math.Max(1, Bounds.Width - 48),
            MaxTextHeight = 36,
            MaxLineCount = 1,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(
            formatted,
            new Point(24, Math.Max(0, (Bounds.Height - formatted.Height) * 0.5)));
    }

    private static string FormatIdentifier(string value) =>
        MaterialTechsetViewerViewModel.FormatIdentifier(value);

    private sealed record GraphVisual(
        IReadOnlyList<NodeVisual> Nodes,
        IReadOnlyList<EdgeVisual> Edges,
        Size Size);

    private sealed record PassVisual(
        NodeVisual Pass,
        IReadOnlyList<(NodeVisual Node, Color CableColor)> Sources);

    private readonly record struct EdgeVisual(Point Start, Point End, Color Color);

    private sealed record NodeVisual(
        Rect Bounds,
        string Title,
        IReadOnlyList<string> Rows,
        Color Accent,
        int InputCount,
        bool HasOutput)
    {
        public Point Input(int index)
        {
            int count = Math.Max(1, InputCount);
            double bodyHeight = Bounds.Height - NodeHeaderHeight;
            return new Point(
                Bounds.Left,
                Bounds.Top + NodeHeaderHeight + bodyHeight * (index + 1) / (count + 1));
        }

        public Point Output => new(Bounds.Right, Bounds.Center.Y);
    }
}
