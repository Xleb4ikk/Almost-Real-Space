using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// The craft's coast stays a complete solid line after a maneuver is planned;
    /// the plan is an additional dashed path that starts at ignition. A prograde
    /// impulse at 600 s must raise the dashed orbit without deleting the solid one.
    /// </summary>
    private static int Test569_NavMapManeuvers()
    {
        StarSystem system = TwoBodySystem();
        OrbitingBody planet = system.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);

        double altitude = 300e3d;
        double radius = planet.Radius + altitude;
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
        const double burnTime = 600d;
        const double dv = 1000d;

        var plan = new ManeuverPlan();
        plan.Add(1, burnTime);
        plan.SetDv(0, 0, dv);

        var model = new NavMapModel();
        model.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null, true, false, plan, 300d);

        NavLine coast = null;
        NavLine planned = null;
        for (int i = 0; i < model.Lines.Count; i++)
        {
            NavLine line = model.Lines[i];
            if (line.Kind == NavLineKind.ShipForecast)
            {
                coast = line;
            }
            else if (line.Kind == NavLineKind.ManeuverForecast)
            {
                planned = line;
            }
        }

        bool applied = model.AppliedManeuverCount == 1
            && model.BurnMarkers.Count == 1
            && Math.Abs(model.BurnMarkers[0].DvMs - dv) < 1e-6d
            && Math.Abs(model.BurnMarkers[0].TimeSeconds - burnTime) < 1e-6d;
        // The solid coast stays complete across the burn; the dashed maneuver line
        // starts at the ignition point and is not a replacement for the coast.
        bool split = coast != null && planned != null
            && coast.Points.Count > 100 && planned.Points.Count >= 2
            && coast.EndTimeS > burnTime + 1000d
            && Math.Abs(planned.StartTimeS - burnTime) < 1e-6d;
        bool planNoted = model.ForecastValid
            && model.Note.IndexOf("МАНЁВР", StringComparison.OrdinalIgnoreCase) >= 0;

        double coastMax = 0d;
        double plannedMax = 0d;
        if (coast != null)
        {
            for (int i = 0; i < coast.Points.Count; i++)
            {
                coastMax = Math.Max(coastMax, coast.Points[i].Magnitude - radius);
            }
        }

        if (planned != null)
        {
            for (int i = 0; i < planned.Points.Count; i++)
            {
                plannedMax = Math.Max(plannedMax, planned.Points[i].Magnitude - radius);
            }
        }

        // The circular coast stays at its radius; +1 km/s raises the apoapsis by
        // millions of metres, so a real predicted orbit must leave the shell.
        bool orbitRaised = coastMax < 100e3d && plannedMax > 1e6d;

        // No plan: the old single ballistic line is still what the map draws.
        var ballisticModel = new NavMapModel();
        ballisticModel.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null, true);
        bool ballisticUnchanged = ballisticModel.AppliedManeuverCount == 0
            && ballisticModel.BurnMarkers.Count == 0;
        for (int i = 0; i < ballisticModel.Lines.Count; i++)
        {
            if (ballisticModel.Lines[i].Kind == NavLineKind.ManeuverForecast)
            {
                ballisticUnchanged = false;
            }
        }

        // The player picks the TIME first: a maneuver with ΔV = 0 must already put
        // its ignition marker on the solid coast line, while no maneuver orbit is
        // drawn at all (a zero burn changes nothing).
        var timeOnlyPlan = new ManeuverPlan();
        timeOnlyPlan.Add(1, burnTime);
        var timeOnlyModel = new NavMapModel();
        timeOnlyModel.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null,
            true, false, timeOnlyPlan, 300d);

        NavLine timeOnlyCoast = null;
        bool timeOnlyPlanLine = false;
        for (int i = 0; i < timeOnlyModel.Lines.Count; i++)
        {
            NavLine line = timeOnlyModel.Lines[i];
            if (line.Kind == NavLineKind.ShipForecast)
            {
                timeOnlyCoast = line;
            }
            else if (line.Kind == NavLineKind.ManeuverForecast)
            {
                timeOnlyPlanLine = true;
            }
        }

        double timeOnlyGap = double.PositiveInfinity;
        if (timeOnlyModel.BurnMarkers.Count == 1 && timeOnlyCoast != null)
        {
            Vector3d marker = timeOnlyModel.BurnMarkers[0].Position;
            for (int i = 0; i < timeOnlyCoast.Points.Count; i++)
            {
                timeOnlyGap = Math.Min(timeOnlyGap, (timeOnlyCoast.Points[i] - marker).Magnitude);
            }
        }

        bool timeOnly = timeOnlyModel.BurnMarkers.Count == 1
            && Math.Abs(timeOnlyModel.BurnMarkers[0].TimeSeconds - burnTime) < 1e-6d
            && Math.Abs(timeOnlyModel.BurnMarkers[0].DvMs) < 1e-9d
            && !timeOnlyPlanLine
            && timeOnlyModel.AppliedManeuverCount == 0
            && timeOnlyModel.PlannedWindowSeconds <= 0d
            && timeOnlyCoast != null
            && timeOnlyGap < 1d;

        // The dashed orbit must start exactly at that ignition marker, not at the
        // craft's current position.
        double planStartGap = double.PositiveInfinity;
        double coastMarkerGap = double.PositiveInfinity;
        if (planned != null && model.BurnMarkers.Count == 1)
        {
            planStartGap = (planned.Points[0] - model.BurnMarkers[0].Position).Magnitude;
            Vector3d marker = model.BurnMarkers[0].Position;
            if (coast != null)
            {
                for (int i = 0; i < coast.Points.Count; i++)
                {
                    coastMarkerGap = Math.Min(coastMarkerGap, (coast.Points[i] - marker).Magnitude);
                }
            }
        }

        bool startsAtMarker = planStartGap < 1d && coastMarkerGap < 1d;

        // The default "+ МАНЁВР" in LEO lands almost at the 3-orbit visual cap.
        // The forecast must extend past that cap, otherwise the new orbit is a
        // two-degree stub and looks like "the maneuver was not drawn".
        OrbitalElements elements = OrbitalElements.FromState(
            radial * radius, tangent * Math.Sqrt(mu / radius), mu);
        double period = elements.OrbitalPeriodSeconds;
        var latePlan = new ManeuverPlan();
        latePlan.Add(1, 3d * period);
        latePlan.SetDv(0, 0, 500d);
        var lateModel = new NavMapModel();
        lateModel.Rebuild(system, ship, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true, false, latePlan, 300d);

        bool lateLine = false;
        double lateMax = 0d;
        double lateBallisticEnd = 0d;
        for (int i = 0; i < lateModel.Lines.Count; i++)
        {
            NavLine line = lateModel.Lines[i];
            if (line.Kind == NavLineKind.ShipForecast)
            {
                lateBallisticEnd = Math.Max(lateBallisticEnd, line.EndTimeS);
                continue;
            }

            if (line.Kind != NavLineKind.ManeuverForecast)
            {
                continue;
            }

            lateLine = line.Points.Count >= 2;
            for (int k = 0; k < line.Points.Count; k++)
            {
                lateMax = Math.Max(lateMax, line.Points[k].Magnitude - radius);
            }
        }

        bool lateVisible = lateModel.AppliedManeuverCount == 1
            && lateLine
            && lateMax > 100e3d
            && lateBallisticEnd > (3d * period) + period
            && lateModel.ForecastWindowSeconds > (3d * period) + period;

        // Axis semantics: +Normal points at the body (an inward radial impulse
        // lowers periapsis), +Binormal tilts the orbit out of the current plane.
        var normalPlan = new ManeuverPlan();
        normalPlan.Add(1, burnTime);
        normalPlan.SetDv(0, 1, 100d);
        var normalModel = new NavMapModel();
        normalModel.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null, true, false, normalPlan, 300d);
        double normalMin = double.PositiveInfinity;
        for (int i = 0; i < normalModel.Lines.Count; i++)
        {
            NavLine line = normalModel.Lines[i];
            if (line.Kind != NavLineKind.ManeuverForecast)
            {
                continue;
            }

            for (int k = 0; k < line.Points.Count; k++)
            {
                normalMin = Math.Min(normalMin, line.Points[k].Magnitude);
            }
        }

        var binormalPlan = new ManeuverPlan();
        binormalPlan.Add(1, burnTime);
        binormalPlan.SetDv(0, 2, 500d);
        var binormalModel = new NavMapModel();
        binormalModel.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null, true, false, binormalPlan, 300d);
        double outOfPlane = 0d;
        for (int i = 0; i < binormalModel.Lines.Count; i++)
        {
            NavLine line = binormalModel.Lines[i];
            if (line.Kind != NavLineKind.ManeuverForecast)
            {
                continue;
            }

            for (int k = 0; k < line.Points.Count; k++)
            {
                outOfPlane = Math.Max(outOfPlane, Math.Abs(line.Points[k].Y));
            }
        }

        bool axes = normalMin < radius - 50e3d && outOfPlane > 100e3d;

        // The map camera must not follow the ship's instantaneous orbital plane:
        // pitching a rocket during ascent would otherwise spin the whole globe.
        var pitchedShip = new Spacecraft(
            ship.Position,
            ship.Velocity + new Vector3d(0d, 0d, 500d),
            ship.Mass);
        var pitchedModel = new NavMapModel();
        pitchedModel.Rebuild(system, pitchedShip, 0d, 1, shipId, visible, 3600d, 0d, null, true);
        bool mapStable = (pitchedModel.OrbitNormal - model.OrbitNormal).Magnitude < 1e-12d
            && (pitchedModel.OrbitRight - model.OrbitRight).Magnitude < 1e-12d;

        bool pass = applied && split && planNoted && orbitRaised && ballisticUnchanged
            && timeOnly && startsAtMarker && lateVisible && axes && mapStable;
        Check(pass, "T569 nav map maneuvers",
            "applied=" + model.AppliedManeuverCount
            + " markers=" + model.BurnMarkers.Count
            + " split=" + split
            + " coastEnd=" + (coast != null ? coast.EndTimeS.ToString("F1") : "-")
            + " planStart=" + (planned != null ? planned.StartTimeS.ToString("F1") : "-")
            + " planStartGap=" + planStartGap.ToString("F1") + "m"
            + " coastMarkerGap=" + coastMarkerGap.ToString("F1") + "m"
            + " timeOnly=" + timeOnly + " timeOnlyMarkerGap=" + timeOnlyGap.ToString("F1") + "m"
            + " coastMax=" + coastMax.ToString("F0") + "m"
            + " planMax=" + plannedMax.ToString("F0") + "m"
            + " note=" + model.Note
            + " ballistic=" + ballisticUnchanged
            + " late=" + lateVisible + " lateSpan=" + lateModel.ForecastWindowSeconds.ToString("F0")
            + "s lateBallisticEnd=" + lateBallisticEnd.ToString("F0")
            + "s lateMax=" + lateMax.ToString("F0") + "m"
            + " axes=" + axes + " normalMin=" + normalMin.ToString("F0") + "m"
            + " outOfPlane=" + outOfPlane.ToString("F0") + "m"
            + " mapStable=" + mapStable);
        return 0;
    }
}
