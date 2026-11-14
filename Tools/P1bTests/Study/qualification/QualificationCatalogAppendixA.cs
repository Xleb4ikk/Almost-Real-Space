using System;
using System.Collections.Generic;
using P1bStudy.Framework;

// TASK.md Appendix A: tiers S (ship), P (planets) and F (pure-gravity regimes).
//
// Every entry is a thin adapter over the existing builder of the old test (Test552/553/559).
// No initial condition, force or reference parameter is redefined here: the adapter only
// assigns the stable id, the tags of A3, the metre goals of A7 and the audit route of G8.
internal static partial class P1bTests
{
    private static void QualificationAppendAppendixA(
        List<QualificationProblem> list, QualificationProfile profile, bool productionOnly = false)
    {
        QualificationAppendAppendixShips(list, profile, productionOnly);
    }

    // ---------------------------------------------------------------------------------
    // Tier S: Test552 ship scenarios (A4)
    // ---------------------------------------------------------------------------------

    private static void QualificationAppendAppendixShips(
        List<QualificationProblem> list, QualificationProfile profile, bool productionOnly = false)
    {
        // S2: ship near Earth, gravity and J2 (A10).
        list.Add(QualificationAppendixShip(
            "S01", "Earth LEO 400 km circular", "Earth LEO 400 km circular, 1 d",
            new[] { "leo" }, false));
        list.Add(QualificationAppendixShip(
            "S02", "Earth LEO 400 km polar", "Earth LEO 400 km polar, 1 d",
            new[] { "leo" }, false));
        list.Add(QualificationAppendixShip(
            "S03", "Earth LEO 400 km sun-synchronous", "Earth sun-synchronous 400 km, 1 d",
            new[] { "leo", "j2" }, false));
        list.Add(QualificationAppendixShip(
            "S04", "Earth GTO 400 x 40000 km", "Earth GTO 400 x 40000 km, 2 d",
            new[] { "gto", "ecc-high" }, false));
        list.Add(QualificationAppendixShip(
            "S05", "Earth Molniya 500 x 40000 km", "Earth Molniya 500 x 40000 km, 7 d",
            new[] { "molniya", "ecc-high" }, false));
        list.Add(QualificationAppendixShip(
            "S06", "Earth GEO 35786 km", "Earth GEO 35786 km, 30 d",
            new[] { "geo", "long-arc" }, false));
        list.Add(QualificationAppendixShip(
            "S09", "Earth 400 km + J2", "Earth LEO 400 km + J2, 1 d",
            new[] { "leo", "j2" }, false));
        list.Add(QualificationAppendixShip(
            "S14", "Earth HEO 300 x 120000 km", "Earth HEO 300 x 120000 km, 5 d",
            new[] { "heo", "ecc-high" }, false));

        // S3: velocity/mass-dependent forces and heliocentric flights (A10).
        // The production profile (Appendix C, C4) excludes them: S10/S11/S17 stay on DOP853
        // in the game and only enter the comparison to prove that candidates give N/A;
        // S07/S08 are heliocentric and not part of the C4 task list.
        if (!productionOnly)
        {
            list.Add(QualificationAppendixShip(
                "S10", "Earth 250 km + atmosphere drag", "Earth 250 km + atmosphere drag, 1 d",
                new[] { "leo", "drag", "velocity-dependent" }, false));
            list.Add(QualificationAppendixShip(
                "S11", "Earth LEO 400 km + 600 s prograde burn",
                "Earth LEO 400 km + 600 s prograde burn, 1 d",
                new[] { "leo", "thrust", "velocity-dependent" }, false));
            list.Add(QualificationAppendixShip(
                "S17", "Earth LEO 400 km + unaligned 600 s burn",
                "Earth LEO 400 km + unaligned 600 s burn, 1 d",
                new[] { "leo", "thrust", "velocity-dependent" }, false));
            list.Add(QualificationAppendixShip(
                "S07", "Sun 1 AU circular", "Sun 1 AU circular, 1 y",
                new[] { "heliocentric", "far-flight" }, false));
            list.Add(QualificationAppendixShip(
                "S08", "Sun 0.5 x 2 AU eccentric", "Sun 0.5 x 2 AU eccentric, 1 y",
                new[] { "heliocentric", "far-flight", "ecc-mid" }, false));
        }

        // S4: Moon flyby and Earth hyperbola; these carry the 0.1/0.01 m goals (A7).
        list.Add(QualificationAppendixShip(
            "S12", "Earth-Moon flyby inside lunar SOI (Moon-centered)",
            "Earth-Moon flyby inside lunar SOI, 2 d",
            new[] { "flyby", "moon-soi", "close-encounter" }, true));
        list.Add(QualificationAppendixShip(
            "S15", "Earth hyperbolic flyby e=1.5", "Earth hyperbolic flyby e=1.5, 1 d",
            new[] { "flyby", "escape", "close-encounter" }, true));
    }

    /// <summary>
    /// One Test552 case as a qualification problem: same case object, same reference builder,
    /// same ship adapter as Test552. Metre goals only (A7.3: no A1-A3 on S/P/F).
    /// </summary>
    private static QualificationProblem QualificationAppendixShip(
        string id, string caseLabel, string title, string[] tags, bool fineGoals)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title;
        q.Tags = tags;
        q.AnalyticLevels = false;
        q.MeterTargets = fineGoals
            ? QualificationMeterGoals
            : new[] { 100d, 10d, 1d };
        q.OracleKind = "reference";
        q.OracleId = "ship-certified-dop853-local-v1";
        q.OracleNote = "Test552 case + StudyShipMatrixBuildReferences (certified fixed DOP853, "
            + "local frame) with the independent cross-check where the case has one";
        q.AuditKind = "ship";

        StudyShipMatrixCase found = null;
        foreach (StudyShipMatrixCase c in QualificationShipCases())
        {
            if (string.Equals(c.Label, caseLabel, StringComparison.Ordinal))
            {
                found = c;
                break;
            }
        }

        if (found == null)
        {
            q.Status = "INVALID";
            q.AuditNote = "case not found in StudyShipMatrixBuildCases: " + caseLabel;
            return q;
        }

        try
        {
            StudyShipMatrixSetTargets(found);
            StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(found);
            StudyProblem p = StudyBridgeShipProblem(found, refs);
            p.Label = QualificationLabel(id, title);
            q.Build = delegate { return p; };
            q.CentralBody = found.Sys.AllBodies[found.Central].Name;
            q.Forces = QualificationShipForces(found);
            q.Floor = p.Floor;
            q.OracleTimes = (double[])p.CheckpointTimes.Clone();
            q.Oracle = QualificationCloneStates(p.CheckpointRef);
            q.Residual = p.ReferenceCrossDiffM;
            q.AuditNote = "reference " + p.ReferenceNote
                + "; floor " + QualificationFormat(p.Floor)
                + "; independent cross-diff " + QualificationFormat(p.ReferenceCrossDiffM)
                + (p.ReferenceIndependent ? " (independent)" : " (single reference)");
        }
        catch (Exception ex)
        {
            q.Status = "INVALID";
            q.AuditNote = ex.GetType().Name + ": " + ex.Message;
        }

        return q;
    }

    private static string[] QualificationShipForces(StudyShipMatrixCase c)
    {
        var forces = new List<string> { "gravity", "third-body" };
        if (c.Sys.AllBodies[c.Central].J2 != 0d)
        {
            forces.Add("j2");
        }

        if (c.Drag != null)
        {
            forces.Add("drag");
        }

        if (c.Thrust)
        {
            forces.Add("thrust");
        }

        return forces.ToArray();
    }
}
