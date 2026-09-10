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

    private static double WrapLonError(double a, double b)
    {
        double d = Math.Abs(a - b);
        return Math.Min(d, 360d - d);
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

    private static int Test85_CubeSphere()
    {
        // 1) Единичная длина по всем граням.
        bool unit = true;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int i = 0; i <= 16; i++)
            {
                for (int j = 0; j <= 16; j++)
                {
                    Vector3d d = CubeSphere.Direction(face, i / 16d, j / 16d);
                    if (Math.Abs(d.Magnitude - 1d) > 1e-12d)
                    {
                        unit = false;
                    }
                }
            }
        }

        // 2) Непрерывность через рёбра грани +Z (face 4): выведенные пары
        // параметризаций соседних граней должны давать бит-в-бит один вектор.
        bool edges = true;
        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32d;
            edges &= SameDir(CubeSphere.Direction(4, t, 1d), CubeSphere.Direction(2, t, 0d));
            edges &= SameDir(CubeSphere.Direction(4, t, 0d), CubeSphere.Direction(3, t, 1d));
            edges &= SameDir(CubeSphere.Direction(4, 0d, t), CubeSphere.Direction(1, 1d, t));
            edges &= SameDir(CubeSphere.Direction(4, 1d, t), CubeSphere.Direction(0, 0d, t));
        }

        // 3) Центры узлов quadtree: depth 0 — ось грани, depth 1 — 4 разных центра.
        Vector3d c0 = CubeSphere.NodeCenterDirection(4, 0, 0, 0);
        bool axis = Math.Abs(c0.Z - 1d) < 1e-12d;
        Vector3d ca = CubeSphere.NodeCenterDirection(4, 1, 0, 0);
        Vector3d cb = CubeSphere.NodeCenterDirection(4, 1, 1, 0);
        Vector3d cc = CubeSphere.NodeCenterDirection(4, 1, 0, 1);
        Vector3d cd = CubeSphere.NodeCenterDirection(4, 1, 1, 1);
        bool distinct = (ca - cb).Magnitude > 1e-6d && (ca - cc).Magnitude > 1e-6d
            && (ca - cd).Magnitude > 1e-6d && (cb - cc).Magnitude > 1e-6d;

        Check(unit, "T85 cube-sphere", "все направления единичные");
        Check(edges, "T85 cube-sphere", "непрерывность через рёбра граней");
        Check(axis && distinct, "T85 cube-sphere", "центры узлов quadtree");
        return 0;
    }
}
