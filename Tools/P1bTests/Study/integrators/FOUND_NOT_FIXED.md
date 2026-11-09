# Found but not fixed

This list contains issues found during the data audit that are outside the
T1-T9 scope, or that were deliberately left alone. None of them changes the
numbers reported in `REPORT_DATA_AUDIT.md` unless stated.

1. `results/` on disk did not contain the main Test552/553/554/558/559 CSVs.
   They existed only inside `Tools.zip`. The audit baseline was therefore
   taken from `Tools.zip` and copied to `results_baseline/`; the on-disk
   pre-audit files are in `results_baseline/on_disk_original/`.
   Not a code bug, but the canonical result location was incomplete.

2. Test553's `reference_family` column said `Runge-Kutta` while the actual
   reference is two dd-Taylor runs. Fixed as part of T6 (`ReferenceFamily =
   "Taylor"`), so this is recorded here only as a pre-audit data error.

3. `Test552_ModeSummary.analysis.csv`, `Test553_ModeSummary.analysis.csv`,
   `Test554_ModeSummary.analysis.csv`, `Test555_ModeSummary.analysis.csv`,
   `Test558_ModeSummary.analysis.csv` are byte-for-byte copies of the run CSV
   (same size), not a separate analysis table. Not fixed: changing their
   meaning would be a new report format, not a data correction.

4. `Test554_Production.csv` contains only a small production table (491 bytes
   in the baseline), not a full production sweep. Not fixed: outside T1-T9.

5. `README.md` and the body of `ORBIT_INTEGRATOR_TEST_SCHEME.md` still describe
   log-log interpolation. The scheme body is deliberately untouched (only the
   banner is allowed); the README was not part of the requested output. The
   generated Markdown header now states the single-price rule.

6. `StudyReport` still contains the now-unused `Interpolated`,
   `InterpolateTime` and `BaseKey` members. They are kept for format
   compatibility and to avoid a refactor; they are never set by the new rule.

7. `Test559_PureGravity_tie115.csv` and the `Test559_tie115_*` files are a
   separate manual 1.15-grid rerun, not produced by the default suite. Not
   fixed; the audit uses the default grid only.

8. The archived tests Test555 and Test557 still run in the full 551-559
   sequence and write their own outputs. They were not modified, as required.
   Their numbers are not part of the audit tables.

9. T2's literal check "LEO 1000 orbits: both modes within 10%" is not met by
   the fixed-step methods. This is not a T2 implementation bug: the
   all-checkpoint fixed-step grid is quantized by nck (P3/T3), so the
   end-only mode can find a cheaper arbitrary-step hit. Adaptive methods agree
   within 0.5%. The exact numbers are in `REPORT_DATA_AUDIT.md` (T2). Not
   "fixed", because T3 explicitly forbids inventing intermediate step counts
   in the all-checkpoint mode.

10. T3 leaves `bracket_wide=1` / `bracket_quantized=1` for fixed-step
    all-checkpoint cells where the step count jumps by nck. This is the
    requested behaviour, not a missing refinement.

11. The Kepler weight CV is above 10% for several scenarios (for example
    `Earth LEO 400 km circular`, CV ~29%, and `Moon orbit 100 km x1000
    orbits`, CV ~37%). The median w is stable across three independent runs
    within 6%, but the flag `w_unreliable=1` is set honestly. The high CV is
    wall-clock timing noise at 100 000 calls per block, not a changed force.

12. IAS15's error is non-monotonic in rtol on many series. `StudyIas15.cs:591`
    maps `control` directly to `core.Epsilon = max(rtol, 1e-16)`; the detailed
    list is in `REPORT_DATA_AUDIT.md` (T9). Not fixed: T9 asks for a statement,
    not a change.

13. GJ8's start-up calls are included in `calls` and therefore in `cost`; the
    new `calls_startup` column only exposes them. Not fixed: changing the cost
    accounting would be a new pricing rule.

14. The second-reference budget for T6 is 300 s / 20M RHS per scenario. In the
    final run every scenario has `ref_independent=1`; no scenario hit the
    budget. If a future scenario does, it will be listed by name and marked
    `ref_independent=0`, not silently promoted.

15. `Test553`'s `ref_independent=1` for the dd-Taylor references means "two
    independent step resolutions of the same family"; no participant is Taylor,
    so no different-family reference was required. If a reader interprets
    "independent" as "different family", this field is conservative only for
    Test553.
