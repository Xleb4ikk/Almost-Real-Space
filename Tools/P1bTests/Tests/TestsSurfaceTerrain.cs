using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test20_Kind()
    {
        // A4.1: every event carries a typed Kind + detector source.
        // No DetectorName parsing: Kind comes directly from the detector.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));

        var propTd = new EventDrivenPropagator(phys);
        propTd.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipTd = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d), bv0 + new Vector3d(-3000d, 0d, 0d), 1000d);
        EventOccurrence? evTd = propTd.Propagate(shipTd, 0d, 30d * 86400d);

        var propAt = new EventDrivenPropagator(phys);
        propAt.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        propAt.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        var shipAt = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d), bv0 + new Vector3d(500d, 2000d, 0d), 1000d);
        EventOccurrence? evExit = propAt.Propagate(shipAt, 0d, 600d);
        EventOccurrence? evEntry = evExit.HasValue ? propAt.Propagate(shipAt, evExit.Value.TimeSeconds, 600d - evExit.Value.TimeSeconds) : null;

        OrbitingBody moon = sys.AllBodies[2];
        moon.EvaluateWorldState(0d, out Vector3d mp0, out Vector3d mv0);
        var propSoi = new EventDrivenPropagator(phys);
        propSoi.TransitionDetectors.Add(new SoiChangeDetector(sys));
        var shipSoi = new Spacecraft(mp0 + new Vector3d(moon.SphereOfInfluenceRadius * 0.9d, 0d, 0d), mv0 + new Vector3d(2000d, 0d, 0d), 1000d);
        EventOccurrence? evSoi = propSoi.Propagate(shipSoi, 0d, 10000d);

        var thrust = new ThrustSource(2000d, 990d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => 1d;
        var physP = new SpacecraftPhysics();
        physP.Sources.Add(new CachedGravitySource(sys));
        physP.Sources.Add(thrust);
        var propP = new EventDrivenPropagator(physP);
        propP.CrossingDetectors.Add(new PropellantDepletionDetector(990d, "main"));
        var shipP = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 8e6d, 0d), bv0 + new Vector3d(5300d, 0d, 0d), 1000d);
        EventOccurrence? evDep = propP.Propagate(shipP, 0d, 120d);

        var propT = new EventDrivenPropagator(phys);
        propT.TimedEvents.Add(new TimedEvent { TimeSeconds = 50d, DeltaMass = -10d, MinimumMassKg = 500d });
        var shipT = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 8e6d, 0d), bv0 + new Vector3d(5300d, 0d, 0d), 1000d);
        EventOccurrence? evTimed = propT.Propagate(shipT, 0d, 120d);

        bool td = evTd.HasValue && evTd.Value.Kind == EventKind.Touchdown && evTd.Value.SourceDetector is AltitudeCrossingDetector;
        bool exit = evExit.HasValue && evExit.Value.Kind == EventKind.AtmosphereExit && evExit.Value.SourceDetector is AltitudeCrossingDetector;
        bool entry = evEntry.HasValue && evEntry.Value.Kind == EventKind.AtmosphereEntry && evEntry.Value.SourceDetector is AltitudeCrossingDetector;
        bool soi = evSoi.HasValue && evSoi.Value.Kind == EventKind.SoiChange && evSoi.Value.SourceDetector is SoiChangeDetector && evSoi.Value.Body != null;
        bool dep = evDep.HasValue && evDep.Value.Kind == EventKind.PropellantDepleted && evDep.Value.SourceDetector is PropellantDepletionDetector;
        bool timed = evTimed.HasValue && evTimed.Value.Kind == EventKind.Timed && evTimed.Value.SourceDetector == null;
        Check(td && exit && entry && soi && dep && timed, "T20 kind",
            string.Format("touchdown={0} exit={1} entry={2} soi={3}({4}) depletion={5} timed={6}",
                td, exit, entry, soi, soi ? evSoi.Value.Body.Name : "нет", dep, timed));
        return 0;
    }

    private static int Test20_SurfaceInverse()
    {
        // A4.2: inverse with the same Rodrigues. 1000 random lat/lon x axis x time,
        // poles, +/-pi seam both ways, time seam (2pi - representation).
        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        star.Radius = 6371000d;
        star.PrimeMeridianOffsetDegrees = 37.5d;

        var rng = new Random(777);
        double worstLat = 0d;
        double worstLon = 0d;
        for (int i = 0; i < 1000; i++)
        {
            double latIn = rng.NextDouble() * 178d - 89d;
            double lonIn = rng.NextDouble() * 360d;
            var axis = new Vector3d(rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d).Normalized;
            if (axis.SqrMagnitude < 0.5d)
            {
                axis = new Vector3d(0.2d, 0.1d, 0.97d).Normalized;
            }

            star.NorthPoleDirection = axis;
            star.RotationPeriodSeconds = i % 3 == 0 ? 0d : (i % 3 == 1 ? 3600d : 86400d);
            double t = rng.NextDouble() * 1e6d;
            star.GetSurfaceState(latIn, lonIn, 5000d, t, out Vector3d wp, out _);
            star.SurfaceLatLonAt(wp, t, out double latOut, out double lonOut);
            double eLat = Math.Abs(latOut - latIn);
            double eLon = WrapLonError(lonOut, lonIn);
            if (eLat > worstLat) worstLat = eLat;
            if (eLon > worstLon) worstLon = eLon;
        }

        bool roundtrip = worstLat < 1e-9d && worstLon < 1e-9d;

        // Poles: lon is deterministically 0.
        star.NorthPoleDirection = new Vector3d(0.3d, 0d, 0.95d);
        star.RotationPeriodSeconds = 86400d;
        star.GetSurfaceState(90d, 123d, 0d, 1000d, out Vector3d wpN, out _);
        star.SurfaceLatLonAt(wpN, 1000d, out double latN, out double lonN);
        star.GetSurfaceState(-90d, 45d, 0d, 2000d, out Vector3d wpS, out _);
        star.SurfaceLatLonAt(wpS, 2000d, out double latS, out double lonS);
        bool poles = Math.Abs(latN - 90d) < 1e-9d && lonN == 0d && Math.Abs(latS + 90d) < 1e-9d && lonS == 0d;

        // Seam both ways: 179.9999 deg and 180.0001 deg adjacent, not via 360 deg.
        star.NorthPoleDirection = new Vector3d(0d, 0d, 1d);
        star.RotationPeriodSeconds = 0d;
        star.GetSurfaceState(0d, 179.9999d, 0d, 0d, out Vector3d wpA, out _);
        star.SurfaceLatLonAt(wpA, 0d, out _, out double lonA);
        star.GetSurfaceState(0d, 180.0001d, 0d, 0d, out Vector3d wpB, out _);
        star.SurfaceLatLonAt(wpB, 0d, out _, out double lonB);
        star.GetSurfaceState(0d, -0.0001d, 0d, 0d, out Vector3d wpC, out _);
        star.SurfaceLatLonAt(wpC, 0d, out _, out double lonC);
        bool seam = WrapLonError(lonA, 179.9999d) < 1e-9d
            && WrapLonError(lonB, 180.0001d) < 1e-9d
            && Math.Abs(lonA - lonB) < 0.001d
            && WrapLonError(lonC, 359.9999d) < 1e-9d
            && lonA >= 0d && lonA < 360d && lonB >= 0d && lonB < 360d && lonC >= 0d && lonC < 360d;

        // Time seam: fixed world point, the body rotates underneath.
        // Raw longitude sawtooths through 0/360, unwrapped is a line with slope -omega.
        star.RotationPeriodSeconds = 3600d;
        var fixedPoint = new Vector3d(star.Radius + 1000d, 0d, 0d);
        double prevUnwrapped = 0d;
        bool first = true;
        bool linear = true;
        double maxStep = 0d;
        for (int i = 0; i <= 40; i++)
        {
            double t = 3960d * i / 40d;
            star.SurfaceLatLonAt(fixedPoint, t, out _, out double lonT);
            double unwrapped = lonT;
            if (!first)
            {
                while (unwrapped - prevUnwrapped > 180d) unwrapped -= 360d;
                while (unwrapped - prevUnwrapped < -180d) unwrapped += 360d;
                double step = Math.Abs(unwrapped - prevUnwrapped);
                if (step > maxStep) maxStep = step;
                double expect = 360d * (3960d / 40d) / 3600d;
                if (Math.Abs(step - expect) > 1e-6d) linear = false;
            }

            prevUnwrapped = unwrapped;
            first = false;
        }

        // Retrograde axis flip: roundtrip still holds.
        star.NorthPoleDirection = new Vector3d(0d, 0d, -1d);
        star.GetSurfaceState(30d, 200d, 100d, 500d, out Vector3d wpF, out _);
        star.SurfaceLatLonAt(wpF, 500d, out double latF, out double lonF);
        bool flip = Math.Abs(latF - 30d) < 1e-9d && WrapLonError(lonF, 200d) < 1e-9d;

        Check(roundtrip && poles && seam && linear && flip, "T20 surface-inverse",
            string.Format("roundtrip×1000: maxΔlat={0:E2}° maxΔlon={1:E2}° (<1e-9); полюса lon=0: {2}; шов: {3:F4}/{4:F4}/neg→{5:F4}; шов во времени линеен (шаг 99°±1e-6): {6}; флип: {7}",
                worstLat, worstLon, poles, lonA, lonB, lonC, linear, flip));
        return 0;
    }

    private static int Test20_Reaction()
    {
        // A4.3: touchdown by v_n, breakup invariants, clamp == dry, focus without recompute.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var breakup = new SingleThresholdBreakup();

        // Soft: 1 m above the surface, (5,-1,0) m/s - v_n~4.5 < 5, v_t=5 survives.
        var physS = new SpacecraftPhysics();
        physS.Sources.Add(new CachedGravitySource(sys));
        var propS = new EventDrivenPropagator(physS);
        propS.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipS = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 1d, 0d), bv0 + new Vector3d(5d, -1d, 0d), 1000d);
        EventOccurrence? evS = propS.Propagate(shipS, 0d, 60d);
        TouchdownOutcome soft = EventReactions.ApplyTouchdown(shipS, evS.Value, planet, breakup, 5d);
        planet.EvaluateWorldState(evS.Value.TimeSeconds, out Vector3d bpS, out _);
        double softAlt = (shipS.Position - bpS).Magnitude - planet.Radius;
        planet.GetSurfaceState(soft.Landed.LatitudeDegrees, soft.Landed.LongitudeDegrees, 0d, evS.Value.TimeSeconds, out _, out Vector3d surfVS);
        Vector3d nS = (shipS.Position - bpS).Normalized;
        double postVn = Vector3d.Dot(shipS.Velocity - surfVS, nS);
        double postVt = (shipS.Velocity - surfVS - (nS * postVn)).Magnitude;

        // Hard: 50 km, -2000 m/s radial - breakup, 10 specs.
        var physH = new SpacecraftPhysics();
        physH.Sources.Add(new CachedGravitySource(sys));
        var propH = new EventDrivenPropagator(physH);
        propH.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipH = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d), bv0 + new Vector3d(0d, -2000d, 0d), 1000d);
        EventOccurrence? evH = propH.Propagate(shipH, 0d, 600d);
        Vector3d shipHVel = shipH.Velocity;
        double shipHMass = shipH.Mass;
        TouchdownOutcome hard = EventReactions.ApplyTouchdown(shipH, evH.Value, planet, breakup, 5d);
        planet.EvaluateWorldState(evH.Value.TimeSeconds, out Vector3d bpH, out _);
        planet.SurfaceLatLonAt(shipH.Position, evH.Value.TimeSeconds, out double latH, out double lonH);
        planet.GetSurfaceState(latH, lonH, 0d, evH.Value.TimeSeconds, out _, out Vector3d surfVH);
        Vector3d vImpactH = shipHVel - surfVH;
        double sumM = 0d;
        Vector3d sumMF = Vector3d.Zero;
        Vector3d sumMV = Vector3d.Zero;
        double maxSpawnErr = 0d;
        for (int i = 0; i < hard.Specs.Count; i++)
        {
            sumM += hard.Specs[i].Mass;
            sumMF += hard.Specs[i].FragmentVelocity * hard.Specs[i].Mass;
            sumMV += hard.Specs[i].Velocity * hard.Specs[i].Mass;
            double alt = (hard.Specs[i].Position - bpH).Magnitude - planet.Radius;
            double e = Math.Abs(alt - 5d);
            if (e > maxSpawnErr) maxSpawnErr = e;
        }

        Vector3d expectMF = vImpactH * shipHMass;
        Vector3d expectMV = shipHVel * shipHMass;
        double momErr = (sumMF - expectMF).Magnitude / Math.Max(1d, expectMF.Magnitude);
        double worldMomErr = (sumMV - expectMV).Magnitude / Math.Max(1d, expectMV.Magnitude);
        double massErr = Math.Abs(sumM - shipHMass) / shipHMass;

        // Clamp: mass == dry exactly.
        var thrust = new ThrustSource(2000d, 900d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => 1d;
        var physD = new SpacecraftPhysics();
        physD.Sources.Add(new CachedGravitySource(sys));
        physD.Sources.Add(thrust);
        var propD = new EventDrivenPropagator(physD);
        propD.CrossingDetectors.Add(new PropellantDepletionDetector(900d, "main"));
        var shipD = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 8e6d, 0d), bv0 + new Vector3d(5300d, 0d, 0d), 1000d);
        EventOccurrence? evD = propD.Propagate(shipD, 0d, 600d);
        double clamped = EventReactions.ApplyDepletion(shipD, evD.Value);

        // Focus: body from the event, no recompute.
        OrbitingBody moon = sys.AllBodies[2];
        moon.EvaluateWorldState(0d, out Vector3d mp0, out Vector3d mv0);
        var propF = new EventDrivenPropagator(physS);
        propF.TransitionDetectors.Add(new SoiChangeDetector(sys));
        var shipF = new Spacecraft(mp0 + new Vector3d(moon.SphereOfInfluenceRadius * 0.9d, 0d, 0d), mv0 + new Vector3d(2000d, 0d, 0d), 1000d);
        EventOccurrence? evF = propF.Propagate(shipF, 0d, 10000d);
        FocusResult focus = EventReactions.ApplySoi(evF.Value);

        bool softOk = evS.HasValue && soft.IsLanding
            && Math.Abs(soft.Landed.NormalSpeed) <= planet.CrashToleranceMps
            && Math.Abs(softAlt) < 0.5d && Math.Abs(postVn) < 1e-6d
            && Math.Abs(postVt - soft.Landed.TangentialSpeed) < 1e-6d;
        bool hardOk = evH.HasValue && !hard.IsLanding && hard.Specs.Count == 10
            && massErr < 1e-12d && momErr < 1e-9d && worldMomErr < 1e-9d && maxSpawnErr < 0.5d;
        bool clampOk = evD.HasValue && clamped == 900d;
        bool focusOk = evF.HasValue && ReferenceEquals(focus.NewBody, evF.Value.Body);
        Check(softOk && hardOk && clampOk && focusOk, "T20 reaction",
            string.Format("soft: landed={0} vn={1:F2}м/с(≤5) alt={2:E2}м postVn={3:E2} vtСохранена={4}; hard: N={5} ΣmΔ={6:E2} ΣmfΔ={7:E2} ΣmvΔ={8:E2} spawnΔ={9:E2}м; clamp==900: {10}; focus is Body: {11}",
                soft.IsLanding, soft.Landed.NormalSpeed, softAlt, postVn, Math.Abs(postVt - soft.Landed.TangentialSpeed) < 1e-6d,
                hard.Specs != null ? hard.Specs.Count : -1, massErr, momErr, worldMomErr, maxSpawnErr, clampOk, focusOk));
        return 0;
    }

    private sealed class TiltedTerrain : ITerrainModel
    {
        private readonly Vector3d normal;

        public TiltedTerrain(Vector3d normal)
        {
            this.normal = normal.Normalized;
        }

        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetSeaLevelMeters()
        {
            return double.NegativeInfinity;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return normal;
        }
    }

    private static int Test20_SurfaceMotion()
    {
        // A4.4: standing bit-for-bit, rotation following, stick 30 deg / slip 60 deg,
        // slip->stick after t_stop, liftoff under outward-g. Sphere: a_t==0 -> always stick.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        SurfaceMotion.GravityProvider realG = (pos, t) => sys.EvaluateShipAcceleration(pos, t);
        var sphere = new SphericalTerrain();

        // Standing: the body orbits, so absolute bit-exact rest is impossible -
        // the relative offset is checked (stays at R +/-mm).
        Vector3d standP = bp0 + new Vector3d(0d, planet.Radius, 0d);
        Vector3d standV = bv0;
        Vector3d p = standP;
        Vector3d v = standV;
        bool standRegime = true;
        double standDrift = 0d;
        for (int i = 0; i < 10; i++)
        {
            SurfaceMotionResult r = SurfaceMotion.Step(p, v, planet, sphere, realG, 0.5d * i, 0.5d);
            p = r.Position;
            v = r.Velocity;
            standRegime = standRegime && r.Regime == VesselRegime.Landed && r.IsStuck;
            planet.EvaluateWorldState(0.5d * (i + 1), out Vector3d bpi, out _);
            double drift = ((p - bpi) - new Vector3d(0d, planet.Radius, 0d)).Magnitude;
            if (drift > standDrift) standDrift = drift;
        }

        // Velocity must equal the surface velocity at the end (the body moved
        // along its orbit - no absolute rest, only co-motion).
        planet.EvaluateWorldState(5d, out _, out Vector3d bv5);
        bool standstill = standDrift < 1e-3d && standRegime && (v - bv5).SqrMagnitude == 0d;

        // Following: period 86400 (centrifugal 0.034 << 9.8 - contact holds;
        // period 3600 at Earth radius physically breaks everything into Liftoff).
        planet.RotationPeriodSeconds = 86400d;
        planet.SurfaceLatLonAt(standP, 0d, out double lat0, out double lon0);
        planet.GetSurfaceState(lat0, lon0, 0d, 0d, out _, out Vector3d surfV0);
        Vector3d pr = standP;
        Vector3d vr = surfV0;
        for (int i = 0; i < 7200; i++)
        {
            SurfaceMotionResult r = SurfaceMotion.Step(pr, vr, planet, sphere, realG, 0.5d * i, 0.5d);
            pr = r.Position;
            vr = r.Velocity;
        }

        planet.GetSurfaceState(lat0, lon0, 0d, 3600d, out Vector3d expectP, out _);
        double followErr = (pr - expectP).Magnitude;

        OrbitingBody star = sys.Root;
        star.Radius = 6371000d;
        SurfaceMotion.GravityProvider downG = (pos, t) => new Vector3d(0d, -9.8d, 0d);
        var tilt30 = new TiltedTerrain(new Vector3d(0.5d, 0.8660254037844386d, 0d));
        var tilt60 = new TiltedTerrain(new Vector3d(0.8660254037844386d, 0.5d, 0d));
        Vector3d slopeP = new Vector3d(0d, star.Radius, 0d);
        SurfaceMotionResult stuck = SurfaceMotion.Step(slopeP, Vector3d.Zero, star, tilt30, downG, 0d, 0.1d);
        for (int i = 1; i < 10; i++)
        {
            stuck = SurfaceMotion.Step(stuck.Position, stuck.Velocity, star, tilt30, downG, 0.1d * i, 0.1d);
        }

        bool stickOk = stuck.Regime == VesselRegime.Landed && stuck.IsStuck && stuck.Velocity.Magnitude < 1e-9d;

        Vector3d slideV = Vector3d.Zero;
        Vector3d slideP = slopeP;
        for (int i = 0; i < 10; i++)
        {
            SurfaceMotionResult r = SurfaceMotion.Step(slideP, slideV, star, tilt60, downG, 0.1d * i, 0.1d);
            slideP = r.Position;
            slideV = r.Velocity;
        }

        double slideSpeed = slideV.Magnitude;
        bool slipOk = Math.Abs(slideSpeed - 6.037d) < 0.05d;

        Vector3d stopP = slopeP;
        Vector3d stopV = new Vector3d(50d, 0d, 0d);
        SurfaceMotionResult stopped = SurfaceMotion.Step(stopP, stopV, star, sphere, downG, 0d, 15d);
        double stopDist = (stopped.Position - slopeP).Magnitude;
        bool stopOk = stopped.Regime == VesselRegime.Landed && stopped.IsStuck
            && stopped.Velocity.Magnitude < 1e-9d && Math.Abs(stopDist - 255.1d) < 2d;

        SurfaceMotion.GravityProvider upG = (pos, t) => new Vector3d(0d, 9.8d, 0d);
        SurfaceMotionResult lift = SurfaceMotion.Step(slopeP, Vector3d.Zero, star, sphere, upG, 0d, 0.5d);
        bool liftOk = lift.Regime == VesselRegime.Flying
            && lift.Position.X == slopeP.X && lift.Position.Y == slopeP.Y && lift.Position.Z == slopeP.Z;

        Check(standstill && followErr < 1e-3d && stickOk && slipOk && stopOk && liftOk, "T20 surface-motion",
            string.Format("стоянка относит.: {0}; следование 3600с Δ={1:E2}м; стик 30° v={2:E2}: {3}; слип 60° v={4:F3}м/с (≈6.037): {5}; стоп с 50м/с: v={6:E2} путь={7:F1}м (≈255): {8}; отрыв без сдвига: {9}",
                standstill, followErr, stuck.Velocity.Magnitude, stickOk, slideSpeed, slipOk,
                stopped.Velocity.Magnitude, stopDist, stopOk, liftOk));
        return 0;
    }

    private static int Test29_DescentChain()
    {
        // V10: end-to-end chain - entry, parachute braking (A=1500),
        // soft touchdown, reaction, a day of standing with rotation following.
        // Each phase uses its own tolerance from the existing tests, not one shared.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        planet.RotationPeriodSeconds = 86400d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var chute = new DragSource(planet);
        chute.DragCoefficient = 1d;
        chute.ReferenceAreaM2 = 1500d;
        phys.Sources.Add(chute);
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var ship = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 120000d, 0d), bv0 + new Vector3d(1500d, -300d, 0d), 1000d);

        var names = new List<string>();
        double t = 0d;
        EventOccurrence touchEvent = default;
        bool hasTouch = false;
        while (t < 7200d - 1e-9d && names.Count < 2)
        {
            EventOccurrence? ev = prop.Propagate(ship, t, 7200d - t);
            if (!ev.HasValue)
            {
                break;
            }

            names.Add(ev.Value.DetectorName);
            t = ev.Value.TimeSeconds;
            if (ev.Value.Kind == EventKind.Touchdown)
            {
                touchEvent = ev.Value;
                hasTouch = true;
            }
        }

        bool sequence = names.Count == 2
            && names[0].StartsWith("AtmosphereEntry") && names[1].StartsWith("Touchdown");
        TouchdownOutcome outcome = EventReactions.ApplyTouchdown(ship, touchEvent, planet, new SingleThresholdBreakup(), 5d);
        planet.GetSurfaceState(outcome.Landed.LatitudeDegrees, outcome.Landed.LongitudeDegrees, 0d, t, out _, out _);

        SurfaceMotion.GravityProvider realG = (pos, tt) => sys.EvaluateShipAcceleration(pos, tt);
        var sphere = new SphericalTerrain();
        Vector3d p = ship.Position;
        Vector3d v = ship.Velocity;
        bool regimeOk = true;
        for (int i = 0; i < 720; i++)
        {
            SurfaceMotionResult r = SurfaceMotion.Step(p, v, planet, sphere, realG, t + (120d * i), 120d);
            p = r.Position;
            v = r.Velocity;
            regimeOk = regimeOk && r.Regime == VesselRegime.Landed;
        }

        planet.GetSurfaceState(outcome.Landed.LatitudeDegrees, outcome.Landed.LongitudeDegrees, 0d, t + 86400d, out Vector3d expectP, out _);
        double dayDrift = (p - expectP).Magnitude;

        Check(sequence && hasTouch && outcome.IsLanding && regimeOk && dayDrift < 0.01d, "T29 descent-chain",
            string.Format("цепочка [Entry,Touchdown]: {0}; мягкое vn={1:F2}м/с(≤5): {2}; сутки landed, дрейф={3:E2}м (<0.01): {4}",
                sequence, outcome.Landed.NormalSpeed, outcome.IsLanding, dayDrift, regimeOk && dayDrift < 0.01d));
        return 0;
    }

    private static int Test29_TerminalVelocity()
    {
        // V11: terminal velocity against the analytic v=sqrt(2mg/rho*Cd*A) on a flat
        // profile (H=1e6 m - density nearly const over the measured range).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 2000000d,
            SeaLevelDensityKgPerCubicMeter = 0.1d,
            ScaleHeightMeters = 1000000d
        };
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        double mu = planet.ResolveStandardGravitationalParameter();
        double g0 = mu / (planet.Radius * planet.Radius);
        double analytic = Math.Sqrt((2d * 1000d * g0) / (0.1d * 1d * 10d));
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var chute = new DragSource(planet);
        chute.DragCoefficient = 1d;
        chute.ReferenceAreaM2 = 10d;
        phys.Sources.Add(chute);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 60000d, 0d), bv0 + new Vector3d(0d, -50d, 0d), 1000d);

        double sumDown = 0d;
        int count = 0;
        double t = 0d;
        while (t < 1200d - 1e-9d)
        {
            phys.Step(ship, t, 1d);
            t += 1d;
            planet.EvaluateWorldState(t, out Vector3d bp, out Vector3d bv);
            Vector3d rel = ship.Position - bp;
            double alt = rel.Magnitude - planet.Radius;
            if (alt >= 20000d && alt <= 30000d)
            {
                Vector3d radial = rel / rel.Magnitude;
                sumDown += -Vector3d.Dot(ship.Velocity - bv, radial);
                count++;
            }

            if (alt <= 0d)
            {
                break;
            }
        }

        // The measured 0.9% has a clear make-up: g/rho gradients over the window
        // (~0.5%) plus residual transient (still catching up to terminal from above).
        // Systematic, not noise: the 1.5% bound keeps margin but catches model regressions.
        double meanDown = count > 0 ? sumDown / count : double.NaN;
        double relErr = Math.Abs(meanDown / analytic - 1d);
        Check(count > 50 && relErr < 0.015d, "T29 terminal-velocity",
            string.Format("сэмплов в окне 20–30км: {0}; средняя {1:F2}м/с vs аналитика {2:F2} (Δ={3:E2}, допуск 1.5%)",
                count, meanDown, analytic, relErr));
        return 0;
    }


    private sealed class SphereOnlyTerrain : ITerrainModel
    {
        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians) => 0d;
        public double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians) => 0d;
        public double GetSeaLevelMeters() => double.NegativeInfinity;
        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds) => relativePosition.Normalized;
    }

    private static int Test70_TryLiftoffSpin()
    {
        // Phase 1.1: TryLiftoff with omega != 0.
        // 1) Sync with T52: CO-ROTATING ship (v_t=0), zero thrust:
        // omega^2*R=10>g -> Flying (centrifugal lifts off), omega^2*R=5<g -> Landed.
        OrbitingBody fastBody = SpinPlanet(omega2R: 10d, g0: 9.8d);
        OrbitingBody slowBody = SpinPlanet(omega2R: 5d, g0: 9.8d);
        double radius = 1e6d;
        double omega = fastBody.SpinAngularSpeed;
        Vector3d gIn = new Vector3d(-9.8d, 0d, 0d);
        Vector3d surfaceV = new Vector3d(0d, omega * radius, 0d);

        var coRot = new Spacecraft(new Vector3d(radius, 0d, 0d), surfaceV, 1000d);
        VesselRegime fast = EventReactions.TryLiftoff(coRot, fastBody, Vector3d.Zero, gIn, 0d, out _);

        var coRotSlow = new Spacecraft(new Vector3d(radius, 0d, 0d), new Vector3d(0d, slowBody.SpinAngularSpeed * radius, 0d), 1000d);
        VesselRegime slow = EventReactions.TryLiftoff(coRotSlow, slowBody, Vector3d.Zero, gIn, 0d, out _);
        bool t52Sync = fast == VesselRegime.Flying && slow == VesselRegime.Landed;

        // 2) Co-rotating ship with thrust: velocity is not doubled -
        // v_new == surface velocity to within a hair (tangential relative is zero).
        var landedShip = new Spacecraft(new Vector3d(radius, 0d, 0d), surfaceV, 1000d);
        VesselRegime lift = EventReactions.TryLiftoff(landedShip, fastBody,
            new Vector3d(16d, 0d, 0d), gIn, 0d, out _);
        bool noDoubleCount = lift == VesselRegime.Flying
            && (landedShip.Velocity - surfaceV).Magnitude < 1e-9d;

        // 3) Ship with an unrelated relative velocity (v=0 spawn,
        // v_t_rel = -omega*R): liftoff needs thrust > g+omega^2*R (Coriolis presses down);
        // after liftoff the relative tangential is PRESERVED - inertial
        // velocity is zero, not a doubled surface velocity.
        var spawnShip = new Spacecraft(new Vector3d(radius, 0d, 0d), Vector3d.Zero, 1000d);
        VesselRegime spawnLift = EventReactions.TryLiftoff(spawnShip, fastBody,
            new Vector3d(30d, 0d, 0d), gIn, 0d, out _);
        bool relativeKept = spawnLift == VesselRegime.Flying && spawnShip.Velocity.Magnitude < 1e-6d;

        Check(t52Sync && noDoubleCount && relativeKept, "T70 tryliftoff-spin",
            string.Format("T52 через TryLiftoff: ω²R=10 → {0}, ω²R=5 → {1}; со-вращение не удвоено (|Δv|={2:E2}): {3}; v=0-спавн: тяга 30>g+ω²R → {4}, v инерциально сохранена (|v|={5:E2}): {6}",
                fast, slow, (landedShip.Velocity - surfaceV).Magnitude, noDoubleCount,
                spawnLift, spawnShip.Velocity.Magnitude, relativeKept));
        return 0;
    }

    private static OrbitingBody MakeTerrainBody()
    {
        return new OrbitingBody
        {
            Name = "Terra",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            RotationPeriodSeconds = 3600d
        };
    }

    private static HeightfieldTerrain MakeTerrain(int seed)
    {
        return new HeightfieldTerrain
        {
            Seed = seed,
            AmplitudeMeters = 2000d,
            BaseFrequency = 3d,
            Octaves = 5,
            SeaLevelMeters = -500d
        };
    }

    private static int Test77_TerrainHeightfield()
    {
        OrbitingBody body = MakeTerrainBody();
        HeightfieldTerrain terrainA = MakeTerrain(42);
        HeightfieldTerrain terrainB = MakeTerrain(42);
        body.Terrain = terrainA;

        // Determinism: same seed - bit-for-bit identical heights.
        bool deterministic = true;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                if (terrainA.GetHeightMeters(body, lat, lon) != terrainB.GetHeightMeters(body, lat, lon))
                {
                    deterministic = false;
                }
            }
        }

        // Sea clamp + amplitude range.
        double worst = 0d;
        bool seaClamp = true;
        for (int i = 0; i <= 40; i++)
        {
            for (int j = 0; j <= 40; j++)
            {
                double lat = (-85d + (170d * i / 40d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 40d)) * (Math.PI / 180d);
                double h = terrainA.GetHeightMeters(body, lat, lon);
                if (h < terrainA.SeaLevelMeters)
                {
                    seaClamp = false;
                }

                worst = Math.Max(worst, Math.Abs(h));
            }
        }

        bool amplitudeOk = worst <= terrainA.AmplitudeMeters + 1e-9d;

        // Normal is consistent with the height function: an independent finite
        // difference (coarser than the normal's eps, 1e-3 rad) via GetSurfaceState agrees in direction.
        body.EvaluateWorldState(0d, out Vector3d bodyP, out _);
        double lat0 = 0.37d;
        double lon0 = 1.11d;
        double h0 = terrainA.GetHeightMeters(body, lat0, lon0);
        double eps = 1e-3d;
        double hN = terrainA.GetHeightMeters(body, Math.Min(lat0 + eps, 1.5707963267948966d - 1e-9d), lon0);
        double hE = terrainA.GetHeightMeters(body, lat0, lon0 + eps);
        body.GetSurfaceState(lat0 * (180d / Math.PI), lon0 * (180d / Math.PI), h0, 0d, out Vector3d q0, out _);
        body.GetSurfaceState((lat0 + eps) * (180d / Math.PI), lon0 * (180d / Math.PI), hN, 0d, out Vector3d qN, out _);
        body.GetSurfaceState(lat0 * (180d / Math.PI), (lon0 + eps) * (180d / Math.PI), hE, 0d, out Vector3d qE, out _);
        Vector3d normalManual = Vector3d.Cross(qN - q0, qE - q0).Normalized;
        if (Vector3d.Dot(normalManual, q0 - bodyP) < 0d)
        {
            normalManual = -normalManual;
        }

        Vector3d normalT = terrainA.GetOutwardNormal(body, q0 - bodyP, 0d).Normalized;
        double normalAngle = Math.Acos(Math.Min(1d, Math.Max(-1d, Vector3d.Dot(normalManual, normalT))));
        bool normalConsistent = normalAngle < 0.05d;

        // Spin: the normal of one body-fixed point (lat0/lon0) at t = quarter
        // period equals the Rodrigues rotation of the t=0 normal about the spin
        // axis. GetSurfaceState gives the point's position at each time and also
        // accounts for RotationAngleAtTime(t), so WorldLatLonOf yields the same lat/lon.
        double tQuarter = 900d;
        body.EvaluateWorldState(0d, out Vector3d bodyP0, out _);
        body.EvaluateWorldState(tQuarter, out Vector3d bodyPQ, out _);
        body.GetSurfaceState(lat0 * (180d / Math.PI), lon0 * (180d / Math.PI), h0, 0d, out Vector3d pAt0, out _);
        body.GetSurfaceState(lat0 * (180d / Math.PI), lon0 * (180d / Math.PI), h0, tQuarter, out Vector3d pAtQ, out _);
        Vector3d normal0 = terrainA.GetOutwardNormal(body, pAt0 - bodyP0, 0d).Normalized;
        Vector3d normalQ = terrainA.GetOutwardNormal(body, pAtQ - bodyPQ, tQuarter).Normalized;
        double spinAngle = body.SpinAngularSpeed * (tQuarter - 0d);
        Vector3d axis = body.SpinAxis;
        double cosA = Math.Cos(spinAngle);
        double sinA = Math.Sin(spinAngle);
        Vector3d rotated = (normal0 * cosA) + (Vector3d.Cross(axis, normal0) * sinA)
            + (axis * (Vector3d.Dot(axis, normal0) * (1d - cosA)));
        double spinError = (rotated.Normalized - normalQ).Magnitude;
        bool spinOk = spinError < 1e-9d;

        // Terrain-contact detector: above the peak g<0 earlier than the sphere, higher up g>0.
        var detector = AltitudeCrossingDetector.ForTouchdown(body);
        double peakHeight = double.NegativeInfinity;
        double peakLat = 0d;
        double peakLon = 0d;
        for (int i = 0; i <= 200; i++)
        {
            for (int j = 0; j <= 200; j++)
            {
                double lat = (-85d + (170d * i / 200d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 200d)) * (Math.PI / 180d);
                double h = terrainA.GetHeightMeters(body, lat, lon);
                if (h > peakHeight)
                {
                    peakHeight = h;
                    peakLat = lat;
                    peakLon = lon;
                }
            }
        }

        body.EvaluateWorldState(0d, out Vector3d bodyPos, out _);
        body.GetSurfaceState(peakLat * (180d / Math.PI), peakLon * (180d / Math.PI), peakHeight, 0d, out Vector3d peakPos, out _);
        Vector3d radial = (peakPos - bodyPos).Normalized;

        // The detector's g is HEIGHT above terrain: -50 m (below the surface) -> a
        // crossing, +300 m (above) -> not yet. The event time is the exact surface.
        var stateLow = new SpacecraftIntegrationState { Position = peakPos - (radial * 50d), Velocity = Vector3d.Zero, Mass = 1000d };
        var stateHigh = new SpacecraftIntegrationState { Position = peakPos + (radial * 300d), Velocity = Vector3d.Zero, Mass = 1000d };
        bool detectorLow = detector.Evaluate(stateLow, 0d) < 0d;
        bool detectorHigh = detector.Evaluate(stateHigh, 0d) > 0d;
        bool sphereWouldMiss = (stateLow.Position - bodyPos).Magnitude > body.Radius;

        Check(deterministic, "T77 terrain-heightfield", "детерминизм seed=42");
        Check(seaClamp, "T77 terrain-heightfield", "море клампится (уровень −500 м)");
        Check(amplitudeOk, "T77 terrain-heightfield", "амплитуда |h| ≤ 2000 м (max " + worst.ToString("F1") + ")");
        Check(normalConsistent, "T77 terrain-heightfield", "нормаль↔высота, угол " + normalAngle.ToString("E3") + " рад < 0.05");
        Check(spinOk, "T77 terrain-heightfield", "спин нормали, Δ=" + spinError.ToString("E3") + " < 1e-9");
        Check(detectorLow && detectorHigh, "T77 terrain-heightfield", "детектор над пиком: g<0 на +50 м, g>0 на +300 м");
        Check(sphereWouldMiss, "T77 terrain-heightfield", "сфера бы пропустила пик (корабль выше R)");
        return 0;
    }

    private static HeightfieldTerrain MakeTerrainAdvanced(int seed)
    {
        return new HeightfieldTerrain
        {
            Seed = seed,
            AmplitudeMeters = 2000d,
            BaseFrequency = 3d,
            Octaves = 5,
            SeaLevelMeters = -500d,
            Lacunarity = 2.3d,
            Gain = 0.45d,
            ContinentFrequency = 1d,
            ContinentOctaves = 3,
            ContinentThreshold = 0d,
            ContinentSharpness = 0.25d,
            ContinentDepth = 0.75d,
            RidgedMix = 0.6d,
            WarpStrength = 0.15d,
            WarpFrequency = 1d,
            WarpOctaves = 2,
            WarpSeedOffset = 7
        };
    }

    private static int Test82_TerrainAdvanced()
    {
        OrbitingBody body = MakeTerrainBody();
        HeightfieldTerrain legacy = MakeTerrain(42);
        body.Terrain = legacy;

        // Zero new parameters = bit-for-bit legacy SampleFbm branch.
        HeightfieldTerrain legacyB = MakeTerrain(42);
        bool legacyExact = true;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                if (legacy.GetHeightMeters(body, lat, lon) != legacyB.GetHeightMeters(body, lat, lon))
                {
                    legacyExact = false;
                }
            }
        }

        // WarpStrength = 0 ignores WarpSeedOffset (early exit into legacy).
        HeightfieldTerrain warpOff = MakeTerrain(42);
        warpOff.WarpSeedOffset = 7;
        warpOff.WarpFrequency = 5d;
        warpOff.WarpOctaves = 4;
        bool warpOffExact = true;
        for (int i = 0; i <= 10; i++)
        {
            for (int j = 0; j <= 10; j++)
            {
                double lat = (-80d + (160d * i / 10d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 10d)) * (Math.PI / 180d);
                if (warpOff.GetHeightMeters(body, lat, lon) != legacy.GetHeightMeters(body, lat, lon))
                {
                    warpOffExact = false;
                }
            }
        }

        // Each feature on its own changes the output (parameter wiring is live).
        HeightfieldTerrain ridged = MakeTerrain(42);
        ridged.RidgedMix = 0.6d;
        HeightfieldTerrain warped = MakeTerrain(42);
        warped.WarpStrength = 0.15d;
        HeightfieldTerrain cont = MakeTerrain(42);
        cont.ContinentFrequency = 1d;
        HeightfieldTerrain lac = MakeTerrain(42);
        lac.Lacunarity = 2.3d;
        HeightfieldTerrain gain = MakeTerrain(42);
        gain.Gain = 0.45d;
        bool ridgedDiffers = false;
        bool warpedDiffers = false;
        bool contDiffers = false;
        bool lacDiffers = false;
        bool gainDiffers = false;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                double h0 = legacy.GetHeightMeters(body, lat, lon);
                if (ridged.GetHeightMeters(body, lat, lon) != h0)
                {
                    ridgedDiffers = true;
                }

                if (warped.GetHeightMeters(body, lat, lon) != h0)
                {
                    warpedDiffers = true;
                }

                if (cont.GetHeightMeters(body, lat, lon) != h0)
                {
                    contDiffers = true;
                }

                if (lac.GetHeightMeters(body, lat, lon) != h0)
                {
                    lacDiffers = true;
                }

                if (gain.GetHeightMeters(body, lat, lon) != h0)
                {
                    gainDiffers = true;
                }
            }
        }

        // The warp stream is independent: a different WarpSeedOffset gives different terrain.
        HeightfieldTerrain warpedB = MakeTerrain(42);
        warpedB.WarpStrength = 0.15d;
        warpedB.WarpSeedOffset = 8;
        bool warpSeedDiffers = false;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                if (warped.GetHeightMeters(body, lat, lon) != warpedB.GetHeightMeters(body, lat, lon))
                {
                    warpSeedDiffers = true;
                }
            }
        }

        // Full advanced: determinism, sea clamp, amplitude bound.
        HeightfieldTerrain advA = MakeTerrainAdvanced(42);
        HeightfieldTerrain advB = MakeTerrainAdvanced(42);
        body.Terrain = advA;
        bool advDeterministic = true;
        bool advSeaClamp = true;
        double advWorst = 0d;
        for (int i = 0; i <= 40; i++)
        {
            for (int j = 0; j <= 40; j++)
            {
                double lat = (-85d + (170d * i / 40d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 40d)) * (Math.PI / 180d);
                double ha = advA.GetHeightMeters(body, lat, lon);
                if (ha != advB.GetHeightMeters(body, lat, lon))
                {
                    advDeterministic = false;
                }

                if (ha < advA.SeaLevelMeters)
                {
                    advSeaClamp = false;
                }

                advWorst = Math.Max(advWorst, Math.Abs(ha));
            }
        }

        bool advBoundOk = advWorst <= advA.AmplitudeMeters * (1d + advA.ContinentDepth) + 1e-6d;

        // Normal parity at the crest (the advanced-terrain extremum - the
        // hardest case, where the double/float divergence is maximal due to warp).
        double peakH = double.NegativeInfinity;
        double peakLat = 0d;
        double peakLon = 0d;
        for (int i = 0; i <= 100; i++)
        {
            for (int j = 0; j <= 100; j++)
            {
                double lat = (-85d + (170d * i / 100d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 100d)) * (Math.PI / 180d);
                double h = advA.GetHeightMeters(body, lat, lon);
                if (h > peakH)
                {
                    peakH = h;
                    peakLat = lat;
                    peakLon = lon;
                }
            }
        }

        body.EvaluateWorldState(0d, out Vector3d advBodyP, out _);
        double advEps = 1e-3d;
        double advH0 = advA.GetHeightMeters(body, peakLat, peakLon);
        double advHN = advA.GetHeightMeters(body, Math.Min(peakLat + advEps, 1.5707963267948966d - 1e-9d), peakLon);
        double advHE = advA.GetHeightMeters(body, peakLat, peakLon + advEps);
        body.GetSurfaceState(peakLat * (180d / Math.PI), peakLon * (180d / Math.PI), advH0, 0d, out Vector3d advQ0, out _);
        body.GetSurfaceState((peakLat + advEps) * (180d / Math.PI), peakLon * (180d / Math.PI), advHN, 0d, out Vector3d advQN, out _);
        body.GetSurfaceState(peakLat * (180d / Math.PI), (peakLon + advEps) * (180d / Math.PI), advHE, 0d, out Vector3d advQE, out _);
        Vector3d advManual = Vector3d.Cross(advQN - advQ0, advQE - advQ0).Normalized;
        if (Vector3d.Dot(advManual, advQ0 - advBodyP) < 0d)
        {
            advManual = -advManual;
        }

        Vector3d advNormal = advA.GetOutwardNormal(body, advQ0 - advBodyP, 0d).Normalized;
        double advAngle = Math.Acos(Math.Min(1d, Math.Max(-1d, Vector3d.Dot(advManual, advNormal))));
        bool advNormalOk = advAngle < 0.05d;

        // Zeros instead of initializers (old scenes without the new serialized
        // fields) - also bit-for-bit legacy via Effective* normalization.
        HeightfieldTerrain zeroed = MakeTerrain(42);
        zeroed.Lacunarity = 0d;
        zeroed.Gain = 0d;
        zeroed.ContinentFrequency = 0d;
        zeroed.ContinentOctaves = 0;
        zeroed.ContinentThreshold = 0d;
        zeroed.ContinentSharpness = 0d;
        zeroed.ContinentDepth = 0d;
        zeroed.RidgedMix = 0d;
        zeroed.WarpStrength = 0d;
        zeroed.WarpFrequency = 0d;
        zeroed.WarpOctaves = 0;
        zeroed.WarpSeedOffset = 0;
        bool zeroedExact = true;
        for (int i = 0; i <= 10; i++)
        {
            for (int j = 0; j <= 10; j++)
            {
                double lat = (-80d + (160d * i / 10d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 10d)) * (Math.PI / 180d);
                if (zeroed.GetHeightMeters(body, lat, lon) != legacy.GetHeightMeters(body, lat, lon))
                {
                    zeroedExact = false;
                }
            }
        }

        Check(legacyExact, "T82 terrain-advanced", "нули новых параметров = бит-в-бит legacy");
        Check(zeroedExact, "T82 terrain-advanced", "нули полей (старые сцены) = бит-в-бит legacy");
        Check(warpOffExact, "T82 terrain-advanced", "WarpStrength=0 игнорирует warp-параметры");
        Check(ridgedDiffers && warpedDiffers && contDiffers && lacDiffers && gainDiffers, "T82 terrain-advanced", "каждая фича меняет вывод");
        Check(warpSeedDiffers, "T82 terrain-advanced", "warp-поток независим (seed offset меняет рельеф)");
        Check(advDeterministic, "T82 terrain-advanced", "детерминизм advanced");
        Check(advSeaClamp, "T82 terrain-advanced", "кламп моря держится с фичами");
        Check(advBoundOk, "T82 terrain-advanced", "граница |h| ≤ A·(1+depth) (max " + advWorst.ToString("F1") + ")");
        Check(advNormalOk, "T82 terrain-advanced", "паритет нормали на гребне, угол " + advAngle.ToString("E3") + " рад < 0.05");
        return 0;
    }

    private static bool ColorsEqual(UnityEngine.Color a, UnityEngine.Color b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    private static int Test83_TerrainColor()
    {
        double sea = 0d;
        double amp = 50000d;
        double[] heights = { -1000d, 0d, 1d, 100d, 1000d, 5000d, 20000d, 40000d, 80000d };
        double[] slopes = { 0d, 0.1d, 0.5d, 1d, 5d };
        double[] masks = { -1d, 0d, 0.7d };

        // All features off = bit-for-bit legacy (including steep slopes and a
        // non-zero mask: guards, not literal ">= 0"/"<= 0" comparisons).
        bool legacyExact = true;
        foreach (double h in heights)
        {
            foreach (double s in slopes)
            {
                foreach (double m in masks)
                {
                    UnityEngine.Color ex = TerrainPalette.HeightColorEx(h, sea, amp, s, m, 0d, 0.1d, 0d, 0d);
                    UnityEngine.Color leg = TerrainPalette.HeightColorLegacy(h, sea, amp);
                    if (!ColorsEqual(ex, leg))
                    {
                        legacyExact = false;
                    }
                }
            }
        }

        // Rock-override: beach (t = 0.01) + steep slope -> exactly rock.
        UnityEngine.Color rock = TerrainPalette.Rock;
        bool rockFull = ColorsEqual(
            TerrainPalette.HeightColorEx(1000d, sea, amp, 1d, 0d, 0.5d, 0.1d, 0d, 0d), rock);

        // Partial blend: mid-transition is neither base nor rock.
        UnityEngine.Color rockBase = TerrainPalette.HeightColorLegacy(1000d, sea, amp);
        UnityEngine.Color rockMid = TerrainPalette.HeightColorEx(1000d, sea, amp, 0.55d, 0d, 0.5d, 0.1d, 0d, 0d);
        bool rockBlend = !ColorsEqual(rockMid, rockBase) && !ColorsEqual(rockMid, rock);

        // Below the threshold - base unchanged.
        bool rockBelow = ColorsEqual(
            TerrainPalette.HeightColorEx(1000d, sea, amp, 0.4d, 0d, 0.5d, 0.1d, 0d, 0d), rockBase);

        // Rock leaves the sea alone even on steep ground.
        UnityEngine.Color seaColor = TerrainPalette.HeightColorLegacy(-1000d, sea, amp);
        bool rockSkipsSea = ColorsEqual(
            TerrainPalette.HeightColorEx(-1000d, sea, amp, 5d, 0d, 0.5d, 0.1d, 0d, 0d), seaColor);

        // Rock height gate: on the beach (h=1000, t=0.02) rock is suppressed,
        // on a mountain (h=30000, t=0.6) it triggers - rocks only on large mountains.
        bool rockHeightGate = ColorsEqual(
            TerrainPalette.HeightColorEx(1000d, sea, amp, 1d, 0d, 0.5d, 0.1d, 0d, 0d, 0d, 0d, 0d, 0.4d), rockBase)
            && ColorsEqual(
                TerrainPalette.HeightColorEx(30000d, sea, amp, 1d, 0d, 0.5d, 0.1d, 0d, 0d, 0d, 0d, 0d, 0.4d), rock);

        // Snow-gate: t = 0.85 (h = 85000). Gentle - legacy snow, steep - rock.
        UnityEngine.Color snowLeg = TerrainPalette.HeightColorLegacy(85000d, sea, amp);
        bool snowGentle = ColorsEqual(
            TerrainPalette.HeightColorEx(85000d, sea, amp, 0.1d, 0d, 0.5d, 0.1d, 0.5d, 0d), snowLeg);
        bool snowSteep = ColorsEqual(
            TerrainPalette.HeightColorEx(85000d, sea, amp, 1d, 0d, 0.5d, 0.1d, 0.5d, 0d), rock);

        // Snow-guard: snowTan = 0 + steep -> legacy snow (no restriction).
        // Rock is off here - isolating snow-guard from rock-override.
        bool snowGuard = ColorsEqual(
            TerrainPalette.HeightColorEx(85000d, sea, amp, 1d, 0d, 0d, 0.1d, 0d, 0d), snowLeg);

        // Mask shifts bands: t = 0.24 (h = 24000) with strength 0.05.
        UnityEngine.Color bandBase = TerrainPalette.HeightColorLegacy(24000d, sea, amp);
        bool maskShifts = !ColorsEqual(
            TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, 1d, 0d, 0.1d, 0d, 0.05d), bandBase)
            && !ColorsEqual(
                TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, -1d, 0d, 0.1d, 0d, 0.05d), bandBase);

        // Mask strength = 0 is ignored (even a non-zero mask).
        bool maskOff = ColorsEqual(
            TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, 1d, 0d, 0.1d, 0d, 0d), bandBase);

        // SlopeTan: scale-invariance and guards.
        bool slopeZero = TerrainPalette.SlopeTan(1d) == 0d;
        bool slopeOne = Math.Abs(TerrainPalette.SlopeTan(Math.Sqrt(0.5d)) - 1d) < 1e-6d;
        bool slopeHuge = TerrainPalette.SlopeTan(0d) >= 1e6d && TerrainPalette.SlopeTan(-0.5d) >= 1e6d;
        bool slopeMono = TerrainPalette.SlopeTan(0.9d) < TerrainPalette.SlopeTan(0.5d)
            && TerrainPalette.SlopeTan(0.5d) < TerrainPalette.SlopeTan(0.1d);

        // SampleColorNoise: determinism, range, seed-offset independence.
        HeightfieldTerrain colorT = MakeTerrain(42);
        colorT.ColorNoiseFrequency = 2d;
        colorT.ColorNoiseOctaves = 3;
        bool noiseDet = true;
        bool noiseRange = true;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                double m1 = colorT.SampleColorNoise(lat, lon);
                double m2 = colorT.SampleColorNoise(lat, lon);
                if (m1 != m2)
                {
                    noiseDet = false;
                }

                if (!(m1 >= -1d - 1e-9d && m1 <= 1d + 1e-9d))
                {
                    noiseRange = false;
                }
            }
        }

        HeightfieldTerrain colorT2 = MakeTerrain(42);
        colorT2.ColorNoiseFrequency = 2d;
        colorT2.ColorNoiseOctaves = 3;
        colorT2.ColorNoiseSeedOffset = 5;
        bool noiseSeedDiffers = false;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                if (colorT.SampleColorNoise(lat, lon) != colorT2.SampleColorNoise(lat, lon))
                {
                    noiseSeedDiffers = true;
                }
            }
        }

        // Color detail (mottling): changes land color, strength 0 = ignored,
        // does not affect the sea (isolated from the water branch).
        bool detailChanges = !ColorsEqual(
            TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, 0d, 0d, 0.1d, 0d, 0d, 1d, 0.35d), bandBase)
            && !ColorsEqual(
                TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, 0d, 0d, 0.1d, 0d, 0d, -1d, 0.35d), bandBase);
        bool detailOff = ColorsEqual(
            TerrainPalette.HeightColorEx(24000d, sea, amp, 0d, 0d, 0d, 0.1d, 0d, 0d, 1d, 0d), bandBase);
        bool detailSkipsSea = ColorsEqual(
            TerrainPalette.HeightColorEx(-1000d, sea, amp, 0d, 0d, 0d, 0.1d, 0d, 0d, 1d, 0.35d), seaColor);

        // SampleColorDetailNoise: determinism, range, seed-offset independence.
        HeightfieldTerrain detailT = MakeTerrain(42);
        detailT.ColorDetailFrequency = 50d;
        detailT.ColorDetailOctaves = 3;
        bool detailDet = true;
        bool detailRange = true;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                double m1 = detailT.SampleColorDetailNoise(lat, lon);
                double m2 = detailT.SampleColorDetailNoise(lat, lon);
                if (m1 != m2)
                {
                    detailDet = false;
                }

                if (!(m1 >= -1d - 1e-9d && m1 <= 1d + 1e-9d))
                {
                    detailRange = false;
                }
            }
        }

        HeightfieldTerrain detailT2 = MakeTerrain(42);
        detailT2.ColorDetailFrequency = 50d;
        detailT2.ColorDetailOctaves = 3;
        detailT2.ColorDetailSeedOffset = 5;
        bool detailSeedDiffers = false;
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j <= 20; j++)
            {
                double lat = (-80d + (160d * i / 20d)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / 20d)) * (Math.PI / 180d);
                if (detailT.SampleColorDetailNoise(lat, lon) != detailT2.SampleColorDetailNoise(lat, lon))
                {
                    detailSeedDiffers = true;
                }
            }
        }

        Check(legacyExact, "T83 terrain-color", "Ex(всё выкл) = бит-в-бит legacy (guard'ы, склоны, маска)");
        Check(rockFull && rockBlend && rockBelow && rockSkipsSea, "T83 terrain-color", "rock-override: скала/бленд/порог/море");
        Check(rockHeightGate, "T83 terrain-color", "rock-override: порог по высоте (пляж без скал, горы со скалой)");
        Check(snowGentle && snowSteep && snowGuard, "T83 terrain-color", "snow-gate: пологий снег, крутой скала, guard 0");
        Check(maskShifts && maskOff, "T83 terrain-color", "маска сдвигает bands; strength 0 = игнор");
        Check(slopeZero && slopeOne && slopeHuge && slopeMono, "T83 terrain-color", "SlopeTan: 0/1/вертикаль/монотонность");
        Check(noiseDet && noiseRange && noiseSeedDiffers, "T83 terrain-color", "SampleColorNoise: детерминизм, [−1,1], seed offset");
        Check(detailChanges && detailOff && detailSkipsSea, "T83 terrain-color", "цветовая деталь: меняет землю, strength 0 = игнор, море не трогает");
        Check(detailDet && detailRange && detailSeedDiffers, "T83 terrain-color", "SampleColorDetailNoise: детерминизм, [−1,1], seed offset");
        return 0;
    }

    private static UnityEngine.Vector3 ToStubVector(Vector3d v)
    {
        return new UnityEngine.Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }

    private static bool SameDir(Vector3d a, Vector3d b)
    {
        return (a - b).Magnitude < 1e-12d;
    }

    /// <summary>
    /// Fixture "as in game". The source of truth is Assets/_Project/Profiles/Terrain/
    /// EarthLike.asset, not this copy: T117 catches divergence.
    ///
    /// </summary>
    private static HeightfieldTerrain SceneLikeTerrain()
    {
        return new HeightfieldTerrain
        {
            Seed = 24334543,
            AmplitudeMeters = 9144d,
            BaseFrequency = 20d,
            Octaves = 10,
            SeaLevelMeters = 0d,
            Lacunarity = 2d,
            Gain = 0.5d,
            ContinentFrequency = 4d,
            ContinentOctaves = 3,
            ContinentThreshold = -0.1d,
            ContinentSharpness = 0.3d,
            ContinentDepth = 1.2d,
            RidgedMix = 0.7d,
            PlainMix = 0.85d,
            PlainFrequency = 1.8d,
            PlainOctaves = 2,
            PlainThreshold = 0.05d,
            PlainSharpness = 0.25d,
            PlainElevation = 0.1d,
            BeachHeightMeters = 12d,
            BeachShelfAltitudeMeters = 8d,
            BeachShelfWidth = 0.08d,
            DetailMix = 0d,
            DetailFrequency = 700d,
            DetailOctaves = 5,
            WarpStrength = 0.1d,
            WarpFrequency = 2d,
            WarpOctaves = 2,
            ColorRockSlopeTan = 0.6d,
            ColorRockSlopeWidth = 0.15d,
            ColorRockHeightMin = 0.4d,
            ColorSnowSlopeTan = 0.5d,
            ColorNoiseFrequency = 25d,
            ColorNoiseOctaves = 4,
            ColorNoiseStrength = 0.09d,
            ColorDetailFrequency = 400d,
            ColorDetailOctaves = 3,
            ColorDetailStrength = 0.15d
        };
    }

    private static int Test87_LandFraction()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int total = 0;
        int land = 0;
        double maxH = double.NegativeInfinity;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= 100; i++)
            {
                for (int j = 0; j <= 100; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / 100d, j / 100d);
                    double h = TerrainNoise.SampleHeight(p, new Unity.Mathematics.double3(d.X, d.Y, d.Z)) * t.AmplitudeMeters;
                    if (h < t.SeaLevelMeters)
                    {
                        h = t.SeaLevelMeters;
                    }

                    total++;
                    if (h > t.SeaLevelMeters + (t.AmplitudeMeters * 0.001d))
                    {
                        land++;
                    }

                    if (h > maxH)
                    {
                        maxH = h;
                    }
                }
            }
        }

        double fraction = (double)land / total;
        System.Console.WriteLine("LANDFRAC fraction=" + fraction.ToString("F3") + " maxH=" + maxH.ToString("F0"));
        Check(fraction > 0.15d && fraction < 0.85d, "T87 land-fraction",
            "суша разумной доли: " + fraction.ToString("F3"));
        return 0;
    }

    private static int Test86_NaNProbe()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int bad = 0;
        double worst = 0d;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= 64; i++)
            {
                for (int j = 0; j <= 64; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / 64d, j / 64d);
                    double h = TerrainNoise.SampleHeight(p, new Unity.Mathematics.double3(d.X, d.Y, d.Z));
                    if (double.IsNaN(h) || double.IsInfinity(h))
                    {
                        bad++;
                    }
                    else
                    {
                        worst = Math.Max(worst, Math.Abs(h));
                    }
                }
            }
        }

        Check(bad == 0, "T86 nan-probe", "нет не-конечных высот (worst " + worst.ToString("E3") + ")");
        return 0;
    }

    private static int Test84_TerrainFloatParity()
    {
        OrbitingBody body = MakeTerrainBody();
        body.EvaluateWorldState(0d, out Vector3d bodyPos, out _);

        // Worst case for float: warp + ridged + continent at once
        // (input-shift error is amplified by crest sensitivity).
        HeightfieldTerrain adv = MakeTerrainAdvanced(42);
        adv.WarpStrength = 0.15d;
        adv.RidgedMix = 0.6d;
        adv.ColorNoiseFrequency = 2d;
        adv.ColorNoiseOctaves = 3;
        adv.ColorNoiseStrength = 0.05d;
        body.Terrain = adv;
        TerrainNoiseParams par = TerrainNoiseParams.FromTerrain(adv);
        double amp = adv.AmplitudeMeters;
        double sea = adv.SeaLevelMeters;

        // Direction grid + running the real job code.
        int n = 120;
        int count = (n + 1) * (n + 1);
        double[] lats = new double[count];
        double[] lons = new double[count];
        Unity.Collections.NativeArray<Unity.Mathematics.double3> dirs =
            new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        Unity.Collections.NativeArray<double> hs =
            new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        Unity.Collections.NativeArray<float> ms =
            new Unity.Collections.NativeArray<float>(count, Unity.Collections.Allocator.TempJob);
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= n; j++)
            {
                int k = (i * (n + 1)) + j;
                double lat = (-85d + (170d * i / n)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / n)) * (Math.PI / 180d);
                lats[k] = lat;
                lons[k] = lon;
                double cosLat = Math.Cos(lat);
                dirs[k] = new Unity.Mathematics.double3(
                    cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
            }
        }

        TerrainTileJob job = new TerrainTileJob { Params = par, Directions = dirs, Heights = hs, ColorMasks = ms };
        for (int k = 0; k < count; k++)
        {
            job.Execute(k);
        }

        // Scenario 0/1: the job (same implementation as the physics) must match
        // GetHeightMeters bit-for-bit - including warp+ridged on crests
        // (top-5% heights). A mismatch would mean the formula duplicates drifted.
        double[] hDouble = new double[count];
        double maxH = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            double h = adv.GetHeightMeters(body, lats[k], lons[k]);
            hDouble[k] = h;
            if (h > maxH)
            {
                maxH = h;
            }
        }

        bool heightsExact = true;
        bool crestExact = true;
        double worstH = 0d;
        double worstCrestH = 0d;
        double crestFloor = maxH - (0.05d * (maxH - sea));
        for (int k = 0; k < count; k++)
        {
            double hf = hs[k] * amp;
            if (hf < sea)
            {
                hf = sea;
            }

            if (hf != hDouble[k])
            {
                heightsExact = false;
            }

            double div = Math.Abs(hf - hDouble[k]);
            if (div > worstH)
            {
                worstH = div;
            }

            if (hDouble[k] >= crestFloor)
            {
                if (hf != hDouble[k])
                {
                    crestExact = false;
                }

                if (div > worstCrestH)
                {
                    worstCrestH = div;
                }
            }
        }

        // Mask: float vs double parity.
        double worstMask = 0d;
        for (int k = 0; k < count; k++)
        {
            double md = adv.SampleColorNoise(lats[k], lons[k]);
            double div = Math.Abs(ms[k] - md);
            if (div > worstMask)
            {
                worstMask = div;
            }
        }

        dirs.Dispose();
        hs.Dispose();
        ms.Dispose();

        // Scenario 2: the finest phase-4 tile (0.5 deg, 65 vertices) around the peak:
        // double vs float normals end-to-end (positions + central differences).
        double peakLat = 0d;
        double peakLon = 0d;
        for (int k = 0; k < count; k++)
        {
            if (hDouble[k] >= maxH)
            {
                peakLat = lats[k];
                peakLon = lons[k];
            }
        }

        int res = 65;
        int m = res + 2;
        double tileSpan = 0.5d;
        double step = tileSpan / (res - 1);
        Vector3d[,] posD = new Vector3d[m, m];
        UnityEngine.Vector3[,] posF = new UnityEngine.Vector3[m, m];
        for (int row = 0; row < m; row++)
        {
            double latDeg = (peakLat * (180d / Math.PI)) + ((row - ((m - 1) * 0.5d)) * step);
            if (latDeg > 88d)
            {
                latDeg = 88d;
            }

            if (latDeg < -88d)
            {
                latDeg = -88d;
            }

            double lat = latDeg * (Math.PI / 180d);
            for (int col = 0; col < m; col++)
            {
                double lon = (peakLon * (180d / Math.PI)) + ((col - ((m - 1) * 0.5d)) * step);
                double lonRad = lon * (Math.PI / 180d);
                double h = adv.GetHeightMeters(body, lat, lonRad);
                body.GetSurfaceState(latDeg, lon, h, 0d, out Vector3d world, out _);
                posD[row, col] = world - bodyPos;
                double cosLat2 = Math.Cos(lat);
                Unity.Mathematics.double3 dir = new Unity.Mathematics.double3(
                    cosLat2 * Math.Cos(lonRad), cosLat2 * Math.Sin(lonRad), Math.Sin(lat));
                double hf = TerrainNoise.SampleHeight(par, dir) * amp;
                if (hf < sea)
                {
                    hf = sea;
                }

                body.GetSurfaceState(latDeg, lon, hf, 0d, out Vector3d worldF, out _);
                Vector3d relF = worldF - bodyPos;
                posF[row, col] = new UnityEngine.Vector3((float)relF.X, (float)relF.Y, (float)relF.Z);
            }
        }

        // Scenario 3: the cos <= 1e-6 guard under float - same triggering,
        // tan values agree wherever the slope is finite.
        double worstNormalAngle = 0d;
        bool guardAgrees = true;
        double worstTanRel = 0d;
        for (int row = 0; row < res; row++)
        {
            for (int col = 0; col < res; col++)
            {
                int r = row + 1;
                int c = col + 1;
                Vector3d dLonD = posD[r, c + 1] - posD[r, c - 1];
                Vector3d dLatD = posD[r + 1, c] - posD[r - 1, c];
                Vector3d nD = Vector3d.Cross(dLonD, dLatD).Normalized;
                Vector3d upD = posD[r, c].Normalized;
                double cosD = Vector3d.Dot(nD, upD);

                UnityEngine.Vector3 dLonF = posF[r, c + 1] - posF[r, c - 1];
                UnityEngine.Vector3 dLatF = posF[r + 1, c] - posF[r - 1, c];
                UnityEngine.Vector3 nF = UnityEngine.Vector3.Cross(dLonF, dLatF);
                float lenF = nF.magnitude;
                nF = lenF > 1e-10f ? nF / lenF : posF[r, c].normalized;
                UnityEngine.Vector3 upF = posF[r, c].normalized;
                double cosF = UnityEngine.Vector3.Dot(nF, upF);

                double angle = Math.Acos(Math.Min(1d, Math.Max(-1d, Vector3d.Dot(nD, ToVector3d(nF)))));
                if (angle > worstNormalAngle)
                {
                    worstNormalAngle = angle;
                }

                double tanD = TerrainPalette.SlopeTan(cosD);
                double tanF = TerrainPalette.SlopeTan(cosF);
                bool guardD = tanD >= 1e6d;
                bool guardF = tanF >= 1e6d;
                if (guardD != guardF)
                {
                    guardAgrees = false;
                }

                if (!guardD && tanD > 0.01d)
                {
                    double rel = Math.Abs(tanF - tanD) / tanD;
                    if (rel > worstTanRel)
                    {
                        worstTanRel = rel;
                    }
                }
            }
        }

        Check(heightsExact, "T84 terrain-float-parity", "job == managed бит-в-бит (худшее " + worstH.ToString("E3") + " м)");
        Check(crestExact, "T84 terrain-float-parity", "гребни warp+ridged бит-в-бит (худшее " + worstCrestH.ToString("E3") + " м)");
        Check(worstMask <= 5e-6d, "T84 terrain-float-parity", "маска ≤ 5e-6 (худшее " + worstMask.ToString("E3") + ")");
        Check(worstNormalAngle <= 2e-3d, "T84 terrain-float-parity", "нормали мелкого тайла ≤ 2e-3 рад (худшее " + worstNormalAngle.ToString("E3") + ")");
        Check(guardAgrees && worstTanRel <= 0.05d, "T84 terrain-float-parity", "guard cos и tan под float (rel " + worstTanRel.ToString("E3") + ")");
        return 0;
    }

    private static Vector3d ToVector3d(UnityEngine.Vector3 v)
    {
        return new Vector3d(v.x, v.y, v.z);
    }

    /// <summary>
    /// Mean second difference of heights along the equator - isolates fine-scale
    /// roughness (large slope / low frequencies are not caught by a first difference).
    /// </summary>
    private static double FineRoughness(TerrainNoiseParams p, int samples, double stepRad)
    {
        double sum = 0d;
        double prev2 = TerrainNoise.SampleHeight(p, new Unity.Mathematics.double3(1d, 0d, 0d));
        double prev1 = TerrainNoise.SampleHeight(p, new Unity.Mathematics.double3(Math.Cos(stepRad), Math.Sin(stepRad), 0d));
        for (int k = 2; k < samples; k++)
        {
            double lon = k * stepRad;
            double cur = TerrainNoise.SampleHeight(p, new Unity.Mathematics.double3(Math.Cos(lon), Math.Sin(lon), 0d));
            sum += Math.Abs(cur - (2d * prev1) + prev2);
            prev2 = prev1;
            prev1 = cur;
        }

        return sum / Math.Max(1, samples - 2);
    }

    private static int Test89_TerrainPlains()
    {
        // The plains mask must use its OWN stream (salt 5), not the default
        // (base salt 0 and warp-Z salt 300 there). Verified indirectly: changing
        // PlainFrequency with PlainMix=0 must not change the output, while
        // enabling PlainMix flattens terrain by the mask only.
        HeightfieldTerrain freqOnly = SceneLikeTerrain();
        freqOnly.PlainMix = 0d;
        freqOnly.PlainFrequency = 1.8d;
        freqOnly.DetailMix = 0d;
        freqOnly.DetailFrequency = 0d;
        HeightfieldTerrain off = SceneLikeTerrain();
        off.PlainMix = 0d;
        off.PlainFrequency = 0d;
        off.DetailMix = 0d;
        off.DetailFrequency = 0d;

        // flat - exactly the scene configuration (validating the real values).
        HeightfieldTerrain flat = SceneLikeTerrain();

        // detailOn - same shape but without plains: isolating the detail contribution.
        HeightfieldTerrain detailOn = SceneLikeTerrain();
        detailOn.PlainMix = 0d;
        detailOn.DetailMix = 0.06d;
        detailOn.DetailFrequency = 700d;
        detailOn.DetailOctaves = 5;

        TerrainNoiseParams pFreq = TerrainNoiseParams.FromTerrain(freqOnly);
        TerrainNoiseParams pOff = TerrainNoiseParams.FromTerrain(off);
        TerrainNoiseParams pFlat = TerrainNoiseParams.FromTerrain(flat);
        TerrainNoiseParams pDetail = TerrainNoiseParams.FromTerrain(detailOn);

        const int n = 80;
        double[,] aOff = new double[n + 1, n + 1];
        double[,] aFlat = new double[n + 1, n + 1];
        bool freqIgnored = true;
        bool differs = false;
        bool nonFinite = false;
        double roughOff = 0d;
        double roughFlat = 0d;
        long roughCount = 0;
        long total = 0;
        long nearPlain = 0;
        long nearPlainOff = 0;
        long highFlat = 0;
        long highOff = 0;
        long landFlat = 0;
        long landOff = 0;

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    Unity.Mathematics.double3 dir = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    double hOff = TerrainNoise.SampleHeight(pOff, dir);
                    double hFreq = TerrainNoise.SampleHeight(pFreq, dir);
                    double hFlat = TerrainNoise.SampleHeight(pFlat, dir);
                    if (hFreq != hOff)
                    {
                        freqIgnored = false;
                    }

                    if (hFlat != hOff)
                    {
                        differs = true;
                    }

                    if (double.IsNaN(hFlat) || double.IsInfinity(hFlat))
                    {
                        nonFinite = true;
                    }

                    aOff[i, j] = hOff;
                    aFlat[i, j] = hFlat;
                    total++;
                    if (hFlat > 0d && Math.Abs(hFlat - flat.PlainElevation) < 0.05d)
                    {
                        nearPlain++;
                    }

                    if (hOff > 0d && Math.Abs(hOff - flat.PlainElevation) < 0.05d)
                    {
                        nearPlainOff++;
                    }

                    if (hFlat > 0.4d)
                    {
                        highFlat++;
                    }

                    if (hOff > 0.4d)
                    {
                        highOff++;
                    }

                    if (hFlat > 0d)
                    {
                        landFlat++;
                    }

                    if (hOff > 0d)
                    {
                        landOff++;
                    }
                }
            }

            for (int i = 1; i < n; i++)
            {
                for (int j = 1; j < n; j++)
                {
                    roughOff += Math.Abs(aOff[i + 1, j] - aOff[i - 1, j])
                        + Math.Abs(aOff[i, j + 1] - aOff[i, j - 1]);
                    roughFlat += Math.Abs(aFlat[i + 1, j] - aFlat[i - 1, j])
                        + Math.Abs(aFlat[i, j + 1] - aFlat[i, j - 1]);
                    roughCount++;
                }
            }
        }

        double meanOff = roughOff / roughCount;
        double meanFlat = roughFlat / roughCount;
        double fineOff = FineRoughness(pOff, 4000, 1e-4d);
        double fineDetail = FineRoughness(pDetail, 4000, 1e-4d);
        double plainFrac = (double)nearPlain / total;
        double plainFracOff = (double)nearPlainOff / total;
        System.Console.WriteLine("PLAINS nearPlain=" + plainFrac.ToString("F3")
            + " nearPlainOff=" + plainFracOff.ToString("F3")
            + " highFlat=" + ((double)highFlat / total).ToString("F3")
            + " highOff=" + ((double)highOff / total).ToString("F3")
            + " landFlat=" + ((double)landFlat / total).ToString("F3")
            + " landOff=" + ((double)landOff / total).ToString("F3")
            + " roughOff=" + meanOff.ToString("E3") + " roughFlat=" + meanFlat.ToString("E3")
            + " fineOff=" + fineOff.ToString("E3") + " fineDetail=" + fineDetail.ToString("E3"));

        // The job (same implementation as physics/render) must match
        // HeightfieldTerrain bit-for-bit with the plains mask enabled.
        int m = 40;
        int count = (m + 1) * (m + 1);
        Unity.Collections.NativeArray<Unity.Mathematics.double3> dirs =
            new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        Unity.Collections.NativeArray<double> hs =
            new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        Unity.Collections.NativeArray<float> ms =
            new Unity.Collections.NativeArray<float>(count, Unity.Collections.Allocator.TempJob);
        Unity.Collections.NativeArray<float> cds =
            new Unity.Collections.NativeArray<float>(count, Unity.Collections.Allocator.TempJob);
        for (int i = 0; i <= m; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                int k = (i * (m + 1)) + j;
                double lat = (-85d + (170d * i / m)) * (Math.PI / 180d);
                double lon = (-180d + (360d * j / m)) * (Math.PI / 180d);
                double cosLat = Math.Cos(lat);
                dirs[k] = new Unity.Mathematics.double3(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
            }
        }

        TerrainTileJob job = new TerrainTileJob { Params = pFlat, Directions = dirs, Heights = hs, ColorMasks = ms, ColorDetails = cds };
        bool jobExact = true;
        bool detailWired = false;
        double worstDetail = 0d;
        for (int k = 0; k < count; k++)
        {
            job.Execute(k);
            if (hs[k] != TerrainNoise.SampleHeight(pFlat, dirs[k]))
            {
                jobExact = false;
            }

            double sd = TerrainNoise.SampleColorDetailNoise(pFlat, dirs[k]);
            if (Math.Abs(cds[k]) > 1e-6d)
            {
                detailWired = true;
            }

            double div = Math.Abs(cds[k] - sd);
            if (div > worstDetail)
            {
                worstDetail = div;
            }
        }

        dirs.Dispose();
        hs.Dispose();
        ms.Dispose();
        cds.Dispose();

        Check(freqIgnored, "T89 terrain-plains", "PlainFrequency без PlainMix не меняет рельеф");
        Check(differs, "T89 terrain-plains", "PlainMix>0 меняет рельеф");
        Check(plainFrac > 0.1d, "T89 terrain-plains",
            "доля равнин заметна: " + plainFrac.ToString("F3"));
        Check(meanFlat < meanOff, "T89 terrain-plains",
            "равнины снижают шероховатость: " + meanFlat.ToString("E3") + " < " + meanOff.ToString("E3"));
        Check(fineDetail > fineOff * 1.05d, "T89 terrain-plains",
            "деталь добавляет мелкомасштабный рельеф: " + fineDetail.ToString("E3") + " > " + (fineOff * 1.05d).ToString("E3"));
        Check(!nonFinite, "T89 terrain-plains", "нет не-конечных высот");
        Check(jobExact, "T89 terrain-plains", "job == managed бит-в-бит с маской равнин");
        Check(detailWired && worstDetail <= 5e-6d, "T89 terrain-plains",
            "job пишет цветовую деталь (расхождение " + worstDetail.ToString("E3") + ")");
        return 0;
    }

    /// <summary>
    /// Terrain profile is the single copy point (T91): FromProfile must carry
    /// ALL fields into HeightfieldTerrain, Build must assemble terrain from
    /// BodyBlueprint.Terrain, and Clone must not share the palette with the
    /// original. A field forgotten when copying is caught here, not by eye in four places.
    /// </summary>
    private static int Test91_TerrainProfile()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        profile.AmplitudeMeters = 3210.5d;
        profile.ColorDetailStrength = 0.22d;
        profile.WarpSeedOffset = 17;
        profile.BeachHeightMeters = 30d;
        profile.BeachShelfAltitudeMeters = 8d;
        profile.BeachShelfWidth = 0.08d;
        profile.BeachShelfWidthMaxScale = 6d;
        profile.BeachShelfWidthNoiseFrequency = 120d;
        profile.BeachShelfWidthNoiseOctaves = 3;

        const int seed = 24334543;
        HeightfieldTerrain t = HeightfieldTerrain.FromProfile(profile, seed);

        bool fieldsCopied = t.Seed == seed
            && t.AmplitudeMeters == profile.AmplitudeMeters
            && t.BaseFrequency == profile.BaseFrequency
            && t.Octaves == profile.Octaves
            && t.SeaLevelMeters == profile.SeaLevelMeters
            && t.Lacunarity == profile.Lacunarity
            && t.Gain == profile.Gain
            && t.ContinentFrequency == profile.ContinentFrequency
            && t.ContinentOctaves == profile.ContinentOctaves
            && t.ContinentThreshold == profile.ContinentThreshold
            && t.ContinentSharpness == profile.ContinentSharpness
            && t.ContinentDepth == profile.ContinentDepth
            && t.RidgedMix == profile.RidgedMix
            && t.PlainMix == profile.PlainMix
            && t.PlainFrequency == profile.PlainFrequency
            && t.PlainOctaves == profile.PlainOctaves
            && t.PlainThreshold == profile.PlainThreshold
            && t.PlainSharpness == profile.PlainSharpness
            && t.PlainElevation == profile.PlainElevation
            && t.BeachHeightMeters == profile.BeachHeightMeters
            && t.BeachShelfAltitudeMeters == profile.BeachShelfAltitudeMeters
            && t.BeachShelfWidth == profile.BeachShelfWidth
            && t.BeachShelfWidthMaxScale == profile.BeachShelfWidthMaxScale
            && t.BeachShelfWidthNoiseFrequency == profile.BeachShelfWidthNoiseFrequency
            && t.BeachShelfWidthNoiseOctaves == profile.BeachShelfWidthNoiseOctaves
            && t.DetailMix == profile.DetailMix
            && t.DetailFrequency == profile.DetailFrequency
            && t.DetailOctaves == profile.DetailOctaves
            && t.WarpStrength == profile.WarpStrength
            && t.WarpFrequency == profile.WarpFrequency
            && t.WarpOctaves == profile.WarpOctaves
            && t.WarpSeedOffset == profile.WarpSeedOffset
            && t.ColorRockSlopeTan == profile.ColorRockSlopeTan
            && t.ColorRockSlopeWidth == profile.ColorRockSlopeWidth
            && t.ColorRockHeightMin == profile.ColorRockHeightMin
            && t.ColorSnowSlopeTan == profile.ColorSnowSlopeTan
            && t.ColorNoiseFrequency == profile.ColorNoiseFrequency
            && t.ColorNoiseOctaves == profile.ColorNoiseOctaves
            && t.ColorNoiseStrength == profile.ColorNoiseStrength
            && t.ColorNoiseSeedOffset == profile.ColorNoiseSeedOffset
            && t.ColorDetailFrequency == profile.ColorDetailFrequency
            && t.ColorDetailOctaves == profile.ColorDetailOctaves
            && t.ColorDetailStrength == profile.ColorDetailStrength
            && t.ColorDetailSeedOffset == profile.ColorDetailSeedOffset
            && t.Palette != null
            && !ReferenceEquals(t.Palette, profile.Palette)
            && t.Palette.Matches(profile.Palette);

        // Reference manual build: the shape must match bit-for-bit.
        var manual = new HeightfieldTerrain
        {
            Seed = seed,
            AmplitudeMeters = profile.AmplitudeMeters,
            BaseFrequency = profile.BaseFrequency,
            Octaves = profile.Octaves,
            SeaLevelMeters = profile.SeaLevelMeters,
            Lacunarity = profile.Lacunarity,
            Gain = profile.Gain,
            ContinentFrequency = profile.ContinentFrequency,
            ContinentOctaves = profile.ContinentOctaves,
            ContinentThreshold = profile.ContinentThreshold,
            ContinentSharpness = profile.ContinentSharpness,
            ContinentDepth = profile.ContinentDepth,
            RidgedMix = profile.RidgedMix,
            PlainMix = profile.PlainMix,
            PlainFrequency = profile.PlainFrequency,
            PlainOctaves = profile.PlainOctaves,
            PlainThreshold = profile.PlainThreshold,
            PlainSharpness = profile.PlainSharpness,
            PlainElevation = profile.PlainElevation,
            BeachHeightMeters = profile.BeachHeightMeters,
            BeachShelfAltitudeMeters = profile.BeachShelfAltitudeMeters,
            BeachShelfWidth = profile.BeachShelfWidth,
            BeachShelfWidthMaxScale = profile.BeachShelfWidthMaxScale,
            BeachShelfWidthNoiseFrequency = profile.BeachShelfWidthNoiseFrequency,
            BeachShelfWidthNoiseOctaves = profile.BeachShelfWidthNoiseOctaves,
            DetailMix = profile.DetailMix,
            DetailFrequency = profile.DetailFrequency,
            DetailOctaves = profile.DetailOctaves,
            WarpStrength = profile.WarpStrength,
            WarpFrequency = profile.WarpFrequency,
            WarpOctaves = profile.WarpOctaves,
            WarpSeedOffset = profile.WarpSeedOffset,
            ColorRockSlopeTan = profile.ColorRockSlopeTan,
            ColorRockSlopeWidth = profile.ColorRockSlopeWidth,
            ColorRockHeightMin = profile.ColorRockHeightMin,
            ColorSnowSlopeTan = profile.ColorSnowSlopeTan,
            ColorNoiseFrequency = profile.ColorNoiseFrequency,
            ColorNoiseOctaves = profile.ColorNoiseOctaves,
            ColorNoiseStrength = profile.ColorNoiseStrength,
            ColorNoiseSeedOffset = profile.ColorNoiseSeedOffset,
            ColorDetailFrequency = profile.ColorDetailFrequency,
            ColorDetailOctaves = profile.ColorDetailOctaves,
            ColorDetailStrength = profile.ColorDetailStrength,
            ColorDetailSeedOffset = profile.ColorDetailSeedOffset
        };

        TerrainNoiseParams fromProfile = TerrainNoiseParams.FromTerrain(t);
        TerrainNoiseParams fromManual = TerrainNoiseParams.FromTerrain(manual);
        bool heightsExact = true;
        for (int i = 0; i < 240; i++)
        {
            double lat = (-80d + (160d * i / 239d)) * (Math.PI / 180d);
            double lon = (-180d + (360d * i / 97d)) * (Math.PI / 180d);
            double cosLat = Math.Cos(lat);
            Unity.Mathematics.double3 dir = new Unity.Mathematics.double3(
                cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
            if (TerrainNoise.SampleHeight(fromProfile, dir) != TerrainNoise.SampleHeight(fromManual, dir))
            {
                heightsExact = false;
                break;
            }
        }

        // Build: BodyBlueprint.Terrain -> OrbitingBody.Terrain; the profile is
        // latched at build time (editing it after Build has no effect).
        var blueprint = new SystemBlueprint();
        blueprint.Bodies.Add(new BodyBlueprint
        {
            Name = "Тело",
            ParentIndex = -1,
            StandardGravitationalParameter = 1.327e20d,
            Terrain = profile,
            TerrainSeed = seed
        });
        StarSystem system = blueprint.Build();
        HeightfieldTerrain built = system.Root.Terrain as HeightfieldTerrain;
        bool buildWired = built != null
            && built.Seed == seed
            && built.AmplitudeMeters == profile.AmplitudeMeters
            && built.WarpSeedOffset == profile.WarpSeedOffset;
        if (built != null)
        {
            profile.AmplitudeMeters += 1d;
            buildWired = buildWired && built.AmplitudeMeters != profile.AmplitudeMeters;
        }

        TerrainProfile clone = profile.Clone();
        clone.Palette.Rock.r = 0.123456f;
        bool cloneIndependent = profile.Palette.Rock.r != 0.123456f;

        Check(fieldsCopied, "T91 terrain-profile", "FromProfile копирует все поля + палитру");
        Check(heightsExact, "T91 terrain-profile", "высоты из профиля == ручная сборка бит-в-бит");
        Check(buildWired, "T91 terrain-profile", "Build собирает terrain из BodyBlueprint.Terrain");
        Check(cloneIndependent, "T91 terrain-profile", "Clone не делит палитру с оригиналом");
        return 0;
    }

    /// <summary>
    /// The palette from the asset (sRGB->Linear) must match the legacy
    /// TerrainPalette constants bit-for-bit - otherwise a migration would silently change the picture.
    /// </summary>
    private static int Test92_TerrainPaletteData()
    {
        TerrainPaletteData palette = new TerrainPaletteData();
        bool defaults = ColorsEqual(palette.SandLinear, TerrainPalette.Sand)
            && ColorsEqual(palette.DesertLinear, TerrainPalette.Desert)
            && ColorsEqual(palette.DryGrassLinear, TerrainPalette.DryGrass)
            && ColorsEqual(palette.GrassLinear, TerrainPalette.Grass)
            && ColorsEqual(palette.ForestLinear, TerrainPalette.Forest)
            && ColorsEqual(palette.TundraLinear, TerrainPalette.Tundra)
            && ColorsEqual(palette.RockLinear, TerrainPalette.Rock)
            && ColorsEqual(palette.SnowLinear, TerrainPalette.Snow)
            && ColorsEqual(palette.SeaLinear, TerrainPalette.Sea)
            && ColorsEqual(palette.SoilLinear, TerrainPalette.Soil)
            && ColorsEqual(palette.LushLinear, TerrainPalette.Lush);

        TerrainPaletteData clone = palette.Clone();
        bool matches = clone.Matches(palette);
        clone.Grass.r += 0.05f;
        bool detects = !clone.Matches(palette);

        Check(defaults, "T92 terrain-palette", "данные палитры == legacy-константы");
        Check(matches && detects, "T92 terrain-palette", "Matches ловит копию и правку");
        return 0;
    }

    /// <summary>
    /// Decor placement: determinism (same point - same instance), parity of the
    /// Burst job and the direct function, scale/rotation ranges, and filters
    /// (density, height, water, biome).
    /// </summary>
    private static int Test93_GroundDecorDistribution()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var layer = new GroundDecorLayer
        {
            NearMeshes = new UnityEngine.Mesh[] { new UnityEngine.Mesh(), new UnityEngine.Mesh(), new UnityEngine.Mesh() },
            SpacingMeters = 2d,
            MaxInstancesPerChunk = 5000,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 0d,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1.2d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 0.6d,
            MaxScale = 1.4d,
            SteepPower = 4d
        };

        GroundDecorPlacementParams p = GroundDecorPlacementParams.FromLayer(layer, terrain, 1143000d, Vector3d.Zero);

        int n = 24;
        int count = CubeSphere.FaceCount * (n + 1) * (n + 1);
        var dirs = new Unity.Mathematics.double3[count];
        var randoms = new Unity.Mathematics.double3[count];
        var meshPicks = new double[count];
        int index = 0;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    dirs[index] = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    randoms[index] = new Unity.Mathematics.double3(
                        0d,
                        GroundDecorDistribution.Hash01(face, i, j, 4, 22),
                        GroundDecorDistribution.Hash01(face, j, i, 9, 23));
                    meshPicks[index] = GroundDecorDistribution.Hash01(face, i, j, 11, 24);
                    index++;
                }
            }
        }

        var jobDirs = new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        var jobRandoms = new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        var jobMeshPicks = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var jobBury = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var jobLean = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var jobAccepted = new Unity.Collections.NativeArray<int>(count, Unity.Collections.Allocator.TempJob);
        var jobInstances = new Unity.Collections.NativeArray<GroundDecorInstance>(count, Unity.Collections.Allocator.TempJob);
        var buryRandoms = new double[count];
        var leanRandoms = new double[count];
        for (int k = 0; k < count; k++)
        {
            jobDirs[k] = dirs[k];
            jobRandoms[k] = randoms[k];
            jobMeshPicks[k] = meshPicks[k];
            buryRandoms[k] = GroundDecorDistribution.Hash01(k, 7, 13, 47, 25);
            leanRandoms[k] = GroundDecorDistribution.Hash01(k, 11, 17, 53, 26);
            jobBury[k] = buryRandoms[k];
            jobLean[k] = leanRandoms[k];
        }

        var job = new GroundDecorCandidateJob
        {
            Directions = jobDirs,
            Randoms = jobRandoms,
            MeshPicks = jobMeshPicks,
            BuryRandoms = jobBury,
            LeanRandoms = jobLean,
            Terrain = terrainParams,
            Placement = p,
            Accepted = jobAccepted,
            Instances = jobInstances
        };
        for (int k = 0; k < count; k++)
        {
            job.Execute(k);
        }

        int acceptedCount = 0;
        bool jobParity = true;
        bool rangesOk = true;
        bool finiteOk = true;
        bool deterministic = true;
        bool meshIndexOk = true;
        for (int k = 0; k < count; k++)
        {
            bool direct = GroundDecorDistribution.TryEvaluate(p, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out GroundDecorInstance directInstance);
            if (direct != (jobAccepted[k] != 0))
            {
                jobParity = false;
            }

            if (!direct)
            {
                continue;
            }

            acceptedCount++;
            GroundDecorInstance jobInstance = jobInstances[k];
            if (directInstance.Scale != jobInstance.Scale || directInstance.Yaw != jobInstance.Yaw
                || directInstance.MeshIndex != jobInstance.MeshIndex
                || directInstance.Position.x != jobInstance.Position.x
                || directInstance.Position.y != jobInstance.Position.y
                || directInstance.Position.z != jobInstance.Position.z)
            {
                jobParity = false;
            }

            if (directInstance.MeshIndex < 0 || directInstance.MeshIndex >= layer.NearMeshes.Length)
            {
                meshIndexOk = false;
            }

            if (directInstance.Scale < (float)p.MinScale || directInstance.Scale > (float)p.MaxScale
                || directInstance.Yaw < 0f || directInstance.Yaw >= 6.283186f)
            {
                rangesOk = false;
            }

            if (float.IsNaN(directInstance.Position.x) || float.IsNaN(directInstance.Position.y) || float.IsNaN(directInstance.Position.z)
                || float.IsNaN(directInstance.Normal.x) || float.IsNaN(directInstance.Normal.y) || float.IsNaN(directInstance.Normal.z))
            {
                finiteOk = false;
            }

            if (!GroundDecorDistribution.TryEvaluate(p, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out GroundDecorInstance again)
                || again.Scale != directInstance.Scale || again.Yaw != directInstance.Yaw
                || again.Position.x != directInstance.Position.x)
            {
                deterministic = false;
            }
        }

        jobDirs.Dispose();
        jobRandoms.Dispose();
        jobMeshPicks.Dispose();
        jobBury.Dispose();
        jobLean.Dispose();
        jobAccepted.Dispose();
        jobInstances.Dispose();

        // The filters must zero out placements.
        GroundDecorPlacementParams noDensity = p;
        noDensity.Density = 0d;
        GroundDecorPlacementParams tooHigh = p;
        tooHigh.MinAltitudeMeters = 1e9d;
        GroundDecorPlacementParams tooWet = p;
        tooWet.WetMax = -0.001d;
        GroundDecorPlacementParams drowned = p;
        drowned.SeaLevelMeters = 1e9d;

        int zeroDensity = 0;
        int zeroAltitude = 0;
        int zeroWet = 0;
        int zeroWater = 0;
        for (int k = 0; k < count; k++)
        {
            if (GroundDecorDistribution.TryEvaluate(noDensity, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out _)) zeroDensity++;
            if (GroundDecorDistribution.TryEvaluate(tooHigh, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out _)) zeroAltitude++;
            if (GroundDecorDistribution.TryEvaluate(tooWet, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out _)) zeroWet++;
            if (GroundDecorDistribution.TryEvaluate(drowned, terrainParams, dirs[k], randoms[k], meshPicks[k], buryRandoms[k], leanRandoms[k], out _)) zeroWater++;
        }

        double hashA = GroundDecorDistribution.Hash01(1, 2, 3, 4, 5);
        double hashB = GroundDecorDistribution.Hash01(1, 2, 3, 4, 5);
        bool hashOk = hashA == hashB && hashA >= 0d && hashA < 1d;

        Check(acceptedCount > 0, "T93 decor-distribution", "посадки есть: " + acceptedCount + " из " + count);
        Check(jobParity && deterministic, "T93 decor-distribution", "джоба == прямая функция, бит-в-бит");
        Check(rangesOk && finiteOk, "T93 decor-distribution", "масштаб/поворот/позиции в границах");
        Check(meshIndexOk, "T93 decor-distribution", "MeshIndex в [0, " + layer.NearMeshes.Length + ")");
        Check(zeroDensity == 0 && zeroAltitude == 0 && zeroWet == 0 && zeroWater == 0,
            "T93 decor-distribution",
            "фильтры нулят: density=" + zeroDensity + " altitude=" + zeroAltitude + " wet=" + zeroWet + " water=" + zeroWater);
        Check(hashOk, "T93 decor-distribution", "Hash01 детерминирован и в [0,1)");
        return 0;
    }

    /// <summary>
    /// "Tree line" check: sample Terra terrain and verify that the Trees layer
    /// filter (MinAltitude 5, MaxAltitude 3200, slope 0.9, wet 0.1..1) accepts
    /// trees at high altitude but not above 3.2 km.
    /// </summary>
    private static int Test94_TreeHighAltitude()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var trees = new GroundDecorLayer
        {
            SpacingMeters = 60d,
            Density = 0.8d,
            DistributionFrequency = 150d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 5d,
            MaxAltitudeMeters = 3200d,
            MaxSlopeTan = 0.9d,
            AvoidWater = true,
            WetMin = 0.1d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 3d
        };
        GroundDecorPlacementParams p = GroundDecorPlacementParams.FromLayer(trees, terrain, 1143000d, Vector3d.Zero);

        const int n = 128;
        int total = 0;
        int above25 = 0;
        int above32 = 0;
        int acceptedAbove25 = 0;
        int acceptedAbove32 = 0;
        double maxHeight = double.MinValue;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    var dir = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    double raw = TerrainNoise.SampleHeight(terrainParams, dir) * terrain.AmplitudeMeters;
                    maxHeight = Math.Max(maxHeight, raw);
                    if (raw > 2500d)
                    {
                        above25++;
                    }

                    if (raw > 3200d)
                    {
                        above32++;
                    }

                    if (raw <= terrain.SeaLevelMeters + (terrain.AmplitudeMeters * 0.001d))
                    {
                        continue;
                    }

                    total++;
                    bool accepted = GroundDecorDistribution.TryEvaluate(
                        p, terrainParams, dir, new Unity.Mathematics.double3(0d, 0.5d, 0.5d), 0.5d, 0.5d, 0.5d, out _);
                    if (accepted && raw > 2500d)
                    {
                        acceptedAbove25++;
                    }

                    if (accepted && raw > 3200d)
                    {
                        acceptedAbove32++;
                    }
                }
            }
        }

        System.Console.WriteLine("TREEALT total=" + total + " maxH=" + maxHeight.ToString("F0")
            + " above2.5k=" + above25 + " above3.2k=" + above32
            + " accepted2.5k=" + acceptedAbove25 + " accepted3.2k=" + acceptedAbove32);
        Check(acceptedAbove25 > 0, "T94 tree-high-altitude", "деревья принимаются выше 2.5 км: " + acceptedAbove25);
        Check(acceptedAbove32 == 0, "T94 tree-high-altitude", "выше 3.2 км деревьев нет: " + acceptedAbove32);
        return 0;
    }

    /// <summary>
    /// Decor green band: grass/trees are accepted only on green palette land -
    /// water, beach (t&lt;0.03), rock (t&gt;=0.45) and dry (wet&lt;0.38) must
    /// give zero placements; rocks above the green band are accepted, in water not.
    /// Plus CubeFaceDirection parity with CubeSphere.Direction, the required
    /// IsSurfaceAllowed condition for TryEvaluate, and the snap guard IsMeshPointAboveWater.
    /// </summary>
    private static int Test99_DecorGreenBand()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var grass = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MinNormalizedHeight = 0.032d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 3200d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = GroundDecorLayer.GreenWetMin,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams g = GroundDecorPlacementParams.FromLayer(grass, terrain, 1143000d, Vector3d.Zero);

        var rocks = new GroundDecorLayer
        {
            SpacingMeters = 60d,
            Density = 1d,
            DistributionFrequency = 350d,
            DistributionOctaves = 3,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MaxAltitudeMeters = 8100d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams r = GroundDecorPlacementParams.FromLayer(rocks, terrain, 1143000d, Vector3d.Zero);

        bool wiringOk = g.MaxNormalizedHeight == GroundDecorLayer.RockBottomNormalizedHeight
            && r.MaxNormalizedHeight == 0d;

        const int n = 64;
        var zeroRandom = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);
        int water = 0, sand = 0, high = 0, dry = 0, green = 0;
        int grassAccepted = 0, grassBad = 0;
        int grassOnWater = 0, grassOnSand = 0, grassOnHigh = 0, grassOnDry = 0;
        int rocksAcceptedHigh = 0, rocksOnWater = 0;
        int allowedViolations = 0, allowedOnly = 0;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    var dir = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    double raw = TerrainNoise.SampleHeight(terrainParams, dir) * terrain.AmplitudeMeters;
                    bool isWater = raw <= terrain.SeaLevelMeters + (terrain.AmplitudeMeters * 0.001d);
                    if (isWater)
                    {
                        water++;
                    }

                    double aboveSea = raw - terrain.SeaLevelMeters;
                    double mask = TerrainNoise.SampleColorNoise(terrainParams, dir);
                    mask = mask < -1d ? -1d : (mask > 1d ? 1d : mask);
                    double t = (aboveSea / Math.Max(1d, terrain.AmplitudeMeters)) + (mask * terrain.ColorNoiseStrength);
                    // Wetness curve comes FROM the decor code, otherwise the test
                    // would classify "dry" by a different formula and see grass where there is none.
                    double wet = GroundDecorDistribution.BiomeWetness(mask);

                    bool acceptedGrass = GroundDecorDistribution.TryEvaluate(
                        g, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
                    bool acceptedRocks = GroundDecorDistribution.TryEvaluate(
                        r, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
                    bool allowed = GroundDecorDistribution.IsSurfaceAllowed(g, terrainParams, dir);
                    if (acceptedGrass && !allowed)
                    {
                        allowedViolations++;
                    }

                    if (!acceptedGrass && allowed)
                    {
                        allowedOnly++;
                    }

                    if (isWater)
                    {
                        if (acceptedGrass) grassOnWater++;
                        if (acceptedRocks) rocksOnWater++;
                        continue;
                    }

                    if (t < 0.032d)
                    {
                        sand++;
                        if (acceptedGrass) grassOnSand++;
                    }
                    else if (t >= GroundDecorLayer.RockBottomNormalizedHeight)
                    {
                        high++;
                        if (acceptedGrass) grassOnHigh++;
                        if (acceptedRocks) rocksAcceptedHigh++;
                    }
                    else if (wet < GroundDecorLayer.GreenWetMin)
                    {
                        dry++;
                        if (acceptedGrass) grassOnDry++;
                    }
                    else
                    {
                        green++;
                    }

                    if (acceptedGrass)
                    {
                        grassAccepted++;
                        if (isWater || aboveSea < 2d || aboveSea > 3200d
                            || t < 0.032d || t >= GroundDecorLayer.RockBottomNormalizedHeight
                            || wet < GroundDecorLayer.GreenWetMin || wet > 1d)
                        {
                            grassBad++;
                        }
                    }
                }
            }
        }

        // Direction parity: the Burst twin must point the same way as CubeSphere.
        bool dirParity = true;
        int[] faces = { 0, 1, 2, 3, 4, 5 };
        double[] uvs = { 0d, 0.13d, 0.5d, 0.87d, 1d, -0.05d, 1.05d };
        for (int fi = 0; fi < faces.Length && dirParity; fi++)
        {
            for (int ui = 0; ui < uvs.Length && dirParity; ui++)
            {
                for (int vi = 0; vi < uvs.Length && dirParity; vi++)
                {
                    Vector3d expected = CubeSphere.Direction(faces[fi], uvs[ui], uvs[vi]);
                    Unity.Mathematics.double3 actual =
                        GroundDecorDistribution.CubeFaceDirection(faces[fi], uvs[ui], uvs[vi]);
                    double dx = Math.Abs(expected.X - actual.x);
                    double dy = Math.Abs(expected.Y - actual.y);
                    double dz = Math.Abs(expected.Z - actual.z);
                    if (dx > 1e-9d || dy > 1e-9d || dz > 1e-9d)
                    {
                        dirParity = false;
                    }
                }
            }
        }

        // Snap guard: sim=(x,z,-y) from astro-rel; chunk centre (R+100,0,0).
        double radius = 1143000d;
        var chunkCenter = new Vector3d(radius + 100d, 0d, 0d);
        GroundDecorPlacementParams meshP = GroundDecorPlacementParams.FromLayer(grass, terrain, radius, chunkCenter);
        // NB: FromLayer takes Amplitude/SeaLevel from the EarthLike terrain
        // profile; for a point check we substitute sea level 0 and the profile amplitude.
        double sea = meshP.SeaLevelMeters;
        double tol = meshP.AmplitudeMeters * 0.001d;
        bool meshLand = GroundDecorDistribution.IsMeshPointAboveWater(
            meshP, new Unity.Mathematics.float3(0f, 0f, 0f));
        // sim=(x,z,-y) from astro-rel: with centre (R+100,0,0) the radial is the X
        // axis, Y/Z are tangential (a 10 m shift along them barely changes the height).
        bool meshMapping = Math.Abs(GroundDecorDistribution.MeshPointHeightAboveRadius(
                meshP, new Unity.Mathematics.float3(10f, 0f, 0f)) - 110d) < 1e-3d
            && Math.Abs(GroundDecorDistribution.MeshPointHeightAboveRadius(
                meshP, new Unity.Mathematics.float3(-10f, 0f, 0f)) - 90d) < 1e-3d
            && Math.Abs(GroundDecorDistribution.MeshPointHeightAboveRadius(
                meshP, new Unity.Mathematics.float3(0f, 0f, 10f)) - 100d) < 1e-2d;
        var drownedCenter = new Vector3d(radius + sea + tol - 10d, 0d, 0d);
        GroundDecorPlacementParams meshSunk = GroundDecorPlacementParams.FromLayer(grass, terrain, radius, drownedCenter);
        bool meshWater = !GroundDecorDistribution.IsMeshPointAboveWater(
            meshSunk, new Unity.Mathematics.float3(0f, 0f, 0f));

        System.Console.WriteLine("GREENBAND water=" + water + " sand=" + sand + " high=" + high
            + " dry=" + dry + " green=" + green + " grassAccepted=" + grassAccepted
            + " rocksAcceptedHigh=" + rocksAcceptedHigh + " allowedOnly=" + allowedOnly);
        Check(wiringOk, "T99 decor-green-band", "FromLayer пробрасывает MaxNormalizedHeight");
        Check(green > 0 && grassAccepted > 0, "T99 decor-green-band", "зелень есть и принимается: " + grassAccepted + " из " + green);
        Check(grassBad == 0 && grassOnWater == 0 && grassOnSand == 0 && grassOnHigh == 0 && grassOnDry == 0,
            "T99 decor-green-band",
            "трава вне зелени: bad=" + grassBad + " water=" + grassOnWater + " sand=" + grassOnSand
            + " high=" + grassOnHigh + " dry=" + grassOnDry);
        Check(rocksAcceptedHigh > 0 && rocksOnWater == 0, "T99 decor-green-band",
            "камни в горах есть (" + rocksAcceptedHigh + "), в воде нет (" + rocksOnWater + ")");
        Check(allowedViolations == 0, "T99 decor-green-band",
            "TryEvaluate ⊆ IsSurfaceAllowed, нарушений=" + allowedViolations);
        Check(dirParity, "T99 decor-green-band", "CubeFaceDirection == CubeSphere.Direction");
        Check(meshLand && meshMapping && meshWater, "T99 decor-green-band",
            "страж снэпа: land=" + meshLand + " mapping=" + meshMapping + " water=" + meshWater);
        return 0;
    }

    /// <summary>
    /// Pool stale-flag reset (T100): candidate arrays come from DecorArrayPool with
    /// foreign data. The job must explicitly clear Accepted on rejected entries,
    /// otherwise a stale Accepted=1 with a stale position draws another chunk's decor in the sky.
    /// </summary>
    private static int Test100_DecorStalePoolReset()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var layer = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 0d,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 0.6d,
            MaxScale = 1.4d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams p = GroundDecorPlacementParams.FromLayer(layer, terrain, 1143000d, Vector3d.Zero);
        // Everything under water: every candidate must be rejected.
        p.SeaLevelMeters = 1e9d;

        const int n = 8;
        int count = CubeSphere.FaceCount * (n + 1) * (n + 1);
        var dirs = new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        var randoms = new Unity.Collections.NativeArray<Unity.Mathematics.double3>(count, Unity.Collections.Allocator.TempJob);
        var meshPicks = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var bury = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var lean = new Unity.Collections.NativeArray<double>(count, Unity.Collections.Allocator.TempJob);
        var accepted = new Unity.Collections.NativeArray<int>(count, Unity.Collections.Allocator.TempJob);
        var instances = new Unity.Collections.NativeArray<GroundDecorInstance>(count, Unity.Collections.Allocator.TempJob);
        int index = 0;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    dirs[index] = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    randoms[index] = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);
                    meshPicks[index] = 0.5d;
                    bury[index] = 0.5d;
                    lean[index] = 0.5d;
                    // "Foreign data" from the pool: flag plus an instance from the sky.
                    accepted[index] = 1;
                    instances[index] = new GroundDecorInstance
                    {
                        Position = new Unity.Mathematics.float3(1e6f, 2e6f, -3e6f),
                        Scale = 5f
                    };
                    index++;
                }
            }
        }

        var job = new GroundDecorCandidateJob
        {
            Directions = dirs,
            Randoms = randoms,
            MeshPicks = meshPicks,
            BuryRandoms = bury,
            LeanRandoms = lean,
            Terrain = terrainParams,
            Placement = p,
            Accepted = accepted,
            Instances = instances
        };
        for (int k = 0; k < count; k++)
        {
            job.Execute(k);
        }

        int stale = 0;
        for (int k = 0; k < count; k++)
        {
            if (accepted[k] != 0)
            {
                stale++;
            }
        }

        dirs.Dispose();
        randoms.Dispose();
        meshPicks.Dispose();
        bury.Dispose();
        lean.Dispose();
        accepted.Dispose();
        instances.Dispose();

        Check(stale == 0, "T100 decor-stale-reset", "stale Accepted после отклонения: " + stale + " из " + count);
        return 0;
    }

    /// <summary>
    /// Beach by the water (T101): the shelf pulls coastal terrain toward +8 m
    /// (without touching the ocean or far land), the CPU palette paints everything
    /// below 0.4*beach as sand for any mask and then fades sand out to 1.6*beach,
    /// and decor (grass AND rocks) below the beach line is rejected entirely.
    /// </summary>
    private static int Test101_BeachBand()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        profile.BeachHeightMeters = 200d;
        profile.BeachShelfAltitudeMeters = 8d;
        profile.BeachShelfWidth = 0.08d;
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        TerrainProfile legacyProfile = TerrainProfile.CreateEarthLike(1143000d);

        // Disable the shelf on the twin EXPLICITLY: the test must not depend on
        // the preset omitting a field.
        legacyProfile.BeachShelfAltitudeMeters = 0d;
        legacyProfile.BeachShelfWidth = 0d;
        HeightfieldTerrain legacyTerrain = HeightfieldTerrain.FromProfile(legacyProfile, 24334543);
        TerrainNoiseParams legacyParams = TerrainNoiseParams.FromTerrain(legacyTerrain);

        var grass = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MinNormalizedHeight = 0.032d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 3200d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = GroundDecorLayer.GreenWetMin,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams g = GroundDecorPlacementParams.FromLayer(grass, terrain, 1143000d, Vector3d.Zero);

        var rocks = new GroundDecorLayer
        {
            SpacingMeters = 60d,
            Density = 1d,
            DistributionFrequency = 350d,
            DistributionOctaves = 3,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MaxAltitudeMeters = 8100d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams r = GroundDecorPlacementParams.FromLayer(rocks, terrain, 1143000d, Vector3d.Zero);

        bool wiringOk = g.BeachHeightMeters == 200d && r.BeachHeightMeters == 200d;

        const int n = 48;
        var zeroRandom = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);
        int beachTotal = 0, beachGrass = 0, beachRocks = 0, grassAbove = 0;
        int beachBand = 0, twinBeachBand = 0, shelfPulled = 0, shelfWrong = 0, sandMismatch = 0;
        int fadePoints = 0, fadeExactSand = 0;
        const double beachH = 200d;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    var dir = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    double raw = TerrainNoise.SampleHeight(terrainParams, dir) * terrain.AmplitudeMeters;
                    double twin = TerrainNoise.SampleHeight(legacyParams, dir) * terrain.AmplitudeMeters;
                    double aboveSea = raw - terrain.SeaLevelMeters;
                    bool isWater = raw <= terrain.SeaLevelMeters + (terrain.AmplitudeMeters * 0.001d);
                    if (isWater)
                    {
                        // The shelf leaves the ocean alone: strictly below sea
                        // level heights are bit-for-bit. (The 0..amp*0.001 band is
                        // land for the shelf but "water" for rendering: there the shelf legitimately pulls to +8.)
                        if (twin <= terrain.SeaLevelMeters && twin != raw)
                        {
                            shelfWrong++;
                        }

                        continue;
                    }

                    if (aboveSea < beachH)
                    {
                        beachTotal++;
                        if (GroundDecorDistribution.TryEvaluate(
                            g, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _)) beachGrass++;
                        if (GroundDecorDistribution.TryEvaluate(
                            r, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _)) beachRocks++;

                        // Sand for any mask (steep slope + mask +1): in the full
                        // band (up to 0.4*beach) - exactly Sand, no mottling or rock.
                        double mask = TerrainNoise.SampleColorNoise(terrainParams, dir);
                        mask = mask < -1d ? -1d : (mask > 1d ? 1d : mask);
                        UnityEngine.Color sandColor = TerrainPalette.HeightColorEx(
                            raw, terrain.SeaLevelMeters, terrain.AmplitudeMeters,
                            5d, 1d, 0.6d, 0.15d, 0.5d, 0.09d, 1d, 0.35d, 0d, 0d, beachH);
                        double sandW = TerrainPalette.SandWeight(aboveSea, beachH);
                        if (sandW >= 1d && !ColorsEqual(sandColor, TerrainPalette.Sand))
                        {
                            sandMismatch++;
                        }

                        if (sandW > 0d && sandW < 1d)
                        {
                            fadePoints++;
                            if (ColorsEqual(sandColor, TerrainPalette.Sand))
                            {
                                fadeExactSand++;
                            }
                        }

                        beachBand++;
                    }
                    else if (GroundDecorDistribution.TryEvaluate(
                        g, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _))
                    {
                        grassAbove++;
                    }

                    double twinAbove = twin - terrain.SeaLevelMeters;
                    if (twinAbove >= 0d && twinAbove < beachH)
                    {
                        twinBeachBand++;
                    }

                    // The shelf pulls toward +8 m and never pushes away: for land
                    // the distance to the shelf does not grow (ocean already filtered above).
                    double toShelfNow = Math.Abs(aboveSea - 8d);
                    double toShelfTwin = Math.Abs(twinAbove - 8d);
                    if (twinAbove >= 0d)
                    {
                        if (toShelfNow <= toShelfTwin + 1e-9d)
                        {
                            if (toShelfTwin - toShelfNow > 1e-6d)
                            {
                                shelfPulled++;
                            }
                        }
                        else
                        {
                            shelfWrong++;
                        }
                    }
                }
            }
        }

        System.Console.WriteLine("BEACH total=" + beachTotal + " grassAbove=" + grassAbove
            + " band=" + beachBand + " twinBand=" + twinBeachBand + " pulled=" + shelfPulled
            + " fadePoints=" + fadePoints + " fadeExactSand=" + fadeExactSand);
        Check(wiringOk, "T101 beach-band", "FromLayer читает пляж из террейна");
        Check(beachTotal > 0, "T101 beach-band", "полоса пляжа существует: " + beachTotal);
        Check(beachGrass == 0 && beachRocks == 0, "T101 beach-band",
            "на пляже ничего: трава=" + beachGrass + " камни=" + beachRocks);
        Check(grassAbove > 0, "T101 beach-band", "выше пляжа трава принимается: " + grassAbove);
        Check(sandMismatch == 0, "T101 beach-band", "песок при любой маске/склоне: mismatch=" + sandMismatch);
        Check(fadePoints > 0 && fadeExactSand == 0, "T101 beach-band",
            "в затухании песка (0.4…1.6·beach) цвет смешанный, чистого Sand нет: точек "
            + fadePoints + ", чистого песка " + fadeExactSand);
        Check(shelfWrong == 0 && shelfPulled > 0, "T101 beach-band",
            "полка тянет к +8м: pulled=" + shelfPulled + " wrong=" + shelfWrong);
        Check(beachBand >= twinBeachBand, "T101 beach-band",
            "полка расширяет полосу: shelf=" + beachBand + " legacy=" + twinBeachBand);
        return 0;
    }

    /// <summary>
    /// Vegetation density by latitude (T102). Fade removed: every layer without
    /// IgnoreLatitude has the same weight at any latitude (UniformGreenWeight),
    /// and rocks with IgnoreLatitude are unconstrained. Uniformity is checked,
    /// not monotonicity.
    /// </summary>
    private static int Test102_PolarDecorLatitude()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var grass = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MinNormalizedHeight = 0.032d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = GroundDecorLayer.GreenWetMin,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams g = GroundDecorPlacementParams.FromLayer(grass, terrain, 1143000d, Vector3d.Zero);

        var rocks = new GroundDecorLayer
        {
            SpacingMeters = 60d,
            Density = 1d,
            DistributionFrequency = 350d,
            DistributionOctaves = 3,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d,
            IgnoreLatitude = true
        };
        GroundDecorPlacementParams r = GroundDecorPlacementParams.FromLayer(rocks, terrain, 1143000d, Vector3d.Zero);

        // Latitude weight: the same everywhere, no fade (IgnoreLatitude = 1).
        var equator = new Unity.Mathematics.double3(1d, 0d, 0d);
        var pole = new Unity.Mathematics.double3(0d, 0d, 1d);
        double wEquator = GroundDecorDistribution.LatitudeGreenWeight(g, equator);
        double wPole = GroundDecorDistribution.LatitudeGreenWeight(g, pole);
        double wRocksPole = GroundDecorDistribution.LatitudeGreenWeight(r, pole);
        bool uniform = true;
        for (int lat = 0; lat <= 90; lat += 5)
        {
            double la = lat * System.Math.PI / 180d;
            var dir = new Unity.Mathematics.double3(System.Math.Cos(la), 0d, System.Math.Sin(la));
            double w = GroundDecorDistribution.LatitudeGreenWeight(g, dir);
            if (System.Math.Abs(w - GroundDecorDistribution.UniformGreenWeight) > 1e-12d)
            {
                uniform = false;
            }
        }

        // Land on ice (lon90/lat80): no grass, flagged rocks present.
        double iceLa = 80d * System.Math.PI / 180d;
        double iceLo = 90d * System.Math.PI / 180d;
        var iceDir = new Unity.Mathematics.double3(
            System.Math.Cos(iceLa) * System.Math.Cos(iceLo),
            System.Math.Cos(iceLa) * System.Math.Sin(iceLo),
            System.Math.Sin(iceLa));
        var zeroRandom = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);
        bool iceGrass = GroundDecorDistribution.TryEvaluate(
            g, terrainParams, iceDir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
        bool iceRocks = GroundDecorDistribution.TryEvaluate(
            r, terrainParams, iceDir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);

        // Land in tundra (lon90/lat65): same weight as the equator.
        double tunLa = 65d * System.Math.PI / 180d;
        double tunLo = 90d * System.Math.PI / 180d;
        var tundraDir = new Unity.Mathematics.double3(
            System.Math.Cos(tunLa) * System.Math.Cos(tunLo),
            System.Math.Cos(tunLa) * System.Math.Sin(tunLo),
            System.Math.Sin(tunLa));
        double wTundra = GroundDecorDistribution.LatitudeGreenWeight(g, tundraDir);

        System.Console.WriteLine("POLAR wEquator=" + wEquator.ToString("F3")
            + " wTundra=" + wTundra.ToString("F3") + " wPole=" + wPole.ToString("F3")
            + " iceGrass=" + iceGrass + " iceRocks=" + iceRocks);
        Check(wEquator == GroundDecorDistribution.UniformGreenWeight
            && wPole == GroundDecorDistribution.UniformGreenWeight
            && wRocksPole == 1d && uniform, "T102 polar-decor-latitude",
            "вес одинаков на всех широтах: " + wEquator.ToString("F3")
            + " камни без ограничения=" + wRocksPole.ToString("F3"));
        Check(wTundra == wEquator && wTundra > 0d, "T102 polar-decor-latitude",
            "на широте тундры вес не меньше экватора: " + wTundra.ToString("F3"));
        Check(iceRocks, "T102 polar-decor-latitude",
            "камни с IgnoreLatitude на льду принимаются");
        return 0;
    }

    /// <summary>
    /// Vertex mask (T103): the shader interpolates the CPU mask from vertices
    /// (paint/placement parity, variant A) instead of its own Fbm. The bilinear
    /// interpolation error at real chunk scale must be negligible: otherwise the
    /// biome edge would diverge between the picture and the decor.
    /// Harness measurement (Terra seed): depth 6 - maxMaskErr ~1.3e-3, depth 10 - ~4e-6.
    /// </summary>
    private static int Test103_VertexMaskInterp()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        double worstMask = 0d;
        foreach (int depth in new[] { 6, 10 })
        {
            double cell = (1d / (1 << depth)) / 64d;
            var hash = new System.Random(4242 + depth);
            for (int s = 0; s < 800; s++)
            {
                int face = hash.Next(0, 6);
                double u0 = hash.NextDouble() * (1d - cell);
                double v0 = hash.NextDouble() * (1d - cell);
                Vector3d c00 = CubeSphere.Direction(face, u0, v0);
                Vector3d c10 = CubeSphere.Direction(face, u0 + cell, v0);
                Vector3d c01 = CubeSphere.Direction(face, u0, v0 + cell);
                Vector3d c11 = CubeSphere.Direction(face, u0 + cell, v0 + cell);
                Vector3d cc = CubeSphere.Direction(face, u0 + (cell * 0.5d), v0 + (cell * 0.5d));
                double mTrue = TerrainNoise.SampleColorNoise(terrainParams,
                    new Unity.Mathematics.double3(cc.X, cc.Y, cc.Z));
                double mInterp = (TerrainNoise.SampleColorNoise(terrainParams,
                        new Unity.Mathematics.double3(c00.X, c00.Y, c00.Z))
                    + TerrainNoise.SampleColorNoise(terrainParams,
                        new Unity.Mathematics.double3(c10.X, c10.Y, c10.Z))
                    + TerrainNoise.SampleColorNoise(terrainParams,
                        new Unity.Mathematics.double3(c01.X, c01.Y, c01.Z))
                    + TerrainNoise.SampleColorNoise(terrainParams,
                        new Unity.Mathematics.double3(c11.X, c11.Y, c11.Z))) * 0.25d;
                double err = System.Math.Abs(mTrue - mInterp);
                if (err > worstMask)
                {
                    worstMask = err;
                }
            }
        }

        System.Console.WriteLine("VERTEXMASK worst=" + worstMask.ToString("E3"));
        Check(worstMask < 0.01d, "T103 vertex-mask-interp",
            "ошибка интерполяции маски в масштабе чанков: " + worstMask.ToString("E3"));
        return 0;
    }

    /// <summary>
    /// Dry steppe with green ground (T104). The point must remain green-height
    /// land with a gentle slope where the blade mat (WetMin=0) and trees
    /// (WetMin=0.2) both grow, and rocks too.
    /// </summary>
    private static int Test104_DrySteppeVegetation()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);

        var grass = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MinNormalizedHeight = 0.032d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams g = GroundDecorPlacementParams.FromLayer(grass, terrain, 1143000d, Vector3d.Zero);

        var trees = new GroundDecorLayer
        {
            SpacingMeters = 12d,
            Density = 1d,
            DistributionFrequency = 150d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 5d,
            MinNormalizedHeight = 0.032d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0.2d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams t = GroundDecorPlacementParams.FromLayer(trees, terrain, 1143000d, Vector3d.Zero);

        var rocks = new GroundDecorLayer
        {
            SpacingMeters = 60d,
            Density = 1d,
            DistributionFrequency = 350d,
            DistributionOctaves = 3,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d,
            IgnoreLatitude = true
        };
        GroundDecorPlacementParams r = GroundDecorPlacementParams.FromLayer(rocks, terrain, 1143000d, Vector3d.Zero);

        var dir = new Unity.Mathematics.double3(-0.13226119907427766d, -0.6148487863029645d, -0.7774753662986409d);
        double raw = TerrainNoise.SampleHeight(terrainParams, dir) * terrain.AmplitudeMeters;
        var zeroRandom = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);
        bool grassOk = GroundDecorDistribution.TryEvaluate(
            g, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
        bool treesOk = GroundDecorDistribution.TryEvaluate(
            t, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
        bool rocksOk = GroundDecorDistribution.TryEvaluate(
            r, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);

        System.Console.WriteLine("DRYSTEPPE raw=" + raw.ToString("F0")
            + " grass=" + grassOk + " trees=" + treesOk + " rocks=" + rocksOk);
        // The original complaint ("2732 m relief") was measured on a preset that
        // diverged from the game; the complaint itself is checked by inequality, not a number.
        Check(System.Math.Abs(raw - 2323d) < 1d, "T104 dry-steppe-vegetation",
            "регрессионный пин высоты: " + raw.ToString("F0") + " м (было 2321)");
        Check(raw > 0d && raw < 4000d, "T104 dry-steppe-vegetation",
            "жалоба по существу: точка суши на зелёных высотах, рельеф "
            + raw.ToString("F0") + " м (исходная жалоба: 2732 м)");
        Check(grassOk && treesOk && rocksOk, "T104 dry-steppe-vegetation",
            "сухая степь зеленеет: трава=" + grassOk + " деревья=" + treesOk + " камни=" + rocksOk);
        return 0;
    }

    /// <summary>
    /// Sand is the absolute beach only (T106): below 0.4*beach the palette always
    /// gives exactly Sand, above 1.6*beach never, and in the 0.4..1.6 fade sand is
    /// mixed with the biome. Decor with MinNormalizedHeight = 0 seeds from the first
    /// metre above the beach and never on painted sand (paint<->decor parity).
    /// </summary>
    private static int Test106_SandIsBeachBandOnly()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        profile.BeachHeightMeters = 200d;
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);
        double radius = 1143000d;
        double amp = terrain.AmplitudeMeters;
        double sea = terrain.SeaLevelMeters;
        double beach = terrain.BeachHeightMeters;
        double seaEpsilon = amp * 0.001d;

        // 1a. Full sand band: exactly Sand for any mask/slope.
        int sandAbove = 0, paintedAbove = 0, sandBelow = 0, paintedBelow = 0;
        int fadePoints = 0, fadeExactSand = 0;
        double[] heights = { beach + 0.5d, beach + 5d, beach + 40d, beach + 120d, beach + 300d, beach + 900d };
        double[] masks = { -1d, -0.35d, 0d, 0.35d, 1d };
        double[] slopes = { 0d, 0.3d, 0.8d, 2d };
        for (int hi = 0; hi < heights.Length; hi++)
        {
            for (int mi = 0; mi < masks.Length; mi++)
            {
                for (int si = 0; si < slopes.Length; si++)
                {
                    UnityEngine.Color c = TerrainPalette.HeightColorEx(
                        sea + heights[hi], sea, amp, slopes[si], masks[mi],
                        0.6d, 0.15d, 0.5d, 0.09d, 1d, 0.35d, 0d, 0d, beach);
                    if (ColorsEqual(c, TerrainPalette.Sand))
                    {
                        sandAbove++;
                    }

                    paintedAbove++;
                }
            }
        }

        for (int hi = 0; hi < heights.Length; hi++)
        {
            double aboveSea = (beach - seaEpsilon) * (hi + 1) / (double)(heights.Length + 1);
            double h = seaEpsilon + aboveSea;
            double sandW = TerrainPalette.SandWeight(aboveSea, beach);
            for (int mi = 0; mi < masks.Length; mi++)
            {
                for (int si = 0; si < slopes.Length; si++)
                {
                    UnityEngine.Color c = TerrainPalette.HeightColorEx(
                        h, sea, amp, slopes[si], masks[mi], 0.6d, 0.15d, 0.5d, 0.09d, 1d, 0.35d, 0d, 0d, beach);
                    bool isSand = ColorsEqual(c, TerrainPalette.Sand);
                    if (sandW >= 1d)
                    {
                        if (isSand)
                        {
                            sandBelow++;
                        }

                        paintedBelow++;
                    }
                    else if (sandW > 0d)
                    {
                        fadePoints++;
                        if (isSand)
                        {
                            fadeExactSand++;
                        }
                    }
                }
            }
        }

        // 1b. The fade is monotonic and symmetric about the beach line: exactly
        // half sand at BeachHeightMeters, zero at 1.6*beach.
        double halfW = TerrainPalette.SandWeight(beach, beach);
        double zeroW = TerrainPalette.SandWeight(1.6d * beach, beach);
        bool fadeCurve = System.Math.Abs(halfW - 0.5d) < 1e-9d && zeroW <= 0d
            && TerrainPalette.SandWeight(0.2d * beach, beach) >= 1d
            && TerrainPalette.SandWeight(beach, 0d) == 0d;

        // 2. Decor with MinNormalizedHeight = 0 seeds from the first metre above the beach.
        var grass = new GroundDecorLayer
        {
            SpacingMeters = 2d,
            Density = 1d,
            DistributionFrequency = 5000d,
            DistributionOctaves = 4,
            ClusterThreshold = 0d,
            MinAltitudeMeters = 2d,
            MinNormalizedHeight = 0d,
            MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
            MaxAltitudeMeters = 1e9d,
            MaxSlopeTan = 1e9d,
            AvoidWater = true,
            WetMin = 0d,
            WetMax = 1d,
            MinScale = 1d,
            MaxScale = 2d,
            SteepPower = 0d
        };
        GroundDecorPlacementParams g = GroundDecorPlacementParams.FromLayer(grass, terrain, radius, Vector3d.Zero);
        var zeroRandom = new Unity.Mathematics.double3(0d, 0.5d, 0.5d);

        int onBeach = 0, beachPoints = 0, justAbove = 0;
        int greenWithGrass = 0, grassOnPaintedSand = 0;
        const int n = 96;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / (double)n, j / (double)n);
                    var dir = new Unity.Mathematics.double3(d.X, d.Y, d.Z);
                    double raw = TerrainNoise.SampleHeight(terrainParams, dir) * amp;
                    if (raw <= sea + seaEpsilon)
                    {
                        continue;
                    }

                    double aboveSea = raw - sea;
                    bool accepted = GroundDecorDistribution.TryEvaluate(
                        g, terrainParams, dir, zeroRandom, 0.5d, 0.5d, 0.5d, out _);
                    if (aboveSea < beach)
                    {
                        beachPoints++;
                        if (accepted)
                        {
                            onBeach++;
                        }
                    }

                    if (!accepted)
                    {
                        continue;
                    }

                    double mask = TerrainNoise.SampleColorNoise(terrainParams, dir);
                    mask = mask < -1d ? -1d : (mask > 1d ? 1d : mask);
                    double slope = GroundDecorDistribution.SlopeTan(terrainParams, g, dir, out _);
                    UnityEngine.Color c = TerrainPalette.HeightColorEx(
                        raw, sea, amp, slope, mask, 0.6d, 0.15d, 0.5d, 0.09d, 0.35d, 0d, 0d, beach);
                    if (ColorsEqual(c, TerrainPalette.Sand))
                    {
                        grassOnPaintedSand++;
                    }

                    greenWithGrass++;
                    if (aboveSea < beach + 60d)
                    {
                        justAbove++;
                    }
                }
            }
        }

        System.Console.WriteLine("SANDBAND paintedAbove=" + paintedAbove + " sandAbove=" + sandAbove
            + " paintedBelow=" + paintedBelow + " sandBelow=" + sandBelow
            + " fadePoints=" + fadePoints + " fadeExactSand=" + fadeExactSand
            + " halfW=" + halfW.ToString("F3") + " zeroW=" + zeroW.ToString("F3")
            + " beachPoints=" + beachPoints + " onBeach=" + onBeach
            + " greenWithGrass=" + greenWithGrass + " justAbove=" + justAbove
            + " grassOnPaintedSand=" + grassOnPaintedSand);
        Check(paintedAbove > 0 && sandAbove == 0, "T106 sand-is-beach-band",
            "выше пляжа палитра не рисует чистый песок (" + sandAbove + " из " + paintedAbove + ")");
        Check(paintedBelow > 0 && sandBelow == paintedBelow, "T106 sand-is-beach-band",
            "ниже 0.4·пляжа палитра рисует песок всегда (" + sandBelow + " из " + paintedBelow + ")");
        Check(fadeCurve, "T106 sand-is-beach-band",
            "кривая песка: половина на границе пляжа, ноль с 1.6·пляжа (half=" + halfW.ToString("F3")
            + ", zero=" + zeroW.ToString("F3") + ")");
        Check(fadePoints > 0 && fadeExactSand == 0, "T106 sand-is-beach-band",
            "в затухании песок смешан с биомом (точек " + fadePoints + ", чистого Sand " + fadeExactSand + ")");
        Check(beachPoints > 0 && onBeach == 0, "T106 sand-is-beach-band",
            "на пляже декор не сеет: точек " + beachPoints + ", посадок " + onBeach);
        Check(justAbove > 0, "T106 sand-is-beach-band",
            "трава сеется сразу над пляжем (точек в первых 60 м: " + justAbove + ")");
        Check(greenWithGrass > 0 && grassOnPaintedSand == 0, "T106 sand-is-beach-band",
            "паритет краска↔декор: трава на зелени (" + greenWithGrass + "), на песке "
            + grassOnPaintedSand);
        return 0;
    }

    /// <summary>
    /// The biome wetness curve must not saturate (T107). A soft curve
    /// (BiomeWetness, mirrored in PlanetSurface.shader) keeps the share of land
    /// with no possible trees within a few percent.
    /// </summary>
    private static int Test107_WetnessNeverSaturates()
    {
        TerrainProfile profile = TerrainProfile.CreateEarthLike(1143000d);
        profile.BeachHeightMeters = 12d;
        HeightfieldTerrain terrain = HeightfieldTerrain.FromProfile(profile, 24334543);
        TerrainNoiseParams terrainParams = TerrainNoiseParams.FromTerrain(terrain);
        double amp = terrain.AmplitudeMeters;
        double sea = terrain.SeaLevelMeters;

        // 1. The curve never hits 0/1 at any mask value.
        double minWet = 1d, maxWet = 0d;
        int atZero = 0, atOne = 0;
        for (int i = -1000; i <= 1000; i++)
        {
            double w = GroundDecorDistribution.BiomeWetness(i / 1000d);
            minWet = System.Math.Min(minWet, w);
            maxWet = System.Math.Max(maxWet, w);
            if (w <= 0d) { atZero++; }
            if (w >= 1d) { atOne++; }
        }

        // 2. Share of land where trees (WetMin=0.2) are impossible at all.
        const int n = 60000;
        double golden = System.Math.PI * (3d - System.Math.Sqrt(5d));
        int land = 0, treeDead = 0;
        for (int i = 0; i < n; i++)
        {
            double z = 1d - (2d * ((i + 0.5d) / n));
            double r = System.Math.Sqrt(System.Math.Max(0d, 1d - (z * z)));
            double phi = i * golden;
            var dir = new Unity.Mathematics.double3(System.Math.Cos(phi) * r, System.Math.Sin(phi) * r, z);
            double raw = TerrainNoise.SampleHeight(terrainParams, dir) * amp;
            if (raw <= sea + (amp * 0.001d))
            {
                continue;
            }

            double mask = TerrainNoise.SampleColorNoise(terrainParams, dir);
            mask = mask < -1d ? -1d : (mask > 1d ? 1d : mask);
            double t = ((raw - sea) / System.Math.Max(1d, amp)) + (mask * terrain.ColorNoiseStrength);
            if ((raw - sea) < terrain.BeachHeightMeters || t >= GroundDecorLayer.RockBottomNormalizedHeight)
            {
                continue;
            }

            land++;
            if (GroundDecorDistribution.BiomeWetness(mask) < 0.2d)
            {
                treeDead++;
            }
        }

        double deadShare = land > 0 ? (double)treeDead / land : 0d;
        System.Console.WriteLine("WETNESS min=" + minWet.ToString("F4") + " max=" + maxWet.ToString("F4")
            + " atZero=" + atZero + " atOne=" + atOne
            + " land=" + land + " treeDead=" + (deadShare * 100d).ToString("F1") + "%");
        Check(atZero == 0 && atOne == 0 && minWet > 0d && maxWet < 1d, "T107 wetness-no-saturation",
            "кривая не насыщается: min=" + minWet.ToString("F4") + " max=" + maxWet.ToString("F4"));
        Check(land > 0 && deadShare < 0.1d, "T107 wetness-no-saturation",
            "суши без единого дерева: " + (deadShare * 100d).ToString("F1") + "% (было ~25%)");
        return 0;
    }
}
