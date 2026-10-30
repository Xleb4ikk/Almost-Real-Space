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
    private static EventOccurrence? RunLegacy(StarSystem sys, OrbitingBody planet, Vector3d p0, Vector3d v0, double m0, double horizon, bool atmosphere)
    {
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.UseDenseRefinement = false;
        AddDetectors(prop, planet, atmosphere);
        var ship = new Spacecraft(p0, v0, m0);
        return prop.Propagate(ship, 0d, horizon);
    }

    private static EventOccurrence? RunDense(StarSystem sys, OrbitingBody planet, Vector3d p0, Vector3d v0, double m0, double horizon, bool atmosphere, out long legacyCalls, out long denseCalls)
    {
        // Measure the refinement cost: a counting source on both paths.
        var physL = new SpacecraftPhysics();
        var countL = new CountingSource(new CachedGravitySource(sys));
        physL.Sources.Add(countL);
        var propL = new EventDrivenPropagator(physL);
        propL.UseDenseRefinement = false;
        AddDetectors(propL, planet, atmosphere);
        var shipL = new Spacecraft(p0, v0, m0);
        EventOccurrence? rL = propL.Propagate(shipL, 0d, horizon);
        legacyCalls = countL.Calls;

        var physD = new SpacecraftPhysics();
        var countD = new CountingSource(new CachedGravitySource(sys));
        physD.Sources.Add(countD);
        var propD = new EventDrivenPropagator(physD);
        propD.UseDenseRefinement = true;
        AddDetectors(propD, planet, atmosphere);
        var shipD = new Spacecraft(p0, v0, m0);
        EventOccurrence? rD = propD.Propagate(shipD, 0d, horizon);
        denseCalls = countD.Calls;
        return rD;
    }

    private static void AddDetectors(EventDrivenPropagator prop, OrbitingBody planet, bool atmosphere)
    {
        if (atmosphere)
        {
            prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
            prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        }
        else
        {
            prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        }
    }

    private static List<EventOccurrence> CollectAll(StarSystem sys, OrbitingBody planet, Vector3d p0, Vector3d v0, bool dense)
    {
        var out_ = new List<EventOccurrence>();
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.UseDenseRefinement = dense;
        AddDetectors(prop, planet, true);
        var ship = new Spacecraft(p0, v0, 1000d);
        double t = 0d;
        while (t < 600d - 1e-9d && out_.Count <= 5)
        {
            EventOccurrence? ev = prop.Propagate(ship, t, 600d - t);
            if (!ev.HasValue)
            {
                break;
            }

            out_.Add(ev.Value);
            t = ev.Value.TimeSeconds;
        }

        return out_;
    }

    private static int Test21_Kahan()
    {
        // T21a: pure arithmetic. Exact value 1e7×0.1 = 1e6.
        // The naive accumulation error is NOT pinned by a threshold (it depends on
        // the rounding order of a given implementation) — printed for information.
        // Kahan must be substantially closer: its own absolute tolerance plus at
        // least 100× better than naive (in practice — orders of magnitude).
        double naive = 0d;
        var kahan = new KahanAccumulator(0d);
        for (int i = 0; i < 10000000; i++)
        {
            naive += 0.1d;
            kahan.Add(0.1d);
        }

        double naiveErr = Math.Abs(naive - 1000000d);
        double kahanErr = Math.Abs(kahan.Sum - 1000000d);
        bool micro = kahanErr < 1e-6d && kahanErr <= naiveErr * 0.01d;

        // T21b: 10 years of ballistic coast in daily frames (as the game does):
        // completion, finiteness, system bounds, a scheduled event exactly at the
        // end of the span, bit determinism of a repeated run.
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        double span = 10d * 365d * 86400d;
        double muPlanet = sys.AllBodies[1].ResolveStandardGravitationalParameter();
        double circV = Math.Sqrt(muPlanet / 1e8d);
        Func<Spacecraft> makeShip = () => new Spacecraft(
            pp + new Vector3d(0d, 1e8d, 0d), pv + new Vector3d(circV, 0d, 0d), 1000d);
        Func<LongWarpResult> runYears = () =>
        {
            var phys = new SpacecraftPhysics();
            phys.Sources.Add(new CachedGravitySource(sys));
            var prop = new EventDrivenPropagator(phys);
            prop.TimedEvents.Add(new TimedEvent { TimeSeconds = span, DeltaMass = -100d, MinimumMassKg = 500d });
            var driver = new LongWarpDriver(phys, prop);
            var ship = makeShip();
            double t = 0d;
            LongWarpResult last = new LongWarpResult(0d, null, true);
            var sw = Stopwatch.StartNew();
            while (t < span - 1e-9d)
            {
                double frameEnd = Math.Min(t + 86400d, span);
                last = driver.AdvanceToTarget(ship, t, frameEnd);
                if (last.StoppingEvent.HasValue && last.ReachedTimeSeconds < span - 1e-9d)
                {
                    break;
                }

                t = frameEnd;
            }

            sw.Stop();
            return last;
        };

        LongWarpResult r1 = runYears();
        // Second run for determinism — the ship state is compared separately.
        var phys2 = new SpacecraftPhysics();
        phys2.Sources.Add(new CachedGravitySource(sys));
        var prop2 = new EventDrivenPropagator(phys2);
        var driver2 = new LongWarpDriver(phys2, prop2);
        var shipA = makeShip();
        var shipB = makeShip();
        driver2.AdvanceToTarget(shipA, 0d, span);
        driver2.AdvanceToTarget(shipB, 0d, span);
        double rePos = (shipA.Position - shipB.Position).Magnitude;

        bool macro = r1.StoppingEvent.HasValue
            && r1.ReachedTimeSeconds == span
            && r1.StoppingEvent.Value.TimeSeconds == span
            && shipA.Position.IsFinite && shipA.Velocity.IsFinite
            && shipA.Position.Magnitude < 5e11d
            && rePos == 0d;
        Check(micro && macro, "T21 kahan",
            string.Format("micro: naiveΔ={0:E2}с (инфо, без порога) kahanΔ={1:E2}с (<1e-6, лучше≥100x); macro 10 лет: дошёл={2} t={3:E12} timed-точно={4} |pos|<5e11: {5} детерминизм бит-в-бит: {6}",
                naiveErr, kahanErr, r1.StoppingEvent.HasValue, r1.ReachedTimeSeconds,
                r1.StoppingEvent.HasValue && r1.StoppingEvent.Value.TimeSeconds == span,
                shipA.Position.Magnitude < 5e11d, rePos == 0d));
        return 0;
    }

    private static int Test22_DragContract()
    {
        // B1 contract: formula, v_rel of a rotating atmosphere, cutoffs, warp exclusion.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);

        // (a) Hand calculation: h=0, ρ=1.2, v_rel=(1000,0,0), Cd=1, A=10 → F=(-6e6,0,0).
        var drag = new DragSource(planet);
        // Tolerances 1e-6, not 1e-12: subtracting the body position (|bp|~1.5e11)
        // gives altitude noise ±3e-5 m → density noise ~4e-9. The world-origin fp
        // effect (the reason for A2): physics tolerates 1e-11 of forces, tests ask
        // no tighter.
        Vector3d pA = bp0 + new Vector3d(planet.Radius, 0d, 0d);
        DynamicsContribution cA = drag.Evaluate(pA, bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        bool formula = Math.Abs((cA.Force.X + 6000000d) / 6000000d) < 1e-6d
            && cA.Force.Y == 0d && cA.Force.Z == 0d
            && cA.SpecificAcceleration.SqrMagnitude == 0d && cA.MassFlow == 0d;
        double halfH = 8500d * Math.Log(2d);
        DynamicsContribution cHalf = drag.Evaluate(bp0 + new Vector3d(planet.Radius + halfH, 0d, 0d), bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        bool half = Math.Abs(cHalf.Force.X / cA.Force.X - 0.5d) < 1e-6d;

        // (b) v_rel discipline: rotating body (period 3600), point above the equator.
        // A co-rotating ship (|v_world| huge) → exactly 0; an inertially hovering
        // one → drag opposes v_rel, not world velocity.
        planet.RotationPeriodSeconds = 3600d;
        double rAt = planet.Radius + 50000d;
        Vector3d rVec = new Vector3d(0d, rAt, 0d);
        double omega = 2d * Math.PI / 3600d;
        Vector3d atmV = bv0 + new Vector3d(-omega * rAt, 0d, 0d);
        DynamicsContribution cCo = drag.Evaluate(bp0 + rVec, atmV, 1000d, 0d);
        bool coRotating = cCo.Force.SqrMagnitude == 0d;
        DynamicsContribution cHover = drag.Evaluate(bp0 + rVec, bv0, 1000d, 0d);
        Vector3d vRelHover = bv0 - atmV;
        bool hoverDir = (cHover.Force.Normalized + vRelHover.Normalized).Magnitude < 1e-12d;
        double rho50 = 1.2d * Math.Exp(-50000d / 8500d);
        double expectHover = 0.5d * rho50 * 10d * vRelHover.Magnitude * vRelHover.Magnitude;
        bool hoverMag = Math.Abs(cHover.Force.Magnitude / expectHover - 1d) < 1e-6d;

        // (c) Cutoffs: above Top → 0; below Top → ≠0; no atmosphere → 0; Cd=0 → 0;
        // degenerate (H=0) → 0 and finite (not NaN).
        DynamicsContribution cAbove = drag.Evaluate(bp0 + new Vector3d(planet.Radius + 100001d, 0d, 0d), bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        DynamicsContribution cBelow = drag.Evaluate(bp0 + new Vector3d(planet.Radius + 99999d, 0d, 0d), bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        bool cutoff = cAbove.Force.SqrMagnitude == 0d && cBelow.Force.SqrMagnitude > 0d;
        var moonDrag = new DragSource(sys.AllBodies[2]);
        DynamicsContribution cNoAtm = moonDrag.Evaluate(pA, bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        drag.DragCoefficient = 0d;
        DynamicsContribution cNoCd = drag.Evaluate(pA, bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        drag.DragCoefficient = 1d;
        planet.Atmosphere.ScaleHeightMeters = 0d;
        DynamicsContribution cDeg = drag.Evaluate(pA, bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        planet.Atmosphere.ScaleHeightMeters = 8500d;
        bool guards = cNoAtm.Force.SqrMagnitude == 0d && cNoCd.Force.SqrMagnitude == 0d
            && cDeg.Force.SqrMagnitude == 0d && cDeg.Force.IsFinite;

        // (e) Long warp: forbidden inside the atmosphere by the drag contribution, allowed outside.
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(drag);
        var prop = new EventDrivenPropagator(phys);
        var driver = new LongWarpDriver(phys, prop);
        var inside = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d),
            Velocity = bv0 + new Vector3d(7000d, 0d, 0d),
            Mass = 1000d
        };
        var outside = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, planet.Radius + 500000d, 0d),
            Velocity = bv0 + new Vector3d(7000d, 0d, 0d),
            Mass = 1000d
        };
        planet.RotationPeriodSeconds = 0d;
        bool warpOut = driver.CanEnterLongWarp(outside, 0d);
        planet.RotationPeriodSeconds = 3600d;
        bool warpIn = !driver.CanEnterLongWarp(inside, 0d);

        Check(formula && half && coRotating && hoverDir && hoverMag && cutoff && guards && warpIn && warpOut, "T22 drag-contract",
            string.Format("формула F=(-6e6,0,0): {0}; ρ(H·ln2)/ρ0=0.5: {1}; со-вращение F=0: {2}; hover против v_rel: {3} |F|={4:E3}Н; срез Top: {5}; стражи: {6}; варп внутри запрещён/снаружи можно: {7}/{8}",
                formula, half, coRotating, hoverDir, cHover.Force.Magnitude, cutoff, guards, warpIn, warpOut));
        return 0;
    }

    private static int Test22_DragDip()
    {
        // (d) Dissipation separate from detection: a dip with and without drag —
        // both find [Exit, Entry], but the exit speed is lower with drag (energy
        // lost). Events untouched: same detectors, same bisection.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d p0 = bp0 + new Vector3d(0d, planet.Radius + 95000d, 0d);
        Vector3d v0 = bv0 + new Vector3d(3000d, 800d, 0d);

        Func<bool, List<EventOccurrence>> fly = (withDrag) =>
        {
            var phys = new SpacecraftPhysics();
            phys.Sources.Add(new CachedGravitySource(sys));
            if (withDrag)
            {
                phys.Sources.Add(new DragSource(planet));
            }

            var prop = new EventDrivenPropagator(phys);
            prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
            prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
            var ship = new Spacecraft(p0, v0, 1000d);
            var events = new List<EventOccurrence>();
            double t = 0d;
            while (t < 1200d - 1e-9d && events.Count < 2)
            {
                EventOccurrence? ev = prop.Propagate(ship, t, 1200d - t);
                if (!ev.HasValue)
                {
                    break;
                }

                events.Add(ev.Value);
                t = ev.Value.TimeSeconds;
            }

            return events;
        };

        List<EventOccurrence> clean = fly(false);
        List<EventOccurrence> draggy = fly(true);
        bool order = clean.Count == 2 && draggy.Count == 2
            && clean[0].DetectorName.StartsWith("AtmosphereExit") && clean[1].DetectorName.StartsWith("AtmosphereEntry")
            && draggy[0].DetectorName.StartsWith("AtmosphereExit") && draggy[1].DetectorName.StartsWith("AtmosphereEntry");
        double dvExit = clean[0].State.Velocity.Magnitude - draggy[0].State.Velocity.Magnitude;
        Check(order && dvExit > 0.5d, "T22 drag-dip",
            string.Format("оба находят [Exit,Entry]: {0}; Δv_выход={1:F2}м/с (>0.5): {2}",
                order, dvExit, dvExit > 0.5d));
        return 0;
    }

    private static AtmosphereProfile ThinAtmosphere()
    {
        return new AtmosphereProfile
        {
            TopAltitudeMeters = 300000d,
            SeaLevelDensityKgPerCubicMeter = 1e-6d,
            ScaleHeightMeters = 30000d
        };
    }

    private static double PlanetRelativeEnergy(StarSystem sys, OrbitingBody planet, Vector3d pos, Vector3d vel, double time)
    {
        planet.EvaluateWorldState(time, out Vector3d bp, out Vector3d bv);
        Vector3d r = pos - bp;
        Vector3d v = vel - bv;
        return 0.5d * v.SqrMagnitude - planet.ResolveStandardGravitationalParameter() / r.Magnitude;
    }

    private static int Test23_Decay()
    {
        // Orbital decay in a thin atmosphere: 10 orbits, apo/peri and energy
        // monotonically down (increase only within the 1e-6 wobble of perturbations).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = ThinAtmosphere();
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        // 250 km orbit in a thin atmosphere: linear regime (−0.7 km/orbit), no
        // avalanche (at 200 km the ρ feedback drops into the planet within ~10
        // orbits: ΔE/E=−0.68, periapsis 3.8e6 m).
        double mu = planet.ResolveStandardGravitationalParameter();
        double r0 = planet.Radius + 250000d;
        double circV = Math.Sqrt(mu / r0);
        double period = 2d * Math.PI * Math.Sqrt(r0 * r0 * r0 / mu);

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new DragSource(planet));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, r0, 0d), bv0 + new Vector3d(circV, 0d, 0d), 1000d);

        double e0 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 0d);
        bool mono = true;
        double prevMax = double.MaxValue;
        double prevMin = double.MaxValue;
        double prevE = e0;
        double t = 0d;
        for (int orbit = 0; orbit < 10; orbit++)
        {
            var segs = new List<DenseSegment>();
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, period, segs);
            if (ev.HasValue)
            {
                break;
            }

            t += period;
            double maxR = 0d;
            double minR = double.MaxValue;
            for (int i = 0; i < segs.Count; i++)
            {
                planet.EvaluateWorldState(segs[i].T0, out Vector3d bp, out _);
                double r = (segs[i].Y0.Position - bp).Magnitude;
                if (r > maxR) maxR = r;
                if (r < minR) minR = r;
            }

            double e = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, t);
            if (maxR > prevMax + 10d || minR > prevMin + 10d || e > prevE + 1e-6d * Math.Abs(prevE))
            {
                mono = false;
            }

            prevMax = maxR;
            prevMin = minR;
            prevE = e;
        }

        double totalDrop = (prevE - e0) / Math.Abs(e0);
        bool cleanRegime = prevMin > planet.Radius;
        Check(mono && totalDrop < -1e-4d && cleanRegime, "T23 decay",
            string.Format("монотонность апо/пери/E (слаб 10м/1e-6): {0}; суммарно ΔE/E={1:E2} (<-1e-4 за 10 витков); финал апо={2:E6}м пери={3:E6}м (над R={4:E6}м: {5})",
                mono, totalDrop, prevMax, prevMin, planet.Radius, cleanRegime));
        return 0;
    }

    private static double RunCircularDecay(StarSystem sys, OrbitingBody planet, double altitudeMeters, double dirSign, int orbits, out double deltaEnergy)
    {
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        double mu = planet.ResolveStandardGravitationalParameter();
        double r = planet.Radius + altitudeMeters;
        double circV = Math.Sqrt(mu / r);
        double period = 2d * Math.PI * Math.Sqrt(r * r * r / mu);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new DragSource(planet));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, r, 0d), bv0 + new Vector3d(dirSign * circV, 0d, 0d), 1000d);
        double e0 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 0d);
        double t = 0d;
        for (int i = 0; i < orbits; i++)
        {
            EventOccurrence? ev = prop.Propagate(ship, t, period);
            if (ev.HasValue)
            {
                break;
            }

            t += period;
        }

        double e1 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, t);
        deltaEnergy = e1 - e0;
        return t;
    }

    private static int Test23_Retrograde()
    {
        // Rotating atmosphere in dynamics: +X against the wind, −X with it
        // (at point +Y the atmosphere moves −X). Losses ∝ v_rel²: ratio ≈
        // ((7800+473)/(7800−473))² ≈ 1.275. Tolerance [1.1, 1.5] — sign + rate.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = ThinAtmosphere();
        planet.RotationPeriodSeconds = 86400d;
        double dPlus = 0d;
        double dMinus = 0d;
        RunCircularDecay(sys, planet, 250000d, 1d, 5, out dPlus);
        RunCircularDecay(sys, planet, 250000d, -1d, 5, out dMinus);
        double ratio = dPlus / dMinus;
        Check(dPlus < 0d && dMinus < 0d && ratio > 1.1d && ratio < 1.5d, "T23 retrograde",
            string.Format("потери +X={0:E3} −X={1:E3} Дж/кг (обе <0); отношение={2:F3} (≈1.275 ∝v_rel², допуск [1.1,1.5])",
                dPlus, dMinus, ratio));
        return 0;
    }

    private static int Test23_TopCrossing()
    {
        // Top cutoff: force is continuous (ρ→0 from below, 0 above), state without
        // a jump, events [Entry, Touchdown] present. Standard dense atmosphere,
        // steep fall from 200 km, 1 s sample: |Δv| ≤ 25 m/s throughout.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var drag = new DragSource(planet);
        DynamicsContribution above = drag.Evaluate(
            bp0 + new Vector3d(planet.Radius + 100010d, 0d, 0d), bv0 + new Vector3d(0d, -2500d, 0d), 1000d, 0d);
        DynamicsContribution below = drag.Evaluate(
            bp0 + new Vector3d(planet.Radius + 99990d, 0d, 0d), bv0 + new Vector3d(0d, -2500d, 0d), 1000d, 0d);
        bool forceGate = above.Force.SqrMagnitude == 0d && below.Force.SqrMagnitude > 0d && below.Force.IsFinite;

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new DragSource(planet));
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var ship = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d), bv0 + new Vector3d(0d, -1000d, 0d), 1000d);
        // Continuity is measured only in a ±30 s window around the Top crossing:
        // near the ground braking is thousands of g, which is also physics, not a
        // force jump.
        var traceT = new List<double>();
        var traceV = new List<Vector3d>();
        var traceAlt = new List<double>();
        double t = 0d;
        while (t < 900d - 1e-9d)
        {
            phys.Step(ship, t, 1d);
            t += 1d;
            if (!ship.Position.IsFinite || !ship.Velocity.IsFinite)
            {
                break;
            }

            planet.EvaluateWorldState(t, out Vector3d bp, out _);
            double alt = (ship.Position - bp).Magnitude - planet.Radius;
            traceT.Add(t);
            traceV.Add(ship.Velocity);
            traceAlt.Add(alt);
            if (alt <= 0d)
            {
                break;
            }
        }

        double crossT = double.NaN;
        for (int i = 1; i < traceAlt.Count; i++)
        {
            if (traceAlt[i - 1] > 100000d && traceAlt[i] <= 100000d)
            {
                crossT = traceT[i];
            }
        }

        // ±3 s window: exactly the force switch-on zone (drag 0→~1 m/s² + g 9.4).
        // Not wider: a 2.5 km/s vertical fall is already deep in after 30 s.
        double maxDv = 0d;
        for (int i = 1; i < traceT.Count; i++)
        {
            if (Math.Abs(traceT[i] - crossT) <= 3d)
            {
                double dv = (traceV[i] - traceV[i - 1]).Magnitude;
                if (dv > maxDv) maxDv = dv;
            }
        }

        // Events via a separate clean propagator run (the stepping loop above is
        // only for velocity continuity; detection below uses the standard path).
        var phys2 = new SpacecraftPhysics();
        phys2.Sources.Add(new CachedGravitySource(sys));
        phys2.Sources.Add(new DragSource(planet));
        var prop2 = new EventDrivenPropagator(phys2);
        prop2.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        prop2.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var ship2 = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d), bv0 + new Vector3d(0d, -1000d, 0d), 1000d);
        var found = new List<string>();
        double t2 = 0d;
        while (t2 < 900d - 1e-9d && found.Count < 2)
        {
            EventOccurrence? ev = prop2.Propagate(ship2, t2, 900d - t2);
            if (!ev.HasValue)
            {
                break;
            }

            found.Add(ev.Value.DetectorName);
            t2 = ev.Value.TimeSeconds;
            if (ev.Value.Kind == EventKind.Touchdown)
            {
                break;
            }
        }

        bool order = found.Count == 2
            && found[0].StartsWith("AtmosphereEntry") && found[1].StartsWith("Touchdown");
        Check(forceGate && maxDv < 25d && order, "T23 top-crossing",
            string.Format("сила Top±10м: 0/снизу {0:E2}Н: {1}; макс |Δv|/1с={2:F2}м/с (<25); события [Entry,Touchdown]: {3}",
                below.Force.Magnitude, forceGate, maxDv, order));
        return 0;
    }

    private static StarSystem TwoBodySystem()
    {
        // Star + one planet without a moon: third-body work (~9% of the drag rate
        // over a 60 s window) is excluded by construction, leaving only the star's
        // tide ~0.1%. For a rate baseline, not general physics.
        var star = new OrbitingBody
        {
            Name = "Звезда",
            StandardGravitationalParameter = 1.327e20d,
            EpochTimeSeconds = 0d
        };
        var planet = new OrbitingBody
        {
            Name = "Планета",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            SemiMajorAxis = 1.5e11d,
            Eccentricity = 0d,
            InclinationDegrees = 0d,
            LongitudeOfAscendingNodeDegrees = 0d,
            ArgumentOfPeriapsisDegrees = 0d,
            MeanAnomalyAtEpochDegrees = 0d,
            EpochTimeSeconds = 0d
        };
        planet.Parent = star;
        star.Children.Add(planet);
        return new StarSystem(star);
    }

    private static int Test23_RateBaseline()
    {
        // Rate baseline: measured dE/dt over 60 s against the analytic −½ρCdAv³/m
        // (circular 200 km, non-rotating planet, thin atmosphere, two-body system).
        // Tolerance 5%.
        StarSystem sys = TwoBodySystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = ThinAtmosphere();
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        double mu = planet.ResolveStandardGravitationalParameter();
        double r = planet.Radius + 200000d;
        double circV = Math.Sqrt(mu / r);
        double rho = 1e-6d * Math.Exp(-200000d / 30000d);
        double analytic = -0.5d * rho * 1d * 10d * circV * circV * circV / 1000d;

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new DragSource(planet));
        var ship = new Spacecraft(bp0 + new Vector3d(0d, r, 0d), bv0 + new Vector3d(circV, 0d, 0d), 1000d);
        double e0 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 0d);
        phys.Step(ship, 0d, 60d);
        double e1 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 60d);
        double measured = (e1 - e0) / 60d;
        double relErr = Math.Abs(measured / analytic - 1d);
        Check(relErr < 0.05d, "T23 rate-baseline",
            string.Format("dE/dt измерено={0:E4} аналитика={1:E4} Дж/кг/с (Δ={2:E2}, допуск 5%)", measured, analytic, relErr));
        return 0;
    }

    private static int Test24_History()
    {
        // Invariant 1: history only reads the track; a bare run and a run with
        // history give bit-identical state/events.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d fallP = bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d);
        Vector3d fallV = bv0 + new Vector3d(-3000d, 0d, 0d);

        var physBare = new SpacecraftPhysics();
        physBare.Sources.Add(new CachedGravitySource(sys));
        var propBare = new EventDrivenPropagator(physBare);
        propBare.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipBare = new Spacecraft(fallP, fallV, 1000d);
        EventOccurrence? evBare = propBare.Propagate(shipBare, 0d, 30d * 86400d);

        var physHist = new SpacecraftPhysics();
        physHist.Sources.Add(new CachedGravitySource(sys));
        var propHist = new EventDrivenPropagator(physHist);
        propHist.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipHist = new Spacecraft(fallP, fallV, 1000d);
        var track = new List<DenseSegment>();
        EventOccurrence? evHist = propHist.PropagateWithSegments(shipHist, 0d, 30d * 86400d, track);
        var history = new TrajectoryHistory();
        var evTimes = new List<double>();
        if (evHist.HasValue)
        {
            evTimes.Add(evHist.Value.TimeSeconds);
        }

        HistoryBuilder.BuildFromTrack(history, track, 0d, evHist.Value.TimeSeconds, 30d, evTimes);
        bool independent = shipBare.Position.X == shipHist.Position.X && shipBare.Position.Y == shipHist.Position.Y
            && shipBare.Position.Z == shipHist.Position.Z
            && shipBare.Velocity.X == shipHist.Velocity.X && shipBare.Velocity.Y == shipHist.Velocity.Y
            && shipBare.Velocity.Z == shipHist.Velocity.Z && shipBare.Mass == shipHist.Mass
            && evBare.Value.TimeSeconds == evHist.Value.TimeSeconds;

        // Invariant 2: the time grid does not depend on chunk slicing (T11 dip,
        // 10 s slices vs a single call; stride 60).
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        Vector3d dipP = bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d);
        Vector3d dipV = bv0 + new Vector3d(500d, 2000d, 0d);
        var histA = RunDipHistory(sys, planet, dipP, dipV, 10d);
        var histB = RunDipHistory(sys, planet, dipP, dipV, 600d);
        bool gridEqual = histA.Count == histB.Count;
        double maxEventDt = 0d;
        double maxValDiff = 0d;
        if (gridEqual)
        {
            for (int i = 0; i < histA.Count; i++)
            {
                if (histA[i].Kind == HistorySampleKind.Regular)
                {
                    if (histA[i].TimeSeconds != histB[i].TimeSeconds || histA[i].Kind != histB[i].Kind)
                    {
                        gridEqual = false;
                    }
                }
                else
                {
                    double dt = Math.Abs(histA[i].TimeSeconds - histB[i].TimeSeconds);
                    if (dt > maxEventDt) maxEventDt = dt;
                    if (histA[i].Kind != histB[i].Kind)
                    {
                        gridEqual = false;
                    }
                }

                double dp = (histA[i].State.Position - histB[i].State.Position).Magnitude;
                if (dp > maxValDiff) maxValDiff = dp;
            }
        }

        bool deterministic = gridEqual && maxEventDt < 1e-3d && maxValDiff < 10d;

        // Invariant 3: coarse stride 120 — events 26.7/407 fall between grid nodes
        // but are kept with exact times plus boundaries 0/600.
        var histC = RunDipHistory(sys, planet, dipP, dipV, 10d, 120d);
        bool preserved = histC.Count == 8
            && histC[0].TimeSeconds == 0d && histC[0].Kind == HistorySampleKind.Boundary
            && histC[histC.Count - 1].TimeSeconds == 600d && histC[histC.Count - 1].Kind == HistorySampleKind.Boundary;
        int eventCount = 0;
        for (int i = 0; i < histC.Count; i++)
        {
            if (histC[i].Kind == HistorySampleKind.Event)
            {
                eventCount++;
            }
        }

        preserved = preserved && eventCount == 2;

        // Merge unit on a synthetic track: an event exactly on a grid node (4.0),
        // nearly on the boundary (10−5e-10) and inside (5.0); priority is Event.
        var zero = SpacecraftIntegrationState.Zero;
        var coeffs = new ShampineCoeffs(zero, zero, zero, zero);
        var synth = new List<DenseSegment>();
        synth.Add(new DenseSegment(0d, 10d, 10d,
            new SpacecraftIntegrationState { Position = new Vector3d(0d, 0d, 0d), Velocity = new Vector3d(1d, 0d, 0d), Mass = 100d },
            new SpacecraftIntegrationState { Position = new Vector3d(10d, 0d, 0d), Velocity = new Vector3d(1d, 0d, 0d), Mass = 100d },
            coeffs));
        var hu = new TrajectoryHistory();
        var unitEvents = new List<double>();
        unitEvents.Add(4d);
        unitEvents.Add(10d - 5e-10d);
        unitEvents.Add(5d);
        HistoryBuilder.BuildFromTrack(hu, synth, 0d, 10d, 2d, unitEvents);
        bool unit = hu.Samples.Count == 7
            && hu.Samples[0].Kind == HistorySampleKind.Boundary && hu.Samples[0].TimeSeconds == 0d
            && hu.Samples[2].Kind == HistorySampleKind.Event && hu.Samples[2].TimeSeconds == 4d
            && hu.Samples[6].Kind == HistorySampleKind.Event;

        Check(independent && deterministic && preserved && unit, "T24 history",
            string.Format("независимость бит-в-бит: {0}; сетка времён равна: {1} (события Δt≤{2:E2}с, значения Δ≤{3:E2}м); preserve 8 сэмплов/2 события/границы: {4}; юнит слияния 7 шт: {5}",
                independent, gridEqual, maxEventDt, maxValDiff, preserved, unit));
        return 0;
    }

    private static List<HistorySample> RunDipHistory(StarSystem sys, OrbitingBody planet, Vector3d p0, Vector3d v0, double sliceSeconds)
    {
        return RunDipHistory(sys, planet, p0, v0, sliceSeconds, 60d);
    }

    private static List<HistorySample> RunDipHistory(StarSystem sys, OrbitingBody planet, Vector3d p0, Vector3d v0, double sliceSeconds, double strideSeconds)
    {
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        var ship = new Spacecraft(p0, v0, 1000d);
        var track = new List<DenseSegment>();
        var events = new List<double>();
        double t = 0d;
        while (t < 600d - 1e-9d)
        {
            double frameEnd = Math.Min(t + sliceSeconds, 600d);
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, frameEnd - t, track);
            if (!ev.HasValue)
            {
                t = frameEnd;
                continue;
            }

            events.Add(ev.Value.TimeSeconds);
            t = ev.Value.TimeSeconds;
        }

        var history = new TrajectoryHistory();
        HistoryBuilder.BuildFromTrack(history, track, 0d, 600d, strideSeconds, events);
        var out_ = new List<HistorySample>();
        for (int i = 0; i < history.Samples.Count; i++)
        {
            out_.Add(history.Samples[i]);
        }

        return out_;
    }

    private static int Test25_Lambert()
    {
        // (a) Hohmann exactly 180°: r1=7e6, r2=−14e6, dt=half-period a_t=10.5e6.
        // |v1| is the exact vis-viva, v1⊥r1, arrival is KeplerAdvance to r2.
        double mu = 3.986e14d;
        var r1 = new Vector3d(7e6d, 0d, 0d);
        var r2 = new Vector3d(-14e6d, 0d, 0d);
        double semiT = 10.5e6d;
        double dtH = Math.PI * Math.Sqrt(semiT * semiT * semiT / mu);
        LambertSolution hohmann = LambertSolver.Solve(r1, r2, dtH, mu, false);
        double expectV1 = Math.Sqrt(mu * ((2d / 7e6d) - (1d / semiT)));
        KeplerPredictor.Advance(r1, hohmann.DepartureVelocity, mu, dtH, out Vector3d arrP, out _);
        bool hohmannOk = Math.Abs(hohmann.DepartureVelocity.Magnitude / expectV1 - 1d) < 1e-9d
            && Math.Abs(Vector3d.Dot(hohmann.DepartureVelocity, r1)) < 1e-6d
            && (arrP - r2).Magnitude < 1d;

        // (b) Self-consistency 90° + long way: both arrive.
        var q1 = new Vector3d(7e6d, 0d, 0d);
        var q2 = new Vector3d(0d, 10e6d, 0d);
        LambertSolution shortWay = LambertSolver.Solve(q1, q2, 4000d, mu, false);
        LambertSolution longWay = LambertSolver.Solve(q1, q2, 20000d, mu, true);
        KeplerPredictor.Advance(q1, shortWay.DepartureVelocity, mu, 4000d, out Vector3d arrS, out _);
        KeplerPredictor.Advance(q1, longWay.DepartureVelocity, mu, 20000d, out Vector3d arrL, out _);
        bool chordsOk = (arrS - q2).Magnitude < 1d && (arrL - q2).Magnitude < 1d;

        // (c) Guards: dt≤0, zero vector, coincident points.
        bool guards = false;
        try
        {
            LambertSolver.Solve(q1, q2, 0d, mu, false);
        }
        catch (ArgumentOutOfRangeException)
        {
            try
            {
                LambertSolver.Solve(Vector3d.Zero, q2, 1000d, mu, false);
            }
            catch (ArgumentException)
            {
                try
                {
                    LambertSolver.Solve(q1, q1, 1000d, mu, false);
                }
                catch (ArgumentException)
                {
                    guards = true;
                }
            }
        }

        // (d) Porkchop planetA→planetB: table free of NaN, best finite and minimal,
        // bit-for-bit determinism.
        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        OrbitingBody planetA = sys.AllBodies[1];
        OrbitingBody planetB = sys.AllBodies[3];
        PorkchopCell[,] table1 = Porkchop.Scan(sys, star, planetA, planetB, 0d, 30d * 86400d, 4, 40d * 86400d, 100d * 86400d, 4);
        PorkchopCell[,] table2 = Porkchop.Scan(sys, star, planetA, planetB, 0d, 30d * 86400d, 4, 40d * 86400d, 100d * 86400d, 4);
        bool tableOk = true;
        bool deterministic = true;
        double best = double.PositiveInfinity;
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (double.IsNaN(table1[i, j].DeltaV))
                {
                    tableOk = false;
                }

                if (table1[i, j].DeltaV != table2[i, j].DeltaV || table1[i, j].Valid != table2[i, j].Valid)
                {
                    deterministic = false;
                }

                if (table1[i, j].Valid && table1[i, j].DeltaV < best)
                {
                    best = table1[i, j].DeltaV;
                }
            }
        }

        bool found = Porkchop.TryBest(table1, out int bi, out int bj);
        bool bestOk = found && table1[bi, bj].DeltaV == best;

        // (e) Kepler predictor vs numerical integrator: circular, 1 orbit.
        StarSystem sys2 = TwoBodySystem();
        OrbitingBody planet2 = sys2.AllBodies[1];
        planet2.EvaluateWorldState(0d, out Vector3d pp, out Vector3d pv);
        double mu2 = planet2.ResolveStandardGravitationalParameter();
        double rCirc = 8e6d;
        double vCirc = Math.Sqrt(mu2 / rCirc);
        double period = 2d * Math.PI * Math.Sqrt(rCirc * rCirc * rCirc / mu2);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys2));
        var ship = new Spacecraft(pp + new Vector3d(0d, rCirc, 0d), pv + new Vector3d(vCirc, 0d, 0d), 1000d);
        phys.Step(ship, 0d, period);
        planet2.EvaluateWorldState(period, out Vector3d pp1, out Vector3d pv1);
        KeplerPredictor.Advance(
            new Vector3d(0d, rCirc, 0d), new Vector3d(vCirc, 0d, 0d), mu2, period,
            out Vector3d kpP, out _);
        // Numerical ship in the world frame vs prediction relative to the planet:
        // compared relative to the body at the end. Bound 50 m, not 1 m: the
        // numerical one feels the star's tide (~16 m per orbit), the prediction is
        // pure two-body. This is an honest model bound, not integrator noise.
        double numericVsKepler = ((ship.Position - pp1) - kpP).Magnitude;

        Check(hohmannOk && chordsOk && guards && tableOk && deterministic && bestOk && numericVsKepler < 50d, "T25 lambert",
            string.Format("гоман |v1|Δ={0:E2} ⊥={1:E2} прибытие={2:E2}м; хорды {3:E2}/{4:E2}м; стражи: {5}; porkchop best={6:F0}м/с мин: {7} детерм: {8}; kepler-vs-число={9:E2}м",
                Math.Abs(hohmann.DepartureVelocity.Magnitude / expectV1 - 1d),
                Math.Abs(Vector3d.Dot(hohmann.DepartureVelocity, r1)),
                (arrP - r2).Magnitude,
                (arrS - q2).Magnitude, (arrL - q2).Magnitude, guards, best, bestOk, deterministic, numericVsKepler));
        return 0;
    }

    private static SpacecraftPhysics FlightPhysics(StarSystem sys, bool withDrag, OrbitingBody dragBody)
    {
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        if (withDrag)
        {
            phys.Sources.Add(new DragSource(dragBody));
        }

        return phys;
    }

    private static int Test27_Impulsive()
    {
        // P1c.1: porkchop→Lambert→validator pipeline; impact invalid;
        // determinism; drag seen by the validator.
        // (a) planetA(t=5d) → planetB(t=45d), best 3×3 cell.
        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        OrbitingBody planetA = sys.AllBodies[1];
        OrbitingBody planetB = sys.AllBodies[3];
        PorkchopCell[,] table = Porkchop.Scan(sys, star, planetA, planetB,
            5d * 86400d, 10d * 86400d, 3, 40d * 86400d, 50d * 86400d, 3);
        bool hasBest = Porkchop.TryBest(table, out int bi, out int bj);
        PorkchopCell best = table[bi, bj];
        double muStar = star.ResolveStandardGravitationalParameter();
        // Interplanetary pipeline: Lambert centre→centre (like the planner),
        // departure from the SOI edge at v∞. patched-conics accuracy class:
        // end-to-end execution, not precision (the lunar case below covers that).
        // The 5e9d bound is only a gross guard against garbage.
        double tD = best.DepartTimeSeconds;
        double tA = best.ArriveTimeSeconds;
        planetA.EvaluateWorldState(tD, out Vector3d depP, out Vector3d depV);
        star.EvaluateWorldState(tD, out Vector3d scD, out Vector3d scvD);
        planetB.EvaluateWorldState(tA, out Vector3d arrP, out _);
        star.EvaluateWorldState(tA, out Vector3d scA, out _);
        LambertSolution lam = LambertSolver.Solve(depP - scD, arrP - scA, tA - tD, muStar, false);
        Vector3d vinf = lam.DepartureVelocity - (depV - scvD);
        Vector3d dir0 = vinf.Normalized;
        double soiA = planetA.SphereOfInfluenceRadius;
        Vector3d escPos = depP + (dir0 * soiA);
        Vector3d escVel = depV + vinf;
        var flightPhys = FlightPhysics(sys, false, planetA);
        var flightProp = new EventDrivenPropagator(flightPhys);
        var departState = new SpacecraftIntegrationState { Position = escPos, Velocity = escVel, Mass = 1000d };
        ValidationResult val = TrajectoryValidator.ValidateImpulsive(sys, flightPhys, flightProp,
            departState, tD, escVel, planetB, tA, 5e9d);

        // Lunar precision: parking 8e6 m → Moon in 3 days, Lambert in the planet
        // frame (where two-body is nearly exact), target is the Moon itself.
        OrbitingBody moon = sys.AllBodies[2];
        double muA = planetA.ResolveStandardGravitationalParameter();
        double parkR = planetA.Radius + 8e6d;
        double parkV = Math.Sqrt(muA / parkR);
        double tD2 = 86400d;
        double tA2 = tD2 + (3d * 86400d);
        planetA.EvaluateWorldState(tD2, out Vector3d apD, out Vector3d avD);
        moon.EvaluateWorldState(tA2, out Vector3d mpA, out _);
        planetA.EvaluateWorldState(tA2, out Vector3d apA, out _);
        Vector3d shipP0 = apD + new Vector3d(0d, parkR, 0d);
        LambertSolution lamM = LambertSolver.Solve(shipP0 - apD, mpA - apA, tA2 - tD2, muA, false);
        var moonPhys = FlightPhysics(sys, false, planetA);
        var moonProp = new EventDrivenPropagator(moonPhys);
        moonProp.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planetA));
        moonProp.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(moon));
        var moonState = new SpacecraftIntegrationState { Position = shipP0, Velocity = avD + lamM.DepartureVelocity, Mass = 1000d };
        ValidationResult moonVal = TrajectoryValidator.ValidateImpulsive(sys, moonPhys, moonProp,
            moonState, tD2, avD + lamM.DepartureVelocity, moon, tA2, 1e6d);
        // A direct hit on the target is also an arrival (within the body), not a
        // miss: touching the TARGET 8.8 ks before tA (gravity focusing pulled it
        // earlier). Distinguished from hitting a NON-target (T27b), invalid there.
        bool moonImpact = false;
        for (int i = 0; i < moonVal.Events.Count; i++)
        {
            if (moonVal.Events[i].Kind == EventKind.Touchdown && ReferenceEquals(moonVal.Events[i].Body, moon))
            {
                moonImpact = true;
            }
        }

        bool moonOk = (moonVal.Valid && moonVal.ArrivalPositionErrorMeters < 1e6d) || moonImpact;

        // (b) Impact invalid: radially down from 8e6 m, touchdown en route.
        // The real sample ship does not mutate bit-for-bit.
        planetA.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var sample = new Spacecraft(bp0 + new Vector3d(0d, planetA.Radius + 8e6d, 0d), bv0 + new Vector3d(0d, -3000d, 0d), 1000d);
        Vector3d sampleP0 = sample.Position;
        Vector3d sampleV0 = sample.Velocity;
        var killPhys = FlightPhysics(sys, false, planetA);
        var killProp = new EventDrivenPropagator(killPhys);
        killProp.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planetA));
        var killState = new SpacecraftIntegrationState { Position = sample.Position, Velocity = sample.Velocity, Mass = sample.Mass };
        ValidationResult kill = TrajectoryValidator.ValidateImpulsive(sys, killPhys, killProp,
            killState, 0d, sample.Velocity, planetA, 3600d, 1e6d);
        bool untouched = sample.Position.X == sampleP0.X && sample.Position.Y == sampleP0.Y && sample.Position.Z == sampleP0.Z
            && sample.Velocity.X == sampleV0.X && sample.Velocity.Y == sampleV0.Y && sample.Velocity.Z == sampleV0.Z;
        bool killSawTouchdown = false;
        for (int i = 0; i < kill.Events.Count; i++)
        {
            if (kill.Events[i].Kind == EventKind.Touchdown)
            {
                killSawTouchdown = true;
            }
        }

        // (c) Determinism: repeat of the lunar validation — bit-for-bit.
        var moonPhys2 = FlightPhysics(sys, false, planetA);
        var moonProp2 = new EventDrivenPropagator(moonPhys2);
        moonProp2.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planetA));
        moonProp2.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(moon));
        ValidationResult moonVal2 = TrajectoryValidator.ValidateImpulsive(sys, moonPhys2, moonProp2,
            moonState, tD2, avD + lamM.DepartureVelocity, moon, tA2, 1e6d);
        bool deterministic = moonVal.Valid == moonVal2.Valid
            && moonVal.ArrivalPositionErrorMeters == moonVal2.ArrivalPositionErrorMeters
            && moonVal.MinClearanceMeters == moonVal2.MinClearanceMeters;

        // (d) Drag is visible: a 95 km dip (T22 ICs), target planetB is far away
        // (huge tolerance; the DIFFERENCE in misses with/without drag matters).
        planetA.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        var dipState = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, planetA.Radius + 95000d, 0d),
            Velocity = bv0 + new Vector3d(3000d, 800d, 0d),
            Mass = 1000d
        };
        // 150 s comparison window: both still in the air (entry ~10 s, return
        // ~200 s), the divergence has accumulated (~200 m), no underground chaos.
        var cleanPhys = FlightPhysics(sys, false, planetA);
        var cleanProp = new EventDrivenPropagator(cleanPhys);
        ValidationResult clean = TrajectoryValidator.ValidateImpulsive(sys, cleanPhys, cleanProp,
            dipState, 0d, dipState.Velocity, planetB, 150d, 1e12d);
        var dragPhys = FlightPhysics(sys, true, planetA);
        var dragProp = new EventDrivenPropagator(dragPhys);
        ValidationResult draggy = TrajectoryValidator.ValidateImpulsive(sys, dragPhys, dragProp,
            dipState, 0d, dipState.Velocity, planetB, 150d, 1e12d);
        double dragExcess = draggy.ArrivalPositionErrorMeters - clean.ArrivalPositionErrorMeters;

        Check(hasBest && val.Valid && moonOk && !kill.Valid && killSawTouchdown && untouched && deterministic && dragExcess > 50d, "T27 impulsive",
            string.Format("межпланет valid={0} промах={1:E2}м (класс patched-conics); луна ok={2} (rendezvous {3:E2}м / прямое попадание {4}); удар invalid={5} touchdown={6} образец цел={7}; детерм={8}; drag-избыток={9:E2}м",
                val.Valid, val.ArrivalPositionErrorMeters, moonOk, moonVal.ArrivalPositionErrorMeters, moonImpact,
                !kill.Valid, killSawTouchdown, untouched, deterministic, dragExcess));
        return 0;
    }

    private static int Test28_FiniteBurn()
    {
        // P1c.2: dry=live (determinism, NOT physics); fuel budget;
        // gravity losses from a pair with equal Δv / thrust×10, direction fixed
        // inertially (otherwise steering loss would mix in).
        StarSystem sys = TestSystem();
        OrbitingBody planetA = sys.AllBodies[1];
        double muA = planetA.ResolveStandardGravitationalParameter();
        double parkR = planetA.Radius + 8e6d;
        double parkV = Math.Sqrt(muA / parkR);
        planetA.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d parkP0 = bp0 + new Vector3d(0d, parkR, 0d);
        Vector3d parkV0 = bv0 + new Vector3d(parkV, 0d, 0d);
        var preset = new BurnTolerancePreset(1e-2d, 1e-5d, 1e-4d, 1e-10d);

        // (a) Dry-run determinism (honest label: not a proof of physics).
        var thrustA = new ThrustSource(20000d, 4000d, new ConstantIsp(300d));
        thrustA.ThrottleAt = (time) => time >= 100d && time < 110d ? 1d : 0d;
        var physA = FlightPhysics(sys, false, planetA);
        physA.Sources.Add(thrustA);
        WarpController warpA;
        ManeuverEvaluator evalA = BuildEvaluator(physA, 4000d, out warpA);
        var liveShip = new Spacecraft(parkP0, parkV0, 5000d);
        ManeuverResult live = evalA.CoastThenBurn(liveShip, 0d, 100d, 110d, preset, 1d);
        var cloneShip = new Spacecraft(parkP0, parkV0, 5000d);
        ManeuverResult dry = evalA.CoastThenBurn(cloneShip, 0d, 100d, 110d, preset, 1d);
        bool deterministic = live.EndState.Position.X == dry.EndState.Position.X
            && live.EndState.Position.Y == dry.EndState.Position.Y
            && live.EndState.Position.Z == dry.EndState.Position.Z
            && live.EndState.Velocity.X == dry.EndState.Velocity.X
            && live.EndState.Velocity.Y == dry.EndState.Velocity.Y
            && live.EndState.Velocity.Z == dry.EndState.Velocity.Z
            && live.EndState.Mass == dry.EndState.Mass;

        // (b) Overuse: 10 kg tank, a 30 s window needs ~20 kg → depletion, invalid.
        var thrustB = new ThrustSource(2000d, 4990d, new ConstantIsp(300d));
        thrustB.ThrottleAt = (time) => time >= 100d && time < 130d ? 1d : 0d;
        var physB = FlightPhysics(sys, false, planetA);
        physB.Sources.Add(thrustB);
        WarpController warpB;
        ManeuverEvaluator evalB = BuildEvaluator(physB, 4990d, out warpB);
        BurnValidationResult over = TrajectoryValidator.ValidateFiniteBurn(evalB,
            new SpacecraftIntegrationState { Position = parkP0, Velocity = parkV0, Mass = 5000d },
            0d, 100d, 130d, preset, 1d,
            parkP0, parkV0, 1e9d, 1e9d, 10d, 1.0d);
        bool fuel = !over.Valid && !over.CompletedBurn
            && over.StoppingEvent.HasValue && over.StoppingEvent.Value.Kind == EventKind.PropellantDepleted
            && Math.Abs(over.FuelUsedKg - 10d) < 1e-4d;

        // (c) Gravity losses: Δv=1500 m/s, thrust 60000 N vs 6000 N (burn ~157 s
        // vs ~1570 s), direction is the inertial prograde vector at ignition.
        // Validator flags: tolerance below the loss → invalid, wider → valid.
        double isp = 300d;
        double g0 = ThrustSource.StandardGravity;
        double m0 = 8000d;
        double fuelNeed = m0 * (1d - Math.Exp(-1500d / (isp * g0)));
        double tShort = fuelNeed / (60000d / (isp * g0));
        double tLong = fuelNeed / (6000d / (isp * g0));
        BurnRig rigS = BuildBurnRig(sys, planetA, m0, 60000d, isp, tShort);
        BurnRig rigL = BuildBurnRig(sys, planetA, m0, 6000d, isp, tLong);
        double lossShort = RunProgradeBurn(sys, planetA, rigS, parkP0, parkV0, preset,
            out Vector3d idealPS, out Vector3d idealVS, out SpacecraftIntegrationState endS, out double fuelS);
        double lossLong = RunProgradeBurn(sys, planetA, rigL, parkP0, parkV0, preset,
            out _, out _, out _, out _);
        var startState = new SpacecraftIntegrationState { Position = parkP0, Velocity = parkV0, Mass = m0 };
        BurnValidationResult flagTight = TrajectoryValidator.ValidateFiniteBurn(rigS.Eval,
            startState, 0d, 100d, 100d + tShort, preset, 1d,
            idealPS, idealVS, 1e9d, 0.5d, fuelNeed, 100d);
        BurnValidationResult flagLoose = TrajectoryValidator.ValidateFiniteBurn(rigS.Eval,
            startState, 0d, 100d, 100d + tShort, preset, 1d,
            idealPS, idealVS, 1e9d, 10d, fuelNeed, 100d);
        bool flags = !flagTight.Valid && flagLoose.Valid;
        bool losses = lossLong > lossShort * 10d && lossShort > 0.02d;

        Check(deterministic && fuel && losses && flags, "T28 finite-burn",
            string.Format("сухой=живой бит-в-бит: {0}; перерасход invalid+depletion топливо={1:F3}кг: {2}; loss short={3:F3}м/с long={4:F2}м/с (>10×,>0.02): {5}; флаг tight-invalid/loose-valid: {6}",
                deterministic, over.FuelUsedKg, fuel, lossShort, lossLong, losses, flags));
        return 0;
    }

    private static int Test32_Escape()
    {
        // V3: escape from the star over 10 years (v∞=27 km/s, ~57 AU). v∞ is
        // conserved (no hidden energy sources/sinks in the cruiser), arrival is
        // checked against a hyperbolic Kepler (planet perturbations measured, not ignored).
        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        double muStar = star.ResolveStandardGravitationalParameter();
        double span = 10d * 365d * 86400d;
        Vector3d r0 = new Vector3d(-1.5e11d, 0d, 0d);
        Vector3d v0 = new Vector3d(0d, -50000d, 0d);
        double vinfSq0 = v0.SqrMagnitude - (2d * muStar / r0.Magnitude);

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(r0, v0, 1000d);
        double t = 0d;
        while (t < span - 1e-9d)
        {
            double frameEnd = Math.Min(t + 86400d, span);
            EventOccurrence? ev = prop.Propagate(ship, t, frameEnd - t);
            if (ev.HasValue)
            {
                break;
            }

            t = frameEnd;
        }

        double vinfSq1 = ship.Velocity.SqrMagnitude - (2d * muStar / ship.Position.Magnitude);
        double vinfDrift = Math.Abs(vinfSq1 / vinfSq0 - 1d);
        KeplerPredictor.Advance(r0, v0, muStar, span, out Vector3d kpP, out _);
        double keplerErr = (kpP - ship.Position).Magnitude;
        Check(t >= span - 1e-9d && ship.Position.IsFinite && ship.Velocity.IsFinite
            && ship.Position.Magnitude < 2e13d && vinfDrift < 1e-4d && keplerErr < 1e9d, "T32 escape",
            string.Format("дошёл 10 лет: {0}; |pos|={1:E3}м (<2e13); v∞² дрейф={2:E2} (<1e-4); kepler-прибытие Δ={3:E2}м (<1e9)",
                t >= span - 1e-9d, ship.Position.Magnitude, vinfDrift, keplerErr));
        return 0;
    }

    private static int Test34_GoldenState()
    {
        // Golden-master of the integrator basis (risk mitigation, not a correctness
        // proof — that is T13/Tsiolkovsky): fixed input, one SpacecraftPhysics.Step,
        // bitwise Position/Velocity/Mass reference plus the RHS call count.
        // Values are from a green run; update the reference only by an explicit
        // decision, not by fitting. Independent anchor: the same arc by
        // KeplerPredictor within tolerance.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bodyPos, out Vector3d bodyVel);
        var phys = new SpacecraftPhysics();
        var counting = new CountingSource(new CachedGravitySource(sys));
        phys.Sources.Add(counting);
        var ship = new Spacecraft(
            bodyPos + new Vector3d(0d, planet.Radius + 200000d, 0d),
            bodyVel + new Vector3d(7500d, 0d, 0d),
            1000d);
        phys.Step(ship, 0d, 600d);
        // Golden from the exact per-body root-indirect build; A/B on Sun 1 AU:
        // old subtree form 23.66 m vs exact 0.0376 m. The independent Kepler
        // anchor still agrees to 1e-6, so the update is not fitting the test.
        bool bitwise =
            ship.Position.X == 147004123096.9961d &&
            ship.Position.Y == 23171251.600864597d &&
            ship.Position.Z == 0.00017879682654596798d &&
            ship.Velocity.X == 5629.710631722125d &&
            ship.Velocity.Y == 25176.110817733676d &&
            ship.Velocity.Z == 1.1635175826135667E-06d &&
            ship.Mass == 1000d &&
            counting.Calls == 121L;
        planet.EvaluateWorldState(600d, out Vector3d bodyEnd, out _);
        KeplerPredictor.Advance(
            new Vector3d(0d, planet.Radius + 200000d, 0d),
            new Vector3d(7500d, 0d, 0d),
            planet.StandardGravitationalParameter, 600d,
            out Vector3d keplerPos, out _);
        if (!bitwise)
        {
            Console.WriteLine("T34 actual: P="
                + ship.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + ship.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + ship.Position.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " V="
                + ship.Velocity.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + ship.Velocity.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + ship.Velocity.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " M=" + ship.Mass.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " calls=" + counting.Calls.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        double anchorErr = ((ship.Position - bodyEnd) - keplerPos).Magnitude / keplerPos.Magnitude;
        Check(bitwise && anchorErr < 1e-4d, "T34 golden-state",
            string.Format("бит-в-бит эталон + CALLS=121: {0}; якорь Кеплер relErr={1:E2} (<1e-4)",
                bitwise, anchorErr));
        return 0;
    }

    private static int Test39_SecularDrift()
    {
        // Step 0 + regression gate for the "rails forever / bake / PEFRL" decision:
        // 80 years with two steps (convergence must be << the effect) + 2000 years
        // without the moon for the secular planet-planet rate (the moon forces a
        // small step, 2000 years with it is infeasible).
        // Outer orbit μ = μ_parent_own + μ_subtree, with the N-body Jacobi
        // initialization placing the sub-tree barycentre on the rail.
        StarSystem sys = TestSystem();
        double[] posFine;
        double[] devFine = NBodyDeviations(sys, 80d, 900d, 1d, out posFine);
        double[] posCoarse;
        double[] devCoarse = NBodyDeviations(sys, 80d, 1800d, 1d, out posCoarse);
        // The tool's convergence must be orders of magnitude below the effect:
        // otherwise we measure integrator error, not physics.
        double worstConv = 0d;
        for (int i = 0; i < posFine.Length; i++)
        {
            double d = Math.Abs(posFine[i] - posCoarse[i]);
            if (d > worstConv) worstConv = d;
        }

        double worst80 = 0d;
        for (int i = 0; i < devFine.Length; i++)
        {
            if (devFine[i] > worst80) worst80 = devFine[i];
        }

        double[] posLong;
        StarSystem sysNoMoon = SystemWithoutMoon();
        double[] devLong = NBodyDeviations(sysNoMoon, 2000d, 14400d, 50d, out posLong);
        double worst2000 = 0d;
        for (int i = 0; i < devLong.Length; i++)
        {
            if (devLong[i] > worst2000) worst2000 = devLong[i];
        }

        // Tool self-check: repeat with the same step — bit-for-bit.
        double[] posRepeat;
        double[] devRepeat = NBodyDeviations(sys, 80d, 900d, 1d, out posRepeat);
        bool deterministic = true;
        for (int i = 0; i < devFine.Length; i++)
        {
            if (devRepeat[i] != devFine[i]) deterministic = false;
        }

        for (int i = 0; i < posFine.Length; i++)
        {
            if (posRepeat[i] != posFine[i]) deterministic = false;
        }

        string perBody = "";
        for (int i = 0; i < devFine.Length; i++)
        {
            perBody += string.Format("{0}={1:E2}м ", sys.AllBodies[i].Name, devFine[i]);
        }

        // Step 0 decision: the effect 1e9 m >> the 500 km threshold, so "rails
        // forever" is dead and bake is needed (200-500 years of fixed ephemeris).
        // The gate pins this: the effect must EXCEED the threshold, convergence must
        // be orders below. Upper bounds come from tool regression. Planet B without
        // the moon: 7.25e8 m ≈ the SOI/Hill order.
        Check(worst80 > 500000d && worst80 < 5000000000d && worstConv < 5000d && worst2000 > 20000000d && worst2000 < 100000000000d && deterministic, "T39 secular-drift",
            string.Format("80 лет: худш. {0:E2}м (в (500км, 5e9м): bake-решение) [{1}]; сходимость h/h2: {2:E2}м (<5км); 2000 лет: худш. {3:E2}м (в (20000км, 1e11м)); детерминизм: {4}",
                worst80, perBody, worstConv, worst2000, deterministic));
        return 0;
    }

    private static int Test36_HelioDrift()
    {
        // Long heliocentric: circular r=2.5e11 in TwoBodySystem (the planet
        // perturbation is real, ~2e-5 relative — included in the bound deliberately),
        // 5 years monthly (60 chunks). Metrics: ΔE/E in the star frame + Δr/r +
        // boundedness 0.5·r0<r<2·r0.
        StarSystem sys = TwoBodySystem();
        OrbitingBody star = sys.AllBodies[0];
        double mu = star.ResolveStandardGravitationalParameter();
        star.EvaluateWorldState(0d, out Vector3d starPos, out _);
        double r0 = 2.5e11d;
        double v0 = Math.Sqrt(mu / r0);
        double span = 5d * 365d * 86400d;
        int months = 60;
        double chunk = span / months;
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(starPos + new Vector3d(r0, 0d, 0d), new Vector3d(0d, v0, 0d), 1000d);
        double e0 = (0.5d * v0 * v0) - (mu / r0);
        double worstDe = 0d;
        double worstDr = 0d;
        double minR = double.MaxValue;
        double maxR = 0d;
        double t = 0d;
        for (int m = 0; m < months; m++)
        {
            var segs = new List<DenseSegment>();
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, chunk, segs);
            if (ev.HasValue)
            {
                break;
            }

            t += chunk;
            for (int i = 0; i < segs.Count; i++)
            {
                double r = (segs[i].Y0.Position - starPos).Magnitude;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
            }

            double rel = (ship.Position - starPos).Magnitude;
            if (rel < minR) minR = rel;
            if (rel > maxR) maxR = rel;
            double e = (0.5d * ship.Velocity.SqrMagnitude) - (mu / rel);
            double de = Math.Abs((e - e0) / e0);
            double dr = Math.Abs((rel - r0) / r0);
            if (de > worstDe) worstDe = de;
            if (dr > worstDr) worstDr = dr;
        }

        Check(worstDe < 1e-4d && worstDr < 1e-3d && minR > 0.5d * r0 && maxR < 2d * r0, "T36 helio-drift",
            string.Format("5 лет, 60 чанков: худш. ΔE/E={0:E2} (<1e-4), худш. Δr/r={1:E2} (<1e-3); радиус в [{2:E2},{3:E2}] при r0={4:E2}",
                worstDe, worstDr, minR, maxR, r0));
        return 0;
    }

    private static int Test37_LunarYears()
    {
        // Moon-perturbed multi-year: the same high circular orbit as T30
        // (R+2e7, prograde), but in the full system WITH the moon, 5 years monthly
        // (60 chunks). Same metrics as T30 (ΔE/E, Δapo via the true a(1+e)=r for a
        // circular orbit) + boundedness (no escape/fall).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        double mu = planet.ResolveStandardGravitationalParameter();
        double r = planet.Radius + 2e7d;
        double circV = Math.Sqrt(mu / r);
        double span = 5d * 365d * 86400d;
        int months = 60;
        double chunk = span / months;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, r, 0d), bv0 + new Vector3d(-circV, 0d, 0d), 1000d);
        double e0 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 0d);
        double worstDe = 0d;
        double worstDa = 0d;
        double minR = double.MaxValue;
        double maxR = 0d;
        double t = 0d;
        for (int m = 0; m < months; m++)
        {
            var segs = new List<DenseSegment>();
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, chunk, segs);
            if (ev.HasValue)
            {
                break;
            }

            t += chunk;
            for (int i = 0; i < segs.Count; i++)
            {
                planet.EvaluateWorldState(segs[i].T0, out Vector3d bp, out _);
                double rr = (segs[i].Y0.Position - bp).Magnitude;
                if (rr < minR) minR = rr;
                if (rr > maxR) maxR = rr;
            }

            double e = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, t);
            double de = Math.Abs((e - e0) / e0);
            double da = Math.Abs((maxR - r) / r);
            if (de > worstDe) worstDe = de;
            if (da > worstDa) worstDa = da;
        }

        // Cost of the moon as a number: ~27x the moonless T30, same integrator and
        // orbit, the difference is only physics.
        Check(worstDe < 5e-4d && worstDa < 3e-2d && minR > planet.Radius && maxR < 10d * r, "T37 lunar-years",
            string.Format("5 лет с Луной, 60 чанков: худш. ΔE/E={0:E2}, худш. Δапо={1:E2}; радиус в [{2:E2},{3:E2}] (R={4:E2})",
                worstDe, worstDa, minR, maxR, planet.Radius));
        return 0;
    }

    private static int Test38_LowDrag()
    {
        // Low drag orbits over long spans: circular 200 km, thin atmosphere (same
        // Cd=1/A=10/m=1000 as T23 — comparable), 6 orbits in chunks. Metrics as T23
        // (monotonicity with +10 m/1e-6 slacks) + periapsis > R every orbit + total
        // drop in a corridor around the analytic estimate (which understates, since
        // ρ grows as it descends).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = ThinAtmosphere();
        planet.RotationPeriodSeconds = 0d;
        double mu = planet.ResolveStandardGravitationalParameter();
        double r0 = planet.Radius + 200000d;
        double circV = Math.Sqrt(mu / r0);
        double period = 2d * Math.PI * Math.Sqrt(r0 * r0 * r0 / mu);
        double rho0 = 1e-6d * Math.Exp(-200000d / 30000d);
        double rate0 = 0.5d * rho0 * circV * circV * circV * 1d * 10d / 1000d;
        double analyticDrop = 6d * period * rate0;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new DragSource(planet));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, r0, 0d), bv0 + new Vector3d(circV, 0d, 0d), 1000d);
        double e0 = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, 0d);
        bool mono = true;
        double prevMax = double.MaxValue;
        double prevMin = double.MaxValue;
        double prevE = e0;
        bool crashed = false;
        double t = 0d;
        int doneOrbits = 0;
        double finalMinR = double.NaN;
        for (int orbit = 0; orbit < 6; orbit++)
        {
            var segs = new List<DenseSegment>();
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, period, segs);
            if (ev.HasValue)
            {
                break;
            }

            t += period;
            double maxR = 0d;
            double minR = double.MaxValue;
            for (int i = 0; i < segs.Count; i++)
            {
                planet.EvaluateWorldState(segs[i].T0, out Vector3d bp, out _);
                double r = (segs[i].Y0.Position - bp).Magnitude;
                if (r > maxR) maxR = r;
                if (r < minR) minR = r;
            }

            double e = PlanetRelativeEnergy(sys, planet, ship.Position, ship.Velocity, t);
            if (maxR > prevMax + 10d || minR > prevMin + 10d || e > prevE + 1e-6d * Math.Abs(prevE))
            {
                mono = false;
            }

            if (minR <= planet.Radius)
            {
                crashed = true;
                break;
            }

            doneOrbits++;
            finalMinR = minR;
            prevMax = maxR;
            prevMin = minR;
            prevE = e;
        }

        // Sign: the measured drop is negative (energy falls), analyticDrop is a
        // positive rate magnitude, so magnitudes are compared.
        double totalDrop = prevE - e0;
        double ratio = Math.Abs(totalDrop) / analyticDrop;
        Check(mono && !crashed && doneOrbits == 6 && finalMinR > planet.Radius + 50000d && ratio > 0.5d && ratio < 4d, "T38 low-drag",
            string.Format("6 витков 200км ({0} пройдено): монотонность {1}, без падения {2}, финальный перигей {3:E2}м (>R+50км); сброс {4:E2} Дж/кг vs аналитика {5:E2} (отношение {6:F2} в [0.5,4])",
                doneOrbits, mono, !crashed, finalMinR, totalDrop, analyticDrop, ratio));
        return 0;
    }

    private static int Test61_UnbakeableMoon()
    {
        // Moon with a 5.4 h period: a 2-day segment cannot fit 9 wrapped orbits, so
        // bake must refuse LOUDLY (and the estimator too), not emit aliased garbage.
        // Cascade: the planet carries the moon's ripple — also unbakeable; the star
        // is not counted (it is not baked).
        StarSystem sys = TwelveBodySystem();
        OrbitingBody mI1 = null;
        foreach (OrbitingBody b in sys.AllBodies)
        {
            if (b.Name == "I-1")
            {
                mI1 = b;
            }
        }

        mI1.SemiMajorAxis = 1.5e7; // period = 2π√(a³/μ_planet) ≈ 5.4 h

        bool estimateThrows = false;
        try
        {
            EphemerisBaker.EstimateBakeBytes(sys, 1024d, new BakeConfig());
        }
        catch (InvalidOperationException)
        {
            estimateThrows = true;
        }

        bool bakeThrows = false;
        string message = "";
        try
        {
            EphemerisBaker.Bake(sys, 1024d, new BakeConfig());
        }
        catch (InvalidOperationException ex)
        {
            bakeThrows = true;
            message = ex.Message;
        }

        bool mentionsMoon = message.Contains("I-1");
        bool mentionsPlanet = message.Contains("I (");
        bool planetBStillListed = false;
        foreach (OrbitingBody b in sys.AllBodies)
        {
            if (b.Name == "III-1")
            {
                planetBStillListed = !message.Contains("III-1");
            }
        }

        Check(estimateThrows && bakeThrows && mentionsMoon && mentionsPlanet && planetBStillListed, "T61 unbakeable-moon",
            string.Format("оценщик бросает: {0}; бейк бросает: {1}; в списке луна I-1: {2}; каскад на планету I: {3}; посторонние тела не тронуты: {4}",
                estimateThrows, bakeThrows, mentionsMoon, mentionsPlanet, planetBStillListed));
        return 0;
    }

    private static int Test63_KeplerContinuation()
    {
        // P1: Kepler continuation past the end of the rail. A player who reaches the
        // end of the baked world stays in physics: the body follows a Keplerian orbit
        // taken from the integrated state at EndSeconds (the seam is seamless).
        // Checks:
        // 1) the fit is valid for all bodies (a>0, e<1);
        // 2) seamless seam: |cont(End+dt) − rail(End) − railV(End)·dt| ≤ 1 m
        //    at dt=1 s (curvature gives ~0.003 m, the fit ~1e-9 relative);
        // 3) beyond the end (+10 and +100 years) — no exceptions, radius bounded by
        //    a(1+e)·(1+1e-6);
        // 4) BG3 file: continuation is read from the tail, the windowed path works;
        // 5) BE2 file: same for uncompressed;
        // 6) ephemeris without continuation (legacy) past the end — LOUD as before.
        StarSystem sys = TwelveBodySystem();
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };
        var memory = EphemerisBaker.Bake(sys, 2d, config);

        bool fitValid = memory.Count > 0;
        foreach (var kv in memory)
        {
            double[] c = kv.Value.Continuation;
            if (c == null || c.Length != 6 || !(c[0] > 0d) || !(c[1] >= 0d) || !(c[1] < 1d))
            {
                fitValid = false;
            }
        }

        foreach (var kv in memory)
        {
            kv.Key.Baked = kv.Value;
        }

        sys.InvalidatePositionCache();
        var bodies = sys.AllBodies;
        double end = memory[bodies[1]].EndSeconds;

        // Seam: rail at End vs continuation at End+1 s (linear extrapolation).
        bool seamOk = true;
        double worstSeam = 0d;
        foreach (var kv in memory)
        {
            double mu = kv.Key.Parent.ResolveStandardGravitationalParameter() + kv.Key.ResolveSubtreeStandardGravitationalParameter();
            kv.Value.TryEvaluate(end, out Vector3d pEnd, out Vector3d vEnd);
            if (!kv.Value.TryEvaluateContinuation(end + 1d, mu, out Vector3d pc, out _))
            {
                seamOk = false;
                break;
            }

            double dev = (pc - (pEnd + (vEnd * 1d))).Magnitude;
            if (dev > worstSeam)
            {
                worstSeam = dev;
            }

            if (dev > 1d)
            {
                seamOk = false;
            }
        }

        // Beyond the end: +10 and +100 years, radius bounded.
        bool beyondOk = true;
        foreach (var kv in memory)
        {
            double mu = kv.Key.Parent.ResolveStandardGravitationalParameter() + kv.Key.ResolveSubtreeStandardGravitationalParameter();
            double a = kv.Value.Continuation[0];
            double e = kv.Value.Continuation[1];
            foreach (double years in new[] { 10d, 100d })
            {
                if (!kv.Value.TryEvaluateContinuation(end + (years * 365d * 86400d), mu, out Vector3d pc, out Vector3d vc))
                {
                    beyondOk = false;
                    break;
                }

                double r = pc.Magnitude;
                if (!(r < (a * (1d + e)) * 1.000001d) || !pc.IsFinite || !vc.IsFinite)
                {
                    beyondOk = false;
                    break;
                }
            }

            if (!beyondOk)
            {
                break;
            }
        }

        // BG3 file: continuation from the tail + windowed reading.
        string dirC = System.IO.Path.Combine(Path.GetTempPath(), "t63c_" + Guid.NewGuid().ToString("N"));
        StarSystem sysC = TwelveBodySystem();
        var filesC = EphemerisBaker.BakeToFiles(sysC, dirC, 2d, config, null, null, null, compress: true);
        bool bg3Ok = filesC.Count > 0;
        OrbitingBody moonC = null;
        BakedEphemeris moonFile = null;
        foreach (var kv in filesC)
        {
            if (kv.Key.Name == "III-1")
            {
                moonC = kv.Key;
                moonFile = kv.Value;
            }

            BakedEphemeris f = BakedEphemeris.OpenFile(kv.Value.FilePath);
            double mu = kv.Key.Parent.ResolveStandardGravitationalParameter() + kv.Key.ResolveSubtreeStandardGravitationalParameter();
            if (f == null)
            {
                Console.WriteLine("  DBG BG3: OpenFile null, len={0}", new System.IO.FileInfo(kv.Value.FilePath).Length);
                bg3Ok = false;
                break;
            }

            if (f.Continuation == null || !f.TryEvaluateContinuation(end + 3600d, mu, out Vector3d pc, out Vector3d vc))
            {
                Console.WriteLine("  DBG BG3: cont={0} len={1}", f.Continuation == null ? "null" : "ok", f.Continuation == null ? -1 : f.Continuation[0]);
                bg3Ok = false;
                break;
            }
        }

        if (bg3Ok)
        {
            // Seam via WINDOWED file reading (segment decoding + tail).
            double muM = moonC.Parent.ResolveStandardGravitationalParameter() + moonC.ResolveSubtreeStandardGravitationalParameter();
            moonFile.TryEvaluate(end - 1d, out Vector3d pRail, out Vector3d vRail);
            moonFile.TryEvaluateContinuation(end + 1d, muM, out Vector3d pCont, out _);
            double seamFile = (pCont - (pRail + (vRail * 2d))).Magnitude;
            if (seamFile > 3d)
            {
                bg3Ok = false;
            }
        }

        // BE2 file: uncompressed path with continuation.
        string dirP = System.IO.Path.Combine(Path.GetTempPath(), "t63p_" + Guid.NewGuid().ToString("N"));
        StarSystem sysP = TwelveBodySystem();
        var filesP = EphemerisBaker.BakeToFiles(sysP, dirP, 2d, config);
        bool be2Ok = filesP.Count > 0;
        foreach (var kv in filesP)
        {
            BakedEphemeris f = BakedEphemeris.OpenFile(kv.Value.FilePath);
            double mu = kv.Key.Parent.ResolveStandardGravitationalParameter() + kv.Key.ResolveSubtreeStandardGravitationalParameter();
            if (f == null || f.Continuation == null || !f.TryEvaluateContinuation(end + 3600d, mu, out _, out _))
            {
                be2Ok = false;
                break;
            }
        }

        // Legacy (no continuation): fallback via EvaluateWorldState — loud.
        bool legacyLoud = true;
        StarSystem sysL = TwelveBodySystem();
        var legacy = EphemerisBaker.Bake(sysL, 2d, config);
        foreach (var kv in legacy)
        {
            kv.Value.Continuation = null;
        }

        foreach (var kv in legacy)
        {
            kv.Key.Baked = kv.Value;
        }

        sysL.InvalidatePositionCache();
        try
        {
            sysL.AllBodies[1].EvaluateWorldState(end + 1d, out _, out _);
            legacyLoud = false;
        }
        catch (InvalidOperationException)
        {
            legacyLoud = true;
        }

        // Fallback via EvaluateWorldState: past the end without an exception.
        bool worldOk = true;
        try
        {
            bodies[1].EvaluateWorldState(end + (3600d * 24d), out Vector3d pW, out Vector3d vW);
            worldOk = pW.IsFinite && vW.IsFinite;
        }
        catch (Exception)
        {
            worldOk = false;
        }

        Check(fitValid && seamOk && beyondOk && bg3Ok && be2Ok && legacyLoud && worldOk, "T63 kepler-continuation",
            string.Format("продолжение: фитт={0}; стык={1} (worst {2:E3}м); за концом={3}; BG3={4}; BE2={5}; legacy громко={6}; world+1сут={7}",
                fitValid, seamOk, worstSeam, beyondOk, bg3Ok, be2Ok, legacyLoud, worldOk));
        return 0;
    }

    private static int Test64_HillValidation()
    {
        // P2: loud Hill validation at system load. A moon beyond the stability limit
        // (0.3 R_H) and too-close siblings throw InvalidOperationException with names
        // and numbers when CONSTRUCTING StarSystem, not when baking.
        // Numbers: planet III (a=1.5e11, μ=4e14) → R_H = a·(μ/(3μ★))^{1/3}
        // = 1.74e9 m; limit 0.3·R_H = 5.22e8 m; mutual R_H of a pair (μ=4.9e12,
        // 1e12) at a~3e8 ≈ 2.4e7 m; separation limit 3× = 7.2e7 m.
        double muStar = 1.327e20;
        double muPlanet = 4e14;
        double aPlanet = 1.5e11;
        double hill = aPlanet * Math.Pow(muPlanet / (3d * muStar), 1d / 3d);

        // 1) Moon at 0.6 R_H — throws.
        bool unstableThrows = false;
        string unstableMessage = "";
        try
        {
            MakeMoonSystem(0.6d * hill);
        }
        catch (InvalidOperationException ex)
        {
            unstableThrows = true;
            unstableMessage = ex.Message;
        }

        // 2) Moon just inside the limit (0.3 R_H minus margin) — passes.
        bool boundaryPasses = true;
        try
        {
            MakeMoonSystem(0.29d * hill);
        }
        catch (InvalidOperationException)
        {
            boundaryPasses = false;
        }

        // 3) Siblings too close — throws.
        bool closeSiblingsThrow = false;
        try
        {
            OrbitingBody star = T64Star();
            OrbitingBody planet = T64Planet();
            star.Children.Add(planet);
            OrbitingBody m1 = T64Moon("m1", 2.4e7);
            OrbitingBody m2 = T64Moon("m2", 2.4e7 + 0.3e7); // separation 3e6 ≪ 3× mutual
            planet.Children.Add(m1);
            planet.Children.Add(m2);
            new StarSystem(star);
        }
        catch (InvalidOperationException)
        {
            closeSiblingsThrow = true;
        }

        // 4) The message contains the name and numbers.
        bool messageUseful = unstableMessage.Contains("m1") && unstableMessage.Contains("R_H");

        // 5) The production fixture passes.
        bool fixturePasses = true;
        try
        {
            TwelveBodySystem();
        }
        catch (InvalidOperationException)
        {
            fixturePasses = false;
        }

        Check(unstableThrows && boundaryPasses && closeSiblingsThrow && messageUseful && fixturePasses, "T64 hill-validation",
            string.Format("0.6 R_H бросает: {0} (в сообщении имя+числа: {1}); 0.29 R_H проходит: {2}; близкие сиблинги бросают: {3}; фикстура 1+5+6 проходит: {4}",
                unstableThrows, messageUseful, boundaryPasses, closeSiblingsThrow, fixturePasses));
        return 0;
    }

    private static int Test65_SystemBlueprint()
    {
        // P3: assembling StarSystem from a blueprint (the core of the Unity inspector bake).
        // 1) blueprint (star+planet+moon) == manual assembly: a 1-year bake and state
        //    evaluations match bit-for-bit;
        // 2) unstable moon (beyond 0.3 R_H) — Build() throws loudly (P2 works via blueprint);
        // 3) zero or two roots — loud;
        // 4) invalid ParentIndex — loud.
        var bp = new SystemBlueprint();
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Звезда", ParentIndex = -1, StandardGravitationalParameter = 1.327e20, Radius = 3e8
        });
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Планета", ParentIndex = 0, StandardGravitationalParameter = 4e14,
            Radius = 3.4e6, SemiMajorAxis = 1.5e11, Eccentricity = 0.015,
            MeanAnomalyAtEpochDegrees = 80
        });
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Луна", ParentIndex = 1, StandardGravitationalParameter = 4.9e12,
            Radius = 1.3e6, SemiMajorAxis = 3.84e8, Eccentricity = 0.03,
            MeanAnomalyAtEpochDegrees = 12, NorthPoleY = 0.9d, NorthPoleZ = 0.435889894354067d
        });

        StarSystem fromBlueprint = bp.Build();
        StarSystem manual = ManualStarPlanetMoon();
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };

        var ephBp = EphemerisBaker.Bake(fromBlueprint, 1d, config);
        var ephManual = EphemerisBaker.Bake(manual, 1d, config);
        foreach (var kv in ephBp)
        {
            kv.Key.Baked = kv.Value;
        }

        foreach (var kv in ephManual)
        {
            kv.Key.Baked = kv.Value;
        }

        fromBlueprint.InvalidatePositionCache();
        manual.InvalidatePositionCache();

        bool identical = true;
        for (int k = 0; k < 200; k++)
        {
            double t = (k / 199d) * 1d * 365d * 86400d;
            for (int i = 0; i < 3; i++)
            {
                fromBlueprint.EvaluateBodyState(fromBlueprint.AllBodies[i], t, out Vector3d p1, out Vector3d v1);
                manual.EvaluateBodyState(manual.AllBodies[i], t, out Vector3d p2, out Vector3d v2);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z || v1.X != v2.X || v1.Y != v2.Y || v1.Z != v2.Z)
                {
                    identical = false;
                }
            }
        }

        // Unstable moon: 0.8 R_H of the planet (limit ~0.3).
        var unstable = new SystemBlueprint();
        unstable.Bodies.Add(new BodyBlueprint { Name = "Звезда", ParentIndex = -1, StandardGravitationalParameter = 1.327e20 });
        unstable.Bodies.Add(new BodyBlueprint { Name = "Планета", ParentIndex = 0, StandardGravitationalParameter = 4e14, SemiMajorAxis = 1.5e11 });
        unstable.Bodies.Add(new BodyBlueprint { Name = "Луна", ParentIndex = 1, StandardGravitationalParameter = 4.9e12, SemiMajorAxis = 8e8 });
        bool unstableThrows = false;
        try
        {
            unstable.Build();
        }
        catch (InvalidOperationException)
        {
            unstableThrows = true;
        }

        bool zeroRootsThrows = false;
        try
        {
            var zero = new SystemBlueprint();
            zero.Bodies.Add(new BodyBlueprint { Name = "a", ParentIndex = 0 });
            zero.Build();
        }
        catch (InvalidOperationException)
        {
            zeroRootsThrows = true;
        }

        bool twoRootsThrows = false;
        try
        {
            var two = new SystemBlueprint();
            two.Bodies.Add(new BodyBlueprint { Name = "a", ParentIndex = -1 });
            two.Bodies.Add(new BodyBlueprint { Name = "b", ParentIndex = -1 });
            two.Build();
        }
        catch (InvalidOperationException)
        {
            twoRootsThrows = true;
        }

        bool badParentThrows = false;
        try
        {
            var bad = new SystemBlueprint();
            bad.Bodies.Add(new BodyBlueprint { Name = "Звезда", ParentIndex = -1 });
            bad.Bodies.Add(new BodyBlueprint { Name = "Тело", ParentIndex = 5 });
            bad.Build();
        }
        catch (InvalidOperationException)
        {
            badParentThrows = true;
        }

        Check(identical && unstableThrows && zeroRootsThrows && twoRootsThrows && badParentThrows, "T65 system-blueprint",
            string.Format("блюпринт==ручная сборка (бейк 1y, 200 точек бит-в-бит): {0}; нестабильная луна громко: {1}; ноль корней: {2}; два корня: {3}; битый ParentIndex: {4}",
                identical, unstableThrows, zeroRootsThrows, twoRootsThrows, badParentThrows));
        return 0;
    }

    private static int Test67_KeplerPredictorGuards()
    {
        // S1: hyperbolic NaN guard, un-bracketed bisection, M reduction by period.

        // 1) e=1+1e-9 (outside the |e−1|<1e-9 window): Newton diverges — the result
        // must be finite and match the residual, not NaN.
        double h = KeplerPredictor.SolveHyperbolicAnomaly(0.5d, 1.000000001d);
        double residual = (1.000000001d * Math.Sinh(h)) - h - 0.5d;
        bool nearParabolicFinite = double.IsFinite(h) && Math.Abs(residual) < 1e-6d;

        // 2) NaN M — loud.
        bool nanThrows = false;
        try
        {
            KeplerPredictor.SolveHyperbolicAnomaly(double.NaN, 2d);
        }
        catch (ArgumentOutOfRangeException)
        {
            nanThrows = true;
        }

        // 3) Hyperbola with a very large M (dt in years): finite result.
        double hBig = KeplerPredictor.SolveHyperbolicAnomaly(1e6d, 3d);
        bool bigMFinite = double.IsFinite(hBig);

        // 4) M reduction by period: Advance over 100 years ≡ Advance over 100 years
        // − 3 periods (the phase difference is only fp reduction, not ulp at 1e10 rad).
        Vector3d r0 = new Vector3d(1.5e11, 0d, 0d);
        double muE = 1.327e20d;
        Vector3d v0 = new Vector3d(0d, Math.Sqrt(muE / 1.5e11) * 1.001d, 0d); // e≈0.001... take a noticeable ellipse
        v0 = new Vector3d(0d, Math.Sqrt(2d * muE / 1.5e11) * 0.7d, 0d); // e≈0.51
        double a = 1.5e11 / (2d - (0.7d * 0.7d * (2d * 1.5e11 / 1.5e11)));
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / muE);
        double dt = 100d * 365d * 86400d;
        double dtReduced = dt - (Math.Floor(dt / period) * period);
        KeplerPredictor.Advance(r0, v0, muE, dt, out Vector3d pLong, out Vector3d vLong);
        KeplerPredictor.Advance(r0, v0, muE, dtReduced, out Vector3d pShort, out Vector3d vShort);
        double phaseDiff = (pLong - pShort).Magnitude;
        bool reductionWorks = phaseDiff < 1d; // without reduction, ulp(1e10 rad) would give ~hundreds of metres

        bool parabolicThrows = false;
        try
        {
            // Exactly parabolic energy: v = sqrt(2μ/r).
            double rEsc = 1e11;
            Vector3d rEscVec = new Vector3d(rEsc, 0d, 0d);
            Vector3d vEsc = new Vector3d(0d, Math.Sqrt(2d * muE / rEsc), 0d);
            KeplerPredictor.Advance(rEscVec, vEsc, muE, 1000d, out _, out _);
        }
        catch (NotSupportedException)
        {
            parabolicThrows = true;
        }
        catch (ArgumentException)
        {
            parabolicThrows = true;
        }

        Check(nearParabolicFinite && nanThrows && bigMFinite && reductionWorks && parabolicThrows, "T67 kepler-predictor-guards",
            string.Format("e≈1 конечен и точен: {0} (res={1:E2}); NaN M громко: {2}; M=1e6 конечен: {3}; редукция M: {4} (Δ={5:E3}м); околопарабола громко: {6}",
                nearParabolicFinite, residual, nanThrows, bigMFinite, reductionWorks, phaseDiff, parabolicThrows));
        return 0;
    }

    private static StarSystem TunnelSystem()
    {
        // Small static body as the system root (orbiting 1e9 from a star, the
        // asteroid would move at 364 km/s and be uncatchable by the test's construction).
        var asteroid = new OrbitingBody
        {
            Name = "TunRock",
            Radius = 1000d,
            StandardGravitationalParameter = 1e9d
        };
        return new StarSystem(asteroid);
    }

    private static int Test79_TidalLock()
    {
        // Assembled via blueprint: tidal lock computes the period from μ_local =
        // μ_parent(own) + μ_subtree — the same as EvaluateLocalOffset.
        StarSystem sys = MakeTidalSystem(0d, false).Build();
        OrbitingBody moon = sys.AllBodies[2];
        OrbitingBody planet = sys.AllBodies[1];

        double expectedPeriod = 2d * Math.PI * Math.Sqrt(
            (2e7d * 2e7d * 2e7d) / (4e14d + 5e12d));
        bool periodOk = moon.RotationPeriodSeconds == expectedPeriod;
        bool manualIgnored = moon.RotationPeriodSeconds != 999d;

        // Multi-orbit check (50 orbits): at e=0 the deviation is always zero; a wrong
        // μ would give drift ~2π·(Δμ/μ) per orbit ≈ 4.4°/orbit — impossible to hide.
        double worstPhase = 0d;
        double orbitPeriod = expectedPeriod;
        for (int k = 1; k <= 50; k++)
        {
            worstPhase = Math.Max(worstPhase, TidalDeviation(moon, planet, k * orbitPeriod));
        }

        bool phaseOk = worstPhase < 1e-6d;

        // Determinism: rebuild from an UNCHANGED blueprint — bit-for-bit.
        StarSystem sys2 = MakeTidalSystem(0d, false).Build();
        OrbitingBody moon2 = sys2.AllBodies[2];
        bool idempotent = moon2.RotationPeriodSeconds == moon.RotationPeriodSeconds
            && moon2.PrimeMeridianOffsetDegrees == moon.PrimeMeridianOffsetDegrees;

        // lat/lon round-trip on a tidally locked body stays mutually inverse.
        bool roundtrip = true;
        for (int k = 0; k < 8; k++)
        {
            double t = k * orbitPeriod * 0.125d;
            moon.GetSurfaceState(23.5d, -117.25d, 100d, t, out Vector3d wp, out _);
            moon.SurfaceLatLonAt(wp, t, out double latBack, out double lonBack);
            if (Math.Abs(latBack - 23.5d) > 1e-7d || Math.Abs(KeplerMath.NormalizeAngle((lonBack + 180d) * (Math.PI / 180d)) - KeplerMath.NormalizeAngle((-117.25d + 180d) * (Math.PI / 180d))) * (180d / Math.PI) > 1e-7d)
            {
                roundtrip = false;
            }
        }

        // Libration: e=0.05 → deviation amplitude = 2e (equation of the centre
        // ν−M ≈ 2e·sin M). Bounds 1.5e..2.5e: a "±e" model would fail below.
        StarSystem sysE = MakeTidalSystem(0.05d, false).Build();
        OrbitingBody moonE = sysE.AllBodies[2];
        OrbitingBody planetE = sysE.AllBodies[1];
        double ePeriod = moonE.RotationPeriodSeconds;
        double worstLibration = 0d;
        for (int k = 0; k < 720; k++)
        {
            worstLibration = Math.Max(worstLibration, TidalDeviation(moonE, planetE, k * ePeriod / 720d));
        }

        bool librationOk = worstLibration > 1.5d * 0.05d && worstLibration < 2.5d * 0.05d;

        // Lock on the root (star, no parent) — loud exception.
        bool rootRejected = false;
        try
        {
            MakeTidalSystem(0d, true).Build();
        }
        catch (InvalidOperationException)
        {
            rootRejected = true;
        }

        Check(periodOk && manualIgnored, "T79 tidal-lock",
            "период = 2π√(a³/μ_local) бит-в-бит (" + moon.RotationPeriodSeconds.ToString("F1") + "с), ручные 999с игнорированы");
        Check(phaseOk, "T79 tidal-lock", "50 витков: подкосительный дрейф " + worstPhase.ToString("E2") + " рад < 1e-6");
        Check(idempotent, "T79 tidal-lock", "пересборка без правки данных: период/оффсет бит-в-бит");
        Check(roundtrip, "T79 tidal-lock", "round-trip lat/lon на захваченном теле");
        Check(librationOk, "T79 tidal-lock", "либрация e=0.05: амплитуда " + worstLibration.ToString("F4") + " рад ∈ (1.5e, 2.5e) = ±2e");
        Check(rootRejected, "T79 tidal-lock", "замок на звезде громко отвергнут");
        return 0;
    }
}
