using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// "Aim zone" — a cone of directions that exactly covers the drawn Crosshair
    /// ring (radius in screen pixels). The builder's aiming (node snapping, delete
    /// by crosshair) and interaction (E) count a hit only inside this cone: the
    /// interaction zone matches the ring area, not "roughly nearby" and not a single
    /// point at the center.
    ///
    /// The angle uses the LIVE camera FOV: RMB zoom narrows the field of view, so the
    /// ring covers fewer directions and the cone narrows with it (otherwise the zone
    /// would drift relative to the drawn ring).
    /// </summary>
    public static class AimCone
    {
        /// <summary>
        /// Cone samples: center + 8 on the ring. The octagon covers the disc with a
        /// gap of ~6% of the radius (≈0.3 px at radius 5) — indistinguishable for the
        /// ring, while an exact cone of revolution would be complex analytics for the
        /// same 0.3 px.
        /// </summary>
        public const int Samples = 9;

        /// <summary>
        /// Tangent of the cone half-angle for the camera: the ring's pixel radius is
        /// converted to an angle via the vertical FOV. Camera null — zero (the zone
        /// degenerates to a point, a fallback for frames without a camera).
        /// </summary>
        public static float Tangent(Camera camera)
        {
            if (camera == null)
            {
                return 0f;
            }

            Crosshair crosshair = camera.GetComponent<Crosshair>();
            float radiusPx = crosshair != null ? crosshair.Radius : Crosshair.DefaultRadiusPx;
            return (radiusPx / (Mathf.Max(1f, Screen.height) * 0.5f))
                * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Sample directions: 0 — center, then Samples-1 around the ring of radius
        /// tangent (in the tangent plane of the view: the same value the snap checks).
        /// The basis is built from forward; when looking nearly along the world
        /// vertical, right is used as reference — a degenerate cross would give zero
        /// directions.
        /// </summary>
        public static void FillDirections(Vector3 forward, float tangent, Vector3[] directions)
        {
            directions[0] = forward;
            Vector3 axis = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, axis));
            Vector3 up = Vector3.Cross(right, forward);
            for (int i = 0; i < Samples - 1; i++)
            {
                float angle = (Mathf.PI * 2f * i) / (Samples - 1);
                Vector3 offset = (right * Mathf.Cos(angle)) + (up * Mathf.Sin(angle));
                directions[i + 1] = Vector3.Normalize(forward + (offset * tangent));
            }
        }
    }
}
