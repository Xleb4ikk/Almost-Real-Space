# Test560b - fourth review fixes

This note records the changes made after the fourth review. No production
physics, cost formula, 20% tie band or production default was changed. All
numbers below are read from the final `results/decision/Test560b_*.csv` files;
the full run finished with `ALL PASS (1 FINDING)`.

## 1. Tie-break by error at equal cost

`DecisionBBuildTable` now sorts every candidate list by
`DecisionBComparableCost` first and by `err/target` second, but **only** when the
costs are equal within `1e-9 * max(1, cost)`. The same comparer is used for
`ok`, `edge`, `prodCandidates`, `portedCandidates`, `allCandidates` and
`allTies`. A different cost keeps the old cost order.

`best_all` ties now show every member as `method@cost/errFrac`; the full `why`
text shows the selected member's error and the lowest-error member in the tie.
A validity row `tie_break_by_error` checks, for every cell, that the selected
member has the lowest `err/target` inside the exact minimum-cost group.

Effect (same cost, better error wins):

| cell | before | after |
|---|---|---|
| Heliocentric 0.3 AU e 0.5, 1 y / 10 km | `tie: RK4, PEFRL`; best_ported RK4 525,600 | `tie: PEFRL@525,600/6.00E-005, RK4@525,600/1.22E-004`; best_ported PEFRL |
| Heliocentric 5 AU e 0.3, 10 y [regime86400] / 100 km | `tie: RK4, PEFRL`; best_ported RK4 36,640 | `tie: PEFRL@36,640/4.26E-003, RK4@36,640/2.53E-002`; best_ported PEFRL |
| High orbit 200 000 km, 30 d / 100 m | `tie: RK4, PEFRL`; best_ported RK4 43,200 | `tie: PEFRL@43,200/1.71E-004, RK4@43,200/2.28E-004`; best_ported PEFRL |
| GEO 35 786 km, 30 d / 100 m | `tie: Y6C, Y6, PEFRL`; best_ported Y6C 95,040 | `tie: Y6@95,040/4.20E-002, Y6C@..., PEFRL@...`; best_ported Y6 |

`selection_metric_consistent` re-selects every ok/edge cell from
`Test560b_Runs.csv` using `cost_if_ported` and compares the control with the
matrix row: 0 mismatches. It also reports 0 cells where the old `cost_total`
order would have chosen a different control.

## 2. Reference certification

The old acceptance check only asked for a finite floor and called every
non-finite floor "uncertified". It now separates the two positive
certificates and does **not** call an unratioed resolution "certified":

- `ratio_ok` = `LocalAsymptotic`;
- `resolved_finest_delta` = `LocalUnratioedResolutionUsed`;
- `not_certified` = `!LocalValid || !LocalResolved`.

`Test560b_Validity.csv` now has one `reference_resolution` row per
production/regime86400 scenario/config with `floor_m`, `floor_v` and the
reference note. `Test560b_DecisionMatrix.md` has a `reference` column in the
Scenarios table, and `Test560b_DecisionDetails.md` has a `reference:` line per
cell.

Final counts (16 production/regime configurations):

**2 ratio_ok, 14 resolved_finest_delta, 0 not_certified.**

The two `ratio_ok` rows are surface descent and LEO 300 km, 30 d. The 14
`resolved_finest_delta` rows are labelled as the weaker certificate in the
CSV note; they are not called Richardson-certified. `AllowUnratioedResolution`
and the floors were not changed.

## 3. Scope and bound

`Test560b_DecisionTable.csv/.md` now have `scope` and `bound`:

- `scope` = `config`, `max_step` or `chunk` (which axis the row belongs to);
- `bound` is a **heuristic** label: `chunk` when `steps <= 1.2*chunks`;
  otherwise `max-step` when `maxStep > 0` and
  `steps >= 0.8*(duration/maxStep)`; otherwise `tolerance`.

The new section **"Max-step variants (informational, not mixed into best_all)"**
is in `Test560b_DecisionTable.md` and `Test560b_DecisionDetails.md`; the
per-cell tables in `Test560b_DecisionMatrix.md` have the same section. It lists
every executed production row with `max_step != config_max_step` and status
ok/edge, and those rows are not candidates for `best_all`/`best_production`.
The existing production/regime86400 split is unchanged.

Far-flight production `why` now says `max-step-bound (600 s)`. The max-step
section shows:

| method | max step | err | cost total | ported |
|---|---:|---:|---:|---:|
| DOPRI5-adaptive [cap=86400 s] | 86 400 | 9.174E-003 m | 25 648 | **69 616** |
| DOP853-adaptive [cap=86400 s] | 86 400 | 9.203E-003 m | 47 632 | **135 568** |
| RKN12(10) [cap=86400 s] | 86 400 | 5.720E-003 m | 65 952 | 190 528 |
| IAS15 [cap=86400 s] | 86 400 | 9.392E-003 m | 80 496 | 234 160 |

DOPRI5-adaptive `[cap=86400 s]` is 69,616 comparable / 0.00917 m, 22.7x cheaper
than the `best_all` Leapfrog 1,580,464 at the same 100 km target.

## 4. Borderline parity and atol

`local_frame_error_control` now has three bands:

- `PASS/MATCHED` when `value <= 0.5*threshold`;
- `BORDERLINE` when `0.5*threshold < value <= threshold`;
- `FINDING` when `value > threshold`.

The LEO 300 km matched-atol value is 9.834959 m against a threshold of 10 m,
i.e. **BORDERLINE, 98.35% of the threshold**, not a pass. The game-default
value remains a separate **FINDING: 68.059 m**.

`DecisionBRun`, `Test560b_Runs.csv` and `Test560b_DecisionMatrix.csv` now carry
`production_atol` and `stand_atol`:

- production rows: `production_atol=1e-4/1e-8/1e-4`, `stand_atol` empty;
- stand rows: `stand_atol=0/0/0`, `production_atol` empty.

The full `why` text says `production atol 1e-4/1e-8/1e-4 vs stand Atol=0`.

Every production scenario now has one `production_parity_game_default` row (one
target per scenario), reusing the stand DOPRI5 control selected in the matrix;
the production run is capped at 10M RHS. The existing LEO 300 (rtol 1e-12) and
surface-ascent rows are kept and not duplicated. Final parity rows:

| scenario | value, m | threshold, m | status |
|---|---:|---:|---|
| LEO 300 km, 30 d (rtol 1e-12) | 68.059 | 10 | FINDING |
| local_frame_error_control, LEO 300 km, 30 d | 9.835 | 10 | BORDERLINE (98.35%) |
| LEO 200 km, 1 d | 0.897 | 1 | BORDERLINE |
| Cislunar Moon flyby | 7.101 | 10 | BORDERLINE |
| GEO 35 786 km, 30 d | 0.754 | 10 | PASS |
| High orbit 200 000 km, 30 d | 3.18E-05 | 10 | PASS |
| Surface descent 2 km | 3.40E-06 | 0.01 | PASS |
| Surface hover 0.5 km | 2.89E-08 | 0.01 | PASS |
| Surface level 0.5 km | 2.42E-08 | 0.01 | PASS |
| Surface level 1 km | 1.52E-08 | 0.01 | PASS |
| Heliocentric 0.3 AU, 1 y | 0.012 | 1000 | PASS |
| Heliocentric 5 AU [forced Sun] | 1.330 | 10000 | PASS |
| Far flight 30 AU, 10 y | 0.315 | 10000 | PASS |

The final line is now `ALL PASS (1 FINDING)`; FINDING is reported, not counted
as a test failure. Three BORDERLINE rows are listed separately.

## 5. Fixed-step h_eff

`DecisionBRun.HAvg = duration / accepted_steps` (NaN when steps = 0). It is in
`Test560b_Runs.csv`, `Test560b_DecisionMatrix.csv` and
`Test560b_ChunkSensitivity.csv` as `h_avg`. `Test560b_DecisionTable.md` and
`Test560b_DecisionDetails.md` show `h_req` and `h_avg` for every executed fixed
method.

The cislunar DOPRI5-fixed refinement control 424.264 s really runs two 300 s
steps per 600 s chunk, so its `h_avg` is 300 s
(`Test560b_Runs.csv`: control 424.26406871192853, steps 2880, chunks 1440,
h_avg 300). It is the same executed trajectory as the 300 s request, so the
refinement skips it from the selection list (1e-6 relative `h_avg` rule) and
keeps it in `Test560b_Runs.csv`; no duplicate decision row is created. Cost and
error were not changed.

## 6. Labels and transparency

- `best_production` is labelled `[current default]` for DOP853 and
  `[kernel switch]` for DOPRI5; `Test560b_DecisionTable.csv` has
  `best_production_kind`.
- The 5 AU `regime86400` scenario is labelled
  `Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]`.
- Surface ascent/descent `why` includes
  `prescribed thrust schedule; open-loop`. They stay in the normal decision
  table; only the note was added.
- `target_sweep` is renamed `target_sweep_executed` (and
  `production_target_sweep_executed`), with the note
  `reuses executed runs; no re-run at 0.1x/10x`.
- `DecisionBFindEventProduction` and `DecisionBFindEventStand` now clamp chunks,
  breakpoints and measurement checkpoints exactly like the main path. A
  validity row `thrust_event_clamp` records that no current production scenario
  combines thrust with a touchdown/SOI event, and that both event paths share
  the clamp.

## 7. Final run and size

- `Test560b_IntegratorDecisionMatrix`: **ALL PASS (1 FINDING)**, 6,291,835 ms
  (about 104.9 min), 3 BORDERLINE rows.
- Coverage: **475 rows** = 19 scenarios x 25 methods; `ranked=241`, `n/a=99`,
  `not run=135`, `unresolved=0`.
- Decision matrix: 650 rows; decision table: 21 rows (18 production cells +
  3 regime86400 rows).
- References: **2 ratio_ok, 14 resolved_finest_delta, 0 not_certified**.
- Dominance is unchanged: all-method RK4 12/18 raw `cost_total`; production
  DOPRI5 15/18. `cost_rhs` changes 0/18; `cost_if_ported` changes 0/18;
  all-method wall-clock changes 1/18.
- Production default is still **DOP853**; `SpacecraftPhysics.MaxStepSize=0` and
  `DominantBodyOverride=null` are unchanged.

## 8. Regression

- `Test34_GoldenState`: **ALL PASS** (`CALLS=119`, bit-identical golden).
- `Test551_IntegratorFrameworkSelfTest`: **ALL PASS**.
- `Test556_IntegratorWorkPrecisionCrossovers`: **ALL PASS**; its regenerated
  audited Markdown was restored from git after the run.
- `Test557_ProductionChunking`: **ALL PASS**.
- The tracked audited `results/Test552...Test559` files were compared by
  SHA256 before/after and are unchanged.

## 9. Files

Updated code: `Study/decisionb/DecisionBCore.cs`,
`Study/decisionb/DecisionBReport.cs`, `Study/decisionb/DecisionBAudit.cs`,
`Study/decisionb/DecisionBScenarios.cs`, `Study/tests/Test560b.cs`.

Updated results: `Test560b_Runs.csv`, `Test560b_DecisionMatrix.csv/.md`,
`Test560b_DecisionTable.csv/.md`, `Test560b_DecisionDetails.md`,
`Test560b_Validity.csv`, `Test560b_ChunkSensitivity.csv`,
`Test560b_MethodCoverage.csv`, `Test560b_DominanceAudit.csv/.md`,
`Test560b_HeliocentricIsolation.csv/.md`, `Test560b_Events.csv`,
`Test560b_Warp.csv`.

New: this file.

Production physics (`SpacecraftPhysics`, `SpacecraftOrbitIntegrator`), the cost
formulas, the 20% tie band, the production default and the audited
Test552-Test559 files were not changed.
