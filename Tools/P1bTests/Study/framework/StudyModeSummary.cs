using System.Collections.Generic;

namespace P1bStudy.Framework
{
    /// <summary>
    /// Mode-level aggregation. This is a thin wrapper around StudyRegimeAnalysis:
    /// every number is computed once from the same sweep rows, so the mode summary
    /// and the detailed map cannot disagree.
    /// </summary>
    internal static class StudyModeSummary
    {
        public static void Write(
            string path, string title, List<StudyScenarioResult> results, StudyOptions options)
        {
            StudyRegimeAnalysis.WriteModeSummary(path, title, results, options);
        }
    }
}
