using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private sealed class ExportBody
    {
        public string Name { get; set; }
        public int Parent { get; set; }
        public double Mu { get; set; }
        public double SubtreeMu { get; set; }
        public double T0 { get; set; }
        public double SegmentLength { get; set; }
        public int Degree { get; set; }
        public int Segments { get; set; }
        public double[][] Coeffs { get; set; }
    }

    private sealed class ExportData
    {
        public double T0 { get; set; }
        public double Days { get; set; }
        public int Central { get; set; }
        public double[] ShipP0 { get; set; }
        public double[] ShipV0 { get; set; }
        public double[] RelP { get; set; }
        public double[] RelV { get; set; }
        public ExportBody[] Bodies { get; set; }
    }

    private static void ExportRealBake(
        string file, double bakeYears, double days, double worstGateMeters)
    {
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Export " + file, real, nb, bakeYears, worstGateMeters);

        OrbitingBody central = real.AllBodies[1];
        double mu = central.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(central, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        var index = new Dictionary<OrbitingBody, int>();
        for (int i = 0; i < real.AllBodies.Count; i++)
        {
            index[real.AllBodies[i]] = i;
        }

        var bodies = new List<ExportBody>();
        foreach (OrbitingBody body in real.AllBodies)
        {
            int stride = body.Baked != null ? BakedEphemeris.AxisStride(body.Baked.Degree) : 0;
            int segments = body.Baked != null ? body.Baked.SegmentCount : 0;
            var coeffs = new double[segments][];
            for (int s = 0; s < segments; s++)
            {
                coeffs[s] = new double[stride];
                Array.Copy(body.Baked.Coeffs, s * stride, coeffs[s], 0, stride);
            }

            bodies.Add(new ExportBody
            {
                Name = body.Name,
                Parent = body.Parent == null ? -1 : index[body.Parent],
                Mu = body.ResolveStandardGravitationalParameter(),
                SubtreeMu = body.ResolveSubtreeStandardGravitationalParameter(),
                T0 = body.Baked != null ? body.Baked.T0Seconds : 0d,
                SegmentLength = body.Baked != null ? body.Baked.SegmentLengthSeconds : 0d,
                Degree = body.Baked != null ? body.Baked.Degree : 0,
                Segments = segments,
                Coeffs = coeffs
            });
        }

        var data = new ExportData
        {
            T0 = t0,
            Days = days,
            Central = 1,
            ShipP0 = new[] { p0.X, p0.Y, p0.Z },
            ShipV0 = new[] { v0.X, v0.Y, v0.Z },
            RelP = new[] { relP.X, relP.Y, relP.Z },
            RelV = new[] { relV.X, relV.Y, relV.Z },
            Bodies = bodies.ToArray()
        };
        string path = Path.Combine("Study", "taylor", "golden", file);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("written: " + Path.GetFullPath(path));
        Check(File.Exists(path), "ephemeris export", path);
    }

    private static int Test518_EphemerisExport()
    {
        Console.WriteLine("--- Export baked ephemeris for the real-system oracle ---");
        // The export is rewritten by this test; these hashes are the current
        // deterministic bake output. Independent check: StudyBake gates the bake
        // itself against Y6C (0.24-0.36 m, the PASS lines above). If the Python
        // oracle is reused, regenerate golden_real*.json from these exports.
        ExportRealBake("real_bake_export.json", 60d / 365.25d, 30d, 0.01d);
        GoldenRequireFile(
            Path.Combine("Study", "taylor", "golden", "real_bake_export.json"),
            "B57345239602AE658CEFD7A251AA8364E80642C95B07A3E21E6A6C4A6AD14C01");
        ExportRealBake("real_bake_export_1y.json", 1.1d, 365d, 1d);
        GoldenRequireFile(
            Path.Combine("Study", "taylor", "golden", "real_bake_export_1y.json"),
            "A99A4D8EE69FF01337D00BFE866310DA03CD0393CAFDCAF196D64A696A9E9EB6");
        ExportRealBake("real_bake_export_10y.json", 10.1d, 3650d, 20d);
        GoldenRequireFile(
            Path.Combine("Study", "taylor", "golden", "real_bake_export_10y.json"),
            "97499DE926E75DECCD290FF77A81F4301666B70B6B79F3EA5A93B9F02C937DFA");
        Console.WriteLine("--- Export complete ---");
        return 0;
    }
}
