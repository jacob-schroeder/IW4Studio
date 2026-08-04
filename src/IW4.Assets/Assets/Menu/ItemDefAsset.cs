using IW4.Assets.Math;
using IW4.Assets.Zone;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using SoundAliasListAssetModel = IW4.Assets.Assets.Sound.SoundAliasListAsset;

namespace IW4.Assets.Assets.Menu;

public sealed class ItemDefAsset : BaseAsset
{
    public const int SerializedSize = 0x1cc;

    public WindowDef Window { get; init; } = new();
    public IReadOnlyList<RectangleDef> TextRect { get; init; } = [];
    public ItemDefType Type { get; init; }
    public int DataType { get; init; }
    public ItemHorizontalAlignment Align { get; init; }
    public ItemFont FontEnum { get; init; }
    public int TextAlignMode { get; init; }
    public float TextAlignX { get; init; }
    public float TextAlignY { get; init; }
    public float TextScale { get; init; }
    public ItemTextStyle TextStyle { get; init; }
    public int GameMsgWindowIndex { get; init; }
    public int GameMsgWindowMode { get; init; }
    public XString Text { get; init; }
    public string? TextString { get; set; }
    public ItemFlags ItemFlags { get; init; }
    // 0x134: raw serialized parent value. DB_AddXAsset overwrites the runtime
    // destination cell with the registered Menu pointer, so the serialized
    // value remains separate from effective runtime identity.
    public int RuntimeParentPointer { get; init; }
    public XRuntimeAddress? RuntimeParentAddress { get; private set; }
    public XPointer<MenuEventHandlerSet> MouseEnterText { get; init; }
    public MenuEventHandlerSet? MouseEnterTextSet { get; set; }
    public XPointer<MenuEventHandlerSet> MouseExitText { get; init; }
    public MenuEventHandlerSet? MouseExitTextSet { get; set; }
    public XPointer<MenuEventHandlerSet> MouseEnter { get; init; }
    public MenuEventHandlerSet? MouseEnterSet { get; set; }
    public XPointer<MenuEventHandlerSet> MouseExit { get; init; }
    public MenuEventHandlerSet? MouseExitSet { get; set; }
    public XPointer<MenuEventHandlerSet> Action { get; init; }
    public MenuEventHandlerSet? ActionSet { get; set; }
    public XPointer<MenuEventHandlerSet> Accept { get; init; }
    public MenuEventHandlerSet? AcceptSet { get; set; }
    public XPointer<MenuEventHandlerSet> OnFocus { get; init; }
    public MenuEventHandlerSet? OnFocusSet { get; set; }
    public XPointer<MenuEventHandlerSet> LeaveFocus { get; init; }
    public MenuEventHandlerSet? LeaveFocusSet { get; set; }
    public XString Dvar { get; init; }
    public string? DvarString { get; set; }
    public XString DvarTest { get; init; }
    public string? DvarTestString { get; set; }
    public XPointer<ItemKeyHandler> OnKey { get; init; }
    public ItemKeyHandler? OnKeyHandler { get; set; }
    public XString EnableDvar { get; init; }
    public string? EnableDvarString { get; set; }
    public ItemDvarFlags DvarFlags { get; init; }
    public XPointer<SoundAliasListAssetModel> FocusSound { get; init; }
    /// <summary>Resolved only for inspection/detached authoring capture.
    /// The serialized form is a symbolic Sound XAsset reference.</summary>
    public SoundAliasListAssetModel? FocusSoundAsset { get; set; }
    /// <summary>Serialized external Sound identity, distinct from the
    /// resolved runtime alias-list object.</summary>
    public string? FocusSoundName { get; set; }
    public float Special { get; init; }
    public IReadOnlyList<int> CursorPos { get; init; } = [];
    public ItemDefData TypeData { get; init; } = new();
    public EditFieldDef? EditField { get; set; }
    public ListBoxDef? ListBox { get; set; }
    public MultiDef? Multi { get; set; }
    public string? DvarEnumName { get; set; }
    public NewsTickerDef? NewsTicker { get; set; }
    public TextScrollDef? TextScroll { get; set; }
    public int ImageTrack { get; init; }
    public int FloatExpressionCount { get; init; }
    public XPointer<ItemFloatExpression[]> FloatExpressions { get; init; }
    public XPointer<Statement> VisibleExpression { get; init; }
    public Statement? VisibleStatement { get; set; }
    public XPointer<Statement> DisabledExpression { get; init; }
    public Statement? DisabledStatement { get; set; }
    public XPointer<Statement> TextExpression { get; init; }
    public Statement? TextStatement { get; set; }
    public XPointer<Statement> MaterialExpression { get; init; }
    public Statement? MaterialStatement { get; set; }
    public Vec4 GlowColor { get; init; } = new();
    public byte DecayActive { get; init; }
    public byte DecayActivePad0 { get; init; }
    public byte DecayActivePad1 { get; init; }
    public byte DecayActivePad2 { get; init; }
    public int FxBirthTime { get; init; }
    public int FxLetterTime { get; init; }
    public int FxDecayStartTime { get; init; }
    public int FxDecayDuration { get; init; }
    public int LastSoundPlayedTime { get; init; }

    public IReadOnlyList<ItemFloatExpression> LoadedFloatExpressions { get; set; } = [];

    public void SetRuntimeParentAddress(XAssetPoolAddress address)
    {
        RuntimeParentAddress = XRuntimeAddress.FromAssetPool(address);
    }
}
