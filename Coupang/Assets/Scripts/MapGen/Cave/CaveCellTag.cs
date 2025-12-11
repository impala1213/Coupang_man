using UnityEngine;

/// <summary>
/// Classification of air cells inside caves, for object placement.
/// Only used for cave interior (corridors / rooms).
/// </summary>
public enum CaveCellTag
{
    None = 0,

    CorridorFloor = 1,
    CorridorWall = 2,
    CorridorCeiling = 3,

    RoomFloor = 4,
    RoomWall = 5,
    RoomCeiling = 6,
}
