# GalilegoPhysicsTests

xUnit 2.6.1 (net8.0) suite for **Core only**: `Assets/_Project/Scripts/Core/**`.
The project compiles the real Core sources by glob. It deliberately contains:

- **no** `UnityStubs.cs`;
- **no** `tests/Shims`;
- **no** `UNITY_5_3_OR_NEWER` define;
- **no** `ProjectReference` to a generated Unity `.csproj`.

If Core ever takes a `UnityEngine` dependency again, this project stops
compiling. That is intentional.

The old non-Core xUnit files live in the separate `tests/GalilegoSimTests`
project (with its own local `Shims.cs`). It is intentionally outside the
Core-only build:

```powershell
dotnet test tests/GalilegoSimTests/GalilegoSimTests.csproj
```

`GalilegoSimTests` also compiles Core by the same
`..\..\Assets\_Project\Scripts\Core\**\*.cs` glob, not by a handwritten list.
It includes fast production-path `SpacecraftPhysics` + `ToleranceFrameSystem`
tests (`LocalFrameTests`) at 1 AU, including an independent hand-written RK4
reference and a local+dense segment walk. `ProductionPathTests` adds direct
checks of the full wrapper against a closed-form Kepler solution (absolute and
local frame, DOP853 and the DOPRI5 option), a local-vs-absolute frame
equivalence check, and a J2 magnitude check on `GravitySource`.

## Quick start

```powershell
# Full suite (default): ~20 s, includes long horizons and the 1-year golden
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj

# Fast loop while iterating: excludes the slow tier
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Tier!=Slow"
```

There is no environment variable gating for physics tests: every test runs
unless a trait filter excludes it. `CalibrationTests` always verifies the
existing gates; with `GALILEGO_CALIBRATE=1` it also rewrites the calibration
artifacts and may run for rows that do not yet have a `ToleranceTable` entry.

### Filters

Traits are per matrix row: `Integrator`, `Scenario`, `Tier`, `ScenarioClass`.
Every fast test class and fast StepController method carries `Tier=Fast`; slow
matrix rows, the 1-year golden, `CalibrationTests` (it sweeps the whole matrix)
and the slow StepController methods carry `Tier=Slow`.

```powershell
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Integrator=Dop853&Scenario~Kepler"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Tier=Fast"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Tier!=Slow"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "DisplayName~flyby"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~DenseOrderTests"
```

## Integrator matrix

`Integration/Matrix/IntegratorMatrixTests.cs` is one `[MatrixTheory]` over:

- integrators: `Dopri5`, `Dop853`;
- scenarios from `Support/Scenarios/ScenarioCatalog.cs`;
- tolerance levels declared by each scenario.

A new scenario is **one catalog entry**, not a new test method. The row gets
traits and runs conservation, oracle and step-budget checks.

### Step budget is mandatory

`Support/StepBudget.cs` pins the measured step count and RHS-call count for every
matrix row within ±10%. Rows with fewer than ten steps (the short thrust cases)
are pinned exactly, so a 2–3x step-control regression cannot hide behind an
absolute floor. This is a regression gate for step control, not a wall-clock
benchmark.

If `IntegratorMatrixTests` sees no budget entry, it **fails** with:

```
No step budget for <scenario | integrator | tolerance>;
run GALILEGO_CALIBRATE=1 and add the entry to StepBudget.
```

Only `GALILEGO_CALIBRATE=1` allows a missing entry. The consistency test
`StepBudgetConsistencyTests` enumerates the whole matrix (including Slow rows)
and also fails on stale entries that are no longer in the matrix.

`StepBudget.Production` is a separate DOP853 budget for the production path
(`segments == null`, as used by `SpacecraftPhysics.Step`). Dense output adds
three stages per accepted step, so the dense budget is deliberately larger; the
production budget catches RHS regressions in the game path.

### Calibration

```powershell
$env:GALILEGO_CALIBRATE="1"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~CalibrationTests"
Remove-Item Env:GALILEGO_CALIBRATE
```

`CalibrationTests` is `Tier=Slow` because it runs **all** matrix rows, including
Slow scenarios, and writes:

- `TestResults/calibration.md` - measured steps, RHS calls and errors;
- `TestResults/step-budget-snippet.cs` - ready-to-paste dense `StepBudget`
  entries;
- `TestResults/production-step-budget-snippet.cs` - ready-to-paste
  `segments == null` production-RHS entries.

Paste the snippet into `Support/StepBudget.cs`, then rerun the consistency test.

`CalibrationTests` always runs its verification pass. Without
`GALILEGO_CALIBRATE=1` a missing `ToleranceTable` row is a failure;
with it, the measured row is still emitted so a brand-new scenario can be
bootstrapped. `ToleranceTableConsistencyTests` always runs the matrix: it fails
if a gate is below the measured error or more than 4x above it, so a stale or
over-loose `ToleranceTable` cannot pass silently.

### Step controller

`StepControllerTests` checks that tightening the tolerance never reduces the step
count and strictly increases it between the extreme tolerances. Every matrix
scenario is controller-active:

- `drift-harmonic`: a 1 h harmonic oscillator with no central gravity. Step
  counts are 34/126/503 for DOPRI5 and 12/36/112 for DOP853 at
  1e-6/1e-9/1e-12.
- `restricted-3body`: a LEO test particle in the Earth-Moon restricted field.
  Step counts are 17/66 for DOPRI5 and 10/30 for DOP853 at 1e-6/1e-9.
- `restricted-flyby`: a 15,000 s close pass past a fixed second body at
  ~1.35e7 m. Step counts are 27/62/236 for DOPRI5 and 26/46/135 for DOP853 at
  1e-6/1e-9/1e-12.

`drift-linear` is exact (`a = 0`), so its error estimate is identically zero and
no controller can act on it. It is deliberately outside the matrix;
`StepCapTests` asserts that it uses the 600 s cap at every tolerance and stays
position-exact.

## Dense output order

`Integration/Dense/DenseOrderTests.cs` executes exactly one accepted step over
six step sizes for `kepler-circular` and five for `kepler-e05`, evaluates the
dense polynomial at ?=0.25/0.5/0.75 against the independent Kepler oracle, and
fits `log(error)` versus `log(h)`.

Measured slopes:

- DOPRI5: 5.16 (circular, 6 steps) and 4.95 (e05, 5 steps); gate `>= 4.7`;
- DOP853: 7.98 (circular, 4 steps) and 7.78 (e05, 5 steps); gate `>= 7.5`, the
  Hairer/Wanner contd8 segment. The circular probe stops at 75 s because a
  smaller step reaches the double-precision round-off floor.

`Integration/Dense/DenseReferenceTests.cs` compares the DOPRI5 dense polynomial
at five ? values against an independent Hairer/Shampine reference built from
`ExactRational` d1..d7 coefficients. A swap or shift of any single d coefficient
fails this test. `Dopri5_DenseCoefficientsSumToZero` is an additional cheap
identity. For DOP853, `Dop853DensePolynomialMatchesTheExtendedTableau` is a
tableau pin: it recomputes F0..F6 from the 16-stage extended tableau and `D`
coefficients and compares the dense polynomial at five ? values. Independent
accuracy evidence comes from `DenseOrderTests` and the analytic-oracle
interpolation/midpoint tests.

DOP853 dense output uses the 16-stage extended tableau: the 12 method stages,
the FSAL stage and three extra stages. That costs 3 additional RHS evaluations
per accepted step only when a dense-segment collector is supplied.
`DenseVelocityInterpolationMatchesTheOracle` also checks the dense velocity
inside segments, using three quarters of the endpoint velocity gate for the
same row (still tighter than the matrix row; half left too little margin).

## Reading a failure

Every assertion failure includes the scenario, integrator, tolerance, measured
value, step/RHS counts, a 10-row `time / |?pos| / |?vel| / energy-drift` table,
a repeat `dotnet test --filter` command, the C# snippet, and the CSV path.
`GALILEGO_DUMP=1` writes CSV artifacts for green runs too, under
`TestResults/artifacts/`.

## Tolerances

`Support/ToleranceTable.cs` is the single place for matrix assertion gates. Gates
are per `(scenario, integrator, tolerance)`, measured by
`GALILEGO_CALIBRATE=1`; each measured field carries up to 4x headroom over the
measured error. There is no class lumping and no linear `tol/1e-9` scaling.
Reversibility/chunking gates are calibrated only for the Kepler rows that use
them; other rows store 0 for those fields. Dead fields (for example
`restricted-flyby` energy/angular, which the scenario never checks) are zeroed
and the consistency test fails if a non-zero gate is left on an unmeasured
field.
`ToleranceTableConsistencyTests` enforces both sides: the measured error must
fit the gate, and the gate must stay within 4x of the measurement (with floors
for tiny mass/energy values). `AbsoluteAccuracyCeilingTests` adds hardcoded
absolute ceilings at `1e-9` and `1e-6`, tighter than the periapsis-relative
physical gate and independent of the calibration table. Those ceilings are
tight regression locks pinned to the measured error (at most 4x headroom), not
bounds derived from first principles. Do not relax a gate just to make a test
green; recalibrate with `GALILEGO_CALIBRATE=1`.

## Golden data

- `GoldenData/golden_core_kepler.json` and
  `GoldenData/golden_core_kepler_1y.json` are generated by
  `GoldenData/gen_core_kepler.py` using mpmath closed-form Kepler propagation,
  never by a test.
- The stand goldens `golden_real.json`, `golden_real_1y_hi.json` and
  `golden_real_10y_k28.json` are linked from
  `Tools/P1bTests/Study/taylor/golden` via `<None Link=...>`. They are full
  baked-system data and cannot be reproduced by Core-only dynamics; the tests
  load them and verify `P = central_P + rel_P`.
- SHA-256 checks are an **integrity pin for linked data**, not a Core physics
  gate. They hash LF-normalized bytes, so Windows CRLF and Linux LF checkouts
  agree; `.gitattributes` also keeps these golden JSON files `eol=lf`. A
  deliberate stand golden regeneration updates the hashes in the same change.
- Core golden files are SHA-256 pinned (`CoreGoldenHashesMatch`) and must
  declare generator `gen_core_kepler.py` and model `two-body, central at
  origin`. Both DOP853 and DOPRI5 are checked against them, each with its own
  absolute gates.
- The 1-year golden checks 65 interior samples through the analytic Kepler
  oracle for DOP853 (gate 1.0 m). DOPRI5 is deliberately not certified on that
  case; `Dopri5YearLongGoldenStaysFinite` requires a finite, non-stalled run at
  a coarse tolerance plus divergence bounds (within twice the orbit size and
  twice the periapsis speed), so an escape or collapse cannot pass. It is not
  an accuracy gate: DOPRI5 measures ~9,000 km of phase drift over the year.
- Core golden gates are absolute per case, not drawn from `ToleranceTable`.
  DOP853: 3.0e-7 m circular, 4.5e-7 m e=0.5, 1.4e-5 m e=0.9, 4.8e-4 m e=0.99,
  7.5e-7 m near-parabolic, 2.3e-8 m hyperbolic, 9.0e-7 m inclined, 1.0 m for
  the 1-year case. DOPRI5 uses its own measured gates (wider at high
  eccentricity). The 1-year eccentric case (a=10,000 km, e=0.5) covers about
  100 orbits and uses a 1.0 m / 1.5e-3 m/s gate (measured 0.389 m /
  5.64e-4 m/s); the 10-year circular case was removed because it cost ~59 s and
  8M steps without adding a different failure mode.
- Regenerate Core goldens with:

```powershell
cd tests/GalilegoPhysicsTests/GoldenData
python gen_core_kepler.py
```

## What remains in the stand

`Tools/P1bTests --fast` has 175 cases (the 100-year bake is Slow after its
coverage/finite-endpoint checks were added); 71 cases are Slow and 7 are
`diagnostic`. Analysis-only cases that assert nothing
(`Test112b_GradientSetAnalysis`, `Test112c_RidgedDistribution`,
`Test112d_DampSignalHistogram`, `Test125_HashAxisMultiplier`,
`Test127_HashFixField`, `Test142_ContinentsDiag`, `Test503_ReferenceConvergence`)
are `diagnostic`: they are skipped by the normal fast/slow sweeps and run only
with `--diagnostics` or `--exact`, so they cannot be counted as passing checks.
P1b also owns tests that depend on Unity-adjacent code outside Core: `Planet`, `Universe`,
`Spacecraft`, `Events`, `Ephemeris`, `Debris`, terrain, atmosphere and the
long-running benchmark/experiment matrix.

The restored legacy xUnit files are in `tests/GalilegoSimTests`; integration-level
equivalents also exist in the stand. They are not part of the Core-only project
because they need `Spacecraft`, `Universe`, `Events` and `Spacecraft.Solver`.

| Old check | Core-only analogue / restored location |
|---|---|
| `Rk4Tests` | `Rk4Tests` (Core-only, unchanged) |
| `RegressionTests` | `Integration/Kepler/LegacyParityTests` + `tests/GalilegoSimTests` |
| `OrbitAnalyticTests` | matrix `kepler-circular` + Core golden + restored project |
| `TsiolkovskyTests` | `SpacecraftIntegratorTests.ConstantThrustMatchesTsiolkovsky` + restored project |
| `MassToleranceTests` | `SpacecraftIntegratorTests.TightMassToleranceReallyDrivesTheStepSize` + restored project |
| `AltitudeCrossingTests`, `TimedEventTests`, `PriorityRestartTests`, `SoiChangeTests`, `ContactSolverTests` | restored verbatim in `tests/GalilegoSimTests` |
| P1b `Test21_Kahan` micro | `Mathematics/KahanTests` |
| P1b `Test34_GoldenState`, `Test35_KeplerEdge`, `Test39_SecularDrift` | stay in the stand (non-Core dependencies) |

Core has no full N-body integrator. `restricted-3body` (Earth-Moon LEO) and
`restricted-flyby` are production-path test particles in a restricted field.

## Known limitations

- DOP853 dense output falls back to the cubic Shampine segment if one of the
  three extended stages leaves the positive-mass domain. The accepted endpoint
  is unaffected; this only affects interpolation in that rare case.
- Every matrix gate is calibrated per row and checked for staleness; the old
  "DOP853 is less accurate than DOPRI5" limitation no longer applies.


