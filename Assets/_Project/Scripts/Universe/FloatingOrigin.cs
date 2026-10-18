using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Single render anchor (floating origin, double). Absolute astro position mapped
    /// to Unity zero. All objects are placed as ToSimulation(abs − Anchor): near the
    /// player the coordinates are small (fractions of a mm) instead of ~planet radius,
    /// where float ulp ≈ 0.1 m causes terrain jitter, TAA smearing and "crazy" sun
    /// direction. SimulationRunner sets Anchor = PlayerPosition every frame after
    /// physics (views run in LateUpdate, so they read the fresh anchor).
    /// </summary>
    public static class FloatingOrigin
    {
        public static Vector3d Anchor;

        public static Vector3 ToRender(Vector3d absoluteAstro)
        {
            return AstroFrame.ToSimulation(absoluteAstro - Anchor);
        }

        public static Quaternion RenderRotation(QuaternionD orientation)
        {
            return AstroFrame.ToSimulation(orientation);
        }
    }
}
