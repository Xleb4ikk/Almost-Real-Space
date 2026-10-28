# Orbit validation against JPL DE441

This folder is a reproducible validation dossier for the solar-system propagator used by
*Almost Real Space*. It answers three questions:

1. **Is the force law the standard one?** The 1PN/EIH implementation is checked against the
   Damour--Deruelle two-body 1PN equations.
2. **Is the integrator converged?** The DOP853 stepper is checked against tighter tolerances,
   smaller steps, and against five other integrators.
3. **Does the real solar system come out right?** Ten years of propagation are compared with
   JPL Horizons DE441 for the planets, the Moon, the Galilean moons and Titan.

The short answer: the equations and the integrator are correct; the remaining error is the
physical model (1PN truncation, gauge, lunar/satellite model, small-body completeness), not a
broken stepper. The measured planet errors are 0.15--2.5 km over 10 years; the Moon is 68 km
(2026--2036) and 109 km (2016--2026); the satellites are model-limited.

## 1. Method

### Force model

* Newtonian point masses for Sun, planets, Moon and 409 small bodies
  (343 main-belt asteroids + 30 KBOs + 36 KBO-ring masses).
* 1PN acceleration from the Sun for each planet (the `1PN` model) and the full EIH N-body
  1PN implementation (`EIH`).
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
window is a held-out epoch. No parameter was fitted to either window.

## 2. What is proven

### 2.1 The 1PN force is the standard EIH/Damour--Deruelle force

`Test536_EihTwoBodyDd` compares `JplAccEih` with the Damour--Deruelle relative 1PN
acceleration on 2000 random two-body states in the centre-of-mass frame:

```
trials=2000  worst relative error vs DD: 1.034E-015
PASS EIH matches Damour-Deruelle 1PN
```

The error is at double-precision level, so the coefficients and signs of all EIH terms
(including the `a_B` term and the `7/2` term) are the standard ones, not a fitted variant.

### 2.2 The integrator is converged

`Test541_JplConvergence` repeats the 2026 ten-year 1PN run with:

* baseline: `rtol=1e-13`, `maxStep=86400 s`;
* `rtol=1e-14`;
* `maxStep=43200 s`;
* both.

All four give the same Jupiter/Saturn/Mars errors to 0.000 km. `Test539` and `Test542`
show that the day-0 relative states are exact (0 m; 4.7e-10 after the GM/unit combination),
and `Test540` checks the initial accelerations and the Mars-moon mass ratio.

### 2.3 The real system matches DE441

`Test537_JplFullComparison` (2026--2036) and `Test538_JplHoldout` (2016--2026, independent
epoch) propagate the full system and compare with Horizons. Position error in km:

| body | 2026 1PN | 2026 full409 | 2016 1PN | 2016 full409 |
|---|---:|---:|---:|---:|
| Mercury | 0.50 | 0.57 | 1.16 | 0.73 |
| Venus | 0.43 | 0.45 | 0.03 | 0.15 |
| Earth | 0.95 | 2.50 | 2.53 | 1.57 |
| Mars | 1.14 | 0.28 | 4.13 | 0.17 |
| Jupiter | 17.01 | 0.15 | 1.06 | 0.32 |
| Saturn | 10.41 | 0.50 | 2.13 | 2.47 |
| Uranus | 0.91 | 0.80 | 0.52 | 1.01 |
| Neptune | 6.47 | 1.58 | 5.07 | 2.17 |
| Moon (geo) | 177.15 | 68.08 | 160.51 | 108.74 |
| **RMS(8)** | **7.44** | **1.13** | **2.66** | **1.37** |

"full409" is the model with the 409 small bodies, 1PN, solar J2 and Earth J2. The improvement
from 1PN to full409 is the real asteroid/KBO ring mass and the solar J2; the held-out epoch
shows that the result is not a fit to 2026.

### 2.4 Other integrators on the same full system

`Test546_JplFullSystemIntegrators` runs every study integrator on the same 409-small-body,
1PN, J2 force and compares with Horizons:

| model | step | time | RMS(8) km | Jupiter km | Saturn km | Moon km |
|---|---:|---:|---:|---:|---:|---:|
| **DOP853 full409** | adaptive | 16.5 s | **1.13** | 0.150 | 0.505 | **68.08** |
| DOPRI5 full409 | 0.5 d | 4.0 s | 129.2 | 0.150 | 0.505 | 1 244.6 |
| Y6 full409 | 0.5 d | 4.0 s | 9 731 | 0.277 | 0.505 | 156.7 |
| RK4 full409 | 0.5 d | 2.3 s | 6 525 | 0.147 | 0.505 | 122 183 |
| PEFRL full409 | 0.5 d | 2.3 s | 6 870 | 0.238 | 0.505 | 1 292.7 |
| DOPRI5 full409 | 1 d | 2.1 s | 2 912 | 0.151 | 0.504 | 67 949 |
| Y6 full409 | 1 d | 2.1 s | 19 492 | 0.407 | 0.506 | 5 745 |
| PEFRL full409 | 1 d | 1.2 s | 15 195 | 0.329 | 0.505 | 20 208 |
| RK4 full409 | 1 d | 1.1 s | 206 769 | 0.057 | 0.519 | 409 803 |

Jupiter and Saturn are nearly integrator-independent (0.15/0.50 km); the differences are on
Mercury and the Moon. DOP853 is the only stepper that keeps all planets and the Moon at the
model's accuracy. The fixed-step methods need `h <= 0.5 d` and a high order (Y6) to approach
the inner-system accuracy.

### 2.5 The integrator itself, without the small-body noise

`Test545_JplIntegratorComparison` uses the ten-body 1PN problem and a tight DOP853 reference:

| integrator | step | time | max error |
|---|---:|---:|---:|
| DOP853 adaptive | adaptive | 307 ms | 0.003 km |
| DOP853 1PN | adaptive | 333 ms | 0.001 km |
| Y6 | 0.5 d | 60 ms | 88 km |
| Y6 | 1 d | 30 ms | 5 534 km |
| DOPRI5 | 0.5 d | 65 ms | 1 260 km |
| PEFRL | 0.5 d | 33 ms | 1 247 km |
| Y6 1PN | 112.5 s | 38 347 ms | 71.6 km |
| RK4 | 1 d | 16 ms | 584 811 km |

The halving of `h` follows the theoretical order: Y6 5 534 -> 88 km (63x, 6th order),
PEFRL 19 860 -> 1 247 (16x, 4th), DOPRI5 67 092 -> 1 260 (53x, 5th). The fixed-step
integrators are not broken; the step is simply too coarse for Mercury/the Moon.

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

### 2.7 The oracle itself

The downloaded JPL SPK kernels (`de440s`, `jup365`, `sat441`) were evaluated with SPICE and
matched the Horizons JSON to 0.000--0.002 km for the Earth, the Moon, Jupiter, Saturn, the
Galilean moons and Titan. The SPK files are not part of the repository (large); the JSON is
the same DE441 ephemeris, so the comparison is offline and reproducible.

## 3. Why it should work

* The force is not invented here: `JplAccEih` is the standard EIH N-body 1PN acceleration;
  its two-body limit agrees with Damour--Deruelle at 1e-15.
* DOP853 is a standard 8(5,3) Dormand--Prince method; its error control is tested by
  tightening the tolerance and halving the step (no change to 0.000 km).
* The initial states, GMs and small-body masses are JPL DE441/Horizons/IOM values, not
  fitted to the residuals.
* The same model is tested on an independent 2016--2026 window; the results are similar,
  so the 2026 agreement is not a coincidence.
* The measured residuals scale with the physical model: adding the 409 small bodies reduces
  Jupiter from 17.0 km to 0.15 km and Saturn from 10.4 km to 0.50 km; adding Earth J2
  reduces the Moon from 177 km to 68 km. The residual Mercury error (~0.6 km) is the
  1PN/gauge/model floor, not an integrator error.

## 4. Limitations

This is a validation of a 1PN, point-mass/test-particle model, not a claim to reproduce
DE441 at the metre level for every body.

* 1PN is a truncation; the harmonic-gauge/test-particle terms and the DE441 PPN coordinate
  conventions leave a few-km-level residual on the inner planets and the Earth/Moon.
* The 409 small bodies are test particles (no mutual perturbations); the KBO ring is a
  smooth 36-mass ring rather than the exact DE441 particle phases.
* The Moon uses Earth J2 only; the full lunar model (C22, lunar gravity field, core,
  tides, Earth--Moon 1PN, SRP, geodetic precession) is required for the 1 km lunar target.
* The Galilean/Saturn satellites need their full satellite theories; the current model is
  a first-order reproduction, not the JPL satellite ephemerides.
* The 1 km-per-object target is not met for the Moon and the satellites; it is met for most
  planets (Jupiter 0.15 km, Saturn 0.50 km, Mars 0.28 km, Mercury 0.57 km).

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
```

`Test537`, `Test538` and `Test546` use all cores (`Parallel.For`; `JPL_JOBS` caps it).
The full JPL suite can be run with:

```powershell
dotnet run --project P1bTests.csproj -- --from 530 --to 546 --parallel --log-dir Study\jpl\results\parallel
```

## 6. Data and files

* `horizons_real_2026_10y.json`, `horizons_real_2016_10y.json` — DE441 states
  (10 years, 1-year sampling) from JPL Horizons.
* `horizons_short_2026.json`, `horizons_short_2016.json` — 0/1/10/30/90/180/365-day
  states for the short-horizon checks.
* `de441_asteroids.csv` — 343 main-belt numbers and exact DE441 GMs (au^3/day^2), from
  JPL IOM 392R-21-005 Table 1.
* `fetch_horizons.py`, `fetch_holdout.py`, `fetch_short.py`, `add_asteroids.py`,
  `add_n16.py`, `add_top50.py`, `add_kbos.py`, `add_kbo_ring.py` — data builders.
* `Jpl1Pn.cs` — Newtonian/1PN/EIH/J2 accelerations and DOP853.
* `EihUnit.cs` — `Test536` (EIH vs DD).
* `FullComparison.cs` — `Test537`/`538`/`539`/`540`/`541`/`542`/`543`/`544`.
* `RingBelt.cs` — asteroid-ring/combined belt models.
* `IntegratorComparison.cs` — `Test545`.
* `FullSystemIntegrators.cs` — `Test546` and generic fixed-step acceleration paths.
* `results/` — logs and the parallel-suite run.

Data provenance: NASA/JPL Horizons DE441 and the JPL Planetary Ephemeris DE440/DE441
(Park et al. 2021); asteroid/KBO masses from JPL IOM 392R-21-005 (Farnocchia 2021).
NASA/JPL data are in the public domain. The source code is part of *Almost Real Space*.
