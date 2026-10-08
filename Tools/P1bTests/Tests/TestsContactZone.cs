using System;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;

// Зона контакта (вертикальный срез). Test300–302 — орбитальная безопасность решения о входе:
// на орбите и на входе зона не включается, посадка включает. Test303 — гистерезис.
// Test304–305 — точность и совместное вращение перехода координат.
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
}
