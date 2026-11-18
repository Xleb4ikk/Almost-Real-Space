using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// The navigation map is built from the game's own physics: frame-relative body
    /// positions come from ephemerides/rails, the forecast is a DOP853 N-body coast
    /// with the same gravity/drag sources as SimulationRunner, and the history is the
    /// actual ship samples. This checks the data contract, not the drawing.
    /// </summary>
    private static int Test567_NavMapModel()
    {
        StarSystem system = TwoBodySystem();
        OrbitingBody planet = system.AllBodies[1];
        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);

        double altitude = 200e3d;
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
        var model = new NavMapModel();
        model.Rebuild(system, ship, 0d, 1, shipId, visible, 3600d, 0d, null, true);

        bool markers = model.Bodies.Count == system.AllBodies.Count
            && model.Bodies[1].IsFrame
            && model.Bodies[1].Position.Magnitude < 1e-6d
            && model.Bodies[0].Position.Magnitude > 1e8d;
        bool frameRelative = Math.Abs(model.FrameRelativeShipPosition.Magnitude - radius) < 1d;

        // The frame body itself must have an orbit too, and its marker (the map
        // origin) must lie exactly on that polyline. Without the inserted origin
        // vertex a 160-point chord passes wide of the body at planet zoom.
        bool frameOrbit = false;
        bool frameOrbitOnBody = false;
        for (int i = 0; i < model.Lines.Count; i++)
        {
            NavLine line = model.Lines[i];
            if (line.Kind != NavLineKind.BodyOrbit || line.BodyIndex != 1)
            {
                continue;
            }

            frameOrbit = true;
            for (int k = 0; k < line.Points.Count; k++)
            {
                if (line.Points[k].Magnitude < 1e-3d)
                {
                    frameOrbitOnBody = true;
                }
            }
        }

        bool[] frameHidden = (bool[])visible.Clone();
        frameHidden[1] = false;
        var frameHiddenModel = new NavMapModel();
        frameHiddenModel.Rebuild(system, ship, 0d, 1, shipId, frameHidden, 3600d, 0d, null, true);
        bool frameOrbitToggle = true;
        for (int i = 0; i < frameHiddenModel.Lines.Count; i++)
        {
            NavLine line = frameHiddenModel.Lines[i];
            if (line.Kind == NavLineKind.BodyOrbit && line.BodyIndex == 1)
            {
                frameOrbitToggle = false;
            }
        }

        bool[] shipHidden = (bool[])visible.Clone();
        shipHidden[shipId] = false;
        var shipHiddenModel = new NavMapModel();
        shipHiddenModel.Rebuild(system, ship, 0d, 1, shipId, shipHidden, 3600d, 3600d, null, true);
        bool shipToggle = true;
        for (int i = 0; i < shipHiddenModel.Lines.Count; i++)
        {
            if (shipHiddenModel.Lines[i].Kind == NavLineKind.ShipForecast
                || shipHiddenModel.Lines[i].Kind == NavLineKind.ShipHistory)
            {
                shipToggle = false;
            }
        }

        NavLine forecast = null;
        for (int i = 0; i < model.Lines.Count; i++)
        {
            if (model.Lines[i].Kind == NavLineKind.ShipForecast)
            {
                forecast = model.Lines[i];
                break;
            }
        }

        bool forecastValid = model.ForecastValid && forecast != null && forecast.Points.Count > 100;
        bool forecastFinite = true;
        bool forecastStartsAtShip = false;
        if (forecast != null)
        {
            for (int i = 0; i < forecast.Points.Count; i++)
            {
                if (!forecast.Points[i].IsFinite)
                {
                    forecastFinite = false;
                }
            }

            if (forecast.Points.Count > 0)
            {
                Vector3d expected = ship.Position - model.FramePosition;
                forecastStartsAtShip = (forecast.Points[0] - expected).Magnitude < 1e-3d;
            }
        }

        // Forecast length is the slider value: a longer requested window must
        // produce a longer integrated forecast (unless the 3-orbit map cap binds).
        var shortForecastModel = new NavMapModel();
        shortForecastModel.Rebuild(system, ship, 0d, 1, shipId, visible, 600d, 0d, null, true);
        var longForecastModel = new NavMapModel();
        longForecastModel.Rebuild(system, ship, 0d, 1, shipId, visible, 6d * 3600d, 0d, null, true);
        bool forecastLengthReacts = longForecastModel.ForecastValid
            && shortForecastModel.ForecastValid
            && longForecastModel.ForecastWindowSeconds > shortForecastModel.ForecastWindowSeconds + 1d;

        // No forecast on the surface/contact path, but the real state and history remain.
        var history = new List<NavShipSample>();
        for (int i = 0; i <= 20; i++)
        {
            double t = i * 30d;
            double angle = i * 0.03d;
            Vector3d offset = (radial * (Math.Cos(angle) * radius)) + (tangent * (Math.Sin(angle) * radius));
            planet.EvaluateWorldState(t, out Vector3d p, out _);
            history.Add(new NavShipSample(t, p + offset));
        }

        var surfaceModel = new NavMapModel();
        surfaceModel.Rebuild(system, ship, 600d, 1, shipId, visible, 3600d, 3600d, history, false);

        bool forecastOff = !surfaceModel.ForecastValid;
        bool historyValid = false;
        for (int i = 0; i < surfaceModel.Lines.Count; i++)
        {
            if (surfaceModel.Lines[i].Kind == NavLineKind.ShipHistory && surfaceModel.Lines[i].Points.Count >= 2)
            {
                historyValid = true;
            }
        }

        bool noteHonest = surfaceModel.Note.IndexOf("ВЫКЛ", StringComparison.OrdinalIgnoreCase) >= 0;
        bool planeOrthonormal = Math.Abs(model.OrbitNormal.Magnitude - 1d) < 1e-9d
            && Math.Abs(model.OrbitRight.Magnitude - 1d) < 1e-9d
            && Math.Abs(Vector3d.Dot(model.OrbitNormal, model.OrbitRight)) < 1e-9d;

        // A standing craft is carried by the planet: in the star frame its site
        // path is the planet's orbit + rotation. The map must build both the
        // past and the future from the ephemerides over the requested windows.
        var landedShip = new Spacecraft(
            bodyPosition + (radial * (planet.Radius + 5d)),
            bodyVelocity,
            1000d);
        var landedModel = new NavMapModel();
        landedModel.Rebuild(system, landedShip, 600d, 0, 1, visible,
            3d * 86400d, 86400d, null, false, true);

        // A parked craft deliberately draws no site track: the full rotation at
        // ~4 m AGL reads as a very low orbit. Only the standing marker remains.
        bool landedHistoryOff = !landedModel.HasSurfaceHistory;
        bool landedForecastOff = !landedModel.ForecastValid && !landedModel.HasSurfaceForecast;
        bool landedNoteHonest = landedModel.Note.IndexOf("ПОВЕРХНОСТ", StringComparison.OrdinalIgnoreCase) >= 0
            || landedModel.Note.IndexOf("СТОЯНК", StringComparison.OrdinalIgnoreCase) >= 0;
        bool landedNoShipLine = true;
        for (int i = 0; i < landedModel.Lines.Count; i++)
        {
            NavLine line = landedModel.Lines[i];
            if (line.Kind == NavLineKind.ShipForecast || line.Kind == NavLineKind.ShipHistory)
            {
                landedNoShipLine = false;
            }
        }

        // The centred body itself gets the same history/forecast spans (ephemeris).
        // A long slider (641 days) must not be clipped by the ship forecast's
        // 60-day N-body budget: body paths are cheap ephemeris samples.
        var bodyTrailModel = new NavMapModel();
        bodyTrailModel.Rebuild(system, ship, 0d, 0, 1, visible, 641d * 86400d, 2d * 86400d, null, true);
        bool bodyTrailValid = bodyTrailModel.HasBodyHistory && bodyTrailModel.HasBodyForecast;
        double bodyForecastSpan = 0d;
        for (int i = 0; i < bodyTrailModel.Lines.Count; i++)
        {
            NavLine line = bodyTrailModel.Lines[i];
            if (line.Kind == NavLineKind.BodyForecast)
            {
                bodyForecastSpan = line.EndTimeS - line.StartTimeS;
            }
        }

        bool bodyForecastLong = bodyForecastSpan > 60d * 86400d;

        // Low-altitude surface view: the dominant body has a heightfield, so the map
        // must switch to the real local terrain and keep the true sub-point.
        planet.Terrain = new HeightfieldTerrain();
        double lowRadius = planet.Radius + 10e3d;
        var lowShip = new Spacecraft(
            bodyPosition + (radial * lowRadius),
            bodyVelocity + (tangent * Math.Sqrt(mu / lowRadius)),
            1000d);
        var surfaceView = new NavMapModel();
        surfaceView.Rebuild(system, lowShip, 0d, 1, shipId, visible, 3600d, 0d, null, false);
        bool surfaceMode = surfaceView.ViewMode == NavViewMode.Orbits
            && surfaceView.HasSurfaceFrame
            && surfaceView.DominantBodyIndex == 1
            && surfaceView.ShipHeightAboveTerrain > -1000d
            && surfaceView.ShipHeightAboveTerrain < 20000d
            && Math.Abs(surfaceView.ShipLatitudeDegrees) <= 90d
            && surfaceView.ShipLongitudeDegrees >= 0d
            && surfaceView.ShipLongitudeDegrees < 360d
            && surfaceView.BodyRadiusMeters > 1d;

        bool pass = markers && frameRelative && forecastValid && forecastFinite && forecastStartsAtShip
            && forecastOff && historyValid && noteHonest && planeOrthonormal && surfaceMode
            && frameOrbit && frameOrbitOnBody && frameOrbitToggle && shipToggle && forecastLengthReacts
            && landedHistoryOff && landedForecastOff && landedNoteHonest && landedNoShipLine
            && bodyTrailValid && bodyForecastLong;
        Check(pass, "T567 nav map model",
            string.Format(
                "bodies={0} frame={1:F2}м; forecast={2} pts={3} finite={4} start={5}; no-forecast={6} history={7} note={8}; plane={9}; surface={10} mode={11} hasFrame={12} dominant={13} terrain={14} lat={15:F2} lon={16:F2} hAGL={17:F0}; frameOrbit={18}/{19} toggle={20} shipToggle={21} forecastLen={22}; landedHistoryOff={23} landedForecastOff={24} note={25} noShipLine={26}; bodyTrail={27} bodyForecastSpan={28:F0}s",
                model.Bodies.Count,
                model.FrameRelativeShipPosition.Magnitude,
                forecastValid,
                forecast != null ? forecast.Points.Count : 0,
                forecastFinite,
                forecastStartsAtShip,
                forecastOff,
                historyValid,
                surfaceModel.Note,
                planeOrthonormal,
                surfaceMode,
                surfaceView.ViewMode,
                surfaceView.HasSurfaceFrame,
                surfaceView.DominantBodyIndex,
                surfaceView.DominantBody != null ? surfaceView.DominantBody.Terrain != null : false,
                surfaceView.ShipLatitudeDegrees,
                surfaceView.ShipLongitudeDegrees,
                surfaceView.ShipHeightAboveTerrain,
                frameOrbit,
                frameOrbitOnBody,
                frameOrbitToggle,
                shipToggle,
                forecastLengthReacts,
                landedHistoryOff,
                landedForecastOff,
                landedModel.Note,
                landedNoShipLine,
                bodyTrailValid,
                bodyForecastSpan));
        return 0;
    }

    /// <summary>
    /// Apsis markers are the dominant body's apse, placed in the SELECTED frame at
    /// the time the craft reaches it. The old code anchored the marker at "now", so
    /// switching TERRA → SOL left it where the previous frame had it instead of
    /// following the frame's own motion along the drawn path.
    /// </summary>
    private static int Test570_NavMapFrameApsides()
    {
        StarSystem system = TwoBodySystem();
        OrbitingBody star = system.AllBodies[0];
        OrbitingBody planet = system.AllBodies[1];
        star.EvaluateWorldState(0d, out Vector3d starPosition, out _);
        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);

        double altitude = 100e3d;
        double radius = planet.Radius + altitude;
        double mu = planet.ResolveStandardGravitationalParameter();
        Vector3d radial = new Vector3d(0d, 0d, 1d);
        Vector3d tangent = new Vector3d(1d, 0d, 0d);

        // A short bound hop (mostly radial, a little tangential): a real Ap a few
        // minutes ahead, like the suborbital launch in the bug report. It is NOT a
        // heliocentric conic, so the marker must stay near the craft.
        var ship = new Spacecraft(
            bodyPosition + (radial * radius),
            bodyVelocity + (radial * 3000d) + (tangent * 100d),
            1000d);

        bool[] visible = new bool[system.AllBodies.Count + 1];
        for (int i = 0; i < visible.Length; i++)
        {
            visible[i] = true;
        }

        // Same centre (the planet) in both runs: only the reference frame differs,
        // exactly like pressing "ИСК" in the tablet's frame table.
        var planetFrame = new NavMapModel();
        planetFrame.Rebuild(system, ship, 0d, 1, 1, visible, 3600d, 0d, null, true);

        var starFrame = new NavMapModel();
        starFrame.Rebuild(system, ship, 0d, 0, 1, visible, 3600d, 0d, null, true);

        OrbitalElements elements = OrbitalElements.FromState(
            radial * radius, (radial * 3000d) + (tangent * 100d), mu);
        if (!elements.IsValid || !elements.IsBound
            || !elements.TryGetTimeToApsides(mu, out _, out double toApoapsis))
        {
            Check(false, "T570 nav frame apsides", "исходная орбита не построена");
            return 0;
        }

        Vector3d eDirection = elements.EccentricityVector / elements.Eccentricity;
        planet.EvaluateWorldState(toApoapsis, out Vector3d planetAtAp, out _);
        star.EvaluateWorldState(0d, out Vector3d starAtNow, out _);

        // In the planet frame the frame body IS the dominant body at the apsis time,
        // so the marker is the pure conic apse.
        bool planetApsis = planetFrame.HasApsides
            && (planetFrame.ApoapsisPosition - (-(eDirection * elements.ApoapsisDistance))).Magnitude < 1e-3d
            && (planetFrame.PeriapsisPosition - (eDirection * elements.PeriapsisDistance)).Magnitude < 1e-3d
            && Math.Abs(planetFrame.ApoapsisAltitudeMeters
                - (elements.ApoapsisDistance - planet.Radius)) < 1e-3d;

        // In the star frame the same physical apse is translated by the planet's own
        // heliocentric position at the APOAPSIS TIME, not at "now".
        Vector3d expectedStarAp = (planetAtAp - starAtNow) - (eDirection * elements.ApoapsisDistance);
        bool starApsis = starFrame.HasApsides && starFrame.HasApoapsis
            && (starFrame.ApoapsisPosition - expectedStarAp).Magnitude < 1e-3d;

        // On screen the centre is the planet, so the frame motion over the time to
        // the apsis is exactly what moves the marker. The old code produced the same
        // screen offset in both frames (frameMovesMarker would be false).
        Vector3d planetScreen = planetFrame.ApoapsisPosition - planetFrame.CenterPosition;
        Vector3d starScreen = starFrame.ApoapsisPosition - starFrame.CenterPosition;
        Vector3d frameTravel = planetAtAp - bodyPosition;
        Vector3d shift = starScreen - planetScreen;
        bool frameMovesMarker = (shift - frameTravel).Magnitude < 1e-3d
            && frameTravel.Magnitude > 1e5d;

        // The star-frame marker is ~1 AU from the STAR (map origin), but relative to
        // the craft/planet it must stay at hop scale, not at an AU-scale conic.
        bool onPath = starScreen.Magnitude < 1e9d;

        bool pass = planetApsis && starApsis && frameMovesMarker && onPath;
        Check(pass, "T570 nav frame apsides",
            string.Format(
                "planetAp={0:E3}м starAp={1:E3}м shift={2:E3}м frameTravel={3:E3}м screen={4:E3}м",
                planetScreen.Magnitude,
                starScreen.Magnitude,
                shift.Magnitude,
                frameTravel.Magnitude,
                starScreen.Magnitude));
        return 0;
    }
}
