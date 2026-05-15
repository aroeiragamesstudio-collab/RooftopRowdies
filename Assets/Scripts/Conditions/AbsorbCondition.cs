using UnityEngine;

[System.Flags]
public enum AbsorbCondition 
{
    None            = 0,
    Idle            = 1 << 0,   // 1
    Walking         = 1 << 1,   // 2
    Falling         = 1 << 2,   // 4
    Jumping         = 1 << 3,   // 8
    Shot            = 1 << 4,   // 16
    Swinging        = 1 << 5,   // 32
    SatDown         = 1 << 6,   // 64
    HoldingSurface  = 1 << 7,   // 128
    Absorbed        = 1 << 8,   // 256
    Flying          = 1 << 9,   // 512
    Everything      = ~0        // all bits set
}
