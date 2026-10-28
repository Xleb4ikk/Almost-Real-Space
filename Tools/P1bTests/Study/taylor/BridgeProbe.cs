using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test523_BakeGoldenBridge()
    {
        Console.WriteLine("--- Bake <-> golden export bridge ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Bridge", real, nb, 1.1d, 1d);

        string path = Path.Combine("Study", "taylor", "golden", "real_bake_export_1y.json");
        GoldenRequireFile(path, "43E944C19ECC5ADD7BDE51A2D3CC1B105C940F3AAA976BB9D9DF74F9D19760D1");
        ExportData data = JsonSerializer.Deserialize<ExportData>(File.ReadAllText(path));
        Console.WriteLine("export T0={0}, Days={1}, bodies={2}",
            data.T0.ToString("R", CultureInfo.InvariantCulture),
            data.Days.ToString("R", CultureInfo.InvariantCulture),
            data.Bodies.Length);

        double t0 = StudyShipT0;
        double[] samples = { t0, t0 + 86400d, t0 + (30d * 86400d), t0 + (365d * 86400d) };
        double worstRaw = 0d;
        double worstPos = 0d;
        double worstVel = 0d;
        BakedEphemeris exportedEarth = null;
        for (int i = 0; i < real.AllBodies.Count; i++)
        {
            OrbitingBody body = real.AllBodies[i];
            ExportBody eb = data.Bodies[i];
            if (body.Baked == null)
            {
                Console.WriteLine("body {0}: no bake", body.Name);
                continue;
            }

            double raw = 0d;
            int stride = BakedEphemeris.AxisStride(body.Baked.Degree);
            double[] flat = new double[eb.Segments * stride];
            for (int s = 0; s < eb.Segments; s++)
            {
                Array.Copy(eb.Coeffs[s], 0, flat, s * stride, stride);
            }

            if (flat.Length == body.Baked.Coeffs.Length)
            {
                for (int k = 0; k < flat.Length; k++)
                {
                    raw = Math.Max(raw, Math.Abs(flat[k] - body.Baked.Coeffs[k]));
                }
            }
            else
            {
                raw = double.PositiveInfinity;
            }

            var exported = new BakedEphemeris
            {
                T0Seconds = eb.T0,
                SegmentLengthSeconds = eb.SegmentLength,
                Degree = eb.Degree,
                SegmentCount = eb.Segments,
                Coeffs = flat
            };
            if (body.Name == "Earth")
            {
                exportedEarth = exported;
            }

            double pos = 0d;
            double vel = 0d;
            foreach (double t in samples)
            {
                bool okA = body.Baked.TryEvaluate(t, out Vector3d pa, out Vector3d va);
                bool okB = exported.TryEvaluate(t, out Vector3d pb, out Vector3d vb);
                if (!okA || !okB)
                {
                    pos = double.PositiveInfinity;
                    vel = double.PositiveInfinity;
                    break;
                }

                pos = Math.Max(pos, (pa - pb).Magnitude);
                vel = Math.Max(vel, (va - vb).Magnitude);
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "body {0,-7} segs={1}/{2} L={3:R}/{4:R} deg={5}/{6} T0={7:R}/{8:R} maxCoeffDiff={9:E2} posDiff={10:E2} m velDiff={11:E2} m/s",
                body.Name, body.Baked.SegmentCount, eb.Segments,
                body.Baked.SegmentLengthSeconds, eb.SegmentLength,
                body.Baked.Degree, eb.Degree,
                body.Baked.T0Seconds, eb.T0,
                raw, pos, vel));
            worstRaw = Math.Max(worstRaw, raw);
            worstPos = Math.Max(worstPos, pos);
            worstVel = Math.Max(worstVel, vel);
        }

        string goldenPath = Path.Combine("Study", "taylor", "golden", "golden_real_1y_hi.json");
        GoldenRequireFile(goldenPath, "7582663FBAF275C2076B9D411DFB91B9833FC8683AD422FF3793C39BD6ACBC4E");
        GoldFile golden = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(goldenPath));
        GoldCp gYear = null;
        foreach (GoldCp cp in golden.Checkpoints)
        {
            if (Math.Abs(cp.Days - 365d) < 1e-9d)
            {
                gYear = cp;
            }
        }

        int badBoundaries = 0;
        int boundaryCount = 0;
        foreach (OrbitingBody body in real.AllBodies)
        {
            if (body.Baked == null)
            {
                continue;
            }

            for (int k = 1; k < body.Baked.SegmentCount; k++)
            {
                double boundary = k * body.Baked.SegmentLengthSeconds;
                boundaryCount++;
                if (body.Baked.SegmentIndex(boundary) != k)
                {
                    badBoundaries++;
                }
            }
        }

        Check(badBoundaries == 0, "segment index at k*L",
            badBoundaries.ToString(CultureInfo.InvariantCulture) + " bad of " +
            boundaryCount.ToString(CultureInfo.InvariantCulture));

        if (exportedEarth != null)
        {
            exportedEarth.TryEvaluate(t0, out Vector3d central0, out Vector3d centralV0);
            var ship0 = new Vector3d(data.ShipP0[0], data.ShipP0[1], data.ShipP0[2]);
            var rel0 = new Vector3d(data.RelP[0], data.RelP[1], data.RelP[2]);
            var shipV0 = new Vector3d(data.ShipV0[0], data.ShipV0[1], data.ShipV0[2]);
            var relV0 = new Vector3d(data.RelV[0], data.RelV[1], data.RelV[2]);
            double initPos = ((ship0 - rel0) - central0).Magnitude;
            double initVel = ((shipV0 - relV0) - centralV0).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "export initial state mismatch: |(ShipP0-RelP)-central(t0)|={0:E3} m, vel={1:E3} m/s",
                initPos, initVel));
        }

        Check(gYear != null && gYear.CentralP != null, "golden central checkpoint", goldenPath);
        if (gYear != null && gYear.CentralP != null && exportedEarth != null)
        {
            double tYear = t0 + (365d * 86400d);
            exportedEarth.TryEvaluate(tYear, out Vector3d central, out _);
            double centralErr = GoldErr(
                new[] { DblFix(central.X), DblFix(central.Y), DblFix(central.Z) },
                gYear.CentralP);
            double relCentral = (central - new Vector3d(
                double.Parse(gYear.CentralP[0], CultureInfo.InvariantCulture),
                double.Parse(gYear.CentralP[1], CultureInfo.InvariantCulture),
                double.Parse(gYear.CentralP[2], CultureInfo.InvariantCulture))).Magnitude;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "golden central @365 vs export: exact-dec={0:E3} m, double-dec={1:E3} m",
                centralErr, relCentral));
            Check(centralErr < 1e-3d, "golden central 1y",
                centralErr.ToString("E3", CultureInfo.InvariantCulture));
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "bridge worst: coeff={0:E3} pos={1:E3} m vel={2:E3} m/s",
            worstRaw, worstPos, worstVel));
        Console.WriteLine("--- Bake <-> golden export bridge complete ---");
        return 0;
    }
}

