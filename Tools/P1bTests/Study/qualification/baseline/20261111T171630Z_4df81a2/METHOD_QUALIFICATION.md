# Method qualification (Test564)

Generated from `matrix.csv` and `runs.csv` of run `20261111T171630Z_4df81a2`.
Profile: **full**; commit: `4df81a2f3633794149ea3cb33f574ff758c2db38`; 16 problems × 15 methods × 3 levels.

Statuses: PASS / FAIL / N/A (from `Supports`) / SKIP (oracle floor or budget) / ERROR.
Gate thresholds are frozen; see `PROGRESS.md` in this folder.

## Summary

| method | family | PASS | FAIL | N/A | SKIP | best at |
|---|---|---:|---:|---:|---:|---|
| RK4 | Runge-Kutta | 34 | 2 | 0 | 12 | — |
| DOPRI5-fixed | Runge-Kutta | 36 | 1 | 0 | 11 | — |
| DOPRI5-adaptive | Runge-Kutta | 30 | 13 | 0 | 5 | — |
| DOP853-adaptive | Runge-Kutta | 40 | 3 | 0 | 5 | escape A1, long-horizon A1, many-body A1, many-body A2, sanity A1, sanity A2, velocity-dependent A1, velocity-dependent A2 |
| PEFRL | Symplectic | 30 | 2 | 6 | 10 | — |
| Y6 | Symplectic | 31 | 1 | 6 | 10 | — |
| Y6C | Symplectic | 34 | 1 | 6 | 7 | — |
| WH-Y6 | Kepler-split | 22 | 1 | 21 | 4 | — |
| Leapfrog | Symplectic | 10 | 19 | 6 | 13 | — |
| RKN12(10) | Runge-Kutta | 35 | 3 | 6 | 4 | central-dominant A1, central-dominant A2, central-dominant A3, close-encounter A1, escape A1, escape A2, escape A3, long-horizon A3, nbody-regular A1, nbody-regular A2, nbody-regular A3, ship A1 |
| IAS15 | Gauss-Radau | 41 | 2 | 0 | 5 | long-horizon A2, many-body A2, sanity A3 |
| SRKN14A | Symplectic | 32 | 1 | 6 | 9 | — |
| WH-BM14A | Kepler-split | 22 | 1 | 21 | 4 | — |
| GJ8 | Multistep | 34 | 1 | 6 | 7 | — |
| Encke-IAS15 | Encke | 22 | 1 | 21 | 4 | ecc-low A1, ecc-low A2, ecc-low A3, ecc-mid A1, ecc-mid A2, escape A3, kepler A1, kepler A2, kepler A3 |

## Method × problem (A1 / A2 / A3)

| method | K1 | K2 | K3 | K4 | K5 | K6 | K7 | K8 | T1 | T2 | T3 | R1 | R2 | R3 | R4 | L2 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| RK4 | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | P/P/skip | skip/skip/skip | P/F/skip | P/skip/skip |
| DOPRI5-fixed | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | P/P/skip | P/skip/skip | P/F/skip | P/skip/skip |
| DOPRI5-adaptive | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | F/F/F | F/F/F | F/F/skip | F/skip/skip | P/F/skip | F/F/skip |
| DOP853-adaptive | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/F/F | P/P/P | P/P/skip | P/skip/skip | P/F/skip | P/P/skip |
| PEFRL | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/skip/skip |
| Y6 | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/skip | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| Y6C | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | skip/skip/skip | P/P/P | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| WH-Y6 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| Leapfrog | P/F/F | P/F/F | P/F/F | F/F/F | P/F/F | P/F/F | P/F/F | n/a/n/a/n/a | P/F/F | P/F/F | skip/skip/skip | P/skip/skip | n/a/n/a/n/a | skip/skip/skip | P/skip/skip | skip/skip/skip |
| RKN12(10) | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | P/F/F | P/P/P | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| IAS15 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/F | P/P/P | P/P/skip | P/skip/skip | P/F/skip | P/P/skip |
| SRKN14A | P/P/P | P/P/P | P/P/P | P/P/skip | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | skip/skip/skip | P/P/skip | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| WH-BM14A | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| GJ8 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | P/P/P | P/P/P | skip/skip/skip | P/P/P | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |
| Encke-IAS15 | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | P/P/P | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | n/a/n/a/n/a | P/skip/skip | P/F/skip | P/P/skip |

Legend: P = PASS, F = FAIL, n/a = not applicable, skip = budget/oracle floor.

## Per method

### RK4

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, many-body, 1pn, ship
* **Best at:** —
* **N/A:** —
* **FAIL:** level not reached at the finest control: K4.A3; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, R3.A1, L2.A2; oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 3.879E+03, max |dL|/L = 1.436E+04, max dCOM/scale = 1.606E-10, worst rev_err = 6.376E+06

### DOPRI5-fixed

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, many-body, 1pn, ship, perturbed
* **Best at:** —
* **N/A:** —
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, L2.A2; oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 2.460E+04, max |dL|/L = 7.694E+04, max dCOM/scale = 1.606E-10, worst rev_err = 9.489E+06

### DOPRI5-adaptive

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, ship
* **Best at:** —
* **N/A:** —
* **FAIL:** level not reached at the finest control: T1.A3, R2.A1, R2.A2; G5 finiteness: aborted at t=1.822E+001 s: scaled error stayed above 1 at the smallest step; step floor 2.000E-013 s reached at t=1.822E+001 s (last scaled error 1.071E+000); worst error at completed checkpoints 3.01E+000 m: T3.A1, T3.A2, T3.A3; G5 finiteness: aborted at t=1.552E+008 s: a pair distance collapsed to 3.97E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 7.33E+008 m: R1.A1, R1.A2, R1.A3; G5 finiteness: aborted at t=1.093E+006 s: scaled error stayed above 1 at the smallest step; step floor 2.592E-008 s reached at t=1.093E+006 s (last scaled error 2.738E+002); worst error at completed checkpoints 1.31E+007 m: R3.A1; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2; G5 finiteness: aborted at t=5.764E+002 s: non-finite trial state; step floor 6.283E-012 s reached at t=5.764E+002 s (last scaled error 1.000E+010); worst error at completed checkpoints 1.57E+000 m: L2.A1, L2.A2
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 2.463E+01, max |dL|/L = 7.847E-01, max dCOM/scale = 8.929E-14, worst rev_err = 6.444E+06

### DOP853-adaptive

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, nbody-chaotic, many-body, 1pn, ship, perturbed
* **Best at:** escape A1, long-horizon A1, many-body A1, many-body A2, sanity A1, sanity A2, velocity-dependent A1, velocity-dependent A2
* **N/A:** —
* **FAIL:** level not reached at the finest control: T3.A2, T3.A3; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.549E+01, max |dL|/L = 6.382E-01, max dCOM/scale = 1.606E-10, worst rev_err = 6.444E+06

### PEFRL

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: K4.A3; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: T1.A3, T3.A1, T3.A2, T3.A3, R1.A3, L2.A2; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.577E+05, max |dL|/L = 6.613E-01, max dCOM/scale = 2.244E-13, worst rev_err = 6.500E+06

### Y6

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: K4.A3, T1.A3, T3.A1, T3.A2, T3.A3, R1.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 5.278E+04, max |dL|/L = 7.370E-01, max dCOM/scale = 1.104E-13, worst rev_err = 8.324E+04

### Y6C

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 2.686E+07, max |dL|/L = 7.370E-01, max dCOM/scale = 1.341E-15, worst rev_err = 8.324E+04

### WH-Y6

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** —
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3; force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 2.221E-10

### Leapfrog

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: K1.A2, K1.A3, K2.A2, K2.A3, K3.A2, K3.A3, K4.A1, K4.A2, K4.A3, K5.A2, K5.A3, K6.A2, K6.A3, K7.A2, K7.A3, T1.A2, T1.A3, T2.A2, T2.A3
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3, R1.A2, R1.A3, R3.A1, R4.A2, L2.A1, L2.A2; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 6.196E+03, max |dL|/L = 2.272E+01, max dCOM/scale = 1.292E-12, worst rev_err = 6.444E+06

### RKN12(10)

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, nbody-chaotic, many-body, ship, perturbed
* **Best at:** central-dominant A1, central-dominant A2, central-dominant A3, close-encounter A1, escape A1, escape A2, escape A3, long-horizon A3, nbody-regular A1, nbody-regular A2, nbody-regular A3, ship A1
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** level not reached at the finest control: T3.A2, T3.A3; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 6.673E+01, max |dL|/L = 6.382E-01, max dCOM/scale = 3.220E-14, worst rev_err = 6.444E+06

### IAS15

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, velocity-dependent, nbody-regular, long-horizon, nbody-chaotic, many-body, 1pn, ship, perturbed
* **Best at:** long-horizon A2, many-body A2, sanity A3
* **N/A:** —
* **FAIL:** level not reached at the finest control: T3.A3; G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 1.969E+00 > 0.1*1.496E-01: R2.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, max dCOM/scale = 1.606E-10, worst rev_err = 6.444E+06

### SRKN14A

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: K4.A3, T3.A1, T3.A2, T3.A3, R1.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 6.895E+05, max |dL|/L = 6.382E-01, max dCOM/scale = 9.758E-14, worst rev_err = 1.023E+07

### WH-BM14A

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** —
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3; force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 6.444E+06

### GJ8

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, sanity, nbody-regular, long-horizon, many-body, ship, perturbed
* **Best at:** —
* **N/A:** force depends on velocity or mass (drag/thrust): splitting order is lost: K8.A1, K8.A2, K8.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** RHS budget reached before the level: T3.A1, T3.A2, T3.A3; oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.545E+06, max |dL|/L = 6.382E-01, max dCOM/scale = 3.134E-14, worst rev_err = 2.264E+06

### Encke-IAS15

* **Valid for:** kepler, ecc-low, central-dominant, ecc-mid, ecc-high, escape, close-encounter, ship, perturbed, long-horizon
* **Best at:** ecc-low A1, ecc-low A2, ecc-low A3, ecc-mid A1, ecc-mid A2, escape A3, kepler A1, kepler A2, kepler A3
* **N/A:** needs one central body at the origin (CentralMu > 0, PosDim == 3): K7.A1, K7.A2, K7.A3, K8.A1, K8.A2, K8.A3, T1.A1, T1.A2, T1.A3, T2.A1, T2.A2, T2.A3, T3.A1, T3.A2, T3.A3, R1.A1, R1.A2, R1.A3, R2.A1, R2.A2, R2.A3
* **FAIL:** G3 angular momentum drift 6.382E-01 > 10*1.837E-03: R4.A2
* **SKIP:** oracle floor 2.544E-03 > 0.1*6.771E-03: R3.A2; oracle floor 2.544E-03 > 0.1*6.771E-06: R3.A3; oracle floor 3.682E-07 > 0.1*1.837E-06: R4.A3; oracle floor 1.568E-12 > 0.1*1.000E-12: L2.A3
* **Observed order:** n/a (too few asymptotic points)
* **Typical invariants (all rows):** max |dE|/E = 1.137E-02, max |dL|/L = 6.382E-01, worst rev_err = 6.444E+06

