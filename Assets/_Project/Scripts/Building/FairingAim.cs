using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Aim math of the fairing builder (pure, site axes: x north, y zenith, z east).
    /// The player points at a spot beside the rocket; the spot is read on a vertical plane that
    /// contains the fairing axis and faces the camera: height is the point's height, radius is its
    /// distance from the axis. Pulling the aim sideways widens the shell, straight up keeps it.
    /// </summary>
    public static class FairingAim
    {
        /// <summary>
        /// Intersect the camera ray with the vertical plane through the axis that faces the camera.
        /// radius ≥ 0 (left and right of the axis are the same), height is in site axes, distance is the ray length.
        /// </summary>
        public static bool TryAimRing(
            Vector3 rayOrigin, Vector3 rayDirection, Vector2 axisXZ, float reach,
            out float radius, out float height, out float distance)
        {
            radius = 0f;
            height = 0f;
            distance = 0f;

            var toCamera = new Vector2(rayOrigin.x - axisXZ.x, rayOrigin.z - axisXZ.y);
            if (toCamera.sqrMagnitude < 0.01f)
            {
                // Camera is almost on the axis: face the plane against the view direction.
                toCamera = new Vector2(-rayDirection.x, -rayDirection.z);
            }

            if (toCamera.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            toCamera.Normalize();
            float denominator = (rayDirection.x * toCamera.x) + (rayDirection.z * toCamera.y);
            if (Mathf.Abs(denominator) < 0.02f)
            {
                return false;           // looking almost along the plane
            }

            float t = (((axisXZ.x - rayOrigin.x) * toCamera.x) + ((axisXZ.y - rayOrigin.z) * toCamera.y)) / denominator;
            if (t < 0.1f || t > reach)
            {
                return false;
            }

            Vector3 hit = rayOrigin + (rayDirection * t);
            float lateral = ((hit.x - axisXZ.x) * -toCamera.y) + ((hit.z - axisXZ.y) * toCamera.x);
            radius = Mathf.Abs(lateral);
            height = hit.y;
            distance = t;
            return true;
        }
    }
}
