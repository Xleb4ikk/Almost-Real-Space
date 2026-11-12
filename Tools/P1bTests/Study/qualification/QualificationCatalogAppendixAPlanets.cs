using System;
using System.Collections.Generic;
using P1bStudy.Framework;
using Galilego.Universe;

// TASK.md Appendix A, tiers P (planets, A6) and F (pure-gravity regimes, A5).
// Planet problems use the Test553 path; F problems use the Test559 builders. Both are built
// lazily: P2 (100 years, two dd-Taylor references) must cost nothing in the smoke profile.
internal static partial class P1bTests
{
    private static StarSystem qualificationAppendixSolar;

    private static StarSystem QualificationAppendixSolar()
    {
        if (qualificationAppendixSolar == null)
        {
            qualificationAppendixSolar = StudyShipMatrixSystem(false, false);
        }

        return qualificationAppendixSolar;
    }

    private static void QualificationAppendAppendixPlanetsAndF(
        List<QualificationProblem> list, QualificationProfile profile)
    {
        // P2–P6 (A6). Full only: two dd-Taylor references over 10/100 years dominate the run.
        list.Add(QualificationAppendixPlanet(
            "P2", "Real solar 7-body, 100 y", "Real solar 7-body, 100 y",
            StudyRealSolar7, 100d, new[] { "planets", "long-arc" }, false));
        list.Add(QualificationAppendixPlanet(
            "P3", "Custom 6-body, 10 y", "Custom 6-body, 10 y",
            StudyCustomSystem, 10d, new[] { "planets" }, false));
        list.Add(QualificationAppendixPlanet(
            "P4", "Tight binary, 10 y", "Tight binary, 10 y",
            StudyTightBinarySystem, 10d, new[] { "planets", "close-encounter" }, false));
        list.Add(QualificationAppendixPlanet(
            "P5", "Sun + eccentric comet + Jupiter, 10 y", "Sun + eccentric comet + Jupiter, 10 y",
            StudyEccentricSystem, 10d, new[] { "planets", "ecc-high" }, false));
        list.Add(QualificationAppendixPlanet(
            "P6", "Heliocentric Sun-Jupiter probe, 10 y", "Heliocentric Sun-Jupiter probe, 10 y",
            StudyHeliocentricSystem, 10d, new[] { "planets", "probe-central" }, true));

        // F01–F05 (A5): far flight, heliocentric, small-body flyby, Moon orbit, Moon hop.
        list.Add(QualificationAppendixPure(
            "F01", "Far flight 30 AU e 0.5 (physical)",
            delegate { return BuildPureProblem(FarFlightCase(QualificationAppendixSolar())); },
            new[] { "far-flight", "heliocentric" }, false, "Sun",
            new[] { "gravity", "third-body" }));
        list.Add(QualificationAppendixPure(
            "F02", "Heliocentric 1 AU e 0.5",
            delegate { return HeliocentricProblem(QualificationAppendixSolar(), 1d, 0.5d); },
            new[] { "heliocentric", "ecc-mid" }, false, "Sun",
            new[] { "gravity", "third-body" }));
        list.Add(QualificationAppendixPure(
            "F03", "Small-body flyby vinf 50",
            delegate { return SmallBodyFlybyProblem(50d); },
            new[] { "flyby", "close-encounter" }, true, "Small body (mu=1e9)",
            new[] { "gravity", "third-body" }));
        list.Add(QualificationAppendixPure(
            "F04", "Moon orbit 100 km x1000 orbits",
            delegate { return MoonOrbitProblem(QualificationAppendixSolar(), 100d, 1000); },
            new[] { "moon-soi", "long-arc" }, false, "Moon",
            new[] { "gravity", "third-body" }));
        list.Add(QualificationAppendixPure(
            "F05", "Moon hop 1000 m/s",
            delegate { return MoonHopProblem(QualificationAppendixSolar(), 1000d); },
            new[] { "moon-soi" }, false, "Moon",
            new[] { "gravity", "third-body" }));
    }

    private static QualificationProblem QualificationAppendixPlanet(
        string id, string title, string label, Func<StarSystem> system, double years,
        string[] tags, bool heliocentric)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title;
        q.Tags = tags;
        q.AnalyticLevels = false;
        q.MeterTargets = new[] { 100d, 10d, 1d };
        q.FullOnly = true;
        q.OracleKind = "reference";
        q.OracleId = "dd-taylor-pair-v1";
        q.OracleNote = "two independent dd-Taylor references (43200 s / 21600 s); floor = worst pair difference";
        q.AuditKind = "planet";
        q.CentralBody = heliocentric ? "Sun" : "barycentre (no central body)";
        q.Forces = heliocentric ? new[] { "gravity", "third-body" } : new[] { "gravity" };
        q.Build = delegate
        {
            StarSystem sys = system();
            StudyNBody nb = StudyMakeNBody(sys);
            double sampleEvery = 30d * 86400d;
            StudyRefResult reference = StudyPlanetIndependentReference(nb, years, sampleEvery);
            StudyRefResult alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery);
            StudyProblem p = heliocentric
                ? StudyBridgeHeliocentricProblem(label, sys, nb, 0, 1, 2, reference, alternative)
                : StudyBridgePlanetProblem(label, nb, years, reference, alternative);
            p.Label = QualificationLabel(id, title);
            return p;
        };
        return q;
    }

    /// <summary>
    /// F-tier adapter over a Test559 builder. The builder already produces a certified
    /// reference (local fixed DOP853 plus an independent second reference where available).
    /// </summary>
    private static QualificationProblem QualificationAppendixPure(
        string id, string title, Func<StudyProblem> build, string[] tags, bool fineGoals,
        string centralBody, string[] forces)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title;
        q.Tags = tags;
        q.AnalyticLevels = false;
        q.MeterTargets = fineGoals ? QualificationMeterGoals : new[] { 100d, 10d, 1d };
        q.OracleKind = "reference";
        q.OracleId = "ship-certified-dop853-local-v1";
        q.OracleNote = "Test559 pure-gravity builder: certified fixed DOP853 local reference"
            + " + independent second reference where the case has one";
        q.AuditKind = "ship";
        q.CentralBody = centralBody;
        q.Forces = forces;
        q.Build = delegate
        {
            StudyProblem p = build();
            p.Label = QualificationLabel(id, title);
            return p;
        };
        return q;
    }
}
