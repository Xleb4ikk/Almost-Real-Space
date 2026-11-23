# Procedural fairings

Short version: a fairing is an ordinary **stack part** (`PartKind.Fairing` — a 40 mm
base plate like the separator) plus a **shell** (`PlacedPart.Fairing`, a
`FairingShell` profile of rings) that is built in segments with the aiming reticle.
The shell mass lives in the plate's `PartMetrics.MassKg`, so the ΔV panel, the
flight model and the tablet all see it without knowing about fairings.

---

## Flow (build mode)

1. Hot slot **6** (or the "ПРОЧОЕ" card) selects the fairing. The ghost is a flat
   plate snapped to a free node; the plate radius is the node's radius.
2. **RMB** places the plate and starts the shell.
3. While building, the aim is intersected with the vertical plane through the
   fairing axis that faces the camera (`FairingAim.TryAimRing`): the aim height is
   the next ring's height, the distance from the axis is its radius.
   **RMB** adds a segment; the HUD shows the length and the mass of the whole shell.
   * aim straight up — the radius is kept (a cylinder);
   * aim at the axis — a nose point (`FairingEnd.Apex`);
   * aim at a part's bottom/top edge ring — the shell closes onto it
     (`FairingEnd.Docked`);
   * **Shift** disables the radius/nose snaps.
4. **F** finishes the shell open (`FairingEnd.Open`). A closing ring finishes it
   automatically.
5. **Ctrl+Z** while building pops the last segment; the plate is removed by the
   next undo.
6. Aiming at a fairing and pressing RMB continues it — **including a finished one**:
   a closed shell (nose cone / docked) is unsealed first, its closing ring removed,
   so new sections can follow. The petal count comes from the slot (X/Z), so an
   installed fairing can be reshaped without deleting it.
   **Nothing has to be in the hand**: with an empty hotbar slot the same aim + RMB
   continues the fairing (`UpdateEmptyHandFairing`).
7. **Alt+RMB** removes the **last section** of a fairing — the usual way to cancel a
   section that was just placed (the plate goes last; with only the plate left the
   whole fairing is removed). While a shell is being edited Alt+RMB *always* means
   "remove the last section" (even if the aim happens to be on the payload inside);
   otherwise it works from the normal Alt delete aim: on a fairing it removes a
   section, on any other part it deletes the part. Deleting a whole fairing in one
   press is gone: sections are removed one at a time.

The shell is **light-tight**: the petals butt at the seam (`SeamGapDegrees = 0`), so a
payload inside is never visible through a slot, and the seam does not read as a cut
in the finished fairing.

The shell is also a **wall for placement**: `FairingShellBlocks` rejects a part whose
body sphere crosses the shell radius at any height, so a capsule cannot be pushed
through a built fairing (and the fairing builder itself rejects sections that cut a
part, `FairingClearOfParts`).

## Flow (flight)

**B** (seated) jettisons every fairing that still has its petals:

* `ProceduralFairing.DetachPetals` unparents the petals and hands them over;
* each petal becomes a `DebrisPool` slot (n-body motion) and its mesh is drawn by
  `FairingDebrisVisuals` instead of the debris sphere marker (`DebrisView`);
* the shell mass leaves the flight model, the stage core that owns the plate and
  `Ship.Mass` (`FlightRuntime.OnFairingReleased`); in the contact zone the PhysX
  body masses follow in `FixedUpdate`.

Returning to build (`R`, or a rewind to point 0) rebuilds the petals and resets
`FairingShell.Jettisoned`; a rewind into the middle of the flight does **not**
bring a jettisoned shell back (see the limitations).

## Mass and geometry

* Shell: `area × 9 kg/m² × 1.08` (`FairingShell.ArealDensityKgPerM2`,
  `HardwareFactor`) — a Falcon-class composite sandwich.
* Plate: `πr² × 40 mm × 2700 kg/m³ × 0.30` (`FairingShell.PlateMassKg`), i.e. the
  same ribbed-aluminium idea as the separator.
* Petals: 2/3/4/6/8, cycled with **X/Z** in the slot and the card; the mesh is one
  shared "petal frame" mesh per fairing (origin at the hinge on the base ring,
  +X outward, +Z tangential).
* The wall has a real thickness (`ShellThicknessM = 12 mm`): outer skin, inner
  skin, seam walls and rims. Profile normals are averaged between adjacent rings,
  so the shell shades as one surface instead of banding at every joint.
* Convex `MeshCollider` slices (≤60° of arc) per petal: built for physics, disabled
  in build mode so the player can walk through the open petals.

## Known limitations / gotchas

* **No legacy `UnityEngine.Input` anywhere in this feature.** The project runs with
  *Active Input Handling = Input System Package*, and the old class throws
  `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class`
  at runtime. Shift is read with `PlayerInput.ShiftHeld()`.
* **Runtime semi-transparent HDRP materials do not render in this project** (the
  same reason the other ghosts are opaque). `FairingTransparentGhost` therefore
  defaults to `false`: the section projection is an opaque emissive green fill plus
  the `FairingOutline` wire frame. Setting the flag to `true` creates an
  `HDRP/Unlit` transparent material — if it draws nothing, keep the flag off.
* The shell is mass and visuals only: it is not part of the PhysX/proxy collision
  shape (the plate is), and it is not a staging boundary.
* A shell must not cut through parts (`FairingClearOfParts`); a wall flush with a
  part is allowed (the 12 mm skin is subtracted from the required clearance), but
  a shell narrower than the payload is rejected with "Проходит сквозь деталь".
  The Orion capsule is checked as a **full-width cylinder**: its heat shield is a
  wide disc, and the linear cone metrics (`radius → 0.38 R`) understate the surface,
  which let a tapering shell pass through the real capsule.
* The shells are built by code, never saved: `ProceduralFairing.Shell` is
  `[NonSerialized]`, the draft lives in `SpacecraftBuilder.parts`.
