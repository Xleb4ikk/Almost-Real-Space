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
    // Прогон [0,T) покадрово: frameSim секунд симуляции за итерацию,
    // внутри кадра режем по границе control-tick пока тяга активна.
    private static double RunFramed(SpacecraftPhysics phys, EventDrivenPropagator prop, WarpController warp, Spacecraft ship, double t0, double t1, double frameSim, Func<double, bool> thrustActive)
    {
        double t = t0;
        warp.ResetTicks(t0);
        while (t < t1 - 1e-12d)
        {
            double frameEnd = Math.Min(t + frameSim, t1);
            while (t < frameEnd - 1e-12d)
            {
                bool active = thrustActive(t);
                double maxChunk = warp.GetMaxChunkSeconds(t, OrbitIntegrator.DefaultMaxStepSize, active);
                double chunk = Math.Min(maxChunk, frameEnd - t);
                if (chunk <= 0d)
                {
                    chunk = Math.Min(OrbitIntegrator.DefaultMinStepSize, frameEnd - t);
                }

                warp.SampleControl(t, active ? 1d : 0d);
                prop.Propagate(ship, t, chunk);
                t += chunk;
            }
        }

        return t;
    }

    private static void WarpPair(double posTol, double velTol, double massTol, double rtol,
        out double dPos, out double dVel, out double dMass)
    {
        StarSystem sys = TestSystem();
        var thrustA = new TestThrustSource { ThrustNewtons = 1000d, MassFlowKgS = 1d };
        thrustA.ActiveAt = (t) => t >= 5d && t < 15d;
        var thrustB = new TestThrustSource { ThrustNewtons = 1000d, MassFlowKgS = 1d };
        thrustB.ActiveAt = thrustA.ActiveAt;

        var physA = new SpacecraftPhysics { PositionAbsoluteTolerance = posTol, VelocityAbsoluteTolerance = velTol, MassAbsoluteTolerance = massTol, RelativeTolerance = rtol };
        physA.Sources.Add(new CachedGravitySource(sys));
        physA.Sources.Add(thrustA);
        var physB = new SpacecraftPhysics { PositionAbsoluteTolerance = posTol, VelocityAbsoluteTolerance = velTol, MassAbsoluteTolerance = massTol, RelativeTolerance = rtol };
        physB.Sources.Add(new CachedGravitySource(sys));
        physB.Sources.Add(thrustB);
        var propA = new EventDrivenPropagator(physA);
        var propB = new EventDrivenPropagator(physB);

        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d planetPos, out Vector3d planetVel);
        Vector3d p0 = planetPos + new Vector3d(0d, 7e6d, 0d);
        Vector3d v0 = planetVel + new Vector3d(7500d, 0d, 0d);
        var shipA = new Spacecraft(p0, v0, 1000d);
        var shipB = new Spacecraft(p0, v0, 1000d);
        var warpA = new WarpController();
        var warpB = new WarpController();
        warpA.SetWarpFactor(1d);
        warpB.SetWarpFactor(3d);

        RunFramed(physA, propA, warpA, shipA, 0d, 20d, 1d / 60d, thrustA.ActiveAt);
        RunFramed(physB, propB, warpB, shipB, 0d, 20d, 3d / 60d, thrustB.ActiveAt);

        dPos = (shipA.Position - shipB.Position).Magnitude;
        dVel = (shipA.Velocity - shipB.Velocity).Magnitude;
        dMass = Math.Abs(shipA.Mass - shipB.Mass);
    }

    private static int Test1_WarpIdentity()
    {
        // Использованные допуски — дефолты SpacecraftPhysics из P0, явно:
        // posTol=1e-2м, velTol=1e-5м/с, massTol=1e-4кг, rtol=1e-10.
        // Честная формулировка критерия: расхождение ×1/×3 — это глобальная
        // ошибка интегратора (накопление локальных atol за ~20с разной нарезки
        // чанков), а не артефакт варпа. Доказательство — скейлинг: ужесточение
        // допусков ×100 обязано сжать расхождение на порядок и более. Если бы
        // варп вносил собственную ошибку, она бы от допусков не зависела.
        WarpPair(1e-2d, 1e-5d, 1e-4d, 1e-10d, out double dPos0, out double dVel0, out double dMass0);
        WarpPair(1e-4d, 1e-7d, 1e-6d, 1e-12d, out double dPos1, out double dVel1, out double dMass1);

        bool scales = dPos1 < dPos0 / 10d && dVel1 < dVel0 / 10d && dMass1 < dMass0 / 10d;
        bool bounded = dPos0 < 0.5d && dVel0 < 1e-3d && dMass0 < 2e-3d;
        Check(scales && bounded, "T1 warp x1==x3",
            string.Format("tol=(1e-2,1e-5,1e-4): dPos={0:E3} dVel={1:E3} dMass={2:E3}; " +
                "tol/100: dPos={3:E3} dVel={4:E3} dMass={5:E3} (сжатие ~{6:F0}x/{7:F0}x/{8:F0}x, нужно >10x)",
                dPos0, dVel0, dMass0, dPos1, dVel1, dMass1,
                dPos0 / Math.Max(dPos1, 1e-18d), dVel0 / Math.Max(dVel1, 1e-18d), dMass0 / Math.Max(dMass1, 1e-18d)));
        return 0;
    }

    private static int Test2_ControlTickClip()
    {
        var warp = new WarpController();
        warp.ResetTicks(0d);
        double t = 0d;
        double maxSeen = 0d;
        bool aligned = true;
        for (int i = 0; i < 8; i++)
        {
            double chunk = warp.GetMaxChunkSeconds(t, OrbitIntegrator.DefaultMaxStepSize, true);
            if (chunk > maxSeen)
            {
                maxSeen = chunk;
            }

            double end = t + chunk;
            double k = end / WarpController.ControlTickSeconds;
            if (Math.Abs(k - Math.Round(k)) > 1e-9d)
            {
                aligned = false;
            }

            warp.SampleControl(t, 1d);
            t = end;
        }

        Check(maxSeen <= WarpController.ControlTickSeconds + 1e-12d && aligned, "T2 control-tick clip",
            string.Format("maxChunk={0:F4}с (лимит 0.5), границы по сетке 0.5с: {1}", maxSeen, aligned));
        return 0;
    }

    private static int Test3_LongWarpStopsAtEvent()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        SpacecraftPhysics phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var driver = new LongWarpDriver(phys, prop);

        planet.EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
        var ship = new Spacecraft(bp + new Vector3d(0d, planet.Radius + 200000d, 0d), bv + new Vector3d(-3000d, 0d, 0d), 1000d);
        var st = new SpacecraftIntegrationState { Position = ship.Position, Velocity = ship.Velocity, Mass = ship.Mass };
        Check(driver.CanEnterLongWarp(st, 0d), "T3 can-enter", "баллистика: вход разрешён");

        double target = 30d * 86400d;
        LongWarpResult r = driver.AdvanceToTarget(ship, 0d, target);
        bool stopped = r.StoppingEvent.HasValue && !r.ReachedTarget && r.ReachedTimeSeconds < target;
        double g = double.NaN;
        if (stopped)
        {
            var ev = r.StoppingEvent.Value;
            planet.EvaluateWorldState(ev.TimeSeconds, out Vector3d bp2, out _);
            g = (ev.State.Position - bp2).Magnitude - planet.Radius;
        }

        Check(stopped && Math.Abs(g) < 1.0d, "T3 stop-at-touchdown",
            string.Format("stop={0:F3}с из {1:F0}с, событие={2}, остаток высоты g={3:E3}м (допуск 1м)",
                r.ReachedTimeSeconds, target, stopped ? r.StoppingEvent.Value.DetectorName : "нет", g));
        return 0;
    }

    private static int Test4_SilentCancel()
    {
        StarSystem sys = TestSystem();
        var thrust = new TestThrustSource { ThrustNewtons = 500d, MassFlowKgS = 0.5d };
        thrust.ActiveAt = (t) => true;
        SpacecraftPhysics phys; EventDrivenPropagator prop;
        BuildShipPhysics(sys, out phys, out prop, thrust);
        var driver = new LongWarpDriver(phys, prop);

        var idle = new SpacecraftIntegrationState { Position = new Vector3d(1.6e11d, 0d, 0d), Velocity = new Vector3d(0d, 30000d, 0d), Mass = 1000d };
        bool blockedWhenThrust = !driver.CanEnterLongWarp(idle, 0d);

        SpacecraftPhysics phys2 = new SpacecraftPhysics();
        phys2.Sources.Add(new CachedGravitySource(sys));
        var prop2 = new EventDrivenPropagator(phys2);
        var driver2 = new LongWarpDriver(phys2, prop2);
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        var ship = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(7000d, 0d, 0d), 1000d);
        Vector3d before = ship.Position;
        driver2.RequestSilentCancel();
        LongWarpResult r = driver2.AdvanceToTarget(ship, 0d, 864000d);
        bool silent = !r.ReachedTarget && r.ReachedTimeSeconds == 0d && (ship.Position - before).Magnitude == 0d;

        Check(blockedWhenThrust && silent, "T4 silent-cancel",
            string.Format("вход при тяге запрещён: {0}; отмена до RHS: ReachedTarget={1}, t={2:F1}с, корабль не сдвинут: {3}",
                blockedWhenThrust, r.ReachedTarget, r.ReachedTimeSeconds, silent));
        return 0;
    }

    private static int Test5_Singularity()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bp, out _);

        bool insideInvalid = !sys.TryEvaluateAcceleration(bp, 0d, out _);

        SpacecraftPhysics phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var ship = new Spacecraft(bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d), bv0 + new Vector3d(-2000d, -500d, 0d), 1000d);
        EventOccurrence? hit = prop.Propagate(ship, 0d, 600d);
        bool eventFired = hit.HasValue && hit.Value.DetectorName.StartsWith("Touchdown");

        Vector3d near = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bp + new Vector3d(1e-9d, 0d, 0d), bp, 3.986e14d);
        bool finite = near.IsFinite;

        Check(insideInvalid && eventFired && finite, "T5 singularity",
            string.Format("TryEvaluate внутри=false: {0}; Touchdown-событие: {1}; r=1e-9м конечен: {2} ({3:E2})",
                insideInvalid, eventFired, finite, near.Magnitude));
        return 0;
    }

    private static int Test6_CacheIdentical()
    {
        StarSystem sys = TestSystem();
        var rnd = new Random(42);
        double[] times = new double[] { 0d, 12.5d, 100d, 3600.25d, 86400.125d };
        var shipPos = new Vector3d(1.55e11d, 2e9d, 1e8d);

        Vector3d off = new Vector3d(1d, 0d, 0d);
        OrbitingBody.ResetEvaluationCounters();
        sys.InvalidatePositionCache();
        var naive = new List<Vector3d>();
        var naiveOff = new List<Vector3d>();
        for (int i = 0; i < times.Length; i++)
        {
            naive.Add(sys.EvaluateShipAcceleration(shipPos, times[i]));
            naiveOff.Add(sys.EvaluateShipAcceleration(shipPos + off, times[i]));
        }

        long naiveWorld = OrbitingBody.WorldStateEvaluationCount;
        long naiveLocal = OrbitingBody.LocalOffsetEvaluationCount;

        OrbitingBody.ResetEvaluationCounters();
        sys.InvalidatePositionCache();
        sys.CacheHits = 0;
        sys.CacheMisses = 0;
        var cached = new List<Vector3d>();
        var cachedOff = new List<Vector3d>();
        for (int i = 0; i < times.Length; i++)
        {
            cached.Add(sys.EvaluateShipAccelerationCached(shipPos, times[i]));
            // повтор в то же время — обязан попасть в мемоизацию (FSAL-кейс)
            cachedOff.Add(sys.EvaluateShipAccelerationCached(shipPos + off, times[i]));
        }

        long cachedLocal = OrbitingBody.LocalOffsetEvaluationCount;
        bool identical = true;
        for (int i = 0; i < times.Length; i++)
        {
            if (naive[i].X != cached[i].X || naive[i].Y != cached[i].Y || naive[i].Z != cached[i].Z)
            {
                identical = false;
            }

            if (naiveOff[i].X != cachedOff[i].X || naiveOff[i].Y != cachedOff[i].Y || naiveOff[i].Z != cachedOff[i].Z)
            {
                identical = false;
            }
        }

        // Наивный путь: на каждое тело рекурсивный EvaluateWorldState (каждый
        // тянет родителей) => world-вызовов 2×N×глубина; кэш: 1 local на
        // некорневое тело за distinct-время + мемоизация повторов.
        // Определения счётчиков, чтобы строка читалась однозначно:
        // world/local — вызовы OrbitingBody.EvaluateWorldState / локального оффсета;
        // hits/misses — вызовы StarSystem.EnsureCachedPositions (их 10 = 5 времён ×
        // 2 точки; каждый miss вычисляет по 1 local на каждое из 3 некорневых тел,
        // отсюда local=15 при misses=5; каждый hit — 0 вычислений).
        // Наивно (10 вызовов × 4 тела): world=80 (рекурсия тянет родителей:
        // корень 1 + планета 2 + луна 3 + планета 2 = 8 за вызов), local=40.
        bool fewer = cachedLocal < naiveLocal;
        Check(identical && fewer && sys.CacheHits == times.Length, "T6 cache bit-identical",
            string.Format("бит-в-бит: {0}; наивно world={1} local={2}, кэш local={3} (=misses({4})×3 тела); кэш-вызовов hits={5}+misses={4}=10",
                identical, naiveWorld, naiveLocal, cachedLocal, sys.CacheMisses, sys.CacheHits));
        return 0;
    }

    private static int Test7_BudgetPriority()
    {
        // Очередь — 256 настоящих Tier1-продвижений (без слипов): каждый Action
        // делает DebrisUpdater.AdvanceSlot (физика + предикат). Плюс 1 действие
        // активного корабля = 257 действий в логе (не путать с T9: там 257-й
        // объект, здесь 256 объектов + активный).
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        var pool = new DebrisPool();
        for (int i = 0; i < 256; i++)
        {
            pool.Spawn(pp + new Vector3d(0d, 8e6d + i * 1000d, 0d), pv + new Vector3d(7000d, 0d, 0d), 10d, 0d, 1e7d);
        }

        var updater = new DebrisUpdater(sys);
        var sched = new FrameBudgetScheduler();
        var order = new List<string>();
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            int k = i;
            sched.EnqueueDebris(() => { if (updater.AdvanceSlot(pool, k, 0d, pp)) { order.Add("d" + k); } else { order.Add("d" + k + "!"); } });
        }

        var sw = new Stopwatch();
        sw.Start();
        sched.RunFrame(() => order.Add("active"), sw);
        sw.Stop();
        bool activeFirst = order.Count > 0 && order[0] == "active";
        int firstFrameDebris = order.Count - 1;
        double firstMs = sw.Elapsed.TotalMilliseconds;

        int frames = 1;
        double maxMs = firstMs;
        while (sched.PendingCount > 0 && frames < 100)
        {
            sw.Restart();
            sched.RunFrame(() => { }, sw);
            sw.Stop();
            if (sw.Elapsed.TotalMilliseconds > maxMs)
            {
                maxMs = sw.Elapsed.TotalMilliseconds;
            }

            frames++;
        }

        // 256 debris-действий + 1 active = 257 записей лога; все 256 слотов
        // обработаны (остаток очереди 0), кадры ~4мс каждый.
        bool allDone = sched.PendingCount == 0 && order.Count == 257;
        double avgPerFrame = (double)(order.Count - 1) / Math.Max(1, frames);
        Check(activeFirst && allDone && frames <= 32 && maxMs < 10d, "T7 budget+slicing",
            string.Format("активный первым: {0}; кадров {1}, среднее {2:F1} мусора/кадр, 1-й кадр {3} шт за {4:F2}мс, макс кадр {5:F2}мс (бюджет 4мс); итого записей {6} (=256 мусор+1 active), остаток очереди 0: {7}",
                activeFirst, frames, avgPerFrame, firstFrameDebris, firstMs, maxMs, order.Count, sched.PendingCount == 0));
        return 0;
    }

    private static int Test8_PoolNoAllocs()
    {
        StarSystem sys = TestSystem();
        var pool = new DebrisPool();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        for (int i = 0; i < 10; i++)
        {
            pool.Spawn(pp + new Vector3d(0d, 8e6d + i * 5000d, 0d), pv + new Vector3d(7000d, 0d, 0d), 10d, 0d, 1e7d);
        }

        var before = new object[DebrisPool.Capacity];
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            before[i] = pool.SlotAt(i).Body;
        }

        var updater = new DebrisUpdater(sys);
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 20; k++)
        {
            updater.AdvanceAll(pool, k * 1d, pp);
        }

        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();
        bool sameObjects = true;
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            if (!ReferenceEquals(before[i], pool.SlotAt(i).Body))
            {
                sameObjects = false;
            }
        }

        long allocated = bytesAfter - bytesBefore;
        Check(sameObjects && allocated < 1024 * 1024, "T8 pool-no-new",
            string.Format("объекты Body те же: {0}; аллокаций за 20 циклов: {1} байт (порог 1МБ)", sameObjects, allocated));
        return 0;
    }

    private static int Test9_Eviction()
    {
        var pool = new DebrisPool();
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            pool.Spawn(new Vector3d(i, 0d, 0d), Vector3d.Zero, 10d, 0d, 1e7d);
        }

        long minSeqBefore = long.MaxValue;
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            long s = pool.SlotAt(i).Sequence;
            if (s < minSeqBefore)
            {
                minSeqBefore = s;
            }
        }

        pool.Spawn(new Vector3d(9999d, 0d, 0d), Vector3d.Zero, 10d, 0d, 1e7d);
        long minSeqAfter = long.MaxValue;
        bool hasNewcomer = false;
        for (int i = 0; i < DebrisPool.Capacity; i++)
        {
            long s = pool.SlotAt(i).Sequence;
            if (s < minSeqAfter)
            {
                minSeqAfter = s;
            }

            if (pool.SlotAt(i).Body.Position.X == 9999d)
            {
                hasNewcomer = true;
            }
        }

        Check(pool.ActiveCount == DebrisPool.Capacity && hasNewcomer && minSeqAfter > minSeqBefore, "T9 eviction-256",
            string.Format("active={0}/256, новичок внутри: {1}, minSeq {2}->{3}", pool.ActiveCount, hasNewcomer, minSeqBefore, minSeqAfter));
        return 0;
    }

    private static int Test10_SleepError()
    {
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        Vector3d p0 = pp + new Vector3d(0d, 2e7d, 0d);
        Vector3d v0 = pv + new Vector3d(5000d, 0d, 0d);

        var tight = new SpacecraftPhysics();
        tight.Sources.Add(new CachedGravitySource(sys));
        var fine = new Spacecraft(p0, v0, 500d);
        double t = 0d;
        double T = 3600d;
        while (t < T - 1e-9d)
        {
            double h = Math.Min(1d, T - t);
            tight.Step(fine, t, h);
            t += h;
        }

        var updater = new DebrisUpdater(sys);
        var pool = new DebrisPool();
        int idx = pool.Spawn(p0, v0, 500d, 0d, 1e7d);
        t = 0d;
        while (t < T - 1e-9d)
        {
            updater.AdvanceSlot(pool, idx, t, pp);
            t = pool.SlotAt(idx).NextCheckTimeSeconds;
        }

        DebrisSlot slot = pool.SlotAt(idx);
        double div = slot.Active ? (slot.Body.Position - fine.Position).Magnitude : double.NaN;
        const double bound = 10000d;
        Check(slot.Active && div < bound, "T10 sleep-error",
            string.Format("расхождение 1с-шаги vs 30с-прыжки за 3600с: {0:F1}м (граница {1:F0}м для мусора)", div, bound));
        return 0;
    }

    private static int Test11_MultiCrossing()
    {
        // Долг P1b: нырок туда-обратно внутри одного 600с чанка. Старт на 50км
        // (внутри 100км оболочки) вверх 2000м/с: выход ~27с, апогей ~204км,
        // возврат ~381с. На границах чанка g0<0 и g1<0 — старое поведение
        // (DetectionSubdivisions=1) обязано промахнуться, новое — найти оба.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d p0 = bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d);
        Vector3d v0 = bv0 + new Vector3d(500d, 2000d, 0d);

        var physOld = new SpacecraftPhysics();
        physOld.Sources.Add(new CachedGravitySource(sys));
        var propOld = new EventDrivenPropagator(physOld);
        propOld.DetectionSubdivisions = 1;
        propOld.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        propOld.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        var shipOld = new Spacecraft(p0, v0, 1000d);
        EventOccurrence? missed = propOld.Propagate(shipOld, 0d, 600d);

        var physNew = new SpacecraftPhysics();
        physNew.Sources.Add(new CachedGravitySource(sys));
        var propNew = new EventDrivenPropagator(physNew);
        propNew.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        propNew.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        var shipNew = new Spacecraft(p0, v0, 1000d);
        var names = new List<string>();
        var times = new List<double>();
        double t = 0d;
        while (t < 600d - 1e-9d && names.Count <= 5)
        {
            EventOccurrence? ev = propNew.Propagate(shipNew, t, 600d - t);
            if (!ev.HasValue)
            {
                break;
            }

            names.Add(ev.Value.DetectorName);
            times.Add(ev.Value.TimeSeconds);
            t = ev.Value.TimeSeconds;
        }

        bool oldMisses = !missed.HasValue;
        bool order = names.Count == 2
            && names[0].StartsWith("AtmosphereExit")
            && names[1].StartsWith("AtmosphereEntry");
        bool windows = order && times[0] > 15d && times[0] < 40d && times[1] > 360d && times[1] < 410d;
        Check(oldMisses && order && windows, "T11 multi-crossing",
            string.Format("subdiv=1 промах: {0}; subdiv=20: [{1}] t=[{2:F1}с, {3:F1}с] (окна [15,40]/[360,410])",
                oldMisses, string.Join(",", names.ToArray()),
                times.Count > 0 ? times[0] : double.NaN, times.Count > 1 ? times[1] : double.NaN));
        return 0;
    }

    private static int Test12_Depletion()
    {
        // T=2000Н, Isp=300с: mdot=0.6796кг/с, 100кг топлива = 147.14с работы.
        // Газ постоянно открыт — отсечка только по событию, не по времени.
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        var thrust = new ThrustSource(2000d, 900d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => 1d;
        thrust.ThrustDirection = new Vector3d(0d, 1d, 0d);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(thrust);
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(new PropellantDepletionDetector(900d, "main"));
        var ship = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(5300d, 0d, 0d), 1000d);

        EventOccurrence? hit = prop.Propagate(ship, 0d, 600d);
        double mdot = 2000d / (300d * ThrustSource.StandardGravity);
        double tExpect = 100d / mdot;
        bool fired = hit.HasValue && hit.Value.DetectorName.StartsWith("PropellantDepleted");
        double dtEvent = fired ? Math.Abs(hit.Value.TimeSeconds - tExpect) : double.NaN;
        bool massOk = Math.Abs(ship.Mass - 900d) < 1e-3d && ship.Mass >= 900d - 1e-3d;
        DynamicsContribution post = thrust.Evaluate(ship.Position, ship.Velocity, ship.Mass, hit.HasValue ? hit.Value.TimeSeconds : 600d);
        bool flamedOut = post.Force.SqrMagnitude == 0d && post.MassFlow == 0d;

        thrust.ThrottleAt = (time) => 0d;
        var driver = new LongWarpDriver(phys, prop);
        var idle = new SpacecraftIntegrationState { Position = ship.Position, Velocity = ship.Velocity, Mass = ship.Mass };
        bool coastAllowed = driver.CanEnterLongWarp(idle, 600d);

        Check(fired && dtEvent < 2d && massOk && flamedOut && coastAllowed, "T12 depletion",
            string.Format("событие: {0} t={1:F2}с (аналитика {2:F2}с, ±2с); масса={3:F4}кг (≈900); заглох: {4}; coast при газе 0 разрешён: {5}",
                fired, fired ? hit.Value.TimeSeconds : double.NaN, tExpect, ship.Mass, flamedOut, coastAllowed));
        return 0;
    }

    private static int Test13_Tsiolkovsky()
    {
        // Контрольный прогон B (баллистика) вычитает гравитацию: разность A−B —
        // чистый эффект тяги, сверяется с Δv=Isp·g0·ln(m0/m1). Плюс точная масса.
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        Vector3d p0 = pp + new Vector3d(0d, 8e6d, 0d);
        Vector3d v0 = pv + new Vector3d(5300d, 0d, 0d);

        var thrust = new ThrustSource(1000d, 500d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => time < 10d ? 1d : 0d;
        thrust.ThrustDirection = new Vector3d(1d, 0d, 0d);
        var physA = new SpacecraftPhysics();
        physA.Sources.Add(new CachedGravitySource(sys));
        physA.Sources.Add(thrust);
        var physB = new SpacecraftPhysics();
        physB.Sources.Add(new CachedGravitySource(sys));
        var shipA = new Spacecraft(p0, v0, 1000d);
        var shipB = new Spacecraft(p0, v0, 1000d);
        physA.Step(shipA, 0d, 10d);
        physB.Step(shipB, 0d, 10d);

        double mdot = 1000d / (300d * ThrustSource.StandardGravity);
        double m1 = 1000d - mdot * 10d;
        double analyticDv = 300d * ThrustSource.StandardGravity * Math.Log(1000d / m1);
        double measuredDv = (shipA.Velocity - shipB.Velocity).Magnitude;
        double dDv = Math.Abs(measuredDv - analyticDv);
        double dMass = Math.Abs(shipA.Mass - m1);
        Check(dDv < 0.05d && dMass < 1e-4d, "T13 tsiolkovsky",
            string.Format("Δv={0:F4}м/с vs Циолковский {1:F4} (Δ={2:E2}, допуск 0.05); масса Δ={3:E2}кг",
                measuredDv, analyticDv, dDv, dMass));
        return 0;
    }

    private static int Test14_BurnWarpIdentity()
    {
        // Почти круговая высокая орбита + гладкий 10с прожиг: ошибка усечения
        // ниже округлений double, поэтому ужесточение допусков НЕ сжимает
        // расхождение (проверено отладкой: chemical vs tight бит-в-бит, а coast
        // детерминирован бит-в-бит). Здесь другой режим, чем в T1: там динамика
        // с эксцентриситетом, доминирует усечение и скейлинг работает; здесь
        // доминирует path-dependence округлений (~17 ulp при |pos|~1.5e11 —
        // неустранимо в double при разной нарезке чанков 619 vs 219).
        // Поэтому критерий честный другой: ограниченность + детерминизм
        // (та же нарезка дважды = бит-в-бит ноль).
        EvaluatorPair(BurnTolerancePreset.Chemical, out double dPos0, out double dVel0, out double dMass0);
        EvaluatorRun(BurnTolerancePreset.Chemical, 1d / 60d, out Vector3d pA, out Vector3d vA, out double mA);
        EvaluatorRun(BurnTolerancePreset.Chemical, 1d / 60d, out Vector3d pB, out Vector3d vB, out double mB);
        double rePos = (pA - pB).Magnitude;
        double reVel = (vA - vB).Magnitude;
        double reMass = Math.Abs(mA - mB);

        bool bounded = dPos0 < 0.5d && dVel0 < 1e-3d && dMass0 < 2e-3d;
        bool deterministic = rePos == 0d && reVel == 0d && reMass == 0d;
        Check(bounded && deterministic, "T14 burn x1==x3",
            string.Format("x1/x3: dPos={0:E3}м dVel={1:E3} dMass={2:E3} (пороги 0.5/1e-3/2e-3); повтор той же нарезки: dPos={3:E1} dVel={4:E1} dMass={5:E1} (бит-в-бит: {6})",
                dPos0, dVel0, dMass0, rePos, reVel, reMass, deterministic));
        return 0;
    }

    private static int Test15_IspAltitude()
    {
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp, out _);

        var constModel = new ConstantIsp(320d);
        bool constOk = constModel.GetSpecificImpulseSeconds(bp, 0d) == 320d;

        var altModel = new AltitudeInterpolatedIsp(planet, 250d, 320d);
        double ispGround = altModel.GetSpecificImpulseSeconds(bp + new Vector3d(planet.Radius, 0d, 0d), 0d);
        double halfH = 8500d * Math.Log(2d);
        double ispHalf = altModel.GetSpecificImpulseSeconds(bp + new Vector3d(planet.Radius + halfH, 0d, 0d), 0d);
        double ispHigh = altModel.GetSpecificImpulseSeconds(bp + new Vector3d(planet.Radius + 500000d, 0d, 0d), 0d);
        double isp10k = altModel.GetSpecificImpulseSeconds(bp + new Vector3d(planet.Radius + 10000d, 0d, 0d), 0d);
        double isp50k = altModel.GetSpecificImpulseSeconds(bp + new Vector3d(planet.Radius + 50000d, 0d, 0d), 0d);
        bool shape = Math.Abs(ispGround - 250d) < 1e-9d
            && Math.Abs(ispHalf - 285d) < 1e-6d
            && Math.Abs(ispHigh - 320d) < 1e-6d
            && isp10k < isp50k && isp50k < ispHigh;

        var thrustSL = new ThrustSource(1000d, 100d, altModel);
        thrustSL.ThrottleAt = (time) => 1d;
        thrustSL.ThrustDirection = new Vector3d(0d, 1d, 0d);
        DynamicsContribution cSL = thrustSL.Evaluate(bp + new Vector3d(planet.Radius, 0d, 0d), Vector3d.Zero, 1000d, 0d);
        DynamicsContribution cVac = thrustSL.Evaluate(bp + new Vector3d(planet.Radius + 500000d, 0d, 0d), Vector3d.Zero, 1000d, 0d);
        double ratio = cSL.MassFlow / cVac.MassFlow;
        bool flowRatio = Math.Abs(ratio - 320d / 250d) < 0.01d;

        OrbitingBody moon = sys.AllBodies[2];
        var noAtm = new AltitudeInterpolatedIsp(moon, 250d, 320d);
        bool noAtmVac = noAtm.GetSpecificImpulseSeconds(bp, 0d) == 320d;

        Check(constOk && shape && flowRatio && noAtmVac, "T15 isp-altitude",
            string.Format("const=320: {0}; Isp(0)={1:F1} Isp(H·ln2)={2:F3} Isp(500км)={3:F4} монотонна: {4}; flowSL/flowVac={5:F4} (≈1.28): {6}; без атмосферы=vac: {7}",
                constOk, ispGround, ispHalf, ispHigh, isp10k < isp50k, ratio, flowRatio, noAtmVac));
        return 0;
    }

    private static int Test16_PresetIsParameter()
    {
        // Кастомный грубый пресет доводит прожиг до конца, а допуски физики
        // после вызова равны тем, что были до, — пресет параметр, не архитектура.
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        var thrust = new ThrustSource(1000d, 500d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => time >= 5d && time < 10d ? 1d : 0d;
        thrust.ThrustDirection = new Vector3d(0d, 1d, 0d);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(thrust);
        double savedPos = phys.PositionAbsoluteTolerance;
        double savedVel = phys.VelocityAbsoluteTolerance;
        double savedMass = phys.MassAbsoluteTolerance;
        double savedRt = phys.RelativeTolerance;
        var coastProp = new EventDrivenPropagator(phys);
        var burnProp = new EventDrivenPropagator(phys);
        var driver = new LongWarpDriver(phys, coastProp);
        var warp = new WarpController();
        var evaluator = new ManeuverEvaluator(phys, coastProp, driver, burnProp, warp);
        var ship = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(5300d, 0d, 0d), 1000d);

        var custom = new BurnTolerancePreset(1d, 1e-1d, 1e-1d, 1e-8d);
        ManeuverResult r = evaluator.CoastThenBurn(ship, 0d, 5d, 10d, custom, 1d / 60d);
        double mdot = 1000d / (300d * ThrustSource.StandardGravity);
        double dMass = Math.Abs(r.EndState.Mass - (1000d - mdot * 5d));
        bool restored = phys.PositionAbsoluteTolerance == savedPos
            && phys.VelocityAbsoluteTolerance == savedVel
            && phys.MassAbsoluteTolerance == savedMass
            && phys.RelativeTolerance == savedRt;

        Check(r.CompletedBurn && !r.StoppingEvent.HasValue && r.EndState.Position.IsFinite && dMass < 0.05d && restored, "T16 preset-parameter",
            string.Format("burn завершён: {0}; масса Δ={1:E2}кг (0.05); допуски восстановлены: {2}",
                r.CompletedBurn, dMass, restored));
        return 0;
    }

    private sealed class CountingSource : IDynamicsSource
    {
        public long Calls;
        private readonly IDynamicsSource inner;

        public CountingSource(IDynamicsSource inner)
        {
            this.inner = inner;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            Calls++;
            return inner.Evaluate(position, velocity, mass, timeSeconds);
        }
    }

    private static int Test18_DenseGate()
    {
        // Gate A1: dense-бисекция vs legacy-перепогон на тех же сценариях.
        // ppb-сценарий T3 (touchdown) и двухкорневой T11 (exit+entry).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);

        // Сценарий 1: падение на поверхность (один корень).
        Vector3d fallP = bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d);
        Vector3d fallV = bv0 + new Vector3d(-3000d, 0d, 0d);
        EventOccurrence? legacyFall = RunLegacy(sys, planet, fallP, fallV, 1000d, 30d * 86400d, false);
        EventOccurrence? denseFall = RunDense(sys, planet, fallP, fallV, 1000d, 30d * 86400d, false, out long legacyCallsF, out long denseCallsF);

        // Сценарий 2: нырок туда-обратно (два корня).
        Vector3d dipP = bp0 + new Vector3d(0d, planet.Radius + 50000d, 0d);
        Vector3d dipV = bv0 + new Vector3d(500d, 2000d, 0d);
        var legacyDip = CollectAll(sys, planet, dipP, dipV, false);
        var denseDip = CollectAll(sys, planet, dipP, dipV, true);

        bool fallMatch = legacyFall.HasValue && denseFall.HasValue
            && Math.Abs(legacyFall.Value.TimeSeconds - denseFall.Value.TimeSeconds) <= 1e-6d;
        bool dipMatch = legacyDip.Count == 2 && denseDip.Count == 2
            && Math.Abs(legacyDip[0].TimeSeconds - denseDip[0].TimeSeconds) <= 1e-6d
            && Math.Abs(legacyDip[1].TimeSeconds - denseDip[1].TimeSeconds) <= 1e-6d;

        // Рестарт из dense-корня: переоткрытия нет (инвариант b доказан T18, не словами).
        var physR = new SpacecraftPhysics();
        physR.Sources.Add(new CachedGravitySource(sys));
        var propR = new EventDrivenPropagator(physR);
        propR.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipR = new Spacecraft(denseFall.Value.State.Position, denseFall.Value.State.Velocity, denseFall.Value.State.Mass);
        EventOccurrence? reopen = propR.Propagate(shipR, denseFall.Value.TimeSeconds, 600d);

        // Рендер: coast-дуга 3600с без событий, 100 сэмплов без единого Step.
        var physW = new SpacecraftPhysics();
        var countW = new CountingSource(new CachedGravitySource(sys));
        physW.Sources.Add(countW);
        var propW = new EventDrivenPropagator(physW);
        var driverW = new LongWarpDriver(physW, propW);
        var shipW = new Spacecraft(fallP, fallV, 1000d);
        var render = new List<DenseSegment>();
        LongWarpResult wr = driverW.AdvanceToTargetWithSegments(shipW, 0d, 3600d, render);
        long callsAfterWarp = countW.Calls;
        // Честная непрерывность. Два известных эффекта, оба не разрывы:
        // (1) StepForward оставляет неинтегрированный остаток ≤ MinStepSize
        // в конце чанка (старое поведение): gap вперёд до 1e-6с, скачок в v·gap.
        // (2) Время конца чанка драйвер считает одним сложением, интегратор —
        // суммой внутренних dt: стык может перекрываться на fp-шум (~1e-13с)
        // при нулевом скачке состояния. Допуск назад — 1e-9с.
        bool trackOk = render.Count > 0;
        bool continuous = true;
        int badJoint = -1;
        double badGap = 0d;
        double badJump = 0d;
        for (int i = 1; i < render.Count; i++)
        {
            double gap = render[i].T0 - render[i - 1].T1;
            double jump = (render[i].Y0.Position - render[i - 1].Y1.Position).Magnitude;
            double vmax = render[i - 1].Y1.Velocity.Magnitude;
            double jumpBound = gap > 0d ? vmax * gap + 1e-9d : 1e-9d;
            if (gap < -1e-9d || gap > OrbitIntegrator.DefaultMinStepSize || jump > jumpBound)
            {
                if (badJoint < 0)
                {
                    badJoint = i;
                    badGap = gap;
                    badJump = jump;
                }

                continuous = false;
            }
        }

        bool samplesFinite = true;
        SpacecraftIntegrationState s0 = DenseSegment.EvaluateTrack(render, 0d);
        SpacecraftIntegrationState s1 = DenseSegment.EvaluateTrack(render, 3600d);
        for (int i = 0; i < 100; i++)
        {
            SpacecraftIntegrationState s = DenseSegment.EvaluateTrack(render, 3600d * i / 99d);
            if (!s.Position.IsFinite || !s.Velocity.IsFinite || double.IsNaN(s.Mass) || double.IsInfinity(s.Mass))
            {
                samplesFinite = false;
            }
        }

        bool endsMatch = s0.Position.X == fallP.X && s0.Position.Y == fallP.Y && s0.Position.Z == fallP.Z
            && s1.Position.X == shipW.Position.X && s1.Position.Y == shipW.Position.Y && s1.Position.Z == shipW.Position.Z;
        bool noStepInSampling = countW.Calls == callsAfterWarp;

        Check(fallMatch && dipMatch && !reopen.HasValue && trackOk && continuous && samplesFinite && endsMatch && noStepInSampling, "T18 dense-gate",
            string.Format("корни legacy-vs-dense: падение Δt={0:E2}с, нырок Δ=[{1:E2},{2:E2}] (допуск 1e-6); RHS уточнение legacy={3} vs dense={4}; рестарт=null: {5}; трек: сегментов {6}, непрерывен: {7} (первый рваный стык {11}: gap={12:E2}с jump={13:E2}м), 100 сэмплов конечны: {8}, концы совпали: {9}, Step при сэмплинге: {10}",
                Math.Abs(legacyFall.Value.TimeSeconds - denseFall.Value.TimeSeconds),
                Math.Abs(legacyDip[0].TimeSeconds - denseDip[0].TimeSeconds),
                Math.Abs(legacyDip[1].TimeSeconds - denseDip[1].TimeSeconds),
                legacyCallsF, denseCallsF, !reopen.HasValue,
                render.Count, continuous, samplesFinite, endsMatch, noStepInSampling,
                badJoint, badGap, badJump));
        return 0;
    }

    private static int Test19_AdaptiveGrain()
    {
        // Правило «только мельче»: медленная круговая обязана остаться на базе 20
        // (регрессия запрещена), быстрый гиперболический graze — дробиться до капа.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        double earthR = planet.Radius;
        double mu = planet.ResolveStandardGravitationalParameter();

        int slowRule = DetectionGrain.Subdivisions(600d, 20, 7120d);
        int fastRule = DetectionGrain.Subdivisions(600d, 20, 20d);
        int nanRule = DetectionGrain.Subdivisions(600d, 20, double.NaN);
        int infRule = DetectionGrain.Subdivisions(600d, 20, double.PositiveInfinity);

        // Высокая круговая: r = R+8e6, T = 2π√(r³/μ) ≈ 17145с → зерно 30с, subdiv 20.
        double circR = earthR + 8e6d;
        double circV = Math.Sqrt(mu / circR);
        var circState = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, circR, 0d),
            Velocity = bv0 + new Vector3d(circV, 0d, 0d),
            Mass = 1000d
        };
        double tauCirc = DetectionGrain.CharacteristicTimeSeconds(sys, circState, 0d);
        int subCirc = DetectionGrain.Subdivisions(600d, 20, tauCirc);

        // Гипербола v_inf=60км/с, r_p=R+50км (внутри 100км оболочки):
        // |a|=μ/v²=110722м, τ=2π√(|a|³/μ)≈11.6с → зерно 0.58с → кап 200 (3с).
        // Состояние на ν=-25° до перицентра (экваториальная плоскость, перицентр +X):
        // r=7.073e6м, v=(430, 60926, 0)м/с. Нырок ~26с: вход ~34с, выход ~60с.
        var flyState = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(6.410e6d, -2.989e6d, 0d),
            Velocity = bv0 + new Vector3d(430d, 60926d, 0d),
            Mass = 1000d
        };
        double tauFly = DetectionGrain.CharacteristicTimeSeconds(sys, flyState, 0d);
        int subFly = DetectionGrain.Subdivisions(600d, 20, tauFly);

        var physF = new SpacecraftPhysics();
        physF.Sources.Add(new CachedGravitySource(sys));
        var propF = new EventDrivenPropagator(physF);
        propF.SystemForGrain = sys;
        propF.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        propF.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(planet));
        var shipF = new Spacecraft(flyState.Position, flyState.Velocity, flyState.Mass);
        var flyEvents = new List<EventOccurrence>();
        double tf = 0d;
        while (tf < 1200d - 1e-9d && flyEvents.Count <= 5)
        {
            EventOccurrence? ev = propF.Propagate(shipF, tf, 1200d - tf);
            if (!ev.HasValue)
            {
                break;
            }

            flyEvents.Add(ev.Value);
            tf = ev.Value.TimeSeconds;
        }

        bool flyOk = flyEvents.Count == 2
            && flyEvents[0].DetectorName.StartsWith("AtmosphereEntry")
            && flyEvents[1].DetectorName.StartsWith("AtmosphereExit")
            && flyEvents[0].TimeSeconds > 20d && flyEvents[0].TimeSeconds < 50d
            && flyEvents[1].TimeSeconds > 45d && flyEvents[1].TimeSeconds < 80d;

        // Убегание без детекторов: фолбэк жив, исключений нет.
        var escState = new SpacecraftIntegrationState
        {
            Position = bp0 + new Vector3d(0d, circR, 0d),
            Velocity = bv0 + new Vector3d(12000d, 0d, 0d),
            Mass = 1000d
        };
        double tauEsc = DetectionGrain.CharacteristicTimeSeconds(sys, escState, 0d);
        var physE = new SpacecraftPhysics();
        physE.Sources.Add(new CachedGravitySource(sys));
        var propE = new EventDrivenPropagator(physE);
        propE.SystemForGrain = sys;
        var shipE = new Spacecraft(escState.Position, escState.Velocity, escState.Mass);
        EventOccurrence? escHit = null;
        bool escThrow = false;
        try
        {
            escHit = propE.Propagate(shipE, 0d, 600d);
        }
        catch
        {
            escThrow = true;
        }

        bool rules = slowRule == 20 && fastRule == 200 && nanRule == 20 && infRule == 20;
        bool regression = subCirc == 20 && tauCirc > 16000d && tauCirc < 18000d;
        bool hyper = subFly == 200 && tauFly > 8d && tauFly < 16d;
        bool escape = !escThrow && !escHit.HasValue && !double.IsNaN(tauEsc) && !double.IsInfinity(tauEsc) && tauEsc > 0d;
        Check(rules && regression && hyper && flyOk && escape, "T19 adaptive-grain",
            string.Format("правило: slow={0} fast={1} nan={2} inf={3} (20/200/20/20); круговая τ={4:F0}с subdiv={5}; гипербола τ={6:F1}с subdiv={7}; graze: [{8}] t=[{9:F1},{10:F1}]с; убегание τ={11:F1}с без исключений: {12}",
                slowRule, fastRule, nanRule, infRule, tauCirc, subCirc, tauFly, subFly,
                string.Join(",", flyEvents.ConvertAll(e => e.DetectorName).ToArray()),
                flyEvents.Count > 0 ? flyEvents[0].TimeSeconds : double.NaN,
                flyEvents.Count > 1 ? flyEvents[1].TimeSeconds : double.NaN,
                tauEsc, escape));
        return 0;
    }

    private static ManeuverEvaluator BuildEvaluator(SpacecraftPhysics phys, double dryMassKg, out WarpController warp)
    {
        var coastProp = new EventDrivenPropagator(phys);
        var burnProp = new EventDrivenPropagator(phys);
        burnProp.CrossingDetectors.Add(new PropellantDepletionDetector(dryMassKg, "main"));
        var driver = new LongWarpDriver(phys, coastProp);
        warp = new WarpController();
        return new ManeuverEvaluator(phys, coastProp, driver, burnProp, warp);
    }

    private static int Test52_Centrifugal()
    {
        // Быстрый спин (ω²R > g) обязан ОТРЫВАТЬ покоящийся со-вращающийся
        // корабль: центробежная −ω×(ω×r) направлена наружу. Баг был в знаке
        // (+ω×(ω×r) — внутрь): отрыв не наступал никогда.
        SurfaceMotionResult fast = SpinCase(omega2R: 10d, g0: 9.8d);
        SurfaceMotionResult slow = SpinCase(omega2R: 5d, g0: 9.8d);
        bool fastOk = fast.Regime == VesselRegime.Flying;
        bool slowOk = slow.Regime == VesselRegime.Landed && slow.IsStuck;

        // Контакт держится ⟺ a_free·n < 0: у медленного спина прижатие
        // гравитацией минус центробежная должно быть положительным по модулю.
        Check(fastOk && slowOk, "T52 centrifugal",
            string.Format("ω²R=10>g=9.8 → {0} (ожидался Flying); ω²R=5<g → {1} (ожидался Landed)",
                fast.Regime, slow.Regime));
        return 0;
    }

    private static SurfaceMotionResult SpinCase(double omega2R, double g0)
    {
        double radius = 1e6d;
        double mu = g0 * radius * radius;
        double omega = Math.Sqrt(omega2R / radius);
        var body = new OrbitingBody
        {
            Name = "SpinBody",
            Radius = radius,
            StandardGravitationalParameter = mu,
            RotationPeriodSeconds = (2d * Math.PI) / omega,
            NorthPoleDirection = new Vector3d(0d, 0d, 1d)
        };
        body.SyncMassFromGravitationalParameter();
        // Корень в нуле, статичен — гравитация чисто центральная от origin.
        Vector3d pos = new Vector3d(radius, 0d, 0d);
        Vector3d vel = new Vector3d(0d, omega * radius, 0d); // со-вращение
        SurfaceMotion.GravityProvider grav = (p, t) =>
            PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, Vector3d.Zero, mu);
        return SurfaceMotion.Step(pos, vel, body, new SphereOnlyTerrain(), grav, 0d, 0.5d);
    }

    private static int Test53_LambertHalfPlane()
    {
        // 180°-геометрия: dt обязан влиять на решение, longWay обязан зеркалить.
        // Баг: форс a=(r1+r2)/2 — любой dt возвращал один и тот же Hohmann.
        Vector3d r1 = new Vector3d(1e8, 0, 0);
        Vector3d r2 = new Vector3d(-2e8, 0, 0);
        double mu = 3.986e14;
        double[] dts = { 3.0e5, 5.0e5, 1.0e6, 1.0e9 };

        var solutions = new List<LambertSolution>();
        foreach (double dt in dts)
        {
            solutions.Add(LambertSolver.Solve(r1, r2, dt, mu, false));
        }

        bool vary = true;
        for (int i = 0; i < solutions.Count; i++)
        {
            for (int j = i + 1; j < solutions.Count; j++)
            {
                if ((solutions[i].DepartureVelocity - solutions[j].DepartureVelocity).Magnitude < 1d)
                {
                    vary = false;
                }
            }
        }

        // Round-trip: Advance(r1, v1, μ, dt) обязан попасть в r2.
        // Возле точного 180° коника вырождена по плоскости — допуски щедрые,
        // но многократно жёстче «любой dt — один ответ».
        bool roundTrip = true;
        double worst = 0d;
        for (int i = 0; i < dts.Length; i++)
        {
            KeplerPredictor.Advance(r1, solutions[i].DepartureVelocity, mu, dts[i], out Vector3d pred, out _);
            double err = (pred - r2).Magnitude / r2.Magnitude;
            if (err > worst) worst = err;
            if (err > 1e-6d) roundTrip = false;
        }

        // longWay — та же коника в обратную сторону: v_long = −v_short точно.
        bool mirror = true;
        foreach (double dt in dts)
        {
            LambertSolution lw = LambertSolver.Solve(r1, r2, dt, mu, true);
            if ((lw.DepartureVelocity + LambertSolver.Solve(r1, r2, dt, mu, false).DepartureVelocity).Magnitude > 1e-6d)
            {
                mirror = false;
            }
        }

        // dt меньше полупериода min-energy эллипса — решений нет: исключение.
        double tofMin = Math.PI * Math.Sqrt(Math.Pow(1.5e8, 3) / mu);
        bool throwLow = ThrowsArgumentOutOfRange(() => LambertSolver.Solve(r1, r2, tofMin * 0.5d, mu, false));

        Check(vary && roundTrip && mirror && throwLow, "T53 lambert-halfplane",
            string.Format("dt-вариативность: {0}; round-trip худший отн. err={1:E2} (<1e-6); v_long=−v_short: {2}; dt<min бросает: {3}",
                vary, worst, mirror, throwLow));
        return 0;
    }

    private static int Test54_LambertOvershoot()
    {
        // Кап z=39 — граница single-rev семейства: dt за ней обязан громко
        // бросать, а не молча возвращать скорость для чужого tof.
        Vector3d q1 = new Vector3d(1e8, 0, 0);
        Vector3d q2 = new Vector3d(0, 1.5e8, 0);
        double mu = 3.986e14;
        bool throwHuge = ThrowsArgumentOutOfRange(() => LambertSolver.Solve(q1, q2, 1e13, mu, false));
        bool throwTiny = ThrowsArgumentOutOfRange(() => LambertSolver.Solve(q1, q2, 1d, mu, false));
        bool normalOk = false;
        try
        {
            LambertSolver.Solve(q1, q2, 1e5, mu, false);
            normalOk = true;
        }
        catch (ArgumentOutOfRangeException)
        {
            normalOk = false;
        }

        Check(throwHuge && throwTiny && normalOk, "T54 lambert-overshoot",
            string.Format("dt=1e13 бросает: {0}; dt=1с бросает: {1}; dt=1e5 решается: {2}", throwHuge, throwTiny, normalOk));
        return 0;
    }

    private static bool ThrowsArgumentOutOfRange(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static int Test55_ForceAcceptSync()
    {
        // Зажатые допуски → постоянные rejects → кламп к MinStep → force-accept.
        // Инвариант после фикса: сумма длительностей dense-сегментов = целевой dt
        // (±MinStep), конец трека — принятое состояние, стыки непрерывны.
        double mu = 3.986e14;
        Vector3d pos = new Vector3d(7e6, 0, 0);
        Vector3d vel = new Vector3d(0, Math.Sqrt(mu / 7e6), 0);
        double targetDt = 1e-3;

        SpacecraftOrbitIntegrator.DerivativeFunction deriv = (s, t) => new SpacecraftIntegrationState
        {
            Position = s.Velocity,
            Velocity = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(s.Position, Vector3d.Zero, mu),
            Mass = 0d
        };

        var track = new List<DenseSegment>();
        SpacecraftIntegrationResult res = SpacecraftOrbitIntegrator.StepForward(
            new SpacecraftIntegrationState { Position = pos, Velocity = vel, Mass = 1000d },
            0d, targetDt, deriv,
            1e-12d, 1e-12d, 1e-12d, 1e-12d, track);

        double span = track.Count > 0 ? track[track.Count - 1].T1 - track[0].T0 : -1d;
        bool spanOk = track.Count > 0 && Math.Abs(span - targetDt) <= 1e-6d * 1.5;
        bool continuity = true;
        for (int i = 1; i < track.Count; i++)
        {
            if ((track[i].Y0.Position - track[i - 1].Y1.Position).Magnitude > 1e-9d)
            {
                continuity = false;
            }
        }

        bool endsMatch = track.Count > 0
            && (track[track.Count - 1].Y1.Position - res.State.Position).Magnitude == 0d;

        // Legacy-степпер (без dense) обязан дать тот же результат в допуске
        // форс-акцепта: та же математика, другой порядок суммирования стадий.
#pragma warning disable 0618 // намеренно: кросс-чек с legacy-степпером
        IntegrationResult legacy = OrbitIntegrator.StepForward(
            pos, vel, 0d, targetDt,
            (p, t) => PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, Vector3d.Zero, mu),
            1e-12d, 1e-12d);
#pragma warning restore 0618
        double legacyAgree = (legacy.Position - res.State.Position).Magnitude;

        Check(spanOk && continuity && endsMatch && legacyAgree < 1e-6d, "T55 force-accept-sync",
            string.Format("сегментов {0}, span={1:E9}с (target 1e-3, ±1.5·MinStep): {2}; стыки: {3}; конец трека=итог: {4}; legacy Δpos={5:E2}м",
                track.Count, span, spanOk, continuity, endsMatch, legacyAgree));
        return 0;
    }

    private static int Test56_ApsisZero()
    {
        // В периапсисе timeToPeriapsis == 0 (раньше возвращался полный период),
        // в апоапсисе timeToApoapsis == 0.
        double mu = 3.986e14;
        double rp = 7e6;
        double ecc = 0.3d;
        double ra = rp * (1d + ecc) / (1d - ecc);
        double vPeri = Math.Sqrt(mu * (1d + ecc) / rp);
        double vApo = Math.Sqrt(mu * (1d - ecc) / ra);

        OrbitalElements atPeri = OrbitalElements.FromState(new Vector3d(rp, 0, 0), new Vector3d(0, vPeri, 0), mu);
        OrbitalElements atApo = OrbitalElements.FromState(new Vector3d(-ra, 0, 0), new Vector3d(0, -vApo, 0), mu);
        bool ok = atPeri.IsValid && atApo.IsValid;
        atPeri.TryGetTimeToApsides(mu, out double tPeri, out double tApoAtPeri);
        atApo.TryGetTimeToApsides(mu, out double tPeriAtApo, out double tApo);
        ok = ok && Math.Abs(tPeri) < 1e-6d && tApoAtPeri > 0d;
        ok = ok && tPeriAtApo > 0d && Math.Abs(tApo) < 1e-6d;

        Check(ok, "T56 apsis-zero",
            string.Format("в периапсисе: tPe={0:E6}с (0), tAp={1:F0}с; в апоапсисе: tPe={2:F0}с, tAp={3:E6}с (0)",
                tPeri, tApoAtPeri, tPeriAtApo, tApo));
        return 0;
    }

    private static int Test57_RailValidation()
    {
        // Молчаливые клампы рельсы (a≥1м, e≤0.999) заменены громкой валидацией:
        // гиперболическая «рельса» — баг данных, а не валидный случай.
        var star = new OrbitingBody { Name = "S", StandardGravitationalParameter = 1e15 };
        var hyper = new OrbitingBody { Name = "Hyper", SemiMajorAxis = 1e8, Eccentricity = 1.2d };
        hyper.Parent = star;
        star.Children.Add(hyper);
        var degenerate = new OrbitingBody { Name = "Degenerate", SemiMajorAxis = 0d, Eccentricity = 0d };
        degenerate.Parent = star;
        star.Children.Add(degenerate);
        var valid = new OrbitingBody { Name = "Valid", SemiMajorAxis = 1e8, Eccentricity = 0.01d };
        valid.Parent = star;
        star.Children.Add(valid);

        bool hyperThrows = false;
        try { hyper.EvaluateWorldState(0d, out _, out _); }
        catch (ArgumentOutOfRangeException) { hyperThrows = true; }

        bool degenerateThrows = false;
        try { degenerate.EvaluateWorldState(0d, out _, out _); }
        catch (ArgumentOutOfRangeException) { degenerateThrows = true; }

        bool validOk = false;
        try
        {
            valid.EvaluateWorldState(0d, out Vector3d p, out Vector3d v);
            validOk = p.IsFinite && v.IsFinite;
        }
        catch
        {
            validOk = false;
        }

        Check(hyperThrows && degenerateThrows && validOk, "T57 rail-validation",
            string.Format("e=1.2 бросает: {0}; a=0 бросает: {1}; e=0.01 решается: {2}", hyperThrows, degenerateThrows, validOk));
        return 0;
    }

    private static int Test58_GrainProbe()
    {        // A0-зонд: что реально происходит с зерном детекта в чистом межпланетном
        // сценарии. Гипотеза ревью: если ×20 вдали от тел, то либо FindDominantBody
        // не переключается на звезду, либо есть скрытый кламп. Ожидание по коду:
        // доминанта — звезда, τ ~ годы, желаемое зерно ~месяцы > базы 30с —
        // возврат baseDivisions=20 из документированного ПОЛА («только мельче»).
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d starRef, out Vector3d starRefV);
        double muStar = sys.Root.ResolveStandardGravitationalParameter();

        // Межпланетный круиз: 1.9e11 от звезды (вне обеих SOI), круговая ~26.4 км/с.
        sys.Root.EvaluateWorldState(0d, out Vector3d rootP, out Vector3d rootV);
        Vector3d cruisePos = rootP + new Vector3d(1.9e11, 0, 0);
        Vector3d cruiseVel = rootV + new Vector3d(0, Math.Sqrt(muStar / 1.9e11), 0);
        var cruiseState = new SpacecraftIntegrationState { Position = cruisePos, Velocity = cruiseVel, Mass = 1000d };

        OrbitingBody dominant = sys.FindDominantBody(cruisePos, 0d);
        double tau = DetectionGrain.CharacteristicTimeSeconds(sys, cruiseState, 0d);
        int subdiv = DetectionGrain.Subdivisions(600d, 20, tau);
        double desiredGrain = tau / DetectionGrain.GrainDivisor;
        double baseGrain = 600d / 20;

        // Возле планеты: атмосфера достижима внутри чанка — зерно обязано мельчать.
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d paP, out Vector3d paV);
        var nearState = new SpacecraftIntegrationState
        {
            Position = paP + new Vector3d(sys.AllBodies[1].Radius + 50000d, 0, 0),
            Velocity = paV + new Vector3d(0, 2000d, 0),
            Mass = 1000d
        };
        double tauNear = DetectionGrain.CharacteristicTimeSeconds(sys, nearState, 0d);
        int subdivNear = DetectionGrain.Subdivisions(600d, 20, tauNear);

        Check(dominant == sys.Root && tau > 1e7d && subdiv == 20 && desiredGrain > baseGrain && subdivNear == 20,
            "T58 grain-probe",
            string.Format("межпланета: доминанта={0}, τ={1:E3}с, желаемое зерно={2:E0}с vs база {3:F0}с → subdiv={4} (из пола); у планеты: τ={5:E1}с → subdiv={6}",
                dominant.Name, tau, desiredGrain, baseGrain, subdiv, tauNear, subdivNear));

        // A0-фикс: условный пол. Вдали от тел ни одна пороговая сфера
        // (атмосферы/поверхности/SOI) недостижима за чанк → подчанкование
        // не нужно; возле планеты — нужно. Гейт: T11/T18/T19/T44.
        var detList = new System.Collections.Generic.List<ICrossingDetector>();
        bool deepSpaceFine = DetectionGrain.NeedsFineGrain(sys, detList, cruiseState, 0d, 600d);
        bool deepSpaceFineWithDets = DetectionGrain.NeedsFineGrain(sys,
            new System.Collections.Generic.List<ICrossingDetector>
            {
                AltitudeCrossingDetector.ForTouchdown(sys.AllBodies[1]),
                AltitudeCrossingDetector.ForTouchdown(sys.AllBodies[2])
            },
            cruiseState, 0d, 600d);
        bool nearPlanetFine = DetectionGrain.NeedsFineGrain(sys,
            new System.Collections.Generic.List<ICrossingDetector>
            {
                AltitudeCrossingDetector.ForTouchdown(sys.AllBodies[1])
            },
            nearState, 0d, 600d);

        Check(!deepSpaceFine && !deepSpaceFineWithDets && nearPlanetFine, "T58 grain-needs",
            string.Format("NeedsFineGrain: круиз без детекторов={0} (ожидался false), круиз с детекторами={1} (false — сферы недостижимы), у планеты={2} (true)",
                deepSpaceFine, deepSpaceFineWithDets, nearPlanetFine));
        return 0;
    }

    private static int Test69_WarpLadder()
    {
        // P4: лестница [1,5,10,100,500,1000,3000] с капом атмосферы ×3 и
        // запретом тяги выше ×3. Проверки:
        // 1) снап к ступеням (запрос 7 → ×5, 2999 → ×1000, 99999 → ×3000, 0.5 → ×1);
        // 2) EffectiveWarpFactor: в атмосфере любая ступень ≥5 → ×3, в космосе — ступень;
        // 3) планировщик кадра: effective × realDt, useLongWarp при >×3, кэп горизонтом;
        // 4) EffectiveThrottle: >×3 → 0; PrepareForLongWarp обнуляет снимок газа →
        //    CanEnterLongWarp пропускает баллистику с живым ThrustSource;
        // 5) дальний варп ×3000 покадрово == одним куском (бит-в-бит);
        // 6) физический варп ×1 == ×3 c атмосферой и тягой (класс T1 с drag).
        var warp = new WarpController();

        // 1) Снап.
        bool snapping = true;
        warp.SetWarpFactor(7d);
        snapping &= warp.WarpFactor == 5d;
        warp.SetWarpFactor(2999d);
        snapping &= warp.WarpFactor == 1000d;
        warp.SetWarpFactor(99999d);
        snapping &= warp.WarpFactor == 3000d;
        warp.SetWarpFactor(0.5d);
        snapping &= warp.WarpFactor == 1d;
        warp.SetWarpFactor(3000d);
        snapping &= warp.WarpFactor == 3000d;

        // 2) Капы.
        bool capsOk = warp.EffectiveWarpFactor(true) == 3d && warp.EffectiveWarpFactor(false) == 3000d;
        warp.SetWarpFactor(1d);
        capsOk &= warp.EffectiveWarpFactor(true) == 1d && warp.EffectiveWarpFactor(false) == 1d;
        warp.SetWarpFactor(5d);
        capsOk &= warp.EffectiveWarpFactor(true) == 3d && warp.EffectiveWarpFactor(false) == 5d;

        // 3) Планировщик кадра: 60 fps при ×3000 → 50 с/кадр, long-warp.
        warp.SetWarpFactor(3000d);
        WarpFrameDecision d = WarpPlanner.ComputeFrame(warp, false, 1d / 60d, double.PositiveInfinity);
        bool plannerOk = d.EffectiveFactor == 3000d && d.UseLongWarp
            && Math.Abs(d.SimSeconds - 50d) < 1e-9d;
        d = WarpPlanner.ComputeFrame(warp, true, 1d / 60d, double.PositiveInfinity);
        plannerOk &= d.EffectiveFactor == 3d && !d.UseLongWarp && Math.Abs(d.SimSeconds - (3d / 60d)) < 1e-12d;
        // Кэп горизонтом: осталось 20 с — кадр не летит за конец мира.
        d = WarpPlanner.ComputeFrame(warp, false, 1d / 60d, 20d);
        plannerOk &= d.SimSeconds == 20d;

        // 4) Тяга: >×3 → 0; PrepareForLongWarp делает CanEnterLongWarp=true
        // с живым ThrustSource (снимок газа обнулён).
        bool thrustCut = warp.EffectiveThrottle(0.8d, 5d) == 0d && warp.EffectiveThrottle(0.8d, 3d) == 0.8d;

        StarSystem sys = ManualStarPlanetMoon();
        var physics = new SpacecraftPhysics();
        physics.Sources.Add(new CachedGravitySource(sys));
        var warpCtl = new WarpController();
        var thrust = new ThrustSource(50000d, 100d, new ConstantIsp(320d));
        thrust.ThrottleAt = tt2 => warpCtl.CurrentControlSnapshot.Throttle;
        physics.Sources.Add(thrust);
        var propCtl = new EventDrivenPropagator(physics);

        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d p0, out Vector3d v0);
        var ship = new SpacecraftIntegrationState
        {
            Position = p0 + new Vector3d(0d, planet.Radius + 200000d, 0d),
            Velocity = v0 + new Vector3d(7600d, 0d, 0d),
            Mass = 1000d
        };
        warpCtl.SampleControl(0d, 0.8d);
        var driver = new LongWarpDriver(physics, propCtl);
        bool blockedWithThrottle = !driver.CanEnterLongWarp(ship, 0d);
        warpCtl.PrepareForLongWarp(0d);
        bool passAfterPrepare = driver.CanEnterLongWarp(ship, 0d);
        bool thrustMechanicsOk = thrustCut && blockedWithThrottle && passAfterPrepare;

        // 5) Дальний варп ×3000: покадрово (50 с/кадр) против одного куска.
        // Контракт T1: одна и та же нарезка — бит-в-бит, разная нарезка — в
        // допуске интегратора (для суток разгона — 2 м по позиции).
        StarSystem sysA = ManualStarPlanetMoon();
        var physA = new SpacecraftPhysics();
        physA.Sources.Add(new CachedGravitySource(sysA));
        var propA = new EventDrivenPropagator(physA);
        var shipFrame = new Spacecraft(new Vector3d(2e11, 0d, 0d), new Vector3d(0d, 21000d, 0d), 1000d);
        var shipOnce = new Spacecraft(shipFrame.Position, shipFrame.Velocity, 1000d);
        var shipRepeat = new Spacecraft(shipFrame.Position, shipFrame.Velocity, 1000d);
        double spanDay = 86400d;
        double t = 0d;
        while (t < spanDay - 1e-9d)
        {
            double sim = Math.Min(50d, spanDay - t);
            propA.Propagate(shipFrame, t, sim);
            t += sim;
        }

        var propB = new EventDrivenPropagator(physA);
        propB.Propagate(shipOnce, 0d, spanDay);
        var propC = new EventDrivenPropagator(physA);
        double tC = 0d;
        while (tC < spanDay - 1e-9d)
        {
            double sim = Math.Min(50d, spanDay - tC);
            propC.Propagate(shipRepeat, tC, sim);
            tC += sim;
        }

        Vector3d dSlice = shipFrame.Position - shipOnce.Position;
        Vector3d dRepeat = shipFrame.Position - shipRepeat.Position;
        bool frameDeterminism = dRepeat.Magnitude == 0d
            && dSlice.Magnitude < 2d
            && (shipFrame.Velocity - shipOnce.Velocity).Magnitude < 1e-3d;

        // 6) Физический варп ×1 == ×3 с атмосферой и тягой: покрыто T1 (границы
        // допусков), здесь — дымовой контроль управляющего зерна: чанки ≤0.5с.
        warpCtl.SetWarpFactor(3000d);
        warpCtl.ResetTicks(0d);
        double maxChunkSeen = 0d;
        double tt = 0d;
        while (tt < 5d)
        {
            double chunk = warpCtl.GetMaxChunkSeconds(tt, 600d, true);
            if (chunk > maxChunkSeen)
            {
                maxChunkSeen = chunk;
            }

            tt += chunk;
        }

        bool grainOk = maxChunkSeen <= WarpController.ControlTickSeconds + 1e-12d;

        // 7) Регресс A1 (аудит pre-Unity/9.1): сэмпл ПОСЛЕ ресета на ненулевом
        // t не теряется (старый sentinel «TickTime == 0» был проверкой эпохи),
        // а PrepareForLongWarp обнуляет газ безусловно.
        var warpCtlA1 = new WarpController();
        warpCtlA1.ResetTicks(5d);
        warpCtlA1.SampleControl(5d, 0.8d);
        bool sampleAfterReset = warpCtlA1.CurrentControlSnapshot.Throttle == 0.8d
            && warpCtlA1.CurrentControlSnapshot.TickTimeSeconds == 5d;
        warpCtlA1.SampleControl(5.2d, 0.9d);
        bool inTickKept = warpCtlA1.CurrentControlSnapshot.Throttle == 0.8d; // до границы тика сэмпл не проходит
        warpCtlA1.SampleControl(5.5d, 0.9d);
        bool tickGridOk = warpCtlA1.CurrentControlSnapshot.Throttle == 0.9d;
        warpCtlA1.PrepareForLongWarp(5d);
        bool prepareUnconditional = warpCtlA1.CurrentControlSnapshot.Throttle == 0d;

        Check(snapping && capsOk && plannerOk && thrustMechanicsOk && frameDeterminism && grainOk
            && sampleAfterReset && inTickKept && tickGridOk && prepareUnconditional, "T69 warp-ladder",
            string.Format("снап={0}; капы атмосферы={1}; планировщик={2}; тяга>×3 запрещена+Prepare={3}; сутки покадрово vs кусок dPos={4:E3}м (повтор бит-в-бит={5}); зерно ≤0.5с={6}; A1-сэмплы после ресета/Prepare: {7}",
                snapping, capsOk, plannerOk, thrustMechanicsOk, dSlice.Magnitude, dRepeat.Magnitude == 0d, grainOk,
                sampleAfterReset && inTickKept && tickGridOk && prepareUnconditional));
        return 0;
    }

    private static OrbitingBody SpinPlanet(double omega2R, double g0)
    {
        double radius = 1e6d;
        double mu = g0 * radius * radius;
        double omega = Math.Sqrt(omega2R / radius);
        var body = new OrbitingBody
        {
            Name = "SpinBody" + omega2R.ToString("R"),
            Radius = radius,
            StandardGravitationalParameter = mu,
            RotationPeriodSeconds = (2d * Math.PI) / omega,
            NorthPoleDirection = new Vector3d(0d, 0d, 1d)
        };
        body.SyncMassFromGravitationalParameter();
        return body;
    }

    private static int Test73_EphemerisThreadGate()
    {
        // Фаза 4: thread-контракт BakedEphemeris. Оконный курсор (gzip forward-
        // only + переиспользуемое окно) под гейтом: конкурентные TryEvaluate с
        // нескольких потоков обязаны давать результаты, БИТ-В-БИТ равные
        // однопоточному прогону, с консистентным счётчиком WindowLoads.
        StarSystem sys = ManualStarPlanetMoon();
        var cfg = new BakeConfig { Degree = 11, MaxStepSeconds = 600d };
        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "t73-bake");
        EphemerisBaker.BakeToFiles(sys, outDir, 0.02d, cfg, compress: true);
        EphemerisBaker.AttachFiles(sys, outDir);
        BakedEphemeris bakedRef = sys.AllBodies[1].Baked;

        // Однопоточный эталон: 2000 запросов по спану.
        double span = bakedRef.EndSeconds;
        var reference = new List<Vector3d>(2000);
        for (int i = 0; i < 2000; i++)
        {
            bakedRef.TryEvaluate(span * i / 2000d, out Vector3d p, out _);
            reference.Add(p);
        }

        // Конкурентный прогон: 4 потока по ПЕРЕМЕШАННОЙ сетке (j = 7919·i mod 2000 —
        // перестановка тех же времён): гарантированные промахи окна, перескоки
        // курсора и contention, но бит-в-бит сравнимо с эталоном.
        var concurrent = new Vector3d[2000];
        long windowLoadsBefore = bakedRef.WindowLoads;
        System.Threading.Tasks.Parallel.For(0, 2000, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            int j = (i * 7919) % 2000;
            bakedRef.TryEvaluate(span * j / 2000d, out Vector3d p, out _);
            concurrent[j] = p;
        });

        // Сверка бит-в-бит с эталоном (обратный порядок запросов → та же сетка времён).
        bool bitExact = true;
        for (int i = 0; i < 2000; i++)
        {
            if (concurrent[i].X != reference[i].X || concurrent[i].Y != reference[i].Y || concurrent[i].Z != reference[i].Z)
            {
                bitExact = false;
                break;
            }
        }

        bool loadsCounted = bakedRef.WindowLoads >= windowLoadsBefore;

        // Snapshot-контракт: LoadRangeToMemory из другого потока параллельно с
        // оконным чтением — без гейта не пересекается (свои стримы), результат
        // валиден.
        bool snapshotOk = false;
        object snapshotLock = new object();
        System.Threading.Tasks.Parallel.Invoke(
            () => bakedRef.TryEvaluate(span * 0.5d, out _, out _),
            () =>
            {
                BakedEphemeris snap = bakedRef.LoadRangeToMemory(span * 0.25d, span * 0.75d, 1000000);
                lock (snapshotLock)
                {
                    snapshotOk = snap.TryEvaluate(span * 0.5d, out Vector3d p, out Vector3d v) && p.IsFinite && v.IsFinite;
                }
            });

        // Регресс A3: переттач того же набора — старые файловые инстансы
        // Dispose'ятся, файл сразу доступен для перезаписи (раньше FileStream
        // жил до GC-финализатора и блокировал файл).
        EphemerisBaker.AttachFiles(sys, outDir);
        string overwriteTarget = System.IO.Path.Combine(outDir, System.IO.Directory.GetFiles(outDir)[0]);
        byte[] keep = System.IO.File.ReadAllBytes(overwriteTarget);
        bool fileFree = false;
        try
        {
            System.IO.File.WriteAllBytes(overwriteTarget, keep);
            fileFree = true;
        }
        catch (IOException)
        {
            fileFree = false;
        }

        bool reattachWorks = sys.AllBodies[1].Baked.TryEvaluate(span * 0.5d, out Vector3d pRe, out _) && pRe.IsFinite;

        Check(bitExact && loadsCounted && snapshotOk && fileFree && reattachWorks, "T73 ephemeris-thread-gate",
            string.Format("4 потока, перемешанная сетка (промахи окон) == эталон бит-в-бит: {0}; WindowLoads консистентен: {1}; LoadRangeToMemory параллельно с окном: {2}; переттач освобождает файл (перезапись сразу): {3}; новый инстанс жив: {4}",
                bitExact, loadsCounted, snapshotOk, fileFree, reattachWorks));
        return 0;
    }

    private static int Test75_TorqueProbeGuard()
    {
        // A4 (аудит pre-Unity/3.1): probe-шаги драйвера (бисекция события) идут
        // через тот же physics-экземпляр и без save/restore оставляли бы в
        // TotalTorqueBody след probe-состояния. Контракт: снимок после Propagate
        // с детектором == снимок того же интегрирования без драйвера (шаг 0→600).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
        };
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        // Старт ВЫШЕ атмосферы со сходящей траекторией — вход (~25 с) внутри
        // первого чанка 600 с: бисекция гарантированно отрабатывает.
        Vector3d p0 = bp0 + new Vector3d(0d, planet.Radius + 150000d, 0d);
        Vector3d v0 = bv0 + new Vector3d(500d, -2000d, 0d);

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(new StateTorqueSource());
        var prop = new EventDrivenPropagator(phys);
        prop.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(planet));
        var ship = new Spacecraft(p0, v0, 1000d);
        EventOccurrence? ev = prop.Propagate(ship, 0d, 600d);
        bool eventFired = ev.HasValue && ev.Value.Kind == EventKind.AtmosphereEntry;

        var physManual = new SpacecraftPhysics();
        physManual.Sources.Add(new CachedGravitySource(sys));
        physManual.Sources.Add(new StateTorqueSource());
        var clone = new Spacecraft(p0, v0, 1000d);
        // Эталон — то же ЕДИНСТВЕННОЕ интегрирование 0→30с (драйвер режет
        // чанки по MaxChunk/DetectionSubdivisions = 30с; событие внутри первого
        // чанка, поэтому ship-bound путь — ровно один шаг 0→30). Dense-сборка
        // сегментов бит-в-бит не меняет интегрирование (контракт StepWithSegments).
        physManual.Step(clone, 0d, 30d);
        Vector3d expected = physManual.TotalTorqueBody;
        bool singleChunk = ev.HasValue && ev.Value.TimeSeconds < 30d;

        bool snapshotClean = phys.TotalTorqueBody.X == expected.X
            && phys.TotalTorqueBody.Y == expected.Y
            && phys.TotalTorqueBody.Z == expected.Z;

        Check(eventFired && singleChunk && snapshotClean, "T75 torque-probe-guard",
            string.Format("событие вошло в атмосферу в первом чанке (бисекция отработала, t*={0:F2}с<30с): {1}; снимок == шагу 0→30 без драйвера (бит-в-бит): {2}",
                ev.HasValue ? ev.Value.TimeSeconds : -1d, eventFired && singleChunk, snapshotClean));
        return 0;
    }

    private static SystemBlueprint MakeTidalSystem(double moonEccentricity, bool lockStar)
    {
        var bp = new SystemBlueprint();
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Star",
            StandardGravitationalParameter = 1e16d,
            Radius = 1e8d,
            NorthPoleY = 0d,
            NorthPoleZ = 1d,
            TidallyLocked = lockStar
        });
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Planet",
            StandardGravitationalParameter = 4e14d,
            Radius = 6e6d,
            SemiMajorAxis = 5e9d,
            NorthPoleY = 0d,
            NorthPoleZ = 1d,
            ParentIndex = 0
        });
        bp.Bodies.Add(new BodyBlueprint
        {
            Name = "Moon",
            StandardGravitationalParameter = 5e12d,
            Radius = 1.7e6d,
            SemiMajorAxis = 2e7d,
            Eccentricity = moonEccentricity,
            RotationPeriodSeconds = 999d,
            NorthPoleY = 0d,
            NorthPoleZ = 1d,
            ParentIndex = 1,
            TidallyLocked = true
        });
        return bp;
    }

    /// <summary>Угол между радиусом к точке lon=0 и направлением на родителя (рад).</summary>
    private static double TidalDeviation(OrbitingBody moon, OrbitingBody parent, double t)
    {
        moon.GetSurfaceState(0d, 0d, 0d, t, out Vector3d meridian, out _);
        moon.EvaluateWorldState(t, out Vector3d moonPos, out _);
        parent.EvaluateWorldState(t, out Vector3d parentPos, out _);
        Vector3d a = (meridian - moonPos).Normalized;
        Vector3d b = (moonPos - parentPos).Normalized;
        return Math.Acos(Math.Min(1d, Math.Max(-1d, Vector3d.Dot(a, b))));
    }
}
