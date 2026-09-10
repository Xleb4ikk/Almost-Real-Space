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
        // Замер цены уточнения: считающий источник на обоих путях.
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
        // T21a: чистая арифметика. Точное значение 1e7×0.1 = 1e6.
        // Ошибка наивного накопления НЕ фиксируется порогом (зависит от порядка
        // округлений конкретной реализации) — печатается информационно.
        // Kahan обязан быть существенно ближе: свой абсолютный допуск + лучше
        // наивного минимум в 100 раз (фактически — на порядки).
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

        // T21b: 10 лет баллистического coast дневными кадрами (как игра):
        // завершение, конечность, границы системы, плановое событие ровно
        // в конце спена, бит-детерминизм повторного прогона.
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
        // Второй прогон для детерминизма — состояние корабля сверяем отдельно.
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
        // B1 контракт: формула, v_rel вращающейся атмосферы, срезы, warp-исключение.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);

        // (a) Ручной счёт: h=0, ρ=1.2, v_rel=(1000,0,0), Cd=1, A=10 → F=(-6e6,0,0).
        var drag = new DragSource(planet);
        // Допуски 1e-6, не 1e-12: вычитание позиции тела (|bp|~1.5e11) даёт шум
        // высоты ±3e-5м → шум плотности ~4e-9. Тот самый fp-эффект world-origin
        // (ради него A2): физика терпит (1e-11 сил), тесты туже не требуют.
        Vector3d pA = bp0 + new Vector3d(planet.Radius, 0d, 0d);
        DynamicsContribution cA = drag.Evaluate(pA, bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        bool formula = Math.Abs((cA.Force.X + 6000000d) / 6000000d) < 1e-6d
            && cA.Force.Y == 0d && cA.Force.Z == 0d
            && cA.SpecificAcceleration.SqrMagnitude == 0d && cA.MassFlow == 0d;
        double halfH = 8500d * Math.Log(2d);
        DynamicsContribution cHalf = drag.Evaluate(bp0 + new Vector3d(planet.Radius + halfH, 0d, 0d), bv0 + new Vector3d(1000d, 0d, 0d), 1000d, 0d);
        bool half = Math.Abs(cHalf.Force.X / cA.Force.X - 0.5d) < 1e-6d;

        // (b) Дисциплина v_rel: вращающееся тело (период 3600), точка над экватором.
        // Со-вращающийся корабль (|v_world| огромна) → строго 0; инерциально
        // висящий → drag против v_rel, не против мировой скорости.
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

        // (c) Срезы: над Top → 0; под Top → ≠0; нет атмосферы → 0; Cd=0 → 0;
        // вырожденная (H=0) → 0 и конечно (не NaN).
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

        // (e) Дальний варп: внутри атмосферы запрещён drag-вкладом, снаружи разрешён.
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
        // (d) Диссипация отдельно от детекта: нырок с drag и без — оба находят
        // [Exit, Entry], но скорость на выходе с drag меньше (энергия ушла).
        // События не тронуты: те же детекторы, та же бисекция.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
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
            ScaleHeightMeters = 30000d,
            SeaLevelPressurePascals = 10d
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
        // Орбитальный decay в тонкой атмосфере: 10 витков, апо/пери и энергия
        // монотонно вниз (рост только в пределах wobble возмущений 1e-6).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = ThinAtmosphere();
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        // Орбита 250км в тонкой атмосфере: линейный режим (−0.7км/виток),
        // без лавины (на 200км обратная связь ρ сваливает в планету за ~10
        // витков — проверено отдельным прогоном: ΔE/E=−0.68, пери 3.8e6м).
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
        // Вращающаяся атмосфера в динамике: +X против ветра, −X по ветру
        // (в точке +Y атмосфера идёт −X). Потери ∝ v_rel²: отношение ≈
        // ((7800+473)/(7800−473))² ≈ 1.275. Допуск [1.1, 1.5] — знак + темп.
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
        // Срез Top: сила непрерывна (ρ→0 снизу, 0 сверху), состояние без скачка,
        // события [Entry, Touchdown] на месте. Стандартная плотная атмосфера,
        // крутое падение с 200км, сэмпл 1с: |Δv| ≤ 25м/с везде.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
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
        // Непрерывность меряем только в окне ±30с вокруг пересечения Top:
        // у земли торможение в тысячи g, и это тоже физика, а не скачок силы.
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

        // Окно ±3с: именно зона включения силы (drag 0→~1м/с² + g 9.4).
        // Шире нельзя: отвесное падение 2.5км/с за 30с уже в инферно на 40км.
        double maxDv = 0d;
        for (int i = 1; i < traceT.Count; i++)
        {
            if (Math.Abs(traceT[i] - crossT) <= 3d)
            {
                double dv = (traceV[i] - traceV[i - 1]).Magnitude;
                if (dv > maxDv) maxDv = dv;
            }
        }

        // События — отдельным чистым прогоном пропагатора (шаговый цикл выше —
        // только для непрерывности скорости; детект ниже — штатным путём).
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
        // Звезда + одна планета без луны: работа третьих тел (~9% от темпа drag
        // на 60с окне) исключена конструктивно, остался только прилив звезды
        // ~0.1%. Для baseline темпа, не для общей физики.
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
        // Baseline темпа: измеренный dE/dt за 60с против аналитики −½ρCdAv³/m
        // (круговая 200км, невращающаяся планета, тонкая атмосфера, двухтельная
        // система). Допуск 5%.
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
        // Инвариант 1: история — только чтение трека; голый прогон и прогон
        // с историей дают бит-в-бит то же состояние/события.
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

        // Инвариант 2: сетка времён не зависит от нарезки чанков (T11-нырок,
        // slices 10с против одного вызова; stride 60).
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
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

        // Инвариант 3: грубый stride 120 — события 26.7/407 между узлами сетки,
        // но сохраняются с точными временами + границы 0/600.
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

        // Юнит слияния на синтетическом треке: событие ровно на сетке (4.0),
        // почти на границе (10−5e-10) и внутри (5.0); приоритет Event.
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
        // (a) Гоман ровно 180°: r1=7e6, r2=−14e6, dt=полпериода a_t=10.5e6.
        // |v1| — точная виз-вива, v1⊥r1, прибытие — KeplerAdvance в r2.
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

        // (b) Самосогласованность 90° + long way: оба прибывают.
        var q1 = new Vector3d(7e6d, 0d, 0d);
        var q2 = new Vector3d(0d, 10e6d, 0d);
        LambertSolution shortWay = LambertSolver.Solve(q1, q2, 4000d, mu, false);
        LambertSolution longWay = LambertSolver.Solve(q1, q2, 20000d, mu, true);
        KeplerPredictor.Advance(q1, shortWay.DepartureVelocity, mu, 4000d, out Vector3d arrS, out _);
        KeplerPredictor.Advance(q1, longWay.DepartureVelocity, mu, 20000d, out Vector3d arrL, out _);
        bool chordsOk = (arrS - q2).Magnitude < 1d && (arrL - q2).Magnitude < 1d;

        // (c) Стражи: dt≤0, нулевой вектор, совпадающие точки.
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

        // (d) Porkchop планетаА→планетаБ: таблица без NaN, best конечен и минимален,
        // детерминизм бит-в-бит.
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

        // (e) Kepler-предикт против численного интегратора: круговая, 1 виток.
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
        // Численный корабль в мировой рамке vs предикт относительно планеты:
        // сравниваем относительно тела в конце. Граница 50м, не 1м: численный
        // чувствует прилив звезды (~16м за виток), предикт — чистые два тела.
        // Это честная граница модели, а не шум интегратора.
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
        // P1c.1: pipeline porkchop→Ламберт→валидатор; ударный invalid;
        // детерминизм; drag виден валидатором.
        // (a) ПланетаА(t=5д) → планетаБ(t=45д), best клетка 3×3.
        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        OrbitingBody planetA = sys.AllBodies[1];
        OrbitingBody planetB = sys.AllBodies[3];
        PorkchopCell[,] table = Porkchop.Scan(sys, star, planetA, planetB,
            5d * 86400d, 10d * 86400d, 3, 40d * 86400d, 50d * 86400d, 3);
        bool hasBest = Porkchop.TryBest(table, out int bi, out int bj);
        PorkchopCell best = table[bi, bj];
        double muStar = star.ResolveStandardGravitationalParameter();
        // Межпланетный пайплайн: Ламберт центр→центр (как планировщик), вылет
        // с края SOI на v∞. Класс точности — patched-conics (~1e9м, меньше SOI
        // цели быть не обязан): проверяет сквозное исполнение, не прецизионность.
        // Прецизионность — лунным перелётом ниже, где two-body ≈ N-body.
        // Честная оговорка про границу 5e9d ниже: она зафиксирована ПОСЛЕ замера
        // (5e6 → провал 8.67e9 на старте из глубины well; 5e8 → провал 1.17e9
        // на старте с края SOI), а не предсказана из теории. Роль границы —
        // gross-guard от мусора (NaN/расходимость/не та геометрия дают 1e11+),
        // не доказательство точности. Точность доказывает лунный кейс.
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

        // Лунная прецизионность: парковка 8e6м → Луна за 3 суток, Ламберт
        // в рамке планеты (там two-body почти точен), цель — сама Луна.
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
        // Прямое попадание в цель — тоже прибытие (в пределах тела), а не промах:
        // касание ЦЕЛИ за 8.8кс до tA (фокусировка гравитацией потянула раньше).
        // Отличать от удара НЕ в цель (T27b): там invalid без оговорок.
        bool moonImpact = false;
        for (int i = 0; i < moonVal.Events.Count; i++)
        {
            if (moonVal.Events[i].Kind == EventKind.Touchdown && ReferenceEquals(moonVal.Events[i].Body, moon))
            {
                moonImpact = true;
            }
        }

        bool moonOk = (moonVal.Valid && moonVal.ArrivalPositionErrorMeters < 1e6d) || moonImpact;

        // (b) Ударный invalid: радиально вниз с 8e6м, touchdown в пути.
        // Реальный корабль-образец не мутирует бит-в-бит.
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

        // (c) Детерминизм: повтор лунной валидации — бит-в-бит.
        var moonPhys2 = FlightPhysics(sys, false, planetA);
        var moonProp2 = new EventDrivenPropagator(moonPhys2);
        moonProp2.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planetA));
        moonProp2.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(moon));
        ValidationResult moonVal2 = TrajectoryValidator.ValidateImpulsive(sys, moonPhys2, moonProp2,
            moonState, tD2, avD + lamM.DepartureVelocity, moon, tA2, 1e6d);
        bool deterministic = moonVal.Valid == moonVal2.Valid
            && moonVal.ArrivalPositionErrorMeters == moonVal2.ArrivalPositionErrorMeters
            && moonVal.MinClearanceMeters == moonVal2.MinClearanceMeters;

        // (d) Drag виден: нырок 95км (T22 ICs), цель — планетаБ далеко
        // (допуск огромный, важна РАЗНИЦА промахов с/без drag).
        planetA.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        var dipState = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, planetA.Radius + 95000d, 0d),
            Velocity = bv0 + new Vector3d(3000d, 800d, 0d),
            Mass = 1000d
        };
        // Окно сравнения 150с: оба ещё в воздухе (вход ~10с, возврат ~200с),
        // расхождение уже накопилось (~200м), подземного хаоса нет.
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
        // P1c.2: сухой=живой (детерминизм, НЕ физика); бюджет топлива;
        // gravity losses парой одинаковый-Δv/тяга×10, направление инерциально
        // фиксировано (иначе примешался бы steering loss).
        StarSystem sys = TestSystem();
        OrbitingBody planetA = sys.AllBodies[1];
        double muA = planetA.ResolveStandardGravitationalParameter();
        double parkR = planetA.Radius + 8e6d;
        double parkV = Math.Sqrt(muA / parkR);
        planetA.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d parkP0 = bp0 + new Vector3d(0d, parkR, 0d);
        Vector3d parkV0 = bv0 + new Vector3d(parkV, 0d, 0d);
        var preset = new BurnTolerancePreset(1e-2d, 1e-5d, 1e-4d, 1e-10d);

        // (a) Детерминизм сухого прогона (честная подпись: не доказательство физики).
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

        // (b) Перерасход: бак 10кг, окно 30с требует ~20кг → depletion, invalid.
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

        // (c) Gravity losses: Δv=1500м/с, тяга 60000Н vs 6000Н (горение ~157с
        // vs ~1570с), направление — инерциальный прогонный вектор в зажигании.
        // Флаги валидатора: допуск уже потери → invalid, шире → valid.
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
        // V3: убегание от звезды 10 лет (v∞=27км/с, ~57 а.е.). v∞ сохраняется
        // (нет скрытых источников/стоков энергии в крейсере), прибытие сверяется
        // с гиперболическим Kepler (возмущения планет — замер, не игнор).
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
        // Golden-master управляемого базиса (митигация риска #1, не доказательство
        // корректности — она у T13/Циолковского): фиксированный вход, один шаг
        // SpacecraftPhysics.Step, эталон Position/Velocity/Mass побитово + число
        // RHS-вызовов (ловит и смену траектории шагов, не только финал).
        // Значения записаны с зелёного прогона; любое молчаливое изменение базиса
        // роняет тест — обновлять эталон только явным решением, не подгонкой.
        // Независимый якорь: та же дуга аналитикой KeplerPredictor в допуске
        // (проверяет, что зацементировано не мусорное значение).
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
        bool bitwise =
            ship.Position.X == 147004123096.99823d &&
            ship.Position.Y == 23171251.590329804d &&
            ship.Position.Z == 0.00017448425047284386d &&
            ship.Velocity.X == 5629.710645784098d &&
            ship.Velocity.Y == 25176.110803451433d &&
            ship.Velocity.Z == 1.1497807741506363E-06d &&
            ship.Mass == 1000d &&
            counting.Calls == 81L;
        planet.EvaluateWorldState(600d, out Vector3d bodyEnd, out _);
        KeplerPredictor.Advance(
            new Vector3d(0d, planet.Radius + 200000d, 0d),
            new Vector3d(7500d, 0d, 0d),
            planet.StandardGravitationalParameter, 600d,
            out Vector3d keplerPos, out _);
        double anchorErr = ((ship.Position - bodyEnd) - keplerPos).Magnitude / keplerPos.Magnitude;
        Check(bitwise && anchorErr < 1e-4d, "T34 golden-state",
            string.Format("бит-в-бит эталон + CALLS=81: {0}; якорь Кеплер relErr={1:E2} (<1e-4)",
                bitwise, anchorErr));
        return 0;
    }

    private static int Test39_SecularDrift()
    {
        // Step 0 + регресс-гейт решения «рельсы навсегда / bake / PEFRL»:
        // 80 лет двумя шагами (сходимость обязана быть << эффекта) + 2000 лет
        // без луны для секулярного темпа planet-planet (луна заставляет мелкий
        // шаг, 2000 лет с ней неподъёмны).
        // μ-фикс (OrbitingBody): внешняя орбита μ_parent_own + μ_subtree.
        // Якоби-инициализация N-body: центр планеты смещён так, чтобы барицентр
        // поддерева совпал с рельсой, иначе +12 м/с ложного дрейфа (~1e9 м/год,
        // поймано диагностикой роста full vs noMoon).
        StarSystem sys = TestSystem();
        double[] posFine;
        double[] devFine = NBodyDeviations(sys, 80d, 900d, 1d, out posFine);
        double[] posCoarse;
        double[] devCoarse = NBodyDeviations(sys, 80d, 1800d, 1d, out posCoarse);
        // Сходимость инструмента обязана быть на порядки ниже эффекта:
        // иначе меряем ошибку интегратора, а не физику.
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

        // Самопроверка инструмента: повтор тем же шагом — бит-в-бит.
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

        // Решение Step 0 (партия 40-80 лет): эффект 1e9 м >> порога 500 км —
        // «рельсы навсегда» мертвы, нужен bake (200-500 лет фиксированной эфемериды,
        // без скользящего окна/чекпоинтов). Гейт пинит именно это решение:
        // эффект обязан ПРЕВЫШАТЬ порог (иначе решение пересмотреть), сходимость —
        // быть на порядки ниже эффекта. Верхние границы — от регресса инструмента.
        // Планета Б без луны: 7.25e8 м ≈ SOI 5.8e5 км / Hill 9.9e5 км — порядок SOI.
        Check(worst80 > 500000d && worst80 < 5000000000d && worstConv < 5000d && worst2000 > 20000000d && worst2000 < 100000000000d && deterministic, "T39 secular-drift",
            string.Format("80 лет: худш. {0:E2}м (в (500км, 5e9м): bake-решение) [{1}]; сходимость h/h2: {2:E2}м (<5км); 2000 лет: худш. {3:E2}м (в (20000км, 1e11м)); детерминизм: {4}",
                worst80, perBody, worstConv, worst2000, deterministic));
        return 0;
    }

    private static int Test36_HelioDrift()
    {
        // Долгая гелиоцентрика: круговая r=2.5e11 в TwoBodySystem (возмущение
        // планеты реально, ~2e-5 относительно — входит в границу осознанно),
        // 5 лет помесячно (60 чанков). Метрики: ΔE/E в рамке звезды + Δr/r +
        // ограниченность 0.5·r0<r<2·r0. Границы после замера (дисциплина).
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
        // Возмущённая Луной многолетняя: та же высокая круговая, что T30
        // (R+2e7, прямое движение), но в полной системе С Луной, 5 лет
        // помесячно (60 чанков). Метрики те же, что T30 (ΔE/E, Δапо по истинному
        // a(1+e)=r для круговой) + ограниченность (без побега/падения).
        // Границы после замера; отношение к безлунным числам T30 — в отчёт
        // (цена Луны числом, а не словами).
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

        // Цена Луны числом: 1.46e-4 против 5.47e-6 безлунного T30 (~27x) —
        // тот же интегратор, та же орбита, разница только в физике.
        Check(worstDe < 5e-4d && worstDa < 3e-2d && minR > planet.Radius && maxR < 10d * r, "T37 lunar-years",
            string.Format("5 лет с Луной, 60 чанков: худш. ΔE/E={0:E2}, худш. Δапо={1:E2}; радиус в [{2:E2},{3:E2}] (R={4:E2})",
                worstDe, worstDa, minR, maxR, planet.Radius));
        return 0;
    }

    private static int Test38_LowDrag()
    {
        // Низкие drag-орбиты длительно: круговая 200км, тонкая атмосфера
        // (те же Cd=1/A=10/m=1000, что T23 — сравнимо), 6 витков почанково
        // (180км/8 и 190км/8 падали в планету — лавина быстрее линейной оценки;
        // прецедент сдвига T23 200→250км). Метрики как T23 (монотонность со
        // слаками +10м/1e-6) + пери > R каждый виток + суммарный сброс
        // в коридоре от аналитической оценки (коридор после замера; аналитика
        // по начальным условиям занижает, т.к. ρ растёт при снижении).
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

        // Знак: measured-сброс отрицательный (энергия падает), analyticDrop —
        // положительная величина темпа. Сравниваем модули (первый прогон
        // делил знаковое на положительное и получал −1.21 — ошибка теста).
        double totalDrop = prevE - e0;
        double ratio = Math.Abs(totalDrop) / analyticDrop;
        Check(mono && !crashed && doneOrbits == 6 && finalMinR > planet.Radius + 50000d && ratio > 0.5d && ratio < 4d, "T38 low-drag",
            string.Format("6 витков 200км ({0} пройдено): монотонность {1}, без падения {2}, финальный перигей {3:E2}м (>R+50км); сброс {4:E2} Дж/кг vs аналитика {5:E2} (отношение {6:F2} в [0.5,4])",
                doneOrbits, mono, !crashed, finalMinR, totalDrop, analyticDrop, ratio));
        return 0;
    }

    private static int Test61_UnbakeableMoon()
    {
        // Луна с периодом 5.4 ч: сегмент 2 сут не может аппроксимировать 9
        // wrapped витков — бейк обязан ГРОМКО отказаться (и оценщик тоже), а не
        // выдать алиасированный мусор. Каскад: планета несёт рябь луны — тоже
        // unbakeable, звезда не в счёте (не выпекается).
        StarSystem sys = TwelveBodySystem();
        OrbitingBody mI1 = null;
        foreach (OrbitingBody b in sys.AllBodies)
        {
            if (b.Name == "I-1")
            {
                mI1 = b;
            }
        }

        mI1.SemiMajorAxis = 1.5e7; // период = 2π√(a³/μ_планеты) ≈ 5.4 ч

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
        // P1: кеплерово продолжение за концом рельсы. Игрок, дошедший до конца
        // испечённого мира, остаётся в физике: тело летит по кеплеровой орбите,
        // снятой с интегрированного состояния в EndSeconds (стык бесшовный).
        // Проверки:
        // 1) фиттинг валиден для всех тел (a>0, e<1);
        // 2) стык бесшовный: |cont(End+dt) − rail(End) − railV(End)·dt| ≤ 1 м
        //    при dt=1с (кривизна даёт ~0.003 м, фиттинг ~1e-9 относительных);
        // 3) за концом (+10 и +100 лет) — без исключений, радиус ограничен
        //    a(1+e)·(1+1e-6);
        // 4) BG3-файл: продолжение читается из хвоста, оконный путь работает;
        // 5) BE2-файл: то же для несжатого;
        // 6) эфемерида без продолжения (legacy) за концом — ГРОМКО как раньше.
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

        // Стык: рельса в End против продолжения в End+1с (линейная экстраполяция).
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

        // За концом: +10 и +100 лет, радиус ограничен.
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

        // BG3-файл: продолжение из хвоста + оконное чтение.
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
            // Стык через ОКОННОЕ чтение файла (декодирование сегментов + хвост).
            double muM = moonC.Parent.ResolveStandardGravitationalParameter() + moonC.ResolveSubtreeStandardGravitationalParameter();
            moonFile.TryEvaluate(end - 1d, out Vector3d pRail, out Vector3d vRail);
            moonFile.TryEvaluateContinuation(end + 1d, muM, out Vector3d pCont, out _);
            double seamFile = (pCont - (pRail + (vRail * 2d))).Magnitude;
            if (seamFile > 3d)
            {
                bg3Ok = false;
            }
        }

        // BE2-файл: несжатый путь с продолжением.
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

        // Legacy (без продолжения): фолбэк через EvaluateWorldState — громко.
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

        // Фолбэк через EvaluateWorldState: за концом без исключения.
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
        // P2: громкая Hill-валидация при загрузке системы. Луна за пределом
        // устойчивости (0.3 R_H) и слишком близкие сиблинги — InvalidOperationException
        // с именами и числами при КОНСТРУИРОВАНИИ StarSystem, а не при выпечке.
        // Дисциплина чисел: планета III (a=1.5e11, μ=4e14) → R_H = a·(μ/(3μ★))^{1/3}
        // = 1.74e9 м; граница 0.3·R_H = 5.22e8 м; взаимный R_H пары (μ=4.9e12,
        // 1e12) на a~3e8 ≈ 2.4e7 м; граница разноса 3× = 7.2e7 м.
        double muStar = 1.327e20;
        double muPlanet = 4e14;
        double aPlanet = 1.5e11;
        double hill = aPlanet * Math.Pow(muPlanet / (3d * muStar), 1d / 3d);

        // 1) Луна на 0.6 R_H — бросает.
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

        // 2) Луна ровно на границе (0.3 R_H минус запас) — проходит.
        bool boundaryPasses = true;
        try
        {
            MakeMoonSystem(0.29d * hill);
        }
        catch (InvalidOperationException)
        {
            boundaryPasses = false;
        }

        // 3) Сиблинги слишком близко — бросает.
        bool closeSiblingsThrow = false;
        try
        {
            OrbitingBody star = T64Star();
            OrbitingBody planet = T64Planet();
            star.Children.Add(planet);
            OrbitingBody m1 = T64Moon("m1", 2.4e7);
            OrbitingBody m2 = T64Moon("m2", 2.4e7 + 0.3e7); // разнос 3e6 ≪ 3·взаимного
            planet.Children.Add(m1);
            planet.Children.Add(m2);
            new StarSystem(star);
        }
        catch (InvalidOperationException)
        {
            closeSiblingsThrow = true;
        }

        // 4) Сообщение содержит имя и числа.
        bool messageUseful = unstableMessage.Contains("m1") && unstableMessage.Contains("R_H");

        // 5) Боевая фикстура (после фикса I-1/I-2/II-1) проходит.
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
        // P3: сборка StarSystem из чертежа (ядро Unity-инспекторного бейка).
        // 1) блюпринт (звезда+планета+луна) == ручная сборка: бейк 1 года и
        //    оценки состояний бит-в-бит совпадают;
        // 2) нестабильная луна (за 0.3 R_H) — Build() громко бросает (P2 работает
        //    через блюпринт);
        // 3) ноль или два корня — громко;
        // 4) некорректный ParentIndex — громко.
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

        // Нестабильная луна: 0.8 R_H планеты (граница ~0.3).
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
        // S1: NaN-guard гиперболы, незабрекеченная бисекция, редукция M по периоду.

        // 1) e=1+1e-9 (мимо окна |e−1|<1e-9): Ньютон улетает — результат обязан
        // быть конечным и соответствовать невязке, а не NaN.
        double h = KeplerPredictor.SolveHyperbolicAnomaly(0.5d, 1.000000001d);
        double residual = (1.000000001d * Math.Sinh(h)) - h - 0.5d;
        bool nearParabolicFinite = double.IsFinite(h) && Math.Abs(residual) < 1e-6d;

        // 2) NaN M — громко.
        bool nanThrows = false;
        try
        {
            KeplerPredictor.SolveHyperbolicAnomaly(double.NaN, 2d);
        }
        catch (ArgumentOutOfRangeException)
        {
            nanThrows = true;
        }

        // 3) Гипербола с очень большим M (dt в годы): конечный результат.
        double hBig = KeplerPredictor.SolveHyperbolicAnomaly(1e6d, 3d);
        bool bigMFinite = double.IsFinite(hBig);

        // 4) Редукция M по периоду: Advance на 100 лет ≡ Advance на 100 лет − 3
        // периода (разница фаз — только fp-редукция, не ulp-накопление на 1e10 рад).
        Vector3d r0 = new Vector3d(1.5e11, 0d, 0d);
        double muE = 1.327e20d;
        Vector3d v0 = new Vector3d(0d, Math.Sqrt(muE / 1.5e11) * 1.001d, 0d); // e≈0.001... возьмём заметный эллипс
        v0 = new Vector3d(0d, Math.Sqrt(2d * muE / 1.5e11) * 0.7d, 0d); // e≈0.51
        double a = 1.5e11 / (2d - (0.7d * 0.7d * (2d * 1.5e11 / 1.5e11)));
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / muE);
        double dt = 100d * 365d * 86400d;
        double dtReduced = dt - (Math.Floor(dt / period) * period);
        KeplerPredictor.Advance(r0, v0, muE, dt, out Vector3d pLong, out Vector3d vLong);
        KeplerPredictor.Advance(r0, v0, muE, dtReduced, out Vector3d pShort, out Vector3d vShort);
        double phaseDiff = (pLong - pShort).Magnitude;
        bool reductionWorks = phaseDiff < 1d; // без редукции ulp(1e10 рад) давал бы ~сотни метров

        bool parabolicThrows = false;
        try
        {
            // Ровно параболическая энергия: v = sqrt(2μ/r).
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
        // Малое статичное тело — корень системы (на орбите 1e9 от звезды
        // астероид летел бы 364 км/с и был бы не ловим по построению теста).
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
        // Сборка через блюпринт: приливный замок считает период по μ_local =
        // μ_родителя(собств.) + μ_поддерева — той же, что EvaluateLocalOffset.
        StarSystem sys = MakeTidalSystem(0d, false).Build();
        OrbitingBody moon = sys.AllBodies[2];
        OrbitingBody planet = sys.AllBodies[1];

        double expectedPeriod = 2d * Math.PI * Math.Sqrt(
            (2e7d * 2e7d * 2e7d) / (4e14d + 5e12d));
        bool periodOk = moon.RotationPeriodSeconds == expectedPeriod;
        bool manualIgnored = moon.RotationPeriodSeconds != 999d;

        // Многовитковая проверка (50 витков): при e=0 отклонение нулевое всегда;
        // неверная μ дала бы дрейф ~2π·(Δμ/μ) за виток ≈ 4.4°/виток — не спрятать.
        double worstPhase = 0d;
        double orbitPeriod = expectedPeriod;
        for (int k = 1; k <= 50; k++)
        {
            worstPhase = Math.Max(worstPhase, TidalDeviation(moon, planet, k * orbitPeriod));
        }

        bool phaseOk = worstPhase < 1e-6d;

        // Детерминизм: пересборка из НЕизменённого блюпринта — бит-в-бит.
        StarSystem sys2 = MakeTidalSystem(0d, false).Build();
        OrbitingBody moon2 = sys2.AllBodies[2];
        bool idempotent = moon2.RotationPeriodSeconds == moon.RotationPeriodSeconds
            && moon2.PrimeMeridianOffsetDegrees == moon.PrimeMeridianOffsetDegrees;

        // Round-trip lat/lon на захваченном теле остаётся взаимно обратным.
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

        // Лиbrация: e=0.05 → амплитуда отклонения = 2e (уравнение центра
        // ν−M ≈ 2e·sin M). Границы 1.5e..2.5e: модель «±e» упала бы снизу.
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

        // Замок на корне (звезде, без родителя) — громкое исключение.
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
