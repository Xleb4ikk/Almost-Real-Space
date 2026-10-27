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

## Real-system export

`real_bake_export.json` is written by `Test518_EphemerisExport`. It contains:
- `T0`, `Days`, `Central` (Earth), ship absolute `ShipP0`/`ShipV0`, and
  `RelP`/`RelV`;
- every body with `Parent`, `Mu`, `SubtreeMu`, `T0`, `SegmentLength`,
  `Degree`, `Segments`, and per-segment `Coeffs` (3 axes x (Degree+1)).
The coefficients are the same doubles the C# `BakedEphemeris` uses. This
is the input for the still-missing real-system mp oracle.

## SHA-256 of golden-bearing files

```
Kepler3DMatrix.cs        86754580E55D6583ABC83116CE776409A0B587EF69E361E4451F221C8EC58BB5
KeplerGolden.cs          1A9CCF8E64438ABA19DB7805918D58ECE6572D976671A164E64954FB680BEE5A
SolarSystemGolden.cs     921BA77003809D6F3AACA6B69FAC81AE71F4E865693F79B1FFF944F20A79A118
ShipMovingBody.cs        A712643331C04DDD9C7BDF63B24978C8563137F7756DB85ED95EDA7BB3B6F87D
Pythagorean.cs           D53541721C40B3D8804F000F2F99F594883F71C9DAA72A1DAB5D000018B4CEAB
ChebyshevDegree.cs       CA29C927261C4D61A650D2F29CDC2F8DA5DD2890ED92CC703C7899F73889CBBF
JetOracle.cs             9C6A81FB600C7C31FCC335C5D73D16DA754F3F2CDAA2255208A0FF77BB973C28
real_bake_export.json    6FC2347D68FC3BCC29FA0D52AF2FBD352C76D5FDDDF991E6D938EAC88A87A027
golden_real.json         2A5206636425B04AE7B21EC9765F1FA4EF3723D98472DF347ED77BA3355402E8
```
