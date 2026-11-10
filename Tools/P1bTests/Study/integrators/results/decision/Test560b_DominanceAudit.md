# Test560b - dominance audit

**Verdict (all methods):** all-method: RK4 wins 12/18 raw cost_total; target/chunk-dependent (target changes 4, chunk groups 1); cost_rhs changes 0, wall-clock changes 1, cost_if_ported changes 0. Raw stand cost is not the production comparison; see cost_if_ported.

**Production-only verdict:** production: DOPRI5 [production] wins 15/18 at 1x cost_total; chunk-bound (600 s production cap; target changes 2, chunk groups 1); cost_rhs changes 0, wall-clock changes 0

`cell_winner` and `dominance_count` use raw `cost_total`; `cost_if_ported`
is a separate `cost_metric` row. The decision table uses `cost_if_ported`
for any stand/production comparison.

| check | scenario | target | winner | value | note |
|---|---|---|---|---:|---|
| cell_winner | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | RK4 | 4808 | status ok; err 6.702E-006 |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.01 m | RK4 | 4808 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | RK4 | 4808 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 1 m | RK4 | 4808 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m/s | RK4 | 4808 | status ok; err 1.773E-007 |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.01 m/s | RK4 | 4808 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m/s | RK4 | 4808 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 1 m/s | RK4 | 4808 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m | RK4 | 2432 | status ok; err 1.701E-007 |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.01 m | RK4 | 2432 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m | RK4 | 2432 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 1 m | RK4 | 2432 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m/s | RK4 | 2432 | status ok; err 5.362E-008 |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.01 m/s | RK4 | 2432 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m/s | RK4 | 2432 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 1 m/s | RK4 | 2432 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m | RK4 | 4800 | status ok; err 6.473E-006 |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.01 m | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 1 m | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m/s | RK4 | 4800 | status ok; err 3.654E-009 |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.01 m/s | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m/s | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 1 m/s | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m | RK4 | 4800 | status ok; err 7.365E-006 |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.01 m | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 1 m | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m/s | RK4 | 4800 | status ok; err 3.934E-008 |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.01 m/s | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m/s | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 1 m/s | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m | RK4 | 4800 | status ok; err 7.832E-006 |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.01 m | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 1 m | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m/s | RK4 | 4800 | status ok; err 2.328E-008 |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.01 m/s | RK4 | 4800 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m/s | RK4 | 4800 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 1 m/s | RK4 | 4800 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "LEO 200 km, 1 d" | 10 m | DOPRI5-fixed | 20659 | status ok; err 7.804E+000 |
| target_sweep_executed | "LEO 200 km, 1 d" | 1 m | DOP853-adaptive | 20797 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "LEO 200 km, 1 d" | 10 m | DOPRI5-fixed | 20659 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "LEO 200 km, 1 d" | 100 m | DOPRI5-adaptive | 18913 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "LEO 300 km, 30 d" | 100 m | DOP853-adaptive | 223045 | status ok; err 2.591E+001 |
| target_sweep_executed | "LEO 300 km, 30 d" | 10 m | IAS15 | 290344 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "LEO 300 km, 30 d" | 100 m | DOP853-adaptive | 223045 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "LEO 300 km, 30 d" | 1 km | DOP853-adaptive | 222973 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "High orbit 35 786 km (GEO), 30 d" | 100 m | PEFRL | 34560 | status ok; err 3.111E+001 |
| target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 10 m | Y6 | 34560 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 100 m | PEFRL | 34560 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 1 km | PEFRL | 17280 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "High orbit 200 000 km, 30 d" | 100 m | RK4 | 17280 | status ok; err 2.281E-002 |
| target_sweep_executed | "High orbit 200 000 km, 30 d" | 10 m | RK4 | 17280 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "High orbit 200 000 km, 30 d" | 100 m | RK4 | 17280 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "High orbit 200 000 km, 30 d" | 1 km | RK4 | 17280 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 100 m | DOPRI5-adaptive | 10992 | status ok; err 1.177E+001 |
| target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 10 m | DOPRI5-adaptive | 11766 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 100 m | DOPRI5-adaptive | 10992 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 1 km | DOPRI5-adaptive | 10548 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Heliocentric 0.3 AU e 0.5, 1 y" | 10 km | RK4 | 210240 | status ok; err 1.221E+000 |
| target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 1 km | RK4 | 210240 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 10 km | RK4 | 210240 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 100 km | RK4 | 210240 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 100 km | Leapfrog | 1051200 | status ok; err 1.825E+003 |
| target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 10 km | Leapfrog | 1051200 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 100 km | Leapfrog | 1051200 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 1000 km | Leapfrog | 1051200 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| cell_winner | "Far flight 30 AU e 0.5, 10 y" | 100 km | Leapfrog | 529264 | status ok; err 1.337E+000 |
| target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 10 km | Leapfrog | 529264 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 100 km | Leapfrog | 529264 | 1x budget; reuses executed runs; no re-run at 0.1x/10x |
| target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 1000 km | Leapfrog | 529264 | 10x budget; reuses executed runs; no re-run at 0.1x/10x |
| dominance_count | all production cells | - | RK4 | 0.6666666666666666 | 12 / 18 cells |
| cost_metric | all production cells | - | RK4 | 0 | 0 / 18 cells where the cost_rhs winner differs from cost_total |
| cost_metric | all production cells | - | changes | 1 | 1 / 18 cells where the wall-clock winner differs from cost_total |
| cost_metric | all production cells | - | RK4 | 0 | 0 / 18 cells where the cost_if_ported winner differs from cost_total |
| chunk_sweep | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | RK4 | 4808 | constant chunk 0.05 s; top methods only |
| chunk_sweep | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | RK4 | 4800 | constant chunk 0.5 s; top methods only |
| chunk_sweep | "LEO 200 km, 1 d" | 10 m | RK4 | 6912080 | constant chunk 0.05 s; top methods only |
| chunk_sweep | "LEO 200 km, 1 d" | 10 m | RK4 | 691200 | constant chunk 0.5 s; top methods only |
| chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOPRI5-fixed | 18733 | constant chunk 60 s; top methods only |
| chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOPRI5-fixed | 12260 | constant chunk 600 s; top methods only |
| chunk_sweep | "Far flight 30 AU e 0.5, 10 y" | 100 km | Leapfrog | 1051200 | constant chunk 600 s; top methods only |
| chunk_sweep | "Far flight 30 AU e 0.5, 10 y" | 100 km | Leapfrog | 529264 | constant chunk 86400 s; top methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.01 m | DOPRI5 [production] | 26422 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | DOPRI5 [production] | 26422 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 1 m | DOPRI5 [production] | 26422 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.01 m/s | DOPRI5 [production] | 26422 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m/s | DOPRI5 [production] | 26422 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 1 m/s | DOPRI5 [production] | 26422 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.01 m | DOPRI5 [production] | 13376 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | 13376 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 1 m | DOPRI5 [production] | 13376 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.01 m/s | DOPRI5 [production] | 13376 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | 13376 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 1 m/s | DOPRI5 [production] | 13376 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface hover 0.5 km, 0 m/s relative" | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 0.5 km, 100 m/s horizontal" | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.01 m | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 1 m | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.01 m/s | DOPRI5 [production] | 26400 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | 26400 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Surface level flight 1 km, 100 m/s horizontal" | 1 m/s | DOPRI5 [production] | 26400 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 200 km, 1 d" | 1 m | DOP853 [production] | 59775 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 200 km, 1 d" | 10 m | DOP853 [production] | 59775 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 200 km, 1 d" | 100 m | DOP853 [production] | 59775 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 300 km, 30 d" | 10 m | DOP853 [production] | 956031 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 300 km, 30 d" | 100 m | DOP853 [production] | 956031 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "LEO 300 km, 30 d" | 1 km | DOP853 [production] | 734940 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 10 m | DOP853 [production] | 159840 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 100 m | DOP853 [production] | 159840 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 35 786 km (GEO), 30 d" | 1 km | DOPRI5 [production] | 95040 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 200 000 km, 30 d" | 10 m | DOPRI5 [production] | 95040 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 200 000 km, 30 d" | 100 m | DOPRI5 [production] | 95040 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "High orbit 200 000 km, 30 d" | 1 km | DOPRI5 [production] | 95040 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 10 m | DOP853 [production] | 53280 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 100 m | DOPRI5 [production] | 33408 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 1 km | DOPRI5 [production] | 33408 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 1 km | DOPRI5 [production] | 1156320 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 10 km | DOPRI5 [production] | 1156320 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 0.3 AU e 0.5, 1 y" | 100 km | DOPRI5 [production] | 1156320 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 10 km | DOPRI5 [production] | 11563200 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 100 km | DOPRI5 [production] | 11563200 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 1000 km | DOPRI5 [production] | 11563200 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 10 km | DOPRI5 [production] | 9475456 | 0.1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5 [production] | 9475456 | 1x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_target_sweep_executed | "Far flight 30 AU e 0.5, 10 y" | 1000 km | DOPRI5 [production] | 9475456 | 10x budget; reuses executed runs; no re-run at 0.1x/10x; production methods only |
| production_chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOPRI5 [production] | 38016418 | constant chunk 0.05 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOPRI5 [production] | 3801600 | constant chunk 0.5 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOP853 [production] | 53391 | constant chunk 60 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | "LEO 200 km, 1 d" | 10 m | DOP853 [production] | 15412 | constant chunk 600 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5 [production] | 11563200 | constant chunk 600 s; production DOP853/DOPRI5 only |
| production_chunk_sweep | "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5 [production] | 9475456 | constant chunk 86400 s; production DOP853/DOPRI5 only |
| production_dominance_count | production cells | - | DOPRI5 [production] | 0.8333333333333334 | 15 / 18 production cells |
| production_cost_metric_rhs | production cells | - | DOPRI5 [production] | 0 | 0 / 18 production cells where the cost_rhs winner differs from cost_total |
| production_cost_metric_ms | production cells | - | DOPRI5 [production] | 0 | 0 / 18 production cells where the wall-clock winner differs from cost_total |
| production_cost_metric_target | production cells | - | changes | 2 | 2 target cells where the production winner changes; see production_target_sweep_executed |
| production_verdict | production cells | - | DOPRI5 [production] | 0.8333333333333334 | "production: DOPRI5 [production] wins 15/18 at 1x cost_total; chunk-bound (600 s production cap; target changes 2, chunk groups 1); cost_rhs changes 0, wall-clock changes 0" |
