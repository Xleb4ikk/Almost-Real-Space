# Method qualification (Test564)

Generated from `matrix.csv` and `runs.csv` of run `20261111T191712Z_9507b47`.
Profile: **full**; commit: `9507b47b498afd85e0df587500b27ff207298f88`; 16 problems × 15 methods × 3 levels.

Statuses: PASS / FAIL / N/A (from `Supports`) / SKIP (oracle floor or budget) / ERROR.
Gate thresholds are frozen; see `PROGRESS.md` in this folder.

## Summary

| method | family | PASS | FAIL | N/A | SKIP | ERROR | best at |
|---|---|---:|---:|---:|---:|---:|---|
| RK4 | Runge-Kutta | 32 | 4 | 0 | 12 | 0 | — |
| DOPRI5-fixed | Runge-Kutta | 37 | 0 | 0 | 11 | 0 | — |
| DOPRI5-adaptive | Runge-Kutta | 38 | 5 | 0 | 5 | 0 | — |
| DOP853-adaptive | Runge-Kutta | 41 | 2 | 0 | 5 | 0 | escape A1, long-horizon A1, many-body A1, many-body A2, sanity A1, sanity A2, velocity-dependent A1, velocity-dependent A2 |
| PEFRL | Symplectic | 31 | 1 | 6 | 10 | 0 | — |
| Y6 | Symplectic | 32 | 0 | 6 | 10 | 0 | — |
| Y6C | Symplectic | 35 | 0 | 6 | 7 | 0 | — |
| WH-Y6 | Kepler-split | 23 | 0 | 21 | 4 | 0 | — |
| Leapfrog | Symplectic | 10 | 19 | 6 | 13 | 0 | — |
| RKN12(10) | Runge-Kutta | 36 | 2 | 6 | 4 | 0 | central-dominant A1, central-dominant A2, central-dominant A3, close-encounter A1, escape A1, escape A2, escape A3, long-horizon A3, nbody-regular A1, nbody-regular A2, nbody-regular A3, ship A1 |
| IAS15 | Gauss-Radau | 42 | 1 | 0 | 5 | 0 | close-encounter A2, long-horizon A2, many-body A2, sanity A3 |
| SRKN14A | Symplectic | 33 | 0 | 6 | 9 | 0 | — |
| WH-BM14A | Kepler-split | 23 | 0 | 21 | 4 | 0 | — |
| GJ8 | Multistep | 22 | 13 | 6 | 7 | 0 | — |
| Encke-IAS15 | Encke | 23 | 0 | 21 | 4 | 0 | ecc-low A1, ecc-low A2, ecc-low A3, ecc-mid A1, ecc-mid A2, escape A3, kepler A1, kepler A2, kepler A3 |

## Method × problem (A1 / A2 / A3)

| method | K1 | K2 | K3 | K4 | K5 | K6 | K7 | K8 | T1 | T2 | T3 | R1 | R2 | R3 | R4 | L2 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| RK4 | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | F/F/skip | skip/skip/skip | P/P/skip | F/skip/skip |
| DOPRI5-fixed | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | P/P/skip | P/skip/skip | P/P/skip | P/skip/skip |
| DOPRI5-adaptive | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/F/F | P/P/P | F/F/skip | P/skip/skip | P/P/skip | P/P/skip |
| DOP853-adaptive | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/F/F | P/P/P | P/P/skip | P/skip/skip | P/P/skip | P/P/skip |
| PEFRL | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/skip/skip |
| Y6 | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| Y6C | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | skip/skip/skip | P/P/P | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| WH-Y6 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| Leapfrog | P/F/F | P/F/F | P/F/F | F/F/F | P/F/F | P/F/F | P/F/F | n/a/n/a/n/a | P/F/F | P/F/F | skip/skip/skip | P/skip/skip | n/a/n/a/n/a | skip/skip/skip | P/skip/skip | skip/skip/skip |
| RKN12(10) | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | P/F/F | P/P/P | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| IAS15 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/skip | P/skip/skip | P/P/skip | P/P/skip |
| SRKN14A | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| WH-BM14A | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |
| GJ8 | P/P/P | F/F/F | F/F/F | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | F/F/F | skip/skip/skip | P/P/P | n/a/n/a/n/a | P/skip/skip | F/F/skip | F/F/skip |
| Encke-IAS15 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/P/skip | P/P/skip |

Legend: P = PASS, F = FAIL, n/a = not applicable, skip = budget/oracle floor.
**Valid for** (in the per-method sections) lists the tags of problems where the method
has at least one PASS; it is not a claim that every level of those problems passes.
**Gate notes on PASS** shows gates that were skipped by the model-drift guard (G3).
**Observed order** is filled for adaptive methods too (informative only): G2 applies
only to fixed-step methods with a declared ExpectedOrder.

## Per method

### RK4

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, many-body, ship
* **Best at:** —
* **N/A:** —
* **FAIL:** level not reached at the finest control: K4.A3; G2 order: observed 4.94 vs expected 4.00: R2.A1, R2.A2; G2 order: observed 4.90 vs expected 4.00: L2.A1
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, R3.A1, L2.A2; oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 4.30 vs 4.00; K2 4.37 vs 4.00; K3 4.21 vs 4.00; K4 4.17 vs 4.00; K5 4.00 vs 4.00; K6 4.00 vs 4.00; K7 4.00 vs 4.00; K8 3.87 vs 4.00; T2 4.05 vs 4.00; R2 4.94 vs 4.00; R3 4.96 vs 4.00; R4 4.02 vs 4.00; L2 4.90 vs 4.00
* **Typical invariants (all rows):** max |dE|/E = 3.879E+03, max |dL|/L = 1.436E+04, max dCOM/scale = 9.259E-14, worst rev_err = 6.376E+06

### DOPRI5-fixed

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, many-body, 1pn, ship, perturbed
* **Best at:** —
* **N/A:** —
* **FAIL:** —
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, L2.A2; oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 4.87 vs 5.00; K2 4.99 vs 5.00; K3 5.01 vs 5.00; K4 5.02 vs 5.00; K5 5.27 vs 5.00; K6 5.26 vs 5.00; K7 5.00 vs 5.00; K8 4.96 vs 5.00; T2 4.87 vs 5.00; R2 4.88 vs 5.00; R3 4.95 vs 5.00; R4 5.31 vs 5.00; L2 4.93 vs 5.00
* **Typical invariants (all rows):** max |dE|/E = 2.460E+04, max |dL|/L = 7.694E+04, max dCOM/scale = 1.606E-10, worst rev_err = 9.489E+06

### DOPRI5-adaptive

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, nbody-chaotic, many-body, ship, perturbed
* **Best at:** —
* **N/A:** —
* **FAIL:** level not reached at the finest control: T1.A3, T3.A2, T3.A3, R2.A1, R2.A2
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K3 1.02 vs n/a; K4 1.00 vs n/a; K6 1.06 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 2.463E+01, max |dL|/L = 7.847E-01, max dCOM/scale = 2.654E-13, worst rev_err = 6.444E+06

### DOP853-adaptive

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, nbody-chaotic, many-body, 1pn, ship, perturbed
* **Best at:** escape A1, long-horizon A1, many-body A1, many-body A2, sanity A1, sanity A2, velocity-dependent A1, velocity-dependent A2
* **N/A:** —
* **FAIL:** level not reached at the finest control: T3.A2, T3.A3
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 0.99 vs n/a; K5 0.89 vs n/a; K6 0.91 vs n/a; K7 1.00 vs n/a; K8 0.95 vs n/a; R1 1.11 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 1.549E+01, max |dL|/L = 6.382E-01, max dCOM/scale = 1.606E-10, worst rev_err = 6.444E+06

### PEFRL

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: K4.A3
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, L2.A2; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 4.00 vs 4.00; K3 4.00 vs 4.00; K4 4.00 vs 4.00; K5 3.98 vs 4.00; K6 3.99 vs 4.00; K7 3.99 vs 4.00; T2 3.98 vs 4.00; R3 4.00 vs 4.00; R4 3.99 vs 4.00; L2 4.00 vs 4.00
* **Typical invariants (all rows):** max |dE|/E = 1.577E+05, max |dL|/L = 6.613E-01, max dCOM/scale = 2.244E-13, worst rev_err = 6.500E+06

### Y6

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** RHS budget reached before the level: K4.A3, T1.A3, T3.A1, T3.A2, T3.A3, R1.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 5.91 vs 6.00; K2 5.91 vs 6.00; K3 6.00 vs 6.00; K5 5.83 vs 6.00; K6 5.76 vs 6.00; K7 5.98 vs 6.00; T2 5.87 vs 6.00; R3 6.03 vs 6.00; R4 5.95 vs 6.00
* **Typical invariants (all rows):** max |dE|/E = 5.278E+04, max |dL|/L = 7.370E-01, max dCOM/scale = 1.104E-13, worst rev_err = 8.324E+04

### Y6C

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 5.91 vs 6.00; K2 6.00 vs 6.00; K3 6.00 vs 6.00; K4 6.00 vs 6.00; K5 5.83 vs 6.00; K6 5.76 vs 6.00; K7 5.98 vs 6.00; T1 5.99 vs 6.00; T2 5.95 vs 6.00; R3 6.01 vs 6.00; R4 5.95 vs 6.00; L2 6.00 vs 6.00
* **Typical invariants (all rows):** max |dE|/E = 2.686E+07, max |dL|/L = 7.370E-01, max dCOM/scale = 1.341E-15, worst rev_err = 8.324E+04

### WH-Y6

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** —
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3; force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** R4 6.19 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 2.221E-10

### Leapfrog

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: K1.A2, K1.A3, K2.A2, K2.A3, K3.A2, K3.A3, K4.A1, K4.A2, K4.A3, K5.A2, K5.A3, K6.A2, K6.A3, K7.A2, K7.A3, T1.A2, T1.A3, T2.A2, T2.A3
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3, R1.A2, R1.A3, R3.A1, R4.A2, L2.A1, L2.A2; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1
* **Observed order:** K1 2.00 vs 2.00; K2 2.00 vs 2.00; K3 2.00 vs 2.00; K5 2.00 vs 2.00; K6 2.00 vs 2.00; K7 2.00 vs 2.00; T1 2.00 vs 2.00; T2 2.00 vs 2.00; R1 2.00 vs 2.00; R4 2.00 vs 2.00; L2 2.00 vs 2.00
* **Typical invariants (all rows):** max |dE|/E = 6.196E+03, max |dL|/L = 2.272E+01, max dCOM/scale = 1.292E-12, worst rev_err = 6.444E+06

### RKN12(10)

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, nbody-chaotic, many-body, ship, perturbed
* **Best at:** central-dominant A1, central-dominant A2, central-dominant A3, close-encounter A1, escape A1, escape A2, escape A3, long-horizon A3, nbody-regular A1, nbody-regular A2, nbody-regular A3, ship A1
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: T3.A2, T3.A3
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 1.08 vs n/a; K5 1.18 vs n/a; T1 1.12 vs n/a; T2 1.07 vs n/a; R1 0.96 vs n/a; R3 1.17 vs n/a; L2 1.54 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 6.673E+01, max |dL|/L = 6.382E-01, max dCOM/scale = 3.220E-14, worst rev_err = 6.444E+06

### IAS15

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, nbody-chaotic, many-body, 1pn, ship, perturbed
* **Best at:** close-encounter A2, long-horizon A2, many-body A2, sanity A3
* **N/A:** —
* **FAIL:** level not reached at the finest control: T3.A3
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 2.28 vs n/a; K5 1.75 vs n/a; K6 1.76 vs n/a; K7 2.26 vs n/a; T1 2.16 vs n/a; R2 2.61 vs n/a; R4 2.06 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, max dCOM/scale = 1.606E-10, worst rev_err = 6.444E+06

### SRKN14A

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** RHS budget reached before the level: K4.A3, T3.A1, T3.A2, T3.A3, R1.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** K1 5.86 vs 6.00; K2 5.85 vs 6.00; K3 6.10 vs 6.00; K5 5.77 vs 6.00; K6 5.78 vs 6.00; K7 6.01 vs 6.00; T2 5.99 vs 6.00; R3 5.99 vs 6.00; R4 5.92 vs 6.00; L2 6.27 vs 6.00
* **Typical invariants (all rows):** max |dE|/E = 6.895E+05, max |dL|/L = 6.382E-01, max dCOM/scale = 9.758E-14, worst rev_err = 1.023E+07

### WH-BM14A

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** —
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3; force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** R4 6.88 vs n/a
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 6.444E+06

### GJ8

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G2 order: observed 9.27 vs expected 10.00: K2.A1, K2.A2, K2.A3; G2 order: observed 7.08 vs expected 10.00: K3.A1, K3.A2, K3.A3; G2 order: observed 8.83 vs expected 10.00: T2.A1, T2.A2, T2.A3; G2 order: observed 10.59 vs expected 10.00: R4.A1, R4.A2; G2 order: observed 10.71 vs expected 10.00: L2.A1, L2.A2
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** K1 10.03 vs 10.00; K2 9.27 vs 10.00; K3 7.08 vs 10.00; K5 9.70 vs 10.00; K6 9.93 vs 10.00; K7 10.47 vs 10.00; T2 8.83 vs 10.00; R4 10.59 vs 10.00; L2 10.71 vs 10.00
* **Typical invariants (all rows):** max |dE|/E = 1.545E+06, max |dL|/L = 6.382E-01, max dCOM/scale = 3.134E-14, worst rev_err = 2.099E+06

### Encke-IAS15

* **Valid for (≥1 PASS):** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** ecc-low A1, ecc-low A2, ecc-low A3, ecc-mid A1, ecc-mid A2, escape A3, kepler A1, kepler A2, kepler A3
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, K8.A1, K8.A2, K8.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** —
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Gate notes on PASS:** G3 skipped: model drift (energy 1.137E-02 > 1.000E-05); G3 skipped: model drift (angular 6.382E-01 > 1.000E-05): R4.A1; G3 skipped: model drift (energy 1.137E-02 > 1.000E-08); G3 skipped: model drift (angular 6.382E-01 > 1.000E-08): R4.A2
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 6.444E+06

