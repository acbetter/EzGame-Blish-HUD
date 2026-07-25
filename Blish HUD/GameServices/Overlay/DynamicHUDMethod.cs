using System.ComponentModel;

namespace Blish_HUD.Graphics {
    public enum DynamicHUDMethod : int {
        [LocalizedGraphicsDescription("Enum_DynamicHUDMethod_AlwaysShow", "Always Show (Default)")]
        AlwaysShow = 0,

        [LocalizedGraphicsDescription("Enum_DynamicHUDMethod_ShowPeaceful", "Show Only out of Combat")]
        ShowPeaceful = 1,

        [LocalizedGraphicsDescription("Enum_DynamicHUDMethod_ShowInCombat", "Show Only in Combat")]
        ShowInCombat = 2,

        [LocalizedGraphicsDescription("Enum_DynamicHUDMethod_NeverShow", "Never Show")]
        NeverShow = 3
    }
}
