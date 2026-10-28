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

`Test518_EphemerisExport` writes two exports with the same ship orbit:

- `real_bake_export.json` — 60-day bake, `Days=30`; input for the 1/30-day
  golden in `golden_real.json` (K=28, tol=1e-20, dps=40).
- `real_bake_export_1y.json` — 1.1-year bake, `Days=365`; input for the
  1-year golden in `golden_real_1y_hi.json` (K=28, tol=1e-20, dps=40).
- `real_bake_export_10y.json` — 10.1-year bake, `Days=3650`; input for the
  optional 10-year golden. The oracle run at K=20/tol=1e-12 is roughly
  5–7 hours with `gmpy2`; generate it with
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
- `golden_real_1y_hi.json` is certified: K=34, tol=1e-26, dps=50 gives
  `|dp|=1.3e-16 m`, `|dv|=1.81e-20 m/s` at day 365. The certified P/V are
  bit-identical to the pre-certify values.
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

```
Kepler3DMatrix.cs        8E238621D3AAF6527EF07325BB282497AB32BAA3B8903718F9F47E5AC39F34EC
KeplerGolden.cs          1A9CCF8E64438ABA19DB7805918D58ECE6572D976671A164E64954FB680BEE5A
SolarSystemGolden.cs     921BA77003809D6F3AACA6B69FAC81AE71F4E865693F79B1FFF944F20A79A118
ShipMovingBody.cs        A712643331C04DDD9C7BDF63B24978C8563137F7756DB85ED95EDA7BB3B6F87D
Pythagorean.cs           D53541721C40B3D8804F000F2F99F594883F71C9DAA72A1DAB5D000018B4CEAB
ChebyshevDegree.cs       CA29C927261C4D61A650D2F29CDC2F8DA5DD2890ED92CC703C7899F73889CBBF
JetOracle.cs             9C6A81FB600C7C31FCC335C5D73D16DA754F3F2CDAA2255208A0FF77BB973C28
real_bake_export.json    6FC2347D68FC3BCC29FA0D52AF2FBD352C76D5FDDDF991E6D938EAC88A87A027
golden_real.json         2A5206636425B04AE7B21EC9765F1FA4EF3723D98472DF347ED77BA3355402E8
real_bake_export_1y.json 43E944C19ECC5ADD7BDE51A2D3CC1B105C940F3AAA976BB9D9DF74F9D19760D1
golden_real_1y_hi.json   7582663FBAF275C2076B9D411DFB91B9833FC8683AD422FF3793C39BD6ACBC4E
real_bake_export_10y.json FF5E6C2DB5E001762E6AC5BA7768E5254AE1B2FBCBB9EB29EDADCAD47E1A80F0
```
