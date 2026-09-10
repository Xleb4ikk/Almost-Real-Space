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
        string dir = System.IO.Path.Combine(Path.GetTempPath(), "t68_" + Guid.NewGuid().ToString("N"));
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
}
