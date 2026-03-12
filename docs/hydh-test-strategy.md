# High Density Hydroponics Harness Strategy

The runtime suite targets branch regressions in `Building_HighDensityHydro` and related compatibility/config paths.

Current in-game coverage:

- `hdh.spawn-smoke`
- `hdh.quantum-config`
- `hdh.quantum-scaling`
- `hdh.plant-selection-reset`
- `hdh.phase-sowing-growing`
- `hdh.phase-growing-harvest`
- `hdh.phase-harvest-sowing`
- `hdh.phase-harvest-regrow`
- `hdh.growing-empty-reset`
- `hdh.no-power-damage`
- `hdh.power-growth-gate`
- `hdh.save-load-roundtrip`
- `hdh.dbh-compatibility`

Static coverage comes from the harness manifest:

- expected package id
- supported RimWorld version
- Harmony dependency
- required HDH defs
- compiled assembly presence
