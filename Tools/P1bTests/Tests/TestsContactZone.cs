using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

// Зона контакта (вертикальный срез). Test300–302 — орбитальная безопасность решения о входе:
// на орбите и на входе зона не включается, посадка включает. Test303 — гистерезис.
// Test304–305 — точность и совместное вращение перехода координат.
// Test306 — AdvanceTime (время в зоне без физики). Test307–308 — острова разрыва стыков.
internal static partial class P1bTests
{
    private static OrbitingBody ZonePlanet(StarSystem sys, out ITerrainModel terrain)
    {
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 86400d;
        terrain = new SphericalTerrain();
        return planet;
    }

    // Скорость неподвижного на поверхности тела на высоте alt над радиусом (совместное вращение).
    private static Vector3d StandVelocity(OrbitingBody planet, Vector3d nearPoint, double t, double altitude)
    {
        planet.SurfaceLatLonAt(nearPoint, t, out double lat, out double lon);
        planet.GetSurfaceState(lat, lon, altitude, t, out _, out Vector3d v);
        return v;
    }

    static int Test300_GateIgnoresOrbit()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        double mu = planet.StandardGravitationalParameter;
        var gate = new ContactZoneGate();
        int insideCount = 0;
        double minSpeed = double.MaxValue;
        for (int i = 0; i < 200; i++)
        {
            double t = i * 60d;
            planet.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
            double r = planet.Radius + 200000d;
            double vCirc = Math.Sqrt(mu / r);
            double phase = 2d * Math.PI * i / 200d;
            Vector3d pos = bp + new Vector3d(r * Math.Cos(phase), 0d, r * Math.Sin(phase));
            Vector3d vel = bv + new Vector3d(-vCirc * Math.Sin(phase), 0d, vCirc * Math.Cos(phase));
            ContactZoneGate.Measure(planet, terrain, pos, vel, t, out _, out double speed);
            minSpeed = Math.Min(minSpeed, speed);
            if (gate.Update(planet, terrain, pos, vel, t)) insideCount++;
        }

        Check(insideCount == 0 && minSpeed > 1000d, "T300 gate-ignores-orbit",
            string.Format(Inv, "орбита 200 км: кадров внутри зоны={0} (ожидание 0); min |v_rel|={1:F0} м/с (порог входа 60)",
                insideCount, minSpeed));
        return 0;
    }

    static int Test301_GateIgnoresEntry()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        var gate = new ContactZoneGate();
        int insideCount = 0;
        for (int i = 0; i < 100; i++)
        {
            double t = i * 0.1d;
            planet.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
            Vector3d radial = new Vector3d(1d, 0d, 0d);
            Vector3d pos = bp + (radial * (planet.Radius + 100d));
            Vector3d vel = bv + new Vector3d(0d, -300d, 0d);
            if (gate.Update(planet, terrain, pos, vel, t)) insideCount++;
        }

        Check(insideCount == 0, "T301 gate-ignores-entry",
            string.Format(Inv, "вход 300 м/с на высоте 100 м: кадров внутри зоны={0} (ожидание 0)", insideCount));
        return 0;
    }

    static int Test302_GateEnablesLanding()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        var gate = new ContactZoneGate();
        planet.EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
        Vector3d pos = bp + (new Vector3d(1d, 0d, 0d) * (planet.Radius + 100d));
        Vector3d vel = StandVelocity(planet, pos, 0d, 100d) + new Vector3d(0d, -5d, 0d);
        bool entered = gate.Update(planet, terrain, pos, vel, 0d);
        ContactZoneGate.Measure(planet, terrain, pos, vel, 0d, out double altitude, out double speed);

        Check(entered && altitude > 99d && altitude < 101d && speed < 60d, "T302 gate-enables-landing",
            string.Format(Inv, "высота {0:F2} м, v_rel {1:F2} м/с: зона={2} (ожидание true)", altitude, speed, entered));
        return 0;
    }

    static int Test303_GateHysteresis()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        var gate = new ContactZoneGate();
        planet.EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
        Vector3d n = new Vector3d(1d, 0d, 0d);
        Vector3d p100 = bp + (n * (planet.Radius + 100d));
        Vector3d p350 = bp + (n * (planet.Radius + 350d));
        Vector3d p450 = bp + (n * (planet.Radius + 450d));
        Vector3d p50 = bp + (n * (planet.Radius + 50d));
        bool a = gate.Update(planet, terrain, p100, StandVelocity(planet, p100, 0d, 100d), 0d);
        bool b = gate.Update(planet, terrain, p350, StandVelocity(planet, p350, 0d, 350d), 0d);
        bool c = gate.Update(planet, terrain, p450, StandVelocity(planet, p450, 0d, 450d), 0d);
        bool d = gate.Update(planet, terrain, p50, StandVelocity(planet, p50, 0d, 50d), 0d);
        gate.Reset();
        bool e = gate.Update(planet, terrain, p350, StandVelocity(planet, p350, 0d, 350d), 0d);
        bool f = gate.Update(planet, terrain, p100, StandVelocity(planet, p100, 0d, 100d) + new Vector3d(130d, 0d, 0d), 0d);

        Check(a && b && !c && d && !e && !f, "T303 gate-hysteresis",
            string.Format(Inv, "вход 100 м→{0}; 350 м (в полосе)→{1}; 450 м (выход)→{2}; снова 50 м→{3}; после сброса 350 м→{4}; 100 м со скоростью 130 м/с→{5} (ожидание T,T,F,T,F,F)",
                a, b, c, d, e, f));
        return 0;
    }

    static int Test304_HandoffRoundTrip()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        double worstPos = 0d;
        double worstVel = 0d;
        for (int i = 0; i < 40; i++)
        {
            double t = 3600d * i + 123.456d;
            planet.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
            double lon = 0.37d * i;
            Vector3d dir = new Vector3d(Math.Cos(lon), 0.3d * Math.Sin(lon), Math.Sin(lon)).Normalized;
            Vector3d pos = bp + (dir * (planet.Radius + 50d + 10d * i));
            Vector3d vel = bv + new Vector3d(3d * Math.Sin(i), 1d * i, -2d * Math.Cos(i));
            SiteFrame frame = SiteFrame.Anchor(planet, terrain, pos, t);
            frame.ToLocal(pos, vel, out Vector3d lp, out Vector3d lv);
            frame.ToWorld(lp, lv, out Vector3d back, out Vector3d backV);
            worstPos = Math.Max(worstPos, (back - pos).Magnitude);
            worstVel = Math.Max(worstVel, (backV - vel).Magnitude);
        }

        Check(worstPos <= 1e-6d && worstVel <= 1e-9d, "T304 handoff-round-trip",
            string.Format(Inv, "40 состояний: max |Δr|={0:E2} м (≤1e-6), max |Δv|={1:E2} м/с (≤1e-9)", worstPos, worstVel));
        return 0;
    }

    static int Test305_HandoffCorotation()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = ZonePlanet(sys, out ITerrainModel terrain);
        double t0 = 1000d;
        double t1 = t0 + 7200d;
        planet.EvaluateWorldState(t0, out Vector3d bp0, out Vector3d bv0);
        Vector3d pos0 = bp0 + (new Vector3d(0d, 1d, 0d) * (planet.Radius + 2d));
        SiteFrame frame0 = SiteFrame.Anchor(planet, terrain, pos0, t0);
        frame0.ToLocal(pos0, StandVelocity(planet, pos0, t0, 2d), out Vector3d lp, out Vector3d lv);

        // Покой на высоте 2 м: локальная скорость ноль; через 2 ч мировая скорость и положение
        // совпадают с состоянием неподвижной точки на той же высоте (совместное вращение).
        SiteFrame frame1 = frame0.At(t1);
        frame1.ToWorld(lp, Vector3d.Zero, out Vector3d worldP, out Vector3d worldV);
        planet.GetSurfaceState(frame0.LatitudeDegrees, frame0.LongitudeDegrees, 2d, t1,
            out Vector3d expectP, out Vector3d expectV);
        double errV = (worldV - expectV).Magnitude;
        double errP = (worldP - expectP).Magnitude;

        Check(errV <= 1e-9d && errP <= 1e-6d, "T305 handoff-corotation",
            string.Format(Inv, "покой 2 ч: |Δv| от скорости места={0:E2} м/с (≤1e-9), |Δr| от точки места={1:E2} м (≤1e-6), |v_места|={2:F2} м/с",
                errV, errP, expectV.Magnitude));
        return 0;
    }

    // Test306: AdvanceTime — только время, без физики: монотонность, точная сумма,
    // состояние корабля не трогается, StepFlying после него продолжает работать.
    static int Test306_AdvanceTimeMonotonic()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        var warp = new WarpController();
        var physics = new SpacecraftPhysics();
        physics.Sources.Add(new CachedGravitySource(sys));
        var propagator = new EventDrivenPropagator(physics);
        propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        propagator.HardHorizonSeconds = sys.BakedEndSeconds();
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(sys, warp, propagator, driver, null, new JointedBreakup(), new DebrisPool(),
            new PartDefinition[0], 1d, 600d);

        planet.EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
        step.DominantBody = planet;
        step.Ship = new Ship(bp + new Vector3d(0d, planet.Radius + 200000d, 0d),
            bv + new Vector3d(3000d, 0d, 0d), 1000d);

        // 1) Монотонность и сумма: разные dt, 100 повторов набора (700 вызовов).
        double[] dts = { 0.016d, 0.001d, 0.5d, 0.25d, 0.033d, 1d, 0.1d };
        double expected = 0d;
        double previous = step.TimeSeconds;
        bool monotonic = true;
        for (int r = 0; r < 100; r++)
        {
            for (int k = 0; k < dts.Length; k++)
            {
                step.AdvanceTime(dts[k]);
                expected += dts[k];
                if (step.TimeSeconds < previous)
                {
                    monotonic = false;
                }

                previous = step.TimeSeconds;
            }
        }

        double tolerance = 1e-9d * Math.Max(1d, expected);
        bool sumOk = Math.Abs(step.TimeSeconds - expected) <= tolerance;

        // 2) Состояние корабля не меняется: AdvanceTime — только время.
        Vector3d p0 = step.Ship.Position;
        Vector3d v0 = step.Ship.Velocity;
        double m0 = step.Ship.Mass;
        step.AdvanceTime(5d);
        bool stateUntouched = (step.Ship.Position - p0).Magnitude == 0d
            && (step.Ship.Velocity - v0).Magnitude == 0d
            && step.Ship.Mass == m0;

        // 3) StepFlying после AdvanceTime работает и продолжает время на свой dt.
        double before = step.TimeSeconds;
        step.StepFlying(0.5f, true);
        double advanced = step.TimeSeconds - before;
        bool flyingOk = advanced > 0d && advanced <= 0.5d + 1e-6d;

        Check(monotonic && sumOk && stateUntouched && flyingOk, "T306 advance-time-monotonic",
            string.Format(Inv,
                "700 вызовов: монотонно={0}; Σdt={1:F6} с, время={2:F6} с, допуск={3:E1} с: {4}; состояние корабля не тронуто: {5}; StepFlying после: Δt={6:F6} с: {7}",
                monotonic, expected, step.TimeSeconds, tolerance, sumOk, stateUntouched, advanced, flyingOk));
        return 0;
    }

    // Test307: разрыв одной ноги T1 — отделяется только нога, корень цел.
    static int Test307_IslandSplitLeg()
    {
        List<PartDefinition> defs = LoadVesselT1();
        ZoneIslands.SplitResult split = ZoneIslands.Split(defs, new[] { 3 });
        bool rootOk = SameSet(split.Root, new[] { 0, 1, 2, 4, 5, 6, 7 });
        bool detachedOk = split.Detached.Count == 1 && SameSet(split.Detached[0], new[] { 3 });

        Check(rootOk && detachedOk, "T307 island-split-leg",
            string.Format(Inv, "разрыв ноги 3: корень={0} (ожидание 7 деталей без ноги), островов={1}, остров0={2} (ожидание [3])",
                split.Root.Length, split.Detached.Count,
                split.Detached.Count > 0 ? string.Join(",", split.Detached[0]) : "нет"));
        return 0;
    }

    // Test308: разрыв ветвей дерева — двигатель, кабина, две ноги.
    static int Test308_IslandSplitTree()
    {
        List<PartDefinition> defs = LoadVesselT1();
        ZoneIslands.SplitResult engine = ZoneIslands.Split(defs, new[] { 2 });
        ZoneIslands.SplitResult cabin = ZoneIslands.Split(defs, new[] { 7 });
        ZoneIslands.SplitResult legs = ZoneIslands.Split(defs, new[] { 3, 4 });

        bool engineOk = engine.Detached.Count == 1 && SameSet(engine.Detached[0], new[] { 2 })
            && engine.Root.Length == 7;
        bool cabinOk = cabin.Detached.Count == 1 && SameSet(cabin.Detached[0], new[] { 7 })
            && cabin.Root.Length == 7;
        bool legsOk = legs.Detached.Count == 2
            && SameSet(legs.Detached[0], new[] { 3 }) && SameSet(legs.Detached[1], new[] { 4 })
            && legs.Root.Length == 6;

        Check(engineOk && cabinOk && legsOk, "T308 island-split-tree",
            string.Format(Inv,
                "двигатель: островов={0} (ожидание 1 [2]), корень={1} (7); кабина: островов={2} (1 [7]), корень={3} (7); две ноги: островов={4} (2 [3],[4]), корень={5} (6)",
                engine.Detached.Count, engine.Root.Length, cabin.Detached.Count, cabin.Root.Length,
                legs.Detached.Count, legs.Root.Length));
        return 0;
    }

    private static bool SameSet(int[] actual, int[] expected)
    {
        if (actual.Length != expected.Length)
        {
            return false;
        }

        var remaining = new List<int>(expected);
        for (int i = 0; i < actual.Length; i++)
        {
            if (!remaining.Remove(actual[i]))
            {
                return false;
            }
        }

        return remaining.Count == 0;
    }
}
