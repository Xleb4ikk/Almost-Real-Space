using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support.Scenarios
{
    internal static class StateFromElements
    {
        public static void Reconstruct(OrbitalElements elements, double mu, out Vector3d position, out Vector3d velocity)
        {
            InitialOrbit.StateFromElements(
                mu,
                elements.SemiMajorAxis,
                elements.Eccentricity,
                elements.InclinationDegrees,
                elements.LongitudeOfAscendingNodeDegrees,
                elements.ArgumentOfPeriapsisDegrees,
                elements.TrueAnomalyDegrees,
                out Double3 r,
                out Double3 v);
            position = r.ToVector3d();
            velocity = v.ToVector3d();
        }
    }
}
