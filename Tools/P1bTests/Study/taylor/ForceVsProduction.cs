using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test519_ForceVsProduction()
    {
        Console.WriteLine("--- Taylor force vs production PhysicsSolver ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Force", real, nb, 1d);
        OrbitingBody earth = real.AllBodies[1];
        double mu = earth.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(earth, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        double worst = 0d;
        double scale = 0d;
        var times = new[] { t0, t0 + 86400d, t0 + (10d * 86400d) };
        foreach (double t in times)
        {
            Vector3d taylor = TaylorShip.EvaluateAcceleration(real, p0, t);
            Vector3d total = Vector3d.Zero;
            for (int i = 0; i < real.AllBodies.Count; i++)
            {
                OrbitingBody body = real.AllBodies[i];
                real.EvaluateBodyState(body, t, out Vector3d bpos, out _);
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    p0, bpos, body.ResolveStandardGravitationalParameter());
            }

            real.EvaluateBodyState(real.AllBodies[0], t, out Vector3d rootPos, out _);
            Vector3d rootAccel = Vector3d.Zero;
            foreach (OrbitingBody child in real.AllBodies[0].Children)
            {
                real.EvaluateBodyState(child, t, out Vector3d cpos, out _);
                rootAccel += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    rootPos, cpos, child.ResolveSubtreeStandardGravitationalParameter());
            }

            total -= rootAccel;
            double diff = (taylor - total).Magnitude;
            double s = total.Magnitude;
            double rel = diff / Math.Max(s, 1e-300d);
            worst = Math.Max(worst, rel);
            scale = Math.Max(scale, s);
            if (Math.Abs(t - t0) < 1e-9d)
            {
                Check(rel < 1e-10d, "force near-Earth", rel.ToString("E2", CultureInfo.InvariantCulture));
            }
            else
            {
                Check(rel < 1e-14d, "force far from planets", rel.ToString("E2", CultureInfo.InvariantCulture));
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "t={0:F0}: |a|={1:E3} m/s^2, rel diff={2:E2}",
                t, s, rel));
        }

        Check(worst < 1e-10d, "Taylor force vs PhysicsSolver",
            worst.ToString("E2", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Taylor force complete ---");
        return 0;
    }
}


