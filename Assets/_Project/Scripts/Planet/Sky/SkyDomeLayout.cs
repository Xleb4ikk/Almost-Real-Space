using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Radius of the camera-centred sky domes (atmosphere, clouds, stars).
    ///
    /// The domes are screen-covering proxies: every shader marches the scene
    /// analytically from the ray DIRECTION set in the vertex shader, so the
    /// radius has no effect on what is drawn — it only decides at which depth
    /// the proxy sits. That is exactly why it must be chosen carefully.
    ///
    /// History: the domes used to sit at 0.5·far, all three at the same depth.
    /// Near the ground that is harmless, but FirstPersonCamera grows far with
    /// altitude (≈1400 km at 400 km altitude, ≈1.2e7 m at 10000 km) while the
    /// near plane stays at 0.03 m in the cabin / 0.1 m in EVA. At those ratios
    /// HDRP's depth buffer cannot separate two surfaces at 0.5·far, and the
    /// atmosphere lost its fragments inside a circle around the view axis —
    /// the hole grew with altitude. Separating 0.40/0.45/0.50 fixed the
    /// atmosphere but left the cloud and star domes near the bad distance.
    ///
    /// Fix: stop scaling the domes with far at all. A fixed radius of ~1 km
    /// is always in the well-resolved part of the depth range,
    /// independent of altitude and near/far ratio. Occlusion is unaffected:
    /// the atmosphere and clouds use ZTest Always and their own analytic
    /// depth-based occlusion, and the star field discards every pixel where
    /// the opaque depth is not sky (IsSky), so it does not rely on being
    /// behind the planet either.
    ///
    /// Full write-up (symptom, evidence, checklist for new bodies, diagnosis
    /// recipe): Docs/SkyDomes.md.
    /// </summary>
    internal static class SkyDomeLayout
    {
        /// <summary>Default proxy radius, metres.</summary>
        public const float MinRadiusMeters = 1000f;

        /// <summary>Keep the proxy well in front of the near plane.</summary>
        public const float NearPlaneFactor = 10f;

        /// <summary>Never let the proxy approach the far plane.</summary>
        public const float MaxFarFraction = 0.25f;

        /// <summary>
        /// Proxy radius for the camera the picture is drawn with. Small and
        /// fixed for normal cameras, with guards for extreme near/far setups.
        /// </summary>
        public static float Radius(Camera camera)
        {
            if (camera == null)
            {
                return MinRadiusMeters;
            }

            float radius = Mathf.Max(MinRadiusMeters, camera.nearClipPlane * NearPlaneFactor);
            return Mathf.Min(radius, Mathf.Max(1f, camera.farClipPlane * MaxFarFraction));
        }
    }
}
