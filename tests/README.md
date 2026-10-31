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

The old non-Core xUnit files that were removed from this project are preserved
verbatim in the separate `tests/GalilegoSimTests` project (with its own local
`Shims.cs`). It is intentionally outside the Core-only build:

```powershell
dotnet test tests/GalilegoSimTests/GalilegoSimTests.csproj
```

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
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "DisplayName~Kepler e=0.9"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~DenseOutputTests"
```

`methodDisplay=method` is set in `xunit.runner.json`; matrix rows render as
`Dop853 | Kepler e=0.9 | 1 period | tol 1.0E-009`.

## Integrator matrix

`Integration/Matrix/IntegratorMatrixTests.cs` is one `[MatrixTheory]` over:

- integrators: `Dopri5`, `Dop853`;
- scenarios from `Support/Scenarios/ScenarioCatalog.cs`;
- tolerance levels declared by each scenario.

A new scenario is **one catalog entry**, not a new test method:

```csharp
CreateKepler("kepler-e07", "Kepler e=0.7 | 1 period", ScenarioClass.KeplerEccentric,
    1.0e7d, 0.7d, 20d, 10d, 30d, 40d, 1.0d, ScenarioTier.Fast,
    new[] { 1e-6d, 1e-9d }),
```

The row gets traits and runs conservation, oracle and step-budget checks. A new
integrator requires an `IntegratorKind` value, a switch in
`Support/ScenarioExecution.cs`, the matrix loop, and a `ToleranceTable` row for
every scenario class.

`Support/StepBudget.cs` pins the measured step count and RHS-call count for every
matrix row within ±10%. It is a regression gate for step control, not a
wall-clock benchmark; when step control changes intentionally, refresh it from
`TestResults/calibration.md`.

### Fluent runs

```csharp
ScenarioExecution run = Run.Scenario("kepler-e05")
    .With(Integrator.Dop853, 1e-12)
    .Execute();

run.MatchesOracle(posTol, velTol)
   .ConservesEnergy(eTol)
   .ConservesAngularMomentum(lTol)
   .ConservesLrl(lrlTolerance)
   .IsTimeReversible(posTol, velTol)
   .IsDeterministic()
   .IsChunkingInvariant(posTol, velTol)
   .ConvergesWith(1e-13);
```

`ScenarioExecution` exposes the final state, dense segments, step count, RHS
call count, maximum oracle errors and maximum energy/angular-momentum/LRL drift.

## Reading a failure

Every assertion failure includes:

- scenario, integrator and requested tolerance;
- measured value, unit and the tolerance;
- step count, RHS call count, chunk count and dense segment count;
- 10-row table of `time / |Δpos| / |Δvel| / energy drift`;
- a ready `dotnet test --filter` command;
- the C# snippet that reproduces the run;
- the CSV artifact path.

Example:

```
ПАДЕНИЕ: ошибка позиции против оракула
Сценарий:   kepler-e05 — Kepler e=0.5 | 1 period
Интегратор: Dop853, tol=1E-09
Измерено:   1.479E+01 м > допуск 4.929E+01 м
...
Повторить: dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "Integrator=Dop853&Scenario~Kepler"
C#:        Run.Scenario("kepler-e05").With(Integrator.Dop853, 1E-09).Execute()
CSV:       .../TestResults/artifacts/kepler-e05-Dop853-tol-1E-09.csv
```

`GALILEGO_DUMP=1` writes the CSV even for green runs. CSV columns:
`time_s,px,py,pz,vx,vy,vz,position_error_m,energy_drift_rel`.
Artifacts go to `TestResults/artifacts/` at the repository root.

## Tolerance calibration

All assertion tolerances live in `Support/ToleranceTable.cs`. Base values are
normalized measurements at requested integrator tolerance `1e-9` (long-horizon
and golden classes have their own measured rows). `For(integrator, class, tol)`
scales the base by `tol/1e-9` and applies a documented `HeadroomDivisor = 3`, so
runtime gates carry about x3 headroom, not x10. Long rows were re-measured at
both tolerance levels so the loose end is not under-scaled.

To re-measure the whole matrix:

```powershell
$env:GALILEGO_CALIBRATE="1"
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~CalibrationTests"
Remove-Item Env:GALILEGO_CALIBRATE
```

It writes `TestResults/calibration.md` (scenario, integrator, tolerance, steps,
RHS calls, oracle error, mass error, energy/angular/LRL drift). Set each base to
`measured × 3` (the table stores the pre-divisor normalization); do not edit a
tolerance just to make a red test green.

## Golden data

- `GoldenData/golden_core_kepler.json` and
  `GoldenData/golden_core_kepler_10y.json` are **generated by**
  `GoldenData/gen_core_kepler.py`, never by a test. The Python script uses
  mpmath closed-form Kepler propagation (50–70 digits), not the C# integrator.
- The existing stand goldens `golden_real.json`, `golden_real_1y_hi.json` and
  `golden_real_10y_k28.json` are **linked**, not copied, from
  `Tools/P1bTests/Study/taylor/golden` via `<None Link=...>`. They describe the
  full baked system and cannot be reproduced by Core-only two-body dynamics;
  the tests load them and verify `P = central_P + rel_P`.
- The SHA-256 check in `GoldenTests` is an **integrity pin for the linked data**,
  not a Core physics gate. A deliberate stand golden regeneration updates those
  hashes in the same change.
- Regenerate Core goldens with:

```powershell
cd tests/GalilegoPhysicsTests/GoldenData
python gen_core_kepler.py
```

## What remains in the stand (and why)

`Tools/P1bTests --fast` (469 PASS / 0 FAIL) stays untouched. It owns tests that
depend on Unity-adjacent code outside Core: `Planet`, `Universe`,
`Spacecraft`, `Events`, `Ephemeris`, `Debris`, terrain, atmosphere and the
long-running benchmark/experiment matrix. Those are not Core-only and must not
be pulled into this project.

The restored legacy xUnit files now live in `tests/GalilegoSimTests`; their
integration-level equivalents also exist in the stand. They are not part of the
Core-only project because they need `Spacecraft`, `Universe`, `Events` and
`Spacecraft.Solver`.

Mapping of the old xUnit project and the ported stand checks:

| Old check | New test |
|---|---|
| `Rk4Tests` (free fall, growing acceleration) | `Rk4Tests` (unchanged Core-only) |
| `RegressionTests` (new path vs old path, pure gravity) | `Integration/Kepler/LegacyParityTests` + restored `tests/GalilegoSimTests` |
| `OrbitAnalyticTests` (circular orbit, one period) | matrix `kepler-circular` + `Integration/Golden/GoldenTests` + restored project |
| `TsiolkovskyTests` | `Integration/Ship/SpacecraftIntegratorTests.ConstantThrustMatchesTsiolkovsky` + restored project |
| `MassToleranceTests` | `Integration/Ship/SpacecraftIntegratorTests.TightMassToleranceReallyDrivesTheStepSize` + restored project |
| `AltitudeCrossingTests`, `TimedEventTests`, `PriorityRestartTests`, `SoiChangeTests`, `ContactSolverTests` | restored verbatim in `tests/GalilegoSimTests`; integration-level equivalents also exist in the stand |
| P1b `Test21_Kahan` (micro arithmetic part) | `Mathematics/KahanTests` |
| P1b `Test34_GoldenState` | stays in the stand: uses `SpacecraftPhysics`/`Universe`; the Core-only analogue is the linked Core golden |
| P1b `Test35_KeplerEdge` | stays: uses `KeplerMath` in `Trajectory/Prediction`; Core has only `OrbitalElements` |
| P1b `Test39_SecularDrift`, `Test21` 10-year macro | stays: `LongWarpDriver`/`EventDrivenPropagator`/`StarSystem` |

There is no full N-body integrator in Core (Core integrates one spacecraft
state). `restricted-3body` is the production-path test particle in a moving
three-body field; the classical Pythagorean three-body problem is an
independent-oracle check (`Integration/Kepler/PythagoreanOracleTests`, Slow).

## Known observations from the suite

- DOP853's dense segment is cubic (`C4 = 0` in `SpacecraftOrbitIntegrator`), so
  its interpolation error is larger than DOPRI5's Shampine quartic. The tests
  measure it and keep the documented gates; full DOP853 dense output is a
  separate follow-up.
- DOP853 is not uniformly more accurate than DOPRI5 at the same requested
  tolerance in this implementation (row-wise calibration shows this); the
  tolerance table is per integrator and per class, not a claim of formal
  superiority.
- `OrbitIntegrator.StepToTime` is covered as a thin wrapper; the near-parabolic
  hyperbolic apsides branch is covered by
  `OrbitalElementsTests.NearParabolicHyperbolicApsidesAreRejected`.
