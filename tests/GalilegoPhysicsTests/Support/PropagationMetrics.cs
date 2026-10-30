using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support
{
    internal readonly struct ConservationDrift
    {
        public readonly double Energy;
        public readonly double AngularMomentum;
        public readonly double LaplaceRungeLenz;

        public ConservationDrift(double energy, double angularMomentum, double laplaceRungeLenz)
        {
            Energy = energy;
            AngularMomentum = angularMomentum;
            LaplaceRungeLenz = laplaceRungeLenz;
        }
    }

    internal static class PropagationMetrics
    {
        public static double Energy(SpacecraftIntegrationState state, double mu)
        {
            Double3 r = ToDouble3(state.Position);
            Double3 v = ToDouble3(state.Velocity);
            return (0.5d * v.SqrMagnitude) - (mu / r.Magnitude);
        }

        public static Double3 AngularMomentum(SpacecraftIntegrationState state)
        {
            return Double3.Cross(ToDouble3(state.Position), ToDouble3(state.Velocity));
        }

        public static Double3 LaplaceRungeLenz(SpacecraftIntegrationState state, double mu)
        {
            Double3 r = ToDouble3(state.Position);
            Double3 v = ToDouble3(state.Velocity);
            Double3 h = Double3.Cross(r, v);
            return (Double3.Cross(v, h) / mu) - (r / r.Magnitude);
        }

        public static ConservationDrift Drift(
            SpacecraftIntegrationState initial,
            SpacecraftIntegrationState current,
            double mu)
        {
            double energy0 = Energy(initial, mu);
            double energy1 = Energy(current, mu);
            double energyScale = Math.Max(
                Math.Abs(energy0),
                (0.5d * initial.Velocity.SqrMagnitude) + (mu / initial.Position.Magnitude));
            double energyDrift = energyScale > 0d ? Math.Abs(energy1 - energy0) / energyScale : Math.Abs(energy1 - energy0);

            Double3 h0 = AngularMomentum(initial);
            Double3 h1 = AngularMomentum(current);
            double hScale = Math.Max(h0.Magnitude, 1e-300d);
            double angularDrift = (h1 - h0).Magnitude / hScale;

            Double3 a0 = LaplaceRungeLenz(initial, mu);
            Double3 a1 = LaplaceRungeLenz(current, mu);
            double lrlDrift = (a1 - a0).Magnitude;

            return new ConservationDrift(energyDrift, angularDrift, lrlDrift);
        }

        private static Double3 ToDouble3(Vector3d value) => new Double3(value.X, value.Y, value.Z);
    }
}
