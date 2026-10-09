using System;
using Galilego.Core;
using Galilego.Spacecraft.Solver;
using UnityEngine;

// Фаза 4 (ТЗ v2, раздел 7): ноги и подвеска — пружинные ползуны с ходом и демпфированием.
// Test220 — статическая осадка W/(N·k); Test221 — затухание свободных колебаний (ζ = 0.4);
// Test222 — ограничение хода; Test223 — устойчивость на склоне 15° на трёх ногах.
internal static partial class P1bTests
{
    private const double LegDt = 1e-3;
    private const int LegIterations = 20;
    private const double LegLength = 2.5d;
    private const double LegMass = 50d;

    private static RigidBody LegBody(Vector3d top)
    {
        // Цилиндр r = 0.15 м, h = 2.5 м, масса 50 кг: Ixx = Izz = m(3r²+h²)/12, Iyy = m·r²/2.
        double r = 0.15d;
        double h = LegLength;
        double ixx = LegMass * ((3d * r * r) + (h * h)) / 12d;
        double iyy = LegMass * r * r / 2d;
        var leg = new RigidBody(LegMass, new Vector3d(ixx, iyy, ixx), top - new Vector3d(0d, 0.5d * h, 0d), QuaternionD.Identity);
        leg.ContactPointsBody = new[] { new Vector3d(0d, -0.5d * h, 0d) };
        return leg;
    }

    // Ось ноги — вертикаль вниз; сжатие = s0 − s (тело сближается с ногой).
    static int Test220_LegSag()
    {
        const double tankMass = 4800d;
        const double k = 49000d;
        var solver = new RigidAssemblySolver(LegDt, LegIterations);
        solver.Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d);
        var tank = new RigidBody(tankMass, new Vector3d(5e4d, 5e4d, 5e4d), new Vector3d(0d, 3.0d, 0d), QuaternionD.Identity);
        int it = solver.AddBody(tank);
        var legs = new int[4];
        double[] xs = { 2.2d, -2.2d, 0d, 0d };
        double[] zs = { 0d, 0d, 2.2d, -2.2d };
        for (int i = 0; i < 4; i++)
        {
            var top = new Vector3d(xs[i], 2.5d, zs[i]);
            legs[i] = solver.AddBody(LegBody(top));
            // ζ = 0.4 по опорному режиму: колеблющаяся масса — бак на ногу (ТЗ 6.7).
            solver.AddSliderJoint(it, legs[i], top, new Vector3d(0d, -1d, 0d), -0.3d, 0.3d, k, 0.4d,
                damperReferenceMass: tankMass / 4d);
        }

        bool dbg = Environment.GetEnvironmentVariable("P1B_LEG_DEBUG") == "1";

        // 20 с: мода бака с ζ = 0.4 на массе бака затухает за τ ≈ 2 с; 6 с — недостаточно.
        for (int step = 0; step < 20000; step++)
        {
            tank.ForceWorld = new Vector3d(0d, -9.81d * tankMass, 0d);
            for (int i = 0; i < 4; i++)
            {
                solver.Bodies[legs[i]].ForceWorld = new Vector3d(0d, -9.81d * LegMass, 0d);
            }

            solver.Step();
            if (dbg && step % 2000 == 0)
            {
                string legsText = string.Empty;
                for (int i = 0; i < 4; i++)
                {
                    legsText += solver.SliderCoordinate(i).ToString("F5", Inv) + " ";
                }

                Console.WriteLine("  [T220] t=" + solver.Time.ToString("F1", Inv) + " s, tankY=" +
                    tank.Position.Y.ToString("F5", Inv) + ", s=[" + legsText + "], contacts=" + solver.LastContactCount);
            }
        }

        double expected = tankMass * 9.81d / (4d * k);
        double worst = 0d;
        for (int i = 0; i < 4; i++)
        {
            double compression = -solver.SliderCoordinate(i);
            worst = Math.Max(worst, Math.Abs(compression - expected) / expected);
        }

        Check(worst <= 0.02d, "T220 leg-sag",
            string.Format(Inv, "W/(N·k) = {0:F4} м; максимум отклонения по 4 ногам = {1:P2} (≤2%)", expected, worst));
        return 0;
    }

    // Одна нога, малая масса бака: собственные колебания относительной координаты.
    static int Test221_LegDampingDecay()
    {
        const double tankMass = 50d;
        const double k = 49000d;
        const double zeta = 0.4d;
        // Нога в воздухе (без поверхности): относительная масса — m_эфф = mA·mB/(mA+mB), как
        // в формуле ζ. Опёртая на землю нога даёт массу бака — это другой, вынужденный случай.
        var solver = new RigidAssemblySolver(LegDt, LegIterations);
        var tank = new RigidBody(tankMass, new Vector3d(1d, 1d, 1d), new Vector3d(0d, 3.0d, 0d), QuaternionD.Identity);
        int it = solver.AddBody(tank);
        var top = new Vector3d(0d, 2.5d, 0d);
        int leg = solver.AddBody(LegBody(top));
        // ζ = 0.4 по опорному режиму (масса аппарата на ногу = масса бака здесь).
        double c = 2d * zeta * Math.Sqrt(k * tankMass);
        solver.AddSliderJoint(it, leg, top, new Vector3d(0d, -1d, 0d), -0.3d, 0.3d, k, zeta,
            damperReferenceMass: tankMass);

        double mEff = (tankMass * LegMass) / (tankMass + LegMass);
        double omega = Math.Sqrt(k / mEff);
        // Воздушный режим: эффективное затухание из фактического c, τ = 2·m_eff/c.
        double zetaAir = c / (2d * Math.Sqrt(k * mEff));
        double sEq = solver.SliderCoordinate(0);
        tank.Position = tank.Position + new Vector3d(0d, 0.02d, 0d);

        var peakTimes = new System.Collections.Generic.List<double>();
        var peakValues = new System.Collections.Generic.List<double>();
        double prev = 0d;
        double prevPrev = 0d;
        for (int step = 0; step < 2000; step++)
        {
            // Без поля тяжести: собственные колебания относительной координаты.
            solver.Step();
            double x = Math.Abs(solver.SliderCoordinate(0) - sEq);
            if (step >= 2 && prev > x && prev > prevPrev && prev > 1e-6)
            {
                peakTimes.Add(solver.Time - LegDt);
                peakValues.Add(prev);
            }

            prevPrev = prev;
            prev = x;
        }

        int n = peakTimes.Count;
        double meanT = 0d;
        double meanY = 0d;
        for (int i = 0; i < n; i++)
        {
            meanT += peakTimes[i];
            meanY += Math.Log(peakValues[i]);
        }

        meanT /= n;
        meanY /= n;
        double num = 0d;
        double den = 0d;
        for (int i = 0; i < n; i++)
        {
            num += (peakTimes[i] - meanT) * (Math.Log(peakValues[i]) - meanY);
            den += (peakTimes[i] - meanT) * (peakTimes[i] - meanT);
        }

        double tauMeasured = -1d / (num / den);
        double tauTheory = 1d / (zetaAir * omega);
        Check(n >= 3 && Math.Abs(tauMeasured - tauTheory) / tauTheory <= 0.1d && tauMeasured <= 1d,
            "T221 leg-damping-decay",
            string.Format(Inv, "пиков={0}; τ_изм={1:F4} с, τ_теор = 2·m_eff/c = {2:F4} с (±10%), ζ_возд={3:F3} (ζ_опорн=0.4); спад в e ≤ 1 с",
                n, tauMeasured, tauTheory, zetaAir));
        return 0;
    }

    // Сильная нагрузка: статическое сжатие и растяжение выходят за ход — ограничения держат.
    static int Test222_TravelLimits()
    {
        const double tankMass = 50d;
        const double k = 49000d;
        var solver = new RigidAssemblySolver(LegDt, LegIterations);
        solver.Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d);
        var tank = new RigidBody(tankMass, new Vector3d(1d, 1d, 1d), new Vector3d(0d, 3.0d, 0d), QuaternionD.Identity);
        int it = solver.AddBody(tank);
        var top = new Vector3d(0d, 2.5d, 0d);
        int leg = solver.AddBody(LegBody(top));
        solver.AddSliderJoint(it, leg, top, new Vector3d(0d, -1d, 0d), -0.3d, 0.3d, k, 0.4d);

        double maxCompression = 0d;
        double maxExtension = 0d;
        for (int phase = 0; phase < 2; phase++)
        {
            // Фаза 0: тянем бак вниз (сжатие). Фаза 1: тянем вверх (растяжение).
            double push = phase == 0 ? 30000d : -30000d;
            for (int step = 0; step < 3000; step++)
            {
                tank.ForceWorld = new Vector3d(0d, (-9.81d * tankMass) - push, 0d);
                solver.Bodies[leg].ForceWorld = new Vector3d(0d, -9.81d * LegMass, 0d);
                solver.Step();
                double compression = -solver.SliderCoordinate(0);
                maxCompression = Math.Max(maxCompression, compression);
                maxExtension = Math.Max(maxExtension, -compression);
            }
        }

        const double tol = 1e-3d;
        Check(maxCompression <= 0.3d + tol && maxExtension <= 0.3d + tol, "T222 travel-limits",
            string.Format(Inv, "max сжатие={0:F4} м, max растяжение={1:F4} м (ход ±0.3 м, допуск 1 мм)",
                maxCompression, maxExtension));
        return 0;
    }

    // Три ноги на склоне 15°: устойчивость 30 с (трение μs = 0.7 > tg 15° = 0.27).
    static int Test223_Slope15Stable()
    {
        const double tankMass = 4800d;
        const double k = 80000d;
        double th = 15d * Math.PI / 180d;
        var normal = new Vector3d(Math.Sin(th), Math.Cos(th), 0d);
        var solver = new RigidAssemblySolver(LegDt, LegIterations)
        {
            Ground = new PlaneGround(normal, 0d),
            StaticFriction = 0.7d,
            KineticFriction = 0.5d,
        };
        var tank = new RigidBody(tankMass, new Vector3d(5e4d, 5e4d, 5e4d), new Vector3d(0d, 3.0d, 0d), QuaternionD.Identity);
        int it = solver.AddBody(tank);
        var legs = new int[3];
        for (int i = 0; i < 3; i++)
        {
            double ang = i * 2d * Math.PI / 3d;
            double x = 2.2d * Math.Cos(ang);
            double z = 2.2d * Math.Sin(ang);
            // Точка крепления над плоскостью y = −x·tg15 (нога вертикальна, стопа на плоскости).
            var top = new Vector3d(x, 2.5d - (x * Math.Tan(th)), z);
            legs[i] = solver.AddBody(LegBody(top));
            solver.AddSliderJoint(it, legs[i], top, new Vector3d(0d, -1d, 0d), -0.3d, 0.3d, k, 0.4d,
                damperReferenceMass: tankMass / 3d);
        }

        bool dbg = Environment.GetEnvironmentVariable("P1B_LEG_DEBUG") == "1";
        Vector3d start = tank.Position;
        double maxCompression = 0d;
        double maxTilt = 0d;
        for (int step = 0; step < 30000; step++)
        {
            tank.ForceWorld = new Vector3d(0d, -9.81d * tankMass, 0d);
            for (int i = 0; i < 3; i++)
            {
                solver.Bodies[legs[i]].ForceWorld = new Vector3d(0d, -9.81d * LegMass, 0d);
            }

            solver.Step();
            for (int i = 0; i < 3; i++)
            {
                maxCompression = Math.Max(maxCompression, -solver.SliderCoordinate(i));
            }

            Vector3d up = tank.Orientation.Rotate(new Vector3d(0d, 1d, 0d));
            double tilt = Math.Acos(Math.Max(-1d, Math.Min(1d, up.Y))) * 180d / Math.PI;
            maxTilt = Math.Max(maxTilt, tilt);

            if (dbg && step % 5000 == 0)
            {
                Vector3d d = tank.Position - start;
                string legsText = string.Empty;
                for (int i = 0; i < 3; i++)
                {
                    legsText += solver.SliderCoordinate(i).ToString("F5", Inv) + " ";
                }

                Console.WriteLine("  [T223] t=" + solver.Time.ToString("F1", Inv) +
                    " с, drift=(" + d.X.ToString("F4", Inv) + "," + d.Z.ToString("F4", Inv) + "), tilt=" + tilt.ToString("F3", Inv) +
                    "°, s=[" + legsText + "], contacts=" + solver.LastContactCount +
                    ", Nimp=" + solver.LastNormalImpulseSum.ToString("F3", Inv) +
                    ", Fimp=" + solver.LastFrictionImpulseSum.ToString("F3", Inv));
            }
        }

        Vector3d drift = tank.Position - start;
        double horizontal = Math.Sqrt((drift.X * drift.X) + (drift.Z * drift.Z));
        bool finite = double.IsFinite(horizontal) && double.IsFinite(maxTilt);
        Check(finite && horizontal <= 0.05d && maxTilt <= 5d && maxCompression <= 0.3d, "T223 slope15-stable",
            string.Format(Inv, "30 с: горизонтальный снос={0:F4} м (≤0.05), max наклон={1:F3}° (≤5), max сжатие={2:F4} м (≤0.3)",
                horizontal, maxTilt, maxCompression));
        return 0;
    }
}
