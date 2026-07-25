using System.ComponentModel;

namespace Blish_HUD.Graphics {

    public enum FramerateMethod : int {
        [LocalizedGraphicsDescription("Enum_FramerateMethod_Custom", "Custom")]
        Custom        = -1,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_SyncWithGame", "Sync With Game")]
        SyncWithGame  = 0,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo30Fps", "Locked To 30 Fps")]
        LockedTo30Fps = 1,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo60Fps", "Locked To 60 Fps")]
        LockedTo60Fps = 2,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo90Fps", "Locked To 90 Fps")]
        LockedTo90Fps = 3,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_Unlimited", "Match Monitor Refresh Rate")]
        Unlimited     = 4,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_TrueUnlimited", "Truly Unlimited")]
        TrueUnlimited = 5,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo120Fps", "Locked To 120 Fps")]
        LockedTo120Fps = 6,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo144Fps", "Locked To 144 Fps")]
        LockedTo144Fps = 7,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo165Fps", "Locked To 165 Fps")]
        LockedTo165Fps = 8,
        [LocalizedGraphicsDescription("Enum_FramerateMethod_LockedTo240Fps", "Locked To 240 Fps")]
        LockedTo240Fps = 9,
    }

}
