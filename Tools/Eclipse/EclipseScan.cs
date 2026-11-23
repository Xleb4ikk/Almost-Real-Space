using System;
using System.Text;
using UnityEngine;
using Galilego.Core;
using Galilego.Universe;

public static class EclipseScan
{
    private const double Day = 86400d;

    public static void Main()
    {
        var authoring = UnityEngine.Object.FindAnyObjectByType<StarSystemAuthoring>();
        if (authoring == null)
        {
            Debug.LogError("[EclipseScan] no StarSystemAuthoring");
            return;
        }

        StarSystem system = authoring.BuildBlueprint().Build();
        OrbitingBody sol = system.Root;
        OrbitingBody terra = Find(system, "Terra");
        OrbitingBody moon = Find(system, "Артемида") ?? Find(system, "Artemis");
        if (terra == null || moon == null)
        {
            Debug.LogError("[EclipseScan] Terra/Moon not found");
            return;
        }

        double moonPeriod = 2d * Math.PI * Math.Sqrt(Math.Pow(moon.SemiMajorAxis, 3d)
            / (terra.ResolveStandardGravitationalParameter() + moon.ResolveStandardGravitationalParameter()));
        double terraPeriod = 2d * Math.PI * Math.Sqrt(Math.Pow(terra.SemiMajorAxis, 3d)
            / (sol.ResolveStandardGravitationalParameter() + terra.ResolveSubtreeStandardGravitationalParameter()));
        Debug.Log(string.Format("[EclipseScan] P_moon={0:F3} d, P_terra={1:F3} d",
            moonPeriod / Day, terraPeriod / Day));

        var sb = new StringBuilder();
        double horizon = terraPeriod * 2d;
        double step = 600d; // 10 min
        double minTerra = 1d;
        double minTerraTime = 0d;
        double minMoon = 1d;
        double minMoonTime = 0d;

        for (double t = 0d; t <= horizon; t += step)
        {
            terra.EvaluateWorldState(t, out Vector3d terraPos, out _);
            moon.EvaluateWorldState(t, out Vector3d moonPos, out _);
            sol.EvaluateWorldState(t, out Vector3d solPos, out _);

            double vt = Visibility(terraPos, terra.Radius, moonPos, moon.Radius, solPos, sol.Radius);
            double vm = Visibility(moonPos, moon.Radius, terraPos, terra.Radius, solPos, sol.Radius);

            if (vt < minTerra) { minTerra = vt; minTerraTime = t; }
            if (vm < minMoon) { minMoon = vm; minMoonTime = t; }

            if (vt < 0.9999d || vm < 0.9999d)
            {
                sb.AppendLine(string.Format("[EclipseScan] t={0:F2} d: f_terra={1:F6} f_moon={2:F6}",
                    t / Day, vt, vm));
            }
        }

        Debug.Log("[EclipseScan] min f_terra=" + minTerra.ToString("F6")
            + " at t=" + (minTerraTime / Day).ToString("F3") + " d");
        Debug.Log("[EclipseScan] min f_moon=" + minMoon.ToString("F6")
            + " at t=" + (minMoonTime / Day).ToString("F3") + " d");

        // Surface detail at the two deepest events.
        ScanSurface(sol, terra, moon, minTerraTime, true);
        ScanSurface(sol, terra, moon, minMoonTime, false);

        Debug.Log("[EclipseScan] events:\n" + sb.ToString());
    }

    private static OrbitingBody Find(StarSystem system, string name)
    {
        foreach (OrbitingBody b in system.AllBodies)
        {
            if (b.Name == name) return b;
        }
        return null;
    }

    private static double Visibility(Vector3d observer, double observerRadius,
        Vector3d occluder, double occluderRadius, Vector3d star, double starRadius)
    {
        Vector3d toStar = star - observer;
        Vector3d toOcc = occluder - observer;
        double ds = toStar.Magnitude;
        double dOcc = toOcc.Magnitude;
        if (!(ds > 0d) || !(dOcc > 0d)) return 1d;
        double alpha = Math.Asin(Math.Min(starRadius / ds, 1d));
        double occ = Math.Asin(Math.Min(occluderRadius / dOcc, 1d));
        double sep = Math.Acos(Math.Max(-1d, Math.Min(1d,
            Vector3d.Dot(toStar, toOcc) / (ds * dOcc))));
        return StarPhysics.EclipseVisibleFraction(alpha, occ, sep);
    }

    private static void ScanSurface(OrbitingBody sol, OrbitingBody terra, OrbitingBody moon,
        double time, bool onTerra)
    {
        terra.EvaluateWorldState(time, out Vector3d terraPos, out _);
        moon.EvaluateWorldState(time, out Vector3d moonPos, out _);
        sol.EvaluateWorldState(time, out Vector3d solPos, out _);

        OrbitingBody body = onTerra ? terra : moon;
        OrbitingBody occ = onTerra ? moon : terra;

        Vector3d bodyPos = onTerra ? terraPos : moonPos;
        Vector3d occPos = onTerra ? moonPos : terraPos;
        Vector3d toOcc = (occPos - bodyPos);
        double occDistance = toOcc.Magnitude;
        Vector3d centerDir = toOcc / occDistance;

        // Sample directions within 1.2 * (sun angular radius + occluder radius) of
        // the sub-occluder point.
        double sunAng = Math.Asin(sol.Radius / (solPos - bodyPos).Magnitude);
        double occAng = Math.Asin(Math.Min(occ.Radius / occDistance, 1d));
        double span = Math.Min(0.06d, 1.2d * (sunAng + occAng));

        Vector3d tangent = Vector3d.Cross(centerDir, new Vector3d(0d, 0d, 1d));
        if (tangent.SqrMagnitude < 1e-9d) tangent = Vector3d.Cross(centerDir, new Vector3d(0d, 1d, 0d));
        tangent = tangent.Normalized;
        Vector3d bitangent = Vector3d.Cross(centerDir, tangent).Normalized;

        double min = 1d;
        double minOffset = 0d;
        int n = 24;
        for (int i = -n; i <= n; i++)
        {
            for (int j = -n; j <= n; j++)
            {
                double u = span * i / n;
                double v = span * j / n;
                Vector3d dir = (centerDir + (tangent * u) + (bitangent * v)).Normalized;
                Vector3d p = bodyPos + (dir * body.Radius);
                double vis = Visibility(p, body.Radius, occPos, occ.Radius, solPos, sol.Radius);
                if (vis < min)
                {
                    min = vis;
                    minOffset = Math.Sqrt((u * u) + (v * v));
                }
            }
        }

        Debug.Log(string.Format(
            "[EclipseScan] surface {0}: min f={1:F6} at offset {2:F4}° (center f={3:F6}, sun R={4:F4}°, occ R={5:F4}°)",
            onTerra ? "Terra" : "Moon", min, minOffset * 180d / Math.PI,
            Visibility(bodyPos, body.Radius, occPos, occ.Radius, solPos, sol.Radius),
            sunAng * 180d / Math.PI, occAng * 180d / Math.PI));
    }
}
