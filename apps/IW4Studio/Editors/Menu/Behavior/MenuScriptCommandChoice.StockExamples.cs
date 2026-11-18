namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

internal sealed partial record MenuScriptCommandChoice
{
    // Extracted from type-0 event handlers in the stock PS3 multiplayer fastfiles.
    public (string Zone, string Menu, string Script)? StockExample => Name switch
    {
        "show" => ("common_mp.ff", "votemap_splitscreen", "show \"maps\" ;"),
        "hide" => ("common_mp.ff", "votemap_splitscreen", "hide \"maps\" ;"),
        "open" => ("code_post_gfx_mp.ff", "error_popmenu", "open \"main\" ;"),
        "close" => ("code_post_gfx_mp.ff", "acceptinvite_warning", "close \"self\" ;"),
        "setitemcolor" => ("code_post_gfx_mp.ff", "acceptinvite_warning", "setItemColor \"self\" \"backcolor\" 0 0 0 1 ;"),
        "focusfirst" => ("code_post_gfx_mp.ff", "error_popmenu_lobby", "focusFirst ;"),
        "setfocus" => ("common_mp.ff", "kickwarning", "setFocus \"cancel_button\" ;"),
        "setfocusbydvar" => ("ui_mp.ff", "settings_map", "setfocusbydvar \"ui_mapname\" ;"),
        "setdvar" => ("ui_mp.ff", "main", "setdvar \"ui_mptype\" 0 ;"),
        "exec" => ("common_mp.ff", "menu_xboxlive", "exec \"exec default_720p.cfg\" ;"),
        "execnow" => ("ui_mp.ff", "menu_cac_assault", "execnow \"uploadstats\" ;"),
        "execOnDvarIntValue" => ("ui_mp.ff", "settings_gametype", "execOnDvarIntValue \"systemlink\" 1 \"exec default_systemlink.cfg\" ;"),
        "execNowOnDvarIntValue" => ("ui_mp.ff", "settings_quick_ctf", "execNowOnDvarIntValue \"g_hardcore\" 0 \"exec hardcore_disable.cfg\" ;"),
        "play" => ("code_post_gfx_mp.ff", "acceptinvite_warning", "play \"mouse_over\" ;"),
        "scriptmenuresponse" => ("common_mp.ff", "changeclass", "scriptMenuResponse \"class0\" ;"),
        "setPlayerData" => ("common_mp.ff", "menu_xboxlive", "setPlayerData ( \"round\" , \"awardCount\" , 0 ) ;"),
        "setPlayerDataSplitScreen" => ("common_mp.ff", "menu_xboxlive", "setPlayerDataSplitscreen ( 0 , \"round\" , \"awardCount\" , 0 ) ;"),
        "resetStatsConfirm" => ("code_post_gfx_mp.ff", "menu_resetstats_warning", "resetStatsConfirm ;"),
        "resetStatsCancel" => ("code_post_gfx_mp.ff", "menu_resetstats_warning", "resetStatsCancel ;"),
        "feederTop" => ("ui_mp.ff", "menu_leaderboard_accuracy", "feederTop ;"),
        "kickPlayer" => ("common_mp.ff", "kickwarning", "kickplayer ;"),
        "getKickPlayerQuestion" => ("common_mp.ff", "player_popup", "getKickPlayerQuestion ;"),
        "makehost" => ("patch_mp.ff", "hostchangewarning", "makehost ;"),
        "togglePlayerMute" => ("common_mp.ff", "player_popup", "togglePlayerMute ;"),
        "addFriendFromId" => ("common_mp.ff", "player_popup", "addFriendFromId ;"),
        "resolveError" => ("code_post_gfx_mp.ff", "error_popmenu", "resolveError ;"),
        "lerp" => ("common_mp.ff", "dirt_effect_center", "lerp \"alpha\" \"from\" 1 \"to\" 0 \"over\" \"1.0\" ;"),
        "uiScript" => ("code_post_gfx_mp.ff", "error_popmenu", "uiScript \"clearError\" ;"),
        _ => null
    };
}
