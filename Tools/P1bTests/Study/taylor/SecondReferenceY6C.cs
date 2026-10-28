using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test526_SecondReferenceY6CDd()
    {
        Console.WriteLine("--- Second reference: fixed-step Y6C in dd, h and h/2 ---");

        CheckY6CCase("30 d", 30d, 10d, 5d);

        Console.WriteLine("--- Second reference complete ---");
        return 0;
    }

    private static void CheckY6CCase(string label, double days, double coarseH, double fineH)
    {
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Y6C " + label, real, nb, days > 60d ? 1.1d : (60d / 365.25d), days > 60d ? 1d : 0.01d);

        string goldenFile = days > 60d ? "golden_real_1y_hi.json" : "golden_real.json";
        string goldenPath = Path.Combine("Study", "taylor", "golden", goldenFile);
        GoldFile golden = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(goldenPath));
        GoldCp g = null;
        foreach (GoldCp cp in golden.Checkpoints)
        {
            if (Math.Abs(cp.Days - days) < 1e-9d)
            {
                g = cp;
            }
        }

        Check(g != null, "Y6C golden checkpoint " + label, goldenPath);
        if (g == null)
        {
            return;
        }

        OrbitingBody body = real.AllBodies[1];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        StudyDd3 accDd = StudyForceDd(real, StudyDd3.From(p0), t0);
        Vector3d accDouble = TaylorShip.EvaluateAcceleration(real, p0, t0);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: force t0 dd={1} double={2} diff={3:E3} m/s2",
            label, accDd.ToVector3d(), accDouble,
            (accDd.ToVector3d() - accDouble).Magnitude));

        var oneStep = StudyShipY6CDd(real, p0, v0, t0, coarseH / 86400d, coarseH);
        var oneTaylor = TaylorShip.RunDdSeconds(
            real, p0, v0, t0, coarseH, 24, 1e-12d, 1e-15d, coarseH, 1e-6d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: one step h={1:F0}: |Y6C-Taylor|={2:E3} m",
            label, coarseH, (oneStep.p - oneTaylor.P).Magnitude));

        var coarse = StudyShipY6CDd(real, p0, v0, t0, days, coarseH);
        var fine = StudyShipY6CDd(real, p0, v0, t0, days, fineH);
        double self = (coarse.p - fine.p).Magnitude;
        double dp = GoldErr(WorldFix(fine.p).ToArray(), g.P);
        double dv = GoldErr(WorldFix(fine.v).ToArray(), g.V);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: Y6C dd h={1:F0}/{2:F0} self={3:E3} m; h={2:F0} vs golden dp={4:E3} m dv={5:E3} m/s",
            label, coarseH, fineH, self, dp, dv));
        Check(double.IsFinite(self) && double.IsFinite(dp) && double.IsFinite(dv),
            "Y6C " + label + " finite", "self/dp/dv");
        Check(self < 5d, "Y6C " + label + " self",
            self.ToString("E3", CultureInfo.InvariantCulture));
        Check(dp < 1d, "Y6C " + label + " vs golden",
            dp.ToString("E3", CultureInfo.InvariantCulture));
        StudyWriteCsv("Test526_SecondReferenceY6CDd.csv",
            "case,self_m,dp_m,dv_mps",
            new List<string> { StudyCsvRow(label, self, dp, dv) });
    }
}

