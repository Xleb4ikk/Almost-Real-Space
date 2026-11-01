# Taylor golden generators

These scripts reproduce the strings embedded in the C# tests. Run from this
directory; they need only `mpmath` (and Python 3).

| Script | Test | Precision / settings |
|---|---|---|
| `gen_jet_oracle.py` | Test511 | dps=50, Taylor order 6 |
| `gen_kepler3d.py` | Test510 | dps=50, analytic Kepler equation; r0/v0 rounded to IEEE double before T |
| `gen_kepler507_514.py` | Test507, Test514 | dps=60, analytic Kepler equation |
| `gen_chebyshev_degree.py` | Test515 | dps=60, degree 12, t=0.37, L=1 |
| `gen_ship_moving.py` | Test512 | dps=60, K=24, h=0.005; exact double coefficients/IC |
| `gen_si.py` | Test509 | dps=50, K=40, tol=1e-40, hmax=86400 s |
| `gen_pythagorean.py` | Test517 | dps=40, K=32, tol=1e-30, hmax=0.005 |
| `mp_golden_real.py` | Test516 | dps=40, K=28, tol=1e-20, hmax=3600 s; real baked ephemeris |

`mpt_nbody.py` is the independent N-body Taylor recurrence used by
`gen_si.py` and `gen_pythagorean.py`; it is a copy of the reference used for
the original review, so the archive is self-contained.

Units: Test509, Test516 and Test504 are SI (m, m/s). Test507, Test510,
Test511, Test512, Test515 and Test517 are dimensionless (G=1-style units).

## Test512 input caveat

The child Chebyshev coefficients are first built with IEEE double
arithmetic, exactly as `QuadraticEphemeris` does in C#, and then promoted to
mpmath. This is required: using exact decimal `0.01`, `1.05`, `1+sqrt(0.2)`
instead introduces errors of order 1e-15, which masks the dd result.

## Real-system exports and golden

`Test518_EphemerisExport` writes three exports with the same ship orbit:

- `real_bake_export.json` � 60-day bake, `Days=30`; input for the 1/30-day
  golden in `golden_real.json` (K=28, tol=1e-20, dps=40).
- `real_bake_export_1y.json` � 1.1-year bake, `Days=365`; input for the
  1-year golden in `golden_real_1y_hi.json` (K=28, tol=1e-20, dps=40).
- `real_bake_export_10y.json` � 10.1-year bake, `Days=3650`; input for the
  optional 10-year golden. The oracle run at K=20/tol=1e-12 is roughly
  5�7 hours with `gmpy2`; generate it with
  `--checkpoints 365,730,1095,1460,1825,2190,2555,2920,3285,3650`.

Each export contains `T0`, `Days`, `Central` (Earth), ship absolute
`ShipP0`/`ShipV0`, `RelP`/`RelV`, and every body with `Parent`, `Mu`,
`SubtreeMu`, `T0`, `SegmentLength`, `Degree`, `Segments`, and per-segment
`Coeffs` (3 axes x (Degree+1)). The coefficients are the exact doubles the
C# `BakedEphemeris` uses; the oracle treats the piecewise polynomial as the
exact model.

Reproduce a golden with:

```powershell
python mp_golden_real.py real_bake_export_1y.json `
  --K 28 --tol 1e-20 --dps 40 --hmax 3600 `
  --checkpoints 365 --out golden_real_1y_hi.json
```

The canonical files consumed by `Test516_DdRealBudget` must also carry
`golden_error_estimate`; add `--certify` (or copy the estimates from the
certified output). Without it the test fails loudly instead of crashing.

Certify the 1-year golden (main run plus K+6, dps+10, tol/1e6):

```powershell
python mp_golden_real.py real_bake_export_1y.json `
  --K 28 --tol 1e-20 --dps 40 --hmax 3600 `
  --checkpoints 365 --certify --out golden_real_1y_hi_certified.json
```

The certified file carries `golden_error_estimate` per checkpoint. Compare it
with the canonical golden; if the checkpoints agree, replace the canonical
file and update the SHA-256 constants in `Test518`/`Test523`:

```powershell
python compare_golden.py golden_real_1y_hi.json golden_real_1y_hi_certified.json
```

**Bake-span caveat.** `EphemerisBaker.AdjustedSegmentLength` redistributes the
requested segment length over the whole span (`span / ceil(span/requested)`),
so a different bake span changes the segment boundaries and therefore the
model. A golden is only valid for the export with the same bake span; do not
change `ExportRealBake`'s span without regenerating the corresponding golden.

## Long-horizon coverage

- `Test504_TaylorReference` checks a 10-year analytic circular Kepler ship
  directly against the closed-form state (no golden file). At the default
  `1e-12` tolerance and `hmax=3600` s the error is `dp=1.90e-2 m`,
  `dv=2.16e-5 m/s`; the test gates are `0.1 m` and `1e-4 m/s`.
- `Test516_DdRealBudget` checks the real baked system at 1 and 30 days with
  `golden_real.json`, and at 1 year with `golden_real_1y_hi.json`.
  At the default dd tolerances (`1e-9`/`1e-12`, `hmax=3600` s) the 1-year
  errors are `dp=1.55e-3 m`, `dv=1.84e-7 m/s`; the tight dd run
  (`1e-12`/`1e-15`, `hmax=900` s) gives `dp=1.47e-7 m`,
  `dv=1.75e-11 m/s`.
- The pre-fix 1-year tight error was `8.1e-3 m`. It was not a precision
  floor: at boundaries `k*L` the double quotient `(t-T0)/L` can round to
  `k-epsilon`, so a plain `floor` selected the previous segment and the
  step extrapolated the old Chebyshev polynomial across the boundary
  (observed at k=15 and k=30). `BakedEphemeris.SegmentIndex` now snaps by
  a few ulps of the quotient; the 1-year error curve is smooth
  (`3.2e-9 m` at 30 d, `1.6e-7 m` at 365 d).
- `golden_real_1y_hi.json` carries the numerical certificate from the last
  certification run: K=34, tol=1e-26, dps=50 gives `|dp|=1.3e-16 m`,
  `|dv|=1.81e-20 m/s` at day 365. The certificate was reused after the
  per-body-root model change because the oracle method and settings are
  unchanged; rerun `--certify` if the generator itself changes.
- `Test523_BakeGoldenBridge` checks that the in-memory bake is bit-identical
  to the export used by the golden (`maxCoeffDiff=0`, position/velocity
  difference 0 at 1, 30 and 365 days) and pins the SHA-256 of both export and
  golden files. `Test518` also checks the export hash immediately after
  writing it, so a stale export fails loudly instead of silently invalidating
  the golden.
- `Test526_SecondReferenceY6CDd` is the formal independent method check where
  no analytic solution exists: fixed-step Y6C in dd at `h` and `h/2`. At
  30 days with `h=10/5 s` the self-difference is 1.4 m and the `h=5 s` result
  agrees with the golden to 0.297 m. Y6C needs a few seconds per step at
  perigee; it is a coarse independent check, not a sub-mm reference.
- A 10-year *real-baked-system* golden is not included: the mpmath oracle is
  single-threaded and takes about 10 hours for that span (about 7 hours with
  `gmpy2`; K=20/tol=1e-12 is the practical setting). The 10-year Kepler case
  covers the integrator's long horizon; the real-system model is validated
  to 1 year. `mp_golden_real.py` plus a longer `ExportRealBake` is the
  generator if that run is needed.
- `mp_golden_real.py --selftest` cross-checks the oracle against a direct
  force expression and scipy DOP853 on a synthetic system.

## SHA-256 of golden-bearing files

Hashes are computed on LF-normalized bytes, so they are checkout-EOL independent.

```
Kepler3DMatrix.cs        D110A6AE9232B408DB99CA11695700207C4ACF36A54AB801074E7B3DC9AB2DC4
KeplerGolden.cs          7E98CD0BFABE143743E8B1FF0EC904FC1D68C0EF09A1999531F54172D9B90989
SolarSystemGolden.cs     D2637AE697E82ABA6A397CE679F450A15B3E92F578FC2BA020CB5F03CC5D460E
ShipMovingBody.cs        40E4729412C4D6E29B883C9FB93A8B94B9A3F450C142113C986F2E648E353BB0
Pythagorean.cs           3C127ABA546393BFC9E6D35E8619740B89B8DB99AE00A93BA3DD7D3A9E88D91B
ChebyshevDegree.cs       4228CE65461A8E4346D30E5B5E1E0561E7F2675644770550E6E8DAE06F300840
JetOracle.cs             C6CB9F9C5393E80B27016527B279953452D907BBDB2C4D7FD7D5F77996F6D61F
real_bake_export.json    2AC44A9C1CA0BB5D1DD7B1A6D456BEBC4F36F2D3B21981C0A4E8E400882C0F92
golden_real.json         6FA820F7C492500C2E7A8FDF3DBF93A2CB72323914104041FDEBFFFB50544A67
real_bake_export_1y.json 80AB6A8CE9307B5C473DF15FC0E8A0DC491700738D895863E398905B1478E8B9
golden_real_1y_hi.json   F5EB80C44B75AE98314098E6FD742EC16B21F16044521B47CCB39CBC06E94BFC
real_bake_export_10y.json E16DD9D2D685CA8034C94C6A8BEE16F9385F6D1B1D285523D9EE56E920193BFC
golden_real_10y_k28.json 72AB6A2D7B647321B15C1CD98B02868E03521ECFCB23A5916BEC92BF4A2B83A4
```
