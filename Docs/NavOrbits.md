# Nav tablet — the map shows the selected time window, not a whole orbit

Short version: every body with a parent draws its **history and forecast over the
slider windows**, in the selected ISK:

```
point(t) = body(t) − frame(t),   t ∈ [now − history, now + forecast]
```

There is **no full-period orbit rail**. A complete ellipse at T+00:43 is a prediction
that ignores the sliders and shows a path the body did not take during the displayed
time; it is the "green phantom" the player sees as soon as the mission clock is short.
Under Terra's ISK and a 14 h history the Moon's trail is a short lunar arc near its
marker; under the Sun's ISK the same 14 h is a heliocentric segment. If the two look
identical, the frame offset is not inside the sampled path.

## Symptom (two regressions on the same feature)

1. **The Moon had no trail at all.** `BuildBodyTrails` built only the **centred**
   body's history/forecast, so with Terra centred the Moon's row in the frames table
   toggled a line nobody drew. "У луны нет орбиты в иск солнца" was this.
2. **Restoring full-period rails overcorrected.** A whole ellipse is drawn regardless
   of the sliders, and under the Sun's ISK it is not the Moon's path during the shown
   hours. At T+00:43 with a 14 h history slider it reads as a phantom orbit.

## What is drawn

* **Every body with a parent**, in every frame, gets a history and a forecast **subject
  to the per-body dot** in the frames table — not just the centred body. The panel's
  column is literally `СЛЕД · ТЕЛО`.
* **Only the slider span**: `now − history … now + forecast`. The history is bright
  green, the forecast amber dashed; a short-period moon is capped to three revolutions
  so a multi-day slider does not become a scribble.
* **The selected ISK is part of the geometry**: the frame body's motion over the span
  is subtracted sample by sample (`body(t) − frame(t)`). Switching `SOL ↔ TERRA`
  changes the Moon's trail from a heliocentric segment to a lunar arc and back.
* **The frame body has no trail**: its path in its own frame is identically zero.
  The ship's trail is separate (live samples), and a parked craft deliberately draws
  no site track.
* **The zoom survives an `ИСК` press.** Resetting it to the planet close-up made the
  trail the player had just zoomed out to look at disappear — the "no orbit when I
  change the ISK" report.

`NavLineKind.BodyOrbit` is intentionally not emitted. If a future feature wants a
reference ellipse, it must be a separate layer with its own legend row, off by default;
it must never be the thing the `СЛЕД` dot turns on.

## The body plate shows the live height — and nothing else

Hovering a marker expands the body's plate in place. It used to show the body's radius:
over Artemis the row read `R 412.000 КМ`. Three decimals are metre precision, but a dot
in front of a whole number reads as a thousands separator — 412 000 km, the real Moon's
distance from Terra — so the plate looked like it was quoting the Moon's altitude. And a
radius is not a navigation datum: it answered a question nobody asks while aiming.

The plate is now the name and one row:

```
АРТЕМИДА
H 82 613 КМ
```

* `H` is the **live** height: `|ship − body| − radius`. For the body the craft is
  actually over it is `ShipHeightAboveTerrain`, the terrain datum — the same number as
  the map's own `H ... AGL`, so the plate cannot disagree with the footer.
* **No radius.** It was removed on request ("там не должен отображаться радиус
  планеты"): the plate is an aim readout, and a static body size only competed with the
  one number that changes. The globe on the map is already to scale, so the size is
  visible without a label.
* `TabletFormat.Dist` still drops trailing fractional zeros (`412.000` → `412`,
  `12.500` → `12.5`): the dot slip that started this is gone from every altitude and
  distance on the tablet.

## How to check a change

`Test572_NavMapBodyTrailsEveryFrame`:
every parented body has a history line in every frame, the Moon's span equals the
slider (not its period), the endpoints are `body(t) − frame(t)`, a frame switch changes
the geometry, the span follows the slider, the per-body dot hides the trail, and no
`BodyOrbit` line is emitted.

The drawing half needs a picture. Render the map headlessly from a probe next to the
project (`Rebuild` arguments: `forecastSeconds` first, then `historySeconds`):

```csharp
model.Rebuild(sys, null, 0d, frameIndex, centerIndex, visible,
    forecastSeconds: 0d, historySeconds: 14d * 3600d, null, true);
var r = new NavMapRenderer(1024, 640, false);
r.Draw(model, state, info);          // first draw applies the new-frame angles
r.Zoom = 0.012f;
r.Draw(model, state, info);
File.WriteAllBytes(path, ImageConversion.EncodeToPNG(r.Texture));
```

At `frame = Terra, center = Terra` the Moon must be a short arc near its marker
(not a closed ellipse). At `frame = Sol` the same 14 h must be a heliocentric segment.
The legend must read `ИСТОРИЯ 14:00:00` and the lines must change when the slider moves.

The plate is checkable on the same texture: `renderer.Hover(pixelOf(artemisMarker), out text)`
must return exactly two lines — the name and a row beginning with `H ` holding the craft's
current distance to that surface. A `R ` row is a regression.
