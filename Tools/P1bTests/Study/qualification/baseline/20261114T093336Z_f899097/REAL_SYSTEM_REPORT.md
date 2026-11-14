# REAL_SYSTEM_REPORT: integrators on the real system (Appendix B, tier H)

Generated from `matrix.csv`, `nasa_validation.csv` and `real_system.csv` of this run;
no hand edits. The ranking uses only the certified reference of the SAME model
(TASK.md B1). The Horizons residual is a reference-only column.

**Production-интегратор игры в этот отчёт не входит.**

## E_model from layer V (`nasa_validation.csv`)

| task | window | model | E_model rms8_end (km) | E_model Moon 10 y (km) |
|---|---|---|---:|---:|
| R2 | 2026-2036 | 1PN | 7.442 | 177.152 |
| H1 | 2026-2036 | Newton | 739.027 | 178.055 |
| H1h | 2016-2026 | Newton | 1106.895 | 161.308 |
| H2h | 2016-2026 | 1PN | 2.657 | 160.510 |
| H3 | 2026-2036 | 409small+J2+EJ2 | 1.133 | 68.077 |
| H6 | n/a | n/a | n/a | n/a |

## Goals by task (M and Q)

| task | goal | winner | price | second | margin | excluded N/A | excluded FAIL |
|---|---|---|---:|---|---:|---|---|
| R2 | A1 | DOP853-adaptive | 2.644E+04 | IAS15 | 1.36× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| R2 | A2 | IAS15 | 4.819E+04 | DOP853-adaptive | 1.10× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| R2 | A3 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| R2 | M100 | IAS15 | 4.819E+04 | DOP853-adaptive | 1.18× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| R2 | M10 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| R2 | M1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| R2 | Q1 | IAS15 | 4.523E+04 | DOP853-adaptive | 1.02× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| R2 | Q10 | DOP853-adaptive | 3.549E+04 | IAS15 | 1.07× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H1 | A1 | RKN12(10) | 1.162E+04 | SRKN14A | 1.83× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| H1 | A2 | RKN12(10) | 1.985E+04 | IAS15 | 2.43× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| H1 | A3 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1 | M100 | RKN12(10) | 1.985E+04 | IAS15 | 2.43× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| H1 | M10 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1 | M1 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1 | Q1 | RKN12(10) | 1.662E+04 | IAS15 | 2.73× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| H1 | Q10 | RKN12(10) | 1.245E+04 | SRKN14A | 2.56× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00); GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| H1h | A1 | GJ8 | 1.158E+04 | RKN12(10) | 1.03× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00) |
| H1h | A2 | RKN12(10) | 2.012E+04 | GJ8 | 1.27× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); Leapfrog (level not reached at the finest control) |
| H1h | A3 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1h | M100 | RKN12(10) | 2.123E+04 | GJ8 | 1.21× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); Leapfrog (level not reached at the finest control) |
| H1h | M10 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1h | M1 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| H1h | Q1 | RKN12(10) | 1.675E+04 | GJ8 | 1.03× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); Leapfrog (level not reached at the finest control) |
| H1h | Q10 | GJ8 | 1.158E+04 | RKN12(10) | 1.33× | WH-Y6, WH-BM14A, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00) |
| H2h | A1 | DOP853-adaptive | 2.798E+04 | IAS15 | 1.28× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H2h | A2 | IAS15 | 4.813E+04 | DOP853-adaptive | 1.10× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H2h | A3 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H2h | M100 | IAS15 | 4.813E+04 | DOP853-adaptive | 1.18× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H2h | M10 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H2h | M1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H2h | Q1 | IAS15 | 4.526E+04 | DOP853-adaptive | 1.02× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H2h | Q10 | DOP853-adaptive | 3.554E+04 | IAS15 | 1.18× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.92 vs expected 4.00); DOPRI5-adaptive (level not reached at the finest control) |
| H3 | A1 | DOP853-adaptive | 2.753E+04 | IAS15 | 1.31× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00) |
| H3 | A2 | IAS15 | 4.709E+04 | DOP853-adaptive | 1.19× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00) |
| H3 | A3 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H3 | M100 | IAS15 | 4.709E+04 | DOP853-adaptive | 1.19× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00) |
| H3 | M10 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H3 | M1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H3 | Q1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H3 | Q10 | DOP853-adaptive | 3.715E+04 | IAS15 | 1.19× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00) |
| H6 | A1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | DOPRI5-adaptive (G5 finiteness: aborted at t=5.253E+007 s: scaled error stayed above 1 at the smallest step; step floor 3.154E-006 s reached at t=5.253E+007 s (last scaled error 5.173E+000)) |
| H6 | A2 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H6 | A3 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H6 | M100 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H6 | M10 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| H6 | M1 | — | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |

## Integrator against the model (same-model error at the certifying control)

| task | goal | winner | model error (km) | method error (km) | ratio |
|---|---|---|---:|---:|---:|
| R2 | A1 | DOP853-adaptive | 7.442 | 144.878 | 5.14E-002× |
| R2 | A2 | IAS15 | 7.442 | 0.020 | 3.64E+002× |
| R2 | A3 | — | 7.442 | n/a | n/a |
| R2 | M100 | IAS15 | 7.442 | 0.020 | 3.64E+002× |
| R2 | M10 | — | 7.442 | n/a | n/a |
| R2 | M1 | — | 7.442 | n/a | n/a |
| R2 | Q1 | IAS15 | 7.442 | 0.295 | 2.53E+001× |
| R2 | Q10 | DOP853-adaptive | 7.442 | 6.507 | 1.14E+000× |
| H1 | A1 | RKN12(10) | 739.027 | 138.906 | 5.32E+000× |
| H1 | A2 | RKN12(10) | 739.027 | 0.047 | 1.58E+004× |
| H1 | A3 | — | 739.027 | n/a | n/a |
| H1 | M100 | RKN12(10) | 739.027 | 0.047 | 1.58E+004× |
| H1 | M10 | — | 739.027 | n/a | n/a |
| H1 | M1 | — | 739.027 | n/a | n/a |
| H1 | Q1 | RKN12(10) | 739.027 | 1.180 | 6.26E+002× |
| H1 | Q10 | RKN12(10) | 739.027 | 4.133 | 1.79E+002× |
| H1h | A1 | GJ8 | 1106.895 | 3.662 | 3.02E+002× |
| H1h | A2 | RKN12(10) | 1106.895 | 0.135 | 8.19E+003× |
| H1h | A3 | — | 1106.895 | n/a | n/a |
| H1h | M100 | RKN12(10) | 1106.895 | 0.048 | 2.30E+004× |
| H1h | M10 | — | 1106.895 | n/a | n/a |
| H1h | M1 | — | 1106.895 | n/a | n/a |
| H1h | Q1 | RKN12(10) | 1106.895 | 0.554 | 2.00E+003× |
| H1h | Q10 | GJ8 | 1106.895 | 3.662 | 3.02E+002× |
| H2h | A1 | DOP853-adaptive | 2.657 | 83.201 | 3.19E-002× |
| H2h | A2 | IAS15 | 2.657 | 0.027 | 9.67E+001× |
| H2h | A3 | — | 2.657 | n/a | n/a |
| H2h | M100 | IAS15 | 2.657 | 0.027 | 9.67E+001× |
| H2h | M10 | — | 2.657 | n/a | n/a |
| H2h | M1 | — | 2.657 | n/a | n/a |
| H2h | Q1 | IAS15 | 2.657 | 0.266 | 9.99E+000× |
| H2h | Q10 | DOP853-adaptive | 2.657 | 6.818 | 3.90E-001× |
| H3 | A1 | DOP853-adaptive | 1.133 | 91.672 | 1.24E-002× |
| H3 | A2 | IAS15 | 1.133 | 0.061 | 1.85E+001× |
| H3 | A3 | — | 1.133 | n/a | n/a |
| H3 | M100 | IAS15 | 1.133 | 0.061 | 1.85E+001× |
| H3 | M10 | — | 1.133 | n/a | n/a |
| H3 | M1 | — | 1.133 | n/a | n/a |
| H3 | Q1 | — | 1.133 | n/a | n/a |
| H3 | Q10 | DOP853-adaptive | 1.133 | 4.082 | 2.78E-001× |
| H6 | A1 | — | n/a | n/a | n/a |
| H6 | A2 | — | n/a | n/a | n/a |
| H6 | A3 | — | n/a | n/a | n/a |
| H6 | M100 | — | n/a | n/a | n/a |
| H6 | M10 | — | n/a | n/a | n/a |
| H6 | M1 | — | n/a | n/a | n/a |

## Reference column vs Horizons — mixes model and integrator error, never ranks

| task | goal | winner | err_vs_horizons_rms8 (km) |
|---|---|---|---:|
| R2 | A1 | DOP853-adaptive | 8.142 |
| R2 | A2 | IAS15 | 7.442 |
| R2 | A3 | — | n/a |
| R2 | M100 | IAS15 | 7.442 |
| R2 | M10 | — | n/a |
| R2 | M1 | — | n/a |
| R2 | Q1 | IAS15 | 7.442 |
| R2 | Q10 | DOP853-adaptive | 7.439 |
| H1 | A1 | RKN12(10) | 740.347 |
| H1 | A2 | RKN12(10) | 739.027 |
| H1 | A3 | — | n/a |
| H1 | M100 | RKN12(10) | 739.027 |
| H1 | M10 | — | n/a |
| H1 | M1 | — | n/a |
| H1 | Q1 | RKN12(10) | 739.043 |
| H1 | Q10 | RKN12(10) | 739.495 |
| H1h | A1 | GJ8 | 1107.634 |
| H1h | A2 | RKN12(10) | 1106.895 |
| H1h | A3 | — | n/a |
| H1h | M100 | RKN12(10) | 1106.895 |
| H1h | M10 | — | n/a |
| H1h | M1 | — | n/a |
| H1h | Q1 | RKN12(10) | 1106.905 |
| H1h | Q10 | GJ8 | 1107.634 |
| H2h | A1 | DOP853-adaptive | 2.710 |
| H2h | A2 | IAS15 | 2.657 |
| H2h | A3 | — | n/a |
| H2h | M100 | IAS15 | 2.657 |
| H2h | M10 | — | n/a |
| H2h | M1 | — | n/a |
| H2h | Q1 | IAS15 | 2.657 |
| H2h | Q10 | DOP853-adaptive | 2.634 |
| H3 | A1 | DOP853-adaptive | 2.706 |
| H3 | A2 | IAS15 | 1.133 |
| H3 | A3 | — | n/a |
| H3 | M100 | IAS15 | 1.133 |
| H3 | M10 | — | n/a |
| H3 | M1 | — | n/a |
| H3 | Q1 | — | n/a |
| H3 | Q10 | DOP853-adaptive | 1.163 |
| H6 | A1 | — | n/a |
| H6 | A2 | — | n/a |
| H6 | A3 | — | n/a |
| H6 | M100 | — | n/a |
| H6 | M10 | — | n/a |
| H6 | M1 | — | n/a |

## Class summary `real-system` (TASK.md §11)

A class of fewer than two tasks is reported as a fact about one task, not as a
class conclusion (TASK.md §11, rule 2).

| goal | tasks | winner | second | margin | excluded N/A | excluded FAIL |
|---|---|---|---|---:|---|---|
| A1 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00), DOPRI5-adaptive (level not reached at the finest control), GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| A2 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00), DOPRI5-adaptive (level not reached at the finest control), Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| A3 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| M100 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00), DOPRI5-adaptive (level not reached at the finest control), Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| M10 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| M1 | R2, H1, H1h, H2h, H3, H6 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | none |
| Q1 | R2, H1, H1h, H2h, H3 | — | — | — | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00), DOPRI5-adaptive (level not reached at the finest control), Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 8.63 vs expected 10.00) |
| Q10 | R2, H1, H1h, H2h, H3 | DOP853-adaptive | IAS15 | 14.0% | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15 | RK4 (G2 order: observed 4.94 vs expected 4.00), DOPRI5-adaptive (level not reached at the finest control), GJ8 (G2 order: observed 8.63 vs expected 10.00) |
