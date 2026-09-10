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

    private static int Test46_FullBake1024()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        StarSystem sys = TestSystem();
        var eph = EphemerisBaker.Bake(sys, 1024d, 900d, 12, Test40_SegRule);
        sw.Stop();
        double bakeSec = sw.Elapsed.TotalSeconds;
        string dir = System.IO.Path.Combine(Path.GetTempPath(), "t46_" + Guid.NewGuid().ToString("N"));
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
        bad.FilePath = System.IO.Path.Combine(Path.GetTempPath(), "t45_missing.bin");
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
        string dir = System.IO.Path.Combine(Path.GetTempPath(), "t43_" + Guid.NewGuid().ToString("N"));
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

        string dir = Path.Combine(Path.GetTempPath(), "t59_" + Guid.NewGuid().ToString("N"));
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
        string dirC = Path.Combine(Path.GetTempPath(), "t59c_" + Guid.NewGuid().ToString("N"));
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
        string dir = System.IO.Path.Combine(Path.GetTempPath(), "t62_" + Guid.NewGuid().ToString("N"));
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
}
