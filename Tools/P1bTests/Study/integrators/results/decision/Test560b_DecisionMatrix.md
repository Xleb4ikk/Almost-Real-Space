# Test560b - integrator decision matrix (all StudyRegistry methods)

Generated from `Test560b_DecisionMatrix.csv` on 2026-11-10 16:24:00 UTC.
Every numeric cell is an executed run; all runs, including losers and failures,
are in `Test560b_Runs.csv`. Cost is ranked by `cost_total` = `cost_rhs` +
`cost_local_frame` + `cost_aux`; comparisons that include stand-only methods
use the disclosed port estimate `cost_if_ported`. No interpolation.

**Scope:** every scenario integrates the translational centre of mass
(position, velocity, mass). `AttitudePhysics`, `VesselFlightModel`, gimbal/RCS,
the contact/landing model and rigid-body rotation are not exercised here. The
`touchdown` event is a sphere-crossing time, not a contact model.

## Audit of the existing stand

- Test551-Test559 cover the framework, ship/planet sweeps, edge cases, J2/Sun and
  pure-gravity flybys. The audited results in `results/` are restored from git and
  are not read or overwritten by Test560b; `results_baseline/` and the old numbers
  in `ORBIT_INTEGRATOR_TEST_SCHEME.md` are not used.
- Test560b covers the full `StudyRegistry.All()` list plus the existing 600 s and
  86 400 s cap variants of DOPRI5/DOP853/RKN12/IAS15. Production rows are only
  DOP853/DOPRI5; every other row is `stand-only` and marked as requiring a port.

## Method and cost model

- Chunks: surface ascent/descent/hover/level 0.05 s flight (0.5 s only as
  sensitivity for ascent/descent); LEO 600 s coast and 0.05 s burn; high
  orbit/cislunar 600 s; heliocentric 600 s production and 86 400 s
  regime-sensitivity; far flight 86 400 s outer chunk with production max step 600 s
  and a separate 86 400 s regime configuration.
- `cost_rhs` = all RHS/force calls, including rejected steps and restarts.
- `cost_local_frame` = production-only extra two gravity sums per local RHS
  (the local derivative has three sums; one is already in `cost_rhs`). Stand methods
  report 0 here because their RHS is already the local force.
- `cost_if_ported` = `cost_total` for production; for a stand method it is
  `cost_total + 2*(raw_rhs - chunks)`, the measured production convention for the
  local-frame wrapper. It is an estimate, not a re-run, and is used whenever a
  stand method is compared with production (`requires port`).
- `cost_aux` = measured Kepler weight * auxiliary solves (WH/Y6/Encke); WH's
  measured CV is in `Test560b_kepler_weights.csv`.
- `cost_total` is the raw ranking metric inside a method class. `best_all_valid`
  and `best_ported` rank by `cost_if_ported`; `best_stand_raw` is shown only as
  a raw stand number and is explicitly not comparable. `ms` is diagnostic.
- Max step: production config uses the game's 600 s cap; far/heliocentric have a
  separate `regime86400` configuration. Production and stand methods are compared
  only within the same config/max step. `scope` says which axis a row belongs to
  (`config`, `max_step`, `chunk`); `bound` is the winner's heuristic label and
  `production_bound` is the selected production row's label: `chunk` when
  steps <= 1.2*chunks, `max-step` when maxStep>0 and
  0.8*(duration/maxStep) <= steps <= 1.2*(duration/maxStep), otherwise `tolerance`.
  It is not a proof of the controller state.
- Status: `ok` = error <= 80% target, `edge` = 80-100%, `no_hit` = >100%,
  `n/a` = method cannot support the force, `unresolved` = no finite run,
  `not run` = deliberate exclusion with a reason. An edge winner gets a margin
  recommendation; ties are <=20% by the comparable cost.
- Edge policy: `selected_best_all_total` is the chosen ok run that defines
  `best_all`; `selected_production_total` is the production base. `cheapest_edge_total`
  is compared with both in `edge_policy`; a cheaper edge is never promoted to ok.

## Scenarios

| scenario | regime | config | chunk | targets | floor (m) | reference |
|---|---|---|---:|---|---:|---|
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | surface | production | 0.05 s flight (production) | 0.1 m, 0.1 m/s | 2.75E-007 | resolved_finest_delta |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | surface | chunk-sweep | fixed chunk 0.5 s | 0.1 m, 0.1 m/s | 2.75E-007 | resolved_finest_delta |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | surface | production | 0.05 s flight (production) | 0.1 m, 0.1 m/s | 1.20E-007 | ratio_ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | surface | chunk-sweep | fixed chunk 0.5 s | 0.1 m, 0.1 m/s | 1.20E-007 | ratio_ok |
| "Surface hover 0.5 km, 0 m/s relative" | surface | production | 0.05 s flight (production) | 0.1 m, 0.1 m/s | 2.26E-008 | resolved_finest_delta |
| "Surface level flight 0.5 km, 100 m/s horizontal" | surface | production | 0.05 s flight (production) | 0.1 m, 0.1 m/s | 2.82E-007 | resolved_finest_delta |
| "Surface level flight 1 km, 100 m/s horizontal" | surface | production | 0.05 s flight (production) | 0.1 m, 0.1 m/s | 3.28E-007 | resolved_finest_delta |
| "LEO 200 km, 1 d" | leo | production | 600 s coast / 0.05 s burn (production) | 10 m | 1.98E-005 | resolved_finest_delta |
| "LEO 300 km, 30 d" | leo | production | 600 s coast / 0.05 s burn (production) | 100 m | 1.54E-003 | ratio_ok |
| "High orbit 35 786 km (GEO), 30 d" | high-orbit | production | 600 s (production) | 100 m | 9.64E-006 | resolved_finest_delta |
| "High orbit 200 000 km, 30 d" | high-orbit | production | 600 s (production) | 100 m | 8.81E-007 | resolved_finest_delta |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | cislunar | production | 600 s (production) | 100 m | 1.33E-003 | resolved_finest_delta |
| "Heliocentric 0.3 AU e 0.5, 1 y" | heliocentric | production | 600 s (production) | 10 km | 7.31E-004 | resolved_finest_delta |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | heliocentric | regime86400 | 86 400 s (regime sensitivity, not production) | 10 km | 7.31E-004 | resolved_finest_delta |
| "Heliocentric 5 AU e 0.3, 10 y" | heliocentric | auto-bug | 600 s (production) | 100 km | 1.27E-002 | resolved_finest_delta |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | heliocentric | production | 600 s (forced Sun; production auto path is buggy) | 100 km | 1.27E-002 | resolved_finest_delta |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | heliocentric | regime86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 1.27E-002 | resolved_finest_delta |
| "Far flight 30 AU e 0.5, 10 y" | far-flight | production | 86 400 s outer chunk / 600 s production max step | 100 km | 9.87E-003 | resolved_finest_delta |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | far-flight | regime86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 9.87E-003 | resolved_finest_delta |

## Method coverage

Coverage rows: ranked=241, n/a=99, not run=135. Full method x regime/config coverage and reasons: `Test560b_MethodCoverage.csv`.

## Per-regime cells (production and regime configs shown separately)

### Cislunar Moon flyby, SOI entry-exit, 10 d / 100 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 6.969E+000 | 18720 | 34560 | 0 | 53280 | 53280 |  | 14.3 | ok |
| DOPRI5 [production] | production | 600 | 2.254E+001 | 12096 | 21312 | 0 | 33408 | 33408 |  | 8.6 | ok |
| RK4 | stand-only | 600 | 8.000E+001 | 40320 | 0 | 0 | 40320 | 118080 |  | 15.4 | ok |
| DOPRI5-fixed | stand-only | 600 | 3.090E+001 | 18720 | 0 | 0 | 18720 | 53280 |  | 9.0 | ok |
| DOPRI5-adaptive | stand-only | 600 | 1.177E+001 | 10992 | 0 | 0 | 10992 | 30096 |  | 5.6 | ok |
| DOP853-adaptive | stand-only | 600 | 6.993E+000 | 18720 | 0 | 0 | 18720 | 53280 |  | 9.9 | ok |
| PEFRL | stand-only | 600 | 6.749E+001 | 28800 | 0 | 0 | 28800 | 83520 |  | 15.0 | ok |
| Y6 | stand-only | 600 | 1.508E+001 | 41760 | 0 | 0 | 41760 | 122400 | 92160 | 21.2 | ok |
| Y6C | stand-only | 600 | 1.508E+001 | 41760 | 0 | 0 | 41760 | 122400 | 92160 | 21.6 | ok |
| WH-Y6 | stand-only | 600 | 7.743E+001 | 21600 | 0 | 52124.242830177194 | 73724.24283017719 | 114044.24283017719 |  | 19.9 | ok |
| Leapfrog | stand-only | 600 | 4.362E+001 | 738720 | 0 | 0 | 738720 | 2213280 | 1569600 | 373.3 | ok |
| RKN12(10) | stand-only | 600 | 1.539E-003 | 25920 | 0 | 0 | 25920 | 74880 |  | 13.0 | ok |
| IAS15 | stand-only | 600 | 2.974E-003 | 35264 | 0 | 0 | 35264 | 102912 |  | 24.1 | ok |
| SRKN14A | stand-only | 600 | 8.709E-001 | 20160 | 0 | 0 | 20160 | 57600 |  | 10.7 | ok |
| WH-BM14A | stand-only | 600 | 9.659E-002 | 20160 | 0 | 55847.40303233271 | 76007.40303233272 | 113447.40303233272 |  | 21.3 | ok |
| GJ8 | stand-only | 600 | 2.167E-003 | 225425 | 0 | 0 | 225425 | 673395 |  | 142.1 | ok |
| Encke-IAS15 | stand-only | 600 | 1.733E-003 | 162350 | 0 | 419760.45751385257 | 582110.4575138525 | 903930.4575138525 |  | 194.6 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Far flight 30 AU e 0.5, 10 y / 100 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 2.513E-001 | 6310864 | 12614400 | 0 | 18925264 | 18925264 |  | 9277.1 | ok |
| DOPRI5 [production] | production | 600 | 3.835E-001 | 3160928 | 6314528 | 0 | 9475456 | 9475456 |  | 4348.1 | ok |
| RK4 | stand-only | 600 | 2.503E-001 | 2102400 | 0 | 0 | 2102400 | 6299872 |  | 1660.1 | ok |
| DOPRI5-fixed | stand-only | 600 | 2.508E-001 | 3157264 | 0 | 0 | 3157264 | 9464464 |  | 3845.7 | ok |
| DOPRI5-adaptive | stand-only | 600 | 2.508E-001 | 3157264 | 0 | 0 | 3157264 | 9464464 |  | 3878.5 | ok |
| DOP853-adaptive | stand-only | 600 | 2.508E-001 | 6310864 | 0 | 0 | 6310864 | 18925264 |  | 8346.3 | ok |
| PEFRL | stand-only | 600 | 2.376E-001 | 2102400 | 0 | 0 | 2102400 | 6299872 |  | 2926.6 | ok |
| Y6 | stand-only | 600 | 1.397E-001 | 3682864 | 0 | 0 | 3682864 | 11041264 |  | 5121.2 | ok |
| Y6C | stand-only | 600 | 9.174E-003 | 3682864 | 0 | 0 | 3682864 | 11041264 |  | 5133.8 | ok |
| WH-Y6 | stand-only | 600 | 3.196E+000 | 3682864 | 0 | 4111809.0696840514 | 7794673.069684051 | 15153073.069684051 |  | 5677.4 | ok |
| Leapfrog | stand-only | 600 | 1.337E+000 | 529264 | 0 | 0 | 529264 | 1580464 |  | 732.8 | ok |
| RKN12(10) | stand-only | 600 | 5.676E-001 | 8938864 | 0 | 0 | 8938864 | 26809264 |  | 11303.7 | ok |
| IAS15 | stand-only | 600 | 9.392E-003 | 7884007 | 0 | 0 | 7884007 | 23644693 |  | 12122.4 | ok |
| SRKN14A | stand-only | 600 | 5.361E-001 | 7358400 | 0 | 0 | 7358400 | 22067872 |  | 10194.8 | ok |
| WH-BM14A | stand-only | 600 | 5.657E+001 | 7358400 | 0 | 8811019.435037253 | 16169419.435037253 | 30878891.435037255 |  | 11450.6 | ok |
| GJ8 | stand-only | 600 | 6.694E-002 | 1468896 | 0 | 0 | 1468896 | 4399360 |  | 1575.1 | ok |
| Encke-IAS15 | stand-only | 600 | 6.759E-001 | 48355704 | 0 | 54041482.46307821 | 102397186.4630782 | 199101266.4630782 |  | 80700.4 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

Max-step variants (informational, not mixed into best_all):

| method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---:|---:|---:|---:|---:|---|---|
| DOPRI5-adaptive [cap=86400 s] | 86400 | 600 | 9.174E-003 | 25648 | 69616 | chunk | ok |
| DOP853-adaptive [cap=86400 s] | 86400 | 600 | 9.203E-003 | 47632 | 135568 | chunk | ok |
| RKN12(10) [cap=86400 s] | 86400 | 600 | 5.720E-003 | 65952 | 190528 | chunk | ok |
| IAS15 [cap=86400 s] | 86400 | 600 | 9.392E-003 | 80496 | 234160 | chunk | ok |

### Far flight 30 AU e 0.5, 10 y [regime86400] / 100 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 86400 | 5.619E-002 | 47632 | 87936 | 0 | 135568 | 135568 |  | 66.1 | ok |
| DOPRI5 [production] | production | 86400 | 8.964E-003 | 29312 | 51296 | 0 | 80608 | 80608 |  | 32.6 | ok |
| RK4 | stand-only | 86400 | 9.265E-003 | 14656 | 0 | 0 | 14656 | 36640 |  | 13.3 | ok |
| DOPRI5-fixed | stand-only | 86400 | 9.174E-003 | 25648 | 0 | 0 | 25648 | 69616 |  | 29.1 | ok |
| DOPRI5-adaptive | stand-only | 86400 | 9.174E-003 | 25648 | 0 | 0 | 25648 | 69616 |  | 29.3 | ok |
| DOP853-adaptive | stand-only | 86400 | 9.203E-003 | 47632 | 0 | 0 | 47632 | 135568 |  | 60.9 | ok |
| PEFRL | stand-only | 86400 | 1.739E-002 | 14656 | 0 | 0 | 14656 | 36640 |  | 21.9 | ok |
| Y6 | stand-only | 86400 | 4.787E-002 | 29312 | 0 | 0 | 29312 | 80608 |  | 37.9 | ok |
| Y6C | stand-only | 86400 | 1.005E-002 | 29312 | 0 | 0 | 29312 | 80608 |  | 38.1 | ok |
| WH-Y6 | stand-only | 86400 | 3.514E-002 | 29312 | 0 | 28663.752723216065 | 57975.752723216065 | 109271.75272321606 |  | 41.5 | ok |
| Leapfrog | stand-only | 86400 | 2.828E+004 | 7328 | 0 | 0 | 7328 | 14656 |  | 7.5 | ok |
| RKN12(10) | stand-only | 86400 | 5.720E-003 | 65952 | 0 | 0 | 65952 | 190528 |  | 81.7 | ok |
| IAS15 | stand-only | 86400 | 9.392E-003 | 80496 | 0 | 0 | 80496 | 234160 |  | 125.4 | ok |
| SRKN14A | stand-only | 86400 | 2.073E-002 | 51296 | 0 | 0 | 51296 | 146560 |  | 72.7 | ok |
| WH-BM14A | stand-only | 86400 | 4.102E-002 | 51296 | 0 | 61422.32726403442 | 112718.32726403442 | 207982.32726403442 |  | 80.8 | ok |
| GJ8 | stand-only | 86400 | 9.392E-003 | 91488 | 0 | 0 | 91488 | 267136 |  | 129.4 | ok |
| Encke-IAS15 | stand-only | 86400 | 1.480E-002 | 366418 | 0 | 409502.2982429579 | 775920.2982429579 | 1501428.2982429578 |  | 613.4 | ok |
| DOPRI5-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |

Max-step variants (informational, not mixed into best_all):

| method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---:|---:|---:|---:|---:|---|---|
| DOPRI5-adaptive [cap=600 s] | 600 | 86400 | 2.508E-001 | 3157264 | 9464464 | max-step | ok |
| DOP853-adaptive [cap=600 s] | 600 | 86400 | 2.508E-001 | 6310864 | 18925264 | max-step | ok |
| IAS15 [cap=600 s] | 600 | 86400 | 9.392E-003 | 7884007 | 23644693 | max-step | ok |
| RKN12(10) [cap=600 s] | 600 | 86400 | 5.676E-001 | 8938864 | 26809264 | max-step | ok |

### Heliocentric 0.3 AU e 0.5, 1 y / 10 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 1.913E-001 | 683280 | 1261440 | 0 | 1944720 | 1944720 |  | 955.6 | ok |
| DOPRI5 [production] | production | 600 | 1.976E-002 | 420480 | 735840 | 0 | 1156320 | 1156320 |  | 476.0 | ok |
| RK4 | stand-only | 600 | 1.221E+000 | 210240 | 0 | 0 | 210240 | 525600 |  | 186.9 | ok |
| DOPRI5-fixed | stand-only | 600 | 2.309E-002 | 367920 | 0 | 0 | 367920 | 998640 |  | 423.3 | ok |
| DOPRI5-adaptive | stand-only | 600 | 2.309E-002 | 367920 | 0 | 0 | 367920 | 998640 |  | 424.8 | ok |
| DOP853-adaptive | stand-only | 600 | 3.305E-002 | 683280 | 0 | 0 | 683280 | 1944720 |  | 890.4 | ok |
| PEFRL | stand-only | 600 | 6.000E-001 | 210240 | 0 | 0 | 210240 | 525600 |  | 317.6 | ok |
| Y6 | stand-only | 600 | 1.783E-001 | 420480 | 0 | 0 | 420480 | 1156320 |  | 552.8 | ok |
| Y6C | stand-only | 600 | 7.948E-002 | 420480 | 0 | 0 | 420480 | 1156320 |  | 557.2 | ok |
| WH-Y6 | stand-only | 600 | 1.748E-002 | 420480 | 0 | 413020.05374812306 | 833500.0537481231 | 1569340.053748123 |  | 608.9 | ok |
| Leapfrog | stand-only | 600 | 6.617E+003 | 1524240 | 0 | 0 | 1524240 | 4467600 | 3679200 | 2100.4 | ok |
| RKN12(10) | stand-only | 600 | 3.410E-002 | 946080 | 0 | 0 | 946080 | 2733120 |  | 1246.8 | ok |
| IAS15 | stand-only | 600 | 3.407E-002 | 1156817 | 0 | 0 | 1156817 | 3365331 |  | 1923.8 | ok |
| SRKN14A | stand-only | 600 | 2.391E-001 | 735840 | 0 | 0 | 735840 | 2102400 |  | 1101.5 | ok |
| WH-BM14A | stand-only | 600 | 6.649E-001 | 735840 | 0 | 885042.9723174066 | 1620882.9723174064 | 2987442.9723174064 |  | 1234.3 | ok |
| GJ8 | stand-only | 600 | 3.407E-002 | 1314497 | 0 | 0 | 1314497 | 3838371 |  | 1912.4 | ok |
| Encke-IAS15 | stand-only | 600 | 1.417E-001 | 6525362 | 0 | 7325248.325630463 | 13850610.325630464 | 26796214.325630464 |  | 11607.2 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Heliocentric 0.3 AU e 0.5, 1 y [regime86400] / 10 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 86400 | 7.098E+003 | 5773 | 10810 | 0 | 16583 | 16583 |  | 7.8 | ok |
| DOPRI5 [production] | production | 86400 | 2.908E+003 | 11790 | 22844 | 0 | 34634 | 34634 | 30350 | 15.1 | ok |
| RK4 | stand-only | 86400 | 4.525E+003 | 29184 | 0 | 0 | 29184 | 86816 |  | 22.1 | ok |
| DOPRI5-fixed | stand-only | 86400 | 4.977E+003 | 15728 | 0 | 0 | 15728 | 46448 |  | 18.0 | ok |
| DOPRI5-adaptive | stand-only | 86400 | 7.500E+003 | 9866 | 0 | 0 | 9866 | 28862 |  | 11.4 | ok |
| DOP853-adaptive | stand-only | 86400 | 2.055E+002 | 6210 | 0 | 0 | 6210 | 17894 |  | 7.9 | ok |
| PEFRL | stand-only | 86400 | 5.204E+003 | 23360 | 0 | 0 | 23360 | 69344 | 60512 | 31.4 | ok |
| Y6 | stand-only | 86400 | 2.656E+003 | 15712 | 0 | 0 | 15712 | 46400 | 38672 | 20.9 | ok |
| Y6C | stand-only | 86400 | 2.656E+003 | 15712 | 0 | 0 | 15712 | 46400 | 38672 | 20.9 | ok |
| WH-Y6 | stand-only | 86400 | 1.325E-001 | 2944 | 0 | 2891.7690216763563 | 5835.769021676357 | 10987.769021676357 |  | 4.2 | ok |
| Leapfrog | stand-only | 86400 | 1.026E+005 | 374128 | 0 | 0 | 374128 | 1121648 |  | 0.0 | no_hit |
| RKN12(10) | stand-only | 86400 | 3.749E-003 | 6624 | 0 | 0 | 6624 | 19136 |  | 7.9 | ok |
| IAS15 | stand-only | 86400 | 5.840E-003 | 14382 | 0 | 0 | 14382 | 42410 |  | 21.4 | ok |
| SRKN14A | stand-only | 86400 | 4.384E+003 | 5152 | 0 | 0 | 5152 | 14720 |  | 7.0 | ok |
| WH-BM14A | stand-only | 86400 | 2.086E-002 | 5152 | 0 | 6196.647903592192 | 11348.647903592191 | 20916.64790359219 |  | 8.0 | ok |
| GJ8 | stand-only | 86400 | 1.084E-003 | 51820 | 0 | 0 | 51820 | 154724 |  | 74.3 | ok |
| Encke-IAS15 | stand-only | 86400 | 7.323E-003 | 60427 | 0 | 67834.21066492127 | 128261.21066492127 | 248379.21066492127 |  | 101.3 | ok |
| DOPRI5-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |

Max-step variants (informational, not mixed into best_all):

| method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---:|---:|---:|---:|---:|---|---|
| DOPRI5-adaptive [cap=600 s] | 600 | 86400 | 2.309E-002 | 315728 | 946448 | max-step | ok |
| DOP853-adaptive [cap=600 s] | 600 | 86400 | 3.305E-002 | 631088 | 1892528 | max-step | ok |
| RKN12(10) [cap=600 s] | 600 | 86400 | 3.410E-002 | 893888 | 2680928 | max-step | ok |
| IAS15 [cap=600 s] | 600 | 86400 | 1.153E-002 | 954517 | 2862815 | max-step | ok |

### Heliocentric 5 AU e 0.3, 10 y [forced Sun] / 100 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 1.281E+000 | 6832800 | 12614400 | 0 | 19447200 | 19447200 |  | 9594.3 | ok |
| DOPRI5 [production] | production | 600 | 1.179E+000 | 4204800 | 7358400 | 0 | 11563200 | 11563200 |  | 4771.3 | ok |
| RK4 | stand-only | 600 | 4.197E-001 | 2102400 | 0 | 0 | 2102400 | 5256000 |  | 1867.0 | ok |
| DOPRI5-fixed | stand-only | 600 | 4.238E-001 | 3679200 | 0 | 0 | 3679200 | 9986400 |  | 4416.0 | ok |
| DOPRI5-adaptive | stand-only | 600 | 4.238E-001 | 3679200 | 0 | 0 | 3679200 | 9986400 |  | 4499.4 | ok |
| DOP853-adaptive | stand-only | 600 | 4.004E-001 | 6832800 | 0 | 0 | 6832800 | 19447200 |  | 8884.8 | ok |
| PEFRL | stand-only | 600 | 1.114E+000 | 2102400 | 0 | 0 | 2102400 | 5256000 |  | 3063.1 | ok |
| Y6 | stand-only | 600 | 2.778E+000 | 4204800 | 0 | 0 | 4204800 | 11563200 |  | 5352.6 | ok |
| Y6C | stand-only | 600 | 4.299E-001 | 4204800 | 0 | 0 | 4204800 | 11563200 |  | 5371.6 | ok |
| WH-Y6 | stand-only | 600 | 1.776E+000 | 4204800 | 0 | 4080475.240760947 | 8285275.240760947 | 15643675.240760947 |  | 5892.5 | ok |
| Leapfrog | stand-only | 600 | 1.825E+003 | 1051200 | 0 | 0 | 1051200 | 2102400 |  | 962.8 | ok |
| RKN12(10) | stand-only | 600 | 3.530E-001 | 9460800 | 0 | 0 | 9460800 | 27331200 |  | 11656.3 | ok |
| IAS15 | stand-only | 600 | 4.120E-001 | 7927442 | 0 | 0 | 7927442 | 22731126 |  | 12511.1 | ok |
| SRKN14A | stand-only | 600 | 4.287E-001 | 7358400 | 0 | 0 | 7358400 | 21024000 |  | 10256.8 | ok |
| WH-BM14A | stand-only | 600 | 1.742E+001 | 7358400 | 0 | 8743875.515916314 | 16102275.515916314 | 29767875.515916314 |  | 11615.0 | ok |
| GJ8 | stand-only | 600 | 4.120E-001 | 9504242 | 0 | 0 | 9504242 | 27461526 |  | 13055.3 | ok |
| Encke-IAS15 | stand-only | 600 | 6.414E+000 | 48379441 | 0 | 53655988.030646615 | 102035429.03064662 | 197743111.03064662 |  | 80545.9 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun] / 100 km

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 86400 | 7.844E-002 | 47632 | 87936 | 0 | 135568 | 135568 |  | 66.0 | ok |
| DOPRI5 [production] | production | 86400 | 1.347E+000 | 29312 | 51296 | 0 | 80608 | 80608 |  | 32.9 | ok |
| RK4 | stand-only | 86400 | 2.530E+003 | 14656 | 0 | 0 | 14656 | 36640 |  | 13.2 | ok |
| DOPRI5-fixed | stand-only | 86400 | 1.230E+000 | 25648 | 0 | 0 | 25648 | 69616 |  | 29.0 | ok |
| DOPRI5-adaptive | stand-only | 86400 | 1.230E+000 | 25648 | 0 | 0 | 25648 | 69616 |  | 29.0 | ok |
| DOP853-adaptive | stand-only | 86400 | 1.014E-001 | 47632 | 0 | 0 | 47632 | 135568 |  | 60.3 | ok |
| PEFRL | stand-only | 86400 | 4.260E+002 | 14656 | 0 | 0 | 14656 | 36640 |  | 21.7 | ok |
| Y6 | stand-only | 86400 | 4.154E+000 | 29312 | 0 | 0 | 29312 | 80608 |  | 37.4 | ok |
| Y6C | stand-only | 86400 | 4.187E+000 | 29312 | 0 | 0 | 29312 | 80608 |  | 37.6 | ok |
| WH-Y6 | stand-only | 86400 | 4.178E+000 | 29312 | 0 | 28445.322074102187 | 57757.32207410219 | 109053.32207410218 |  | 41.5 | ok |
| Leapfrog | stand-only | 86400 | 7.156E+004 | 87616 | 0 | 0 | 87616 | 255520 | 233632 | 117.1 | ok |
| RKN12(10) | stand-only | 86400 | 1.603E-002 | 65952 | 0 | 0 | 65952 | 190528 |  | 80.4 | ok |
| IAS15 | stand-only | 86400 | 6.981E-002 | 81784 | 0 | 0 | 81784 | 238024 |  | 126.4 | ok |
| SRKN14A | stand-only | 86400 | 2.191E-001 | 51296 | 0 | 0 | 51296 | 146560 |  | 71.8 | ok |
| WH-BM14A | stand-only | 86400 | 3.425E-001 | 51296 | 0 | 60954.26158736183 | 112250.26158736183 | 207514.26158736183 |  | 80.1 | ok |
| GJ8 | stand-only | 86400 | 6.981E-002 | 92776 | 0 | 0 | 92776 | 271000 |  | 129.6 | ok |
| Encke-IAS15 | stand-only | 86400 | 4.119E-002 | 385941 | 0 | 428034.000569287 | 813975.000569287 | 1578529.000569287 |  | 644.9 | ok |
| DOPRI5-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=86400 s] | stand-only | 86400 | - |  |  |  |  |  |  | - | not run |

Max-step variants (informational, not mixed into best_all):

| method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---:|---:|---:|---:|---:|---|---|
| DOPRI5-adaptive [cap=600 s] | 600 | 86400 | 4.238E-001 | 3157264 | 9464464 | max-step | ok |
| DOP853-adaptive [cap=600 s] | 600 | 86400 | 4.004E-001 | 6310864 | 18925264 | max-step | ok |
| IAS15 [cap=600 s] | 600 | 86400 | 6.591E-002 | 7887318 | 23654626 | max-step | ok |
| RKN12(10) [cap=600 s] | 600 | 86400 | 3.530E-001 | 8938864 | 26809264 | max-step | ok |

### High orbit 200 000 km, 30 d / 100 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 1.135E-004 | 56160 | 103680 | 0 | 159840 | 159840 |  | 46.3 | ok |
| DOPRI5 [production] | production | 600 | 5.794E-005 | 34560 | 60480 | 0 | 95040 | 95040 |  | 26.2 | ok |
| RK4 | stand-only | 600 | 2.281E-002 | 17280 | 0 | 0 | 17280 | 43200 |  | 7.8 | ok |
| DOPRI5-fixed | stand-only | 600 | 8.184E-005 | 30240 | 0 | 0 | 30240 | 82080 |  | 15.6 | ok |
| DOPRI5-adaptive | stand-only | 600 | 8.184E-005 | 30240 | 0 | 0 | 30240 | 82080 |  | 15.8 | ok |
| DOP853-adaptive | stand-only | 600 | 3.119E-005 | 56160 | 0 | 0 | 56160 | 159840 |  | 30.9 | ok |
| PEFRL | stand-only | 600 | 1.706E-002 | 17280 | 0 | 0 | 17280 | 43200 |  | 10.8 | ok |
| Y6 | stand-only | 600 | 5.015E-005 | 34560 | 0 | 0 | 34560 | 95040 |  | 18.2 | ok |
| Y6C | stand-only | 600 | 4.529E-005 | 34560 | 0 | 0 | 34560 | 95040 |  | 18.7 | ok |
| WH-Y6 | stand-only | 600 | 6.678E-005 | 34560 | 0 | 19569.36542669584 | 54129.36542669584 | 114609.36542669585 |  | 22.8 | ok |
| Leapfrog | stand-only | 600 | 7.411E+001 | 77760 | 0 | 0 | 77760 | 224640 | 211680 | 40.4 | ok |
| RKN12(10) | stand-only | 600 | 6.548E-006 | 77760 | 0 | 0 | 77760 | 224640 |  | 40.1 | ok |
| IAS15 | stand-only | 600 | 4.682E-005 | 98995 | 0 | 0 | 98995 | 288345 |  | 69.2 | ok |
| SRKN14A | stand-only | 600 | 3.447E-005 | 60480 | 0 | 0 | 60480 | 172800 |  | 32.8 | ok |
| WH-BM14A | stand-only | 600 | 3.767E-005 | 60480 | 0 | 41934.35448577681 | 102414.3544857768 | 214734.3544857768 |  | 43.3 | ok |
| GJ8 | stand-only | 600 | 4.682E-005 | 111955 | 0 | 0 | 111955 | 327225 |  | 73.3 | ok |
| Encke-IAS15 | stand-only | 600 | 5.422E-005 | 500704 | 0 | 324023.1331550369 | 824727.133155037 | 1817495.133155037 |  | 423.3 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### High orbit 35 786 km (GEO), 30 d / 100 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 1.392E-004 | 56160 | 103680 | 0 | 159840 | 159840 |  | 46.4 | ok |
| DOPRI5 [production] | production | 600 | 3.079E+000 | 90720 | 172800 | 0 | 263520 | 263520 | 95040 | 75.0 | ok |
| RK4 | stand-only | 600 | 2.899E+001 | 51840 | 0 | 0 | 51840 | 146880 |  | 21.1 | ok |
| DOPRI5-fixed | stand-only | 600 | 9.767E-002 | 108000 | 0 | 0 | 108000 | 315360 | 82080 | 57.9 | ok |
| DOPRI5-adaptive | stand-only | 600 | 5.642E-001 | 108000 | 0 | 0 | 108000 | 315360 | 82080 | 58.9 | ok |
| DOP853-adaptive | stand-only | 600 | 1.044E-004 | 56160 | 0 | 0 | 56160 | 159840 |  | 32.3 | ok |
| PEFRL | stand-only | 600 | 3.111E+001 | 34560 | 0 | 0 | 34560 | 95040 |  | 20.3 | ok |
| Y6 | stand-only | 600 | 4.204E+000 | 34560 | 0 | 0 | 34560 | 95040 |  | 19.3 | ok |
| Y6C | stand-only | 600 | 4.204E+000 | 34560 | 0 | 0 | 34560 | 95040 |  | 19.6 | ok |
| WH-Y6 | stand-only | 600 | 1.659E-004 | 34560 | 0 | 20705.877540868074 | 55265.877540868074 | 115745.87754086807 |  | 23.9 | ok |
| Leapfrog | stand-only | 600 | 7.614E+001 | 1118880 | 0 | 0 | 1118880 | 3348000 |  | 599.3 | ok |
| RKN12(10) | stand-only | 600 | 1.624E-005 | 77760 | 0 | 0 | 77760 | 224640 |  | 42.3 | ok |
| IAS15 | stand-only | 600 | 8.586E-005 | 146525 | 0 | 0 | 146525 | 430935 |  | 103.0 | ok |
| SRKN14A | stand-only | 600 | 1.450E-004 | 60480 | 0 | 0 | 60480 | 172800 |  | 34.0 | ok |
| WH-BM14A | stand-only | 600 | 3.202E-005 | 60480 | 0 | 44369.73758757445 | 104849.73758757445 | 217169.73758757446 |  | 45.7 | ok |
| GJ8 | stand-only | 600 | 2.277E-005 | 266915 | 0 | 0 | 266915 | 792105 |  | 181.4 | ok |
| Encke-IAS15 | stand-only | 600 | 4.072E-005 | 609894 | 0 | 417605.50518882915 | 1027499.5051888291 | 2238647.5051888293 |  | 528.7 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| RKN12(10) [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### LEO 200 km, 1 d / 10 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 4.572E-001 | 20823 | 38952 | 0 | 59775 | 59775 |  | 19.9 | ok |
| DOPRI5 [production] | production | 600 | 6.195E+000 | 25200 | 47706 | 0 | 72906 | 72906 |  | 23.7 | ok |
| RK4 | stand-only | 600 | 5.369E+000 | 49136 | 0 | 0 | 49136 | 144718 |  | 30.0 | ok |
| DOPRI5-fixed | stand-only | 600 | 7.804E+000 | 20659 | 0 | 0 | 20659 | 59287 |  | 14.7 | ok |
| DOPRI5-adaptive | stand-only | 600 | 3.160E+000 | 25273 | 0 | 0 | 25273 | 73129 | 63589 | 18.4 | ok |
| DOP853-adaptive | stand-only | 600 | 6.025E-001 | 20797 | 0 | 0 | 20797 | 59701 |  | 15.9 | ok |
| IAS15 | stand-only | 600 | 7.437E-003 | 35435 | 0 | 0 | 35435 | 103615 |  | 32.4 | ok |
| Encke-IAS15 | stand-only | 600 | 1.515E-002 | 160021 | 0 | 45157.74275353767 | 205178.74275353766 | 522530.74275353766 |  | 171.5 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### LEO 300 km, 30 d / 100 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 3.680E+000 | 322359 | 633672 | 0 | 956031 | 956031 |  | 322.8 | ok |
| DOPRI5 [production] | production | 600 | 5.802E+001 | 1011912 | 2012778 | 0 | 3024690 | 3024690 | 2774904 | 980.7 | ok |
| RK4 | stand-only | 600 | 6.431E+001 | 1335344 | 0 | 0 | 1335344 | 3994990 |  | 803.3 | ok |
| DOPRI5-fixed | stand-only | 600 | 3.123E+000 | 1671589 | 0 | 0 | 1671589 | 5003725 | 2515459 | 1187.1 | ok |
| DOPRI5-adaptive | stand-only | 600 | 2.730E+001 | 1140499 | 0 | 0 | 1140499 | 3410455 | 2751313 | 811.8 | ok |
| DOP853-adaptive | stand-only | 600 | 2.591E+001 | 223045 | 0 | 0 | 223045 | 658093 |  | 171.0 | ok |
| IAS15 | stand-only | 600 | 2.315E-001 | 290344 | 0 | 0 | 290344 | 859990 |  | 258.7 | ok |
| Encke-IAS15 | stand-only | 600 | 4.606E-001 | 1150652 | 0 | 323666.4817347966 | 1474318.4817347967 | 3764580.4817347964 |  | 1292.9 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 / 0.1 m/s

| method | class | max step | err max v | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 1.768E-008 | 15613 | 28824 | 0 | 44437 | 44437 |  | 20.1 | ok |
| DOPRI5 [production] | production | 600 | 1.486E-008 | 9608 | 16814 | 0 | 26422 | 26422 |  | 11.7 | ok |
| RK4 | stand-only | 600 | 1.773E-007 | 4808 | 0 | 0 | 4808 | 12024 |  | 3.2 | ok |
| DOPRI5-fixed | stand-only | 600 | 1.817E-007 | 8412 | 0 | 0 | 8412 | 22836 |  | 6.7 | ok |
| DOPRI5-adaptive | stand-only | 600 | 3.277E-007 | 8400 | 0 | 0 | 8400 | 22800 |  | 7.5 | ok |
| DOP853-adaptive | stand-only | 600 | 3.188E-007 | 15600 | 0 | 0 | 15600 | 44400 |  | 15.2 | ok |
| IAS15 | stand-only | 600 | 3.262E-007 | 43571 | 0 | 0 | 43571 | 128313 |  | 40.0 | ok |
| Encke-IAS15 | stand-only | 600 | 1.291E-007 | 194726 | 0 | 112874.5116618657 | 307600.5116618657 | 694652.5116618657 |  | 218.0 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 / 0.1 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 6.321E-006 | 15613 | 28824 | 0 | 44437 | 44437 |  | 20.1 | ok |
| DOPRI5 [production] | production | 600 | 6.358E-006 | 9608 | 16814 | 0 | 26422 | 26422 |  | 11.7 | ok |
| RK4 | stand-only | 600 | 6.702E-006 | 4808 | 0 | 0 | 4808 | 12024 |  | 3.2 | ok |
| DOPRI5-fixed | stand-only | 600 | 6.702E-006 | 8412 | 0 | 0 | 8412 | 22836 |  | 6.7 | ok |
| DOPRI5-adaptive | stand-only | 600 | 1.963E-005 | 8400 | 0 | 0 | 8400 | 22800 |  | 7.5 | ok |
| DOP853-adaptive | stand-only | 600 | 1.960E-005 | 15600 | 0 | 0 | 15600 | 44400 |  | 15.2 | ok |
| IAS15 | stand-only | 600 | 1.963E-005 | 43571 | 0 | 0 | 43571 | 128313 |  | 40.0 | ok |
| Encke-IAS15 | stand-only | 600 | 7.817E-006 | 194726 | 0 | 112874.5116618657 | 307600.5116618657 | 694652.5116618657 |  | 218.0 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal / 0.1 m/s

| method | class | max step | err max v | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 2.598E-008 | 7904 | 14592 | 0 | 22496 | 22496 |  | 9.3 | ok |
| DOPRI5 [production] | production | 600 | 4.304E-008 | 4864 | 8512 | 0 | 13376 | 13376 |  | 4.4 | ok |
| RK4 | stand-only | 600 | 5.362E-008 | 2432 | 0 | 0 | 2432 | 6080 |  | 1.7 | ok |
| DOPRI5-fixed | stand-only | 600 | 4.233E-008 | 4256 | 0 | 0 | 4256 | 11552 |  | 3.4 | ok |
| DOPRI5-adaptive | stand-only | 600 | 4.233E-008 | 4256 | 0 | 0 | 4256 | 11552 |  | 3.3 | ok |
| DOP853-adaptive | stand-only | 600 | 1.792E-008 | 7904 | 0 | 0 | 7904 | 22496 |  | 6.5 | ok |
| IAS15 | stand-only | 600 | 1.710E-008 | 22742 | 0 | 0 | 22742 | 67010 |  | 22.2 | ok |
| Encke-IAS15 | stand-only | 600 | 2.921E-008 | 100562 | 0 | 57740.64673520016 | 158302.64673520016 | 358210.6467352002 |  | 112.0 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal / 0.1 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 3.437E-006 | 7904 | 14592 | 0 | 22496 | 22496 |  | 9.3 | ok |
| DOPRI5 [production] | production | 600 | 3.371E-006 | 4864 | 8512 | 0 | 13376 | 13376 |  | 4.4 | ok |
| RK4 | stand-only | 600 | 1.701E-007 | 2432 | 0 | 0 | 2432 | 6080 |  | 1.7 | ok |
| DOPRI5-fixed | stand-only | 600 | 1.372E-007 | 4256 | 0 | 0 | 4256 | 11552 |  | 3.4 | ok |
| DOPRI5-adaptive | stand-only | 600 | 1.372E-007 | 4256 | 0 | 0 | 4256 | 11552 |  | 3.3 | ok |
| DOP853-adaptive | stand-only | 600 | 1.328E-007 | 7904 | 0 | 0 | 7904 | 22496 |  | 6.5 | ok |
| IAS15 | stand-only | 600 | 2.395E-007 | 22742 | 0 | 0 | 22742 | 67010 |  | 22.2 | ok |
| Encke-IAS15 | stand-only | 600 | 4.233E-007 | 100562 | 0 | 57740.64673520016 | 158302.64673520016 | 358210.6467352002 |  | 112.0 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface hover 0.5 km, 0 m/s relative / 0.1 m/s

| method | class | max step | err max v | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 3.453E-009 | 15600 | 28800 | 0 | 44400 | 44400 |  | 14.3 | ok |
| DOPRI5 [production] | production | 600 | 3.689E-009 | 9600 | 16800 | 0 | 26400 | 26400 |  | 9.1 | ok |
| RK4 | stand-only | 600 | 3.654E-009 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.4 | ok |
| DOPRI5-fixed | stand-only | 600 | 3.691E-009 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOPRI5-adaptive | stand-only | 600 | 3.691E-009 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 3.455E-009 | 15600 | 0 | 0 | 15600 | 44400 |  | 11.9 | ok |
| IAS15 | stand-only | 600 | 3.656E-009 | 34842 | 0 | 0 | 34842 | 102126 |  | 31.9 | ok |
| Encke-IAS15 | stand-only | 600 | 3.472E-007 | 161945 | 0 | 90300.3488631425 | 252245.34886314248 | 573735.3488631425 |  | 181.2 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface hover 0.5 km, 0 m/s relative / 0.1 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 6.474E-006 | 15600 | 28800 | 0 | 44400 | 44400 |  | 14.3 | ok |
| DOPRI5 [production] | production | 600 | 6.473E-006 | 9600 | 16800 | 0 | 26400 | 26400 |  | 9.1 | ok |
| RK4 | stand-only | 600 | 6.473E-006 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.4 | ok |
| DOPRI5-fixed | stand-only | 600 | 6.473E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOPRI5-adaptive | stand-only | 600 | 6.473E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 6.473E-006 | 15600 | 0 | 0 | 15600 | 44400 |  | 11.9 | ok |
| IAS15 | stand-only | 600 | 6.473E-006 | 34842 | 0 | 0 | 34842 | 102126 |  | 31.9 | ok |
| Encke-IAS15 | stand-only | 600 | 1.222E-005 | 161945 | 0 | 90300.3488631425 | 252245.34886314248 | 573735.3488631425 |  | 181.2 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface level flight 0.5 km, 100 m/s horizontal / 0.1 m/s

| method | class | max step | err max v | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 5.317E-008 | 15600 | 28800 | 0 | 44400 | 44400 |  | 14.8 | ok |
| DOPRI5 [production] | production | 600 | 3.938E-008 | 9600 | 16800 | 0 | 26400 | 26400 |  | 9.3 | ok |
| RK4 | stand-only | 600 | 3.934E-008 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.2 | ok |
| DOPRI5-fixed | stand-only | 600 | 3.949E-008 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.2 | ok |
| DOPRI5-adaptive | stand-only | 600 | 3.949E-008 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 5.354E-008 | 15600 | 0 | 0 | 15600 | 44400 |  | 12.1 | ok |
| IAS15 | stand-only | 600 | 4.122E-008 | 40575 | 0 | 0 | 40575 | 119325 |  | 37.2 | ok |
| Encke-IAS15 | stand-only | 600 | 9.484E-008 | 198982 | 0 | 112576.87280880555 | 311558.87280880555 | 707122.8728088056 |  | 222.3 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface level flight 0.5 km, 100 m/s horizontal / 0.1 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 6.917E-006 | 15600 | 28800 | 0 | 44400 | 44400 |  | 14.8 | ok |
| DOPRI5 [production] | production | 600 | 7.366E-006 | 9600 | 16800 | 0 | 26400 | 26400 |  | 9.3 | ok |
| RK4 | stand-only | 600 | 7.365E-006 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.2 | ok |
| DOPRI5-fixed | stand-only | 600 | 7.362E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.2 | ok |
| DOPRI5-adaptive | stand-only | 600 | 7.362E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 6.971E-006 | 15600 | 0 | 0 | 15600 | 44400 |  | 12.1 | ok |
| IAS15 | stand-only | 600 | 7.315E-006 | 40575 | 0 | 0 | 40575 | 119325 |  | 37.2 | ok |
| Encke-IAS15 | stand-only | 600 | 8.817E-006 | 198982 | 0 | 112576.87280880555 | 311558.87280880555 | 707122.8728088056 |  | 222.3 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface level flight 1 km, 100 m/s horizontal / 0.1 m

| method | class | max step | err max r | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 7.788E-006 | 15600 | 28800 | 0 | 44400 | 44400 |  | 16.4 | ok |
| DOPRI5 [production] | production | 600 | 7.833E-006 | 9600 | 16800 | 0 | 26400 | 26400 |  | 8.6 | ok |
| RK4 | stand-only | 600 | 7.832E-006 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.3 | ok |
| DOPRI5-fixed | stand-only | 600 | 7.836E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.4 | ok |
| DOPRI5-adaptive | stand-only | 600 | 7.836E-006 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 7.813E-006 | 15600 | 0 | 0 | 15600 | 44400 |  | 12.1 | ok |
| IAS15 | stand-only | 600 | 7.783E-006 | 40561 | 0 | 0 | 40561 | 119283 |  | 38.1 | ok |
| Encke-IAS15 | stand-only | 600 | 9.312E-006 | 197925 | 0 | 112228.28517697823 | 310153.2851769782 | 703603.2851769782 |  | 221.5 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

### Surface level flight 1 km, 100 m/s horizontal / 0.1 m/s

| method | class | max step | err max v | cost rhs | local | aux | total | ported | edge (ported) | ms | status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| DOP853 [production] | production | 600 | 4.461E-009 | 15600 | 28800 | 0 | 44400 | 44400 |  | 16.4 | ok |
| DOPRI5 [production] | production | 600 | 2.340E-008 | 9600 | 16800 | 0 | 26400 | 26400 |  | 8.6 | ok |
| RK4 | stand-only | 600 | 2.328E-008 | 4800 | 0 | 0 | 4800 | 12000 |  | 3.3 | ok |
| DOPRI5-fixed | stand-only | 600 | 2.339E-008 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.4 | ok |
| DOPRI5-adaptive | stand-only | 600 | 2.339E-008 | 8400 | 0 | 0 | 8400 | 22800 |  | 6.3 | ok |
| DOP853-adaptive | stand-only | 600 | 4.463E-009 | 15600 | 0 | 0 | 15600 | 44400 |  | 12.1 | ok |
| IAS15 | stand-only | 600 | 2.312E-008 | 40561 | 0 | 0 | 40561 | 119283 |  | 38.1 | ok |
| Encke-IAS15 | stand-only | 600 | 9.695E-008 | 197925 | 0 | 112228.28517697823 | 310153.2851769782 | 703603.2851769782 |  | 221.5 | ok |
| DOPRI5-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| DOP853-adaptive [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |
| IAS15 [cap=600 s] | stand-only | 600 | - |  |  |  |  |  |  | - | not run |

## Decision table (compact; cost_total / cost_if_ported)

| regime | scenario | scope | winner bound (heuristic) | production bound (heuristic) | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|---|---|---|
| cislunar | "Cislunar Moon flyby, SOI entry-exit, 10 d" | config | chunk | chunk | 100 m | DOPRI5 [production] [kernel switch] | DOPRI5-adaptive (requires port) | tie: DOPRI5-adaptive (requires port)@30,096/1.18E-001, DOPRI5 [production]@33,408/2.25E-001 | low | best_all includes production; production option DOPRI5 [production] [kernel switch] is within 11.0% of best_all; production +11% vs best_all; edge 92,160 (206% more expensive than best_all, 176% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| far-flight | "Far flight 30 AU e 0.5, 10 y" | config | max-step | max-step | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +500% vs best_all; no edge runs; winner bound max-step-bound (600 s), production bound max-step-bound (600 s) (heuristic) |
| far-flight | "Far flight 30 AU e 0.5, 10 y [regime86400]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y" | config | chunk | chunk | 10 km | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@525,600/6.00E-005, RK4 (requires port)@525,600/1.22E-004 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 3,679,200 (600% more expensive than best_all, 218% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | config | chunk | chunk | 10 km | DOP853 [production] [current default] | WH-Y6 (requires port) | WH-Y6 (requires port) | medium | best_all is a port candidate; production +51% vs best_all; edge 30,350 (176% more expensive than best_all, 83% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@36,640/4.26E-003, RK4 (requires port)@36,640/2.53E-002 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 233,632 (538% more expensive than best_all, 190% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| high-orbit | "High orbit 200 000 km, 30 d" | config | chunk | chunk | 100 m | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@43,200/1.71E-004, RK4 (requires port)@43,200/2.28E-004 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 211,680 (390% more expensive than best_all, 123% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| high-orbit | "High orbit 35 786 km (GEO), 30 d" | config | chunk | chunk | 100 m | DOP853 [production] [current default] | tie: Y6 (requires port), Y6C (requires port) | tie: Y6 (requires port)@95,040/4.20E-002, Y6C (requires port)@95,040/4.20E-002, PEFRL (requires port)@95,040/3.11E-001 | low | best_all is a tie of port candidates; production +68% vs best_all; edge 82,080 (14% cheaper than best_all, 49% cheaper than production); winner bound chunk, production bound chunk (heuristic) |
| leo | "LEO 200 km, 1 d" | config | tolerance | chunk | 10 m | DOP853 [production] [current default] | DOPRI5-fixed (requires port) | tie: DOPRI5-fixed (requires port)@59,287/7.80E-001, DOP853-adaptive (requires port)@59,701/6.03E-002, DOP853 [production]@59,775/4.57E-002 | low | best_all includes production; production option DOP853 [production] [current default] is within 0.8% of best_all; production +1% vs best_all; edge 63,589 (7% more expensive than best_all, 6% more expensive than production); winner bound tolerance, production bound chunk (heuristic) |
| leo | "LEO 300 km, 30 d" | config | tolerance | tolerance | 100 m | DOP853 [production] [current default] | DOP853-adaptive (requires port) | DOP853-adaptive (requires port) | medium | best_all is a port candidate; production +45% vs best_all; edge 2,515,459 (282% more expensive than best_all, 163% more expensive than production); winner bound tolerance, production bound tolerance (heuristic) |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |

Full acceptable percentages, selected_best_all_total, selected_production_total and
the per-row edge policy are in `Test560b_DecisionTable.csv` and
`Test560b_DecisionDetails.md`.

### Prescribed-force surface hover/level (separate section)

These are prescribed-force trajectories, not closed-loop altitude hold.
`ThrustScalesWithMass` uses a linear mass estimate; the force is prescribed.

| regime | scenario | scope | winner bound (heuristic) | production bound (heuristic) | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|---|---|---|
| surface | "Surface hover 0.5 km, 0 m/s relative" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface hover 0.5 km, 0 m/s relative" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |

## Far / heliocentric: production-600 vs regime86400

The production-600 cell is the game's current configuration. The 86 400 s
cell is an explicit regime sensitivity, not the current production default.
Both are shown; the ratio is not hidden inside a single winner.

| scenario | target | best prod-600 | cost | best regime86400 | cost | best ratio | DOP853 prod-600 | DOP853 regime86400 | DOP853 ratio |
|---|---|---|---:|---|---:|---:|---:|---:|---:|
| "Heliocentric 0.3 AU e 0.5, 1 y" | 10 km | RK4 (requires port) | 525,600 | WH-Y6 (requires port) | 10,988 | 47.8x | 1944720 | 16583 | 117.3x |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 100 km | Leapfrog (requires port) | 2,102,400 | RK4 (requires port) | 36,640 | 57.4x | 19447200 | 135568 | 143.4x |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | Leapfrog (requires port) | 1,580,464 | Leapfrog (requires port) | 14,656 | 107.8x | 18925264 | 135568 | 139.6x |

## Wall-clock and metric sensitivity (all methods vs production-only)

- all-method `cost_total` wins: 12 / 18 cells.
- production-only `cost_total` wins: 15 / 18 production cells.
- all-method wall-clock vs `cost_total`: 1 / 18 cells where the wall-clock winner differs from cost_total.
- production-only wall-clock vs `cost_total`: 0 / 18 production cells where the wall-clock winner differs from cost_total.
- production-only `cost_rhs` vs `cost_total`: 0 / 18 production cells where the cost_rhs winner differs from cost_total.
- all-method `cost_if_ported` vs `cost_total`: 0 / 18 cells where the cost_if_ported winner differs from cost_total.

## Validity summary (numbers from `Test560b_Validity.csv`)

| check | scenario | method | metric | value | threshold | status | note |
|---|---|---|---|---:|---:|---|---|
| production_default | - | - | default_integrator_is_dop853 | 1 | 1 | PASS | Dop853 |
| reference_resolution | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | - | floor_m/floor_v | 2.751621147421441E-07 | 6.879052868553602E-07 | resolved_finest_delta | floor_m=2.752E-007; floor_v=6.879E-007; fixed DOP853 h=0.05 s, ratio=2.45 order=1.30 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=2.75E-007 m retry 2 RESOLVED WITHOUT RATIO: finest d=2.752E-007 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | - | floor_m/floor_v | 1.2022966698118821E-07 | 1.984131484954506E-08 | ratio_ok | floor_m=1.202E-007; floor_v=1.984E-008; fixed DOP853 h=0.0125 s, ratio=46.85 order=5.55 (floor from finest disagreement), est 1.20E-007 m / 2.0E-008 m/s, finest d=1.20E-007 m |
| reference_resolution | Surface hover 0.5 km, 0 m/s relative | - | floor_m/floor_v | 2.263609484195607E-08 | 5.6590237104890166E-08 | resolved_finest_delta | floor_m=2.264E-008; floor_v=5.659E-008; fixed DOP853 h=0.025 s, ratio=NaN order=NaN UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=2.26E-008 m retry 2 RESOLVED WITHOUT RATIO: finest d=2.264E-008 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Surface level flight 0.5 km, 100 m/s horizontal | - | floor_m/floor_v | 2.8161806132913515E-07 | 7.040451533228379E-07 | resolved_finest_delta | floor_m=2.816E-007; floor_v=7.040E-007; fixed DOP853 h=0.2 s, ratio=1.83 order=0.87 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=2.82E-007 m retry 2 RESOLVED WITHOUT RATIO: finest d=2.816E-007 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Surface level flight 1 km, 100 m/s horizontal | - | floor_m/floor_v | 3.27674407264988E-07 | 8.191860181624699E-07 | resolved_finest_delta | floor_m=3.277E-007; floor_v=8.192E-007; fixed DOP853 h=0.05 s, ratio=2.73 order=1.45 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=3.28E-007 m retry 2 RESOLVED WITHOUT RATIO: finest d=3.277E-007 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | LEO 200 km, 1 d | - | floor_m/floor_v | 1.9805465827965774E-05 | 1.6504554856638145E-07 | resolved_finest_delta | floor_m=1.981E-005; floor_v=1.650E-007; fixed DOP853 h=7.5 s, ratio=6.34 order=2.66 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=1.98E-005 m retry 2 RESOLVED WITHOUT RATIO: finest d=1.981E-005 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | LEO 300 km, 30 d | - | floor_m/floor_v | 0.001541339771916362 | 1.7378762014516959E-06 | ratio_ok | floor_m=1.541E-003; floor_v=1.738E-006; fixed DOP853 h=18.75 s, ratio=528.08 order=9.04 (floor from finest disagreement), est 1.54E-003 m / 1.7E-006 m/s, finest d=1.54E-003 m |
| reference_resolution | High orbit 35 786 km (GEO), 30 d | - | floor_m/floor_v | 9.64171414807208E-06 | 1.6069523580120135E-08 | resolved_finest_delta | floor_m=9.642E-006; floor_v=1.607E-008; fixed DOP853 h=37.5 s, ratio=3.19 order=1.67 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.64E-006 m retry 2 RESOLVED WITHOUT RATIO: finest d=9.642E-006 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | High orbit 200 000 km, 30 d | - | floor_m/floor_v | 8.808490763140521E-07 | 1.4680817938567535E-09 | resolved_finest_delta | floor_m=8.808E-007; floor_v=1.468E-009; fixed DOP853 h=300 s, ratio=1.38 order=0.47 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=8.81E-007 m retry 2 RESOLVED WITHOUT RATIO: finest d=8.808E-007 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Cislunar Moon flyby, SOI entry-exit, 10 d | - | floor_m/floor_v | 0.001326778003403687 | 8.845186689357915E-06 | resolved_finest_delta | floor_m=1.327E-003; floor_v=8.845E-006; fixed DOP853 h=9.375 s, ratio=3.20 order=1.68 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=1.33E-003 m retry 2 RESOLVED WITHOUT RATIO: finest d=1.327E-003 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Heliocentric 0.3 AU e 0.5, 1 y | - | floor_m/floor_v | 0.000731101932860252 | 1.0154193511947945E-07 | resolved_finest_delta | floor_m=7.311E-004; floor_v=1.015E-007; fixed DOP853 h=900 s, ratio=16.12 order=4.01 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=7.31E-004 m retry 2 RESOLVED WITHOUT RATIO: finest d=7.311E-004 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Heliocentric 0.3 AU e 0.5, 1 y [regime86400] | - | floor_m/floor_v | 0.000731101932860252 | 1.0154193511947945E-07 | resolved_finest_delta | floor_m=7.311E-004; floor_v=1.015E-007; fixed DOP853 h=900 s, ratio=16.12 order=4.01 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=7.31E-004 m retry 2 RESOLVED WITHOUT RATIO: finest d=7.311E-004 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | - | floor_m/floor_v | 0.01274898079054503 | 3.5413835529291753E-07 | resolved_finest_delta | floor_m=1.275E-002; floor_v=3.541E-007; fixed DOP853 h=9000 s, ratio=1.96 order=0.97 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=1.27E-002 m retry 2 RESOLVED WITHOUT RATIO: finest d=1.275E-002 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun] | - | floor_m/floor_v | 0.01274898079054503 | 3.5413835529291753E-07 | resolved_finest_delta | floor_m=1.275E-002; floor_v=3.541E-007; fixed DOP853 h=9000 s, ratio=1.96 order=0.97 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=1.27E-002 m retry 2 RESOLVED WITHOUT RATIO: finest d=1.275E-002 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Far flight 30 AU e 0.5, 10 y | - | floor_m/floor_v | 0.009865273896297811 | 1.1418141083678022E-07 | resolved_finest_delta | floor_m=9.865E-003; floor_v=1.142E-007; fixed DOP853 h=5400 s, ratio=NaN order=NaN UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.87E-003 m retry 2 RESOLVED WITHOUT RATIO: finest d=9.865E-003 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| reference_resolution | Far flight 30 AU e 0.5, 10 y [regime86400] | - | floor_m/floor_v | 0.009865273896297811 | 1.1418141083678022E-07 | resolved_finest_delta | floor_m=9.865E-003; floor_v=1.142E-007; fixed DOP853 h=5400 s, ratio=NaN order=NaN UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.87E-003 m retry 2 RESOLVED WITHOUT RATIO: finest d=9.865E-003 m <= 0.01*target; no Richardson pair (stiff/round-off plateau); weaker certificate: finest observed delta, no Richardson ratio (not called certified) |
| production_parity_matched_atol_reference | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | DOP853 | prod_vs_stand_max_m | 1.9186280982559914E-05 | 0.01 | PASS | reference; rtol=1e-12; max step 600; matched atol=0/0/0 (production pos/vel/mass atol == stand cfg.Atol=0) |
| production_parity_matched_atol_reference | LEO 200 km, 1 d | DOP853 | prod_vs_stand_max_m | 0.007121609593203267 | 1 | PASS | reference; rtol=1e-12; max step 600; matched atol=0/0/0 (production pos/vel/mass atol == stand cfg.Atol=0) |
| production_parity_matched_atol_reference | High orbit 35 786 km (GEO), 30 d | DOP853 | prod_vs_stand_max_m | 0.00024357853200688903 | 10 | PASS | reference; rtol=1e-12; max step 600; matched atol=0/0/0 (production pos/vel/mass atol == stand cfg.Atol=0) |
| production_parity_game_default | LEO 300 km, 30 d | DOPRI5 | prod_vs_stand_max_m | 68.05912642276951 | 10 | FINDING | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=1e-12; max step 600; production err 9.536E+001 m; stand DOPRI5 err 2.730E+001 m |
| production_parity_matched_atol | LEO 300 km, 30 d | DOPRI5 | prod_vs_stand_max_m | 9.834958747051864 | 10 | PASS | matched atol=0/0/0 (production pos/vel/mass atol == stand cfg.Atol=0); rtol=1e-12; max step 600; production err 1.746E+001 m; stand DOPRI5 err 2.730E+001 m |
| local_frame_error_control | LEO 300 km, 30 d | DOPRI5 | prod_minus_stand_at_matched_atol_m | 9.834958747051864 | 10 | BORDERLINE | matched atol=0/0/0 (production pos/vel/mass atol == stand cfg.Atol=0); rtol=1e-12; production err 1.746E+001 m; stand DOPRI5 err 2.730E+001 m; matched diff 9.835E+000 m (98.35% of threshold); game-default atol diff 6.806E+001 m; DOP853 matched-atol (0/0/0) reference 4.550E-001 m; production atol 1e-4/1e-8/1e-4 vs stand Atol=0 (the game-default row); matched-atol difference is inside target/10 but above target/20: BORDERLINE, not a pass claim |
| production_parity_game_default | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | DOPRI5 | prod_vs_stand_max_m | 1.9262401371808124E-05 | 0.01 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=1e-12; surface drag/thrust/variable mass; max step 600 |
| best_all_tie_class | Cislunar Moon flyby, SOI entry-exit, 10 d | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: DOPRI5-adaptive (requires port)@30,096/1.18E-001, DOPRI5 [production]@33,408/2.25E-001"; class="tie: production+stand-only"; production option is inside the tie, no port required for it |
| best_all_tie_class | Heliocentric 0.3 AU e 0.5, 1 y | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: PEFRL (requires port)@525,600/6.00E-005, RK4 (requires port)@525,600/1.22E-004"; class="tie: stand-only" |
| best_all_tie_class | Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun] | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: PEFRL (requires port)@36,640/4.26E-003, RK4 (requires port)@36,640/2.53E-002"; class="tie: stand-only" |
| best_all_tie_class | High orbit 200 000 km, 30 d | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: PEFRL (requires port)@43,200/1.71E-004, RK4 (requires port)@43,200/2.28E-004"; class="tie: stand-only" |
| best_all_tie_class | High orbit 35 786 km (GEO), 30 d | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: Y6 (requires port)@95,040/4.20E-002, Y6C (requires port)@95,040/4.20E-002, PEFRL (requires port)@95,040/3.11E-001"; class="tie: stand-only" |
| best_all_tie_class | LEO 200 km, 1 d | - | tie_class_matches_tie_members | 1 | 1 | PASS | best_all="tie: DOPRI5-fixed (requires port)@59,287/7.80E-001, DOP853-adaptive (requires port)@59,701/6.03E-002, DOP853 [production]@59,775/4.57E-002"; class="tie: production+stand-only"; production option is inside the tie, no port required for it |
| selection_metric_consistent | - | - | control_matches_cost_if_ported_selection | 0 | 0 | PASS | 296 ok/edge cells re-selected from Test560b_Runs.csv controls; 0 cell(s) where the cost_total order would pick a different control |
| production_parity_game_default | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | DOPRI5 | prod_vs_stand_max_m | 3.4012498074575885E-06 | 0.01 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (0.1 m); production err 3.371E-006 m; stand DOPRI5 err 1.372E-007 m; cap 10M RHS |
| production_parity_game_default | Surface hover 0.5 km, 0 m/s relative | DOPRI5 | prod_vs_stand_max_m | 2.887112106350497E-08 | 0.01 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (0.1 m); production err 6.473E-006 m; stand DOPRI5 err 6.473E-006 m; cap 10M RHS |
| production_parity_game_default | Surface level flight 0.5 km, 100 m/s horizontal | DOPRI5 | prod_vs_stand_max_m | 2.4214420007545643E-08 | 0.01 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (0.1 m); production err 7.366E-006 m; stand DOPRI5 err 7.362E-006 m; cap 10M RHS |
| production_parity_game_default | Surface level flight 1 km, 100 m/s horizontal | DOPRI5 | prod_vs_stand_max_m | 1.5192563640397045E-08 | 0.01 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (0.1 m); production err 7.833E-006 m; stand DOPRI5 err 7.836E-006 m; cap 10M RHS |
| production_parity_game_default | LEO 200 km, 1 d | DOPRI5 | prod_vs_stand_max_m | 0.8969631073799559 | 1 | BORDERLINE | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=1E-10; max step 600; one target per scenario (10 m); production err 2.263E+000 m; stand DOPRI5 err 3.160E+000 m; cap 10M RHS |
| production_parity_game_default | High orbit 35 786 km (GEO), 30 d | DOPRI5 | prod_vs_stand_max_m | 0.7541318818851337 | 10 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=1E-12; max step 600; one target per scenario (100 m); production err 1.318E+000 m; stand DOPRI5 err 5.642E-001 m; cap 10M RHS |
| production_parity_game_default | High orbit 200 000 km, 30 d | DOPRI5 | prod_vs_stand_max_m | 3.175323492146992E-05 | 10 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (100 m); production err 5.794E-005 m; stand DOPRI5 err 8.184E-005 m; cap 10M RHS |
| production_parity_game_default | Cislunar Moon flyby, SOI entry-exit, 10 d | DOPRI5 | prod_vs_stand_max_m | 7.101018448709519 | 10 | BORDERLINE | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=1E-09; max step 600; one target per scenario (100 m); production err 4.675E+000 m; stand DOPRI5 err 1.177E+001 m; cap 10M RHS |
| production_parity_game_default | Heliocentric 0.3 AU e 0.5, 1 y | DOPRI5 | prod_vs_stand_max_m | 0.012030793784560009 | 1000 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (10 km); production err 1.976E-002 m; stand DOPRI5 err 2.309E-002 m; cap 10M RHS |
| production_parity_game_default | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | DOPRI5 | prod_vs_stand_max_m | 1.3298583682398264 | 10000 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (100 km); production err 1.179E+000 m; stand DOPRI5 err 4.238E-001 m; cap 10M RHS |
| production_parity_game_default | Far flight 30 AU e 0.5, 10 y | DOPRI5 | prod_vs_stand_max_m | 0.31548646587306023 | 10000 | PASS | game-default atol=1e-4/1e-8/1e-4; stand cfg.Atol=0; rtol=0.0001; max step 600; one target per scenario (100 km); production err 3.835E-001 m; stand DOPRI5 err 2.508E-001 m; cap 10M RHS |
| tie_break_by_error | Cislunar Moon flyby, SOI entry-exit, 10 d | - | exact_cost_tie_err_order (target 100 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 30,096; selected DOPRI5-adaptive err/target 1.177E-001; best error DOPRI5-adaptive 1.177E-001; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Far flight 30 AU e 0.5, 10 y | - | exact_cost_tie_err_order (target 100 km) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 1,580,464; selected Leapfrog err/target 1.337E-005; best error Leapfrog 1.337E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Far flight 30 AU e 0.5, 10 y [regime86400] | - | exact_cost_tie_err_order (target 100 km) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 14,656; selected Leapfrog err/target 2.828E-001; best error Leapfrog 2.828E-001; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Heliocentric 0.3 AU e 0.5, 1 y | - | exact_cost_tie_err_order (target 10 km) | 2 | 1 | PASS | 2 row(s) at the minimum comparable cost 525,600; selected PEFRL err/target 6.000E-005; best error PEFRL 6.000E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Heliocentric 0.3 AU e 0.5, 1 y [regime86400] | - | exact_cost_tie_err_order (target 10 km) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 10,988; selected WH-Y6 err/target 1.325E-005; best error WH-Y6 1.325E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | - | exact_cost_tie_err_order (target 100 km) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 2,102,400; selected Leapfrog err/target 1.825E-002; best error Leapfrog 1.825E-002; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun] | - | exact_cost_tie_err_order (target 100 km) | 2 | 1 | PASS | 2 row(s) at the minimum comparable cost 36,640; selected PEFRL err/target 4.260E-003; best error PEFRL 4.260E-003; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | High orbit 200 000 km, 30 d | - | exact_cost_tie_err_order (target 100 m) | 2 | 1 | PASS | 2 row(s) at the minimum comparable cost 43,200; selected PEFRL err/target 1.706E-004; best error PEFRL 1.706E-004; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | High orbit 35 786 km (GEO), 30 d | - | exact_cost_tie_err_order (target 100 m) | 3 | 1 | PASS | 3 row(s) at the minimum comparable cost 95,040; selected Y6 err/target 4.204E-002; best error Y6 4.204E-002; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | LEO 200 km, 1 d | - | exact_cost_tie_err_order (target 10 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 59,287; selected DOPRI5-fixed err/target 7.804E-001; best error DOPRI5-fixed 7.804E-001; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | LEO 300 km, 30 d | - | exact_cost_tie_err_order (target 100 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 658,093; selected DOP853-adaptive err/target 2.591E-001; best error DOP853-adaptive 2.591E-001; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | - | exact_cost_tie_err_order (target 0.1 m/s) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,024; selected RK4 err/target 1.773E-006; best error RK4 1.773E-006; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | - | exact_cost_tie_err_order (target 0.1 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,024; selected RK4 err/target 6.702E-005; best error RK4 6.702E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m/s) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 6,080; selected RK4 err/target 5.362E-007; best error RK4 5.362E-007; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 6,080; selected RK4 err/target 1.701E-006; best error RK4 1.701E-006; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface hover 0.5 km, 0 m/s relative | - | exact_cost_tie_err_order (target 0.1 m/s) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 3.654E-008; best error RK4 3.654E-008; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface hover 0.5 km, 0 m/s relative | - | exact_cost_tie_err_order (target 0.1 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 6.473E-005; best error RK4 6.473E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface level flight 0.5 km, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m/s) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 3.934E-007; best error RK4 3.934E-007; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface level flight 0.5 km, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 7.365E-005; best error RK4 7.365E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface level flight 1 km, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 7.832E-005; best error RK4 7.832E-005; epsilon 0.005 relative (differences below it keep matrix order) |
| tie_break_by_error | Surface level flight 1 km, 100 m/s horizontal | - | exact_cost_tie_err_order (target 0.1 m/s) | 1 | 1 | PASS | 1 row(s) at the minimum comparable cost 12,000; selected RK4 err/target 2.328E-007; best error RK4 2.328E-007; epsilon 0.005 relative (differences below it keep matrix order) |

## Sensitivity (not production)

| scenario | config | method | max step | chunk | target | err | total | status |
|---|---|---|---:|---|---:|---:|---:|---|
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 600 | fixed chunk 0.5 s | 0.1 m | 1.2722809979115505E-06 | 4736 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 600 | fixed chunk 0.5 s | 0.1 m/s | 4.285017530696575E-08 | 4736 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 600 | fixed chunk 0.5 s | 0.1 m | 2.42373128437785E-07 | 2816 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 600 | fixed chunk 0.5 s | 0.1 m/s | 7.074951461591964E-08 | 2816 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 600 | fixed chunk 0.5 s | 0.1 m | 0.00037115062267059114 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 600 | fixed chunk 0.5 s | 0.1 m/s | 3.394348278812014E-05 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 600 | fixed chunk 0.5 s | 0.1 m | 2.3068380873350602E-07 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 600 | fixed chunk 0.5 s | 0.1 m/s | 5.5948176839286846E-08 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 600 | fixed chunk 0.5 s | 0.1 m | 2.3068380873350602E-07 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 600 | fixed chunk 0.5 s | 0.1 m/s | 5.5948176839286846E-08 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 600 | fixed chunk 0.5 s | 0.1 m | 1.2399884443078935E-06 | 1676 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 600 | fixed chunk 0.5 s | 0.1 m/s | 4.285099338535653E-08 | 1676 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 600 | fixed chunk 0.5 s | 0.1 m | 9.75279544566922E-08 | 6716 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 600 | fixed chunk 0.5 s | 0.1 m/s | 9.14509493774382E-09 | 6716 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 600 | fixed chunk 0.5 s | 0.1 m | 9.273266131093852E-08 | 41232.644652843606 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 600 | fixed chunk 0.5 s | 0.1 m/s | 6.181343975552515E-09 | 41232.644652843606 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 600 | fixed chunk 0.5 s | 0.1 m | 4.4332470165023276E-05 | 2368 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 600 | fixed chunk 0.5 s | 0.1 m/s | 8.302037394663808E-06 | 2368 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 600 | fixed chunk 0.5 s | 0.1 m | 0.0021387621537364765 | 1408 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 600 | fixed chunk 0.5 s | 0.1 m/s | 0.00042235730998642736 | 1408 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 600 | fixed chunk 0.5 s | 0.1 m | 0.001264922516916845 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 600 | fixed chunk 0.5 s | 0.1 m/s | 0.000255370532395129 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 600 | fixed chunk 0.5 s | 0.1 m | 0.0021387689248336243 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 600 | fixed chunk 0.5 s | 0.1 m/s | 0.00042234920694878307 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 600 | fixed chunk 0.5 s | 0.1 m | 0.002138762154579196 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 600 | fixed chunk 0.5 s | 0.1 m/s | 0.000422357309855902 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 600 | fixed chunk 0.5 s | 0.1 m | 4.434598999580991E-05 | 832 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 600 | fixed chunk 0.5 s | 0.1 m/s | 8.302036747475323E-06 | 832 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 600 | fixed chunk 0.5 s | 0.1 m | 7.710198075781035E-05 | 3781 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 600 | fixed chunk 0.5 s | 0.1 m/s | 1.5319314608821847E-05 | 3781 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 600 | fixed chunk 0.5 s | 0.1 m | 6.392216717582249E-06 | 34060.77879184555 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 600 | fixed chunk 0.5 s | 0.1 m/s | 1.2497389021998322E-06 | 34060.77879184555 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0 | fixed chunk 0.5 s | 0.1 m |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0 | fixed chunk 0.5 s | 0.1 m/s |  |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 600 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m |  |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 86400 | fixed chunk 0.5 s | 0.1 m/s |  |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853 [production] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 7097.87163385693 | 16583 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5 [production] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 2908.2059544103927 | 34634 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RK4 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 4525.2388752218 | 29184 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-fixed | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 4976.676862088839 | 15728 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 7500.329276655034 | 9866 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 205.54167172628794 | 6210 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | PEFRL | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 5203.910313514408 | 23360 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 2655.675509001935 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6C | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 2655.7143358984135 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-Y6 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.13247623798954639 | 5835.769021676357 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Leapfrog | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 102606.00600686448 | 374128 | no_hit |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.0037494171182868865 | 6624 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.005840227464846627 | 14382 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | SRKN14A | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 4383.9060236189 | 5152 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-BM14A | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.02086481623638425 | 11348.647903592191 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | GJ8 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.0010837253243179137 | 51820 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Encke-IAS15 | 86400 | 86 400 s (regime sensitivity, not production) | 10 km | 0.007323103064758805 | 128261.21066492127 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 600 | 86 400 s (regime sensitivity, not production) | 10 km | 0.02308972304801801 | 315728 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km |  |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive [cap=600 s] | 600 | 86 400 s (regime sensitivity, not production) | 10 km | 0.03305431356059966 | 631088 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km |  |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) [cap=600 s] | 600 | 86 400 s (regime sensitivity, not production) | 10 km | 0.03410089988254285 | 893888 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km |  |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 [cap=600 s] | 600 | 86 400 s (regime sensitivity, not production) | 10 km | 0.01152516516171045 | 954517 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, not production) | 10 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853 [production] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5 [production] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RK4 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-fixed | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | PEFRL | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Y6 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Y6C | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | WH-Y6 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Leapfrog | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | SRKN14A | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | WH-BM14A | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | GJ8 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Encke-IAS15 | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive [cap=600 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive [cap=86400 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive [cap=600 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive [cap=86400 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) [cap=600 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) [cap=86400 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 [cap=600 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 [cap=86400 s] | 0 | 600 s (production) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853 [production] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.07844181345529094 | 135568 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5 [production] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 1.3466194535630707 | 80608 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RK4 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 2529.8815982979922 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-fixed | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 1.2304006482949918 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 1.2304006482949918 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.10139908849059144 | 47632 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | PEFRL | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 426.0479030655795 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 4.153979626173955 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6C | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 4.187455584518255 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-Y6 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 4.177633195238588 | 57757.32207410219 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Leapfrog | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 71558.78839804594 | 87616 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.016025215859305768 | 65952 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.06981393598321498 | 81784 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | SRKN14A | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.21913878980673934 | 51296 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-BM14A | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.34253296778772113 | 112250.26158736183 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | GJ8 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.06981393598321498 | 92776 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Encke-IAS15 | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.041187154768450605 | 813975.000569287 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 600 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.4237824914796237 | 3157264 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive [cap=600 s] | 600 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.40038459383678754 | 6310864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) [cap=600 s] | 600 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.3529590472035639 | 8938864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km |  |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 [cap=600 s] | 600 | 86 400 s (regime sensitivity, forced Sun) | 100 km | 0.06591489042935661 | 7887318 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 [cap=86400 s] | 86400 | 86 400 s (regime sensitivity, forced Sun) | 100 km |  |  | not run |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOP853 [production] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.05618630087409604 | 135568 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5 [production] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.008963652222909436 | 80608 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | RK4 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.009265354423751102 | 14656 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5-fixed | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.009173971791091345 | 25648 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.009173971791091345 | 25648 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOP853-adaptive | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.00920316251220482 | 47632 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | PEFRL | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.017387517880422892 | 14656 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Y6 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.04787460083986286 | 29312 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Y6C | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.01004542998606622 | 29312 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | WH-Y6 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.03514013981506516 | 57975.752723216065 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Leapfrog | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 28277.044334093214 | 7328 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | RKN12(10) | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.005720397712075029 | 65952 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | IAS15 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.00939227737436238 | 80496 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | SRKN14A | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.02073363449283708 | 51296 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | WH-BM14A | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.041021437469753806 | 112718.32726403442 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | GJ8 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.00939227737436238 | 91488 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Encke-IAS15 | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.014804265566486013 | 775920.2982429579 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 600 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.25075096351552456 | 3157264 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km |  |  | not run |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOP853-adaptive [cap=600 s] | 600 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.25075096351547266 | 6310864 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOP853-adaptive [cap=86400 s] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km |  |  | not run |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | RKN12(10) [cap=600 s] | 600 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.5676269531260423 | 8938864 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | RKN12(10) [cap=86400 s] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km |  |  | not run |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | IAS15 [cap=600 s] | 600 | 86 400 s (regime max step, not the current production 600 s) | 100 km | 0.00939227737452787 | 7884007 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | IAS15 [cap=86400 s] | 86400 | 86 400 s (regime max step, not the current production 600 s) | 100 km |  |  | not run |

## Dominance audit

**Verdict (all methods):** all-method: RK4 wins 12/18 raw cost_total; target/chunk-dependent (target changes 4, chunk groups 1); cost_rhs changes 0, wall-clock changes 1, cost_if_ported changes 0. Raw stand cost is not the production comparison; see cost_if_ported.
**Production-only verdict:** production: DOPRI5 [production] wins 15/18 at 1x cost_total; chunk-bound (600 s production cap; target changes 2, chunk groups 1); cost_rhs changes 0, wall-clock changes 0

`cell_winner`, `dominance_count`, `target_sweep_executed` and `chunk_sweep` use raw
`cost_total`; `cost_if_ported` is reported as a separate `cost_metric` row.
The decision table above uses `cost_if_ported` for any stand/production comparison.

| check | scenario | target | winner | value | note |
|---|---|---|---|---:|---|
| cell_winner | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m | RK4 | 4808 | status ok; err 6.702E-006 |
| cell_winner | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m/s | RK4 | 4808 | status ok; err 1.773E-007 |
| cell_winner | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.1 m | RK4 | 2432 | status ok; err 1.701E-007 |
| cell_winner | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.1 m/s | RK4 | 2432 | status ok; err 5.362E-008 |
| cell_winner | Surface hover 0.5 km, 0 m/s relative | 0.1 m | RK4 | 4800 | status ok; err 6.473E-006 |
| cell_winner | Surface hover 0.5 km, 0 m/s relative | 0.1 m/s | RK4 | 4800 | status ok; err 3.654E-009 |
| cell_winner | Surface level flight 0.5 km, 100 m/s horizontal | 0.1 m | RK4 | 4800 | status ok; err 7.365E-006 |
| cell_winner | Surface level flight 0.5 km, 100 m/s horizontal | 0.1 m/s | RK4 | 4800 | status ok; err 3.934E-008 |
| cell_winner | Surface level flight 1 km, 100 m/s horizontal | 0.1 m | RK4 | 4800 | status ok; err 7.832E-006 |
| cell_winner | Surface level flight 1 km, 100 m/s horizontal | 0.1 m/s | RK4 | 4800 | status ok; err 2.328E-008 |
| cell_winner | LEO 200 km, 1 d | 10 m | DOPRI5-fixed | 20659 | status ok; err 7.804E+000 |
| cell_winner | LEO 300 km, 30 d | 100 m | DOP853-adaptive | 223045 | status ok; err 2.591E+001 |
| cell_winner | High orbit 35 786 km (GEO), 30 d | 100 m | PEFRL | 34560 | status ok; err 3.111E+001 |
| cell_winner | High orbit 200 000 km, 30 d | 100 m | RK4 | 17280 | status ok; err 2.281E-002 |
| cell_winner | Cislunar Moon flyby, SOI entry-exit, 10 d | 100 m | DOPRI5-adaptive | 10992 | status ok; err 1.177E+001 |
| cell_winner | Heliocentric 0.3 AU e 0.5, 1 y | 10 km | RK4 | 210240 | status ok; err 1.221E+000 |
| cell_winner | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | 100 km | Leapfrog | 1051200 | status ok; err 1.825E+003 |
| cell_winner | Far flight 30 AU e 0.5, 10 y | 100 km | Leapfrog | 529264 | status ok; err 1.337E+000 |
| dominance_count | all production cells | - | RK4 | 0.6666666666666666 | 12 / 18 cells |
| cost_metric | all production cells | - | RK4 | 0 | 0 / 18 cells where the cost_rhs winner differs from cost_total |
| cost_metric | all production cells | - | changes | 1 | 1 / 18 cells where the wall-clock winner differs from cost_total |
| cost_metric | all production cells | - | RK4 | 0 | 0 / 18 cells where the cost_if_ported winner differs from cost_total |
| chunk_sweep | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m | RK4 | 4808 | constant chunk 0.05 s; top methods only |
| chunk_sweep | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m | RK4 | 4800 | constant chunk 0.5 s; top methods only |
| chunk_sweep | LEO 200 km, 1 d | 10 m | RK4 | 6912080 | constant chunk 0.05 s; top methods only |
| chunk_sweep | LEO 200 km, 1 d | 10 m | RK4 | 691200 | constant chunk 0.5 s; top methods only |
| chunk_sweep | LEO 200 km, 1 d | 10 m | DOPRI5-fixed | 18733 | constant chunk 60 s; top methods only |
| chunk_sweep | LEO 200 km, 1 d | 10 m | DOPRI5-fixed | 12260 | constant chunk 600 s; top methods only |
| chunk_sweep | Far flight 30 AU e 0.5, 10 y | 100 km | Leapfrog | 1051200 | constant chunk 600 s; top methods only |
| chunk_sweep | Far flight 30 AU e 0.5, 10 y | 100 km | Leapfrog | 529264 | constant chunk 86400 s; top methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.01 m | DOPRI5 [production] | 26422 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m | DOPRI5 [production] | 26422 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 1 m | DOPRI5 [production] | 26422 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.01 m/s | DOPRI5 [production] | 26422 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 0.1 m/s | DOPRI5 [production] | 26422 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 | 1 m/s | DOPRI5 [production] | 26422 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.01 m | DOPRI5 [production] | 13376 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.1 m | DOPRI5 [production] | 13376 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 1 m | DOPRI5 [production] | 13376 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.01 m/s | DOPRI5 [production] | 13376 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 0.1 m/s | DOPRI5 [production] | 13376 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal | 1 m/s | DOPRI5 [production] | 13376 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface hover 0.5 km, 0 m/s relative | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 0.5 km, 100 m/s horizontal | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Surface level flight 1 km, 100 m/s horizontal | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 200 km, 1 d | 1 m | DOP853 [production] | 59775 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 200 km, 1 d | 10 m | DOP853 [production] | 59775 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 200 km, 1 d | 100 m | DOP853 [production] | 59775 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 300 km, 30 d | 10 m | DOP853 [production] | 956031 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 300 km, 30 d | 100 m | DOP853 [production] | 956031 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | LEO 300 km, 30 d | 1 km | DOP853 [production] | 734940 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 35 786 km (GEO), 30 d | 10 m | DOP853 [production] | 159840 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 35 786 km (GEO), 30 d | 100 m | DOP853 [production] | 159840 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 35 786 km (GEO), 30 d | 1 km | DOPRI5 [production] | 95040 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 200 000 km, 30 d | 10 m | DOPRI5 [production] | 95040 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 200 000 km, 30 d | 100 m | DOPRI5 [production] | 95040 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | High orbit 200 000 km, 30 d | 1 km | DOPRI5 [production] | 95040 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Cislunar Moon flyby, SOI entry-exit, 10 d | 10 m | DOP853 [production] | 53280 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Cislunar Moon flyby, SOI entry-exit, 10 d | 100 m | DOPRI5 [production] | 33408 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Cislunar Moon flyby, SOI entry-exit, 10 d | 1 km | DOPRI5 [production] | 33408 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 0.3 AU e 0.5, 1 y | 1 km | DOPRI5 [production] | 1156320 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 0.3 AU e 0.5, 1 y | 10 km | DOPRI5 [production] | 1156320 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 0.3 AU e 0.5, 1 y | 100 km | DOPRI5 [production] | 1156320 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | 10 km | DOPRI5 [production] | 11563200 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | 100 km | DOPRI5 [production] | 11563200 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Heliocentric 5 AU e 0.3, 10 y [forced Sun] | 1000 km | DOPRI5 [production] | 11563200 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Far flight 30 AU e 0.5, 10 y | 10 km | DOPRI5 [production] | 9475456 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Far flight 30 AU e 0.5, 10 y | 100 km | DOPRI5 [production] | 9475456 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | Far flight 30 AU e 0.5, 10 y | 1000 km | DOPRI5 [production] | 9475456 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_chunk_sweep | LEO 200 km, 1 d | 10 m | DOPRI5 [production] | 38016418 | constant chunk 0.05 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | LEO 200 km, 1 d | 10 m | DOPRI5 [production] | 3801600 | constant chunk 0.5 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | LEO 200 km, 1 d | 10 m | DOP853 [production] | 53391 | constant chunk 60 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | LEO 200 km, 1 d | 10 m | DOP853 [production] | 15412 | constant chunk 600 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | Far flight 30 AU e 0.5, 10 y | 100 km | DOPRI5 [production] | 11563200 | constant chunk 600 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | Far flight 30 AU e 0.5, 10 y | 100 km | DOPRI5 [production] | 9475456 | constant chunk 86400 s; production DOP853/DOPRI5 only |
| production_dominance_count | production cells | - | DOPRI5 [production] | 0.8333333333333334 | 15 / 18 production cells |
| production_cost_metric_rhs | production cells | - | DOPRI5 [production] | 0 | 0 / 18 production cells where the cost_rhs winner differs from cost_total |
| production_cost_metric_ms | production cells | - | DOPRI5 [production] | 0 | 0 / 18 production cells where the wall-clock winner differs from cost_total |
| production_cost_metric_target | production cells | - | changes | 2 | 2 target cells where the production winner changes; see production_target_sweep_executed |
| production_verdict | production cells | - | DOPRI5 [production] | 0.8333333333333334 | production: DOPRI5 [production] wins 15/18 at 1x cost_total; chunk-bound (600 s production cap; target changes 2, chunk groups 1); cost_rhs changes 0, wall-clock changes 0 |

The first Test560 ranked by `cost_rhs` with LEO 60 s and surface ascent 0.5 s,
a 5-method subset, soft 1x targets and raw stand cost. DOPRI5's 6 stages beat
DOP853's 12 when the chunk allowed one step and neither the local-frame nor the
port overhead was in the ranking. This table separates `cost_total` (raw stand),
`cost_if_ported` (comparable with production) and `best_production`, and repeats
the winner count under cost_rhs/target/chunk sweeps.

## Heliocentric 5 AU / 10 y isolation

| variant | method | dominant | frame | max step | err max m | cost total | status |
|---|---|---|---|---:|---:|---:|---|
| auto | DOP853 [production] | auto | local | 600 | 199051987.080305 | 19447200 | ok |
| forced-sun | DOP853 [production] | Sun | local | 600 | 1.2808757511069577 | 19447200 | ok |
| absolute | DOP853 [production] | auto | absolute | 600 | 1.2851476295813995 | 19447200 | ok |
| forced-sun-86400 | DOP853 [production] | Sun | local | 86400 | 0.07844181345529094 | 135568 | ok |
| stand-600 | DOP853-adaptive | Sun | local | 600 | 0.40038459383678754 | 6832800 | ok |
| stand-86400 | DOP853-adaptive | Sun | local | 86400 | 0.10139908849059144 | 47632 | ok |

The `auto` row is the current production path (dominant-body switch). The forced-Sun
and absolute rows isolate it. If `auto` is orders of magnitude worse while forced/absolute
agree with the Sun-centred reference, the scenario is a production frame-switch finding,
not an integrator result; the decision table uses the forced-Sun comparison and keeps
the auto path visible.

## Validity / events / warp

- `Test560b_Validity.csv`: production default, parity, determinism,
  chunk invariance. FAIL/finding rows are listed there.
- `Test560b_Events.csv`: touchdown/SOI event time.
- `Test560b_Warp.csv`: real 60-frame warp runs, cost per real second.

## Failed / unresolved / n/a

- Cislunar Moon flyby, SOI entry-exit, 10 d / 100 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- Far flight 30 AU e 0.5, 10 y / 100 km: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- Far flight 30 AU e 0.5, 10 y [regime86400] / 100 km: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]]
- Heliocentric 0.3 AU e 0.5, 1 y / 10 km: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- Heliocentric 0.3 AU e 0.5, 1 y [regime86400] / 10 km: unresolved [Leapfrog:no_hit], n/a [-], not run [DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]]
- Heliocentric 5 AU e 0.3, 10 y [forced Sun] / 100 km: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun] / 100 km: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=86400 s], DOP853-adaptive [cap=86400 s], RKN12(10) [cap=86400 s], IAS15 [cap=86400 s]]
- High orbit 200 000 km, 30 d / 100 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- High orbit 35 786 km (GEO), 30 d / 100 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], IAS15 [cap=600 s]]
- LEO 200 km, 1 d / 10 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- LEO 300 km, 30 d / 100 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 / 0.1 m/s: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 / 0.1 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal / 0.1 m/s: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal / 0.1 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface hover 0.5 km, 0 m/s relative / 0.1 m/s: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface hover 0.5 km, 0 m/s relative / 0.1 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface level flight 0.5 km, 100 m/s horizontal / 0.1 m/s: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface level flight 0.5 km, 100 m/s horizontal / 0.1 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface level flight 1 km, 100 m/s horizontal / 0.1 m: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]
- Surface level flight 1 km, 100 m/s horizontal / 0.1 m/s: unresolved [-], n/a [-], not run [DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s], IAS15 [cap=600 s]]

## Production-available vs stand-only

- Production-available: DOP853, DOPRI5. The default is still DOP853.
- Stand-only (requires port): every other StudyRegistry method and its cap variants.
  `WH-Y6`, `WH-BM14A` and `Encke-IAS15` require a central body; `PEFRL/Y6/Y6C/
  Leapfrog/RKN12/SRKN14A/GJ8` require a conservative second-order force and are
  reported n/a on drag/thrust; RK4/DOPRI5-fixed/DOPRI5/DOP853/IAS15/Encke are
  applicable where their Supports() says so.
- A stand-only winner is a port candidate, not a production recommendation:
  `cost_if_ported` is an estimate from the measured production local-frame
  convention, and the port itself was not run.
- When `best_all` is a mixed tie (`tie: production+stand-only`), the production
  method is already inside the 20% tie band; the production option needs no port,
  and the tie is not called a stand-only port candidate.

