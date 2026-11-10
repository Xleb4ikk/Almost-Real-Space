# Test560b - third review fixes

This note records the two point fixes made after the third review and the
numbers from the final full run. All numbers below are read from the final
`results/decision/Test560b_*.csv` files; the full run ended with `ALL PASS`
(1,571,573 ms, about 26.2 min).

## 1. DOPRI5 parity now uses a true matched atol

The previous `production_parity_matched_atol` row used production atol
`1e-6/1e-10/1e-8`, while the stand adaptive integrator runs with
`cfg.Atol = 0` (`AdaptiveIntegrator.MakeConfig`). That was not fully matched.

The fixed comparison uses production `PositionAbsoluteTolerance = 0`,
`VelocityAbsoluteTolerance = 0`, `MassAbsoluteTolerance = 0` and stand
`cfg.Atol = 0`, with the same `rtol = 1e-12`, same max step 600 s and the same
chunking. Only the local frame/controller differs.

`Test560b_Validity.csv`:

| check | scenario | method | value | threshold | status |
|---|---|---|---:|---:|---|
| production_parity_game_default | LEO 300 km, 30 d | DOPRI5 | 68.059126 m | 10 | FINDING |
| production_parity_matched_atol | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS |
| local_frame_error_control | LEO 300 km, 30 d | DOPRI5 | 9.834959 m | 10 | PASS/MATCHED |
| production_parity_matched_atol_reference | LEO 200 km, 1 d | DOP853 | 0.007122 m | 1 | PASS |
| production_parity_game_default | Surface ascent 0.5 km | DOPRI5 | 1.92624E-05 m | 0.01 | PASS |

- Game-default atol `1e-4/1e-8/1e-4`, `rtol=1e-12`: production **95.36 m**,
  stand DOPRI5 **27.30 m**, difference **68.059 m** → FINDING.
- True matched atol `0/0/0`, `rtol=1e-12`: production **17.46 m**, stand
  DOPRI5 **27.30 m**, difference **9.835 m** → PASS/MATCHED. It is below
  `target/10 = 10 m` but **not zero**: it is 98% of the threshold.
- DOP853 with the same matched atol `0/0/0` on LEO 300 gives **0.4550 m**.
- DOP853 reference rows with matched atol `0/0/0`: surface **1.9186E-05 m**,
  LEO 200 **0.007122 m**, GEO **0.00024358 m**.

Conclusion: the game-default 68.059 m difference is an **atol effect**. The
true matched difference is 9.835 m, so a residual local-frame/controller
difference is visible but does not cross the `target/10` finding threshold.
No local-frame bug is claimed, and no production code was changed. The old
7.318 m number is **not** called matched anywhere: it used `1e-6/1e-10/1e-8`
against stand `Atol=0`.

## 2. `best_all_class` is tie-aware

A tie in `best_all` is now classified from its members, not from the single
cheapest row:

- production + stand-only → `tie: production+stand-only`;
- stand-only only → `tie: stand-only`;
- production only → `tie: production`;
- no tie → `production` or `stand-only`.

The two affected rows:

| scenario | best_all_valid | best_all_class |
|---|---|---|
| Cislunar Moon flyby, SOI entry-exit, 10 d | `tie: DOPRI5-adaptive (requires port), DOPRI5 [production]` | `tie: production+stand-only` |
| LEO 200 km, 1 d | `tie: DOPRI5-fixed (requires port), DOP853-adaptive (requires port), DOP853 [production]` | `tie: production+stand-only` |

For these rows the compact `why` now says `best_all includes production;
production option ... is within X% of best_all` and does not call the whole
tie a port candidate. The full `why` says the selected stand option requires
a port, while the production option in the tie needs no port.
`Test560b_DecisionDetails.md` shows the tie class, not `(stand-only)`.

A validity row `best_all_tie_class` was added. It fails if a `best_all` string
starts with `tie:` and contains `[production]` while the class is
`stand-only` (or `tie: stand-only`). Both mixed ties pass; the stand-only ties
(RK4/PEFRL, Y6C/Y6/PEFRL) are classed `tie: stand-only`.

## 3. Full-run numbers

- `Test560b_IntegratorDecisionMatrix`: **ALL PASS**, 1,571,573 ms.
- Coverage: **475 rows** = 19 scenarios x 25 methods; `ranked=241`,
  `n/a=99`, `not run=135`, `unresolved=0`.
- Decision table: 21 rows (18 production cells + 3 `regime86400` rows).
- Dominance is unchanged: production DOPRI5 **15/18** at 1x `cost_total`
  (chunk-bound; target changes 2, chunk groups 1); all-method RK4 **12/18**
  raw `cost_total` (target changes 4, chunk groups 1).
- `cost_rhs` changes: **0/18**; `cost_if_ported` changes: **0/18**;
  production-only wall-clock changes: **0/18**; all-method wall-clock changes:
  **1/18**.
- Far/heliocentric production and regime86400 rows are unchanged; DOP853
  production/regime ratios remain 139.6x (far), 117.3x (0.3 AU), 143.4x
  (5 AU).

## 4. Regression

- `Test34_GoldenState`: **ALL PASS** (`CALLS=119`, bit-identical golden).
- `Test551_IntegratorFrameworkSelfTest`: **ALL PASS**.
- `Test556_IntegratorWorkPrecisionCrossovers`: **ALL PASS**; its regenerated
  audited Markdown was restored from git after the run.
- `Test557_ProductionChunking`: **ALL PASS**.
- Production default is still **DOP853**; `SpacecraftPhysics.MaxStepSize=0`
  and `DominantBodyOverride=null` are unchanged.
- The tracked audited `results/Test552...Test559` files were not modified by
  Test560b (SHA256 compared before/after). Test556's regenerated audited
  Markdown was restored from git; Test557 rewrote its own gitignored
  `Test557_ProductionChunking.csv` during its regression run, which is expected.

## 5. Files

Updated: `Test560b_Validity.csv`, `Test560b_DecisionTable.csv/md`,
`Test560b_DecisionDetails.md`, `Test560b_DecisionMatrix.csv/md`,
`Test560b_MethodCoverage.csv`, `Test560b_DominanceAudit.csv/md`,
`Test560b_HeliocentricIsolation.csv/md`, `Test560b_ChunkSensitivity.csv`,
`Test560b_ReviewFixes.md`, `Test560b_ReviewFixes2.md`.
New: this file.

Code touched: `Study/decisionb/DecisionBAudit.cs` (matched atol and notes),
`Study/decisionb/DecisionBReport.cs` (tie-aware class/why/details/validity),
`Study/tests/Test560b.cs` (tie validity check and acceptance check).
Production code and audited results were not changed.
