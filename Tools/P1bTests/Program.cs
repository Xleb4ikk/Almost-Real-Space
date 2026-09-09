using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal sealed class TestThrustSource : IDynamicsSource
{
    public double ThrustNewtons;
    public double MassFlowKgS;
    public Vector3d Direction = new Vector3d(1d, 0d, 0d);
    public Func<double, bool> ActiveAt;

    public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
    {
        bool active = ActiveAt != null ? ActiveAt(timeSeconds) : ThrustNewtons > 0d;
        if (!active)
        {
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
        }

        return new DynamicsContribution(Vector3d.Zero, Direction.Normalized * ThrustNewtons, MassFlowKgS, Vector3d.Zero);
    }
}

internal static class P1bTests
{
    private static int failures;

    private static void Check(bool cond, string name, string detail)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name + " | " + detail);
        if (!cond)
        {
            failures++;
        }
    }

    private static StarSystem TestSystem()
    {
        return ExampleSystemPresets.CreateExampleSystem();
    }

    private static void BuildShipPhysics(StarSystem sys, out SpacecraftPhysics phys, out EventDrivenPropagator prop, TestThrustSource thrust)
    {
        phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        if (thrust != null)
        {
            phys.Sources.Add(thrust);
        }

        prop = new EventDrivenPropagator(phys);
    }

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

    private static void EvaluatorPair(BurnTolerancePreset preset, out double dPos, out double dVel, out double dMass)
    {
        EvaluatorRun(preset, 1d / 60d, out Vector3d pA, out Vector3d vA, out double mA);
        EvaluatorRun(preset, 3d / 60d, out Vector3d pB, out Vector3d vB, out double mB);
        dPos = (pA - pB).Magnitude;
        dVel = (vA - vB).Magnitude;
        dMass = Math.Abs(mA - mB);
    }

    private static void EvaluatorRun(BurnTolerancePreset preset, double frameSim, out Vector3d pos, out Vector3d vel, out double mass)
    {
        StarSystem sys = TestSystem();
        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        var thrust = new ThrustSource(1000d, 500d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => time >= 100d && time < 110d ? 1d : 0d;
        thrust.ThrustDirection = new Vector3d(0d, 1d, 0d);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        phys.Sources.Add(thrust);
        var coastProp = new EventDrivenPropagator(phys);
        var burnProp = new EventDrivenPropagator(phys);
        burnProp.CrossingDetectors.Add(new PropellantDepletionDetector(500d, "main"));
        var driver = new LongWarpDriver(phys, coastProp);
        var warp = new WarpController();
        var evaluator = new ManeuverEvaluator(phys, coastProp, driver, burnProp, warp);
        var ship = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(5300d, 0d, 0d), 1000d);
        ManeuverResult r = evaluator.CoastThenBurn(ship, 0d, 100d, 110d, preset, frameSim);
        pos = r.EndState.Position;
        vel = r.EndState.Velocity;
        mass = r.EndState.Mass;
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

    private static int Test17_FrameBridge()
    {
        // T17a: roundtrip astro→sim→astro бит-в-бит (боевая перестановка осей).
        // Через double-двойник моста: под UNITY_5_3_OR_NEWER боевой мост пакует
        // в float (это его работа на границе рендера), побитовая проверка — на
        // типизированном sim-типе; формулы маппинга общие (см. SimOf/AstroOf).
        var rng = new Random(1234);
        bool roundtrip = true;
        for (int i = 0; i < 1000; i++)
        {
            double s = 1e11 * (rng.NextDouble() * 2d - 1d);
            var v = new Vector3d(s * rng.NextDouble(), s * rng.NextDouble(), s * rng.NextDouble());
            Vector3d back = AstroOf(SimOf(v));
            if (back.X != v.X || back.Y != v.Y || back.Z != v.Z)
            {
                roundtrip = false;
            }
        }

        var zeroBack = AstroOf(SimOf(Vector3d.Zero));
        roundtrip = roundtrip && zeroBack.X == 0d && zeroBack.Y == 0d && zeroBack.Z == 0d;

        // T17b: жёсткое вращение — проекция оффсета на ось и его длина обязаны
        // быть константами времени (старый код без поворота к оси давал
        // синусоиду амплитудой ~r·sin(tilt) ≈ 1e6м — этот тест на нём падает).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 86400d;
        planet.NorthPoleDirection = new Vector3d(0.3d, 0d, 0.95d);
        Vector3d axis = planet.NorthPoleDirection.Normalized;
        double minDot = double.MaxValue;
        double maxDot = double.MinValue;
        double minLen = double.MaxValue;
        double maxLen = double.MinValue;
        for (int i = 0; i <= 24; i++)
        {
            double t = 86400d * i / 24d;
            planet.GetSurfaceState(10d, 20d, 0d, t, out Vector3d wp, out _);
            planet.EvaluateWorldState(t, out Vector3d bp, out _);
            Vector3d off = wp - bp;
            double dot = Vector3d.Dot(off, axis);
            double len = off.Magnitude;
            if (dot < minDot) minDot = dot;
            if (dot > maxDot) maxDot = dot;
            if (len < minLen) minLen = len;
            if (len > maxLen) maxLen = len;
        }

        // Порог 1e-4м: поворот Родригеса сохраняет инварианты с точностью ~20 ulp
        // (≈2e-5м при r~6e6м) — чистый fp-шум chained-операций; старый код без
        // поворота давал бы синусоиду амплитудой ~r·sin(tilt) ≈ 1e6м.
        bool rigid = (maxDot - minDot) < 1e-4d && (maxLen - minLen) < 1e-4d;

        // T17c: ось (0,0,1) — поворот Родригеса обязан быть тождеством бит-в-бит
        // (нулевой вектор k, вся поправка — точные нули), а сама экваториальная
        // формула — совпасть с независимым референсом до 1e-12 (референс снят
        // PowerShell: триг .NET Framework vs .NET 8 различается в последнем ulp,
        // поэтому побайтово сверяется только поворот, не косинусы).
        // Оффсет сверяется на звезде в начале координат: вычитание позиции тела
        // при |bp|~1e11 съедало бы младшие биты ((bp+off)−bp ≠ off), а корень
        // неподвижен в нуле — wpC и есть оффсет, бит-в-бит.
        OrbitingBody star = sys.Root;
        star.Radius = 6371000d;
        star.RotationPeriodSeconds = 0d;
        star.NorthPoleDirection = new Vector3d(0d, 0d, 1d);
        star.GetSurfaceState(10d, 20d, 1000d, 0d, out Vector3d wpC, out _);
        Vector3d offC = wpC;
        double latR = 10d * Math.PI / 180d;
        double lonR = 20d * Math.PI / 180d;
        var eqR = new Vector3d(
            6372000d * Math.Cos(latR) * Math.Cos(lonR),
            6372000d * Math.Cos(latR) * Math.Sin(lonR),
            6372000d * Math.Sin(latR));
        bool identityRotation = offC.X == eqR.X && offC.Y == eqR.Y && offC.Z == eqR.Z;
        double refErr = Math.Max(
            Math.Abs((eqR.X - 5896754.4375541173d) / 6372000d),
            Math.Max(
                Math.Abs((eqR.Y - 2146243.09404684d) / 6372000d),
                Math.Abs((eqR.Z - 1106486.18809368d) / 6372000d)));
        bool compat = identityRotation && refErr < 1e-12d;

        // T17d: смена фокуса не двигает физику — реконструкция мировой позиции
        // из фокус-относительных векторов точна для обоих фокусов.
        var physD = new SpacecraftPhysics();
        physD.Sources.Add(new CachedGravitySource(sys));
        var propD = new EventDrivenPropagator(physD);
        var driverD = new LongWarpDriver(physD, propD);
        planet.EvaluateWorldState(0d, out Vector3d bpD, out Vector3d bvD);
        var shipD = new Spacecraft(bpD + new Vector3d(0d, planet.Radius + 200000d, 0d), bvD + new Vector3d(7000d, 0d, 0d), 1000d);
        driverD.AdvanceToTarget(shipD, 0d, 3600d);
        planet.EvaluateWorldState(3600d, out Vector3d bpA, out _);
        OrbitingBody moon = sys.AllBodies[2];
        moon.EvaluateWorldState(3600d, out Vector3d bpM, out _);
        Vector3d relA = shipD.Position - bpA;
        Vector3d relM = shipD.Position - bpM;
        Vector3d recA = relA + bpA;
        Vector3d recM = relM + bpM;
        // (a−b)+b — не тождество в fp (пара ulp): допуск 1мм при |pos|~1e11.
        bool focus = (recA - shipD.Position).Magnitude < 1e-3d
            && (recM - shipD.Position).Magnitude < 1e-3d;
        var simA = SimOf(relA);
        var simM = SimOf(relM);
        bool bridge = simA.X == relA.X && simA.Y == relA.Z && simA.Z == -relA.Y
            && simM.X == relM.X && simM.Y == relM.Z && simM.Z == -relM.Y;

        Check(roundtrip && rigid && compat && focus && bridge, "T17 frame-bridge",
            string.Format("roundtrip 1000+ноль бит-в-бит: {0}; жёсткость: Δ(off·axis)={1:E2}м Δ|off|={2:E2}м; совм. (0,0,1) бит-в-бит: {3}; фокус-реконструкция: {4}; мост sim=(x,z,−y): {5}",
                roundtrip, maxDot - minDot, maxLen - minLen, compat, focus, bridge));
        return 0;
    }

    private static int Test20_Kind()
    {
        // A4.1: каждое событие несёт типизированный Kind + детектор-источник.
        // Ни одного парсинга DetectorName: kind идёт из детектора напрямую.
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

    private static double WrapLonError(double a, double b)
    {
        double d = Math.Abs(a - b);
        return Math.Min(d, 360d - d);
    }

    private static int Test20_SurfaceInverse()
    {
        // A4.2: инверс тем же Родригесом. 1000 случайных lat/lon×ось×время,
        // полюса, шов ±π в обе стороны, шов во времени (2π — представление).
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

        // Полюса: lon детерминированно 0.
        star.NorthPoleDirection = new Vector3d(0.3d, 0d, 0.95d);
        star.RotationPeriodSeconds = 86400d;
        star.GetSurfaceState(90d, 123d, 0d, 1000d, out Vector3d wpN, out _);
        star.SurfaceLatLonAt(wpN, 1000d, out double latN, out double lonN);
        star.GetSurfaceState(-90d, 45d, 0d, 2000d, out Vector3d wpS, out _);
        star.SurfaceLatLonAt(wpS, 2000d, out double latS, out double lonS);
        bool poles = Math.Abs(latN - 90d) < 1e-9d && lonN == 0d && Math.Abs(latS + 90d) < 1e-9d && lonS == 0d;

        // Шов в обе стороны: 179.9999° и 180.0001° рядом, не через 360°.
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

        // Шов во времени: фиксированная мировая точка, тело проворачивается под ней.
        // Сырая долгота пилит через 0/360, развёрнутая — прямая с наклоном −ω.
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

        // Ретроградный флип оси: roundtrip жив и там.
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
        // A4.3: touchdown по v_n, инварианты breakup, кламп == dry, фокус без пересчёта.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        var breakup = new SingleThresholdBreakup();

        // Мягкое: 1м над поверхностью, (5,-1,0) м/с — v_n≈4.5 < 5, v_t=5 живёт.
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

        // Жёсткое: 50км, -2000 м/с radial — разрушение, 10 спеков.
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

        // Кламп: масса == dry точно.
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

        // Фокус: тело из события, без пересчёта.
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

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return normal;
        }
    }

    private static int Test20_SurfaceMotion()
    {
        // A4.4: стоянка бит-в-бит, следование за вращением, стик 30° / слип 60°,
        // slip→stick за t_stop, отрыв при outward-g. Сфера: a_t≡0 → всегда стик.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        SurfaceMotion.GravityProvider realG = (pos, t) => sys.EvaluateShipAcceleration(pos, t);
        var sphere = new SphericalTerrain();

        // Стоянка: тело движется по орбите, поэтому абсолютного побитового покоя
        // быть не может — проверяется относительный оффсет (стоит на R ±мм).
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

        // Скорость обязана равняться скорости поверхности в конце (тело уехало
        // по орбите — абсолютного покоя нет, есть совместное движение).
        planet.EvaluateWorldState(5d, out _, out Vector3d bv5);
        bool standstill = standDrift < 1e-3d && standRegime && (v - bv5).SqrMagnitude == 0d;

        // Следование: период 86400 (центробежка 0.034 ≪ 9.8 — контакт держится;
        // период 3600 на земном радиусе физически срывает всё в Liftoff).
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

    /// <summary>
    /// Тестовый двойник позиционного моста (x, z, −y): независим от ветки
    /// UNITY_5_3_OR_NEWER — под ней AstroFrame.ToSimulation(Vector3d) отдаёт
    /// UnityEngine.Vector3, а тестам нужен типизированный sim-тип с double.
    /// Формула обязана совпадать с AstroFrame (проверяется T17_Handedness).
    /// </summary>
    private static SimVector3 SimOf(Vector3d astro)
    {
        return new SimVector3(astro.X, astro.Z, -astro.Y);
    }

    private static Vector3d AstroOf(SimVector3 sim)
    {
        return new Vector3d(sim.X, -sim.Z, sim.Y);
    }

    private static SimVector3 SimCross(SimVector3 left, SimVector3 right)
    {
        return new SimVector3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double SimMagnitude(SimVector3 v)
    {
        return Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
    }

    private static int Test17_Handedness()
    {
        // Мост обязан быть поворотом (det=+1), а не отражением: иначе все
        // величины через cross product (в первую очередь скорость вращения
        // поверхности) тихо меняют знак. Допуск 1e-12, НЕ побитово: прямой
        // и обратный порядок округлений при перемножении разный, и «улучшать»
        // этот тест до bit-exact значит сделать его flaky (см. комментарий).
        SimVector3 mx = SimOf(new Vector3d(1d, 0d, 0d));
        SimVector3 my = SimOf(new Vector3d(0d, 1d, 0d));
        SimVector3 mz = SimOf(new Vector3d(0d, 0d, 1d));
        SimVector3 crossMyMz = SimCross(my, mz);
        double det = (mx.X * crossMyMz.X) + (mx.Y * crossMyMz.Y) + (mx.Z * crossMyMz.Z);
        bool properRotation = det == 1d;

        var rng = new Random(20240);
        double worstCross = 0d;
        for (int i = 0; i < 1000; i++)
        {
            var a = new Vector3d(rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d);
            var b = new Vector3d(rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d, rng.NextDouble() * 2d - 1d);
            Vector3d astroCross = Vector3d.Cross(a, b);
            if (astroCross.SqrMagnitude < 1e-6d)
            {
                continue;
            }

            SimVector3 lhs = SimCross(SimOf(a), SimOf(b));
            SimVector3 rhs = SimOf(astroCross);
            SimVector3 diff = new SimVector3(lhs.X - rhs.X, lhs.Y - rhs.Y, lhs.Z - rhs.Z);
            double relErr = SimMagnitude(diff) / SimMagnitude(rhs);
            if (relErr > worstCross) worstCross = relErr;
        }

        bool crossCommutes = worstCross < 1e-12d;

        // Физический дискриминатор: скорость вращения поверхности.
        // На старом отражении (x,z,y) этот тест падает ЗНАКОМ (lhs ≈ −rhs),
        // а не шумом — проверено выводом формулы, не запуском.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 7200d;
        planet.NorthPoleDirection = new Vector3d(0.2d, 0.1d, 0.97d);
        planet.GetSurfaceState(15d, 40d, 100d, 1234d, out Vector3d wp, out Vector3d wv);
        planet.EvaluateWorldState(1234d, out Vector3d bp, out Vector3d bv);
        Vector3d rel = wp - bp;
        Vector3d axis = planet.SpinAxis;
        double omega = planet.SpinAngularSpeed;
        Vector3d omegaVec = axis * omega;
        Vector3d rotVelAstro = wv - bv;
        SimVector3 lhsRot = SimCross(SimOf(omegaVec), SimOf(rel));
        SimVector3 rhsRot = SimOf(rotVelAstro);
        SimVector3 diffRot = new SimVector3(lhsRot.X - rhsRot.X, lhsRot.Y - rhsRot.Y, lhsRot.Z - rhsRot.Z);
        double rotErr = SimMagnitude(diffRot) / SimMagnitude(rhsRot);
        bool rotationSign = rotErr < 1e-9d;

        Check(properRotation && crossCommutes && rotationSign, "T17 handedness",
            string.Format("det=+1 точно: {0}; cross коммутирует (худш. {1:E2} <1e-12): {2}; v_rot знак верный ({3:E2} <1e-9): {4}",
                properRotation, worstCross, crossCommutes, rotErr, rotationSign));
        return 0;
    }

    private static int Test26_CircularMatrix()
    {
        // Матрица e × i, три независимые проверки (не путать!):
        // (1) roundtrip dt=0 — конвенции углов (класс бага T25: O(1), не шум);
        // (2) дрейф энергии предиктора против себя — ТОЛЬКО сохранение формы,
        //     фазу M0 он не видит в принципе (E фазо-независима);
        // (3) T26b: четверть периода против ВНЕШНЕГО численного эталона
        //     (TwoBodySystem, луна исключена конструктивно) — единственное,
        //     что ловит ошибку начальной фазы M0. Граница 10м: ошибки конвенций
        //     дают километры, прилив звезды ~1м, шум интегратора ~см.
        // e=0 — другой вырожденный случай (перицентра нет): если красное —
        // фиксим долготой, а не вариацией ω. Двойное вырождение e≈1e-8 ×
        // i=90°/180° — главные кандидаты на сюрприз (порог узла + ветки h_z
        // чинились независимо); отчёт построчный, худший не маскирует впритык.
        double mu = 3.986e14d;
        double[] ecc = new double[] { 0d, 1e-8d, 1e-4d, 0.05d };
        double[] inc = new double[] { 0d, 5d, 90d, 180d };
        double nodeRad = 40d * Math.PI / 180d;
        double argpRad = 30d * Math.PI / 180d;
        double eccAnom0 = 1d;
        double worstRoundtrip = 0d;
        double worstEnergy = 0d;
        double worstNumeric = 0d;
        string worstCase = "";
        string worstNumericCase = "";
        var perCase = new List<string>();
        StarSystem sys2 = TwoBodySystem();
        OrbitingBody planet2 = sys2.AllBodies[1];
        for (int ei = 0; ei < ecc.Length; ei++)
        {
            for (int ii = 0; ii < inc.Length; ii++)
            {
                double e = ecc[ei];
                double incl = inc[ii] * Math.PI / 180d;
                double a = 8e6d;
                double yScale = Math.Sqrt(Math.Max(0d, 1d - (e * e)));
                var pfP = new Vector3d(a * (Math.Cos(eccAnom0) - e), a * yScale * Math.Sin(eccAnom0), 0d);
                double rPf = pfP.Magnitude;
                double vFac = Math.Sqrt(mu * a) / rPf;
                var pfV = new Vector3d(-vFac * Math.Sin(eccAnom0), vFac * yScale * Math.Cos(eccAnom0), 0d);
                Vector3d r0 = KeplerMath.RotateOrbitalToWorld(pfP, nodeRad, incl, argpRad);
                Vector3d v0 = KeplerMath.RotateOrbitalToWorld(pfV, nodeRad, incl, argpRad);

                KeplerPredictor.Advance(r0, v0, mu, 0d, out Vector3d prtP, out Vector3d prtV);
                double rtErr = Math.Max((prtP - r0).Magnitude / r0.Magnitude, (prtV - v0).Magnitude / v0.Magnitude);
                double energy0 = 0.5d * v0.SqrMagnitude - mu / r0.Magnitude;
                double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);
                KeplerPredictor.Advance(r0, v0, mu, 0.1d * period, out Vector3d paP, out Vector3d paV);
                double energy1 = 0.5d * paV.SqrMagnitude - mu / paP.Magnitude;
                double eDrift = Math.Abs((energy1 - energy0) / energy0);

                planet2.EvaluateWorldState(0d, out Vector3d pp0, out Vector3d pv0);
                var phys = new SpacecraftPhysics();
                phys.Sources.Add(new CachedGravitySource(sys2));
                var ship = new Spacecraft(pp0 + r0, pv0 + v0, 1000d);
                phys.Step(ship, 0d, 0.25d * period);
                planet2.EvaluateWorldState(0.25d * period, out Vector3d pp1, out _);
                KeplerPredictor.Advance(r0, v0, mu, 0.25d * period, out Vector3d kpP, out _);
                double numErr = ((ship.Position - pp1) - kpP).Magnitude;
                perCase.Add(string.Format("e={0},i={1}°:{2:F2}м", e, inc[ii], numErr));
                if (rtErr > worstRoundtrip)
                {
                    worstRoundtrip = rtErr;
                    worstCase = string.Format("e={0} i={1}°", e, inc[ii]);
                }

                if (eDrift > worstEnergy)
                {
                    worstEnergy = eDrift;
                }

                if (numErr > worstNumeric)
                {
                    worstNumeric = numErr;
                    worstNumericCase = string.Format("e={0} i={1}°", e, inc[ii]);
                }
            }
        }

        Check(worstRoundtrip < 1e-6d && worstEnergy < 1e-9d && worstNumeric < 10d, "T26 circular-matrix",
            string.Format("roundtrip худш.={0:E2} ({1}); E-дрейф худш.={2:E2}; numeric T/4 худш.={3:F2}м ({4}, <10м) [{5}]",
                worstRoundtrip, worstCase, worstEnergy, worstNumeric, worstNumericCase, string.Join(" ", perCase.ToArray())));
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

    private static ManeuverEvaluator BuildEvaluator(SpacecraftPhysics phys, double dryMassKg, out WarpController warp)
    {
        var coastProp = new EventDrivenPropagator(phys);
        var burnProp = new EventDrivenPropagator(phys);
        burnProp.CrossingDetectors.Add(new PropellantDepletionDetector(dryMassKg, "main"));
        var driver = new LongWarpDriver(phys, coastProp);
        warp = new WarpController();
        return new ManeuverEvaluator(phys, coastProp, driver, burnProp, warp);
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

    private sealed class BurnRig
    {
        public SpacecraftPhysics Phys;
        public ThrustSource Thrust;
        public ManeuverEvaluator Eval;
        public double BurnSeconds;
        public double Mass0;
        public double Isp;
    }

    private static BurnRig BuildBurnRig(StarSystem sys, OrbitingBody planet, double m0, double thrustN, double isp, double burnSeconds)
    {
        var thrust = new ThrustSource(thrustN, 1000d, new ConstantIsp(isp));
        thrust.ThrottleAt = (time) => time >= 100d && time < 100d + burnSeconds ? 1d : 0d;
        var phys = FlightPhysics(sys, false, planet);
        phys.Sources.Add(thrust);
        WarpController warp;
        ManeuverEvaluator eval = BuildEvaluator(phys, 1000d, out warp);
        return new BurnRig { Phys = phys, Thrust = thrust, Eval = eval, BurnSeconds = burnSeconds, Mass0 = m0, Isp = isp };
    }

    private static double RunProgradeBurn(StarSystem sys, OrbitingBody planet, BurnRig rig, Vector3d parkP0, Vector3d parkV0, BurnTolerancePreset preset,
        out Vector3d idealEndPosition, out Vector3d idealEndVelocity, out SpacecraftIntegrationState endState, out double fuelUsed)
    {
        // Идеал = мгновенный Δv в зажигании + coast аналитикой Kepler
        // (независимый от DOPRI механизм — иначе общий баг спрятался бы).
        // Направление — инерциальный прогонный вектор в зажигании (без steering).
        double cutTime = 100d + rig.BurnSeconds;
        var preShip = new Spacecraft(parkP0, parkV0, rig.Mass0);
        var coastOnly = new LongWarpDriver(rig.Phys, new EventDrivenPropagator(rig.Phys));
        coastOnly.AdvanceToTarget(preShip, 0d, 100d);
        planet.EvaluateWorldState(100d, out Vector3d bpI, out Vector3d bvI);
        Vector3d dir = (preShip.Velocity - bvI).Normalized;
        rig.Thrust.ThrustDirection = dir;
        double mdot = rig.Thrust.ThrustMaxNewtons / (rig.Isp * ThrustSource.StandardGravity);
        double dvIdeal = rig.Isp * ThrustSource.StandardGravity * Math.Log(rig.Mass0 / (rig.Mass0 - (mdot * rig.BurnSeconds)));
        Vector3d relPreP = preShip.Position - bpI;
        Vector3d relPreV = preShip.Velocity - bvI;
        double mu = planet.ResolveStandardGravitationalParameter();
        KeplerPredictor.Advance(relPreP, relPreV + (dir * dvIdeal), mu, rig.BurnSeconds, out Vector3d relIdealP, out Vector3d relIdealV);
        planet.EvaluateWorldState(cutTime, out Vector3d bpC, out Vector3d bvC);
        idealEndPosition = bpC + relIdealP;
        idealEndVelocity = bvC + relIdealV;

        var liveShip = new Spacecraft(parkP0, parkV0, rig.Mass0);
        ManeuverResult run = rig.Eval.CoastThenBurn(liveShip, 0d, 100d, cutTime, preset, 1d);
        endState = run.EndState;
        fuelUsed = rig.Mass0 - run.EndState.Mass;
        return (run.EndState.Velocity - idealEndVelocity).Magnitude;
    }

    private static int Test29_DescentChain()
    {
        // V10: сквозная цепочка — вход, торможение парашютом (A=1500),
        // мягкое касание, реакция, сутки стояния со следованием за вращением.
        // Каждая фаза — своим допуском из существующих тестов, не одним общим.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d,
            SeaLevelPressurePascals = 101325d
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
        // V11: терминальная скорость против аналитики v=√(2mg/ρCdA) на плоском
        // профиле (H=1e6м — плотность почти const на участке замера).
        // Независимый эталон drag-динамики (был только rate-baseline на орбите).
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 2000000d,
            SeaLevelDensityKgPerCubicMeter = 0.1d,
            ScaleHeightMeters = 1000000d,
            SeaLevelPressurePascals = 10000d
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

        // 0.9% измеренного — понятный состав: градиенты g/ρ по окну ~0.5% +
        // остаточный переходный (ещё догоняет терминал сверху). Систематика,
        // не шум: граница 1.5% держит с запасом, но ловит регресс модели.
        double meanDown = count > 0 ? sumDown / count : double.NaN;
        double relErr = Math.Abs(meanDown / analytic - 1d);
        Check(count > 50 && relErr < 0.015d, "T29 terminal-velocity",
            string.Format("сэмплов в окне 20–30км: {0}; средняя {1:F2}м/с vs аналитика {2:F2} (Δ={3:E2}, допуск 1.5%)",
                count, meanDown, analytic, relErr));
        return 0;
    }

    private struct YearDriftAngles
    {
        public double MaxInclDriftRad;
        public double MaxNodeDriftRad;
        public double MaxApsDriftRad;
        public int NanNodeMonths;
        public int NanApsMonths;
        public int InvalidMonths;
    }

    private static double AngDiffRad(double aDeg, double bDeg)
    {
        double d = Math.Abs(aDeg - bDeg) % 360d;
        if (d > 180d)
        {
            d = 360d - d;
        }

        return d * Math.PI / 180d;
    }

    private static string YearDriftCase(StarSystem sys, OrbitingBody planet, Vector3d relP0, Vector3d relV0, double trueApoapsis, string name, bool boundAps, out double maxEnergyDrift, out double maxApoDev, out YearDriftAngles angles)
    {
        // Год баллистики помесячными чанками (TwoBodySystem: луны нет, прилив
        // звезды ~1e-6 — внутри границ ниже). Возвращает худшие дрейфы.
        double span = 365d * 86400d;
        double month = span / 12d;
        planet.EvaluateWorldState(0d, out Vector3d pp0, out Vector3d pv0);
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(pp0 + relP0, pv0 + relV0, 1000d);
        double mu = planet.ResolveStandardGravitationalParameter();
        double e0 = 0.5d * relV0.SqrMagnitude - mu / relP0.Magnitude;
        // База — ИСТИННЫЙ апоцентр a(1+e), а не начальный радиус: иначе «дрейф»
        // меряет эксцентриситет начальных условий, а не численник (поймано:
        // e=0.05 давал 7.9e-2 именно поэтому, орбита при этом стабильна).
        double apo0 = trueApoapsis;
        OrbitalElements el0 = OrbitalElements.FromState(relP0, relV0, mu);
        double incl0 = el0.InclinationDegrees;
        double node0 = el0.LongitudeOfAscendingNodeDegrees;
        double aps0 = el0.ArgumentOfPeriapsisDegrees;
        maxEnergyDrift = 0d;
        maxApoDev = 0d;
        angles = new YearDriftAngles();
        double t = 0d;
        for (int m = 0; m < 12; m++)
        {
            var segs = new List<DenseSegment>();
            EventOccurrence? ev = prop.PropagateWithSegments(ship, t, month, segs);
            if (ev.HasValue)
            {
                break;
            }

            t += month;
            double maxR = 0d;
            for (int i = 0; i < segs.Count; i++)
            {
                planet.EvaluateWorldState(segs[i].T0, out Vector3d bp, out _);
                double r = (segs[i].Y0.Position - bp).Magnitude;
                if (r > maxR) maxR = r;
            }

            planet.EvaluateWorldState(t, out Vector3d bpt, out Vector3d bvt);
            Vector3d relP = ship.Position - bpt;
            Vector3d relV = ship.Velocity - bvt;
            double e = 0.5d * relV.SqrMagnitude - mu / relP.Magnitude;
            double de = Math.Abs((e - e0) / e0);
            double da = Math.Abs((maxR - apo0) / apo0);
            if (de > maxEnergyDrift) maxEnergyDrift = de;
            if (da > maxApoDev) maxApoDev = da;

            OrbitalElements el = OrbitalElements.FromState(relP, relV, mu);
            if (!el.IsValid)
            {
                angles.InvalidMonths++;
                continue;
            }

            double di = AngDiffRad(el.InclinationDegrees, incl0);
            if (di > angles.MaxInclDriftRad) angles.MaxInclDriftRad = di;
            if (double.IsNaN(el.LongitudeOfAscendingNodeDegrees))
            {
                angles.NanNodeMonths++;
            }
            else if (!double.IsNaN(node0))
            {
                double dn = AngDiffRad(el.LongitudeOfAscendingNodeDegrees, node0);
                if (dn > angles.MaxNodeDriftRad) angles.MaxNodeDriftRad = dn;
            }

            // Аргумент перицентра осмыслен только при реальном e: у круговых
            // кейсов e≈0 и ω блуждает на радианы (замер: 1.4–2.8) — это не дрейф,
            // а неопределённость самого угла. Граница только при boundAps.
            if (double.IsNaN(el.ArgumentOfPeriapsisDegrees))
            {
                angles.NanApsMonths++;
            }
            else if (boundAps && !double.IsNaN(aps0))
            {
                double dw = AngDiffRad(el.ArgumentOfPeriapsisDegrees, aps0);
                if (dw > angles.MaxApsDriftRad) angles.MaxApsDriftRad = dw;
            }
        }

        return string.Format("{0}: ΔE/E={1:E2} Δапо={2:E2} Δi={3:E2} ΔΩ={4:E2} Δω={5:E2} NaN(Ω/ω)={6}/{7} invalid={8}",
            name, maxEnergyDrift, maxApoDev,
            angles.MaxInclDriftRad, angles.MaxNodeDriftRad, angles.MaxApsDriftRad,
            angles.NanNodeMonths, angles.NanApsMonths, angles.InvalidMonths);
    }

    private static int Test30_YearDrift()
    {
        // V5/V6: годовые дрейфы bound-орбит. Границы после замера (дисциплина):
        // первичный прогон печатает числа, границы фиксируем по ним с запасом.
        StarSystem sys = TwoBodySystem();
        OrbitingBody planet = sys.AllBodies[1];
        double mu = planet.ResolveStandardGravitationalParameter();
        double r = planet.Radius + 2e7d;
        double circV = Math.Sqrt(mu / r);
        var results = new List<string>();
        double wE = 0d;
        double wA = 0d;

        string[] names = new string[] { "круг", "поляр", "ретро", "e=0.05" };
        Vector3d[] pp = new Vector3d[4];
        Vector3d[] vv = new Vector3d[4];
        // В точке +Y прямое движение — это −X (против часовой с +Z):
        // h=(0,R,0)×(−v,0,0)=(0,0,+Rv), i=0. Знак +X даёт i=180° (ретро).
        pp[0] = new Vector3d(0d, r, 0d);
        vv[0] = new Vector3d(-circV, 0d, 0d);
        pp[1] = new Vector3d(r, 0d, 0d);
        vv[1] = new Vector3d(0d, 0d, circV);
        pp[2] = new Vector3d(0d, r, 0d);
        vv[2] = new Vector3d(circV, 0d, 0d);
        double ae = r;
        double yS = Math.Sqrt(1d - (0.05d * 0.05d));
        double e0a = 1d;
        var pfP = new Vector3d(ae * (Math.Cos(e0a) - 0.05d), ae * yS * Math.Sin(e0a), 0d);
        double vF = Math.Sqrt(mu * ae) / pfP.Magnitude;
        var pfV = new Vector3d(-vF * Math.Sin(e0a), vF * yS * Math.Cos(e0a), 0d);
        pp[3] = KeplerMath.RotateOrbitalToWorld(pfP, 0.7d, 0.1d, 0.5d);
        vv[3] = KeplerMath.RotateOrbitalToWorld(pfV, 0.7d, 0.1d, 0.5d);

        double[] trueApo = new double[] { r, r, r, r * 1.05d };
        double[] deCase = new double[4];
        double worstIncl = 0d;
        double worstNode = 0d;
        double worstAps = 0d;
        int nanNodeTotal = 0;
        int nanApsTotal = 0;
        int invalidTotal = 0;
        for (int i = 0; i < 4; i++)
        {
            double de;
            double da;
            YearDriftAngles ang;
            // Граница ω — только e=0.05 (реальный эксцентриситет): у круговых
            // ω не определён физически, замер дал блуждание 1.4–2.8 рад.
            results.Add(YearDriftCase(sys, planet, pp[i], vv[i], trueApo[i], names[i], i == 3, out de, out da, out ang));
            deCase[i] = de;
            if (de > wE) wE = de;
            if (da > wA) wA = da;
            if (ang.MaxInclDriftRad > worstIncl) worstIncl = ang.MaxInclDriftRad;
            if (ang.MaxNodeDriftRad > worstNode) worstNode = ang.MaxNodeDriftRad;
            if (ang.MaxApsDriftRad > worstAps) worstAps = ang.MaxApsDriftRad;
            nanNodeTotal += ang.NanNodeMonths;
            nanApsTotal += ang.NanApsMonths;
            invalidTotal += ang.InvalidMonths;
        }

        // Ретро-сплит: та же физика и поле, дрейф обязан совпадать почти точно
        // (замер: 0.99). Коридор [0.5, 2.0] — расхождение в 2 раза уже аномалия.
        // Индивидуальные границы энергии ловят регресс одного кейса, который
        // общий worst скрыл бы.
        double retroRatio = deCase[0] > 0d ? deCase[2] / deCase[0] : double.NaN;
        // ΔΩ e=0.05 (6.3e-3) — реальная солнечная регрессия узла (оценка:
        // 0.75·n·(M★/M)·(a/R)³·T ≈ 6e-3), не численник. Граница 0.05 с запасом,
        // но на порядки ниже уровня «что-то сломалось».
        Check(wE < 5e-5d && wA < 1e-3d
            && deCase[0] < 5e-5d && deCase[1] < 5e-5d && deCase[2] < 5e-5d && deCase[3] < 5e-5d
            && retroRatio > 0.5d && retroRatio < 2.0d
            && worstIncl < 1e-2d && worstNode < 0.05d && worstAps < 0.1d
            && nanNodeTotal == 0 && nanApsTotal == 0 && invalidTotal == 0, "T30 year-drift",
            string.Format("год, 4 кейса [{0}]; худш. ΔE/E={1:E2}, худш. Δапо={2:E2}; углы: Δi={3:E2}(<1e-2) ΔΩ={4:E2}(<0.05) Δω={5:E2}(<0.1, только e=0.05); NaN Ω/ω={6}/{7}(=0); invalid={8}(=0); ретро/прямо={9:F2} ([0.5,2])",
                string.Join(" ", results.ToArray()), wE, wA, worstIncl, worstNode, worstAps, nanNodeTotal, nanApsTotal, invalidTotal, retroRatio));
        return 0;
    }

    private static int Test31_Flyby()
    {
        // V1: гиперболический пролёт планеты (v∞=60км/с, r_p=R+50км).
        // (i) Обратный ход Kepler: −2000с/+2000с возврат (валидирует и ICs крыльев).
        // (ii) |v∞| вход/выход на r=5e7: vis-viva симметрично, допуск 1e-3
        // (третьи тела дают ~1e-9 — запас 6 порядков).
        // (iii) Угол разворота vs 2·arcsin(1/e), e из перицентра: допуск 0.1°,
        // бюджет: конечность крыльев ~0.015°×2 + N-body ~1e-6°.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        double mu = planet.ResolveStandardGravitationalParameter();
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        double rp = planet.Radius + 50000d;
        double vinfExpect = 60000d;
        double vp = Math.Sqrt((vinfExpect * vinfExpect) + (2d * mu / rp));
        Vector3d periP = bp0 + new Vector3d(rp, 0d, 0d);
        Vector3d periV = bv0 + new Vector3d(0d, vp, 0d);

        KeplerPredictor.Advance(periP - bp0, periV - bv0, mu, -2000d, out Vector3d backP, out Vector3d backV);
        KeplerPredictor.Advance(backP, backV, mu, 2000d, out Vector3d fwdP, out Vector3d fwdV);
        double rtBack = Math.Max((fwdP - (periP - bp0)).Magnitude / rp, (fwdV - (periV - bv0)).Magnitude / vp);
        // Состояние назад — относительно планеты, а планета движется: в мир
        // корабль ставится через положение/скорость НА МОМЕНТ СТАРТА (−2000с),
        // не через bp0/bv0 (t=0). Иначе старт в 6e7м от расчётной ветки —
        // поймано: e=50 вместо 59 и нырок под поверхность при «честной» физике.
        planet.EvaluateWorldState(-2000d, out Vector3d bpBack, out Vector3d bvBack);

        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        var ship = new Spacecraft(bpBack + backP, bvBack + backV, 1000d);
        var track = new List<DenseSegment>();
        EventOccurrence? ev = prop.PropagateWithSegments(ship, -2000d, 20000d, track);
        bool clean = !ev.HasValue;

        // Двухпроходный скан трека (без хрупких порогов вроде rp+1000):
        // проход 1 — время перицентра (минимум радиуса), проход 2 — пересечения
        // крыла до/после него с линейной интерполяцией времени и состоянием
        // через dense-трек (ошибка ~ v·δt, δt — доли секунды).
        double wingR = 5e7d;
        double minR = double.MaxValue;
        double tPeri = track[0].T0;
        for (int i = 0; i < track.Count; i++)
        {
            DenseSegment seg = track[i];
            planet.EvaluateWorldState(seg.T0, out Vector3d bp, out _);
            double r0 = (seg.Y0.Position - bp).Magnitude;
            if (r0 < minR)
            {
                minR = r0;
                tPeri = seg.T0;
            }

            if (i == track.Count - 1)
            {
                planet.EvaluateWorldState(seg.T1, out Vector3d bp1, out _);
                double r1 = (seg.Y1.Position - bp1).Magnitude;
                if (r1 < minR)
                {
                    minR = r1;
                    tPeri = seg.T1;
                }
            }
        }

        bool foundIn = false;
        bool foundOut = false;
        double tIn = 0d;
        double tOut = 0d;
        for (int i = 0; i < track.Count && !(foundIn && foundOut); i++)
        {
            DenseSegment seg = track[i];
            planet.EvaluateWorldState(seg.T0, out Vector3d bp0s, out _);
            planet.EvaluateWorldState(seg.T1, out Vector3d bp1s, out _);
            double r0 = (seg.Y0.Position - bp0s).Magnitude;
            double r1 = (seg.Y1.Position - bp1s).Magnitude;
            bool crosses = (r0 >= wingR && r1 < wingR) || (r0 < wingR && r1 >= wingR);
            if (!crosses)
            {
                continue;
            }

            double tCross = seg.T0 + ((seg.T1 - seg.T0) * (wingR - r0) / (r1 - r0));
            if (tCross < tPeri && !foundIn)
            {
                foundIn = true;
                tIn = tCross;
            }
            else if (tCross > tPeri && !foundOut)
            {
                foundOut = true;
                tOut = tCross;
            }
        }

        SpacecraftIntegrationState stIn = DenseSegment.EvaluateTrack(track, tIn);
        SpacecraftIntegrationState stOut = DenseSegment.EvaluateTrack(track, tOut);
        Vector3d vIn = stIn.Velocity;
        Vector3d vOut = stOut.Velocity;

        planet.EvaluateWorldState(tIn, out Vector3d bpIn, out Vector3d bvIn);
        planet.EvaluateWorldState(tOut, out Vector3d bpOut, out Vector3d bvOut);
        Vector3d relVIn = vIn - bvIn;
        Vector3d relVOut = vOut - bvOut;
        double vinfIn = Math.Sqrt(Math.Max(0d, relVIn.SqrMagnitude - (2d * mu / wingR)));
        double vinfOut = Math.Sqrt(Math.Max(0d, relVOut.SqrMagnitude - (2d * mu / wingR)));
        double vinfRatio = Math.Abs(vinfOut / vinfIn - 1d);
        planet.EvaluateWorldState(tPeri, out _, out Vector3d bvPeri);
        SpacecraftIntegrationState stPeri = DenseSegment.EvaluateTrack(track, tPeri);
        Vector3d relVPeri = stPeri.Velocity - bvPeri;
        double eMeas = (minR * relVPeri.SqrMagnitude) / mu - 1d;
        Vector3d relPIn = stIn.Position - bpIn;
        Vector3d relPOut = stOut.Position - bpOut;
        KeplerPredictor.Advance(relPIn, relVIn, mu, tOut - tIn, out Vector3d kpP, out Vector3d kpV);
        double keplerTurn = Math.Acos(Math.Max(-1d, Math.Min(1d, Vector3d.Dot(relVIn, kpV) / (relVIn.Magnitude * kpV.Magnitude)))) * 180d / Math.PI;
        double keplerPosErr = (kpP - relPOut).Magnitude;
        double keplerVelErr = (kpV - relVOut).Magnitude;
        double expectTurn = 2d * Math.Asin(1d / eMeas) * 180d / Math.PI;
        double cosTurn = Vector3d.Dot(relVIn, relVOut) / (relVIn.Magnitude * relVOut.Magnitude);
        double measTurn = Math.Acos(Math.Max(-1d, Math.Min(1d, cosTurn))) * 180d / Math.PI;
        double turnErr = Math.Abs(measTurn - expectTurn);

        Check(clean && rtBack < 1e-6d && foundIn && foundOut && vinfRatio < 1e-3d && turnErr < 0.1d && keplerVelErr < 0.1d, "T31 flyby",
            string.Format("чисто={0} roundtrip-назад={1:E2}; крылья найдены {2}/{3}; |v∞| {4:F0}/{5:F0}м/с Δ={6:E2}(<1e-3); разворот {7:F3}° vs 2arcsin(1/{8:F1})={9:F3}° Δ={10:F4}°(<0.1); kepler-мост Δv={11:E2}м/с Δr={12:E2}м",
                clean, rtBack, foundIn, foundOut, vinfIn, vinfOut, vinfRatio, measTurn, eMeas, expectTurn, turnErr, keplerVelErr, keplerPosErr));
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

    private static int Test30_PartsTrap()
    {
        // R2a-ловушка: намеренно смещённая конфигурация (COM ≠ origin).
        // Раскладка: 700 @ (0,0,0), 200 @ (3,0,0), 100 @ (0,1,0).
        // Эталон посчитан вручную независимо (см. разбор):
        // COM=(0.6,0.1,0), Ixx=90, Iyy=1440, Izz=1530, Ixy=60, Ixz=Iyz=0.
        // Код «от origin» даёт 100/1800/1900/0 — другая структура тензора
        // (главные оси случайно совпадают с координатными), ловушка ловит
        // не шум, а неверную физику. Допуск 1e-9 относительный: расхождение
        // неверного кода — десятки процентов, запас 7+ порядков.
        // Проверено красным прогоном на d=r вместо d=r−COM (см. отчёт).
        var parts = new List<Part>();
        parts.Add(new Part(700d, new Vector3d(0d, 0d, 0d), 1e9d));
        parts.Add(new Part(200d, new Vector3d(3d, 0d, 0d), 1e9d));
        parts.Add(new Part(100d, new Vector3d(0d, 1d, 0d), 1e9d));
        AssemblyStats stats = AssemblyStats.Compute(parts);

        bool massOk = stats.TotalMassKg == 1000d;
        bool comOk = Math.Abs((stats.CenterOfMass.X - 0.6d) / 0.6d) < 1e-9d
            && Math.Abs((stats.CenterOfMass.Y - 0.1d) / 0.1d) < 1e-9d
            && stats.CenterOfMass.Z == 0d;
        Matrix3x3 inertia = stats.Inertia;
        bool diagOk = Math.Abs((inertia.M11 - 90d) / 90d) < 1e-9d
            && Math.Abs((inertia.M22 - 1440d) / 1440d) < 1e-9d
            && Math.Abs((inertia.M33 - 1530d) / 1530d) < 1e-9d;
        bool offDiagOk = Math.Abs((inertia.M12 - 60d) / 60d) < 1e-9d
            && inertia.M12 == inertia.M21
            && inertia.M13 == 0d && inertia.M31 == 0d
            && inertia.M23 == 0d && inertia.M32 == 0d;
        Matrix3x3 inv = inertia.Inverse();
        Vector3d probe = new Vector3d(1.5d, -2.5d, 3.5d);
        Vector3d roundtrip = inv * (inertia * probe);
        Vector3d diff = roundtrip - probe;
        bool inverseOk = diff.SqrMagnitude / probe.SqrMagnitude < 1e-24d;

        Check(massOk && comOk && diagOk && offDiagOk && inverseOk, "T30 parts-trap",
            string.Format("масса=1000: {0}; COM=(0.6,0.1,0): {1}; диаг(90,1440,1530): {2}; Ixy=60 симм/нули: {3}; I·I⁻¹: {4}",
                massOk, comOk, diagOk, offDiagOk, inverseOk));
        return 0;
    }

    private static Vector3d ZeroTorque(QuaternionD q, Vector3d w, double t)
    {
        return Vector3d.Zero;
    }

    private static int Test31_AttitudeSpin()
    {
        // R1a: свободное вращение вокруг главной оси — ω константа, угол ровно ωt.
        // R1b: постоянный момент вокруг главной оси из покоя — ω линейна, угол квадратичен.
        var inertiaA = new Matrix3x3(2d, 0d, 0d, 0d, 2d, 0d, 0d, 0d, 4d);
        var stateA = new AttitudeState(QuaternionD.Identity, new Vector3d(0d, 0d, 5d));
        for (int i = 0; i < 3000; i++)
        {
            stateA = AttitudePhysics.Step(stateA, inertiaA, ZeroTorque, 0.02d * i, 0.02d);
        }

        Vector3d turnedA = stateA.Attitude.Rotate(new Vector3d(1d, 0d, 0d));
        var expectA = new Vector3d(Math.Cos(300d), Math.Sin(300d), 0d);
        double angleErrA = (turnedA - expectA).Magnitude;
        double omegaErrA = (stateA.AngularVelocity - new Vector3d(0d, 0d, 5d)).Magnitude / 5d;

        var inertiaB = new Matrix3x3(2d, 0d, 0d, 0d, 3d, 0d, 0d, 0d, 4d);
        var stateB = new AttitudeState(QuaternionD.Identity, Vector3d.Zero);
        for (int i = 0; i < 500; i++)
        {
            stateB = AttitudePhysics.Step(stateB, inertiaB, (q, w, t) => new Vector3d(0d, 0d, 8d), 0.02d * i, 0.02d);
        }

        Vector3d turnedB = stateB.Attitude.Rotate(new Vector3d(1d, 0d, 0d));
        var expectB = new Vector3d(Math.Cos(100d), Math.Sin(100d), 0d);
        double angleErrB = (turnedB - expectB).Magnitude;
        double omegaErrB = (stateB.AngularVelocity - new Vector3d(0d, 0d, 20d)).Magnitude / 20d;

        // Границы по замеру: fixed RK4 dt=0.02 даёт фазу ~6e-6/1.6e-4 за 300/100 рад
        // (детерминировано, тригонометрии в степпере нет — только +−*/sqrt),
        // ω при этом держится на 1e-12..1e-14.
        Check(angleErrA < 1e-5d && omegaErrA < 1e-12d && angleErrB < 1e-3d && omegaErrB < 1e-9d, "T31 attitude-spin",
            string.Format("свободное: угол {0:E2} ω {1:E2}; момент: угол {2:E2} ω {3:E2}",
                angleErrA, omegaErrA, angleErrB, omegaErrB));
        return 0;
    }

    private static int Test31_AttitudeTumble()
    {
        // R1c: свободный кувырок несимметричного тела (тензор R2a!) 1000с:
        // энергия и |L| дрейфуют в границе после замера. Диагональ бы это
        // пропустила — прецессии/кувырка несимметричного тела там нет.
        var inertia = new Matrix3x3(90d, 60d, 0d, 60d, 1440d, 0d, 0d, 0d, 1530d);
        var w0 = new Vector3d(1d, 2d, 3d);
        double e0 = 0.5d * Vector3d.Dot(w0, inertia * w0);
        double l0 = (inertia * w0).Magnitude;
        Vector3d worldL0 = inertia * w0;
        var state = new AttitudeState(QuaternionD.Identity, w0);
        for (int i = 0; i < 50000; i++)
        {
            state = AttitudePhysics.Step(state, inertia, ZeroTorque, 0.02d * i, 0.02d);
        }

        double e1 = 0.5d * Vector3d.Dot(state.AngularVelocity, inertia * state.AngularVelocity);
        double l1 = (inertia * state.AngularVelocity).Magnitude;
        double qNorm = Math.Abs(state.Attitude.NormSquared - 1d);
        // Мировая L обязана сохраняться (момента нет): E и |L| от конвенции q̇
        // не зависят и перевёрнутый порядок q̇ НЕ ловят (доказано красным
        // прогоном — одноосевые вращения коммутируют, подалгебра ≅ ℂ).
        // Зеркальная кинематика разворачивает L_world на O(1) за секунды.
        Vector3d worldL1 = state.Attitude.Rotate(inertia * state.AngularVelocity);
        double worldDrift = (worldL1 - worldL0).Magnitude / worldL0.Magnitude;
        // Дрейф ~1e-5/1000с (линейный порядок, не экспонента): для постоянно
        // идущей симуляции темп ~1e-8/с, задокументирован, не скрыт.
        Check(Math.Abs((e1 - e0) / e0) < 1e-4d && Math.Abs((l1 - l0) / l0) < 1e-4d && qNorm < 1e-12d && worldDrift < 1e-3d, "T31 attitude-tumble",
            string.Format("1000с кувырка: ΔE/E={0:E2} Δ|L|/|L|={1:E2} |q|−1={2:E2} ΔL_world={3:E2}",
                Math.Abs((e1 - e0) / e0), Math.Abs((l1 - l0) / l0), qNorm, worldDrift));
        return 0;
    }

    private static int Test31_AttitudePD()
    {
        // R1d: PD гасит кувырок и держит цель. Контроллер test-local (как
        // CountingSource): продакшн-автопилот переиспользует эту логику
        // (TODO-узел), не дублирует. τ = −kp·err − kd·ω, err = 2·vec(qt⁻¹·q).
        var inertia = new Matrix3x3(100d, 0d, 0d, 0d, 120d, 0d, 0d, 0d, 150d);
        var state = new AttitudeState(
            QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), 0.5d),
            new Vector3d(0.5d, 0.3d, 0.2d));
        // Замыкание захватывает только константы (без аллокаций в тике):
        // ошибка считается внутри производной из переданного ей состояния.
        for (int i = 0; i < 6000; i++)
        {
            state = AttitudePhysics.Step(state, inertia,
                (q, w, t) => (new Vector3d(
                    (QuaternionD.Identity.Conjugated * q).X,
                    (QuaternionD.Identity.Conjugated * q).Y,
                    (QuaternionD.Identity.Conjugated * q).Z) * -40d) - (w * 100d),
                0.02d * i, 0.02d);
        }

        double omegaMag = state.AngularVelocity.Magnitude;
        double angle = 2d * Math.Acos(Math.Max(-1d, Math.Min(1d, Math.Abs(state.Attitude.W))));
        Check(omegaMag < 1e-3d && angle < 0.017d, "T31 attitude-pd",
            string.Format("120с PD: |ω|={0:E2} (<1e-3) угол={1:E2}рад (<0.017)", omegaMag, angle));
        return 0;
    }

    private static int Test31_TorqueLink()
    {
        // Связка тяги с ориентацией + поле Torque. Снимок поворачивает телесное
        // направление; без снимка — legacy мировой путь (старые тесты им идут).
        var thrust = new ThrustSource(1000d, 500d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => 1d;
        thrust.BodyThrustDirection = new Vector3d(0d, 0d, 1d);
        thrust.AttitudeSnapshot = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), Math.PI / 2d);
        DynamicsContribution cSnap = thrust.Evaluate(Vector3d.Zero, Vector3d.Zero, 900d, 0d);
        Vector3d expectDir = new Vector3d(0d, -1d, 0d);
        bool snapOk = (cSnap.Force.Normalized - expectDir).Magnitude < 1e-12d
            && Math.Abs(cSnap.Force.Magnitude - 1000d) / 1000d < 1e-12d
            && cSnap.MassFlow > 0d;

        var legacy = new ThrustSource(1000d, 500d, new ConstantIsp(300d));
        legacy.ThrottleAt = (time) => 1d;
        legacy.ThrustDirection = new Vector3d(0d, 1d, 0d);
        DynamicsContribution cLeg = legacy.Evaluate(Vector3d.Zero, Vector3d.Zero, 900d, 0d);
        bool legacyOk = (cLeg.Force.Normalized - new Vector3d(0d, 1d, 0d)).Magnitude == 0d;

        var block = new RcsBlock();
        block.Thrusters.Add(new RcsThruster(new Vector3d(1d, 0d, 0d), new Vector3d(0d, 0d, 1d), 10d));
        block.Thrusters.Add(new RcsThruster(new Vector3d(-1d, 0d, 0d), new Vector3d(0d, 0d, -1d), 10d));
        block.Thrusters[0].Active = true;
        block.Thrusters[1].Active = true;
        Vector3d torque = block.EvaluateTorque(QuaternionD.Identity, Vector3d.Zero, 0d);
        bool torqueOk = torque.X == 0d && torque.Y == -20d && torque.Z == 0d;
        DynamicsContribution full = block.EvaluateFull(QuaternionD.Identity, Vector3d.Zero, 0d);
        bool fullOk = full.Force.SqrMagnitude == 0d && full.Torque.Y == -20d;

        // Оживлённый канал (фаза 2.2): RcsBlock полным Evaluate идёт в
        // SpacecraftPhysics — сила в RHS, момент в снимок TotalTorqueBody;
        // снимок кормит AttitudePhysics.Step постоянным моментом и вращение
        // реально раскручивается.
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(block);
        bool noProviderLoud = false;
        try
        {
            phys.Step(new Spacecraft(Vector3d.Zero, Vector3d.Zero, 900d), 0d, 0.5d);
        }
        catch (InvalidOperationException)
        {
            noProviderLoud = true;
        }

        QuaternionD q90 = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), Math.PI / 2d);
        block.AttitudeProvider = () => q90;
        var shipRcs = new Spacecraft(Vector3d.Zero, Vector3d.Zero, 900d);
        phys.Step(shipRcs, 0d, 0.5d);
        bool snapshotOk = phys.TotalTorqueBody.X == 0d && phys.TotalTorqueBody.Y == -20d && phys.TotalTorqueBody.Z == 0d;
        Vector3d torqueSnapshot = phys.TotalTorqueBody;

        // Сила того же источника реально в орбитальной RHS: один дюз (0,0,1)·10Н
        // при q90 даёт мировую силу (0,−10,0) → Δv = −10/900 ≈ −0.011 м/с по y.
        block.Thrusters[1].Active = false;
        var shipForce = new Spacecraft(Vector3d.Zero, Vector3d.Zero, 900d);
        phys.Step(shipForce, 0d, 1d);
        bool forcePathOk = shipForce.Velocity.Y < -0.005d && Math.Abs(shipForce.Velocity.Y + (10d / 900d)) < 1e-6d;

        var inertia1 = new Matrix3x3(1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d);
        AttitudeState att1 = AttitudePhysics.Step(new AttitudeState(QuaternionD.Identity, Vector3d.Zero), inertia1, torqueSnapshot, 0d, 0.1d);
        bool spinOk = att1.AngularVelocity.Y < -1.5d;

        Check(snapOk && legacyOk && torqueOk && fullOk && noProviderLoud && snapshotOk && forcePathOk && spinOk, "T31 torque-link",
            string.Format("снимок→(0,−1,0): {0}; legacy мировой: {1}; RCS τ=(0,−20,0): {2}; full с Torque: {3}; без провайдера громко: {4}; τ в снимке: {5}; сила в RHS: {6}; снимок крутит вращение: {7}",
                snapOk, legacyOk, torqueOk, fullOk, noProviderLoud, snapshotOk, forcePathOk, spinOk));
        return 0;
    }

    private static QuaternionD PitchTarget(QuaternionD vertical, double timeSeconds)
    {
        double tilt = timeSeconds < 20d ? 0d : (15d * Math.PI / 180d) * Math.Min(1d, (timeSeconds - 20d) / 80d);
        return QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), -tilt) * vertical;
    }

    private static int Test33_Ascent()
    {
        // R3: взлёт со связкой ориентация→тяга. Снимок ориентации и расписание
        // газа идут на одной тиковой сетке 0.5с (игровой цикл обязан держать
        // обе на одних часах — здесь это инвариант цикла теста, не кода).
        // Отрицательный контроль: фиксированный мировой вектор тяги даёт
        // другую (вертикальную) траекторию — изгиб именно от связки.
        StarSystem sys = TestSystem();
        OrbitingBody planet = sys.AllBodies[1];
        planet.RotationPeriodSeconds = 0d;
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        // Площадка lon=90°: оффсет (0,R,0), нормаль +Y — туда же смотрят нос
        // (q0), вертикальный контроль (0,1,0) и векторы liftoff-теста.
        // lat=0,lon=0 дал бы нормаль +X и ракета летела бы боком (поймано).
        planet.GetSurfaceState(0d, 90d, 0d, 0d, out Vector3d padP, out Vector3d padV);
        var inertia = new Matrix3x3(15000d, 0d, 0d, 0d, 15000d, 0d, 0d, 0d, 8000d);
        QuaternionD vertical = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), -Math.PI / 2d);

        var thrust = new ThrustSource(80000d, 1000d, new ConstantIsp(300d));
        thrust.ThrottleAt = (time) => time < 120d ? 1d : 0d;
        thrust.BodyThrustDirection = new Vector3d(0d, 0d, 1d);
        var phys = FlightPhysics(sys, false, planet);
        phys.Sources.Add(thrust);
        var ship = new Spacecraft(padP, padV, 5000d);
        var attitude = new AttitudeState(vertical, Vector3d.Zero);
        double maxOmega = 0d;
        double t = 0d;
        while (t < 120d - 1e-12d)
        {
            thrust.AttitudeSnapshot = attitude.Attitude;
            QuaternionD target = PitchTarget(vertical, t);
            for (int k = 0; k < 25; k++)
            {
                attitude = AttitudePhysics.Step(attitude, inertia,
                    (q, w, tt) => (new Vector3d(
                        (target.Conjugated * q).X,
                        (target.Conjugated * q).Y,
                        (target.Conjugated * q).Z) * -6000d) - (w * 15000d),
                    t + (0.02d * k), 0.02d);
            }

            if (attitude.AngularVelocity.Magnitude > maxOmega)
            {
                maxOmega = attitude.AngularVelocity.Magnitude;
            }

            phys.Step(ship, t, 0.5d);
            t += 0.5d;
        }

        planet.EvaluateWorldState(t, out Vector3d bpE, out _);
        double bentHoriz = Math.Abs((ship.Position - bpE).X - (padP - bp0).X);
        double bentAlt = (ship.Position - bpE).Magnitude - planet.Radius;
        double fuelUsed = 5000d - ship.Mass;

        var thrustV = new ThrustSource(80000d, 1000d, new ConstantIsp(300d));
        thrustV.ThrottleAt = (time) => time < 120d ? 1d : 0d;
        thrustV.ThrustDirection = new Vector3d(0d, 1d, 0d);
        var physV = FlightPhysics(sys, false, planet);
        physV.Sources.Add(thrustV);
        var shipV = new Spacecraft(padP, padV, 5000d);
        double tv = 0d;
        while (tv < 120d - 1e-12d)
        {
            physV.Step(shipV, tv, 0.5d);
            tv += 0.5d;
        }

        planet.EvaluateWorldState(tv, out Vector3d bpV, out _);
        double controlHoriz = Math.Abs((shipV.Position - bpV).X - (padP - bp0).X);

        var padShip = new Spacecraft(padP, padV, 5000d);
        VesselRegime liftOn = EventReactions.TryLiftoff(padShip, planet,
            new Vector3d(0d, 80000d / 5000d, 0d), new Vector3d(0d, -9.82d, 0d), 0d, out _);
        var padShip2 = new Spacecraft(padP, padV, 5000d);
        VesselRegime liftOff = EventReactions.TryLiftoff(padShip2, planet,
            Vector3d.Zero, new Vector3d(0d, -9.82d, 0d), 0d, out _);

        Check(bentHoriz > 1000d && controlHoriz < bentHoriz / 10d && Math.Abs(fuelUsed - 3264d) < 5d
            && liftOn == VesselRegime.Flying && liftOff == VesselRegime.Landed, "T33 ascent",
            string.Format("изгиб {0:F0}м vs вертикаль {1:F0}м; топливо {2:F1}кг (≈3264); взлёт с тягой={3} без={4}; max|ω|={5:E2}рад/с (снос ≤{6:E2}рад/чанк)",
                bentHoriz, controlHoriz, fuelUsed, liftOn, liftOff, maxOmega, maxOmega * 0.5d));
        return 0;
    }

    private static int Test33_StagingMomentum()
    {
        // T33a: m1=800, m2=200, v0=(1000,0,0), Δv_rel=(2,0,0).
        // Эталон посчитан вручную: v1=(1000.4,0,0), v2=(998.4,0,0),
        // 800·1000.4+200·998.4=1,000,000=M·v0 ровно. Допуск — fp-шум (~1e-12),
        // не «близко»: формула обязана держать тождество, а не приближение.
        var pool = new DebrisPool();
        var ship = new Spacecraft(new Vector3d(1e7d, 0d, 0d), new Vector3d(1000d, 0d, 0d), 1000d);
        int slot = ManeuverEvaluator.StageSeparation(ship, 200d, new Vector3d(2d, 0d, 0d), pool, 0d, 100d);
        Spacecraft stage = pool.SlotAt(slot).Body;
        double totalMomentum = (800d * ship.Velocity.X) + (200d * stage.Velocity.X);
        double relErr = Math.Abs((totalMomentum - 1000000d) / 1000000d);
        bool massesOk = ship.Mass == 800d && stage.Mass == 200d;
        bool velocitiesOk = Math.Abs(ship.Velocity.X - 1000.4d) < 1e-9d && Math.Abs(stage.Velocity.X - 998.4d) < 1e-9d;
        bool poolOk = pool.ActiveCount == 1 && pool.SlotAt(slot).Active;
        Check(massesOk && velocitiesOk && poolOk && relErr < 1e-12d, "T33 staging-momentum",
            string.Format("m1=800 v1={0:R}, m2=200 v2={1:R}; Σmv={2:R} (эталон 1000000); relErr={3:E2}; слот активен: {4}",
                ship.Velocity.X, stage.Velocity.X, totalMomentum, relErr, poolOk));
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

    // ---- Step 0: офлайн-замер взаимных возмущений (НЕ игровой код) ----
    // Автономный N-body (fixed-step RK4, барицентр, без аллокаций в цикле):
    // меряет, насколько планеты реально тянут друг друга относительно рельс.
    // Если дрейф неразличим — рельсы остаются навсегда; иначе данные решают
    // bake vs PEFRL. Тест ниже фиксирует решение числами как регресс-гейт.
    private static void NBodyAccel(double[] p, double[] a, double[] m, double g, int n)
    {
        for (int i = 0; i < 3 * n; i++)
        {
            a[i] = 0d;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * j] - p[3 * i];
                double dy = p[3 * j + 1] - p[3 * i + 1];
                double dz = p[3 * j + 2] - p[3 * i + 2];
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double r = Math.Sqrt(r2);
                double s = g / (r2 * r);
                double fi = s * m[j];
                double fj = s * m[i];
                a[3 * i] += fi * dx;
                a[3 * i + 1] += fi * dy;
                a[3 * i + 2] += fi * dz;
                a[3 * j] -= fj * dx;
                a[3 * j + 1] -= fj * dy;
                a[3 * j + 2] -= fj * dz;
            }
        }
    }

    private static void NBodyRK4Step(double[] p, double[] v, double[] m, double g, int n, double h,
        double[] kp1, double[] kv1, double[] kp2, double[] kv2, double[] kp3, double[] kv3, double[] kp4, double[] kv4,
        double[] tp, double[] ta)
    {
        NBodyAccel(p, ta, m, g, n);
        for (int i = 0; i < 3 * n; i++)
        {
            kp1[i] = v[i];
            kv1[i] = ta[i];
            tp[i] = p[i] + (0.5d * h * kp1[i]);
        }

        NBodyAccel(tp, ta, m, g, n);
        for (int i = 0; i < 3 * n; i++)
        {
            kp2[i] = v[i] + (0.5d * h * kv1[i]);
            kv2[i] = ta[i];
            tp[i] = p[i] + (0.5d * h * kp2[i]);
        }

        NBodyAccel(tp, ta, m, g, n);
        for (int i = 0; i < 3 * n; i++)
        {
            kp3[i] = v[i] + (0.5d * h * kv2[i]);
            kv3[i] = ta[i];
            tp[i] = p[i] + (h * kp3[i]);
        }

        NBodyAccel(tp, ta, m, g, n);
        for (int i = 0; i < 3 * n; i++)
        {
            kp4[i] = v[i] + (h * kv3[i]);
            kv4[i] = ta[i];
        }

        for (int i = 0; i < 3 * n; i++)
        {
            p[i] += (h / 6d) * ((kp1[i] + kp4[i]) + (2d * (kp2[i] + kp3[i])));
            v[i] += (h / 6d) * ((kv1[i] + kv4[i]) + (2d * (kv2[i] + kv3[i])));
        }
    }

    private static double SubtreeMass(System.Collections.Generic.IReadOnlyList<Galilego.Universe.OrbitingBody> bodies, double[] m, int n, int idx)
    {
        double total = m[idx];
        var b = bodies[idx];
        for (int j = 0; j < b.Children.Count; j++)
        {
            for (int k = 0; k < n; k++)
            {
                if (ReferenceEquals(bodies[k], b.Children[j]))
                {
                    total += SubtreeMass(bodies, m, n, k);
                    break;
                }
            }
        }

        return total;
    }

    // Прогон spanYears с шагом stepSeconds, сэмпл каждые sampleYears:
    // возвращает максимальные отклонения от рельс (звездо-относительно)
    // по телам. Барицентр на входе, сравнение в звездо-центрированной рамке
    // (разность сокращает дрейф начала координат с обеих сторон).
    /// <summary>
    /// Независимый n-body RK4 (мелкий шаг), записывающий СВОИ позиции
    /// (относительно звезды) в моменты k·sampleYears — без чтения рельс в ходе
    /// интегрирования. Благодаря этому справочный прогон независим от выпечки
    /// и может идти с ней параллельно на другом ядре (T60).
    /// </summary>
    internal static double[][] NBodyReference(StarSystem sys, double spanYears, double stepSeconds, double sampleYears, out double[] finalPos)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        double g = PhysicsSolver.GravitationalConstant;
        double[] m = new double[n];
        double[] p = new double[3 * n];
        double[] v = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
            m[i] = bodies[i].Mass;
            p[3 * i] = bp.X;
            p[3 * i + 1] = bp.Y;
            p[3 * i + 2] = bp.Z;
            v[3 * i] = bv.X;
            v[3 * i + 1] = bv.Y;
            v[3 * i + 2] = bv.Z;
        }

        // Та же якоби-инициализация, что у NBodyDeviations (дети-вперёд).
        for (int i = n - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.Children.Count == 0 || m[i] <= 0d)
            {
                continue;
            }

            double sx = 0d; double sy = 0d; double sz = 0d;
            double svx = 0d; double svy = 0d; double svz = 0d;
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = -1;
                for (int k = 0; k < n; k++)
                {
                    if (ReferenceEquals(bodies[k], b.Children[j]))
                    {
                        c = k;
                        break;
                    }
                }

                if (c < 0)
                {
                    continue;
                }

                double mc = SubtreeMass(bodies, m, n, c);
                sx += mc * (p[3 * c] - p[3 * i]);
                sy += mc * (p[3 * c + 1] - p[3 * i + 1]);
                sz += mc * (p[3 * c + 2] - p[3 * i + 2]);
                svx += mc * (v[3 * c] - v[3 * i]);
                svy += mc * (v[3 * c + 1] - v[3 * i + 1]);
                svz += mc * (v[3 * c + 2] - v[3 * i + 2]);
            }

            p[3 * i] -= sx / m[i];
            p[3 * i + 1] -= sy / m[i];
            p[3 * i + 2] -= sz / m[i];
            v[3 * i] -= svx / m[i];
            v[3 * i + 1] -= svy / m[i];
            v[3 * i + 2] -= svz / m[i];
        }

        double totalM = 0d;
        double[] rc = new double[3];
        double[] vc = new double[3];
        for (int i = 0; i < n; i++)
        {
            totalM += m[i];
            rc[0] += m[i] * p[3 * i];
            rc[1] += m[i] * p[3 * i + 1];
            rc[2] += m[i] * p[3 * i + 2];
            vc[0] += m[i] * v[3 * i];
            vc[1] += m[i] * v[3 * i + 1];
            vc[2] += m[i] * v[3 * i + 2];
        }

        for (int i = 0; i < n; i++)
        {
            p[3 * i] -= rc[0] / totalM;
            p[3 * i + 1] -= rc[1] / totalM;
            p[3 * i + 2] -= rc[2] / totalM;
            v[3 * i] -= vc[0] / totalM;
            v[3 * i + 1] -= vc[1] / totalM;
            v[3 * i + 2] -= vc[2] / totalM;
        }

        var kp1 = new double[3 * n];
        var kv1 = new double[3 * n];
        var kp2 = new double[3 * n];
        var kv2 = new double[3 * n];
        var kp3 = new double[3 * n];
        var kv3 = new double[3 * n];
        var kp4 = new double[3 * n];
        var kv4 = new double[3 * n];
        var tp = new double[3 * n];
        var ta = new double[3 * n];

        double year = 365d * 86400d;
        double span = spanYears * year;
        double sampleDt = sampleYears * year;
        int sampleCount = (int)Math.Round(span / sampleDt);
        var samples = new double[sampleCount][];
        double t = 0d;
        double nextSample = sampleDt;
        int nextIdx = 0;
        while (t < span - 1e-9d)
        {
            double h = Math.Min(stepSeconds, span - t);
            NBodyRK4Step(p, v, m, g, n, h, kp1, kv1, kp2, kv2, kp3, kv3, kp4, kv4, tp, ta);
            t += h;
            if (t + 1e-9d >= nextSample && nextIdx < sampleCount)
            {
                nextSample += sampleDt;
                var s = new double[3 * n];
                for (int i = 0; i < n; i++)
                {
                    s[3 * i] = p[3 * i] - p[0];
                    s[3 * i + 1] = p[3 * i + 1] - p[1];
                    s[3 * i + 2] = p[3 * i + 2] - p[2];
                }

                samples[nextIdx++] = s;
            }
        }

        finalPos = new double[3 * n];
        Array.Copy(p, finalPos, 3 * n);
        return samples;
    }

    private static double[] NBodyDeviations(StarSystem sys, double spanYears, double stepSeconds, double sampleYears, out double[] finalPos)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        double g = PhysicsSolver.GravitationalConstant;
        double[] m = new double[n];
        double[] p = new double[3 * n];
        double[] v = new double[3 * n];
        double totalM = 0d;
        double[] rc = new double[3];
        double[] vc = new double[3];
        for (int i = 0; i < n; i++)
        {
            bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
            m[i] = bodies[i].Mass;
            p[3 * i] = bp.X;
            p[3 * i + 1] = bp.Y;
            p[3 * i + 2] = bp.Z;
            v[3 * i] = bv.X;
            v[3 * i + 1] = bv.Y;
            v[3 * i + 2] = bv.Z;
        }

        // Инициализация Якоби: рельса планеты — это барицентр подсистемы
        // (μ-фикс), а не центр планеты. Без коррекции N-body барицентр стартует
        // со сдвигом Mm/(Mp+Mm)*v_rel (~12 м/с → ~1e9 м/год ложного дрейфа,
        // поймано диагностикой: baryDev линейный с нуля). Коррекция снизу вверх:
        // центр родителя смещается так, чтобы барицентр поддерева совпал с рельсой.
        // Обход дети-вперёд (AllBodies — родители-вперёд, идём с конца).
        for (int i = n - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.Children.Count == 0 || m[i] <= 0d)
            {
                continue;
            }

            double sx = 0d; double sy = 0d; double sz = 0d;
            double svx = 0d; double svy = 0d; double svz = 0d;
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = -1;
                for (int k = 0; k < n; k++)
                {
                    if (ReferenceEquals(bodies[k], b.Children[j]))
                    {
                        c = k;
                        break;
                    }
                }

                if (c < 0)
                {
                    continue;
                }

                double mc = SubtreeMass(bodies, m, n, c);
                sx += mc * (p[3 * c] - p[3 * i]);
                sy += mc * (p[3 * c + 1] - p[3 * i + 1]);
                sz += mc * (p[3 * c + 2] - p[3 * i + 2]);
                svx += mc * (v[3 * c] - v[3 * i]);
                svy += mc * (v[3 * c + 1] - v[3 * i + 1]);
                svz += mc * (v[3 * c + 2] - v[3 * i + 2]);
            }

            p[3 * i] -= sx / m[i];
            p[3 * i + 1] -= sy / m[i];
            p[3 * i + 2] -= sz / m[i];
            v[3 * i] -= svx / m[i];
            v[3 * i + 1] -= svy / m[i];
            v[3 * i + 2] -= svz / m[i];
        }

        for (int i = 0; i < n; i++)
        {
            totalM += m[i];
            rc[0] += m[i] * p[3 * i];
            rc[1] += m[i] * p[3 * i + 1];
            rc[2] += m[i] * p[3 * i + 2];
            vc[0] += m[i] * v[3 * i];
            vc[1] += m[i] * v[3 * i + 1];
            vc[2] += m[i] * v[3 * i + 2];
        }

        for (int i = 0; i < n; i++)
        {
            p[3 * i] -= rc[0] / totalM;
            p[3 * i + 1] -= rc[1] / totalM;
            p[3 * i + 2] -= rc[2] / totalM;
            v[3 * i] -= vc[0] / totalM;
            v[3 * i + 1] -= vc[1] / totalM;
            v[3 * i + 2] -= vc[2] / totalM;
        }

        double[] kp1 = new double[3 * n];
        double[] kv1 = new double[3 * n];
        double[] kp2 = new double[3 * n];
        double[] kv2 = new double[3 * n];
        double[] kp3 = new double[3 * n];
        double[] kv3 = new double[3 * n];
        double[] kp4 = new double[3 * n];
        double[] kv4 = new double[3 * n];
        double[] tp = new double[3 * n];
        double[] ta = new double[3 * n];
        double[] worst = new double[n];
        double year = 365d * 86400d;
        double span = spanYears * year;
        double sampleDt = sampleYears * year;
        double t = 0d;
        double nextSample = sampleDt;
        while (t < span - 1e-9d)
        {
            double h = Math.Min(stepSeconds, span - t);
            NBodyRK4Step(p, v, m, g, n, h, kp1, kv1, kp2, kv2, kp3, kv3, kp4, kv4, tp, ta);
            t += h;
            if (t + 1e-9d >= nextSample)
            {
                nextSample += sampleDt;
                bodies[0].EvaluateWorldState(t, out Vector3d sp, out _);
                for (int i = 0; i < n; i++)
                {
                    bodies[i].EvaluateWorldState(t, out Vector3d rp, out _);
                    double dx = (p[3 * i] - p[0]) - (rp.X - sp.X);
                    double dy = (p[3 * i + 1] - p[1]) - (rp.Y - sp.Y);
                    double dz = (p[3 * i + 2] - p[2]) - (rp.Z - sp.Z);
                    double dev = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    if (dev > worst[i]) worst[i] = dev;
                }
            }
        }

        finalPos = new double[3 * n];
        Array.Copy(p, finalPos, 3 * n);
        return worst;
    }

    private static StarSystem SystemWithoutMoon()
    {
        OrbitingBody star = new OrbitingBody { Name = "Вымышленная звезда", StandardGravitationalParameter = 1.327e20d };
        OrbitingBody planetA = new OrbitingBody
        {
            Name = "Планета А",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            SemiMajorAxis = 1.5e11d,
            Eccentricity = 0.02d,
            Parent = star,
        };
        planetA.SyncMassFromGravitationalParameter();
        star.Children.Add(planetA);
        OrbitingBody planetB = new OrbitingBody
        {
            Name = "Планета Б",
            StandardGravitationalParameter = 4.28e13d,
            Radius = 3.39e6d,
            SemiMajorAxis = 2.28e11d,
            Eccentricity = 0.09d,
            InclinationDegrees = 1.9d,
            LongitudeOfAscendingNodeDegrees = 49d,
            ArgumentOfPeriapsisDegrees = 287d,
            MeanAnomalyAtEpochDegrees = 19d,
            Parent = star,
        };
        planetB.SyncMassFromGravitationalParameter();
        star.Children.Add(planetB);
        return new StarSystem(star);
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

    /// <summary>
    /// Категории скорости: slow — длинные прогоны (годы симуляции, сетки,
    /// длинные прожиги/спуски), отобранные по измеренной стоимости, а не
    /// наугад. --fast гоняет всё остальное (&lt;2 мин), --slow — только их.
    /// </summary>
    private static readonly HashSet<string> SlowTests = new HashSet<string>
    {
        "Test21_Kahan",
        "Test25_Lambert",
        "Test28_FiniteBurn",
        "Test29_DescentChain",
        "Test30_YearDrift",
        "Test31_Flyby",
        "Test32_Escape",
        "Test33_Ascent",
        "Test37_LunarYears",
        "Test39_SecularDrift",
        "Test40_EphemerisBake",
        "Test41_LongBakeProbe",
        "Test46_FullBake1024",
        "Test60_TwelveBodyAccuracy"
    };

    private static List<KeyValuePair<string, Func<int>>> TestRegistry()
    {
        return new List<KeyValuePair<string, Func<int>>>
        {
            new KeyValuePair<string, Func<int>>("Test1_WarpIdentity", Test1_WarpIdentity),
            new KeyValuePair<string, Func<int>>("Test2_ControlTickClip", Test2_ControlTickClip),
            new KeyValuePair<string, Func<int>>("Test3_LongWarpStopsAtEvent", Test3_LongWarpStopsAtEvent),
            new KeyValuePair<string, Func<int>>("Test4_SilentCancel", Test4_SilentCancel),
            new KeyValuePair<string, Func<int>>("Test5_Singularity", Test5_Singularity),
            new KeyValuePair<string, Func<int>>("Test6_CacheIdentical", Test6_CacheIdentical),
            new KeyValuePair<string, Func<int>>("Test7_BudgetPriority", Test7_BudgetPriority),
            new KeyValuePair<string, Func<int>>("Test8_PoolNoAllocs", Test8_PoolNoAllocs),
            new KeyValuePair<string, Func<int>>("Test9_Eviction", Test9_Eviction),
            new KeyValuePair<string, Func<int>>("Test10_SleepError", Test10_SleepError),
            new KeyValuePair<string, Func<int>>("Test11_MultiCrossing", Test11_MultiCrossing),
            new KeyValuePair<string, Func<int>>("Test12_Depletion", Test12_Depletion),
            new KeyValuePair<string, Func<int>>("Test13_Tsiolkovsky", Test13_Tsiolkovsky),
            new KeyValuePair<string, Func<int>>("Test14_BurnWarpIdentity", Test14_BurnWarpIdentity),
            new KeyValuePair<string, Func<int>>("Test15_IspAltitude", Test15_IspAltitude),
            new KeyValuePair<string, Func<int>>("Test16_PresetIsParameter", Test16_PresetIsParameter),
            new KeyValuePair<string, Func<int>>("Test18_DenseGate", Test18_DenseGate),
            new KeyValuePair<string, Func<int>>("Test19_AdaptiveGrain", Test19_AdaptiveGrain),
            new KeyValuePair<string, Func<int>>("Test17_FrameBridge", Test17_FrameBridge),
            new KeyValuePair<string, Func<int>>("Test17_Handedness", Test17_Handedness),
            new KeyValuePair<string, Func<int>>("Test20_Kind", Test20_Kind),
            new KeyValuePair<string, Func<int>>("Test20_SurfaceInverse", Test20_SurfaceInverse),
            new KeyValuePair<string, Func<int>>("Test20_Reaction", Test20_Reaction),
            new KeyValuePair<string, Func<int>>("Test20_SurfaceMotion", Test20_SurfaceMotion),
            new KeyValuePair<string, Func<int>>("Test21_Kahan", Test21_Kahan),
            new KeyValuePair<string, Func<int>>("Test22_DragContract", Test22_DragContract),
            new KeyValuePair<string, Func<int>>("Test22_DragDip", Test22_DragDip),
            new KeyValuePair<string, Func<int>>("Test23_Decay", Test23_Decay),
            new KeyValuePair<string, Func<int>>("Test23_Retrograde", Test23_Retrograde),
            new KeyValuePair<string, Func<int>>("Test23_TopCrossing", Test23_TopCrossing),
            new KeyValuePair<string, Func<int>>("Test23_RateBaseline", Test23_RateBaseline),
            new KeyValuePair<string, Func<int>>("Test24_History", Test24_History),
            new KeyValuePair<string, Func<int>>("Test25_Lambert", Test25_Lambert),
            new KeyValuePair<string, Func<int>>("Test26_CircularMatrix", Test26_CircularMatrix),
            new KeyValuePair<string, Func<int>>("Test27_Impulsive", Test27_Impulsive),
            new KeyValuePair<string, Func<int>>("Test28_FiniteBurn", Test28_FiniteBurn),
            new KeyValuePair<string, Func<int>>("Test29_DescentChain", Test29_DescentChain),
            new KeyValuePair<string, Func<int>>("Test29_TerminalVelocity", Test29_TerminalVelocity),
            new KeyValuePair<string, Func<int>>("Test30_YearDrift", Test30_YearDrift),
            new KeyValuePair<string, Func<int>>("Test31_Flyby", Test31_Flyby),
            new KeyValuePair<string, Func<int>>("Test32_Escape", Test32_Escape),
            new KeyValuePair<string, Func<int>>("Test30_PartsTrap", Test30_PartsTrap),
            new KeyValuePair<string, Func<int>>("Test31_AttitudeSpin", Test31_AttitudeSpin),
            new KeyValuePair<string, Func<int>>("Test31_AttitudeTumble", Test31_AttitudeTumble),
            new KeyValuePair<string, Func<int>>("Test31_AttitudePD", Test31_AttitudePD),
            new KeyValuePair<string, Func<int>>("Test31_TorqueLink", Test31_TorqueLink),
            new KeyValuePair<string, Func<int>>("Test33_Ascent", Test33_Ascent),
            new KeyValuePair<string, Func<int>>("Test33_StagingMomentum", Test33_StagingMomentum),
            new KeyValuePair<string, Func<int>>("Test34_GoldenState", Test34_GoldenState),
            new KeyValuePair<string, Func<int>>("Test35_KeplerEdge", Test35_KeplerEdge),
            new KeyValuePair<string, Func<int>>("Test36_HelioDrift", Test36_HelioDrift),
            new KeyValuePair<string, Func<int>>("Test37_LunarYears", Test37_LunarYears),
            new KeyValuePair<string, Func<int>>("Test38_LowDrag", Test38_LowDrag),
            new KeyValuePair<string, Func<int>>("Test39_SecularDrift", Test39_SecularDrift),
            new KeyValuePair<string, Func<int>>("Test40_EphemerisBake", Test40_EphemerisBake),
            new KeyValuePair<string, Func<int>>("Test41_LongBakeProbe", Test41_LongBakeProbe),
            new KeyValuePair<string, Func<int>>("Test42_BakeRule", Test42_BakeRule),
            new KeyValuePair<string, Func<int>>("Test43_BakeStream", Test43_BakeStream),
            new KeyValuePair<string, Func<int>>("Test44_TimeCap", Test44_TimeCap),
            new KeyValuePair<string, Func<int>>("Test45_PorkchopHorizon", Test45_PorkchopHorizon),
            new KeyValuePair<string, Func<int>>("Test46_FullBake1024", Test46_FullBake1024),
            new KeyValuePair<string, Func<int>>("Test60_TwelveBodyAccuracy", Test60_TwelveBodyAccuracy),
            new KeyValuePair<string, Func<int>>("Test52_Centrifugal", Test52_Centrifugal),
            new KeyValuePair<string, Func<int>>("Test53_LambertHalfPlane", Test53_LambertHalfPlane),
            new KeyValuePair<string, Func<int>>("Test54_LambertOvershoot", Test54_LambertOvershoot),
            new KeyValuePair<string, Func<int>>("Test55_ForceAcceptSync", Test55_ForceAcceptSync),
            new KeyValuePair<string, Func<int>>("Test56_ApsisZero", Test56_ApsisZero),
            new KeyValuePair<string, Func<int>>("Test57_RailValidation", Test57_RailValidation),
            new KeyValuePair<string, Func<int>>("Test58_GrainProbe", Test58_GrainProbe),
            new KeyValuePair<string, Func<int>>("Test59_BakeFilesAndSpeed", Test59_BakeFilesAndSpeed),
            new KeyValuePair<string, Func<int>>("Test61_UnbakeableMoon", Test61_UnbakeableMoon),
            new KeyValuePair<string, Func<int>>("Test62_CompressV2", Test62_CompressV2),
            new KeyValuePair<string, Func<int>>("Test63_KeplerContinuation", Test63_KeplerContinuation),
            new KeyValuePair<string, Func<int>>("Test64_HillValidation", Test64_HillValidation),
            new KeyValuePair<string, Func<int>>("Test65_SystemBlueprint", Test65_SystemBlueprint),
            new KeyValuePair<string, Func<int>>("Test66_NanProtocol", Test66_NanProtocol),
            new KeyValuePair<string, Func<int>>("Test67_KeplerPredictorGuards", Test67_KeplerPredictorGuards),
            new KeyValuePair<string, Func<int>>("Test68_StructuralContracts", Test68_StructuralContracts),
            new KeyValuePair<string, Func<int>>("Test69_WarpLadder", Test69_WarpLadder),
            new KeyValuePair<string, Func<int>>("Test70_TryLiftoffSpin", Test70_TryLiftoffSpin),
            new KeyValuePair<string, Func<int>>("Test71_DebrisTunnel", Test71_DebrisTunnel),
            new KeyValuePair<string, Func<int>>("Test72_VisualFrame", Test72_VisualFrame),
            new KeyValuePair<string, Func<int>>("Test74_ScaleBounds", Test74_ScaleBounds),
            new KeyValuePair<string, Func<int>>("Test73_EphemerisThreadGate", Test73_EphemerisThreadGate),
            new KeyValuePair<string, Func<int>>("Test75_TorqueProbeGuard", Test75_TorqueProbeGuard),
            new KeyValuePair<string, Func<int>>("Test76_QuaternionBridge", Test76_QuaternionBridge),
            new KeyValuePair<string, Func<int>>("Test77_TerrainHeightfield", Test77_TerrainHeightfield),
            new KeyValuePair<string, Func<int>>("Test78_JointedBreakup", Test78_JointedBreakup),
            new KeyValuePair<string, Func<int>>("Test79_TidalLock", Test79_TidalLock)
        };
    }

    private static int Test46_FullBake1024()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        StarSystem sys = TestSystem();
        var eph = EphemerisBaker.Bake(sys, 1024d, 900d, 12, Test40_SegRule);
        sw.Stop();
        double bakeSec = sw.Elapsed.TotalSeconds;
        string dir = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t46_" + Guid.NewGuid().ToString("N"));
        EphemerisBaker.ExportFiles(sys, dir);
        long totalBytes = 0L;
        double worstA = 0d;
        double worstM = 0d;
        double worstB = 0d;
        string stat = "";
        foreach (var kv in eph)
        {
            kv.Key.Baked = kv.Value;
        }

        sys.InvalidatePositionCache();
        foreach (var kv in eph)
        {
            string s = kv.Value.ToPortableString();
            totalBytes += s.Length;
            double wj = 0d;
            for (int k = 1; k < kv.Value.SegmentCount; k++)
            {
                double tb = kv.Value.T0Seconds + k * kv.Value.SegmentLengthSeconds;
                kv.Value.TryEvaluate(tb - 1e-3d, out Vector3d p1, out _);
                kv.Value.TryEvaluate(tb + 1e-3d, out Vector3d p2, out _);
                double jp = (p2 - p1).Magnitude;
                if (jp > wj)
                {
                    wj = jp;
                }
            }

            if (kv.Key.Name == "Планета А")
            {
                worstA = wj;
            }
            else if (kv.Key.Name == "Спутник планеты А")
            {
                worstM = wj;
            }
            else if (kv.Key.Name == "Планета Б")
            {
                worstB = wj;
            }

            stat += string.Format("{0}: segs={1} base64={2} jump={3:E2}м; ", kv.Key.Name, kv.Value.SegmentCount, s.Length, wj);
        }

        try
        {
            System.IO.Directory.Delete(dir, true);
        }
        catch (System.IO.IOException)
        {
        }

        Console.WriteLine(string.Format("DBG T46 1024y bakeSec={0:F0} totalBase64={1} [{2}]", bakeSec, totalBytes, stat));
        Check(worstA < 500d && worstM < 50d && worstB < 500d, "T46 full-bake-1024",
            string.Format("1024y за {0:F0}с, base64 {1}: стыки A={2:E2}м M={3:E2}м B={4:E2}м [{5}]", bakeSec, totalBytes, worstA, worstM, worstB, stat));
        return 0;
    }

    private static OrbitingBody T60Body(
        string name, double mu, double radius, double a, double e,
        double incDeg, double lanDeg, double argpDeg, double m0Deg)
    {
        return new OrbitingBody
        {
            Name = name,
            StandardGravitationalParameter = mu,
            Radius = radius,
            SemiMajorAxis = a,
            Eccentricity = e,
            InclinationDegrees = incDeg,
            LongitudeOfAscendingNodeDegrees = lanDeg,
            ArgumentOfPeriapsisDegrees = argpDeg,
            MeanAnomalyAtEpochDegrees = m0Deg,
            EpochTimeSeconds = 0d
        };
    }

    internal static StarSystem TwelveBodySystem()
    {
        // Представительная игровая система: звезда + 5 планет + 6 спутников.
        // Луны ≤ ~0.3 R_H планеты (проградная стабильность) и разнос ≥3 взаимных
        // R_H — иначе система хаотична и рельсы физически нельзя сверить с
        // n-body на больших горизонтах (I-2 на 3e8 = 0.54 R_H разбегался за
        // 8 лет: 6.7e10 м между шагами 900/450, поймано probechaos).
        // Периоды лун 3.9–27.3 сут (все bakeable: период/4 ≥ минимума сегмента);
        // пара I-1/I-2 и пара II-1/II-2 взаимно возмущаются.
        OrbitingBody star = new OrbitingBody { Name = "Звезда", StandardGravitationalParameter = 1.327e20d };

        OrbitingBody pI = T60Body("I", 3.5e14, 2.4e6, 5.8e10, 0.02, 0, 0, 0, 10);
        OrbitingBody pII = T60Body("II", 4.5e14, 3.0e6, 1.05e11, 0.03, 40, 0, 0, 100);
        OrbitingBody pIII = T60Body("III", 4.0e14, 3.4e6, 1.5e11, 0.015, 80, 0, 0, 200);
        OrbitingBody pIV = T60Body("IV", 4.2e13, 2.0e6, 2.3e11, 0.04, 130, 0, 0, 300);
        OrbitingBody pV = T60Body("V", 1.5e14, 2.8e6, 4.5e11, 0.05, 200, 0, 0, 400);
        star.Children.Add(pI);
        star.Children.Add(pII);
        star.Children.Add(pIII);
        star.Children.Add(pIV);
        star.Children.Add(pV);

        OrbitingBody mI1 = T60Body("I-1", 8e11, 9e5, 1.0e8, 0.01, 0, 0, 0, 0);
        OrbitingBody mI2 = T60Body("I-2", 2e11, 7e5, 1.6e8, 0.02, 10, 0, 0, 90);
        pI.Children.Add(mI1);
        pI.Children.Add(mI2);

        OrbitingBody mII1 = T60Body("II-1", 1e12, 1.1e6, 1.2e8, 0.015, 5, 0, 0, 30);
        OrbitingBody mII2 = T60Body("II-2", 3e11, 8e5, 3e8, 0.03, 15, 0, 0, 240);
        pII.Children.Add(mII1);
        pII.Children.Add(mII2);

        OrbitingBody mIII1 = T60Body("III-1", 4.9e12, 1.3e6, 3.84e8, 0.03, 12, 0, 0, 150);
        pIII.Children.Add(mIII1);

        OrbitingBody mV1 = T60Body("V-1", 2e12, 1.0e6, 2e8, 0.02, 25, 0, 0, 60);
        pV.Children.Add(mV1);

        foreach (OrbitingBody b in new[] { pI, pII, pIII, pIV, pV, mI1, mI2, mII1, mII2, mIII1, mV1 })
        {
            b.SyncMassFromGravitationalParameter();
        }

        return new StarSystem(star);
    }

    private static int Test60_TwelveBodyAccuracy()
    {
        // «Насколько точно это моделирует n-body». Позиционный гейт на сотни лет
        // закрыт физикой: система с массивными лунами хаотична (e-folding < 1
        // года для лун — измерено probechaos/probechaos2), два интегратора
        // расходятся экспоненциально при сколь угодно малой разнице шагов и
        // насыщаются на масштабе ~1e5-5e6 м (перигейные биения) за 5-16 лет.
        // Поэтому точность доказывается тремя измеримыми величинами:
        // 1) короткий горизонт: рельсы vs независимый RK4-справочник (шаг 450 с)
        //    на 4 годах (до насыщения хаоса) — ловит любые систематические баги;
        // 2) насыщение хаоса на 16 годах — порог выше измеренного насыщения;
        // 3) энергосохранение рельс на 1024 года (65 срезов через 16 лет):
        //    дрейф означал бы секулярную порчу орбит (спираль/убегание);
        // 4) файлы/скорость: оценка байтов == факт, макс-файл под лимитом git.
        StarSystem sys = TwelveBodySystem();
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };

        double[][] refSamples = null;
        double[] finalPos = null;
        double bakeSec = 0;
        double refSec = 0;
        Dictionary<OrbitingBody, BakedEphemeris> eph = null;

        System.Threading.Tasks.Parallel.Invoke(
            () =>
            {
                var swB = System.Diagnostics.Stopwatch.StartNew();
                eph = EphemerisBaker.Bake(sys, 1024d, config);
                swB.Stop();
                bakeSec = swB.Elapsed.TotalSeconds;
            },
            () =>
            {
                var swR = System.Diagnostics.Stopwatch.StartNew();
                refSamples = NBodyReference(sys, 16d, 450d, 1d, out finalPos);
                swR.Stop();
                refSec = swR.Elapsed.TotalSeconds;
            });

        var estimate = EphemerisBaker.EstimateBakeBytes(sys, 1024d, config);
        long totalBytes = 0;
        long maxFile = 0;
        foreach (var kv in estimate)
        {
            totalBytes += kv.Value;
            if (kv.Value > maxFile)
            {
                maxFile = kv.Value;
            }
        }

        foreach (var kv in eph)
        {
            kv.Key.Baked = kv.Value;
        }

        sys.InvalidatePositionCache();

        // 1) Короткий горизонт: рельсы (относительно звезды) vs сэмплы справочника
        // в те же моменты (k·1 год, k=1..16).
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        double[] worst = new double[n];
        double worst4y = 0d;
        for (int k = 0; k < refSamples.Length; k++)
        {
            double t = (k + 1) * 1d * 365d * 86400d;
            bodies[0].EvaluateWorldState(t, out Vector3d sp, out _);
            for (int i = 1; i < n; i++)
            {
                bodies[i].EvaluateWorldState(t, out Vector3d rp, out _);
                double dx = (rp.X - sp.X) - refSamples[k][3 * i];
                double dy = (rp.Y - sp.Y) - refSamples[k][3 * i + 1];
                double dz = (rp.Z - sp.Z) - refSamples[k][3 * i + 2];
                double dev = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (dev > worst[i])
                {
                    worst[i] = dev;
                    if (t <= 4d * 365d * 86400d && dev > worst4y)
                    {
                        worst4y = dev;
                    }
                }
            }
        }

        string detail = "";
        double worstAll = 0d;
        for (int i = 1; i < n; i++)
        {
            OrbitingBody b = bodies[i];
            detail += string.Format("{0}={1:E1}м ", b.Name, worst[i]);
            if (worst[i] > worstAll)
            {
                worstAll = worst[i];
            }
        }

        // 2) Энергосохранение рельс на 1024 года (65 срезов через 16 лет).
        // E = Σ½m|v|² − Σ_{i<j} G·mᵢmⱼ/rᵢⱼ по абсолютным состояниям из рельс.
        double gConst = PhysicsSolver.GravitationalConstant;
        double year = 365d * 86400d;
        double e0 = 0d;
        double worstEnergy = 0d;
        var pos = new Vector3d[n];
        var vel = new Vector3d[n];
        for (int k = 0; k <= 64; k++)
        {
            double t = k * 16d * year;
            for (int i = 0; i < n; i++)
            {
                bodies[i].EvaluateWorldState(t, out pos[i], out vel[i]);
            }

            double e = 0d;
            for (int i = 0; i < n; i++)
            {
                double mI = bodies[i].Mass;
                e += 0.5d * mI * (vel[i].X * vel[i].X + vel[i].Y * vel[i].Y + vel[i].Z * vel[i].Z);
                for (int j = i + 1; j < n; j++)
                {
                    double dx = pos[i].X - pos[j].X;
                    double dy = pos[i].Y - pos[j].Y;
                    double dz = pos[i].Z - pos[j].Z;
                    e -= gConst * mI * bodies[j].Mass / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                }
            }

            if (k == 0)
            {
                e0 = e;
            }
            else
            {
                double drift = Math.Abs(e - e0) / Math.Abs(e0);
                if (drift > worstEnergy)
                {
                    worstEnergy = drift;
                }
            }
        }

        // Гейты. Хаос системы с массивными лунами измерен (probechaos/2):
        // III-1 расходится с любым независимым решением экспоненциально
        // (e-folding < 1 года) и насыщается на ~1e5-5e6 м (перигейные биения)
        // — рельсы против справочника на ТОМ ЖЕ шаге 900 с дают тот же
        // профиль, что и против 450/225, т.е. это физика, а не баг пекаря.
        // Гейт 1 (4 года): до насыщения хаоса отклонение ~1e4 м (I-1);
        // линейный баг-сдвиг (скорость 1 м/с) дал бы 1.3e8 м — порог 1e5 м
        // ловит его с запасом 1000× и остаётся в 3 порядках от масштаба орбиты.
        bool shortOk = worst4y < 1e5d;
        // Гейт 2 (16 лет): насыщение хаоса; порог 1e7 м = 2.6% орбиты луны.
        bool chaosOk = worstAll < 1e7d;
        // Гейт 3: дрейф энергии отсутствует на 1024 годах (полоса осцилляций).
        bool energyOk = worstEnergy < 1e-4d;
        // Гейт 4: макс-файл под лимитом GitHub 100 МБ.
        bool filesOk = maxFile < 95d * 1024d * 1024d;

        Check(shortOk && chaosOk && energyOk && filesOk, "T60 twelve-body-1024y",
            string.Format("12 тел: бейк 1024y {0:F0}с ∥ справочник 16y {1:F0}с (2 ядра), файлы {2:E1}МБ (макс {3:E1}МБ); худшее 4y: {4:E1}м (<1e5); худшее 16y (насыщение хаоса): {5:E1}м (<1e7); дрейф энергии 1024y: {6:E2} (<1e-4); файлы<100МБ: {7} — {8}",
                bakeSec, refSec, totalBytes / 1048576.0, maxFile / 1048576.0, worst4y, worstAll, worstEnergy, filesOk, detail));
        return 0;
    }

    private static int Test45_PorkchopHorizon()
    {
        StarSystem bakedSys = TestSystem();
        EphemerisBaker.BakeAndAttach(bakedSys, 60d / 365d, 900d, 12, Test40_SegRule);
        double end = bakedSys.BakedEndSeconds();
        OrbitingBody star = bakedSys.AllBodies[0];
        OrbitingBody planetA = bakedSys.AllBodies[1];
        OrbitingBody planetB = bakedSys.AllBodies[3];
        bool noThrow = true;
        int beyondTotal = 0;
        // Контракт P1: за концом рельсы мир живёт (кеплерово продолжение) —
        // клетки зашипшот-горизонтом теперь ВАЛИДНЫ: позиции планет конечны
        // (продолжение бесшовно), Ламберт против них согласован с миром, в
        // котором полетит корабль. Проверяем конечность Δv валидных клеток.
        bool beyondSane = true;
        try
        {
            PorkchopCell[,] table = Porkchop.Scan(bakedSys, star, planetA, planetB,
                0d, 10d * 86400d, 4, 55d * 86400d, 70d * 86400d, 4);
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    PorkchopCell c = table[i, j];
                    if (c.DepartTimeSeconds > end || c.ArriveTimeSeconds > end)
                    {
                        beyondTotal++;
                        if (c.Valid && (double.IsNaN(c.DeltaV) || double.IsInfinity(c.DeltaV)))
                        {
                            beyondSane = false;
                        }
                    }

                    if (c.Valid && (double.IsNaN(c.DeltaV) || double.IsInfinity(c.DeltaV)))
                    {
                        beyondSane = false;
                    }
                }
            }

            if (Porkchop.TryBest(table, out int bi, out int bj))
            {
                PorkchopCell c = table[bi, bj];
                if (!c.Valid)
                {
                    beyondSane = false;
                }
            }

            PorkchopCell[,] table2 = Porkchop.Scan(bakedSys, star, planetA, planetB,
                65d * 86400d, 70d * 86400d, 2, 80d * 86400d, 90d * 86400d, 2);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    beyondTotal++;
                    if (table2[i, j].Valid && (double.IsNaN(table2[i, j].DeltaV) || double.IsInfinity(table2[i, j].DeltaV)))
                    {
                        beyondSane = false;
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
            noThrow = false;
        }

        PorkchopCell[,] inside = Porkchop.Scan(bakedSys, star, planetA, planetB,
            5d * 86400d, 10d * 86400d, 3, 40d * 86400d, 50d * 86400d, 3);
        int validInside = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (inside[i, j].Valid)
                {
                    validInside++;
                }
            }
        }

        StarSystem corruptSys = TestSystem();
        var bad = new BakedEphemeris();
        bad.T0Seconds = 0d;
        bad.SegmentLengthSeconds = 8d * 86400d;
        bad.Degree = 12;
        bad.SegmentCount = 100;
        bad.FilePath = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t45_missing.bin");
        corruptSys.AllBodies[1].Baked = bad;
        bool loud = false;
        try
        {
            Porkchop.Scan(corruptSys, corruptSys.AllBodies[0], corruptSys.AllBodies[1], corruptSys.AllBodies[3],
                0d, 1d * 86400d, 1, 2d * 86400d, 3d * 86400d, 1);
        }
        catch (InvalidOperationException)
        {
            loud = true;
        }

        Check(noThrow && beyondSane && beyondTotal > 0 && validInside > 0 && loud, "T45 porkchop-horizon",
            string.Format("скан за горизонт {0:F0}с не бросил: {1}; клеток за горизонтом {2}, все разумны (продолжение): {3}; валидных внутри (окно T27): {4}; битый файл громко: {5}",
                end, noThrow, beyondTotal, beyondSane, validInside, loud));
        return 0;
    }

    private static int Test44_TimeCap()
    {
        StarSystem sys = TestSystem();
        EphemerisBaker.BakeAndAttach(sys, 30d / 365d, 900d, 12, Test40_SegRule);
        double end = sys.BakedEndSeconds();
        // Контракт P1: за концом рельсы мир живёт — кеплерово продолжение,
        // конечные состояние. Громко остаётся только ДО T0.
        bool beyondAlive = false;
        bool pastLoud = false;
        try
        {
            sys.EvaluateBodyState(sys.AllBodies[1], end + 1d, out Vector3d pB, out Vector3d vB);
            beyondAlive = pB.IsFinite && vB.IsFinite;
        }
        catch (InvalidOperationException)
        {
            beyondAlive = false;
        }

        try
        {
            sys.EvaluateBodyState(sys.AllBodies[1], -1d, out _, out _);
        }
        catch (InvalidOperationException)
        {
            pastLoud = true;
        }

        sys.EvaluateBodyState(sys.AllBodies[1], 0d, out Vector3d pp, out Vector3d pv);
        SpacecraftPhysics phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        var prop = new EventDrivenPropagator(phys);
        prop.HardHorizonSeconds = end;
        var driver = new LongWarpDriver(phys, prop);
        var ship = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(5300d, 0d, 0d), 1000d);
        LongWarpResult r = driver.AdvanceToTarget(ship, 0d, 60d * 86400d);
        bool stopped = !r.ReachedTarget && r.StoppingEvent.HasValue
            && r.StoppingEvent.Value.Kind == EventKind.EphemerisEnd
            && r.StoppingEvent.Value.DetectorName == "EphemerisEnd"
            && r.StoppingEvent.Value.TimeSeconds == end
            && r.StoppingEvent.Value.State.Position.IsFinite;
        bool reactOk = false;
        if (stopped)
        {
            var react = EventReactions.ApplyEphemerisEnd(r.StoppingEvent.Value);
            reactOk = react.TimeSeconds == end;
        }

        var ship2 = new Spacecraft(pp + new Vector3d(0d, 8e6d, 0d), pv + new Vector3d(5300d, 0d, 0d), 1000d);
        LongWarpResult r2 = driver.AdvanceToTarget(ship2, 0d, 10d * 86400d);
        bool transparent = r2.ReachedTarget && !r2.StoppingEvent.HasValue;
        bool defaultInf = new EventDrivenPropagator(phys).HardHorizonSeconds == double.PositiveInfinity;
        OrbitingBody planet = sys.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bp0, out Vector3d bv0);
        Vector3d fallP = bp0 + new Vector3d(0d, planet.Radius + 200000d, 0d);
        Vector3d fallV = bv0 + new Vector3d(-3000d, 0d, 0d);
        SpacecraftPhysics physF = new SpacecraftPhysics();
        physF.Sources.Add(new CachedGravitySource(sys));
        var propU = new EventDrivenPropagator(physF);
        propU.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
        var shipU = new Spacecraft(fallP, fallV, 1000d);
        EventOccurrence? tdU = propU.Propagate(shipU, 0d, 30d * 86400d);
        bool hasTd = tdU.HasValue && tdU.Value.Kind == EventKind.Touchdown;
        double ttd = hasTd ? tdU.Value.TimeSeconds : double.NaN;
        bool tieWins = false;
        bool preempts = false;
        if (hasTd)
        {
            var propT = new EventDrivenPropagator(physF);
            propT.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
            propT.HardHorizonSeconds = ttd;
            var shipT = new Spacecraft(fallP, fallV, 1000d);
            EventOccurrence? tdT = propT.Propagate(shipT, 0d, 30d * 86400d);
            tieWins = tdT.HasValue && tdT.Value.Kind == EventKind.Touchdown;
            var propC = new EventDrivenPropagator(physF);
            propC.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(planet));
            propC.HardHorizonSeconds = ttd - 1d;
            var shipC = new Spacecraft(fallP, fallV, 1000d);
            EventOccurrence? tdC = propC.Propagate(shipC, 0d, 30d * 86400d);
            preempts = tdC.HasValue && tdC.Value.Kind == EventKind.EphemerisEnd && tdC.Value.TimeSeconds == ttd - 1d;
        }

        Check(beyondAlive && pastLoud && stopped && reactOk && transparent && defaultInf && hasTd && tieWins && preempts, "T44 time-cap",
            string.Format("за концом живём: {0}; до T0 громко: {1}; стоп на горизонте {2:F0}с: {3}; реакция: {4}; прозрачен внутри: {5}; дефолт inf: {6}; touchdown {7:F1}с: tie->Touchdown {8}, cap-1с->EphemerisEnd {9}",
                beyondAlive, pastLoud, end, stopped, reactOk, transparent, defaultInf, ttd, tieWins, preempts));
        return 0;
    }

    private static int Test43_BakeStream()
    {
        StarSystem memSys = TestSystem();
        EphemerisBaker.BakeAndAttach(memSys, 30d / 365d, 900d, 12, Test40_SegRule);
        string dir = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t43_" + Guid.NewGuid().ToString("N"));
        EphemerisBaker.ExportFiles(memSys, dir);
        bool manifest = System.IO.File.Exists(System.IO.Path.Combine(dir, "manifest.txt"));
        StarSystem fileSys = TestSystem();
        EphemerisBaker.AttachFiles(fileSys, dir);
        int n = memSys.AllBodies.Count;
        double span = 30d * 86400d;
        bool identical = true;
        for (int k = 0; k < 1000; k++)
        {
            double t = span * k / 999d;
            for (int i = 0; i < n; i++)
            {
                memSys.EvaluateBodyState(memSys.AllBodies[i], t, out Vector3d p1, out Vector3d v1);
                fileSys.EvaluateBodyState(fileSys.AllBodies[i], t, out Vector3d p2, out Vector3d v2);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z || v1.X != v2.X || v1.Y != v2.Y || v1.Z != v2.Z)
                {
                    identical = false;
                }
            }
        }

        long loadsAfterScan = 0L;
        long totalSegs = 0L;
        for (int i = 0; i < n; i++)
        {
            BakedEphemeris e = fileSys.AllBodies[i].Baked;
            if (e != null)
            {
                loadsAfterScan += e.WindowLoads;
                totalSegs += e.SegmentCount;
            }
        }

        for (int k = 0; k < 20; k++)
        {
            double t = (k % 2 == 0) ? span : 0d;
            for (int i = 0; i < n; i++)
            {
                fileSys.EvaluateBodyState(fileSys.AllBodies[i], t, out Vector3d p1, out _);
                memSys.EvaluateBodyState(memSys.AllBodies[i], t, out Vector3d p2, out _);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z)
                {
                    identical = false;
                }
            }
        }

        // Контракт P1: до T0 — громко; ЗА концом — кеплерово продолжение
        // (позиция конечна, без исключения). Проверяем оба направления.
        bool outside = true;
        for (int i = 0; i < n && outside; i++)
        {
            if (fileSys.AllBodies[i].Baked == null)
            {
                continue;
            }

            try
            {
                fileSys.EvaluateBodyState(fileSys.AllBodies[i], -1d, out _, out _);
                outside = false;
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                fileSys.EvaluateBodyState(fileSys.AllBodies[i], span + 86400d, out Vector3d pB, out Vector3d vB);
                if (!pB.IsFinite || !vB.IsFinite)
                {
                    outside = false;
                }
            }
            catch (InvalidOperationException)
            {
                outside = false;
            }
        }

        bool bulk = true;
        BakedEphemeris src = fileSys.AllBodies[2].Baked;
        BakedEphemeris mem = src.LoadRangeToMemory(0d, span);
        if (mem == null || mem.Coeffs == null)
        {
            bulk = false;
        }
        else
        {
            for (int k = 0; k < 200; k++)
            {
                double t = span * k / 199d;
                src.TryEvaluate(t, out Vector3d p1, out Vector3d v1);
                mem.TryEvaluate(t, out Vector3d p2, out Vector3d v2);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z || v1.X != v2.X || v1.Y != v2.Y || v1.Z != v2.Z)
                {
                    bulk = false;
                    break;
                }
            }
        }

        try
        {
            System.IO.Directory.Delete(dir, true);
        }
        catch (System.IO.IOException)
        {
        }

        Check(manifest && identical && loadsAfterScan <= totalSegs + 8L && outside && bulk, "T43 bake-stream",
            string.Format("файлы+манифест: {0}; стрим==память 1000 точек бит-в-бит: {1}; загрузок окон {2} (сегментов {3}); эвикшен идентичен: {1}; прошлое громко/будущее продолжение: {4}; bulk: {5}",
                manifest, identical, loadsAfterScan, totalSegs, outside, bulk));
        return 0;
    }

    private static int Test42_BakeRule()
    {
        StarSystem sys = TestSystem();
        double moonSeg = EphemerisBaker.DefaultSegmentLengthSeconds(sys.AllBodies[2]);
        double planetASeg = EphemerisBaker.DefaultSegmentLengthSeconds(sys.AllBodies[1]);
        double planetBSeg = EphemerisBaker.DefaultSegmentLengthSeconds(sys.AllBodies[3]);
        EphemerisBaker.BakeAndAttach(sys, 30d / 365d, 900d, 12, null);
        sys.EvaluateBodyState(sys.AllBodies[1], 15d * 86400d, out Vector3d p, out Vector3d v);
        bool attached = sys.AllBodies[1].Baked != null && sys.AllBodies[2].Baked != null && sys.AllBodies[3].Baked != null;
        bool finite = p.IsFinite && v.IsFinite;
        Check(moonSeg > 5d * 86400d && moonSeg < 10d * 86400d
            && planetASeg > 5d * 86400d && planetASeg < 10d * 86400d
            && planetBSeg == 64d * 86400d && attached && finite, "T42 bake-rule",
            string.Format("сегменты: луна={0:F2}д планетаА={1:F2}д планетаБ={2:F2}д; attach={3} finite={4}",
                moonSeg / 86400d, planetASeg / 86400d, planetBSeg / 86400d, attached, finite));
        return 0;
    }

    private static int Test41_LongBakeProbe()
    {
        StarSystem sys = TestSystem();
        var eph = EphemerisBaker.Bake(sys, 200d, 900d, 12, Test40_SegRule);
        long totalBytes = 0L;
        string stat = "";
        foreach (var kv in eph)
        {
            string s = kv.Value.ToPortableString();
            totalBytes += s.Length;
            double worstJumpP = 0d;
            double worstJumpV = 0d;
            for (int k = 1; k < kv.Value.SegmentCount; k++)
            {
                double tb = kv.Value.T0Seconds + k * kv.Value.SegmentLengthSeconds;
                kv.Value.TryEvaluate(tb - 1e-3d, out Vector3d p1, out Vector3d v1);
                kv.Value.TryEvaluate(tb + 1e-3d, out Vector3d p2, out Vector3d v2);
                double jp = (p2 - p1).Magnitude;
                double jv = (v2 - v1).Magnitude;
                if (jp > worstJumpP)
                {
                    worstJumpP = jp;
                }

                if (jv > worstJumpV)
                {
                    worstJumpV = jv;
                }
            }

            kv.Value.TryEvaluate(0d, out Vector3d p0, out _);
            kv.Value.TryEvaluate(kv.Value.EndSeconds, out Vector3d p1e, out _);
            stat += string.Format("{0}: segs={1} base64={2} jumpP={3:E2}м jumpV={4:E2}м/с; ", kv.Key.Name, kv.Value.SegmentCount, s.Length, worstJumpP, worstJumpV);
        }

        double jumpA = 0d;
        double jumpM = 0d;
        double jumpB = 0d;
        foreach (var kv in eph)
        {
            double wj = 0d;
            for (int k = 1; k < kv.Value.SegmentCount; k++)
            {
                double tb = kv.Value.T0Seconds + k * kv.Value.SegmentLengthSeconds;
                kv.Value.TryEvaluate(tb - 1e-3d, out Vector3d p1, out _);
                kv.Value.TryEvaluate(tb + 1e-3d, out Vector3d p2, out _);
                double jp = (p2 - p1).Magnitude;
                if (jp > wj)
                {
                    wj = jp;
                }
            }

            if (kv.Key.Name == "Планета А")
            {
                jumpA = wj;
            }
            else if (kv.Key.Name == "Спутник планеты А")
            {
                jumpM = wj;
            }
            else if (kv.Key.Name == "Планета Б")
            {
                jumpB = wj;
            }
        }

        Console.WriteLine(string.Format("DBG T41 200y totalBase64={0} [{1}]", totalBytes, stat));
        Check(totalBytes > 0L && jumpA < 500d && jumpM < 50d && jumpB < 500d, "T41 long-bake-probe",
            string.Format("200y bake: totalBase64={0} стыки A={1:E2}м(<500) M={2:E2}м(<50) B={3:E2}м(<500) [{4}]", totalBytes, jumpA, jumpM, jumpB, stat));
        return 0;
    }

    private static double Test40_SegRule(OrbitingBody b)
    {
        if (b.Children.Count > 0)
        {
            return 8d * 86400d;
        }

        if (b.Parent != null && b.Parent.Parent != null)
        {
            return 8d * 86400d;
        }

        return 32d * 86400d;
    }

    private static void Test40_Truth(StarSystem sys, double spanYears, double stepSeconds, double sampleSeconds, out double[] outT, out Vector3d[] outP, out Vector3d[] outV)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        double g = PhysicsSolver.GravitationalConstant;
        double year = 365d * 86400d;
        double span = spanYears * year;
        double[] m = new double[n];
        double[] p = new double[3 * n];
        double[] v = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
            m[i] = bodies[i].ResolveStandardGravitationalParameter() / g;
            p[3 * i] = bp.X;
            p[3 * i + 1] = bp.Y;
            p[3 * i + 2] = bp.Z;
            v[3 * i] = bv.X;
            v[3 * i + 1] = bv.Y;
            v[3 * i + 2] = bv.Z;
        }

        for (int i = n - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.Children.Count == 0 || m[i] <= 0d)
            {
                continue;
            }

            double sx = 0d;
            double sy = 0d;
            double sz = 0d;
            double svx = 0d;
            double svy = 0d;
            double svz = 0d;
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = -1;
                for (int k = 0; k < n; k++)
                {
                    if (ReferenceEquals(bodies[k], b.Children[j]))
                    {
                        c = k;
                        break;
                    }
                }

                if (c < 0)
                {
                    continue;
                }

                double mc = m[c];
                for (int q = 0; q < bodies[c].Children.Count; q++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (ReferenceEquals(bodies[k], bodies[c].Children[q]))
                        {
                            mc += m[k];
                        }
                    }
                }

                sx += mc * (p[3 * c] - p[3 * i]);
                sy += mc * (p[3 * c + 1] - p[3 * i + 1]);
                sz += mc * (p[3 * c + 2] - p[3 * i + 2]);
                svx += mc * (v[3 * c] - v[3 * i]);
                svy += mc * (v[3 * c + 1] - v[3 * i + 1]);
                svz += mc * (v[3 * c + 2] - v[3 * i + 2]);
            }

            p[3 * i] -= sx / m[i];
            p[3 * i + 1] -= sy / m[i];
            p[3 * i + 2] -= sz / m[i];
            v[3 * i] -= svx / m[i];
            v[3 * i + 1] -= svy / m[i];
            v[3 * i + 2] -= svz / m[i];
        }

        double totalM = 0d;
        double[] rc = new double[3];
        double[] vc = new double[3];
        for (int i = 0; i < n; i++)
        {
            totalM += m[i];
            rc[0] += m[i] * p[3 * i];
            rc[1] += m[i] * p[3 * i + 1];
            rc[2] += m[i] * p[3 * i + 2];
            vc[0] += m[i] * v[3 * i];
            vc[1] += m[i] * v[3 * i + 1];
            vc[2] += m[i] * v[3 * i + 2];
        }

        for (int i = 0; i < n; i++)
        {
            p[3 * i] -= rc[0] / totalM;
            p[3 * i + 1] -= rc[1] / totalM;
            p[3 * i + 2] -= rc[2] / totalM;
            v[3 * i] -= vc[0] / totalM;
            v[3 * i + 1] -= vc[1] / totalM;
            v[3 * i + 2] -= vc[2] / totalM;
        }

        double[] kp1 = new double[3 * n];
        double[] kv1 = new double[3 * n];
        double[] kp2 = new double[3 * n];
        double[] kv2 = new double[3 * n];
        double[] kp3 = new double[3 * n];
        double[] kv3 = new double[3 * n];
        double[] kp4 = new double[3 * n];
        double[] kv4 = new double[3 * n];
        double[] tp = new double[3 * n];
        double[] ta = new double[3 * n];
        var tl = new List<double>();
        var pl = new List<Vector3d>();
        var vl = new List<Vector3d>();
        double t = 0d;
        double next = 0d;
        while (t < span - 1e-9d)
        {
            double h = Math.Min(stepSeconds, span - t);
            for (int pass = 0; pass < 1; pass++)
            {
                for (int k = 0; k < 3 * n; k++)
                {
                    ta[k] = 0d;
                }

                for (int a = 0; a < n; a++)
                {
                    for (int b2 = a + 1; b2 < n; b2++)
                    {
                        double dx = p[3 * b2] - p[3 * a];
                        double dy = p[3 * b2 + 1] - p[3 * a + 1];
                        double dz = p[3 * b2 + 2] - p[3 * a + 2];
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double s = g / (r2 * Math.Sqrt(r2));
                        double fi = s * m[b2];
                        double fj = s * m[a];
                        ta[3 * a] += fi * dx;
                        ta[3 * a + 1] += fi * dy;
                        ta[3 * a + 2] += fi * dz;
                        ta[3 * b2] -= fj * dx;
                        ta[3 * b2 + 1] -= fj * dy;
                        ta[3 * b2 + 2] -= fj * dz;
                    }
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    kp1[k] = v[k];
                    kv1[k] = ta[k];
                    tp[k] = p[k] + (0.5d * h * kp1[k]);
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    ta[k] = 0d;
                }

                for (int a = 0; a < n; a++)
                {
                    for (int b2 = a + 1; b2 < n; b2++)
                    {
                        double dx = tp[3 * b2] - tp[3 * a];
                        double dy = tp[3 * b2 + 1] - tp[3 * a + 1];
                        double dz = tp[3 * b2 + 2] - tp[3 * a + 2];
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double s = g / (r2 * Math.Sqrt(r2));
                        double fi = s * m[b2];
                        double fj = s * m[a];
                        ta[3 * a] += fi * dx;
                        ta[3 * a + 1] += fi * dy;
                        ta[3 * a + 2] += fi * dz;
                        ta[3 * b2] -= fj * dx;
                        ta[3 * b2 + 1] -= fj * dy;
                        ta[3 * b2 + 2] -= fj * dz;
                    }
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    kp2[k] = v[k] + (0.5d * h * kv1[k]);
                    kv2[k] = ta[k];
                    tp[k] = p[k] + (0.5d * h * kp2[k]);
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    ta[k] = 0d;
                }

                for (int a = 0; a < n; a++)
                {
                    for (int b2 = a + 1; b2 < n; b2++)
                    {
                        double dx = tp[3 * b2] - tp[3 * a];
                        double dy = tp[3 * b2 + 1] - tp[3 * a + 1];
                        double dz = tp[3 * b2 + 2] - tp[3 * a + 2];
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double s = g / (r2 * Math.Sqrt(r2));
                        double fi = s * m[b2];
                        double fj = s * m[a];
                        ta[3 * a] += fi * dx;
                        ta[3 * a + 1] += fi * dy;
                        ta[3 * a + 2] += fi * dz;
                        ta[3 * b2] -= fj * dx;
                        ta[3 * b2 + 1] -= fj * dy;
                        ta[3 * b2 + 2] -= fj * dz;
                    }
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    kp3[k] = v[k] + (0.5d * h * kv2[k]);
                    kv3[k] = ta[k];
                    tp[k] = p[k] + (h * kp3[k]);
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    ta[k] = 0d;
                }

                for (int a = 0; a < n; a++)
                {
                    for (int b2 = a + 1; b2 < n; b2++)
                    {
                        double dx = tp[3 * b2] - tp[3 * a];
                        double dy = tp[3 * b2 + 1] - tp[3 * a + 1];
                        double dz = tp[3 * b2 + 2] - tp[3 * a + 2];
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double s = g / (r2 * Math.Sqrt(r2));
                        double fi = s * m[b2];
                        double fj = s * m[a];
                        ta[3 * a] += fi * dx;
                        ta[3 * a + 1] += fi * dy;
                        ta[3 * a + 2] += fi * dz;
                        ta[3 * b2] -= fj * dx;
                        ta[3 * b2 + 1] -= fj * dy;
                        ta[3 * b2 + 2] -= fj * dz;
                    }
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    kp4[k] = v[k] + (h * kv3[k]);
                    kv4[k] = ta[k];
                }

                for (int k = 0; k < 3 * n; k++)
                {
                    p[k] += (h / 6d) * ((kp1[k] + kp4[k]) + (2d * (kp2[k] + kp3[k])));
                    v[k] += (h / 6d) * ((kv1[k] + kv4[k]) + (2d * (kv2[k] + kv3[k])));
                }
            }

            t += h;
            if (t + 1e-9d >= next)
            {
                next += sampleSeconds;
                tl.Add(t);
                for (int i = 0; i < n; i++)
                {
                    pl.Add(new Vector3d(p[3 * i], p[3 * i + 1], p[3 * i + 2]));
                    vl.Add(new Vector3d(v[3 * i], v[3 * i + 1], v[3 * i + 2]));
                }
            }
        }

        outT = tl.ToArray();
        outP = pl.ToArray();
        outV = vl.ToArray();
    }

    private static int Test40_EphemerisBake()
    {
        StarSystem bakedSys = TestSystem();
        var eph = EphemerisBaker.Bake(bakedSys, 2d, 900d, 12, Test40_SegRule);
        foreach (var kv in eph)
        {
            kv.Key.Baked = kv.Value;
        }

        bakedSys.InvalidatePositionCache();
        Test40_Truth(TestSystem(), 2d, 450d, 86400d, out double[] tt, out Vector3d[] tp2, out Vector3d[] tv2);
        int n = bakedSys.AllBodies.Count;
        int ns = tt.Length;
        double[] worstP = new double[n];
        double[] worstV = new double[n];
        for (int s = 0; s < ns; s++)
        {
            double t = tt[s];
            for (int i = 0; i < n; i++)
            {
                bakedSys.EvaluateBodyState(bakedSys.AllBodies[i], t, out Vector3d bp, out Vector3d bv);
                bakedSys.EvaluateBodyState(bakedSys.AllBodies[0], t, out Vector3d bs, out Vector3d bsv);
                Vector3d relP = bp - bs;
                Vector3d relV = bv - bsv;
                Vector3d tP = tp2[s * n + i] - tp2[s * n];
                Vector3d tV = tv2[s * n + i] - tv2[s * n];
                double dp = (relP - tP).Magnitude;
                double dv = (relV - tV).Magnitude;
                if (dp > worstP[i])
                {
                    worstP[i] = dp;
                }

                if (dv > worstV[i])
                {
                    worstV[i] = dv;
                }
            }
        }

        StarSystem bakedSys2 = TestSystem();
        var eph2 = EphemerisBaker.Bake(bakedSys2, 2d, 900d, 12, Test40_SegRule);
        bool det = eph.Count == eph2.Count;
        for (int i = 0; i < n && det; i++)
        {
            OrbitingBody b1 = bakedSys.AllBodies[i];
            OrbitingBody b2 = bakedSys2.AllBodies[i];
            if (!eph.ContainsKey(b1) || !eph2.ContainsKey(b2))
            {
                continue;
            }

            double[] a = eph[b1].Coeffs;
            double[] b = eph2[b2].Coeffs;
            if (a.Length != b.Length)
            {
                det = false;
                break;
            }

            for (int k = 0; k < a.Length; k++)
            {
                if (a[k] != b[k])
                {
                    det = false;
                    break;
                }
            }
        }

        StarSystem plainSys = TestSystem();
        // Контракт P1: ДО T0 — громко; ЗА концом — кеплерово продолжение
        // (конечные состояние). T40-бейк на 2 года, t=3 года — за концом.
        bool fb = true;
        for (int i = 0; i < n && fb; i++)
        {
            if (bakedSys.AllBodies[i].Baked == null)
            {
                continue;
            }

            try
            {
                bakedSys.EvaluateBodyState(bakedSys.AllBodies[i], -1000000d, out _, out _);
                fb = false;
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                bakedSys.EvaluateBodyState(bakedSys.AllBodies[i], 3d * 365d * 86400d, out Vector3d pB, out Vector3d vB);
                if (!pB.IsFinite || !vB.IsFinite)
                {
                    fb = false;
                }
            }
            catch (InvalidOperationException)
            {
                fb = false;
            }
        }

        // Тела без рельсы (звезда) не изменились: кеплеров путь у bakedSys
        // совпадает с чистой системой в те же моменты.
        StarSystem plainSys2 = TestSystem();
        foreach (double t in new double[] { -1000000d, 3d * 365d * 86400d })
        {
            for (int i = 0; i < n; i++)
            {
                if (bakedSys.AllBodies[i].Baked != null)
                {
                    continue;
                }

                bakedSys.EvaluateBodyState(bakedSys.AllBodies[i], t, out Vector3d bp, out Vector3d bv);
                plainSys2.EvaluateBodyState(plainSys2.AllBodies[i], t, out Vector3d pp, out Vector3d pv);
                if (bp.X != pp.X || bp.Y != pp.Y || bp.Z != pp.Z || bv.X != pv.X || bv.Y != pv.Y || bv.Z != pv.Z)
                {
                    fb = false;
                }
            }
        }

        bool rt = true;
        foreach (var kv in eph)
        {
            string s = kv.Value.ToPortableString();
            BakedEphemeris back = BakedEphemeris.FromPortableString(s);
            if (back == null || back.Coeffs.Length != kv.Value.Coeffs.Length)
            {
                rt = false;
                break;
            }

            for (int k = 0; k < 200; k++)
            {
                double t = kv.Value.T0Seconds + (kv.Value.EndSeconds - kv.Value.T0Seconds) * k / 199d;
                kv.Value.TryEvaluate(t, out Vector3d p1, out Vector3d v1);
                back.TryEvaluate(t, out Vector3d p2, out Vector3d v2);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z || v1.X != v2.X || v1.Y != v2.Y || v1.Z != v2.Z)
                {
                    rt = false;
                    break;
                }
            }

            if (!rt)
            {
                break;
            }
        }

        string perBody = "";
        for (int i = 0; i < n; i++)
        {
            perBody += string.Format("{0} p={1:E2}м v={2:E2}м/с; ", bakedSys.AllBodies[i].Name, worstP[i], worstV[i]);
        }

        Console.WriteLine(string.Format("DBG T40 {0} det={1} fb={2} rt={3}", perBody, det, fb, rt));
        double worstAllP = 0d;
        double worstAllV = 0d;
        for (int i = 0; i < n; i++)
        {
            if (worstP[i] > worstAllP)
            {
                worstAllP = worstP[i];
            }

            if (worstV[i] > worstAllV)
            {
                worstAllV = worstV[i];
            }
        }

        Check(worstAllP < 50d && worstAllV < 1e-2d && det && fb && rt, "T40 ephemeris-bake",
            string.Format("2г bake12 8/32д: худш. p={0:E2}м (<50м) v={1:E2}м/с (<1e-2) [{2}] дет={3} прошлое-громко/будущее-продолжение={4} roundtrip={5}",
                worstAllP, worstAllV, perBody, det, fb, rt));
        return 0;
    }

    private static int Test35_KeplerEdge()
    {
        // B6/B7: остаточные невязки обоих решателей Кеплера по сетке, включая
        // тяжёлые углы (e→1 при M≈π и M малом). Допуск — на невязку уравнения,
        // а не на agreement реализаций. Фолбэк покрыт тем же ассёртом: если
        // Ньютон где-то не дотянет, бисекция обязана вытянуть.
        double worstElliptic = 0d;
        double[] ecc = new double[] { 0.9d, 0.99d, 0.999d, 0.9999d };
        double[] means = new double[] { 1e-8d, 1e-4d, 0.1d, 1.0d, Math.PI - 0.1d, Math.PI - 1e-7d };
        foreach (double e in ecc)
        {
            foreach (double m in means)
            {
                double root = KeplerMath.SolveEccentricAnomaly(m, e);
                double residual = Math.Abs(root - (e * Math.Sin(root)) - m);
                if (residual > worstElliptic)
                {
                    worstElliptic = residual;
                }
            }
        }

        double worstHyperbolic = 0d;
        double[] eccH = new double[] { 1.1d, 1.5d, 2.0d, 59d };
        double[] meansH = new double[] { -450d, -25d, -1d, -0.01d, 0.01d, 1d, 25d, 450d };
        foreach (double e in eccH)
        {
            foreach (double m in meansH)
            {
                double root = KeplerPredictor.SolveHyperbolicAnomaly(m, e);
                double residual = Math.Abs((e * Math.Sinh(root)) - root - m) / Math.Max(1d, Math.Abs(m));
                if (residual > worstHyperbolic)
                {
                    worstHyperbolic = residual;
                }
            }
        }

        // Гиперболические апсиды: inbound e=2 (ν=−1) → время до перицентра;
        // проверка независимая: Advance на это время обязан встать в r_p=α(e−1).
        // Outbound (скорость инвертирована) → перицентра впереди нет (NaN).
        double mu = 3.986e14d;
        double alpha = 1e7d;
        double eh = 2d;
        double nuIn = -1d;
        double pParam = alpha * ((eh * eh) - 1d);
        double rIn = pParam / (1d + (eh * Math.Cos(nuIn)));
        var posIn = new Vector3d(rIn * Math.Cos(nuIn), rIn * Math.Sin(nuIn), 0d);
        double v2 = mu * ((2d / rIn) + (1d / alpha));
        // Скорость = cos γ · transverse + sin γ · radial (γ со знаком):
        // угол вектора = (ν+π/2) − γ. Знак проверен: r·v < 0 (сближение).
        double gamma = Math.Atan2(eh * Math.Sin(nuIn), 1d + (eh * Math.Cos(nuIn)));
        double dirAngle = nuIn + (Math.PI / 2d) - gamma;
        var velIn = new Vector3d(Math.Sqrt(v2) * Math.Cos(dirAngle), Math.Sqrt(v2) * Math.Sin(dirAngle), 0d);
        OrbitalElements elIn = OrbitalElements.FromState(posIn, velIn, mu);
        bool okIn = elIn.TryGetTimeToApsides(mu, out double tPeIn, out double tApIn);
        KeplerPredictor.Advance(posIn, velIn, mu, tPeIn, out Vector3d arrP, out _);
        double periRadius = alpha * (eh - 1d);
        bool landsOnPeriapsis = okIn && !double.IsNaN(tPeIn) && tPeIn > 900d && tPeIn < 1400d
            && Math.Abs(arrP.Magnitude - periRadius) < 1000d && double.IsNaN(tApIn);

        var velOut = new Vector3d(-velIn.X, -velIn.Y, -velIn.Z);
        OrbitalElements elOut = OrbitalElements.FromState(posIn, velOut, mu);
        bool okOut = elOut.TryGetTimeToApsides(mu, out double tPeOut, out double tApOut);
        bool outboundNone = okOut && double.IsNaN(tPeOut) && double.IsNaN(tApOut);

        Check(worstElliptic < 1e-9d && worstHyperbolic < 1e-9d && landsOnPeriapsis && outboundNone, "T35 kepler-edge",
            string.Format("невязки: эллипс {0:E2}, гипербола {1:E2} (<1e-9); inbound tPe={2:F0}с r_p={3:F0}м (ожид 1e7); outbound NaN: {4}",
                worstElliptic, worstHyperbolic, tPeIn, arrP.Magnitude, outboundNone));
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

    // ════════════════════════════════════════════════════════════════════
    // Регресс-тесты по найденным багам (ревью 2026-09): центробежный знак,
    // Ламберт 180° (dt/longWay), кап z=39, force-accept рассинхрон,
    // timeToPeriapsis при M=0, валидация кеплеровой рельсы.
    // ════════════════════════════════════════════════════════════════════

    private sealed class SphereOnlyTerrain : ITerrainModel
    {
        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians) => 0d;
        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds) => relativePosition.Normalized;
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

    private static int Test59_BakeFilesAndSpeed()
    {
        // B1/D: BakeToFiles (потоковая запись) == Bake (память) бит-в-бит;
        // EstimateBakeBytes == фактический размер файлов; замер скорости
        // 8-летней выпечки с проекцией на 1024 года; прогресс/отмена.
        // ВАЖНО: каждая выпечка — на СВЕЖЕЙ системе: после attach тело читает
        // испечённую эфемериду вместо рельсов, и следующий бейк пошёл бы от
        // других начальных состояний (поймано этим же тестом).
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };
        StarSystem sys = TestSystem();
        var memory = EphemerisBaker.Bake(sys, 2d, config);

        string dir = Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t59_" + Guid.NewGuid().ToString("N"));
        var fileBaked = EphemerisBaker.BakeToFiles(sys, dir, 2d, config);

        // Бит-в-бит: файловый и memory-путь — один движок. Сравнение по
        // словарям, attach не участвует в сравнении.
        bool bitExact = true;
        for (int i = 0; i < 500; i++)
        {
            double t = (i / 500d) * 2d * 365d * 86400d;
            foreach (var kv in memory)
            {
                kv.Value.TryEvaluate(t, out Vector3d pm, out Vector3d vm);
                fileBaked[kv.Key].TryEvaluate(t, out Vector3d pf, out Vector3d vf);
                if ((pm - pf).SqrMagnitude != 0d || (vm - vf).SqrMagnitude != 0d)
                {
                    bitExact = false;
                }
            }
        }

        // Оценка размера == факт (формат детерминирован: 27 + seg·stride·8).
        var estimate = EphemerisBaker.EstimateBakeBytes(sys, 2d, config);
        bool sizeOk = true;
        long totalEstimated = 0;
        long totalActual = 0;
        foreach (var kv in estimate)
        {
            long actual = new FileInfo(Path.Combine(dir, SanitizeNameForCheck(kv.Key.Name) + ".bin")).Length;
            if (actual != kv.Value)
            {
                sizeOk = false;
            }

            totalEstimated += kv.Value;
            totalActual += actual;
        }

        // Сжатие B2-ревизия: BG2-контейнер (квантование с бюджетом ошибки +
        // zigzag-varint + gzip). Гарантия: |Δpos| ≤ budget/2 = 1 м на сегмент
        // (шаг кванта ≤ budget/13, Σ|T_j| ≤ 13); скорость проверяется с запасом.
        string dirC = Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t59c_" + Guid.NewGuid().ToString("N"));
        var fileC = EphemerisBaker.BakeToFiles(sys, dirC, 2d, config, null, null, null, compress: true);
        double posBound = config.QuantBudgetMeters * 0.5d;
        double velBound = 2e-3d;
        bool compressedExact = fileC.Count == memory.Count;
        double worstQPos = 0d;
        double worstQVel = 0d;
        for (int i = 0; i < 500 && compressedExact; i++)
        {
            double t = (i / 500d) * 2d * 365d * 86400d;
            foreach (var kv in memory)
            {
                kv.Value.TryEvaluate(t, out Vector3d pm, out Vector3d vm);
                fileC[kv.Key].TryEvaluate(t, out Vector3d pc, out Vector3d vc);
                double dp = (pm - pc).Magnitude;
                double dv = (vm - vc).Magnitude;
                if (dp > worstQPos)
                {
                    worstQPos = dp;
                }

                if (dv > worstQVel)
                {
                    worstQVel = dv;
                }

                if (dp > posBound || dv > velBound)
                {
                    compressedExact = false;
                    break;
                }
            }
        }

        // Прыжок назад по окнам: поздний t, затем ранний — переоткрытие потока.
        bool backwardOk = true;
        double tLate = 2d * 365d * 86400d - 1d;
        double tEarly = 1d;
        foreach (var kv in memory)
        {
            kv.Value.TryEvaluate(tLate, out Vector3d pmL, out _);
            fileC[kv.Key].TryEvaluate(tLate, out Vector3d pcL, out _);
            kv.Value.TryEvaluate(tEarly, out Vector3d pmE, out _);
            fileC[kv.Key].TryEvaluate(tEarly, out Vector3d pcE, out _);
            if ((pmL - pcL).Magnitude > posBound || (pmE - pcE).Magnitude > posBound)
            {
                backwardOk = false;
            }
        }

        long rawBytes = 0;
        long gzBytes = 0;
        foreach (string f in Directory.GetFiles(dir))
        {
            rawBytes += new FileInfo(f).Length;
        }

        foreach (string f in Directory.GetFiles(dirC))
        {
            gzBytes += new FileInfo(f).Length;
        }

        double ratio = (double)gzBytes / rawBytes;

        // Скорость: 8 лет, проекция ×128 на 1024 года (порядок, не метрика гейта).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        EphemerisBaker.Bake(sys, 8d, config);
        sw.Stop();
        double projected = sw.Elapsed.TotalSeconds * 128d;

        // Прогресс монотонно доходит до ~1; отмена бросает OperationCanceledException.
        double lastProgress = 0d;
        bool progressOk = true;
        EphemerisBaker.Bake(sys, 1d, config, null, f =>
        {
            if (f < lastProgress - 1e-9d)
            {
                progressOk = false;
            }

            lastProgress = f;
        });
        bool cancelOk = false;
        bool firstCall = true;
        try
        {
            EphemerisBaker.Bake(sys, 1d, config, null, null, () =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    return false;
                }

                return true;
            });
        }
        catch (OperationCanceledException)
        {
            cancelOk = true;
        }

        Check(bitExact && sizeOk && compressedExact && backwardOk && progressOk && lastProgress > 0.99d && cancelOk,
            "T59 bake-files-speed",
            string.Format("файл==память бит-в-бит: {0}; оценка={1}Б == факт={2}Б: {3}; BG2: бюджет-квант pos={4:E2}м(<={5:F1}м) vel={6:E2}м/с(<={7:F1}), назад={8}, сжатие {9:F0}Б/{10:F0}Б={11:F2}x; 8 лет за {12:F2}с → проекция 1024 лет ~{13:F0}с; прогресс: {14}; отмена: {15}",
                bitExact, totalEstimated, totalActual, sizeOk, worstQPos, posBound, worstQVel, velBound, backwardOk,
                gzBytes, rawBytes, ratio,
                sw.Elapsed.TotalSeconds, projected, progressOk && lastProgress > 0.99d, cancelOk));
        return 0;
    }

    private static string SanitizeNameForCheck(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name.Trim();
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


    private static int Test62_CompressV2()
    {
        // B3-ревизия на реальной 12-тельной системе (луны с кламп-сегментами —
        // худший случай сжатия). Проверки:
        // 1) ошибка позиции BG2 относительно несжатых рельсок ≤ budget/2 по
        //    всему спану (гарантия формата: шаг ≤ budget/13, Σ|T_j| ≤ 13);
        // 2) ошибка скорости ≤ 2e-3 м/с (измеренный класс 1e-7..1e-4, гейт
        //    T40 1e-2 — двухкратный запас под гейт);
        // 3) сжатие ≥ 2× к BE1;
        // 4) прыжок назад по окнам в пределах того же бюджета;
        // 5) LoadRangeToMemory == оконное чтение бит-в-бит (оба пути декодируют
        //    одни и те же кванты).
        StarSystem sys = TwelveBodySystem();
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d };
        var memory = EphemerisBaker.Bake(sys, 2d, config);
        string dir = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t62_" + Guid.NewGuid().ToString("N"));
        var files = EphemerisBaker.BakeToFiles(sys, dir, 2d, config, null, null, null, compress: true);
        double span = 2d * 365d * 86400d;
        double posBound = config.QuantBudgetMeters * 0.5d;
        double velBound = 2e-3d;
        double worstPos = 0d;
        double worstVel = 0d;
        bool within = files.Count == memory.Count;
        for (int i = 0; i < 2000 && within; i++)
        {
            double t = span * i / 1999d;
            foreach (var kv in memory)
            {
                kv.Value.TryEvaluate(t, out Vector3d pm, out Vector3d vm);
                files[kv.Key].TryEvaluate(t, out Vector3d pc, out Vector3d vc);
                double dp = (pm - pc).Magnitude;
                double dv = (vm - vc).Magnitude;
                if (dp > worstPos)
                {
                    worstPos = dp;
                }

                if (dv > worstVel)
                {
                    worstVel = dv;
                }

                if (dp > posBound || dv > velBound)
                {
                    within = false;
                    Console.WriteLine("  DEBUG: {0} t={1:F0} dp={2:E3} dv={3:E3}", kv.Key.Name, t, dp, dv);
                    break;
                }
            }
        }

        long be1Bytes = 0L;
        long gzBytes = 0L;
        foreach (var kv in memory)
        {
            be1Bytes += 27L + kv.Value.Coeffs.Length * 8L;
        }

        foreach (var kv in files)
        {
            gzBytes += new System.IO.FileInfo(kv.Value.FilePath).Length;
        }

        double ratio = (double)gzBytes / be1Bytes;
        bool ratioOk = ratio <= 0.5d;

        bool backwardOk = true;
        foreach (var kv in memory)
        {
            files[kv.Key].TryEvaluate(span - 1d, out Vector3d pL, out _);
            files[kv.Key].TryEvaluate(1d, out Vector3d pE, out _);
            kv.Value.TryEvaluate(span - 1d, out Vector3d qL, out _);
            kv.Value.TryEvaluate(1d, out Vector3d qE, out _);
            if ((pL - qL).Magnitude > posBound || (pE - qE).Magnitude > posBound)
            {
                backwardOk = false;
            }
        }

        bool bulkOk = true;
        OrbitingBody moonKey = null;
        foreach (var kv in files)
        {
            if (kv.Key.Name == "III-1")
            {
                moonKey = kv.Key;
                break;
            }
        }

        BakedEphemeris src = files[moonKey];
        BakedEphemeris bulk = src.LoadRangeToMemory(0d, span);
        if (bulk == null || bulk.Coeffs == null)
        {
            bulkOk = false;
        }
        else
        {
            for (int k = 0; k < 300 && bulkOk; k++)
            {
                double t = span * k / 299d;
                src.TryEvaluate(t, out Vector3d p1, out Vector3d v1);
                bulk.TryEvaluate(t, out Vector3d p2, out Vector3d v2);
                if (p1.X != p2.X || p1.Y != p2.Y || p1.Z != p2.Z || v1.X != v2.X || v1.Y != v2.Y || v1.Z != v2.Z)
                {
                    bulkOk = false;
                }
            }
        }

        Check(within && ratioOk && backwardOk && bulkOk, "T62 compress-v2",
            string.Format("BG2 12 тел, 2y: pos-worst={0:E2}м(<={1:F1}м) vel-worst={2:E2}м/с(<={3:F1}); сжатие {4:F0}Б/{5:F0}Б={6:F2}x BE1 (<=0.5); назад={7}; bulk-бит-в-бит={8}",
                worstPos, posBound, worstVel, velBound, gzBytes, be1Bytes, ratio, backwardOk, bulkOk));
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
        string dirC = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t63c_" + Guid.NewGuid().ToString("N"));
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
        string dirP = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t63p_" + Guid.NewGuid().ToString("N"));
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

    private static OrbitingBody T64Star()
    {
        return new OrbitingBody { Name = "Звезда", StandardGravitationalParameter = 1.327e20d };
    }

    private static OrbitingBody T64Planet()
    {
        return new OrbitingBody
        {
            Name = "Планета",
            StandardGravitationalParameter = 4e14,
            SemiMajorAxis = 1.5e11,
            Eccentricity = 0.01,
            Parent = null
        };
    }

    private static OrbitingBody T64Moon(string name, double a)
    {
        return new OrbitingBody
        {
            Name = name,
            StandardGravitationalParameter = 4.9e12,
            SemiMajorAxis = a,
            Eccentricity = 0.01
        };
    }

    private static void MakeMoonSystem(double moonA)
    {
        OrbitingBody star = T64Star();
        OrbitingBody planet = T64Planet();
        star.Children.Add(planet);
        OrbitingBody m1 = T64Moon("m1", moonA);
        planet.Parent = star;
        m1.Parent = planet;
        planet.Children.Add(m1);
        new StarSystem(star);
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

    private static StarSystem ManualStarPlanetMoon()
    {
        OrbitingBody star = new OrbitingBody { Name = "Звезда", StandardGravitationalParameter = 1.327e20d, Radius = 3e8 };
        OrbitingBody planet = new OrbitingBody
        {
            Name = "Планета",
            StandardGravitationalParameter = 4e14,
            Radius = 3.4e6,
            SemiMajorAxis = 1.5e11,
            Eccentricity = 0.015,
            MeanAnomalyAtEpochDegrees = 80
        };
        planet.Parent = star;
        star.Children.Add(planet);
        OrbitingBody moon = new OrbitingBody
        {
            Name = "Луна",
            StandardGravitationalParameter = 4.9e12,
            Radius = 1.3e6,
            SemiMajorAxis = 3.84e8,
            Eccentricity = 0.03,
            MeanAnomalyAtEpochDegrees = 12,
            NorthPoleDirection = new Vector3d(0d, 0.9d, 0.435889894354067d)
        };
        moon.Parent = planet;
        planet.Children.Add(moon);
        return new StarSystem(star);
    }

    private sealed class NanSource : IDynamicsSource
    {
        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return DynamicsContribution.FromSpecificAcceleration(new Vector3d(double.NaN, 0d, 0d));
        }
    }

    private sealed class DrainSource : IDynamicsSource
    {
        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 1d, Vector3d.Zero); // кг/с: сводит массу в ноль
        }
    }

    private sealed class NanDetector : ICrossingDetector
    {
        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            return timeSeconds > 1d ? double.NaN : state.Position.X;
        }

        public EventDirection Direction => EventDirection.Either;
        public int Priority => 10;
        public string Name => "NanDetector";
        public EventKind Kind => EventKind.Timed;
    }

    private static int Test66_NanProtocol()
    {
        // S1: NaN-протокол. Порча состояния обязана быть громкой на каждом
        // рубеже: нормализация, RHS, интегратор, детекторы. Молчаливые события
        // «нет пересечения» при NaN-g — прямой антагонист детекторного слоя.
        bool normalNaN = double.IsNaN(Vector3d.Zero.Normalized.X) == false; // ноль остаётся Zero
        var nanVec = new Vector3d(double.NaN, 1d, 0d);
        bool nanPropagates = double.IsNaN(nanVec.Normalized.X) && double.IsNaN(nanVec.Normalized.Y);
        bool zeroStaysZero = Vector3d.Zero.Normalized.X == 0d && Vector3d.Zero.Normalized.Y == 0d;
        var unit = new Vector3d(3d, 0d, 0d).Normalized;
        bool normalWorks = unit.X == 1d;

        // Интеграторы forward-only.
        bool forwardOnly = false;
        try
        {
#pragma warning disable 0618
            OrbitIntegrator.StepForward(Vector3d.Zero, Vector3d.Zero, 0d, -1d, (p, t) => Vector3d.Zero);
#pragma warning restore 0618
        }
        catch (ArgumentOutOfRangeException)
        {
            forwardOnly = true;
        }

        bool shipForwardOnly = false;
        try
        {
            SpacecraftOrbitIntegrator.StepForward(
                new SpacecraftIntegrationState { Position = Vector3d.Zero, Velocity = Vector3d.Zero, Mass = 1000d },
                0d, -1d,
                (s, t) => new SpacecraftIntegrationState { Position = s.Velocity, Velocity = Vector3d.Zero, Mass = 0d },
                1e-6d, 1e-6d, 1e-6d, 1e-10d);
        }
        catch (ArgumentOutOfRangeException)
        {
            shipForwardOnly = true;
        }

        // Не-конечный вклад источника — громко в RHS.
        bool nanSourceThrows = false;
        var phys = new SpacecraftPhysics();
        phys.Sources.Add(new NanSource());
        var shipNan = new Spacecraft(new Vector3d(0d, 8e6d, 0d), new Vector3d(5000d, 0d, 0d), 1000d);
        try
        {
            phys.Step(shipNan, 0d, 1d);
        }
        catch (InvalidOperationException)
        {
            nanSourceThrows = true;
        }

        // Источник, сводящий массу в ноль — громко на первом шаге после исчерпания.
        bool drainThrows = false;
        var phys2 = new SpacecraftPhysics();
        phys2.Sources.Add(new DrainSource());
        var shipDrain = new Spacecraft(new Vector3d(0d, 8e6d, 0d), new Vector3d(5000d, 0d, 0d), 0.1d);
        try
        {
            phys2.Step(shipDrain, 0d, 10d);
        }
        catch (InvalidOperationException)
        {
            drainThrows = true;
        }

        // Детектор с NaN-g — громко, а не молчаливый пропуск события.
        bool nanDetectorThrows = false;
        StarSystem sys = ManualStarPlanetMoon();
        var gravity = new CachedGravitySource(sys);
        var physics = new SpacecraftPhysics();
        physics.Sources.Add(gravity);
        var prop = new EventDrivenPropagator(physics);
        prop.CrossingDetectors.Add(new NanDetector());
        var ship = new Spacecraft(new Vector3d(0d, 8e6d, 0d), new Vector3d(5000d, 0d, 0d), 1000d);
        try
        {
            prop.Propagate(ship, 0d, 100d);
        }
        catch (InvalidOperationException ex)
        {
            nanDetectorThrows = ex.Message.Contains("NanDetector");
        }

        // Здоровый путь не пострадал: без NaN-детектора propagation работает.
        var prop2 = new EventDrivenPropagator(physics);
        var shipOk = new Spacecraft(new Vector3d(0d, 8e6d, 0d), new Vector3d(5000d, 0d, 0d), 1000d);
        EventOccurrence? none = prop2.Propagate(shipOk, 0d, 10d);
        bool healthyOk = none == null && shipOk.Position.IsFinite;

        Check(normalNaN && nanPropagates && zeroStaysZero && normalWorks && forwardOnly && shipForwardOnly
            && nanSourceThrows && drainThrows && nanDetectorThrows && healthyOk, "T66 nan-protocol",
            string.Format("нормализация: NaN распространяется={0}, ноль=Zero={1}, юнит={2}; forward-only: orbit={3}, ship={4}; NaN-источник громко={5}; drain-масса громко={6}; NaN-детектор громко={7}; здоровый путь жив={8}",
                nanPropagates, zeroStaysZero, normalWorks, forwardOnly, shipForwardOnly, nanSourceThrows, drainThrows, nanDetectorThrows, healthyOk));
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

    private static int Test68_StructuralContracts()
    {
        // S2/S3: структурная прочность ядра — «программист рядом» не может
        // молча потерять тела, зациклить иерархию, дублировать имена,
        // частично прицепить эфемериды или отравить кэш.
        bool cycleThrows = false;
        try
        {
            OrbitingBody a = new OrbitingBody { Name = "A", StandardGravitationalParameter = 1e14 };
            OrbitingBody b = new OrbitingBody { Name = "B", StandardGravitationalParameter = 1e12 };
            a.Parent = b;
            b.Parent = a;
            b.Children.Add(a);
            a.Children.Add(b);
            new StarSystem(a);
        }
        catch (InvalidOperationException)
        {
            cycleThrows = true;
        }

        bool parentConflictThrows = false;
        try
        {
            OrbitingBody root = new OrbitingBody { Name = "Root", StandardGravitationalParameter = 1.327e20 };
            OrbitingBody x = new OrbitingBody { Name = "X", StandardGravitationalParameter = 1e14, SemiMajorAxis = 1e11 };
            OrbitingBody y = new OrbitingBody { Name = "Y", StandardGravitationalParameter = 1e14, SemiMajorAxis = 2e11 };
            x.Parent = root;
            root.Children.Add(x);
            y.Parent = root;
            // Конфликт: y числится ребёнком root, но Parent указывает на x.
            root.Children.Add(y);
            y.Parent = x;
            new StarSystem(root);
        }
        catch (InvalidOperationException)
        {
            parentConflictThrows = true;
        }

        bool duplicateNamesThrow = false;
        try
        {
            OrbitingBody root = new OrbitingBody { Name = "Root", StandardGravitationalParameter = 1.327e20 };
            OrbitingBody x1 = new OrbitingBody { Name = "Twin", StandardGravitationalParameter = 1e14, SemiMajorAxis = 1e11 };
            OrbitingBody x2 = new OrbitingBody { Name = "Twin", StandardGravitationalParameter = 1e14, SemiMajorAxis = 2e11 };
            x1.Parent = root;
            x2.Parent = root;
            root.Children.Add(x1);
            root.Children.Add(x2);
            new StarSystem(root);
        }
        catch (InvalidOperationException)
        {
            duplicateNamesThrow = true;
        }

        // Частичный attach: манифест без одной луны — громко + откат.
        bool partialAttachThrows = false;
        bool rollbackClean = false;
        string dir = System.IO.Path.Combine("C:\\Users\\xleb4ikk\\AppData\\Local\\Temp\\opencode", "t68_" + Guid.NewGuid().ToString("N"));
        try
        {
            StarSystem full = ManualStarPlanetMoon();
            EphemerisBaker.BakeToFiles(full, dir, 2d, new BakeConfig { Degree = 12, MaxStepSeconds = 900d });
            // Убираем луну из манифеста (оригинал сохраняем для проверки отката).
            string manifestPath = System.IO.Path.Combine(dir, "manifest.txt");
            string[] lines = System.IO.File.ReadAllLines(manifestPath);
            System.IO.File.WriteAllLines(manifestPath, System.Linq.Enumerable.Where(lines, l => !l.StartsWith("Луна\t")));

            StarSystem target = ManualStarPlanetMoon();
            EphemerisBaker.AttachFiles(target, dir);
        }
        catch (InvalidOperationException ex)
        {
            partialAttachThrows = ex.Message.Contains("Луна");
            StarSystem victim = ManualStarPlanetMoon();
            rollbackClean = true;
            try
            {
                // Откат: восстановленный манифест — attach проходит целиком.
                string manifestPath2 = System.IO.Path.Combine(dir, "manifest.txt");
                System.IO.File.WriteAllLines(manifestPath2, new[]
                {
                    "Звезда\tnot-used",
                    "Планета\tПланета.bin",
                    "Луна\tЛуна.bin"
                });
                EphemerisBaker.AttachFiles(victim, dir);
            }
            catch (InvalidOperationException)
            {
                rollbackClean = false;
            }
        }

        // AttachEphemeris: чужое тело громко; валидный attach сбрасывает кэш.
        bool foreignBodyThrows = false;
        StarSystem owner = ManualStarPlanetMoon();
        var memoryEph = EphemerisBaker.Bake(owner, 1d, new BakeConfig { Degree = 12, MaxStepSeconds = 900d });
        try
        {
            StarSystem outsider = ManualStarPlanetMoon();
            owner.AttachEphemeris(outsider.AllBodies[1], memoryEph[owner.AllBodies[1]]);
        }
        catch (InvalidOperationException)
        {
            foreignBodyThrows = true;
        }

        bool attachViaMethodWorks = false;
        StarSystem owner2 = ManualStarPlanetMoon();
        var eph2 = EphemerisBaker.Bake(owner2, 1d, new BakeConfig { Degree = 12, MaxStepSeconds = 900d });
        owner2.EvaluateBodyState(owner2.AllBodies[1], 100d, out _, out _); // прогрев кэша (кеплеровы позиции)
        foreach (var kv in eph2)
        {
            owner2.AttachEphemeris(kv.Key, kv.Value);
        }

        owner2.EvaluateBodyState(owner2.AllBodies[1], 100d, out Vector3d pAfter, out Vector3d vAfter);
        eph2[owner2.AllBodies[1]].TryEvaluate(100d, out Vector3d pRail, out Vector3d vRail);
        // Кэш сброшен: позиция после attach == рельсе (звезда в нуле).
        attachViaMethodWorks = (pAfter - pRail).Magnitude < 1e-6d && vAfter.IsFinite;

        Check(cycleThrows && parentConflictThrows && duplicateNamesThrow && partialAttachThrows && rollbackClean
            && foreignBodyThrows && attachViaMethodWorks, "T68 structural-contracts",
            string.Format("цикл громко={0}; конфликт Parent={1}; дубли имён={2}; частичный attach громко={3}; откат чист={4}; чужое тело={5}; attach-метод работает={6}",
                cycleThrows, parentConflictThrows, duplicateNamesThrow, partialAttachThrows, rollbackClean, foreignBodyThrows, attachViaMethodWorks));
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

    private static int Test70_TryLiftoffSpin()
    {
        // Фаза 1.1: TryLiftoff при ω≠0. Баг был двойной: критерий отрыва без
        // центробежной (−ω×(ω×r)) расходился с SurfaceMotion (T52), а нормальная
        // составляющая относительной скорости при отрыве не вычищалась.
        // 1) Синхронизация с T52: СО-ВРАЩАЮЩИЙСЯ корабль (v_t=0), нулевая тяга:
        // ω²R=10>g → Flying (центробежная отрывает), ω²R=5<g → Landed.
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

        // 2) Со-вращающийся корабль с тягой: скорость не удваивается —
        // v_new == v_поверхности бит-в-близко (тангенциальная относительная нулевая).
        var landedShip = new Spacecraft(new Vector3d(radius, 0d, 0d), surfaceV, 1000d);
        VesselRegime lift = EventReactions.TryLiftoff(landedShip, fastBody,
            new Vector3d(16d, 0d, 0d), gIn, 0d, out _);
        bool noDoubleCount = lift == VesselRegime.Flying
            && (landedShip.Velocity - surfaceV).Magnitude < 1e-9d;

        // 3) Корабль с посторонней относительной скоростью (v=0-спавн,
        // v_t_rel = −ωR): отрыв требует тяги > g+ω²R (Кориолис прижимает),
        // после отрыва относительная тангенциальная СОХРАНЕНА — инерциальная
        // скорость нулевая, а не удвоенная поверхность.
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

    private static int Test71_DebrisTunnel()
    {
        // Фаза 1.2: антитуннельный кап прыжка debris. Баг: IsInsideAnyBody
        // проверяется только на КОНЦЕ hop'а — при FarHop=30с обломок со скоростью
        // v мог войти и выйти из тела радиусом < v·hop/2 (кастомный астероид),
        // детект не срабатывал никогда. Фикс: hop ≤ 0.25·R_min/v с полом 0.25с.
        StarSystem sys = TunnelSystem();
        var updater = new DebrisUpdater(sys);

        // Обломок на подлёте к статичному астероиду R=1e3, скорость 2000: хорда
        // 2R/v=1с — старый FarHop=30с давал чистое туннелирование. Фокус далеко
        // (FarHop-режим).
        var pool = new DebrisPool();
        Vector3d startPos = new Vector3d(5000d, 0d, 0d);
        pool.Spawn(startPos, new Vector3d(-2000d, 0d, 0d), 100d, 0d, 1e7d);
        Vector3d farFocus = new Vector3d(2e11, 0d, 0d);

        bool detected = false;
        double t = 0d;
        int hops = 0;
        while (t < 10d && hops < 1000)
        {
            hops++;
            if (!updater.AdvanceSlot(pool, 0, t, farFocus))
            {
                detected = true;
                break;
            }

            t = pool.SlotAt(0).NextCheckTimeSeconds;
        }

        // Крупное тело: кап не должен ломать LOD-экономию (R/v-кап больше FarHop).
        StarSystem big = ManualStarPlanetMoon();
        var bigUpdater = new DebrisUpdater(big);
        var poolBig = new DebrisPool();
        poolBig.Spawn(new Vector3d(2e11, 0d, 0d), new Vector3d(0d, 5000d, 0d), 100d, 0d, 1e7d);
        var probeSlot = poolBig.SlotAt(0);
        double bigHop = bigUpdater.HopForSlot(probeSlot, new Vector3d(0d, 0d, 0d), 0d);

        bool capWorks = detected && hops > 5;
        bool lodIntact = bigHop == DebrisUpdater.FarHopSeconds;

        // 3) Регресс A2: v_rel = 0 (со-движущийся обломок) не отключает кап
        // тихо — знаменатель клампится параболической скоростью у тела.
        var poolStill = new DebrisPool();
        poolStill.Spawn(new Vector3d(5000d, 0d, 0d), Vector3d.Zero, 100d, 0d, 1e7d);
        double stillHop = updater.HopForSlot(poolStill.SlotAt(0), new Vector3d(2e11, 0d, 0d), 0d);
        // Пол = √(2μ/d) = √(2e9/5000) ≈ 447 м/с → кап = 0.25·1000/447 ≈ 0.56с < NearHop=1с.
        double expectedFloorSpeed = Math.Sqrt(2d * 1e9d / 5000d);
        bool zeroRelClamped = stillHop < DebrisUpdater.NearHopSeconds
            && stillHop >= DebrisUpdater.MinHopSeconds - 1e-12d;

        Check(capWorks && lodIntact && zeroRelClamped, "T71 debris-tunnel",
            string.Format("астероид R=1e3 v=2000: детект={0} за {1} hop'ов (кап 0.25·R/v={2:E3}с); крупная система: LOD-прыжок {3}с не сжат: {4}; v_rel=0 клампнут (√(2μ/d)={5:E0}м/с, hop={6:E3}с<1с): {7}",
                detected, hops, DebrisUpdater.TunnelSafetyFactor * sys.MinimumBodyRadiusMeters / 2000d, bigHop, lodIntact,
                expectedFloorSpeed, stillHop, zeroRelClamped));
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

    private static int Test72_VisualFrame()
    {
        // Фаза 2.1: GetVisualFrame — готовый визуальный кадр (позиция+скорость+
        // ориентация) из ядра, мёртвое Unity-поле VisualTransform удалено.
        // Контракт: ориентация собрана тем же путём, что GetSurfaceState —
        // поворот базисных векторов body-frame кватернионом обязан совпадать
        // с поверхностными точками (нулевой меридиан, 90°E, полюс) на теле с
        // НАКЛОНЁННОЙ осью и вращением, в разные моменты времени.
        StarSystem sys = ManualStarPlanetMoon();
        OrbitingBody moon = sys.AllBodies[2];
        moon.RotationPeriodSeconds = 86400d;
        moon.PrimeMeridianOffsetDegrees = 37d;

        bool basisOk = true;
        double worstPosErr = 0d;
        foreach (double t in new[] { 0d, 1234.5d, 86400d * 3.7d })
        {
            sys.GetVisualFrame(moon, t, out Vector3d pos, out _, out QuaternionD orientation);
            moon.EvaluateWorldState(t, out Vector3d bodyP, out _);
            Vector3d xw = orientation.Rotate(new Vector3d(1d, 0d, 0d));
            Vector3d yw = orientation.Rotate(new Vector3d(0d, 1d, 0d));
            Vector3d zw = orientation.Rotate(new Vector3d(0d, 0d, 1d));

            moon.GetSurfaceState(0d, 0d, 0d, t, out Vector3d pX, out _);
            moon.GetSurfaceState(0d, 90d, 0d, t, out Vector3d pY, out _);
            double ex = ((pX - bodyP).Normalized - xw).Magnitude;
            double ey = ((pY - bodyP).Normalized - yw).Magnitude;
            double ez = (moon.SpinAxis - zw).Magnitude;
            if (ex > worstPosErr)
            {
                worstPosErr = ex;
            }

            if (ey > worstPosErr)
            {
                worstPosErr = ey;
            }

            if (ez > worstPosErr)
            {
                worstPosErr = ez;
            }

            basisOk &= ex < 1e-9d && ey < 1e-9d && ez < 1e-9d;
        }

        // Ось (0,0,1) без вращения — тождество бит-в-бит (T17c-ветка).
        OrbitingBody star = sys.AllBodies[0];
        sys.GetVisualFrame(star, 0d, out _, out _, out QuaternionD starOrientation);
        bool identityOk = starOrientation.W == 1d && starOrientation.X == 0d && starOrientation.Y == 0d && starOrientation.Z == 0d;

        Check(basisOk && identityOk, "T72 visual-frame",
            string.Format("базис body→world совпал с GetSurfaceState (наклонная ось, вращение, 3 момента): худшая ошибка {0:E2} (<1e-9); звезда (0,0,1) — тождество бит-в-бит: {1}",
                worstPosErr, identityOk));
        return 0;
    }

    private static int Test74_ScaleBounds()
    {
        // Фаза 2.3: fp-бюджет масштаба громко. Орбита дальше MaxOrbitRadiusMeters
        // (1e13 м) отвергается при загрузке: ulp(1e13)≈2e-3 м — рельеф и
        // посадочные допуски теряют смысл. Граница — на полуось рельсы; hyper/
        // degenerate (a≤0) не проверяются; на границе 1e13 ровно — проходит.
        bool farLoud = false;
        try
        {
            var far = new OrbitingBody { Name = "Far", Radius = 1e6d, SemiMajorAxis = 2e13d };
            var farStar = new OrbitingBody { Name = "Star", Radius = 3e8d, StandardGravitationalParameter = 1.327e20d };
            far.Parent = farStar;
            farStar.Children.Add(far);
            new StarSystem(farStar);
        }
        catch (InvalidOperationException)
        {
            farLoud = true;
        }

        var edge = new OrbitingBody { Name = "Edge", Radius = 1e6d, SemiMajorAxis = 1e13d };
        var edgeStar = new OrbitingBody { Name = "Star", Radius = 3e8d, StandardGravitationalParameter = 1.327e20d };
        edge.Parent = edgeStar;
        edgeStar.Children.Add(edge);
        bool edgePasses = new StarSystem(edgeStar) != null;

        // Гиперболическая/вырожденная рельса (a≤0) — не проверяется на масштаб.
        var hyper = new OrbitingBody { Name = "Hyper", Radius = 1e6d, SemiMajorAxis = 1e8d, Eccentricity = 1.2d };
        var hyperStar = new OrbitingBody { Name = "Star", Radius = 3e8d, StandardGravitationalParameter = 1.327e20d };
        hyper.Parent = hyperStar;
        hyperStar.Children.Add(hyper);
        bool hyperPasses = new StarSystem(hyperStar) != null;

        Check(farLoud && edgePasses && hyperPasses, "T74 scale-bounds",
            string.Format("a=2e13 громко: {0}; a=1e13 ровно проходит: {1}; hyper a=1e8 e=1.2 пропущен: {2}",
                farLoud, edgePasses, hyperPasses));
        return 0;
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

    private sealed class StateTorqueSource : IDynamicsSource
    {
        // Задел на gravity-gradient: момент ЗАВИСИТ от состояния — probe-шаг
        // бисекции в другой точке обязан оставлять другой след в снимке, и
        // только save/restore держит снимок согласованным с принятой траекторией.
        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, position * 1e-9d);
        }
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

    private static int Test76_QuaternionBridge()
    {
        // Фаза B2: мост ориентаций q_sim = R·q_astro·R⁻¹. Инварианты:
        // 1) ось идёт через позиционный мост: q_sim(axis a, θ) вращает
        //    ToSim(a); 2) тождество — бит-в-бит; 3) вращение вокруг севера
        //    +Z выглядит как вращение вокруг up +Y; 4) норма сохранена.
        QuaternionD identity = AstroFrame.ToSimulationFrame(QuaternionD.Identity);
        bool identityOk = identity.W == 1d && identity.X == 0d && identity.Y == 0d && identity.Z == 0d;

        bool axisOk = true;
        double worst = 0d;
        foreach (double theta in new[] { 0.0d, 0.3d, Math.PI / 2d, 2.7d, -Math.PI })
        {
            Vector3d axisAstro = new Vector3d(1d, 2d, -1d);
            QuaternionD q = QuaternionD.FromAxisAngle(axisAstro, theta);
            QuaternionD qSim = AstroFrame.ToSimulationFrame(q);
            // Ось в sim-кадре.
            SimVector3 axisSim = SimOf(axisAstro.Normalized);
            Vector3d simAxis = new Vector3d(axisSim.X, axisSim.Y, axisSim.Z);
            // Действие: q_sim, применённое к мостнутой оси, не меняет её (вращение вокруг собственной оси).
            Vector3d turned = qSim.Rotate(simAxis);
            double drift = (turned - simAxis).Magnitude;
            if (drift > worst)
            {
                worst = drift;
            }

            axisOk &= drift < 1e-12d && Math.Abs(qSim.NormSquared - 1d) < 1e-12d;
        }

        // Север +Z → вращение вокруг up +Y: мост кватерниона вращения вокруг Z
        // даёт кватернион с осью (0,±1,0).
        QuaternionD spinZ = AstroFrame.ToSimulationFrame(QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), 0.7d));
        bool upAxisOk = Math.Abs(spinZ.X) < 1e-12d && Math.Abs(spinZ.Z) < 1e-12d && Math.Abs(Math.Abs(spinZ.Y) - Math.Sin(0.35d)) < 1e-12d;

        // Согласованность с векторным мостом: R·(q·v·q⁻¹) = q_sim·R·v.
        Vector3d v = new Vector3d(3d, -2d, 5d);
        QuaternionD q2 = QuaternionD.FromAxisAngle(new Vector3d(1d, 1d, 1d), 1.1d);
        Vector3d astroWay = q2.Rotate(v);
        SimVector3 simWayS = SimOf(astroWay);
        Vector3d simWay = new Vector3d(simWayS.X, simWayS.Y, simWayS.Z);
        SimVector3 vS = SimOf(v);
        Vector3d direct = AstroFrame.ToSimulationFrame(q2).Rotate(new Vector3d(vS.X, vS.Y, vS.Z));
        bool commutes = (simWay - direct).Magnitude < 1e-12d;

        Check(identityOk && axisOk && upAxisOk && commutes, "T76 quaternion-bridge",
            string.Format("тождество бит-в-бит: {0}; оси через позиционный мост, норма=1 (худш. дрейф {1:E2}): {2}; север +Z → ось up +Y: {3}; R·(q·v) == q_sim·(R·v): {4}",
                identityOk, worst, axisOk, upAxisOk, commutes));
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

        // Детерминизм: одинаковый seed — бит-в-бит одинаковые высоты.
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

        // Кламп моря + диапазон амплитуды.
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

        // Нормаль согласована с высотной функцией: независимая конечная разность
        // (крупнее eps нормали, 1e-3 рад) через GetSurfaceState совпадает по направлению.
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

        // Спин: нормаль одного тел-fixed пункта (lat0/lon0) в момент t = quarter
        // периода равна повороту Родригеса нормали t=0 вокруг оси спина.
        // Позицию пункта в каждый момент даёт GetSurfaceState — он же учитывает
        // RotationAngleAtTime(t), поэтому WorldLatLonOf даёт тот же lat/lon.
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

        // Детектор касания с рельефом: над пиком g<0 раньше сферы, выше — g>0.
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

        // g детектора — ВЫСОТА над рельефом: −50 м (ниже поверхности) → пересечение,
        // +300 м (выше) → ещё нет. Момент события — точная поверхность.
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

    private static int Test78_JointedBreakup()
    {
        var parts = new List<Part>
        {
            new Part(3000d, new Vector3d(0d, 0d, 2d), 500000d),
            new Part(2000d, new Vector3d(0d, 0d, -2d), 600000d)
        };
        var model = new JointedBreakup { PulseSeconds = 0.05d, SurfaceRestitution = 0.2d, TangentialDampFactor = 0.5d, SpreadFactor = 0.0d };
        Vector3d normal = new Vector3d(0d, 0d, 1d);
        Vector3d impact = new Vector3d(30d, 0d, -10d);
        var input = new BreakupInput(
            5000d, impact, new Vector3d(0d, 0d, 0d),
            new Vector3d(0d, 0d, 6.371e6d), normal, 1d, parts);
        IReadOnlyList<PartSeparationSpec> specs = model.PlanBreakup(input);

        // Перегрузка: load/кг = 10/0.05 = 200 → деталь 3000кг: 600000 > 500000 (ломается),
        // 2000кг: 400000 < 600000 (выживает). Спека ровно одна.
        bool oneBroken = specs.Count == 1 && specs[0].PartIndex == 0;

        // Тангенциальный инвариант: Σ m_i·f_i^t (broken) + m_surv·f_surv^t = m_ship·v_t.
        double survivorMass = 2000d;
        Vector3d fSurv = new Vector3d(30d, 0d, 0d) * (1d - 0.5d);
        Vector3d fragmentTangent = specs[0].FragmentVelocity - (normal * Vector3d.Dot(specs[0].FragmentVelocity, normal));
        Vector3d momentum = (fragmentTangent * specs[0].Mass) + (fSurv * survivorMass);
        Vector3d expect = new Vector3d(30d, 0d, 0d) * 5000d;
        double momentumError = (momentum - expect).Magnitude;
        bool momentumOk = momentumError < 1e-6d * expect.Magnitude;

        // Отскок: нормальная компонента наружу, ровно −e·v_n = +2 м/с (не сильно).
        double normalOut = Vector3d.Dot(specs[0].FragmentVelocity, normal);
        bool bounceOk = Math.Abs(normalOut - 2d) < 1e-9d;

        // Стыки держат → пустой список (посадка с повреждениями).
        var strong = new List<Part>
        {
            new Part(3000d, new Vector3d(0d, 0d, 2d), 1e12d),
            new Part(2000d, new Vector3d(0d, 0d, -2d), 1e12d)
        };
        var strongInput = new BreakupInput(5000d, impact, Vector3d.Zero, new Vector3d(0d, 0d, 6.371e6d), normal, 1d, strong);
        bool holdsOk = model.PlanBreakup(strongInput).Count == 0;

        // Без деталей → whole-ship fallback (10 фрагментов, инвариант Σm).
        var fallbackInput = new BreakupInput(5000d, impact, Vector3d.Zero, new Vector3d(0d, 0d, 6.371e6d), normal, 1d);
        IReadOnlyList<PartSeparationSpec> fallback = model.PlanBreakup(fallbackInput);
        double fallbackMass = 0d;
        for (int i = 0; i < fallback.Count; i++)
        {
            fallbackMass += fallback[i].Mass;
        }

        bool fallbackOk = fallback.Count == 10 && Math.Abs(fallbackMass - 5000d) < 1e-6d;

        Check(oneBroken, "T78 jointed-breakup", "слабейший стык отломан, спека ровно одна");
        Check(momentumOk, "T78 jointed-breakup", "тангенциальный инвариант, Δ=" + momentumError.ToString("E2"));
        Check(bounceOk, "T78 jointed-breakup", "отскок наружу, v_n=" + normalOut.ToString("F2") + " (= e·|v_n|)");
        Check(holdsOk, "T78 jointed-breakup", "прочные стыки → пустой список спеков");
        Check(fallbackOk, "T78 jointed-breakup", "без деталей → whole-ship fallback (10 фрагментов)");
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

    public static int Main(string[] args)
    {
        Console.WriteLine("P1b tests...");
        string filter = null;
        bool fastOnly = false;
        bool slowOnly = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--list")
            {
                foreach (KeyValuePair<string, Func<int>> entry in TestRegistry())
                {
                    Console.WriteLine((SlowTests.Contains(entry.Key) ? "slow " : "fast ") + entry.Key);
                }

                return 0;
            }

            if (args[i] == "--filter" && i + 1 < args.Length)
            {
                filter = args[i + 1];
                i++;
            }
            else if (args[i] == "--fast")
            {
                fastOnly = true;
            }
            else if (args[i] == "--slow")
            {
                slowOnly = true;
            }
        }

        foreach (KeyValuePair<string, Func<int>> entry in TestRegistry())
        {
            if (filter != null && entry.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (fastOnly && SlowTests.Contains(entry.Key))
            {
                continue;
            }

            if (slowOnly && !SlowTests.Contains(entry.Key))
            {
                continue;
            }

            entry.Value();
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
        return failures;
    }
}
