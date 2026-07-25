using System.ComponentModel;

namespace Blish_HUD.Graphics {
    public enum DpiMethod : int {
        [LocalizedGraphicsDescription("Enum_DpiMethod_SyncWithGame", "Automatic - Sync With Game")]
        SyncWithGame = 0,

        [LocalizedGraphicsDescription("Enum_DpiMethod_UseGameDpi", "Enabled - Use Game DPI")]
        UseGameDpi = 1,

        [LocalizedGraphicsDescription("Enum_DpiMethod_NoScaling", "Disabled - Never Scale")]
        NoScaling = 2
    }
}
