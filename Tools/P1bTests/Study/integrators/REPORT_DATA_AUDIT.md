# Data audit of the orbit-integrator stand (Test551-Test559)

No conclusions about which method is better are drawn here. Only numbers,
their meaning, and their verification are recorded.

Baseline: `results_baseline/` (see `MANIFEST.txt`), taken from `Tools.zip`
(commit `826ac2b` + dirty; that dirty tree became HEAD `8230943`). The snapshot
was validated on Test552 / "Earth LEO 400 km circular": 302/302 rows, 0 missing
keys, 0 mismatches in `calls` / `aux_ops` / `err_m` / `err_final_m`.
Environment: .NET 8.0.24, x64, not Unity, not IL2CPP.

---

## 1. P1-P10: confirmed before any fix

| # | Status | Evidence |
|---|---|---|
| P1 | confirmed | `StudySweep.cs:182-231` had `MeasureKeplerWeight`, but every stand test pinned `KeplerWeight=1d`: `PureGravityCases.cs:87`, `OrbitEdgeCases.cs:64`, `InvariantsAndSun.cs:52`, `LowAltitudeCases.cs:41`, `StudyBridge.cs:177,553`. From baseline CSV: LEO 30 d WH-BM14A 58 368 / 89 088 / 196 608 at w=1/3/10; RKN12 183 093. Moon 100 km x1000 WH-BM14A 103 936 / 211 456; RKN12 130 663. |
| P2 | confirmed | `cost/calls = 3.0` in every aux=0 row of Test552/554/558 and `1.0` in Test553/559. Solving `cost = m*calls + w*aux` gives `w=1.0` in all aux>0 rows. |
| P3 | confirmed (mechanism; aggregate count not reproduced) | `SpacecraftOrbitMatrix.cs:590-604` sets nck; `StudyIntegratorApi.cs:108-137` stops a fixed step on every interval, `:224-246` clips an adaptive step. Moon flyby 1000/100: nck=20; minimum steps: Encke/WH-BM14A/WH-Y6 20, IAS15/RKN12 25, DOPRI5/DOP853 27, RK4/PEFRL/Y6/Y6C/SRKN14A/Leapfrog 80. The external audit's "112 of 301" is not reproduced because its cell definition was not stated; this audit uses `checkpoint_bound = steps <= 1.5*nck` (T2) and reports its own count. |
| P4 | confirmed | `StudyReport.cs:163` interpolated only for adaptive or a declared order. WH-BM14A order NaN at `StudyAdvancedIntegrators.cs:117-120`, WH-Y6 at `StudyRegistry.cs:169`, Encke at `StudyAdvancedIntegrators.cs:204-207`. Baseline WH-BM14A Moon flyby 1000/100: (580, 3.060839) and (1160, 0.442552); the report took 1160. |
| P5 | confirmed | `StudyRegimeAnalysis.cs:415-451` grouped by `first.Integrator` and kept the minimum cell cost. `Test552_FinalMap.md` listed only base names; cap variants were not separate methods. |
| P6 | confirmed | Baseline, most accurate runs, "Earth LEO 400 km, 30 d": DOP853 E=5.7652864046394825 L=5087.779106140137; RKN12 E=5.765310790389776 L=5087.800621032715; IAS15 E=5.765300069004297 L=5087.791160583496; WH-BM14A E=5.765352230519056 L=5087.837188720703. "Moon orbit 100 km x1000 orbits": E=33.57926, L=37758.97 for all. This is physical drift. |
| P7 | confirmed, with correction | Test559: 57 scenarios `Runge-Kutta`, 9 `Gauss-Radau`; `certified=1` everywhere. Targets below 10*floor: 1 m in 1 scenario, 0.1 m in 6, 0.01 m in 14. `AllowUnratioedResolution=true` in 9 places. Correction: Test553 actually uses two dd-Taylor references, while the CSV said `Runge-Kutta`. |
| P8 | confirmed partially | Test559 uses `StudyShipMatrixSystem(false,false)` with bake=true (`SpacecraftOrbitMatrix.cs:439-466`): baked Chebyshev. `SeamAwareReference` is set only for "Earth-Moon flyby inside lunar SOI (Moon-centered)" (`:4231`, 0 seams in its window). In Test559 neither candidate nor reference lands on seams. The comment at `:928-930` describes the DD cross-check, not the graded reference. T7 measures the seam effect. |
| P9 | confirmed | Duplicate controls in baseline: 210 rows in Test552, 110 in Test554, 1872 in Test559 (also 47 in Test553, 279 in Test558). Cause: `StudySweep.cs:345-353` runs the coarse-up pass, `:403-413` runs the fine-down pass over the same grid. |
| P10 | confirmed partially | GJ8 calls/step: median 2.443, p95 7.195, max 27.815 in Test552; p95 58.812 in Test559. IAS15 maps `control` to `epsilon = max(rtol,1e-16)` (`StudyIas15.cs:591`). Non-monotonic: 129 of 220 IAS15 series have at least one error increase while rtol tightens; 19 of 220 have more than one pass/fail flip at 1 m. The audit's "15 of 108" is not reproduced exactly because its definition was not stated; the list is in T9. |

---

## 2. Task status

| Task | Status |
|---|---|
| Phase 0 snapshot | done (`results_baseline/MANIFEST.txt`; filtered run bit-identical on one scenario) |
| T1 unit / Kepler weight | done / verified |
| T2 checkpoints / end-only | done / verified, one literal check not met for fixed-step LEO 1000 (see T2) |
| T3 single price rule | done / verified |
| T4 cap variants separate | done / verified |
| T5 drift vs reference | done / verified |
| T6 reference metadata / second reference | done / verified; 4 scenarios had no second reference or no metadata and were fixed; 0 `ref_independent=0` in the final run |
| T7 ephemeris / seams | done (measurement only) |
| T8 duplicate sweep pass | done / verified (0 duplicates; duplicate work quantified) |
| T9 headers / GJ8 startup / IAS15 note | done |
| Final full run | done; see section 10 |

---

## 3. T1 - cost unit and Kepler weight

Code: `StudySweep.cs:182-231,249-250,550` and `StudyReport.cs:45-51,1013-1018,1064-1109`.
`cost = calls + w*aux`; `calls` and `aux_ops` are always written separately.
`w` is resolved as: explicit option, environment override, pinned per-scenario
value, or a fresh measurement that is then pinned in
`Tools/P1bTests/Study/integrators/kepler_weights.csv`. Measurement: warm-up,
12 blocks of 100 000 paired calls (first block discarded), median of 11 ratios,
CV over the 11 blocks, dt = 1e-3 * CharTime. `w_unreliable=1` when CV>10%.

Pinned values (source `measured`; 3 independent runs in parentheses):

| scenario | w | CV | 3-run values |
|---|---:|---:|---|
| Earth LEO 400 km circular | 0.7657834973504921 | 0.293051 | 0.7561 / 0.7681 / 0.7658 |
| Earth LEO 400 km, 30 d | 0.7356980511630156 | 0.010617 | 0.7124 / 0.7357 / 0.7528 |
| Moon orbit 100 km x1000 orbits | 0.7679311444054828 | 0.371570 | 0.7706 / 0.7679 / 0.7265 |
| Moon flyby vinf 1000 alt 100 | 3.3353813114595274 | 0.013589 | 3.3354 / 3.3667 / 3.3198 |
| Heliocentric 1 AU e 0.9 | 1.0908784704957737 | 0.017566 | 1.1155 / 1.0909 / 1.0799 |
| Earth flyby 3 R vinf 1000 | 4.431107705600887 | 0.015787 | 4.4499 / 4.4311 / 4.3121 |

All three-run spreads are below 10%. Several CVs are above 10% (flag set);
the median is stable, the block noise is wall-clock.

Cost at 1 m, same executed hit row, old formula (`3*calls + 1*aux` for
552/554, `calls + 1*aux` for 559) vs new (`calls + w*aux`):

| scenario | method | base calls | base aux | steps | base err | old cost | new cost |
|---|---|---:|---:|---:|---:|---:|---:|
| Earth LEO 400 km circular | WH-BM14A | 448 | 480 | 32 | 0.016835 | 1 824 | 815.576 |
| Earth LEO 400 km circular | WH-Y6 | 449 | 448 | 64 | 0.042852 | 1 795 | 792.071 |
| Earth LEO 400 km circular | RKN12(10) | 1 429 | 0 | 84 | 0.707629 | 4 287 | 1 429 |
| Earth LEO 400 km circular | DOP853-adaptive | 3 481 | 0 | 290 | 0.580892 | 10 443 | 3 481 |
| Earth LEO 400 km, 30 d | WH-BM14A | 14 336 | 15 360 | 1 024 | 0.082836 | 58 368 | 25 636.3 |
| Earth LEO 400 km, 30 d | WH-Y6 | 14 337 | 14 336 | 2 048 | 0.983516 | 57 347 | 24 884.0 |
| Earth LEO 400 km, 30 d | RKN12(10) | 61 031 | 0 | 3 590 | 0.754475 | 183 093 | 61 031 |
| Earth LEO 400 km, 30 d | DOP853-adaptive | 236 569 | 0 | 19 714 | 0.507951 | 709 707 | 236 569 |
| Moon orbit 100 km x1000 orbits | WH-BM14A | 50 176 | 53 760 | 3 584 | 0.739812 | 103 936 | 91 460.0 |
| Moon orbit 100 km x1000 orbits | WH-Y6 | 84 225 | 84 224 | 12 032 | 0.779243 | 168 449 | 148 903 |
| Moon orbit 100 km x1000 orbits | RKN12(10) | 130 663 | 0 | 7 686 | 0.949719 | 130 663 | 130 663 |
| Moon orbit 100 km x1000 orbits | DOP853-adaptive | 506 905 | 0 | 42 242 | 0.637045 | 506 905 | 506 905 |
| Moon flyby vinf 1000 alt 100 | WH-BM14A | 560 | 600 | 40 | 0.442552 | 1 160 | 2 561.23 |
| Moon flyby vinf 1000 alt 100 | WH-Y6 | 841 | 840 | 120 | 0.373733 | 1 681 | 3 642.72 |
| Moon flyby vinf 1000 alt 100 | RKN12(10) | 460 | 0 | 27 | 0.871302 | 460 | 460 |
| Moon flyby vinf 1000 alt 100 | DOP853-adaptive | 385 | 0 | 32 | 0.594177 | 385 | 385 |
| Earth flyby 3 R vinf 1000 | WH-BM14A | 840 | 900 | 60 | 0.141419 | 1 740 | 4 828.0 |
| Earth flyby 3 R vinf 1000 | WH-Y6 | 981 | 980 | 140 | 0.548565 | 1 961 | 5 323.49 |
| Earth flyby 3 R vinf 1000 | RKN12(10) | 460 | 0 | 27 | 0.965477 | 460 | 460 |
| Earth flyby 3 R vinf 1000 | DOP853-adaptive | 493 | 0 | 41 | 0.525362 | 493 | 493 |
| Heliocentric 1 AU e 0.9 | WH-BM14A | 3 136 | 3 360 | 224 | 0.642102 | 6 496 | 6 801.35 |
| Heliocentric 1 AU e 0.9 | WH-Y6 | 4 929 | 4 928 | 704 | 0.524269 | 9 857 | 10 304.8 |
| Heliocentric 1 AU e 0.9 | RKN12(10) | 1 131 | 0 | 58 | 0.098839 | 1 131 | 1 131 |
| Heliocentric 1 AU e 0.9 | DOP853-adaptive | 2 736 | 0 | 227 | 0.442581 | 2 736 | 2 736 |

Sensitivity of WH at 1 m (same hit row; `0.5w` / `w` / `2w`):

| scenario | method | 0.5w | w | 2w |
|---|---|---:|---:|---:|
| Earth LEO 400 km circular | WH-BM14A | 632 | 816 | 1 183 |
| Earth LEO 400 km circular | WH-Y6 | 621 | 792 | 1 135 |
| Earth LEO 400 km, 30 d | WH-BM14A | 19 986 | 25 636 | 36 937 |
| Earth LEO 400 km, 30 d | WH-Y6 | 19 610 | 24 884 | 35 431 |
| Moon orbit 100 km x1000 orbits | WH-BM14A | 70 818 | 91 460 | 132 744 |
| Moon orbit 100 km x1000 orbits | WH-Y6 | 116 564 | 148 903 | 213 581 |
| Moon flyby vinf 1000 alt 100 | WH-BM14A | 1 561 | 2 561 | 4 562 |
| Moon flyby vinf 1000 alt 100 | WH-Y6 | 2 242 | 3 643 | 6 444 |
| Earth flyby 3 R vinf 1000 | WH-BM14A | 2 834 | 4 828 | 8 816 |
| Earth flyby 3 R vinf 1000 | WH-Y6 | 3 152 | 5 323 | 9 666 |
| Heliocentric 1 AU e 0.9 | WH-BM14A | 4 969 | 6 801 | 10 467 |
| Heliocentric 1 AU e 0.9 | WH-Y6 | 7 617 | 10 305 | 15 681 |

Accounting checks in Test551: RK4 `calls=4*steps`; DOPRI5-fixed `1+6*steps`;
WH-Y6 `1+7*steps, aux=7*steps`; WH-BM14A `calls=14*steps, aux=15*steps`;
Encke `aux=calls`; DOPRI5-adaptive `1+6*(accepted+rejected)`;
DOP853-adaptive `1+11*(accepted+rejected)+accepted`. All PASS.

Regression: Test551 runs after T1-T9 all PASS. The base grid of the six
control scenarios was rerun with `P1B_NO_REFINE=1`: 454+274+1054 rows, 0
missing from baseline, 0 mismatches. Full-run common rows: 0 mismatches
>1e-12, 0 ulp-level differences.

---

## 4. T2 - checkpoints and end-only mode

Code: `StudyRow.Nck/StepsForcedMin/CheckpointBound/CheckpointMode`,
`StudyProblem.CloneEndOnly`, `StudySweepRunner.RunEndOnly`,
`StudyReport.WriteCellsCsv`. `nck = CheckpointTimes.Length`;
`steps_forced_min = StudyStops.Build(p).Times.Length`;
`checkpoint_bound = steps <= 1.5*nck`. Both modes run for every scenario;
neither is primary.

Control scenarios at 1 m (all-checkpoint vs end-only; 4 methods):

| scenario | method | all status | cost_all | err_all | end status | cost_end | err_end |
|---|---|---|---|---:|---|---|---:|
| Earth LEO 400 km circular | WH-BM14A | OK | 815.576 | 0.016835 | OK | 509.735 | 0.717872 |
| Earth LEO 400 km circular | WH-Y6 | OK | 989.839 | 0.009643 | OK | 656.106 | 0.345474 |
| Earth LEO 400 km circular | RKN12(10) | OK | 1 429 | 0.707629 | OK | 1 259 | 0.762697 |
| Earth LEO 400 km circular | DOP853-adaptive | OK | 3 481 | 0.580892 | OK | 3 349 | 0.593467 |
| Earth LEO 400 km, 30 d | WH-BM14A | OK | 25 636.3 | 0.082836 | OK | 24 860.2 | 0.160163 |
| Earth LEO 400 km, 30 d | WH-Y6 | OK | 27 994.3 | 0.448431 | OK | 20 230.6 | 0.498331 |
| Earth LEO 400 km, 30 d | RKN12(10) | OK | 61 031 | 0.754475 | OK | 60 419 | 0.788650 |
| Earth LEO 400 km, 30 d | DOP853-adaptive | OK | 221 209 | 0.962480 | OK | 220 009 | 0.971034 |
| Moon orbit 100 km x1000 orbits | WH-BM14A | OK | 117 591 | 0.172147 | OK | 90 898.6 | 0.763975 |
| Moon orbit 100 km x1000 orbits | WH-Y6 | OK | 190 089 | 0.146537 | OK | 148 792 | 0.757697 |
| Moon orbit 100 km x1000 orbits | RKN12(10) | OK | 130 663 | 0.949719 | OK | 129 184 | 0.967695 |
| Moon orbit 100 km x1000 orbits | DOP853-adaptive | OK | 506 905 | 0.637045 | OK | 505 765 | 0.639074 |
| Moon flyby vinf 1000 alt 100 | WH-BM14A | OK | 2 561.23 | 0.442552 | OK | 2 305.11 | 0.723495 |
| Moon flyby vinf 1000 alt 100 | WH-Y6 | OK | 4 249.67 | 0.125765 | OK | 3 217.85 | 0.992118 |
| Moon flyby vinf 1000 alt 100 | RKN12(10) | OK | 460 | 0.871302 | OK | 188 | 0.918765 |
| Moon flyby vinf 1000 alt 100 | DOP853-adaptive | OK | 385 | 0.594177 | OK | 229 | 0.747236 |
| Earth flyby 3 R vinf 1000 | WH-BM14A | OK | 4 828 | 0.141419 | OK | 3 621 | 0.990181 |
| Earth flyby 3 R vinf 1000 | WH-Y6 | OK | 5 323.49 | 0.548565 | OK | 5 133.4 | 0.756943 |
| Earth flyby 3 R vinf 1000 | RKN12(10) | OK | 460 | 0.473942 | OK | 205 | 0.946692 |
| Earth flyby 3 R vinf 1000 | DOP853-adaptive | OK | 481 | 0.783966 | OK | 349 | 0.726462 |
| Heliocentric 1 AU e 0.9 | WH-BM14A | OK | 6 801.35 | 0.642102 | OK | 6 619.17 | 0.484508 |
| Heliocentric 1 AU e 0.9 | WH-Y6 | OK | 10 304.8 | 0.524269 | OK | 10 158.5 | 0.670431 |
| Heliocentric 1 AU e 0.9 | RKN12(10) | OK | 1 131 | 0.098839 | OK | 939 | 0.081947 |
| Heliocentric 1 AU e 0.9 | DOP853-adaptive | OK | 2 603 | 0.863314 | OK | 2 400 | 0.835781 |

`nck` is 16, 256, 256, 20, 20, 32 for the six rows above; end-only always
has nck=1. In the final full run, `checkpoint_bound` (steps <= 1.5*nck) is
true in 243 of 3 605 all-checkpoint rows in Test552, 76 of 1 029 in Test554,
and 3 974 of 16 949 in Test559. The end-only run has 4 445 / 1 375 / 24 049
rows and its minimum finite step count is 2 / 2 / 1; for every method this is
below the all-checkpoint nck of the same scenario (16-256). For example
WH-BM14A Moon flyby: 40 all-checkpoint steps vs 1 end-only step at the
coarsest point. The end-only minimum is reported per method in the final CSV.

**Literal LEO 1000 check.** "Earth LEO 400 km, 1000 orbits (~64 d)" was run
separately. Adaptive methods agree within 10% (DOP853 380 953 vs 379 105,
RKN12 104 534 vs 104 109, IAS15 261 138 vs 258 885). Fixed-step methods do
not: WH-Y6 56 913 vs 39 363 (30.8%), WH-BM14A 39 115 vs 31 119 (20.4%),
SRKN14A 426 496 vs 318 934 (25.2%), Y6/Y6C 25.0%. This is the P3/T3
quantization, not a T2 implementation bug: the all-checkpoint fixed grid has
step counts that are multiples of nck (WH-Y6 4 608=18*256; the end-only run
uses 3 187 steps), while T3 forbids inventing an intermediate all-checkpoint
step count. The exact control/step/error triples are in the T2 check CSV.

---

## 5. T3 - one price rule

Code: `StudyReport.CostAtTarget` returns the cheapest executed hit; no
interpolation. `StudySweep.RefineTargetBrackets` uses the same hit/miss pair,
adds at most 12 real runs per cell, and stops at `cost_hit/cost_miss <= 1.10`.
For fixed all-checkpoint methods whose step counts are multiples of nck it
sets `bracket_wide=1, bracket_quantized=1` and does not spend the budget.
`cost_hit`, `err_hit`, `cost_miss`, `err_miss`, `bracket_ratio`,
`bracket_wide`, `bracket_quantized` are in `*_cells.csv` for both modes.

WH-BM14A, "Moon flyby vinf 1000 alt 100", 1 m:

| mode | cost_hit | err_hit | cost_miss | err_miss | ratio | wide | quantized |
|---|---:|---:|---:|---:|---:|---|---|
| all checkpoints | 2 561.23 | 0.442552 | 1 280.61 | 3.060839 | 2.000000 | 1 | 1 |
| end only | 2 305.11 | 0.723495 | 2 112.60 | 1.000000 | 1.090909 | 0 | 0 |

No reported `cost` is below the cheapest executed run of the same
scenario/method/variant: checked for all six controls, 0 violations.
At 1 m, six-control counts: Test552 38 cells, 27 OK, 22 wide, 16 quantized;
Test554 19 cells, 16 OK, 11 wide, 9 quantized; Test559 76 cells, 50 OK, 43
wide, 32 quantized. End-only: 26/15/53 OK and 6/3/10 wide, 0 quantized.

---

## 6. T4 - every cap variant is a separate method

Code: `StudyRegimeAnalysis.cs:415-451` groups by `first.Key`, not
`first.Integrator`; `StudyReport.VariantLabel` returns the full key;
`DiagnoseTies` and the winner tie list use full keys. The CSV always had 19
distinct keys (15 base integrators + 4 cap variants); the summary now keeps
all 19. Before T4 the analysis grouped them into 15. The number of
scenarioxtarget cells is unchanged (Test552 17 scenarios, Test553 6,
Test554 4, Test558 17, Test559 66; targets as in each test). Test551 check:
`A (100) ~ A [cap=600 s] (105) [tie within 10%]` - both names retained.

---

## 7. T5 - drift vs reference

Code: `StudySweep.EvaluateError` writes `energy_drift_from_initial`,
`energy_drift_vs_ref`, `energy_drift_ref` and the same three angular columns.
`energy_drift_from_initial` is the old column, renamed and kept. The reference
is evaluated at the same checkpoint; no candidate-reference state is mixed.

LEO 30 d, most accurate run per method:

| method | err_m | E from init | E vs ref | E ref | L from init | L vs ref | L ref |
|---|---:|---:|---:|---:|---:|---:|---:|
| DOP853-adaptive | 0.015235 | 5.76529 | 3.12589e-05 | 5.76530 | 5087.78 | 0.027588 | 5087.79 |
| RKN12(10) | 0.015242 | 5.76531 | 2.93888e-05 | 5.76530 | 5087.80 | 0.025925 | 5087.79 |
| IAS15 | 0.001524 | 5.76530 | 2.62633e-06 | 5.76530 | 5087.79 | 0.002312 | 5087.79 |
| WH-BM14A | 0.010615 | 5.76535 | 5.62370e-05 | 5.76530 | 5087.84 | 0.049622 | 5087.79 |
| WH-Y6 | 0.001645 | 5.76524 | 7.47107e-05 | 5.76530 | 5087.74 | 0.065941 | 5087.79 |

The old 5.765 is the scenario's physical drift (`E ref`), not the
integrator's; the vs-ref values are 4-5 orders smaller. Test551 pure Kepler:
`E_ref_drift=1.67e-16`, relative `3.33e-16` (<=1e-13); method-vs-reference
`7.38e-13`, from-initial `7.38e-13`. Test558 also writes the six-column
invariants CSV.

---

## 8. T6 - reference metadata and second reference

Code: `StudyProblem.ReferenceMethod/ReferenceFamily/ReferenceIndependent/
ReferenceResolution/ReferenceCrossDiffM/ReferenceSecondMethod`;
`StudyBridgeAttachSecondReference` runs IAS15 rtol 1e-13 with a 20M RHS /
300 s budget and raises `floor = max(old floor, max|ref_A-ref_B|)` without
replacing the grading reference. Test553 uses two dd-Taylor runs and sets
`ReferenceFamily=Taylor`. Test551 analytic check: DOP853 2.69e-12, IAS15
9.02e-15, cross 2.69e-12, floor 2.69e-12 - both <= floor.

Final-run reference resolutions (scenarios):
- Test552: 17 `ratio_ok`, 0 `ref_independent=0`.
- Test553: 6 `ratio_ok`, 0 `ref_independent=0`.
- Test554: 4 `ratio_ok`.
- Test558: 17 `ratio_ok`, 0 `ref_independent=0`.
- Test559: 48 `ratio_ok`, 18 `unratioed_exemption_used`, 0 `not_certified`,
  0 `ref_independent=0`.

Before the final run, four metadata/second-reference gaps were fixed:
`StudyBridgeHeliocentricProblem` now writes the dd-Taylor metadata;
`NormalizedSunProblem` writes analytic metadata; `NormalizedFromCase` copies
and re-checks the reference; the far-flight physical and normalized problems
now get the IAS15 second reference. Before the fix, far-flight floor was
0.007995 m; after the second reference it is 0.01163 m (physical) and
0.01172 m (normalized).

Floor increases that cross `target < 10*floor` (targets become SKIP):
- Test554: "Earth LEO 400 km, 1000 orbits (~64 d)", target 0.1 and 0.01 m;
  floor 1.824e-4 -> 1.746e-2 m.
- Test559: "Far flight 30 AU e 0.5 (physical)" and its normalized copy,
  target 0.1 m; floor 7.995e-3 -> 1.163e-2 / 1.172e-2 m.
- Test559: "Heliocentric 0.3 AU e 0.99", target 1 and 0.1 m;
  floor 6.771e-3 -> 0.449965 m.
- Test559: "Heliocentric 1 AU e 0.5", target 0.01 m;
  floor 2.658e-4 -> 1.253e-3 m.
- Test559: "Moon orbit 100 km x1000 orbits", target 0.1 m;
  floor 5.860e-3 -> 1.178e-2 m.
Eight scenario/target pairs crossed; all method cells for those pairs become
SKIP. The T6 check on the six controls showed 19 changed method cells for
Moon orbit 1000 (0.1 m). Test552 and Test558 floors rose but no target
crossed 10*floor.

---

## 9. T7 - ephemeris and seams (measurement only)

`StudySeamProbe` prints, per body, baked/analytic and segment length, then runs
DOP853, RKN12(10), WH-BM14A with and without seam stops at the controls below.
No default sweep is changed.

| scenario | bodies and ephemeris | seams in window | method | -seams cost/err | +seams cost/err |
|---|---|---|---:|---:|---:|
| Moon orbit 100 km x1000 | Sun analytic; Earth/Moon baked, seg 582 203 s, 65 segments; Jupiter/Saturn/Venus/Mars baked, seg 4.73e6-5.41e6 s | 14 | DOP853 | 439 321 / 2.332 m | 439 405 / 2.331 m |
| | | | RKN12 | 33 243 / 3.698e6 m | 33 692 / 3.706e6 m |
| | | | WH-BM14A | 117 591 / 0.1721 m | 117 821 / 0.5678 m |
| Moon flyby 1000/100 | same baked system | 0 | DOP853 | 661 / 3.057e-4 m | 661 / 3.057e-4 m |
| | | | RKN12 | 460 / 0.8713 m | 460 / 0.8713 m |
| | | | WH-BM14A | 2 561 / 0.4426 m | 2 561 / 0.4426 m |
| Earth LEO 400 km, 30 d | same baked system | 4 | DOP853 | 205 849 / 1.836 m | 205 885 / 1.836 m |
| | | | RKN12 | 15 085 / 1.362e7 m | 14 806 / 1.363e7 m |
| | | | WH-BM14A | 25 636 / 0.08284 m | 25 736 / 0.1504 m |

Test553 is analytic Kepler rails (`StudyRealSolar7`, no bake); Test559's
small-body flybys are analytic rails; Test552/554/558/559 use the baked
Chebyshev system. The reference and the candidates both step through seams in
Test559; only the Moon-centered SOI case in 552/554 has `SeamAwareReference`
(and its window has 0 seams).

---

## 10. T8 - duplicate sweep pass

`RunPoint` now returns an existing row for the same
scenario/method/variant/control within 1e-9 relative. Duplicate controls in
the full CSVs: baseline 210 (552), 47 (553), 110 (554), 279 (558), 1872
(559); final run 0 in all five. Duplicate work removed (sum of `calls` on
duplicate rows): 2 183 710 (552), 5 553 932 (553), 20 406 228 (554), 814 636
(558), 79 908 112 (559). The full-suite wall time is not lower than baseline
because T2 adds the end-only mode, T6 adds second references, and T3 adds real
refinement runs; the duplicate-removal check is the row/call evidence above.
On the six controls, the remaining rows at the same control are identical to
baseline: 0 mismatches.

---

## 11. T9 - headers, GJ8 start-up, IAS15 non-monotonicity

Every final log has: date, commit, dirty, diff hash, runtime `.NET 8.0.24
(x64); not Unity / not IL2CPP`, cost-unit warning, and per-scenario `nck`,
checkpoint mode, `w`/CV/source, reference metadata. All nine final logs share
one diff hash (see section 13).

GJ8 `calls_startup` is a separate column; `calls` still includes start-up.
Example (final CSV): Moon orbit 100 km x1000, one row calls=100 699,
steps=50 176, calls_startup=354. The amount is not removed from cost; the
column is diagnostic only.

IAS15 maps `control` (rtol) to `core.Epsilon = max(rtol,1e-16)`
(`StudyIas15.cs:591`). In the final full CSV, 129 of 220 IAS15
scenario/variant series have at least one error increase while rtol tightens;
19 of 220 have more than one pass/fail flip at 1 m. The 19 series are:
Test552 "Earth 400 km + J2", "Earth LEO 400 km circular", "Earth LEO 400 km
polar", "Earth LEO 400 km sun-synchronous"; Test558 "J2 x0 inc 0/45/90/98",
"J2 x0.1 inc 0/45/90/98", "J2 x1 inc 0/45/98", "J2 x10 inc 90/98"; Test559
"Heliocentric 0.3 AU e 0" and "Heliocentric 0.3 AU e 0.9". The pass/fail
pattern strings above are ordered loose-to-tight rtol; the exact controls and
errors for every series are the `integrator=IAS15` rows of
`results/Test552_FrameworkShipSweep.csv`,
`results/Test554_FrameworkOrbitEdgeCases.csv`,
`results/Test558_J2AndSun.csv` and `results/Test559_PureGravity.csv`.
No fix: T9 requests a statement.

---

## 12. Global regression and 10 random cells

Full-run 2 vs `results_baseline`, key `(scenario, integrator, variant, control)`,
columns `calls`, `aux_ops`, `err_m`, `err_final_m`:

| test | baseline rows | final rows | common | only baseline | only final | STOP (>1e-12) | ulp (<=1e-12) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Test552 | 4 084 | 3 605 | 3 383 | 701 | 222 | 0 | 0 |
| Test553 | 1 946 | 2 026 | 1 892 | 54 | 134 | 0 | 0 |
| Test554 | 1 358 | 1 029 | 977 | 381 | 52 | 0 | 0 |
| Test558 | 5 903 | 4 915 | 4 360 | 1 543 | 555 | 0 | 0 |
| Test559 | 22 019 | 16 949 | 16 291 | 5 728 | 658 | 0 | 0 |

The `only baseline` controls are refinement points that the new T3 rule did
not re-run; the base grid is not affected. This was verified separately with
`P1B_NO_REFINE=1` on the six controls: 454 (Test552) + 274 (Test554) + 1 054
(Test559) rows, 0 missing from baseline, 0 mismatches. The `only final`
controls are new refinement points. `calls`/`aux` are exact; `err` is exact
(0 ulp-level differences). The two tests with more final rows than baseline
(Test553, and end-only files not counted here) are the T3 refinement budget
and the second-reference runs, not changed physics.

10 random cells (`seed=20261109`, two per test) and their reruns are in
`TestResults/p1b_checks/final_cells/sample.csv`; the rerun comparison is in
`TestResults/p1b_checks/final_cells/verify.txt`. All ten are byte-exact in
`calls`, `aux_ops` and `err_m`:

| # | test | scenario | method | mode | target | control | calls | aux | err |
|---|---|---|---:|---|---:|---:|---:|---:|---:|
| 1 | 552 | Earth LEO 400 km sun-synchronous | Encke-IAS15 | all | 1 | 11089.710191961583 | 26 571 | 26 571 | 2.895602849e-06 |
| 2 | 552 | Earth GTO 400 x 40000 km | IAS15 | all | 1 | 1.0e-06 | 5 249 | 0 | 0.090056661 |
| 3 | 553 | Heliocentric Sun-Jupiter probe, 10 y | IAS15 | end | 100 | 1.0e-07 | 5 846 | 0 | 32.667796 |
| 4 | 553 | Sun + eccentric comet + Jupiter, 10 y | Y6C | end | 10 | 4580.070244695365 | 482 315 | 0 | 9.637891 |
| 5 | 554 | Earth-Moon flyby inside lunar SOI (Moon-centered) | Y6C | all | 0.01 | 987.654320987654 | 1 261 | 0 | 0.003472620 |
| 6 | 554 | Earth LEO 400 km, 1000 orbits (~64 d) | Encke-IAS15 | end | 10 | 177435.36307138533 | 1 146 500 | 1 146 500 | 0.015393973 |
| 7 | 558 | Sun 1 AU normalized (dimensionless) | RK4 | end | 10 | 0.0045300542950105 | 5 548 | 0 | 9.309076 |
| 8 | 558 | J2 x1 inc 0 | DOPRI5-adaptive cap=600 s | all | 100 | 4.216965034285818e-09 | 6 829 | 0 | 93.291123 |
| 9 | 559 | Moon flyby vinf 3000 alt 100 | GJ8 | all | 0.01 | 18.06356548799366 | 6 986 | 0 | 0.000528878 |
| 10 | 559 | Moon flyby vinf 200 alt 10 | DOP853-adaptive cap=600 s | end | 0.001 | 3.162277660168376e-12 | 2 065 | 0 | 0.000360652 |

The cell-4 rerun exits 1 because Test553's structural check "an adaptive
method reaches a target" has no adaptive method when only Y6C is filtered;
the numeric row itself is exact. This is a test-harness filter check, not a
data mismatch.

---

## 13. Final full run

Order: Test551, Test552, Test553, Test554, Test555 (archived), Test557
(archived), Test558, Test559, Test556 (replay last, after fresh CSVs).
No source or report file was edited between tests.

| test | wall time |
|---|---:|
| Test551 | 0.66 s |
| Test552 | 530.65 s |
| Test553 | 874.96 s |
| Test554 | 584.03 s |
| Test555 | 36.70 s |
| Test557 | 1.74 s |
| Test558 | 505.62 s |
| Test559 | 2 423.55 s |
| Test556 | 0.31 s |
| total | 82 min 39 s (14:21:34-15:44:13 UTC) |

All nine logs end with `ALL PASS`. The diff hash is one value in all nine:
`9ea6c17da1dad7100ccd97ac435af616b3bd3b26d82dee6096fde0bccdcf2aad`.
Commit recorded: `8230943ccdb8aad8ec1525ba1009a52a334da695`; worktree `dirty`
(the audit files and `results_baseline/` are untracked). Runtime: .NET 8.0.24,
x64, not Unity / not IL2CPP. Logs: `results/Test*_final.log` and
`results/FINAL_FULL_RUN.log`.

After the run, `StudyOptions.RhsCostMultiplier` was kept as an ignored
compatibility field and the two pricing lines in archived Test555 were restored,
so Test555 compiles without a source change. The multiplier is not used in any
cost (T1 uses `calls + w*aux`); Test555 is archived and is not part of this
audit. The reported Test551-554 and Test556-559 numbers are unaffected. The
audit Markdown files were also finalized after the run; they are documents, not
measured code. The one-hash statement above refers to the nine run-time logs,
all of which carry the same value.

---

## 14. Not checked / open

- The external audit's exact P3 aggregate "112 of 301" and P10 "15 of 108"
  were not reproduced because their cell definitions were not stated; this
  report uses its own explicit definitions and lists the numbers.
- The far-flight physical/normalized T6 second reference changes floors and
  statuses; the final run reflects it.
- All other "not checked" items are listed in `FOUND_NOT_FIXED.md`.
