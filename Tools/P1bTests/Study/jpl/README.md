# Orbit validation against JPL DE441

This folder is a reproducible validation dossier for the solar-system propagator used by
*Almost Real Space*. It answers three questions:

1. **Is the force law the standard one?** The EIH implementation is checked against the
   Damour--Deruelle two-body 1PN equations. This is a two-body check only.
2. **Is the integrator converged?** The DOP853 stepper is checked against tighter tolerances,
   smaller steps, and against five other integrators. This is a self-convergence check; the
   tableau/order are checked separately by `check_dop853_order.py`.
3. **Does the real solar system come out right?** Ten years of propagation are compared with
   JPL Horizons DE441 for the planets, the Moon, the Galilean moons and Titan.

The short answer: the EIH force matches the standard two-body 1PN limit; the DOP853
integrator self-converges. The headline solar-system model is **Sun-only 1PN + 409 Newtonian
small bodies + solar J2 + Earth J2**, not the full EIH N-body force. With that model the
planet errors are 0.15--2.5 km over 10 years; the Moon is 68 km (2026--2036) and 109 km
(2016--2026); the satellites are model-limited. The full EIH N-body force is implemented and
checked in the two-body limit, but it is not the headline model: in these runs it is worse on
the Moon and inner planets for reasons that are not yet understood. Fixed-step symplectic
methods (Y6/PEFRL) are not order-consistent with the velocity-dependent 1PN force; their 1PN
rows reflect that mismatch, not the methods' merit. Model structure (solar/Earth J2, the
small-body set) was chosen after inspecting the 2026 residuals; the 2016 window checks
parameter values, not the model-selection process.

## 1. Method

### Force model

* Newtonian point masses for Sun, planets, Moon and 409 small bodies
  (343 main-belt asteroids + 30 KBOs + 36 KBO-ring masses). The small bodies are **test
  particles**: they feel the planets, but do not perturb each other.
* **Headline model: Sun-only 1PN test-particle force.** Each planet/Moon gets the 1PN
  acceleration from the Sun only (`JplAcc1Pn`). Mutual 1PN terms are not included. This is
  the model behind the headline `full409` table.
* **Full EIH:** the N-body 1PN force (`JplAccEih`, Moyer/Standish form) is implemented and
  checked in the two-body limit. It is not used in the headline results.
* Solar J2; Earth J2 for the Moon; the satellite test adds J2/J4/J6 for Jupiter/Saturn,
  J2 of the Galilean moons/Titan, and J2 of the Moon.
* Small-body masses are the exact DE441 values from JPL IOM 392R-21-005, Table 1
  (`de441_asteroids.csv`); the states are JPL Horizons DE441.

### Integrator

`JplDop853` is a 12-stage Dormand--Prince 8(5,3) adaptive integrator (DOP853). The
production game integrator (`SpacecraftOrbitIntegrator`, DOPRI5) is a separate code; this
dossier validates the study/propagator DOP853 and compares it with the fixed-step
DOPRI5/RK4/PEFRL/Y6 implementations.

### Oracle

JPL Horizons DE441 (the same ephemeris as DE440 for the modern era). The files
`horizons_real_2026_10y.json` and `horizons_real_2016_10y.json` are the oracle; the 2016
window is a second epoch. The initial states, GMs and small-body masses are JPL values; the
model structure was selected after looking at the 2026 residuals, so 2016 is a holdout for
the parameter values, not for the model structure.

## 2. Evidence and scope

### 2.1 The two-body 1PN limit matches Damour--Deruelle

`Test536_EihTwoBodyDd` compares `JplAccEih` with the Damour--Deruelle relative 1PN
acceleration on 2000 random two-body states in the centre-of-mass frame:

```
trials=2000  worst relative error vs DD: 1.034E-015
PASS EIH matches Damour-Deruelle 1PN
```

The error is at double-precision level. This verifies the two-body coefficients and signs
(including the `a_B` and `7/2` terms); it does **not** test the N-body terms
(`-4*pot[A]`, `-pot[B]`, cross accelerations), conservation laws, or boost invariance. The
full EIH force is therefore "two-body-verified", not "N-body-verified".

### 2.2 The adaptive integrator self-converges

`Test541_JplConvergence` repeats the 2026 ten-year Sun-only 1PN run with:

* baseline: `rtol=1e-13`, `maxStep=86400 s`;
* `rtol=1e-14`;
* `maxStep=43200 s`;
* both.

All four give the same Jupiter/Saturn/Mars errors to 0.000 km. This is a **self-convergence**
check: an adaptive controller can hide a wrong coefficient by shrinking the step. The
`check_dop853_order.py` script in this folder independently checks the tableau: row sums of A
equal C, `sum(B)=1`, `sum(E5)=0`, and `sum(B*C^k)=1/(k+1)` for k=0..7; it also runs a
fixed-step Kepler orbit (e=0.1, one revolution) against the exact Kepler-equation solution.
The empirical order is 7.58 -> 7.74 -> 7.83 -> 7.90 (converging to 8; the finest pairs hit
round-off), and the embedded E5 estimate scales as h^6. The script is pure Python (no numpy).
It does not exercise the C# `SpacecraftOrbitIntegrator`. `Test547_SpacecraftDop853` does:
it runs the production `StepForwardDop853` on a Kepler orbit (DOP853 1.6e-14 m vs DOPRI5
9.9e-11 m) and checks the dense output. A C# fixed-step order test is still missing.

`Test539` and `Test542` show that the day-0 relative states are exact (0 m; 4.7e-10 after the
GM/unit combination), and `Test540` checks the initial accelerations and the Mars-moon mass
ratio.

### 2.3 The real system against DE441

`Test537_JplFullComparison` (2026--2036) and `Test538_JplHoldout` (2016--2026) propagate the
full system and compare with Horizons. Position error in km:

| body | 2026 10-body 1PN | 10-body EIH | 409small (Newtonian) | full409 | 2016 10-body 1PN | 10-body EIH | 409small (Newtonian) | full409 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Mercury | 0.50 | 0.94 | 0.57 | 0.57 | 1.16 | 1.76 | 1.20 | 0.73 |
| Venus | 0.43 | 0.06 | 0.15 | 0.45 | 0.03 | 0.06 | 0.15 | 0.15 |
| Earth | 0.95 | 1.60 | 2.02 | 2.50 | 2.53 | 2.90 | 1.97 | 1.57 |
| Mars | 1.14 | 1.33 | 0.23 | 0.28 | 4.13 | 4.00 | 0.13 | 0.17 |
| Jupiter | 17.01 | 17.12 | 0.15 | 0.15 | 1.06 | 1.05 | 0.32 | 0.32 |
| Saturn | 10.41 | 10.43 | 0.50 | 0.50 | 2.13 | 2.04 | 2.47 | 2.47 |
| Uranus | 0.91 | 0.93 | 0.80 | 0.80 | 0.52 | 0.49 | 1.01 | 1.01 |
| Neptune | 6.47 | 6.49 | 1.58 | 1.58 | 5.07 | 5.06 | 2.17 | 2.17 |
| Moon (geo) | 177.15 | 222.67 | 177.15 | 68.08 | 160.51 | 202.46 | 160.51 | 108.74 |
| **RMS(8)** | **7.44** | **7.50** | **0.99** | **1.13** | **2.66** | **2.71** | **1.47** | **1.37** |

The `1PN` and `EIH` columns are the 10-body model (`nb10`, barycentric Sun) without the small
bodies. `409small` is Newtonian + the 409 small bodies; `full409` is Sun-only 1PN + 409 +
solar J2 + Earth J2. EIH+409 and 1PN+409 were **not run**; the table cannot support an
EIH-with-small-bodies or 1PN-with-small-bodies conclusion.

Three things in this table matter:

* **The full EIH is worse than the 10-body 1PN** in this comparison, especially for the Moon
  (177 -> 223 km in 2026, 161 -> 202 km in 2016), Earth (0.95 -> 1.60) and Mercury (0.50 ->
  0.94). The formula was checked by hand against Moyer/Standish and the two-body test passes;
  the N-body discrepancy is unexplained. EIH with the 409 small bodies was not run.
* **The full409 is not uniformly better than the 10-body 1PN.** It reduces Jupiter from 17.0
  to 0.15 km and Saturn from 10.4 to 0.50 km, but Earth goes from 0.95 to 2.50 km: the
  asteroids take it 0.95 -> 2.02 and solar J2 adds about 0.5. Mercury goes 0.50 -> 0.57 and
  Venus 0.43 -> 0.45. The RMS(8) improves from 7.44 to 1.13, driven by Jupiter/Saturn; the
  Earth residual is dominated by model choices, not by the integrator.
* **The Moon's 177 -> 68 km is entirely Earth J2.** The `409small` column leaves it at
  177.15 km; adding Earth J2 (`409small+EJ2`) gives 68.08 km, and `full409` keeps 68.08 km.
  Solar J2 alone does not help the Moon. In 2026 solar J2 slightly worsens RMS
  (0.99 -> 1.13 with both J2; 1.09 without solar J2) and Venus (0.15 -> 0.45).

The 2016 window does not reproduce every 2026 improvement: Saturn 2.13 -> 2.47 km and Uranus
0.52 -> 1.01 km are worse in the holdout; Jupiter's 17.0 -> 0.15 km improvement is a 2026
effect (2016: 1.06 -> 0.32 km).

`Test531` is a legacy heliocentric run (`barycentric=false`, Sun fixed): its 1PN Mercury is
5.27 km and Jupiter 18.75 km, while the 2026 10-body 1PN in this table is 0.50/17.01 km. The
difference is the initial configuration, not the small bodies; the `409small` Mercury is
0.57 km. `Test531`'s 50/100 km gates are obsolete; it should be migrated to the barycentric
configuration or marked legacy.

### 2.4 Other integrators on the same full system

`Test546_JplFullSystemIntegrators` runs every study integrator on the same 409-small-body,
Sun-only 1PN, J2 force and compares with Horizons:

| model | step | calls | time | RMS(8) km | Jupiter km | Saturn km | Moon km |
|---|---:|---:|---:|---:|---:|---:|---:|
| **DOP853 full409** | adaptive | 156 947 | 16.5 s | **1.13** | 0.150 | 0.505 | **68.08** |
| DOPRI5 full409 | 0.5 d | 43 801 | 4.0 s | 129.2 | 0.150 | 0.505 | 1 244.6 |
| Y6 full409 | 0.5 d | 51 101 | 4.0 s | 9 731 | 0.277 | 0.505 | 156.7 |
| RK4 full409 | 0.5 d | 29 200 | 2.3 s | 6 525 | 0.147 | 0.505 | 122 183 |
| PEFRL full409 | 0.5 d | 29 200 | 2.3 s | 6 870 | 0.238 | 0.505 | 1 292.7 |
| DOPRI5 full409 | 1 d | 21 901 | 2.1 s | 2 912 | 0.151 | 0.504 | 67 949 |
| Y6 full409 | 1 d | 25 551 | 2.1 s | 19 492 | 0.407 | 0.506 | 5 745 |
| PEFRL full409 | 1 d | 14 600 | 1.2 s | 15 195 | 0.329 | 0.505 | 20 208 |
| RK4 full409 | 1 d | 14 600 | 1.1 s | 206 769 | 0.057 | 0.519 | 409 803 |

This table compares the full model **against Horizons**, so it mixes integrator error with
model error. Small per-planet wins can be accidental: RK4 at 1 d gives Jupiter 0.057 km, better
than DOP853's 0.150 km, because the RK4 error happens to cancel part of the model error. Use
`Test545` (same-force reference) for integrator quality; use this table only as a full-system
sanity check.

The 16.5 s for DOP853 is **not** caused by taking more steps: `Test545` and `Test546` both
make 156 947 force calls. The full run is slower because each call is a 10 x 409 pair loop.
The fixed-step methods make fewer calls but do not reach the model accuracy; Y6 at 0.5 d is
still 9 731 km RMS because of the 1PN splitting mismatch described below.

### 2.5 The integrator itself, without the small-body noise

`Test545_JplIntegratorComparison` uses the ten-body problem and a tight DOP853 reference.
The `force` column matters: Y6/PEFRL are symplectic splitting methods designed for
velocity-independent forces; the 1PN force depends on velocity, so their 1PN rows are not a
fair measure of the methods.

| integrator | force | step | calls | time | max error |
|---|---|---:|---:|---:|---:|
| DOP853 adaptive | Newtonian | adaptive | 156 947 | 307 ms | 0.003 km |
| DOP853 1PN | 1PN | adaptive | 156 947 | 333 ms | 0.001 km |
| Y6 | Newtonian | 0.5 d | 51 101 | 60 ms | 88 km |
| Y6 | Newtonian | 1 d | 25 551 | 30 ms | 5 534 km |
| DOPRI5 | Newtonian | 0.5 d | 43 801 | 65 ms | 1 260 km |
| DOPRI5 | Newtonian | 1 d | 21 901 | 32 ms | 67 092 km |
| PEFRL | Newtonian | 0.5 d | 29 200 | 33 ms | 1 247 km |
| PEFRL | Newtonian | 1 d | 14 600 | 16 ms | 19 860 km |
| RK4 | Newtonian | 0.5 d | 29 200 | 33 ms | 120 665 km |
| RK4 | Newtonian | 1 d | 14 600 | 16 ms | 584 811 km |
| Y6 1PN | 1PN | 112.5 s | n/a | 38 347 ms | 71.6 km |

For **Newtonian** forces the halving of `h` follows the theoretical order: Y6 5 534 -> 88 km
(63x, 6th order), PEFRL 19 860 -> 1 247 (16x, 4th), DOPRI5 67 092 -> 1 260 (53x, faster than
2^5 = 32, pre-asymptotic). The
Newtonian fixed-step methods are not broken; the step is too coarse for Mercury/the Moon.

For the **1PN** force this no longer holds. Y6 at 112.5 s (3 000x smaller than 0.5 d) still
gives 71.6 km, no better than Newtonian Y6 at 0.5 d (88 km). `Test531` labels this a
**splitting artifact**: the 1PN acceleration depends on velocity, so the symplectic splitting
loses its order. `Test546` shows the same effect in the full system: Y6 at 0.5 d has RMS
9 731 km versus DOPRI5's 129 km at the same step. The Y6/PEFRL 1PN rows therefore show a
method-force incompatibility, not a lack of step resolution. "Use h <= 0.5 d and a high-order
method" is not a valid fix for this.

### 2.6 The satellites

`Test544_JplSatelliteComparison` (2026--2036) with J2/J4/J6 of Jupiter/Saturn, J2 of the
moons and J2 of the Moon:

| body | error |
|---|---:|
| Moon | 68.07 km |
| Io | 13 233 km |
| Europa | 14 122 km |
| Ganymede | 9 157 km |
| Callisto | 1 127 km |
| Mimas | 115 943 km |
| Titan | 124 325 km |
| Iapetus | 7 420 km |

The satellites are a separate, much more sensitive problem: close orbits, non-spherical
planets, Laplace/Titan--Hyperion resonances, tides and moon gravity fields. The J4/J6
addition alone improved Mimas from 8.9e6 km to 1.2e5 km and Io from 52 000 to 13 000 km.
The Moon needs the full lunar model (C22/lunar gravity field/core/tides/1PN Earth--Moon).

The Earth J2 term acts only on the Moon; `JplAccEarthJ2` accepts `moonMu` but does not use it,
and there is no reaction `a_Earth -= (mu_Moon/mu_Earth) a_Moon`. The solar J2 uses the
ecliptic z-axis, while the Sun's rotation axis is tilted by about 7 deg. Both are model
limitations; the Earth-reaction effect has not been computed.

### 2.7 The oracle itself

The downloaded JPL SPK kernels (`de440s`, `jup365`, `sat441`) were evaluated with SPICE and
matched the Horizons JSON to 0.000--0.002 km for the Earth, the Moon, Jupiter, Saturn, the
Galilean moons and Titan. The SPK files are not part of the repository (large); the JSON is
the same DE441 ephemeris, so the comparison is offline and reproducible.

## 3. What the evidence supports

* The force is not invented here: `JplAccEih` is the standard EIH N-body 1PN acceleration;
  its two-body limit agrees with Damour--Deruelle at 1e-15. The headline model is the simpler
  Sun-only 1PN term, not the full EIH.
* DOP853 is a standard 8(5,3) Dormand--Prince method; its adaptive error control is tested by
  tightening the tolerance and halving the step (no change to 0.000 km). The tableau and
  fixed-step order are checked by `check_dop853_order.py`; the C# stepper is not.
* The initial states, GMs and small-body masses are JPL DE441/Horizons/IOM values. The model
  structure (solar/Earth J2, the 409-body set) was selected after inspecting the 2026
  residuals, so this is not a blind prediction; 2016 is a holdout for the parameter values.
* The same model is tested on the 2016--2026 window; the RMS is similar (1.37 vs 1.13 km), but
  not every body improves (Saturn 2.13 -> 2.47, Uranus 0.52 -> 1.01), so the 2026 agreement
  is not a uniform success.
* The measured residuals respond to physical model terms: adding the 409 small bodies reduces
  Jupiter from 17.0 to 0.15 km and Saturn from 10.4 to 0.50 km in 2026; adding Earth J2
  reduces the Moon from 177 to 68 km. The 2016 Jupiter improvement is smaller (1.06 -> 0.32),
  and solar J2 slightly worsens Venus (0.15 -> 0.45) and RMS (1.09 -> 1.13) in 2026. The
  residual Mercury error (~0.6 km) is attributed to model error; it is not isolated. It is
  not an integrator error (Test545 DOP853 gives 0.003 km).

## 4. Limitations and open issues

This is a validation of a 1PN, point-mass/test-particle model, not a claim to reproduce
DE441 at the metre level for every body.

* The headline model is Sun-only 1PN. It ignores mutual 1PN terms. The full EIH is
  implemented and passes the two-body check, but it is worse in the 10-body comparison
  (Moon/inner planets); the reason is not understood. EIH with the 409 small bodies was not
  run. Its N-body terms are not covered by Test536.
* 1PN is a truncation; the harmonic-gauge/test-particle terms and the DE441 PPN coordinate
  conventions leave a few-km-level residual on the inner planets and the Earth/Moon.
* The 409 small bodies are test particles (no mutual perturbations); the KBO ring is a
  smooth 36-mass ring rather than the exact DE441 particle phases.
* The Moon uses Earth J2 only; there is no Earth reaction to the Moon's J2 acceleration
  (`moonMu` is unused), and solar J2 uses the ecliptic z-axis rather than the Sun's ~7-deg
  axis. The full lunar model (C22, lunar gravity field, core, tides, Earth--Moon 1PN, SRP,
  geodetic precession) is required for the 1 km lunar target.
* The Galilean/Saturn satellites need their full satellite theories; the current model is
  a first-order reproduction, not the JPL satellite ephemerides.
* The 1 km-per-object target is not met for the Moon and the satellites; it is met for most
  planets (Jupiter 0.15 km, Saturn 0.50 km, Mars 0.28 km, Mercury 0.57 km) in 2026, but
  Saturn 2.47 km in 2016.
* **Metric:** `RMS(8)` is the RMS of the eight planets at the final 10-year endpoint, from a
  file sampled once per year. It is not an RMS over all 11 annual samples. Mercury (88 d) and
  the Moon (27 d) are phase-sensitive at 365 d sampling; a 10--30 d grid and max/RMS over all
  samples would be a stronger test. The comparison is heliocentric, so the Sun's error is
  distributed across all planets.
* **Test541 is self-convergence**, not a proof of the DOP853 order. `check_dop853_order.py`
  validates the tableau and the fixed-step Kepler order (7.6--7.9 -> 8), but not a C#
  fixed-step order test. `Test547_SpacecraftDop853` checks the production
  `StepForwardDop853` on a Kepler orbit and its dense output. An independent Taylor-code
  cross-check is also missing.
* **No N-body EIH conservation/boost test.** Energy, momentum, centre-of-mass and boost
  invariance are not checked; a three-body comparison against an independent EIH
  implementation (e.g. REBOUNDx) is not in the repository.
* **Test546 mixes model and integrator error.** It uses Horizons as the reference, so
  per-planet improvements can be accidental (RK4 Jupiter 0.057 km vs DOP853 0.150 km). Use
  Test545 for same-force integrator comparisons; an error-vs-force-calls plot is not yet
  included, although the calls columns provide the data.
* **The DOP853 `rtol=1e-13` is conservative.** The integrator error is 0.003 km against a
  model error of ~1 km; `rtol=1e-10...1e-11` may give the same RMS much faster. This has not
  been tested.
* **Model selection is not blind.** The 2026 residuals motivated solar/Earth J2 and the
  409-body set; the 2016 window checks their values, not the choice of terms.

## 5. Reproduce

Run from `Tools/P1bTests` (the tests use paths relative to that directory):

```powershell
cd Tools/P1bTests

dotnet run --project P1bTests.csproj -- --exact Test536_EihTwoBodyDd
dotnet run --project P1bTests.csproj -- --exact Test541_JplConvergence
dotnet run --project P1bTests.csproj -- --exact Test537_JplFullComparison
dotnet run --project P1bTests.csproj -- --exact Test538_JplHoldout
dotnet run --project P1bTests.csproj -- --exact Test545_JplIntegratorComparison
dotnet run --project P1bTests.csproj -- --exact Test546_JplFullSystemIntegrators
dotnet run --project P1bTests.csproj -- --exact Test544_JplSatelliteComparison
dotnet run --project P1bTests.csproj -- --exact Test547_SpacecraftDop853
dotnet run --project P1bTests.csproj -- --exact Test548_Bake100y
```

The tableau/order check is pure Python:

```powershell
python Study\jpl\check_dop853_order.py
```

`Test537`, `Test538` and `Test546` use all cores (`Parallel.For`; `JPL_JOBS` caps it).
The full JPL suite can be run with:

```powershell
dotnet run --project P1bTests.csproj -- --from 530 --to 546 --parallel --log-dir Study\jpl\results\parallel
```

## 6. Data and files

* `horizons_real_2026_10y.json`, `horizons_real_2016_10y.json` вЂ” DE441 states
  (10 years, 1-year sampling) from JPL Horizons.
* `horizons_short_2026.json`, `horizons_short_2016.json` вЂ” 0/1/10/30/90/180/365-day
  states for the short-horizon checks.
* `de441_asteroids.csv` вЂ” 343 main-belt numbers and exact DE441 GMs (au^3/day^2), from
  JPL IOM 392R-21-005 Table 1.
* `fetch_horizons.py`, `fetch_holdout.py`, `fetch_short.py`, `add_asteroids.py`,
  `add_n16.py`, `add_top50.py`, `add_kbos.py`, `add_kbo_ring.py` вЂ” data builders.
* `Jpl1Pn.cs` вЂ” Newtonian/1PN/EIH/J2 accelerations and DOP853.
* `EihUnit.cs` вЂ” `Test536` (EIH vs DD, two-body only).
* `FullComparison.cs` вЂ” `Test537`/`538`/`539`/`540`/`541`/`542`/`543`/`544`.
* `RingBelt.cs` вЂ” asteroid-ring/combined belt models.
* `IntegratorComparison.cs` вЂ” `Test545`.
* `FullSystemIntegrators.cs` вЂ” `Test546` and generic fixed-step acceleration paths.
* `SpacecraftDop853.cs` вЂ” `Test547` production `StepForwardDop853` and dense output.
* `..\..\Assets\_Project\Scripts\Core\Integration\DOP853Coefficients.cs` вЂ” production DOP853 tableau.
* `..\..\Assets\_Project\Scripts\Core\Integration\SpacecraftOrbitIntegrator.cs` вЂ” `StepForwardDop853`.
* `..\..\Assets\_Project\Scripts\Spacecraft\SpacecraftPhysics.cs` вЂ” DOP853 default, DOPRI5 option.
* `check_dop853_order.py` вЂ” independent tableau/order check (row sums, order conditions,
  fixed-step Kepler order, E5 h^6); pure Python, no numpy.
* `results/` вЂ” logs and the parallel-suite run.

Data provenance: NASA/JPL Horizons DE441 and the JPL Planetary Ephemeris DE440/DE441
(Park et al. 2021); asteroid/KBO masses from JPL IOM 392R-21-005 (Farnocchia 2021).
NASA/JPL data are in the public domain. The source code is part of *Almost Real Space*.
