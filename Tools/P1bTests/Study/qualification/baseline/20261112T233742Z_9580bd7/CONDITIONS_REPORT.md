# Conditions report (Test564, Appendix A)

Generated from `conditions.csv` and `matrix.csv` of run `20261112T233742Z_9580bd7`; no number is typed by hand.
Extrema are computed from the reference checkpoints plus the initial state; for a
single particle in a central field the osculating conic adds the periapsis/apoapsis
extreme the sparse checkpoint grid could miss. Price is `calls + w·aux`.

## Flight conditions

| problem | tags | central body | r_min, m | r_max, m | v_min, m/s | v_max, m/s | horizon, s | forces | vel/mass | oracle floor, m |
|---|---|---|---:|---:|---:|---:|---:|---|---:|---:|
| K1 | kepler;ecc-low;central-dominant | central field (mu=1, origin) | 1.0000E+000 | 1.0000E+000 | 1.0000E+000 | 1.0000E+000 | 18.8496 | gravity | no | 1.000E-014 |
| K2 | kepler;ecc-low;central-dominant | central field (mu=1, origin) | 9.0000E-001 | 1.1000E+000 | 9.0453E-001 | 1.1055E+000 | 18.8496 | gravity | no | 1.000E-014 |
| K3 | kepler;ecc-mid;central-dominant | central field (mu=1, origin) | 5.0000E-001 | 1.5000E+000 | 5.7735E-001 | 1.7321E+000 | 18.8496 | gravity | no | 5.050E-014 |
| K4 | kepler;ecc-high;central-dominant | central field (mu=1, origin) | 1.0000E-001 | 1.9000E+000 | 2.2942E-001 | 4.3589E+000 | 18.8496 | gravity | no | 4.067E-014 |
| K5 | kepler;escape;central-dominant | central field (mu=1, origin) | 1.0000E+000 | 6.8047E+000 | 5.4214E-001 | 1.4142E+000 | 10 | gravity | no | 1.000E-014 |
| K6 | kepler;escape;close-encounter;central-dominant | central field (mu=1, origin) | 1.0000E+000 | 9.5095E+000 | 8.4280E-001 | 1.5811E+000 | 10 | gravity | no | 1.000E-014 |
| K7 | sanity | origin (no central body) | 1.0000E+000 | 1.0000E+000 | 1.0000E+000 | 1.0000E+000 | 25.1327 | none | no | 1.000E-014 |
| K8 | velocity-dependent;sanity | origin (no central body) | 9.2832E-005 | 1.0000E+000 | 0.0000E+000 | 9.2669E-001 | 25.1327 | none | yes | 1.000E-014 |
| T1 | nbody-regular;long-horizon | barycentre (3 bodies) | 4.9661E-016 | 1.0810E+000 | 4.6721E-001 | 1.2717E+000 | 63.2591 | gravity | no | 4.000E-016 |
| T2 | nbody-regular | barycentre (3 bodies) | 5.7735E-001 | 5.7735E-001 | 1.0000E+000 | 1.0000E+000 | 3.6276 | gravity | no | 1.000E-014 |
| T3 | nbody-chaotic;close-encounter | barycentre (3 bodies) | 1.4824E-002 | 4.2449E+001 | 0.0000E+000 | 1.5968E+001 | 20 | gravity | no | 4.000E-016 |
| R1 | nbody-regular;long-horizon;many-body;central-dominant | barycentre (7 bodies, no central body) | 3.8232E+008 | 1.4131E+012 | 1.0227E+001 | 3.5272E+004 | 3.15576E+08 | gravity | no | 5.882E-002 |
| R2 | 1pn;velocity-dependent;long-horizon;many-body | barycentre (10 bodies, no central body) | 9.3387E+007 | 4.4702E+012 | 8.5157E+000 | 5.8987E+004 | 3.1536E+08 | gravity;1pn | yes | 1.969E+000 |
| R3 | ship;perturbed | Earth | 6.7710E+006 | 6.7710E+006 | 7.6726E+003 | 7.6726E+003 | 2.592E+06 | gravity;third-body | no | 2.544E-003 |
| R4 | ship;close-encounter | Moon | 1.8370E+006 | 1.0363E+008 | 1.0316E+003 | 2.5175E+003 | 91695.6 | gravity;third-body | no | 3.682E-007 |
| L1 | kepler;long-horizon | Earth | 6.7710E+006 | 6.7710E+006 | 7.6726E+003 | 7.6726E+003 | 5.54486E+06 | gravity;third-body | no | 1.824E-004 |
| L2 | kepler;ecc-mid;long-horizon | central field (mu=1, origin) | 5.0000E-001 | 1.5000E+000 | 5.7735E-001 | 1.7321E+000 | 628.319 | gravity | no | 1.568E-012 |
| S01 | leo | Earth | 6.7710E+006 | 6.7710E+006 | 7.6726E+003 | 7.6726E+003 | 86400 | gravity;third-body | no | 5.021E-006 |
| S02 | leo | Earth | 6.7710E+006 | 6.7710E+006 | 7.6726E+003 | 7.6726E+003 | 86400 | gravity;third-body | no | 1.458E-006 |
| S03 | leo;j2 | Earth | 6.7634E+006 | 6.7710E+006 | 7.6685E+003 | 7.6802E+003 | 86400 | gravity;third-body;j2 | no | 2.623E-006 |
| S04 | gto;ecc-high | Earth | 6.7710E+006 | 4.6371E+007 | 1.4800E+003 | 1.0136E+004 | 172800 | gravity;third-body | no | 3.390E-006 |
| S05 | molniya;ecc-high | Earth | 6.8692E+006 | 4.6372E+007 | 1.4893E+003 | 1.0054E+004 | 604800 | gravity;third-body | no | 9.521E-005 |
| S06 | geo;long-arc | Earth | 4.2159E+007 | 4.2170E+007 | 3.0742E+003 | 3.0751E+003 | 2.592E+06 | gravity;third-body | no | 1.796E-004 |
| S09 | leo;j2 | Earth | 6.7515E+006 | 6.7710E+006 | 7.6726E+003 | 7.6947E+003 | 86400 | gravity;third-body;j2 | no | 2.568E-006 |
| S14 | heo;ecc-high | Earth | 6.6710E+006 | 1.2000E+008 | 5.9149E+002 | 1.0640E+004 | 432000 | gravity;third-body | no | 2.316E-005 |
| S10 | leo;drag;velocity-dependent | Earth | 6.6210E+006 | 6.6210E+006 | 7.7590E+003 | 7.7590E+003 | 86400 | gravity;third-body;drag | yes | 1.921E-005 |
| S11 | leo;thrust;velocity-dependent | Earth | 6.6960E+006 | 1.4882E+007 | 4.0771E+003 | 9.0615E+003 | 86400 | gravity;third-body;thrust | yes | 1.778E-005 |
| S17 | leo;thrust;velocity-dependent | Earth | 6.6889E+006 | 1.5414E+007 | 3.9583E+003 | 9.1151E+003 | 86400 | gravity;third-body;thrust | yes | 2.505E-005 |
| S07 | heliocentric;far-flight | Sun | 1.4960E+011 | 1.4961E+011 | 2.9783E+004 | 2.9785E+004 | 3.15576E+07 | gravity;third-body | no | 5.315E-004 |
| S08 | heliocentric;far-flight;ecc-mid | Sun | 7.4799E+010 | 2.9920E+011 | 1.3320E+004 | 5.3280E+004 | 3.15576E+07 | gravity;third-body | no | 9.181E-004 |
| S12 | flyby;moon-soi;close-encounter | Moon | 2.0000E+007 | 1.1413E+008 | 5.9512E+002 | 9.0000E+002 | 172800 | gravity;third-body | no | 4.621E-006 |
| S15 | flyby;escape;close-encounter | Earth | 6.6710E+006 | 5.1212E+008 | 5.6061E+003 | 1.2222E+004 | 86400 | gravity;third-body | no | 1.819E-006 |
| P2 | planets;long-arc | barycentre (no central body) | 2.9386E+008 | 1.5066E+012 | 8.9914E+000 | 3.5276E+004 | 3.15576E+09 | gravity | no | 1.712E+000 |
| P3 | planets | barycentre (no central body) | 3.9107E+008 | 3.4538E+011 | 2.1022E+001 | 2.6498E+004 | 3.15576E+08 | gravity | no | 3.590E-002 |
| P4 | planets;close-encounter | barycentre (no central body) | 9.5238E+009 | 1.5714E+010 | 2.7603E+004 | 4.5544E+004 | 3.15576E+08 | gravity | no | 9.126E-002 |
| P5 | planets;ecc-high | barycentre (no central body) | 7.0855E+008 | 8.1579E+011 | 1.1863E+001 | 8.9390E+004 | 3.15576E+08 | gravity | no | 7.359E-002 |
| P6 | planets;probe-central | Sun | 1.0470E+011 | 1.9450E+011 | 2.1854E+004 | 4.0595E+004 | 3.13632E+08 | gravity;third-body | no | 6.875E-004 |
| F01 | far-flight;heliocentric | Sun | 2.2440E+012 | 6.7319E+012 | 3.1396E+003 | 9.4187E+003 | 3.15576E+07 | gravity;third-body | no | 1.163E-002 |
| F02 | heliocentric;ecc-mid | Sun | 7.4799E+010 | 2.2440E+011 | 1.7196E+004 | 5.1589E+004 | 3.15576E+07 | gravity;third-body | no | 1.253E-003 |
| F03 | flyby;close-encounter | Small body (mu=1e9) | 2.0000E+003 | 6.4833E+004 | 1.8262E+002 | 1.0012E+003 | 251.014 | gravity;third-body | no | 5.232E-008 |
| F04 | moon-soi;long-arc | Moon | 1.8369E+006 | 1.8371E+006 | 1.6336E+003 | 1.6338E+003 | 7.06515E+06 | gravity;third-body | no | 1.178E-002 |
| F05 | moon-soi | Moon | 3.7442E+005 | 1.7380E+006 | 1.0000E+003 | 4.6419E+003 | 1234.57 | gravity;third-body | no | 1.060E-007 |

## Scenario results by metre goal

| problem | goal | winner | price | second | margin | excluded N/A | excluded FAIL |
|---|---|---|---:|---|---:|---|---|
| R1 | M100 | GJ8 | 1.956E+04 | RKN12(10) | 1.12× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| R1 | M10 | RKN12(10) | 2.592E+04 | GJ8 | 1.12× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| R1 | M1 | RKN12(10) | 3.044E+04 | GJ8 | 1.41× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| R1 | M0.1 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| R1 | M0.01 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| R3 | M100 | WH-BM14A | 1.282E+04 | WH-Y6 | 1.46× | none | RK4 (G2 order: observed 4.96 vs expected 4.00) |
| R3 | M10 | WH-BM14A | 1.923E+04 | WH-Y6 | 1.46× | none | RK4 (G2 order: observed 4.96 vs expected 4.00) |
| R3 | M1 | WH-BM14A | 2.565E+04 | WH-Y6 | 1.09× | none | RK4 (G2 order: observed 4.96 vs expected 4.00) |
| R3 | M0.1 | WH-BM14A | 2.565E+04 | WH-Y6 | 1.58× | none | RK4 (G2 order: observed 4.96 vs expected 4.00) |
| R3 | M0.01 | — | — | — | — | none | none |
| R4 | M100 | DOPRI5-adaptive | 2.470E+02 | DOP853-adaptive | 1.36× | none | GJ8 (G2 order: observed 10.59 vs expected 10.00) |
| R4 | M10 | DOPRI5-adaptive | 3.250E+02 | DOP853-adaptive | 1.11× | none | GJ8 (G2 order: observed 10.59 vs expected 10.00) |
| R4 | M1 | DOP853-adaptive | 3.850E+02 | DOPRI5-adaptive | 1.06× | none | GJ8 (G2 order: observed 10.59 vs expected 10.00) |
| R4 | M0.1 | DOP853-adaptive | 4.450E+02 | RKN12(10) | 1.03× | none | GJ8 (G2 order: observed 10.59 vs expected 10.00) |
| R4 | M0.01 | RKN12(10) | 4.770E+02 | DOP853-adaptive | 1.11× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 10.59 vs expected 10.00) |
| L1 | M100 | WH-BM14A | 3.972E+04 | WH-Y6 | 1.45× | none | none |
| L1 | M10 | WH-BM14A | 5.295E+04 | WH-Y6 | 1.09× | none | none |
| L1 | M1 | WH-BM14A | 7.943E+04 | WH-Y6 | 1.09× | none | none |
| L1 | M0.1 | WH-BM14A | 7.943E+04 | WH-Y6 | 1.09× | none | none |
| L1 | M0.01 | WH-Y6 | 4.299E+05 | DOP853-adaptive | 2.42× | none | RKN12(10) (level not reached at the finest control); IAS15 (level not reached at the finest control) |
| S01 | M100 | WH-BM14A | 4.051E+02 | WH-Y6 | 1.46× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S01 | M10 | WH-Y6 | 5.905E+02 | WH-BM14A | 1.37× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S01 | M1 | WH-BM14A | 8.101E+02 | WH-Y6 | 1.21× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S02 | M100 | WH-BM14A | 4.001E+02 | WH-Y6 | 1.46× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S02 | M10 | WH-Y6 | 5.836E+02 | WH-BM14A | 1.37× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S02 | M1 | WH-BM14A | 8.003E+02 | WH-Y6 | 1.21× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S03 | M100 | RKN12(10) | 8.850E+02 | WH-BM14A | 1.32× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S03 | M10 | RKN12(10) | 1.157E+03 | GJ8 | 1.65× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S03 | M1 | RKN12(10) | 1.412E+03 | GJ8 | 1.35× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S04 | M100 | WH-BM14A | 8.651E+02 | WH-Y6 | 1.45× | none | GJ8 (G2 order: observed 10.76 vs expected 10.00) |
| S04 | M10 | RKN12(10) | 1.530E+03 | WH-BM14A | 1.13× | none | GJ8 (G2 order: observed 10.76 vs expected 10.00) |
| S04 | M1 | RKN12(10) | 1.797E+03 | WH-BM14A | 1.44× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 10.76 vs expected 10.00) |
| S05 | M100 | RKN12(10) | 5.189E+03 | WH-BM14A | 1.31× | none | none |
| S05 | M10 | RKN12(10) | 6.091E+03 | WH-BM14A | 1.11× | none | none |
| S05 | M1 | RKN12(10) | 6.594E+03 | WH-BM14A | 1.54× | none | none |
| S06 | M100 | RKN12(10) | 2.262E+03 | WH-BM14A | 1.02× | none | RK4 (G2 order: observed 4.76 vs expected 4.00) |
| S06 | M10 | WH-BM14A | 2.299E+03 | RKN12(10) | 1.21× | none | RK4 (G2 order: observed 4.76 vs expected 4.00); Leapfrog (level not reached at the finest control) |
| S06 | M1 | WH-BM14A | 3.065E+03 | RKN12(10) | 1.03× | none | RK4 (G2 order: observed 4.76 vs expected 4.00); Leapfrog (level not reached at the finest control) |
| S09 | M100 | WH-BM14A | 3.930E+02 | RKN12(10) | 2.25× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S09 | M10 | WH-BM14A | 7.859E+02 | RKN12(10) | 1.47× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S09 | M1 | WH-BM14A | 7.859E+02 | RKN12(10) | 1.82× | none | RK4 (G2 order: observed 4.58 vs expected 4.00) |
| S14 | M100 | RKN12(10) | 1.880E+03 | DOP853-adaptive | 1.19× | none | none |
| S14 | M10 | RKN12(10) | 2.065E+03 | DOP853-adaptive | 1.22× | none | Leapfrog (level not reached at the finest control) |
| S14 | M1 | RKN12(10) | 2.282E+03 | DOP853-adaptive | 1.32× | none | Leapfrog (level not reached at the finest control) |
| S10 | M100 | DOP853-adaptive | 2.149E+03 | IAS15 | 1.85× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 4.63 vs expected 4.00) |
| S10 | M10 | DOP853-adaptive | 3.097E+03 | IAS15 | 1.28× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 4.63 vs expected 4.00) |
| S10 | M1 | DOP853-adaptive | 3.493E+03 | IAS15 | 1.14× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 4.63 vs expected 4.00) |
| S11 | M100 | DOP853-adaptive | 2.407E+03 | DOPRI5-adaptive | 1.09× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 3.46 vs expected 4.00) |
| S11 | M10 | DOP853-adaptive | 3.080E+03 | DOPRI5-adaptive | 1.67× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 3.46 vs expected 4.00) |
| S11 | M1 | DOP853-adaptive | 3.649E+03 | IAS15 | 1.41× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | RK4 (G2 order: observed 3.46 vs expected 4.00) |
| S17 | M100 | DOP853-adaptive | 2.474E+03 | DOPRI5-adaptive | 1.05× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | none |
| S17 | M10 | DOP853-adaptive | 3.080E+03 | IAS15 | 1.70× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | none |
| S17 | M1 | DOP853-adaptive | 3.840E+03 | IAS15 | 1.44× | PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8 | none |
| S07 | M100 | RKN12(10) | 1.880E+02 | DOP853-adaptive | 1.73× | none | Leapfrog (level not reached at the finest control) |
| S07 | M10 | RKN12(10) | 1.880E+02 | DOP853-adaptive | 2.18× | none | Leapfrog (level not reached at the finest control) |
| S07 | M1 | RKN12(10) | 2.380E+02 | GJ8 | 2.37× | none | Leapfrog (level not reached at the finest control) |
| S08 | M100 | RKN12(10) | 2.390E+02 | DOP853-adaptive | 1.70× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.07 vs expected 10.00) |
| S08 | M10 | RKN12(10) | 3.400E+02 | DOP853-adaptive | 1.62× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.07 vs expected 10.00) |
| S08 | M1 | RKN12(10) | 3.570E+02 | IAS15 | 1.75× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.07 vs expected 10.00) |
| S12 | M100 | DOPRI5-adaptive | 1.030E+02 | SRKN14A | 1.22× | none | DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00) |
| S12 | M10 | DOPRI5-adaptive | 1.390E+02 | DOP853-adaptive | 1.22× | none | DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00) |
| S12 | M1 | DOP853-adaptive | 1.930E+02 | DOPRI5-adaptive | 1.00× | none | DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00) |
| S12 | M0.1 | DOP853-adaptive | 1.930E+02 | RKN12(10) | 1.24× | none | DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00) |
| S12 | M0.01 | DOP853-adaptive | 1.930E+02 | RKN12(10) | 1.24× | none | DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00) |
| S15 | M100 | DOPRI5-adaptive | 2.650E+02 | DOP853-adaptive | 1.18× | none | none |
| S15 | M10 | DOP853-adaptive | 3.490E+02 | DOPRI5-adaptive | 1.02× | none | none |
| S15 | M1 | DOP853-adaptive | 3.970E+02 | RKN12(10) | 1.03× | none | none |
| S15 | M0.1 | RKN12(10) | 4.090E+02 | DOP853-adaptive | 1.06× | none | Leapfrog (level not reached at the finest control) |
| S15 | M0.01 | RKN12(10) | 4.430E+02 | DOP853-adaptive | 1.19× | none | Leapfrog (level not reached at the finest control) |
| P2 | M100 | GJ8 | 2.854E+05 | RKN12(10) | 1.06× | WH-Y6, WH-BM14A, Encke-IAS15 | DOPRI5-adaptive (G5 finiteness: aborted at t=5.176E+008 s: a pair distance collapsed to 4.00E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 7.45E+008 m) |
| P2 | M10 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P2 | M1 | — | — | — | — | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P3 | M100 | GJ8 | 2.920E+04 | RKN12(10) | 1.16× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P3 | M10 | RKN12(10) | 3.793E+04 | GJ8 | 1.39× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P3 | M1 | RKN12(10) | 5.106E+04 | GJ8 | 1.13× | WH-Y6, WH-BM14A, Encke-IAS15 | DOPRI5-adaptive (level not reached at the finest control) |
| P4 | M100 | RKN12(10) | 4.529E+04 | GJ8 | 1.29× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P4 | M10 | RKN12(10) | 5.233E+04 | GJ8 | 1.59× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P4 | M1 | IAS15 | 8.463E+04 | GJ8 | 1.08× | WH-Y6, WH-BM14A, Encke-IAS15 | DOP853-adaptive (level not reached at the finest control); RKN12(10) (level not reached at the finest control) |
| P5 | M100 | RKN12(10) | 6.451E+03 | DOP853-adaptive | 2.39× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P5 | M10 | RKN12(10) | 8.410E+03 | IAS15 | 2.12× | WH-Y6, WH-BM14A, Encke-IAS15 | none |
| P5 | M1 | RKN12(10) | 1.011E+04 | DOP853-adaptive | 2.27× | WH-Y6, WH-BM14A, Encke-IAS15 | DOPRI5-adaptive (level not reached at the finest control) |
| P6 | M100 | RKN12(10) | 3.045E+03 | WH-BM14A | 1.09× | none | Leapfrog (level not reached at the finest control) |
| P6 | M10 | RKN12(10) | 3.528E+03 | WH-BM14A | 1.88× | none | Leapfrog (level not reached at the finest control) |
| P6 | M1 | RKN12(10) | 4.862E+03 | WH-BM14A | 1.37× | none | Leapfrog (level not reached at the finest control) |
| F01 | M100 | DOPRI5-adaptive | 5.500E+01 | DOP853-adaptive | 1.98× | none | Leapfrog (level not reached at the finest control) |
| F01 | M10 | DOPRI5-adaptive | 9.700E+01 | DOP853-adaptive | 1.12× | none | Leapfrog (level not reached at the finest control) |
| F01 | M1 | DOP853-adaptive | 1.090E+02 | DOPRI5-adaptive | 1.06× | none | Leapfrog (level not reached at the finest control) |
| F02 | M100 | RKN12(10) | 3.550E+02 | DOP853-adaptive | 1.97× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.40 vs expected 10.00) |
| F02 | M10 | RKN12(10) | 4.220E+02 | DOP853-adaptive | 2.07× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.40 vs expected 10.00) |
| F02 | M1 | RKN12(10) | 4.900E+02 | IAS15 | 1.85× | none | Leapfrog (level not reached at the finest control); GJ8 (G2 order: observed 9.40 vs expected 10.00) |
| F03 | M100 | DOPRI5-adaptive | 1.690E+02 | DOP853-adaptive | 1.78× | none | none |
| F03 | M10 | DOPRI5-adaptive | 1.810E+02 | DOP853-adaptive | 1.66× | none | none |
| F03 | M1 | DOPRI5-adaptive | 2.290E+02 | DOP853-adaptive | 1.42× | none | none |
| F03 | M0.1 | DOPRI5-adaptive | 2.950E+02 | DOP853-adaptive | 1.14× | none | none |
| F03 | M0.01 | DOPRI5-adaptive | 3.730E+02 | DOP853-adaptive | 1.03× | none | none |
| F04 | M100 | WH-BM14A | 5.153E+04 | WH-Y6 | 1.64× | none | none |
| F04 | M10 | WH-BM14A | 7.729E+04 | RKN12(10) | 1.52× | none | none |
| F04 | M1 | WH-BM14A | 1.159E+05 | RKN12(10) | 1.13× | none | none |
| F05 | M100 | PEFRL | 3.200E+01 | DOPRI5-fixed | 1.53× | none | Y6 (G2 order: observed 6.68 vs expected 6.00); Y6C (G2 order: observed 6.67 vs expected 6.00); GJ8 (G2 order: observed 5.71 vs expected 10.00) |
| F05 | M10 | DOPRI5-fixed | 4.900E+01 | PEFRL | 1.31× | none | Y6 (G2 order: observed 6.68 vs expected 6.00); Y6C (G2 order: observed 6.67 vs expected 6.00); GJ8 (G2 order: observed 5.71 vs expected 10.00) |
| F05 | M1 | DOPRI5-fixed | 9.700E+01 | SRKN14A | 1.15× | none | DOPRI5-adaptive (G3 energy drift 6.451E-06 > 10*5.754E-07); Y6 (G2 order: observed 6.68 vs expected 6.00); Y6C (G2 order: observed 6.67 vs expected 6.00); GJ8 (G2 order: observed 5.71 vs expected 10.00) |

## Class summary

Classes are the groups of A9.1. The rule is section 11: at least two tasks with the
goal evaluated; candidates must pass on all of them. A single task is reported as its
own fact, never as a class conclusion.

### leo / gto, gravity

**M100**

*Problems:* S01, S02, S03, S04, S09. *Recommended:* **WH-BM14A** (score 1.057); second RKN12(10) (margin 64.5%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00), GJ8 (G2 order: observed 10.76 vs expected 10.00)

**M10**

*Problems:* S01, S02, S03, S04, S09. *Recommended:* **WH-BM14A** (score 1.290) — tie with **RKN12(10)** within 15%; second RKN12(10) (margin 10.2%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00), GJ8 (G2 order: observed 10.76 vs expected 10.00)

**M1**

*Problems:* S01, S02, S03, S04, S09. *Recommended:* **WH-BM14A** (score 1.147); second RKN12(10) (margin 23.9%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00), Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 10.76 vs expected 10.00)

### molniya / heo

**M100**

*Problems:* S05, S14. *Recommended:* **RKN12(10)** (score 1.000); second WH-BM14A (margin 32.1%).
*Excluded N/A:* none
*Excluded FAIL:* none

**M10**

*Problems:* S05, S14. *Recommended:* **RKN12(10)** (score 1.000); second WH-BM14A (margin 64.6%).
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog (level not reached at the finest control)

**M1**

*Problems:* S05, S14. *Recommended:* **RKN12(10)** (score 1.000); second DOP853-adaptive (margin 73.7%).
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog (level not reached at the finest control)

### geo

**M100**

*Not proven (one task, a fact about it only):* winner RKN12(10) at 2.262E+03, second WH-BM14A, margin 1.02×; N/A: none; FAIL: RK4 (G2 order: observed 4.76 vs expected 4.00)

**M10**

*Not proven (one task, a fact about it only):* winner WH-BM14A at 2.299E+03, second RKN12(10), margin 1.21×; N/A: none; FAIL: RK4 (G2 order: observed 4.76 vs expected 4.00); Leapfrog (level not reached at the finest control)

**M1**

*Not proven (one task, a fact about it only):* winner WH-BM14A at 3.065E+03, second RKN12(10), margin 1.03×; N/A: none; FAIL: RK4 (G2 order: observed 4.76 vs expected 4.00); Leapfrog (level not reached at the finest control)

### heliocentric / far-flight

**M100**

*Problems:* S07, S08, F01, F02. *Recommended:* **RKN12(10)** (score 1.294); second DOP853-adaptive (margin 42.3%).
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 9.07 vs expected 10.00)

**M10**

*Problems:* S07, S08, F01, F02. *Recommended:* **RKN12(10)** (score 1.123); second DOP853-adaptive (margin 50.6%).
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 9.07 vs expected 10.00)

**M1**

*Problems:* S07, S08, F01, F02. *Recommended:* **RKN12(10)** (score 1.090); second DOP853-adaptive (margin 66.3%).
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog (level not reached at the finest control), GJ8 (G2 order: observed 9.07 vs expected 10.00)

### flyby / moon-soi

**M100**

*Problems:* S12, S15, F03, F04, F05. *Recommended:* **DOPRI5-adaptive** (score 2.437) — tie with **DOP853-adaptive** within 15%; second DOP853-adaptive (margin 1.6%).
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00), Y6 (G2 order: observed 6.68 vs expected 6.00), Y6C (G2 order: observed 6.67 vs expected 6.00), GJ8 (G2 order: observed 5.71 vs expected 10.00)

**M10**

*Problems:* S12, S15, F03, F04, F05. *Recommended:* **RKN12(10)** (score 1.825) — tie with **DOP853-adaptive** within 15%; second DOP853-adaptive (margin 2.2%).
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00), Y6 (G2 order: observed 6.68 vs expected 6.00), Y6C (G2 order: observed 6.67 vs expected 6.00), GJ8 (G2 order: observed 5.71 vs expected 10.00)

**M1**

*Problems:* S12, S15, F03, F04, F05. *Recommended:* **RKN12(10)** (score 1.281) — tie with **DOP853-adaptive** within 15%; second DOP853-adaptive (margin 10.4%).
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00), DOPRI5-adaptive (G3 energy drift 6.451E-06 > 10*5.754E-07), Y6 (G2 order: observed 6.68 vs expected 6.00), Y6C (G2 order: observed 6.67 vs expected 6.00), GJ8 (G2 order: observed 5.71 vs expected 10.00)

**M0.1**

*Problems:* S12, S15, F03. *Recommended:* **DOP853-adaptive** (score 1.065) — tie with **RKN12(10)** within 15%; second RKN12(10) (margin 13.9%).
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00), Leapfrog (level not reached at the finest control)

**M0.01**

*Problems:* S12, S15, F03. *Recommended:* **DOP853-adaptive** (score 1.072) — tie with **RKN12(10)** within 15%; second RKN12(10) (margin 4.7%).
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed (G2 order: observed 5.56 vs expected 5.00), Leapfrog (level not reached at the finest control)

### j2

**M100**

*Problems:* S03, S09. *Recommended:* **WH-BM14A** (score 1.148); second RKN12(10) (margin 30.7%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00)

**M10**

*Problems:* S03, S09. *Recommended:* **RKN12(10)** (score 1.213) — tie with **WH-BM14A** within 15%; second WH-BM14A (margin 6.8%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00)

**M1**

*Problems:* S03, S09. *Recommended:* **WH-BM14A** (score 1.173) — tie with **RKN12(10)** within 15%; second RKN12(10) (margin 14.9%).
*Excluded N/A:* none
*Excluded FAIL:* RK4 (G2 order: observed 4.58 vs expected 4.00)

### drag / thrust

**M100**

*Problems:* S10, S11, S17. *Recommended:* **DOP853-adaptive** (score 1.000); second DOPRI5-adaptive (margin 55.5%).
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4 (G2 order: observed 4.63 vs expected 4.00)

**M10**

*Problems:* S10, S11, S17. *Recommended:* **DOP853-adaptive** (score 1.000); second IAS15 (margin 53.8%).
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4 (G2 order: observed 4.63 vs expected 4.00)

**M1**

*Problems:* S10, S11, S17. *Recommended:* **DOP853-adaptive** (score 1.000); second IAS15 (margin 32.2%).
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4 (G2 order: observed 4.63 vs expected 4.00)

### planets / probe-central

**M100**

*Problems:* P2, P3, P4, P5, P6. *Recommended:* **RKN12(10)** (score 1.043); second GJ8 (margin 93.3%).
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive (G5 finiteness: aborted at t=5.176E+008 s: a pair distance collapsed to 4.00E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 7.45E+008 m), Leapfrog (level not reached at the finest control)

**M10**

*Problems:* P3, P4, P5, P6. *Recommended:* **RKN12(10)** (score 1.000); second IAS15 (margin 129.6%).
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog (level not reached at the finest control)

**M1**

*Problems:* P3, P4, P5, P6. *Recommended:* **IAS15** (score 1.128); second GJ8 (margin 33.6%).
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive (level not reached at the finest control), DOP853-adaptive (level not reached at the finest control), Leapfrog (level not reached at the finest control), RKN12(10) (level not reached at the finest control)

