using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Registry of cylindrical decor colliders (tree trunks).
    /// The renderer updates the list for the dominant body from visible chunks near
    /// the player, and SimulationRunner pushes the player out of trunks. Positions and
    /// axes are in the INERTIAL astro frame (like PlayerPosition).
    /// </summary>
    public static class GroundDecorCollisionRegistry
    {
        public struct Cylinder
        {
            /// <summary>Trunk base centre (inertial astro).</summary>
            public Vector3d Position;

            /// <summary>Trunk axis (surface normal, inertial).</summary>
            public Vector3d Up;

            public double Radius;
            public double Height;
        }

        private static readonly List<Cylinder> cylinders = new List<Cylinder>();
        private static string bodyName = string.Empty;

        public static int Count => cylinders.Count;

        public static Cylinder Get(int index) => cylinders[index];

        /// <summary>Begin updating the set for a body (called by the renderer).</summary>
        public static void Begin(string body)
        {
            bodyName = body;
            cylinders.Clear();
        }

        public static void Add(Vector3d position, Vector3d up, double radius, double height)
        {
            cylinders.Add(new Cylinder
            {
                Position = position,
                Up = up,
                Radius = radius,
                Height = height
            });
        }

        /// <summary>
        /// Push a point out of all trunks of the body. Push is horizontal
        /// (along the tangent to the normal); height is measured along the trunk axis.
        /// </summary>
        public static bool TryResolve(string body, Vector3d position, double playerRadius, out Vector3d resolved)
        {
            resolved = position;
            if (!string.Equals(body, bodyName))
            {
                return false;
            }

            for (int i = 0; i < cylinders.Count; i++)
            {
                Cylinder c = cylinders[i];
                Vector3d d = resolved - c.Position;
                double axial = Vector3d.Dot(d, c.Up);
                if (axial < -1d || axial > c.Height)
                {
                    continue;
                }

                Vector3d radial = d - (c.Up * axial);
                double distance = radial.Magnitude;
                double minDistance = c.Radius + playerRadius;
                if (distance >= minDistance)
                {
                    continue;
                }

                Vector3d push;
                if (distance > 1e-6d)
                {
                    push = radial * (1d / distance);
                }
                else
                {
                    push = Vector3d.Cross(c.Up, new Vector3d(0d, 0d, 1d));
                    if (push.SqrMagnitude < 1e-9d)
                    {
                        push = Vector3d.Cross(c.Up, new Vector3d(1d, 0d, 0d));
                    }

                    push = push.Normalized;
                }

                resolved += push * (minDistance - distance);
            }

            return (resolved - position).SqrMagnitude > 0d;
        }
    }
}
