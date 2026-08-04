using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Closed v1 edit set for one Menu draft.</summary>
public abstract record MenuEdit;

public sealed record ReplaceMenuSettingsEdit(MenuSettingsValue Value) : MenuEdit;

public sealed record ReplaceRootWindowEdit(MenuWindowValue Value) : MenuEdit;

public sealed record ReplaceItemEdit(
    MenuNodeId ItemId,
    MenuItemValue Value) : MenuEdit;

public sealed record ReplaceItemPayloadEdit(
    MenuNodeId ItemId,
    MenuItemValue Value) : MenuEdit;

public sealed record ReplaceItemWindowEdit(
    MenuNodeId ItemId,
    MenuWindowValue Value) : MenuEdit;

public sealed record AddMenuItemEdit(
    ItemDefType Type,
    int? InsertIndex = null,
    string? Name = null) : MenuEdit;

public sealed record RemoveMenuItemEdit(MenuNodeId ItemId) : MenuEdit;

public sealed record MoveMenuItemEdit(
    MenuNodeId ItemId,
    int DestinationIndex) : MenuEdit;

public sealed record DuplicateMenuItemEdit(
    MenuNodeId ItemId,
    int? InsertIndex = null) : MenuEdit;

public sealed record ChangeMenuItemTypeEdit(
    MenuNodeId ItemId,
    ItemDefType Type) : MenuEdit;

/// <summary>Closed v1 edit set for an ordered MenuFile registration list.</summary>
public abstract record MenuFileEdit;

public sealed record AddExistingMenuRegistrationEdit(
    string MenuName,
    int? InsertIndex = null) : MenuFileEdit;

public sealed record RetargetMenuFileRegistrationEdit(
    MenuRegistrationId RegistrationId,
    string MenuName) : MenuFileEdit;

internal sealed record AddMenuFileRegistrationEdit(
    NestedXAssetBuildLink Link,
    int? InsertIndex = null) : MenuFileEdit;

public sealed record RemoveMenuFileRegistrationEdit(
    MenuRegistrationId RegistrationId) : MenuFileEdit;

public sealed record MoveMenuFileRegistrationEdit(
    MenuRegistrationId RegistrationId,
    int DestinationIndex) : MenuFileEdit;

public sealed record DuplicateMenuFileRegistrationEdit(
    MenuRegistrationId RegistrationId,
    int? InsertIndex = null) : MenuFileEdit;

internal sealed record ReplaceMenuFileRegistrationEdit(
    MenuRegistrationId RegistrationId,
    NestedXAssetBuildLink Link) : MenuFileEdit;

public sealed record EditMenuFileRegistrationMenuEdit(
    MenuRegistrationId RegistrationId,
    MenuEdit Edit) : MenuFileEdit;
