using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Tree-walk fallback for eclipse queries when a <see cref="StarSystem"/> is
    /// not available (the editor preview, a scene being authored, a component that
    /// runs before the runner is built). In Play the callers should prefer
    /// <see cref="StarSystem.StarVisibilityAt"/>: it reuses the parents-first
    /// position cache instead of recursively evaluating every body.
    /// </summary>
    public static class StarOcclusion
    {
        /// <summary>
        /// Fraction of the star's disc visible from <paramref name="observer"/>
        /// around every body of the tree except <paramref name="ignoreBody"/> (the
        /// body the observer stands on; its horizon is a separate model). The root
        /// of the tree is the star; its radius is the physical disc.
        /// </summary>
        public static double VisibilityAt(
            OrbitingBody root,
            Vector3d observer,
            double timeSeconds,
            OrbitingBody ignoreBody,
            out OrbitingBody dominantOccluder)
        {
            dominantOccluder = null;
            if (root == null || !(root.Radius > 0d))
            {
                return 1d;
            }

            root.EvaluateWorldState(timeSeconds, out Vector3d starPosition, out _);
            double starDistance = (starPosition - observer).Magnitude;
            if (!(starDistance > 0d))
            {
                return 1d;
            }

            double visible = 1d;
            double strongest = 0d;
            Visit(
                root, root.Radius, observer, starPosition, starDistance, timeSeconds,
                ignoreBody, ref visible, ref strongest, ref dominantOccluder);
            return StarPhysics.Clamp(visible, 0d, 1d);
        }

        /// <summary>Star visibility at the centre of a body, the body itself excluded.</summary>
        public static double VisibilityForBody(
            OrbitingBody body,
            double timeSeconds,
            out OrbitingBody dominantOccluder)
        {
            dominantOccluder = null;
            if (body == null)
            {
                return 1d;
            }

            OrbitingBody root = body;
            int guard = 0;
            while (root.Parent != null && guard++ < 64)
            {
                root = root.Parent;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d position, out _);
            double visible = VisibilityAt(root, position, timeSeconds, body, out dominantOccluder);
            if (visible >= 1d || dominantOccluder == null)
            {
                return visible;
            }

            // Same whole-body rule as StarSystem.StarVisibilityForBody: a small
            // transiting body does not dim the whole disc.
            dominantOccluder.EvaluateWorldState(timeSeconds, out Vector3d occluderPosition, out _);
            double distance = (occluderPosition - position).Magnitude;
            if (!(distance > 0d))
            {
                return visible;
            }

            return EclipseGeometry.CoversBody(body.Radius, dominantOccluder.Radius, distance)
                ? visible
                : 1d;
        }

        private static void Visit(
            OrbitingBody parent,
            double starRadiusMeters,
            Vector3d observer,
            Vector3d starPosition,
            double starDistance,
            double timeSeconds,
            OrbitingBody ignoreBody,
            ref double visible,
            ref double strongest,
            ref OrbitingBody dominantOccluder)
        {
            if (!(visible > 1e-9d))
            {
                return;
            }

            for (int i = 0; i < parent.Children.Count; i++)
            {
                OrbitingBody body = parent.Children[i];
                if (body == null)
                {
                    continue;
                }

                if (!ReferenceEquals(body, ignoreBody) && body.Radius > 0d)
                {
                    body.EvaluateWorldState(timeSeconds, out Vector3d position, out _);
                    double distance = (position - observer).Magnitude;
                    if (distance > 1d && distance < starDistance)
                    {
                        double fraction = EclipseGeometry.VisibleFraction(
                            observer, starPosition, starRadiusMeters, position, body.Radius);
                        if (fraction < 1d)
                        {
                            double hidden = 1d - fraction;
                            if (hidden > strongest)
                            {
                                strongest = hidden;
                                dominantOccluder = body;
                            }

                            visible *= fraction;
                        }
                    }
                }

                // The ignored body can have children (a moon of a moon) that still
                // cast shadows, so recurse even when the body itself is skipped.
                Visit(
                    body, starRadiusMeters, observer, starPosition, starDistance, timeSeconds,
                    ignoreBody, ref visible, ref strongest, ref dominantOccluder);
            }
        }
    }
}
