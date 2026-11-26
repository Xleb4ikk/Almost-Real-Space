using System;
using Galilego.Core;
using Galilego.Planning;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Tablet
{
    /// <summary>
    /// n-body прогон корабля с ТОЙ ЖЕ физикой, что у прогноза на карте и у самого полёта: N-тел по эфемеридам
    /// (CachedGravitySource: барицентрический член звезды и J2) и сопротивление в атмосферах (DragSource).
    /// Отличие от прогноза карты — боевой допуск (RelativeTolerance по умолчанию 1e-11): планировщику нужны метры, а не пиксели.
    /// Вызывать только из главного потока (кэши StarSystem не потокобезопасны).
    /// </summary>
    public sealed class GamePlannerPropagator : IPlannerPropagator
    {
        private const double ChunkSeconds = 3d * 86400d;
        private readonly SpacecraftPhysics physics;
        private readonly Func<double> mass;

        public GamePlannerPropagator(StarSystem system, Func<double> shipMassKg)
        {
            mass = shipMassKg;
            physics = new SpacecraftPhysics { ToleranceFrameSystem = system, MaxStepSize = 6d * 3600d };
            physics.Sources.Add(new CachedGravitySource(system));
            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                OrbitingBody b = system.AllBodies[i];
                if (b.Atmosphere != null) { physics.Sources.Add(new DragSource(b)); }
            }
        }

        public bool TryPropagate(double t0, Vector3d worldPos, Vector3d worldVel, double t1, out Vector3d pos, out Vector3d vel)
        {
            var probe = new Ship(worldPos, worldVel, mass());
            double t = t0;
            while (t < t1 - 1e-9d)
            {
                double dt = Math.Min(ChunkSeconds, t1 - t);
                physics.Step(probe, t, dt);
                t += dt;
                if (!probe.Position.IsFinite || !probe.Velocity.IsFinite) { pos = vel = Vector3d.Zero; return false; }
            }

            pos = probe.Position;
            vel = probe.Velocity;
            return true;
        }
    }
}
