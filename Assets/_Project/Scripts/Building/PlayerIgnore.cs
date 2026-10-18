using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// The collider is fully ignored by the player's WALKING (floor, ceiling, capsule sweep,
    /// "fits" checks) but remains an ordinary physics collider for rays: E aim,
    /// screen pointer of monitors, etc. For monitors, the tablet, pilot seat interaction boxes.
    /// </summary>
    public sealed class PlayerIgnore : MonoBehaviour { }
}
