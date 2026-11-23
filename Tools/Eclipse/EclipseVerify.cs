using System;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

public static class EclipseVerify
{
    private const double Day = 86400d;

    public static void Main()
    {
        var authoring = UnityEngine.Object.FindAnyObjectByType<StarSystemAuthoring>();
        StarSystem system = authoring.BuildBlueprint().Build();
        OrbitingBody sol = system.Root;
        OrbitingBody terra = Find(system, "Terra");
        OrbitingBody moon = Find(system, "Артемида");
        if (terra == null || moon == null)
        {
            Debug.LogError("[EclipseVerify] Terra/Artemis missing");
            return;
        }

        // 1. Clear sky at the epoch for both bodies.
        double clearTerra = system.StarVisibilityForBody(terra, 0d, out OrbitingBody occTerra);
        double clearMoon = system.StarVisibilityForBody(moon, 0d, out OrbitingBody occMoon);
        Debug.Log(string.Format("[EclipseVerify] t=0: terra={0:F6} occ={1}, moon={2:F6} occ={3}",
            clearTerra, Name(occTerra), clearMoon, Name(occMoon)));

        // 2. Solar eclipse from Terra (the Moon transits the Sun) at the minimum
        //    found by EclipseScan: t = 88.743 d.
        double solar = system.StarVisibilityForBody(terra, 88.743d * Day, out OrbitingBody solarOcc);
        Debug.Log(string.Format("[EclipseVerify] solar t=88.743d: terra={0:F6} occ={1}",
            solar, Name(solarOcc)));

        // Surface point near the sub-lunar point, same time.
        terra.EvaluateWorldState(88.743d * Day, out Vector3d terraPos, out _);
        moon.EvaluateWorldState(88.743d * Day, out Vector3d moonPos, out _);
        sol.EvaluateWorldState(88.743d * Day, out Vector3d solPos, out _);
        Vector3d toMoon = (moonPos - terraPos).Normalized;
        Vector3d surface = terraPos + (toMoon * terra.Radius);
        double solarSurface = system.StarVisibilityAt(surface, 88.743d * Day, out OrbitingBody surfOcc, terra);
        Debug.Log(string.Format("[EclipseVerify] solar surface: f={0:F6} occ={1}",
            solarSurface, Name(surfOcc)));

        // 3. Lunar eclipse from Artemis (Terra transits the Sun) at the minimum:
        //    t = 363.486 d.
        double lunar = system.StarVisibilityForBody(moon, 363.486d * Day, out OrbitingBody lunarOcc);
        Debug.Log(string.Format("[EclipseVerify] lunar t=363.486d: moon={0:F6} occ={1}",
            lunar, Name(lunarOcc)));

        // 4. Ship in Terra's shadow: a point 2 R beyond Terra on the anti-solar
        //    side must see no Sun at all.
        terra.EvaluateWorldState(0d, out Vector3d p0, out _);
        sol.EvaluateWorldState(0d, out Vector3d s0, out _);
        Vector3d away = (p0 - s0).Normalized;
        Vector3d ship = p0 + (away * (2d * terra.Radius));
        double shipVisibility = system.StarVisibilityAt(ship, 0d, out OrbitingBody shipOcc);
        Debug.Log(string.Format("[EclipseVerify] ship behind Terra: f={0:F6} occ={1}",
            shipVisibility, Name(shipOcc)));

        // 5. Ignoring Terra removes its own limb from the same query.
        double surfaceIgnoring = system.StarVisibilityAt(
            terraPos + (away * terra.Radius), 0d, out _, terra);
        Debug.Log(string.Format("[EclipseVerify] Terra surface with ignoreBody: f={0:F6}",
            surfaceIgnoring));
    }

    private static OrbitingBody Find(StarSystem system, string name)
    {
        foreach (OrbitingBody b in system.AllBodies)
        {
            if (b.Name == name) return b;
        }
        return null;
    }

    private static string Name(OrbitingBody b)
    {
        return b != null ? b.Name : "-";
    }
}
