using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    private static BodyBlueprint StudyBody(
        string name, int parent, double mu, double radius, double a, double e,
        double incDeg, double nodeDeg, double argpDeg, double meanDeg,
        double northY = 0d, double northZ = 1d)
    {
        return new BodyBlueprint
        {
            Name = name,
            ParentIndex = parent,
            StandardGravitationalParameter = mu,
            Radius = radius,
            SemiMajorAxis = a,
            Eccentricity = e,
            InclinationDegrees = incDeg,
            LongitudeOfAscendingNodeDegrees = nodeDeg,
            ArgumentOfPeriapsisDegrees = argpDeg,
            MeanAnomalyAtEpochDegrees = meanDeg,
            EpochTimeSeconds = 0d,
            NorthPoleY = northY,
            NorthPoleZ = northZ
        };
    }

    /// <summary>
    /// The user's system:
    ///   Sol (current scene), Terra (current scene) + Luna (~0.28 R_H),
    ///   Vesper (1.3 M_Terra at 0.7 a_Terra), Jove (0.4 M_Jupiter at 1.4 AU),
    ///   Ares (radius scaled like Terra from Mars, surface gravity = Mars).
    /// </summary>
    private static StarSystem StudyCustomSystem()
    {
        double terraMu = 1.28e13d;
        double terraR = 1.143e6d;
        double terraA = 5.51876e10d;

        // Ares: R = (R_Terra / R_Earth) * R_Mars; g = g_Mars -> M = g R^2 / G.
        double radiusScale = terraR / 6.371e6d;
        double aresR = radiusScale * 3.3895e6d;
        double aresG = 3.71d;
        // mu = g R^2 (not g R^2 / G: that is the mass, not the gravitational parameter).
        double aresMu = aresG * aresR * aresR;

        var bp = new SystemBlueprint();
        var sol = StudyBody("Sol", -1, 2.65433e19d, 2.345e8d, 0d, 0d, 0d, 0d, 0d, 0d);
        bp.Bodies.Add(sol);
        bp.Bodies.Add(StudyBody("Terra", 0, terraMu, terraR, terraA, 0.017d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Luna", 1, 1.5e11d, 3.1e5d, 0.85e8d, 0d, 5d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Vesper", 0, 1.664e13d, 1.247e6d, 0.7d * terraA, 0.01d, 2d, 40d, 0d, 60d));
        bp.Bodies.Add(StudyBody("Jove", 0, 5.06746e16d, 5.267e7d, 1.4d * StudyAU, 0.02d, 1.5d, 120d, 0d, 150d));
        bp.Bodies.Add(StudyBody("Ares", 0, aresMu, aresR, 2.2d * StudyAU, 0.05d, 1d, 200d, 30d, 250d));

        bp.Bodies[1].Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 2e6d,
            SeaLevelDensityKgPerCubicMeter = 1.2d,
            ScaleHeightMeters = 8500d
        };
        return bp.Build();
    }

    /// <summary>
    /// The report's 7-body real-solar fixture (common.py / ic7.cs).
    /// </summary>
    private static StarSystem StudyRealSolar7()
    {
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Sun", -1, 1.32712440018e20d, 6.96e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Earth", 0, 3.986004418e14d, 6.371e6d, 1.495978707e11d, 0.0167d, 0d, 0d, 102.9d, 100d));
        bp.Bodies.Add(StudyBody("Moon", 1, 4.9028e12d, 1.737e6d, 3.844e8d, 0.0549d, 5.145d, 125d, 318d, 135d));
        bp.Bodies.Add(StudyBody("Jupiter", 0, 1.26686534e17d, 6.9911e7d, 7.785e11d, 0.0489d, 1.303d, 100.5d, 273.9d, 20d));
        bp.Bodies.Add(StudyBody("Saturn", 0, 3.7931187e16d, 5.8232e7d, 1.4335e12d, 0.0565d, 2.485d, 113.7d, 339.4d, 317d));
        bp.Bodies.Add(StudyBody("Venus", 0, 3.24859e14d, 6.0518e6d, 1.0821e11d, 0.0068d, 3.39d, 76.7d, 54.9d, 50d));
        bp.Bodies.Add(StudyBody("Mars", 0, 4.282837e13d, 3.3895e6d, 2.2794e11d, 0.0934d, 1.85d, 49.6d, 286.5d, 19.4d));
        return bp.Build();
    }

    private static StudyNBody StudyMakeNBody(StarSystem sys)
    {
        int n = sys.AllBodies.Count;
        var nb = new StudyNBody
        {
            Names = new string[n],
            Mu = new double[n],
            P0 = new double[3 * n],
            V0 = new double[3 * n]
        };

        var index = new Dictionary<OrbitingBody, int>(n);
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = sys.AllBodies[i];
            index[body] = i;
            nb.Names[i] = body.Name;
            nb.Mu[i] = body.ResolveStandardGravitationalParameter();
            sys.EvaluateBodyState(body, 0d, out Vector3d p, out Vector3d v);
            nb.P0[3 * i] = p.X;
            nb.P0[(3 * i) + 1] = p.Y;
            nb.P0[(3 * i) + 2] = p.Z;
            nb.V0[3 * i] = v.X;
            nb.V0[(3 * i) + 1] = v.Y;
            nb.V0[(3 * i) + 2] = v.Z;
        }

        // Same hierarchy correction as EphemerisBaker.RunBake: the rails give each
        // child's subtree barycentre about the parent; the n-body must start from
        // the SAME rigidly shifted states, otherwise the Earth/Moon-phase error is
        // mistaken for an integrator error.
        double[] railP = (double[])nb.P0.Clone();
        double[] railV = (double[])nb.V0.Clone();
        for (int i = n - 1; i >= 0; i--)
        {
            OrbitingBody body = sys.AllBodies[i];
            if (body.Children.Count == 0 || nb.Mu[i] <= 0d)
            {
                continue;
            }

            double mSub = nb.Mu[i];
            double sx = 0d, sy = 0d, sz = 0d;
            double svx = 0d, svy = 0d, svz = 0d;
            for (int j = 0; j < body.Children.Count; j++)
            {
                OrbitingBody child = body.Children[j];
                int c = index[child];
                double mc = child.ResolveSubtreeStandardGravitationalParameter();
                mSub += mc;
                sx += mc * (railP[3 * c] - railP[3 * i]);
                sy += mc * (railP[(3 * c) + 1] - railP[(3 * i) + 1]);
                sz += mc * (railP[(3 * c) + 2] - railP[(3 * i) + 2]);
                svx += mc * (railV[3 * c] - railV[3 * i]);
                svy += mc * (railV[(3 * c) + 1] - railV[(3 * i) + 1]);
                svz += mc * (railV[(3 * c) + 2] - railV[(3 * i) + 2]);
            }

            StudyShiftSubtree(
                body, index, nb.P0, nb.V0,
                -sx / mSub, -sy / mSub, -sz / mSub,
                -svx / mSub, -svy / mSub, -svz / mSub);
        }

        double totalMass = 0d;
        double cpx = 0d, cpy = 0d, cpz = 0d;
        double cvx = 0d, cvy = 0d, cvz = 0d;
        for (int i = 0; i < n; i++)
        {
            totalMass += nb.Mu[i];
            cpx += nb.Mu[i] * nb.P0[3 * i];
            cpy += nb.Mu[i] * nb.P0[(3 * i) + 1];
            cpz += nb.Mu[i] * nb.P0[(3 * i) + 2];
            cvx += nb.Mu[i] * nb.V0[3 * i];
            cvy += nb.Mu[i] * nb.V0[(3 * i) + 1];
            cvz += nb.Mu[i] * nb.V0[(3 * i) + 2];
        }

        cpx /= totalMass;
        cpy /= totalMass;
        cpz /= totalMass;
        cvx /= totalMass;
        cvy /= totalMass;
        cvz /= totalMass;
        for (int i = 0; i < n; i++)
        {
            nb.P0[3 * i] -= cpx;
            nb.P0[(3 * i) + 1] -= cpy;
            nb.P0[(3 * i) + 2] -= cpz;
            nb.V0[3 * i] -= cvx;
            nb.V0[(3 * i) + 1] -= cvy;
            nb.V0[(3 * i) + 2] -= cvz;
        }

        return nb;
    }

    private static void StudyShiftSubtree(
        OrbitingBody body, Dictionary<OrbitingBody, int> index, double[] p, double[] v,
        double dx, double dy, double dz, double dvx, double dvy, double dvz)
    {
        int i = index[body];
        p[3 * i] += dx;
        p[(3 * i) + 1] += dy;
        p[(3 * i) + 2] += dz;
        v[3 * i] += dvx;
        v[(3 * i) + 1] += dvy;
        v[(3 * i) + 2] += dvz;
        for (int j = 0; j < body.Children.Count; j++)
        {
            StudyShiftSubtree(body.Children[j], index, p, v, dx, dy, dz, dvx, dvy, dvz);
        }
    }

}

