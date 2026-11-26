# Maneuver planner (planning v1)

The tablet's **ПЛАНИРОВЩИК** window finds interplanetary windows, refines a plan
with the game's own n-body physics, and writes the impulses into the maneuver
plan as ordinary, editable maneuvers.

## Files

```
Assets/_Project/Scripts/Trajectory/Planner/      pure math, no Unity
  UniversalKepler  ConicMath  LambertLagrange  LambertPrograde  EscapeSolver
  TransferScanner  FlybyGeometry  FlybySearch  ShipDeparture  CaptureGeometry
  ShootingCorrector  PlannerWorld  PlannerEngine  ManeuverAxes  NelderMead
Assets/_Project/Scripts/Tablet/Nav/Planner/      tablet adapters and window
  PlanExport  PlannerController
  GamePlannerWorld  GamePlannerPropagator  PlannerView
```

`Galilego.Tablet` changes:

* `ManeuverPlan` — planner fields on `Maneuver` (`PlannerId`, `Role`,
  `FrameLocked`, `PlannedDvWorld`, `Node*`), `AddPlanned`, `RemoveByPlanner`,
  `IsEdited`, `RestorePlanned`, `ReframeManeuver`, plan clock, and
  `MaxDvPerAxis = double.MaxValue` (interplanetary burns are ≫ 6 km/s). The
  global "СИСТЕМА КООРДИНАТ МАНЁВРА" selector skips `FrameLocked` maneuvers.
* `NavMapModel.ApplyBurn` — writes the burn's node state **before** the zero-ΔV
  early return, so the planner and the per-maneuver frame switch always have a
  world state at the impulse.
* `NavTabletView` — the `ПЛАНИРОВЩИК` button, `PlannerView`, planner rows in the
  maneuver cards, `SetViewFrame` / `FrameChangeFromPlanner` / `PlannerOpen`,
  slider acceleration scaled to the current ΔV.
* `NavTabletStation` — `AttachPlannerOnce` (game `StarSystem` + `SpacecraftPhysics`),
  does not pin the map frame on a planner frame switch, and `AutoFrame` leaves
  the view alone while the planner window is open.
* `Trajectory/Transfers/LambertSolver` — `TimeOfFlight` returns `0` instead of
  `+∞` on the short-path side of `y ≤ 0`; the old `+∞` made the bisection reject
  fast hyperbolic arcs.

## Physics and verification

The package was verified before integration: 74 C# checks, 28 independent
mpmath/SciPy checks, 25/25 mutants killed; details in
`Docs/PlannerVerification.md` (copied from the package report). After the one
integration fix below, the 74-check C# suite was re-run and still passes.

* `ShootingCorrector.DampingRatio` was lowered from `1e-3` to `1e-4`. A Unity
  smoke run on a coplanar 1 AU → 1.02 AU transfer (185-day flight) stalled the
  final periapsis aim at ~896 km with `1e-3`; at `1e-4` the same case converges
  to 42.6 m. The 74-check suite (including the Earth→Mars n-body case) still
  passes at `1e-4`.

The claims that matter at runtime:

* transfer cost is the patched-conic optimum + a two-stage n-body shooting
  correction (departure min-norm + TCM1), miss ≈ 50 m on a planet-to-planet
  transfer; flyby plans are flagged `Preliminary` beyond 3 days because the
  ship's own orbit phase is not predictable that far;
* the n-body propagator uses the same `CachedGravitySource` + `DragSource`
  model as `SimulationRunner`, only with a tighter relative tolerance;
* `GamePlannerPropagator` tracked a pure Kepler orbit to 3.3 mm over 185 days in
  the Unity smoke run, so a non-converged shot is a corrector/geometry limit,
  not integration noise. The same run found 2 transfer windows (4616 m/s ideal),
  built a refined 3-impulse draft (42.6 m miss), committed it into
  `ManeuverPlan` and verified that `ReframeManeuver` keeps the physical ΔV.

## Known limits (v1)

* Start and target must orbit the **same parent** (planet↔planet, moon↔moon);
  moon → another planet chains are not implemented.
* One-revolution Lambert arcs; capture-orbit inclination is not optimized.
* Gravity assist: one flyby, departure only from the current orbit.
* The n-body correction is a local Newton solve. For a pathological geometry it
  can fail to converge; the draft is still valid, keeps the patched-conic
  impulses and carries a note ("n-body уточнение не сошлось …"). The map
  forecast is then the truth — check it before committing.
* Window search runs on the main thread (~1 s for a transfer grid,
  ~1 s for a flyby search, ~40–100 ms per n-body shot while the draft is built),
  so the window can hitch while computing.
* The available-ΔV budget is not wired to `DvResult`: `AttachPlanner` passes
  `double.NaN`, which hides the "not enough fuel" warning and the hatch pattern.
  Pass the same value `DvTabletAdapter` uses when that adapter becomes available.
* The map forecast is capped at 60 days, so an arrival burn hundreds of days out
  is not drawn on the dashed map path; its card and node state come from the
  planner.

## Do not re-apply the package snapshot wholesale

The package's `Scripts/NavMapModel.cs` and `Scripts/NavTabletStation.cs` predate
the current `HEAD` and contain two older behaviours that this project has
already fixed:

* `NavMapModel` draws full-period **orbit rails** (`BuildBodyOrbits` /
  `BuildFrameOrbit`). The current map deliberately draws only the selected
  history/forecast window — those rails are the "green phantom" regression.
* `NavTabletStation` calls `overlayMap.ResetView()` on every `FrameChanged`.
  The current map keeps the wheel zoom across an `ИСК` press.

Only the planner hunks from those files were taken; the trail semantics and the
no-reset-on-`ИСК` behaviour stay as they are.
