using System.ComponentModel;

namespace Blish_HUD.Graphics {
    public enum ManualUISize : int {
        [LocalizedGraphicsDescription("Enum_ManualUISize_SyncWithGame", "Automatic - Sync With Game")]
        SyncWithGame = 0,

        [LocalizedGraphicsDescription("Enum_ManualUISize_Small", "Small")]
        Small = 1,

        [LocalizedGraphicsDescription("Enum_ManualUISize_Normal", "Normal")]
        Normal = 2,

        [LocalizedGraphicsDescription("Enum_ManualUISize_Large", "Large")]
        Large = 3,

        [LocalizedGraphicsDescription("Enum_ManualUISize_Larger", "Larger")]
        Larger = 4
    }
}
