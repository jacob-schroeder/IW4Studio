using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class MenuDefAsset : BaseAsset
{
    public const int SerializedSize = 0x2f0;

    // 0x00: WindowDef begins with the asset-name XString.
    public WindowDef Window { get; init; } = new();
    public XString FontPointer { get; init; }
    public string? Font { get; set; }
    public int Fullscreen { get; init; }
    public int ItemCount { get; init; }
    public int FontIndex { get; init; }
    public IReadOnlyList<int> CursorItems { get; init; } = [];
    public int FadeCycle { get; init; }
    public float FadeClamp { get; init; }
    public float FadeAmount { get; init; }
    public float FadeInAmount { get; init; }
    public float BlurRadius { get; init; }
    public XPointer<MenuEventHandlerSet> OnOpen { get; init; }
    public MenuEventHandlerSet? OnOpenSet { get; set; }
    public XPointer<MenuEventHandlerSet> OnCloseRequest { get; init; }
    public MenuEventHandlerSet? OnCloseRequestSet { get; set; }
    public XPointer<MenuEventHandlerSet> OnClose { get; init; }
    public MenuEventHandlerSet? OnCloseSet { get; set; }
    public XPointer<MenuEventHandlerSet> OnEsc { get; init; }
    public MenuEventHandlerSet? OnEscSet { get; set; }
    public XPointer<ItemKeyHandler> ExecKeys { get; init; }
    public ItemKeyHandler? ExecKeyHandler { get; set; }
    public XPointer<Statement> VisibleExpression { get; init; }
    public Statement? VisibleStatement { get; set; }
    public XString AllowedBinding { get; init; }
    public string? AllowedBindingString { get; set; }
    public XString SoundName { get; init; }
    public string? SoundNameString { get; set; }
    public int ImageTrack { get; init; }
    public Vec4 FocusColor { get; init; } = new();
    public XPointer<Statement> RectXExpression { get; init; }
    public Statement? RectXStatement { get; set; }
    public XPointer<Statement> RectYExpression { get; init; }
    public Statement? RectYStatement { get; set; }
    public XPointer<Statement> RectWExpression { get; init; }
    public Statement? RectWStatement { get; set; }
    public XPointer<Statement> RectHExpression { get; init; }
    public Statement? RectHStatement { get; set; }
    public XPointer<XPointer<ItemDefAsset>[]> ItemsPointer { get; init; }
    public IReadOnlyList<MenuTransition> ScaleTransitions { get; init; } = [];
    public IReadOnlyList<MenuTransition> AlphaTransitions { get; init; } = [];
    public IReadOnlyList<MenuTransition> XTransitions { get; init; } = [];
    public IReadOnlyList<MenuTransition> YTransitions { get; init; } = [];
    public XPointer<ExpressionSupportingData> ExpressionData { get; init; }
    public ExpressionSupportingData? ExpressionDataValue { get; set; }
    public IReadOnlyList<ItemDefReference> Items { get; set; } = [];
}
