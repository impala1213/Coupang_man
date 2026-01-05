using UnityEngine;

/// <summary>
/// Marker component to indicate that a cave piece (room/corridor/cap) was successfully placed
/// by the current dungeon generation run.
///
/// We use a token to avoid accidentally treating "attempt" pieces as placed,
/// even if they remain in the hierarchy until end-of-frame (Destroy).
/// </summary>
[DisallowMultipleComponent]
public class CavePlacedPiece : MonoBehaviour
{
    [SerializeField] private int placedToken = 0;

    public int PlacedToken => placedToken;

    /// <summary>
    /// Mark this piece as placed for the given dungeon token.
    /// </summary>
    public void MarkPlaced(int token)
    {
        placedToken = token;
    }

    /// <summary>
    /// True if this piece is placed for the given dungeon token.
    /// </summary>
    public bool IsPlacedFor(int token)
    {
        return placedToken == token && token != 0;
    }
}
