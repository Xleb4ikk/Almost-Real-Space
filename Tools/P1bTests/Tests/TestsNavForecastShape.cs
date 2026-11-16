using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// The navigation map must draw the real coast as a curve. Two regressions:
    /// a short suborbital hop used to be sampled over the requested multi-hour
    /// window (two chords, ending underground), and an eccentric body orbit used
    /// uniform time (chords near periapsis).
    /// </summary>
    private static int Test568_NavMapShortArc()
    {
        StarSystem system = TwoBodySystem();
        OrbitingBody planet = system.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);

        double altitude = 100e3d;
        double radius = planet.Radius + altitude;
        double mu = planet.ResolveStandardGravitationalParameter();
        Vector3d radial = new Vector3d(0d, 0d, 1d);
        Vector3d tangent = new Vector3d(1d, 0d, 0d);
        double circular = Math.Sqrt(mu / radius);

        bool[] visible = new bool[system.AllBodies.Count + 1];
        for (int i = 0; i < visible.Length; i++)
        {
            visible[i] = true;
        }

        int shipId = system.AllBodies.Count;
        int arcCount = 0;
        int arcSmooth = 0;
        int arcOnGround = 0;
        int arcAboveGround = 0;
        int arcStoppedBeforeWindow = 0;
        string detail = "";

        for (int trial = 0; trial < 3; trial++)
        {
            double factor = 0.5d + (trial * 0.15d);
            double speed = factor * circular;
            var ship = new Spacecraft(
                bodyPosition + (radial * radius),
                bodyVelocity + (tangent * speed),
                1000d);

            var model = new NavMapModel();
            model.Rebuild(system, ship, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true);

            NavLine forecast = null;
            for (int i = 0; i < model.Lines.Count; i++)
            {
                if (model.Lines[i].Kind == NavLineKind.ShipForecast)
                {
                    forecast = model.Lines[i];
                    break;
                }
            }

            if (forecast == null || !model.ForecastValid)
            {
                detail += " trial" + trial + ":no-line";
                continue;
            }

            arcCount++;
            double minAltitude = double.PositiveInfinity;
            double maxSpecifiedAltitude = double.NegativeInfinity;
            for (int i = 0; i < forecast.Points.Count; i++)
            {
                double pointAltitude = forecast.Points[i].Magnitude - planet.Radius;
                minAltitude = Math.Min(minAltitude, pointAltitude);
                if (i + 1 < forecast.Points.Count)
                {
                    maxSpecifiedAltitude = Math.Max(maxSpecifiedAltitude, pointAltitude);
                }
            }

            double maxStepAngle = 0d;
            for (int i = 1; i < forecast.Points.Count; i++)
            {
                Vector3d a = forecast.Points[i - 1];
                Vector3d b = forecast.Points[i];
                if (a.Magnitude > 1e-6d && b.Magnitude > 1e-6d)
                {
                    double cosine = Math.Max(-1d, Math.Min(1d,
                        Vector3d.Dot(a, b) / (a.Magnitude * b.Magnitude)));
                    maxStepAngle = Math.Max(maxStepAngle, Math.Acos(cosine) * (180d / Math.PI));
                }
            }

            bool dense = forecast.Points.Count >= 60;
            bool smooth = maxStepAngle < 2d;
            bool endsOnGround = Math.Abs(minAltitude) < 2000d;
            bool aboveGround = maxSpecifiedAltitude > -1d;
            bool stopped = model.ForecastWindowSeconds > 1d
                && model.ForecastWindowSeconds < 3d * 86400d - 1d;
            if (dense && smooth)
            {
                arcSmooth++;
            }

            if (endsOnGround)
            {
                arcOnGround++;
            }

            if (aboveGround)
            {
                arcAboveGround++;
            }

            if (stopped)
            {
                arcStoppedBeforeWindow++;
            }

            detail += " trial" + trial
                + ":v=" + factor.ToString("F2") + "c"
                + " pts=" + forecast.Points.Count
                + " span=" + model.ForecastWindowSeconds.ToString("F1") + "s"
                + " minAlt=" + minAltitude.ToString("F0") + "m"
                + " step=" + maxStepAngle.ToString("F2") + "deg;";
        }

        // A normal circular orbit still uses the three-revolution map cap, so the
        // same 160 samples cover a shorter, smoother path (not a six-hour / 4+ orbit
        // window).
        var orbiter = new Spacecraft(
            bodyPosition + (radial * radius),
            bodyVelocity + (tangent * circular),
            1000d);
        var orbiterModel = new NavMapModel();
        orbiterModel.Rebuild(system, orbiter, 0d, 1, shipId, visible, 3d * 86400d, 0d, null, true);
        OrbitalElements elements = OrbitalElements.FromState(
            orbiter.Position - bodyPosition, orbiter.Velocity - bodyVelocity, mu);
        double expectedSpan = 3d * elements.OrbitalPeriodSeconds;
        bool orbitCap = Math.Abs(orbiterModel.ForecastWindowSeconds - expectedSpan) < 1d;

        // A short-period moon on a multi-day slider is capped to three orbits and
        // stays a dense curve instead of a scribble of many revolutions.
        var star = new OrbitingBody
        {
            Name = "Звезда",
            StandardGravitationalParameter = 1.327e20d,
            EpochTimeSeconds = 0d,
        };
        var testPlanet = new OrbitingBody
        {
            Name = "Планета",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            SemiMajorAxis = 1.5e11d,
            Eccentricity = 0d,
            EpochTimeSeconds = 0d,
        };
        testPlanet.Parent = star;
        star.Children.Add(testPlanet);

        double moonPeriod = 2d * 3600d;
        double moonAxis = Math.Pow(
            testPlanet.StandardGravitationalParameter * moonPeriod * moonPeriod
            / (4d * Math.PI * Math.PI), 1d / 3d);
        var moon = new OrbitingBody
        {
            Name = "Спутник",
            StandardGravitationalParameter = 4.9e12d,
            Radius = 1.0e6d,
            SemiMajorAxis = moonAxis,
            Eccentricity = 0.02d,
            EpochTimeSeconds = 0d,
        };
        moon.Parent = testPlanet;
        testPlanet.Children.Add(moon);
        var moonSystem = new StarSystem(star);
        bool[] moonVisible = new bool[moonSystem.AllBodies.Count + 1];
        for (int i = 0; i < moonVisible.Length; i++)
        {
            moonVisible[i] = true;
        }

        int planetIndex = 1;
        int moonIndex = 2;
        var moonModel = new NavMapModel();
        moonModel.Rebuild(moonSystem, null, 0d, planetIndex, moonIndex, moonVisible,
            3d * 86400d, 86400d, null, true);
        double bodyForecastSpan = 0d;
        double bodyHistorySpan = 0d;
        int bodyForecastPoints = 0;
        for (int i = 0; i < moonModel.Lines.Count; i++)
        {
            NavLine line = moonModel.Lines[i];
            if (line.Kind == NavLineKind.BodyForecast)
            {
                bodyForecastSpan = line.EndTimeS - line.StartTimeS;
                bodyForecastPoints = line.Points.Count;
            }
            else if (line.Kind == NavLineKind.BodyHistory)
            {
                bodyHistorySpan = line.EndTimeS - line.StartTimeS;
            }
        }

        bool moonCap = bodyForecastSpan > 1d
            && bodyForecastSpan <= (3d * moonPeriod) + 1d
            && bodyForecastPoints >= 100
            && bodyHistorySpan > 1d
            && bodyHistorySpan <= (3d * moonPeriod) + 1d;

        // Eccentric planet orbit: equal true-anomaly steps, not uniform time (which
        // leaves a few long chords at periapsis where the body moves fastest).
        var eccentricStar = new OrbitingBody
        {
            Name = "Звезда",
            StandardGravitationalParameter = 1.327e20d,
            EpochTimeSeconds = 0d,
        };
        var eccentricPlanet = new OrbitingBody
        {
            Name = "Планета",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            SemiMajorAxis = 1.5e11d,
            Eccentricity = 0.6d,
            EpochTimeSeconds = 0d,
        };
        eccentricPlanet.Parent = eccentricStar;
        eccentricStar.Children.Add(eccentricPlanet);
        var eccentricSystem = new StarSystem(eccentricStar);
        bool[] eccentricVisible = { true, true };
        var eccentricModel = new NavMapModel();
        eccentricModel.Rebuild(eccentricSystem, null, 0d, 0, 1, eccentricVisible, 600d, 0d, null, true);

        double minAngle = double.PositiveInfinity;
        double maxAngle = 0d;
        double angleSum = 0d;
        int angleCount = 0;
        for (int i = 0; i < eccentricModel.Lines.Count; i++)
        {
            NavLine line = eccentricModel.Lines[i];
            if (line.Kind != NavLineKind.BodyOrbit || line.BodyIndex != 1)
            {
                continue;
            }

            for (int k = 1; k < line.Points.Count; k++)
            {
                Vector3d a = line.Points[k - 1];
                Vector3d b = line.Points[k];
                if (a.Magnitude > 1e-6d && b.Magnitude > 1e-6d)
                {
                    double cosine = Math.Max(-1d, Math.Min(1d,
                        Vector3d.Dot(a, b) / (a.Magnitude * b.Magnitude)));
                    double angle = Math.Acos(cosine) * (180d / Math.PI);
                    minAngle = Math.Min(minAngle, angle);
                    maxAngle = Math.Max(maxAngle, angle);
                    angleSum += angle;
                    angleCount++;
                }
            }
        }

        double meanAngle = angleCount > 0 ? angleSum / angleCount : 0d;
        bool anomalySampling = angleCount > 100
            && maxAngle < meanAngle * 1.15d
            && minAngle > meanAngle * 0.4d;

        bool pass = arcCount == 3 && arcSmooth == 3 && arcOnGround == 3
            && arcAboveGround == 3 && arcStoppedBeforeWindow == 3
            && orbitCap && moonCap && anomalySampling;
        Check(pass, "T568 nav map trajectory shape",
            "arcs=" + arcSmooth + "/" + arcCount
            + " onGround=" + arcOnGround + "/3"
            + " above=" + arcAboveGround + "/3"
            + " stopped=" + arcStoppedBeforeWindow + "/3"
            + " | 3-orbitCap=" + orbitCap + " (span " + orbiterModel.ForecastWindowSeconds.ToString("F0") + "s)"
            + " | moonCap=" + moonCap + " (span " + bodyForecastSpan.ToString("F0") + "s / 3T "
            + (3d * moonPeriod).ToString("F0") + "s, pts " + bodyForecastPoints + ")"
            + " | anomaly=" + anomalySampling + " (min " + minAngle.ToString("F2")
            + " mean " + meanAngle.ToString("F2") + " max " + maxAngle.ToString("F2") + " deg)"
            + " | " + detail);
        return 0;
    }
}
