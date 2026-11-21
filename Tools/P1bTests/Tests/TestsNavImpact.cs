using System;
using System.Diagnostics;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// A forecast that dives into the ground must stop ON the surface, report where
    /// and when it hits, and never integrate inside the planet.
    ///
    /// Two real defects are pinned here:
    ///  * the maneuver leg's final coast loop had no "already hit" guard, so after a
    ///    contact it kept integrating from a state BELOW the surface for the whole
    ///    remaining window (16335 s instead of 566 s in the measured case) and painted
    ///    the fan of dashed garbage that runs past the globe on the nav map;
    ///  * a hyperbolic dive was sampled in 27-minute steps because the conic estimate
    ///    returned +infinity for every e>1 arc, so a step could cross the surface
    ///    between two samples and never be noticed;
    ///  * the line simply ended in the middle of the globe with nothing but a legend
    ///    string, so the player could not tell where or when the craft hits.
    /// </summary>
    private static int Test571_NavMapImpactMarker()
    {
        StarSystem system = TwoBodySystem();
        OrbitingBody planet = system.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);

        double radius = planet.Radius + 400e3d;
        double mu = planet.ResolveStandardGravitationalParameter();
        Vector3d radial = new Vector3d(0d, 0d, 1d);
        Vector3d tangent = new Vector3d(1d, 0d, 0d);
        var ship = new Spacecraft(
            bodyPosition + (radial * radius),
            bodyVelocity + (tangent * Math.Sqrt(mu / radius)),
            1000d);

        bool[] visible = new bool[system.AllBodies.Count + 1];
        for (int i = 0; i < visible.Length; i++)
        {
            visible[i] = true;
        }

        int shipId = system.AllBodies.Count;

        // Retrograde 1.2 km/s from a 400 km circular orbit: the maneuver orbit's
        // periapsis ends up thousands of kilometres below the ground.
        var plan = new ManeuverPlan();
        plan.Add(1, 300d);
        plan.SetDv(0, 0, -1200d);

        var model = new NavMapModel();
        var watch = Stopwatch.StartNew();
        model.Rebuild(system, ship, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true, false, plan, 300d);
        watch.Stop();
        long firstBuildMs = watch.ElapsedMilliseconds;

        NavLine planned = null;
        double deepestMeters = 0d;
        for (int i = 0; i < model.Lines.Count; i++)
        {
            NavLine line = model.Lines[i];
            if (line.Kind != NavLineKind.ShipForecast && line.Kind != NavLineKind.ManeuverForecast)
            {
                continue;
            }

            if (line.Kind == NavLineKind.ManeuverForecast)
            {
                planned = line;
            }

            for (int k = 0; k < line.Points.Count; k++)
            {
                deepestMeters = Math.Max(deepestMeters, planet.Radius - line.Points[k].Magnitude);
            }
        }

        NavImpactMarker mark = model.Impacts.Count == 1 ? model.Impacts[0] : null;

        bool marker = mark != null
            && mark.Planned
            && mark.BodyIndex == 1
            && mark.BodyName == planet.Name
            && Math.Abs(mark.Position.Magnitude - planet.Radius) < 1d
            && mark.TimeSeconds > 300d && mark.TimeSeconds < model.TimeSeconds + (3d * 86400d)
            && mark.SpeedMps > 1000d && mark.SpeedMps < 20000d;

        bool endsOnGround = planned != null && planned.Points.Count >= 2
            && Math.Abs(planned.Points[planned.Points.Count - 1].Magnitude - planet.Radius) < 1d;

        bool samePoint = mark != null && planned != null
            && Math.Abs(planned.EndTimeS - mark.TimeSeconds) < 1e-6d
            && Math.Abs(model.PlannedWindowSeconds - mark.TimeSeconds) < 1e-3d;

        bool neverUnderground = deepestMeters <= 1d;
        bool noted = model.Note.IndexOf("УДАР", StringComparison.OrdinalIgnoreCase) >= 0;

        // The old code spent minutes computing this one frame; a miss shows up as a
        // hanging map, not as a wrong picture, so pin the cost too.
        bool fast = firstBuildMs < 5000L;

        // The same rebuild again is served from the forecast cache: the marker must
        // survive the replay, not vanish because only the lines were cached.
        model.Rebuild(system, ship, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true, false, plan, 300d);
        bool replayed = model.Impacts.Count == 1
            && Math.Abs(model.Impacts[0].TimeSeconds - mark.TimeSeconds) < 1e-6d
            && model.Impacts[0].Planned
            && Math.Abs(model.Impacts[0].Position.Magnitude - planet.Radius) < 1d;

        // A circular orbit never touches the ground: no marker, no impact note.
        var circular = new NavMapModel();
        circular.Rebuild(system, ship, 0d, 1, shipId, visible, 86400d, 0d, null, true);
        bool clean = circular.Impacts.Count == 0
            && circular.Note.IndexOf("УДАР", StringComparison.OrdinalIgnoreCase) < 0;

        // Hyperbolic dive: above escape speed and pointed down. The conic estimate used
        // to return +infinity for every e>1 arc, so this leg was sampled in 27-minute
        // steps — a step could carry the probe clean through the globe unseen.
        var inbound = new Spacecraft(
            bodyPosition + (radial * radius),
            bodyVelocity + (radial * -9000d) + (tangent * 6500d),
            1000d);
        var hyper = new NavMapModel();
        hyper.Rebuild(system, inbound, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true);

        double hyperDepth = 0d;
        for (int i = 0; i < hyper.Lines.Count; i++)
        {
            NavLine line = hyper.Lines[i];
            if (line.Kind != NavLineKind.ShipForecast)
            {
                continue;
            }

            for (int k = 0; k < line.Points.Count; k++)
            {
                hyperDepth = Math.Max(hyperDepth, planet.Radius - line.Points[k].Magnitude);
            }
        }

        bool hyperHit = hyper.Impacts.Count == 1
            && !hyper.Impacts[0].Planned
            && Math.Abs(hyper.Impacts[0].Position.Magnitude - planet.Radius) < 1d
            && hyper.Impacts[0].TimeSeconds > 0d && hyper.Impacts[0].TimeSeconds < 600d
            && hyperDepth <= 1d;

        bool pass = marker && endsOnGround && samePoint && neverUnderground && noted && fast
            && replayed && clean && hyperHit;
        Check(pass, "T571 nav map ground contact marker",
            "marker=" + marker
            + (mark != null
                ? " (t+" + mark.TimeSeconds.ToString("F1") + "s, alt "
                    + (mark.Position.Magnitude - planet.Radius).ToString("F1") + "m, v "
                    + mark.SpeedMps.ToString("F1") + "m/s)"
                : " (none, count " + model.Impacts.Count + ")")
            + " | endsOnGround=" + endsOnGround
            + " | samePoint=" + samePoint
            + " | neverUnderground=" + neverUnderground + " (depth " + deepestMeters.ToString("F1") + "m)"
            + " | noted=" + noted + " (" + model.Note + ")"
            + " | fast=" + fast + " (" + firstBuildMs + "ms)"
            + " | replayed=" + replayed
            + " | clean=" + clean
            + " | hyper=" + hyperHit
            + (hyper.Impacts.Count > 0
                ? " (t+" + hyper.Impacts[0].TimeSeconds.ToString("F1") + "s, depth "
                    + hyperDepth.ToString("F1") + "m)"
                : " (none)"));
        return 0;
    }
}
