using System.ComponentModel;

namespace IW4.Assets.Assets.Menu;

/// <summary>
/// Numeric selector dispatched by the PS3 MW2 HUD and UI owner-draw tables.
/// Only selectors with an active runtime handler are offered for authoring.
/// </summary>
public enum WindowOwnerDraw : int
{
    [Description("None")]
    None = 0,

    // Player HUD.
    [Description("5 · Player ammo value")]
    CG_PLAYER_AMMO_VALUE = 5,

    [Description("6 · Player ammo backdrop")]
    CG_PLAYER_AMMO_BACKDROP = 6,

    [Description("20 · Player stance")]
    CG_PLAYER_STANCE = 20,

    [Description("60 · Spectator following")]
    CG_SPECTATOR_FOLLOWING = 60,

    [Description("61 · Spectator controls")]
    CG_SPECTATOR_CONTROLS = 61,

    [Description("71 · Hold-breath hint")]
    CG_HOLD_BREATH_HINT = 71,

    [Description("72 · Cursor hint")]
    CG_CURSOR_HINT = 72,

    [Description("79 · Player health bar")]
    CG_PLAYER_BAR_HEALTH = 79,

    [Description("80 · Mantle hint")]
    CG_MANTLE_HINT = 80,

    [Description("81 · Weapon name · Faded")]
    CG_PLAYER_WEAPON_NAME_FADE = 81,

    [Description("82 · Weapon-name backdrop · Faded")]
    CG_PLAYER_WEAPON_NAME_BACK_FADE = 82,

    [Description("83 · Weapon name · No fade")]
    CG_PLAYER_WEAPON_NAME = 83,

    [Description("84 · Weapon-name backdrop · No fade")]
    CG_PLAYER_WEAPON_NAME_BACK = 84,

    [Description("90 · Center message")]
    CG_CENTER_MESSAGE = 90,

    [Description("98 · Player health-bar backdrop")]
    CG_PLAYER_BAR_HEALTH_BACK = 98,

    [Description("103 · Frag offhand icon")]
    CG_OFFHAND_WEAPON_ICON_FRAG = 103,

    [Description("104 · Smoke/flash offhand icon")]
    CG_OFFHAND_WEAPON_ICON_SECONDARY = 104,

    [Description("105 · Frag offhand ammo")]
    CG_OFFHAND_WEAPON_AMMO_FRAG = 105,

    [Description("106 · Smoke/flash offhand ammo")]
    CG_OFFHAND_WEAPON_AMMO_SECONDARY = 106,

    [Description("107 · Frag offhand name")]
    CG_OFFHAND_WEAPON_NAME_FRAG = 107,

    [Description("108 · Smoke/flash offhand name")]
    CG_OFFHAND_WEAPON_NAME_SECONDARY = 108,

    [Description("109 · Frag offhand highlight")]
    CG_OFFHAND_WEAPON_SELECT_FRAG = 109,

    [Description("110 · Smoke/flash offhand highlight")]
    CG_OFFHAND_WEAPON_SELECT_SECONDARY = 110,

    [Description("112 · Low-health blood overlay")]
    CG_PLAYER_LOW_HEALTH_OVERLAY = 112,

    [Description("113 · Invalid-command hint")]
    CG_INVALID_CMD_HINT = 113,

    [Description("114 · Sprint meter")]
    CG_PLAYER_SPRINT_METER = 114,

    [Description("115 · Sprint-meter backdrop")]
    CG_PLAYER_SPRINT_BACK = 115,

    [Description("116 · Weapon backdrop")]
    CG_PLAYER_WEAPON_BACKGROUND = 116,

    [Description("117 · Ammo-clip graphic · Hand 0")]
    CG_PLAYER_WEAPON_AMMO_CLIP_GRAPHIC_0 = 117,

    [Description("118 · Primary-weapon icon")]
    CG_PLAYER_WEAPON_PRIMARY_ICON = 118,

    [Description("119 · Ammo stock")]
    CG_PLAYER_WEAPON_AMMO_STOCK = 119,

    [Description("120 · Low-ammo warning")]
    CG_PLAYER_WEAPON_LOW_AMMO_WARNING = 120,

    [Description("121 · Ammo-clip graphic · Hand 1")]
    CG_PLAYER_WEAPON_AMMO_CLIP_GRAPHIC_1 = 121,

    // Partial compass and action slots.
    [Description("145 · Partial-compass ticker tape")]
    CG_PLAYER_COMPASS_TICKERTAPE = 145,

    [Description("146 · Partial-compass ticker tape · No objectives")]
    CG_PLAYER_COMPASS_TICKERTAPE_NO_OBJ = 146,

    [Description("150 · Compass player")]
    CG_PLAYER_COMPASS_PLAYER = 150,

    [Description("151 · Compass-player backdrop")]
    CG_PLAYER_COMPASS_BACK = 151,

    [Description("152 · Compass pointers")]
    CG_PLAYER_COMPASS_POINTERS = 152,

    [Description("155 · Vehicle HUD compass")]
    CG_PLAYER_COMPASS_VEHICLES = 155,

    [Description("156 · Compass planes")]
    CG_PLAYER_COMPASS_PLANES = 156,

    [Description("158 · Compass friendlies")]
    CG_PLAYER_COMPASS_FRIENDS = 158,

    [Description("159 · Compass map")]
    CG_PLAYER_COMPASS_MAP = 159,

    [Description("160 · Compass north coordinate")]
    CG_PLAYER_COMPASS_NORTH_COORD = 160,

    [Description("161 · Compass east coordinate")]
    CG_PLAYER_COMPASS_EAST_COORD = 161,

    [Description("162 · Compass north-coordinate scroll")]
    CG_PLAYER_COMPASS_NORTH_COORD_SCROLL = 162,

    [Description("163 · Compass east-coordinate scroll")]
    CG_PLAYER_COMPASS_EAST_COORD_SCROLL = 163,

    [Description("165 · Compass sentry")]
    CG_PLAYER_COMPASS_SENTRY = 165,

    [Description("166 · Simple compass")]
    CG_PLAYER_COMPASS_SIMPLE = 166,

    [Description("170 · Action-slot D-pad")]
    CG_PLAYER_ACTIONSLOT_DPAD = 170,

    [Description("171 · Action slot 1")]
    CG_PLAYER_ACTIONSLOT_1 = 171,

    [Description("172 · Action slot 2")]
    CG_PLAYER_ACTIONSLOT_2 = 172,

    [Description("173 · Action slot 3")]
    CG_PLAYER_ACTIONSLOT_3 = 173,

    [Description("174 · Action slot 4")]
    CG_PLAYER_ACTIONSLOT_4 = 174,

    [Description("175 · Compass enemies")]
    CG_PLAYER_COMPASS_ENEMIES = 175,

    // Full-screen map.
    [Description("180 · Full map backdrop")]
    CG_PLAYER_FULLMAP_BACK = 180,

    [Description("181 · Full map")]
    CG_PLAYER_FULLMAP_MAP = 181,

    [Description("182 · Full-map pointers")]
    CG_PLAYER_FULLMAP_POINTERS = 182,

    [Description("183 · Full-map player")]
    CG_PLAYER_FULLMAP_PLAYER = 183,

    [Description("185 · Full-map friendlies")]
    CG_PLAYER_FULLMAP_FRIENDS = 185,

    [Description("186 · Full-map location selector")]
    CG_PLAYER_FULLMAP_LOCATION_SELECTOR = 186,

    [Description("187 · Full-map border")]
    CG_PLAYER_FULLMAP_BORDER = 187,

    [Description("188 · Full-map enemies")]
    CG_PLAYER_FULLMAP_ENEMIES = 188,

    [Description("189 · Full-map sentry")]
    CG_PLAYER_FULLMAP_SENTRY = 189,

    // In-game voice indicators.
    [Description("193 · HUD talker slot 1")]
    CG_TALKER_1 = 193,

    [Description("194 · HUD talker slot 2")]
    CG_TALKER_2 = 194,

    [Description("195 · HUD talker slot 3")]
    CG_TALKER_3 = 195,

    [Description("196 · HUD talker slot 4")]
    CG_TALKER_4 = 196,

    // Front-end UI handlers active in the PS3 build.
    [Description("250 · Key-binding status")]
    UI_OWNERDRAW_KEY_BIND_STATUS = 250,

    [Description("266 · Local talking indicator")]
    UI_OWNERDRAW_LOCAL_TALKING = 266,

    [Description("267 · UI talker slot 1")]
    UI_OWNERDRAW_TALKER_1 = 267,

    [Description("268 · UI talker slot 2")]
    UI_OWNERDRAW_TALKER_2 = 268,

    [Description("269 · UI talker slot 3")]
    UI_OWNERDRAW_TALKER_3 = 269,

    [Description("270 · UI talker slot 4")]
    UI_OWNERDRAW_TALKER_4 = 270,

    [Description("272 · Signed-in status")]
    UI_OWNERDRAW_LOGGED_IN_USER = 272,

    [Description("273 · Reserved slots")]
    UI_OWNERDRAW_RESERVED_SLOTS = 273,

    [Description("275 · Playlist description")]
    UI_OWNERDRAW_PLAYLIST_DESCRIPTION = 275,

    [Description("276 · Signed-in user name")]
    UI_OWNERDRAW_LOGGED_IN_USER_NAME = 276,

    [Description("278 · Map custom data")]
    UI_OWNERDRAW_MAP_CUSTOM_DATA = 278
}
