using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// "Can the camera see this body" — a RENDER question, never a gravity one.
    ///
    /// The sphere of influence answers "which body owns the ship's physics". It is
    /// not a visibility boundary: Terra's SOI ends at 165 500 km, where the planet
    /// is still 0.8 degrees (~8 px) across. Gating the surface on
    /// <c>CameraBody == body</c> made Terra (and every body without a far disc)
    /// pop out of existence exactly at that line. These helpers answer the actual
    /// question instead:
    ///   * is the body inside the camera's far plane (or is the eye at/inside it),
    ///   * is the body in front of the camera (not behind it),
    ///   * how many pixels its radius covers.
    /// A body that fails all three is not rasterised at any LOD and its proxy disc
    /// is the honest representation.
    /// </summary>
    public static class BodyVisibility
    {
        /// <summary>Fully inside the far plane, with a small margin for the limb.</summary>
        public static bool IsInsideFarPlane(OrbitingBody body, Camera camera, Vector3d bodyPosition, double farFraction = 0.98d)
        {
            if (body == null || camera == null)
            {
                return false;
            }

            Vector3d relative = bodyPosition - FloatingOrigin.Anchor;
            double distance = relative.Magnitude;
            if (!(distance > 1d) || distance <= body.Radius)
            {
                return true;
            }

            return distance - body.Radius < camera.farClipPlane * farFraction;
        }

        /// <summary>
        /// In front of the camera. The margin is the body's angular radius, so a
        /// large body whose centre is behind the eye can still have a visible limb.
        /// </summary>
        public static bool IsInFront(Camera camera, Vector3d bodyPosition, double bodyRadius, double distance)
        {
            if (camera == null)
            {
                return false;
            }

            Vector3 render = FloatingOrigin.ToRender(bodyPosition) - camera.transform.position;
            float magnitude = render.magnitude;
            if (magnitude < 1e-6f)
            {
                return true;
            }

            double angularRadius = distance > bodyRadius
                ? System.Math.Asin(bodyRadius / distance)
                : System.Math.PI;
            float minDot = -(float)System.Math.Sin(angularRadius) - 0.01f;
            return Vector3.Dot(camera.transform.forward, render / magnitude) >= minDot;
        }

        /// <summary>Inside the far plane and in front: the real LOD can be rasterised.</summary>
        public static bool IsRasterised(
            OrbitingBody body, Camera camera, Vector3d bodyPosition, double farFraction = 0.98d)
        {
            if (body == null || camera == null)
            {
                return false;
            }

            double distance = (bodyPosition - FloatingOrigin.Anchor).Magnitude;
            if (!(distance > 1d))
            {
                return false;
            }

            return IsInsideFarPlane(body, camera, bodyPosition, farFraction)
                && IsInFront(camera, bodyPosition, body.Radius, distance)
                // A body below half a pixel cannot be meaningfully rasterised by a
                // 4-chunk cube face: it only aliases as it moves. The far disc (which
                // has its own screen floor) is the stable representation there.
                && RadiusPixels(camera, body.Radius, distance) >= 0.5d;
        }

        /// <summary>Angular radius in pixels, at the camera's vertical FOV.</summary>
        public static double RadiusPixels(Camera camera, double radius, double distance)
        {
            if (camera == null || !(distance > 1d))
            {
                return 0d;
            }

            double halfFov = camera.fieldOfView * Mathf.Deg2Rad * 0.5;
            if (!(halfFov > 1e-6) || !(camera.pixelHeight > 0))
            {
                return 0d;
            }

            return System.Math.Atan2(radius, distance) / System.Math.Tan(halfFov) * (camera.pixelHeight * 0.5);
        }
    }
}
