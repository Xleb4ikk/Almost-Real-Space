using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static BigInteger3State WorldFix(Vector3d p)
    {
        return new BigInteger3State(DblFix(p.X), DblFix(p.Y), DblFix(p.Z));
    }

    private readonly struct BigInteger3State
    {
        public readonly System.Numerics.BigInteger X;
        public readonly System.Numerics.BigInteger Y;
        public readonly System.Numerics.BigInteger Z;

        public BigInteger3State(System.Numerics.BigInteger x, System.Numerics.BigInteger y, System.Numerics.BigInteger z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public System.Numerics.BigInteger[] ToArray()
        {
            return new[] { X, Y, Z };
        }
    }

    private static double LocalGoldErr(Vector3d p, string[] golden)
    {
        return GoldErr(WorldFix(p).ToArray(), golden);
    }

    private static double DdGoldErr(TaylorShipDdResult r, string[] golden)
    {
        return GoldErr(new[]
        {
            DblFix(r.DdPx.Hi) + DblFix(r.DdPx.Lo),
            DblFix(r.DdPy.Hi) + DblFix(r.DdPy.Lo),
            DblFix(r.DdPz.Hi) + DblFix(r.DdPz.Lo)
        }, golden);
    }

    private static double DdGoldErrV(TaylorShipDdResult r, string[] golden)
    {
        return GoldErr(new[]
        {
            DblFix(r.DdVx.Hi) + DblFix(r.DdVx.Lo),
            DblFix(r.DdVy.Hi) + DblFix(r.DdVy.Lo),
            DblFix(r.DdVz.Hi) + DblFix(r.DdVz.Lo)
        }, golden);
    }

    private static int Test516_DdRealBudget()
    {
        Console.WriteLine("--- Real baked system: double vs dd vs mp golden ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Dd budget", real, nb, 60d / 365.25d, 0.01d);

        string goldenPath = Path.Combine("Study", "taylor", "golden", "golden_real.json");
        GoldFile golden = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(goldenPath));
        GoldCp gOne = null;
        GoldCp gThirty = null;
        foreach (GoldCp cp in golden.Checkpoints)
        {
            if (Math.Abs(cp.Days - 1d) < 1e-9d) gOne = cp;
            if (Math.Abs(cp.Days - 30d) < 1e-9d) gThirty = cp;
        }

        Check(gOne != null && gThirty != null, "real golden checkpoints", goldenPath);
        if (gOne == null || gThirty == null)
        {
            return 0;
        }

        OrbitingBody body = real.AllBodies[1];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        const double days = 30d;
        double T = t0 + (days * 86400d);
        real.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;
        real.EvaluateBodyState(body, T, out Vector3d bpT, out _);

        GoldCp gYear = null;
        Task<TaylorShipDdResult> yearTask = null;
        Task<TaylorShipDdResult> yearTightTask = null;
        string goldenYearPath = Path.Combine("Study", "taylor", "golden", "golden_real_1y_hi.json");
        if (File.Exists(goldenYearPath))
        {
            GoldFile goldenYear = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(goldenYearPath));
            foreach (GoldCp cp in goldenYear.Checkpoints)
            {
                if (Math.Abs(cp.Days - 365d) < 1e-9d) gYear = cp;
            }

            Check(gYear != null, "real golden 1y checkpoint", goldenYearPath);
            if (gYear != null)
            {
                StarSystem realYear = StudyRealSolar7();
                StudyNBody nbYear = StudyMakeNBody(realYear);
                StudyBake("Dd budget 1y", realYear, nbYear, 1.1d, 1d);
                OrbitingBody bodyYear = realYear.AllBodies[1];
                double muYear = bodyYear.ResolveStandardGravitationalParameter();
                realYear.EvaluateBodyState(bodyYear, t0, out Vector3d bpYear, out Vector3d bvYear);
                StudyShipOrbit(6.771e6d, 4.6371e7d, muYear, out Vector3d relPYear, out Vector3d relVYear);
                var p0Year = bpYear + relPYear;
                var v0Year = bvYear + relVYear;
                yearTask = Task.Run(() => TaylorShip.RunDdSeconds(
                    realYear, p0Year, v0Year, t0, 365d * 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d));
                yearTightTask = Task.Run(() => TaylorShip.RunDdSeconds(
                    realYear, p0Year, v0Year, t0, 365d * 86400d, 24, 1e-12d, 1e-15d, 900d, 1e-3d));
            }
        }
        else
        {
            Console.WriteLine("1y golden not present; run Test518 and mp_golden_real.py --checkpoints 365.");
        }

        var refFine = StudyShipRefLocal(real, 1, relP, relV, t0, days, 0.625d);
        var refFiner = StudyShipRefLocal(real, 1, relP, relV, t0, days, 0.3125d);
        double refSelf = (refFine.p - refFiner.p).Magnitude;

        var refOne = StudyShipRefLocal(real, 1, relP, relV, t0, 1d, 1.25d);
        real.EvaluateBodyState(body, t0 + 86400d, out Vector3d bp1, out _);
        TaylorShipResult doubleOne = null;
        TaylorShipDdResult ddOne = null;
        Parallel.Invoke(
            () =>
            {
                doubleOne = TaylorShip.RunSeconds(
                    real, p0, v0, t0, 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
            },
            () =>
            {
                ddOne = TaylorShip.RunDdSeconds(
                    real, p0, v0, t0, 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
            });

        double goldDp1 = double.Parse(gOne.Err["dp"], CultureInfo.InvariantCulture);
        double goldDv1 = double.Parse(gOne.Err["dv"], CultureInfo.InvariantCulture);
        double oneDoubleP = GoldErr(WorldFix(doubleOne.P).ToArray(), gOne.P);
        double oneDoubleV = GoldErr(WorldFix(doubleOne.V).ToArray(), gOne.V);
        double oneDdP = DdGoldErr(ddOne, gOne.P);
        double oneDdV = DdGoldErrV(ddOne, gOne.V);
        double oneLocalP = LocalGoldErr(refOne.p, gOne.RelP);
        double localOneContext = (doubleOne.P - bp1 - refOne.p).Magnitude;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "1 day: double={0:E2}/{1:E2}, dd={2:E2}/{3:E2}, localDOP={4:E2}, (local context {5:E2}), golden={6:E2}/{7:E2}",
            oneDoubleP, oneDoubleV, oneDdP, oneDdV, oneLocalP, localOneContext, goldDp1, goldDv1));
        CheckReferenceMargin("double-world 1d", oneDoubleP, goldDp1);
        CheckReferenceMargin("dd 1d", oneDdP, goldDp1);
        CheckReferenceMargin("local DOP 1d", oneLocalP, goldDp1);
        CheckReferenceMargin("double-world v 1d", oneDoubleV, goldDv1);
        CheckReferenceMargin("dd v 1d", oneDdV, goldDv1);
        Check(oneDdP < 1e-6d, "dd 1d absolute dp", oneDdP.ToString("E2", CultureInfo.InvariantCulture));
        Check(oneDdV < 1e-9d, "dd 1d absolute dv", oneDdV.ToString("E2", CultureInfo.InvariantCulture));

        TaylorShipResult doubleRun = null;
        TaylorShipDdResult ddRun = null;
        TaylorShipDdResult ddTight = null;
        Parallel.Invoke(
            () =>
            {
                doubleRun = TaylorShip.RunSeconds(
                    real, p0, v0, t0, days * 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
            },
            () =>
            {
                ddRun = TaylorShip.RunDdSeconds(
                    real, p0, v0, t0, days * 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
            },
            () =>
            {
                ddTight = TaylorShip.RunDdSeconds(
                    real, p0, v0, t0, days * 86400d, 24, 1e-12d, 1e-15d, 1800d, 1e-3d);
            });
        double goldDp = double.Parse(gThirty.Err["dp"], CultureInfo.InvariantCulture);
        double goldDv = double.Parse(gThirty.Err["dv"], CultureInfo.InvariantCulture);
        double doubleP = GoldErr(WorldFix(doubleRun.P).ToArray(), gThirty.P);
        double doubleV = GoldErr(WorldFix(doubleRun.V).ToArray(), gThirty.V);
        double ddP = DdGoldErr(ddRun, gThirty.P);
        double ddV = DdGoldErrV(ddRun, gThirty.V);
        double ddTightP = DdGoldErr(ddTight, gThirty.P);
        double ddTightV = DdGoldErrV(ddTight, gThirty.V);
        double localP = LocalGoldErr(refFine.p, gThirty.RelP);
        double localContext = ((doubleRun.P - bpT) - refFine.p).Magnitude;
        double budget = (doubleRun.P - ddRun.P).Magnitude;
        double budgetV = (doubleRun.V - ddRun.V).Magnitude;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "30 d: double={0:E2}/{1:E2}, dd={2:E2}/{3:E2}, localDOP={4:E2}, (local context {5:E2}), golden={6:E2}/{7:E2}",
            doubleP, doubleV, ddP, ddV, localP, localContext, goldDp, goldDv));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "double-vs-dd={0:E3} m ({1:E3} m/s), local ref self={2:E3} m",
            budget, budgetV, refSelf));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "30 d tight dd (tol 1e-12/1e-15, hmax 1800): dp={0:E2} dv={1:E2}, steps={2}",
            ddTightP, ddTightV, ddTight.Steps));
        CheckReferenceMargin("double-world 30d", doubleP, goldDp);
        CheckReferenceMargin("dd 30d", ddP, goldDp);
        CheckReferenceMargin("dd 30d tight", ddTightP, goldDp);
        CheckReferenceMargin("local DOP 30d", localP, goldDp);
        CheckReferenceMargin("double-world v 30d", doubleV, goldDv);
        CheckReferenceMargin("dd v 30d", ddV, goldDv);
        Check(ddP < 1e-4d, "dd 30d absolute dp", ddP.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddV < 1e-7d, "dd 30d absolute dv", ddV.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddTightP < 1e-8d, "dd 30d tight absolute dp", ddTightP.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddTightV < 1e-11d, "dd 30d tight absolute dv", ddTightV.ToString("E2", CultureInfo.InvariantCulture));
        Check(double.IsFinite(budget) && double.IsFinite(budgetV), "double-vs-dd finite", "budget");

        if (gYear != null)
        {
            TaylorShipDdResult ddYear = yearTask.Result;
            TaylorShipDdResult ddYearTight = yearTightTask.Result;
            double ddYearP = DdGoldErr(ddYear, gYear.P);
            double ddYearV = DdGoldErrV(ddYear, gYear.V);
            double ddYearTightP = DdGoldErr(ddYearTight, gYear.P);
            double ddYearTightV = DdGoldErrV(ddYearTight, gYear.V);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "1 y: dd={0:E2}/{1:E2} ({2} steps), tight dd (hmax 900)={3:E2}/{4:E2} ({5} steps), golden=K28 tol 1e-20 dps 40",
                ddYearP, ddYearV, ddYear.Steps, ddYearTightP, ddYearTightV, ddYearTight.Steps));
            Check(ddYearP < 1e-2d, "dd 1y absolute dp", ddYearP.ToString("E2", CultureInfo.InvariantCulture));
            Check(ddYearV < 1e-6d, "dd 1y absolute dv", ddYearV.ToString("E2", CultureInfo.InvariantCulture));
            Check(ddYearTightP < 1e-6d, "dd 1y tight absolute dp", ddYearTightP.ToString("E2", CultureInfo.InvariantCulture));
            Check(ddYearTightV < 1e-9d, "dd 1y tight absolute dv", ddYearTightV.ToString("E2", CultureInfo.InvariantCulture));

            StudyWriteCsv("Test516_DdRealBudget.csv",
                "case,dp_m,dv_mps,steps",
                new List<string>
                {
                    StudyCsvRow("1d dd", oneDdP, oneDdV, ddOne.Steps),
                    StudyCsvRow("30d double", doubleP, doubleV, doubleRun.Steps),
                    StudyCsvRow("30d dd", ddP, ddV, ddRun.Steps),
                    StudyCsvRow("30d tight dd", ddTightP, ddTightV, ddTight.Steps),
                    StudyCsvRow("1y dd", ddYearP, ddYearV, ddYear.Steps),
                    StudyCsvRow("1y tight dd", ddYearTightP, ddYearTightV, ddYearTight.Steps)
                });
        }

        Console.WriteLine("--- Real system dd budget complete ---");
        return 0;
    }
}

