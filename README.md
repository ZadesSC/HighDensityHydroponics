# HighDensityHydroponics

High Density Hydroponics mod for RimWorld.

## Harness Tests

The runtime/static regression suite now lives in this repo under [Tests](/D:/Projects/rimworld_mods/HighDensityHydroponics/Tests) and runs through the harness reference in [External/ZadesRimWorldTestHarness](/D:/Projects/rimworld_mods/HighDensityHydroponics/External/ZadesRimWorldTestHarness).

Main files:

- manifest: [Tests/highdensityhydro.harness.json](/D:/Projects/rimworld_mods/HighDensityHydroponics/Tests/highdensityhydro.harness.json)
- runner script: [Tests/run-harness-tests.ps1](/D:/Projects/rimworld_mods/HighDensityHydroponics/Tests/run-harness-tests.ps1)
- suite mod: [Tests/Zades.HighDensityHydroponics.Tests](/D:/Projects/rimworld_mods/HighDensityHydroponics/Tests/Zades.HighDensityHydroponics.Tests)
- strategy doc: [docs/hydh-test-strategy.md](/D:/Projects/rimworld_mods/HighDensityHydroponics/docs/hydh-test-strategy.md)

Run everything from this repo:

```powershell
.\Tests\run-harness-tests.ps1 run-all
```

Useful variants:

```powershell
.\Tests\run-harness-tests.ps1 run-static
.\Tests\run-harness-tests.ps1 run-game
.\Tests\run-harness-tests.ps1 start-game
.\Tests\run-harness-tests.ps1 monitor-game
```
