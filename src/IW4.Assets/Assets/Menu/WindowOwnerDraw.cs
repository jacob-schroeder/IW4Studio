namespace IW4.Assets.Assets.Menu;

public enum WindowOwnerDraw : int
{
    None = 0,

    /// <summary>
    /// Selector 0x0FA tests key-bind state and draws EXE_KEYWAIT or EXE_KEYCHANGE.
    /// </summary>
    UI_OWNERDRAW_KEY_BIND_STATUS = 0x0FA,

    /// <summary>
    /// Selector 0x0FB dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_0FB = 0x0FB,

    /// <summary>
    /// Selector 0x0FC dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_0FC = 0x0FC,

    /// <summary>
    /// Selector 0x0FD dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_0FD = 0x0FD,

    /// <summary>
    /// Selector 0x0FE dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_0FE = 0x0FE,

    /// <summary>
    /// Selector 0x0FF dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_0FF = 0x0FF,

    /// <summary>
    /// Selector 0x100 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_100 = 0x100,

    /// <summary>
    /// Selector 0x101 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_101 = 0x101,

    /// <summary>
    /// Selector 0x102 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_102 = 0x102,

    /// <summary>
    /// Selector 0x103 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_103 = 0x103,

    /// <summary>
    /// Selector 0x104 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_104 = 0x104,

    /// <summary>
    /// Selector 0x105 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_105 = 0x105,

    /// <summary>
    /// Selector 0x106 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_106 = 0x106,

    /// <summary>
    /// Selector 0x107 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_107 = 0x107,

    /// <summary>
    /// Selector 0x108 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_108 = 0x108,

    /// <summary>
    /// Selector 0x109 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_109 = 0x109,

    /// <summary>
    /// Selector 0x10A draws the voice_on material while local talking is active.
    /// </summary>
    UI_OWNERDRAW_LOCAL_TALKING = 0x10A,

    /// <summary>
    /// Selector 0x10B draws talker slot 0.
    /// </summary>
    UI_OWNERDRAW_TALKER_NUM_0 = 0x10B,

    /// <summary>
    /// Selector 0x10C draws talker slot 1.
    /// </summary>
    UI_OWNERDRAW_TALKER_NUM_1 = 0x10C,

    /// <summary>
    /// Selector 0x10D draws talker slot 2.
    /// </summary>
    UI_OWNERDRAW_TALKER_NUM_2 = 0x10D,

    /// <summary>
    /// Selector 0x10E draws talker slot 3.
    /// </summary>
    UI_OWNERDRAW_TALKER_NUM_3 = 0x10E,

    /// <summary>
    /// Selector 0x10F dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_10F = 0x10F,

    /// <summary>
    /// Selector 0x110 draws signed-in user text.
    /// </summary>
    UI_OWNERDRAW_LOGGED_IN_USER = 0x110,

    /// <summary>
    /// Selector 0x111 formats and draws a reserved-slot/count value.
    /// </summary>
    UI_OWNERDRAW_RESERVED_SLOTS = 0x111,

    /// <summary>
    /// Selector 0x112 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_112 = 0x112,

    /// <summary>
    /// Selector 0x113 draws playlist description/population/party-size text.
    /// </summary>
    UI_OWNERDRAW_PLAYLIST_DESCRIPTION = 0x113,

    /// <summary>
    /// Selector 0x114 draws the logged-in user name.
    /// </summary>
    UI_OWNERDRAW_LOGGED_IN_USER_NAME = 0x114,

    /// <summary>
    /// Selector 0x115 dispatches directly to the owner-draw epilogue.
    /// </summary>
    UI_OWNERDRAW_NOOP_115 = 0x115,

    /// <summary>
    /// Selector 0x116 draws map custom data selected by longname, description,
    /// or mapimage.
    /// </summary>
    UI_OWNERDRAW_MAP_CUSTOM_DATA = 0x116
}
