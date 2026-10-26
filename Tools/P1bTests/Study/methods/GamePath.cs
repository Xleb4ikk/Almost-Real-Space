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
{    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipGame(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days)
    {
        var phys = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        var counting = new CountingSource(new CachedGravitySource(sys));
        phys.Sources.Add(counting);
        var prop = new EventDrivenPropagator(phys)
        {
            MaxChunkSeconds = 600d,
            DetectionSubdivisions = 20,
            UseDenseRefinement = true
        };
        var ship = new Spacecraft(p0, v0, 1000d);
        var sw = Stopwatch.StartNew();
        // Propagate takes a DURATION, not an absolute target time.
        prop.Propagate(ship, t0, days * 86400d);
        sw.Stop();
        return (ship.Position, ship.Velocity, counting.Calls, sw.Elapsed.TotalMilliseconds);
    }

    private static StarSystem StudyCloneBakedSystem(StarSystem source, bool custom)
    {
        StarSystem clone = custom ? StudyCustomSystem() : StudyRealSolar7();
        int n = Math.Min(clone.AllBodies.Count, source.AllBodies.Count);
        for (int i = 0; i < n; i++)
        {
            clone.AllBodies[i].Baked = source.AllBodies[i].Baked;
        }

        clone.InvalidatePositionCache();
        return clone;
    }
}

