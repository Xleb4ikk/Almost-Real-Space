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
        // Two years: the force is probed at 365 d, and the last day of a one-year
        // bake is the Chebyshev horizon (TaylorChebyshev extrapolates, production
        // switches to the rail, so a horizon-edge mismatch is not a force bug).
        StudyBake("Force", real, nb, 2d, 2.5d);
        OrbitingBody earth = real.AllBodies[1];
        double mu = earth.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(earth, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        double worst = 0d;
        double scale = 0d;
        var times = new[]
        {
            t0, t0 + 86400d, t0 + (10d * 86400d),
            t0 + (64d * 86400d), t0 + (365d * 86400d)
        };
        foreach (double t in times)
        {
            Vector3d taylor = TaylorShip.EvaluateAcceleration(real, p0, t);
            // The production side must come from StarSystem itself, not from a
            // second copy of the formula: a formula bug present on both sides
            // (the old subtree-mu indirect term) cancels and the test passes.
            Vector3d total = real.EvaluateShipAccelerationCached(p0, t, includeOblateness: false);
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
                Check(rel < 1e-10d, "force far from planets", rel.ToString("E2", CultureInfo.InvariantCulture));
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "t={0:F0}: |a|={1:E3} m/s^2, abs diff={2:E3} rel diff={3:E2}",
                t, s, diff, rel));
        }

        Check(worst < 1e-10d, "Taylor force vs PhysicsSolver",
            worst.ToString("E2", CultureInfo.InvariantCulture));

        // Separate the force formula from the ephemeris evaluation: the Taylor
        // path builds body positions from TaylorChebyshev, production uses
        // EvaluateBodyState. A late-time mismatch here is not a force-model bug.
        foreach (double t in new[] { t0 + (64d * 86400d), t0 + (365d * 86400d) })
        {
            var world = new Vector3d[real.AllBodies.Count];
            double worstBody = 0d;
            string worstName = "none";
            for (int i = 0; i < real.AllBodies.Count; i++)
            {
                OrbitingBody body = real.AllBodies[i];
                if (body.Parent == null)
                {
                    world[i] = body.RootPosition;
                }
                else
                {
                    TaylorLocalState local = TaylorChebyshev.LocalState(body.Baked, t, 1);
                    int parent = -1;
                    for (int k = 0; k < real.AllBodies.Count; k++)
                    {
                        if (ReferenceEquals(real.AllBodies[k], body.Parent))
                        {
                            parent = k;
                            break;
                        }
                    }

                    world[i] = world[parent] + new Vector3d(
                        local.X.C[0], local.Y.C[0], local.Z.C[0]);
                }

                real.EvaluateBodyState(body, t, out Vector3d exact, out _);
                double d = (world[i] - exact).Magnitude;
                if (d > worstBody)
                {
                    worstBody = d;
                    worstName = body.Name;
                }

                if (body.Parent != null)
                {
                    Console.WriteLine("  body " + body.Name + " t="
                        + ((t - t0) / 86400d).ToString("F0", CultureInfo.InvariantCulture)
                        + " d: TaylorChebyshev vs EvaluateBodyState = "
                        + d.ToString("E3", CultureInfo.InvariantCulture) + " m");
                }
            }

            Check(worstBody < 1d, "Taylor ephemeris at "
                + ((t - t0) / 86400d).ToString("F0", CultureInfo.InvariantCulture) + " d",
                worstName + " " + worstBody.ToString("E3", CultureInfo.InvariantCulture) + " m < 1 m");

            // The dd force goes through LocalStateDd, which has its own recurrence.
            var worldDd = new Vector3d[real.AllBodies.Count];
            double worstDd = 0d;
            string worstDdName = "none";
            for (int i = 0; i < real.AllBodies.Count; i++)
            {
                OrbitingBody body = real.AllBodies[i];
                if (body.Parent == null)
                {
                    worldDd[i] = body.RootPosition;
                }
                else
                {
                    TaylorLocalStateDd local = TaylorChebyshev.LocalStateDd(
                        body.Baked, P1bTests.StudyDd.From(t), t, 1);
                    int parent = -1;
                    for (int k = 0; k < real.AllBodies.Count; k++)
                    {
                        if (ReferenceEquals(real.AllBodies[k], body.Parent))
                        {
                            parent = k;
                            break;
                        }
                    }

                    worldDd[i] = worldDd[parent] + new Vector3d(
                        local.X.C[0].ToDouble, local.Y.C[0].ToDouble, local.Z.C[0].ToDouble);
                }

                real.EvaluateBodyState(body, t, out Vector3d exact, out _);
                double d = (worldDd[i] - exact).Magnitude;
                if (d > worstDd)
                {
                    worstDd = d;
                    worstDdName = body.Name;
                }
            }

            Check(worstDd < 1d, "Taylor dd ephemeris at "
                + ((t - t0) / 86400d).ToString("F0", CultureInfo.InvariantCulture) + " d",
                worstDdName + " " + worstDd.ToString("E3", CultureInfo.InvariantCulture) + " m < 1 m");
        }

        // The local force is a different expression of the same physics: compare
        // its solar/Moon differential terms against the world force at the same
        // state. A systematic difference here propagates into every local-frame
        // reference and is not visible in the world-vs-world force check above.
        {
            OrbitingBody earthBody = real.AllBodies[1];
            double t = t0 + (64d * 86400d);
            real.EvaluateBodyState(earthBody, t0, out Vector3d ep0, out _);
            real.EvaluateBodyState(earthBody, t, out Vector3d ep, out _);
            Vector3d rel = p0 - ep0;
            Vector3d local = real.EvaluateShipAccelerationLocal(rel, earthBody, t);
            Vector3d worldRel =
                real.EvaluateShipAccelerationCached(ep + rel, t, includeOblateness: false)
                - real.EvaluateShipAccelerationCached(ep, t, includeOblateness: false);
            double d = (local - worldRel).Magnitude;
            Console.WriteLine("local vs world-relative force at 64 d: abs="
                + d.ToString("E3", CultureInfo.InvariantCulture) + " m/s^2, rel="
                + (d / Math.Max(worldRel.Magnitude, 1e-30d)).ToString("E2", CultureInfo.InvariantCulture));
            Check(d < 1e-12d, "local vs world-relative force",
                d.ToString("E3", CultureInfo.InvariantCulture) + " m/s^2 < 1E-12");

            Vector3d dd = TaylorShip.EvaluateAccelerationDd(real, p0, t);
            Vector3d production = real.EvaluateShipAccelerationCached(p0, t, includeOblateness: false);
            double ddDiff = (dd - production).Magnitude;
            Console.WriteLine("dd vs production force at 64 d: abs="
                + ddDiff.ToString("E3", CultureInfo.InvariantCulture) + " m/s^2, rel="
                + (ddDiff / Math.Max(production.Magnitude, 1e-30d)).ToString("E2", CultureInfo.InvariantCulture));
            Check(ddDiff < 1e-12d, "dd vs production force at 64 d",
                ddDiff.ToString("E3", CultureInfo.InvariantCulture) + " m/s^2 < 1E-12");
        }

        Console.WriteLine("--- Taylor force complete ---");
        return 0;
    }
}



