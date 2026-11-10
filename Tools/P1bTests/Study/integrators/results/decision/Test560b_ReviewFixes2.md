# Test560b - second review fixes

This note records the changes made after the second review. All numbers are
read from the final `Test560b_*.csv` files in this directory; the full run
ended with `ALL PASS` (1,571,573 ms, about 26.2 min).

## 1. DOPRI5 parity: game-default atol vs true matched atol

`Test560b_Validity.csv` has:

| check | scenario | method | value | threshold | status |
|---|---|---|---:|---:|---|
| production_parity_game_default | LEO 300 km, 30 d | DOPRI5 | 68.059126 m | 10 | FINDING |
| production_parity_matched_atol | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS |
| local_frame_error_control | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS/MATCHED |
| production_parity_matched_atol_reference | LEO 200 km, 1 d | DOP853 | 0.007122 m | 1 | PASS |
| production_parity_game_default | Surface ascent 0.5 km | DOPRI5 | 1.92624E-05 m | 0.01 | PASS |

- Game-default atol (`1e-4/1e-8/1e-4`), `rtol=1e-12`: production **95.36 m**,
  stand **27.30 m**, diff **68.06 m** → FINDING.
- The previous "matched" row used production atol `1e-6/1e-10/1e-8` while the
  stand adaptive integrator has `cfg.Atol=0`; it was not fully matched.
- True matched atol (`0/0/0` production, `cfg.Atol=0` stand, same
  `rtol=1e-12`): production **17.46 m**, stand **27.30 m**, diff **9.835 m**
  → PASS/MATCHED. It is below `target/10 = 10 m` but **not zero** (98% of the
  threshold). The DOP853 matched-atol (`0/0/0`) reference on LEO 300 is
  **0.4550 m**.
- No local-frame bug is claimed at the target/10 threshold; the game-default
  difference is an atol effect. The true matched residual is reported as a
  residual, not as a proven controller bug. No production code was changed.

The DOP853 matched-atol rows remain as the reference (surface, LEO 200, GEO);
the LEO 300 DOP853 matched reference is 0.4550 m and is recorded in the
`local_frame_error_control` note.

## 2. Far/heliocentric table in ReviewFixes corrected

`Test560b_ReviewFixes.md` section 5 no longer labels `best_all` as
"best prod-600". It now has, for each configuration, separate
`best_production`, `best_all`, `best_ported`, `best_stand_raw` and `ratio`
columns. The corrected production values are:

| scenario | config | best_production |
|---|---|---:|
| Heliocentric 0.3 AU e 0.5, 1 y | production | DOPRI5 1,156,320 |
| Heliocentric 0.3 AU e 0.5, 1 y | regime86400 | DOP853 16,583 |
| Heliocentric 5 AU e 0.3, 10 y [forced Sun] | production | DOPRI5 11,563,200 |
| Heliocentric 5 AU e 0.3, 10 y | regime86400 | DOPRI5 80,608 |
| Far flight 30 AU e 0.5, 10 y | production | DOPRI5 9,475,456 |
| Far flight 30 AU e 0.5, 10 y | regime86400 | DOPRI5 80,608 |

The DOP853 production/regime ratios remain 139.6x (far), 117.3x (0.3 AU) and
143.4x (5 AU).

## 3. Decision table selected totals and edge comparison

`Test560b_DecisionTable.csv` now has:

- `selected_best_all_total` (the ok run defining `best_all`);
- `selected_production_method`;
- `selected_production_total`;
- `cheapest_edge_method` / `cheapest_edge_total`;
- `edge_policy` comparing the cheapest edge with **both** bases.

GEO example: `selected_best_all_total` = 95,040 (Y6C ported),
`selected_production_total` = 159,840 (DOP853), `cheapest_edge_total` = 82,080
(DOPRI5-fixed ported). The edge is 14% cheaper than best_all and 49% cheaper
than production; it is not selected because err/target > 0.8.

## 4. Compact decision table

`Test560b_DecisionTable.md` is now the compact table:

`regime | scenario | target | best_production | best_ported | best_all | confidence | why`

The full acceptable percentages and per-row details are in
`Test560b_DecisionTable.csv` and the new `Test560b_DecisionDetails.md`.
Hover/level are a separate section labelled **prescribed-force; no closed-loop
altitude hold**; sensitivity rows are a separate section too.

## 5. Dominance wording

`Test560b_DominanceAudit.md` no longer says "artifact". It now states:

- production: DOPRI5 15/18 at 1x `cost_total`, chunk-bound (600 s cap);
- all-method: RK4 12/18 raw `cost_total`, target/chunk-dependent;
- `cost_rhs` changes: 0/18; `cost_if_ported` changes: 0/18;
- production-only wall-clock changes: 0/18; all-method wall-clock changes:
  1/18 in this run (the previous run had 2/18; wall-clock is timing-noisy).

The raw all-method view is explicitly marked as not the production comparison.

## 6. Final checks

- `Test560b_IntegratorDecisionMatrix`: **ALL PASS**.
- `Test34_GoldenState`: **ALL PASS** (`CALLS=119`).
- `Test551_IntegratorFrameworkSelfTest`: **ALL PASS**.
- `Test556_IntegratorWorkPrecisionCrossovers`: **ALL PASS** (audited output
  restored from git after the run).
- `Test557_ProductionChunking`: **ALL PASS**.
- Production default: **DOP853**; `SpacecraftPhysics.MaxStepSize=0` and
  `DominantBodyOverride=null` are unchanged.
- 475 coverage rows (19 scenarios x 25 methods); no method dropped.
- Audited `results/Test552...Test559` were not modified.

## 7. Files

Updated: `Test560b_Validity.csv`, `Test560b_DecisionTable.csv/md`,
`Test560b_DecisionDetails.md`, `Test560b_DecisionMatrix.csv/md`,
`Test560b_MethodCoverage.csv`, `Test560b_DominanceAudit.csv/md`,
`Test560b_HeliocentricIsolation.csv/md`, `Test560b_ChunkSensitivity.csv`,
`Test560b_ReviewFixes.md`.
New: this file and `Test560b_ReviewFixes3.md` (third review).

Code touched: `Study/decisionb/DecisionBAudit.cs`, `DecisionBReport.cs`,
`Study/tests/Test560b.cs` (plus the earlier `DecisionBScenarios.cs` /
`SpacecraftOrbitMatrix.cs` changes from the first review).
