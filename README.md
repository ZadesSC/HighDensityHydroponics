# HighDensityHydroponics

High Density Hydroponics mod for RimWorld.

## Building for RimWorld 1.7

Install the .NET 10 SDK, RimWorld 1.7, and the Harmony Workshop mod. On macOS with the default Steam library:

```sh
dotnet build Source/HighDensityHydro/HighDensityHydro.1.7.csproj -c Release
```

The project finds the game assemblies inside `RimWorld.app/Contents/MacOS` and Harmony under Workshop item `2009463077/1.7/Assemblies`. For another Steam library or platform, supply the actual assembly paths:

```sh
dotnet build Source/HighDensityHydro/HighDensityHydro.1.7.csproj -c Release \
  -p:RimWorldAssemblyDir="/path/to/game/assemblies" \
  -p:HarmonyDllPath="/path/to/Harmony/1.7/Assemblies/0Harmony.dll"
```

`RIMWORLD_DIR`, `RIMWORLD_ASSEMBLY_DIR`, and `HARMONY_DLL_PATH` environment variables are also supported. The game assembly directory must contain `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll`.

The 1.7 build writes `1.7/Assemblies/HighDensityHydro.dll`; it does not bundle game or Harmony libraries. `LoadFolders.xml` selects the matching version's assembly while sharing definitions, textures, patches, and translations. The original 1.6 DLL is preserved under `1.6/Assemblies`; the existing .NET Framework projects also build to that folder.

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
