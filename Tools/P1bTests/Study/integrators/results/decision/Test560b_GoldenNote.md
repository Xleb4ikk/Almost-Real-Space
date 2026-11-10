# Test34 golden count: 121 → 119 (explicit note)

`Test34_GoldenState` compares Position/Velocity/Mass bit-for-bit and also counts
RHS calls. The state is **bit-for-bit identical** to the old golden
(P=147004123096.9961, 23171251.600864597, 0.00017879682654596798;
V=5629.710631722125, 25176.110817733676, 1.1635175826135667E-06; M=1000).

The diagnostic call count changed from 121 to 119 after HEAD `b06cac9`
("Orbits: propagate spacecraft in the dominant body's local frame"): the outer
`SpacecraftPhysics.Step` torque-snapshot path now evaluates the derivative once
per Step, so two old diagnostic gravity calls are no longer made in the absolute
path. No integration formula, step control or state changed.

The golden test was updated explicitly to `CALLS=119` with this comment in
`Tools/P1bTests/Tests/TestsOrbit.cs`; it was not silently fitted. The independent
Kepler anchor still passes (`relErr=1.02e-6 < 1e-4`).
