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
* `NavTabletView` — the `ПЛАНИРОВЩИК` tab under the reference-frame tab, the
  wide left-side planner drawer, planner rows in the maneuver cards, `SetViewFrame` /
  `FrameChangeFromPlanner` / `PlannerOpen`, frames/planner exclusivity, and slider
  acceleration scaled to the current ΔV.
* `NavTablet.uxml` — the `NavTabletPlanner.uss` stylesheet reference.
* `NavTabletStation` — `AttachPlannerOnce` (game `StarSystem` + `SpacecraftPhysics`),
  does not pin the map frame on a planner frame switch, leaves the view alone
  while the drawer is open, and skips map rebuilds under it (one forced rebuild
  when it closes).
* `PlannerController` — `Clear()` and `WindowsVersion` for the window list; the
  UI uses them to invalidate results when the target, mode or selection changes.
* `Resources/NavTablet/NavTabletPlanner.uss` — drawer, cards, rows, tiles and a
  themed thin scrollbar; linked from `NavTablet.uxml`.
* `Trajectory/Transfers/LambertSolver` — `TimeOfFlight` returns `0` instead of
  `+∞` on the short-path side of `y ≤ 0`; the old `+∞` made the bisection reject
  fast hyperbolic arcs.

## Tablet drawer (UI pass)

The planner is the **second tab in the left menu rail**, directly under the
reference-frame panel (`СИСТЕМЫ ОТСЧЁТА`). The tab is measured from the frames
panel's live layout, so it stays below it even when the body list grows. The
drawer opens to the right of the rail (left 32 px) at 92 % of the tablet width,
leaving both vertical tabs visible; closing it returns the map frame and the
flight-plan column.

The two menus are **mutually exclusive**: opening the planner collapses the
frames panel (its `Opened` handler), and opening the frames panel calls
`PlannerView.Close`. Both tabs stay visible while the other panel is open, so
the player can switch directly with one click; the first panel collapses
automatically.

`NavTabletPanelSettings` now matches **width** (`m_Match: 0`, reference
1024×576). At the user's 1250×840 window the old 0.5 match scaled by height and
cropped the right side of the tablet, which cut the plan tiles and the heat map;
matching width fits the whole tablet horizontally.

The drawer layout is a two-column scroll:

* left — route (target / flyby-body dropdowns limited to the start body's
  siblings), departure orbit, arrival orbit, search limits and `НАЙТИ ОКНА`;
* right — porkchop heat map, window list, selected-window summary and flags,
  plan tiles, impulse list and note;
* a sticky footer keeps `ПОСТРОИТЬ ПЛАН` / `В ПЛАН` / `УБРАТЬ` visible without
  scrolling. While the n-body draft runs the first button becomes
  `ОТМЕНИТЬ РАСЧЁТ` and is actually enabled (the old button claimed cancel but
  was disabled while busy).

Other behaviour fixes in this pass:

* **`ЗАДАТЬ` orbit** now works in transfer mode and clears stale windows
  (`SetOrbit` calls `PlannerController.Clear`). The gravity-assist v1 engine
  starts only from the current orbit, so in that mode the dead button pair is
  replaced by the caption `ГРАВ. МАНЁВР V1: СТАРТ ТОЛЬКО С ТЕКУЩЕЙ ОРБИТЫ`
  instead of pretending to work;
* target and flyby body are dropdowns instead of a click-to-cycle button, so
  long body lists stay legible;
* `НАЙТИ ОКНА` repaints the status as `ПОИСК ОКОН…` before the synchronous
  Lambert grid and catches a failed search instead of freezing silently;
* window-list rebuilds are keyed by `WindowsVersion`, so a second search with
  the same number of windows no longer leaves stale times on screen;
* the runtime theme's wide white scroller is recoloured/narrowed at runtime
  (its thumb is `unity-base-slider__dragger`, not the USS-visible container);
* the porkchop texture is destroyed when its size changes (no per-search
  texture leak), and the text fields are re-synced from settings on reopen;
* text labels that can be long (`planner-title`, impulse metadata, tile values,
  footer buttons) wrap instead of overflowing, and the footer wraps on a
  narrow map.

The drawer and rail were rendered in a temporary Play-mode UIDocument scene with
the same `NavTablet.uxml`, `NavTabletView`, panel settings and styles. At the
1024-wide layout the planner tab was at `x = 7.2…25.4`, the open drawer started
at `x = 32` and was 942 px wide, its content's rightmost edge was exactly the
drawer edge (no horizontal overflow), and the plan column stayed at
`x = 747.8`. Opening the planner reported `framesOpen=False`; the tabs remain
outside the drawer so the other panel is one click away. The real
`OutdoorsScene` does not contain a `NavTabletStation` at startup (the tablet is
part of the spawned ship), so the actual in-game monitor was not clicked.

## Physics and verification

The package was verified before integration (74 C# checks, 28 independent
mpmath/SciPy checks, 25/25 mutants killed — `Docs/PlannerVerification.md`).
**Those test sources are not in this repository**; that document says so itself,
and no test in `Tools/P1bTests` compiles or references the planner math. Treat
the package numbers as a historical claim, not as something that runs here.

The formulas on `HEAD` were re-checked on 2026-11-26 by an independent
standalone stand (`Tools/PlannerCheck`, pure C# + Python oracles: DOP853 direct
ODE integration, classical elements + Kepler equation in mpmath at 50 digits):
Lambert 1.2e-12, universal Kepler 2.6e-11, escape/flyby/capture geometry
1e-15, Hohmann and capture Δv equal to the closed-form values to < 1e-6 m/s.
Numbers and the two defects found in the plan assembly are in
`Docs/PlannerVerification.md` ("Independent audit of HEAD"). Re-run it after any
edit to `Trajectory/Planner/*`.

* `ShootingCorrector.DampingRatio` was lowered from `1e-3` to `1e-4`. A Unity
  smoke run on a coplanar 1 AU → 1.02 AU transfer (185-day flight) stalled the
  final periapsis aim at ~896 km with `1e-3`; at `1e-4` the same case converges
  to 42.6 m. That behaviour was measured in the package suite, which is not in
  the repository; the current stand does not exercise the corrector.

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

## Review fixes (2026-11-26)

Found during an independent audit of the planner; the core math (Lambert, universal
Kepler, escape, hyperbolic flyby, Hohmann) was confirmed against mpmath/SciPy oracles
to `1e-13…1e-14`, but the plan assembly had two logic defects and several smaller ones:

* **`CaptureKind.None` is now honoured.** `TransferScanner`/`FlybySearch` already cost
  a `None` transfer without a capture burn, but `MakeTransferDraftCo`/`MakeDraftCo`
  always appended a `Capture` impulse. The window price and the plan therefore differed,
  and choosing "ПРОЛЁТ" still circularised at the target. Both builders now skip the
  capture impulse; the periapsis aim is still used for the flyby/closest approach.
* **Flyby n-body fallback restores the patched impulses.** If leg 1 converged but leg 2
  did not, `dvDep`/`dvFly` stayed on the n-body values while the states and capture were
  patched — a hybrid plan labelled "ПАТЧ-КОНИКИ". The `!refined` branch now resets
  `dvDep` and `dvFly` to `dep.Burn.DvWorld` / `fl.MotionDir * (SpeedPeriOut - SpeedPeriIn)`.
* Window sorting uses a deterministic two-level sort instead of a non-transitive
  tolerance comparator (`List.Sort` could throw on near-equal windows).
* Departure-phase search is capped at 3 days for the two-body (`Preliminary`) ship track and at
  30 days when the window is within `ShortHorizonS` and the n-body track is used. A GEO/lunar
  orbit no longer has to find its burn point inside an arbitrary 3-day slice while the track
  is exact anyway.
* `PlannerController.Search` rejects a ship below `Radius + SafeAltitude`, a ship that is not
  free-flying (`IPlannerShipState`: landed/destroyed/throttle, wired from `NavTabletStation`),
  and clamps the prep/arrival altitudes to the safe minimum (with a status note);
  `MaxFlightDays` is honoured instead of silently doubling the transfer TOF upper bound.
* `FlybySolution.HelioEnergyGain` includes the `|v∞out|² − |v∞in|²` term for powered
  flybys; the UI no longer calls a powered flyby's total Δv∞ "ДАРОМ".
* The radial-node T/N/B fallback is world Z in both `ManeuverAxes` and
  `NavMapModel.ApplyBurn`, so a degenerate node cannot mean two different impulses.

## Audit fixes 2 (2026-11-26, standalone stand on HEAD)

Second independent audit, this time with the formulas re-checked on the current
tree (`Tools/PlannerCheck`). The core math came out clean to 1e-11…1e-16; the
defects were all in how a gravity assist is priced and assembled:

* **A gravity-assist plan was thrown away over millimetres per second.**
  `MakeDraftCo` rejected the draft whenever the *actual* departure arc asked for
  a hair more turn than `rp = rpMin` allows (`!fl.Feasible`). Measured on
  Earth → Jupiter → Saturn: `need − max = 0.000225°` → `DeficitDv = 0.0074 м/с`,
  yet the plan was cancelled with «пролёт не стыкуется». This is structural: the
  cost `dep + powered(rp) + deficit(rp) + arr` is monotone inside the feasible
  set, so the optimum always sits **exactly on the `rpMin` wall** and any
  deviation of the real orbit flips feasibility. Now a deficit up to
  `FlybyPlanner.FlybyDeficitToleranceM` (2 м/с, plus 1e-4 of the plan cost) is
  accepted, reported in the note and closed by КОРРЕКЦИЯ 2 (which was already in
  the plan and in `RefinedDv`). Only a real shortfall (hundreds of m/s) cancels.
* **No more phantom Δv for a weak flyby.** `FlybyGeometry` used to charge
  `2·v∞·sin((min−need)/2)` when the required turn was *below* the turn at
  `rp = SOI`. A weaker turn is achieved by a periapsis **outside** the SOI and is
  free — the SOI is a patched-conic validity boundary, not a wall. Measured
  before the fix: Jupiter, `need = 0.399 рад`, `min = 0.568 рад`, v∞ = 2.6 км/с →
  439.5 м/с charged for a flyby that costs nothing. Now `DeficitDv = 0` and `rp`
  comes from `TryPeriapsisForTurn` (may exceed the SOI).
* **`PatchedDv` no longer mixes ideal and actual.** The flyby draft took the real
  departure but the window's ideal `win.ArrDv`/`win.Flyby.PoweredDv`. Now it uses
  the recomputed `fl` for the flyby, so `PatchedDv` equals the impulse list
  (17115.7867 → 17115.7895 on an interior window, the 2.9 мм/с periapsis burn is
  no longer lost).
* **Degenerate flyby no longer returns a zero axis.** With `v∞_in ∥ v∞_out`
  `PeriapsisDir`/`MotionDir` stayed `(0,0,0)` while `Rp > 0`, so
  `PeriapsisPosition` was the planet's centre. Now a deterministic perpendicular
  axis is used (regression case `Fdeg/3`).
* **Capture burn is exactly circularising.** It was
  `capVel.normalized·(vTarget − |capVel|)`, which keeps any radial component the
  n-body aim leaves behind (`e ≈ v_r/v_c`). Now the impulse turns the velocity
  onto the tangent (`t̂·vTarget − vCap`); the patched-conic case is unchanged.
* **`PlanePenaltyDv` excludes the requested orbit prep.** A 300→700 км prep was
  reported as a 221.8 м/с "orbit not aligned with the window" penalty.
* The docs no longer claim the missing 74-check suite runs here.

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
