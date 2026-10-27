using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
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
        StudyBake("Dd budget", real, nb, 60d / 365.25d);

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
        var refFine = StudyShipRefLocal(real, 1, relP, relV, t0, days, 0.625d);
        var refFiner = StudyShipRefLocal(real, 1, relP, relV, t0, days, 0.3125d);
        double refSelf = (refFine.p - refFiner.p).Magnitude;

        var refOne = StudyShipRefLocal(real, 1, relP, relV, t0, 1d, 1.25d);
        real.EvaluateBodyState(body, t0 + 86400d, out Vector3d bp1, out _);
        var doubleOne = TaylorShip.RunSeconds(
            real, p0, v0, t0, 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
        var ddOne = TaylorShip.RunDdSeconds(
            real, p0, v0, t0, 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);

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

        var doubleRun = TaylorShip.RunSeconds(
            real, p0, v0, t0, days * 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
        var ddRun = TaylorShip.RunDdSeconds(
            real, p0, v0, t0, days * 86400d, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
        double goldDp = double.Parse(gThirty.Err["dp"], CultureInfo.InvariantCulture);
        double goldDv = double.Parse(gThirty.Err["dv"], CultureInfo.InvariantCulture);
        double doubleP = GoldErr(WorldFix(doubleRun.P).ToArray(), gThirty.P);
        double doubleV = GoldErr(WorldFix(doubleRun.V).ToArray(), gThirty.V);
        double ddP = DdGoldErr(ddRun, gThirty.P);
        double ddV = DdGoldErrV(ddRun, gThirty.V);
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
        CheckReferenceMargin("double-world 30d", doubleP, goldDp);
        CheckReferenceMargin("dd 30d", ddP, goldDp);
        CheckReferenceMargin("local DOP 30d", localP, goldDp);
        CheckReferenceMargin("double-world v 30d", doubleV, goldDv);
        CheckReferenceMargin("dd v 30d", ddV, goldDv);
        Check(double.IsFinite(budget) && double.IsFinite(budgetV), "double-vs-dd finite", "budget");
        Console.WriteLine("--- Real system dd budget complete ---");
        return 0;
    }
}
