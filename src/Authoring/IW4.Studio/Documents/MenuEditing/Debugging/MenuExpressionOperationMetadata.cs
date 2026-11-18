using IW4.Game.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Debugging;

/// <summary>
/// Native result contracts for simulated environment operations. Keeping this
/// metadata shared prevents scenario inputs and evaluator coercion from
/// disagreeing about an operation's value type.
/// </summary>
internal static class MenuExpressionOperationMetadata
{
    public static MenuDebugValueKind? EnvironmentResultKind(
        OperationEnum operation, string? qualifier) => operation switch
        {
            OperationEnum.OP_ANYNEWMAPPACKS or
            OperationEnum.OP_GETPLAYERDATAANYBOOLTRUE or
            OperationEnum.OP_SPECTATINGCLIENT or
            OperationEnum.OP_ISEMPJAMMED or
            OperationEnum.OP_ADSJAVELIN or
            OperationEnum.OP_WEAPLOCKBLINK or
            OperationEnum.OP_WEAPATTACKTOP or
            OperationEnum.OP_WEAPATTACKDIRECT or
            OperationEnum.OP_WEAPLOCKING or
            OperationEnum.OP_WEAPLOCKED or
            OperationEnum.OP_WEAPLOCKTOOCLOSE or
            OperationEnum.OP_ISSPLITSCREENONLINEPOSSIBLE =>
                MenuDebugValueKind.Boolean,
            OperationEnum.OP_WEAPLOCKSCREENPOSX or
            OperationEnum.OP_WEAPLOCKSCREENPOSY => MenuDebugValueKind.Float,
            // The PS3 player-field dispatch at 0x232664 distinguishes strings
            // such as teamname from integer clip counts for either hand.
            OperationEnum.OP_PLAYERFIELD when
                string.Equals(qualifier, "clipAmmo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(qualifier, "clipAmmo_left", StringComparison.OrdinalIgnoreCase) =>
                MenuDebugValueKind.Integer,
            _ => null
        };
}
