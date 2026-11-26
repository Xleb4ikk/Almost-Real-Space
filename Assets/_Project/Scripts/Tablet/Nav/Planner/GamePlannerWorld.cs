using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Planning;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Tablet
{
    /// <summary>
    /// Мир планировщика поверх StarSystem игры. Индекс тела = позиция в StarSystem.AllBodies — тот же, что у
    /// NavState.BodyNames и Maneuver.FrameBody. Все обращения к эфемеридам идут из главного потока.
    /// </summary>
    public sealed class GamePlannerWorld : IPlannerWorld, IBodyStateSource, IPlannerShipState
    {
        private readonly StarSystem system;
        private readonly Func<double> clock;
        private readonly Func<Ship> ship;
        private readonly Func<bool> freeFlying;
        private readonly IReadOnlyList<OrbitingBody> bodies;
        private readonly int[] parent;
        private readonly Dictionary<OrbitingBody, int> index = new Dictionary<OrbitingBody, int>();

        public GamePlannerWorld(StarSystem system, Func<double> universeClock, Func<Ship> shipSource, Func<bool> freeFlying = null)
        {
            this.system = system;
            clock = universeClock;
            ship = shipSource;
            this.freeFlying = freeFlying;
            bodies = system.AllBodies;
            parent = new int[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) { index[bodies[i]] = i; }
            for (int i = 0; i < bodies.Count; i++) { parent[i] = bodies[i].Parent == null ? -1 : index[bodies[i].Parent]; }
        }

        public double UniverseNow { get { return clock(); } }
        public int BodyCount { get { return bodies.Count; } }
        public string BodyName(int i) { return bodies[i].Name; }
        public int ParentOf(int i) { return parent[i]; }
        public double Mu(int i) { return bodies[i].ResolveStandardGravitationalParameter(); }
        public double Radius(int i) { return bodies[i].Radius; }
        public double SoiRadius(int i) { return parent[i] < 0 ? double.PositiveInfinity : bodies[i].SphereOfInfluenceRadius; }

        public double SafeAltitude(int i)
        {
            OrbitingBody b = bodies[i];
            return b.Atmosphere != null ? b.Atmosphere.TopAltitudeMeters + 2000d : Math.Max(5000d, 0.002d * b.Radius);
        }

        public bool TryBodyState(int i, double universeTime, out Vector3d position, out Vector3d velocity)
        {
            position = velocity = Vector3d.Zero;
            if (i < 0 || i >= bodies.Count) { return false; }
            try
            {
                system.EvaluateBodyState(bodies[i], universeTime, out position, out velocity);
                return position.IsFinite && velocity.IsFinite;
            }
            catch (Exception)
            {
                // время вне диапазона эфемерид и т.п.: для планировщика эта клетка просто недоступна
                position = velocity = Vector3d.Zero;
                return false;
            }
        }

        public bool TryGetBodyState(int bodyIndex, double universeTimeS, out Vector3d position, out Vector3d velocity)
        {
            return TryBodyState(bodyIndex, universeTimeS, out position, out velocity);
        }

        public bool TryShipState(out Vector3d position, out Vector3d velocity)
        {
            Ship s = ship();
            position = s != null ? s.Position : Vector3d.Zero;
            velocity = s != null ? s.Velocity : Vector3d.Zero;
            return s != null;
        }

        public int ShipBody()
        {
            Ship s = ship();
            if (s == null) { return -1; }
            OrbitingBody b = system.FindDominantBody(s.Position, clock());
            int i;
            return b != null && index.TryGetValue(b, out i) ? i : -1;
        }

        public bool ShipIsFreeFlying { get { return freeFlying == null || freeFlying(); } }

        public double ShipMass { get { Ship s = ship(); return s != null ? s.Mass : 1d; } }
    }
}
