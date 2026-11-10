# Test560b - review fixes

This note records what changed in Test560b after the decision-table review.
All numbers below are read from the final `results/decision/Test560b_*.csv`
files; the full run finished with `ALL PASS`.

## 1. What was changed

1. **Surface hover / level flight added.** Three new scenarios:
   `Surface hover 0.5 km, 0 m/s relative`,
   `Surface level flight 0.5 km, 100 m/s horizontal`,
   `Surface level flight 1 km, 100 m/s horizontal`.
   Horizon 60 s, production chunk 0.05 s, drag + thrust + variable mass.
   These are **prescribed-force** trajectories; `ThrustScalesWithMass` uses a
   linear mass estimate. No closed-loop altitude hold is claimed. Ascent and
   descent are unchanged.
2. **`cost_if_ported` added.** Production rows use `cost_total`; stand rows use
   `cost_total + 2*(raw_rhs - chunks)`, the production local-frame convention.
   The column is in `Test560b_Runs.csv`, `Test560b_DecisionMatrix.csv`,
   `Test560b_ChunkSensitivity.csv` and the decision table.
3. **Decision table is split into four winners:** `best_production`,
   `best_ported`, `best_stand_raw`, `best_all_valid` (plus `best_all_class`).
   `best_ported` and `best_all_valid` use `cost_if_ported`; `best_stand_raw` is
   marked raw / requires port / not comparable.
4. **Two (three) explicit percentage bases.** Every acceptable entry now shows
   `vs best_production`, `vs best_all`, and `vs best_stand_raw` (raw, not
   comparable). The table also has `production_vs_best_all_pct`,
   `best_all_vs_production_pct` and `best_production_vs_best_stand_raw_pct`.
5. **Edge policy is explicit.** The table stores `selected_best_all_total`,
   `selected_production_total`, `selected_production_method`,
   `cheapest_edge_method`, `cheapest_edge_total` and an `edge_policy` string.
   A cheaper edge is shown, not promoted to ok. The `why` column compares the
   cheapest edge with both the best_all and production bases.
6. **DOPRI5 parity split into game-default atol and a true matched atol.**
   `Test560b_Validity.csv` has `production_parity_game_default`,
   `production_parity_matched_atol` and `local_frame_error_control`; the true
   matched row now uses production `0/0/0` against stand `cfg.Atol=0` (same
   `rtol=1e-12`). The game-default difference is an atol effect; the true
   matched difference is 9.835 m, below `target/10` but not zero. No
   local-frame bug is claimed at that threshold. Production code was not
   changed.
7. **Audit split.** Wall-clock and `cost_rhs` checks are separate for all
   methods and production-only. A `cost_if_ported` winner check was added.
   Far/heliocentric sections show both production-600 and regime86400, including
   the DOP853 ratios.
8. **Scope label.** The report now states that every scenario is translational
   centre-of-mass motion; `AttitudePhysics`, `VesselFlightModel`, gimbal/RCS and
   the contact/landing model are not tested. `touchdown` is a sphere-crossing
   event, not a contact model.

## 2. Final size and coverage

- 19 scenarios: 7 surface (ascent, ascent 0.5 s sensitivity, descent,
  descent 0.5 s sensitivity, hover, level 0.5, level 1), 4 LEO/high-orbit
  (LEO 200, LEO 300, GEO, 200 000 km), 1 cislunar, 5 heliocentric
  (0.3 AU production + regime, 5 AU auto-bug + forced Sun + regime), and
  2 far-flight (production + regime). The two 0.5 s surface rows are chunk
  sensitivity, not production decisions.
- 25 methods: 2 production + 15 `StudyRegistry.All()` + 8 cap variants.
- 475 coverage rows = 19 scenarios x 25 methods. Statuses:
  `ranked=241`, `n/a=99`, `not run=135`, `unresolved=0`.
- 650 decision-matrix rows; 21 decision-table rows (18 production cells +
  3 `regime86400` rows).
- Original 12 production cells are still present and still show the accepted
  result: **DOPRI5 wins 9/12**, DOP853 wins LEO 200, LEO 300 and GEO, and RK4
  wins 6/12 under the all-method raw view.
- With the six new surface cells the expanded set is 18 production cells:
  DOPRI5 wins 15/18 (all six new surface cells) and RK4 wins 12/18. The extra
  surface cells are the reason the count is larger; they are not a changed
  ranking of the original 12.

## 3. DOPRI5 parity: game-default atol vs true matched atol

`Test560b_Validity.csv` separates the two comparisons:

| check | scenario | method | value | threshold | status |
|---|---|---|---:|---:|---|
| production_parity_game_default | LEO 300 km, 30 d | DOPRI5 | 68.059126 m | 10 | FINDING |
| production_parity_matched_atol | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS |
| local_frame_error_control | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS/MATCHED |
| production_parity_matched_atol_reference | LEO 200 km, 1 d | DOP853 | 0.007122 m | 1 | PASS |
| production_parity_game_default | Surface ascent 0.5 km | DOPRI5 | 1.92624E-05 m | 0.01 | PASS |

Game-default atol (`1e-4/1e-8/1e-4`) at `rtol=1e-12`: production **95.36 m**,
stand **27.30 m**, difference **68.06 m** (3.49x) — this is the FINDING.

The earlier "matched" row used production atol `1e-6/1e-10/1e-8` while the
stand adaptive integrator runs with `cfg.Atol = 0`; that was not a matched
comparison. The true matched row now uses production `0/0/0` and stand
`cfg.Atol = 0` with the same `rtol=1e-12`, so only the local frame/controller
differs. It gives production **17.46 m**, stand **27.30 m**, difference
**9.835 m**, below `target/10 = 10 m` → PASS/MATCHED. This is **not zero**:
it is 98% of the threshold, so the local-frame/controller residual is visible
even if it does not cross the finding threshold. The DOP853 matched-atol
(`0/0/0`) reference on the same LEO 300 scenario is **0.4550 m**.

Therefore the earlier wording "local-frame DOPRI5 bug" is **not supported at
the target/10 threshold**: the game-default difference is an **atol effect**.
The true matched residual (9.835 m) is much larger than DOP853's (0.455 m), but
it remains below the stated finding threshold and is reported as a residual,
not as a proven local-frame bug. The production default is unchanged; no
production code was fixed.

For reference, the old production best on this scenario was 58.02 m at
`rtol=1e-13` with the game-default atol. It is still a game-default-atol row,
not evidence of a frame bug.

## 4. Percentages and the GEO example

GEO, position, 100 m, `production`:

- `best_production` = DOP853 [production], cost_total 159,840.
- `best_ported` / `best_all_valid` = Y6C (requires port), cost_if_ported 95,040.
- `best_stand_raw` = Y6C, raw 34,560 (not comparable).
- DOPRI5 [production] appears as **+65% vs best_production**, **+177% vs
  best_all**, and **+662% vs best_stand_raw**. The old "+662%" was the raw-stand
  base; it is still visible, but it is no longer the headline comparison.
- `cheapest_edge_method` = DOPRI5-fixed (requires port), `cheapest_edge_total`
  = 82,080; `selected_best_all_total` = 95,040 and `selected_production_total`
  = 159,840 (DOP853). The edge is 14% cheaper than best_all and 49% cheaper
  than production, but is **not** selected because its error/target is above
  0.8. The row does not say "no edge".

The same pattern is why `best_all_valid` must not be read as a production
recommendation: for surface ascent it is RK4 ported at 12,024 versus raw 4,808,
while the production recommendation is still DOPRI5 at 26,422. The surface
hover/level rows are **prescribed-force** trajectories, not closed-loop altitude
hold; their thrust schedule uses a linear mass estimate.

## 5. Far / heliocentric: both configurations

Corrected table. `best_production` and `best_all` are different columns; the
previous production column used `best_all` values and is not used as production.

| scenario | config | best_production | best_all | best_ported | best_stand_raw | ratio (prod-600 / regime86400, best_all) |
|---|---|---|---|---|---|---:|
| Heliocentric 0.3 AU e 0.5, 1 y | production | DOPRI5 1,156,320 | RK4/PEFRL 525,600 | RK4 525,600 | RK4 210,240 | 47.8x |
| Heliocentric 0.3 AU e 0.5, 1 y | regime86400 | DOP853 16,583 | WH-Y6 10,988 | WH-Y6 10,988 | SRKN14A 5,152 | — |
| Heliocentric 5 AU e 0.3, 10 y [forced Sun] | production | DOPRI5 11,563,200 | Leapfrog 2,102,400 | Leapfrog 2,102,400 | Leapfrog 1,051,200 | 57.4x |
| Heliocentric 5 AU e 0.3, 10 y | regime86400 | DOPRI5 80,608 | RK4/PEFRL 36,640 | RK4 36,640 | RK4 14,656 | — |
| Far flight 30 AU e 0.5, 10 y | production | DOPRI5 9,475,456 | Leapfrog 1,580,464 | Leapfrog 1,580,464 | Leapfrog 529,264 | 107.8x |
| Far flight 30 AU e 0.5, 10 y | regime86400 | DOPRI5 80,608 | Leapfrog 14,656 | Leapfrog 14,656 | Leapfrog 7,328 | — |

DOP853 reference ratios (production-600 / regime86400): far **139.6x**,
heliocentric 0.3 AU **117.3x**, heliocentric 5 AU **143.4x**. The two
configurations are separate decision-table rows and are never folded into one
winner.

## 6. Wall-clock and metric sensitivity

From `Test560b_DominanceAudit.csv`:

- production: DOPRI5 wins **15 / 18** production cells at 1x `cost_total`;
  the win is chunk-bound (600 s production cap) and changes in 2 target cells
  and 1 chunk group;
- all-method: RK4 wins **12 / 18** raw `cost_total` cells; this is
  target/chunk-dependent, not a universal ranking;
- production-only wall-clock winner differs in **0 / 18** production cells;
- production-only `cost_rhs` winner differs in **0 / 18** production cells;
- `cost_if_ported` winner differs from raw `cost_total` in **0 / 18** cells;
- all-method wall-clock winner differs from `cost_total` in **1 / 18** cells
  (the previous full run had 2/18; wall-clock is timing-noisy).

These are different measurements and are kept as separate rows. Wall-clock is
noisy and diagnostic; `cost_total` / `cost_if_ported` remain the ranking units.

## 7. What changed in the conclusions

Changed:

- `best_all_valid` is now a comparable (ported) number, not raw stand cost.
  The surface, far and heliocentric winners are port candidates; their
  `cost_if_ported` overhead is 2.0-3.0x the raw stand cost.
- GEO DOPRI5 is no longer reported only as "+662%"; the production base is +65%.
- DOPRI5 game-default atol gives a FINDING (68.059 m); true matched atol
  (`0/0/0` vs stand `Atol=0`) gives 9.835 m, just below `target/10 = 10 m`
  → PASS/MATCHED, but not zero. The difference is dominated by atol, not by a
  proven local-frame controller bug.
- GEO's cheap edge is visible (DOPRI5-fixed 82,080 vs selected_best_all 95,040
  and selected_production 159,840).
- Far/heliocentric now show both configurations and the 100-140x DOP853 ratio.
- Surface hover/level are explicitly labelled prescribed-force, not a
  closed-loop altitude hold.

Unchanged:

- Production default is still DOP853.
- DOPRI5 is still not universal: 9/12 on the original production cells;
  DOP853 wins LEO 200, LEO 300 and GEO.
- RK4 is still 6/12 on the original all-method cells; the expanded 12/18 is
  entirely the six new surface cells, which are cheap for RK4.
- The production dominant-body switch bug (5 AU auto path) is unchanged and
  remains an isolation finding.
- No local-frame DOPRI5 bug is claimed; the game-default atol difference is
  documented, not fixed.
- `AttitudePhysics`, `VesselFlightModel`, gimbal/RCS and contact are not tested
  by Test560b; all scenarios are translational centre-of-mass motion.

## 8. Verification

- `Test560b_IntegratorDecisionMatrix`: **ALL PASS**, full run
  1,571,573 ms (about 26.2 min).
- `Test34_GoldenState`: **ALL PASS** (`CALLS=119`, bit-identical golden).
- `Test551_IntegratorFrameworkSelfTest`: **ALL PASS**.
- `Test556_IntegratorWorkPrecisionCrossovers`: **ALL PASS** (its generated
  audited Markdown was restored from git after the run).
- `Test557_ProductionChunking`: **ALL PASS**.
- `SpacecraftPhysics.Integrator` default check: **DOP853**.
- Audited `results/Test552...Test559` were not edited by this work; Test560b
  writes only `results/decision/`.

## 9. Files

Updated: `Test560b_DecisionMatrix.csv/md`, `Test560b_DecisionTable.csv/md`,
`Test560b_DecisionDetails.md`, `Test560b_MethodCoverage.csv`,
`Test560b_Validity.csv`, `Test560b_DominanceAudit.csv/md`,
`Test560b_HeliocentricIsolation.csv/md`,
`Test560b_ChunkSensitivity.csv`.
New: this file, `Test560b_ReviewFixes2.md` (second review) and
`Test560b_ReviewFixes3.md` (third review: true matched atol and tie-aware
`best_all_class`).

Code touched: `Tools/P1bTests/Study/decisionb/DecisionBScenarios.cs`,
`DecisionBCore.cs`, `DecisionBReport.cs`, `DecisionBAudit.cs`,
`Tools/P1bTests/Study/tests/Test560b.cs`, and only the `ThrustScalesWithMass`
field/throttle branch in
`Tools/P1bTests/Study/integrators/SpacecraftOrbitMatrix.cs` (default false, so
the audited Test552-559 paths are unchanged).
