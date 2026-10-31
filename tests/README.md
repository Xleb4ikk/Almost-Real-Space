# GalilegoPhysicsTests

xUnit 2.6.1 (net8.0) suite for **Core only**: `Assets/_Project/Scripts/Core/**`.
The project compiles the real Core sources by glob and links the two independent
Taylor oracle files from `Tools/P1bTests/Reference` (`TaylorJet.cs`,
`TaylorNBody.cs`). It deliberately contains:

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

## Quick start

```powershell
# Fast tier (default): whole suite well under 15 s, every test < 0.5 s
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj

# Slow tier: long horizons, 10-year golden, Pythagorean oracle
$env:GALILEGO_SLOW="1"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj
Remove-Item Env:GALILEGO_SLOW
```

Slow tests are `[SlowFact]`/`[SlowTheory]` and are reported as skipped with the
reason `GALILEGO_SLOW=1` until the variable is set. The integrator matrix marks
slow rows skipped the same way.

### Filters

Traits are per matrix row: `Integrator`, `Scenario`, `Tier`, `ScenarioClass`.
Every fast test class also carries `Tier=Fast`; slow tests carry `Tier=Slow`.

```powershell
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Integrator=Dop853&Scenario~Kepler"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Tier=Fast"
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
matrix row within ±10%. This is a regression gate for step control, not a
wall-clock benchmark.

If `IntegratorMatrixTests` sees no budget entry, it **fails** with:

```
нет бюджета шагов для <scenario | integrator | tolerance>;
запустите GALILEGO_CALIBRATE=1 и добавьте строку в StepBudget.
```

Only `GALILEGO_CALIBRATE=1` allows a missing entry. The consistency test
`StepBudgetConsistencyTests` enumerates the whole matrix (including Slow rows)
and also fails on stale entries that are no longer in the matrix.

### Calibration

```powershell
$env:GALILEGO_CALIBRATE="1"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~CalibrationTests"
Remove-Item Env:GALILEGO_CALIBRATE
```

Calibration runs **all** matrix rows, including Slow scenarios, and writes:

- `TestResults/calibration.md` — measured steps, RHS calls and errors;
- `TestResults/step-budget-snippet.cs` — ready-to-paste
  `StepBudget` dictionary entries.

Paste the snippet into `Support/StepBudget.cs`, then rerun the consistency test.

### Step controller and bounded scenarios

`StepControllerTests` checks that tightening the tolerance never reduces the step
count and strictly increases it between the extreme tolerances. Explicit
`ControllerBound` exceptions are printed and listed here:

- `drift-linear`: `a = 0`; the integrators are exact, so the error estimate is
  zero and the step is pinned at `DefaultMaxStepSize = 600 s` for every
  tolerance.
- `restricted-3body`: over the 1-hour arc the error estimate stays below the
  scale, so every tolerance accepts the 600 s cap. This pins the cap, not the
  controller. Its perturber contribution is still ~2.0e3 m, far above the
  oracle gate.

`restricted-flyby` is the active-controller counterpart: a 15,000 s close pass
past a fixed second body at ~1.35e7 m. Step counts for DOPRI5 are
27/62/236 at 1e-6/1e-9/1e-12; DOP853 gives 26/46/135.

## Dense output order

`Integration/Dense/DenseOrderTests.cs` executes exactly one accepted step for
`h = 600, 300, 150, 75 s`, evaluates the dense polynomial at θ=0.25/0.5/0.75
against the independent Kepler oracle, and fits `log(error)` versus `log(h)`.

Measured slopes:

- DOPRI5: ~5.31 (gate `>= 4.7`);
- DOP853: ~4.0 (gate `4.0 ± 0.3`), marked
  `KnownLimitation=CubicDense`.

`Integration/Dense/DenseReferenceTests.cs` compares the DOPRI5 dense polynomial
at five θ values against an independent Hairer/Shampine reference built from
`ExactRational` d1..d7 coefficients. A swap or shift of any single d coefficient
fails this test. `Dopri5_DenseCoefficientsSumToZero` is an additional cheap
identity.

For DOP853 the dense segment is cubic (`C4 = 0`); full DOP853 dense output is a
separate production follow-up, not a test-side fix.

## Reading a failure

Every assertion failure includes the scenario, integrator, tolerance, measured
value, step/RHS counts, a 10-row `time / |Δpos| / |Δvel| / energy-drift` table,
a repeat `dotnet test --filter` command, the C# snippet, and the CSV path.
`GALILEGO_DUMP=1` writes CSV artifacts for green runs too, under
`TestResults/artifacts/`.

## Tolerances

`Support/ToleranceTable.cs` is the single place for assertion gates. Base rows
are normalized measurements at requested tolerance `1e-9`; `For(...)` scales by
`tol/1e-9` and applies `HeadroomDivisor = 3`, so runtime gates carry about ×3
headroom. Long and golden rows have their own measured bases. Do not relax a
gate just to make a test green; recalibrate with `GALILEGO_CALIBRATE=1`.

## Golden data

- `GoldenData/golden_core_kepler.json` and
  `GoldenData/golden_core_kepler_10y.json` are generated by
  `GoldenData/gen_core_kepler.py` using mpmath closed-form Kepler propagation,
  never by a test.
- The stand goldens `golden_real.json`, `golden_real_1y_hi.json` and
  `golden_real_10y_k28.json` are linked from
  `Tools/P1bTests/Study/taylor/golden` via `<None Link=...>`. They are full
  baked-system data and cannot be reproduced by Core-only dynamics; the tests
  load them and verify `P = central_P + rel_P`.
- SHA-256 checks are an **integrity pin for linked data**, not a Core physics
  gate. A deliberate stand golden regeneration updates the hashes in the same
  change.
- Regenerate Core goldens with:

```powershell
cd tests/GalilegoPhysicsTests/GoldenData
python gen_core_kepler.py
```

## What remains in the stand

`Tools/P1bTests --fast` stays untouched (175 cases, 469 PASS, 0 FAIL). It owns
tests that depend on Unity-adjacent code outside Core: `Planet`, `Universe`,
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

Core has no full N-body integrator. `restricted-3body` and `restricted-flyby`
are production-path test particles in a restricted field; the classical
Pythagorean three-body problem is an independent-oracle Slow check.

## Known limitations

- DOP853 dense segment is cubic (`C4=0`), so interpolation is ~4th order and
  much coarser than DOPRI5's quartic Shampine segment.
- DOP853 is not uniformly more accurate than DOPRI5 at the same requested
  tolerance; the per-integrator tolerance table records the measured behavior.
- `restricted-3body` and `drift-linear` are step-cap-bounded as described above.
