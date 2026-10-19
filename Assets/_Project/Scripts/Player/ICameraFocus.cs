using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// A target the player camera smoothly flies to (screen, console, hatch...).
    /// The pose is queried EVERY frame: the target may move with the ship
    /// (floating origin shifts the world), so the world pose must not be cached.
    /// </summary>
    public interface ICameraFocus
    {
        /// <summary>Desired camera pose in world space. false — the target is gone (pose is not applied).</summary>
        bool TryGetFocusPose(Camera camera, out Vector3 position, out Quaternion rotation);
    }
}
