# Eclipse probes

Both are run through the Unity pipeline MCP (`run_script`), not the CLI. They live
outside `Assets/`, so they never become part of the build. With the Editor open on
`OutdoorsScene`:

```text
tools.unity.run_script { file: "Tools/Eclipse/EclipseScan.cs", entry: "EclipseScan.Main" }
tools.unity.run_script { file: "Tools/Eclipse/EclipseVerify.cs", entry: "EclipseVerify.Main" }
```

`EclipseScan` rebuilds the system from the open scene's `StarSystemAuthoring` (rails,
no baked ephemeris required) and writes every solar/lunar event plus the deepest `f`
on both bodies to the Unity console. `EclipseVerify` checks the new API: clear sky at
the epoch, solar `f = 0.846`, lunar `f = 0.326`, and a ship behind Terra at `f = 0`.

The numbers these produced for `OutdoorsScene` are in `Docs/Eclipses.md`.
